using System.Text;
using Roadhog.Core.Model;

namespace Roadhog.Infrastructure.Vmm;

internal sealed partial class InventoryInteractionDecoder
{
    // Local read-only adapter, documented in docs/equipment-upgrade.md.
    public EquipmentUpgradeInventory ReadEquipmentInventory(ulong gameBase, Func<uint, int> baseSockets, out bool complete,
        Func<uint, int>? templateLevel = null)
    {
        _guards.Clear();
        var manager = GU(gameBase + 0xD4B010);
        Require(manager != 0, "Missing inventory manager.");
        var head = GU(manager + 0x780); var count = GU(manager + 0x788);
        Require(head != 0 && count <= 512, "Invalid inventory tree.");
        var equipped = Guard(manager + 0x790, 128);
        var ids = Enumerable.Range(0, 32).Select(i => BitConverter.ToUInt32(equipped, i * 4)).ToHashSet();
        var seen = new HashSet<ulong>(); var items = new List<EquipmentUpgradeItem>();
        var allItems = true;
        void Walk(ulong node, int depth)
        {
            if (node == head) return;
            Require(node != 0 && depth < 32 && seen.Count < 512 && seen.Add(node), "Invalid inventory links.");
            var link = Guard(node, 48);
            Require(link[25] == 0, "Unexpected tree sentinel.");
            Walk(BitConverter.ToUInt64(link, 0), depth + 1);
            try
            {
                var decoder = new InventoryInteractionDecoder(read, readMany);
                var item = decoder.ReadUpgradeItem(BitConverter.ToUInt64(link, 40), BitConverter.ToUInt32(link, 32), ids, baseSockets, templateLevel);
                if (item != null) items.Add(item);
            }
            catch (InvalidDataException) { allItems = false; }
            Walk(BitConverter.ToUInt64(link, 16), depth + 1);
        }
        try { Walk(GU(head + 8), 0); }
        catch (InvalidDataException) { allItems = false; }
        Require(items.Select(i => i.InstanceId).Distinct().Count() == items.Count, "Duplicate inventory identities.");
        VerifyGuards();
        complete = allItems && (ulong)seen.Count == count;
        return new(items.AsReadOnly());
    }

    private EquipmentUpgradeItem? ReadUpgradeItem(ulong item, uint expectedId, HashSet<uint> ids, Func<uint, int> baseSockets, Func<uint, int>? templateLevel)
    {
        Require(item != 0, "Missing inventory item.");
        var identity = Guard(item + 8, 16);
        var id = BitConverter.ToUInt32(identity); var template = BitConverter.ToUInt32(identity, 4);
        Require(id != 0 && id == expectedId && template != 0, "Mixed inventory identity.");
        var equipment = IsUpgradeEquipment(template);
        if (equipment || template is >= 166000000 and <= 167099999)
        {
            var quantity = BitConverter.ToUInt64(identity, 8);
            var slot = BitConverter.ToInt16(Guard(item + 0x4F6, 2));
            var nameData = Guard(item + 24, 32);
            var length = BitConverter.ToUInt64(nameData, 16); var capacity = BitConverter.ToUInt64(nameData, 24);
            Require(quantity > 0 && length <= 256 && capacity >= length && capacity <= 4096, "Invalid item fields.");
            var name = Encoding.Unicode.GetString(capacity < 8 ? nameData.AsSpan(0, (int)length * 2)
                : Guard(BitConverter.ToUInt64(nameData), (int)length * 2));
            byte enchant = 0; var total = 0; ushort[] stones = Array.Empty<ushort>();
            var canEnchant = false; var canSocket = false;
            if (equipment)
            {
                var flags = BitConverter.ToUInt32(Guard(item + 120, 4));
                var restricted = BitConverter.ToUInt32(Guard(item + 56, 4));
                var values = Guard(item + 169, 18);
                enchant = values[0]; total = checked(baseSockets(template) + values[5]);
                Require(enchant <= 30 && total is >= 0 and <= 6, "Invalid equipment upgrade fields.");
                stones = Enumerable.Range(0, total).Select(i => BitConverter.ToUInt16(values, 6 + i * 2)).ToArray();
                canEnchant = restricted == 0 && (flags & 0x10500) == 0;
                canSocket = restricted == 0 && (flags & 0x20500) == 0;
            }
            var level = 0;
            if (equipment || template is >= 166000000 and <= 166099999)
            {
                level = templateLevel?.Invoke(template) ?? throw new InvalidDataException("Missing item level metadata.");
                Require(level is > 0 and <= 255, "Invalid item level.");
            }
            VerifyGuards();
            return new(id, template, name, quantity, slot, slot < 0 || ids.Contains(id), enchant,
                total, Array.AsReadOnly(stones), canEnchant, canSocket && total > 0, equipment ? 0 : level, equipment ? level : 0);
        }
        return null;
    }

    internal static bool IsUpgradeEquipment(uint id) => id is >= 100000000 and <= 103099999
        or >= 110000000 and <= 115099999;

    public EquipmentUpgradeUi ReadEquipmentUi(ulong gameBase)
    {
        _guards.Clear(); Viewport(gameBase);
        ulong Root(int id) => GU(gameBase + 0xD63990 + (ulong)id * 8);
        bool Open(int id) { var a = Root(id); return a != 0 && Visible(a); }
        var other = new[] { 152, 168, 171, 310 }.Any(Open);
        var busy = Open(279);
        EquipmentUpgradeDialog? dialog = null;
        foreach (var id in new[] { 249, 250 })
        {
            var root = Root(id); if (root == 0 || !Visible(root)) continue;
            Require(dialog == null, "Multiple equipment editors.");
            var target = GU(root + 1320); var material = GU(root + 1336);
            Require(target != 0 && material != 0, "Missing equipment editor slots.");
            var equipmentId = BitConverter.ToUInt32(Guard(target + 952, 4));
            var materialId = BitConverter.ToUInt32(Guard(material + 952, 4));
            Require(equipmentId != 0 && materialId != 0, "Unbound equipment editor.");
            var nodes = Nodes(root);
            dialog = new(id, equipmentId, materialId, id == 249 ? EquipmentUpgradeKind.Enchant : EquipmentUpgradeKind.Manastone,
                nodes.SingleOrDefault(n => n.Address == GU(root + 1248))?.Point(this),
                nodes.FirstOrDefault(n => n.Name == "cancel")?.Point(this), false);
        }
        for (var id = 336; id <= 365; id++)
        {
            var modal = Root(id); if (modal == 0 || !Visible(modal)) continue;
            var fields = Guard(modal + 1240, 12);
            var ownerId = BitConverter.ToInt32(Guard(modal + 968, 4));
            if (dialog == null || dialog.FinalConfirmation || ownerId != dialog.DialogId ||
                (BitConverter.ToUInt32(fields) & ~8u) != 1 || BitConverter.ToUInt32(fields, 4) != 2101 ||
                BitConverter.ToUInt32(fields, 8) != 2102) { other = true; continue; }
            var nodes = Nodes(modal);
            dialog = dialog with { DialogId = id, FinalConfirmation = true,
                ConfirmButton = nodes.SingleOrDefault(n => n.Address == GU(modal + 1400))?.Point(this),
                CancelButton = nodes.SingleOrDefault(n => n.Address == GU(modal + 1408))?.Point(this) };
        }
        var bagOpen = Open(27); var items = new List<InventoryUiItem>(); uint hover = 0;
        if (bagOpen && !other && !busy && dialog == null) (items, hover) = ReadBagItems(Root(27));
        if (other && dialog != null) dialog = dialog with { ConfirmButton = null, CancelButton = null };
        VerifyGuards();
        return new(bagOpen, items.AsReadOnly(), hover, dialog, busy, other);
    }
}
