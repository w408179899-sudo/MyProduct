using Roadhog.Core.Model;

namespace Roadhog.Infrastructure.Vmm;

/// <summary>
/// Read-only Game.dll adapter, layout exported 2026-09-09 and verified on account 2.
/// Reads one atomic binding set; a short read or concurrent slot/page change rejects the capture.
/// No keyboard settings are inferred: input conventions belong to SkillKeyBindings.
/// </summary>
internal sealed class QuickbarDecoder(Func<ulong, int, byte[]> read)
{
    public QuickbarSnapshot Read(ulong module)
    {
        var guards = new List<(ulong Address, byte[] Bytes)>();
        byte[] Capture(ulong address, int length)
        {
            var bytes = read(address, length);
            if (bytes.Length != length) throw new InvalidDataException("Incomplete quickbar read.");
            guards.Add((address, bytes));
            return bytes;
        }
        ulong Pointer(ulong address)
        {
            var value = BitConverter.ToUInt64(Capture(address, 8));
            if (value < 0x10000 || value >= 0x0000800000000000) throw new InvalidDataException("Invalid quickbar pointer.");
            return value;
        }
        var page = BitConverter.ToInt32(Capture(module + 0xD4AE0C, 4));
        if (page is < 0 or >= 10) throw new InvalidDataException("Invalid quickbar page.");
        var table = Capture(module + 0xD61260 + (ulong)page * 768, 384);
        var dialogIds = Capture(module + 0x6E2180, 8);
        var slots = new List<QuickbarSlotSnapshot>(24);
        for (var bar = 0; bar < 2; bar++)
        {
            var dialogId = BitConverter.ToUInt32(dialogIds, bar * 4);
            if (dialogId > 0x19D) throw new InvalidDataException("Invalid quickbar dialog.");
            var panel = Pointer(module + 0xD63990 + dialogId * 8);
            if (BitConverter.ToUInt32(Capture(panel + 1344, 4)) != bar) throw new InvalidDataException("Unexpected quickbar kind.");
            var pointers = Capture(panel + 1240, 96);
            for (var slot = 0; slot < 12; slot++)
            {
                var control = BitConverter.ToUInt64(pointers, slot * 8);
                if (control < 0x10000 || control >= 0x0000800000000000) throw new InvalidDataException("Invalid quickbar slot.");
                var value = Capture(control + 952, 8);
                var id = BitConverter.ToUInt32(value);
                var type = BitConverter.ToUInt32(value, 4);
                var offset = (bar * 12 + slot) * 16;
                var tableType = BitConverter.ToUInt32(table, offset);
                var tableId = BitConverter.ToUInt32(table, offset + 4);
                if (type > 55 || type != tableType || type == 21 && (id == 0 || id != tableId))
                    throw new InvalidDataException("Quickbar table and controls disagree.");
                slots.Add(new((SkillQuickbar)bar, slot, type, type == 21 ? id : 0));
            }
        }
        foreach (var guard in guards)
            if (!guard.Bytes.SequenceEqual(read(guard.Address, guard.Bytes.Length)))
                throw new InvalidDataException("Quickbar changed during capture.");
        return new(page, slots.AsReadOnly());
    }
}
