using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
namespace Smart.Hosting.Windows;

// FT_CreateDeviceInfoList + FT_GetDeviceInfoDetail enumerate the same zero-based index used by
// LeechCore's FT_Create(index, FT_OPEN_BY_INDEX). This class never calls FT_Create or opens a device.
public sealed class D3xxDeviceInventory : ID3xxInventorySource
{
    private static readonly object EnumerationGate = new();
    private readonly object _sync = new();
    private readonly string _directory, _fileName, _hash;
    private readonly CreateDeviceInfoList _createList;
    private readonly GetDeviceInfoDetail _detail;
    private nint _library;
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate uint CreateDeviceInfoList(out uint count);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate uint GetDeviceInfoDetail(uint index,
        out uint flags, out uint type, out uint id, out uint location, [Out] byte[] serial, [Out] byte[] description, out nint handle);

    public D3xxDeviceInventory(string vmmLibraryPath, string driverFileName)
    {
        if (!OperatingSystem.IsWindows() || !Environment.Is64BitProcess) throw new PlatformNotSupportedException("D3XX binding requires Windows x64.");
        if (!Path.IsPathFullyQualified(vmmLibraryPath) || driverFileName is not ("FTD3XX.dll" or "FTD3XXWU.dll"))
            throw new DmaBindingException("Select an absolute VMM path and one supported D3XX driver.");
        _directory = Path.GetDirectoryName(Path.GetFullPath(vmmLibraryPath))!; _fileName = driverFileName;
        var path = Path.Combine(_directory, _fileName);
        if (!File.Exists(path)) throw new DmaBindingException("The selected D3XX driver must exist beside the configured VMM library: " + path);
        lock (EnumerationGate)
        {
            RejectAlternateDriver();
            var existing = GetModuleHandleW(_fileName);
            if (existing != 0 && !SamePath(ModulePath(existing), path))
                throw new DmaBindingException("Another D3XX library with the same filename is already loaded from a different path.");
            _library = NativeLibrary.Load(path);
            try
            {
                if (!SamePath(ModulePath(_library), path)) throw new DmaBindingException("The loaded D3XX path differs from the selected library.");
                using var stream = File.OpenRead(path); _hash = Convert.ToHexString(SHA256.HashData(stream));
                _createList = Marshal.GetDelegateForFunctionPointer<CreateDeviceInfoList>(NativeLibrary.GetExport(_library, "FT_CreateDeviceInfoList"));
                _detail = Marshal.GetDelegateForFunctionPointer<GetDeviceInfoDetail>(NativeLibrary.GetExport(_library, "FT_GetDeviceInfoDetail"));
            }
            catch { NativeLibrary.Free(_library); _library = 0; throw; }
        }
    }
    public D3xxInventory Read()
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_library == 0, this);
            lock (EnumerationGate)
            {
                RejectAlternateDriver();
                Check(_createList(out var count), "FT_CreateDeviceInfoList");
                if (count is < 1 or > 64) throw new DmaBindingException("D3XX enumeration is empty or exceeds the 64-device limit.");
                var devices = new List<D3xxDevice>((int)count);
                for (uint index = 0; index < count; index++)
                {
                    var serial = new byte[256]; var description = new byte[256];
                    Check(_detail(index, out var flags, out var type, out var id, out var location, serial, description, out _), "FT_GetDeviceInfoDetail");
                    devices.Add(new((int)index, flags, new(type, id, location, Decode(serial), Decode(description))));
                }
                return new(_fileName, _hash, devices.ToArray());
            }
        }
    }
    private void RejectAlternateDriver()
    {
        var alternate = _fileName == "FTD3XX.dll" ? "FTD3XXWU.dll" : "FTD3XX.dll";
        if (File.Exists(Path.Combine(_directory, alternate)) || GetModuleHandleW(alternate) != 0)
            throw new DmaBindingException("Protected binding requires one unambiguous D3XX driver; an alternate driver could change LeechCore's index mapping: " + alternate);
        // Match LeechCore's initial bare-filename loader search, not only its same-directory fallback.
        if (NativeLibrary.TryLoad(alternate, out var module))
        {
            NativeLibrary.Free(module);
            throw new DmaBindingException("An alternate D3XX driver is available on the loader search path; its device index mapping is not verified: " + alternate);
        }
    }
    private static void Check(uint status, string operation)
    {
        if (status != 0) throw new DmaBindingException(operation + " failed with D3XX status " + status + ". Binding cannot be verified.");
    }
    private static string Decode(byte[] bytes)
    {
        var end = Array.IndexOf(bytes, (byte)0);
        if (end < 0 || end > 128) throw new DmaBindingException("D3XX returned an invalid identity string.");
        return Encoding.ASCII.GetString(bytes, 0, end);
    }
    private static bool SamePath(string left, string right) => StringComparer.OrdinalIgnoreCase.Equals(Path.GetFullPath(left), Path.GetFullPath(right));
    private static string ModulePath(nint module)
    {
        var path = new StringBuilder(32768);
        if (GetModuleFileNameW(module, path, path.Capacity) is 0 or >= 32768) throw new DmaBindingException("Cannot identify the loaded D3XX library path.");
        return path.ToString();
    }
    public void Dispose()
    {
        lock (_sync) { if (_library == 0) return; NativeLibrary.Free(_library); _library = 0; }
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern nint GetModuleHandleW(string moduleName);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern uint GetModuleFileNameW(nint module, StringBuilder fileName, int size);
}
