namespace Roadhog.Core.Model;

/// <summary>
/// Describes world-object coverage completeness. Partial includes an interrupted
/// traversal or a scanned object that could not be represented safely. Even a
/// Complete result may contain observations whose individual behavior-driving
/// fields are unknown; callers must inspect <see cref="WorldObjectFieldValidity"/>
/// before drawing a negative conclusion from a default value.
/// </summary>
internal enum WorldObjectReadCompleteness
{
    Complete = 0,
    Partial = 1,
    Failed = 2
}

internal enum WorldObjectTraversalTermination
{
    NotStarted = 0,
    EmptyTree = 1,
    ReachedTreeEnd = 2,
    AnchorReadFailed = 3,
    TraversalReadFailed = 4,
    SelfLoopDetected = 5,
    CycleDetected = 6,
    GuardLimitReached = 7
}

/// <summary>
/// Describes whether behavior-driving fields were actually read. A valid zero is
/// intentionally different from a zero left behind by a failed memory read.
/// </summary>
internal sealed record WorldObjectFieldValidity(
    bool CurrentHp,
    bool MaxHp,
    bool TargetServerObjectId,
    bool IsTargetingLocalPlayer,
    bool LootableRaw,
    bool InteractionState);

internal sealed record WorldObjectObservation(
    WorldObjectSnapshot Snapshot,
    WorldObjectFieldValidity Fields);

internal sealed record WorldObjectReadDiagnostics(
    long CaptureSequence,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt,
    bool BypassMemoryCache,
    WorldObjectTraversalTermination TraversalTermination,
    bool LocalServerObjectIdAvailable,
    int ScannedServerObjects,
    int ResolvedEntities,
    int NpcLikeEntities,
    int EmittedObjects,
    int NodeIdentityReadFailures,
    int EntityLookupFailures,
    int EntityTypeReadFailures,
    int PositionReadFailures,
    int ActorResolutionFailures,
    int ActorIdentityMismatches,
    int StaticMetadataMisses,
    int StaticCatalogErrors,
    int TargetFieldReadFailures,
    int HealthFieldReadFailures,
    int LootFieldReadFailures,
    int InteractionStateReadFailures,
    string? FirstIssue)
{
    public double DurationMilliseconds => Math.Max(0D, (CompletedAt - StartedAt).TotalMilliseconds);
}

internal sealed record WorldObjectReadResult
{
    public WorldObjectReadResult(
        WorldObjectReadCompleteness completeness,
        IReadOnlyList<WorldObjectObservation>? observations,
        WorldObjectReadDiagnostics diagnostics,
        string? error = null)
    {
        Completeness = completeness;
        Observations = observations?.ToArray() ?? Array.Empty<WorldObjectObservation>();
        Diagnostics = diagnostics;
        Error = error;
        Objects = Observations.Select(static observation => observation.Snapshot).ToArray();
    }

    public WorldObjectReadCompleteness Completeness { get; }

    public IReadOnlyList<WorldObjectObservation> Observations { get; }

    public IReadOnlyList<WorldObjectSnapshot> Objects { get; }

    public WorldObjectReadDiagnostics Diagnostics { get; }

    public string? Error { get; }
}
