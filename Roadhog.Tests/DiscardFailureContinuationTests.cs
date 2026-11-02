using Roadhog.Application;
using Roadhog.Application.SemiAuto;
using Roadhog.Application.StationaryCombat;
using Roadhog.Application.Trading;
using Roadhog.Application.Workers;
using Roadhog.Core.Accounts;
using Roadhog.Core.Diagnostics;
using Roadhog.Core.Paths;

internal static partial class CleanupWorkflowTests
{
    // Replay script 3: backpack-triggered grocery preparation, full inventory,
    // UI preparation/hover failure with successful cancellation and bag closure.
    public static async Task BackpackDiscardFailureRetriesInSameHostAsync()
    {
        foreach (var failure in new[] { "bag_not_open", "hover_changed" })
        {
            var game = new InventoryDiscardTests.Simulation(1) { WrongHover = failure == "hover_changed" };
            game.Api.TargetEntityId = 0;
            game.Api.InventoryCapacity = 1;
            game.Settings.BagCleanupEnabled = true;
            game.Settings.BagCleanupThreshold = 2;
            game.Settings.CleanupWorkflow = new()
            {
                Mode = CleanupMode.GroceryShop, GroceryPostSaleAction = GroceryPostSaleAction.Discard,
                GroceryScheduleEnabled = false, NpcCleanup = true
            };
            if (failure == "bag_not_open")
            {
                var press = game.Input.AfterPress;
                game.Input.AfterPress = key => { if (key != "I") press?.Invoke(key); };
            }
            var config = new AccountConfig
            {
                AccountName = "discard-failure-" + failure, MainMode = AccountMainMode.SemiAuto,
                ScriptSettings = new() { MainMode = AccountMainMode.SemiAuto, Maintenance = game.Settings,
                    Paths = new() { GroceryStallPathName = "nearby-unused" } }
            };
            var logger = new InMemoryRoadhogLogger();
            var states = new AccountRuntimeManager(logger);
            // Local-discard retry belongs to the retained Discard mode. Its departure
            // guard needs a reachable route even though this test never leaves the bag.
            var position = game.Api.Player.Position!.Value;
            var paths = new InMemorySharedPathStore(new SharedPathDocument
            {
                Name = "nearby-unused", MapId = game.Api.Channel.MapId,
                Points = new() { new() { X = position.X, Y = position.Y, Z = position.Z } }
            });
            var semi = new SemiAutoCombatController(game.Input);
            var runner = new CleanupWorkflowRunner(game.Input, paths,
                (_, _, _) => throw new Exception("abandoned local discard cannot depart for trading"),
                new Journal(), grocerySuccessStore: new NoGrocerySale());
            var loop = new DefaultAccountWorkerLoop(game.Input, semi,
                new StationaryCombatController(game.Input, semi, paths), cleanupWorkflow: runner);
            var host = new AccountWorkerHost(game.Api, logger, states, loop,
                new() { TickInterval = TimeSpan.FromMilliseconds(5) });
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            async Task Until(Func<bool> ready)
            {
                while (!ready())
                {
                    Require(host.IsRunning, "discard failure must not stop the account: " + host.CleanupFailure);
                    await Task.Delay(10, deadline.Token);
                }
            }
            try
            {
                Require(host.Start(config).Success, "start real worker host");
                await Until(() => logger.Entries.Any(e => e.EventName == "bag_cleanup.discard.failed"));
                var reads = game.Api.PlayerReadCount;
                var bagKeys = game.Input.Keys.Count(k => k == "I");
                await Until(() => game.Api.PlayerReadCount >= reads + 3);
                await Task.Delay(200, deadline.Token);
                Require(host.IsRunning && host.CleanupFailure == null && host.StandaloneShopRestartRequestId == null,
                    "local discard failure keeps the same host running, without failure or restart intent");
                Require(game.Removed.Count == 0 && game.Api.InventoryItems.Count == 1 && !game.Open && !game.LeftHeld,
                    "failed item is retained and input/bag cleanup finishes");
                var failed = logger.Entries.Single(e => e.EventName == "bag_cleanup.discard.failed");
                Require(Equals(failed.Fields["resumePolicy"], "retry_remaining_items"),
                    "backpack-triggered grocery retains the unfinished batch");
                Require(game.Input.Keys.Count(k => k == "I") == bagKeys && logger.Entries.Count(e => e.EventName == "worker.loop.enter") == 1,
                    "retry backoff cannot immediately reopen inventory or restart the worker");
                Require(!logger.Entries.Any(e => e.EventName is "account.failed" or "cleanup_workflow.terminated" or
                    "cleanup_workflow.preparation_pending" or "grocery_shop.failed" or "cleanup_workflow.complete"),
                    "pending retry is neither completion nor account failure");
                game.WrongHover = false;
                if (failure == "bag_not_open")
                    game.Input.AfterPress = key => { if (key == "I") game.Open = !game.Open; };
                await Until(() => game.Removed.Count == 1 && !game.Open);
                Require(logger.Entries.Any(e => e.EventName == "bag_cleanup.discard.retry") &&
                    !logger.Entries.Any(e => e.EventName == "cleanup_workflow.discard_abandoned"),
                    "recovery continues the existing batch without abandoning it");
            }
            finally { await host.StopAsync(); }
        }
    }

    private sealed class NoGrocerySale : IGroceryShopSuccessStore
    {
        public Task<DateTimeOffset?> LoadAsync(string accountId, CancellationToken token = default) =>
            Task.FromResult<DateTimeOffset?>(null);
        public Task RecordAsync(string accountId, DateTimeOffset soldOutAt, CancellationToken token = default) =>
            throw new Exception("a failed local discard must not fabricate a sale");
    }
}
