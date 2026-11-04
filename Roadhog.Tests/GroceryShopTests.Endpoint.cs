using Roadhog.Application.SemiAuto;
using Roadhog.Application.StationaryCombat;
using Roadhog.Application.Trading;
using Roadhog.Application.Travel;
using Roadhog.Application.Workers;
using Roadhog.Core.Common;
using Roadhog.Core.Model;
using Roadhog.Core.Paths;

internal static partial class GroceryShopTests
{
    public static async Task StallEndpointMovementAsync()
    {
        foreach (var scenario in new[] { "ordinary", "precise", "intermediate", "already_there", "cancel" })
        {
            using var trip = new Trip();
            trip.Api.Channel = trip.Api.Channel with { MapId = 2 };
            trip.Api.Player = trip.Api.Player with
            {
                Position = new(scenario == "intermediate" ? 1012.84f : scenario == "already_there" ? 1019.5f : 1017.84f, 0, 0),
                CameraYawDegrees = 90, ActorYawDegrees = 90
            };
            if (scenario == "intermediate") trip.Route.Points = new() { new() { X = 1015 }, new() { X = 1020 } };
            // Movement holds the right mouse without interacting with an inventory cell.
            trip.Input.AfterMouseDown = null;
            trip.Input.AfterMouseUp = null;
            trip.Input.AfterMove = (_, _) => trip.Api.Player = trip.Api.Player with { CameraYawDegrees = 90, ActorYawDegrees = 90 };
            trip.Input.AfterKeyDown = key =>
            {
                if (key != "W") return;
                if (scenario == "cancel") trip.Stop.Cancel();
                else trip.Api.Player = trip.Api.Player with { Position = new(1019.5f, 0, 0) };
            };
            var context = trip.Context;
            var movement = new StationaryCombatController(trip.Input, new SemiAutoCombatController(trip.Input));
            var result = await CleanupPathTravel.FollowAsync(context, trip.Route, movement.ExecuteCleanupPathAsync,
                finalReachDistance: scenario == "ordinary" ? null : 1);
            var moves = scenario is "precise" or "intermediate" or "cancel";
            Check(trip.Input.KeyDowns.Contains("W") == moves, "2.16m requires movement only for the stricter endpoint: " + scenario);
            Check(result.Success == (scenario != "cancel"), "stop cancels final approach: " + scenario);
            if (moves) Check(trip.Input.KeyUps.Contains("W") && trip.Input.MouseCommands.Contains("up:Right"), "release movement on arrival or stop");
            var reached = trip.Logger.Entries.Where(e => e.EventName == "manual_path.execute.point_reached").ToArray();
            if (scenario == "intermediate") Check(reached.Length == 2 && Convert.ToDouble(reached[0].Fields!["distance"]) > 1 &&
                Convert.ToDouble(reached[1].Fields!["distance"]) <= 1, "ordinary waypoint keeps 5m range while final point requires 1m");
            Check(context.CleanupPathFinalReachDistance == null && context.Config.ScriptSettings!.Combat.PathFollowReachDistance == 5,
                "endpoint precision is scoped to this path call, without changing account settings");
        }
    }

    public static async Task StallEndpointGuardsAsync()
    {
        foreach (var npcFirst in new[] { false, true })
        foreach (var scenario in new[] { "arrived", "post_stop_1_18m", "boundary_3m", "outside", "wrong_map", "wrong_floor", "dead", "cancel", "failed" })
        {
            using var trip = new Trip();
            if (npcFirst)
            {
                NpcMode(trip);
                trip.Config.ScriptSettings!.Maintenance.BagCleanupDiscardItemNameKeywords.Add("unsellable");
                trip.Api.InventoryItems = trip.Api.InventoryItems.Append(new InventoryItemSnapshot(100, 100, "unsellable", 1, 2, false,
                    CanSellToNpc: false)).ToArray();
            }
            var context = trip.Context;
            var runner = new CleanupWorkflowRunner(trip.Input, new InMemorySharedPathStore(trip.Route, trip.NpcRoute), (travel, name, points) =>
            {
                if (name == trip.NpcRoute.Name)
                {
                    Check(travel.CleanupPathFinalReachDistance == null, "merchant path retains normal precision");
                    trip.Api.Player = trip.Api.Player with { Position = points[^1] };
                    return Task.FromResult(OperationResult.Ok());
                }
                Check(travel.CleanupPathFinalReachDistance == 1 && points[^1] == trip.Route.Points[^1].ToVector3(),
                    "both grocery modes approach the configured final point at 1m precision");
                Check(!trip.BagOpen && !trip.Api.NpcTradeRead!().DialogOpen, "NPC and bag close before travelling to stall");
                var distance = scenario switch
                {
                    "post_stop_1_18m" => 1.18f,
                    "boundary_3m" => 3f,
                    "outside" => 3.01f,
                    _ => .5f
                };
                trip.Api.Player = trip.Api.Player with { Position = new(1020 - distance, 0, scenario == "wrong_floor" ? 8 : 0) };
                if (scenario == "wrong_map") trip.Api.Channel = trip.Api.Channel with { MapId = 3 };
                if (scenario == "dead") trip.Api.Player = trip.Api.Player with { CurrentHp = 0 };
                if (scenario == "cancel") trip.Stop.Cancel();
                return Task.FromResult(scenario == "failed" ? OperationResult.Fail("path failed") : OperationResult.Ok());
            }, new UnusedJournal(), grocerySuccessStore: trip.Success, groceryDelay: trip.Delay, groceryClock: () => trip.Now);
            var request = new CleanupRequest(trip.Config.ScriptSettings!, true) { GroceryTrigger = GroceryShopTrigger.Manual };
            Exception? failure = null;
            try { await runner.RunAsync(context, request); }
            catch (Exception ex) when (ex is InvalidOperationException or OperationCanceledException) { failure = ex; }
            var shouldOpen = scenario is "arrived" or "post_stop_1_18m" or "boundary_3m";
            Check((failure == null) == shouldOpen && trip.ShopStarts == (shouldOpen ? 1 : 0),
                "only confirmed endpoint arrival permits opening stall: " + npcFirst + "/" + scenario);
            Check(request.RequestsRestart == shouldOpen && trip.Success.Saves == (shouldOpen ? 1 : 0),
                "unconfirmed arrival cannot report sellout or restart");
            Check(context.CleanupPathFinalReachDistance == null && context.Config.ScriptSettings!.Combat.PathFollowReachDistance == 5,
                "stall arrival policy does not leak to later paths");
        }
    }
}
