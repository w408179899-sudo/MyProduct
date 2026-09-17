namespace Smart.Hosting;

// Keep the original constructor and Deconstruct stable for existing package consumers.
public sealed record DmaSettings(string LibraryPath, string DeviceUri, string[] Arguments, int? ProcessId, string ProcessName, string ModuleName)
{
    public DmaDeviceBinding? Binding { get; init; }
    public DmaWorkerSettings Worker { get; init; } = new();
}
public sealed record DmaDeviceIdentity(uint Type, uint VendorProductId, uint LocationId, string SerialNumber, string Description);
public sealed record DmaDeviceBinding(int DeviceIndex, DmaDeviceIdentity Identity, string TopologySha256,
    string DriverFileName, string DriverSha256)
{
    public void Validate()
    {
        if (DeviceIndex is < 0 or > 63 || Identity is null || Identity.Type == 0 || Identity.VendorProductId == 0 ||
            string.IsNullOrWhiteSpace(Identity.SerialNumber) || Identity.SerialNumber.Length > 128 || Identity.SerialNumber.Contains('\0') ||
            string.IsNullOrWhiteSpace(Identity.Description) || Identity.Description.Length > 128 || Identity.Description.Contains('\0'))
            throw new ArgumentException("The DMA binding requires a bounded, complete D3XX device identity.");
        if (DriverFileName is not ("FTD3XX.dll" or "FTD3XXWU.dll") || !IsHash(DriverSha256) || !IsHash(TopologySha256))
            throw new ArgumentException("The DMA binding requires an explicit D3XX driver and SHA256 fingerprints.");
    }
    private static bool IsHash(string value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);
}
