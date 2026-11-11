using Roadhog.Core.Radar;

namespace Roadhog.Application.StationaryCombat;

/// <summary>Tracks consecutive failed return ticks, independently of forward movement.</summary>
public sealed class ReturnNavigationFailureState
{
    private long _observationVersion;
    private long _startedAt;
    private uint _mapId;
    private RadarPoint _goal;
    private string? _phase;

    public bool Active => _phase is not null;

    public TimeSpan Observe(uint mapId, RadarPoint goal, string phase, TimeProvider clock)
    {
        _observationVersion++;
        if (!Active || _mapId != mapId || _goal != goal || _phase != phase)
        {
            _startedAt = clock.GetTimestamp();
            _mapId = mapId;
            _goal = goal;
            _phase = phase;
        }
        return clock.GetElapsedTime(_startedAt);
    }

    public void Reset() => _phase = null;

    // Both the worker and the controller use this scope. An intervening tick owned by
    // combat, maintenance, cleanup, channel switching or a read yield breaks the streak.
    // Nested scopes preserve a failure observed by the inner controller tick.
    public TickObservation TrackTick() => new(this, _observationVersion);

    public readonly struct TickObservation : IDisposable
    {
        private readonly ReturnNavigationFailureState _state;
        private readonly long _version;

        internal TickObservation(ReturnNavigationFailureState state, long version)
        {
            _state = state;
            _version = version;
        }

        public void Dispose()
        {
            if (_state._observationVersion == _version) _state.Reset();
        }
    }
}
