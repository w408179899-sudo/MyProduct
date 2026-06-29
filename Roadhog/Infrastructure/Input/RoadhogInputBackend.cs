namespace Roadhog.Infrastructure.Input;

public enum RoadhogInputBackend
{
    HardwareBox,
    KmBoxNet
}

public static class RoadhogInputBackendParser
{
    public static RoadhogInputBackend ParseOrDefault(
        string? value,
        RoadhogInputBackend fallback = RoadhogInputBackend.HardwareBox)
    {
        return TryParse(value, out var backend) ? backend : fallback;
    }

    public static bool TryParse(string? value, out RoadhogInputBackend backend)
    {
        backend = RoadhogInputBackend.HardwareBox;
        var normalized = Normalize(value);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return false;
        }

        switch (normalized)
        {
            case "hardwarebox":
            case "hardware":
            case "serial":
            case "serialbox":
            case "kmboxserial":
            case "kmboxbplus":
                backend = RoadhogInputBackend.HardwareBox;
                return true;
            case "kmboxnet":
            case "kmnet":
            case "net":
            case "udp":
                backend = RoadhogInputBackend.KmBoxNet;
                return true;
            default:
                return false;
        }
    }

    private static string Normalize(string? value)
    {
        return (value ?? string.Empty)
            .Trim()
            .Replace("_", string.Empty)
            .Replace("-", string.Empty)
            .Replace(" ", string.Empty)
            .ToLowerInvariant();
    }
}
