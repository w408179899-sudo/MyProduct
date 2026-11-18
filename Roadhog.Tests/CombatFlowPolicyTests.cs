using Roadhog.Application;
using Roadhog.Application.Workers;
using Roadhog.Core.Accounts;
using Roadhog.Core.Diagnostics;
using Roadhog.Core.Profiles;
using Roadhog.Core.Hardware;
using Roadhog.Infrastructure.Config;
using Roadhog.Infrastructure.Profiles;

internal static class CombatFlowPolicyTests
{
    public static Task DefaultsAndUnknownVersionsAsync()
    {
        Require(new AccountConfig().CombatFlowVersion == CombatFlowVersion.Legacy, "old configurations default to legacy");
        var policy = new CombatFlowPolicy();
        Require(policy.Resolve(CombatFlowVersion.CoordinatedV2) == CombatFlowVersion.CoordinatedV2, "explicit opt-in selects new flow");
        Require(policy.Resolve((CombatFlowVersion)999) == CombatFlowVersion.Legacy, "unknown versions select legacy");
        policy.ForceLegacy = true;
        Require(policy.Resolve(CombatFlowVersion.CoordinatedV2) == CombatFlowVersion.Legacy, "global rollback overrides account opt-in");
        return Task.CompletedTask;
    }

    public static async Task PolicyPersistenceAndMalformedFallbackAsync()
    {
        using var temp = new TemporaryDirectory();
        var accountsPath = Path.Combine(temp.Path, "accounts.json");
        var store = new JsonCombatFlowPolicyStore(JsonCombatFlowPolicyStore.PathFor(accountsPath));
        var logger = new InMemoryRoadhogLogger();
        Require(await store.ResolveAsync(CombatFlowVersion.CoordinatedV2, logger) == CombatFlowVersion.CoordinatedV2,
            "missing policy preserves explicit account opt-in");
        Require((await store.SaveAsync(true)).Success, "rollback persists");
        var restarted = new JsonCombatFlowPolicyStore(store.FilePath);
        Require(await restarted.ResolveAsync(CombatFlowVersion.CoordinatedV2, logger) == CombatFlowVersion.Legacy,
            "rollback survives reader recreation");
        var accounts = new JsonAccountConfigStore(accountsPath);
        Require((await accounts.UpsertAsync(new() { AccountName = "one", CombatFlowVersion = CombatFlowVersion.CoordinatedV2 })).Success,
            "normal account edit saves");
        Require((await restarted.LoadAsync()).Value!.ForceLegacy, "account save cannot erase global rollback");
        Require((await restarted.SaveAsync(false)).Success, "explicitly release rollback");
        Require(await store.ResolveAsync(CombatFlowVersion.CoordinatedV2, logger) == CombatFlowVersion.CoordinatedV2,
            "new startup can honor account selection after release");
        await File.WriteAllTextAsync(store.FilePath, "{broken");
        Require(await store.ResolveAsync(CombatFlowVersion.CoordinatedV2, logger) == CombatFlowVersion.Legacy,
            "malformed policy cannot silently enable v2");
        Require(logger.Entries.Any(entry => entry.EventName == "combat_flow.policy.invalid_using_legacy"), "fallback is diagnosed");
        using var stop = new CancellationTokenSource(); stop.Cancel();
        try { await store.ResolveAsync(CombatFlowVersion.CoordinatedV2, logger, stop.Token); }
        catch (OperationCanceledException) { return; }
        throw new InvalidOperationException("cancelled startup must not fall through to either flow");
    }

    public static async Task AccountSelectionSurvivesProfileAndSerializationAsync()
    {
        using var temp = new TemporaryDirectory();
        var accounts = new JsonAccountConfigStore(Path.Combine(temp.Path, "accounts.json"));
        var profiles = new JsonScriptProfileStore(Path.Combine(temp.Path, "profiles"));
        Require((await profiles.SaveAsync(new ScriptProfileDocument { Name = "shared", Settings = new() { ProfileName = "shared" } })).Success,
            "shared profile saves");
        var selected = new AccountConfig { AccountName = "new", ProfileName = "shared", CombatFlowVersion = CombatFlowVersion.CoordinatedV2 };
        var stable = new AccountConfig { AccountName = "old", ProfileName = "shared" };
        Require((await accounts.SaveAllAsync([selected, stable])).Success, "version selections serialize");
        var loaded = (await accounts.LoadAllAsync()).Value!;
        var builder = new AccountStartConfigBuilder(profiles, new JsonBagCleanupNameListStore(Path.Combine(temp.Path, "lists.json")),
            new InMemoryRoadhogLogger());
        var newStart = await builder.BuildAsync(loaded.Single(account => account.AccountName == "new"));
        var oldStart = await builder.BuildAsync(loaded.Single(account => account.AccountName == "old"));
        Require(newStart.Success && newStart.Value!.CombatFlowVersion == CombatFlowVersion.CoordinatedV2,
            "shared profile load retains account opt-in");
        Require(oldStart.Success && oldStart.Value!.CombatFlowVersion == CombatFlowVersion.Legacy,
            "same shared profile does not enable another account");
        newStart.Value!.CombatFlowVersion = CombatFlowVersion.Legacy;
        Require(loaded.Single(account => account.AccountName == "new").CombatFlowVersion == CombatFlowVersion.CoordinatedV2,
            "effective startup changes cannot rewrite saved account selection");
    }

    public static async Task DirectStartupUsesPolicyAndKeepsRunningVersionAsync()
    {
        using var temp = new TemporaryDirectory();
        var policy = new JsonCombatFlowPolicyStore(JsonCombatFlowPolicyStore.PathFor(Path.Combine(temp.Path, "accounts.json")));
        Require((await policy.SaveAsync(true)).Success, "force legacy before direct start");
        var logger = new InMemoryRoadhogLogger();
        var loop = new HoldingLoop();
        var hardware = new InMemoryHardwareDeviceResolver(new HardwareDeviceFeature(
            "mock-flow", "usb-port", "high", "device", "parent", "container", "hardware", "location", "mock", "mock", "fpga", ["mock-flow"]));
        var orchestrator = new AccountOrchestrator(new FakeGameApi(), logger, new AccountRuntimeManager(logger),
            hardware, new InMemoryTargetProcessResolver(), loop, new(),
            resolveCombatFlowVersion: requested => policy.ResolveAsync(requested, logger).GetAwaiter().GetResult());
        var account = new AccountConfig { AccountName = "direct", HardwareKey = "mock-flow", CombatFlowVersion = CombatFlowVersion.CoordinatedV2 };
        try
        {
            Require(orchestrator.Start(account).Success, "direct start accepted");
            var first = await loop.Entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Require(first.Config.CombatFlowVersion == CombatFlowVersion.Legacy &&
                orchestrator.GetActiveCombatFlowVersion(account.AccountName) == CombatFlowVersion.Legacy, "direct flow and status use effective version");
            Require(account.CombatFlowVersion == CombatFlowVersion.CoordinatedV2, "resolving does not rewrite caller selection");
            Require((await policy.SaveAsync(false)).Success, "release override while running");
            Require(first.Config.CombatFlowVersion == CombatFlowVersion.Legacy, "running worker retains selected implementation");
            Require((await orchestrator.StopAsync(account.AccountName)).Success, "stop first implementation");
            Require(orchestrator.GetActiveCombatFlowVersion(account.AccountName) is null, "stopped worker has no active implementation");
            loop.Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
            Require(orchestrator.Start(account).Success, "explicit restart accepted");
            var second = await loop.Entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Require(second.Config.CombatFlowVersion == CombatFlowVersion.CoordinatedV2 &&
                orchestrator.GetActiveCombatFlowVersion(account.AccountName) == CombatFlowVersion.CoordinatedV2, "next worker uses new selection");
            Require(first.StopToken.IsCancellationRequested, "previous worker is cancelled before replacement starts");
        }
        finally { await orchestrator.StopAllAsync(); }
    }

    private sealed class HoldingLoop : IAccountWorkerLoop
    {
        public TaskCompletionSource<AccountWorkerContext> Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task RunAsync(AccountWorkerContext context)
        {
            Entered.TrySetResult(context);
            await Task.Delay(Timeout.Infinite, context.StopToken);
        }
    }

    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "roadhog-flow-policy-" + Guid.NewGuid().ToString("N"));
        public TemporaryDirectory() => Directory.CreateDirectory(Path);
        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
