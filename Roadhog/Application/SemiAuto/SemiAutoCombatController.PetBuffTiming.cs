using Roadhog.Application.Workers;
using Roadhog.Core.Accounts;
using Roadhog.Core.Model;

namespace Roadhog.Application.SemiAuto;

public sealed partial class SemiAutoCombatController
{
    private static bool IsSpiritmasterPetBuffMaintenanceEnabled(AccountWorkerContext context) =>
        context.Config.ScriptSettings?.Skills?.SpiritmasterAutoSkillLogicEnabled == true;

    private static bool IsPetBuffRuleAllowed(SpiritmasterPetBuffRuleConfig rule,
        MaintenanceRuleRunTiming runTiming, bool includeAlwaysRules) =>
        !string.IsNullOrWhiteSpace(rule.Key) &&
        (rule.RunTiming == runTiming || (includeAlwaysRules && rule.RunTiming == MaintenanceRuleRunTiming.Always));

    // The combat entry keeps its existing summon/heal/attack ordering. This entry
    // only maintains buffs, so idle/after-loot work cannot start summon or pet heal.
    private async Task<bool> TryHandleSpiritmasterPetBuffMaintenanceAsync(
        AccountWorkerContext context, SemiAutoCombatState state, SemiAutoScriptSettings settings,
        MaintenanceRuleRunTiming runTiming, bool includeAlwaysRules, Func<Task>? beforeMaintenanceKeyPress = null)
    {
        if (!IsSpiritmasterPetBuffMaintenanceEnabled(context)) return false;
        if (state.HasSpiritmasterPetBuffConfirmation)
        {
            await FinishSpiritmasterPetBuffMaintenanceAsync(context, state).ConfigureAwait(false);
            return true;
        }
        var spiritSettings = context.Config.ScriptSettings!.Skills.Spiritmaster;
        var rules = spiritSettings.PetBuffRules.Where(rule => IsPetBuffRuleAllowed(rule, runTiming, includeAlwaysRules)).ToArray();
        if (rules.Length == 0) return false;
        var player = await ReadPlayerAsync(context).ConfigureAwait(false);
        if (player.IsDead || (player.CharacterClassId is { } classId && classId != AionClassId.Spiritmaster))
        {
            state.ClearSpiritmasterPetBuffAttempts();
            return false;
        }
        var roster = await ReadSummonedPetRosterAsync(context).ConfigureAwait(false);
        if (roster.LocalPlayerPet.Pet is not { IsSummoned: true, IsAlive: true })
        {
            state.ClearSpiritmasterPetBuffAttempts();
            return false;
        }
        var skills = await ReadMaintenanceSkillsAsync(context, rules.Select(rule => (rule.SkillId, rule.SkillName))).ConfigureAwait(false);
        var handled = await TryPressSpiritmasterPetBuffRuleAsync(context, state, settings, spiritSettings,
            skills, roster.LocalPlayerPet, player, runTiming, includeAlwaysRules, beforeMaintenanceKeyPress).ConfigureAwait(false);
        if (state.HasSpiritmasterPetBuffConfirmation)
            await FinishSpiritmasterPetBuffMaintenanceAsync(context, state).ConfigureAwait(false);
        return handled;
    }

    // Settle the existing finite reservation before the caller resumes movement,
    // looting, resting or another skill. No monster target is needed for evidence.
    private async Task FinishSpiritmasterPetBuffMaintenanceAsync(AccountWorkerContext context, SemiAutoCombatState state)
    {
        var spiritSettings = context.Config.ScriptSettings!.Skills.Spiritmaster;
        while (state.HasSpiritmasterPetBuffConfirmation)
        {
            context.StopToken.ThrowIfCancellationRequested();
            var skills = await ReadMaintenanceSkillsAsync(context,
                spiritSettings.PetBuffRules.Select(rule => (rule.SkillId, rule.SkillName))).ConfigureAwait(false);
            var observation = await ObserveSpiritmasterPetBuffConfirmationAsync(context, state, spiritSettings, skills).ConfigureAwait(false);
            if (!observation.Awaiting) break;
            if (!state.HasSpiritmasterPetBuffConfirmation) break;
            await _petBuffDelay(StatusMaintenancePressBurstInterval, context.StopToken).ConfigureAwait(false);
        }
    }
}
