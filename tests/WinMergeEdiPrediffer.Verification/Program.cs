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
        new() { Id = "850", FilePattern = @"\\A_850\.edi$", Name = "850", Rules =
        [new() { Name = "ST02", FindPattern = @"(^|~)(ST\*[^*~\r\n]+\*)[^*~\r\n]*", Replacement = "$1$2<IGNORE>" }] },
        new() { Id = "860", FilePattern = @"\\A_860\.edi$", Name = "860", Rules =
        [new() { Name = "ST02", FindPattern = @"(^|~)(ST\*[^*~\r\n]+\*)[^*~\r\n]*", Replacement = "$1$2<860>" }] },
        new() { Id = "fallback", Name = "Default" }
    ]
};
ConfigurationValidator.Validate(configuration);
Check(RuleEngine.SelectGroup(configuration, @"D:\EDI\old\A_850.edi")?.Id == "850", "specific group before fallback");
Check(RuleEngine.SelectGroup(configuration, @"D:\EDI\new\a_860.EDI")?.Id == "860", "case insensitive original full path");
Check(RuleEngine.SelectGroup(configuration, @"D:\other.txt")?.Id == "fallback", "default matches any file");
Check(RuleEngine.Apply(configuration.Groups[0], "ST*850*0001~SE*16*0001~") == "ST*850*<IGNORE>~SE*16*0001~", "ignore ST02 and preserve ST01/SE02");
Check(RuleEngine.Apply(configuration.Groups[0], "ST*860*0001~") == "ST*860*<IGNORE>~", "ST01 remains visible");
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
configuration.Groups[0].Enabled = false;
Check(RuleEngine.SelectGroup(configuration, @"D:\EDI\old\A_850.edi")?.Id == "fallback", "disabled specific group falls back");
configuration.Groups[0].Enabled = true;
var directoryGroups = new RuleConfiguration { Groups =
[
    new() { Name = "old directory", FilePattern = @"\\old\\A\.edi$" },
    new() { Name = "new directory", FilePattern = @"\\new\\A\.edi$" }
] };
Check(RuleEngine.SelectGroup(directoryGroups, @"D:\EDI\old\A.edi")?.Name == "old directory" && RuleEngine.SelectGroup(directoryGroups, @"D:\EDI\new\A.edi")?.Name == "new directory", "same filename selects by original directory");
var invalid = new RuleConfiguration { Groups = [new() { Name = "invalid group", Rules = [new() { Name = "invalid rule", FindPattern = "[" }] }] };
var errorHasLocation = false;
try { ConfigurationValidator.Validate(invalid); }
catch (InvalidOperationException ex) { errorHasLocation = ex.Message.Contains("invalid group / invalid rule"); }
Check(errorHasLocation, "invalid regex identifies group and rule");
Check(ConfigurationJson.Deserialize(ConfigurationJson.Serialize(configuration)).Groups.Count == 3, "JSON round trip");
var fallback = configuration.Groups[2];
configuration.Groups.Remove(fallback); configuration.Groups.Insert(0, fallback);
Check(RuleEngine.SelectGroup(configuration, @"D:\EDI\old\A_850.edi")?.Id == "fallback", "catch-all follows displayed order without special treatment");
configuration.Groups.Remove(fallback); configuration.Groups.Add(fallback);
const string legacyJson = """
    {"schemaVersion":1,"groups":[
      {"id":"fallback","name":"Fallback","isDefault":true,"priority":0},
      {"id":"last","name":"Last","priority":20},
      {"id":"first","name":"First","priority":10},
      {"id":"tie","name":"Tie","priority":10},
      {"id":"disabled","name":"Disabled","priority":5,"enabled":false}
    ]}
    """;
var migrated = ConfigurationJson.Deserialize(legacyJson);
Check(migrated.SchemaVersion == 2 && migrated.Groups.Select(g => g.Id).SequenceEqual(["disabled", "first", "tie", "last", "fallback"]), "legacy priority and fallback migrate once, including disabled and tied groups");
Check(RuleEngine.SelectGroup(migrated, @"D:\test.edi")?.Id == "first", "migrated legacy configuration preserves first enabled match");
migrated.Groups.Reverse();
var savedOrder = ConfigurationJson.Serialize(migrated);
Check(!savedOrder.Contains("priority") && !savedOrder.Contains("isDefault") && ConfigurationJson.Deserialize(savedOrder).Groups.Select(g => g.Id).SequenceEqual(migrated.Groups.Select(g => g.Id)), "saved display order round trips without legacy fields or reordering");
var legacyLive = JsonSerializer.Deserialize<LivePluginConfiguration>("{\"configuration\":" + legacyJson + "}", ConfigurationJson.Options)!;
Check(legacyLive.Configuration.Groups[1].Id == "first" && legacyLive.Configuration.SchemaVersion == 2, "nested legacy COM configuration uses identical migration");
ManagerVerification.Run(Check);

var payloadArgument = Array.IndexOf(args, "--com-payload");
if (payloadArgument >= 0)
{
    var payload = Path.GetFullPath(args[payloadArgument + 1]);
    DeploymentVerification.Run(configuration, payload, Check);
    RouterVerification.Run(configuration, payload, Check);
    OriginalPathVerification.Run(payload, Check);
}
var winMergeArgument = Array.IndexOf(args, "--winmerge-path");
if (winMergeArgument >= 0)
{
    if (payloadArgument < 0) throw new ArgumentException("--winmerge-path requires --com-payload.");
    var fixtureArgument = Array.IndexOf(args, "--edi-fixtures");
    WinMergeIntegrationVerification.Run(Path.GetFullPath(args[winMergeArgument + 1]),
        Path.GetFullPath(args[payloadArgument + 1]), Check,
        fixtureArgument >= 0 ? Path.GetFullPath(args[fixtureArgument + 1]) : null);
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
