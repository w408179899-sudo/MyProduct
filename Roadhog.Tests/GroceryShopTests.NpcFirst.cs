using Roadhog.Application.Workers;
using Roadhog.Core.Accounts;
using Roadhog.Core.Model;

internal static partial class GroceryShopTests
{
    private static void AddNpcStages(Trip trip)
    {
        NpcMode(trip);
        var settings = trip.Config.ScriptSettings!.Maintenance;
        settings.BagCleanupDiscardItemNameKeywords.Add("discard");
        settings.BagCleanupSellItemNameKeywords.Add("sell");
        trip.Api.InventoryItems = trip.Api.InventoryItems.Concat(new[]
        {
            new InventoryItemSnapshot(100, 100, "discard equipment", 1, 2, false, 1, VendorSellUnitPrice: 900),
            new InventoryItemSnapshot(101, 101, "discard other", 1, 3, false, VendorSellUnitPrice: 10),
            new InventoryItemSnapshot(102, 102, "sell item", 1, 4, false, VendorSellUnitPrice: 1)
        }).ToArray();
    }

    public static async Task NpcFirstOrderAndCompletionAsync()
    {
        using var trip = new Trip(); AddNpcStages(trip);
        var settings = trip.Config.ScriptSettings!;
        // A successful NPC sale must not need the unused stall route or its return item.
        settings.Paths.GroceryStallPathName = "unused missing stall route";
        settings.Maintenance.BagCleanupSellItemNameKeywords.Add("卷轴");
        trip.Api.InventoryItems = trip.Api.InventoryItems.Concat(new[]
        {
            new InventoryItemSnapshot(103, 103, "discard equipment B", 1, 5, false, 2, VendorSellUnitPrice: 300),
            new InventoryItemSnapshot(104, 104, "discard equipment C", 1, 6, false, 3, VendorSellUnitPrice: 200),
            new InventoryItemSnapshot(105, 105, "discard equipment D", 1, 7, false, 7, VendorSellUnitPrice: 100),
            new InventoryItemSnapshot(106, 106, "discard other B", 1, 8, false, VendorSellUnitPrice: 5),
            new InventoryItemSnapshot(107, 107, "sell equipment", 1, 9, false, 1, VendorSellUnitPrice: 2)
        }).Reverse().ToArray();
        var request = new CleanupRequest(settings, true) { GroceryTrigger = GroceryShopTrigger.Manual };
        await trip.Runner().RunAsync(trip.Context, request);
        Check(trip.NpcBatches.SequenceEqual(new[] { 3, 1, 2, 1, 1 }), "discard triples stay within their category; ordinary sell uses single entries");
        Check(trip.NpcSubmittedItems.SelectMany(i => i).SequenceEqual(new ulong[] { 105, 104, 103, 100, 106, 101, 102, 107 }),
            "discard equipment before cheaper other discard goods, then ordinary sales in their normal order");
        Check(trip.Events.Count(e => e == "npc_junk_register") == 1 && trip.Events.Count(e => e == "npc_junk_empty_submit") == 1 &&
            trip.Events.IndexOf("npc_junk_register") < trip.Events.IndexOf("npc_junk_empty_submit") &&
            trip.Events.IndexOf("npc_junk_empty_submit") < trip.Events.IndexOf("npc_submit"),
            "even an empty junk basket is registered and submitted once before configured sales");
        Check(trip.ShopStarts == 0 && trip.Follows == 1 && trip.Discards == 0 && request.RequestsRestart && !request.SoldOutConfirmed,
            "NPC sold-out skips stall and discard, then uses existing restart handoff");
        Check(trip.Api.InventoryItems.Select(i => i.InstanceId).Order().SequenceEqual(new ulong[] { 11, 12 }) && trip.Success.Saves == 0,
            "protect return item even when it matches sell list; unrelated stall goods do not force a trip or fake stall sellout");
    }

    public static async Task NpcFirstSaleRejectedAsync()
    {
        foreach (var rejectedBatch in new[] { 1, 2, 3 })
        {
            using var trip = new Trip(); AddNpcStages(trip); trip.RejectNpcBatch = rejectedBatch;
            var request = new CleanupRequest(trip.Config.ScriptSettings!, true) { GroceryTrigger = GroceryShopTrigger.Manual };
            await trip.Runner().RunAsync(trip.Context, request);
            Check(trip.NpcBatches.Count == rejectedBatch && trip.NpcSubmittedItems.All(i => i.Length == 1), "rejection stops this and all later NPC phases");
            Check(trip.Events.IndexOf("npc_rejected") < trip.Events.IndexOf("route:grocery") && trip.Events.IndexOf("npc_submit") < trip.Events.IndexOf("shop"),
                "confirmed rejection transfers to the configured stall after NPC sale");
            Check(trip.ShopStarts == 1 && trip.Success.Saves == 1 && request.SoldOutConfirmed && request.RequestsRestart && trip.Discards == 0,
                "existing discount stall completes and restarts without selling or discarding again");
            Check(!trip.Api.InventoryItems.Any(i => i.InstanceId == 12) && trip.Api.InventoryItems.Any(i => i.InstanceId == 102),
                "stall uses only its existing list; rejected and later non-stall goods remain");
            Check(trip.Logger.Entries.All(e => e.EventName != "grocery_shop.stage_skipped"), "sale rejection is a normal branch, not an exception");
        }
    }

    public static async Task NpcFirstBackpackAndUnsellableAsync()
    {
        foreach (var scenario in new[] { "recover", "return_disabled", "still_full", "enough_space", "disabled", "unsellable", "late", "no_discard" })
        {
            using var trip = new Trip { AllowPreDiscard = true }; AddNpcStages(trip);
            var settings = trip.Config.ScriptSettings!;
            settings.Maintenance.BagCleanupEnabled = scenario != "disabled";
            settings.Maintenance.BagCleanupReturnWhenFull = scenario != "return_disabled";
            settings.Maintenance.BagCleanupDiscardItemNameKeywords.Add("卷轴");
            trip.Api.InventoryCapacity = scenario == "enough_space" ? 100 : 5;
            if (scenario is "return_disabled" or "still_full" or "no_discard") settings.Maintenance.BagCleanupThreshold = 3;
            if (scenario == "unsellable") trip.Api.InventoryItems = trip.Api.InventoryItems.Select(i => i.InstanceId == 100 ? i with { CanSellToNpc = false } : i).ToArray();
            if (scenario == "no_discard")
            {
                trip.Api.InventoryItems = trip.Api.InventoryItems.Where(i => i.InstanceId is not (100 or 101)).ToArray();
            }
            if (scenario == "late") trip.AfterPreDiscardClose = () =>
            {
                trip.AfterPreDiscardClose = null;
                trip.Api.InventoryItems = trip.Api.InventoryItems.Append(new InventoryItemSnapshot(200, 200, "discard late", 1, 2, false)).ToArray();
            };
            var request = new CleanupRequest(settings, false) { GroceryTrigger = GroceryShopTrigger.Backpack };
            await trip.Runner().RunAsync(trip.Context, request);
            var depart = scenario is "still_full" or "no_discard";
            var discards = scenario is "enough_space" or "disabled" or "no_discard" ? 0 : scenario == "late" ? 3 : 2;
            Check(trip.Discards == discards && !trip.BagOpen, "finish local discard, including late and NPC-ineligible goods: " + scenario);
            Check(trip.ScrollClicks == (depart ? 1 : 0) && trip.Follows == (depart ? 1 : 0) &&
                trip.NpcBatches.Count == (depart ? 1 : 0) && request.RequestsRestart == depart && trip.ShopStarts == 0,
                "only exhausted discard plus insufficient capacity can visit NPC: " + scenario);
            Check(trip.Api.InventoryItems.Any(i => i.InstanceId == 11), "protect selected return scroll before local discard");
            if (scenario == "still_full") Check(trip.Events.LastIndexOf("pre_discard") < trip.Events.IndexOf("scroll") &&
                trip.NpcSubmittedItems.Single().SequenceEqual(new ulong[] { 102 }), "discard completes before return; NPC receives only remaining sale goods");
        }
    }

    public static async Task NpcFirstScheduledUnsellableAsync()
    {
        using var trip = new Trip(); AddNpcStages(trip);
        trip.Config.ScriptSettings!.Maintenance.BagCleanupReturnWhenFull = false;
        trip.Api.InventoryItems = trip.Api.InventoryItems.Select(i => i.InstanceId == 100 ? i with { CanSellToNpc = false } : i).ToArray();
        var request = new CleanupRequest(trip.Config.ScriptSettings!, false) { GroceryTrigger = GroceryShopTrigger.Scheduled };
        await trip.Runner().RunAsync(trip.Context, request);
        Check(trip.Discards == 0 && trip.ShopStarts == 1 && trip.Api.InventoryItems.Any(i => i.InstanceId == 100) && request.SoldOutConfirmed,
            "scheduled cleanup preserves discard goods for NPC, and unsellable leftovers still require the stall branch");
    }

    public static async Task NpcFirstEmptyStallAndFailureAsync()
    {
        foreach (var scenario in new[] { "no_stall", "sale_failure", "bad_stall_route" })
        {
            using var trip = new Trip(); AddNpcStages(trip);
            trip.RejectNpcBatch = scenario == "sale_failure" ? 0 : 1;
            trip.FailNpcSubmit = scenario == "sale_failure";
            var settings = trip.Config.ScriptSettings!;
            if (scenario == "no_stall")
            {
                settings.Maintenance.BagCleanupStallItems.Clear();
                settings.Paths.RevivePathName = trip.NpcRoute.Name;
            }
            if (scenario == "bad_stall_route") settings.Paths.GroceryStallPathName = "missing";
            var request = new CleanupRequest(settings, true) { GroceryTrigger = GroceryShopTrigger.Manual };
            Exception? failure = null;
            try { await trip.Runner().RunAsync(trip.Context, request); } catch (InvalidOperationException ex) { failure = ex; }
            Check((failure != null) == (scenario != "no_stall"), "only confirmed rejection with no stall goods finishes normally");
            Check(!request.RequestsRestart && !request.SoldOutConfirmed && trip.ShopStarts == 0 && trip.Discards == 0 && trip.NpcBatches.Count == 1,
                "unfinished or failed sale cannot restart, discard, or replay NPC submission");
            if (scenario == "no_stall") Check(trip.Follows == 2 &&
                trip.Logger.Entries.Any(e => e.EventName == "grocery_shop.return_to_combat.complete"),
                "no stall candidates must execute the return route before completing");
        }
    }
}
