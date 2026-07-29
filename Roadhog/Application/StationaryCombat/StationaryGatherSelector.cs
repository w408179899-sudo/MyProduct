using Roadhog.Core.Accounts;
using Roadhog.Core.Model;

namespace Roadhog.Application.StationaryCombat;

public sealed record StationaryGatherCandidate(
    GatherObjectSnapshot Target,
    GatherFilterRuleSettings Rule);

public static class StationaryGatherSelector
{
    public static StationaryGatherCandidate? SelectCandidate(
        GatherSnapshot snapshot,
        GatherScriptSettings settings,
        Vector3Snapshot home,
        double searchRadiusMeters,
        DateTimeOffset now,
        Func<uint, DateTimeOffset, bool>? isSuppressed = null)
    {
        if (!snapshot.CompetitionDataAvailable ||
            !snapshot.MonsterDataAvailable ||
            !snapshot.LocalGathering.DataAvailable)
        {
            return null;
        }

        var rules = (settings.Rules ?? new List<GatherFilterRuleSettings>())
            .Where(rule =>
                rule.Enabled &&
                rule.GatherSourceId != 0 &&
                !string.IsNullOrWhiteSpace(rule.GatherKey))
            .GroupBy(rule => rule.GatherSourceId)
            .ToDictionary(group => group.Key, group => group.First());
        if (rules.Count == 0)
        {
            return null;
        }

        var searchRadius = Math.Max(1.0D, searchRadiusMeters);
        var occupiedRadius = Math.Clamp(settings.OccupiedCheckRadiusMeters, 0.5D, 20.0D);
        var playerPosition = snapshot.LocalPosition ?? home;
        return snapshot.Objects
            .Where(target =>
                target.ServerObjectId != 0 &&
                target.RuntimeAvailabilityRaw != 0 &&
                target.InteractionAvailability == GatherInteractionAvailability.Allowed &&
                (target.Position ?? target.SpawnPosition) is not null &&
                rules.ContainsKey(target.GatherSourceId) &&
                !(isSuppressed?.Invoke(target.ServerObjectId, now) ?? false) &&
                StationaryCombatTargetSelector.HorizontalDistance(
                    (target.Position ?? target.SpawnPosition)!.Value,
                    home) <= searchRadius &&
                !snapshot.IsLikelyOccupied(target, occupiedRadius))
            .OrderBy(target => StationaryCombatTargetSelector.HorizontalDistance(
                (target.Position ?? target.SpawnPosition)!.Value,
                playerPosition))
            .ThenBy(target => target.ServerObjectId)
            .Select(target => new StationaryGatherCandidate(target, rules[target.GatherSourceId]))
            .FirstOrDefault();
    }
}
