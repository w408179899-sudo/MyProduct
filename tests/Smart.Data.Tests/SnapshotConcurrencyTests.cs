using Smart.Contracts;
using Xunit;
namespace Smart.Data.Tests;

public sealed class SnapshotConcurrencyTests
{
    private sealed class Source(Func<CaptureContext, CancellationToken, ValueTask<RawRead<int>>> read) : IRawChannelReader<int, NoPartition>
    {
        public int Calls;
        public ValueTask<RawRead<int>> CaptureAsync(CaptureContext context, NoPartition partition, CancellationToken token)
        { Interlocked.Increment(ref Calls); return read(context, token); }
    }
    private static readonly SessionIdentity Identity = new("d", "c", "a", "w", 1, "p", "m");
    private static (SnapshotSession Session, SnapshotChannel<int, NoPartition> Channel) Setup(Source source)
    {
        var catalog = new SnapshotCatalog();
        var channel = catalog.Register<int, NoPartition, int>("value", source, new ReplaceMerger<int>(_ => true), SnapshotMergePolicy.Replace, TimeSpan.Zero);
        catalog.Seal();
        return (new SnapshotProvider(catalog, options: new(TimeSpan.FromMilliseconds(10))).OpenSession(Identity), channel);
    }
    [Fact] public async Task HungRefreshRetainsOfficialSnapshotAndNeverStartsOverlappingReads()
    {
        var release = new TaskCompletionSource<RawRead<int>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = true;
        var source = new Source((_, _) => { if (first) { first = false; return ValueTask.FromResult(RawRead<int>.Complete(7)); } return new(release.Task); });
        var setup = Setup(source); using var session = setup.Session;
        var published = await session.Reader.ReadAsync(setup.Channel, default);
        try
        {
            Assert.Same(published, await session.Reader.ReadAsync(setup.Channel, default).AsTask().WaitAsync(TimeSpan.FromSeconds(2)));
            var values = await Task.WhenAll(Enumerable.Range(0, 32).Select(_ => session.Reader.ReadAsync(setup.Channel, default).AsTask()));
            Assert.All(values, value => Assert.Same(published, value));
            Assert.Equal(2, source.Calls); Assert.Equal(1, session.Metrics.InFlight); Assert.Equal(1, session.Metrics.RefreshTimeouts);
        }
        finally { release.TrySetResult(RawRead<int>.Complete(9)); }
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        while (session.Metrics.InFlight != 0) await Task.Delay(5, deadline.Token);
        Assert.Equal(9, (await session.Reader.ReadAsync(setup.Channel, default)).Value);
    }
    [Fact] public async Task CancellingOneColdReaderDoesNotCancelAnotherReaderOrDuplicateCapture()
    {
        var release = new TaskCompletionSource<RawRead<int>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var source = new Source((_, _) => new(release.Task));
        var setup = Setup(source); using var session = setup.Session; using var cancel = new CancellationTokenSource();
        var first = session.Reader.ReadAsync(setup.Channel, default, cancel.Token).AsTask();
        var second = session.Reader.ReadAsync(setup.Channel, default).AsTask();
        cancel.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        release.SetResult(RawRead<int>.Complete(42));
        Assert.Equal(42, (await second.WaitAsync(TimeSpan.FromSeconds(2))).Value); Assert.Equal(1, source.Calls);
    }
    [Fact] public async Task ResetWakesColdReaderEvenWhenOldCaptureIgnoresCancellation()
    {
        var release = new TaskCompletionSource<RawRead<int>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var source = new Source((context, _) => context.Session.ConnectionId == "c" ? new(release.Task) : ValueTask.FromResult(RawRead<int>.Complete(99)));
        var setup = Setup(source); using var session = setup.Session;
        var waiting = session.Reader.ReadAsync(setup.Channel, default).AsTask();
        session.Reset(Identity with { ConnectionId = "next" });
        try { Assert.Equal(99, (await waiting.WaitAsync(TimeSpan.FromSeconds(2))).Value); }
        finally { release.TrySetResult(RawRead<int>.Complete(-1)); }
        Assert.Equal(99, (await session.Reader.ReadAsync(setup.Channel, default)).Value);
    }
    [Fact] public async Task ResetCancelsOldProviderCaptureAndStartsOnlyNewGenerationData()
    {
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var source = new Source(async (context, token) =>
        {
            if (context.Session.ConnectionId == "c")
            {
                try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
                finally { cancelled.TrySetResult(); }
            }
            return RawRead<int>.Complete(19);
        });
        var setup = Setup(source); using var session = setup.Session;
        var pending = session.Reader.ReadAsync(setup.Channel, default).AsTask();
        session.Reset(Identity with { ConnectionId = "replacement" });
        await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(19, (await pending.WaitAsync(TimeSpan.FromSeconds(2))).Value);
    }
    [Fact] public async Task ReaderEnteringAfterSessionInvalidationReportsLifecycleCancellation()
    {
        var source = new Source((_, _) => ValueTask.FromResult(RawRead<int>.Complete(1)));
        var setup = Setup(source); using var session = setup.Session;
        session.Dispose();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => session.Reader.ReadAsync(setup.Channel, default).AsTask());
        Assert.Equal(0, source.Calls);
    }

    private sealed class BlockingObserver : ISnapshotObserver, IDisposable
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly ManualResetEventSlim _release = new();
        public int Writes;
        public void Published<T, TP>(SessionIdentity session, string channel, TP partition, PublishedSnapshot<T> snapshot) where TP : notnull
        {
            Entered.TrySetResult();
            _release.Wait(TimeSpan.FromSeconds(5));
            Interlocked.Increment(ref Writes);
        }
        public void Release() => _release.Set();
        public void Dispose() => _release.Dispose();
    }

    [Fact] public async Task AsyncDisposalWaitsForAlreadyCommittedObserverBeforeClosingTheSession()
    {
        using var observer = new BlockingObserver();
        var release = new TaskCompletionSource<RawRead<int>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var source = new Source((_, _) => new(release.Task));
        var catalog = new SnapshotCatalog();
        var channel = catalog.Register<int, NoPartition, int>("value", source, new ReplaceMerger<int>(_ => true), SnapshotMergePolicy.Replace, TimeSpan.Zero);
        catalog.Seal();
        using var session = new SnapshotProvider(catalog, observer: observer).OpenSession(Identity);
        var reading = session.Reader.ReadAsync(channel, default).AsTask();
        release.SetResult(RawRead<int>.Complete(1));
        await observer.Entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var closing = session.DisposeAsync().AsTask();
        try
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => reading);
            Assert.False(closing.IsCompleted);
            Assert.Equal(0, observer.Writes);
        }
        finally { observer.Release(); await closing.WaitAsync(TimeSpan.FromSeconds(2)); }
        Assert.Equal(1, observer.Writes);
        Assert.Equal(0, session.Metrics.InFlight);
    }

    [Fact] public async Task AsyncDisposalAlsoDrainsCapturesFromRetiredGenerations()
    {
        var oldRelease = new TaskCompletionSource<RawRead<int>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var source = new Source((context, _) => context.Session.ConnectionId == "c"
            ? new(oldRelease.Task) : ValueTask.FromResult(RawRead<int>.Complete(2)));
        var setup = Setup(source); using var session = setup.Session;
        var reading = session.Reader.ReadAsync(setup.Channel, default).AsTask();
        session.Reset(Identity with { ConnectionId = "replacement" });
        Assert.Equal(2, (await reading.WaitAsync(TimeSpan.FromSeconds(2))).Value);
        var closing = session.DisposeAsync().AsTask();
        try { Assert.False(closing.IsCompleted); }
        finally { oldRelease.TrySetResult(RawRead<int>.Complete(-1)); await closing.WaitAsync(TimeSpan.FromSeconds(2)); }
        Assert.Equal(0, session.Metrics.InFlight);
        Assert.Equal(1, session.Metrics.Publications);
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task InvalidationReturnsWithoutBlockingOnCallbacksAndAsyncDisposalStillDrainsThem(bool reset)
    {
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var source = new Source(async (context, token) =>
        {
            if (context.Session.ConnectionId != "c") return RawRead<int>.Complete(2);
            token.Register(() => { entered.TrySetResult(); release.Wait(TimeSpan.FromSeconds(5)); });
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return RawRead<int>.Complete(1);
        });
        var setup = Setup(source); using var session = setup.Session;
        var reading = session.Reader.ReadAsync(setup.Channel, default).AsTask();
        var invalidating = Task.Run(() =>
        {
            if (reset) session.Reset(Identity with { ConnectionId = "replacement" }); else session.Dispose();
        });
        Task? closing = null;
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            await invalidating.WaitAsync(TimeSpan.FromSeconds(2));
            if (reset) Assert.Equal(2, (await reading.WaitAsync(TimeSpan.FromSeconds(2))).Value);
            else await Assert.ThrowsAnyAsync<OperationCanceledException>(() => reading);
            closing = session.DisposeAsync().AsTask();
            Assert.False(closing.IsCompleted);
        }
        finally
        {
            release.Set();
            await invalidating.WaitAsync(TimeSpan.FromSeconds(2));
            await (closing ?? session.DisposeAsync().AsTask()).WaitAsync(TimeSpan.FromSeconds(2));
        }
        Assert.Equal(0, session.Metrics.InFlight);
    }

    [Fact] public async Task CallbackFailuresAcrossGenerationsAreReportedTogetherAfterDrainAndCanBeAcknowledgedByRetry()
    {
        var oldRelease = new TaskCompletionSource<RawRead<int>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var currentRelease = new TaskCompletionSource<RawRead<int>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var source = new Source((context, token) =>
        {
            var generationName = context.Session.ConnectionId;
            token.Register(() => throw new IOException(generationName + ":first"));
            token.Register(() => throw new InvalidOperationException(generationName + ":second"));
            return new(context.Session.ConnectionId == "c" ? oldRelease.Task : currentRelease.Task);
        });
        var setup = Setup(source); using var session = setup.Session;
        using var oldStop = new CancellationTokenSource();
        var oldReading = session.Reader.ReadAsync(setup.Channel, default, oldStop.Token).AsTask();
        oldStop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => oldReading);
        session.Reset(Identity with { ConnectionId = "replacement" });
        var reading = session.Reader.ReadAsync(setup.Channel, default).AsTask();
        session.Dispose();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => reading);
        var closing = session.DisposeAsync().AsTask();
        try
        {
            Assert.False(closing.IsCompleted);
        }
        finally
        {
            oldRelease.TrySetResult(RawRead<int>.Complete(-1));
            currentRelease.TrySetResult(RawRead<int>.Complete(-2));
        }
        var failure = await Assert.ThrowsAsync<AggregateException>(() => closing.WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.Equal(new[] { "c:first", "c:second", "replacement:first", "replacement:second" },
            failure.Flatten().InnerExceptions.Select(x => x.Message).Order(StringComparer.Ordinal));
        Assert.Equal(0, session.Metrics.InFlight);
        Assert.Equal(0, session.Metrics.Publications);
        await session.DisposeAsync();
    }
}
