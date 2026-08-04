using System.Diagnostics;
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
    private static readonly TimeSpan MutexTimeout = TimeSpan.FromSeconds(2);

    private readonly string _path;
    private readonly Func<DateTimeOffset> _utcNow;
    private readonly Func<int, DateTimeOffset, bool> _isProcessAlive;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public DeviceLeaseStore(
        string? path = null,
        Func<DateTimeOffset>? utcNow = null,
        Func<int, DateTimeOffset, bool>? isProcessAlive = null)
    {
        _path = string.IsNullOrWhiteSpace(path) ? DefaultPath : Path.GetFullPath(path);
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
        _isProcessAlive = isProcessAlive ?? IsProcessAlive;
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
        if (processId <= 0 || string.IsNullOrWhiteSpace(hardwareKey) || string.IsNullOrWhiteSpace(vmmDeviceName))
        {
            return DeviceLeaseAcquireResult.Failed("Device lease requires a process, hardware key, and VMM device.");
        }

        return WithLock(() =>
        {
            var leases = RemoveInactive(ReadCore());
            var currentIdentity = new ProcessIdentity(processId, processStartedAtUtc);
            leases.RemoveAll(lease => SameProcess(lease, currentIdentity));

            var hardware = hardwareKey.Trim();
            var vmm = vmmDeviceName.Trim();
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
        return leases
            .Where(lease =>
                lease.ProcessId > 0 &&
                !string.IsNullOrWhiteSpace(lease.HardwareKey) &&
                !string.IsNullOrWhiteSpace(lease.VmmDeviceName) &&
                _isProcessAlive(lease.ProcessId, lease.ProcessStartedAtUtc))
            .GroupBy(
                lease => new ProcessIdentity(lease.ProcessId, lease.ProcessStartedAtUtc),
                lease => lease)
            .Select(group => group.OrderByDescending(lease => lease.LastSeenUtc).First())
            .ToList();
    }

    private List<DeviceLease> ReadCore()
    {
        if (!File.Exists(_path))
        {
            return new List<DeviceLease>();
        }

        var json = File.ReadAllText(_path);
        return JsonSerializer.Deserialize<List<DeviceLease>>(json, _jsonOptions) ?? new List<DeviceLease>();
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
        using var mutex = new Mutex(false, MutexName);
        var acquired = false;
        try
        {
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
            if (acquired)
            {
                mutex.ReleaseMutex();
            }
        }
    }

    private static bool SameProcess(DeviceLease lease, ProcessIdentity identity)
    {
        return lease.ProcessId == identity.ProcessId &&
            Math.Abs((lease.ProcessStartedAtUtc.ToUniversalTime() - identity.ProcessStartedAtUtc.ToUniversalTime()).TotalSeconds) < 1;
    }

    private static bool IsProcessAlive(int processId, DateTimeOffset expectedStartTimeUtc)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return !process.HasExited &&
                Math.Abs((process.StartTime.ToUniversalTime() - expectedStartTimeUtc.ToUniversalTime()).TotalSeconds) < 1;
        }
        catch
        {
            return false;
        }
    }

    private readonly record struct ProcessIdentity(int ProcessId, DateTimeOffset ProcessStartedAtUtc);
}
