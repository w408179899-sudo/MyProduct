using System.Collections.Immutable;
using Smart.Adapters.Dma;
using Smart.Contracts;
using Smart.Data;
using Smart.Hosting.Windows;
using Xunit;
namespace SampleProject.Tests;

public sealed class ConnectionSnapshotLifetimeTests
{
    private sealed class Transport : IProcessMemoryTransport, IMemoryConnectionLifecycle
    {
        public string DeviceId => "device";
        public string ConnectionId => "connection";
        public bool IsConnected { get; private set; } = true;
        public event Action? Disconnected;
        public int Subscriptions => Disconnected?.GetInvocationList().Length ?? 0;
        public void Exit() { IsConnected = false; Disconnected?.Invoke(); }
        public void Dispose() => Exit();
        public IReadOnlyList<ProcessBinding> ListProcesses(string? module = null) => [];
        public ProcessBinding GetProcess(int pid, string module) => new(pid, "process", "instance", 4096);
        public ImmutableArray<MemoryBlock> ReadBatch(int pid, IReadOnlyList<MemoryReadRequest> reads) => [];
    }
    private sealed class Connection : IAsyncDisposable
    {
        public int Closes;
        public ValueTask DisposeAsync() { Closes++; return ValueTask.CompletedTask; }
    }
    private sealed class Reader : IRawChannelReader<int, string>
    {
        public bool Fail;
        public ValueTask<RawRead<int>> CaptureAsync(CaptureContext context, string partition, CancellationToken token) =>
            ValueTask.FromResult(Fail ? RawRead<int>.Failed("temporary read failure") : RawRead<int>.Complete(0));
    }
    private static (SnapshotSession Session, SnapshotChannel<int, string> Channel, Reader Raw) Create(string account)
    {
        var raw = new Reader(); var catalog = new SnapshotCatalog();
        var channel = catalog.Register<int, string, int>("values", raw, new ReplaceMerger<int>(_ => true), SnapshotMergePolicy.Replace, TimeSpan.Zero);
        catalog.Seal();
        return (new SnapshotProvider(catalog).OpenSession(new("device", "connection", account, "worker", 1, "process", "module")), channel, raw);
    }
    [Fact] public async Task OrdinaryReadFailureHoldsSnapshotButWorkerExitInvalidatesAllSharingAccounts()
    {
        var transport = new Transport(); var first = Create("one"); var second = Create("two");
        await using var a = first.Session; await using var b = second.Session;
        await using var scopeA = new ConnectionSnapshotLifetime(transport, new Connection(), a);
        await using var scopeB = new ConnectionSnapshotLifetime(transport, new Connection(), b);
        var published = await a.Reader.ReadAsync(first.Channel, "p");
        await b.Reader.ReadAsync(second.Channel, "p");
        first.Raw.Fail = true;
        Assert.Same(published, await a.Reader.ReadAsync(first.Channel, "p"));
        transport.Exit();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => a.Reader.ReadAsync(first.Channel, "p").AsTask());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => b.Reader.ReadAsync(second.Channel, "p").AsTask());
    }
    [Fact] public async Task ExitBeforeSubscriptionCannotLeaveAUsableSnapshotSession()
    {
        var transport = new Transport(); transport.Exit(); var state = Create("one");
        await using var session = state.Session;
        await using var scope = new ConnectionSnapshotLifetime(transport, new Connection(), session);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => session.Reader.ReadAsync(state.Channel, "p").AsTask());
        Assert.Equal(0, session.Metrics.Captures);
    }
    [Fact] public async Task ClosingOneScopeUnsubscribesOnlyThatAccount()
    {
        var transport = new Transport(); var a = Create("one"); var b = Create("two");
        await using var sessionA = a.Session; await using var sessionB = b.Session;
        var connection = new Connection();
        var scopeA = new ConnectionSnapshotLifetime(transport, connection, sessionA);
        await using var scopeB = new ConnectionSnapshotLifetime(transport, new Connection(), sessionB);
        Assert.Equal(2, transport.Subscriptions);
        await scopeA.DisposeAsync();
        Assert.Equal(1, transport.Subscriptions); Assert.Equal(1, connection.Closes);
        Assert.Equal(0, (await sessionB.Reader.ReadAsync(b.Channel, "p")).Value);
    }
}
