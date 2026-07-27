namespace Roadhog.Core.Model;

public sealed record GatherSnapshot(
    ushort LocalEntityId,
    uint LocalServerObjectId,
    Vector3Snapshot? LocalPosition,
    IReadOnlyList<GatherObjectSnapshot> Objects,
    IReadOnlyList<GatherCompetitionPlayerSnapshot> NearbyPlayers,
    IReadOnlyList<WorldObjectSnapshot> NearbyMonsters,
    bool MonsterDataAvailable,
    bool CompetitionDataAvailable,
    DateTimeOffset CapturedAt)
{
    public static GatherSnapshot Empty(DateTimeOffset capturedAt)
    {
        return new GatherSnapshot(
            0,
            0,
            null,
            Array.Empty<GatherObjectSnapshot>(),
            Array.Empty<GatherCompetitionPlayerSnapshot>(),
            Array.Empty<WorldObjectSnapshot>(),
            false,
            false,
            capturedAt);
    }

    public GatherObjectSnapshot? FindObject(uint serverObjectId)
    {
        return serverObjectId == 0
            ? null
            : Objects.FirstOrDefault(item => item.ServerObjectId == serverObjectId);
    }

    public bool ContainsObject(uint serverObjectId)
    {
        return FindObject(serverObjectId) is not null;
    }

    public IReadOnlyList<GatherCompetitionPlayerSnapshot> FindLikelyCompetitors(
        GatherObjectSnapshot target,
        double radiusMeters)
    {
        if (!CompetitionDataAvailable ||
            target.GatherSourceId == 0 ||
            (target.Position ?? target.SpawnPosition) is not { } targetPosition ||
            radiusMeters <= 0)
        {
            return Array.Empty<GatherCompetitionPlayerSnapshot>();
        }

        var radiusSquared = radiusMeters * radiusMeters;
        return NearbyPlayers
            .Where(player =>
                player.MatchesGatherSource(target.GatherSourceId) &&
                player.Position is { } playerPosition &&
                DistanceSquared(targetPosition, playerPosition) <= radiusSquared)
            .OrderBy(player =>
                player.Position is { } playerPosition
                    ? DistanceSquared(targetPosition, playerPosition)
                    : double.MaxValue)
            .ToArray();
    }

    public bool IsLikelyOccupied(GatherObjectSnapshot target, double radiusMeters)
    {
        return FindLikelyCompetitors(target, radiusMeters).Count > 0;
    }

    private static double DistanceSquared(Vector3Snapshot left, Vector3Snapshot right)
    {
        var dx = left.X - right.X;
        var dy = left.Y - right.Y;
        var dz = left.Z - right.Z;
        return (dx * dx) + (dy * dy) + (dz * dz);
    }
}
