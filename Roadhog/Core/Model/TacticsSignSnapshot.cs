namespace Roadhog.Core.Model;

public sealed record TacticsSignSnapshot(
    IReadOnlyList<uint> ServerObjectIds,
    DateTimeOffset CapturedAt)
{
    public bool HasActiveSign => ServerObjectIds.Any(serverObjectId => serverObjectId != 0);

    public bool Contains(uint serverObjectId)
    {
        return serverObjectId != 0 && ServerObjectIds.Contains(serverObjectId);
    }

    public static TacticsSignSnapshot Empty(DateTimeOffset capturedAt)
    {
        return new TacticsSignSnapshot(Array.Empty<uint>(), capturedAt);
    }
}
