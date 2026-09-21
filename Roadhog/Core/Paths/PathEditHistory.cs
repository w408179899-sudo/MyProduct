namespace Roadhog.Core.Paths;

/// <summary>Bounded, independent undo history for one path editor.</summary>
public sealed class PathEditHistory
{
    private const int Limit = 50;
    private readonly List<(SharedPathDocument Document, int Selection)> _entries = new();
    public bool CanUndo => _entries.Count > 0;

    public void Remember(SharedPathDocument document, int selection)
    {
        if (_entries.Count == Limit) _entries.RemoveAt(0);
        _entries.Add((document.Clone(), selection));
    }

    public bool TryUndo(PathRecordingBuffer buffer, out int selection)
    {
        selection = -1;
        if (!CanUndo) return false;
        var entry = _entries[^1];
        _entries.RemoveAt(_entries.Count - 1);
        buffer.Load(entry.Document.Points, entry.Document.MapId);
        selection = entry.Selection;
        return true;
    }

    public void Clear() => _entries.Clear();
}
