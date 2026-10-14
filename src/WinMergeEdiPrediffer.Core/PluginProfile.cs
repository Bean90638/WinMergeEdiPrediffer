namespace WinMergeEdiPrediffer.Core;

/// <summary>各組的載入入口；新版規則由共用 JSON 提供，執行時只讀。</summary>
public sealed class PluginProfile
{
    public int SchemaVersion { get; set; } = 1;
    public string FileFilter { get; set; } = ".*";
    public string ConfigurationPath { get; set; } = "";
    public string ManagerPath { get; set; } = "";
    public string ComHostPath { get; set; } = "";
    public string SharedRulesPath { get; set; } = "";
    public RuleGroup Group { get; set; } = new();

}

public sealed class LivePluginConfiguration
{
    public int SchemaVersion { get; set; } = 1;
    public string ConfigurationPath { get; set; } = "";
    public string ManagerPath { get; set; } = "";
    public RuleConfiguration Configuration { get; set; } = new();
}
