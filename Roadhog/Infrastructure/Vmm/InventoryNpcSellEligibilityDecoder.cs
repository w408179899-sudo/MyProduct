namespace Roadhog.Infrastructure.Vmm;

// aion202609091125.json: NPC inventory dimming 6207A0 -> 3A1080.
// Read the same item predicate; never call client code. Guard the predicate and item identity.
internal static class InventoryNpcSellEligibilityDecoder
{
    public static bool Read(Func<ulong, int, byte[]> read, ulong item, uint instanceId, uint templateId)
    {
        byte[] Bytes(ulong address, int length)
        {
            var value = read(address, length);
            if (value.Length != length) throw new InvalidDataException("Incomplete NPC sale eligibility capture.");
            return value;
        }
        var first = Bytes(item, 216);
        bool Identity(byte[] bytes) => BitConverter.ToUInt32(bytes, 8) == instanceId &&
            BitConverter.ToUInt32(bytes, 12) == templateId && instanceId != 0 && templateId != 0;
        if (!Identity(first)) throw new InvalidDataException("NPC sale eligibility identity mismatch.");
        var weapon = templateId >= 100000000 && templateId - 100000000 <= 0x2F4D5F;
        var weaponState = weapon ? Bytes(item + 1232, 4) : Array.Empty<byte>();
        var second = Bytes(item, 216);
        if (!Identity(second) || !first.AsSpan(56, 4).SequenceEqual(second.AsSpan(56, 4)) || first[120] != second[120] ||
            !first.AsSpan(170, 4).SequenceEqual(second.AsSpan(170, 4)) || first[203] != second[203] || first[207] != second[207] ||
            !first.AsSpan(212, 4).SequenceEqual(second.AsSpan(212, 4)) ||
            weapon && !weaponState.AsSpan().SequenceEqual(Bytes(item + 1232, 4)))
            throw new InvalidDataException("NPC sale eligibility changed during capture.");
        var associatedTemplate = BitConverter.ToUInt32(first, 170);
        return BitConverter.ToUInt32(first, 56) == 0 && (first[120] & 4) != 0 &&
            (associatedTemplate == 0 || associatedTemplate == templateId || first[207] != 3) &&
            (first[203] == 0 || BitConverter.ToUInt32(first, 212) == 0) &&
            (!weapon || BitConverter.ToUInt32(weaponState, 0) == 0);
    }
}
