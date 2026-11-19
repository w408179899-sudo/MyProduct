using System.Reflection;
using Roadhog.Application.SemiAuto;
using Roadhog.Core.Accounts;
using Roadhog.Core.Common;
using Roadhog.Core.Model;
using static SharedSkillAuditFixture;

internal static class CoordinatedPetRecoveryEvidenceTests
{
    private const uint Heal = 1678;
    public static IEnumerable<(string, Func<Task>)> Cases => new (string, Func<Task>)[]
    {
        ("combat flow v2 pet recovery HP rise alone does not confirm release", HpOnlyAsync),
        ("combat flow v2 pet recovery CD and HP confirm exact skill", BothAsync),
        ("combat flow v2 pet recovery CD alone keeps reservation", CdOnlyAsync),
        ("combat flow v2 pet recovery release evidence survives later zero endpoint", StickyAsync),
        ("combat flow v2 pet recovery unrelated cooldown cannot confirm", OtherSkillAsync),
        ("combat flow v2 pet recovery retries retain initial cooldown baseline", RetryBaselineAsync),
        ("combat flow v2 pet recovery preexisting cooldown is not release evidence", OldCooldownAsync),
        ("combat flow v2 pet recovery backward endpoint is not release evidence", BackwardAsync),
        ("combat flow v2 pet recovery recovered threshold cancels without claiming success", ThresholdAsync),
        ("combat flow v2 pet recovery pet replacement clears release evidence", ReplacementAsync),
        ("combat flow v2 pet recovery evidence is account local", IsolationAsync),
        ("combat flow v2 pet recovery no evidence still expires", ExpiryAsync),
        ("combat flow v2 pet recovery rejected key records no evidence", RejectedAsync),
        ("combat flow v2 pet recovery MP initial attempt yields to eligible heal", MpInitialAsync),
        ("combat flow v2 pet recovery MP retries yield when pet deteriorates", MpRetryAsync),
        ("combat flow v2 pet recovery MP yields only to eligible rules", MpGatesAsync),
        ("combat flow v2 pet recovery MP handoff rechecks fresh player safety", MpFreshSafetyAsync),
        ("combat flow v2 pet recovery MP resumes after confirmed pet recovery", MpResumesAsync),
        ("combat flow v2 pet recovery MP handoff does not interrupt HP maintenance", HpPriorityAsync),
        ("combat flow v2 pet recovery MP handoff cancellation sends no further input", MpCancelAsync),
        ("combat flow v2 pet recovery MP handoff rejected heal never resumes MP inline", MpRejectedAsync),
        ("combat flow v2 pet recovery confirmed routine heal can hand off without HP gain", EmergencyAsync),
    };

    private static async Task<SharedSkillAuditFixture> CreateAsync(uint petHp = 20)
    {
        var f = new SharedSkillAuditFixture();
        f.AddPetHpRule(Heal, 80);
        f.Settings.Skills.Spiritmaster.PetHpMaintenanceRules[0].BelowPercent = 30;
        f.SetPet(11, petHp); await f.PrepareAsync(); return f;
    }
    private static bool Confirmed(SharedSkillAuditFixture f) => f.Logger.Entries.Any(e => e.EventName == "semi_auto.spiritmaster.pet_hp.pet_hp_confirmed");
    private static void Due(SharedSkillAuditFixture f, uint hp)
    {
        f.SetPet(11, hp);
        f.State.DeferSpiritmasterPetHpIncreaseConfirmation(DateTimeOffset.Now.AddSeconds(-1), TimeSpan.Zero);
    }
    private static async Task HpOnlyAsync()
    {
        using var f = await CreateAsync(); await f.TickAsync(); f.SetPet(11, 21); await f.TickAsync();
        Check(f.State.PendingSpiritmasterPetHpIncreaseConfirmation is not null && !Confirmed(f), "small regen does not release an unconfirmed action");
        Sequence(new[] { "D4" }, f.Keyboard.Keys, "ordinary attacks remain reserved");
        Check(f.Logger.Entries.Any(e => e.EventName == "combat_flow.pet_hp.hp_rise_without_release"), "diagnostics distinguish HP recovery from skill release");
    }
    private static async Task BothAsync()
    {
        using var f = await CreateAsync(); await f.TickAsync(); f.Confirm(Heal); f.SetPet(11, 25); await f.TickAsync();
        Check(f.State.PendingSpiritmasterPetHpIncreaseConfirmation is null && Confirmed(f), "both independent observations confirm recovery");
        Check(f.Keyboard.Keys.Contains("D1"), "ordinary work resumes after real confirmation");
    }
    private static async Task CdOnlyAsync()
    {
        using var f = await CreateAsync(); await f.TickAsync(); f.Confirm(Heal); Due(f, 19); await f.TickAsync();
        Check(f.State.PendingSpiritmasterPetHpIncreaseConfirmation?.ReleasedCooldownEndTime is not null && !Confirmed(f), "release alone cannot claim healing");
        Sequence(new[] { "D4" }, f.Keyboard.Keys, "cooling skill is not re-pressed or bypassed by an attack");
    }
    private static async Task StickyAsync()
    {
        using var f = await CreateAsync(); await f.TickAsync(); f.Confirm(Heal); f.SetPet(11, 19); await f.TickAsync();
        f.Api.Skills = f.Api.Skills.Select(s => s.SkillId == Heal ? s with { CooldownEndTime = 0 } : s).ToArray();
        f.SetPet(11, 25); await f.TickAsync();
        Check(Confirmed(f) && f.State.PendingSpiritmasterPetHpIncreaseConfirmation is null, "a later endpoint reset does not erase accepted release evidence");
    }
    private static async Task OtherSkillAsync()
    {
        using var f = await CreateAsync(); await f.TickAsync(); f.Confirm(LowMpId); f.SetPet(11, 21); await f.TickAsync();
        Check(!Confirmed(f) && f.State.PendingSpiritmasterPetHpIncreaseConfirmation is not null, "unrelated skill CD must never validate a pet heal");
    }
    private static async Task RetryBaselineAsync()
    {
        using var f = await CreateAsync(); await f.TickAsync(); Due(f, 18); await f.TickAsync();
        Check(f.State.PendingSpiritmasterPetHpIncreaseConfirmation?.BaselineCooldownEndTime == 0, "retry keeps its original release baseline");
        f.Confirm(Heal); f.SetPet(11, 22); await f.TickAsync();
        Check(Confirmed(f), "an eventual successful retry confirms");
    }
    private static async Task OldCooldownAsync()
    {
        using var f = await CreateAsync();
        var old = unchecked((uint)Environment.TickCount64 - 5000);
        f.Api.Skills = f.Api.Skills.Select(s => s.SkillId == Heal ? s with { CooldownEndTime = old } : s).ToArray();
        await f.TickAsync(); f.SetPet(11, 21); await f.TickAsync();
        Check(!Confirmed(f) && f.State.PendingSpiritmasterPetHpIncreaseConfirmation?.BaselineCooldownEndTime == old,
            "an unchanged historical endpoint is not a newly accepted cast");
    }
    private static async Task BackwardAsync()
    {
        using var f = await CreateAsync(); var old = unchecked((uint)Environment.TickCount64 - 5000);
        f.Api.Skills = f.Api.Skills.Select(s => s.SkillId == Heal ? s with { CooldownEndTime = old } : s).ToArray();
        await f.TickAsync();
        f.Api.Skills = f.Api.Skills.Select(s => s.SkillId == Heal ? s with { CooldownEndTime = old - 100 } : s).ToArray();
        f.SetPet(11, 21); await f.TickAsync(); Check(!Confirmed(f), "backward CD movement cannot confirm");
    }
    private static async Task ThresholdAsync()
    {
        using var f = await CreateAsync(); await f.TickAsync(); f.SetPet(11, 40); await f.TickAsync();
        Check(!Confirmed(f) && f.State.PendingSpiritmasterPetHpIncreaseConfirmation is null, "recovery above the configured threshold ends the need, not proof of release");
    }
    private static async Task ReplacementAsync()
    {
        using var f = await CreateAsync(); await f.TickAsync(); f.Confirm(Heal); f.SetPet(11, 19); await f.TickAsync();
        f.SetPet(12, 25); await f.TickAsync();
        Check(!Confirmed(f) && f.State.PendingSpiritmasterPetHpIncreaseConfirmation is null, "another pet cannot complete the old pet's confirmation");
    }
    private static async Task IsolationAsync()
    {
        using var a = await CreateAsync(); using var b = await CreateAsync(); await a.TickAsync(); await b.TickAsync();
        a.Confirm(Heal); a.SetPet(11, 25); b.SetPet(11, 25); await a.TickAsync(); await b.TickAsync();
        Check(Confirmed(a) && !Confirmed(b) && b.State.PendingSpiritmasterPetHpIncreaseConfirmation is not null, "release evidence is not shared");
    }
    private static async Task ExpiryAsync()
    {
        using var f = await CreateAsync(); await f.TickAsync();
        var p = f.State.PendingSpiritmasterPetHpIncreaseConfirmation!;
        typeof(SemiAutoCombatState).GetField("pendingSpiritmasterPetHpIncreaseConfirmation", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(f.State, p with { ExpiresAt = DateTimeOffset.Now.AddSeconds(-1) });
        await f.TickAsync(); Check(f.State.PendingSpiritmasterPetHpIncreaseConfirmation is null && !Confirmed(f), "stronger evidence cannot create an infinite reservation");
    }
    private static async Task RejectedAsync()
    {
        using var f = await CreateAsync(); f.Keyboard.PressResult = _ => OperationResult.Fail("test rejection"); await f.TickAsync();
        Check(f.State.PendingSpiritmasterPetHpIncreaseConfirmation is null && !Confirmed(f), "rejected input cannot own release evidence");
    }
    private static async Task MpInitialAsync()
    {
        using var f = await CreateAsync(); f.ConfigureResource("mp"); await f.PrepareAsync(); await f.MaintainAsync();
        Sequence(new[] { "D4" }, f.Keyboard.Keys, "ready pet heal precedes the first MP key");
        Check(f.State.PendingSpiritmasterPetHpIncreaseConfirmation?.SkillId == Heal, "pet confirmation owns the next tick");
    }
    private static async Task MpRetryAsync()
    {
        using var f = await CreateAsync(80); f.ConfigureResource("mp"); await f.PrepareAsync();
        f.Keyboard.AfterPress = key => { if (key == "D3") f.SetPet(11, 20); };
        await f.MaintainAsync(); Sequence(new[] { "D3", "D4" }, f.Keyboard.Keys, "pet damage during MP confirmation prevents a second MP key");
        Check(!f.Logger.Entries.Any(e => e.EventName == "semi_auto.maintenance.unconfirmed"), "deliberate handoff is not a timeout");
    }
    private static async Task MpGatesAsync()
    {
        foreach (var gate in new[] { "floor", "cooldown", "missing", "healthy", "dead_pet", "disabled", "other_class" })
        {
            using var f = await CreateAsync(); f.ConfigureResource("mp");
            if (gate == "floor") f.Api.Player = f.Api.Player with { CurrentHp = 79 };
            if (gate == "cooldown") f.Confirm(Heal);
            if (gate == "missing") f.Api.Skills = f.Api.Skills.Where(s => s.SkillId != Heal).ToArray();
            if (gate == "healthy") f.SetPet(11, 31);
            if (gate == "dead_pet") f.SetPet(11, 0);
            if (gate == "disabled") f.Settings.Skills.SpiritmasterAutoSkillLogicEnabled = false;
            if (gate == "other_class") f.Api.Player = f.Api.Player with { CharacterClassId = AionClassId.Sorcerer };
            await f.PrepareAsync(); f.Keyboard.AfterPress = _ => f.Confirm(LowMpId); await f.MaintainAsync();
            Sequence(new[] { "D3" }, f.Keyboard.Keys, "unusable pet rule cannot starve MP: " + gate);
        }
    }
    private static async Task MpFreshSafetyAsync()
    {
        using var f = await CreateAsync(); f.ConfigureResource("mp"); await f.PrepareAsync();
        var reads = 0; f.Observer.BeforePetHealthRead = _ => { if (++reads == 2) f.Api.Player = f.Api.Player with { CurrentHp = 79 }; };
        f.Keyboard.AfterPress = _ => f.Confirm(LowMpId); await f.MaintainAsync();
        Sequence(new[] { "D3" }, f.Keyboard.Keys, "late low player HP retracts the heal proposal");
    }
    private static async Task MpResumesAsync()
    {
        using var f = await CreateAsync(); f.ConfigureResource("mp"); await f.PrepareAsync(); await f.MaintainAsync();
        f.Confirm(Heal); f.SetPet(11, 60); f.Keyboard.AfterPress = key => { if (key == "D3") f.Confirm(LowMpId); };
        await f.TickAsync(); await f.MaintainAsync();
        Check(f.Keyboard.Keys.Contains("D3") && f.State.PendingSpiritmasterPetHpIncreaseConfirmation is null, "MP remains eligible after pet confirmation");
    }
    private static async Task HpPriorityAsync()
    {
        using var f = await CreateAsync(); f.ConfigureResource("hp"); await f.PrepareAsync();
        f.Keyboard.AfterPress = _ => f.Confirm(LowHpId); await f.MaintainAsync();
        Sequence(new[] { "D2" }, f.Keyboard.Keys, "this MP-only handoff never steals human HP maintenance");
    }
    private static async Task MpCancelAsync()
    {
        using var f = await CreateAsync(80); f.ConfigureResource("mp"); await f.PrepareAsync();
        f.Keyboard.AfterPress = _ => { f.SetPet(11, 20); f.Cancel(); }; var cancelled = false;
        try { await f.MaintainAsync(); } catch (OperationCanceledException) { cancelled = true; }
        Check(cancelled, "stop remains immediate"); Sequence(new[] { "D3" }, f.Keyboard.Keys, "no MP or heal after stop");
    }
    private static async Task MpRejectedAsync()
    {
        using var f = await CreateAsync(); f.ConfigureResource("mp"); await f.PrepareAsync();
        f.Keyboard.PressResult = _ => OperationResult.Fail("test rejection"); await f.MaintainAsync();
        Sequence(new[] { "D4" }, f.Keyboard.Keys, "failed urgent input returns to the worker rather than resuming MP inline");
        Check(f.State.PendingSpiritmasterPetHpIncreaseConfirmation is null, "failed input creates no reservation");
    }
    private static async Task EmergencyAsync()
    {
        using var f = new SharedSkillAuditFixture();
        f.AddPetHpRule(Heal, 80); f.Settings.Skills.Spiritmaster.PetHpMaintenanceRules[0].BelowPercent = 30;
        f.AddPetHpRule(1785); f.SetPet(11, 31);
        f.Settings.Skills.Spiritmaster.PetHpMaintenanceRules[1].BelowPercent = 40;
        f.Api.Quickbar = f.Api.Quickbar with { Slots = f.Api.Quickbar.Slots.Where(s => s.SkillId != 1785).Append(new(SkillQuickbar.Main, 4, 21, 1785)).ToArray() };
        await f.PrepareAsync(); await f.TickAsync(); f.Confirm(1785); f.Clock.Advance(2000); Due(f, 20); await f.TickAsync();
        Sequence(new[] { "D5", "D4" }, f.Keyboard.Keys, "routine release without adequate HP cannot block a ready emergency heal");
        Check(f.State.PendingSpiritmasterPetHpIncreaseConfirmation?.SkillId == Heal &&
            f.State.PendingSpiritmasterPetHpIncreaseConfirmation.ReleasedCooldownEndTime is null, "handoff starts independent exact-skill evidence");
    }
}
