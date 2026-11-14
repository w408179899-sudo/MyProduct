using System.Diagnostics;
using System.Text.Json;
using Roadhog.Application.Radar;
using Roadhog.Core.Accounts;
using Roadhog.Core.Radar;
using Roadhog.Infrastructure.Radar;

internal static class RadarNavigationGraphTests
{
    public static Task EndpointsExtendAlongEachSegmentAsync()
    {
        var graph = Build(Wall(0, 0, 4, 0));
        Check(graph.Points.SequenceEqual(new[] { new RadarPoint(-1.7, 0), new RadarPoint(5.7, 0) }), "one outward point per endpoint");
        Check(graph.EdgeCount == 0, "opposite endpoints must not connect through their own wall");
        var reversed = Build(Wall(4, 0, 0, 0));
        Check(graph.Points.OrderBy(p => p.X).SequenceEqual(reversed.Points.OrderBy(p => p.X)), "segment direction must not change point locations");
        var diagonal = Build(Wall(1, 1, 4, 5));
        Near(-0.02, diagonal.Points[0].X); Near(-0.36, diagonal.Points[0].Y);
        Near(5.02, diagonal.Points[1].X); Near(6.36, diagonal.Points[1].Y);
        return Task.CompletedTask;
    }

    public static Task NearestIntersectionUsesGapMidpointAsync()
    {
        foreach (var gap in new[] { 3.0, 2.5, 2.0, 1.7001, 1.7, 1.0, 0.2, 0.001, 0.00002 })
        {
            // Two obstructions: choose the first, independent of input order.
            var graph = Build(Wall(0, 0, 4, 0), Wall(-4, -2, -4, 2), Wall(-gap, -1, -gap, 1));
            var expectedExtension = gap <= 1.7 ? gap / 2 : 1.7;
            Check(graph.Points.Contains(new RadarPoint(-expectedExtension, 0)), "incorrect extension for gap " + gap);
            Check(graph.Points.Contains(new RadarPoint(5.7, 0)), "opposite endpoint should keep its default extension");
            AssertGraphClear(graph);
        }
        var collinear = Build(Wall(0, 0, 4, 0), Wall(-3, 0, -0.002, 0));
        Check(collinear.Points.Contains(new RadarPoint(-0.001, 0)), "collinear obstruction must shrink extension");
        var behind = Build(Wall(0, 0, 4, 0), Wall(1, -1, 1, 1));
        Check(behind.Points.Contains(new RadarPoint(-1.7, 0)), "obstacle behind the ray must not shorten it");
        return Task.CompletedTask;
    }

    public static Task SharedEndpointsAndZeroLengthDoNotInventPassagesAsync()
    {
        var walls = new[] { Wall(0, 0, 4, 0), Wall(4, 0, 4, 4), Wall(20, 20, 20, 20) };
        var graph = RadarNavigationGraphBuilder.Prepare(walls);
        Check(graph.Points.Count == 2, "shared endpoint has zero gap, degenerate segment has no direction");
        AssertGraphClear(graph);
        return Task.CompletedTask;
    }

    public static Task PointActorTraversesMillimetreGapAsync()
    {
        foreach (var offset in new[] { 0.0, 3000.0 })
        {
            var graph = Build(Wall(offset + 2, -4, offset + 2, 0), Wall(offset, 0.001, offset + 4, 0.001));
            var start = new RadarPoint(offset, -0.0005);
            var goal = new RadarPoint(offset + 4, -0.0005);
            var plan = Plan(graph, start, goal, 1);
            Check(plan.Success && !plan.Direct, "point actor should pass through a 0.001m gap");
            Check(plan.Points.Contains(new RadarPoint(offset + 2, 0.0005)), "route must use the gap midpoint");
            AssertRouteClear(plan, graph.Obstacles);
            Check(!RadarGeometry.IsPathClear(new RadarPoint(offset, 0), new RadarPoint(offset + 4, 0), graph.Obstacles), "touching endpoint remains blocked");
            var tinyCross = new[] { Wall(offset + 2, -1, offset + 2, 1) };
            var tiny = new RadarRoutePlanner().Plan(new RadarRouteRequest(
                new RadarPoint(offset + 1.9999, 0), new RadarPoint(offset + 2.0001, 0), tinyCross, 0));
            Check(!tiny.Success, "sub-millimetre displacement across a wall is not already_at_goal");
        }
        return Task.CompletedTask;
    }

    public static Task CacheRestoresAndRejectsStaleOrDamagedDataAsync()
    {
        var walls = new[] { Wall(2, -2, 2, 2), Wall(6, 0, 6, 4) };
        var graph = RadarNavigationGraphBuilder.Prepare(walls);
        var cache = graph.ToCache();
        Check(RadarNavigationGraphBuilder.Prepare(walls, cache).LoadedFromCache, "valid cache must restore without geometry rebuild");
        var extended = RadarNavigationGraphBuilder.Prepare(walls, cache, extensionMeters: 3.2);
        Check(!extended.LoadedFromCache, "changing extension must reject the previous graph cache");
        Check(extended.Points.Contains(new RadarPoint(2, -5.2)), "configured extension must move wall endpoints");
        Check(RadarNavigationGraphBuilder.Prepare(walls, extended.ToCache(), extensionMeters: 3.2).LoadedFromCache,
            "same extension must reuse its graph cache");
        Check(!RadarNavigationGraphBuilder.Prepare(walls, extended.ToCache()).LoadedFromCache,
            "default extension must reject another account's custom cache");
        foreach (var invalid in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
            Near(1.7, RadarNavigationGraphBuilder.Prepare(walls, extensionMeters: invalid).ExtensionMeters);
        var edits = new Action<RadarNavigationCache>[]
        {
            c => c.Version++, c => c.Version--, c => c.GeometryHash = "old", c => c.Points[0] = new RadarPoint(2, 0),
            c => c.Edges.RemoveAt(0), c => c.Edges[0] = new RadarNavigationEdge(-1, 99, 1),
            c => c.Edges[0] = c.Edges[0] with { Distance = double.NaN }, c => c.Points = null!
        };
        foreach (var edit in edits)
        {
            var damaged = cache.Clone(); edit(damaged);
            var repaired = RadarNavigationGraphBuilder.Prepare(walls, damaged);
            Check(!repaired.LoadedFromCache, "damaged derived data must rebuild");
            AssertGraphClear(repaired);
        }
        walls[0].End = new RadarPoint(2, 8);
        Check(!RadarNavigationGraphBuilder.Prepare(walls, cache).LoadedFromCache, "geometry edit must invalidate cache");
        Check(graph.Obstacles[0].End == new RadarPoint(2, 2), "prepared graph owns an independent geometry snapshot");
        cache.Points.Clear();
        Check(graph.Points.Count == 4, "exported cache must not mutate the running graph");
        return Task.CompletedTask;
    }

    public static Task AStarMatchesIndependentShortestPathOracleAsync()
    {
        var graph = Build(Wall(2, -3, 2, 2), Wall(4, 0, 4, 5), Wall(6, -3, 6, 1));
        var start = new RadarPoint(0, 0);
        var goal = new RadarPoint(8, 0);
        var result = Plan(graph, start, goal);
        Check(result.Success && result.Points.Count >= 4, "route must chain points belonging to multiple walls");
        AssertRouteClear(result, graph.Obstacles);
        // Floyd-Warshall on an independently built visibility matrix is the oracle.
        var nodes = graph.Points.Concat(new[] { start, goal }).ToArray();
        var distances = new double[nodes.Length, nodes.Length];
        for (var i = 0; i < nodes.Length; i++)
            for (var j = 0; j < nodes.Length; j++)
                distances[i, j] = i == j ? 0 : RadarGeometry.IsPathClear(nodes[i], nodes[j], graph.Obstacles)
                    ? nodes[i].DistanceTo(nodes[j]) : double.PositiveInfinity;
        for (var k = 0; k < nodes.Length; k++)
            for (var i = 0; i < nodes.Length; i++)
                for (var j = 0; j < nodes.Length; j++)
                    distances[i, j] = Math.Min(distances[i, j], distances[i, k] + distances[k, j]);
        Near(distances[nodes.Length - 2, nodes.Length - 1], result.RouteDistance);
        Check(!Plan(graph, start, goal, 0).Success, "existing detour budget still applies");
        return Task.CompletedTask;
    }

    public static Task DenseMapCachesTwoHundredPointsAndReusesGraphAsync()
    {
        var walls = Enumerable.Range(0, 100).Select(i => Wall(i % 10 * 10, i / 10 * 10, i % 10 * 10, i / 10 * 10 + 3)).ToArray();
        var timer = Stopwatch.StartNew();
        var graph = RadarNavigationGraphBuilder.Prepare(walls);
        var buildMs = timer.Elapsed.TotalMilliseconds;
        Check(graph.Points.Count == 200, "100 isolated segments should produce exactly 200 points");
        var before = graph.ToCache().ContentHash;
        timer.Restart();
        for (var query = 0; query < 30; query++)
        {
            var result = Plan(graph, new RadarPoint(-5, 1 + query * 0.01), new RadarPoint(95, 1.5));
            Check(result.Success, "dense map must no longer hit the former candidate budget");
            AssertRouteClear(result, graph.Obstacles);
        }
        Console.WriteLine($"     100 walls: build={buildMs:F2}ms, 30 cached queries={timer.Elapsed.TotalMilliseconds:F2}ms, nodes={graph.Points.Count}, edges={graph.EdgeCount}");
        Check(graph.ToCache().ContentHash == before, "queries must not modify the static graph");
        return Task.CompletedTask;
    }

    public static Task EnclosedTargetAndTouchRemainBlockedAsync()
    {
        var graph = Build(Wall(0, 0, 4, 0), Wall(4, 0, 4, 4), Wall(4, 4, 0, 4), Wall(0, 4, 0, 0));
        Check(!Plan(graph, new RadarPoint(-2, 2), new RadarPoint(2, 2)).Success, "closed walls cannot be crossed");
        Check(!Plan(graph, new RadarPoint(0, 2), new RadarPoint(2, 2)).Success, "start on a wall must not escape by ignoring the intersection");
        return Task.CompletedTask;
    }

    public static async Task MapSavePersistsCacheAndLegacyLoadIsNonDestructiveAsync()
    {
        using var folder = new TestFolder();
        var path = Path.Combine(folder.Path, "77.json");
        var document = new RadarMapDocument { MapId = 77, Segments = new() { Wall(2, -2, 2, 2) } };
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(document));
        var before = await File.ReadAllBytesAsync(path);
        var store = new JsonRadarMapStore(folder.Path);
        var legacy = await store.LoadAsync(77);
        Check(legacy.Success && legacy.Value!.Document.NavigationCache is not null, "old map must load with derived graph");
        Check(before.SequenceEqual(await File.ReadAllBytesAsync(path)), "reading must not overwrite concurrent/editor map data");
        Check((await store.SaveAsync(legacy.Value!.Document)).Success, "save with cache should succeed");
        var saved = JsonSerializer.Deserialize<RadarMapDocument>(await File.ReadAllTextAsync(path))!;
        Check(RadarNavigationGraphBuilder.Prepare(saved.Segments, saved.NavigationCache).LoadedFromCache, "saved graph should restore directly");
        var clone = saved.Clone(); clone.NavigationCache!.Points.Clear();
        Check(saved.NavigationCache!.Points.Count == 2, "map clone must not share mutable cache points");
        saved.Segments[0].End = new RadarPoint(2, 10);
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(saved));
        var changed = await store.LoadAsync(77);
        Check(changed.Value!.Document.NavigationCache!.Points.Contains(new RadarPoint(2, 11.7)), "edited geometry must rebuild stale cache");
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        var current = await File.ReadAllBytesAsync(path);
        try { await store.SaveAsync(saved, canceled.Token); } catch (OperationCanceledException) { }
        Check(current.SequenceEqual(await File.ReadAllBytesAsync(path)), "canceled save must preserve previous map");
        Check(!Directory.GetFiles(folder.Path, "*.tmp").Any(), "atomic cache save must leave no staging file");
    }

    public static async Task NavigatorSharesGraphAndRefreshesAfterMapSaveAsync()
    {
        using var folder = new TestFolder();
        var store = new JsonRadarMapStore(folder.Path);
        var doc = new RadarMapDocument { MapId = 77, Segments = new() { Wall(2, -2, 2, 2) } };
        Check((await store.SaveAsync(doc)).Success, "save setup map");
        var navigator = new StationaryObstacleNavigator(store, new RadarRoutePlanner(), new RadarMapRevisionRegistry());
        var state = new StationaryObstacleNavigationState();
        var settings = new RadarObstacleScriptSettings { Enabled = true };
        var start = new RadarPoint(0, 0); var goal = new RadarPoint(4, 0);
        var first = await navigator.ResolveAsync(state, 77, start, goal, RadarNavigationPurpose.ReturnHome, 0, settings, 0.1);
        Check(first.Action == RadarNavigationAction.MoveToWaypoint, "wall should require detour");
        var graph = state.NavigationGraph;
        var scoring = await navigator.ScoreRouteDistancesAsync(77, start, new[] { new RadarRouteDistanceRequest("target", goal) }, settings);
        Near(first.Plan!.RouteDistance, scoring.Scores["target"].EffectiveDistance);
        await navigator.ResolveAsync(state, 77, start, new RadarPoint(4, 0.2), RadarNavigationPurpose.ReturnHome, 0, settings, 0.1);
        Check(ReferenceEquals(graph, state.NavigationGraph), "moving target must reuse static graph");
        settings.EndpointExtensionMeters = 3.2;
        var extended = await navigator.ResolveAsync(state, 77, start, goal, RadarNavigationPurpose.ReturnHome, 0, settings, 0.1);
        Check(!ReferenceEquals(graph, state.NavigationGraph), "extension edit must replace the active graph");
        Check(extended.Plan!.RouteDistance > first.Plan.RouteDistance, "extension edit must replan the active route");
        var extendedGraph = state.NavigationGraph;
        var extendedScores = await navigator.ScoreRouteDistancesAsync(77, start, new[] { new RadarRouteDistanceRequest("target", goal) }, settings);
        Near(extended.Plan.RouteDistance, extendedScores.Scores["target"].EffectiveDistance);
        var otherSettings = settings.Clone(); otherSettings.EndpointExtensionMeters = 1.7;
        var otherScores = await navigator.ScoreRouteDistancesAsync(77, start, new[] { new RadarRouteDistanceRequest("target", goal) }, otherSettings);
        Near(first.Plan.RouteDistance, otherScores.Scores["target"].EffectiveDistance);
        await navigator.ResolveAsync(state, 77, start, goal, RadarNavigationPurpose.ReturnHome, 0, settings, 0.1);
        Check(ReferenceEquals(extendedGraph, state.NavigationGraph), "another account's scoring must not replace this account's graph");
        doc.Segments.Clear();
        Check((await store.SaveAsync(doc)).Success, "save empty map"); navigator.NotifyMapSaved(77);
        var second = await navigator.ResolveAsync(state, 77, start, goal, RadarNavigationPurpose.ReturnHome, 0, settings, 0.1);
        Check(second.Action == RadarNavigationAction.Direct && !ReferenceEquals(graph, state.NavigationGraph), "map save must replace cached topology");
        File.Delete(Path.Combine(folder.Path, "77.json")); navigator.NotifyMapSaved(77);
        var deleted = await navigator.ResolveAsync(state, 77, start, goal, RadarNavigationPurpose.ReturnHome, 0, settings, 0.1);
        Check(deleted.Action == RadarNavigationAction.Direct, "deleted map must not retain stale walls");
    }

    public static Task Script2IncidentMapPreservesEnclosureAndRoutesOutsideAsync()
    {
        var fixture = Path.Combine(AppContext.BaseDirectory, "Fixtures", "radar-script2-20261101.json");
        var map = JsonSerializer.Deserialize<RadarMapDocument>(File.ReadAllText(fixture))!;
        var graph = RadarNavigationGraphBuilder.Prepare(map.Segments);
        var route = Plan(graph, new RadarPoint(693.508, 2967.151), new RadarPoint(689.8, 2958.04), 30);
        // Walls 44/45/46 form a closed triangle (44 intersects 46). The logged
        // player is inside it: removing the old node budget must not invent an exit.
        var triangle = new[] { new RadarPoint(693.5284403263898, 2966.5376640720297), map.Segments[44].End, map.Segments[45].End };
        var player = new RadarPoint(693.508, 2967.151);
        Check(Enumerable.Range(0, 3).All(i =>
        {
            var a = triangle[i]; var b = triangle[(i + 1) % 3];
            return (b.X - a.X) * (player.Y - a.Y) - (b.Y - a.Y) * (player.X - a.X) > 0;
        }), "incident fixture must still enclose the logged player");
        Check(!route.Success && route.Reason == "no_route", "enclosed player must not cross a drawn wall");
        var outside = Plan(graph, new RadarPoint(691, 2967), new RadarPoint(695, 2967), 30);
        Check(outside.Success && !outside.Direct, "same cluster must remain routable from outside: " + outside.Reason);
        AssertRouteClear(outside, map.Segments);
        Console.WriteLine($"     script 2 replay: walls={map.Segments.Count}, nodes={graph.Points.Count}, inside={route.Reason}, outside={outside.RouteDistance:F3}m/{outside.Points.Count} points");
        return Task.CompletedTask;
    }

    private static RadarObstacleSegment Wall(double x1, double y1, double x2, double y2) =>
        new() { Start = new(x1, y1), End = new(x2, y2) };
    private static RadarNavigationGraph Build(params RadarObstacleSegment[] walls) => RadarNavigationGraphBuilder.Prepare(walls);
    private static RadarRoutePlan Plan(RadarNavigationGraph graph, RadarPoint start, RadarPoint goal, double budget = 200) =>
        new RadarRoutePlanner().Plan(new RadarRouteRequest(start, goal, graph.Obstacles, budget, graph));
    private static void AssertGraphClear(RadarNavigationGraph graph)
    {
        foreach (var point in graph.Points) Check(RadarGeometry.IsPathClear(point, point, graph.Obstacles), "point is on obstacle");
        foreach (var edge in graph.ToCache().Edges)
            Check(RadarGeometry.IsPathClear(graph.Points[edge.From], graph.Points[edge.To], graph.Obstacles), "cached edge crosses obstacle");
    }
    private static void AssertRouteClear(RadarRoutePlan plan, IReadOnlyList<RadarObstacleSegment> walls)
    {
        for (var index = 1; index < plan.Points.Count; index++)
            Check(RadarGeometry.IsPathClear(plan.Points[index - 1], plan.Points[index], walls), "route crosses obstacle");
    }
    private static void Near(double expected, double actual) => Check(Math.Abs(expected - actual) < 1e-8, $"expected {expected}, actual {actual}");
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private sealed class TestFolder : IDisposable
    {
        private readonly string _parent = System.IO.Path.GetFullPath(System.IO.Path.GetTempPath());
        public string Path { get; }
        public TestFolder() { Path = System.IO.Path.Combine(_parent, "roadhog-radar-cache-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(Path); }
        public void Dispose()
        {
            var full = System.IO.Path.GetFullPath(Path);
            if (System.IO.Path.GetDirectoryName(full) != _parent.TrimEnd(System.IO.Path.DirectorySeparatorChar) ||
                !System.IO.Path.GetFileName(full).StartsWith("roadhog-radar-cache-", StringComparison.Ordinal)) throw new InvalidOperationException("Unexpected test path");
            Directory.Delete(full, recursive: true);
        }
    }
}
