using System.Text.Json;
using Roadhog.Core.Accounts;

internal static partial class WorkerProcessManagerTests
{
    public static async Task CombatFlowRollbackStopsOnlyNewAccountsAsync()
    {
        await using var test = new TestEnvironment();
        var candidate = test.Account(1);
        candidate.CombatFlowVersion = CombatFlowVersion.CoordinatedV2;
        var stable = test.Account(2);
        var manager = await test.ManagerAsync([candidate, stable]);
        RequireAll(await Task.WhenAll(manager.StartAsync(candidate.InstanceId), manager.StartAsync(stable.InstanceId)), "both versions start");
        var candidatePid = (await test.RunningAsync(manager, candidate)).WorkerProcessId!.Value;
        var stablePid = (await test.RunningAsync(manager, stable)).WorkerProcessId!.Value;
        var startPath = Path.Combine(manager.PathsFor(candidate).LogDirectory, "normal-start.json");
        var started = JsonSerializer.Deserialize<AccountConfig>(await File.ReadAllTextAsync(startPath))!;
        Require(started.CombatFlowVersion == CombatFlowVersion.CoordinatedV2, "account opt-in reaches worker start");

        var stopped = await manager.SetForceLegacyCombatFlowAsync(true);
        Require(stopped.Count == 1 && stopped[candidate.AccountName].Success, "only new-flow account is stopped");
        Require(!IsAlive(candidatePid) && IsAlive(stablePid), "old new-flow process exits before stable restart; legacy account continues");
        Require(!View(manager, candidate).DesiredRunning && View(manager, candidate).WorkerProcessId is null,
            "rollback stops run intent instead of replaying an unconfirmed workflow");
        Require((await manager.CombatFlowPolicy.LoadAsync()).Value!.ForceLegacy, "rollback is durable");
        Require(View(manager, candidate).Config.CombatFlowVersion == CombatFlowVersion.CoordinatedV2,
            "rollback does not erase account selection");

        Require((await manager.StartAsync(candidate.InstanceId)).Success, "explicit start after state inspection is allowed");
        var restartedPid = (await test.RunningAsync(manager, candidate)).WorkerProcessId!.Value;
        started = JsonSerializer.Deserialize<AccountConfig>(await File.ReadAllTextAsync(startPath))!;
        Require(started.CombatFlowVersion == CombatFlowVersion.Legacy, "global rollback overrides explicit account start");
        Require((await manager.SetForceLegacyCombatFlowAsync(true)).Count == 0 && IsAlive(restartedPid),
            "repeating rollback leaves an already stable worker running");
        using (var child = System.Diagnostics.Process.GetProcessById(restartedPid)) child.Kill(true);
        await UntilAsync(() => View(manager, candidate) is { State: "running", WorkerProcessId: { } pid } && pid != restartedPid,
            "auto recovery replaces killed mock child");
        started = JsonSerializer.Deserialize<AccountConfig>(await File.ReadAllTextAsync(startPath))!;
        Require(started.CombatFlowVersion == CombatFlowVersion.Legacy, "auto recovery cannot re-enable new flow");
        Require(View(manager, stable).WorkerProcessId == stablePid, "legacy process remains untouched across rollback and recovery");
        Require((await manager.StopAsync(candidate.InstanceId)).Success, "explicit stop completes");
        Require((await manager.SetForceLegacyCombatFlowAsync(false)).Count == 0, "release changes startup policy only");
        Require(!View(manager, candidate).DesiredRunning && View(manager, candidate).WorkerProcessId is null,
            "releasing override never restarts a stopped account");
    }

    public static async Task CombatFlowRollbackCancelsPendingStartAsync()
    {
        await using var test = new TestEnvironment();
        var candidate = test.Account(1);
        candidate.CombatFlowVersion = CombatFlowVersion.CoordinatedV2;
        var manager = await test.ManagerAsync([candidate], scenario: "slow-start");
        var starting = manager.StartAsync(candidate.InstanceId);
        var entered = Path.Combine(manager.PathsFor(candidate).LogDirectory, "start-entered");
        await UntilAsync(() => File.Exists(entered), "new-flow start is in progress");
        var stopped = await manager.SetForceLegacyCombatFlowAsync(true);
        Require(stopped.Count == 1 && stopped[candidate.AccountName].Success, "rollback stops pending start");
        Require(!(await starting).Success, "pending start cannot win after rollback stop");
        Require(!View(manager, candidate).DesiredRunning && View(manager, candidate).WorkerProcessId is null,
            "cancelled generation cannot auto-restart");
    }

    public static async Task CombatFlowRollbackCompletesAfterManagerReopenAsync()
    {
        await using var test = new TestEnvironment();
        var candidate = test.Account(1);
        candidate.CombatFlowVersion = CombatFlowVersion.CoordinatedV2;
        var stable = test.Account(2);
        var previous = await test.ManagerAsync([candidate, stable], scenario: "combat-flow-versioned");
        RequireAll(await Task.WhenAll(previous.StartAsync(candidate.InstanceId), previous.StartAsync(stable.InstanceId)), "both workers start");
        var candidatePid = (await test.RunningAsync(previous, candidate)).WorkerProcessId!.Value;
        var stablePid = (await test.RunningAsync(previous, stable)).WorkerProcessId!.Value;
        Require(View(previous, candidate).Worker?.ActiveCombatFlowVersion == CombatFlowVersion.CoordinatedV2 &&
            View(previous, stable).Worker?.ActiveCombatFlowVersion == CombatFlowVersion.Legacy,
            "workers report the actual version selected when execution started");
        // Simulate losing the manager immediately after its durable policy write,
        // before it has sent Stop to the existing new-flow worker.
        Require((await previous.CombatFlowPolicy.SaveAsync(true)).Success, "rollback intent persists before shutdown");
        await test.DetachAsync(previous);
        Require(IsAlive(candidatePid) && IsAlive(stablePid), "both workers survive manager interruption");
        var reopened = await test.ManagerAsync([candidate, stable], scenario: "combat-flow-versioned");
        Require(!IsAlive(candidatePid) && !View(reopened, candidate).DesiredRunning && View(reopened, candidate).WorkerProcessId is null,
            "adoption completes persisted rollback before automatic recovery");
        Require(File.Exists(Path.Combine(reopened.PathsFor(candidate).LogDirectory, "input-released")),
            "adoption uses normal input-release shutdown");
        Require((await test.RunningAsync(reopened, stable)).WorkerProcessId == stablePid,
            "existing legacy worker is adopted without restart");
        Require((await test.InfoAsync(stable)).Starts == 1, "legacy execution is never replayed");
        Require((await reopened.CombatFlowPolicy.LoadAsync()).Value!.ForceLegacy, "global rollback remains persisted");
    }
}
