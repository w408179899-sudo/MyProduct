using Roadhog.Application.Workers;
using Roadhog.Core.Accounts;
using Roadhog.Core.Model;

namespace Roadhog.Application.SemiAuto;

public sealed partial class SemiAutoCombatController
{
    private sealed record SpiritmasterPetHpSafetySnapshot(PlayerSnapshot Player, SummonedPetSnapshot Pet);
    private static readonly TimeSpan SpiritmasterPetHpMinimumRetryInterval = TimeSpan.FromMilliseconds(100);
    // This is an unconfirmed-action lifetime, not an artificial skill cooldown.
    private static readonly TimeSpan SpiritmasterPetHpConfirmationLifetime = TimeSpan.FromSeconds(30);

    private async Task<bool> TryPressSpiritmasterPetHpRuleAsync(
        AccountWorkerContext context, SemiAutoCombatState state, SemiAutoScriptSettings settings,
        SpiritmasterSkillSettings spiritSettings, IReadOnlyList<SkillSnapshot> skills,
        SummonedPetSnapshot pet, PlayerSnapshot? player)
    {
        if (state.PendingSpiritmasterPetHpIncreaseConfirmation is not null) return true;
        foreach (var (rule, ruleIndex) in spiritSettings.PetHpMaintenanceRules
                     .Select((rule, index) => (Rule: rule, Index: index))
                     .Where(item => !string.IsNullOrWhiteSpace(item.Rule.Key))
                     .OrderBy(item => Math.Clamp(item.Rule.BelowPercent, 0, 100)))
        {
            var skill = ResolveSpiritmasterConfiguredSkill(rule.SkillId, rule.SkillName, skills);
            if (skill is null || pet.HpPercent > Math.Clamp(rule.BelowPercent, 0, 100) ||
                GetMaintenanceCooldownReadiness(skill, state) != SemiAutoSkillCooldownReadiness.Ready)
                continue;

            var minimumPlayerHpPercent = Math.Clamp(rule.MinPlayerHpPercent, 0, 100);
            if (minimumPlayerHpPercent > 0 && player is not null && player.HpPercent < minimumPlayerHpPercent)
            {
                LogSpiritmasterPetHpPlayerHpBlocked(context, state, player, "initial", skill.SkillId, minimumPlayerHpPercent);
                continue;
            }
            var safety = await ReadSpiritmasterPetHpSafetyAsync(context).ConfigureAwait(false);
            context.StopToken.ThrowIfCancellationRequested();
            // Zero disables the configurable HP floor, never the player/pet life checks.
            if (safety.Player.IsDead || !safety.Pet.IsAlive) return true;
            if (safety.Pet.ServerObjectId != pet.ServerObjectId) continue;
            if (minimumPlayerHpPercent > 0 && safety.Player.HpPercent < minimumPlayerHpPercent)
            {
                LogSpiritmasterPetHpPlayerHpBlocked(context, state, safety.Player, "initial_snapshot", skill.SkillId, minimumPlayerHpPercent);
                continue;
            }
            player = safety.Player;
            pet = safety.Pet;
            if (pet.HpPercent > Math.Clamp(rule.BelowPercent, 0, 100)) continue;
            if (!await PressSpiritmasterRawKeyAsync(context, settings, rule.Key, "pet_hp").ConfigureAwait(false)) return true;

            var retryInterval = ResolveSpiritmasterPetHpRetryInterval(settings);
            state.BeginSpiritmasterPetHpIncreaseConfirmation(skill.SkillId, rule.Key, pet.ServerObjectId,
                pet.CurrentHp, pet.CapturedAt, DateTimeOffset.Now, retryInterval, SpiritmasterPetHpConfirmationLifetime, ruleIndex);
            context.Logger.Info("semi_auto.spiritmaster.pet_hp.confirmation_started", new Dictionary<string, object?>
            {
                ["account"] = context.Config.AccountName, ["skillId"] = skill.SkillId, ["skillName"] = skill.Name,
                ["key"] = rule.Key, ["petServerObjectId"] = pet.ServerObjectId,
                ["baselinePetHp"] = pet.CurrentHp, ["baselinePetMaxHp"] = pet.MaxHp,
                ["playerHpPercent"] = player.HpPercent, ["minimumPlayerHpPercent"] = minimumPlayerHpPercent,
                ["retryIntervalMs"] = (int)retryInterval.TotalMilliseconds
            });
            context.Logger.Info("semi_auto.spiritmaster.pet_hp_key_pressed", new Dictionary<string, object?>
            {
                ["account"] = context.Config.AccountName, ["key"] = rule.Key,
                ["skillId"] = skill.SkillId, ["skillName"] = skill.Name,
                ["petHpPercent"] = pet.HpPercent, ["belowPercent"] = rule.BelowPercent,
                ["minimumPlayerHpPercent"] = minimumPlayerHpPercent
            });
            return true;
        }
        return false;
    }

    private async Task<bool> TryContinueSpiritmasterPetHpConfirmationAsync(
        AccountWorkerContext context,
        SemiAutoCombatState state,
        SemiAutoScriptSettings settings,
        IReadOnlyList<SkillSnapshot> skills,
        SpiritmasterSkillSettings spiritSettings)
    {
        context.StopToken.ThrowIfCancellationRequested();
        var pending = state.PendingSpiritmasterPetHpIncreaseConfirmation;
        if (pending is null)
        {
            return false;
        }

        // Duplicate skills may have different HP limits. Continue the selected rule.
        var rule = spiritSettings.PetHpMaintenanceRules.ElementAtOrDefault(pending.RuleIndex);
        if (rule is null || !string.Equals(rule.Key, pending.Key, StringComparison.Ordinal) ||
            (rule.SkillId != pending.SkillId &&
             ResolveSpiritmasterConfiguredSkill(rule.SkillId, rule.SkillName, skills)?.SkillId != pending.SkillId))
        {
            state.ClearSpiritmasterPetHpIncreaseConfirmation();
            context.Logger.Info("semi_auto.spiritmaster.pet_hp.confirmation_cancelled", new Dictionary<string, object?>
            {
                ["account"] = context.Config.AccountName, ["skillId"] = pending.SkillId, ["reason"] = "rule_changed"
            });
            return false;
        }
        var minimumPlayerHpPercent = Math.Clamp(rule.MinPlayerHpPercent, 0, 100);

        var retryInterval = ResolveSpiritmasterPetHpRetryInterval(settings);
        var safety = await ReadSpiritmasterPetHpSafetyAsync(context)
            .ConfigureAwait(false);
        context.StopToken.ThrowIfCancellationRequested();
        if (safety.Player.IsDead)
        {
            state.ClearSpiritmasterPetHpIncreaseConfirmation();
            return true;
        }

        var now = DateTimeOffset.Now;
        if (now >= pending.ExpiresAt)
        {
            state.ClearSpiritmasterPetHpIncreaseConfirmation();
            context.Logger.Warn(
                "semi_auto.spiritmaster.pet_hp.confirmation_expired",
                new Dictionary<string, object?>
                {
                    ["account"] = context.Config.AccountName,
                    ["petServerObjectId"] = pending.PetServerObjectId,
                    ["baselinePetHp"] = pending.BaselineCurrentHp,
                    ["attemptCount"] = pending.AttemptCount
                });
            return false;
        }

        if (safety.Player.CharacterClassId is { } classId && classId != AionClassId.Spiritmaster)
        {
            state.ClearSpiritmasterPetHpIncreaseConfirmation();
            return false;
        }

        if (!safety.Pet.IsAlive || safety.Pet.ServerObjectId != pending.PetServerObjectId)
        {
            state.ClearSpiritmasterPetHpIncreaseConfirmation();
            context.Logger.Info(
                "semi_auto.spiritmaster.pet_hp.confirmation_cancelled",
                new Dictionary<string, object?>
                {
                    ["account"] = context.Config.AccountName,
                    ["reason"] = !safety.Pet.IsAlive ? "pet_unavailable" : "pet_identity_changed",
                    ["expectedPetServerObjectId"] = pending.PetServerObjectId,
                    ["currentPetServerObjectId"] = safety.Pet.ServerObjectId,
                    ["attemptCount"] = pending.AttemptCount
                });
            return false;
        }

        // Recheck the configured HP floor on every reserved tick, even before the retry
        // deadline or when the provider still publishes the same pet snapshot.
        if (minimumPlayerHpPercent > 0 && safety.Player.HpPercent < minimumPlayerHpPercent)
        {
            state.ClearSpiritmasterPetHpIncreaseConfirmation();
            LogSpiritmasterPetHpPlayerHpBlocked(context, state, safety.Player, "retry", pending.SkillId, minimumPlayerHpPercent);
            return false;
        }

        if (safety.Pet.CapturedAt > pending.BaselineCapturedAt &&
            safety.Pet.CurrentHp > pending.BaselineCurrentHp)
        {
            state.ClearSpiritmasterPetHpIncreaseConfirmation();
            context.Logger.Info(
                "semi_auto.spiritmaster.pet_hp.pet_hp_confirmed",
                new Dictionary<string, object?>
                {
                    ["account"] = context.Config.AccountName,
                    ["skillId"] = pending.SkillId,
                    ["petServerObjectId"] = pending.PetServerObjectId,
                    ["baselinePetHp"] = pending.BaselineCurrentHp,
                    ["currentPetHp"] = safety.Pet.CurrentHp,
                    ["petMaxHp"] = safety.Pet.MaxHp,
                    ["attemptCount"] = pending.AttemptCount
                });
            return false;
        }

        if (safety.Pet.HpPercent > Math.Clamp(rule.BelowPercent, 0, 100))
        {
            state.ClearSpiritmasterPetHpIncreaseConfirmation();
            context.Logger.Info("semi_auto.spiritmaster.pet_hp.confirmation_cancelled", new Dictionary<string, object?>
            {
                ["account"] = context.Config.AccountName, ["skillId"] = pending.SkillId,
                ["reason"] = "pet_hp_above_threshold", ["petHpPercent"] = safety.Pet.HpPercent,
                ["belowPercent"] = rule.BelowPercent
            });
            return false;
        }

        var skill = skills.FirstOrDefault(item => item.SkillId == pending.SkillId);
        if (skill is null)
        {
            state.ClearSpiritmasterPetHpIncreaseConfirmation();
            context.Logger.Info(
                "semi_auto.spiritmaster.pet_hp.confirmation_cancelled",
                new Dictionary<string, object?>
                {
                    ["account"] = context.Config.AccountName,
                    ["reason"] = "retry_skill_missing",
                    ["petServerObjectId"] = safety.Pet.ServerObjectId
                });
            return false;
        }

        if (now < pending.NextCheckAt)
        {
            return true;
        }

        if (safety.Pet.CapturedAt <= pending.BaselineCapturedAt ||
            GetMaintenanceCooldownReadiness(skill, state) != SemiAutoSkillCooldownReadiness.Ready)
        {
            state.DeferSpiritmasterPetHpIncreaseConfirmation(DateTimeOffset.Now, retryInterval);
            return true;
        }

        if (!await PressSpiritmasterRawKeyAsync(context, settings, pending.Key, "pet_hp_retry")
                .ConfigureAwait(false))
        {
            state.DeferSpiritmasterPetHpIncreaseConfirmation(DateTimeOffset.Now, retryInterval);
            return true;
        }

        var pressedAt = DateTimeOffset.Now;
        state.RecordSpiritmasterPetHpIncreaseRetry(
            safety.Pet.CurrentHp,
            safety.Pet.CapturedAt,
            pressedAt,
            retryInterval);
        context.Logger.Warn(
            "semi_auto.spiritmaster.pet_hp.pet_hp_retry",
            new Dictionary<string, object?>
            {
                ["account"] = context.Config.AccountName,
                ["skillId"] = pending.SkillId,
                ["key"] = pending.Key,
                ["petServerObjectId"] = pending.PetServerObjectId,
                ["previousBaselinePetHp"] = pending.BaselineCurrentHp,
                ["retryBaselinePetHp"] = safety.Pet.CurrentHp,
                ["petMaxHp"] = safety.Pet.MaxHp,
                ["playerHpPercent"] = safety.Player.HpPercent,
                ["attemptCount"] = pending.AttemptCount + 1
            });
        return true;
    }

    private async Task<SpiritmasterPetHpSafetySnapshot>
        ReadSpiritmasterPetHpSafetyAsync(
            AccountWorkerContext context)
    {
        var pet = await ReadSummonedPetAsync(context).ConfigureAwait(false);
        // Recheck the player's configured HP floor after the pet read has yielded.
        var player = await ReadPlayerAsync(context)
            .ConfigureAwait(false);
        return new SpiritmasterPetHpSafetySnapshot(player, pet);
    }

    private static TimeSpan ResolveSpiritmasterPetHpRetryInterval(
        SemiAutoScriptSettings settings)
    {
        return Max(
            Ms(settings.PostPressSuppressMs, 650),
            SpiritmasterPetHpMinimumRetryInterval);
    }

    private static void LogSpiritmasterPetHpPlayerHpBlocked(
        AccountWorkerContext context,
        SemiAutoCombatState state,
        PlayerSnapshot player,
        string phase,
        uint skillId,
        int minimumPlayerHpPercent)
    {
        var now = DateTimeOffset.Now;
        if (!ShouldLog(state.LastSpiritmasterPetHpConfirmationLogAt, now))
        {
            return;
        }

        state.LastSpiritmasterPetHpConfirmationLogAt = now;
        context.Logger.Info(
            "semi_auto.spiritmaster.pet_hp.player_hp_blocked",
            new Dictionary<string, object?>
            {
                ["account"] = context.Config.AccountName,
                ["phase"] = phase,
                ["skillId"] = skillId,
                ["playerCurrentHp"] = player.CurrentHp,
                ["playerMaxHp"] = player.MaxHp,
                ["playerHpPercent"] = player.HpPercent,
                ["minimumPlayerHpPercent"] = minimumPlayerHpPercent
            });
    }

}
