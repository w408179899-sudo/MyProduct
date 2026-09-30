using System.Text.Json;
using Roadhog.Core.Common;

namespace Roadhog.Infrastructure.Hardware;

public sealed record DeviceLease(
    int ProcessId,
    DateTimeOffset ProcessStartedAtUtc,
    string ClientRoot,
    string HardwareKey,
    string VmmDeviceName,
    DateTimeOffset LastSeenUtc);

public sealed record DeviceLeaseAcquireResult(
    bool Success,
    DeviceLease? Lease,
    DeviceLease? Conflict,
    string? Error)
{
    public static DeviceLeaseAcquireResult Acquired(DeviceLease lease)
    {
        return new DeviceLeaseAcquireResult(true, lease, null, null);
    }

    public static DeviceLeaseAcquireResult Occupied(DeviceLease conflict)
    {
        return new DeviceLeaseAcquireResult(false, null, conflict, null);
    }

    public static DeviceLeaseAcquireResult Failed(string error)
    {
        return new DeviceLeaseAcquireResult(false, null, null, error);
    }
}

public sealed class DeviceLeaseStore
{
    private const string MutexName = @"Local\Roadhog.DeviceLeaseStore";
    private const string CorruptedRegistryRecovery =
        "设备占用记录已损坏。请先关闭所有 Roadhog.exe，然后按 Win + R，输入 %LOCALAPPDATA%\\Roadhog，" +
        "删除 device-leases.json 后重新打开程序并保存硬件配置。";
    // UI callers are synchronous. Contention must fail promptly without assuming the device is free.
    private static readonly TimeSpan MutexTimeout = TimeSpan.FromSeconds(2);

    private readonly string _path;
    private readonly Func<DateTimeOffset> _utcNow;
    private readonly Func<int, DateTimeOffset, ProcessIdentityPresence> _checkProcessPresence;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public DeviceLeaseStore(
        string? path = null,
        Func<DateTimeOffset>? utcNow = null,
        Func<int, DateTimeOffset, bool>? isProcessAlive = null,
        IProcessIdentityPresenceProbe? presenceProbe = null)
    {
        if (isProcessAlive is not null && presenceProbe is not null)
            throw new ArgumentException("Specify only one process presence override.");
        _path = string.IsNullOrWhiteSpace(path) ? DefaultPath : Path.GetFullPath(path);
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
        _checkProcessPresence = isProcessAlive is not null
            ? (processId, startedAtUtc) => isProcessAlive(processId, startedAtUtc)
                ? ProcessIdentityPresence.Alive : ProcessIdentityPresence.Gone
            : (presenceProbe ?? new WindowsProcessIdentityPresenceProbe()).Check;
    }

    public static string DefaultPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Roadhog",
        "device-leases.json");

    public OperationResult<IReadOnlyList<DeviceLease>> ReadActive()
    {
        return WithLock(() =>
        {
            var leases = ReadCore();
            var active = RemoveInactive(leases);
            if (active.Count != leases.Count)
            {
                WriteCore(active);
            }

            return OperationResult<IReadOnlyList<DeviceLease>>.Ok(active);
        }, OperationResult<IReadOnlyList<DeviceLease>>.Fail);
    }

    public DeviceLeaseAcquireResult TryAcquire(
        int processId,
        DateTimeOffset processStartedAtUtc,
        string clientRoot,
        string hardwareKey,
        string vmmDeviceName)
    {
        if (processId <= 0 || processStartedAtUtc == default || string.IsNullOrWhiteSpace(clientRoot) ||
            string.IsNullOrWhiteSpace(hardwareKey) || string.IsNullOrWhiteSpace(vmmDeviceName))
        {
            return DeviceLeaseAcquireResult.Failed("Device lease requires a process identity, client root, hardware key, and VMM device.");
        }

        return WithLock(() =>
        {
            var leases = RemoveInactive(ReadCore());
            var currentIdentity = new ProcessIdentity(processId, processStartedAtUtc);
            var hardware = hardwareKey.Trim();
            var vmm = vmmDeviceName.Trim();
            var previous = leases.FirstOrDefault(lease => SameProcess(lease, currentIdentity));
            if (previous is not null &&
                (!string.Equals(previous.HardwareKey, hardware, StringComparison.OrdinalIgnoreCase) ||
                 !string.Equals(CanonicalVmmDeviceName(previous.VmmDeviceName), CanonicalVmmDeviceName(vmm), StringComparison.OrdinalIgnoreCase)))
                return DeviceLeaseAcquireResult.Failed("当前进程仍占用原 DMA 设备，请先关闭持有设备的进程，再切换绑定。");
            leases.RemoveAll(lease => SameProcess(lease, currentIdentity));

            var conflict = leases.FirstOrDefault(lease =>
                string.Equals(lease.HardwareKey, hardware, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(CanonicalVmmDeviceName(lease.VmmDeviceName), CanonicalVmmDeviceName(vmm), StringComparison.OrdinalIgnoreCase));
            if (conflict is not null)
            {
                return DeviceLeaseAcquireResult.Occupied(conflict);
            }

            var acquired = new DeviceLease(
                processId,
                processStartedAtUtc.ToUniversalTime(),
                clientRoot.Trim(),
                hardware,
                vmm,
                _utcNow());
            leases.Add(acquired);
            WriteCore(leases);
            return DeviceLeaseAcquireResult.Acquired(acquired);
        }, DeviceLeaseAcquireResult.Failed);
    }

    public OperationResult Release(int processId, DateTimeOffset processStartedAtUtc)
    {
        return WithLock(() =>
        {
            var leases = RemoveInactive(ReadCore());
            var identity = new ProcessIdentity(processId, processStartedAtUtc);
            leases.RemoveAll(lease => SameProcess(lease, identity));
            WriteCore(leases);
            return OperationResult.Ok();
        }, OperationResult.Fail);
    }

    public static string CanonicalVmmDeviceName(string? vmmDeviceName)
    {
        var value = string.IsNullOrWhiteSpace(vmmDeviceName) ? "fpga" : vmmDeviceName.Trim();
        return string.Equals(value, "fpga", StringComparison.OrdinalIgnoreCase)
            ? "fpga://devindex=0"
            : value;
    }

    private List<DeviceLease> RemoveInactive(IEnumerable<DeviceLease> leases)
    {
        var active = leases.Where(LeaseIsActive).ToList();
        if (active.GroupBy(lease => new ProcessIdentity(lease.ProcessId, lease.ProcessStartedAtUtc))
            .Any(group => group.Count() > 1))
            throw new JsonException("同一进程存在多条设备占用记录，无法确认哪个设备仍在使用。");
        return active;
    }

    private bool LeaseIsActive(DeviceLease? lease)
    {
        if (lease is null || lease.ProcessId <= 0 || lease.ProcessStartedAtUtc == default ||
            string.IsNullOrWhiteSpace(lease.HardwareKey) || string.IsNullOrWhiteSpace(lease.VmmDeviceName))
            throw new JsonException("设备占用记录缺少进程身份或设备标识，无法确认设备已释放。");
        var presence = _checkProcessPresence(lease.ProcessId, lease.ProcessStartedAtUtc);
        return presence switch
        {
            ProcessIdentityPresence.Alive => true,
            ProcessIdentityPresence.Gone => false,
            _ => throw new InvalidOperationException($"无法确认进程 {lease.ProcessId} 是否已完全退出，已保留设备占用记录；请稍后重试。")
        };
    }

    private List<DeviceLease> ReadCore()
    {
        if (!File.Exists(_path))
        {
            return new List<DeviceLease>();
        }

        var json = File.ReadAllText(_path);
        return JsonSerializer.Deserialize<List<DeviceLease>>(json, _jsonOptions)
            ?? throw new JsonException("设备占用记录内容为 null，无法确认设备已释放。");
    }

    private void WriteCore(IReadOnlyList<DeviceLease> leases)
    {
        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(tempPath, JsonSerializer.Serialize(leases, _jsonOptions));
            File.Move(tempPath, _path, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    private T WithLock<T>(Func<T> action, Func<string, T> failureFactory)
    {
        Mutex? mutex = null;
        var acquired = false;
        try
        {
            mutex = new Mutex(false, MutexName);
            try
            {
                acquired = mutex.WaitOne(MutexTimeout);
            }
            catch (AbandonedMutexException)
            {
                acquired = true;
            }

            if (!acquired)
            {
                return failureFactory("Timed out waiting for the device lease registry.");
            }

            return action();
        }
        catch (JsonException ex)
        {
            return failureFactory(CorruptedRegistryRecovery + " 原始错误：" + ex.Message);
        }
        catch (Exception ex)
        {
            return failureFactory(ex.Message);
        }
        finally
        {
            if (acquired) mutex!.ReleaseMutex();
            mutex?.Dispose();
        }
    }

    private static bool SameProcess(DeviceLease lease, ProcessIdentity identity)
    {
        return lease.ProcessId == identity.ProcessId &&
            Math.Abs((lease.ProcessStartedAtUtc.ToUniversalTime() - identity.ProcessStartedAtUtc.ToUniversalTime()).TotalSeconds) < 1;
    }

    private readonly record struct ProcessIdentity(int ProcessId, DateTimeOffset ProcessStartedAtUtc);
}
