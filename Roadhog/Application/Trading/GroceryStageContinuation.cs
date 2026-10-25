using Roadhog.Core.Api;
using Roadhog.Application.BagCleanup;
using Roadhog.Application.Workers;
using Roadhog.Core.Input;

namespace Roadhog.Application.Trading;

/// <summary>Ends a failed grocery stage without replaying trades or blocking later stages.</summary>
internal sealed class GroceryStageContinuation(AccountWorkerContext context, IKeyboardInput input,
    Func<int, CancellationToken, Task>? delay = null)
{
    private readonly TradingActions actions = new(input, context.Snapshots, context.StopToken, delay);
    public int FailureCount { get; private set; }

    public void RecordFailure(string stage, Exception error)
    {
        FailureCount++;
        context.Logger.Warn("grocery_shop.stage_skipped", new Dictionary<string, object?>
        { ["account"] = context.Config.AccountName, ["stage"] = stage, ["error"] = error.Message });
    }

    public async Task<bool> TryAsync(string stage, Func<Task> action)
    {
        context.StopToken.ThrowIfCancellationRequested();
        try { await action(); context.StopToken.ThrowIfCancellationRequested(); return true; }
        catch (SnapshotReadBudgetExceededException) { throw; }
        catch (Exception ex) when (!context.StopToken.IsCancellationRequested)
        { RecordFailure(stage, ex); return false; }
    }

    public async Task ReleaseAsync()
    {
        // Releasing held inputs remains necessary even when the user stops the worker.
        try { await actions.Reset(); }
        catch (Exception ex) { RecordFailure("release_inputs", ex); }
    }

    public async Task CloseWindowsAsync()
    {
        await ReleaseAsync();
        await TryAsync("completion_jump", () => actions.Key("Space"));
        await TryAsync("close_npc", async () =>
        {
            async Task<Roadhog.Core.Model.NpcTradeSnapshot> Ui() =>
                (await context.Snapshots.ReadNpcTradeAsync().WaitAsync(context.StopToken)).Value;
            try { await actions.Wait(Ui, s => !s.IsOpen && !s.DialogOpen && !s.OtherModalOpen, 2000); }
            catch (TradingActions.ConfirmationTimeoutException)
            {
                // Cancel a pending quantity/error dialog; never confirm or resubmit it.
                await actions.Key("Escape");
                await actions.Key("Space");
                await actions.Wait(Ui, s => !s.IsOpen && !s.DialogOpen && !s.OtherModalOpen, 2000);
            }
        });
        await TryAsync("close_inventory", async () =>
        {
            async Task<Roadhog.Core.Model.InventoryInteractionSnapshot> Bag() =>
                (await context.Snapshots.ReadInventoryInteractionAsync().WaitAsync(context.StopToken)).Value;
            var bag = await Bag();
            InventoryDiscardActions.RequireIdle(bag);
            if (bag.IsOpen)
            {
                await actions.Key("I");
                await actions.Wait(Bag, s => !s.IsOpen, 2000);
            }
        });
        await ReleaseAsync();
    }
}
