using Roadhog.Application;
using Roadhog.Application.SemiAuto;
using Roadhog.Application.StationaryCombat;
using Roadhog.Application.Trading;
using Roadhog.Application.Workers;
using Roadhog.Core.Accounts;
using Roadhog.Core.Common;
using Roadhog.Core.Diagnostics;
using Roadhog.Core.Input;
using Roadhog.Core.Model;
using Roadhog.Core.Paths;

internal static partial class CleanupWorkflowTests
{
    public static async Task CooldownPreservesSellRulesAsync()
    {
        var game = new InventoryDiscardTests.Simulation(0);
        game.Api.TargetEntityId = 0;
        game.Api.InventoryCapacity = 2;
        var config = new AccountConfig { AccountName = "cooldown-policy", MainMode = AccountMainMode.SemiAuto, ScriptSettings = new() };
        var settings = config.ScriptSettings;
        settings.MainMode = AccountMainMode.SemiAuto;
        settings.Maintenance.BagCleanupEnabled = true;
        settings.Maintenance.BagCleanupThreshold = 2;
        settings.Maintenance.BagCleanupRules = new()
        {
            new() { Key = BagCleanupRuleCatalog.WhiteEquipment, Enabled = true, Action = BagCleanupAction.Discard },
            new() { Key = BagCleanupRuleCatalog.SkillBook, Enabled = true, Action = BagCleanupAction.Discard },
            new() { Key = BagCleanupRuleCatalog.SpellBook, Enabled = true, Action = BagCleanupAction.Sell }
        };
        var book = new InventoryItemSnapshot(20, 101, "咒语书", 1, 0, false, ItemType: 31);
        var trash = new InventoryItemSnapshot(21, 102, "trash", 1, 1, false, ItemType: 7, QualityRank: 1);
        var logger = new InMemoryRoadhogLogger();
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(12));
        var context = new AccountWorkerContext(config, game.Api, logger, new AccountRuntimeManager(logger), new() { TickInterval = TimeSpan.FromMilliseconds(1) }, stop.Token);
        var paths = ManualCleanupPaths(settings, game.Api, game.Input);
        var runner = new CleanupWorkflowRunner(game.Input, paths, (_, _, _) => throw new Exception("cooldown must not visit merchant"), new Journal());
        var semi = new SemiAutoCombatController(game.Input);
        var combat = new StationaryCombatController(game.Input, semi, paths);
        Require(context.CleanupRequests.Request(settings, true).Success, "manual town cleanup establishes full-cleanup cooldown");
        var work = new DefaultAccountWorkerLoop(game.Input, semi, combat, cleanupWorkflow: runner).RunAsync(context);
        async Task WaitForCompletions(int count)
        {
            while (logger.Entries.Count(e => e.EventName == "cleanup_workflow.complete") < count)
            {
                if (work.IsCompleted) await work;
                Require(!logger.Entries.Any(e => e.EventName == "cleanup_workflow.failed_continuing"), "cleanup must not fail");
                await Task.Delay(5, stop.Token);
            }
        }
        try
        {
            await WaitForCompletions(1);
            game.Api.InventoryItems = new[] { book, trash };
            await WaitForCompletions(2);
            Require(game.Removed.SequenceEqual(new[] { trash.InstanceId }), "cooldown may discard trash but cannot discard the overlapping sell item");
            Require(game.Api.InventoryItems.Single().InstanceId == book.InstanceId, "sell item remains for next full cleanup");
            Require(settings.Maintenance.BagCleanupRules.Any(r => r.Action == BagCleanupAction.Sell), "saved sell rules stay intact");
        }
        finally
        {
            stop.Cancel();
            try { await work; } catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        }
    }

    public static async Task DiscardThenAuctionWithoutSaleAsync()
    {
        var game = new InventoryDiscardTests.Simulation(1);
        game.Api.TargetEntityId = 0;
        var logger = new InMemoryRoadhogLogger();
        var config = new AccountConfig { AccountName = "discard-next-stage", ScriptSettings = new() };
        config.ScriptSettings.Maintenance.BagCleanupRules = game.Settings.BagCleanupRules;
        config.ScriptSettings.Maintenance.CleanupWorkflow = new() { NpcCleanup = true, Auction = true };
        config.ScriptSettings.Paths.AuctionPathName = "auction";
        var auction = Auction();
        game.Api.AuctionRead = () => auction;
        var press = game.Input.AfterPress;
        game.Input.AfterPress = key => { press?.Invoke(key); if (key == "Space") auction = auction with { IsOpen = false }; };
        var up = game.Input.AfterMouseUp;
        game.Input.AfterMouseUp = button =>
        {
            var held = game.LeftHeld;
            up?.Invoke(button);
            if (!held || button != RoadhogMouseButton.Left || game.Api.InventoryItems.Count != 0) return;
            var point = game.Api.UiCursorRead!().Position;
            if (Math.Abs(point.Y - 100) <= 1 && Math.Abs(point.X - 100) <= 1) auction = auction with { ActiveTab = 2 };
            if (Math.Abs(point.Y - 100) <= 1 && Math.Abs(point.X - 200) <= 1) auction = auction with { ActiveTab = 1 };
        };
        var paths = ManualCleanupPaths(config.ScriptSettings, game.Api, game.Input,
            new SharedPathDocument { Name = "auction", Points = new() { new() { X = 0 }, new() { X = 20 } } });
        var traveled = new List<string>();
        var runner = new CleanupWorkflowRunner(game.Input, paths, (_, name, points) =>
        {
            Require(game.Removed.Count == 1 && !game.Open, "discard and close bag before auction travel");
            traveled.Add(name + ":" + points[0].X);
            return Task.FromResult(OperationResult.Ok());
        }, new Journal());
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        var context = new AccountWorkerContext(config, game.Api, logger, new AccountRuntimeManager(logger), new(), stop.Token);
        await runner.RunAsync(context, new(config.ScriptSettings, true));
        Require(traveled.SequenceEqual(new[] { "auction:0", "auction:20", "resume:1000" }), "manual cleanup continues auction without sale, then returns to grinding");
        Require(!auction.IsOpen && logger.Entries.Any(e => e.EventName == "cleanup_workflow.complete"), "later stage completes normally");
    }

    public static async Task AuctionSkipsVanishedListingsAsync()
    {
        foreach (var disappearDuringHover in new[] { false, true })
        {
            var api = new FakeGameApi();
            var input = new RecordingKeyboardInput();
            Cursor(api, input);
            var rows = new[]
            {
                new AuctionListing(1, 21, 1, "item-a", 30, "7", new(400, 100)),
                new AuctionListing(2, 22, 1, "item-b", 30, "7", new(450, 100))
            };
            var state = Auction() with { Listings = rows };
            api.AuctionRead = () =>
            {
                if (disappearDuringHover && api.InventoryUiCursor == rows[1].Point)
                    state = state with { Listings = state.Listings.Where(l => l.ListingId != 2).ToArray() };
                return state with { HoveredListingId = state.Listings.FirstOrDefault(l => l.Point == api.InventoryUiCursor)?.ListingId ?? 0 };
            };
            api.InventoryInteractionRead = () => new(false, false, false, Array.Empty<InventoryUiItem>(), 0, 0, null, null, false);
            var journal = new Journal();
            foreach (var row in rows) journal.History.Listings.Add(new(row.ListingId, row.TemplateId, 30, DateTimeOffset.UtcNow.AddDays(-2)));
            var settings = new MaintenanceScriptSettings
            {
                BagCleanupExcludedItemNames = new() { "item" },
                BagCleanupAuctionHouseItems = new(),
                CleanupWorkflow = new() { OldListingAction = AuctionOldListingAction.Withdraw }
            };
            var down = false;
            var withdrawals = 0;
            var rightClicks = 0;
            input.AfterPress = key => { Require(key == "Space", "only close key expected"); state = state with { IsOpen = false }; };
            input.AfterMouseDown = _ => down = true;
            input.AfterMouseUp = button =>
            {
                if (!down) return;
                down = false;
                var point = api.InventoryUiCursor;
                if (button == RoadhogMouseButton.Right)
                {
                    var row = state.Listings.Single(l => l.Point == point);
                    Require(row.ListingId == 1, "vanished second row must not be clicked");
                    rightClicks++;
                    state = state with { WithdrawConfirmation = new(row.ListingId, new(500, 100)) };
                }
                else if (point == new GameUiPoint(100, 100)) state = state with { ActiveTab = 2 };
                else if (point == new GameUiPoint(200, 100)) state = state with { ActiveTab = 1 };
                else if (point == new GameUiPoint(500, 100))
                {
                    withdrawals++;
                    api.InventoryItems = new[] { new InventoryItemSnapshot(21, 101, "item-a", 1, 0, false) };
                    state = state with { WithdrawConfirmation = null, Listings = disappearDuringHover ? new[] { rows[1] } : Array.Empty<AuctionListing>() };
                }
                else throw new Exception("unexpected click " + point);
            };
            using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await new AuctionTradingSequence(input, journal, Fast).RunAsync(api.Create(new(), new InMemoryRoadhogLogger(), stop.Token), "auction", settings, _ => { }, stop.Token);
            Require(withdrawals == 1 && rightClicks == 1 && !state.IsOpen, "current-list processing skips sold entry and completes");
            Require(!journal.History.Listings.Any(l => l.ListingId == 1) && journal.History.WithdrawnTemplates.Count == 0, "withdraw all does not add legacy relisting exclusions");
        }
    }
}
