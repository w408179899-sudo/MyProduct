using Roadhog.Application;
using Roadhog.Application.BagCleanup;
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
    private static InMemorySharedPathStore ManualCleanupPaths(ScriptSettings settings, FakeGameApi api,
        RecordingKeyboardInput input, params SharedPathDocument[] extra)
    {
        settings.Paths.BagCleanupTownReturnKey = "F6";
        settings.Paths.RevivePathName = "resume";
        var loading = false;
        var destination = extra.FirstOrDefault()?.Points.FirstOrDefault()?.ToVector3() ?? new Vector3Snapshot(1000, 0, 0);
        api.TransitionRead = () =>
        {
            if (loading) { loading = false; return new(false, null, null, DateTimeOffset.UtcNow); }
            return new(true, api.Player, api.Channel, DateTimeOffset.UtcNow);
        };
        var press = input.AfterPress;
        input.AfterPress = key =>
        {
            if (key == "F6") { api.Player = api.Player with { Position = destination }; loading = true; }
            else press?.Invoke(key);
        };
        return new InMemorySharedPathStore(extra.Append(new SharedPathDocument
        { Name = "resume", Points = new() { new() { X = 1000 } } }).ToArray());
    }

    public static async Task ManualRecallBeforeDiscardAsync()
    {
        foreach (var mode in new[] { "manual", "legacy-key", "automatic" })
        {
            var game = new InventoryDiscardTests.Simulation(2) { TwoLayers = true };
            game.Api.TargetEntityId = 0;
            game.Api.InventoryCapacity = 72;
            var config = new AccountConfig { AccountName = mode, ScriptSettings = new() };
            var settings = config.ScriptSettings;
            settings.Maintenance.BagCleanupRules = game.Settings.BagCleanupRules;
            settings.Paths.BagCleanupTownReturnKey = mode == "legacy-key" ? "" : "F6";
            settings.Paths.TownReturnKey = "F5";
            settings.Paths.RevivePathName = "resume";
            var paths = new InMemorySharedPathStore(new SharedPathDocument
            { Name = "resume", Points = new() { new() { X = 1000 }, new() { X = 0 } } });
            var recalled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var events = new List<string>();
            var press = game.Input.AfterPress;
            game.Input.AfterPress = key =>
            {
                events.Add(key);
                if (key is "F6" or "F5") recalled.TrySetResult();
                else press?.Invoke(key);
            };
            var logger = new InMemoryRoadhogLogger();
            using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var context = new AccountWorkerContext(config, game.Api, logger, new AccountRuntimeManager(logger), new(), stop.Token);
            var runner = new CleanupWorkflowRunner(game.Input, paths, (_, name, points) =>
            {
                Require(game.Removed.Count == 2 && !game.Open, "complete discard before return to grinding");
                events.Add(name); game.Api.Player = game.Api.Player with { Position = points[^1] };
                return Task.FromResult(OperationResult.Ok());
            }, new Journal());
            var work = runner.RunAsync(context, new(settings, mode != "automatic"));
            if (mode != "automatic")
            {
                await recalled.Task.WaitAsync(stop.Token);
                await Task.Delay(350, stop.Token);
                Require(!events.Contains("I") && game.Removed.Count == 0 && !work.IsCompleted,
                    "keypress alone is not recall success and cannot start discard");
                game.Api.Player = game.Api.Player with { Position = new(1000, 0, 0) };
            }
            await work;
            Require(game.Removed.Count == 2 && game.Confirms == 4, "both confirmation layers reset for each item");
            if (mode == "automatic") Require(!events.Any(e => e is "F6" or "F5" or "resume"), "automatic discard keeps its original location");
            else Require(events.First() == (mode == "legacy-key" ? "F5" : "F6") && events.Last() == "resume", "configured cleanup recall precedes bag, revive follows completed cleanup");
        }
    }

    public static async Task FullDiscardBatchAsync()
    {
        var game = new InventoryDiscardTests.Simulation(20) { TwoLayers = true };
        game.Api.TargetEntityId = 0;
        game.Api.InventoryCapacity = 100;
        var config = new AccountConfig { AccountName = "long-discard", ScriptSettings = new() };
        config.ScriptSettings.Maintenance.BagCleanupThreshold = 2;
        config.ScriptSettings.Maintenance.BagCleanupRules = game.Settings.BagCleanupRules;
        var addedAfterClose = false;
        var press = game.Input.AfterPress;
        game.Input.AfterPress = key =>
        {
            press?.Invoke(key);
            if (key == "I" && !game.Open && game.Removed.Count == 20 && !addedAfterClose)
            {
                addedAfterClose = true;
                game.Api.InventoryItems = new[] { new InventoryItemSnapshot(567, 999, "late item", 1, 0, false, 7, 1) };
            }
        };
        var logger = new InMemoryRoadhogLogger();
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var context = new AccountWorkerContext(config, game.Api, logger, new AccountRuntimeManager(logger), new(), stop.Token);
        var seller = new BagCleanupSeller(game.Input);
        var controller = new BagCleanupController(game.Input, new InMemorySharedPathStore(), (_, _, _) => throw new Exception("discard cannot travel"),
            seller: seller, discarder: new BagCleanupDiscarder(game.Input, seller, Fast));
        var progress = 0;
        await controller.RunDiscardRequestedAsync(context, _ => progress++);
        Require(addedAfterClose && game.Removed.Count == 21 && game.Removed.Distinct().Count() == 21 && game.Confirms == 42,
            "all items, including fresh candidates after closing, are verified despite free slots exceeding threshold");
        Require(progress > 100 && !game.Open && game.Api.InventoryItems.Count == 0, "long batch completes only after fresh bag exhaustion and closed UI");
    }

    public static async Task WorkerDiscardRetriesAsync()
    {
        foreach (var failure in new[] { "hover", "confirmation", "stop" })
        {
            var game = new InventoryDiscardTests.Simulation(2);
            game.Api.TargetEntityId = 0;
            var up = game.Input.AfterMouseUp;
            var injected = false;
            game.Input.AfterMouseUp = button =>
            {
                up?.Invoke(button);
                if (game.Removed.Count == 1 && !injected)
                {
                    injected = true;
                    game.WrongHover = failure != "confirmation";
                    game.RejectConfirm = failure == "confirmation";
                }
            };
            var config = new AccountConfig { AccountName = "retry-" + failure, MainMode = AccountMainMode.SemiAuto, ScriptSettings = new() };
            var settings = config.ScriptSettings;
            settings.MainMode = AccountMainMode.SemiAuto;
            settings.Maintenance.BagCleanupRules = game.Settings.BagCleanupRules;
            var paths = ManualCleanupPaths(settings, game.Api, game.Input);
            var logger = new InMemoryRoadhogLogger();
            // The real mouse adapter pacing plus a four-second unresponsive dialog and retry
            // deliberately outlive a short action timeout; this is a whole-workflow deadline.
            using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(45));
            var context = new AccountWorkerContext(config, game.Api, logger, new AccountRuntimeManager(logger), new() { TickInterval = TimeSpan.FromMilliseconds(1) }, stop.Token);
            var semi = new SemiAutoCombatController(game.Input);
            var combat = new StationaryCombatController(game.Input, semi, paths);
            var runner = new CleanupWorkflowRunner(game.Input, paths, (_, _, _) => throw new Exception("no NPC sale candidates"), new Journal());
            Require(context.CleanupRequests.Request(settings, true).Success, "enqueue manual cleanup");
            var work = new DefaultAccountWorkerLoop(game.Input, semi, combat, cleanupWorkflow: runner).RunAsync(context);
            async Task WaitFor(string eventName)
            {
                while (!logger.Entries.Any(e => e.EventName == eventName))
                {
                    if (work.IsCompleted) await work;
                    await Task.Delay(5, stop.Token);
                }
            }
            try
            {
                await WaitFor("cleanup_workflow.preparation_pending");
                Require(context.CleanupRequests.Current != null && game.Removed.Count == 1 && !game.Open && !game.LeftHeld,
                    "failed second discard preserves pending request and safely closes UI");
                Require(!logger.Entries.Any(e => e.EventName is "cleanup_workflow.complete" or "cleanup_workflow.failed_continuing"),
                    "unfinished discard cannot count as completion or resume normal work");
                if (failure != "stop")
                {
                    game.WrongHover = game.RejectConfirm = false;
                    await WaitFor("cleanup_workflow.complete");
                    Require(game.Removed.SequenceEqual(new ulong[] { 100, 101 }) && game.Api.InventoryItems.Count == 0,
                        "retry locates only remaining actual inventory items");
                    Require(game.Input.Keys.Count(k => k == "F6") == 1, "discard retry must not recall again");
                }
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested)
            {
                throw new Exception(failure + " retry timed out; removed=" + game.Removed.Count + "; keys=" + string.Join(",", game.Input.Keys) +
                    "; events=" + string.Join(" | ", logger.Entries.TakeLast(18).Select(e => e.EventName + ":" + string.Join(",", e.Fields.Select(f => f.Key + "=" + f.Value)))));
            }
            finally
            {
                stop.Cancel();
                try { await work.WaitAsync(TimeSpan.FromSeconds(2)); } catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
            }
            Require(logger.Entries.Count(e => e.EventName == "worker.loop.enter") == 1, "discard retry preserves same worker");
            if (failure == "stop") Require(game.Removed.Count == 1 && !logger.Entries.Any(e => e.EventName == "cleanup_workflow.complete"), "manual stop cancels retry, never claims all discarded");
        }
    }

    public static async Task UnconfirmedRecallStaysPendingAsync()
    {
        var previousTimeout = Environment.GetEnvironmentVariable("ROADHOG_BAG_CLEANUP_TOWN_RETURN_SETTLE_MS");
        Environment.SetEnvironmentVariable("ROADHOG_BAG_CLEANUP_TOWN_RETURN_SETTLE_MS", "100");
        try
        {
            var game = new InventoryDiscardTests.Simulation(1);
            game.Api.TargetEntityId = 0;
            var config = new AccountConfig { AccountName = "recall-pending", MainMode = AccountMainMode.SemiAuto, ScriptSettings = new() };
            config.ScriptSettings.MainMode = AccountMainMode.SemiAuto;
            config.ScriptSettings.Maintenance.BagCleanupRules = game.Settings.BagCleanupRules;
            var press = game.Input.AfterPress;
            var paths = ManualCleanupPaths(config.ScriptSettings, game.Api, game.Input);
            game.Input.AfterPress = press; // Recall key does not change the actual player position.
            var logger = new InMemoryRoadhogLogger();
            using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            var context = new AccountWorkerContext(config, game.Api, logger, new AccountRuntimeManager(logger), new() { TickInterval = TimeSpan.FromMilliseconds(1) }, stop.Token);
            var semi = new SemiAutoCombatController(game.Input);
            var combat = new StationaryCombatController(game.Input, semi, paths);
            var runner = new CleanupWorkflowRunner(game.Input, paths, (_, _, _) => throw new Exception("must not travel before recall"), new Journal());
            context.CleanupRequests.Request(config.ScriptSettings, true);
            var work = new DefaultAccountWorkerLoop(game.Input, semi, combat, cleanupWorkflow: runner).RunAsync(context);
            try
            {
                while (!logger.Entries.Any(e => e.EventName == "bag_cleanup.return.press") || game.Api.TransitionReadCount < 5)
                {
                    if (work.IsCompleted) await work;
                    await Task.Delay(5, stop.Token);
                }
                Require(context.CleanupRequests.Current != null && !game.Input.Keys.Contains("I") && game.Drags == 0,
                    "unverified recall leaves request pending without any discard input");
                Require(game.Input.Keys.Count(k => k == "F6") == 1, "unconfirmed recall is observed without repeated key submission");
            }
            finally
            {
                stop.Cancel();
                try { await work.WaitAsync(TimeSpan.FromSeconds(2)); } catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
            }
        }
        finally { Environment.SetEnvironmentVariable("ROADHOG_BAG_CLEANUP_TOWN_RETURN_SETTLE_MS", previousTimeout); }
    }
}
