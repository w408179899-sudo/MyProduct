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
        var runner = new CleanupWorkflowRunner(input, paths, async (context, _, _) =>
        {
            calls++;
            api.Player = api.Player with { CurrentHp = 0 };
            api.NpcTradeRead = () => throw new IOException("incomplete NPC UI");
            await context.Snapshots.ReadNpcTradeAsync();
            return OperationResult.Ok();
        }, new Journal());
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var context = new AccountWorkerContext(new() { AccountName = "budget-cleanup", MainMode = settings.MainMode, ScriptSettings = settings },
            api, logger, new AccountRuntimeManager(logger), new() { TickInterval = TimeSpan.FromMilliseconds(10) }, stop.Token);
        context.CleanupRequests.Request(settings, true);
        var request = context.CleanupRequests.Current;
        var semi = new SemiAutoCombatController(input);
        var worker = new DefaultAccountWorkerLoop(input, semi, new StationaryCombatController(input, semi), cleanupWorkflow: runner);
        var run = worker.RunAsync(context);
        try
        {
            while (!logger.Entries.Any(e => e.EventName == "player_life.death.detected"))
            {
                if (run.IsCompleted) await run;
                await Task.Delay(20, stop.Token);
            }
            Require(calls == 1 && ReferenceEquals(request, context.CleanupRequests.Current) && request!.Failure != null,
                "an interrupted cleanup must remain paused and must not replay the path/trade");
            Require(logger.Entries.Any(e => e.EventName == "worker.read_budget.yield") &&
                !logger.Entries.Any(e => e.EventName is "cleanup_workflow.failed_continuing" or "cleanup_workflow.complete"),
                "scheduling cancellation must reach the worker without becoming completion or retryable cleanup failure");
        }
        catch (Exception ex)
        {
            throw new Exception("calls=" + calls + "; failure=" + context.CleanupRequests.Current?.Failure + "; events=" +
                string.Join(" | ", logger.Entries.TakeLast(15).Select(e => e.EventName + ":" + string.Join(",", e.Fields.Select(f => f.Key + "=" + f.Value)))), ex);
        }
        finally
        {
            stop.Cancel();
            try { await run.WaitAsync(TimeSpan.FromSeconds(3)); } catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        }
    }
}
