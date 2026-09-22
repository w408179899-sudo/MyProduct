using System.Diagnostics;
using System.Text.Json;
using Roadhog.Core.Accounts;
using Roadhog.Core.Common;
using Roadhog.Infrastructure.Hardware;
using Roadhog.Infrastructure.WorkerProcesses;

internal static class WorkerHostRobustnessTests
{
    public static async Task<int> RunChildAsync(string[] args)
    {
        var index = Array.IndexOf(args, "--host-robustness-worker");
        var spec = JsonSerializer.Deserialize<WorkerLaunchSpec>(await File.ReadAllTextAsync(args[index + 1]))!;
        var scenario = args.FirstOrDefault(a => a.StartsWith("--host-scenario="))?[16..] ?? "normal";
        return await new WorkerProcessHost(s =>
        {
            if (scenario == "factory-hang")
            {
                File.WriteAllText(Path.Combine(s.Paths.ClientRoot, "factory-entered"), "ready");
                new ManualResetEventSlim().Wait();
            }
            return new Backend(s.Paths.ClientRoot, blockNative: scenario == "native-hang");
        }, shutdownTimeout: TimeSpan.FromSeconds(1)).RunAsync(spec);
    }

    public static async Task AuthorizationLossReleasesBeforeNativeCompletesAsync()
    {
        using var test = new EnvironmentScope();
        using var lifetime = new CancellationTokenSource();
        var backend = new Backend(test.Root, blockNative: true) { FailFirstRelease = true };
        var terminated = false;
        var running = new WorkerProcessHost(_ => backend, _ => terminated = true, TimeSpan.FromSeconds(3)).RunAsync(test.Spec, lifetime.Token);
        using var nativeDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        Task<OperationResult>? native = null;
        try
        {
            await ReadyAsync(test.Client);
            native = test.Client.CallAsync<OperationResult>("block-native", [], nativeDeadline.Token);
            await backend.NativeEntered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            backend.Authorized = false;
            await backend.ReleaseEntered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Require(!backend.NativeCompletion.Task.IsCompleted, "authorization loss releases input before the uncooperative native call completes");
            var stops = Enumerable.Range(0, 4).Select(_ => test.Client.CallAsync<OperationResult>(WorkerCommands.Stop, [], nativeDeadline.Token)).ToArray();
            for (var i = 0; i < 8; i++)
            {
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(1));
                var status = await test.Client.CallAsync<WorkerStatus>(WorkerCommands.Status, [], deadline.Token);
                Require(!status.Authorized, "status remains responsive and authorization stays denied while native and stop calls are pending");
            }
            backend.NativeCompletion.TrySetResult();
            await native.WaitAsync(TimeSpan.FromSeconds(3));
            Require((await Task.WhenAll(stops)).All(r => r.Success), "concurrent stops complete despite an earlier input-release exception");
            await test.Client.CallAsync<OperationResult>(WorkerCommands.Shutdown, [], nativeDeadline.Token);
            Require(await running.WaitAsync(TimeSpan.FromSeconds(3)) == 0 && !terminated, "cooperative final shutdown does not use hard termination");
        }
        finally
        {
            backend.NativeCompletion.TrySetResult(); nativeDeadline.Cancel(); lifetime.Cancel();
            await running.WaitAsync(TimeSpan.FromSeconds(5));
            if (native is not null) try { await native; } catch { }
        }
    }

    public static async Task ShutdownSurvivesDisconnectedCallerAsync()
    {
        using var test = new EnvironmentScope();
        using var lifetime = new CancellationTokenSource();
        var backend = new Backend(test.Root) { BlockFirstRelease = true };
        var terminated = false;
        var running = new WorkerProcessHost(_ => backend, _ => terminated = true, TimeSpan.FromSeconds(3)).RunAsync(test.Spec, lifetime.Token);
        using var caller = new CancellationTokenSource();
        try
        {
            await ReadyAsync(test.Client);
            var shutdown = test.Client.CallAsync<OperationResult>(WorkerCommands.Shutdown, [], caller.Token);
            await backend.ReleaseEntered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            caller.Cancel();
            try { await shutdown; throw new InvalidOperationException("cancelled caller unexpectedly received a reply"); }
            catch (OperationCanceledException) { }
            backend.ReleaseCompletion.TrySetResult();
            Require(await running.WaitAsync(TimeSpan.FromSeconds(3)) == 0, "accepted shutdown completes after the requesting pipe disconnects");
            Require(backend.StopCalls > 0 && !terminated, "disconnection does not abort cleanup or require the watchdog");
            Require(!File.Exists(test.Spec.ManifestPath), "disconnected shutdown removes the worker's own manifest");
            Require(new DeviceLeaseStore(test.Spec.LeasePath).ReadActive().Value?.Count == 0, "disconnected shutdown releases its DMA lease");
        }
        finally
        {
            backend.ReleaseCompletion.TrySetResult(); lifetime.Cancel();
            await running.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    public static async Task ShutdownDeadlineTerminatesStuckWorkerAsync()
    {
        foreach (var scenario in new[] { "factory-hang", "native-hang" })
        {
            using var test = new EnvironmentScope();
            using var child = await test.StartChildAsync(scenario);
            using var clientLifetime = new CancellationTokenSource(TimeSpan.FromSeconds(12));
            Task<OperationResult>? native = null;
            try
            {
                await UntilAsync(() => File.Exists(test.Spec.ManifestPath), "worker publishes its manifest before native construction");
                if (scenario == "factory-hang")
                {
                    await UntilAsync(() => File.Exists(Path.Combine(test.Root, "factory-entered")), "factory reaches its blocking point");
                    var status = await test.Client.CallAsync<WorkerStatus>(WorkerCommands.Status, [], clientLifetime.Token);
                    Require(!status.InitializationComplete && status.ProcessId == child.Id, "blocked factory still exposes authenticated process status");
                }
                else
                {
                    await ReadyAsync(test.Client);
                    native = test.Client.CallAsync<OperationResult>("block-native", [], clientLifetime.Token);
                    await UntilAsync(() => File.Exists(Path.Combine(test.Root, "native-entered")), "native operation reaches its blocking point");
                }
                try { await test.Client.CallAsync<OperationResult>(WorkerCommands.Shutdown, [], clientLifetime.Token); }
                catch (Exception exception) when (exception is IOException or OperationCanceledException or WorkerRpcException) { }
                await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
                Require(child.ExitCode == 15, "worker watchdog ends an accepted shutdown even when native code or construction never returns");
                if (scenario == "native-hang") Require(File.Exists(Path.Combine(test.Root, "input-released")), "input release is attempted before hard termination");
                Require(new DeviceLeaseStore(test.Spec.LeasePath).ReadActive().Value?.Count == 0, "dead worker DMA lease is reclaimed by PID and start time");

                // Reuse both the instance and device identities: abandoned mutexes must permit a new process.
                using var replacement = await test.StartChildAsync("normal");
                try
                {
                    await ReadyAsync(test.Client);
                    var status = await test.Client.CallAsync<WorkerStatus>(WorkerCommands.Status, [], clientLifetime.Token);
                    Require(status.ProcessId == replacement.Id, "replacement acquires the original account and both KMBox locks");
                    await test.Client.CallAsync<OperationResult>(WorkerCommands.Shutdown, [], clientLifetime.Token);
                    await replacement.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
                    Require(replacement.ExitCode == 0, "replacement exits normally");
                }
                finally { await EnsureExitedAsync(replacement); }
            }
            finally
            {
                clientLifetime.Cancel(); await EnsureExitedAsync(child);
                if (native is not null) try { await native; } catch { }
            }
        }
    }

    private sealed class Backend(string root, bool blockNative = false) : IWorkerProcessBackend
    {
        public volatile bool Authorized = true;
        public bool FailFirstRelease { get; init; }
        public bool BlockFirstRelease { get; init; }
        public int StopCalls;
        private int _releaseCalls;
        public TaskCompletionSource NativeEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource NativeCompletion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseCompletion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public WorkerStatus GetStatus() => new() { Authorized = Authorized };
        public Task<OperationResult> StartAsync(AccountConfig account, bool cleanupFirst, CancellationToken cancellationToken) => Task.FromResult(OperationResult.Ok());
        public Task<OperationResult> StopAsync(CancellationToken cancellationToken) { Interlocked.Increment(ref StopCalls); return Task.FromResult(OperationResult.Ok()); }
        public async Task<OperationResult> ReleaseInputAsync(CancellationToken cancellationToken)
        {
            var count = Interlocked.Increment(ref _releaseCalls);
            File.WriteAllText(Path.Combine(root, "input-released"), count.ToString());
            ReleaseEntered.TrySetResult();
            if (count == 1 && FailFirstRelease) throw new IOException("simulated release failure");
            if (count == 1 && BlockFirstRelease) await ReleaseCompletion.Task.WaitAsync(cancellationToken);
            return OperationResult.Ok();
        }
        public Task<OperationResult<HardwareVerification>> VerifyHardwareAsync(CancellationToken cancellationToken) =>
            Task.FromResult(OperationResult<HardwareVerification>.Fail("not used by this test"));
        public async Task<object?> InvokeAsync(string method, JsonElement[] arguments, IProgress<string> progress, CancellationToken cancellationToken)
        {
            if (method != "block-native") throw new InvalidOperationException("unexpected test operation");
            File.WriteAllText(Path.Combine(root, "native-entered"), "ready"); NativeEntered.TrySetResult();
            if (blockNative) await NativeCompletion.Task; // Deliberately ignores cancellation, as a hung native read does.
            return OperationResult.Ok();
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private static async Task ReadyAsync(WorkerRpcClient client)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(6));
        while (true)
        {
            var status = await client.CallAsync<WorkerStatus>(WorkerCommands.Status, [], deadline.Token);
            if (status.InitializationComplete) { Require(status.Authorized, "test worker authorizes using its injected backend"); return; }
            await Task.Delay(20, deadline.Token);
        }
    }
    private static async Task UntilAsync(Func<bool> condition, string reason)
    {
        var elapsed = Stopwatch.StartNew();
        while (!condition())
        {
            if (elapsed.Elapsed > TimeSpan.FromSeconds(6)) throw new TimeoutException(reason);
            await Task.Delay(20);
        }
    }
    private static async Task EnsureExitedAsync(Process process)
    {
        if (!process.HasExited) process.Kill(entireProcessTree: true);
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

    private sealed class EnvironmentScope : IDisposable
    {
        public string Root { get; }
        public WorkerLaunchSpec Spec { get; }
        public WorkerRpcClient Client => new(Spec.PipeName, Spec.Token);
        public EnvironmentScope()
        {
            var repo = new DirectoryInfo(AppContext.BaseDirectory);
            while (repo is not null && !Directory.Exists(Path.Combine(repo.FullName, "Roadhog.Tests"))) repo = repo.Parent;
            Root = Path.Combine(repo?.FullName ?? Environment.CurrentDirectory, ".tmp", "multi-account-tests", "robustness", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
            var key = Guid.NewGuid().ToString("N");
            Spec = new WorkerLaunchSpec
            {
                Account = new AccountConfig { InstanceId = Guid.NewGuid().ToString("D"), AccountName = "host-robustness", HardwareKey = "mock-" + key,
                    VmmDeviceName = "fpga://devindex=" + Random.Shared.Next(10000, 90000),
                    KmBox = new() { IpAddress = "127.0.0.1", Port = Random.Shared.Next(10000, 65000), Mac = key[..8] } },
                Paths = new WorkerServicePaths { ClientRoot = Root, LogDirectory = Root },
                PipeName = "Roadhog.HostRobustness." + key, Token = key + key,
                ManifestPath = Path.Combine(Root, "worker.json"), LeasePath = Path.Combine(Root, "leases.json")
            };
        }
        public async Task<Process> StartChildAsync(string scenario)
        {
            var path = Path.Combine(Root, "launch.json");
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(Spec));
            var start = new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "Roadhog.Tests.exe"))
            { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
            start.ArgumentList.Add("--host-robustness-worker"); start.ArgumentList.Add(path); start.ArgumentList.Add("--host-scenario=" + scenario);
            return Process.Start(start) ?? throw new InvalidOperationException("test child did not start");
        }
        public void Dispose()
        {
            try { Directory.Delete(Root, recursive: true); } catch (IOException) { }
        }
    }
}
