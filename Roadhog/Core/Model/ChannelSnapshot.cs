namespace Roadhog.Core.Model;

public sealed record ChannelSnapshot(
    int Index,
    int Count,
    uint MapId,
    DateTimeOffset CapturedAt)
{
    public int Number => Index + 1;

    public bool IsValid =>
        MapId != 0 &&
        Count > 0 &&
        Index >= 0 &&
        Index < Count;
}
