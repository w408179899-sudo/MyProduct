using Smart.Adapters.Dma;
using Smart.Runtime;
namespace Smart.Hosting.Windows;

// The dictionary lock protects metadata only. Native initialization and close serialize per device.
public sealed class VmmConnectionPool(InputLeaseRegistry leases, Func<DmaSettings, IProcessMemoryTransport>? connect = null) : IAsyncDisposable
{
    internal sealed class Entry(IProcessMemoryTransport transport, IDisposable physicalLease)
    {
        public IProcessMemoryTransport Transport { get; } = transport;
        public DmaDispatcher Dispatcher { get; } = new(transport);
        public IDisposable PhysicalLease { get; } = physicalLease;
        public TaskCompletionSource Closed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Users;
        public volatile bool Retired;
    }
    internal sealed class Slot(string signature)
    {
        public string Signature { get; } = signature;
        public SemaphoreSlim Gate { get; } = new(1, 1);
        public Entry? Entry;
        public int Holders;
    }
    public sealed class ConnectionLease : IAsyncDisposable
    {
        private readonly VmmConnectionPool _pool;
        private readonly string _key;
        private readonly Slot _slot;
        private readonly Entry _entry;
        private readonly object _sync = new();
        private Task? _release;
        internal bool Counted = true;
        internal ConnectionLease(VmmConnectionPool pool, string key, Slot slot, Entry entry)
        { _pool = pool; _key = key; _slot = slot; _entry = entry; }
        public IProcessMemoryTransport Transport => _entry.Transport;
        public DmaDispatcher Dispatcher => _entry.Dispatcher;
        public bool IsCurrent => !_entry.Retired && (_entry.Transport is not IMemoryConnectionLifecycle lifecycle || lifecycle.IsConnected);
        public void Retire() => _entry.Retired = true;
        public ValueTask DisposeAsync()
        {
            lock (_sync)
            {
                if (_release is null || _release.IsFaulted || _release.IsCanceled)
                    _release = _pool.ReleaseAsync(_key, _slot, _entry, this);
                return new(_release);
            }
        }
    }
    private readonly object _sync = new();
    private readonly Dictionary<string, Slot> _slots = new(StringComparer.OrdinalIgnoreCase);
    private readonly InputLeaseRegistry _localOwnership = new();
    private bool _disposed;
    public async ValueTask<ConnectionLease> AcquireAsync(DmaSettings settings, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(settings);
        DmaBindingPolicy.ValidateSettings(settings); // Managed DTO checks only; driver calls belong to the worker.
        ArgumentNullException.ThrowIfNull(settings.Worker);
        settings.Worker.Validate();
        var key = settings.DeviceUri.Trim();
        var signature = System.Text.Json.JsonSerializer.Serialize(new { Path = Path.GetFullPath(settings.LibraryPath), settings.Arguments, settings.Binding, settings.Worker });
        Slot slot;
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!_slots.TryGetValue(key, out slot!)) { slot = new(signature); _slots.Add(key, slot); }
            if (slot.Signature != signature) throw new ArgumentException("Accounts sharing a DMA device must use identical library and connection arguments.");
            slot.Holders++;
        }
        try
        {
            while (true)
            {
                Task? closing = null;
                await slot.Gate.WaitAsync(token).ConfigureAwait(false);
                try
                {
                    if (slot.Entry is { } existing)
                    {
                        if (existing.Transport is IMemoryConnectionLifecycle { IsConnected: false }) existing.Retired = true;
                        if (existing.Retired) closing = existing.Closed.Task;
                        else { existing.Users++; return new(this, key, slot, existing); }
                    }
                    else
                    {
                        // Real file leases stay inside the worker, including when native initialization/close hangs.
                        var physical = (connect is null ? _localOwnership : leases).Acquire("dma:" + key);
                        IProcessMemoryTransport transport;
                        try
                        {
                            transport = await Task.Run(() => connect is not null
                                ? connect(settings) ?? throw new ArgumentException("Connection factory returned no transport.")
                                : new IsolatedVmmTransport(settings.LibraryPath, key, VmmTransport.CreateArguments(key, settings.Arguments), new()
                                {
                                    WorkerPath = settings.Worker.WorkerPath,
                                    StartupTimeoutMs = settings.Worker.StartupTimeoutMs,
                                    OperationTimeoutMs = settings.Worker.OperationTimeoutMs,
                                    ShutdownTimeoutMs = settings.Worker.ShutdownTimeoutMs,
                                    LeaseDirectory = leases.LeaseDirectory ?? InputLeaseRegistry.SharedDirectory,
                                    ProfileJson = System.Text.Json.JsonSerializer.Serialize(settings)
                                }, token), token).ConfigureAwait(false);
                        }
                        catch { physical.Dispose(); throw; }
                        // Ownership must reach the caller after native initialization, even if cancellation arrived meanwhile.
                        // The hardware factory then checks cancellation and runs its retryable cleanup.
                        var entry = new Entry(transport, physical) { Users = 1 }; slot.Entry = entry;
                        return new(this, key, slot, entry);
                    }
                }
                finally { slot.Gate.Release(); }
                await closing!.WaitAsync(token).ConfigureAwait(false);
            }
        }
        catch { ReleaseHolder(key, slot); throw; }
    }
    private async Task ReleaseAsync(string key, Slot slot, Entry entry, ConnectionLease lease)
    {
        await slot.Gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (lease.Counted) { entry.Users--; lease.Counted = false; }
            if (entry.Users == 0)
            {
                entry.Retired = true;
                await entry.Dispatcher.DisposeAsync().ConfigureAwait(false);
                entry.PhysicalLease.Dispose();
                slot.Entry = null; entry.Closed.TrySetResult();
            }
        }
        finally { slot.Gate.Release(); }
        // A failed close retains this holder and the physical lease until a successful retry.
        ReleaseHolder(key, slot);
    }
    private void ReleaseHolder(string key, Slot slot)
    {
        lock (_sync) { if (--slot.Holders == 0) _slots.Remove(key); }
    }
    public ValueTask DisposeAsync()
    {
        lock (_sync)
        {
            if (_slots.Count != 0) throw new InvalidOperationException("Stop and dispose account sessions before disposing the connection pool.");
            _disposed = true;
        }
        return ValueTask.CompletedTask;
    }
}
