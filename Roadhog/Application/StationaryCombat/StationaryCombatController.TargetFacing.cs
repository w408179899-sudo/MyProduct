using Roadhog.Application.Workers;
using Roadhog.Core.Accounts;
using Roadhog.Core.Model;

namespace Roadhog.Application.StationaryCombat;

public sealed partial class StationaryCombatController
{
    private async Task<bool> PrepareStationaryDefenseTabAsync(
        AccountWorkerContext context,
        StationaryCombatState state,
        WorldObjectSnapshot target)
    {
        if (context.Config.ScriptSettings?.CombatMode != AccountCombatMode.Stationary ||
            !state.CurrentTargetIsMaintenanceDefense ||
            state.CurrentTargetIsGatherSafetyClear ||
            state.CurrentTargetIsRevivePathClear ||
            state.CurrentTargetIsTacticalMark)
        {
            return true;
        }

        // A defense target can move while attacking our pet. Earlier facing and
        // waypoint turns cannot authorize the next Tab, including a wrong-lock retry.
        state.FacedCandidateEntityId = 0;
        var position = await ReadFreshTabTargetPositionAsync(context, target).ConfigureAwait(false);
        if (position.Position is { } targetPosition)
        {
            var player = await ReadPlayerForActionAsync(context).ConfigureAwait(false);
            if (await FaceTargetStepAsync(context, state, player, targetPosition, target).ConfigureAwait(false))
            {
                state.FacedCandidateEntityId = target.EntityId;
                return true;
            }
        }

        LogActionThrottled(context, state, "stationary_combat.target.defense_face_wait",
            TargetActionKey(target.EntityId, target.ServerObjectId), new Dictionary<string, object?>
            {
                ["account"] = context.Config.AccountName,
                ["targetEntityId"] = target.EntityId,
                ["targetServerObjectId"] = target.ServerObjectId,
                ["targetName"] = target.Name,
                ["reason"] = position.Position is null ? "target_position_missing" : "face_incomplete"
            }, TimeSpan.FromMilliseconds(500));
        return false;
    }

    private async Task<bool> PrepareOrdinaryStationaryAcquireTabAsync(
        AccountWorkerContext context,
        StationaryCombatState state,
        WorldObjectSnapshot target)
    {
        if (state.Fighting ||
            context.Config.ScriptSettings?.CombatMode != AccountCombatMode.Stationary ||
            state.CurrentTargetIsMaintenanceDefense ||
            state.CurrentTargetIsGatherSafetyClear ||
            state.CurrentTargetIsRevivePathClear ||
            state.CurrentTargetIsTacticalMark)
        {
            return true;
        }

        // Pre-aim and earlier facing do not survive a detour or a moving candidate.
        // Check at the actual Tab boundary, including pending-lock retries, after radar permits it.
        state.FacedCandidateEntityId = 0;
        var position = await ReadFreshTabTargetPositionAsync(context, target).ConfigureAwait(false);
        var faced = false;
        if (position.Position is { } targetPosition)
        {
            var player = await ReadPlayerForActionAsync(context).ConfigureAwait(false);
            faced = await FaceTargetStepAsync(context, state, player, targetPosition, target).ConfigureAwait(false);
        }

        if (faced)
        {
            state.FacedCandidateEntityId = target.EntityId;
            return true;
        }

        LogActionThrottled(context, state, "stationary_combat.target.acquire_face_wait",
            TargetActionKey(target.EntityId, target.ServerObjectId), new Dictionary<string, object?>
            {
                ["account"] = context.Config.AccountName,
                ["targetEntityId"] = target.EntityId,
                ["targetServerObjectId"] = target.ServerObjectId,
                ["targetName"] = target.Name,
                ["reason"] = position.Position is null ? "target_position_missing" : "face_incomplete"
            }, TimeSpan.FromMilliseconds(500));
        return false;
    }
}
