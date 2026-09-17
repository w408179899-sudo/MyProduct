using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;

namespace Smart.KmBoxProbe;

public sealed class KmBoxProbeOptions
{
    public IPAddress Address { get; }
    public int Port { get; }
    public TimeSpan Timeout { get; }
    public string DeviceId => $"kmbox:{Address}:{Port}";
    internal uint DeviceCode { get; }
    public KmBoxProbeOptions(string ipAddress, int port, string mac, TimeSpan timeout)
    {
        if (!IPAddress.TryParse(ipAddress?.Trim(), out var address) || address.AddressFamily != AddressFamily.InterNetwork)
            throw new ArgumentException("IpAddress must be a valid IPv4 address.");
        if (port is < 1 or > 65535) throw new ArgumentOutOfRangeException(nameof(port));
        var code = mac?.Trim().Replace(":", "", StringComparison.Ordinal).Replace("-", "", StringComparison.Ordinal);
        if (code is null || code.Length != 8 || !uint.TryParse(code, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var parsed))
            throw new ArgumentException("Mac must contain exactly eight hexadecimal characters.");
        if (timeout < TimeSpan.FromMilliseconds(1) || timeout > TimeSpan.FromSeconds(30))
            throw new ArgumentOutOfRangeException(nameof(timeout), "Timeout must be 1..30000 milliseconds.");
        Address = address; Port = port; DeviceCode = parsed; Timeout = timeout;
    }
    private sealed record ConnectionDocument(string IpAddress, int Port, string Mac);
    public static async Task<KmBoxProbeOptions> LoadAsync(string configPath, TimeSpan timeout)
    {
        if (!Path.IsPathFullyQualified(configPath)) throw new ArgumentException("Use an absolute --config path.");
        await using var stream = new FileStream(configPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length is <= 0 or > 65536) throw new ArgumentException("KMBox configuration must be 1..65536 bytes.");
        var document = await JsonSerializer.DeserializeAsync<ConnectionDocument>(stream,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? throw new ArgumentException("KMBox configuration cannot be null.");
        return new(document.IpAddress, document.Port, document.Mac, timeout);
    }
}
