namespace Roadhog.Core.Model;

public sealed record LootCorpseSnapshot(
    ushort EntityId,
    uint ServerObjectId,
    ushort EntityType,
    uint ObjectType,
    uint NpcTemplateId,
    ushort Level,
    string Name,
    Vector3Snapshot? Position,
    double? DistanceToLocalPlayer,
    uint CurrentHp,
    uint MaxHp,
    byte HpPercent,
    uint LootableRaw,
    uint InteractionState,
    DateTimeOffset CapturedAt)
{
    public const uint MonsterObjectType = 2;

    public bool IsLootable => LootableRaw != 0;

    public bool IsCorpse => CurrentHp == 0 || HpPercent == 0 || IsLootable;

    public bool IsMonsterCorpse => ObjectType == MonsterObjectType && IsCorpse;
}
