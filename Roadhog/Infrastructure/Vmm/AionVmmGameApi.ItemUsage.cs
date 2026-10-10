using Vmmsharp;

namespace Roadhog.Infrastructure.Vmm;

internal sealed partial class AionVmmGameApi
{
    private static bool TryReadItemUseGroup(VmmProcess process, ulong gameBase, uint templateId,
        Dictionary<uint, byte[]> chunks, out uint group)
    {
        group = 0;
        if (!TryFindStaticItemPackedHandle(process, gameBase, templateId, out var handle)) return false;
        var index = handle >> StaticResolverPackedChunkShift;
        var offset = checked((int)(handle & StaticResolverPackedOffsetMask));
        if (!TryReadStaticResolverChunk(process, gameBase, index == 0 ? 0 : index - 1, chunks, out var bytes) ||
            offset > bytes.Length) return false;
        return TryDecodeItemUseGroup(templateId, bytes.AsSpan(offset), out group);
    }

    // Reuses the existing static item schema used by food metadata. 36 is return, 31 is scroll buffs.
    internal static bool TryDecodeItemUseGroup(uint templateId, ReadOnlySpan<byte> record, out uint group)
    {
        group = 0;
        if (record.Length < ItemStaticUseGroupOffset + sizeof(uint) || BitConverter.ToUInt32(record) != templateId)
            return false;
        group = BitConverter.ToUInt32(record.Slice(ItemStaticUseGroupOffset, sizeof(uint)));
        return true;
    }
}
