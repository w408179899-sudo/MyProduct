using System.Diagnostics;
using System.Text.Json;
using Roadhog.Application;
using Roadhog.Core.Accounts;
using Roadhog.Core.Common;
using Roadhog.Core.Diagnostics;
using Roadhog.Infrastructure.Composition;
using Roadhog.Infrastructure.Hardware;
using Roadhog.Infrastructure.WorkerProcesses;

internal static class WorkerProcessManagerTests
{
    public static async Task<int> RunChildAsync(string[] args)
    {
        // This entry only exists in the test executable. It never constructs real
        // services, authorizes against a server, opens DMA, or sends KMBox input.
        try
        {
            var index = Array.IndexOf(args, "--account-worker");
            var spec = JsonSerializer.Deserialize<WorkerLaunchSpec>(await File.ReadAllTextAsync(args[index + 1]))!;
            var scenario = args.FirstOrDefault(a => a.StartsWith("--worker-scenario=", StringComparison.Ordinal))?.Split('=', 2)[1] ?? "normal";
            if (scenario == "account1-stuck-init") scenario = spec.Account.AccountName.EndsWith("-1", StringComparison.Ordinal) ? "stuck-init" : "normal";
            return await new WorkerProcessHost(s => new MockBackend(s, scenario)).RunAsync(spec);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 99;
        }
    }

    public static async Task IsolationAndDuplicateStartAsync()
    {
        await using var test = new TestEnvironment();
        var first = test.Account(1);
        var second = test.Account(2);
        var manager = await test.ManagerAsync(new[] { first, second });
        RequireAll(await Task.WhenAll(manager.StartAsync(first.InstanceId), manager.StartAsync(second.InstanceId)), "start accounts");
        var firstStatus = await test.RunningAsync(manager, first);
        var secondStatus = await test.RunningAsync(manager, second);
        Require(firstStatus.WorkerProcessId != secondStatus.WorkerProcessId && firstStatus.WorkerProcessId != Environment.ProcessId,
            "accounts have independent process IDs");
        using (var firstProcess = Process.GetProcessById(firstStatus.WorkerProcessId!.Value))
        using (var secondProcess = Process.GetProcessById(secondStatus.WorkerProcessId!.Value))
            Require(firstProcess.MainWindowHandle == IntPtr.Zero && secondProcess.MainWindowHandle == IntPtr.Zero, "account workers have no user-facing window");
        RequireAll(await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => manager.StartAsync(first.InstanceId))), "duplicate start is idempotent");
        var firstInfo = await test.InfoAsync(first);
        var secondInfo = await test.InfoAsync(second);
        Require(firstInfo.Starts == 1 && secondInfo.Starts == 1, $"duplicate starts must not restart the backend; actual first={firstInfo.Starts}, second={secondInfo.Starts}");
        Require(firstInfo.Account.HardwareKey == first.HardwareKey && secondInfo.Account.HardwareKey == second.HardwareKey,
            "DMA binding does not cross accounts");
        Require(firstInfo.Account.KmBox!.Mac == first.KmBox!.Mac && secondInfo.Account.KmBox!.Mac == second.KmBox!.Mac,
            "KMBox binding does not cross accounts");
        Require(firstInfo.Paths.LogDirectory != secondInfo.Paths.LogDirectory && firstInfo.Paths.LicenseCredentialPath != secondInfo.Paths.LicenseCredentialPath,
            "logs and account credentials have separate paths");
        Require(firstInfo.Paths.AccountConfigPath == secondInfo.Paths.AccountConfigPath &&
            firstInfo.Paths.PathLibraryDirectory == secondInfo.Paths.PathLibraryDirectory &&
            firstInfo.Paths.ProfileLibraryDirectory == secondInfo.Paths.ProfileLibraryDirectory &&
            firstInfo.Paths.RadarMapDirectory == secondInfo.Paths.RadarMapDirectory &&
            firstInfo.Paths.BagCleanupNameListPath == secondInfo.Paths.BagCleanupNameListPath, "configuration libraries are shared");
        Require(!firstInfo.Paths.EnableLogging && firstInfo.Paths.LicenseHeartbeatInterval == TimeSpan.FromSeconds(73) &&
            firstInfo.Paths.LicenseHeartbeatRetryCount == 2 && firstInfo.Paths.LicenseHeartbeatRetryDelay == TimeSpan.FromSeconds(4) &&
            firstInfo.Paths.LicenseRequestTimeout == TimeSpan.FromSeconds(9) && firstInfo.Paths.AccountWorkerTickInterval == TimeSpan.FromMilliseconds(125) &&
            firstInfo.Paths.AccountWorkerStopTimeout == TimeSpan.FromSeconds(6) && firstInfo.Paths.PollPlayerSnapshotInWorker,
            "custom logging authorization and worker timing survive launch serialization");
        var reconstructed = RoadhogWorkerProcessBackend.CreateOptions(test.Spec(first));
        Require(!reconstructed.EnableLogging && reconstructed.LicenseHeartbeatInterval == firstInfo.Paths.LicenseHeartbeatInterval &&
            reconstructed.LicenseHeartbeatRetryCount == firstInfo.Paths.LicenseHeartbeatRetryCount &&
            reconstructed.LicenseHeartbeatRetryDelay == firstInfo.Paths.LicenseHeartbeatRetryDelay &&
            reconstructed.LicenseRequestTimeout == firstInfo.Paths.LicenseRequestTimeout &&
            reconstructed.AccountWorkerTickInterval == firstInfo.Paths.AccountWorkerTickInterval &&
            reconstructed.AccountWorkerStopTimeout == firstInfo.Paths.AccountWorkerStopTimeout && reconstructed.PollPlayerSnapshotInWorker &&
            reconstructed.BagCleanupNameListPath == firstInfo.Paths.BagCleanupNameListPath, "production options preserve explicit launch settings without connecting hardware");
        Require((await manager.StopAsync(first.InstanceId)).Success, "first account stops");
        Require(!IsAlive(firstStatus.WorkerProcessId!.Value) && IsAlive(secondStatus.WorkerProcessId!.Value), "stopping first leaves second process alive");
        Require((await test.InfoAsync(second)).Running, "second backend keeps running");
        Require((await manager.StopAllAsync()).Values.All(value => value.Success), "stop all succeeds");
        Require(new DeviceLeaseStore(test.LeasePath).ReadActive().Value?.Count == 0, "normal exit releases every hardware lease");

        var delayedSettingsRuntime = manager.RuntimeFor(first.InstanceId);
        manager.BeginShutdown();
        Require(!(await manager.StartAsync(first.InstanceId)).Success, "application shutdown rejects new starts");
        var rejectedLazyRequest = false;
        try { await delayedSettingsRuntime.ReadPlayerAsync(first.AccountName); }
        catch (InvalidOperationException) { rejectedLazyRequest = true; }
        Require(rejectedLazyRequest && manager.Snapshot().All(view => view.WorkerProcessId is null), "a delayed settings request cannot relaunch an idle child during exit");
        Require((await manager.StopAsync(first.InstanceId)).Success, "Stop remains allowed during application shutdown");
        manager.CancelShutdown();
        Require((await manager.StartAsync(first.InstanceId)).Success, "cancelled application exit allows explicit account start again");
        Require((await manager.StopAsync(first.InstanceId)).Success, "resumed manager remains stoppable");
    }

    public static async Task CrashRecoveryAndDisableAsync()
    {
        await using var test = new TestEnvironment();
        var first = test.Account(1);
        var second = test.Account(2);
        var noRecovery = test.Account(3);
        noRecovery.AutoRecover = false;
        var manager = await test.ManagerAsync(new[] { first, second, noRecovery });
        RequireAll(await Task.WhenAll(manager.StartAsync(first.InstanceId), manager.StartAsync(second.InstanceId), manager.StartAsync(noRecovery.InstanceId)), "start crash tests");
        var oldFirst = (await test.RunningAsync(manager, first)).WorkerProcessId!.Value;
        var oldSecond = (await test.RunningAsync(manager, second)).WorkerProcessId!.Value;
        await KillAsync(oldFirst);
        await UntilAsync(() => View(manager, first) is { State: "running", WorkerProcessId: { } pid } && pid != oldFirst, "first account recovers to a new process");
        Require(View(manager, second).WorkerProcessId == oldSecond && (await test.InfoAsync(second)).Starts == 1,
            "another account is not restarted by recovery");
        var noRecoveryPid = (await test.RunningAsync(manager, noRecovery)).WorkerProcessId!.Value;
        await KillAsync(noRecoveryPid);
        await UntilAsync(() => View(manager, noRecovery) is { State: "failed", WorkerProcessId: null }, "disabled recovery remains failed");
        await Task.Delay(350);
        Require(View(manager, noRecovery).WorkerProcessId is null, "AutoRecover=false never spawns a replacement");

        // Race the monitor's recovery intent with an explicit user Stop.
        await KillAsync(View(manager, first).WorkerProcessId!.Value);
        Require((await manager.StopAsync(first.InstanceId)).Success, "Stop wins over recovery");
        await Task.Delay(400);
        Require(View(manager, first) is { State: "stopped", DesiredRunning: false, WorkerProcessId: null }, "stopped account does not relaunch");
        Require(View(manager, second).WorkerProcessId == oldSecond, "other account still has original PID");
    }

    public static async Task DetachAndAdoptAsync()
    {
        await using var test = new TestEnvironment();
        var account = test.Account(1);
        var firstManager = await test.ManagerAsync(new[] { account });
        Require((await firstManager.StartAsync(account.InstanceId)).Success, "start before detach");
        var pid = (await test.RunningAsync(firstManager, account)).WorkerProcessId!.Value;
        await test.DetachAsync(firstManager);
        Require(IsAlive(pid), "manager detach leaves the worker alive");
        var secondManager = await test.ManagerAsync(new[] { account });
        Require((await test.RunningAsync(secondManager, account)).WorkerProcessId == pid, "new manager adopts existing PID");
        Require((await secondManager.StartAsync(account.InstanceId)).Success, "start of adopted worker is idempotent");
        Require((await test.InfoAsync(account)).Starts == 1, "adoption never repeats backend start");
        Require((await secondManager.StopAsync(account.InstanceId)).Success, "adopted process remains controllable");
        Require(!File.Exists(test.ManifestPath(account)), "shutdown removes own manifest");
    }

    public static async Task StopTimeoutAndStartupCancelAsync()
    {
        await using (var test = new TestEnvironment())
        {
            var account = test.Account(1);
            var manager = await test.ManagerAsync(new[] { account }, "stuck-stop");
            Require((await manager.StartAsync(account.InstanceId)).Success, "start unresponsive-stop process");
            var pid = (await test.RunningAsync(manager, account)).WorkerProcessId!.Value;
            var timer = Stopwatch.StartNew();
            Require((await manager.StopAsync(account.InstanceId).WaitAsync(TimeSpan.FromSeconds(5))).Success, "stop timeout falls back to terminating account process");
            Require(timer.Elapsed < TimeSpan.FromSeconds(4) && !IsAlive(pid), "stuck child is bounded and killed");
            Require(test.Logger.Entries.Any(entry => entry.EventName == "account_process.stop_timeout"), "timeout is diagnosed");
            await Task.Delay(300);
            Require(View(manager, account).WorkerProcessId is null && !View(manager, account).DesiredRunning, "forced stop does not recover");
        }
        await using (var test = new TestEnvironment())
        {
            var account = test.Account(1);
            var manager = await test.ManagerAsync(new[] { account }, "slow-start");
            var starting = manager.StartAsync(account.InstanceId);
            await UntilAsync(() => Directory.EnumerateFiles(test.Root, "start-entered", SearchOption.AllDirectories).Any(), "slow backend reached start");
            var stopping = manager.StopAsync(account.InstanceId);
            Require((await stopping.WaitAsync(TimeSpan.FromSeconds(5))).Success, "Stop cancels pending startup");
            Require(!(await starting.WaitAsync(TimeSpan.FromSeconds(2))).Success, "cancelled start reports no success");
            await Task.Delay(300);
            Require(View(manager, account) is { DesiredRunning: false, WorkerProcessId: null, State: "stopped" }, "cancelled startup cannot restore running intent");
        }
    }

    public static async Task AuthorizationAndIdentityGuardsAsync()
    {
        await using (var test = new TestEnvironment())
        {
            var account = test.Account(1);
            var manager = await test.ManagerAsync(new[] { account }, "unauthorized");
            Require(!(await manager.StartAsync(account.InstanceId)).Success, "unauthorized backend cannot start");
            Require(View(manager, account).WorkerProcessId is null, "rejected authorization reclaims the failed startup process");
            var rejectedLaunchToken = test.Spec(account).Token;
            await Task.Delay(500);
            Require(!View(manager, account).DesiredRunning && View(manager, account).WorkerProcessId is null,
                "explicit authorization rejection cancels recovery intent even when AutoRecover is enabled");
            Require(test.Spec(account).Token == rejectedLaunchToken, "authorization rejection does not repeatedly spawn replacement workers");
            Require(!Directory.EnumerateFiles(test.Root, "started", SearchOption.AllDirectories).Any(), "unauthorized backend never enters Start");
        }
        await using (var test = new TestEnvironment())
        {
            var account = test.Account(1);
            var manager = await test.ManagerAsync(new[] { account });
            var verification = await manager.VerifyHardwareAsync(account.Clone(), CancellationToken.None);
            Require(verification.CharacterName == account.CharacterName && verification.HardwareKey == account.HardwareKey && verification.KmBoxConnected,
                "hardware verification result survives RPC and belongs to selected account");
            Require(View(manager, account).WorkerProcessId is null, "temporary verification child is stopped");
            Require((await manager.StartAsync(account.InstanceId)).Success, "start identity test");
            var client = test.Client(account);
            foreach (var mutate in new Action<AccountConfig>[]
            {
                candidate => candidate.InstanceId = Guid.NewGuid().ToString("N"),
                candidate => candidate.AccountName = "another-account",
                candidate => candidate.HardwareKey = "another-device",
                candidate => candidate.VmmDeviceName = "fpga://devindex=99",
                candidate => candidate.KmBox!.Mac = "another-kmbox",
                candidate => candidate.LicenseCredentialPath = "another-license",
                candidate => candidate.OwnerLicenseGrantPath = "another-owner-license.json",
                candidate => candidate.BagCleanupNameListPath = "another-name-list.json",
                candidate => candidate.RadarMapDirectory = "another-radar-library"
            })
            {
                var foreign = account.Clone();
                mutate(foreign);
                await ExpectRpcFailureAsync(() => client.CallAsync<OperationResult>(WorkerCommands.Start, new object?[] { foreign }));
            }
            Require((await test.InfoAsync(account)).Starts == 1, "foreign identity never reaches backend start");
        }
    }

    public static async Task SlowInitializationAsync()
    {
        await using (var test = new TestEnvironment())
        {
            var account = test.Account(1);
            var manager = await test.ManagerAsync(new[] { account }, "slow-init");
            var timer = Stopwatch.StartNew();
            Require((await manager.StartAsync(account.InstanceId)).Success, "startup waits for authorization initialization");
            Require(timer.Elapsed >= TimeSpan.FromMilliseconds(300), "backend was not started before delayed initialization");
            Require((await test.InfoAsync(account)).Starts == 1, "delayed initialization reaches one backend start");
        }
        await using (var test = new TestEnvironment())
        {
            var account = test.Account(1);
            var manager = await test.ManagerAsync(new[] { account }, "stuck-init");
            var starting = manager.StartAsync(account.InstanceId);
            await UntilAsync(() => Directory.EnumerateFiles(test.Root, "init-entered", SearchOption.AllDirectories).Any(), "initialization entered");
            var status = await test.Client(account).CallAsync<WorkerStatus>(WorkerCommands.Status, Array.Empty<object?>());
            Require(!status.InitializationComplete && !status.Authorized, "status remains readable during authorization initialization");
            Require((await manager.StopAsync(account.InstanceId).WaitAsync(TimeSpan.FromSeconds(5))).Success, "Stop interrupts authorization initialization");
            Require(!(await starting.WaitAsync(TimeSpan.FromSeconds(2))).Success, "initializing startup reports cancellation");
            Require(!View(manager, account).DesiredRunning && View(manager, account).WorkerProcessId is null, "cancelled initialization leaves no child");
        }
    }

    public static async Task SlowRecoveryDoesNotBlockOtherAccountsAsync()
    {
        await using var test = new TestEnvironment();
        var recovering = test.Account(1);
        var healthy = test.Account(2);
        var previous = await test.ManagerAsync(new[] { recovering, healthy });
        RequireAll(await Task.WhenAll(previous.StartAsync(recovering.InstanceId), previous.StartAsync(healthy.InstanceId)), "initial accounts start");
        var recoveringPid = (await test.RunningAsync(previous, recovering)).WorkerProcessId!.Value;
        var healthyPid = (await test.RunningAsync(previous, healthy)).WorkerProcessId!.Value;
        await test.DetachAsync(previous);
        await KillAsync(recoveringPid);

        var manager = await test.ManagerAsync(new[] { recovering, healthy }, "account1-stuck-init");
        await UntilAsync(() => Directory.EnumerateFiles(test.Root, "init-entered", SearchOption.AllDirectories).Any(), "monitor recovery enters blocked initialization");
        await UntilAsync(() => View(manager, healthy).Worker?.IsRunning == true, "healthy account is adopted during another recovery");
        var reported = View(manager, healthy).Worker!.ReportedAtUtc;
        await UntilAsync(() => View(manager, healthy).Worker?.ReportedAtUtc > reported.AddMilliseconds(100),
            "healthy account status must continue updating during another account initialization", 1500);
        Require(View(manager, healthy).WorkerProcessId == healthyPid, "blocked recovery does not replace healthy process");
        Require((await manager.StopAsync(healthy.InstanceId).WaitAsync(TimeSpan.FromSeconds(2))).Success, "healthy account stops promptly during another recovery");
        Require((await manager.StartAsync(healthy.InstanceId).WaitAsync(TimeSpan.FromSeconds(2))).Success, "healthy account starts promptly during another recovery");
        Require(View(manager, recovering).Worker?.InitializationComplete != true, "other account still awaits initialization");
    }

    public static async Task IntentPersistenceFailuresAsync()
    {
        await using (var test = new TestEnvironment())
        {
            var account = test.Account(1);
            var manager = await test.ManagerAsync(new[] { account });
            var intentPath = Path.Combine(test.Root, "config", "workers", "run-intent.json");
            Directory.CreateDirectory(intentPath);
            try
            {
                var result = await manager.StartAsync(account.InstanceId);
                Require(!result.Success, "failed start-intent persistence reports failure");
                await Task.Delay(350);
                Require(!View(manager, account).DesiredRunning && View(manager, account).WorkerProcessId is null,
                    "failed intent cannot leave monitor authorized to launch account");
                Require(!Directory.EnumerateFiles(test.Root, "launch.json", SearchOption.AllDirectories).Any(), "no child launch occurs before successful intent commit");
            }
            finally { Directory.Delete(intentPath); }
        }
        await using (var test = new TestEnvironment())
        {
            var account = test.Account(1);
            var manager = await test.ManagerAsync(new[] { account });
            Require((await manager.StartAsync(account.InstanceId)).Success, "start before failed stop intent");
            var pid = (await test.RunningAsync(manager, account)).WorkerProcessId!.Value;
            var intentPath = Path.Combine(test.Root, "config", "workers", "run-intent.json");
            File.Delete(intentPath);
            Directory.CreateDirectory(intentPath);
            try
            {
                await manager.StopAsync(account.InstanceId).WaitAsync(TimeSpan.FromSeconds(3));
                Require(!IsAlive(pid) && !View(manager, account).DesiredRunning, "stop intent failure cannot prevent process shutdown");
                await Task.Delay(300);
                Require(View(manager, account).WorkerProcessId is null, "failed stop persistence never relaunches stopped account in current session");
            }
            finally { Directory.Delete(intentPath); }
        }
    }

    public static async Task HardwareLeaseAndDuplicateHostAsync()
    {
        await using var test = new TestEnvironment();
        var account = test.Account(1);
        var manager = await test.ManagerAsync(new[] { account });
        Require((await manager.StartAsync(account.InstanceId)).Success, "start ownership test");
        var spec = test.Spec(account);
        var duplicate = spec with { PipeName = "Roadhog.Tests." + Guid.NewGuid().ToString("N"), ManifestPath = Path.Combine(test.Root, "duplicate.json") };
        Require(await test.RunRejectedChildAsync(duplicate) == 10, "duplicate instance mutex rejects second host");
        var sharedHardware = spec.Account.Clone();
        sharedHardware.InstanceId = Guid.NewGuid().ToString("N");
        sharedHardware.AccountName = "duplicate-hardware";
        sharedHardware.KmBox = test.Account(4).KmBox;
        var conflict = duplicate with { Account = sharedHardware, PipeName = "Roadhog.Tests." + Guid.NewGuid().ToString("N") };
        Require(await test.RunRejectedChildAsync(conflict) == 11, "same DMA binding cannot be leased by another account process");
        var otherRoot = Path.Combine(test.Root, "other-client");
        var sharedEndpoint = test.Account(5);
        sharedEndpoint.KmBox!.IpAddress = account.KmBox!.IpAddress;
        sharedEndpoint.KmBox.Port = account.KmBox.Port;
        var endpointConflict = duplicate with
        {
            Account = sharedEndpoint, PipeName = "Roadhog.Tests." + Guid.NewGuid().ToString("N"),
            Paths = spec.Paths with { ClientRoot = otherRoot }
        };
        Require(await test.RunRejectedChildAsync(endpointConflict) == 13, "same KMBox endpoint is rejected across separate client roots and DMA devices");
        var sharedMac = test.Account(6);
        sharedMac.KmBox!.Mac = account.KmBox.Mac.ToUpperInvariant();
        var macConflict = endpointConflict with { Account = sharedMac, PipeName = "Roadhog.Tests." + Guid.NewGuid().ToString("N") };
        Require(await test.RunRejectedChildAsync(macConflict) == 14, "same KMBox MAC is rejected with a different network endpoint");
        Require((await test.InfoAsync(account)).Running, "rejected children do not disturb owner");
        Require(new DeviceLeaseStore(test.LeasePath).ReadActive().Value?.Count == 1, "only legitimate owner retains lease");
    }

    public static async Task PreviousBootRequiresEachAccountConfirmationAsync()
    {
        await using var test = new TestEnvironment();
        var accounts = Enumerable.Range(1, 6).Select(test.Account).ToArray();
        foreach (var account in accounts) account.HardwareVerificationSessionId = "previous-boot";
        var intentPath = Path.Combine(test.Root, "config", "workers", "run-intent.json");
        Directory.CreateDirectory(Path.GetDirectoryName(intentPath)!);
        await File.WriteAllTextAsync(intentPath, JsonSerializer.Serialize(accounts.Select(a => a.InstanceId)));
        var manager = await test.ManagerAsync(accounts);
        await Task.Delay(200);
        Require(manager.Snapshot().All(v => !v.DesiredRunning && v.WorkerProcessId is null && v.State == "verification_required"), "all six accounts remain stopped after a changed boot session");
        Require(JsonSerializer.Deserialize<string[]>(await File.ReadAllTextAsync(intentPath))!.Length == 0, "previous-boot restart intent is durably cleared");
        foreach (var account in accounts)
            Require(!(await manager.StartAsync(account.InstanceId)).Success, "manual start also requires this boot's confirmation");
        Require(!Directory.EnumerateFiles(test.Root, "launch.json", SearchOption.AllDirectories).Any(), "unverified starts do not spawn a worker or acquire hardware");
        var proof = await manager.VerifyHardwareAsync(accounts[0], CancellationToken.None);
        Require(proof.SessionId == HardwareVerificationSession.CurrentId && !(await manager.StartAsync(accounts[0].InstanceId)).Success,
            "reading alone cannot replace explicit save/confirmation");
        accounts[0].HardwareVerificationSessionId = proof.SessionId;
        manager.UpdateAccounts(accounts);
        Require((await manager.StartAsync(accounts[0].InstanceId)).Success, "the individually confirmed account starts");
        var pid = (await test.RunningAsync(manager, accounts[0])).WorkerProcessId;
        Require(manager.Snapshot().Skip(1).All(v => !v.DesiredRunning && v.WorkerProcessId is null), "confirming one account never unlocks the other five");
        await test.DetachAsync(manager);
        var reopened = await test.ManagerAsync(accounts);
        Require(View(reopened, accounts[0]).WorkerProcessId == pid && View(reopened, accounts[0]).DesiredRunning,
            "reopening only the main UI in the same boot adopts the same confirmed worker");
    }

    public static async Task ConcurrentSelfExitAndStopAsync()
    {
        await using var test = new TestEnvironment();
        var account = test.Account(1); account.AutoRecover = false;
        var manager = await test.ManagerAsync(new[] { account });
        for (var round = 0; round < 12; round++)
        {
            Require((await manager.StartAsync(account.InstanceId)).Success, "self-exit fixture starts");
            var pid = (await test.RunningAsync(manager, account)).WorkerProcessId!.Value;
            try { await test.Client(account).CallAsync<OperationResult>("mock.exit", new object?[] { round % 3 }); }
            catch (Exception exception) when (exception is IOException or WorkerRpcException) { }
            var stopped = await manager.StopAsync(account.InstanceId);
            Require(stopped.Success, "concurrent self-exit and Stop: " + stopped.Error);
            Require(!IsAlive(pid) && View(manager, account) is { DesiredRunning: false, WorkerProcessId: null }, "self-exit stop leaves no worker or recovery intent");
        }
    }

    public static async Task RejectedStartReleasesWorkerAsync()
    {
        await using var test = new TestEnvironment();
        var rejected = test.Account(1); var healthy = test.Account(2);
        var manager = await test.ManagerAsync(new[] { rejected, healthy }, "reject-start");
        Require((await manager.StartAsync(healthy.InstanceId)).Success, "healthy account starts");
        var healthyPid = (await test.RunningAsync(manager, healthy)).WorkerProcessId;
        var result = await manager.StartAsync(rejected.InstanceId);
        Require(!result.Success && result.Error == "mock binding mismatch", "actionable rejection is retained");
        Require(View(manager, rejected) is { DesiredRunning: false, WorkerProcessId: null, State: "failed" }, "rejection stops and reclaims idle worker");
        var launchToken = test.Spec(rejected).Token;
        await Task.Delay(600);
        Require(test.Spec(rejected).Token == launchToken && View(manager, rejected).WorkerProcessId is null, "rejected configuration does not enter an automatic spawn loop");
        Require(!File.ReadAllText(Path.Combine(test.Root, "config", "workers", "run-intent.json")).Contains(rejected.InstanceId), "rejected start clears durable intent");
        Require(new DeviceLeaseStore(test.LeasePath).ReadActive().Value?.Count == 1, "only healthy account keeps its lease");
        Require(View(manager, healthy).WorkerProcessId == healthyPid && (await test.InfoAsync(healthy)).Running, "other account remains running with the same PID");
        await File.WriteAllTextAsync(Path.Combine(manager.PathsFor(rejected).LogDirectory, "allow-start"), "fixed");
        Require((await manager.StartAsync(rejected.InstanceId)).Success, "explicit start works after correction");
        Require(test.Spec(rejected).Token != launchToken && (await test.InfoAsync(rejected)).Running, "explicit retry uses a fresh authenticated worker");
    }

    public static async Task ManualCancellationAsync()
    {
        await using var test = new TestEnvironment();
        var account = test.Account(1);
        var manager = await test.ManagerAsync(new[] { account });
        Require((await manager.StartAsync(account.InstanceId)).Success, "start manual command test");
        var client = test.Client(account);
        var manual = client.CallAsync<OperationResult>("mock.block", Array.Empty<object?>());
        await UntilAsync(() => Directory.EnumerateFiles(test.Root, "manual-entered", SearchOption.AllDirectories).Any(), "manual request entered backend");
        var status = await client.CallAsync<WorkerStatus>(WorkerCommands.Status, Array.Empty<object?>()).WaitAsync(TimeSpan.FromSeconds(2));
        Require(status.IsRunning, "status remains responsive during long manual action");
        Require((await manager.StopAsync(account.InstanceId).WaitAsync(TimeSpan.FromSeconds(5))).Success, "Stop cancels long manual action");
        try { await manual; throw new InvalidOperationException("manual request should be cancelled"); }
        catch (OperationCanceledException) { }
        Require(Directory.EnumerateFiles(test.Root, "input-released", SearchOption.AllDirectories).Any(), "Stop releases input before waiting for the manual action to drain");
        Require(View(manager, account).WorkerProcessId is null, "manual cancellation allows graceful exit");
    }

    private static AccountProcessView View(WorkerProcessManager manager, AccountConfig account) => manager.Snapshot().Single(view => view.Config.InstanceId == account.InstanceId);

    private static async Task UntilAsync(Func<bool> condition, string reason, int timeoutMs = 10000)
    {
        var timer = Stopwatch.StartNew();
        while (!condition())
        {
            if (timer.ElapsedMilliseconds >= timeoutMs) throw new TimeoutException(reason);
            await Task.Delay(25);
        }
    }

    private static bool IsAlive(int pid)
    {
        try { using var process = Process.GetProcessById(pid); return !process.HasExited; }
        catch (ArgumentException) { return false; }
    }

    private static async Task KillAsync(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch (ArgumentException) { }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void RequireAll(IEnumerable<OperationResult> results, string reason) =>
        Require(results.All(result => result.Success), reason + ": " + string.Join("; ", results.Where(result => !result.Success).Select(result => result.Error)));

    private static async Task ExpectRpcFailureAsync(Func<Task<OperationResult>> action)
    {
        try { await action(); }
        catch (WorkerRpcException) { return; }
        throw new InvalidOperationException("Expected worker identity rejection");
    }

    private sealed record MockInfo(AccountConfig Account, WorkerServicePaths Paths, int Starts, bool Running);

    private sealed class MockBackend(WorkerLaunchSpec spec, string scenario) : IWorkerProcessBackend
    {
        private readonly AccountRuntimeManager _states = new(NoOpRoadhogLogger.Instance);
        private int _starts;
        private volatile bool _running;
        private volatile bool _initialized;

        public async Task InitializeAsync(CancellationToken cancellationToken)
        {
            Directory.CreateDirectory(spec.Paths.LogDirectory);
            _states.MarkStarting(spec.Account);
            _states.MarkStopped(spec.Account.AccountName);
            if (scenario is "slow-init" or "stuck-init")
            {
                await File.WriteAllTextAsync(Path.Combine(spec.Paths.LogDirectory, "init-entered"), "ready", cancellationToken);
                await Task.Delay(scenario == "slow-init" ? 350 : 30000, cancellationToken);
            }
            _initialized = true;
        }

        public WorkerStatus GetStatus() => new()
        {
            Authorized = scenario != "unauthorized", AuthorizationError = scenario == "unauthorized" ? "mock authorization denied" : null,
            IsRunning = _running, Snapshot = _states.Snapshot().SingleOrDefault()
        };

        public async Task<OperationResult> StartAsync(AccountConfig account, bool cleanupFirst, CancellationToken cancellationToken)
        {
            if (!_initialized) throw new InvalidOperationException("backend started before initialization");
            if (scenario == "reject-start" && account.AccountName.EndsWith("1", StringComparison.Ordinal)
                && !File.Exists(Path.Combine(spec.Paths.LogDirectory, "allow-start")))
                return OperationResult.Fail("mock binding mismatch");
            if (scenario == "idle-start" && account.AccountName.EndsWith("1", StringComparison.Ordinal))
                return OperationResult.Ok();
            if (scenario == "slow-start")
            {
                await File.WriteAllTextAsync(Path.Combine(spec.Paths.LogDirectory, "start-entered"), "ready", cancellationToken);
                await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
            }
            Interlocked.Increment(ref _starts);
            await File.WriteAllTextAsync(Path.Combine(spec.Paths.LogDirectory, "started"), _starts.ToString(), cancellationToken);
            _states.MarkStarting(account);
            _states.MarkRunning(account.AccountName, Environment.CurrentManagedThreadId);
            if (scenario == "player-info")
            {
                var first = account.AccountName.EndsWith("1", StringComparison.Ordinal);
                var provider = new FakeGameApi();
                provider.Player = provider.Player with { CharacterName = account.CharacterName,
                    Level = (ushort)(first ? 50 : 32), CharacterClass = first ? "精灵星" : "守护星" };
                var readers = new Roadhog.Infrastructure.Vmm.RoadhogSnapshotReaderFactory(provider, _states.CreatePlayerInfoObserver);
                await readers.Create(account, NoOpRoadhogLogger.Instance, cancellationToken).ReadPlayerAsync();
            }
            _running = true;
            return OperationResult.Ok();
        }

        public async Task<OperationResult> StopAsync(CancellationToken cancellationToken)
        {
            Require(File.Exists(Path.Combine(spec.Paths.LogDirectory, "input-released")), "host must release input before waiting for backend Stop");
            if (scenario == "stuck-stop") await Task.Delay(Timeout.Infinite, CancellationToken.None);
            _running = false;
            _states.MarkStopped(spec.Account.AccountName);
            return OperationResult.Ok();
        }

        public async Task<OperationResult> ReleaseInputAsync(CancellationToken cancellationToken)
        {
            await File.WriteAllTextAsync(Path.Combine(spec.Paths.LogDirectory, "input-released"), "released", cancellationToken);
            return OperationResult.Ok();
        }

        public Task<OperationResult<HardwareVerification>> VerifyHardwareAsync(CancellationToken cancellationToken) =>
            Task.FromResult(OperationResult<HardwareVerification>.Ok(new(spec.Account.CharacterName, spec.Account.HardwareKey, spec.Account.VmmDeviceName, true, HardwareVerificationSession.CurrentId)));

        public async Task<object?> InvokeAsync(string method, JsonElement[] arguments, IProgress<string> progress, CancellationToken cancellationToken)
        {
            if (method == "mock.info") return new MockInfo(spec.Account.Clone(), spec.Paths, Volatile.Read(ref _starts), _running);
            if (method == "mock.exit")
            {
                _ = Task.Run(async () => { await Task.Delay(arguments[0].GetInt32()); Environment.Exit(0); });
                return OperationResult.Ok();
            }
            if (method == "mock.block")
            {
                await File.WriteAllTextAsync(Path.Combine(spec.Paths.LogDirectory, "manual-entered"), "ready", cancellationToken);
                await Task.Delay(Timeout.Infinite, cancellationToken);
                return OperationResult.Ok();
            }
            throw new InvalidOperationException("Unknown mock method: " + method);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class TestEnvironment : IAsyncDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "RoadhogProcessTests", Guid.NewGuid().ToString("N"));
        public string LeasePath => Path.Combine(Root, "leases.json");
        public InMemoryRoadhogLogger Logger { get; } = new();
        private readonly List<WorkerProcessManager> _managers = new();
        private readonly Dictionary<int, DateTime> _children = new();
        private readonly string _hardwareScope = Guid.NewGuid().ToString("N");
        private readonly int _kmBoxPort = Random.Shared.Next(20000, 60000);

        public TestEnvironment() => Directory.CreateDirectory(Path.Combine(Root, "config"));

        public AccountConfig Account(int number) => new()
        {
            InstanceId = Guid.NewGuid().ToString("N"), AccountName = "mock-account-" + number, CharacterName = "角色" + number,
            HardwareVerificationSessionId = HardwareVerificationSession.CurrentId,
            HardwareKey = "mock-dma-" + number, VmmDeviceName = "fpga://devindex=" + number, TargetProcessName = "mock-game.exe",
            KmBox = new() { IpAddress = "127.0.0." + number, Port = _kmBoxPort, Mac = _hardwareScope + number }, AutoRecover = true
        };

        public async Task<WorkerProcessManager> ManagerAsync(IReadOnlyList<AccountConfig> accounts, string scenario = "normal")
        {
            var paths = new RoadhogServiceOptions
            {
                AccountConfigPath = Path.Combine(Root, "config", "accounts.json"),
                PathLibraryDirectory = Path.Combine(Root, "config", "paths"),
                ProfileLibraryDirectory = Path.Combine(Root, "config", "profiles"),
                RadarMapDirectory = Path.Combine(Root, "config", "radar-maps"),
                LogDirectory = Path.Combine(Root, "logs"), OwnerLicenseGrantPath = Path.Combine(Root, "unused-owner.json"),
                EnableLogging = false, LicenseHeartbeatInterval = TimeSpan.FromSeconds(73), LicenseHeartbeatRetryCount = 2,
                LicenseHeartbeatRetryDelay = TimeSpan.FromSeconds(4), LicenseRequestTimeout = TimeSpan.FromSeconds(9),
                AccountWorkerTickInterval = TimeSpan.FromMilliseconds(125), AccountWorkerStopTimeout = TimeSpan.FromSeconds(6), PollPlayerSnapshotInWorker = true
            };
            var manager = new WorkerProcessManager(paths, Logger, new()
            {
                ExecutablePath = Path.Combine(AppContext.BaseDirectory, "Roadhog.Tests.exe"),
                PrefixArguments = new[] { "--worker-scenario=" + scenario }, LeasePath = LeasePath,
                StartupTimeout = TimeSpan.FromSeconds(5), StopTimeout = TimeSpan.FromMilliseconds(900),
                PollInterval = TimeSpan.FromMilliseconds(50), RecoveryDelay = TimeSpan.FromMilliseconds(100)
            });
            _managers.Add(manager);
            await manager.InitializeAsync(accounts);
            return manager;
        }

        public async Task<AccountProcessView> RunningAsync(WorkerProcessManager manager, AccountConfig account)
        {
            await UntilAsync(() => View(manager, account) is { State: "running", WorkerProcessId: not null }, "account reaches running: " + account.AccountName);
            var view = View(manager, account);
            TrackChild(view.WorkerProcessId!.Value);
            return view;
        }

        public async Task DetachAsync(WorkerProcessManager manager)
        {
            foreach (var view in manager.Snapshot()) if (view.WorkerProcessId is { } pid) TrackChild(pid);
            _managers.Remove(manager);
            await manager.DisposeAsync();
        }

        private string InstanceDirectory(AccountConfig account) => Path.Combine(Root, "config", "workers", Guid.Parse(account.InstanceId).ToString("N"));
        public string ManifestPath(AccountConfig account) => Path.Combine(InstanceDirectory(account), "worker.json");
        public WorkerLaunchSpec Spec(AccountConfig account) => JsonSerializer.Deserialize<WorkerLaunchSpec>(File.ReadAllText(Path.Combine(InstanceDirectory(account), "launch.json")))!;
        public WorkerRpcClient Client(AccountConfig account) { var spec = Spec(account); return new(spec.PipeName, spec.Token); }
        public Task<MockInfo> InfoAsync(AccountConfig account) => Client(account).CallAsync<MockInfo>("mock.info", Array.Empty<object?>()).WaitAsync(TimeSpan.FromSeconds(3));

        public async Task<int> RunRejectedChildAsync(WorkerLaunchSpec spec)
        {
            var launchPath = Path.Combine(Root, "rejected-" + Guid.NewGuid().ToString("N") + ".json");
            await File.WriteAllTextAsync(launchPath, JsonSerializer.Serialize(spec));
            var start = new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "Roadhog.Tests.exe")) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
            start.ArgumentList.Add("--account-worker");
            start.ArgumentList.Add(launchPath);
            using var child = Process.Start(start)!;
            _children[child.Id] = child.StartTime.ToUniversalTime();
            try { await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)); return child.ExitCode; }
            finally { if (!child.HasExited) { child.Kill(true); await child.WaitForExitAsync(); } }
        }

        public async ValueTask DisposeAsync()
        {
            foreach (var manager in _managers)
            {
                foreach (var view in manager.Snapshot()) if (view.WorkerProcessId is { } pid) TrackChild(pid);
                try { await manager.StopAllAsync().WaitAsync(TimeSpan.FromSeconds(8)); }
                finally { await manager.DisposeAsync(); }
            }
            // Match PID and start time so a recycled PID can never target another process.
            foreach (var child in _children)
            {
                try
                {
                    using var process = Process.GetProcessById(child.Key);
                    if (!process.HasExited && process.StartTime.ToUniversalTime() == child.Value) await KillAsync(child.Key);
                }
                catch (ArgumentException) { }
            }
            Directory.Delete(Root, recursive: true);
        }

        private void TrackChild(int pid)
        {
            try { using var process = Process.GetProcessById(pid); _children[pid] = process.StartTime.ToUniversalTime(); }
            catch (ArgumentException) { }
        }
    }
}
