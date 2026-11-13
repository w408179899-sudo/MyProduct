namespace Roadhog.Core.Model;

public sealed record InventoryWindowSnapshot(
    bool IsOpen,
    double X,
    double Y,
    double Width,
    double Height,
    ulong DialogAddress,
    ulong VtableAddress,
    DateTimeOffset CapturedAt,
    InventoryWindowRectSource RectSource = InventoryWindowRectSource.LegacyDialogRect,
    ulong RootWidgetAddress = 0,
    ulong RectAddress = 0,
    ulong WidgetFlags = 0,
    string DialogSource = "");

public enum InventoryWindowRectSource
{
    LegacyDialogRect,
    RootWidgetRectExperimental
}
