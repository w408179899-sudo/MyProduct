using Roadhog.Application.BagCleanup;
using Roadhog.Application.Input;
using Roadhog.Application.Trading;
using Roadhog.Core.Model;

internal static partial class GroceryShopTests
{
    public static async Task UnknownReturnInterruptionRetryAsync()
    {
        foreach (var scenario in new[] { "retry", "relocated", "missing", "attack", "death", "cancel", "loading", "preparing_loading", "inventory_loading", "moving_loading", "exhausted" })
        {
            using var trip = new Trip { Scenario = "timeout" };
            var escaped = false;
            var postEscapeScenes = 0;
            var scene = trip.Api.TransitionRead;
            trip.Api.TransitionRead = () =>
            {
                if (escaped)
                {
                    postEscapeScenes++;
                    if (scenario == "preparing_loading" && postEscapeScenes == 2 || scenario == "inventory_loading" && postEscapeScenes == 3)
                        trip.Scenario = "slow_loading";
                }
                return scene!();
            };
            var pressed = trip.Input.AfterPress;
            trip.Input.AfterPress = key =>
            {
                pressed?.Invoke(key);
                if (key != "Escape" || escaped) return;
                escaped = true;
                if (scenario == "relocated") trip.Api.InventoryItems = trip.Api.InventoryItems.Select(i => i.InstanceId == 11
                    ? i with { InstanceId = 33, Count = 1, Slot = 3 } : i).ToArray();
                if (scenario == "missing") trip.Api.InventoryItems = trip.Api.InventoryItems.Where(i => i.InstanceId != 11).ToArray();
                if (scenario is "attack" or "death" or "cancel") trip.Scenario = scenario;
                if (scenario == "loading") trip.Scenario = "slow_loading";
            };
            var move = trip.Input.AfterMove;
            trip.Input.AfterMove = (x, y) =>
            {
                move?.Invoke(x, y);
                if (escaped && scenario == "moving_loading") trip.Scenario = "slow_loading";
            };
            trip.AfterScrollClick = () =>
            {
                if (trip.ScrollClicks == 2 && scenario is "retry" or "relocated") trip.Scenario = "success";
            };
            try
            {
                await new GroceryReturnSequence(trip.Input, trip.Delay, () => trip.Now).RunAsync(trip.Context, trip.Route,
                    trip.Config.ScriptSettings!.Paths.GroceryReturnItemName, _ => { });
                Check(scenario is "retry" or "relocated" or "loading" or "preparing_loading" or "inventory_loading" or "moving_loading", "only a confirmed return succeeds: " + scenario);
            }
            catch (GroceryTripDepartureFailedException) when (scenario is "missing" or "exhausted") { }
            catch (GroceryTripInterruptedException) when (scenario == "attack") { }
            catch (CleanupDeathInterruptionException) when (scenario == "death") { }
            catch (OperationCanceledException) when (scenario == "cancel") { }
            var expected = scenario == "exhausted" ? 3 : scenario is "retry" or "relocated" ? 2 : 1;
            Check(trip.ScrollClicks == expected, "bounded retries never overlap loading/combat/death/stop or a missing scroll: " + scenario);
            Check(trip.CursorVisits.Contains(InventoryItemMouseMover.ResetPoint), "scroll clicks approach through fixed reset point");
            Check(trip.Success.Saves == 0 && trip.Follows == 0 && trip.ShopStarts == 0, "return retry alone cannot start a sale or record success");
            Check(trip.Input.MouseCommands.Last() == "up:Right", "all retry exits release input");
        }
    }
}
