using Roadhog.Application.Trading;
using Roadhog.Application.Workers;
using Roadhog.Core.Accounts;
using Roadhog.Core.Input;
using Roadhog.Core.Model;

namespace Roadhog.Application.BagCleanup;

/// <summary>NPC sale UI actions; pathing and return remain owned by the cleanup controller.</summary>
public sealed class NpcSaleSequence(IKeyboardInput input, Func<int, CancellationToken, Task>? delay = null)
{
    public async Task RunAsync(AccountWorkerContext context, string npcName, BagCleanupState state)
    {
        var token = context.StopToken;
        var snapshots = context.Snapshots;
        var actions = new TradingActions(input, snapshots, token, delay);
        var selected = (await snapshots.ReadLockedTargetAsync().WaitAsync(token)).Value;
        TradingActions.Require(selected.HasTarget && selected.ServerObjectId != 0 && selected.Name == npcName,
            "出售前选中的 NPC 与清包配置不符。");
        var npcId = selected.ServerObjectId;
        async Task<NpcTradeSnapshot> Ui()
        {
            var ui = (await snapshots.ReadNpcTradeAsync().WaitAsync(token)).Value;
            var target = (await snapshots.ReadLockedTargetAsync().WaitAsync(token)).Value;
            TradingActions.Require(target.ServerObjectId == npcId && target.Name == npcName, "出售时选中的 NPC 已改变。");
            return ui;
        }
        bool Selling(NpcTradeSnapshot ui) => ui.IsSelling && ui.NpcServerObjectId == npcId && !ui.OtherModalOpen;
        async Task<InventoryInteractionSnapshot> BagUi() => (await snapshots.ReadInventoryInteractionAsync().WaitAsync(token)).Value;
        async Task InventoryOpen(bool open)
        {
            var ui = await BagUi();
            InventoryDiscardActions.RequireIdle(ui);
            if (ui.IsOpen != open) await actions.Key("I");
            await actions.Wait(BagUi, s => s.IsOpen == open && !s.OtherModalOpen && s.DiscardDialog == null);
        }
        try
        {
            await actions.Reset();
            await InventoryOpen(false);
            var current = await Ui();
            if (!Selling(current))
            {
                await actions.Click(Ui, s => s.SellEntry, s => s.DialogOpen && !s.OtherModalOpen);
                await actions.Wait(Ui, Selling);
            }
            TradingActions.Require((await Ui()).Basket.Count == 0, "商人出售列表已有物品，停止以免出售未配置物品。");
            state.MarkSellItemEntryClicked();
            while (true)
            {
                var inventory = (await snapshots.ReadInventoryAsync().WaitAsync(token)).Value;
                var candidates = BagCleanupItemMatcher.SelectSellRegistrationItems(inventory, context.Config.ScriptSettings!.Maintenance);
                if (candidates.Count == 0) break;
                var batch = BagCleanupSellBatchPlanner.SelectNextBatch(candidates).Items;
                state.SetSellCandidates(batch, candidates.Count);
                await InventoryOpen(true);
                var registered = new List<InventoryItemSnapshot>();
                foreach (var item in batch)
                {
                    await actions.RightClickBag(item, async () => Selling(await Ui()) && Matches((await Ui()).Basket, registered));
                    registered.Add(item);
                    await actions.Wait(Ui, s => Selling(s) && Matches(s.Basket, registered));
                    context.Logger.Info("bag_cleanup.sell.registration_verified", new Dictionary<string, object?>
                    {
                        ["account"] = context.Config.AccountName, ["name"] = item.Name,
                        ["instanceId"] = item.InstanceId, ["quantity"] = item.Count
                    });
                }
                state.MarkSellItemsRegistered(batch.Count);
                await InventoryOpen(false);
                await actions.Wait(Ui, s => Selling(s) && !s.InventoryOpen && s.SellButton != null);
                var beforeMoney = (await snapshots.ReadInventoryMoneyAsync().WaitAsync(token)).Value;
                state.SetInitialMoney(beforeMoney);
                await actions.Click(Ui, s => s.SellButton, s => Selling(s) && !s.InventoryOpen && Matches(s.Basket, batch));
                state.MarkSellButtonClicked();
                // Submit once; prove both item removal and money receipt before another batch.
                var after = await actions.Wait(async () => (
                    Ui: await Ui(), Inventory: (await snapshots.ReadInventoryAsync().WaitAsync(token)).Value,
                    Money: (await snapshots.ReadInventoryMoneyAsync().WaitAsync(token)).Value),
                    s => Selling(s.Ui) && s.Ui.Basket.Count == 0 && s.Money > beforeMoney &&
                        batch.All(i => s.Inventory.All(a => a.InstanceId != i.InstanceId)));
                var delta = after.Money - beforeMoney;
                state.MarkSellBatchVerified(delta);
                context.Logger.Info("bag_cleanup.sell.batch_verified", new Dictionary<string, object?>
                {
                    ["account"] = context.Config.AccountName, ["count"] = batch.Count,
                    ["moneyBefore"] = beforeMoney, ["moneyAfter"] = after.Money, ["moneyDelta"] = delta,
                    ["remaining"] = BagCleanupItemMatcher.SelectSellRegistrationItems(after.Inventory, context.Config.ScriptSettings.Maintenance).Count
                });
            }
        }
        finally { await actions.Reset(); }
    }

    private static bool Matches(IReadOnlyList<NpcTradeItem> actual, IReadOnlyList<InventoryItemSnapshot> expected) =>
        actual.Count == expected.Count && expected.All(i => actual.Any(a => a.InstanceId == i.InstanceId &&
            a.TemplateId == i.TemplateId && a.Quantity == i.Count));
}
