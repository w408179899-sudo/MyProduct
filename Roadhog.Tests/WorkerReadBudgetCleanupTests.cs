using Roadhog.Application;
using Roadhog.Application.SemiAuto;
using Roadhog.Application.StationaryCombat;
using Roadhog.Application.Trading;
using Roadhog.Application.Workers;
using Roadhog.Core.Accounts;
using Roadhog.Core.Api;
using Roadhog.Core.Common;
using Roadhog.Core.Diagnostics;
using Roadhog.Core.Model;
using Roadhog.Core.Paths;

internal static partial class CleanupWorkflowTests
{
    public static async Task WorkerReadBudgetDoesNotReplayCleanupAsync()
    {
        await CleanupReadRecoveryAsync(false);
        await CleanupReadRecoveryAsync(true);
        var logger = new InMemoryRoadhogLogger();
        var states = new AccountRuntimeManager(logger);
        var loop = new FailedCleanupLoop();
        var host = new AccountWorkerHost(new FakeGameApi(), logger, states, loop,
            new() { RecoveryDelay = TimeSpan.FromMilliseconds(1) });
        try
        {
            Require(host.Start(new() { AccountName = "recovering-cleanup", ScriptSettings = new() }, cleanupFirst: true).Success, "start cleanup recovery test");
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            while (loop.Calls < 2) await Task.Delay(10, deadline.Token);
            Require(host.IsRunning && loop.Calls == 2 && loop.PendingOnRecovery == false && host.CleanupFailure == null &&
                !logger.Entries.Any(e => e.EventName is "cleanup_workflow.terminated" or "account.failed"),
                "escaped cleanup exception recovers the host without retaining or replaying its request");
        }
        finally { await host.StopAsync(); }
    }

    private sealed class FailedCleanupLoop : IAccountWorkerLoop
    {
        public int Calls;
        public bool PendingOnRecovery;
        public async Task RunAsync(AccountWorkerContext context)
        {
            if (++Calls == 1) throw new CleanupExecutionFailedException("unconfirmed sale", new TimeoutException());
            PendingOnRecovery = context.CleanupRequests.Current != null;
            await Task.Delay(Timeout.Infinite, context.StopToken);
        }
    }

    private static async Task CleanupReadRecoveryAsync(bool timesOut)
    {
        var api = new FakeGameApi { TargetEntityId = 0, InventoryCapacity = 81,
            InventoryItems = new[] { new InventoryItemSnapshot(167000001, 1, "魔石:攻击力+5", 1, 0, false, QualityRank: 2) } };
        var input = new RecordingKeyboardInput();
        var logger = new InMemoryRoadhogLogger();
        var settings = new ScriptSettings { MainMode = AccountMainMode.SemiAuto };
        settings.Paths.MaintenancePathName = "merchant";
        var rules = BagCleanupRuleCatalog.CreateDefaultRules();
        var sell = rules.Single(r => r.Key == BagCleanupRuleCatalog.GreenManastone);
        sell.Enabled = true; sell.Action = BagCleanupAction.Sell;
        settings.Maintenance.BagCleanupRules = rules;
        var paths = ManualCleanupPaths(settings, api, input, new SharedPathDocument
            { Name = "merchant", CleanupNpcName = "merchant", Points = new() { new() { X = 50 } } });
        var calls = 0;
        var readRecovered = false;
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var runner = new CleanupWorkflowRunner(input, paths, async (context, _, _) =>
        {
            calls++;
            if (timesOut) throw new SnapshotReadBudgetExceededException("npc_trade");
            var watch = System.Diagnostics.Stopwatch.StartNew();
            api.NpcTradeRead = () => watch.Elapsed < TimeSpan.FromMilliseconds(2300)
                ? throw new IOException("transient NPC UI")
                : new(false, new Dictionary<string, GameUiPoint>(), false, -1, 0, false, Array.Empty<NpcTradeItem>(), null, false);
            await context.Snapshots.ReadNpcTradeAsync();
            readRecovered = true;
            stop.Cancel();
            return OperationResult.Ok();
        }, new Journal());
        var context = new AccountWorkerContext(new() { AccountName = "budget-cleanup", MainMode = settings.MainMode, ScriptSettings = settings },
            api, logger, new AccountRuntimeManager(logger), new() { TickInterval = TimeSpan.FromMilliseconds(10) }, stop.Token);
        context.CleanupRequests.Request(settings, true);
        var request = context.CleanupRequests.Current;
        var semi = new SemiAutoCombatController(input);
        var worker = new DefaultAccountWorkerLoop(input, semi, new StationaryCombatController(input, semi), cleanupWorkflow: runner);
        var run = worker.RunAsync(context);
        try
        {
            if (timesOut)
            {
                while (!logger.Entries.Any(e => e.EventName == "cleanup_workflow.failed_continuing"))
                { if (run.IsCompleted) await run; await Task.Delay(10, stop.Token); }
                var reads = api.PlayerReadCount;
                while (api.PlayerReadCount < reads + 3)
                { if (run.IsCompleted) await run; await Task.Delay(10, stop.Token); }
                Require(!run.IsCompleted && context.CleanupRequests.Current == null && request!.Failure != null,
                    "timed-out cleanup is abandoned and the same worker resumes normal processing");
            }
            else
            {
                try { await run; throw new Exception("expected explicit cancellation"); }
                catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
                Require(ReferenceEquals(request, context.CleanupRequests.Current), "transient read resumes the original operation");
            }
            Require(calls == 1 && readRecovered == !timesOut, "failed cleanup cannot replay the path/trade");
            Require(logger.Entries.Any(e => e.EventName == "cleanup_workflow.operation_timeout") == timesOut &&
                !logger.Entries.Any(e => e.EventName is "cleanup_workflow.complete" or "cleanup_workflow.terminated") &&
                logger.Entries.Count(e => e.EventName == "worker.loop.enter") == 1,
                "timeout is neither completion, worker stop nor worker restart");
        }
        catch (Exception ex)
        {
            throw new Exception("calls=" + calls + "; failure=" + context.CleanupRequests.Current?.Failure + "; events=" +
                string.Join(" | ", logger.Entries.TakeLast(15).Select(e => e.EventName + ":" + string.Join(",", e.Fields.Select(f => f.Key + "=" + f.Value)))), ex);
        }
        finally
        {
            stop.Cancel();
            try { await run.WaitAsync(TimeSpan.FromSeconds(3)); }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        }
    }
}
