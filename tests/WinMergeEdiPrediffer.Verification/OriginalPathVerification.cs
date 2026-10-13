using System.Text.Json;
using WinMergeEdiPrediffer.Core;

namespace WinMergeEdiPrediffer.Verification;

internal static class OriginalPathVerification
{
    public static void Run(string payload, Action<bool, string> check)
    {
        var root = Path.GetFullPath(Path.Combine("work", "original-path-verification-" + Guid.NewGuid().ToString("N")));
        var configuration = new RuleConfiguration { Groups =
        [
            new() { Id = "850", Name = "850 directory", FilePattern = @"\\850\\.*\.edi$", Rules =
                [new() { FindPattern = @"(^|~)(ST\*[^*~\r\n]+\*)[^*~\r\n]*", Replacement = "$1$2<IGNORE>" }] },
            new() { Id = "860", Name = "860 directory", FilePattern = @"\\860\\.*\.edi$", Rules =
            [
                new() { FindPattern = @"(^|~)(ST\*[^*~\r\n]+\*)[^*~\r\n]*", Replacement = "$1$2<IGNORE>" },
                new() { FindPattern = @"(^|~)(SE\*[^*~\r\n]+\*)[^*~\r\n]*", Replacement = "$1$2<IGNORE>" }
            ] },
            new() { Id = "fallback", Name = "Fallback",
                Rules = [new() { FindPattern = "CANARY", Replacement = "FALLBACK" }] }
        ] };
        ExportResult Export() => PluginExporter.Export(configuration, Path.Combine(root, "source.json"), payload,
            root, Environment.ProcessPath!, Path.Combine(root, "backups"));
        Export();
        using var client = new NativeComClient(Path.Combine(root, PluginExporter.RouterName + ".comhost.dll"));
        check(client.MemberNames().Contains("PluginOriginalFilePath"), "TLB exposes optional original source path");
        var temporary = Path.Combine(Path.GetTempPath(), "WinMerge", "t0_wmdoc123.tmp");
        const string path850 = @"D:\EDI\new\850\A_850.edi";
        const string old850 = @"D:\EDI\old\850\A_850.edi";
        const string path860 = @"D:\EDI\new\860\A_860.edi";
        const string old860 = @"D:\EDI\old\860\A_860.edi";
        const string text850 = "ST*850*0001~\r\nSE*16*0002~";
        const string text860 = "ST*860*0009~\r\nSE*11*0009~";
        var folder850 = client.Prediff(text850, wrapVariants: true, fullPath: path850);
        var editor850 = client.Prediff(text850, wrapVariants: true, fullPath: path850, processingPath: temporary);
        check(folder850 == editor850 && editor850.Text == "ST*850*<IGNORE>~\r\nSE*16*0002~",
            "850 folder and editor temporary path use identical source routing");
        check(editor850 == client.Prediff("ST*850*0002~\r\nSE*16*0002~", wrapVariants: true, fullPath: old850, processingPath: temporary),
            "850 screenshot pair becomes identical through the editor path");
        var folder860 = client.Prediff(text860, wrapVariants: true, fullPath: path860);
        var editor860 = client.Prediff(text860, wrapVariants: true, fullPath: path860, processingPath: temporary);
        check(folder860 == editor860 && editor860.Text == "ST*860*<IGNORE>~\r\nSE*11*<IGNORE>~",
            "860 editor applies both ST02 and SE02 in configured order");
        check(editor860 == client.Prediff("ST*860*0001~\r\nSE*11*0001~", wrapVariants: true, fullPath: old860, processingPath: temporary),
            "860 screenshot pair becomes identical through the editor path");
        check(client.Prediff(text860, fullPath: path850, processingPath: temporary).Text == "ST*860*<IGNORE>~\r\nSE*11*0009~",
            "source directory selects the group independently of transaction type");
        check(client.Prediff("ST*850*0001~", fullPath: path850, processingPath: temporary).Text !=
            client.Prediff("ST*860*0001~", fullPath: path850, processingPath: temporary).Text,
            "ST01 differences remain visible with temporary processing paths");
        check(client.Prediff("ST*850*0001~BEG*EDITED~", fullPath: path850, processingPath: temporary).Text == "ST*850*<IGNORE>~BEG*EDITED~",
            "unsaved buffer content is processed without rereading the original file");
        check(client.Prediff(text860, fullPath: @"D:\EDI\new\860\Nested\A.edi", processingPath: temporary) == editor860,
            "nested source directory retains matching semantics");
        check(Fails(() => client.Prediff(text850, processingPath: temporary, provideOriginalPath: false)),
            "old host processing path alone is rejected instead of falling back");
        check(Fails(() => client.Prediff(text850)), "missing original path cannot reuse previous side");
        check(!client.Prediff("CANARY", fullPath: "", processingPath: temporary).Changed,
            "explicit empty source for an unsaved document preserves content and skips fallback");
        check(client.Prediff("CANARY", fullPath: @"D:\other.txt", processingPath: temporary).Text == "FALLBACK",
            "known unmatched source still selects fallback after specific groups");
        check(Fails(() => client.Prediff(text850, fullPath: "relative.edi", processingPath: temporary)),
            "relative original source is rejected even with an absolute processing path");
        check(Fails(() => client.Prediff(text850)), "failed source validation clears pending paths");
        client.SetProperty("PluginVariables", temporary);
        client.SetProperty("PluginOriginalFilePath", path850);
        check(Fails(() => client.SetProperty("PluginOriginalFilePath", 42)), "invalid original source setter reports an error");
        check(Fails(() => client.Prediff(text850)), "invalid source setter clears previously valid source");
        client.SetProperty("PluginOriginalFilePath", path850);
        client.SetProperty("PluginVariables", temporary);
        check(Fails(() => client.Prediff(text850)), "a new processing-path setter starts a fresh source context");
        check(client.Prediff(text850, fullPath: path850, processingPath: @"D:\EDI\new\860\misleading.edi") == editor850,
            "processing filename cannot override original source group");
        configuration.Groups[0].Rules[0].Replacement = "$1$2<HOT>";
        var updated = Export();
        check(updated.RulesOnlyUpdate && client.Prediff(text850, fullPath: path850, processingPath: temporary).Text.Contains("<HOT>"),
            "existing COM object reads hot rules while source and processing paths differ");
        var previousLog = Environment.GetEnvironmentVariable("WINMERGE_EDI_DIAGNOSTICS_PATH");
        var log = Path.Combine(root, "routing.jsonl");
        try
        {
            Environment.SetEnvironmentVariable("WINMERGE_EDI_DIAGNOSTICS_PATH", log);
            client.Prediff("ST*850*0001~PRIVATE-EDI-CONTENT", fullPath: path850, processingPath: temporary);
            client.Prediff("CANARY", fullPath: "", processingPath: temporary);
            check(Fails(() => client.Prediff(text850, processingPath: temporary, provideOriginalPath: false)), "missing source is traceable");
            var lines = File.ReadAllLines(log);
            using var selected = JsonDocument.Parse(lines[0]);
            check(selected.RootElement.GetProperty("originalPath").GetString() == path850 &&
                selected.RootElement.GetProperty("processingPath").GetString() == temporary &&
                selected.RootElement.GetProperty("selectedGroupId").GetString() == "850" &&
                selected.RootElement.GetProperty("configurationFingerprint").GetString()?.Length == 64,
                "diagnostics capture both paths group and configuration fingerprint");
            check(lines.Length == 3 && lines[1].Contains("no-original-path") && lines[2].Contains("missing-original-path") &&
                !string.Join("", lines).Contains("PRIVATE-EDI-CONTENT"), "diagnostics record missing reasons without EDI content");
            Environment.SetEnvironmentVariable("WINMERGE_EDI_DIAGNOSTICS_PATH", root);
            check(client.Prediff(text850, fullPath: path850, processingPath: temporary).Text.Contains("<HOT>"),
                "unwritable diagnostics destination cannot break comparison");
        }
        finally { Environment.SetEnvironmentVariable("WINMERGE_EDI_DIAGNOSTICS_PATH", previousLog); }
    }

    private static bool Fails(Action action)
    {
        try { action(); return false; }
        catch (InvalidOperationException) { return true; }
    }
}
