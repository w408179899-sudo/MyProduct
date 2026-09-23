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
    private Task? _disposeTask;
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
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var hardware = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var kmbox = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var macs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var credentials = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var account in accounts)
        {
            if (!Guid.TryParse(account.InstanceId, out _) || !ids.Add(account.InstanceId)) throw new InvalidOperationException("账号实例标识无效或重复。");
            if (!string.IsNullOrWhiteSpace(account.HardwareKey) && !hardware.Add(account.HardwareKey)) throw new InvalidOperationException("DMA 设备已被其他账号绑定。");
            if (account.KmBox is { } input && !kmbox.Add(input.IpAddress.Trim() + ":" + input.Port)) throw new InvalidOperationException("KMBox 地址已被其他账号绑定。");
            if (account.KmBox is { } box && !macs.Add(box.Mac.Replace(":", "").Replace("-", "").Trim())) throw new InvalidOperationException("KMBox 设备已被其他账号绑定。");
            if (!credentials.Add(Path.GetFullPath(Processes.PathsFor(account).LicenseCredentialPath))) throw new InvalidOperationException("两个账号不能共用同一份客户端授权凭据，请为新账号完成独立授权或导入其原有授权。");
        }
        // Saved indices may be stale after reboot. Allow stopped accounts to be corrected one at a time
        // even when their old indices form a swap; actual running ownership remains protected by leases.
        var activeIds = Processes.Snapshot().Where(view => view.DesiredRunning || view.WorkerProcessId.HasValue)
            .Select(view => view.Config.InstanceId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (accounts.Where(account => !string.IsNullOrWhiteSpace(account.VmmDeviceName))
            .GroupBy(account => DeviceLeaseStore.CanonicalVmmDeviceName(account.VmmDeviceName), StringComparer.OrdinalIgnoreCase)
            .Any(group => group.Count() > 1 && group.Any(account => activeIds.Contains(account.InstanceId))))
            throw new InvalidOperationException("读取编号正被运行中或正在恢复的账号占用，请先停止对应账号，再重新验证并保存硬件配置。");
        Processes.ValidateAccountUpdate(accounts);
        await SharedCleanupMigration.MigrateAsync(Options.AccountConfigPath, accounts, Options.ProfileLibraryDirectory,
            Options.BagCleanupNameListPath, cancellationToken).ConfigureAwait(false);
        var result = await Accounts.SaveAllAsync(accounts, cancellationToken).ConfigureAwait(false);
        if (!result.Success) throw new InvalidOperationException(result.Error);
        Processes.UpdateAccounts(accounts);
    }

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
        lock (_disposeSync) return new ValueTask(_disposeTask ??= Processes.DisposeAsync().AsTask());
    }

    private static bool HasPhysicalKey(string key) => !string.IsNullOrWhiteSpace(key) &&
        !key.Trim().StartsWith("auto", StringComparison.OrdinalIgnoreCase) && key.Trim() != "0";
}
