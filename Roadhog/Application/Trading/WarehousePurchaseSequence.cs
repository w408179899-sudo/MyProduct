using Roadhog.Core.Accounts;
using Roadhog.Core.Api;
using Roadhog.Core.Input;
using Roadhog.Core.Model;
using static Roadhog.Application.Trading.TradingActions;

namespace Roadhog.Application.Trading;

public sealed class WarehousePurchaseSequence(IKeyboardInput input, Func<int, CancellationToken, Task>? delay = null,
    Func<DateTimeOffset>? clock = null)
{
    public async Task RunAsync(IRoadhogSnapshotReader snapshots, CleanupWorkflowSettings settings, Action<string> report, CancellationToken token)
    {
        var actions = new TradingActions(input, snapshots, token, delay);
        async Task<ShopPurchaseSnapshot> Ui() => (await snapshots.ReadPersonalShopAsync().WaitAsync(token)).Value.Purchase;
        async Task<ulong> Money() => (await snapshots.ReadInventoryMoneyAsync().WaitAsync(token)).Value;
        async Task<IReadOnlyList<InventoryItemSnapshot>> Bag() => (await snapshots.ReadInventoryAsync().WaitAsync(token)).Value;
        DateTimeOffset Now() => clock?.Invoke() ?? DateTimeOffset.UtcNow;
        var started = Now(); var nextInput = DateTimeOffset.MinValue; var nextReport = DateTimeOffset.MinValue;
        try
        {
            await actions.Reset();
            var own = (await snapshots.ReadPersonalShopAsync().WaitAsync(token)).Value;
            Require(!own.IsSelling && own.Editor == null, "自己的摊位尚未关闭。");
            if (own.InventoryOpen)
            {
                await actions.Key("I");
                await actions.Wait(async () => (await snapshots.ReadPersonalShopAsync().WaitAsync(token)).Value, s => !s.InventoryOpen);
            }
            ShopPurchaseSnapshot ui; uint seller;
            while (true)
            {
                token.ThrowIfCancellationRequested(); await actions.Alive();
                var target = (await snapshots.ReadLockedTargetAsync().WaitAsync(token)).Value;
                ui = await Ui(); seller = target.ServerObjectId;
                var selected = target.HasTarget && seller != 0 && target.Name == settings.WarehouseName;
                if (selected && ui.IsOpen && ui.SellerObjectId == seller && ui.Items.Count > 0) break;
                var now = Now();
                if (now >= nextReport)
                {
                    report($"等待仓库号 {settings.WarehouseName} 到场并摆摊（{(int)(now - started).TotalSeconds} 秒）");
                    nextReport = now.AddSeconds(5);
                }
                if (now >= nextInput)
                {
                    if (ui.IsOpen && (!selected || ui.SellerObjectId != seller))
                    { await actions.Key("Space"); await actions.Wait(Ui, s => !s.IsOpen); }
                    if (!selected) await actions.Key(settings.WarehouseSelectionKey);
                    else if (!ui.IsOpen) await actions.Key("C");
                    nextInput = now.AddSeconds(2);
                }
                await actions.Pause(200);
            }
            Require(ui.Basket.Count == 0 && ui.QuantityDialog == null, "仓库摊位已有待购买内容，请先清空。");
            var item = ui.Items[0]; var gold = await Money();
            Require(item.UnitPrice > 0, "仓库摊位第一件物品单价无效。");
            var quantity = CleanupTradePolicy.PurchaseQuantity(gold, item.UnitPrice, item.Quantity, item.Quantity);
            if (quantity == 0) { report("金币不足购买仓库摊位第一件物品，跳过转移"); await Close(); return; }
            var beforeCount = Count(await Bag(), item.TemplateId);
            bool Owner(ShopPurchaseSnapshot s) => s.IsOpen && s.SellerObjectId == seller;
            report("购买仓库摊位第一件物品");
            await actions.Move(item.Point ?? throw new InvalidOperationException("仓库第一件物品不在可点击区域。"));
            await actions.Wait(Ui, s => Owner(s) && s.HoveredInstanceId == item.InstanceId);
            await actions.Click(Ui, s => s.Items.FirstOrDefault()?.Point,
                s => Owner(s) && s.HoveredInstanceId == item.InstanceId && s.Items.FirstOrDefault() == item && s.Basket.Count == 0 && s.QuantityDialog == null,
                RoadhogMouseButton.Right, shift: true);
            if (item.Quantity > 1)
            {
                ui = await actions.Wait(Ui, s => Owner(s) && s.QuantityDialog?.InstanceId == item.InstanceId);
                quantity = CleanupTradePolicy.PurchaseQuantity(await Money(), item.UnitPrice, item.Quantity, ui.QuantityDialog!.Maximum);
                Require(quantity > 0, "当前金币不足购买。");
                bool Modal(ShopPurchaseSnapshot s) => Owner(s) && s.QuantityDialog?.InstanceId == item.InstanceId && s.Items.Any(i => i.InstanceId == item.InstanceId && i.UnitPrice == item.UnitPrice);
                await actions.Number(Ui, s => s.QuantityDialog?.Input, Modal, quantity);
                await actions.Wait(Ui, s => Modal(s) && s.QuantityDialog!.Quantity == quantity);
                await actions.Click(Ui, s => s.QuantityDialog?.Confirm, s => Modal(s) && s.QuantityDialog!.Quantity == quantity);
            }
            bool Basket(ShopPurchaseSnapshot s) => Owner(s) && s.QuantityDialog == null && s.Basket.Count == 1 &&
                s.Basket[0].InstanceId == item.InstanceId && s.Basket[0].TemplateId == item.TemplateId && s.Basket[0].UnitPrice == item.UnitPrice && s.Basket[0].Quantity == quantity;
            await actions.Wait(Ui, Basket);
            var beforeGold = await Money(); var cost = checked(quantity * item.UnitPrice);
            Require(beforeGold >= cost, "金币不足，不提交购买。");
            var targetBefore = (await snapshots.ReadLockedTargetAsync().WaitAsync(token)).Value;
            Require(targetBefore.ServerObjectId == seller && targetBefore.Name == settings.WarehouseName, "购买前仓库身份改变。");
            await actions.Click(Ui, s => s.BuyButton, Basket);
            await actions.Wait(Money, m => m == beforeGold - cost);
            await actions.Wait(Bag, b => Count(b, item.TemplateId) >= checked(beforeCount + quantity));
            await actions.Wait(Ui, s => Owner(s) && s.Basket.Count == 0);
            report($"转移金币完成：{cost:N0}（{quantity} 个）");
            await Close();
        }
        finally { await actions.Reset(); }
        async Task Close() { await actions.Key("Space"); await actions.Wait(Ui, s => !s.IsOpen); }
    }
    private static ulong Count(IEnumerable<InventoryItemSnapshot> items, uint template) => items.Where(i => !i.IsEquipped && i.TemplateId == template).Aggregate(0UL, (sum, i) => checked(sum + i.Count));
}
