using Roadhog.Core.Model;

namespace Roadhog.Infrastructure.Vmm;

internal enum InventoryReadCompleteness
{
    Failed = 0,
    Partial = 1,
    Complete = 2
}

internal sealed record InventoryItemFieldValidity(
    bool TemplateId,
    bool Count,
    bool Name,
    bool Slot,
    bool IsEquipped,
    bool ItemType,
    bool QualityRank,
    bool VendorSellUnitPrice);

internal sealed record InventoryItemObservation(
    InventoryItemSnapshot Snapshot,
    InventoryItemFieldValidity Fields);

internal sealed record InventoryReadResult(
    InventoryReadCompleteness Completeness,
    IReadOnlyList<InventoryItemObservation> Observations,
    string Error)
{
    public IReadOnlyList<InventoryItemSnapshot> Items => Observations
        .Select(static observation => observation.Snapshot)
        .Where(static item => item.Slot >= 0)
        .OrderBy(static item => item.Slot)
        .ThenBy(static item => item.TemplateId)
        .ThenBy(static item => item.InstanceId)
        .ToArray();
}
