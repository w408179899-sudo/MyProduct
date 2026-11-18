using Roadhog.Application.SemiAuto;
using Roadhog.Application.Workers;
using Roadhog.Core.Accounts;
using Roadhog.Core.Model;

namespace Roadhog.Application.StationaryCombat;

public sealed partial class StationaryCombatController
{
    // Observe only snapshots already supplied to the flow. CapturedAt distinguishes reused
    // snapshots during a multi-step loot tick from newly observed health.
    private static void ObserveCoordinatedHealth(
        AccountWorkerContext context, SemiAutoCombatState semiAutoState,
        StationaryCombatState state, PlayerSnapshot player, string boundary)
    {
        if (context.Config.CombatFlowVersion != CombatFlowVersion.CoordinatedV2)
            return;

        // Boundary keys are fixed call sites / enum values, never entity IDs or health values.
        LogActionThrottled(context, state, "combat_flow.health.observed", boundary,
            new Dictionary<string, object?>
            {
                ["account"] = context.Config.AccountName,
                ["boundary"] = boundary,
                ["hp"] = player.CurrentHp,
                ["maxHp"] = player.MaxHp,
                ["hpPercent"] = player.HpPercent,
                ["playerCapturedAt"] = player.CapturedAt,
                ["playerTargetEntityId"] = player.TargetEntityId,
                ["topLevelState"] = state.TopLevelState.ToString(),
                ["fighting"] = state.Fighting,
                ["lootStep"] = state.LootAfterKill.Step.ToString(),
                ["deathRecoveryStep"] = state.DeathRecovery.Step.ToString(),
                ["maintenanceResting"] = semiAutoState.IsMaintenanceResting,
                ["summonVerificationPending"] = semiAutoState.HasPendingSpiritmasterSummonVerification,
                ["targetEntityId"] = state.CurrentTargetEntityId,
                ["targetServerObjectId"] = state.CurrentTargetServerObjectId,
                ["candidateEntityId"] = state.CandidateEntityId,
                ["candidateServerObjectId"] = state.CandidateServerObjectId,
                ["x"] = player.Position?.X,
                ["y"] = player.Position?.Y,
                ["z"] = player.Position?.Z
            }, TimeSpan.FromMilliseconds(500));
    }
}
