internal static partial class WorkerProcessManagerTests
{
    public static async Task RetiredCombatPolicyDoesNotInterruptWorkersAsync()
    {
        await using var test = new TestEnvironment();
        var account = test.Account(1);
        const string retiredPolicy = """{"ForceLegacy":true}""";
        var directory = Path.Combine(test.Root, "config");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "combat-flow-policy.json");
        await File.WriteAllTextAsync(path, retiredPolicy);

        var previous = await test.ManagerAsync([account]);
        Require((await previous.StartAsync(account.InstanceId)).Success, "a retired policy must not block startup");
        var pid = (await test.RunningAsync(previous, account)).WorkerProcessId!.Value;
        Require(View(previous, account).Worker is { IsRunning: true, ActiveCombatFlowVersion: null },
            "the old mock protocol does not report a combat version");
        await test.DetachAsync(previous);

        var reopened = await test.ManagerAsync([account]);
        Require((await test.RunningAsync(reopened, account)).WorkerProcessId == pid && IsAlive(pid),
            "a retired policy must not stop or replace the adopted worker");
        Require(View(reopened, account).DesiredRunning && (await test.InfoAsync(account)).Starts == 1,
            "adoption preserves running intent without replaying startup");
        Require(await File.ReadAllTextAsync(path) == retiredPolicy, "the retired file remains available for package rollback");
    }
}
