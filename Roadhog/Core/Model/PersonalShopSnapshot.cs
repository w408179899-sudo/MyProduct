namespace Roadhog.Core.Model;

public sealed record PersonalShopPoint(int X, int Y);
public sealed record PersonalShopCursorSnapshot(int Width, int Height, PersonalShopPoint Position);
public sealed record PersonalShopBagItem(uint InstanceId, uint TemplateId, ulong Quantity, PersonalShopPoint Point);
public sealed record PersonalShopListing(uint InstanceId, uint TemplateId, ulong Quantity, ulong UnitPrice);
public sealed record PersonalShopEditor(uint InstanceId, bool UnitPriceMode, ulong UnitPrice, ulong TotalPrice,
    ulong Quantity, PersonalShopPoint? PriceInput, PersonalShopPoint? ConfirmButton);
// BagItems are currently interactive cells. Modal price editing or selling makes the bag noninteractive.
public sealed record PersonalShopSnapshot(bool IsOpen, bool IsSelling, bool InventoryOpen,
    IReadOnlyList<PersonalShopBagItem> BagItems, uint HoveredInstanceId,
    IReadOnlyList<PersonalShopListing> Listings, PersonalShopEditor? Editor,
    PersonalShopPoint? StartButton);
