using Roadhog.Core.Model;

namespace Roadhog.Core.Paths;

public static class NearbyPathEntry
{
    public const double DistanceLimit = 100;
    public const double HeightTolerance = 2;

    public static int Find(ChannelTransitionSnapshot scene, SharedPathDocument path) =>
        scene.IsReady && !scene.Player!.IsDead ? Find(scene.Channel!.MapId, scene.Player.Position!.Value, path) : -1;

    public static int Find(uint? map, Vector3Snapshot position, SharedPathDocument path)
    {
        if (path.MapId is not > 0 || map != path.MapId) return -1;
        var nearest = -1;
        var distanceSquared = DistanceLimit * DistanceLimit;
        for (var i = 0; i < path.PointCount; i++)
        {
            var point = path.Points[i];
            var height = Math.Abs(position.Z - point.Z);
            var distance = Math.Pow(position.X - point.X, 2) + Math.Pow(position.Y - point.Y, 2);
            if (height <= HeightTolerance && distance < distanceSquared)
            {
                nearest = i;
                distanceSquared = distance;
            }
        }
        return nearest;
    }
}
