using Roadhog.Core.Model;
using Vmmsharp;

namespace Roadhog.Infrastructure.Vmm;

internal sealed partial class AionVmmGameApi
{
    private const int ItemStaticUseGroupOffset = 328;
    // Item static schema verified against Tone's drink/food samples; see docs/food-maintenance.md.
    private static bool TryReadFoodDefinition(VmmProcess process, ulong gameBase, uint id,
        Dictionary<uint, byte[]> chunks, out FoodItemDefinition? food)
    {
        food = null;
        if (!TryFindStaticItemPackedHandle(process, gameBase, id, out var handle)) return false;
        var index = handle >> StaticResolverPackedChunkShift;
        var offset = checked((int)(handle & StaticResolverPackedOffsetMask));
        if (!TryReadStaticResolverChunk(process, gameBase, index == 0 ? 0 : index - 1, chunks, out var bytes) ||
            offset + 499 > bytes.Length || BitConverter.ToUInt32(bytes, offset) != id) return false;
        return TryDecodeFoodDefinition(bytes.AsSpan(offset), address =>
        {
            // Use-skill names are UTF-16, unlike the ASCII eat/drink animation strings.
            return TryReadUtf16String(process, address, 128, out var name) ? name : null;
        }, out food);
    }

    internal static bool TryDecodeFoodDefinition(ReadOnlySpan<byte> record, Func<ulong, string?> readName,
        out FoodItemDefinition? food)
    {
        food = null;
        if (record.Length < 499) return false;
        var group = BitConverter.ToUInt32(record.Slice(ItemStaticUseGroupOffset, 4));
        if (group is not (21 or 22)) return true;
        var pointer = BitConverter.ToUInt64(record.Slice(280, 8));
        var name = pointer == 0 ? null : readName(pointer);
        if (string.IsNullOrWhiteSpace(name) || name.Length >= 128 ||
            name.Any(c => !(char.IsAsciiLetterOrDigit(c) || c == '_')) || record[498] == 0) return false;
        food = new(name, record[498], record[493]);
        return true;
    }
}
