namespace Roadhog.Core.Model;

public readonly record struct PathFollowPose(
    Vector3Snapshot? Position,
    DateTimeOffset CapturedAt,
    double? CameraYawDegrees,
    double? CameraPitchDegrees,
    double? ActorYawDegrees)
{
    public static PathFollowPose FromPlayer(PlayerSnapshot player) =>
        new(player.Position, player.CapturedAt, player.CameraYawDegrees, player.CameraPitchDegrees, player.ActorYawDegrees);
}
