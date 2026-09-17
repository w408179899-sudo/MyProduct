using System.Globalization;
namespace Smart.ProbeProtocol;

public sealed record ProbeManifest(int SchemaVersion, Guid SessionId, int ProcessId, string ProcessName,
    DateTimeOffset ProcessStartedAt, string MainModuleName, string Address, int Length, int PointerSize,
    string Mode, int IntervalMs, int TornWindowMs)
{
    public ulong GetAddress()
    {
        if (Address is null || !Address.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ||
            !ulong.TryParse(Address.AsSpan(2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var address) ||
            address == 0 || address > ulong.MaxValue - ProbeMemoryProtocol.Size)
            throw new InvalidDataException("Manifest must contain a nonzero hexadecimal memory address.");
        return address;
    }

    public void Validate()
    {
        if (SchemaVersion != ProbeMemoryProtocol.Version || SessionId == Guid.Empty || ProcessId <= 0 ||
            string.IsNullOrWhiteSpace(ProcessName) || ProcessName.Length > 260 ||
            string.IsNullOrWhiteSpace(MainModuleName) || MainModuleName.Length > 260 ||
            ProcessStartedAt <= DateTimeOffset.UnixEpoch || Length != ProbeMemoryProtocol.Size || PointerSize != 8 ||
            Mode is not ("stable" or "mixed" or "torn") || IntervalMs is < 10 or > 60000 || TornWindowMs is < 1 or > 60000)
            throw new InvalidDataException("Unsupported or incomplete probe manifest.");
        _ = GetAddress();
    }
}
