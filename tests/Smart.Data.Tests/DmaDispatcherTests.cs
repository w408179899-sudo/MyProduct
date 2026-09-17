using System.Collections.Immutable;
using Smart.Adapters.Dma;
using Smart.Data;
using Xunit;
namespace Smart.Data.Tests;

public sealed class DmaDispatcherTests
{
    private sealed class Transport : IMemoryTransport
    {
        public string DeviceId => "device"; public string ConnectionId => "connection";
        public int Calls; public int Concurrent; public int MaximumConcurrent; public bool Disposed;
        public ImmutableArray<MemoryBlock> ReadBatch(int processId, IReadOnlyList<MemoryReadRequest> requests)
        {
            var current = Interlocked.Increment(ref Concurrent);
            MaximumConcurrent = Math.Max(MaximumConcurrent, current);
            Interlocked.Increment(ref Calls);
            var blocks = requests.Select(x => new MemoryBlock(x.Address, ImmutableArray.Create<byte>(1, 2), x.Length == 2)).ToImmutableArray();
            Interlocked.Decrement(ref Concurrent);
            return blocks;
        }
        public void Dispose() => Disposed = true;
    }
    private static CaptureContext Context() => new(new("device", "connection", "account", "worker", 1, "start", "module"), 1);
    [Fact] public async Task NativeReadsAreSerializedAndTransportDisposesAfterDrain()
    {
        var transport = new Transport(); var dispatcher = new DmaDispatcher(transport, 4);
        var results = await Task.WhenAll(Enumerable.Range(0, 32).Select(_ =>
            dispatcher.ReadAsync(Context(), [new(0x1000, 2)], default).AsTask()));
        Assert.Equal(32, transport.Calls); Assert.Equal(1, transport.MaximumConcurrent);
        Assert.All(results, x => Assert.True(x[0].Complete));
        await dispatcher.DisposeAsync(); Assert.True(transport.Disposed);
    }
    [Fact] public async Task ForeignConnectionAndOversizedBatchAreRejected()
    {
        var transport = new Transport(); await using var dispatcher = new DmaDispatcher(transport);
        await Assert.ThrowsAsync<InvalidOperationException>(() => dispatcher.ReadAsync(
            Context() with { Session = Context().Session with { ConnectionId = "other" } }, [new(1, 2)], default).AsTask());
        await Assert.ThrowsAsync<ArgumentException>(() => dispatcher.ReadAsync(Context(), [new(1, 2_000_000)], default).AsTask());
        Assert.Equal(0, transport.Calls);
    }
    [Fact] public async Task ShortMemoryReadRemainsPartialAtProviderBoundary()
    {
        await using var dispatcher = new DmaDispatcher(new Transport());
        var blocks = await dispatcher.ReadAsync(Context(), [new(0x1000, 4)], default);
        Assert.False(blocks[0].Complete); Assert.Equal(2, blocks[0].Bytes.Length);
    }

    private sealed class ControlledTransport(Func<ImmutableArray<MemoryBlock>> read) : IMemoryTransport
    {
        public string DeviceId => "device";
        public string ConnectionId => "connection";
        public int Calls;
        public bool Disposed;
        public ImmutableArray<MemoryBlock> ReadBatch(int processId, IReadOnlyList<MemoryReadRequest> requests)
        { Interlocked.Increment(ref Calls); return read(); }
        public void Dispose() => Disposed = true;
    }

    [Fact] public async Task CancellationReleasesQueuedAndBackpressuredCallersWhileNativeCallStillOwnsTransport()
    {
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var transport = new ControlledTransport(() =>
        {
            entered.TrySetResult();
            release.Wait(TimeSpan.FromSeconds(5));
            return [new(0x1000, ImmutableArray.Create<byte>(1), true)];
        });
        var dispatcher = new DmaDispatcher(transport, capacity: 1);
        using var firstStop = new CancellationTokenSource();
        using var queuedStop = new CancellationTokenSource();
        using var waitingStop = new CancellationTokenSource();
        var first = dispatcher.ReadAsync(Context(), [new(0x1000, 1)], firstStop.Token).AsTask();
        Task? closing = null;
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            var queued = dispatcher.ReadAsync(Context(), [new(0x1000, 1)], queuedStop.Token).AsTask();
            var waiting = dispatcher.ReadAsync(Context(), [new(0x1000, 1)], waitingStop.Token).AsTask();
            Assert.Equal(1, dispatcher.Metrics.Queued);
            firstStop.Cancel(); queuedStop.Cancel(); waitingStop.Cancel();
            foreach (var request in new[] { first, queued, waiting })
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => request.WaitAsync(TimeSpan.FromSeconds(2)));
            closing = dispatcher.DisposeAsync().AsTask();
            Assert.False(closing.IsCompleted);
            Assert.False(transport.Disposed);
            Assert.Equal(1, transport.Calls);
        }
        finally
        {
            release.Set();
            await (closing ?? dispatcher.DisposeAsync().AsTask()).WaitAsync(TimeSpan.FromSeconds(2));
        }
        Assert.True(transport.Disposed);
        Assert.Equal(1, transport.Calls);
        Assert.Equal(0, dispatcher.Metrics.Queued);
        Assert.Equal(0, dispatcher.Metrics.Active);
    }

    [Fact] public async Task FailedNativeBatchDoesNotAbandonFollowingRequests()
    {
        var fail = true;
        var transport = new ControlledTransport(() =>
        {
            if (fail) { fail = false; throw new IOException("injected native failure"); }
            return [new(0x1000, ImmutableArray.Create<byte>(3), true)];
        });
        await using var dispatcher = new DmaDispatcher(transport);
        await Assert.ThrowsAsync<IOException>(() => dispatcher.ReadAsync(Context(), [new(0x1000, 1)], default).AsTask());
        var result = await dispatcher.ReadAsync(Context(), [new(0x1000, 1)], default).AsTask().WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(3, result[0].Bytes[0]);
        Assert.Equal(2, transport.Calls);
        Assert.Equal(1, dispatcher.Metrics.Failures);
    }
}
