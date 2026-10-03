using Roadhog.Core.Api;
using Roadhog.Core.Model;

namespace Roadhog.Infrastructure.Vmm;

internal sealed partial class AionVmmGameApi
{
    // Apply this before both replacement and field-aware merging: an unverified
    // Complete label must never authorize pruning through either path.
    internal static WorldObjectReadResult NormalizeWorldObjectRead(WorldObjectReadResult read)
    {
        if (read.Completeness != WorldObjectReadCompleteness.Complete) return read;
        if (HasVerifiedWorldObjectCoverage(read, out var issue)) return read;
        return new WorldObjectReadResult(
            read.Observations.Count == 0 ? WorldObjectReadCompleteness.Failed : WorldObjectReadCompleteness.Partial,
            read.Observations,
            read.Diagnostics with { FirstIssue = issue },
            issue);
    }

    private static bool HasVerifiedWorldObjectCoverage(WorldObjectReadResult read, out string issue)
    {
        issue = "world_tree_coverage_unverified";
        var proof = read.Diagnostics.TreeProof;
        if (proof is null || !proof.HeaderVerified || !proof.LinksVerified || !proof.FinalReadVerified ||
            !string.IsNullOrWhiteSpace(proof.Error)) return false;
        var diagnostics = read.Diagnostics;
        if (diagnostics.NodeIdentityReadFailures != 0 || diagnostics.EntityLookupFailures != 0 ||
            diagnostics.EntityTypeReadFailures != 0 || diagnostics.PositionReadFailures != 0 ||
            diagnostics.ActorResolutionFailures != 0 || diagnostics.ActorIdentityMismatches != 0 ||
            diagnostics.StaticMetadataMisses != 0 || diagnostics.StaticCatalogErrors != 0)
        {
            issue = "world_tree_object_coverage_incomplete";
            return false;
        }
        if (!IsWorldTreePointer(proof.Header) || proof.NodeCount < 0 ||
            proof.NodeCount != read.Diagnostics.ScannedServerObjects ||
            read.Diagnostics.EmittedObjects != read.Observations.Count ||
            read.Observations.Count > proof.NodeCount)
        {
            issue = "world_tree_coverage_counters_mismatch";
            return false;
        }

        if (read.Diagnostics.TraversalTermination == WorldObjectTraversalTermination.EmptyTree)
        {
            if (proof.NodeCount == 0 && read.Observations.Count == 0 && proof.Root == proof.Header &&
                proof.FirstNode == proof.Header && proof.LastNode == proof.Header)
            {
                issue = string.Empty;
                return true;
            }
        }
        else if (read.Diagnostics.TraversalTermination == WorldObjectTraversalTermination.ReachedTreeEnd &&
                 proof.NodeCount > 0 && IsWorldTreePointer(proof.Root) && proof.Root != proof.Header &&
                 IsWorldTreePointer(proof.FirstNode) && proof.FirstNode != proof.Header &&
                 IsWorldTreePointer(proof.LastNode) && proof.LastNode != proof.Header)
        {
            issue = string.Empty;
            return true;
        }

        issue = "world_tree_coverage_termination_mismatch";
        return false;
    }

    private void LogWorldObjectPublication(
        GameApiReadContext context, WorldObjectReadResult original, WorldObjectReadResult effective,
        string mode, IReadOnlyList<WorldObjectSnapshot>? previous, IReadOnlyList<WorldObjectSnapshot>? published)
    {
        var coverageRejected = original.Completeness != effective.Completeness;
        if (!coverageRejected && ReferenceEquals(previous, published)) return;
        if (!coverageRejected && previous is { Count: 0 } && published is { Count: 0 }) return;
        var previousIds = previous?.Select(WorldObjectIdentityKey).ToHashSet(StringComparer.Ordinal)
            ?? new HashSet<string>(StringComparer.Ordinal);
        var publishedIds = published?.Select(WorldObjectIdentityKey).ToHashSet(StringComparer.Ordinal)
            ?? new HashSet<string>(StringComparer.Ordinal);
        var pruned = previousIds.Except(publishedIds).ToArray();
        // Normal non-empty captures are silent. Log the rare decision that can
        // remove targets, and throttle persistent structural faults per session.
        if (!coverageRejected && pruned.Length == 0 &&
            !(mode == "replace" && previous is null && publishedIds.Count == 0)) return;
        var session = BuildStableSnapshotSessionKey(context);
        var now = DateTimeOffset.Now;
        var key = session + "\u001fworld_publication\u001f" + mode + "\u001f" + effective.Diagnostics.FirstIssue;
        lock (_stableSnapshotLogSync)
        {
            if (coverageRejected && _stableSnapshotLogAtByKey.TryGetValue(key, out var last) &&
                now - last < TimeSpan.FromSeconds(2)) return;
            _stableSnapshotLogAtByKey[key] = now;
        }

        var proof = effective.Diagnostics.TreeProof;
        var fields = new Dictionary<string, object?>
        {
            ["account"] = context.AccountName,
            ["session"] = session,
            ["captureSequence"] = effective.Diagnostics.CaptureSequence,
            ["observedCompleteness"] = original.Completeness.ToString(),
            ["effectiveCompleteness"] = effective.Completeness.ToString(),
            ["mode"] = mode,
            ["traversalTermination"] = effective.Diagnostics.TraversalTermination.ToString(),
            ["headerVerified"] = proof?.HeaderVerified ?? false,
            ["linksVerified"] = proof?.LinksVerified ?? false,
            ["finalReadVerified"] = proof?.FinalReadVerified ?? false,
            ["scannedServerObjects"] = effective.Diagnostics.ScannedServerObjects,
            ["observedCount"] = effective.Observations.Count,
            ["previousCount"] = previous?.Count ?? 0,
            ["publishedCount"] = published?.Count ?? 0,
            ["prunedCount"] = pruned.Length,
            ["prunedIdentities"] = string.Join(",", pruned.Take(8)),
            ["coverageRejected"] = coverageRejected,
            ["issue"] = effective.Error ?? effective.Diagnostics.FirstIssue ?? string.Empty
        };
        if (coverageRejected) _logger.Warn("vmm.world_objects.publication", fields);
        else _logger.Info("vmm.world_objects.publication", fields);
    }
}
