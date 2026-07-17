namespace Roadhog.Core.Model;

public sealed record TeamSnapshot(
    PartySnapshot Party,
    SummonedPetRosterSnapshot SummonedPetRoster,
    IReadOnlyList<TeamMemberSnapshot> Members,
    DateTimeOffset CapturedAt)
{
    public TeamMemberSnapshot? LocalMember => Members.FirstOrDefault(member => member.IsSelf);

    public TeamMemberSnapshot? LeaderMember => Members.FirstOrDefault(member => member.IsLeader);

    public IReadOnlyList<TeamMemberSnapshot> OtherMembers =>
        Members.Where(member => !member.IsSelf).ToArray();

    public int PartyMemberPetCount => SummonedPetRoster.PartyMemberPetCount;
}
