using System.Globalization;

namespace Roadhog.Infrastructure.Offsets;

public sealed class OffsetDefinition
{
    public string Key { get; set; } = string.Empty;

    public string Group { get; set; } = string.Empty;

    public string Module { get; set; } = string.Empty;

    public OffsetKind Kind { get; set; }

    public string? ValueHex { get; set; }

    public string? Pattern { get; set; }

    public string? Description { get; set; }

    public string? Source { get; set; }

    public string? VerifiedBuild { get; set; }

    public DateTimeOffset? VerifiedAt { get; set; }

    public bool TryGetValue(out ulong value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(ValueHex))
        {
            return false;
        }

        var text = ValueHex.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? ValueHex[2..]
            : ValueHex;

        return ulong.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
    }
}
