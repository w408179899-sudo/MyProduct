using System.Collections.Immutable;
using System.Runtime.InteropServices;
namespace Smart.Adapters.Dma;

public sealed record ProcessBinding(int ProcessId, string Name, string ProcessIdentity, ulong ModuleBase, string ModuleFingerprint = "")
{
    public string ModuleIdentity => ModuleBase.ToString("X16") + ":" + ModuleFingerprint;
}
public sealed class TargetProcessUnavailableException(int pid) : IOException("Target process exited: " + pid);

public sealed partial class VmmTransport
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate bool PidListDelegate(IntPtr h, IntPtr pids, ref nuint count);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate bool ProcessInfoDelegate(IntPtr h, uint pid, IntPtr info, ref nuint bytes);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate ulong ModuleBaseDelegate(IntPtr h, uint pid, [MarshalAs(UnmanagedType.LPUTF8Str)] string name);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr ScatterInitializeDelegate(IntPtr h, uint pid, uint flags);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate bool ScatterPrepareDelegate(IntPtr h, ulong address, uint bytes);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate bool ScatterExecuteDelegate(IntPtr h);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate bool ScatterReadDelegate(IntPtr h, ulong address, uint bytes, [Out] byte[] buffer, out uint read);
    private PidListDelegate _pidList = null!;
    private ProcessInfoDelegate _processInfo = null!;
    private ModuleBaseDelegate _moduleBase = null!;
    private ScatterInitializeDelegate? _scatterInitialize;
    private ScatterPrepareDelegate _scatterPrepare = null!;
    private ScatterExecuteDelegate _scatterExecute = null!;
    private ScatterReadDelegate _scatterRead = null!;
    private CloseDelegate _scatterClose = null!;
    public bool UsesScatter => _scatterInitialize is not null;
    private T Export<T>(string name) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(_library, name));
    private void InitializeInspection()
    {
        _pidList = Export<PidListDelegate>("VMMDLL_PidList");
        _processInfo = Export<ProcessInfoDelegate>("VMMDLL_ProcessGetInformation");
        _moduleBase = Export<ModuleBaseDelegate>("VMMDLL_ProcessGetModuleBaseU");
        if (!NativeLibrary.TryGetExport(_library, "VMMDLL_Scatter_Initialize", out var initialize)) return;
        _scatterPrepare = Export<ScatterPrepareDelegate>("VMMDLL_Scatter_Prepare");
        _scatterExecute = Export<ScatterExecuteDelegate>("VMMDLL_Scatter_ExecuteRead");
        _scatterRead = Export<ScatterReadDelegate>("VMMDLL_Scatter_Read");
        _scatterClose = Export<CloseDelegate>("VMMDLL_Scatter_CloseHandle");
        _scatterInitialize = Marshal.GetDelegateForFunctionPointer<ScatterInitializeDelegate>(initialize);
    }
    public IReadOnlyList<ProcessBinding> ListProcesses(string? requiredModule = null)
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            nuint count = 0;
            if (!_pidList(_handle, IntPtr.Zero, ref count) || count is 0 or > 65536)
                throw new IOException("Cannot enumerate target processes.");
            var capacity = count;
            var memory = Marshal.AllocHGlobal(checked((int)capacity * 4));
            try
            {
                if (!_pidList(_handle, memory, ref count) || count > capacity) throw new IOException("Process list changed during enumeration.");
                var result = new List<ProcessBinding>();
                for (var index = 0; index < (int)count; index++)
                {
                    var pid = Marshal.ReadInt32(memory, index * 4);
                    if (pid <= 0) continue;
                    var process = InspectProcess(pid, requiredModule);
                    if (process is not null) result.Add(process);
                }
                return result;
            }
            finally { Marshal.FreeHGlobal(memory); }
        }
    }
    // Layout from MemProcFS PROCESS_INFORMATION v7, x64 C ABI. Validate version/size before offsets.
    private ProcessBinding? InspectProcess(int pid, string? module, bool requiredBinding = false)
    {
        const int capacity = 448;
        var data = new byte[capacity];
        BitConverter.GetBytes(0xc0ffee663df9301eUL).CopyTo(data, 0);
        BitConverter.GetBytes((ushort)7).CopyTo(data, 8);
        BitConverter.GetBytes((ushort)capacity).CopyTo(data, 10);
        var buffer = Marshal.AllocHGlobal(capacity);
        try
        {
            Marshal.Copy(data, 0, buffer, capacity);
            nuint size = capacity;
            if (!_processInfo(_handle, (uint)pid, buffer, ref size) || size < 160 || size > capacity) return null;
            Marshal.Copy(buffer, data, 0, capacity);
            if (BitConverter.ToUInt16(data, 8) != 7 || BitConverter.ToUInt32(data, 24) != pid || BitConverter.ToUInt32(data, 32) != 0) return null;
            var name = System.Text.Encoding.UTF8.GetString(data, 52, 64).TrimEnd('\0');
            var identity = $"{BitConverter.ToUInt64(data, 136):X16}:{BitConverter.ToUInt64(data, 120):X16}:{BitConverter.ToUInt64(data, 144):X16}";
            var address = module is null ? 0 : ValidateModuleBase(_moduleBase(_handle, (uint)pid, module), requiredBinding);
            var fingerprint = "";
            if (address != 0)
            {
                var header = new byte[4096];
                if (!_read(_handle, (uint)pid, address, header, 4096, out var read, 1) || read != 4096)
                    throw new IOException("Cannot verify module identity.");
                fingerprint = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(header));
            }
            return new(pid, name, identity, address, fingerprint);
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }
    internal static ulong ValidateModuleBase(ulong address, bool requiredBinding)
    {
        // ModuleBaseU reports zero on any lookup failure, not only a confirmed unload.
        // Diagnostic enumeration can show an unbound process; live bindings cannot publish this as identity.
        if (requiredBinding && address == 0) throw new IOException("Cannot verify required module identity.");
        return address;
    }
    public ProcessBinding GetProcess(int pid, string module)
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var process = InspectProcess(pid, module, requiredBinding: true);
            if (process is not null) return process;
            // Distinguish a confirmed process exit from a connection-wide inspection failure.
            nuint count = 0;
            if (!_pidList(_handle, IntPtr.Zero, ref count) || count > 65536) throw new IOException("Process enumeration failed.");
            var capacity = count;
            var memory = Marshal.AllocHGlobal(Math.Max(4, checked((int)capacity * 4)));
            try
            {
                if (!_pidList(_handle, memory, ref count) || count > capacity) throw new IOException("Process enumeration changed.");
                for (var i = 0; i < (int)count; i++)
                    if (Marshal.ReadInt32(memory, i * 4) == pid) throw new IOException("Process is listed but its identity cannot be read.");
                throw new TargetProcessUnavailableException(pid);
            }
            finally { Marshal.FreeHGlobal(memory); }
        }
    }
    private ImmutableArray<MemoryBlock> ReadScatter(int pid, IReadOnlyList<MemoryReadRequest> requests)
    {
        var scatter = _scatterInitialize!(_handle, (uint)pid, 1);
        if (scatter == IntPtr.Zero) throw new IOException("Scatter initialization failed.");
        try
        {
            var prepared = new bool[requests.Count];
            for (var i = 0; i < requests.Count; i++) prepared[i] = _scatterPrepare(scatter, requests[i].Address, (uint)requests[i].Length);
            _scatterExecute(scatter); // Individual read counts determine partial/complete validity.
            var result = ImmutableArray.CreateBuilder<MemoryBlock>(requests.Count);
            for (var i = 0; i < requests.Count; i++)
            {
                var request = requests[i];
                if (!prepared[i]) { result.Add(new(request.Address, [], false)); continue; }
                var buffer = new byte[request.Length];
                var success = _scatterRead(scatter, request.Address, (uint)request.Length, buffer, out var count);
                result.Add(MemoryBlock.FromNativeOwnedBuffer(request.Address, buffer, success, count));
            }
            return result.MoveToImmutable();
        }
        finally { _scatterClose(scatter); }
    }
}
