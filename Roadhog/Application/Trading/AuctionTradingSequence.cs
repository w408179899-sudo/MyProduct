using Roadhog.Core.Accounts;
using Roadhog.Core.Api;
using Roadhog.Core.Input;
using Roadhog.Core.Model;
using Roadhog.Application.AuctionHouse;
using static Roadhog.Application.Trading.TradingActions;

namespace Roadhog.Application.Trading;

public sealed class AuctionTradingSequence(IKeyboardInput input, IAuctionListingJournal journal,
    Func<int, CancellationToken, Task>? delay = null)
{
    public Task RunAsync(IRoadhogSnapshotReader snapshots, string account, MaintenanceScriptSettings settings,
        Action<string> report, CancellationToken token, string? configuredNpcName = null) =>
        RunCoreAsync(snapshots, account, settings, report, token, configuredNpcName, withdrawAll: true);

    internal Task ContinueRegistrationAsync(IRoadhogSnapshotReader snapshots, string account, MaintenanceScriptSettings settings,
        Action<string> report, CancellationToken token) =>
        RunCoreAsync(snapshots, account, settings, report, token, null, withdrawAll: false);

    private async Task RunCoreAsync(IRoadhogSnapshotReader snapshots, string account, MaintenanceScriptSettings settings,
        Action<string> report, CancellationToken token, string? configuredNpcName, bool withdrawAll)
    {
        var actions = new TradingActions(input, snapshots, token, delay);
        var broker = new AuctionBrokerSelector(input, snapshots, configuredNpcName, token, delay);
        var character = (await snapshots.ReadPlayerAsync().WaitAsync(token)).Value.CharacterName;
        var history = await journal.LoadAsync(account, character, token);
        async Task<AuctionHouseSnapshot> Ui() => (await snapshots.ReadAuctionHouseAsync().WaitAsync(token)).Value;
        async Task<IReadOnlyList<InventoryItemSnapshot>> Bag() => (await snapshots.ReadInventoryAsync().WaitAsync(token)).Value;
        async Task<ulong> Money() => (await snapshots.ReadInventoryMoneyAsync().WaitAsync(token)).Value;
        async Task<InventoryInteractionSnapshot> InventoryUi() => (await snapshots.ReadInventoryInteractionAsync().WaitAsync(token)).Value;
        async Task CloseBag()
        {
            if (!(await InventoryUi()).IsOpen) return;
            await actions.Key("I"); await actions.Wait(InventoryUi, b => !b.IsOpen);
        }
        async Task Tab(string button, int index)
        {
            if ((await Ui()).ActiveTab != index)
                await actions.Click(Ui, s => s.Button(button), s => s.IsOpen && s.Editor == null && !s.OtherModalOpen && s.WithdrawConfirmation == null);
            await actions.Wait(Ui, s => s.IsOpen && s.ActiveTab == index);
        }
        try
        {
            await actions.Reset(); await actions.Alive();
            var ui = await Ui();
            Require(ui.Editor == null && ui.WithdrawConfirmation == null && ui.RegistrationConfirmation == null && !ui.OtherModalOpen, "请先关闭已有交易确认框。");
            if (!withdrawAll) Require(ui.IsOpen && ui.ActiveTab == 1 && ui.ListingsLoaded, "续接上架需要已确认打开的登录页。");
            if (!ui.IsOpen)
            {
                await CloseBag();
                report("选择交易中介");
                await broker.SelectAsync();
                ui = await Ui();
                Require(await broker.IsSelectedAsync(), "未选中交易中介。");
                if (!ui.DialogOpen) await actions.Key("C");
                await actions.Wait(Ui, s => s.DialogOpen && s.TradeButton != null);
                Require(await broker.IsSelectedAsync(), "交易中介已改变。");
                await actions.Click(Ui, s => s.TradeButton, s => s.DialogOpen && !s.IsOpen);
                await actions.Wait(Ui, s => s.IsOpen);
            }
            if (withdrawAll)
            {
                await CloseBag();
                report("拍卖行：撤回全部在售物品");
                await Tab("register_item_btn", 1);
                ui = await actions.Wait(Ui, s => s.ListingsLoaded);
                foreach (var listing in ui.Listings.ToArray())
                {
                    ui = await actions.Wait(Ui, s => s.IsOpen && s.ActiveTab == 1 && s.ListingsLoaded);
                    var current = ui.Listings.SingleOrDefault(l => l.ListingId == listing.ListingId);
                    if (current == null) continue;
                    report("撤回挂售：" + listing.Name);
                    for (int scrolls = 0; current != null && current.Point == null; scrolls++)
                    {
                        Require(scrolls < 30 && ui.ListingsScrollPoint != null, "未找到可见的挂售行。");
                        var beforeScroll = ui.ListingsScrollY;
                        var ordered = ui.Listings.ToList();
                        var firstVisible = ordered.FindIndex(l => l.Point != null);
                        Require(firstVisible >= 0, "拍卖行列表没有可见行。");
                        var direction = ordered.FindIndex(l => l.ListingId == listing.ListingId) < firstVisible ? 1 : -1;
                        await actions.Scroll(ui.ListingsScrollPoint!, direction);
                        ui = await actions.Wait(Ui, s => s.IsOpen && s.ListingsLoaded && s.ActiveTab == 1 &&
                            (s.ListingsScrollY != beforeScroll || s.Listings.All(l => l.ListingId != listing.ListingId)));
                        current = ui.Listings.SingleOrDefault(l => l.ListingId == listing.ListingId);
                    }
                    if (current == null) continue;
                    await actions.Move(current.Point!);
                    ui = await actions.Wait(Ui, s => s.IsOpen && s.ActiveTab == 1 && s.ListingsLoaded &&
                        (s.HoveredListingId == listing.ListingId || s.Listings.All(l => l.ListingId != listing.ListingId)));
                    if (ui.Listings.All(l => l.ListingId != listing.ListingId)) continue;
                    var beforeCount = Count(await Bag(), listing.TemplateId);
                    await actions.Click(Ui, s => s.Listings.SingleOrDefault(l => l.ListingId == listing.ListingId)?.Point,
                        s => s.IsOpen && s.ActiveTab == 1 && s.ListingsLoaded && s.HoveredListingId == listing.ListingId &&
                            s.Listings.Any(l => l.ListingId == listing.ListingId && l.TemplateId == listing.TemplateId && l.Quantity == listing.Quantity && l.TotalPrice == listing.TotalPrice), RoadhogMouseButton.Right);
                    await actions.Wait(Ui, s => s.WithdrawConfirmation?.ListingId == listing.ListingId);
                    await actions.Click(Ui, s => s.WithdrawConfirmation?.ConfirmButton, s => s.WithdrawConfirmation?.ListingId == listing.ListingId);
                    await actions.Wait(Ui, s => s.ListingsLoaded && s.WithdrawConfirmation == null && s.Listings.All(l => l.ListingId != listing.ListingId));
                    await actions.Wait(Bag, b => Count(b, listing.TemplateId) >= checked(beforeCount + listing.Quantity));
                    history.Listings.RemoveAll(r => r.ListingId == listing.ListingId);
                    await journal.SaveAsync(account, character, history, token);
                }
                await actions.Wait(Ui, s => s.IsOpen && s.ActiveTab == 1 && s.ListingsLoaded && s.Listings.Count == 0 && s.WithdrawConfirmation == null);
            }
            report("拍卖行：按配置登录物品");
            var plan = (await Bag()).Where(i => CleanupTradePolicy.Rule(i, settings, true) != null).ToArray();
            var bagPrepared = false;
            foreach (var planned in plan)
            {
                token.ThrowIfCancellationRequested();
                var rule = CleanupTradePolicy.Rule(planned, settings, true)!;
                if (rule.PriceLookupMethod == AuctionPriceLookupMethod.SearchCalculation)
                { report("保留物品：搜索定价算法尚未配置，" + planned.Name); continue; }
                if (rule.PriceLookupMethod == AuctionPriceLookupMethod.Manual && rule.EffectiveUnitPrice == null) continue;
                ui = await actions.Wait(Ui, s => s.IsOpen && s.ActiveTab == 1 && s.ListingsLoaded && s.Editor == null);
                if (ui.Listings.Count >= ui.ListingCapacity) { report("拍卖行已满 15 项，剩余物品留在背包"); break; }
                var item = (await Bag()).SingleOrDefault(i => i.InstanceId == planned.InstanceId && i.TemplateId == planned.TemplateId);
                if (item == null) continue;
                var ids = ui.Listings.Select(l => l.ListingId).ToHashSet();
                report("拍卖行上架：" + item.Name);
                if (!bagPrepared)
                {
                    await actions.BringBagToFront(async () => (await Ui()) is
                        { IsOpen: true, ActiveTab: 1, Editor: null, OtherModalOpen: false, WithdrawConfirmation: null, RegistrationConfirmation: null });
                    bagPrepared = true;
                }
                await actions.RightClickBag(item, async () => (await Ui()) is
                    { IsOpen: true, ActiveTab: 1, Editor: null, OtherModalOpen: false, WithdrawConfirmation: null, RegistrationConfirmation: null });
                ui = await actions.Wait(Ui, s => s.Editor != null);
                var editor = ui.Editor!;
                Require(editor.TemplateId == item.TemplateId && editor.InstanceId == item.InstanceId && editor.MaximumQuantity >= item.Count && editor.UnitPriceMode,
                    "上架物品或计价方式不匹配。");
                var price = AuctionPricePolicy.Resolve(rule, item, editor);
                if (price == null || price == 0 || price < editor.MinimumAllowedPrice)
                {
                    await actions.Click(Ui, s => s.Editor?.CancelButton, s => s.Editor?.InstanceId == item.InstanceId);
                    await actions.Wait(Ui, s => s.Editor == null);
                    report("保留物品：无有效最低价或低于允许价格，" + item.Name); continue;
                }
                bool Same(AuctionHouseSnapshot s) => s.IsOpen && s.ActiveTab == 1 && !s.OtherModalOpen && s.Editor is { } e &&
                    e.InstanceId == item.InstanceId && e.TemplateId == item.TemplateId && e.UnitPriceMode;
                if (editor.Quantity != item.Count) await actions.Number(Ui, s => s.Editor?.QuantityInput, Same, item.Count);
                if (editor.UnitPrice != price.Value)
                    await actions.Number(Ui, s => s.Editor?.PriceInput, Same, price.Value);
                bool Ready(AuctionHouseSnapshot s) => Same(s) && s.Editor!.Quantity == item.Count && s.Editor.UnitPrice == price && s.Editor.UnitPrice >= s.Editor.MinimumAllowedPrice;
                await actions.Wait(Ui, Ready);
                await actions.Click(Ui, s => s.Editor?.ConfirmButton, Ready);
                var total = checked(price.Value * item.Count);
                bool Listed(AuctionHouseSnapshot s) => s.Editor == null && s.RegistrationConfirmation == null && !s.OtherModalOpen &&
                    s.ListingsLoaded && s.Listings.Any(l => !ids.Contains(l.ListingId) && l.TemplateId == item.TemplateId && l.Quantity == item.Count && l.TotalPrice == total);
                ui = await actions.Wait(Ui, s => s.RegistrationConfirmation != null || s.OtherModalOpen || Listed(s));
                Require(!ui.OtherModalOpen, "上架期间出现其他确认框，停止交易。");
                if (ui.RegistrationConfirmation is { } confirmation)
                {
                    bool Confirmable(AuctionHouseSnapshot s) => s.IsOpen && s.ActiveTab == 1 && !s.OtherModalOpen &&
                        s.Editor == null && s.RegistrationConfirmation is { } c && c.InstanceId == item.InstanceId &&
                        c.Quantity == item.Count && c.UnitPrice == price && c.TotalPrice == total && c.UnitPriceMode && c.Fee == confirmation.Fee;
                    Require(Confirmable(ui), "上架手续费确认的物品、数量或价格不匹配。");
                    Require(await Money() >= confirmation.Fee, "金币不足以支付拍卖行手续费。");
                    report($"确认拍卖行上架：{item.Name}，数量 {item.Count}，单价 {price:N0}，手续费 {confirmation.Fee:N0}");
                    await actions.Click(Ui, s => s.RegistrationConfirmation?.ConfirmButton, Confirmable);
                }
                ui = await actions.Wait(Ui, Listed);
                var added = ui.Listings.Single(l => !ids.Contains(l.ListingId) && l.TemplateId == item.TemplateId && l.Quantity == item.Count && l.TotalPrice == total);
                await actions.Wait(Bag, b => b.All(i => i.InstanceId != item.InstanceId));
                history.Listings.RemoveAll(r => r.ListingId == added.ListingId);
                history.Listings.Add(new(added.ListingId, item.TemplateId, price.Value, DateTimeOffset.UtcNow));
                await journal.SaveAsync(account, character, history, token);
            }
            await CloseBag();
            report("拍卖行：计算领取已售金币");
            await Tab("account_btn", 2);
            ui = await actions.Wait(Ui, s => s.IsOpen && s.ActiveTab == 2 && s.SettlementLoaded);
            if (ui.SettlementMoney > 0)
            {
                var proceeds = ui.SettlementMoney; var before = await Money();
                var expected = checked(before + proceeds);
                await actions.Click(Ui, s => s.Button("collect_btn"), s => s.IsOpen && s.ActiveTab == 2 && s.SettlementLoaded && s.SettlementMoney == proceeds);
                await actions.Wait(Money, m => m >= expected);
                await actions.Wait(Ui, s => s.ActiveTab == 2 && s.SettlementLoaded && s.SettlementMoney == 0);
            }
            await actions.Key("Space"); await actions.Wait(Ui, s => !s.IsOpen && s.Editor == null && s.WithdrawConfirmation == null);
            await CloseBag();
        }
        finally { await actions.Reset(); }
    }
    private static ulong Count(IEnumerable<InventoryItemSnapshot> items, uint template) => items.Where(i => !i.IsEquipped && i.TemplateId == template).Aggregate(0UL, (sum, i) => checked(sum + i.Count));
}
