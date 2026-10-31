using Roadhog.Core.Model;

namespace Roadhog.Infrastructure.Vmm;

// Documented in LOOT_INTERACTION_ADAPTER.md; raw values stay inside this adapter.
internal sealed class LootInteractionDecoder(Func<ulong, int, byte[]> read)
{
    internal const ulong CorpseIdRva = 0xD65CE4;
    internal const ulong WindowRva = 0xD64068;

    internal LootInteractionRead Capture(ulong module)
    {
        uint? corpse = null;
        bool? window = null, confirmation = null;
        string? error = null;
        try { corpse = UInt32(module + CorpseIdRva); }
        catch (Exception ex) { error = ex.Message; }
        try
        {
            var address = UInt64(module + WindowRva);
            if (address == 0) { window = false; confirmation = false; }
            else
            {
                if (address < 0x10000 || address >= 0x0000800000000000 || (address & 7) != 0)
                    throw new InvalidDataException("Invalid loot window address.");
                var flags = UInt64(address + 0x28);
                var index = unchecked((int)UInt32(address + 0x510));
                if (index < -1) throw new InvalidDataException("Invalid loot confirmation index.");
                if (UInt64(module + WindowRva) != address)
                { window = null; confirmation = null; throw new InvalidDataException("Loot window changed during capture."); }
                window = (flags & 1) != 0;
                confirmation = index >= 0;
            }
        }
        catch (Exception ex) { error = ex.Message; }
        return new(corpse, window, confirmation, error);
    }

    private byte[] Bytes(ulong address, int length)
    {
        var bytes = read(address, length);
        if (bytes.Length != length) throw new InvalidDataException("Incomplete loot interaction read.");
        return bytes;
    }

    private uint UInt32(ulong address)
    {
        var value = BitConverter.ToUInt32(Bytes(address, 4));
        if (value is 0xCDCDCDCD or 0xCCCCCCCC) throw new InvalidDataException("Poisoned loot interaction value.");
        return value;
    }

    private ulong UInt64(ulong address)
    {
        var value = BitConverter.ToUInt64(Bytes(address, 8));
        if (value is 0xCDCDCDCDCDCDCDCD or 0xCCCCCCCCCCCCCCCC) throw new InvalidDataException("Poisoned loot interaction pointer/flags.");
        return value;
    }
}

internal sealed record LootInteractionRead(uint? CorpseId, bool? WindowOpen, bool? ConfirmationOpen, string? Error)
{
    public bool HasFields => CorpseId.HasValue || WindowOpen.HasValue || ConfirmationOpen.HasValue;
    public LootInteractionSnapshot? Merge(LootInteractionSnapshot? previous) =>
        (CorpseId ?? previous?.CorpseServerObjectId) is { } id &&
        (WindowOpen ?? previous?.WindowOpen) is { } window &&
        (ConfirmationOpen ?? previous?.ConfirmationOpen) is { } confirmation
            ? new(id, window, confirmation) : null;
}
