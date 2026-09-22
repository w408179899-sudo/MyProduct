using System.Text.Json;
using Roadhog.Core.Accounts;
using Roadhog.Core.Common;
using Roadhog.Core.Input;
using Roadhog.Infrastructure.Composition;
using Roadhog.Infrastructure.Input;

namespace Roadhog.Infrastructure.WorkerProcesses;

/// <summary>Process boundary seam. Test executables inject their own backend; production always uses real services.</summary>
public interface IWorkerProcessBackend : IAsyncDisposable
{
    Task InitializeAsync(CancellationToken cancellationToken);
    WorkerStatus GetStatus();
    Task<OperationResult> StartAsync(AccountConfig account, bool cleanupFirst, CancellationToken cancellationToken);
    Task<OperationResult> StopAsync(CancellationToken cancellationToken);
    Task<OperationResult> ReleaseInputAsync(CancellationToken cancellationToken) => Task.FromResult(OperationResult.Ok());
    Task<OperationResult<HardwareVerification>> VerifyHardwareAsync(CancellationToken cancellationToken);
    Task<object?> InvokeAsync(string method, JsonElement[] arguments, IProgress<string> progress, CancellationToken cancellationToken);
}

/// <summary>Runtime sees only its launch account. Shared account files are written by the console.</summary>
public sealed class WorkerAccountConfigStore : IAccountConfigStore
{
    private AccountConfig _account;
    public WorkerAccountConfigStore(AccountConfig account) => _account = account.Clone();
    public void Update(AccountConfig account) => Volatile.Write(ref _account, account.Clone());
    public Task<OperationResult<IReadOnlyList<AccountConfig>>> LoadAllAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(OperationResult<IReadOnlyList<AccountConfig>>.Ok(new[] { Volatile.Read(ref _account).Clone() }));
    }
    public Task<OperationResult> SaveAllAsync(IReadOnlyList<AccountConfig> accounts, CancellationToken cancellationToken = default) =>
        Task.FromResult(OperationResult.Fail("后台进程不能修改共享账号配置。"));
    public Task<OperationResult> UpsertAsync(AccountConfig account, CancellationToken cancellationToken = default) =>
        Task.FromResult(OperationResult.Fail("后台进程不能修改共享账号配置。"));
}

internal sealed class RoadhogWorkerProcessBackend : IWorkerProcessBackend
{
    private readonly WorkerLaunchSpec _spec;
    private readonly RoadhogServices _services;
    private readonly RuntimeRpcDispatcher _dispatcher;
    private readonly FileSystemWatcher? _radarWatcher;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<uint, byte> _knownMaps = new();
    private readonly object _stopSync = new();
    private Task<IReadOnlyDictionary<string, OperationResult>>? _pendingStop;
    public RoadhogWorkerProcessBackend(WorkerLaunchSpec spec)
    {
        _spec = spec;
        var options = CreateOptions(spec);
        _services = RoadhogServices.Create(options);
        _dispatcher = new RuntimeRpcDispatcher(_services.Runtime, spec.Account.AccountName, spec.Account.VmmDeviceName);
        if (!string.IsNullOrWhiteSpace(spec.Paths.RadarMapDirectory))
        {
            Directory.CreateDirectory(spec.Paths.RadarMapDirectory);
            foreach (var file in Directory.EnumerateFiles(spec.Paths.RadarMapDirectory, "*.json"))
                if (uint.TryParse(Path.GetFileNameWithoutExtension(file), out var id)) _knownMaps.TryAdd(id, 0);
            _radarWatcher = new FileSystemWatcher(spec.Paths.RadarMapDirectory, "*.json")
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size
            };
            _radarWatcher.Changed += RadarFileChanged;
            _radarWatcher.Created += RadarFileChanged;
            _radarWatcher.Deleted += RadarFileChanged;
            _radarWatcher.Renamed += (_, args) => { InvalidateMap(args.OldFullPath); InvalidateMap(args.FullPath); };
            _radarWatcher.Error += (_, _) =>
            {
                try { foreach (var file in Directory.EnumerateFiles(spec.Paths.RadarMapDirectory, "*.json")) InvalidateMap(file); }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
                foreach (var mapId in _knownMaps.Keys) _services.Runtime.NotifyRadarMapSaved(mapId);
            };
            _radarWatcher.EnableRaisingEvents = true;
        }
    }

    internal static RoadhogServiceOptions CreateOptions(WorkerLaunchSpec spec)
    {
        var paths = spec.Paths;
        var options = new RoadhogServiceOptions
        {
            AccountOverride = spec.Account.Clone(),
            AccountConfigPath = paths.AccountConfigPath,
            PathLibraryDirectory = paths.PathLibraryDirectory,
            ProfileLibraryDirectory = paths.ProfileLibraryDirectory,
            RadarMapDirectory = paths.RadarMapDirectory,
            BagCleanupNameListPath = string.IsNullOrWhiteSpace(paths.BagCleanupNameListPath) ? null : paths.BagCleanupNameListPath,
            LogDirectory = paths.LogDirectory,
            LicenseCredentialPath = paths.LicenseCredentialPath,
            OwnerLicenseGrantPath = paths.OwnerLicenseGrantPath,
            EnableLogging = paths.EnableLogging,
            LicenseHeartbeatInterval = paths.LicenseHeartbeatInterval,
            LicenseHeartbeatRetryCount = paths.LicenseHeartbeatRetryCount,
            LicenseHeartbeatRetryDelay = paths.LicenseHeartbeatRetryDelay,
            LicenseRequestTimeout = paths.LicenseRequestTimeout,
            AccountWorkerTickInterval = paths.AccountWorkerTickInterval,
            AccountWorkerStopTimeout = paths.AccountWorkerStopTimeout,
            PollPlayerSnapshotInWorker = paths.PollPlayerSnapshotInWorker
        };
        if (!string.IsNullOrWhiteSpace(paths.LicenseServerUrl)) options.LicenseServerUrl = paths.LicenseServerUrl;
        Roadhog.Infrastructure.Hardware.SavedHardwareBindingPolicy.ConfigureResolver(options.HardwareResolver, spec.Account);
        return options;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken) =>
        await _services.LicenseCoordinator.InitializeAsync(cancellationToken).ConfigureAwait(false);

    public WorkerStatus GetStatus()
    {
        var state = _services.AccountOrchestrator.Snapshot().FirstOrDefault(s =>
            string.Equals(s.AccountName, _spec.Account.AccountName, StringComparison.OrdinalIgnoreCase));
        var license = _services.LicenseCoordinator.State;
        return new WorkerStatus
        {
            InstanceId = _spec.Account.InstanceId,
            ProcessId = Environment.ProcessId,
            Authorized = license.IsAuthorized,
            AuthorizationError = license.ErrorCode ?? (license.IsAuthorized ? null : license.Kind.ToString()),
            IsRunning = state?.Status is "starting" or "running" or "stopping",
            Snapshot = state
        };
    }

    public async Task<OperationResult> StartAsync(AccountConfig account, bool cleanupFirst, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var hardware = ValidatePhysicalBinding();
        if (!hardware.Success) return hardware;
        lock (_stopSync)
        {
            if (_pendingStop is { IsCompleted: false }) return OperationResult.Fail("账号仍在停止中。");
            _pendingStop = null;
        }
        if (_services.AccountConfigStore is WorkerAccountConfigStore store) store.Update(account);
        OperationResult StartBusiness() => cleanupFirst
            ? _services.AccountOrchestrator.RequestCleanup(account)
            : _services.AccountOrchestrator.Start(account);
        // Cleanup on an already running worker keeps its existing verified session.
        if (GetStatus().IsRunning) return StartBusiness();
        return await AccountIdentityStartGuard.RunAsync(_spec.Account,
            token => _services.Runtime.ReadPlayerAsync(_spec.Account.AccountName, token), StartBusiness, cancellationToken).ConfigureAwait(false);
    }

    public async Task<OperationResult> StopAsync(CancellationToken cancellationToken)
    {
        var results = await BeginStop().ConfigureAwait(false);
        var release = await ReleaseInputAsync(cancellationToken).ConfigureAwait(false);
        return results.Values.FirstOrDefault(result => !result.Success) ?? release;
    }

    private Task<IReadOnlyDictionary<string, OperationResult>> BeginStop()
    {
        lock (_stopSync) return _pendingStop ??= _services.AccountOrchestrator.StopAllAsync();
    }

    public async Task<OperationResult> ReleaseInputAsync(CancellationToken cancellationToken)
    {
        // Request worker cancellation synchronously before releasing input, even if a DMA call will not return.
        _ = BeginStop();
        if (_services.KeyboardInput is KmBoxNetKeyboardInput input)
        {
            var connected = await input.ConnectAsync(cancellationToken).ConfigureAwait(false);
            if (!connected.Success) return connected;
        }
        return _services.KeyboardInput is IInputStateReset reset
            ? await reset.ReleaseAllAsync(cancellationToken).ConfigureAwait(false)
            : OperationResult.Ok();
    }

    public async Task<OperationResult<HardwareVerification>> VerifyHardwareAsync(CancellationToken cancellationToken)
    {
        var account = _spec.Account;
        var sessionId = Roadhog.Infrastructure.Hardware.HardwareVerificationSession.CurrentId;
        if (string.IsNullOrWhiteSpace(sessionId))
            return OperationResult<HardwareVerification>.Fail("无法建立本次开机的验证记录，请检查当前用户的注册表访问权限并重新打开客户端。");
        var binding = ValidatePhysicalBinding();
        if (!binding.Success) return OperationResult<HardwareVerification>.Fail(binding.Error ?? "DMA 设备绑定无效。");
        var player = await _services.Runtime.ReadPlayerAsync(account.AccountName, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(player.CharacterName) || player.EntityId == 0)
            return OperationResult<HardwareVerification>.Fail("尚未读取到真实角色，请先进入游戏。");
        if (_services.KeyboardInput is not KmBoxNetKeyboardInput input)
            return OperationResult<HardwareVerification>.Fail("KMBox 输入设备不可用。");
        var connect = await input.ConnectAsync(cancellationToken).ConfigureAwait(false);
        return connect.Success
            ? OperationResult<HardwareVerification>.Ok(new(player.CharacterName, account.HardwareKey, account.VmmDeviceName, true, sessionId))
            : OperationResult<HardwareVerification>.Fail(connect.Error ?? "KMBox 握手失败。");
    }

    private OperationResult ValidatePhysicalBinding() =>
        Roadhog.Infrastructure.Hardware.SavedHardwareBindingPolicy.Validate(_spec.Account, _services.HardwareResolver);

    public Task<object?> InvokeAsync(string method, JsonElement[] arguments, IProgress<string> progress, CancellationToken cancellationToken) =>
        _dispatcher.InvokeAsync(method, arguments, progress, cancellationToken);

    public async ValueTask DisposeAsync()
    {
        _radarWatcher?.Dispose();
        try { await StopAsync(CancellationToken.None).ConfigureAwait(false); }
        finally { _services.Dispose(); }
    }

    private void RadarFileChanged(object sender, FileSystemEventArgs args) => InvalidateMap(args.FullPath);
    private void InvalidateMap(string path)
    {
        if (uint.TryParse(Path.GetFileNameWithoutExtension(path), out var mapId))
        {
            _knownMaps.TryAdd(mapId, 0);
            _services.Runtime.NotifyRadarMapSaved(mapId);
        }
    }
}
