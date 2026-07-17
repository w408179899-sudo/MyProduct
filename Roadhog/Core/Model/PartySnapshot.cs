namespace Roadhog.Core.Model;

public sealed record PartySnapshot(
    uint PartyId,
    uint PartyFlags,
    ulong PrimaryPartyCount,
    uint LeaderServerObjectId,
    uint LocalServerObjectId,
    ushort LocalEntityId,
    string LocalName,
    Vector3Snapshot? LocalPosition,
    uint LocalTargetServerObjectId,
    int VisiblePlayerActorCount,
    DateTimeOffset CapturedAt,
    IReadOnlyList<PartyMemberSnapshot> Members,
    string MemberReadError = "",
    string LiveActorReadError = "")
{
    public bool HasLeader => LeaderServerObjectId != 0;

    public bool LocalIsLeader => LocalServerObjectId != 0 &&
                                 LeaderServerObjectId != 0 &&
                                 LocalServerObjectId == LeaderServerObjectId;

    public PartyMemberSnapshot? LocalMember =>
        Members.FirstOrDefault(member => member.IsSelf);

    public PartyMemberSnapshot? LeaderMember =>
        Members.FirstOrDefault(member => member.IsLeader);

    public static PartySnapshot Empty(DateTimeOffset capturedAt)
    {
        return new PartySnapshot(
            0,
            0,
            0,
            0,
            0,
            0,
            string.Empty,
            null,
            0,
            0,
            capturedAt,
            Array.Empty<PartyMemberSnapshot>());
    }
}
