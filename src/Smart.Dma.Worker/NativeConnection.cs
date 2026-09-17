using System.Collections.Immutable;
using System.Diagnostics;
using System.Text.Json;
using Smart.Adapters.Dma;
using Smart.Hosting;
using Smart.Hosting.Windows;
using Smart.Runtime;

namespace Smart.Dma.Worker;

internal sealed class NativeConnection(VmmTransport transport, IDmaBindingGuard? binding,
    IDisposable? physicalLease) : IProcessMemoryTransport
{
    private long _lastVerification = Stopwatch.GetTimestamp();
    private bool _nativeClosed, _bindingClosed;
    public string DeviceId => transport.DeviceId;
    public string ConnectionId => transport.ConnectionId;
    internal bool UsesScatter => transport.UsesScatter;
    internal static IProcessMemoryTransport Open(WorkerStartup startup)
    {
        IDmaBindingGuard? guard = null; IDisposable? physical = null; VmmTransport? native = null;
        try
        {
            if (startup.ProfileJson is not null)
            {
                var settings = JsonSerializer.Deserialize<DmaSettings>(startup.ProfileJson) ?? throw new ArgumentException("Missing worker profile.");
                if (!StringComparer.OrdinalIgnoreCase.Equals(Path.GetFullPath(settings.LibraryPath), Path.GetFullPath(startup.LibraryPath)) ||
                    !StringComparer.OrdinalIgnoreCase.Equals(settings.DeviceUri.Trim(), startup.DeviceId) ||
                    !VmmTransport.CreateArguments(settings.DeviceUri.Trim(), settings.Arguments).SequenceEqual(startup.Arguments))
                    throw new ArgumentException("Worker initialization differs from its bound profile.");
                guard = new DmaBindingVerifier().Acquire(settings);
                if (guard is not null) physical = new InputLeaseRegistry(startup.LeaseDirectory).Acquire(guard.LeaseKey);
            }
            native = new(startup.LibraryPath, startup.DeviceId, startup.Arguments);
            guard?.Verify();
            return new NativeConnection(native, guard, physical);
        }
        catch
        {
            // If native close hangs, the parent job deadline terminates this worker while its leases stay held.
            native?.Dispose(); guard?.Dispose(); physical?.Dispose(); throw;
        }
    }
    private void Verify(bool force = false)
    {
        if (binding is null || !force && Stopwatch.GetElapsedTime(_lastVerification) < TimeSpan.FromSeconds(1)) return;
        binding.Verify(); _lastVerification = Stopwatch.GetTimestamp();
    }
    public IReadOnlyList<ProcessBinding> ListProcesses(string? requiredModule = null)
    { Verify(force: true); return transport.ListProcesses(requiredModule); }
    public ProcessBinding GetProcess(int pid, string module)
    { Verify(); return transport.GetProcess(pid, module); }
    public ImmutableArray<MemoryBlock> ReadBatch(int processId, IReadOnlyList<MemoryReadRequest> requests)
    { Verify(); return transport.ReadBatch(processId, requests); }
    public void Dispose()
    {
        if (!_nativeClosed) { transport.Dispose(); _nativeClosed = true; }
        if (!_bindingClosed) { binding?.Dispose(); _bindingClosed = true; }
        physicalLease?.Dispose();
    }
}
