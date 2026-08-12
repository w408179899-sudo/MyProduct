namespace Roadhog.Application.Radar;

public sealed class RadarMapRevisionRegistry
{
    private readonly object _syncRoot = new();
    private readonly Dictionary<uint, long> _revisions = new();

    public long Get(uint mapId)
    {
        lock (_syncRoot)
        {
            return _revisions.TryGetValue(mapId, out var revision) ? revision : 0L;
        }
    }

    public long Increment(uint mapId)
    {
        if (mapId == 0)
        {
            return 0L;
        }

        lock (_syncRoot)
        {
            var revision = (_revisions.TryGetValue(mapId, out var current) ? current : 0L) + 1L;
            _revisions[mapId] = revision;
            return revision;
        }
    }
}
