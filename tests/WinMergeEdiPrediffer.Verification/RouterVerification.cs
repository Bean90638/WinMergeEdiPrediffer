using System.Text.Json;
using System.Text.RegularExpressions;
using WinMergeEdiPrediffer.Core;

namespace WinMergeEdiPrediffer.Verification;

internal static class RouterVerification
{
    public static void Run(RuleConfiguration configuration, string payload, Action<bool, string> check)
    {
        var root = Path.GetFullPath(Path.Combine("work", "group-com-" + Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "user-file.txt"), "preserve me");
        configuration = ConfigurationJson.Deserialize(ConfigurationJson.Serialize(configuration));
        ExportResult Export(bool inUse = false) => PluginExporter.Export(configuration, Path.Combine(root, "source.json"),
            payload, root, Environment.ProcessPath!, Path.Combine(root, "backups"), inUse);
        var installed = Export();
        check(!installed.Manifest.PathRouting && installed.Manifest.Groups.Select(g => g.PluginName).Distinct().Count() == 3,
            "official host receives a separate entry point per enabled group");
        check(installed.Manifest.OwnedFiles.Count == 19 && installed.Manifest.OwnedFiles.All(f => File.Exists(Path.Combine(root, f))),
            "each entry has complete private COM payload and shared rules");
        var first = installed.Manifest.Groups[0];
        using (var client = new NativeComClient(Path.Combine(root, first.PluginName + ".comhost.dll")))
        using (var second = new NativeComClient(Path.Combine(root, installed.Manifest.Groups[1].PluginName + ".comhost.dll")))
        {
            check(client.MemberNames().Contains("PluginVariables") && !client.MemberNames().Contains("PluginOriginalFilePath"),
                "TLB uses official ABI without a custom original path property");
            check((string?)client.Property("PluginFileFilters") == first.EffectiveFilter && (bool?)client.Property("PluginIsAutomatic") == true,
                "COM advertises effective original path filter");
            var result = client.Prediff("ST*850*0001~", wrapVariants: true, processingPath: @"C:\Temp\t0_wmdoc.tmp");
            check(result.Handled && result.Changed && result.Size == result.Text.Length && result.Text == "ST*850*<IGNORE>~",
                "official temporary path and native wrapped BYREF buffer apply selected group");
            check(second.Prediff("ST*860*0001~").Text == "ST*860*<860>~", "separate COM payload does not reuse another group's profile");
            check(client.Prediff("ST*850*0001~").Text.Contains("<IGNORE>"), "selected group needs no path setter");
            var staticPaths = installed.Manifest.OwnedFiles.Where(f => f != PluginExporter.LiveRulesFilename).Select(f => Path.Combine(root, f)).ToArray();
            var bytes = staticPaths.ToDictionary(f => f, File.ReadAllBytes);
            var locks = staticPaths.Select(f => File.Open(f, FileMode.Open, FileAccess.Read, FileShare.Read)).ToArray();
            try
            {
                configuration.Groups[0].Rules[0].Replacement = "$1$2<LIVE>";
                var live = Export(true);
                check(live.RulesOnlyUpdate && !live.ReloadPluginsRequired && client.Prediff("ST*850*0001~").Text.Contains("<LIVE>"),
                    "existing official COM instance reads hot content rules while binaries are locked");
                configuration.Groups[0].FilePattern = @"\\renamed\.edi$";
                var paths = Export(true);
                check(paths.RulesOnlyUpdate && paths.ReloadPluginsRequired &&
                    ((string?)client.Property("PluginFileFilters"))!.Contains("RENAMED"),
                    "path change publishes dynamic filter metadata without overwriting locked DLLs");
                configuration.Groups[0].Enabled = false;
                check(Export(true).ReloadPluginsRequired && !client.Prediff("ST*850*0001~").Changed &&
                    (bool?)client.Property("PluginIsAutomatic") == false,
                    "disabled group immediately stops processing through an existing object");
                configuration.Groups[0].Enabled = true;
                var reenabled = Export(true);
                check(reenabled.ReloadPluginsRequired && reenabled.Manifest.RetiredGroups.Count == 0 &&
                    client.Prediff("ST*850*0001~").Text.Contains("<LIVE>"), "reenabled group reuses retained entry without DLL replacement");
                var added = new RuleGroup { Id = "new", Name = "New", FilePattern = @"\\new\\", Rules =
                    [new() { FindPattern = "X", Replacement = "Z" }] };
                configuration.Groups.Insert(0, added);
                var addition = Export(true);
                check(!addition.RulesOnlyUpdate && addition.ReloadPluginsRequired, "new group adds an entry without touching loaded payloads");
                using (var addedClient = new NativeComClient(Path.Combine(root, addition.Manifest.Groups[0].PluginName + ".comhost.dll")))
                {
                    check(addedClient.Prediff("X").Text == "Z", "new entry is immediately loadable in the same native process");
                    configuration.Groups.Remove(added);
                    check(Export(true).Manifest.RetiredGroups.Any(g => g.Id == "new") && !addedClient.Prediff("X").Changed,
                        "removed loaded entry is retained safely and cannot apply stale rules");
                    configuration.Groups.Insert(0, added);
                    check(Export(true).Manifest.RetiredGroups.Count == 0 && addedClient.Prediff("X").Text == "Z",
                        "removed group can be re-added without overwriting a locked host");
                }
                check(bytes.All(pair => File.ReadAllBytes(pair.Key).SequenceEqual(pair.Value)), "hot update never overwrites loaded binaries or bootstrap profiles");
            }
            finally { foreach (var file in locks) file.Dispose(); }
        }
        var migrationRoot = root + "-migration";
        ExportResult Migrate() => PluginExporter.Export(configuration, Path.Combine(migrationRoot, "source.json"),
            payload, migrationRoot, Environment.ProcessPath!, Path.Combine(migrationRoot, "backups"));
        Migrate();
        configuration.Groups[0].Name += " renamed";
        var next = Migrate();
        check(next.RulesOnlyUpdate && next.ReloadPluginsRequired && next.BackupDirectory is not null,
            "metadata change backs up shared configuration and requests an in-process reload");
        check(File.ReadAllText(Path.Combine(root, "user-file.txt")) == "preserve me", "deployment preserves unrelated files");
        // Exercise migration from the former custom-host Router deployment.
        var legacy = PluginExporter.RouterName + ".comhost.dll";
        File.Copy(Path.Combine(payload, PluginExporter.PayloadName + ".bridge.dll"), Path.Combine(migrationRoot, legacy));
        next.Manifest.PathRouting = true;
        next.Manifest.OwnedFiles.Add(legacy);
        File.WriteAllText(Path.Combine(migrationRoot, PluginExporter.ManifestFilename), JsonSerializer.Serialize(next.Manifest, ConfigurationJson.Options));
        var migrated = Migrate();
        check(!File.Exists(Path.Combine(migrationRoot, legacy)) && File.Exists(Path.Combine(migrated.BackupDirectory!, legacy)),
            "custom Router entry is backed up and retired during migration");
    }
}
