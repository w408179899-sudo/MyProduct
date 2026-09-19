using System.Text;
using Roadhog.Core.Model;

namespace Roadhog.Infrastructure.Vmm;

/// <summary>Read-only adapter for the 2026-09-09 client. Never called by UI or workflow code.</summary>
internal sealed class PersonalShopDecoder(Func<ulong, int, byte[]> read,
    Func<IReadOnlyList<(ulong Address, int Size)>, IReadOnlyList<byte[]>>? readMany = null)
{
    private readonly Dictionary<(ulong Address, int Size), byte[]> _guards = new();
    private int _width, _height;

    public PersonalShopCursorSnapshot ReadCursor(ulong gameBase)
    {
        Viewport(gameBase);
        var cursor = Bytes(gameBase + 0xDAD020, 8);
        var x = BitConverter.ToInt32(cursor, 0);
        var y = BitConverter.ToInt32(cursor, 4);
        Require(Math.Abs((long)x) <= 32768 && Math.Abs((long)y) <= 32768, "Invalid cursor.");
        return new(_width, _height, new(x, y));
    }

    public PersonalShopSnapshot Read(ulong gameBase)
    {
        _guards.Clear();
        Viewport(gameBase);
        var shop = GU(gameBase + 0xD63ED0);
        var bag = GU(gameBase + 0xD63990 + 27 * 8);
        var bagItems = new List<PersonalShopBagItem>();
        uint hover = 0;
        var bagOpen = bag != 0 && Visible(bag);
        var editorAddress = shop == 0 ? 0 : GU(shop + 0x520);
        var editorVisible = editorAddress != 0 && Visible(editorAddress);
        var isSelling = shop != 0 && BitConverter.ToUInt32(Guard(shop + 0x4D8, 4)) == 1;
        if (bagOpen && !editorVisible && !isSelling)
        {
            Require(Name(bag) == "inventory_dialog", "Unexpected inventory dialog.");
            foreach (var grid in Nodes(bag).Where(n => n.Name is "list0" or "list1" or "list2" or "list3" or "list4"))
            {
                var layout = Bytes(grid.Address + 0x2E0, 0x98);
                var columns = BitConverter.ToUInt32(layout, 8);
                Require(BitConverter.ToUInt32(layout, 0) == 1 && columns is >= 1 and <= 20, "Unsupported inventory grid.");
                double G(int off) => Number(BitConverter.ToDouble(layout, off - 0x2E0));
                var w = G(0x2F8); var h = G(0x2F0);
                Require(w > 0 && h > 0, "Invalid inventory cell.");
                var begin = GU(grid.Address + 0x368); var end = GU(grid.Address + 0x370);
                var count = Count(begin, end, 135);
                var hoveredIndex = BitConverter.ToInt16(Bytes(grid.Address + 0x3F0, 2));
                var pointers = count == 0 ? Array.Empty<byte>() : Guard(begin, count * 8);
                GuardEntries(pointers, count);
                for (var i = 0; i < count; i++)
                {
                    var entry = BitConverter.ToUInt64(pointers, i * 8);
                    if (entry == 0) continue;
                    var data = Guard(entry + 0xA0, 24);
                    var id = BitConverter.ToUInt32(data, 0);
                    var template = BitConverter.ToUInt32(data, 8);
                    if (id == 0 || template == 0) continue;
                    var amount = BitConverter.ToUInt64(data, 16);
                    Require(amount > 0, "Invalid inventory quantity.");
                    var point = Point(grid.X + G(0x328) + G(0x340) + (i % columns) * (w + G(0x308)) + w / 2,
                        grid.Y + G(0x330) + G(0x338) + (i / columns) * (h + G(0x310)) + h / 2);
                    if (point != null) bagItems.Add(new(id, template, amount, point));
                    if (i == hoveredIndex) hover = id;
                }
            }
            Require(bagItems.Select(i => i.InstanceId).Distinct().Count() == bagItems.Count, "Duplicate inventory instances.");
        }
        var listings = new List<PersonalShopListing>();
        PersonalShopEditor? editor = null;
        PersonalShopPoint? start = null;
        var open = false; var selling = false;
        if (shop != 0)
        {
            Require(Name(shop) == "personal_shop_dialog", "Unexpected personal shop dialog.");
            open = Visible(shop);
            var active = BitConverter.ToUInt32(Guard(shop + 0x4D8, 4));
            Require(active <= 1, "Invalid shop active flag.");
            selling = active == 1;
            // The bag can cover Start; price editing and selling make it unavailable.
            if (open && !bagOpen && !editorVisible && !selling)
                start = Nodes(shop).SingleOrDefault(n => n.Name == "start")?.Point(this);
            var list = GU(shop + 0x4F0);
            Require(list != 0, "Missing sale list.");
            var begin = GU(list + 0x368); var end = GU(list + 0x370);
            var count = Count(begin, end, 10);
            var pointers = count == 0 ? Array.Empty<byte>() : Guard(begin, count * 8);
            GuardEntries(pointers, count);
            var head = GU(shop + 0x4E0);
            for (var i = 0; i < count; i++)
            {
                var entry = BitConverter.ToUInt64(pointers, i * 8);
                if (entry == 0) continue;
                var data = Guard(entry + 0xA0, 24);
                var id = BitConverter.ToUInt32(data, 0); var template = BitConverter.ToUInt32(data, 8);
                if (template == 0) continue;
                var quantity = BitConverter.ToUInt64(data, 16);
                Require(id != 0 && quantity > 0, "Invalid registered item.");
                listings.Add(new(id, template, quantity, Price(head, id)));
            }
            Require(listings.Select(i => i.InstanceId).Distinct().Count() == listings.Count, "Duplicate listings.");
            var pending = BitConverter.ToUInt32(Guard(shop + 0x510, 4));
            var dialog = GU(shop + 0x520);
            if (dialog != 0 && Visible(dialog))
            {
                Require(pending != 0 && Name(dialog) is "item_price_dialog" or "item_price_count_dialog", "Unexpected price editor.");
                var fields = Guard(dialog + 1468, 44);
                var mode = BitConverter.ToUInt32(fields, 0);
                Require(mode <= 1, "Invalid price mode.");
                var nodes = Nodes(dialog);
                editor = new(pending, mode == 0, BitConverter.ToUInt64(fields, 4), BitConverter.ToUInt64(fields, 12),
                    BitConverter.ToUInt64(fields, 36), nodes.SingleOrDefault(n => n.Name == "price")?.Point(this),
                    nodes.SingleOrDefault(n => n.Name == "ok")?.Point(this));
            }
            else Require(pending == 0, "Shop editor changed during capture.");
        }
        var guards = _guards.ToArray();
        var verification = Batch(guards.Select(g => g.Key).ToArray());
        for (var i = 0; i < guards.Length; i++) Require(verification[i].AsSpan().SequenceEqual(guards[i].Value), "Shop UI changed during capture.");
        return new(open, selling, bagOpen, bagItems.AsReadOnly(), hover, listings.AsReadOnly(), editor, start);
    }

    private ulong Price(ulong head, uint id)
    {
        var node = GU(head + 8);
        for (var count = 0; node != head && Bytes(node + 0x19, 1)[0] == 0; count++)
        {
            Require(count < 64, "Invalid price tree.");
            var key = BitConverter.ToUInt32(Guard(node + 0x20, 4));
            if (key == id) return GU(node + 0x28);
            node = GU(node + (key > id ? 0UL : 0x10UL));
        }
        throw new InvalidDataException("Registered item has no price.");
    }

    private List<Node> Nodes(ulong root)
    {
        var result = new List<Node>();
        var seen = new HashSet<ulong>();
        void Walk(ulong a, double x, double y, int depth)
        {
            Require(depth <= 8 && seen.Count < 400 && seen.Add(a), "Invalid widget tree.");
            if (!Visible(a)) return;
            var geometry = Guard(a + 0x58, 64);
            double D(int offset) => Number(BitConverter.ToDouble(geometry, offset));
            x += D(0); y += D(8);
            var w = D(16); var h = D(24);
            Require(w >= 0 && h >= 0, "Negative widget dimensions.");
            result.Add(new(a, Name(a), x, y, w, h, (U(a + 0x28) & 2) != 0));
            var head = GU(a + 0x238);
            if (head == 0) return;
            var n = GU(head);
            for (var i = 0; n != head; i++)
            {
                Require(n != 0 && i < 128, "Invalid children list.");
                Walk(GU(n + 0x10), x + D(32), y + D(40), depth + 1);
                n = GU(n);
            }
        }
        Walk(root, 0, 0, 0);
        return result;
    }

    private sealed record Node(ulong Address, string Name, double X, double Y, double W, double H, bool Enabled)
    {
        public PersonalShopPoint? Point(PersonalShopDecoder decoder) => Enabled && W > 0 && H > 0 ? decoder.Point(X + W / 2, Y + H / 2) : null;
    }
    private PersonalShopPoint? Point(double x, double y) => x >= 0 && y >= 0 && x < _width && y < _height ? new((int)Math.Round(x), (int)Math.Round(y)) : null;
    private void Viewport(ulong b)
    {
        var data = Bytes(b + 0xDACF00, 16);
        _width = checked((int)Number(BitConverter.ToDouble(data, 0)));
        _height = checked((int)Number(BitConverter.ToDouble(data, 8)));
        Require(_width is >= 320 and <= 16384 && _height is >= 200 and <= 16384, "Invalid viewport.");
    }
    private string Name(ulong a)
    {
        var data = Bytes(a + 8, 32);
        var n = BitConverter.ToUInt64(data, 16); var capacity = BitConverter.ToUInt64(data, 24);
        Require(n <= 256 && capacity >= n && capacity < 4096, "Invalid widget name.");
        return n == 0 ? "" : Encoding.UTF8.GetString(capacity < 16 ? data.AsSpan(0, (int)n) : Bytes(BitConverter.ToUInt64(data), (int)n));
    }
    private bool Visible(ulong a) => (GU(a + 0x28) & 1) != 0;
    private static int Count(ulong begin, ulong end, ulong max)
    {
        Require(end >= begin && (end - begin) % 8 == 0 && (end - begin) / 8 <= max, "Invalid item vector.");
        return (int)((end - begin) / 8);
    }
    private ulong U(ulong a) => BitConverter.ToUInt64(Bytes(a, 8));
    private ulong GU(ulong a) => BitConverter.ToUInt64(Guard(a, 8));
    private void GuardEntries(byte[] pointers, int count)
    {
        var requests = Enumerable.Range(0, count).Select(i => BitConverter.ToUInt64(pointers, i * 8))
            .Where(p => p != 0).Select(p => (Address: p + 0xA0, Size: 24)).Distinct().Where(r => !_guards.ContainsKey(r)).ToArray();
        var values = Batch(requests);
        for (var i = 0; i < requests.Length; i++) _guards.Add(requests[i], values[i]);
    }
    private IReadOnlyList<byte[]> Batch(IReadOnlyList<(ulong Address, int Size)> requests)
    {
        foreach (var request in requests) Require(request.Address >= 0x10000 && request.Address < 0x800000000000, "Invalid batch pointer.");
        var values = readMany == null ? requests.Select(r => Bytes(r.Address, r.Size)).ToArray() : readMany(requests);
        Require(values.Count == requests.Count, "Incomplete UI batch.");
        for (var i = 0; i < requests.Count; i++) Require(values[i] != null && values[i].Length == requests[i].Size, "Incomplete UI batch field.");
        return values;
    }
    private byte[] Guard(ulong a, int size)
    {
        if (_guards.TryGetValue((a, size), out var previous)) return previous;
        var bytes = Bytes(a, size); _guards.Add((a, size), bytes); return bytes;
    }
    private byte[] Bytes(ulong a, int size)
    {
        Require(a >= 0x10000 && a < 0x800000000000, "Invalid pointer.");
        var bytes = read(a, size);
        Require(bytes != null && bytes.Length == size, "Incomplete UI read.");
        return bytes!;
    }
    private static double Number(double value) { Require(double.IsFinite(value) && Math.Abs(value) <= 32768, "Invalid geometry."); return value; }
    private static void Require(bool value, string message) { if (!value) throw new InvalidDataException(message); }
}
