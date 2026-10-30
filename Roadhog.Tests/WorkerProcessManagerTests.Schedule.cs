using System.Text.Json;
using Roadhog.Application.GameSessions;
using Roadhog.Core.Common;
using Roadhog.Core.Accounts;

internal static partial class WorkerProcessManagerTests
{
    public static async Task DailyScheduleBusyDeviceRetriesWithoutReplayAsync()
    {
        await using var test = new TestEnvironment();
        var clock = new DailyGameScheduleTests.Clock("2026-10-30T09:59:50");
        var account = test.Account(1); account.DailySchedule = new() { ReloginEnabled = true, ReloginTimes = ["10:00"] };
        var neighbor = test.Account(2); neighbor.HardwareKey = account.HardwareKey; neighbor.VmmDeviceName = account.VmmDeviceName;
        var manager = await test.ManagerAsync([account, neighbor], scheduleTime: clock);
        Require((await manager.StartAsync(neighbor.InstanceId)).Success, "device owner starts before scheduled slot");
        var neighborPid = (await test.RunningAsync(manager, neighbor)).WorkerProcessId;
        clock.Set("2026-10-30T10:00:01");
        await UntilAsync(() => test.Logger.Entries.Any(e => e.EventName == "schedule.retry"), "busy device is deferred");
        await Task.Delay(180);
        Require(test.Logger.Entries.Count(e => e.EventName == "schedule.retry") == 1 && View(manager, neighbor).WorkerProcessId == neighborPid,
            "retry is bounded and does not displace the device owner");
        Require((await manager.StopAsync(neighbor.InstanceId)).Success, "test releases its device owner");
        clock.Set("2026-10-30T10:00:32");
        await UntilAsync(() => View(manager, account).Worker?.CompletedGameSessionOperation == GameSessionOperation.Relogin,
            "deferred fixed-time request retries after the device becomes free");
        var old = View(manager, account).WorkerProcessId;
        await test.Client(account).CallAsync<OperationResult>("mock.game-session.completed", [Guid.NewGuid(), GameSessionOperation.Relogin]);
        await UntilAsync(() => View(manager, account) is { State: "running", DesiredRunning: true, WorkerProcessId: { } pid } && pid != old,
            "deferred relogin resumes normal bot once");
    }

    public static async Task DailyScheduleChangedBeforeDispatchAsync()
    {
        await using var test = new TestEnvironment();
        var clock = new DailyGameScheduleTests.Clock("2026-10-30T09:59:50");
        var account = test.Account(1); account.DailySchedule = new() { ReloginEnabled = true, ReloginTimes = ["10:00"] };
        var manager = await test.ManagerAsync([account], scheduleTime: clock);
        using (await manager.HoldStartAdmissionsAsync())
        {
            clock.Set("2026-10-30T10:00:00");
            await Task.Delay(200);
            account.DailySchedule.ReloginEnabled = false;
            manager.UpdateAccounts([account]);
        }
        await Task.Delay(300);
        Require(View(manager, account) is { DesiredRunning: false, WorkerProcessId: null }, "disabled schedule cannot execute queued admission");
    }

    private static string ScheduleRequestPath(TestEnvironment test, Roadhog.Infrastructure.WorkerProcesses.WorkerProcessManager manager, AccountConfig account) =>
        Path.Combine(manager.PathsFor(account).LogDirectory, "game-session.json");

    public static async Task DailyScheduleManualStopSurvivesReopenAsync()
    {
        await using var test = new TestEnvironment();
        var clock = new DailyGameScheduleTests.Clock("2026-10-30T08:00:00");
        var account = test.Account(1); account.DailySchedule = new() { OnlineEnabled = true, ReloginEnabled = true, ReloginTimes = ["10:00"] };
        var neighbor = test.Account(2);
        var manager = await test.ManagerAsync([account, neighbor], scheduleTime: clock);
        var first = (await test.RunningAsync(manager, account)).WorkerProcessId;
        Require((await manager.StartAsync(neighbor.InstanceId)).Success, "neighbor starts normally");
        var neighborPid = (await test.RunningAsync(manager, neighbor)).WorkerProcessId;
        clock.Set("2026-10-30T09:00:00");
        Require((await manager.StopAsync(account.InstanceId)).Success, "manual stop completes");
        clock.Set("2026-10-30T10:00:01"); await Task.Delay(200);
        Require(!View(manager, account).DesiredRunning && View(manager, account).WorkerProcessId == null, "10am relogin cannot undo the manual stop");
        await test.DetachAsync(manager);
        manager = await test.ManagerAsync([account, neighbor], scheduleTime: clock);
        await Task.Delay(200);
        Require(!View(manager, account).DesiredRunning && View(manager, account).WorkerProcessId == null, "manual pause persists across main-window reopen");
        clock.Set("2026-10-31T08:00:00");
        var resumed = await test.RunningAsync(manager, account);
        Require(resumed.WorkerProcessId != first, "next window starts a new worker");
        Require(View(manager, neighbor).WorkerProcessId == neighborPid, "neighbor retains original process across scheduling and adoption");
    }

    public static async Task DailyScheduleOfflineWinsAsync()
    {
        await using var test = new TestEnvironment();
        var clock = new DailyGameScheduleTests.Clock("2026-10-30T21:59:50");
        var account = test.Account(1);
        account.DailySchedule = new() { OnlineEnabled = true, ReloginEnabled = true, ReloginTimes = ["22:00", "23:00"] };
        var manager = await test.ManagerAsync([account], scheduleTime: clock);
        await test.RunningAsync(manager, account);
        clock.Set("2026-10-30T22:00:00");
        var request = ScheduleRequestPath(test, manager, account);
        await UntilAsync(() => View(manager, account).Worker?.CompletedGameSessionOperation == GameSessionOperation.Exit, "offline boundary dispatches normal game exit");
        using (var json = JsonDocument.Parse(await File.ReadAllTextAsync(request)))
            Require(json.RootElement.GetProperty("Operation").GetInt32() == (int)GameSessionOperation.Exit, "offline beats simultaneous fixed relogin");
        await test.Client(account).CallAsync<OperationResult>("mock.game-session.completed", [Guid.NewGuid(), GameSessionOperation.Exit]);
        await UntilAsync(() => View(manager, account) is { DesiredRunning: false, WorkerProcessId: null }, "scheduled exit remains offline");
        clock.Set("2026-10-30T23:00:01"); await Task.Delay(250);
        Require(View(manager, account).WorkerProcessId == null, "fixed slot outside the online window is ignored");
        Require(!(await manager.StartAsync(account.InstanceId)).Success, "manual start explains active offline restriction");
        clock.Set("2026-10-31T08:00:00");
        await test.RunningAsync(manager, account);
    }

    public static async Task DailyScheduleFixedReloginOnceAsync()
    {
        await using var test = new TestEnvironment();
        var clock = new DailyGameScheduleTests.Clock("2026-10-30T09:59:50");
        var account = test.Account(1); account.DailySchedule = new() { ReloginEnabled = true, ReloginTimes = ["10:00"] };
        var manager = await test.ManagerAsync([account], scheduleTime: clock);
        await Task.Delay(150);
        Require(View(manager, account).WorkerProcessId == null, "standalone fixed plan waits for its time");
        clock.Set("2026-10-30T10:00:01");
        await UntilAsync(() => View(manager, account).Worker?.CompletedGameSessionOperation == GameSessionOperation.Relogin, "fixed slot dispatches relogin");
        var first = (await test.RunningAsync(manager, account)).WorkerProcessId;
        await test.Client(account).CallAsync<OperationResult>("mock.game-session.completed", [Guid.NewGuid(), GameSessionOperation.Relogin]);
        await UntilAsync(() => View(manager, account) is { DesiredRunning: true, State: "running", WorkerProcessId: { } pid } && pid != first, "fixed relogin completes in fresh worker");
        var next = View(manager, account).WorkerProcessId;
        clock.Set("2026-10-30T10:00:45"); await Task.Delay(200);
        await test.DetachAsync(manager);
        manager = await test.ManagerAsync([account], scheduleTime: clock);
        await Task.Delay(250);
        Require(View(manager, account).WorkerProcessId == next && test.Logger.Entries.Count(e => e.EventName == "schedule.completed") == 1,
            "repeated ticks and manager reopening cannot replay the same slot");
    }

    public static async Task DailyScheduleDeadlineCancelsPendingStartAsync()
    {
        await using var test = new TestEnvironment();
        var clock = new DailyGameScheduleTests.Clock("2026-10-30T21:59:50");
        var account = test.Account(1); account.DailySchedule = new() { OnlineEnabled = true };
        var manager = await test.ManagerAsync([account], "slow-start", scheduleTime: clock);
        await UntilAsync(() => Directory.EnumerateFiles(test.Root, "start-entered", SearchOption.AllDirectories).Any(), "scheduled startup is in flight");
        clock.Set("2026-10-30T22:00:00");
        await UntilAsync(() => View(manager, account).Worker?.CompletedGameSessionOperation == GameSessionOperation.Exit, "offline deadline interrupts long startup and dispatches exit");
        await test.Client(account).CallAsync<OperationResult>("mock.game-session.completed", [Guid.NewGuid(), GameSessionOperation.Exit]);
        await UntilAsync(() => View(manager, account) is { DesiredRunning: false, WorkerProcessId: null }, "deadline leaves no restarted worker");
    }

    public static async Task DailyScheduleStopWinsQueuedAdmissionAsync()
    {
        await using var test = new TestEnvironment();
        var clock = new DailyGameScheduleTests.Clock("2026-10-30T09:59:50");
        var account = test.Account(1); account.DailySchedule = new() { ReloginEnabled = true, ReloginTimes = ["10:00"] };
        var manager = await test.ManagerAsync([account], scheduleTime: clock);
        using (await manager.HoldStartAdmissionsAsync())
        {
            clock.Set("2026-10-30T10:00:00");
            await Task.Delay(200);
            Require((await manager.StopAsync(account.InstanceId)).Success, "Stop cancels scheduled admission before worker creation");
        }
        await Task.Delay(200);
        Require(View(manager, account) is { DesiredRunning: false, WorkerProcessId: null }, "queued schedule cannot reverse later Stop");
        Require(!File.Exists(ScheduleRequestPath(test, manager, account)), "cancelled schedule sends no game command");
    }
}
