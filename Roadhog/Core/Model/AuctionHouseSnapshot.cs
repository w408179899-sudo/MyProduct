namespace Roadhog.Core.Model;

public sealed record AuctionMarketRow(uint TemplateId, ulong Quantity, string Name, ulong TotalPrice, ulong UnitPrice);
public sealed record AuctionEditor(uint TemplateId, ulong Quantity, ulong UnitPrice, ulong? MarketMinimum, GameUiPoint? CancelButton);
public sealed record AuctionHouseSnapshot(bool DialogOpen, GameUiPoint? TradeButton, bool IsOpen, int ActiveTab,
    IReadOnlyDictionary<string, GameUiPoint> Buttons, string SearchName, string ResultMessage,
    IReadOnlyList<AuctionMarketRow> MarketRows, bool SettlementLoaded, ulong SettlementMoney, AuctionEditor? Editor)
{
    public uint BrokerTargetServerObjectId { get; init; }
    public GameUiPoint? Button(string name) => Buttons.TryGetValue(name, out var point) ? point : null;
}
