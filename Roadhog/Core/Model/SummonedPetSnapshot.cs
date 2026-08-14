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
    string EvidenceSource = "",
    SummonedPetHealthFieldValidity HealthFields = default)
{
    public const uint ActorObjectType = 2;

    public bool HasKnownHealth => CurrentHp > 0 || MaxHp > 0;

    public bool HasReliableHealth =>
        HealthFields.CurrentHp &&
        HealthFields.MaxHp &&
        MaxHp > 0 &&
        CurrentHp <= MaxHp;

    public double ReliableHpPercent => HasReliableHealth
        ? Math.Clamp(CurrentHp * 100.0D / MaxHp, 0.0D, 100.0D)
        : 0.0D;

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

public readonly record struct SummonedPetHealthFieldValidity(
    bool CurrentHp,
    bool MaxHp,
    bool HpPercent);
