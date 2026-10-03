using System.Text.Json;
using Roadhog.Core.Accounts;
using Roadhog.Core.Common;
using Roadhog.Infrastructure.Hardware;
using Roadhog.Infrastructure.WorkerProcesses;

internal static partial class WorkerProcessManagerTests
{
    public static async Task StandaloneShopRestartsFreshWorkerAsync()
    {
        foreach (var alreadyRunning in new[] { false, true })
        {
            await using var test = new TestEnvironment();
            var account = test.Account(1);
            account.AutoRecover = alreadyRunning; // Planned restart must also work with recovery disabled.
            account.ScriptSettings = new() { MainMode = AccountMainMode.SemiAuto };
            var neighbor = test.Account(2);
            var manager = await test.ManagerAsync(new[] { account, neighbor });
            Require((await manager.StartAsync(neighbor.InstanceId)).Success, "start neighboring account");
            var neighborPid = (await test.RunningAsync(manager, neighbor)).WorkerProcessId;
            if (alreadyRunning) Require((await manager.StartAsync(account.InstanceId)).Success, "start combat before shop");
            var shopSettings = account.ScriptSettings.Clone();
            shopSettings.Maintenance.CleanupWorkflow.StandaloneShopDiscount = 8;
            Require((await manager.StartAsync(account.InstanceId, standaloneShopSettings: shopSettings)).Success, "dispatch discounted shop");
            var oldPid = (await test.RunningAsync(manager, account)).WorkerProcessId!.Value;
            var oldToken = test.Spec(account).Token;
            await test.Client(account).CallAsync<OperationResult>("mock.shop.sold-out", new object?[] { Guid.Empty });
            await Task.Delay(250);
            Require(View(manager, account).WorkerProcessId == oldPid && !test.Logger.Entries.Any(e => e.EventName == "standalone_shop.restart.stopping"),
                "sold-out progress text and an empty identifier cannot trigger a restart");
            var updated = account.Clone();
            updated.ScriptSettings!.Combat.StationaryCombatRadius = 87;
            manager.UpdateAccounts(new[] { updated, neighbor });
            var requestId = Guid.NewGuid();
            Require((await test.Client(account).CallAsync<OperationResult>("mock.shop.sold-out", new object?[] { requestId })).Success, "publish completed sale through IPC");
            await UntilAsync(() => View(manager, account) is { State: "running", WorkerProcessId: { } pid } && pid != oldPid,
                "sold-out account stops then starts a fresh worker");
            var newPid = (await test.RunningAsync(manager, account)).WorkerProcessId;
            Require(!IsAlive(oldPid) && test.Spec(account).Token != oldToken, "old process exits before authenticated replacement");
            var received = JsonSerializer.Deserialize<AccountConfig>(await File.ReadAllTextAsync(Path.Combine(manager.PathsFor(account).LogDirectory, "normal-start.json")))!;
            Require(received.ScriptSettings!.Combat.StationaryCombatRadius == 87 &&
                received.ScriptSettings.Maintenance.CleanupWorkflow.StandaloneShopDiscount == 5,
                "ordinary restart uses current account config instead of replaying one-shot shop settings");
            Require((await test.InfoAsync(account)).Starts == 1 && View(manager, account).Worker?.StandaloneShopRestartRequestId == null,
                "replacement is started normally once with no stale completion");
            await Task.Delay(350);
            Require(View(manager, account).WorkerProcessId == newPid && test.Logger.Entries.Count(e => e.EventName == "standalone_shop.restart.complete") == 1,
                "repeated status polling never replays the sold-out restart");
            Require(View(manager, neighbor).WorkerProcessId == neighborPid && (await test.InfoAsync(neighbor)).Running,
                "restart leaves other account process and state intact");
            Require(new DeviceLeaseStore(test.LeasePath).ReadActive().Value?.Count == 2, "one lease per running account after replacement");
            Require(File.ReadAllText(Path.Combine(test.Root, "config", "workers", "run-intent.json")).Contains(account.InstanceId),
                "successful ordinary start restores durable running intent");
        }
    }

    public static async Task StandaloneShopManualStopWinsAsync()
    {
        await using var test = new TestEnvironment();
        var account = test.Account(1);
        var manager = await test.ManagerAsync(new[] { account });
        Require((await manager.StartAsync(account.InstanceId, standaloneShopSettings: new())).Success, "start shop for cancellation");
        var oldPid = (await test.RunningAsync(manager, account)).WorkerProcessId!.Value;
        var oldToken = test.Spec(account).Token;
        using (await manager.HoldStartAdmissionsAsync())
        {
            await test.Client(account).CallAsync<OperationResult>("mock.shop.sold-out", new object?[] { Guid.NewGuid() });
            await UntilAsync(() => View(manager, account) is { State: "stopped", WorkerProcessId: null }, "automatic stop completes before start admission");
            Require(!IsAlive(oldPid), "original worker has exited at the stop/start boundary");
            Require((await manager.StopAsync(account.InstanceId)).Success, "later user Stop invalidates automatic startup while it waits for admission");
        }
        await UntilAsync(() => test.Logger.Entries.Any(e => e.EventName == "standalone_shop.restart.failed"), "cancelled continuation is observed");
        await Task.Delay(350);
        Require(View(manager, account) is { DesiredRunning: false, WorkerProcessId: null } && test.Spec(account).Token == oldToken,
            "automatic startup cannot undo later user Stop or launch a new process");
        Require(!File.ReadAllText(Path.Combine(test.Root, "config", "workers", "run-intent.json")).Contains(account.InstanceId), "cancelled restart stays stopped durably");
    }

    public static async Task StandaloneShopStopFailureNeverStartsAsync()
    {
        await using var test = new TestEnvironment();
        var account = test.Account(1);
        var reconciler = new ShopExitReconciler();
        var manager = await test.ManagerAsync(new[] { account }, exitReconciler: reconciler);
        Require((await manager.StartAsync(account.InstanceId, standaloneShopSettings: new())).Success, "start shop before injected exit failure");
        var oldPid = (await test.RunningAsync(manager, account)).WorkerProcessId!.Value;
        var token = test.Spec(account).Token;
        reconciler.BlockedPid = oldPid;
        try
        {
            await test.Client(account).CallAsync<OperationResult>("mock.shop.sold-out", new object?[] { Guid.NewGuid() });
            await UntilAsync(() => test.Logger.Entries.Any(e => e.EventName == "standalone_shop.restart.failed"), "stop failure aborts restart");
            await Task.Delay(300);
            Require(!IsAlive(oldPid) && View(manager, account) is { DesiredRunning: false } && test.Spec(account).Token == token,
                "failed exit reconciliation keeps intent stopped and cannot spawn replacement despite AutoRecover");
            Require(View(manager, account).Error?.Contains("mock residual") == true, "stop failure remains actionable");
        }
        finally { reconciler.BlockedPid = 0; }
    }

    public static async Task StandaloneShopManualStopDuringShutdownWinsAsync()
    {
        await using var test = new TestEnvironment();
        var account = test.Account(1);
        var reconciler = new BlockingShopExitReconciler();
        var manager = await test.ManagerAsync(new[] { account }, exitReconciler: reconciler);
        Require((await manager.StartAsync(account.InstanceId, standaloneShopSettings: new())).Success, "start shop before coalesced Stop race");
        var oldPid = (await test.RunningAsync(manager, account)).WorkerProcessId!.Value;
        var token = test.Spec(account).Token;
        try
        {
            await test.Client(account).CallAsync<OperationResult>("mock.shop.sold-out", new object?[] { Guid.NewGuid() });
            await reconciler.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Require(!IsAlive(oldPid), "automatic stop is reconciling the exited worker");
            var explicitStop = manager.StopAsync(account.InstanceId);
            reconciler.Release.TrySetResult();
            Require((await explicitStop).Success, "user Stop coalesces with existing stop task");
            await UntilAsync(() => test.Logger.Entries.Any(e => e.EventName == "standalone_shop.restart.failed"), "later coalesced Stop cancels continuation");
            Require(View(manager, account) is { DesiredRunning: false, WorkerProcessId: null } && test.Spec(account).Token == token,
                "coalesced Stop still invalidates the pending automatic Start generation");
        }
        finally { reconciler.Release.TrySetResult(); }
    }

    public static async Task StandaloneShopCompletionSurvivesManagerReconnectAsync()
    {
        await using var test = new TestEnvironment();
        var account = test.Account(1);
        account.AutoRecover = false;
        var manager = await test.ManagerAsync(new[] { account });
        Require((await manager.StartAsync(account.InstanceId, standaloneShopSettings: new())).Success, "start shop before manager detaches");
        var oldPid = (await test.RunningAsync(manager, account)).WorkerProcessId!.Value;
        await test.DetachAsync(manager);
        Require(IsAlive(oldPid), "detaching the manager keeps shop worker alive");
        var requestId = Guid.NewGuid();
        await test.Client(account).CallAsync<OperationResult>("mock.shop.sold-out", new object?[] { requestId });
        Require((await test.Client(account).CallAsync<WorkerStatus>(WorkerCommands.Status, Array.Empty<object?>())).StandaloneShopRestartRequestId == requestId,
            "completion remains visible while manager is disconnected");
        manager = await test.ManagerAsync(new[] { account });
        await UntilAsync(() => View(manager, account) is { State: "running", WorkerProcessId: { } pid } && pid != oldPid,
            "reconnected manager adopts completion and runs planned Stop/Start despite AutoRecover=false");
        await test.RunningAsync(manager, account);
        Require(!IsAlive(oldPid) && (await test.InfoAsync(account)).Starts == 1, "reconnection completes exactly one fresh ordinary startup");
    }

    public static async Task StandaloneShopStartFailureNeverLoopsAsync()
    {
        await using var test = new TestEnvironment();
        var account = test.Account(1);
        var manager = await test.ManagerAsync(new[] { account });
        Require((await manager.StartAsync(account.InstanceId, standaloneShopSettings: new())).Success, "start shop before rejected ordinary startup");
        await test.RunningAsync(manager, account);
        var rejection = Path.Combine(manager.PathsFor(account).LogDirectory, "reject-normal-start");
        await File.WriteAllTextAsync(rejection, "reject");
        await test.Client(account).CallAsync<OperationResult>("mock.shop.sold-out", new object?[] { Guid.NewGuid() });
        await UntilAsync(() => test.Logger.Entries.Any(e => e.EventName == "standalone_shop.restart.failed"), "ordinary start failure reported");
        var token = test.Spec(account).Token;
        await Task.Delay(500);
        Require(View(manager, account) is { DesiredRunning: false, WorkerProcessId: null } && test.Spec(account).Token == token,
            "rejected ordinary start neither loops through AutoRecover nor replays shop");
        Require(!File.ReadAllText(Path.Combine(test.Root, "config", "workers", "run-intent.json")).Contains(account.InstanceId), "failed restart clears durable intent");
        File.Delete(rejection);
        Require((await manager.StartAsync(account.InstanceId)).Success, "explicit Start remains possible after correcting failure");
        Require((await test.InfoAsync(account)).Starts == 1, "corrected Start uses a fresh normal worker");
    }

    public static async Task StandaloneShopShutdownWinsAsync()
    {
        await using var test = new TestEnvironment();
        var account = test.Account(1);
        var manager = await test.ManagerAsync(new[] { account });
        Require((await manager.StartAsync(account.InstanceId, standaloneShopSettings: new())).Success, "start shop before application shutdown");
        await test.RunningAsync(manager, account);
        var token = test.Spec(account).Token;
        using (await manager.HoldStartAdmissionsAsync())
        {
            await test.Client(account).CallAsync<OperationResult>("mock.shop.sold-out", new object?[] { Guid.NewGuid() });
            await UntilAsync(() => View(manager, account) is { State: "stopped", WorkerProcessId: null }, "stop completes before shutdown");
            manager.BeginShutdown();
            RequireAll((await manager.StopAllAsync()).Values, "application shutdown cancels pending restart");
        }
        await test.DetachAsync(manager).WaitAsync(TimeSpan.FromSeconds(5));
        Require(test.Spec(account).Token == token, "shutdown and disposal cannot launch a replacement after sale");
    }

    private sealed class ShopExitReconciler : IWorkerProcessExitReconciler
    {
        public int BlockedPid;
        public Task ReconcileAsync(WorkerDescriptor descriptor, string executablePath, CancellationToken cancellationToken = default)
        {
            Require(!IsAlive(descriptor.ProcessId), "Stop must wait for actual old process exit before reconciliation");
            if (descriptor.ProcessId == Volatile.Read(ref BlockedPid)) throw new InvalidOperationException("mock residual process blocks restart");
            return Task.CompletedTask;
        }
    }

    private sealed class BlockingShopExitReconciler : IWorkerProcessExitReconciler
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task ReconcileAsync(WorkerDescriptor descriptor, string executablePath, CancellationToken cancellationToken = default)
        {
            Require(!IsAlive(descriptor.ProcessId), "reconcile only an exited owned worker");
            Entered.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
        }
    }
}
