using Roadhog.Core.Model;

namespace Roadhog.Application.SemiAuto;

/// <summary>Action lifecycle state only. Trusted snapshot storage belongs to the provider.</summary>
public sealed class QuickbarSkillCombatState
{
    public QuickbarSkillCombatState(TimeProvider? timeProvider = null)
    {
        ClockBootstrap = new QuickbarSkillClockBootstrap(timeProvider);
    }

    public QuickbarSkillClockBootstrap ClockBootstrap { get; }
    public QuickbarAttackWeaveState AttackWeave { get; } = new();
    internal bool ClockBootstrapCompletionLogged { get; set; }
    private readonly Dictionary<string, DateTimeOffset> _retryAfter = new(StringComparer.Ordinal);
    private readonly Dictionary<string, RetryHold> _monotonicRetryAfter = new(StringComparer.Ordinal);
    private TimeProvider? _pendingTimeProvider;
    private long _pendingStartedTimestamp;
    private long _pendingLastAttemptTimestamp;
    private TimeSpan _pendingConfirmationTimeout;
    private readonly Dictionary<string, QuickbarSkillNode> _acceptedChainOpportunities = new(StringComparer.Ordinal);
    private readonly HashSet<string> _yieldedRoots = new(StringComparer.Ordinal);
    private string? _bindingLayout;
    private ushort _targetEntityId;
    private uint _targetServerObjectId;
    private bool _scopeObserved;
    private uint? _releaseClockAnchor;
    private long _releaseClockAnchorTimestamp;
    private TimeProvider? _releaseClockTimeProvider;
    private long _nextAttemptId;
    private long _transitionProcessedAttemptId;
    private TimeProvider? _transitionTimeProvider;
    private long _transitionStartedTimestamp;

    public QuickbarSkillNode? ActiveChainSource { get; private set; }
    public QuickbarSkillPendingAction? PendingAction { get; private set; }
    public QuickbarSkillChainTransition? ChainTransition { get; private set; }
    public string? LastChainTransitionEndReason { get; private set; }
    public long LastAvailabilityVersion { get; internal set; }
    public bool YieldToWorker { get; internal set; }
    public bool SupportsCombatState { get; internal set; }
    internal bool TickInFlight { get; set; }
    public static readonly TimeSpan RetryInterval = TimeSpan.FromMilliseconds(80);
    // At 80ms, allow the existing eight-second acceptance window. A handful
    // of retries is too short when another skill's animation is still running.
    public const int MaximumUnconfirmedAttempts = 100;
    public static readonly TimeSpan MaximumChainTransitionWait = TimeSpan.FromMilliseconds(1500);

    public void Reset()
    {
        AttackWeave.Reset();
        ActiveChainSource = null;
        DropPendingAction();
        EndChainTransition("reset");
        _transitionProcessedAttemptId = 0;
        _retryAfter.Clear();
        _monotonicRetryAfter.Clear();
        _acceptedChainOpportunities.Clear();
        _yieldedRoots.Clear();
        _bindingLayout = null;
        _scopeObserved = false;
        ClearReleaseClockLowerBound();
        ClockBootstrap.Reset();
        ClockBootstrapCompletionLogged = false;
        LastAvailabilityVersion = 0;
        YieldToWorker = false;
        SupportsCombatState = false;
    }

    public bool ObserveScope(LockedTargetSnapshot target, SkillAvailabilitySnapshot availability)
    {
        // A provider may expose only supported dynamic slots. The full, separately
        // validated binding signature prevents a root-to-child display transition
        // from looking like a user changed the physical shortcut layout.
        var bindingParts = availability.BindingSlots?.OrderBy(slot => slot.Bar).ThenBy(slot => slot.Slot)
            .Select(slot => (int)slot.Bar + "," + slot.Slot + "," + slot.ContentType + "," + slot.BaseSkillId)
            ?? availability.Slots.OrderBy(slot => slot.Bar).ThenBy(slot => slot.Slot)
                .Select(slot => (int)slot.Bar + "," + slot.Slot + "," + slot.ContentType + "," + slot.BaseSkillId);
        var layout = availability.Page + ":" + (availability.BindingSignature ?? string.Join(";", bindingParts));
        var changed = _scopeObserved && (_targetEntityId != target.TargetEntityId ||
            _targetServerObjectId != target.ServerObjectId || !string.Equals(layout, _bindingLayout, StringComparison.Ordinal));
        if (changed || !target.IsMonsterAlive)
        {
            AttackWeave.Reset();
            ActiveChainSource = null;
            DropPendingAction();
            EndChainTransition("scope_changed");
            _transitionProcessedAttemptId = 0;
            _retryAfter.Clear();
            _monotonicRetryAfter.Clear();
            _acceptedChainOpportunities.Clear();
            _yieldedRoots.Clear();
            ClearReleaseClockLowerBound();
            ClockBootstrap.Reset();
            ClockBootstrapCompletionLogged = false;
        }
        _scopeObserved = true;
        _targetEntityId = target.TargetEntityId;
        _targetServerObjectId = target.ServerObjectId;
        _bindingLayout = layout;
        ObserveAcceptedOpportunities(availability);
        return changed;
    }

    public void BeginAction(QuickbarSkillNode node, SkillSnapshot? skill, SkillAvailabilitySnapshot availability,
        DateTimeOffset now, TimeSpan confirmationTimeout, DateTimeOffset? attemptTickStartedAt = null,
        TimeProvider? timeProvider = null, bool isClockBootstrap = false, long? attemptTickStartedTimestamp = null)
    {
        // Poll cadence owns retries; varying read latency must not defer an
        // otherwise eligible retry by an entire extra polling cycle.
        var attemptCycleStartedAt = attemptTickStartedAt ?? now;
        if (PendingAction is { RetryStopped: false } pending && pending.Node.NodeKey == node.NodeKey)
        {
            // A retry retains the baseline captured before the first key. Otherwise
            // a late release could become the new baseline and never be confirmed.
            PendingAction = pending with { LastAttemptAt = attemptCycleStartedAt, AttemptCount = pending.AttemptCount + 1 };
            if (_pendingTimeProvider is not null)
                _pendingLastAttemptTimestamp = ReferenceEquals(_pendingTimeProvider, timeProvider)
                    ? attemptTickStartedTimestamp ?? _pendingTimeProvider.GetTimestamp()
                    : _pendingTimeProvider.GetTimestamp();
            return;
        }
        var latePredecessorId = ChainTransition is { Confirmed: false } transition &&
            transition.Source.Children.Any(child => child.NodeKey == node.NodeKey) ? transition.Source.SkillId : 0;
        EndChainTransition(node.NodeKey.Contains('/') ? "child_pressed" : "root_selected");
        if (_yieldedRoots.Contains(node.NodeKey)) _yieldedRoots.Clear();
        PendingAction = new(node, skill?.CooldownEndTime ?? 0, availability.LastReleasedSkillTime,
            now, now + confirmationTimeout, attemptCycleStartedAt, 1, ++_nextAttemptId,
            AllowedLatePredecessorSkillId: latePredecessorId, IsClockBootstrap: isClockBootstrap);
        _pendingTimeProvider = timeProvider;
        _pendingStartedTimestamp = timeProvider?.GetTimestamp() ?? 0;
        _pendingLastAttemptTimestamp = attemptTickStartedTimestamp ?? _pendingStartedTimestamp;
        _pendingConfirmationTimeout = confirmationTimeout;
    }

    public bool IsRepeatBlocked(QuickbarSkillNode node, DateTimeOffset now) =>
        IsRetryHoldActive(node.NodeKey, now) ||
        _acceptedChainOpportunities.ContainsKey(node.NodeKey) ||
        (PendingAction is { RetryStopped: false } pending && pending.Node.NodeKey == node.NodeKey &&
            ((_pendingTimeProvider is not null
                ? _pendingTimeProvider.GetElapsedTime(_pendingLastAttemptTimestamp) < RetryInterval
                : now < pending.LastAttemptAt + RetryInterval) || pending.AttemptCount >= MaximumUnconfirmedAttempts));

    private bool IsRetryHoldActive(string nodeKey, DateTimeOffset now) =>
        _monotonicRetryAfter.TryGetValue(nodeKey, out var hold)
            ? hold.TimeProvider.GetElapsedTime(hold.StartedTimestamp) < hold.Duration
            : _retryAfter.TryGetValue(nodeKey, out var retryAfter) && now < retryAfter;

    public bool HasYieldedRoot(QuickbarSkillNode node) => _yieldedRoots.Contains(node.NodeKey);

    /// <summary>
    /// Conservative game-clock estimate under the same running-client, one-ms-per-ms
    /// assumption as the existing cooldown clock. The actual release tick is an
    /// earlier instant, so a late first observation only adds waiting. A paused or
    /// slower remote clock invalidates that rate assumption; this is not an OS tick
    /// offset or proof of a clock source. Only numeric calibration anchors are stored.
    /// </summary>
    public uint? ObserveReleaseClockLowerBound(uint actualReleaseTime, TimeProvider timeProvider)
    {
        if (actualReleaseTime == 0)
        {
            ClearReleaseClockLowerBound();
            return null;
        }
        if (_releaseClockAnchor != actualReleaseTime || !ReferenceEquals(_releaseClockTimeProvider, timeProvider))
        {
            _releaseClockAnchor = actualReleaseTime;
            _releaseClockAnchorTimestamp = timeProvider.GetTimestamp();
            _releaseClockTimeProvider = timeProvider;
            return actualReleaseTime;
        }
        var elapsed = timeProvider.GetElapsedTime(_releaseClockAnchorTimestamp);
        var elapsedMs = elapsed.Ticks / TimeSpan.TicksPerMillisecond;
        // A clock reset or a half-range interval cannot safely participate in the
        // usual signed uint32 cooldown comparison. Re-anchor conservatively.
        if (elapsedMs < 0 || elapsedMs >= int.MaxValue)
        {
            _releaseClockAnchorTimestamp = timeProvider.GetTimestamp();
            return actualReleaseTime;
        }
        return unchecked(actualReleaseTime + (uint)elapsedMs);
    }

    private void ClearReleaseClockLowerBound()
    {
        _releaseClockAnchor = null;
        _releaseClockAnchorTimestamp = 0;
        _releaseClockTimeProvider = null;
    }

    public bool TryConfirmAction(SkillAvailabilitySnapshot availability, IReadOnlyList<SkillSnapshot> skills, DateTimeOffset now,
        TimeProvider? timeProvider = null)
    {
        if (PendingAction is not { } pending) return false;
        var preciseRelease = availability.LastReleasedSkillId == pending.Node.SkillId &&
            availability.LastReleasedSkillTime != pending.PreviousReleasedSkillTime &&
            (pending.LatePredecessorReleasedSkillTime == 0 || availability.LastReleasedSkillTime != pending.LatePredecessorReleasedSkillTime);
        var skill = skills.FirstOrDefault(item => item.SkillId == pending.Node.SkillId);
        var observedDifferentRelease = availability.LastReleasedSkillTime != pending.PreviousReleasedSkillTime &&
            availability.LastReleasedSkillId != 0 && availability.LastReleasedSkillId != pending.Node.SkillId;
        // If a release clock is available, a CD baseline captured before a user's
        // intervening manual action must not be accepted as proof of this key.
        // A cleared actor skill id after a probability failure still has its new
        // release time and may be confirmed by the exact configured skill's CD.
        var releaseClockAdvancedOrUnavailable = (availability.LastReleasedSkillTime != pending.PreviousReleasedSkillTime ||
            (availability.CombatState is null && availability.LastReleasedSkillTime == 0 &&
             pending.PreviousReleasedSkillTime == 0 && availability.LastReleasedSkillId == 0)) &&
            (pending.LatePredecessorReleasedSkillTime == 0 || availability.LastReleasedSkillTime != pending.LatePredecessorReleasedSkillTime);
        var cooldownAdvanced = !observedDifferentRelease && releaseClockAdvancedOrUnavailable && skill is not null && skill.CooldownEndTime != 0 &&
            (pending.PreviousCooldownEndTime == 0 || unchecked((int)(skill.CooldownEndTime - pending.PreviousCooldownEndTime)) > 0);
        if (!preciseRelease && !cooldownAdvanced) return false;
        ActiveChainSource = pending.Node.Children.Count > 0 ? pending.Node : null;
        BeginChainTransition(pending, now, "release_confirmed", timeProvider);
        if (ChainTransition is { } transition && transition.AttemptId == pending.AttemptId)
            ChainTransition = transition with { Confirmed = true, ConfirmedReleasedSkillTime = availability.LastReleasedSkillTime };
        DropPendingAction();
        if (pending.Node.NodeKey.Contains('/'))
            _acceptedChainOpportunities[pending.Node.NodeKey] = pending.Node;
        else if (skill?.CooldownDuration == 0)
            _yieldedRoots.Add(pending.Node.NodeKey);
        return true;
    }

    /// <summary>Waits for a configured continuation to appear; it never makes a dark skill usable.</summary>
    public bool BeginChainTransition(QuickbarSkillPendingAction pending, DateTimeOffset now, string reason,
        TimeProvider? timeProvider = null)
    {
        if (pending.AttemptId <= _transitionProcessedAttemptId) return false;
        _transitionProcessedAttemptId = pending.AttemptId;
        if (pending.Node.Children.Count == 0) return false;
        var opportunityMs = pending.Node.Children
            .Select(child => child.ChainTimeMs is > 0 ? child.ChainTimeMs.Value : (int)MaximumChainTransitionWait.TotalMilliseconds).Max();
        var duration = TimeSpan.FromMilliseconds(Math.Min(MaximumChainTransitionWait.TotalMilliseconds, opportunityMs));
        // First key time is not execution time: retries may spend seconds behind
        // another animation. Start at the first actual release/CD evidence, once
        // per action, and cover all configured branches that may still open.
        EndChainTransition("new_stage");
        ChainTransition = new(pending.Node, now, now + duration, duration, pending.AttemptId, reason,
            pending.LatePredecessorReleasedSkillTime != 0 ? pending.LatePredecessorReleasedSkillTime : pending.PreviousReleasedSkillTime);
        _transitionTimeProvider = timeProvider;
        _transitionStartedTimestamp = timeProvider?.GetTimestamp() ?? 0;
        return true;
    }

    public bool IsChainTransitionWaiting(DateTimeOffset now) => ChainTransition is { } transition &&
        (_transitionTimeProvider is not null
            ? _transitionTimeProvider.GetElapsedTime(_transitionStartedTimestamp) < transition.WaitDuration
            : now < transition.Deadline);

    public void EndChainTransition(string reason)
    {
        if (ChainTransition is not null) LastChainTransitionEndReason = reason;
        ChainTransition = null;
        _transitionTimeProvider = null;
    }

    public void ObserveChainTransition(SkillAvailabilitySnapshot availability, DateTimeOffset now)
    {
        if (ChainTransition is not { } transition) return;
        if (!transition.Confirmed && PendingAction?.AttemptId == transition.AttemptId &&
            TryObserveLatePredecessorRelease(availability))
        {
            // The parent actor record can arrive after its genuinely open child
            // has already entered CD. It is not evidence of the child's release.
            transition = transition with { PreviousReleasedSkillTime = availability.LastReleasedSkillTime };
            ChainTransition = transition;
        }
        var referenceTime = transition.Confirmed ? transition.ConfirmedReleasedSkillTime : transition.PreviousReleasedSkillTime;
        if (availability.LastReleasedSkillTime != referenceTime && availability.LastReleasedSkillId != 0 &&
            availability.LastReleasedSkillId != transition.Source.SkillId)
        {
            EndChainTransition("different_release");
            if (PendingAction?.AttemptId == transition.AttemptId) DropPendingAction();
            ActiveChainSource = null;
        }
        else if (!IsChainTransitionWaiting(now)) EndChainTransition("expired");
    }

    public bool TryObserveLatePredecessorRelease(SkillAvailabilitySnapshot availability)
    {
        if (PendingAction is not { AllowedLatePredecessorSkillId: not 0 } pending ||
            availability.LastReleasedSkillId != pending.AllowedLatePredecessorSkillId ||
            availability.LastReleasedSkillTime == 0 || availability.LastReleasedSkillTime == pending.PreviousReleasedSkillTime)
            return false;
        if (pending.LatePredecessorReleasedSkillTime != 0)
            return availability.LastReleasedSkillTime == pending.LatePredecessorReleasedSkillTime;
        PendingAction = pending with { LatePredecessorReleasedSkillTime = availability.LastReleasedSkillTime };
        return true;
    }

    public void StopPendingRetries() { if (PendingAction is { } pending) PendingAction = pending with { RetryStopped = true }; }

    public void RejectAction(DateTimeOffset now, TimeSpan retryDelay)
    {
        if (PendingAction is not { } pending) return;
        _retryAfter[pending.Node.NodeKey] = now + retryDelay;
        if (_pendingTimeProvider is { } timeProvider)
            _monotonicRetryAfter[pending.Node.NodeKey] = new(timeProvider, timeProvider.GetTimestamp(), retryDelay);
        else _monotonicRetryAfter.Remove(pending.Node.NodeKey);
        DropPendingAction();
        // An unaccepted key does not advance (or erase) its previously confirmed predecessor.
    }

    public bool IsActionExpired(DateTimeOffset now) => PendingAction is { } pending &&
        (_pendingTimeProvider is not null
            ? _pendingTimeProvider.GetElapsedTime(_pendingStartedTimestamp) >= _pendingConfirmationTimeout
            : now >= pending.Deadline);

    public void DropPendingAction()
    {
        PendingAction = null;
        _pendingTimeProvider = null;
        _pendingStartedTimestamp = 0;
        _pendingLastAttemptTimestamp = 0;
        _pendingConfirmationTimeout = TimeSpan.Zero;
    }

    public void SuspendInputAttempts(bool preserveAttackWeave = false)
    {
        if (!preserveAttackWeave) AttackWeave.Reset();
        if (PendingAction is { } pending) _transitionProcessedAttemptId = Math.Max(_transitionProcessedAttemptId, pending.AttemptId);
        DropPendingAction();
        _retryAfter.Clear();
        _monotonicRetryAfter.Clear();
        EndChainTransition("maintenance");
    }

    public bool PendingIdentityMatches(SkillAvailabilitySnapshot availability)
    {
        if (PendingAction is not { } pending) return false;
        var binding = availability.BindingSlots?.FirstOrDefault(slot => slot.Bar == pending.Node.Bar && slot.Slot == pending.Node.Slot);
        if (binding is not null)
            return binding.ContentType == 21 && binding.BaseSkillId == pending.Node.BaseSkillId && binding.EffectiveSkillId == pending.Node.SkillId;
        return QuickbarSkillReleasePriority.GetMatchingSlot(pending.Node, availability) is not null;
    }

    private void ObserveAcceptedOpportunities(SkillAvailabilitySnapshot availability)
    {
        foreach (var pair in _acceptedChainOpportunities.ToArray())
            if (QuickbarSkillReleasePriority.GetMatchingSlot(pair.Value, availability) is not { CanUse: true })
                _acceptedChainOpportunities.Remove(pair.Key);
    }

    private sealed record RetryHold(TimeProvider TimeProvider, long StartedTimestamp, TimeSpan Duration);
}

public sealed record QuickbarSkillPendingAction(
    QuickbarSkillNode Node,
    uint PreviousCooldownEndTime,
    uint PreviousReleasedSkillTime,
    DateTimeOffset StartedAt,
    DateTimeOffset Deadline,
    DateTimeOffset LastAttemptAt,
    int AttemptCount,
    long AttemptId = 0,
    bool RetryStopped = false,
    uint AllowedLatePredecessorSkillId = 0,
    uint LatePredecessorReleasedSkillTime = 0,
    bool IsClockBootstrap = false);

public sealed record QuickbarSkillChainTransition(
    QuickbarSkillNode Source,
    DateTimeOffset StartedAt,
    DateTimeOffset Deadline,
    TimeSpan WaitDuration,
    long AttemptId,
    string Reason,
    uint PreviousReleasedSkillTime,
    bool Confirmed = false,
    uint ConfirmedReleasedSkillTime = 0);
