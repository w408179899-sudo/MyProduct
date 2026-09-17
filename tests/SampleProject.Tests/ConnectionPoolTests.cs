using System.Collections.Immutable;
using Smart.Adapters.Dma;
using Smart.Hosting;
using Smart.Hosting.Windows;
using Smart.Runtime;
using Xunit;
namespace SampleProject.Tests;

public sealed class ConnectionPoolTests
{
    private sealed class Transport : IProcessMemoryTransport, IMemoryConnectionLifecycle
    {
        public string DeviceId => "device";
        public string ConnectionId { get; } = Guid.NewGuid().ToString("N");
        public bool IsConnected { get; private set; } = true;
        public event Action? Disconnected;
        public void Disconnect() { IsConnected = false; Disconnected?.Invoke(); }
        public int Disposals;
        public int CloseFailures;
        public void Dispose()
        {
            if (Interlocked.Decrement(ref CloseFailures) >= 0) throw new IOException("injected close failure");
            Interlocked.Increment(ref Disposals);
        }
        public ImmutableArray<MemoryBlock> ReadBatch(int processId, IReadOnlyList<MemoryReadRequest> reads) => [];
        public IReadOnlyList<ProcessBinding> ListProcesses(string? module = null) => [];
        public ProcessBinding GetProcess(int pid, string module) => new(pid, "process", "instance", 4096);
    }
    private static DmaSettings Settings => new(Path.Combine(Path.GetTempPath(), "vmm.dll"), "device", [], 1, "", "module");
    [Fact] public async Task AccountsShareConnectionAndStoppingOneDoesNotCloseOthersTransport()
    {
        var created = 0; var transport = new Transport();
        await using var pool = new VmmConnectionPool(new InputLeaseRegistry(), _ => { created++; return transport; });
        var first = await pool.AcquireAsync(Settings, default);
        var second = await pool.AcquireAsync(Settings with { ProcessId = 2 }, default);
        Assert.Same(first.Dispatcher, second.Dispatcher); Assert.Equal(1, created);
        await first.DisposeAsync(); Assert.Equal(0, transport.Disposals);
        Assert.True(second.IsCurrent);
        await second.DisposeAsync(); Assert.Equal(1, transport.Disposals);
    }
    [Fact] public async Task RetiredConnectionWaitsForOldScopesBeforeReopening()
    {
        var created = new List<Transport>();
        await using var pool = new VmmConnectionPool(new InputLeaseRegistry(), _ => { var transport = new Transport(); created.Add(transport); return transport; });
        var first = await pool.AcquireAsync(Settings, default);
        var second = await pool.AcquireAsync(Settings, default);
        first.Retire(); Assert.False(second.IsCurrent);
        var next = pool.AcquireAsync(Settings, default).AsTask();
        await first.DisposeAsync(); Assert.False(next.IsCompleted);
        await second.DisposeAsync();
        await using var replacement = await next.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(2, created.Count); Assert.NotEqual(first.Transport.ConnectionId, replacement.Transport.ConnectionId);
    }
    [Fact] public async Task ConflictingConnectionSettingsCannotSilentlyReuseDevice()
    {
        await using var pool = new VmmConnectionPool(new InputLeaseRegistry(), _ => new Transport());
        await using var lease = await pool.AcquireAsync(Settings, default);
        await Assert.ThrowsAsync<ArgumentException>(() => pool.AcquireAsync(Settings with { Arguments = ["-norefresh"] }, default).AsTask());
    }
    [Fact] public async Task DisconnectedTransportWaitsForEveryOldScopeBeforeOpeningReplacement()
    {
        var created = new List<Transport>();
        var leases = new InputLeaseRegistry();
        await using var pool = new VmmConnectionPool(leases, _ => { var transport = new Transport(); created.Add(transport); return transport; });
        await using var first = await pool.AcquireAsync(Settings, default);
        await using var second = await pool.AcquireAsync(Settings, default);
        created[0].Disconnect();
        Assert.False(first.IsCurrent); Assert.False(second.IsCurrent);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var waiting = pool.AcquireAsync(Settings, timeout.Token).AsTask();
        Assert.False(waiting.IsCompleted); Assert.Single(created);
        Assert.Throws<InvalidOperationException>(() => leases.Acquire("dma:" + Settings.DeviceUri));
        await first.DisposeAsync();
        Assert.False(waiting.IsCompleted); Assert.Equal(0, created[0].Disposals);
        await second.DisposeAsync();
        await using var replacement = await waiting.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(2, created.Count); Assert.Equal(1, created[0].Disposals);
        Assert.True(replacement.IsCurrent); Assert.NotSame(first.Dispatcher, replacement.Dispatcher);
        Assert.NotEqual(first.Transport.ConnectionId, replacement.Transport.ConnectionId);
    }
    [Theory]
    [InlineData("worker")]
    [InlineData("binding")]
    [InlineData("remove-binding")]
    public async Task WorkerOrBindingConflictsCannotReuseAnExistingConnection(string conflict)
    {
        var settings = Settings with { DeviceUri = "fpga://ft601=1,devindex=0" };
        settings = settings with { Binding = DmaBindingPolicy.Capture(settings.DeviceUri, new("FTD3XX.dll", new('A', 64),
            [new(0, 0, new(601, 0x0403601F, 10, "unique-board", "FT601"))])) };
        var created = 0;
        await using var pool = new VmmConnectionPool(new InputLeaseRegistry(), _ => { created++; return new Transport(); });
        await using var lease = await pool.AcquireAsync(settings, default);
        var conflicting = conflict switch
        {
            "worker" => settings with { Worker = settings.Worker with { OperationTimeoutMs = 6000 } },
            "binding" => settings with { Binding = settings.Binding! with { DriverSha256 = new('B', 64) } },
            _ => settings with { Binding = null }
        };
        await Assert.ThrowsAsync<ArgumentException>(() => pool.AcquireAsync(conflicting, default).AsTask());
        Assert.Equal(1, created); Assert.True(lease.IsCurrent);
    }
    [Fact] public async Task NullInjectedFactoryFailsWithoutNativeFallbackAndReleasesOwnership()
    {
        var leases = new InputLeaseRegistry(); var created = 0;
        var settings = Settings with { LibraryPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "missing-vmm.dll") };
        await using var pool = new VmmConnectionPool(leases, _ => ++created == 1 ? null! : new Transport());
        var error = await Assert.ThrowsAsync<ArgumentException>(() => pool.AcquireAsync(settings, default).AsTask());
        Assert.Contains("Connection factory returned no transport", error.Message); Assert.Equal(1, created);
        using (leases.Acquire("dma:" + settings.DeviceUri)) { }
        await using var replacement = await pool.AcquireAsync(settings, default);
        Assert.Equal(2, created); Assert.True(replacement.IsCurrent);
    }
    [Fact] public async Task BlockedInitializationDoesNotBlockAnotherDevicesAcquireOrRelease()
    {
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var pool = new VmmConnectionPool(new InputLeaseRegistry(), settings =>
        {
            if (settings.DeviceUri == "slow") { entered.TrySetResult(); release.Wait(TimeSpan.FromSeconds(5)); }
            return new Transport();
        });
        var slow = pool.AcquireAsync(Settings with { DeviceUri = "slow" }, default).AsTask();
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            var other = await pool.AcquireAsync(Settings, default).AsTask().WaitAsync(TimeSpan.FromSeconds(2));
            await other.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
            Assert.False(slow.IsCompleted);
        }
        finally { release.Set(); await (await slow).DisposeAsync(); }
    }
    [Fact] public async Task FailedCloseRetainsOwnershipAndRetryReopensAfterSuccessfulCleanup()
    {
        var failed = new Transport { CloseFailures = 1 }; var count = 0;
        await using var pool = new VmmConnectionPool(new InputLeaseRegistry(), _ => ++count == 1 ? failed : new Transport());
        var lease = await pool.AcquireAsync(Settings, default);
        await Assert.ThrowsAsync<IOException>(() => lease.DisposeAsync().AsTask());
        Assert.False(lease.IsCurrent);
        var waiting = pool.AcquireAsync(Settings, default).AsTask();
        Assert.False(waiting.IsCompleted);
        await lease.DisposeAsync(); await lease.DisposeAsync();
        await using var next = await waiting.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(1, failed.Disposals); Assert.Equal(2, count);
    }
}
