namespace Roadhog.Core.Diagnostics;

public sealed class InMemoryRoadhogLogger : IRoadhogLogger
{
    private readonly List<RoadhogLogEntry> _entries = new();
    private readonly object _syncRoot = new();

    public IReadOnlyList<RoadhogLogEntry> Entries
    {
        get
        {
            lock (_syncRoot)
            {
                return _entries.ToArray();
            }
        }
    }

    public void Info(string eventName, IReadOnlyDictionary<string, object?>? fields = null)
    {
        Append("info", eventName, fields, null);
    }

    public void Warn(string eventName, IReadOnlyDictionary<string, object?>? fields = null)
    {
        Append("warn", eventName, fields, null);
    }

    public void Error(string eventName, Exception exception, IReadOnlyDictionary<string, object?>? fields = null)
    {
        Append("error", eventName, fields, exception.Message);
    }

    private void Append(string level, string eventName, IReadOnlyDictionary<string, object?>? fields, string? error)
    {
        var safeFields = fields is null
            ? new Dictionary<string, object?>()
            : new Dictionary<string, object?>(fields);

        lock (_syncRoot)
        {
            _entries.Add(new RoadhogLogEntry(DateTimeOffset.Now, level, eventName, safeFields, error));
        }
    }
}
