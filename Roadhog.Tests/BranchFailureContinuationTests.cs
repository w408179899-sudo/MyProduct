using Roadhog.Application;
using Roadhog.Application.SemiAuto;
using Roadhog.Application.StationaryCombat;
using Roadhog.Application.Trading;
using Roadhog.Application.Workers;
using Roadhog.Core.Accounts;
using Roadhog.Core.Common;
using Roadhog.Core.Diagnostics;
using Roadhog.Core.Model;
using Roadhog.Core.Paths;

internal static partial class CleanupWorkflowTests
{
    public static async Task CleanupBranchesReturnToMainAsync()
    {
        foreach (var failure in new[] { "recall_input", "route_deadline", "release_input" })
        {
            var api = new FakeGameApi
            {
                TargetEntityId = 0, InventoryCapacity = 1,
                InventoryItems = new[] { new InventoryItemSnapshot(167000001, 1, "魔石:攻击力+5", 1, 0, false, QualityRank: 2) }
            };
            var input = new RecordingKeyboardInput();
            var logger = new InMemoryRoadhogLogger();
            var settings = new ScriptSettings { MainMode = AccountMainMode.SemiAuto };
            settings.Maintenance.BagCleanupEnabled = true;
            settings.Maintenance.BagCleanupThreshold = 2;
            settings.Maintenance.CleanupWorkflow = new() { NpcCleanup = true };
            settings.Paths.MaintenancePathName = "merchant";
            var rule = settings.Maintenance.BagCleanupRules.Single(r => r.Key == BagCleanupRuleCatalog.GreenManastone);
            rule.Enabled = true; rule.Action = BagCleanupAction.Sell;
            var paths = ManualCleanupPaths(settings, api, input, new SharedPathDocument
                { Name = "merchant", CleanupNpcName = "merchant", Points = new() { new() { X = 50 } } });
            var press = input.AfterPress;
            input.AfterPress = key =>
            {
                if (failure == "recall_input" && key == "F6") throw new IOException("recall input failed");
                press?.Invoke(key);
            };
            var visits = 0;
            var runner = new CleanupWorkflowRunner(input, paths, (_, _, _) =>
            {
                visits++;
                if (failure == "release_input") input.AfterKeyUp = _ => throw new IOException("release also failed");
                throw new CleanupExecutionFailedException("path deadline", new TimeoutException());
            }, new Journal());
            using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(12));
            var context = new AccountWorkerContext(new() { AccountName = failure, MainMode = settings.MainMode, ScriptSettings = settings },
                api, logger, new AccountRuntimeManager(logger), new() { TickInterval = TimeSpan.FromMilliseconds(5) }, stop.Token);
            Require(context.CleanupRequests.Request(settings, true).Success, "enqueue failing cleanup");
            var semi = new SemiAutoCombatController(input);
            var loop = new DefaultAccountWorkerLoop(input, semi, new StationaryCombatController(input, semi, paths), cleanupWorkflow: runner);
            var task = loop.RunAsync(context);
            async Task Until(Func<bool> ready)
            {
                while (!ready()) { if (task.IsCompleted) await task; await Task.Delay(10, stop.Token); }
            }
            try
            {
                await Until(() => logger.Entries.Any(e => e.EventName == "cleanup_workflow.failed_continuing"));
                var reads = api.PlayerReadCount;
                await Until(() => api.PlayerReadCount >= reads + 3);
                Require(!task.IsCompleted && context.CleanupRequests.Current == null &&
                    context.CleanupRequests.AutomaticRetryAfter > DateTimeOffset.UtcNow,
                    "failed branch releases ownership and leaves automatic retry cooldown while main work continues");
                Require(visits == (failure == "recall_input" ? 0 : 1) &&
                    logger.Entries.Count(e => e.EventName == "worker.loop.enter") == 1 &&
                    !logger.Entries.Any(e => e.EventName is "account.failed" or "cleanup_workflow.terminated" or "standalone_shop.restart.requested"),
                    "branch failure never stops/restarts the worker or replays a path");
                if (failure == "release_input")
                    Require(logger.Entries.Any(e => e.EventName == "worker.branch.release_failed"), "recovery input failure is also local");
            }
            finally
            {
                input.AfterKeyUp = null;
                stop.Cancel();
                try { await task.WaitAsync(TimeSpan.FromSeconds(3)); } catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
            }
        }
    }
}
