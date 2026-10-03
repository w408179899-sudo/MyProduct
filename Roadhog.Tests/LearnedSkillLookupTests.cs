using Roadhog.Infrastructure.Vmm;

internal static class LearnedSkillLookupTests
{
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    public static Task OrderedLookupAsync()
    {
        var memory = new Memory(new uint[] { 985, 1211, 1227, 1268, 1271, 1333, 1335 });
        var decoder = new AionVmmGameApi.LearnedSkillExactLookupDecoder(memory.Read);
        Check(decoder.TryFind(memory.Header, 1227, out var rankOne, out _) && rankOne == memory.Nodes[1227],
            "exact learned lookup returns the actual requested rank I node");
        Check(decoder.TryFind(memory.Header, 1271, out var rankThree, out _) && rankThree == memory.Nodes[1271] && rankThree != rankOne,
            "another rank remains a distinct exact outer ID");
        Check(decoder.TryFind(memory.Header, 1100, out var missing, out _) && missing == 0,
            "a missing exact ID does not become the nearest ID or display rank");
        Check(decoder.Verify(decoder.Guards.Select(guard => memory.Read(guard.Address, guard.Bytes.Length)).ToArray(), out _),
            "unchanged ordered capture guards verify successfully");

        var large = new Memory(Enumerable.Range(1, 1023).Select(value => (uint)value).ToArray());
        var bounded = new AionVmmGameApi.LearnedSkillExactLookupDecoder(large.Read);
        Check(bounded.TryFind(large.Header, 1023, out var last, out _) && last == large.Nodes[1023] && bounded.NodeReadCount == 10,
            "one selected ID reads its ten-node path instead of enumerating 1023 outer nodes");
        var previousReads = bounded.NodeReadCount;
        Check(bounded.TryFind(large.Header, 1023, out _, out _) && bounded.NodeReadCount == previousReads,
            "repeated IDs share only this capture's verified node blocks");
        var nextCapture = new AionVmmGameApi.LearnedSkillExactLookupDecoder(large.Read);
        Check(nextCapture.TryFind(large.Header, 1023, out _, out _) && nextCapture.NodeReadCount == 10,
            "a new capture never reuses a previous frame's pointers");
        return Task.CompletedTask;
    }

    public static Task FaultsAsync()
    {
        var memory = new Memory(new uint[] { 100, 200, 300 });
        var decoder = new AionVmmGameApi.LearnedSkillExactLookupDecoder(memory.Read);
        Check(!decoder.TryFind(memory.Header, 0, out _, out _), "zero is not a learned skill identity");
        memory.Fault = "short";
        Check(!new AionVmmGameApi.LearnedSkillExactLookupDecoder(memory.Read).TryFind(memory.Header, 300, out _, out _),
            "short search-node reads cannot fabricate a missing or ready skill");
        memory.Fault = "io";
        Check(!new AionVmmGameApi.LearnedSkillExactLookupDecoder(memory.Read).TryFind(memory.Header, 300, out _, out _),
            "failed search reads fail the raw capture");
        memory.Fault = null;
        var root = memory.Nodes[200];
        BitConverter.GetBytes(root).CopyTo(memory.Blocks[root], 0x10);
        Check(!new AionVmmGameApi.LearnedSkillExactLookupDecoder(memory.Read).TryFind(memory.Header, 300, out _, out _),
            "a cyclic right branch is rejected");

        memory = new Memory(new uint[] { 100, 200, 300 });
        memory.Blocks[memory.Header][0x19] = 0;
        Check(!new AionVmmGameApi.LearnedSkillExactLookupDecoder(memory.Read).TryFind(memory.Header, 300, out _, out _),
            "a non-sentinel header is rejected");
        memory = new Memory(new uint[] { 100, 200, 300 });
        memory.Blocks[memory.Nodes[300]][0x19] = 1;
        Check(!new AionVmmGameApi.LearnedSkillExactLookupDecoder(memory.Read).TryFind(memory.Header, 300, out _, out _),
            "foreign sentinel pointers are rejected rather than reported as missing");
        memory = new Memory(new uint[] { 100, 200, 300 });
        BitConverter.GetBytes(150u).CopyTo(memory.Blocks[memory.Nodes[300]], 0x20);
        Check(!new AionVmmGameApi.LearnedSkillExactLookupDecoder(memory.Read).TryFind(memory.Header, 300, out _, out _),
            "an out-of-order right child is rejected");

        memory = new Memory(new uint[] { 100, 200, 300 });
        decoder = new AionVmmGameApi.LearnedSkillExactLookupDecoder(memory.Read);
        Check(decoder.TryFind(memory.Header, 300, out _, out _), "valid capture is available for mutation checks");
        memory.Blocks[memory.Nodes[300]][0x18] ^= 1;
        Check(decoder.Verify(decoder.Guards.Select(guard => memory.Read(guard.Address, guard.Bytes.Length)).ToArray(), out _),
            "mutable tree color bytes are not invented identity guards");
        BitConverter.GetBytes(301u).CopyTo(memory.Blocks[memory.Nodes[300]], 0x20);
        Check(!decoder.Verify(decoder.Guards.Select(guard => memory.Read(guard.Address, guard.Bytes.Length)).ToArray(), out _),
            "a changed selected outer key invalidates this capture");
        Check(!decoder.Verify(decoder.Guards.Select(guard => memory.Read(guard.Address, guard.Bytes.Length)[..^1]).ToArray(), out _),
            "incomplete final guards reject publication");
        memory = new Memory(Array.Empty<uint>());
        Check(new AionVmmGameApi.LearnedSkillExactLookupDecoder(memory.Read).TryFind(memory.Header, 300, out var absent, out _) && absent == 0,
            "a valid empty tree is a complete missing-ID observation");
        memory = new Memory(new uint[] { 300 });
        BitConverter.GetBytes(0UL).CopyTo(memory.Blocks[memory.Nodes[300]], 0x30);
        Check(new AionVmmGameApi.LearnedSkillExactLookupDecoder(memory.Read).TryFind(memory.Header, 300, out absent, out _) && absent == 0,
            "an exact outer key with no learned levels agrees with the game's missing-skill result");
        return Task.CompletedTask;
    }

    public static Task CooldownFieldsAsync()
    {
        byte[] Value(uint value) => BitConverter.GetBytes(value);
        Check(AionVmmGameApi.LearnedSkillExactLookupDecoder.TryDecodeCooldown(1227,
            new[] { Value(1227), Value(0), Value(0) }, out var duration, out var end) && duration == 0 && end == 0,
            "a fully read zero duration/end is a real ready-skill value");
        Check(AionVmmGameApi.LearnedSkillExactLookupDecoder.TryDecodeCooldown(1227,
            new[] { Value(1227), Value(30000), Value(987654) }, out duration, out end) && duration == 30000 && end == 987654,
            "mandatory cooldown capture preserves the current game's raw end time");
        for (var field = 0; field < 3; field++)
        {
            var fields = new[] { Value(1227), Value(30000), Value(987654) };
            fields[field] = fields[field][..3];
            Check(!AionVmmGameApi.LearnedSkillExactLookupDecoder.TryDecodeCooldown(1227, fields, out _, out _),
                "a short mandatory identity/duration/end read never becomes a ready zero");
        }
        Check(!AionVmmGameApi.LearnedSkillExactLookupDecoder.TryDecodeCooldown(1227,
            new[] { Value(1271), Value(0), Value(0) }, out _, out _),
            "even matching cooldowns cannot substitute rank III for requested rank I");
        Check(!AionVmmGameApi.LearnedSkillExactLookupDecoder.TryDecodeCooldown(1227,
            new[] { Value(1227), Array.Empty<byte>(), Value(0) }, out _, out _),
            "a failed duration read is not a legitimate zero cooldown");
        Check(!AionVmmGameApi.LearnedSkillExactLookupDecoder.TryDecodeCooldown(0,
            new[] { Value(0), Value(0), Value(0) }, out _, out _),
            "a zero identity never establishes a readable skill");
        return Task.CompletedTask;
    }

    private sealed class Memory
    {
        public ulong Header { get; } = 0x500000;
        public Dictionary<uint, ulong> Nodes { get; } = new();
        public Dictionary<ulong, byte[]> Blocks { get; } = new();
        public string? Fault { get; set; }

        public Memory(uint[] ids)
        {
            var header = new byte[56];
            header[0x19] = 1;
            Blocks.Add(Header, header);
            for (var i = 0; i < ids.Length; i++)
            {
                var address = 0x600000UL + (ulong)i * 0x100;
                Nodes.Add(ids[i], address);
                var node = new byte[56];
                BitConverter.GetBytes(ids[i]).CopyTo(node, 0x20);
                BitConverter.GetBytes(0x900000UL + (ulong)i * 0x100).CopyTo(node, 0x28);
                BitConverter.GetBytes(1UL).CopyTo(node, 0x30);
                Blocks.Add(address, node);
            }
            ulong Build(int first, int last)
            {
                if (first > last) return Header;
                var middle = first + (last - first) / 2;
                var address = Nodes[ids[middle]];
                BitConverter.GetBytes(Build(first, middle - 1)).CopyTo(Blocks[address], 0);
                BitConverter.GetBytes(Build(middle + 1, last)).CopyTo(Blocks[address], 0x10);
                return address;
            }
            BitConverter.GetBytes(Build(0, ids.Length - 1)).CopyTo(header, 8);
        }

        public byte[] Read(ulong address, int count)
        {
            if (Fault == "io") throw new IOException("injected lookup read failure");
            foreach (var (start, bytes) in Blocks)
            {
                if (address < start || address - start > (ulong)bytes.Length || (ulong)count > (ulong)bytes.Length - (address - start)) continue;
                var result = bytes.AsSpan((int)(address - start), count).ToArray();
                return Fault == "short" && count == 56 ? result[..^1] : result;
            }
            return Array.Empty<byte>();
        }
    }
}
