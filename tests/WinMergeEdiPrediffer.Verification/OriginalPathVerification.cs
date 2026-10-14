using System.Text.RegularExpressions;
using WinMergeEdiPrediffer.Core;

namespace WinMergeEdiPrediffer.Verification;

internal static class OriginalPathVerification
{
    public static void Run(string payload, Action<bool, string> check)
    {
        var root = Path.GetFullPath(Path.Combine("work", "original-filter-" + Guid.NewGuid().ToString("N")));
        var configuration = new RuleConfiguration { Groups =
        [
            new() { Id = "850", Name = "850", FilePattern = @"\\850\\.*\.edi$", Rules =
                [new() { FindPattern = @"(^|~)(ST\*[^*~\r\n]+\*)[^*~\r\n]*", Replacement = "$1$2<IGNORE>" }] },
            new() { Id = "860", Name = "860", FilePattern = @"\\860\\.*\.edi$", Rules =
                [new() { FindPattern = @"(^|~)(SE\*[^*~\r\n]+\*)[^*~\r\n]*", Replacement = "$1$2<IGNORE>" }] },
            new() { Id = "overlap", Name = "overlap", FilePattern = @"\.edi$" },
            new() { Id = "fallback", Name = "Fallback" }
        ] };
        var exported = PluginExporter.Export(configuration, Path.Combine(root, "source.json"), payload,
            root, Environment.ProcessPath!, Path.Combine(root, "backups"));
        foreach (var path in new[] { @"D:\EDI\new\850\A.edi", @"D:\EDI\old\850\Testing\A.edi",
                     @"D:\EDI\new\860\A.EDI", @"D:\other\A.edi", @"D:\other.txt" })
        {
            var hits = exported.Manifest.Groups.Where(g => Regex.IsMatch(path.ToUpperInvariant(), g.EffectiveFilter.ToUpperInvariant())).ToArray();
            check(hits.Length == 1 && hits[0].Id == RuleEngine.SelectGroup(configuration, path)?.Id,
                "host filter preserves first displayed match and preview for " + path);
        }
        var selected = exported.Manifest.Groups.Single(g => g.Id == "850");
        using var client = new NativeComClient(Path.Combine(root, selected.PluginName + ".comhost.dll"));
        const string input = "ST*850*0001~\r\nSE*16*0002~";
        check(client.Prediff(input, processingPath: @"D:\EDI\new\850\A.edi") ==
              client.Prediff(input, processingPath: @"C:\Temp\t0_wmdoc.tmp", wrapVariants: true),
            "folder source and file editor temporary buffer apply identical selected rules");
        check(client.Prediff("ST*850*0001~BEG*EDITED~").Text == "ST*850*<IGNORE>~BEG*EDITED~",
            "unsaved editor changes are processed without reading original disk files");
        check(client.Prediff("ST*850*0001~").Text != client.Prediff("ST*860*0001~").Text,
            "visible transaction type differences remain visible");
    }
}
