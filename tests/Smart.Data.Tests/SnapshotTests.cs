using System.Collections.Concurrent;
using Microsoft.Extensions.Time.Testing;
using Smart.Contracts;
using Smart.Data;
using Xunit;
namespace Smart.Data.Tests;

public sealed class SnapshotTests
{
    private static readonly SessionIdentity Identity = new("device", "connection", "account", "worker", 123, "process-start", "module-base");
    private sealed class Reader<T>(Func<CaptureContext, string, CancellationToken, ValueTask<RawRead<T>>> capture) : IRawChannelReader<T, string>
    {
        public int Calls;
        public ValueTask<RawRead<T>> CaptureAsync(CaptureContext c, string p, CancellationToken ct)
        { Interlocked.Increment(ref Calls); return capture(c, p, ct); }
    }
    private static (SnapshotSession Session, SnapshotChannel<int, string> Token, Reader<int> Reader) Setup(
        Func<CaptureContext, string, CancellationToken, ValueTask<RawRead<int>>> capture, FakeTimeProvider? time = null, TimeSpan? interval = null)
    {
        var reader = new Reader<int>(capture);
        var catalog = new SnapshotCatalog();
        var token = catalog.Register<int, string, int>("numbers", reader, new ReplaceMerger<int>(_ => true),
            SnapshotMergePolicy.Replace, interval ?? TimeSpan.Zero);
        catalog.Seal();
        return (new SnapshotProvider(catalog, time).OpenSession(Identity), token, reader);
    }
    private static ValueTask<RawRead<int>> Good(int value) => ValueTask.FromResult(RawRead<int>.Complete(value));

    [Fact] public void CatalogRequiresSealAndRejectsDuplicatesAndLateRegistration()
    {
        var reader = new Reader<int>((_, _, _) => Good(1)); var catalog = new SnapshotCatalog();
        catalog.Register<int, string, int>("one", reader, new ReplaceMerger<int>(_ => true), SnapshotMergePolicy.Replace, TimeSpan.Zero);
        Assert.Throws<InvalidOperationException>(() => new SnapshotProvider(catalog));
        Assert.Throws<InvalidOperationException>(() => catalog.Register<int, string, int>("one", reader, new ReplaceMerger<int>(_ => true), SnapshotMergePolicy.Replace, TimeSpan.Zero));
        catalog.Seal();
        Assert.Throws<InvalidOperationException>(() => catalog.Register<int, string, int>("two", reader, new ReplaceMerger<int>(_ => true), SnapshotMergePolicy.Replace, TimeSpan.Zero));
        Assert.All(catalog.Channels, c => Assert.Equal(SnapshotReadPolicy.Stable, c.Policy));
        Assert.Single(Enum.GetValues<SnapshotReadPolicy>());
    }
    [Fact] public async Task ForeignTokenCannotReadAnotherCatalog()
    {
        var a = Setup((_, _, _) => Good(1)); var b = Setup((_, _, _) => Good(2));
        using var sa = a.Session; using var sb = b.Session;
        await Assert.ThrowsAsync<InvalidOperationException>(() => sa.Reader.ReadAsync(b.Token, "x").AsTask());
        Assert.Equal(0, a.Reader.Calls);
    }
    [Theory] [InlineData(0)] [InlineData(1)] [InlineData(-4)]
    public async Task ValidValuesPublishImmediately(int value)
    {
        var x = Setup((_, _, _) => Good(value)); using var session = x.Session;
        var result = await session.Reader.ReadAsync(x.Token, "p");
        Assert.Equal(value, result.Value); Assert.Equal(1, result.Stamp.Version);
        Assert.Equal(1, x.Reader.Calls);
    }
    [Fact] public async Task ValidFalseAndNullableNullAreData()
    {
        var catalog = new SnapshotCatalog();
        var boolean = catalog.Register<bool, string, bool>("bool", new Reader<bool>((_, _, _) => ValueTask.FromResult(RawRead<bool>.Complete(false))),
            new ReplaceMerger<bool>(_ => true), SnapshotMergePolicy.Replace, TimeSpan.Zero);
        var nullable = catalog.Register<string?, string, string?>("nullable", new Reader<string?>((_, _, _) => ValueTask.FromResult(RawRead<string?>.Complete(null))),
            new ReplaceMerger<string?>(_ => true), SnapshotMergePolicy.Replace, TimeSpan.Zero);
        catalog.Seal(); using var session = new SnapshotProvider(catalog).OpenSession(Identity);
        Assert.False((await session.Reader.ReadAsync(boolean, "p")).Value);
        Assert.Null((await session.Reader.ReadAsync(nullable, "p")).Value);
    }
    [Fact] public async Task FailedReadKeepsExactObjectVersionAndTimestampEvenAfterDays()
    {
        var time = new FakeTimeProvider(); var fail = false;
        var x = Setup((_, _, _) => fail ? ValueTask.FromResult(RawRead<int>.Failed("fault")) : Good(17), time);
        using var session = x.Session;
        var first = await session.Reader.ReadAsync(x.Token, "p"); fail = true;
        time.Advance(TimeSpan.FromDays(30));
        var held = await session.Reader.ReadAsync(x.Token, "p");
        Assert.Same(first, held);
        time.Advance(TimeSpan.FromDays(30));
        Assert.Same(first, await session.Reader.ReadAsync(x.Token, "p"));
    }
    [Fact] public async Task ColdStartRetriesWithoutReturningDefault()
    {
        var time = new FakeTimeProvider(); var fail = true;
        var x = Setup((_, _, _) => fail ? ValueTask.FromResult(RawRead<int>.Failed("offline")) : Good(42), time);
        using var session = x.Session; using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var pending = session.Reader.ReadAsync(x.Token, "p", timeout.Token).AsTask();
        Assert.False(pending.IsCompleted); fail = false;
        time.Advance(TimeSpan.FromMilliseconds(30));
        var result = await pending.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(42, result.Value); Assert.Equal(1, result.Stamp.Version);
    }
    [Fact] public async Task ColdStartCanBeCancelled()
    {
        var x = Setup((_, _, _) => ValueTask.FromResult(RawRead<int>.Failed("offline")));
        using var session = x.Session; using var cancel = new CancellationTokenSource();
        var task = session.Reader.ReadAsync(x.Token, "p", cancel.Token).AsTask(); cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
    }
    [Fact] public async Task NewValidReadReplacesPublicationAndSameValueIsStillAValidCapture()
    {
        var next = 5; var x = Setup((_, _, _) => Good(next)); using var session = x.Session;
        var first = await session.Reader.ReadAsync(x.Token, "p"); next = 6;
        var second = await session.Reader.ReadAsync(x.Token, "p");
        var third = await session.Reader.ReadAsync(x.Token, "p");
        Assert.Equal(6, second.Value); Assert.Equal(first.Stamp.Version + 1, second.Stamp.Version);
        Assert.Equal(second.Stamp.Version + 1, third.Stamp.Version);
    }
    [Fact] public async Task PartitionValuesCannotFallbackAcrossKeys()
    {
        var x = Setup((_, p, _) => p == "a" ? Good(1) : ValueTask.FromResult(RawRead<int>.Failed("missing")));
        using var session = x.Session; await session.Reader.ReadAsync(x.Token, "a");
        using var stop = new CancellationTokenSource();
        var b = session.Reader.ReadAsync(x.Token, "b", stop.Token).AsTask();
        Assert.False(b.IsCompleted); stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => b);
    }
    [Fact] public async Task SameKeyConcurrentReadersCoalesceCapture()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var x = Setup(async (_, _, ct) => { entered.TrySetResult(); await release.Task.WaitAsync(ct); return RawRead<int>.Complete(8); },
            interval: TimeSpan.FromSeconds(10));
        using var session = x.Session; using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var tasks = Enumerable.Range(0, 32).Select(_ => session.Reader.ReadAsync(x.Token, "p", stop.Token).AsTask()).ToArray();
        await entered.Task.WaitAsync(stop.Token); release.SetResult();
        var results = await Task.WhenAll(tasks);
        Assert.Equal(1, x.Reader.Calls); Assert.All(results, r => Assert.Same(results[0], r));
    }
    [Fact] public async Task ResetDiscardsInFlightOldGeneration()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var x = Setup(async (context, _, ct) =>
        {
            if (context.Session.ConnectionId == "connection")
            { entered.TrySetResult(); await release.Task.WaitAsync(ct); return RawRead<int>.Complete(1); }
            return RawRead<int>.Complete(99);
        });
        using var session = x.Session; using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var oldRequest = session.Reader.ReadAsync(x.Token, "p", stop.Token).AsTask();
        await entered.Task.WaitAsync(stop.Token);
        session.Reset(Identity with { ConnectionId = "new-connection" });
        var current = await session.Reader.ReadAsync(x.Token, "p", stop.Token);
        release.SetResult(); var completed = await oldRequest;
        Assert.Equal(99, current.Value); Assert.Equal(99, completed.Value);
        Assert.Equal(current.Stamp.Generation, completed.Stamp.Generation);
    }
    [Theory] [InlineData("device")] [InlineData("connection")] [InlineData("pid")]
    [InlineData("process")] [InlineData("module")]
    public async Task LifecycleChangeCannotReuseOldPublication(string field)
    {
        var fail = false; var x = Setup((_, _, _) => fail ? ValueTask.FromResult(RawRead<int>.Failed("fault")) : Good(3));
        using var session = x.Session; await session.Reader.ReadAsync(x.Token, "p"); fail = true;
        var next = field switch
        {
            "device" => Identity with { DeviceId = "other" }, "connection" => Identity with { ConnectionId = "other" },
            "pid" => Identity with { ProcessId = 456 }, "process" => Identity with { ProcessStartId = "other" },
            _ => Identity with { ModuleId = "other" }
        };
        session.Reset(next);
        using var stop = new CancellationTokenSource();
        var pending = session.Reader.ReadAsync(x.Token, "p", stop.Token).AsTask();
        Assert.False(pending.IsCompleted); stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
    }
    [Fact] public async Task AccountsAndWorkersHaveSeparateSessions()
    {
        var fail = false; var reader = new Reader<int>((_, _, _) => fail ? ValueTask.FromResult(RawRead<int>.Failed("fault")) : Good(1));
        var catalog = new SnapshotCatalog();
        var token = catalog.Register<int, string, int>("a", reader, new ReplaceMerger<int>(_ => true), SnapshotMergePolicy.Replace, TimeSpan.Zero);
        catalog.Seal(); var provider = new SnapshotProvider(catalog);
        using var first = provider.OpenSession(Identity);
        await first.Reader.ReadAsync(token, "x"); fail = true;
        using var second = provider.OpenSession(Identity with { AccountId = "second", WorkerId = "worker2" });
        using var stop = new CancellationTokenSource();
        var pending = second.Reader.ReadAsync(token, "x", stop.Token).AsTask();
        Assert.False(pending.IsCompleted); stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.Throws<InvalidOperationException>(() => first.Reset(Identity with { AccountId = "other" }));
    }
    [Fact] public async Task WaitingForChangeNeverTreatsFallbackAsNewPublication()
    {
        var time = new FakeTimeProvider(); var fail = false;
        var x = Setup((_, _, _) => fail ? ValueTask.FromResult(RawRead<int>.Failed("fault")) : Good(1), time);
        using var session = x.Session; var first = await session.Reader.ReadAsync(x.Token, "p"); fail = true;
        using var stop = new CancellationTokenSource();
        var pending = session.Reader.WaitForChangeAsync(x.Token, "p", first.Stamp, stop.Token).AsTask();
        time.Advance(TimeSpan.FromMinutes(1)); Assert.False(pending.IsCompleted); stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
    }
    [Fact] public async Task ReaderExceptionsRetainPublication()
    {
        var fail = false;
        var x = Setup((_, _, _) => fail ? throw new IOException("native read") : Good(7));
        using var session = x.Session; var first = await session.Reader.ReadAsync(x.Token, "p"); fail = true;
        Assert.Same(first, await session.Reader.ReadAsync(x.Token, "p"));
    }
    [Fact] public async Task DisposalCancelsPendingReaders()
    {
        var x = Setup((_, _, _) => ValueTask.FromResult(RawRead<int>.Failed("offline")));
        var task = x.Session.Reader.ReadAsync(x.Token, "p").AsTask(); x.Session.Dispose();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
    }
}
