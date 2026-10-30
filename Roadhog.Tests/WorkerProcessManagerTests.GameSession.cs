using System.Text.Json;
using Roadhog.Application.GameSessions;
using Roadhog.Core.Accounts;
using Roadhog.Core.Common;
using Roadhog.Infrastructure.WorkerProcesses;

internal static partial class WorkerProcessManagerTests
{
    public static async Task GameSessionExitOnlyDoesNotLoginAsync()
    {
        foreach (var alreadyRunning in new[] { false, true })
        {
            await using var test = new TestEnvironment();
            var account = test.Account(1);
            var neighbor = test.Account(2);
            var manager = await test.ManagerAsync(new[] { account, neighbor });
            Require((await manager.StartAsync(neighbor.InstanceId)).Success, "start neighbor before exit-only request");
            var neighborPid = (await test.RunningAsync(manager, neighbor)).WorkerProcessId;
            if (alreadyRunning) Require((await manager.StartAsync(account.InstanceId)).Success, "start target before exit-only request");
            Require((await manager.RequestGameSessionAsync(account.InstanceId, GameSessionOperation.Exit)).Success,
                "stopped and running accounts both dispatch the exit-only command");
            var oldPid = (await test.RunningAsync(manager, account)).WorkerProcessId!.Value;
            var sent = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(manager.PathsFor(account).LogDirectory, "game-session.json")));
            using (sent)
            {
                Require(sent.RootElement.GetProperty("Operation").GetInt32() == (int)GameSessionOperation.Exit &&
                    sent.RootElement.GetProperty("Account").GetProperty("InstanceId").GetString() == account.InstanceId,
                    "exit-only operation and account identity cross the real worker RPC boundary intact");
            }
            if (!alreadyRunning)
                Require(!File.Exists(Path.Combine(manager.PathsFor(account).LogDirectory, "normal-start.json")),
                    "an idle account requesting exit must not start ordinary combat first");
            await test.Client(account).CallAsync<OperationResult>("mock.game-session.completed", new object?[] { Guid.NewGuid(), GameSessionOperation.Exit });
            await UntilAsync(() => View(manager, account) is { DesiredRunning: false, WorkerProcessId: null }, "exit-only completes offline");
            await Task.Delay(250);
            Require(!IsAlive(oldPid) && !View(manager, account).DesiredRunning,
                "exit-only reaps the old worker and cannot be auto-recovered into login");
            Require(!File.Exists(Path.Combine(manager.PathsFor(account).LogDirectory, "login-entered")),
                "exit-only does not dispatch login-only startup");
            Require(!File.ReadAllText(Path.Combine(test.Root, "config", "workers", "run-intent.json")).Contains(account.InstanceId),
                "exit-only clears durable run intent");
            Require(View(manager, neighbor).WorkerProcessId == neighborPid && (await test.InfoAsync(neighbor)).Starts == 1,
                "the neighboring account keeps its original worker and uninterrupted ordinary start");
        }
    }

    public static async Task GameSessionReloginRestartsNormallyAsync()
    {
        foreach (var alreadyRunning in new[] { false, true })
        {
            await using var test = new TestEnvironment();
            var account = test.Account(1);
            account.AutoRecover = false;
            account.ScriptSettings = new() { MainMode = AccountMainMode.SemiAuto };
            var neighbor = test.Account(2);
            var manager = await test.ManagerAsync(new[] { account, neighbor });
            Require((await manager.StartAsync(neighbor.InstanceId)).Success, "start unaffected account");
            var neighborPid = (await test.RunningAsync(manager, neighbor)).WorkerProcessId;
            if (alreadyRunning) Require((await manager.StartAsync(account.InstanceId)).Success, "start target before relogin");
            Require((await manager.RequestGameSessionAsync(account.InstanceId, GameSessionOperation.Relogin)).Success,
                "running account relogin is not swallowed by the duplicate Start no-op");
            var oldPid = (await test.RunningAsync(manager, account)).WorkerProcessId!.Value;
            var oldToken = test.Spec(account).Token;
            using (var sent = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(manager.PathsFor(account).LogDirectory, "game-session.json"))))
                Require(sent.RootElement.GetProperty("Operation").GetInt32() == (int)GameSessionOperation.Relogin,
                    "relogin is distinct from exit-only at the process boundary");
            await test.Client(account).CallAsync<OperationResult>("mock.game-session.completed", new object?[] { Guid.Empty, GameSessionOperation.Relogin });
            await Task.Delay(250);
            Require(View(manager, account).WorkerProcessId == oldPid, "an empty completion identity cannot start relogin");
            var updated = account.Clone();
            updated.ScriptSettings!.Combat.StationaryCombatRadius = 87;
            manager.UpdateAccounts(new[] { updated, neighbor });
            await test.Client(account).CallAsync<OperationResult>("mock.game-session.completed", new object?[] { Guid.NewGuid(), GameSessionOperation.Relogin });
            await UntilAsync(() => View(manager, account) is { State: "running", DesiredRunning: true, WorkerProcessId: { } pid } && pid != oldPid,
                "confirmed game exit reaches ordinary startup in a fresh worker even with AutoRecover disabled");
            var newPid = (await test.RunningAsync(manager, account)).WorkerProcessId;
            Require(!IsAlive(oldPid) && test.Spec(account).Token != oldToken, "old session is reaped before the authenticated replacement runs");
            var started = JsonSerializer.Deserialize<AccountConfig>(await File.ReadAllTextAsync(Path.Combine(manager.PathsFor(account).LogDirectory, "normal-start.json")))!;
            Require(started.ScriptSettings!.Combat.StationaryCombatRadius == 87,
                "relogin resumes normal combat with the latest account settings");
            Require(!File.Exists(Path.Combine(manager.PathsFor(account).LogDirectory, "login-entered")),
                "relogin dispatches normal startup, not the login-only command that stops before combat");
            Require((await test.InfoAsync(account)).Starts == 1 && View(manager, account).Worker?.GameSessionCompletedRequestId is null,
                "replacement has one normal start and no stale completion");
            await Task.Delay(350);
            Require(View(manager, account).WorkerProcessId == newPid &&
                test.Logger.Entries.Count(entry => entry.EventName == "game_session.relogin_complete") == 1,
                "repeated status polling cannot replay the completed relogin");
            Require(View(manager, neighbor).WorkerProcessId == neighborPid && (await test.InfoAsync(neighbor)).Starts == 1,
                "relogin leaves every neighboring worker unchanged");
        }
    }

    public static async Task GameSessionStopWinsHandoffAsync()
    {
        await using var test = new TestEnvironment();
        var account = test.Account(1);
        var manager = await test.ManagerAsync(new[] { account });
        Require((await manager.RequestGameSessionAsync(account.InstanceId, GameSessionOperation.Relogin)).Success, "queue relogin before stop race");
        var oldPid = (await test.RunningAsync(manager, account)).WorkerProcessId!.Value;
        var oldToken = test.Spec(account).Token;
        using (await manager.HoldStartAdmissionsAsync())
        {
            await test.Client(account).CallAsync<OperationResult>("mock.game-session.completed", new object?[] { Guid.NewGuid(), GameSessionOperation.Relogin });
            await UntilAsync(() => View(manager, account) is { State: "stopped", WorkerProcessId: null }, "exit finished before blocked restart admission");
            Require((await manager.StopAsync(account.InstanceId)).Success, "explicit Stop supersedes pending relogin");
        }
        await UntilAsync(() => test.Logger.Entries.Any(entry => entry.EventName == "game_session.relogin_complete"), "cancelled handoff resolves");
        await Task.Delay(250);
        Require(!IsAlive(oldPid) && View(manager, account) is { DesiredRunning: false, WorkerProcessId: null } && test.Spec(account).Token == oldToken,
            "a later Stop prevents any new worker after game exit");
        Require(!File.ReadAllText(Path.Combine(test.Root, "config", "workers", "run-intent.json")).Contains(account.InstanceId),
            "cancelled relogin leaves durable intent stopped");
    }

    public static async Task GameSessionStopFailureDoesNotLoginAsync()
    {
        await using var test = new TestEnvironment();
        var account = test.Account(1);
        var reconciler = new ShopExitReconciler();
        var manager = await test.ManagerAsync(new[] { account }, exitReconciler: reconciler);
        Require((await manager.RequestGameSessionAsync(account.InstanceId, GameSessionOperation.Relogin)).Success, "request relogin before reconciliation fault");
        var oldPid = (await test.RunningAsync(manager, account)).WorkerProcessId!.Value;
        var oldToken = test.Spec(account).Token;
        reconciler.BlockedPid = oldPid;
        try
        {
            await test.Client(account).CallAsync<OperationResult>("mock.game-session.completed", new object?[] { Guid.NewGuid(), GameSessionOperation.Relogin });
            await UntilAsync(() => test.Logger.Entries.Any(entry => entry.EventName == "game_session.stop_failed"), "failed stop is diagnosed");
            await Task.Delay(250);
            Require(View(manager, account) is { DesiredRunning: false } && test.Spec(account).Token == oldToken &&
                !test.Logger.Entries.Any(entry => entry.EventName == "game_session.relogin_complete"),
                "failed worker cleanup cannot start login or retain automatic recovery intent");
        }
        finally { reconciler.BlockedPid = 0; }
    }
}
