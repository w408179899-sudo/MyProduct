namespace Roadhog.Core.Model;

public sealed record TeamMemberSnapshot(
    PartyMemberSnapshot PartyMember,
    int FunctionKeyNumber,
    OwnedSummonedPetSnapshot? SummonedPet)
{
    public uint ServerObjectId => PartyMember.ServerObjectId;

    public string Name => PartyMember.Name;

    public bool IsSelf => PartyMember.IsSelf;

    public bool IsLeader => PartyMember.IsLeader;

    public bool HasSummonedPet => SummonedPet?.IsSummoned == true;

    public bool IsScreenVisible => PartyMember.IsScreenVisible;

    public bool IsWithinSameInstanceDistance => PartyMember.DistanceToLocalPlayer is <= 50.0D;
}
