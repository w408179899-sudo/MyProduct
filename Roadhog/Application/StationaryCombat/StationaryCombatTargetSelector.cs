using Roadhog.Core.Model;

namespace Roadhog.Application.StationaryCombat;

public static class StationaryCombatTargetSelector
{
    public static WorldObjectSnapshot? SelectNearest(
        IEnumerable<WorldObjectSnapshot> objects,
        Vector3Snapshot distanceOrigin,
        Vector3Snapshot stationaryPosition,
        double stationaryRadius,
        bool preferAggressiveMonsters = false,
        Func<WorldObjectSnapshot, double>? distanceResolver = null,
        Func<WorldObjectSnapshot, int>? killPriority = null)
    {
        var radius = Math.Max(0.0D, stationaryRadius);
        var resolveDistance = distanceResolver ??
                              (target => HorizontalDistance(target.Position!.Value, distanceOrigin));
        var candidates = objects
            .Where(IsSelectableMonster)
            .Where(target => target.Position is not null)
            .Where(target => HorizontalDistance(target.Position!.Value, stationaryPosition) <= radius);

        return candidates
            .OrderBy(target =>
            {
                var distance = resolveDistance(target);
                return (killPriority is not null && !IsReachableDistance(distance),
                    killPriority?.Invoke(target) ?? int.MaxValue,
                    !(preferAggressiveMonsters && target.IsAggressiveToPlayer),
                    distance, target.ServerObjectId, target.EntityId);
            })
            .FirstOrDefault();
    }

    internal static bool IsReachableDistance(double distance) => double.IsFinite(distance) && distance < double.MaxValue;

    public static bool IsSelectableMonster(WorldObjectSnapshot target)
    {
        return string.Equals(target.ObjectKind, "monster", StringComparison.OrdinalIgnoreCase) &&
               target.Position is not null &&
               target.IsAlive;
    }

    public static double HorizontalDistance(Vector3Snapshot left, Vector3Snapshot right)
    {
        var dx = left.X - right.X;
        var dy = left.Y - right.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }
}
