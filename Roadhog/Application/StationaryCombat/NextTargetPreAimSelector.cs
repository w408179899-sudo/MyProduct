using Roadhog.Core.Model;

namespace Roadhog.Application.StationaryCombat;

public static class NextTargetPreAimSelector
{
    public static NextTargetPreAimSelection? Select(
        IEnumerable<WorldObjectSnapshot> objects,
        Vector3Snapshot distanceOrigin,
        Vector3Snapshot home,
        double radius,
        ushort currentTargetEntityId,
        uint currentTargetServerObjectId,
        uint localSideServerObjectId,
        uint localSidePetServerObjectId,
        bool allowClaimedByOther,
        bool preferAggressiveMonsters,
        IReadOnlyCollection<string>? activeMonsterNameFilters = null,
        NextTargetPreAimSelection? currentSelection = null,
        DateTimeOffset? now = null,
        TimeSpan? minimumHold = null,
        double switchDistanceMargin = 2.0D,
        NextTargetPreAimExclusionSnapshot? exclusions = null,
        IReadOnlySet<uint>? teamSideServerObjectIds = null,
        Func<WorldObjectSnapshot, double>? distanceResolver = null)
    {
        var effectiveNow = now ?? DateTimeOffset.Now;
        var effectiveExclusions = exclusions ?? NextTargetPreAimExclusionSnapshot.Empty;
        var resolveDistance = distanceResolver ??
                              (target => StationaryCombatTargetSelector.HorizontalDistance(
                                  target.Position!.Value,
                                  distanceOrigin));
        var candidates = objects
            .Where(StationaryCombatTargetSelector.IsSelectableMonster)
            .Where(target => !StationaryCombatState.IsSameTarget(
                target.EntityId,
                target.ServerObjectId,
                currentTargetEntityId,
                currentTargetServerObjectId))
            .Where(target => !effectiveExclusions.IsTemporarilyExcluded(target))
            .Where(target => IsTargetingLocalSide(
                target,
                localSideServerObjectId,
                localSidePetServerObjectId) ||
                IsTargetingTeamSide(target, teamSideServerObjectIds) ||
                IsOrdinaryTargetEligible(
                    target,
                    effectiveExclusions,
                    activeMonsterNameFilters,
                    allowClaimedByOther,
                    localSideServerObjectId,
                    localSidePetServerObjectId,
                    teamSideServerObjectIds))
            .Select(target => BuildSelection(
                target,
                distanceOrigin,
                home,
                radius,
                localSideServerObjectId,
                localSidePetServerObjectId,
                teamSideServerObjectIds,
                preferAggressiveMonsters,
                effectiveNow,
                resolveDistance))
            .Where(selection => selection is not null)
            .Cast<NextTargetPreAimSelection>()
            .OrderByDescending(selection => selection.PriorityTier)
            .ThenBy(selection => selection.DistanceToOrigin)
            .ThenBy(selection => selection.Target.ServerObjectId)
            .ThenBy(selection => selection.Target.EntityId)
            .ToArray();

        if (candidates.Length == 0)
        {
            return null;
        }

        var best = candidates[0];
        if (currentSelection is null)
        {
            return best with { DecisionReason = "selected" };
        }

        var current = candidates.FirstOrDefault(candidate =>
            StationaryCombatState.IsSameTarget(
                candidate.Target.EntityId,
                candidate.Target.ServerObjectId,
                currentSelection.Target.EntityId,
                currentSelection.Target.ServerObjectId));
        if (current is null)
        {
            return best with { DecisionReason = "current_invalid" };
        }

        if (StationaryCombatState.IsSameTarget(
            best.Target.EntityId,
            best.Target.ServerObjectId,
            current.Target.EntityId,
            current.Target.ServerObjectId))
        {
            return current with
            {
                SelectedAt = currentSelection.SelectedAt,
                DecisionReason = "kept_best"
            };
        }

        if (best.PriorityTier > current.PriorityTier)
        {
            return best with { DecisionReason = "higher_priority" };
        }

        if (best.PriorityTier < current.PriorityTier)
        {
            return current with
            {
                SelectedAt = currentSelection.SelectedAt,
                DecisionReason = "kept_higher_priority_current"
            };
        }

        var hold = minimumHold ?? TimeSpan.FromSeconds(1);
        if (currentSelection.SelectedAt != DateTimeOffset.MinValue &&
            effectiveNow - currentSelection.SelectedAt < hold)
        {
            return current with
            {
                SelectedAt = currentSelection.SelectedAt,
                DecisionReason = "kept_hold"
            };
        }

        if (current.DistanceToOrigin - best.DistanceToOrigin < Math.Max(0.0D, switchDistanceMargin))
        {
            return current with
            {
                SelectedAt = currentSelection.SelectedAt,
                DecisionReason = "kept_stability"
            };
        }

        return best with { DecisionReason = "closer_after_hold" };
    }

    private static NextTargetPreAimSelection? BuildSelection(
        WorldObjectSnapshot target,
        Vector3Snapshot distanceOrigin,
        Vector3Snapshot home,
        double radius,
        uint localSideServerObjectId,
        uint localSidePetServerObjectId,
        IReadOnlySet<uint>? teamSideServerObjectIds,
        bool preferAggressiveMonsters,
        DateTimeOffset selectedAt,
        Func<WorldObjectSnapshot, double> distanceResolver)
    {
        if (target.Position is null)
        {
            return null;
        }

        var targetingLocalSide = IsTargetingLocalSide(
            target,
            localSideServerObjectId,
            localSidePetServerObjectId);
        var targetingTeamSide = !targetingLocalSide &&
                                IsTargetingTeamSide(target, teamSideServerObjectIds);
        var homeDistance = StationaryCombatTargetSelector.HorizontalDistance(target.Position.Value, home);
        if (!targetingLocalSide &&
            !targetingTeamSide &&
            homeDistance > Math.Max(0.0D, radius))
        {
            return null;
        }

        var aggressivePriority = preferAggressiveMonsters && target.IsAggressiveToPlayer;
        var priorityTier = targetingLocalSide
            ? 4
            : targetingTeamSide
                ? 3
                : aggressivePriority
                    ? 2
                    : 1;
        return new NextTargetPreAimSelection(
            target,
            priorityTier,
            distanceResolver(target),
            targetingLocalSide,
            targetingTeamSide,
            aggressivePriority,
            selectedAt,
            "selected");
    }

    private static bool IsActiveMonsterFiltered(
        WorldObjectSnapshot target,
        IReadOnlyCollection<string>? activeMonsterNameFilters)
    {
        if (activeMonsterNameFilters is null ||
            activeMonsterNameFilters.Count == 0 ||
            string.IsNullOrWhiteSpace(target.Name))
        {
            return false;
        }

        var targetName = target.Name.Trim();
        return activeMonsterNameFilters.Any(filter =>
            !string.IsNullOrWhiteSpace(filter) &&
            string.Equals(targetName, filter.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsOrdinaryTargetEligible(
        WorldObjectSnapshot target,
        NextTargetPreAimExclusionSnapshot exclusions,
        IReadOnlyCollection<string>? activeMonsterNameFilters,
        bool allowClaimedByOther,
        uint localSideServerObjectId,
        uint localSidePetServerObjectId,
        IReadOnlySet<uint>? teamSideServerObjectIds)
    {
        return !exclusions.IsIgnored(target) &&
               !IsActiveMonsterFiltered(target, activeMonsterNameFilters) &&
               (allowClaimedByOther || !IsClaimedByOther(
                   target,
                   localSideServerObjectId,
                   localSidePetServerObjectId,
                   teamSideServerObjectIds));
    }

    private static bool IsClaimedByOther(
        WorldObjectSnapshot target,
        uint localSideServerObjectId,
        uint localSidePetServerObjectId,
        IReadOnlySet<uint>? teamSideServerObjectIds)
    {
        return target.TargetServerObjectId != 0 &&
               target.TargetServerObjectId != target.ServerObjectId &&
               !IsTargetingLocalSide(target, localSideServerObjectId, localSidePetServerObjectId) &&
               !IsTargetingTeamSide(target, teamSideServerObjectIds);
    }

    private static bool IsTargetingLocalSide(
        WorldObjectSnapshot target,
        uint localSideServerObjectId,
        uint localSidePetServerObjectId)
    {
        if (target.IsTargetingLocalPlayer)
        {
            return true;
        }

        return target.TargetServerObjectId != 0 &&
               ((localSideServerObjectId != 0 && target.TargetServerObjectId == localSideServerObjectId) ||
                (localSidePetServerObjectId != 0 && target.TargetServerObjectId == localSidePetServerObjectId));
    }

    private static bool IsTargetingTeamSide(
        WorldObjectSnapshot target,
        IReadOnlySet<uint>? teamSideServerObjectIds)
    {
        return target.TargetServerObjectId != 0 &&
               teamSideServerObjectIds?.Contains(target.TargetServerObjectId) == true;
    }
}

public sealed record NextTargetPreAimSelection(
    WorldObjectSnapshot Target,
    int PriorityTier,
    double DistanceToOrigin,
    bool IsTargetingLocalSide,
    bool IsTargetingTeamSide,
    bool IsAggressivePriority,
    DateTimeOffset SelectedAt,
    string DecisionReason)
{
    public bool IsTargetingProtectedSide =>
        IsTargetingLocalSide || IsTargetingTeamSide;
}
