using Roadhog.Core.Model;

namespace Roadhog.Core.Accounts;

public enum GroceryNpcSaleStage { DiscardEquipment, DiscardOther, Sell }

public static class GroceryNpcSalePolicy
{
    public static IReadOnlyList<InventoryItemSnapshot> SelectCandidates(IEnumerable<InventoryItemSnapshot> inventory,
        MaintenanceScriptSettings settings) => BagCleanupItemMatcher.SelectDiscardItems(inventory, settings)
            .Where(i => i.CanSellToNpc).ToArray();

    public static IReadOnlyList<InventoryItemSnapshot> SelectCandidates(IEnumerable<InventoryItemSnapshot> inventory,
        MaintenanceScriptSettings settings, GroceryNpcSaleStage stage)
    {
        var candidates = stage == GroceryNpcSaleStage.Sell
            ? BagCleanupItemMatcher.SelectSellRegistrationItems(inventory, settings)
            : BagCleanupItemMatcher.SelectDiscardItems(inventory, settings);
        return candidates.Where(i => i.Count > 0 && i.CanSellToNpc && (stage == GroceryNpcSaleStage.Sell ||
            BagCleanupItemMatcher.IsEquipment(i) == (stage == GroceryNpcSaleStage.DiscardEquipment))).ToArray();
    }

    // Completion concerns both configured sets, including items the NPC cannot buy.
    public static IReadOnlyList<InventoryItemSnapshot> SelectPendingItems(IEnumerable<InventoryItemSnapshot> inventory,
        MaintenanceScriptSettings settings)
    {
        var bag = inventory.ToArray();
        return BagCleanupItemMatcher.SelectDiscardItems(bag, settings)
            .Concat(BagCleanupItemMatcher.SelectSellRegistrationItems(bag, settings))
            .Where(i => i.Count > 0).DistinctBy(i => i.InstanceId).ToArray();
    }
}
