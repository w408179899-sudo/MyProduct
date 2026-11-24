using Roadhog.Application.Trading;
using Roadhog.Core.Api;
using Roadhog.Core.Diagnostics;
using Roadhog.Core.Model;

namespace Roadhog.Application.ManastoneExchange;

internal sealed record ManastoneRewardOpenResult(ulong Bags, ulong Bundles);

internal sealed class ManastoneRewardOpenSequence(TradingActions actions, IRoadhogSnapshotReader snapshots,
    IRoadhogLogger logger, string account, CancellationToken token)
{
    private const int SettlementInactivityMs = 30_000;
    private const ulong StonesPerReward = 10;

    public async Task<ManastoneRewardOpenResult> OpenAsync(IProgress<string>? progress = null)
    {
        var inventory = await Inventory();
        if (NextStack(inventory) == null) return new(0, 0);

        var ui = await Bag();
        TradingActions.Require(Idle(ui), "存在其他背包操作或弹窗，停止开启魔石奖励。");
        if (!ui.IsOpen) await ToggleBag(open: true);

        var bagId = ManastoneExchangePlan.Quests[0].RewardId;
        ulong bags = 0, bundles = 0;
        while (NextStack(inventory) is { } item)
        {
            progress?.Report($"正在开启{item.Name}，当前一堆 {item.Count} 个。");
            var settled = await OpenStack(item, inventory);
            if (item.TemplateId == bagId) bags += settled.Opened;
            else bundles += settled.Opened;
            inventory = settled.Inventory;
        }

        return new(bags, bundles);
    }

    public Task CloseBagAsync() => ToggleBag(open: false);

    private async Task<(ulong Opened, IReadOnlyList<InventoryItemSnapshot> Inventory)> OpenStack(
        InventoryItemSnapshot item, IReadOnlyList<InventoryItemSnapshot> before)
    {
        var rewardCount = ManastoneExchangePlan.Count(before, item.TemplateId);
        var stoneCount = MagicStoneCount(before);
        ulong Consumed(IReadOnlyList<InventoryItemSnapshot> inventory)
        {
            var remaining = inventory.Where(i => InBag(i) && i.TemplateId == item.TemplateId && i.InstanceId == item.InstanceId)
                .Aggregate(0UL, (sum, i) => sum + i.Count);
            if (remaining >= item.Count) return 0;
            var consumed = item.Count - remaining;
            return ManastoneExchangePlan.Count(inventory, item.TemplateId) == rewardCount - consumed ? consumed : 0;
        }

        async Task<(InventoryInteractionSnapshot Ui, IReadOnlyList<InventoryItemSnapshot> Inventory)> Observe() =>
            (await Bag(), await Inventory());

        var ui = await Bag();
        TradingActions.Require(Idle(ui) && ui.IsOpen, "背包窗口发生变化，停止开启魔石奖励。");
        await actions.RightClickBag(item, NpcClosed, viaResetPoint: false);
        // One item may open directly. A larger stack requires the quantity dialog.
        var opened = await actions.Wait(Observe, s => s.Ui.ItemUseQuantity != null ||
            (item.Count == 1 && s.Ui.IsOpen && Idle(s.Ui) && Consumed(s.Inventory) == 1));
        var amount = 1UL;
        if (opened.Ui.ItemUseQuantity is { } popup)
        {
            bool Editing(InventoryInteractionSnapshot state) => state.IsOpen && NoOtherInventoryAction(state) &&
                state.ItemUseQuantity is { } quantity && quantity.InstanceId == item.InstanceId &&
                quantity.Maximum == popup.Maximum && quantity.Maximum > 0 && quantity.Maximum <= item.Count;
            TradingActions.Require(Editing(opened.Ui), "开包数量窗口与目标物品不符。");
            amount = popup.Maximum;
            await actions.Click(Bag, s => s.ItemUseQuantity?.AllButton, Editing);
            await actions.Wait(Bag, s => Editing(s) && s.ItemUseQuantity!.Quantity == amount);
            await actions.Click(Bag, s => s.ItemUseQuantity?.ConfirmButton,
                s => Editing(s) && s.ItemUseQuantity!.Quantity == amount);
        }

        // ALL opens one reward at a time. Each confirmed inventory advance
        // starts a new inactivity timeout while the game processes the batch.
        var settlement = opened;
        ulong settledCount = 0;
        while (settledCount < amount)
        {
            settlement = await actions.Wait(Observe, s =>
            {
                if (!s.Ui.IsOpen || !Idle(s.Ui)) return false;
                var consumed = Consumed(s.Inventory);
                return consumed > settledCount && consumed <= amount &&
                    MagicStoneCount(s.Inventory) >= stoneCount + consumed * StonesPerReward;
            }, SettlementInactivityMs);
            settledCount = Consumed(settlement.Inventory);
        }

        logger.Info("manastone_exchange.rewards_opened", new Dictionary<string, object?>
        {
            ["account"] = account, ["rewardId"] = item.TemplateId, ["instanceId"] = item.InstanceId,
            ["opened"] = amount, ["stonesGained"] = MagicStoneCount(settlement.Inventory) - stoneCount
        });
        return (amount, settlement.Inventory);
    }

    private async Task ToggleBag(bool open)
    {
        var state = await Bag();
        TradingActions.Require(Idle(state), "存在其他背包操作或弹窗，无法切换背包。");
        if (state.IsOpen == open) return;
        await actions.Key("I");
        await actions.Wait(Bag, s => s.IsOpen == open);
    }

    private async Task<InventoryInteractionSnapshot> Bag()
    {
        TradingActions.Require(await NpcClosed(), "NPC 对话仍然打开，停止开启魔石奖励。");
        return (await snapshots.ReadInventoryInteractionAsync().WaitAsync(token)).Value;
    }

    private async Task<bool> NpcClosed() => !(await snapshots.ReadManastoneExchangeAsync().WaitAsync(token)).Value.IsOpen;

    private async Task<IReadOnlyList<InventoryItemSnapshot>> Inventory() =>
        (await snapshots.ReadInventoryAsync().WaitAsync(token)).Value;

    private static InventoryItemSnapshot? NextStack(IReadOnlyList<InventoryItemSnapshot> inventory) =>
        inventory.Where(i => InBag(i) && i.Count > 0 && ManastoneExchangePlan.Quests.Any(q => q.RewardId == i.TemplateId))
            .OrderBy(i => i.Slot).FirstOrDefault();

    private static ulong MagicStoneCount(IReadOnlyList<InventoryItemSnapshot> inventory) =>
        inventory.Where(i => InBag(i) && i.ItemType == 24).Aggregate(0UL, (sum, i) => sum + i.Count);

    private static bool InBag(InventoryItemSnapshot item) => !item.IsEquipped && item.Slot >= 0;

    private static bool Idle(InventoryInteractionSnapshot state) =>
        !state.OtherModalOpen && state.ItemUseQuantity == null && NoOtherInventoryAction(state);

    private static bool NoOtherInventoryAction(InventoryInteractionSnapshot state) =>
        !state.ShopIsOpen && !state.IsSelling && state.DiscardDialog == null && state.PendingDiscardInstanceId == 0;
}
