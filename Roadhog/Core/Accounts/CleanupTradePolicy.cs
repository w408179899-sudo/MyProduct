using Roadhog.Core.Model;

namespace Roadhog.Core.Accounts;

public static class CleanupTradePolicy
{
    public static bool Matches(string name, string keyword) => !string.IsNullOrWhiteSpace(keyword) &&
        name.Contains(keyword.Trim(), StringComparison.OrdinalIgnoreCase);
    public static bool Protected(InventoryItemSnapshot item, MaintenanceScriptSettings settings) =>
        settings.BagCleanupExcludedItemNames.Any(k => Matches(item.Name, k));
    public static bool Reserved(InventoryItemSnapshot item, MaintenanceScriptSettings settings) =>
        settings.BagCleanupAuctionHouseItems.Concat(settings.BagCleanupStallItems).Any(r => Matches(item.Name, r.Name));
    public static BagCleanupTradeItemConfig? Rule(InventoryItemSnapshot item, MaintenanceScriptSettings settings, bool auction)
    {
        if (item.IsEquipped || item.Slot < 0 || item.Count == 0 || Protected(item, settings)) return null;
        var auctionRule = settings.BagCleanupAuctionHouseItems.FirstOrDefault(r => Matches(item.Name, r.Name));
        return auction ? auctionRule : auctionRule != null ? null : settings.BagCleanupStallItems.FirstOrDefault(r => Matches(item.Name, r.Name));
    }
    public static ulong PurchaseQuantity(ulong gold, ulong unitPrice, ulong stock, ulong modalMaximum) =>
        unitPrice == 0 ? 0 : Math.Min(gold / unitPrice, Math.Min(stock, modalMaximum));
}
