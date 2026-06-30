using Roadhog.Core.Model;

namespace Roadhog.Application.StationaryCombat;

public static class StationaryCombatTargetSelector
{
    public static WorldObjectSnapshot? SelectNearest(
        IEnumerable<WorldObjectSnapshot> objects,
        Vector3Snapshot playerPosition,
        Vector3Snapshot stationaryPosition,
        double stationaryRadius,
        bool preferAggressiveMonsters = false)
    {
        var radius = Math.Max(0.0D, stationaryRadius);
        var candidates = objects
            .Where(IsSelectableMonster)
            .Where(target => target.Position is not null)
            .Where(target => HorizontalDistance(target.Position!.Value, stationaryPosition) <= radius);

        if (preferAggressiveMonsters)
        {
            return candidates
                .OrderByDescending(target => target.IsAggressiveToPlayer)
                .ThenBy(target => HorizontalDistance(target.Position!.Value, playerPosition))
                .ThenBy(target => target.ServerObjectId)
                .ThenBy(target => target.EntityId)
                .FirstOrDefault();
        }

        return candidates
            .OrderBy(target => HorizontalDistance(target.Position!.Value, playerPosition))
            .ThenBy(target => target.ServerObjectId)
            .ThenBy(target => target.EntityId)
            .FirstOrDefault();
    }

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
