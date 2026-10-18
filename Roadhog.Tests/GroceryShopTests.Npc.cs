using Roadhog.Application.Workers;
using Roadhog.Application.BagCleanup;
using Roadhog.Application.Trading;
using Roadhog.Core.Common;
using Roadhog.Core.Model;
using Roadhog.Core.Accounts;

internal static partial class GroceryShopTests
{
    public static async Task AlreadyAtReturnEntranceAsync()
    {
        using var trip = new Trip();
        trip.Api.Channel = trip.Api.Channel with { MapId = trip.Route.MapId!.Value };
        trip.Api.Player = trip.Api.Player with { Position = new Vector3Snapshot(1002, 0, 0) };
        var reports = new List<string>();
        await new GroceryReturnSequence(trip.Input, trip.Delay, () => trip.Now).RunAsync(trip.Context, trip.Route,
            trip.Config.ScriptSettings!.Paths.GroceryReturnItemName, reports.Add);
        Check(trip.ScrollClicks == 0 && reports.Any(r => r.Contains("无需使用")),
            "already at the exact same-map entrance needs no unobservable recall or repeated scroll consumption");
        trip.Api.Channel = trip.Api.Channel with { MapId = 1 };
        await new GroceryReturnSequence(trip.Input, trip.Delay, () => trip.Now).RunAsync(trip.Context, trip.Route,
            trip.Config.ScriptSettings!.Paths.GroceryReturnItemName, reports.Add);
        Check(trip.ScrollClicks == 1, "same coordinates on another map cannot skip the return item");
    }

    public static async Task CleanupItemReturnAsync()
    {
        using var trip = new Trip();
        var settings = trip.Config.ScriptSettings!;
        settings.Paths.BagCleanupReturnItemName = settings.Paths.GroceryReturnItemName;
        settings.Paths.MaintenancePathName = trip.NpcRoute.Name;
        settings.Paths.BagCleanupTownReturnKey = "F6";
        settings.Paths.TownReturnKey = "F7";
        var reads = 0;
        trip.Api.TransitionRead = () =>
        {
            if (trip.ScrollClicks > 0)
            {
                if (++reads == 1) return new(false, null, null, DateTimeOffset.UtcNow);
                trip.Api.Channel = trip.Api.Channel with { MapId = 2 };
                trip.Api.Player = trip.Api.Player with { Position = new Vector3Snapshot(1000, 0, 0) };
            }
            return new(true, trip.Api.Player, trip.Api.Channel, DateTimeOffset.UtcNow);
        };
        await new BagCleanupController(trip.Input, new InMemorySharedPathStore(trip.NpcRoute),
            (_, _, _) => Task.FromResult(OperationResult.Ok())).ReturnToTownRequestedAsync(trip.Context, _ => { });
        Check(trip.ScrollClicks == 1 && trip.Api.Channel.MapId == 2 && !trip.BagOpen &&
            !trip.Input.Keys.Contains("F6") && !trip.Input.Keys.Contains("F7"),
            "cleanup item selection uses verified inventory click and landing instead of configured legacy keys");
    }

    public static async Task NpcPostSaleAsync()
    {
        foreach (var scenario in new[] { "sold_out", "no_goods", "arrival_no_goods", "pre_discard", "missing_map", "short_scroll", "failed_submit" })
        {
            using var trip = new Trip();
            var settings = trip.Config.ScriptSettings!;
            settings.Maintenance.CleanupWorkflow.GroceryPostSaleAction = GroceryPostSaleAction.NpcSell;
            settings.Paths.BagCleanupReturnItemName = settings.Paths.GroceryReturnItemName;
            settings.Paths.MaintenancePathName = trip.NpcRoute.Name;
            trip.AddDiscardCandidates(4);
            if (scenario == "sold_out") trip.Api.InventoryItems = trip.Api.InventoryItems.Append(
                new InventoryItemSnapshot(999, 999, "junk unavailable", 1, 20, false, 11, CanSellToNpc: false)).ToArray();
            settings.Maintenance.BagCleanupDiscardItemNameKeywords.Add("卷轴");
            settings.Maintenance.BagCleanupStallItems.Add(new() { Name = "卷轴", UnitPrice = 100 });
            trip.Api.InventoryItems = trip.Api.InventoryItems.Select(i => i.InstanceId == 11 ? i with { VendorSellUnitPrice = 20000 } : i).ToArray();
            if (scenario == "no_goods")
            {
                settings.Maintenance.BagCleanupStallItems.Clear();
                settings.Paths.GroceryStallPathName = "missing-unused-route";
            }
            if (scenario == "arrival_no_goods") trip.AfterScrollClick = () => trip.Api.InventoryItems = trip.Api.InventoryItems.Where(i => i.Name != "goods").ToArray();
            if (scenario == "missing_map") trip.NpcRoute.MapId = null;
            if (scenario == "short_scroll") trip.Api.InventoryItems = trip.Api.InventoryItems.Select(i => i.InstanceId == 11 ? i with { Count = 1 } : i).ToArray();
            if (scenario == "failed_submit") trip.FailNpcSubmit = true;
            if (scenario == "pre_discard")
            {
                trip.Stop.CancelAfter(TimeSpan.FromSeconds(35)); // Existing discard controller uses real input settling delays.
                trip.AllowPreDiscard = true; settings.Maintenance.BagCleanupEnabled = true; trip.Api.InventoryCapacity = trip.Api.InventoryItems.Count;
            }
            var context = trip.Context;
            context.CleanupRequests.Request(settings, true, groceryTrigger: scenario == "pre_discard" ? GroceryShopTrigger.Backpack : GroceryShopTrigger.Manual);
            var request = context.CleanupRequests.Current!;
            try
            {
                await trip.Runner().RunAsync(context, request);
                Check(scenario is not ("missing_map" or "short_scroll"), "invalid departure must fail: " + scenario);
            }
            catch (InvalidOperationException) when (scenario is "missing_map" or "short_scroll") { }
            catch (Exception ex) { throw new Exception("NPC flow scenario " + scenario + ": " + ex, ex); }
            if (scenario is "missing_map" or "short_scroll")
                Check(trip.ScrollClicks == 0 && trip.ShopStarts == 0 && trip.NpcBatches.Count == 0 && !request.RequestsRestart,
                    "validate the whole NPC trip before starting a stall or using its first scroll");
            else if (scenario == "failed_submit")
                Check(trip.NpcBatches.SequenceEqual(new[] { 3 }) && trip.Discards == 0 && request.RequestsRestart && trip.Success.Saves == 1 &&
                    trip.Input.Keys.Contains("Space") && !trip.BagOpen && request.Failure == null &&
                    trip.Api.InventoryItems.Count(i => i.Name.StartsWith("junk")) == 4,
                    "failed submit retains items, closes windows and permits restart without repeating sale or discarding");
            else if (scenario == "pre_discard")
                Check(trip.Discards == 4 && trip.ScrollClicks == 0 && trip.NpcBatches.Count == 0 && !request.RequestsRestart,
                    "backpack local discard still recovers capacity before NPC mode departure");
            else
            {
                Check(trip.NpcBatches.SequenceEqual(new[] { 3, 1 }) && trip.Discards == 0 && request.RequestsRestart && !trip.BagOpen,
                    "NPC mode sells three plus remainder, never discards, and completes after verified sale: " + scenario);
                Check(trip.Api.InventoryItems.Any(i => i.InstanceId == 11) && trip.Api.InventoryItems.Where(i => i.Name.StartsWith("junk")).All(i => !i.CanSellToNpc),
                    "selected scroll is protected from both stall and NPC candidates");
                if (scenario == "sold_out") Check(trip.Api.InventoryItems.Any(i => i.InstanceId == 999), "NPC-ineligible candidate remains in the bag");
                Check(trip.Success.Saves == (scenario == "sold_out" ? 1 : 0) && request.SoldOutConfirmed == (scenario == "sold_out"),
                    "NPC-only completion never invents a stall sellout timestamp");
                Check(trip.ScrollClicks == (scenario == "no_goods" ? 1 : 2) && trip.ShopStarts == (scenario == "sold_out" ? 1 : 0),
                    "no stall candidates still takes the NPC leg");
            }
        }
    }
}
