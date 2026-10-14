using System.Text;
using System.Text.RegularExpressions;

namespace WinMergeEdiPrediffer.Core;

/// <summary>WinMerge uppercases filters and paths and splits filters on semicolons.
/// Emit a common .NET/PCRE subset that survives that transformation.</summary>
public static class WinMergePathFilter
{
    public static string Compile(string pattern)
    {
        if (pattern.StartsWith("(?i)", StringComparison.Ordinal)) pattern = pattern[4..];
        var result = new StringBuilder();
        var inClass = false;
        for (var i = 0; i < pattern.Length; i++)
        {
            var c = pattern[i];
            if (c == '\\')
            {
                if (++i == pattern.Length) throw Unsupported("結尾反斜線");
                var escape = pattern[i];
                var replacement = escape switch
                {
                    'd' => inClass ? "0-9" : "[0-9]",
                    'w' => inClass ? "A-Z0-9_" : "[A-Z0-9_]",
                    's' => inClass ? " \\011\\012\\013\\014\\015" : "[ \\011\\012\\013\\014\\015]",
                    'D' when !inClass => "[^0-9]",
                    'W' when !inClass => "[^A-Z0-9_]",
                    'S' when !inClass => "[^ \\011\\012\\013\\014\\015]",
                    't' => "\\011", 'n' => "\\012", 'r' => "\\015", 'f' => "\\014",
                    ';' => "\\073",
                    'b' when !inClass => "(?:(?<![A-Z0-9_])(?=[A-Z0-9_])|(?<=[A-Z0-9_])(?![A-Z0-9_]))",
                    'B' when !inClass => "(?:(?<![A-Z0-9_])(?![A-Z0-9_])|(?<=[A-Z0-9_])(?=[A-Z0-9_]))",
                    'A' => "\\A", 'Z' => "\\Z",
                    _ when char.IsDigit(escape) => throw Unsupported("數字反向參照或八進位跳脫"),
                    _ when !char.IsLetter(escape) => "\\" + escape,
                    _ => throw Unsupported("\\" + escape)
                };
                result.Append(replacement);
                continue;
            }
            if (!inClass && c == '(' && i + 1 < pattern.Length && pattern[i + 1] == '?')
            {
                var tail = pattern[(i + 2)..];
                if (!(tail.StartsWith(':') || tail.StartsWith('=') || tail.StartsWith('!') ||
                      tail.StartsWith('>') || tail.StartsWith("<=", StringComparison.Ordinal) || tail.StartsWith("<!", StringComparison.Ordinal)))
                    throw Unsupported("具名群組、條件群組或內嵌選項（僅支援開頭 (?i)）");
            }
            if (inClass && c == '-' && i + 1 < pattern.Length && pattern[i + 1] == '[')
                throw Unsupported("字元類別相減");
            if (c == '[') inClass = true;
            if (c == ']') inClass = false;
            result.Append(c == ';' ? "\\073" : char.ToUpperInvariant(c).ToString());
        }
        var compiled = result.ToString();
        _ = new Regex(compiled, RegexOptions.CultureInvariant, RuleEngine.RegexTimeout);
        return compiled;
    }

    public static bool IsMatch(string pattern, string path) =>
        Regex.IsMatch(path.TrimStart().ToUpperInvariant(), Compile(pattern), RegexOptions.CultureInvariant, RuleEngine.RegexTimeout);

    private static InvalidOperationException Unsupported(string syntax) =>
        new($"官方 WinMerge 路徑篩選不支援此語法：{syntax}。請使用一般群組、字元類別或直接填入字元；內容規則仍使用完整 .NET 正則。");
}
