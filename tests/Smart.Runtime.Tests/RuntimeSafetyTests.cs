using Smart.Contracts;
using Xunit;

namespace Smart.Runtime.Tests;

public sealed class RuntimeSafetyTests
{
    private sealed class NoReads : ISnapshotReader
    {
        public ValueTask<PublishedSnapshot<T>> ReadAsync<T, TP>(SnapshotChannel<T, TP> channel, TP partition,
            CancellationToken cancellationToken = default) where TP : notnull => throw new InvalidOperationException("Unexpected read.");
        public ValueTask<PublishedSnapshot<T>> WaitForChangeAsync<T, TP>(SnapshotChannel<T, TP> channel, TP partition,
            SnapshotStamp after, CancellationToken cancellationToken = default) where TP : notnull => throw new InvalidOperationException("Unexpected read.");
    }

    private sealed class Module(string id) : IAccountModule
    {
        public string Id => id;
        public int Priority => 0;
        public Action<CancellationToken>? Initialize { get; init; }
        public Func<CancellationToken, ValueTask<ModuleResult>> Tick { get; init; } =
            _ => ValueTask.FromResult(new ModuleResult(TimeSpan.FromSeconds(1)));
        public int Stops;
        public ValueTask InitializeAsync(ModuleContext context, CancellationToken token)
        { Initialize?.Invoke(token); return ValueTask.CompletedTask; }
        public ValueTask<ModuleResult> TickAsync(TickContext context, CancellationToken token) => Tick(token);
        public ValueTask StopAsync(ModuleStopReason reason, CancellationToken token)
        { Interlocked.Increment(ref Stops); return ValueTask.CompletedTask; }
    }

    [Fact]
    public async Task FaultingModuleCancellationCallbackCannotSkipInputAndModuleCleanup()
    {
        var device = new ActionTests.Device();
        var leases = new InputLeaseRegistry();
        var executor = new ActionExecutor(device, leases);
        var module = new Module("faulty")
        {
            Initialize = token => token.Register(() => throw new InvalidOperationException("cancel callback failed")),
            Tick = _ => throw new InvalidOperationException("business module failed")
        };
        var worker = new AccountWorker("account", new NoReads(), executor, [module]);
        var failure = await Assert.ThrowsAnyAsync<Exception>(() => worker.RunAsync(default));
        Assert.True(device.Disposed);
        Assert.Equal(1, module.Stops);
        Assert.Contains("cancel callback failed", failure.ToString());
        Assert.Contains("business module failed", failure.ToString());
        using var reacquired = leases.Acquire(device.DeviceId);
    }

    private sealed class CancellationDevice : IInputDevice
    {
        public string DeviceId => "callback-device";
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Releases, Disposals;
        public async ValueTask SendAsync(InputCommand command, CancellationToken token)
        {
            var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var registration = token.Register(() =>
            {
                cancelled.TrySetCanceled(token);
                throw new InvalidOperationException("device callback failed");
            });
            Entered.TrySetResult();
            // Only the failing callback may wake SendAsync. A separate cancellable Delay could
            // wake it first and unregister this callback before CancelAsync gets to invoke it.
            await cancelled.Task;
        }
        public ValueTask ReleaseAllAsync(CancellationToken token) { Releases++; return ValueTask.CompletedTask; }
        public ValueTask DisposeAsync() { Disposals++; return ValueTask.CompletedTask; }
    }

    private sealed class BlockingCancellationDevice : IInputDevice
    {
        public string DeviceId => "blocking-callback-device";
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ManualResetEventSlim CallbackRelease { get; } = new();
        public int Disposals;
        public ValueTask SendAsync(InputCommand command, CancellationToken token)
        {
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            token.Register(() =>
            {
                completion.TrySetCanceled(token);
                Entered.TrySetResult();
                CallbackRelease.Wait();
            });
            return new(completion.Task);
        }
        public ValueTask ReleaseAllAsync(CancellationToken token) => ValueTask.CompletedTask;
        public ValueTask DisposeAsync() { Disposals++; return ValueTask.CompletedTask; }
    }

    [Fact]
    public async Task ExecutorCancellationCallbackFailureStillReleasesInputDeviceAndLease()
    {
        var leases = new InputLeaseRegistry();
        var device = new CancellationDevice();
        var executor = new ActionExecutor(device, leases);
        var execution = executor.ExecuteAsync(new("press", InputResource.Keyboard,
            [InputCommand.PressDown(4)], TimeSpan.FromSeconds(10)), default).AsTask();
        await device.Entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var failure = await Assert.ThrowsAnyAsync<Exception>(() => executor.DisposeAsync().AsTask());
        Assert.Contains("device callback failed", failure.ToString());
        Assert.Equal(ActionState.Cancelled, (await execution.WaitAsync(TimeSpan.FromSeconds(2))).State);
        Assert.True(device.Releases > 0);
        Assert.Equal(1, device.Disposals);
        using var reacquired = leases.Acquire(device.DeviceId);
        await executor.DisposeAsync();
        Assert.Equal(1, device.Disposals);
    }

    [Fact]
    public async Task ConcurrentDisposalKeepsLeaseUntilTheOriginalCancellationCallbacksFinish()
    {
        var leases = new InputLeaseRegistry();
        var device = new BlockingCancellationDevice();
        var executor = new ActionExecutor(device, leases);
        var execution = executor.ExecuteAsync(new("press", InputResource.Keyboard,
            [InputCommand.PressDown(4)], TimeSpan.FromSeconds(10)), default).AsTask();
        var first = executor.DisposeAsync().AsTask();
        Task second = Task.CompletedTask;
        try
        {
            await device.Entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            second = executor.DisposeAsync().AsTask();
            Assert.False(second.IsCompleted);
            Assert.Equal(0, device.Disposals);
            Assert.Throws<InvalidOperationException>(() => leases.Acquire(device.DeviceId));
        }
        finally
        {
            device.CallbackRelease.Set();
            await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(2));
            device.CallbackRelease.Dispose();
        }
        Assert.Equal(ActionState.Cancelled, (await execution.WaitAsync(TimeSpan.FromSeconds(2))).State);
        Assert.Equal(1, device.Disposals);
        using var reacquired = leases.Acquire(device.DeviceId);
    }

    [Fact]
    public async Task ReleasingOwnerAfterDisposalCannotCallTheOldDevice()
    {
        var device = new ActionTests.Device();
        var executor = new ActionExecutor(device, new());
        await executor.ExecuteAsync(new("hold", InputResource.Keyboard, [InputCommand.PressDown(4)],
            TimeSpan.FromSeconds(1), InputRetention.UntilOwnerChanges, "movement"), default);
        await executor.DisposeAsync();
        var releases = device.Events.Count(x => x == "release-all");
        await Assert.ThrowsAsync<ObjectDisposedException>(() => executor.ReleaseOwnerAsync("movement", default).AsTask());
        Assert.Equal(releases, device.Events.Count(x => x == "release-all"));
    }

    [Fact]
    public void ModuleLimitStopsEnumeratingAtTheFirstExcessEntry()
    {
        var visited = 0;
        IEnumerable<IAccountModule> TooMany()
        {
            for (var i = 0; ; i++)
            {
                if (++visited > 65) throw new InvalidOperationException("Enumeration continued beyond the declared limit.");
                yield return new Module("module-" + i);
            }
        }
        Assert.Throws<ArgumentException>(() => ModuleCatalog.Validate(TooMany(), []));
        Assert.Equal(65, visited);
    }

    [Fact]
    public async Task RunnerStopDeadlineIncludesBlockingCancellationCallbacks()
    {
        using var callbackRelease = new ManualResetEventSlim();
        var initialized = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var module = new Module("blocking-callback")
        {
            Initialize = token =>
            {
                token.Register(() => callbackRelease.Wait());
                initialized.TrySetResult();
            }
        };
        await using var runner = new AccountRunner(() => new("account", new NoReads(),
            new ActionExecutor(new ActionTests.Device(), new()), [module]));
        runner.Start();
        await initialized.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var stopping = Task.Run(() => runner.StopAsync(TimeSpan.FromMilliseconds(30)));
        try
        {
            await Assert.ThrowsAsync<TimeoutException>(() => stopping.WaitAsync(TimeSpan.FromSeconds(2)));
            Assert.True(stopping.IsCompleted, "The caller deadline must cover cancellation callbacks as well as the worker task.");
        }
        finally { callbackRelease.Set(); }
        await runner.StopAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(AccountState.Stopped, runner.Status.State);
    }

    [Fact]
    public async Task RunnerCancellationFailureCanBeObservedWithoutPreventingSubsequentDisposal()
    {
        var initialized = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var device = new ActionTests.Device();
        var module = new Module("throwing-callback")
        {
            Initialize = token =>
            {
                token.Register(() => throw new InvalidOperationException("runner callback failed"));
                initialized.TrySetResult();
            }
        };
        await using var runner = new AccountRunner(() => new("account", new NoReads(),
            new ActionExecutor(device, new()), [module]));
        runner.Start();
        await initialized.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var failure = await Assert.ThrowsAnyAsync<Exception>(() => runner.StopAsync(TimeSpan.FromSeconds(2)));
        Assert.Contains("runner callback failed", failure.ToString());
        Assert.True(device.Disposed);
        Assert.Equal(1, module.Stops);
        await runner.StopAsync(TimeSpan.FromSeconds(2));
    }

    private sealed class RecoveringExecutor(InputLeaseRegistry leases, string deviceId) : IActionExecutor
    {
        private readonly IDisposable _lease = leases.Acquire(deviceId);
        public bool FailCleanup = true;
        public int CleanupAttempts;
        public ValueTask<ActionFeedback> ExecuteAsync(ActionPlan plan, CancellationToken token) =>
            ValueTask.FromResult(new ActionFeedback(plan.Id, ActionState.Succeeded));
        public ValueTask ReleaseOwnerAsync(string owner, CancellationToken token) => ValueTask.CompletedTask;
        public ValueTask DisposeAsync()
        {
            Interlocked.Increment(ref CleanupAttempts);
            if (FailCleanup) throw new IOException("physical release failed");
            _lease.Dispose();
            return ValueTask.CompletedTask;
        }
    }

    [Fact]
    public async Task FaultedRunnerRetainsUnreleasedWorkerAndRejectsRestartUntilStopRetrySucceeds()
    {
        var leases = new InputLeaseRegistry();
        var first = new RecoveringExecutor(leases, "old-device");
        var initialized = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var created = 0;
        await using var runner = new AccountRunner(() =>
        {
            var generation = Interlocked.Increment(ref created);
            return new("account", new NoReads(), generation == 1 ? first :
                new RecoveringExecutor(leases, "new-device") { FailCleanup = false },
                [new Module("module") { Initialize = _ => initialized.TrySetResult() }]);
        });
        runner.Start();
        await initialized.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await Assert.ThrowsAsync<AggregateException>(() => runner.StopAsync(TimeSpan.FromSeconds(2)));
        Assert.Equal(AccountState.Faulted, runner.Status.State);
        Assert.Throws<InvalidOperationException>(runner.Start);
        Assert.Equal(1, created);
        Assert.Throws<InvalidOperationException>(() => leases.Acquire("old-device"));
        first.FailCleanup = false;
        await runner.StopAsync(TimeSpan.FromSeconds(2));
        using (leases.Acquire("old-device")) { }
        runner.Start();
        Assert.Equal(2, created);
        await runner.StopAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task ConcurrentRunnerDisposalAndRepeatedStopCleanInputOnlyOnce()
    {
        var leases = new InputLeaseRegistry();
        var executor = new RecoveringExecutor(leases, "single-cleanup") { FailCleanup = false };
        var initialized = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var runner = new AccountRunner(() => new("account", new NoReads(), executor,
            [new Module("module") { Initialize = _ => initialized.TrySetResult() }]));
        runner.Start();
        await initialized.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await Task.WhenAll(Enumerable.Range(0, 16).Select(_ => runner.DisposeAsync().AsTask()));
        await runner.StopAsync(TimeSpan.FromSeconds(2));
        await runner.DisposeAsync();
        Assert.Equal(1, executor.CleanupAttempts);
        using var reacquired = leases.Acquire("single-cleanup");
    }
}
