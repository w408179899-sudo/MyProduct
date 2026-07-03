using Roadhog.Application.Workers;
using Roadhog.Core.Accounts;
using Roadhog.Core.Api;
using Roadhog.Core.Common;
using Roadhog.Core.Diagnostics;
using Roadhog.Core.Hardware;
using Roadhog.Core.Processes;

namespace Roadhog.Application;

public sealed class AccountOrchestrator
{
    private readonly Dictionary<string, AccountWorkerHost> _workers = new(StringComparer.OrdinalIgnoreCase);
    private readonly IRoadhogGameApi _gameApi;
    private readonly IRoadhogLogger _logger;
    private readonly AccountRuntimeManager _runtimeStates;
    private readonly IHardwareDeviceResolver _hardwareResolver;
    private readonly ITargetProcessResolver _processResolver;
    private readonly IAccountWorkerLoop _workerLoop;
    private readonly AccountWorkerOptions _workerOptions;
    private readonly Dictionary<string, string> _hardwareOwners = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _syncRoot = new();

    public AccountOrchestrator(
        IRoadhogGameApi gameApi,
        IRoadhogLogger logger,
        AccountRuntimeManager runtimeStates,
        IHardwareDeviceResolver hardwareResolver,
        ITargetProcessResolver processResolver,
        IAccountWorkerLoop workerLoop,
        AccountWorkerOptions workerOptions)
    {
        _gameApi = gameApi;
        _logger = logger;
        _runtimeStates = runtimeStates;
        _hardwareResolver = hardwareResolver;
        _processResolver = processResolver;
        _workerLoop = workerLoop;
        _workerOptions = workerOptions;
    }

    public OperationResult Start(AccountConfig config)
    {
        if (!config.Enabled)
        {
            return OperationResult.Fail("Account is disabled: " + config.AccountName);
        }

        var startConfig = config.Clone();
        var worker = GetOrCreateWorker(startConfig.AccountName);
        if (worker.IsRunning)
        {
            return OperationResult.Fail("Account worker is already running: " + startConfig.AccountName);
        }

        var hardwareResult = BindHardware(startConfig);
        if (!hardwareResult.Success || hardwareResult.Value is null)
        {
            return OperationResult.Fail(hardwareResult.Error ?? "Failed to bind hardware device.");
        }

        var hardwareBinding = PreferConfiguredVmmDeviceName(startConfig, hardwareResult.Value);
        ApplyHardwareBinding(startConfig, hardwareBinding);

        var reserveResult = ReserveHardware(startConfig.AccountName, hardwareBinding);
        if (!reserveResult.Success)
        {
            return reserveResult;
        }

        TryResolveRuntimeProcess(startConfig);

        var startResult = worker.Start(startConfig);
        if (!startResult.Success)
        {
            ReleaseHardware(startConfig.AccountName);
        }

        return startResult;
    }

    public async Task<OperationResult> StopAsync(string accountName)
    {
        AccountWorkerHost? worker;
        lock (_syncRoot)
        {
            if (!_workers.TryGetValue(accountName, out worker))
            {
                return OperationResult.Ok();
            }
        }

        var result = await worker.StopAsync().ConfigureAwait(false);
        if (result.Success)
        {
            ReleaseHardware(accountName);
        }

        return result;
    }

    public async Task<IReadOnlyDictionary<string, OperationResult>> StopAllAsync()
    {
        KeyValuePair<string, AccountWorkerHost>[] workers;
        lock (_syncRoot)
        {
            workers = _workers.ToArray();
        }

        var results = new Dictionary<string, OperationResult>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in workers)
        {
            var result = await pair.Value.StopAsync().ConfigureAwait(false);
            results[pair.Key] = result;
            if (result.Success)
            {
                ReleaseHardware(pair.Key);
            }
        }

        return results;
    }

    public IReadOnlyList<AccountRuntimeSnapshot> Snapshot()
    {
        return _runtimeStates.Snapshot();
    }

    private AccountWorkerHost GetOrCreateWorker(string accountName)
    {
        lock (_syncRoot)
        {
            if (!_workers.TryGetValue(accountName, out var worker))
            {
                worker = new AccountWorkerHost(_gameApi, _logger, _runtimeStates, _workerLoop, _workerOptions);
                _workers[accountName] = worker;
            }

            return worker;
        }
    }

    private OperationResult<HardwareBinding> BindHardware(AccountConfig config)
    {
        if (!IsAutoHardwareKey(config.HardwareKey))
        {
            return _hardwareResolver.BindByKey(config.AccountName, config.HardwareKey);
        }

        var devices = _hardwareResolver.ListDevices();
        if (devices.Count == 0)
        {
            return OperationResult<HardwareBinding>.Fail("Hardware device not found for auto binding.");
        }

        lock (_syncRoot)
        {
            foreach (var device in devices)
            {
                if (!_hardwareOwners.TryGetValue(device.BindingKey, out var owner)
                    || string.Equals(owner, config.AccountName, StringComparison.OrdinalIgnoreCase)
                    || !IsWorkerRunning(owner))
                {
                    return OperationResult<HardwareBinding>.Ok(CreateHardwareBinding(config.AccountName, device));
                }
            }
        }

        return OperationResult<HardwareBinding>.Fail("No free hardware device is available for account: " + config.AccountName);
    }

    private void TryResolveRuntimeProcess(AccountConfig config)
    {
        var processResult = config.ProcessId > 0
            ? _processResolver.BindByPid(config.AccountName, config.ProcessId, config.TargetProcessName)
            : _processResolver.TryAutoBind(config.AccountName, config.TargetProcessName);

        if (processResult.Success && processResult.Value is not null)
        {
            config.ProcessId = processResult.Value.ProcessId;
            config.TargetProcessName = processResult.Value.ProcessName;
            _logger.Info("account.process.resolved", new Dictionary<string, object?>
            {
                ["account"] = config.AccountName,
                ["pid"] = config.ProcessId,
                ["processName"] = config.TargetProcessName
            });
            return;
        }

        config.ProcessId = 0;
        _logger.Warn("account.process.resolve_skipped", new Dictionary<string, object?>
        {
            ["account"] = config.AccountName,
            ["error"] = processResult.Error
        });
    }

    private OperationResult ReserveHardware(string accountName, HardwareBinding binding)
    {
        lock (_syncRoot)
        {
            if (_hardwareOwners.TryGetValue(binding.BindingKey, out var owner)
                && !string.Equals(owner, accountName, StringComparison.OrdinalIgnoreCase))
            {
                if (_workers.TryGetValue(owner, out var ownerWorker) && ownerWorker.IsRunning)
                {
                    return OperationResult.Fail($"Hardware '{binding.BindingKey}' is already bound to account '{owner}'.");
                }

                _hardwareOwners.Remove(binding.BindingKey);
            }

            ReleaseHardwareLocked(accountName);
            _hardwareOwners[binding.BindingKey] = accountName;
            _logger.Info("account.hardware.bound", new Dictionary<string, object?>
            {
                ["account"] = accountName,
                ["hardwareKey"] = binding.BindingKey,
                ["hardwareKind"] = binding.BindingKind,
                ["hardwareConfidence"] = binding.BindingConfidence,
                ["deviceInstanceId"] = binding.DeviceInstanceId,
                ["vmmDevice"] = binding.VmmDeviceName
            });

            return OperationResult.Ok();
        }
    }

    private void ReleaseHardware(string accountName)
    {
        lock (_syncRoot)
        {
            ReleaseHardwareLocked(accountName);
        }
    }

    private void ReleaseHardwareLocked(string accountName)
    {
        var ownedHardware = _hardwareOwners
            .Where(pair => string.Equals(pair.Value, accountName, StringComparison.OrdinalIgnoreCase))
            .Select(pair => pair.Key)
            .ToArray();

        foreach (var hardwareKey in ownedHardware)
        {
            _hardwareOwners.Remove(hardwareKey);
            _logger.Info("account.hardware.unbound", new Dictionary<string, object?>
            {
                ["account"] = accountName,
                ["hardwareKey"] = hardwareKey
            });
        }
    }

    private static void ApplyHardwareBinding(AccountConfig config, HardwareBinding binding)
    {
        config.HardwareKey = binding.BindingKey;
        config.HardwareBindingKind = binding.BindingKind;
        config.HardwareBindingConfidence = binding.BindingConfidence;
        config.HardwareDeviceInstanceId = binding.DeviceInstanceId;
        config.HardwareLocationKey = binding.LocationKey;
        config.HardwareDisplayName = binding.DisplayName;
        config.VmmDeviceName = binding.VmmDeviceName;
    }

    private static HardwareBinding PreferConfiguredVmmDeviceName(AccountConfig config, HardwareBinding binding)
    {
        return IsDefaultVmmDeviceName(binding.VmmDeviceName) && !IsDefaultVmmDeviceName(config.VmmDeviceName)
            ? binding with { VmmDeviceName = config.VmmDeviceName.Trim() }
            : binding;
    }

    private bool IsWorkerRunning(string accountName)
    {
        return _workers.TryGetValue(accountName, out var worker) && worker.IsRunning;
    }

    private static HardwareBinding CreateHardwareBinding(string accountName, HardwareDeviceFeature device)
    {
        return new HardwareBinding(
            accountName,
            device.BindingKey,
            device.BindingKind,
            device.BindingConfidence,
            device.DeviceInstanceId,
            device.ParentInstanceId,
            device.ContainerId,
            device.HardwareId,
            device.LocationKey,
            device.DisplayName,
            device.Manufacturer,
            device.VmmDeviceName,
            device.AliasKeys,
            DateTimeOffset.Now);
    }

    private static bool IsAutoHardwareKey(string hardwareKey)
    {
        return string.IsNullOrWhiteSpace(hardwareKey)
            || string.Equals(hardwareKey.Trim(), "0", StringComparison.OrdinalIgnoreCase)
            || string.Equals(hardwareKey.Trim(), "auto", StringComparison.OrdinalIgnoreCase)
            || string.Equals(hardwareKey.Trim(), "automatic", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsDefaultVmmDeviceName(string vmmDeviceName)
    {
        return string.IsNullOrWhiteSpace(vmmDeviceName)
            || string.Equals(vmmDeviceName.Trim(), "fpga", StringComparison.OrdinalIgnoreCase);
    }
}
