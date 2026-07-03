using Roadhog.Core.Api;
using Roadhog.Core.Accounts;
using Roadhog.Core.Common;
using Roadhog.Core.Diagnostics;
using Roadhog.Core.Hardware;
using Roadhog.Core.Model;

namespace Roadhog.Application;

public sealed class RoadhogRuntime
{
    private readonly IRoadhogGameApi _gameApi;
    private readonly IRoadhogLogger _logger;
    private readonly IAccountConfigStore? _accountConfigStore;
    private readonly IHardwareDeviceResolver? _hardwareResolver;

    public RoadhogRuntime(
        IRoadhogGameApi gameApi,
        IRoadhogLogger logger,
        AccountRuntimeManager accounts,
        AccountOrchestrator orchestrator,
        IAccountConfigStore? accountConfigStore = null,
        IHardwareDeviceResolver? hardwareResolver = null)
    {
        _gameApi = gameApi;
        _logger = logger;
        _accountConfigStore = accountConfigStore;
        _hardwareResolver = hardwareResolver;
        Accounts = accounts;
        Orchestrator = orchestrator;
    }

    public AccountRuntimeManager Accounts { get; }

    public AccountOrchestrator Orchestrator { get; }

    public async Task<OperationResult<PlayerSnapshot>> ReadPlayerAsync(
        string? accountName = null,
        CancellationToken cancellationToken = default)
    {
        var result = await ReadPlayerSnapshotAsync(accountName, cancellationToken).ConfigureAwait(false);
        if (result.Success)
        {
            _logger.Info("player.refresh.ok", new Dictionary<string, object?>
            {
                ["account"] = accountName,
                ["hasPosition"] = result.Value?.Position is not null
            });
        }
        else
        {
            _logger.Warn("player.refresh.failed", new Dictionary<string, object?>
            {
                ["account"] = accountName,
                ["error"] = result.Error
            });
        }

        return result;
    }

    public async Task<OperationResult<PlayerAbnormalStatusSnapshot>> ReadPlayerAbnormalStatusesAsync(
        string? accountName = null,
        CancellationToken cancellationToken = default)
    {
        var result = await ReadPlayerAbnormalStatusSnapshotAsync(accountName, cancellationToken).ConfigureAwait(false);
        if (result.Success)
        {
            _logger.Info("player_abnormal.refresh.ok", new Dictionary<string, object?>
            {
                ["account"] = accountName,
                ["harmfulAbnormalCount"] = result.Value?.HarmfulAbnormalCount ?? 0,
                ["harmfulAbnormalSummary"] = result.Value?.HarmfulAbnormalSummary
            });
        }
        else
        {
            _logger.Warn("player_abnormal.refresh.failed", new Dictionary<string, object?>
            {
                ["account"] = accountName,
                ["error"] = result.Error
            });
        }

        return result;
    }

    public async Task<OperationResult<LockedTargetAbnormalStatusSnapshot>> ReadLockedTargetAbnormalStatusesAsync(
        string? accountName = null,
        CancellationToken cancellationToken = default)
    {
        var result = await ReadLockedTargetAbnormalStatusSnapshotAsync(accountName, cancellationToken).ConfigureAwait(false);
        if (result.Success)
        {
            _logger.Info("locked_target_abnormal.refresh.ok", new Dictionary<string, object?>
            {
                ["account"] = accountName,
                ["hasTarget"] = result.Value?.HasTarget ?? false,
                ["targetEntityId"] = result.Value?.Target.TargetEntityId ?? 0,
                ["abnormalStatusCount"] = result.Value?.AbnormalStatusCount ?? 0,
                ["physicalDebuffCount"] = result.Value?.PhysicalDebuffCount ?? 0
            });
        }
        else
        {
            _logger.Warn("locked_target_abnormal.refresh.failed", new Dictionary<string, object?>
            {
                ["account"] = accountName,
                ["error"] = result.Error
            });
        }

        return result;
    }

    public async Task<OperationResult<SummonedPetSnapshot>> ReadSummonedPetAsync(
        string? accountName = null,
        CancellationToken cancellationToken = default)
    {
        var result = await ReadSummonedPetSnapshotAsync(accountName, cancellationToken).ConfigureAwait(false);
        if (result.Success)
        {
            _logger.Info("summoned_pet.refresh.ok", new Dictionary<string, object?>
            {
                ["account"] = accountName,
                ["isSummoned"] = result.Value?.IsSummoned ?? false,
                ["serverObjectId"] = result.Value?.ServerObjectId ?? 0,
                ["templateId"] = result.Value?.NpcTemplateId ?? 0,
                ["name"] = result.Value?.Name
            });
        }
        else
        {
            _logger.Warn("summoned_pet.refresh.failed", new Dictionary<string, object?>
            {
                ["account"] = accountName,
                ["error"] = result.Error
            });
        }

        return result;
    }

    public async Task<OperationResult<SummonedPetRosterSnapshot>> ReadSummonedPetRosterAsync(
        string? accountName = null,
        CancellationToken cancellationToken = default)
    {
        var result = await ReadSummonedPetRosterSnapshotAsync(accountName, cancellationToken).ConfigureAwait(false);
        if (result.Success)
        {
            _logger.Info("summoned_pet_roster.refresh.ok", new Dictionary<string, object?>
            {
                ["account"] = accountName,
                ["localPetSummoned"] = result.Value?.LocalPlayerPet.IsSummoned ?? false,
                ["partyPetCount"] = result.Value?.PartyMemberPetCount ?? 0,
                ["visibleSummonedPetCount"] = result.Value?.VisibleSummonedPetCount ?? 0
            });
        }
        else
        {
            _logger.Warn("summoned_pet_roster.refresh.failed", new Dictionary<string, object?>
            {
                ["account"] = accountName,
                ["error"] = result.Error
            });
        }

        return result;
    }

    public async Task<OperationResult<IReadOnlyList<SkillSnapshot>>> RefreshSkillsAsync(
        string? accountName = null,
        CancellationToken cancellationToken = default)
    {
        var result = await ReadSkillsAsync(accountName, cancellationToken).ConfigureAwait(false);
        if (result.Success)
        {
            _logger.Info("skills.refresh.ok", new Dictionary<string, object?>
            {
                ["account"] = accountName,
                ["count"] = result.Value?.Count ?? 0
            });
        }
        else
        {
            _logger.Warn("skills.refresh.failed", new Dictionary<string, object?>
            {
                ["account"] = accountName,
                ["error"] = result.Error
            });
        }

        return result;
    }

    public async Task<OperationResult<IReadOnlyList<WorldObjectSnapshot>>> RefreshWorldObjectsAsync(
        string? accountName = null,
        CancellationToken cancellationToken = default)
    {
        var result = await ReadWorldObjectsAsync(accountName, cancellationToken).ConfigureAwait(false);
        if (result.Success)
        {
            _logger.Info("world_objects.refresh.ok", new Dictionary<string, object?>
            {
                ["account"] = accountName,
                ["count"] = result.Value?.Count ?? 0
            });
        }
        else
        {
            _logger.Warn("world_objects.refresh.failed", new Dictionary<string, object?>
            {
                ["account"] = accountName,
                ["error"] = result.Error
            });
        }

        return result;
    }

    private Task<OperationResult<IReadOnlyList<SkillSnapshot>>> ReadSkillsAsync(
        string? accountName,
        CancellationToken cancellationToken)
    {
        if (_gameApi is IRoadhogScopedGameApi scopedApi &&
            !string.IsNullOrWhiteSpace(accountName))
        {
            return scopedApi.ReadSkillsAsync(CreateReadContext(accountName), cancellationToken);
        }

        return _gameApi.ReadSkillsAsync(cancellationToken);
    }

    private Task<OperationResult<IReadOnlyList<WorldObjectSnapshot>>> ReadWorldObjectsAsync(
        string? accountName,
        CancellationToken cancellationToken)
    {
        if (_gameApi is IRoadhogScopedGameApi scopedApi &&
            !string.IsNullOrWhiteSpace(accountName))
        {
            return scopedApi.ReadWorldObjectsAsync(CreateReadContext(accountName), cancellationToken);
        }

        return _gameApi.ReadWorldObjectsAsync(cancellationToken);
    }

    private Task<OperationResult<PlayerSnapshot>> ReadPlayerSnapshotAsync(
        string? accountName,
        CancellationToken cancellationToken)
    {
        if (_gameApi is IRoadhogScopedGameApi scopedApi &&
            !string.IsNullOrWhiteSpace(accountName))
        {
            return scopedApi.ReadPlayerAsync(CreateReadContext(accountName), cancellationToken);
        }

        return _gameApi.ReadPlayerAsync(cancellationToken);
    }

    private Task<OperationResult<PlayerAbnormalStatusSnapshot>> ReadPlayerAbnormalStatusSnapshotAsync(
        string? accountName,
        CancellationToken cancellationToken)
    {
        if (_gameApi is IRoadhogScopedGameApi scopedApi &&
            !string.IsNullOrWhiteSpace(accountName))
        {
            return scopedApi.ReadPlayerAbnormalStatusesAsync(CreateReadContext(accountName), cancellationToken);
        }

        return _gameApi.ReadPlayerAbnormalStatusesAsync(cancellationToken);
    }

    private Task<OperationResult<LockedTargetAbnormalStatusSnapshot>> ReadLockedTargetAbnormalStatusSnapshotAsync(
        string? accountName,
        CancellationToken cancellationToken)
    {
        if (_gameApi is IRoadhogScopedGameApi scopedApi &&
            !string.IsNullOrWhiteSpace(accountName))
        {
            return scopedApi.ReadLockedTargetAbnormalStatusesAsync(CreateReadContext(accountName), cancellationToken);
        }

        return _gameApi.ReadLockedTargetAbnormalStatusesAsync(cancellationToken);
    }

    private Task<OperationResult<SummonedPetSnapshot>> ReadSummonedPetSnapshotAsync(
        string? accountName,
        CancellationToken cancellationToken)
    {
        if (_gameApi is IRoadhogScopedGameApi scopedApi &&
            !string.IsNullOrWhiteSpace(accountName))
        {
            return scopedApi.ReadSummonedPetAsync(CreateReadContext(accountName), cancellationToken);
        }

        return _gameApi.ReadSummonedPetAsync(cancellationToken);
    }

    private Task<OperationResult<SummonedPetRosterSnapshot>> ReadSummonedPetRosterSnapshotAsync(
        string? accountName,
        CancellationToken cancellationToken)
    {
        if (_gameApi is IRoadhogScopedGameApi scopedApi &&
            !string.IsNullOrWhiteSpace(accountName))
        {
            return scopedApi.ReadSummonedPetRosterAsync(CreateReadContext(accountName), cancellationToken);
        }

        return _gameApi.ReadSummonedPetRosterAsync(cancellationToken);
    }

    private GameApiReadContext CreateReadContext(string accountName)
    {
        var account = Accounts.Snapshot()
            .FirstOrDefault(item => string.Equals(item.AccountName, accountName, StringComparison.OrdinalIgnoreCase));

        if (account is not null)
        {
            return new GameApiReadContext(
                account.AccountName,
                account.ProcessId,
                account.TargetProcessName,
                account.VmmDeviceName);
        }

        var config = LoadSavedAccountConfig(accountName);
        return config is null
            ? new GameApiReadContext(accountName, 0, string.Empty, string.Empty)
            : new GameApiReadContext(
                config.AccountName,
                config.ProcessId,
                config.TargetProcessName,
                config.VmmDeviceName);
    }

    private AccountConfig? LoadSavedAccountConfig(string accountName)
    {
        if (_accountConfigStore is null)
        {
            return null;
        }

        var result = _accountConfigStore.LoadAllAsync().GetAwaiter().GetResult();
        if (!result.Success || result.Value is null)
        {
            _logger.Warn("account_config.read_context.load_failed", new Dictionary<string, object?>
            {
                ["account"] = accountName,
                ["error"] = result.Error
            });
            return null;
        }

        var config = result.Value
            .FirstOrDefault(item => string.Equals(item.AccountName, accountName, StringComparison.OrdinalIgnoreCase))
            ?.Clone();
        if (config is not null)
        {
            config.VmmDeviceName = ResolveCurrentVmmDeviceName(config);
        }

        return config;
    }

    private string ResolveCurrentVmmDeviceName(AccountConfig config)
    {
        if (_hardwareResolver is null ||
            string.IsNullOrWhiteSpace(config.HardwareKey) ||
            !IsDefaultVmmDeviceName(config.VmmDeviceName))
        {
            return config.VmmDeviceName;
        }

        var device = _hardwareResolver.ListDevices()
            .FirstOrDefault(item => DeviceMatchesHardwareKey(item, config.HardwareKey));
        return device?.VmmDeviceName ?? config.VmmDeviceName;
    }

    private static bool IsDefaultVmmDeviceName(string vmmDeviceName)
    {
        return string.IsNullOrWhiteSpace(vmmDeviceName) ||
            string.Equals(vmmDeviceName.Trim(), "fpga", StringComparison.OrdinalIgnoreCase);
    }

    private static bool DeviceMatchesHardwareKey(HardwareDeviceFeature device, string hardwareKey)
    {
        var expected = hardwareKey.Trim();
        return string.Equals(device.BindingKey.Trim(), expected, StringComparison.OrdinalIgnoreCase) ||
            device.AliasKeys.Any(alias => string.Equals(alias.Trim(), expected, StringComparison.OrdinalIgnoreCase));
    }
}
