using Roadhog.Application;
using Roadhog.Application.BagCleanup;
using Roadhog.Application.SemiAuto;
using Roadhog.Application.StationaryCombat;
using Roadhog.Application.Trading;
using Roadhog.Application.Workers;
using Roadhog.Core.Accounts;
using Roadhog.Core.Api;
using Roadhog.Core.Diagnostics;
using Roadhog.Core.Model;

internal static class CombatFlowRefactorTests
{
    public static IEnumerable<(string Name, Func<Task> Run)> Cases
    {
        get
        {
            yield return ("refactor maintenance distinguishes confirmation and timeout", MaintenanceOutcomesAsync);
            yield return ("refactor maintenance guards every attempt", MaintenanceGuardAsync);
            foreach (var boundary in new[] { "guard", "press", "confirm", "delay" })
                yield return ("refactor maintenance cancellation after " + boundary, () => MaintenanceCancellationAsync(boundary));
            yield return ("refactor target handoff preserves roles and clears exit state", TargetTransitionsAsync);
            yield return ("refactor quickbar handoff cancels rather than resumes", QuickbarHandoffAsync);
            yield return ("refactor test marker contention stays bounded and never fabricates data", MarkerObservationAsync);
            foreach (var scenario in new[] { "queued", "success", "restart", "combat", "death", "session", "trip", "discard", "failure", "budget", "post_sale", "execution_failure", "stop_prepare", "stop_jump" })
                yield return ("refactor cleanup ownership " + scenario, () => CleanupOwnershipAsync(scenario));
            foreach (var scenario in new[] { "none", "low_hp", "ordinary_approach", "defense_approach", "dead", "outside_home", "postcombat_return" })
                yield return ("refactor stationary phase " + scenario, () => StationaryPhaseAsync(scenario));
            foreach (var test in CombatFlowMaintenanceAdapterTests.Cases) yield return test;
        }
    }

    private static async Task MaintenanceOutcomesAsync()
    {
        foreach (var confirmationAttempt in new[] { 1, 3, int.MaxValue })
        {
            using var actions = new MaintenanceActions { ConfirmOn = confirmationAttempt };
            var result = await RunMaintenance(actions);
            var confirmed = confirmationAttempt != int.MaxValue;
            Check(result.Outcome == (confirmed ? MaintenanceActionOutcome.Confirmed : MaintenanceActionOutcome.Unconfirmed),
                "timeout must not masquerade as a confirmed release");
            Check(result.ConsumesTick && result.Attempts == (confirmed ? confirmationAttempt : 21),
                "the existing six-second/300ms cadence and tick ownership must survive extraction");
            Check((result.ConfirmedSkill is not null) == confirmed, "only evidence can produce a confirmed skill");
            Check(actions.Delays.All(d => d == TimeSpan.FromMilliseconds(300)), "retry cadence changed");
        }
        using var failed = new MaintenanceActions { PressSucceeds = false };
        var failure = await RunMaintenance(failed);
        Check(failure.Outcome == MaintenanceActionOutcome.InputFailed && !failure.ConsumesTick &&
              failed.Calls.SequenceEqual(new[] { "guard", "press" }), "failed input must not reach confirmation or a second press");
    }

    private static async Task MaintenanceGuardAsync()
    {
        foreach (var blockedAt in new[] { 0, 1, 3 })
        foreach (var consumes in new[] { false, true })
        {
            using var actions = new MaintenanceActions { BlockAfter = blockedAt, BlockConsumes = consumes };
            var result = await RunMaintenance(actions);
            Check(result.Outcome == MaintenanceActionOutcome.TargetBlocked && result.ConsumesTick == consumes &&
                  result.Attempts == blockedAt && actions.Presses == blockedAt, "a dead/changed target must stop the next input");
            Check(actions.Calls.Count(c => c == "guard") == blockedAt + 1, "each retry needs a fresh target guard");
        }
    }

    private static async Task MaintenanceCancellationAsync(string boundary)
    {
        using var actions = new MaintenanceActions { CancelAfter = boundary };
        await ExpectCanceled(() => RunMaintenance(actions));
        Check(actions.Calls.Last() == boundary, "cancellation leaked a later confirmation/input");
    }

    private static Task<MaintenanceActionResult> RunMaintenance(MaintenanceActions actions) =>
        MaintenanceConfirmation.RunAsync(actions, TimeSpan.FromSeconds(6), TimeSpan.FromMilliseconds(300));

    private static Task MarkerObservationAsync()
    {
        var path = Path.Combine(Path.GetTempPath(), "RoadhogMarker-" + Guid.NewGuid().ToString("N"));
        try
        {
            File.WriteAllText(path, "attempt\nattempt\n");
            using (var writer = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.None))
            {
                var elapsed = System.Diagnostics.Stopwatch.StartNew();
                var failed = false;
                try { TestMarkerReader.ReadAllLines(path); }
                catch (IOException error) when ((error.HResult & 0xffff) is 32 or 33) { failed = true; }
                Check(failed && elapsed.Elapsed < TimeSpan.FromSeconds(2), "persistent contention must fail within a bound, never return invented observations");
            }
            Check(TestMarkerReader.ReadAllLines(path).SequenceEqual(new[] { "attempt", "attempt" }), "retry changed marker contents or count");
            File.Delete(path);
            var missingFailed = false;
            try { TestMarkerReader.ReadAllLines(path); }
            catch (FileNotFoundException) { missingFailed = true; }
            Check(missingFailed, "non-contention errors must remain visible");
            return Task.CompletedTask;
        }
        finally { File.Delete(path); }
    }

    private sealed class MaintenanceActions : IMaintenanceConfirmationActions, IDisposable
    {
        private readonly CancellationTokenSource stop = new();
        public CancellationToken StopToken => stop.Token;
        public DateTimeOffset Now { get; private set; } = DateTimeOffset.UnixEpoch;
        public int ConfirmOn { get; init; } = int.MaxValue;
        public int BlockAfter { get; init; } = int.MaxValue;
        public bool BlockConsumes { get; init; }
        public bool PressSucceeds { get; init; } = true;
        public string? CancelAfter { get; init; }
        public int Presses { get; private set; }
        public List<string> Calls { get; } = new();
        public List<TimeSpan> Delays { get; } = new();
        private void Observe(string name) { Calls.Add(name); if (CancelAfter == name) stop.Cancel(); }
        public Task<MaintenanceAttemptPermission> BeforeAttemptAsync(int attempts, DateTimeOffset startedAt)
        {
            Observe("guard");
            return Task.FromResult(attempts >= BlockAfter ? new MaintenanceAttemptPermission(false, BlockConsumes) : MaintenanceAttemptPermission.Allow);
        }
        public Task<bool> PressAsync() { Observe("press"); Presses++; return Task.FromResult(PressSucceeds); }
        public Task<SkillSnapshot?> ConfirmAsync()
        {
            Observe("confirm");
            return Task.FromResult<SkillSnapshot?>(Presses >= ConfirmOn ? Skill() : null);
        }
        public Task DelayAsync(TimeSpan delay) { Observe("delay"); Delays.Add(delay); Now += delay; return Task.CompletedTask; }
        public void Dispose() => stop.Dispose();
    }

    private static Task TargetTransitionsAsync()
    {
        var state = new StationaryCombatState { CurrentTargetIsTacticalMark = true, PostCombatReturnPending = true };
        state.MarkGatherSafetyApproach();
        state.MarkMaintenanceDefenseApproach();
        Check(!state.Fighting && state.CurrentTargetBypassesHomeLeash, "approaching is not fighting");
        state.BeginFight(12, 123, maintenanceDefense: false, revivePathClear: true);
        Check(state.Fighting && state.CurrentTargetEntityId == 12 && state.CurrentTargetServerObjectId == 123 &&
              !state.CurrentTargetIsMaintenanceDefense && state.CurrentTargetIsRevivePathClear &&
              state.CurrentTargetIsGatherSafetyClear && state.CurrentTargetIsTacticalMark && state.CurrentTargetBypassesHomeLeash,
            "handoff must preserve roles that its caller did not replace");
        state.PreparePathTargetRoles(false, false);
        Check(!state.CurrentTargetIsRevivePathClear && !state.CurrentTargetBypassesHomeLeash,
            "ordinary path selection clears only its owned roles");
        state.ClearTarget();
        Check(!state.Fighting && state.CurrentTargetEntityId == 0 && state.CurrentTargetServerObjectId == 0 &&
              !state.CurrentTargetIsMaintenanceDefense && !state.CurrentTargetIsGatherSafetyClear &&
              !state.CurrentTargetIsRevivePathClear && !state.CurrentTargetIsTacticalMark && !state.CurrentTargetBypassesHomeLeash,
            "target exit left a role capable of affecting a future monster");
        Check(state.PostCombatReturnPending, "target exit must preserve the independent return request");
        state.BeginFight(13, 124, maintenanceDefense: true);
        state.EnterDeathRecovery(DateTimeOffset.Now);
        Check(!state.Fighting && state.CurrentTargetEntityId == 0 && !state.CurrentTargetIsMaintenanceDefense &&
              !state.PostCombatReturnPending, "death must clear both combat and its return context");
        return Task.CompletedTask;
    }

    private static Task QuickbarHandoffAsync()
    {
        foreach (var preserve in new[] { false, true })
        {
            var state = new QuickbarSkillCombatState();
            var node = new QuickbarSkillNode(11, "test", "D1", SkillQuickbar.Main, 0, 11, "root", Array.Empty<QuickbarSkillNode>());
            node = node with { Children = new[] { node with { SkillId = 12, NodeKey = "root/child" } } };
            var availability = new SkillAvailabilitySnapshot(0, Array.Empty<SkillAvailabilitySlotSnapshot>());
            state.BeginAction(node, Skill(), availability, DateTimeOffset.Now, TimeSpan.FromSeconds(8));
            Check(state.BeginChainTransition(state.PendingAction!, DateTimeOffset.Now, "release"), "fixture needs a live chain transition");
            state.AttackWeave.TrackOpeningPress(Skill(), TimeProvider.System, TimeSpan.FromSeconds(8));
            state.CancelInputAttemptsForHandoff(preserve);
            Check(state.PendingAction is null && state.ChainTransition is null && state.LastChainTransitionEndReason == "maintenance",
                "handoff retained an old key attempt/chain");
            Check(state.AttackWeave.HasOpeningAttempt == preserve, "attack weave preservation contract changed");
            Check(!state.TryConfirmAction(availability with { LastReleasedSkillId = 11, LastReleasedSkillTime = 100 },
                new[] { Skill() with { CooldownEndTime = 1000 } }, DateTimeOffset.Now), "a late release resurrected a cancelled attempt");
        }
        return Task.CompletedTask;
    }

    private static async Task CleanupOwnershipAsync(string scenario)
    {
        using var f = new CleanupFixture(scenario);
        var before = DateTimeOffset.UtcNow;
        if (scenario is "budget" or "stop_prepare" or "stop_jump")
        {
            await ExpectCanceled(f.RunAsync);
            Check(f.Context.CleanupRequests.Current == f.Request && !f.Actions.Calls.Contains("abandon"),
                "budget/stop must propagate to the worker without replay or false completion");
            if (scenario != "budget") Check(!f.Actions.Calls.Contains("run"), "stop during handoff leaked workflow input");
            else Check(f.Progress.OperationActive, "the outer budget handler lost transaction ownership");
            return;
        }
        var result = await f.RunAsync();
        Check(f.Actions.Calls.Count(c => c == "run") == (scenario == "queued" ? 0 : 1), "workflow replayed or disappeared");
        switch (scenario)
        {
            case "queued":
            case "combat":
            case "death":
                Check(f.Context.CleanupRequests.Current == f.Request && result == CleanupRequestTickResult.Yield,
                    "resumable work lost its request");
                Check(!f.Actions.Calls.Contains("abandon"), "interrupted work must not be silently abandoned");
                if (scenario == "death") Check(!f.Request.TownReturnCompleted, "death retained an obsolete town-return completion");
                break;
            case "restart":
            case "post_sale":
                Check(result == CleanupRequestTickResult.RestartPending && f.Context.CleanupRequests.Current is null &&
                    f.Context.CleanupRequests.StandaloneShopRestartRequestId == f.Request.RequestId, "restart ownership was not transferred exactly once");
                Check(f.Actions.Calls.Contains("finish") == (scenario == "post_sale"), "post-sale recovery ran outside its confirmed boundary");
                break;
            default:
                Check(result == CleanupRequestTickResult.Yield && f.Context.CleanupRequests.Current is null, "completed/abandoned request remained queued");
                if (scenario is "failure" or "execution_failure")
                    Check(f.Actions.Calls.Last() == "abandon" && f.Progress.CheckLifeAfterYield &&
                          f.Progress.NextCheck >= before.AddSeconds(30), "failure did not return ownership with a bounded retry delay");
                if (scenario == "execution_failure") Check(!f.Actions.Calls.Contains("finish"), "execution failure must not enter post-sale finalization");
                if (scenario == "trip") Check(f.Progress.NextCheck >= before.AddSeconds(2) &&
                    f.Progress.NextCheck < before.AddSeconds(10), "pre-NPC trip interruption changed retry policy");
                if (scenario == "discard") Check(f.Progress.NextCheck >= before.AddSeconds(30), "discard abandonment lost cooldown");
                if (scenario == "success") Check(f.Progress.LastCleanup >= before, "full cleanup completion lost cooldown");
                break;
        }
    }

    private sealed class CleanupFixture : IDisposable
    {
        public CancellationTokenSource Stop { get; } = new();
        public AccountWorkerContext Context { get; }
        public StationaryCombatState State { get; } = new();
        public CleanupLoopProgress Progress { get; } = new();
        public CleanupRequest Request { get; }
        public CleanupActions Actions { get; }
        public CleanupFixture(string scenario)
        {
            var logger = new InMemoryRoadhogLogger();
            Context = new(new AccountConfig { AccountName = "refactor" }, new FakeGameApi(), logger,
                new AccountRuntimeManager(logger), new AccountWorkerOptions(), Stop.Token);
            var settings = new ScriptSettings();
            settings.Maintenance.CleanupWorkflow.NpcCleanup = true;
            settings.Maintenance.CleanupWorkflow.GroceryPostSaleAction = GroceryPostSaleAction.NpcSell;
            var grocery = scenario is "trip" or "post_sale" or "execution_failure" or "stop_jump";
            var accepted = Context.CleanupRequests.Request(settings, true, standaloneShop: scenario == "restart",
                groceryTrigger: grocery ? GroceryShopTrigger.Manual : GroceryShopTrigger.None);
            Check(accepted.Success, "fixture request rejected: " + accepted.Error);
            Request = Context.CleanupRequests.Current!;
            Request.TownReturnCompleted = true;
            Request.FullCleanupStarted = true;
            Request.PreparationStage = scenario is "discard" or "combat" or "death" or "session"
                ? CleanupPreparationStage.Discarding : CleanupPreparationStage.None;
            Request.SoldOutConfirmed = scenario is "post_sale" or "execution_failure";
            Request.GroceryNpcStageStarted = Request.SoldOutConfirmed;
            Actions = new(this, scenario);
        }
        public Task<CleanupRequestTickResult> RunAsync() => CleanupRequestRunner.RunAsync(Context, State, Request, Progress, Actions);
        public void Dispose() => Stop.Dispose();
    }

    private sealed class CleanupActions(CleanupFixture fixture, string scenario) : ICleanupRequestActions
    {
        public List<string> Calls { get; } = new();
        public Task<bool> PrepareAsync(CleanupRequest request)
        {
            Calls.Add("prepare");
            if (scenario == "stop_prepare") fixture.Stop.Cancel();
            return Task.FromResult(scenario != "queued");
        }
        public Task StopJumpAsync() { Calls.Add("jump"); if (scenario == "stop_jump") fixture.Stop.Cancel(); return Task.CompletedTask; }
        public Task RunWorkflowAsync(CleanupRequest request)
        {
            Calls.Add("run");
            Exception? error = scenario switch
            {
                "combat" => new CleanupCombatInterruptionException(), "death" => new CleanupDeathInterruptionException(),
                "session" => new CleanupSessionInterruptionException(), "trip" => new GroceryTripDepartureFailedException("trip"),
                "budget" => new SnapshotReadBudgetExceededException("inventory"),
                "execution_failure" => new CleanupExecutionFailedException("execution", new Exception()),
                "discard" or "failure" or "post_sale" => new InvalidOperationException(scenario), _ => null
            };
            return error is null ? Task.CompletedTask : Task.FromException(error);
        }
        public Task FinishGroceryAfterFailureAsync(CleanupRequest request, Exception error) { Calls.Add("finish"); return Task.CompletedTask; }
        public Task ClearChannelSwitchAsync() { Calls.Add("channel"); return Task.CompletedTask; }
        public Task AbandonAsync(CleanupRequest request, Exception error)
        {
            Calls.Add("abandon"); fixture.Context.CleanupRequests.Abandon(); return Task.CompletedTask;
        }
    }

    private static async Task StationaryPhaseAsync(string scenario)
    {
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var settings = new ScriptSettings
        {
            MainMode = AccountMainMode.CustomCombat, CombatMode = AccountCombatMode.Stationary,
            Combat = new() { HasStationaryCombatPosition = true, StationaryCombatRadius = 60,
                TargetAcquireDistance = 29, ReturnHomeWhenNoTarget = false, EnableLoot = false },
            Maintenance = new() { SitMaintenanceEnabled = true, SitHpBelowPercent = 50, SitHpRecoverToPercent = 60 },
            SemiAuto = new() { KeyHoldMs = 1, KeyGapMs = 1 }
        };
        var api = new FakeGameApi
        {
            Player = new(1, 0, "test", scenario == "dead" ? 0u : scenario == "low_hp" ? 20u : 100u, 100,
                100, 100, 0, new(scenario == "outside_home" ? 100 : 0, 0, 0), DateTimeOffset.Now, StanceFlags: 1),
            TargetEntityId = 0, TargetCurrentHp = 0, TargetPosition = null,
            WorldObjects = scenario.EndsWith("approach") ? new[]
            {
                new WorldObjectSnapshot(200, 2000, "monster", "monster", new(50, 0, 0), 50, 1000, 1000,
                    TargetServerObjectId: scenario == "defense_approach" ? 100u : 0u,
                    IsTargetingLocalPlayer: scenario == "defense_approach")
            } : Array.Empty<WorldObjectSnapshot>()
        };
        var input = new RecordingKeyboardInput();
        var logger = new InMemoryRoadhogLogger();
        var state = new StationaryCombatState { PostCombatReturnPending = scenario == "postcombat_return" };
        state.MarkStartupRecoveryChecked();
        var semiAuto = new SemiAutoCombatState();
        var context = new AccountWorkerContext(new AccountConfig { AccountName = "test", ScriptSettings = settings },
            api, logger, new AccountRuntimeManager(logger), new AccountWorkerOptions(), stop.Token);
        var controller = new StationaryCombatController(input, new SemiAutoCombatController(input));
        try
        {
            await controller.TickAsync(context, SemiAutoSkillPlan.FromSettings(settings.Skills), semiAuto, state);
            Check(!state.Fighting && state.CurrentTargetEntityId == 0, "no scenario has a confirmed target lock");
            Check(semiAuto.IsMaintenanceResting == (scenario == "low_hp"), "only low HP enters maintenance rest");
            Check((state.TopLevelState == StationaryCombatTopLevelState.DeathRecovery) == (scenario == "dead"),
                "death takes priority over other stationary phases");
            if (scenario is "ordinary_approach" or "defense_approach")
            {
                Check(state.CandidateEntityId == 200 &&
                      state.CurrentTargetIsMaintenanceDefense == (scenario == "defense_approach"),
                    "the approach preserves the selected candidate and its defense role");
            }
            else
            {
                Check(state.CandidateEntityId == 0, "no candidate is acquired without a target");
            }
            if (scenario != "low_hp") Check(!input.Keys.Contains("OemComma"), "non-maintenance phases must not sit");
        }
        finally { controller.StopWorkerBackgroundWork(context, state); stop.Cancel(); }
    }

    private static SkillSnapshot Skill() => new(11, "test", 1, 1, "test", 1, false, 1000, 0);
    private static async Task ExpectCanceled(Func<Task> action)
    {
        try { await action(); }
        catch (OperationCanceledException) { return; }
        throw new InvalidOperationException("Expected cancellation to propagate");
    }
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
