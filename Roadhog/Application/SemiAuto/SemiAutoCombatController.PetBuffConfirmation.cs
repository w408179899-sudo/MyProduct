using Roadhog.Application.JumpAssist;
using Roadhog.Application.Workers;
using Roadhog.Core.Accounts;
using Roadhog.Core.Model;

namespace Roadhog.Application.SemiAuto;

public sealed partial class SemiAutoCombatController
{
    private async Task<bool> ContinueSpiritmasterPetBuffConfirmationAsync(
        AccountWorkerContext context, SemiAutoCombatState state, SemiAutoScriptSettings settings,
        SpiritmasterSkillSettings spiritSettings, IReadOnlyList<SkillSnapshot> skills,
        MaintenanceScriptSettings maintenance, SemiAutoSkillPlan plan, CombatJumpAssistSession? jumpAssist,
        bool requireCooldownCalibrationForMaintenance, Func<Task<bool>>? ensureHpMaintenanceTargetBeforeKeyPress)
    {
        var roster = await ReadSummonedPetRosterAsync(context).ConfigureAwait(false);
        var player = await ReadPlayerAsync(context).ConfigureAwait(false);
        context.StopToken.ThrowIfCancellationRequested();
        if (player.IsDead)
        {
            state.ClearSpiritmasterPetBuffAttempts();
            return true;
        }
        if (roster.LocalPlayerPet.Pet is not { IsSummoned: true, IsAlive: true } pet)
        {
            state.ClearSpiritmasterPetBuffAttempts();
            return false;
        }

        var awaiting = false;
        foreach (var rule in spiritSettings.PetBuffRules.Where(rule => !string.IsNullOrWhiteSpace(rule.Key)))
        {
            var skill = ResolveSpiritmasterConfiguredSkill(rule.SkillId, rule.SkillName, skills);
            var skillId = skill?.SkillId ?? rule.SkillId;
            var statusConfirmed = skill is not null &&
                HasSpiritmasterPetBuff(state, rule, skill, roster.LocalPlayerPet.AbnormalStatuses);
            if (state.TryCompleteSpiritmasterPetBuffConfirmation(skillId, skill, pet.ServerObjectId,
                    statusConfirmed, out var result))
            {
                context.Logger.Info(result.Confirmed
                    ? "semi_auto.spiritmaster.pet_buff_confirmed"
                    : "semi_auto.spiritmaster.pet_buff_unconfirmed", new Dictionary<string, object?>
                {
                    ["account"] = context.Config.AccountName,
                    ["key"] = rule.Key,
                    ["skillId"] = skillId,
                    ["skillName"] = skill?.Name ?? rule.SkillName,
                    ["petServerObjectId"] = pet.ServerObjectId,
                    ["pressCount"] = result.PressCount,
                    ["attemptCount"] = result.AttemptCount,
                    ["confirmElapsedMs"] = (long)result.Elapsed.TotalMilliseconds,
                    ["cooldownAdvanced"] = result.CooldownAdvanced,
                    ["petAbnormalIds"] = FormatAbnormalIdList(roster.LocalPlayerPet.AbnormalStatuses.Select(entry => entry.AbnormalId))
                });
            }
            if (statusConfirmed)
            {
                state.ConfirmSpiritmasterPetBuff(skillId);
                continue;
            }
            awaiting |= state.IsAwaitingSpiritmasterPetBuffConfirmation(skillId, pet.ServerObjectId);
        }
        if (!awaiting) return false;

        // Only player HP recovery may interrupt this short cast reservation.
        // Reuse the usual thresholds, timing, cooldown, targeting and input guards.
        await RunWithJumpPauseAsync(jumpAssist, "semi_auto_pet_buff_confirmation",
            () => TryPressMaintenanceRuleAsync(context, state, settings, maintenance.HpMaintenanceRules,
                "hp", player.CurrentHp, player.MaxHp, player, plan: plan,
                requireCooldownCalibrationForMaintenance: requireCooldownCalibrationForMaintenance,
                runTiming: MaintenanceRuleRunTiming.InCombat, includeAlwaysRules: true,
                ensureTargetBeforeKeyPress: ensureHpMaintenanceTargetBeforeKeyPress)).ConfigureAwait(false);
        return true;
    }
}
