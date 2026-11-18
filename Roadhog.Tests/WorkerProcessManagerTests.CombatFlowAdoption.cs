using System.Text.Json;
using Roadhog.Core.Accounts;
using Roadhog.Core.Common;
using Roadhog.Infrastructure.WorkerProcesses;

internal static partial class WorkerProcessManagerTests
{
    public static async Task CombatFlowRollbackPreservesAdoptedLegacyWorkerAsync()
    {
        await using var test = new TestEnvironment();
        var account = test.Account(1);
        var previous = await test.ManagerAsync([account]);
        Require((await previous.StartAsync(account.InstanceId)).Success, "legacy worker starts");
        var pid = (await test.RunningAsync(previous, account)).WorkerProcessId!.Value;
        Require(View(previous, account).Worker is { IsRunning: true, ActiveCombatFlowVersion: null },
            "old worker protocol does not report a combat version");
        Require((await previous.CombatFlowPolicy.SaveAsync(true)).Success, "global rollback persists");
        await test.DetachAsync(previous);

        // A saved preference controls the next start, not the old process that
        // remains alive while the console is closed or upgraded.
        var nextSelection = account.Clone();
        nextSelection.CombatFlowVersion = CombatFlowVersion.CoordinatedV2;
        var reopened = await test.ManagerAsync([nextSelection]);
        Require((await test.RunningAsync(reopened, nextSelection)).WorkerProcessId == pid && IsAlive(pid),
            "persisted rollback preserves a verified old worker with no version field");
        Require(View(reopened, nextSelection).DesiredRunning &&
            View(reopened, nextSelection).Config.CombatFlowVersion == CombatFlowVersion.CoordinatedV2,
            "legacy run intent and future account preference are both preserved");
        Require((await reopened.SetForceLegacyCombatFlowAsync(true)).Count == 0 && IsAlive(pid),
            "repeating rollback does not mistake a saved preference for the adopted runtime");
        Require((await test.InfoAsync(nextSelection)).Starts == 1,
            "preserving the legacy worker never replays its startup");
    }

    public static async Task<int> RunVersionedCombatFlowChildAsync(string[] args)
    {
        // This separate fixture augments the original old-protocol mock without
        // changing any existing test case or its backend behavior.
        try
        {
            var index = Array.IndexOf(args, "--account-worker");
            var spec = JsonSerializer.Deserialize<WorkerLaunchSpec>(await File.ReadAllTextAsync(args[index + 1]))!;
            return await new WorkerProcessHost(s => new VersionedCombatFlowBackend(new MockBackend(s, "normal"))).RunAsync(spec);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 99;
        }
    }

    private sealed class VersionedCombatFlowBackend(IWorkerProcessBackend inner) : IWorkerProcessBackend
    {
        private volatile CombatFlowVersion _activeVersion;

        public Task InitializeAsync(CancellationToken cancellationToken) => inner.InitializeAsync(cancellationToken);

        public WorkerStatus GetStatus()
        {
            var status = inner.GetStatus();
            return status with { ActiveCombatFlowVersion = status.IsRunning ? _activeVersion : null };
        }

        public async Task<OperationResult> StartAsync(AccountConfig account, bool cleanupFirst, CancellationToken cancellationToken)
        {
            if (!inner.GetStatus().IsRunning) _activeVersion = account.CombatFlowVersion;
            return await inner.StartAsync(account, cleanupFirst, cancellationToken);
        }

        public Task<OperationResult> StopAsync(CancellationToken cancellationToken) => inner.StopAsync(cancellationToken);
        public Task<OperationResult> ReleaseInputAsync(CancellationToken cancellationToken) => inner.ReleaseInputAsync(cancellationToken);
        public Task<OperationResult<HardwareVerification>> VerifyHardwareAsync(CancellationToken cancellationToken) =>
            inner.VerifyHardwareAsync(cancellationToken);
        public Task<object?> InvokeAsync(string method, JsonElement[] arguments, IProgress<string> progress, CancellationToken cancellationToken) =>
            inner.InvokeAsync(method, arguments, progress, cancellationToken);
        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }
}
