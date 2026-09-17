using System.Net;

namespace Hardware.KmBox;

public sealed class KmBoxOptions
{
    public string IpAddress { get; set; } = string.Empty;

    public int Port { get; set; }

    public string Mac { get; set; } = string.Empty;

    public int CommandTimeoutMs { get; set; } = 1000;

    public int SendTimeoutMs { get; set; } = 1000;

    public int ReceiveTimeoutMs { get; set; } = 1000;

    public int DefaultClickHoldMs { get; set; } = 30;

    public int TypeKeyDelayMs { get; set; } = 40;

    public KmBoxOptions CloneAndValidate()
    {
        var clone = new KmBoxOptions
        {
            IpAddress = IpAddress?.Trim() ?? string.Empty,
            Port = Port,
            Mac = NormalizeMac(Mac),
            CommandTimeoutMs = CommandTimeoutMs,
            SendTimeoutMs = SendTimeoutMs,
            ReceiveTimeoutMs = ReceiveTimeoutMs,
            DefaultClickHoldMs = DefaultClickHoldMs,
            TypeKeyDelayMs = TypeKeyDelayMs
        };

        if (!IPAddress.TryParse(clone.IpAddress, out var address) ||
            address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
        {
            throw new ArgumentException("KmBoxOptions.IpAddress must be a valid IPv4 address.", nameof(IpAddress));
        }

        if (clone.Port <= 0 || clone.Port > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(Port), "KmBoxOptions.Port must be between 1 and 65535.");
        }

        if (clone.Mac.Length != 8 || !clone.Mac.All(IsHexCharacter))
        {
            throw new ArgumentException("KmBoxOptions.Mac must be 8 hexadecimal characters.", nameof(Mac));
        }

        if (clone.CommandTimeoutMs <= 0 ||
            clone.SendTimeoutMs <= 0 ||
            clone.ReceiveTimeoutMs <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(CommandTimeoutMs), "KMBox timeouts must be greater than 0.");
        }

        if (clone.DefaultClickHoldMs < 0 || clone.TypeKeyDelayMs < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(DefaultClickHoldMs), "KMBox delays must be greater than or equal to 0.");
        }

        return clone;
    }

    private static string NormalizeMac(string? value)
    {
        return (value ?? string.Empty)
            .Trim()
            .Replace(":", string.Empty)
            .Replace("-", string.Empty)
            .ToUpperInvariant();
    }

    private static bool IsHexCharacter(char value)
    {
        return (value >= '0' && value <= '9') ||
               (value >= 'A' && value <= 'F') ||
               (value >= 'a' && value <= 'f');
    }
}
