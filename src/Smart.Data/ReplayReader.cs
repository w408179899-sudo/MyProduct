using System.Collections.Immutable;
using Smart.Contracts;
namespace Smart.Data;

public sealed record ReplaySample<T>(TimeSpan At, T Value);
public sealed class ReplayClock(TimeProvider time)
{
    private readonly long _start = time.GetTimestamp();
    public TimeSpan Elapsed => time.GetElapsedTime(_start);
}

// Recorded official values re-enter through normal provider validation/publication.
// Supply FakeTimeProvider in tests; no sleeping and no hardware dependency.
public sealed class ReplayReader<T> : IRawChannelReader<T, NoPartition>
{
    private readonly ImmutableArray<ReplaySample<T>> _samples;
    private readonly ReplayClock _clock;
    public ReplayReader(IEnumerable<ReplaySample<T>> samples, TimeProvider time) : this(samples, new ReplayClock(time)) { }
    public ReplayReader(IEnumerable<ReplaySample<T>> samples, ReplayClock clock)
    {
        _samples = samples.ToImmutableArray(); _clock = clock;
        if (_samples.IsDefaultOrEmpty || _samples[0].At < TimeSpan.Zero)
            throw new ArgumentException("Replay times must be nonnegative.");
        for (var i = 1; i < _samples.Length; i++)
            if (_samples[i].At <= _samples[i - 1].At) throw new ArgumentException("Replay times must strictly increase.");
    }
    public ValueTask<RawRead<T>> CaptureAsync(CaptureContext context, NoPartition partition, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var elapsed = _clock.Elapsed;
        if (elapsed < _samples[0].At) return ValueTask.FromResult(RawRead<T>.Failed("Waiting for the first recorded publication."));
        var low = 0; var high = _samples.Length - 1;
        while (low < high)
        {
            var middle = low + (high - low + 1) / 2;
            if (_samples[middle].At <= elapsed) low = middle; else high = middle - 1;
        }
        return ValueTask.FromResult(RawRead<T>.Complete(_samples[low].Value) with { ObservationId = "replay:" + low });
    }
}
