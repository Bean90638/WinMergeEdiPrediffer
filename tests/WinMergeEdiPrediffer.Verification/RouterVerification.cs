using System.Text.Json;
using WinMergeEdiPrediffer.Core;

namespace WinMergeEdiPrediffer.Verification;

internal static class RouterVerification
{
    public static void Run(RuleConfiguration configuration, string payload, Action<bool, string> check)
    {
        bool Fails(Action action, string message)
        {
            try { action(); return false; }
            catch (InvalidOperationException ex) { return ex.Message.Contains(message); }
        }
        const string path850 = @"D:\EDI\old\A_850.edi";
        const string path860 = @"D:\EDI\new\A_860.edi";
        var paths = new RuleConfiguration { Groups = [new() { FilePattern = @"(?i)\\EDI\d+;A\.edi$" }] };
        ConfigurationValidator.Validate(paths);
        check(RuleEngine.SelectGroup(paths, @"D:\edi850;A.edi") is not null, ".NET path escapes, inline options and semicolons");
        var root = Path.Combine(Directory.GetCurrentDirectory(), "work", "com-verification-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "user-file.txt"), "preserve me");
        var source = Path.Combine(root, "source.json");
        ExportResult Export(bool inUse = false) => PluginExporter.Export(configuration, source, payload, root, Environment.ProcessPath!, Path.Combine(root, "backups"), inUse);
        var exported = Export();
        const string legacyName = "EdiPrediff_0000_legacy";
        foreach (var extension in new[] { ".dll", ".runtimeconfig.json", ".deps.json" })
        {
            File.Copy(Path.Combine(payload, PluginExporter.PayloadName + extension), Path.Combine(root, legacyName + extension));
            exported.Manifest.OwnedFiles.Add(legacyName + extension);
        }
        exported.Manifest.PathRouting = false;
        var manifestPath = Path.Combine(root, PluginExporter.ManifestFilename);
        File.WriteAllText(manifestPath, JsonSerializer.Serialize(exported.Manifest, ConfigurationJson.Options));
        exported = Export();
        check(exported.BackupDirectory is not null && Directory.Exists(exported.BackupDirectory), "migration creates recoverable backup");
        check(File.ReadAllBytes(Path.Combine(exported.BackupDirectory!, legacyName + ".dll")).SequenceEqual(File.ReadAllBytes(Path.Combine(payload, PluginExporter.PayloadName + ".dll"))), "legacy managed locator backed up before migration");
        check(!new[] { ".dll", ".runtimeconfig.json", ".deps.json" }.Any(e => File.Exists(Path.Combine(root, legacyName + e))), "legacy root managed DLL and runtime files removed");
        check(File.ReadAllText(Path.Combine(root, "user-file.txt")) == "preserve me", "export preserves unrelated user files");
        check(Directory.GetFiles(root, "*.dll").Select(Path.GetFileName).SequenceEqual(new[] { PluginExporter.RouterName + ".comhost.dll" }), "one fixed COM entry point for all groups");
        check(exported.Manifest.PathRouting && exported.Manifest.Groups.Count == 3 && exported.Manifest.Groups.All(g => g.PluginName == PluginExporter.RouterName), "logical groups share one routing deployment");
        check(exported.Manifest.OwnedFiles.Count == 7 && exported.Manifest.OwnedFiles.All(f => File.Exists(Path.Combine(root, f))), "complete standard .NET COM deployment and shared snapshot");
        File.WriteAllText(Path.Combine(payload, PluginExporter.PayloadName + ".profile.json"), JsonSerializer.Serialize(new PluginProfile { Group = configuration.Groups[0] }, ConfigurationJson.Options));
        using (var baseline = new NativeComClient(Path.Combine(payload, PluginExporter.PayloadName + ".comhost.dll")))
        {
            check((string?)baseline.Property("PluginEvent") == "BUFFER_PREDIFF", "unrenamed COM DLL and legacy profile load");
            check(baseline.Prediff("ST*850*0001~", wrapVariants: true).Text == "ST*850*<IGNORE>~", "legacy profile works without path variables");
        }
        using var client = new NativeComClient(Path.Combine(root, PluginExporter.RouterName + ".comhost.dll"));
        check(client.MemberNames().Contains("PrediffBufferW") && client.MemberNames().Contains("PluginVariables"), "embedded TLB exposes prediffer and path property");
        check((string?)client.Property("PluginEvent") == "BUFFER_PREDIFF", "COM plugin event");
        check((bool?)client.Property("PluginIsAutomatic") == true && (string?)client.Property("PluginFileFilters") == ".*", "fixed automatic metadata for any file");
        var description = (string?)client.Property("PluginDescription");
        var result850 = client.Prediff("ST*850*0001~SE*16*0001~", fullPath: path850);
        check(result850.Handled && result850.Changed && result850.Size == result850.Text.Length && result850.Text == "ST*850*<IGNORE>~SE*16*0001~", "path setter and native BYREF BSTR/size/bool round trip");
        check(client.Prediff("ST*850*0001~SE*16*0001~", wrapVariants: true, fullPath: path850) == result850, "WinMerge BYREF VARIANT wrapper and BSTR ownership");
        check(client.Prediff("ST*860*0001~", fullPath: path860).Text == "ST*860*<860>~", "same COM object routes next file to a different group");
        var unchanged = client.Prediff("no EDI content\r\n", fullPath: path850);
        check(unchanged.Handled && !unchanged.Changed && unchanged.Size == unchanged.Text.Length, "native unchanged content");
        check(client.Prediff("no EDI content\r\n", wrapVariants: true, fullPath: path850) == unchanged, "WinMerge wrapped unchanged content");
        check(!client.Prediff("ST*850*0001~", fullPath: @"D:\random.txt").Changed, "fallback with no rules preserves content");
        check(Fails(() => client.Prediff("ST*850*0001~"), "原始檔案路徑"), "path is consumed once and never reused for a different file");
        check(Fails(() => client.Prediff("ST*850*0001~", fullPath: "relative.edi"), "原始檔案路徑"), "missing full path reports COM error");
        client.SetProperty("PluginVariables", path850);
        check(Fails(() => client.SetProperty("PluginVariables", 42), "VARIANT"), "invalid path setter reports an explicit type error");
        check(Fails(() => client.Prediff("ST*850*0001~"), "原始檔案路徑"), "failed setter clears preceding valid path");
        var staticPaths = exported.Manifest.OwnedFiles.Where(f => f != PluginExporter.LiveRulesFilename).Select(f => Path.Combine(root, f)).ToArray();
        var before = staticPaths.ToDictionary(f => f, f => (Bytes: File.ReadAllBytes(f), Modified: File.GetLastWriteTimeUtc(f)));
        var locks = staticPaths.Select(f => File.Open(f, FileMode.Open, FileAccess.Read, FileShare.Read)).ToArray();
        try
        {
            ExportResult ApplyLive() => Export(true);
            void Hot(ExportResult change, string name) => check(change.RulesOnlyUpdate && !change.ReloadPluginsRequired, name);
            configuration.Groups[0].Rules[0].Replacement = "$1$2<LIVE>";
            Hot(ApplyLive(), "rules update with deployed binaries and profile locked");
            check(client.Prediff("ST*850*0001~", wrapVariants: true, fullPath: path850).Text == "ST*850*<LIVE>~", "already-loaded COM object reads new rules");
            configuration.Groups[0].Enabled = false;
            Hot(ApplyLive(), "disable group without reload");
            check(!client.Prediff("ST*850*0001~", fullPath: path850).Changed, "disabled group immediately falls back");
            configuration.Groups[0].Enabled = true;
            Hot(ApplyLive(), "reenable group without reload");
            check(client.Prediff("ST*850*0001~", fullPath: path850).Text.Contains("<LIVE>"), "reenabled group immediately processes content");
            configuration.Groups[0].FilePattern = @"\\renamed\.edi$";
            Hot(ApplyLive(), "path changes without reload");
            check(!client.Prediff("ST*850*0001~", fullPath: path850).Changed && client.Prediff("ST*850*0001~", fullPath: @"D:\renamed.edi").Text.Contains("<LIVE>"), "live path changes affect selection");
            configuration.Groups[0].FilePattern = @"\\A_850\.edi$";
            var competitor = new RuleGroup { Id = "competitor", Name = "competitor", FilePattern = @"\\A_850\.edi$", Rules = [new() { FindPattern = "ST", Replacement = "TEST" }] };
            configuration.Groups.Insert(0, competitor);
            Hot(ApplyLive(), "add overlapping group without loading another DLL");
            check(client.Prediff("ST*850*0001~", fullPath: path850).Text == "TEST*850*0001~", "first displayed matching group wins without merging rules");
            configuration.Groups.Remove(competitor); configuration.Groups.Insert(1, competitor);
            ApplyLive();
            check(client.Prediff("ST*850*0001~", fullPath: path850).Text.Contains("<LIVE>"), "moving group down immediately uses displayed order");
            configuration.Groups.Remove(competitor); configuration.Groups.Insert(0, competitor);
            Hot(ApplyLive(), "display order changes without reload");
            check(client.Prediff("ST*850*0001~", fullPath: path850).Text == "TEST*850*0001~", "moving group back up immediately changes selection");
            configuration.Groups.Remove(competitor);
            Hot(ApplyLive(), "delete group without deleting a DLL");
            var test = new RuleGroup { Id = "Test", Name = "Test", FilePattern = @"\\Test\.edi$", Rules =
                [new() { FindPattern = "X", Replacement = "Y" }, new() { FindPattern = "Y", Replacement = "Z" }] };
            configuration.Groups.Insert(0, test);
            Hot(ApplyLive(), "add multi-rule group without loading another DLL");
            check(client.Prediff("X\r\n", fullPath: @"D:\Test.edi").Text == "Z\r\n" && Directory.GetFiles(root, "*.dll").Length == 1, "new group executes ordered rules through existing object");
            configuration.Groups.Remove(test);
            var removed = ApplyLive();
            Hot(removed, "remove multi-rule group without reload");
            check(removed.Manifest.RetiredGroups.Count == 0 && !client.Prediff("X", fullPath: @"D:\Test.edi").Changed, "deleted group stops matching without obsolete DLL entries");
            configuration.Groups.Insert(0, test);
            Hot(ApplyLive(), "re-add group through existing deployment");
            check(client.Prediff("X", fullPath: @"D:\Test.edi").Text == "Z", "re-added group works with original COM object");
            var snapshotPath = Path.Combine(root, PluginExporter.LiveRulesFilename);
            var validSnapshot = File.ReadAllText(snapshotPath);
            test.Rules[0].FindPattern = "[";
            check(Fails(() => ApplyLive(), "Test") && File.ReadAllText(snapshotPath) == validSnapshot && client.Prediff("X", fullPath: @"D:\Test.edi").Text == "Z", "invalid edit leaves last applied rules usable");
            test.Rules[0].FindPattern = "X";
            test.Rules[0].Replacement = "UNAPPLIED";
            File.WriteAllText(source, ConfigurationJson.Serialize(configuration));
            check(client.Prediff("X", fullPath: @"D:\Test.edi").Text == "Z", "saving source JSON alone does not publish edits");
            test.Rules[0].Replacement = "Y";
            PluginExporter.AtomicWrite(snapshotPath, "{");
            check(Fails(() => client.Prediff("X", fullPath: @"D:\Test.edi"), "COM PluginVariables"), "corrupt live rules report COM error");
            PluginExporter.AtomicWrite(snapshotPath, validSnapshot);
            check(Fails(() => client.Prediff("X"), "原始檔案路徑"), "corrupt snapshot clears path even on setter failure");
            check(client.Prediff("X", fullPath: @"D:\Test.edi").Text == "Z", "COM object recovers after valid rules restored");
            foreach (var group in configuration.Groups) group.Enabled = false;
            Hot(ApplyLive(), "all groups disabled without unloading");
            check(!client.Prediff("X", fullPath: @"D:\Test.edi").Changed && (bool?)client.Property("PluginIsAutomatic") == true && (string?)client.Property("PluginFileFilters") == ".*", "all disabled keeps constant metadata and content");
            foreach (var group in configuration.Groups) group.Enabled = true;
            ApplyLive();
            check((string?)client.Property("PluginDescription") == description, "description remains stable after group edits");
            var manifestText = File.ReadAllText(manifestPath);
            var tampered = JsonSerializer.Deserialize<ExportManifest>(manifestText, ConfigurationJson.Options)!;
            tampered.OwnedFiles.Add("../user-file.txt");
            File.WriteAllText(manifestPath, JsonSerializer.Serialize(tampered, ConfigurationJson.Options));
            check(Fails(() => ApplyLive(), "不屬於本專案") && File.ReadAllText(Path.Combine(root, "user-file.txt")) == "preserve me", "manifest cannot modify unrelated or escaping paths");
            File.WriteAllText(manifestPath, manifestText);
            var changedPayload = Path.Combine(root, "changed-payload");
            Directory.CreateDirectory(changedPayload);
            foreach (var file in Directory.EnumerateFiles(payload)) File.Copy(file, Path.Combine(changedPayload, Path.GetFileName(file)));
            File.AppendAllText(Path.Combine(changedPayload, PluginExporter.PayloadName + ".deps.json"), " ");
            var beforeUpgrade = File.ReadAllText(snapshotPath);
            check(Fails(() => PluginExporter.Export(configuration, source, changedPayload, root, Environment.ProcessPath!, Path.Combine(root, "backups"), true), "升級或修復") && File.ReadAllText(snapshotPath) == beforeUpgrade, "binary upgrade blocked before modifying live deployment");
            check(before.All(pair => File.ReadAllBytes(pair.Key).SequenceEqual(pair.Value.Bytes) && File.GetLastWriteTimeUtc(pair.Key) == pair.Value.Modified), "all edits preserve static deployment bytes and timestamps");
        }
        finally { foreach (var file in locks) file.Dispose(); }
    }
}
