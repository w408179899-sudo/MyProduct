using Roadhog.Core.Model;

namespace Roadhog.Application.StationaryCombat;

public static class StationaryCombatTargetSelector
{
    public static WorldObjectSnapshot? SelectNearest(
        IEnumerable<WorldObjectSnapshot> objects,
        Vector3Snapshot playerPosition,
        Vector3Snapshot stationaryPosition,
        double stationaryRadius)
    {
        var radius = Math.Max(0.0D, stationaryRadius);
        return objects
            .Where(IsSelectableMonster)
            .Where(target => target.Position is not null)
            .Where(target => HorizontalDistance(target.Position!.Value, stationaryPosition) <= radius)
            .OrderBy(target => HorizontalDistance(target.Position!.Value, playerPosition))
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
