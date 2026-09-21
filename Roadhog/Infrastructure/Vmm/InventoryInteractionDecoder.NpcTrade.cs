using Roadhog.Core.Model;

namespace Roadhog.Infrastructure.Vmm;

internal sealed partial class InventoryInteractionDecoder
{
    // Existing DlgTrade layout, aion202609091125.json: 2CC800, 2CCED0, 2D01E0.
    // Mode 1 is NPC selling; mode 3 is the existing personal-shop purchase adapter.
    public NpcTradeSnapshot ReadNpcTrade(ulong gameBase)
    {
        _guards.Clear();
        Viewport(gameBase);
        ulong Root(int id) => GU(gameBase + 0xD63990 + (ulong)id * 8);
        bool Open(ulong address) => address != 0 && Visible(address);
        var dialog = Root(132); var window = Root(134); var bag = Root(27);
        var dialogOpen = Open(dialog); var open = Open(window); var bagOpen = Open(bag);
        var entries = new Dictionary<string, GameUiPoint>();
        if (dialogOpen)
        {
            Require(Name(dialog) == "dlg_dialog", "Unexpected NPC dialog.");
            foreach (var node in Nodes(dialog).Where(n => n.Enabled))
                if (node.Point(this) is { } point && AuctionText(node.Address) is { Length: > 0 and < 80 } text)
                    entries.TryAdd(text, point);
        }
        int mode = -1; uint npc = 0;
        var basket = new List<NpcTradeItem>(); GameUiPoint? sell = null;
        if (open)
        {
            mode = BitConverter.ToInt32(Guard(window + 1252, 4));
            Require(mode is >= 0 and <= 6, "Invalid NPC trade mode.");
            npc = BitConverter.ToUInt32(Guard(window + 1240, 4));
            if (mode == 1)
            {
                var nodes = Nodes(window);
                Require(npc != 0 && nodes.Any(n => n.Name == "sell_item_container"), "Unexpected NPC selling window.");
                var list = GU(window + 1320 + 8); // item_basket for mode 1
                var begin = GU(list + 0x368); var count = Count(begin, GU(list + 0x370), 135);
                for (int i = 0; i < count; i++)
                {
                    var row = GU(begin + (ulong)i * 8); if (row == 0) continue;
                    var item = Guard(row + 160, 24);
                    var template = BitConverter.ToUInt32(item, 8); if (template == 0) continue;
                    var id = BitConverter.ToUInt32(item, 0); var quantity = BitConverter.ToUInt64(item, 16);
                    Require(id != 0 && quantity > 0, "Invalid NPC sell basket item.");
                    basket.Add(new(id, template, quantity));
                }
                Require(basket.Select(i => i.InstanceId).Distinct().Count() == basket.Count, "Duplicate NPC sell basket item.");
                if (!bagOpen) sell = nodes.SingleOrDefault(n => n.Name == "ok")?.Point(this);
            }
        }
        var modalOpen = false;
        for (int id = 336; id <= 365; id++) if (Open(Root(id))) modalOpen = true;
        if (modalOpen) { entries.Clear(); sell = null; }
        VerifyGuards();
        return new(dialogOpen, entries, open, mode, npc, bagOpen, basket.AsReadOnly(), sell, modalOpen);
    }
}
