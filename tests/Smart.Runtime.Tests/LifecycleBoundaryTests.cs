using Microsoft.Extensions.Time.Testing;
using Smart.Contracts;
using Smart.Data;
using Smart.Hosting;
using Xunit;
namespace Smart.Runtime.Tests;

public sealed class LifecycleBoundaryTests
{
    private sealed class DelayedSource(FakeTimeProvider time) : IRawChannelReader<int, NoPartition>
    {
        public async ValueTask<RawRead<int>> CaptureAsync(CaptureContext context, NoPartition partition, CancellationToken token)
        { await Task.Yield(); time.Advance(TimeSpan.FromMilliseconds(100)); return RawRead<int>.Complete(1); }
    }
    private sealed class ComputeModule(SnapshotChannel<int, NoPartition> channel, FakeTimeProvider time) : IAccountModule
    {
        public string Id => "compute"; public int Priority => 1; public IReadOnlyList<string> RequiredChannels => [channel.Id];
        public TaskCompletionSource Completed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool CanComputeAfterRead, CanComputeAfterOverrun;
        public async ValueTask<ModuleResult> TickAsync(TickContext context, CancellationToken token)
        {
            await context.Snapshots.ReadAsync(channel, default, token);
            CanComputeAfterRead = context.Budget.TrySpend();
            time.Advance(TimeSpan.FromMilliseconds(6));
            CanComputeAfterOverrun = context.Budget.TrySpend(); Completed.TrySetResult();
            return new(TimeSpan.FromHours(1));
        }
    }
    [Fact] public async Task SnapshotWaitDoesNotConsumeComputeBudgetButActualComputeStillDoes()
    {
        var time = new FakeTimeProvider(); var catalog = new SnapshotCatalog();
        var channel = catalog.Register<int, NoPartition, int>("counter", new DelayedSource(time), new ReplaceMerger<int>(_ => true), SnapshotMergePolicy.Replace, TimeSpan.Zero);
        catalog.Seal(); using var snapshots = new SnapshotProvider(catalog, time).OpenSession(new("d", "c", "a", "w", 1, "p", "m"));
        var module = new ComputeModule(channel, time);
        var worker = new AccountWorker("a", snapshots.Reader, new ActionExecutor(new ActionTests.Device(), new()), [module], time: time, registeredChannels: [channel.Id]);
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var run = worker.RunAsync(stop.Token);
        await module.Completed.Task.WaitAsync(stop.Token); stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
        Assert.True(module.CanComputeAfterRead); Assert.False(module.CanComputeAfterOverrun);
    }
    private sealed class CleanupFactory : IRuntimeSessionFactory
    {
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Retrying { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Cleanups;
        public async ValueTask<IRuntimeSession> OpenAsync(AccountProfile profile, CancellationToken token) =>
            new Scope(this, await new MockSessionFactory(new()).OpenAsync(profile, token));
        private sealed class Scope(CleanupFactory owner, IRuntimeSession inner) : IRuntimeSession
        {
            public AccountWorker Worker => inner.Worker;
            public void Invalidate() => inner.Invalidate();
            public ValueTask<bool> IsCurrentAsync(CancellationToken token) => ValueTask.FromResult(false);
            public async ValueTask DisposeAsync()
            {
                if (Interlocked.Increment(ref owner.Cleanups) == 1) throw new IOException("first close failed");
                owner.Retrying.TrySetResult(); await owner.Release.Task; await inner.DisposeAsync();
            }
        }
    }
    [Fact] public async Task StopDeadlineCoversRetryCleanupWithoutReleasingOwnershipEarly()
    {
        var factory = new CleanupFactory();
        await using var account = new ManagedAccount(new("a", ProbeIntervalMs: 100), factory);
        account.Start(); await SessionTests.Until(() => account.Status.State == SessionState.Faulted);
        try
        {
            await Assert.ThrowsAsync<TimeoutException>(() => account.StopAsync(TimeSpan.FromMilliseconds(30)));
            Assert.True(factory.Retrying.Task.IsCompleted); Assert.Equal(SessionState.Stopping, account.Status.State);
            Assert.Throws<InvalidOperationException>(account.Start); Assert.Equal(2, factory.Cleanups);
        }
        finally { factory.Release.TrySetResult(); await account.StopAsync(TimeSpan.FromSeconds(2)); }
        Assert.Equal(2, factory.Cleanups); Assert.Equal(SessionState.Stopped, account.Status.State);
    }
    private sealed class ThrowingCondition : IActionPrecondition
    { public ValueTask<bool> EvaluateAsync(CancellationToken token) => throw new IOException("condition implementation error"); }
    [Fact] public async Task ConditionExceptionIsClassifiedSeparatelyFromInputTransportFailure()
    {
        var device = new ActionTests.Device(); await using var executor = new ActionExecutor(device, new());
        var result = await executor.ExecuteAsync(ActionTests.Hold() with { Precondition = new ThrowingCondition() }, default);
        Assert.Equal(ActionState.Failed, result.State); Assert.Equal(ActionFailureKind.Precondition, result.Failure);
        Assert.Empty(device.Events);
    }
}
