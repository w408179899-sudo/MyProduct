namespace Roadhog.Core.Hardware;

public sealed record HardwareDeviceFeature(
    string BindingKey,
    string BindingKind,
    string BindingConfidence,
    string DeviceInstanceId,
    string ParentInstanceId,
    string ContainerId,
    string HardwareId,
    string LocationKey,
    string DisplayName,
    string Manufacturer,
    string VmmDeviceName,
    IReadOnlyList<string> AliasKeys);
