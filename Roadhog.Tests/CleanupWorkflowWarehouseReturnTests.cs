using Roadhog.Application.BagCleanup;
using Roadhog.Application.StationaryCombat;
using Roadhog.Application.Trading;
using Roadhog.Application.Workers;
using Roadhog.Core.Accounts;
using Roadhog.Core.Common;
using Roadhog.Core.Paths;

internal static partial class GroceryShopTests
{
    public static async Task ItemTransferAsync()
    {
        foreach (var scenario in new[] { "success", "retry", "no-items", "wrong_landing" })
        {
            using var trip = new Trip(expectedUnitPrice: 1) { Scenario = scenario == "retry" ? "timeout" : scenario, ExpectBagMouseReset = false };
            if (scenario == "retry")
                trip.AfterScrollClick = () => { if (trip.ScrollClicks == 2) trip.Scenario = "success"; };
            var settings = trip.Config.ScriptSettings!;
            settings.Paths.StallPathName = trip.Route.Name;
            settings.Paths.StallReturnItemName = settings.Paths.GroceryReturnItemName;
            settings.Paths.GroceryStallPathName = "unused-grocery-route";
            settings.Maintenance.CleanupWorkflow.StandaloneShopDiscount = 0;
            settings.Maintenance.CleanupWorkflow.TransferGold = true;
            settings.Maintenance.BagCleanupAuctionHouseItems.Add(new() { Name = "auction-reserved", UnitPrice = 12345 });
            settings.Maintenance.ItemTransferItemNameKeywords = scenario == "no-items" ? ["missing"] : ["GOOD", "卷轴", "reserved"];
            trip.AddDiscardCandidates();
            trip.Api.InventoryItems = [.. trip.Api.InventoryItems, new(3, 13, "auction-reserved", 1, 3, false)];
            var context = trip.Context;
            Check(!context.CleanupRequests.Request(settings, false, itemTransfer: true).Success, "automatic requests cannot trigger transfer");
            Check(!context.CleanupRequests.Request(settings, true, standaloneShop: true, itemTransfer: true).Success, "transfer cannot mix request kinds");
            var invalid = settings.Clone();
            invalid.Paths.StallPathName = string.Empty;
            Check(!context.CleanupRequests.Request(invalid, true, itemTransfer: true).Success, "missing route is rejected");
            Check(context.CleanupRequests.Request(settings, true, itemTransfer: true).Success, "manual transfer ignores saved grocery mode and discount");
            var request = context.CleanupRequests.Current!;
            Check(request.ItemTransfer && !request.GroceryShop && !request.RequestsRestart, "sale not yet confirmed and no linked grocery task");
            Check(!context.CleanupRequests.Request(settings, true, itemTransfer: true).Success, "a transfer cannot be submitted twice");
            settings.Maintenance.ItemTransferItemNameKeywords.Clear();
            Exception? failure = null;
            var startingGold = trip.Api.InventoryMoney;
            try { await trip.Runner().RunAsync(context, request); }
            catch (Exception error) { failure = error; }
            if (scenario is "success" or "retry")
            {
                Check(failure is null, "transfer succeeded: " + failure);
                Check(trip.ScrollClicks == (scenario == "retry" ? 2 : 1), "transfer reaches the scroll directly even when retrying");
                Check(trip.Events.IndexOf("land") < trip.Events.IndexOf("route") && trip.Events.IndexOf("route") < trip.Events.IndexOf("shop") &&
                    trip.Follows == 1 && trip.Confirms == 1 && trip.ShopStarts == 1 && trip.Api.InventoryMoney == startingGold + 3,
                    "confirmed arrival and route precede the single transaction for 3 units at 1 coin each");
                Check(request.SoldOutConfirmed && request.RequestsRestart && !trip.ShopState.IsOpen &&
                    trip.Api.InventoryItems.Any(item => item.Name == settings.Paths.StallReturnItemName) &&
                    trip.Api.InventoryItems.Any(item => item.Name == "auction-reserved") && trip.Api.InventoryItems.Any(item => item.Name == "junk0"),
                    "confirmed sellout closes the shop and restarts without selling the scroll, auction stock or unrelated goods");
            }
            else
            {
                Check(scenario == "no-items" ? failure is InvalidOperationException && trip.ScrollClicks == 0 : failure is OperationCanceledException,
                    "empty plan or unconfirmed landing cannot sell: " + failure);
                Check(trip.Follows == 0 && trip.ShopStarts == 0 && !request.RequestsRestart, "failed transfer cannot navigate, sell or restart");
            }
            Check(trip.NpcBatches.Count == 0 && trip.Success.Saves == 0 && trip.Discards == 0,
                "transfer does not execute the configured cleanup stages or record grocery schedule success");
        }
    }

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

    public static async Task ItemTransferResumesAfterAttackAsync()
    {
        foreach (var scenario in new[] { "before_cast", "retry", "late_landing", "loading_returns_to_origin", "wrong_landing", "exhausted", "stop" })
        {
            using var trip = new Trip(expectedUnitPrice: 1) { Scenario = "hp_attack", ExpectBagMouseReset = false };
            var settings = trip.Config.ScriptSettings!;
            settings.Paths.StallPathName = trip.Route.Name;
            settings.Paths.StallReturnItemName = settings.Paths.GroceryReturnItemName;
            settings.Maintenance.ItemTransferItemNameKeywords = ["goods"];
            var context = trip.Context;
            Check(context.CleanupRequests.Request(settings, manual: true, itemTransfer: true).Success, "item transfer starts");
            var request = context.CleanupRequests.Current!;
            var state = new StationaryCombatState();
            var progress = new CleanupLoopProgress();
            var actions = new TransferActions(trip.Runner(), context);
            Task<CleanupRequestTickResult> Tick() => CleanupRequestRunner.RunAsync(context, state, request, progress, actions);
            if (scenario == "before_cast")
            {
                trip.Api.TargetEntityId = 3;
                trip.Api.TargetIsTargetingLocalPlayer = true;
            }
            if (scenario is "late_landing" or "loading_returns_to_origin")
            {
                var press = trip.Input.AfterPress;
                trip.Input.AfterPress = key =>
                {
                    press?.Invoke(key);
                    if (key == "Escape")
                    {
                        trip.Scenario = "success";
                        trip.Now += TimeSpan.FromSeconds(10);
                    }
                };
            }
            Check(await Tick() == CleanupRequestTickResult.Yield && !progress.OperationActive &&
                  ReferenceEquals(context.CleanupRequests.Current, request) && request.Failure is null &&
                  trip.Logger.Entries.Any(e => e.EventName == "item_transfer.return.interrupted"),
                "an attack retains the same item transfer without a failure cooldown: " + scenario);
            Check(trip.ScrollClicks == (scenario == "before_cast" ? 0 : 1) && trip.Follows == 0 && trip.ShopStarts == 0,
                "an interrupted cast cannot start travel or sale: " + scenario);

            actions.Ready = false;
            var clicks = trip.ScrollClicks;
            Check(await Tick() == CleanupRequestTickResult.Yield && actions.Runs == 1 &&
                  ReferenceEquals(context.CleanupRequests.Current, request) && trip.ScrollClicks == clicks,
                "combat preparation prevents scroll retries until it finishes: " + scenario);
            trip.Api.TargetEntityId = 0;
            trip.Api.TargetIsTargetingLocalPlayer = false;
            actions.Ready = true;
            if (scenario == "stop")
            {
                trip.Stop.Cancel();
                try { await Tick(); throw new Exception("stop did not cancel item transfer"); }
                catch (OperationCanceledException) when (trip.Stop.IsCancellationRequested) { }
                Check(trip.ScrollClicks == clicks && actions.Runs == 1 && trip.ShopStarts == 0, "stop never retries the cast");
                continue;
            }
            if (scenario is "retry" or "exhausted")
            {
                trip.Scenario = "timeout";
                if (scenario == "retry")
                    trip.AfterScrollClick = () => { if (trip.ScrollClicks == 2) trip.Scenario = "success"; };
            }
            else if (scenario == "before_cast") trip.Scenario = "success";
            else if (scenario == "loading_returns_to_origin") trip.Scenario = "timeout";
            else if (scenario == "wrong_landing")
            {
                trip.Scenario = "timeout";
                trip.Api.Channel = trip.Api.Channel with { MapId = 9 };
            }
            var result = await Tick();
            if (scenario is "loading_returns_to_origin" or "wrong_landing" or "exhausted")
            {
                Check(result == CleanupRequestTickResult.Yield && actions.Failure != null && context.CleanupRequests.Current is null &&
                      trip.ScrollClicks == (scenario == "exhausted" ? GroceryReturnSequence.MaxScrollClicks : clicks) &&
                      trip.Follows == 0 && trip.ShopStarts == 0, "ambiguous landing and exhausted attempts cannot replay or sell: " + scenario);
                continue;
            }
            Check(result == CleanupRequestTickResult.RestartPending && actions.Failure is null &&
                  context.CleanupRequests.StandaloneShopRestartRequestId == request.RequestId && trip.Follows == 1 &&
                  trip.Confirms == 1 && trip.ShopStarts == 1 && trip.ScrollClicks == (scenario == "retry" ? 2 : 1),
                "confirmed arrival resumes the same request and sells only once: " + scenario);
        }
    }

    private sealed class TransferActions(CleanupWorkflowRunner runner, AccountWorkerContext context) : ICleanupRequestActions
    {
        public bool Ready { get; set; } = true;
        public int Runs { get; private set; }
        public Exception? Failure { get; private set; }
        public Task<bool> PrepareAsync(CleanupRequest request) => Task.FromResult(Ready);
        public Task StopJumpAsync() => Task.CompletedTask;
        public Task RunWorkflowAsync(CleanupRequest request) { Runs++; return runner.RunAsync(context, request); }
        public Task FinishGroceryAfterFailureAsync(CleanupRequest request, Exception error) => throw new InvalidOperationException("Item transfer must not run grocery finalization.");
        public Task ClearChannelSwitchAsync() => Task.CompletedTask;
        public Task AbandonAsync(CleanupRequest request, Exception error)
        {
            Failure = error;
            context.CleanupRequests.Abandon();
            return Task.CompletedTask;
        }
    }
}
