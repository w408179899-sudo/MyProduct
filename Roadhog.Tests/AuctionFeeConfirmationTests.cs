using Roadhog.Application.Trading;
using Roadhog.Core.Accounts;
using Roadhog.Core.Diagnostics;
using Roadhog.Core.Input;
using Roadhog.Core.Model;

internal static partial class CleanupWorkflowTests
{
    public static async Task AuctionRegistrationFeeAsync()
    {
        foreach (var scenario in new[] { "valid", "wrong_item", "wrong_quantity", "wrong_price", "wrong_mode", "insufficient", "foreign_modal", "unconfirmed", "existing", "stop" })
        {
            var api = new FakeGameApi { InventoryMoney = 100 };
            var input = new RecordingKeyboardInput(); Cursor(api, input);
            var item = new InventoryItemSnapshot(20, 11, "item", 3, 0, false);
            api.InventoryItems = new[] { item };
            var settings = new MaintenanceScriptSettings { BagCleanupAuctionHouseItems = new() { new() { Name = "item", UnitPrice = 20 } } };
            var confirmation = new AuctionRegistrationConfirmation(11, 3, 20, true, 60, 10, new(650, 150), new(700, 150));
            var state = Auction() with { SettlementMoney = 50, RegistrationConfirmation = scenario == "existing" ? confirmation : null };
            bool bagOpen = true, held = false; int editorClicks = 0, feeClicks = 0, collects = 0;
            string typed = "";
            using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(scenario == "unconfirmed" ? 1 : 4));
            api.InventoryInteractionRead = () => new(bagOpen, false, false,
                api.InventoryItems.Select(i => new InventoryUiItem((uint)i.InstanceId, i.TemplateId, i.Count, new(700, 300))).ToArray(),
                api.InventoryUiCursor == new GameUiPoint(700, 300) ? 11u : 0u, 0, null, null, false);
            api.AuctionRead = () => state;
            input.AfterPress = key =>
            {
                if (key == "I") bagOpen = !bagOpen;
                else if (key == "Space") state = state with { IsOpen = false };
                else if (key == "A") typed = "";
                else if (key.StartsWith("D") && state.Editor != null)
                { typed += key[1..]; state = state with { Editor = state.Editor with { UnitPrice = ulong.Parse(typed) } }; }
                else throw new Exception("unexpected key " + key);
            };
            input.AfterMouseDown = _ => held = true;
            input.AfterMouseUp = button =>
            {
                if (!held) return; held = false;
                var point = api.InventoryUiCursor;
                if (button == RoadhogMouseButton.Right)
                {
                    Require(point == new GameUiPoint(700, 300), "right-click exact inventory instance");
                    state = state with { Editor = new(20, 3, 1, 20, new(600, 150))
                    { InstanceId = 11, MaximumQuantity = 3, MinimumAllowedPrice = 1, UnitPriceMode = true, PriceInput = new(500, 150), ConfirmButton = new(550, 150) } };
                }
                else if (point == new GameUiPoint(200, 100)) state = state with { ActiveTab = 1 };
                else if (point == new GameUiPoint(500, 150)) { }
                else if (point == new GameUiPoint(550, 150))
                {
                    editorClicks++;
                    Require(state.Editor is { Quantity: 3, UnitPrice: 20 }, "editor quantity and unit price verified before fee dialog");
                    state = state with { Editor = null, OtherModalOpen = scenario == "foreign_modal",
                        RegistrationConfirmation = confirmation with
                        {
                            InstanceId = scenario == "wrong_item" ? 99u : 11u,
                            Quantity = scenario == "wrong_quantity" ? 1UL : 3UL,
                            UnitPrice = scenario == "wrong_price" ? 21UL : 20UL,
                            UnitPriceMode = scenario != "wrong_mode", Fee = scenario == "insufficient" ? 101UL : 10UL
                        } };
                }
                else if (point == new GameUiPoint(650, 150))
                {
                    feeClicks++;
                    if (scenario == "stop") { stop.Cancel(); return; }
                    if (scenario == "unconfirmed") return;
                    api.InventoryMoney -= 10;
                    api.InventoryItems = Array.Empty<InventoryItemSnapshot>();
                    state = state with { RegistrationConfirmation = null, Listings = new[] { new AuctionListing(100, 20, 3, "item", 60, "7", null) } };
                }
                else if (point == new GameUiPoint(100, 100))
                {
                    Require(feeClicks == 1 && state.RegistrationConfirmation == null && api.InventoryItems.Count == 0, "cannot settle before fee confirmation and verified listing");
                    state = state with { ActiveTab = 2 };
                }
                else if (point == new GameUiPoint(300, 100))
                {
                    collects++; api.InventoryMoney += state.SettlementMoney;
                    state = state with { SettlementMoney = 0 };
                }
                else throw new Exception("unexpected click " + point);
            };
            Exception? error = null;
            var journal = new Journal();
            try { await new AuctionTradingSequence(input, journal, Fast).RunAsync(api.Create(new(), new InMemoryRoadhogLogger(), stop.Token), "account", settings, _ => { }, stop.Token); }
            catch (Exception ex) { error = ex; }
            if (scenario == "valid")
                Require(error == null && editorClicks == 1 && feeClicks == 1 && collects == 1 && api.InventoryMoney == 140 && journal.History.Listings.Single().ListingId == 100,
                    "fee confirmed once; listing recorded; proceeds collected after fee deducted");
            else
            {
                Require(error != null && collects == 0 && journal.History.Listings.Count == 0, "unconfirmed listing cannot be recorded or advance to collection: " + scenario);
                Require(editorClicks == (scenario == "existing" ? 0 : 1) && feeClicks == (scenario is "unconfirmed" or "stop" ? 1 : 0), "no blind confirmation or re-submit: " + scenario);
            }
            Require(!held && input.KeyUps.Contains("ControlKey"), "inputs released on success failure and cancellation");
        }
    }
}
