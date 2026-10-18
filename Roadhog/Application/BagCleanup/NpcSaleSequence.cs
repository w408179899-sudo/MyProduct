using Roadhog.Application.Trading;
using Roadhog.Application.Workers;
using Roadhog.Core.Accounts;
using Roadhog.Core.Input;
using Roadhog.Core.Model;

namespace Roadhog.Application.BagCleanup;

public enum NpcSaleOutcome { AllConfiguredItemsSold, NoSale }

/// <summary>NPC sale UI actions; pathing and return remain owned by the cleanup controller.</summary>
public sealed class NpcSaleSequence(IKeyboardInput input, Func<int, CancellationToken, Task>? delay = null,
    int confirmationTimeoutMs = 8000)
{
    public async Task<NpcSaleOutcome> RunAsync(AccountWorkerContext context, string npcName, BagCleanupState state,
        Func<IReadOnlyList<InventoryItemSnapshot>, IReadOnlyList<InventoryItemSnapshot>>? selectCandidates = null,
        int batchSize = 1, bool registerAllJunkFirst = false)
    {
        if (batchSize is < 1 or > 3) throw new ArgumentOutOfRangeException(nameof(batchSize));
        selectCandidates ??= bag => BagCleanupItemMatcher.SelectSellRegistrationItems(bag, context.Config.ScriptSettings!.Maintenance);
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
        async Task<bool> SubmitBatch(InventoryItemSnapshot[] batch, int remaining, string stage)
        {
            state.MarkSellItemsRegistered(batch.Length);
            await actions.Wait(Ui, s => Selling(s) && s.InventoryOpen && s.SellButton != null);
            var beforeMoney = (await snapshots.ReadInventoryMoneyAsync().WaitAsync(token)).Value;
            state.SetInitialMoney(beforeMoney);
            await actions.Click(Ui, s => s.SellButton, s => Selling(s) && s.InventoryOpen && Matches(s.Basket, batch));
            state.MarkSellButtonClicked();
            // Submit once; prove both item removal and money receipt before another batch.
            async Task<(NpcTradeSnapshot Ui, IReadOnlyList<InventoryItemSnapshot> Inventory, ulong Money)> ReadResult() => (
                Ui: await Ui(), Inventory: (await snapshots.ReadInventoryAsync().WaitAsync(token)).Value,
                Money: (await snapshots.ReadInventoryMoneyAsync().WaitAsync(token)).Value);
            bool Sold((NpcTradeSnapshot Ui, IReadOnlyList<InventoryItemSnapshot> Inventory, ulong Money) s) =>
                Selling(s.Ui) && s.Ui.Basket.Count == 0 && s.Money > beforeMoney &&
                batch.All(i => s.Inventory.All(a => a.InstanceId != i.InstanceId));
            (NpcTradeSnapshot Ui, IReadOnlyList<InventoryItemSnapshot> Inventory, ulong Money) after;
            try { after = await actions.Wait(ReadResult, Sold, confirmationTimeoutMs); }
            catch (TradingActions.ConfirmationTimeoutException)
            {
                after = await ReadResult();
                await actions.Alive();
                // A rejected sale clears its basket while keeping the entire batch and balance.
                if (Selling(after.Ui) && after.Ui.InventoryOpen && after.Ui.Basket.Count == 0 && after.Money == beforeMoney &&
                    batch.All(i => after.Inventory.Any(a => a.InstanceId == i.InstanceId && a.TemplateId == i.TemplateId && a.Count == i.Count && !a.IsEquipped)))
                {
                    context.Logger.Info("bag_cleanup.sell.no_sale", new Dictionary<string, object?>
                    {
                        ["account"] = context.Config.AccountName, ["count"] = batch.Length, ["stage"] = stage,
                        ["money"] = after.Money, ["remaining"] = remaining,
                        ["reason"] = "basket_cleared_items_and_money_unchanged"
                    });
                    await InventoryOpen(false);
                    return false;
                }
                if (!Sold(after)) throw;
            }
            var delta = after.Money - beforeMoney;
            state.MarkSellBatchVerified(delta);
            context.Logger.Info("bag_cleanup.sell.batch_verified", new Dictionary<string, object?>
            {
                ["account"] = context.Config.AccountName, ["count"] = batch.Length, ["stage"] = stage,
                ["instanceIds"] = batch.Select(i => i.InstanceId).ToArray(),
                ["moneyBefore"] = beforeMoney, ["moneyAfter"] = after.Money, ["moneyDelta"] = delta,
                ["remaining"] = selectCandidates(after.Inventory).Count
            });
            return true;
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
            await actions.BringBagToFront(async () => Selling(await Ui()) && (await Ui()).Basket.Count == 0);
            if (registerAllJunkFirst)
            {
                var junk = await NpcJunkRegistrationSequence.RunAsync(context, actions, Ui, Selling);
                if (junk.Length == 0)
                {
                    // The requested initial sell click is a no-op when the game's junk selection is empty.
                    await actions.Click(Ui, s => s.SellButton, s => Selling(s) && s.InventoryOpen && s.Basket.Count == 0);
                    await actions.Pause(200);
                    TradingActions.Require(Selling(await Ui()) && (await Ui()).Basket.Count == 0, "空杂货出售后商人列表变化。");
                    context.Logger.Info("bag_cleanup.sell.junk_empty", new Dictionary<string, object?> { ["account"] = context.Config.AccountName });
                }
                else
                {
                    state.SetSellCandidates(junk, junk.Length);
                    if (!await SubmitBatch(junk, junk.Length, "automatic_junk")) return NpcSaleOutcome.NoSale;
                }
            }
            while (true)
            {
                var inventory = (await snapshots.ReadInventoryAsync().WaitAsync(token)).Value;
                var candidates = selectCandidates(inventory);
                if (candidates.Count == 0)
                {
                    await InventoryOpen(false);
                    return NpcSaleOutcome.AllConfiguredItemsSold;
                }
                // The ordinary cleanup keeps its one-entry default; grocery cleanup opts into three.
                var batch = candidates.OrderBy(i => i.VendorSellUnitPrice).ThenBy(i => i.Slot).Take(batchSize).ToArray();
                state.SetSellCandidates(batch, candidates.Count);
                var registered = new List<InventoryItemSnapshot>();
                foreach (var item in batch)
                {
                    context.Logger.Info("bag_cleanup.sell.registration_started", new Dictionary<string, object?>
                    { ["account"] = context.Config.AccountName, ["name"] = item.Name, ["instanceId"] = item.InstanceId, ["quantity"] = item.Count });
                    await actions.RightClickBag(item, async () => Selling(await Ui()) && Matches((await Ui()).Basket, registered), viaResetPoint: false);
                    registered.Add(item);
                    await actions.Wait(Ui, s => Selling(s) && Matches(s.Basket, registered));
                    context.Logger.Info("bag_cleanup.sell.registration_verified", new Dictionary<string, object?>
                    {
                        ["account"] = context.Config.AccountName, ["name"] = item.Name,
                        ["instanceId"] = item.InstanceId, ["quantity"] = item.Count
                    });
                }
                if (!await SubmitBatch(batch, candidates.Count, "configured")) return NpcSaleOutcome.NoSale;
            }
        }
        finally { await actions.Reset(); }
    }

    private static bool Matches(IReadOnlyList<NpcTradeItem> actual, IReadOnlyList<InventoryItemSnapshot> expected) =>
        actual.Count == expected.Count && expected.All(i => actual.Any(a => a.InstanceId == i.InstanceId &&
            a.TemplateId == i.TemplateId && a.Quantity == i.Count));
}
