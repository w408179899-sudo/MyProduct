using Roadhog.Application.CombatFlow;
using Roadhog.Application.SemiAuto;
using Roadhog.Application.Workers;
using Roadhog.Core.Accounts;
using Roadhog.Core.Model;

namespace Roadhog.Application.StationaryCombat;

public sealed partial class StationaryCombatController
{
    private StationaryRecoveryCoordinator? _recoveryCoordinator;

    private async Task<StationaryRecoveryDecision> CoordinateRecoveryAsync(
        AccountWorkerContext context, SemiAutoSkillPlan plan, SemiAutoCombatState semiAutoState,
        StationaryCombatState state, PlayerSnapshot player, Vector3Snapshot playerPosition,
        Vector3Snapshot home, double radius, CombatScriptSettings combat, double distanceFromHome,
        bool temporaryTargetSwitchGuard, bool gatherWorkAvailable)
    {
        var actions = new RecoveryActions(this, context, plan, semiAutoState, state, player,
            playerPosition, home, radius, combat, distanceFromHome, temporaryTargetSwitchGuard, gatherWorkAvailable);
        var decision = await (_recoveryCoordinator ??= new()).DecideAsync(actions).ConfigureAwait(false);
        LogActionThrottled(context, state, "combat_flow.recovery.decision", decision.Reason,
            new Dictionary<string, object?>
            {
                ["account"] = context.Config.AccountName, ["version"] = "CoordinatedV2",
                ["disposition"] = decision.Disposition.ToString(), ["reason"] = decision.Reason,
                ["allowsSit"] = decision.AllowsSit, ["wakeTarget"] = decision.WakeTarget?.ServerObjectId,
                ["defenseTarget"] = decision.DefenseTarget?.ServerObjectId
            }, TimeSpan.FromSeconds(3));
        return decision;
    }

    // All existing action semantics remain here. The V2 coordinator cannot directly access input or raw data.
    private sealed class RecoveryActions(
        StationaryCombatController owner, AccountWorkerContext context, SemiAutoSkillPlan plan,
        SemiAutoCombatState semiAutoState, StationaryCombatState state, PlayerSnapshot player,
        Vector3Snapshot playerPosition, Vector3Snapshot home, double radius, CombatScriptSettings combat,
        double distanceFromHome, bool temporaryTargetSwitchGuard, bool gatherWorkAvailable)
        : IStationaryRecoveryActions
    {
        public CancellationToken StopToken => context.StopToken;
        public bool Fighting => state.Fighting;
        public bool CurrentTargetIsDefense => state.CurrentTargetIsMaintenanceDefense;
        public bool NoTargetRestActive => state.NoTargetRestActive;
        public bool MaintenanceResting => semiAutoState.IsMaintenanceResting;
        public bool TemporaryTargetSwitchGuard => temporaryTargetSwitchGuard;
        public bool GatherBlocksMaintenance => IsGatherMaintenanceBlocked(state);
        public bool MayEnterMaintenanceRest => ShouldAttemptSitMaintenance(context, player);
        public bool MayEnterNoTargetRest => !temporaryTargetSwitchGuard && !gatherWorkAvailable &&
            CanUseNoTargetRestAtHome(combat) && distanceFromHome <= ReturnStopDistance &&
            !ShouldDelayNoTargetActionAfterLoot(state, DateTimeOffset.Now);
        public bool IsTeamLeader => IsEnabledTeamLeader(context);
        public Task<WorldObjectSnapshot?> SelectDefenseTargetAsync() =>
            owner.SelectMaintenanceDefenseTargetAsync(context, state, playerPosition);
        public Task<WorldObjectSnapshot?> SelectOrdinaryTargetAsync() =>
            owner.SelectTargetAsync(context, state, playerPosition, home, radius, combat.ContestMonster);
        public Task<WorldObjectSnapshot?> ContinueNoTargetRestAsync() =>
            owner.TickNoTargetRestAtHomeAsync(context, semiAutoState, state, player, playerPosition,
                home, radius, combat, distanceFromHome);
        public Task<bool> InterruptMaintenanceRestAsync(string reason) =>
            owner.TryInterruptMaintenanceRestForDefenseAsync(context, semiAutoState, state, player, reason);
        public Task<(bool CanSit, WorldObjectSnapshot? DefenseTarget)> EvaluateLeaderRestAsync() =>
            owner.EvaluateLeaderRestGuardAsync(context, state, playerPosition);
        public Task<bool> HandleMaintenanceAsync(bool allowSit, bool clearSit) =>
            owner.TryHandleStationaryMaintenanceAsync(context, plan, semiAutoState, state, player,
                allowSitMaintenance: allowSit, clearSitWhenDisallowed: clearSit);
        public Task StopMovementAsync() => owner.StopMovementAsync(context, state);
        public Task<bool> EnterNoTargetRestAsync() =>
            owner.TryEnterNoTargetRestAtHomeAsync(context, semiAutoState, state, player, distanceFromHome);
        public void RecordDefenseBlocksRest(WorldObjectSnapshot target) =>
            LogActionThrottled(context, state, "stationary_combat.maintenance_rest.blocked", "defense",
                new Dictionary<string, object?>
                {
                    ["account"] = context.Config.AccountName, ["reason"] = "defense_target_detected",
                    ["targetEntityId"] = target.EntityId, ["targetServerObjectId"] = target.ServerObjectId,
                    ["targetName"] = target.Name
                }, TimeSpan.FromSeconds(3));
    }
}
