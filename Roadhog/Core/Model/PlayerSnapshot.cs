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
    double? ActorYawDegrees = null)
{
    public bool HasKnownHealth => MaxHp > 0;

    public bool IsDead => HasKnownHealth && CurrentHp == 0;

    public bool IsAlive => HasKnownHealth && CurrentHp > 0;

    public double HpPercent => MaxHp == 0
        ? 100.0D
        : Math.Clamp(CurrentHp * 100.0D / MaxHp, 0.0D, 100.0D);

    public double MpPercent => MaxMp == 0
        ? 100.0D
        : Math.Clamp(CurrentMp * 100.0D / MaxMp, 0.0D, 100.0D);
}
