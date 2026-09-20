using System.Text.Json;
using Roadhog.Application.Trading;
using Roadhog.Application.Workers;
using Roadhog.Core.Accounts;
using Roadhog.Core.Api;
using Roadhog.Core.Diagnostics;
using Roadhog.Core.Input;
using Roadhog.Core.Model;
using Roadhog.Infrastructure.Config;

internal static partial class CleanupWorkflowTests
{
    private static void Require(bool value, string message) { if (!value) throw new Exception(message); }
    private static Task Fast(int ms, CancellationToken token) { token.ThrowIfCancellationRequested(); return Task.CompletedTask; }
    private static PersonalShopSnapshot Shop() => new(false, false, false, Array.Empty<InventoryUiItem>(), 0, Array.Empty<PersonalShopListing>(), null, null);
    private static AuctionHouseSnapshot Auction() => new(false, null, true, 0, new Dictionary<string, GameUiPoint>
    {
        ["account_btn"] = new(100,100), ["register_item_btn"] = new(200,100), ["collect_btn"] = new(300,100)
    }, "", "", Array.Empty<AuctionMarketRow>(), true, 0, null) { ListingsLoaded = true };
    private static void Cursor(FakeGameApi api, RecordingKeyboardInput input) => input.AfterMove = (x,y) => api.InventoryUiCursor = new(api.InventoryUiCursor.X + x, api.InventoryUiCursor.Y + y);
    private sealed class Journal : IAuctionListingJournal
    {
        public AuctionListingHistory History = new();
        public Task<AuctionListingHistory> LoadAsync(string account, string character, CancellationToken token) => Task.FromResult(History);
        public Task SaveAsync(string account, string character, AuctionListingHistory history, CancellationToken token) { History = history; return Task.CompletedTask; }
    }

    public static async Task ConfigurationAndPolicyAsync()
    {
        var legacy = JsonSerializer.Deserialize<ScriptSettings>("{}")!;
        Require(legacy.Maintenance.CleanupWorkflow.NpcCleanup && !legacy.Maintenance.CleanupWorkflow.Auction && !legacy.Maintenance.CleanupWorkflow.TransferGold, "legacy defaults retain ordinary cleanup only");
        var config = new AccountConfig { ScriptSettings = new() };
        var s = config.ScriptSettings;
        s.Paths.AuctionPathName = "auction"; s.Paths.StallPathName = "stall";
        s.Maintenance.CleanupWorkflow = new() { Auction = true, TransferGold = true, PersonalShop = true, WarehouseName = "warehouse", WarehouseSelectionKey = "F7", OldListingAction = AuctionOldListingAction.Reprice, OldListingHours = 36 };
        var clone = JsonSerializer.Deserialize<AccountConfig>(JsonSerializer.Serialize(config.Clone()))!;
        Require(clone.ScriptSettings!.Paths.AuctionPathName == "auction" && clone.ScriptSettings.Paths.StallPathName == "stall" && clone.ScriptSettings.Maintenance.CleanupWorkflow.OldListingHours == 36, "paths and workflow survive clone and persistence");
        clone.ScriptSettings.Maintenance.CleanupWorkflow.WarehouseName = "changed";
        Require(s.Maintenance.CleanupWorkflow.WarehouseName == "warehouse", "workflow copies do not share mutations");
        var mailbox = new CleanupRequestMailbox();
        var requests = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => Task.Run(() => mailbox.Request(s, true))));
        Require(requests.Count(r => r.Success) == 1, "one pending or executing manual request");
        mailbox.Complete(); Require(mailbox.Request(s, false).Success, "next request after completion");
        var automatic = mailbox.Current!.Settings.Maintenance.CleanupWorkflow;
        Require(automatic.NpcCleanup && automatic.Auction && !automatic.TransferGold && !automatic.PersonalShop, "automatic trigger cannot transfer or stall");
        Require(s.Maintenance.CleanupWorkflow.TransferGold, "automatic masking cannot modify saved preferences");
        var item = new InventoryItemSnapshot(1, 11, "green sword", 3, 0, false, ItemType: 1, QualityRank: 1);
        s.Maintenance.BagCleanupAuctionHouseItems.Add(new() { Name = "sword", UnitPrice = 30 });
        s.Maintenance.BagCleanupStallItems.Add(new() { Name = "sword", UnitPrice = 20 });
        s.Maintenance.BagCleanupDiscardItemNameKeywords.Add("sword");
        Require(CleanupTradePolicy.Rule(item, s.Maintenance, true)?.UnitPrice == 30 && CleanupTradePolicy.Rule(item, s.Maintenance, false) == null, "auction owns overlaps");
        Require(!BagCleanupItemMatcher.SelectDiscardItems(new[] { item }, s.Maintenance).Any() && !BagCleanupItemMatcher.SelectSellRegistrationItems(new[] { item }, s.Maintenance).Any(), "reserved trading items cannot be discarded or NPC sold");
        s.Maintenance.BagCleanupExcludedItemNames.Add("sword"); Require(CleanupTradePolicy.Rule(item, s.Maintenance, true) == null, "whitelist dominates trading");
        Require(CleanupTradePolicy.PurchaseQuantity(100, 30, 9, 8) == 3 && CleanupTradePolicy.PurchaseQuantity(100, 30, 2, 8) == 2 && CleanupTradePolicy.PurchaseQuantity(100, 30, 9, 1) == 1 && CleanupTradePolicy.PurchaseQuantity(100, 0, 9, 9) == 0, "affordability, stock and modal caps");
        var now = DateTimeOffset.UtcNow; var listing = new AuctionListing(10, 20, 3, "item", 30, "7 days", null);
        var history = new AuctionListingHistory(); Require(!history.IsOld(listing, now, 24), "display days cannot invent listing age");
        history.Listings.Add(new(10,20,10,now.AddHours(-25)));
        Require(history.IsOld(listing,now,24) && !history.IsOld(listing with { TemplateId = 99 },now,24), "old listing identity and age both required");
        var directory = Path.Combine(Path.GetTempPath(), "cleanup-journal-" + Guid.NewGuid().ToString("N"));
        try
        {
            var journal = new JsonAuctionListingJournal(directory);
            await journal.SaveAsync("account", "character", history, default);
            Require((await journal.LoadAsync("account", "character", default)).Listings.Count == 1 && (await journal.LoadAsync("account", "other", default)).Listings.Count == 0, "journal persists and isolates characters");
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    public static async Task AuctionSubmissionAsync()
    {
        foreach (var scenario in new[] { "manual", "minimum", "no_quote", "full", "search", "wrong_editor", "withdraw", "reprice", "unknown_age" })
        {
            var api = new FakeGameApi { InventoryMoney = 100 }; var input = new RecordingKeyboardInput(); Cursor(api,input);
            using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            var state = Auction() with { SettlementMoney = 50 }; var journal = new Journal();
            var item = new InventoryItemSnapshot(20, 11, "item", 3, 0, false);
            api.InventoryItems = new[] { item }; api.AuctionRead = () => state with { HoveredListingId = api.InventoryUiCursor == new GameUiPoint(400,100) ? 8u : 0u };
            var bagItem = new InventoryUiItem(11,20,3,new(700,300));
            var bagOpen = true;
            api.InventoryInteractionRead = () => new(bagOpen,false,false,new[] {bagItem},api.InventoryUiCursor == bagItem.Point ? 11u : 0u,0,null,null,false);
            var settings = new MaintenanceScriptSettings { BagCleanupAuctionHouseItems = new() { new() { Name = "item", UnitPrice = 17,
                PriceLookupMethod = scenario == "search" ? AuctionPriceLookupMethod.SearchCalculation : scenario is "minimum" or "no_quote" ? AuctionPriceLookupMethod.DialogMinimum : AuctionPriceLookupMethod.Manual } } };
            if (scenario == "full") state = state with { Listings = Enumerable.Range(1,15).Select(i => new AuctionListing((uint)i,99,1,"other",1,"7",null)).ToArray() };
            if (scenario is "withdraw" or "reprice" or "unknown_age")
            {
                settings.CleanupWorkflow.OldListingAction = scenario == "reprice" ? AuctionOldListingAction.Reprice : AuctionOldListingAction.Withdraw;
                api.InventoryItems = Array.Empty<InventoryItemSnapshot>();
                state = state with { Listings = new[] {new AuctionListing(8,20,3,"item",90,"7",new(400,100))} };
                if (scenario != "unknown_age") journal.History.Listings.Add(new(8,20,30,DateTimeOffset.UtcNow.AddDays(-2)));
            }
            int submits = 0, collects = 0, withdrawals = 0; bool down = false; string typed = "";
            input.AfterPress = key =>
            {
                if (key == "Space") state = state with { IsOpen = false };
                else if (key == "I") bagOpen = !bagOpen;
                else if (key == "A") typed = "";
                else if (key.StartsWith("D") && state.Editor != null) { typed += key[1..]; state = state with { Editor = state.Editor with { UnitPrice = ulong.Parse(typed) } }; }
                else throw new Exception("unexpected key " + key);
            };
            input.AfterMouseDown = _ => down = true;
            input.AfterMouseUp = button =>
            {
                if (!down) return; down = false;
                var p = api.InventoryUiCursor;
                if (button == RoadhogMouseButton.Right)
                {
                    if (p == new GameUiPoint(400,100)) { state = state with { WithdrawConfirmation = new(8,new(410,100)) }; return; }
                    state = state with { Editor = new(scenario == "wrong_editor" ? 99u : 20u,3,1,scenario == "no_quote" ? null : 23,new(600,100))
                    { InstanceId = 11, MaximumQuantity = 3, MinimumAllowedPrice = 2, UnitPriceMode = true, PriceInput = new(500,100), ConfirmButton = new(550,100) } }; return;
                }
                if (p == new GameUiPoint(100,100)) state = state with { ActiveTab = 2 };
                else if (p == new GameUiPoint(200,100)) state = state with { ActiveTab = 1 };
                else if (p == new GameUiPoint(300,100)) { collects++; api.InventoryMoney += 50; state = state with { SettlementMoney = 0 }; }
                else if (p == new GameUiPoint(500,100)) { }
                else if (p == new GameUiPoint(600,100)) state = state with { Editor = null };
                else if (p == new GameUiPoint(410,100)) { withdrawals++; state = state with { WithdrawConfirmation = null, Listings = Array.Empty<AuctionListing>() }; api.InventoryItems = new[]{ item }; }
                else if (p == new GameUiPoint(550,100))
                {
                    Require(collects == 1,"settle before listing"); submits++;
                    var price = scenario == "minimum" ? 23UL : 17UL;
                    Require(state.Editor!.UnitPrice == price,"correct strategy price, automatic ignores manual price");
                    state = state with { Editor = null, Listings = new[]{new AuctionListing(100,20,3,"item",price*3,"7",null)} }; api.InventoryItems = Array.Empty<InventoryItemSnapshot>();
                }
                else throw new Exception("unexpected click " + p);
            };
            try
            {
                await new AuctionTradingSequence(input,journal,Fast).RunAsync(api.Create(new(),new InMemoryRoadhogLogger(),stop.Token),"account",settings,_=>{},stop.Token);
                Require(scenario != "wrong_editor","wrong item must fail");
            }
            catch (InvalidOperationException) when (scenario == "wrong_editor") { }
            Require(collects == 1 && api.InventoryMoney == 150,"settlement confirmed exactly once " + scenario);
            Require(submits == (scenario is "manual" or "minimum" or "reprice" ? 1 : 0),"only eligible items committed " + scenario);
            Require(withdrawals == (scenario is "withdraw" or "reprice" ? 1 : 0),"withdraw only aged owned listings " + scenario);
            if (submits == 1) Require(journal.History.Listings.Single().ListingId == 100,"successful listing journaled");
            if (scenario == "withdraw") Require(journal.History.WithdrawnTemplates.Contains(20) && api.InventoryItems.Count == 1,"withdraw without relisting persists protection");
        }
    }

    public static async Task WarehousePurchaseAsync()
    {
        foreach (var scenario in new[] { "stack", "single", "wrong_owner", "insufficient", "cancel_wait", "unconfirmed" })
        {
            var api = new FakeGameApi { InventoryMoney = scenario == "insufficient" ? 20UL : 100UL, TargetName = "elsewhere", TargetOwnServerObjectId = 5 };
            var input = new RecordingKeyboardInput(); Cursor(api,input);
            using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            var item = new ShopPurchaseItem(8,20,scenario == "single" ? 1UL : 9UL,30,new(500,300));
            var purchase = ShopPurchaseSnapshot.Closed; bool down = false; int purchases = 0, right = 0, selects = 0, opens = 0; string typed = "";
            var now = DateTimeOffset.UtcNow;
            Task Advance(int ms, CancellationToken t) { now = now.AddMilliseconds(ms); return Fast(ms,t); }
            api.PersonalShopRead = () => Shop() with { Purchase = purchase with { HoveredInstanceId = api.InventoryUiCursor == item.Point ? item.InstanceId : 0 } };
            input.AfterPress = key =>
            {
                if (key == "F7") { selects++; if (scenario == "cancel_wait") stop.Cancel(); else {api.TargetName="warehouse";api.TargetOwnServerObjectId=7;} }
                else if (key == "C") { opens++; if (scenario == "wrong_owner" && opens > 1) stop.Cancel(); purchase = new(true,scenario == "wrong_owner" ? 99u : 7u,new[]{item},Array.Empty<ShopPurchaseItem>(),0,null,new(700,300)); }
                else if (key == "Space") purchase = ShopPurchaseSnapshot.Closed;
                else if (key == "A") typed = "";
                else if (key.StartsWith("D")) { typed += key[1..]; purchase = purchase with { QuantityDialog = purchase.QuantityDialog! with {Quantity=ulong.Parse(typed)} }; }
            };
            input.AfterMouseDown = _ => down = true;
            input.AfterMouseUp = button =>
            {
                if (!down) return;down=false;
                if (button == RoadhogMouseButton.Right)
                {
                    right++;
                    Require(input.KeyDowns.Contains("ShiftKey"),"purchase uses shift");
                    purchase = item.Quantity == 1 ? purchase with { Basket = new[]{ item } } : purchase with { QuantityDialog = new(8,9,9,new(600,300),new(650,300)) }; return;
                }
                var p = api.InventoryUiCursor;
                if (p == new GameUiPoint(600,300)) return;
                if (p == new GameUiPoint(650,300)) { var q = purchase.QuantityDialog!.Quantity; Require(q == 3,"floor affordability"); purchase = purchase with { QuantityDialog = null, Basket = new[]{item with{Quantity=q}} }; return; }
                Require(p == new GameUiPoint(700,300),"final purchase button"); purchases++;
                if (scenario == "unconfirmed") { stop.Cancel(); return; }
                var quantity = purchase.Basket.Single().Quantity; api.InventoryMoney -= quantity * 30;
                api.InventoryItems = new[]{new InventoryItemSnapshot(20,88,"bought",checked((uint)quantity),0,false)};
                purchase = purchase with {Basket=Array.Empty<ShopPurchaseItem>()};
            };
            try
            {
                await new WarehousePurchaseSequence(input,Advance,()=>now).RunAsync(api.Create(new(),new InMemoryRoadhogLogger(),stop.Token), new(){WarehouseName="warehouse",WarehouseSelectionKey="F7"},_=>{},stop.Token);
                Require(scenario is "stack" or "single" or "insufficient","must wait or cancel " + scenario);
            }
            catch(OperationCanceledException) when(scenario is "wrong_owner" or "cancel_wait" or "unconfirmed") { }
            Require(purchases == (scenario is "stack" or "single" or "unconfirmed" ? 1 : 0),"one final submit only " + scenario);
            Require(right <= 1 && selects >= 1,"select by configured key and never repeat purchase");
            Require(input.KeyUps.Contains("ShiftKey"),"shift always released");
            if(scenario=="stack")Require(api.InventoryMoney==10,"retain unaffordable remainder, no reserve policy");
        }
    }

    public static async Task ConfiguredStallAsync()
    {
        foreach (var scenario in new[] { "batches", "purchased_merge", "closed_early", "cancel_wait" })
        {
            var api = new FakeGameApi { InventoryMoney = 100 }; var input = new RecordingKeyboardInput(); Cursor(api,input);
            using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            var count = scenario == "batches" ? 12 : 1;
            var plan = Enumerable.Range(0,count).Select(i => new PlannedShopItem(new InventoryItemSnapshot((uint)(20+i),(ulong)(11+i),"item"+i,3,i,false),(ulong)(17+i))).ToArray();
            api.InventoryItems = plan.Select(p => scenario == "purchased_merge" ? p.Item with { Count = 8 } : p.Item).ToArray();
            var state = Shop() with { StartButton = new(700,100), StopButton = new(750,100) };
            bool down=false; int starts=0, confirms=0, sellingReads=0; string typed="",field="";
            api.PersonalShopRead = () =>
            {
                if(state.IsSelling && ++sellingReads == 5)
                {
                    if(scenario=="closed_early")state=state with{IsSelling=false,IsOpen=false,Listings=Array.Empty<PersonalShopListing>()};
                    else if(scenario=="cancel_wait")stop.Cancel();
                    else
                    {
                        foreach(var l in state.Listings)
                        {
                            api.InventoryMoney += l.Quantity*l.UnitPrice;
                            api.InventoryItems = api.InventoryItems.Select(i => i.InstanceId==l.InstanceId ? i with{Count=checked(i.Count-(uint)l.Quantity)}:i).Where(i=>i.Count>0).ToArray();
                        }
                        state=state with{Listings=Array.Empty<PersonalShopListing>()};
                    }
                }
                var items=api.InventoryItems.Select(i=>new InventoryUiItem((uint)i.InstanceId,i.TemplateId,i.Count,new(400+i.Slot*20,300))).ToArray();
                return state with {BagItems=items,HoveredInstanceId=items.FirstOrDefault(i=>i.Point==api.InventoryUiCursor)?.InstanceId??0};
            };
            input.AfterPress = key =>
            {
                if(key=="Y")state=state with{IsOpen=!state.IsOpen};
                else if(key=="I")state=state with{InventoryOpen=!state.InventoryOpen};
                else if(key=="A")typed="";
                else if(key.StartsWith("D"))
                {
                    typed+=key[1..];var n=ulong.Parse(typed);var e=state.Editor!;
                    e=field=="quantity"?e with{Quantity=n}:e with{UnitPrice=n};
                    state=state with{Editor=e with{TotalPrice=e.Quantity*e.UnitPrice}};
                }
                else throw new Exception("unexpected shop key "+key);
            };
            input.AfterMouseDown = _=>down=true;
            input.AfterMouseUp = button=>
            {
                if(!down)return;down=false;
                var p=api.InventoryUiCursor;
                if(button==RoadhogMouseButton.Right)
                {
                    var item=api.InventoryItems.Single(i=>p==new GameUiPoint(400+i.Slot*20,300));
                    state=state with{Editor=new((uint)item.InstanceId,true,1,item.Count,item.Count,new(500,100),new(600,100)){QuantityInput=new(550,100)}};
                }
                else if(p==new GameUiPoint(500,100))field="price";
                else if(p==new GameUiPoint(550,100))field="quantity";
                else if(p==new GameUiPoint(600,100))
                {
                    var e=state.Editor!; var planned=plan.Single(i=>i.Item.InstanceId==e.InstanceId);
                    Require(e.Quantity==3&&e.UnitPrice==planned.UnitPrice,"uses configured price and original planned quantity");
                    state=state with{Editor=null,Listings=state.Listings.Append(new(e.InstanceId,planned.Item.TemplateId,e.Quantity,e.UnitPrice)).ToArray()};confirms++;
                }
                else if(p==new GameUiPoint(700,100)){Require(state.Listings.Count<=10,"shop capacity");starts++;sellingReads=0;state=state with{IsSelling=true};}
                else if(p==new GameUiPoint(750,100))state=state with{IsSelling=false};
                else throw new Exception("unexpected shop click "+p);
            };
            try
            {
                await new ConfiguredPersonalShopSequence(input,Fast).RunAsync(api.Create(new(),new InMemoryRoadhogLogger(),stop.Token),plan,_=>{},stop.Token);
                Require(scenario is "batches" or "purchased_merge","closed UI cannot prove sold out");
                Require(confirms==count&&starts==(count+9)/10,"all matching entries sold over batches");
                if(scenario=="purchased_merge")Require(api.InventoryItems.Single().Count==5,"warehouse purchase excluded even when merged into same stack");
            }
            catch(InvalidOperationException)when(scenario=="closed_early"){}
            catch(OperationCanceledException)when(scenario=="cancel_wait"){}
            Require(!down&&input.KeyUps.Contains("ControlKey"),"stall input released on every exit");
        }
    }
}
