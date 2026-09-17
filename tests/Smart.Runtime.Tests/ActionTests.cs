using System.Collections.Concurrent;
using Microsoft.Extensions.Time.Testing;
using Smart.Contracts;
using Smart.Hosting;
using Smart.Runtime;
using Xunit;
namespace Smart.Runtime.Tests;

public sealed class ActionTests
{
    internal sealed class Device(string id = "input") : IInputDevice
    {
        public string DeviceId => id;
        public ConcurrentQueue<string> Events { get; } = new();
        public TaskCompletionSource Down { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool ThrowSend;
        public bool ThrowRelease;
        public bool Disposed;
        public ValueTask SendAsync(InputCommand command, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            Events.Enqueue(command.Operation + ":" + command.Code);
            if (ThrowSend) throw new IOException("send failed");
            if (command.Operation == InputOperation.KeyDown) Down.TrySetResult();
            return ValueTask.CompletedTask;
        }
        public ValueTask ReleaseAllAsync(CancellationToken ct)
        {
            Events.Enqueue("release-all");
            if (ThrowRelease) throw new IOException("release failed");
            return ValueTask.CompletedTask;
        }
        public ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
    }
    internal static ActionPlan Hold(string id = "hold", int code = 4) =>
        new(id, InputResource.Keyboard, [InputCommand.PressDown(code), InputCommand.Wait(TimeSpan.FromSeconds(5))], TimeSpan.FromSeconds(10));

    [Fact] public async Task CancellationReleasesHeldInput()
    {
        var device = new Device(); await using var executor = new ActionExecutor(device, new());
        using var stop = new CancellationTokenSource();
        var action = executor.ExecuteAsync(Hold(), stop.Token).AsTask();
        await device.Down.Task.WaitAsync(TimeSpan.FromSeconds(2)); stop.Cancel();
        Assert.Equal(ActionState.Cancelled, (await action).State);
        Assert.Equal("release-all", device.Events.Last());
    }
    [Fact] public async Task TimeoutUsesInjectedClockAndReleasesInput()
    {
        var time = new FakeTimeProvider(); var device = new Device();
        await using var executor = new ActionExecutor(device, new(), time);
        var plan = Hold() with { Timeout = TimeSpan.FromSeconds(1), Commands = [InputCommand.PressDown(4), InputCommand.Wait(TimeSpan.FromSeconds(1))] };
        var action = executor.ExecuteAsync(plan, CancellationToken.None).AsTask();
        await device.Down.Task; time.Advance(TimeSpan.FromSeconds(2));
        Assert.Equal(ActionState.TimedOut, (await action.WaitAsync(TimeSpan.FromSeconds(2))).State);
        Assert.Equal("release-all", device.Events.Last());
    }
    [Fact] public async Task ConcurrentActionIsRejectedWithoutInterleaving()
    {
        var device = new Device(); await using var executor = new ActionExecutor(device, new());
        using var stop = new CancellationTokenSource();
        var first = executor.ExecuteAsync(Hold(), stop.Token).AsTask(); await device.Down.Task;
        var second = await executor.ExecuteAsync(Hold("other", 5), CancellationToken.None);
        Assert.Equal(ActionState.Rejected, second.State);
        Assert.DoesNotContain("KeyDown:5", device.Events);
        stop.Cancel(); await first;
    }
    [Fact] public async Task PhysicalEndpointLeaseSurvivesUntilExecutorDisposal()
    {
        var leases = new InputLeaseRegistry();
        var first = new ActionExecutor(new Device(), leases);
        Assert.Throws<InvalidOperationException>(() => new ActionExecutor(new Device(), leases));
        await first.DisposeAsync();
        await using var next = new ActionExecutor(new Device(), leases);
    }
    [Fact] public async Task DifferentEndpointsCanRunIndependently()
    {
        var leases = new InputLeaseRegistry();
        await using var first = new ActionExecutor(new Device("one"), leases);
        await using var second = new ActionExecutor(new Device("two"), leases);
        var plan = new ActionPlan("tap", InputResource.Keyboard, [InputCommand.PressDown(4)], TimeSpan.FromSeconds(1));
        Assert.All(await Task.WhenAll(first.ExecuteAsync(plan, default).AsTask(), second.ExecuteAsync(plan, default).AsTask()),
            r => Assert.Equal(ActionState.Succeeded, r.State));
    }
    [Fact] public async Task InvalidResourceClaimsCannotReachHardware()
    {
        var device = new Device(); await using var executor = new ActionExecutor(device, new());
        await Assert.ThrowsAsync<ArgumentException>(() => executor.ExecuteAsync(Hold() with { Resources = InputResource.None }, default).AsTask());
        Assert.Empty(device.Events);
    }
    [Fact] public async Task UnknownCommandsCannotReachHardware()
    {
        var device = new Device(); await using var executor = new ActionExecutor(device, new());
        var plan = Hold() with { Commands = [new((InputOperation)99)] };
        await Assert.ThrowsAsync<ArgumentException>(() => executor.ExecuteAsync(plan, default).AsTask());
        Assert.Empty(device.Events);
    }
    [Fact] public async Task SendFailureStillReleasesAndReportsFailure()
    {
        var device = new Device { ThrowSend = true }; await using var executor = new ActionExecutor(device, new());
        var result = await executor.ExecuteAsync(Hold(), default);
        Assert.Equal(ActionState.Failed, result.State); Assert.Equal("release-all", device.Events.Last());
    }
    [Fact] public async Task ReleaseFailureIsNeverReportedAsSuccess()
    {
        var device = new Device { ThrowRelease = true }; var executor = new ActionExecutor(device, new());
        var result = await executor.ExecuteAsync(Hold() with { Commands = [InputCommand.PressDown(4)] }, default);
        Assert.Equal(ActionState.Failed, result.State);
        device.ThrowRelease = false; await executor.DisposeAsync();
    }
    [Fact] public async Task DisposeCancelsActiveActionBeforeReleasingEndpoint()
    {
        var device = new Device(); var executor = new ActionExecutor(device, new());
        var pending = executor.ExecuteAsync(Hold(), default).AsTask(); await device.Down.Task;
        await executor.DisposeAsync();
        Assert.Equal(ActionState.Cancelled, (await pending).State); Assert.True(device.Disposed);
    }

    [Fact] public async Task PersistentInputKeepsSameOwnerAndReleasesBeforeAnotherOwner()
    {
        var device = new Device(); var executor = new ActionExecutor(device, new());
        var first = new ActionPlan("hold", InputResource.Keyboard, [InputCommand.PressDown(4)],
            TimeSpan.FromSeconds(1), InputRetention.UntilOwnerChanges, "movement");
        await executor.ExecuteAsync(first, default);
        Assert.DoesNotContain("release-all", device.Events);
        await executor.ExecuteAsync(first with { Id = "continue", Commands = [InputCommand.PressDown(5)] }, default);
        Assert.DoesNotContain("release-all", device.Events);
        await executor.ExecuteAsync(first with { Id = "other", Owner = "ui", Priority = 1, Commands = [InputCommand.PressDown(6)] }, default);
        var events = device.Events.ToArray();
        Assert.True(Array.IndexOf(events, "release-all") < Array.IndexOf(events, "KeyDown:6"));
        await executor.DisposeAsync();
        Assert.Equal("release-all", device.Events.Last());
    }
}
