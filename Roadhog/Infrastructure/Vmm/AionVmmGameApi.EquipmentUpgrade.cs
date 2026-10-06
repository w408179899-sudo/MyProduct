using Roadhog.Core.Api;
using Roadhog.Core.Common;
using Roadhog.Core.Model;

namespace Roadhog.Infrastructure.Vmm;

internal sealed partial class AionVmmGameApi : IEquipmentUpgradeGameApi
{
    public Task<OperationResult<EquipmentUpgradeUi>> ReadEquipmentUpgradeUiAsync(GameApiReadContext context, CancellationToken token) =>
        Task.Run(() => ReadStable(context, AionVmmSnapshotChannels.EquipmentUpgradeUi,
            () => ReadInventoryInteractionCore(context, static (decoder, b) => decoder.ReadEquipmentUi(b))), token);

    public Task<OperationResult<EquipmentUpgradeInventory>> ReadEquipmentUpgradeInventoryAsync(GameApiReadContext context, CancellationToken token) =>
        Task.Run(() => ReadStable(context, AionVmmSnapshotChannels.EquipmentUpgradeInventory,
            () => ReadEquipmentInventoryCore(context)), token);

    private OperationResult<EquipmentUpgradeInventory> ReadEquipmentInventoryCore(GameApiReadContext context)
    {
        try
        {
            var connection = GetOrCreateConnection(context.VmmDeviceName);
            lock (connection.SyncRoot)
            {
                if (!TryResolveProcess(connection.Vmm, context, out var process, out var error))
                    return OperationResult<EquipmentUpgradeInventory>.Fail(error);
                var b = process.GetModuleBase(ResolveModuleName());
                if (b == 0) throw new InvalidDataException("Missing Game.dll.");
                byte[] Read(ulong address, int size)
                {
                    if (!TryReadBytes(process, address, size, out var bytes, bypassMemoryCache: true) || bytes.Length != size)
                        throw new InvalidDataException("Incomplete equipment capture.");
                    return bytes;
                }
                var chunks = new Dictionary<uint, byte[]>();
                (byte[] Chunk, int Offset) Template(uint template)
                {
                    if (!TryFindStaticItemPackedHandle(process, b, template, out var handle)) throw new InvalidDataException("Missing equipment template.");
                    var rawIndex = handle >> StaticResolverPackedChunkShift;
                    var offset = checked((int)(handle & StaticResolverPackedOffsetMask));
                    if (!TryReadStaticResolverChunk(process, b, rawIndex == 0 ? 0 : rawIndex - 1, chunks, out var chunk) ||
                        offset + 494 > chunk.Length || BitConverter.ToUInt32(chunk, offset) != template)
                        throw new InvalidDataException("Invalid equipment template.");
                    return (chunk, offset);
                }
                int TemplateLevel(uint template)
                {
                    var (chunk, offset) = Template(template);
                    return chunk[offset + 493];
                }
                int BaseSockets(uint template)
                {
                    var (chunk, offset) = Template(template);
                    var quality = chunk[offset + 481]; var subtype = chunk[offset + 480];
                    if (quality >= 7 || subtype >= 4) return 0;
                    var count = BitConverter.ToInt32(Read(b + 0x6C1B80 + (ulong)(quality * 16 + subtype * 4), 4));
                    if (count is < 0 or > 6) throw new InvalidDataException("Invalid base socket count.");
                    return count;
                }
                var inventory = new InventoryInteractionDecoder(Read).ReadEquipmentInventory(b, BaseSockets, out var complete, TemplateLevel);
                if (complete) return OperationResult<EquipmentUpgradeInventory>.Ok(inventory);
                if (inventory.Items.Count > 0 && _stableSnapshots.TryUpdate<EquipmentUpgradeInventory>(
                        BuildStableSnapshotSessionKey(context), AionVmmSnapshotChannels.EquipmentUpgradeInventory, DateTimeOffset.Now,
                        previous => MergeEquipmentInventory(previous, inventory), out var merged, out _))
                    return OperationResult<EquipmentUpgradeInventory>.Ok(merged!);
                return OperationResult<EquipmentUpgradeInventory>.Fail("Incomplete equipment inventory; retaining publication.");
            }
        }
        catch (Exception ex) { return OperationResult<EquipmentUpgradeInventory>.Fail(ex.Message); }
    }

    internal static EquipmentUpgradeInventory MergeEquipmentInventory(EquipmentUpgradeInventory previous, EquipmentUpgradeInventory partial)
    {
        var items = previous.Items.ToDictionary(i => i.InstanceId);
        foreach (var item in partial.Items) items[item.InstanceId] = item;
        return new(items.Values.OrderBy(i => i.InstanceId).ToArray());
    }
}
