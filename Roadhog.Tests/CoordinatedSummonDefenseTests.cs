using Roadhog.Application.SemiAuto;
using Roadhog.Application.StationaryCombat;
using Roadhog.Core.Accounts;
using Roadhog.Core.Model;
using static SharedSkillAuditFixture;

internal static class CoordinatedSummonDefenseTests
{
    public static IEnumerable<(string, Func<Task>)> Cases => new (string, Func<Task>)[]
    {
        ("combat flow v2 summon defense begins after timeout", TimeoutYieldsDefenseAsync),
        ("combat flow v2 summon defense preserves active confirmation", PendingDoesNotYieldAsync),
        ("combat flow v2 summon defense preserves first attempt", FirstAttemptAsync),
        ("combat flow v2 summon defense leaves legacy unchanged", LegacyAsync),
        ("combat flow v2 summon defense resumes summon without threat", NoThreatAsync),
        ("combat flow v2 summon defense resets after pet returns", PetResetsAsync),
        ("combat flow v2 summon defense honors cancellation", CancelAsync),
        ("combat flow v2 summon defense keeps HP priority", HpFirstAsync),
        ("combat flow v2 summon defense attacks without recursive summon", StationaryDefenseAsync),
        ("combat flow v2 summon defense ignores ordinary nearby monsters", OrdinaryMonsterAsync),
        ("combat flow v2 summon defense ends after attacker dies", AttackerDiesAsync),
        ("combat flow v2 summon defense stops input on newly observed death", PlayerDiesAsync),
    };

    private static async Task<SharedSkillAuditFixture> CreateAsync(CombatFlowVersion version = CombatFlowVersion.CoordinatedV2)
    {
        var f = new SharedSkillAuditFixture();
        f.Context.Config.CombatFlowVersion = version;
        f.Settings.Skills.Spiritmaster.SummonSkills = new() { new() { Key = "NumPad8" } };
        f.SetPet(11, 0);
        await f.PrepareAsync();
        return f;
    }

    private static void Expire(SharedSkillAuditFixture f) =>
        f.State.BeginSpiritmasterSummonVerification(DateTimeOffset.Now.AddSeconds(-10), TimeSpan.FromSeconds(5));

    private static Task<bool> EnsureAsync(SharedSkillAuditFixture f, Func<Task<bool>> defense) =>
        f.Controller.EnsureSpiritmasterPetAsync(f.Context, f.Plan, f.State, handleSummonRetryDefense: defense);

    public static async Task TimeoutYieldsDefenseAsync()
    {
        using var f = await CreateAsync();
        Expire(f);
        var calls = 0;
        for (var tick = 0; tick < 3; tick++)
            Check(await EnsureAsync(f, () => { calls++; return Task.FromResult(true); }), "defense owns this tick");
        Check(calls == 3, "an active defense continues across ticks after the expired attempt");
        Sequence(Array.Empty<string>(), f.Keyboard.Keys, "retry must not interrupt defense");
        Check(!f.State.HasPendingSpiritmasterSummonVerification, "expired input reservation is released");
    }

    public static async Task PendingDoesNotYieldAsync()
    {
        using var f = await CreateAsync();
        Expire(f);
        await EnsureAsync(f, () => Task.FromResult(true));
        f.State.BeginSpiritmasterSummonVerification(DateTimeOffset.Now, TimeSpan.FromMinutes(1));
        var called = false;
        await EnsureAsync(f, () => { called = true; return Task.FromResult(true); });
        Check(!called && f.State.HasPendingSpiritmasterSummonVerification, "an in-flight summon remains protected even after earlier failures");
        Sequence(Array.Empty<string>(), f.Keyboard.Keys, "waiting sends no extra input");
    }

    public static async Task FirstAttemptAsync()
    {
        using var f = await CreateAsync();
        var called = false;
        await EnsureAsync(f, () => { called = true; return Task.FromResult(true); });
        Check(!called, "the first summon keeps the established priority");
        Sequence(new[] { "NumPad8" }, f.Keyboard.Keys, "first attempt still summons");
    }

    public static async Task LegacyAsync()
    {
        using var f = await CreateAsync(CombatFlowVersion.Legacy);
        Expire(f);
        var called = false;
        await EnsureAsync(f, () => { called = true; return Task.FromResult(true); });
        Check(!called, "legacy never enters V2 defense handoff");
        Sequence(new[] { "NumPad8" }, f.Keyboard.Keys, "legacy retry is unchanged");
    }

    public static async Task NoThreatAsync()
    {
        using var f = await CreateAsync();
        Expire(f);
        await EnsureAsync(f, () => Task.FromResult(true));
        await EnsureAsync(f, () => Task.FromResult(false));
        Sequence(new[] { "NumPad8" }, f.Keyboard.Keys, "ending the threat resumes summoning");
        Check(f.State.HasPendingSpiritmasterSummonVerification, "resumed summon has its confirmation window");
    }

    public static async Task PetResetsAsync()
    {
        using var f = await CreateAsync();
        Expire(f);
        await EnsureAsync(f, () => Task.FromResult(true));
        f.SetPet(12, 100);
        Check(!await EnsureAsync(f, () => throw new InvalidOperationException("live pet should release control")), "pet arrival ends recovery");
        f.SetPet(12, 0);
        await EnsureAsync(f, () => throw new InvalidOperationException("a new episode starts with a summon"));
        Sequence(new[] { "NumPad8" }, f.Keyboard.Keys, "new episode has no stale defense permission");
    }

    public static async Task CancelAsync()
    {
        using var f = await CreateAsync();
        Expire(f);
        var cancelled = false;
        try { await EnsureAsync(f, () => { f.Cancel(); return Task.FromResult(false); }); }
        catch (OperationCanceledException) { cancelled = true; }
        Check(cancelled, "stop inside defense preparation propagates");
        Sequence(Array.Empty<string>(), f.Keyboard.Keys, "stop prevents subsequent summon input");
    }

    public static async Task HpFirstAsync()
    {
        using var f = await CreateAsync();
        f.ConfigureResource("hp");
        f.Keyboard.AfterPress = key => { if (key == "D2") f.Confirm(LowHpId); };
        await f.PrepareAsync();
        Expire(f);
        var calls = 0;
        await EnsureAsync(f, () => { calls++; return Task.FromResult(true); });
        Check(calls == 0, "configured HP recovery retains its first turn");
        await EnsureAsync(f, () => { calls++; return Task.FromResult(true); });
        Check(calls == 1, "defense receives the following turn");
        Sequence(new[] { "D2" }, f.Keyboard.Keys, "HP and defense do not press another summon");
    }

    private static (StationaryCombatController Controller, StationaryCombatState State) ConfigureStationary(SharedSkillAuditFixture f, bool attacker)
    {
        f.Settings.MainMode = AccountMainMode.CustomCombat;
        f.Settings.CombatMode = AccountCombatMode.Stationary;
        f.Settings.Combat.HasStationaryCombatPosition = true;
        f.Settings.Combat.StationaryCombatRadius = 30;
        f.Settings.Combat.EnableLoot = false;
        f.Settings.Combat.ReturnHomeWhenNoTarget = false;
        f.Api.Player = f.Api.Player with { CurrentHp = 100, MaxHp = 100, Position = new(0, 0, 0), TargetEntityId = attacker ? (ushort)200 : (ushort)0 };
        f.Api.LocalServerObjectId = 100;
        f.Api.TargetEntityId = attacker ? (ushort)200 : (ushort)0;
        f.Api.TargetOwnServerObjectId = 2000;
        f.Api.TargetObjectType = LockedTargetSnapshot.MonsterObjectType;
        f.Api.TargetCurrentHp = 1000;
        f.Api.TargetMaxHp = 1000;
        f.Api.TargetPosition = new(5, 0, 0);
        f.Api.TargetServerObjectId = attacker ? 100u : 0u;
        f.Api.TargetIsTargetingLocalPlayer = attacker;
        f.Api.WorldObjects = new[] { new WorldObjectSnapshot(200, 2000, "attacker", "monster", new(5, 0, 0), 5, 1000, 1000,
            TargetServerObjectId: attacker ? 100u : 0u, IsTargetingLocalPlayer: attacker) };
        f.Api.SummonedPetRoster = SummonedPetRosterSnapshot.Empty(100, DateTimeOffset.Now);
        var state = new StationaryCombatState();
        state.MarkStartupRecoveryChecked();
        return (new(f.Keyboard, f.Controller), state);
    }

    public static async Task StationaryDefenseAsync()
    {
        using var f = await CreateAsync();
        var (controller, state) = ConfigureStationary(f, true);
        Expire(f);
        for (var tick = 0; tick < 4; tick++)
            await EnsureAsync(f, () => controller.TryHandleCoordinatedSummonRetryDefenseAsync(f.Context, f.Plan, f.State, state));
        Check(state.Fighting && state.CurrentTargetIsMaintenanceDefense, "the actual attacker is adopted as defense");
        Check(f.Keyboard.Keys.Contains("D1"), "ordinary player attack can execute without a pet");
        Check(!f.Keyboard.Keys.Contains("NumPad8"), "nested maintenance cannot re-enter summon while defense owns input");
        Check(f.Logger.Entries.Any(e => e.EventName == "stationary_combat.recovery_defense.target_selected" &&
              Equals(e.Fields["phase"], "pet_recovery")), "defense ownership is observable");
    }

    public static async Task OrdinaryMonsterAsync()
    {
        using var f = await CreateAsync();
        var (controller, state) = ConfigureStationary(f, false);
        Expire(f);
        await EnsureAsync(f, () => controller.TryHandleCoordinatedSummonRetryDefenseAsync(f.Context, f.Plan, f.State, state));
        Check(!state.Fighting, "unrelated monsters do not take over pet recovery");
        Sequence(new[] { "NumPad8" }, f.Keyboard.Keys, "no attacker means retry summoning");
    }

    public static async Task AttackerDiesAsync()
    {
        using var f = await CreateAsync();
        var (controller, state) = ConfigureStationary(f, true);
        Expire(f);
        Func<Task<bool>> defense = () => controller.TryHandleCoordinatedSummonRetryDefenseAsync(f.Context, f.Plan, f.State, state);
        await EnsureAsync(f, defense);
        Check(state.Fighting, "fixture establishes an actual defense fight");
        f.Api.TargetCurrentHp = 0;
        f.Api.WorldObjects = f.Api.WorldObjects.Select(target => target with { CurrentHp = 0 }).ToArray();
        await EnsureAsync(f, defense);
        Check(!state.Fighting, "dead attacker ends defense");
        f.Keyboard.Keys.Clear();
        await EnsureAsync(f, defense);
        Sequence(new[] { "NumPad8" }, f.Keyboard.Keys, "the completed defense releases input to summoning");
    }

    public static async Task PlayerDiesAsync()
    {
        using var f = await CreateAsync();
        var (controller, state) = ConfigureStationary(f, true);
        Expire(f);
        f.Observer.BeforePetRead = _ => f.Api.Player = f.Api.Player with { CurrentHp = 0 };
        await EnsureAsync(f, () => controller.TryHandleCoordinatedSummonRetryDefenseAsync(f.Context, f.Plan, f.State, state));
        Sequence(Array.Empty<string>(), f.Keyboard.Keys, "fresh death cannot fall through to retry a summon");
    }
}
