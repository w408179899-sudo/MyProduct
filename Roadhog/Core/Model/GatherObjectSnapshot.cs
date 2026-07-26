namespace Roadhog.Core.Model;

public enum GatherInteractionAvailability
{
    Unknown,
    Allowed,
    Blocked
}

public sealed record GatherObjectSnapshot(
    ushort EntityId,
    uint ServerObjectId,
    uint GatherSourceId,
    string Name,
    ushort DisplayLevel,
    byte RuntimeAvailabilityRaw,
    float InteractionRadius,
    uint InteractionState,
    Vector3Snapshot? Position,
    Vector3Snapshot? SpawnPosition,
    double? DistanceToLocalPlayer,
    bool IsLockedTarget,
    GatherSourceDefinition? Source,
    DateTimeOffset CapturedAt)
{
    public GatherInteractionAvailability InteractionAvailability => InteractionState switch
    {
        19 or 21 or 23 or 25 or 40 => GatherInteractionAvailability.Allowed,
        20 or 22 or 24 or 26 or 41 => GatherInteractionAvailability.Blocked,
        _ => GatherInteractionAvailability.Unknown
    };

    public bool HasConcreteIdentity => ServerObjectId != 0;

    public bool IsRuntimeAvailableCandidate => RuntimeAvailabilityRaw != 0;

    public bool IsGatherableCandidate =>
        HasConcreteIdentity &&
        GatherSourceId != 0 &&
        IsRuntimeAvailableCandidate &&
        InteractionAvailability != GatherInteractionAvailability.Blocked;
}
