using Roadhog.Application.Channels;
using Roadhog.Application.SemiAuto;
using Roadhog.Application.Workers;
using Roadhog.Core.Accounts;

namespace Roadhog.Application.StationaryCombat;

public sealed partial class StationaryCombatController
{
    public async Task SetChannelSwitchPendingAsync(AccountWorkerContext context, StationaryCombatState state, bool pending)
    {
        if (state.ChannelSwitchPending == pending) return;
        state.ChannelSwitchPending = pending;
        if (!pending) return;

        StopNextTargetPreAim(context, state, "fixed_channel_wait", clearCandidate: true);
        state.ClearSmartPreAimHandoff(clearDisplacedTargetGuard: true);
        await WaitForNextTargetPreAimCameraIdleAsync(context, state).ConfigureAwait(false);
        if (state.Fighting || ChannelSwitchSafety.HasExclusiveWork(state)) return;

        // Finish the monster already locked at the deadline, including semi-auto/team combat.
        // A merely planned next candidate must not keep the account busy forever.
        var locked = await ReadLockedTargetAsync(context).ConfigureAwait(false);
        if (locked.IsMonsterAlive)
        {
            state.SetCurrentTarget(locked);
            state.MarkCandidate(locked.TargetEntityId, locked.ServerObjectId, DateTimeOffset.Now);
            state.Fighting = true;
        }
        else state.ClearTarget();
    }

    // Called after the life guard and before team target acquisition or normal work.
    public async Task<TimeSpan?> TryTickChannelSwitchWaitAsync(AccountWorkerContext context, SemiAutoSkillPlan plan,
        SemiAutoCombatState semiAutoState, StationaryCombatState state)
    {
        if (!state.ChannelSwitchPending || ChannelSwitchSafety.HasExclusiveWork(state)) return null;

        var player = await ReadPlayerAsync(context).ConfigureAwait(false);
        if (player.IsDead) return IdleDelay; // Life guard runs first on every worker tick.
        var position = player.Position!.Value;
        await RefreshLocalCombatSideAsync(context, plan, state, player).ConfigureAwait(false);
        var home = position;
        var radius = Math.Max(1d, context.Config.ScriptSettings?.Combat?.StationaryCombatRadius ?? 45d);
        if (context.Config.ScriptSettings is { MainMode: AccountMainMode.CustomCombat, CombatMode: AccountCombatMode.Stationary })
        {
            var resolved = await TryResolveStationaryHomeAsync(context, state).ConfigureAwait(false);
            if (resolved.Success && resolved.Value is not null) home = resolved.Value.Position;
        }
        var fighting = await TryHandleRecoveryDefenseTargetAsync(context, plan, semiAutoState, state,
            player, position, home, radius, StationaryCombatTargetSelector.HorizontalDistance(position, home),
            "fixed_channel_wait", allowRevivePathClear: false).ConfigureAwait(false);
        if (fighting.HasValue) return fighting;

        semiAutoState.ResetAttackKeyPressThrottle();
        await StopSoloJumpAsync(state, "fixed_channel_wait").ConfigureAwait(false);
        await StopMovementAsync(context, state, releaseRightMouse: true).ConfigureAwait(false);
        StopPathFollowPoller(state);
        state.ClearTarget();
        LogActionThrottled(context, state, "fixed_channel.wait.peace", "holding", new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName, ["reason"] = "no_new_target_until_channel_attempt"
        }, TimeSpan.FromSeconds(5));
        return IdleDelay;
    }
}
