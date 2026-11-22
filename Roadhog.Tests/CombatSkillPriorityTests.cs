using Roadhog.Application.SemiAuto;
using Roadhog.Core.Accounts;
using Roadhog.Core.Model;
using static SharedSkillAuditFixture;

internal static class CombatSkillPriorityTests
{
    private const uint ShieldId = 1390;
    private const string ShieldKey = "D4";
    private const uint ReplacementAttackId = 910;
    private const string PetAttackKey = "D7";

    public static async Task PetAttackTakesFirstEligibleTurnAsync()
    {
        using var f = await CreateAsync(includePetAttack: true);
        f.Api.Player = f.Api.Player with { CurrentHp = 30 };

        await f.TickAsync();
        Sequence(new[] { PetAttackKey, "D2" }, f.Keyboard.Keys,
            "the pet command comes first and the due heal keeps priority for the follow-up input");
        f.State.MarkMaintenanceKeyAttempted("D2", DateTimeOffset.Now.AddMilliseconds(-650));
        await f.TickAsync();
        Sequence(new[] { PetAttackKey, "D2", ShieldKey }, f.Keyboard.Keys,
            "the shield takes priority once healing is done and the pet command is recorded for this target");
    }

    public static async Task PetAttackCanPreemptPendingShieldAsync()
    {
        using var f = await CreateAsync(includePetAttack: true);
        await f.TickAsync();
        Sequence(new[] { PetAttackKey, ShieldKey }, f.Keyboard.Keys,
            "the first target receives the pet attack and then the shield starts in the same tick");

        f.Api.TargetOwnServerObjectId++;
        f.Clock.Advance(300);
        await f.TickAsync();
        Sequence(new[] { PetAttackKey, ShieldKey, PetAttackKey, "D1" }, f.Keyboard.Keys,
            "the new target receives the pet command before ordinary input while the earlier shield awaits its effect");
        Check(f.State.CombatStatusAttempts.Values.Single().Awaiting, "the pet command retains the shield confirmation");

        SetShield(f);
        f.Clock.Advance(300);
        await f.TickAsync();
        Check(f.Logged("quickbar_skill.release.confirmed", "skillId", ShieldId), "the retained shield still confirms from its effect");
    }

    public static async Task AttackYieldsAndResumesAsync()
    {
        using var f = await CreateAsync();
        SetShield(f);
        await f.TickAsync();
        var original = f.State.QuickbarSkills.PendingAction;
        Check(original is not null, "the first ordinary action must await confirmation");

        f.Clock.Advance(80);
        await f.TickAsync();
        Sequence(new[] { "D1", "D1" }, f.Keyboard.Keys, "no higher priority action displaces the retry");
        f.Api.Player = f.Api.Player with { CurrentHp = 30 };
        await f.TickAsync();
        Sequence(new[] { "D1", "D1", "D2" }, f.Keyboard.Keys, "ready emergency heal owns the next input");
        Check(f.State.QuickbarSkills.PendingAction?.AttemptId == original!.AttemptId,
            "the interrupted attack remains associated with the initial attempt");

        f.Clock.Advance(80);
        await f.TickAsync();
        Sequence(new[] { "D1", "D1", "D2", "D1" }, f.Keyboard.Keys, "the same attack resumes when the heal is no longer needed");
        Check(f.State.QuickbarSkills.PendingAction?.AttemptCount == 3, "continuing A preserves its retry budget");
        Release(f, AttackId, 200);
        await f.TickAsync();
        Check(f.State.QuickbarSkills.PendingAction is null &&
            f.Logged("quickbar_skill.release.confirmed", "skillId", AttackId), "the resumed attack is confirmed");
    }

    public static async Task LateAttackDoesNotReplayAsync()
    {
        using var f = await CreateAsync();
        SetShield(f);
        await f.TickAsync();
        f.Api.Player = f.Api.Player with { CurrentHp = 30 };
        await f.TickAsync();

        Release(f, AttackId, 200);
        f.Clock.Advance(80);
        await f.TickAsync();
        Sequence(new[] { "D1", "D2" }, f.Keyboard.Keys, "late confirmation wins over retry after the heal");
        Check(f.Logged("quickbar_skill.release.confirmed", "skillId", AttackId), "confirmation stays attached to A");
    }

    public static async Task CoolingHealLeavesAttackAsync()
    {
        using var f = await CreateAsync();
        SetShield(f);
        f.Confirm(LowHpId);
        await f.TickAsync();
        var original = f.State.QuickbarSkills.PendingAction;
        f.Api.Player = f.Api.Player with { CurrentHp = 30 };
        f.Clock.Advance(80);
        await f.TickAsync();
        Sequence(new[] { "D1", "D1" }, f.Keyboard.Keys, "a higher priority heal in cooldown leaves the ready attack eligible");
        Check(original is not null && f.State.QuickbarSkills.PendingAction?.AttemptId == original.AttemptId,
            "an unavailable heal never takes the attack's input ownership");
    }

    public static async Task ShieldPreemptsAndRetainsAttackRetryAsync()
    {
        using var f = await CreateAsync();
        SetShield(f);
        await f.TickAsync();
        var original = f.State.QuickbarSkills.PendingAction;
        Check(original?.Node.SkillId == AttackId, "the ordinary skill starts the first attempt");

        f.Api.PlayerAbnormalStatuses = PlayerAbnormalStatusSnapshot.Empty(f.Api.Player.EntityId);
        f.Clock.Advance(80);
        await f.TickAsync();
        Sequence(new[] { "D1", ShieldKey }, f.Keyboard.Keys, "a newly ready shield wins over the ready attack retry");
        Check(f.State.CombatStatusAttempts.Values.Single().Awaiting && f.State.QuickbarSkills.PendingAction == original,
            "shield reserves its own evidence without replacing A or counting a retry for it");
        Check(!f.Logged("quickbar_skill.release.confirmed", "skillId", ShieldId), "the shield key alone cannot confirm the effect");

        f.Clock.Advance(80);
        await f.TickAsync();
        Sequence(new[] { "D1", ShieldKey, "D1" }, f.Keyboard.Keys, "A can resume while the unavailable shield awaits its effect");
        Check(f.State.QuickbarSkills.PendingAction is { AttemptCount: 2 } retry && retry.AttemptId == original!.AttemptId,
            "the resumed input retains the original attempt and retry budget");

        SetShield(f);
        Release(f, AttackId, 200);
        f.Clock.Advance(300);
        await f.TickAsync();
        Sequence(new[] { "D1", ShieldKey, "D1" }, f.Keyboard.Keys, "confirmed skills are not replayed");
        Check(f.Logged("quickbar_skill.release.confirmed", "skillId", ShieldId) &&
            f.Logged("quickbar_skill.release.confirmed", "skillId", AttackId), "each attempt completes from its own evidence");
    }

    public static async Task PostCombatShieldDoesNotConfirmOldCombatPressAsync()
    {
        using var f = await CreateAsync();
        f.Settings.Maintenance.StatusMaintenanceRules.Single().RunTiming = MaintenanceRuleRunTiming.Always;
        await f.TickAsync();
        var combatAttempt = f.State.CombatStatusAttempts.Values.Single();
        f.Clock.Advance(1600);
        await f.TickAsync();
        Check(!combatAttempt.Awaiting, "the shield input goes unconfirmed during the fight");
        MakeShieldRetryEligible(f);
        f.Keyboard.AfterPress = key => { if (key == ShieldKey) SetShield(f); };

        Check(await f.MaintainAsync(), "the out-of-combat path can perform a newly eligible full-time shield");
        await f.TickAsync();

        Check(f.Keyboard.Keys.Count(key => key == ShieldKey) == 2 && f.State.CombatStatusAttempts.Count == 0,
            "the newly sent shield replaces the old attempt for the same effect");
        var confirmations = f.Logger.Entries.Where(entry => entry.EventName == "semi_auto.maintenance.status_key_pressed" &&
            Equals(entry.Fields["skillId"], ShieldId)).ToArray();
        Check(confirmations is [var confirmed] && !Equals(confirmed.Fields["attemptId"], combatAttempt.Id) &&
            Equals(confirmed.Fields["phase"], "outside_combat_queue") &&
            !f.Logged("quickbar_skill.release.confirmed", "skillId", ShieldId),
            "one effect confirms the new attempt only and never counts as the original combat release");
    }

    public static async Task ShieldPreemptionDoesNotReplayLateAttackAsync()
    {
        using var f = await CreateAsync();
        SetShield(f);
        await f.TickAsync();
        f.Api.PlayerAbnormalStatuses = PlayerAbnormalStatusSnapshot.Empty(f.Api.Player.EntityId);
        f.Clock.Advance(80);
        await f.TickAsync();

        Release(f, AttackId, 200);
        f.Clock.Advance(80);
        await f.TickAsync();

        Sequence(new[] { "D1", ShieldKey }, f.Keyboard.Keys, "A's late release cancels its retry while shield confirmation is pending");
        Check(f.Logged("quickbar_skill.release.confirmed", "skillId", AttackId) &&
            f.State.CombatStatusAttempts.Values.Single().Awaiting, "the two confirmations remain independent");
    }

    public static async Task ShieldCancelsWhenEffectArrivesBeforeHandoffAsync()
    {
        using var f = await CreateAsync();
        f.Api.SkillAvailabilityRead = () =>
        {
            SetShield(f);
            return f.Api.SkillAvailability;
        };

        await f.TickAsync();
        Sequence(new[] { "D1" }, f.Keyboard.Keys, "a buff that appears after nomination yields its turn to the normal skill");
        Check(f.State.CombatStatusAttempts.Count == 0, "no input means no new confirmation attempt");
    }

    public static async Task ShieldWinsTheSlotOfTheNextOrdinaryRootAsync()
    {
        using var f = await CreateAsync(includeReplacement: true);
        SetShield(f);
        await f.TickAsync();
        var original = f.State.QuickbarSkills.PendingAction;
        Check(original?.Node.SkillId == AttackId, "A was sent first and still awaits evidence");

        f.Clock.Advance(80);
        f.Api.PlayerAbnormalStatuses = PlayerAbnormalStatusSnapshot.Empty(f.Api.Player.EntityId);
        f.Api.SkillAvailability = f.Api.SkillAvailability with
        {
            UnsupportedSkillIds = [],
            Slots = [new(SkillQuickbar.Main, 4, 21, ReplacementAttackId, ReplacementAttackId, true)]
        };
        await f.TickAsync();
        Sequence(new[] { "D1", ShieldKey }, f.Keyboard.Keys, "the shield takes the slot the selector would give to the next ordinary root");
        Check(f.State.QuickbarSkills.PendingAction?.AttemptId == original!.AttemptId,
            "the original ordinary attempt remains available for later evidence or retry");

        SetShield(f);
        f.Clock.Advance(300);
        f.Api.SkillAvailability = f.Api.SkillAvailability with { UnsupportedSkillIds = [AttackId], Slots = [] };
        await f.TickAsync();
        Sequence(new[] { "D1", ShieldKey, "D1" }, f.Keyboard.Keys, "the original attack can resume once ready again");
        Check(f.State.QuickbarSkills.PendingAction?.AttemptId == original.AttemptId &&
            f.Logged("quickbar_skill.release.confirmed", "skillId", ShieldId),
            "resumption preserves the original baseline after the shield effect");
    }

    public static async Task ShieldWaitsThroughHealAsync()
    {
        using var f = await StartShieldAndHealAsync();
        f.Clock.Advance(700);
        await f.TickAsync();
        Sequence(new[] { ShieldKey, "D2", "D1" }, f.Keyboard.Keys, "unconfirmed shield yields when its wait ends");
        Check(f.State.CombatStatusAttempts.Values.Single().Awaiting == false &&
            f.Logged("semi_auto.maintenance.status_unconfirmed", "skillId", ShieldId),
            "timeout retains the shield request while the character can fight");

        MakeShieldRetryEligible(f);
        Release(f, AttackId, 200);
        f.Keyboard.AfterPress = key =>
        {
            if (key == ShieldKey) SetShield(f);
        };
        await f.TickAsync();
        Sequence(new[] { ShieldKey, "D2", "D1", ShieldKey }, f.Keyboard.Keys, "the pending shield gets a new eligible turn");
        Check(f.State.CombatStatusAttempts.Count == 0 &&
            f.Logged("semi_auto.maintenance.status_key_pressed", "skillId", ShieldId), "the second shield attempt confirms the actual buff");
    }

    public static async Task ShieldCooldownAndLateBuffAsync()
    {
        using var f = await StartShieldAndHealAsync();
        f.Confirm(ShieldId);
        f.Clock.Advance(700);
        MakeShieldRetryEligible(f);
        await f.TickAsync();
        Check(f.Keyboard.Keys.Count(key => key == ShieldKey) == 1 && f.State.CombatStatusAttempts.Count == 1 &&
            !f.Logged("semi_auto.maintenance.status_key_pressed", "skillId", ShieldId),
            "the shield's cooldown suppresses replay but cannot confirm the effect");

        SetShield(f);
        f.Clock.Advance(80);
        await f.TickAsync();
        Check(f.Keyboard.Keys.Count(key => key == ShieldKey) == 1 && f.State.CombatStatusAttempts.Count == 0 &&
            f.Logged("semi_auto.maintenance.status_key_pressed", "skillId", ShieldId),
            "a late buff completes the original request without another key");
    }

    public static async Task ShieldScopeAsync()
    {
        using var f = await CreateAsync();
        await f.TickAsync();
        f.Api.TargetOwnServerObjectId++;
        f.Clock.Advance(350);
        await f.TickAsync();
        Check(f.State.CombatStatusAttempts.Count == 1, "the self-buff remains pending across monster identities");
        var beforeDeath = f.Keyboard.Keys.ToArray();
        f.Api.Player = f.Api.Player with { CurrentHp = 0 };
        await f.TickAsync();
        Check(f.State.CombatStatusAttempts.Count == 0, "death clears an in-flight shield");
        Sequence(beforeDeath, f.Keyboard.Keys, "death cannot send an old skill");

        using var stopped = await CreateAsync();
        await stopped.TickAsync();
        stopped.Cancel();
        try { await stopped.TickAsync(); throw new InvalidOperationException("expected stop cancellation"); }
        catch (OperationCanceledException) { }
        Check(stopped.State.CombatStatusAttempts.Count == 0, "stop clears an in-flight shield");
        Sequence(new[] { ShieldKey }, stopped.Keyboard.Keys, "stop cannot send an old skill");
    }

    private static async Task<SharedSkillAuditFixture> StartShieldAndHealAsync()
    {
        var f = await CreateAsync();
        try
        {
            await f.TickAsync();
            Check(f.State.CombatStatusAttempts.Values.Single().Awaiting, "shield has not yet taken effect");
            f.Api.Player = f.Api.Player with { CurrentHp = 30 };
            f.State.MarkMaintenanceKeyAttempted(ShieldKey, DateTimeOffset.Now.AddMilliseconds(-650));
            f.Clock.Advance(650);
            await f.TickAsync();
            Sequence(new[] { ShieldKey, "D2" }, f.Keyboard.Keys, "heal can take priority within the buff confirmation window");
            Check(f.State.CombatStatusAttempts.Values.Single().Awaiting, "the heal does not discard the pending shield");
            return f;
        }
        catch
        {
            f.Dispose();
            throw;
        }
    }

    private static async Task<SharedSkillAuditFixture> CreateAsync(bool includeReplacement = false, bool includePetAttack = false)
    {
        var f = new SharedSkillAuditFixture();
        f.Settings.Skills.SpiritmasterAutoSkillLogicEnabled = includePetAttack;
        if (includePetAttack) f.Settings.Skills.Spiritmaster.OpeningAttackKey = PetAttackKey;
        f.Settings.Maintenance.StatusMaintenanceRules.Add(new()
        {
            SkillId = ShieldId, AbnormalStatusId = ShieldId, Key = ShieldKey, RunTiming = MaintenanceRuleRunTiming.InCombat
        });
        f.Settings.Maintenance.HpMaintenanceRules.Add(new() { SkillId = LowHpId, Key = "D2", BelowPercent = 50 });
        f.Api.Skills = f.Api.Skills.Append(Skill(ShieldId, "shield", 1)).ToArray();
        f.Api.Quickbar = f.Api.Quickbar with
        {
            Slots = f.Api.Quickbar.Slots.Append(new(SkillQuickbar.Main, 3, 21, ShieldId)).ToArray()
        };
        if (includeReplacement)
        {
            f.Settings.QuickbarSkills.ExecutionTree.Add(new()
                { SkillId = ReplacementAttackId, Name = "replacement", BaseName = "replacement" });
            f.Api.Skills = f.Api.Skills.Append(Skill(ReplacementAttackId, "replacement", 1)).ToArray();
            f.Api.Quickbar = f.Api.Quickbar with
            {
                Slots = f.Api.Quickbar.Slots.Append(new(SkillQuickbar.Main, 4, 21, ReplacementAttackId)).ToArray()
            };
            f.Api.SkillAvailability = f.Api.SkillAvailability with
            {
                BindingSlots = f.Api.SkillAvailability.BindingSlots!.Append(
                    new(SkillQuickbar.Main, 4, 21, ReplacementAttackId, ReplacementAttackId)).ToArray()
            };
        }
        f.Api.PlayerAbnormalStatuses = PlayerAbnormalStatusSnapshot.Empty(f.Api.Player.EntityId);
        f.Keyboard.AfterPress = key =>
        {
            if (key != "D2") return;
            Release(f, LowHpId, 100);
            f.Api.Player = f.Api.Player with { CurrentHp = 100 };
        };
        await f.PrepareAsync();
        return f;
    }

    private static void MakeShieldRetryEligible(SharedSkillAuditFixture f) =>
        f.State.MarkMaintenanceKeyAttempted(ShieldKey, DateTimeOffset.Now.AddSeconds(-4));

    private static void SetShield(SharedSkillAuditFixture f) =>
        f.Api.PlayerAbnormalStatuses = new(f.Api.Player.EntityId, DateTimeOffset.Now, 0, new[] { Buff(ShieldId) });

    private static void Release(SharedSkillAuditFixture f, uint id, uint time)
    {
        f.Confirm(id);
        f.Api.SkillAvailability = f.Api.SkillAvailability with { LastReleasedSkillId = id, LastReleasedSkillTime = time };
    }
}
