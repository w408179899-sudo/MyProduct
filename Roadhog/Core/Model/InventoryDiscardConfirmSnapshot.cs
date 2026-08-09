namespace Roadhog.Core.Model;

public enum InventoryDiscardConfirmKind
{
    None,
    Normal,
    Special,
    PendingWithoutVisibleDialog
}

public sealed record InventoryDiscardConfirmSnapshot(
    bool IsOpen,
    uint PendingItemInstanceId,
    InventoryDiscardConfirmKind Kind,
    int DialogId,
    ulong DialogAddress,
    DateTimeOffset CapturedAt)
{
    public static InventoryDiscardConfirmSnapshot Closed(DateTimeOffset capturedAt)
    {
        return new InventoryDiscardConfirmSnapshot(
            false,
            0,
            InventoryDiscardConfirmKind.None,
            -1,
            0,
            capturedAt);
    }
}
