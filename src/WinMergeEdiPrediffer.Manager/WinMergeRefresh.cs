using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace WinMergeEdiPrediffer.Manager;

internal sealed record WinMergeRefreshResult(IReadOnlyList<int> NotifiedProcesses, IReadOnlyList<string> PendingProcesses)
{
    public string Message => PendingProcesses.Count > 0
        ? "規則已更新，但有 WinMerge 視窗尚未刷新：" + string.Join("；", PendingProcesses) + "。"
        : NotifiedProcesses.Count > 0
            ? "規則已熱更新；已通知 WinMerge 以最新規則刷新目前比對。"
            : "規則已更新；下次開啟 WinMerge 比對時會使用最新設定。";
}

/// <summary>沿用 WinMerge 的選單命令，刷新已開啟的比對，不關閉程式或比對分頁。</summary>
internal static class WinMergeRefresh
{
    private const uint WmCommand = 0x0111;
    // WinMerge v2.16.58.2 Src/resource.h，先讀取外掛、再重設已解析的自動預差異器。
    private const int ReloadPlugins = 32884;
    private const int AutomaticPrediffer = 33311;
    private const int RefreshComparison = 32787;

    internal static WinMergeRefreshResult RefreshOpenWindows(bool reloadPluginsRequired = false)
    {
        var notified = new List<int>();
        var pending = new List<string>();
        foreach (var process in Process.GetProcessesByName("WinMergeU"))
        {
            using (process)
            {
                try
                {
                    var version = FileVersionInfo.GetVersionInfo(process.MainModule!.FileName);
                    if ((version.FileMajorPart, version.FileMinorPart, version.FileBuildPart, version.FilePrivatePart) != (2, 16, 58, 2))
                        throw new InvalidOperationException(reloadPluginsRequired
                            ? "此版本請手動重新讀取外掛、選擇自動預差異化，再按 F5，不需重開 WinMerge"
                            : "此版本請手動按 F5 刷新目前比對");
                    var windows = MainWindows(process.Id);
                    if (windows.Count == 0) throw new InvalidOperationException("找不到主視窗");
                    foreach (var window in windows)
                    {
                        if (reloadPluginsRequired && !IsWindowEnabled(window)) throw new InvalidOperationException("選組條件已更新；請關閉擴充功能等對話視窗後再按套用，不需重開 WinMerge");
                        // 非同步送入視窗程序，避免「檔案完全相同」訊息視窗阻塞管理器。
                        // 狀態列只表示已送出刷新要求，完成應以實際比對結果確認。
                        // 日常只送刷新，不釋放擴充功能視窗仍持有的外掛物件。
                        // 內容規則只刷新；選組條件變更重新載入並重設所有文件的自動選組。
                        if (reloadPluginsRequired)
                        {
                            Command(window, ReloadPlugins);
                            Command(window, AutomaticPrediffer);
                        }
                        if (!SendNotifyMessage(window, WmCommand, (nuint)RefreshComparison, 0))
                            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
                    }
                    notified.Add(process.Id);
                }
                catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or NotSupportedException)
                {
                    pending.Add($"PID {process.Id}：{ex.Message}");
                }
            }
        }
        return new(notified, pending);
    }

    private static List<nint> MainWindows(int processId)
    {
        var result = new List<nint>();
        EnumWindows((window, _) =>
        {
            GetWindowThreadProcessId(window, out var owner);
            if (owner != processId) return true;
            var className = new StringBuilder(256);
            GetClassName(window, className, className.Capacity);
            if (className.ToString() == "WinMergeWindowClassW" || className.ToString().StartsWith("WinMergeWindowClassW-", StringComparison.Ordinal))
                result.Add(window);
            return true;
        }, 0);
        return result;
    }

    private static void Command(nint window, int command)
    {
        if (SendMessageTimeout(window, WmCommand, (nuint)command, 0, 0x0002 | 0x0020, 5000, out _) == 0)
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
    }

    private delegate bool EnumWindowCallback(nint window, nint parameter);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowCallback callback, nint parameter);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out uint processId);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(nint window, StringBuilder className, int maximum);
    [DllImport("user32.dll")] private static extern bool IsWindowEnabled(nint window);
    [DllImport("user32.dll", EntryPoint = "SendNotifyMessageW", SetLastError = true)] private static extern bool SendNotifyMessage(nint window, uint message, nuint wParam, nint lParam);
    [DllImport("user32.dll", SetLastError = true)] private static extern nint SendMessageTimeout(nint window, uint message, nuint wParam, nint lParam, uint flags, uint timeout, out nuint result);
}
