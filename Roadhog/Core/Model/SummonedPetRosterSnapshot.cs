namespace Roadhog.Core.Model;

public sealed record SummonedPetRosterSnapshot(
    uint LocalServerObjectId,
    uint LocalLinkedPetServerObjectId,
    DateTimeOffset CapturedAt,
    OwnedSummonedPetSnapshot LocalPlayerPet,
    IReadOnlyList<OwnedSummonedPetSnapshot> PartyMemberPets,
    IReadOnlyList<uint> PartyMemberServerObjectIds,
    string PartyMemberReadError = "")
{
    public int PartyMemberPetCount => PartyMemberPets.Count;

    public int VisibleSummonedPetCount => (LocalPlayerPet.IsSummoned ? 1 : 0) + PartyMemberPets.Count;

    public static SummonedPetRosterSnapshot Empty(uint localServerObjectId, DateTimeOffset capturedAt)
    {
        return new SummonedPetRosterSnapshot(
            localServerObjectId,
            0,
            capturedAt,
            new OwnedSummonedPetSnapshot(
                SummonedPetOwnerKind.LocalPlayer,
                localServerObjectId,
                string.Empty,
                string.Empty,
                SummonedPetSnapshot.NotSummoned(localServerObjectId, capturedAt),
                0,
                Array.Empty<AbnormalStatusEntrySnapshot>()),
            Array.Empty<OwnedSummonedPetSnapshot>(),
            Array.Empty<uint>());
    }
}
