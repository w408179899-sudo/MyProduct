namespace Roadhog.Core.Model;

public sealed record WorldObjectSnapshot(
    ushort EntityId,
    uint ServerObjectId,
    string Name,
    string ObjectKind,
    Vector3Snapshot? Position,
    double? DistanceToLocalPlayer,
    uint CurrentHp = 0,
    uint MaxHp = 0,
    uint TargetServerObjectId = 0,
    bool IsTargetingLocalPlayer = false,
    bool AggressiveKnown = false,
    bool IsAggressiveToPlayer = false,
    string? AggressiveSource = null,
    uint LootableRaw = 0,
    uint InteractionState = 0)
{
    public bool HasKnownHealth => CurrentHp > 0 || MaxHp > 0;

    public bool IsAlive => !HasKnownHealth || CurrentHp > 0;

    public bool IsPassiveToPlayer => AggressiveKnown && !IsAggressiveToPlayer;

    public bool HasLoot => LootableRaw != 0;
}
