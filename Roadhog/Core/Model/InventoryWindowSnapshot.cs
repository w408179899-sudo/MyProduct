namespace Roadhog.Core.Model;

public sealed record InventoryWindowSnapshot(
    bool IsOpen,
    double X,
    double Y,
    double Width,
    double Height,
    ulong DialogAddress,
    ulong VtableAddress,
    DateTimeOffset CapturedAt)
{
    public bool IsAtTopLeft(double tolerance = 1.0)
    {
        return Math.Abs(X) <= tolerance && Math.Abs(Y) <= tolerance;
    }
}
