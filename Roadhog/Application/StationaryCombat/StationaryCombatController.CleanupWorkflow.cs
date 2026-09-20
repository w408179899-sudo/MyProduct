using Roadhog.Application.SemiAuto;
using Roadhog.Application.Workers;
using Roadhog.Core.Accounts;

namespace Roadhog.Application.StationaryCombat;

public sealed partial class StationaryCombatController
{
    public async Task ReturnAfterCleanupAsync(AccountWorkerContext context, SemiAutoSkillPlan plan,
        SemiAutoCombatState semiAutoState, StationaryCombatState state)
    {
        state.StartCleanupReturnToCombat();
        while (true)
        {
            context.StopToken.ThrowIfCancellationRequested();
            var life = await TickPlayerLifeGuardAsync(context, plan, semiAutoState, state, followRevivePath: true);
            if (life.HasValue)
            {
                await Task.Delay(life.Value, context.StopToken);
                continue;
            }
            if (!state.CleanupReturnToCombatActive) state.StartCleanupReturnToCombat();
            var home = await TryResolveStationaryHomeAsync(context, state);
            if (!home.Success || home.Value == null) throw new InvalidOperationException("清包后无法确认挂机点：" + home.Error);
            var player = await ReadPlayerAsync(context);
            if (await TickCleanupReturnToCombatAsync(context, plan, semiAutoState, state, player) == StationaryCombatBehaviorStatus.Success) return;
            await Task.Delay(MoveTickDelay, context.StopToken);
        }
    }

    public async Task<bool> PrepareCleanupTickAsync(AccountWorkerContext context, SemiAutoSkillPlan plan,
        SemiAutoCombatState semiAutoState, StationaryCombatState state)
    {
        await SetChannelSwitchPendingAsync(context, state, true);
        if ((await TickPlayerLifeGuardAsync(context, plan, semiAutoState, state, followRevivePath: true)).HasValue) return false;
        var player = await ReadPlayerAsync(context);
        await RefreshLocalCombatSideAsync(context, plan, state, player);
        if (context.Config.ScriptSettings?.MainMode == AccountMainMode.SemiAuto && !state.Fighting)
        {
            var target = (await context.Snapshots.ReadLockedTargetAsync()).Value;
            if (target.IsMonsterAlive)
            {
                await _semiAuto.TickAsync(context, plan, semiAutoState);
                return false;
            }
        }
        var position = player.Position!.Value;
        var fight = await TryHandleRecoveryDefenseTargetAsync(context, plan, semiAutoState, state, player,
            position, position, Math.Max(1, context.Config.ScriptSettings?.Combat.StationaryCombatRadius ?? 45), 0,
            "cleanup_workflow", allowRevivePathClear: false);
        if (fight.HasValue) return false;
        if (player.IsResting)
        {
            if (!state.NoTargetRestExitPending)
            {
                var stand = await _input.PressKeyAsync("X", TimeSpan.FromMilliseconds(60), context.StopToken);
                if (!stand.Success) throw new InvalidOperationException("清包前无法起身：" + stand.Error);
                state.MarkNoTargetRestKey(DateTimeOffset.Now);
                state.MarkNoTargetRestExitPending();
            }
            else if (DateTimeOffset.Now - state.LastNoTargetRestKeyAt > TimeSpan.FromSeconds(8))
                throw new InvalidOperationException("清包前未确认角色起身。");
            return false;
        }
        semiAutoState.ClearMaintenanceRest(); state.ClearNoTargetRest();
        await StopSoloJumpAsync(state, "cleanup_workflow");
        await StopMovementAsync(context, state, releaseRightMouse: true);
        StopPathFollowPoller(state);
        state.ClearTarget(); state.ClearLootAfterKill(); state.ClearStartupRecovery(); state.Gather.Reset();
        semiAutoState.AttackWeave.Reset(); semiAutoState.ResetAttackKeyPressThrottle();
        return true;
    }
}
