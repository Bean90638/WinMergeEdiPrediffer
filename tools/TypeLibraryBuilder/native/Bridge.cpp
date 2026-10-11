// Minimal x64 Win32 declarations: this loader needs no CRT or Windows SDK.
// All EDI rules and COM objects remain in the C# component below this DLL.
using Dword = unsigned long;
using Bool = int;
using Handle = void*;
using HResult = long;
struct Guid { unsigned long a; unsigned short b, c; unsigned char d[8]; };
struct InitOnce { void* value; };
using InitCallback = Bool (__stdcall*)(InitOnce*, void*, void**);
using Factory = HResult (__stdcall*)(const Guid*, const Guid*, void**);
using Procedure = void (__stdcall*)();

extern "C" {
__declspec(dllimport) Dword __stdcall GetModuleFileNameW(Handle, wchar_t*, Dword);
__declspec(dllimport) Handle __stdcall LoadLibraryExW(const wchar_t*, Handle, Dword);
__declspec(dllimport) Procedure __stdcall GetProcAddress(Handle, const char*);
__declspec(dllimport) Handle __stdcall GetProcessHeap();
__declspec(dllimport) void* __stdcall HeapAlloc(Handle, Dword, unsigned __int64);
__declspec(dllimport) Bool __stdcall HeapFree(Handle, Dword, void*);
__declspec(dllimport) Dword __stdcall GetLastError();
__declspec(dllimport) void __stdcall SetLastError(Dword);
__declspec(dllimport) Bool __stdcall InitOnceExecuteOnce(InitOnce*, InitCallback, void*, void**);
extern unsigned char __ImageBase;
}

static InitOnce initialization = {};
static Factory factory = nullptr;

static Bool __stdcall LoadComponent(InitOnce*, void*, void**)
{
    constexpr Dword capacity = 32768;
    auto path = static_cast<wchar_t*>(HeapAlloc(GetProcessHeap(), 0, capacity * sizeof(wchar_t)));
    if (!path) { SetLastError(8); return 0; } // ERROR_NOT_ENOUGH_MEMORY
    const auto length = GetModuleFileNameW(&__ImageBase, path, capacity);
    constexpr wchar_t suffix[] = L".comhost.dll";
    constexpr Dword suffixLength = sizeof(suffix) / sizeof(wchar_t) - 1;
    constexpr wchar_t component[] = L"\\WinMergeEdiPrediffer.Plugin.comhost.dll";
    constexpr Dword componentLength = sizeof(component) / sizeof(wchar_t) - 1;
    Dword error = 0;
    if (length <= suffixLength || length >= capacity || length - suffixLength + componentLength >= capacity)
        error = 123; // ERROR_INVALID_NAME
    else
    {
        for (Dword i = 0; i < suffixLength; ++i)
            if (path[length - suffixLength + i] != suffix[i]) error = 123;
        if (!error)
        {
            for (Dword i = 0; i <= componentLength; ++i) path[length - suffixLength + i] = component[i];
            // Full path plus restricted DLL search: component-local and system directories.
            auto module = LoadLibraryExW(path, nullptr, 0x100 | 0x1000);
            if (!module) error = GetLastError();
            else
            {
                factory = reinterpret_cast<Factory>(GetProcAddress(module, "DllGetClassObject"));
                if (!factory) error = GetLastError();
                // Keep the COM host loaded for the process lifetime, like .NET COM hosting.
            }
        }
    }
    HeapFree(GetProcessHeap(), 0, path);
    if (error) { SetLastError(error); return 0; }
    return 1;
}

extern "C" HResult __stdcall DllGetClassObject(const Guid* clsid, const Guid* iid, void** result)
{
    if (!result) return static_cast<HResult>(0x80004003UL); // E_POINTER
    *result = nullptr;
    if (!InitOnceExecuteOnce(&initialization, LoadComponent, nullptr, nullptr))
    {
        const auto error = GetLastError();
        return static_cast<HResult>(0x80070000UL | (error ? (error & 0xffffUL) : 1UL));
    }
    return factory(clsid, iid, result);
}

extern "C" HResult __stdcall DllCanUnloadNow() { return 1; } // S_FALSE
