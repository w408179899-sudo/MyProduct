namespace Roadhog.Core.Model;

public sealed record PlayerSnapshot(
    ushort EntityId,
    ushort TargetEntityId,
    uint CurrentHp,
    uint MaxHp,
    uint CurrentMp,
    uint MaxMp,
    ushort CurrentDp,
    Vector3Snapshot? Position,
    DateTimeOffset CapturedAt);
