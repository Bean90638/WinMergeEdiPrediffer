using System.Diagnostics;

namespace WinMergeEdiPrediffer.Core;

/// <summary>供整合驗證辨識官方 WinMerge 版本。</summary>
public static class WinMergeHostSupport
{
    public static bool IsSupported(string executable)
    {
        if (!File.Exists(executable)) return false;
        var version = FileVersionInfo.GetVersionInfo(executable);
        return (version.FileMajorPart, version.FileMinorPart, version.FileBuildPart, version.FilePrivatePart) == (2, 16, 58, 2);
    }
}
