using Roadhog.Core.Model;
using Roadhog.Infrastructure.Vmm;

internal static class WorldObjectTreeCaptureTests
{
    public static Task StableTreesAndReadBudgetAsync()
    {
        foreach (var ids in new[]
        {
            Array.Empty<ushort>(), new ushort[] { 10 }, new ushort[] { 10, 20 },
            new ushort[] { 10, 20, 30 }, new ushort[] { 10, 20, 30, 40, 50, 60, 70 }
        })
        {
            var memory = new Memory(ids);
            var capture = Capture(memory);
            Check(capture.StructureComplete && capture.HeaderVerified, "a stable standard tree proves its complete structure");
            Check(capture.IsEmpty == (ids.Length == 0), "only a verified self-linked header is an empty tree");
            Check(capture.Termination == (ids.Length == 0
                ? WorldObjectTraversalTermination.EmptyTree : WorldObjectTraversalTermination.ReachedTreeEnd),
                "stable trees have their actual normal termination");
            Check(capture.Nodes.Select(node => node.EntityId).SequenceEqual(ids),
                "root-based coverage produces the exact inorder node set");
            Check(capture.ReadCount == ids.Length + 1 && memory.ReadCount == ids.Length + 1,
                "capture uses one complete block read for the header and each node");
            var verified = capture.Verify(memory.Batch(capture));
            Check(verified.StructureUnchanged && verified.InvalidIdentityNodeAddresses.Count == 0,
                "one unchanged final batch verifies every captured node identity and link");
            Check(memory.BatchCount == 1, "final guards are checked by one injected batch");
        }

        var large = new Memory(Enumerable.Range(1, 1023).Select(id => (ushort)id).ToArray());
        var largeCapture = Capture(large);
        Check(largeCapture.StructureComplete && largeCapture.Nodes.Count == 1023 && large.ReadCount == 1024,
            "the read budget scales once per node, with no per-field or repeated node IO");
        Check(largeCapture.Verify(large.Batch(largeCapture)).StructureUnchanged && large.BatchCount == 1,
            "the complete large capture needs one final batch");

        // Mirror a two-node fixture to cover a root with only a left child.
        var onlyLeft = new Memory(new ushort[] { 10, 20 });
        onlyLeft.Pointer(onlyLeft.Header, 8, onlyLeft.Nodes[20]);
        onlyLeft.Pointer(onlyLeft.Nodes[20], 0, onlyLeft.Nodes[10]);
        onlyLeft.Pointer(onlyLeft.Nodes[20], 8, onlyLeft.Header);
        onlyLeft.Pointer(onlyLeft.Nodes[20], 16, onlyLeft.Header);
        onlyLeft.Pointer(onlyLeft.Nodes[10], 8, onlyLeft.Nodes[20]);
        onlyLeft.Pointer(onlyLeft.Nodes[10], 16, onlyLeft.Header);
        var leftCapture = Capture(onlyLeft);
        Check(leftCapture.StructureComplete && leftCapture.Nodes.Select(node => node.EntityId).SequenceEqual(new ushort[] { 10, 20 }),
            "left-only and right-only trees both cover their actual nodes");
        return Task.CompletedTask;
    }

    public static Task InvalidStructureAsync()
    {
        void Reject(Action<Memory> mutate, string message, bool empty = false)
        {
            var memory = new Memory(empty ? Array.Empty<ushort>() : new ushort[] { 10, 20, 30 });
            mutate(memory);
            var capture = Capture(memory);
            Check(!capture.StructureComplete && capture.Issue is not null, message);
        }

        Reject(m => m.Blocks[m.Header][25] = 0, "a regular-node flag cannot establish a header sentinel");
        Reject(m => m.Blocks[m.Header][25] = 2, "an arbitrary nonzero header nil byte is not a valid sentinel");
        Reject(m => m.Pointer(m.Header, 8, 0), "a zero root pointer is not this tree's empty sentinel");
        Reject(m => m.Pointer(m.Header, 8, m.Nodes[20] + 1), "a misaligned root is rejected without a narrower pointer fallback");
        Reject(m => m.Pointer(m.Header, 0, m.Header), "a self-linked begin cannot empty a nonempty root");
        Reject(m => m.Pointer(m.Header, 16, m.Header), "a self-linked end cannot truncate a nonempty root");
        Reject(m => m.Pointer(m.Header, 0, m.Nodes[20]), "captured minimum must agree with root traversal");
        Reject(m => m.Pointer(m.Header, 16, m.Nodes[20]), "captured maximum must agree with root traversal");
        Reject(m => m.Pointer(m.Header, 0, 0x700000), "a self root with a foreign begin is not verified empty", empty: true);
        Reject(m => m.Pointer(m.Header, 16, 0x700000), "a self root with a foreign end is not verified empty", empty: true);
        Reject(m => m.Blocks[m.Nodes[20]][25] = 1, "a foreign sentinel at the root cannot fabricate an empty tree");
        Reject(m => m.Blocks[m.Nodes[30]][25] = 1, "a foreign sentinel at the end cannot prove a complete short tree");
        Reject(m => m.Blocks[m.Nodes[30]][25] = 255, "invalid node nil bytes cannot prove successful termination");
        Reject(m => m.Pointer(m.Nodes[10], 8, m.Header), "every child's parent must identify the node that reached it");
        Reject(m => m.Pointer(m.Nodes[20], 16, m.Nodes[20]), "a self loop does not count as complete coverage");
        Reject(m => m.Pointer(m.Nodes[30], 16, m.Nodes[20]), "a multi-node cycle is rejected");
        Reject(m => m.Pointer(m.Nodes[20], 16, m.Nodes[10]), "a shared child cannot masquerade as a second covered subtree");
        Reject(m => m.Pointer(m.Nodes[20], 0, 0), "a zero child is rejected rather than treated as a sentinel");
        Reject(m => m.ShortAddress = m.Header, "a short header block cannot fabricate valid links");
        Reject(m => m.ShortAddress = m.Nodes[10], "a short node block cannot fabricate an absent subtree");
        Reject(m => m.FailedAddress = m.Header, "a failed header block cannot publish a fabricated empty tree");
        Reject(m => m.FailedAddress = m.Nodes[10], "a failed node block cannot prove complete coverage");
        return Task.CompletedTask;
    }

    public static Task PartialCoverageRetainsIndependentNodesAsync()
    {
        var memory = new Memory(new ushort[] { 10, 20, 30 });
        memory.ShortAddress = memory.Nodes[10];
        var capture = Capture(memory);
        Check(!capture.StructureComplete && capture.Nodes.Select(node => node.EntityId).SequenceEqual(new ushort[] { 20, 30 }),
            "one failed branch leaves independently captured nodes available for provider partial merge");
        memory.ShortAddress = null;
        Check(capture.Verify(memory.Batch(capture)).StructureUnchanged,
            "the retained branches can still have their identities verified by the final batch");

        memory = new Memory(new ushort[] { 10, 20, 30 });
        memory.Blocks[memory.Nodes[10]][25] = 1;
        capture = Capture(memory);
        Check(!capture.StructureComplete && capture.Nodes.Select(node => node.EntityId).SequenceEqual(new ushort[] { 20, 30 }),
            "a foreign sentinel is omitted while independent normal nodes remain usable");

        memory = new Memory(new ushort[] { 10, 20, 30 });
        memory.EntityId(memory.Nodes[10], 0);
        memory.ServerId(memory.Nodes[10], 0);
        capture = Capture(memory);
        Check(capture.StructureComplete && capture.Nodes[0].EntityId == 0 && capture.Nodes[0].ServerObjectId == 0,
            "successfully read zero identities retain the adapter's existing placeholder-filter semantics");
        return Task.CompletedTask;
    }

    public static Task FinalGuardsAndIdentityAsync()
    {
        void Changed(Action<Memory> mutate, bool invalidIdentity, string message)
        {
            var memory = new Memory(new ushort[] { 10, 20, 30 });
            var capture = Capture(memory);
            mutate(memory);
            var verified = capture.Verify(memory.Batch(capture));
            Check(!verified.StructureUnchanged && verified.Issue is not null, message);
            Check(verified.InvalidIdentityNodeAddresses.Contains(memory.Nodes[30]) == invalidIdentity,
                "only observations with unverified identities are excluded from partial merge");
        }

        Changed(m => m.Pointer(m.Header, 8, m.Nodes[10]), false, "a changed root invalidates complete absence evidence");
        Changed(m => m.Pointer(m.Header, 0, m.Nodes[20]), false, "a changed minimum invalidates complete absence evidence");
        Changed(m => m.Pointer(m.Header, 16, m.Nodes[20]), false, "a changed maximum invalidates complete absence evidence");
        Changed(m => m.Pointer(m.Nodes[30], 8, m.Header), false, "a changed parent downgrades coverage while stable identity can merge");
        Changed(m => m.Pointer(m.Nodes[30], 0, m.Nodes[10]), false, "a changed left link invalidates complete coverage");
        Changed(m => m.Pointer(m.Nodes[30], 16, m.Nodes[10]), false, "a changed right link invalidates complete coverage");
        Changed(m => m.ServerId(m.Nodes[30], 999), true, "server identity replacement excludes that node's observation");
        Changed(m => m.EntityId(m.Nodes[30], 999), true, "entity-slot reuse excludes that node's observation");
        Changed(m => m.Blocks[m.Nodes[30]][25] = 1, true, "a node becoming a sentinel excludes that observation");
        Changed(m => m.ShortAddress = m.Nodes[30], true, "short final identity reads exclude that observation");

        var empty = new Memory(Array.Empty<ushort>());
        var emptyCapture = Capture(empty);
        empty.Pointer(empty.Header, 8, 0x700000);
        Check(!emptyCapture.Verify(empty.Batch(emptyCapture)).StructureUnchanged,
            "an empty tree becoming nonempty during capture cannot be published as complete empty");

        var memory = new Memory(new ushort[] { 10, 20, 30 });
        var capture = Capture(memory);
        var final = memory.Batch(capture).ToArray();
        var missingFinal = capture.Verify(final[..^1]);
        Check(!missingFinal.StructureUnchanged && missingFinal.InvalidIdentityNodeAddresses.Contains(capture.Guards[^1].Address),
            "missing final batch results cannot establish complete publication");
        Check(!capture.Verify(final.Concat(new[] { new byte[1] }).ToArray()).StructureUnchanged,
            "extra final results cannot be matched to the capture by position");
        memory.ShortAddress = memory.Header;
        Check(!capture.Verify(memory.Batch(capture)).StructureUnchanged,
            "a short final header guard rejects complete publication");
        return Task.CompletedTask;
    }

    public static Task CaptureChangesAndMutableBytesAsync()
    {
        var memory = new Memory(new ushort[] { 10, 20, 30 });
        memory.AfterRead = address =>
        {
            if (address == memory.Nodes[10]) memory.Pointer(memory.Header, 0, memory.Nodes[20]);
        };
        var capture = Capture(memory);
        Check(capture.StructureComplete && !capture.Verify(memory.Batch(capture)).StructureUnchanged,
            "a mutation between structural reads is caught by final guards before publication");

        memory = new Memory(new ushort[] { 10, 20, 30 });
        capture = Capture(memory);
        foreach (var bytes in memory.Blocks.Values)
        {
            bytes[24] ^= 1; // Tree color may change without identity or membership changing.
            if (bytes.Length > 27) bytes[26] ^= 0x7F; // Padding is not an invented identity guard.
        }
        Check(capture.Verify(memory.Batch(capture)).StructureUnchanged,
            "mutable colors and padding are deliberately excluded from publication guards");

        // A stable deletion produces a newly complete smaller tree immediately.
        memory = new Memory(new ushort[] { 10, 20 });
        capture = Capture(memory);
        Check(capture.StructureComplete && capture.Nodes.Count == 2 && capture.Verify(memory.Batch(capture)).StructureUnchanged,
            "a valid smaller tree is accepted on its first capture without debounce");
        var newlyEmpty = new Memory(Array.Empty<ushort>());
        capture = Capture(newlyEmpty);
        Check(capture.StructureComplete && capture.IsEmpty && capture.Verify(newlyEmpty.Batch(capture)).StructureUnchanged,
            "a valid newly empty tree is accepted on its first capture without debounce");
        return Task.CompletedTask;
    }

    public static Task GuardLimitAndInvalidHeadersAsync()
    {
        var reads = 0;
        var decoder = new AionVmmGameApi.WorldObjectTreeCaptureDecoder((_, _) =>
        {
            reads++;
            throw new IOException("an invalid header must never be read");
        });
        foreach (var header in new ulong[] { 0, 1, 0x500001, 0xFFFF800000000000 })
        {
            var invalid = decoder.Capture(header);
            Check(!invalid.StructureComplete && !invalid.HeaderVerified && invalid.ReadCount == 0 && reads == 0,
                "null, low, misaligned and non-user header pointers fail before any memory IO");
            Check(!invalid.Verify(Array.Empty<byte[]>()).StructureUnchanged,
                "no captured header guard cannot become successful verification by an empty final batch");
        }

        const ulong treeHeader = 0x500000;
        const ulong firstAddress = 0x1000000;
        const ulong step = 64;
        const int nodeCount = 100001;
        byte[] Chain(ulong address, int size)
        {
            var bytes = new byte[size];
            void Pointer(int offset, ulong value) => BitConverter.GetBytes(value).CopyTo(bytes, offset);
            if (address == treeHeader)
            {
                Pointer(0, firstAddress);
                Pointer(8, firstAddress);
                Pointer(16, firstAddress + (nodeCount - 1UL) * step);
                bytes[25] = 1;
                return bytes;
            }
            var index = checked((int)((address - firstAddress) / step));
            Pointer(0, treeHeader);
            Pointer(8, index == 0 ? treeHeader : address - step);
            Pointer(16, index + 1 == nodeCount ? treeHeader : address + step);
            BitConverter.GetBytes((uint)index + 1).CopyTo(bytes, 28);
            BitConverter.GetBytes(unchecked((ushort)(index + 1))).CopyTo(bytes, 32);
            return bytes;
        }

        var bounded = new AionVmmGameApi.WorldObjectTreeCaptureDecoder(Chain).Capture(treeHeader);
        Check(!bounded.StructureComplete && bounded.Termination == WorldObjectTraversalTermination.GuardLimitReached,
            "a long acyclic malformed tree terminates at the existing capture guard limit");
        Check(bounded.Nodes.Count == 100000 && bounded.ReadCount == 100001,
            "the guard rejects the next node before IO while retaining independently captured earlier nodes");
        return Task.CompletedTask;
    }

    private static AionVmmGameApi.WorldObjectTreeCapture Capture(Memory memory) =>
        new AionVmmGameApi.WorldObjectTreeCaptureDecoder(memory.Read).Capture(memory.Header);

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class Memory
    {
        public ulong Header { get; } = 0x500000;
        public Dictionary<ushort, ulong> Nodes { get; } = new();
        public Dictionary<ulong, byte[]> Blocks { get; } = new();
        public ulong? ShortAddress { get; set; }
        public ulong? FailedAddress { get; set; }
        public Action<ulong>? AfterRead { get; set; }
        public int ReadCount { get; private set; }
        public int BatchCount { get; private set; }

        public Memory(ushort[] ids)
        {
            Blocks[Header] = new byte[26];
            Blocks[Header][25] = 1;
            foreach (var id in ids)
            {
                var address = 0x600000UL + (ulong)Nodes.Count * 0x100;
                Nodes.Add(id, address);
                Blocks[address] = new byte[34];
                ServerId(address, 1000u + id);
                EntityId(address, id);
            }

            ulong Build(int first, int last, ulong parent)
            {
                if (first > last) return Header;
                var middle = first + (last - first) / 2;
                var address = Nodes[ids[middle]];
                Pointer(address, 8, parent);
                Pointer(address, 0, Build(first, middle - 1, address));
                Pointer(address, 16, Build(middle + 1, last, address));
                return address;
            }

            Pointer(Header, 8, Build(0, ids.Length - 1, Header));
            Pointer(Header, 0, ids.Length == 0 ? Header : Nodes[ids[0]]);
            Pointer(Header, 16, ids.Length == 0 ? Header : Nodes[ids[^1]]);
        }

        public void Pointer(ulong address, int offset, ulong value) =>
            BitConverter.GetBytes(value).CopyTo(Blocks[address], offset);

        public void ServerId(ulong address, uint value) =>
            BitConverter.GetBytes(value).CopyTo(Blocks[address], 28);

        public void EntityId(ulong address, ushort value) =>
            BitConverter.GetBytes(value).CopyTo(Blocks[address], 32);

        public byte[] Read(ulong address, int size)
        {
            ReadCount++;
            if (FailedAddress == address) throw new IOException("injected tree block failure");
            var result = Blocks.TryGetValue(address, out var block) && block.Length >= size
                ? block.AsSpan(0, size).ToArray() : Array.Empty<byte>();
            AfterRead?.Invoke(address);
            return ShortAddress == address && result.Length != 0 ? result[..^1] : result;
        }

        public IReadOnlyList<byte[]> Batch(AionVmmGameApi.WorldObjectTreeCapture capture)
        {
            BatchCount++;
            return capture.Guards.Select(guard => Read(guard.Address, guard.Bytes.Length)).ToArray();
        }
    }
}
