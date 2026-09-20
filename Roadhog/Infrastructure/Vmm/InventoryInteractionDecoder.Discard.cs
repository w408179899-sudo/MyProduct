using Roadhog.Core.Model;

namespace Roadhog.Infrastructure.Vmm;

internal sealed partial class InventoryInteractionDecoder
{
    public InventoryInteractionSnapshot ReadInventory(ulong gameBase)
    {
        _guards.Clear();
        Viewport(gameBase);
        // Slots 0..9 belong to pre-world screens. In-world dialogs start at 10;
        // the known ordinary/special discard pools end at 365 in this client.
        const int first = 10, last = 365;
        var table = Guard(gameBase + 0xD63990 + first * 8, (last - first + 1) * 8);
        ulong Root(int id) => BitConverter.ToUInt64(table, (id - first) * 8);
        var addresses = Enumerable.Range(first, last - first + 1).Select(Root).Where(a => a != 0).Distinct().ToArray();
        var flags = addresses.Select(a => (Address: a + 0x28, Size: 8)).ToArray();
        var values = Batch(flags);
        for (var i = 0; i < flags.Length; i++) _guards.Add(flags[i], values[i]);
        var bag = Root(27); var shop = Root(168);
        var open = bag != 0 && Visible(bag);
        var shopOpen = shop != 0 && Visible(shop);
        var active = shop == 0 ? 0 : BitConverter.ToUInt32(Guard(shop + 0x4D8, 4));
        Require(active <= 1, "Invalid shop state.");
        var pending = bag == 0 ? 0 : BitConverter.ToUInt32(Guard(bag + 0x598, 4));
        InventoryDiscardDialog? dialog = null;
        var otherModal = new[] { 171, 310 }.Any(id => Root(id) != 0 && Visible(Root(id)));
        for (var id = 336; id <= 365; id++)
        {
            var address = Root(id);
            if (address == 0 || !Visible(address)) continue;
            var fields = Guard(address + 0x4D8, 0x34);
            uint F(int offset) => BitConverter.ToUInt32(fields, offset - 0x4D8);
            var normal = id <= 355 && F(0x4D8) == 2049 && F(0x4DC) == 2101 && F(0x4E0) == 2104;
            var special = id >= 356 && F(0x4D8) == 3 && F(0x4DC) == 2105 && F(0x4E0) == 2106;
            if (pending == 0 || (!normal && !special) || (F(0x4E8) != pending && F(0x508) != pending))
            {
                otherModal = true;
                continue;
            }
            Require(dialog == null, "Multiple discard dialogs for one item.");
            var nodes = Nodes(address);
            // UIMsgBox::CreateButtons stores primary/cancel widgets at indices 175/176.
            var confirm = GU(address + 0x578); var cancel = GU(address + 0x580);
            dialog = new(pending, normal ? InventoryDiscardConfirmKind.Normal : InventoryDiscardConfirmKind.Special, id,
                nodes.SingleOrDefault(n => n.Address == confirm)?.Point(this),
                nodes.SingleOrDefault(n => n.Address == cancel)?.Point(this));
        }
        var items = new List<InventoryUiItem>(); uint hover = 0;
        if (open && pending == 0 && !otherModal && !shopOpen && active == 0)
            (items, hover) = ReadBagItems(bag);
        GameUiPoint? drop = null;
        if (open && pending == 0 && !otherModal && !shopOpen && active == 0)
        {
            var rectangles = new List<(double X, double Y, double W, double H)>();
            foreach (var address in addresses.Where(Visible))
            {
                var bytes = Guard(address + 0x58, 32);
                var rect = Enumerable.Range(0, 4).Select(i => Number(BitConverter.ToDouble(bytes, i * 8))).ToArray();
                Require(rect[2] >= 0 && rect[3] >= 0, "Invalid in-world dialog bounds.");
                if (rect[2] > 0 && rect[3] > 0) rectangles.Add((rect[0], rect[1], rect[2], rect[3]));
            }
            foreach (var y in new[] { .5, .4, .6, .3, .7 })
            foreach (var x in new[] { .65, .5, .8, .35, .2 })
            {
                var point = new GameUiPoint((int)(_width * x), (int)(_height * y));
                if (rectangles.All(r => point.X < r.X - 12 || point.X > r.X + r.W + 12 || point.Y < r.Y - 12 || point.Y > r.Y + r.H + 12))
                { drop ??= point; }
            }
        }
        VerifyGuards();
        return new(open, shopOpen, active == 1, items.AsReadOnly(), hover, pending, dialog, drop, otherModal);
    }
}
