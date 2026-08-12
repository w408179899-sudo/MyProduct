using Roadhog.Core.Model;

namespace Roadhog.Core.Radar;

public sealed record RadarLiveSnapshot(
    uint MapId,
    PlayerSnapshot? Player,
    IReadOnlyList<WorldObjectSnapshot> WorldObjects,
    DateTimeOffset CapturedAt,
    string? Error = null)
{
    public bool HasUsablePlayer => Player?.Position is not null;

    public bool IsStale(DateTimeOffset now, TimeSpan maximumAge)
    {
        return now - CapturedAt > maximumAge;
    }
}
