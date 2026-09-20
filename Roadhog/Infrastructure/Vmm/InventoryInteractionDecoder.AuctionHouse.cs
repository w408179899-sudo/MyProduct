using System.Globalization;
using System.Text;
using Roadhog.Core.Model;

namespace Roadhog.Infrastructure.Vmm;

internal sealed partial class InventoryInteractionDecoder
{
    // Game.dll 2026-09-09. Dialog, text getters, rows and editor verified on account 2, 2026-09-20.
    public AuctionHouseSnapshot ReadAuction(ulong gameBase)
    {
        _guards.Clear();
        Viewport(gameBase);
        ulong Root(int id) => GU(gameBase + 0xD63990 + (ulong)id * 8);
        var dialog = Root(132); var auction = Root(152); var editor = Root(310);
        bool Open(ulong a) => a != 0 && Visible(a);
        var dialogOpen = Open(dialog); var open = Open(auction);
        GameUiPoint? trade = null;
        if (dialogOpen)
        {
            Require(Name(dialog) == "dlg_dialog", "Unexpected NPC dialog.");
            trade = Nodes(dialog).SingleOrDefault(n => n.Enabled && AuctionText(n.Address) == "委托交易")?.Point(this);
        }
        var buttons = new Dictionary<string, GameUiPoint>();
        var tab = -1; var search = ""; var message = ""; var loaded = false; ulong money = 0;
        var rows = new List<AuctionMarketRow>();
        AuctionEditor? sell = null;
        if (open)
        {
            Require(Name(auction) == "vendor_dialog", "Unexpected auction dialog.");
            var nodes = Nodes(auction);
            foreach (var n in nodes.Where(n => n.Name is "item_list_btn" or "register_item_btn" or "account_btn" or "search_btn" or "search_cancel_btn"))
                if (n.Point(this) is { } point) buttons.Add(n.Name, point);
            tab = BitConverter.ToInt32(Guard(GU(auction + 1256) + 0x2F8, 4));
            Require(tab is >= 0 and <= 2, "Invalid auction tab.");
            if (tab == 0)
            {
                search = AuctionText(GU(auction + 1528));
                var noItems = nodes.SingleOrDefault(n => n.Name == "no_item_msg");
                message = noItems == null ? "" : AuctionText(noItems.Address);
                foreach (var row in AuctionRows(GU(auction + 1304)))
                {
                    Require(row.Cells.Count >= 4, "Incomplete market columns.");
                    rows.Add(new(row.Template, row.Quantity, row.Cells[0], AuctionMoney(row.Cells[2]) ?? throw new InvalidDataException("Missing market total."),
                        AuctionMoney(row.Cells[3]) ?? throw new InvalidDataException("Missing market unit price.")));
                }
            }
            if (tab == 2)
            {
                loaded = Guard(auction + 1488, 1)[0] != 0;
                if (loaded) money = GU(GU(auction + 1472) + 672);
            }
            if (Open(editor))
            {
                Require(tab == 1 && Name(editor) == "vendor_sell_stack_dialog", "Unexpected auction editor.");
                var item = AuctionRows(GU(editor + 1240)).Single();
                var quantity = GU(editor + 1368); var price = GU(editor + 1376);
                Require(quantity > 0 && quantity <= GU(editor + 1360), "Invalid auction quantity.");
                var cancel = Nodes(editor).SingleOrDefault(n => n.Name == "cancel" && n.W > 20)?.Point(this);
                sell = new(item.Template, quantity, price, AuctionMoney(AuctionText(GU(editor + 1280))), cancel);
            }
        }
        // Generic modal windows may cover controls; never publish a clickable target through them.
        for (int id = 336; id <= 365; id++)
            if (Open(Root(id))) { trade = null; buttons.Clear(); if (sell != null) sell = sell with { CancelButton = null }; }
        VerifyGuards();
        return new(dialogOpen, trade, open, tab, buttons, search, message, rows, loaded, money, sell);
    }

    private string AuctionWide(ulong address)
    {
        var data = Guard(address, 32); var n = BitConverter.ToUInt64(data, 16); var capacity = BitConverter.ToUInt64(data, 24);
        Require(n <= 8192 && capacity >= n && capacity <= 16384, "Invalid auction text.");
        if (n == 0) return "";
        return Encoding.Unicode.GetString(capacity < 8 ? data.AsSpan(0, (int)n * 2) : Guard(BitConverter.ToUInt64(data), (int)n * 2));
    }

    private string AuctionText(ulong address)
    {
        // Decode recognized text getter layouts; no client code is executed.
        var code = Bytes(GU(GU(address) + 632), 24);
        if (code[0] == 0x33 && code[1] == 0xC0 && code[2] == 0xC3) return "";
        Require(code[0] == 0x48 && code[2] == 0x81, "Unsupported auction text getter.");
        var offset = BitConverter.ToInt32(code, 3);
        if (code[1] == 0x8B && offset == 0x2F8)
        {
            var begin = GU(address + 0x2F8); var end = GU(address + 0x300);
            Require(end >= begin && end - begin <= 65536, "Invalid label runs.");
            return begin == end ? "" : AuctionWide(begin + 40);
        }
        Require(code[1] == 0x8D && offset is >= 0 and <= 8192, "Unsupported auction text layout.");
        return AuctionWide(address + (ulong)offset);
    }

    internal static ulong? AuctionMoney(string value)
    {
        var text = new string(value.TakeWhile(c => char.IsAsciiDigit(c) || c == ',' || c == ' ').ToArray()).Trim();
        return ulong.TryParse(text, NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var number) ? number : null;
    }

    private List<(uint Template, ulong Quantity, List<string> Cells)> AuctionRows(ulong list)
    {
        var begin = GU(list + 0x368); var end = GU(list + 0x370); var count = Count(begin, end, 512);
        var result = new List<(uint, ulong, List<string>)>();
        for (int i = 0; i < count; i++)
        {
            var row = GU(begin + (ulong)i * 8); if (row == 0) continue;
            var template = BitConverter.ToUInt32(Guard(row + 168, 4)); if (template == 0) continue;
            var quantity = GU(row + 176); Require(quantity > 0, "Invalid auction row quantity.");
            var cb = GU(row + 72); var ce = GU(row + 80);
            Require(ce >= cb && (ce - cb) % 24 == 0 && (ce - cb) / 24 <= 16, "Invalid auction columns.");
            var cells = new List<string>();
            for (var c = cb; c < ce; c += 24)
            {
                var rb = GU(c); var re = GU(c + 8);
                Require(re >= rb && (re - rb) % 80 == 0 && (re - rb) / 80 <= 64, "Invalid auction text runs.");
                var text = new StringBuilder(); for (var r = rb; r < re; r += 80) text.Append(AuctionWide(r));
                cells.Add(text.ToString());
            }
            result.Add((template, quantity, cells));
        }
        return result;
    }
}
