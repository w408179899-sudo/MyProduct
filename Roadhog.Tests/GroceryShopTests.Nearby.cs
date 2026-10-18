using Roadhog.Application.BagCleanup;
using Roadhog.Application.SemiAuto;
using Roadhog.Application.StationaryCombat;
using Roadhog.Application.Trading;
using Roadhog.Application.Travel;
using Roadhog.Application.Workers;
using Roadhog.Core.Accounts;
using Roadhog.Core.Common;
using Roadhog.Core.Model;
using Roadhog.Core.Paths;

internal static partial class GroceryShopTests
{
    public static Task NearbyPathPolicyAsync()
    {
        using var trip = new Trip();
        var path = trip.Route;
        path.Points = new() { new() { X = 1000 }, new() { X = 1100 }, new() { X = 1200 } };
        foreach (var (x, z, map, expected) in new (float, float, uint?, int)[]
        {
            (1000, 0, 2, 0), (1109.999f, 0, 2, 1), (1201, 2, 2, 2),
            (1110, 0, 2, -1), (1110.001f, 0, 2, -1), (1101, 2.001f, 2, -1),
            (1101, 0, 1, -1), (1101, 0, null, -1), (float.NaN, 0, 2, -1)
        }) Check(NearbyPathEntry.Find(map, new(x, 0, z), path) == expected, $"nearby boundary {x}/{z}/{map}");
        path.MapId = null;
        Check(NearbyPathEntry.Find(2, new(1100, 0, 0), path) == -1, "legacy mapless path cannot infer current map");
        path.MapId = 2;
        path.Points.Add(new() { X = 1101, Z = 8 });
        Check(NearbyPathEntry.Find(2, new(1101, 0, 0), path) == 1, "nearest wrong-floor point cannot hide a valid point");
        path.Points.Add(new() { X = 1101.5 });
        Check(NearbyPathEntry.Find(2, new(1101, 0, 0), path) == 4, "select nearest eligible point, not first match");
        Check(NearbyPathEntry.Find(new(false, null, null, trip.Now), path) == -1, "loading cannot skip recall");
        var dead = new ChannelTransitionSnapshot(true, trip.Api.Player with { CurrentHp = 0, Position = new(1101, 0, 0) },
            trip.Api.Channel with { MapId = 2 }, trip.Now);
        Check(NearbyPathEntry.Find(dead, path) == -1, "death cannot skip recall");

        var start = dead with { Player = dead.Player! with { CurrentHp = 100 } };
        var plan = new ReturnItemPlan(start);
        plan.ReturnTo(path, "missing");
        var adjacent = new SharedPathDocument { MapId = 2, Points = new() { new() { X = 1102 } } };
        Check(!plan.NeedsReturn(adjacent), "skipped return retains actual position instead of pretending to land at point one");
        var far = new SharedPathDocument { MapId = 3, Points = new() { new() { X = 0 }, new() { X = 50 } } };
        plan.Add(far, "scroll");
        plan.Add(path, "SCROLL");
        Check(plan.Required.Count == 1 && plan.Required["scroll"] == 2, "same item across remote stages needs two, case insensitive");
        try { CleanupPathTravel.RequireItems(plan, new[] { new InventoryItemSnapshot(1, 1, "scroll", 1, 0, false) }); throw new Exception("short multileg inventory accepted"); }
        catch (InvalidOperationException) { }
        return Task.CompletedTask;
    }

    private sealed class ReachedNearbyRoute : Exception { }

    public static async Task NearbySharedWorkflowEntriesAsync()
    {
        foreach (var kind in new[] { "cleanup_item", "cleanup_key", "auction_item", "auction_key", "warehouse", "grocery", "npc_no_goods" })
        {
            using var trip = new Trip();
            var settings = trip.Config.ScriptSettings!;
            trip.Route.Points = new() { new() { X = 1000 }, new() { X = 1100 }, new() { X = 1200 } };
            trip.Route.CleanupNpcName = "merchant";
            trip.Api.Channel = trip.Api.Channel with { MapId = 2 };
            trip.Api.Player = trip.Api.Player with { Position = new(1101, 0, 0) };
            trip.Api.InventoryItems = trip.Api.InventoryItems.Where(i => i.InstanceId != 11).ToArray();
            settings.Paths.MaintenancePathName = settings.Paths.AuctionPathName = settings.Paths.StallPathName = trip.Route.Name;
            settings.Paths.RevivePathName = "revive";
            settings.Paths.BagCleanupReturnByReversePath = true;
            settings.Paths.BagCleanupTownReturnKey = settings.Paths.TownReturnKey = "";
            settings.Paths.BagCleanupReturnItemName = kind == "cleanup_item" ? "absent" : "";
            settings.Paths.AuctionReturnItemName = kind == "auction_item" ? "absent" : "";
            settings.Paths.StallReturnItemName = ""; // No selected item is needed when already at the warehouse route.
            settings.Maintenance.BagCleanupSellItemNameKeywords.Add("goods");
            settings.Maintenance.CleanupWorkflow = new()
            {
                NpcCleanup = kind.StartsWith("cleanup"), Auction = kind.StartsWith("auction"),
                TransferGold = kind == "warehouse", WarehouseName = "warehouse", WarehouseSelectionKey = "F9",
                GroceryPostSaleAction = kind == "npc_no_goods" ? GroceryPostSaleAction.NpcSell : GroceryPostSaleAction.Discard
            };
            if (kind == "npc_no_goods") settings.Maintenance.BagCleanupStallItems.Clear();
            var revive = trip.Route.Clone(); revive.Name = "revive";
            var reached = false;
            var runner = new CleanupWorkflowRunner(trip.Input, new InMemorySharedPathStore(trip.Route, revive), (_, name, points) =>
            {
                Check(name == trip.Route.Name && points.SequenceEqual(new[] { new Vector3Snapshot(1100, 0, 0), new(1200, 0, 0) }),
                    kind + " continues from middle point in original order");
                reached = true;
                throw new ReachedNearbyRoute(); // Stop before trading; this test owns departure and path handoff.
            }, new UnusedJournal(), grocerySuccessStore: trip.Success, groceryDelay: trip.Delay, groceryClock: () => trip.Now);
            var request = new CleanupRequest(settings, true)
            { GroceryTrigger = kind is "grocery" or "npc_no_goods" ? GroceryShopTrigger.Manual : GroceryShopTrigger.None };
            try { await runner.RunAsync(trip.Context, request); }
            catch (ReachedNearbyRoute) { }
            Check(reached && trip.ScrollClicks == 0 && !trip.Input.Keys.Contains("I") &&
                (kind == "npc_no_goods" ? trip.Input.Keys.All(k => k == "Space") : trip.Input.Keys.Count == 0),
                kind + " must reach walking without scroll, return key, or opening the bag");
        }
    }

    public static async Task NearbyLegacyAndFormalExecutorAsync()
    {
        using var trip = new Trip();
        trip.Api.Channel = trip.Api.Channel with { MapId = 2 };
        trip.Api.Player = trip.Api.Player with { Position = new(1010, 0, 0) };
        var settings = trip.Config.ScriptSettings!;
        settings.Paths.MaintenancePathName = settings.Paths.RevivePathName = trip.NpcRoute.Name;
        settings.Paths.BagCleanupReturnItemName = settings.Paths.BagCleanupTownReturnKey = settings.Paths.TownReturnKey = "";
        var store = new InMemorySharedPathStore(trip.NpcRoute);
        var movement = new StationaryCombatController(trip.Input, new SemiAutoCombatController(trip.Input), store);
        var cleanup = new BagCleanupController(trip.Input, store, movement.ExecuteCleanupPathAsync);
        await cleanup.ReturnToTownRequestedAsync(trip.Context, _ => { });
        await cleanup.ReturnToReviveRequestedAsync(trip.Context, _ => { });
        var result = await CleanupPathTravel.FollowAsync(trip.Context, trip.NpcRoute, movement.ExecuteCleanupPathAsync);
        Check(result.Success && trip.Input.Keys.Count == 0 && trip.Input.KeyDowns.Count == 0,
            "legacy returns and formal executor at last point must neither recall nor walk backwards");
        var started = trip.Logger.Entries.Single(e => e.EventName == "manual_path.execute.start");
        Check(Convert.ToInt32(started.Fields!["pathPointCount"]) == 1,
            "production executor receives only the remaining suffix instead of forcibly walking from original point one");

        settings.Maintenance.BagCleanupEnabled = true;
        settings.Maintenance.BagCleanupSellItemNameKeywords.Add("goods");
        trip.Api.InventoryCapacity = trip.Api.InventoryItems.Count;
        var state = new BagCleanupState();
        var begun = await cleanup.TickAfterLootAsync(trip.Context, state);
        Check(begun.Status == BagCleanupTickStatus.Running && state.Active,
            "automatic full-bag cleanup near its path does not require an unused legacy return key");
    }

    public static async Task NearbyReturnExecutionBoundariesAsync()
    {
        foreach (var (x, z, map, expectedClicks) in new (float, float, uint, int)[]
        {
            (1029.999f, 0, 2, 0), (1030, 0, 2, 1), (1031, 0, 2, 1),
            (1020, 2, 2, 0), (1020, 2.001f, 2, 1), (1020, 0, 1, 1)
        })
        {
            using var trip = new Trip();
            trip.Api.Channel = trip.Api.Channel with { MapId = map };
            trip.Api.Player = trip.Api.Player with { Position = new(x, 0, z) };
            if (expectedClicks == 0) trip.Api.InventoryItems = Array.Empty<InventoryItemSnapshot>();
            await new GroceryReturnSequence(trip.Input, trip.Delay, () => trip.Now).RunAsync(trip.Context, trip.Route,
                trip.Config.ScriptSettings!.Paths.GroceryReturnItemName, _ => { });
            Check(trip.ScrollClicks == expectedClicks && !trip.BagOpen &&
                (expectedClicks != 0 || !trip.Input.Keys.Contains("I")),
                $"return execution honors map, strict distance and height: {x}/{z}/{map}");
        }
    }

    public static async Task NearbyMultiLegGroceryAsync()
    {
        foreach (var kind in new[] { "first_near", "second_near", "both_near" })
        {
            using var trip = new Trip();
            var settings = trip.Config.ScriptSettings!;
            settings.Maintenance.CleanupWorkflow.GroceryPostSaleAction = GroceryPostSaleAction.NpcSell;
            settings.Paths.BagCleanupReturnItemName = settings.Paths.GroceryReturnItemName;
            settings.Paths.MaintenancePathName = trip.NpcRoute.Name;
            trip.AddDiscardCandidates();
            trip.Api.InventoryItems = trip.Api.InventoryItems.Select(i => i.InstanceId == 11 ? i with { Count = 1 } : i).ToArray();
            if (kind != "second_near")
            {
                trip.Api.Channel = trip.Api.Channel with { MapId = 2 };
                trip.Api.Player = trip.Api.Player with { Position = new(1021, 0, 0) };
            }
            if (kind != "first_near") trip.NpcRoute.Points[1].X = 1021;
            if (kind == "both_near") trip.Api.InventoryItems = trip.Api.InventoryItems.Where(i => i.InstanceId != 11).ToArray();
            var routeCalls = 0;
            var runner = new CleanupWorkflowRunner(trip.Input, new InMemorySharedPathStore(trip.Route, trip.NpcRoute), (_, name, points) =>
            {
                var path = name == trip.Route.Name ? trip.Route : trip.NpcRoute;
                var expectedStart = name == trip.Route.Name ? (kind == "second_near" ? 0 : 1) : (kind == "first_near" ? 0 : 1);
                Check(points.SequenceEqual(path.Points.Skip(expectedStart).Select(p => p.ToVector3())), "each leg enters correct route suffix: " + kind);
                Check(!trip.BagOpen, "bag closes before walking");
                trip.Api.Player = trip.Api.Player with { Position = points[^1] }; routeCalls++;
                return Task.FromResult(OperationResult.Ok());
            }, new UnusedJournal(), grocerySuccessStore: trip.Success, groceryDelay: trip.Delay, groceryClock: () => trip.Now);
            var request = new CleanupRequest(settings, true) { GroceryTrigger = GroceryShopTrigger.Manual };
            await runner.RunAsync(trip.Context, request);
            Check(trip.ScrollClicks == (kind == "both_near" ? 0 : 1) && routeCalls == 2 && trip.NpcBatches.SequenceEqual(new[] { 1 }) && request.RequestsRestart,
                "preflight counts only necessary scroll legs and both stages complete: " + kind);
        }
    }
}
