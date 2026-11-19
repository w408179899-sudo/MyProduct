using Roadhog.Core.Accounts;
using Roadhog.Core.Model;
using static SharedSkillAuditFixture;

internal static class CoordinatedSummonRecoveryTests
{
    public static async Task LowHpPrecedesSummonAsync()
    {
        using var f = await CreateAsync();
        Check(await f.Controller.EnsureSpiritmasterPetAsync(f.Context, f.Plan, f.State), "missing pet owns the tick");
        Sequence(new[] { "D2" }, f.Keyboard.Keys, "V2 offers configured HP recovery before the first summon");
        Check(!f.State.HasPendingSpiritmasterSummonVerification, "HP recovery does not claim a summon was sent");
    }

    public static async Task ConfirmationReservesInputAsync()
    {
        using var f = await CreateAsync();
        f.State.BeginSpiritmasterSummonVerification(DateTimeOffset.Now, TimeSpan.FromMinutes(1));
        await EnsureAsync(f);
        Sequence(Array.Empty<string>(), f.Keyboard.Keys, "HP must not interrupt a pending summon");
        Check(f.State.HasPendingSpiritmasterSummonVerification, "waiting preserves confirmation");
        f.SetPet(12, 100);
        Check(!await f.Controller.EnsureSpiritmasterPetAsync(f.Context, f.Plan, f.State), "appearance releases the summon reservation");
        Sequence(Array.Empty<string>(), f.Keyboard.Keys, "confirmed pet returns normal control without extra input");
    }

    public static async Task ExpiredConfirmationYieldsHpAsync()
    {
        using var f = await CreateAsync();
        f.State.BeginSpiritmasterSummonVerification(DateTimeOffset.Now.AddSeconds(-10), TimeSpan.FromSeconds(5));
        await EnsureAsync(f);
        Sequence(new[] { "D2" }, f.Keyboard.Keys, "expired summon yields HP before another summon");
        Check(f.Logger.Entries.Any(e => e.EventName == "semi_auto.spiritmaster.summon_verify_timeout"), "timeout remains observable");
        Check(!f.State.HasPendingSpiritmasterSummonVerification, "expired reservation is released");
    }

    public static async Task HpFailureCannotStarveSummonAsync()
    {
        using var f = await CreateAsync();
        f.Keyboard.AfterPress = null; // No cooldown confirmation; exercise the real bounded six-second timeout.
        await EnsureAsync(f);
        Check(f.Logger.Entries.Any(e => e.EventName == "semi_auto.maintenance.unconfirmed"), "fixture exercised unconfirmed recovery");
        Check(f.Keyboard.Keys.Count > 0 && f.Keyboard.Keys.All(key => key == "D2"), "only HP inputs own the first turn");
        f.State.ClearMaintenanceKeyAttempt("D2");
        Check(f.State.ShouldPressMaintenanceKey("D2", DateTimeOffset.Now, TimeSpan.FromSeconds(3)), "HP is eligible again");
        f.Keyboard.Keys.Clear();
        await EnsureAsync(f);
        Sequence(new[] { "NumPad8" }, f.Keyboard.Keys, "one unconfirmed HP turn still hands input to summoning");
        Check(f.State.HasPendingSpiritmasterSummonVerification, "summon confirmation starts after fair handoff");
    }

    public static async Task NoHpWorkStillSummonsAsync(string reason)
    {
        using var f = await CreateAsync();
        switch (reason)
        {
            case "healthy": f.Api.Player = f.Api.Player with { CurrentHp = f.Api.Player.MaxHp }; break;
            case "cooling": f.Confirm(LowHpId); break;
            case "missing": f.Settings.Maintenance.HpMaintenanceRules.Clear(); break;
            case "after_combat": f.Settings.Maintenance.HpMaintenanceRules[0].RunTiming = MaintenanceRuleRunTiming.AfterCombat; break;
        }
        await EnsureAsync(f);
        Sequence(new[] { "NumPad8" }, f.Keyboard.Keys, reason + " cannot stall summoning");
    }

    public static async Task OnlyHpGetsPriorityAsync()
    {
        using var f = await CreateAsync();
        f.Settings.Maintenance.HpMaintenanceRules.Clear();
        f.ConfigureResource("mp");
        f.Api.Player = f.Api.Player with { CurrentMp = 1 };
        await f.PrepareAsync();
        await EnsureAsync(f);
        Sequence(new[] { "NumPad8" }, f.Keyboard.Keys, "unrelated MP maintenance cannot occupy this HP handoff");
    }

    public static async Task StopDuringPreparationSendsNoInputAsync(bool hpTurn)
    {
        using var f = await CreateAsync();
        if (!hpTurn) f.Settings.Maintenance.HpMaintenanceRules.Clear();
        var cancelled = false;
        try
        {
            await f.Controller.EnsureSpiritmasterPetAsync(f.Context, f.Plan, f.State,
                () => { f.Cancel(); return Task.CompletedTask; });
        }
        catch (OperationCanceledException) { cancelled = true; }
        Check(cancelled, "stop after movement release must propagate");
        Sequence(Array.Empty<string>(), f.Keyboard.Keys, "stop prevents both recovery and summon inputs");
    }

    public static async Task LivingPetResetsHandoffAsync()
    {
        using var f = await CreateAsync();
        // A short fixture cooldown genuinely expires during the maintenance gap;
        // replacing a still-cooling snapshot with zero must not bypass its clock.
        f.Api.Skills = f.Api.Skills.Select(skill => skill.SkillId == LowHpId ? skill with { CooldownDuration = 100 } : skill).ToArray();
        await EnsureAsync(f);
        f.SetPet(12, 100);
        Check(!await f.Controller.EnsureSpiritmasterPetAsync(f.Context, f.Plan, f.State), "live pet ends missing-pet episode");
        f.SetPet(12, 0);
        f.State.ClearMaintenanceKeyAttempt("D2");
        f.Keyboard.Keys.Clear();
        await EnsureAsync(f);
        Sequence(new[] { "D2" }, f.Keyboard.Keys, "a new missing-pet episode gets a new HP opportunity");
    }

    public static async Task DrainTargetGuardSurvivesHandoffAsync()
    {
        using var f = await CreateAsync();
        f.Api.Skills = f.Api.Skills.Select(skill => skill.SkillId == LowHpId
            ? skill with { XmlFirstTarget = "target", XmlTargetRelationRestriction = "enemy", XmlEffects = "SkillATKDrain_Instant" }
            : skill).ToArray();
        f.Settings.Maintenance.HpMaintenanceRules[0].RunTiming = MaintenanceRuleRunTiming.InCombat;
        // The target dies after eligibility checks and before the first HP input.
        await f.Controller.EnsureSpiritmasterPetAsync(f.Context, f.Plan, f.State,
            () => { f.Api.TargetCurrentHp = 0; return Task.CompletedTask; });
        Sequence(Array.Empty<string>(), f.Keyboard.Keys, "dead target never receives a drain input or same-tick summon");
        Check(f.Logger.Entries.Any(e => e.EventName == "semi_auto.maintenance.target_guard_blocked"), "existing per-attempt drain guard remains active");
        await EnsureAsync(f);
        Sequence(new[] { "NumPad8" }, f.Keyboard.Keys, "blocked drain cannot reserve future summon attempts");
    }

    private static async Task EnsureAsync(SharedSkillAuditFixture f) =>
        Check(await f.Controller.EnsureSpiritmasterPetAsync(f.Context, f.Plan, f.State), "missing pet owns the tick");

    private static async Task<SharedSkillAuditFixture> CreateAsync()
    {
        var f = new SharedSkillAuditFixture();
        f.Settings.Skills.Spiritmaster.SummonSkills = new() { new() { Key = "NumPad8" } };
        f.ConfigureResource("hp");
        f.SetPet(11, 0);
        f.Keyboard.AfterPress = key => { if (key == "D2") f.Confirm(LowHpId); };
        await f.PrepareAsync();
        return f;
    }
}
