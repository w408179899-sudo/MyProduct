using Roadhog.Application.SemiAuto;
using Roadhog.Core.Accounts;
using Roadhog.Core.Common;
using Roadhog.Core.Model;
using static SharedSkillAuditFixture;

internal static class CoordinatedSummonCooldownTests
{
    private const uint PetId = 1696, SpeedId = 1778;
    private const string PetKey = "Alt+OemMinus", SpeedKey = "Alt+D9";
    public static IEnumerable<(string, Func<Task>)> Cases => new (string, Func<Task>)[]
    {
        ("combat flow v2 summon cooldown blocks initial input without a cast reservation", CoolingAsync),
        ("combat flow v2 summon cooldown resolves manually configured skill key", ManualKeyAsync),
        ("combat flow v2 summon cooldown expiry resumes input", ExpiryAsync),
        ("combat flow v2 summon cooldown waits through final eighty milliseconds", ExactBoundaryAsync),
        ("combat flow v2 summon cooldown ready skill has no artificial wait", ReadyAsync),
        ("combat flow v2 summon cooldown guards combat maintenance entry", CombatEntryAsync),
        ("combat flow v2 summon cooldown allows actual defense before any cast", DefenseAsync),
        ("combat flow v2 summon cooldown preserves HP priority", HpAsync),
        ("combat flow v2 summon cooldown preserves pending cast input", PendingAsync),
        ("combat flow v2 summon cooldown skips optional speed cooldown", SpeedCoolingAsync),
        ("combat flow v2 summon cooldown keeps ready speed order", SpeedReadyAsync),
        ("combat flow v2 summon cooldown rechecks after speed input", CooldownChangesAsync),
        ("combat flow v2 summon cooldown checks death after speed input", DeathAsync),
        ("combat flow v2 summon cooldown rejected input starts no confirmation", RejectedAsync),
        ("combat flow v2 summon cooldown cancels before input", CancelAsync),
        ("combat flow v2 summon cooldown uncalibrated duration uses monotonic time", UnknownClockAsync),
        ("combat flow v2 summon cooldown changed endpoint restarts duration observation", ChangedEndpointAsync),
        ("combat flow v2 summon cooldown pet arrival resets duration observation", PetReturnsAsync),
        ("combat flow v2 summon cooldown is isolated between accounts", IsolationAsync),
    };

    private static async Task<SharedSkillAuditFixture> CreateAsync(bool manual = false, bool speed = false)
    {
        var f = new SharedSkillAuditFixture();
        f.SetPet(11, 0);
        f.Api.Skills = f.Api.Skills.Append(Skill(PetId, "pet summon", 1, 5000))
            .Append(Skill(SpeedId, "summon speed", 1, 60000)).ToArray();
        f.Api.Quickbar = f.Api.Quickbar with { Slots = f.Api.Quickbar.Slots
            .Append(new(SkillQuickbar.Alt, 10, 21, PetId)).Append(new(SkillQuickbar.Alt, 8, 21, SpeedId)).ToArray() };
        f.Settings.Skills.Spiritmaster.SummonSkills = new();
        if (speed) f.Settings.Skills.Spiritmaster.SummonSkills.Add(new() { SkillId = SpeedId, Key = SpeedKey });
        f.Settings.Skills.Spiritmaster.SummonSkills.Add(new() { SkillId = manual ? 0 : PetId, Key = PetKey });
        await f.PrepareAsync();
        return f;
    }

    private static Task<bool> Ensure(SharedSkillAuditFixture f, SemiAutoCombatState? state = null, Func<Task<bool>>? defense = null) =>
        f.Controller.EnsureSpiritmasterPetAsync(f.Context, f.Plan, state ?? f.State, handleSummonRetryDefense: defense);
    private static void EndAt(SharedSkillAuditFixture f, uint end) =>
        f.Api.Skills = f.Api.Skills.Select(s => s.SkillId == PetId ? s with { CooldownEndTime = end } : s).ToArray();

    private static async Task CoolingAsync()
    {
        using var f = await CreateAsync(); f.Confirm(PetId);
        await Ensure(f); await Ensure(f);
        Sequence(Array.Empty<string>(), f.Keyboard.Keys, "cooldown is not a cast attempt");
        Check(!f.State.HasPendingSpiritmasterSummonVerification, "cooldown starts no verification window");
        Check(f.State.ShouldAttemptSpiritmasterSummon(DateTimeOffset.Now, TimeSpan.FromSeconds(6)), "waiting consumes no retry interval");
        Check(f.Logger.Entries.Any(e => e.EventName == "combat_flow.summon.cooldown_wait"), "normal cooldown is diagnosed separately from a timeout");
    }
    private static async Task ManualKeyAsync()
    {
        using var f = await CreateAsync(manual: true); f.Confirm(PetId);
        await Ensure(f);
        Sequence(Array.Empty<string>(), f.Keyboard.Keys, "the actual skill at Alt+Minus is cooling despite configured ID zero");
        Check(f.Observer.Requests.Any(ids => ids is not null && ids.SequenceEqual(new[] { PetId })), "read only the exact resolved skill cooldown");
        Check(f.Settings.Skills.Spiritmaster.SummonSkills[0].SkillId == 0, "diagnostics do not rewrite manual configuration");
    }
    private static async Task ExpiryAsync()
    {
        using var f = await CreateAsync(); f.Confirm(PetId); await Ensure(f);
        EndAt(f, unchecked((uint)Environment.TickCount64 - 1000));
        await Ensure(f);
        Sequence(new[] { PetKey }, f.Keyboard.Keys, "expiry permits one summon");
        Check(f.State.HasPendingSpiritmasterSummonVerification, "only delivered input begins verification");
    }
    private static async Task ReadyAsync()
    {
        using var f = await CreateAsync(); await Ensure(f);
        Sequence(new[] { PetKey }, f.Keyboard.Keys, "already ready does not sleep five seconds");
    }
    private static Task ExactBoundaryAsync()
    {
        using var f = new SharedSkillAuditFixture();
        foreach (var osTick in new[] { 100000u, uint.MaxValue - 30 })
        {
            var gameTick = f.State.EstimateGameTick(osTick);
            foreach (var remaining in new[] { 80, 79, 1, 0, -1 })
            {
                var skill = Skill(PetId, "pet summon", 1, 5000) with { CooldownEndTime = unchecked(gameTick + (uint)remaining) };
                var expected = remaining > 0 ? SemiAutoSkillCooldownReadiness.CoolingDown : SemiAutoSkillCooldownReadiness.Ready;
                Check(SemiAutoCombatController.GetSummonCooldownReadiness(skill, f.State, osTick) == expected,
                    "summon expiry is exact, including tick wrap, and never borrows attack tolerance");
            }
        }
        return Task.CompletedTask;
    }
    private static async Task CombatEntryAsync()
    {
        using var f = await CreateAsync(); f.Confirm(PetId); await f.TickAsync();
        Check(!f.Keyboard.Keys.Contains(PetKey), "the combat path cannot bypass summon cooldown");
        Check(!f.State.HasPendingSpiritmasterSummonVerification, "combat entry starts no fake cast window");
    }
    private static async Task DefenseAsync()
    {
        using var f = await CreateAsync(); f.Confirm(PetId); var calls = 0;
        await Ensure(f, defense: () => { calls++; return Task.FromResult(true); });
        Check(calls == 1 && f.State.CoordinatedSummonDefenseAllowed, "defense need not wait for a deliberately failed cast");
        Check(f.State.CoordinatedSummonDefenseReason == "summon_cooldown", "cooldown defense must not be mislabeled as a failed cast");
        Sequence(Array.Empty<string>(), f.Keyboard.Keys, "the summon owner sends no input during defense");
    }
    private static async Task HpAsync()
    {
        using var f = await CreateAsync(); f.ConfigureResource("hp"); await f.PrepareAsync(); f.Confirm(PetId);
        f.Keyboard.AfterPress = key => { if (key == "D2") f.Confirm(LowHpId); };
        await Ensure(f, defense: () => throw new Exception("HP gets its established first turn"));
        Sequence(new[] { "D2" }, f.Keyboard.Keys, "cooldown does not lock out HP recovery");
        Check(!f.State.HasPendingSpiritmasterSummonVerification, "HP recovery is not a summon attempt");
    }
    private static async Task PendingAsync()
    {
        using var f = await CreateAsync(); f.Confirm(PetId);
        f.State.BeginSpiritmasterSummonVerification(DateTimeOffset.Now, TimeSpan.FromSeconds(5));
        await Ensure(f, defense: () => throw new Exception("pending input still owns its protected window"));
        Sequence(Array.Empty<string>(), f.Keyboard.Keys, "new cooldown logic never interrupts an actual pending cast");
    }
    private static async Task SpeedCoolingAsync()
    {
        using var f = await CreateAsync(speed: true); f.Confirm(SpeedId); await Ensure(f);
        Sequence(new[] { PetKey }, f.Keyboard.Keys, "optional speed cooldown cannot block ready summon");
    }
    private static async Task SpeedReadyAsync()
    {
        using var f = await CreateAsync(speed: true); await Ensure(f);
        Sequence(new[] { SpeedKey, PetKey }, f.Keyboard.Keys, "ready speed retains the configured order");
    }
    private static async Task CooldownChangesAsync()
    {
        using var f = await CreateAsync(speed: true);
        f.Keyboard.AfterPress = key => { if (key == SpeedKey) f.Confirm(PetId); };
        await Ensure(f);
        Sequence(new[] { SpeedKey }, f.Keyboard.Keys, "a newly started cooldown is rechecked after preparation");
        Check(!f.State.HasPendingSpiritmasterSummonVerification, "preparation alone does not claim a summon");
    }
    private static async Task DeathAsync()
    {
        using var f = await CreateAsync(speed: true);
        f.Keyboard.AfterPress = key => { if (key == SpeedKey) f.Api.Player = f.Api.Player with { CurrentHp = 0 }; };
        await Ensure(f); Sequence(new[] { SpeedKey }, f.Keyboard.Keys, "death while preparing prevents the pet key");
        Check(!f.State.HasPendingSpiritmasterSummonVerification, "no cast confirmation starts after death");
    }
    private static async Task RejectedAsync()
    {
        using var f = await CreateAsync(); f.Keyboard.PressResult = _ => OperationResult.Fail("test rejection");
        await Ensure(f); Check(!f.State.HasPendingSpiritmasterSummonVerification, "rejected delivery is not an in-flight cast");
        var defended = false;
        await Ensure(f, defense: () => { defended = true; return Task.FromResult(true); });
        Check(defended, "rejected input must not lock out defense during the retry interval");
    }
    private static async Task CancelAsync()
    {
        using var f = await CreateAsync(); var cancelled = false;
        try { await f.Controller.EnsureSpiritmasterPetAsync(f.Context, f.Plan, f.State, () => { f.Cancel(); return Task.CompletedTask; }); }
        catch (OperationCanceledException) { cancelled = true; }
        Check(cancelled, "cancellation propagates before any summon input"); Sequence(Array.Empty<string>(), f.Keyboard.Keys, "stop sends no input");
    }
    private static async Task UnknownClockAsync()
    {
        using var f = await CreateAsync(); var state = new SemiAutoCombatState(); EndAt(f, 123);
        await Ensure(f, state); f.Clock.Advance(4999); f.Clock.ShiftUtc(TimeSpan.FromHours(3)); await Ensure(f, state);
        Sequence(Array.Empty<string>(), f.Keyboard.Keys, "wall time cannot end the five-second cooldown duration");
        f.Clock.Advance(1); await Ensure(f, state);
        Sequence(new[] { PetKey }, f.Keyboard.Keys, "a stable endpoint cannot defer summon forever without clock calibration");
    }
    private static async Task ChangedEndpointAsync()
    {
        using var f = await CreateAsync(); var state = new SemiAutoCombatState(); EndAt(f, 123);
        await Ensure(f, state); f.Clock.Advance(4000); EndAt(f, 124); await Ensure(f, state);
        f.Clock.Advance(1000); await Ensure(f, state); Sequence(Array.Empty<string>(), f.Keyboard.Keys, "new endpoint has its own duration");
        f.Clock.Advance(4000); await Ensure(f, state); Sequence(new[] { PetKey }, f.Keyboard.Keys, "new duration eventually expires");
    }
    private static async Task PetReturnsAsync()
    {
        using var f = await CreateAsync(); var state = new SemiAutoCombatState(); EndAt(f, 123);
        await Ensure(f, state); f.Clock.Advance(5000); f.SetPet(12, 100); await Ensure(f, state);
        f.SetPet(12, 0); await Ensure(f, state);
        Sequence(Array.Empty<string>(), f.Keyboard.Keys, "a new disappearance cannot reuse an earlier expired observation");
    }
    private static async Task IsolationAsync()
    {
        using var one = await CreateAsync(); using var two = await CreateAsync(); one.Confirm(PetId);
        await Ensure(one); await Ensure(two);
        Sequence(Array.Empty<string>(), one.Keyboard.Keys, "cooling account waits"); Sequence(new[] { PetKey }, two.Keyboard.Keys, "another account proceeds");
    }
}
