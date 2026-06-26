namespace Roadhog.Core.Model;

public sealed record LockedTargetSnapshot(
    ushort TargetEntityId,
    uint ServerObjectId,
    ushort EntityType,
    uint ObjectType,
    string Name,
    uint CurrentHp,
    uint MaxHp,
    Vector3Snapshot? Position,
    double? DistanceToLocalPlayer,
    DateTimeOffset CapturedAt)
{
    public const uint MonsterObjectType = 2;

    public bool HasTarget => TargetEntityId != 0;

    public bool IsMonster => ObjectType == MonsterObjectType;

    public bool IsAlive => CurrentHp > 0;

    public bool IsMonsterAlive => HasTarget && IsMonster && IsAlive;

    public static LockedTargetSnapshot Empty(DateTimeOffset capturedAt)
    {
        return new LockedTargetSnapshot(
            0,
            0,
            0,
            0,
            string.Empty,
            0,
            0,
            null,
            null,
            capturedAt);
    }
}
