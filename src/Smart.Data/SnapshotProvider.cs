using Smart.Contracts;
namespace Smart.Data;

public sealed record SnapshotProviderOptions(TimeSpan RefreshWait)
{
    public static SnapshotProviderOptions Default { get; } = new(TimeSpan.FromMilliseconds(25));
}
public sealed record SnapshotMetrics(long Captures, long Publications, long RefreshTimeouts, int InFlight);
public sealed class SnapshotProvider
{
    private readonly SnapshotCatalog _catalog;
    private readonly TimeProvider _time;
    private readonly IEventSink _events;
    private readonly ISnapshotObserver? _observer;
    private readonly SnapshotProviderOptions _options;
    private long _generation;
    public SnapshotProvider(SnapshotCatalog catalog, TimeProvider? time = null, IEventSink? events = null,
        ISnapshotObserver? observer = null, SnapshotProviderOptions? options = null)
    {
        if (!catalog.IsSealed) throw new InvalidOperationException("Catalog must be sealed.");
        _catalog = catalog; _time = time ?? TimeProvider.System; _events = events ?? NullEventSink.Instance;
        _observer = observer; _options = options ?? SnapshotProviderOptions.Default;
        if (_options.RefreshWait <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(options));
    }
    public SnapshotSession OpenSession(SessionIdentity identity, int maximumPartitions = 256)
    {
        identity.Validate(); ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumPartitions);
        return new(_catalog, identity, () => Interlocked.Increment(ref _generation), _time, _events, maximumPartitions, _observer, _options);
    }
}
public sealed class SnapshotSession : IDisposable, IAsyncDisposable
{
    private static readonly object SinglePartitionKey = new();
    private readonly object _sync = new();
    private readonly SnapshotCatalog _catalog;
    private readonly Func<long> _nextGeneration;
    private readonly TimeProvider _time;
    private readonly IEventSink _events;
    private readonly int _maximumPartitions;
    private readonly ISnapshotObserver? _observer;
    private readonly SnapshotProviderOptions _options;
    private readonly Dictionary<(string Channel, object Partition), object> _entries = new();
    // Reset removes published entries, but their old captures may still be returning from native I/O.
    private readonly HashSet<Task> _pendingCaptures = new();
    private readonly HashSet<Task> _pendingCancellations = new();
    private readonly CancellationTokenSource _end = new();
    private CancellationTokenSource _generationEnd = new();
    private TaskCompletionSource _changed = NewCompletion();
    private SessionIdentity _identity;
    private long _generation, _captures, _publications, _refreshTimeouts;
    private int _inFlight;
    private bool _disposed;
    internal SnapshotSession(SnapshotCatalog catalog, SessionIdentity identity, Func<long> nextGeneration,
        TimeProvider time, IEventSink events, int maximumPartitions, ISnapshotObserver? observer, SnapshotProviderOptions options)
    {
        _catalog = catalog; _identity = identity; _nextGeneration = nextGeneration; _generation = nextGeneration();
        _time = time; _events = events; _maximumPartitions = maximumPartitions; _observer = observer; _options = options;
        Reader = new Client(this);
    }
    public ISnapshotReader Reader { get; }
    public SnapshotMetrics Metrics => new(Interlocked.Read(ref _captures), Interlocked.Read(ref _publications),
        Interlocked.Read(ref _refreshTimeouts), Volatile.Read(ref _inFlight));
    private static TaskCompletionSource NewCompletion() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    public void Reset(SessionIdentity identity)
    {
        identity.Validate();
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (identity.AccountId != _identity.AccountId || identity.WorkerId != _identity.WorkerId)
                throw new InvalidOperationException("Account/worker replacement requires a new session.");
            _identity = identity; _generation = _nextGeneration(); _entries.Clear();
            var previous = _generationEnd; _generationEnd = new();
            _changed.TrySetResult(); _changed = NewCompletion();
            CancelInBackground(previous);
        }
    }
    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true; _entries.Clear(); _changed.TrySetResult();
            CancelInBackground(_end);
            CancelInBackground(_generationEnd);
        }
    }
    // Called under _sync. CancelAsync marks the token immediately, but user callbacks cannot
    // block invalidation or prevent cancellation of another owner. Failures remain observable at drain.
    private void CancelInBackground(CancellationTokenSource source)
    {
        _pendingCancellations.RemoveWhere(static task => task.IsCompletedSuccessfully);
        var cancellation = source.CancelAsync();
        if (!cancellation.IsCompletedSuccessfully) _pendingCancellations.Add(cancellation);
    }
    public async ValueTask DisposeAsync()
    {
        Dispose();
        Task[] pending, cancellations;
        lock (_sync)
        {
            cancellations = _pendingCancellations.ToArray();
            pending = _pendingCaptures.Concat(cancellations).ToArray();
        }
        // No new captures can enter after invalidation. Include prior generations and observers,
        // so the host can close diagnostics and owned dependencies only after their last use.
        try { await Task.WhenAll(pending).ConfigureAwait(false); }
        catch { /* Collect all cancellation errors below after every task has finished. */ }
        List<Exception>? failures = null;
        lock (_sync)
        {
            foreach (var cancellation in cancellations)
            {
                // A concurrent drain may already have reported this error. Report once, so retrying
                // completed cleanup can release the host's remaining resources and device lease.
                if (!_pendingCancellations.Remove(cancellation)) continue;
                if (cancellation.Exception is { } error)
                    (failures ??= []).AddRange(error.Flatten().InnerExceptions);
                else if (cancellation.IsCanceled)
                    (failures ??= []).Add(new TaskCanceledException(cancellation));
            }
        }
        if (failures is not null) throw new AggregateException("Snapshot cancellation callbacks failed after all captures drained.", failures);
    }
    private sealed class Entry<T>(CancellationToken cancellation)
    {
        public CancellationToken Cancellation { get; } = cancellation;
        public PublishedSnapshot<T>? Published;
        public TaskCompletionSource? Capture;
        public long LastAttempt, LastDiagnostic;
        public bool Attempted, Diagnosed;
        public int ConsecutiveFailures;
        public string? ObservationId;
    }
    private static object PartitionKey<TP>(TP partition) where TP : notnull =>
        typeof(TP) == typeof(NoPartition) ? SinglePartitionKey : partition;
    private static TimeSpan CaptureInterval<T>(Entry<T> entry, TimeSpan minimum)
    {
        var retry = entry.ConsecutiveFailures == 0 ? TimeSpan.Zero :
            TimeSpan.FromMilliseconds(Math.Min(250, 25 * (1 << Math.Min(4, entry.ConsecutiveFailures - 1))));
        return minimum > retry ? minimum : retry;
    }
    private static bool Matches<T>(PublishedSnapshot<T>? value, SnapshotStamp? after) => value is not null &&
        (after is null || value.Stamp.Generation != after.Value.Generation || value.Stamp.Version > after.Value.Version);
    private async ValueTask<PublishedSnapshot<T>> ReadAsync<T, TP>(SnapshotChannel<T, TP> token, TP partition,
        SnapshotStamp? after, CancellationToken caller) where TP : notnull
    {
        caller.ThrowIfCancellationRequested();
        if (partition is null) throw new ArgumentNullException(nameof(partition));
        var registration = _catalog.Resolve(token);
        while (true)
        {
            caller.ThrowIfCancellationRequested();
            Entry<T> entry; CaptureContext context; Task changed;
            TaskCompletionSource? capture; var start = false; var hasPublication = false;
            lock (_sync)
            {
                // A read can pass its caller-token check immediately before the host ends this session.
                // Invalidation must still be cancellation, never a module/data fault.
                if (_disposed) throw new OperationCanceledException("Snapshot session ended.", _end.Token);
                var key = (token.Id, PartitionKey(partition));
                if (!_entries.TryGetValue(key, out var stored))
                {
                    if (_entries.Count >= _maximumPartitions) throw new InvalidOperationException("Session partition budget exceeded.");
                    stored = new Entry<T>(_generationEnd.Token); _entries.Add(key, stored);
                }
                entry = (Entry<T>)stored;
                hasPublication = Matches(entry.Published, after);
                var due = !entry.Attempted || _time.GetElapsedTime(entry.LastAttempt) >= CaptureInterval(entry, registration.Metadata.MinimumCaptureInterval);
                if (hasPublication && (!due || entry.Capture is not null)) return entry.Published!;
                context = new(_identity, _generation); changed = _changed.Task;
                capture = entry.Capture;
                if (capture is null && due)
                {
                    capture = NewCompletion(); entry.Capture = capture; start = true;
                    _pendingCaptures.Add(capture.Task);
                }
            }
            // Capture belongs to the session. One caller cancelling must not cancel another caller's shared read.
            if (start) _ = CaptureOnceAsync(token, partition, registration, context, entry, capture!);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(caller, _end.Token);
            var ct = linked.Token;
            if (capture is not null)
            {
                var completion = capture.Task.IsCompleted ? capture.Task : Task.WhenAny(capture.Task, changed);
                try
                {
                    if (hasPublication) await completion.WaitAsync(_options.RefreshWait, _time, ct).ConfigureAwait(false);
                    else await completion.WaitAsync(ct).ConfigureAwait(false);
                }
                catch (TimeoutException) { Interlocked.Increment(ref _refreshTimeouts); }
                ct.ThrowIfCancellationRequested();
                lock (_sync)
                {
                    if (_disposed) throw new OperationCanceledException("Snapshot session ended.", _end.Token);
                    if (_generation != context.Generation) continue;
                    if (Matches(entry.Published, after)) return entry.Published!;
                }
            }
            // Only cold start or an explicit WaitForChange waits for a later official publication.
            await Task.Delay(TimeSpan.FromMilliseconds(25), _time, ct).ConfigureAwait(false);
        }
    }
    private async Task CaptureOnceAsync<T, TP>(SnapshotChannel<T, TP> token, TP partition,
        SnapshotCatalog.Registration<T, TP> registration, CaptureContext context, Entry<T> entry,
        TaskCompletionSource completion) where TP : notnull
    {
        Interlocked.Increment(ref _captures); Interlocked.Increment(ref _inFlight);
        DiagnosticEvent? diagnostic = null; PublishedSnapshot<T>? publication = null;
        try
        {
            SnapshotCatalog.CaptureResult<T> capture;
            try { capture = await registration.CaptureAsync(context, partition, entry.Published, entry.Cancellation).ConfigureAwait(false); }
            catch (OperationCanceledException) when (entry.Cancellation.IsCancellationRequested) { return; }
            catch (Exception ex)
            { capture = new(MergeDecision<T>.Hold, new(Guid.NewGuid().ToString("N"), Error: ex.GetType().Name + ": " + ex.Message), ReadCompleteness.Failed); }
            lock (_sync)
            {
                if (_disposed || _generation != context.Generation) return;
                entry.LastAttempt = _time.GetTimestamp(); entry.Attempted = true;
                // Replay/event sources may identify a repeated observation. Re-reading one frame must not fabricate a publication.
                if (capture.ObservationId is not null && capture.ObservationId == entry.ObservationId) return;
                if (capture.Decision.Publish)
                {
                    entry.Published = new(capture.Decision.Value,
                        new(context.Generation, (entry.Published?.Stamp.Version ?? 0) + 1), _time.GetUtcNow());
                    entry.ConsecutiveFailures = 0; entry.ObservationId = capture.ObservationId;
                    publication = entry.Published; Interlocked.Increment(ref _publications);
                }
                else entry.ConsecutiveFailures = Math.Min(100, entry.ConsecutiveFailures + 1);
                if ((!capture.Decision.Publish || capture.Completeness == ReadCompleteness.Partial) &&
                    (!entry.Diagnosed || _time.GetElapsedTime(entry.LastDiagnostic) >= TimeSpan.FromSeconds(2)))
                {
                    entry.Diagnosed = true; entry.LastDiagnostic = _time.GetTimestamp();
                    diagnostic = new(_time.GetUtcNow(), "snapshot.capture", context.Session.AccountId,
                        $"channel={token.Id};partition={partition};generation={context.Generation};pid={context.Session.ProcessId};capture={capture.Diagnostics.CaptureId};kind={capture.Completeness};publish={capture.Decision.Publish};invalid={capture.Diagnostics.InvalidFields};termination={capture.Diagnostics.TraversalTermination};error={capture.Diagnostics.Error}");
                }
            }
        }
        finally
        {
            // Observers enqueue bounded work; they cannot change committed data.
            if (publication is not null) { try { _observer?.Published(context.Session, token.Id, partition, publication); } catch { } }
            if (diagnostic is not null) { try { _events.Write(diagnostic); } catch { } }
            lock (_sync)
            {
                entry.Capture = null;
                Interlocked.Decrement(ref _inFlight);
                _pendingCaptures.Remove(completion.Task);
                completion.TrySetResult();
            }
        }
    }
    private sealed class Client(SnapshotSession owner) : ISnapshotReader
    {
        public ValueTask<PublishedSnapshot<T>> ReadAsync<T, TP>(SnapshotChannel<T, TP> channel,
            TP partition, CancellationToken cancellationToken = default) where TP : notnull => owner.ReadAsync(channel, partition, null, cancellationToken);
        public ValueTask<PublishedSnapshot<T>> WaitForChangeAsync<T, TP>(SnapshotChannel<T, TP> channel,
            TP partition, SnapshotStamp after, CancellationToken cancellationToken = default) where TP : notnull => owner.ReadAsync(channel, partition, after, cancellationToken);
    }
}
