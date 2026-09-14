using System.Text;
using Roadhog.Core.Model;

namespace Roadhog.Infrastructure.Vmm;

/// <summary>Game.dll 2026-09-09 UI layout; all pointer, geometry and traversal validation stays here.</summary>
internal sealed class ChannelSwitchUiDecoder(Func<ulong, int, byte[]> read)
{
    private readonly List<(ulong Address, byte[] Value)> _guards = new();
    private int _width, _height;

    public ChannelSwitchUiSnapshot Read(ulong gameBase)
    {
        _guards.Clear();
        _width = checked((int)D(gameBase + 0xDACF00));
        _height = checked((int)D(gameBase + 0xDACF08));
        Require(_width is >= 320 and <= 16384 && _height is >= 200 and <= 16384, "Invalid viewport.");
        var cursor = new ChannelUiPoint(I(gameBase + 0xDAD020), I(gameBase + 0xDAD024));
        Require(Math.Abs((long)cursor.X) <= 32768 && Math.Abs((long)cursor.Y) <= 32768, "Invalid cursor.");
        ulong Slot(int id) => GuardU(gameBase + 0xD63990 + (ulong)id * 8);
        var start = Slot(17);
        var main = Slot(376);
        var sub = Slot(377);
        var dialog = Slot(227);
        ChannelUiPoint? menu = null, service = null, switchItem = null, drop = null, move = null;
        var options = new List<ChannelUiOption>();
        var open = false;
        var expanded = false;
        var selected = 0;
        ulong startButton = 0;
        if (start != 0)
        {
            Require(Name(start) == "start_dialog", "Unexpected start dialog.");
            startButton = GuardU(start + 0x4D8);
            if (startButton != 0 && Visible(start))
            {
                Require(Name(startButton) == "start_button", "Unexpected menu button.");
                menu = ChildPoint(start, startButton);
            }
        }
        if (main != 0 && Visible(main) && I(main + 0x3C8) == 17 && GuardU(main + 0x518) == startButton)
        {
            service = FindPoint(main, "menu_service");
            if (sub != 0 && Visible(sub) && I(sub + 0x3C8) == 17)
                switchItem = FindPoint(sub, "service_channel");
        }
        if (dialog != 0)
        {
            Require(Name(dialog) == "channel_dialog", "Unexpected channel dialog.");
            open = Visible(dialog);
            if (open)
            {
                var combo = GuardU(dialog + 0x4D8);
                Require(combo != 0 && Name(combo) == "channel_list", "Missing channel combo.");
                var list = GuardU(combo + 0x2F8);
                Require(list != 0 && Name(list) == "item_list", "Missing channel list.");
                expanded = GuardI(combo + 0x30) == 1;
                selected = GuardI(list + 0x300) + 1;
                drop = FindPoint(dialog, "drop_btn");
                move = FindPoint(dialog, "ok");
                var begin = GuardU(list + 0x2E8);
                var end = GuardU(list + 0x2F0);
                Require(end >= begin && (end - begin) % 96 == 0 && (end - begin) / 96 <= 100, "Invalid channel entries.");
                var count = (int)((end - begin) / 96);
                Require(count > 0 && selected >= 0 && selected <= count, "Invalid channel selection.");
                var rect = Rect(list, 0x58);
                var client = Rect(list, 0x78);
                var padding = D(list + 0x350);
                var border = D(list + 0x258);
                var scroll = D(list + 0x360);
                var maxRows = I(combo + 0x308);
                // Full visible lists expose their exact scaled row height, including font scaling.
                // Do not guess coordinates for a clipped/scrolling list.
                var rowHeight = (rect.H - 2 * padding - 2 * border) / count;
                var canLocateRows = expanded && count <= maxRows && Math.Abs(scroll) < 0.01 &&
                    rowHeight >= D(list + 0x308) - 0.01 && rowHeight is >= 4 and <= 100;
                for (var i = 0; i < count; i++)
                {
                    var entry = begin + (ulong)i * 96;
                    var label = WideName(entry);
                    var digits = new string(label.Where(char.IsAsciiDigit).ToArray());
                    Require(int.TryParse(digits, out var number) && number == i + 1, "Unexpected channel label.");
                    var enabled = (I(entry + 88) & 1) != 0;
                    ChannelUiPoint? point = canLocateRows
                        ? Point(rect.X + client.X + client.W / 2, rect.Y + client.Y + (i + 0.5) * rowHeight)
                        : null;
                    options.Add(new ChannelUiOption(number, enabled, point));
                }
            }
        }
        // Any root/visibility/selection change during traversal invalidates the whole dependent UI tree.
        foreach (var (address, bytes) in _guards)
            Require(read(address, bytes.Length).AsSpan().SequenceEqual(bytes), "Channel UI changed during traversal.");
        return new(_width, _height, cursor, menu, service, switchItem, open, expanded,
            selected, drop, move, options.AsReadOnly(), DateTimeOffset.Now);
    }

    private ChannelUiPoint? FindPoint(ulong parent, string name)
    {
        var origin = Rect(parent, 0x58);
        return Find(parent, name, origin.X, origin.Y, new HashSet<ulong>(), 0);
    }

    private ChannelUiPoint? Find(ulong parent, string name, double x, double y, HashSet<ulong> seen, int depth)
    {
        Require(depth <= 8 && seen.Count <= 256 && seen.Add(parent), "Invalid channel UI tree.");
        var client = Rect(parent, 0x78);
        var head = GuardU(parent + 0x238);
        if (head == 0) return null;
        var node = GuardU(head);
        var nodes = new HashSet<ulong>();
        while (node != head)
        {
            Require(node != 0 && nodes.Count < 128 && nodes.Add(node), "Invalid UI child list.");
            var child = GuardU(node + 0x10);
            Require(child != 0, "Missing UI child.");
            var rect = Rect(child, 0x58);
            var cx = x + client.X + rect.X;
            var cy = y + client.Y + rect.Y;
            if (Visible(child))
            {
                if (Name(child) == name) return Enabled(child) ? Point(cx + rect.W / 2, cy + rect.H / 2) : null;
                var result = Find(child, name, cx, cy, seen, depth + 1);
                if (result != null) return result;
            }
            node = GuardU(node);
        }
        return null;
    }

    private ChannelUiPoint? ChildPoint(ulong parent, ulong child)
    {
        if (!Visible(child) || !Enabled(child)) return null;
        var p = Rect(parent, 0x58);
        var c = Rect(parent, 0x78);
        var r = Rect(child, 0x58);
        return Point(p.X + c.X + r.X + r.W / 2, p.Y + c.Y + r.Y + r.H / 2);
    }

    private ChannelUiPoint? Point(double x, double y) =>
        x >= 0 && y >= 0 && x < _width && y < _height ? new((int)Math.Round(x), (int)Math.Round(y)) : null;

    private (double X, double Y, double W, double H) Rect(ulong widget, ulong offset)
    {
        var bytes = Bytes(widget + offset, 32);
        var x = BitConverter.ToDouble(bytes, 0);
        var y = BitConverter.ToDouble(bytes, 8);
        var w = BitConverter.ToDouble(bytes, 16);
        var h = BitConverter.ToDouble(bytes, 24);
        Require(double.IsFinite(x) && double.IsFinite(y) && double.IsFinite(w) && double.IsFinite(h) &&
            Math.Abs(x) <= 32768 && Math.Abs(y) <= 32768 && w is >= 0 and <= 32768 && h is >= 0 and <= 32768, "Invalid UI rectangle.");
        _guards.Add((widget + offset, bytes));
        return (x, y, w, h);
    }
    private string Name(ulong widget)
    {
        var length = U(widget + 0x18);
        var capacity = U(widget + 0x20);
        Require(length <= 128 && capacity >= length && capacity < 4096, "Invalid widget name.");
        return Encoding.UTF8.GetString(Bytes(capacity < 16 ? widget + 8 : U(widget + 8), (int)length));
    }
    private string WideName(ulong entry)
    {
        var length = U(entry + 16);
        var capacity = U(entry + 24);
        Require(length > 0 && length <= 64 && capacity >= length && capacity < 4096, "Invalid channel label.");
        return Encoding.Unicode.GetString(Bytes(capacity < 8 ? entry : U(entry), (int)length * 2));
    }
    private bool Visible(ulong widget) => (GuardU(widget + 0x28) & 1) != 0;
    private bool Enabled(ulong widget) => (U(widget + 0x28) & 2) != 0;
    private byte[] Bytes(ulong address, int size)
    {
        Require(address >= 0x10000 && address < 0x800000000000, "Invalid UI pointer.");
        var bytes = read(address, size);
        Require(bytes.Length == size, "Incomplete UI field.");
        return bytes;
    }
    private ulong U(ulong address) => BitConverter.ToUInt64(Bytes(address, 8));
    private int I(ulong address) => BitConverter.ToInt32(Bytes(address, 4));
    private double D(ulong address)
    {
        var value = BitConverter.ToDouble(Bytes(address, 8));
        Require(double.IsFinite(value), "Invalid UI scalar.");
        return value;
    }
    private ulong GuardU(ulong address) { var bytes = Bytes(address, 8); _guards.Add((address, bytes)); return BitConverter.ToUInt64(bytes); }
    private int GuardI(ulong address) { var bytes = Bytes(address, 4); _guards.Add((address, bytes)); return BitConverter.ToInt32(bytes); }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
}
