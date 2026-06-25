namespace Roadhog.Core.Model;

public sealed record InventoryItemSnapshot(
    uint TemplateId,
    ulong InstanceId,
    string Name,
    uint Count,
    int Slot,
    bool IsEquipped);
