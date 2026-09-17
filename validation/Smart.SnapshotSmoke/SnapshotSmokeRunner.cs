using System.Diagnostics;
using Smart.Adapters.Dma;
using Smart.Contracts;
using Smart.Data;
using Smart.NativeSmoke;
using Smart.Runtime;
namespace Smart.SnapshotSmoke;

public sealed record ContractChecks(bool ColdWaitCancelled, bool FirstPublished, bool FailedReadHeldExactSnapshot,
    bool RecoveryPublished, bool ResetWaitCancelled, bool NewGenerationPublished);
public sealed record SnapshotSmokeReport(bool Passed, string Outcome, string? Error, NativeSmokeIdentity? Identity,
    ContractChecks Checks, long InvalidAddressFailures, long SuccessfulHeaderCaptures, long UnexpectedCompleteZeroReads,
    long OtherReadErrors, SnapshotStamp? FirstStamp, SnapshotStamp? RecoveryStamp, SnapshotStamp? ResetStamp,
    string? HeaderSha256, double ElapsedMilliseconds, double StopMilliseconds, bool CleanupComplete,
    SnapshotMetrics? Snapshots, DmaDispatcherMetrics? Dma);

// One diagnostic scope, with provider-owned snapshots and no business-layer read fallback.
public sealed class SnapshotSmokeRunner(Func<IProcessMemoryTransport> connect, InputLeaseRegistry leases, string device) : IAsyncDisposable
{
    private readonly object _stateGate = new();
    private readonly HashSet<Task> _reportedControlFailures = [];
    private readonly SemaphoreSlim _cleanupGate = new(1, 1);
    private readonly CancellationTokenSource _stop = new();
    private readonly CancellationTokenSource _targetChanged = new();
    private Task _identityCancellation = Task.CompletedTask;
    private string? _identityError;
    private Task<SnapshotSmokeReport>? _run;
    private Task? _stopCancellation;
    private IDisposable? _lease;
    private IProcessMemoryTransport? _transport;
    private DmaDispatcher? _dispatcher;
    private SnapshotSession? _session;
    private bool _disposed, _clean, _sessionClosed, _transportClosed;

    public Task<SnapshotSmokeReport> RunAsync(NativeSmokeCommand command, CancellationToken cancellation = default)
    {
        if (command.DurationMs is < 500 or > 60000) throw new ArgumentOutOfRangeException(nameof(command));
        lock (_stateGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_run is not null) throw new InvalidOperationException("A snapshot smoke scope is single-use.");
            return _run = RunCoreAsync(command, cancellation);
        }
    }
    private async Task<SnapshotSmokeReport> RunCoreAsync(NativeSmokeCommand command, CancellationToken external)
    {
        var elapsed = Stopwatch.StartNew();
        using var deadline = new CancellationTokenSource();
        using var deadlineStop = new CancellationTokenSource();
        var deadlineTask = DeadlineAsync(command.DurationMs, deadline, deadlineStop.Token);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(external, deadline.Token, _stop.Token, _targetChanged.Token);
        var token = linked.Token;
        NativeSmokeIdentity? identity = null; HeaderReader? raw = null;
        PublishedSnapshot<HeaderSnapshot>? first = null, recovered = null, reset = null;
        var coldCancelled = false; var held = false; var resetCancelled = false;
        string? error = null; var outcome = "Completed";
        try
        {
            token.ThrowIfCancellationRequested();
            _lease = leases.Acquire("dma:" + device.Trim());
            _transport = await Task.Run(connect, CancellationToken.None).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            Require(StringComparer.OrdinalIgnoreCase.Equals(_transport.DeviceId, device.Trim()), "The connected device differs from the selected URI.");
            var binding = await Task.Run(() => _transport.GetProcess(command.ProcessId, command.Module), CancellationToken.None).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            Require(binding.ProcessId == command.ProcessId && binding.ModuleBase != 0 && !string.IsNullOrWhiteSpace(binding.ProcessIdentity), "The selected process/module binding is invalid.");
            identity = new(_transport.DeviceId, _transport.ConnectionId, binding.ProcessId, binding.Name, binding.ProcessIdentity,
                command.Module, "0x" + binding.ModuleBase.ToString("X16"), binding.ModuleFingerprint);
            _dispatcher = new(new BoundTransport(_transport, binding, command.Module), capacity: 4);
            raw = new(_dispatcher, binding, message =>
            {
                if (Interlocked.CompareExchange(ref _identityError, message, null) is null)
                    _identityCancellation = _targetChanged.CancelAsync();
            });
            var catalog = new SnapshotCatalog();
            var channel = catalog.Register<HeaderSnapshot, NoPartition, HeaderSnapshot>("smoke.pe-header", raw,
                new ReplaceMerger<HeaderSnapshot>(_ => true), SnapshotMergePolicy.Replace, TimeSpan.FromMilliseconds(25));
            catalog.Seal();
            var sessionIdentity = new SessionIdentity(_transport.DeviceId, _transport.ConnectionId, "snapshot-smoke",
                Guid.NewGuid().ToString("N"), binding.ProcessId, binding.ProcessIdentity, binding.ModuleIdentity);
            _session = new SnapshotProvider(catalog).OpenSession(sessionIdentity);

            // Raw counters plus an idle provider prove real failed captures completed, rather than a cached return.
            await ExpectEmptyWaitAsync(channel, raw, token).ConfigureAwait(false);
            coldCancelled = true;
            raw.UseValidAddress = true;
            first = await _session.Reader.ReadAsync(channel, NoPartition.Value, token).ConfigureAwait(false);
            Require(first.Stamp.Version == 1 && raw.ValidSuccesses > 0 && _session.Metrics.Publications == 1,
                "The first valid capture did not become the first official publication.");
            await DrainCapturesAsync(token).ConfigureAwait(false);

            raw.UseValidAddress = false;
            var failedBefore = raw.InvalidFailures; var publishedBefore = _session.Metrics.Publications;
            do
            {
                var current = await _session.Reader.ReadAsync(channel, NoPartition.Value, token).ConfigureAwait(false);
                Require(current == first, "A failed read changed the official value, stamp or capture time.");
                await Task.Delay(10, token).ConfigureAwait(false);
            } while (raw.InvalidFailures <= failedBefore || _session.Metrics.InFlight != 0);
            Require(_session.Metrics.Publications == publishedBefore, "A failed capture created a publication.");
            held = true;

            raw.UseValidAddress = true;
            var successBefore = raw.ValidSuccesses;
            recovered = await _session.Reader.WaitForChangeAsync(channel, NoPartition.Value, first.Stamp, token).ConfigureAwait(false);
            Require(recovered.Value == first.Value && recovered.Stamp.Generation == first.Stamp.Generation &&
                recovered.Stamp.Version == first.Stamp.Version + 1 && raw.ValidSuccesses > successBefore,
                "Recovery did not publish a newly successful PE capture in the same generation.");
            await DrainCapturesAsync(token).ConfigureAwait(false);

            raw.UseValidAddress = false;
            _session.Reset(sessionIdentity);
            await ExpectEmptyWaitAsync(channel, raw, token).ConfigureAwait(false);
            resetCancelled = true;
            raw.UseValidAddress = true;
            reset = await _session.Reader.ReadAsync(channel, NoPartition.Value, token).ConfigureAwait(false);
            Require(reset.Stamp.Generation != first.Stamp.Generation && reset.Stamp.Version == 1 && reset.Value == first.Value,
                "Reset leaked the prior generation or failed to publish a new generation's first value.");
            await DrainCapturesAsync(token).ConfigureAwait(false);
            Require(raw.UnexpectedZero == 0 && raw.OtherErrors == 0, "Unexpected complete address-zero reads or other read errors occurred.");
            token.ThrowIfCancellationRequested();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            outcome = _identityError is not null ? "TargetChanged" : deadline.IsCancellationRequested ? "Deadline" : "Cancelled";
            error = _identityError ?? "The scenario ended before every contract assertion completed.";
        }
        catch (Exception ex) { outcome = "Failed"; error = ex.GetType().Name + ": " + ex.Message; }
        finally
        {
            List<Exception> controlFailures = [];
            await ObserveControlTaskAsync(deadlineStop.CancelAsync(), controlFailures).ConfigureAwait(false);
            await ObserveControlTaskAsync(deadlineTask, controlFailures).ConfigureAwait(false);
            if (controlFailures.Count != 0)
            {
                outcome = "Failed";
                error = AppendError(error, new AggregateException("Deadline cancellation callbacks failed.", controlFailures));
            }
        }
        elapsed.Stop();
        var stop = Stopwatch.StartNew();
        try { await CleanupAsync().ConfigureAwait(false); }
        catch (Exception ex) { outcome = "CleanupFailed"; error = AppendError(error, ex); }
        stop.Stop();
        var checks = new ContractChecks(coldCancelled, first is not null, held, recovered is not null, resetCancelled, reset is not null);
        var passed = outcome == "Completed" && _clean && checks == new ContractChecks(true, true, true, true, true, true);
        return new(passed, outcome, error, identity, checks, raw?.InvalidFailures ?? 0, raw?.ValidSuccesses ?? 0,
            raw?.UnexpectedZero ?? 0, raw?.OtherErrors ?? 0, first?.Stamp, recovered?.Stamp, reset?.Stamp, first?.Value.Sha256,
            elapsed.Elapsed.TotalMilliseconds, stop.Elapsed.TotalMilliseconds, _clean, _session?.Metrics, _dispatcher?.Metrics);
    }
    private async Task ExpectEmptyWaitAsync(SnapshotChannel<HeaderSnapshot, NoPartition> channel, HeaderReader raw, CancellationToken token)
    {
        var failedBefore = raw.InvalidFailures; var publications = _session!.Metrics.Publications;
        using var waitCancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        var waiting = _session.Reader.ReadAsync(channel, NoPartition.Value, waitCancellation.Token).AsTask();
        var timedOut = false;
        try { await waiting.WaitAsync(TimeSpan.FromMilliseconds(150), token).ConfigureAwait(false); }
        catch (TimeoutException) { timedOut = true; }
        finally { await waitCancellation.CancelAsync().ConfigureAwait(false); }
        Require(timedOut, "A generation without a valid first capture returned a publication.");
        try { await waiting.WaitAsync(token).ConfigureAwait(false); throw new InvalidOperationException("The cold-start read ignored cancellation."); }
        catch (OperationCanceledException) when (waitCancellation.IsCancellationRequested && !token.IsCancellationRequested) { }
        await DrainCapturesAsync(token).ConfigureAwait(false);
        Require(raw.InvalidFailures > failedBefore && _session.Metrics.Publications == publications,
            "No completed invalid-address failure was observed, or it published a value.");
    }
    private async Task DrainCapturesAsync(CancellationToken token)
    {
        while (_session!.Metrics.InFlight != 0) await Task.Delay(5, token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
    }
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
    private static async Task DeadlineAsync(int milliseconds, CancellationTokenSource deadline, CancellationToken stop)
    {
        try { await Task.Delay(milliseconds, stop).ConfigureAwait(false); }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { return; }
        await deadline.CancelAsync().ConfigureAwait(false);
    }
    private async Task CleanupAsync()
    {
        await _cleanupGate.WaitAsync().ConfigureAwait(false);
        List<Exception> failures = [];
        try
        {
            if (_clean) return;
            if (!_sessionClosed && _session is not null)
            {
                try { await _session.DisposeAsync().ConfigureAwait(false); _sessionClosed = true; }
                catch (Exception ex) { failures.Add(ex); }
            }
            await ObserveControlTaskAsync(_identityCancellation, failures).ConfigureAwait(false);
            // Do not close a dependency while a failed session drain might still own it.
            if ((_session is null || _sessionClosed) && !_transportClosed)
            {
                try
                {
                    if (_dispatcher is not null) await _dispatcher.DisposeAsync().ConfigureAwait(false);
                    else _transport?.Dispose();
                    _transportClosed = true;
                }
                catch (Exception ex) { failures.Add(ex); }
            }
            if (_transportClosed)
            {
                try { _lease?.Dispose(); _lease = null; _clean = true; }
                catch (Exception ex) { failures.Add(ex); }
            }
        }
        finally { _cleanupGate.Release(); }
        if (failures.Count != 0) throw new AggregateException("Snapshot smoke cleanup failed.", failures);
    }
    public async ValueTask DisposeAsync()
    {
        Task<SnapshotSmokeReport>? run; Task cancellation;
        lock (_stateGate)
        {
            _disposed = true; run = _run;
            cancellation = _stopCancellation ??= _stop.CancelAsync();
        }
        List<Exception> failures = [];
        await ObserveControlTaskAsync(cancellation, failures).ConfigureAwait(false);
        if (run is not null) await ObserveControlTaskAsync(run, failures).ConfigureAwait(false);
        try { await CleanupAsync().ConfigureAwait(false); }
        catch (Exception ex) { failures.Add(ex); }
        if (failures.Count != 0) throw new AggregateException("Snapshot smoke disposal failed after draining owned work.", failures);
    }
    private async Task ObserveControlTaskAsync(Task task, List<Exception> failures)
    {
        try { await task.ConfigureAwait(false); }
        catch (Exception ex)
        {
            lock (_stateGate)
                if (_reportedControlFailures.Add(task)) failures.Add(ex);
        }
    }
    private static string AppendError(string? error, Exception failure) => (error is null ? "" : error + "; ") +
        (failure is AggregateException aggregate ? string.Join("; ", aggregate.Flatten().InnerExceptions.Select(ex => ex.Message)) : failure.Message);
}
