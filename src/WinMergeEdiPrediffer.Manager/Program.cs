using System.Text.Json;
using WinMergeEdiPrediffer.Core;

namespace WinMergeEdiPrediffer.Manager;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var reportIndex = Array.IndexOf(args, "--result");
        var reportPath = reportIndex >= 0 && reportIndex + 1 < args.Length ? args[reportIndex + 1] : null;
        try
        {
            if (reportIndex >= 0 && reportPath is null) throw new ArgumentException("--result 後面需要 JSON 結果檔路徑。");
            if (args.Length > 0 && args[0] is "--export" or "--install")
            {
                if (args.Length < 3) throw new ArgumentException("用法：--export 或 --install 設定.json 目標資料夾 [--result 結果.json]");
                var path = Path.GetFullPath(args[1]);
                var configuration = ConfigurationJson.Deserialize(File.ReadAllText(path));
                if (args[0] == "--install") WinMergeIntegration.EnsureArchitecture();
                var exported = PluginExporter.Export(configuration, path, AppPaths.Payload, args[2], Application.ExecutablePath, AppPaths.Backups,
                    destinationInUse: WinMergeIntegration.IsRunning());
                var registryBackup = args[0] == "--install" && !exported.RulesOnlyUpdate ? WinMergeIntegration.ConfigureAutomatic() : null;
                var refresh = args[0] == "--install" ? WinMergeRefresh.RefreshOpenWindows(exported.ReloadPluginsRequired) : null;
                var result = new
                {
                    success = true, destination = Path.GetFullPath(args[2]), groups = exported.Manifest.Groups,
                    backup = exported.BackupDirectory, registryBackup, rulesOnlyUpdate = exported.RulesOnlyUpdate,
                    reloadPluginsRequired = exported.ReloadPluginsRequired, refresh, nextStep = refresh?.Message
                };
                if (reportPath is not null) PluginExporter.AtomicWrite(reportPath, JsonSerializer.Serialize(result, ConfigurationJson.Options));
                Console.WriteLine(JsonSerializer.Serialize(result, ConfigurationJson.Options));
                return 0;
            }
            ApplicationConfiguration.Initialize();
            Application.Run(new MainForm(args.FirstOrDefault()));
            return 0;
        }
        catch (Exception ex)
        {
            if (reportPath is not null) PluginExporter.AtomicWrite(reportPath, JsonSerializer.Serialize(new { success = false, error = ex.Message }, ConfigurationJson.Options));
            if (args.FirstOrDefault()?.StartsWith("--", StringComparison.Ordinal) == true) Console.Error.WriteLine(ex.Message);
            else MessageBox.Show(ex.Message, "EDI 規則管理器", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }
}
