using System.Globalization;
using System.Text;
using Roadhog.Core.Common;
using Roadhog.Core.Model;

namespace Roadhog.Core.Paths;

public sealed class PathRecordingBuffer
{
    public const double MinimumDistanceMeters = 5.0D;

    private readonly List<SharedPathPoint> _points = new();

    public IReadOnlyList<SharedPathPoint> Points => _points;

    public int Count => _points.Count;

    public double TotalDistance => _points.Count == 0
        ? 0.0D
        : _points[^1].TotalDistance;

    public void Load(IEnumerable<SharedPathPoint>? points)
    {
        _points.Clear();
        if (points is not null)
        {
            _points.AddRange(points.Select(point => point.Clone()));
        }

        Recalculate();
    }

    public OperationResult<SharedPathPoint> TryAdd(
        Vector3Snapshot position,
        DateTimeOffset recordedAt,
        double minimumDistanceMeters = MinimumDistanceMeters)
    {
        minimumDistanceMeters = Math.Max(MinimumDistanceMeters, minimumDistanceMeters);

        if (_points.Count > 0)
        {
            var distance = Distance(_points[^1], position);
            if (distance < minimumDistanceMeters)
            {
                return OperationResult<SharedPathPoint>.Fail(
                    "Path point skipped because distance " +
                    Format(distance, 2) +
                    "m is below " +
                    Format(minimumDistanceMeters, 2) +
                    "m.");
            }
        }

        var point = CreatePoint(position, recordedAt);
        _points.Add(point);
        return OperationResult<SharedPathPoint>.Ok(point.Clone());
    }

    public OperationResult RemoveLast()
    {
        if (_points.Count == 0)
        {
            return OperationResult.Fail("No path point to remove.");
        }

        _points.RemoveAt(_points.Count - 1);
        Recalculate();
        return OperationResult.Ok();
    }

    public void Clear()
    {
        _points.Clear();
    }

    public SharedPathDocument ToDocument(string name)
    {
        var now = DateTimeOffset.Now;
        return new SharedPathDocument
        {
            Version = 1,
            Name = name,
            CreatedAt = now,
            UpdatedAt = now,
            Points = _points.Select(point => point.Clone()).ToList()
        };
    }

    public string ToCoordinateText()
    {
        var sb = new StringBuilder();
        for (var i = 0; i < _points.Count; i++)
        {
            var point = _points[i];
            sb.Append(Format(point.X, 3));
            sb.Append(", ");
            sb.Append(Format(point.Y, 3));
            sb.Append(", ");
            sb.Append(Format(point.Z, 3));
            if (i + 1 < _points.Count)
            {
                sb.AppendLine();
            }
        }

        return sb.ToString();
    }

    public void Recalculate()
    {
        var total = 0.0D;
        for (var i = 0; i < _points.Count; i++)
        {
            var point = _points[i];
            point.Index = i + 1;
            if (i == 0)
            {
                point.SegmentDistance = 0.0D;
                point.TotalDistance = 0.0D;
                continue;
            }

            point.SegmentDistance = Distance(_points[i - 1], point);
            total += point.SegmentDistance;
            point.TotalDistance = total;
        }
    }

    public static double Distance(SharedPathPoint point, Vector3Snapshot position)
    {
        var dx = position.X - point.X;
        var dy = position.Y - point.Y;
        var dz = position.Z - point.Z;
        return Math.Sqrt(dx * dx + dy * dy + dz * dz);
    }

    public static double Distance(SharedPathPoint left, SharedPathPoint right)
    {
        var dx = right.X - left.X;
        var dy = right.Y - left.Y;
        var dz = right.Z - left.Z;
        return Math.Sqrt(dx * dx + dy * dy + dz * dz);
    }

    private SharedPathPoint CreatePoint(Vector3Snapshot position, DateTimeOffset recordedAt)
    {
        var segment = 0.0D;
        var total = 0.0D;
        if (_points.Count > 0)
        {
            segment = Distance(_points[^1], position);
            total = _points[^1].TotalDistance + segment;
        }

        return new SharedPathPoint
        {
            Index = _points.Count + 1,
            X = position.X,
            Y = position.Y,
            Z = position.Z,
            SegmentDistance = segment,
            TotalDistance = total,
            RecordedAt = recordedAt
        };
    }

    private static string Format(double value, int decimals)
    {
        return value.ToString("F" + decimals, CultureInfo.InvariantCulture);
    }
}
