using Roadhog.Core.Model;

namespace Roadhog.Application.CombatFlow;

internal enum RecoveryDisposition { Continue, Wait }

/// <summary>The handoff to target acquisition. Waiting never implies an action succeeded.</summary>
internal sealed record StationaryRecoveryDecision(
    RecoveryDisposition Disposition,
    WorldObjectSnapshot? WakeTarget,
    WorldObjectSnapshot? DefenseTarget,
    bool AllowsSit,
    string Reason);

/// <summary>
/// Observations are read at each decision boundary because actions can change posture and ownership.
/// The adapter owns snapshots and input; the coordinator owns only ordering and handoff.
/// </summary>
internal interface IStationaryRecoveryActions
{
    CancellationToken StopToken { get; }
    bool Fighting { get; }
    bool CurrentTargetIsDefense { get; }
    bool NoTargetRestActive { get; }
    bool MaintenanceResting { get; }
    bool TemporaryTargetSwitchGuard { get; }
    bool GatherBlocksMaintenance { get; }
    bool MayEnterMaintenanceRest { get; }
    bool MayEnterNoTargetRest { get; }
    bool IsTeamLeader { get; }
    Task<WorldObjectSnapshot?> SelectDefenseTargetAsync();
    Task<WorldObjectSnapshot?> SelectOrdinaryTargetAsync();
    Task<WorldObjectSnapshot?> ContinueNoTargetRestAsync();
    Task<bool> InterruptMaintenanceRestAsync(string reason);
    Task<(bool CanSit, WorldObjectSnapshot? DefenseTarget)> EvaluateLeaderRestAsync();
    Task<bool> HandleMaintenanceAsync(bool allowSit, bool clearSit);
    Task StopMovementAsync();
    Task<bool> EnterNoTargetRestAsync();
    void RecordDefenseBlocksRest(WorldObjectSnapshot target);
}

/// <summary>
/// First V2 boundary: stationary rest, resource maintenance and defensive acquisition.
/// Stateless across ticks: no pending action or external transaction can be carried into another version.
/// </summary>
internal sealed class StationaryRecoveryCoordinator
{
    public async Task<StationaryRecoveryDecision> DecideAsync(IStationaryRecoveryActions actions)
    {
        WorldObjectSnapshot? wakeTarget = null;
        WorldObjectSnapshot? defenseTarget = null;
        var allowsSit = true;
        var skipMaintenance = false;
        void CheckStop() => actions.StopToken.ThrowIfCancellationRequested();
        StationaryRecoveryDecision Result(RecoveryDisposition disposition, string reason) =>
            new(disposition, wakeTarget, defenseTarget, allowsSit, reason);

        CheckStop();
        if (actions.TemporaryTargetSwitchGuard)
        {
            wakeTarget = await actions.SelectDefenseTargetAsync().ConfigureAwait(false);
            CheckStop();
            if (wakeTarget is null)
                wakeTarget = await actions.SelectOrdinaryTargetAsync().ConfigureAwait(false);
            CheckStop();
        }

        if (actions.NoTargetRestActive)
        {
            wakeTarget = await actions.ContinueNoTargetRestAsync().ConfigureAwait(false);
            CheckStop();
            return Result(actions.NoTargetRestActive && wakeTarget is null
                ? RecoveryDisposition.Wait : RecoveryDisposition.Continue, "no_target_rest");
        }

        if (actions.MaintenanceResting)
        {
            if (actions.Fighting && actions.CurrentTargetIsDefense)
            {
                var interrupted = await actions.InterruptMaintenanceRestAsync("active_defense_target").ConfigureAwait(false);
                CheckStop();
                if (!interrupted) return Result(RecoveryDisposition.Wait, "standing_for_defense");
                skipMaintenance = true;
            }
            else
            {
                wakeTarget = await actions.SelectDefenseTargetAsync().ConfigureAwait(false);
                CheckStop();
                if (wakeTarget?.Position is not null)
                {
                    var interrupted = await actions.InterruptMaintenanceRestAsync("defense_target_detected").ConfigureAwait(false);
                    CheckStop();
                    if (!interrupted) return Result(RecoveryDisposition.Wait, "standing_for_defense");
                    skipMaintenance = true;
                }
            }
        }

        var mayEnterMaintenanceRest = actions.MayEnterMaintenanceRest;
        var mayEnterNoTargetRest = actions.MayEnterNoTargetRest;
        if (!actions.Fighting && !skipMaintenance &&
            (mayEnterMaintenanceRest || mayEnterNoTargetRest) && actions.IsTeamLeader)
        {
            var leader = await actions.EvaluateLeaderRestAsync().ConfigureAwait(false);
            CheckStop();
            allowsSit = leader.CanSit;
            defenseTarget = leader.DefenseTarget;
            wakeTarget ??= defenseTarget;
            skipMaintenance = defenseTarget is not null;
        }
        else if (!actions.Fighting && !skipMaintenance && !actions.MaintenanceResting &&
                 !actions.TemporaryTargetSwitchGuard && !actions.GatherBlocksMaintenance && mayEnterMaintenanceRest)
        {
            defenseTarget = await actions.SelectDefenseTargetAsync().ConfigureAwait(false);
            CheckStop();
            wakeTarget ??= defenseTarget;
            allowsSit = defenseTarget is null;
            if (defenseTarget is not null) actions.RecordDefenseBlocksRest(defenseTarget);
        }

        if (!actions.Fighting && !actions.GatherBlocksMaintenance && !skipMaintenance)
        {
            var handled = await actions.HandleMaintenanceAsync(
                !actions.TemporaryTargetSwitchGuard && allowsSit, actions.TemporaryTargetSwitchGuard).ConfigureAwait(false);
            CheckStop();
            if (handled)
            {
                await actions.StopMovementAsync().ConfigureAwait(false);
                CheckStop();
                return Result(RecoveryDisposition.Wait, "maintenance_owns_tick");
            }
        }

        // Re-evaluate after maintenance, matching the existing post-loot timing boundary.
        if (!actions.Fighting && allowsSit && actions.MayEnterNoTargetRest)
        {
            wakeTarget = await actions.SelectDefenseTargetAsync().ConfigureAwait(false);
            CheckStop();
            if (wakeTarget is null)
                wakeTarget = await actions.SelectOrdinaryTargetAsync().ConfigureAwait(false);
            CheckStop();
            if (wakeTarget?.Position is null)
            {
                var entered = await actions.EnterNoTargetRestAsync().ConfigureAwait(false);
                CheckStop();
                if (entered) return Result(RecoveryDisposition.Wait, "enter_no_target_rest");
            }
        }

        return Result(RecoveryDisposition.Continue, defenseTarget is not null || skipMaintenance
            ? "defense_handoff" : "normal_handoff");
    }
}
