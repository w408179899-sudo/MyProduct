using Roadhog.Core.Api;
using Roadhog.Core.Common;
using Roadhog.Core.Diagnostics;
using Roadhog.Core.Model;

namespace Roadhog.Application;

public sealed class RoadhogRuntime
{
    private readonly IRoadhogGameApi _gameApi;
    private readonly IRoadhogLogger _logger;

    public RoadhogRuntime(
        IRoadhogGameApi gameApi,
        IRoadhogLogger logger,
        AccountRuntimeManager accounts,
        AccountOrchestrator orchestrator)
    {
        _gameApi = gameApi;
        _logger = logger;
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

    private GameApiReadContext CreateReadContext(string accountName)
    {
        var account = Accounts.Snapshot()
            .FirstOrDefault(item => string.Equals(item.AccountName, accountName, StringComparison.OrdinalIgnoreCase));

        return account is null
            ? new GameApiReadContext(accountName, 0, string.Empty, string.Empty)
            : new GameApiReadContext(
                account.AccountName,
                account.ProcessId,
                account.TargetProcessName,
                account.VmmDeviceName);
    }
}
