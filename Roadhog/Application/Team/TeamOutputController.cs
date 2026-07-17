using Roadhog.Application.Workers;
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
    private const string LeaderAssistKey = "C";
    private const string AssistTargetKey = "Oem3";

    private readonly IKeyboardInput _keyboard;

    public TeamOutputController(IKeyboardInput keyboard)
    {
        _keyboard = keyboard;
    }

    public async Task<TeamOutputTickResult> TickAsync(
        AccountWorkerContext context,
        TeamOutputState state)
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

        if (output.StopWhenLeaderDead && leader.PartyMember.IsDead)
        {
            await SelectLeaderAndAssistAsync(context, state, leader).ConfigureAwait(false);
            return TeamOutputTickResult.SkipNormalWork(TeamOutputTickDelay);
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
        var lockedTargetResult = await ReadLockedTargetAsync(context).ConfigureAwait(false);
        if (!IsLeaderAttackTarget(lockedTargetResult, leader, leaderTargetServerObjectId, out var rejectReason))
        {
            LogTargetRejected(context, state, lockedTargetResult.Value, leader, leaderTargetServerObjectId, rejectReason);
            await SelectLeaderAndAssistAsync(context, state, leader).ConfigureAwait(false);
            return TeamOutputTickResult.SkipNormalWork(TeamOutputTickDelay);
        }

        context.Logger.Info("team_output.target.accepted", new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["leader"] = leader.Name,
            ["leaderServerObjectId"] = leader.ServerObjectId,
            ["leaderTargetServerObjectId"] = leaderTargetServerObjectId,
            ["targetName"] = lockedTargetResult.Value?.Name,
            ["targetServerObjectId"] = lockedTargetResult.Value?.ServerObjectId,
            ["targetTargetServerObjectId"] = lockedTargetResult.Value?.TargetServerObjectId
        });
        return TeamOutputTickResult.Continue(TeamOutputTickDelay);
    }

    private async Task<bool> SelectLeaderAndAssistAsync(
        AccountWorkerContext context,
        TeamOutputState state,
        TeamMemberSnapshot leader)
    {
        if (!await EnsureMemberBodySelectedAsync(context, state, leader).ConfigureAwait(false))
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

    private async Task<bool> EnsureMemberBodySelectedAsync(
        AccountWorkerContext context,
        TeamOutputState state,
        TeamMemberSnapshot member)
    {
        if (member.ServerObjectId == 0)
        {
            return false;
        }

        var current = await ReadLockedTargetAsync(context).ConfigureAwait(false);
        if (IsSelectedMemberBody(current, member))
        {
            return true;
        }

        var functionKey = FormatFunctionKey(member);
        if (string.IsNullOrWhiteSpace(functionKey))
        {
            return false;
        }

        for (var attempt = 1; attempt <= 3; attempt++)
        {
            var pressResult = await _keyboard
                .PressKeyAsync(functionKey, ResolveKeyHold(context), context.StopToken)
                .ConfigureAwait(false);
            if (!pressResult.Success)
            {
                LogInputFailure(context, state, "team_output.select_key.failed", functionKey, pressResult);
                return false;
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
                return true;
            }
        }

        return false;
    }

    private static bool IsLeaderAttackTarget(
        OperationResult<LockedTargetSnapshot> result,
        TeamMemberSnapshot leader,
        uint leaderTargetServerObjectId,
        out string rejectReason)
    {
        if (!result.Success || result.Value is null)
        {
            rejectReason = "target_read_failed";
            return false;
        }

        var target = result.Value;
        if (leaderTargetServerObjectId == 0)
        {
            rejectReason = "leader_target_unknown";
            return false;
        }

        if (target.ServerObjectId == 0 || target.ServerObjectId != leaderTargetServerObjectId)
        {
            rejectReason = "target_mismatch";
            return false;
        }

        if (!target.IsMonsterAlive)
        {
            rejectReason = "not_alive_monster";
            return false;
        }

        if (!IsTargetingLeaderSide(target, leader))
        {
            rejectReason = "not_targeting_leader_side";
            return false;
        }

        rejectReason = string.Empty;
        return true;
    }

    private static bool IsTargetingLeaderSide(
        LockedTargetSnapshot target,
        TeamMemberSnapshot leader)
    {
        if (target.TargetServerObjectId == 0)
        {
            return false;
        }

        if (target.TargetServerObjectId == leader.ServerObjectId)
        {
            return true;
        }

        var pet = leader.SummonedPet?.Pet;
        return pet?.IsSummoned == true &&
               pet.ServerObjectId != 0 &&
               target.TargetServerObjectId == pet.ServerObjectId;
    }

    private static bool IsSelectedMemberBody(
        OperationResult<LockedTargetSnapshot> result,
        TeamMemberSnapshot member)
    {
        return result.Success &&
               result.Value is not null &&
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

    private static GameApiReadContext CreateReadContext(AccountWorkerContext context)
    {
        return new GameApiReadContext(
            context.Config.AccountName,
            context.Config.ProcessId,
            context.Config.TargetProcessName,
            context.Config.VmmDeviceName);
    }
}
