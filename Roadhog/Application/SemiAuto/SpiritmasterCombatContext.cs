using Roadhog.Core.Model;

namespace Roadhog.Application.SemiAuto;

public sealed record SpiritmasterCombatContext(
    PlayerSnapshot? Player,
    SummonedPetRosterSnapshot? PetRoster,
    LockedTargetAbnormalStatusSnapshot? LockedTargetAbnormalStatuses)
{
    public OwnedSummonedPetSnapshot? LocalPet => PetRoster?.LocalPlayerPet;

    public bool HasKnownNonSpiritmasterPlayer =>
        Player?.CharacterClassId is { } classId && classId != AionClassId.Spiritmaster;

    public bool CanUseSpiritmasterLogic => !HasKnownNonSpiritmasterPlayer;

    public bool HasSummonedPet => LocalPet?.Pet is { IsSummoned: true, IsAlive: true };

    public uint LocalServerObjectId => PetRoster?.LocalServerObjectId ?? 0;

    public uint LocalPetServerObjectId => HasSummonedPet ? LocalPet?.Pet.ServerObjectId ?? 0 : 0;

    public bool LockedTargetTargetsSpiritmasterBodyOrPet
    {
        get
        {
            var target = LockedTargetAbnormalStatuses?.Target;
            if (target is null || target.TargetServerObjectId == 0)
            {
                return false;
            }

            return (target.LocalServerObjectId != 0 && target.TargetServerObjectId == target.LocalServerObjectId) ||
                   (LocalPetServerObjectId != 0 && target.TargetServerObjectId == LocalPetServerObjectId);
        }
    }
}
