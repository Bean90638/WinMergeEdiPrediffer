using System.Text.Json;
using System.Text.RegularExpressions;
using WinMergeEdiPrediffer.Core;
using WinMergeEdiPrediffer.Verification;

try
{
var checks = new List<string>();
void Check(bool condition, string name)
{
    if (!condition) throw new InvalidOperationException("FAILED: " + name);
    checks.Add(name); Console.WriteLine("PASS " + name);
}
var configuration = new RuleConfiguration
{
    Groups =
    [
        new() { Id = "fallback", IsDefault = true, Priority = 0, Name = "Default" },
        new() { Id = "850", Priority = 10, FilePattern = @"\\A_850\.edi$", Name = "850", Rules =
        [new() { Name = "ST02", FindPattern = @"(^|~)(ST\*[^*~\r\n]+\*)[^*~\r\n]*", Replacement = "$1$2<IGNORE>" }] },
        new() { Id = "860", Priority = 20, FilePattern = @"\\A_860\.edi$", Name = "860", Rules =
        [new() { Name = "ST02", FindPattern = @"(^|~)(ST\*[^*~\r\n]+\*)[^*~\r\n]*", Replacement = "$1$2<860>" }] }
    ]
};
ConfigurationValidator.Validate(configuration);
Check(RuleEngine.SelectGroup(configuration, @"D:\EDI\old\A_850.edi")?.Id == "850", "specific group before fallback");
Check(RuleEngine.SelectGroup(configuration, @"D:\EDI\new\a_860.EDI")?.Id == "860", "case insensitive original full path");
Check(RuleEngine.SelectGroup(configuration, @"D:\other.txt")?.Id == "fallback", "default matches any file");
Check(RuleEngine.Apply(configuration.Groups[1], "ST*850*0001~SE*16*0001~") == "ST*850*<IGNORE>~SE*16*0001~", "ignore ST02 and preserve ST01/SE02");
Check(RuleEngine.Apply(configuration.Groups[1], "ST*860*0001~") == "ST*860*<IGNORE>~", "ST01 remains visible");
var sequence = new RuleGroup { Rules =
[
    new() { FindPattern = "A", Replacement = "B" }, new() { FindPattern = "B", Replacement = "C" },
    new() { Enabled = false, FindPattern = "C", Replacement = "D" }
] };
Check(RuleEngine.Apply(sequence, "A\r\nA\n") == "C\r\nC\n", "ordered multiple rules, disabled rule, line breaks preserved");
sequence.Rules = [new() { UseRegularExpression = false, IgnoreCase = true, FindPattern = "a*", Replacement = "$1" }];
Check(RuleEngine.Apply(sequence, "A*") == "$1", "literal replacement and case switch");
sequence.Rules = [new() { FindPattern = "A", Replacement = "A\n" }];
var rejectedLineChange = false;
try { RuleEngine.Apply(sequence, "A"); }
catch (InvalidOperationException) { rejectedLineChange = true; }
Check(rejectedLineChange, "reject changes to line structure");
sequence.Rules = [new() { FindPattern = "^A$", Replacement = "B" }];
Check(RuleEngine.Apply(sequence, "A\r\nA\nA\r") == "B\r\nB\nB\r", "line anchors apply independently and preserve CRLF/LF/CR");
sequence.Rules = [new() { FindPattern = @"A\r?\nA", Replacement = "B" }];
Check(RuleEngine.Apply(sequence, "A\r\nA") == "A\r\nA", "rules cannot merge separate lines");
sequence.Rules = [new() { Name = "first", FindPattern = "A", Replacement = "B" }, new() { Name = "second", FindPattern = "B", Replacement = "C" }];
var steps = RuleEngine.Preview(sequence, "A");
Check(steps.Count == 2 && steps[0].Text == "B" && steps[1].Text == "C" && steps.All(s => s.Changed), "preview shows each ordered rule result");
configuration.Groups[1].Enabled = false;
Check(RuleEngine.SelectGroup(configuration, @"D:\EDI\old\A_850.edi")?.IsDefault == true, "disabled specific group falls back");
configuration.Groups[1].Enabled = true;
var directoryGroups = new RuleConfiguration { Groups =
[
    new() { Name = "old directory", Priority = 10, FilePattern = @"\\old\\A\.edi$" },
    new() { Name = "new directory", Priority = 20, FilePattern = @"\\new\\A\.edi$" }
] };
Check(RuleEngine.SelectGroup(directoryGroups, @"D:\EDI\old\A.edi")?.Name == "old directory" && RuleEngine.SelectGroup(directoryGroups, @"D:\EDI\new\A.edi")?.Name == "new directory", "same filename selects by original directory");
var invalid = new RuleConfiguration { Groups = [new() { Name = "invalid group", Rules = [new() { Name = "invalid rule", FindPattern = "[" }] }] };
var errorHasLocation = false;
try { ConfigurationValidator.Validate(invalid); }
catch (InvalidOperationException ex) { errorHasLocation = ex.Message.Contains("invalid group / invalid rule"); }
Check(errorHasLocation, "invalid regex identifies group and rule");
Check(ConfigurationJson.Deserialize(ConfigurationJson.Serialize(configuration)).Groups.Count == 3, "JSON round trip");

var payloadArgument = Array.IndexOf(args, "--com-payload");
if (payloadArgument >= 0)
{
    var payload = Path.GetFullPath(args[payloadArgument + 1]);
    DeploymentVerification.Run(configuration, payload, Check);
    RouterVerification.Run(configuration, payload, Check);
}
var reportArgument = Array.IndexOf(args, "--report");
if (reportArgument >= 0)
    File.WriteAllText(Path.GetFullPath(args[reportArgument + 1]), JsonSerializer.Serialize(new { passed = checks.Count, checks }, ConfigurationJson.Options));
Console.WriteLine($"{checks.Count} checks passed.");
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex);
    Environment.ExitCode = 1;
}
