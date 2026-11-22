using Roadhog.Core.Api;
using Roadhog.Core.Common;
using Roadhog.Core.Model;

namespace Roadhog.Infrastructure.Vmm;

internal sealed partial class AionVmmGameApi
{
    Task<OperationResult<PublishedGameSnapshot<LockedCombatTargetSnapshot>>> ILockedTargetPublicationSource.ReadLockedCombatTargetPublicationAsync(
        GameApiReadContext context, CancellationToken cancellationToken) => Task.Run(() =>
    {
        var capture = context with { BypassMemoryCache = true };
        var publication = CaptureStable(capture, AionVmmSnapshotChannels.LockedCombatTarget,
            () => ReadLockedCombatTargetCore(capture, cancellationToken));
        if (!publication.UsedFallback && publication.Result is { Success: true, Value: { } target })
            return OperationResult<PublishedGameSnapshot<LockedCombatTargetSnapshot>>.Ok(new(publication.PublicationVersion, target));

        // A retained value remains official, but cannot confirm the current selection for new input.
        return OperationResult<PublishedGameSnapshot<LockedCombatTargetSnapshot>>.Fail(
            publication.ObservedError ?? publication.Result.Error ?? "No verified combat target publication.");
    }, cancellationToken);

    private OperationResult<LockedCombatTargetSnapshot> ReadLockedCombatTargetCore(
        GameApiReadContext context, CancellationToken stop)
    {
        stop.ThrowIfCancellationRequested();
        var connection = GetOrCreateConnection(context.VmmDeviceName);
        lock (connection.SyncRoot)
        {
            stop.ThrowIfCancellationRequested();
            if (!TryResolveProcess(connection.Vmm, context, out var process, out var error))
                return OperationResult<LockedCombatTargetSnapshot>.Fail(error);
            var module = ResolveModuleName();
            var gameBase = process.GetModuleBase(module);
            if (gameBase == 0)
                return OperationResult<LockedCombatTargetSnapshot>.Fail("Module not found: " + module);

            byte[] Read(ulong address, int size)
            {
                stop.ThrowIfCancellationRequested();
                return TryReadBytes(process, address, size, out var bytes, context.BypassMemoryCache)
                    ? bytes : Array.Empty<byte>();
            }

            ulong ResolveEntity(ushort id)
            {
                stop.ThrowIfCancellationRequested();
                return TryReadPointer(process, gameBase + EntitySystemPointerRva, out var system, context.BypassMemoryCache) &&
                       TryReadPointer(process, system + EntityTreeOffset, out var tree, context.BypassMemoryCache) &&
                       TryFindEntityById(process, tree, id, out var entity, context.BypassMemoryCache) ? entity : 0;
            }

            var observed = CaptureLockedCombatTarget(gameBase, Read,
                requests => ReadOpportunityBatch(process, requests, stop), ResolveEntity);
            stop.ThrowIfCancellationRequested();
            return process.GetModuleBase(module) == gameBase
                ? observed
                : OperationResult<LockedCombatTargetSnapshot>.Fail("Combat target module changed during capture.");
        }
    }

    internal static OperationResult<LockedCombatTargetSnapshot> CaptureLockedCombatTarget(
        ulong gameBase,
        Func<ulong, int, byte[]> read,
        Func<IReadOnlyList<(ulong Address, int Size)>, IReadOnlyList<byte[]>> readMany,
        Func<ushort, ulong> resolveEntity)
    {
        try
        {
            var selectionAddress = gameBase + LocalEntityIdRva + sizeof(ushort);
            var selection = read(selectionAddress, sizeof(ushort));
            if (selection.Length != sizeof(ushort))
                return OperationResult<LockedCombatTargetSnapshot>.Fail("Selected combat target id is unavailable.");
            var id = BitConverter.ToUInt16(selection);
            if (id == 0)
                return OperationResult<LockedCombatTargetSnapshot>.Ok(new(0, 0, false));

            var entity = resolveEntity(id);
            if (entity == 0)
                return OperationResult<LockedCombatTargetSnapshot>.Fail("Selected combat target entity is unavailable.");
            var actor = new SkillOpportunityActorResolver(read, readMany, requireUniqueIdentity: true).Read(entity);
            if (actor is null)
                return OperationResult<LockedCombatTargetSnapshot>.Fail("Selected combat target actor is unavailable.");

            var isAlive = false;
            if (actor.ObjectType == LockedTargetSnapshot.MonsterObjectType)
            {
                var health = read(actor.Actor + ActorMaxHpOffset, 2 * sizeof(uint));
                if (health.Length != 2 * sizeof(uint))
                    return OperationResult<LockedCombatTargetSnapshot>.Fail("Selected combat target health is incomplete.");
                var maxHp = BitConverter.ToUInt32(health);
                var currentHp = BitConverter.ToUInt32(health, sizeof(uint));
                if (maxHp == 0 || currentHp > maxHp)
                    return OperationResult<LockedCombatTargetSnapshot>.Fail("Selected combat target health is invalid.");
                isAlive = currentHp > 0;
            }

            if (resolveEntity(id) != entity ||
                actor.Verify(readMany) != SkillOpportunityActorVerification.Complete ||
                !read(selectionAddress, sizeof(ushort)).AsSpan().SequenceEqual(selection))
                return OperationResult<LockedCombatTargetSnapshot>.Fail("Selected combat target changed during capture.");

            return OperationResult<LockedCombatTargetSnapshot>.Ok(new(id, actor.ServerObjectId, isAlive));
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException)
        {
            return OperationResult<LockedCombatTargetSnapshot>.Fail(ex.Message);
        }
    }
}
