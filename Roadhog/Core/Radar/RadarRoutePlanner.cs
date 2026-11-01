using System.Diagnostics;

namespace Roadhog.Core.Radar;

public sealed record RadarRouteRequest(
    RadarPoint Start,
    RadarPoint Goal,
    IReadOnlyList<RadarObstacleSegment> Obstacles,
    double MaximumExtraDistanceMeters = 30.0D,
    RadarNavigationGraph? NavigationGraph = null);

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
    public RadarRoutePlan Plan(RadarRouteRequest request)
    {
        var stopwatch = Stopwatch.StartNew();
        // A prepared graph owns the obstacle snapshot against which its edges were validated.
        var obstacles = request.NavigationGraph?.Obstacles ?? request.Obstacles ?? Array.Empty<RadarObstacleSegment>();
        var directDistance = request.Start.DistanceTo(request.Goal);
        if (directDistance <= RadarGeometry.CoordinateTolerance &&
            RadarGeometry.IsPathClear(request.Start, request.Goal, obstacles))
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

        var graph = request.NavigationGraph ?? RadarNavigationGraphBuilder.Prepare(obstacles);
        var points = FindShortestPath(request.Start, request.Goal, graph);
        if (points.Count == 0)
        {
            return RadarRoutePlan.Unreachable(
                directDistance,
                obstacles.Count,
                stopwatch.Elapsed,
                "no_route");
        }

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

    private static IReadOnlyList<RadarPoint> FindShortestPath(
        RadarPoint start, RadarPoint goal, RadarNavigationGraph graph)
    {
        // Static nodes/edges are never copied or modified by a query. Only the two
        // dynamic nodes, their connections, and A* search state belong to this call.
        var count = graph.Points.Count;
        var startIndex = count;
        var goalIndex = count + 1;
        var toGoal = Enumerable.Repeat(double.PositiveInfinity, count).ToArray();
        var startEdges = new List<(int Target, double Cost)>();
        for (var index = 0; index < count; index++)
        {
            var point = graph.Points[index];
            if (RadarGeometry.IsPathClear(start, point, graph.Obstacles))
                startEdges.Add((index, start.DistanceTo(point)));
            if (RadarGeometry.IsPathClear(point, goal, graph.Obstacles))
                toGoal[index] = point.DistanceTo(goal);
        }
        var distances = Enumerable.Repeat(double.PositiveInfinity, count + 2).ToArray();
        var previous = Enumerable.Repeat(-1, count + 2).ToArray();
        var closed = new bool[count + 2];
        var queue = new PriorityQueue<int, double>();
        distances[startIndex] = 0;
        queue.Enqueue(startIndex, start.DistanceTo(goal));
        while (queue.TryDequeue(out var current, out _))
        {
            if (closed[current]) continue;
            if (current == goalIndex) break;
            closed[current] = true;
            var edges = current == startIndex ? startEdges : graph.Neighbors(current);
            foreach (var edge in edges) Relax(edge.Target, edge.Cost);
            if (current < count && double.IsFinite(toGoal[current])) Relax(goalIndex, toGoal[current]);

            void Relax(int target, double cost)
            {
                var candidate = distances[current] + cost;
                if (closed[target] || candidate >= distances[target]) return;
                distances[target] = candidate;
                previous[target] = current;
                queue.Enqueue(target, candidate + (target == goalIndex ? 0 : graph.Points[target].DistanceTo(goal)));
            }
        }
        if (previous[goalIndex] < 0) return Array.Empty<RadarPoint>();
        var route = new List<RadarPoint> { goal };
        for (var current = previous[goalIndex]; current != startIndex; current = previous[current])
            route.Add(graph.Points[current]);
        route.Add(start);
        route.Reverse();
        return route;
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
