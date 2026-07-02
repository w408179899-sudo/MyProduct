namespace Roadhog.Core.Model;

public sealed record SummonedPetSnapshot(
    bool IsSummoned,
    ushort EntityId,
    uint ServerObjectId,
    ushort EntityType,
    uint ObjectType,
    uint NpcTemplateId,
    string Name,
    string StaticName,
    string NpcType,
    string Tribe,
    ushort Level,
    uint CurrentHp,
    uint MaxHp,
    byte HpPercent,
    Vector3Snapshot? Position,
    double? DistanceToLocalPlayer,
    uint LocalServerObjectId,
    DateTimeOffset CapturedAt,
    uint LocalLinkedPetServerObjectId = 0,
    bool OwnerConfirmed = false,
    string EvidenceSource = "")
{
    public const uint ActorObjectType = 2;

    public bool HasKnownHealth => CurrentHp > 0 || MaxHp > 0;

    public bool IsAlive => IsSummoned && (!HasKnownHealth || CurrentHp > 0);

    public static SummonedPetSnapshot NotSummoned(uint localServerObjectId, DateTimeOffset capturedAt)
    {
        return new SummonedPetSnapshot(
            false,
            0,
            0,
            0,
            0,
            0,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            0,
            0,
            0,
            0,
            null,
            null,
            localServerObjectId,
            capturedAt);
    }
}
