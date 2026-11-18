using Roadhog.Core.Common;
using Roadhog.Application.GameSessions;
using Roadhog.Infrastructure.Hardware;

internal static partial class WorkerProcessManagerTests
{
    public static async Task ColdWorldSnapshotEscalatesAfterThreeRestartsAsync()
    {
        await using var test = new TestEnvironment();
        var account = test.Account(1);
        account.AutoRecover = false;
        var neighbor = test.Account(2);
        var manager = await test.ManagerAsync(new[] { account, neighbor });
        Require((await manager.StartAsync(account.InstanceId)).Success, "start blocked account");
        Require((await manager.StartAsync(neighbor.InstanceId)).Success, "start neighboring account");
        var neighborPid = (await test.RunningAsync(manager, neighbor)).WorkerProcessId;

        Task<OperationResult> ReportStallAsync() => test.Client(account).CallAsync<OperationResult>(
            "mock.script.restart", new object?[] { Guid.NewGuid(), true });
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            var pid = (await test.RunningAsync(manager, account)).WorkerProcessId;
            Require((await ReportStallAsync()).Success, "report missing initial world publication");
            await UntilAsync(() => View(manager, account) is { State: "running", WorkerProcessId: { } current } && current != pid &&
                test.Logger.Entries.Count(e => e.EventName == "account.restart.complete") == attempt, "replace worker once for each failed startup");
            Require(!test.Logger.Entries.Any(e => e.EventName == "game_session.world_snapshot_relogin_requested"),
                "three completed worker replacements are required for escalation");
        }

        var stalledPid = (await test.RunningAsync(manager, account)).WorkerProcessId;
        Require((await ReportStallAsync()).Success, "report that the third replacement is still blocked");
        await UntilAsync(() => View(manager, account).Worker?.PendingGameSessionOperation == GameSessionOperation.Relogin,
            "fourth failure requests game exit and relogin through the worker RPC");
        Require(View(manager, account).WorkerProcessId == stalledPid &&
                test.Logger.Entries.Count(e => e.EventName == "account.restart.complete") == 3 &&
                test.Logger.Entries.Count(e => e.EventName == "game_session.world_snapshot_relogin_requested") == 1,
            "keep the third replacement alive to exit the game, without a fourth script restart");
        await test.Client(account).CallAsync<OperationResult>("mock.game-session.completed",
            new object?[] { Guid.NewGuid(), GameSessionOperation.Relogin });
        await UntilAsync(() => View(manager, account) is { State: "running", WorkerProcessId: { } current } && current != stalledPid &&
            test.Logger.Entries.Any(e => e.EventName == "game_session.relogin_complete"), "restart only after confirmed game exit");

        var freshPid = (await test.RunningAsync(manager, account)).WorkerProcessId;
        Require((await ReportStallAsync()).Success, "report cold read after successful relogin");
        await UntilAsync(() => View(manager, account) is { State: "running", WorkerProcessId: { } current } && current != freshPid &&
            test.Logger.Entries.Count(e => e.EventName == "account.restart.complete") == 4, "new login starts a new recovery sequence");
        Require(View(manager, neighbor).WorkerProcessId == neighborPid &&
                test.Logger.Entries.Count(e => e.EventName == "game_session.world_snapshot_relogin_requested") == 1,
            "the counter resets and the neighbor keeps its original process");
    }

    public static async Task ScriptRestartFreshWorkerAsync()
    {
        foreach (var recover in new[] { false, true })
        {
            await using var test = new TestEnvironment();
            var account = test.Account(1); account.AutoRecover = recover;
            var neighbor = test.Account(2);
            var manager = await test.ManagerAsync(new[] { account, neighbor });
            Require((await manager.StartAsync(account.InstanceId)).Success, "start recovery account");
            Require((await manager.StartAsync(neighbor.InstanceId)).Success, "start neighboring account");
            var oldPid = (await test.RunningAsync(manager, account)).WorkerProcessId!.Value;
            var neighborPid = (await test.RunningAsync(manager, neighbor)).WorkerProcessId;
            var oldToken = test.Spec(account).Token;
            await test.Client(account).CallAsync<OperationResult>("mock.script.restart", new object?[] { Guid.Empty });
            await Task.Delay(250);
            Require(View(manager, account).WorkerProcessId == oldPid, "empty request cannot restart a worker");
            await test.Client(account).CallAsync<OperationResult>("mock.script.restart", new object?[] { Guid.NewGuid() });
            await UntilAsync(() => View(manager, account) is { State: "running", WorkerProcessId: { } pid } && pid != oldPid,
                "request replaces the script worker even if AutoRecover is disabled");
            var newPid = View(manager, account).WorkerProcessId;
            Require(!IsAlive(oldPid) && test.Spec(account).Token != oldToken, "old worker exits before fresh authenticated worker");
            Require((await test.InfoAsync(account)).Starts == 1 && View(manager, account).Worker?.ScriptRestartRequestId == null,
                "new session starts ordinary work once with no stale request");
            await Task.Delay(350);
            Require(View(manager, account).WorkerProcessId == newPid &&
                    test.Logger.Entries.Count(e => e.EventName == "account.restart.complete") == 1,
                "status polling does not duplicate restart");
            Require(View(manager, neighbor).WorkerProcessId == neighborPid && (await test.InfoAsync(neighbor)).Running,
                "other account is untouched");
            Require(new DeviceLeaseStore(test.LeasePath).ReadActive().Value?.Count == 2,
                "exactly one hardware lease per running account");
            Require(File.ReadAllText(Path.Combine(test.Root, "config", "workers", "run-intent.json")).Contains(account.InstanceId),
                "restart preserves durable run intent");
        }
    }

    public static async Task ScriptRestartManualStopWinsAsync()
    {
        await using var test = new TestEnvironment();
        var account = test.Account(1);
        var manager = await test.ManagerAsync(new[] { account });
        Require((await manager.StartAsync(account.InstanceId)).Success, "start account");
        var oldToken = test.Spec(account).Token;
        using (await manager.HoldStartAdmissionsAsync())
        {
            await test.Client(account).CallAsync<OperationResult>("mock.script.restart", new object?[] { Guid.NewGuid() });
            await UntilAsync(() => View(manager, account) is { State: "stopped", WorkerProcessId: null }, "old worker stopped");
            Require((await manager.StopAsync(account.InstanceId)).Success, "manual stop cancels queued restart");
        }
        await UntilAsync(() => test.Logger.Entries.Any(e => e.EventName == "account.restart.failed"), "cancelled restart observed");
        await Task.Delay(350);
        Require(View(manager, account) is { DesiredRunning: false, WorkerProcessId: null } && test.Spec(account).Token == oldToken,
            "automatic restart cannot undo a later manual stop");
    }

    public static async Task ScriptRestartStopFailureAsync()
    {
        await using var test = new TestEnvironment();
        var account = test.Account(1);
        var reconciler = new ShopExitReconciler();
        var manager = await test.ManagerAsync(new[] { account }, exitReconciler: reconciler);
        Require((await manager.StartAsync(account.InstanceId)).Success, "start account");
        var oldPid = (await test.RunningAsync(manager, account)).WorkerProcessId!.Value;
        var token = test.Spec(account).Token;
        reconciler.BlockedPid = oldPid;
        try
        {
            await test.Client(account).CallAsync<OperationResult>("mock.script.restart", new object?[] { Guid.NewGuid() });
            await UntilAsync(() => test.Logger.Entries.Any(e => e.EventName == "account.restart.retry_scheduled"), "retry scheduled");
            Require(View(manager, account).DesiredRunning && test.Spec(account).Token == token,
                "failed cleanup preserves intent but never starts overlapping workers");
            reconciler.BlockedPid = 0;
            await UntilAsync(() => View(manager, account) is { State: "running", WorkerProcessId: { } pid } && pid != oldPid,
                "deferred restart resumes after cleanup succeeds");
            Require((await test.InfoAsync(account)).Starts == 1, "replacement starts exactly once");
        }
        finally { reconciler.BlockedPid = 0; }
    }
}
