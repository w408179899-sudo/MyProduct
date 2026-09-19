using System.Text;
using Roadhog.Core.Model;

internal static class PersonalShopProbeTests
{
    public static Task RegistrationAndFaultsAsync()
    {
        const ulong game = 0x10000000, shop = 0x20000000, list = 0x21000000;
        const ulong vector = 0x22000000, item = 0x23000000, head = 0x24000000, node = 0x25000000;
        var memory = new TestMemory();
        memory.U(game + 0xD63ED0, shop);
        memory.U(shop + 0x4F0, list);
        memory.U(shop + 0x4E0, head);
        memory.U(list + 0x368, vector);
        memory.U(list + 0x370, vector + 8);
        memory.U(vector, item);
        memory.I(item + 0xA0, 123);
        memory.I(item + 0xA8, 167000294);
        memory.U(item + 0xB0, 26);
        memory.U(head + 8, node);
        memory.Put(node + 0x19, new byte[] { 0 });
        memory.I(node + 0x20, 123);
        memory.U(node + 0x28, 1);
        var decoder = new PersonalShopLiveProbe.Memory(memory.Read, game);
        var listing = decoder.Registered().Single();
        Require(listing.InstanceId == 123 && listing.TemplateId == 167000294 &&
                listing.Quantity == 26 && listing.UnitPrice == 1,
            "Instance, template, full stack and unit price must remain distinct.");

        memory.U(head + 8, head);
        Reject(() => decoder.Registered(), "An absent price must never become zero or one.");
        memory.U(head + 8, node);
        memory.U(list + 0x370, vector + 88);
        Reject(() => decoder.Registered(), "More than ten shop slots is invalid.");
        memory.U(list + 0x370, vector + 7);
        Reject(() => decoder.Registered(), "An unaligned vector must fail.");
        memory.U(list + 0x370, vector + 8);
        memory.Remove(node + 0x28);
        Reject(() => decoder.Registered(), "A short price read must fail before input.");
        return Task.CompletedTask;
    }

    public static Task GeometryAndHoverAsync()
    {
        const ulong parent = 0x20000000, child = 0x21000000, head = 0x22000000, node = 0x23000000;
        var memory = new TestMemory();
        memory.Widget(parent, "shop", 28, 120, 324.8, 322.4, 2.4, 13.6);
        memory.Widget(child, "start", 212, 285.6, 54.4, 20.8, 0, 0);
        memory.U(parent + 0x238, head);
        memory.U(head, node);
        memory.U(node, head);
        memory.U(node + 0x10, child);
        var decoder = new PersonalShopLiveProbe.Memory(memory.Read, 0x10000000);
        var button = decoder.Nodes(parent).Single(n => n.Name == "start");
        Require(Math.Abs(button.X + button.W / 2 - 269.6) < .001 &&
                Math.Abs(button.Y + button.H / 2 - 429.6) < .001,
            "Control coordinates must include parent bounds and client origin.");
        memory.D(child + 0x58, double.NaN);
        Reject(() => decoder.Nodes(parent), "Nonfinite geometry must never reach input.");
        memory.D(child + 0x58, 212);
        memory.U(node, node);
        Reject(() => decoder.Nodes(parent), "Cyclic child lists must terminate with failure.");

        const ulong grid = 0x30000000, vector = 0x31000000, entry = 0x32000000;
        memory.U(grid + 0x368, vector);
        memory.U(grid + 0x370, vector + 8);
        memory.U(vector, entry);
        memory.I(entry + 0xA0, 456);
        memory.Put(grid + 0x3F0, BitConverter.GetBytes((short)-1));
        Require(decoder.Hover(grid) == 0, "Reopened inventory without a hover event has no target.");
        memory.Put(grid + 0x3F0, BitConverter.GetBytes((short)0));
        Require(decoder.Hover(grid) == 456, "Hover must resolve the actual UI instance.");
        return Task.CompletedTask;
    }

    public static Task AuthorizedPriceAndPlanAsync()
    {
        PersonalShopLiveProbe.ValidateEditor(123, 123, 0, 1, 26, 26, 26);
        Reject(() => PersonalShopLiveProbe.ValidateEditor(124, 123, 0, 1, 26, 26, 26), "Wrong item.");
        Reject(() => PersonalShopLiveProbe.ValidateEditor(123, 123, 1, 1, 1, 26, 26), "Total-price editing mode.");
        Reject(() => PersonalShopLiveProbe.ValidateEditor(123, 123, 0, 2, 52, 26, 26), "Wrong unit price.");
        Reject(() => PersonalShopLiveProbe.ValidateEditor(123, 123, 0, 1, 1, 1, 26), "Partial stack.");
        var plan = new[] { new InventoryItemSnapshot(567, 123, "test", 26, 0, false) };
        var entry = new PersonalShopLiveProbe.Listing(0, 123, 567, 26, 1);
        PersonalShopLiveProbe.ValidatePlan(plan, new[] { entry });
        Reject(() => PersonalShopLiveProbe.ValidatePlan(plan, new[] { entry with { UnitPrice = 2 } }), "Price changed before start.");
        Reject(() => PersonalShopLiveProbe.ValidatePlan(plan, new[] { entry with { InstanceId = 124 } }), "Unplanned item before start.");
        Reject(() => PersonalShopLiveProbe.ValidatePlan(plan, new[] { entry, entry }), "Duplicate registration.");
        Reject(() => PersonalShopLiveProbe.ValidatePlan(plan, Array.Empty<PersonalShopLiveProbe.Listing>()), "Incomplete registration.");
        Reject(() => PersonalShopLiveProbe.ValidatePlan(Array.Empty<InventoryItemSnapshot>(), Array.Empty<PersonalShopLiveProbe.Listing>()), "Empty shop.");
        return Task.CompletedTask;
    }

    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }

    private static void Reject(Action action, string message)
    {
        try { action(); }
        catch (Exception) { return; }
        throw new InvalidOperationException(message);
    }

    private sealed class TestMemory
    {
        private readonly Dictionary<ulong, byte> _bytes = new();
        public void Put(ulong address, byte[] bytes)
        {
            for (var i = 0; i < bytes.Length; i++) _bytes[address + (ulong)i] = bytes[i];
        }
        public void U(ulong address, ulong value) => Put(address, BitConverter.GetBytes(value));
        public void I(ulong address, uint value) => Put(address, BitConverter.GetBytes(value));
        public void D(ulong address, double value) => Put(address, BitConverter.GetBytes(value));
        public void Remove(ulong address) => _bytes.Remove(address);
        public byte[]? Read(ulong address, int count)
        {
            var result = new List<byte>();
            for (var i = 0; i < count && _bytes.TryGetValue(address + (ulong)i, out var value); i++) result.Add(value);
            return result.ToArray();
        }
        public void Widget(ulong address, string name, double x, double y, double w, double h, double cx, double cy)
        {
            Put(address + 8, Encoding.UTF8.GetBytes(name));
            U(address + 0x18, (ulong)name.Length);
            U(address + 0x20, 15);
            U(address + 0x28, 3);
            var values = new[] { x, y, w, h, cx, cy, w, h };
            for (var i = 0; i < values.Length; i++) D(address + 0x58 + (ulong)i * 8, values[i]);
            U(address + 0x238, 0);
        }
    }
}
