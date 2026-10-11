using System.Diagnostics;
using System.Runtime.InteropServices;

internal static class BridgeBuilder
{
    public static void Build(string sourceDirectory, string outputPath, string typeLibraryPath)
    {
        var tools = FindCompiler();
        var source = Path.GetFullPath(sourceDirectory);
        var output = Path.GetFullPath(outputPath);
        var intermediate = Path.GetDirectoryName(output)!;
        Directory.CreateDirectory(intermediate);
        var imports = Path.Combine(intermediate, "Bridge.Kernel32.lib");
        var objectPath = Path.Combine(intermediate, "Bridge.obj");
        Run(Path.Combine(tools, "link.exe"), "/lib", "/nologo", "/machine:x64",
            "/def:" + Path.Combine(source, "Kernel32.def"), "/out:" + imports);
        Run(Path.Combine(tools, "cl.exe"), "/nologo", "/c", "/O2", "/GS-", "/Zl", "/W4", "/WX",
            "/Fo" + objectPath, Path.Combine(source, "Bridge.cpp"));
        Run(Path.Combine(tools, "link.exe"), "/nologo", "/dll", "/noentry", "/nodefaultlib", "/machine:x64",
            "/incremental:no", "/dynamicbase", "/nxcompat", "/highentropyva",
            "/def:" + Path.Combine(source, "Bridge.def"), "/out:" + output,
            "/implib:" + Path.Combine(intermediate, "Bridge.lib"), objectPath, imports);
        var update = BeginUpdateResource(output, false);
        if (update == IntPtr.Zero) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            var bytes = File.ReadAllBytes(Path.GetFullPath(typeLibraryPath));
            if (!UpdateResource(update, "TYPELIB", (IntPtr)1, 0, bytes, checked((uint)bytes.Length)))
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            var commit = update;
            update = IntPtr.Zero;
            if (!EndUpdateResource(commit, false))
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        }
        finally { if (update != IntPtr.Zero) EndUpdateResource(update, true); }
        Console.WriteLine("WinMerge COM bridge: " + output);
    }

    private static string FindCompiler()
    {
        var explicitPath = Environment.GetEnvironmentVariable("WINMERGE_EDI_MSVC_PATH");
        if (!string.IsNullOrWhiteSpace(explicitPath)) return ValidateCompiler(Path.GetFullPath(explicitPath));
        foreach (var root in new[] { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86) }.Distinct())
        {
            var visualStudio = Path.Combine(root, "Microsoft Visual Studio");
            if (!Directory.Exists(visualStudio)) continue;
            foreach (var version in Directory.EnumerateDirectories(visualStudio).OrderDescending())
                foreach (var edition in Directory.EnumerateDirectories(version))
                {
                    var msvc = Path.Combine(edition, "VC", "Tools", "MSVC");
                    if (!Directory.Exists(msvc)) continue;
                    foreach (var toolset in Directory.EnumerateDirectories(msvc).OrderDescending())
                    {
                        var directory = Path.Combine(toolset, "bin", "Hostx64", "x64");
                        if (File.Exists(Path.Combine(directory, "cl.exe")) && File.Exists(Path.Combine(directory, "link.exe"))) return directory;
                    }
                }
        }
        throw new FileNotFoundException("建置 WinMerge 轉接 DLL 需要 Visual Studio 的 MSVC x64 C++ 工具。非預設安裝位置可設定 WINMERGE_EDI_MSVC_PATH 指向含 cl.exe 與 link.exe 的目錄。");
    }

    private static string ValidateCompiler(string directory)
    {
        if (!File.Exists(Path.Combine(directory, "cl.exe")) || !File.Exists(Path.Combine(directory, "link.exe")))
            throw new FileNotFoundException("WINMERGE_EDI_MSVC_PATH 必須指向 MSVC Hostx64/x64 工具目錄。", directory);
        return directory;
    }

    private static void Run(string executable, params string[] arguments)
    {
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new IOException("無法啟動建置工具：" + executable);
        process.WaitForExit();
        if (process.ExitCode != 0) throw new InvalidOperationException($"{Path.GetFileName(executable)} 建置失敗：{process.ExitCode}");
    }

    [DllImport("kernel32.dll", EntryPoint = "BeginUpdateResourceW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr BeginUpdateResource(string path, [MarshalAs(UnmanagedType.Bool)] bool delete);
    [DllImport("kernel32.dll", EntryPoint = "UpdateResourceW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UpdateResource(IntPtr update, string type, IntPtr name, ushort language, byte[] data, uint size);
    [DllImport("kernel32.dll", EntryPoint = "EndUpdateResourceW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EndUpdateResource(IntPtr update, [MarshalAs(UnmanagedType.Bool)] bool discard);
}
