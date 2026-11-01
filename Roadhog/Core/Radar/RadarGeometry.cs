namespace Roadhog.Core.Radar;

public static class RadarGeometry
{
    // Numerical tolerance only, not an actor radius or minimum passage width.
    public const double CoordinateTolerance = 1e-9D;

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
        if (lengthSquared <= CoordinateTolerance * CoordinateTolerance)
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

        if (o1 * o2 < 0 && o3 * o4 < 0)
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
        var value = (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
        if (Math.Abs(value) <= CoordinateTolerance * a.DistanceTo(b))
        {
            return 0;
        }

        return value > 0.0D ? 1 : -1;
    }

    private static bool OnSegment(RadarPoint a, RadarPoint b, RadarPoint c)
    {
        return b.X <= Math.Max(a.X, c.X) + CoordinateTolerance &&
               b.X + CoordinateTolerance >= Math.Min(a.X, c.X) &&
               b.Y <= Math.Max(a.Y, c.Y) + CoordinateTolerance &&
               b.Y + CoordinateTolerance >= Math.Min(a.Y, c.Y);
    }

    // direction is a unit vector. Includes touch, overlap, and intersections at the origin.
    public static bool TryRayIntersectionDistance(
        RadarPoint origin, RadarPoint direction, RadarObstacleSegment obstacle, out double distance)
    {
        distance = 0;
        var sx = obstacle.End.X - obstacle.Start.X;
        var sy = obstacle.End.Y - obstacle.Start.Y;
        var qx = obstacle.Start.X - origin.X;
        var qy = obstacle.Start.Y - origin.Y;
        var denominator = direction.X * sy - direction.Y * sx;
        if (Math.Abs(denominator) <= CoordinateTolerance * Math.Max(obstacle.Length, CoordinateTolerance))
        {
            if (Math.Abs(qx * direction.Y - qy * direction.X) > CoordinateTolerance) return false;
            var first = qx * direction.X + qy * direction.Y;
            var last = (obstacle.End.X - origin.X) * direction.X + (obstacle.End.Y - origin.Y) * direction.Y;
            if (Math.Max(first, last) < -CoordinateTolerance) return false;
            distance = Math.Max(0, Math.Min(first, last));
            return true;
        }
        var t = (qx * sy - qy * sx) / denominator;
        var u = (qx * direction.Y - qy * direction.X) / denominator;
        var parameterTolerance = CoordinateTolerance / Math.Max(obstacle.Length, CoordinateTolerance);
        if (t < -CoordinateTolerance || u < -parameterTolerance || u > 1 + parameterTolerance) return false;
        distance = Math.Max(0, t);
        return true;
    }
}
