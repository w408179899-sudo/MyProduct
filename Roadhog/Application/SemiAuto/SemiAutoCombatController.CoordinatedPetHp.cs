using Roadhog.Application.Workers;
using Roadhog.Core.Accounts;
using Roadhog.Core.Model;

namespace Roadhog.Application.SemiAuto;

public sealed partial class SemiAutoCombatController
{
    private sealed record PetHpHandoffCandidate(uint SkillId, string Key, int RuleIndex,
        int BelowPercent, int MinPlayerHpPercent);

    // A pending heal still owns ordinary combat input. Only a more urgent,
    // independently eligible pet heal may take over after its input window.
    // This is a bounded confirmation window, not a claim to observe live casting.
    private async Task<bool> TryHandoffCoordinatedPetHpAsync(
        AccountWorkerContext context, SemiAutoCombatState state, SemiAutoScriptSettings settings,
        SpiritmasterSkillSettings spiritSettings, IReadOnlyList<SkillSnapshot> skills,
        SpiritmasterPetHpIncreaseConfirmation pending, SpiritmasterPetHpRuleConfig currentRule,
        SpiritmasterPetHpSafetySnapshot safety, TimeSpan retryInterval)
    {
        if (safety.Pet.CapturedAt <= pending.BaselineCapturedAt || pending.InputTimeProvider is null)
            return false;
        var candidate = SelectPetHpHandoffCandidate(state, spiritSettings, skills, pending, currentRule, safety);
        if (candidate is null) return false;

        var window = ResolveCooldownConfirmationWindow(settings, useSpiritmasterMinimum: true);
        var elapsed = pending.InputTimeProvider.GetElapsedTime(pending.InputStartedTimestamp);
        if (elapsed < window)
        {
            // Do not repeat the old key here: repeating would continually restart
            // the quiet window and prevent the urgent skill from ever taking over.
            return true;
        }
        if (DateTimeOffset.Now + window >= pending.ExpiresAt) return false;

        // Selection used this tick's skill list. Refresh the exact alternative
        // and then both life/HP guards before issuing a different input.
        var freshSkills = (await context.Snapshots.ReadSkillsAsync(new[] { candidate.SkillId })
            .ConfigureAwait(false)).Value;
        context.StopToken.ThrowIfCancellationRequested();
        var fresh = await ReadSpiritmasterPetHpSafetyAsync(context).ConfigureAwait(false);
        context.StopToken.ThrowIfCancellationRequested();
        if (!ReferenceEquals(state.PendingSpiritmasterPetHpIncreaseConfirmation, pending)) return true;
        if (fresh.Player.IsDead || !fresh.Pet.IsAlive || fresh.Pet.ServerObjectId != pending.PetServerObjectId ||
            fresh.Player.CharacterClassId is { } classId && classId != AionClassId.Spiritmaster)
        {
            state.ClearSpiritmasterPetHpIncreaseConfirmation();
            context.Logger.Info("semi_auto.spiritmaster.pet_hp.handoff_cancelled", new Dictionary<string, object?>
            {
                ["account"] = context.Config.AccountName, ["fromSkillId"] = pending.SkillId,
                ["toSkillId"] = candidate.SkillId, ["reason"] = "life_or_identity_changed"
            });
            return true;
        }

        var rule = spiritSettings.PetHpMaintenanceRules.ElementAtOrDefault(candidate.RuleIndex);
        var activeRule = spiritSettings.PetHpMaintenanceRules.ElementAtOrDefault(pending.RuleIndex);
        var skill = freshSkills.FirstOrDefault(item => item.SkillId == candidate.SkillId);
        var ready = skill is null ? SemiAutoSkillCooldownReadiness.Unknown : GetMaintenanceCooldownReadiness(skill, state);
        if (activeRule is null || activeRule.Key != pending.Key ||
            ResolveSpiritmasterConfiguredSkill(activeRule.SkillId, activeRule.SkillName, skills)?.SkillId != pending.SkillId ||
            candidate.BelowPercent >= Math.Clamp(activeRule.BelowPercent, 0, 100) ||
            rule is null || rule.Key != candidate.Key ||
            Math.Clamp(rule.BelowPercent, 0, 100) != candidate.BelowPercent ||
            Math.Clamp(rule.MinPlayerHpPercent, 0, 100) != candidate.MinPlayerHpPercent ||
            ResolveSpiritmasterConfiguredSkill(rule.SkillId, rule.SkillName, freshSkills)?.SkillId != candidate.SkillId ||
            fresh.Pet.CapturedAt <= pending.BaselineCapturedAt ||
            fresh.Pet.HpPercent > candidate.BelowPercent || !fresh.Player.IsAlive ||
            fresh.Player.HpPercent < candidate.MinPlayerHpPercent || ready != SemiAutoSkillCooldownReadiness.Ready ||
            DateTimeOffset.Now + window >= pending.ExpiresAt)
        {
            state.DeferSpiritmasterPetHpIncreaseConfirmation(DateTimeOffset.Now, retryInterval);
            return true;
        }

        if (!await PressSpiritmasterRawKeyAsync(context, settings, candidate.Key, "pet_hp_handoff").ConfigureAwait(false))
        {
            // A failed delivery cannot erase the old reservation or claim that
            // either skill succeeded. Retry pacing remains the existing setting.
            state.DeferSpiritmasterPetHpIncreaseConfirmation(DateTimeOffset.Now, retryInterval);
            return true;
        }
        context.StopToken.ThrowIfCancellationRequested();
        var pressedAt = DateTimeOffset.Now;
        state.BeginSpiritmasterPetHpIncreaseConfirmation(candidate.SkillId, candidate.Key, fresh.Pet.ServerObjectId,
            fresh.Pet.CurrentHp, fresh.Pet.CapturedAt, pressedAt, retryInterval, pending.ExpiresAt - pressedAt,
            candidate.RuleIndex);
        state.ObserveSpiritmasterPetHpInput(_timeProvider, skill);
        context.Logger.Info("semi_auto.spiritmaster.pet_hp.handoff", new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName, ["version"] = "CoordinatedV2",
            ["fromSkillId"] = pending.SkillId, ["toSkillId"] = candidate.SkillId,
            ["fromAttemptCount"] = pending.AttemptCount, ["reason"] = "more_urgent_pet_hp_rule",
            ["petServerObjectId"] = fresh.Pet.ServerObjectId, ["petHpPercent"] = fresh.Pet.HpPercent,
            ["baselinePetHp"] = fresh.Pet.CurrentHp, ["playerHpPercent"] = fresh.Player.HpPercent,
            ["minimumPlayerHpPercent"] = candidate.MinPlayerHpPercent, ["key"] = candidate.Key,
            ["inputProtectionMs"] = (int)window.TotalMilliseconds, ["previousInputElapsedMs"] = (long)elapsed.TotalMilliseconds,
            ["cooldownReadiness"] = ready.ToString(), ["confirmationExpiresAt"] = pending.ExpiresAt
        });
        return true;
    }

    private static PetHpHandoffCandidate? SelectPetHpHandoffCandidate(
        SemiAutoCombatState state, SpiritmasterSkillSettings spiritSettings, IReadOnlyList<SkillSnapshot> skills,
        SpiritmasterPetHpIncreaseConfirmation pending, SpiritmasterPetHpRuleConfig currentRule,
        SpiritmasterPetHpSafetySnapshot safety)
    {
        var currentThreshold = Math.Clamp(currentRule.BelowPercent, 0, 100);
        foreach (var (rule, index) in spiritSettings.PetHpMaintenanceRules.Select((rule, index) => (rule, index))
                     .OrderBy(item => Math.Clamp(item.rule.BelowPercent, 0, 100)))
        {
            var threshold = Math.Clamp(rule.BelowPercent, 0, 100);
            var floor = Math.Clamp(rule.MinPlayerHpPercent, 0, 100);
            if (string.IsNullOrWhiteSpace(rule.Key) || threshold >= currentThreshold ||
                safety.Pet.HpPercent > threshold || !safety.Player.IsAlive || safety.Player.HpPercent < floor) continue;
            var skill = ResolveSpiritmasterConfiguredSkill(rule.SkillId, rule.SkillName, skills);
            if (skill is null || skill.SkillId == pending.SkillId ||
                GetMaintenanceCooldownReadiness(skill, state) != SemiAutoSkillCooldownReadiness.Ready) continue;
            return new(skill.SkillId, rule.Key, index, threshold, floor);
        }
        return null;
    }
}
