using Roadhog.Application;
using Roadhog.Application.SemiAuto;
using Roadhog.Application.StationaryCombat;
using Roadhog.Application.Workers;
using Roadhog.Core.Accounts;
using Roadhog.Core.Diagnostics;
using Roadhog.Core.Model;

internal static class CoordinatedStationaryRecoveryTests
{
    public static async Task ThreatPreventsRestThroughoutApproachAsync()
    {
        foreach (var lowMp in new[] { false, true })
        foreach (var targetsPet in new[] { false, true })
        {
            using var f = new Fixture(lowMp, targetsPet);
            for (var tick = 0; tick < 6; tick++)
            {
                await f.TickAsync();
                Check(!f.SemiAuto.IsMaintenanceResting && !f.Input.Keys.Contains("OemComma"),
                    $"lowMp={lowMp}, targetsPet={targetsPet}, tick={tick}: an attacker prevents sitting before acquisition");
                Check(f.State.CurrentTargetIsMaintenanceDefense && f.State.CandidateEntityId == f.Attacker.EntityId,
                    "the live attacker remains the defense candidate");
                Check(!f.State.Fighting, "fixture keeps the attacker outside acquisition range across ticks");
            }
        }
    }

    public static async Task InterruptedRestDoesNotRestartAsync()
    {
        foreach (var seated in new[] { false, true })
        {
            using var f = new Fixture();
            f.SemiAuto.StartMaintenanceRest(forHp: true, forMp: false);
            f.Api.Player = f.Api.Player with { StanceFlags = seated ? 5u : 1u, MotionMode = seated ? 1u : 0u };
            f.Input.AfterPress = key =>
            {
                if (key == "X") f.Api.Player = f.Api.Player with { StanceFlags = 1, MotionMode = 0 };
            };
            for (var tick = 0; tick < 6; tick++) await f.TickAsync();
            Check(!f.SemiAuto.IsMaintenanceResting && !f.Input.Keys.Contains("OemComma"),
                "canceling rest must not permit the next approach tick to sit again");
            Check(f.Input.Keys.Count(key => key == "X") == (seated ? 1 : 0),
                "only a seated player needs the existing single X interruption");
            Check(f.State.CurrentTargetIsMaintenanceDefense && !f.State.Fighting,
                "defense approach remains active without toggling rest");
        }
    }

    public static async Task EndedThreatAllowsRecoveryAsync()
    {
        foreach (var end in new[] { "dead", "missing", "other_target" })
        {
            using var f = new Fixture();
            // Start at the already-canceled boundary from the incident.
            f.SemiAuto.StartMaintenanceRest(forHp: true, forMp: false);
            await f.TickAsync();
            Check(f.State.CurrentTargetIsMaintenanceDefense && !f.SemiAuto.IsMaintenanceResting,
                "fixture establishes a defense candidate after canceling rest");
            f.Api.WorldObjects = end switch
            {
                "dead" => new[] { f.Attacker with { CurrentHp = 0 } },
                "missing" => Array.Empty<WorldObjectSnapshot>(),
                _ => new[] { f.Attacker with { TargetServerObjectId = 9000, IsTargetingLocalPlayer = false } }
            };
            await f.TickAsync();
            Check(f.SemiAuto.IsMaintenanceResting && f.Input.Keys.Count(key => key == "OemComma") == 1,
                end + ": stale defense flags must not block recovery after the actual threat ends");
        }
    }

    public static async Task OrdinaryMonstersStillAllowRestAsync()
    {
        foreach (var nearby in new[] { "none", "aggressive", "other_target" })
        {
            using var f = new Fixture();
            f.Api.WorldObjects = nearby == "none" ? Array.Empty<WorldObjectSnapshot>() : new[]
            {
                f.Attacker with
                {
                    TargetServerObjectId = nearby == "other_target" ? 9000u : 0u,
                    IsTargetingLocalPlayer = false,
                    AggressiveKnown = true,
                    IsAggressiveToPlayer = true
                }
            };
            await f.TickAsync();
            Check(f.SemiAuto.IsMaintenanceResting && f.Input.Keys.SequenceEqual(new[] { "OemComma" }),
                nearby + ": ordinary nearby monsters do not suppress low-resource recovery");
        }
    }

    public static async Task RecoveryStillPrecedesDefenseAsync()
    {
        foreach (var lowMp in new[] { false, true })
        {
            using var f = new Fixture(lowMp);
            var rules = lowMp ? f.Settings.Maintenance.MpMaintenanceRules : f.Settings.Maintenance.HpMaintenanceRules;
            rules.Add(new()
            {
                BelowPercent = 80, Key = "D8", SkillId = lowMp ? 0u : 7000u,
                ActionType = lowMp ? MaintenanceRuleActionType.Potion : MaintenanceRuleActionType.Skill
            });
            f.Api.InventoryItems = new[] { new InventoryItemSnapshot(1, 1, "高级精神恢复剂", 10, 0, false, 17) };
            var heal = new SkillSnapshot(7000, "heal", 1, 1, "heal", 1, false, 1000, 0);
            f.Api.Skills = new[] { heal };
            TestCooldownClock.Calibrate(f.SemiAuto);
            f.Input.AfterPress = _ => f.Api.Skills = new[]
                { heal with { CooldownEndTime = unchecked(TestCooldownClock.CurrentTick + 1000u) } };
            await f.TickAsync();
            Check(f.Input.Keys.SequenceEqual(new[] { "D8" }),
                $"lowMp={lowMp}: defense preserves recovery; keys={string.Join(",", f.Input.Keys)}; events={string.Join(",", f.Logger.Entries.Select(e => e.EventName))}");
            Check(!f.SemiAuto.IsMaintenanceResting && f.State.CandidateEntityId == 0,
                "ordinary recovery still completes its tick before target acquisition");
        }
    }

    public static async Task DefenseStillEntersCombatAsync()
    {
        using var f = new Fixture();
        f.SemiAuto.StartMaintenanceRest(forHp: true, forMp: false);
        await f.TickAsync();
        var close = f.Attacker with { Position = new Vector3Snapshot(5, 0, 0), DistanceToLocalPlayer = 5 };
        f.Api.WorldObjects = new[] { close };
        f.Api.TargetEntityId = close.EntityId;
        f.Api.TargetOwnServerObjectId = close.ServerObjectId;
        f.Api.TargetCurrentHp = close.CurrentHp;
        f.Api.TargetMaxHp = close.MaxHp;
        f.Api.TargetName = close.Name;
        f.Api.TargetPosition = close.Position;
        f.Api.TargetServerObjectId = close.TargetServerObjectId;
        f.Api.TargetIsTargetingLocalPlayer = true;
        await f.TickAsync();
        Check(f.State.Fighting && f.State.CurrentTargetServerObjectId == close.ServerObjectId,
            "the standing defense candidate can still become the active fight target");
        Check(!f.Input.Keys.Contains("OemComma"), "acquisition must not be preempted by another sit");
    }

    public static async Task RestResumesAfterDefenseAsync()
    {
        using var f = new Fixture();
        f.Api.WorldObjects = Array.Empty<WorldObjectSnapshot>();
        await f.TickAsync();
        Check(f.SemiAuto.IsMaintenanceResting && f.Input.Keys.SequenceEqual(new[] { "OemComma" }),
            "low resources enter maintenance rest");

        f.Api.Player = f.Api.Player with { StanceFlags = 5, MotionMode = 1 };
        f.Api.WorldObjects = new[] { f.Attacker };
        f.Input.AfterPress = key =>
        {
            if (key == "X") f.Api.Player = f.Api.Player with { StanceFlags = 1, MotionMode = 0 };
        };
        for (var tick = 0; tick < 3; tick++)
        {
            await f.TickAsync();
            Check(!f.SemiAuto.IsMaintenanceResting && f.State.CurrentTargetIsMaintenanceDefense,
                "the interrupted rest yields to defense approach");
        }
        Check(f.Input.Keys.Count(key => key == "OemComma") == 1 && f.Input.Keys.Count(key => key == "X") == 1,
            "a sustained threat never repeats the sit and wake loop");

        f.Api.WorldObjects = Array.Empty<WorldObjectSnapshot>();
        await f.TickAsync();
        Check(f.SemiAuto.IsMaintenanceResting, "ended threat permits resource recovery");
        Check(f.Logger.Entries.Any(e => e.EventName == "combat_flow.recovery.decision"),
            "the recovery coordinator exposes its decision");
    }

    public static async Task NoTargetRestWaitsForStandingAsync()
    {
        using var f = new Fixture();
        f.Settings.Maintenance.SitMaintenanceEnabled = false;
        f.Settings.Combat.ReturnHomeWhenNoTarget = true;
        f.Settings.Combat.SitWhenNoTargetAtHome = true;
        f.Api.Player = f.Api.Player with { CurrentHp = 99 };
        f.Api.WorldObjects = Array.Empty<WorldObjectSnapshot>();
        await f.TickAsync();
        Check(f.State.NoTargetRestActive && f.Input.Keys.SequenceEqual(new[] { "OemComma" }),
            "no-target rest begins at home with non-full resources");

        f.Api.Player = f.Api.Player with { CurrentHp = 100, StanceFlags = 5, MotionMode = 1 };
        await f.TickAsync();
        Check(f.State.NoTargetRestActive && f.State.NoTargetRestExitPending &&
              f.Input.Keys.SequenceEqual(new[] { "OemComma", "X" }),
            "full resources start standing confirmation without treating key submission as completion");

        f.Api.Player = f.Api.Player with { StanceFlags = 1, MotionMode = 0 };
        await f.TickAsync();
        await f.TickAsync();
        Check(!f.State.NoTargetRestActive && !f.State.NoTargetRestExitPending &&
              f.Input.Keys.SequenceEqual(new[] { "OemComma", "X" }),
            "confirmed standing clears rest and full resources do not sit again");
    }

    public static async Task TemporarySwitchGuardPreventsBothRestKindsAsync()
    {
        using var f = new Fixture();
        f.Settings.Combat.ReturnHomeWhenNoTarget = true;
        f.Settings.Combat.SitWhenNoTargetAtHome = true;
        f.Api.WorldObjects = Array.Empty<WorldObjectSnapshot>();
        f.SemiAuto.StartMaintenanceRest(forHp: true, forMp: false);
        var until = DateTimeOffset.Now.AddMinutes(1);
        f.State.TemporarilyExcludeTarget(f.Attacker.EntityId, f.Attacker.ServerObjectId, until, until);
        await f.TickAsync();
        Check(f.State.ShouldGuardTemporaryTargetSwitch(DateTimeOffset.Now), "fixture keeps the guard active");
        Check(!f.SemiAuto.IsMaintenanceResting && !f.State.NoTargetRestActive &&
              !f.Input.Keys.Contains("OemComma"),
            "pending target handoff clears maintenance rest and blocks no-target rest");
    }

    public static async Task GatherWakesRestBeforeInteractionAsync()
    {
        using var f = new Fixture();
        f.Settings.Maintenance.SitMaintenanceEnabled = false;
        f.Settings.Combat.ReturnHomeWhenNoTarget = true;
        f.Settings.Combat.SitWhenNoTargetAtHome = true;
        f.Settings.Gather = new()
        {
            StationaryPriorityEnabled = true, StationarySearchRadiusMeters = 10,
            ThreatClearRadiusMeters = 7, OccupiedCheckRadiusMeters = 5,
            Rules = new() { new() { Enabled = true, GatherSourceId = 400951, Name = "shell", GatherKey = "NumPad1" } }
        };
        f.Api.Player = f.Api.Player with { CurrentHp = 99 };
        f.Api.WorldObjects = Array.Empty<WorldObjectSnapshot>();
        var now = DateTimeOffset.Now;
        f.Api.Gather = new(1, 100, new(0, 0, 0), Array.Empty<GatherObjectSnapshot>(),
            Array.Empty<GatherCompetitionPlayerSnapshot>(), Array.Empty<WorldObjectSnapshot>(), true, true,
            new(true, false, 0, false, 0, null, null), now);
        await f.TickAsync();
        Check(f.State.NoTargetRestActive && f.Input.Keys.SequenceEqual(new[] { "OemComma" }),
            "empty gather field permits no-target rest");

        f.Api.Player = f.Api.Player with { StanceFlags = 5, MotionMode = 1 };
        f.Api.Gather = f.Api.Gather with
        {
            Objects = new[] { new GatherObjectSnapshot(10, 10010, 400951, "shell", 1, 1, 2, 19,
                new(1, 0, 0), new(1, 0, 0), 1, false, null, now.AddSeconds(1)) },
            CapturedAt = now.AddSeconds(1)
        };
        f.State.LastGatherScanAt = DateTimeOffset.MinValue;
        await f.TickAsync();
        Check(f.State.NoTargetRestExitPending && f.Input.Keys.SequenceEqual(new[] { "OemComma", "X" }),
            "available gather work must first wake the player without interacting while seated");

        f.Api.Player = f.Api.Player with { StanceFlags = 1, MotionMode = 0 };
        f.State.LastGatherScanAt = DateTimeOffset.MinValue;
        await f.TickAsync();
        Check(!f.State.NoTargetRestActive && f.Input.Keys.Contains("NumPad1") &&
              f.Input.Keys.Count(key => key == "OemComma") == 1,
            "confirmed standing transfers input to gathering without reentering rest");
    }

    public static async Task StopDuringRestInterruptionPreventsFurtherInputAsync()
    {
        foreach (var cancelBeforeTick in new[] { true, false })
        {
            using var f = new Fixture();
            f.SemiAuto.StartMaintenanceRest(forHp: true, forMp: false);
            f.Api.Player = f.Api.Player with { StanceFlags = 5, MotionMode = 1 };
            if (cancelBeforeTick) f.Cancel();
            else f.Input.AfterPress = key => { if (key == "X") f.Cancel(); };
            try
            {
                await f.TickAsync();
                throw new InvalidOperationException("canceled tick must propagate cancellation");
            }
            catch (OperationCanceledException) { }
            var expected = cancelBeforeTick ? Array.Empty<string>() : new[] { "X" };
            Check(f.Input.Keys.SequenceEqual(expected) && f.Input.KeyDowns.Count == 0,
                "Stop cannot leak a sit, acquire or movement input after the cancellation boundary");
        }
    }

    private sealed class Fixture : IDisposable
    {
        private readonly CancellationTokenSource _stop = new(TimeSpan.FromSeconds(20));
        public ScriptSettings Settings { get; } = new()
        {
            MainMode = AccountMainMode.CustomCombat,
            CombatMode = AccountCombatMode.Stationary,
            Combat = new()
            {
                HasStationaryCombatPosition = true, StationaryCombatRadius = 60,
                TargetAcquireDistance = 29, ReturnHomeWhenNoTarget = false, EnableLoot = false
            },
            Maintenance = new()
            {
                SitMaintenanceEnabled = true, SitHpBelowPercent = 50, SitHpRecoverToPercent = 60,
                SitMpBelowPercent = 15, SitMpRecoverToPercent = 20
            },
            SemiAuto = new() { KeyHoldMs = 1, KeyGapMs = 1 }
        };
        public FakeGameApi Api { get; } = new();
        public RecordingKeyboardInput Input { get; } = new();
        public InMemoryRoadhogLogger Logger { get; } = new();
        public StationaryCombatState State { get; } = new();
        public SemiAutoCombatState SemiAuto { get; } = new();
        public WorldObjectSnapshot Attacker { get; }
        public StationaryCombatController Controller { get; }
        public AccountWorkerContext Context { get; }
        public SemiAutoSkillPlan Plan { get; }

        public Fixture(bool lowMp = false, bool targetsPet = false)
        {
            Settings.Skills.SpiritmasterAutoSkillLogicEnabled = targetsPet;
            Api.Player = new(1, 0, "test", lowMp ? 100u : 20u, 100, lowMp ? 10u : 100u, 100,
                0, new(0, 0, 0), DateTimeOffset.Now, StanceFlags: 1);
            Api.TargetEntityId = 0;
            Api.TargetCurrentHp = 0;
            Api.TargetPosition = null;
            Api.SummonedPetRoster = SummonedPetRosterSnapshot.Empty(100, DateTimeOffset.Now) with
                { LocalLinkedPetServerObjectId = targetsPet ? 500u : 0u };
            Attacker = new(200, 2000, "attacker", "monster", new(50, 0, 0), 50, 1000, 1000,
                TargetServerObjectId: targetsPet ? 500u : 100u, IsTargetingLocalPlayer: !targetsPet);
            Api.WorldObjects = new[] { Attacker };
            State.MarkStartupRecoveryChecked();
            Controller = new(Input, new SemiAutoCombatController(Input));
            Context = new(new AccountConfig { AccountName = "test", ScriptSettings = Settings }, Api, Logger,
                new AccountRuntimeManager(Logger), new AccountWorkerOptions(), _stop.Token);
            Plan = SemiAutoSkillPlan.FromSettings(Settings.Skills);
        }

        public Task<TimeSpan> TickAsync() => Controller.TickAsync(Context, Plan, SemiAuto, State);

        public void Cancel() => _stop.Cancel();

        public void Dispose()
        {
            Controller.StopWorkerBackgroundWork(Context, State);
            _stop.Cancel();
            _stop.Dispose();
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
