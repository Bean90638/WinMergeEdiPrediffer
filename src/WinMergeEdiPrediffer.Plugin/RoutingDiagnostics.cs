using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WinMergeEdiPrediffer.Core;

namespace WinMergeEdiPrediffer.Plugin;

internal static class RoutingDiagnostics
{
    private static readonly object Sync = new();

    internal static void Write(PluginProfile profile, string processingPath, string? originalPath,
        RuleGroup? group, string reason, bool changed, string transformed)
    {
        var destination = Environment.GetEnvironmentVariable("WINMERGE_EDI_DIAGNOSTICS_PATH");
        if (string.IsNullOrWhiteSpace(destination)) return;
        try
        {
            var configuration = new RuleConfiguration { Groups = [profile.Group] };
            var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(ConfigurationJson.Serialize(configuration))));
            var record = JsonSerializer.Serialize(new
            {
                timestampUtc = DateTime.UtcNow, processId = Environment.ProcessId,
                processingPath, originalPath, selectedGroupId = group?.Id,
                reason, changed, configurationFingerprint = fingerprint,
                outputFingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(transformed)))
            });
            lock (Sync)
            {
                // 由使用者指定已存在且可寫的目錄；診斷失敗不可阻止比對。
                File.AppendAllText(destination, record + Environment.NewLine, Encoding.UTF8);
            }
        }
        catch (Exception) { /* 診斷不可改變比對結果。 */ }
    }
}
