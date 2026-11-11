using Roadhog.Application.Radar;
using Roadhog.Application.StationaryCombat;
using Roadhog.Core.Common;
using Roadhog.Core.Api;
using Roadhog.Core.Model;
using Roadhog.Core.Radar;

internal static partial class TownReturnTests
{
    private static readonly string[] ReturnPhases = ["outside_radius", "no_target", "post_combat_return"];

    private static Harness BlockedReturn(ReturnClock clock, string phase, bool enclosed = false)
    {
        var map = new RadarMapDocument { MapId = 1 };
        if (enclosed)
        {
            var corners = new[] { new RadarPoint(95, -5), new RadarPoint(105, -5), new RadarPoint(105, 5), new RadarPoint(95, 5) };
            for (var i = 0; i < corners.Length; i++)
                map.Segments.Add(new() { Id = i.ToString(), Start = corners[i], End = corners[(i + 1) % corners.Length] });
        }
        else map.Segments.Add(new() { Id = "long-wall", Start = new(75, -100), End = new(75, 100) });
        var navigator = new StationaryObstacleNavigator(new StaticRadarMapStore(map), new RadarRoutePlanner(), new RadarMapRevisionRegistry());
        var h = new Harness(clock, navigator);
        h.Api.Player = Scene(50).Player!;
        h.Api.Channel = Scene(50).Channel!;
        h.Api.TransitionRead = () => Scene(50);
        h.State.MarkStartupRecoveryChecked();
        var combat = h.Context.Config.ScriptSettings!.Combat;
        combat.StationaryCombatRadius = phase == "outside_radius" ? 20 : 60;
        combat.ReturnHomeWhenNoTarget = phase == "no_target";
        combat.ReturnHomeAfterCombat = phase == "post_combat_return";
        combat.RadarObstacleAvoidance.Enabled = true;
        combat.RadarObstacleAvoidance.MaximumDetourExtraMeters = 30;
        h.State.PostCombatReturnPending = phase == "post_combat_return";
        return h;
    }

    private static void RequireBlocked(Harness h, string phase, string reason)
    {
        Check(h.State.ReturnNavigationFailure.Active && !h.State.IsMovingForward &&
            !h.Input.KeyDowns.Contains("W") && !h.Input.Keys.Contains("F8"), "blocked route waits without movement or early recall");
        Check(h.Logger.Entries.Any(e => e.EventName == "stationary_combat.radar.navigation" &&
            Equals(e.Fields["phase"], phase) && Equals(e.Fields["reason"], reason)), "fixture must exercise real navigation failure: " + phase + "/" + reason);
    }

    public static async Task UnreachableReturnBoundaryAsync()
    {
        foreach (var phase in ReturnPhases)
        foreach (var enclosed in new[] { false, true })
        {
            var clock = new ReturnClock();
            using var h = BlockedReturn(clock, phase, enclosed);
            await h.Tick();
            RequireBlocked(h, phase, enclosed ? "no_route" : "detour_limit_exceeded");
            clock.Advance(TimeSpan.FromMilliseconds(14999));
            await h.Tick();
            Check(!h.State.StuckTownReturnPending && !h.Input.Keys.Contains("F8"), "14.999 seconds cannot recall");
            clock.Advance(TimeSpan.FromMilliseconds(1));
            await h.Tick();
            Check(h.State.StuckTownReturnPending && h.Input.Keys.Count(k => k == "F8") == 1 &&
                !h.State.ReturnNavigationFailure.Active, "15 seconds triggers one bounded recovery: " + phase);
            h.Api.TransitionRead = Loading;
            await h.Tick();
            Check(h.Input.Keys.Count(k => k == "F8") == 1 && !h.Input.KeyDowns.Contains("W"), "pending recall owns the flow without replay");
            h.Api.TransitionRead = () => Scene(1);
            await h.Tick();
            Check(h.State.StartupRecoveryActive && h.State.StartupRecoveryPointIndex == 0 &&
                !h.State.StuckTownReturnPending, "confirmed landing reconnects the original route entry");
            Check(h.Logger.Entries.Count(e => e.EventName == "stationary_combat.return_home.navigation_failed_recovery") == 1,
                "one diagnostic per recovery, including failed reason and duration");
        }
    }

    public static async Task UnreachableReturnSuccessResetAsync()
    {
        foreach (var phase in ReturnPhases)
        {
            var clock = new ReturnClock();
            using var h = BlockedReturn(clock, phase);
            await h.Tick();
            clock.Advance(TimeSpan.FromSeconds(14));
            // Move to the same side of the wall as home: the next plan is genuinely direct.
            h.Api.Player = Scene(90).Player!;
            h.State.ObstacleNavigation.ClearRoute();
            await h.Tick();
            Check(!h.State.ReturnNavigationFailure.Active && !h.Input.Keys.Contains("F8"), "successful return plan clears previous failures");
            h.Api.Player = Scene(50).Player!;
            h.State.ObstacleNavigation.ClearRoute();
            clock.Advance(TimeSpan.FromSeconds(20));
            await h.Tick();
            Check(h.State.ReturnNavigationFailure.Active && !h.Input.Keys.Contains("F8"), "a new obstruction starts a fresh 15 seconds");
        }
    }

    public static async Task UnreachableReturnInterruptionAsync()
    {
        foreach (var phase in ReturnPhases)
        foreach (var interruption in new[] { "rest", "combat", "channel", "worker_takeover" })
        {
            var clock = new ReturnClock();
            using var h = BlockedReturn(clock, phase);
            await h.Tick();
            clock.Advance(TimeSpan.FromSeconds(20));
            switch (interruption)
            {
                case "rest":
                    h.Api.Player = h.Api.Player with { StanceFlags = 5, MotionMode = 1 };
                    await h.Tick();
                    h.Api.Player = h.Api.Player with { StanceFlags = 0, MotionMode = 0 };
                    break;
                case "combat":
                    h.State.Fighting = true;
                    h.State.CurrentTargetEntityId = 100;
                    h.State.CurrentTargetServerObjectId = 100;
                    h.Api.TargetEntityId = 100;
                    h.Api.TargetOwnServerObjectId = 100;
                    h.Api.TargetCurrentHp = h.Api.TargetMaxHp = 100;
                    h.Api.TargetPosition = h.Api.Player.Position;
                    h.Api.TargetIsTargetingLocalPlayer = true;
                    await h.Tick();
                    h.State.ClearTarget();
                    h.Api.TargetEntityId = 0;
                    break;
                case "channel":
                    await h.Controller.SetChannelSwitchPendingAsync(h.Context, h.State, true);
                    await h.Controller.TryTickChannelSwitchWaitAsync(h.Context, h.Plan, h.SemiState, h.State);
                    await h.Controller.SetChannelSwitchPendingAsync(h.Context, h.State, false);
                    break;
                case "worker_takeover":
                    // An entire worker tick was handled by cleanup/team/maintenance, without a controller call.
                    using (h.State.ReturnNavigationFailure.TrackTick()) { }
                    break;
            }
            Check(!h.State.ReturnNavigationFailure.Active && !h.Input.Keys.Contains("F8"), "interruption breaks the failure streak: " + phase + "/" + interruption);
            await h.Tick();
            Check(!h.Input.Keys.Contains("F8"), "resuming must not count time owned by other work");
        }
    }

    public static async Task UnreachableReturnFailureBackoffAsync()
    {
        foreach (var phase in ReturnPhases)
        foreach (var failure in new[] { "missing_key", "missing_path", "press_failed", "timeout" })
        {
            var clock = new ReturnClock();
            using var h = BlockedReturn(clock, phase);
            await h.Tick();
            if (failure == "missing_key") h.Context.Config.ScriptSettings!.Paths.TownReturnKey = "";
            if (failure == "missing_path") h.Context.Config.ScriptSettings!.Paths.RevivePathName = "missing";
            if (failure == "press_failed") h.Input.PressResult = _ => OperationResult.Fail("test recall failure");
            // Keep the already resolved home when testing a missing recovery route.
            h.Context.Config.ScriptSettings!.Combat.HasStationaryCombatPosition = true;
            h.Context.Config.ScriptSettings.Combat.StationaryCombatX = 100;
            clock.Advance(TimeSpan.FromSeconds(15));
            await h.Tick();
            if (failure == "timeout")
            {
                h.State.ReturnTransition!.Start(Scene(50), h.Path, DateTimeOffset.UtcNow.AddSeconds(-91));
                await h.Tick();
            }
            Check(!h.State.StuckTownReturnPending && !h.State.ReturningHome && !h.State.PostCombatReturningHome &&
                !h.State.ReturnNavigationFailure.Active && h.State.ReturnNavigationRetryAfter > DateTimeOffset.UtcNow,
                "recovery failure releases ownership and starts backoff: " + phase + "/" + failure);
            var keyCount = h.Input.Keys.Count;
            for (var i = 0; i < 3; i++) await h.Tick();
            Check(h.Input.Keys.Count == keyCount && !h.State.ReturnNavigationFailure.Active,
                "backoff prevents immediate replay of navigation or recall");
        }
    }

    public static async Task UnreachableReturnSceneAndStopAsync()
    {
        foreach (var mode in new[] { "death", "new_map", "loading", "stop", "stop_during_scene_read" })
        {
            var clock = new ReturnClock();
            using var h = BlockedReturn(clock, "outside_radius");
            await h.Tick();
            clock.Advance(TimeSpan.FromSeconds(15));
            if (mode == "death") h.Api.TransitionRead = () => Scene(50, hp: 0);
            if (mode == "new_map") h.Api.TransitionRead = () => Scene(50, map: 2);
            if (mode == "loading") h.Api.TransitionRead = Loading;
            if (mode == "stop") h.Stop.Cancel();
            if (mode == "stop_during_scene_read") h.Api.TransitionReadAsync = _ =>
            {
                h.Stop.Cancel();
                return Task.FromResult(Scene(50));
            };
            try { await h.Tick(); Check(!mode.StartsWith("stop"), "manual stop must cancel the tick"); }
            catch (OperationCanceledException) when (h.Stop.IsCancellationRequested) { }
            Check(!h.Input.Keys.Contains("F8") && !h.State.ReturnNavigationFailure.Active,
                "no stale recall after scene change or manual stop: " + mode);
            if (mode == "death") Check(h.State.TopLevelState == StationaryCombatTopLevelState.DeathRecovery, "death owns recovery");
        }
    }

    public static Task UnreachableReturnIdentityAndWorkerScopesAsync()
    {
        var clock = new ReturnClock();
        var state = new ReturnNavigationFailureState();
        var other = new ReturnNavigationFailureState();
        using (state.TrackTick())
        using (state.TrackTick()) state.Observe(1, new(100, 0), "outside_radius", clock);
        Check(state.Active && !other.Active, "nested worker/controller scopes preserve the observed account only");
        clock.Advance(TimeSpan.FromSeconds(16));
        Check(state.Observe(2, new(100, 0), "outside_radius", clock) == TimeSpan.Zero, "map change starts a new streak");
        clock.Advance(TimeSpan.FromSeconds(16));
        Check(state.Observe(2, new(120, 0), "outside_radius", clock) == TimeSpan.Zero, "new home starts a new streak");
        clock.Advance(TimeSpan.FromSeconds(16));
        Check(state.Observe(2, new(120, 0), "no_target", clock) == TimeSpan.Zero, "new return phase starts a new streak");
        using (state.TrackTick()) { }
        Check(!state.Active, "worker ticks that skip ordinary combat clear pending failure time");
        return Task.CompletedTask;
    }

    public static async Task UnreachableReturnReadYieldAndDisabledAsync()
    {
        foreach (var phase in ReturnPhases)
        {
            var clock = new ReturnClock();
            using var h = BlockedReturn(clock, phase);
            await h.Tick();
            clock.Advance(TimeSpan.FromSeconds(20));
            h.Api.PlayerReadFallback = OperationResult<PlayerSnapshot>.Fail("test unavailable player read");
            using (SnapshotReadBudget.Enter(TimeSpan.FromMilliseconds(50)))
            {
                try { await h.Tick(); throw new Exception("expected player read yield"); }
                catch (SnapshotReadBudgetExceededException) { }
            }
            Check(!h.State.ReturnNavigationFailure.Active && !h.Input.Keys.Contains("F8"), "read yield interrupts the failure streak");
            h.Api.PlayerReadFallback = null;
            await h.Tick();
            Check(h.State.ReturnNavigationFailure.Active && !h.Input.Keys.Contains("F8"), "published reads resume with a fresh failure window");
            h.Context.Config.ScriptSettings!.Combat.RadarObstacleAvoidance.Enabled = false;
            clock.Advance(TimeSpan.FromSeconds(20));
            await h.Tick();
            Check(!h.State.ReturnNavigationFailure.Active && !h.Input.Keys.Contains("F8"), "disabling radar preserves ordinary movement without stale recall");
        }
    }

    public static async Task UnreachableTargetDoesNotRecallAsync()
    {
        var clock = new ReturnClock();
        using var h = BlockedReturn(clock, "no_target", enclosed: true);
        h.Context.Config.ScriptSettings!.Combat.ReturnHomeWhenNoTarget = false;
        h.Api.WorldObjects = [new(100, 100, "enclosed-monster", "monster", new(100, 0, 0), 1, 100, 100)];
        await h.Tick();
        clock.Advance(TimeSpan.FromSeconds(30));
        await h.Tick();
        Check(h.Logger.Entries.Any(e => e.EventName == "stationary_combat.target.route_scoring_timing" &&
            Equals(e.Fields["candidateCount"], 1)), "fixture must score the enclosed candidate through ordinary selection");
        Check(!h.State.ReturnNavigationFailure.Active && !h.State.StuckTownReturnPending && !h.Input.Keys.Contains("F8"),
            "ordinary unreachable monster selection must never start return recovery");
        Check(!h.Logger.Entries.Any(e => e.EventName == "stationary_combat.return_home.navigation_failed_recovery"),
            "no return-recovery diagnostic outside a return branch");
    }
}
