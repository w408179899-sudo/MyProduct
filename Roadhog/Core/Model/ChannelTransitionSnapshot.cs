namespace Roadhog.Core.Model;

// A ready value contains a player captured together with the scene and channel.
// It must never be assembled from the independently cached player channel.
public sealed record ChannelTransitionSnapshot(
    bool InWorld, PlayerSnapshot? Player, ChannelSnapshot? Channel, DateTimeOffset CapturedAt)
{
    public bool IsReady => InWorld && Player is { EntityId: > 0, Position: not null } && Channel is { IsValid: true };
}
