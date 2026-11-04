using Roadhog.Application;
using Roadhog.Application.BagCleanup;
using Roadhog.Application.SemiAuto;
using Roadhog.Application.StationaryCombat;
using Roadhog.Application.Trading;
using Roadhog.Application.Workers;
using Roadhog.Core.Accounts;
using Roadhog.Core.Diagnostics;
using Roadhog.Core.Model;
using Roadhog.Core.Paths;

internal static class AutomaticDiscardLimitTests
{
    private static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }

    public static Task PriorityAsync()
    {
        var settings = new MaintenanceScriptSettings();
        settings.BagCleanupDiscardItemNameKeywords.Add("discard");
        settings.BagCleanupExcludedItemNames.Add("keep");
        settings.BagCleanupSellItemNameKeywords.Add("sell");
        InventoryItemSnapshot Item(ulong id, string name, uint type = 0) => new(1, id, name, 1, (int)id, false, type);
        var bag = new[]
        {
            Item(1, "discard armor", 7), Item(2, "discard material"), Item(3, "discard recipe", 27),
            Item(4, "discard stigma", 9), Item(5, "discard skill", 31), Item(6, "discard skill B", 31),
            Item(7, "discard 技能书"), Item(8, "discard 烙印"), Item(9, "discard 制作卷轴"),
            Item(10, "keep discard skill", 31), Item(11, "sell skill", 31), Item(12, "discard equipped", 31) with { IsEquipped = true },
            Item(13, "discard 疾走卷轴"), Item(14, "discard 料理法")
        };
        Check(BagCleanupItemMatcher.SelectDiscardItems(bag.Reverse(), settings).Select(i => i.InstanceId)
            .SequenceEqual(new ulong[] { 5, 6, 7, 4, 8, 3, 9, 14, 1, 2, 13 }),
            "skill books, stigmas, crafting scrolls, then others; ties preserve bag order and protected/non-discard items stay excluded");
        Check(BagCleanupItemMatcher.SelectSellRegistrationItems(bag, settings).Select(i => i.InstanceId).SequenceEqual(new ulong[] { 11 }),
            "discard priority does not change configured sale eligibility");
        return Task.CompletedTask;
    }

    private sealed class Run : IDisposable
    {
        public readonly InventoryDiscardTests.Simulation Game;
        public readonly InMemoryRoadhogLogger Logger = new();
        public readonly CancellationTokenSource Stop = new(TimeSpan.FromSeconds(90));
        public readonly AccountWorkerContext Context;
        public readonly BagCleanupController Controller;
        public Run(int count)
        {
            Game = new(count);
            Game.Api.TargetEntityId = 0;
            Game.Api.TargetIsTargetingLocalPlayer = false;
            Game.Api.Channel = Game.Api.Channel with { MapId = 2 };
            Game.Api.Player = Game.Api.Player with { Position = new(0, 0, 0) };
            Game.Api.InventoryCapacity = count;
            Game.Settings.BagCleanupEnabled = true;
            Game.Settings.BagCleanupThreshold = count;
            Game.Settings.BagCleanupDiscardItemNameKeywords.Add("item");
            var settings = new ScriptSettings { MainMode = AccountMainMode.SemiAuto, Maintenance = Game.Settings };
            Context = new(new() { AccountName = "discard-limit", MainMode = AccountMainMode.SemiAuto, ScriptSettings = settings },
                Game.Api, Logger, new AccountRuntimeManager(Logger), new() { TickInterval = TimeSpan.FromMilliseconds(5) }, Stop.Token);
            var seller = new BagCleanupSeller(Game.Input);
            Controller = new(Game.Input, new InMemorySharedPathStore(), (_, _, _) => throw new Exception("local limit cannot travel"),
                seller: seller, discarder: new BagCleanupDiscarder(Game.Input, seller, (_, ct) => Task.Delay(1, ct)));
        }
        public async Task<BagCleanupTickResult> Finish(BagCleanupState state)
        {
            BagCleanupTickResult result;
            do { Stop.Token.ThrowIfCancellationRequested(); result = await Controller.TickAfterLootAsync(Context, state); }
            while (state.Active);
            return result;
        }
        public void Dispose() => Stop.Dispose();
    }

    public static async Task GreenArmorFallbackAsync()
    {
        foreach (var configuredCount in new[] { 0, 10, 13 })
        {
            using var run = new Run(configuredCount + 6);
            run.Game.Api.InventoryItems = run.Game.Api.InventoryItems.Select((item, index) => index < configuredCount
                ? item : item with
                {
                    Name = ((index - configuredCount) % 3) switch { 0 => "fallback armor", 1 => "绿色腰带", _ => "绿色戒指" },
                    ItemType = (index - configuredCount) % 3 == 0 ? 7U : 8U,
                    QualityRank = 2
                }).ToArray();
            var protectedItems = new[]
            {
                new InventoryItemSnapshot(1, 300, "keep armor", 1, 30, false, 7, 2),
                new InventoryItemSnapshot(1, 301, "weapon", 1, 31, false, 1, 2),
                new InventoryItemSnapshot(1, 302, "accessory", 1, 32, false, 8, 2),
                new InventoryItemSnapshot(1, 303, "blue armor", 1, 33, false, 7, 3),
                new InventoryItemSnapshot(1, 304, "equipped armor", 1, 34, true, 7, 2),
                new InventoryItemSnapshot(1, 305, "green named unknown armor", 1, 35, false, 0, 0),
                new InventoryItemSnapshot(1, 306, "绿色项链", 1, 36, false, 8, 2),
                new InventoryItemSnapshot(1, 307, "绿色耳环", 1, 37, false, 8, 2),
                new InventoryItemSnapshot(1, 308, "keep 腰带", 1, 38, false, 8, 2),
                new InventoryItemSnapshot(1, 309, "已穿戴戒指", 1, 39, true, 8, 2),
                new InventoryItemSnapshot(1, 310, "蓝色腰带", 1, 40, false, 8, 3),
                new InventoryItemSnapshot(1, 311, "蓝色戒指", 1, 41, false, 8, 3),
                new InventoryItemSnapshot(1, 312, "图案:绿色腰带", 1, 42, false, 27, 2)
            };
            run.Game.Settings.BagCleanupExcludedItemNames.Add("keep");
            run.Game.Settings.BagCleanupSellItemNameKeywords.Add("fallback armor");
            run.Game.Api.InventoryItems = run.Game.Api.InventoryItems.Concat(protectedItems).ToArray();
            Check(BagCleanupItemMatcher.SelectDiscardItems(run.Game.Api.InventoryItems, run.Game.Settings).Count == configuredCount,
                "manual discard keeps configured eligibility only");
            var result = await run.Finish(new BagCleanupState());
            var expected = Math.Min(13, configuredCount + 6);
            Check(run.Game.Removed.SequenceEqual(Enumerable.Range(100, expected).Select(id => (ulong)id)),
                "configured discard first, then green armor, belts and rings up to the same thirteen-entry limit: " + configuredCount);
            Check(!run.Game.Open && protectedItems.All(item => run.Game.Api.InventoryItems.Any(left => left.InstanceId == item.InstanceId)),
                "fallback closes bag and preserves whitelist, weapons, accessories, equipped items and unknown quality/type");
        }
    }

    public static async Task LimitAndPriorityAsync()
    {
        foreach (var count in new[] { 12, 13, 20 })
        {
            using var run = new Run(count);
            if (count == 20) run.Game.Api.InventoryItems = run.Game.Api.InventoryItems.Select((item, index) => item with
            { ItemType = index >= 13 && index <= 18 ? new uint[] { 31, 9, 27 }[(index - 13) % 3] : 7, Count = index == 13 ? 999U : 1U }).ToArray();
            var state = new BagCleanupState();
            var result = await run.Finish(state);
            Check(run.Game.Removed.Count == Math.Min(13, count) && !run.Game.Open && !run.Game.LeftHeld && !state.Active,
                "automatic batch stops at 13 confirmed inventory entries and releases the bag: " + count);
            Check(result.Status == BagCleanupTickStatus.Skipped && result.Reason == (count < 13 ? "discard_completed_capacity_recovered" : "discard_limit_reached"),
                "reaching limit yields locally, including exact exhaustion; never signals a town return");
            if (count == 20) Check(run.Game.Removed.SequenceEqual(new ulong[] { 113, 116, 114, 117, 115, 118, 100, 101, 102, 103, 104, 105, 106 }),
                "actual UI discards prioritized items first and an entire 999-item stack counts once");
            if (count >= 13)
            {
                Check(run.Context.CleanupRequests.BackpackRetryAfter > DateTimeOffset.UtcNow.AddSeconds(25), "limited local batch leaves time for grinding");
                var keys = run.Game.Input.Keys.Count;
                Check((await run.Controller.TickAfterLootAsync(run.Context, state)).Reason == "discard_batch_cooldown" && run.Game.Input.Keys.Count == keys,
                    "the next legacy post-loot tick cannot immediately open another discard batch");
                Check(run.Context.CleanupRequests.Request(run.Context.Config.ScriptSettings!, true).Success, "manual cleanup is not blocked by the automatic batch cooldown");
            }
        }
    }

    public static async Task RetryPreservesLimitAsync()
    {
        foreach (var failure in new[] { "hover", "close", "confirm" })
        {
            using var run = new Run(20);
            var injected = false;
            var closeBlocked = failure == "close";
            var after = run.Game.Input.AfterMouseUp;
            run.Game.Input.AfterMouseUp = button =>
            {
                after!(button);
                if (!injected && failure == "hover" && run.Game.Removed.Count == 12) { injected = true; run.Game.WrongHover = true; }
                if (!injected && failure == "confirm" && run.Game.Removed.Count == 13)
                {
                    injected = true;
                    throw new IOException("input reported failure after successful discard confirmation");
                }
            };
            var press = run.Game.Input.AfterPress;
            run.Game.Input.AfterPress = key =>
            {
                if (closeBlocked && key == "I" && run.Game.Open && run.Game.Removed.Count == 13) return;
                press!(key);
            };
            var state = new BagCleanupState();
            do { run.Stop.Token.ThrowIfCancellationRequested(); await run.Controller.TickAfterLootAsync(run.Context, state); }
            while (state.Step != BagCleanupStep.WaitDiscardRetry);
            Check(state.DiscardedItemCount == (failure == "hover" ? 12 : 13) && state.DiscardItemLimit == 13,
                "a failed item or failed close preserves both the confirmed count and limit");
            run.Game.WrongHover = closeBlocked = false;
            await Task.Delay(2100, run.Stop.Token);
            await run.Finish(state);
            Check(run.Game.Removed.Count == 13 && run.Game.Removed.Distinct().Count() == 13 && !run.Game.Open,
                "retry finishes the same batch without discarding item 14");
        }
    }

    public static async Task LateCandidatesRespectLimitAsync()
    {
        using var run = new Run(12);
        var added = false;
        var press = run.Game.Input.AfterPress;
        run.Game.Input.AfterPress = key =>
        {
            press!(key);
            if (!added && key == "I" && !run.Game.Open && run.Game.Removed.Count == 12)
            {
                added = true;
                run.Game.Api.InventoryItems = Enumerable.Range(0, 3).Select(i => new InventoryItemSnapshot(1, (uint)(200 + i), "item late", 1, i, false, 31)).ToArray();
            }
        };
        var count = await run.Controller.RunDiscardRequestedAsync(run.Context, _ => { }, 13);
        Check(added && count == 13 && run.Game.Removed.Count == 13 && run.Game.Api.InventoryItems.Count == 2 && !run.Game.Open,
            "late candidates after closing continue the same budget and returned count includes the first 12");
    }

    private sealed class UnusedJournal : IAuctionListingJournal
    {
        public Task<AuctionListingHistory> LoadAsync(string account, string character, CancellationToken token) => throw new Exception("must stay local");
        public Task SaveAsync(string account, string character, AuctionListingHistory history, CancellationToken token) => throw new Exception("must stay local");
    }
    private sealed class UnusedSuccess : IGroceryShopSuccessStore
    {
        public Task<DateTimeOffset?> LoadAsync(string accountId, CancellationToken token = default) => Task.FromResult<DateTimeOffset?>(null);
        public Task RecordAsync(string accountId, DateTimeOffset time, CancellationToken token = default) => throw new Exception("limit is not sellout");
    }
    private static CleanupWorkflowRunner Runner(Run run) => new(run.Game.Input,
        new InMemorySharedPathStore(new SharedPathDocument { Name = "stall", MapId = 2, Points = new() { new() { X = 0 }, new() { X = 20 } } }),
        (_, _, _) => throw new Exception("discard limit must finish without any travel"), new UnusedJournal(), grocerySuccessStore: new UnusedSuccess());

    public static async Task WorkflowLimitAsync()
    {
        foreach (var mode in new[] { "normal", "grocery_npc", "grocery_discard" })
        foreach (var fallback in new[] { false, true })
        {
            using var run = new Run(20);
            if (fallback) run.Game.Api.InventoryItems = run.Game.Api.InventoryItems.Select((item, index) => index < 10
                ? item : item with { Name = "fallback armor", ItemType = 7, QualityRank = 2 }).ToArray();
            var settings = run.Context.Config.ScriptSettings!;
            if (mode != "normal") settings.Maintenance.CleanupWorkflow.Mode = CleanupMode.GroceryShop;
            settings.Maintenance.CleanupWorkflow.GroceryPostSaleAction = mode == "grocery_npc" ? GroceryPostSaleAction.NpcSell : GroceryPostSaleAction.Discard;
            settings.Paths.GroceryStallPathName = "stall";
            settings.Paths.GroceryReturnItemName = settings.Paths.BagCleanupReturnItemName = "item protected scroll";
            run.Game.Api.InventoryItems = run.Game.Api.InventoryItems.Append(new InventoryItemSnapshot(1, 999, "item protected scroll", 2, 20, false)).ToArray();
            run.Game.Api.InventoryCapacity = 21;
            var request = new CleanupRequest(settings, false) { GroceryTrigger = mode == "normal" ? GroceryShopTrigger.None : GroceryShopTrigger.Backpack };
            await Runner(run).RunAsync(run.Context, request);
            Check(run.Game.Removed.Count == 13 && run.Game.Api.InventoryItems.Any(i => i.InstanceId == 999) && !run.Game.Open && !request.RequestsRestart,
                "normal and both grocery workflows stop locally after 13, preserve return scroll, and do not restart: " + mode);
            if (fallback) Check(run.Game.Removed.SequenceEqual(Enumerable.Range(100, 13).Select(id => (ulong)id)),
                "all automatic workflows share the ten configured plus three green armor budget");
        }
    }

    public static async Task WorkerResumesGrindingAsync()
    {
        using var run = new Run(20);
        var paths = new InMemorySharedPathStore();
        var semi = new SemiAutoCombatController(run.Game.Input);
        var worker = new DefaultAccountWorkerLoop(run.Game.Input, semi, new StationaryCombatController(run.Game.Input, semi, paths), cleanupWorkflow: Runner(run));
        var work = worker.RunAsync(run.Context);
        try
        {
            while (!run.Logger.Entries.Any(e => e.EventName == "bag_cleanup.discard.complete"))
            {
                if (work.IsCompleted) { await work; throw new Exception("worker ended before limited discard completed"); }
                await Task.Delay(10, run.Stop.Token);
            }
            var reads = run.Game.Api.PlayerReadCount;
            await Task.Delay(3000, run.Stop.Token);
            Check(!work.IsCompleted && run.Game.Api.PlayerReadCount > reads && run.Context.CleanupRequests.Current == null &&
                run.Game.Removed.Count == 13 && !run.Game.Open && run.Context.CleanupRequests.StandaloneShopRestartRequestId == null,
                "worker resumes normal ticks without immediately discarding another batch or restarting");
        }
        finally
        {
            run.Stop.Cancel();
            try { await work.WaitAsync(TimeSpan.FromSeconds(3)); } catch (OperationCanceledException) { }
        }
    }
}
