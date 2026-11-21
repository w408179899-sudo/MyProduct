using Roadhog.Application.SemiAuto;
using Roadhog.Core.Accounts;
using Roadhog.Core.Model;
using static SharedSkillAuditFixture;

internal static class CombatSkillPriorityTests
{
    private const uint ShieldId = 1390;
    private const string ShieldKey = "D4";
    private const uint ReplacementAttackId = 910;

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

    public static async Task ShieldUsesTheCombatReleaseSlotAsync()
    {
        using var f = await CreateAsync();
        SetShield(f);
        await f.TickAsync();
        Check(f.State.QuickbarSkills.PendingAction?.Node.SkillId == AttackId, "the ordinary skill owns the active slot");

        f.Api.PlayerAbnormalStatuses = PlayerAbnormalStatusSnapshot.Empty(f.Api.Player.EntityId);
        await f.TickAsync();
        Sequence(new[] { "D1" }, f.Keyboard.Keys, "a missing shield cannot send input over the unresolved attack");
        Check(f.State.CombatStatusAttempts.Count == 0, "shield has not started its confirmation window");

        Release(f, AttackId, 200);
        f.Clock.Advance(80);
        await f.TickAsync();
        Sequence(new[] { "D1", ShieldKey }, f.Keyboard.Keys, "confirmed attack hands the next release slot to the shield");
        var events = f.Logger.Entries.Where(entry =>
            entry.EventName is "quickbar_skill.key.pressed" or "quickbar_skill.release.confirmed").ToArray();
        Check(events.Length == 3 &&
            events[1].EventName == "quickbar_skill.release.confirmed" && Equals(events[1].Fields["skillId"], AttackId) &&
            events[2].EventName == "quickbar_skill.key.pressed" && Equals(events[2].Fields["skillId"], ShieldId),
            "the same release trace orders attack confirmation before shield input");

        f.Clock.Advance(300);
        await f.TickAsync();
        Sequence(new[] { "D1", ShieldKey }, f.Keyboard.Keys, "normal inputs wait while the shield is unconfirmed");
        Check(!f.Logged("quickbar_skill.release.confirmed", "skillId", ShieldId), "pressing the shield alone cannot confirm it");

        SetShield(f);
        f.Clock.Advance(300);
        await f.TickAsync();
        Check(f.Logged("quickbar_skill.release.confirmed", "skillId", ShieldId), "the shared trace reports shield release only after the effect");
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
        f.Api.Player = f.Api.Player with { CurrentHp = 0 };
        await f.TickAsync();
        Check(f.State.CombatStatusAttempts.Count == 0, "death clears an in-flight shield");
        Sequence(new[] { ShieldKey }, f.Keyboard.Keys, "death cannot send an old skill");

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

    private static async Task<SharedSkillAuditFixture> CreateAsync(bool includeReplacement = false)
    {
        var f = new SharedSkillAuditFixture();
        f.Settings.Skills.SpiritmasterAutoSkillLogicEnabled = false;
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
