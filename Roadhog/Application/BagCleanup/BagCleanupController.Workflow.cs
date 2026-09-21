using Roadhog.Application.Workers;
using Roadhog.Core.Accounts;

namespace Roadhog.Application.BagCleanup;

public sealed class CleanupCombatInterruptionException : Exception
{
    public CleanupCombatInterruptionException() : base("清包被攻击打断，先处理战斗。") { }
}

public sealed class CleanupDeathInterruptionException : Exception
{
    public CleanupDeathInterruptionException() : base("清包期间角色死亡，先复活再继续。") { }
}

public sealed partial class BagCleanupController
{
    public static TimeSpan FullCleanupCooldown => ReadCleanupCooldown();
    public async Task ReturnToTownRequestedAsync(AccountWorkerContext context, Action<string> report, string? destinationPathName = null)
    {
        var state = new BagCleanupState();
        state.Start(0, 0);
        state.ReturnDestinationPathName = destinationPathName;
        while (state.Active && state.Step != BagCleanupStep.LoadCleanupPath)
        {
            report("清包前回城，等待回城位置确认");
            var result = await TickWorkflowAsync(context, state);
            if (result.Reason == "town_return_interrupted_by_attack") throw new CleanupCombatInterruptionException();
            EnsureRunning(result);
            await Task.Delay(100, context.StopToken);
        }
        if (state.Step != BagCleanupStep.LoadCleanupPath) throw new InvalidOperationException("清包前回城未确认完成。");
    }

    public async Task ReturnToReviveRequestedAsync(AccountWorkerContext context, Action<string> report)
    {
        var state = new BagCleanupState();
        state.Start(0, 0);
        state.Advance(BagCleanupStep.PressReturnToRevive);
        while (state.Active)
        {
            report("清包后返回复活点，等待回城位置确认");
            var result = await TickWorkflowAsync(context, state);
            EnsureRunning(result);
            if (result.Status == BagCleanupTickStatus.Completed) return;
            await Task.Delay(100, context.StopToken);
        }
        throw new InvalidOperationException("返回复活点未确认完成。");
    }

    /// <summary>The caller owns trigger/cooldown and starts at the shared grinding origin.</summary>
    public async Task RunRequestedAsync(AccountWorkerContext context, Action<string> report)
    {
        var state = new BagCleanupState();
        var settings = context.Config.ScriptSettings!.Maintenance;
        var bag = (await context.Snapshots.ReadInventoryAsync().WaitAsync(context.StopToken)).Value;
        var discard = BagCleanupItemMatcher.SelectDiscardItems(bag, settings);
        if (discard.Count > 0)
        {
            state.StartDiscard(0, int.MaxValue, discard.Count);
            while (state.Active && state.Step != BagCleanupStep.CloseDiscardInventory)
            {
                context.StopToken.ThrowIfCancellationRequested();
                report("正在按配置丢弃背包物品");
                var result = await TickWorkflowAsync(context, state);
                if (result.Reason == "discard_interrupted_by_attack")
                {
                    var ui = (await context.Snapshots.ReadInventoryInteractionAsync().WaitAsync(context.StopToken)).Value;
                    if (ui.PendingDiscardInstanceId == 0 && ui.DiscardDialog == null && !ui.OtherModalOpen && !ui.IsOpen)
                        throw new CleanupCombatInterruptionException();
                }
                EnsureRunning(result);
                await Task.Delay(100, context.StopToken);
            }
            if (state.Step != BagCleanupStep.CloseDiscardInventory) throw new InvalidOperationException("丢弃清包被中断。");
            var close = await _discarder.CloseInventoryWindowIfOpenAsync(context);
            if (!close.Success) throw new InvalidOperationException(close.Error);
        }
        bag = (await context.Snapshots.ReadInventoryAsync().WaitAsync(context.StopToken)).Value;
        if (BagCleanupItemMatcher.SelectSellRegistrationItems(bag, settings).Count == 0) return;
        state.Start(0, 0);
        state.Advance(BagCleanupStep.LoadCleanupPath);
        while (state.Active)
        {
            context.StopToken.ThrowIfCancellationRequested();
            report(state.Step is BagCleanupStep.LoadCleanupPath or BagCleanupStep.FollowCleanupPath ? "沿清包路径前往商人" :
                state.Step is BagCleanupStep.ReturnByReversePath or BagCleanupStep.PostCleanupJump ? "出售完成，沿清包路径返回" : "正在向商人出售背包物品");
            var result = await TickWorkflowAsync(context, state);
            EnsureRunning(result);
            if (result.Status == BagCleanupTickStatus.Completed) return;
            await Task.Delay(100, context.StopToken);
        }
        throw new InvalidOperationException("出售清包未完成。");
    }
    private static void EnsureRunning(BagCleanupTickResult result)
    {
        if (result.Status is not (BagCleanupTickStatus.Running or BagCleanupTickStatus.Completed))
            throw new InvalidOperationException(result.Error ?? result.Reason);
    }

    private async Task<BagCleanupTickResult> TickWorkflowAsync(AccountWorkerContext context, BagCleanupState state)
    {
        async Task CheckLifeAsync()
        {
            context.StopToken.ThrowIfCancellationRequested();
            if (state.ReturnTransition?.Active == true) return; // The coherent scene observation owns life checks during recall.
            if ((await context.Snapshots.ReadPlayerAsync().WaitAsync(context.StopToken)).Value.IsDead)
            {
                if (state.DiscardActive)
                {
                    var cancel = await _discarder.CancelPendingDiscardAsync(context);
                    var close = await _discarder.CloseInventoryWindowIfOpenAsync(context);
                    context.Logger.Warn("bag_cleanup.discard.death_interrupted", new Dictionary<string, object?>
                    {
                        ["account"] = context.Config.AccountName,
                        ["cancelSuccess"] = cancel.Success, ["cancelError"] = cancel.Error,
                        ["inventoryCloseSuccess"] = close.Success, ["inventoryCloseError"] = close.Error
                    });
                }
                throw new CleanupDeathInterruptionException();
            }
        }
        await CheckLifeAsync();
        var result = await TickAfterLootAsync(context, state);
        // Path execution can observe a death during the tick. Hand off before return/jump actions.
        await CheckLifeAsync();
        return result;
    }
}
