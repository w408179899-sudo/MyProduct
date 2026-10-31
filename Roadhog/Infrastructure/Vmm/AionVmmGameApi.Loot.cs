using Roadhog.Core.Api;
using Roadhog.Core.Common;
using Roadhog.Core.Model;

namespace Roadhog.Infrastructure.Vmm;

internal sealed partial class AionVmmGameApi : ILootInteractionGameApi
{
    private sealed record LootReadSession(VmmConnection Connection, string Identity, string StoreKey);
    private readonly Dictionary<string, LootReadSession> _lootReadSessions = new(StringComparer.Ordinal);

    public Task<OperationResult<LootInteractionSnapshot>> ReadLootInteractionAsync(
        GameApiReadContext context, CancellationToken cancellationToken = default) =>
        Task.Run(() => _stableSnapshotReads.Execute(BuildStableSnapshotReadScopeKey(context),
            AionVmmSnapshotChannels.LootInteraction, () => ReadAndPublishLootInteraction(context, cancellationToken)), cancellationToken);

    private OperationResult<LootInteractionSnapshot> ReadAndPublishLootInteraction(GameApiReadContext context, CancellationToken stop)
    {
        var scope = BuildStableSnapshotReadScopeKey(context);
        LootReadSession? session;
        lock (_connectionSync) _lootReadSessions.TryGetValue(scope, out session);
        var captured = new LootInteractionRead(null, null, null, "No loot interaction captured.");
        try
        {
            stop.ThrowIfCancellationRequested();
            var connection = GetOrCreateConnection(context.VmmDeviceName);
            if (session is not null && !ReferenceEquals(session.Connection, connection)) Invalidate();
            lock (connection.SyncRoot)
            {
                stop.ThrowIfCancellationRequested();
                if (!TryResolveProcess(connection.Vmm, context, out var process, out var error))
                    throw new InvalidDataException(error);
                var module = process.GetModuleBase(ResolveModuleName());
                if (module == 0) throw new InvalidDataException("Module not found: Game.dll");
                if (!TryReadUInt16(process, module + LocalEntityIdRva, out var entityId, true) || entityId is 0xCDCD or 0xCCCC)
                    throw new InvalidDataException("Incomplete loot session identity.");
                if (entityId == 0) { Invalidate(); throw new InvalidDataException("Loot character session ended."); }
                if (!TryReadUInt64(process, module + EntitySystemPointerRva, out var entitySystem, true) ||
                    !IsLikelyUserPointer(entitySystem) || entitySystem < 0x10000 ||
                    entitySystem is 0xCDCDCDCD or 0xCCCCCCCC || (entitySystem & 7) != 0)
                    throw new InvalidDataException("Incomplete loot entity system identity.");
                var identity = $"{SafeGetProcessPid(process)}:{module:X}:{entityId}:{entitySystem:X}";
                if (session is not null && session.Identity != identity) Invalidate();
                captured = new LootInteractionDecoder((address, length) =>
                {
                    stop.ThrowIfCancellationRequested();
                    return TryReadBytes(process, address, length, out var bytes, true) ? bytes : [];
                }).Capture(module);
                if (!TryReadUInt16(process, module + LocalEntityIdRva, out var finalId, true) || finalId is 0xCDCD or 0xCCCC)
                    throw new InvalidDataException("Incomplete final loot session identity.");
                if (entityId != finalId)
                { Invalidate(); throw new InvalidDataException("Loot character changed during capture."); }
                if (!TryReadUInt64(process, module + EntitySystemPointerRva, out var finalSystem, true))
                    throw new InvalidDataException("Incomplete final loot entity system identity.");
                if (entitySystem != finalSystem || module != process.GetModuleBase(ResolveModuleName()))
                { Invalidate(); throw new InvalidDataException("Loot session changed during capture."); }
                session ??= new(connection, identity,
                    BuildStableSnapshotSessionKey(context) + "\u001eloot:" + Guid.NewGuid().ToString("N"));
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            if (IsStableSnapshotSessionFailure(ex.Message)) Invalidate();
            captured = new(null, null, null, ex.Message);
        }

        stop.ThrowIfCancellationRequested();
        if (session is null) return OperationResult<LootInteractionSnapshot>.Fail(captured.Error ?? "No loot session.");
        lock (_connectionSync)
        {
            stop.ThrowIfCancellationRequested();
            if (!_connections.TryGetValue(BuildConnectionKey(context.VmmDeviceName), out var current) ||
                !ReferenceEquals(current, session.Connection))
            { Invalidate(); return OperationResult<LootInteractionSnapshot>.Fail("Loot connection generation changed."); }
            _lootReadSessions[scope] = session;
            var channel = AionVmmSnapshotChannels.LootInteraction;
            var now = DateTimeOffset.Now;
            var value = captured.Merge(null);
            if (value is null && captured.HasFields)
                _stableSnapshots.TryUpdate(session.StoreKey, channel, now,
                    prior => captured.Merge(prior)!, out value, out _);
            var observed = value is not null
                ? OperationResult<LootInteractionSnapshot>.Ok(value)
                : OperationResult<LootInteractionSnapshot>.Fail(captured.Error ?? "Incomplete loot interaction.");
            var resolution = _stableSnapshots.Resolve(session.StoreKey, channel, context, observed, now);
            LogStableSnapshotResolution(context, channel.Name, resolution);
            return resolution.Result;
        }

        void Invalidate()
        {
            if (session is null) return;
            _stableSnapshots.ClearSession(session.StoreKey);
            lock (_connectionSync) _lootReadSessions.Remove(scope);
            session = null;
        }
    }
}
