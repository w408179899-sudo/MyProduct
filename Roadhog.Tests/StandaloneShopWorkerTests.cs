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
            while (context.CleanupRequests.Current?.Failure == null)
            { if (work.IsCompleted) await work; await Task.Delay(5, stop.Token); }
            var keys = game.Input.Keys.Count;
            await Task.Delay(150, stop.Token);
            Require(context.CleanupRequests.Current is { StandaloneShop: true, Failure: not null } && !work.IsCompleted,
                "failed request remains held in the same worker until explicit stop");
            Require(context.CleanupRequests.StandaloneShopRestartRequestId == null, "failure never authorizes automatic Stop/Start");
            Require(game.Input.Keys.Count == keys && !game.Input.Keys.Any(k => k is "F6" or "F5"), "failure does not resume combat or recall");
            Require(logger.Entries.Count(e => e.EventName == "standalone_shop.failed") == 1 &&
                !logger.Entries.Any(e => e.EventName == "cleanup_workflow.failed_continuing"), "failure is reported once without replay or ordinary cleanup fallback");
        }
        finally { stop.Cancel(); try { await work; } catch (OperationCanceledException) when (stop.IsCancellationRequested) { } }
        Require(work.IsCompleted, "explicit stop terminates held task");
    }
}
