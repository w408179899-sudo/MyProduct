using Roadhog.Application.Workers;
using Roadhog.Core.Accounts;
using Roadhog.Core.Input;
using Roadhog.Core.Model;

internal static partial class GroceryShopTests
{
    private static void AddAutomaticJunk(Trip trip, params ulong[] ids)
    {
        trip.BulkJunkIds.UnionWith(ids);
        trip.Api.InventoryItems = trip.Api.InventoryItems.Concat(ids.Select((id, index) =>
            new InventoryItemSnapshot((uint)id, id, "automatic junk " + id, 2, 10 + index, false))).ToArray();
    }

    public static async Task JunkFirstOrderAsync()
    {
        using var trip = new Trip(); AddNpcStages(trip);
        AddAutomaticJunk(trip, 200, 201, 202, 203);
        trip.Api.InventoryItems = trip.Api.InventoryItems.Append(
            new InventoryItemSnapshot(105, 105, "discard equipment also selected by junk", 1, 8, false, 1)).ToArray();
        trip.BulkJunkIds.Add(105);
        var request = new CleanupRequest(trip.Config.ScriptSettings!, true) { GroceryTrigger = GroceryShopTrigger.Manual };
        await trip.Runner().RunAsync(trip.Context, request);
        Check(trip.NpcBatches.SequenceEqual(new[] { 5, 1, 1, 1 }), "automatic junk stays one batch even above the manual three-entry limit");
        Check(trip.NpcSubmittedItems[0].SequenceEqual(new ulong[] { 200, 201, 202, 203, 105 }) &&
            trip.NpcSubmittedItems.Skip(1).SelectMany(i => i).SequenceEqual(new ulong[] { 100, 101, 102 }),
            "junk first, then fresh discard equipment, discard other, and ordinary sale; never resell overlapping items");
        Check(trip.Events.Count(e => e == "npc_junk_register") == 1 &&
            trip.Events.IndexOf("npc_junk_register") < trip.Events.IndexOf("npc_submit") && request.RequestsRestart && trip.ShopStarts == 0,
            "junk registration occurs exactly once before any sale and NPC completion skips stall");
    }

    public static async Task JunkFirstRejectedAsync()
    {
        using var trip = new Trip(); AddNpcStages(trip); AddAutomaticJunk(trip, 200);
        trip.RejectNpcBatch = 1;
        var request = new CleanupRequest(trip.Config.ScriptSettings!, true) { GroceryTrigger = GroceryShopTrigger.Manual };
        await trip.Runner().RunAsync(trip.Context, request);
        Check(trip.NpcBatches.Count == 1 && trip.NpcSubmittedItems[0].SequenceEqual(new ulong[] { 200 }),
            "rejected automatic junk prevents every later configured NPC sale");
        Check(trip.ShopStarts == 1 && request.SoldOutConfirmed && request.RequestsRestart && trip.Discards == 0 &&
            trip.Api.InventoryItems.Where(i => i.InstanceId is 100 or 101 or 102 or 200).Count() == 4,
            "first-batch rejection keeps all unsold NPC goods and uses the original stall list");
        Check(trip.Events.IndexOf("npc_rejected") < trip.Events.IndexOf("shop") &&
            trip.Events.Count(e => e == "npc_junk_register") == 1, "never retry junk registration after rejection");
    }

    public static async Task JunkFirstWithoutConfiguredItemsAsync()
    {
        foreach (var hasJunk in new[] { false, true })
        {
            using var trip = new Trip(); NpcMode(trip);
            if (hasJunk) AddAutomaticJunk(trip, 200);
            var request = new CleanupRequest(trip.Config.ScriptSettings!, true) { GroceryTrigger = GroceryShopTrigger.Manual };
            await trip.Runner().RunAsync(trip.Context, request);
            Check(trip.Events.Count(e => e == "npc_junk_register") == 1 && request.RequestsRestart && trip.ShopStarts == 0,
                "no configured candidates must not skip the mandatory initial junk registration");
            Check(trip.NpcBatches.Count == (hasJunk ? 1 : 0) &&
                trip.Events.Count(e => e == "npc_junk_empty_submit") == 0,
                "submit confirmed junk only; an unconfirmed empty basket is cancelled without a sale");
        }
    }

    public static async Task JunkFirstFailureAndProtectionAsync()
    {
        foreach (var scenario in new[] { "whitelist", "return_item", "submit_failure", "stop" })
        {
            using var trip = new Trip(); AddNpcStages(trip);
            var settings = trip.Config.ScriptSettings!;
            if (scenario == "return_item") trip.BulkJunkIds.Add(11);
            else AddAutomaticJunk(trip, 200);
            if (scenario == "whitelist") settings.Maintenance.BagCleanupExcludedItemNames.Add("automatic junk");
            if (scenario == "submit_failure") trip.FailNpcSubmit = true;
            if (scenario == "stop")
            {
                var click = trip.Input.AfterMouseUp;
                trip.Input.AfterMouseUp = button =>
                {
                    click?.Invoke(button);
                    if (button == RoadhogMouseButton.Left && trip.Api.InventoryUiCursor == new GameUiPoint(160, 400)) trip.Stop.Cancel();
                };
            }
            var before = trip.Api.InventoryItems.ToArray();
            var request = new CleanupRequest(settings, true) { GroceryTrigger = GroceryShopTrigger.Manual };
            try { await trip.Runner().RunAsync(trip.Context, request); throw new Exception("unsafe junk batch was accepted: " + scenario); }
            catch (InvalidOperationException) when (scenario != "stop") { }
            catch (OperationCanceledException) when (scenario == "stop" && trip.Stop.IsCancellationRequested) { }
            Check(before.SequenceEqual(trip.Api.InventoryItems) && !request.RequestsRestart && trip.ShopStarts == 0 && trip.Discards == 0,
                "protected or uncertain junk sale cannot advance to manual sales, stall or restart: " + scenario);
            Check(trip.NpcBatches.Count == (scenario == "submit_failure" ? 1 : 0) && trip.Events.Count(e => e == "npc_junk_register") == 1,
                "submit at most once, and never submit protected or cancelled junk");
            if (scenario != "stop") Check(!trip.BagOpen && !trip.Api.NpcTradeRead!().IsOpen, "failure still closes NPC and bag");
        }
    }
}
