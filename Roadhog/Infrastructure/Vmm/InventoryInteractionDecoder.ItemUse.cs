using Roadhog.Core.Model;

namespace Roadhog.Infrastructure.Vmm;

internal sealed partial class InventoryInteractionDecoder
{
    private InventoryItemUseQuantity? ReadInventoryItemUseQuantity(ulong window)
    {
        if (Name(window) != "item_count_dialog") return null;
        var controls = Nodes(window);
        var prompt = controls.SingleOrDefault(n => n.Name == "ipc_message");
        if (prompt is null || AuctionText(prompt.Address) != "需要解除几个？") return null;

        // The inventory dialog carries the selected instance in both message arguments.
        var fields = Guard(window + 0x4E8, 0x24);
        var instance = BitConverter.ToUInt32(fields);
        Require(instance != 0 && instance == BitConverter.ToUInt32(fields, 0x20), "Mismatched item-use quantity identity.");
        var (quantity, maximum) = ReadCountDialogQuantities(window);

        GameUiPoint? Button(string name)
        {
            var point = controls.SingleOrDefault(n => n.Name == name)?.Point(this);
            var bounds = controls[0];
            return bounds.Enabled && point is not null && point.X >= bounds.X && point.X < bounds.X + bounds.W &&
                point.Y >= bounds.Y && point.Y < bounds.Y + bounds.H ? point : null;
        }

        return new(instance, quantity, maximum, Button("all"), Button("ok"));
    }

    private (ulong Quantity, ulong Maximum) ReadCountDialogQuantities(ulong window)
    {
        var quantity = GU(window + 1504);
        var maximum = GU(window + 1520);
        Require(maximum > 0 && quantity <= maximum, "Invalid item count dialog quantity.");
        return (quantity, maximum);
    }
}
