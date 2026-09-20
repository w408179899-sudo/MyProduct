namespace Roadhog.Core.Model;

public sealed record GameUiPoint(int X, int Y);
public sealed record GameUiCursorSnapshot(int Width, int Height, GameUiPoint Position);
public sealed record InventoryUiItem(uint InstanceId, uint TemplateId, ulong Quantity, GameUiPoint Point);
public sealed record InventoryDiscardDialog(uint InstanceId, InventoryDiscardConfirmKind Kind,
    int DialogId, GameUiPoint? ConfirmButton, GameUiPoint? CancelButton);
public sealed record InventoryInteractionSnapshot(bool IsOpen, bool ShopIsOpen, bool IsSelling,
    IReadOnlyList<InventoryUiItem> Items, uint HoveredInstanceId, uint PendingDiscardInstanceId,
    InventoryDiscardDialog? DiscardDialog, GameUiPoint? DropPoint, bool OtherModalOpen);
