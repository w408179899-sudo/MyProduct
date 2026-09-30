using Roadhog.Application.Licensing;
using Roadhog.Core.Accounts;
using Roadhog.Core.Diagnostics;
using Roadhog.Infrastructure.Config;
using Roadhog.Infrastructure.Diagnostics;
using Roadhog.Infrastructure.Hardware;
using Roadhog.Infrastructure.Input;
using Roadhog.Infrastructure.Licensing;
using Roadhog.Infrastructure.Paths;
using Roadhog.Infrastructure.Profiles;
using Roadhog.Infrastructure.Radar;
using Roadhog.Infrastructure.WorkerProcesses;

namespace Roadhog.Infrastructure.Composition;

/// <summary>The desktop owns configuration. This composition never constructs a game provider or keyboard.</summary>
public sealed class MultiAccountWorkspace : IAsyncDisposable
{
    private readonly object _disposeSync = new();
    private readonly SemaphoreSlim _saveDisposeGate = new(1, 1);
    private Task? _disposeTask;
    private int _disposing;
    public RoadhogServiceOptions Options { get; }
    public IRoadhogLogger Logger { get; }
    public JsonAccountConfigStore Accounts { get; }
    public JsonSharedPathStore Paths { get; }
    public JsonScriptProfileStore Profiles { get; }
    public JsonRadarMapStore RadarMaps { get; }
    public JsonBagCleanupNameListStore NameLists { get; }
    public WindowsHardwareDeviceResolver Hardware { get; }
    public WorkerProcessManager Processes { get; }
    private readonly DeviceLeaseStore _deviceLeases;

    public HardwareSelectionAvailability HardwareAvailability(string editingId)
    {
        var leases = _deviceLeases.ReadActive();
        if (!leases.Success || leases.Value is null) throw new InvalidOperationException("无法读取设备占用状态：" + leases.Error);
        return new HardwareSelectionAvailability(editingId, Processes.Snapshot(), leases.Value);
    }

    public MultiAccountWorkspace(RoadhogServiceOptions? options = null, WorkerProcessLaunchOptions? launch = null)
    {
        Options = options ?? RoadhogServiceOptions.FromEnvironment();
        Logger = new FileRoadhogLogger(Path.Combine(Options.LogDirectory, "manager"));
        Accounts = new(Options.AccountConfigPath);
        Paths = new(Options.PathLibraryDirectory);
        Profiles = new(Options.ProfileLibraryDirectory);
        RadarMaps = new(Options.RadarMapDirectory);
        var directory = Path.GetDirectoryName(Path.GetFullPath(Options.AccountConfigPath))!;
        NameLists = new(Options.BagCleanupNameListPath ?? Path.Combine(directory, JsonBagCleanupNameListStore.DefaultFileName),
            logger: Logger);
        Hardware = new(Options.HardwareResolver);
        Processes = new(Options, Logger, launch);
        _deviceLeases = new(launch?.LeasePath);
    }

    public async Task<IReadOnlyList<AccountConfig>> InitializeAsync(CancellationToken cancellationToken)
    {
        var result = await Accounts.LoadAllAsync(cancellationToken).ConfigureAwait(false);
        if (!result.Success || result.Value is null) throw new InvalidOperationException(result.Error);
        var accounts = result.Value.Select(a => a.Clone()).ToArray();
        var legacyKmBox = new JsonKmBoxNetDeviceConfigStore(Options.KmBoxNetConfigPath).Load().Value;
        var legacyPrimary = accounts.FirstOrDefault(a => HasPhysicalKey(a.HardwareKey)) ?? accounts.FirstOrDefault();
        var regionsBefore = accounts.Select(a => a.Region).ToArray();
        var changed = false;
        foreach (var account in accounts)
        {
            if (Guid.TryParse(account.InstanceId, out _)) continue;
            account.InstanceId = Guid.NewGuid().ToString("D");
            if (ReferenceEquals(account, legacyPrimary) && account.KmBox is null && legacyKmBox?.IsConfigured == true)
                account.KmBox = new AccountKmBoxSettings { IpAddress = legacyKmBox.IpAddress, Port = legacyKmBox.Port, Mac = legacyKmBox.Mac };
            if (ReferenceEquals(account, legacyPrimary) && string.IsNullOrWhiteSpace(account.LicenseCredentialPath)) account.LicenseCredentialPath = Options.LicenseCredentialPath;
            if (string.IsNullOrWhiteSpace(account.VmmDeviceName) || account.VmmDeviceName.Equals("fpga", StringComparison.OrdinalIgnoreCase))
            {
                var binding = HasPhysicalKey(account.HardwareKey) ? Hardware.BindByKey(account.AccountName, account.HardwareKey) : null;
                // Leave an offline/ambiguous binding unconfigured instead of guessing another device.
                account.VmmDeviceName = binding?.Success == true && binding.Value is not null &&
                    !binding.Value.VmmDeviceName.Equals("fpga", StringComparison.OrdinalIgnoreCase)
                    ? binding.Value.VmmDeviceName : string.Empty;
            }
            changed = true;
        }
        await SharedCleanupMigration.MigrateAsync(Options.AccountConfigPath, accounts, Options.ProfileLibraryDirectory,
            Options.BagCleanupNameListPath, cancellationToken).ConfigureAwait(false);
        changed |= !regionsBefore.SequenceEqual(accounts.Select(a => a.Region));
        if (changed)
        {
            // Keep a readable legacy backup before the first conversion.
            var backup = Options.AccountConfigPath + ".before-multi-account.bak";
            if (File.Exists(Options.AccountConfigPath) && !File.Exists(backup)) File.Copy(Options.AccountConfigPath, backup);
            var saved = await Accounts.SaveAllAsync(accounts, cancellationToken).ConfigureAwait(false);
            if (!saved.Success) throw new InvalidOperationException(saved.Error);
        }
        await Processes.InitializeAsync(accounts, cancellationToken).ConfigureAwait(false);
        return accounts;
    }

    public async Task SaveAccountsAsync(IReadOnlyList<AccountConfig> accounts, CancellationToken cancellationToken = default)
    {
        await _saveDisposeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (Volatile.Read(ref _disposing) != 0) throw new ObjectDisposedException(nameof(MultiAccountWorkspace));
            // Keep start admission closed through validation, disk persistence, and the manager update.
            // Otherwise a worker can start with the old binding after validation but before the new file is written.
            using var admission = await Processes.HoldStartAdmissionsAsync(cancellationToken).ConfigureAwait(false);
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var credentials = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var account in accounts)
            {
                if (!Guid.TryParse(account.InstanceId, out _) || !ids.Add(account.InstanceId)) throw new InvalidOperationException("账号实例标识无效或重复。");
                if (!credentials.Add(Path.GetFullPath(Processes.PathsFor(account).LicenseCredentialPath))) throw new InvalidOperationException("两个账号不能共用同一份客户端授权凭据，请为新账号完成独立授权或导入其原有授权。");
            }
            // Stopped accounts may save overlapping selections while devices are reassigned one at a time.
            // Recheck new selections against live ownership; unchanged saved overlaps do not block settings saves.
            var previous = Processes.Snapshot().ToDictionary(view => view.Config.InstanceId, view => view.Config, StringComparer.OrdinalIgnoreCase);
            foreach (var account in accounts)
                if (!previous.TryGetValue(account.InstanceId, out var old) || HardwareSelectionChanged(old, account))
                    HardwareAvailability(account.InstanceId).EnsureAvailable(account);
            Processes.ValidateAccountUpdate(accounts);
            await SharedCleanupMigration.MigrateAsync(Options.AccountConfigPath, accounts, Options.ProfileLibraryDirectory,
                Options.BagCleanupNameListPath, cancellationToken).ConfigureAwait(false);
            var result = await Accounts.SaveAllAsync(accounts, cancellationToken).ConfigureAwait(false);
            if (!result.Success) throw new InvalidOperationException(result.Error);
            Processes.UpdateAccounts(accounts);
        }
        finally { _saveDisposeGate.Release(); }
    }

    private static bool HardwareSelectionChanged(AccountConfig a, AccountConfig b) =>
        (a.HardwareKey, a.HardwareDeviceInstanceId, a.VmmDeviceName, a.KmBox?.IpAddress, a.KmBox?.Port, a.KmBox?.Mac) !=
        (b.HardwareKey, b.HardwareDeviceInstanceId, b.VmmDeviceName, b.KmBox?.IpAddress, b.KmBox?.Port, b.KmBox?.Mac);

    public LicenseCoordinator CreateLicenseCoordinator(AccountConfig account)
    {
        var paths = Processes.PathsFor(account);
        var identity = new WindowsDeviceIdentityProvider();
        return new LicenseCoordinator(
            new CloudflareLicenseApiClient(new HttpClient { BaseAddress = new Uri(paths.LicenseServerUrl.TrimEnd('/') + "/"), Timeout = Options.LicenseRequestTimeout }),
            new DpapiLicenseCredentialStore(paths.LicenseCredentialPath), identity, Logger,
            new LicenseCoordinatorOptions
            {
                HeartbeatInterval = Options.LicenseHeartbeatInterval, HeartbeatRetryCount = Options.LicenseHeartbeatRetryCount,
                HeartbeatRetryDelay = Options.LicenseHeartbeatRetryDelay, ClientVersion = typeof(MultiAccountWorkspace).Assembly.GetName().Version?.ToString(3) ?? "unknown"
            }, ownerLicenseGrantProvider: new SignedOwnerLicenseGrantProvider(paths.OwnerLicenseGrantPath, identity));
    }

    public IBagCleanupNameListStore NameListsFor(AccountConfig account) =>
        new SharedAccountConfigurationStore(SharedAccountConfigurationStore.PathFor(Options.AccountConfigPath), account.Region);

    public async Task MigrateSharedConfigurationAsync(IReadOnlyList<AccountConfig> accounts, CancellationToken token = default)
    {
        var before = accounts.Select(a => a.Region).ToArray();
        await SharedCleanupMigration.MigrateAsync(Options.AccountConfigPath, accounts, Options.ProfileLibraryDirectory,
            Options.BagCleanupNameListPath, token).ConfigureAwait(false);
        if (!before.SequenceEqual(accounts.Select(a => a.Region)))
        {
            var result = await Accounts.SaveAllAsync(accounts, token).ConfigureAwait(false);
            if (!result.Success) throw new InvalidOperationException(result.Error);
        }
    }

    public JsonRadarMapStore RadarMapsFor(AccountConfig account) =>
        new(Processes.PathsFor(account).RadarMapDirectory);

    public ValueTask DisposeAsync()
    {
        lock (_disposeSync)
        {
            if (_disposeTask is null)
            {
                Interlocked.Exchange(ref _disposing, 1);
                _disposeTask = DisposeCoreAsync();
            }
            return new ValueTask(_disposeTask);
        }
    }

    private async Task DisposeCoreAsync()
    {
        await _saveDisposeGate.WaitAsync().ConfigureAwait(false);
        try { await Processes.DisposeAsync().ConfigureAwait(false); }
        finally { _saveDisposeGate.Release(); }
    }

    private static bool HasPhysicalKey(string key) => !string.IsNullOrWhiteSpace(key) &&
        !key.Trim().StartsWith("auto", StringComparison.OrdinalIgnoreCase) && key.Trim() != "0";
}
