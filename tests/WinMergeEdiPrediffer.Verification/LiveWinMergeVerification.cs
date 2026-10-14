using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WinMergeEdiPrediffer.Core;

namespace WinMergeEdiPrediffer.Verification;

internal static class LiveWinMergeVerification
{
    public static void Run(string executable, string payload, Action<bool, string> check, bool fromFolder = false)
    {
        var root = Path.GetFullPath(Path.Combine("work", "official-live-" + Guid.NewGuid().ToString("N")));
        var runtime = Path.Combine(root, "runtime");
        Directory.CreateDirectory(runtime);
        foreach (var source in Directory.EnumerateFiles(Path.GetDirectoryName(executable)!, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(Path.GetDirectoryName(executable)!, source);
            if (relative.StartsWith("MergePlugins", StringComparison.OrdinalIgnoreCase)) continue;
            var target = Path.Combine(runtime, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(source, target);
        }
        var left = Path.Combine(root, "old", "850", "A.edi");
        var right = Path.Combine(root, "new", "850", "A.edi");
        Directory.CreateDirectory(Path.GetDirectoryName(left)!);
        Directory.CreateDirectory(Path.GetDirectoryName(right)!);
        File.WriteAllText(left, "A\r\n"); File.WriteAllText(right, "RIGHT\r\n");
        var before = new[] { left, right }.ToDictionary(p => p, File.ReadAllBytes);
        var configuration = new RuleConfiguration { Groups =
        [
            new() { Id = "850", Name = "850", FilePattern = @"\\850\\.*\.edi$", Rules = [new() { FindPattern = "A", Replacement = "B" }] },
            new() { Id = "fallback", Name = "Fallback" }
        ] };
        ExportResult Apply(bool running = false) => PluginExporter.Export(configuration, Path.Combine(root, "source.json"), payload,
            Path.Combine(runtime, "MergePlugins"), Environment.ProcessPath!, Path.Combine(root, "backups"), running);
        Apply();
        var log = Path.Combine(root, "trace.jsonl");
        var start = new ProcessStartInfo(Path.Combine(runtime, "WinMergeU.exe")) { UseShellExecute = false, WorkingDirectory = runtime };
        foreach (var arg in new[] { "/s-", "/u", "/noprefs", "/minimize", "/cfg", "PluginsEnabled=1", "/cfg", "PredifferMode=1",
            "/cfg", "ShowIdentical=1", "/prediffer", "<Automatic>",
            fromFolder ? Path.GetDirectoryName(left)! : left, fromFolder ? Path.GetDirectoryName(right)! : right }) start.ArgumentList.Add(arg);
        start.Environment["WINMERGE_EDI_DIAGNOSTICS_PATH"] = log;
        using var process = Process.Start(start) ?? throw new IOException("WinMerge start failed");
        try
        {
            var processId = process.Id;
            Wait(() => { process.Refresh(); return process.MainWindowHandle != 0; }, "main window");
            var window = process.MainWindowHandle;
            string Fingerprint(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
            int LineCount() => File.Exists(log) ? ReadLines().Length : 0;
            string[] ReadLines()
            {
                using var stream = new FileStream(log, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(stream);
                return reader.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries);
            }
            bool Has(int after, string group, string text, bool temporary = false) => File.Exists(log) && ReadLines().Skip(after).Any(line =>
            {
                try
                {
                    using var row = JsonDocument.Parse(line);
                    return (!temporary || row.RootElement.GetProperty("processingPath").GetString() is { Length: > 0 } path && !before.ContainsKey(path)) &&
                        row.RootElement.GetProperty("selectedGroupId").GetString() == group &&
                        row.RootElement.GetProperty("outputFingerprint").GetString() == Fingerprint(text);
                }
                catch (JsonException) { return false; }
            });
            Wait(() => Has(0, "850", "B\r\n"), "initial transformation");
            if (fromFolder)
            {
                var countBeforeOpen = LineCount();
                nint list = 0;
                Wait(() =>
                {
                    EnumChildWindows(window, (child, _) =>
                    {
                        var name = new StringBuilder(256);
                        GetClassName(child, name, name.Capacity);
                        if (name.ToString() == "SysListView32" && SendMessageTimeout(child, 0x1004, 0, 0, 2, 1000, out var items) != 0 && items > 0)
                            list = child;
                        return true;
                    }, 0);
                    return list != 0;
                }, "folder result list");
                SendMessageTimeout(list, 0x0100, 0x23, 0, 2, 1000, out _); // End selects the only fixture file.
                Command(window, 32866); // Compare selected folder item.
                Wait(() => Has(countBeforeOpen, "850", "B\r\n", temporary: true), "folder result opens editor with original-path group");
                check(true, "official folder result opens file comparison and retains original path selection");
            }
            void Refresh(ExportResult change, int after, string group, string text, string name)
            {
                if (change.ReloadPluginsRequired) { Command(window, 32884); Command(window, 33311); }
                Command(window, 32787);
                Wait(() => Has(after, group, text), name);
                process.Refresh();
                check(!process.HasExited && process.Id == processId && process.MainWindowHandle == window,
                    "same official WinMerge process and window (" + (fromFolder ? "folder to file" : "direct file") + "): " + name);
            }
            var count = LineCount();
            configuration.Groups[0].Rules[0].Replacement = "C";
            Refresh(Apply(true), count, "850", "C\r\n", "content hot update");
            count = LineCount();
            configuration.Groups[0].FilePattern = @"\\absent\\";
            Refresh(Apply(true), count, "fallback", "A\r\n", "path change reselects fallback");
            count = LineCount();
            var added = new RuleGroup { Id = "added", Name = "Added", FilePattern = @"\\850\\.*\.edi$", Rules =
                [new() { FindPattern = "A", Replacement = "D" }] };
            configuration.Groups.Insert(0, added);
            Refresh(Apply(true), count, "added", "D\r\n", "new group loaded without restarting");
            count = LineCount();
            configuration.Groups.Remove(added); configuration.Groups.Add(added);
            Refresh(Apply(true), count, "fallback", "A\r\n", "display order changes selection");
            count = LineCount();
            configuration.Groups.Remove(added); configuration.Groups.Insert(0, added);
            Refresh(Apply(true), count, "added", "D\r\n", "display order restored");
            count = LineCount();
            added.Enabled = false;
            Refresh(Apply(true), count, "fallback", "A\r\n", "disable loaded group");
            count = LineCount();
            added.Enabled = true;
            Refresh(Apply(true), count, "added", "D\r\n", "reenable retained group");
            count = LineCount();
            configuration.Groups.Remove(added);
            Refresh(Apply(true), count, "fallback", "A\r\n", "delete group safely");
            check(before.All(p => File.ReadAllBytes(p.Key).SequenceEqual(p.Value)), "live official comparisons preserve both source files");
        }
        finally
        {
            if (!process.HasExited)
            {
                process.CloseMainWindow();
                if (!process.WaitForExit(5000)) process.Kill(entireProcessTree: true);
            }
        }
    }

    private static void Wait(Func<bool> condition, string name)
    {
        var deadline = Stopwatch.StartNew();
        while (deadline.Elapsed < TimeSpan.FromSeconds(30))
        {
            if (condition()) return;
            Thread.Sleep(100);
        }
        throw new TimeoutException("Official WinMerge live verification: " + name);
    }
    private static void Command(nint window, int command)
    {
        if (SendMessageTimeout(window, 0x0111, (nuint)command, 0, 0x0002 | 0x0020, 10000, out _) == 0)
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
    }
    private delegate bool EnumChildCallback(nint window, nint parameter);
    [DllImport("user32.dll")] private static extern bool EnumChildWindows(nint parent, EnumChildCallback callback, nint parameter);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(nint window, StringBuilder name, int maximum);
    [DllImport("user32.dll", SetLastError = true)] private static extern nint SendMessageTimeout(nint window, uint message,
        nuint wParam, nint lParam, uint flags, uint timeout, out nuint result);
}
