using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Security.Cryptography;
using Smart.Adapters.Dma;
using Smart.NativeSmoke;
using Smart.Runtime;
using Xunit;
namespace Smart.NativeSmoke.Tests;

public sealed class NativeSmokeTests
{
    private static byte[] Image()
    {
        var header = new byte[4096];
        BinaryPrimitives.WriteUInt16LittleEndian(header, 0x5A4D);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(60), 128);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(128), 0x4550);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(132), 0x8664);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(134), 3);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(148), 240);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(152), 0x20B);
        return header;
    }
    [Fact] public void BoundedDosAndPeParserAcceptsACompleteHeader()
    {
        var image = Image(); var offset = PeHeaderValidation.ReadDosOffset(image.AsSpan(0, 64));
        var info = PeHeaderValidation.ReadPePrefix(image.AsSpan(offset, 26), offset);
        Assert.Equal(128, offset); Assert.Equal(3, info.Sections); Assert.Equal(264, info.RequiredPeBytes);
        PeHeaderValidation.ValidateCompletePe(image.AsSpan(offset, info.RequiredPeBytes), info);
        Assert.Throws<InvalidDataException>(() => PeHeaderValidation.ValidateCompletePe(image.AsSpan(offset, info.RequiredPeBytes - 1), info));
    }
    [Theory] [InlineData(-1)] [InlineData(0)] [InlineData(63)] [InlineData(65537)] [InlineData(int.MaxValue)]
    public void OutOfBoundsPePointerNeverBecomesAReadRequest(int offset)
    {
        var image = Image(); BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(60), offset);
        Assert.Throws<InvalidDataException>(() => PeHeaderValidation.ReadDosOffset(image.AsSpan(0, 64)));
    }
    [Theory] [InlineData(0, 0)] [InlineData(6, 0)] [InlineData(6, 97)] [InlineData(20, 95)] [InlineData(20, 4097)] [InlineData(24, 0)]
    public void InvalidPeSignatureSectionsOrOptionalSizeFailValidation(int relativeOffset, ushort value)
    {
        var image = Image(); BinaryPrimitives.WriteUInt16LittleEndian(image.AsSpan(128 + relativeOffset), value);
        Assert.Throws<InvalidDataException>(() => PeHeaderValidation.ReadPePrefix(image.AsSpan(128, 26), 128));
    }
    private static string[] Arguments() => ["--library", Path.Combine(Path.GetTempPath(), "vmm.dll"), "--device", "unit://native",
        "--pid", "42", "--module", "target.exe", "--duration-ms", "100"];
    [Fact] public void CommandRequiresExplicitSelectionAndOnlyAddsRequestedDiagnosticFlags()
    {
        var normal = NativeSmokeCommand.Parse(Arguments());
        Assert.Equal(new[] { "", "-device", "unit://native" }, normal.VmmArguments());
        var diagnostic = NativeSmokeCommand.Parse([.. Arguments(), "--diagnostic", "--zero-control"]);
        Assert.Contains("-printf", diagnostic.VmmArguments()); Assert.Contains("-v", diagnostic.VmmArguments()); Assert.True(diagnostic.ZeroControl);
        Assert.Throws<ArgumentException>(() => NativeSmokeCommand.Parse([]));
        Assert.Throws<ArgumentException>(() => NativeSmokeCommand.Parse([.. Arguments(), "--pid", "43"]));
        Assert.Throws<ArgumentException>(() => NativeSmokeCommand.Parse([.. Arguments(), "--unknown"]));
    }
    [Fact] public void IsolationIsExplicitAndDoesNotLeakIntoNativeArguments()
    {
        var direct = NativeSmokeCommand.Parse(Arguments());
        var isolated = NativeSmokeCommand.Parse([.. Arguments(), "--isolated"]);
        Assert.False(direct.Isolated); Assert.True(isolated.Isolated);
        Assert.Equal(direct.VmmArguments(), isolated.VmmArguments());
        Assert.False(new NativeSmokeCommand(direct.Library, direct.Device, direct.ProcessId, direct.Module,
            direct.DurationMs, direct.Diagnostic, direct.ZeroControl).Isolated);
        Assert.Contains("--isolated", NativeSmokeCommand.Usage);
        Assert.Throws<ArgumentException>(() => NativeSmokeCommand.Parse([.. Arguments(), "--isolated", "--isolated"]));
    }
    [Theory] [InlineData("--pid", "0")] [InlineData("--duration-ms", "99")] [InlineData("--duration-ms", "60001")]
    [InlineData("--library", "vmm.dll")] [InlineData("--device", "fpga")] [InlineData("--module", "")]
    public void MalformedSelectionIsRejectedBeforeAnyTransportIsCreated(string option, string value)
    {
        var arguments = Arguments(); arguments[Array.IndexOf(arguments, option) + 1] = value;
        Assert.Throws<ArgumentException>(() => NativeSmokeCommand.Parse(arguments));
    }
    private sealed class Transport : IProcessMemoryTransport
    {
        private readonly byte[] _image = Image();
        public string DeviceId => "unit://native";
        public string ConnectionId => "unit-connection";
        public bool Incomplete;
        public bool IncompleteBatchPe;
        public bool ZeroComplete;
        public readonly List<MemoryReadRequest[]> Batches = [];
        public int CloseFailures;
        public bool Closed;
        public IReadOnlyList<ProcessBinding> ListProcesses(string? requiredModule = null) => throw new InvalidOperationException("The smoke must inspect only the explicitly selected PID.");
        public ProcessBinding GetProcess(int pid, string module) => new(pid, "target", "stable-process", 0x100000,
            Convert.ToHexString(SHA256.HashData(_image)));
        public ImmutableArray<MemoryBlock> ReadBatch(int processId, IReadOnlyList<MemoryReadRequest> requests)
        {
            Batches.Add(requests.ToArray());
            return requests.Select(request => request.Address == 0 && ZeroComplete
                ? new MemoryBlock(0, new byte[request.Length].ToImmutableArray(), true)
                : request.Address == 0 || Incomplete || IncompleteBatchPe && requests.Count > 1 && request.Address == 0x100080
                    ? new MemoryBlock(request.Address, [], false)
                    : new MemoryBlock(request.Address, _image.AsSpan(checked((int)(request.Address - 0x100000)), request.Length).ToArray().ToImmutableArray(), true)).ToImmutableArray();
        }
        public void Dispose()
        {
            if (CloseFailures-- > 0) throw new IOException("Controlled native close failure.");
            Closed = true;
        }
    }
    [Fact] public async Task FakeTransportVerifiesHeadersBadReadControlAndReleasesLease()
    {
        var transport = new Transport(); var leases = new InputLeaseRegistry();
        await using var runner = new NativeSmokeRunner(() => transport, leases, transport.DeviceId);
        var report = await runner.RunAsync(NativeSmokeCommand.Parse([.. Arguments(), "--zero-control"]));
        Assert.True(report.Passed, report.Error); Assert.True(report.CompleteIterations > 0); Assert.True(report.ZeroReadRejected > 0);
        Assert.Contains(transport.Batches, batch => batch.Length == 3 && batch[0] == new MemoryReadRequest(0x100000, 4096)
            && batch[1] == new MemoryReadRequest(0x100080, 264) && batch[2] == new MemoryReadRequest(0, 64));
        Assert.DoesNotContain(transport.Batches, batch => batch.Length == 1 && batch[0].Address == 0);
        Assert.True(report.CleanupComplete); Assert.True(transport.Closed); Assert.Equal(0, report.Dma!.Queued); Assert.Equal(0, report.Dma.Active);
        using var reacquired = leases.Acquire("dma:" + transport.DeviceId);
    }
    [Fact] public async Task PartialPeInAnOtherwiseCompleteBatchFailsTheIteration()
    {
        var transport = new Transport { IncompleteBatchPe = true };
        await using var runner = new NativeSmokeRunner(() => transport, new(), transport.DeviceId);
        var report = await runner.RunAsync(NativeSmokeCommand.Parse([.. Arguments(), "--zero-control"]));
        Assert.False(report.Passed); Assert.Equal(0, report.CompleteIterations); Assert.True(report.FailedIterations > 0);
        Assert.Contains(transport.Batches, batch => batch.Length == 3); Assert.True(report.CleanupComplete);
    }
    [Fact] public async Task UnexpectedlyCompleteInvalidReadCannotPassTheControlGate()
    {
        var transport = new Transport { ZeroComplete = true };
        await using var runner = new NativeSmokeRunner(() => transport, new(), transport.DeviceId);
        var report = await runner.RunAsync(NativeSmokeCommand.Parse([.. Arguments(), "--zero-control"]));
        Assert.False(report.Passed); Assert.True(report.CompleteIterations > 0); Assert.True(report.ZeroReadUnexpectedlyComplete > 0);
        Assert.Equal(0, report.ZeroReadRejected); Assert.True(report.CleanupComplete);
    }
    [Fact] public async Task IncompleteRequiredReadsFailWithoutBypassingCleanup()
    {
        var transport = new Transport { Incomplete = true };
        await using var runner = new NativeSmokeRunner(() => transport, new(), transport.DeviceId);
        var report = await runner.RunAsync(NativeSmokeCommand.Parse(Arguments()));
        Assert.False(report.Passed); Assert.True(report.FailedIterations > 0); Assert.True(report.CleanupComplete); Assert.True(transport.Closed);
    }
    [Fact] public async Task FailedNativeCloseKeepsItsLeaseUntilRetryCompletes()
    {
        var transport = new Transport { CloseFailures = 1 }; var leases = new InputLeaseRegistry();
        await using var runner = new NativeSmokeRunner(() => transport, leases, transport.DeviceId);
        var report = await runner.RunAsync(NativeSmokeCommand.Parse(Arguments()));
        Assert.False(report.Passed); Assert.Equal("CleanupFailed", report.Outcome); Assert.False(report.CleanupComplete);
        Assert.Throws<InvalidOperationException>(() => leases.Acquire("dma:" + transport.DeviceId));
        await runner.DisposeAsync();
        using var reacquired = leases.Acquire("dma:" + transport.DeviceId);
    }
    [Fact] public async Task HelpAndMalformedArgumentsReturnWithoutOpeningAnyNativeLibrary()
    {
        Assert.Equal(0, await Smart.NativeSmoke.Program.Main([]));
        Assert.Equal(0, await Smart.NativeSmoke.Program.Main(["--help"]));
        Assert.Equal(2, await Smart.NativeSmoke.Program.Main(["--unknown"]));
    }
}
