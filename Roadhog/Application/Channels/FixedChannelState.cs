namespace Roadhog.Application.Channels;

public sealed class FixedChannelState
{
    public int TargetChannelNumber { get; private set; }
    public uint MapId { get; private set; }
    public int ObservedChannelNumber { get; private set; }
    public bool Completed { get; private set; }
    public bool WaitingForPeace { get; private set; }
    public bool LocationObserved { get; private set; }
    public DateTimeOffset NextLocationReadAt { get; set; } = DateTimeOffset.MinValue;
    public DateTimeOffset? PeaceSince { get; private set; }
    public DateTimeOffset NextAttemptAt { get; private set; } = DateTimeOffset.MinValue;
    public DateTimeOffset NextChannelReadAt { get; set; } = DateTimeOffset.MinValue;
    public int SwitchAttemptCount { get; private set; }
    public bool AwaitingConfirmation { get; set; }
    public uint AttemptMapId { get; private set; }
    private uint? _lastHp;

    public void ObserveLocation(int target, uint map, int channel)
    {
        if (TargetChannelNumber != target) { Reset(); TargetChannelNumber = target; }
        LocationObserved = true;
        if (MapId != map || ObservedChannelNumber != channel)
        {
            PeaceSince = null;
            _lastHp = null;
            MapId = map;
            ObservedChannelNumber = channel;
        }
    }

    public void BreakPeace() => PeaceSince = null;

    public void Complete()
    {
        Completed = true;
        WaitingForPeace = false;
        AwaitingConfirmation = false;
    }

    public bool BeginWaitingForPeace()
    {
        if (WaitingForPeace) return false;
        WaitingForPeace = true;
        BreakPeace();
        return true;
    }

    public void CancelWaitingForPeace() => WaitingForPeace = false;

    public void ObserveActivity(uint hp, bool busy, DateTimeOffset now)
    {
        if (busy || (_lastHp.HasValue && hp < _lastHp.Value)) PeaceSince = null;
        else PeaceSince ??= now;
        _lastHp = hp;
    }

    public bool IsPeaceful(DateTimeOffset now) => PeaceSince.HasValue && now - PeaceSince.Value >= FixedChannelController.RequiredPeaceDuration;

    public void StartAttempt(DateTimeOffset now)
    {
        SwitchAttemptCount++;
        WaitingForPeace = false;
        AttemptMapId = MapId;
        AwaitingConfirmation = true;
        NextAttemptAt = now + FixedChannelController.RetryInterval;
    }

    public void Reset()
    {
        TargetChannelNumber = 0;
        MapId = 0;
        ObservedChannelNumber = 0;
        Completed = false;
        WaitingForPeace = false;
        LocationObserved = false;
        NextLocationReadAt = DateTimeOffset.MinValue;
        PeaceSince = null;
        _lastHp = null;
        NextAttemptAt = DateTimeOffset.MinValue;
        NextChannelReadAt = DateTimeOffset.MinValue;
        SwitchAttemptCount = 0;
        AwaitingConfirmation = false;
        AttemptMapId = 0;
    }
}
