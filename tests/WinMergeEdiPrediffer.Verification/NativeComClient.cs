using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace WinMergeEdiPrediffer.Verification;

/// <summary>以 WinMerge 的 LoadTypeLib → DllGetClassObject → IDispatch 流程測試，無須 COM 註冊。</summary>
internal sealed class NativeComClient : IDisposable
{
    private readonly IntPtr dispatch;
    private const int VariantSize = 24;

    public NativeComClient(string path)
    {
        Marshal.ThrowExceptionForHR(LoadTypeLibEx(path, 2, out var library));
        Guid? classId = null;
        for (var i = 0; i < library.GetTypeInfoCount(); i++)
        {
            library.GetTypeInfoType(i, out var kind);
            library.GetDocumentation(i, out var name, out _, out _, out _);
            if (kind != TYPEKIND.TKIND_COCLASS || name != "WinMergeScript") continue;
            library.GetTypeInfo(i, out var info);
            info.GetTypeAttr(out var attribute);
            try { classId = Marshal.PtrToStructure<TYPEATTR>(attribute).guid; }
            finally { info.ReleaseTypeAttr(attribute); }
        }
        if (classId is null) throw new InvalidOperationException("TLB 沒有 WinMergeScript coclass。");
        var module = NativeLibrary.Load(path);
        var getFactory = Marshal.GetDelegateForFunctionPointer<GetClassObject>(NativeLibrary.GetExport(module, "DllGetClassObject"));
        var iidFactory = new Guid("00000001-0000-0000-C000-000000000046");
        var clsid = classId.Value;
        Console.WriteLine($"COM coclass {clsid} from {Path.GetFileName(path)}");
        Marshal.ThrowExceptionForHR(getFactory(ref clsid, ref iidFactory, out var factory));
        try
        {
            var iidDispatch = new Guid("00020400-0000-0000-C000-000000000046");
            Marshal.ThrowExceptionForHR(Method<CreateInstance>(factory, 3)(factory, IntPtr.Zero, ref iidDispatch, out dispatch));
        }
        finally { Marshal.Release(factory); }
    }

    public string[] MemberNames()
    {
        Marshal.ThrowExceptionForHR(Method<GetTypeInfo>(dispatch, 4)(dispatch, 0, 0, out var pointer));
        try
        {
            var info = (ITypeInfo)Marshal.GetObjectForIUnknown(pointer);
            info.GetTypeAttr(out var attribute);
            try
            {
                var count = Marshal.PtrToStructure<TYPEATTR>(attribute).cFuncs;
                var names = new List<string>();
                for (var i = 0; i < count; i++)
                {
                    info.GetFuncDesc(i, out var function);
                    try
                    {
                        var description = Marshal.PtrToStructure<FUNCDESC>(function);
                        var buffer = new string[1];
                        info.GetNames(description.memid, buffer, 1, out var actual);
                        if (actual > 0) names.Add(buffer[0]);
                    }
                    finally { info.ReleaseFuncDesc(function); }
                }
                return names.ToArray();
            }
            finally { info.ReleaseTypeAttr(attribute); }
        }
        finally { Marshal.Release(pointer); }
    }

    public object? Property(string name)
    {
        var parameters = new DISPPARAMS();
        return Invoke(name, 2, ref parameters);
    }

    public void SetProperty(string name, object value)
    {
        var argument = Marshal.AllocCoTaskMem(VariantSize);
        var namedArgument = Marshal.AllocCoTaskMem(4);
        Marshal.Copy(new byte[VariantSize], 0, argument, VariantSize);
        Marshal.WriteInt32(namedArgument, -3); // DISPID_PROPERTYPUT
        try
        {
            Marshal.GetNativeVariantForObject(value, argument);
            var parameters = new DISPPARAMS { cArgs = 1, rgvarg = argument, cNamedArgs = 1, rgdispidNamedArgs = namedArgument };
            _ = Invoke(name, 4, ref parameters);
        }
        finally
        {
            VariantClear(argument);
            Marshal.FreeCoTaskMem(argument);
            Marshal.FreeCoTaskMem(namedArgument);
        }
    }

    public (string Text, int Size, bool Changed, bool Handled) Prediff(string text, bool wrapVariants = false, string? fullPath = null)
    {
        if (fullPath is not null) SetProperty("PluginVariables", fullPath);
        var buffer = Marshal.AllocCoTaskMem(IntPtr.Size);
        var size = Marshal.AllocCoTaskMem(4);
        var changed = Marshal.AllocCoTaskMem(2);
        var arguments = Marshal.AllocCoTaskMem(VariantSize * 3);
        var values = wrapVariants ? Marshal.AllocCoTaskMem(VariantSize * 3) : IntPtr.Zero;
        if (values != IntPtr.Zero) Marshal.Copy(new byte[VariantSize * 3], 0, values, VariantSize * 3);
        Marshal.WriteIntPtr(buffer, Marshal.StringToBSTR(text));
        Marshal.WriteInt32(size, text.Length);
        Marshal.WriteInt16(changed, 0);
        try
        {
            WriteByRef(arguments, 0x400B, changed);
            WriteByRef(arguments + VariantSize, 0x4003, size);
            WriteByRef(arguments + VariantSize * 2, 0x4008, buffer);
            if (wrapVariants)
                for (var i = 0; i < 3; i++)
                {
                    Marshal.ThrowExceptionForHR(VariantCopyInd(values + VariantSize * i, arguments + VariantSize * i));
                    WriteByRef(arguments + VariantSize * i, 0x400C, values + VariantSize * i);
                }
            var parameters = new DISPPARAMS { cArgs = 3, rgvarg = arguments };
            var handled = (bool)(Invoke("PrediffBufferW", 1, ref parameters) ?? false);
            if (wrapVariants)
            {
                // 重現 WinMerge 的 MoveVariantValue：回寫數值並移轉 BSTR 所有權。
                Marshal.WriteInt16(changed, Marshal.ReadInt16(values, 8));
                Marshal.WriteInt32(size, Marshal.ReadInt32(values + VariantSize, 8));
                Marshal.FreeBSTR(Marshal.ReadIntPtr(buffer));
                Marshal.WriteIntPtr(buffer, Marshal.ReadIntPtr(values + VariantSize * 2, 8));
                Marshal.WriteInt16(values + VariantSize * 2, 0);
            }
            return (Marshal.PtrToStringBSTR(Marshal.ReadIntPtr(buffer)), Marshal.ReadInt32(size), Marshal.ReadInt16(changed) != 0, handled);
        }
        finally
        {
            Marshal.FreeBSTR(Marshal.ReadIntPtr(buffer));
            if (values != IntPtr.Zero)
            {
                for (var i = 0; i < 3; i++) VariantClear(values + VariantSize * i);
                Marshal.FreeCoTaskMem(values);
            }
            foreach (var pointer in new[] { buffer, size, changed, arguments }) Marshal.FreeCoTaskMem(pointer);
        }
    }

    private object? Invoke(string name, ushort flags, ref DISPPARAMS parameters)
    {
        var names = Marshal.AllocCoTaskMem(IntPtr.Size);
        var id = Marshal.AllocCoTaskMem(4);
        var result = Marshal.AllocCoTaskMem(VariantSize);
        var exception = Marshal.AllocCoTaskMem(Marshal.SizeOf<EXCEPINFO>());
        var namePointer = Marshal.StringToCoTaskMemUni(name);
        Marshal.WriteIntPtr(names, namePointer);
        Marshal.Copy(new byte[VariantSize], 0, result, VariantSize);
        Marshal.Copy(new byte[Marshal.SizeOf<EXCEPINFO>()], 0, exception, Marshal.SizeOf<EXCEPINFO>());
        var empty = Guid.Empty;
        try
        {
            Marshal.ThrowExceptionForHR(Method<GetIds>(dispatch, 5)(dispatch, ref empty, names, 1, 0, id));
            var hr = Method<DispatchInvoke>(dispatch, 6)(dispatch, Marshal.ReadInt32(id), ref empty, 0, flags, ref parameters, result, exception, IntPtr.Zero);
            if (hr < 0)
            {
                var error = Marshal.PtrToStructure<EXCEPINFO>(exception);
                throw new InvalidOperationException($"COM {name}: 0x{hr:X8} {error.bstrDescription}");
            }
            return Marshal.GetObjectForNativeVariant(result);
        }
        finally
        {
            VariantClear(result);
            Marshal.DestroyStructure<EXCEPINFO>(exception);
            foreach (var pointer in new[] { namePointer, names, id, result, exception }) Marshal.FreeCoTaskMem(pointer);
        }
    }

    private static void WriteByRef(IntPtr variant, ushort type, IntPtr value)
    {
        Marshal.Copy(new byte[VariantSize], 0, variant, VariantSize);
        Marshal.WriteInt16(variant, (short)type);
        Marshal.WriteIntPtr(variant, 8, value);
    }

    private static T Method<T>(IntPtr instance, int index) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(instance), index * IntPtr.Size));
    public void Dispose() => Marshal.Release(dispatch);

    [DllImport("oleaut32.dll", CharSet = CharSet.Unicode)] private static extern int LoadTypeLibEx(string file, int mode, out ITypeLib library);
    [DllImport("oleaut32.dll")] private static extern int VariantClear(IntPtr variant);
    [DllImport("oleaut32.dll")] private static extern int VariantCopyInd(IntPtr destination, IntPtr source);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetClassObject(ref Guid clsid, ref Guid iid, out IntPtr factory);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int CreateInstance(IntPtr factory, IntPtr outer, ref Guid iid, out IntPtr instance);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetTypeInfo(IntPtr instance, uint index, uint lcid, out IntPtr info);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetIds(IntPtr instance, ref Guid iid, IntPtr names, uint count, uint lcid, IntPtr ids);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int DispatchInvoke(IntPtr instance, int id, ref Guid iid, uint lcid, ushort flags, ref DISPPARAMS parameters, IntPtr result, IntPtr exception, IntPtr argumentError);
}
