using System.Text;
using System.Text.RegularExpressions;

namespace WinMergeEdiPrediffer.Core;

public static class RuleEngine
{
    public static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(2);

    public static IReadOnlyList<RuleGroup> OrderedGroups(RuleConfiguration configuration) =>
        configuration.Groups.Where(g => g.Enabled).OrderBy(g => g.IsDefault)
            .ThenBy(g => g.Priority).ThenBy(g => configuration.Groups.IndexOf(g)).ToList();

    public static RuleGroup? SelectGroup(RuleConfiguration configuration, string fullPath) =>
        OrderedGroups(configuration).FirstOrDefault(g => IsPathMatch(g, fullPath));

    public static bool IsPathMatch(RuleGroup group, string fullPath) =>
        // 路徑選組在 C# 內進行；Windows 路徑不分大小寫，保留正則本身的語法。
        Regex.IsMatch(fullPath, group.FilePattern,
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexTimeout);

    public static string Apply(RuleGroup group, string input)
    {
        var text = input;
        foreach (var rule in group.Rules.Where(r => r.Enabled))
            text = ApplyRule(group, rule, text);
        return text;
    }

    public static IReadOnlyList<RuleStep> Preview(RuleGroup group, string input)
    {
        var steps = new List<RuleStep>();
        var text = input;
        foreach (var rule in group.Rules.Where(r => r.Enabled))
        {
            var output = ApplyRule(group, rule, text);
            steps.Add(new RuleStep(rule.Name, output, text != output)); text = output;
        }
        return steps;
    }

    private static string ApplyRule(RuleGroup group, ReplacementRule rule, string text)
    {
        var options = RegexOptions.CultureInvariant | (rule.IgnoreCase ? RegexOptions.IgnoreCase : RegexOptions.None);
        var expression = new Regex(rule.UseRegularExpression ? rule.FindPattern : Regex.Escape(rule.FindPattern), options, RegexTimeout);
        string Transform(string line)
        {
            var output = rule.UseRegularExpression ? expression.Replace(line, rule.Replacement) : expression.Replace(line, _ => rule.Replacement);
            if (output.IndexOfAny(['\r', '\n']) >= 0)
                throw new InvalidOperationException($"「{group.Name} / {rule.Name}」產生換行，無法用於預差異化。請保留原有的 CR/LF。");
            return output;
        }
        // 每行分別取代，再原樣放回 CR、LF 或 CRLF；規則無法移動或合併原始行。
        var result = new StringBuilder(text.Length); var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] is not ('\r' or '\n')) continue;
            result.Append(Transform(text[start..i])); result.Append(text[i]);
            if (text[i] == '\r' && i + 1 < text.Length && text[i + 1] == '\n') result.Append(text[++i]);
            start = i + 1;
        }
        result.Append(Transform(text[start..])); return result.ToString();
    }
}

public sealed record RuleStep(string Name, string Text, bool Changed);
