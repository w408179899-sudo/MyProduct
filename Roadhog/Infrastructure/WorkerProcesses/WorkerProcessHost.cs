using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Roadhog.Core.Accounts;
using Roadhog.Core.Common;
using Roadhog.Infrastructure.Hardware;

namespace Roadhog.Infrastructure.WorkerProcesses;

public sealed class WorkerProcessHost
{
    private readonly Func<WorkerLaunchSpec, IWorkerProcessBackend> _backendFactory;
    private readonly Action<int> _terminateProcess;
    private readonly TimeSpan? _shutdownTimeout;

    public WorkerProcessHost(Func<WorkerLaunchSpec, IWorkerProcessBackend>? backendFactory = null,
        Action<int>? terminateProcess = null, TimeSpan? shutdownTimeout = null)
    {
        _backendFactory = backendFactory ?? (spec => new RoadhogWorkerProcessBackend(spec));
        _terminateProcess = terminateProcess ?? Environment.Exit;
        _shutdownTimeout = shutdownTimeout;
    }

    public async Task<int> RunAsync(WorkerLaunchSpec launch, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(launch);
        try
        {
            if (!string.IsNullOrWhiteSpace(launch.ManifestPath)) File.Delete(launch.ManifestPath + ".error");
            return await RunCoreAsync(launch, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            WriteStartupError(launch.ManifestPath, exception.Message);
            return 12;
        }
    }

    private async Task<int> RunCoreAsync(WorkerLaunchSpec launch, CancellationToken cancellationToken)
    {
        Validate(launch);
        var spec = launch with { Account = launch.Account.Clone() };
        using var process = Process.GetCurrentProcess();
        var startedAt = new DateTimeOffset(process.StartTime.ToUniversalTime());
        using var ownership = new WorkerMutex(spec.Paths.ClientRoot, spec.Account.InstanceId);
        if (!ownership.Acquired)
        {
            WriteStartupError(spec.ManifestPath, "同一账号后台已经运行，不能重复启动。");
            return 10;
        }
        var address = System.Net.IPAddress.Parse(spec.Account.KmBox!.IpAddress.Trim());
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        var endpoint = address + ":" + spec.Account.KmBox.Port.ToString(System.Globalization.CultureInfo.InvariantCulture);
        using var endpointOwnership = new WorkerMutex("Roadhog.KmBox.Endpoint", endpoint, globalScope: true);
        if (!endpointOwnership.Acquired)
        {
            WriteStartupError(spec.ManifestPath, "KMBox 地址已被其他账号后台占用：" + endpoint);
            return 13;
        }
        var mac = new string(spec.Account.KmBox.Mac.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
        using var macOwnership = new WorkerMutex("Roadhog.KmBox.Mac", mac, globalScope: true);
        if (!macOwnership.Acquired)
        {
            WriteStartupError(spec.ManifestPath, "KMBox MAC 已被其他账号后台占用：" + spec.Account.KmBox.Mac);
            return 14;
        }
        var leases = new DeviceLeaseStore(string.IsNullOrWhiteSpace(spec.LeasePath) ? null : spec.LeasePath);
        var acquired = leases.TryAcquire(Environment.ProcessId, startedAt, spec.Paths.ClientRoot,
            spec.Account.HardwareKey, spec.Account.VmmDeviceName);
        if (!acquired.Success)
        {
            WriteStartupError(spec.ManifestPath, acquired.Conflict is { } conflict
                ? $"DMA 设备已被其他后台占用（进程 {conflict.ProcessId}，设备 {conflict.VmmDeviceName}）。"
                : acquired.Error ?? "无法取得 DMA 设备租约。");
            return 11;
        }
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        IWorkerProcessBackend? backend = null;
        WorkerCommandsSession? commands = null;
        Task? initialization = null;
        try
        {
            // Publish a controllable process identity before constructing any provider/native dependency.
            backend = new DeferredWorkerProcessBackend(spec, _backendFactory);
            commands = new WorkerCommandsSession(spec, backend, lifetime, _terminateProcess, _shutdownTimeout);
            var server = new WorkerRpcServer(spec.PipeName, spec.Token, commands.HandleAsync);
            var serving = server.RunAsync(lifetime.Token);
            WriteManifest(spec.ManifestPath, new WorkerDescriptor
            {
                InstanceId = spec.Account.InstanceId,
                AccountName = spec.Account.AccountName,
                PipeName = spec.PipeName,
                Token = spec.Token,
                ProcessId = Environment.ProcessId,
                ProcessStartedAtUtc = startedAt
            });
            // Authorization can take seconds. Status and shutdown remain available throughout initialization.
            initialization = commands.InitializeAsync(lifetime.Token);
            await serving.ConfigureAwait(false);
            return 0;
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { return 0; }
        finally
        {
            lifetime.Cancel();
            try
            {
                if (backend is not null)
                {
                    using var releaseDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                    try { await backend.ReleaseInputAsync(releaseDeadline.Token).ConfigureAwait(false); } catch { }
                }
                if (commands is not null) await commands.DrainAsync().ConfigureAwait(false);
                if (initialization is not null)
                    try { await initialization.ConfigureAwait(false); } catch { }
                if (backend is not null) await backend.DisposeAsync().ConfigureAwait(false);
            }
            finally
            {
                commands?.Dispose();
                leases.Release(Environment.ProcessId, startedAt);
                RemoveOwnManifest(spec.ManifestPath, Environment.ProcessId, startedAt);
            }
        }
    }

    private static void Validate(WorkerLaunchSpec spec)
    {
        if (spec.ProtocolVersion != 1) throw new InvalidDataException("不支持的后台进程协议版本。");
        if (!spec.Account.Validate(out var error)) throw new InvalidDataException(error);
        if (string.IsNullOrWhiteSpace(spec.Account.InstanceId) || string.IsNullOrWhiteSpace(spec.Account.HardwareKey)
            || string.IsNullOrWhiteSpace(spec.Account.VmmDeviceName) || string.IsNullOrWhiteSpace(spec.PipeName)
            || string.IsNullOrWhiteSpace(spec.Token) || string.IsNullOrWhiteSpace(spec.ManifestPath)
            || string.IsNullOrWhiteSpace(spec.Paths.ClientRoot))
            throw new InvalidDataException("后台启动配置缺少账号、设备或通信标识。");
        if (spec.Account.KmBox is null) throw new InvalidDataException("尚未配置该账号的 KMBox。");
        if (!spec.Account.KmBox.Validate(out error)) throw new InvalidDataException(error);
        if (spec.Account.HardwareKey.Trim().ToLowerInvariant() is "0" or "auto" or "automatic")
            throw new InvalidDataException("后台账号必须绑定明确的物理 DMA 设备，不能使用自动绑定。");
        if (string.Equals(spec.Account.VmmDeviceName.Trim(), "fpga", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("请先保存明确的 DMA 设备编号（例如 fpga://devindex=0），再启动后台。");
        AccountResourcePath.ValidateLaunchOverride(spec.Account.LicenseCredentialPath, spec.Paths.LicenseCredentialPath, spec.Paths.AccountConfigPath, "授权凭据");
        AccountResourcePath.ValidateLaunchOverride(spec.Account.BagCleanupNameListPath, spec.Paths.BagCleanupNameListPath, spec.Paths.AccountConfigPath, "物品名单");
        AccountResourcePath.ValidateLaunchOverride(spec.Account.RadarMapDirectory, spec.Paths.RadarMapDirectory, spec.Paths.AccountConfigPath, "雷达地图");
        AccountResourcePath.ValidateLaunchOverride(spec.Account.OwnerLicenseGrantPath, spec.Paths.OwnerLicenseGrantPath, spec.Paths.AccountConfigPath, "本机授权");
    }

    private static void WriteManifest(string path, WorkerDescriptor descriptor)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temporary = path + "." + Environment.ProcessId + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(descriptor));
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static void WriteStartupError(string manifestPath, string error)
    {
        if (string.IsNullOrWhiteSpace(manifestPath)) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(manifestPath))!);
            File.WriteAllText(manifestPath + ".error", error);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException) { }
    }

    private static void RemoveOwnManifest(string path, int processId, DateTimeOffset startedAt)
    {
        try
        {
            if (!File.Exists(path)) return;
            var descriptor = JsonSerializer.Deserialize<WorkerDescriptor>(File.ReadAllText(path));
            if (descriptor?.ProcessId == processId && descriptor.ProcessStartedAtUtc == startedAt) File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException) { }
    }

    private sealed class WorkerCommandsSession : IDisposable
    {
        private readonly WorkerLaunchSpec _spec;
        private readonly IWorkerProcessBackend _backend;
        private readonly CancellationTokenSource _lifetime;
        private readonly Action<int> _terminateProcess;
        private readonly TimeSpan _shutdownTimeout;
        private readonly CancellationTokenSource _shutdownCompleted = new();
        private readonly SemaphoreSlim _actions = new(1, 1);
        private readonly object _sync = new();
        private readonly List<CancellationTokenSource> _retired = new();
        private CancellationTokenSource _manual = new();
        private bool _shuttingDown;
        private int _stopping;
        private volatile bool _initialized;
        private string? _initializationError;
        private Task<OperationResult>? _shutdownTask;

        public WorkerCommandsSession(WorkerLaunchSpec spec, IWorkerProcessBackend backend, CancellationTokenSource lifetime,
            Action<int> terminateProcess, TimeSpan? shutdownTimeout)
        {
            _spec = spec; _backend = backend; _lifetime = lifetime; _terminateProcess = terminateProcess;
            _shutdownTimeout = shutdownTimeout ?? TimeSpan.FromSeconds(Math.Clamp(spec.Paths.AccountWorkerStopTimeout.TotalSeconds + 5, 5, 120));
            if (_shutdownTimeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(shutdownTimeout));
        }

        public async Task InitializeAsync(CancellationToken cancellationToken)
        {
            try { await _backend.InitializeAsync(cancellationToken).ConfigureAwait(false); }
            catch (Exception exception) { _initializationError = exception.Message; }
            finally { _initialized = true; }
            if (_initializationError is not null) return;
            var wasAuthorized = _backend.GetStatus().Authorized;
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
                    var authorized = _backend.GetStatus().Authorized;
                    if (wasAuthorized && !authorized)
                    {
                        CancelManualOperations();
                        // Authorization loss must release input even while a native read owns the action gate.
                        try { await _backend.ReleaseInputAsync(cancellationToken).ConfigureAwait(false); }
                        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
                        { _initializationError = "授权失效后释放输入失败：" + exception.Message; }
                        await _actions.WaitAsync(cancellationToken).ConfigureAwait(false);
                        try { await _backend.StopAsync(cancellationToken).ConfigureAwait(false); }
                        finally { _actions.Release(); }
                    }
                    wasAuthorized = authorized;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        }

        public async Task<object?> HandleAsync(string method, JsonElement[] arguments, IProgress<string> progress, CancellationToken cancellationToken)
        {
            if (method == WorkerCommands.Status)
            {
                var status = _backend.GetStatus();
                return status with
                {
                    InstanceId = _spec.Account.InstanceId,
                    ProcessId = Environment.ProcessId,
                    InitializationComplete = _initialized,
                    Authorized = _initialized && _initializationError is null && status.Authorized,
                    AuthorizationError = _initializationError ?? (!_initialized ? "正在验证授权" : status.AuthorizationError),
                    ReportedAtUtc = DateTimeOffset.UtcNow
                };
            }
            if (method == WorkerCommands.Shutdown)
            {
                Task<OperationResult> shutdown;
                lock (_sync)
                {
                    _shuttingDown = true;
                    // Once accepted, shutdown survives loss of its UI/client connection.
                    shutdown = _shutdownTask ??= Task.Run(ShutdownAsync, CancellationToken.None);
                }
                return await shutdown.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            if (method == WorkerCommands.Stop)
            {
                lock (_sync)
                {
                    _stopping++;
                    CancelManualOperations();
                }
                try
                {
                    // Input release is independent of a long or hung DMA operation holding the action gate.
                    try { await _backend.ReleaseInputAsync(cancellationToken).ConfigureAwait(false); }
                    catch (Exception) when (!cancellationToken.IsCancellationRequested) { }
                    await _actions.WaitAsync(cancellationToken).ConfigureAwait(false);
                    try
                    {
                        var result = await _backend.StopAsync(cancellationToken).ConfigureAwait(false);
                        return result;
                    }
                    finally { _actions.Release(); }
                }
                finally { lock (_sync) _stopping--; }
            }

            CancellationToken manualToken;
            lock (_sync)
            {
                if (_shuttingDown || _stopping > 0) throw new InvalidOperationException("账号后台正在停止或退出。");
                manualToken = _manual.Token;
            }
            using var operation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, manualToken, _lifetime.Token);
            await _actions.WaitAsync(operation.Token).ConfigureAwait(false);
            try
            {
                if (!_initialized || _initializationError is not null || !_backend.GetStatus().Authorized)
                    throw new InvalidOperationException(_initializationError ?? "账号授权尚未通过，请先完成激活。");
                if (method is WorkerCommands.Start or WorkerCommands.Cleanup)
                {
                    if (arguments.Length != 1) throw new InvalidDataException("启动请求缺少账号配置。");
                    var requested = arguments[0].Deserialize<AccountConfig>(WorkerRpcProtocol.Json)
                        ?? throw new InvalidDataException("账号配置为空。");
                    EnsureSameAccount(requested);
                    var effective = requested.Clone();
                    effective.ProcessId = _spec.Account.ProcessId;
                    effective.TargetProcessName = _spec.Account.TargetProcessName;
                    effective.HardwareBindingKind = _spec.Account.HardwareBindingKind;
                    effective.HardwareBindingConfidence = _spec.Account.HardwareBindingConfidence;
                    effective.HardwareDeviceInstanceId = _spec.Account.HardwareDeviceInstanceId;
                    effective.HardwareLocationKey = _spec.Account.HardwareLocationKey;
                    effective.HardwareDisplayName = _spec.Account.HardwareDisplayName;
                    return await _backend.StartAsync(effective, method == WorkerCommands.Cleanup, operation.Token).ConfigureAwait(false);
                }
                if (method == WorkerCommands.VerifyHardware)
                    return await _backend.VerifyHardwareAsync(operation.Token).ConfigureAwait(false);
                return await _backend.InvokeAsync(method, arguments, progress, operation.Token).ConfigureAwait(false);
            }
            finally { _actions.Release(); }
        }

        private void EnsureSameAccount(AccountConfig requested)
        {
            var original = _spec.Account;
            if (!requested.Validate(out var error)) throw new InvalidDataException(error);
            if (!string.Equals(requested.InstanceId, original.InstanceId, StringComparison.Ordinal)
                || !string.Equals(requested.AccountName, original.AccountName, StringComparison.Ordinal)
                || !string.Equals(requested.HardwareKey, original.HardwareKey, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(DeviceLeaseStore.CanonicalVmmDeviceName(requested.VmmDeviceName), DeviceLeaseStore.CanonicalVmmDeviceName(original.VmmDeviceName), StringComparison.OrdinalIgnoreCase)
                || requested.KmBox is null || original.KmBox is null
                || !string.Equals(requested.KmBox.IpAddress, original.KmBox.IpAddress, StringComparison.OrdinalIgnoreCase)
                || requested.KmBox.Port != original.KmBox.Port
                || !string.Equals(requested.KmBox.Mac, original.KmBox.Mac, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(requested.LicenseCredentialPath, original.LicenseCredentialPath, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(requested.BagCleanupNameListPath, original.BagCleanupNameListPath, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(requested.RadarMapDirectory, original.RadarMapDirectory, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(requested.OwnerLicenseGrantPath, original.OwnerLicenseGrantPath, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("账号身份、硬件绑定、资料目录或授权来源已更改，请关闭旧后台后重新启动。");
        }

        private void CancelManualOperations()
        {
            lock (_sync)
            {
                var previous = _manual;
                _manual = new();
                _retired.Add(previous);
                previous.Cancel();
            }
        }

        private async Task<OperationResult> ShutdownAsync()
        {
            _ = EnforceShutdownDeadlineAsync();
            CancelManualOperations();
            try
            {
                try { await _backend.ReleaseInputAsync(_lifetime.Token).ConfigureAwait(false); }
                catch (Exception) when (!_lifetime.IsCancellationRequested) { }
                await _actions.WaitAsync(_lifetime.Token).ConfigureAwait(false);
                try { return await _backend.StopAsync(_lifetime.Token).ConfigureAwait(false); }
                finally { _actions.Release(); }
            }
            catch (Exception exception) { return OperationResult.Fail("后台停止失败：" + exception.Message); }
            finally
            {
                // Leave time for a connected caller's reply; disconnected callers cannot cancel shutdown.
                _ = Task.Run(async () =>
                {
                    await Task.Delay(250).ConfigureAwait(false);
                    try { _lifetime.Cancel(); } catch (ObjectDisposedException) { }
                });
            }
        }

        private async Task EnforceShutdownDeadlineAsync()
        {
            try
            {
                await Task.Delay(_shutdownTimeout, _shutdownCompleted.Token).ConfigureAwait(false);
                // A stuck provider constructor/read/dispose cannot leave an unmanageable orphan worker.
                _terminateProcess(15);
            }
            catch (OperationCanceledException) when (_shutdownCompleted.IsCancellationRequested) { }
        }

        public void Dispose()
        {
            _shutdownCompleted.Cancel();
            _shutdownCompleted.Dispose();
            _manual.Cancel();
            _manual.Dispose();
            foreach (var source in _retired) source.Dispose();
            // A vendor call may outlive a disconnected RPC; its finally still releases this semaphore.
        }

        public async Task DrainAsync()
        {
            lock (_sync) { _shuttingDown = true; _manual.Cancel(); }
            // Never release the hardware lease while an in-flight native action still owns the device.
            // A hung vendor call is recovered by the parent terminating this worker process.
            await _actions.WaitAsync().ConfigureAwait(false);
            _actions.Release();
        }
    }

    private sealed class WorkerMutex : IDisposable
    {
        private readonly ManualResetEventSlim _ready = new();
        private readonly ManualResetEventSlim _release = new();
        private readonly Thread _thread;
        private Exception? _failure;
        public bool Acquired { get; private set; }
        public WorkerMutex(string root, string instanceId, bool globalScope = false)
        {
            var scope = globalScope ? root : Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
            var key = scope.ToUpperInvariant() + "|" + instanceId.ToUpperInvariant();
            var name = @"Local\Roadhog.Worker." + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
            _thread = new Thread(() =>
            {
                try
                {
                    using var mutex = new Mutex(false, name);
                    try { Acquired = mutex.WaitOne(0); }
                    catch (AbandonedMutexException) { Acquired = true; }
                    _ready.Set();
                    if (!Acquired) return;
                    try { _release.Wait(); }
                    finally { mutex.ReleaseMutex(); }
                }
                catch (Exception exception) { _failure = exception; }
                finally { _ready.Set(); }
            }) { IsBackground = true, Name = "Roadhog account ownership" };
            _thread.Start();
            _ready.Wait();
            if (_failure is not null) throw new InvalidOperationException("无法取得账号后台独占锁。", _failure);
        }
        public void Dispose()
        {
            _release.Set();
            _thread.Join();
            _ready.Dispose();
            _release.Dispose();
        }
    }
}
