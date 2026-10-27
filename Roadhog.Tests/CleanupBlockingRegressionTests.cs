using Roadhog.Application.Trading;
using Roadhog.Core.Api;
using Roadhog.Core.Diagnostics;
using Roadhog.Core.Model;
using Roadhog.Infrastructure.Vmm;

internal static class CleanupBlockingRegressionTests
{
    private static void Check(bool condition, string message)
    { if (!condition) throw new Exception(message); }

    public static async Task UnresponsiveCaptureAsync()
    {
        var late = new TaskCompletionSource<ChannelTransitionSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        var api = new FakeGameApi();
        var calls = 0;
        api.TransitionReadAsync = _ => { Interlocked.Increment(ref calls); return late.Task; };
        var reader = new RoadhogSnapshotReader(new(), api, new InMemoryRoadhogLogger(), CancellationToken.None);
        try
        {
            for (var i = 0; i < 3; i++)
            {
                using var budget = SnapshotReadBudget.Enter(TimeSpan.FromMilliseconds(40));
                try { await reader.ReadChannelTransitionAsync().WaitAsync(TimeSpan.FromSeconds(2)); throw new Exception("deadline expected"); }
                catch (SnapshotReadBudgetExceededException ex) { Check(ex.Channel == "channel_transition", "timeout identifies the blocked channel"); }
            }
            Check(calls == 1, "unresponsive native read must not accumulate captures on later attempts");
            Check((await reader.ReadPlayerAsync()).Value.IsAlive, "an unrelated channel remains usable");
        }
        finally { late.TrySetResult(new(true, api.Player, api.Channel, DateTimeOffset.UtcNow)); }
        api.TransitionReadAsync = null;
        api.TransitionRead = () => new(false, null, null, DateTimeOffset.UtcNow);
        using (SnapshotReadBudget.Enter(TimeSpan.FromSeconds(1)))
        {
            var fresh = await reader.ReadChannelTransitionAsync();
            Check(fresh.Version == 1 && !fresh.Value.IsReady, "late cancelled result cannot publish or replace the next loading observation");
        }

        using var stop = new CancellationTokenSource(40);
        var blocked = new TaskCompletionSource<ChannelTransitionSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        api.TransitionReadAsync = _ => blocked.Task;
        var stoppedReader = new RoadhogSnapshotReader(new(), api, new InMemoryRoadhogLogger(), stop.Token);
        try
        {
            try { await stoppedReader.ReadChannelTransitionAsync().WaitAsync(TimeSpan.FromSeconds(2)); throw new Exception("stop expected"); }
            catch (OperationCanceledException ex) { Check(ex is not SnapshotReadBudgetExceededException, "user stop retains its meaning"); }
        }
        finally { blocked.TrySetResult(new(false, null, null, DateTimeOffset.UtcNow)); }
    }

    public static async Task ConfirmationCancelsProviderAsync()
    {
        var api = new FakeGameApi();
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        api.TransitionReadAsync = async token =>
        {
            try { await Task.Delay(Timeout.Infinite, token); throw new Exception("unreachable"); }
            finally { cancelled.TrySetResult(); }
        };
        var reader = new RoadhogSnapshotReader(new(), api, new InMemoryRoadhogLogger(), CancellationToken.None);
        var actions = new TradingActions(new RecordingKeyboardInput(), reader, CancellationToken.None);
        var laterReads = 0;
        using var budget = SnapshotReadBudget.Enter(TimeSpan.FromSeconds(5));
        try
        {
            await actions.Wait(async () =>
            {
                await reader.ReadChannelTransitionAsync();
                Interlocked.Increment(ref laterReads);
                return (await reader.ReadPlayerAsync()).Value;
            }, _ => false, timeoutMs: 40);
            throw new Exception("confirmation deadline expected");
        }
        catch (TradingActions.ConfirmationTimeoutException) { }
        await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(1));
        Check(laterReads == 0, "expired composite confirmation cannot continue to further reads");
        Check((await reader.ReadPlayerAsync()).Value.IsAlive, "action cancellation scope must be restored before the next action");
    }
}
