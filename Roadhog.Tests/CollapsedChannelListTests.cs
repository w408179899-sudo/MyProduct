using System.Text;
using Roadhog.Core.Api;
using Roadhog.Core.Common;
using Roadhog.Core.Model;
using Roadhog.Infrastructure.Vmm;

internal static class CollapsedChannelListTests
{
    public static Task CapturedCollapsedGeometryAsync()
    {
        var memory = new ChannelDialogMemory();
        var snapshot = memory.Decode();
        Require(snapshot.DialogOpen && !snapshot.DropdownOpen, "collapsed list must publish the open channel dialog");
        Require(snapshot.SelectedChannelNumber == 1, "collapsed list must retain the real selected channel");
        Require(snapshot.DropdownButton == new ChannelUiPoint(956, 539) && snapshot.MoveButton == new ChannelUiPoint(899, 573),
            "visible buttons must remain usable while the list has negative client height");
        Require(snapshot.Options.Count == 3 && snapshot.Options.All(option => option.Point is null),
            "collapsed options must have no click coordinates");
        Require(memory.HiddenGeometryReads == 0, "hidden list geometry must not participate in publication");

        memory.HiddenBoundsMissing = true;
        Require(memory.Decode().DialogOpen && memory.HiddenGeometryReads == 0,
            "control traversal must skip hidden bounds as well as direct list geometry");
        return Task.CompletedTask;
    }

    public static Task ExpandSelectAndCollapseAsync()
    {
        var memory = new ChannelDialogMemory { Expanded = true };
        var expanded = memory.Decode();
        Require(expanded.DropdownOpen && expanded.Options.All(option => option.Point is not null),
            "visible expanded list must expose validated row coordinates");
        Require(expanded.Options[2].Point == new ChannelUiPoint(895, 584), "third row must use actual scaled list geometry");
        memory.Expanded = false;
        memory.Selected = 3;
        var collapsed = memory.Decode();
        Require(collapsed.DialogOpen && !collapsed.DropdownOpen && collapsed.SelectedChannelNumber == 3,
            "selection must publish after the list collapses back to negative client height");
        Require(collapsed.MoveButton is not null && collapsed.Options.All(option => option.Point is null),
            "post-selection state must permit move without retaining old row coordinates");
        Require(memory.HiddenGeometryReads == 0, "collapsed geometry must remain unread after selection");
        return Task.CompletedTask;
    }

    public static Task VisibleFaultsAndVisibilityChangesAsync()
    {
        var memory = new ChannelDialogMemory { Expanded = true, InvalidExpandedClient = true };
        var error = Rejected(memory.Decode);
        Require(error.Contains("Invalid UI rectangle.", StringComparison.Ordinal), "visible negative dimensions must still be rejected");
        memory.InvalidExpandedClient = false;
        memory.HideExpandedList = true;
        Rejected(memory.Decode);
        memory.HideExpandedList = false;
        memory.ChangeVisibilityDuringRead = true;
        Rejected(memory.Decode); // Visibility changed during traversal; no mixed tree may publish.
        memory.ChangeVisibilityDuringRead = false;
        Require(memory.Decode().Options[2].Point is not null, "a later coherent expanded read must recover immediately");
        return Task.CompletedTask;
    }

    public static Task StablePublicationAsync()
    {
        var memory = new ChannelDialogMemory { DialogOpen = false };
        var store = new DmaStableSnapshotStore(AionVmmSnapshotChannels.Registry);
        var channel = AionVmmSnapshotChannels.ChannelSwitchUi;
        var context = new GameApiReadContext("test", 1, "Aion.bin", "fake");
        ChannelSwitchUiSnapshot Publish()
        {
            OperationResult<ChannelSwitchUiSnapshot> observed;
            try { observed = OperationResult<ChannelSwitchUiSnapshot>.Ok(memory.Decode()); }
            catch (InvalidDataException ex) { observed = OperationResult<ChannelSwitchUiSnapshot>.Fail(ex.Message); }
            return store.Resolve("session", channel, context, observed, DateTimeOffset.Now).Result.Value!;
        }

        Require(!Publish().DialogOpen, "baseline must reproduce the last closed-dialog snapshot");
        memory.DialogOpen = true;
        var collapsed = Publish();
        Require(collapsed.DialogOpen && !collapsed.DropdownOpen, "collapsed negative-height list must replace the old closed snapshot");
        memory.Expanded = true;
        memory.InvalidExpandedClient = true;
        Require(ReferenceEquals(collapsed, Publish()), "a real expanded-geometry failure must preserve the last official snapshot");
        memory.InvalidExpandedClient = false;
        Require(Publish().DropdownOpen, "first valid expanded capture must publish immediately");
        return Task.CompletedTask;
    }

    private static string Rejected(Func<ChannelSwitchUiSnapshot> read)
    {
        try { read(); }
        catch (InvalidDataException ex) { return ex.Message; }
        throw new InvalidOperationException("invalid or inconsistent visible geometry must not publish");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}

// Real decoder input; the collapsed client bytes are from script/4 at 2026-09-15 16:52:40.
internal sealed class ChannelDialogMemory
{
    private const ulong GameBase = 0x10000000, Dialog = 0x20000000, Combo = 0x20001000;
    private const ulong List = 0x20002000, Drop = 0x20003000, Move = 0x20004000, Entries = 0x30000000;
    private const string CollapsedClientHex = "34333333333303403433333333330340CDCCCCCCCCCC604034333333333313C0";
    private readonly Dictionary<ulong, byte[]> _memory = new();
    private ulong _nextNode;
    private int _listVisibilityReads;
    internal bool DialogOpen = true, Expanded, InvalidExpandedClient, HideExpandedList, ChangeVisibilityDuringRead, HiddenBoundsMissing;
    internal int Selected = 1, HiddenGeometryReads;
    internal ChannelUiPoint Cursor = new(500, 300);

    internal ChannelSwitchUiSnapshot Decode()
    {
        _memory.Clear();
        _nextNode = 0x40000000;
        _listVisibilityReads = 0;
        D(GameBase + 0xDACF00, 1024);
        D(GameBase + 0xDACF08, 768);
        I(GameBase + 0xDAD020, Cursor.X);
        I(GameBase + 0xDAD024, Cursor.Y);
        foreach (var slot in new[] { 17, 376, 377, 227 }) U(GameBase + 0xD63990 + (ulong)slot * 8, 0);
        U(GameBase + 0xD63990 + 227 * 8, Dialog);
        Widget(Dialog, "channel_dialog", DialogOpen);
        Widget(Combo, "channel_list", true);
        Widget(List, "item_list", Expanded && !HideExpandedList);
        Widget(Drop, "drop_btn", true);
        Widget(Move, "ok", true);
        Rect(Drop, 0x58, 946, 529, 20, 20);
        Rect(Move, 0x58, 889, 563, 20, 20);
        Rect(List, 0x58, 825.4, 549.1, 139.2, Expanded ? 43.8 : 0);
        if (Expanded && !InvalidExpandedClient) Rect(List, 0x78, 2.4, 2.4, 134.4, 39);
        else _memory[List + 0x78] = Convert.FromHexString(CollapsedClientHex);
        if (HiddenBoundsMissing && !Expanded) _memory.Remove(List + 0x58);
        U(Dialog + 0x4D8, Combo);
        U(Combo + 0x2F8, List);
        I(Combo + 0x30, Expanded ? 1 : 0);
        I(Combo + 0x308, 3);
        I(List + 0x300, Selected - 1);
        U(List + 0x2E8, Entries);
        U(List + 0x2F0, Entries + 3 * 96);
        D(List + 0x350, 0);
        D(List + 0x258, 2.4);
        D(List + 0x360, 0);
        D(List + 0x308, 13);
        Children(Dialog, Combo, Move);
        // Put the hidden list first to reproduce traversal before locating the visible drop button.
        Children(Combo, List, Drop);
        for (var index = 0; index < 3; index++)
        {
            var entry = Entries + (ulong)index * 96;
            var label = (index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
            _memory[entry] = Encoding.Unicode.GetBytes(label);
            U(entry + 16, 1);
            U(entry + 24, 7);
            I(entry + 88, 1);
        }
        return new ChannelSwitchUiDecoder(Read).Read(GameBase);
    }

    private byte[] Read(ulong address, int size)
    {
        if (address == List + 0x58 || address == List + 0x78)
        {
            if (!Expanded || HideExpandedList) HiddenGeometryReads++;
        }
        if (address == List + 0x28 && ChangeVisibilityDuringRead && ++_listVisibilityReads > 1)
            return BitConverter.GetBytes(2UL);
        if (!_memory.TryGetValue(address, out var bytes) || bytes.Length != size)
            throw new InvalidDataException($"fixture field unavailable: 0x{address:X}");
        return bytes;
    }

    private void Widget(ulong address, string name, bool visible)
    {
        _memory[address + 8] = Encoding.UTF8.GetBytes(name);
        U(address + 0x18, (ulong)name.Length);
        U(address + 0x20, 15);
        U(address + 0x28, visible ? 3UL : 2UL);
        Rect(address, 0x58, 0, 0, 1024, 768);
        Rect(address, 0x78, 0, 0, 1024, 768);
        U(address + 0x238, 0);
    }

    private void Children(ulong parent, params ulong[] children)
    {
        var head = _nextNode;
        _nextNode += 0x100;
        U(parent + 0x238, head);
        var previous = head;
        foreach (var child in children)
        {
            var node = _nextNode;
            _nextNode += 0x100;
            U(previous, node);
            U(node + 0x10, child);
            previous = node;
        }
        U(previous, head);
    }

    private void Rect(ulong widget, ulong offset, params double[] values) =>
        _memory[widget + offset] = values.SelectMany(BitConverter.GetBytes).ToArray();
    private void U(ulong address, ulong value) => _memory[address] = BitConverter.GetBytes(value);
    private void I(ulong address, int value) => _memory[address] = BitConverter.GetBytes(value);
    private void D(ulong address, double value) => _memory[address] = BitConverter.GetBytes(value);
}
