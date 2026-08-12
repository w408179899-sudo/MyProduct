namespace Roadhog.Core.Radar;

public sealed class RadarMapDocument
{
    public const int CurrentVersion = 1;

    public int Version { get; set; } = CurrentVersion;

    public uint MapId { get; set; }

    public string MapCode { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;

    public List<RadarObstacleSegment> Segments { get; set; } = new();

    public RadarMapDocument Clone()
    {
        return new RadarMapDocument
        {
            Version = Version,
            MapId = MapId,
            MapCode = MapCode,
            CreatedAt = CreatedAt,
            UpdatedAt = UpdatedAt,
            Segments = Segments?.Select(segment => segment.Clone()).ToList() ?? new List<RadarObstacleSegment>()
        };
    }
}

public sealed class RadarObstacleSegment
{
    public string Id { get; set; } = string.Empty;

    public RadarPoint Start { get; set; }

    public RadarPoint End { get; set; }

    public double Length => Start.DistanceTo(End);

    public RadarObstacleSegment Clone()
    {
        return new RadarObstacleSegment
        {
            Id = Id,
            Start = Start,
            End = End
        };
    }
}

public sealed record RadarMapLoadResult(bool Found, RadarMapDocument Document);
