using Roadhog.Core.Api;
using Roadhog.Core.Input;
using Roadhog.Core.Model;
using static Roadhog.Application.Trading.TradingActions;

namespace Roadhog.Application.Trading;

public sealed record PlannedShopItem(InventoryItemSnapshot Item, ulong UnitPrice);

public sealed class ConfiguredPersonalShopSequence(IKeyboardInput input, Func<int, CancellationToken, Task>? delay = null)
{
    public async Task RunAsync(IRoadhogSnapshotReader snapshots, IReadOnlyList<PlannedShopItem> plan, Action<string> report, CancellationToken token)
    {
        var actions = new TradingActions(input, snapshots, token, delay);
        async Task<PersonalShopSnapshot> Ui() => (await snapshots.ReadPersonalShopAsync().WaitAsync(token)).Value;
        async Task<IReadOnlyList<InventoryItemSnapshot>> Bag() => (await snapshots.ReadInventoryAsync().WaitAsync(token)).Value;
        async Task<ulong> Money() => (await snapshots.ReadInventoryMoneyAsync().WaitAsync(token)).Value;
        try
        {
            await actions.Reset();
            foreach (var batch in plan.Chunk(10))
            {
                var ui = await Ui();
                Require(!ui.IsSelling && ui.Editor == null && ui.Listings.Count == 0 && !ui.OtherModalOpen, "已有摊位或登记内容，停止以免混入本次计划。");
                if (!ui.IsOpen) { await actions.Key("Y"); await actions.Wait(Ui, s => s.IsOpen); }
                if (!ui.InventoryOpen) { await actions.Key("I"); await actions.Wait(Ui, s => s.InventoryOpen); }
                var registered = new List<PersonalShopListing>();
                foreach (var entry in batch)
                {
                    var item = entry.Item; var price = entry.UnitPrice; Require(price > 0, "摆摊单价必须大于零。");
                    var current = (await Bag()).SingleOrDefault(i => i.InstanceId == item.InstanceId && i.TemplateId == item.TemplateId);
                    Require(current != null && current.Count >= item.Count, "计划摆摊物品已变化。");
                    report($"摆摊登记：{item.Name}，{item.Count} 个，单价 {price:N0}");
                    GameUiPoint? Locate(PersonalShopSnapshot s) => s.BagItems.SingleOrDefault(i => i.InstanceId == item.InstanceId && i.TemplateId == item.TemplateId && i.Quantity == current!.Count)?.Point;
                    await actions.Move(Locate(await Ui()) ?? throw new InvalidOperationException("摆摊物品不在可见背包中。"));
                    await actions.Wait(Ui, s => s.HoveredInstanceId == item.InstanceId);
                    await actions.Click(Ui, Locate, s => s.IsOpen && !s.IsSelling && s.Editor == null && s.InventoryOpen && !s.OtherModalOpen && s.HoveredInstanceId == item.InstanceId, RoadhogMouseButton.Right);
                    bool Editor(PersonalShopSnapshot s) => s.IsOpen && !s.IsSelling && !s.OtherModalOpen && s.Editor?.InstanceId == item.InstanceId;
                    ui = await actions.Wait(Ui, Editor);
                    if (ui.Editor!.Quantity != item.Count) await actions.Number(Ui, s => s.Editor?.QuantityInput, Editor, item.Count);
                    await actions.Number(Ui, s => s.Editor?.PriceInput, Editor, price);
                    bool Ready(PersonalShopSnapshot s) => Editor(s) && s.Editor!.UnitPriceMode && s.Editor.Quantity == item.Count && s.Editor.UnitPrice == price && s.Editor.TotalPrice == checked(price * item.Count);
                    await actions.Wait(Ui, Ready);
                    await actions.Click(Ui, s => s.Editor?.ConfirmButton, Ready);
                    var listing = new PersonalShopListing(checked((uint)item.InstanceId), item.TemplateId, item.Count, price);
                    registered.Add(listing);
                    await actions.Wait(Ui, s => s.Editor == null && s.Listings.Count == registered.Count && registered.All(s.Listings.Contains));
                }
                ui = await Ui();
                if (ui.InventoryOpen) { await actions.Key("I"); await actions.Wait(Ui, s => !s.InventoryOpen); }
                var original = await Bag(); var gold = await Money();
                var expectedProceeds = batch.Aggregate(0UL, (sum, p) => checked(sum + p.Item.Count * p.UnitPrice));
                bool Consumed(IReadOnlyList<InventoryItemSnapshot> bag) => batch.All(p =>
                {
                    var before = original.Single(i => i.InstanceId == p.Item.InstanceId).Count;
                    var now = bag.SingleOrDefault(i => i.InstanceId == p.Item.InstanceId)?.Count ?? 0;
                    return before >= p.Item.Count && now <= before - p.Item.Count;
                });
                await actions.Click(Ui, s => s.StartButton, s => s.IsOpen && !s.IsSelling && s.Editor == null && !s.InventoryOpen && s.Listings.Count == registered.Count && registered.All(s.Listings.Contains));
                await actions.Wait(Ui, s => s.IsSelling);
                var started = DateTimeOffset.UtcNow; var nextReport = DateTimeOffset.MinValue;
                while (true)
                {
                    await actions.Alive(); ui = await Ui(); var bag = await Bag(); var money = await Money();
                    if (Consumed(bag) && money >= checked(gold + expectedProceeds) && ui.Listings.Count == 0) break;
                    if (!ui.IsSelling)
                    {
                        Require(ui.IsOpen && ui.Editor == null && ui.Listings.Count == 0, "摆摊中断，未确认本轮全部售罄。");
                        // The game can clear/stop the stall before inventory and proceeds arrive.
                        // Await all sale postconditions, without treating the empty UI as success.
                        report("摊位清单已清空，核对背包扣除和出售收入");
                        var settled = await actions.Wait(async () =>
                        {
                            var current = await Ui();
                            var consumed = Consumed(await Bag());
                            var proceeds = await Money();
                            return (Ui: current, Consumed: consumed, Money: proceeds);
                        }, s => !s.Ui.OtherModalOpen && s.Ui.Editor == null && s.Ui.Listings.Count == 0 &&
                            s.Consumed && s.Money >= checked(gold + expectedProceeds));
                        ui = settled.Ui;
                        break;
                    }
                    Require(ui.Listings.All(l => registered.Any(p => p.InstanceId == l.InstanceId && p.TemplateId == l.TemplateId && p.UnitPrice == l.UnitPrice && l.Quantity <= p.Quantity)), "摊位清单与计划不一致。");
                    var time = DateTimeOffset.UtcNow;
                    if (time >= nextReport)
                    {
                        report($"等待摆摊售罄：剩余 {ui.Listings.Count} 项，已等待 {(int)(time - started).TotalSeconds} 秒"); nextReport = time.AddSeconds(5);
                    }
                    await actions.Pause(200);
                }
                if (ui.IsSelling)
                {
                    await actions.Click(Ui, s => s.StopButton, s => s.IsSelling && s.Listings.Count == 0);
                    await actions.Wait(Ui, s => !s.IsSelling);
                }
                if ((await Ui()).IsOpen) { await actions.Key("Y"); await actions.Wait(Ui, s => !s.IsOpen); }
            }
        }
        finally { await actions.Reset(); }
    }
}
