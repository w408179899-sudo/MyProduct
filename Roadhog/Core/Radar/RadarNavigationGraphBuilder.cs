using System.Security.Cryptography;

namespace Roadhog.Core.Radar;

public static class RadarNavigationGraphBuilder
{
    // Bump whenever point generation or path intersection semantics change.
    public const int CurrentVersion = 3;
    public const double ExtensionMeters = 1.7D;
    public const double MinimumExtensionMeters = 0.1D;
    public const double MaximumExtensionMeters = 100.0D;

    public static double NormalizeExtensionMeters(double value) => double.IsFinite(value)
        ? Math.Clamp(value, MinimumExtensionMeters, MaximumExtensionMeters)
        : ExtensionMeters;

    public static RadarNavigationGraph Prepare(
        IReadOnlyList<RadarObstacleSegment> obstacles,
        RadarNavigationCache? cache = null,
        CancellationToken cancellationToken = default,
        double extensionMeters = ExtensionMeters)
    {
        cancellationToken.ThrowIfCancellationRequested();
        extensionMeters = NormalizeExtensionMeters(extensionMeters);
        var geometryHash = GeometryHash(obstacles, extensionMeters);
        if (IsReusable(cache, geometryHash, obstacles.Count))
        {
            return new RadarNavigationGraph(obstacles, cache!, loadedFromCache: true, extensionMeters);
        }

        var points = BuildPoints(obstacles, extensionMeters, cancellationToken);
        var edges = new List<RadarNavigationEdge>();
        for (var from = 0; from < points.Count; from++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (var to = from + 1; to < points.Count; to++)
            {
                if (RadarGeometry.IsPathClear(points[from], points[to], obstacles))
                {
                    edges.Add(new RadarNavigationEdge(from, to, points[from].DistanceTo(points[to])));
                }
            }
        }
        var built = new RadarNavigationCache
        {
            Version = CurrentVersion,
            GeometryHash = geometryHash,
            Points = points,
            Edges = edges
        };
        built.ContentHash = ContentHash(built);
        return new RadarNavigationGraph(obstacles, built, loadedFromCache: false, extensionMeters);
    }

    private static List<RadarPoint> BuildPoints(
        IReadOnlyList<RadarObstacleSegment> obstacles,
        double extensionMeters,
        CancellationToken cancellationToken)
    {
        var points = new List<RadarPoint>(obstacles.Count * 2);
        // Do not round to millimetres: distinct points in a sub-millimetre gap matter.
        var unique = new HashSet<RadarPoint>();
        for (var index = 0; index < obstacles.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var segment = obstacles[index];
            AddEndpoint(segment.Start, segment.End, index);
            AddEndpoint(segment.End, segment.Start, index);
        }
        return points;

        void AddEndpoint(RadarPoint endpoint, RadarPoint otherEnd, int ownIndex)
        {
            var length = endpoint.DistanceTo(otherEnd);
            if (length <= RadarGeometry.CoordinateTolerance) return;
            var direction = new RadarPoint((endpoint.X - otherEnd.X) / length, (endpoint.Y - otherEnd.Y) / length);
            var nearest = double.PositiveInfinity;
            for (var index = 0; index < obstacles.Count; index++)
            {
                if (index == ownIndex) continue;
                if (RadarGeometry.TryRayIntersectionDistance(endpoint, direction, obstacles[index], out var distance))
                    nearest = Math.Min(nearest, distance);
            }
            // A shared/intersecting endpoint has no outward gap under the agreed rule.
            if (nearest <= RadarGeometry.CoordinateTolerance) return;
            var extension = nearest <= extensionMeters + RadarGeometry.CoordinateTolerance
                ? nearest / 2.0D
                : extensionMeters;
            var point = new RadarPoint(endpoint.X + direction.X * extension, endpoint.Y + direction.Y * extension);
            if (RadarGeometry.IsPathClear(point, point, obstacles) && unique.Add(point)) points.Add(point);
        }
    }

    private static bool IsReusable(RadarNavigationCache? cache, string geometryHash, int obstacleCount)
    {
        if (cache is null || cache.Version != CurrentVersion || cache.GeometryHash != geometryHash ||
            cache.Points is null || cache.Edges is null || cache.Points.Count > obstacleCount * 2 ||
            cache.Edges.Count > (long)cache.Points.Count * (cache.Points.Count - 1) / 2 ||
            cache.Points.Any(point => !double.IsFinite(point.X) || !double.IsFinite(point.Y))) return false;

        var unique = new HashSet<(int, int)>();
        foreach (var edge in cache.Edges)
        {
            if (edge.From < 0 || edge.To <= edge.From || edge.To >= cache.Points.Count ||
                !double.IsFinite(edge.Distance) || edge.Distance <= 0 || !unique.Add((edge.From, edge.To))) return false;
            var actual = cache.Points[edge.From].DistanceTo(cache.Points[edge.To]);
            if (Math.Abs(actual - edge.Distance) > RadarGeometry.CoordinateTolerance * Math.Max(1, actual)) return false;
        }
        return cache.ContentHash == ContentHash(cache);
    }

    private static string GeometryHash(IReadOnlyList<RadarObstacleSegment> obstacles, double extensionMeters) => Hash(writer =>
    {
        writer.Write(CurrentVersion);
        writer.Write(extensionMeters);
        writer.Write(obstacles.Count);
        foreach (var segment in obstacles)
        {
            WritePoint(writer, segment.Start);
            WritePoint(writer, segment.End);
        }
    });

    private static string ContentHash(RadarNavigationCache cache) => Hash(writer =>
    {
        writer.Write(cache.Version);
        writer.Write(cache.GeometryHash);
        writer.Write(cache.Points.Count);
        foreach (var point in cache.Points) WritePoint(writer, point);
        writer.Write(cache.Edges.Count);
        foreach (var edge in cache.Edges)
        {
            writer.Write(edge.From);
            writer.Write(edge.To);
            writer.Write(edge.Distance);
        }
    });

    private static void WritePoint(BinaryWriter writer, RadarPoint point)
    {
        writer.Write(point.X);
        writer.Write(point.Y);
    }

    private static string Hash(Action<BinaryWriter> write)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        write(writer);
        writer.Flush();
        return Convert.ToHexString(SHA256.HashData(stream.GetBuffer().AsSpan(0, (int)stream.Length)));
    }
}
