using System.Diagnostics;
using Smart.Adapters.Dma;
using Smart.Contracts;
using Smart.Data;
using Smart.ProbeProtocol;
using Smart.Runtime;

namespace Smart.HardwareProbe;

public sealed record ProbeReport(bool Passed, string Outcome, string? Error, int ProcessId, Guid FixtureSession,
    long OfficialReads, long Publications, long CounterChanges, long ZeroPublications, long? FirstCounter, long? LastCounter,
    long? LastValue, long TornReads, long ValidationRejected, long ReadErrors, bool SoftDeadlineReached,
    double MeasurementMilliseconds, double CpuMilliseconds, long AllocatedBytes, double StopMilliseconds,
    bool CleanupComplete, double MaximumProgressGapMilliseconds, double ProgressGapLimitMilliseconds,
    double MinimumObservationMilliseconds, bool ProgressCoverageSufficient, DmaDispatcherMetrics? Dma, SnapshotMetrics? Snapshots);

// Single-use scope. Failed cleanup keeps the original transport and physical lease for DisposeAsync retry.
public sealed class HardwareProbeRunner(Func<IProcessMemoryTransport> connect, InputLeaseRegistry leases,
    string deviceUri, TimeProvider? time = null) : IAsyncDisposable
{
    private readonly object _sync = new();
    private readonly SemaphoreSlim _cleanupGate = new(1, 1);
    private readonly CancellationTokenSource _stop = new();
    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private Task<ProbeReport>? _run;
    private IDisposable? _lease;
    private IProcessMemoryTransport? _transport;
    private DmaDispatcher? _dispatcher;
    private SnapshotSession? _snapshots;
    private bool _disposed, _cleanupComplete, _snapshotsDisposed, _transportDisposed;

    public Task<ProbeReport> RunAsync(ProbeManifest manifest, ProbeRunOptions options, CancellationToken token = default)
    {
        manifest.Validate(); options.Validate();
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceUri);
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_run is not null) throw new InvalidOperationException("A hardware probe scope is single-use.");
            _run = RunCoreAsync(manifest, options, token);
            return _run;
        }
    }
    private async Task<ProbeReport> RunCoreAsync(ProbeManifest manifest, ProbeRunOptions options, CancellationToken external)
    {
        var start = _time.GetTimestamp();
        using var process = Process.GetCurrentProcess();
        var cpu = process.TotalProcessorTime;
        var allocated = GC.GetTotalAllocatedBytes();
        using var deadline = new CancellationTokenSource(options.Duration, _time);
        using var identityChanged = new CancellationTokenSource();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token, external, _stop.Token, identityChanged.Token);
        string? error = null, identityError = null;
        var outcome = "Completed";
        ProbeReader? raw = null;
        var publicationsObserver = new ProbePublicationObserver(_time, start);
        var maximumGap = options.ResolveProgressGap(manifest).TotalMilliseconds;
        long reads = 0;
        try
        {
            linked.Token.ThrowIfCancellationRequested();
            _lease = leases.Acquire("dma:" + deviceUri.Trim());
            // Initialization and exact-PID binding are native calls too. The deadline is soft while they are blocked.
            _transport = await Task.Run(connect, CancellationToken.None).ConfigureAwait(false);
            linked.Token.ThrowIfCancellationRequested();
            if (!StringComparer.OrdinalIgnoreCase.Equals(_transport.DeviceId, deviceUri.Trim()))
                throw new InvalidOperationException("The transport device identity differs from the explicitly selected device.");
            var binding = await Task.Run(() => _transport.GetProcess(manifest.ProcessId, manifest.MainModuleName), CancellationToken.None).ConfigureAwait(false);
            linked.Token.ThrowIfCancellationRequested();
            if (binding.ProcessId != manifest.ProcessId || binding.ModuleBase == 0 || string.IsNullOrWhiteSpace(binding.ProcessIdentity) ||
                !StringComparer.OrdinalIgnoreCase.Equals(NormalizeName(binding.Name), NormalizeName(manifest.ProcessName)))
                throw new InvalidOperationException("The manifest PID, process name, or required module does not match the selected target.");
            _dispatcher = new(new BoundProbeTransport(_transport, manifest, binding), capacity: 4);
            raw = new(_dispatcher, manifest, message =>
            {
                Interlocked.CompareExchange(ref identityError, message, null);
                identityChanged.Cancel();
            });
            var catalog = new SnapshotCatalog();
            var channel = catalog.Register<ProbeSample, NoPartition, ProbeSample>("fixture.sample", raw,
                new ReplaceMerger<ProbeSample>(_ => true), SnapshotMergePolicy.Replace, options.PollInterval);
            catalog.Seal();
            _snapshots = new SnapshotProvider(catalog, _time, observer: publicationsObserver).OpenSession(new(_transport.DeviceId, _transport.ConnectionId,
                "hardware-probe", manifest.SessionId.ToString("N"), manifest.ProcessId, binding.ProcessIdentity, binding.ModuleIdentity));
            while (true)
            {
                linked.Token.ThrowIfCancellationRequested();
                _ = await _snapshots.Reader.ReadAsync(channel, NoPartition.Value, linked.Token).ConfigureAwait(false);
                linked.Token.ThrowIfCancellationRequested();
                reads++;
                await Task.Delay(options.PollInterval, _time, linked.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        {
            if (identityError is not null) { outcome = "TargetChanged"; error = identityError; }
            else if (external.IsCancellationRequested || _stop.IsCancellationRequested) outcome = "Cancelled";
        }
        catch (Exception ex) { outcome = "Failed"; error = ex.GetType().Name + ": " + ex.Message; }
        var measurementEnded = _time.GetTimestamp();
        var elapsed = _time.GetElapsedTime(start, measurementEnded).TotalMilliseconds;
        var cpuMilliseconds = (process.TotalProcessorTime - cpu).TotalMilliseconds;
        var allocations = GC.GetTotalAllocatedBytes() - allocated;
        var stop = Stopwatch.StartNew();
        try { await CleanupAsync().ConfigureAwait(false); }
        catch (Exception ex) { outcome = "CleanupFailed"; error = (error is null ? "" : error + "; ") + ex.Message; }
        stop.Stop();
        var publications = _snapshots?.Metrics.Publications ?? 0;
        var observed = publicationsObserver.Snapshot(measurementEnded);
        var coverageSufficient = elapsed >= maximumGap * 2;
        var passed = outcome == "Completed" && _cleanupComplete && observed.CounterChanges > 0 && publications >= 2 &&
            coverageSufficient && observed.MaximumGapMilliseconds <= maximumGap;
        if (!passed && error is null && outcome == "Completed")
        {
            if (observed.CounterChanges == 0 || publications < 2)
                error = "No proof of fixture activity: at least two different valid counters are required.";
            else if (!coverageSufficient)
                error = "Observation must cover at least two configured maximum-progress-gap windows.";
            else error = "Fixture counter activity stopped longer than the configured maximum-progress-gap window.";
        }
        return new(passed, outcome, error, manifest.ProcessId, manifest.SessionId, reads, publications,
            observed.CounterChanges, observed.ZeroPublications, observed.FirstCounter, observed.LastCounter, observed.LastValue,
            raw?.TornReads ?? 0, raw?.ValidationRejected ?? 0, raw?.ReadErrors ?? 0,
            deadline.IsCancellationRequested, elapsed, cpuMilliseconds, allocations, stop.Elapsed.TotalMilliseconds,
            _cleanupComplete, observed.MaximumGapMilliseconds, maximumGap, maximumGap * 2, coverageSufficient,
            _dispatcher?.Metrics, _snapshots?.Metrics);
    }
    private static string NormalizeName(string name)
    {
        name = Path.GetFileName(name);
        return name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;
    }
    private async Task CleanupAsync()
    {
        await _cleanupGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_cleanupComplete) return;
            if (!_snapshotsDisposed && _snapshots is not null) { await _snapshots.DisposeAsync().ConfigureAwait(false); _snapshotsDisposed = true; }
            if (!_transportDisposed)
            {
                if (_dispatcher is not null) await _dispatcher.DisposeAsync().ConfigureAwait(false);
                else _transport?.Dispose();
                _transportDisposed = true;
            }
            _lease?.Dispose(); _lease = null;
            _cleanupComplete = true;
        }
        finally { _cleanupGate.Release(); }
    }
    public async ValueTask DisposeAsync()
    {
        Task<ProbeReport>? run;
        lock (_sync) { _disposed = true; run = _run; }
        await _stop.CancelAsync().ConfigureAwait(false);
        if (run is not null) await run.ConfigureAwait(false);
        await CleanupAsync().ConfigureAwait(false);
    }
}
