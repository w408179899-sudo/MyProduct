using Roadhog.Infrastructure.WorkerProcesses;

internal static partial class WorkerProcessManagerTests
{
    public static async Task InitializationFaultKeepsRunIntentAsync()
    {
        await using var test = new TestEnvironment();
        var account = test.Account(1);
        var neighbor = test.Account(2);
        var manager = await test.ManagerAsync([account, neighbor]);
        Require((await manager.StartAsync(neighbor.InstanceId)).Success, "neighbor starts before initialization fault");
        var neighborPid = (await test.RunningAsync(manager, neighbor)).WorkerProcessId;
        var log = manager.PathsFor(account).LogDirectory;
        Directory.CreateDirectory(log);
        var failure = Path.Combine(log, "fail-initialization");
        await File.WriteAllTextAsync(failure, "temporary IO failure");
        Require(!(await manager.StartAsync(account.InstanceId)).Success, "initial fault is reported to caller");
        await UntilAsync(() => File.Exists(Path.Combine(log, "initialization-attempts")) &&
            File.ReadAllLines(Path.Combine(log, "initialization-attempts")).Length >= 3,
            "initialization exceptions retry across fresh worker processes");
        Require(View(manager, account).DesiredRunning, "initialization exception is not a permanent authorization rejection");
        Require(File.ReadAllText(Path.Combine(test.Root, "config", "workers", "run-intent.json")).Contains(account.InstanceId),
            "initialization retries preserve durable intent");
        File.Delete(failure);
        await test.RunningAsync(manager, account);
        Require((await test.InfoAsync(account)).Starts == 1, "ordinary combat starts once after initialization recovers");
        Require(View(manager, neighbor).WorkerProcessId == neighborPid && (await test.InfoAsync(neighbor)).Starts == 1,
            "initialization recovery leaves neighboring workers untouched");
    }

    public static async Task CrashRecoveryStartFailureAndStopAsync()
    {
        await using var test = new TestEnvironment();
        var account = test.Account(1);
        var manager = await test.ManagerAsync([account]);
        Require((await manager.StartAsync(account.InstanceId)).Success, "start before unexpected exit");
        var oldPid = (await test.RunningAsync(manager, account)).WorkerProcessId!.Value;
        var log = manager.PathsFor(account).LogDirectory;
        var failure = Path.Combine(log, "reject-normal-start");
        await File.WriteAllTextAsync(failure, "temporary startup rejection");
        await KillAsync(oldPid);
        await UntilAsync(() => test.Logger.Entries.Count(e => e.EventName == "account_process.failed" &&
            e.Fields["error"]?.ToString() == "mock normal restart rejected") >= 3,
            "ordinary crash recovery keeps retrying rejected startup");
        Require(View(manager, account).DesiredRunning, "failed recovery cannot turn itself into a user stop");
        File.Delete(failure);
        await test.RunningAsync(manager, account);
        Require((await test.InfoAsync(account)).Starts == 1, "crash recovery eventually resumes combat");

        await File.WriteAllTextAsync(failure, "fail the next recovery too");
        var failures = test.Logger.Entries.Count(e => e.EventName == "account_process.failed");
        await KillAsync(View(manager, account).WorkerProcessId!.Value);
        await UntilAsync(() => test.Logger.Entries.Count(e => e.EventName == "account_process.failed") > failures + 1,
            "second recovery reaches backoff");
        Require((await manager.StopAsync(account.InstanceId)).Success, "manual Stop interrupts failed recovery");
        var launchToken = test.Spec(account).Token;
        File.Delete(failure);
        await Task.Delay(500);
        Require(View(manager, account) is { DesiredRunning: false, WorkerProcessId: null } && test.Spec(account).Token == launchToken,
            "clearing the fault after Stop cannot resurrect the account");
    }
}
