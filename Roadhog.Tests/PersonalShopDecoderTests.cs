using System.Text;
using Roadhog.Infrastructure.Vmm;

internal static partial class PersonalShopDecoderTests
{
    public static Task InventoryDiscardGeometryAndFaultsAsync()
    {
        var m = new Fixture();
        const ulong bag = 0x30000000, grid = 0x31000000, dialog = 0x32000000, ok = 0x33000000, cancel = 0x34000000;
        m.U(Fixture.Shop + 0x28, 0);
        m.U(Fixture.Game + 0xD63990 + 27 * 8, bag);
        m.Widget(bag, "inventory_dialog", 150, 100, 320, 330, 2, 4);
        m.Widget(grid, "list0", 10, 40, 288, 240, 0, 0);
        m.Children(bag, grid);
        m.I(grid + 0x2E0, 1); m.I(grid + 0x2E8, 9); m.D(grid + 0x2F0, 32); m.D(grid + 0x2F8, 32);
        m.U(grid + 0x368, Fixture.Vector); m.U(grid + 0x370, Fixture.Vector + 8);
        var ready = m.Decoder().ReadInventory(Fixture.Game);
        Require(ready.IsOpen && ready.Items.Single().Point == new Roadhog.Core.Model.GameUiPoint(178, 160) && ready.HoveredInstanceId == 123,
            "shared UI layout and hover identity work with moved inventory");
        Require(ready.DropPoint != null && ready.DropPoint.X > 482, "release point excludes actual inventory bounds");
        m.I(bag + 0x598, 123);
        m.U(Fixture.Game + 0xD63990 + 336 * 8, dialog);
        m.Widget(dialog, "msgbox", 400, 300, 200, 180, 0, 0);
        m.Widget(ok, "", 10, 100, 50, 20, 0, 0); m.Widget(cancel, "cancel", 80, 100, 50, 20, 0, 0);
        m.Children(dialog, ok, cancel);
        m.U(dialog + 0x578, ok); m.U(dialog + 0x580, cancel);
        m.I(dialog + 0x4D8, 2049); m.I(dialog + 0x4DC, 2101); m.I(dialog + 0x4E0, 2104); m.I(dialog + 0x4E8, 123);
        var confirm = m.Decoder().ReadInventory(Fixture.Game);
        Require(confirm.DiscardDialog is { InstanceId: 123, Kind: Roadhog.Core.Model.InventoryDiscardConfirmKind.Normal } &&
            confirm.DiscardDialog.ConfirmButton == new Roadhog.Core.Model.GameUiPoint(435, 410), "primary button pointer and ancestor geometry");
        Require(confirm.Items.Count == 0 && confirm.DropPoint == null, "modal makes bag noninteractive");
        m.I(dialog + 0x4E8, 999);
        Require(m.Decoder().ReadInventory(Fixture.Game) is { OtherModalOpen: true, DiscardDialog: null }, "foreign confirmation blocks action");
        m.I(dialog + 0x4E8, 123); m.U(ok + 0x28, 1);
        Require(m.Decoder().ReadInventory(Fixture.Game).DiscardDialog!.ConfirmButton == null, "disabled button not clickable");
        m.ShortAddress = bag + 0x598;
        Reject(() => m.Decoder().ReadInventory(Fixture.Game), "short pending identity cannot publish idle");
        m.ShortAddress = null;
        m.U(Fixture.Game + 0xD63990 + 336 * 8, 0); m.U(Fixture.Game + 0xD63990 + 356 * 8, dialog);
        m.I(dialog + 0x4D8, 3); m.I(dialog + 0x4DC, 2105); m.I(dialog + 0x4E0, 2106);
        Require(m.Decoder().ReadInventory(Fixture.Game).DiscardDialog!.Kind == Roadhog.Core.Model.InventoryDiscardConfirmKind.Special, "special pool decoded separately");
        return Task.CompletedTask;
    }

    public static Task DecodeAndFaultsAsync()
    {
        var memory = new Fixture();
        var snapshot = memory.Decoder().Read(Fixture.Game);
        Require(snapshot.IsOpen && !snapshot.IsSelling && snapshot.StartButton == new Roadhog.Core.Model.GameUiPoint(270, 430), "parent bounds and client origin");
        Require(snapshot.Listings.Single() is { InstanceId: 123, TemplateId: 567, Quantity: 26, UnitPrice: 1 }, "production price tree and full-stack decoding");
        memory.U(Fixture.Head + 8, Fixture.Head);
        Reject(() => memory.Decoder().Read(Fixture.Game), "Missing price cannot publish a listing.");
        memory.U(Fixture.Head + 8, Fixture.Node);
        memory.I(Fixture.Shop + 0x4D8, 2);
        Reject(() => memory.Decoder().Read(Fixture.Game), "Invalid active flag.");
        memory.I(Fixture.Shop + 0x4D8, 0);
        memory.D(Fixture.Button + 0x58, double.NaN);
        Reject(() => memory.Decoder().Read(Fixture.Game), "Invalid geometry.");
        memory.D(Fixture.Button + 0x58, 212);
        memory.ShortAddress = Fixture.Node + 0x28;
        Reject(() => memory.Decoder().Read(Fixture.Game), "Short price read.");
        memory.ShortAddress = null;
        var calls = 0;
        var changing = new InventoryInteractionDecoder((a, n) => a == Fixture.Game + 0xD63ED0 && ++calls > 1 ? BitConverter.GetBytes(0UL) : memory.Read(a, n));
        Reject(() => changing.Read(Fixture.Game), "Changed root cannot publish old geometry.");
        return Task.CompletedTask;
    }

    public static Task BatchesAndClosedStateAsync()
    {
        var memory = new Fixture();
        var batches = 0;
        var decoder = new InventoryInteractionDecoder(memory.Read, requests =>
        {
            batches++;
            return requests.Select(r => memory.Read(r.Address, r.Size)).ToArray();
        });
        Require(decoder.Read(Fixture.Game).Listings.Count == 1 && batches >= 2, "batch capture and end guards both execute");
        var shortBatch = new InventoryInteractionDecoder(memory.Read, _ => Array.Empty<byte[]>());
        Reject(() => shortBatch.Read(Fixture.Game), "Missing batch entries cannot publish.");
        var changedBatch = new InventoryInteractionDecoder(memory.Read, requests =>
        {
            var result = requests.Select(r => memory.Read(r.Address, r.Size)).ToArray();
            if (requests.Count > 1) result[0][0] ^= 1;
            return result;
        });
        Reject(() => changedBatch.Read(Fixture.Game), "Guard batch must detect changes.");
        memory.U(Fixture.Game + 0xD63ED0, 0);
        var closed = memory.Decoder().Read(Fixture.Game);
        Require(!closed.IsOpen && !closed.IsSelling && closed.Listings.Count == 0, "Valid null root publishes closed shop immediately.");
        memory.ShortAddress = Fixture.Game + 0xD63ED0;
        Reject(() => memory.Decoder().Read(Fixture.Game), "Short root cannot masquerade as closed shop.");
        return Task.CompletedTask;
    }

    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static void Reject(Action action, string message)
    {
        try { action(); } catch (InvalidDataException) { return; }
        throw new Exception(message);
    }
    private sealed class Fixture
    {
        internal const ulong Game = 0x10000000, Shop = 0x20000000, Button = 0x21000000, List = 0x22000000,
            Vector = 0x23000000, Item = 0x24000000, Head = 0x25000000, Node = 0x26000000;
        private readonly Dictionary<ulong, byte> _memory = new();
        internal ulong? ShortAddress;
        internal Fixture()
        {
            D(Game + 0xDACF00, 1024); D(Game + 0xDACF08, 768);
            U(Game + 0xD63ED0, Shop);
            Widget(Shop, "personal_shop_dialog", 28, 120, 324.8, 322.4, 2.4, 13.6);
            Widget(Button, "start", 212, 285.6, 54.4, 20.8, 0, 0);
            const ulong children = 0x27000000, child = 0x28000000;
            U(Shop + 0x238, children); U(children, child); U(child, children); U(child + 0x10, Button);
            U(Shop + 0x4F0, List); U(Shop + 0x4E0, Head);
            U(List + 0x368, Vector); U(List + 0x370, Vector + 8); U(Vector, Item);
            I(Item + 0xA0, 123); I(Item + 0xA8, 567); U(Item + 0xB0, 26);
            U(Head + 8, Node); I(Node + 0x20, 123); U(Node + 0x28, 1);
        }
        internal InventoryInteractionDecoder Decoder() => new(Read);
        internal byte[] Read(ulong a, int n) => Enumerable.Range(0, a == ShortAddress ? n - 1 : n).Select(i => _memory.GetValueOrDefault(a + (ulong)i)).ToArray();
        internal void Put(ulong a, byte[] value) { for (var i = 0; i < value.Length; i++) _memory[a + (ulong)i] = value[i]; }
        internal void U(ulong a, ulong value) => Put(a, BitConverter.GetBytes(value));
        internal void I(ulong a, uint value) => Put(a, BitConverter.GetBytes(value));
        internal void D(ulong a, double value) => Put(a, BitConverter.GetBytes(value));
        internal void Children(ulong parent, params ulong[] children)
        {
            var head = parent + 0x900; U(parent + 0x238, head); U(head, children.Length == 0 ? head : head + 0x30);
            for (var i = 0; i < children.Length; i++)
            {
                var node = head + (ulong)(i + 1) * 0x30;
                U(node, i == children.Length - 1 ? head : node + 0x30); U(node + 0x10, children[i]);
            }
        }
        internal void Widget(ulong a, string name, double x, double y, double w, double h, double cx, double cy)
        {
            if (name.Length >= 16) { U(a + 8, a + 0x800); Put(a + 0x800, Encoding.UTF8.GetBytes(name)); }
            else Put(a + 8, Encoding.UTF8.GetBytes(name));
            U(a + 0x18, (ulong)name.Length); U(a + 0x20, (ulong)Math.Max(15, name.Length)); U(a + 0x28, 3);
            var values = new[] { x, y, w, h, cx, cy, w, h };
            for (var i = 0; i < values.Length; i++) D(a + 0x58 + (ulong)i * 8, values[i]);
        }
    }
}
