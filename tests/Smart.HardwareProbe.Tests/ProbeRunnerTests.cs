using System.Buffers.Binary;
using System.Collections.Immutable;
using Microsoft.Extensions.Time.Testing;
using Smart.Adapters.Dma;
using Smart.HardwareProbe;
using Smart.ProbeProtocol;
using Smart.Runtime;
using Xunit;

namespace Smart.HardwareProbe.Tests;

public sealed class ProbeRunnerTests
{
    private static ProbeManifest Manifest() => new(1, Guid.NewGuid(), 3456, "Smart.ProbeTarget",
        DateTimeOffset.UtcNow, "Smart.ProbeTarget.exe", "0x100000", ProbeMemoryProtocol.Size, 8, "mixed", 10, 2);
    // Two consecutive injected failures invoke the provider's 25 ms + 50 ms retry backoff.
    private static readonly ProbeRunOptions Options = new(TimeSpan.FromMilliseconds(300), TimeSpan.FromMilliseconds(5), TimeSpan.FromMilliseconds(125));
    private sealed class Transport(ProbeManifest manifest) : IProcessMemoryTransport
    {
        public string DeviceId => "fpga://probe-test";
        public string ConnectionId => "test-connection";
        public Func<int, ImmutableArray<MemoryBlock>>? Read;
        public Func<ProcessBinding>? Inspect;
        public TaskCompletionSource FirstRead { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Reads, Inspections, Disposals, Enumerations;
        public bool FailDispose;
        public ProcessBinding Binding { get; } = new(manifest.ProcessId, manifest.ProcessName + ".exe", "process-identity", 0x400000, "module-fingerprint");
        public IReadOnlyList<ProcessBinding> ListProcesses(string? requiredModule = null)
        { Enumerations++; throw new InvalidOperationException("Automatic process selection must never occur."); }
        public ProcessBinding GetProcess(int pid, string module)
        {
            Assert.Equal(manifest.ProcessId, pid); Assert.Equal(manifest.MainModuleName, module);
            Interlocked.Increment(ref Inspections);
            return Inspect?.Invoke() ?? Binding;
        }
        public ImmutableArray<MemoryBlock> ReadBatch(int pid, IReadOnlyList<MemoryReadRequest> requests)
        {
            Assert.Equal(manifest.ProcessId, pid);
            Assert.Single(requests); Assert.Equal(manifest.GetAddress(), requests[0].Address); Assert.Equal(ProbeMemoryProtocol.Size, requests[0].Length);
            var index = Interlocked.Increment(ref Reads);
            FirstRead.TrySetResult();
            return Read?.Invoke(index) ?? Frame(manifest, index);
        }
        public void Dispose()
        {
            if (FailDispose) throw new IOException("Native close failed.");
            Interlocked.Increment(ref Disposals);
        }
    }
    private static ImmutableArray<MemoryBlock> Frame(ProbeManifest manifest, long counter, bool torn = false, Guid? session = null, bool badChecksum = false)
    {
        var bytes = new byte[ProbeMemoryProtocol.Size];
        ProbeMemoryProtocol.Encode(bytes, session ?? manifest.SessionId, (counter + 1) * 2, counter, DateTimeOffset.UtcNow);
        if (torn) BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(ProbeMemoryProtocol.EndSequenceOffset), (counter + 1) * 2 + 2);
        if (badChecksum) bytes[ProbeMemoryProtocol.ChecksumOffset] ^= 0xff;
        return [new(manifest.GetAddress(), ImmutableArray.Create(bytes), true)];
    }
    private static async Task<ProbeReport> FinishAsync(Task<ProbeReport> run, FakeTimeProvider clock)
    {
        for (var step = 0; step < 1000 && !run.IsCompleted; step++)
        {
            clock.Advance(TimeSpan.FromMilliseconds(5));
            await Task.Delay(1);
        }
        return await run.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task ValidChangesAndZeroPublishWhileTornAndFailedReadsKeepTheOfficialValue()
    {
        var manifest = Manifest(); var clock = new FakeTimeProvider(); var transport = new Transport(manifest);
        transport.Read = i => i switch
        {
            1 => Frame(manifest, 7),
            2 => throw new IOException("temporary read failure"),
            3 => Frame(manifest, 8, torn: true),
            4 => Frame(manifest, 8),
            _ => Frame(manifest, i + 4)
        };
        await using var runner = new HardwareProbeRunner(() => transport, new(), transport.DeviceId, clock);
        var run = runner.RunAsync(manifest, Options);
        await transport.FirstRead.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var report = await FinishAsync(run, clock);
        Assert.True(report.Passed, report.Error);
        Assert.True(report.Publications >= 2); Assert.True(report.CounterChanges >= 1); Assert.True(report.ZeroPublications >= 1);
        Assert.Equal(7, report.FirstCounter); Assert.True(report.LastCounter >= 8);
        Assert.True(report.OfficialReads > report.Publications); Assert.Equal(1, report.TornReads); Assert.True(report.ReadErrors > 0);
        Assert.True(report.CleanupComplete); Assert.Equal(0, report.Dma!.Active); Assert.Equal(0, report.Dma.Queued);
        Assert.Equal(0, transport.Enumerations); Assert.Equal(1, transport.Disposals);
    }

    [Fact]
    public async Task InitialActivityFollowedByPermanentReadFailureFailsAcceptanceWhileKeepingTheLastOfficialZero()
    {
        var manifest = Manifest(); var clock = new FakeTimeProvider(); var transport = new Transport(manifest)
        { Read = i => i <= 2 ? Frame(manifest, i + 6) : throw new IOException("device stopped responding") };
        await using var runner = new HardwareProbeRunner(() => transport, new(), transport.DeviceId, clock);
        var run = runner.RunAsync(manifest, Options);
        await transport.FirstRead.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var report = await FinishAsync(run, clock);
        Assert.False(report.Passed); Assert.Equal("Completed", report.Outcome);
        Assert.Equal(2, report.Publications); Assert.Equal(1, report.CounterChanges);
        Assert.Equal(8, report.LastCounter); Assert.Equal(0, report.LastValue); Assert.Equal(1, report.ZeroPublications);
        Assert.True(report.ReadErrors > 0); Assert.True(report.OfficialReads > 2);
        Assert.True(report.MaximumProgressGapMilliseconds > report.ProgressGapLimitMilliseconds);
        Assert.True(report.ProgressCoverageSufficient); Assert.Contains("activity stopped", report.Error);
    }

    [Fact]
    public async Task ShortObservationCannotPassEvenWhenSeveralValidFramesArePublished()
    {
        var manifest = Manifest(); var clock = new FakeTimeProvider(); var transport = new Transport(manifest);
        await using var runner = new HardwareProbeRunner(() => transport, new(), transport.DeviceId, clock);
        var run = runner.RunAsync(manifest, Options with { Duration = TimeSpan.FromMilliseconds(100) });
        await transport.FirstRead.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var report = await FinishAsync(run, clock);
        Assert.False(report.Passed); Assert.True(report.CounterChanges > 0); Assert.False(report.ProgressCoverageSufficient);
        Assert.Equal(250, report.MinimumObservationMilliseconds); Assert.Contains("two configured", report.Error);
    }

    [Fact]
    public async Task ColdStartFailuresEndAtOverallDeadlineWithoutFabricatedZeroOrSuccess()
    {
        var manifest = Manifest(); var clock = new FakeTimeProvider(); var transport = new Transport(manifest)
        { Read = _ => throw new IOException("all reads fail") };
        await using var runner = new HardwareProbeRunner(() => transport, new(), transport.DeviceId, clock);
        var run = runner.RunAsync(manifest, Options);
        await transport.FirstRead.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var report = await FinishAsync(run, clock);
        Assert.False(report.Passed); Assert.True(report.SoftDeadlineReached); Assert.True(report.ReadErrors > 0);
        Assert.Equal(0, report.Publications); Assert.Equal(0, report.OfficialReads); Assert.Equal(0, report.ZeroPublications);
        Assert.Null(report.LastCounter); Assert.Null(report.LastValue); Assert.True(report.CleanupComplete);
    }

    [Fact]
    public async Task RepeatedIdenticalFrameDoesNotProveTargetActivity()
    {
        var manifest = Manifest(); var clock = new FakeTimeProvider(); var transport = new Transport(manifest)
        { Read = _ => Frame(manifest, 8) };
        await using var runner = new HardwareProbeRunner(() => transport, new(), transport.DeviceId, clock);
        var run = runner.RunAsync(manifest, Options);
        await transport.FirstRead.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var report = await FinishAsync(run, clock);
        Assert.False(report.Passed); Assert.Equal(1, report.Publications); Assert.Equal(0, report.CounterChanges);
        Assert.Equal(1, report.ZeroPublications); Assert.Contains("activity", report.Error);
    }

    [Fact]
    public async Task ChecksumAndBackwardSequenceAreValidationRejectionsWithoutRollingBackPublication()
    {
        var manifest = Manifest(); var clock = new FakeTimeProvider(); var transport = new Transport(manifest);
        transport.Read = i => i switch
        {
            1 => Frame(manifest, 8),
            2 => Frame(manifest, 9),
            3 => Frame(manifest, 10, badChecksum: true),
            4 => Frame(manifest, 7),
            _ => Frame(manifest, i + 6)
        };
        await using var runner = new HardwareProbeRunner(() => transport, new(), transport.DeviceId, clock);
        var run = runner.RunAsync(manifest, Options);
        await transport.FirstRead.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var report = await FinishAsync(run, clock);
        Assert.True(report.Passed, report.Error); Assert.True(report.Publications >= 2); Assert.True(report.LastCounter >= 9);
        Assert.True(report.ValidationRejected >= 2); Assert.Equal(0, report.TornReads);
    }

    [Fact]
    public async Task CorruptSessionBytesWithInvalidChecksumHoldAndRecoverWithoutEndingTheSession()
    {
        var manifest = Manifest(); var clock = new FakeTimeProvider(); var transport = new Transport(manifest);
        var corrupt = Frame(manifest, 8)[0].Bytes.ToArray();
        corrupt[ProbeMemoryProtocol.PayloadOffset] ^= 1;
        transport.Read = i => i switch
        {
            1 => Frame(manifest, 7),
            2 => [new(manifest.GetAddress(), ImmutableArray.Create(corrupt), true)],
            _ => Frame(manifest, i + 5)
        };
        await using var runner = new HardwareProbeRunner(() => transport, new(), transport.DeviceId, clock);
        var run = runner.RunAsync(manifest, Options);
        await transport.FirstRead.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var report = await FinishAsync(run, clock);
        Assert.True(report.Passed, report.Error); Assert.Equal("Completed", report.Outcome);
        Assert.True(report.Publications >= 2); Assert.Equal(1, report.ValidationRejected);
        Assert.True(report.LastCounter >= 8); Assert.True(report.ZeroPublications >= 1);
    }

    [Fact]
    public async Task IncompleteBlocksCannotPublishEvenWhenTheBufferContainsAValidFrame()
    {
        var manifest = Manifest(); var clock = new FakeTimeProvider(); var transport = new Transport(manifest)
        { Read = _ => [Frame(manifest, 8)[0] with { Complete = false }] };
        await using var runner = new HardwareProbeRunner(() => transport, new(), transport.DeviceId, clock);
        var run = runner.RunAsync(manifest, Options);
        await transport.FirstRead.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var report = await FinishAsync(run, clock);
        Assert.False(report.Passed); Assert.Equal(0, report.Publications); Assert.True(report.ReadErrors > 0);
        Assert.Equal(0, report.ZeroPublications); Assert.True(report.CleanupComplete);
    }

    [Fact]
    public async Task ExistingPhysicalLeaseRejectsTheProbeBeforeNativeInitialization()
    {
        var manifest = Manifest(); var transport = new Transport(manifest); var leases = new InputLeaseRegistry();
        using var existing = leases.Acquire("dma:" + transport.DeviceId);
        var created = 0;
        await using var runner = new HardwareProbeRunner(() => { created++; return transport; }, leases, transport.DeviceId);
        var report = await runner.RunAsync(manifest, Options);
        Assert.False(report.Passed); Assert.Equal("Failed", report.Outcome); Assert.Equal(0, created);
        Assert.Throws<InvalidOperationException>(() => leases.Acquire("dma:" + transport.DeviceId));
        Assert.Equal(0, transport.Reads); Assert.Equal(0, transport.Disposals);
    }

    [Fact]
    public async Task ExternalCancellationClosesTheScopeAndDoesNotReportAcceptance()
    {
        var manifest = Manifest(); var clock = new FakeTimeProvider(); var transport = new Transport(manifest);
        using var stop = new CancellationTokenSource();
        transport.Read = i => { if (i == 3) stop.Cancel(); return Frame(manifest, i); };
        await using var runner = new HardwareProbeRunner(() => transport, new(), transport.DeviceId, clock);
        var run = runner.RunAsync(manifest, Options, stop.Token);
        await transport.FirstRead.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var report = await FinishAsync(run, clock);
        Assert.False(report.Passed); Assert.Equal("Cancelled", report.Outcome); Assert.True(report.CleanupComplete);
        Assert.Equal(1, transport.Disposals); Assert.Equal(0, report.Dma!.Active); Assert.Equal(0, report.Dma.Queued);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ProcessOrFixtureSessionReplacementEndsTheScopeBeforePublishingReplacement(bool processReplacement)
    {
        var manifest = Manifest(); var clock = new FakeTimeProvider(); var transport = new Transport(manifest);
        if (processReplacement) transport.Inspect = () => transport.Reads >= 2
            ? transport.Binding with { ProcessIdentity = "new-process" } : transport.Binding;
        else transport.Read = i => Frame(manifest, i, session: i >= 2 ? Guid.NewGuid() : manifest.SessionId);
        await using var runner = new HardwareProbeRunner(() => transport, new(), transport.DeviceId, clock);
        var run = runner.RunAsync(manifest, Options);
        await transport.FirstRead.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var report = await FinishAsync(run, clock);
        Assert.False(report.Passed); Assert.Equal("TargetChanged", report.Outcome); Assert.Equal(1, report.Publications);
        Assert.Equal(0, report.CounterChanges); Assert.True(report.CleanupComplete);
    }

    [Fact]
    public async Task ManifestNameMismatchRejectsBindingBeforeAnyMemoryRead()
    {
        var manifest = Manifest(); var transport = new Transport(manifest);
        transport.Inspect = () => transport.Binding with { Name = "another-program.exe" };
        await using var runner = new HardwareProbeRunner(() => transport, new(), transport.DeviceId);
        var report = await runner.RunAsync(manifest, Options).WaitAsync(TimeSpan.FromSeconds(2));
        Assert.False(report.Passed); Assert.Equal("Failed", report.Outcome); Assert.Equal(0, transport.Reads);
        Assert.Equal(0, transport.Enumerations); Assert.Equal(1, transport.Disposals);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task InitializationAndBindingShareDeadlineAndKeepLeaseWhileNativeCallIsBlocked(bool blockInitialization)
    {
        var manifest = Manifest(); var clock = new FakeTimeProvider(); var transport = new Transport(manifest);
        var leases = new InputLeaseRegistry(); using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        IProcessMemoryTransport Connect()
        {
            if (blockInitialization) { entered.TrySetResult(); release.Wait(); }
            return transport;
        }
        if (!blockInitialization) transport.Inspect = () => { entered.TrySetResult(); release.Wait(); return transport.Binding; };
        await using var runner = new HardwareProbeRunner(Connect, leases, transport.DeviceId, clock);
        var run = runner.RunAsync(manifest, Options);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            clock.Advance(TimeSpan.FromSeconds(1));
            Assert.False(run.IsCompleted);
            Assert.Throws<InvalidOperationException>(() => leases.Acquire("dma:" + transport.DeviceId));
        }
        finally { release.Set(); }
        var report = await run.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.False(report.Passed); Assert.True(report.SoftDeadlineReached); Assert.Equal(0, transport.Reads);
        Assert.Equal(1, transport.Disposals); Assert.True(report.CleanupComplete);
        using var reacquired = leases.Acquire("dma:" + transport.DeviceId);
    }

    [Fact]
    public async Task NativeCloseFailureRetainsTheScopeAndLeaseUntilExplicitDisposeRetrySucceeds()
    {
        var manifest = Manifest(); var clock = new FakeTimeProvider(); var transport = new Transport(manifest) { FailDispose = true };
        var leases = new InputLeaseRegistry();
        var runner = new HardwareProbeRunner(() => transport, leases, transport.DeviceId, clock);
        try
        {
            var run = runner.RunAsync(manifest, Options);
            await transport.FirstRead.Task.WaitAsync(TimeSpan.FromSeconds(2));
            var report = await FinishAsync(run, clock);
            Assert.False(report.Passed); Assert.Equal("CleanupFailed", report.Outcome); Assert.False(report.CleanupComplete);
            Assert.Throws<InvalidOperationException>(() => leases.Acquire("dma:" + transport.DeviceId));
        }
        finally { transport.FailDispose = false; await runner.DisposeAsync(); }
        Assert.Equal(1, transport.Disposals);
        using var reacquired = leases.Acquire("dma:" + transport.DeviceId);
    }

    [Fact]
    public void CommandRequiresExplicitDeviceLibraryManifestAndBoundedDuration()
    {
        Assert.Throws<ArgumentException>(() => ProbeCommand.Parse([]));
        var root = Path.GetFullPath(Path.GetTempPath());
        string[] Valid(string duration) => ["--library", Path.Combine(root, "vmm.dll"), "--device", "fpga://devindex=5",
            "--manifest", Path.Combine(root, "fixture.json"), "--duration-ms", duration];
        var command = ProbeCommand.Parse(Valid("1000"));
        Assert.Equal("fpga://devindex=5", command.DeviceUri);
        Assert.Throws<ArgumentOutOfRangeException>(() => ProbeCommand.Parse(Valid("0")));
        Assert.Throws<ArgumentOutOfRangeException>(() => ProbeCommand.Parse(Valid("3600001")));
        Assert.Throws<ArgumentException>(() => ProbeCommand.Parse([.. Valid("1000"), "--pid", "123"]));
        Assert.Throws<ArgumentException>(() => ProbeCommand.Parse([.. Valid("1000"), "--device", "fpga://different"]));
        Assert.Throws<ArgumentOutOfRangeException>(() => ProbeCommand.Parse([.. Valid("1000"), "--max-progress-gap-ms", "0"]));
        Assert.Equal(TimeSpan.FromMilliseconds(500), ProbeCommand.Parse([.. Valid("1000"), "--max-progress-gap-ms", "500"]).Run.MaximumProgressGap);
        Assert.Equal(TimeSpan.FromMilliseconds(262), new ProbeRunOptions(TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(5)).ResolveProgressGap(Manifest()));
    }
}
