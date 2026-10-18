using Roadhog.Application.Trading;
using Roadhog.Application.Workers;
using Roadhog.Core.Accounts;
using Roadhog.Core.Input;
using Roadhog.Core.Model;
using static Roadhog.Application.Trading.TradingActions;

namespace Roadhog.Application.BagCleanup;

public sealed class ArrowPurchaseUnconfirmedException(Exception inner) : Exception("补箭购买结果未确认，停止后检查背包再启动。", inner);

/// <summary>Only replenishes arrows; route, sale and post-sale jump belong to the caller.</summary>
public sealed class NpcArrowPurchaseSequence(IKeyboardInput input, Func<int, CancellationToken, Task>? delay = null,
    int confirmationTimeoutMs = 8000)
{
    public static async Task<bool> NeededAsync(AccountWorkerContext context, bool duringCleanup = true)
    {
        var player = (await context.Snapshots.ReadPlayerAsync().WaitAsync(context.StopToken)).Value;
        if (player.IsDead || player.CharacterClassId != AionClassId.Ranger) return false;
        return ArrowRestockPolicy.NeedsRestock(player, (await context.Snapshots.ReadInventoryAsync().WaitAsync(context.StopToken)).Value, duringCleanup);
    }

    public async Task RunAsync(AccountWorkerContext context, string npcName, Action<string>? report = null)
    {
        if (!await NeededAsync(context)) return;
        var token = context.StopToken;
        var actions = new TradingActions(input, context.Snapshots, token, delay);
        async Task<NpcTradeSnapshot> Ui() => (await context.Snapshots.ReadNpcTradeAsync().WaitAsync(token)).Value;
        async Task<IReadOnlyList<InventoryItemSnapshot>> Bag() => (await context.Snapshots.ReadInventoryAsync().WaitAsync(token)).Value;
        async Task<ulong> Money() => (await context.Snapshots.ReadInventoryMoneyAsync().WaitAsync(token)).Value;
        try
        {
            await actions.Reset();
            var initial = await Ui();
            Require(!initial.IsOpen && !initial.DialogOpen && !initial.OtherModalOpen, "补箭前商人窗口尚未关闭。");
            var inventoryUi = (await context.Snapshots.ReadInventoryInteractionAsync().WaitAsync(token)).Value;
            InventoryDiscardActions.RequireIdle(inventoryUi);
            if (inventoryUi.IsOpen) await actions.Key("I");
            var npc = new BagCleanupNpcInteractor(input);
            Check(await npc.SelectConfiguredNpcAsync(context, npcName));
            Check(await npc.OpenDialogAsync(context));
            var selected = (await context.Snapshots.ReadLockedTargetAsync().WaitAsync(token)).Value;
            Require(selected.ServerObjectId != 0 && selected.Name == npcName, "补箭的商人与配置不符。");
            var npcId = selected.ServerObjectId;
            async Task<NpcTradeSnapshot> BoundUi()
            {
                var ui = await Ui();
                var target = (await context.Snapshots.ReadLockedTargetAsync().WaitAsync(token)).Value;
                Require(target.ServerObjectId == npcId && target.Name == npcName, "购买弓箭时选中的商人改变。");
                return ui;
            }
            bool Buying(NpcTradeSnapshot ui) => ui.IsBuying && ui.NpcServerObjectId == npcId && ui.Purchase != null && !ui.OtherModalOpen;
            await actions.Click(BoundUi, s => s.BuyEntry, s => s.DialogOpen && !s.OtherModalOpen);
            var shop = await actions.Wait(BoundUi, Buying);
            Require(shop.Purchase!.Basket.Count == 0 && shop.Purchase.QuantityDialog == null, "购买列表已有物品，停止补箭。");
            var arrow = shop.Purchase.Items.SingleOrDefault(i => i.TemplateId == ArrowRestockPolicy.ArrowTemplateId);
            Require(arrow is { Point: not null, UnitPrice: > 0 }, "当前杂货商没有可点击的弓箭商品。");
            var before = await Bag();
            var count = ArrowRestockPolicy.Count(before);
            var batches = ArrowRestockPolicy.Batches(count);
            var quantity = (ulong)batches * ArrowRestockPolicy.BatchCount;
            var beforeMoney = await Money();
            var cost = checked(quantity * arrow!.UnitPrice);
            Require(beforeMoney >= cost, "金币不足，无法补足弓箭。");
            report?.Invoke($"弓箭共 {count} 支，登录 {batches} 批，每批 10000 支");
            bool BasketMatches(NpcTradeSnapshot s, ulong expected) => Buying(s) &&
                s.Purchase!.Basket.All(i => i.TemplateId == arrow.TemplateId && i.InstanceId == arrow.ItemId) &&
                s.Purchase.Basket.Aggregate(0UL, (sum, i) => checked(sum + i.Quantity)) == expected;
            NpcShopItem? Item(NpcTradeSnapshot s) => s.Purchase?.Items.SingleOrDefault(i => i.ItemId == arrow.ItemId && i.TemplateId == arrow.TemplateId && i.UnitPrice == arrow.UnitPrice);
            for (var batch = 0; batch < batches; batch++)
            {
                var registered = (ulong)batch * ArrowRestockPolicy.BatchCount;
                var state = await BoundUi();
                Require(BasketMatches(state, registered) && state.Purchase!.QuantityDialog == null && Item(state)?.Point != null, "登录弓箭前购买列表发生变化。");
                try
                {
                    Check(await input.KeyDownAsync("ShiftKey", token));
                    await actions.Pause(100);
                    await actions.Move(Item(state)!.Point!);
                    await actions.Click(BoundUi, s => Item(s)?.Point,
                        s => BasketMatches(s, registered) && s.Purchase!.QuantityDialog == null && s.Purchase.HoveredItemId == arrow.ItemId,
                        RoadhogMouseButton.Right);
                    // The client handles the right-click on its next frame; keep Shift held through that frame.
                    await actions.Pause(100);
                }
                finally { await input.KeyUpAsync("ShiftKey", CancellationToken.None); }
                bool Editing(NpcTradeSnapshot s) => BasketMatches(s, registered) && s.Purchase!.QuantityDialog is { } q &&
                    q.ItemId == arrow.ItemId && q.Maximum == ArrowRestockPolicy.BatchCount;
                await actions.Wait(BoundUi, s => s.Purchase?.QuantityDialog != null);
                Require(Editing(await BoundUi()), "弓箭单批上限不是 10000，停止以免超量购买。");
                await actions.Click(BoundUi, s => s.Purchase?.QuantityDialog?.AllButton, Editing);
                await actions.Wait(BoundUi, s => Editing(s) && s.Purchase!.QuantityDialog!.Quantity == ArrowRestockPolicy.BatchCount);
                await actions.Click(BoundUi, s => s.Purchase?.QuantityDialog?.ConfirmButton,
                    s => Editing(s) && s.Purchase!.QuantityDialog!.Quantity == ArrowRestockPolicy.BatchCount);
                await actions.Wait(BoundUi, s => BasketMatches(s, registered + ArrowRestockPolicy.BatchCount) && s.Purchase!.QuantityDialog == null);
                context.Logger.Info("arrow_restock.batch_registered", new Dictionary<string, object?>
                    { ["account"] = context.Config.AccountName, ["batch"] = batch + 1, ["quantity"] = registered + ArrowRestockPolicy.BatchCount });
            }
            if (batches > 0)
            {
                Require(ArrowRestockPolicy.Count(await Bag()) == count && await Money() == beforeMoney, "购买前弓箭数量或金币改变。");
                try
                {
                    await actions.Click(BoundUi, s => s.Purchase?.BuyButton, s => BasketMatches(s, quantity) && s.Purchase!.QuantityDialog == null && Item(s) != null);
                    async Task<(NpcTradeSnapshot Ui, IReadOnlyList<InventoryItemSnapshot> Bag, ulong Money)> Result() => (await BoundUi(), await Bag(), await Money());
                    var verified = await actions.Wait(Result, s => BasketMatches(s.Ui, 0) &&
                        ArrowRestockPolicy.Count(s.Bag) == count + quantity && s.Money == beforeMoney - cost, confirmationTimeoutMs);
                    Require(before.Where(i => i.TemplateId != arrow.TemplateId).All(i => verified.Bag.Any(a => a.InstanceId == i.InstanceId && a.TemplateId == i.TemplateId && a.Count == i.Count)),
                        "补箭后其他背包物品改变。");
                    context.Logger.Info("arrow_restock.purchase_verified", new Dictionary<string, object?>
                        { ["account"] = context.Config.AccountName, ["before"] = count, ["bought"] = quantity,
                            ["after"] = ArrowRestockPolicy.Count(verified.Bag), ["moneySpent"] = cost, ["batches"] = batches });
                }
                catch (Exception ex) when (ex is not OperationCanceledException) { throw new ArrowPurchaseUnconfirmedException(ex); }
            }
            await actions.Key("Space");
            await actions.Wait(Ui, s => !s.IsOpen && !s.DialogOpen && !s.OtherModalOpen);
            report?.Invoke($"补箭完成，共 {count + quantity} 支");
        }
        finally { await actions.Reset(); }
    }
}
