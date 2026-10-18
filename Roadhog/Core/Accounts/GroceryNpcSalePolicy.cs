using Roadhog.Core.Model;

namespace Roadhog.Core.Accounts;

public static class GroceryNpcSalePolicy
{
    public static IReadOnlyList<InventoryItemSnapshot> SelectCandidates(IEnumerable<InventoryItemSnapshot> inventory,
        MaintenanceScriptSettings settings) => BagCleanupItemMatcher.SelectDiscardItems(inventory, settings)
            .Where(i => i.CanSellToNpc).ToArray();
}
