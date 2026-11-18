using Roadhog.Application.CombatFlow;
using Roadhog.Core.Model;

internal static class CoordinatorContractTests
{
    public static async Task CancellationAfterEveryActionStopsHandoffAsync()
    {
        foreach (var boundary in new[]
                 { "defense", "ordinary", "continue_rest", "interrupt_rest", "leader", "maintenance", "stop_movement", "enter_rest" })
        {
            using var a = new Actions { CancelAfter = boundary };
            switch (boundary)
            {
                case "defense":
                case "ordinary": a.TemporaryTargetSwitchGuard = true; break;
                case "continue_rest": a.NoTargetRestActive = true; break;
                case "interrupt_rest":
                    a.MaintenanceResting = true;
                    a.Fighting = a.CurrentTargetIsDefense = true;
                    break;
                case "leader": a.IsTeamLeader = a.MayEnterMaintenanceRest = true; break;
                case "stop_movement": a.MaintenanceHandled = true; break;
                case "enter_rest": a.MayEnterNoTargetRest = true; break;
            }
            try
            {
                await new StationaryRecoveryCoordinator().DecideAsync(a);
                throw new InvalidOperationException(boundary + ": cancellation was swallowed");
            }
            catch (OperationCanceledException) { }
            Check(a.Calls.LastOrDefault() == boundary,
                boundary + ": an action that completes after Stop must not hand control to another action");
        }
    }

    public static async Task LeaderThreatHandsOffWithoutMaintenanceAsync()
    {
        using var a = new Actions
        {
            IsTeamLeader = true, MayEnterMaintenanceRest = true,
            LeaderAllowsSit = false, LeaderTarget = Target()
        };
        var result = await new StationaryRecoveryCoordinator().DecideAsync(a);
        Check(result.Disposition == RecoveryDisposition.Continue && !result.AllowsSit &&
              result.WakeTarget == a.LeaderTarget && result.DefenseTarget == a.LeaderTarget,
            "the leader's selected defense target must survive the handoff with sitting denied");
        Check(a.Calls.SequenceEqual(new[] { "leader" }),
            "teammate protection owns this handoff before ordinary maintenance or no-target rest");
    }

    public static async Task StandingConfirmationUsesPostActionStateAsync()
    {
        foreach (var remainsSeated in new[] { false, true })
        {
            using var a = new Actions { NoTargetRestActive = true, RestRemainsActive = remainsSeated };
            var result = await new StationaryRecoveryCoordinator().DecideAsync(a);
            Check(result.Disposition == (remainsSeated ? RecoveryDisposition.Wait : RecoveryDisposition.Continue),
                "rest continuation must use the state after the action, not its initial flag");
            Check(a.Calls.SequenceEqual(new[] { "continue_rest" }),
                "rest completion yields to the outer tick without starting a second maintenance action");
        }
    }

    public static async Task DefenseDeniesSitButPreservesResourceMaintenanceAsync()
    {
        using var a = new Actions
        {
            MayEnterMaintenanceRest = true, DefenseTarget = Target(), MaintenanceHandled = true
        };
        var result = await new StationaryRecoveryCoordinator().DecideAsync(a);
        Check(result.Disposition == RecoveryDisposition.Wait && !result.AllowsSit &&
              result.DefenseTarget == a.DefenseTarget && result.WakeTarget == a.DefenseTarget,
            "resource maintenance can own the tick while the selected threat remains available for later acquisition");
        Check(a.MaintenanceArguments is { AllowSit: false, ClearSit: false } &&
              a.Calls.SequenceEqual(new[] { "defense", "record_defense", "maintenance", "stop_movement" }),
            "a threat only forbids sitting; successful resource maintenance still releases movement");
    }

    public static async Task GuardAndGatherPreserveOwnershipAsync()
    {
        using (var guarded = new Actions { TemporaryTargetSwitchGuard = true, MaintenanceHandled = true })
        {
            var result = await new StationaryRecoveryCoordinator().DecideAsync(guarded);
            Check(result.Disposition == RecoveryDisposition.Wait &&
                  guarded.MaintenanceArguments is { AllowSit: false, ClearSit: true } &&
                  !guarded.Calls.Contains("enter_rest"),
                "target-switch guard explicitly clears rest while preserving ordinary maintenance");
        }
        using (var gathering = new Actions { GatherBlocksMaintenance = true, MayEnterMaintenanceRest = true })
        {
            var result = await new StationaryRecoveryCoordinator().DecideAsync(gathering);
            Check(result.Disposition == RecoveryDisposition.Continue && gathering.Calls.Count == 0,
                "active gathering retains ownership without triggering maintenance or threat scans for sitting");
        }
    }

    private static WorldObjectSnapshot Target() =>
        new(200, 2000, "attacker", "monster", new(50, 0, 0), 50, 1000, 1000,
            TargetServerObjectId: 100, IsTargetingLocalPlayer: true);

    private sealed class Actions : IStationaryRecoveryActions, IDisposable
    {
        private readonly CancellationTokenSource _stop = new();
        public CancellationToken StopToken => _stop.Token;
        public bool Fighting { get; set; }
        public bool CurrentTargetIsDefense { get; set; }
        public bool NoTargetRestActive { get; set; }
        public bool MaintenanceResting { get; set; }
        public bool TemporaryTargetSwitchGuard { get; set; }
        public bool GatherBlocksMaintenance { get; set; }
        public bool MayEnterMaintenanceRest { get; set; }
        public bool MayEnterNoTargetRest { get; set; }
        public bool IsTeamLeader { get; set; }
        public WorldObjectSnapshot? DefenseTarget { get; set; }
        public WorldObjectSnapshot? LeaderTarget { get; set; }
        public bool LeaderAllowsSit { get; set; } = true;
        public bool RestRemainsActive { get; set; }
        public bool MaintenanceHandled { get; set; }
        public string? CancelAfter { get; set; }
        public List<string> Calls { get; } = new();
        public (bool AllowSit, bool ClearSit)? MaintenanceArguments { get; private set; }

        private void Record(string name)
        {
            if (_stop.IsCancellationRequested)
                throw new InvalidOperationException("Action " + name + " began after cancellation at " + CancelAfter);
            Calls.Add(name);
            if (name == CancelAfter) _stop.Cancel();
        }

        // Intentionally complete normally after cancellation: coordinator, not the fake, must enforce the boundary.
        public Task<WorldObjectSnapshot?> SelectDefenseTargetAsync()
        {
            Record("defense");
            return Task.FromResult(DefenseTarget);
        }
        public Task<WorldObjectSnapshot?> SelectOrdinaryTargetAsync()
        {
            Record("ordinary");
            return Task.FromResult<WorldObjectSnapshot?>(null);
        }
        public Task<WorldObjectSnapshot?> ContinueNoTargetRestAsync()
        {
            Record("continue_rest");
            NoTargetRestActive = RestRemainsActive;
            return Task.FromResult<WorldObjectSnapshot?>(null);
        }
        public Task<bool> InterruptMaintenanceRestAsync(string reason)
        {
            Record("interrupt_rest");
            MaintenanceResting = false;
            return Task.FromResult(true);
        }
        public Task<(bool CanSit, WorldObjectSnapshot? DefenseTarget)> EvaluateLeaderRestAsync()
        {
            Record("leader");
            return Task.FromResult((LeaderAllowsSit, LeaderTarget));
        }
        public Task<bool> HandleMaintenanceAsync(bool allowSit, bool clearSit)
        {
            Record("maintenance");
            MaintenanceArguments = (allowSit, clearSit);
            return Task.FromResult(MaintenanceHandled);
        }
        public Task StopMovementAsync()
        {
            Record("stop_movement");
            return Task.CompletedTask;
        }
        public Task<bool> EnterNoTargetRestAsync()
        {
            Record("enter_rest");
            return Task.FromResult(true);
        }
        public void RecordDefenseBlocksRest(WorldObjectSnapshot target) => Record("record_defense");
        public void Dispose() => _stop.Dispose();
    }

    private static void Check(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }
}
