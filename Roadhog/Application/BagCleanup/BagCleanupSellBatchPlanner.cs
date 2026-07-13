using Roadhog.Core.Accounts;
using Roadhog.Core.Model;

namespace Roadhog.Application.BagCleanup;

public enum BagCleanupSellBatchKind
{
    None,
    NonEquipment,
    Equipment
}

public sealed record BagCleanupSellBatch(
    IReadOnlyList<InventoryItemSnapshot> Items,
    BagCleanupSellBatchKind Kind,
    int MaxBatchCount)
{
    public static BagCleanupSellBatch Empty { get; } =
        new(Array.Empty<InventoryItemSnapshot>(), BagCleanupSellBatchKind.None, 0);

    public string KindName => Kind switch
    {
        BagCleanupSellBatchKind.NonEquipment => "non_equipment",
        BagCleanupSellBatchKind.Equipment => "equipment",
        _ => "none"
    };
}

public static class BagCleanupSellBatchPlanner
{
    public static BagCleanupSellBatch SelectNextBatch(IEnumerable<InventoryItemSnapshot> candidates)
    {
        var ordered = candidates.ToArray();
        if (ordered.Length == 0)
        {
            return BagCleanupSellBatch.Empty;
        }

        var nonEquipment = ordered
            .Where(item => !BagCleanupItemMatcher.IsEquipment(item))
            .Take(BagCleanupSeller.NonEquipmentSellRegistrationItemsPerBatch)
            .ToArray();
        if (nonEquipment.Length > 0)
        {
            return new BagCleanupSellBatch(
                nonEquipment,
                BagCleanupSellBatchKind.NonEquipment,
                BagCleanupSeller.NonEquipmentSellRegistrationItemsPerBatch);
        }

        var equipment = ordered
            .Where(BagCleanupItemMatcher.IsEquipment)
            .Take(BagCleanupSeller.EquipmentSellRegistrationItemsPerBatch)
            .ToArray();
        return equipment.Length == 0
            ? BagCleanupSellBatch.Empty
            : new BagCleanupSellBatch(
                equipment,
                BagCleanupSellBatchKind.Equipment,
                BagCleanupSeller.EquipmentSellRegistrationItemsPerBatch);
    }
}
