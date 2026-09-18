using System.Collections.Immutable;
using System.Net;
using System.Text.Json;
namespace Smart.Hosting;

public enum RuntimeMode { Mock, Hardware }
public sealed record InputSettings(string Address, int Port, string Mac);
public sealed record AccountProfile(string Id, RuntimeMode Mode = RuntimeMode.Mock, DmaSettings? Dma = null,
    InputSettings? Input = null, int ProbeIntervalMs = 1000, int RetryDelayMs = 1000,
    ImmutableDictionary<string, JsonElement>? ModuleSettings = null, bool RecordSnapshots = false)
{
    // Display evidence from the last explicit configuration test, not a live character snapshot.
    public VerifiedCharacter? TestedCharacter { get; init; }
    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Id);
        if (TestedCharacter is { } tested && (string.IsNullOrWhiteSpace(tested.Id) || tested.Id.Length > 256 ||
            string.IsNullOrWhiteSpace(tested.Name) || tested.Name.Length > 256))
            throw new ArgumentException("Invalid tested character identity.");
        if (Id.Length > 128 || ModuleSettings is { Count: > 64 } ||
            ModuleSettings?.Any(x => string.IsNullOrWhiteSpace(x.Key) || x.Key.Length > 128 || x.Value.ValueKind == JsonValueKind.Undefined || x.Value.GetRawText().Length > 65536) == true)
            throw new ArgumentException("Account/module configuration size budget exceeded.");
        if (!Enum.IsDefined(Mode) || ProbeIntervalMs is < 100 or > 60000 || RetryDelayMs is < 100 or > 30000)
            throw new ArgumentException("Invalid runtime mode or session timings.");
        if (Mode == RuntimeMode.Mock) return;
        if (Dma is null || Input is null) throw new ArgumentException("Hardware mode requires DMA and input settings.");
        Dma.Binding?.Validate();
        if (Dma.Worker is null) throw new ArgumentException("DMA worker settings cannot be null.");
        Dma.Worker.Validate();
        if (!Path.IsPathFullyQualified(Dma.LibraryPath) || !File.Exists(Dma.LibraryPath))
            throw new ArgumentException("Provide an existing absolute VMM library path.");
        ArgumentException.ThrowIfNullOrWhiteSpace(Dma.DeviceUri);
        ArgumentException.ThrowIfNullOrWhiteSpace(Dma.ModuleName);
        if (Dma.ProcessId is <= 0 || (Dma.ProcessId is null && string.IsNullOrWhiteSpace(Dma.ProcessName)))
            throw new ArgumentException("Select a PID or an unambiguous process name.");
        if (Dma.Arguments is null || Dma.Arguments.Length > 32 || Dma.Arguments.Any(x => string.IsNullOrWhiteSpace(x) || x.Equals("-device", StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("Device URI is configured separately from bounded VMM arguments.");
        if (Dma.Arguments.Any(x => x.Equals("-norefresh", StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("Live DMA sessions require process/module refresh; -norefresh is incompatible with lifecycle detection.");
        if (!IPAddress.TryParse(Input.Address, out var address) || address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork ||
            Input.Port is < 1 or > 65535 || string.IsNullOrWhiteSpace(Input.Mac) ||
            Input.Mac.Length != 8 || !Input.Mac.All(Uri.IsHexDigit)) throw new ArgumentException("Invalid KMBox endpoint.");
    }
}
public sealed record HostSettings(ImmutableArray<AccountProfile> Accounts)
{
    public static HostSettings Empty { get; } = new([]);
    public void Validate()
    {
        if (Accounts.IsDefault || Accounts.Length > 64 || Accounts.Any(x => x is null) || Accounts.Select(x => x.Id).Distinct(StringComparer.Ordinal).Count() != Accounts.Length)
            throw new ArgumentException("Require at most 64 uniquely named accounts.");
        foreach (var account in Accounts) account.Validate();
    }
}
