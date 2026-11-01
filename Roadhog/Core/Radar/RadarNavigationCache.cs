namespace Roadhog.Core.Radar;

// Derived map data. Version/fingerprints let old maps and edited geometry rebuild safely.
public sealed class RadarNavigationCache
{
    public int Version { get; set; }
    public string GeometryHash { get; set; } = string.Empty;
    public string ContentHash { get; set; } = string.Empty;
    public List<RadarPoint> Points { get; set; } = new();
    public List<RadarNavigationEdge> Edges { get; set; } = new();

    public RadarNavigationCache Clone() => new()
    {
        Version = Version,
        GeometryHash = GeometryHash,
        ContentHash = ContentHash,
        Points = Points?.ToList() ?? new(),
        Edges = Edges?.ToList() ?? new()
    };
}

// Each undirected edge is stored once; runtime adjacency is reconstructed on load.
public readonly record struct RadarNavigationEdge(int From, int To, double Distance);

public sealed class RadarNavigationGraph
{
    private readonly RadarNavigationCache _cache;
    private readonly IReadOnlyList<(int Target, double Cost)>[] _adjacency;

    internal RadarNavigationGraph(
        IReadOnlyList<RadarObstacleSegment> obstacles,
        RadarNavigationCache cache,
        bool loadedFromCache)
    {
        _cache = cache.Clone();
        Obstacles = Array.AsReadOnly(obstacles.Select(segment => segment.Clone()).ToArray());
        Points = _cache.Points.AsReadOnly();
        var adjacency = Enumerable.Range(0, Points.Count)
            .Select(_ => new List<(int Target, double Cost)>()).ToArray();
        foreach (var edge in _cache.Edges)
        {
            adjacency[edge.From].Add((edge.To, edge.Distance));
            adjacency[edge.To].Add((edge.From, edge.Distance));
        }
        _adjacency = adjacency.Select(edges => (IReadOnlyList<(int Target, double Cost)>)edges.AsReadOnly()).ToArray();
        LoadedFromCache = loadedFromCache;
    }

    public IReadOnlyList<RadarObstacleSegment> Obstacles { get; }
    public IReadOnlyList<RadarPoint> Points { get; }
    public int EdgeCount => _cache.Edges.Count;
    public bool LoadedFromCache { get; }
    public RadarNavigationCache ToCache() => _cache.Clone();
    internal IReadOnlyList<(int Target, double Cost)> Neighbors(int node) => _adjacency[node];
}
