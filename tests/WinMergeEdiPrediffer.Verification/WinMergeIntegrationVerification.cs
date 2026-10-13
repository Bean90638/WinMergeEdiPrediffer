using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using WinMergeEdiPrediffer.Core;

namespace WinMergeEdiPrediffer.Verification;

internal static class WinMergeIntegrationVerification
{
    public static void Run(string executable, string payload, Action<bool, string> check, string? fixtureRoot = null)
    {
        check(WinMergeHostSupport.IsSupported(executable), "WinMerge executable advertises original source path API 1");
        var root = Path.GetFullPath(Path.Combine("work", "winmerge-integration-" + Guid.NewGuid().ToString("N")));
        var runtime = Path.Combine(root, "runtime");
        Directory.CreateDirectory(runtime);
        var isolatedExe = Path.Combine(runtime, "WinMergeU.exe");
        File.Copy(executable, isolatedExe);
        // Runtime DLLs come from the installed, matching official base version; no installed files are changed.
        var installed = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WinMerge");
        foreach (var dll in new[] { "vcomp140.dll", "darkmodelib\\darkmode.dll" })
        {
            var source = Path.Combine(installed, dll);
            if (!File.Exists(source)) continue;
            var destination = Path.Combine(runtime, dll);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(source, destination);
        }
        var configuration = new RuleConfiguration { Groups =
        [
            new() { Id = "850", Name = "850", FilePattern = @"\\850\\.*\.edi$", Priority = 10, Rules =
                [new() { FindPattern = @"(^|~)(ST\*[^*~\r\n]+\*)[^*~\r\n]*", Replacement = "$1$2<IGNORE>" }] },
            new() { Id = "860", Name = "860", FilePattern = @"\\860\\.*\.edi$", Priority = 20, Rules =
            [
                new() { FindPattern = @"(^|~)(ST\*[^*~\r\n]+\*)[^*~\r\n]*", Replacement = "$1$2<IGNORE>" },
                new() { FindPattern = @"(^|~)(SE\*[^*~\r\n]+\*)[^*~\r\n]*", Replacement = "$1$2<IGNORE>" }
            ] }
        ] };
        PluginExporter.Export(configuration, Path.Combine(root, "source.json"), payload,
            Path.Combine(runtime, "MergePlugins"), Environment.ProcessPath!, Path.Combine(root, "backups"));
        string Fixture(string side, string type, string text)
        {
            var path = Path.Combine(root, side, type, "A_" + type + ".edi");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            if (fixtureRoot is not null && !side.StartsWith("control-", StringComparison.Ordinal))
                File.Copy(Path.Combine(fixtureRoot, side, type, "A_" + type + ".edi"), path);
            else File.WriteAllText(path, text);
            return path;
        }
        var sourceHashes = new Dictionary<string, string>();
        string Input(string side, string type, string sample)
        {
            if (fixtureRoot is null) return sample;
            var source = Path.Combine(fixtureRoot, side, type, "A_" + type + ".edi");
            var bytes = File.ReadAllBytes(source);
            sourceHashes[source] = Convert.ToHexString(SHA256.HashData(bytes));
            return sample;
        }
        var new850 = Fixture("new", "850", Input("new", "850", "ST*850*0001~\r\nBEG*VISIBLE~\r\nSE*16*0002~\r\n"));
        var old850 = Fixture("old", "850", Input("old", "850", "ST*850*0002~\r\nBEG*VISIBLE~\r\nSE*16*0002~\r\n"));
        var new860 = Fixture("new", "860", Input("new", "860", "ST*860*0009~\r\nBCH*VISIBLE~\r\nSE*11*0009~\r\n"));
        var old860 = Fixture("old", "860", Input("old", "860", "ST*860*0001~\r\nBCH*VISIBLE~\r\nSE*11*0001~\r\n"));
        var control850 = Fixture("control-left", "850", "ST*850*0001~");
        var control860 = Fixture("control-right", "850", "ST*860*0002~");
        var allFiles = new[] { new850, old850, new860, old860, control850, control860 };
        var hashes = allFiles.ToDictionary(p => p, p => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p))));
        var results = new List<object>();
        void Compare(string name, string left, string right, int expectedExit, bool folders = false)
        {
            var log = Path.Combine(root, name + ".jsonl");
            var report = Path.Combine(root, name + ".html");
            var start = new ProcessStartInfo(isolatedExe)
            {
                UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
                WorkingDirectory = runtime, RedirectStandardOutput = true, RedirectStandardError = true
            };
            // WinMerge removes the final .dll; the full name can select a duplicate user-directory plugin.
            foreach (var argument in new[] { "/s-", "/u", "/noprefs", "/noninteractive", "/enableexitcode", "/minimize",
                "/cfg", "PluginsEnabled=1", "/cfg", "PredifferMode=1", "/prediffer", PluginExporter.RouterName + ".comhost",
                "/or", report }) start.ArgumentList.Add(argument);
            if (folders) start.ArgumentList.Add("/r");
            start.ArgumentList.Add(left); start.ArgumentList.Add(right);
            start.Environment["WINMERGE_EDI_DIAGNOSTICS_PATH"] = log;
            using var process = Process.Start(start) ?? throw new IOException("Unable to start isolated WinMerge.");
            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(60000)) { process.Kill(entireProcessTree: true); throw new TimeoutException(name); }
            var console = outputTask.GetAwaiter().GetResult() + errorTask.GetAwaiter().GetResult();
            File.WriteAllText(Path.Combine(root, name + ".console.txt"), console);
            Console.WriteLine($"WinMerge {name}: exit={process.ExitCode}, report={File.Exists(report)}, trace={File.Exists(log)} {console}");
            check(process.ExitCode == expectedExit && File.Exists(report) && new FileInfo(report).Length > 0,
                "real WinMerge " + name + " result and comparison report");
            check(File.Exists(log), "real WinMerge " + name + " invokes routing diagnostics");
            var records = File.ReadAllLines(log).Select(line => JsonDocument.Parse(line)).ToArray();
            try
            {
                check(records.All(r => r.RootElement.GetProperty("originalPath").GetString() is { Length: > 0 } source && allFiles.Contains(source)),
                    "real WinMerge " + name + " supplies only expected original paths");
                if (!folders)
                    check(records.Any(r => r.RootElement.GetProperty("processingPath").GetString() != r.RootElement.GetProperty("originalPath").GetString()),
                        "real WinMerge " + name + " handles editor temporary files");
                results.Add(new { name, exitCode = process.ExitCode, report, log, records = records.Length });
            }
            finally { foreach (var record in records) record.Dispose(); }
        }
        Compare("file-850", new850, old850, 0);
        Compare("file-860", new860, old860, 0);
        Compare("folders", Path.Combine(root, "new"), Path.Combine(root, "old"), 0, folders: true);
        Compare("preserve-ST01", control850, control860, 1);
        check(hashes.All(pair => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(pair.Key))) == pair.Value),
            "real WinMerge folder and file comparisons preserve all original SHA-256 hashes");
        if (fixtureRoot is not null)
            check(sourceHashes.All(pair => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(pair.Key))) == pair.Value),
                "actual user EDI fixture sources remain byte-for-byte unchanged");
        File.WriteAllText(Path.Combine(root, "integration-report.json"), JsonSerializer.Serialize(results, ConfigurationJson.Options));
        Console.WriteLine("WinMerge integration evidence: " + root);
    }
}
