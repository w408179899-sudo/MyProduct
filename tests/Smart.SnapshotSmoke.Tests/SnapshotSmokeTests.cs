using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Reflection;
using System.Text.Json;
using Smart.Adapters.Dma;
using Smart.NativeSmoke;
using Smart.Runtime;
using Smart.SnapshotSmoke;
using Xunit;
namespace Smart.SnapshotSmoke.Tests;

public sealed class SnapshotSmokeTests
{
    private static byte[] Image()
    {
        var bytes = new byte[4096];
        BinaryPrimitives.WriteUInt16LittleEndian(bytes, 0x5A4D);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(60), 128);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(128), 0x4550);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(132), 0x8664);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(134), 3);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(148), 240);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(152), 0x20B);
        return bytes;
    }
    private static NativeSmokeCommand Command(int deadline = 2000) => new(Path.Combine(Path.GetTempPath(), "vmm.dll"),
        "unit://snapshot", 42, "target.exe", deadline, false, false);
    private sealed class Transport : IProcessMemoryTransport
    {
        private readonly byte[] _image = Image();
        public string DeviceId => "unit://snapshot";
        public string ConnectionId => "unit-connection";
        public bool CompleteZero, FailValid, ChangeIdentity;
        public int CloseFailures, ZeroAttempts, ValidAttempts, Closed;
        public ManualResetEventSlim? ReadEntered, ReadRelease;
        public IReadOnlyList<ProcessBinding> ListProcesses(string? requiredModule = null) => throw new InvalidOperationException("Process enumeration is forbidden.");
        public ProcessBinding GetProcess(int pid, string module) => new(pid, "target", ChangeIdentity && ZeroAttempts > 0 ? "changed" : "stable",
            0x100000, Convert.ToHexString(SHA256.HashData(_image)));
        public ImmutableArray<MemoryBlock> ReadBatch(int processId, IReadOnlyList<MemoryReadRequest> requests)
        {
            ReadEntered?.Set();
            if (ReadRelease is not null && !ReadRelease.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("Test read was not released.");
            Assert.Equal(42, processId); var request = Assert.Single(requests); Assert.Equal(4096, request.Length);
            if (request.Address == 0)
            {
                ZeroAttempts++;
                return [new(0, CompleteZero ? _image.ToImmutableArray() : [], CompleteZero)];
            }
            Assert.Equal((ulong)0x100000, request.Address); ValidAttempts++;
            return [new(request.Address, FailValid ? [] : _image.ToImmutableArray(), !FailValid)];
        }
        public void Dispose()
        {
            if (CloseFailures-- > 0) throw new IOException("Controlled native close failure.");
            Closed++;
        }
    }
    [Fact] public async Task ActualFailedCapturesHoldExactPublicationAndResetIsolatesItsGeneration()
    {
        var transport = new Transport(); var leases = new InputLeaseRegistry();
        await using var runner = new SnapshotSmokeRunner(() => transport, leases, transport.DeviceId);
        var report = await runner.RunAsync(Command());
        Assert.True(report.Passed, report.Error); Assert.Equal(new(true, true, true, true, true, true), report.Checks);
        Assert.True(report.InvalidAddressFailures >= 3); Assert.Equal(3, report.SuccessfulHeaderCaptures);
        Assert.Equal(3, report.Snapshots!.Publications); Assert.Equal(0, report.Snapshots.InFlight);
        Assert.Equal(1, report.FirstStamp!.Value.Version); Assert.Equal(2, report.RecoveryStamp!.Value.Version);
        Assert.NotEqual(report.FirstStamp.Value.Generation, report.ResetStamp!.Value.Generation); Assert.Equal(1, report.ResetStamp.Value.Version);
        Assert.Equal(transport.ZeroAttempts, report.InvalidAddressFailures); Assert.Equal(transport.ValidAttempts, report.SuccessfulHeaderCaptures);
        Assert.True(report.CleanupComplete); Assert.Equal(1, transport.Closed); Assert.Equal(0, report.Dma!.Active); Assert.Equal(0, report.Dma.Queued);
        using var reacquired = leases.Acquire("dma:" + transport.DeviceId);
    }
    [Fact] public async Task CompleteAddressZeroCannotProduceAFalsePassingResultOrZeroExitCode()
    {
        var transport = new Transport { CompleteZero = true }; using var output = new StringWriter();
        var result = await Program.ExecuteAsync(Command(), () => transport, new(), output);
        Assert.Equal(2, result);
        var report = JsonSerializer.Deserialize<SnapshotSmokeReport>(output.ToString())!;
        Assert.False(report.Passed); Assert.False(report.Checks.ColdWaitCancelled); Assert.True(report.UnexpectedCompleteZeroReads > 0);
        Assert.Equal(0, report.Snapshots!.Publications); Assert.True(report.CleanupComplete);
    }
    [Fact] public async Task NeverValidHeaderHitsTheDeadlineAndStillClosesNativeResources()
    {
        var transport = new Transport { FailValid = true };
        await using var runner = new SnapshotSmokeRunner(() => transport, new(), transport.DeviceId);
        var report = await runner.RunAsync(Command(500));
        Assert.False(report.Passed); Assert.Equal("Deadline", report.Outcome); Assert.True(report.OtherReadErrors > 0);
        Assert.False(report.Checks.FirstPublished); Assert.True(report.CleanupComplete); Assert.Equal(1, transport.Closed);
    }
    [Fact] public async Task AChangedProcessIdentityEndsTheRunWithoutPublishing()
    {
        var transport = new Transport { ChangeIdentity = true };
        await using var runner = new SnapshotSmokeRunner(() => transport, new(), transport.DeviceId);
        var report = await runner.RunAsync(Command());
        Assert.False(report.Passed); Assert.Equal("TargetChanged", report.Outcome); Assert.Equal(0, report.Snapshots!.Publications);
        Assert.True(report.CleanupComplete); Assert.Equal(1, transport.Closed);
    }
    [Fact] public async Task FailedCloseRetainsOwnershipUntilCleanupRetry()
    {
        var transport = new Transport { CloseFailures = 1 }; var leases = new InputLeaseRegistry();
        await using var runner = new SnapshotSmokeRunner(() => transport, leases, transport.DeviceId);
        var report = await runner.RunAsync(Command());
        Assert.False(report.Passed); Assert.Equal("CleanupFailed", report.Outcome); Assert.False(report.CleanupComplete);
        Assert.Throws<InvalidOperationException>(() => leases.Acquire("dma:" + transport.DeviceId));
        await runner.DisposeAsync(); using var reacquired = leases.Acquire("dma:" + transport.DeviceId);
    }
    [Fact] public async Task CancelledBeforeStartDoesNotConnectOrTakeALease()
    {
        using var stop = new CancellationTokenSource(); await stop.CancelAsync();
        await using var runner = new SnapshotSmokeRunner(() => throw new InvalidOperationException("Must not connect."), new(), "unit://snapshot");
        var report = await runner.RunAsync(Command(), stop.Token);
        Assert.False(report.Passed); Assert.Equal("Cancelled", report.Outcome); Assert.Null(report.Identity); Assert.True(report.CleanupComplete);
    }
    [Fact] public async Task SafeEntryAndArgumentsNeverOpenHardware()
    {
        Assert.Equal(0, await Program.Main([])); Assert.Equal(0, await Program.Main(["--help"]));
        Assert.Equal(2, await Program.Main(["--unknown"]));
        string[] args = ["--library", Command().Library, "--device", "unit://snapshot", "--pid", "42", "--module", "target.exe", "--duration-ms", "500"];
        Assert.Equal(500, SnapshotSmokeCommand.Parse(args).DurationMs);
        Assert.Throws<ArgumentException>(() => SnapshotSmokeCommand.Parse([.. args, "--zero-control"]));
        args[^1] = "499"; Assert.Throws<ArgumentException>(() => SnapshotSmokeCommand.Parse(args));
    }
    [Fact] public void ProviderCommandPreservesExplicitWorkerIsolation()
    {
        string[] args = ["--library", Command().Library, "--device", "unit://snapshot", "--pid", "42", "--module", "target.exe", "--duration-ms", "500"];
        Assert.False(SnapshotSmokeCommand.Parse(args).Isolated);
        var isolated = SnapshotSmokeCommand.Parse([.. args, "--isolated", "--diagnostic"]);
        Assert.True(isolated.Isolated); Assert.True(isolated.Diagnostic);
        Assert.DoesNotContain("--isolated", isolated.VmmArguments());
        Assert.Contains("--isolated", SnapshotSmokeCommand.Usage);
    }
    private static T PrivateField<T>(object value, string name) =>
        (T)value.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(value)!;
    [Fact] public async Task ThrowingIdentityCancellationIsReportedOnceAfterNativeAndLeaseCleanup()
    {
        var transport = new Transport { ChangeIdentity = true }; var leases = new InputLeaseRegistry();
        await using var runner = new SnapshotSmokeRunner(() => transport, leases, transport.DeviceId);
        using var callback = PrivateField<CancellationTokenSource>(runner, "_targetChanged").Token.Register(
            () => throw new InvalidOperationException("Controlled identity callback failure."));
        var report = await runner.RunAsync(Command());
        Assert.False(report.Passed); Assert.Contains("Controlled identity callback failure.", report.Error);
        Assert.True(report.CleanupComplete); Assert.Equal(1, transport.Closed);
        using var reacquired = leases.Acquire("dma:" + transport.DeviceId);
        await runner.DisposeAsync(); // The report already observed this callback error; cleanup retry must not rethrow it forever.
    }
    [Fact] public async Task ThrowingStopCancellationStillWaitsForOwnedNativeReadBeforeReleasingLease()
    {
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        var transport = new Transport { ReadEntered = entered, ReadRelease = release }; var leases = new InputLeaseRegistry();
        var runner = new SnapshotSmokeRunner(() => transport, leases, transport.DeviceId);
        using var callback = PrivateField<CancellationTokenSource>(runner, "_stop").Token.Register(
            () => throw new InvalidOperationException("Controlled stop callback failure."));
        var run = runner.RunAsync(Command()); Task? dispose = null;
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(3)));
            dispose = runner.DisposeAsync().AsTask();
            await Task.Delay(50);
            Assert.False(dispose.IsCompleted); Assert.Equal(0, transport.Closed);
            Assert.Throws<InvalidOperationException>(() => leases.Acquire("dma:" + transport.DeviceId));
        }
        finally { release.Set(); }
        var error = await Assert.ThrowsAsync<AggregateException>(() => dispose!.WaitAsync(TimeSpan.FromSeconds(3)));
        Assert.Contains(error.Flatten().InnerExceptions, failure => failure.Message == "Controlled stop callback failure.");
        Assert.True((await run).CleanupComplete); Assert.Equal(1, transport.Closed);
        using var reacquired = leases.Acquire("dma:" + transport.DeviceId);
        await runner.DisposeAsync();
    }
    [Fact] public async Task FaultedRunTaskCannotSkipRetryingAnOwnedNativeClose()
    {
        var transport = new Transport { CloseFailures = 1 }; var leases = new InputLeaseRegistry();
        var runner = new SnapshotSmokeRunner(() => transport, leases, transport.DeviceId);
        var report = await runner.RunAsync(Command()); Assert.False(report.CleanupComplete);
        typeof(SnapshotSmokeRunner).GetField("_run", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(runner,
            Task.FromException<SnapshotSmokeReport>(new InvalidOperationException("Controlled run failure.")));
        var error = await Assert.ThrowsAsync<AggregateException>(() => runner.DisposeAsync().AsTask());
        Assert.Contains(error.Flatten().InnerExceptions, failure => failure.Message == "Controlled run failure.");
        Assert.Equal(1, transport.Closed); using var reacquired = leases.Acquire("dma:" + transport.DeviceId);
        await runner.DisposeAsync();
    }
    [Fact] public async Task DisposedScopeRejectsAFirstRunBeforeConnecting()
    {
        var connected = false;
        var runner = new SnapshotSmokeRunner(() => { connected = true; return new Transport(); }, new(), "unit://snapshot");
        await runner.DisposeAsync();
        Assert.Throws<ObjectDisposedException>(() => { _ = runner.RunAsync(Command()); }); Assert.False(connected);
    }
    [Fact] public async Task DisposeCannotFinishWhileAFirstRunIsStillAcquiringItsLease()
    {
        var transport = new Transport(); var leases = new InputLeaseRegistry();
        var runner = new SnapshotSmokeRunner(() => transport, leases, transport.DeviceId);
        var registryGate = PrivateField<object>(leases, "_sync");
        var started = new TaskCompletionSource<SnapshotSmokeReport>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var disposing = new ManualResetEventSlim();
        using var disposeCompleted = new ManualResetEventSlim();
        var thread = new Thread(() =>
        {
            try { started.SetResult(runner.RunAsync(Command()).GetAwaiter().GetResult()); }
            catch (Exception ex) { started.SetException(ex); }
        }) { IsBackground = true };
        Task? dispose = null;
        Monitor.Enter(registryGate);
        try
        {
            thread.Start();
            Assert.True(SpinWait.SpinUntil(() => (thread.ThreadState & ThreadState.WaitSleepJoin) != 0, TimeSpan.FromSeconds(3)));
            dispose = Task.Run(async () =>
            {
                disposing.Set();
                try { await runner.DisposeAsync(); }
                finally { disposeCompleted.Set(); }
            });
            Assert.True(disposing.Wait(TimeSpan.FromSeconds(3)));
            // Run owns the scope-state gate until its task has been recorded. Dispose must wait for it.
            Assert.False(disposeCompleted.Wait(50)); Assert.Equal(0, transport.Closed);
        }
        finally { Monitor.Exit(registryGate); }
        await dispose!.WaitAsync(TimeSpan.FromSeconds(3));
        var report = await started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.True(report.CleanupComplete); Assert.InRange(transport.Closed, 0, 1);
        using var reacquired = leases.Acquire("dma:" + transport.DeviceId);
        Assert.Throws<ObjectDisposedException>(() => { _ = runner.RunAsync(Command()); });
    }
}
