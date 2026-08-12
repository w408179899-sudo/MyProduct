namespace Roadhog.Core.Radar;

public sealed class RadarObstacleSpatialIndex
{
    private const double DefaultCellSizeMeters = 50.0D;
    private readonly IReadOnlyList<RadarObstacleSegment> _segments;
    private readonly Dictionary<(int X, int Y), List<int>> _cells = new();
    private readonly double _cellSize;

    public RadarObstacleSpatialIndex(
        IReadOnlyList<RadarObstacleSegment> segments,
        double cellSizeMeters = DefaultCellSizeMeters)
    {
        _segments = segments ?? Array.Empty<RadarObstacleSegment>();
        _cellSize = Math.Max(5.0D, cellSizeMeters);
        for (var index = 0; index < _segments.Count; index++)
        {
            AddSegment(index, _segments[index]);
        }
    }

    public IReadOnlyList<RadarObstacleSegment> All => _segments;

    public IReadOnlyList<RadarObstacleSegment> QueryCorridor(
        RadarPoint start,
        RadarPoint end,
        double marginMeters)
    {
        if (_segments.Count == 0)
        {
            return Array.Empty<RadarObstacleSegment>();
        }

        var margin = Math.Max(0.0D, marginMeters);
        var minX = Math.Min(start.X, end.X) - margin;
        var maxX = Math.Max(start.X, end.X) + margin;
        var minY = Math.Min(start.Y, end.Y) - margin;
        var maxY = Math.Max(start.Y, end.Y) + margin;
        var indices = new HashSet<int>();
        for (var cellX = Cell(minX); cellX <= Cell(maxX); cellX++)
        {
            for (var cellY = Cell(minY); cellY <= Cell(maxY); cellY++)
            {
                if (_cells.TryGetValue((cellX, cellY), out var cellSegments))
                {
                    indices.UnionWith(cellSegments);
                }
            }
        }

        return indices
            .Where(index => BoundsOverlap(_segments[index], minX, minY, maxX, maxY))
            .Select(index => _segments[index])
            .ToArray();
    }

    private void AddSegment(int index, RadarObstacleSegment segment)
    {
        var minX = Math.Min(segment.Start.X, segment.End.X);
        var maxX = Math.Max(segment.Start.X, segment.End.X);
        var minY = Math.Min(segment.Start.Y, segment.End.Y);
        var maxY = Math.Max(segment.Start.Y, segment.End.Y);
        for (var cellX = Cell(minX); cellX <= Cell(maxX); cellX++)
        {
            for (var cellY = Cell(minY); cellY <= Cell(maxY); cellY++)
            {
                if (!_cells.TryGetValue((cellX, cellY), out var list))
                {
                    list = new List<int>();
                    _cells[(cellX, cellY)] = list;
                }

                list.Add(index);
            }
        }
    }

    private int Cell(double coordinate)
    {
        return (int)Math.Floor(coordinate / _cellSize);
    }

    private static bool BoundsOverlap(
        RadarObstacleSegment segment,
        double minX,
        double minY,
        double maxX,
        double maxY)
    {
        return Math.Max(segment.Start.X, segment.End.X) >= minX &&
               Math.Min(segment.Start.X, segment.End.X) <= maxX &&
               Math.Max(segment.Start.Y, segment.End.Y) >= minY &&
               Math.Min(segment.Start.Y, segment.End.Y) <= maxY;
    }
}
