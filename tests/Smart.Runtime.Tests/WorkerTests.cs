using Smart.Contracts;
using Smart.Runtime;
using Xunit;
namespace Smart.Runtime.Tests;

public sealed class WorkerTests
{
    private sealed class NoReads : ISnapshotReader
    {
        public ValueTask<PublishedSnapshot<T>> ReadAsync<T, TP>(SnapshotChannel<T, TP> channel, TP partition, CancellationToken ct = default) where TP : notnull =>
            throw new InvalidOperationException("Module unexpectedly read data.");
        public ValueTask<PublishedSnapshot<T>> WaitForChangeAsync<T, TP>(SnapshotChannel<T, TP> channel, TP partition, SnapshotStamp stamp, CancellationToken ct = default) where TP : notnull =>
            throw new InvalidOperationException("Module unexpectedly read data.");
    }
    private sealed class Module(string id, int priority, Func<TickContext, ModuleResult> tick) : IAccountModule
    {
        public string Id => id; public int Priority => priority;
        public ValueTask<ModuleResult> TickAsync(TickContext context, CancellationToken cancellationToken) => ValueTask.FromResult(tick(context));
    }
    [Fact] public async Task HigherPriorityActionPreemptsAndReleasesBeforeNewInput()
    {
        var device = new ActionTests.Device();
        var executor = new ActionExecutor(device, new InputLeaseRegistry());
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var low = new Module("low", 1, c => new(TimeSpan.FromMilliseconds(10),
            c.LastAction is null ? ActionTests.Hold("low", 4) : null));
        var high = new Module("high", 10, c =>
        {
            if (c.LastAction?.State == ActionState.Succeeded) done.TrySetResult();
            return new(TimeSpan.FromMilliseconds(10), device.Down.Task.IsCompleted && c.LastAction is null
                ? new("high", InputResource.Keyboard, [InputCommand.PressDown(5)], TimeSpan.FromSeconds(1)) : null);
        });
        var worker = new AccountWorker("one", new NoReads(), executor, [low, high]);
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var running = worker.RunAsync(stop.Token);
        await done.Task.WaitAsync(stop.Token); stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running);
        var events = device.Events.ToArray();
        Assert.True(Array.IndexOf(events, "release-all") < Array.IndexOf(events, "KeyDown:5"));
        Assert.Equal(2, worker.Metrics.Actions);
    }
    [Fact] public async Task DuplicateStartIsIdempotentAndRestartCreatesFreshWorker()
    {
        var count = 0; var leases = new InputLeaseRegistry();
        await using var runner = new AccountRunner(() =>
        {
            Interlocked.Increment(ref count);
            return new("a", new NoReads(), new ActionExecutor(new ActionTests.Device(), leases),
                [new Module("idle", 1, _ => new(TimeSpan.FromSeconds(1)))]);
        });
        runner.Start(); runner.Start(); Assert.Equal(1, count);
        await runner.StopAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(AccountState.Stopped, runner.Status.State);
        await runner.StopAsync(TimeSpan.FromSeconds(2));
        runner.Start(); Assert.Equal(2, count);
        await runner.StopAsync(TimeSpan.FromSeconds(2));
    }
    [Fact] public async Task ModuleFailureStopsOnlyItsAccountAndDisposesInput()
    {
        var failedDevice = new ActionTests.Device("first"); var otherDevice = new ActionTests.Device("second");
        var leases = new InputLeaseRegistry();
        var failureEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var first = new AccountRunner(() => new("first", new NoReads(), new ActionExecutor(failedDevice, leases),
            [new Module("failure", 1, _ => { failureEntered.TrySetResult(); throw new InvalidOperationException("rule failed"); })]));
        var secondTick = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var second = new AccountRunner(() => new("second", new NoReads(), new ActionExecutor(otherDevice, leases),
            [new Module("idle", 1, _ => { secondTick.TrySetResult(); return new(TimeSpan.FromMilliseconds(20)); })]));
        first.Start(); second.Start();
        await secondTick.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await failureEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await first.StopAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(AccountState.Faulted, first.Status.State);
        Assert.Equal(AccountState.Running, second.Status.State);
        Assert.True(failedDevice.Disposed);
        await second.StopAsync(TimeSpan.FromSeconds(2));
    }
    [Fact] public async Task UnknownDuplicateModulesAreRejectedAtStartup()
    {
        await using var executor = new ActionExecutor(new ActionTests.Device(), new());
        var module = new Module("same", 1, _ => new(TimeSpan.FromSeconds(1)));
        Assert.Throws<ArgumentException>(() => new AccountWorker("a", new NoReads(), executor, [module, module]));
    }
    [Fact] public void WorkBudgetBoundsCandidateEvaluation()
    {
        var budget = new WorkBudget(3, TimeSpan.FromSeconds(1));
        Assert.True(budget.TrySpend(2)); Assert.False(budget.TrySpend(2));
        Assert.True(budget.TrySpend()); Assert.False(budget.TrySpend());
    }

    [Fact] public async Task IdleModulesDoNotCauseHighFrequencyPolling()
    {
        var time = new Microsoft.Extensions.Time.Testing.FakeTimeProvider();
        var device = new ActionTests.Device();
        var worker = new AccountWorker("idle", new NoReads(), new ActionExecutor(device, new(), time),
            [new Module("slow", 1, _ => new(TimeSpan.FromSeconds(1)))], time: time);
        using var stop = new CancellationTokenSource();
        var running = worker.RunAsync(stop.Token);
        Assert.Equal(1, worker.Metrics.Ticks);
        time.Advance(TimeSpan.FromMilliseconds(100));
        Assert.Equal(1, worker.Metrics.Ticks);
        stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running);
    }
}
