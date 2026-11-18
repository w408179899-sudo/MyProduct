using Roadhog.Application.Workers;
using Roadhog.Core.Model;

namespace Roadhog.Application.SemiAuto;

public sealed partial class SemiAutoCombatController
{
    // HP recovery is an outcome, not proof that this particular key was accepted.
    // Retain the exact skill's forward CD evidence until this reservation ends.
    private static bool ObserveCoordinatedPetHpRelease(AccountWorkerContext context, SemiAutoCombatState state,
        SpiritmasterPetHpIncreaseConfirmation pending, SkillSnapshot? skill, SummonedPetSnapshot pet)
    {
        if (pending.ReleasedCooldownEndTime is not null) return true;
        if (pending.BaselineCooldownEndTime is not { } baseline || skill is null || skill.SkillId != pending.SkillId ||
            !HasCooldownEndAdvanced(baseline, skill.CooldownEndTime)) return false;
        state.ObserveSpiritmasterPetHpRelease(skill.CooldownEndTime);
        context.Logger.Info("combat_flow.pet_hp.release_observed", new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName, ["skillId"] = skill.SkillId,
            ["petServerObjectId"] = pending.PetServerObjectId, ["baselineCooldownEndTime"] = baseline,
            ["cooldownEndTime"] = skill.CooldownEndTime, ["petHpPercent"] = pet.HpPercent,
            ["petDistance"] = pet.DistanceToLocalPlayer, ["attemptCount"] = pending.AttemptCount
        });
        return true;
    }
}
