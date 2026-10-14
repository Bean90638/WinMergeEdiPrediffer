using WinMergeEdiPrediffer.Core;
using System.Text.Json;
using System.Text.RegularExpressions;
var count=0;
void Check(bool test, string name) { if(!test) throw new Exception(name); Console.WriteLine("PASS "+name); count++; }
var patterns = new (string Pattern, string Path, bool Hit)[] {
 (@"\\850\\.*\.edi$", @"D:\Temp\EDI\850\Testing\111.edi", true),
 (@"\\850\\.*\.edi$", @"D:\Temp\EDI\860\111.edi", false),
 (@"(?i)\\EDI\d+;A\.edi$", @"D:\edi850;A.edi", true),
 (@"\\\w+\sfile\.edi$", @"D:\abc_123 file.edi", true),
 (@"\\[a-z\d]+\.edi$", @"D:\abc123.EDI", true),
 (@"^D:\\(old|new)\\.*\.edi$", @"D:\new\a.edi", true),
 (@"\\850\\(?!ignore).*\.edi$", @"D:\850\ignore.edi", false),
 (@"\bA\.edi$", @"D:\A.edi", true)
};
foreach(var (pattern,path,hit) in patterns) {
 var compiled=WinMergePathFilter.Compile(pattern);
 Check(compiled==compiled.ToUpperInvariant() && !compiled.Contains(';'), "filter survives host transformation "+pattern);
 Check(WinMergePathFilter.IsMatch(pattern,path)==hit,"preview result "+pattern);
}
foreach(var pattern in new[]{@"(?<name>A)",@"\p{L}+",@"(?-i)a",@"[A-Z-[X]]",@"(?<=A+)B"}) {
 bool rejected=false; try{WinMergePathFilter.Compile(pattern);}catch(InvalidOperationException){rejected=true;}
 Check(rejected,"incompatible syntax explicitly rejected "+pattern);
}
var root=Path.Combine(Path.GetTempPath(),"edi-export-"+Guid.NewGuid().ToString("N"));
var payload=Path.Combine(root,"payload");Directory.CreateDirectory(payload);
foreach(var ext in new[]{".bridge.dll",".comhost.dll",".dll",".runtimeconfig.json",".deps.json"}) File.WriteAllText(Path.Combine(payload,PluginExporter.PayloadName+ext),ext);
var config=new RuleConfiguration{Groups=[new(){Id="850",Name="850",FilePattern=@"\\850\\.*\.edi$",Rules=[new(){FindPattern="A",Replacement="B"}]},new(){Id="edi",Name="EDI",FilePattern=@"\.edi$"},new(){Id="fallback",Name="Other"}]};
var target=Path.Combine(root,"plugins");
ExportResult Export(bool running=false)=>PluginExporter.Export(config,Path.Combine(root,"source.json"),payload,target,"/tmp/manager",Path.Combine(root,"backups"),running);
var first=Export();
Check(first.Manifest.Groups.Count==3 && first.Manifest.OwnedFiles.Count==19,"three complete independent group entries");
foreach(var path in new[]{@"D:\850\a.edi",@"D:\860\a.edi",@"D:\other.txt"}){
 var hits=first.Manifest.Groups.Where(g=>Regex.IsMatch(path.ToUpperInvariant(),g.EffectiveFilter)).ToArray();
 Check(hits.Length==1 && hits[0].Id==RuleEngine.SelectGroup(config,path)?.Id,"overlap exclusions agree with preview "+path);
}
var profile=Path.Combine(target,first.Manifest.Groups[0].PluginName,PluginExporter.PayloadName+".profile.json");
var reader=new LiveProfileReader(profile);
Check(RuleEngine.Apply(reader.Current.Group,"A")=="B","initial per-group shared rules");
config.Groups[0].Rules[0].Replacement="C";
var hot=Export(true);
Check(hot.RulesOnlyUpdate&&!hot.ReloadPluginsRequired && RuleEngine.Apply(reader.Current.Group,"A")=="C","already loaded profile reads hot content rules");
config.Groups.Reverse();
var structural=Export(true);
Check(structural.ReloadPluginsRequired&&structural.RulesOnlyUpdate,"live order update requests reload without replacing DLLs");
var added=new RuleGroup{Id="added",Name="Added",FilePattern=@"\\added\\",Rules=[new(){FindPattern="A",Replacement="D"}]};
config.Groups.Insert(0,added);var addition=Export(true);
Check(addition.ReloadPluginsRequired&&!addition.RulesOnlyUpdate&&addition.Manifest.Groups.Count==4,"live addition installs only a new entry");
var newReader=new LiveProfileReader(Path.Combine(target,addition.Manifest.Groups[0].PluginName,PluginExporter.PayloadName+".profile.json"));
Check(RuleEngine.Apply(newReader.Current.Group,"A")=="D","new entry reads its own group");
config.Groups.Remove(added);var removal=Export(true);
Check(removal.ReloadPluginsRequired&&removal.Manifest.RetiredGroups.Count==1&&!newReader.Current.Group.Enabled,"removed entry immediately disabled without DLL deletion");
config.Groups.Insert(0,added);var readd=Export(true);
Check(readd.Manifest.RetiredGroups.Count==0&&readd.RulesOnlyUpdate&&newReader.Current.Group.Enabled,"readded group reuses retained entry");
config.Groups.Clear();var empty=Export(true);
Check(empty.Manifest.Groups.Count==0&&empty.Manifest.RetiredGroups.Count==4&&Directory.GetFiles(target,"*.dll").Length==4,"all removed entries remain inert while loaded");
var cleanup=Export();
Check(cleanup.Manifest.RetiredGroups.Count==0&&Directory.GetFiles(target,"*.dll").Length==0,"closed-host maintenance cleans retired entries");
Console.WriteLine(count+" checks passed");
