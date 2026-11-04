using Roadhog.Application.ManastoneExchange;
using Roadhog.Core.Common;

namespace Roadhog.Application;

public sealed partial class RoadhogRuntime
{
    public async Task<OperationResult<ManastoneExchangeResult>> RunManastoneExchangeAsync(string accountName,
        IProgress<string>? progress, CancellationToken token)
    {
        if (_keyboardInput == null) return OperationResult<ManastoneExchangeResult>.Fail("鼠标键盘输入不可用。");
        if (!await _inventoryTestGate.WaitAsync(0, token).ConfigureAwait(false))
            return OperationResult<ManastoneExchangeResult>.Fail("另一个背包操作正在进行。");
        try
        {
            OperationResult<ManastoneExchangeResult>? result = null;
            async Task<OperationResult> Execute()
            {
                var snapshots = _snapshotReaders.Create(ResolveSnapshotConfig(accountName), _logger, token);
                result = await new ManastoneExchangeSequence(_keyboardInput, _logger)
                    .RunAsync(snapshots, accountName, progress, token).ConfigureAwait(false);
                return result.Success ? OperationResult.Ok() : OperationResult.Fail(result.Error ?? "魔石兑换停止。");
            }
            var operation = Orchestrator == null ? await Execute().ConfigureAwait(false)
                : await Orchestrator.RunManualInputAsync(Execute).ConfigureAwait(false);
            return result ?? OperationResult<ManastoneExchangeResult>.Fail(operation.Error ?? "无法开始魔石兑换。");
        }
        finally { _inventoryTestGate.Release(); }
    }
}
