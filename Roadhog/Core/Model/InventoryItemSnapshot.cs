namespace Roadhog.Core.Model;

public sealed record InventoryItemSnapshot(
    uint TemplateId,
    ulong InstanceId,
    string Name,
    uint Count,
    int Slot,
    bool IsEquipped,
    uint ItemType = 0,
    byte QualityRank = 0);
