using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using WinMergeEdiPrediffer.Core;

namespace WinMergeEdiPrediffer.Plugin;

/// <summary>保留 WinMerge 的 IDispatch ABI，並從內嵌 TLB 提供它必要的 GetTypeInfo。</summary>
[ComVisible(true), Guid("d4f1b0ba-447a-46b3-a253-9b5844df6a35"), ClassInterface(ClassInterfaceType.AutoDispatch)]
public sealed class WinMergeScript : IDispatchBridge, ICustomQueryInterface
{
    private static readonly Guid DispatchGuid = new("00020400-0000-0000-C000-000000000046");
    private static readonly Guid TypeInfoGuid = new("f6ebfd94-1e9a-445b-8c0a-a39686514251");
    private readonly LiveProfileReader profiles;
    private readonly string hostPath;
    private string currentFilePath = "";
    private string? originalFilePath;
    private static readonly Dictionary<string, int> Members = new(StringComparer.OrdinalIgnoreCase)
    {
        ["PluginEvent"] = 1, ["PluginDescription"] = 2, ["PluginFileFilters"] = 3,
        ["PluginIsAutomatic"] = 4, ["PluginExtendedProperties"] = 5,
        ["PrediffBufferW"] = 10, ["ShowSettingsDialog"] = 11, ["PluginVariables"] = 12,
        ["PluginOriginalFilePath"] = 13
    };

    public WinMergeScript()
    {
        var assemblyPath = typeof(WinMergeScript).Assembly.Location;
        var profilePath = Path.ChangeExtension(assemblyPath, ".profile.json");
        profiles = new LiveProfileReader(profilePath);
        var profile = profiles.Bootstrap;
        hostPath = string.IsNullOrEmpty(profile.ComHostPath) ? Path.ChangeExtension(assemblyPath, ".comhost.dll")
            : Path.GetFullPath(profile.ComHostPath, Path.GetDirectoryName(assemblyPath)!);
        _ = profiles.Current;
    }

    public CustomQueryInterfaceResult GetInterface(ref Guid iid, out IntPtr ppv)
    {
        ppv = IntPtr.Zero;
        if (iid != DispatchGuid) return CustomQueryInterfaceResult.NotHandled;
        ppv = Marshal.GetComInterfaceForObject(this, typeof(IDispatchBridge), CustomQueryInterfaceMode.Ignore);
        return CustomQueryInterfaceResult.Handled;
    }

    public int GetTypeInfoCount(out uint count) { count = 1; return 0; }

    public int GetTypeInfo(uint index, uint lcid, out IntPtr info)
    {
        info = IntPtr.Zero;
        if (index != 0) return unchecked((int)0x8002000B);
        var library = IntPtr.Zero;
        try
        {
            Marshal.ThrowExceptionForHR(LoadTypeLibEx(hostPath, 2, out library)); // REGKIND_NONE
            var typeGuid = TypeInfoGuid;
            // 直接返回原生 ITypeInfo，避免為型別程式庫建立跨執行緒 RCW。
            var getTypeInfo = Marshal.GetDelegateForFunctionPointer<GetTypeInfoOfGuid>(
                Marshal.ReadIntPtr(Marshal.ReadIntPtr(library), 6 * IntPtr.Size));
            return getTypeInfo(library, ref typeGuid, out info);
        }
        catch (Exception ex) { return ex.HResult; }
        finally { if (library != IntPtr.Zero) Marshal.Release(library); }
    }

    public int GetIDsOfNames(ref Guid iid, IntPtr names, uint count, uint lcid, IntPtr ids)
    {
        var result = 0;
        for (var i = 0; i < count; i++)
        {
            var name = Marshal.PtrToStringUni(Marshal.ReadIntPtr(names, i * IntPtr.Size)) ?? "";
            if (!Members.TryGetValue(name, out var id)) { id = -1; result = unchecked((int)0x80020006); }
            Marshal.WriteInt32(ids, i * 4, id);
        }
        return result;
    }

    public int Invoke(int id, ref Guid iid, uint lcid, ushort flags, ref DISPPARAMS parameters,
        IntPtr result, IntPtr exceptionInfo, IntPtr argumentError)
    {
        try
        {
            var profile = profiles.Current;
            object value;
            if ((flags & 2) != 0 && parameters.cArgs == 0)
                value = id switch
                {
                    1 => "BUFFER_PREDIFF", 2 => profile.SchemaVersion == 3 ? "EDI 預差異器：依路徑選組、JSON 規則熱更新（C# COM）" : $"EDI 規則組：{profile.Group.Name}（C# COM）",
                    3 => profile.FileFilter, 4 => profile.SchemaVersion == 3 || profile.Group.Enabled,
                    5 => "SettingsDialog=1;MenuCaption=" + (profile.SchemaVersion == 3 ? "EDI 預差異器" : profile.Group.Name),
                    12 => currentFilePath, 13 => originalFilePath ?? "",
                    _ => throw new MissingMemberException()
                };
            else if ((flags & 4) != 0 && id is 12 or 13)
            {
                // Host 先傳處理路徑，再傳原始路徑；每次開始皆清除上一側的來源。
                if (id == 12) { currentFilePath = ""; originalFilePath = null; }
                else originalFilePath = null;
                var property = id == 12 ? "PluginVariables" : "PluginOriginalFilePath";
                if (parameters.cArgs != 1 || parameters.rgvarg == IntPtr.Zero)
                    throw new ArgumentException($"{property} 必須接收一個檔案路徑字串。");
                var path = Marshal.GetObjectForNativeVariant(parameters.rgvarg) as string
                    ?? throw new ArgumentException($"{property} 的 VARIANT 必須是檔案路徑字串。");
                if (id == 12) currentFilePath = path;
                else originalFilePath = path;
                value = null!;
            }
            else if ((flags & 1) != 0 && id == 10)
            {
                var processingPath = currentFilePath;
                var path = originalFilePath;
                currentFilePath = ""; originalFilePath = null;
                if (profile.SchemaVersion == 3 && path is null)
                {
                    RoutingDiagnostics.Write(profile, processingPath, null, null, "missing-original-path", false);
                    throw new InvalidOperationException("WinMerge 未提供原始檔案路徑，無法選取 EDI 規則組。請使用支援 PluginOriginalFilePath 的 WinMerge 修正版。");
                }
                if (profile.SchemaVersion == 3 && path!.Length > 0 && !Path.IsPathFullyQualified(path))
                    throw new InvalidOperationException("WinMerge 提供的原始檔案路徑不是完整路徑，無法選取 EDI 規則組。");
                // Host 明確傳入空來源代表未儲存的新文件；保留內容，不套用預設組。
                var group = profile.SchemaVersion == 3
                    ? string.IsNullOrEmpty(path) ? null : RuleEngine.SelectGroup(profile.RoutingConfiguration!, path)
                    : profile.Group;
                value = Prediff(ref parameters, group, out var changed);
                RoutingDiagnostics.Write(profile, processingPath, path, group,
                    string.IsNullOrEmpty(path) && profile.SchemaVersion == 3 ? "no-original-path"
                        : group is null ? "no-match" : "selected", changed);
            }
            else if ((flags & 1) != 0 && id == 11 && parameters.cArgs == 0)
            {
                if (!File.Exists(profile.ManagerPath)) throw new FileNotFoundException("找不到規則管理器。", profile.ManagerPath);
                var start = new ProcessStartInfo(profile.ManagerPath) { UseShellExecute = true };
                start.ArgumentList.Add(profile.ConfigurationPath);
                Process.Start(start);
                value = true;
            }
            else return unchecked((int)0x80020003);
            if (result != IntPtr.Zero) Marshal.GetNativeVariantForObject(value, result);
            return 0;
        }
        catch (Exception ex)
        {
            if (id is 10 or 12 or 13) { currentFilePath = ""; originalFilePath = null; }
            // 返回 EXCEPINFO，讓 WinMerge 顯示失敗；不可默默將無法處理的檔案當作相同。
            if (exceptionInfo != IntPtr.Zero)
                Marshal.StructureToPtr(new EXCEPINFO { bstrSource = "WinMergeEdiPrediffer", bstrDescription = ex.Message, scode = ex.HResult }, exceptionInfo, false);
            return unchecked((int)0x80020009);
        }
    }

    private bool Prediff(ref DISPPARAMS arguments, RuleGroup? group, out bool contentChanged)
    {
        if (arguments.cArgs != 3) throw new ArgumentException("PrediffBufferW 必須接收 text、size、changed。");
        var variantSize = IntPtr.Size == 8 ? 24 : 16;
        var changed = ByRef(arguments.rgvarg, 0x400B);
        var size = ByRef(arguments.rgvarg + variantSize, 0x4003);
        var buffer = ByRef(arguments.rgvarg + variantSize * 2, 0x4008);
        var bstr = Marshal.ReadIntPtr(buffer);
        var original = Marshal.PtrToStringBSTR(bstr);
        var output = group?.Enabled == true ? RuleEngine.Apply(group, original) : original;
        if (output != original)
        {
            var replacement = Marshal.StringToBSTR(output);
            Marshal.WriteIntPtr(buffer, replacement);
            Marshal.FreeBSTR(bstr);
        }
        Marshal.WriteInt32(size, output.Length);
        contentChanged = output != original;
        Marshal.WriteInt16(changed, contentChanged ? (short)-1 : (short)0);
        return true;
    }

    private static IntPtr ByRef(IntPtr variant, ushort expected)
    {
        // WinMerge 的 invokeV 會依型別資訊把參數包成 BYREF VARIANT，
        // 內層是 VariantCopyInd 產生的 BSTR/I4/BOOL 值。亦接受直接的 typed BYREF。
        for (var depth = 0; depth < 16; depth++)
        {
            if (variant == IntPtr.Zero) throw new ArgumentException("WinMerge 傳入空的 VARIANT 指標。");
            var actual = (ushort)Marshal.ReadInt16(variant);
            if (actual == 0x400C) { variant = Marshal.ReadIntPtr(variant, 8); continue; }
            if (actual == expected) return Marshal.ReadIntPtr(variant, 8);
            if (depth > 0 && actual == (expected & ~0x4000)) return variant + 8;
            throw new ArgumentException($"WinMerge 傳入的 VARIANT 型別不正確：預期 0x{expected:X4}，收到 0x{actual:X4}。");
        }
        throw new ArgumentException("WinMerge VARIANT 參照層數過多。");
    }

    [DllImport("oleaut32.dll", CharSet = CharSet.Unicode)]
    private static extern int LoadTypeLibEx(string path, int registrationKind, out IntPtr typeLib);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetTypeInfoOfGuid(IntPtr library, ref Guid guid, out IntPtr info);
}

// 自訂 IID 避免 .NET 內建的 IDispatch 蓋過 GetTypeInfo；QueryInterface 將 IID_IDispatch 導向此 ABI。
[ComVisible(true), Guid("f6ebfd94-1e9a-445b-8c0a-a39686514251"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IDispatchBridge
{
    [PreserveSig] int GetTypeInfoCount(out uint count);
    [PreserveSig] int GetTypeInfo(uint index, uint lcid, out IntPtr info);
    [PreserveSig] int GetIDsOfNames(ref Guid iid, IntPtr names, uint count, uint lcid, IntPtr ids);
    [PreserveSig] int Invoke(int id, ref Guid iid, uint lcid, ushort flags, ref DISPPARAMS parameters, IntPtr result, IntPtr exceptionInfo, IntPtr argumentError);
}
