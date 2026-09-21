using Roadhog.Application;
using Roadhog.Application.BagCleanup;
using Roadhog.Application.SemiAuto;
using Roadhog.Application.StationaryCombat;
using Roadhog.Application.Trading;
using Roadhog.Application.Workers;
using Roadhog.Core.Accounts;
using Roadhog.Core.Diagnostics;
using Roadhog.Core.Model;

internal static partial class CleanupWorkflowTests
{
    public static async Task AutomaticWorkerDiscardCompletionAsync()
    {
        var game = new InventoryDiscardTests.Simulation(3);
        game.Api.TargetEntityId = 0; game.Api.InventoryCapacity = 3;
        game.Settings.BagCleanupEnabled = true; game.Settings.BagCleanupThreshold = 1;
        var config = new AccountConfig { AccountName = "automatic-discard", MainMode = AccountMainMode.SemiAuto,
            ScriptSettings = new() { MainMode = AccountMainMode.SemiAuto, Maintenance = game.Settings } };
        var up = game.Input.AfterMouseUp;
        var injected = false;
        game.Input.AfterMouseUp = button =>
        {
            up?.Invoke(button);
            if (!injected && game.Removed.Count == 2) { injected = true; game.WrongHover = true; }
        };
        var logger = new InMemoryRoadhogLogger();
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var context = new AccountWorkerContext(config, game.Api, logger, new AccountRuntimeManager(logger), new() { TickInterval = TimeSpan.FromMilliseconds(1) }, stop.Token);
        var paths = new InMemorySharedPathStore();
        var semi = new SemiAutoCombatController(game.Input);
        var combat = new StationaryCombatController(game.Input, semi, paths);
        var runner = new CleanupWorkflowRunner(game.Input, paths, (_, _, _) => throw new Exception("automatic discard must stay in place"), new Journal());
        var work = new DefaultAccountWorkerLoop(game.Input, semi, combat, cleanupWorkflow: runner).RunAsync(context);
        async Task WaitFor(string name)
        {
            while (!logger.Entries.Any(e => e.EventName == name))
            {
                if (work.IsCompleted) await work;
                Require(!logger.Entries.Any(e => e.EventName == "cleanup_workflow.failed_continuing"), "automatic discard cannot be dropped");
                await Task.Delay(5, stop.Token);
            }
        }
        try
        {
            await WaitFor("cleanup_workflow.preparation_pending");
            Require(context.CleanupRequests.Current is { Manual: false } && game.Removed.Count == 2 && game.Api.InventoryItems.Count == 1,
                "real capacity trigger retains its automatic request after recovering enough free slots");
            Require(!logger.Entries.Any(e => e.EventName == "cleanup_workflow.complete"), "partial discard is not completion");
            game.WrongHover = false;
            await WaitFor("cleanup_workflow.complete");
            Require(game.Removed.SequenceEqual(new ulong[] { 100, 101, 102 }) && !game.Open, "automatic worker clears the last item before resuming");
            Require(game.Input.Keys.All(k => k is "I" or "Escape"), "automatic retry never recalls, moves or opens NPC dialogue");
        }
        finally
        {
            stop.Cancel();
            try { await work.WaitAsync(TimeSpan.FromSeconds(2)); } catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        }
    }

    public static async Task PostLootDiscardKeepsOwnershipAsync()
    {
        var game = new InventoryDiscardTests.Simulation(2);
        game.Api.TargetEntityId = 0; game.Api.InventoryCapacity = 2;
        game.Settings.BagCleanupEnabled = true; game.Settings.BagCleanupThreshold = 1;
        var settings = new ScriptSettings { MainMode = AccountMainMode.CustomCombat, CombatMode = AccountCombatMode.Stationary,
            Maintenance = game.Settings, Combat = new() { EnableLoot = true, HasStationaryCombatPosition = true, StationaryCombatRadius = 30 } };
        var config = new AccountConfig { AccountName = "post-loot-discard", ScriptSettings = settings };
        var up = game.Input.AfterMouseUp;
        var injected = false;
        game.Input.AfterMouseUp = button =>
        {
            up?.Invoke(button);
            if (!injected && game.Removed.Count == 1) { injected = true; game.WrongHover = true; }
        };
        var logger = new InMemoryRoadhogLogger();
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        var context = new AccountWorkerContext(config, game.Api, logger, new AccountRuntimeManager(logger), new(), stop.Token);
        var semi = new SemiAutoCombatController(game.Input);
        var combat = new StationaryCombatController(game.Input, semi, new InMemorySharedPathStore());
        var state = new StationaryCombatState();
        state.StartLootAfterKill(new LockedTargetSnapshot(100, 5000, 0, LockedTargetSnapshot.MonsterObjectType,
            "dead target", 0, 100, new(0, 0, 0), 0, DateTimeOffset.Now), DateTimeOffset.Now);
        state.LootAfterKill.MoveToPostCombatMaintenance(DateTimeOffset.Now);
        var plan = SemiAutoSkillPlan.FromSettings(settings.Skills);
        var semiState = new SemiAutoCombatState();
        async Task Tick()
        {
            stop.Token.ThrowIfCancellationRequested();
            await combat.TickAsync(context, plan, semiState, state);
            await Task.Delay(5, stop.Token);
        }
        while (state.BagCleanup.Step != BagCleanupStep.WaitDiscardRetry)
        {
            await Tick();
            Require(state.LootAfterKill.Active, "post-combat maintenance must not advance after a partial discard failure");
        }
        var dragCount = game.Drags;
        for (var i = 0; i < 5; i++) await Tick();
        Require(state.LootAfterKill.Active && state.BagCleanup.DiscardActive && game.Drags == dragCount,
            "retry wait owns maintenance and does not hammer the item");
        game.WrongHover = false;
        while (state.LootAfterKill.Active) await Tick();
        Require(game.Removed.Count == 2 && game.Api.InventoryItems.Count == 0 && !game.Open,
            "only verified full discard releases post-combat maintenance");
        Require(logger.Entries.Count(e => e.EventName == "bag_cleanup.discard.complete") == 1, "no false or duplicate completion");
    }

    public static async Task AutomaticDiscardClosedBagRecheckAsync()
    {
        var game = new InventoryDiscardTests.Simulation(3) { TwoLayers = true };
        game.Api.TargetEntityId = 0; game.Api.InventoryCapacity = 3;
        game.Settings.BagCleanupEnabled = true; game.Settings.BagCleanupThreshold = 1;
        var added = false;
        var press = game.Input.AfterPress;
        game.Input.AfterPress = key =>
        {
            press?.Invoke(key);
            if (key == "I" && !game.Open && game.Removed.Count == 3 && !added)
            {
                added = true;
                game.Api.InventoryItems = new[] { new InventoryItemSnapshot(567, 999, "late item", 1, 0, false, 7, 1) };
            }
        };
        var logger = new InMemoryRoadhogLogger();
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var context = new AccountWorkerContext(new AccountConfig { AccountName = "closed-bag-recheck", ScriptSettings = new() { Maintenance = game.Settings } },
            game.Api, logger, new AccountRuntimeManager(logger), new(), stop.Token);
        var seller = new BagCleanupSeller(game.Input);
        var controller = new BagCleanupController(game.Input, new InMemorySharedPathStore(), (_, _, _) => throw new Exception("no NPC work expected"),
            seller: seller, discarder: new BagCleanupDiscarder(game.Input, seller, Fast));
        var state = new BagCleanupState();
        var result = await controller.TickAfterLootAsync(context, state);
        while (state.Active)
        {
            Require(result.Status == BagCleanupTickStatus.Running, "every unfinished step stays running");
            result = await controller.TickAfterLootAsync(context, state);
        }
        Require(added && game.Removed.Count == 4 && game.Confirms == 8 && !game.Open, "fresh candidate is discarded even though space already recovered");
        var completed = logger.Entries.Single(e => e.EventName == "bag_cleanup.discard.complete");
        Require(Equals(completed.Fields["discardedCount"], 4) && Equals(completed.Fields["remainingDiscardCandidateCount"], 0),
            "completion is logged only with zero remaining candidates and preserves cumulative count");
    }

    public static async Task AutomaticDiscardRetrySafetyAsync()
    {
        foreach (var mode in new[] { "input-exception", "attack", "attack-after-confirmation", "stop" })
        {
            var game = new InventoryDiscardTests.Simulation(1)
            { WrongHover = mode is "attack" or "stop", RejectConfirm = mode == "attack-after-confirmation" };
            game.Api.TargetEntityId = 0; game.Api.InventoryCapacity = 2;
            var logger = new InMemoryRoadhogLogger();
            using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var context = new AccountWorkerContext(new AccountConfig { AccountName = mode, ScriptSettings = new() { Maintenance = game.Settings } },
                game.Api, logger, new AccountRuntimeManager(logger), new(), stop.Token);
            var seller = new BagCleanupSeller(game.Input);
            var controller = new BagCleanupController(game.Input, new InMemorySharedPathStore(), (_, _, _) => throw new Exception("unexpected travel"),
                seller: seller, discarder: new BagCleanupDiscarder(game.Input, seller, (_, ct) => Task.Delay(1, ct)));
            var state = new BagCleanupState(); state.StartDiscard(0, 1, 1);
            var up = game.Input.AfterMouseUp;
            if (mode == "input-exception")
            {
                game.Input.AfterMouseUp = _ => throw new InvalidOperationException("input disconnected");
                state.Advance(BagCleanupStep.CloseDiscardInventory);
            }
            while (state.Step != BagCleanupStep.WaitDiscardRetry) await controller.TickAfterLootAsync(context, state);
            Require(state.DiscardActive && game.Removed.Count == 0, "failure leaves an active unfinished batch");
            if (mode == "input-exception")
            {
                game.Input.AfterMouseUp = up;
                while (state.Active) { await controller.TickAfterLootAsync(context, state); await Task.Delay(5, stop.Token); }
                Require(game.Removed.Count == 1 && !game.Open, "input recovery resumes the same batch");
            }
            else if (mode is "attack" or "attack-after-confirmation")
            {
                Require(state.DiscardTarget == null && !state.DiscardConfirmSeen, "verified cancellation clears stale item and dialog state");
                game.Api.TargetEntityId = 100;
                var attackStart = System.Diagnostics.Stopwatch.StartNew();
                var result = await controller.TickAfterLootAsync(context, state);
                Require(result.Reason == "discard_interrupted_by_attack" && !state.Active && game.Removed.Count == 0,
                    "an attacker preempts the retry wait immediately");
                Require(attackStart.Elapsed < TimeSpan.FromSeconds(1), "combat handoff cannot wait for an already cancelled confirmation");
            }
            else
            {
                stop.Cancel();
                try { await controller.TickAfterLootAsync(context, state); throw new Exception("stop was ignored"); }
                catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
                Require(game.Removed.Count == 0, "explicit stop cannot send a new discard action");
            }
        }
    }
}
