using Roadhog.Application.BagCleanup;
using Roadhog.Application.SemiAuto;
using Roadhog.Application.StationaryCombat;
using Roadhog.Application.Trading;
using Roadhog.Application.Workers;
using Roadhog.Core.Accounts;
using Roadhog.Core.Common;
using Roadhog.Core.Model;
using Roadhog.Core.Paths;

internal static partial class ArrowRestockTests
{
    private sealed class UnusedJournal : IAuctionListingJournal
    {
        public Task<AuctionListingHistory> LoadAsync(string account, string character, CancellationToken token) => throw new Exception("supply must not auction");
        public Task SaveAsync(string account, string character, AuctionListingHistory history, CancellationToken token) => throw new Exception("supply must not auction");
    }

    private static SharedPathDocument SetupRoute(Fixture f)
    {
        f.Api.Channel = f.Api.Channel with { MapId = 2 };
        f.Api.Player = f.Api.Player with { Position = new(101, 0, 0) };
        var settings = f.Context.Config.ScriptSettings!;
        settings.Paths.MaintenancePathName = settings.Paths.RevivePathName = "supplies";
        settings.Paths.BagCleanupReturnByReversePath = true;
        settings.Paths.BagCleanupReturnItemName = "absent scroll";
        settings.Paths.BagCleanupTownReturnKey = settings.Paths.TownReturnKey = "";
        return new() { Name = "supplies", MapId = 2, CleanupNpcName = "merchant",
            Points = new() { new() { X = 0 }, new() { X = 100 }, new() { X = 110 } } };
    }

    public static async Task PostSaleJumpAsync()
    {
        foreach (var scenario in new[] { "success", "enough", "other_class", "sale_failed" })
        {
            using var f = new Fixture(scenario == "enough" ? 50000U : 24000U);
            if (scenario == "other_class") f.Api.Player = f.Api.Player with { CharacterClassId = AionClassId.Cleric };
            var route = SetupRoute(f);
            var state = new BagCleanupState(); state.Start(0, 0); state.SetPath(route);
            state.PrepareReturnAfterSuccess();
            if (scenario == "sale_failed") state.PrepareReturnAfterFailure("sale failed", "test");
            f.Ui = f.Ui with { IsOpen = true, Mode = 1 };
            var controller = new BagCleanupController(f.Input, new InMemorySharedPathStore(route), (_, _, _) => Task.FromResult(OperationResult.Ok()));
            await controller.TickAfterLootAsync(f.Context, state);
            Check(f.Input.Keys.SequenceEqual(new[] { "Space" }) && !f.Ui.IsOpen,
                "sale completion jumps and closes shop before deciding to reopen: " + scenario);
            Check(state.Step == (scenario == "success" ? BagCleanupStep.BuyArrows : BagCleanupStep.ReturnByReversePath),
                "only successful ranger cleanup below 50000 reopens merchant: " + scenario);
            if (scenario != "success") continue;
            await controller.TickAfterLootAsync(f.Context, state);
            Check(f.BuyClicks == 1 && ArrowRestockPolicy.Count(f.Api.InventoryItems) == 54000 &&
                state.Step == BagCleanupStep.ReturnByReversePath && f.Input.Keys.First() == "Space",
                "post-sale restock buys three stacks then resumes existing return path");
        }
    }

    public static async Task SupplyWorkflowAsync()
    {
        foreach (var count in new uint[] { 24000, 50000 })
        {
            using var f = new Fixture(count);
            var route = SetupRoute(f);
            var settings = f.Context.Config.ScriptSettings!;
            settings.Maintenance.CleanupWorkflow.Mode = CleanupMode.GroceryShop;
            settings.Maintenance.CleanupWorkflow.Auction = settings.Maintenance.CleanupWorkflow.TransferGold = true;
            settings.Maintenance.BagCleanupSellItemNameKeywords.Add("protected");
            settings.Maintenance.BagCleanupDiscardItemNameKeywords.Add("protected");
            var paths = new List<string>(); var returned = 0;
            var runner = new CleanupWorkflowRunner(f.Input, new InMemorySharedPathStore(route), (_, name, points) =>
            {
                if (paths.Count == 0) Check(points.SequenceEqual(new[] { new Vector3Snapshot(100, 0, 0), new(110, 0, 0) }),
                    "supply uses same-map nearby suffix");
                paths.Add(name); f.Api.Player = f.Api.Player with { Position = points[^1] };
                return Task.FromResult(OperationResult.Ok());
            }, new UnusedJournal());
            Check(f.Context.CleanupRequests.Request(settings, false, arrowRestock: true).Success, "supply queued");
            var request = f.Context.CleanupRequests.Current!;
            await runner.RunAsync(f.Context, request, _ => { returned++; return Task.CompletedTask; });
            Check(!request.RequestsRestart && !f.Input.Keys.Contains("I") && f.Api.InventoryItems.Single(i => i.InstanceId == 12).Count == 3,
                "supply does not discard, sell, open backpack, use scroll or restart script");
            Check(count == 24000 ? f.BuyClicks == 1 && paths.Count == 2 && returned == 1 && ArrowRestockPolicy.Count(f.Api.InventoryItems) == 54000
                : f.Input.Keys.Count == 0 && paths.Count == 0 && returned == 0, "completed purchase returns; already replenished request cancels without travel");
        }
    }

    public static async Task WorkerThresholdAndFailureAsync()
    {
        using var f = new Fixture(10000) { Scenario = "partial_purchase" };
        var route = SetupRoute(f);
        var config = f.Context.Config; var settings = config.ScriptSettings!;
        config.MainMode = settings.MainMode = AccountMainMode.SemiAuto;
        settings.Maintenance.BagCleanupEnabled = false;
        settings.Maintenance.CleanupWorkflow.Mode = CleanupMode.GroceryShop;
        settings.Maintenance.CleanupWorkflow.GroceryScheduleEnabled = false;
        f.Api.InventoryCapacity = 100;
        var paths = new InMemorySharedPathStore(route);
        var runner = new CleanupWorkflowRunner(f.Input, paths, (_, _, points) =>
        {
            f.Api.Player = f.Api.Player with { Position = points[^1] };
            return Task.FromResult(OperationResult.Ok());
        }, new UnusedJournal());
        var semi = new SemiAutoCombatController(f.Input);
        var loop = new DefaultAccountWorkerLoop(f.Input, semi, new StationaryCombatController(f.Input, semi, paths), cleanupWorkflow: runner);
        var task = loop.RunAsync(f.Context);
        async Task Until(Func<bool> condition)
        {
            while (!condition())
            {
                if (task.IsCompleted) await task;
                await Task.Delay(10, f.Stop.Token);
            }
        }
        try
        {
            await Until(() => f.Logger.Entries.Any(e => e.EventName == "worker.loop.enter"));
            await Task.Delay(2200, f.Stop.Token);
            Check(!f.Logger.Entries.Any(e => e.EventName == "arrow_restock.requested") && f.BuyClicks == 0,
                "exactly 10000 does not queue a combat supply trip");
            f.Api.InventoryItems = f.Api.InventoryItems.Select(i => i.InstanceId == 11 ? i with { Count = 9999 } : i).ToArray();
            await Until(() => f.Logger.Entries.Any(e => e.EventName == "cleanup_workflow.failed_continuing"));
            Check(f.Context.CleanupRequests.Current == null &&
                f.Logger.Entries.Count(e => e.EventName == "arrow_restock.requested") == 1 && f.BuyClicks == 1,
                "below 10000 buys even with cleanup disabled, free backpack and grocery mode");
            Check(!task.IsCompleted, "ambiguous purchase abandons supply and resumes the main loop");
            var reads = f.Api.PlayerReadCount;
            await Task.Delay(2200, f.Stop.Token);
            Check(f.Api.PlayerReadCount > reads && f.BuyClicks == 1 &&
                f.Context.CleanupRequests.StandaloneShopRestartRequestId == null, "normal processing resumes without repeating payment or restarting");
        }
        finally
        {
            f.Stop.Cancel();
            try { await task; } catch (OperationCanceledException) when (f.Stop.IsCancellationRequested) { }
        }
    }

    public static async Task GrocerySaleThenSupplyAsync()
    {
        using var f = new Fixture(24000);
        var route = SetupRoute(f);
        var travel = 0;
        await new GroceryNpcSaleSequence(f.Input, new InMemorySharedPathStore(route), (_, _, _) =>
        {
            travel++; return Task.FromResult(OperationResult.Ok());
        }, f.Pause).RunAsync(f.Context, _ => { });
        Check(f.JunkClicks == 1 && f.SaleClicks == 1 && f.BuyClicks == 1 && f.AllClicks == 3 && travel == 1,
            "grocery cleanup sells junk, jumps, then buys three arrow stacks at the same NPC");
        var keys = f.Input.Keys.ToArray();
        var firstJump = Array.IndexOf(keys, "Space");
        Check(firstJump >= 0 && Array.IndexOf(keys, "C", firstJump) > firstJump && !f.Ui.IsOpen && !f.BagOpen,
            "reopen dialog only after sale completion jump; close both windows after restock");
        Check(ArrowRestockPolicy.Count(f.Api.InventoryItems) == 54000 && f.Api.InventoryMoney == 970003 &&
            f.Logger.Entries.Any(e => e.EventName == "grocery_shop.npc_sale.complete"),
            "sale income and purchase cost are independently confirmed before grocery completion");
    }

    public static async Task GroceryFailureContinuationAsync()
    {
        foreach (var scenario in new[] { "sale_limit", "sale_throw", "select_fail", "jump_fail", "all_close_fail", "missing_buy", "money_only" })
        {
            using var f = new Fixture(24000) { Scenario = scenario };
            var route = SetupRoute(f);
            var selectionAttempts = 0; var jumps = 0; var dialogs = 0;
            f.Input.PressResult = key =>
            {
                if (key == "F8" && ++selectionAttempts == 1 && scenario == "select_fail") return OperationResult.Fail("selection failed");
                if (key == "Space" && (++jumps == 1 && scenario == "jump_fail" || scenario == "all_close_fail")) return OperationResult.Fail("jump failed");
                return OperationResult.Ok();
            };
            var press = f.Input.AfterPress;
            f.Input.AfterPress = key =>
            {
                press!(key);
                if (key == "C" && ++dialogs == 2 && scenario == "missing_buy")
                    f.Ui = f.Ui with { DialogEntries = new Dictionary<string, GameUiPoint>() };
            };
            await new GroceryNpcSaleSequence(f.Input, new InMemorySharedPathStore(route),
                (_, _, _) => Task.FromResult(OperationResult.Ok()), f.Pause).RunAsync(f.Context, _ => { });
            Check(f.Logger.Entries.Any(e => e.EventName == "grocery_shop.stage_skipped") &&
                f.Logger.Entries.Any(e => e.EventName == "grocery_shop.npc_sale.complete") &&
                f.Ui.IsOpen == (scenario == "all_close_fail") && !f.BagOpen && !f.Shift,
                "every failed step is recorded and processing still ends: " + scenario);
            Check(f.SaleClicks == (scenario == "select_fail" ? 0 : 1) && f.BuyClicks == (scenario is "missing_buy" or "all_close_fail" ? 0 : 1),
                "failed sale never repeats; later arrow purchase is attempted once: " + scenario);
            Check(ArrowRestockPolicy.Count(f.Api.InventoryItems) == (scenario is "missing_buy" or "money_only" or "all_close_fail" ? 24000UL : 54000UL),
                "sale rejection and input failures do not block later arrow replenishment");
            if (scenario is "sale_limit" or "sale_throw" or "select_fail")
                Check(f.Api.InventoryItems.Single(i => i.InstanceId == 12).Count == 3, "unsold item is retained");
        }
    }
}
