using Roadhog.Application;
using Roadhog.Application.SemiAuto;
using Roadhog.Application.StationaryCombat;
using Roadhog.Application.Trading;
using Roadhog.Application.Workers;
using Roadhog.Core.Accounts;
using Roadhog.Core.Diagnostics;

internal static partial class CleanupWorkflowTests
{
    public static async Task StandaloneShopWorkerFailureAsync()
    {
        var game = new InventoryDiscardTests.Simulation(0);
        var config = AutomaticCleanupConfig(game);
        config.ScriptSettings!.Maintenance.CleanupWorkflow.StandaloneShopDiscount = 5;
        var logger = new InMemoryRoadhogLogger();
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        var context = new AccountWorkerContext(config, game.Api, logger, new AccountRuntimeManager(logger),
            new() { TickInterval = TimeSpan.FromMilliseconds(5) }, stop.Token);
        Require(context.CleanupRequests.Request(config.ScriptSettings, true, standaloneShop: true).Success, "enqueue independent shop");
        var paths = AutomaticCleanupPaths();
        var runner = new CleanupWorkflowRunner(game.Input, paths, (_, _, _) => throw new Exception("empty shop cannot start a return path"), new Journal());
        var semi = new SemiAutoCombatController(game.Input);
        var combat = new StationaryCombatController(game.Input, semi, paths);
        var work = new DefaultAccountWorkerLoop(game.Input, semi, combat, cleanupWorkflow: runner).RunAsync(context);
        try
        {
            while (!logger.Entries.Any(e => e.EventName == "cleanup_workflow.failed_continuing"))
            { if (work.IsCompleted) await work; await Task.Delay(10, stop.Token); }
            var reads = game.Api.PlayerReadCount;
            while (game.Api.PlayerReadCount < reads + 3)
            { if (work.IsCompleted) await work; await Task.Delay(10, stop.Token); }
            await Task.Delay(150, stop.Token);
            Require(context.CleanupRequests.Current == null && !work.IsCompleted,
                "failed shop is abandoned and the main loop continues");
            Require(context.CleanupRequests.StandaloneShopRestartRequestId == null, "failure never authorizes automatic Stop/Start");
            Require(!game.Input.Keys.Any(k => k is "F6" or "F5"), "failed shop does not submit a recall");
            Require(logger.Entries.Count(e => e.EventName == "cleanup_workflow.failed_continuing") == 1 &&
                logger.Entries.Count(e => e.EventName == "worker.loop.enter") == 1, "failure returns to the same worker without replay");
        }
        finally { stop.Cancel(); try { await work; } catch (OperationCanceledException) when (stop.IsCancellationRequested) { } }
        Require(work.IsCompleted, "explicit stop still ends the task");
    }
}
