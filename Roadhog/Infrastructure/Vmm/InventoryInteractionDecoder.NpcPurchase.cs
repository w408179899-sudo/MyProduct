using Roadhog.Core.Model;

namespace Roadhog.Infrastructure.Vmm;

internal sealed partial class InventoryInteractionDecoder
{
    // aion202609091125.json: DlgTrade modes/layout 2CC800, price 05D380,
    // Shift registration 2D34E0, quantity dialog 2D3650 and ALL handler 2D6B70.
    private NpcPurchaseSnapshot ReadNpcPurchase(ulong gameBase, ulong window, bool bagOpen, out ulong quantityWindow)
    {
        var nodes = Nodes(window);
        Require(nodes.Any(n => n.Name == "buy_item_container"), "Unexpected NPC purchase window.");
        var head = GU(GU(gameBase + 0xD4B010) + 2368);
        var stock = GU(window + 1264); var basketList = GU(window + 1320);
        var stockNode = nodes.SingleOrDefault(n => n.Address == stock) ?? throw new InvalidDataException("Missing NPC stock list.");
        uint? hoveredId = null;
        var hoveredIndex = BitConverter.ToInt16(Guard(stock + 0x3F0, 2));
        var items = new List<NpcShopItem>();
        var basket = new List<NpcTradeItem>();
        foreach (var (list, isStock) in new[] { (stock, true), (basketList, false) })
        {
            var begin = GU(list + 0x368); var count = Count(begin, GU(list + 0x370), 512);
            for (var i = 0; i < count; i++)
            {
                var row = GU(begin + (ulong)i * 8); if (row == 0) continue;
                var data = Guard(row + 160, 24);
                var id = BitConverter.ToUInt32(data); var template = BitConverter.ToUInt32(data, 8);
                if (template == 0) continue;
                var record = MapRecord(head, id);
                Require(BitConverter.ToUInt32(Guard(record + 8, 4)) == id && BitConverter.ToUInt32(Guard(record + 12, 4)) == template,
                    "NPC purchase item identity changed.");
                if (isStock)
                {
                    var name = AuctionWide(record + 24);
                    var type = BitConverter.ToUInt32(Guard(record + 96, 4));
                    Require(name.Length is > 0 and <= 256, "Invalid NPC item name.");
                    items.Add(new(id, template, name, type, NpcPurchaseUnitPrice(gameBase, GU(record + 128)), bagOpen ? null : ListPoint(stockNode, i)));
                    if (i == hoveredIndex) hoveredId = id;
                }
                else
                {
                    var quantity = BitConverter.ToUInt64(data, 16);
                    Require(quantity > 0, "Invalid NPC purchase basket quantity.");
                    basket.Add(new(id, template, quantity));
                }
            }
        }
        Require(items.Select(i => i.ItemId).Distinct().Count() == items.Count, "Duplicate NPC stock identity.");
        NpcBuyQuantity? quantityDialog = null;
        quantityWindow = GU(window + 2248);
        if (quantityWindow != 0 && Visible(quantityWindow))
        {
            var id = BitConverter.ToUInt32(Guard(window + 2236, 4));
            Require(items.Any(i => i.ItemId == id), "Unbound NPC purchase quantity dialog.");
            var controls = Nodes(quantityWindow);
            var quantity = GU(quantityWindow + 1504); var maximum = GU(quantityWindow + 1520);
            Require(maximum > 0 && quantity <= maximum, "Invalid NPC purchase quantity.");
            quantityDialog = new(id, quantity, maximum, controls.SingleOrDefault(n => n.Name == "all")?.Point(this),
                controls.SingleOrDefault(n => n.Name == "ok")?.Point(this));
        }
        else quantityWindow = 0;
        return new(items.AsReadOnly(), basket.AsReadOnly(), hoveredId, quantityDialog,
            quantityDialog == null && !bagOpen ? nodes.SingleOrDefault(n => n.Name == "ok")?.Point(this) : null);
    }

    private ulong NpcPurchaseUnitPrice(ulong gameBase, ulong price)
    {
        uint D(ulong rva) => BitConverter.ToUInt32(Guard(gameBase + rva, 4));
        var region = D(0xD71C1C);
        var initialRate = D(0xD6F238);
        Require(initialRate <= 10000, "Invalid NPC purchase rate.");
        price = checked(price * initialRate) / 100;
        if (region <= 9 && (585 & (1 << (int)region)) != 0) return price;
        var influenceRate = D(0xD6F23C); var taxRate = D(0xD6F240);
        Require(influenceRate <= 10000 && taxRate <= 10000, "Invalid NPC purchase tax.");
        return Math.Max(1UL, checked(checked(price * influenceRate) / 100 * taxRate) / 100);
    }
}
