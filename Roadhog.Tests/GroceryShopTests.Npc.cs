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

}
