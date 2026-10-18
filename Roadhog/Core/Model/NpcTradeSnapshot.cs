namespace Roadhog.Core.Model;

public sealed record NpcTradeItem(uint InstanceId, uint TemplateId, ulong Quantity);

public sealed record NpcShopItem(uint ItemId, uint TemplateId, string Name, uint ItemType,
    ulong UnitPrice, GameUiPoint? Point);
public sealed record NpcBuyQuantity(uint ItemId, ulong Quantity, ulong Maximum,
    GameUiPoint? AllButton, GameUiPoint? ConfirmButton);
public sealed record NpcPurchaseSnapshot(IReadOnlyList<NpcShopItem> Items, IReadOnlyList<NpcTradeItem> Basket,
    uint? HoveredItemId, NpcBuyQuantity? QuantityDialog, GameUiPoint? BuyButton);

public sealed record NpcTradeSnapshot(bool DialogOpen, IReadOnlyDictionary<string, GameUiPoint> DialogEntries,
    bool IsOpen, int Mode, uint NpcServerObjectId, bool InventoryOpen,
    IReadOnlyList<NpcTradeItem> Basket, GameUiPoint? SellButton, bool OtherModalOpen,
    GameUiPoint? RegisterAllJunkButton = null)
{
    public bool IsSelling => IsOpen && Mode == 1;
    public bool IsBuying => IsOpen && Mode == 0;
    public NpcPurchaseSnapshot? Purchase { get; init; }
    public GameUiPoint? BuyEntry => DialogEntries.TryGetValue("购买道具", out var point) ||
        DialogEntries.TryGetValue("购买物品", out point) ? point : null;
    public GameUiPoint? SellEntry => DialogEntries.TryGetValue("出售道具", out var point) ||
        DialogEntries.TryGetValue("出售物品", out point) ? point : null;
}
