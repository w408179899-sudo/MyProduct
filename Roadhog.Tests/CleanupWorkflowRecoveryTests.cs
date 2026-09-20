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
    public static async Task WorkerFailureRecoveryAsync()
    {
        await WorkerFailureRecoveryAsync(discardHoverFailure: false);
        await WorkerFailureRecoveryAsync(discardHoverFailure: true);
    }

    private static async Task WorkerFailureRecoveryAsync(bool discardHoverFailure)
    {
        var api = new FakeGameApi { TargetEntityId = 0 };
        var input = new RecordingKeyboardInput();
        var logger = new InMemoryRoadhogLogger();
        var runtime = new AccountRuntimeManager(logger);
        var config = new AccountConfig { AccountName = "cleanup-recovery", MainMode = AccountMainMode.SemiAuto, ScriptSettings = new() };
        config.ScriptSettings.MainMode = AccountMainMode.SemiAuto;
        config.ScriptSettings.Maintenance.BagCleanupEnabled = true;
        config.ScriptSettings.Maintenance.BagCleanupThreshold = 5;
        config.ScriptSettings.Maintenance.CleanupWorkflow = new() { NpcCleanup = false, Auction = true };
        config.ScriptSettings.Paths.AuctionPathName = "missing";
        var bagOpen = false;
        if (discardHoverFailure)
        {
            config.ScriptSettings.Maintenance.CleanupWorkflow = new() { NpcCleanup = true };
            config.ScriptSettings.Maintenance.BagCleanupDiscardItemNameKeywords = new() { "绳套陷阱 III" };
            api.InventoryItems = new[] { new InventoryItemSnapshot(10, 20, "绳套陷阱 III", 1, 0, false) };
            api.InventoryInteractionRead = () => new(bagOpen, false, false,
                new[] { new InventoryUiItem(20, 10, 1, new(100, 100)) }, 0, 0, null, new(400, 400), false);
            input.AfterPress = key => { if (key == "I") bagOpen = !bagOpen; };
            Cursor(api, input);
        }
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        var context = new AccountWorkerContext(config, api, logger, runtime, new() { TickInterval = TimeSpan.FromMilliseconds(1) }, stop.Token);
        var semi = new SemiAutoCombatController(input);
        var combat = new StationaryCombatController(input, semi);
        var runner = new CleanupWorkflowRunner(input, new InMemorySharedPathStore(), (_, _, _) => Task.FromResult(OperationResult.Ok()), new Journal());
        context.CleanupRequests.Request(config.ScriptSettings, true);
        var task = new DefaultAccountWorkerLoop(input, semi, combat, cleanupWorkflow: runner).RunAsync(context);
        try
        {
            while (!logger.Entries.Any(e => e.EventName == "cleanup_workflow.failed_continuing"))
            {
                if (task.IsCompleted) await task;
                await Task.Delay(5, stop.Token);
            }
            var baseline = api.PlayerReadCount;
            while (api.PlayerReadCount < baseline + 3) await Task.Delay(5, stop.Token);
            Require(!task.IsCompleted && context.CleanupRequests.Current == null, "failed preflight is discarded and normal player processing resumes");
            Require(logger.Entries.Count(e => e.EventName == "cleanup_workflow.failed_continuing") == 1, "full bag cannot immediately requeue failed cleanup");
            if (discardHoverFailure)
            {
                Require(logger.Entries.Any(e => e.EventName == "bag_cleanup.discard.failed" && Equals(e.Fields["reason"], "discard_drag_failed")), "account 2 hover mismatch reproduces exact failure");
                Require(!bagOpen && api.InventoryItems.Count == 1, "mismatched item remains intact and bag closes before resuming");
                api.InventoryItems = Array.Empty<InventoryItemSnapshot>();
            }
            var retry = config.ScriptSettings.Clone();
            retry.Maintenance.CleanupWorkflow = new() { NpcCleanup = true };
            Require(context.CleanupRequests.Request(retry, true).Success, "manual request still accepted during automatic cooldown");
            while (!logger.Entries.Any(e => e.EventName == "cleanup_workflow.complete"))
            {
                if (task.IsCompleted) await task;
                await Task.Delay(5, stop.Token);
            }
            Require(logger.Entries.Count(e => e.EventName == "worker.loop.enter") == 1, "local failure must retain combat state and worker");
        }
        finally
        {
            stop.Cancel();
            try { await task; } catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        }
    }

    public static async Task WorkerCleanupDeathRecoveryAsync()
    {
        var api = new FakeGameApi
        {
            TargetEntityId = 0,
            InventoryItems = new[] { new InventoryItemSnapshot(167000001, 1, "魔石:攻击力+5", 1, 0, false, QualityRank: 2) }
        };
        var input = new RecordingKeyboardInput();
        var logger = new InMemoryRoadhogLogger();
        var config = new AccountConfig { AccountName = "cleanup-death", MainMode = AccountMainMode.SemiAuto, ScriptSettings = new() };
        config.ScriptSettings.MainMode = AccountMainMode.SemiAuto;
        config.ScriptSettings.Paths.MaintenancePathName = "merchant";
        var rules = BagCleanupRuleCatalog.CreateDefaultRules();
        var sell = rules.Single(r => r.Key == BagCleanupRuleCatalog.GreenManastone);
        sell.Enabled = true; sell.Action = BagCleanupAction.Sell;
        config.ScriptSettings.Maintenance.BagCleanupRules = rules;
        Require(BagCleanupItemMatcher.SelectSellRegistrationItems(api.InventoryItems, config.ScriptSettings.Maintenance).Count == 1, "fixture selects NPC sale");
        var paths = new InMemorySharedPathStore(new SharedPathDocument
        {
            Name = "merchant", CleanupNpcName = "merchant", Points = new() { new() { X = 50 } }
        });
        var travel = new List<string>();
        var runner = new CleanupWorkflowRunner(input, paths, (_, name, _) =>
        {
            travel.Add(name);
            api.Player = api.Player with { CurrentHp = 0 };
            return Task.FromResult(OperationResult.Fail("角色死亡，路径流程已停止。"));
        }, new Journal());
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        var context = new AccountWorkerContext(config, api, logger, new AccountRuntimeManager(logger), new() { TickInterval = TimeSpan.FromMilliseconds(1) }, stop.Token);
        context.CleanupRequests.Request(config.ScriptSettings, true);
        var semi = new SemiAutoCombatController(input);
        var combat = new StationaryCombatController(input, semi);
        var task = new DefaultAccountWorkerLoop(input, semi, combat, cleanupWorkflow: runner).RunAsync(context);
        try
        {
            while (!logger.Entries.Any(e => e.EventName == "player_life.death.detected"))
            {
                if (task.IsCompleted) await task;
                await Task.Delay(5, stop.Token);
            }
            Require(!task.IsCompleted && context.CleanupRequests.Current != null, "death hands off to life guard and preserves pending cleanup");
            Require(travel.SequenceEqual(new[] { "merchant" }), "dead player must not run reverse path");
            Require(!logger.Entries.Any(e => e.EventName is "bag_cleanup.completion_jump.pressed" or "cleanup_workflow.failed_continuing"), "death must not become a failed account or completion action");
        }
        finally
        {
            stop.Cancel();
            try { await task; } catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        }
    }
}
