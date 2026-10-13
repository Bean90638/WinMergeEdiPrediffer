using System.Diagnostics;
using System.Text.Json;
using Microsoft.Win32;
using WinMergeEdiPrediffer.Core;

namespace WinMergeEdiPrediffer.Manager;

internal static class WinMergeIntegration
{
    internal const string SettingsKey = @"Software\Thingamahoochie\WinMerge\Settings";
    internal static bool IsRunning()
    {
        var processes = Process.GetProcessesByName("WinMergeU");
        try { return processes.Length != 0; }
        finally { foreach (var process in processes) process.Dispose(); }
    }
    internal static void EnsureArchitecture()
    {
        var executable = AppPaths.WinMergeExecutable;
        if (!WinMergeHostSupport.IsSupported(executable))
            throw new InvalidOperationException("目前 WinMerge 未支援依原始路徑選組。請使用隨附的 WinMerge 修正版；自訂位置可設定 WINMERGE_EDI_WINMERGE_PATH。");
        foreach (var process in Process.GetProcessesByName("WinMergeU"))
        {
            using (process)
            {
                var runningPath = process.MainModule?.FileName;
                if (runningPath is null || !WinMergeHostSupport.IsSupported(runningPath))
                    throw new InvalidOperationException("仍有未支援原始路徑選組的 WinMerge 正在執行。請先關閉原版 WinMerge，再開啟隨附的修正版後套用。");
            }
        }
        using var reader = new BinaryReader(File.OpenRead(executable));
        reader.BaseStream.Position = 0x3C; var header = reader.ReadInt32(); reader.BaseStream.Position = header + 4;
        if (reader.ReadUInt16() != 0x8664) throw new InvalidOperationException("目前 COM DLL 是 x64，必須搭配 64 位元 WinMerge。");
    }
    internal static string ConfigureAutomatic()
    {
        using var key = Registry.CurrentUser.CreateSubKey(SettingsKey);
        var saved = new[] { "PredifferMode", "PluginsEnabled", "PluginsCustomSettingsList" }.ToDictionary(name => name, name => new
        {
            exists = key.GetValueNames().Contains(name), value = key.GetValue(name),
            kind = key.GetValueNames().Contains(name) ? key.GetValueKind(name).ToString() : null
        });
        Directory.CreateDirectory(AppPaths.Backups);
        var backup = Path.Combine(AppPaths.Backups, "winmerge-settings-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N") + ".json");
        PluginExporter.AtomicWrite(backup, JsonSerializer.Serialize(new { registryKey = SettingsKey, values = saved }, ConfigurationJson.Options));
        var entries = (key.GetValue("PluginsCustomSettingsList") as string ?? "").Split('\t', StringSplitOptions.RemoveEmptyEntries)
            .Where(entry => !entry.StartsWith("BUFFER_PREDIFF.EdiPrediff_", StringComparison.OrdinalIgnoreCase)).ToList();
        const string oldPlugin = "BUFFER_PREDIFF.PrediffLineFilter=";
        var oldIndex = entries.FindIndex(entry => entry.StartsWith(oldPlugin, StringComparison.OrdinalIgnoreCase));
        if (oldIndex < 0) entries.Add(oldPlugin + "automatic:false");
        else
        {
            var options = entries[oldIndex][oldPlugin.Length..].Split('|').Where(item => !item.StartsWith("automatic:", StringComparison.OrdinalIgnoreCase)).ToList();
            options.Add("automatic:false"); entries[oldIndex] = oldPlugin + string.Join('|', options);
        }
        key.SetValue("PluginsCustomSettingsList", string.Join('\t', entries), RegistryValueKind.String);
        key.SetValue("PredifferMode", 1, RegistryValueKind.DWord); key.SetValue("PluginsEnabled", 1, RegistryValueKind.DWord);
        return backup;
    }
}
