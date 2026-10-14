using WinMergeEdiPrediffer.Core;

namespace WinMergeEdiPrediffer.Verification;

internal static class DeploymentVerification
{
    public static void Run(RuleConfiguration configuration, string payload, Action<bool, string> check)
    {
        var root = Path.GetFullPath(Path.Combine("work", "deployment-verification-" + Guid.NewGuid().ToString("N")));
        var destination = Path.Combine(root, "plugins");
        var source = Path.Combine(root, "profiles.json");
        var backups = Path.Combine(root, "backups");
        var manager = Environment.ProcessPath!;
        var initial = PluginExporter.Export(configuration, source, payload, destination, manager, backups);
        var missing = Path.Combine(destination, initial.Manifest.Groups[0].PluginName, PluginExporter.PayloadName + ".profile.json");
        File.Delete(missing); // 模擬安裝配套遺失，強制進入完整修復流程。
        var manifest = Path.Combine(destination, PluginExporter.ManifestFilename);
        var before = initial.Manifest.OwnedFiles.Select(file => Path.Combine(destination, file))
            .Where(File.Exists).Append(manifest).ToDictionary(file => file, File.ReadAllBytes);
        var updated = ConfigurationJson.Deserialize(ConfigurationJson.Serialize(configuration));
        updated.Groups[0].Rules[0].Replacement = "$1$2<UPDATED>";
        var attributes = File.GetAttributes(manifest);
        var failed = false;
        try
        {
            File.SetAttributes(manifest, attributes | FileAttributes.ReadOnly);
            try { PluginExporter.Export(updated, source, payload, destination, manager, backups); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { failed = true; }
        }
        finally { File.SetAttributes(manifest, attributes); }
        check(failed && before.All(file => File.Exists(file.Key) && File.ReadAllBytes(file.Key).SequenceEqual(file.Value)),
            "failed full deployment restores existing files and leaves original manifest intact");
        check(!File.Exists(missing), "failed repair restores previously missing owned file to missing state");
    }
}
