using System.Collections.Immutable;
using System.Diagnostics;
using System.Security.Cryptography;
using Smart.Adapters.Dma;
using Smart.Data;
using Smart.Runtime;
namespace Smart.NativeSmoke;

public sealed record NativeSmokeIdentity(string DeviceId, string ConnectionId, int ProcessId, string Name, string ProcessIdentity, string Module,
    string ModuleBase, string ModuleFingerprint);
public sealed record NativeSmokeReport(bool Passed, string Outcome, string? Error, NativeSmokeIdentity? Identity,
    PeHeaderInfo? Pe, string? HeaderSha256, long IdentityChecks, long CompleteIterations, long FailedIterations,
    long ZeroReadRejected, long ZeroReadUnexpectedlyComplete, double InitializationMilliseconds,
    double MeasurementMilliseconds, double StopMilliseconds, bool CleanupComplete, DmaDispatcherMetrics? Dma);

// This diagnostic scope is intentionally separate from business snapshot publication.
public sealed class NativeSmokeRunner(Func<IProcessMemoryTransport> connect, InputLeaseRegistry leases,
    string device) : IAsyncDisposable
{
    private readonly SemaphoreSlim _cleanup = new(1, 1);
    private IDisposable? _lease;
    private IProcessMemoryTransport? _transport;
    private DmaDispatcher? _dispatcher;
    private bool _used, _cleanupComplete;
    public async Task<NativeSmokeReport> RunAsync(NativeSmokeCommand command, CancellationToken token = default)
    {
        if (_used) throw new InvalidOperationException("A native smoke scope is single-use.");
        _used = true;
        var initialization = Stopwatch.StartNew(); var measurement = new Stopwatch();
        NativeSmokeIdentity? identity = null; PeHeaderInfo? pe = null; string? hash = null, error = null;
        var outcome = "Completed";
        long checks = 0, complete = 0, failed = 0, zeroRejected = 0, zeroComplete = 0;
        try
        {
            token.ThrowIfCancellationRequested();
            _lease = leases.Acquire("dma:" + device.Trim());
            _transport = connect();
            if (!StringComparer.OrdinalIgnoreCase.Equals(_transport.DeviceId, device.Trim()))
                throw new InvalidOperationException("The connected device differs from the explicitly selected URI.");
            token.ThrowIfCancellationRequested();
            var initial = _transport.GetProcess(command.ProcessId, command.Module); checks++;
            if (initial.ProcessId != command.ProcessId || initial.ModuleBase == 0 || string.IsNullOrWhiteSpace(initial.ProcessIdentity))
                throw new InvalidDataException("The selected process/module has no valid binding.");
            identity = new(_transport.DeviceId, _transport.ConnectionId, initial.ProcessId, initial.Name, initial.ProcessIdentity, command.Module,
                "0x" + initial.ModuleBase.ToString("X16"), initial.ModuleFingerprint);
            _dispatcher = new(_transport, capacity: 4);
            var context = new CaptureContext(new(_transport.DeviceId, _transport.ConnectionId, "native-smoke",
                Guid.NewGuid().ToString("N"), initial.ProcessId, initial.ProcessIdentity, initial.ModuleIdentity), 1);
            initialization.Stop(); measurement.Start();
            while (measurement.ElapsedMilliseconds < command.DurationMs)
            {
                token.ThrowIfCancellationRequested();
                var current = _transport.GetProcess(command.ProcessId, command.Module); checks++;
                if (current.ProcessId != initial.ProcessId || current.ProcessIdentity != initial.ProcessIdentity || current.ModuleIdentity != initial.ModuleIdentity)
                    throw new InvalidOperationException("Selected process/module identity changed during the smoke run.");
                try
                {
                    var dos = await ReadCompleteAsync(context, initial.ModuleBase, PeHeaderValidation.DosBytes, token).ConfigureAwait(false);
                    var offset = PeHeaderValidation.ReadDosOffset(dos.AsSpan());
                    var peAddress = checked(initial.ModuleBase + (ulong)offset);
                    var prefix = await ReadCompleteAsync(context, peAddress, PeHeaderValidation.PePrefixBytes, token).ConfigureAwait(false);
                    var observed = PeHeaderValidation.ReadPePrefix(prefix.AsSpan(), offset);
                    MemoryReadRequest[] requests = command.ZeroControl
                        ? [new(initial.ModuleBase, PeHeaderValidation.FixedHeaderBytes), new(peAddress, observed.RequiredPeBytes), new(0, 64)]
                        : [new(initial.ModuleBase, PeHeaderValidation.FixedHeaderBytes), new(peAddress, observed.RequiredPeBytes)];
                    var blocks = await _dispatcher.ReadAsync(context, requests, token).ConfigureAwait(false);
                    if (blocks.Length != requests.Length) throw new IOException("The bounded header batch returned an unexpected block count.");
                    var header = RequireComplete(blocks[0], requests[0]);
                    var fullPe = RequireComplete(blocks[1], requests[1]);
                    PeHeaderValidation.ValidateCompletePe(fullPe.AsSpan(), observed);
                    var observedHash = Convert.ToHexString(SHA256.HashData(header.AsSpan()));
                    if (observedHash != initial.ModuleFingerprint || hash is not null && hash != observedHash)
                        throw new InvalidDataException("Fixed module-header fingerprint changed during the capture.");
                    pe = observed; hash = observedHash; complete++;
                    if (command.ZeroControl)
                    {
                        if (blocks[2].Address != 0) throw new IOException("The invalid-read control returned a different address.");
                        if (blocks[2].Complete && blocks[2].Bytes.Length == 64) zeroComplete++;
                        else zeroRejected++;
                    }
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                catch (Exception ex) when (ex is IOException or InvalidDataException)
                { failed++; error = ex.GetType().Name + ": " + ex.Message; }
                var remaining = command.DurationMs - measurement.ElapsedMilliseconds;
                if (remaining > 0) await Task.Delay((int)Math.Min(100, remaining), token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { outcome = "Cancelled"; }
        catch (Exception ex) { outcome = "Failed"; error = ex.GetType().Name + ": " + ex.Message; }
        finally { initialization.Stop(); measurement.Stop(); }
        var stop = Stopwatch.StartNew();
        try { await CleanupAsync().ConfigureAwait(false); }
        catch (Exception ex) { outcome = "CleanupFailed"; error = (error is null ? "" : error + "; ") + ex.Message; }
        stop.Stop();
        var passed = outcome == "Completed" && _cleanupComplete && complete > 0 && failed == 0 &&
            (!command.ZeroControl || zeroRejected > 0 && zeroComplete == 0);
        if (!passed && error is null && outcome == "Completed") error = "No complete stable PE capture, or invalid-address control unexpectedly succeeded.";
        return new(passed, outcome, error, identity, pe, hash, checks, complete, failed, zeroRejected, zeroComplete,
            initialization.Elapsed.TotalMilliseconds, measurement.Elapsed.TotalMilliseconds, stop.Elapsed.TotalMilliseconds,
            _cleanupComplete, _dispatcher?.Metrics);
    }
    private async Task<ImmutableArray<byte>> ReadCompleteAsync(CaptureContext context, ulong address, int length, CancellationToken token)
    {
        var blocks = await _dispatcher!.ReadAsync(context, [new(address, length)], token).ConfigureAwait(false);
        if (blocks.Length != 1) throw new IOException("The bounded header read returned an unexpected block count.");
        return RequireComplete(blocks[0], new(address, length));
    }
    private static ImmutableArray<byte> RequireComplete(MemoryBlock block, MemoryReadRequest request)
    {
        if (block.Address != request.Address || !block.Complete || block.Bytes.Length != request.Length)
            throw new IOException("A requested bounded header read was incomplete.");
        return block.Bytes;
    }
    private async Task CleanupAsync()
    {
        await _cleanup.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_cleanupComplete) return;
            if (_dispatcher is not null) await _dispatcher.DisposeAsync().ConfigureAwait(false);
            else _transport?.Dispose();
            _lease?.Dispose(); _lease = null; _cleanupComplete = true;
        }
        finally { _cleanup.Release(); }
    }
    public ValueTask DisposeAsync() => new(CleanupAsync());
}
