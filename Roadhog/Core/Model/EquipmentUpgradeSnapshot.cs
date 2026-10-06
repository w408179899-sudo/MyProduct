namespace Roadhog.Core.Model;

public enum EquipmentUpgradeKind { Enchant, Manastone }

public sealed record EquipmentUpgradeItem(uint InstanceId, uint TemplateId, string Name, ulong Count,
    int Slot, bool IsEquipped, byte EnchantLevel, int SocketCount, IReadOnlyList<ushort> Manastones,
    bool CanEnchant, bool CanSocket, int EnchantStoneLevel = 0, int EquipmentLevel = 0)
{
    public int UsedSockets => Manastones.Count(x => x != 0);
    public bool IsEnchantStone => TemplateId is >= 166000000 and <= 166099999;
    public bool IsManastone => TemplateId is >= 167000000 and <= 167099999;
}

public sealed record EquipmentUpgradeInventory(IReadOnlyList<EquipmentUpgradeItem> Items);
public sealed record EquipmentUpgradeDialog(int DialogId, uint EquipmentId, uint MaterialId,
    EquipmentUpgradeKind Kind, GameUiPoint? ConfirmButton, GameUiPoint? CancelButton, bool FinalConfirmation);
public sealed record EquipmentUpgradeUi(bool BagOpen, IReadOnlyList<InventoryUiItem> Items,
    uint HoveredInstanceId, EquipmentUpgradeDialog? Dialog, bool Busy, bool OtherModalOpen);
