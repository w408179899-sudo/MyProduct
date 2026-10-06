using Roadhog.Application.SemiAuto;
using Roadhog.Core.Accounts;
using Roadhog.Core.Model;

namespace Roadhog.Application.Food;

public sealed record FoodQuickbarBinding(int Page, QuickbarSlotSnapshot Slot,
    InventoryItemSnapshot Item, string Key);

public static class FoodQuickbarBindings
{
    public static IReadOnlyList<FoodQuickbarBinding> Candidates(QuickbarSnapshot quickbar,
        IReadOnlyList<InventoryItemSnapshot> inventory, FoodCatalog catalog, FoodKind kind, int playerLevel)
    {
        var result = new List<FoodQuickbarBinding>();
        foreach (var slot in quickbar.Slots.OrderBy(s => s.Bar).ThenBy(s => s.Slot))
        {
            var key = SkillKeyBindings.GetSlotKey(slot.Bar, slot.Slot);
            if (slot.ContentType != 1 || slot.ItemTemplateId == 0 || slot.ItemInstanceId == 0 || key == null) continue;
            var item = catalog.Select(inventory.Where(i => i.TemplateId == slot.ItemTemplateId &&
                i.InstanceId == slot.ItemInstanceId), kind, playerLevel);
            if (item != null) result.Add(new(quickbar.Page, slot, item, key));
        }
        return result;
    }

    public static FoodQuickbarBinding? Select(QuickbarSnapshot quickbar,
        IReadOnlyList<InventoryItemSnapshot> inventory, FoodCatalog catalog, FoodKind kind, int playerLevel,
        IReadOnlyList<FoodQuickbarPreference> preferences)
    {
        var selected = preferences.Take(1).Select(p => p.TemplateId).ToHashSet();
        var candidates = Candidates(quickbar, inventory, catalog, kind, playerLevel)
            .Where(c => selected.Contains(c.Item.TemplateId)).ToArray();
        var item = catalog.Select(candidates.Select(c => c.Item), kind, playerLevel);
        return item == null ? null : candidates.First(c => c.Item == item);
    }
}
