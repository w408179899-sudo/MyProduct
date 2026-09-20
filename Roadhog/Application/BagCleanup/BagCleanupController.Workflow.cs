using Roadhog.Application.Workers;
using Roadhog.Core.Accounts;

namespace Roadhog.Application.BagCleanup;

public sealed class CleanupCombatInterruptionException : Exception
{
    public CleanupCombatInterruptionException() : base("清包被攻击打断，先处理战斗。") { }
}

public sealed partial class BagCleanupController
{
    public static TimeSpan FullCleanupCooldown => ReadCleanupCooldown();
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
                var result = await TickAfterLootAsync(context, state);
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
            var result = await TickAfterLootAsync(context, state);
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

}
