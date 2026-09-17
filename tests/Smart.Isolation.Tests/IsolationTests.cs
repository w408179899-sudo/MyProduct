using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using Smart.Adapters.Dma;
using Smart.Runtime;
using Xunit;

namespace Smart.Isolation.Tests;

public sealed class WindowsFactAttribute : FactAttribute
{
    public WindowsFactAttribute() { if (!OperatingSystem.IsWindows() || !Environment.Is64BitProcess) Skip = "Windows x64 process/job isolation test."; }
}
public sealed class WindowsTheoryAttribute : TheoryAttribute
{
    public WindowsTheoryAttribute() { if (!OperatingSystem.IsWindows() || !Environment.Is64BitProcess) Skip = "Windows x64 process/job isolation test."; }
}

public sealed class IsolationTests : IDisposable
{
    private readonly string? _previousRoot = Environment.GetEnvironmentVariable("DOTNET_ROOT_X64");
    public IsolationTests()
    {
        // testhost.exe is an apphost; its child must use the same private SDK runtime as this test process.
        Environment.SetEnvironmentVariable("DOTNET_ROOT_X64", Path.GetFullPath(Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(), "..", "..", "..")));
    }
    public void Dispose() => Environment.SetEnvironmentVariable("DOTNET_ROOT_X64", _previousRoot);
    private static string WorkerPath => Path.Combine(AppContext.BaseDirectory, "fake-worker", "Smart.Dma.Worker.Fake.exe");
    private static string WriteEvidence(string name, object evidence)
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "isolation-evidence"); Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, name);
        File.WriteAllText(path, JsonSerializer.Serialize(evidence, new JsonSerializerOptions { WriteIndented = true }));
        return path;
    }
    private sealed class Scope : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "Smart.Isolation." + Guid.NewGuid().ToString("N"));
        public string Device { get; } = "fixture://" + Guid.NewGuid().ToString("N");
        public Scope() { Directory.CreateDirectory(Root); Assert.True(File.Exists(WorkerPath), "The independently built fake worker was not copied into the test output."); }
        public VmmWorkerOptions Options(int startup = 5000, int operation = 1000, int shutdown = 500) => new()
        { WorkerPath = IsolationTests.WorkerPath, LeaseDirectory = Root, StartupTimeoutMs = startup, OperationTimeoutMs = operation, ShutdownTimeoutMs = shutdown };
        public IsolatedVmmTransport Open(string mode = "normal", string? device = null, VmmWorkerOptions? options = null, CancellationToken cancellation = default)
        {
            device ??= Device;
            return new(WorkerPath, device, VmmTransport.CreateArguments(device, ["-fake-mode", mode]), options ?? Options(), cancellation);
        }
        public void AssertHeld(string? device = null) => Assert.Throws<InvalidOperationException>(() => new InputLeaseRegistry(Root).Acquire("dma:" + (device ?? Device)));
        public void AssertReleased(string? device = null) { using var lease = new InputLeaseRegistry(Root).Acquire("dma:" + (device ?? Device)); }
        public string Marker(int pid, string phase) => Path.Combine(Root, $"fake-worker-{pid}.{phase}");
        public int[] StartedPids() => Directory.GetFiles(Root, "fake-worker-*.started").Select(path =>
            int.Parse(Path.GetFileNameWithoutExtension(path)["fake-worker-".Length..], CultureInfo.InvariantCulture)).ToArray();
        public void Dispose()
        {
            // Only PIDs written by this scope's own fake factory may be cleaned up after a failing assertion.
            foreach (var pid in StartedPids())
            {
                try
                {
                    using var process = Process.GetProcessById(pid);
                    if (process.ProcessName == "Smart.Dma.Worker.Fake" && !process.HasExited)
                    { process.Kill(); Assert.True(process.WaitForExit(5000)); }
                }
                catch (ArgumentException) { }
            }
        }
    }
    private static bool HasExited(int pid)
    {
        try { using var process = Process.GetProcessById(pid); return process.HasExited; }
        catch (ArgumentException) { return true; }
    }
    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var elapsed = Stopwatch.StartNew();
        while (!condition())
        { Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(5), "Controlled process condition did not arrive."); await Task.Delay(10); }
    }
    private static void AssertData(IsolatedVmmTransport transport)
    {
        var block = Assert.Single(transport.ReadBatch(42, [new(0x1200, 4)]));
        Assert.True(block.Complete); Assert.Equal(new byte[] { 0, 1, 2, 3 }, block.Bytes.ToArray());
    }

    [WindowsFact] public void BatchRoundTripPreservesBindingsAndExposesOnlyCompleteBlocks()
    {
        using var scope = new Scope(); using var transport = scope.Open();
        Assert.NotEqual(Environment.ProcessId, transport.WorkerProcessId); Assert.True(transport.IsConnected); scope.AssertHeld();
        var process = Assert.Single(transport.ListProcesses("fixture.exe"));
        Assert.Equal(42, process.ProcessId); Assert.Equal(process, transport.GetProcess(42, "fixture.exe"));
        var blocks = transport.ReadBatch(42, [new(0x1200, 4), new(0, 4), new(0xBAD, 4)]);
        Assert.Equal(3, blocks.Length); Assert.True(blocks[0].Complete); Assert.Equal(new byte[] { 0, 1, 2, 3 }, blocks[0].Bytes.ToArray());
        Assert.All(blocks.Skip(1), block => { Assert.False(block.Complete); Assert.Empty(block.Bytes); });
        var pid = transport.WorkerProcessId; transport.Dispose(); Assert.True(HasExited(pid)); scope.AssertReleased();
    }
    [WindowsFact] public void OrdinaryReadErrorAndMissingProcessDoNotRetireTheConnection()
    {
        using var scope = new Scope(); using var transport = scope.Open("read-error-once");
        var notifications = 0; transport.Disconnected += () => Interlocked.Increment(ref notifications);
        var error = Assert.Throws<IOException>(() => transport.ReadBatch(42, [new(0x1200, 4)]));
        Assert.Contains("Controlled ordinary read error", error.Message); Assert.True(transport.IsConnected);
        Assert.Throws<TargetProcessUnavailableException>(() => transport.GetProcess(404, "missing.exe"));
        AssertData(transport); Assert.Equal(0, Volatile.Read(ref notifications)); scope.AssertHeld();
    }
    [WindowsFact] public async Task HungInitializationIsKilledBeforeItsLeaseCanBeReacquired()
    {
        using var scope = new Scope(); var elapsed = Stopwatch.StartNew();
        var opening = Task.Run(() => scope.Open("hang-init", options: scope.Options(startup: 2000)));
        await WaitUntilAsync(() => scope.StartedPids().Length == 1);
        var pid = Assert.Single(scope.StartedPids()); Assert.False(opening.IsCompleted); scope.AssertHeld();
        var error = await Assert.ThrowsAsync<TimeoutException>(async () => await opening.WaitAsync(TimeSpan.FromSeconds(8)));
        Assert.Contains("startup deadline", error.Message); Assert.IsAssignableFrom<OperationCanceledException>(error.InnerException);
        Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(8)); Assert.True(HasExited(pid)); scope.AssertReleased();
        using var reconnected = scope.Open(); AssertData(reconnected);
    }
    [WindowsFact] public async Task CancellingDuringInitializationRemainsCallerCancellationAndClosesTheWorker()
    {
        using var scope = new Scope(); using var cancellation = new CancellationTokenSource();
        var opening = Task.Run(() => scope.Open("hang-init", cancellation: cancellation.Token));
        await WaitUntilAsync(() => scope.StartedPids().Length == 1);
        var pid = Assert.Single(scope.StartedPids()); Assert.False(opening.IsCompleted); scope.AssertHeld();
        var stopping = Stopwatch.StartNew(); await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await opening.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.True(stopping.Elapsed < TimeSpan.FromSeconds(5)); Assert.True(HasExited(pid)); scope.AssertReleased();
        using var reconnected = scope.Open(); AssertData(reconnected);
    }
    [WindowsTheory]
    [InlineData("dll-missing", nameof(DllNotFoundException))]
    [InlineData("bad-image", nameof(BadImageFormatException))]
    [InlineData("entry-missing", nameof(EntryPointNotFoundException))]
    public void NativeLoadFailuresAreConfigurationErrorsAndReleaseTheFailedWorker(string mode, string expectedType)
    {
        using var scope = new Scope();
        var error = Assert.Throws<MemoryWorkerConfigurationException>(() => scope.Open(mode));
        Assert.Contains(expectedType, error.Message);
        var pid = Assert.Single(scope.StartedPids()); Assert.True(HasExited(pid)); scope.AssertReleased();
        using var reconnected = scope.Open(); AssertData(reconnected);
    }
    [WindowsFact] public async Task HungReadIsKilledWithOneDisconnectAndAllowsASeparateNewConnection()
    {
        using var scope = new Scope(); using var transport = scope.Open("hang-read");
        var pid = transport.WorkerProcessId; var connection = transport.ConnectionId; var notifications = 0;
        transport.Disconnected += () => Interlocked.Increment(ref notifications);
        var elapsed = Stopwatch.StartNew(); var reading = Task.Run(() => transport.ReadBatch(42, [new(0x1200, 4)]));
        await WaitUntilAsync(() => File.Exists(scope.Marker(pid, "read")));
        Assert.False(reading.IsCompleted); scope.AssertHeld();
        await Assert.ThrowsAsync<MemoryConnectionLostException>(async () => await reading.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(5)); Assert.True(HasExited(pid)); Assert.False(transport.IsConnected);
        Assert.Throws<MemoryConnectionLostException>(() => transport.GetProcess(42, "fixture.exe"));
        Assert.Equal(1, Volatile.Read(ref notifications)); scope.AssertReleased();
        using var reconnected = scope.Open(); Assert.NotEqual(connection, reconnected.ConnectionId); AssertData(reconnected);
        transport.Dispose(); Assert.Equal(1, Volatile.Read(ref notifications)); Assert.True(reconnected.IsConnected);
    }
    [WindowsFact] public async Task HungNativeCloseKeepsTheLeaseUntilTheWorkerIsDead()
    {
        using var scope = new Scope(); var transport = scope.Open("hang-close", options: scope.Options(shutdown: 1000));
        var pid = transport.WorkerProcessId; var elapsed = Stopwatch.StartNew(); var stopping = Task.Run(transport.Dispose);
        await WaitUntilAsync(() => File.Exists(scope.Marker(pid, "close")));
        Assert.False(stopping.IsCompleted); scope.AssertHeld();
        await stopping.WaitAsync(TimeSpan.FromSeconds(5)); Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(5));
        Assert.True(HasExited(pid)); scope.AssertReleased(); transport.Dispose();
    }
    [WindowsFact] public async Task ACrashingWorkerNotifiesOnceAndCannotKillAnotherDeviceWorker()
    {
        using var scope = new Scope(); using var failed = scope.Open("crash-read");
        var otherDevice = scope.Device + "-other"; using var healthy = scope.Open(device: otherDevice);
        var notifications = 0;
        failed.Disconnected += () => throw new InvalidOperationException("Controlled observer failure.");
        failed.Disconnected += () => Interlocked.Increment(ref notifications);
        var pid = failed.WorkerProcessId;
        Assert.Throws<MemoryConnectionLostException>(() => failed.ReadBatch(42, [new(0x1200, 4)]));
        await WaitUntilAsync(() => Volatile.Read(ref notifications) == 1);
        Assert.True(HasExited(pid)); Assert.False(failed.IsConnected);
        var releaseClock = Stopwatch.StartNew(); Exception? releaseError = null; List<object> releaseAttempts = [];
        for (var attempt = 0; attempt < 21; attempt++)
        {
            try { scope.AssertReleased(); releaseError = null; break; }
            catch (InvalidOperationException error)
            {
                releaseError = error;
                releaseAttempts.Add(new { ElapsedMilliseconds = releaseClock.Elapsed.TotalMilliseconds, error.HResult,
                    FullException = error.ToString(), InnerHResult = error.InnerException?.HResult });
                await Task.Delay(50);
            }
        }
        File.WriteAllText(Path.Combine(scope.Root, "crash-lease-release-diagnostic.txt"),
            "Milliseconds=" + releaseClock.Elapsed.TotalMilliseconds.ToString(CultureInfo.InvariantCulture) + "; Error=" + releaseError);
        WriteEvidence("crash-lease-release.json", new { FailedWorkerPid = pid, HealthyWorkerPid = healthy.WorkerProcessId,
            ExitedBeforeReacquire = HasExited(pid), NoDisposeBeforeReacquire = true, RetryMilliseconds = releaseClock.Elapsed.TotalMilliseconds,
            Attempts = releaseAttempts, Released = releaseError is null, LeaseDirectory = scope.Root });
        Assert.Null(releaseError);
        AssertData(healthy); Assert.True(healthy.IsConnected); scope.AssertHeld(otherDevice);
        failed.Dispose(); Assert.Equal(1, Volatile.Read(ref notifications)); AssertData(healthy);
    }
    [WindowsFact] public void TheSamePhysicalEndpointCannotBeOpenedByTwoWorkerProcesses()
    {
        using var scope = new Scope(); using var owner = scope.Open();
        Assert.ThrowsAny<IOException>(() => scope.Open());
        Assert.True(owner.IsConnected); AssertData(owner); scope.AssertHeld();
        owner.Dispose(); scope.AssertReleased(); using var replacement = scope.Open(); AssertData(replacement);
    }
    [WindowsFact] public void MissingWorkerFailsWithoutFallingBackToTheNativeLibrary()
    {
        using var scope = new Scope(); var options = scope.Options() with { WorkerPath = Path.Combine(scope.Root, "missing-worker.exe") };
        var error = Assert.Throws<MemoryWorkerConfigurationException>(() => scope.Open(options: options));
        Assert.Contains("no in-process fallback", error.Message); Assert.Empty(scope.StartedPids()); scope.AssertReleased();
    }
    [WindowsFact] public void MissingNativeDeviceArgumentIsRejectedBeforeLaunchingAWorker()
    {
        using var scope = new Scope();
        Assert.Throws<ArgumentException>(() => new IsolatedVmmTransport(WorkerPath, scope.Device, ["", "-printf"], scope.Options()));
        Assert.Empty(scope.StartedPids()); scope.AssertReleased();
    }
    [WindowsFact] public void ADeviceArgumentDifferentFromTheLeasedEndpointIsRejectedBeforeLaunching()
    {
        using var scope = new Scope();
        Assert.Throws<ArgumentException>(() => new IsolatedVmmTransport(WorkerPath, scope.Device,
            VmmTransport.CreateArguments(scope.Device + "-wrong"), scope.Options()));
        Assert.Empty(scope.StartedPids()); scope.AssertReleased();
    }
    [WindowsFact] public void CancelledBeforeStartDoesNotLaunchAWorkerOrHoldAnEndpoint()
    {
        using var scope = new Scope(); using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => scope.Open(cancellation: cancellation.Token));
        Assert.Empty(scope.StartedPids()); scope.AssertReleased();
    }
    [WindowsFact] public async Task KillingTheParentProcessClosesItsPrivateJobAndReleasesTheChildLease()
    {
        using var scope = new Scope(); var ready = Path.Combine(scope.Root, "parent-ready.txt");
        using var parent = new Process { StartInfo = new(WorkerPath) { UseShellExecute = false, CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden, RedirectStandardOutput = true, RedirectStandardError = true } };
        foreach (var argument in new[] { "--parent-death-host", scope.Root, ready, scope.Device }) parent.StartInfo.ArgumentList.Add(argument);
        Assert.True(parent.Start());
        var output = parent.StandardOutput.ReadToEndAsync(); var error = parent.StandardError.ReadToEndAsync();
        try
        {
            await WaitUntilAsync(() => File.Exists(ready) && new FileInfo(ready).Length > 0);
            var child = int.Parse(await File.ReadAllTextAsync(ready), CultureInfo.InvariantCulture);
            Assert.NotEqual(parent.Id, child); Assert.False(HasExited(child)); scope.AssertHeld();
            parent.Kill(entireProcessTree: false); // The Windows job, not this test's process-tree kill, must remove the worker.
            await parent.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            await WaitUntilAsync(() => HasExited(child)); scope.AssertReleased();
            using var replacement = scope.Open(); AssertData(replacement);
        }
        finally
        {
            if (!parent.HasExited) { parent.Kill(entireProcessTree: false); await parent.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)); }
            await Task.WhenAll(output, error).WaitAsync(TimeSpan.FromSeconds(5));
        }
    }
    [WindowsFact] public void FiveSecondBatchIpcSampleRecordsParentChildCpuAllocationAndCleanup()
    {
        using var scope = new Scope(); using var transport = scope.Open("performance");
        using var parent = Process.GetCurrentProcess(); using var worker = Process.GetProcessById(transport.WorkerProcessId);
        var requests = Enumerable.Range(0, 128).Select(index => new MemoryReadRequest(0x100000 + (ulong)index * 64, 64)).ToArray();
        for (var i = 0; i < 32; i++) transport.ReadBatch(42, requests);
        List<double> latencies = new(65536); long completed = 0, bytes = 0;
        parent.Refresh(); worker.Refresh();
        var parentCpu = parent.TotalProcessorTime; var childCpu = worker.TotalProcessorTime;
        var allocation = GC.GetTotalAllocatedBytes(precise: true); var measurement = Stopwatch.StartNew();
        do
        {
            var started = Stopwatch.GetTimestamp();
            var blocks = transport.ReadBatch(42, requests);
            latencies.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            Assert.Equal(128, blocks.Length);
            foreach (var block in blocks) { Assert.True(block.Complete); Assert.Equal(64, block.Bytes.Length); bytes += block.Bytes.Length; }
            completed++;
        } while (measurement.Elapsed < TimeSpan.FromSeconds(5));
        measurement.Stop(); var allocated = GC.GetTotalAllocatedBytes(precise: true) - allocation;
        parent.Refresh(); worker.Refresh();
        var parentCpuMilliseconds = (parent.TotalProcessorTime - parentCpu).TotalMilliseconds;
        var childCpuMilliseconds = (worker.TotalProcessorTime - childCpu).TotalMilliseconds;
        latencies.Sort(); var stopping = Stopwatch.StartNew(); transport.Dispose(); stopping.Stop();
        Assert.True(HasExited(worker.Id)); scope.AssertReleased();
        WriteEvidence("ipc-batch-sample.json", new
        {
            Kind = "Fake-worker IPC sample; not DMA hardware throughput", AtUtc = DateTimeOffset.UtcNow,
            SegmentsPerBatch = 128, BytesPerSegment = 64, WarmupBatches = 32, CompletedBatches = completed, CompletedBytes = bytes,
            ElapsedMilliseconds = measurement.Elapsed.TotalMilliseconds, ParentCpuMilliseconds = parentCpuMilliseconds,
            ChildCpuMilliseconds = childCpuMilliseconds, ParentAllocatedBytes = allocated,
            P50Milliseconds = latencies[(int)Math.Ceiling(latencies.Count * .50) - 1], P99Milliseconds = latencies[(int)Math.Ceiling(latencies.Count * .99) - 1],
            BatchesPerSecond = completed / measurement.Elapsed.TotalSeconds, StopMilliseconds = stopping.Elapsed.TotalMilliseconds,
            WorkerExited = HasExited(worker.Id), LeaseReleased = true, ReadMarkerIoDisabled = true
        });
    }
}
