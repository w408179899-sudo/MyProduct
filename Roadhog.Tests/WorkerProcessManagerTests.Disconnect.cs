using Roadhog.Core.Common;
using Roadhog.Infrastructure.WorkerProcesses;

internal static partial class WorkerProcessManagerTests
{
    public static async Task DisconnectScheduleCancelsLongLoginAsync()
    {
        await using var test = new TestEnvironment();
        var clock = new DailyGameScheduleTests.Clock("2026-10-30T21:59:30");
        var account = test.Account(1); account.DailySchedule = new() { OnlineEnabled = true };
        var manager = await test.ManagerAsync([account], scheduleTime: clock);
        await test.RunningAsync(manager, account);
        var log = manager.PathsFor(account).LogDirectory;
        await File.WriteAllTextAsync(Path.Combine(log, "delay-normal-start"), "waiting");
        await test.Client(account).CallAsync<OperationResult>("mock.game-disconnected", [Guid.NewGuid()]);
        await UntilAsync(() => File.Exists(Path.Combine(log, "delayed-recovery-entered")), "background recovery is inside long login");
        clock.Set("2026-10-30T22:00:00");
        await UntilAsync(() => View(manager, account).Worker?.CompletedGameSessionOperation == Roadhog.Application.GameSessions.GameSessionOperation.Exit,
            "offline deadline cancels long background login and dispatches exit");
        await test.Client(account).CallAsync<OperationResult>("mock.game-session.completed", [Guid.NewGuid(), Roadhog.Application.GameSessions.GameSessionOperation.Exit]);
        await UntilAsync(() => View(manager, account) is { DesiredRunning: false, WorkerProcessId: null }, "long login does not outlive scheduled offline");
    }

    public static async Task DisconnectStopDuringReconciliationAsync()
    {
        await using var test = new TestEnvironment();
        var account = test.Account(1);
        var reconcile = new BlockingShopExitReconciler();
        var manager = await test.ManagerAsync([account], exitReconciler: reconcile);
        Require((await manager.StartAsync(account.InstanceId)).Success, "start before exit handoff");
        var oldToken = test.Spec(account).Token;
        await test.Client(account).CallAsync<OperationResult>("mock.game-disconnected", [Guid.NewGuid()]);
        await reconcile.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var stopping = manager.StopAsync(account.InstanceId);
        reconcile.Release.TrySetResult();
        Require((await stopping).Success, "stop during process reconciliation completes");
        await Task.Delay(350);
        Require(test.Spec(account).Token == oldToken && View(manager, account) is { DesiredRunning: false, WorkerProcessId: null },
            "a stop in the old-worker/new-worker gap prevents relogin");
    }

    public static async Task DisconnectConfigurationFailureStopsRetryAsync()
    {
        await using var test = new TestEnvironment();
        var account = test.Account(1);
        var manager = await test.ManagerAsync([account]);
        Require((await manager.StartAsync(account.InstanceId)).Success, "start before config rejection");
        await File.WriteAllTextAsync(Path.Combine(manager.PathsFor(account).LogDirectory, "reject-normal-start"), "invalid config");
        await test.Client(account).CallAsync<OperationResult>("mock.game-disconnected", [Guid.NewGuid()]);
        await UntilAsync(() => View(manager, account) is { DesiredRunning: false, WorkerProcessId: null }, "configuration rejection clears recovery intent");
        var token = test.Spec(account).Token;
        await Task.Delay(350);
        Require(test.Spec(account).Token == token, "only attempted login failures retry, configuration rejection does not loop");
    }

    public static async Task DisconnectRecoveryRetriesAndIsolatesAsync()
    {
        await using var test = new TestEnvironment();
        var account = test.Account(1); var neighbor = test.Account(2);
        var manager = await test.ManagerAsync([account, neighbor]);
        Require((await manager.StartAsync(account.InstanceId)).Success, "target started");
        Require((await manager.StartAsync(neighbor.InstanceId)).Success, "neighbor started");
        var old = (await test.RunningAsync(manager, account)).WorkerProcessId;
        var other = (await test.RunningAsync(manager, neighbor)).WorkerProcessId;
        var log = manager.PathsFor(account).LogDirectory;
        var offline = Path.Combine(log, "network-offline");
        await File.WriteAllTextAsync(offline, "offline");
        await test.Client(account).CallAsync<OperationResult>("mock.game-disconnected", [Guid.NewGuid()]);
        await UntilAsync(() => File.Exists(Path.Combine(log, "login-retries")) && File.ReadAllLines(Path.Combine(log, "login-retries")).Length >= 3,
            "long outage retries existing login across fresh workers");
        Require(View(manager, account).DesiredRunning && !IsAlive(old!.Value), "outage preserves intent and reaps old session");
        Require(File.ReadAllText(Path.Combine(test.Root, "config", "workers", "run-intent.json")).Contains(account.InstanceId), "intent remains durable");
        File.Delete(offline);
        await UntilAsync(() => View(manager, account) is { State: "running", DesiredRunning: true }, "network returns and bot resumes");
        var replacement = View(manager, account).WorkerProcessId;
        await Task.Delay(300);
        Require(replacement != old && View(manager, account).WorkerProcessId == replacement && (await test.InfoAsync(account)).Starts == 1,
            "one resumed start, no repeated recovery from stale request");
        Require(View(manager, neighbor).WorkerProcessId == other && (await test.InfoAsync(neighbor)).Starts == 1, "neighbor untouched");
    }

    public static async Task DisconnectStopAndDisabledAsync()
    {
        await using var test = new TestEnvironment();
        var account = test.Account(1); var disabled = test.Account(2); disabled.AutoRecover = false;
        var manager = await test.ManagerAsync([account, disabled]);
        Require((await manager.StartAsync(account.InstanceId)).Success, "start before retry/stop race");
        Require((await manager.StartAsync(disabled.InstanceId)).Success, "start disabled recovery account");
        var log = manager.PathsFor(account).LogDirectory;
        await File.WriteAllTextAsync(Path.Combine(log, "network-offline"), "offline");
        await test.Client(account).CallAsync<OperationResult>("mock.game-disconnected", [Guid.NewGuid()]);
        await UntilAsync(() => File.Exists(Path.Combine(log, "login-retries")), "retry started");
        Require((await manager.StopAsync(account.InstanceId)).Success, "manual stop cancels ongoing recovery");
        var token = test.Spec(account).Token;
        File.Delete(Path.Combine(log, "network-offline"));
        var other = View(manager, disabled).WorkerProcessId;
        await test.Client(disabled).CallAsync<OperationResult>("mock.game-disconnected", [Guid.NewGuid()]);
        await Task.Delay(600);
        Require(View(manager, account) is { DesiredRunning: false, WorkerProcessId: null } && test.Spec(account).Token == token,
            "network restore cannot undo manual Stop");
        Require(View(manager, disabled).WorkerProcessId == other, "disabled auto recovery never logs in");
    }

    public static async Task DisconnectScheduleOfflineWinsAsync()
    {
        await using var test = new TestEnvironment();
        var clock = new DailyGameScheduleTests.Clock("2026-10-30T21:59:30");
        var account = test.Account(1); account.DailySchedule = new() { OnlineEnabled = true };
        var manager = await test.ManagerAsync([account], scheduleTime: clock);
        await test.RunningAsync(manager, account);
        var log = manager.PathsFor(account).LogDirectory;
        await File.WriteAllTextAsync(Path.Combine(log, "network-offline"), "offline");
        await test.Client(account).CallAsync<OperationResult>("mock.game-disconnected", [Guid.NewGuid()]);
        await UntilAsync(() => File.Exists(Path.Combine(log, "login-retries")), "outage login attempted before offline boundary");
        clock.Set("2026-10-30T22:00:00");
        await UntilAsync(() => View(manager, account).Worker?.CompletedGameSessionOperation == Roadhog.Application.GameSessions.GameSessionOperation.Exit,
            "scheduled offline supersedes recovery");
        await test.Client(account).CallAsync<OperationResult>("mock.game-session.completed", [Guid.NewGuid(), Roadhog.Application.GameSessions.GameSessionOperation.Exit]);
        await UntilAsync(() => View(manager, account) is { DesiredRunning: false, WorkerProcessId: null }, "offline completes");
        File.Delete(Path.Combine(log, "network-offline"));
        await Task.Delay(350);
        Require(View(manager, account).WorkerProcessId == null, "network returning outside window cannot log in");
        clock.Set("2026-10-31T08:00:00");
        await test.RunningAsync(manager, account);
    }
}
