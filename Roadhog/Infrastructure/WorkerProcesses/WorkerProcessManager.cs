using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Roadhog.Application;
using Roadhog.Core.Accounts;
using Roadhog.Core.Common;
using Roadhog.Core.Diagnostics;
using Roadhog.Infrastructure.Composition;
using Roadhog.Infrastructure.Hardware;

namespace Roadhog.Infrastructure.WorkerProcesses;

public sealed record AccountProcessView(AccountConfig Config, string State, string? Error,
    WorkerStatus? Worker, bool DesiredRunning, int? WorkerProcessId, string LogDirectory);

public sealed record WorkerProcessLaunchOptions
{
    public string ExecutablePath { get; init; } = Path.ChangeExtension(typeof(WorkerProcessManager).Assembly.Location, ".exe");
    public IReadOnlyList<string> PrefixArguments { get; init; } = Array.Empty<string>();
    public TimeSpan StartupTimeout { get; init; } = TimeSpan.FromSeconds(35);
    public TimeSpan StopTimeout { get; init; } = TimeSpan.FromSeconds(12);
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(1);
    public TimeSpan RecoveryDelay { get; init; } = TimeSpan.FromSeconds(3);
    public string? LeasePath { get; init; }
}

/// <summary>Owns processes and user intent. Game logic and hardware stay in the child process.</summary>
public sealed class WorkerProcessManager : IAsyncDisposable
{
    private sealed class Entry(AccountConfig config)
    {
        public readonly object Sync = new();
        public readonly SemaphoreSlim Gate = new(1, 1);
        public AccountConfig Config = config.Clone();
        public WorkerDescriptor? Descriptor;
        public Process? Process;
        public WorkerStatus? Status;
        public bool Desired;
        public string State = "stopped";
        public string? Error;
        public CancellationTokenSource Operation = new();
        public DateTimeOffset RetryAt;
        public int Failures;
        public int PollFailures;
        public DateTimeOffset? RunningSince;
        public bool StartingRequest;
        public Task<OperationResult>? StopTask;
        public Task? PollTask;
        public readonly List<CancellationTokenSource> RetiredOperations = new();
        public readonly Dictionary<string, object?[]> Notifications = new(StringComparer.Ordinal);
    }

    private readonly object _sync = new();
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly RoadhogServiceOptions _paths;
    private readonly WorkerProcessLaunchOptions _launch;
    private readonly IRoadhogLogger _logger;
    private readonly string _root;
    private readonly SemaphoreSlim _intentGate = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private int _shuttingDown;
    private int _disposed;
    private Task? _disposeTask;
    private Task? _monitor;
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true, WriteIndented = true };

    public WorkerProcessManager(RoadhogServiceOptions paths, IRoadhogLogger logger, WorkerProcessLaunchOptions? launch = null)
    {
        _paths = paths;
        _logger = logger;
        _launch = launch ?? new();
        _root = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(paths.AccountConfigPath))!, "workers");
        Directory.CreateDirectory(_root);
    }

    public async Task InitializeAsync(IReadOnlyList<AccountConfig> accounts, CancellationToken cancellationToken = default)
    {
        ThrowIfUnavailable();
        UpdateAccounts(accounts);
        string[] desired = [];
        var intentPath = Path.Combine(_root, "run-intent.json");
        if (File.Exists(intentPath))
        {
            // A damaged intent file must not guess which hardware should be started.
            try { desired = JsonSerializer.Deserialize<string[]>(await File.ReadAllTextAsync(intentPath, cancellationToken), Json) ?? []; }
            catch (JsonException ex) { _logger.Error("worker.intent.invalid", ex); }
        }
        foreach (var entry in Entries())
        {
            entry.Desired = desired.Contains(entry.Config.InstanceId, StringComparer.OrdinalIgnoreCase)
                && HardwareVerificationSession.IsCurrent(entry.Config);
        }
        await Task.WhenAll(Entries().Select(async entry =>
        {
            await entry.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await TryAdoptAsync(entry, waitForPending: false, cancellationToken).ConfigureAwait(false);
                if (!HardwareVerificationSession.IsCurrent(entry.Config))
                {
                    if (Alive(entry)) await ShutdownCoreAsync(entry).ConfigureAwait(false);
                    lock (entry.Sync) { entry.Desired = false; entry.State = "verification_required"; entry.Error = HardwareVerificationSession.RequiredMessage; }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception ex) { Fail(entry, ex.Message); }
            finally { entry.Gate.Release(); }
        })).ConfigureAwait(false);
        if (desired.Any(id => !Entries().Any(entry => entry.Config.InstanceId.Equals(id, StringComparison.OrdinalIgnoreCase) && entry.Desired)))
        {
            try { await SaveIntentAsync().ConfigureAwait(false); }
            catch (Exception exception) { _logger.Error("worker.intent.reset_after_boot_failed", exception); }
        }
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfUnavailable();
        _monitor ??= Task.Run(MonitorAsync);
    }

    public void UpdateAccounts(IReadOnlyList<AccountConfig> accounts)
    {
        if (Volatile.Read(ref _disposed) != 0) throw new ObjectDisposedException(nameof(WorkerProcessManager));
        ValidateAccountUpdate(accounts);
        lock (_sync)
        {
            foreach (var config in accounts)
            {
                if (!Guid.TryParse(config.InstanceId, out _)) throw new ArgumentException("账号缺少有效的实例标识。");
                if (_entries.TryGetValue(config.InstanceId, out var entry))
                {
                    lock (entry.Sync)
                    {
                        entry.Config = config.Clone();
                        if (entry.State == "verification_required" && HardwareVerificationSession.IsCurrent(config))
                        { entry.State = "stopped"; entry.Error = null; }
                    }
                }
                else _entries.Add(config.InstanceId, new Entry(config));
            }
            foreach (var key in _entries.Keys.Except(accounts.Select(a => a.InstanceId), StringComparer.OrdinalIgnoreCase).ToArray())
            {
                var entry = _entries[key];
                if (Alive(entry) || entry.Desired) throw new InvalidOperationException("请先停止账号，再删除配置。");
                _entries.Remove(key);
            }
        }
    }

    public void ValidateAccountUpdate(IReadOnlyList<AccountConfig> accounts)
    {
        lock (_sync)
        {
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var config in accounts)
            {
                if (!Guid.TryParse(config.InstanceId, out _) || !ids.Add(config.InstanceId)) throw new InvalidOperationException("账号实例标识无效或重复。");
                if (_entries.TryGetValue(config.InstanceId, out var entry) && (entry.Desired || Alive(entry)) &&
                    (entry.Config.AccountName != config.AccountName || entry.Config.HardwareKey != config.HardwareKey ||
                     entry.Config.VmmDeviceName != config.VmmDeviceName || entry.Config.LicenseCredentialPath != config.LicenseCredentialPath ||
                     entry.Config.BagCleanupNameListPath != config.BagCleanupNameListPath || entry.Config.RadarMapDirectory != config.RadarMapDirectory ||
                     entry.Config.OwnerLicenseGrantPath != config.OwnerLicenseGrantPath ||
                     entry.Config.HardwareVerificationSessionId != config.HardwareVerificationSessionId || entry.Config.CharacterName != config.CharacterName ||
                     entry.Config.KmBox?.IpAddress != config.KmBox?.IpAddress || entry.Config.KmBox?.Port != config.KmBox?.Port || entry.Config.KmBox?.Mac != config.KmBox?.Mac))
                    throw new InvalidOperationException("请先停止账号，再修改名称、硬件绑定、资料目录或授权。");
            }
            foreach (var pair in _entries)
                if (!ids.Contains(pair.Key) && (pair.Value.Desired || Alive(pair.Value))) throw new InvalidOperationException("请先停止账号，再删除配置。");
        }
    }

    public IReadOnlyList<AccountProcessView> Snapshot() => Entries().Select(entry =>
    {
        lock (entry.Sync) return new AccountProcessView(entry.Config.Clone(), entry.State, entry.Error,
            entry.Status, entry.Desired, ProcessId(entry), PathsFor(entry.Config).LogDirectory);
    }).ToArray();

    public IRoadhogRuntime RuntimeFor(string instanceId)
    {
        var entry = Get(instanceId);
        var client = new WorkerRpcClient(ct => GetClientAsync(entry, ct));
        return new RemoteRoadhogRuntime(client, entry.Config.AccountName, (method, arguments) =>
        {
            // Saved settings already live on disk. Notifications only refresh an existing runtime.
            lock (entry.Sync) if (Alive(entry)) entry.Notifications[method] = arguments;
        });
    }

    public async Task<HardwareVerification> VerifyHardwareAsync(AccountConfig draft, CancellationToken cancellationToken)
    {
        var entry = Get(draft.InstanceId);
        if (Alive(entry)) throw new InvalidOperationException("请先停止此账号，再验证新的硬件绑定。");
        var old = entry.Config;
        lock (entry.Sync) entry.Config = draft.Clone();
        try
        {
            var client = await GetClientAsync(entry, cancellationToken).ConfigureAwait(false);
            var result = await client.CallAsync<OperationResult<HardwareVerification>>(WorkerCommands.VerifyHardware, [], cancellationToken).ConfigureAwait(false);
            if (!result.Success || result.Value is null || string.IsNullOrWhiteSpace(result.Value.CharacterName) ||
                !result.Value.KmBoxConnected || string.IsNullOrWhiteSpace(result.Value.SessionId) || result.Value.SessionId != HardwareVerificationSession.CurrentId ||
                !string.Equals(result.Value.HardwareKey, draft.HardwareKey, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(result.Value.VmmDeviceName, draft.VmmDeviceName, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(result.Error ?? "硬件验证结果不完整或与所选设备不匹配。");
            return result.Value;
        }
        finally
        {
            var stopped = await StopAsync(draft.InstanceId).ConfigureAwait(false);
            lock (entry.Sync) entry.Config = old;
            if (!stopped.Success) throw new InvalidOperationException("验证结束后无法释放账号后台：" + stopped.Error);
        }
    }

    public async Task<OperationResult> StartAsync(string instanceId, bool cleanup = false, CancellationToken cancellationToken = default)
    {
        if (Volatile.Read(ref _shuttingDown) != 0 || Volatile.Read(ref _disposed) != 0) return OperationResult.Fail("主界面正在退出，不能启动账号。");
        var entry = Get(instanceId);
        if (!HardwareVerificationSession.IsCurrent(entry.Config))
        {
            lock (entry.Sync) { entry.Desired = false; entry.State = "verification_required"; entry.Error = HardwareVerificationSession.RequiredMessage; }
            return OperationResult.Fail(HardwareVerificationSession.RequiredMessage);
        }
        CancellationTokenSource operation;
        lock (entry.Sync)
        {
            if (Volatile.Read(ref _shuttingDown) != 0 || Volatile.Read(ref _disposed) != 0) return OperationResult.Fail("主界面正在退出，不能启动账号。");
            if (entry.State == "stopping" || entry.StopTask is { IsCompleted: false }) return OperationResult.Fail("账号正在停止，请稍后再启动。");
            if (entry.Desired && entry.State == "starting") return OperationResult.Ok();
            if (entry.Desired && Alive(entry) && entry.Status?.IsRunning == true && !cleanup) return OperationResult.Ok();
            entry.Desired = true;
            entry.Operation.Cancel();
            entry.RetiredOperations.Add(entry.Operation);
            entry.Operation = new();
            operation = entry.Operation;
            entry.StartingRequest = true;
            entry.State = "starting";
            entry.Error = null;
        }
        try
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, operation.Token, _lifetime.Token);
            try { await SaveIntentAsync().ConfigureAwait(false); }
            catch (Exception ex)
            {
                lock (entry.Sync) { entry.Desired = false; entry.Operation.Cancel(); }
                Fail(entry, "无法保存启动状态，未启动账号：" + ex.Message);
                return OperationResult.Fail(entry.Error!);
            }
            await entry.Gate.WaitAsync(linked.Token).ConfigureAwait(false);
            try
            {
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(linked.Token);
                deadline.CancelAfter(_launch.StartupTimeout + _launch.StopTimeout + TimeSpan.FromSeconds(5));
                return await StartCoreAsync(entry, cleanup, deadline.Token).ConfigureAwait(false);
            }
            finally { entry.Gate.Release(); }
        }
        catch (OperationCanceledException)
        {
            if (!operation.IsCancellationRequested && !cancellationToken.IsCancellationRequested && !_lifetime.IsCancellationRequested)
            {
                Fail(entry, "账号启动超时，已回收未完成初始化的后台。");
                return OperationResult.Fail(entry.Error!);
            }
            if (cancellationToken.IsCancellationRequested)
            {
                lock (entry.Sync) if (ReferenceEquals(entry.Operation, operation)) entry.Desired = false;
                try { await SaveIntentAsync().ConfigureAwait(false); } catch (Exception ex) { _logger.Error("account_process.cancel_intent_failed", ex); }
            }
            return OperationResult.Fail("账号启动已取消。");
        }
        catch (Exception ex) { Fail(entry, ex.Message); return OperationResult.Fail(ex.Message); }
        finally { lock (entry.Sync) if (ReferenceEquals(entry.Operation, operation)) entry.StartingRequest = false; }
    }

    private async Task<OperationResult> StartCoreAsync(Entry entry, bool cleanup, CancellationToken cancellationToken)
    {
        ThrowIfUnavailable();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(_launch.StartupTimeout);
        cancellationToken = deadline.Token;
        if (!entry.Desired) return OperationResult.Fail("账号已停止。");
        // Recovery may have acquired the gate while a manual start persisted its intent.
        if (!cleanup && Alive(entry) && entry.Status?.IsRunning == true) return OperationResult.Ok();
        var paths = PathsFor(entry.Config);
        var builder = new AccountStartConfigBuilder(new Roadhog.Infrastructure.Profiles.JsonScriptProfileStore(paths.ProfileLibraryDirectory),
            new Roadhog.Infrastructure.Config.JsonBagCleanupNameListStore(paths.BagCleanupNameListPath, logger: _logger), _logger,
            path => new Roadhog.Infrastructure.Config.JsonBagCleanupNameListStore(
                AccountResourcePath.Resolve(path, paths.BagCleanupNameListPath, _paths.AccountConfigPath), logger: _logger));
        var built = await builder.BuildAsync(entry.Config, cancellationToken).ConfigureAwait(false);
        if (!built.Success || built.Value is null)
        {
            Fail(entry, built.Error ?? "无法生成账号运行配置。");
            return OperationResult.Fail(entry.Error!);
        }
        var client = await EnsureWorkerAsync(entry, cancellationToken).ConfigureAwait(false);
        if (entry.Status is { InitializationComplete: true, Authorized: false } unavailable)
        {
            // A missing/rejected credential is an actionable setup failure, not a reason to spawn another process repeatedly.
            lock (entry.Sync) entry.Desired = false;
            await ShutdownCoreAsync(entry).ConfigureAwait(false);
            var error = unavailable.AuthorizationError ?? "账号授权尚未通过，请先完成授权。";
            try { await SaveIntentAsync().ConfigureAwait(false); }
            catch (Exception ex) { error += "；停止状态未能保存：" + ex.Message; }
            Fail(entry, error);
            return OperationResult.Fail(error);
        }
        try
        {
            var result = await client.CallAsync<OperationResult>(cleanup ? WorkerCommands.Cleanup : WorkerCommands.Start,
                [built.Value], cancellationToken).ConfigureAwait(false);
            if (!result.Success)
            {
                // An explicit rejection before any business started needs a user correction.
                // Keep crash/transport recovery separate from repeatedly retrying a rejected configuration.
                var error = result.Error ?? "启动失败";
                if (entry.Status?.IsRunning != true)
                {
                    lock (entry.Sync) entry.Desired = false;
                    await ShutdownCoreAsync(entry).ConfigureAwait(false);
                    try { await SaveIntentAsync().ConfigureAwait(false); }
                    catch (Exception ex) { error += "；停止状态未能保存：" + ex.Message; }
                }
                Fail(entry, error);
                return OperationResult.Fail(error);
            }
            var status = await client.CallAsync<WorkerStatus>(WorkerCommands.Status, [], cancellationToken).ConfigureAwait(false);
            lock (entry.Sync) { entry.Status = status; entry.State = status.IsRunning ? "running" : "starting"; entry.Error = null; entry.RunningSince ??= DateTimeOffset.UtcNow; }
            return result;
        }
        catch
        {
            await ShutdownCoreAsync(entry).ConfigureAwait(false);
            throw;
        }
    }

    public Task<OperationResult> StopAsync(string instanceId, CancellationToken cancellationToken = default)
    {
        if (Volatile.Read(ref _disposed) != 0) return Task.FromResult(OperationResult.Fail("账号管理器已经关闭。"));
        var entry = Get(instanceId);
        lock (entry.Sync)
        {
            if (entry.StopTask is { IsCompleted: false }) return entry.StopTask;
            entry.Desired = false;
            entry.Operation.Cancel();
            entry.State = "stopping";
            // Coalescing prevents an older queued Stop from killing a subsequent explicit restart.
            return entry.StopTask = Task.Run(() => StopCoreAsync(entry));
        }
    }

    private async Task<OperationResult> StopCoreAsync(Entry entry)
    {
        // Persist and stop independently: a full disk or a blocked writer must never delay input release or process termination.
        var persistence = Task.Run(SaveIntentAsync);
        string? stopError = null;
        // Explicit stop completes even when a view/form that requested it has closed.
        await entry.Gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (entry.Process is null)
            {
                using var discoverDeadline = new CancellationTokenSource(_launch.StartupTimeout);
                await TryAdoptAsync(entry, waitForPending: true, discoverDeadline.Token).ConfigureAwait(false);
            }
            await ShutdownCoreAsync(entry).ConfigureAwait(false);
        }
        catch (Exception ex) { stopError = "停止失败：" + ex.Message; }
        finally { entry.Gate.Release(); }
        try { await persistence.ConfigureAwait(false); }
        catch (Exception ex)
        {
            stopError = (stopError is null ? "账号已停止，但停止状态未能保存：" : stopError + "；停止状态未能保存：") + ex.Message;
            _logger.Error("account_process.stop_intent_failed", ex);
        }
        lock (entry.Sync) { entry.State = Alive(entry) ? "failed" : "stopped"; entry.Error = stopError; if (!Alive(entry)) entry.Status = null; }
        return stopError is null ? OperationResult.Ok() : OperationResult.Fail(stopError);
    }

    public async Task<IReadOnlyDictionary<string, OperationResult>> StopAllAsync()
    {
        var entries = Entries();
        var results = await Task.WhenAll(entries.Select(async entry =>
            new KeyValuePair<string, OperationResult>(entry.Config.AccountName, await StopAsync(entry.Config.InstanceId).ConfigureAwait(false)))).ConfigureAwait(false);
        return results.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
    }

    public void BeginShutdown()
    {
        if (Volatile.Read(ref _disposed) != 0) return;
        Interlocked.Exchange(ref _shuttingDown, 1);
        foreach (var entry in Entries())
            lock (entry.Sync) entry.Operation.Cancel();
    }

    public void CancelShutdown()
    {
        if (Volatile.Read(ref _disposed) == 0) Interlocked.Exchange(ref _shuttingDown, 0);
    }

    public async Task ForceStopAllAsync()
    {
        BeginShutdown();
        var entries = Entries();
        foreach (var entry in entries) lock (entry.Sync) entry.Desired = false;
        await Task.WhenAll(entries.Select(async entry =>
        {
            Process? process; WorkerDescriptor? descriptor;
            lock (entry.Sync) { process = entry.Process; descriptor = entry.Descriptor; }
            if (process is not null && descriptor is not null)
                await TerminateOwnedAsync(process, descriptor).ConfigureAwait(false);
        })).ConfigureAwait(false);
        await SaveIntentAsync().ConfigureAwait(false);
    }

    private void ThrowIfUnavailable()
    {
        if (Volatile.Read(ref _shuttingDown) != 0 || Volatile.Read(ref _disposed) != 0)
            throw new InvalidOperationException("主界面正在退出或已经关闭，不能连接或启动账号后台。");
    }

    private async Task<WorkerRpcClient> GetClientAsync(Entry entry, CancellationToken cancellationToken)
    {
        ThrowIfUnavailable();
        CancellationToken operation;
        lock (entry.Sync)
        {
            if (entry.State == "stopping") throw new OperationCanceledException("账号正在停止。");
            if (entry.Operation.IsCancellationRequested)
            {
                entry.RetiredOperations.Add(entry.Operation);
                entry.Operation = new();
            }
            operation = entry.Operation.Token;
        }
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, operation, _lifetime.Token);
        await entry.Gate.WaitAsync(linked.Token).ConfigureAwait(false);
        try { return await EnsureWorkerAsync(entry, linked.Token).ConfigureAwait(false); }
        finally { entry.Gate.Release(); }
    }

    private async Task<WorkerRpcClient> EnsureWorkerAsync(Entry entry, CancellationToken cancellationToken)
    {
        ThrowIfUnavailable();
        if (!Alive(entry)) await TryAdoptAsync(entry, waitForPending: true, cancellationToken).ConfigureAwait(false);
        if (Alive(entry) && entry.Descriptor is not null)
        {
            try
            {
                var ready = await WaitForReadyAsync(entry, cancellationToken).ConfigureAwait(false);
                lock (entry.Sync) entry.Status = ready;
            }
            catch { await ShutdownCoreAsync(entry).ConfigureAwait(false); throw; }
            return Client(entry);
        }
        ValidateHardware(entry.Config);
        foreach (var other in Entries().Where(e => !ReferenceEquals(e, entry) && e.Config.KmBox is not null && !string.IsNullOrWhiteSpace(e.Config.VmmDeviceName)))
        {
            var a = entry.Config; var b = other.Config;
            if (string.Equals(a.HardwareKey, b.HardwareKey, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(Hardware.DeviceLeaseStore.CanonicalVmmDeviceName(a.VmmDeviceName), Hardware.DeviceLeaseStore.CanonicalVmmDeviceName(b.VmmDeviceName), StringComparison.OrdinalIgnoreCase) ||
                string.Equals(a.KmBox!.IpAddress, b.KmBox!.IpAddress, StringComparison.OrdinalIgnoreCase) && a.KmBox.Port == b.KmBox.Port ||
                string.Equals(a.KmBox!.Mac.Replace(":", "").Replace("-", ""), b.KmBox!.Mac.Replace(":", "").Replace("-", ""), StringComparison.OrdinalIgnoreCase) ||
                string.Equals(Path.GetFullPath(PathsFor(a).LicenseCredentialPath), Path.GetFullPath(PathsFor(b).LicenseCredentialPath), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("账号与“" + b.AccountName + "”共用了设备或客户端授权，请先检查配置。");
        }
        var directory = InstanceDirectory(entry.Config.InstanceId);
        Directory.CreateDirectory(directory);
        var spec = new WorkerLaunchSpec
        {
            Account = entry.Config.Clone(), Paths = PathsFor(entry.Config),
            PipeName = "Roadhog.Account." + Guid.NewGuid().ToString("N"),
            Token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)),
            ManifestPath = Path.Combine(directory, "worker.json"), LeasePath = _launch.LeasePath ?? string.Empty
        };
        var launchPath = Path.Combine(directory, "launch-" + Guid.NewGuid().ToString("N") + ".json");
        await WriteJsonAsync(launchPath, spec, cancellationToken).ConfigureAwait(false);
        // Children consume an immutable launch file. The stable breadcrumb lets a new manager discover a worker even if its manifest was lost.
        await WriteJsonAsync(Path.Combine(directory, "launch.json"), spec, cancellationToken).ConfigureAwait(false);
        await WriteJsonAsync(Path.Combine(directory, "pending.json"), new PendingLaunch(spec.PipeName, spec.Token, DateTimeOffset.UtcNow), cancellationToken).ConfigureAwait(false);
        var start = new ProcessStartInfo(_launch.ExecutablePath)
        {
            UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
            WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(_launch.ExecutablePath))!
        };
        foreach (var arg in _launch.PrefixArguments) start.ArgumentList.Add(arg);
        start.ArgumentList.Add("--account-worker"); start.ArgumentList.Add(launchPath);
        try
        {
            lock (_sync)
            {
                ThrowIfUnavailable();
                cancellationToken.ThrowIfCancellationRequested();
                var process = Process.Start(start) ?? throw new InvalidOperationException("无法启动账号后台。");
                try
                {
                    _ = process.SafeHandle; // Retain the OS process object, not just a recyclable PID.
                    var descriptor = new WorkerDescriptor
                    {
                        InstanceId = entry.Config.InstanceId, AccountName = entry.Config.AccountName,
                        PipeName = spec.PipeName, Token = spec.Token, ProcessId = process.Id,
                        ProcessStartedAtUtc = process.StartTime.ToUniversalTime()
                    };
                    lock (entry.Sync) { entry.Process?.Dispose(); entry.Process = process; entry.Descriptor = descriptor; }
                }
                catch
                {
                    try { if (!process.HasExited) process.Kill(); } finally { process.Dispose(); }
                    throw;
                }
            }
            var status = await WaitForReadyAsync(entry, cancellationToken).ConfigureAwait(false);
            lock (entry.Sync) { entry.Status = status; if (!entry.Desired) entry.State = "idle"; }
            ClearPending(entry.Config.InstanceId, spec.Token);
            _logger.Info("account_process.started", new Dictionary<string, object?> { ["account"] = entry.Config.AccountName, ["pid"] = entry.Process!.Id });
            return Client(entry);
        }
        catch
        {
            // A responsive status pipe is not proof that initialization finished. Reclaim every failed startup.
            await ShutdownCoreAsync(entry).ConfigureAwait(false);
            ClearPending(entry.Config.InstanceId, spec.Token);
            try { File.Delete(launchPath); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            throw;
        }
    }

    private async Task<WorkerStatus> WaitForReadyAsync(Entry entry, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        deadline.CancelAfter(_launch.StartupTimeout);
        while (true)
        {
            deadline.Token.ThrowIfCancellationRequested();
            if (!Alive(entry))
            {
                var errorPath = Path.Combine(InstanceDirectory(entry.Config.InstanceId), "worker.json.error");
                var reason = File.Exists(errorPath) ? await File.ReadAllTextAsync(errorPath, deadline.Token).ConfigureAwait(false) : "账号后台在初始化时退出。";
                throw new InvalidOperationException(reason);
            }
            using var attempt = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token);
            attempt.CancelAfter(TimeSpan.FromMilliseconds(750));
            try
            {
                var status = await Client(entry).CallAsync<WorkerStatus>(WorkerCommands.Status, [], attempt.Token).ConfigureAwait(false);
                if (status.InstanceId != entry.Config.InstanceId || status.ProcessId != entry.Process!.Id)
                    throw new InvalidDataException("后台账号身份不匹配。");
                if (status.InitializationComplete) return status;
            }
            catch (OperationCanceledException) when (!deadline.IsCancellationRequested) { }
            catch (TimeoutException) when (!deadline.IsCancellationRequested) { }
            await Task.Delay(100, deadline.Token).ConfigureAwait(false);
        }
    }

    private async Task ShutdownCoreAsync(Entry entry)
    {
        if (!Alive(entry))
        {
            if (entry.Descriptor is { } exited) ClearPending(entry.Config.InstanceId, exited.Token);
            Forget(entry); return;
        }
        var process = entry.Process!;
        var descriptor = entry.Descriptor ?? throw new InvalidOperationException("后台缺少已验证的进程身份，拒绝强制结束。");
        using var timeout = new CancellationTokenSource(_launch.StopTimeout);
        try
        {
            EnsureOwnedProcess(process, descriptor);
            if (entry.Descriptor is not null)
            {
                var shutdown = await Client(entry).CallAsync<OperationResult>(WorkerCommands.Shutdown, [], timeout.Token).ConfigureAwait(false);
                if (!shutdown.Success) _logger.Warn("account_process.shutdown_warning", new Dictionary<string, object?>
                    { ["account"] = entry.Config.AccountName, ["error"] = shutdown.Error });
            }
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (System.ComponentModel.Win32Exception) when (HasExitedOrDisposed(process))
        {
            // The child can exit between HasExited and Windows reading its executable identity.
            // A completed exit already satisfies Stop; never reinterpret it as an unknown live PID.
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or InvalidOperationException or TimeoutException or WorkerRpcException)
        {
            _logger.Warn("account_process.stop_timeout", new Dictionary<string, object?> { ["account"] = entry.Config.AccountName, ["error"] = ex.Message });
            await TerminateOwnedAsync(process, descriptor).ConfigureAwait(false);
        }
        ClearPending(entry.Config.InstanceId, descriptor.Token);
        Forget(entry);
    }

    private void EnsureOwnedProcess(Process process, WorkerDescriptor descriptor)
    {
        _ = process.SafeHandle;
        if (process.HasExited) return;
        if (process.Id != descriptor.ProcessId || process.StartTime.ToUniversalTime() != descriptor.ProcessStartedAtUtc.UtcDateTime ||
            !string.Equals(process.MainModule?.FileName, Path.GetFullPath(_launch.ExecutablePath), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("后台进程身份已变化，拒绝结束未知进程。");
    }

    private async Task TerminateOwnedAsync(Process process, WorkerDescriptor descriptor)
    {
        try
        {
            EnsureOwnedProcess(process, descriptor);
            if (!process.HasExited) process.Kill();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (InvalidOperationException) when (HasExitedOrDisposed(process)) { }
        catch (System.ComponentModel.Win32Exception) when (HasExitedOrDisposed(process)) { }
    }

    private static bool HasExitedOrDisposed(Process process)
    {
        try { return process.HasExited; }
        catch (InvalidOperationException) { return true; }
        catch (System.ComponentModel.Win32Exception) { return false; }
    }

    private async Task MonitorAsync()
    {
        var active = new HashSet<Task>();
        try
        {
            while (!_lifetime.IsCancellationRequested)
            {
                active.RemoveWhere(task => task.IsCompleted);
                foreach (var entry in Entries())
                {
                    if (entry.PollTask is { IsCompleted: false }) continue;
                    entry.PollTask = Task.Run(async () =>
                    {
                        try { await PollAsync(entry).ConfigureAwait(false); }
                        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
                        catch (Exception ex) { Fail(entry, ex.Message); }
                    });
                    active.Add(entry.PollTask);
                }
                await Task.Delay(_launch.PollInterval, _lifetime.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        finally { await Task.WhenAll(active).ConfigureAwait(false); }
    }

    private async Task PollAsync(Entry entry)
    {
        if (Volatile.Read(ref _shuttingDown) != 0) return;
        if (!await entry.Gate.WaitAsync(0, _lifetime.Token).ConfigureAwait(false)) return;
        try
        {
            if (entry.StartingRequest) return;
            if (Alive(entry) && entry.Descriptor is not null)
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
                timeout.CancelAfter(TimeSpan.FromSeconds(3));
                try
                {
                    var status = await Client(entry).CallAsync<WorkerStatus>(WorkerCommands.Status, [], timeout.Token).ConfigureAwait(false);
                    if (status.InstanceId != entry.Config.InstanceId || status.ProcessId != entry.Process!.Id) throw new IOException("账号后台身份不匹配。");
                    lock (entry.Sync)
                    {
                        entry.Status = status; entry.PollFailures = 0;
                        if (status.IsRunning)
                        {
                            entry.State = "running"; entry.Error = null;
                            entry.RunningSince ??= DateTimeOffset.UtcNow;
                            if (DateTimeOffset.UtcNow - entry.RunningSince >= TimeSpan.FromMinutes(1)) entry.Failures = 0;
                        }
                        else if (!entry.Desired) entry.State = "idle";
                        else if (!status.Authorized) { entry.State = "failed"; entry.Error = status.AuthorizationError ?? "需要授权"; }
                    }
                    if (entry.Desired && entry.Config.AutoRecover && !status.IsRunning && status.Authorized && DateTimeOffset.UtcNow >= entry.RetryAt)
                    {
                        using var op = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token, entry.Operation.Token);
                        await StartCoreAsync(entry, false, op.Token).ConfigureAwait(false);
                    }
                    await FlushNotificationsAsync(entry, timeout.Token).ConfigureAwait(false);
                    return;
                }
                catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { return; }
                catch (Exception ex)
                {
                    entry.Error = "后台暂未响应：" + ex.Message;
                    // IPC liveness is distinct from the game loop heartbeat; long game actions are not killed.
                    if (++entry.PollFailures < 3) return;
                    await ShutdownCoreAsync(entry).ConfigureAwait(false);
                    Fail(entry, "后台失去响应，已结束此账号进程。");
                }
            }
            if (!Alive(entry))
            {
                var unexpectedlyExited = entry.Process is not null && entry.Desired;
                Forget(entry);
                if (!entry.Desired) return;
                if (unexpectedlyExited) Fail(entry, "账号后台异常退出，等待恢复。");
                if (!entry.Config.AutoRecover) { entry.State = "failed"; entry.Error ??= "后台已退出，请手动重试。"; return; }
                if (DateTimeOffset.UtcNow < entry.RetryAt) return;
                entry.State = "recovering";
                using var op = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token, entry.Operation.Token);
                await StartCoreAsync(entry, false, op.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            if (!_lifetime.IsCancellationRequested && !entry.Operation.IsCancellationRequested)
                Fail(entry, "后台初始化或启动超时，已回收并等待重试。");
        }
        catch (Exception ex) { Fail(entry, ex.Message); }
        finally { entry.Gate.Release(); }
    }

    private async Task FlushNotificationsAsync(Entry entry, CancellationToken cancellationToken)
    {
        KeyValuePair<string, object?[]>[] pending;
        lock (entry.Sync) pending = entry.Notifications.ToArray();
        foreach (var notification in pending)
        {
            try
            {
                await Client(entry).CallAsync<object?>(notification.Key, notification.Value, cancellationToken).ConfigureAwait(false);
                lock (entry.Sync)
                    if (entry.Notifications.TryGetValue(notification.Key, out var current) && ReferenceEquals(current, notification.Value)) entry.Notifications.Remove(notification.Key);
            }
            catch (Exception ex) when (ex is IOException or OperationCanceledException or WorkerRpcException)
            {
                // A long manual operation can defer a settings notification; it is not an IPC health failure.
                _logger.Warn("account_process.notification_deferred", new Dictionary<string, object?> { ["account"] = entry.Config.AccountName, ["method"] = notification.Key, ["error"] = ex.Message });
                return;
            }
        }
    }

    private void Fail(Entry entry, string error)
    {
        lock (entry.Sync)
        {
            entry.State = entry.Desired && entry.Config.AutoRecover ? "recovering" : "failed";
            entry.Error = error;
            entry.RetryAt = DateTimeOffset.UtcNow + TimeSpan.FromMilliseconds(Math.Min(30000, _launch.RecoveryDelay.TotalMilliseconds * Math.Pow(2, Math.Min(entry.Failures++, 5))));
        }
        _logger.Warn("account_process.failed", new Dictionary<string, object?> { ["account"] = entry.Config.AccountName, ["error"] = error });
    }

    private sealed record PendingLaunch(string PipeName, string Token, DateTimeOffset CreatedAtUtc);

    private async Task TryAdoptAsync(Entry entry, bool waitForPending, CancellationToken cancellationToken)
    {
        var directory = InstanceDirectory(entry.Config.InstanceId);
        var candidates = new List<WorkerDescriptor>();
        var manifest = ReadWorkerFile<WorkerDescriptor>(Path.Combine(directory, "worker.json"));
        if (manifest is not null && manifest.ProtocolVersion == 1 && manifest.InstanceId == entry.Config.InstanceId &&
            string.Equals(manifest.AccountName, entry.Config.AccountName, StringComparison.OrdinalIgnoreCase)) candidates.Add(manifest);
        var launch = ReadWorkerFile<WorkerLaunchSpec>(Path.Combine(directory, "launch.json"));
        if (launch?.Account is not null && launch.ProtocolVersion == 1 && launch.Account.InstanceId == entry.Config.InstanceId &&
            string.Equals(launch.Account.AccountName, entry.Config.AccountName, StringComparison.OrdinalIgnoreCase) &&
            candidates.All(c => c.PipeName != launch.PipeName || c.Token != launch.Token))
            candidates.Add(new WorkerDescriptor { InstanceId = entry.Config.InstanceId, AccountName = entry.Config.AccountName,
                PipeName = launch.PipeName, Token = launch.Token });
        var pending = ReadWorkerFile<PendingLaunch>(Path.Combine(directory, "pending.json"));
        string? identityError = null;
        do
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var candidate in candidates)
            {
                if (string.IsNullOrWhiteSpace(candidate.PipeName) || string.IsNullOrWhiteSpace(candidate.Token)) continue;
                Process? process = null;
                using var attempt = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                attempt.CancelAfter(TimeSpan.FromMilliseconds(500));
                try
                {
                    var status = await new WorkerRpcClient(candidate.PipeName, candidate.Token)
                        .CallAsync<WorkerStatus>(WorkerCommands.Status, [], attempt.Token).ConfigureAwait(false);
                    if (status.InstanceId != entry.Config.InstanceId || status.ProcessId <= 0 ||
                        candidate.ProcessId > 0 && status.ProcessId != candidate.ProcessId)
                        throw new InvalidDataException("后台身份与账号记录不符，拒绝接管或结束该进程。");
                    process = Process.GetProcessById(status.ProcessId);
                    _ = process.SafeHandle;
                    var started = process.StartTime.ToUniversalTime();
                    if (process.HasExited || candidate.ProcessId > 0 && started != candidate.ProcessStartedAtUtc.UtcDateTime ||
                        !string.Equals(process.MainModule?.FileName, Path.GetFullPath(_launch.ExecutablePath), StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("后台程序路径或启动时间不符，拒绝接管或结束该进程。");
                    var verified = candidate with { ProcessId = status.ProcessId, ProcessStartedAtUtc = started };
                    lock (entry.Sync)
                    {
                        entry.Process?.Dispose(); entry.Process = process; process = null;
                        entry.Descriptor = verified; entry.Status = status;
                        entry.State = status.IsRunning ? "running" : entry.Desired ? "starting" : "idle";
                    }
                    ClearPending(entry.Config.InstanceId, verified.Token);
                    return;
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { }
                catch (WorkerRpcException ex) { identityError = ex.Message; }
                catch (InvalidDataException ex) { identityError = ex.Message; }
                catch (Exception ex) when (ex is IOException or TimeoutException or ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
                { _logger.Warn("account_process.attach_skipped", new Dictionary<string, object?> { ["account"] = entry.Config.AccountName, ["error"] = ex.Message }); }
                finally { process?.Dispose(); }
            }
            if (!waitForPending || pending is null || DateTimeOffset.UtcNow >= pending.CreatedAtUtc + _launch.StartupTimeout) break;
            await Task.Delay(100, cancellationToken).ConfigureAwait(false);
        } while (true);
        if (identityError is not null)
            throw new InvalidOperationException("无法确认账号后台身份，未操作任何未知进程：" + identityError);
        // A live PID in a file is only a hint; never treat it as permission to kill without the authenticated identity response.
        if (manifest is not null && IsRecordedProcessAlive(manifest))
            throw new InvalidOperationException("后台记录指向仍存活的进程，但身份通信未通过验证；已拒绝自动接管或结束它。");
    }

    private T? ReadWorkerFile<T>(string path) where T : class
    {
        try
        {
            if (!File.Exists(path)) return null;
            using var stream = Infrastructure.Config.AtomicJsonFile.OpenRead(path);
            return JsonSerializer.Deserialize<T>(stream, Json);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        { _logger.Warn("account_process.record_unreadable", new Dictionary<string, object?> { ["file"] = Path.GetFileName(path), ["error"] = ex.Message }); return null; }
    }

    private static bool IsRecordedProcessAlive(WorkerDescriptor descriptor)
    {
        try
        {
            using var process = Process.GetProcessById(descriptor.ProcessId);
            return !process.HasExited && process.StartTime.ToUniversalTime() == descriptor.ProcessStartedAtUtc.UtcDateTime;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { return false; }
    }

    private void ClearPending(string instanceId, string token)
    {
        var path = Path.Combine(InstanceDirectory(instanceId), "pending.json");
        try
        {
            if (ReadWorkerFile<PendingLaunch>(path)?.Token == token) File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { _logger.Warn("account_process.pending_cleanup_failed", new Dictionary<string, object?> { ["error"] = ex.Message }); }
    }

    public WorkerServicePaths PathsFor(AccountConfig account)
    {
        var configDirectory = Path.GetDirectoryName(Path.GetFullPath(_paths.AccountConfigPath))!;
        return new WorkerServicePaths
        {
            ClientRoot = Path.GetDirectoryName(configDirectory)!, AccountConfigPath = _paths.AccountConfigPath,
            PathLibraryDirectory = _paths.PathLibraryDirectory, ProfileLibraryDirectory = _paths.ProfileLibraryDirectory,
            RadarMapDirectory = AccountResourcePath.Resolve(account.RadarMapDirectory, _paths.RadarMapDirectory, _paths.AccountConfigPath),
            BagCleanupNameListPath = AccountResourcePath.Resolve(account.BagCleanupNameListPath,
                _paths.BagCleanupNameListPath ?? Path.Combine(configDirectory, "bag-cleanup-name-lists.json"), _paths.AccountConfigPath),
            LogDirectory = Path.Combine(_paths.LogDirectory, "accounts", account.InstanceId),
            LicenseCredentialPath = AccountResourcePath.Resolve(account.LicenseCredentialPath,
                Path.Combine(InstanceDirectory(account.InstanceId), "license.dat"), _paths.AccountConfigPath),
            OwnerLicenseGrantPath = AccountResourcePath.Resolve(account.OwnerLicenseGrantPath, _paths.OwnerLicenseGrantPath, _paths.AccountConfigPath),
            LicenseServerUrl = _paths.LicenseServerUrl,
            EnableLogging = _paths.EnableLogging,
            LicenseHeartbeatInterval = _paths.LicenseHeartbeatInterval,
            LicenseHeartbeatRetryCount = _paths.LicenseHeartbeatRetryCount,
            LicenseHeartbeatRetryDelay = _paths.LicenseHeartbeatRetryDelay,
            LicenseRequestTimeout = _paths.LicenseRequestTimeout,
            AccountWorkerTickInterval = _paths.AccountWorkerTickInterval,
            AccountWorkerStopTimeout = _paths.AccountWorkerStopTimeout,
            PollPlayerSnapshotInWorker = _paths.PollPlayerSnapshotInWorker
        };
    }

    private async Task SaveIntentAsync()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await _intentGate.WaitAsync(deadline.Token).ConfigureAwait(false);
        try
        {
            var writing = WriteJsonAsync(Path.Combine(_root, "run-intent.json"), Entries().Where(e => e.Desired).Select(e => e.Config.InstanceId).ToArray(), deadline.Token);
            _ = writing.ContinueWith(failed => _ = failed.Exception, CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            await writing.WaitAsync(deadline.Token).ConfigureAwait(false);
        }
        finally { _intentGate.Release(); }
    }

    private static Task WriteJsonAsync<T>(string path, T value, CancellationToken cancellationToken) =>
        Infrastructure.Config.AtomicJsonFile.WriteAsync(path, value, Json, cancellationToken);

    private string InstanceDirectory(string id) => Path.Combine(_root, Guid.Parse(id).ToString("N"));
    private Entry Get(string id) { lock (_sync) return _entries.TryGetValue(id, out var entry) ? entry : throw new KeyNotFoundException("账号不存在。"); }
    private Entry[] Entries() { lock (_sync) return _entries.Values.ToArray(); }
    private static WorkerRpcClient Client(Entry entry) => new(entry.Descriptor!.PipeName, entry.Descriptor.Token);
    private static bool Alive(Entry entry)
    {
        try { return entry.Process is { HasExited: false }; }
        catch (InvalidOperationException) { return false; }
        catch (System.ComponentModel.Win32Exception) { return entry.Process is not null; }
    }
    private static int? ProcessId(Entry entry)
    {
        try { var process = entry.Process; return process is { HasExited: false } ? process.Id : null; }
        catch (InvalidOperationException) { return null; }
        catch (System.ComponentModel.Win32Exception) { return entry.Descriptor?.ProcessId; }
    }
    private static void Forget(Entry entry)
    {
        lock (entry.Sync) { entry.Process?.Dispose(); entry.Process = null; entry.Descriptor = null; entry.Status = null; entry.RunningSince = null; entry.PollFailures = 0; }
    }
    private static void ValidateHardware(AccountConfig config)
    {
        if (string.IsNullOrWhiteSpace(config.HardwareKey) || config.HardwareKey.StartsWith("auto", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(config.VmmDeviceName) || config.KmBox is null)
            throw new InvalidOperationException("请先配置并验证此账号的 DMA 与 KMBox。");
    }

    /// <summary>Detach the manager. Explicit application exit calls StopAllAsync first; a UI crash must not stop workers.</summary>
    public ValueTask DisposeAsync()
    {
        lock (_sync)
        {
            Interlocked.Exchange(ref _disposed, 1);
            Interlocked.Exchange(ref _shuttingDown, 1);
            return new ValueTask(_disposeTask ??= Task.Run(DisposeCoreAsync));
        }
    }

    private async Task DisposeCoreAsync()
    {
        Interlocked.Exchange(ref _disposed, 1);
        Interlocked.Exchange(ref _shuttingDown, 1);
        _lifetime.Cancel();
        var entries = Entries();
        foreach (var entry in entries) lock (entry.Sync) entry.Operation.Cancel();
        if (_monitor is not null) await _monitor.ConfigureAwait(false);
        foreach (var entry in entries)
        {
            Task<OperationResult>? stopping;
            lock (entry.Sync) stopping = entry.StopTask;
            if (stopping is not null) await stopping.ConfigureAwait(false);
            await entry.Gate.WaitAsync().ConfigureAwait(false);
            try
            {
                lock (entry.Sync)
                {
                    entry.Process?.Dispose(); entry.Process = null;
                    entry.Operation.Dispose();
                    foreach (var retired in entry.RetiredOperations) retired.Dispose();
                    entry.RetiredOperations.Clear();
                }
            }
            finally { entry.Gate.Release(); }
        }
        _lifetime.Dispose();
    }
}
