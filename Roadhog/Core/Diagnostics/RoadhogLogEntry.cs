namespace Roadhog.Core.Diagnostics;

public sealed record RoadhogLogEntry(
    DateTimeOffset Timestamp,
    string Level,
    string EventName,
    IReadOnlyDictionary<string, object?> Fields,
    string? Error);
