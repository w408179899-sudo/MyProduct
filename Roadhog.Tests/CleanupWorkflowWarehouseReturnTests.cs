using Roadhog.Application.BagCleanup;
using Roadhog.Application.Trading;
using Roadhog.Core.Accounts;
using Roadhog.Core.Common;
using Roadhog.Core.Paths;

internal static partial class GroceryShopTests
{
    public static async Task WarehouseReturnGuardsAsync()
    {
        foreach (var auction in new[] { false, true })
        foreach (var scenario in new[] { "attack", "hp_attack", "death", "cancel", "wrong_landing", "timeout" })
        {
            using var trip = new Trip { Scenario = scenario };
            var settings = trip.Config.ScriptSettings!;
            settings.Maintenance.CleanupWorkflow = new() { NpcCleanup = false, PersonalShop = !auction, Auction = auction };
            settings.Paths.AuctionPathName = trip.Route.Name;
            if (auction) settings.Paths.AuctionReturnItemName = settings.Paths.GroceryReturnItemName;
            settings.Paths.StallPathName = trip.Route.Name;
            settings.Paths.StallReturnItemName = settings.Paths.GroceryReturnItemName;
            settings.Paths.RevivePathName = "revive";
            settings.Paths.TownReturnKey = "F6";
            var revive = new SharedPathDocument { Name = "revive", MapId = 1, Points = new() { new() { X = 0 } } };
            var runner = new CleanupWorkflowRunner(trip.Input, new InMemorySharedPathStore(trip.Route, revive),
                (_, _, _) => { trip.Follows++; return Task.FromResult(OperationResult.Ok()); }, new UnusedJournal(),
                groceryDelay: trip.Delay, groceryClock: () => trip.Now);
            Exception? failure = null;
            try { await runner.RunAsync(trip.Context, new(settings, true)); }
            catch (Exception ex) { failure = ex; }
            Check(scenario switch
            {
                "attack" or "hp_attack" => failure is GroceryTripInterruptedException,
                "death" => failure is CleanupDeathInterruptionException,
                "cancel" or "wrong_landing" => failure is OperationCanceledException,
                "timeout" => failure is GroceryTripDepartureFailedException,
                _ => false
            }, "warehouse return reports interruption: " + scenario + ": " + failure);
            Check(trip.Follows == 0 && trip.ShopStarts == 0 && !trip.Input.Keys.Any(k => k is "C" or "Y" or "F6"),
                "unconfirmed warehouse return cannot walk, purchase, stall or return to combat: " + scenario);
            Check(trip.ScrollClicks == (scenario == "timeout" ? GroceryReturnSequence.MaxScrollClicks : 1),
                "warehouse return retries only confirmed non-departures: " + scenario);
            Check(trip.Input.KeyUps.Contains("ControlKey") && trip.Input.MouseCommands.Last() == "up:Right",
                "warehouse return releases held inputs: " + scenario);
        }

        // An automatic local discard pass must retain the scroll even if it matches the discard list.
        using var local = new Trip();
        var localSettings = local.Config.ScriptSettings!;
        localSettings.Maintenance.CleanupWorkflow = new() { NpcCleanup = false, PersonalShop = true };
        localSettings.Paths.StallReturnItemName = "  " + localSettings.Paths.GroceryReturnItemName + "  ";
        localSettings.Maintenance.BagCleanupDiscardItemNameKeywords.Add(localSettings.Paths.GroceryReturnItemName);
        await local.Runner().RunAsync(local.Context, new(localSettings, false));
        Check(local.Api.InventoryItems.Any(i => i.Name == localSettings.Paths.GroceryReturnItemName) &&
            local.Discards == 0 && local.ScrollClicks == 0 && local.Follows == 0,
            "automatic local cleanup preserves warehouse scroll before deciding whether town cleanup is needed");
    }
}
