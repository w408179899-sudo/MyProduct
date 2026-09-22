using System.Diagnostics;
using System.Text.Json;
using Roadhog.Application;
using Roadhog.Core.Accounts;
using Roadhog.Core.Common;
using Roadhog.Core.Diagnostics;
using Roadhog.Infrastructure.Composition;
using Roadhog.Infrastructure.Hardware;
using Roadhog.Infrastructure.WorkerProcesses;

internal static class WorkerProcessRobustnessTests
{
    public static async Task<int> RunChildAsync(string[] args)
    {
        var spec = JsonSerializer.Deserialize<WorkerLaunchSpec>(await File.ReadAllTextAsync(args[Array.IndexOf(args, "--account-worker") + 1]))!;
        var scenario = args.FirstOrDefault(value => value.StartsWith("--robustness-scenario=", StringComparison.Ordinal))?.Split('=', 2)[1] ?? "normal";
        if (scenario == "delayed-factory")
        {
            RecordWorker(spec);
            WriteMarker(Path.Combine(spec.Paths.ClientRoot, "factory-entered"), Environment.ProcessId.ToString());
            await Task.Delay(800);
        }
        return await new WorkerProcessHost(launch =>
        {
            RecordWorker(launch);
            return new Backend(launch, scenario);
        }).RunAsync(spec);
    }

    public static async Task<int> RunControllerAsync(string[] args)
    {
        var control = JsonSerializer.Deserialize<ControllerSpec>(await File.ReadAllTextAsync(args[Array.IndexOf(args, "--robustness-controller") + 1]))!;
        // The parent intentionally terminates this process without disposing the
        // manager, reproducing a UI crash between Process.Start and manifest publication.
        var manager = CreateManager(control.Root, control.Scenario, startupTimeout: TimeSpan.FromSeconds(4));
        await manager.InitializeAsync(control.Accounts);
        var starting = manager.StartAsync(control.Accounts[0].InstanceId);
        await File.WriteAllTextAsync(Path.Combine(control.Root, "controller-entered"), Environment.ProcessId.ToString());
        await starting;
        await Task.Delay(Timeout.Infinite);
        return 0;
    }

    public static async Task RepeatedStartStopAndConcurrentStopAsync()
    {
        await using var test = new EnvironmentScope();
        var account = test.Account(1);
        var manager = await test.ManagerAsync(new[] { account });
        var seenPids = new HashSet<int>();
        for (var cycle = 0; cycle < 8; cycle++)
        {
            RequireSuccess(await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => manager.StartAsync(account.InstanceId))), "parallel start " + cycle);
            var running = await RunningAsync(manager, account);
            var pid = running.WorkerProcessId!.Value;
            seenPids.Add(pid);
            Require((await InfoAsync(test, account)).Starts == 1, "parallel starts must enter backend once in cycle " + cycle);
            var stopping = Enumerable.Range(0, 6).Select(_ => manager.StopAsync(account.InstanceId)).ToArray();
            Require((await stopping[0].WaitAsync(TimeSpan.FromSeconds(4))).Success, "first stop completes in cycle " + cycle);
            var restarting = manager.StartAsync(account.InstanceId);
            RequireSuccess(await Task.WhenAll(stopping).WaitAsync(TimeSpan.FromSeconds(4)), "parallel stop " + cycle);
            var restart = await restarting.WaitAsync(TimeSpan.FromSeconds(4));
            if (!restart.Success) Require((await manager.StartAsync(account.InstanceId)).Success, "explicit retry after a still-pending stop succeeds");
            var next = await RunningAsync(manager, account);
            Require(next.WorkerProcessId != pid && !Alive(pid), "restart uses a fresh child after the old child exits");
            Require((await InfoAsync(test, account)).Starts == 1, "late duplicate Stop cannot restart backend work in a new generation");
            await Task.Delay(100);
            Require(View(manager, account).DesiredRunning && View(manager, account).WorkerProcessId == next.WorkerProcessId,
                "late completion from a previous Stop cannot terminate the newer start");
            Require((await manager.StopAsync(account.InstanceId)).Success, "cycle cleanup stop");
        }
        Require(seenPids.Count == 8, "every cycle owned an independent completed process lifetime");
        Require(new DeviceLeaseStore(test.LeasePath).ReadActive().Value?.Count == 0, "repeated stops leave no active DMA leases");
    }

    public static async Task RepeatedRandomCrashesRemainIsolatedAsync()
    {
        await using var test = new EnvironmentScope();
        var accounts = Enumerable.Range(1, 3).Select(test.Account).ToArray();
        var manager = await test.ManagerAsync(accounts);
        RequireSuccess(await Task.WhenAll(accounts.Select(account => manager.StartAsync(account.InstanceId))), "start stress accounts");
        var random = new Random(73421);
        for (var round = 0; round < 9; round++)
        {
            var before = await Task.WhenAll(accounts.Select(account => RunningAsync(manager, account)));
            var victim = random.Next(accounts.Length);
            var oldPid = before[victim].WorkerProcessId!.Value;
            await KillAsync(oldPid);
            await UntilAsync(() => View(manager, accounts[victim]) is { State: "running", WorkerProcessId: { } pid } && pid != oldPid,
                "crashed account recovers in round " + round, 10000);
            for (var index = 0; index < accounts.Length; index++)
            {
                var current = View(manager, accounts[index]);
                Require(current.DesiredRunning && current.Worker?.IsRunning == true, "every account still intends and reports running after recovery");
                if (index != victim) Require(current.WorkerProcessId == before[index].WorkerProcessId, "crash must never restart a neighboring account");
                Require((await InfoAsync(test, accounts[index])).Starts == 1, "backend start remains once per process under repeated recovery");
            }
        }
        manager.BeginShutdown();
        RequireSuccess((await manager.StopAllAsync()).Values, "stress StopAll succeeds");
        await Task.Delay(200);
        Require(manager.Snapshot().All(view => !view.DesiredRunning && view.WorkerProcessId is null), "no recovery remains after final stop");
    }

    public static async Task ForgedAndStaleManifestsNeverStopAnotherAccountAsync()
    {
        await using var test = new EnvironmentScope();
        var first = test.Account(1);
        var second = test.Account(2);
        first.AutoRecover = false;
        var original = await test.ManagerAsync(new[] { first, second });
        Require((await original.StartAsync(second.InstanceId)).Success, "start legitimate neighboring worker");
        var neighborPid = (await RunningAsync(original, second)).WorkerProcessId!.Value;
        var neighbor = ReadDescriptor(test.Manifest(second));
        await test.DetachAsync(original);
        foreach (var variant in new[] { "forged", "wrong-start-time", "wrong-token", "malformed" })
        {
            Directory.CreateDirectory(Path.GetDirectoryName(test.Manifest(first))!);
            var forged = neighbor with { InstanceId = first.InstanceId, AccountName = first.AccountName };
            if (variant == "wrong-start-time") forged = forged with { ProcessStartedAtUtc = forged.ProcessStartedAtUtc.AddMinutes(-1) };
            if (variant == "wrong-token") forged = forged with { Token = new string('0', 64) };
            await File.WriteAllTextAsync(test.Manifest(first), variant == "malformed" ? "{broken" : JsonSerializer.Serialize(forged));
            var manager = await test.ManagerAsync(new[] { first, second });
            await manager.StopAsync(first.InstanceId).WaitAsync(TimeSpan.FromSeconds(4));
            Require(Alive(neighborPid), variant + " manifest cannot make Stop target another account process");
            Require((await InfoAsync(test, second)).Running, variant + " manifest cannot send another account a shutdown command");
            Require(View(manager, first).WorkerProcessId != neighborPid, "unverified manifest is never adopted as owned");
            await test.DetachAsync(manager);
        }
    }

    public static async Task ResponsiveButUninitializedWorkersHaveBoundedLifetimeAsync()
    {
        await using (var test = new EnvironmentScope())
        {
            var account = test.Account(1);
            account.AutoRecover = false;
            var manager = await test.ManagerAsync(new[] { account }, "never-initialize", TimeSpan.FromSeconds(1));
            var starting = manager.StartAsync(account.InstanceId);
            await UntilAsync(() => View(manager, account).WorkerProcessId is not null, "child is created before initialization deadline");
            var pid = View(manager, account).WorkerProcessId!.Value;
            var status = await Client(test, account).CallAsync<WorkerStatus>(WorkerCommands.Status, Array.Empty<object?>());
            Require(!status.InitializationComplete, "the uninitialized worker still answers status");
            Require(!(await starting.WaitAsync(TimeSpan.FromSeconds(4))).Success, "startup deadline reports failure");
            await UntilAsync(() => !Alive(pid) && View(manager, account).WorkerProcessId is null,
                "a responsive status endpoint cannot keep an uninitialized account alive indefinitely", 4000);
            await Task.Delay(250);
            Require(!View(manager, account).WorkerProcessId.HasValue, "AutoRecover=false does not relaunch after startup timeout");
        }
        await using (var test = new EnvironmentScope())
        {
            var account = test.Account(1);
            var manager = await test.ManagerAsync(new[] { account }, "first-initialization-hangs", TimeSpan.FromSeconds(1));
            var starting = manager.StartAsync(account.InstanceId);
            await UntilAsync(() => View(manager, account).WorkerProcessId is not null, "first recovering child starts");
            var firstPid = View(manager, account).WorkerProcessId!.Value;
            await starting.WaitAsync(TimeSpan.FromSeconds(4));
            await UntilAsync(() => View(manager, account) is { State: "running", WorkerProcessId: { } pid } && pid != firstPid,
                "startup timeout reclaims the first process before retrying with a fresh child", 8000);
            Require(!Alive(firstPid), "timed-out startup never overlaps its replacement");
            Require((await InfoAsync(test, account)).Starts == 1, "only recovered ready backend is started");
        }
    }

    public static async Task MissingManifestReattachesWithoutReplacingLaunchIdentityAsync()
    {
        await using var test = new EnvironmentScope();
        var account = test.Account(1);
        var manager = await test.ManagerAsync(new[] { account });
        Require((await manager.StartAsync(account.InstanceId)).Success, "start worker before manifest loss");
        var originalPid = (await RunningAsync(manager, account)).WorkerProcessId!.Value;
        var launch = test.ReadLaunch(account);
        await test.DetachAsync(manager);
        File.Delete(test.Manifest(account));
        var reopened = await test.ManagerAsync(new[] { account });
        var attached = await RunningAsync(reopened, account);
        var after = test.ReadLaunch(account);
        Require(attached.WorkerProcessId == originalPid, "missing manifest must not orphan the already-running worker");
        Require(after.PipeName == launch.PipeName && after.Token == launch.Token, "reattach preserves the original worker pipe and token");
        Require((await InfoAsync(test, account)).Starts == 1, "rediscovery never starts account work twice");
        Require((await reopened.StopAsync(account.InstanceId)).Success && !Alive(originalPid), "rediscovered worker is still independently stoppable");
    }

    public static async Task ControllerCrashBeforeManifestStillReattachesAsync()
    {
        await using var test = new EnvironmentScope();
        var account = test.Account(1);
        var control = new ControllerSpec(test.Root, new[] { account }, "delayed-factory");
        var controllerPath = Path.Combine(test.Root, "controller-spec.json");
        await File.WriteAllTextAsync(controllerPath, JsonSerializer.Serialize(control));
        using var controller = test.LaunchController(controllerPath);
        await UntilAsync(() => File.Exists(Path.Combine(test.Root, "factory-entered")), "worker enters pre-manifest startup window");
        var pid = int.Parse(await File.ReadAllTextAsync(Path.Combine(test.Root, "factory-entered")));
        var launch = test.ReadLaunch(account);
        Require(!File.Exists(test.Manifest(account)), "fault injection precedes host manifest publication");
        controller.Kill(entireProcessTree: false);
        await controller.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(3));
        Require(Alive(pid), "worker survives abrupt controller exit");
        var reopened = await test.ManagerAsync(new[] { account }, "normal", TimeSpan.FromSeconds(4));
        var running = await RunningAsync(reopened, account);
        var after = test.ReadLaunch(account);
        Require(running.WorkerProcessId == pid, "new controller takes ownership of the existing startup process");
        Require(after.PipeName == launch.PipeName && after.Token == launch.Token, "controller crash cannot cause launch identity overwrite");
        Require((await InfoAsync(test, account)).Starts == 1, "recovered controller starts work once after initialization");
    }

    public static async Task DisposedManagerCannotSpawnThroughOldRuntimeAsync()
    {
        await using var test = new EnvironmentScope();
        var account = test.Account(1);
        var manager = await test.ManagerAsync(new[] { account });
        var runtime = manager.RuntimeFor(account.InstanceId);
        Require((await manager.StartAsync(account.InstanceId)).Success, "start before dispose");
        var pid = (await RunningAsync(manager, account)).WorkerProcessId!.Value;
        await test.DetachAsync(manager);
        await KillAsync(pid);
        var observedBefore = test.RecordedWorkerCount();
        var rejected = false;
        try { await runtime.ReadPlayerAsync(account.AccountName).WaitAsync(TimeSpan.FromSeconds(2)); }
        catch (Exception exception) when (exception is ObjectDisposedException or InvalidOperationException or OperationCanceledException) { rejected = true; }
        Require(rejected, "a runtime retained by a closed form rejects calls after manager disposal");
        await Task.Delay(250);
        Require(test.RecordedWorkerCount() == observedBefore, "disposed manager never creates a replacement process");
    }

    private static WorkerProcessManager CreateManager(string root, string scenario, TimeSpan startupTimeout) => new(
        new RoadhogServiceOptions
        {
            AccountConfigPath = Path.Combine(root, "config", "accounts.json"), PathLibraryDirectory = Path.Combine(root, "config", "paths"),
            ProfileLibraryDirectory = Path.Combine(root, "config", "profiles"), RadarMapDirectory = Path.Combine(root, "config", "radar"),
            LogDirectory = Path.Combine(root, "logs"), OwnerLicenseGrantPath = Path.Combine(root, "unused-owner.json")
        }, NoOpRoadhogLogger.Instance, new WorkerProcessLaunchOptions
        {
            ExecutablePath = Path.Combine(AppContext.BaseDirectory, "Roadhog.Tests.exe"),
            PrefixArguments = new[] { "--robustness-worker", "--robustness-scenario=" + scenario },
            StartupTimeout = startupTimeout, StopTimeout = TimeSpan.FromMilliseconds(750),
            PollInterval = TimeSpan.FromMilliseconds(30), RecoveryDelay = TimeSpan.FromMilliseconds(40),
            LeasePath = Path.Combine(root, "leases.json")
        });

    private static AccountProcessView View(WorkerProcessManager manager, AccountConfig account) => manager.Snapshot().Single(value => value.Config.InstanceId == account.InstanceId);
    private static async Task<AccountProcessView> RunningAsync(WorkerProcessManager manager, AccountConfig account)
    {
        try
        {
            await UntilAsync(() => View(manager, account) is { State: "running", WorkerProcessId: not null, Worker.IsRunning: true }, "account reaches running: " + account.AccountName, 10000);
        }
        catch (TimeoutException)
        {
            var state = View(manager, account);
            throw new TimeoutException($"Account did not reach running: {account.AccountName}; state={state.State}; pid={state.WorkerProcessId}; error={state.Error}");
        }
        return View(manager, account);
    }
    private static WorkerDescriptor ReadDescriptor(string path) => JsonSerializer.Deserialize<WorkerDescriptor>(File.ReadAllText(path))!;
    private static WorkerRpcClient Client(EnvironmentScope test, AccountConfig account) { var spec = test.ReadLaunch(account); return new(spec.PipeName, spec.Token); }
    private static Task<BackendInfo> InfoAsync(EnvironmentScope test, AccountConfig account) => Client(test, account).CallAsync<BackendInfo>("robustness.info", Array.Empty<object?>()).WaitAsync(TimeSpan.FromSeconds(2));
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void RequireSuccess(IEnumerable<OperationResult> values, string reason) { var failed = values.Where(value => !value.Success).ToArray(); Require(failed.Length == 0, reason + ": " + string.Join("; ", failed.Select(value => value.Error))); }
    private static async Task UntilAsync(Func<bool> condition, string reason, int milliseconds = 5000)
    {
        var elapsed = Stopwatch.StartNew();
        while (!condition()) { if (elapsed.ElapsedMilliseconds >= milliseconds) throw new TimeoutException(reason); await Task.Delay(15); }
    }
    private static bool Alive(int pid) { try { using var process = Process.GetProcessById(pid); return !process.HasExited; } catch (ArgumentException) { return false; } }
    private static async Task KillAsync(int pid) { try { using var process = Process.GetProcessById(pid); if (!process.HasExited) process.Kill(entireProcessTree: false); await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(3)); } catch (ArgumentException) { } }
    private static void RecordWorker(WorkerLaunchSpec spec)
    {
        Directory.CreateDirectory(Path.Combine(spec.Paths.ClientRoot, "observed-workers"));
        using var process = Process.GetCurrentProcess();
        WriteMarker(Path.Combine(spec.Paths.ClientRoot, "observed-workers", Environment.ProcessId + ".json"), JsonSerializer.Serialize(new OwnedProcess(Environment.ProcessId, process.StartTime.ToUniversalTime())));
    }
    private static void WriteMarker(string path, string text) { var temporary = path + ".tmp"; File.WriteAllText(temporary, text); File.Move(temporary, path, true); }
    private sealed record ControllerSpec(string Root, AccountConfig[] Accounts, string Scenario);
    private sealed record OwnedProcess(int ProcessId, DateTime StartedAtUtc);
    private sealed record BackendInfo(int Starts, bool Running);

    private sealed class Backend(WorkerLaunchSpec spec, string scenario) : IWorkerProcessBackend
    {
        private readonly AccountRuntimeManager _states = new(NoOpRoadhogLogger.Instance);
        private int _starts;
        private volatile bool _running;
        public async Task InitializeAsync(CancellationToken cancellationToken)
        {
            _states.MarkStarting(spec.Account);
            _states.MarkStopped(spec.Account.AccountName);
            var hang = scenario == "never-initialize";
            if (scenario == "first-initialization-hangs")
            {
                try { using var marker = new FileStream(Path.Combine(spec.Paths.ClientRoot, "initialization-attempted"), FileMode.CreateNew, FileAccess.Write); hang = true; }
                catch (IOException) { }
            }
            if (hang) await Task.Delay(Timeout.Infinite, CancellationToken.None);
        }
        public WorkerStatus GetStatus() => new() { Authorized = true, IsRunning = _running, Snapshot = _states.Snapshot().FirstOrDefault() };
        public Task<OperationResult> StartAsync(AccountConfig account, bool cleanupFirst, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _starts);
            _states.MarkStarting(account); _states.MarkRunning(account.AccountName, Environment.CurrentManagedThreadId); _running = true;
            return Task.FromResult(OperationResult.Ok());
        }
        public Task<OperationResult> StopAsync(CancellationToken cancellationToken) { _running = false; _states.MarkStopped(spec.Account.AccountName); return Task.FromResult(OperationResult.Ok()); }
        public Task<OperationResult> ReleaseInputAsync(CancellationToken cancellationToken) => Task.FromResult(OperationResult.Ok());
        public Task<OperationResult<HardwareVerification>> VerifyHardwareAsync(CancellationToken cancellationToken) => Task.FromResult(OperationResult<HardwareVerification>.Ok(new(spec.Account.CharacterName, spec.Account.HardwareKey, spec.Account.VmmDeviceName, true, HardwareVerificationSession.CurrentId)));
        public Task<object?> InvokeAsync(string method, JsonElement[] arguments, IProgress<string> progress, CancellationToken cancellationToken) =>
            method == "robustness.info" ? Task.FromResult<object?>(new BackendInfo(Volatile.Read(ref _starts), _running)) : throw new InvalidOperationException("Unexpected test command: " + method);
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class EnvironmentScope : IAsyncDisposable
    {
        public string Root { get; } = Path.Combine(FindWorkspace(), ".tmp", "multi-account-tests", "robustness", Guid.NewGuid().ToString("N"));
        public string LeasePath => Path.Combine(Root, "leases.json");
        private readonly string _deviceScope = Guid.NewGuid().ToString("N");
        private readonly int _port = Random.Shared.Next(20000, 60000);
        private readonly List<WorkerProcessManager> _managers = new();
        private readonly List<OwnedProcess> _controllers = new();
        public EnvironmentScope() => Directory.CreateDirectory(Path.Combine(Root, "config"));
        public AccountConfig Account(int number) => new()
        {
            InstanceId = Guid.NewGuid().ToString("N"), AccountName = "robust-account-" + number, CharacterName = "压力角色" + number,
            HardwareVerificationSessionId = HardwareVerificationSession.CurrentId,
            HardwareKey = "mock-" + _deviceScope + "-" + number, VmmDeviceName = "fpga://devindex=" + number, AutoRecover = true,
            KmBox = new() { IpAddress = "127.0.0." + number, Port = _port, Mac = _deviceScope + number }
        };
        public async Task<WorkerProcessManager> ManagerAsync(AccountConfig[] accounts, string scenario = "normal", TimeSpan? startupTimeout = null)
        {
            var manager = CreateManager(Root, scenario, startupTimeout ?? TimeSpan.FromSeconds(3));
            _managers.Add(manager); await manager.InitializeAsync(accounts); return manager;
        }
        public async Task DetachAsync(WorkerProcessManager manager) { _managers.Remove(manager); await manager.DisposeAsync(); }
        public string Manifest(AccountConfig account) => Path.Combine(Root, "config", "workers", Guid.Parse(account.InstanceId).ToString("N"), "worker.json");
        public WorkerLaunchSpec ReadLaunch(AccountConfig account) => JsonSerializer.Deserialize<WorkerLaunchSpec>(File.ReadAllText(Path.Combine(Path.GetDirectoryName(Manifest(account))!, "launch.json")))!;
        public int RecordedWorkerCount() => Directory.Exists(Path.Combine(Root, "observed-workers")) ? Directory.EnumerateFiles(Path.Combine(Root, "observed-workers"), "*.json").Count() : 0;
        public Process LaunchController(string specPath)
        {
            var start = new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "Roadhog.Tests.exe")) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
            start.ArgumentList.Add("--robustness-controller"); start.ArgumentList.Add(specPath);
            var process = Process.Start(start)!; _controllers.Add(new(process.Id, process.StartTime.ToUniversalTime())); return process;
        }
        public async ValueTask DisposeAsync()
        {
            foreach (var manager in _managers)
            {
                try { manager.BeginShutdown(); await manager.StopAllAsync().WaitAsync(TimeSpan.FromSeconds(6)); }
                catch { }
                try { await manager.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(6)); } catch { }
            }
            foreach (var controller in _controllers) await KillOwnedAsync(controller);
            var observed = Path.Combine(Root, "observed-workers");
            if (Directory.Exists(observed))
                foreach (var path in Directory.EnumerateFiles(observed, "*.json"))
                    await KillOwnedAsync(JsonSerializer.Deserialize<OwnedProcess>(await File.ReadAllTextAsync(path))!);
            Directory.Delete(Root, recursive: true);
        }
        private static async Task KillOwnedAsync(OwnedProcess owned)
        {
            try
            {
                using var process = Process.GetProcessById(owned.ProcessId);
                if (!process.HasExited && process.StartTime.ToUniversalTime() == owned.StartedAtUtc &&
                    string.Equals(process.MainModule?.FileName, Path.Combine(AppContext.BaseDirectory, "Roadhog.Tests.exe"), StringComparison.OrdinalIgnoreCase))
                { process.Kill(entireProcessTree: false); await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(3)); }
            }
            catch (ArgumentException) { }
        }
        private static string FindWorkspace()
        {
            for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
                if (File.Exists(Path.Combine(directory.FullName, "Roadhog.Tests", "Roadhog.Tests.csproj"))) return directory.FullName;
            throw new InvalidOperationException("Run robustness tests from this workspace's built test executable.");
        }
    }
}
