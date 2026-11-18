using Roadhog.Application.Workers;
using Roadhog.Core.Accounts;
using Roadhog.Core.Model;

namespace Roadhog.Application.SemiAuto;

public sealed partial class SemiAutoCombatController
{
    // Run at every MP skill attempt, including the first. No suspended MP action
    // survives this handoff; normal maintenance can reconsider it after pet recovery.
    private async Task<bool> TryYieldMpToPetHpAsync(AccountWorkerContext context, SemiAutoCombatState state,
        SemiAutoScriptSettings settings, string mpKey, int mpAttempts)
    {
        if (!UsesCoordinatedPetEligibility(context)) return false;
        var spirit = context.Config.ScriptSettings!.Skills.Spiritmaster;
        if (!spirit.PetHpMaintenanceRules.Any(rule => !string.IsNullOrWhiteSpace(rule.Key))) return false;
        if (state.PendingSpiritmasterPetHpIncreaseConfirmation is not null) return true;

        var safety = await ReadSpiritmasterPetHpSafetyAsync(context).ConfigureAwait(false);
        context.StopToken.ThrowIfCancellationRequested();
        if (safety.Player.IsDead) return true;
        if (safety.Player.CharacterClassId is { } classId && classId != AionClassId.Spiritmaster || !safety.Pet.IsAlive)
            return false;
        var due = spirit.PetHpMaintenanceRules.Where(rule => !string.IsNullOrWhiteSpace(rule.Key) &&
            safety.Pet.HpPercent <= Math.Clamp(rule.BelowPercent, 0, 100) &&
            safety.Player.HpPercent >= Math.Clamp(rule.MinPlayerHpPercent, 0, 100)).ToArray();
        if (due.Length == 0) return false;
        var skills = await ReadMaintenanceSkillsAsync(context, due.Select(rule => (rule.SkillId, rule.SkillName))).ConfigureAwait(false);
        context.StopToken.ThrowIfCancellationRequested();
        if (!await TryPressSpiritmasterPetHpRuleAsync(context, state, settings, spirit, skills, safety.Pet, safety.Player)
                .ConfigureAwait(false)) return false;

        state.ClearMaintenanceKeyAttempt(mpKey);
        state.QuickbarSkills.CancelInputAttemptsForHandoff();
        context.Logger.Info("combat_flow.maintenance.pet_hp_handoff", new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName, ["mpKey"] = mpKey, ["mpAttempts"] = mpAttempts,
            ["petSkillId"] = state.PendingSpiritmasterPetHpIncreaseConfirmation?.SkillId,
            ["petServerObjectId"] = safety.Pet.ServerObjectId, ["petHpPercent"] = safety.Pet.HpPercent,
            ["playerHpPercent"] = safety.Player.HpPercent,
            ["reason"] = state.PendingSpiritmasterPetHpIncreaseConfirmation is null ? "pet_hp_input_unavailable" : "eligible_pet_hp_rule"
        });
        return true;
    }
}
