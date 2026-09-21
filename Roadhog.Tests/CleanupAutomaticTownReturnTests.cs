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
    private static InventoryItemSnapshot SaleItem(int slot = 0) =>
        new(200, (uint)(900 + slot), "咒语书", 1, slot, false, ItemType: 31);

    private static AccountConfig AutomaticCleanupConfig(InventoryDiscardTests.Simulation game)
    {
        game.Api.TargetEntityId = 0;
        var config = new AccountConfig { AccountName = "automatic-recall", MainMode = AccountMainMode.SemiAuto, ScriptSettings = new() };
        var settings = config.ScriptSettings;
        settings.MainMode = AccountMainMode.SemiAuto;
        settings.Maintenance.BagCleanupEnabled = true;
        settings.Maintenance.BagCleanupThreshold = 2;
        settings.Maintenance.BagCleanupRules = game.Settings.BagCleanupRules;
        settings.Maintenance.BagCleanupRules.Add(new() { Key = BagCleanupRuleCatalog.SpellBook, Enabled = true, Action = BagCleanupAction.Sell });
        settings.Paths.BagCleanupTownReturnKey = "F6";
        settings.Paths.TownReturnKey = "F5";
        settings.Paths.MaintenancePathName = "merchant";
        settings.Paths.RevivePathName = "resume";
        return config;
    }

    private static InMemorySharedPathStore AutomaticCleanupPaths() => new(
        new SharedPathDocument { Name = "merchant", CleanupNpcName = "merchant", Points = new() { new() { X = 1000 } } },
        new SharedPathDocument { Name = "resume", Points = new() { new() { X = 1000 }, new() { X = 0 } } });

    public static async Task AutomaticDiscardWithSaleStaysLocalAsync()
    {
        var game = new InventoryDiscardTests.Simulation(2);
        var config = AutomaticCleanupConfig(game);
        game.Api.InventoryCapacity = 3;
        game.Api.InventoryItems = game.Api.InventoryItems.Append(SaleItem(2)).ToArray();
        config.ScriptSettings!.Maintenance.CleanupWorkflow.Auction = true;
        var logger = new InMemoryRoadhogLogger();
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var context = new AccountWorkerContext(config, game.Api, logger, new AccountRuntimeManager(logger), new(), stop.Token);
        // Missing town routes must not block local discard or send the recovered bag to town.
        var runner = new CleanupWorkflowRunner(game.Input, new InMemorySharedPathStore(),
            (_, _, _) => throw new Exception("recovered capacity cannot visit any town path"), new Journal());
        await runner.RunAsync(context, new(config.ScriptSettings, false));
        Require(game.Removed.Count == 2 && !game.Open && game.Api.InventoryItems.Single().Name == "咒语书", "discard all trash and retain sale item");
        Require(!game.Input.Keys.Any(k => k is "F6" or "F5"), "sale/auction selection does not force recall after local discard");
        Require(logger.Entries.Any(e => e.EventName == "cleanup_workflow.complete"), "local request completes and can resume grinding");
    }

    public static async Task CleanupRecallSafetyAndPositionAsync()
    {
        foreach (var mode in new[] { "automatic-empty-discard", "automatic-after-discard", "manual" })
        {
            var count = mode == "automatic-empty-discard" ? 0 : 1;
            var game = new InventoryDiscardTests.Simulation(count);
            var config = AutomaticCleanupConfig(game);
            game.Api.InventoryCapacity = count + 1;
            game.Api.InventoryItems = game.Api.InventoryItems.Append(SaleItem(count)).ToArray();
            Require(BagCleanupItemMatcher.SelectSellRegistrationItems(game.Api.InventoryItems, config.ScriptSettings!.Maintenance).Count == 1, "fixture must include an NPC sale candidate");
            var logger = new InMemoryRoadhogLogger();
            using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var recall = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var traveled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var press = game.Input.AfterPress;
            game.Input.AfterPress = key =>
            {
                if (key == "F6") recall.TrySetResult();
                else press?.Invoke(key);
            };
            if (mode == "manual")
            {
                game.Api.TargetEntityId = 10;
                game.Api.TargetCurrentHp = 100;
                game.Api.TargetIsTargetingLocalPlayer = true;
            }
            var context = new AccountWorkerContext(config, game.Api, logger, new AccountRuntimeManager(logger), new(), stop.Token);
            var runner = new CleanupWorkflowRunner(game.Input, AutomaticCleanupPaths(), (_, name, _) =>
            {
                Require(name == "merchant" && recall.Task.IsCompleted && game.Api.Player.Position!.Value.X == 1000, "merchant travel requires observed recall position");
                Require(game.Removed.Count == count && !game.Open, "discard completes before merchant path");
                traveled.TrySetResult();
                stop.Cancel();
                return Task.FromCanceled<OperationResult>(stop.Token);
            }, new Journal());
            var work = runner.RunAsync(context, new(config.ScriptSettings!, mode == "manual"));
            try
            {
                if (mode == "manual")
                {
                    while (!logger.Entries.Any(e => e.EventName == "bag_cleanup.safe_wait"))
                    { if (work.IsCompleted) await work; await Task.Delay(5, stop.Token); }
                    Require(!recall.Task.IsCompleted && game.Removed.Count == 0, "manual waits for safety before recall and before discard");
                    game.Api.TargetEntityId = 0;
                }
                await recall.Task.WaitAsync(stop.Token);
                Require(game.Removed.Count == (mode == "manual" ? 0 : count), "automatic discards before recall; manual recalls before discard");
                await Task.Delay(250, stop.Token);
                Require(!traveled.Task.IsCompleted && !work.IsCompleted, "keypress without changed position cannot start merchant path");
                game.Api.Player = game.Api.Player with { Position = new(1000, 0, 0) };
                try { await work; } catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
                Require(traveled.Task.IsCompletedSuccessfully, "confirmed recall releases merchant path");
                Require(game.Input.Keys.Count(k => k == "F6") == 1, "configured cleanup recall key is pressed once");
            }
            catch (OperationCanceledException)
            {
                throw new Exception(mode + " timed out; removed=" + game.Removed.Count + "; keys=" + string.Join(",", game.Input.Keys) +
                    "; events=" + string.Join(",", logger.Entries.TakeLast(10).Select(e => e.EventName)));
            }
            finally { stop.Cancel(); try { await work; } catch (OperationCanceledException) { } }
        }
    }

    public static async Task LocalDiscardDoesNotConsumeTownCooldownAsync()
    {
        var game = new InventoryDiscardTests.Simulation(1);
        var config = AutomaticCleanupConfig(game);
        game.Api.InventoryCapacity = 2;
        var logger = new InMemoryRoadhogLogger();
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(18));
        var recall = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var press = game.Input.AfterPress;
        game.Input.AfterPress = key => { if (key == "F6") recall.TrySetResult(); else press?.Invoke(key); };
        var context = new AccountWorkerContext(config, game.Api, logger, new AccountRuntimeManager(logger), new() { TickInterval = TimeSpan.FromMilliseconds(1) }, stop.Token);
        var paths = AutomaticCleanupPaths();
        var runner = new CleanupWorkflowRunner(game.Input, paths, (_, _, _) => throw new Exception("unconfirmed recall cannot travel"), new Journal());
        var semi = new SemiAutoCombatController(game.Input);
        var combat = new StationaryCombatController(game.Input, semi, paths);
        var work = new DefaultAccountWorkerLoop(game.Input, semi, combat, cleanupWorkflow: runner).RunAsync(context);
        try
        {
            while (!logger.Entries.Any(e => e.EventName == "cleanup_workflow.complete") || context.CleanupRequests.Current != null)
            { if (work.IsCompleted) await work; await Task.Delay(5, stop.Token); }
            Require(game.Removed.Count == 1 && !recall.Task.IsCompleted, "first request recovers capacity locally");
            game.Api.InventoryItems = new[] { SaleItem(0), SaleItem(1) };
            await recall.Task.WaitAsync(stop.Token);
            Require(!logger.Entries.Any(e => e.EventName == "cleanup_workflow.failed_continuing"), "later full bag can recall immediately instead of consuming full-cleanup cooldown");
        }
        finally { stop.Cancel(); try { await work; } catch (OperationCanceledException) when (stop.IsCancellationRequested) { } }
    }
}
