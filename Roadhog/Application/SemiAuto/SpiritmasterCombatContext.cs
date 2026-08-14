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

    public bool HasSummonedPet => IsConfirmedLocalSummonedPet(LocalPet);

    public uint LocalServerObjectId => PetRoster?.LocalServerObjectId ?? 0;

    public uint LocalPetServerObjectId => HasSummonedPet ? LocalPet?.Pet.ServerObjectId ?? 0 : 0;

    public static bool IsConfirmedLocalSummonedPet(OwnedSummonedPetSnapshot? localPet)
    {
        var pet = localPet?.Pet;
        if (localPet is null ||
            pet is not { IsSummoned: true, IsAlive: true, ServerObjectId: not 0, OwnerConfirmed: true })
        {
            return false;
        }

        if (localPet.OwnerKind != SummonedPetOwnerKind.LocalPlayer ||
            localPet.OwnerServerObjectId == 0 ||
            pet.LocalServerObjectId == 0 ||
            localPet.OwnerServerObjectId != pet.LocalServerObjectId ||
            pet.LocalLinkedPetServerObjectId == 0 ||
            pet.LocalLinkedPetServerObjectId != pet.ServerObjectId)
        {
            return false;
        }

        return IsStaticSummonedPet(pet);
    }

    private static bool IsStaticSummonedPet(SummonedPetSnapshot pet)
    {
        var npcType = NormalizeNpcToken(pet.NpcType);
        if (npcType == "summon_pet")
        {
            return true;
        }

        var tribe = NormalizeNpcToken(pet.Tribe);
        return tribe is "pet" or "pet_dark";
    }

    private static string NormalizeNpcToken(string value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim().Replace('-', '_').ToLowerInvariant();
    }

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
