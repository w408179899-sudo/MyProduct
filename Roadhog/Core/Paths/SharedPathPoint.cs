using Roadhog.Core.Model;

namespace Roadhog.Core.Paths;

public sealed class SharedPathPoint
{
    public int Index { get; set; }

    public double X { get; set; }

    public double Y { get; set; }

    public double Z { get; set; }

    public double SegmentDistance { get; set; }

    public double TotalDistance { get; set; }

    public DateTimeOffset RecordedAt { get; set; } = DateTimeOffset.Now;

    public SharedPathPoint Clone()
    {
        return new SharedPathPoint
        {
            Index = Index,
            X = X,
            Y = Y,
            Z = Z,
            SegmentDistance = SegmentDistance,
            TotalDistance = TotalDistance,
            RecordedAt = RecordedAt
        };
    }

    public Vector3Snapshot ToVector3()
    {
        return new Vector3Snapshot((float)X, (float)Y, (float)Z);
    }
}
