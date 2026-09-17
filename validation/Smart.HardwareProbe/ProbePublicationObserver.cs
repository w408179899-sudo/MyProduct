using Smart.Contracts;
using Smart.Data;
using Smart.ProbeProtocol;

namespace Smart.HardwareProbe;

// Count every committed publication, including those arriving between observation-loop reads.
internal sealed record ProbePublicationMetrics(long CounterChanges, long ZeroPublications, long? FirstCounter,
    long? LastCounter, long? LastValue, double MaximumGapMilliseconds);

internal sealed class ProbePublicationObserver(TimeProvider time, long started) : ISnapshotObserver
{
    private readonly object _sync = new();
    private long _changes, _zeroes, _lastProgressAt = started;
    private long? _firstCounter, _lastCounter, _lastValue;
    private double _maximumGap;
    public void Published<T, TP>(SessionIdentity session, string channel, TP partition, PublishedSnapshot<T> snapshot) where TP : notnull
    {
        if (snapshot.Value is not ProbeSample sample) throw new InvalidOperationException("Unexpected probe publication type.");
        lock (_sync)
        {
            if (_lastCounter is null || _lastCounter != sample.Counter)
            {
                var now = time.GetTimestamp();
                _maximumGap = Math.Max(_maximumGap, time.GetElapsedTime(_lastProgressAt, now).TotalMilliseconds);
                _lastProgressAt = now;
                if (_lastCounter is not null) _changes++;
            }
            _firstCounter ??= sample.Counter;
            _lastCounter = sample.Counter; _lastValue = sample.Value;
            if (sample.Value == 0) _zeroes++;
        }
    }
    public ProbePublicationMetrics Snapshot(long measurementEnded)
    {
        lock (_sync) return new(_changes, _zeroes, _firstCounter, _lastCounter, _lastValue,
            Math.Max(_maximumGap, Math.Max(0, time.GetElapsedTime(_lastProgressAt, measurementEnded).TotalMilliseconds)));
    }
}
