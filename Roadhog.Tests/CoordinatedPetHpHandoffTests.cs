using Roadhog.Core.Accounts;
using Roadhog.Core.Common;
using Roadhog.Core.Model;
using static SharedSkillAuditFixture;

internal static class CoordinatedPetHpHandoffTests
{
    private const uint Routine = 1785;
    private const uint Emergency = 1678;
    public static IReadOnlyList<(string Name, Func<Task> Run)> Cases => new (string, Func<Task>)[]
    {
        ("combat flow v2 pet heal emergency handoff", EmergencyTakesOverAsync),
        ("combat flow v2 pet heal preserves legacy reservation", LegacyReservesAsync),
        ("combat flow v2 pet heal protects recent input", RecentInputReservesAsync),
        ("combat flow v2 pet heal honors configured input window", ConfiguredWindowAsync),
        ("combat flow v2 pet heal stale initial observation cannot hand off", StaleInitialObservationAsync),
        ("combat flow v2 pet heal handoff follows monotonic time", ClockShiftDoesNotEndProtectionAsync),
        ("combat flow v2 pet heal exact player floor", ExactPlayerFloorAsync),
        ("combat flow v2 pet heal alternative gates", AlternativeGatesAsync),
        ("combat flow v2 pet heal fresh safety checks", FreshSafetyChecksAsync),
        ("combat flow v2 pet heal fresh cooldown checks", FreshCooldownChecksAsync),
        ("combat flow v2 pet heal rule changes cancel handoff", RulesChangeDuringChecksAsync),
        ("combat flow v2 pet heal failed input preserves ownership", FailedInputKeepsReservationAsync),
        ("combat flow v2 pet heal stop prevents handoff", StopPreventsInputAsync),
        ("combat flow v2 pet heal no priority oscillation", NoPriorityOscillationAsync),
        ("combat flow v2 pet heal handoff retains expiry", RetainsExpiryAsync),
        ("combat flow v2 pet heal short lifetime cannot start handoff", InsufficientLifetimeAsync),
        ("combat flow v2 pet heal observed recovery retains existing semantics", RecoveryStillYieldsAsync),
        ("combat flow v2 pet heal handoff isolates accounts", AccountIsolationAsync)
    };

    private static async Task<SharedSkillAuditFixture> StartAsync(CombatFlowVersion version = CombatFlowVersion.CoordinatedV2)
    {
        var f = new SharedSkillAuditFixture();
        f.Context.Config.CombatFlowVersion = version;
        f.AddPetHpRule(Routine);
        f.AddPetHpRule(Emergency, 80);
        f.Settings.Skills.Spiritmaster.PetHpMaintenanceRules[0].BelowPercent = 40;
        f.Settings.Skills.Spiritmaster.PetHpMaintenanceRules[1].BelowPercent = 30;
        f.Api.Quickbar = f.Api.Quickbar with { Slots = f.Api.Quickbar.Slots
            .Where(slot => slot.SkillId != Emergency).Append(new(SkillQuickbar.Main, 4, 21, Emergency)).ToArray() };
        f.SetPet(11, 31);
        await f.PrepareAsync();
        await f.TickAsync();
        Check(f.State.PendingSpiritmasterPetHpIncreaseConfirmation?.SkillId == Routine, "routine heal starts at 31 percent");
        Sequence(new[] { "D4" }, f.Keyboard.Keys, "fixture starts one real controller input");
        return f;
    }

    private static void Due(SharedSkillAuditFixture f, uint petHp = 26)
    {
        f.SetPet(11, petHp);
        f.State.DeferSpiritmasterPetHpIncreaseConfirmation(DateTimeOffset.Now.AddSeconds(-1), TimeSpan.Zero);
    }
    private static void EndProtection(SharedSkillAuditFixture f) => f.Clock.Advance(2000);

    public static async Task EmergencyTakesOverAsync()
    {
        using var f = await StartAsync();
        EndProtection(f); Due(f);
        await f.TickAsync();
        Sequence(new[] { "D4", "D5" }, f.Keyboard.Keys, "eligible emergency heal replaces retry without ordinary input");
        var pending = f.State.PendingSpiritmasterPetHpIncreaseConfirmation!;
        Check(pending.SkillId == Emergency && pending.RuleIndex == 1 && pending.AttemptCount == 1 && pending.BaselineCurrentHp == 26,
            "new confirmation belongs to the new skill and fresh pet baseline");
        Check(f.Logged("semi_auto.spiritmaster.pet_hp.handoff", "toSkillId", Emergency), "handoff is explicit, never logged as success");
    }

    public static async Task LegacyReservesAsync()
    {
        using var f = await StartAsync(CombatFlowVersion.Legacy);
        EndProtection(f); Due(f); await f.TickAsync();
        Sequence(new[] { "D4", "D4" }, f.Keyboard.Keys, "legacy keeps its original reservation and retry");
    }

    public static async Task RecentInputReservesAsync()
    {
        using var f = await StartAsync();
        Due(f); await f.TickAsync();
        Sequence(new[] { "D4" }, f.Keyboard.Keys, "wait protects previous input and does not restart its timer with retries");
        f.Clock.Advance(1499); Due(f); await f.TickAsync();
        Sequence(new[] { "D4" }, f.Keyboard.Keys, "minimum confirmation window remains protected");
        f.Clock.Advance(1); Due(f); await f.TickAsync();
        Sequence(new[] { "D4", "D5" }, f.Keyboard.Keys, "handoff begins after the full window");
    }

    public static async Task ClockShiftDoesNotEndProtectionAsync()
    {
        using var f = await StartAsync();
        f.Clock.ShiftUtc(TimeSpan.FromHours(8)); Due(f); await f.TickAsync();
        Sequence(new[] { "D4" }, f.Keyboard.Keys, "wall-clock shift cannot bypass input protection");
        EndProtection(f); Due(f); await f.TickAsync();
        Check(f.State.PendingSpiritmasterPetHpIncreaseConfirmation?.SkillId == Emergency, "monotonic elapsed time ends protection");
    }

    public static async Task ExactPlayerFloorAsync()
    {
        using var f = await StartAsync();
        f.Api.Player = f.Api.Player with { CurrentHp = 80 };
        EndProtection(f); Due(f); await f.TickAsync();
        Sequence(new[] { "D4", "D5" }, f.Keyboard.Keys, "exactly 80 percent is eligible");
    }

    public static async Task ConfiguredWindowAsync()
    {
        using var f = await StartAsync();
        f.Settings.SemiAuto.ConfirmTimeoutMs = 4000;
        EndProtection(f); Due(f); await f.TickAsync();
        Sequence(new[] { "D4" }, f.Keyboard.Keys, "longer configured confirmation window remains protected");
        EndProtection(f); Due(f); await f.TickAsync();
        Sequence(new[] { "D4", "D5" }, f.Keyboard.Keys, "handoff after configured window");
    }

    public static async Task StaleInitialObservationAsync()
    {
        using var f = await StartAsync();
        var initial = f.Api.SummonedPet;
        f.Api.SummonedPet = initial with { CurrentHp = 26, HpPercent = 26 };
        EndProtection(f);
        f.State.DeferSpiritmasterPetHpIncreaseConfirmation(DateTimeOffset.Now.AddSeconds(-1), TimeSpan.Zero);
        await f.TickAsync();
        Sequence(new[] { "D4" }, f.Keyboard.Keys, "same captured observation cannot justify a different input");
    }

    public static async Task AlternativeGatesAsync()
    {
        foreach (var gate in new[] { "hp_floor", "threshold", "cooling", "missing", "same_priority", "disabled", "same_skill" })
        {
            using var f = await StartAsync();
            var rule = f.Settings.Skills.Spiritmaster.PetHpMaintenanceRules[1];
            if (gate == "hp_floor") f.Api.Player = f.Api.Player with { CurrentHp = 79 };
            if (gate == "threshold") rule.BelowPercent = 25;
            if (gate == "cooling") f.Confirm(Emergency);
            if (gate == "missing") f.Api.Skills = f.Api.Skills.Where(s => s.SkillId != Emergency).ToArray();
            if (gate == "same_priority") rule.BelowPercent = 40;
            if (gate == "disabled") rule.Key = "";
            if (gate == "same_skill") rule.SkillId = Routine;
            EndProtection(f); Due(f); await f.TickAsync();
            Check(f.State.PendingSpiritmasterPetHpIncreaseConfirmation?.SkillId == Routine && !f.Keyboard.Keys.Contains("D5"),
                "unavailable or non-urgent alternative cannot take ownership: " + gate);
        }
    }

    public static async Task FreshSafetyChecksAsync()
    {
        foreach (var change in new[] { "low_player", "dead_player", "pet_replaced", "pet_dead", "pet_recovered", "stale_pet" })
        {
            using var f = await StartAsync();
            var initial = f.Api.SummonedPet;
            var reads = 0;
            f.Observer.BeforePetHealthRead = _ =>
            {
                if (++reads != 2) return;
                if (change == "low_player") f.Api.Player = f.Api.Player with { CurrentHp = 79 };
                if (change == "dead_player") f.Api.Player = f.Api.Player with { CurrentHp = 0 };
                if (change == "pet_replaced") f.SetPet(12, 26);
                if (change == "pet_dead") f.SetPet(11, 0);
                if (change == "pet_recovered") f.SetPet(11, 35);
                if (change == "stale_pet") f.Api.SummonedPet = initial;
            };
            EndProtection(f); Due(f); await f.TickAsync();
            Check(reads >= 2 && !f.Keyboard.Keys.Contains("D5"), "late unsafe observation prevents new skill input: " + change);
        }
    }

    public static async Task FreshCooldownChecksAsync()
    {
        foreach (var change in new[] { "cooling", "missing" })
        {
            using var f = await StartAsync();
            f.Observer.BeforePetHealthRead = _ =>
            {
                if (change == "cooling") f.Confirm(Emergency);
                else f.Api.Skills = f.Api.Skills.Where(s => s.SkillId != Emergency).ToArray();
            };
            EndProtection(f); Due(f); await f.TickAsync();
            Check(!f.Keyboard.Keys.Contains("D5") && f.State.PendingSpiritmasterPetHpIncreaseConfirmation?.SkillId == Routine,
                "fresh exact skill check preserves old ownership: " + change);
        }
    }

    public static async Task FailedInputKeepsReservationAsync()
    {
        using var f = await StartAsync();
        f.Keyboard.PressResult = key => key == "D5" ? OperationResult.Fail("input rejected") : OperationResult.Ok();
        EndProtection(f); Due(f); await f.TickAsync();
        Sequence(new[] { "D4", "D5" }, f.Keyboard.Keys, "test reached and rejected the alternative delivery");
        Check(f.State.PendingSpiritmasterPetHpIncreaseConfirmation?.SkillId == Routine,
            "rejected delivery never transfers the confirmation");
        Check(!f.Logged("semi_auto.spiritmaster.pet_hp.handoff", "toSkillId", Emergency), "failure is not handoff success");
        var count = f.Keyboard.Keys.Count;
        await f.TickAsync();
        Check(f.Keyboard.Keys.Count == count, "failed delivery respects retry spacing");
    }

    public static async Task StopPreventsInputAsync()
    {
        using var f = await StartAsync();
        var reads = 0;
        f.Observer.BeforePetHealthRead = _ => { if (++reads == 2) f.Cancel(); };
        EndProtection(f); Due(f);
        try { await f.TickAsync(); throw new InvalidOperationException("stop must propagate"); }
        catch (OperationCanceledException) { }
        Sequence(new[] { "D4" }, f.Keyboard.Keys, "stop during final checks cannot send the alternative");
    }

    public static async Task RulesChangeDuringChecksAsync()
    {
        foreach (var change in new[] { "candidate_key", "candidate_threshold", "candidate_floor", "current_priority", "current_identity" })
        {
            using var f = await StartAsync();
            var reads = 0;
            f.Observer.BeforePetHealthRead = _ =>
            {
                if (++reads != 2) return;
                var rules = f.Settings.Skills.Spiritmaster.PetHpMaintenanceRules;
                if (change == "candidate_key") rules[1].Key = "D6";
                if (change == "candidate_threshold") rules[1].BelowPercent = 10;
                if (change == "candidate_floor") rules[1].MinPlayerHpPercent = 95;
                if (change == "current_priority") rules[0].BelowPercent = 20;
                if (change == "current_identity") rules[0].SkillId = Emergency;
            };
            EndProtection(f); Due(f); await f.TickAsync();
            Check(reads >= 2, "rule changed during final checks");
            Sequence(new[] { "D4" }, f.Keyboard.Keys, "changed rules cancel this handoff: " + change);
        }
    }

    public static async Task NoPriorityOscillationAsync()
    {
        using var f = await StartAsync();
        EndProtection(f); Due(f); await f.TickAsync();
        EndProtection(f); Due(f, 20); await f.TickAsync();
        Sequence(new[] { "D4", "D5", "D5" }, f.Keyboard.Keys, "lower-priority rule cannot take ownership back");
    }

    public static async Task RetainsExpiryAsync()
    {
        using var f = await StartAsync();
        var expiry = f.State.PendingSpiritmasterPetHpIncreaseConfirmation!.ExpiresAt;
        EndProtection(f); Due(f); await f.TickAsync();
        Check(f.State.PendingSpiritmasterPetHpIncreaseConfirmation!.SkillId == Emergency &&
            f.State.PendingSpiritmasterPetHpIncreaseConfirmation.ExpiresAt == expiry,
            "handoff cannot restart the overall 30-second reservation lifetime");
    }

    public static async Task InsufficientLifetimeAsync()
    {
        using var f = await StartAsync();
        var p = f.State.PendingSpiritmasterPetHpIncreaseConfirmation!;
        typeof(Roadhog.Application.SemiAuto.SemiAutoCombatState)
            .GetField("pendingSpiritmasterPetHpIncreaseConfirmation", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .SetValue(f.State, p with { ExpiresAt = DateTimeOffset.Now.AddMilliseconds(500) });
        EndProtection(f); Due(f); await f.TickAsync();
        Check(!f.Keyboard.Keys.Contains("D5"), "cannot start an alternative without room to protect its input");
    }

    public static async Task RecoveryStillYieldsAsync()
    {
        using var f = await StartAsync();
        EndProtection(f); Due(f); await f.TickAsync();
        f.SetPet(11, 35); f.Confirm(Emergency);
        await f.TickAsync();
        Check(f.State.PendingSpiritmasterPetHpIncreaseConfirmation is null && f.Keyboard.Keys.Last() == "D1",
            "real HP increase still ends the reservation and yields to ordinary combat");
    }

    public static async Task AccountIsolationAsync()
    {
        using var first = await StartAsync(); using var second = await StartAsync();
        EndProtection(first); Due(first); await first.TickAsync();
        Check(second.State.PendingSpiritmasterPetHpIncreaseConfirmation?.SkillId == Routine && second.Keyboard.Keys.Count == 1,
            "one account's handoff cannot change another account");
    }
}
