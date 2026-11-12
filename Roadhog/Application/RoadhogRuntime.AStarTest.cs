using Roadhog.Application.NavigationTests;
using Roadhog.Application.Workers;
using Roadhog.Core.Common;

namespace Roadhog.Application;

public sealed partial class RoadhogRuntime
{
    public async Task<OperationResult> RunAStarWalkTestAsync(string accountName, AStarWalkTestRequest request,
        IProgress<string>? progress, CancellationToken cancellationToken)
    {
        if (_stationaryCombatController is null) return OperationResult.Fail("移动控制器不可用。");
        if (!await _inventoryTestGate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
            return OperationResult.Fail("另一个手动操作正在执行。");
        try
        {
            async Task<OperationResult> Execute()
            {
                // The movement controller and all its pollers share this timeout, including snapshot waits.
                using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                lifetime.CancelAfter(AStarWalkTest.MaximumDuration);
                var config = LoadExecutionAccountConfig(accountName, null);
                var context = new AccountWorkerContext(config, _snapshotReaders, _logger, Accounts, new AccountWorkerOptions(), lifetime.Token);
                _logger.Info("astar_test.walk.start", new Dictionary<string, object?>
                { ["account"] = accountName, ["mapId"] = request.MapId, ["points"] = request.Points?.Length });
                var result = await _stationaryCombatController.ExecuteAStarTestAsync(context, request, progress).ConfigureAwait(false);
                _logger.Info("astar_test.walk.finished", new Dictionary<string, object?>
                { ["account"] = accountName, ["success"] = result.Success, ["reason"] = result.Error });
                return result;
            }
            return Orchestrator is null ? await Execute().ConfigureAwait(false)
                : await Orchestrator.RunManualInputAsync(Execute).ConfigureAwait(false);
        }
        finally { _inventoryTestGate.Release(); }
    }
}
