using Roadhog.Core.Model;

namespace Roadhog.Core.Accounts;

public static class CleanupTradePolicy
{
    public static bool Matches(string name, string keyword) => !string.IsNullOrWhiteSpace(keyword) &&
        name.Contains(keyword.Trim(), StringComparison.OrdinalIgnoreCase);
    public static BagCleanupTradeItemConfig? Rule(InventoryItemSnapshot item, MaintenanceScriptSettings settings, bool auction)
    {
        if (item.IsEquipped || item.Slot < 0 || item.Count == 0) return null;
        var auctionRule = settings.BagCleanupAuctionHouseItems.FirstOrDefault(r => Matches(item.Name, r.Name));
        // The workflow passes the fresh remaining bag to each stage. A previous-stage
        // rule must not reserve an unsold item away from the later stage.
        return auction ? auctionRule : settings.BagCleanupStallItems.FirstOrDefault(r => Matches(item.Name, r.Name));
    }
    public static ulong PurchaseQuantity(ulong gold, ulong unitPrice, ulong stock, ulong modalMaximum) =>
        unitPrice == 0 ? 0 : Math.Min(gold / unitPrice, Math.Min(stock, modalMaximum));
}
