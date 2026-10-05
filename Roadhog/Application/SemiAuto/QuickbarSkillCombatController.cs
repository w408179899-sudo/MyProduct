using Roadhog.Application.Workers;
using Roadhog.Core.Accounts;
using Roadhog.Core.Api;
using Roadhog.Core.Diagnostics;
using Roadhog.Core.Input;
using Roadhog.Core.Model;

namespace Roadhog.Application.SemiAuto;

/// <summary>Serial, bounded attack-skill executor. Maintenance remains in the existing combat controller.</summary>
public sealed partial class QuickbarSkillCombatController
{
    private readonly IKeyboardInput _keyboard;
    private readonly TimeProvider _timeProvider;
    public static readonly TimeSpan MinimumConfirmationTimeout = TimeSpan.FromSeconds(8);
    public static readonly TimeSpan TargetTickInterval = TimeSpan.FromMilliseconds(80);

    public QuickbarSkillCombatController(IKeyboardInput keyboard, TimeProvider? timeProvider = null)
    {
        _keyboard = keyboard;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public Task<TimeSpan> TickAsync(AccountWorkerContext context, QuickbarSkillPlan plan,
        QuickbarSkillCombatState state, LockedTargetSnapshot target,
        Func<IReadOnlyList<SkillSnapshot>, IReadOnlySet<uint>>? ordinaryReadiness = null,
        Func<SkillSnapshot, SemiAutoSkillCooldownReadiness>? cooldownReadiness = null,
        Func<Task<LockedTargetSnapshot>>? readTargetBeforePress = null,
        Func<SkillAvailabilityCombatSnapshot, bool>? allowCombatSnapshot = null,
        Func<SkillSnapshot, SkillAvailabilitySnapshot, SemiAutoSkillCooldownReadiness>? availabilityCooldownReadiness = null,
        Func<bool>? isCooldownClockCalibrated = null,
        Func<Task<IReadOnlySet<uint>>>? readSuppressedRootSkillIds = null,
        Func<QuickbarSkillNode, Task>? onSkillPressed = null,
        Func<Task<IReadOnlySet<uint>>>? readSuppressedSkillIds = null)
    {
        if (context.Snapshots is not ISkillAvailabilitySnapshotReader availability)
            throw new InvalidOperationException("技能栏可用模式需要技能可用状态读取接口。");
        return TickAsync(plan, state, target, availability,
            async ids => (await context.Snapshots.ReadSkillsAsync(ids).ConfigureAwait(false)).Value,
            context.Config.ScriptSettings?.SemiAuto ?? new(), context.Logger, context.StopToken,
            readTargetBeforePress ?? (async () => (await context.Snapshots.ReadLockedTargetAsync().ConfigureAwait(false)).Value),
            ordinaryReadiness, cooldownReadiness, allowCombatSnapshot, availabilityCooldownReadiness, isCooldownClockCalibrated,
            readSuppressedRootSkillIds, onSkillPressed, readSuppressedSkillIds);
    }

    /// <summary>Narrow mockable orchestration seam; every read returns an official published value.</summary>
    public async Task<TimeSpan> TickAsync(
        QuickbarSkillPlan plan,
        QuickbarSkillCombatState state,
        LockedTargetSnapshot target,
        ISkillAvailabilitySnapshotReader availabilityReader,
        Func<IReadOnlyCollection<uint>, Task<IReadOnlyList<SkillSnapshot>>> readSkills,
        SemiAutoScriptSettings settings,
        IRoadhogLogger? logger = null,
        CancellationToken cancellationToken = default,
        Func<Task<LockedTargetSnapshot>>? readTargetBeforePress = null,
        Func<IReadOnlyList<SkillSnapshot>, IReadOnlySet<uint>>? ordinaryReadiness = null,
        Func<SkillSnapshot, SemiAutoSkillCooldownReadiness>? cooldownReadiness = null,
        Func<SkillAvailabilityCombatSnapshot, bool>? allowCombatSnapshot = null,
        Func<SkillSnapshot, SkillAvailabilitySnapshot, SemiAutoSkillCooldownReadiness>? availabilityCooldownReadiness = null,
        Func<bool>? isCooldownClockCalibrated = null,
        Func<Task<IReadOnlySet<uint>>>? readSuppressedRootSkillIds = null,
        Func<QuickbarSkillNode, Task>? onSkillPressed = null,
        Func<Task<IReadOnlySet<uint>>>? readSuppressedSkillIds = null)
    {
        var tickStarted = _timeProvider.GetTimestamp();
        var attemptTickStartedAt = _timeProvider.GetUtcNow();
        TimeSpan RemainingDelay()
        {
            var remaining = TargetTickInterval - _timeProvider.GetElapsedTime(tickStarted);
            return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
        }
        if (cancellationToken.IsCancellationRequested)
        {
            state.Reset();
            cancellationToken.ThrowIfCancellationRequested();
        }
        if (state.TickInFlight) throw new InvalidOperationException("技能栏释放循环不能并发执行。");
        state.TickInFlight = true;
        state.YieldToWorker = false;
        state.SupportsCombatState = false;
        try
        {
            if (!settings.AttackWeaveEnabled) state.AttackWeave.Reset();
            else if (state.AttackWeave.TryResetAfterIdle(_timeProvider))
                logger?.Info("quickbar_skill.attack_weave.idle_reset");
            void ObserveClockCalibration()
            {
                if (isCooldownClockCalibrated is null) return;
                state.ClockBootstrap.ObserveCalibration(isCooldownClockCalibrated());
                if (!state.ClockBootstrap.IsCompleted || state.ClockBootstrapCompletionLogged ||
                    state.ClockBootstrap.AttemptedCandidateCount == 0) return;
                state.ClockBootstrapCompletionLogged = true;
                logger?.Info("quickbar_skill.clock.bootstrap.completed", new Dictionary<string, object?>
                {
                    ["reason"] = state.ClockBootstrap.CompletionReason,
                    ["candidateCount"] = state.ClockBootstrap.AttemptedCandidateCount
                });
            }
            void FinishClockCandidate(QuickbarSkillNode node, string reason)
            {
                if (state.ClockBootstrap.CurrentCandidate?.NodeKey != node.NodeKey) return;
                state.ClockBootstrap.FinishCandidate(node, reason);
                logger?.Info("quickbar_skill.clock.bootstrap.candidate.finished", new Dictionary<string, object?>
                {
                    ["skillId"] = node.SkillId, ["node"] = node.NodeKey, ["reason"] = reason
                });
                ObserveClockCalibration();
            }
            void LogTransitionChange(QuickbarSkillChainTransition? previous)
            {
                var current = state.ChainTransition;
                if (previous?.AttemptId == current?.AttemptId) return;
                if (previous is not null)
                    logger?.Info("quickbar_skill.chain.wait.finished", new Dictionary<string, object?>
                    {
                        ["skillId"] = previous.Source.SkillId, ["node"] = previous.Source.NodeKey,
                        ["attemptId"] = previous.AttemptId, ["reason"] = state.LastChainTransitionEndReason
                    });
                if (current is not null)
                    logger?.Info("quickbar_skill.chain.wait.started", new Dictionary<string, object?>
                    {
                        ["skillId"] = current.Source.SkillId, ["node"] = current.Source.NodeKey,
                        ["attemptId"] = current.AttemptId, ["reason"] = current.Reason,
                        ["durationMs"] = current.WaitDuration.TotalMilliseconds, ["deadline"] = current.Deadline
                    });
            }
            if (!target.IsMonsterAlive || (!plan.HasCombatActions && !state.AttackWeave.IsWaiting &&
                (!settings.AttackWeaveEnabled || !state.AttackWeave.HasPendingAttempts)))
            {
                var previous = state.ChainTransition;
                state.Reset();
                LogTransitionChange(previous);
                state.YieldToWorker = true;
                return RemainingDelay();
            }

            bool Observe(SkillAvailabilitySnapshot value)
            {
                state.SupportsCombatState = value.CombatState is not null;
                if (value.CombatState is { } combat &&
                    (!combat.IsAlive || combat.TargetEntityId != target.TargetEntityId ||
                     (combat.TargetServerObjectId != 0 && target.ServerObjectId != 0 && combat.TargetServerObjectId != target.ServerObjectId)))
                {
                    var previous = state.ChainTransition;
                    state.Reset();
                    LogTransitionChange(previous);
                    state.SupportsCombatState = true;
                    state.YieldToWorker = true;
                    logger?.Info("quickbar_skill.combat.yield");
                    return false;
                }
                if (value.CombatState is { } maintenanceCombat && allowCombatSnapshot?.Invoke(maintenanceCombat) == false)
                {
                    var previous = state.ChainTransition;
                    // Maintenance owns input but does not make an already accepted,
                    // still-lit chain opportunity a new opportunity.
                    state.SuspendInputAttempts();
                    LogTransitionChange(previous);
                    state.YieldToWorker = true;
                    logger?.Info("quickbar_skill.combat.yield");
                    return false;
                }
                var previousTransition = state.ChainTransition;
                var scopeChanged = state.ObserveScope(target, value);
                LogTransitionChange(previousTransition);
                if (scopeChanged || value.Page != plan.Page)
                {
                    if (value.Page != plan.Page) state.AttackWeave.Reset();
                    state.YieldToWorker = true;
                    logger?.Info("quickbar_skill.scope.changed");
                    return false;
                }
                return true;
            }

            var published = await availabilityReader.ReadSkillAvailabilityAsync(
                state.LastAvailabilityVersion, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            state.LastAvailabilityVersion = published.Version;
            var availability = published.Value;
            if (!Observe(availability)) return RemainingDelay();
            if (settings.AttackWeaveEnabled)
                ObserveAttackWeaveCooldowns(state, Array.Empty<SkillSnapshot>(), _timeProvider, settings, logger);
            if (settings.AttackWeaveEnabled && state.AttackWeave.IsWaiting &&
                state.AttackWeave.PendingSkillIds is { Count: > 0 } waitingIds)
            {
                // A later stage can enter CD during the pair's wait. Preserve its
                // evidence for the next pair while keeping this tick input-free.
                var waitingSkills = await readSkills(waitingIds).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                ObserveAttackWeaveCooldowns(state, waitingSkills, _timeProvider, settings, logger);
            }
            if (settings.AttackWeaveEnabled && await HandleAttackWeaveAsync(state, target, availability, availabilityReader, settings,
                    Observe, readTargetBeforePress, logger, cancellationToken).ConfigureAwait(false))
                return RemainingDelay();

            var observedSkills = new Dictionary<uint, SkillSnapshot>();
            var skillReadGeneration = 0;
            IReadOnlySet<uint>? ordinaryReadyIds = null;
            IReadOnlyList<SkillSnapshot>? rootsObserved = null;
            IReadOnlySet<uint>? ordinaryProposedIds = null;
            var coolingIds = new HashSet<uint>();
            IReadOnlySet<uint>? suppressedRootSkillIds = null;
            IReadOnlySet<uint>? suppressedSkillIds = null;
            async Task RefreshRootSuppression()
            {
                if (readSuppressedRootSkillIds is not null)
                {
                    suppressedRootSkillIds = await readSuppressedRootSkillIds().ConfigureAwait(false);
                    cancellationToken.ThrowIfCancellationRequested();
                }
                if (readSuppressedSkillIds is not null)
                {
                    suppressedSkillIds = await readSuppressedSkillIds().ConfigureAwait(false);
                    cancellationToken.ThrowIfCancellationRequested();
                }
                if (state.PendingAction is { } pending &&
                    (suppressedSkillIds?.Contains(pending.Node.SkillId) == true ||
                     (!pending.Node.NodeKey.Contains('/') && suppressedRootSkillIds?.Contains(pending.Node.SkillId) == true)))
                {
                    // A target status or missing pet stops retries; confirmation
                    // requires the existing actor/CD evidence.
                    state.StopPendingRetries();
                    if (pending.IsClockBootstrap) FinishClockCandidate(pending.Node,
                        suppressedSkillIds?.Contains(pending.Node.SkillId) == true ? "skill_suppressed" : "target_dot_active");
                }
                if (state.ChainTransition is { } transition &&
                    transition.Source.Children.All(child => suppressedSkillIds?.Contains(child.SkillId) == true))
                {
                    state.EndChainTransition("children_suppressed");
                    LogTransitionChange(transition);
                }
            }
            var transitionsReadThisTick = new HashSet<long>();
            SemiAutoSkillCooldownReadiness? Readiness(SkillSnapshot skill) =>
                availabilityCooldownReadiness?.Invoke(skill, availability) ?? cooldownReadiness?.Invoke(skill);
            void RecomputeEligibility()
            {
                coolingIds.Clear();
                foreach (var skill in observedSkills.Values)
                    if (Readiness(skill) == SemiAutoSkillCooldownReadiness.CoolingDown)
                        coolingIds.Add(skill.SkillId);
                if (rootsObserved is not null && ordinaryProposedIds is not null)
                    ordinaryReadyIds = FilterOrdinaryReadyIds(rootsObserved, ordinaryProposedIds, Readiness);
            }
            void ObserveSkills(IReadOnlyList<SkillSnapshot> skills, bool rootsRead)
            {
                foreach (var skill in skills)
                    observedSkills[skill.SkillId] = skill;
                if (rootsRead && ordinaryReadiness is not null)
                {
                    rootsObserved = skills;
                    ordinaryProposedIds = ordinaryReadiness(skills);
                }
                RecomputeEligibility();
                if (settings.AttackWeaveEnabled)
                    ObserveAttackWeaveCooldowns(state, skills, _timeProvider, settings, logger);
            }
            async Task Read(IReadOnlyCollection<uint> ids, bool rootsRead)
            {
                var requestedIds = settings.AttackWeaveEnabled && state.AttackWeave.HasPendingAttempts
                    ? ids.Concat(state.AttackWeave.PendingSkillIds).Distinct().ToArray()
                    : ids;
                var skills = await readSkills(requestedIds).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                skillReadGeneration++;
                ObserveSkills(skills, rootsRead);
            }
            void ObservePending()
            {
                ObserveClockCalibration();
                var previous = state.ChainTransition;
                state.ObserveChainTransition(availability, _timeProvider.GetUtcNow());
                LogTransitionChange(previous);
                if (state.PendingAction is not { } pending) return;
                var latePredecessorRelease = state.TryObserveLatePredecessorRelease(availability);
                pending = state.PendingAction!;
                var now = _timeProvider.GetUtcNow();
                previous = state.ChainTransition;
                if (state.TryConfirmAction(availability, observedSkills.Values.ToArray(), now, _timeProvider))
                {
                    logger?.Info("quickbar_skill.release.confirmed", Fields(pending.Node));
                    if (pending.IsClockBootstrap) FinishClockCandidate(pending.Node, "release_confirmed");
                }
                else if ((!pending.IsClockBootstrap && state.IsActionExpired(now)) ||
                    pending.AttemptCount >= QuickbarSkillCombatState.MaximumUnconfirmedAttempts)
                {
                    state.RejectAction(now, TimeSpan.FromMilliseconds(Math.Clamp(settings.PostPressSuppressMs, 500, 10000)));
                    if (pending.IsClockBootstrap) FinishClockCandidate(pending.Node, "attempts_exhausted");
                    logger?.Warn("quickbar_skill.release.unconfirmed", Fields(pending.Node));
                }
                else if (coolingIds.Contains(pending.Node.SkillId))
                {
                    var skill = observedSkills.GetValueOrDefault(pending.Node.SkillId);
                    var cooldownAdvanced = skill is not null && skill.CooldownEndTime != 0 &&
                        (pending.PreviousCooldownEndTime == 0 || unchecked((int)(skill.CooldownEndTime - pending.PreviousCooldownEndTime)) > 0);
                    var differentRelease = availability.LastReleasedSkillTime != pending.PreviousReleasedSkillTime &&
                        availability.LastReleasedSkillId != 0 && availability.LastReleasedSkillId != pending.Node.SkillId && !latePredecessorRelease;
                    // Real CD progression may precede the actor's release record.
                    // It stops retries and protects the handoff without claiming
                    // that this requested skill has been proven to execute.
                    if (cooldownAdvanced && !differentRelease)
                    {
                        state.BeginChainTransition(pending, now, "cooldown_started", _timeProvider);
                        state.StopPendingRetries();
                    }
                    else state.DropPendingAction();
                    if (pending.IsClockBootstrap) FinishClockCandidate(pending.Node,
                        cooldownAdvanced && !differentRelease ? "cooldown_started" : "cooling");
                }
                else if (!state.PendingIdentityMatches(availability))
                {
                    state.DropPendingAction();
                    if (pending.IsClockBootstrap) FinishClockCandidate(pending.Node, "binding_changed");
                }
                else if (pending.IsClockBootstrap &&
                    (isCooldownClockCalibrated?.Invoke() == true || state.ClockBootstrap.IsAttemptExpired(pending.Node)))
                {
                    FinishClockCandidate(pending.Node, isCooldownClockCalibrated?.Invoke() == true
                        ? "clock_calibrated" : "candidate_timeout");
                    state.DropPendingAction();
                }
                LogTransitionChange(previous);
            }
            async Task ObserveTransition()
            {
                var previous = state.ChainTransition;
                state.ObserveChainTransition(availability, _timeProvider.GetUtcNow());
                LogTransitionChange(previous);
                if (state.ChainTransition is not { } transition) return;
                if (transitionsReadThisTick.Add(transition.AttemptId))
                    await Read(transition.Source.Children.Select(child => child.SkillId).Distinct().ToArray(), false).ConfigureAwait(false);
                state.ObserveChainTransition(availability, _timeProvider.GetUtcNow());
                if (state.ChainTransition is null)
                {
                    LogTransitionChange(transition);
                    return;
                }
                if (transition.Source.Children.All(child => coolingIds.Contains(child.SkillId)))
                {
                    state.EndChainTransition("children_cooling");
                    LogTransitionChange(transition);
                }
            }
            QuickbarSkillReleaseDecision Select()
            {
                var decision = QuickbarSkillReleasePriority.SelectNext(
                    plan, state, availability, _timeProvider.GetUtcNow(), ordinaryReadyIds, coolingIds,
                    suppressedRootSkillIds, suppressedSkillIds);
                ObserveClockCalibration();
                if (isCooldownClockCalibrated is null || state.ClockBootstrap.IsCompleted || rootsObserved is null ||
                    decision.Kind is QuickbarSkillDecisionKind.PressChain or QuickbarSkillDecisionKind.WaitForChain ||
                    state.PendingAction is { IsClockBootstrap: false, RetryStopped: false }) return decision;
                var bootstrapSkills = suppressedRootSkillIds is { Count: > 0 } || suppressedSkillIds is { Count: > 0 }
                    ? rootsObserved.Where(skill => suppressedRootSkillIds?.Contains(skill.SkillId) != true &&
                        suppressedSkillIds?.Contains(skill.SkillId) != true).ToArray()
                    : rootsObserved;
                var candidate = state.ClockBootstrap.SelectCandidate(plan, availability, bootstrapSkills,
                    Readiness, isCooldownClockCalibrated());
                ObserveClockCalibration();
                if (candidate is null) return decision;
                if (state.PendingAction is { IsClockBootstrap: true } previous && previous.Node.NodeKey != candidate.NodeKey)
                {
                    FinishClockCandidate(previous.Node, "next_candidate_selected");
                    state.DropPendingAction();
                }
                if (state.IsRepeatBlocked(candidate, _timeProvider.GetUtcNow())) return QuickbarSkillReleaseDecision.None;
                var continuingProbe = state.PendingAction is { IsClockBootstrap: true } current &&
                    current.Node.NodeKey == candidate.NodeKey;
                var kind = !continuingProbe && Readiness(observedSkills[candidate.SkillId]) == SemiAutoSkillCooldownReadiness.Ready
                    ? QuickbarSkillDecisionKind.PressRoot : QuickbarSkillDecisionKind.PressClockBootstrap;
                return new(kind, candidate);
            }
            async Task<QuickbarSkillReleaseDecision> SelectWithRoots()
            {
                var decision = Select();
                if (decision.Kind is not (QuickbarSkillDecisionKind.PressChain or QuickbarSkillDecisionKind.WaitForChain) && ordinaryReadiness is not null)
                {
                    await Read(plan.SkillReadIds, true).ConfigureAwait(false);
                    ObservePending();
                    await ObserveTransition().ConfigureAwait(false);
                    decision = Select();
                }
                return decision;
            }

            if (state.PendingAction is { } pending)
            {
                await Read(new[] { pending.Node.SkillId }, false).ConfigureAwait(false);
                ObservePending();
            }
            else if (settings.AttackWeaveEnabled && state.AttackWeave.HasPendingAttempts)
                await Read(state.AttackWeave.PendingSkillIds, false).ConfigureAwait(false);
            await ObserveTransition().ConfigureAwait(false);
            if (settings.AttackWeaveEnabled && await HandleAttackWeaveAsync(state, target, availability, availabilityReader, settings,
                    Observe, readTargetBeforePress, logger, cancellationToken).ConfigureAwait(false))
                return RemainingDelay();
            await RefreshRootSuppression().ConfigureAwait(false);
            var decision = await SelectWithRoots().ConfigureAwait(false);
            if (settings.AttackWeaveEnabled && await HandleAttackWeaveAsync(state, target, availability, availabilityReader, settings,
                    Observe, readTargetBeforePress, logger, cancellationToken).ConfigureAwait(false))
                return RemainingDelay();

            // At most three selection changes and one finite key per tick. A CD
            // transition can hand over to another skill in this same tick, while
            // repeated display changes cannot keep the executor spinning forever.
            for (var selectionAttempt = 0; selectionAttempt < 3 && decision.Node is { } node; selectionAttempt++)
            {
                var selectingRoots = decision.Kind != QuickbarSkillDecisionKind.PressChain;
                if (selectingRoots && ordinaryReadiness is not null)
                    await Read(plan.SkillReadIds, true).ConfigureAwait(false);
                else if (!observedSkills.ContainsKey(node.SkillId))
                    await Read(new[] { node.SkillId }, false).ConfigureAwait(false);

                await RefreshRootSuppression().ConfigureAwait(false);
                // Legacy mock/provider capability uses its official target reader.
                // The new combined snapshot already contains the life and target guard.
                if (availability.CombatState is null && readTargetBeforePress is not null)
                {
                    var currentTarget = await readTargetBeforePress().ConfigureAwait(false);
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!SameTarget(target, currentTarget))
                    {
                        state.Reset();
                        state.YieldToWorker = true;
                        logger?.Info("quickbar_skill.target.changed_before_press");
                        return RemainingDelay();
                    }
                }
                var beforePress = await availabilityReader.ReadSkillAvailabilityAsync(
                    state.LastAvailabilityVersion, cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                state.LastAvailabilityVersion = beforePress.Version;
                availability = beforePress.Value;
                if (!Observe(availability)) return RemainingDelay();
                var guardedSkillReadGeneration = skillReadGeneration;
                RecomputeEligibility();
                ObservePending();
                await ObserveTransition().ConfigureAwait(false);
                if (settings.AttackWeaveEnabled && await HandleAttackWeaveAsync(state, target, availability, availabilityReader, settings,
                        Observe, readTargetBeforePress, logger, cancellationToken).ConfigureAwait(false))
                    return RemainingDelay();
                var rechecked = Select();
                if (rechecked.Kind is not (QuickbarSkillDecisionKind.PressChain or QuickbarSkillDecisionKind.WaitForChain) && ordinaryReadiness is not null && !selectingRoots)
                    rechecked = await SelectWithRoots().ConfigureAwait(false);
                if (settings.AttackWeaveEnabled && await HandleAttackWeaveAsync(state, target, availability, availabilityReader, settings,
                        Observe, readTargetBeforePress, logger, cancellationToken).ConfigureAwait(false))
                    return RemainingDelay();
                if (rechecked.Node?.NodeKey != node.NodeKey || rechecked.Kind != decision.Kind)
                {
                    decision = rechecked;
                    continue;
                }

                if (readSuppressedSkillIds is not null || skillReadGeneration != guardedSkillReadGeneration)
                {
                    // Confirmation can start a new transition whose child CD
                    // read runs after the previous guard. A pet may also vanish
                    // during preparation. Put the final official life/target
                    // guard after every such extra read, with no reads between
                    // that guard and the key for an unchanged candidate.
                    await RefreshRootSuppression().ConfigureAwait(false);
                    if (availability.CombatState is null && readTargetBeforePress is not null)
                    {
                        var currentTarget = await readTargetBeforePress().ConfigureAwait(false);
                        cancellationToken.ThrowIfCancellationRequested();
                        if (!SameTarget(target, currentTarget))
                        {
                            state.Reset();
                            state.YieldToWorker = true;
                            logger?.Info("quickbar_skill.target.changed_before_press");
                            return RemainingDelay();
                        }
                    }
                    var guarded = await availabilityReader.ReadSkillAvailabilityAsync(
                        state.LastAvailabilityVersion, cancellationToken).ConfigureAwait(false);
                    cancellationToken.ThrowIfCancellationRequested();
                    state.LastAvailabilityVersion = guarded.Version;
                    availability = guarded.Value;
                    if (!Observe(availability)) return RemainingDelay();
                    RecomputeEligibility();
                    ObservePending();
                    rechecked = Select();
                    if (rechecked.Node?.NodeKey != node.NodeKey || rechecked.Kind != decision.Kind)
                    {
                        decision = rechecked;
                        continue;
                    }
                }

                var isBootstrap = decision.Kind == QuickbarSkillDecisionKind.PressClockBootstrap;
                var firstBootstrapAttempt = isBootstrap && state.ClockBootstrap.CurrentCandidate?.NodeKey != node.NodeKey;
                if (isBootstrap && !state.ClockBootstrap.MarkAttemptStarted(node)) return RemainingDelay();
                var timeout = isBootstrap ? QuickbarSkillClockBootstrap.MaximumCandidateDuration :
                    TimeSpan.FromMilliseconds(Math.Clamp(settings.ConfirmTimeoutMs, (int)MinimumConfirmationTimeout.TotalMilliseconds, 30000));
                var previousTransition = state.ChainTransition;
                state.BeginAction(node, observedSkills.GetValueOrDefault(node.SkillId), availability, _timeProvider.GetUtcNow(), timeout,
                    attemptTickStartedAt, _timeProvider, isBootstrap, tickStarted);
                LogTransitionChange(previousTransition);
                if (firstBootstrapAttempt)
                    logger?.Info("quickbar_skill.clock.bootstrap.candidate.started", new Dictionary<string, object?>
                    {
                        ["skillId"] = node.SkillId, ["node"] = node.NodeKey,
                        ["candidateDurationMs"] = QuickbarSkillClockBootstrap.MaximumCandidateDuration.TotalMilliseconds,
                        ["totalBudgetMs"] = QuickbarSkillClockBootstrap.MaximumTotalDuration.TotalMilliseconds
                    });
                var result = await _keyboard.PressKeyAsync(node.Key,
                    TimeSpan.FromMilliseconds(Math.Clamp(settings.KeyHoldMs, 1, 30)), cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                if (!result.Success)
                {
                    state.RejectAction(_timeProvider.GetUtcNow(), TimeSpan.FromSeconds(1));
                    if (isBootstrap) FinishClockCandidate(node, "input_failed");
                    logger?.Warn("quickbar_skill.key.failed", Fields(node));
                }
                else
                {
                    if (settings.AttackWeaveEnabled)
                        state.AttackWeave.TrackMainPress(observedSkills.GetValueOrDefault(node.SkillId),
                            !isBootstrap || QuickbarSkillReleasePriority.GetMatchingSlot(node, availability) is { CanUse: true },
                            _timeProvider, timeout);
                    logger?.Info("quickbar_skill.key.pressed", Fields(node));
                    if (onSkillPressed is not null)
                        await onSkillPressed(node).ConfigureAwait(false);
                    cancellationToken.ThrowIfCancellationRequested();
                }
                return RemainingDelay();
            }
            return RemainingDelay();
        }
        catch (OperationCanceledException)
        {
            state.Reset();
            throw;
        }
        finally
        {
            state.TickInFlight = false;
        }
    }

    private static bool SameTarget(LockedTargetSnapshot expected, LockedTargetSnapshot current) =>
        current.IsMonsterAlive && current.TargetEntityId == expected.TargetEntityId &&
        (current.ServerObjectId == 0 || expected.ServerObjectId == 0 || current.ServerObjectId == expected.ServerObjectId);

    private static IReadOnlyDictionary<string, object?> Fields(QuickbarSkillNode node) => new Dictionary<string, object?>
    {
        ["skillId"] = node.SkillId,
        ["skillName"] = node.Name,
        ["key"] = node.Key,
        ["node"] = node.NodeKey
    };

    public static bool IsOrdinarySkill(SkillSnapshot skill) => string.IsNullOrWhiteSpace(skill.XmlCounterSkill) &&
        string.IsNullOrWhiteSpace(skill.XmlPrechainCategory) && string.IsNullOrWhiteSpace(skill.XmlTargetValidStatuses) &&
        string.IsNullOrWhiteSpace(skill.XmlSelfConditionStatuses) && !string.Equals(skill.XmlUltraTransfer, "1", StringComparison.Ordinal);

    private static IReadOnlySet<uint> FilterOrdinaryReadyIds(IReadOnlyList<SkillSnapshot> skills, IReadOnlySet<uint> proposed,
        Func<SkillSnapshot, SemiAutoSkillCooldownReadiness?> cooldownReadiness) =>
        skills.Where(IsOrdinarySkill).Where(skill => proposed.Contains(skill.SkillId) &&
            (cooldownReadiness(skill) is null or SemiAutoSkillCooldownReadiness.Ready))
            .Select(skill => skill.SkillId).ToHashSet();
}
