using Roadhog.Application.BagCleanup;
using Roadhog.Core.Accounts;
using Roadhog.Core.Common;

namespace Roadhog.Application;

public sealed partial class RoadhogRuntime
{
    public async Task<OperationResult<InventoryDiscardTestResult>> TestInventoryDiscardAsync(string accountName,
        MaintenanceScriptSettings settings, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        if (_keyboardInput == null) return OperationResult<InventoryDiscardTestResult>.Fail("鼠标键盘输入不可用。");
        if (!await _inventoryTestGate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
            return OperationResult<InventoryDiscardTestResult>.Fail("背包测试正在进行，请等待完成。");
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(TimeSpan.FromMinutes(2));
            OperationResult<InventoryDiscardTestResult>? result = null;
            async Task<OperationResult> Execute()
            {
                var snapshots = _snapshotReaders.Create(ResolveSnapshotConfig(accountName), _logger, deadline.Token);
                result = await new InventoryDiscardSequence(_keyboardInput, _logger)
                    .RunAsync(snapshots, accountName, settings, progress, deadline.Token).ConfigureAwait(false);
                return result.Success ? OperationResult.Ok() : OperationResult.Fail(result.Error ?? "丢弃失败。");
            }
            var operation = Orchestrator == null ? await Execute().ConfigureAwait(false)
                : await Orchestrator.RunManualInputAsync(Execute).ConfigureAwait(false);
            return result ?? OperationResult<InventoryDiscardTestResult>.Fail(operation.Error ?? "无法开始丢弃测试。");
        }
        catch (OperationCanceledException)
        {
            return OperationResult<InventoryDiscardTestResult>.Fail(cancellationToken.IsCancellationRequested ? "丢弃测试已取消。" : "丢弃测试超时，已释放输入。");
        }
        catch (Exception ex) { return OperationResult<InventoryDiscardTestResult>.Fail(ex.Message); }
        finally { _inventoryTestGate.Release(); }
    }
}
