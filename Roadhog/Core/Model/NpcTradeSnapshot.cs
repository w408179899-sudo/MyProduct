namespace Roadhog.Core.Model;

public sealed record NpcTradeItem(uint InstanceId, uint TemplateId, ulong Quantity);

public sealed record NpcTradeSnapshot(bool DialogOpen, IReadOnlyDictionary<string, GameUiPoint> DialogEntries,
    bool IsOpen, int Mode, uint NpcServerObjectId, bool InventoryOpen,
    IReadOnlyList<NpcTradeItem> Basket, GameUiPoint? SellButton, bool OtherModalOpen)
{
    public bool IsSelling => IsOpen && Mode == 1;
    public GameUiPoint? SellEntry => DialogEntries.TryGetValue("出售道具", out var point) ||
        DialogEntries.TryGetValue("出售物品", out point) ? point : null;
}
