using Roadhog.Core.Model;

namespace Roadhog.Infrastructure.Vmm;

internal sealed partial class InventoryInteractionDecoder
{
    // Read-only layouts from aion202609091125.json: 2CC800, 2D1B20, 2D3650.
    // All pointers, identities and quantities participate in the same guarded publication.
    private ShopPurchaseSnapshot ReadPurchase(ulong gameBase)
    {
        var window = GU(gameBase + 0xD63990 + 134 * 8);
        if (window == 0 || !Visible(window)) return ShopPurchaseSnapshot.Closed;
        var mode = BitConverter.ToInt32(Guard(window + 1252, 4));
        if (mode != 3) return ShopPurchaseSnapshot.Closed;
        var nodes = Nodes(window);
        Require(nodes.Any(n => n.Name == "ps_shop_buy_container"), "Unexpected purchase window.");
        var seller = BitConverter.ToUInt32(Guard(window + 1240, 4));
        Require(seller != 0, "Missing shop owner.");
        var head = GU(GU(gameBase + 0xD4B010) + 2368);
        uint hover = 0;
        List<ShopPurchaseItem> Items(ulong list, bool interactive)
        {
            var node = nodes.SingleOrDefault(n => n.Address == list) ?? throw new InvalidDataException("Missing purchase list.");
            var begin = GU(list + 0x368); var count = Count(begin, GU(list + 0x370), 135);
            var hovered = BitConverter.ToInt16(Guard(list + 0x3F0, 2));
            var result = new List<ShopPurchaseItem>();
            for (var i = 0; i < count; i++)
            {
                var slot = GU(begin + (ulong)i * 8); if (slot == 0) continue;
                var template = BitConverter.ToUInt32(Guard(slot + 168, 4)); if (template == 0) continue;
                var id = BitConverter.ToUInt32(Guard(slot + 160, 4)); var qty = GU(slot + 176);
                var record = MapRecord(head, id);
                Require(id != 0 && qty > 0 && BitConverter.ToUInt32(Guard(record + 8, 4)) == id && BitConverter.ToUInt32(Guard(record + 12, 4)) == template,
                    "Purchase item identity mismatch.");
                var price = GU(record + 128);
                result.Add(new(id, template, qty, price, interactive ? ListPoint(node, i) : null));
                if (interactive && i == hovered) hover = id;
            }
            Require(result.Select(i => i.InstanceId).Distinct().Count() == result.Count, "Duplicate purchase items.");
            return result;
        }
        var items = Items(GU(window + 1288), true);
        var basket = Items(GU(window + 1344), false);
        ShopPurchaseQuantity? editor = null;
        var modal = GU(window + 2248);
        if (modal != 0 && Visible(modal))
        {
            var id = BitConverter.ToUInt32(Guard(window + 2236, 4));
            Require(items.Any(i => i.InstanceId == id), "Unbound purchase quantity dialog.");
            var controls = Nodes(modal); var maximum = GU(modal + 1520); var quantity = GU(modal + 1504);
            Require(maximum > 0 && quantity <= maximum, "Invalid purchase quantity.");
            editor = new(id, quantity, maximum, controls.SingleOrDefault(n => n.Address == GU(modal + 1544))?.Point(this),
                controls.SingleOrDefault(n => n.Name == "ok")?.Point(this));
        }
        return new(true, seller, items.AsReadOnly(), basket.AsReadOnly(), hover, editor,
            editor == null ? nodes.SingleOrDefault(n => n.Name == "ok")?.Point(this) : null);
    }

    private ulong MapRecord(ulong head, uint id)
    {
        var node = GU(head + 8);
        for (var count = 0; node != head && Guard(node + 25, 1)[0] == 0; count++)
        {
            Require(count < 512, "Invalid item map.");
            var key = BitConverter.ToUInt32(Guard(node + 32, 4));
            if (key == id) return GU(node + 40);
            node = GU(node + (key > id ? 0UL : 16UL));
        }
        throw new InvalidDataException("Missing item record.");
    }

    private GameUiPoint? ListPoint(Node grid, int index)
    {
        var layout = Guard(grid.Address + 0x2E0, 0x70);
        double G(int off) => Number(BitConverter.ToDouble(layout, off - 0x2E0));
        var w = G(0x2F8); var h = G(0x2F0);
        var mode = BitConverter.ToUInt32(layout, 0);
        Require(mode <= 1 && h > 0, "Invalid trading layout.");
        double x, y;
        var top = grid.Y + G(0x330);
        var left = grid.X + G(0x328);
        if (mode == 1)
        {
            var columns = BitConverter.ToUInt32(layout, 8);
            Require(columns is >= 1 and <= 20 && w > 0, "Invalid trading grid.");
            x = left + (index < columns ? G(0x340) : 0) + index % columns * (w + G(0x308)) + w / 2;
            y = top + G(0x338) + index / columns * (h + G(0x310)) + h / 2;
        }
        else
        {
            // sub_1805E62D0 list mode and sub_1805DA1E0 header bounds.
            var page = Guard(grid.Address + 1316, 12);
            var perPage = BitConverter.ToInt32(page, 0); var pageIndex = BitConverter.ToInt32(page, 4);
            var columns = Math.Max(1, BitConverter.ToInt32(page, 8));
            Require(perPage is >= 0 and <= 512 && pageIndex is >= 0 and <= 512 && columns <= 20, "Invalid trading page.");
            var local = index - perPage * pageIndex;
            if (local < 0 || perPage > 0 && local >= perPage) return null;
            if (GU(grid.Address + 896) != GU(grid.Address + 904)) top += G(0x300);
            var columnWidth = (grid.W - 2 * G(0x328)) / columns;
            Require(columnWidth > 32, "Trading column too narrow.");
            // Keep inside the item icon; hover identity is checked before every action.
            x = left + G(0x340) + local % columns * columnWidth + Math.Min(16, columnWidth / 2);
            y = top + G(0x338) + local / columns * (h + G(0x310)) + h / 2;
        }
        if (x < left || y < top || x >= grid.X + grid.W || y >= grid.Y + grid.H - G(0x330)) return null;
        return Point(x, y);
    }
}
