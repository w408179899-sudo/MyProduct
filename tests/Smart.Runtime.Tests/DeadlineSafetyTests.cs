using Microsoft.Extensions.Time.Testing;
using Smart.Contracts;
using Smart.Data;
using Xunit;
namespace Smart.Runtime.Tests;

public sealed class DeadlineSafetyTests
{
    [Fact] public async Task TimerCallbackFailureIsReportedByAsyncDisposalAndNeverEscapesClockAdvance()
    {
        var time = new FakeTimeProvider();
        var deadline = new CancellationDeadline(TimeSpan.FromSeconds(1), time);
        var token = deadline.Token;
        token.Register(() => throw new IOException("deadline callback"));
        time.Advance(TimeSpan.FromSeconds(1));
        Assert.True(token.IsCancellationRequested);
        var error = await Assert.ThrowsAsync<AggregateException>(() => deadline.DisposeAsync().AsTask());
        Assert.Contains(error.Flatten().InnerExceptions, e => e.Message == "deadline callback");
    }

    [Fact] public async Task AsyncDisposalWaitsForStartedCancellationCallbacksAndDisarmsFurtherTimerInvocations()
    {
        var time = new FakeTimeProvider();
        var deadline = new CancellationDeadline(TimeSpan.FromSeconds(1), time);
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        deadline.Token.Register(() => { Interlocked.Increment(ref calls); entered.TrySetResult(); release.Wait(TimeSpan.FromSeconds(5)); });
        time.Advance(TimeSpan.FromSeconds(1));
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var disposing = deadline.DisposeAsync().AsTask();
        try { Assert.False(disposing.IsCompleted); }
        finally { release.Set(); await disposing.WaitAsync(TimeSpan.FromSeconds(2)); }
        time.Advance(TimeSpan.FromDays(1));
        await deadline.DisposeAsync();
        Assert.Equal(1, calls);
    }

    [Fact] public async Task DisposalBeforeExpiryPreventsFutureCancellation()
    {
        var time = new FakeTimeProvider();
        var deadline = new CancellationDeadline(TimeSpan.FromSeconds(1), time);
        var token = deadline.Token;
        await deadline.DisposeAsync();
        time.Advance(TimeSpan.FromDays(1));
        Assert.False(token.IsCancellationRequested);
    }

    [Fact] public async Task DisarmFreezesExpiryWithoutWaitingForCallbacksSoInputCanBeReleasedFirst()
    {
        var time = new FakeTimeProvider();
        var early = new CancellationDeadline(TimeSpan.FromSeconds(1), time);
        early.Disarm();
        time.Advance(TimeSpan.FromSeconds(1));
        Assert.False(early.Token.IsCancellationRequested);
        await early.DisposeAsync();

        var expired = new CancellationDeadline(TimeSpan.FromSeconds(1), time);
        using var released = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        expired.Token.Register(() => { entered.TrySetResult(); released.Wait(TimeSpan.FromSeconds(5)); });
        time.Advance(TimeSpan.FromSeconds(1));
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        try
        {
            expired.Disarm();
            Assert.True(expired.Token.IsCancellationRequested);
            var draining = expired.DisposeAsync().AsTask();
            Assert.False(draining.IsCompleted);
            released.Set();
            await draining.WaitAsync(TimeSpan.FromSeconds(2));
        }
        finally { released.Set(); await expired.DisposeAsync(); }
    }

    private sealed class ActionDevice : IInputDevice, IDisposable
    {
        public string DeviceId => "deadline-action";
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly ManualResetEventSlim _released = new();
        private int _sends;
        public int Releases;
        public async ValueTask SendAsync(InputCommand command, CancellationToken token)
        {
            if (Interlocked.Increment(ref _sends) != 1) return;
            token.Register(() =>
            {
                if (!_released.Wait(TimeSpan.FromSeconds(5))) throw new IOException("Input was not released before callback drain.");
                throw new IOException("action deadline callback");
            });
            var cancelled = Task.Delay(Timeout.InfiniteTimeSpan, token);
            Entered.TrySetResult();
            await cancelled;
        }
        public ValueTask ReleaseAllAsync(CancellationToken token)
        { Interlocked.Increment(ref Releases); _released.Set(); return ValueTask.CompletedTask; }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        public void Dispose() => _released.Dispose();
    }

    [Fact] public async Task ActionTimeoutReleasesInputBeforeCallbackDrainAndCallbackFailureCannotKeepExecutorGate()
    {
        var time = new FakeTimeProvider(); using var device = new ActionDevice();
        await using var executor = new ActionExecutor(device, new(), time);
        var plan = new ActionPlan("first", InputResource.Keyboard, [InputCommand.PressDown(4)], TimeSpan.FromSeconds(1));
        var running = executor.ExecuteAsync(plan, default).AsTask();
        await device.Entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        time.Advance(TimeSpan.FromSeconds(1));
        var result = await running.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(ActionState.Failed, result.State);
        Assert.Contains("action deadline callback", result.Detail);
        Assert.Equal(1, device.Releases);
        var next = await executor.ExecuteAsync(plan with { Id = "next" }, default);
        Assert.Equal(ActionState.Succeeded, next.State);
    }

    private sealed class CleanupDevice : IInputDevice, IDisposable
    {
        public string DeviceId => "deadline-cleanup";
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly ManualResetEventSlim _disposed = new();
        public int Disposals;
        public ValueTask SendAsync(InputCommand command, CancellationToken token) => ValueTask.CompletedTask;
        public async ValueTask ReleaseAllAsync(CancellationToken token)
        {
            token.Register(() =>
            {
                if (!_disposed.Wait(TimeSpan.FromSeconds(5))) throw new IOException("Device was not closed before callback drain.");
                throw new IOException("cleanup deadline callback");
            });
            var cancelled = Task.Delay(Timeout.InfiniteTimeSpan, token);
            Entered.TrySetResult();
            try { await cancelled; }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        }
        public ValueTask DisposeAsync() { Interlocked.Increment(ref Disposals); _disposed.Set(); return ValueTask.CompletedTask; }
        public void Dispose() => _disposed.Dispose();
    }

    [Fact] public async Task CleanupDeadlineFaultDoesNotSkipDeviceCloseAndKeepsLeaseUntilRetry()
    {
        var time = new FakeTimeProvider(); using var device = new CleanupDevice(); var leases = new InputLeaseRegistry();
        var executor = new ActionExecutor(device, leases, time);
        var closing = executor.DisposeAsync().AsTask();
        await device.Entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        time.Advance(TimeSpan.FromSeconds(2));
        var failure = await Assert.ThrowsAsync<AggregateException>(() => closing.WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.Contains(failure.Flatten().InnerExceptions, e => e.Message == "cleanup deadline callback");
        Assert.Equal(1, device.Disposals);
        Assert.Throws<InvalidOperationException>(() => leases.Acquire(device.DeviceId));
        await executor.DisposeAsync();
        using var reacquired = leases.Acquire(device.DeviceId);
        Assert.Equal(1, device.Disposals);
    }

    private sealed class DoubleFaultDevice : IInputDevice
    {
        public string DeviceId => "deadline-double-fault";
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _releases;
        public bool Disposed;
        public ValueTask SendAsync(InputCommand command, CancellationToken token) => ValueTask.CompletedTask;
        public async ValueTask ReleaseAllAsync(CancellationToken token)
        {
            if (Interlocked.Increment(ref _releases) != 1) return;
            token.Register(() => throw new IOException("input release callback failed"));
            var cancelled = Task.Delay(Timeout.InfiniteTimeSpan, token);
            Entered.TrySetResult();
            try { await cancelled; }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            { throw new IOException("input release operation failed"); }
        }
        public ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
    }

    [Fact] public async Task ActionCleanupPreservesBothReleaseAndDeadlineCallbackFailures()
    {
        var time = new FakeTimeProvider(); var device = new DoubleFaultDevice();
        await using var executor = new ActionExecutor(device, new(), time);
        var running = executor.ExecuteAsync(new("release", InputResource.Keyboard,
            [InputCommand.PressDown(4)], TimeSpan.FromSeconds(10)), default).AsTask();
        await device.Entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        time.Advance(TimeSpan.FromSeconds(2));
        var result = await running.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(ActionState.Failed, result.State);
        Assert.Contains("input release operation failed", result.Detail);
        Assert.Contains("input release callback failed", result.Detail);
    }

    [Fact] public async Task DisposalPreservesBothReleaseAndDeadlineCallbackFailuresAndLeaseUntilRetry()
    {
        var time = new FakeTimeProvider(); var device = new DoubleFaultDevice(); var leases = new InputLeaseRegistry();
        var executor = new ActionExecutor(device, leases, time);
        var closing = executor.DisposeAsync().AsTask();
        await device.Entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        time.Advance(TimeSpan.FromSeconds(2));
        var error = await Assert.ThrowsAsync<AggregateException>(() => closing.WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.Contains(error.Flatten().InnerExceptions, e => e.Message == "input release operation failed");
        Assert.Contains(error.Flatten().InnerExceptions, e => e.Message == "input release callback failed");
        Assert.False(device.Disposed);
        Assert.Throws<InvalidOperationException>(() => leases.Acquire(device.DeviceId));
        await executor.DisposeAsync();
        Assert.True(device.Disposed);
        using var reacquired = leases.Acquire(device.DeviceId);
    }

    private sealed class StopModule(string id, bool fail) : IAccountModule
    {
        public string Id => id;
        public int Priority => 0;
        public bool Stopped;
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ValueTask<ModuleResult> TickAsync(TickContext context, CancellationToken token) =>
            ValueTask.FromResult(new ModuleResult(TimeSpan.FromHours(1)));
        public async ValueTask StopAsync(ModuleStopReason reason, CancellationToken token)
        {
            Stopped = true;
            if (!fail) return;
            token.Register(() => throw new IOException("module deadline callback"));
            var cancelled = Task.Delay(Timeout.InfiniteTimeSpan, token);
            Entered.TrySetResult();
            try { await cancelled; }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            { throw new IOException("module stop operation failed"); }
        }
    }

    [Fact] public async Task ModuleStopDeadlineFailureIsCollectedAfterInputCleanupAndDoesNotSkipOtherModuleStops()
    {
        var time = new FakeTimeProvider(); var catalog = new SnapshotCatalog(); catalog.Seal();
        using var snapshots = new SnapshotProvider(catalog).OpenSession(new("d", "c", "a", "w", 1, "p", "m"));
        var device = new ActionTests.Device(); var first = new StopModule("first", false); var second = new StopModule("second", true);
        var worker = new AccountWorker("a", snapshots.Reader, new ActionExecutor(device, new(), time), [first, second], time: time);
        using var stop = new CancellationTokenSource();
        var running = worker.RunAsync(stop.Token);
        await stop.CancelAsync();
        await second.Entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.True(device.Disposed);
        time.Advance(TimeSpan.FromSeconds(2));
        var error = await Assert.ThrowsAsync<AggregateException>(() => running.WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.Contains(error.Flatten().InnerExceptions, e => e.Message == "module deadline callback");
        Assert.Contains(error.Flatten().InnerExceptions, e => e.Message == "module stop operation failed");
        Assert.True(first.Stopped); Assert.True(second.Stopped);
    }
}
