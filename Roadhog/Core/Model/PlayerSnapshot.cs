namespace Roadhog.Core.Model;

public sealed record PlayerSnapshot(
    ushort EntityId,
    ushort TargetEntityId,
    string CharacterName,
    uint CurrentHp,
    uint MaxHp,
    uint CurrentMp,
    uint MaxMp,
    ushort CurrentDp,
    Vector3Snapshot? Position,
    DateTimeOffset CapturedAt,
    double? CameraYawDegrees = null,
    double? CameraPitchDegrees = null,
    double? ActorYawDegrees = null);
