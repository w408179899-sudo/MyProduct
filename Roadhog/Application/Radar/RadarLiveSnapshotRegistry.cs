using Roadhog.Core.Model;
using Roadhog.Core.Radar;

namespace Roadhog.Application.Radar;

public sealed class RadarLiveSnapshotRegistry
{
    private readonly object _syncRoot = new();
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);

    public void PublishPlayer(string account, PlayerSnapshot player)
    {
        if (string.IsNullOrWhiteSpace(account))
        {
            return;
        }

        lock (_syncRoot)
        {
            var entry = GetOrCreate(account);
            entry.Player = player;
            entry.PlayerCapturedAt = player.CapturedAt;
        }
    }

    public void PublishWorldObjects(string account, IReadOnlyList<WorldObjectSnapshot> worldObjects, DateTimeOffset capturedAt)
    {
        if (string.IsNullOrWhiteSpace(account))
        {
            return;
        }

        lock (_syncRoot)
        {
            var entry = GetOrCreate(account);
            entry.WorldObjects = worldObjects.ToArray();
            entry.WorldObjectsCapturedAt = capturedAt;
        }
    }

    public void PublishMapId(string account, uint mapId, DateTimeOffset capturedAt)
    {
        if (string.IsNullOrWhiteSpace(account) || mapId == 0)
        {
            return;
        }

        lock (_syncRoot)
        {
            var entry = GetOrCreate(account);
            entry.MapId = mapId;
            entry.MapCapturedAt = capturedAt;
        }
    }

    public bool TryGetFresh(
        string account,
        DateTimeOffset now,
        TimeSpan playerMaximumAge,
        TimeSpan worldObjectsMaximumAge,
        TimeSpan mapMaximumAge,
        out RadarLiveSnapshot snapshot)
    {
        lock (_syncRoot)
        {
            if (!_entries.TryGetValue(account, out var entry) ||
                entry.Player?.Position is null ||
                entry.MapId == 0 ||
                now - entry.PlayerCapturedAt > playerMaximumAge ||
                now - entry.WorldObjectsCapturedAt > worldObjectsMaximumAge ||
                now - entry.MapCapturedAt > mapMaximumAge)
            {
                snapshot = new RadarLiveSnapshot(0, null, Array.Empty<WorldObjectSnapshot>(), now, "cache_miss");
                return false;
            }

            var capturedAt = new[]
            {
                entry.PlayerCapturedAt,
                entry.WorldObjectsCapturedAt,
                entry.MapCapturedAt
            }.Min();
            snapshot = new RadarLiveSnapshot(
                entry.MapId,
                entry.Player,
                entry.WorldObjects,
                capturedAt);
            return true;
        }
    }

    private Entry GetOrCreate(string account)
    {
        if (!_entries.TryGetValue(account, out var entry))
        {
            entry = new Entry();
            _entries[account] = entry;
        }

        return entry;
    }

    private sealed class Entry
    {
        public uint MapId { get; set; }

        public DateTimeOffset MapCapturedAt { get; set; } = DateTimeOffset.MinValue;

        public PlayerSnapshot? Player { get; set; }

        public DateTimeOffset PlayerCapturedAt { get; set; } = DateTimeOffset.MinValue;

        public IReadOnlyList<WorldObjectSnapshot> WorldObjects { get; set; } = Array.Empty<WorldObjectSnapshot>();

        public DateTimeOffset WorldObjectsCapturedAt { get; set; } = DateTimeOffset.MinValue;
    }
}
