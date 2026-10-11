using System.Text.Json;

namespace WinMergeEdiPrediffer.Core;

/// <summary>每次呼叫讀取套用後的規則；以內容判斷更新，不依賴檔案時間戳。</summary>
public sealed class LiveProfileReader
{
    private readonly PluginProfile bootstrap;
    private readonly string? rulesPath;
    private readonly object sync = new();
    private string? cachedText;
    private PluginProfile? cachedProfile;

    public LiveProfileReader(string profilePath)
    {
        bootstrap = JsonSerializer.Deserialize<PluginProfile>(ReadShared(profilePath), ConfigurationJson.Options)
            ?? throw new InvalidOperationException("COM 外掛缺少有效的 profile 設定。");
        if (bootstrap.SchemaVersion is not (1 or 2 or 3)) throw new InvalidOperationException("不支援的 COM profile 版本。");
        ConfigurationValidator.Validate(new RuleConfiguration { Groups = [bootstrap.Group] });
        if (bootstrap.SchemaVersion >= 2)
        {
            if (string.IsNullOrWhiteSpace(bootstrap.SharedRulesPath)) throw new InvalidOperationException("COM 外掛缺少共用規則檔路徑。");
            rulesPath = Path.GetFullPath(bootstrap.SharedRulesPath, Path.GetDirectoryName(Path.GetFullPath(profilePath))!);
        }
    }

    public PluginProfile Bootstrap => bootstrap;

    public PluginProfile Current
    {
        get
        {
            if (rulesPath is null) return bootstrap;
            lock (sync)
            {
                var text = ReadShared(rulesPath);
                if (text == cachedText) return cachedProfile!;
                var live = JsonSerializer.Deserialize<LivePluginConfiguration>(text, ConfigurationJson.Options)
                    ?? throw new InvalidOperationException("已套用的規則不可為 null。");
                if (live.SchemaVersion != 1) throw new InvalidOperationException("不支援的共用規則版本。");
                ConfigurationValidator.Validate(live.Configuration);
                if (bootstrap.SchemaVersion == 3)
                {
                    cachedProfile = new PluginProfile
                    {
                        SchemaVersion = 3, Group = bootstrap.Group, FileFilter = ".*",
                        RoutingConfiguration = live.Configuration,
                        ConfigurationPath = live.ConfigurationPath, ManagerPath = live.ManagerPath,
                        ComHostPath = bootstrap.ComHostPath, SharedRulesPath = bootstrap.SharedRulesPath
                    };
                    cachedText = text;
                    return cachedProfile;
                }
                var ordered = RuleEngine.OrderedGroups(live.Configuration);
                var index = ordered.ToList().FindIndex(g => g.Id.Equals(bootstrap.Group.Id, StringComparison.OrdinalIgnoreCase));
                // 已刪除／停用的組即使仍被開啟的文件持有，也必須立即停止替換。
                var group = index >= 0 ? ordered[index] : new RuleGroup
                {
                    Id = bootstrap.Group.Id, Name = bootstrap.Group.Name + "（已停用／刪除）", Enabled = false
                };
                var current = new PluginProfile
                {
                    SchemaVersion = 2, Group = group,
                    FileFilter = index >= 0 ? PluginExporter.EffectiveFilter(group, ordered.Take(index)) : "(?!)",
                    ConfigurationPath = live.ConfigurationPath, ManagerPath = live.ManagerPath,
                    ComHostPath = bootstrap.ComHostPath, SharedRulesPath = bootstrap.SharedRulesPath
                };
                cachedProfile = current;
                cachedText = text;
                return current;
            }
        }
    }

    private static string ReadShared(string path)
    {
        // 套用以原子置換更新 JSON；讀取中的執行緒不阻止該置換。
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
