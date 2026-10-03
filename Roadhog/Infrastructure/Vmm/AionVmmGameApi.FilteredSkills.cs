using Vmmsharp;

namespace Roadhog.Infrastructure.Vmm;

internal sealed partial class AionVmmGameApi
{
    /// <summary>
    /// Capture-local exact lookup of the ordered outer map used by sub_1803B2100.
    /// It never groups display names or substitutes another learned rank.
    /// </summary>
    internal sealed class LearnedSkillExactLookupDecoder(Func<ulong, int, byte[]> read)
    {
        private readonly Dictionary<ulong, byte[]> _nodes = new();
        private readonly Dictionary<(ulong Address, int Size), byte[]> _guards = new();
        public IReadOnlyList<(ulong Address, byte[] Bytes)> Guards =>
            _guards.Select(pair => (pair.Key.Address, pair.Value)).ToArray();
        public int NodeReadCount { get; private set; }

        private byte[] Bytes(ulong address, int count)
        {
            if (!IsLikelyUserPointer(address)) return Array.Empty<byte>();
            try { return read(address, count); }
            catch (Exception ex) when (ex is IOException or InvalidDataException) { return Array.Empty<byte>(); }
        }

        private void Guard(ulong address, byte[] bytes, ulong offset, int count) =>
            _guards[(address + offset, count)] = bytes.AsSpan((int)offset, count).ToArray();

        public bool TryFind(ulong header, uint skillId, out ulong outerNode, out string error)
        {
            outerNode = 0;
            error = string.Empty;
            if (skillId == 0 || !IsLikelyUserPointer(header))
            {
                error = "invalid exact learned skill lookup identity";
                return false;
            }
            if (!_nodes.TryGetValue(header, out var head))
            {
                head = Bytes(header, checked((int)NodeIsNilOffset + 1));
                if (head.Length != checked((int)NodeIsNilOffset + 1) || head[(int)NodeIsNilOffset] != 1)
                {
                    error = "failed to read learned skill tree sentinel";
                    return false;
                }
                _nodes.Add(header, head);
                Guard(header, head, NodeParentOffset, 8);
                Guard(header, head, NodeIsNilOffset, 1);
            }
            var node = BitConverter.ToUInt64(head, (int)NodeParentOffset);
            var visited = new HashSet<ulong>();
            ulong lower = 0;
            ulong upper = (ulong)uint.MaxValue + 1;
            for (var depth = 0; node != header; depth++)
            {
                if (!IsLikelyUserPointer(node) || depth >= 65536 || !visited.Add(node))
                {
                    error = "learned skill exact lookup has an invalid pointer, cycle or traversal overflow";
                    return false;
                }
                if (!_nodes.TryGetValue(node, out var bytes))
                {
                    bytes = Bytes(node, checked((int)LearnedSkillOuterLevelTreeSizeOffset + 8));
                    if (bytes.Length != checked((int)LearnedSkillOuterLevelTreeSizeOffset + 8))
                    {
                        error = "failed to read complete learned skill search node";
                        return false;
                    }
                    _nodes.Add(node, bytes);
                    NodeReadCount++;
                }
                if (bytes[(int)NodeIsNilOffset] != 0)
                {
                    error = "learned skill exact lookup encountered a foreign sentinel";
                    return false;
                }
                var key = BitConverter.ToUInt32(bytes, (int)LearnedSkillOuterSkillIdOffset);
                if (key <= lower || key >= upper)
                {
                    error = "learned skill exact lookup violated ordered map bounds";
                    return false;
                }
                Guard(node, bytes, NodeIsNilOffset, 1);
                Guard(node, bytes, LearnedSkillOuterSkillIdOffset, 4);
                if (key == skillId)
                {
                    Guard(node, bytes, LearnedSkillOuterLevelTreeHeaderOffset, 8);
                    Guard(node, bytes, LearnedSkillOuterLevelTreeSizeOffset, 8);
                    if (BitConverter.ToUInt64(bytes, (int)LearnedSkillOuterLevelTreeSizeOffset) != 0)
                        outerNode = node;
                    return true;
                }
                var link = key > skillId ? NodeLeftOffset : NodeRightOffset;
                Guard(node, bytes, link, 8);
                if (key > skillId) upper = key;
                else lower = key;
                node = BitConverter.ToUInt64(bytes, (int)link);
            }
            return true; // A valid sentinel means this exact ID is not learned.
        }

        public bool Verify(IReadOnlyList<byte[]> values, out string error)
        {
            var guards = Guards;
            if (values.Count != guards.Count || guards.Where((guard, i) =>
                    values[i] is null || values[i].Length != guard.Bytes.Length ||
                    !values[i].AsSpan().SequenceEqual(guard.Bytes)).Any())
            {
                error = "learned skill exact lookup changed or had an incomplete final guard";
                return false;
            }
            error = string.Empty;
            return true;
        }

        public static bool TryDecodeCooldown(uint expectedId, IReadOnlyList<byte[]> fields,
            out uint duration, out uint end)
        {
            duration = 0;
            end = 0;
            if (expectedId == 0 || fields.Count != 3 || fields.Any(field => field is null || field.Length != 4) ||
                BitConverter.ToUInt32(fields[0]) != expectedId) return false;
            duration = BitConverter.ToUInt32(fields[1]);
            end = BitConverter.ToUInt32(fields[2]);
            return true;
        }
    }

    private static bool TryReadHighestRequestedLearnedSkills(
        VmmProcess process, ulong gameBase, IReadOnlySet<uint> skillIds,
        out List<LearnedSkillInfo> skills, out string error)
    {
        skills = new List<LearnedSkillInfo>();
        error = string.Empty;
        if (skillIds.Count == 0) return true;
        byte[] Read(ulong address, int count) =>
            TryReadBytes(process, address, count, out var bytes, bypassMemoryCache: true)
                ? bytes : Array.Empty<byte>();
        var managerBytes = Read(gameBase + SkillManagerGlobalRva, 8);
        if (managerBytes.Length != 8 || !IsLikelyUserPointer(BitConverter.ToUInt64(managerBytes)))
        {
            error = "failed to read exact learned skill manager";
            return false;
        }
        var manager = BitConverter.ToUInt64(managerBytes);
        var headerBytes = Read(manager + LearnedSkillTreeOffset, 8);
        if (headerBytes.Length != 8 || !IsLikelyUserPointer(BitConverter.ToUInt64(headerBytes)))
        {
            error = "failed to read exact learned skill tree header";
            return false;
        }
        var decoder = new LearnedSkillExactLookupDecoder(Read);
        var header = BitConverter.ToUInt64(headerBytes);
        foreach (var id in skillIds.Order())
        {
            if (!decoder.TryFind(header, id, out var outerNode, out error)) return false;
            if (outerNode == 0) continue;
            if (!TryReadHighestLearnedSkillFromOuterNode(process, outerNode, id, out var skill))
            {
                error = "failed to read exact learned skill " + id;
                return false;
            }
            skills.Add(skill);
        }

        // One final NOCACHE batch checks the capture-local search path and reads
        // mandatory cooldown fields. Failed fields never become plausible zeroes.
        var guards = decoder.Guards;
        var requests = new List<(ulong Address, int Size)>
        {
            (gameBase + SkillManagerGlobalRva, 8), (manager + LearnedSkillTreeOffset, 8)
        };
        requests.AddRange(guards.Select(guard => (guard.Address, guard.Bytes.Length)));
        foreach (var skill in skills)
        {
            requests.Add((skill.SkillItem + SkillItemSkillIdOffset, 4));
            requests.Add((skill.SkillItem + SkillItemCooldownDurationOffset, 4));
            requests.Add((skill.SkillItem + SkillItemCooldownEndTimeOffset, 4));
        }
        var values = ReadOpportunityBatch(process, requests, CancellationToken.None);
        if (values.Count != requests.Count || values[0] is not { Length: 8 } || values[1] is not { Length: 8 } ||
            !values[0].AsSpan().SequenceEqual(managerBytes) || !values[1].AsSpan().SequenceEqual(headerBytes) ||
            !decoder.Verify(values.Skip(2).Take(guards.Count).ToArray(), out error))
        {
            error = string.IsNullOrEmpty(error) ? "exact learned skill manager changed or final read failed" : error;
            return false;
        }
        for (var i = 0; i < skills.Count; i++)
        {
            var offset = 2 + guards.Count + i * 3;
            if (!LearnedSkillExactLookupDecoder.TryDecodeCooldown(skills[i].SkillId,
                values.Skip(offset).Take(3).ToArray(), out var duration, out var end))
            {
                error = "failed mandatory exact skill identity or cooldown read";
                return false;
            }
            var skill = skills[i];
            skill.CooldownDuration = duration;
            skill.CooldownEndTime = end;
            skills[i] = skill;
        }
        return true;
    }
}
