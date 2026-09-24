using System.Diagnostics;
using Roadhog.Application.Workers;
using Roadhog.Core.Model;

namespace Roadhog.Application.StationaryCombat;

public sealed partial class StationaryCombatController
{
    // One published snapshot for this decision, not a cross-tick business cache.
    private async Task<WorldObjectSnapshot?> SelectNextStationaryTargetAsync(
        AccountWorkerContext context, StationaryCombatState state,
        Vector3Snapshot playerPosition, Vector3Snapshot home, double radius,
        bool allowClaimedByOther, WorldObjectSnapshot? gatherThreat = null)
    {
        var timer = Stopwatch.StartNew();
        var objects = await RefreshWorldObjectsAsync(context, state).ConfigureAwait(false);
        var worldMs = timer.Elapsed.TotalMilliseconds;
        var target = await SelectMaintenanceDefenseTargetAsync(context, state, playerPosition, objects)
            .ConfigureAwait(false);
        var defenseMs = timer.Elapsed.TotalMilliseconds - worldMs;
        var defense = target is not null;
        target ??= gatherThreat;
        target ??= await SelectTargetFromSnapshotAsync(context, state, playerPosition, home, radius,
            allowClaimedByOther, objects, allowPreAimReuse: gatherThreat is null &&
                context.Config.ScriptSettings?.Gather.StationaryPriorityEnabled != true).ConfigureAwait(false);
        context.StopToken.ThrowIfCancellationRequested();
        LogActionThrottled(context, state, "stationary_combat.target.selection_timing", "selection", new()
        {
            ["account"] = context.Config.AccountName,
            ["worldReadMs"] = Math.Round(worldMs, 2),
            ["defenseMs"] = Math.Round(defenseMs, 2),
            ["normalSelectionMs"] = Math.Round(timer.Elapsed.TotalMilliseconds - worldMs - defenseMs, 2),
            ["totalMs"] = Math.Round(timer.Elapsed.TotalMilliseconds, 2),
            ["objectCount"] = objects.Count,
            ["defenseSelected"] = defense,
            ["targetServerObjectId"] = target?.ServerObjectId
        }, TimeSpan.FromSeconds(1));
        return target;
    }

    private async Task<WorldObjectSnapshot?> TryReusePostLootCandidateAsync(
        AccountWorkerContext context, StationaryCombatState state, Vector3Snapshot origin,
        Vector3Snapshot home, double radius, WorldObjectSnapshot[] eligible, bool preferAggressive)
    {
        var now = DateTimeOffset.Now;
        if (!IsSmartPreAimEnabled(context) || state.Fighting ||
            state.LastLootAfterKillFinishedAt == DateTimeOffset.MinValue ||
            now - state.LastLootAfterKillFinishedAt > ReadSmartPreAimResultTtl()) return null;
        ushort entityId;
        uint serverId;
        lock (state.NextTargetPreAim.SyncRoot)
        {
            var preAim = state.NextTargetPreAim;
            var observedAt = preAim.LastSnapshotAt != DateTimeOffset.MinValue
                ? preAim.LastSnapshotAt : preAim.TargetSelectedAt;
            if (!preAim.HasCandidate || observedAt == DateTimeOffset.MinValue ||
                now - observedAt > ReadSmartPreAimResultTtl()) return null;
            entityId = preAim.TargetEntityId;
            serverId = preAim.TargetServerObjectId;
        }
        // Require the durable identity as well as the entity slot to avoid slot reuse.
        var candidate = eligible.FirstOrDefault(t => serverId != 0 &&
            t.ServerObjectId == serverId && t.EntityId == entityId);
        if (candidate is null) return null;
        var scores = await ScoreTargetRouteDistancesAsync(context, state, origin, new[] { candidate })
            .ConfigureAwait(false);
        context.StopToken.ThrowIfCancellationRequested();
        var candidateDistance = ResolveTargetDistance(candidate, origin, scores);
        if (!double.IsFinite(candidateDistance) || candidateDistance == double.MaxValue) return null;
        // Straight-line distance is a lower bound on every competitor's route distance.
        // If the candidate wins even against these bounds, full scoring cannot change the winner.
        // Use the existing selector for aggressive priority and deterministic tie breaking.
        var winner = StationaryCombatTargetSelector.SelectNearest(eligible, origin, home, radius,
            preferAggressive, t => ReferenceEquals(t, candidate) ? candidateDistance :
                StationaryCombatTargetSelector.HorizontalDistance(t.Position!.Value, origin));
        if (!ReferenceEquals(winner, candidate)) return null;
        context.Logger.Info("stationary_combat.smart_preaim.post_loot_reused", new Dictionary<string, object?>()
        {
            ["account"] = context.Config.AccountName,
            ["targetEntityId"] = candidate.EntityId,
            ["targetServerObjectId"] = candidate.ServerObjectId,
            ["eligibleCount"] = eligible.Length,
            ["routeScoredCount"] = 1
        });
        return candidate;
    }
}
