using Roadhog.Core.Diagnostics;
using Roadhog.Core.Accounts;

namespace Roadhog.Application;

public sealed class AccountRuntimeManager
{
    private readonly Dictionary<string, AccountRuntimeState> _accounts = new(StringComparer.OrdinalIgnoreCase);
    private readonly IRoadhogLogger _logger;
    private readonly object _syncRoot = new();

    public AccountRuntimeManager(IRoadhogLogger logger)
    {
        _logger = logger;
    }

    public IReadOnlyList<AccountRuntimeSnapshot> Snapshot()
    {
        lock (_syncRoot)
        {
            return _accounts.Values.Select(ToSnapshot).ToArray();
        }
    }

    public AccountRuntimeState GetOrCreate(string accountName, string characterName = "")
    {
        lock (_syncRoot)
        {
            if (!_accounts.TryGetValue(accountName, out var state))
            {
                state = new AccountRuntimeState(accountName, characterName);
                _accounts[accountName] = state;
                _logger.Info("account.created", new Dictionary<string, object?> { ["account"] = accountName });
            }
            else if (!string.IsNullOrWhiteSpace(characterName))
            {
                state.ApplyAccountInfo(characterName, ToConfigSnapshot(state));
            }

            return state;
        }
    }

    public void MarkStarting(AccountConfig config)
    {
        lock (_syncRoot)
        {
            var state = GetOrCreate(config.AccountName, config.CharacterName);
            state.MarkStarting(ToConfigSnapshot(config));
            _logger.Info("account.starting", new Dictionary<string, object?>
            {
                ["account"] = config.AccountName,
                ["character"] = config.CharacterName,
                ["pid"] = config.ProcessId,
                ["processName"] = config.TargetProcessName,
                ["hardwareKey"] = config.HardwareKey,
                ["hardwareKind"] = config.HardwareBindingKind,
                ["hardwareConfidence"] = config.HardwareBindingConfidence,
                ["vmmDevice"] = config.VmmDeviceName
            });
        }
    }

    public void MarkRunning(string accountName, int threadId)
    {
        lock (_syncRoot)
        {
            var state = GetOrCreate(accountName);
            state.MarkRunning(threadId);
            _logger.Info("account.running", new Dictionary<string, object?> { ["account"] = accountName, ["threadId"] = threadId });
        }
    }

    public void MarkHeartbeat(string accountName)
    {
        lock (_syncRoot)
        {
            var state = GetOrCreate(accountName);
            state.MarkHeartbeat();
        }
    }

    public void MarkKill(string accountName)
    {
        lock (_syncRoot)
        {
            var state = GetOrCreate(accountName);
            state.MarkKill();
        }
    }

    public void RequestStop(string accountName)
    {
        lock (_syncRoot)
        {
            var state = GetOrCreate(accountName);
            state.RequestStop();
            _logger.Info("account.stop_requested", new Dictionary<string, object?> { ["account"] = accountName });
        }
    }

    public void MarkStopped(string accountName)
    {
        lock (_syncRoot)
        {
            var state = GetOrCreate(accountName);
            state.MarkStopped();
            _logger.Info("account.stopped", new Dictionary<string, object?> { ["account"] = accountName });
        }
    }

    public void MarkFailed(string accountName, string error)
    {
        lock (_syncRoot)
        {
            var state = GetOrCreate(accountName);
            state.MarkFailed(error);
            _logger.Warn("account.failed", new Dictionary<string, object?> { ["account"] = accountName, ["error"] = error });
        }
    }

    private static AccountRuntimeSnapshot ToSnapshot(AccountRuntimeState state)
    {
        return new AccountRuntimeSnapshot(
            state.AccountName,
            state.CharacterName,
            state.ProcessId,
            state.TargetProcessName,
            state.HardwareKey,
            state.HardwareBindingKind,
            state.HardwareBindingConfidence,
            state.HardwareDeviceInstanceId,
            state.HardwareLocationKey,
            state.HardwareDisplayName,
            state.VmmDeviceName,
            state.Status,
            state.ThreadId,
            state.StopRequested,
            state.CreatedAt,
            state.UpdatedAt,
            state.StartedAt,
            state.StoppedAt,
            state.LastHeartbeatAt,
            state.LastError,
            state.KillCount,
            state.LastKillAt);
    }

    private static AccountRuntimeState.AccountConfigSnapshot ToConfigSnapshot(AccountConfig config)
    {
        return new AccountRuntimeState.AccountConfigSnapshot(
            config.CharacterName,
            config.ProcessId,
            config.TargetProcessName,
            config.HardwareKey,
            config.HardwareBindingKind,
            config.HardwareBindingConfidence,
            config.HardwareDeviceInstanceId,
            config.HardwareLocationKey,
            config.HardwareDisplayName,
            config.VmmDeviceName);
    }

    private static AccountRuntimeState.AccountConfigSnapshot ToConfigSnapshot(AccountRuntimeState state)
    {
        return new AccountRuntimeState.AccountConfigSnapshot(
            state.CharacterName,
            state.ProcessId,
            state.TargetProcessName,
            state.HardwareKey,
            state.HardwareBindingKind,
            state.HardwareBindingConfidence,
            state.HardwareDeviceInstanceId,
            state.HardwareLocationKey,
            state.HardwareDisplayName,
            state.VmmDeviceName);
    }
}
