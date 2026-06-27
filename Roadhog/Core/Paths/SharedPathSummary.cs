namespace Roadhog.Core.Paths;

public sealed record SharedPathSummary(
    string Name,
    int PointCount,
    double TotalDistance,
    DateTimeOffset UpdatedAt);
