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

    public async Task<OperationResult<IReadOnlyList<SkillSnapshot>>> RefreshSkillsAsync(CancellationToken cancellationToken = default)
    {
        var result = await _gameApi.ReadSkillsAsync(cancellationToken).ConfigureAwait(false);
        if (result.Success)
        {
            _logger.Info("skills.refresh.ok", new Dictionary<string, object?> { ["count"] = result.Value?.Count ?? 0 });
        }
        else
        {
            _logger.Warn("skills.refresh.failed", new Dictionary<string, object?> { ["error"] = result.Error });
        }

        return result;
    }
}
