namespace Roadhog.Core.Model;

public sealed record OwnedSummonedPetSnapshot(
    SummonedPetOwnerKind OwnerKind,
    uint OwnerServerObjectId,
    string OwnerName,
    string PartyListName,
    SummonedPetSnapshot Pet,
    uint AbnormalCategory2Count,
    IReadOnlyList<AbnormalStatusEntrySnapshot> AbnormalStatuses,
    string AbnormalStatusReadError = "",
    AionClassId? OwnerClassId = null,
    string OwnerClassName = "")
{
    public bool IsSummoned => Pet.IsSummoned;

    public int AbnormalStatusCount => AbnormalStatuses.Count;

    public int HarmfulAbnormalCount
    {
        get
        {
            var count = 0;
            foreach (var entry in AbnormalStatuses)
            {
                if (entry.IsHarmfulForRest)
                {
                    count++;
                }
            }

            return count;
        }
    }
}
