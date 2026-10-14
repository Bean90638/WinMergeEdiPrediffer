using WinMergeEdiPrediffer.Core;

namespace WinMergeEdiPrediffer.Manager;

internal static class AppPaths
{
    internal static string DataDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WinMergeEdiPrediffer");
    internal static string Backups => Path.Combine(DataDirectory, "backups");
    internal static string Payload => Path.Combine(AppContext.BaseDirectory, "Plugin");
    internal static string WinMergeExecutable
    {
        get
        {
            var configured = Environment.GetEnvironmentVariable("WINMERGE_EDI_WINMERGE_PATH");
            if (!string.IsNullOrWhiteSpace(configured)) return Path.GetFullPath(configured);
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WinMerge", "WinMergeU.exe");
        }
    }
    internal static string DefaultPlugins => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WinMerge", "MergePlugins");
    internal static string Example => Path.Combine(AppContext.BaseDirectory, "profiles.example.json");
    internal static string DefaultConfiguration
    {
        get
        {
            var root = new DirectoryInfo(AppContext.BaseDirectory);
            while (root is not null)
            {
                if (File.Exists(Path.Combine(root.FullName, "WinMergeEdiPrediffer.slnx"))) return Path.Combine(root.FullName, "profiles.json");
                root = root.Parent;
            }
            return Path.Combine(DataDirectory, "profiles.json");
        }
    }
    internal static void SaveConfiguration(RuleConfiguration configuration, string path)
    {
        ConfigurationValidator.Validate(configuration);
        if (File.Exists(path))
        {
            Directory.CreateDirectory(Backups);
            File.Copy(path, Path.Combine(Backups, "profiles-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N") + ".json"));
        }
        PluginExporter.AtomicWrite(path, ConfigurationJson.Serialize(configuration));
    }
}
