using Roadhog.Application.SemiAuto;
using Roadhog.Application.Workers;
using Roadhog.Core.Accounts;
using Roadhog.Core.Model;

namespace Roadhog.Application.StationaryCombat;

public sealed partial class StationaryCombatController
{
    public async Task ReturnAfterCleanupAsync(AccountWorkerContext context, SemiAutoSkillPlan plan,
        SemiAutoCombatState semiAutoState, StationaryCombatState state, TimeSpan? timeout = null)
    {
        state.StartCleanupReturnToCombat();
        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        var limit = timeout ?? TimeSpan.FromMinutes(15);
        while (true)
        {
            context.StopToken.ThrowIfCancellationRequested();
            if (elapsed.Elapsed >= limit)
                throw new CleanupExecutionFailedException("清包后返回挂机点长时间未完成，放弃本次清包，返回主流程。", new TimeoutException());
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

    internal static bool IsFollowingRevivePath(StationaryCombatState state) =>
        state.TopLevelState == StationaryCombatTopLevelState.DeathRecovery &&
        state.DeathRecovery.Step == StationaryCombatDeathRecoveryStep.FollowRevivePath;

    public async Task<bool> PrepareCleanupTickAsync(AccountWorkerContext context, SemiAutoSkillPlan plan,
        SemiAutoCombatState semiAutoState, StationaryCombatState state, bool pauseRevivePath = false)
    {
        state.ClearPostCombatReturn();
        await SetChannelSwitchPendingAsync(context, state, true);
        // Only a scheduled cleanup may pause travel after the normal revival/pet/rest stages.
        // Keep death recovery active so a skipped or failed trip can still resume its return.
        var revivedPlayer = pauseRevivePath && IsFollowingRevivePath(state) ? await ReadPlayerAsync(context) : null;
        var pausingRevivePath = revivedPlayer?.IsAlive == true;
        PlayerSnapshot player;
        if (pausingRevivePath) player = revivedPlayer!;
        else
        {
            if ((await TickPlayerLifeGuardAsync(context, plan, semiAutoState, state, followRevivePath: true)).HasValue) return false;
            player = await ReadPlayerAsync(context);
        }
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
        if (pausingRevivePath)
        {
            context.Logger.Info("cleanup_workflow.revive_path.paused", new Dictionary<string, object?>
            {
                ["account"] = context.Config.AccountName, ["pathName"] = state.DeathRecovery.RevivePathName,
                ["pointNumber"] = state.DeathRecovery.RevivePathPointIndex + 1,
                ["pointCount"] = state.DeathRecovery.RevivePathPoints.Count
            });
            // A cleanup trip can change maps/position. Resume from a fresh position rather
            // than carrying the old point index into the town-return callback or next tick.
            state.DeathRecovery.RevivePathName = string.Empty;
            state.DeathRecovery.RevivePathPoints = Array.Empty<Vector3Snapshot>();
            state.DeathRecovery.RevivePathPointIndex = -1;
            state.DeathRecovery.ClearRevivePathLeaderSiphon();
            state.DeathRecovery.ResetRevivePathStuckTracking();
        }
        state.ClearTarget(); state.ClearLootAfterKill(); state.ClearStartupRecovery(); state.Gather.Reset();
        state.ClearPostCombatReturn();
        semiAutoState.ResetAttackWeaveStates(); semiAutoState.ResetAttackKeyPressThrottle();
        return true;
    }
}
