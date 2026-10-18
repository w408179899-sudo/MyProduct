using Roadhog.Core.Model;

namespace Roadhog.Core.Paths;

// Forecast only the ordered travel legs; execution rechecks the actual scene at each leg.
public sealed class ReturnItemPlan(ChannelTransitionSnapshot scene)
{
    private uint? map = scene.IsReady ? scene.Channel!.MapId : null;
    private Vector3Snapshot position = scene.Player?.Position ?? default;
    private readonly Dictionary<string, long> required = new(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyDictionary<string, long> Required => required;
    public bool NeedsReturn(SharedPathDocument path) => NearbyPathEntry.Find(map, position, path) < 0;

    public void ReturnTo(SharedPathDocument path, string? itemName)
    {
        if (!NeedsReturn(path)) return;
        if (itemName != null)
        {
            var name = itemName.Trim();
            required[name] = required.GetValueOrDefault(name) + 1;
        }
        map = path.MapId;
        position = path.Points[0].ToVector3();
    }

    public void Add(SharedPathDocument path, string? itemName, bool finishAtStart = false)
    {
        ReturnTo(path, itemName);
        map = path.MapId;
        position = path.Points[finishAtStart ? 0 : path.PointCount - 1].ToVector3();
    }
}
