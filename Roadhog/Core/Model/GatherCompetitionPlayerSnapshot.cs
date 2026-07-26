namespace Roadhog.Core.Model;

public sealed record GatherCompetitionPlayerSnapshot(
    ushort EntityId,
    uint ServerObjectId,
    string Name,
    Vector3Snapshot? Position,
    double? DistanceToLocalPlayer,
    uint GatherActionStateRaw,
    uint GatherActionIdRaw,
    uint GatherSourceIdCandidateRaw,
    DateTimeOffset CapturedAt)
{
    public bool IsGatheringActionCandidate =>
        GatherActionStateRaw != 0 &&
        GatherActionIdRaw is 1036 or 1037 or 1038;

    public bool MatchesGatherSource(uint gatherSourceId)
    {
        return gatherSourceId != 0 &&
               IsGatheringActionCandidate &&
               GatherSourceIdCandidateRaw == gatherSourceId;
    }
}
