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
            if (skill is not null && HasSpiritmasterPetBuff(state, rule, skill, roster.LocalPlayerPet.AbnormalStatuses))
            {
                state.ConfirmSpiritmasterPetBuff(skill.SkillId);
                continue;
            }
            awaiting |= state.IsAwaitingSpiritmasterPetBuffConfirmation(skill?.SkillId ?? rule.SkillId,
                skill, pet.ServerObjectId);
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
