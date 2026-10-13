using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

// 使用 Windows 自帶的型別程式庫建置 API，無需另裝 MIDL 或註冊任何 COM 物件。
try
{
if (args.Length == 4 && args[0] == "--build-bridge")
{
    BridgeBuilder.Build(args[1], args[2], args[3]);
    return;
}
if (args.Length != 1) throw new ArgumentException("Usage: TypeLibraryBuilder output.tlb");
var path = Path.GetFullPath(args[0]);
Directory.CreateDirectory(Path.GetDirectoryName(path)!);
Marshal.ThrowExceptionForHR(Native.CreateTypeLib2(SYSKIND.SYS_WIN64, path, out var library));
library.SetGuid(new Guid("316a7bb9-a0c5-4500-b140-bdd03ad2a21e"));
library.SetName("WinMergeEdiPrediffer");
library.SetVersion(1, 0);
library.CreateTypeInfo("IWinMergeScript", TYPEKIND.TKIND_DISPATCH, out var dispatch);
dispatch.SetGuid(new Guid("f6ebfd94-1e9a-445b-8c0a-a39686514251"));
dispatch.SetTypeFlags((uint)TYPEFLAGS.TYPEFLAG_FDISPATCHABLE);

var properties = new[] { "PluginEvent", "PluginDescription", "PluginFileFilters", "PluginIsAutomatic", "PluginExtendedProperties" };
for (var i = 0; i < properties.Length; i++)
    AddFunction(dispatch, (uint)i, i + 1, properties[i], INVOKEKIND.INVOKE_PROPERTYGET,
        properties[i] == "PluginIsAutomatic" ? VarEnum.VT_BOOL : VarEnum.VT_BSTR, []);
AddFunction(dispatch, 5, 10, "PrediffBufferW", INVOKEKIND.INVOKE_FUNC, VarEnum.VT_BOOL,
    [("text", VarEnum.VT_BSTR), ("size", VarEnum.VT_I4), ("changed", VarEnum.VT_BOOL)]);
AddFunction(dispatch, 6, 11, "ShowSettingsDialog", INVOKEKIND.INVOKE_FUNC, VarEnum.VT_BOOL, []);
AddFunction(dispatch, 7, 12, "PluginVariables", INVOKEKIND.INVOKE_PROPERTYGET, VarEnum.VT_BSTR, []);
AddFunction(dispatch, 8, 12, "PluginVariables", INVOKEKIND.INVOKE_PROPERTYPUT, VarEnum.VT_VOID,
    [("value", VarEnum.VT_BSTR)], byRef: false);
AddFunction(dispatch, 9, 13, "PluginOriginalFilePath", INVOKEKIND.INVOKE_PROPERTYGET, VarEnum.VT_BSTR, []);
AddFunction(dispatch, 10, 13, "PluginOriginalFilePath", INVOKEKIND.INVOKE_PROPERTYPUT, VarEnum.VT_VOID,
    [("value", VarEnum.VT_BSTR)], byRef: false);
dispatch.LayOut();
library.CreateTypeInfo("WinMergeScript", TYPEKIND.TKIND_COCLASS, out var coclass);
coclass.SetGuid(new Guid("d4f1b0ba-447a-46b3-a253-9b5844df6a35"));
coclass.SetTypeFlags((uint)TYPEFLAGS.TYPEFLAG_FCANCREATE);
coclass.AddRefTypeInfo((ITypeInfo)dispatch, out var reference);
coclass.AddImplType(0, reference);
coclass.SetImplTypeFlags(0, IMPLTYPEFLAGS.IMPLTYPEFLAG_FDEFAULT);
coclass.LayOut();
library.SaveAllChanges();
Console.WriteLine(path);
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex);
    Environment.ExitCode = 1;
}

static void AddFunction(ICreateTypeInfo type, uint index, int id, string name, INVOKEKIND kind,
    VarEnum resultType, (string Name, VarEnum Type)[] parameters, bool byRef = true)
{
    var allocations = new List<IntPtr>();
    try
    {
        var elemSize = Marshal.SizeOf<ELEMDESC>();
        var elems = parameters.Length == 0 ? IntPtr.Zero : Marshal.AllocCoTaskMem(elemSize * parameters.Length);
        if (elems != IntPtr.Zero) allocations.Add(elems);
        for (var i = 0; i < parameters.Length; i++)
        {
            var scalar = Marshal.AllocCoTaskMem(Marshal.SizeOf<TYPEDESC>());
            allocations.Add(scalar);
            Marshal.StructureToPtr(new TYPEDESC { vt = (short)parameters[i].Type }, scalar, false);
            var elem = new ELEMDESC
            {
                tdesc = byRef ? new TYPEDESC { vt = (short)VarEnum.VT_PTR, lpValue = scalar } : new TYPEDESC { vt = (short)parameters[i].Type },
                desc = new ELEMDESC.DESCUNION { paramdesc = new PARAMDESC { wParamFlags = byRef ? PARAMFLAG.PARAMFLAG_FIN | PARAMFLAG.PARAMFLAG_FOUT : PARAMFLAG.PARAMFLAG_FIN } }
            };
            Marshal.StructureToPtr(elem, elems + i * elemSize, false);
        }
        var function = new FUNCDESC
        {
            memid = id, funckind = FUNCKIND.FUNC_DISPATCH, invkind = kind, callconv = CALLCONV.CC_STDCALL,
            cParams = (short)parameters.Length, lprgelemdescParam = elems,
            elemdescFunc = new ELEMDESC { tdesc = new TYPEDESC { vt = (short)resultType } }
        };
        type.AddFuncDesc(index, ref function);
        // 同一屬性的名稱只設定一次；PROPERTYPUT 最後一個參數沒有名稱。
        if (kind != INVOKEKIND.INVOKE_PROPERTYPUT)
            type.SetFuncAndParamNames(index, new[] { name }.Concat(parameters.Select(p => p.Name)).ToArray(), (uint)parameters.Length + 1);
    }
    finally { foreach (var allocation in allocations) Marshal.FreeCoTaskMem(allocation); }
}

internal static class Native
{
    [DllImport("oleaut32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    internal static extern int CreateTypeLib2(SYSKIND kind, string filename, out ICreateTypeLib library);
}

[ComImport, Guid("0002040F-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface ICreateTypeLib
{
    void CreateTypeInfo([MarshalAs(UnmanagedType.LPWStr)] string name, TYPEKIND kind, out ICreateTypeInfo info);
    void SetName([MarshalAs(UnmanagedType.LPWStr)] string name);
    void SetVersion(ushort major, ushort minor);
    void SetGuid([In, MarshalAs(UnmanagedType.LPStruct)] Guid guid);
    void SetDocString([MarshalAs(UnmanagedType.LPWStr)] string description);
    void SetHelpFileName([MarshalAs(UnmanagedType.LPWStr)] string filename);
    void SetHelpContext(uint context);
    void SetLcid(uint lcid);
    void SetLibFlags(uint flags);
    void SaveAllChanges();
}

[ComImport, Guid("00020405-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface ICreateTypeInfo
{
    void SetGuid([In, MarshalAs(UnmanagedType.LPStruct)] Guid guid);
    void SetTypeFlags(uint flags);
    void SetDocString([MarshalAs(UnmanagedType.LPWStr)] string text);
    void SetHelpContext(uint context);
    void SetVersion(ushort major, ushort minor);
    void AddRefTypeInfo(ITypeInfo info, out uint reference);
    void AddFuncDesc(uint index, ref FUNCDESC function);
    void AddImplType(uint index, uint reference);
    void SetImplTypeFlags(uint index, IMPLTYPEFLAGS flags);
    void SetAlignment(ushort alignment);
    void SetSchema([MarshalAs(UnmanagedType.LPWStr)] string schema);
    void AddVarDesc(uint index, IntPtr descriptor);
    void SetFuncAndParamNames(uint index, [MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.LPWStr, SizeParamIndex = 2)] string[] names, uint count);
    void SetVarName(uint index, [MarshalAs(UnmanagedType.LPWStr)] string name);
    void SetTypeDescAlias(IntPtr descriptor);
    void DefineFuncAsDllEntry(uint index, [MarshalAs(UnmanagedType.LPWStr)] string dll, [MarshalAs(UnmanagedType.LPWStr)] string procedure);
    void SetFuncDocString(uint index, [MarshalAs(UnmanagedType.LPWStr)] string text);
    void SetVarDocString(uint index, [MarshalAs(UnmanagedType.LPWStr)] string text);
    void SetFuncHelpContext(uint index, uint context);
    void SetVarHelpContext(uint index, uint context);
    void SetMops(uint index, [MarshalAs(UnmanagedType.BStr)] string mops);
    void SetTypeIdldesc(IntPtr descriptor);
    void LayOut();
}
