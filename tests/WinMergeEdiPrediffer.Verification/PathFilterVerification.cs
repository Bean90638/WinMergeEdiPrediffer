using WinMergeEdiPrediffer.Core;

namespace WinMergeEdiPrediffer.Verification;

internal static class PathFilterVerification
{
    public static void Run(Action<bool, string> check)
    {
        foreach (var (pattern, path) in new[]
        {
            (@"\\850\\.*\.edi$", @"D:\Temp\850\Testing\111.edi"),
            (@"(?i)\\EDI\d+;A\.edi$", @"D:\edi850;A.edi"),
            (@"\\[a-z\d]+\.edi$", @"D:\abc123.EDI"),
            (@"\\\w+\sfile\.edi$", @"D:\abc_123 file.edi"),
            (@"\bA\.edi$", @"D:\A.edi")
        })
        {
            var compiled = WinMergePathFilter.Compile(pattern);
            check(compiled == compiled.ToUpperInvariant() && !compiled.Contains(';') && WinMergePathFilter.IsMatch(pattern, path),
                "path escapes survive official uppercase and semicolon splitting: " + pattern);
        }
        foreach (var pattern in new[] { @"(?<name>A)", @"\p{L}+", @"(?-i)a", @"[A-Z-[X]]", @"(A)\1" })
        {
            var rejected = false;
            try { WinMergePathFilter.Compile(pattern); }
            catch (InvalidOperationException) { rejected = true; }
            check(rejected, "incompatible path syntax explicitly rejected: " + pattern);
        }
    }
}
