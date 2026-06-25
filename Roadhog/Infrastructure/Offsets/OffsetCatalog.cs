namespace Roadhog.Infrastructure.Offsets;

public sealed class OffsetCatalog
{
    private Dictionary<string, OffsetDefinition>? _index;

    public string CatalogName { get; set; } = "Roadhog offsets";

    public string GameKey { get; set; } = "unknown";

    public string Version { get; set; } = "0.1";

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;

    public List<OffsetDefinition> Offsets { get; set; } = new();

    public bool TryGet(string key, out OffsetDefinition definition)
    {
        EnsureIndex();
        return _index!.TryGetValue(key, out definition!);
    }

    public OffsetDefinition Require(string key)
    {
        if (TryGet(key, out var definition))
        {
            return definition;
        }

        throw new KeyNotFoundException("Offset key was not found: " + key);
    }

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var offset in Offsets)
        {
            if (string.IsNullOrWhiteSpace(offset.Key))
            {
                errors.Add("Offset key cannot be empty.");
                continue;
            }

            if (!seen.Add(offset.Key))
            {
                errors.Add("Duplicate offset key: " + offset.Key);
            }

            if (string.IsNullOrWhiteSpace(offset.Group))
            {
                errors.Add(offset.Key + ": group cannot be empty.");
            }

            if (offset.Kind == OffsetKind.Pattern)
            {
                if (string.IsNullOrWhiteSpace(offset.Pattern))
                {
                    errors.Add(offset.Key + ": pattern cannot be empty.");
                }
            }
            else if (!offset.TryGetValue(out _))
            {
                errors.Add(offset.Key + ": valueHex is missing or invalid.");
            }
        }

        return errors;
    }

    private void EnsureIndex()
    {
        if (_index is not null)
        {
            return;
        }

        _index = Offsets.ToDictionary(item => item.Key, StringComparer.OrdinalIgnoreCase);
    }
}
