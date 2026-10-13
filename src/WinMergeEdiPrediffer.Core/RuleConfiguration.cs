namespace WinMergeEdiPrediffer.Core;

/// <summary>規則設定文件；允許新增多個組，每個組允許新增多條規則。</summary>
public sealed class RuleConfiguration
{
    public int SchemaVersion { get; set; } = 2;
    public List<RuleGroup> Groups { get; set; } = [];
}

public sealed class RuleGroup
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "新的 EDI 規則組";
    public bool Enabled { get; set; } = true;

    /// <summary>判斷完整檔案路徑與檔名；預設任何檔案均可命中。</summary>
    public string FilePattern { get; set; } = ".*";

    /// <summary>依列表順序執行組內所有啟用的替換規則。</summary>
    public List<ReplacementRule> Rules { get; set; } = [];
}

public sealed class ReplacementRule
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "新的替換規則";
    public bool Enabled { get; set; } = true;
    public bool UseRegularExpression { get; set; } = true;
    public bool IgnoreCase { get; set; }
    public string FindPattern { get; set; } = "";
    public string Replacement { get; set; } = "";
}
