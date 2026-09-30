using Roadhog.Infrastructure.Hardware;
using System.Text.Json;

internal static class DeviceLeasePresenceTests
{
    public static Task UnknownOwnerPreservesLeaseAndBlocksTakeoverAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), "roadhog-lease-presence-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "device-leases.json");
        try
        {
            var started = new DateTimeOffset(2026, 9, 30, 1, 2, 3, TimeSpan.Zero);
            var probe = new FakePresenceProbe();
            probe.Set(101, ProcessIdentityPresence.Alive);
            probe.Set(202, ProcessIdentityPresence.Alive);
            var store = new DeviceLeaseStore(path, () => started.AddMinutes(1), presenceProbe: probe);
            Require(store.TryAcquire(101, started, @"C:\script\1", "P0004.H0002", "fpga://devindex=0").Success,
                "first process obtains the lease");
            var original = File.ReadAllBytes(path);

            // Models a timed-out CIM query after Process APIs could not confirm the owner.
            probe.Set(101, ProcessIdentityPresence.Unknown);
            var active = store.ReadActive();
            Require(!active.Success && active.Error?.Contains("保留设备占用记录", StringComparison.Ordinal) == true,
                "uncertain owner does not become an empty active list");
            Require(File.ReadAllBytes(path).SequenceEqual(original), "read does not erase an uncertain owner");

            var takeover = store.TryAcquire(202, started.AddSeconds(5), @"C:\script\2", "P0004.H0002", "fpga://devindex=1");
            Require(!takeover.Success && takeover.Conflict is null && takeover.Error is not null,
                "uncertain owner blocks a new process before lease replacement");
            Require(File.ReadAllBytes(path).SequenceEqual(original), "failed acquisition leaves the original lease intact");

            Require(!store.Release(101, started).Success, "uncertain owner also blocks a release based on incomplete evidence");
            Require(File.ReadAllBytes(path).SequenceEqual(original), "failed release leaves the original lease intact");

            probe.Set(101, ProcessIdentityPresence.Alive);
            var occupied = store.TryAcquire(202, started.AddSeconds(5), @"C:\script\2", "P0004.H0002", "fpga://devindex=1");
            Require(!occupied.Success && occupied.Conflict?.ProcessId == 101,
                "a confirmed live owner remains an ordinary device conflict");

            probe.Set(101, ProcessIdentityPresence.Gone);
            var recovered = store.TryAcquire(202, started.AddSeconds(5), @"C:\script\2", "P0004.H0002", "fpga://devindex=1");
            Require(recovered.Success, "a confirmed gone owner allows a new process to acquire the device");
            Require(store.ReadActive().Value?.Single().ProcessId == 202, "the replacement is the only active lease");
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
            Directory.Delete(directory);
        }
        return Task.CompletedTask;
    }

    public static Task WindowsProbeConfirmsCurrentAndAbsentIdentityAsync()
    {
        using var current = System.Diagnostics.Process.GetCurrentProcess();
        var started = new DateTimeOffset(current.StartTime.ToUniversalTime());
        var probe = new WindowsProcessIdentityPresenceProbe();
        Require(probe.Check(current.Id, started) == ProcessIdentityPresence.Alive,
            "the fast process identity check recognizes the current process");
        Require(probe.Check(current.Id, started.AddMinutes(-1)) == ProcessIdentityPresence.Gone,
            "the same PID with another creation time is not the lease owner");
        var fallback = new WindowsProcessIdentityPresenceProbe(processApiProbe: (_, _) => null);
        Require(fallback.Check(current.Id, started) == ProcessIdentityPresence.Alive,
            "CIM keeps the real owner alive when Process APIs cannot confirm it");
        Require(fallback.Check(current.Id, started.AddMinutes(-1)) == ProcessIdentityPresence.Gone,
            "CIM rejects another start time for a reused PID");
        Require(probe.Check(int.MaxValue, started) == ProcessIdentityPresence.Gone,
            "the read-only CIM fallback confirms that an absent PID is gone");
        return Task.CompletedTask;
    }

    public static Task IncompleteLeaseRecordCannotDisappearAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), "roadhog-lease-damaged-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "device-leases.json");
        try
        {
            var started = new DateTimeOffset(2026, 9, 30, 1, 2, 3, TimeSpan.Zero);
            var complete = new DeviceLease(101, started, @"C:\script\1", "P0004.H0002", "fpga://devindex=0", started);
            var records = new (string Name, string Json)[]
            {
                ("missing PID", JsonSerializer.Serialize(new[] { new { complete.ProcessStartedAtUtc, complete.ClientRoot, complete.HardwareKey, complete.VmmDeviceName, complete.LastSeenUtc } })),
                ("missing hardware key", JsonSerializer.Serialize(new[] { new { complete.ProcessId, complete.ProcessStartedAtUtc, complete.ClientRoot, complete.VmmDeviceName, complete.LastSeenUtc } })),
                ("missing VMM device", JsonSerializer.Serialize(new[] { new { complete.ProcessId, complete.ProcessStartedAtUtc, complete.ClientRoot, complete.HardwareKey, complete.LastSeenUtc } })),
                ("missing process start", JsonSerializer.Serialize(new[] { new { complete.ProcessId, complete.ClientRoot, complete.HardwareKey, complete.VmmDeviceName, complete.LastSeenUtc } })),
                ("null lease item", "[null]"),
                ("null lease document", "null")
            };
            var probe = new FakePresenceProbe();
            probe.Set(101, ProcessIdentityPresence.Gone);
            probe.Set(202, ProcessIdentityPresence.Alive);
            var store = new DeviceLeaseStore(path, () => started.AddMinutes(1), presenceProbe: probe);
            foreach (var (name, json) in records)
            {
                File.WriteAllText(path, json);
                var before = File.ReadAllBytes(path);
                var read = store.ReadActive();
                Require(!read.Success && read.Error?.Contains("设备占用记录已损坏", StringComparison.Ordinal) == true,
                    name + " must be reported as damaged on read");
                Require(!store.TryAcquire(202, started.AddSeconds(5), @"C:\script\2", "P0004.H0002", "fpga://devindex=1").Success,
                    name + " must block device acquisition");
                Require(!store.Release(101, started).Success, name + " must block rewriting on release");
                Require(File.ReadAllBytes(path).SequenceEqual(before), name + " must preserve the exact registry bytes");
            }
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
            Directory.Delete(directory);
        }
        return Task.CompletedTask;
    }

    public static Task DuplicateLiveOwnerCannotLoseOneDeviceAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), "roadhog-lease-duplicate-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "device-leases.json");
        try
        {
            var started = new DateTimeOffset(2026, 9, 30, 1, 2, 3, TimeSpan.Zero);
            var records = new[]
            {
                new DeviceLease(101, started, @"C:\script\1", "P0004.H0002", "fpga://devindex=0", started),
                new DeviceLease(101, started, @"C:\script\1", "P0004.H0003", "fpga://devindex=1", started.AddSeconds(1))
            };
            File.WriteAllText(path, JsonSerializer.Serialize(records));
            var original = File.ReadAllBytes(path);
            var probe = new FakePresenceProbe();
            probe.Set(101, ProcessIdentityPresence.Alive);
            probe.Set(202, ProcessIdentityPresence.Alive);
            var store = new DeviceLeaseStore(path, () => started.AddMinutes(1), presenceProbe: probe);
            var read = store.ReadActive();
            Require(!read.Success && read.Error?.Contains("设备占用记录已损坏", StringComparison.Ordinal) == true,
                "duplicate live owner must be treated as damaged instead of choosing the newest device");
            Require(!store.TryAcquire(202, started.AddSeconds(5), @"C:\script\2", "P0004.H0002", "fpga://devindex=2").Success,
                "duplicate live owner blocks acquisition of either device");
            Require(!store.Release(101, started).Success, "duplicate live owner blocks lossy release");
            Require(File.ReadAllBytes(path).SequenceEqual(original), "all duplicate device rows remain intact");
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
            Directory.Delete(directory);
        }
        return Task.CompletedTask;
    }

    private sealed class FakePresenceProbe : IProcessIdentityPresenceProbe
    {
        private readonly Dictionary<int, ProcessIdentityPresence> _statuses = new();

        public void Set(int processId, ProcessIdentityPresence status) => _statuses[processId] = status;

        public ProcessIdentityPresence Check(int processId, DateTimeOffset processStartedAtUtc) => _statuses[processId];
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
