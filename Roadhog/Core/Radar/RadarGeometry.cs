namespace Roadhog.Core.Radar;

public static class RadarGeometry
{
    private const double Epsilon = 0.000001D;

    public static bool IsPathClear(
        RadarPoint start,
        RadarPoint end,
        IReadOnlyList<RadarObstacleSegment> obstacles)
    {
        foreach (var obstacle in obstacles)
        {
            if (SegmentsIntersect(start, end, obstacle.Start, obstacle.End))
            {
                return false;
            }
        }

        return true;
    }

    public static double SegmentDistance(
        RadarPoint a,
        RadarPoint b,
        RadarPoint c,
        RadarPoint d)
    {
        if (SegmentsIntersect(a, b, c, d))
        {
            return 0.0D;
        }

        return Math.Min(
            Math.Min(PointToSegmentDistance(a, c, d), PointToSegmentDistance(b, c, d)),
            Math.Min(PointToSegmentDistance(c, a, b), PointToSegmentDistance(d, a, b)));
    }

    public static double PointToSegmentDistance(RadarPoint point, RadarPoint start, RadarPoint end)
    {
        var dx = end.X - start.X;
        var dy = end.Y - start.Y;
        var lengthSquared = dx * dx + dy * dy;
        if (lengthSquared <= Epsilon)
        {
            return point.DistanceTo(start);
        }

        var projection = ((point.X - start.X) * dx + (point.Y - start.Y) * dy) / lengthSquared;
        projection = Math.Clamp(projection, 0.0D, 1.0D);
        var closest = new RadarPoint(start.X + projection * dx, start.Y + projection * dy);
        return point.DistanceTo(closest);
    }

    public static bool SegmentsIntersect(RadarPoint a, RadarPoint b, RadarPoint c, RadarPoint d)
    {
        var o1 = Orientation(a, b, c);
        var o2 = Orientation(a, b, d);
        var o3 = Orientation(c, d, a);
        var o4 = Orientation(c, d, b);

        if (o1 != o2 && o3 != o4)
        {
            return true;
        }

        return o1 == 0 && OnSegment(a, c, b) ||
               o2 == 0 && OnSegment(a, d, b) ||
               o3 == 0 && OnSegment(c, a, d) ||
               o4 == 0 && OnSegment(c, b, d);
    }

    private static int Orientation(RadarPoint a, RadarPoint b, RadarPoint c)
    {
        var value = (b.Y - a.Y) * (c.X - b.X) - (b.X - a.X) * (c.Y - b.Y);
        if (Math.Abs(value) <= Epsilon)
        {
            return 0;
        }

        return value > 0.0D ? 1 : 2;
    }

    private static bool OnSegment(RadarPoint a, RadarPoint b, RadarPoint c)
    {
        return b.X <= Math.Max(a.X, c.X) + Epsilon &&
               b.X + Epsilon >= Math.Min(a.X, c.X) &&
               b.Y <= Math.Max(a.Y, c.Y) + Epsilon &&
               b.Y + Epsilon >= Math.Min(a.Y, c.Y);
    }
}
