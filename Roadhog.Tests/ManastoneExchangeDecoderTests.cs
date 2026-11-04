using System.Text;
using Roadhog.Core.Api;
using Roadhog.Core.Common;
using Roadhog.Core.Model;
using Roadhog.Infrastructure.Vmm;

internal static class ManastoneExchangeDecoderTests
{
    public static Task DecodeAndGuardsAsync()
    {
        var m = new Memory();
        var decoded = m.ReadSnapshot();
        Check(decoded is { IsOpen: true, NpcServerObjectId: 2147613646, QuestId: 4113, PageType: 1, PageId: 1003, PageName: "quest_accept_1" }, "page identity and normal select page");
        Check(decoded.Entries.Single() == new ManastoneDialogEntry("交出魔石。", "select1", new(205, 250)), "HTML action matches translated visible label");
        Check(decoded.ConfirmButton == new GameUiPoint(340, 450) && decoded.CloseButton == new GameUiPoint(340, 485), "named buttons use actual geometry");
        m.Page(5, 10, 0, "select_quest", "<html><button href='QuestSelect4113'><font>批量兑换</font></button></html>");
        m.Text(Memory.Choice, "批量兑换");
        Check(m.ReadSnapshot().Entries.Single() is { Href: "QuestSelect4113", Point: not null }, "quest menu keeps exact task identity");
        m.Page(4, 5, 4113, "select_quest_reward1", "<html><button font_xml='v3_quest_reward'>基本奖励</button><item itemnameid='188051284' itemcount='1'>魔石袋</item></html>");
        Check(m.ReadSnapshot() is { PageType: 4, Entries.Count: 0, ConfirmButton: not null }, "reward page and item markup retained");
        m.Widget(Memory.Close, "cancel", 200, 275, 80, 20); m.Text(Memory.Close, "取消");
        m.Widget(Memory.Duplicate, "cancel", 315, 10, 15, 15);
        m.Children(Memory.Dialog, Memory.View, Memory.Ok, Memory.Close, Memory.Duplicate);
        Check(m.ReadSnapshot().CloseButton == new GameUiPoint(340, 485), "normal bottom Cancel is distinguished from empty title-bar Cancel");
        m.Text(Memory.Duplicate, "取消");
        Check(m.ReadSnapshot().CloseButton == null && m.ReadSnapshot().ConfirmButton != null, "ambiguous close labels cannot hide a valid reward snapshot");

        m = new Memory();
        m.U(Memory.Choice + 0x28, 1);
        Check(m.ReadSnapshot().Entries.Single().Point == null, "disabled rendered entry is not clickable");
        m.U(Memory.Choice + 0x28, 3);
        m.U(Memory.View + 0x28, 1);
        Check(m.ReadSnapshot().Entries.Single().Point == null, "disabled parent blocks HTML entry");
        m.U(Memory.View + 0x28, 3);
        m.D(Memory.Choice + 0x60, 600);
        Check(m.ReadSnapshot().Entries.Single().Point == null, "entry outside HTML viewport is not clickable");
        m.D(Memory.Choice + 0x60, 30);
        m.D(Memory.Choice + 0x58, double.NaN);
        Reject(m.ReadSnapshot, "malformed geometry is rejected");
        m.D(Memory.Choice + 0x58, 10);
        m.Short = Memory.Dialog + 0x518;
        Reject(m.ReadSnapshot, "incomplete page identity is rejected");
        m.Short = null;
        m.I(Memory.Dialog + 0x4E8, 0);
        Reject(m.ReadSnapshot, "missing NPC identity cannot publish action points");
        m.I(Memory.Dialog + 0x4E8, 2147613646);
        m.Wide(Memory.Dialog + 0x548, "<html><button href='select1'>broken</html>");
        m.Text(Memory.View, "<html><button href='select1'>broken</html>");
        Reject(m.ReadSnapshot, "unterminated button is not an empty valid page");

        m = new Memory();
        m.Text(Memory.View, "<html>different page</html>");
        Reject(m.ReadSnapshot, "rendered view must belong to captured page");
        m = new Memory();
        m.Widget(Memory.Duplicate, "", 10, 80, 150, 20);
        m.Text(Memory.Duplicate, "交出魔石。");
        m.Children(Memory.View, Memory.Choice, Memory.Duplicate);
        Check(m.ReadSnapshot().Entries.Single().Point == null, "ambiguous identical text cannot select a different action");
        m = new Memory();
        var reads = 0;
        var changing = new InventoryInteractionDecoder((a, n) =>
        {
            var data = m.Read(a, n);
            if (a == Memory.Dialog + 0x518 && ++reads > 1) data[8] ^= 1;
            return data;
        });
        Reject(() => changing.ReadManastoneExchange(Memory.Game), "quest changed during capture rejects mixed state");
        m = new Memory();
        m.U(Memory.Game + 0xD63990 + 366 * 8, Memory.Modal);
        m.Widget(Memory.Modal, "checkbox_dialog", 400, 300, 180, 150);
        var blocked = m.ReadSnapshot();
        Check(blocked.OtherModalOpen && blocked.ConfirmButton == null && blocked.CloseButton == null && blocked.Entries.All(e => e.Point == null), "checkbox and normal modal pools block every click");
        m.U(Memory.Game + 0xD63990 + 366 * 8, 0);
        m.U(Memory.Game + 0xD63990 + 27 * 8, Memory.Bag);
        m.Widget(Memory.Bag, "inventory_dialog", 150, 200, 300, 400);
        Check(m.ReadSnapshot().Entries.All(e => e.Point == null) && m.ReadSnapshot().ConfirmButton == null, "overlapping inventory masks controls");
        m.U(Memory.Game + 0xD63990 + 132 * 8, 0);
        Check(m.ReadSnapshot() is { IsOpen: false, NpcServerObjectId: 0, QuestId: 0, Entries.Count: 0 }, "valid absent dialog clears page identity");
        return Task.CompletedTask;
    }

    public static Task PublicationAsync()
    {
        var channel = AionVmmSnapshotChannels.ManastoneExchange;
        Check(channel.ReadPolicy == DmaSnapshotReadPolicy.Stable && channel.MergePolicy == DmaSnapshotMergePolicy.Replace,
            "dialog is one stable atomic publication");
        var store = new DmaStableSnapshotStore(AionVmmSnapshotChannels.Registry);
        var context = new GameApiReadContext("account5", 1, "Aion.bin", "device5");
        var original = new Memory().ReadSnapshot();
        var now = DateTimeOffset.UtcNow;
        store.Resolve("session5", channel, context, OperationResult<ManastoneExchangeSnapshot>.Ok(original), now);
        var held = store.Resolve("session5", channel, context, OperationResult<ManastoneExchangeSnapshot>.Fail("short page read"), now.AddDays(2));
        Check(held.Result.Success && ReferenceEquals(held.Result.Value, original), "failed capture retains exact publication without expiry");
        var other = store.Resolve("session6", channel, context, OperationResult<ManastoneExchangeSnapshot>.Fail("cold start"), now);
        Check(!other.Result.Success, "another account/session cannot receive prior dialog");
        store.ClearSession("session5");
        Check(!store.Resolve("session5", channel, context, OperationResult<ManastoneExchangeSnapshot>.Fail("reset"), now).Result.Success,
            "session reset invalidates prior page");
        return Task.CompletedTask;
    }

    private static void Check(bool value, string reason) { if (!value) throw new Exception(reason); }
    private static void Reject(Func<ManastoneExchangeSnapshot> read, string reason)
    {
        try { read(); } catch (InvalidDataException) { return; }
        throw new Exception(reason);
    }

    private sealed class Memory
    {
        internal const ulong Game = 0x10000000, Dialog = 0x20000000, View = 0x21000000, Choice = 0x22000000,
            Ok = 0x23000000, Close = 0x24000000, Modal = 0x25000000, Duplicate = 0x26000000, Bag = 0x27000000;
        private readonly Dictionary<ulong, byte> _bytes = new();
        internal ulong? Short;
        private ulong _allocation = 0x40000000;
        internal Memory()
        {
            D(Game + 0xDACF00, 1024); D(Game + 0xDACF08, 768);
            U(Game + 0xD63990 + 132 * 8, Dialog);
            Widget(Dialog, "dlg_dialog", 100, 200, 350, 400);
            Widget(View, "html_view", 20, 10, 300, 200);
            Widget(Choice, "", 10, 30, 150, 20);
            Widget(Ok, "ok", 200, 240, 80, 20);
            Widget(Close, "close", 200, 275, 80, 20);
            Children(Dialog, View, Ok, Close); Children(View, Choice);
            U(Dialog + 0x4E0, View); I(Dialog + 0x4E8, 2147613646);
            Page(1, 1003, 4113, "quest_accept_1", "<html><button href=\"select1\">交出魔石。</button></html>");
            Text(Choice, "交出魔石。"); Text(Ok, "确认"); Text(Close, "关闭");
        }
        internal ManastoneExchangeSnapshot ReadSnapshot() => new InventoryInteractionDecoder(Read).ReadManastoneExchange(Game);
        internal byte[] Read(ulong address, int count) => Enumerable.Range(0, address == Short ? count - 1 : count).Select(i => _bytes.GetValueOrDefault(address + (ulong)i)).ToArray();
        internal void Put(ulong address, byte[] value) { for (var i = 0; i < value.Length; i++) _bytes[address + (ulong)i] = value[i]; }
        internal void U(ulong address, ulong value) => Put(address, BitConverter.GetBytes(value));
        internal void I(ulong address, uint value) => Put(address, BitConverter.GetBytes(value));
        internal void D(ulong address, double value) => Put(address, BitConverter.GetBytes(value));
        internal void Wide(ulong address, string value)
        {
            Put(address, new byte[32]);
            if (value.Length < 8) Put(address, Encoding.Unicode.GetBytes(value));
            else { var buffer = _allocation; _allocation += 0x10000; U(address, buffer); Put(buffer, Encoding.Unicode.GetBytes(value)); }
            U(address + 16, (ulong)value.Length); U(address + 24, (ulong)Math.Max(7, value.Length));
        }
        internal void Text(ulong widget, string value)
        {
            U(widget, widget + 0x3000); U(widget + 0x3000 + 632, widget + 0x4000);
            Put(widget + 0x4000, new byte[] { 0x48, 0x8D, 0x81, 0, 0x10, 0, 0, 0xC3 });
            Wide(widget + 0x1000, value);
        }
        internal void Page(uint type, uint page, uint quest, string name, string html)
        {
            I(Dialog + 0x518, type); I(Dialog + 0x51C, page); I(Dialog + 0x520, quest);
            Wide(Dialog + 0x528, name); Wide(Dialog + 0x548, html); Text(View, html);
        }
        internal void Widget(ulong address, string name, double x, double y, double width, double height)
        {
            if (name.Length < 16) Put(address + 8, Encoding.UTF8.GetBytes(name));
            else { U(address + 8, address + 0x800); Put(address + 0x800, Encoding.UTF8.GetBytes(name)); }
            U(address + 24, (ulong)name.Length); U(address + 32, (ulong)Math.Max(15, name.Length)); U(address + 40, 3);
            D(address + 0x58, x); D(address + 0x60, y); D(address + 0x68, width); D(address + 0x70, height);
            Text(address, "");
        }
        internal void Children(ulong parent, params ulong[] children)
        {
            var head = parent + 0x6000; U(parent + 0x238, head); U(head, children.Length == 0 ? head : head + 0x30);
            for (var i = 0; i < children.Length; i++)
            {
                var link = head + (ulong)(i + 1) * 0x30;
                U(link, i == children.Length - 1 ? head : link + 0x30); U(link + 0x10, children[i]);
            }
        }
    }
}
