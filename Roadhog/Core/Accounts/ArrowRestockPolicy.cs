using Roadhog.Core.Model;

namespace Roadhog.Core.Accounts;

public static class ArrowRestockPolicy
{
    // Standard merchant arrow, verified against script 5 inventory and NPC stock.
    public const uint ArrowTemplateId = 163000003;
    public const ulong CombatThreshold = 10_000;
    public const ulong TargetCount = 50_000;
    public const ulong BatchCount = 10_000;

    public static ulong Count(IEnumerable<InventoryItemSnapshot> inventory) =>
        inventory.Where(i => i.TemplateId == ArrowTemplateId).Aggregate(0UL, (sum, i) => checked(sum + i.Count));
    public static int Batches(ulong count) => count >= TargetCount ? 0 : (int)((TargetCount - count + BatchCount - 1) / BatchCount);
    public static bool NeedsRestock(PlayerSnapshot player, IReadOnlyList<InventoryItemSnapshot> inventory, bool duringCleanup) =>
        !player.IsDead && player.CharacterClassId == AionClassId.Ranger && Count(inventory) < (duringCleanup ? TargetCount : CombatThreshold);
}
