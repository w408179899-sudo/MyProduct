namespace Roadhog.Core.Model;

public sealed record PersonalShopListing(uint InstanceId, uint TemplateId, ulong Quantity, ulong UnitPrice);
public sealed record PersonalShopEditor(uint InstanceId, bool UnitPriceMode, ulong UnitPrice, ulong TotalPrice,
    ulong Quantity, GameUiPoint? PriceInput, GameUiPoint? ConfirmButton)
{
    public GameUiPoint? QuantityInput { get; init; }
}
// BagItems are currently interactive cells. Modal price editing or selling makes the bag noninteractive.
public sealed record PersonalShopSnapshot(bool IsOpen, bool IsSelling, bool InventoryOpen,
    IReadOnlyList<InventoryUiItem> BagItems, uint HoveredInstanceId,
    IReadOnlyList<PersonalShopListing> Listings, PersonalShopEditor? Editor,
    GameUiPoint? StartButton)
{
    public GameUiPoint? StopButton { get; init; }
    public ShopPurchaseSnapshot Purchase { get; init; } = ShopPurchaseSnapshot.Closed;
    public bool OtherModalOpen { get; init; }
}

// The remote purchase list uses shop-local item keys; zero is valid for its first item.
public sealed record ShopPurchaseItem(uint InstanceId, uint TemplateId, ulong Quantity, ulong UnitPrice, GameUiPoint? Point);
public sealed record ShopPurchaseQuantity(uint InstanceId, ulong Quantity, ulong Maximum, GameUiPoint? Input, GameUiPoint? Confirm);
public sealed record ShopPurchaseSnapshot(bool IsOpen, uint SellerObjectId, IReadOnlyList<ShopPurchaseItem> Items,
    IReadOnlyList<ShopPurchaseItem> Basket, uint? HoveredInstanceId, ShopPurchaseQuantity? QuantityDialog, GameUiPoint? BuyButton)
{
    public uint? HoveredBasketInstanceId { get; init; }
    public static ShopPurchaseSnapshot Closed { get; } = new(false, 0, Array.Empty<ShopPurchaseItem>(), Array.Empty<ShopPurchaseItem>(), null, null, null);
}
