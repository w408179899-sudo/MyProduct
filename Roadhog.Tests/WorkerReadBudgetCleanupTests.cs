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
        var loop = new TerminalCleanupLoop();
        var host = new AccountWorkerHost(new FakeGameApi(), logger, states, loop,
            new() { RecoveryDelay = TimeSpan.FromMilliseconds(1) });
        try
        {
            Require(host.Start(new() { AccountName = "terminal-cleanup" }).Success, "start terminal cleanup test");
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            while (host.IsRunning) await Task.Delay(10, deadline.Token);
            Require(loop.Calls == 1 && host.CleanupFailure != null && logger.Entries.Any(e => e.EventName == "cleanup_workflow.terminated") &&
                !logger.Entries.Any(e => e.EventName == "worker.loop.exception"),
                "terminal cleanup ends the host without generic recovery replay");
        }
        finally { await host.StopAsync(); }
    }

    private sealed class TerminalCleanupLoop : IAccountWorkerLoop
    {
        public int Calls;
        public Task RunAsync(AccountWorkerContext context)
        {
            Calls++;
            throw new CleanupExecutionFailedException("unconfirmed sale", new TimeoutException());
        }
    }

    private static async Task CleanupReadRecoveryAsync(bool terminal)
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
            if (terminal) throw new SnapshotReadBudgetExceededException("npc_trade");
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
            try
            {
                await run;
                throw new Exception("expected cleanup cancellation or termination");
            }
            catch (CleanupExecutionFailedException) when (terminal) { }
            catch (OperationCanceledException) when (!terminal && stop.IsCancellationRequested) { }
            Require(calls == 1 && ReferenceEquals(request, context.CleanupRequests.Current) && (request!.Failure != null) == terminal,
                "cleanup resumes the same read or terminates explicitly; never replays the path/trade");
            Require(readRecovered == !terminal, "a transient read longer than the combat budget resumes the original operation");
            Require(logger.Entries.Any(e => e.EventName == "cleanup_workflow.operation_timeout") == terminal &&
                !logger.Entries.Any(e => e.EventName is "cleanup_workflow.failed_continuing" or "cleanup_workflow.complete"),
                "an unconfirmed operation cannot become completion or a replayable cleanup failure");
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
            catch (CleanupExecutionFailedException) when (terminal) { }
        }
    }
}
