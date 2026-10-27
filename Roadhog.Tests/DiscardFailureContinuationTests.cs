using Roadhog.Application;
using Roadhog.Application.SemiAuto;
using Roadhog.Application.StationaryCombat;
using Roadhog.Application.Trading;
using Roadhog.Application.Workers;
using Roadhog.Core.Accounts;
using Roadhog.Core.Diagnostics;

internal static partial class CleanupWorkflowTests
{
    // Replay script 3: backpack-triggered grocery preparation, full inventory,
    // UI preparation/hover failure with successful cancellation and bag closure.
    public static async Task BackpackDiscardFailureKeepsHostRunningAsync()
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
                Mode = CleanupMode.GroceryShop, GroceryPostSaleAction = GroceryPostSaleAction.NpcSell,
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
                ScriptSettings = new() { MainMode = AccountMainMode.SemiAuto, Maintenance = game.Settings }
            };
            var logger = new InMemoryRoadhogLogger();
            var states = new AccountRuntimeManager(logger);
            var paths = new InMemorySharedPathStore();
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
                await Until(() => logger.Entries.Any(e => e.EventName == "cleanup_workflow.discard_abandoned"));
                var reads = game.Api.PlayerReadCount;
                var bagKeys = game.Input.Keys.Count(k => k == "I");
                await Until(() => game.Api.PlayerReadCount >= reads + 3);
                await Task.Delay(200, deadline.Token);
                Require(host.IsRunning && host.CleanupFailure == null && host.StandaloneShopRestartRequestId == null,
                    "local discard failure keeps the same host running, without failure or restart intent");
                Require(game.Removed.Count == 0 && game.Api.InventoryItems.Count == 1 && !game.Open && !game.LeftHeld,
                    "failed item is retained and input/bag cleanup finishes");
                var abandoned = logger.Entries.Single(e => e.EventName == "cleanup_workflow.discard_abandoned");
                Require(Equals(abandoned.Fields["groceryTrigger"], "Backpack") && Equals(abandoned.Fields["automaticRetryAfterMs"], 30000),
                    "backpack-triggered grocery failure gets a bounded retry interval instead of three fatal retries");
                Require(game.Input.Keys.Count(k => k == "I") == bagKeys && logger.Entries.Count(e => e.EventName == "worker.loop.enter") == 1,
                    "a still-full bag cannot immediately reopen inventory or restart the worker");
                Require(!logger.Entries.Any(e => e.EventName is "account.failed" or "cleanup_workflow.terminated" or
                    "cleanup_workflow.preparation_pending" or "grocery_shop.failed" or "cleanup_workflow.complete"),
                    "abandonment is neither a success nor an account failure/retry loop");
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
