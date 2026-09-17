using System.Collections.Concurrent;
using Smart.Contracts;
using Smart.Data;
using Xunit;
namespace Smart.Runtime.Tests;

public sealed class ModuleLifecycleTests
{
    private sealed class Module(string id, Func<TickContext, CancellationToken, ValueTask<ModuleResult>> tick) : IAccountModule
    {
        public string Id => id; public int Priority { get; init; }
        public IReadOnlyList<string> Dependencies { get; init; } = [];
        public IReadOnlyList<string> RequiredChannels { get; init; } = [];
        public ConcurrentQueue<string>? Events;
        public ValueTask InitializeAsync(ModuleContext context, CancellationToken token) { Events?.Enqueue("start:" + Id); return ValueTask.CompletedTask; }
        public ValueTask StopAsync(ModuleStopReason reason, CancellationToken token) { Events?.Enqueue("stop:" + Id); return ValueTask.CompletedTask; }
        public ValueTask<ModuleResult> TickAsync(TickContext context, CancellationToken token) => tick(context, token);
    }
    private static SnapshotSession EmptySession()
    {
        var catalog = new SnapshotCatalog(); catalog.Seal();
        return new SnapshotProvider(catalog).OpenSession(new("d", "c", "a", "w", 1, "p", "m"));
    }
    [Fact] public async Task PendingModuleDoesNotBlockUrgentTicksAndNeverOverlapsItself()
    {
        var starts = 0;
        var blocked = new Module("pending", async (_, token) =>
        {
            Interlocked.Increment(ref starts);
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return new(TimeSpan.FromSeconds(1));
        }) { Priority = 100 };
        var ticks = 0; var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var other = new Module("other", (_, _) =>
        {
            if (++ticks == 3) done.TrySetResult();
            return ValueTask.FromResult(new ModuleResult(TimeSpan.FromMilliseconds(10)));
        });
        using var snapshots = EmptySession(); using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var worker = new AccountWorker("a", snapshots.Reader, new ActionExecutor(new ActionTests.Device(), new()), [blocked, other]);
        var run = worker.RunAsync(stop.Token);
        await done.Task.WaitAsync(stop.Token); stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
        Assert.Equal(1, starts);
    }
    [Fact] public async Task DependenciesInitializeFirstAndStopInReverseOrder()
    {
        var events = new ConcurrentQueue<string>();
        ValueTask<ModuleResult> Idle(TickContext _, CancellationToken token) => ValueTask.FromResult(new ModuleResult(TimeSpan.FromSeconds(1)));
        var first = new Module("first", Idle) { Events = events };
        var second = new Module("second", Idle) { Dependencies = ["first"], Events = events };
        using var snapshots = EmptySession(); using var stop = new CancellationTokenSource();
        var worker = new AccountWorker("a", snapshots.Reader, new ActionExecutor(new ActionTests.Device(), new()), [second, first]);
        var run = worker.RunAsync(stop.Token); stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
        Assert.Equal(new[] { "start:first", "start:second", "stop:second", "stop:first" }, events.ToArray());
    }
    [Fact] public void MissingDependenciesCyclesAndChannelsAreRejected()
    {
        ValueTask<ModuleResult> Idle(TickContext _, CancellationToken token) => ValueTask.FromResult(new ModuleResult(TimeSpan.FromSeconds(1)));
        Assert.Throws<ArgumentException>(() => ModuleCatalog.Validate([new Module("a", Idle) { Dependencies = ["missing"] }], []));
        Assert.Throws<ArgumentException>(() => ModuleCatalog.Validate([new Module("a", Idle) { Dependencies = ["b"] }, new Module("b", Idle) { Dependencies = ["a"] }], []));
        Assert.Throws<ArgumentException>(() => ModuleCatalog.Validate([new Module("a", Idle) { RequiredChannels = ["missing"] }], []));
    }
    private sealed class Source : IRawChannelReader<int, NoPartition>
    {
        public ValueTask<RawRead<int>> CaptureAsync(CaptureContext context, NoPartition partition, CancellationToken token) => ValueTask.FromResult(RawRead<int>.Complete(1));
    }
    [Fact] public async Task NewUndeclaredModuleReadFailsBeforeRawReaderAccess()
    {
        var catalog = new SnapshotCatalog();
        var channel = catalog.Register<int, NoPartition, int>("counter", new Source(), new ReplaceMerger<int>(_ => true), SnapshotMergePolicy.Replace, TimeSpan.Zero);
        catalog.Seal(); using var snapshots = new SnapshotProvider(catalog).OpenSession(new("d", "c", "a", "w", 1, "p", "m"));
        var module = new Module("undeclared", async (context, token) =>
        {
            await context.Snapshots.ReadAsync(channel, NoPartition.Value, token);
            return new(TimeSpan.FromSeconds(1));
        });
        var worker = new AccountWorker("a", snapshots.Reader, new ActionExecutor(new ActionTests.Device(), new()), [module], registeredChannels: ["counter"]);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => worker.RunAsync(default));
        Assert.Contains("did not declare", error.Message);
    }
}
