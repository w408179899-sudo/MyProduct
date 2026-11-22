using Roadhog.Application;
using Roadhog.Application.Radar;
using Roadhog.Application.SemiAuto;
using Roadhog.Application.StationaryCombat;
using Roadhog.Application.Workers;
using Roadhog.Core.Accounts;
using Roadhog.Core.Common;
using Roadhog.Core.Diagnostics;
using Roadhog.Core.Model;
using Roadhog.Core.Radar;
using Roadhog.Infrastructure.Radar;

internal static class StationaryTabFacingTests
{
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    public static async Task DetourInvalidatesPreAimAsync()
    {
        using var h = new Harness(radar: true);
        // Pre-aim had faced the candidate before navigation turned toward a waypoint.
        h.State.FacedCandidateEntityId = 100;
        var route = await h.Navigator!.ResolveAsync(h.State.ObstacleNavigation, 777,
            new(0, 0), new(20, 0), RadarNavigationPurpose.ApproachTarget, 9000,
            h.Settings.Combat.RadarObstacleAvoidance, 34);
        Check(route.Action == RadarNavigationAction.MoveToWaypoint, "fixture must require a detour");
        // The waypoint has been passed; the line is now clear but the camera still faces the waypoint.
        h.SetPlayer(new(12, 0, 0), 0);
        h.State.ObstacleNavigation.CommitDirectApproach(9000);
        await h.Tick();
        Check(h.Input.MouseCommands.Any(c => c.StartsWith("move:")), "must turn after the detour despite the old pre-aim marker");
        Check(!h.Input.Keys.Contains("Tab"), "unconfirmed camera correction must not send Tab");
        Check(h.State.FacedCandidateEntityId == 0, "failed recheck invalidates the old facing marker");
        Check(!h.State.Fighting, "must not enter combat without the intended lock");
    }

    public static async Task TurnsBeforeTabAsync()
    {
        using var h = new Harness();
        h.SetPlayer(new(0, 0, 0), 0);
        h.State.FacedCandidateEntityId = 100;
        var moved = false;
        h.Input.AfterMove = (_, _) => { moved = true; h.SetPlayer(new(0, 0, 0), 90); };
        h.Input.AfterPress = key =>
        {
            if (key == "Tab") Check(moved && h.Api.Player.CameraYawDegrees == 90, "camera must align before Tab");
        };
        await h.Tick();
        Check(h.Input.Keys.Contains("Tab"), "confirmed correction should allow Tab in the same attempt");
        Check(h.State.FacedCandidateEntityId == 100, "confirmed facing refreshes the candidate marker");
    }

    public static async Task PendingRetryTracksCandidateAsync()
    {
        using var h = new Harness();
        h.SetPlayer(new(0, 0, 0), 90);
        h.State.FacedCandidateEntityId = 100;
        h.LockWrongMonster();
        await h.Tick();
        Check(h.Input.Keys.Count(k => k == "Tab") == 1, "fixture must first enter pending Tab verification: " +
            string.Join(" | ", h.Logger.Entries.Select(e => e.EventName + "=" + System.Text.Json.JsonSerializer.Serialize(e.Fields))));
        Check(!h.State.Fighting, "wrong live monster must not be attacked");

        // The real candidate moves around the player; the wrong lock stays put. Follow only the candidate.
        h.Api.WorldObjects = new[] { h.Candidate with { Position = new(0, 20, 0) }, h.WrongMonster };
        h.State.LastTabAt = DateTimeOffset.MinValue;
        h.State.StartPendingTabVerification(h.Candidate, DateTimeOffset.Now.AddSeconds(-1), null);
        await h.Tick();
        Check(h.Input.Keys.Count(k => k == "Tab") == 1, "pending retry must not Tab before the moved candidate is faced");
        Check(h.Logger.Entries.Any(e => e.EventName == "stationary_combat.face_target" &&
            Equals(e.Fields["targetEntityId"], (ushort)100) && Convert.ToDouble(e.Fields["targetYaw"]) == 180),
            "retry must aim at the fresh candidate position, not the wrong locked monster: " +
            string.Join(" | ", h.Logger.Entries.Where(e => e.EventName.Contains("face")).Select(e => e.EventName + "=" + System.Text.Json.JsonSerializer.Serialize(e.Fields))));
        Check(!h.State.Fighting, "wrong lock protection must survive reorientation");
        h.SetPlayer(new(0, 0, 0), 180);
        await h.Tick();
        Check(h.Input.Keys.Count(k => k == "Tab") == 2, "retry resumes once the true candidate is aligned");
        Check(!h.State.Fighting, "an aligned retry still cannot attack a different live lock");
    }

    public static async Task AlignedDoesNotTurnAsync()
    {
        using var h = new Harness();
        h.SetPlayer(new(0, 0, 0), 90);
        h.State.FacedCandidateEntityId = 100;
        await h.Tick();
        Check(h.Input.Keys.Count(k => k == "Tab") == 1, "aligned acquisition should Tab immediately");
        Check(!h.Input.MouseCommands.Any(c => c.StartsWith("move:")), "aligned acquisition must not move the camera");
    }

    public static async Task BlockedRadarDoesNotFaceOrTabAsync()
    {
        using var h = new Harness(radar: true);
        h.State.FacedCandidateEntityId = 100;
        h.SetPlayer(new(0, 0, 0), 0);
        // Simulate a stale direct commitment: the final strict radar check must take priority.
        await h.Navigator!.ResolveAsync(h.State.ObstacleNavigation, 777, new(0, 0), new(20, 0),
            RadarNavigationPurpose.ApproachTarget, 9000, h.Settings.Combat.RadarObstacleAvoidance, 34);
        h.State.ObstacleNavigation.CommitDirectApproach(9000);
        await h.Tick();
        Check(h.Logger.Entries.Any(e => e.EventName == "stationary_combat.tab.blocked_by_obstacle"), "strict radar guard must run");
        Check(!h.Input.Keys.Contains("Tab"), "blocked line must not Tab");
        Check(!h.Logger.Entries.Any(e => e.EventName == "stationary_combat.face_target"), "do not turn toward the monster before the detour is resolved");
    }

    public static async Task MatchingLockSkipsTabFacingAsync()
    {
        using var h = new Harness();
        h.SetPlayer(new(0, 0, 0), 0);
        h.Api.TargetEntityId = 100;
        h.Api.TargetOwnServerObjectId = 9000;
        h.Api.TargetCurrentHp = h.Api.TargetMaxHp = 100;
        h.Api.TargetPosition = h.Candidate.Position;
        await h.Tick();
        Check(h.State.Fighting, "matching lock should enter the existing combat flow");
        Check(!h.Input.Keys.Contains("Tab"), "matching lock needs no Tab");
        Check(!h.Input.MouseCommands.Any(c => c.StartsWith("move:")), "the acquisition fix must not reorient an already locked fight");
    }

    public static async Task TabVerifiesFirstSnapshotWithoutWaitingAsync()
    {
        using var h = new Harness();
        h.SetEnvironment("ROADHOG_STATIONARY_TAB_VERIFY_POLL_MS", "500");
        h.SetEnvironment("ROADHOG_STATIONARY_TAB_VERIFY_WINDOW_MS", "300");
        h.Input.AfterPress = key =>
        {
            if (key == "Tab") h.SetLockedMonster(h.Candidate);
        };

        await h.Tick();

        Check(h.State.Fighting && h.State.CurrentTargetServerObjectId == h.Candidate.ServerObjectId,
            "the first post-Tab publication should acquire the intended monster before the next polling interval");
        Check(h.Input.Keys.Count(key => key == "Tab") == 1, "matching the candidate must stop tabbing");
    }

    public static async Task PetAttackWaitsForFightTargetAsync(int matchingReads)
    {
        using var h = new Harness();
        h.Api.Player = h.Api.Player with { CharacterClassId = AionClassId.Spiritmaster };
        h.Settings.SemiAuto.AttackKeyLoopEnabled = false;
        h.Settings.Skills.SpiritmasterAutoSkillLogicEnabled = true;
        h.Settings.Skills.Spiritmaster.OpeningAttackKey = "D7";
        h.Settings.Skills.Spiritmaster.SummonSkills.Clear();
        h.State.BeginFight(h.Candidate);
        h.LockWrongMonster();
        h.Api.TargetName = h.Candidate.Name;
        // A matching lock can change either at the skill tick handoff or just before the first key.
        for (var read = 0; read < matchingReads; read++)
        {
            h.Api.LockedTargetReadResults.Enqueue(OperationResult<LockedTargetSnapshot>.Ok(
                new(h.Candidate.EntityId, h.Candidate.ServerObjectId, 0, LockedTargetSnapshot.MonsterObjectType,
                    h.Candidate.Name, 100, 100, h.Candidate.Position, 20, DateTimeOffset.Now)));
        }

        await h.Tick();

        Check(!h.Input.Keys.Contains("D7"), "a different live lock must not receive the pet opening command");
        Check(h.State.CurrentTargetServerObjectId == h.Candidate.ServerObjectId,
            "the fight must keep the intended target while the lock is wrong");
        var phase = matchingReads == 1 ? "skill_tick" : "pet_attack_first";
        Check(h.Logger.Entries.Any(entry => entry.EventName == "semi_auto.combat_target.lock_mismatch" &&
            Equals(entry.Fields["phase"], phase) && Equals(entry.Fields["lockedServerObjectId"], h.WrongMonster.ServerObjectId)),
            "the lock mismatch must be caught at the expected input boundary");

        h.Input.AfterPress = key =>
        {
            if (key == "Tab") h.SetLockedMonster(h.Candidate);
        };
        await h.Tick();

        Check(h.Input.Keys.SequenceEqual(new[] { "Tab", "D7" }),
            "the pet command should run only after Tab restores the intended lock: " + string.Join(", ", h.Input.Keys));
    }

    public static async Task PetAttackStopsWhenLockChangesBeforeFollowUpAsync()
    {
        using var h = new Harness();
        h.Api.Player = h.Api.Player with { CharacterClassId = AionClassId.Spiritmaster };
        h.Settings.SemiAuto.AttackKeyLoopEnabled = false;
        h.Settings.Skills.SpiritmasterAutoSkillLogicEnabled = true;
        h.Settings.Skills.Spiritmaster.OpeningAttackKey = "D7";
        h.Input.AfterPress = key =>
        {
            if (key == "Tab") h.SetLockedMonster(h.Candidate);
            if (key == "D7") h.SetLockedMonster(h.WrongMonster);
        };

        await h.Tick();

        Check(h.Input.Keys.SequenceEqual(new[] { "Tab", "D7" }),
            "further input must be cancelled when the lock changes: " + string.Join(", ", h.Input.Keys));
        Check(h.Logger.Entries.Any(entry => entry.EventName == "semi_auto.combat_target.lock_mismatch" &&
            Equals(entry.Fields["phase"], "after_pet_attack")),
            "the follow-up scheduler checks the lock after the pet command");
        Check(h.State.CurrentTargetServerObjectId == h.Candidate.ServerObjectId,
            "the wrong lock must not replace the intended fight target");
    }

    public static async Task OldLockWaitsForTargetChangeAsync()
    {
        using var h = new Harness();
        h.SetEnvironment("ROADHOG_STATIONARY_TAB_VERIFY_POLL_MS", "1");
        h.SetLockedMonster(h.WrongMonster);
        h.Api.WorldObjects = new[] { h.Candidate, h.WrongMonster };
        int worldReadsWhenTabSent = 0;
        h.Input.AfterPress = key =>
        {
            if (key != "Tab") return;
            worldReadsWhenTabSent = h.Api.WorldObjectReadCount;
            // This publication still shows the pre-Tab target. The next publication shows the new lock.
            h.Api.LockedTargetReadResults.Enqueue(OperationResult<LockedTargetSnapshot>.Ok(
                new(h.WrongMonster.EntityId, h.WrongMonster.ServerObjectId, 0, LockedTargetSnapshot.MonsterObjectType,
                    h.WrongMonster.Name, 100, 100, h.WrongMonster.Position, 10, DateTimeOffset.Now)));
            h.SetLockedMonster(h.Candidate);
        };

        await h.Tick();

        Check(h.State.CurrentTargetServerObjectId == h.Candidate.ServerObjectId,
            "an unchanged pre-Tab lock must give the game time to publish the intended monster");
        Check(h.Api.WorldObjectReadCount == worldReadsWhenTabSent,
            "the held pre-Tab lock must not start defense lookups");
    }

    public static async Task WrongLockEndsVerificationAndKeepsTabThrottleAsync()
    {
        using var h = new Harness();
        h.SetEnvironment("ROADHOG_STATIONARY_TAB_VERIFY_WINDOW_MS", "2000");
        h.Api.WorldObjects = new[] { h.Candidate, h.WrongMonster };
        int worldReadsWhenTabSent = 0;
        h.Input.AfterPress = key =>
        {
            if (key != "Tab") return;
            worldReadsWhenTabSent = h.Api.WorldObjectReadCount;
            h.SetLockedMonster(h.WrongMonster);
        };

        await h.Tick();

        Check(h.State.CanRetryPendingTab(DateTimeOffset.Now), "a confirmed wrong lock must end the wait");
        Check(DateTimeOffset.Now < h.State.PendingTabVerifyUntil, "a confirmed wrong lock should finish before the deadline");
        Check(h.Api.WorldObjectReadCount == worldReadsWhenTabSent + 1, "check the new wrong lock for defense only once");
        Check(!h.State.Fighting && h.Input.Keys.SequenceEqual(new[] { "Tab" }),
            "rejecting a wrong lock must leave the candidate intact and send no combat input");
        h.State.LastTabAt = DateTimeOffset.Now.AddSeconds(1);
        await h.Tick();
        Check(h.Input.Keys.Count(key => key == "Tab") == 1, "an early miss cannot bypass the Tab throttle");

        h.State.LastTabAt = DateTimeOffset.MinValue;
        h.Input.AfterPress = key =>
        {
            if (key == "Tab") h.SetLockedMonster(h.Candidate);
        };
        await h.Tick();
        Check(h.State.Fighting && h.Input.Keys.Count(key => key == "Tab") == 2,
            "after the throttle the next Tab should be able to acquire the original candidate");
    }

    public static async Task SlowTabVerificationReadReleasesTheTickAsync(bool checkingDefense)
    {
        using var h = new Harness();
        h.SetEnvironment("ROADHOG_STATIONARY_TAB_VERIFY_WINDOW_MS", "150");
        var providerStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var providerCancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        h.Api.WorldObjects = new[] { h.Candidate, h.WrongMonster };
        h.Input.AfterPress = key =>
        {
            if (key != "Tab") return;
            if (checkingDefense)
            {
                h.SetLockedMonster(h.WrongMonster);
                h.Api.WorldObjectReadOverride = token => WaitForDeadlineAsync<IReadOnlyList<WorldObjectSnapshot>>(token);
            }
            else
            {
                h.Api.LockedTargetReadOverride = WaitForDeadlineAsync<LockedTargetSnapshot>;
            }
        };

        await h.Tick().WaitAsync(TimeSpan.FromSeconds(1));
        await providerStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));
        await providerCancelled.Task.WaitAsync(TimeSpan.FromSeconds(1));
        Check(h.Logger.Entries.Any(e => e.EventName == "stationary_combat.tab.verify_timeout"),
            "the same action deadline must apply while the snapshot provider is waiting");
        Check(!h.State.Fighting && h.Input.Keys.SequenceEqual(new[] { "Tab" }),
            "a timeout must return to the worker without inventing a lock or replaying Tab");

        h.Api.LockedTargetReadOverride = null;
        h.Api.WorldObjectReadOverride = null;
        h.SetLockedMonster(h.Candidate);
        await h.Tick();
        Check(h.State.Fighting && h.Input.Keys.Count(key => key == "Tab") == 1,
            "the next tick must still be able to accept the late lock without another Tab");

        async Task<OperationResult<T>> WaitForDeadlineAsync<T>(CancellationToken token)
        {
            providerStarted.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                throw new InvalidOperationException("The read should only exit by cancellation.");
            }
            finally
            {
                providerCancelled.TrySetResult();
            }
        }
    }

    public static async Task DefenseInvalidatesOldFacingAsync()
    {
        using var h = new Harness();
        StartPetDefense(h);
        h.SetPlayer(new(0, 0, 0), 0);
        await h.Tick();
        Check(h.Input.MouseCommands.Any(c => c.StartsWith("move:")), "pet defense must recheck a previously faced target after camera movement");
        Check(!h.Input.Keys.Contains("Tab"), "unconfirmed defense facing must not send Tab");
        Check(h.State.FacedCandidateEntityId == 0, "failed defense facing must invalidate the old marker");
        Check(h.State.CurrentTargetServerObjectId == 9000 && h.State.CurrentTargetIsMaintenanceDefense,
            "failed facing must retain the pet attacker and defense task");
    }

    public static async Task DefenseRetryTracksMovingAttackerAsync()
    {
        using var h = new Harness();
        StartPetDefense(h);
        await h.Tick();
        Check(h.Input.Keys.Count(k => k == "Tab") == 1, "aligned defense must first attempt Tab");

        h.Api.WorldObjects = new[] { h.Candidate with { Position = new(0, 20, 0), TargetServerObjectId = 2000 }, h.WrongMonster };
        h.State.LastTabAt = DateTimeOffset.MinValue;
        h.State.StartPendingTabVerification(h.Candidate, DateTimeOffset.Now.AddSeconds(-1), null);
        await h.Tick();
        Check(h.Input.Keys.Count(k => k == "Tab") == 1, "wrong-lock retry must wait for the moved pet attacker to be faced");
        Check(h.Logger.Entries.Any(e => e.EventName == "stationary_combat.face_target" &&
            Equals(e.Fields["targetEntityId"], (ushort)100) && Convert.ToDouble(e.Fields["targetYaw"]) == 180),
            "defense retry must face the moved attacker, not the unrelated locked monster");
        h.SetPlayer(new(0, 0, 0), 180);
        await h.Tick();
        Check(h.Input.Keys.Count(k => k == "Tab") == 2, "defense retry resumes after facing is confirmed");
        Check(h.Input.Keys.All(k => k == "Tab") && h.State.CurrentTargetServerObjectId == 9000,
            "repeated wrong locks must not attack or adopt an unrelated monster");

        h.State.LastTabAt = DateTimeOffset.MinValue;
        h.State.StartPendingTabVerification(h.Candidate, DateTimeOffset.Now.AddSeconds(-1), null);
        h.SetPlayer(new(0, 0, 0), 0);
        await h.Tick();
        Check(h.Input.Keys.Count(k => k == "Tab") == 2, "every retry must recheck facing even after a successful previous correction");
    }

    public static async Task DefenseConfirmsCorrectionBeforeTabAsync()
    {
        using var h = new Harness();
        StartPetDefense(h);
        h.SetPlayer(new(0, 0, 0), 0);
        var moved = false;
        h.Input.AfterMove = (_, _) => { moved = true; h.SetPlayer(new(0, 0, 0), 90); };
        h.Input.AfterPress = key =>
        {
            if (key != "Tab") return;
            Check(moved && h.Api.Player.CameraYawDegrees == 90, "defense must align before sending Tab");
            h.Api.TargetEntityId = 100;
            h.Api.TargetOwnServerObjectId = 9000;
            h.Api.TargetServerObjectId = 2000;
            h.Api.TargetPosition = h.Candidate.Position;
        };
        await h.Tick();
        Check(h.Input.Keys.Contains("Tab"), "confirmed defense turn must resume acquisition");
        Check(h.Logger.Entries.Any(e => e.EventName == "stationary_combat.target.acquired"),
            "the corrected Tab must still acquire the pet attacker");
        Check(h.State.CurrentTargetServerObjectId == 9000 && h.State.Fighting, "successful defense acquisition must retain the attacker: " +
            string.Join(" | ", h.Logger.Entries.Where(e => e.EventName.Contains("target")).Select(e => e.EventName + "=" + System.Text.Json.JsonSerializer.Serialize(e.Fields))));
    }

    public static async Task DefenseDetourInvalidatesFacingAsync()
    {
        using var h = new Harness(radar: true);
        StartPetDefense(h);
        var route = await h.Navigator!.ResolveAsync(h.State.ObstacleNavigation, 777,
            new(0, 0), new(20, 0), RadarNavigationPurpose.ApproachTarget, 9000,
            h.Settings.Combat.RadarObstacleAvoidance, 34);
        Check(route.Action == RadarNavigationAction.MoveToWaypoint, "defense fixture must require a detour");
        h.SetPlayer(new(12, 0, 0), 0);
        h.State.ObstacleNavigation.CommitDirectApproach(9000);
        await h.Tick();
        Check(h.Input.MouseCommands.Any(c => c.StartsWith("move:")), "defense must reface the attacker after a detour");
        Check(!h.Input.Keys.Contains("Tab"), "defense detour must not reuse its old facing for Tab");
    }

    public static async Task DefenseAlignedAvoidsExtraTurnAsync()
    {
        using var h = new Harness();
        StartPetDefense(h);
        await h.Tick();
        Check(h.Input.Keys.Count(k => k == "Tab") == 1, "aligned pet defense should Tab immediately");
        Check(!h.Input.MouseCommands.Any(c => c.StartsWith("move:")), "aligned pet defense must not add camera movement");
    }

    public static async Task DefenseRadarRetainsPriorityAsync()
    {
        using var h = new Harness(radar: true);
        StartPetDefense(h);
        await h.Navigator!.ResolveAsync(h.State.ObstacleNavigation, 777, new(0, 0), new(20, 0),
            RadarNavigationPurpose.ApproachTarget, 9000, h.Settings.Combat.RadarObstacleAvoidance, 34);
        h.State.ObstacleNavigation.CommitDirectApproach(9000);
        h.SetPlayer(new(0, 0, 0), 0);
        await h.Tick();
        Check(h.Logger.Entries.Any(e => e.EventName == "stationary_combat.tab.blocked_by_obstacle"), "defense must retain the final radar check");
        Check(!h.Input.Keys.Contains("Tab"), "blocked defense line must not Tab");
        Check(!h.Logger.Entries.Any(e => e.EventName == "stationary_combat.face_target"), "radar detour must take priority over defense Tab facing");
    }

    public static async Task DefenseMatchingLockKeepsFightAsync()
    {
        using var h = new Harness();
        StartPetDefense(h);
        h.SetPlayer(new(0, 0, 0), 0);
        h.Api.TargetEntityId = 100;
        h.Api.TargetOwnServerObjectId = 9000;
        h.Api.TargetServerObjectId = 2000;
        h.Api.TargetPosition = h.Candidate.Position;
        await h.Tick();
        Check(h.State.Fighting && h.State.CurrentTargetServerObjectId == 9000, "matching defense lock must keep the current fight");
        Check(!h.Input.Keys.Contains("Tab"), "matching defense lock must not reacquire");
        Check(!h.Logger.Entries.Any(e => e.EventName is "stationary_combat.face_target" or "stationary_combat.target.defense_face_wait"),
            "matching defense lock must not enter Tab facing; background pre-aim may still move the camera");
        Check(h.Input.Keys.Contains("D1"), "matching pet-defense lock must continue the existing opening attack");
    }

    private static void StartPetDefense(Harness h)
    {
        h.LockWrongMonster();
        var attacker = h.Candidate with { TargetServerObjectId = 2000 };
        h.Api.LocalServerObjectId = 1000;
        h.Api.Player = h.Api.Player with { CharacterClassId = AionClassId.Spiritmaster };
        h.Settings.Skills.SpiritmasterAutoSkillLogicEnabled = true;
        var roster = SummonedPetRosterSnapshot.Empty(1000, DateTimeOffset.Now);
        var pet = SummonedPetSnapshot.NotSummoned(1000, DateTimeOffset.Now) with
        {
            IsSummoned = true, EntityId = 2, ServerObjectId = 2000, LocalLinkedPetServerObjectId = 2000,
            OwnerConfirmed = true, CurrentHp = 100, MaxHp = 100, HpPercent = 100, HealthFields = new(true, true, true)
        };
        h.Api.SummonedPetRoster = roster with { LocalLinkedPetServerObjectId = 2000, LocalPlayerPet = roster.LocalPlayerPet with { Pet = pet } };
        h.Api.SummonedPet = pet;
        h.Api.WorldObjects = new[] { attacker, h.WrongMonster };
        h.State.SetCurrentTarget(attacker);
        h.State.Fighting = true;
        h.State.CurrentTargetIsMaintenanceDefense = true;
        h.State.CurrentTargetBypassesHomeLeash = true;
        h.State.FacedCandidateEntityId = attacker.EntityId;
    }

    private sealed class Harness : IDisposable
    {
        private readonly Dictionary<string, string?> _environment = new();
        private readonly CancellationTokenSource _stop = new(TimeSpan.FromSeconds(15));
        public readonly ScriptSettings Settings = new()
        {
            MainMode = AccountMainMode.CustomCombat, CombatMode = AccountCombatMode.Stationary,
            Combat = new() { HasStationaryCombatPosition = true, StationaryCombatRadius = 60, EnableLoot = false },
            Maintenance = new() { SitMaintenanceEnabled = false }
        };
        public readonly WorldObjectSnapshot Candidate = new(100, 9000, "candidate", "monster", new(20, 0, 0), 20, 100, 100);
        public readonly WorldObjectSnapshot WrongMonster = new(200, 8000, "wrong lock", "monster", new(10, 0, 0), 10, 100, 100);
        public readonly FakeGameApi Api;
        public readonly RecordingKeyboardInput Input = new();
        public readonly InMemoryRoadhogLogger Logger = new();
        public readonly StationaryCombatState State = new();
        public readonly SemiAutoCombatState SemiState = new();
        public readonly StationaryObstacleNavigator? Navigator;
        public readonly StationaryCombatController Controller;
        public readonly AccountWorkerContext Context;
        public Harness(bool radar = false)
        {
            SetEnvironment("AION_FACE_TARGET_BEARING_MODE", "y-x");
            foreach (var name in new[] { "SETTLE_MS", "MOUSE_DOWN_WARMUP_MS", "ADAPTIVE_READ_SETTLE_MS", "ADAPTIVE_READ_TIMEOUT_MS", "DRAG_STEP_DELAY_MS" })
                SetEnvironment("AION_FACE_TARGET_" + name, "0");
            SetEnvironment("AION_FACE_TARGET_TWO_PASS_MAX_PASSES", "1");
            Api = new()
            {
                Player = new(1, 0, "test", 100, 100, 100, 100, 0, new(0, 0, 0), DateTimeOffset.Now, 90, 10, 90),
                TargetEntityId = 0, TargetCurrentHp = 0, TargetMaxHp = 0, TargetPosition = null,
                TargetServerObjectId = 0, TargetIsTargetingLocalPlayer = false,
                WorldObjects = new[] { Candidate }, Skills = Array.Empty<SkillSnapshot>(),
                Channel = new(0, 1, 777, DateTimeOffset.Now)
            };
            if (radar)
            {
                Settings.Combat.RadarObstacleAvoidance.Enabled = true;
                Navigator = new(new StaticRadarMapStore(new RadarMapDocument
                {
                    MapId = 777, Segments = new() { new() { Start = new(10, -8), End = new(10, 8) } }
                }), new RadarRoutePlanner(), new RadarMapRevisionRegistry());
            }
            Controller = new(Input, new SemiAutoCombatController(Input), obstacleNavigator: Navigator);
            Context = new(new() { AccountName = "tab-facing", ScriptSettings = Settings }, Api, Logger,
                new AccountRuntimeManager(Logger), new(), _stop.Token);
            State.MarkStartupRecoveryChecked();
            State.MarkCandidate(Candidate, DateTimeOffset.Now);
        }
        public void SetPlayer(Vector3Snapshot position, double yaw) => Api.Player = Api.Player with
        { Position = position, CameraYawDegrees = yaw, ActorYawDegrees = yaw, CapturedAt = DateTimeOffset.Now };
        public void LockWrongMonster()
        {
            SetLockedMonster(WrongMonster);
            Api.WorldObjects = new[] { Candidate, WrongMonster };
            State.SetCurrentTarget(Candidate);
        }

        public void SetLockedMonster(WorldObjectSnapshot target)
        {
            Api.TargetEntityId = target.EntityId;
            Api.TargetOwnServerObjectId = target.ServerObjectId;
            Api.TargetName = target.Name;
            Api.TargetCurrentHp = Api.TargetMaxHp = 100;
            Api.TargetPosition = target.Position;
        }

        public Task Tick() => Controller.TickAsync(Context, SemiAutoSkillPlan.FromSettings(Settings.Skills), SemiState, State);
        public void SetEnvironment(string name, string value)
        {
            _environment[name] = Environment.GetEnvironmentVariable(name);
            Environment.SetEnvironmentVariable(name, value);
        }
        public void Dispose()
        {
            _stop.Cancel();
            Controller.StopWorkerBackgroundWork(Context, State);
            _stop.Dispose();
            foreach (var pair in _environment) Environment.SetEnvironmentVariable(pair.Key, pair.Value);
        }
    }
}
