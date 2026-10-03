using System.Globalization;
using Roadhog.Core.Api;
using Roadhog.Core.Common;
using Roadhog.Core.Model;
using Vmmsharp;

namespace Roadhog.Infrastructure.Vmm;

internal sealed partial class AionVmmGameApi : ISkillAvailabilityGameApi
{
    private sealed record OpportunitySession(VmmConnection Connection, string Identity, string StoreSessionKey);
    private readonly object _opportunitySessionSync = new();
    private Dictionary<string, OpportunitySession>? _opportunitySessions;

    public Task<OperationResult<SkillAvailabilitySnapshot>> ReadSkillAvailabilityAsync(
        GameApiReadContext context, CancellationToken cancellationToken = default) =>
        Task.Run(() => _stableSnapshotReads.Execute(
            BuildStableSnapshotReadScopeKey(context), AionVmmSnapshotChannels.SkillAvailability,
            () => ReadAndPublishSkillOpportunity(context, cancellationToken)), cancellationToken);

    private OperationResult<SkillAvailabilitySnapshot> ReadAndPublishSkillOpportunity(
        GameApiReadContext context, CancellationToken cancellationToken)
    {
        var scope = BuildStableSnapshotReadScopeKey(context);
        var channel = AionVmmSnapshotChannels.SkillAvailability;
        OpportunitySession? session = null;
        lock (_opportunitySessionSync) _opportunitySessions?.TryGetValue(scope, out session);
        SkillOpportunityRead captured;
        VmmConnection? connection = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            connection = GetOrCreateConnection(context.VmmDeviceName);
            if (session is not null && !ReferenceEquals(session.Connection, connection))
            {
                InvalidateOpportunitySession(scope, session);
                session = null;
            }
            var catalog = GetSkillXmlCatalog();
            if (!string.IsNullOrEmpty(catalog.Error)) throw new InvalidDataException(catalog.Error);
            lock (connection.SyncRoot)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!TryResolveProcess(connection.Vmm, context, out var process, out var error))
                    throw new InvalidDataException(error);
                var module = process.GetModuleBase(ResolveModuleName());
                if (module == 0) throw new InvalidDataException("Module not found for skill opportunity capture.");
                if (!TryReadUInt16(process, module + LocalEntityIdRva, out var entityId, true))
                    throw new InvalidDataException("Skill opportunity role identity capture is incomplete.");
                if (entityId == 0)
                {
                    if (session is not null) InvalidateOpportunitySession(scope, session);
                    session = null;
                    throw new InvalidDataException("Skill opportunity character is no longer active.");
                }
                if (!TryReadPointer(process, module + EntitySystemPointerRva, out var entitySystem, true) ||
                    !TryReadPointer(process, entitySystem + EntityTreeOffset, out var tree, true) ||
                    !TryFindEntityById(process, tree, entityId, out var entity, true))
                    throw new InvalidDataException("Skill opportunity role identity capture is incomplete.");

                byte[] Read(ulong address, int length)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!TryReadBytes(process, address, length, out var bytes, true)) return Array.Empty<byte>();
                    return bytes;
                }
                IReadOnlyList<byte[]> Batch(IReadOnlyList<(ulong Address, int Size)> requests) =>
                    ReadOpportunityBatch(process, requests, cancellationToken);
                var actor = new SkillOpportunityActorResolver(Read, Batch).Read(entity);
                if (actor is null) throw new InvalidDataException("Skill opportunity actor identity capture is incomplete.");

                var characterIdentity = string.Create(CultureInfo.InvariantCulture,
                    $"{SafeGetProcessPid(process)}:{module:X}:{entityId}:{actor.Actor:X}:{actor.ServerObjectId}");
                if (session is not null && !session.Identity.StartsWith(characterIdentity + ":", StringComparison.Ordinal))
                {
                    InvalidateOpportunitySession(scope, session);
                    session = null;
                }

                captured = new SkillOpportunityDecoder(Read, id =>
                    catalog.Details.TryGetValue(id, out var detail) && SupportsSpecialOpportunity(detail), Batch)
                    .Read(module, actor.Actor, includeCombatState: true);
                if ((captured.PresentationInactive || captured.ScopeChanged) && session is not null)
                {
                    InvalidateOpportunitySession(scope, session);
                    session = null;
                }
                if (captured.Completeness != SkillOpportunityReadCompleteness.Failed)
                {
                    if (!TryReadUInt16(process, module + LocalEntityIdRva, out var finalEntityId, true))
                        throw new InvalidDataException("Skill opportunity final role identity capture is incomplete.");
                    var actorVerification = actor.Verify(Batch);
                    if (actorVerification == SkillOpportunityActorVerification.Incomplete)
                        throw new InvalidDataException("Skill opportunity final actor identity capture is incomplete.");
                    if (finalEntityId != entityId || process.GetModuleBase(ResolveModuleName()) != module ||
                        actor.ServerObjectId != captured.ActorServerObjectId || actorVerification == SkillOpportunityActorVerification.Changed)
                    {
                        if (session is not null) InvalidateOpportunitySession(scope, session);
                        session = null;
                        throw new InvalidDataException("Skill opportunity role or module changed during capture.");
                    }

                    var identity = characterIdentity + ":" + captured.BindingSignature + ":" + captured.LayoutIdentity;
                    if (session is null || session.Identity != identity)
                    {
                        if (session is not null) InvalidateOpportunitySession(scope, session);
                        session = new(connection, identity,
                            BuildStableSnapshotSessionKey(context) + "\u001eavailability:" + Guid.NewGuid().ToString("N"));
                        lock (_opportunitySessionSync)
                        {
                            _opportunitySessions ??= new(StringComparer.Ordinal);
                            _opportunitySessions[scope] = session;
                        }
                    }
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            if (session is not null && IsStableSnapshotSessionFailure(ex.Message))
            {
                InvalidateOpportunitySession(scope, session);
                session = null;
            }
            captured = SkillOpportunityRead.Failed(ex.Message);
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (session is null) return OperationResult<SkillAvailabilitySnapshot>.Fail(captured.Error ?? "No official skill opportunity publication exists.");

        // Connection retirement removes the handle under this same lock. A
        // capture from that retired generation cannot republish after reset.
        lock (_connectionSync)
        {
            var connectionKey = BuildConnectionKey(context.VmmDeviceName);
            if (!_connections.TryGetValue(connectionKey, out var current) || !ReferenceEquals(current, session.Connection))
            {
                InvalidateOpportunitySession(scope, session);
                return OperationResult<SkillAvailabilitySnapshot>.Fail("Skill opportunity connection generation changed.");
            }
            var now = DateTimeOffset.Now;
            OperationResult<SkillAvailabilityPublication> observed;
            if (captured.Completeness == SkillOpportunityReadCompleteness.Complete)
            {
                var publication = SkillAvailabilityPublication.Merge(captured, null);
                observed = publication is null
                    ? OperationResult<SkillAvailabilityPublication>.Fail("Incomplete first skill opportunity publication.")
                    : OperationResult<SkillAvailabilityPublication>.Ok(publication);
            }
            else if (captured.Completeness == SkillOpportunityReadCompleteness.Partial &&
                _stableSnapshots.TryUpdate(session.StoreSessionKey, channel, now,
                    prior => SkillAvailabilityPublication.Merge(captured, prior) ?? prior,
                    out var merged, out _))
            {
                observed = OperationResult<SkillAvailabilityPublication>.Ok(merged!);
            }
            else observed = OperationResult<SkillAvailabilityPublication>.Fail(captured.Error ?? "Skill opportunity capture is incomplete.");
            var resolution = _stableSnapshots.Resolve(session.StoreSessionKey, channel, context, observed, now);
            LogStableSnapshotResolution(context, channel.Name, resolution);
            return resolution.Result.Success && resolution.Result.Value is { } value
                ? OperationResult<SkillAvailabilitySnapshot>.Ok(value.ToSnapshot())
                : OperationResult<SkillAvailabilitySnapshot>.Fail(resolution.Result.Error ?? "Skill opportunity capture is incomplete.");
        }
    }

    private static bool SupportsSpecialOpportunity(SkillXmlStaticDetail detail) =>
        HasUsefulSkillXmlValue(detail.CounterSkill) || HasUsefulSkillXmlValue(detail.PrechainCategoryName) ||
        HasUsefulSkillXmlValue(detail.TargetValidStatuses) || HasUsefulSkillXmlValue(detail.SelfConditionStatuses) ||
        string.Equals(detail.UltraTransfer, "1", StringComparison.Ordinal);

    private static IReadOnlyList<byte[]> ReadOpportunityBatch(
        VmmProcess process, IReadOnlyList<(ulong Address, int Size)> requests, CancellationToken stop)
    {
        stop.ThrowIfCancellationRequested();
        if (requests.Count == 0) return Array.Empty<byte[]>();
        using var batch = process.Scatter_Initialize(VmmReadFlagNoCache);
        if (batch is null) throw new InvalidDataException("Could not initialize opportunity batch.");
        var prepared = new bool[requests.Count];
        for (var i = 0; i < requests.Count; i++)
        {
            stop.ThrowIfCancellationRequested();
            prepared[i] = IsLikelyUserPointer(requests[i].Address) && batch.Prepare(requests[i].Address, (uint)requests[i].Size);
        }
        if (!batch.Execute()) throw new InvalidDataException("Could not execute opportunity batch.");
        stop.ThrowIfCancellationRequested();
        return requests.Select((request, i) => prepared[i]
            ? batch.Read(request.Address, (uint)request.Size) : Array.Empty<byte>()).ToArray();
    }

    private void InvalidateOpportunitySession(string scope, OpportunitySession session)
    {
        _stableSnapshots.ClearSession(session.StoreSessionKey);
        lock (_opportunitySessionSync)
        {
            if (_opportunitySessions?.TryGetValue(scope, out var current) == true && ReferenceEquals(current, session))
                _opportunitySessions.Remove(scope);
        }
    }
}
