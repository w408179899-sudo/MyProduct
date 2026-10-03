using System.Text;

namespace Roadhog.Infrastructure.Vmm;

internal enum SkillOpportunityActorVerification { Complete, Incomplete, Changed }

internal sealed record SkillOpportunityActorCapture(
    ulong Actor, ulong Entity, uint ObjectType, uint ServerObjectId,
    IReadOnlyList<(ulong Address, byte[] Bytes)> Guards)
{
    public SkillOpportunityActorVerification Verify(Func<IReadOnlyList<(ulong Address, int Size)>, IReadOnlyList<byte[]>> readMany)
    {
        var requests = Guards.Select(g => (g.Address, Size: g.Bytes.Length)).ToArray();
        var after = readMany(requests);
        if (after.Count != Guards.Count || Guards.Where((guard, i) => after[i] is null || after[i].Length != guard.Bytes.Length).Any())
            return SkillOpportunityActorVerification.Incomplete;
        return Guards.Select((guard, i) => after[i].AsSpan().SequenceEqual(guard.Bytes)).All(equal => equal)
            ? SkillOpportunityActorVerification.Complete : SkillOpportunityActorVerification.Changed;
    }
}

/// <summary>New-channel-only batched form of the existing actor candidate algorithm.</summary>
internal sealed class SkillOpportunityActorResolver(
    Func<ulong, int, byte[]> read,
    Func<IReadOnlyList<(ulong Address, int Size)>, IReadOnlyList<byte[]>> readMany)
{
    private static bool Pointer(ulong value) => value >= 0x10000 && value < 0x800000000000;
    private byte[] Bytes(ulong address, int size)
    {
        if (!Pointer(address)) return Array.Empty<byte>();
        try { return read(address, size); }
        catch (Exception ex) when (ex is IOException or InvalidDataException) { return Array.Empty<byte>(); }
    }

    public SkillOpportunityActorCapture? Read(ulong entity)
    {
        var path = new List<(ulong Address, byte[] Bytes)>();
        ulong Ptr(ulong address)
        {
            var bytes = Bytes(address, 8);
            if (bytes.Length != 8) return 0;
            path.Add((address, bytes));
            return BitConverter.ToUInt64(bytes);
        }
        var vtable = Ptr(entity);
        var function = Ptr(vtable + 0xB8);
        var code = Bytes(function, 16);
        if (code.Length == 16)
        {
            ulong offset = 0;
            if (code[0] == 0x48 && code[1] == 0x8B && code[2] == 0x81) offset = BitConverter.ToUInt32(code, 3);
            else if (code[0] == 0x48 && code[1] == 0x8B && code[2] == 0x41) offset = code[3];
            if (offset != 0)
            {
                path.Add((function, code));
                var manager = Ptr(entity + offset);
                var primary = Scan(manager, 0x400, entity, path);
                if (primary is not null) return primary;
            }
        }
        var direct = Bytes(entity, 0x800);
        var directActor = Scan(entity, 0x800, entity, Array.Empty<(ulong, byte[])>(), direct);
        if (directActor is not null) return directActor;
        if (direct.Length != 0x800) return null;
        for (var offset = 0; offset < 0x800; offset += 8)
        {
            var pointer = BitConverter.ToUInt64(direct, offset);
            if (!Pointer(pointer)) continue;
            var nestedPath = new[] { (Address: entity + (ulong)offset, Bytes: direct.AsSpan(offset, 8).ToArray()) };
            var nested = Scan(pointer, 0x300, entity, nestedPath);
            if (nested is not null) return nested;
        }
        return null;
    }

    private SkillOpportunityActorCapture? Scan(
        ulong region, int size, ulong entity, IReadOnlyList<(ulong Address, byte[] Bytes)> path, byte[]? block = null)
    {
        block ??= Bytes(region, size);
        if (!Pointer(region) || block.Length != size) return null;
        var candidates = Enumerable.Range(0, size / 8)
            .Select(index => (Index: index, Actor: BitConverter.ToUInt64(block, index * 8)))
            .Where(item => Pointer(item.Actor)).GroupBy(item => item.Actor).Select(group => group.First()).ToArray();
        if (candidates.Length == 0) return null;
        var requests = candidates.Select(item => (Address: item.Actor + 8, Size: 40)).ToArray();
        var values = readMany(requests);
        if (values.Count != candidates.Length) return null;
        var matches = new List<(int Index, ulong Actor, uint Type, uint Server, byte[] Identity)>();
        for (var i = 0; i < candidates.Length; i++)
        {
            var bytes = values[i];
            if (bytes is null || bytes.Length != 40 || BitConverter.ToUInt64(bytes) != entity) continue;
            var type = BitConverter.ToUInt32(bytes, 24);
            if (type is 0 or > 32) continue;
            matches.Add((candidates[i].Index, candidates[i].Actor, type, BitConverter.ToUInt32(bytes, 36), bytes));
        }
        if (matches.Count == 0) return null;
        var names = readMany(matches.Select(item => (Address: item.Actor + 0x42, Size: 128)).ToArray());
        if (names.Count != matches.Count) return null;
        var best = -1;
        var bestScore = -1;
        for (var i = 0; i < matches.Count; i++)
        {
            var nameBytes = names[i];
            var hasName = nameBytes is { Length: 128 } &&
                !string.IsNullOrWhiteSpace(Encoding.Unicode.GetString(nameBytes).Split('\0')[0]);
            var score = 60 + (matches[i].Server != 0 ? 10 : 0) + (hasName ? 10 : 0);
            if (score > bestScore) { best = i; bestScore = score; }
        }
        if (best < 0 || bestScore < 60) return null;
        var selected = matches[best];
        var guards = path.ToList();
        guards.Add((region + (ulong)selected.Index * 8, block.AsSpan(selected.Index * 8, 8).ToArray()));
        guards.Add((selected.Actor + 8, selected.Identity.AsSpan(0, 8).ToArray()));
        guards.Add((selected.Actor + 0x20, selected.Identity.AsSpan(24, 4).ToArray()));
        guards.Add((selected.Actor + 0x2C, selected.Identity.AsSpan(36, 4).ToArray()));
        return new(selected.Actor, entity, selected.Type, selected.Server, guards.AsReadOnly());
    }
}
