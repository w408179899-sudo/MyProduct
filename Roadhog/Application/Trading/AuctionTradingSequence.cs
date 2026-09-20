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
    public async Task RunAsync(IRoadhogSnapshotReader snapshots, string account, MaintenanceScriptSettings settings,
        Action<string> report, CancellationToken token, string? configuredNpcName = null)
    {
        var actions = new TradingActions(input, snapshots, token, delay);
        var broker = new AuctionBrokerSelector(input, snapshots, configuredNpcName, token, delay);
        var character = (await snapshots.ReadPlayerAsync().WaitAsync(token)).Value.CharacterName;
        var history = await journal.LoadAsync(account, character, token);
        var options = settings.CleanupWorkflow;
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
            Require(ui.Editor == null && ui.WithdrawConfirmation == null && !ui.OtherModalOpen, "请先关闭已有交易确认框。");
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
            report("拍卖行：领取已售金币");
            await Tab("account_btn", 2);
            ui = await actions.Wait(Ui, s => s.SettlementLoaded);
            if (ui.SettlementMoney > 0)
            {
                var proceeds = ui.SettlementMoney; var before = await Money();
                var expected = checked(before + proceeds);
                await actions.Click(Ui, s => s.Button("collect_btn"), s => s.IsOpen && s.ActiveTab == 2 && s.SettlementLoaded && s.SettlementMoney == proceeds);
                await actions.Wait(Money, m => m >= expected);
                await actions.Wait(Ui, s => s.ActiveTab == 2 && s.SettlementLoaded && s.SettlementMoney == 0);
            }
            await Tab("register_item_btn", 1);
            ui = await actions.Wait(Ui, s => s.ListingsLoaded);
            if (options.OldListingAction != AuctionOldListingAction.Keep)
            {
                foreach (var listing in ui.Listings.Where(l => history.IsOld(l, DateTimeOffset.UtcNow, options.OldListingHours)).ToArray())
                {
                    var rule = settings.BagCleanupAuctionHouseItems.FirstOrDefault(r => CleanupTradePolicy.Matches(listing.Name, r.Name));
                    if (rule == null || settings.BagCleanupExcludedItemNames.Any(k => CleanupTradePolicy.Matches(listing.Name, k))) continue;
                    if (options.OldListingAction == AuctionOldListingAction.Reprice &&
                        (rule.PriceLookupMethod == AuctionPriceLookupMethod.SearchCalculation || rule.PriceLookupMethod == AuctionPriceLookupMethod.Manual && rule.EffectiveUnitPrice == null))
                    { report("旧挂售保持：未配置可用定价方式，" + listing.Name); continue; }
                    report("取回旧挂售：" + listing.Name);
                    ui = await Ui();
                    for (int scrolls = 0; ui.Listings.Single(l => l.ListingId == listing.ListingId).Point == null; scrolls++)
                    {
                        Require(scrolls < 30 && ui.ListingsScrollPoint != null, "未找到可见的旧挂售行。");
                        var beforeScroll = ui.ListingsScrollY;
                        var ordered = ui.Listings.ToList();
                        var firstVisible = ordered.FindIndex(l => l.Point != null);
                        Require(firstVisible >= 0, "拍卖行列表没有可见行。");
                        var direction = ordered.FindIndex(l => l.ListingId == listing.ListingId) < firstVisible ? 1 : -1;
                        await actions.Scroll(ui.ListingsScrollPoint!, direction);
                        ui = await actions.Wait(Ui, s => s.ListingsLoaded && s.ActiveTab == 1 && s.ListingsScrollY != beforeScroll);
                    }
                    await actions.Move(ui.Listings.Single(l => l.ListingId == listing.ListingId).Point!);
                    await actions.Wait(Ui, s => s.HoveredListingId == listing.ListingId);
                    var beforeCount = Count(await Bag(), listing.TemplateId);
                    await actions.Click(Ui, s => s.Listings.SingleOrDefault(l => l.ListingId == listing.ListingId)?.Point,
                        s => s.IsOpen && s.ActiveTab == 1 && s.ListingsLoaded && s.HoveredListingId == listing.ListingId &&
                            s.Listings.Any(l => l.ListingId == listing.ListingId && l.TemplateId == listing.TemplateId && l.Quantity == listing.Quantity && l.TotalPrice == listing.TotalPrice), RoadhogMouseButton.Right);
                    await actions.Wait(Ui, s => s.WithdrawConfirmation?.ListingId == listing.ListingId);
                    await actions.Click(Ui, s => s.WithdrawConfirmation?.ConfirmButton, s => s.WithdrawConfirmation?.ListingId == listing.ListingId);
                    await actions.Wait(Ui, s => s.ListingsLoaded && s.WithdrawConfirmation == null && s.Listings.All(l => l.ListingId != listing.ListingId));
                    await actions.Wait(Bag, b => Count(b, listing.TemplateId) >= checked(beforeCount + listing.Quantity));
                    history.Listings.RemoveAll(r => r.ListingId == listing.ListingId);
                    if (options.OldListingAction == AuctionOldListingAction.Withdraw) history.WithdrawnTemplates.Add(listing.TemplateId);
                    await journal.SaveAsync(account, character, history, token);
                }
            }
            var plan = (await Bag()).Where(i => CleanupTradePolicy.Rule(i, settings, true) != null).ToArray();
            foreach (var planned in plan)
            {
                token.ThrowIfCancellationRequested();
                if (options.OldListingAction == AuctionOldListingAction.Withdraw && history.WithdrawnTemplates.Contains(planned.TemplateId)) continue;
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
                await actions.RightClickBag(item, async () => (await Ui()) is { IsOpen: true, ActiveTab: 1, Editor: null, OtherModalOpen: false });
                ui = await actions.Wait(Ui, s => s.Editor != null);
                var editor = ui.Editor!;
                Require(editor.TemplateId == item.TemplateId && editor.InstanceId == item.InstanceId && editor.MaximumQuantity >= item.Count && editor.UnitPriceMode,
                    "上架物品或计价方式不匹配。");
                var price = rule.PriceLookupMethod == AuctionPriceLookupMethod.DialogMinimum ? editor.MarketMinimum : checked((ulong?)rule.EffectiveUnitPrice);
                if (price == null || price == 0 || price < editor.MinimumAllowedPrice)
                {
                    await actions.Click(Ui, s => s.Editor?.CancelButton, s => s.Editor?.InstanceId == item.InstanceId);
                    await actions.Wait(Ui, s => s.Editor == null);
                    report("保留物品：无有效最低价或低于允许价格，" + item.Name); continue;
                }
                bool Same(AuctionHouseSnapshot s) => s.IsOpen && s.ActiveTab == 1 && !s.OtherModalOpen && s.Editor is { } e &&
                    e.InstanceId == item.InstanceId && e.TemplateId == item.TemplateId && e.UnitPriceMode;
                if (editor.Quantity != item.Count) await actions.Number(Ui, s => s.Editor?.QuantityInput, Same, item.Count);
                await actions.Number(Ui, s => s.Editor?.PriceInput, Same, price.Value);
                bool Ready(AuctionHouseSnapshot s) => Same(s) && s.Editor!.Quantity == item.Count && s.Editor.UnitPrice == price && s.Editor.UnitPrice >= s.Editor.MinimumAllowedPrice;
                await actions.Wait(Ui, Ready);
                await actions.Click(Ui, s => s.Editor?.ConfirmButton, Ready);
                var total = checked(price.Value * item.Count);
                ui = await actions.Wait(Ui, s => s.Editor == null && s.ListingsLoaded && s.Listings.Any(l => !ids.Contains(l.ListingId) && l.TemplateId == item.TemplateId && l.Quantity == item.Count && l.TotalPrice == total));
                var added = ui.Listings.Single(l => !ids.Contains(l.ListingId) && l.TemplateId == item.TemplateId && l.Quantity == item.Count && l.TotalPrice == total);
                await actions.Wait(Bag, b => b.All(i => i.InstanceId != item.InstanceId));
                history.Listings.RemoveAll(r => r.ListingId == added.ListingId);
                history.Listings.Add(new(added.ListingId, item.TemplateId, price.Value, DateTimeOffset.UtcNow));
                await journal.SaveAsync(account, character, history, token);
            }
            await actions.Key("Space"); await actions.Wait(Ui, s => !s.IsOpen && s.Editor == null && s.WithdrawConfirmation == null);
            await CloseBag();
        }
        finally { await actions.Reset(); }
    }
    private static ulong Count(IEnumerable<InventoryItemSnapshot> items, uint template) => items.Where(i => !i.IsEquipped && i.TemplateId == template).Aggregate(0UL, (sum, i) => checked(sum + i.Count));
}
