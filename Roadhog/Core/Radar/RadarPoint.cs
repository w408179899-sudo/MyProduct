namespace Roadhog.Core.Radar;

public readonly record struct RadarPoint(double X, double Y)
{
    public double DistanceTo(RadarPoint other)
    {
        var dx = other.X - X;
        var dy = other.Y - Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }
}
