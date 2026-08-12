using System.Diagnostics;

namespace Roadhog.Core.Radar;

public sealed record RadarRouteRequest(
    RadarPoint Start,
    RadarPoint Goal,
    IReadOnlyList<RadarObstacleSegment> Obstacles,
    double MaximumExtraDistanceMeters = 30.0D);

public sealed record RadarRoutePlan(
    bool Success,
    bool Direct,
    IReadOnlyList<RadarPoint> Points,
    double DirectDistance,
    double RouteDistance,
    int EvaluatedObstacleCount,
    TimeSpan Elapsed,
    string Reason)
{
    public int WaypointCount => Math.Max(0, Points.Count - 1);

    public static RadarRoutePlan Unreachable(
        double directDistance,
        int obstacleCount,
        TimeSpan elapsed,
        string reason)
    {
        return new RadarRoutePlan(
            false,
            false,
            Array.Empty<RadarPoint>(),
            directDistance,
            0.0D,
            obstacleCount,
            elapsed,
            reason);
    }
}

public sealed class RadarRoutePlanner
{
    private const int EndpointCandidateCount = 16;
    private const int MaximumCandidateNodeCount = 1200;
    private const double EndpointCandidateRadiusMeters = 0.5D;
    private const double EndpointCandidateMinimumSeparationMeters = 0.05D;

    public RadarRoutePlan Plan(RadarRouteRequest request)
    {
        var stopwatch = Stopwatch.StartNew();
        var obstacles = request.Obstacles ?? Array.Empty<RadarObstacleSegment>();
        var directDistance = request.Start.DistanceTo(request.Goal);
        if (directDistance <= 0.001D)
        {
            return new RadarRoutePlan(
                true,
                true,
                new[] { request.Start },
                directDistance,
                0.0D,
                obstacles.Count,
                stopwatch.Elapsed,
                "already_at_goal");
        }

        if (RadarGeometry.IsPathClear(request.Start, request.Goal, obstacles))
        {
            return new RadarRoutePlan(
                true,
                true,
                new[] { request.Start, request.Goal },
                directDistance,
                directDistance,
                obstacles.Count,
                stopwatch.Elapsed,
                "direct");
        }

        var nodes = BuildCandidateNodes(request.Start, request.Goal, obstacles);
        if (nodes.Count > MaximumCandidateNodeCount)
        {
            return RadarRoutePlan.Unreachable(
                directDistance,
                obstacles.Count,
                stopwatch.Elapsed,
                "candidate_budget_exceeded");
        }

        var adjacency = BuildVisibilityGraph(nodes, obstacles);
        var pathIndices = FindShortestPath(nodes, adjacency);
        if (pathIndices.Count == 0)
        {
            return RadarRoutePlan.Unreachable(
                directDistance,
                obstacles.Count,
                stopwatch.Elapsed,
                "no_route");
        }

        var rawPoints = pathIndices.Select(index => nodes[index]).ToArray();
        var points = Simplify(rawPoints, obstacles);
        var routeDistance = CalculateLength(points);
        if (routeDistance > directDistance + Math.Max(0.0D, request.MaximumExtraDistanceMeters))
        {
            return RadarRoutePlan.Unreachable(
                directDistance,
                obstacles.Count,
                stopwatch.Elapsed,
                "detour_limit_exceeded");
        }

        return new RadarRoutePlan(
            true,
            false,
            points,
            directDistance,
            routeDistance,
            obstacles.Count,
            stopwatch.Elapsed,
            "planned");
    }

    private static List<RadarPoint> BuildCandidateNodes(
        RadarPoint start,
        RadarPoint goal,
        IReadOnlyList<RadarObstacleSegment> obstacles)
    {
        var nodes = new List<RadarPoint> { start, goal };
        foreach (var obstacle in obstacles)
        {
            AddEndpointCandidates(nodes, obstacle.Start, obstacles);
            AddEndpointCandidates(nodes, obstacle.End, obstacles);
        }

        return Deduplicate(nodes);
    }

    private static void AddEndpointCandidates(
        List<RadarPoint> nodes,
        RadarPoint endpoint,
        IReadOnlyList<RadarObstacleSegment> obstacles)
    {
        for (var index = 0; index < EndpointCandidateCount; index++)
        {
            var angle = index * Math.PI * 2.0D / EndpointCandidateCount;
            var candidate = new RadarPoint(
                endpoint.X + Math.Cos(angle) * EndpointCandidateRadiusMeters,
                endpoint.Y + Math.Sin(angle) * EndpointCandidateRadiusMeters);
            if (obstacles.All(obstacle =>
                    RadarGeometry.PointToSegmentDistance(candidate, obstacle.Start, obstacle.End) >
                    EndpointCandidateMinimumSeparationMeters))
            {
                nodes.Add(candidate);
            }
        }
    }

    private static List<RadarPoint> Deduplicate(IReadOnlyList<RadarPoint> nodes)
    {
        var result = new List<RadarPoint>(nodes.Count);
        var keys = new HashSet<(long X, long Y)>();
        foreach (var node in nodes)
        {
            var key = ((long)Math.Round(node.X * 1000.0D), (long)Math.Round(node.Y * 1000.0D));
            if (keys.Add(key))
            {
                result.Add(node);
            }
        }

        return result;
    }

    private static List<(int Target, double Cost)>[] BuildVisibilityGraph(
        IReadOnlyList<RadarPoint> nodes,
        IReadOnlyList<RadarObstacleSegment> obstacles)
    {
        var graph = Enumerable.Range(0, nodes.Count)
            .Select(_ => new List<(int Target, double Cost)>())
            .ToArray();
        for (var left = 0; left < nodes.Count; left++)
        {
            for (var right = left + 1; right < nodes.Count; right++)
            {
                if (!RadarGeometry.IsPathClear(nodes[left], nodes[right], obstacles))
                {
                    continue;
                }

                var cost = nodes[left].DistanceTo(nodes[right]);
                graph[left].Add((right, cost));
                graph[right].Add((left, cost));
            }
        }

        return graph;
    }

    private static IReadOnlyList<int> FindShortestPath(
        IReadOnlyList<RadarPoint> nodes,
        IReadOnlyList<(int Target, double Cost)>[] graph)
    {
        const int startIndex = 0;
        const int goalIndex = 1;
        var distances = Enumerable.Repeat(double.PositiveInfinity, nodes.Count).ToArray();
        var previous = Enumerable.Repeat(-1, nodes.Count).ToArray();
        var queue = new PriorityQueue<int, double>();
        distances[startIndex] = 0.0D;
        queue.Enqueue(startIndex, nodes[startIndex].DistanceTo(nodes[goalIndex]));

        while (queue.TryDequeue(out var current, out _))
        {
            if (current == goalIndex)
            {
                break;
            }

            foreach (var edge in graph[current])
            {
                var candidate = distances[current] + edge.Cost;
                if (candidate + 0.000001D >= distances[edge.Target])
                {
                    continue;
                }

                distances[edge.Target] = candidate;
                previous[edge.Target] = current;
                var priority = candidate + nodes[edge.Target].DistanceTo(nodes[goalIndex]);
                queue.Enqueue(edge.Target, priority);
            }
        }

        if (previous[goalIndex] < 0)
        {
            return Array.Empty<int>();
        }

        var reversed = new List<int>();
        for (var current = goalIndex; current >= 0; current = previous[current])
        {
            reversed.Add(current);
            if (current == startIndex)
            {
                reversed.Reverse();
                return reversed;
            }
        }

        return Array.Empty<int>();
    }

    private static IReadOnlyList<RadarPoint> Simplify(
        IReadOnlyList<RadarPoint> points,
        IReadOnlyList<RadarObstacleSegment> obstacles)
    {
        if (points.Count <= 2)
        {
            return points.ToArray();
        }

        var result = new List<RadarPoint> { points[0] };
        var current = 0;
        while (current < points.Count - 1)
        {
            var next = points.Count - 1;
            while (next > current + 1 &&
                   !RadarGeometry.IsPathClear(points[current], points[next], obstacles))
            {
                next--;
            }

            result.Add(points[next]);
            current = next;
        }

        return result;
    }

    private static double CalculateLength(IReadOnlyList<RadarPoint> points)
    {
        var length = 0.0D;
        for (var index = 1; index < points.Count; index++)
        {
            length += points[index - 1].DistanceTo(points[index]);
        }

        return length;
    }
}
