using Roadhog.Core.Api;
using Roadhog.Core.Input;
using Roadhog.Core.Model;
using static Roadhog.Application.Trading.TradingActions;

namespace Roadhog.Application.Trading;

public sealed record PlannedShopItem(InventoryItemSnapshot Item, ulong UnitPrice);

public sealed class ConfiguredPersonalShopSequence(IKeyboardInput input, Func<int, CancellationToken, Task>? delay = null,
    int settlementTimeoutMs = 30000)
{
    public async Task RunAsync(IRoadhogSnapshotReader snapshots, IReadOnlyList<PlannedShopItem> plan, Action<string> report, CancellationToken token,
        int? advertisementDiscount = null)
    {
        var actions = new TradingActions(input, snapshots, token, delay);
        var expectedAdvertisement = advertisementDiscount is { } d ? PersonalShopAdvertisement.Text(d) : string.Empty;
        async Task<PersonalShopSnapshot> Ui() => (await snapshots.ReadPersonalShopAsync().WaitAsync(token)).Value;
        async Task<IReadOnlyList<InventoryItemSnapshot>> Bag() => (await snapshots.ReadInventoryAsync().WaitAsync(token)).Value;
        async Task<ulong> Money() => (await snapshots.ReadInventoryMoneyAsync().WaitAsync(token)).Value;
        async Task StopEmptyStall()
        {
            var current = await Ui();
            Require(current.Editor == null && current.Listings.Count == 0 && !current.OtherModalOpen, "空摊关闭前界面发生变化。");
            if (!current.IsSelling) return;
            try
            {
                await actions.Click(Ui, s => s.StopButton, s => s.IsSelling && s.Listings.Count == 0 && s.Editor == null && !s.OtherModalOpen);
            }
            catch (InvalidOperationException) when (!token.IsCancellationRequested)
            {
                // Automatic sellout can stop the stall between observation and the click.
                // Accept only the already-stopped empty state; never toggle it back on.
                current = await Ui();
                if (current.IsSelling || current.Listings.Count != 0 || current.Editor != null || current.OtherModalOpen) throw;
            }
            await actions.Wait(Ui, s => !s.IsSelling && s.Listings.Count == 0 && s.Editor == null && !s.OtherModalOpen);
        }
        try
        {
            await actions.Reset();
            var batchNumber = 0;
            foreach (var batch in plan.Chunk(10))
            {
                batchNumber++;
                report($"摆摊第 {batchNumber}/{(plan.Count + 9) / 10} 批：准备登记 {batch.Length} 项");
                var ui = await Ui();
                Require(!ui.IsSelling && ui.Editor == null && ui.Listings.Count == 0 && !ui.OtherModalOpen, "已有摊位或登记内容，停止以免混入本次计划。");
                await PersonalShopStance.EnsureNormalAsync(actions, snapshots, report, token);
                if (!ui.IsOpen) { await actions.Key("Y"); await actions.Wait(Ui, s => s.IsOpen); }
                await PersonalShopAdvertisement.EnsureAsync(actions, Ui, advertisementDiscount, report);
                ui = await Ui();
                if (!ui.InventoryOpen) { await actions.Key("I"); await actions.Wait(Ui, s => s.InventoryOpen); }
                var registered = new List<PersonalShopListing>();
                foreach (var entry in batch)
                {
                    Require((await Ui()).AdvertisementText == expectedAdvertisement,
                        "摊位文字与本次折扣不一致，停止登记。");
                    var item = entry.Item; var price = entry.UnitPrice; Require(price > 0, "摆摊单价必须大于零。");
                    var current = (await Bag()).SingleOrDefault(i => i.InstanceId == item.InstanceId && i.TemplateId == item.TemplateId);
                    Require(current != null && current.Count >= item.Count, "计划摆摊物品已变化。");
                    report($"摆摊登记：{item.Name}，{item.Count} 个，单价 {price:N0}");
                    GameUiPoint? Locate(PersonalShopSnapshot s) => s.BagItems.SingleOrDefault(i => i.InstanceId == item.InstanceId && i.TemplateId == item.TemplateId && i.Quantity == current!.Count)?.Point;
                    await new Roadhog.Application.Input.InventoryItemMouseMover(input, snapshots, delay)
                        .MoveAsync(Locate(await Ui()) ?? throw new InvalidOperationException("摆摊物品不在可见背包中。"), token, actions.Alive);
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
                await actions.Click(Ui, s => s.StartButton, s => s.IsOpen && !s.IsSelling && s.Editor == null && !s.InventoryOpen &&
                    s.AdvertisementText == expectedAdvertisement &&
                    s.Listings.Count == registered.Count && registered.All(s.Listings.Contains));
                // A buyer may take the entire batch before the first post-click read.
                // An empty list enters settlement verification; it is not sale success by itself.
                await actions.Wait(Ui, s => s.IsSelling || s.Listings.Count == 0 && s.Editor == null);
                var started = DateTimeOffset.UtcNow; var nextReport = DateTimeOffset.MinValue;
                while (true)
                {
                    await actions.Alive(); ui = await Ui(); var bag = await Bag(); var money = await Money();
                    if (Consumed(bag) && money >= checked(gold + expectedProceeds) && ui.Listings.Count == 0) break;
                    if (ui.Listings.Count == 0 || !ui.IsSelling)
                    {
                        Require(ui.IsOpen && ui.Editor == null && ui.Listings.Count == 0 && !ui.OtherModalOpen, "摆摊中断，未确认本轮全部售罄。");
                        // An empty stall can remain in selling mode. Close that empty stall
                        // before settlement so it cannot occupy the character indefinitely.
                        if (ui.IsSelling)
                        {
                            await StopEmptyStall();
                        }
                        // The game can clear/stop the stall before inventory and proceeds arrive.
                        // Await all sale postconditions, without treating the empty UI as success.
                        report($"第 {batchNumber} 批清单已清空，核对背包扣除和出售收入（最多 {settlementTimeoutMs / 1000} 秒）");
                        var consumed = Consumed(bag);
                        var proceeds = money;
                        try
                        {
                            ui = await actions.Wait(async () =>
                            {
                                var current = await Ui();
                                consumed = Consumed(await Bag());
                                proceeds = await Money();
                                return current;
                            }, s => !s.IsSelling && !s.OtherModalOpen && s.Editor == null && s.Listings.Count == 0 &&
                                consumed && proceeds >= checked(gold + expectedProceeds), settlementTimeoutMs);
                        }
                        catch (TradingActions.ConfirmationTimeoutException ex)
                        {
                            throw new InvalidOperationException($"第 {batchNumber} 批售罄结算超时：背包扣除={consumed}，金币={proceeds}，应至少={checked(gold + expectedProceeds)}；未确认售罄，不重复提交。", ex);
                        }
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
                    await StopEmptyStall();
                }
                if ((await Ui()).IsOpen) { await actions.Key("Y"); await actions.Wait(Ui, s => !s.IsOpen); }
                report($"第 {batchNumber} 批已确认售罄，剩余 {Math.Max(0, plan.Count - batchNumber * 10)} 项待摆摊");
            }
        }
        finally { await actions.Reset(); }
    }
}
