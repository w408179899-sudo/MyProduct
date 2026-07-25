using Roadhog.Application.Workers;
using Roadhog.Application.StationaryCombat;
using Roadhog.Core.Accounts;
using Roadhog.Core.Api;
using Roadhog.Core.Common;
using Roadhog.Core.Input;
using Roadhog.Core.Model;

namespace Roadhog.Application.Team;

public sealed class TeamOutputController
{
    private static readonly TimeSpan TeamOutputTickDelay = TimeSpan.FromMilliseconds(50);
    private static readonly TimeSpan WarningLogInterval = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan SelectConfirmDelay = TimeSpan.FromMilliseconds(25);
    private static readonly TimeSpan AssistTargetInitialConfirmDelay = TimeSpan.FromMilliseconds(50);
    private static readonly TimeSpan AssistTargetConfirmRetryDelay = TimeSpan.FromMilliseconds(80);
    private const int AssistTargetConfirmPolls = 3;
    private const int LeaderAssistJumpInterval = 5;
    private const string LeaderAssistKey = "C";
    private const string LeaderAssistJumpKey = "Space";

    private readonly IKeyboardInput _keyboard;

    public TeamOutputController(IKeyboardInput keyboard)
    {
        _keyboard = keyboard;
    }

    public async Task<TeamOutputTickResult> TickAsync(
        AccountWorkerContext context,
        TeamOutputState state,
        StationaryCombatState? combatState = null)
    {
        var team = context.Config.ScriptSettings?.Team ?? new TeamScriptSettings();
        var output = team.Output ?? new TeamOutputScriptSettings();
        if (team.Role != TeamRole.Output || !output.Enabled)
        {
            return TeamOutputTickResult.Continue(context.Options.TickInterval);
        }

        var readContext = CreateReadContext(context);
        var monitor = new TeamMonitor(context.GameApi, context.Logger);
        var snapshotResult = await monitor.ReadSnapshotAsync(readContext, context.StopToken).ConfigureAwait(false);
        if (!snapshotResult.Success || snapshotResult.Value is null)
        {
            if (ShouldLog(state.LastSnapshotWarningAt))
            {
                state.LastSnapshotWarningAt = DateTimeOffset.Now;
                context.Logger.Warn("team_output.snapshot.failed", new Dictionary<string, object?>
                {
                    ["account"] = context.Config.AccountName,
                    ["error"] = snapshotResult.Error
                });
            }

            return RecordLeaderUnavailableTick(state)
                ? TeamOutputTickResult.Continue(TeamOutputTickDelay)
                : TeamOutputTickResult.SkipNormalWork(TeamOutputTickDelay);
        }

        var snapshot = snapshotResult.Value;
        var leader = snapshot.LeaderMember;
        if (leader is null || leader.IsSelf)
        {
            return RecordLeaderUnavailableTick(state)
                ? TeamOutputTickResult.Continue(TeamOutputTickDelay)
                : TeamOutputTickResult.SkipNormalWork(TeamOutputTickDelay);
        }

        if (output.StopWhenLeaderDead && leader.PartyMember.IsDead)
        {
            if (output.AllowSelfDefense)
            {
                var selfDefenseResult = await TryHandleLeaderDeadSelfDefenseAsync(
                        context,
                        state,
                        snapshot,
                        combatState)
                    .ConfigureAwait(false);
                if (selfDefenseResult is not null)
                {
                    ResetLeaderAssistJumpCount(state);
                    return selfDefenseResult;
                }
            }

            if (RecordLeaderUnavailableTick(state))
            {
                return TeamOutputTickResult.Continue(TeamOutputTickDelay);
            }

            await SelectLeaderAndAssistAsync(context, state, leader).ConfigureAwait(false);
            return TeamOutputTickResult.SkipNormalWork(TeamOutputTickDelay);
        }

        if (leader.PartyMember.DistanceToLocalPlayer is null)
        {
            return RecordLeaderUnavailableTick(state)
                ? TeamOutputTickResult.Continue(TeamOutputTickDelay)
                : TeamOutputTickResult.SkipNormalWork(TeamOutputTickDelay);
        }

        state.ConsecutiveLeaderUnavailableTicks = 0;

        if (await TeamLeaderRestSync
                .TryHandleAsync(context, _keyboard, state.LeaderRestSync, snapshot, leader, combatState, "team_output")
                .ConfigureAwait(false))
        {
            ResetLeaderAssistJumpCount(state);
            return TeamOutputTickResult.SkipNormalWork(TeamOutputTickDelay);
        }

        if (output.AllowSelfDefense &&
            await TryFindSelfDefenseThreatAsync(context, state, snapshot).ConfigureAwait(false) is not null)
        {
            ResetLeaderAssistJumpCount(state);
            return TeamOutputTickResult.Continue(TeamOutputTickDelay);
        }

        var groupDistanceMeters = team.GroupDistanceMeters;
        var leaderInGroupRange = TeamLeaderRuntimePolicy.UpdateLeaderGroupState(
            leader,
            groupDistanceMeters,
            state.LeaderGroupActive);
        state.LeaderGroupActive = leaderInGroupRange;
        var activeGroupDistanceMeters = leaderInGroupRange
            ? TeamLeaderRuntimePolicy.ResolveLeaderGroupExitDistanceMeters(groupDistanceMeters)
            : groupDistanceMeters;
        if (!leaderInGroupRange)
        {
            LogFollowDecision(
                context,
                state,
                "leader_out_of_range",
                leader,
                groupDistanceMeters,
                activeGroupDistanceMeters,
                state.LeaderGroupActive,
                combatState);
            ResetLeaderAssistJumpCount(state);
            return TeamOutputTickResult.Continue(TeamOutputTickDelay);
        }

        if (TeamLeaderRuntimePolicy.HasActiveCombatTarget(combatState))
        {
            LogFollowDecision(
                context,
                state,
                "active_combat_target",
                leader,
                groupDistanceMeters,
                activeGroupDistanceMeters,
                state.LeaderGroupActive,
                combatState);
            ResetLeaderAssistJumpCount(state);
            return TeamOutputTickResult.Continue(TeamOutputTickDelay);
        }

        if (!leader.IsScreenVisible || leader.PartyMember.LiveTargetServerObjectId == 0)
        {
            await SelectLeaderAndAssistAsync(context, state, leader).ConfigureAwait(false);
            return output.StopWhenLeaderHasNoTarget
                ? TeamOutputTickResult.SkipNormalWork(TeamOutputTickDelay)
                : TeamOutputTickResult.Continue(TeamOutputTickDelay);
        }

        var leaderTargetServerObjectId = leader.PartyMember.LiveTargetServerObjectId;
        if (TeamLeaderTargetValidator.IsKnownTeamSideTarget(
                snapshot,
                leaderTargetServerObjectId,
                out var knownTargetKind,
                out var knownTargetName))
        {
            await SelectLeaderAndAssistAsync(context, state, leader).ConfigureAwait(false);
            LogLeaderTargetSkipped(
                context,
                state,
                leader,
                leaderTargetServerObjectId,
                "known_team_side_target",
                knownTargetKind,
                knownTargetName);
            return output.StopWhenLeaderHasNoTarget
                ? TeamOutputTickResult.SkipNormalWork(TeamOutputTickDelay)
                : TeamOutputTickResult.Continue(TeamOutputTickDelay);
        }

        var currentTargetResult = await ReadLockedTargetAsync(context, bypassMemoryCache: true).ConfigureAwait(false);
        if (TeamLeaderTargetValidator.IsLeaderAttackTarget(
                currentTargetResult,
                leader,
                leaderTargetServerObjectId,
                out _))
        {
            ResetLeaderAssistJumpCount(state);
            return AcceptLeaderAttackTarget(
                context,
                leader,
                leaderTargetServerObjectId,
                currentTargetResult,
                combatState,
                "already_locked",
                0);
        }

        if (!await SelectLeaderAndAssistAsync(context, state, leader).ConfigureAwait(false))
        {
            return TeamOutputTickResult.SkipNormalWork(TeamOutputTickDelay);
        }

        var assistTargetKey = ResolveAssistTargetKey(output);
        ResetLeaderAssistJumpCount(state);
        var assistResult = await _keyboard
            .PressKeyAsync(assistTargetKey, ResolveKeyHold(context), context.StopToken)
            .ConfigureAwait(false);
        if (!assistResult.Success)
        {
            LogInputFailure(context, state, "team_output.assist_target_key.failed", assistTargetKey, assistResult);
            return TeamOutputTickResult.SkipNormalWork(TeamOutputTickDelay);
        }

        state.LastActionAt = DateTimeOffset.Now;
        context.Logger.Info("team_output.assist_target.pressed", new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["leader"] = leader.Name,
            ["leaderServerObjectId"] = leader.ServerObjectId,
            ["leaderTargetServerObjectId"] = leaderTargetServerObjectId,
            ["key"] = assistTargetKey
        });

        var verification = await VerifyLeaderAttackTargetAfterAssistAsync(
                context,
                leader,
                leaderTargetServerObjectId)
            .ConfigureAwait(false);
        if (!verification.Accepted)
        {
            LogTargetRejected(
                context,
                state,
                verification.LockedTargetResult.Value,
                leader,
                leaderTargetServerObjectId,
                verification.RejectReason);
            await SelectLeaderAndAssistAsync(context, state, leader).ConfigureAwait(false);
            return TeamOutputTickResult.SkipNormalWork(TeamOutputTickDelay);
        }

        return AcceptLeaderAttackTarget(
            context,
            leader,
            leaderTargetServerObjectId,
            verification.LockedTargetResult,
            combatState,
            "assist_key",
            verification.PollCount);
    }

    private static TeamOutputTickResult AcceptLeaderAttackTarget(
        AccountWorkerContext context,
        TeamMemberSnapshot leader,
        uint leaderTargetServerObjectId,
        OperationResult<LockedTargetSnapshot> lockedTargetResult,
        StationaryCombatState? combatState,
        string source,
        int pollCount)
    {
        var combatAdopted = TeamCombatTargetAdopter.TryAdoptLeaderAttackTarget(
            combatState,
            lockedTargetResult.Value);
        context.Logger.Info("team_output.target.accepted", new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["leader"] = leader.Name,
            ["leaderServerObjectId"] = leader.ServerObjectId,
            ["leaderTargetServerObjectId"] = leaderTargetServerObjectId,
            ["targetName"] = lockedTargetResult.Value?.Name,
            ["targetServerObjectId"] = lockedTargetResult.Value?.ServerObjectId,
            ["targetTargetServerObjectId"] = lockedTargetResult.Value?.TargetServerObjectId,
            ["source"] = source,
            ["confirmPollCount"] = pollCount,
            ["combatAdopted"] = combatAdopted
        });
        return TeamOutputTickResult.Continue(TeamOutputTickDelay);
    }

    private async Task<AssistTargetVerification> VerifyLeaderAttackTargetAfterAssistAsync(
        AccountWorkerContext context,
        TeamMemberSnapshot leader,
        uint leaderTargetServerObjectId)
    {
        var lastResult = OperationResult<LockedTargetSnapshot>.Fail("target_not_read");
        var lastRejectReason = "target_not_read";
        await Task.Delay(AssistTargetInitialConfirmDelay, context.StopToken).ConfigureAwait(false);
        for (var poll = 1; poll <= AssistTargetConfirmPolls; poll++)
        {
            if (poll > 1)
            {
                await Task.Delay(AssistTargetConfirmRetryDelay, context.StopToken).ConfigureAwait(false);
            }

            lastResult = await ReadLockedTargetAsync(context, bypassMemoryCache: true).ConfigureAwait(false);
            if (TeamLeaderTargetValidator.IsLeaderAttackTarget(
                    lastResult,
                    leader,
                    leaderTargetServerObjectId,
                    out lastRejectReason))
            {
                return new AssistTargetVerification(lastResult, true, string.Empty, poll);
            }
        }

        return new AssistTargetVerification(lastResult, false, lastRejectReason, AssistTargetConfirmPolls);
    }

    private async Task<bool> SelectLeaderAndAssistAsync(
        AccountWorkerContext context,
        TeamOutputState state,
        TeamMemberSnapshot leader)
    {
        if (!leader.IsLeader)
        {
            return false;
        }

        var selection = await EnsureMemberBodySelectedAsync(context, state, leader).ConfigureAwait(false);
        if (!selection.Selected)
        {
            return false;
        }

        var result = await _keyboard
            .PressKeyAsync(LeaderAssistKey, ResolveKeyHold(context), context.StopToken)
            .ConfigureAwait(false);
        if (!result.Success)
        {
            LogInputFailure(context, state, "team_output.leader_assist_key.failed", LeaderAssistKey, result);
            return false;
        }

        state.LastActionAt = DateTimeOffset.Now;
        context.Logger.Info("team_output.leader_assist.pressed", new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["leader"] = leader.Name,
            ["leaderServerObjectId"] = leader.ServerObjectId,
            ["functionKey"] = FormatFunctionKey(leader),
            ["key"] = LeaderAssistKey
        });
        await PressLeaderJumpIfDueAsync(context, state, leader).ConfigureAwait(false);
        return true;
    }

    private async Task PressLeaderJumpIfDueAsync(
        AccountWorkerContext context,
        TeamOutputState state,
        TeamMemberSnapshot leader)
    {
        state.LeaderAssistPressCountSinceJump++;
        if (state.LeaderAssistPressCountSinceJump < LeaderAssistJumpInterval)
        {
            return;
        }

        state.LeaderAssistPressCountSinceJump = 0;
        var result = await _keyboard
            .PressKeyAsync(LeaderAssistJumpKey, ResolveKeyHold(context), context.StopToken)
            .ConfigureAwait(false);
        if (!result.Success)
        {
            LogInputFailure(context, state, "team_output.leader_jump_key.failed", LeaderAssistJumpKey, result);
            return;
        }

        state.LastActionAt = DateTimeOffset.Now;
        context.Logger.Info("team_output.leader_jump.pressed", new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["leader"] = leader.Name,
            ["leaderServerObjectId"] = leader.ServerObjectId,
            ["key"] = LeaderAssistJumpKey,
            ["assistPressInterval"] = LeaderAssistJumpInterval
        });
    }

    private static void ResetLeaderAssistJumpCount(TeamOutputState state)
    {
        state.LeaderAssistPressCountSinceJump = 0;
    }

    private async Task<MemberSelectionResult> EnsureMemberBodySelectedAsync(
        AccountWorkerContext context,
        TeamOutputState state,
        TeamMemberSnapshot member)
    {
        if (member.ServerObjectId == 0)
        {
            return MemberSelectionResult.NotSelected;
        }

        var current = await ReadLockedTargetAsync(context, bypassMemoryCache: true).ConfigureAwait(false);
        if (IsSelectedMemberBody(current, member))
        {
            return MemberSelectionResult.AlreadySelectedResult;
        }

        var functionKey = FormatFunctionKey(member);
        if (string.IsNullOrWhiteSpace(functionKey))
        {
            return MemberSelectionResult.NotSelected;
        }

        for (var attempt = 1; attempt <= 3; attempt++)
        {
            ResetLeaderAssistJumpCount(state);
            var pressResult = await _keyboard
                .PressKeyAsync(functionKey, ResolveKeyHold(context), context.StopToken)
                .ConfigureAwait(false);
            if (!pressResult.Success)
            {
                LogInputFailure(context, state, "team_output.select_key.failed", functionKey, pressResult);
                return MemberSelectionResult.NotSelected;
            }

            await Task.Delay(SelectConfirmDelay, context.StopToken).ConfigureAwait(false);
            current = await ReadLockedTargetAsync(context, bypassMemoryCache: true).ConfigureAwait(false);
            if (IsSelectedMemberBody(current, member))
            {
                context.Logger.Info("team_output.leader_selected", new Dictionary<string, object?>
                {
                    ["account"] = context.Config.AccountName,
                    ["leader"] = member.Name,
                    ["leaderServerObjectId"] = member.ServerObjectId,
                    ["functionKey"] = functionKey,
                    ["attempt"] = attempt
                });
                return MemberSelectionResult.SelectedByInputResult;
            }
        }

        return MemberSelectionResult.NotSelected;
    }

    private async Task<TeamOutputTickResult?> TryHandleLeaderDeadSelfDefenseAsync(
        AccountWorkerContext context,
        TeamOutputState state,
        TeamSnapshot snapshot,
        StationaryCombatState? combatState)
    {
        if (TeamLeaderRuntimePolicy.HasActiveSelfDefenseTarget(combatState))
        {
            return TeamOutputTickResult.Continue(TeamOutputTickDelay);
        }

        if (combatState is null ||
            TeamLeaderRuntimePolicy.HasActiveCombatTarget(combatState))
        {
            return null;
        }

        var threat = await TryFindSelfDefenseThreatAsync(context, state, snapshot).ConfigureAwait(false);
        if (threat is null)
        {
            return null;
        }

        return TeamCombatTargetAdopter.TryAdoptSelfDefenseTarget(combatState, threat)
            ? TeamOutputTickResult.Continue(TeamOutputTickDelay)
            : null;
    }

    private async Task<WorldObjectSnapshot?> TryFindSelfDefenseThreatAsync(
        AccountWorkerContext context,
        TeamOutputState state,
        TeamSnapshot snapshot)
    {
        var protectedServerObjectIds = GetLocalProtectedServerObjectIds(snapshot);
        if (protectedServerObjectIds.Count == 0)
        {
            return null;
        }

        var worldResult = await ReadWorldObjectsAsync(context).ConfigureAwait(false);
        if (!worldResult.Success || worldResult.Value is null)
        {
            if (ShouldLog(state.LastSelfDefenseWarningAt))
            {
                state.LastSelfDefenseWarningAt = DateTimeOffset.Now;
                context.Logger.Warn("team_output.self_defense.world_objects.failed", new Dictionary<string, object?>
                {
                    ["account"] = context.Config.AccountName,
                    ["error"] = worldResult.Error
                });
            }

            return null;
        }

        var threat = worldResult.Value
            .Where(StationaryCombatTargetSelector.IsSelectableMonster)
            .FirstOrDefault(target =>
                target.TargetServerObjectId != 0 &&
                protectedServerObjectIds.Contains(target.TargetServerObjectId));
        if (threat is null)
        {
            return null;
        }

        if (ShouldLog(state.LastSelfDefenseLogAt))
        {
            state.LastSelfDefenseLogAt = DateTimeOffset.Now;
            context.Logger.Info("team_output.self_defense.threat_detected", new Dictionary<string, object?>
            {
                ["account"] = context.Config.AccountName,
                ["targetEntityId"] = threat.EntityId,
                ["targetServerObjectId"] = threat.ServerObjectId,
                ["targetName"] = threat.Name,
                ["targetingServerObjectId"] = threat.TargetServerObjectId
            });
        }

        return threat;
    }

    private static HashSet<uint> GetLocalProtectedServerObjectIds(TeamSnapshot snapshot)
    {
        var ids = new HashSet<uint>();
        var local = snapshot.LocalMember;
        if (local is null || !local.PartyMember.IsAlive)
        {
            return ids;
        }

        if (local.ServerObjectId != 0)
        {
            ids.Add(local.ServerObjectId);
        }

        var pet = local.SummonedPet?.Pet;
        if (local.PartyMember.Class == AionClassId.Spiritmaster &&
            pet?.IsAlive == true &&
            pet.ServerObjectId != 0)
        {
            ids.Add(pet.ServerObjectId);
        }

        return ids;
    }

    private static bool IsSelectedMemberBody(
        OperationResult<LockedTargetSnapshot> result,
        TeamMemberSnapshot member)
    {
        return result.Success &&
               result.Value is not null &&
               result.Value.ObjectType == LockedTargetSnapshot.PlayerObjectType &&
               result.Value.ServerObjectId != 0 &&
               result.Value.ServerObjectId == member.ServerObjectId;
    }

    private static void LogTargetRejected(
        AccountWorkerContext context,
        TeamOutputState state,
        LockedTargetSnapshot? target,
        TeamMemberSnapshot leader,
        uint leaderTargetServerObjectId,
        string rejectReason)
    {
        if (!ShouldLog(state.LastTargetRejectLogAt))
        {
            return;
        }

        state.LastTargetRejectLogAt = DateTimeOffset.Now;
        context.Logger.Info("team_output.target.rejected", new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["reason"] = rejectReason,
            ["leader"] = leader.Name,
            ["leaderServerObjectId"] = leader.ServerObjectId,
            ["leaderClassId"] = leader.PartyMember.ClassId,
            ["leaderClass"] = leader.PartyMember.ClassName,
            ["leaderPetServerObjectId"] = leader.SummonedPet?.Pet.ServerObjectId,
            ["leaderPetSummoned"] = leader.SummonedPet?.Pet.IsSummoned,
            ["leaderPetOwnerClass"] = leader.SummonedPet?.OwnerClassName,
            ["leaderTargetServerObjectId"] = leaderTargetServerObjectId,
            ["targetName"] = target?.Name,
            ["targetServerObjectId"] = target?.ServerObjectId,
            ["targetObjectType"] = target?.ObjectType,
            ["targetCurrentHp"] = target?.CurrentHp,
            ["targetMaxHp"] = target?.MaxHp,
            ["targetTargetServerObjectId"] = target?.TargetServerObjectId
        });
    }

    private static void LogLeaderTargetSkipped(
        AccountWorkerContext context,
        TeamOutputState state,
        TeamMemberSnapshot leader,
        uint leaderTargetServerObjectId,
        string reason,
        string targetKind,
        string targetName)
    {
        if (!ShouldLog(state.LastTargetRejectLogAt))
        {
            return;
        }

        state.LastTargetRejectLogAt = DateTimeOffset.Now;
        context.Logger.Info("team_output.leader_target.skipped", new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["reason"] = reason,
            ["leader"] = leader.Name,
            ["leaderServerObjectId"] = leader.ServerObjectId,
            ["leaderTargetServerObjectId"] = leaderTargetServerObjectId,
            ["targetKind"] = targetKind,
            ["targetName"] = targetName
        });
    }

    private static void LogFollowDecision(
        AccountWorkerContext context,
        TeamOutputState state,
        string reason,
        TeamMemberSnapshot leader,
        double groupDistanceMeters,
        double groupExitDistanceMeters,
        bool leaderGroupActive,
        StationaryCombatState? combatState)
    {
        if (!ShouldLog(state.LastFollowDecisionLogAt))
        {
            return;
        }

        state.LastFollowDecisionLogAt = DateTimeOffset.Now;
        context.Logger.Info("team_output.follow.deferred", new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["reason"] = reason,
            ["leader"] = leader.Name,
            ["leaderServerObjectId"] = leader.ServerObjectId,
            ["leaderDistanceToLocal"] = leader.PartyMember.DistanceToLocalPlayer,
            ["groupDistanceMeters"] = groupDistanceMeters,
            ["groupExitDistanceMeters"] = groupExitDistanceMeters,
            ["leaderGroupActive"] = leaderGroupActive,
            ["leaderDead"] = leader.PartyMember.IsDead,
            ["leaderScreenVisible"] = leader.IsScreenVisible,
            ["leaderTargetServerObjectId"] = leader.PartyMember.LiveTargetServerObjectId,
            ["combatFighting"] = combatState?.Fighting,
            ["candidateEntityId"] = combatState?.CandidateEntityId,
            ["candidateServerObjectId"] = combatState?.CandidateServerObjectId,
            ["currentTargetEntityId"] = combatState?.CurrentTargetEntityId,
            ["currentTargetServerObjectId"] = combatState?.CurrentTargetServerObjectId
        });
    }

    private static bool RecordLeaderUnavailableTick(TeamOutputState state)
    {
        state.ConsecutiveLeaderUnavailableTicks++;
        if (state.ConsecutiveLeaderUnavailableTicks < TeamLeaderRuntimePolicy.ConsecutiveLeaderUnavailableTicksBeforeNormalWork)
        {
            return false;
        }

        state.LeaderGroupActive = false;
        return true;
    }

    private static void LogInputFailure(
        AccountWorkerContext context,
        TeamOutputState state,
        string eventName,
        string key,
        OperationResult result)
    {
        if (!ShouldLog(state.LastInputWarningAt))
        {
            return;
        }

        state.LastInputWarningAt = DateTimeOffset.Now;
        context.Logger.Warn(eventName, new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["key"] = key,
            ["error"] = result.Error
        });
    }

    private static string FormatFunctionKey(TeamMemberSnapshot member)
    {
        return member.FunctionKeyNumber is >= 1 and <= 6
            ? "F" + member.FunctionKeyNumber.ToString()
            : string.Empty;
    }

    private static TimeSpan ResolveKeyHold(AccountWorkerContext context)
    {
        var configured = context.Config.ScriptSettings?.SemiAuto?.KeyHoldMs ?? 25;
        return TimeSpan.FromMilliseconds(Math.Clamp(configured, 1, 250));
    }

    private static string ResolveAssistTargetKey(TeamOutputScriptSettings output)
    {
        return string.IsNullOrWhiteSpace(output.AssistTargetKey)
            ? TeamOutputScriptSettings.DefaultAssistTargetKey
            : output.AssistTargetKey.Trim();
    }

    private static bool ShouldLog(DateTimeOffset lastLogAt)
    {
        return DateTimeOffset.Now - lastLogAt >= WarningLogInterval;
    }

    private static Task<OperationResult<LockedTargetSnapshot>> ReadLockedTargetAsync(
        AccountWorkerContext context,
        bool bypassMemoryCache = false)
    {
        return context.GameApi is IRoadhogScopedGameApi scopedApi
            ? scopedApi.ReadLockedTargetAsync(CreateReadContext(context, bypassMemoryCache), context.StopToken)
            : context.GameApi.ReadLockedTargetAsync(context.StopToken);
    }

    private static Task<OperationResult<IReadOnlyList<WorldObjectSnapshot>>> ReadWorldObjectsAsync(AccountWorkerContext context)
    {
        return context.GameApi is IRoadhogScopedGameApi scopedApi
            ? scopedApi.ReadWorldObjectsAsync(CreateReadContext(context), context.StopToken)
            : context.GameApi.ReadWorldObjectsAsync(context.StopToken);
    }

    private static GameApiReadContext CreateReadContext(
        AccountWorkerContext context,
        bool bypassMemoryCache = false)
    {
        return new GameApiReadContext(
            context.Config.AccountName,
            context.Config.ProcessId,
            context.Config.TargetProcessName,
            context.Config.VmmDeviceName,
            bypassMemoryCache);
    }

    private readonly record struct MemberSelectionResult(
        bool Selected,
        bool AlreadySelected)
    {
        public static MemberSelectionResult NotSelected => new(false, false);

        public static MemberSelectionResult AlreadySelectedResult => new(true, true);

        public static MemberSelectionResult SelectedByInputResult => new(true, false);
    }

    private readonly record struct AssistTargetVerification(
        OperationResult<LockedTargetSnapshot> LockedTargetResult,
        bool Accepted,
        string RejectReason,
        int PollCount);
}
