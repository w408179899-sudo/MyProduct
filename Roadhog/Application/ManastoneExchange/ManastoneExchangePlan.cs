using Roadhog.Core.Model;

namespace Roadhog.Application.ManastoneExchange;

public sealed record ManastoneExchangeQuest(uint QuestId, string Name, uint RewardId, IReadOnlyList<uint> MaterialIds)
{
    public ulong Available(IReadOnlyList<InventoryItemSnapshot> inventory) =>
        MaterialIds.Min(id => ManastoneExchangePlan.Count(inventory, id, whiteOnly: true)) / 10;
}

public static class ManastoneExchangePlan
{
    // Verified against Biloga's task pages, quest data and inventory deltas on 2026-11-04.
    public static IReadOnlyList<ManastoneExchangeQuest> Quests { get; } = Array.AsReadOnly(new[]
    {
        new ManastoneExchangeQuest(4113, "比洛加的魔石袋", 188051284, Array.AsReadOnly(new uint[] { 167000354, 167000356, 167000361 })),
        new ManastoneExchangeQuest(4114, "比洛加的魔石包袱", 188051283, Array.AsReadOnly(new uint[] { 167000355, 167000357, 167000360 }))
    });

    public static ulong Count(IReadOnlyList<InventoryItemSnapshot> inventory, uint templateId, bool whiteOnly = false) =>
        inventory.Where(i => !i.IsEquipped && i.Slot >= 0 && i.TemplateId == templateId &&
            (!whiteOnly || i is { QualityRank: 1, ItemType: 24 })).Aggregate(0UL, (n, i) => n + i.Count);

    public static bool Confirmed(ManastoneExchangeQuest quest, IReadOnlyList<InventoryItemSnapshot> before,
        IReadOnlyList<InventoryItemSnapshot> after, bool materialsAlreadySubmitted = false) =>
        Count(after, quest.RewardId) == Count(before, quest.RewardId) + 1 &&
        quest.MaterialIds.All(id => materialsAlreadySubmitted
            ? Count(after, id, true) == Count(before, id, true)
            : Count(before, id, true) >= 10 && Count(after, id, true) == Count(before, id, true) - 10);
}

public sealed record ManastoneExchangeResult(int Bags, int Bundles, bool MaterialsExhausted, string Message);
