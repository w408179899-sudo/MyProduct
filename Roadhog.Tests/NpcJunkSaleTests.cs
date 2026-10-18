using Roadhog.Application;
using Roadhog.Application.BagCleanup;
using Roadhog.Application.Workers;
using Roadhog.Core.Accounts;
using Roadhog.Core.Diagnostics;
using Roadhog.Core.Input;
using Roadhog.Core.Model;

internal static class NpcJunkSaleTests
{
    public static async Task FlowAsync()
    {
        foreach (var scenario in new[] { "complete", "empty", "only_junk", "protected", "return_item", "unknown", "quantity", "unavailable", "existing", "wrong_npc", "missing_button", "sale_limit", "partial", "money_only", "stop" })
        {
            void Check(bool condition, string message) { if (!condition) throw new Exception(scenario + ": " + message); }
            var api = new FakeGameApi { TargetName = "merchant", TargetOwnServerObjectId = 42, InventoryMoney = 100 };
            var settings = new ScriptSettings();
            settings.Maintenance.BagCleanupDiscardItemNameKeywords.Add("manual");
            settings.Maintenance.BagCleanupExcludedItemNames.Add("reserved");
            settings.Paths.BagCleanupReturnItemName = "return scroll";
            var manual = Enumerable.Range(0, 5).Select(i => new InventoryItemSnapshot(21, (ulong)(11 + i), "manual", 2, i, false)).ToArray();
            var junk = Enumerable.Range(0, 3).Select(i => new InventoryItemSnapshot(22, (ulong)(21 + i), "auto junk", 3, i + 5, false)).ToArray();
            var reserved = new InventoryItemSnapshot(23, 31, "reserved", 1, 8, false);
            var scroll = new InventoryItemSnapshot(24, 32, "return scroll", 1, 9, false);
            api.InventoryItems = (scenario == "only_junk" ? Array.Empty<InventoryItemSnapshot>() : manual).Concat(junk).Concat(new[] { reserved, scroll }).ToArray();
            if (scenario == "unavailable") api.InventoryItems = api.InventoryItems.Select(i => i.InstanceId == 21 ? i with { CanSellToNpc = false } : i).ToArray();
            var input = new RecordingKeyboardInput();
            var logger = new InMemoryRoadhogLogger();
            var open = false; var held = false; var registrations = 0; var rights = 0;
            var submits = new List<int>(); var events = new List<string>();
            using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(4));
            var ui = new NpcTradeSnapshot(false, new Dictionary<string, GameUiPoint>(), true, 1, 42, false,
                scenario == "existing" ? new[] { new NpcTradeItem(31, 23, 1) } : Array.Empty<NpcTradeItem>(), new(280, 400), false,
                scenario == "missing_button" ? null : new(160, 400));
            InventoryUiItem[] Cells() => api.InventoryItems.Select(i => new InventoryUiItem((uint)i.InstanceId, i.TemplateId, i.Count, new(700 + i.Slot * 20, 140))).ToArray();
            api.InventoryInteractionRead = () => new(open, false, false, Cells(), Cells().FirstOrDefault(i => i.Point == api.InventoryUiCursor)?.InstanceId ?? 0, 0, null, new(500, 380), false);
            api.NpcTradeRead = () => ui with { InventoryOpen = open };
            input.AfterMove = (x, y) =>
            {
                api.InventoryUiCursor = new(api.InventoryUiCursor.X + x, api.InventoryUiCursor.Y + y);
                Check(api.InventoryUiCursor != new GameUiPoint(680, 468), "sale never detours through reset point");
            };
            input.AfterPress = key => { Check(key == "I", "only bag toggles"); open = !open; };
            input.AfterMouseDown = _ => held = true;
            input.AfterMouseUp = button =>
            {
                if (!held) return; held = false;
                var point = api.InventoryUiCursor;
                if (button == RoadhogMouseButton.Right)
                {
                    Check(submits.Count > 0 && (scenario == "empty" || events.Contains("bulk_settled")), "bulk sale finishes before manual registration");
                    rights++; events.Add("manual");
                    var item = Cells().Single(i => i.Point == point);
                    ui = ui with { Basket = ui.Basket.Append(new NpcTradeItem(item.InstanceId, item.TemplateId, item.Quantity)).ToArray() };
                }
                else if (point == new GameUiPoint(160, 400))
                {
                    Check(registrations++ == 0 && rights == 0 && ui.Basket.Count == 0, "register all once at start");
                    events.Add("register_all");
                    var batch = junk.Concat(scenario == "only_junk" ? Array.Empty<InventoryItemSnapshot>() : manual.Take(1));
                    if (scenario == "empty") batch = Array.Empty<InventoryItemSnapshot>();
                    if (scenario == "protected") batch = new[] { reserved };
                    if (scenario == "return_item") batch = new[] { scroll };
                    ui = ui with { Basket = batch.Select(i => new NpcTradeItem(scenario == "unknown" ? (uint)i.InstanceId + 100 : (uint)i.InstanceId,
                        i.TemplateId, scenario == "quantity" ? 99UL : i.Count)).ToArray() };
                    if (scenario == "wrong_npc") api.TargetOwnServerObjectId = 99;
                    if (scenario == "stop") stop.Cancel();
                }
                else if (point == new GameUiPoint(280, 400))
                {
                    Check(registrations == 1, "junk button precedes first sell");
                    submits.Add(ui.Basket.Count);
                    if (ui.Basket.Count == 0) { Check(scenario == "empty", "only empty junk stage sells empty basket"); return; }
                    if (scenario == "partial") api.InventoryItems = api.InventoryItems.Where(i => i.InstanceId != ui.Basket[0].InstanceId).ToArray();
                    else if (scenario is not ("sale_limit" or "money_only"))
                        api.InventoryItems = api.InventoryItems.Where(i => ui.Basket.All(b => b.InstanceId != i.InstanceId)).ToArray();
                    if (scenario is not ("sale_limit" or "partial")) api.InventoryMoney += 50;
                    ui = ui with { Basket = Array.Empty<NpcTradeItem>() };
                    events.Add("bulk_settled");
                }
                else throw new Exception("unexpected click " + point);
            };
            Task Fast(int ms, CancellationToken token) { token.ThrowIfCancellationRequested(); return Task.CompletedTask; }
            var context = new AccountWorkerContext(new() { ScriptSettings = settings }, api, logger, new AccountRuntimeManager(logger), new(), stop.Token);
            var state = new BagCleanupState(); state.Start(0, 0);
            NpcSaleOutcome? outcome = null; Exception? failure = null;
            try
            {
                outcome = await new NpcSaleSequence(input, Fast, confirmationTimeoutMs: 100).RunAsync(context, "merchant", state,
                    bag => GroceryNpcSalePolicy.SelectCandidates(bag, settings.Maintenance), 3, registerAllJunkFirst: true);
            }
            catch (Exception ex) { failure = ex; }
            if (scenario is "complete" or "empty" or "only_junk")
            {
                Check(failure == null && outcome == NpcSaleOutcome.AllConfiguredItemsSold && !open, "successful workflow: " + failure);
                Check(submits.SequenceEqual(scenario == "complete" ? new[] { 4, 3, 1 } : scenario == "empty" ? new[] { 0, 3, 2 } : new[] { 3 }), "bulk batch followed by remaining manual triples");
                Check(rights == (scenario == "complete" ? 4 : scenario == "empty" ? 5 : 0), "bulk/manual overlap is not registered twice");
                Check(state.SellBatchCount == (scenario == "complete" ? 3 : scenario == "empty" ? 2 : 1), "empty sell is not counted as a sale");
                Check(api.InventoryItems.Any(i => i == reserved) && api.InventoryItems.Any(i => i == scroll), "protected inventory preserved");
            }
            else if (scenario == "sale_limit")
                Check(failure == null && outcome == NpcSaleOutcome.NoSale && submits.SequenceEqual(new[] { 4 }) && rights == 0 && state.SellBatchCount == 0, "rejected bulk sale stops before manual registration");
            else
                Check(failure != null && rights == 0 && submits.Count == (scenario is "partial" or "money_only" ? 1 : 0) && state.SellBatchCount == 0, "unsafe bulk stage cannot submit or proceed");
            Check(!held, "release mouse on every exit");
        }
    }
}
