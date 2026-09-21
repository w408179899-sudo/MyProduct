using Roadhog.Application;
using Roadhog.Application.BagCleanup;
using Roadhog.Application.SemiAuto;
using Roadhog.Application.StationaryCombat;
using Roadhog.Application.Travel;
using Roadhog.Application.Workers;
using Roadhog.Core.Accounts;
using Roadhog.Core.Diagnostics;
using Roadhog.Core.Model;
using Roadhog.Core.Paths;
using Roadhog.Infrastructure.Paths;

internal static class TownReturnTests
{
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static ChannelTransitionSnapshot Scene(float x, uint map = 1, uint hp = 100, float z = 0) =>
        new(true, new(7, 0, "recall", hp, 100, 100, 100, 0, new(x, 0, z), DateTimeOffset.UtcNow, 90, 10, 90),
            new(0, 1, map, DateTimeOffset.UtcNow), DateTimeOffset.UtcNow);
    private static ChannelTransitionSnapshot Loading() => new(false, null, null, DateTimeOffset.UtcNow);
    private static SharedPathDocument Route(uint? map = 1) => new()
    { Name = "return", MapId = map, Points = new() { new() { X = 0 }, new() { X = 100 } } };

    public static Task DestinationMatrixAsync()
    {
        foreach (var map in new uint[] { 1, 2 })
        {
            foreach (var loading in new[] { false, true })
            {
                var operation = new TownReturnTransition();
                operation.Start(Scene(1000), Route(map), DateTimeOffset.UtcNow.AddSeconds(-24));
                Check(operation.Observe(Scene(1000)) == TownReturnPhase.WaitingForDeparture, "old position after 24s is still pending");
                if (loading) Check(operation.Observe(Loading()) == TownReturnPhase.Loading, "loading retains ownership");
                Check(operation.Observe(Scene(0, 3)) == TownReturnPhase.WaitingForDestination, "matching coordinates on wrong map cannot arrive");
                Check(operation.Observe(Scene(0, map, z: 70)) == TownReturnPhase.WaitingForDestination, "wrong height cannot arrive");
                Check(operation.Observe(Scene(0, map)) == TownReturnPhase.Arrived, "same-map and cross-map arrivals work even without a sampled loading frame");
            }
        }
        var sameCoordinates = new TownReturnTransition();
        sameCoordinates.Start(Scene(0), Route(2), DateTimeOffset.UtcNow);
        Check(sameCoordinates.Observe(Scene(0, 2)) == TownReturnPhase.Arrived, "different maps may use the same coordinates");
        var alreadyThere = new TownReturnTransition();
        alreadyThere.Start(Scene(0), Route(), DateTimeOffset.UtcNow);
        Check(alreadyThere.Observe(Scene(0)) == TownReturnPhase.WaitingForDeparture, "unchanged baseline is not action confirmation");
        alreadyThere.Observe(Loading());
        Check(alreadyThere.Observe(Scene(0)) == TownReturnPhase.Arrived, "same-position recall can confirm a real scene transition");
        return Task.CompletedTask;
    }

    public static async Task StartupDelayedLandingAsync()
    {
        using var h = new Harness();
        await h.Tick();
        Check(h.State.StartupTownReturnPending && h.Input.Keys.Count(k => k == "F8") == 1, "startup submits exactly one return");
        h.State.ReturnTransition!.Start(Scene(1000), h.Path, DateTimeOffset.UtcNow.AddSeconds(-24));
        await h.Tick();
        Check(h.State.StartupTownReturnPending && !h.Input.KeyDowns.Contains("W"), "24-second delay cannot fall through to direct home movement");
        var playerReads = h.Api.PlayerReadCount;
        h.Api.TransitionRead = Loading;
        h.State.ReturnTransition.Start(Scene(1000), h.Path, DateTimeOffset.UtcNow.AddSeconds(-75));
        for (var i = 0; i < 3; i++) await h.Tick();
        Check(h.Api.PlayerReadCount == playerReads && !h.Input.Keys.Contains("Space"), "loading reads scene only and never jumps");
        Check(h.Logger.Entries.Count(e => e.EventName == "town_return.waiting") == 1, "slow loading reports once and keeps observing");
        h.Api.TransitionRead = () => Scene(0, 9);
        await h.Tick();
        Check(h.State.StartupTownReturnPending && !h.State.StartupRecoveryActive, "wrong-map arrival stays held");
        h.Api.TransitionRead = () => Scene(2);
        await h.Tick();
        Check(!h.State.StartupTownReturnPending && h.State.StartupRecoveryActive && h.State.StartupRecoveryPointIndex == 0,
            "late landing reattaches at route entry instead of running directly home");
        Check(h.Input.Keys.Count(k => k == "F8") == 1 && !h.Input.KeyDowns.Contains("W"), "confirmation never resubmits or moves before handoff");
    }

    public static async Task DeathAndCancellationAsync()
    {
        using (var h = new Harness())
        {
            await h.Tick();
            h.Api.TransitionRead = () => Scene(1000, hp: 0);
            await h.Tick();
            Check(h.State.TopLevelState == StationaryCombatTopLevelState.DeathRecovery && !h.State.StartupTownReturnPending,
                "official scene death cancels recall and hands off to death recovery");
        }
        using (var h = new Harness())
        {
            await h.Tick();
            var pending = new TaskCompletionSource<ChannelTransitionSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
            h.Api.TransitionReadAsync = _ => pending.Task;
            var reads = h.Api.TransitionReadCount;
            var work = h.Tick();
            h.Stop.Cancel();
            try { await work.WaitAsync(TimeSpan.FromSeconds(2)); throw new Exception("expected cancellation"); }
            catch (OperationCanceledException) { }
            finally { pending.TrySetResult(Loading()); }
            Check(h.Api.TransitionReadCount == reads + 1, "one pending provider request and prompt stop");
        }
    }

    public static async Task CleanupBothReturnsAsync()
    {
        foreach (var afterCleanup in new[] { false, true })
        {
            using var h = new Harness();
            var controller = new BagCleanupController(h.Input, h.Paths, (_, _, _) => throw new Exception("must not walk during transition"));
            var state = new BagCleanupState();
            state.Start(0, 0);
            state.Advance(afterCleanup ? BagCleanupStep.PressReturnToRevive : BagCleanupStep.PressTownReturn);
            await controller.TickAfterLootAsync(h.Context, state);
            h.Api.TransitionRead = Loading;
            h.Api.TargetIsTargetingLocalPlayer = true;
            var playerReads = h.Api.PlayerReadCount;
            var targetReads = h.Api.WorldObjectReadCount;
            await controller.TickAfterLootAsync(h.Context, state);
            Check(h.Api.PlayerReadCount == playerReads && h.Api.WorldObjectReadCount == targetReads,
                "loading must not inspect combat from the old map");
            Check(!h.Input.Keys.Contains("Escape") && !h.Input.KeyDowns.Contains("W"), "loading cannot be canceled by stale attackers");
            h.Api.TransitionRead = () => Scene(0, 2);
            await controller.TickAfterLootAsync(h.Context, state);
            Check(state.ReturnTransition!.Active, "cleanup cannot complete on wrong-map coordinates");
            h.Api.TransitionRead = () => Scene(1);
            var result = await controller.TickAfterLootAsync(h.Context, state);
            Check(afterCleanup ? result.Status == BagCleanupTickStatus.Completed : state.Step == BagCleanupStep.LoadCleanupPath,
                "both cleanup branches require proper arrival");
        }
    }

    public static async Task WorkerOwnsTransitionAsync()
    {
        using var h = new Harness();
        var skills = h.Context.Config.ScriptSettings!.Skills;
        skills.SpiritmasterAutoSkillLogicEnabled = true;
        skills.Spiritmaster.SummonSkills.Add(new() { Key = "F7" });
        h.Api.Player = h.Api.Player with { CharacterClassId = AionClassId.Spiritmaster };
        var roster = h.Api.SummonedPetRoster;
        h.Api.SummonedPetRoster = roster with
        {
            LocalServerObjectId = 77, LocalLinkedPetServerObjectId = 8,
            LocalPlayerPet = roster.LocalPlayerPet with
            { OwnerServerObjectId = 77, Pet = roster.LocalPlayerPet.Pet with
                { IsSummoned = true, CurrentHp = 100, MaxHp = 100, HpPercent = 100, EntityId = 8, ServerObjectId = 8,
                  LocalServerObjectId = 77, LocalLinkedPetServerObjectId = 8, OwnerConfirmed = true, HealthFields = new(true, true, true) } }
        };
        var submitted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var observed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var count = 0;
        h.Input.AfterPress = key =>
        {
            if (key != "F8") return;
            h.Api.SummonedPetRoster = SummonedPetRosterSnapshot.Empty(0, DateTimeOffset.UtcNow);
            submitted.TrySetResult();
        };
        h.Api.TransitionRead = () =>
        {
            if (!submitted.Task.IsCompleted) return Scene(1000);
            if (++count == 3) observed.TrySetResult();
            return Loading();
        };
        var loop = new DefaultAccountWorkerLoop(h.Input, h.SemiAuto, h.Controller);
        var work = loop.RunAsync(h.Context);
        try
        {
            await observed.Task.WaitAsync(TimeSpan.FromSeconds(8));
            Check(h.Input.Keys.All(k => k == "F8") && !h.Input.KeyDowns.Contains("W"), "worker must not summon, attack or walk while recall owns it");
        }
        catch (TimeoutException)
        {
            throw new Exception("worker did not enter recall: " + string.Join(",", h.Input.Keys) + "; " +
                string.Join(";", h.Logger.Entries.TakeLast(8).Select(e => e.EventName + "=" + System.Text.Json.JsonSerializer.Serialize(e.Fields))));
        }
        finally
        {
            h.Stop.Cancel();
            try { await work.WaitAsync(TimeSpan.FromSeconds(3)); } catch (OperationCanceledException) { }
        }
        Check(h.Input.KeyUps.Contains("W"), "worker stop releases input");
    }

    public static async Task PathMapRoundTripAsync()
    {
        var buffer = new PathRecordingBuffer();
        Check(buffer.AcceptRecordingMap(2), "first recorded map accepted");
        buffer.TryAdd(new(0, 0, 0), DateTimeOffset.UtcNow);
        Check(!buffer.AcceptRecordingMap(3), "cross-map recording must not connect unrelated coordinates");
        var folder = Path.Combine(Path.GetTempPath(), "roadhog-return-path-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new JsonSharedPathStore(folder);
            Check((await store.SaveAsync(buffer.ToDocument("map"))).Success, "save route");
            var read = (await store.LoadAsync("map")).Value!;
            Check(read.MapId == 2 && read.Clone().MapId == 2, "map survives normalization, persistence and cloning");
            buffer.Load(read.Points, read.MapId);
            Check(buffer.ToDocument("edited").MapId == 2, "editing retains map");
            buffer.Load(read.Points);
            buffer.AcceptRecordingMap(9);
            Check(buffer.MapId == null, "legacy route cannot silently acquire the current map");
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }

    public static async Task StuckRecoveryIsBoundedAsync()
    {
        using var h = new Harness();
        h.Api.TransitionRead = () => Scene(10);
        h.State.IsMovingForward = true;
        var recover = typeof(StationaryCombatController).GetMethod("RecoverBlockedReturnRouteAsync",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        await (Task)recover.Invoke(h.Controller, new object[] { h.Context, h.State })!;
        Check(h.State.StartupRecoveryActive && h.State.StartupRecoveryPointIndex == 0 && h.State.ReturnRouteRejoinAttempted,
            "a nearby matching route can be rejoined once after sustained lack of progress");
        Check(h.Input.KeyUps.Contains("W") && !h.Input.Keys.Contains("Space"), "recovery releases forward instead of another jump");
        await (Task)recover.Invoke(h.Controller, new object[] { h.Context, h.State })!;
        Check(h.State.ReturnNavigationBlocked, "repeated failure must stop rather than restart the same route forever");
        var commands = h.Input.KeyDowns.Count + h.Input.Keys.Count;
        await h.Tick();
        Check(h.Input.KeyDowns.Count + h.Input.Keys.Count == commands, "blocked return cannot resume walking through normal work");
        h.Api.TransitionRead = () => Scene(10, hp: 0);
        await h.Tick();
        Check(!h.State.ReturnNavigationBlocked && h.State.TopLevelState == StationaryCombatTopLevelState.DeathRecovery,
            "blocked navigation still hands a confirmed death to recovery");
    }

    private sealed class Harness : IDisposable
    {
        public readonly CancellationTokenSource Stop = new(TimeSpan.FromSeconds(15));
        public readonly FakeGameApi Api = new() { Player = Scene(1000).Player!, TargetEntityId = 0, Skills = Array.Empty<SkillSnapshot>(), WorldObjects = Array.Empty<WorldObjectSnapshot>() };
        public readonly RecordingKeyboardInput Input = new();
        public readonly InMemoryRoadhogLogger Logger = new();
        public readonly StationaryCombatState State = new();
        public readonly SemiAutoCombatState SemiState = new();
        public readonly SharedPathDocument Path = Route();
        public readonly InMemorySharedPathStore Paths;
        public readonly SemiAutoCombatController SemiAuto;
        public readonly StationaryCombatController Controller;
        public readonly AccountWorkerContext Context;
        public readonly SemiAutoSkillPlan Plan;
        public Harness()
        {
            var settings = new ScriptSettings { MainMode = AccountMainMode.CustomCombat, CombatMode = AccountCombatMode.Stationary };
            settings.Paths.RevivePathName = settings.Paths.MaintenancePathName = "return";
            settings.Paths.TownReturnKey = settings.Paths.BagCleanupTownReturnKey = "F8";
            settings.Combat.StationaryCombatRadius = 20;
            var config = new AccountConfig { AccountName = "recall", MainMode = settings.MainMode, ScriptSettings = settings };
            Paths = new(Path);
            SemiAuto = new(Input);
            Controller = new(Input, SemiAuto, Paths);
            Context = new(config, Api, Logger, new AccountRuntimeManager(Logger), new(), Stop.Token);
            Plan = SemiAutoSkillPlan.FromSettings(settings.Skills);
        }
        public Task Tick() => Controller.TickAsync(Context, Plan, SemiState, State);
        public void Dispose() { Stop.Cancel(); Stop.Dispose(); }
    }
}
