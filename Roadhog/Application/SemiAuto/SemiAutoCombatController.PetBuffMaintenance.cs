using Roadhog.Application.Workers;
using Roadhog.Core.Accounts;
using Roadhog.Core.Model;

namespace Roadhog.Application.SemiAuto;

public sealed partial class SemiAutoCombatController
{
    private readonly record struct PetBuffBurstResult(int PressCount, bool PetStillValid);

    private async Task<PetBuffBurstResult> PressSpiritmasterPetBuffBurstAsync(
        AccountWorkerContext context, SemiAutoCombatState state, SemiAutoScriptSettings settings,
        SpiritmasterPetBuffRuleConfig rule, SkillSnapshot baselineSkill, uint petServerObjectId, int requiredDp)
    {
        var presses = 0;
        for (var index = 0; index < StatusMaintenancePressBurstCount; index++)
        {
            if (index > 0)
                await _petBuffDelay(StatusMaintenancePressBurstInterval, context.StopToken).ConfigureAwait(false);

            // Each repeat is a new input opportunity. Refresh the official skill,
            // pet and lastly player snapshots so a yielded read cannot hide death,
            // replacement, or DP spent by an earlier press.
            var currentSkills = await ReadMaintenanceSkillsAsync(context,
                new[] { (baselineSkill.SkillId, baselineSkill.Name) }).ConfigureAwait(false);
            var roster = await ReadSummonedPetRosterAsync(context).ConfigureAwait(false);
            var player = await ReadPlayerAsync(context).ConfigureAwait(false);
            context.StopToken.ThrowIfCancellationRequested();
            if (player.IsDead || roster.LocalPlayerPet.Pet is not { IsSummoned: true, IsAlive: true } pet ||
                pet.ServerObjectId != petServerObjectId)
                return new(presses, false);
            if (requiredDp > player.CurrentDp ||
                HasSpiritmasterPetBuff(state, rule, baselineSkill, roster.LocalPlayerPet.AbnormalStatuses)) break;
            var currentSkill = currentSkills.FirstOrDefault(skill => skill.SkillId == baselineSkill.SkillId);
            if (currentSkill is null ||
                GetMaintenanceCooldownReadiness(currentSkill, state) == SemiAutoSkillCooldownReadiness.CoolingDown) break;
            if (!await PressSpiritmasterRawKeyAsync(context, settings, rule.Key, "pet_buff").ConfigureAwait(false)) break;
            presses++;
        }
        return new(presses, true);
    }
}
