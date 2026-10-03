using Roadhog.Core.Model;
using Vmmsharp;

namespace Roadhog.Infrastructure.Vmm;

internal sealed partial class AionVmmGameApi
{
    private static WorldObjectReadAttempt ReadVerifiedWorldObjects(
        VmmProcess process, ulong gameBase, string moduleName, ulong entitySystem, ulong entityTreeHeader,
        ushort localEntityId, float localX, float localY, float localZ,
        uint localServerObjectId, bool localServerObjectIdAvailable, NpcXmlCatalog npcCatalog,
        bool bypassMemoryCache, WorldObjectReadCounters counters, bool structuralPartial, string firstIssue)
    {
        byte[] Read(ulong address, int size) =>
            TryReadBytes(process, address, size, out var bytes, bypassMemoryCache) && bytes.Length == size
                ? bytes : Array.Empty<byte>();
        var headerBytes = Read(gameBase + ServerObjectTreeRva, 8);
        if (headerBytes.Length != 8 || !IsLikelyUserPointer(BitConverter.ToUInt64(headerBytes)))
            return WorldObjectReadAttempt.Failed("failed to read ServerObject tree header", counters);
        var capture = new WorldObjectTreeCaptureDecoder(Read).Capture(BitConverter.ToUInt64(headerBytes));
        if (!capture.StructureComplete)
        {
            structuralPartial = true;
            SetFirstWorldObjectReadIssue(ref firstIssue, capture.Issue ?? "world_tree_coverage_unverified");
        }

        // These are existing scene anchors, not guessed node-count offsets.
        // A changed owner invalidates the whole capture instead of merging its
        // observations into an unrelated scene's publication.
        var anchors = new (ulong Address, byte[] Bytes)[]
        {
            (gameBase + ServerObjectTreeRva, headerBytes),
            (gameBase + EntitySystemPointerRva, BitConverter.GetBytes(entitySystem)),
            (entitySystem + EntityTreeOffset, BitConverter.GetBytes(entityTreeHeader)),
            (gameBase + LocalEntityIdRva, BitConverter.GetBytes(localEntityId))
        };
        var observations = new List<(ulong Address, WorldObjectObservation Observation)>();
        var duplicateIds = capture.Nodes.Where(node => node.ServerObjectId != 0)
            .GroupBy(node => node.ServerObjectId).Where(group => group.Count() > 1)
            .Select(group => group.Key).ToHashSet();
        foreach (var node in capture.Nodes)
        {
            counters.ScannedServerObjects++;
            if (duplicateIds.Contains(node.ServerObjectId))
            {
                counters.NodeIdentityReadFailures++;
                structuralPartial = true;
                SetFirstWorldObjectReadIssue(ref firstIssue, "world_node_identity_duplicate");
                continue;
            }

            if (!TryReadWorldObjectObservation(process, entityTreeHeader,
                    node.ServerObjectId, node.EntityId, localEntityId, localX, localY, localZ,
                    localServerObjectId, localServerObjectIdAvailable, npcCatalog.Details,
                    bypassMemoryCache, out var observation, ref counters, ref firstIssue))
                structuralPartial = true;
            if (observation is not null) observations.Add((node.Address, observation));
        }

        var requests = anchors.Select(anchor => (anchor.Address, anchor.Bytes.Length))
            .Concat(capture.Guards.Select(guard => (guard.Address, guard.Bytes.Length))).ToArray();
        IReadOnlyList<byte[]> finalBlocks;
        try
        {
            // One NOCACHE batch verifies structural/identity fields only. HP,
            // position, target changes and tree color are normal live updates.
            finalBlocks = ReadOpportunityBatch(process, requests, CancellationToken.None);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException)
        {
            return WorldObjectReadAttempt.Failed("world_tree_final_capture_read_failed: " + ex.Message, counters);
        }

        if (finalBlocks.Count != requests.Length || anchors.Where((anchor, i) =>
                finalBlocks[i] is null || !finalBlocks[i].AsSpan().SequenceEqual(anchor.Bytes)).Any())
            return WorldObjectReadAttempt.Failed("world_capture_anchor_changed_or_read_failed", counters);
        if (process.GetModuleBase(moduleName) != gameBase)
            return WorldObjectReadAttempt.Failed("Module not found at captured base: world capture module changed", counters);

        var verification = capture.Verify(finalBlocks.Skip(anchors.Length).ToArray());
        if (!verification.StructureUnchanged)
        {
            structuralPartial = true;
            SetFirstWorldObjectReadIssue(ref firstIssue, verification.Issue ?? "world_tree_final_capture_changed");
        }
        counters.NodeIdentityReadFailures += verification.InvalidIdentityNodeAddresses.Count;
        var trustedObservations = observations
            .Where(item => !verification.InvalidIdentityNodeAddresses.Contains(item.Address))
            .Select(item => item.Observation)
            .ToList();
        // Keep the existing distance ordering, including its tie behavior.
        trustedObservations.Sort(static (left, right) =>
            (left.Snapshot.DistanceToLocalPlayer ?? double.MaxValue)
                .CompareTo(right.Snapshot.DistanceToLocalPlayer ?? double.MaxValue));
        var proof = new WorldObjectTreeProof(capture.Header, capture.Root, capture.FirstNode, capture.LastNode,
            capture.Nodes.Count, capture.HeaderVerified, capture.StructureComplete,
            verification.StructureUnchanged, capture.Issue ?? verification.Issue);
        return new WorldObjectReadAttempt(
            structuralPartial
                ? trustedObservations.Count == 0 ? WorldObjectReadCompleteness.Failed : WorldObjectReadCompleteness.Partial
                : WorldObjectReadCompleteness.Complete,
            trustedObservations, counters, capture.Termination, localServerObjectIdAvailable,
            firstIssue, structuralPartial ? firstIssue : null, proof);
    }
}
