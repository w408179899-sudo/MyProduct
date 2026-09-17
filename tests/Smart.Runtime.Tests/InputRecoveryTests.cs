using Microsoft.Extensions.Time.Testing;
using Smart.Contracts;
using Xunit;
namespace Smart.Runtime.Tests;

public sealed class InputRecoveryTests
{
    [Fact] public async Task FailedDisposeCanRetryAndRetainsLeaseUntilPhysicalCleanupSucceeds()
    {
        var leases = new InputLeaseRegistry(); var device = new ActionTests.Device { ThrowRelease = true };
        var executor = new ActionExecutor(device, leases);
        await Assert.ThrowsAsync<IOException>(() => executor.DisposeAsync().AsTask());
        Assert.False(device.Disposed);
        Assert.Throws<InvalidOperationException>(() => leases.Acquire(device.DeviceId));
        device.ThrowRelease = false;
        await Task.WhenAll(executor.DisposeAsync().AsTask(), executor.DisposeAsync().AsTask());
        Assert.True(device.Disposed);
        using var lease = leases.Acquire(device.DeviceId);
    }
    [Fact] public async Task FailedReleaseQuarantinesNewActions()
    {
        var device = new ActionTests.Device { ThrowRelease = true };
        var executor = new ActionExecutor(device, new());
        var plan = ActionTests.Hold() with { Commands = [InputCommand.PressDown(4)] };
        Assert.Equal(ActionState.Failed, (await executor.ExecuteAsync(plan, default)).State);
        device.ThrowRelease = false;
        Assert.Equal(ActionState.Rejected, (await executor.ExecuteAsync(plan with { Id = "new" }, default)).State);
        Assert.Single(device.Events, x => x.StartsWith("KeyDown", StringComparison.Ordinal));
        await executor.DisposeAsync();
    }
    [Fact] public async Task PersistentOwnerPrioritySurvivesActionCompletionAndCanExplicitlyRelease()
    {
        var device = new ActionTests.Device(); await using var executor = new ActionExecutor(device, new());
        var high = new ActionPlan("high", InputResource.Keyboard, [InputCommand.PressDown(4)], TimeSpan.FromSeconds(1),
            InputRetention.UntilOwnerChanges, "high", 100);
        Assert.Equal(ActionState.Succeeded, (await executor.ExecuteAsync(high, default)).State);
        var low = high with { Id = "low", Owner = "low", Priority = 1 };
        Assert.Equal(ActionState.Rejected, (await executor.ExecuteAsync(low, default)).State);
        Assert.DoesNotContain("release-all", device.Events);
        await executor.ReleaseOwnerAsync("low", default);
        Assert.DoesNotContain("release-all", device.Events);
        await executor.ReleaseOwnerAsync("high", default);
        Assert.Equal(ActionState.Succeeded, (await executor.ExecuteAsync(low, default)).State);
    }
    private sealed class Condition : IActionPrecondition
    {
        public bool Allowed;
        public ValueTask<bool> EvaluateAsync(CancellationToken token) => ValueTask.FromResult(Allowed);
    }
    [Fact] public async Task ChangedPreconditionAndExpiredProposalCannotSendInput()
    {
        var clock = new FakeTimeProvider(); var device = new ActionTests.Device();
        await using var executor = new ActionExecutor(device, new(), clock);
        var condition = new Condition { Allowed = false };
        var plan = ActionTests.Hold() with { Precondition = condition };
        Assert.Equal(ActionState.Rejected, (await executor.ExecuteAsync(plan, default)).State);
        condition.Allowed = true;
        Assert.Equal(ActionState.Rejected, (await executor.ExecuteAsync(plan with { ExpiresAt = clock.GetUtcNow() }, default)).State);
        Assert.Empty(device.Events);
    }
    [Fact] public void FileLeasesCoordinateIndependentRegistriesAndRecoverAfterClose()
    {
        var path = Path.Combine(Path.GetTempPath(), "smart-lease-" + Guid.NewGuid().ToString("N"));
        try
        {
            var first = new InputLeaseRegistry(path); var second = new InputLeaseRegistry(path);
            using (first.Acquire("device")) Assert.Throws<InvalidOperationException>(() => second.Acquire("DEVICE"));
            using var acquired = second.Acquire("device");
        }
        finally { if (Directory.Exists(path)) Directory.Delete(path, true); }
    }
}
