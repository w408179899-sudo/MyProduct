using Roadhog.Application.BagCleanup;
using Roadhog.Application.JumpAssist;
using Roadhog.Application.Team;
using Roadhog.Core.Model;

using Roadhog.Application.Radar;

namespace Roadhog.Application.StationaryCombat;

public sealed class StationaryCombatState
{
    public StationaryObstacleNavigationState ObstacleNavigation { get; } = new();
    public CombatJumpAssistSession? JumpAssist { get; set; }

    public StationaryCombatTopLevelState TopLevelState { get; private set; } = StationaryCombatTopLevelState.Normal;

    public StationaryCombatDeathRecoveryState DeathRecovery { get; } = new();

    public StationaryCombatLootAfterKillState LootAfterKill { get; } = new();

    public BagCleanupState BagCleanup { get; } = new();

    public StationaryCombatNoKillRecoveryState NoKillRecovery { get; } = new();

    public StationaryCombatPathCombatState PathCombat { get; } = new();

    public StationaryGatherState Gather { get; } = new();

    public NextTargetPreAimState NextTargetPreAim { get; } = new();

    public LeaderTacticalMarkState LeaderTacticalMark { get; } = new();

    public LocalDefenseThreatGuard LocalDefenseThreat { get; } = new();

    public bool CleanupReturnToCombatActive { get; private set; }

    public bool ReturningHome { get; set; }

    public bool Fighting { get; set; }

    public bool IsMovingForward { get; set; }

    public bool IsRightMouseDown { get; set; }

    public int ConsecutiveCameraTurnNoChangeCount { get; private set; }

    public ushort CurrentTargetEntityId { get; set; }

    public uint CurrentTargetServerObjectId { get; set; }

    public bool CurrentTargetIsMaintenanceDefense { get; set; }

    public bool CurrentTargetIsRevivePathClear { get; set; }

    public bool CurrentTargetIsGatherSafetyClear { get; set; }

    public bool CurrentTargetBypassesHomeLeash { get; set; }

    public bool CurrentTargetIsTacticalMark { get; set; }

    public ushort TeamLeaderProtectionTargetEntityId { get; private set; }

    public uint TeamLeaderProtectionTargetServerObjectId { get; private set; }

    public uint LocalCombatSideServerObjectId { get; set; }

    public uint LocalCombatSidePetServerObjectId { get; set; }

    public bool LocalCombatSideIdentityFresh { get; private set; }

    public int LocalCombatSidePetMissingConfirmations { get; private set; }

    public long LocalCombatSidePetLastMissingCaptureSequence { get; private set; }

    public SummonedPetRosterReadResult? LastSummonedPetRosterReadResult { get; private set; }

    public ushort CandidateEntityId { get; set; }

    public uint CandidateServerObjectId { get; set; }

    public ushort SmartPreAimHandoffEntityId { get; private set; }

    public uint SmartPreAimHandoffServerObjectId { get; private set; }

    public int SmartPreAimHandoffConsecutiveMissingSnapshots { get; private set; }

    public long SmartPreAimHandoffLastMissingCaptureSequence { get; private set; }

    public bool HasSmartPreAimHandoff =>
        SmartPreAimHandoffEntityId != 0 || SmartPreAimHandoffServerObjectId != 0;

    public ushort FacedCandidateEntityId { get; set; }

    public DateTimeOffset TargetStartedAt { get; private set; } = DateTimeOffset.MinValue;

    public ushort CombatApproachStuckEntityId { get; private set; }

    public uint CombatApproachStuckServerObjectId { get; private set; }

    public Vector3Snapshot? CombatApproachLastProgressPosition { get; private set; }

    public DateTimeOffset CombatApproachLastProgressAt { get; private set; } = DateTimeOffset.MinValue;

    public DateTimeOffset LastCombatApproachJumpAt { get; private set; } = DateTimeOffset.MinValue;

    public int CombatApproachJumpCount { get; private set; }

    public Vector3Snapshot? ReturnHomeLastProgressPosition { get; private set; }

    public DateTimeOffset ReturnHomeLastProgressAt { get; private set; } = DateTimeOffset.MinValue;

    public DateTimeOffset LastReturnHomeJumpAt { get; private set; } = DateTimeOffset.MinValue;

    public int ReturnHomeJumpCount { get; private set; }

    public bool NoTargetRestActive { get; private set; }

    public bool NoTargetRestExitPending { get; private set; }

    public DateTimeOffset LastNoTargetRestKeyAt { get; private set; } = DateTimeOffset.MinValue;

    public DateTimeOffset LastLootAfterKillFinishedAt { get; private set; } = DateTimeOffset.MinValue;

    public ushort CurrentTargetDamageEntityId { get; private set; }

    public uint CurrentTargetDamageServerObjectId { get; private set; }

    public uint CurrentTargetDamageBaselineHp { get; private set; }

    public DateTimeOffset CurrentTargetDamageObservedAt { get; private set; } = DateTimeOffset.MinValue;

    public bool CurrentTargetDamageObserved { get; private set; }

    public ushort CurrentTargetStallEntityId { get; private set; }

    public uint CurrentTargetStallServerObjectId { get; private set; }

    public uint CurrentTargetStallLastHp { get; private set; }

    public DateTimeOffset CurrentTargetStallLastProgressAt { get; private set; } = DateTimeOffset.MinValue;

    public bool CurrentTargetSoftRestartPending { get; private set; }

    public bool CurrentTargetSoftRestartAttempted { get; private set; }

    public bool CurrentTargetSoftRestartFaced { get; private set; }

    public DateTimeOffset CurrentTargetSoftRestartStartedAt { get; private set; } = DateTimeOffset.MinValue;

    public DateTimeOffset TemporaryTargetSwitchGuardUntil { get; private set; } = DateTimeOffset.MinValue;

    public ushort MissingCurrentTargetEntityId { get; private set; }

    public uint MissingCurrentTargetServerObjectId { get; private set; }

    public DateTimeOffset MissingCurrentTargetSince { get; private set; } = DateTimeOffset.MinValue;

    public ushort PendingTabCandidateEntityId { get; private set; }

    public uint PendingTabCandidateServerObjectId { get; private set; }

    public DateTimeOffset PendingTabVerifyUntil { get; private set; } = DateTimeOffset.MinValue;

    public ushort PendingTabPreviousLockedEntityId { get; private set; }

    public uint PendingTabPreviousLockedServerObjectId { get; private set; }

    public bool PendingTabCorpseNudged { get; private set; }

    public DateTimeOffset LastTabAt { get; set; }

    public DateTimeOffset LastWorldScanAt { get; set; }

    public DateTimeOffset LastGatherScanAt { get; set; }

    public DateTimeOffset LastLogAt { get; set; }

    public Dictionary<string, DateTimeOffset> LastActionLogAtByKey { get; } = new();

    public IReadOnlyList<WorldObjectSnapshot> CachedWorldObjects { get; set; } = Array.Empty<WorldObjectSnapshot>();

    public WorldObjectReadResult? LastWorldObjectReadResult { get; set; }

    internal object WorldObjectCommitSyncRoot { get; } = new();

    private long _worldObjectReadGeneration;
    private long _lastAcceptedWorldObjectReadOrder;
    private DateTimeOffset _localDefenseTransitionUnknownSince = DateTimeOffset.MinValue;
    private string _localDefenseTransitionUnknownPhase = string.Empty;
    private WorldObjectReadResult? _localDefenseExpiredReadResult;

    public long WorldObjectReadGeneration => Interlocked.Read(ref _worldObjectReadGeneration);

    public bool TryAcceptWorldObjectRead(long observationOrder, long stateGeneration)
    {
        if (observationOrder <= 0 || stateGeneration != WorldObjectReadGeneration)
        {
            return false;
        }

        while (true)
        {
            var accepted = Interlocked.Read(ref _lastAcceptedWorldObjectReadOrder);
            if (observationOrder <= accepted)
            {
                return false;
            }

            if (Interlocked.CompareExchange(
                    ref _lastAcceptedWorldObjectReadOrder,
                    observationOrder,
                    accepted) == accepted)
            {
                return stateGeneration == WorldObjectReadGeneration;
            }
        }
    }

    public DateTimeOffset MarkLocalDefenseTransitionUnknown(string phase, DateTimeOffset now)
    {
        TryMarkLocalDefenseTransitionUnknown(
            phase,
            now,
            WorldObjectReadGeneration,
            out var unknownSince);
        return unknownSince;
    }

    public bool TryMarkLocalDefenseTransitionUnknown(
        string phase,
        DateTimeOffset now,
        long expectedGeneration,
        out DateTimeOffset unknownSince)
    {
        lock (WorldObjectCommitSyncRoot)
        {
            if (expectedGeneration != WorldObjectReadGeneration)
            {
                unknownSince = DateTimeOffset.MinValue;
                return false;
            }

            if (_localDefenseTransitionUnknownSince == DateTimeOffset.MinValue ||
                !string.Equals(_localDefenseTransitionUnknownPhase, phase, StringComparison.Ordinal))
            {
                _localDefenseTransitionUnknownSince = now;
                _localDefenseTransitionUnknownPhase = phase;
            }

            unknownSince = _localDefenseTransitionUnknownSince;
            return true;
        }
    }

    public void ClearLocalDefenseTransitionUnknown(string? phase = null)
    {
        TryClearLocalDefenseTransitionUnknown(phase, WorldObjectReadGeneration);
    }

    public bool TryClearLocalDefenseTransitionUnknown(string? phase, long expectedGeneration)
    {
        lock (WorldObjectCommitSyncRoot)
        {
            if (expectedGeneration != WorldObjectReadGeneration)
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(phase) &&
                !string.Equals(_localDefenseTransitionUnknownPhase, phase, StringComparison.Ordinal))
            {
                return true;
            }

            _localDefenseTransitionUnknownSince = DateTimeOffset.MinValue;
            _localDefenseTransitionUnknownPhase = string.Empty;
            return true;
        }
    }

    public bool TryClearLocalDefenseExpiryEvidence(long expectedGeneration)
    {
        lock (WorldObjectCommitSyncRoot)
        {
            if (expectedGeneration != WorldObjectReadGeneration)
            {
                return false;
            }

            _localDefenseExpiredReadResult = null;
            return true;
        }
    }

    public bool TryMarkLocalDefenseExpired(
        WorldObjectReadResult readResult,
        long expectedGeneration)
    {
        lock (WorldObjectCommitSyncRoot)
        {
            if (expectedGeneration != WorldObjectReadGeneration)
            {
                return false;
            }

            _localDefenseExpiredReadResult = readResult;
            return true;
        }
    }

    public bool IsLocalDefenseExpiredReadResult(
        WorldObjectReadResult readResult,
        long expectedGeneration)
    {
        lock (WorldObjectCommitSyncRoot)
        {
            return expectedGeneration == WorldObjectReadGeneration &&
                   ReferenceEquals(_localDefenseExpiredReadResult, readResult);
        }
    }

    public GatherSnapshot? CachedGatherSnapshot { get; set; }

    public HashSet<ushort> IgnoredTargetEntityIds { get; } = new();

    public HashSet<uint> IgnoredTargetServerObjectIds { get; } = new();

    private readonly Dictionary<LootCorpseKey, DateTimeOffset> _attemptedLootCorpses = new();

    private readonly object _targetSelectionFilterSync = new();

    private readonly Dictionary<TemporaryTargetKey, DateTimeOffset> _temporaryTargetExclusions = new();

    public bool HasTemporaryTargetExclusions
    {
        get
        {
            lock (_targetSelectionFilterSync)
            {
                return _temporaryTargetExclusions.Count > 0;
            }
        }
    }

    public object? PathFollowPoller { get; set; }

    public string StationaryHomePathName { get; private set; } = string.Empty;

    public int StationaryHomePathPointCount { get; private set; }

    public Vector3Snapshot? StationaryHomeFromRevivePath { get; private set; }

    public bool StartupRecoveryChecked { get; private set; }

    public bool StartupRecoveryActive { get; private set; }

    public bool StartupTownReturnPending { get; private set; }

    public Vector3Snapshot? StartupTownReturnStartPosition { get; private set; }

    public DateTimeOffset StartupTownReturnStartedAt { get; private set; } = DateTimeOffset.MinValue;

    public string StartupRecoveryPathName { get; private set; } = string.Empty;

    public int StartupRecoveryPointIndex { get; private set; } = -1;

    public IReadOnlyList<Vector3Snapshot> StartupRecoveryPoints { get; private set; } = Array.Empty<Vector3Snapshot>();

    public int StartupRecoveryStuckPointIndex { get; private set; } = -1;

    public Vector3Snapshot? StartupRecoveryLastProgressPosition { get; private set; }

    public DateTimeOffset StartupRecoveryLastProgressAt { get; private set; } = DateTimeOffset.MinValue;

    public DateTimeOffset LastStartupRecoveryJumpAt { get; private set; } = DateTimeOffset.MinValue;

    public int StartupRecoveryJumpCount { get; private set; }

    public void ClearTarget()
    {
        var clearDisplacedTargetGuard = HasSmartPreAimHandoff;
        if (!clearDisplacedTargetGuard)
        {
            lock (NextTargetPreAim.SyncRoot)
            {
                clearDisplacedTargetGuard = NextTargetPreAim.DisplacedTargetGuardActive &&
                                            IsSameTarget(
                                                NextTargetPreAim.DisplacedTargetReplacementEntityId,
                                                NextTargetPreAim.DisplacedTargetReplacementServerObjectId,
                                                CurrentTargetEntityId,
                                                CurrentTargetServerObjectId);
            }
        }

        ClearSmartPreAimHandoff(clearDisplacedTargetGuard);
        ObstacleNavigation.ClearRoute();
        Fighting = false;
        CurrentTargetEntityId = 0;
        CurrentTargetServerObjectId = 0;
        CurrentTargetIsMaintenanceDefense = false;
        CurrentTargetIsRevivePathClear = false;
        CurrentTargetIsGatherSafetyClear = false;
        CurrentTargetBypassesHomeLeash = false;
        CurrentTargetIsTacticalMark = false;
        ClearTeamLeaderProtectionTarget();
        CandidateEntityId = 0;
        CandidateServerObjectId = 0;
        FacedCandidateEntityId = 0;
        TargetStartedAt = DateTimeOffset.MinValue;
        PathCombat.ClearCurrentTargetAnchor();
        ResetCombatApproachStuckTracking();
        ResetCurrentTargetDamageObservation();
        ResetCurrentTargetStallObservation();
        ResetCurrentTargetMissing();
        ClearPendingTabVerification();
        LeaderTacticalMark.Reset();
    }

    public void PrepareForFixedChannelCorrection(DateTimeOffset now)
    {
        ObstacleNavigation.Reset();
        ReturningHome = false;
        LootAfterKill.Reset();
        BagCleanup.Reset();
        NoKillRecovery.ResetWatch(now);
        CleanupReturnToCombatActive = false;
        PathCombat.Reset();
        Gather.Reset();
        CachedGatherSnapshot = null;
        CachedWorldObjects = Array.Empty<WorldObjectSnapshot>();
        LastWorldObjectReadResult = null;
        LastGatherScanAt = DateTimeOffset.MinValue;
        LastWorldScanAt = DateTimeOffset.MinValue;
        ClearNoTargetRest();
        ClearStartupRecovery();
        InvalidateLocalDefenseReads();
        ClearTarget();
        MarkLocalCombatSideIdentityUnavailable();
        ResetReturnHomeStuckTracking();
        IgnoredTargetEntityIds.Clear();
        IgnoredTargetServerObjectIds.Clear();
        lock (_targetSelectionFilterSync)
        {
            _temporaryTargetExclusions.Clear();
        }
    }

    public void EnterDeathRecovery(DateTimeOffset now)
    {
        ObstacleNavigation.ClearRoute();
        TopLevelState = StationaryCombatTopLevelState.DeathRecovery;
        ReturningHome = false;
        LootAfterKill.Reset();
        BagCleanup.Reset();
        NoKillRecovery.ResetWatch(now);
        CleanupReturnToCombatActive = false;
        PathCombat.Reset();
        Gather.Reset();
        CachedGatherSnapshot = null;
        LastGatherScanAt = DateTimeOffset.MinValue;
        ClearNoTargetRest();
        ClearStartupRecovery();
        InvalidateLocalDefenseReads();
        // A player death is a hard lifetime boundary for both the local actor
        // and its summoned pet. Never let a roster captured before death prove
        // that the post-revive player still has the same pet.
        ClearLocalCombatSideIdentity(identityFresh: false);
        ClearTarget();
        DeathRecovery.Start(now);
    }

    public void InvalidateLocalDefenseReads()
    {
        lock (WorldObjectCommitSyncRoot)
        {
            Interlocked.Increment(ref _worldObjectReadGeneration);
            LocalDefenseThreat.Clear();
            LastWorldObjectReadResult = null;
            CachedWorldObjects = Array.Empty<WorldObjectSnapshot>();
            LastWorldScanAt = DateTimeOffset.MinValue;
            _localDefenseTransitionUnknownSince = DateTimeOffset.MinValue;
            _localDefenseTransitionUnknownPhase = string.Empty;
            _localDefenseExpiredReadResult = null;
        }
    }

    public void MarkLocalCombatSideIdentityUnavailable()
    {
        LocalCombatSidePetMissingConfirmations = 0;
        LocalCombatSideIdentityFresh = false;
    }

    public void ClearLocalCombatSideIdentity(bool identityFresh)
    {
        LocalCombatSideServerObjectId = 0;
        LocalCombatSidePetServerObjectId = 0;
        LocalCombatSidePetMissingConfirmations = 0;
        LocalCombatSidePetLastMissingCaptureSequence = 0;
        LastSummonedPetRosterReadResult = null;
        LocalCombatSideIdentityFresh = identityFresh;
    }

    public void MarkLocalCombatSideWithoutPet()
    {
        LocalCombatSidePetServerObjectId = 0;
        LocalCombatSidePetMissingConfirmations = 0;
        LocalCombatSidePetLastMissingCaptureSequence = 0;
        LastSummonedPetRosterReadResult = null;
        LocalCombatSideIdentityFresh = true;
    }

    public void ApplyLocalCombatSideRoster(
        SummonedPetRosterReadResult readResult,
        int requiredPetMissingConfirmations)
    {
        LastSummonedPetRosterReadResult = readResult;
        var presence = readResult.ResolveLocalPetPresence();
        var localServerObjectId = readResult.Fields.LocalServerObjectId
            ? readResult.Snapshot?.LocalServerObjectId ?? 0
            : 0;
        if (localServerObjectId == 0)
        {
            MarkLocalCombatSideIdentityUnavailable();
            return;
        }

        LocalCombatSideServerObjectId = localServerObjectId;
        if (presence.IsPresent)
        {
            LocalCombatSidePetServerObjectId = presence.ServerObjectId;
            LocalCombatSidePetMissingConfirmations = 0;
            LocalCombatSideIdentityFresh = true;
            return;
        }

        if (!presence.IsExplicitlyAbsent)
        {
            // Unknown interrupts a run of explicit negative captures. Retain
            // the last positive pet ID for threat matching, but do not let this
            // frame prove that the local-side threat disappeared.
            LocalCombatSidePetMissingConfirmations = 0;
            LocalCombatSideIdentityFresh = false;
            return;
        }

        if (LocalCombatSidePetServerObjectId == 0)
        {
            LocalCombatSidePetMissingConfirmations = 0;
            LocalCombatSideIdentityFresh = true;
            return;
        }

        if (presence.CaptureSequence <= 0 ||
            presence.CaptureSequence == LocalCombatSidePetLastMissingCaptureSequence)
        {
            LocalCombatSideIdentityFresh = false;
            return;
        }

        LocalCombatSidePetLastMissingCaptureSequence = presence.CaptureSequence;
        LocalCombatSidePetMissingConfirmations++;
        if (LocalCombatSidePetMissingConfirmations >= Math.Max(1, requiredPetMissingConfirmations))
        {
            LocalCombatSidePetServerObjectId = 0;
            LocalCombatSidePetMissingConfirmations = 0;
            LocalCombatSideIdentityFresh = true;
            return;
        }

        // A single successful roster without the previously confirmed pet can
        // still be a torn snapshot. Keep the ID for positive matches, but do not
        // allow this observation to prove that a threat disappeared.
        LocalCombatSideIdentityFresh = false;
    }

    public void ExitDeathRecovery()
    {
        TopLevelState = StationaryCombatTopLevelState.Normal;
        DeathRecovery.Reset();
    }

    public bool MarkCandidate(ushort entityId, DateTimeOffset now)
    {
        return MarkCandidate(entityId, 0, now);
    }

    public bool MarkCandidate(WorldObjectSnapshot target, DateTimeOffset now)
    {
        return MarkCandidate(target.EntityId, target.ServerObjectId, now);
    }

    public bool MarkCandidate(ushort entityId, uint serverObjectId, DateTimeOffset now)
    {
        ClearNoTargetRest();
        var changed = !IsSameTarget(CandidateEntityId, CandidateServerObjectId, entityId, serverObjectId);
        CandidateEntityId = entityId;
        CandidateServerObjectId = serverObjectId;
        if (changed || TargetStartedAt == DateTimeOffset.MinValue)
        {
            TargetStartedAt = now;
        }

        if (changed)
        {
            ResetCombatApproachStuckTracking();
            ResetReturnHomeStuckTracking();
        }

        return changed;
    }

    public bool StartSmartPreAimHandoff(ushort entityId, uint serverObjectId)
    {
        if (entityId == 0 && serverObjectId == 0)
        {
            return false;
        }

        var changed = !IsSameTarget(
            SmartPreAimHandoffEntityId,
            SmartPreAimHandoffServerObjectId,
            entityId,
            serverObjectId);
        SmartPreAimHandoffEntityId = entityId;
        SmartPreAimHandoffServerObjectId = serverObjectId;
        if (changed)
        {
            SmartPreAimHandoffConsecutiveMissingSnapshots = 0;
            SmartPreAimHandoffLastMissingCaptureSequence = 0;
        }

        return changed;
    }

    public bool IsSmartPreAimHandoffTarget(ushort entityId, uint serverObjectId)
    {
        return HasSmartPreAimHandoff &&
               IsSameTarget(
                   SmartPreAimHandoffEntityId,
                   SmartPreAimHandoffServerObjectId,
                   entityId,
                   serverObjectId);
    }

    public int MarkSmartPreAimHandoffMissing(long captureSequence)
    {
        if (captureSequence == 0 || captureSequence == SmartPreAimHandoffLastMissingCaptureSequence)
        {
            return SmartPreAimHandoffConsecutiveMissingSnapshots;
        }

        SmartPreAimHandoffLastMissingCaptureSequence = captureSequence;
        return ++SmartPreAimHandoffConsecutiveMissingSnapshots;
    }

    public void ResetSmartPreAimHandoffMissing()
    {
        SmartPreAimHandoffConsecutiveMissingSnapshots = 0;
        SmartPreAimHandoffLastMissingCaptureSequence = 0;
    }

    public void ClearSmartPreAimHandoff(bool clearDisplacedTargetGuard)
    {
        if (clearDisplacedTargetGuard)
        {
            lock (NextTargetPreAim.SyncRoot)
            {
                NextTargetPreAim.ClearDisplacedTargetGuard();
            }
        }

        SmartPreAimHandoffEntityId = 0;
        SmartPreAimHandoffServerObjectId = 0;
        SmartPreAimHandoffConsecutiveMissingSnapshots = 0;
        SmartPreAimHandoffLastMissingCaptureSequence = 0;
    }

    public void RefreshCurrentTargetTimeout(DateTimeOffset now)
    {
        if (CurrentTargetEntityId == 0 && CurrentTargetServerObjectId == 0)
        {
            return;
        }

        TargetStartedAt = now;
    }

    public bool IsCombatApproachStuckTrackingTarget(ushort entityId, uint serverObjectId)
    {
        return IsSameTarget(CombatApproachStuckEntityId, CombatApproachStuckServerObjectId, entityId, serverObjectId);
    }

    public void MarkCombatApproachProgress(ushort entityId, uint serverObjectId, Vector3Snapshot position, DateTimeOffset now)
    {
        CombatApproachStuckEntityId = entityId;
        CombatApproachStuckServerObjectId = serverObjectId;
        CombatApproachLastProgressPosition = position;
        CombatApproachLastProgressAt = now;
    }

    public void MarkCombatApproachJump(DateTimeOffset now)
    {
        LastCombatApproachJumpAt = now;
        CombatApproachJumpCount++;
    }

    public void ResetCombatApproachStuckTracking()
    {
        CombatApproachStuckEntityId = 0;
        CombatApproachStuckServerObjectId = 0;
        CombatApproachLastProgressPosition = null;
        CombatApproachLastProgressAt = DateTimeOffset.MinValue;
        LastCombatApproachJumpAt = DateTimeOffset.MinValue;
        CombatApproachJumpCount = 0;
    }

    public void MarkReturnHomeProgress(Vector3Snapshot position, DateTimeOffset now)
    {
        ReturnHomeLastProgressPosition = position;
        ReturnHomeLastProgressAt = now;
    }

    public void MarkReturnHomeJump(DateTimeOffset now)
    {
        LastReturnHomeJumpAt = now;
        ReturnHomeJumpCount++;
    }

    public void ResetReturnHomeStuckTracking()
    {
        ReturnHomeLastProgressPosition = null;
        ReturnHomeLastProgressAt = DateTimeOffset.MinValue;
        LastReturnHomeJumpAt = DateTimeOffset.MinValue;
        ReturnHomeJumpCount = 0;
    }

    public bool ShouldPressNoTargetRestKey(DateTimeOffset now, TimeSpan interval)
    {
        return LastNoTargetRestKeyAt == DateTimeOffset.MinValue ||
               now - LastNoTargetRestKeyAt >= interval;
    }

    public void MarkNoTargetRestKey(DateTimeOffset now)
    {
        NoTargetRestActive = true;
        NoTargetRestExitPending = false;
        LastNoTargetRestKeyAt = now;
    }

    public void MarkNoTargetRestActive()
    {
        NoTargetRestActive = true;
        NoTargetRestExitPending = false;
    }

    public void MarkNoTargetRestExitPending()
    {
        NoTargetRestActive = true;
        NoTargetRestExitPending = true;
    }

    public void ClearNoTargetRest()
    {
        NoTargetRestActive = false;
        NoTargetRestExitPending = false;
        LastNoTargetRestKeyAt = DateTimeOffset.MinValue;
    }

    public int MarkCameraTurnNoChange()
    {
        ConsecutiveCameraTurnNoChangeCount++;
        return ConsecutiveCameraTurnNoChangeCount;
    }

    public void ResetCameraTurnNoChange()
    {
        ConsecutiveCameraTurnNoChangeCount = 0;
    }

    public void SetCurrentTarget(WorldObjectSnapshot target)
    {
        SetCurrentTarget(target.EntityId, target.ServerObjectId);
    }

    public void SetCurrentTarget(LockedTargetSnapshot target)
    {
        SetCurrentTarget(target.TargetEntityId, target.ServerObjectId);
    }

    public void SetCurrentTarget(ushort entityId, uint serverObjectId)
    {
        if (!IsSameTarget(CurrentTargetEntityId, CurrentTargetServerObjectId, entityId, serverObjectId))
        {
            ResetCurrentTargetDamageObservation();
            ResetCurrentTargetStallObservation();
        }

        CurrentTargetEntityId = entityId;
        CurrentTargetServerObjectId = serverObjectId;
    }

    public void MarkTeamLeaderProtectionTarget(WorldObjectSnapshot target)
    {
        TeamLeaderProtectionTargetEntityId = target.EntityId;
        TeamLeaderProtectionTargetServerObjectId = target.ServerObjectId;
    }

    public void ClearTeamLeaderProtectionTarget()
    {
        TeamLeaderProtectionTargetEntityId = 0;
        TeamLeaderProtectionTargetServerObjectId = 0;
    }

    public bool IsTeamLeaderProtectionTarget(WorldObjectSnapshot target)
    {
        return IsSameTarget(
            TeamLeaderProtectionTargetEntityId,
            TeamLeaderProtectionTargetServerObjectId,
            target.EntityId,
            target.ServerObjectId);
    }

    public bool IsTeamLeaderProtectionTarget(LockedTargetSnapshot target)
    {
        return IsSameTarget(
            TeamLeaderProtectionTargetEntityId,
            TeamLeaderProtectionTargetServerObjectId,
            target.TargetEntityId,
            target.ServerObjectId);
    }

    public void TrackCurrentTargetDamageObservation(LockedTargetSnapshot target, DateTimeOffset now)
    {
        if (!target.HasKnownHealth || target.CurrentHp == 0)
        {
            ResetCurrentTargetDamageObservation();
            return;
        }

        if (!IsSameTarget(
                CurrentTargetDamageEntityId,
                CurrentTargetDamageServerObjectId,
                target.TargetEntityId,
                target.ServerObjectId) ||
            CurrentTargetDamageObservedAt == DateTimeOffset.MinValue)
        {
            CurrentTargetDamageEntityId = target.TargetEntityId;
            CurrentTargetDamageServerObjectId = target.ServerObjectId;
            CurrentTargetDamageBaselineHp = target.CurrentHp;
            CurrentTargetDamageObservedAt = now;
            CurrentTargetDamageObserved = false;
            return;
        }

        if (target.CurrentHp < CurrentTargetDamageBaselineHp)
        {
            CurrentTargetDamageObserved = true;
            return;
        }

        if (!CurrentTargetDamageObserved && target.CurrentHp > CurrentTargetDamageBaselineHp)
        {
            CurrentTargetDamageBaselineHp = target.CurrentHp;
            CurrentTargetDamageObservedAt = now;
        }
    }

    public void ResetCurrentTargetDamageObservation()
    {
        CurrentTargetDamageEntityId = 0;
        CurrentTargetDamageServerObjectId = 0;
        CurrentTargetDamageBaselineHp = 0;
        CurrentTargetDamageObservedAt = DateTimeOffset.MinValue;
        CurrentTargetDamageObserved = false;
    }

    public void TrackCurrentTargetStallObservation(LockedTargetSnapshot target, DateTimeOffset now)
    {
        if (!target.HasKnownHealth || target.CurrentHp == 0)
        {
            ResetCurrentTargetStallObservation();
            return;
        }

        if (!IsSameTarget(
                CurrentTargetStallEntityId,
                CurrentTargetStallServerObjectId,
                target.TargetEntityId,
                target.ServerObjectId) ||
            CurrentTargetStallLastProgressAt == DateTimeOffset.MinValue)
        {
            CurrentTargetStallEntityId = target.TargetEntityId;
            CurrentTargetStallServerObjectId = target.ServerObjectId;
            CurrentTargetStallLastHp = target.CurrentHp;
            CurrentTargetStallLastProgressAt = now;
            CurrentTargetSoftRestartPending = false;
            CurrentTargetSoftRestartAttempted = false;
            CurrentTargetSoftRestartFaced = false;
            CurrentTargetSoftRestartStartedAt = DateTimeOffset.MinValue;
            return;
        }

        if (target.CurrentHp < CurrentTargetStallLastHp)
        {
            CurrentTargetStallLastProgressAt = now;
            CurrentTargetSoftRestartPending = false;
            CurrentTargetSoftRestartAttempted = false;
            CurrentTargetSoftRestartFaced = false;
            CurrentTargetSoftRestartStartedAt = DateTimeOffset.MinValue;
        }

        CurrentTargetStallLastHp = target.CurrentHp;
    }

    public bool TryStartCurrentTargetSoftRestart(DateTimeOffset now, TimeSpan timeout)
    {
        if (CurrentTargetSoftRestartPending)
        {
            return true;
        }

        if (CurrentTargetSoftRestartAttempted ||
            CurrentTargetStallLastProgressAt == DateTimeOffset.MinValue ||
            now - CurrentTargetStallLastProgressAt < timeout)
        {
            return false;
        }

        CurrentTargetSoftRestartPending = true;
        CurrentTargetSoftRestartFaced = false;
        CurrentTargetSoftRestartStartedAt = now;
        return true;
    }

    public void MarkCurrentTargetSoftRestartFaced()
    {
        CurrentTargetSoftRestartFaced = true;
    }

    public void CompleteCurrentTargetSoftRestart(LockedTargetSnapshot target, DateTimeOffset now)
    {
        CurrentTargetStallLastHp = target.CurrentHp;
        CurrentTargetStallLastProgressAt = now;
        CurrentTargetSoftRestartPending = false;
        CurrentTargetSoftRestartAttempted = true;
        CurrentTargetSoftRestartFaced = false;
        CurrentTargetSoftRestartStartedAt = DateTimeOffset.MinValue;
    }

    public bool IsCurrentTargetSoftRestartFallbackDue(DateTimeOffset now, TimeSpan timeout)
    {
        return CurrentTargetSoftRestartAttempted &&
               !CurrentTargetSoftRestartPending &&
               CurrentTargetStallLastProgressAt != DateTimeOffset.MinValue &&
               now - CurrentTargetStallLastProgressAt >= timeout;
    }

    public void ResetCurrentTargetStallObservation()
    {
        CurrentTargetStallEntityId = 0;
        CurrentTargetStallServerObjectId = 0;
        CurrentTargetStallLastHp = 0;
        CurrentTargetStallLastProgressAt = DateTimeOffset.MinValue;
        CurrentTargetSoftRestartPending = false;
        CurrentTargetSoftRestartAttempted = false;
        CurrentTargetSoftRestartFaced = false;
        CurrentTargetSoftRestartStartedAt = DateTimeOffset.MinValue;
    }

    public void TemporarilyExcludeTarget(
        ushort entityId,
        uint serverObjectId,
        DateTimeOffset expiresAt,
        DateTimeOffset switchGuardUntil)
    {
        lock (_targetSelectionFilterSync)
        {
            if (TryCreateTemporaryTargetKey(entityId, serverObjectId, out var key))
            {
                _temporaryTargetExclusions[key] = expiresAt;
                if (switchGuardUntil > TemporaryTargetSwitchGuardUntil)
                {
                    TemporaryTargetSwitchGuardUntil = switchGuardUntil;
                }
            }
        }
    }

    public bool IsTargetTemporarilyExcluded(WorldObjectSnapshot target, DateTimeOffset now)
    {
        return IsTargetTemporarilyExcluded(target.EntityId, target.ServerObjectId, now);
    }

    public bool IsTargetTemporarilyExcluded(ushort entityId, uint serverObjectId, DateTimeOffset now)
    {
        lock (_targetSelectionFilterSync)
        {
            PruneExpiredTemporaryTargetExclusions(now);
            return TryCreateTemporaryTargetKey(entityId, serverObjectId, out var key) &&
                   _temporaryTargetExclusions.ContainsKey(key);
        }
    }

    public bool ShouldGuardTemporaryTargetSwitch(DateTimeOffset now)
    {
        return TemporaryTargetSwitchGuardUntil != DateTimeOffset.MinValue &&
               now < TemporaryTargetSwitchGuardUntil;
    }

    public void PruneTemporaryTargetExclusions(
        IEnumerable<WorldObjectSnapshot> objects,
        DateTimeOffset now)
    {
        var liveTargets = objects
            .Where(target => target.IsAlive)
            .Select(target =>
            {
                TryCreateTemporaryTargetKey(target.EntityId, target.ServerObjectId, out var key);
                return key;
            })
            .ToHashSet();
        lock (_targetSelectionFilterSync)
        {
            PruneExpiredTemporaryTargetExclusions(now);
            if (_temporaryTargetExclusions.Count == 0)
            {
                return;
            }

            foreach (var key in _temporaryTargetExclusions.Keys.ToArray())
            {
                if (!liveTargets.Contains(key))
                {
                    _temporaryTargetExclusions.Remove(key);
                }
            }
        }
    }

    private void PruneExpiredTemporaryTargetExclusions(DateTimeOffset now)
    {
        foreach (var entry in _temporaryTargetExclusions.ToArray())
        {
            if (now >= entry.Value)
            {
                _temporaryTargetExclusions.Remove(entry.Key);
            }
        }
    }

    private static bool TryCreateTemporaryTargetKey(
        ushort entityId,
        uint serverObjectId,
        out TemporaryTargetKey key)
    {
        if (serverObjectId != 0)
        {
            key = new TemporaryTargetKey(serverObjectId, 0);
            return true;
        }

        if (entityId != 0)
        {
            key = new TemporaryTargetKey(0, entityId);
            return true;
        }

        key = default;
        return false;
    }

    public DateTimeOffset MarkCurrentTargetMissing(ushort entityId, uint serverObjectId, DateTimeOffset now)
    {
        if (!IsSameTarget(MissingCurrentTargetEntityId, MissingCurrentTargetServerObjectId, entityId, serverObjectId) ||
            MissingCurrentTargetSince == DateTimeOffset.MinValue)
        {
            MissingCurrentTargetEntityId = entityId;
            MissingCurrentTargetServerObjectId = serverObjectId;
            MissingCurrentTargetSince = now;
        }

        return MissingCurrentTargetSince;
    }

    public void ResetCurrentTargetMissing()
    {
        MissingCurrentTargetEntityId = 0;
        MissingCurrentTargetServerObjectId = 0;
        MissingCurrentTargetSince = DateTimeOffset.MinValue;
    }

    public bool IsCurrentTarget(LockedTargetSnapshot target)
    {
        return IsSameTarget(
            CurrentTargetEntityId,
            CurrentTargetServerObjectId,
            target.TargetEntityId,
            target.ServerObjectId);
    }

    public bool IsCandidate(WorldObjectSnapshot target)
    {
        return IsSameTarget(CandidateEntityId, CandidateServerObjectId, target.EntityId, target.ServerObjectId);
    }

    public WorldObjectSnapshot? FindCandidate(IEnumerable<WorldObjectSnapshot> objects)
    {
        return objects.FirstOrDefault(IsCandidate);
    }

    public void StartLootAfterKill(LockedTargetSnapshot killedTarget, DateTimeOffset now)
    {
        LootAfterKill.Start(killedTarget, now);
        ReturningHome = false;
        ClearNoTargetRest();
        ClearTarget();
    }

    public void ClearLootAfterKill()
    {
        LootAfterKill.Reset();
        BagCleanup.Reset();
        CleanupReturnToCombatActive = false;
    }

    public void MarkLootAfterKillFinished(DateTimeOffset now, bool lootKeyPressed)
    {
        if (lootKeyPressed)
        {
            LastLootAfterKillFinishedAt = now;
        }
    }

    public bool HasAttemptedLootCorpse(
        ushort entityId,
        uint serverObjectId,
        DateTimeOffset now,
        TimeSpan ttl)
    {
        PruneAttemptedLootCorpses(now, ttl);
        return TryCreateLootCorpseKey(entityId, serverObjectId, out var key) &&
               _attemptedLootCorpses.ContainsKey(key);
    }

    public void MarkLootCorpseAttempted(ushort entityId, uint serverObjectId, DateTimeOffset now)
    {
        if (TryCreateLootCorpseKey(entityId, serverObjectId, out var key))
        {
            _attemptedLootCorpses[key] = now;
        }
    }

    private void PruneAttemptedLootCorpses(DateTimeOffset now, TimeSpan ttl)
    {
        if (_attemptedLootCorpses.Count == 0)
        {
            return;
        }

        if (ttl <= TimeSpan.Zero)
        {
            _attemptedLootCorpses.Clear();
            return;
        }

        foreach (var entry in _attemptedLootCorpses.ToArray())
        {
            if (now - entry.Value >= ttl)
            {
                _attemptedLootCorpses.Remove(entry.Key);
            }
        }
    }

    private static bool TryCreateLootCorpseKey(
        ushort entityId,
        uint serverObjectId,
        out LootCorpseKey key)
    {
        if (serverObjectId != 0)
        {
            key = new LootCorpseKey(serverObjectId, 0);
            return true;
        }

        if (entityId != 0)
        {
            key = new LootCorpseKey(0, entityId);
            return true;
        }

        key = default;
        return false;
    }

    public bool IsTargetIgnored(ushort entityId)
    {
        return IsTargetIgnored(entityId, 0);
    }

    public bool IsTargetIgnored(WorldObjectSnapshot target)
    {
        return IsTargetIgnored(target.EntityId, target.ServerObjectId);
    }

    public bool IsTargetIgnored(ushort entityId, uint serverObjectId)
    {
        lock (_targetSelectionFilterSync)
        {
            return (serverObjectId != 0 && IgnoredTargetServerObjectIds.Contains(serverObjectId)) ||
                   (entityId != 0 && IgnoredTargetEntityIds.Contains(entityId));
        }
    }

    public void IgnoreTarget(ushort entityId)
    {
        IgnoreTarget(entityId, 0);
    }

    public void IgnoreTarget(WorldObjectSnapshot target)
    {
        IgnoreTarget(target.EntityId, target.ServerObjectId);
    }

    public void IgnoreTarget(ushort entityId, uint serverObjectId)
    {
        lock (_targetSelectionFilterSync)
        {
            if (entityId != 0)
            {
                IgnoredTargetEntityIds.Add(entityId);
            }

            if (serverObjectId != 0)
            {
                IgnoredTargetServerObjectIds.Add(serverObjectId);
            }
        }
    }

    public void MarkStartupRecoveryChecked()
    {
        StartupRecoveryChecked = true;
    }

    public void DeferStartupRecoveryCheck()
    {
        StartupRecoveryChecked = false;
    }

    public void StartCleanupReturnToCombat()
    {
        CleanupReturnToCombatActive = true;
        StartupRecoveryChecked = false;
        StartupRecoveryActive = false;
        ResetStartupTownReturn();
        StartupRecoveryPathName = string.Empty;
        StartupRecoveryPointIndex = -1;
        StartupRecoveryPoints = Array.Empty<Vector3Snapshot>();
        ResetStartupRecoveryStuckTracking();
        ReturningHome = false;
        ClearNoTargetRest();
        ClearTarget();
    }

    public void CompleteCleanupReturnToCombat()
    {
        CleanupReturnToCombatActive = false;
    }

    public bool TryGetStationaryHomeFromRevivePath(
        string pathName,
        out Vector3Snapshot home,
        out int pointCount)
    {
        if (StationaryHomeFromRevivePath is { } cachedHome &&
            string.Equals(StationaryHomePathName, pathName, StringComparison.OrdinalIgnoreCase))
        {
            home = cachedHome;
            pointCount = StationaryHomePathPointCount;
            return true;
        }

        home = default;
        pointCount = 0;
        return false;
    }

    public void SetStationaryHomeFromRevivePath(
        string pathName,
        Vector3Snapshot home,
        int pointCount)
    {
        StationaryHomePathName = pathName;
        StationaryHomeFromRevivePath = home;
        StationaryHomePathPointCount = Math.Max(0, pointCount);
    }

    public void StartStartupRecovery(
        string pathName,
        IReadOnlyList<Vector3Snapshot> points,
        int pointIndex)
    {
        ClearNoTargetRest();
        ResetStartupTownReturn();
        StartupRecoveryChecked = true;
        StartupRecoveryActive = true;
        StartupRecoveryPathName = pathName;
        StartupRecoveryPoints = points;
        StartupRecoveryPointIndex = Math.Max(0, pointIndex);
        ResetStartupRecoveryStuckTracking();
    }

    public void StartStartupTownReturn(
        string pathName,
        IReadOnlyList<Vector3Snapshot> points,
        Vector3Snapshot startPosition,
        DateTimeOffset now)
    {
        ClearNoTargetRest();
        StartupRecoveryChecked = true;
        StartupRecoveryActive = false;
        StartupRecoveryPathName = pathName;
        StartupRecoveryPoints = points;
        StartupRecoveryPointIndex = -1;
        ResetStartupRecoveryStuckTracking();
        StartupTownReturnPending = true;
        StartupTownReturnStartPosition = startPosition;
        StartupTownReturnStartedAt = now;
    }

    public void CompleteStartupTownReturn()
    {
        ResetStartupTownReturn();
    }

    public void AdvanceStartupRecoveryPoint()
    {
        if (StartupRecoveryActive)
        {
            StartupRecoveryPointIndex++;
            ResetStartupRecoveryStuckTracking();
        }
    }

    public void MarkStartupRecoveryProgress(int pointIndex, Vector3Snapshot position, DateTimeOffset now)
    {
        StartupRecoveryStuckPointIndex = pointIndex;
        StartupRecoveryLastProgressPosition = position;
        StartupRecoveryLastProgressAt = now;
    }

    public void MarkStartupRecoveryJump(DateTimeOffset now)
    {
        LastStartupRecoveryJumpAt = now;
        StartupRecoveryJumpCount++;
    }

    public void ResetStartupRecoveryStuckTracking()
    {
        StartupRecoveryStuckPointIndex = -1;
        StartupRecoveryLastProgressPosition = null;
        StartupRecoveryLastProgressAt = DateTimeOffset.MinValue;
        LastStartupRecoveryJumpAt = DateTimeOffset.MinValue;
        StartupRecoveryJumpCount = 0;
    }

    public void ClearStartupRecovery()
    {
        ClearNoTargetRest();
        ResetStartupTownReturn();
        StartupRecoveryChecked = true;
        StartupRecoveryActive = false;
        StartupRecoveryPathName = string.Empty;
        StartupRecoveryPointIndex = -1;
        StartupRecoveryPoints = Array.Empty<Vector3Snapshot>();
        ResetStartupRecoveryStuckTracking();
    }

    private void ResetStartupTownReturn()
    {
        StartupTownReturnPending = false;
        StartupTownReturnStartPosition = null;
        StartupTownReturnStartedAt = DateTimeOffset.MinValue;
    }

    public void PruneIgnoredTargets(IEnumerable<WorldObjectSnapshot> objects)
    {
        var liveServerObjectIds = objects
            .Where(target => target.IsAlive && target.ServerObjectId != 0)
            .Select(target => target.ServerObjectId)
            .ToHashSet();
        var liveEntityIds = objects
            .Where(target => target.IsAlive)
            .Select(target => target.EntityId)
            .ToHashSet();
        lock (_targetSelectionFilterSync)
        {
            IgnoredTargetServerObjectIds.RemoveWhere(serverObjectId => !liveServerObjectIds.Contains(serverObjectId));
            IgnoredTargetEntityIds.RemoveWhere(entityId => !liveEntityIds.Contains(entityId));
        }
    }

    public NextTargetPreAimExclusionSnapshot CreateNextTargetPreAimExclusionSnapshot(
        IEnumerable<WorldObjectSnapshot> objects,
        DateTimeOffset now)
    {
        var liveServerObjectIds = objects
            .Where(target => target.IsAlive && target.ServerObjectId != 0)
            .Select(target => target.ServerObjectId)
            .ToHashSet();
        var liveEntityIds = objects
            .Where(target => target.IsAlive)
            .Select(target => target.EntityId)
            .ToHashSet();
        var liveTemporaryKeys = objects
            .Where(target => target.IsAlive)
            .Select(target =>
            {
                TryCreateTemporaryTargetKey(target.EntityId, target.ServerObjectId, out var key);
                return key;
            })
            .ToHashSet();

        lock (_targetSelectionFilterSync)
        {
            IgnoredTargetServerObjectIds.RemoveWhere(serverObjectId => !liveServerObjectIds.Contains(serverObjectId));
            IgnoredTargetEntityIds.RemoveWhere(entityId => !liveEntityIds.Contains(entityId));
            PruneExpiredTemporaryTargetExclusions(now);
            foreach (var key in _temporaryTargetExclusions.Keys.ToArray())
            {
                if (!liveTemporaryKeys.Contains(key))
                {
                    _temporaryTargetExclusions.Remove(key);
                }
            }

            var temporaryServerObjectIds = _temporaryTargetExclusions.Keys
                .Where(key => key.ServerObjectId != 0)
                .Select(key => key.ServerObjectId)
                .ToArray();
            var temporaryEntityIds = _temporaryTargetExclusions.Keys
                .Where(key => key.EntityId != 0)
                .Select(key => key.EntityId)
                .ToArray();

            return new NextTargetPreAimExclusionSnapshot(
                IgnoredTargetEntityIds.ToArray(),
                IgnoredTargetServerObjectIds.ToArray(),
                temporaryEntityIds,
                temporaryServerObjectIds);
        }
    }

    public bool IsPendingTabCandidate(ushort entityId)
    {
        return IsPendingTabCandidate(entityId, 0);
    }

    public bool IsPendingTabCandidate(WorldObjectSnapshot target)
    {
        return IsPendingTabCandidate(target.EntityId, target.ServerObjectId);
    }

    public bool IsPendingTabCandidate(ushort entityId, uint serverObjectId)
    {
        return PendingTabCandidateEntityId != 0 &&
               IsSameTarget(
                   PendingTabCandidateEntityId,
                   PendingTabCandidateServerObjectId,
                   entityId,
                   serverObjectId);
    }

    public bool IsPendingTabVerifyExpired(DateTimeOffset now)
    {
        return PendingTabCandidateEntityId != 0 &&
               now >= PendingTabVerifyUntil;
    }

    public void StartPendingTabVerification(
        ushort entityId,
        DateTimeOffset verifyUntil,
        ushort previousLockedEntityId)
    {
        StartPendingTabVerification(entityId, 0, verifyUntil, previousLockedEntityId, 0);
    }

    public void StartPendingTabVerification(
        WorldObjectSnapshot target,
        DateTimeOffset verifyUntil,
        LockedTargetSnapshot? previousLockedTarget)
    {
        StartPendingTabVerification(
            target.EntityId,
            target.ServerObjectId,
            verifyUntil,
            previousLockedTarget?.TargetEntityId ?? 0,
            previousLockedTarget?.ServerObjectId ?? 0);
    }

    public void StartPendingTabVerification(
        ushort entityId,
        uint serverObjectId,
        DateTimeOffset verifyUntil,
        ushort previousLockedEntityId,
        uint previousLockedServerObjectId)
    {
        PendingTabCandidateEntityId = entityId;
        PendingTabCandidateServerObjectId = serverObjectId;
        PendingTabVerifyUntil = verifyUntil;
        PendingTabPreviousLockedEntityId = previousLockedEntityId;
        PendingTabPreviousLockedServerObjectId = previousLockedServerObjectId;
        PendingTabCorpseNudged = false;
    }

    public void ClearPendingTabVerification()
    {
        PendingTabCandidateEntityId = 0;
        PendingTabCandidateServerObjectId = 0;
        PendingTabVerifyUntil = DateTimeOffset.MinValue;
        PendingTabPreviousLockedEntityId = 0;
        PendingTabPreviousLockedServerObjectId = 0;
        PendingTabCorpseNudged = false;
    }

    public bool TryMarkPendingTabCorpseNudged()
    {
        if (PendingTabCorpseNudged)
        {
            return false;
        }

        PendingTabCorpseNudged = true;
        return true;
    }

    public static bool IsSameTarget(
        ushort leftEntityId,
        uint leftServerObjectId,
        ushort rightEntityId,
        uint rightServerObjectId)
    {
        if (leftServerObjectId != 0 && rightServerObjectId != 0)
        {
            return leftServerObjectId == rightServerObjectId;
        }

        return leftEntityId != 0 &&
               rightEntityId != 0 &&
               leftEntityId == rightEntityId;
    }
}

public enum StationaryCombatTopLevelState
{
    Normal,
    DeathRecovery
}

public sealed class NextTargetPreAimExclusionSnapshot
{
    public static NextTargetPreAimExclusionSnapshot Empty { get; } = new(
        Array.Empty<ushort>(),
        Array.Empty<uint>(),
        Array.Empty<ushort>(),
        Array.Empty<uint>());

    private readonly HashSet<ushort> _ignoredEntityIds;
    private readonly HashSet<uint> _ignoredServerObjectIds;
    private readonly HashSet<ushort> _temporaryEntityIds;
    private readonly HashSet<uint> _temporaryServerObjectIds;

    public NextTargetPreAimExclusionSnapshot(
        IEnumerable<ushort> ignoredEntityIds,
        IEnumerable<uint> ignoredServerObjectIds,
        IEnumerable<ushort> temporaryEntityIds,
        IEnumerable<uint> temporaryServerObjectIds)
    {
        _ignoredEntityIds = ignoredEntityIds.Where(entityId => entityId != 0).ToHashSet();
        _ignoredServerObjectIds = ignoredServerObjectIds.Where(serverObjectId => serverObjectId != 0).ToHashSet();
        _temporaryEntityIds = temporaryEntityIds.Where(entityId => entityId != 0).ToHashSet();
        _temporaryServerObjectIds = temporaryServerObjectIds.Where(serverObjectId => serverObjectId != 0).ToHashSet();
    }

    public bool IsIgnored(WorldObjectSnapshot target)
    {
        return (target.ServerObjectId != 0 && _ignoredServerObjectIds.Contains(target.ServerObjectId)) ||
               (target.EntityId != 0 && _ignoredEntityIds.Contains(target.EntityId));
    }

    public bool IsTemporarilyExcluded(WorldObjectSnapshot target)
    {
        return (target.ServerObjectId != 0 && _temporaryServerObjectIds.Contains(target.ServerObjectId)) ||
               (target.EntityId != 0 && _temporaryEntityIds.Contains(target.EntityId));
    }

    public NextTargetPreAimExclusionSnapshot WithIgnoredTarget(
        ushort entityId,
        uint serverObjectId)
    {
        if (entityId == 0 && serverObjectId == 0)
        {
            return this;
        }

        return new NextTargetPreAimExclusionSnapshot(
            serverObjectId != 0 || entityId == 0
                ? _ignoredEntityIds
                : _ignoredEntityIds.Append(entityId),
            serverObjectId == 0 ? _ignoredServerObjectIds : _ignoredServerObjectIds.Append(serverObjectId),
            _temporaryEntityIds,
            _temporaryServerObjectIds);
    }
}

public sealed class NextTargetPreAimState
{
    public object SyncRoot { get; } = new();

    public CancellationTokenSource? Cancellation { get; set; }

    public Task? Worker { get; set; }

    public bool StopRequested { get; set; }

    public long SessionId { get; set; }

    public ushort FightTargetEntityId { get; set; }

    public uint FightTargetServerObjectId { get; set; }

    public Vector3Snapshot? FightTargetPosition { get; set; }

    public ushort TargetEntityId { get; set; }

    public uint TargetServerObjectId { get; set; }

    public string TargetName { get; set; } = string.Empty;

    public Vector3Snapshot? TargetPosition { get; set; }

    public int TargetPriorityTier { get; set; }

    public double TargetDistanceToOrigin { get; set; }

    public bool TargetingLocalSide { get; set; }

    public bool TargetingTeamSide { get; set; }

    public bool AggressivePriority { get; set; }

    public DateTimeOffset TargetSelectedAt { get; set; } = DateTimeOffset.MinValue;

    public DateTimeOffset LastSnapshotAt { get; set; } = DateTimeOffset.MinValue;

    public DateTimeOffset LastAdjustedAt { get; set; } = DateTimeOffset.MinValue;

    public DateTimeOffset LastAlignedAt { get; set; } = DateTimeOffset.MinValue;

    public int ConsecutiveMissingSnapshots { get; set; }

    public ushort PendingSwitchTargetEntityId { get; set; }

    public uint PendingSwitchTargetServerObjectId { get; set; }

    public int ConsecutiveBetterTargetSnapshots { get; set; }

    public ushort DisplacedTargetEntityId { get; private set; }

    public uint DisplacedTargetServerObjectId { get; private set; }

    public ushort DisplacedTargetReplacementEntityId { get; private set; }

    public uint DisplacedTargetReplacementServerObjectId { get; private set; }

    public bool DisplacedTargetGuardActive { get; private set; }

    public HashSet<uint> TeamSideServerObjectIds { get; } = new();

    public DateTimeOffset TeamSideSnapshotCapturedAt { get; set; } = DateTimeOffset.MinValue;

    public DateTimeOffset LastTeamSideSnapshotAttemptAt { get; set; } = DateTimeOffset.MinValue;

    public DateTimeOffset LastStoppedAt { get; set; } = DateTimeOffset.MinValue;

    public string LastStopReason { get; set; } = string.Empty;

    public bool HasCandidate =>
        TargetEntityId != 0 || TargetServerObjectId != 0;

    public bool HasAlignedCandidate =>
        HasCandidate && LastAlignedAt != DateTimeOffset.MinValue;

    public bool HasCameraCommittedCandidate =>
        HasCandidate &&
        (LastAdjustedAt != DateTimeOffset.MinValue || LastAlignedAt != DateTimeOffset.MinValue);

    public bool IsWorkerRunning =>
        Worker is { IsCompleted: false };

    public void ClearCandidate()
    {
        TargetEntityId = 0;
        TargetServerObjectId = 0;
        TargetName = string.Empty;
        TargetPosition = null;
        TargetPriorityTier = 0;
        TargetDistanceToOrigin = 0.0D;
        TargetingLocalSide = false;
        TargetingTeamSide = false;
        AggressivePriority = false;
        TargetSelectedAt = DateTimeOffset.MinValue;
        LastSnapshotAt = DateTimeOffset.MinValue;
        LastAdjustedAt = DateTimeOffset.MinValue;
        LastAlignedAt = DateTimeOffset.MinValue;
        ConsecutiveMissingSnapshots = 0;
        ResetPendingSwitchConfirmation();
        if (!DisplacedTargetGuardActive)
        {
            ClearDisplacedTargetGuard();
        }
    }

    public void ResetPendingSwitchConfirmation()
    {
        PendingSwitchTargetEntityId = 0;
        PendingSwitchTargetServerObjectId = 0;
        ConsecutiveBetterTargetSnapshots = 0;
    }

    public void RecordDisplacedTargetGuard(
        ushort displacedEntityId,
        uint displacedServerObjectId,
        ushort replacementEntityId,
        uint replacementServerObjectId)
    {
        if ((displacedEntityId == 0 && displacedServerObjectId == 0) ||
            (replacementEntityId == 0 && replacementServerObjectId == 0) ||
            StationaryCombatState.IsSameTarget(
                displacedEntityId,
                displacedServerObjectId,
                replacementEntityId,
                replacementServerObjectId))
        {
            ClearDisplacedTargetGuard();
            return;
        }

        DisplacedTargetEntityId = displacedEntityId;
        DisplacedTargetServerObjectId = displacedServerObjectId;
        DisplacedTargetReplacementEntityId = replacementEntityId;
        DisplacedTargetReplacementServerObjectId = replacementServerObjectId;
        DisplacedTargetGuardActive = false;
    }

    public bool ActivateDisplacedTargetGuard(
        ushort consumedEntityId,
        uint consumedServerObjectId)
    {
        if (!StationaryCombatState.IsSameTarget(
                DisplacedTargetReplacementEntityId,
                DisplacedTargetReplacementServerObjectId,
                consumedEntityId,
                consumedServerObjectId))
        {
            ClearDisplacedTargetGuard();
            return false;
        }

        DisplacedTargetGuardActive = true;
        return true;
    }

    public bool TryGetActiveDisplacedTargetForFightTarget(
        ushort fightTargetEntityId,
        uint fightTargetServerObjectId,
        out ushort displacedEntityId,
        out uint displacedServerObjectId)
    {
        if (!DisplacedTargetGuardActive)
        {
            displacedEntityId = 0;
            displacedServerObjectId = 0;
            return false;
        }

        if (!StationaryCombatState.IsSameTarget(
                DisplacedTargetReplacementEntityId,
                DisplacedTargetReplacementServerObjectId,
                fightTargetEntityId,
                fightTargetServerObjectId))
        {
            ClearDisplacedTargetGuard();
            displacedEntityId = 0;
            displacedServerObjectId = 0;
            return false;
        }

        displacedEntityId = DisplacedTargetEntityId;
        displacedServerObjectId = DisplacedTargetServerObjectId;
        return displacedEntityId != 0 || displacedServerObjectId != 0;
    }

    public void ClearDisplacedTargetGuard()
    {
        DisplacedTargetEntityId = 0;
        DisplacedTargetServerObjectId = 0;
        DisplacedTargetReplacementEntityId = 0;
        DisplacedTargetReplacementServerObjectId = 0;
        DisplacedTargetGuardActive = false;
    }

    public NextTargetPreAimSelection? CreateCurrentSelection()
    {
        if (!HasCandidate || TargetPosition is null)
        {
            return null;
        }

        return new NextTargetPreAimSelection(
            new WorldObjectSnapshot(
                TargetEntityId,
                TargetServerObjectId,
                TargetName,
                "monster",
                TargetPosition,
                null,
                1,
                1,
                IsTargetingLocalPlayer: TargetingLocalSide,
                AggressiveKnown: AggressivePriority,
                IsAggressiveToPlayer: AggressivePriority),
            TargetPriorityTier,
            TargetDistanceToOrigin,
            TargetingLocalSide,
            TargetingTeamSide,
            AggressivePriority,
            TargetSelectedAt,
            "current");
    }
}

public sealed class StationaryCombatDeathRecoveryState
{
    public StationaryCombatDeathRecoveryStep Step { get; private set; } = StationaryCombatDeathRecoveryStep.StopInput;

    public DateTimeOffset StartedAt { get; private set; } = DateTimeOffset.MinValue;

    public DateTimeOffset StepStartedAt { get; private set; } = DateTimeOffset.MinValue;

    public bool ReviveClicked { get; set; }

    public DateTimeOffset LastReviveClickAt { get; private set; } = DateTimeOffset.MinValue;

    public int ReviveClickCount { get; private set; }

    public int PostReviveScrollsSent { get; set; }

    public string RevivePathName { get; set; } = string.Empty;

    public int RevivePathPointIndex { get; set; } = -1;

    public IReadOnlyList<Vector3Snapshot> RevivePathPoints { get; set; } = Array.Empty<Vector3Snapshot>();

    public int RevivePathStuckPointIndex { get; private set; } = -1;

    public Vector3Snapshot? RevivePathLastProgressPosition { get; private set; }

    public DateTimeOffset RevivePathLastProgressAt { get; private set; } = DateTimeOffset.MinValue;

    public DateTimeOffset LastRevivePathJumpAt { get; private set; } = DateTimeOffset.MinValue;

    public int RevivePathJumpCount { get; private set; }

    public bool RevivePathLeaderSiphonActive { get; private set; }

    public uint RevivePathLeaderSiphonServerObjectId { get; private set; }

    public string RevivePathLeaderSiphonName { get; private set; } = string.Empty;

    public void Start(DateTimeOffset now)
    {
        Step = StationaryCombatDeathRecoveryStep.StopInput;
        StartedAt = now;
        StepStartedAt = now;
        ReviveClicked = false;
        LastReviveClickAt = DateTimeOffset.MinValue;
        ReviveClickCount = 0;
        PostReviveScrollsSent = 0;
        RevivePathName = string.Empty;
        RevivePathPointIndex = -1;
        RevivePathPoints = Array.Empty<Vector3Snapshot>();
        ClearRevivePathLeaderSiphon();
        ResetRevivePathStuckTracking();
    }

    public void MarkReviveClicked(DateTimeOffset now)
    {
        ReviveClicked = true;
        LastReviveClickAt = now;
        ReviveClickCount++;
    }

    public void Advance(DateTimeOffset now)
    {
        Step = Step switch
        {
            StationaryCombatDeathRecoveryStep.StopInput => StationaryCombatDeathRecoveryStep.WaitBeforeReviveClick,
            StationaryCombatDeathRecoveryStep.WaitBeforeReviveClick => StationaryCombatDeathRecoveryStep.ClickRevive,
            StationaryCombatDeathRecoveryStep.ClickRevive => StationaryCombatDeathRecoveryStep.WaitAlive,
            StationaryCombatDeathRecoveryStep.WaitAlive => StationaryCombatDeathRecoveryStep.PostReviveScroll,
            StationaryCombatDeathRecoveryStep.PostReviveScroll => StationaryCombatDeathRecoveryStep.PostReviveSpiritmasterPet,
            StationaryCombatDeathRecoveryStep.PostReviveSpiritmasterPet => StationaryCombatDeathRecoveryStep.PostReviveMaintenance,
            StationaryCombatDeathRecoveryStep.PostReviveMaintenance => StationaryCombatDeathRecoveryStep.FollowRevivePath,
            StationaryCombatDeathRecoveryStep.FollowRevivePath => StationaryCombatDeathRecoveryStep.Complete,
            _ => StationaryCombatDeathRecoveryStep.Complete
        };
        StepStartedAt = now;
    }

    public void MarkRevivePathProgress(int pointIndex, Vector3Snapshot position, DateTimeOffset now)
    {
        RevivePathStuckPointIndex = pointIndex;
        RevivePathLastProgressPosition = position;
        RevivePathLastProgressAt = now;
    }

    public void MarkRevivePathJump(DateTimeOffset now)
    {
        LastRevivePathJumpAt = now;
        RevivePathJumpCount++;
    }

    public void ResetRevivePathStuckTracking()
    {
        RevivePathStuckPointIndex = -1;
        RevivePathLastProgressPosition = null;
        RevivePathLastProgressAt = DateTimeOffset.MinValue;
        LastRevivePathJumpAt = DateTimeOffset.MinValue;
        RevivePathJumpCount = 0;
    }

    public bool ActivateRevivePathLeaderSiphon(uint leaderServerObjectId, string leaderName)
    {
        var changed =
            !RevivePathLeaderSiphonActive ||
            RevivePathLeaderSiphonServerObjectId != leaderServerObjectId;
        RevivePathLeaderSiphonActive = true;
        RevivePathLeaderSiphonServerObjectId = leaderServerObjectId;
        RevivePathLeaderSiphonName = leaderName ?? string.Empty;
        return changed;
    }

    public bool ClearRevivePathLeaderSiphon()
    {
        var wasActive = RevivePathLeaderSiphonActive;
        RevivePathLeaderSiphonActive = false;
        RevivePathLeaderSiphonServerObjectId = 0;
        RevivePathLeaderSiphonName = string.Empty;
        return wasActive;
    }

    public void Reset()
    {
        Step = StationaryCombatDeathRecoveryStep.StopInput;
        StartedAt = DateTimeOffset.MinValue;
        StepStartedAt = DateTimeOffset.MinValue;
        ReviveClicked = false;
        LastReviveClickAt = DateTimeOffset.MinValue;
        ReviveClickCount = 0;
        PostReviveScrollsSent = 0;
        RevivePathName = string.Empty;
        RevivePathPointIndex = -1;
        RevivePathPoints = Array.Empty<Vector3Snapshot>();
        ClearRevivePathLeaderSiphon();
        ResetRevivePathStuckTracking();
    }
}

public enum StationaryCombatDeathRecoveryStep
{
    StopInput,
    WaitBeforeReviveClick,
    ClickRevive,
    WaitAlive,
    PostReviveScroll,
    PostReviveSpiritmasterPet,
    PostReviveMaintenance,
    FollowRevivePath,
    Complete
}

public sealed class StationaryCombatPathCombatState
{
    public bool Active { get; private set; }

    public bool Completed { get; private set; }

    public string CompletedPathName { get; private set; } = string.Empty;

    public string PathName { get; private set; } = string.Empty;

    public int PointIndex { get; private set; } = -1;

    public int Direction { get; private set; } = 1;

    public IReadOnlyList<Vector3Snapshot> Points { get; private set; } = Array.Empty<Vector3Snapshot>();

    public Vector3Snapshot? CurrentTargetAnchor { get; private set; }

    public int PathStuckPointIndex { get; private set; } = -1;

    public Vector3Snapshot? PathLastProgressPosition { get; private set; }

    public DateTimeOffset PathLastProgressAt { get; private set; } = DateTimeOffset.MinValue;

    public DateTimeOffset LastPathJumpAt { get; private set; } = DateTimeOffset.MinValue;

    public int PathJumpCount { get; private set; }

    public void Start(string pathName, IReadOnlyList<Vector3Snapshot> points, int pointIndex)
    {
        Active = true;
        Completed = false;
        CompletedPathName = string.Empty;
        PathName = pathName;
        Points = points;
        PointIndex = Math.Clamp(pointIndex, 0, Math.Max(0, points.Count - 1));
        Direction = 1;
        CurrentTargetAnchor = null;
        ResetPathStuckTracking();
    }

    public void AdvancePoint(bool loopPath, bool reverseAtEnd)
    {
        if (!Active || Points.Count == 0)
        {
            Reset();
            return;
        }

        var nextIndex = PointIndex + Direction;
        if (nextIndex >= 0 && nextIndex < Points.Count)
        {
            PointIndex = nextIndex;
            ResetPathStuckTracking();
            return;
        }

        if (loopPath && reverseAtEnd && Points.Count > 1)
        {
            Direction = -Direction;
            PointIndex = Math.Clamp(PointIndex + Direction, 0, Points.Count - 1);
            ResetPathStuckTracking();
            return;
        }

        if (loopPath)
        {
            PointIndex = Direction >= 0 ? 0 : Points.Count - 1;
            ResetPathStuckTracking();
            return;
        }

        Complete();
    }

    public void MarkCurrentTargetAnchor(Vector3Snapshot position)
    {
        CurrentTargetAnchor = position;
    }

    public void ClearCurrentTargetAnchor()
    {
        CurrentTargetAnchor = null;
    }

    public void MarkPathProgress(int pointIndex, Vector3Snapshot position, DateTimeOffset now)
    {
        PathStuckPointIndex = pointIndex;
        PathLastProgressPosition = position;
        PathLastProgressAt = now;
    }

    public void MarkPathJump(DateTimeOffset now)
    {
        LastPathJumpAt = now;
        PathJumpCount++;
    }

    public void ResetPathStuckTracking()
    {
        PathStuckPointIndex = -1;
        PathLastProgressPosition = null;
        PathLastProgressAt = DateTimeOffset.MinValue;
        LastPathJumpAt = DateTimeOffset.MinValue;
        PathJumpCount = 0;
    }

    public void Reset()
    {
        Active = false;
        Completed = false;
        CompletedPathName = string.Empty;
        PathName = string.Empty;
        PointIndex = -1;
        Direction = 1;
        Points = Array.Empty<Vector3Snapshot>();
        CurrentTargetAnchor = null;
        ResetPathStuckTracking();
    }

    private void Complete()
    {
        Completed = true;
        CompletedPathName = PathName;
        Active = false;
        PathName = string.Empty;
        PointIndex = -1;
        Direction = 1;
        Points = Array.Empty<Vector3Snapshot>();
        CurrentTargetAnchor = null;
        ResetPathStuckTracking();
    }
}

public sealed class StationaryCombatLootAfterKillState
{
    public StationaryCombatLootAfterKillStep Step { get; private set; } = StationaryCombatLootAfterKillStep.Inactive;

    public DateTimeOffset StartedAt { get; private set; } = DateTimeOffset.MinValue;

    public DateTimeOffset StepStartedAt { get; private set; } = DateTimeOffset.MinValue;

    public ushort KilledTargetEntityId { get; private set; }

    public uint KilledTargetServerObjectId { get; private set; }

    public string KilledTargetName { get; private set; } = string.Empty;

    public Vector3Snapshot? KilledTargetPosition { get; private set; }

    public uint KilledTargetLootableRaw { get; private set; }

    public uint KilledTargetInteractionState { get; private set; }

    public bool LootKeyPressed { get; private set; }

    public bool Active => Step != StationaryCombatLootAfterKillStep.Inactive &&
                          Step != StationaryCombatLootAfterKillStep.Complete;

    public void Start(LockedTargetSnapshot killedTarget, DateTimeOffset now)
    {
        Step = StationaryCombatLootAfterKillStep.StopInput;
        StartedAt = now;
        StepStartedAt = now;
        KilledTargetEntityId = killedTarget.TargetEntityId;
        KilledTargetServerObjectId = killedTarget.ServerObjectId;
        KilledTargetName = killedTarget.Name ?? string.Empty;
        KilledTargetPosition = killedTarget.Position;
        KilledTargetLootableRaw = killedTarget.LootableRaw;
        KilledTargetInteractionState = killedTarget.InteractionState;
        LootKeyPressed = false;
    }

    public void MarkLootKeyPressed()
    {
        LootKeyPressed = true;
    }

    public void MoveToPostCombatMaintenance(DateTimeOffset now)
    {
        Step = StationaryCombatLootAfterKillStep.PostCombatMaintenance;
        StepStartedAt = now;
    }

    public void Advance(DateTimeOffset now)
    {
        Step = Step switch
        {
            StationaryCombatLootAfterKillStep.StopInput => StationaryCombatLootAfterKillStep.WaitAfterKill,
            StationaryCombatLootAfterKillStep.WaitAfterKill => StationaryCombatLootAfterKillStep.PressLootKey,
            StationaryCombatLootAfterKillStep.PressLootKey => StationaryCombatLootAfterKillStep.WaitNearCorpse,
            StationaryCombatLootAfterKillStep.WaitNearCorpse => StationaryCombatLootAfterKillStep.WaitAfterNear,
            StationaryCombatLootAfterKillStep.WaitAfterNear => StationaryCombatLootAfterKillStep.PostCombatMaintenance,
            StationaryCombatLootAfterKillStep.PostCombatMaintenance => StationaryCombatLootAfterKillStep.Complete,
            _ => StationaryCombatLootAfterKillStep.Complete
        };
        StepStartedAt = now;
    }

    public void Reset()
    {
        Step = StationaryCombatLootAfterKillStep.Inactive;
        StartedAt = DateTimeOffset.MinValue;
        StepStartedAt = DateTimeOffset.MinValue;
        KilledTargetEntityId = 0;
        KilledTargetServerObjectId = 0;
        KilledTargetName = string.Empty;
        KilledTargetPosition = null;
        KilledTargetLootableRaw = 0;
        KilledTargetInteractionState = 0;
        LootKeyPressed = false;
    }
}

public enum StationaryCombatLootAfterKillStep
{
    Inactive,
    StopInput,
    WaitAfterKill,
    PressLootKey,
    WaitNearCorpse,
    WaitAfterNear,
    PostCombatMaintenance,
    Complete
}

internal readonly record struct LootCorpseKey(uint ServerObjectId, ushort EntityId);

internal readonly record struct TemporaryTargetKey(uint ServerObjectId, ushort EntityId);

internal enum StationaryCombatBehaviorStatus
{
    Running,
    Success,
    Failure
}
