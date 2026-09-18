using System.Collections.Immutable;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using Smart.Hosting;

namespace SampleProject.Desktop.ViewModels;

public partial class ProfileEditor : ObservableObject
{
    [ObservableProperty] private string _id = "local";
    [ObservableProperty] private RuntimeMode _mode;
    [ObservableProperty] private string _libraryPath = "";
    [ObservableProperty] private string _deviceUri = "fpga://devindex=0";
    [ObservableProperty] private string _argumentsJson = "[]";
    [ObservableProperty] private string _bindingDriverFileName = "FTD3XX.dll";
    [ObservableProperty] private string _processId = "0";
    [ObservableProperty] private string _processName = "";
    [ObservableProperty] private string _moduleName = "";
    [ObservableProperty] private string _address = "";
    [ObservableProperty] private string _port = "12345";
    [ObservableProperty] private string _mac = "";
    [ObservableProperty] private string _probeIntervalMs = "1000";
    [ObservableProperty] private string _retryDelayMs = "1000";
    [ObservableProperty] private bool _recordSnapshots;
    [ObservableProperty] private string _moduleSettingsJson = "{}";
    private DmaDeviceBinding? _binding;
    private DmaWorkerSettings _worker = new();
    private VerifiedCharacter? _testedCharacter;
    public DmaDeviceBinding? Binding => _binding;
    public bool IsHardware => Mode == RuntimeMode.Hardware;
    public string BindingStatus => _binding is null ? "尚未绑定物理设备" : $"已保存绑定 · {_binding.Identity.SerialNumber} · 启动时重新核验";
    partial void OnModeChanged(RuntimeMode value) => OnPropertyChanged(nameof(IsHardware));
    partial void OnLibraryPathChanged(string value) => ClearBinding();
    partial void OnDeviceUriChanged(string value) => ClearBinding();
    partial void OnBindingDriverFileNameChanged(string value) => ClearBinding();
    public void ClearBinding() { _binding = null; OnPropertyChanged(nameof(Binding)); OnPropertyChanged(nameof(BindingStatus)); }
    public void SetBinding(DmaDeviceBinding value)
    {
        value.Validate(); BindingDriverFileName = value.DriverFileName;
        DeviceUri = "fpga://ft601=1,devindex=" + value.DeviceIndex.ToString(System.Globalization.CultureInfo.InvariantCulture);
        _binding = value; OnPropertyChanged(nameof(Binding)); OnPropertyChanged(nameof(BindingStatus));
    }
    public DmaSettings ToDmaSettings() => new(LibraryPath.Trim(), DeviceUri.Trim(),
        JsonSerializer.Deserialize<string[]>(ArgumentsJson) ?? throw new ArgumentException("DMA 参数必须是 JSON 数组。"),
        Number(ProcessId, "PID") == 0 ? null : Number(ProcessId, "PID"), ProcessName.Trim(), ModuleName.Trim()) { Binding = _binding, Worker = _worker };
    private static int Number(string value, string name) => int.TryParse(value, out var result) ? result : throw new ArgumentException(name + " 必须填写整数。");
    public InputSettings ToInputSettings() => new(Address.Trim(), Number(Port, "Port"), Mac.Trim());
    public AccountProfile ToProfile()
    {
        var result = new AccountProfile(Id.Trim(), Mode, IsHardware ? ToDmaSettings() : null,
            IsHardware ? ToInputSettings() : null, Number(ProbeIntervalMs, "检查间隔"), Number(RetryDelayMs, "重试间隔"),
            JsonSerializer.Deserialize<ImmutableDictionary<string, JsonElement>>(ModuleSettingsJson), RecordSnapshots);
        return result with { TestedCharacter = _testedCharacter }; // Only verified saves replace this display evidence.
    }
    public static ProfileEditor From(AccountProfile p)
    {
        var result = new ProfileEditor { Id = p.Id, Mode = p.Mode, LibraryPath = p.Dma?.LibraryPath ?? "",
            DeviceUri = p.Dma?.DeviceUri ?? "fpga://devindex=0", ArgumentsJson = JsonSerializer.Serialize(p.Dma?.Arguments ?? []),
            BindingDriverFileName = p.Dma?.Binding?.DriverFileName ?? "FTD3XX.dll", ProcessId = (p.Dma?.ProcessId ?? 0).ToString(),
            ProcessName = p.Dma?.ProcessName ?? "", ModuleName = p.Dma?.ModuleName ?? "", Address = p.Input?.Address ?? "",
            Port = (p.Input?.Port ?? 12345).ToString(), Mac = p.Input?.Mac ?? "", ProbeIntervalMs = p.ProbeIntervalMs.ToString(),
            RetryDelayMs = p.RetryDelayMs.ToString(), RecordSnapshots = p.RecordSnapshots,
            ModuleSettingsJson = JsonSerializer.Serialize(p.ModuleSettings ?? ImmutableDictionary<string, JsonElement>.Empty) };
        result._binding = p.Dma?.Binding; result._worker = p.Dma?.Worker ?? new(); result._testedCharacter = p.TestedCharacter; return result;
    }
}
