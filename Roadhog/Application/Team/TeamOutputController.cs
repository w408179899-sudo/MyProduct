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
    private static readonly TimeSpan AssistTargetConfirmDelay = TimeSpan.FromMilliseconds(50);
    private const int AssistTargetConfirmPolls = 3;
    private const string LeaderAssistKey = "C";
    private const string AssistTargetKey = "Oem3";

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

            return output.StopWhenLeaderHasNoTarget
                ? TeamOutputTickResult.SkipNormalWork(TeamOutputTickDelay)
                : TeamOutputTickResult.Continue(TeamOutputTickDelay);
        }

        var snapshot = snapshotResult.Value;
        var leader = snapshot.LeaderMember;
        if (leader is null || leader.IsSelf)
        {
            return TeamOutputTickResult.Continue(TeamOutputTickDelay);
        }

        if (output.AllowSelfDefense &&
            await HasSelfDefenseThreatAsync(context, state, snapshot).ConfigureAwait(false))
        {
            return TeamOutputTickResult.Continue(TeamOutputTickDelay);
        }

        if (output.StopWhenLeaderDead && leader.PartyMember.IsDead)
        {
            await SelectLeaderAndAssistAsync(context, state, leader).ConfigureAwait(false);
            return TeamOutputTickResult.SkipNormalWork(TeamOutputTickDelay);
        }

        var groupDistanceMeters = team.GroupDistanceMeters;
        var leaderInGroupRange = TeamLeaderRuntimePolicy.IsLeaderInGroupRange(
            leader,
            groupDistanceMeters);
        if (!leaderInGroupRange)
        {
            LogFollowDecision(
                context,
                state,
                "leader_out_of_range",
                leader,
                groupDistanceMeters,
                combatState);
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
                combatState);
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
        if (!await SelectLeaderAndAssistAsync(context, state, leader).ConfigureAwait(false))
        {
            return TeamOutputTickResult.SkipNormalWork(TeamOutputTickDelay);
        }

        var assistResult = await _keyboard
            .PressKeyAsync(AssistTargetKey, ResolveKeyHold(context), context.StopToken)
            .ConfigureAwait(false);
        if (!assistResult.Success)
        {
            LogInputFailure(context, state, "team_output.assist_target_key.failed", AssistTargetKey, assistResult);
            return TeamOutputTickResult.SkipNormalWork(TeamOutputTickDelay);
        }

        state.LastActionAt = DateTimeOffset.Now;
        context.Logger.Info("team_output.assist_target.pressed", new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["leader"] = leader.Name,
            ["leaderServerObjectId"] = leader.ServerObjectId,
            ["leaderTargetServerObjectId"] = leaderTargetServerObjectId,
            ["key"] = AssistTargetKey
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

        var combatAdopted = TeamCombatTargetAdopter.TryAdoptLeaderAttackTarget(
            combatState,
            verification.LockedTargetResult.Value);
        context.Logger.Info("team_output.target.accepted", new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["leader"] = leader.Name,
            ["leaderServerObjectId"] = leader.ServerObjectId,
            ["leaderTargetServerObjectId"] = leaderTargetServerObjectId,
            ["targetName"] = verification.LockedTargetResult.Value?.Name,
            ["targetServerObjectId"] = verification.LockedTargetResult.Value?.ServerObjectId,
            ["targetTargetServerObjectId"] = verification.LockedTargetResult.Value?.TargetServerObjectId,
            ["confirmPollCount"] = verification.PollCount,
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
        for (var poll = 1; poll <= AssistTargetConfirmPolls; poll++)
        {
            if (poll > 1)
            {
                await Task.Delay(AssistTargetConfirmDelay, context.StopToken).ConfigureAwait(false);
            }

            lastResult = await ReadLockedTargetAsync(context).ConfigureAwait(false);
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
        return true;
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

        var current = await ReadLockedTargetAsync(context).ConfigureAwait(false);
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
            var pressResult = await _keyboard
                .PressKeyAsync(functionKey, ResolveKeyHold(context), context.StopToken)
                .ConfigureAwait(false);
            if (!pressResult.Success)
            {
                LogInputFailure(context, state, "team_output.select_key.failed", functionKey, pressResult);
                return MemberSelectionResult.NotSelected;
            }

            await Task.Delay(SelectConfirmDelay, context.StopToken).ConfigureAwait(false);
            current = await ReadLockedTargetAsync(context).ConfigureAwait(false);
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

    private async Task<bool> HasSelfDefenseThreatAsync(
        AccountWorkerContext context,
        TeamOutputState state,
        TeamSnapshot snapshot)
    {
        var protectedServerObjectIds = GetLocalProtectedServerObjectIds(snapshot);
        if (protectedServerObjectIds.Count == 0)
        {
            return false;
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

            return false;
        }

        var threat = worldResult.Value
            .Where(StationaryCombatTargetSelector.IsSelectableMonster)
            .FirstOrDefault(target =>
                target.TargetServerObjectId != 0 &&
                protectedServerObjectIds.Contains(target.TargetServerObjectId));
        if (threat is null)
        {
            return false;
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

        return true;
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
            ["leaderTargetServerObjectId"] = leaderTargetServerObjectId,
            ["targetName"] = target?.Name,
            ["targetServerObjectId"] = target?.ServerObjectId,
            ["targetObjectType"] = target?.ObjectType,
            ["targetCurrentHp"] = target?.CurrentHp,
            ["targetMaxHp"] = target?.MaxHp,
            ["targetTargetServerObjectId"] = target?.TargetServerObjectId
        });
    }

    private static void LogFollowDecision(
        AccountWorkerContext context,
        TeamOutputState state,
        string reason,
        TeamMemberSnapshot leader,
        double groupDistanceMeters,
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

    private static bool ShouldLog(DateTimeOffset lastLogAt)
    {
        return DateTimeOffset.Now - lastLogAt >= WarningLogInterval;
    }

    private static Task<OperationResult<LockedTargetSnapshot>> ReadLockedTargetAsync(AccountWorkerContext context)
    {
        return context.GameApi is IRoadhogScopedGameApi scopedApi
            ? scopedApi.ReadLockedTargetAsync(CreateReadContext(context), context.StopToken)
            : context.GameApi.ReadLockedTargetAsync(context.StopToken);
    }

    private static Task<OperationResult<IReadOnlyList<WorldObjectSnapshot>>> ReadWorldObjectsAsync(AccountWorkerContext context)
    {
        return context.GameApi is IRoadhogScopedGameApi scopedApi
            ? scopedApi.ReadWorldObjectsAsync(CreateReadContext(context), context.StopToken)
            : context.GameApi.ReadWorldObjectsAsync(context.StopToken);
    }

    private static GameApiReadContext CreateReadContext(AccountWorkerContext context)
    {
        return new GameApiReadContext(
            context.Config.AccountName,
            context.Config.ProcessId,
            context.Config.TargetProcessName,
            context.Config.VmmDeviceName);
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
