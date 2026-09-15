using System.Globalization;
using System.Text;
using System.Text.Json;
using Roadhog.Infrastructure.Vmm;

internal static class ChannelUiDiagnosticsTests
{
    public static Task RejectedRectanglesAsync()
    {
        foreach (var values in new[]
        {
            new[] { 12d, 34d, -1d, 20d },
            new[] { 12d, 34d, 20d, -1d },
            new[] { double.NaN, 34d, 20d, 20d },
            new[] { 12d, double.PositiveInfinity, 20d, 20d },
            new[] { 32769d, 34d, 20d, 20d }
        })
        {
            var fixture = new UiMemory();
            var raw = fixture.Rect(UiMemory.Start, 0x58, values);
            using var error = DecodeError(fixture);
            var detail = error.RootElement;
            Require(detail.GetProperty("widgetName").GetString() == "start_dialog", "must name the rejected widget");
            Require(detail.GetProperty("widgetAddress").GetString() == "0x20000000", "must identify the widget address");
            Require(detail.GetProperty("rectAddress").GetString() == "0x20000058", "must identify the field address");
            Require(detail.GetProperty("rectOffset").GetString() == "0x58", "must identify the rectangle layout");
            Require(detail.GetProperty("context").GetString() == "start_menu.parent_bounds", "must identify the traversal purpose");
            Require(detail.GetProperty("rawHex").GetString() == Convert.ToHexString(raw), "must preserve the original failed bytes");
            var fields = new[] { "x", "y", "width", "height" };
            for (var i = 0; i < fields.Length; i++)
                Require(detail.GetProperty(fields[i]).GetString() == values[i].ToString("R", CultureInfo.InvariantCulture),
                    "must preserve invalid and non-finite values without breaking JSON");
            Require(detail.GetProperty("widgetState").GetProperty("visible").GetBoolean(), "must capture visibility");
            Require(detail.GetProperty("dialogState").ValueKind == JsonValueKind.Null, "absent dialog must stay absent");
            Require(detail.GetProperty("metadataSource").GetString() == "best_effort_after_rectangle_failure",
                "extra reads must not be mistaken for the rejected geometry capture");
        }

        // A successful capture still accepts the same geometry and publishes the same menu point.
        var valid = new UiMemory();
        var snapshot = new ChannelSwitchUiDecoder(valid.Read).Read(UiMemory.GameBase);
        Require(snapshot.MenuButton is { X: 110, Y: 220 } && !snapshot.DialogOpen,
            "diagnostics must not change valid UI geometry or visibility");
        return Task.CompletedTask;
    }

    public static Task VisibleWidgetAndMetadataFailureAsync()
    {
        foreach (var expanded in new[] { 0, 1 })
        {
            var fixture = new UiMemory();
            fixture.AddVisibleInvalidChild(expanded);
            using var error = DecodeError(fixture);
            var detail = error.RootElement;
            Require(detail.GetProperty("context").GetString() == "menu_service.child_bounds", "must locate the failing traversal");
            Require(detail.GetProperty("widgetName").GetString() == "visible_item", "must identify the invalid visible child");
            Require(detail.GetProperty("widgetState").GetProperty("visible").GetBoolean(), "must expose the visible state");
            Require(detail.GetProperty("dialogState").GetProperty("visible").GetBoolean(), "must capture an open dialog even before traversing it");
            Require(detail.GetProperty("dropdownState").GetProperty("expandedRaw").GetInt32() == expanded,
                "must distinguish collapsed and expanded dropdowns");
        }

        var broken = new UiMemory();
        var bytes = broken.Rect(UiMemory.Start, 0x58, new[] { 10d, 20d, -1d, 40d });
        broken.FailDiagnosticMetadata = true;
        using var failedMetadata = DecodeError(broken);
        var details = failedMetadata.RootElement;
        Require(details.GetProperty("widgetName").GetProperty("error").GetString() == "diagnostic metadata unavailable",
            "name failure must remain diagnostic-only");
        Require(details.GetProperty("widgetState").GetProperty("error").GetString() == "diagnostic metadata unavailable",
            "flag failure must remain diagnostic-only");
        Require(details.GetProperty("rawHex").GetString() == Convert.ToHexString(bytes),
            "metadata errors must not hide the original rectangle failure");
        return Task.CompletedTask;
    }

    private static JsonDocument DecodeError(UiMemory fixture)
    {
        try { new ChannelSwitchUiDecoder(fixture.Read).Read(UiMemory.GameBase); }
        catch (InvalidDataException ex)
        {
            const string prefix = "Invalid UI rectangle. ";
            Require(ex.Message.StartsWith(prefix, StringComparison.Ordinal), "must retain the original error category");
            return JsonDocument.Parse(ex.Message[prefix.Length..]);
        }
        throw new InvalidOperationException("invalid geometry must still be rejected");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class UiMemory
    {
        internal const ulong GameBase = 0x10000000, Start = 0x20000000, Button = 0x20001000;
        private readonly Dictionary<ulong, byte[]> _memory = new();
        private readonly Dictionary<ulong, int> _reads = new();
        internal bool FailDiagnosticMetadata;

        internal UiMemory()
        {
            _memory[GameBase + 0xDACF00] = BitConverter.GetBytes(1024d);
            _memory[GameBase + 0xDACF08] = BitConverter.GetBytes(768d);
            _memory[GameBase + 0xDAD020] = BitConverter.GetBytes(10);
            _memory[GameBase + 0xDAD024] = BitConverter.GetBytes(20);
            foreach (var slot in new[] { 17, 376, 377, 227 }) Slot(slot, 0);
            Slot(17, Start);
            Widget(Start, "start_dialog", 3);
            Widget(Button, "start_button", 3);
            U(Start + 0x4D8, Button);
            Rect(Start, 0x58, new[] { 100d, 200d, 20d, 40d });
        }

        internal byte[] Read(ulong address, int size)
        {
            _reads.TryGetValue(address, out var count);
            _reads[address] = count + 1;
            if (FailDiagnosticMetadata && count > 0 && (address == Start + 0x18 || address == Start + 0x28))
                throw new InvalidDataException("diagnostic metadata unavailable");
            var bytes = _memory[address];
            Require(bytes.Length == size, "fixture must match the requested field size");
            return bytes;
        }

        internal byte[] Rect(ulong widget, ulong offset, double[] values) =>
            _memory[widget + offset] = values.SelectMany(BitConverter.GetBytes).ToArray();

        internal void AddVisibleInvalidChild(int expanded)
        {
            const ulong main = 0x30000000, head = 0x40000000, node = 0x40001000, child = 0x50000000;
            const ulong dialog = 0x60000000, combo = 0x60001000;
            Slot(376, main);
            Widget(main, "main_menu", 3);
            _memory[main + 0x3C8] = BitConverter.GetBytes(17);
            U(main + 0x518, Button);
            U(main + 0x238, head);
            U(head, node);
            U(node + 0x10, child);
            U(node, head);
            Widget(child, "visible_item", 3);
            Rect(child, 0x58, new[] { 0d, 0d, -1d, 20d });
            Slot(227, dialog);
            Widget(dialog, "channel_dialog", 3);
            Widget(combo, "channel_list", 3);
            U(dialog + 0x4D8, combo);
            _memory[combo + 0x30] = BitConverter.GetBytes(expanded);
        }

        private void Slot(int slot, ulong widget) => U(GameBase + 0xD63990 + (ulong)slot * 8, widget);
        private void U(ulong address, ulong value) => _memory[address] = BitConverter.GetBytes(value);

        private void Widget(ulong address, string name, ulong flags)
        {
            var bytes = Encoding.UTF8.GetBytes(name);
            Require(bytes.Length < 16, "fixture widget uses an inline name");
            _memory[address + 8] = bytes;
            U(address + 0x18, (ulong)bytes.Length);
            U(address + 0x20, 15);
            U(address + 0x28, flags);
            Rect(address, 0x58, new[] { 0d, 0d, 20d, 40d });
            Rect(address, 0x78, new[] { 0d, 0d, 20d, 40d });
        }
    }
}
