using System.Diagnostics;

namespace WinMergeEdiPrediffer.Core;

/// <summary>辨識本工具原始路徑介面所需的 WinMerge 配套。</summary>
public static class WinMergeHostSupport
{
    public const string PrivateBuildMarker = "EDI Original Path API 1";

    public static bool IsSupported(string executable)
    {
        if (!File.Exists(executable)) return false;
        var version = FileVersionInfo.GetVersionInfo(executable);
        return (version.FileMajorPart, version.FileMinorPart, version.FileBuildPart, version.FilePrivatePart) == (2, 16, 58, 2)
            && version.PrivateBuild == PrivateBuildMarker;
    }
}
