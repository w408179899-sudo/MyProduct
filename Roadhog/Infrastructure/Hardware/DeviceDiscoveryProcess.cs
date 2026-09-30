using System.Diagnostics;
using System.Text.Json;
using Hardware.KmBox;
using Roadhog.Core.Accounts;
using Roadhog.Infrastructure.Diagnostics;
using Roadhog.Infrastructure.Vmm;
using Roadhog.Infrastructure.WorkerProcesses;

namespace Roadhog.Infrastructure.Hardware;

internal sealed record DeviceDiscoveryRequest(string Token, string VmmDeviceName, string ProcessName,
    AccountKmBoxSettings? KmBox, string LeasePath);
internal sealed record DeviceDiscoveryResponse(string Token, string VmmDeviceName, string? Role, string? Error);

/// <summary>Every DMA read has a fresh native session. No input backend is constructed.</summary>
internal sealed class DeviceDiscoveryProcess(string directory, WorkerProcessLaunchOptions launch,
    IWorkerProcessExitReconciler? reconciler = null) : IDeviceDiscoveryProbe
{
    public async Task<string> ReadRoleAsync(string vmmDeviceName, string processName, CancellationToken token) =>
        (await RunAsync(vmmDeviceName, processName, null, token).ConfigureAwait(false)).Role
        ?? throw new InvalidOperationException("未读到角色");

    public async Task VerifyKmBoxAsync(AccountKmBoxSettings settings, CancellationToken token) =>
        _ = await RunAsync("", "", settings, token).ConfigureAwait(false);

    private async Task<DeviceDiscoveryResponse> RunAsync(string vmm, string processName,
        AccountKmBoxSettings? kmBox, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        Directory.CreateDirectory(directory);
        var request = new DeviceDiscoveryRequest(Guid.NewGuid().ToString("N"), vmm, processName,
            kmBox, launch.LeasePath ?? DeviceLeaseStore.DefaultPath);
        var path = Path.Combine(directory, request.Token + ".json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(request), token).ConfigureAwait(false);
        var start = new ProcessStartInfo(launch.ExecutablePath) { UseShellExecute = false, CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden, WorkingDirectory = AppContext.BaseDirectory };
        foreach (var arg in launch.PrefixArguments) start.ArgumentList.Add(arg);
        start.ArgumentList.Add("--device-discovery"); start.ArgumentList.Add(path);
        // Explicit local indices must not silently inherit a remote or global-device override.
        start.Environment.Remove("VMM_DEVICE"); start.Environment.Remove("VMM_REMOTE");
        using var child = new Process { StartInfo = start };
        WorkerDescriptor? identity = null;
        var started = false;
        try
        {
            token.ThrowIfCancellationRequested();
            if (!child.Start()) throw new InvalidOperationException("无法启动设备探测进程");
            started = true;
            identity = new WorkerDescriptor { ProcessId = child.Id,
                ProcessStartedAtUtc = new DateTimeOffset(child.StartTime.ToUniversalTime()) };
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
            deadline.CancelAfter(launch.StartupTimeout);
            try { await child.WaitForExitAsync(deadline.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) when (!token.IsCancellationRequested)
            { throw new TimeoutException("探测超时，请确认角色已进入游戏"); }
            token.ThrowIfCancellationRequested();
            var result = JsonSerializer.Deserialize<DeviceDiscoveryResponse>(await File.ReadAllTextAsync(path + ".result", token).ConfigureAwait(false));
            if (result is null || result.Token != request.Token || result.VmmDeviceName != vmm)
                throw new InvalidDataException("探测结果身份不一致");
            if (child.ExitCode != 0 || result.Error is not null) throw new InvalidOperationException(result.Error ?? "探测进程失败");
            return result;
        }
        finally
        {
            try
            {
                if (started)
                {
                    if (!child.HasExited) child.Kill(entireProcessTree: false);
                    using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(12));
                    await child.WaitForExitAsync(cleanup.Token).ConfigureAwait(false);
                    if (identity is null) throw new InvalidOperationException("缺少探测进程身份");
                    await (reconciler ?? new WindowsWorkerProcessExitReconciler()).ReconcileAsync(identity,
                        launch.ExecutablePath, cleanup.Token).ConfigureAwait(false);
                }
            }
            catch (Exception ex) { throw new DeviceDiscoveryCleanupException("探测进程未确认退出，已停止扫描，请稍后重试", ex); }
            finally
            {
                try { File.Delete(path); File.Delete(path + ".result"); } catch (IOException) { }
            }
        }
    }

    // This entry point is only used by the disposable child process, never by the manager.
    internal static async Task<int> RunChildAsync(string path)
    {
        var request = JsonSerializer.Deserialize<DeviceDiscoveryRequest>(await File.ReadAllTextAsync(path))
            ?? throw new InvalidDataException("探测请求为空");
        DeviceDiscoveryResponse response;
        try
        {
            string? role = null;
            if (request.KmBox is { } settings)
            {
                if (!settings.Validate(out var error)) throw new InvalidOperationException(error);
                var address = System.Net.IPAddress.Parse(settings.IpAddress.Trim());
                if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
                using var endpoint = new WorkerProcessHost.WorkerMutex("Roadhog.KmBox.Endpoint",
                    address + ":" + settings.Port.ToString(System.Globalization.CultureInfo.InvariantCulture), true);
                using var mac = new WorkerProcessHost.WorkerMutex("Roadhog.KmBox.Mac",
                    new string(settings.Mac.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant(), true);
                if (!endpoint.Acquired || !mac.Acquired) throw new InvalidOperationException("KMBox 正在被其他后台使用");
                // Dispose/Disconnect send ReleaseAll. This child only handshakes; OS exit closes its socket.
                var device = new KmBoxNetDevice(new KmBoxOptions { IpAddress = settings.IpAddress,
                    Port = settings.Port, Mac = settings.Mac });
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                if (!await device.ConnectAsync(timeout.Token).ConfigureAwait(false)) throw new InvalidOperationException("KMBox 握手失败");
            }
            else
            {
                if (!request.VmmDeviceName.StartsWith("fpga://devindex=", StringComparison.Ordinal)
                    || !int.TryParse(request.VmmDeviceName.AsSpan(16), out var index) || index < 0)
                    throw new InvalidDataException("读取编号无效");
                using var process = Process.GetCurrentProcess();
                var lease = new DeviceLeaseStore(request.LeasePath).TryAcquire(process.Id,
                    new DateTimeOffset(process.StartTime.ToUniversalTime()), Path.GetDirectoryName(path)!,
                    "discovery:" + request.VmmDeviceName, request.VmmDeviceName);
                if (!lease.Success) throw new InvalidOperationException(lease.Conflict is not null ? "设备已被占用" : lease.Error);
                var logger = new FileRoadhogLogger(Path.Combine(Path.GetDirectoryName(path)!, "logs"));
                var provider = new AionVmmGameApi(new AionVmmGameApiOptions { DefaultProcessName = request.ProcessName,
                    DefaultVmmDeviceName = request.VmmDeviceName }, logger);
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25));
                var reader = new RoadhogSnapshotReaderFactory(provider).Create(new AccountConfig {
                    AccountName = "device-discovery", ProcessId = 0, TargetProcessName = request.ProcessName,
                    VmmDeviceName = request.VmmDeviceName }, logger, timeout.Token);
                var player = (await reader.ReadPlayerAsync().ConfigureAwait(false)).Value;
                if (player.EntityId == 0 || string.IsNullOrWhiteSpace(player.CharacterName)) throw new InvalidOperationException("未读到有效角色");
                role = player.CharacterName;
                // Keep the lease until the OS process is gone, including native cleanup.
            }
            response = new(request.Token, request.VmmDeviceName, role, null);
        }
        catch (Exception ex) { response = new(request.Token, request.VmmDeviceName, null, ex.Message); }
        await File.WriteAllTextAsync(path + ".result", JsonSerializer.Serialize(response)).ConfigureAwait(false);
        return response.Error is null ? 0 : 1;
    }
}
