namespace Roadhog.Core.Model;

public sealed record PartyMemberSnapshot(
    string ListName,
    int ListIndex,
    ulong NodeAddress,
    ulong MemberAddress,
    uint PartySlot,
    uint ServerObjectId,
    string Name,
    byte ClassId,
    AionClassId? Class,
    string ClassName,
    byte Level,
    uint CurrentHp,
    uint MaxHp,
    uint CurrentMp,
    uint MaxMp,
    uint CurrentFlightTime,
    uint MaxFlightTime,
    uint AreaField0,
    uint AreaField1,
    Vector3Snapshot CachedPosition,
    byte DataFlags,
    byte FlightAreaFlag,
    byte FlightFlags,
    byte RuntimeState,
    ulong ControlStatusMask,
    bool HasAbnormalBlock,
    short RawAbnormalCount,
    uint UpdateTime,
    IReadOnlyList<AbnormalStatusEntrySnapshot> AbnormalStatuses,
    bool IsSelf,
    bool IsLeader,
    bool HasLiveActor,
    ushort LiveEntityId,
    ulong LiveEntityAddress,
    ulong LiveActorAddress,
    string LiveActorName,
    uint LiveTargetServerObjectId,
    Vector3Snapshot? LivePosition,
    double? DistanceToLocalPlayer,
    PartyMemberVisibilityState VisibilityState)
{
    public bool HasKnownHealth => MaxHp > 0 || CurrentHp > 0;

    public bool IsDead => HasKnownHealth && CurrentHp == 0;

    public bool IsAlive => !HasKnownHealth || CurrentHp > 0;

    public double HpPercent => MaxHp == 0
        ? 100.0D
        : Math.Clamp(CurrentHp * 100.0D / MaxHp, 0.0D, 100.0D);

    public double MpPercent => MaxMp == 0
        ? 100.0D
        : Math.Clamp(CurrentMp * 100.0D / MaxMp, 0.0D, 100.0D);

    public int PhysicalAbnormalCount => AbnormalStatuses.Count(entry => entry.IsPhysicalDebuffCategory);

    public int HarmfulAbnormalCount => AbnormalStatuses.Count(entry => entry.IsHarmfulForRest);

    public bool IsScreenVisible => VisibilityState == PartyMemberVisibilityState.ScreenVisible;
}
