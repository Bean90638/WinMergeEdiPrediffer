using System.Text.RegularExpressions;

namespace WinMergeEdiPrediffer.Core;

public static class ConfigurationValidator
{
    public static void Validate(RuleConfiguration configuration)
    {
        if (configuration.SchemaVersion != 1) throw new InvalidOperationException("不支援的設定版本。");
        if (configuration.Groups is null) throw new InvalidOperationException("設定缺少 groups。");
        if (configuration.Groups.Count(g => g.Enabled && g.IsDefault) > 1)
            throw new InvalidOperationException("只能啟用一個預設規則組。");
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in configuration.Groups)
        {
            if (string.IsNullOrWhiteSpace(group.Id) || !Regex.IsMatch(group.Id, "^[A-Za-z0-9_-]{1,64}$") || !ids.Add(group.Id))
                throw new InvalidOperationException("規則組 ID 必須唯一，且只能包含英文字母、數字、底線與連字號。");
            if (string.IsNullOrWhiteSpace(group.Name)) throw new InvalidOperationException("規則組名稱不可空白。");
            if (string.IsNullOrWhiteSpace(group.FilePattern)) throw new InvalidOperationException($"「{group.Name}」的檔案路徑正則不可空白，任何檔案請填 .*。");
            ValidateExpression(group.FilePattern, $"{group.Name} / 檔案路徑正則");
            if (group.IsDefault && group.FilePattern != ".*") throw new InvalidOperationException($"預設組「{group.Name}」的檔案路徑正則必須是 .*。");
            if (group.Rules is null) throw new InvalidOperationException($"「{group.Name}」缺少 rules。");
            var ruleIds = new HashSet<string>();
            foreach (var rule in group.Rules)
            {
                if (string.IsNullOrWhiteSpace(rule.Id) || !ruleIds.Add(rule.Id)) throw new InvalidOperationException($"「{group.Name}」的規則 ID 不可重複或空白。");
                if (string.IsNullOrWhiteSpace(rule.Name)) throw new InvalidOperationException($"「{group.Name}」有空白規則名稱。");
                if (rule.FindPattern is null || rule.Replacement is null) throw new InvalidOperationException($"「{rule.Name}」的尋找或取代不可為 null。");
                if (rule.FindPattern.Length == 0 && rule.Enabled) throw new InvalidOperationException($"「{rule.Name}」的尋找不可空白；未完成的規則可先停用。");
                if (rule.UseRegularExpression && rule.FindPattern.Length > 0)
                    ValidateExpression(rule.FindPattern, $"{group.Name} / {rule.Name}");
            }
        }
    }

    private static void ValidateExpression(string pattern, string location)
    {
        try { _ = new Regex(pattern, RegexOptions.CultureInvariant, RuleEngine.RegexTimeout); }
        catch (ArgumentException ex) { throw new InvalidOperationException($"「{location}」的正則有誤：{ex.Message}", ex); }
    }

}
