using Roadhog.Application.Workers;
using Roadhog.Core.Accounts;
using Roadhog.Core.Model;

namespace Roadhog.Application.SemiAuto;

public sealed partial class SemiAutoCombatController
{
    private static bool UsesCoordinatedPetEligibility(AccountWorkerContext context) =>
        context.Config.ScriptSettings?.Skills?.SpiritmasterAutoSkillLogicEnabled == true;

    private async Task<PetSkillGuard?> CreatePetSkillGuardAsync(AccountWorkerContext context,
        SemiAutoCombatState state, SkillSnapshot? skill, uint id, string name, string key)
    {
        if (!UsesCoordinatedPetEligibility(context)) return null;
        // Manual keys keep their configuration, but a skill on that slot can supply its prerequisites.
        if (skill is null && id == 0)
            skill = await ReadConfiguredSummonSkillAsync(context, new() { Key = key }).ConfigureAwait(false);
        var requirement = SpiritmasterSkillEligibility.Classify(skill, id, name);
        return requirement == SpiritmasterPetRequirement.None ? null :
            new PetSkillGuard(context, state, requirement, skill?.SkillId ?? id, key);
    }

    private sealed class PetSkillGuard(AccountWorkerContext context, SemiAutoCombatState state,
        SpiritmasterPetRequirement requirement, uint skillId, string key)
    {
        private uint? expectedPetId;

        public async Task<bool> CheckAsync()
        {
            context.StopToken.ThrowIfCancellationRequested();
            var player = await ReadPlayerAsync(context).ConfigureAwait(false);
            if (player.CharacterClassId is { } classId && classId != AionClassId.Spiritmaster) return true;
            var reason = player.IsDead ? "player_dead" : null;
            if (reason is null && requirement == SpiritmasterPetRequirement.MissingPet && skillId != 0)
            {
                var skills = (await context.Snapshots.ReadSkillsAsync(new[] { skillId }).ConfigureAwait(false)).Value;
                context.StopToken.ThrowIfCancellationRequested();
                var skill = skills.FirstOrDefault(s => s.SkillId == skillId);
                if (skill is null || GetSummonCooldownReadiness(skill, state, CurrentOsTick()) != SemiAutoSkillCooldownReadiness.Ready)
                    reason = "summon_not_ready";
            }
            var roster = await ReadSummonedPetRosterAsync(context).ConfigureAwait(false);
            context.StopToken.ThrowIfCancellationRequested();
            var pet = roster.LocalPlayerPet.Pet;
            reason ??= SpiritmasterSkillEligibility.BlockReason(requirement, pet, expectedPetId);
            if (reason is null)
            {
                if (requirement == SpiritmasterPetRequirement.LivingPet) expectedPetId ??= pet.ServerObjectId;
                return true;
            }
            var now = DateTimeOffset.Now;
            if (ShouldLog(state.LastCoordinatedPetSkillBlockLogAt, now))
            {
                state.LastCoordinatedPetSkillBlockLogAt = now;
                context.Logger.Info("combat_flow.pet_skill.blocked", new Dictionary<string, object?>
                {
                    ["account"] = context.Config.AccountName, ["skillId"] = skillId, ["key"] = key,
                    ["reason"] = reason, ["petServerObjectId"] = pet.ServerObjectId,
                    ["expectedPetServerObjectId"] = expectedPetId
                });
            }
            return false;
        }
    }

    private static async Task<bool> HasLivingPetBeforeCoordinatedSummonAsync(AccountWorkerContext context,
        SemiAutoCombatState state)
    {
        var roster = await ReadSummonedPetRosterAsync(context).ConfigureAwait(false);
        context.StopToken.ThrowIfCancellationRequested();
        if (SpiritmasterSkillEligibility.BlockReason(SpiritmasterPetRequirement.MissingPet, roster.LocalPlayerPet.Pet) is null)
            return false;
        state.ClearSpiritmasterSummonVerification();
        state.CoordinatedSummonDefenseAllowed = false;
        state.CoordinatedSummonDefenseReason = null;
        state.CoordinatedSummonCooldown = null;
        return true;
    }
}
