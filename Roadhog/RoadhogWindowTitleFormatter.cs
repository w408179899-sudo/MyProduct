namespace Roadhog;

public static class RoadhogWindowTitleFormatter
{
    public const string DefaultBaseTitle = "GreenPlayer";

    private const string UnconfiguredText = "unconfigured";
    private const int MaxHardwareLength = 22;
    private const int MaxKmBoxLength = 22;

    public static string Build(string? hardwareKey, string? kmBoxDeviceText)
    {
        return Build(DefaultBaseTitle, hardwareKey, kmBoxDeviceText, null);
    }

    public static string Build(string? hardwareKey, string? kmBoxDeviceText, string? characterName)
    {
        return Build(DefaultBaseTitle, hardwareKey, kmBoxDeviceText, characterName);
    }

    public static string Build(string? baseTitle, string? hardwareKey, string? kmBoxDeviceText, string? characterName)
    {
        var title = string.IsNullOrWhiteSpace(baseTitle) ? DefaultBaseTitle : baseTitle.Trim();
        var role = characterName?.Trim() ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(role))
        {
            title += " " + role;
        }

        return title;
    }

    public static string FormatHardware(string? hardwareKey)
    {
        var value = hardwareKey?.Trim() ?? string.Empty;
        if (IsAutoHardwareKey(value))
        {
            return "auto";
        }

        if (value.StartsWith("port:", StringComparison.OrdinalIgnoreCase))
        {
            value = value["port:".Length..];
        }
        else if (value.StartsWith("usb:", StringComparison.OrdinalIgnoreCase))
        {
            value = "USB" + value["usb:".Length..];
        }

        value = value
            .Replace("Port_#", "P", StringComparison.OrdinalIgnoreCase)
            .Replace("Hub_#", "H", StringComparison.OrdinalIgnoreCase);

        return Truncate(value, MaxHardwareLength);
    }

    public static string FormatKmBox(string? kmBoxDeviceText)
    {
        var value = kmBoxDeviceText?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(value)
            || string.Equals(value, "(unconfigured)", StringComparison.OrdinalIgnoreCase))
        {
            return UnconfiguredText;
        }

        var endpoint = value;
        var mac = string.Empty;
        var separatorIndex = value.LastIndexOf('/');
        if (separatorIndex > 0 && separatorIndex < value.Length - 1)
        {
            endpoint = value[..separatorIndex].Trim();
            mac = value[(separatorIndex + 1)..].Trim();
        }

        var shortEndpoint = FormatEndpoint(endpoint);
        if (!string.IsNullOrWhiteSpace(mac))
        {
            var shortMac = mac.Length > 4 ? mac[^4..] : mac;
            return Truncate(shortEndpoint + ":" + shortMac, MaxKmBoxLength);
        }

        return Truncate(shortEndpoint, MaxKmBoxLength);
    }

    private static string FormatEndpoint(string endpoint)
    {
        var value = endpoint.Trim();
        var colonIndex = value.LastIndexOf(':');
        if (colonIndex <= 0 || colonIndex >= value.Length - 1)
        {
            return value;
        }

        var host = value[..colonIndex];
        var port = value[(colonIndex + 1)..];
        var dotIndex = host.LastIndexOf('.');
        if (dotIndex >= 0 && dotIndex < host.Length - 1)
        {
            return host[(dotIndex + 1)..] + ":" + port;
        }

        return value;
    }

    private static bool IsAutoHardwareKey(string value)
    {
        return string.IsNullOrWhiteSpace(value)
            || string.Equals(value, "0", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "auto", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "automatic", StringComparison.OrdinalIgnoreCase);
    }

    private static string Truncate(string value, int maxLength)
    {
        if (value.Length <= maxLength)
        {
            return value;
        }

        const string marker = "...";
        var available = maxLength - marker.Length;
        if (available <= 1)
        {
            return value[..maxLength];
        }

        var head = available / 2;
        var tail = available - head;
        return value[..head] + marker + value[^tail..];
    }
}
