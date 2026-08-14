namespace Roadhog.Core.Model;

public enum SummonedPetRosterReadCompleteness
{
    Complete = 0,
    Partial = 1,
    Failed = 2
}

public enum SummonedPetRosterTraversalTermination
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

public enum LocalSummonedPetPresence
{
    Unknown = 0,
    Present = 1,
    Absent = 2
}

/// <summary>
/// Availability of fields needed to decide local-pet identity. A stored zero
/// is business evidence only when its matching availability flag is true.
/// </summary>
public sealed record SummonedPetRosterFieldValidity(
    bool LocalServerObjectId,
    bool LocalLinkedPetServerObjectId,
    bool VisibleActorTraversal);

public sealed record SummonedPetRosterReadDiagnostics(
    long CaptureSequence,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt,
    bool BypassMemoryCache,
    SummonedPetRosterTraversalTermination TraversalTermination,
    int ScannedServerObjects,
    int EmittedActors,
    int NodeIdentityReadFailures,
    int EntityLookupFailures,
    int EntityTypeReadFailures,
    int ActorResolutionFailures,
    int ActorIdentityMismatches,
    int OwnerFieldReadFailures,
    int StaticMetadataMisses,
    int LocalPetCandidateCount,
    string? FirstIssue)
{
    public double DurationMilliseconds => Math.Max(0D, (CompletedAt - StartedAt).TotalMilliseconds);
}

public sealed record LocalSummonedPetPresenceDecision(
    LocalSummonedPetPresence Presence,
    uint ServerObjectId,
    long CaptureSequence,
    string Reason)
{
    public bool IsPresent => Presence == LocalSummonedPetPresence.Present && ServerObjectId != 0;

    public bool IsExplicitlyAbsent => Presence == LocalSummonedPetPresence.Absent;
}

/// <summary>
/// Quality-aware summoned-pet roster capture. Positive local-pet identity can
/// be established by a valid non-zero local link even when optional pet detail
/// is incomplete. Absence requires a complete capture and a valid zero link.
/// </summary>
public sealed record SummonedPetRosterReadResult(
    SummonedPetRosterReadCompleteness Completeness,
    SummonedPetRosterSnapshot? Snapshot,
    SummonedPetRosterFieldValidity Fields,
    SummonedPetRosterReadDiagnostics Diagnostics,
    string? Error = null)
{
    public LocalSummonedPetPresenceDecision ResolveLocalPetPresence()
    {
        if (Snapshot is null || Completeness == SummonedPetRosterReadCompleteness.Failed)
        {
            return Unknown("capture_failed");
        }

        if (!Fields.LocalServerObjectId || Snapshot.LocalServerObjectId == 0)
        {
            return Unknown("local_identity_unknown");
        }

        if (!Fields.LocalLinkedPetServerObjectId)
        {
            return Unknown("local_pet_link_unknown");
        }

        if (Snapshot.LocalLinkedPetServerObjectId != 0)
        {
            return new LocalSummonedPetPresenceDecision(
                LocalSummonedPetPresence.Present,
                Snapshot.LocalLinkedPetServerObjectId,
                Diagnostics.CaptureSequence,
                "local_pet_link_present");
        }

        if (Completeness != SummonedPetRosterReadCompleteness.Complete ||
            !Fields.VisibleActorTraversal ||
            Diagnostics.LocalPetCandidateCount != 0)
        {
            return Unknown("local_pet_absence_unconfirmed");
        }

        return new LocalSummonedPetPresenceDecision(
            LocalSummonedPetPresence.Absent,
            0,
            Diagnostics.CaptureSequence,
            "complete_zero_local_pet_link");
    }

    public static SummonedPetRosterReadResult Failed(
        long captureSequence,
        DateTimeOffset startedAt,
        DateTimeOffset completedAt,
        bool bypassMemoryCache,
        string error)
    {
        var normalizedError = string.IsNullOrWhiteSpace(error)
            ? "Summoned-pet roster read failed."
            : error;
        return new SummonedPetRosterReadResult(
            SummonedPetRosterReadCompleteness.Failed,
            null,
            new SummonedPetRosterFieldValidity(false, false, false),
            new SummonedPetRosterReadDiagnostics(
                captureSequence,
                startedAt,
                completedAt,
                bypassMemoryCache,
                SummonedPetRosterTraversalTermination.AnchorReadFailed,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                normalizedError),
            normalizedError);
    }

    /// <summary>
    /// A legacy success may supply useful display data, but it can never prove
    /// absence because it has no structural or field-validity contract.
    /// </summary>
    public static SummonedPetRosterReadResult FromLegacy(
        SummonedPetRosterSnapshot? snapshot,
        DateTimeOffset startedAt,
        DateTimeOffset completedAt,
        bool bypassMemoryCache,
        string? error = null)
    {
        return new SummonedPetRosterReadResult(
            snapshot is null
                ? SummonedPetRosterReadCompleteness.Failed
                : SummonedPetRosterReadCompleteness.Partial,
            snapshot,
            new SummonedPetRosterFieldValidity(
                snapshot?.LocalServerObjectId != 0,
                false,
                false),
            new SummonedPetRosterReadDiagnostics(
                0,
                startedAt,
                completedAt,
                bypassMemoryCache,
                snapshot is null
                    ? SummonedPetRosterTraversalTermination.AnchorReadFailed
                    : SummonedPetRosterTraversalTermination.NotStarted,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                snapshot is null ? error : "legacy_roster_has_no_quality_contract"),
            error);
    }

    private LocalSummonedPetPresenceDecision Unknown(string reason)
    {
        return new LocalSummonedPetPresenceDecision(
            LocalSummonedPetPresence.Unknown,
            0,
            Diagnostics.CaptureSequence,
            reason);
    }
}
