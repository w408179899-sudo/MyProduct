using Roadhog.Core.Model;

namespace Roadhog.Application.StationaryCombat;

public enum LocalDefenseThreatGuardStatus
{
    Clear = 0,
    Confirmed = 1,
    UnknownHeld = 2
}

/// <summary>
/// Retains only the identity of a previously confirmed local-side threat while a
/// later observation is uncertain. A retained observation is a transition guard;
/// it must never be treated as a current attack target by itself.
/// </summary>
public sealed class LocalDefenseThreatGuard
{
    private readonly object _syncRoot = new();
    private LocalDefenseThreatGuardStatus _status;
    private WorldObjectSnapshot? _lastConfirmedThreat;
    private DateTimeOffset _lastConfirmedAt = DateTimeOffset.MinValue;
    private long _lastConfirmedCaptureSequence;
    private long _lastConfirmedObservationOrder;
    private int _consecutiveConfirmedNegativeObservations;
    private long _lastNegativeCaptureSequence;
    private long _lastNegativeObservationOrder;
    private DateTimeOffset _lastBypassAttemptAt = DateTimeOffset.MinValue;
    private long _highestObservationOrder;

    internal object SyncRoot => _syncRoot;

    public LocalDefenseThreatGuardStatus Status
    {
        get
        {
            lock (_syncRoot)
            {
                return _status;
            }
        }
    }

    public WorldObjectSnapshot? LastConfirmedThreat
    {
        get
        {
            lock (_syncRoot)
            {
                return _lastConfirmedThreat;
            }
        }
    }

    public DateTimeOffset LastConfirmedAt
    {
        get
        {
            lock (_syncRoot)
            {
                return _lastConfirmedAt;
            }
        }
    }

    public long LastConfirmedCaptureSequence
    {
        get
        {
            lock (_syncRoot)
            {
                return _lastConfirmedCaptureSequence;
            }
        }
    }

    public int ConsecutiveConfirmedNegativeObservations
    {
        get
        {
            lock (_syncRoot)
            {
                return _consecutiveConfirmedNegativeObservations;
            }
        }
    }

    public long LastNegativeCaptureSequence
    {
        get
        {
            lock (_syncRoot)
            {
                return _lastNegativeCaptureSequence;
            }
        }
    }

    public DateTimeOffset LastBypassAttemptAt
    {
        get
        {
            lock (_syncRoot)
            {
                return _lastBypassAttemptAt;
            }
        }
    }

    public bool HasRetainedThreat
    {
        get
        {
            lock (_syncRoot)
            {
                return HasRetainedThreatUnsafe();
            }
        }
    }

    public bool TryBeginObservation(long observationOrder)
    {
        lock (_syncRoot)
        {
            if (observationOrder <= 0)
            {
                return true;
            }

            if (observationOrder <= _highestObservationOrder)
            {
                return false;
            }

            _highestObservationOrder = observationOrder;
            return true;
        }
    }

    public bool Confirm(
        WorldObjectSnapshot threat,
        long captureSequence,
        DateTimeOffset observedAt,
        long observationOrder = 0)
    {
        lock (_syncRoot)
        {
            var effectiveOrder = observationOrder;
            if (effectiveOrder > 0 && effectiveOrder < _highestObservationOrder)
            {
                return false;
            }

            _highestObservationOrder = Math.Max(_highestObservationOrder, effectiveOrder);
            _lastConfirmedThreat = threat;
            _lastConfirmedAt = observedAt;
            _lastConfirmedCaptureSequence = captureSequence;
            _lastConfirmedObservationOrder = effectiveOrder;
            _consecutiveConfirmedNegativeObservations = 0;
            _lastNegativeCaptureSequence = 0;
            _lastNegativeObservationOrder = 0;
            _lastBypassAttemptAt = DateTimeOffset.MinValue;
            _status = LocalDefenseThreatGuardStatus.Confirmed;
            return true;
        }
    }

    public bool HoldUnknown(long observationOrder = 0)
    {
        lock (_syncRoot)
        {
            if (observationOrder > 0 && observationOrder != _highestObservationOrder)
            {
                return false;
            }

            if (HasRetainedThreatUnsafe())
            {
                _status = LocalDefenseThreatGuardStatus.UnknownHeld;
                return true;
            }

            return false;
        }
    }

    public bool RecordConfirmedNegative(
        long captureSequence,
        int requiredConfirmations,
        long observationOrder = 0)
    {
        lock (_syncRoot)
        {
            var effectiveOrder = observationOrder > 0 ? observationOrder : captureSequence;
            if (!HasRetainedThreatUnsafe() ||
                captureSequence == 0 ||
                effectiveOrder <= _lastConfirmedObservationOrder ||
                effectiveOrder == _lastNegativeObservationOrder ||
                (observationOrder > 0 && effectiveOrder != _highestObservationOrder))
            {
                return false;
            }

            _lastNegativeCaptureSequence = captureSequence;
            _lastNegativeObservationOrder = effectiveOrder;
            _highestObservationOrder = Math.Max(_highestObservationOrder, effectiveOrder);
            _consecutiveConfirmedNegativeObservations++;
            if (_consecutiveConfirmedNegativeObservations < Math.Max(1, requiredConfirmations))
            {
                _status = LocalDefenseThreatGuardStatus.UnknownHeld;
                return false;
            }

            ReleaseUnsafe();
            return true;
        }
    }

    public bool Matches(WorldObjectSnapshot candidate)
    {
        lock (_syncRoot)
        {
            return _lastConfirmedThreat is { } retained &&
                   StationaryCombatState.IsSameTarget(
                       retained.EntityId,
                       retained.ServerObjectId,
                       candidate.EntityId,
                       candidate.ServerObjectId);
        }
    }

    public bool IsExpired(DateTimeOffset now, TimeSpan retention)
    {
        lock (_syncRoot)
        {
            return HasRetainedThreatUnsafe() &&
                   _lastConfirmedAt != DateTimeOffset.MinValue &&
                   now - _lastConfirmedAt >= retention;
        }
    }

    public bool ShouldAttemptBypass(DateTimeOffset now, TimeSpan minimumInterval)
    {
        lock (_syncRoot)
        {
            return HasRetainedThreatUnsafe() &&
                   (_lastBypassAttemptAt == DateTimeOffset.MinValue ||
                    now - _lastBypassAttemptAt >= minimumInterval);
        }
    }

    public void MarkBypassAttempt(DateTimeOffset now)
    {
        lock (_syncRoot)
        {
            _lastBypassAttemptAt = now;
        }
    }

    public void Clear()
    {
        lock (_syncRoot)
        {
            ClearUnsafe(resetObservationSequence: true);
        }
    }

    public void Release()
    {
        lock (_syncRoot)
        {
            ReleaseUnsafe();
        }
    }

    public bool ReleaseIfMatches(ushort entityId, uint serverObjectId)
    {
        lock (_syncRoot)
        {
            if (_lastConfirmedThreat is not { } retained ||
                !StationaryCombatState.IsSameTarget(
                    retained.EntityId,
                    retained.ServerObjectId,
                    entityId,
                    serverObjectId))
            {
                return false;
            }

            ReleaseUnsafe();
            return true;
        }
    }

    public bool TryExpire(
        DateTimeOffset now,
        TimeSpan retention,
        out WorldObjectSnapshot? retained,
        out TimeSpan age)
    {
        lock (_syncRoot)
        {
            retained = _lastConfirmedThreat;
            age = _lastConfirmedAt == DateTimeOffset.MinValue
                ? TimeSpan.Zero
                : now - _lastConfirmedAt;
            if (!HasRetainedThreatUnsafe() ||
                _status != LocalDefenseThreatGuardStatus.UnknownHeld ||
                _lastConfirmedAt == DateTimeOffset.MinValue ||
                age < retention)
            {
                return false;
            }

            ReleaseUnsafe();
            return true;
        }
    }

    private bool HasRetainedThreatUnsafe()
    {
        return _status != LocalDefenseThreatGuardStatus.Clear &&
               _lastConfirmedThreat is not null;
    }

    private void ReleaseUnsafe()
    {
        _status = LocalDefenseThreatGuardStatus.Clear;
        _lastConfirmedThreat = null;
        _lastConfirmedAt = DateTimeOffset.MinValue;
        _lastConfirmedCaptureSequence = 0;
        _lastConfirmedObservationOrder = 0;
        _consecutiveConfirmedNegativeObservations = 0;
        _lastNegativeCaptureSequence = 0;
        _lastNegativeObservationOrder = 0;
        _lastBypassAttemptAt = DateTimeOffset.MinValue;
    }

    private void ClearUnsafe(bool resetObservationSequence)
    {
        ReleaseUnsafe();
        if (resetObservationSequence)
        {
            _highestObservationOrder = 0;
        }
    }
}
