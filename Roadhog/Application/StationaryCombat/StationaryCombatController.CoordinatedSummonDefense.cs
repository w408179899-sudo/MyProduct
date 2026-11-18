using Roadhog.Application.SemiAuto;
using Roadhog.Application.Workers;
using Roadhog.Core.Accounts;

namespace Roadhog.Application.StationaryCombat;

public sealed partial class StationaryCombatController
{
    public async Task<bool> TryHandleCoordinatedSummonRetryDefenseAsync(
        AccountWorkerContext context, SemiAutoSkillPlan plan, SemiAutoCombatState semiAutoState,
        StationaryCombatState state)
    {
        context.StopToken.ThrowIfCancellationRequested();
        if (context.Config.CombatFlowVersion != CombatFlowVersion.CoordinatedV2 ||
            !semiAutoState.CoordinatedSummonDefenseAllowed ||
            semiAutoState.IsAwaitingSpiritmasterSummonVerification(DateTimeOffset.Now) ||
            context.Config.ScriptSettings is not { MainMode: AccountMainMode.CustomCombat, CombatMode: AccountCombatMode.Stationary } ||
            state.TopLevelState != StationaryCombatTopLevelState.Normal || state.BagCleanup.Active ||
            state.LootAfterKill.Active || state.Fighting && !state.CurrentTargetIsMaintenanceDefense)
            return false;

        var player = await ReadPlayerAsync(context).ConfigureAwait(false);
        context.StopToken.ThrowIfCancellationRequested();
        // A newly observed death consumes this turn. Returning false here would
        // let the caller send a summon using its earlier living-player snapshot.
        if (!player.IsAlive) return true;
        if (player.Position is null) return false;

        var combat = context.Config.ScriptSettings.Combat;
        var resolved = await TryResolveStationaryHomeAsync(context, state).ConfigureAwait(false);
        context.StopToken.ThrowIfCancellationRequested();
        if (!resolved.Success || resolved.Value is null) return false;
        var home = resolved.Value.Position;
        var distance = StationaryCombatTargetSelector.HorizontalDistance(player.Position.Value, home);
        state.CoordinatedSummonDefenseTick = true;
        try
        {
            var handled = await TryHandleRecoveryDefenseTargetAsync(context, plan, semiAutoState, state,
                player, player.Position.Value, home, Math.Max(1D, combat.StationaryCombatRadius), distance,
                "pet_recovery", allowRevivePathClear: false).ConfigureAwait(false) is not null;
            if (handled)
                LogActionThrottled(context, state, "combat_flow.summon.defense_turn", "pet_recovery",
                    new Dictionary<string, object?>
                    {
                        ["account"] = context.Config.AccountName, ["version"] = "CoordinatedV2",
                        ["reason"] = semiAutoState.CoordinatedSummonDefenseReason ?? "defense_after_summon_timeout", ["hpPercent"] = player.HpPercent,
                        ["fighting"] = state.Fighting, ["targetServerObjectId"] = state.CurrentTargetServerObjectId,
                        ["candidateServerObjectId"] = state.CandidateServerObjectId
                    }, TimeSpan.FromSeconds(3));
            return handled;
        }
        finally
        {
            // This flag scopes nested combat/maintenance input to this call only.
            state.CoordinatedSummonDefenseTick = false;
        }
    }
}
