using Roadhog.Application.Trading;
using Roadhog.Core.Accounts;
using Roadhog.Core.Diagnostics;
using Roadhog.Core.Input;
using Roadhog.Core.Model;

internal static partial class CleanupWorkflowTests
{
    public static async Task AuctionSubmissionAsync()
    {
        foreach (var scenario in new[] { "manual", "minimum", "no_quote", "discount_floor", "discount_above", "discount_no_quote", "full", "search", "wrong_editor", "withdraw_all", "legacy_withdraw", "unknown_age", "unconfigured", "empty", "no_proceeds", "continue_registration" })
        {
            var api = new FakeGameApi { InventoryMoney = 100 };
            var input = new RecordingKeyboardInput();
            Cursor(api, input);
            using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(4));
            var state = Auction() with { SettlementMoney = scenario == "no_proceeds" ? 0UL : 50UL };
            var journal = new Journal();
            var item = new InventoryItemSnapshot(20, 11, "item", 3, 0, false, VendorSellUnitPrice: 400);
            api.InventoryItems = scenario is "unconfigured" or "empty" ? Array.Empty<InventoryItemSnapshot>() : new[] { item };
            if (scenario == "full") api.InventoryItems = Enumerable.Range(0, 16).Select(i => item with { InstanceId = (ulong)(11 + i), Slot = i }).ToArray();
            var settings = new MaintenanceScriptSettings
            {
                BagCleanupExcludedItemNames = new() { "item", "unconfigured" },
                BagCleanupAuctionHouseItems = new() { new() { Name = "item", UnitPrice = 17,
                    PriceLookupMethod = scenario == "search" ? AuctionPriceLookupMethod.SearchCalculation : scenario is "minimum" or "no_quote" ? AuctionPriceLookupMethod.DialogMinimum : AuctionPriceLookupMethod.Manual } }
            };
            var allOrders = scenario is "withdraw_all" or "legacy_withdraw" or "unknown_age";
            if (scenario.StartsWith("discount_"))
            {
                settings.BagCleanupAuctionHouseItems[0].PriceLookupMethod = AuctionPriceLookupMethod.DialogMinimum;
                settings.BagCleanupAuctionHouseItems[0].AuctionDiscount = 9.5m;
            }
            if (allOrders)
            {
                state = state with { Listings = new[]
                {
                    new AuctionListing(8, 20, 3, "item", 90, "7", new(400, 100)),
                    new AuctionListing(9, 21, 1, "manual-item", 30, "7", new(450, 100)),
                    new AuctionListing(10, 22, 1, "unconfigured", 30, "7", new(500, 100))
                } };
                if (scenario != "unknown_age")
                {
                    journal.History.Listings.Add(new(8, 20, 30, DateTimeOffset.UtcNow.AddDays(-2)));
                    journal.History.Listings.Add(new(9, 21, 30, DateTimeOffset.UtcNow));
                }
            }
            if (scenario == "unconfigured") state = state with { Listings = new[] { new AuctionListing(10, 22, 1, "unconfigured", 30, "7", new(400, 100)) } };
            if (scenario == "legacy_withdraw")
            {
                settings.CleanupWorkflow.OldListingAction = AuctionOldListingAction.Withdraw;
                settings.CleanupWorkflow.OldListingHours = 168;
                journal.History.WithdrawnTemplates.Add(20);
            }
            var originalIds = state.Listings.Select(l => l.ListingId).ToArray();
            if (scenario == "continue_registration")
                state = state with { ActiveTab = 1, Listings = new[] { new AuctionListing(80, 20, 3, "already-listed", 51, "7", new(400, 100)) } };
            var expectedSubmits = scenario == "full" ? 15 : allOrders ? 2 : scenario is "manual" or "minimum" or "discount_floor" or "discount_above" or "no_proceeds" or "continue_registration" ? 1 : 0;
            var events = new List<string>();
            var bagOpen = true;
            var bagInFront = false;
            var down = false;
            var typed = "";
            var submits = 0;
            var withdrawals = 0;
            var collects = 0;
            InventoryUiItem[] BagItems() => api.InventoryItems.Select(i => new InventoryUiItem((uint)i.InstanceId, i.TemplateId, i.Count, new(700 + i.Slot * 20, 300))).ToArray();
            api.InventoryInteractionRead = () => new(bagOpen, false, false, BagItems(), BagItems().FirstOrDefault(i => i.Point == api.InventoryUiCursor)?.InstanceId ?? 0, 0, null, null, false);
            api.AuctionRead = () =>
            {
                // Rows compact after each withdrawal; subsequent input must locate the current ID again.
                state = state with { Listings = state.Listings.Select((l, i) => l with { Point = new(400 + i * 50, 100) }).ToArray() };
                return state with { HoveredListingId = state.Listings.FirstOrDefault(l => l.Point == api.InventoryUiCursor)?.ListingId ?? 0 };
            };
            input.AfterPress = key =>
            {
                if (key == "Space") state = state with { IsOpen = false };
                else if (key == "I") { bagOpen = !bagOpen; bagInFront = bagOpen; }
                else if (key == "A") typed = "";
                else if (key.StartsWith("D") && state.Editor != null)
                { typed += key[1..]; state = state with { Editor = state.Editor with { UnitPrice = ulong.Parse(typed) } }; }
                else throw new Exception("unexpected key " + key);
            };
            input.AfterMouseDown = _ => down = true;
            input.AfterMouseUp = button =>
            {
                if (!down) return;
                down = false;
                var p = api.InventoryUiCursor;
                if (button == RoadhogMouseButton.Right)
                {
                    Require(state.ActiveTab == 1 && collects == 0, "withdraw and register on listing tab before collecting");
                    var listing = state.Listings.SingleOrDefault(l => l.Point == p);
                    if (listing != null)
                    {
                        Require(submits == 0, "all withdrawals precede any registration");
                        state = state with { WithdrawConfirmation = new(listing.ListingId, new(410, 150)) };
                        return;
                    }
                    Require(withdrawals == originalIds.Length && state.Listings.All(l => !originalIds.Contains(l.ListingId)), "every original listing withdrawn before opening a registration editor");
                    Require(bagOpen && bagInFront, "inventory prepared before the registration stage stays in front");
                    Require(input.Keys.Count(k => k == "I") == 2, "only one close/open preparation before all registrations");
                    var target = BagItems().Single(i => i.Point == p);
                    state = state with { Editor = new(scenario == "wrong_editor" ? 99u : target.TemplateId, target.Quantity,
                        scenario.StartsWith("discount_") ? 20UL : 1UL,
                        scenario is "no_quote" or "discount_no_quote" ? null : scenario == "discount_floor" ? 20UL : 23UL, new(600, 150))
                    { InstanceId = target.InstanceId, MaximumQuantity = target.Quantity, MinimumAllowedPrice = 2, UnitPriceMode = true, PriceInput = new(500, 150), ConfirmButton = new(550, 150) } };
                    return;
                }
                if (p == new GameUiPoint(200, 100)) state = state with { ActiveTab = 1 };
                else if (p == new GameUiPoint(100, 100))
                {
                    Require(withdrawals == originalIds.Length && submits == expectedSubmits && !bagOpen, "calculation tab is last, after registrations and bag close");
                    events.Add("calculation-tab");
                    state = state with { ActiveTab = 2 };
                }
                else if (p == new GameUiPoint(300, 100))
                {
                    Require(state.ActiveTab == 2 && state.SettlementMoney > 0, "collect only confirmed proceeds");
                    collects++; events.Add("collect"); api.InventoryMoney += state.SettlementMoney;
                    state = state with { SettlementMoney = 0 };
                }
                else if (p == new GameUiPoint(410, 150))
                {
                    var row = state.Listings.Single(l => l.ListingId == state.WithdrawConfirmation!.ListingId);
                    withdrawals++; events.Add("withdraw");
                    var same = api.InventoryItems.FirstOrDefault(i => i.TemplateId == row.TemplateId);
                    api.InventoryItems = same != null
                        ? api.InventoryItems.Select(i => i.InstanceId == same.InstanceId ? i with { Count = checked(i.Count + (uint)row.Quantity) } : i).ToArray()
                        : api.InventoryItems.Append(new(row.TemplateId, row.ListingId + 1000, row.Name, (uint)row.Quantity, api.InventoryItems.Count, false)).ToArray();
                    state = state with { WithdrawConfirmation = null, Listings = state.Listings.Where(l => l.ListingId != row.ListingId).ToArray() };
                }
                else if (p == new GameUiPoint(500, 150)) { }
                else if (p == new GameUiPoint(600, 150)) state = state with { Editor = null };
                else if (p == new GameUiPoint(550, 150))
                {
                    var editor = state.Editor!;
                    var price = scenario switch { "minimum" => 23UL, "discount_floor" => 20UL, "discount_above" => 21UL, _ => 17UL };
                    Require(collects == 0 && editor.UnitPrice == price, "configured pricing before settlement");
                    var target = api.InventoryItems.Single(i => i.InstanceId == editor.InstanceId);
                    Require(editor.Quantity == target.Count, "register the fresh bag quantity including merged withdrawals");
                    submits++; events.Add("register");
                    state = state with { Editor = null, Listings = state.Listings.Append(new((uint)(100 + submits), target.TemplateId, target.Count, target.Name, price * target.Count, "7", null)).ToArray() };
                    api.InventoryItems = api.InventoryItems.Where(i => i.InstanceId != target.InstanceId).ToArray();
                }
                else throw new Exception("unexpected click " + p);
            };
            try
            {
                var sequence = new AuctionTradingSequence(input, journal, Fast);
                var snapshots = api.Create(new(), new InMemoryRoadhogLogger(), stop.Token);
                if (scenario == "continue_registration") await sequence.ContinueRegistrationAsync(snapshots, "account", settings, _ => { }, stop.Token);
                else await sequence.RunAsync(snapshots, "account", settings, _ => { }, stop.Token);
                Require(scenario != "wrong_editor", "wrong item must fail");
            }
            catch (InvalidOperationException) when (scenario == "wrong_editor") { }
            Require(withdrawals == originalIds.Length && submits == expectedSubmits, "all orders and configured registration count: " + scenario);
            var expectedCollects = scenario is "wrong_editor" or "no_proceeds" ? 0 : 1;
            Require(collects == expectedCollects && api.InventoryMoney == (expectedCollects == 0 ? 100UL : 150UL), "settlement exactly once, only after completed listing stage: " + scenario);
            if (expectedCollects == 1) Require(events[^1] == "collect", "collection is the final trade action");
            if (allOrders || scenario == "unconfigured") Require(api.InventoryItems.Single().Name == "unconfigured", "unmatched withdrawn item stays in bag");
            if (scenario == "full") Require(api.InventoryItems.Count == 1 && state.Listings.Count == 15, "fifteen listing limit still collects after retaining overflow");
            if (scenario == "continue_registration") Require(withdrawals == 0 && state.Listings.Any(l => l.ListingId == 80) && state.Listings.Count == 2,
                "explicit continuation preserves completed listings and registers only remaining inventory");
            Require(!down && input.KeyUps.Contains("ControlKey"), "release all inputs on every exit");
            if (scenario == "discount_floor") Require(!input.Keys.Any(k => k.StartsWith("D")), "default floor is submitted without retyping");
            if (scenario == "discount_above") Require(input.Keys.Any(k => k.StartsWith("D")), "higher discounted price is explicitly entered");
        }
    }
}
