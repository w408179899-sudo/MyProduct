namespace Roadhog.Core.Paths;

public sealed class SharedPathDocument
{
    public int Version { get; set; } = 1;

    public string Name { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;

    public string CleanupNpcName { get; set; } = string.Empty;

    public List<SharedPathPoint> Points { get; set; } = new();

    public int PointCount => Points?.Count ?? 0;

    public double TotalDistance => Points is { Count: > 0 }
        ? Points[^1].TotalDistance
        : 0.0D;

    public SharedPathDocument Clone()
    {
        return new SharedPathDocument
        {
            Version = Version,
            Name = Name,
            CreatedAt = CreatedAt,
            UpdatedAt = UpdatedAt,
            CleanupNpcName = CleanupNpcName,
            Points = Points?.Select(point => point.Clone()).ToList() ?? new List<SharedPathPoint>()
        };
    }
}
