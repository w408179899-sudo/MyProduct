using Roadhog.Application;
using Roadhog.Application.BagCleanup;
using Roadhog.Application.Trading;
using Roadhog.Application.Workers;
using Roadhog.Application.SemiAuto;
using Roadhog.Application.StationaryCombat;
using Roadhog.Core.Accounts;
using Roadhog.Core.Common;
using Roadhog.Core.Diagnostics;
using Roadhog.Core.Input;
using Roadhog.Core.Model;
using Roadhog.Core.Paths;

internal static partial class GroceryShopTests
{
    private sealed class SuccessStore : IGroceryShopSuccessStore
    {
        public DateTimeOffset? Last;
        public int Saves;
        public bool FailSave;
        public Action? OnSave;
        public Task<DateTimeOffset?> LoadAsync(string id, CancellationToken token = default) => Task.FromResult(Last);
        public Task RecordAsync(string id, DateTimeOffset time, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            if (FailSave) throw new IOException("success write failure");
            Last = time; Saves++; OnSave?.Invoke(); return Task.CompletedTask;
        }
    }

    private sealed class UnusedJournal : IAuctionListingJournal
    {
        public Task<AuctionListingHistory> LoadAsync(string account, string character, CancellationToken token) => throw new Exception("grocery must not use auction");
        public Task SaveAsync(string account, string character, AuctionListingHistory history, CancellationToken token) => throw new Exception("grocery must not use auction");
    }

    private sealed class Trip : IDisposable
    {
        public readonly FakeGameApi Api = new() { TargetEntityId = 0, TargetIsTargetingLocalPlayer = false, InventoryCapacity = 100 };
        public readonly RecordingKeyboardInput Input = new();
        public readonly InMemoryRoadhogLogger Logger = new();
        public readonly SuccessStore Success = new();
        public readonly CancellationTokenSource Stop = new(TimeSpan.FromSeconds(10));
        public readonly SharedPathDocument Route = new() { Name = "grocery", MapId = 2, Points = new() { new() { X = 1000 }, new() { X = 1020 } } };
        public readonly AccountConfig Config = new() { AccountName = "grocery", ScriptSettings = new() };
        public DateTimeOffset Now = At(12);
        public string Scenario = "success";
        public int ScrollClicks, Follows, Confirms, ShopStarts, Discards;
        public readonly List<string> Events = new();
        public readonly List<GameUiPoint> CursorVisits = new();
        public Action? AfterScrollClick;
        public bool FailDiscard;
        public bool AllowPreDiscard;
        public Action? AfterPreDiscardClose;
        public DateTimeOffset? FirstDiscardAt;
        private uint heldDiscard, pendingDiscard;
        private bool down;
        private int lastRightClickVisit;
        private DateTimeOffset? castAt;
        private string field = "", typed = "";
        private int sellingReads;
        private PersonalShopSnapshot shop = new(false, false, false, Array.Empty<InventoryUiItem>(), 0, Array.Empty<PersonalShopListing>(), null, new(700, 100))
        { AdvertisementText = new string('5', 36), AdvertisementBlurPoint = new(650, 200), StopButton = new(750, 100) };
        public bool BagOpen => shop.InventoryOpen;
        public AccountWorkerContext Context => new(Config, Api, Logger, new AccountRuntimeManager(Logger), new() { TickInterval = TimeSpan.FromMilliseconds(5) }, Stop.Token);
        public Trip()
        {
            Success.OnSave = () => Events.Add("record");
            var settings = Config.ScriptSettings!;
            settings.Maintenance.CleanupWorkflow.Mode = CleanupMode.GroceryShop;
            settings.Maintenance.BagCleanupThreshold = 2;
            settings.Maintenance.BagCleanupStallItems.Add(new() { Name = "goods", UnitPrice = 99999 });
            settings.Paths.GroceryStallPathName = Route.Name;
            settings.Paths.GroceryReturnItemName = "伏魔殿回程卷轴";
            Api.InventoryItems = new[] { new InventoryItemSnapshot(1, 11, settings.Paths.GroceryReturnItemName, 2, 0, false), new InventoryItemSnapshot(2, 12, "goods", 3, 1, false, VendorSellUnitPrice: 20000) };
            Input.AfterMove = (x, y) =>
            {
                Api.InventoryUiCursor = new(Api.InventoryUiCursor.X + x, Api.InventoryUiCursor.Y + y);
                CursorVisits.Add(Api.InventoryUiCursor);
            };
            Api.TransitionRead = () =>
            {
                if (castAt.HasValue)
                {
                    var elapsed = Now - castAt.Value;
                    if (Scenario == "death") Api.Player = Api.Player with { CurrentHp = 0 };
                    if (Scenario == "hp_attack") Api.Player = Api.Player with { CurrentHp = 90 };
                    if (Scenario == "attack") { Api.TargetEntityId = 3; Api.TargetIsTargetingLocalPlayer = true; }
                    if (Scenario == "cancel") Stop.Cancel();
                    if (Scenario is "success" or "slow_loading" or "wrong_landing" or "missing_loading_frame")
                    {
                        if (Scenario != "missing_loading_frame" && elapsed >= TimeSpan.FromSeconds(10) && elapsed < TimeSpan.FromSeconds(Scenario == "slow_loading" ? 70 : 11))
                        {
                            if (Scenario == "slow_loading") { Api.TargetEntityId = 3; Api.TargetIsTargetingLocalPlayer = true; }
                            return new(false, null, null, Now);
                        }
                        if (elapsed >= TimeSpan.FromSeconds(Scenario == "slow_loading" ? 70 : 11))
                        {
                            if (Api.Channel.MapId != 2) Events.Add("land");
                            Api.Channel = Api.Channel with { MapId = 2 };
                            Api.Player = Api.Player with { Position = new(Scenario == "wrong_landing" ? 500 : 1000, 0, 0) };
                            if (Scenario == "wrong_landing" && elapsed > TimeSpan.FromSeconds(25)) Stop.Cancel();
                        }
                    }
                }
                return new(true, Api.Player, Api.Channel, Now);
            };
            InventoryUiItem[] Cells() => Api.InventoryItems.Select(i => new InventoryUiItem((uint)i.InstanceId, i.TemplateId, i.Count, new(400 + i.Slot * 20, 300))).ToArray();
            Api.InventoryInteractionRead = () => new(shop.InventoryOpen, false, false, Cells(), Cells().FirstOrDefault(i => i.Point == Api.InventoryUiCursor)?.InstanceId ?? 0,
                pendingDiscard, pendingDiscard == 0 ? null : new(pendingDiscard, InventoryDiscardConfirmKind.Normal, 356, new(800, 380), null), new(500, 380), false);
            Api.PersonalShopRead = () =>
            {
                if (shop.IsSelling && ++sellingReads == 5)
                {
                    foreach (var listing in shop.Listings)
                    {
                        Api.InventoryMoney += listing.Quantity * listing.UnitPrice;
                        Api.InventoryItems = Api.InventoryItems.Where(i => i.InstanceId != listing.InstanceId).ToArray();
                    }
                    shop = shop with { Listings = Array.Empty<PersonalShopListing>() };
                    Now = At(20); // Sale crosses the next 19:00 departure.
                    Events.Add("sold_out");
                }
                return shop with { BagItems = Cells(), HoveredInstanceId = Cells().FirstOrDefault(i => i.Point == Api.InventoryUiCursor)?.InstanceId ?? 0 };
            };
            Input.AfterPress = key =>
            {
                if (key == "Escape" && Scenario == "timeout_race") Scenario = "slow_loading";
                if (key == "Escape") { pendingDiscard = 0; heldDiscard = 0; }
                if (key == "I")
                {
                    shop = shop with { InventoryOpen = !shop.InventoryOpen };
                    if (shop.InventoryOpen) Events.Add("open_bag");
                    else if (AllowPreDiscard && ScrollClicks == 0 && Events.Contains("pre_discard")) AfterPreDiscardClose?.Invoke();
                }
                else if (key == "Y") shop = shop with { IsOpen = !shop.IsOpen };
                else if (key == "A") typed = "";
                else if (key.StartsWith("D") && key.Length == 2 && char.IsDigit(key[1]))
                {
                    typed += key[1]; var number = ulong.Parse(typed); var editor = shop.Editor!;
                    editor = field == "quantity" ? editor with { Quantity = number } : editor with { UnitPrice = number };
                    shop = shop with { Editor = editor with { TotalPrice = editor.Quantity * editor.UnitPrice } };
                }
            };
            Input.AfterMouseDown = button =>
            {
                if (button == RoadhogMouseButton.Right)
                {
                    Check(CursorVisits.Skip(lastRightClickVisit).Contains(new(680, 468)), "every scroll or stall bag click first visits fixed point");
                    lastRightClickVisit = CursorVisits.Count;
                }
                down = true;
                if (button == RoadhogMouseButton.Left && !shop.IsOpen && pendingDiscard == 0)
                    heldDiscard = Cells().FirstOrDefault(i => i.Point == Api.InventoryUiCursor)?.InstanceId ?? 0;
            };
            Input.AfterMouseUp = button =>
            {
                if (!down) return; down = false;
                var point = Api.InventoryUiCursor;
                if (button == RoadhogMouseButton.Right)
                {
                    var item = Api.InventoryItems.Single(i => point == new GameUiPoint(400 + i.Slot * 20, 300));
                    if (item.Name == settings.Paths.GroceryReturnItemName)
                    { ScrollClicks++; castAt = Now; Events.Add("scroll"); AfterScrollClick?.Invoke(); }
                    else shop = shop with { Editor = new((uint)item.InstanceId, true, 1, item.Count, item.Count, new(500, 100), new(600, 100)) { QuantityInput = new(550, 100) } };
                }
                else if (heldDiscard != 0 && point == new GameUiPoint(500, 380))
                {
                    if (AllowPreDiscard && ScrollClicks == 0)
                        Check(!Events.Contains("land") && !shop.IsOpen && !shop.IsSelling, "pre-discard occurs locally before return or stall");
                    else
                    {
                        Check(Api.Channel.MapId == Route.MapId && Events.Contains("land"), "final discard drag requires confirmed return landing");
                        Check(Events.Contains("sold_out") && !shop.IsOpen && !shop.IsSelling, "final discard requires complete sellout and closed stall");
                        FirstDiscardAt ??= Now;
                    }
                    if (FailDiscard) throw new InvalidOperationException("discard input failure");
                    pendingDiscard = heldDiscard; heldDiscard = 0;
                }
                else if (pendingDiscard != 0 && point == new GameUiPoint(800, 380))
                {
                    var pre = AllowPreDiscard && ScrollClicks == 0;
                    Check(pre || Api.Channel.MapId == Route.MapId, "final discard confirmation occurs on destination map");
                    Api.InventoryItems = Api.InventoryItems.Where(i => i.InstanceId != pendingDiscard).ToArray();
                    pendingDiscard = 0; Discards++; Events.Add(pre ? "pre_discard" : "discard"); Now = Now.AddMinutes(1);
                }
                else if (point == new GameUiPoint(500, 100)) field = "price";
                else if (point == new GameUiPoint(550, 100)) field = "quantity";
                else if (point == new GameUiPoint(600, 100))
                {
                    var editor = shop.Editor!;
                    Check(editor.Quantity == 3 && editor.UnitPrice == 500, "reuse five discount rather than fixed list price");
                    shop = shop with { Editor = null, Listings = shop.Listings.Append(new(editor.InstanceId, 2, editor.Quantity, editor.UnitPrice)).ToArray() }; Confirms++;
                }
                else if (point == new GameUiPoint(700, 100)) { ShopStarts++; sellingReads = 0; shop = shop with { IsSelling = true }; Events.Add("shop"); }
                else if (point == new GameUiPoint(750, 100)) shop = shop with { IsSelling = false };
                else Check(point == new GameUiPoint(650, 200), "only known blur point clicked");
            };
        }
        public void AddDiscardCandidates(int count = 1)
        {
            Config.ScriptSettings!.Maintenance.BagCleanupDiscardItemNameKeywords.Add("junk");
            Api.InventoryItems = Api.InventoryItems.Concat(Enumerable.Range(0, count)
                .Select(i => new InventoryItemSnapshot((uint)(100 + i), (uint)(100 + i), "junk" + i, 1, 2 + i, false))).ToArray();
        }
        public Task Delay(int ms, CancellationToken token) { token.ThrowIfCancellationRequested(); Now = Now.AddMilliseconds(ms); return Task.CompletedTask; }
        public CleanupWorkflowRunner Runner() => new(Input, new InMemorySharedPathStore(Route), (_, name, points) =>
        {
            Check(Api.Channel.MapId == Route.MapId && !BagOpen && points.SequenceEqual(Route.Points.Select(p => p.ToVector3())), "confirm landing and close bag before full ordered route");
            Follows++; Events.Add("route"); return Task.FromResult(OperationResult.Ok());
        }, new UnusedJournal(), grocerySuccessStore: Success, groceryDelay: Delay, groceryClock: () => Now);
        public void Dispose() => Stop.Dispose();
    }

    public static async Task ReturnConfirmationAsync()
    {
        foreach (var scenario in new[] { "success", "slow_loading", "missing_loading_frame", "timeout_race", "attack", "hp_attack", "timeout", "death", "cancel", "wrong_landing" })
        {
            using var trip = new Trip { Scenario = scenario };
            try
            {
                await new GroceryReturnSequence(trip.Input, trip.Delay, () => trip.Now).RunAsync(trip.Context, trip.Route, trip.Config.ScriptSettings!.Paths.GroceryReturnItemName, _ => { });
                Check(scenario is "success" or "slow_loading" or "missing_loading_frame" or "timeout_race", "unconfirmed departure must fail: " + scenario);
            }
            catch (GroceryTripInterruptedException) when (scenario is "attack" or "hp_attack") { }
            catch (GroceryTripDepartureFailedException) when (scenario == "timeout") { }
            catch (CleanupDeathInterruptionException) when (scenario == "death") { }
            catch (OperationCanceledException) when (scenario is "cancel" or "wrong_landing") { }
            Check(trip.ScrollClicks == (scenario == "timeout" ? GroceryReturnSequence.MaxScrollClicks : 1),
                "retry only cancelled non-departures, never loading or combat: " + scenario);
            Check(trip.Input.KeyUps.Contains("ControlKey") && trip.Input.MouseCommands.Last() == "up:Right", "always release held inputs: " + scenario);
            if (scenario is "attack" or "hp_attack" or "timeout") Check(!trip.BagOpen && trip.Input.Keys.Contains("Escape"), "cancel cast and close bag before normal combat");
            if (scenario == "slow_loading") Check(!trip.Input.Keys.Contains("Escape"), "stale attackers cannot cancel after loading starts");
        }
    }

    public static async Task FlowGuardsAsync()
    {
        foreach (var scenario in new[] { "missing_scroll", "no_goods", "invalid_route" })
        {
            using var trip = new Trip();
            if (scenario == "missing_scroll") { trip.AddDiscardCandidates(); trip.Api.InventoryItems = trip.Api.InventoryItems.Skip(1).ToArray(); }
            if (scenario == "no_goods") trip.Config.ScriptSettings!.Maintenance.BagCleanupStallItems.Clear();
            if (scenario == "invalid_route") trip.Route.MapId = null;
            var context = trip.Context;
            Check(context.CleanupRequests.Request(trip.Config.ScriptSettings!, true, groceryTrigger: GroceryShopTrigger.Manual).Success, "request guard case");
            try { await trip.Runner().RunAsync(context, context.CleanupRequests.Current!); Check(scenario != "invalid_route", "reject path without map"); }
            catch (InvalidOperationException) when (scenario == "invalid_route") { }
            Check(trip.ScrollClicks == 0 && trip.Discards == 0 && trip.Follows == 0 && trip.ShopStarts == 0 && trip.Success.Saves == 0 && !context.CleanupRequests.Current!.SoldOutConfirmed, "skip cannot discard, travel or mark success: " + scenario);
            if (scenario == "missing_scroll") Check(!trip.Input.Keys.Any() && trip.Discards == 0 && trip.Api.InventoryItems.Any(i => i.Name == "junk0"), "missing scroll executes no bag actions or discard");
        }
    }

    public static async Task DiscardAfterSelloutAsync()
    {
        using var trip = new Trip();
        trip.AddDiscardCandidates(2);
        trip.Api.InventoryCapacity = 4;
        var context = trip.Context;
        context.CleanupRequests.Request(trip.Config.ScriptSettings!, false, groceryTrigger: GroceryShopTrigger.Scheduled);
        await trip.Runner().RunAsync(context, context.CleanupRequests.Current!);
        Check(trip.ScrollClicks == 1 && trip.Discards == 2 && trip.Api.InventoryItems.Count == 1 && !trip.BagOpen,
            "full bag must recall, sell goods then exhaust remaining discard candidates");
        Check(trip.Events.IndexOf("sold_out") < trip.Events.IndexOf("discard") && trip.Follows == 1 && trip.ShopStarts == 1,
            "capacity recovery cannot replace selling before final discard");
        Check(trip.Success.Saves == 1 && trip.Success.Last >= At(20) && trip.Success.Last == trip.FirstDiscardAt && trip.Now > trip.Success.Last && context.CleanupRequests.Current!.RequestsRestart,
            "record actual sellout time and permit restart only after final discard");
    }

    public static async Task PostSaleDiscardFailureAsync()
    {
        using var trip = new Trip { FailDiscard = true };
        trip.AddDiscardCandidates();
        var context = trip.Context;
        context.CleanupRequests.Request(trip.Config.ScriptSettings!, true, groceryTrigger: GroceryShopTrigger.Manual);
        try { await trip.Runner().RunAsync(context, context.CleanupRequests.Current!); throw new Exception("discard failure ignored"); }
        catch (InvalidOperationException) { }
        Check(trip.Events.Contains("sold_out") && trip.Success.Last >= At(20) && trip.Success.Last == trip.FirstDiscardAt && trip.Success.Saves == 1,
            "discard failure retains the actual successful sale record");
        Check(trip.Discards == 0 && trip.Api.InventoryItems.Any(i => i.Name == "junk0") && !trip.BagOpen &&
            !context.CleanupRequests.Current!.RequestsRestart && !context.CleanupRequests.Current.SoldOutConfirmed,
            "failed final discard cannot publish restart intent or delete unconfirmed items");
    }

    public static async Task InterruptedBeforeDiscardAsync()
    {
        foreach (var scenario in new[] { "attack", "hp_attack", "timeout", "death", "cancel" })
        {
            using var trip = new Trip { Scenario = scenario };
            trip.AddDiscardCandidates();
            var context = trip.Context;
            context.CleanupRequests.Request(trip.Config.ScriptSettings!, true, groceryTrigger: GroceryShopTrigger.Scheduled);
            try { await trip.Runner().RunAsync(context, context.CleanupRequests.Current!); throw new Exception("failed return must stop flow"); }
            catch (GroceryTripInterruptedException) when (scenario is "attack" or "hp_attack") { }
            catch (GroceryTripDepartureFailedException) when (scenario == "timeout") { }
            catch (CleanupDeathInterruptionException) when (scenario == "death") { }
            catch (OperationCanceledException) when (scenario == "cancel") { }
            Check(trip.ScrollClicks == (scenario == "timeout" ? GroceryReturnSequence.MaxScrollClicks : 1) &&
                trip.Discards == 0 && trip.Follows == 0 && trip.ShopStarts == 0 && trip.Success.Saves == 0,
                "unsuccessful recall cannot discard, walk, sell or record success: " + scenario);
            Check(trip.Api.InventoryItems.Any(i => i.Name == "junk0") && !trip.Events.Contains("discard"), "discard candidates survive interrupted recall");
        }
    }

    public static async Task SaleCompletionAsync()
    {
        foreach (var trigger in new[] { GroceryShopTrigger.Manual, GroceryShopTrigger.Scheduled })
        {
            using var trip = new Trip();
            trip.AddDiscardCandidates();
            trip.Api.InventoryCapacity = trigger == GroceryShopTrigger.Backpack ? 2 : 100;
            var settings = trip.Config.ScriptSettings!;
            settings.Maintenance.CleanupWorkflow.GroceryScheduleEnabled = true;
            settings.Maintenance.CleanupWorkflow.GroceryScheduleTimes = new() { "12:00", "19:00" };
            // Even broad discard/stall keywords must not consume the selected scroll.
            settings.Maintenance.BagCleanupDiscardItemNameKeywords.Add("卷轴");
            settings.Maintenance.BagCleanupDiscardItemNameKeywords.Add("goods");
            settings.Maintenance.BagCleanupStallItems.Add(new() { Name = "卷轴", UnitPrice = 1 });
            var context = trip.Context; var runner = trip.Runner();
            Check(context.CleanupRequests.Request(settings, true, groceryTrigger: trigger).Success, "request sale");
            await runner.RunAsync(context, context.CleanupRequests.Current!, _ => throw new Exception("old worker cannot return to combat after sellout"));
            Check(trip.ScrollClicks == 1 && trip.Follows == 1 && trip.Confirms == 1 && trip.ShopStarts == 1, "one complete trip and discounted sale");
            Check(trip.Discards == 1 && trip.Events.IndexOf("open_bag") < trip.Events.IndexOf("scroll") &&
                trip.Events.IndexOf("scroll") < trip.Events.IndexOf("land") && trip.Events.IndexOf("land") < trip.Events.IndexOf("route") &&
                trip.Events.IndexOf("route") < trip.Events.IndexOf("shop") && trip.Events.IndexOf("shop") < trip.Events.IndexOf("sold_out") &&
                trip.Events.IndexOf("sold_out") < trip.Events.IndexOf("record") && trip.Events.IndexOf("record") < trip.Events.IndexOf("discard"),
                "manual, scheduled and backpack flow recalls, follows route, sells out, records sale then discards before restart");
            Check(trip.Api.InventoryItems.Single().Name.Contains("卷轴") && trip.Success.Saves == 1 && trip.Success.Last >= At(20), "protect scroll and persist actual sellout");
            Check(context.CleanupRequests.Current!.SoldOutConfirmed && context.CleanupRequests.Current.RequestsRestart && !trip.BagOpen, "restart intent requires successful durable record and UI closure");
            Check(!await runner.GroceryScheduleDueAsync(context, trip.Now), "20h sellout covers 19h schedule");
        }
        using var failed = new Trip(); failed.Success.FailSave = true;
        var failedContext = failed.Context;
        failedContext.CleanupRequests.Request(failed.Config.ScriptSettings!, true);
        try { await failed.Runner().RunAsync(failedContext, failedContext.CleanupRequests.Current!); throw new Exception("save failure ignored"); }
        catch (IOException) { }
        Check(!failedContext.CleanupRequests.Current!.SoldOutConfirmed && !failedContext.CleanupRequests.Current.RequestsRestart && failed.Success.Saves == 0, "record failure cannot falsely complete or restart");
    }

    public static async Task ScheduleObservedRetriesAsync()
    {
        using var trip = new Trip();
        var context = trip.Context; var runner = trip.Runner();
        var flow = trip.Config.ScriptSettings!.Maintenance.CleanupWorkflow;
        flow.GroceryScheduleEnabled = true;
        flow.GroceryScheduleTimes = new() { "10:10" };
        Check(!await runner.GroceryScheduleDueAsync(context, At(10, 1)), "script3 reproduction: no completion record and future 10:10 cannot depart at 10:01");
        Check(trip.Logger.Entries.All(e => e.EventName != "grocery_shop.schedule.due"), "future schedule does not create a due event");
        Check(await runner.GroceryScheduleDueAsync(context, At(10, 10)), "exact minute creates today's pending task");
        Check(await runner.GroceryScheduleDueAsync(context, At(10, 11)), "failed or missing-scroll attempt does not consume pending time");
        Check(trip.Logger.Entries.Count(e => e.EventName == "grocery_shop.schedule.due") == 1 &&
            Equals(trip.Logger.Entries.Single(e => e.EventName == "grocery_shop.schedule.due").Fields["dueAt"], At(10, 10)),
            "log actual due date once instead of logging every retry");
        Check(await runner.GroceryScheduleDueAsync(context, At(0).AddDays(1)), "running session preserves actually observed task across midnight");
        Check(!await runner.GroceryScheduleDueAsync(trip.Context, At(0).AddDays(1)), "new worker session cannot invent yesterday's task from absent success");
        flow.GroceryScheduleEnabled = false;
        Check(!await runner.GroceryScheduleDueAsync(context, At(0).AddDays(1)), "disable cancels pending scheduling");
        flow.GroceryScheduleEnabled = true;
        Check(!await runner.GroceryScheduleDueAsync(context, At(0).AddDays(1)), "re-enable before first daily time does not resurrect yesterday");
        Check(await runner.GroceryScheduleDueAsync(context, At(10, 10).AddDays(1)), "next daily time triggers normally");
        flow.GroceryScheduleTimes = new() { "12:00", "19:00" };
        Check(!await runner.GroceryScheduleDueAsync(context, At(10, 11).AddDays(1)), "replacing time drops removed pending slot rather than making new future slots due");
        Check(await runner.GroceryScheduleDueAsync(context, At(12).AddDays(1)), "new first time becomes due at noon");
        Check(await runner.GroceryScheduleDueAsync(context, At(19).AddDays(1)), "new later due slot coalesces existing retry");
        trip.Success.Last = At(20).AddDays(1);
        Check(!await runner.GroceryScheduleDueAsync(context, At(20).AddDays(1)), "actual 20h completion covers both 12h and 19h pending slots");
        Check(!await runner.GroceryScheduleDueAsync(context, At(0).AddDays(2)), "completed slots do not return across midnight");
        Check(await runner.GroceryScheduleDueAsync(context, At(12).AddDays(2)), "actual success does not suppress next day's schedule");
        flow.Mode = CleanupMode.Normal;
        Check(!await runner.GroceryScheduleDueAsync(context, At(0).AddDays(3)), "normal cleanup clears grocery pending scheduling");
        flow.Mode = CleanupMode.GroceryShop;
        Check(!await runner.GroceryScheduleDueAsync(context, At(0).AddDays(3)), "switching back before first time cannot infer a past-day task");
        Check(trip.Success.Saves == 0 && trip.ScrollClicks == 0 && trip.Input.MouseCommands.Count == 0,
            "schedule observation neither records success nor sends game input");
        trip.Stop.Cancel();
        try { await runner.GroceryScheduleDueAsync(context, At(12).AddDays(3)); throw new Exception("stopped schedule observation accepted"); }
        catch (OperationCanceledException) { }
    }

    public static async Task WorkerRetryAndCompletionAsync()
    {
        foreach (var scheduled in new[] { false, true })
        {
            using var trip = new Trip { Scenario = "attack" };
            var settings = trip.Config.ScriptSettings!;
            settings.MainMode = trip.Config.MainMode = AccountMainMode.SemiAuto;
            settings.Maintenance.BagCleanupEnabled = !scheduled;
            trip.Api.InventoryCapacity = scheduled ? 100 : 2;
            settings.Maintenance.CleanupWorkflow.GroceryScheduleEnabled = scheduled;
            settings.Maintenance.CleanupWorkflow.GroceryScheduleTimes = new() { "00:00" };
            var paths = new InMemorySharedPathStore(trip.Route);
            var semi = new SemiAutoCombatController(trip.Input);
            var loop = new DefaultAccountWorkerLoop(trip.Input, semi, new StationaryCombatController(trip.Input, semi, paths), cleanupWorkflow: trip.Runner());
            var host = new AccountWorkerHost(trip.Api, trip.Logger, new AccountRuntimeManager(trip.Logger), loop, new() { TickInterval = TimeSpan.FromMilliseconds(5) });
            async Task Until(Func<bool> condition)
            {
                while (!condition()) { trip.Stop.Token.ThrowIfCancellationRequested(); Check(host.IsRunning, "worker remains alive after interrupt"); await Task.Delay(10, trip.Stop.Token); }
            }
            try
            {
                Check(host.Start(trip.Config).Success, "ordinary worker automatically detects trigger");
                await Until(() => trip.Logger.Entries.Any(e => e.EventName == "grocery_shop.attempt_ended"));
                Check(trip.ScrollClicks == 1 && trip.Success.Saves == 0 && host.StandaloneShopRestartRequestId == null && !trip.BagOpen, "interrupted attempt clears without success or restart");
                await Task.Delay(2200, trip.Stop.Token);
                Check(trip.ScrollClicks == 1, "live combat target blocks a new scroll cast");
                trip.Scenario = "success"; trip.Api.TargetEntityId = 0; trip.Api.TargetIsTargetingLocalPlayer = false;
                await Until(() => host.StandaloneShopRestartRequestId.HasValue);
                Check(trip.ScrollClicks == 2 && trip.Success.Saves == 1, "fresh trigger retries after combat regardless of cleanup cooldown");
                Check(trip.Logger.Entries.Count(e => e.EventName == "grocery_shop.attempt_ended") == 1 && trip.Logger.Entries.Count(e => e.EventName == "standalone_shop.restart.requested") == 1, "one interrupted attempt then one completion handoff");
                var count = trip.Input.Keys.Count + trip.Input.MouseCommands.Count;
                await Task.Delay(200, trip.Stop.Token);
                Check(count == trip.Input.Keys.Count + trip.Input.MouseCommands.Count, "completed worker remains idle for manager stop/start");
                Check((await host.StopAsync()).Success && host.StandaloneShopRestartRequestId == null, "Stop drains completion signal");
            }
            finally { await host.StopAsync(); }
        }
    }
}
