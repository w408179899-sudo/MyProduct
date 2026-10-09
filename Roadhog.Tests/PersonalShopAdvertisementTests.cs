using Roadhog.Application.Trading;
using Roadhog.Core.Diagnostics;
using Roadhog.Core.Input;
using Roadhog.Core.Model;

internal static class PersonalShopAdvertisementTests
{
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }

    public static async Task FocusAndConfirmationAsync()
    {
        foreach (var discount in Enumerable.Range(4, 6))
        foreach (var scenario in new[] { "change", "same", "missing", "missing_blur", "unconfirmed", "cancel_digits", "clear_unconfirmed", "modal" })
        {
            using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            var input = new RecordingKeyboardInput();
            var api = new FakeGameApi();
            var point = new GameUiPoint(420, 220);
            var blur = new GameUiPoint(320, 140);
            var expected = PersonalShopAdvertisement.Text(discount);
            Check(expected.Length == 36 && expected.All(c => c.ToString() == discount.ToString()),
                "advertisement is exactly 36 copies of the selected discount digit");
            var state = new PersonalShopSnapshot(true, false, scenario == "change", Array.Empty<InventoryUiItem>(), 0,
                Array.Empty<PersonalShopListing>(), null, null)
            {
                AdvertisementText = scenario == "same" ? expected : "old stall text",
                AdvertisementInput = scenario == "missing" ? null : point,
                AdvertisementBlurPoint = scenario == "missing_blur" ? null : blur,
                OtherModalOpen = scenario == "modal"
            };
            var focused = scenario == "same"; var clicks = 0; var blurClicks = 0; var moved = false; var typedDigits = 0;
            api.PersonalShopRead = () => state;
            input.AfterMove = (x, y) => { moved = true; api.InventoryUiCursor = new(api.InventoryUiCursor.X + x, api.InventoryUiCursor.Y + y); };
            input.AfterMouseDown = button =>
            {
                Check(button == RoadhogMouseButton.Left && moved && !state.InventoryOpen,
                    "move to current input and close covering bag before one left click");
                if (api.InventoryUiCursor == point) clicks++;
                else
                {
                    Check(api.InventoryUiCursor == blur && state.AdvertisementText == expected, "blur only by clicking first empty slot after text confirmation");
                    blurClicks++;
                }
            };
            input.AfterMouseUp = button =>
            {
                if (button == RoadhogMouseButton.Left && api.InventoryUiCursor == point && clicks > 0) focused = true;
                if (button == RoadhogMouseButton.Left && api.InventoryUiCursor == blur && blurClicks > 0) focused = false;
            };
            input.AfterPress = key =>
            {
                if (key == "I") { Check(!focused, "release advertisement focus before inventory shortcut"); state = state with { InventoryOpen = !state.InventoryOpen }; return; }
                Check(focused, "typing requires the completed left click");
                if (key == "Back")
                {
                    if (scenario == "clear_unconfirmed") { stop.Cancel(); return; }
                    state = state with { AdvertisementText = state.AdvertisementText[..^1] }; return;
                }
                Check(key == "D" + discount, "type only the selected discount digit, without spaces or percent");
                typedDigits++;
                if (typedDigits == 18 && scenario == "cancel_digits") { stop.Cancel(); stop.Token.ThrowIfCancellationRequested(); }
                state = state with { AdvertisementText = state.AdvertisementText + key[1..] };
                if (typedDigits == 36 && scenario == "unconfirmed") { state = state with { AdvertisementText = "wrong text" }; stop.Cancel(); }
            };
            var snapshots = api.Create(new(), new InMemoryRoadhogLogger(), stop.Token);
            async Task<PersonalShopSnapshot> Read() => (await snapshots.ReadPersonalShopAsync()).Value;
            var actions = new TradingActions(input, snapshots, stop.Token, (_, ct) => { ct.ThrowIfCancellationRequested(); return Task.CompletedTask; });
            try
            {
                if (scenario is "unconfirmed" or "missing" or "missing_blur" or "modal" or "cancel_digits" or "clear_unconfirmed")
                {
                    var item = new InventoryItemSnapshot(1, 11, "item", 1, 0, false);
                    api.InventoryItems = new[] { item };
                    await new ConfiguredPersonalShopSequence(input, (_, ct) => { ct.ThrowIfCancellationRequested(); return Task.CompletedTask; })
                        .RunAsync(snapshots, new[] { new PlannedShopItem(item, 100) }, _ => {}, stop.Token, discount);
                    throw new Exception("invalid or unconfirmed advertisement proceeded to registration");
                }
                else
                {
                    await PersonalShopAdvertisement.EnsureAsync(actions, Read, discount, _ => {});
                    await actions.Key("I");
                    Check(state.InventoryOpen && !focused, "confirmed text and blurred focus allow opening bag");
                }
                Check(state.AdvertisementText == expected, "4 through 9 discount text matches exactly");
            }
            catch (InvalidOperationException) when (scenario is "missing" or "missing_blur" or "modal") { }
            catch (OperationCanceledException) when (scenario is "unconfirmed" or "cancel_digits" or "clear_unconfirmed") { }
            Check(clicks == (scenario is "same" or "missing" or "modal" ? 0 : 1), "only one left click when a change is needed: " + scenario);
            Check(!input.MouseCommands.Contains("down:Right"), "failed or unconfirmed text cannot register an item");
            Check(blurClicks == (scenario is "change" or "same" ? 1 : 0), "matching text also releases existing edit focus");
            Check(!input.KeyDowns.Contains("ShiftKey") && !input.Keys.Contains("Space"), "digit-only advertisement needs no Shift or Space");
            if (scenario is "change" or "same") Check(typedDigits == (scenario == "same" ? 0 : 36), "type exactly 36 digits only when text changes");
            if (scenario is "change" or "unconfirmed" or "cancel_digits" or "missing_blur")
            {
                Check(!input.KeyDowns.Contains("ControlKey") && !input.Keys.Contains("A") && input.Keys.Count(k => k == "Back") == "old stall text".Length,
                    "clear the actual old text using verified Backspace, without Ctrl+A");
            }
        }
    }
}
