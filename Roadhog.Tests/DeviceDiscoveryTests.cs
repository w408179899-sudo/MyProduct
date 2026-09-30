using System.Diagnostics;
using System.Text.Json;
using Roadhog.Core.Accounts;
using Roadhog.Core.Hardware;
using Roadhog.Infrastructure.Composition;
using Roadhog.Infrastructure.Hardware;
using Roadhog.Infrastructure.WorkerProcesses;

internal static class DeviceDiscoveryTests
{
    private static void Require(bool value, string message) { if (!value) throw new Exception(message); }
    private static AccountConfig Account(int n) => new() { InstanceId = Guid.NewGuid().ToString(), AccountName = "账号" + n,
        CharacterName = "角色" + n, HardwareKey = "usb:" + n, HardwareDeviceInstanceId = "instance" + n,
        HardwareVerificationSessionId = "previous-boot", VmmDeviceName = "fpga://devindex=" + n,
        KmBox = new() { IpAddress = "127.0.0." + (n + 1), Port = 5000, Mac = "12345678" } };
    private static HardwareDeviceFeature Device(AccountConfig a) => new(a.HardwareKey, "usb", "high",
        a.HardwareDeviceInstanceId, "", "", "", "", "DMA", "", "fpga://devindex=99", []);
    private sealed class Probe : IDeviceDiscoveryProbe
    {
        public List<string> Reads = [];
        public int Handshakes;
        public Func<string, CancellationToken, Task<string>> Read = (v, _) => Task.FromResult("角色" + (1 - int.Parse(v[16..])));
        public Func<CancellationToken, Task> Handshake = _ => Task.CompletedTask;
        public Task<string> ReadRoleAsync(string v, string process, CancellationToken token) { Reads.Add(v); return Read(v, token); }
        public Task VerifyKmBoxAsync(AccountKmBoxSettings s, CancellationToken token) { Handshakes++; return Handshake(token); }
    }
    private static Task<DeviceDiscoveryResult> Scan(Probe p, AccountConfig[] a, CancellationToken token = default,
        IReadOnlySet<string>? active = null, IReadOnlyDictionary<string, string?>? occupied = null) =>
        new DeviceDiscoveryService(p).DiscoverAsync(a, a.Select(Device).ToArray(), active ?? new HashSet<string>(),
            occupied ?? new Dictionary<string, string?>(), token);

    public static async Task SwapAsync()
    {
        var a = new[] { Account(0), Account(1) }; var p = new Probe();
        var result = await Scan(p, a);
        Require(p.Reads.Count == 2 && p.Handshakes == 2, "two devices scanned once, not per account");
        Require(result.Accounts[0].VmmDeviceName.EndsWith("=1") && result.Accounts[1].VmmDeviceName.EndsWith("=0"), "roles recover swapped indices");
        Require(result.Accounts.All(HardwareVerificationSession.IsCurrent), "matched accounts receive current boot proof");
        Require(a[0].VmmDeviceName.EndsWith("=0") && a.All(x => x.HardwareVerificationSessionId == "previous-boot"), "proposal does not mutate input");
        Require(result.Accounts[0].HardwareKey == a[0].HardwareKey && result.Accounts[0].KmBox!.IpAddress == a[0].KmBox!.IpAddress,
            "preserves physical identity and KMBox pairing");
    }

    public static async Task AmbiguityAsync()
    {
        var a = new[] { Account(0), Account(1) }; var p = new Probe { Read = (_, _) => Task.FromResult("角色0") };
        var result = await Scan(p, a);
        Require(result.Messages[a[0].InstanceId].Contains("同名") && p.Handshakes == 0, "duplicate observed names cannot select first candidate");
        Require(result.Messages[a[1].InstanceId].Contains("未找到"), "missing role reported separately");
        a[1].CharacterName = a[0].CharacterName; p.Reads.Clear();
        result = await Scan(p, a);
        Require(p.Reads.Count == 0 && result.Messages.Values.All(x => x.Contains("重复")), "duplicate saved names fail before hardware access");
    }

    public static async Task PartialAsync()
    {
        var a = new[] { Account(0), Account(1) };
        var p = new Probe { Read = (v, _) => v.EndsWith("=0") ? Task.FromException<string>(new TimeoutException("fixture timeout")) : Task.FromResult("角色0") };
        var result = await Scan(p, a);
        Require(p.Handshakes == 0 && result.Accounts.All(x => !HardwareVerificationSession.IsCurrent(x)), "incomplete scan cannot prove uniqueness");
        Require(result.Messages.Values.All(x => x.Contains("fixture timeout")), "failed index reason retained");
    }

    public static async Task OccupiedAsync()
    {
        var a = new[] { Account(0), Account(1) }; var p = new Probe();
        var result = await Scan(p, a, active: new HashSet<string> { a[1].InstanceId },
            occupied: new Dictionary<string, string?> { ["fpga://devindex=0"] = "角色1" });
        Require(p.Reads.SequenceEqual(new[] { "fpga://devindex=1" }) && p.Handshakes == 1, "occupied device never reopened, idle account still recovered");
        Require(result.Accounts[0].VmmDeviceName.EndsWith("=1") && !HardwareVerificationSession.IsCurrent(result.Accounts[1]), "active config unchanged");
        p = new Probe();
        result = await Scan(p, a, occupied: new Dictionary<string, string?> { ["fpga://devindex=0"] = null });
        Require(p.Reads.Count == 1 && p.Handshakes == 0, "unknown occupied role blocks speculative saving");
    }

    public static async Task EligibilityAsync()
    {
        var a = new[] { Account(0), Account(1) }; var p = new Probe();
        a[0].HardwareVerificationSessionId = ""; a[1].HardwareDeviceInstanceId = "";
        var result = await Scan(p, a);
        Require(p.Reads.Count == 0 && result.Messages.Count == 2, "unverified or missing physical identities require manual setup");
        a = new[] { Account(0), Account(1) }; a[1].TargetProcessName = "different.exe";
        result = await Scan(p, a);
        Require(p.Reads.Count == 0 && result.Messages.Values.All(x => x.Contains("目标进程")), "do not guess mixed process targets");
    }

    public static async Task CancellationAsync()
    {
        var a = new[] { Account(0), Account(1) }; using var cts = new CancellationTokenSource();
        var p = new Probe { Handshake = _ => { cts.Cancel(); return Task.CompletedTask; } };
        try { await Scan(p, a, cts.Token); throw new Exception("cancellation ignored"); }
        catch (OperationCanceledException) { }
        Require(a.All(x => !HardwareVerificationSession.IsCurrent(x)) && p.Handshakes == 1, "cancelled batch exposes no partial proposal");
    }

    public static async Task HandshakeFailureAsync()
    {
        var a = new[] { Account(0), Account(1) }; var calls = 0;
        var p = new Probe { Handshake = _ => ++calls == 1 ? Task.FromException(new Exception("fixture offline")) : Task.CompletedTask };
        var result = await Scan(p, a);
        Require(!HardwareVerificationSession.IsCurrent(result.Accounts[0]) && HardwareVerificationSession.IsCurrent(result.Accounts[1]), "failed KMBox affects only its matched account");
        Require(result.Messages[a[0].InstanceId].Contains("fixture offline"), "handshake diagnostic preserved");
    }

    public static async Task CleanupFailureAsync()
    {
        var p = new Probe { Read = (_, _) => Task.FromException<string>(new DeviceDiscoveryCleanupException("fixture cleanup", new Exception())) };
        try { await Scan(p, new[] { Account(0), Account(1) }); throw new Exception("cleanup failure ignored"); }
        catch (DeviceDiscoveryCleanupException) { }
        Require(p.Reads.Count == 1 && p.Handshakes == 0, "failed cleanup aborts before another native session");
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public string Root = Path.Combine(Path.GetTempPath(), "RoadhogDiscoveryTests", Guid.NewGuid().ToString("N"));
        public MultiAccountWorkspace Workspace;
        public AccountConfig[] Accounts = [Account(0), Account(1)];
        public Probe Probe = new();
        public Fixture()
        {
            Workspace = new(new RoadhogServiceOptions { AccountConfigPath = Path.Combine(Root, "config", "accounts.json"),
                PathLibraryDirectory = Path.Combine(Root, "paths"), ProfileLibraryDirectory = Path.Combine(Root, "profiles"),
                RadarMapDirectory = Path.Combine(Root, "radar"), LogDirectory = Path.Combine(Root, "logs"),
                KmBoxNetConfigPath = Path.Combine(Root, "unused-kmbox.json"), LicenseCredentialPath = Path.Combine(Root, "unused-license.dat"),
                OwnerLicenseGrantPath = Path.Combine(Root, "unused-owner.json") },
                new WorkerProcessLaunchOptions { ExecutablePath = Path.Combine(AppContext.BaseDirectory, "Roadhog.Tests.exe"),
                    LeasePath = Path.Combine(Root, "leases.json"), PollInterval = TimeSpan.FromMilliseconds(80) });
            Workspace.DiscoveryProbe = Probe;
            Workspace.DiscoveryDevices = () => Accounts.Select(Device).ToArray();
        }
        public async Task Initialize() { await Workspace.SaveAccountsAsync(Accounts); await Workspace.InitializeAsync(CancellationToken.None); }
        public async ValueTask DisposeAsync() { await Workspace.Processes.StopAllAsync(); await Workspace.DisposeAsync(); }
    }

    public static async Task PersistAsync()
    {
        await using var f = new Fixture(); await f.Initialize();
        var result = await f.Workspace.DiscoverDevicesAsync(CancellationToken.None);
        var saved = (await f.Workspace.Accounts.LoadAllAsync()).Value!;
        Require(saved[0].VmmDeviceName.EndsWith("=1") && saved.All(HardwareVerificationSession.IsCurrent), "batch persisted on disk");
        Require(f.Workspace.Processes.Snapshot().All(v => !v.DesiredRunning && v.WorkerProcessId is null && v.State == "stopped"), "discovery never starts workers or restores old intent");
        Require(JsonSerializer.Serialize(saved) == JsonSerializer.Serialize(result.Accounts), "disk, proposal and manager agree");
    }

    public static async Task WorkspaceCancellationAsync()
    {
        await using var f = new Fixture(); await f.Initialize();
        var before = await File.ReadAllTextAsync(f.Workspace.Options.AccountConfigPath);
        using var cts = new CancellationTokenSource();
        f.Probe.Handshake = _ => { cts.Cancel(); return Task.CompletedTask; };
        try { await f.Workspace.DiscoverDevicesAsync(cts.Token); throw new Exception("cancel ignored"); }
        catch (OperationCanceledException) { }
        Require(await File.ReadAllTextAsync(f.Workspace.Options.AccountConfigPath) == before, "cancel leaves exact previous document");
        Require(f.Workspace.Processes.Snapshot().All(v => !HardwareVerificationSession.IsCurrent(v.Config)), "manager still has old binding");
    }

    public static async Task TopologyChangedAsync()
    {
        await using var f = new Fixture(); await f.Initialize(); var inventories = 0;
        f.Workspace.DiscoveryDevices = () => ++inventories == 1 ? f.Accounts.Select(Device).ToArray() : [];
        try { await f.Workspace.DiscoverDevicesAsync(CancellationToken.None); throw new Exception("topology change ignored"); }
        catch (InvalidOperationException ex) { Require(ex.Message.Contains("设备发生变化"), "topology diagnostic"); }
        Require((await f.Workspace.Accounts.LoadAllAsync()).Value!.All(a => !HardwareVerificationSession.IsCurrent(a)), "unplug before commit invalidates entire batch");
    }

    public static async Task UnsupportedModeAsync()
    {
        await using var f = new Fixture(); await f.Initialize(); f.Workspace.Options.UseMockGameApi = true;
        try { await f.Workspace.DiscoverDevicesAsync(default); throw new Exception("mock mode opened real hardware"); }
        catch (InvalidOperationException ex) { Require(ex.Message.Contains("本地 DMA"), "unsupported mode diagnostic"); }
        Require(f.Probe.Reads.Count == 0, "unsupported mode fails before hardware access");
    }

    public static async Task AdmissionAsync()
    {
        await using var f = new Fixture(); await f.Initialize();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var original = f.Probe.Read;
        f.Probe.Read = async (v, token) => { entered.TrySetResult(); await release.Task.WaitAsync(token); return await original(v, token); };
        var scan = f.Workspace.DiscoverDevicesAsync(CancellationToken.None);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        var admission = f.Workspace.Processes.HoldStartAdmissionsAsync();
        var save = f.Workspace.SaveAccountsAsync(f.Accounts);
        await Task.Delay(100);
        Require(!admission.IsCompleted && !save.IsCompleted, "scan excludes concurrent starts and config saves");
        release.SetResult(); await scan;
        (await admission.WaitAsync(TimeSpan.FromSeconds(3))).Dispose();
        await save.WaitAsync(TimeSpan.FromSeconds(3));
    }

    public static async Task ProcessBoundaryAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), "RoadhogDiscoveryProcessTests", Guid.NewGuid().ToString("N"));
        DeviceDiscoveryProcess Create(string scenario, TimeSpan? timeout = null, IWorkerProcessExitReconciler? reconciler = null) =>
            new(directory, new WorkerProcessLaunchOptions { ExecutablePath = Path.Combine(AppContext.BaseDirectory, "Roadhog.Tests.exe"),
                PrefixArguments = ["--discovery-fixture=" + scenario], StartupTimeout = timeout ?? TimeSpan.FromSeconds(5),
                LeasePath = Path.Combine(directory, "leases.json") }, reconciler);
        Require(await Create("ok").ReadRoleAsync("fpga://devindex=0", "Aion.bin", default) == "fixture-role", "real child result returned after process exit");
        try { await Create("stale").ReadRoleAsync("fpga://devindex=0", "Aion.bin", default); throw new Exception("stale result accepted"); }
        catch (InvalidDataException) { }
        try { await Create("hang", TimeSpan.FromMilliseconds(500)).ReadRoleAsync("fpga://devindex=0", "Aion.bin", default); throw new Exception("timeout ignored"); }
        catch (TimeoutException) { }
        using var cancel = new CancellationTokenSource(500);
        try { await Create("hang").ReadRoleAsync("fpga://devindex=0", "Aion.bin", cancel.Token); throw new Exception("cancel ignored"); }
        catch (OperationCanceledException) { }
        Require(!Directory.EnumerateFiles(directory, "*.json").Any(p => Path.GetFileName(p) != "leases.json")
            && !Directory.EnumerateFiles(directory, "*.result").Any(), "temporary requests removed after child exit");
        Require(new DeviceLeaseStore(Path.Combine(directory, "leases.json")).ReadActive().Value!.Count == 0, "killed probe lease is pruned only after process disappearance");
    }

    public static async Task ProductionHandshakeAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), "RoadhogDiscoveryHandshakeTests", Guid.NewGuid().ToString("N"));
        var leasePath = Path.Combine(directory, "leases.json");
        var probe = new DeviceDiscoveryProcess(directory, new WorkerProcessLaunchOptions {
            ExecutablePath = Path.Combine(AppContext.BaseDirectory, "Roadhog.Tests.exe"), PrefixArguments = ["--discovery-fixture=production"],
            LeasePath = leasePath, StartupTimeout = TimeSpan.FromSeconds(5) });
        using var server = new System.Net.Sockets.UdpClient(new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 0));
        var port = ((System.Net.IPEndPoint)server.Client.LocalEndPoint!).Port;
        var settings = new AccountKmBoxSettings { IpAddress = "127.0.0.1", Port = port, Mac = "12345678" };
        var verify = probe.VerifyKmBoxAsync(settings, default);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var packet = await server.ReceiveAsync(deadline.Token);
        Require(packet.Buffer.Length == 16 && BitConverter.ToUInt32(packet.Buffer, 12) == 0xaf3c2828, "production child sends only Connect");
        await server.SendAsync(packet.Buffer, packet.RemoteEndPoint);
        await verify;
        Require(server.Available == 0, "child exit sends no keyboard/mouse ReleaseAll packets");
        using (var ownership = new WorkerProcessHost.WorkerMutex("Roadhog.KmBox.Endpoint", "127.0.0.1:" + port, true))
        {
            Require(ownership.Acquired, "fixture owns endpoint");
            try { await probe.VerifyKmBoxAsync(settings, default); throw new Exception("occupied KMBox accepted"); }
            catch (InvalidOperationException ex) { Require(ex.Message.Contains("正在被"), "production mutex rejects handshake"); }
            Require(server.Available == 0, "occupied KMBox receives no packets");
        }
        using var current = Process.GetCurrentProcess();
        var store = new DeviceLeaseStore(leasePath);
        Require(store.TryAcquire(current.Id, new DateTimeOffset(current.StartTime.ToUniversalTime()), directory,
            "fixture-owned", "fpga://devindex=0").Success, "fixture owns DMA lease");
        try { await probe.ReadRoleAsync("fpga://devindex=0", "Aion.bin", default); throw new Exception("occupied DMA accepted"); }
        catch (InvalidOperationException ex) { Require(ex.Message.Contains("已被占用"), "production child refuses before constructing DMA provider"); }
        Require(store.ReadActive().Value!.Single().ProcessId == current.Id, "probe cannot release someone else's lease");
    }

    public static Task UiTimingAsync() => Sta(() =>
    {
        var f = new Fixture();
        try
        {
            Pump(f.Initialize());
            using (var form = Show(f.Workspace))
            {
                Until(() => Field<Task?>(form, "_initializationTask")?.IsCompleted == true);
                Field<Task>(form, "_initializationTask").GetAwaiter().GetResult();
                Require(f.Probe.Reads.Count == 2 && f.Workspace.Processes.Snapshot().All(v => !v.DesiredRunning), "opening manager scans old boot exactly once without starting");
                form.Hide(); form.Show(); System.Windows.Forms.Application.DoEvents();
                Require(f.Probe.Reads.Count == 2, "restoring window does not rescan");
            }
            using (var reopened = Show(f.Workspace))
            {
                Until(() => Field<Task?>(reopened, "_initializationTask")?.IsCompleted == true);
                Field<Task>(reopened, "_initializationTask").GetAwaiter().GetResult();
                Require(f.Probe.Reads.Count == 2, "same boot persisted mapping skips automatic scan on reopen");
            }

            Pump(f.Workspace.SaveAccountsAsync(f.Accounts));
            var originalRead = f.Probe.Read;
            var attempts = 0;
            f.Probe.Read = (v, token) => ++attempts == 1
                ? Task.FromException<string>(new IOException("device not ready")) : originalRead(v, token);
            using (var retry = Show(f.Workspace))
            {
                Until(() => Field<Task?>(retry, "_initializationTask")?.IsCompleted == true);
                Require(attempts == 4 && f.Workspace.Processes.Snapshot().All(v => HardwareVerificationSession.IsCurrent(v.Config)),
                    "reboot discovery retries a transient read and confirms every account without manual binding");
                Require(Field<System.Windows.Forms.Label>(retry, "_message").Text.Contains("Mapping 获取成功：2 个账号"),
                    "automatic mapping success is visible before batch start");
                var grid = Field<System.Windows.Forms.DataGridView>(retry, "_grid");
                Require(grid.Rows.Cast<System.Windows.Forms.DataGridViewRow>().All(row =>
                    row.Cells[4].Value?.ToString()?.Contains("映射成功") == true),
                    "each mapped account shows readiness in its row");
            }

            f.Accounts[1].CharacterName = f.Accounts[0].CharacterName;
            Pump(f.Workspace.SaveAccountsAsync(f.Accounts));
            using (var failed = Show(f.Workspace))
            {
                Until(() => Field<Task?>(failed, "_initializationTask")?.IsCompleted == true);
                Require(Field<System.Windows.Forms.Label>(failed, "_message").Text.Contains("失败 2 个"),
                    "permanent mapping failure is summarized without futile retries");
                var grid = Field<System.Windows.Forms.DataGridView>(failed, "_grid");
                Require(grid.Rows.Cast<System.Windows.Forms.DataGridViewRow>().All(row =>
                    row.Cells[4].Value?.ToString()?.Contains("映射失败") == true &&
                    row.Cells[4].ToolTipText.Contains("重复")),
                    "failed rows retain their actionable cause");
            }
        }
        finally { Pump(f.DisposeAsync().AsTask()); }
    });

    public static Task UiCancelRetryAsync() => Sta(() =>
    {
        var f = new Fixture();
        try
        {
            Pump(f.Initialize());
            var original = f.Probe.Read;
            f.Probe.Read = async (_, token) => { await Task.Delay(Timeout.Infinite, token); return ""; };
            using var form = Show(f.Workspace);
            Until(() => f.Probe.Reads.Count == 1);
            FindButton(form, "取消识别").PerformClick();
            Until(() => Field<Task?>(form, "_initializationTask")?.IsCompleted == true);
            Field<Task>(form, "_initializationTask").GetAwaiter().GetResult();
            Require(Pump(f.Workspace.Accounts.LoadAllAsync()).Value!.All(a => !HardwareVerificationSession.IsCurrent(a)), "UI cancel leaves old mapping on disk");
            f.Probe.Read = original;
            // PerformClick bypasses the real message loop that installs the UI context.
            SynchronizationContext.SetSynchronizationContext(new System.Windows.Forms.WindowsFormsSynchronizationContext());
            FindButton(form, "自动识别设备").PerformClick();
            Until(() => FindButton(form, "自动识别设备").Enabled && Field<System.Windows.Forms.Label>(form, "_message").Text.Contains("Mapping 获取成功"));
            Require(f.Probe.Reads.Count == 3 && f.Workspace.Processes.Snapshot().All(v => HardwareVerificationSession.IsCurrent(v.Config)), "manual retry rescans once and saves");
            var grid = Field<System.Windows.Forms.DataGridView>(form, "_grid");
            Require(grid.Rows[0].Cells[3].ToolTipText.Contains("已识别"), "per-account result visible when scan completes");
            var view = f.Workspace.Processes.Snapshot()[0];
            var message = form.GetType().GetMethod("DiscoveryMessage", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            Require(message.Invoke(form, [view with { DesiredRunning = true }]) is null
                && message.Invoke(form, [view with { WorkerProcessId = 123 }]) is null,
                "waiting-to-start message cannot obscure a starting or live account");
        }
        finally { Pump(f.DisposeAsync().AsTask()); }
    });

    private static System.Windows.Forms.Button FindButton(System.Windows.Forms.Control control, string name) =>
        control.Controls.Cast<System.Windows.Forms.Control>().SelectMany(Descendants)
            .OfType<System.Windows.Forms.Button>().Single(b => b.Text == name);
    private static IEnumerable<System.Windows.Forms.Control> Descendants(System.Windows.Forms.Control c) =>
        new[] { c }.Concat(c.Controls.Cast<System.Windows.Forms.Control>().SelectMany(Descendants));
    private static Roadhog.MultiAccountForm Show(MultiAccountWorkspace workspace)
    {
        var form = new Roadhog.MultiAccountForm(workspace) { ShowInTaskbar = false, Opacity = 0,
            StartPosition = System.Windows.Forms.FormStartPosition.Manual, Location = new(-30000, -30000) };
        var tray = Field<System.Windows.Forms.NotifyIcon>(form, "_tray"); tray.Visible = false;
        form.Disposed += (_, _) => { Field<System.Windows.Forms.Timer>(form, "_timer").Dispose(); tray.Dispose(); };
        form.Show(); return form;
    }
    private static T Field<T>(object value, string name) => (T)value.GetType().GetField(name,
        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(value)!;
    private static void Until(Func<bool> condition)
    {
        var clock = Stopwatch.StartNew();
        while (!condition()) { if (clock.Elapsed > TimeSpan.FromSeconds(10)) throw new TimeoutException("UI fixture timeout"); System.Windows.Forms.Application.DoEvents(); Thread.Sleep(5); }
    }
    private static void Pump(Task work) { Until(() => work.IsCompleted); work.GetAwaiter().GetResult(); }
    private static T Pump<T>(Task<T> work) { Pump((Task)work); return work.GetAwaiter().GetResult(); }
    private static Task Sta(Action action)
    {
        var complete = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() => { try { SynchronizationContext.SetSynchronizationContext(new System.Windows.Forms.WindowsFormsSynchronizationContext()); action(); complete.SetResult(); }
            catch (Exception ex) { complete.SetException(ex); } }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); return complete.Task;
    }

    public static async Task<int> RunChildAsync(string[] args)
    {
        var scenario = args.Single(a => a.StartsWith("--discovery-fixture="))[20..];
        var path = args[Array.IndexOf(args, "--device-discovery") + 1];
        if (scenario == "production") return await DeviceDiscoveryProcess.RunChildAsync(path);
        var request = JsonSerializer.Deserialize<DeviceDiscoveryRequest>(await File.ReadAllTextAsync(path))!;
        if (scenario == "hang")
        {
            using var current = Process.GetCurrentProcess();
            new DeviceLeaseStore(request.LeasePath).TryAcquire(current.Id, new DateTimeOffset(current.StartTime.ToUniversalTime()),
                Path.GetDirectoryName(path)!, "fixture", request.VmmDeviceName);
            await Task.Delay(Timeout.Infinite);
        }
        await File.WriteAllTextAsync(path + ".result", JsonSerializer.Serialize(new DeviceDiscoveryResponse(
            scenario == "stale" ? "wrong-token" : request.Token, request.VmmDeviceName, "fixture-role", null)));
        return 0;
    }
}
