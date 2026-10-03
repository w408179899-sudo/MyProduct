using Roadhog.Core.Model;

namespace Roadhog.Infrastructure.Vmm;

internal sealed partial class AionVmmGameApi
{
    private static bool IsWorldTreePointer(ulong value) =>
        value >= 0x10000 && IsLikelyUserPointer(value) && (value & 7) == 0;

    internal sealed record WorldObjectTreeNode(ulong Address, uint ServerObjectId, ushort EntityId);

    internal sealed record WorldObjectTreeVerification(
        bool StructureUnchanged,
        IReadOnlySet<ulong> InvalidIdentityNodeAddresses,
        string? Issue);

    /// <summary>
    /// One capture's tree structure and identities. Final verification compares
    /// links, sentinel flags and identities, never mutable color/padding bytes.
    /// </summary>
    internal sealed class WorldObjectTreeCapture
    {
        internal WorldObjectTreeCapture(
            ulong header, ulong root, ulong firstNode, ulong lastNode,
            bool headerVerified, bool structureComplete, bool isEmpty,
            WorldObjectTraversalTermination termination, string? issue,
            IReadOnlyList<WorldObjectTreeNode> nodes,
            IReadOnlyList<(ulong Address, byte[] Bytes)> guards, int readCount)
        {
            Header = header;
            Root = root;
            FirstNode = firstNode;
            LastNode = lastNode;
            HeaderVerified = headerVerified;
            StructureComplete = structureComplete;
            IsEmpty = isEmpty;
            Termination = termination;
            Issue = issue;
            Nodes = nodes;
            Guards = guards;
            ReadCount = readCount;
        }

        public ulong Header { get; }
        public ulong Root { get; }
        public ulong FirstNode { get; }
        public ulong LastNode { get; }
        public bool HeaderVerified { get; }
        public bool StructureComplete { get; }
        public bool IsEmpty { get; }
        public WorldObjectTraversalTermination Termination { get; }
        public string? Issue { get; }
        public IReadOnlyList<WorldObjectTreeNode> Nodes { get; }
        public IReadOnlyList<(ulong Address, byte[] Bytes)> Guards { get; }
        public int ReadCount { get; }

        public WorldObjectTreeVerification Verify(IReadOnlyList<byte[]> finalBlocks)
        {
            var invalidIdentities = new HashSet<ulong>();
            string? issue = Guards.Count == 0 ? "world_tree_final_header_missing" : null;
            if (finalBlocks.Count != Guards.Count)
                issue ??= "world_tree_final_block_count_mismatch";

            for (var i = 0; i < Guards.Count; i++)
            {
                var guard = Guards[i];
                var final = i < finalBlocks.Count ? finalBlocks[i] : null;
                if (final is null || final.Length != guard.Bytes.Length)
                {
                    issue ??= "world_tree_final_block_read_failed";
                    if (guard.Address != Header) invalidIdentities.Add(guard.Address);
                    continue;
                }

                var sentinelUnchanged = final[(int)NodeIsNilOffset] == guard.Bytes[(int)NodeIsNilOffset];
                var linksUnchanged = final.AsSpan(0, checked((int)NodeRightOffset + 8))
                    .SequenceEqual(guard.Bytes.AsSpan(0, checked((int)NodeRightOffset + 8)));
                if (!sentinelUnchanged || !linksUnchanged)
                    issue ??= guard.Address == Header ? "world_tree_header_changed" : "world_tree_node_links_changed";

                if (guard.Address != Header)
                {
                    var identityUnchanged = final.AsSpan((int)ServerNodeServerObjectIdOffset, 4)
                            .SequenceEqual(guard.Bytes.AsSpan((int)ServerNodeServerObjectIdOffset, 4)) &&
                        final.AsSpan((int)ServerNodeEntityIdOffset, 2)
                            .SequenceEqual(guard.Bytes.AsSpan((int)ServerNodeEntityIdOffset, 2));
                    if (!sentinelUnchanged || !identityUnchanged)
                    {
                        invalidIdentities.Add(guard.Address);
                        issue ??= "world_tree_node_identity_changed";
                    }
                }
            }

            return new WorldObjectTreeVerification(issue is null, invalidIdentities, issue);
        }
    }

    /// <summary>
    /// Reads each reachable node once, from the root rather than trusting a
    /// successor reaching a sentinel. The caller verifies Guards in one final
    /// batch after reading object observations. This is not an atomicity claim.
    /// </summary>
    internal sealed class WorldObjectTreeCaptureDecoder(Func<ulong, int, byte[]> read)
    {
        private const int MaximumNodes = 100000;
        private static readonly int HeaderSize = checked((int)NodeIsNilOffset + 1);
        private static readonly int NodeSize = checked((int)ServerNodeEntityIdOffset + 2);

        private static bool IsTreePointer(ulong value) =>
            IsWorldTreePointer(value);

        public WorldObjectTreeCapture Capture(ulong header)
        {
            var guards = new List<(ulong Address, byte[] Bytes)>();
            var nodes = new List<WorldObjectTreeNode>();
            var readCount = 0;
            string? issue = null;
            var termination = WorldObjectTraversalTermination.ReachedTreeEnd;

            void Reject(string value, WorldObjectTraversalTermination reason = WorldObjectTraversalTermination.TraversalReadFailed)
            {
                if (issue is not null) return;
                issue = value;
                termination = reason;
            }

            byte[] ReadBlock(ulong address, int size)
            {
                readCount++;
                try
                {
                    var bytes = read(address, size);
                    return bytes is { Length: var length } && length == size ? bytes.ToArray() : Array.Empty<byte>();
                }
                catch (Exception ex) when (ex is IOException or InvalidDataException)
                {
                    return Array.Empty<byte>();
                }
            }

            WorldObjectTreeCapture Result(ulong captureRoot, ulong captureFirst, ulong captureLast, bool validHeader, bool empty) =>
                new(header, captureRoot, captureFirst, captureLast, validHeader, issue is null, empty,
                    termination, issue, nodes.ToArray(), guards.ToArray(), readCount);

            if (!IsTreePointer(header))
            {
                Reject("world_tree_header_pointer_invalid", WorldObjectTraversalTermination.AnchorReadFailed);
                return Result(0, 0, 0, false, false);
            }

            var head = ReadBlock(header, HeaderSize);
            if (head.Length != HeaderSize)
            {
                Reject("world_tree_header_block_read_failed", WorldObjectTraversalTermination.AnchorReadFailed);
                return Result(0, 0, 0, false, false);
            }

            guards.Add((header, head));
            var first = BitConverter.ToUInt64(head, (int)NodeLeftOffset);
            var root = BitConverter.ToUInt64(head, (int)NodeParentOffset);
            var last = BitConverter.ToUInt64(head, (int)NodeRightOffset);
            if (head[(int)NodeIsNilOffset] != 1)
            {
                Reject("world_tree_header_sentinel_invalid", WorldObjectTraversalTermination.AnchorReadFailed);
                return Result(root, first, last, false, false);
            }

            var headerVerified = IsTreePointer(root) && IsTreePointer(first) && IsTreePointer(last);
            if (!headerVerified)
                Reject("world_tree_header_link_pointer_invalid", WorldObjectTraversalTermination.AnchorReadFailed);

            if (root == header)
            {
                var empty = first == header && last == header;
                if (!empty) Reject("world_tree_empty_header_links_disagree");
                if (issue is null) termination = WorldObjectTraversalTermination.EmptyTree;
                return Result(root, first, last, headerVerified, empty && issue is null);
            }

            if (first == header || last == header)
                Reject("world_tree_nonempty_header_links_disagree");
            if (!IsTreePointer(root))
                return Result(root, first, last, false, false);

            var visited = new HashSet<ulong>();
            var stack = new Stack<(ulong Address, ulong Parent, WorldObjectTreeNode? Emit)>();
            stack.Push((root, header, null));
            while (stack.Count != 0)
            {
                var frame = stack.Pop();
                if (frame.Emit is not null)
                {
                    nodes.Add(frame.Emit);
                    continue;
                }
                if (frame.Address == header) continue;
                if (!IsTreePointer(frame.Address))
                {
                    Reject("world_tree_node_pointer_invalid");
                    continue;
                }
                if (!visited.Add(frame.Address))
                {
                    Reject("world_tree_cycle_or_shared_node", WorldObjectTraversalTermination.CycleDetected);
                    continue;
                }
                if (visited.Count > MaximumNodes)
                {
                    Reject("world_tree_guard_limit_reached", WorldObjectTraversalTermination.GuardLimitReached);
                    break;
                }

                var block = ReadBlock(frame.Address, NodeSize);
                if (block.Length != NodeSize)
                {
                    Reject("world_tree_node_block_read_failed");
                    continue;
                }
                guards.Add((frame.Address, block));
                if (block[(int)NodeIsNilOffset] != 0)
                {
                    Reject("world_tree_foreign_or_invalid_sentinel");
                    continue;
                }

                var left = BitConverter.ToUInt64(block, (int)NodeLeftOffset);
                var parent = BitConverter.ToUInt64(block, (int)NodeParentOffset);
                var right = BitConverter.ToUInt64(block, (int)NodeRightOffset);
                if (parent != frame.Parent) Reject("world_tree_parent_link_mismatch");
                if (!IsTreePointer(left) || !IsTreePointer(right) || !IsTreePointer(parent))
                    Reject("world_tree_node_link_pointer_invalid");
                var node = new WorldObjectTreeNode(frame.Address,
                    BitConverter.ToUInt32(block, (int)ServerNodeServerObjectIdOffset),
                    BitConverter.ToUInt16(block, (int)ServerNodeEntityIdOffset));
                stack.Push((right, frame.Address, null));
                stack.Push((frame.Address, frame.Parent, node));
                stack.Push((left, frame.Address, null));
            }

            if (nodes.Count == 0 || nodes[0].Address != first || nodes[^1].Address != last)
                Reject("world_tree_header_extrema_mismatch");
            return Result(root, first, last, headerVerified, false);
        }
    }
}
