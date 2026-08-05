namespace Roadhog.Core.Model;

public sealed record InventoryItemSnapshot(
    uint TemplateId,
    ulong InstanceId,
    string Name,
    uint Count,
    int Slot,
    bool IsEquipped,
    uint ItemType = 0,
    byte QualityRank = 0,
    ulong VendorSellUnitPrice = 0)
{
    public ulong VendorSellStackTotal =>
        Count == 0 || VendorSellUnitPrice == 0
            ? 0
            : VendorSellUnitPrice > ulong.MaxValue / Count
                ? ulong.MaxValue
                : VendorSellUnitPrice * Count;
}
