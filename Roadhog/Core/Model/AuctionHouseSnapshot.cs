namespace Roadhog.Core.Model;

public sealed record AuctionMarketRow(uint TemplateId, ulong Quantity, string Name, ulong TotalPrice, ulong UnitPrice);
public sealed record AuctionEditor(uint TemplateId, ulong Quantity, ulong UnitPrice, ulong? MarketMinimum, GameUiPoint? CancelButton)
{
    public uint InstanceId { get; init; }
    public ulong MaximumQuantity { get; init; }
    public ulong MinimumAllowedPrice { get; init; }
    public bool UnitPriceMode { get; init; }
    public GameUiPoint? QuantityInput { get; init; }
    public GameUiPoint? PriceInput { get; init; }
    public GameUiPoint? ConfirmButton { get; init; }
}
public sealed record AuctionListing(uint ListingId, uint TemplateId, ulong Quantity, string Name, ulong TotalPrice, string TimeText, GameUiPoint? Point);
public sealed record AuctionWithdrawConfirmation(uint ListingId, GameUiPoint? ConfirmButton);
public sealed record AuctionHouseSnapshot(bool DialogOpen, GameUiPoint? TradeButton, bool IsOpen, int ActiveTab,
    IReadOnlyDictionary<string, GameUiPoint> Buttons, string SearchName, string ResultMessage,
    IReadOnlyList<AuctionMarketRow> MarketRows, bool SettlementLoaded, ulong SettlementMoney, AuctionEditor? Editor)
{
    public uint BrokerTargetServerObjectId { get; init; }
    public bool ListingsLoaded { get; init; }
    public IReadOnlyList<AuctionListing> Listings { get; init; } = Array.Empty<AuctionListing>();
    public int ListingCapacity { get; init; } = 15;
    public AuctionWithdrawConfirmation? WithdrawConfirmation { get; init; }
    public bool OtherModalOpen { get; init; }
    public uint HoveredListingId { get; init; }
    public GameUiPoint? ListingsScrollPoint { get; init; }
    public double ListingsScrollY { get; init; }
    public GameUiPoint? Button(string name) => Buttons.TryGetValue(name, out var point) ? point : null;
}
