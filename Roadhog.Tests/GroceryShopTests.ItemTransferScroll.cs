using Roadhog.Application.BagCleanup;
using Roadhog.Application.Trading;
using Roadhog.Application.Workers;
using Roadhog.Core.Model;

internal static partial class GroceryShopTests
{
    public static async Task ItemTransferScrollHoverAsync()
    {
        foreach (var scenario in new[] { "confirmed", "reentry", "unconfirmed" })
        {
            using var trip = new Trip(expectedUnitPrice: 1) { ExpectBagMouseReset = false };
            var scrollPoint = new GameUiPoint(400, 300);
            trip.Api.InventoryUiCursor = scrollPoint;
            var leftScroll = false;
            var onMove = trip.Input.AfterMove;
            trip.Input.AfterMove = (x, y) =>
            {
                onMove?.Invoke(x, y);
                if (trip.ScrollClicks == 0 && Math.Abs(trip.Api.InventoryUiCursor.X - scrollPoint.X) >= 32)
                    leftScroll = true;
            };
            var readBag = trip.Api.InventoryInteractionRead!;
            trip.Api.InventoryInteractionRead = () =>
            {
                var bag = readBag();
                if (trip.ScrollClicks == 0 && (scenario == "unconfirmed" || scenario == "reentry" && !leftScroll))
                    return bag with { HoveredInstanceId = 0 };
                return bag;
            };

            var context = trip.Context;
            var request = QueueScrollTransfer(trip, context);
            if (scenario == "unconfirmed")
            {
                try
                {
                    await trip.Runner().RunAsync(context, request);
                    throw new Exception("a scroll that was never hovered must not be clicked");
                }
                catch (TradingActions.ClickNotSentException ex)
                {
                    Check(ex.Message.Contains("卷轴悬停未确认"), "the error identifies the step before right-click");
                }
                Check(leftScroll && trip.CursorVisits.Count == 2 && !trip.Input.MouseCommands.Contains("down:Right") && trip.ShopStarts == 0,
                    "an unresolved hover moves away and back once and never submits input");
                continue;
            }

            var scrollVisits = -1;
            trip.AfterScrollClick = () => scrollVisits = trip.CursorVisits.Count;
            await trip.Runner().RunAsync(context, request);
            Check(scrollVisits == (scenario == "confirmed" ? 0 : 2) && trip.ScrollClicks == 1 &&
                trip.Follows == 1 && trip.ShopStarts == 1 && request.SoldOutConfirmed,
                "a correct hover clicks directly, and a stale hover leaves the cell once before one click");
            if (scenario == "reentry")
                Check(leftScroll && trip.CursorVisits[1] == scrollPoint, "hover reentry returns to the same scroll cell");
        }
    }

    public static async Task ItemTransferScrollDepartureAsync()
    {
        foreach (var scenario in new[] { "position", "map", "channel", "character", "loading", "death", "attack" })
        {
            using var trip = new Trip(expectedUnitPrice: 1) { ExpectBagMouseReset = false };
            var bagOpened = false;
            var onPress = trip.Input.AfterPress;
            trip.Input.AfterPress = key =>
            {
                onPress?.Invoke(key);
                if (key != "I" || bagOpened) return;
                bagOpened = true;
                switch (scenario)
                {
                    case "position": trip.Api.Player = trip.Api.Player with { Position = new(8, 0, 0) }; break;
                    case "map": trip.Api.Channel = trip.Api.Channel with { MapId = 9 }; break;
                    case "channel": trip.Api.Channel = trip.Api.Channel with { Index = trip.Api.Channel.Index + 1 }; break;
                    case "character": trip.Api.Player = trip.Api.Player with { CharacterName = "another character" }; break;
                    case "loading": trip.Api.TransitionRead = () => new(false, null, null, trip.Now); break;
                    case "death": trip.Api.Player = trip.Api.Player with { CurrentHp = 0 }; break;
                    case "attack": trip.Api.Player = trip.Api.Player with { CurrentHp = 90 }; break;
                }
            };
            if (scenario == "position")
            {
                // No first departure. The extra metre after the click stays below the threshold
                // from the latest position, so the verified non-departure can retry as before.
                trip.Scenario = "timeout";
                trip.AfterScrollClick = () =>
                {
                    trip.Api.Player = trip.Api.Player with { Position = new(9, 0, 0) };
                    if (trip.ScrollClicks == 2) trip.Scenario = "success";
                };
            }

            var context = trip.Context;
            var request = QueueScrollTransfer(trip, context);
            Exception? error = null;
            try { await trip.Runner().RunAsync(context, request); }
            catch (Exception ex) { error = ex; }

            if (scenario == "position")
            {
                Check(error is null && trip.ScrollClicks == 2 && trip.Follows == 1 && request.SoldOutConfirmed,
                    "same-scene movement before the first click updates the departure baseline: " + error);
                continue;
            }
            Check(error is InvalidOperationException or CleanupDeathInterruptionException or GroceryTripInterruptedException &&
                bagOpened && !trip.Input.MouseCommands.Contains("down:Right") && trip.Follows == 0 && trip.ShopStarts == 0,
                "unsafe scene or combat must stop before the scroll is clicked: " + scenario + ": " + error);
        }
    }

    private static CleanupRequest QueueScrollTransfer(Trip trip, AccountWorkerContext context)
    {
        var settings = trip.Config.ScriptSettings!;
        settings.Paths.StallPathName = trip.Route.Name;
        settings.Paths.StallReturnItemName = settings.Paths.GroceryReturnItemName;
        settings.Maintenance.ItemTransferItemNameKeywords = ["goods"];
        Check(context.CleanupRequests.Request(settings, manual: true, itemTransfer: true).Success, "queue manual item transfer");
        return context.CleanupRequests.Current!;
    }
}
