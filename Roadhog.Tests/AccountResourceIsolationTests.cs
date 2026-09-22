using System.Reflection;
using System.Text.Json;
using Roadhog;
using Roadhog.Core.Accounts;
using Roadhog.Core.Common;
using Roadhog.Core.Hardware;
using Roadhog.Infrastructure.Composition;
using Roadhog.Infrastructure.Config;
using Roadhog.Infrastructure.Radar;
using Roadhog.Infrastructure.WorkerProcesses;

internal static class AccountResourceIsolationTests
{
    public static async Task HostRejectsResourceSourceChangesAsync()
    {
        await using var files = new ResourceEnvironment();
        var account = files.Account();
        var key = Guid.NewGuid().ToString("N");
        var spec = new WorkerLaunchSpec
        {
            Account = account, Paths = files.Workspace.Processes.PathsFor(account),
            PipeName = "Roadhog.ResourceIsolation." + key, Token = key + key,
            ManifestPath = Path.Combine(files.Root, "worker.json"), LeasePath = Path.Combine(files.Root, "leases.json")
        };
        var swaps = new Action<AccountConfig>[]
        {
            value => value.BagCleanupNameListPath = "preserved/2/bag-cleanup-name-lists.json",
            value => value.RadarMapDirectory = "preserved/2/radar-maps",
            value => value.OwnerLicenseGrantPath = "preserved/2/owner-license.json",
            value => value.LicenseCredentialPath = "preserved/2/license.dat"
        };
        foreach (var swap in swaps)
        {
            var changed = account.Clone(); swap(changed);
            var factoryCalled = false;
            var rejected = await new WorkerProcessHost(_ => { factoryCalled = true; return new Backend(); })
                .RunAsync(spec with { Account = changed });
            Require(rejected == 12 && !factoryCalled, "inconsistent launch resource paths must fail before constructing a backend");
        }

        var backend = new Backend();
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(12));
        var running = new WorkerProcessHost(_ => backend).RunAsync(spec, lifetime.Token);
        var client = new WorkerRpcClient(spec.PipeName, spec.Token);
        try
        {
            while (!(await client.CallAsync<WorkerStatus>(WorkerCommands.Status, [], lifetime.Token)).InitializationComplete)
                await Task.Delay(10, lifetime.Token);
            Require((await client.CallAsync<OperationResult>(WorkerCommands.Start, [account], lifetime.Token)).Success,
                "the launch account can start with its own resource sources");
            foreach (var command in new[] { WorkerCommands.Start, WorkerCommands.Cleanup })
                foreach (var swap in swaps)
                {
                    var changed = account.Clone(); swap(changed);
                    try
                    {
                        await client.CallAsync<OperationResult>(command, [changed], lifetime.Token);
                        throw new InvalidOperationException("resource substitution unexpectedly reached the backend");
                    }
                    catch (WorkerRpcException exception) when (exception.Message.Contains("来源已更改", StringComparison.Ordinal)) { }
                }
            Require(backend.Starts == 1, "rejected cross-account resource changes cannot start or clean up with another source");
        }
        finally
        {
            lifetime.Cancel();
            await running.WaitAsync(TimeSpan.FromSeconds(4));
        }
    }

    public static Task SettingsAndHardwarePreserveAccountSourcesAsync() => RunStaAsync(async () =>
    {
        await using var files = new ResourceEnvironment();
        var account = files.Account();
        var sibling = files.Account(); sibling.AccountName = "resource-sibling";
        sibling.HardwareKey += "-2"; sibling.VmmDeviceName = "fpga://devindex=92002";
        sibling.KmBox!.IpAddress = "127.0.0.2"; sibling.KmBox.Mac = "resource-mac-2";
        sibling.BagCleanupNameListPath = "preserved/2/bag-cleanup-name-lists.json";
        sibling.RadarMapDirectory = "preserved/2/radar-maps";
        sibling.OwnerLicenseGrantPath = "preserved/2/owner-license.json";
        sibling.LicenseCredentialPath = "preserved/2/license.dat";
        await files.Workspace.SaveAccountsAsync(new[] { account, sibling });
        Require((await files.Workspace.NameListsFor(account).SaveAsync(new() { Whitelist = new() { "first-only" } })).Success,
            "first account can edit shared public lists");
        Require((await files.Workspace.NameListsFor(sibling).SaveChangesAsync(new(), new() { Whitelist = new() { "second-only" } })).Success,
            "second account can add to shared public lists");
        using var console = new MultiAccountForm(files.Workspace) { ShowInTaskbar = false };
        Field<System.Windows.Forms.NotifyIcon>(console, "_tray").Visible = false;
        foreach (var selected in new[] { account, sibling })
        {
            using var settings = (AccountSettingsForm)Invoke(console, "CreateAccountSettingsForm", selected)!;
            var selectedPaths = files.Workspace.Processes.PathsFor(selected);
            Require(Field<IBagCleanupNameListStore>(settings, "_bagCleanupNameListStore").FilePath == SharedAccountConfigurationStore.PathFor(files.Workspace.Options.AccountConfigPath)
                && Field<JsonRadarMapStore>(settings, "_radarMapStore").DirectoryPath == selectedPaths.RadarMapDirectory,
                "main-window settings use shared cleanup lists while maps remain account-specific");
            object?[] arguments = { null };
            Require((bool)Method(settings, "SaveCurrentSettings").Invoke(settings, arguments)!,
                "settings save remains available without a hardware worker: " + arguments[0]);
            var saved = (await files.Workspace.Accounts.LoadAllAsync()).Value!.Single(value => value.InstanceId == selected.InstanceId);
            AssertSources(saved, selected, "settings save");

            using var hardware = new AccountHardwareForm(selected, new[] { Device(selected) }, (_, _) =>
                throw new InvalidOperationException("unchanged hardware settings must not request verification"));
            Invoke(hardware, "Save");
            Require(hardware.DialogResult == System.Windows.Forms.DialogResult.OK, "unchanged hardware settings must save successfully");
            AssertSources(hardware.Config, selected, "hardware save");
        }
        var firstList = await files.Workspace.NameListsFor(account).LoadAsync();
        var secondList = await files.Workspace.NameListsFor(sibling).LoadAsync();
        Require(firstList.Value?.Document?.Whitelist.SequenceEqual(new[] { "first-only", "second-only" }) == true
            && secondList.Value?.Document?.Whitelist.SequenceEqual(new[] { "first-only", "second-only" }) == true,
            "both accounts see the same combined public rules");
        Require(files.Workspace.Processes.Snapshot().All(view => view.WorkerProcessId is null),
            "opening and saving resource-aware settings must not construct hardware workers");
    });

    private static void AssertSources(AccountConfig actual, AccountConfig expected, string action) =>
        Require(actual.BagCleanupNameListPath == expected.BagCleanupNameListPath && actual.RadarMapDirectory == expected.RadarMapDirectory
            && actual.OwnerLicenseGrantPath == expected.OwnerLicenseGrantPath && actual.LicenseCredentialPath == expected.LicenseCredentialPath,
            action + " must preserve all account resource and authorization sources");

    private static HardwareDeviceFeature Device(AccountConfig account) => new(account.HardwareKey, "port", "physical", "mock-device", "mock-parent",
        "mock-container", "mock-hardware", "mock-location", "mock DMA", "Mock", account.VmmDeviceName, new[] { account.HardwareKey });
    private static MethodInfo Method(object instance, string name) => instance.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static object? Invoke(object instance, string name, params object?[] arguments) => Method(instance, name).Invoke(instance, arguments);
    private static T Field<T>(object instance, string name) => (T)instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(instance)!;
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

    private static Task RunStaAsync(Func<Task> test)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                SynchronizationContext.SetSynchronizationContext(new System.Windows.Forms.WindowsFormsSynchronizationContext());
                var task = test();
                var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(12);
                while (!task.IsCompleted)
                {
                    if (DateTime.UtcNow >= deadline) throw new TimeoutException("Account resource UI test timed out.");
                    System.Windows.Forms.Application.DoEvents(); Thread.Sleep(1);
                }
                task.GetAwaiter().GetResult(); completion.TrySetResult();
            }
            catch (Exception exception) { completion.TrySetException(exception); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); return completion.Task;
    }

    private sealed class ResourceEnvironment : IAsyncDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "roadhog-resource-tests-" + Guid.NewGuid().ToString("N"));
        public MultiAccountWorkspace Workspace { get; }
        public ResourceEnvironment()
        {
            Workspace = new(new RoadhogServiceOptions
            {
                AccountConfigPath = Path.Combine(Root, "config", "accounts.json"),
                PathLibraryDirectory = Path.Combine(Root, "config", "paths"), ProfileLibraryDirectory = Path.Combine(Root, "config", "profiles"),
                RadarMapDirectory = Path.Combine(Root, "config", "radar-maps"), LogDirectory = Path.Combine(Root, "logs"),
                OwnerLicenseGrantPath = Path.Combine(Root, "config", "owner-license.json")
            });
        }
        public AccountConfig Account() => new()
        {
            InstanceId = Guid.NewGuid().ToString("N"), AccountName = "resource-account", CharacterName = "mock-role", HardwareKey = "mock-" + Guid.NewGuid().ToString("N"),
            HardwareVerificationSessionId = Roadhog.Infrastructure.Hardware.HardwareVerificationSession.CurrentId,
            HardwareDeviceInstanceId = "mock-device", VmmDeviceName = "fpga://devindex=92001", KmBox = new() { IpAddress = "127.0.0.1", Port = 12345, Mac = Guid.NewGuid().ToString("N")[..8] },
            BagCleanupNameListPath = "preserved/1/bag-cleanup-name-lists.json", RadarMapDirectory = "preserved/1/radar-maps",
            OwnerLicenseGrantPath = "preserved/1/owner-license.json", LicenseCredentialPath = "preserved/1/license.dat", ScriptSettings = new()
        };
        public async ValueTask DisposeAsync()
        {
            await Workspace.DisposeAsync();
            var root = Path.GetFullPath(Root);
            Require(root.StartsWith(Path.Combine(Path.GetFullPath(Path.GetTempPath()), "roadhog-resource-tests-"), StringComparison.OrdinalIgnoreCase),
                "test cleanup must stay in its unique temporary directory");
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private sealed class Backend : IWorkerProcessBackend
    {
        public int Starts;
        public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public WorkerStatus GetStatus() => new() { Authorized = true };
        public Task<OperationResult> StartAsync(AccountConfig account, bool cleanupFirst, CancellationToken cancellationToken)
        { Starts++; return Task.FromResult(OperationResult.Ok()); }
        public Task<OperationResult> StopAsync(CancellationToken cancellationToken) => Task.FromResult(OperationResult.Ok());
        public Task<OperationResult<HardwareVerification>> VerifyHardwareAsync(CancellationToken cancellationToken) =>
            Task.FromResult(OperationResult<HardwareVerification>.Fail("unused mock verification"));
        public Task<object?> InvokeAsync(string method, JsonElement[] arguments, IProgress<string> progress, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("unused mock RPC");
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
