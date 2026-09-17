using System.Collections.Immutable;
using System.ComponentModel;
using System.Text.Json;
using Smart.Hosting;
namespace SampleProject.Desktop;

public sealed class ProfileEditor
{
    [Category("账号")] public string Id { get; set; } = "local";
    [Category("账号")] public RuntimeMode Mode { get; set; }
    [Category("DMA")] public string LibraryPath { get; set; } = "";
    [Category("DMA")] public string DeviceUri { get; set; } = "fpga://devindex=0";
    [Category("DMA")] public string ArgumentsJson { get; set; } = "[]";
    [Category("DMA"), Description("用于设备绑定的同目录 D3XX 驱动文件名。")]
    public string BindingDriverFileName { get; set; } = "FTD3XX.dll";
    [Browsable(false)] public DmaDeviceBinding? DeviceBinding { get; private set; }
    [Browsable(false)] public DmaWorkerSettings WorkerSettings { get; private set; } = new();
    [Category("DMA"), ReadOnly(true)] public string BindingStatus => DeviceBinding is null
        ? "未绑定：devindex 对应设备仍需人工确认"
        : $"已保存绑定：index={DeviceBinding.DeviceIndex}，serial={DeviceBinding.Identity.SerialNumber}，location={DeviceBinding.Identity.LocationId:X8}；启动时重新核验";
    [Category("进程")] public int ProcessId { get; set; }
    [Category("进程")] public string ProcessName { get; set; } = "";
    [Category("进程")] public string ModuleName { get; set; } = "";
    [Category("KMBox")] public string Address { get; set; } = "";
    [Category("KMBox")] public int Port { get; set; } = 12345;
    [Category("KMBox")] public string Mac { get; set; } = "";
    [Category("运行")] public int ProbeIntervalMs { get; set; } = 1000;
    [Category("运行")] public int RetryDelayMs { get; set; } = 1000;
    [Category("诊断")] public bool RecordSnapshots { get; set; }
    [Category("模块配置")] public string ModuleSettingsJson { get; set; } = "{}";
    public static ProfileEditor From(AccountProfile p) => new()
    {
        Id = p.Id, Mode = p.Mode, LibraryPath = p.Dma?.LibraryPath ?? "", DeviceUri = p.Dma?.DeviceUri ?? "fpga://devindex=0",
        DeviceBinding = p.Dma?.Binding, BindingDriverFileName = p.Dma?.Binding?.DriverFileName ?? "FTD3XX.dll",
        WorkerSettings = p.Dma?.Worker ?? new(),
        ArgumentsJson = JsonSerializer.Serialize(p.Dma?.Arguments ?? []), ProcessId = p.Dma?.ProcessId ?? 0,
        ProcessName = p.Dma?.ProcessName ?? "", ModuleName = p.Dma?.ModuleName ?? "", Address = p.Input?.Address ?? "",
        Port = p.Input?.Port ?? 12345, Mac = p.Input?.Mac ?? "", ProbeIntervalMs = p.ProbeIntervalMs,
        RetryDelayMs = p.RetryDelayMs, RecordSnapshots = p.RecordSnapshots, ModuleSettingsJson = JsonSerializer.Serialize(p.ModuleSettings ?? ImmutableDictionary<string, JsonElement>.Empty)
    };
    public AccountProfile ToProfile() => new(Id.Trim(), Mode,
        Mode == RuntimeMode.Hardware ? new(LibraryPath, DeviceUri, JsonSerializer.Deserialize<string[]>(ArgumentsJson)!, ProcessId == 0 ? null : ProcessId, ProcessName, ModuleName) { Binding = DeviceBinding, Worker = WorkerSettings } : null,
        Mode == RuntimeMode.Hardware ? new(Address, Port, Mac) : null, ProbeIntervalMs, RetryDelayMs,
        JsonSerializer.Deserialize<ImmutableDictionary<string, JsonElement>>(ModuleSettingsJson), RecordSnapshots);
    public void SetBinding(DmaDeviceBinding binding)
    {
        binding.Validate(); DeviceBinding = binding; BindingDriverFileName = binding.DriverFileName;
        DeviceUri = "fpga://ft601=1,devindex=" + binding.DeviceIndex.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }
    public void ClearBinding() => DeviceBinding = null;
}
