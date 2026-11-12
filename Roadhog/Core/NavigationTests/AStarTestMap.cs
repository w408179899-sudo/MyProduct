using Roadhog.Core.Model;

namespace Roadhog.Core.NavigationTests;

public sealed class AStarTestMapDocument
{
    public int Version { get; set; } = 1;
    public uint MapId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public List<AStarTestNode> Nodes { get; set; } = new();
}

public sealed record AStarTestNode(Vector3Snapshot Position, int[] Neighbors);

/// <summary>Independent explicit 3D graph. No game reads or obstacle-line conversion.</summary>
public sealed class AStarTestMap
{
    public const int MaximumNodes = 16_000_000;
    public const int MaximumEdges = 128_000_000;
    private readonly Vector3Snapshot[] _positions;
    private readonly int[] _offsets;
    private readonly int[] _links;
    public uint MapId { get; }
    public string Name { get; }
    public string Description { get; }
    public int Count => _positions.Length;
    public int EdgeCount => _links.Length;
    public Vector3Snapshot Minimum { get; private set; }
    public Vector3Snapshot Maximum { get; private set; }
    public Vector3Snapshot Position(int index) => _positions[index];
    public ReadOnlySpan<int> Neighbors(int index) => _links.AsSpan(_offsets[index], _offsets[index + 1] - _offsets[index]);

    public AStarTestMap(AStarTestMapDocument document, CancellationToken token = default)
    {
        if (document.Version != 1 || document.MapId == 0 || document.Nodes is not { Count: > 0 and <= MaximumNodes })
            throw new InvalidDataException("导航文件版本、地图 ID 或节点数量无效。");
        MapId = document.MapId;
        Name = string.IsNullOrWhiteSpace(document.Name) ? MapId.ToString() : document.Name;
        Description = document.Description ?? string.Empty;
        _positions = new Vector3Snapshot[document.Nodes.Count];
        _offsets = new int[document.Nodes.Count + 1];
        long edgeCount = 0;
        for (var i = 0; i < Count; i++)
        {
            token.ThrowIfCancellationRequested();
            var n = document.Nodes[i];
            if (n is null || !IsFinite(n.Position) || n.Neighbors is null || n.Neighbors.Length > 64)
                throw new InvalidDataException($"导航节点 {i} 无效。");
            _positions[i] = n.Position;
            _offsets[i] = (int)edgeCount;
            if ((edgeCount += n.Neighbors.Length) > MaximumEdges) throw new InvalidDataException("导航连接数量超出测试模块限制。");
        }
        _offsets[Count] = (int)edgeCount;
        _links = new int[(int)edgeCount];
        for (var i = 0; i < Count; i++) document.Nodes[i].Neighbors.CopyTo(_links, _offsets[i]);
        Validate(token);
    }

    // Ownership is transferred only by the file reader; public callers use the copying document constructor.
    internal AStarTestMap(uint mapId, string name, string description, Vector3Snapshot[] positions,
        int[] offsets, int[] links, CancellationToken token)
    {
        MapId = mapId; Name = name; Description = description;
        _positions = positions; _offsets = offsets; _links = links;
        Validate(token);
    }

    private void Validate(CancellationToken token)
    {
        if (MapId == 0 || Count is < 1 or > MaximumNodes || _links.Length > MaximumEdges ||
            _offsets.Length != Count + 1 || _offsets[0] != 0 || _offsets[^1] != _links.Length)
            throw new InvalidDataException("导航地图 ID、节点或连接数量无效。");
        var min = new Vector3Snapshot(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
        var max = new Vector3Snapshot(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);
        for (var i = 0; i < Count; i++)
        {
            if ((i & 1023) == 0) token.ThrowIfCancellationRequested();
            var p = _positions[i];
            if (!IsFinite(p) || _offsets[i] < 0 || _offsets[i + 1] < _offsets[i] ||
                _offsets[i + 1] > _links.Length || _offsets[i + 1] - _offsets[i] > 64)
                throw new InvalidDataException($"导航节点 {i} 无效。");
            min = new(Math.Min(min.X, p.X), Math.Min(min.Y, p.Y), Math.Min(min.Z, p.Z));
            max = new(Math.Max(max.X, p.X), Math.Max(max.Y, p.Y), Math.Max(max.Z, p.Z));
            var neighbors = Neighbors(i);
            for (var k = 0; k < neighbors.Length; k++)
            {
                var j = neighbors[k];
                if (j < 0 || j >= Count || j == i || neighbors[..k].Contains(j))
                    throw new InvalidDataException($"导航节点 {i} 的连接无效。");
                if (Distance(p, _positions[j]) < 0.00001)
                    throw new InvalidDataException("导航连接包含重合节点。");
            }
        }
        Minimum = min; Maximum = max;
    }

    public int FindNearest(Vector3Snapshot point, CancellationToken token)
    {
        if (!IsFinite(point)) return -1;
        var best = double.PositiveInfinity;
        var result = -1;
        for (var i = 0; i < Count; i++)
        {
            if ((i & 1023) == 0) token.ThrowIfCancellationRequested();
            var n = Position(i);
            // Keep start/goal on their own floor and inside the mapped area.
            if (Math.Abs(n.Z - point.Z) > 1 || HorizontalDistance(n, point) > 1.5) continue;
            var d = Distance(n, point);
            if (d < best) { best = d; result = i; }
        }
        return result;
    }

    public static bool IsFinite(Vector3Snapshot p) => float.IsFinite(p.X) && float.IsFinite(p.Y) && float.IsFinite(p.Z);
    public static double HorizontalDistance(Vector3Snapshot a, Vector3Snapshot b) => Math.Sqrt(
        Math.Pow((double)a.X - b.X, 2) + Math.Pow((double)a.Y - b.Y, 2));
    public static double Distance(Vector3Snapshot a, Vector3Snapshot b) => Math.Sqrt(
        Math.Pow((double)a.X - b.X, 2) + Math.Pow((double)a.Y - b.Y, 2) + Math.Pow((double)a.Z - b.Z, 2));
}
