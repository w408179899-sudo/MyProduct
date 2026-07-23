using Roadhog.Application.StationaryCombat;
using Roadhog.Application.Workers;
using Roadhog.Core.Common;
using Roadhog.Core.Input;
using Roadhog.Core.Model;

namespace Roadhog.Application.Team;

internal static class TeamLeaderRestSync
{
    private static readonly TimeSpan ActionRetryInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan WarningLogInterval = TimeSpan.FromSeconds(3);
    private const string RestEnterKey = "OemComma";
    private const string RestExitKey = "X";
    private const string MoveForwardKey = "W";

    public static async Task<bool> TryHandleAsync(
        AccountWorkerContext context,
        IKeyboardInput keyboard,
        TeamLeaderRestSyncState state,
        TeamSnapshot snapshot,
        TeamMemberSnapshot leader,
        StationaryCombatState? combatState,
        string logPrefix)
    {
        var local = snapshot.LocalMember;
        if (local is null ||
            local.ServerObjectId == 0 ||
            leader.ServerObjectId == 0 ||
            leader.IsSelf ||
            local.PartyMember.IsDead ||
            leader.PartyMember.IsDead ||
            !local.PartyMember.HasLiveRestState ||
            !leader.PartyMember.HasLiveRestState)
        {
            return false;
        }

        var shouldRest = leader.PartyMember.IsResting;
        if (local.PartyMember.IsResting == shouldRest)
        {
            state.RememberObservedLeaderRestState(shouldRest);
            return false;
        }

        var now = DateTimeOffset.Now;
        if (!state.ShouldPress(shouldRest, now, ActionRetryInterval))
        {
            return true;
        }

        var key = shouldRest ? RestEnterKey : RestExitKey;
        if (shouldRest)
        {
            await ReleaseActiveInputAsync(context, keyboard, combatState, logPrefix).ConfigureAwait(false);
            combatState?.ClearTarget();
        }

        var result = await keyboard
            .PressKeyAsync(key, ResolveKeyHold(context), context.StopToken)
            .ConfigureAwait(false);
        state.MarkAction(shouldRest, DateTimeOffset.Now);

        if (!result.Success)
        {
            LogInputFailure(context, state, logPrefix, key, result);
            return true;
        }

        context.Logger.Info(logPrefix + ".leader_rest_sync.key_pressed", new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["key"] = key,
            ["desiredResting"] = shouldRest,
            ["leader"] = leader.Name,
            ["leaderServerObjectId"] = leader.ServerObjectId,
            ["leaderStanceFlags"] = leader.PartyMember.LiveStanceFlags,
            ["leaderStanceLow"] = leader.PartyMember.LiveStanceLowNibble,
            ["leaderMotionMode"] = leader.PartyMember.LiveMotionMode,
            ["local"] = local.Name,
            ["localServerObjectId"] = local.ServerObjectId,
            ["localResting"] = local.PartyMember.IsResting,
            ["localStanceFlags"] = local.PartyMember.LiveStanceFlags,
            ["localStanceLow"] = local.PartyMember.LiveStanceLowNibble,
            ["localMotionMode"] = local.PartyMember.LiveMotionMode
        });
        return true;
    }

    private static async Task ReleaseActiveInputAsync(
        AccountWorkerContext context,
        IKeyboardInput keyboard,
        StationaryCombatState? combatState,
        string logPrefix)
    {
        if (combatState is null)
        {
            return;
        }

        if (combatState.IsMovingForward)
        {
            var result = await keyboard.KeyUpAsync(MoveForwardKey, context.StopToken).ConfigureAwait(false);
            combatState.IsMovingForward = false;
            LogInputRelease(context, logPrefix, MoveForwardKey, "key_up", result);
        }

        if (combatState.IsRightMouseDown)
        {
            var result = await keyboard.MouseUpAsync(RoadhogMouseButton.Right, context.StopToken).ConfigureAwait(false);
            combatState.IsRightMouseDown = false;
            LogInputRelease(context, logPrefix, "Right", "mouse_up", result);
        }
    }

    private static void LogInputRelease(
        AccountWorkerContext context,
        string logPrefix,
        string input,
        string action,
        OperationResult result)
    {
        var fields = new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["input"] = input,
            ["action"] = action
        };

        if (result.Success)
        {
            context.Logger.Info(logPrefix + ".leader_rest_sync.input_released", fields);
            return;
        }

        fields["error"] = result.Error;
        context.Logger.Warn(logPrefix + ".leader_rest_sync.input_release.failed", fields);
    }

    private static void LogInputFailure(
        AccountWorkerContext context,
        TeamLeaderRestSyncState state,
        string logPrefix,
        string key,
        OperationResult result)
    {
        if (DateTimeOffset.Now - state.LastWarningAt < WarningLogInterval)
        {
            return;
        }

        state.LastWarningAt = DateTimeOffset.Now;
        context.Logger.Warn(logPrefix + ".leader_rest_sync.key.failed", new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["key"] = key,
            ["error"] = result.Error
        });
    }

    private static TimeSpan ResolveKeyHold(AccountWorkerContext context)
    {
        var configured = context.Config.ScriptSettings?.SemiAuto?.KeyHoldMs ?? 25;
        return TimeSpan.FromMilliseconds(Math.Clamp(configured, 1, 250));
    }
}

public sealed class TeamLeaderRestSyncState
{
    private bool? lastDesiredResting;

    public DateTimeOffset LastActionAt { get; private set; } = DateTimeOffset.MinValue;

    public DateTimeOffset LastWarningAt { get; set; } = DateTimeOffset.MinValue;

    public bool ShouldPress(bool desiredResting, DateTimeOffset now, TimeSpan retryInterval)
    {
        return lastDesiredResting != desiredResting ||
               LastActionAt == DateTimeOffset.MinValue ||
               now - LastActionAt >= retryInterval;
    }

    public void MarkAction(bool desiredResting, DateTimeOffset now)
    {
        lastDesiredResting = desiredResting;
        LastActionAt = now;
    }

    public void RememberObservedLeaderRestState(bool desiredResting)
    {
        lastDesiredResting = desiredResting;
    }
}
