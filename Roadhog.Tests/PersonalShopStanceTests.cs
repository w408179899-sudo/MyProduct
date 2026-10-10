using Roadhog.Application.Trading;
using Roadhog.Core.Common;
using Roadhog.Core.Diagnostics;
using Roadhog.Core.Model;

internal static class PersonalShopStanceTests
{
    private sealed class OpenVerified : Exception;
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }

    public static async Task ConfiguredAsync()
    {
        foreach (var scenario in new[] { "normal", "combat", "delayed", "unchanged", "cancel", "dead", "identity" })
        {
            var api = new FakeGameApi();
            var input = new RecordingKeyboardInput();
            api.Player = api.Player with { StanceFlags = scenario == "normal" ? 1U : 0x21U };
            api.PersonalShopRead = () => new(false, false, false, Array.Empty<InventoryUiItem>(), 0,
                Array.Empty<PersonalShopListing>(), null, null);
            using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(12));
            var snapshots = api.Create(new(), new InMemoryRoadhogLogger(), stop.Token);
            var delays = 0;
            var opened = false;
            input.AfterPress = key =>
            {
                if (key == "X")
                {
                    if (scenario == "combat") api.Player = api.Player with { StanceFlags = 1 };
                    if (scenario == "cancel") stop.Cancel();
                    if (scenario == "dead") api.Player = api.Player with { CurrentHp = 0 };
                    if (scenario == "identity") api.Player = api.Player with { CharacterName = "different" };
                    return;
                }
                Check(key == "Y" && !api.Player.IsCombatStance, "stall only opens after normal stance is confirmed");
                opened = true;
                throw new OpenVerified();
            };
            async Task Delay(int ms, CancellationToken token)
            {
                if (scenario == "delayed" && input.Keys.Contains("X"))
                {
                    Check(!input.Keys.Contains("Y"), "no shop input while stance change is pending");
                    if (++delays == 3) api.Player = api.Player with { StanceFlags = 1 };
                }
                await Task.Delay(10, token);
            }
            var item = new InventoryItemSnapshot(1, 11, "item", 1, 0, false);
            try
            {
                await new ConfiguredPersonalShopSequence(input, Delay).RunAsync(snapshots,
                    new[] { new PlannedShopItem(item, 1) }, _ => { }, stop.Token);
                throw new Exception("expected shop opening or guarded exit");
            }
            catch (OpenVerified) when (scenario is "normal" or "combat" or "delayed") { }
            catch (TradingActions.ConfirmationTimeoutException) when (scenario == "unchanged") { }
            catch (OperationCanceledException) when (scenario == "cancel") { }
            catch (InvalidOperationException) when (scenario is "dead" or "identity") { }
            Check(opened == (scenario is "normal" or "combat" or "delayed"), "failed stance change cannot open a stall");
            Check(input.Keys.Count(k => k == "X") == (scenario == "normal" ? 0 : 1), "X is conditional and never toggled repeatedly");
            Check(input.KeyUps.Contains("ControlKey"), "input is released on success and every failure exit");
        }

        var player = new FakeGameApi().Player;
        Check(!(player with { StanceFlags = 5, MotionMode = 1 }).IsCombatStance, "rest is not combat stance");
        Check((player with { StanceFlags = 0x221 }).IsCombatStance, "combat flag is independent of other posture bits");
    }

    public static async Task TestButtonAsync()
    {
        var simulation = new PersonalShopTests.Simulation(1);
        simulation.Api.Player = simulation.Api.Player with { StanceFlags = 0x21 };
        var original = simulation.Input.AfterPress;
        simulation.Input.AfterPress = key =>
        {
            if (key == "X") simulation.Api.Player = simulation.Api.Player with { StanceFlags = 1 };
            else original!(key);
        };
        var outcome = await simulation.Run();
        Check(outcome.Success && simulation.Starts == 1, "test stall also switches combat stance before registration");
        var keys = simulation.Input.Keys.ToArray();
        Check(keys.Count(k => k == "X") == 1 && Array.IndexOf(keys, "X") < Array.IndexOf(keys, "Y"),
            "X precedes opening the test stall");
    }
}
