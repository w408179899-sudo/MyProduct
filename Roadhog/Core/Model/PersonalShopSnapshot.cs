namespace Roadhog.Core.Model;

public sealed record PersonalShopListing(uint InstanceId, uint TemplateId, ulong Quantity, ulong UnitPrice);
public sealed record PersonalShopEditor(uint InstanceId, bool UnitPriceMode, ulong UnitPrice, ulong TotalPrice,
    ulong Quantity, GameUiPoint? PriceInput, GameUiPoint? ConfirmButton);
// BagItems are currently interactive cells. Modal price editing or selling makes the bag noninteractive.
public sealed record PersonalShopSnapshot(bool IsOpen, bool IsSelling, bool InventoryOpen,
    IReadOnlyList<InventoryUiItem> BagItems, uint HoveredInstanceId,
    IReadOnlyList<PersonalShopListing> Listings, PersonalShopEditor? Editor,
    GameUiPoint? StartButton);
