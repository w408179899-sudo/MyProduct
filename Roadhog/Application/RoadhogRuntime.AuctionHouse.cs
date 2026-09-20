using Roadhog.Application.AuctionHouse;
using Roadhog.Core.Accounts;
using Roadhog.Core.Common;

namespace Roadhog.Application;

public sealed partial class RoadhogRuntime
{
    public async Task<OperationResult<string>> TestAuctionHouseAsync(string accountName, IReadOnlyList<BagCleanupTradeItemConfig> items,
        IProgress<string>? progress = null, CancellationToken cancellationToken = default, string? configuredNpcName = null)
    {
        if (_keyboardInput == null) return OperationResult<string>.Fail("鼠标键盘输入不可用。");
        if (!await _inventoryTestGate.WaitAsync(0, cancellationToken).ConfigureAwait(false)) return OperationResult<string>.Fail("背包测试正在进行。");
        try
        {
            var plan = BagCleanupTradeItemConfig.Normalize(items);
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); deadline.CancelAfter(TimeSpan.FromMinutes(2));
            OperationResult<string>? result = null;
            async Task<OperationResult> Execute()
            {
                var snapshots = _snapshotReaders.Create(ResolveSnapshotConfig(accountName), _logger, deadline.Token);
                result = await new AuctionHouseTestSequence(_keyboardInput, _logger).RunAsync(snapshots, plan, progress, deadline.Token, configuredNpcName).ConfigureAwait(false);
                return result.Success ? OperationResult.Ok() : OperationResult.Fail(result.Error ?? "拍卖行测试失败。");
            }
            var operation = Orchestrator == null ? await Execute().ConfigureAwait(false) : await Orchestrator.RunManualInputAsync(Execute).ConfigureAwait(false);
            return result ?? OperationResult<string>.Fail(operation.Error ?? "无法开始拍卖行测试。");
        }
        catch (OperationCanceledException) { return OperationResult<string>.Fail(cancellationToken.IsCancellationRequested ? "拍卖行测试已取消。" : "拍卖行测试超时。"); }
        catch (Exception ex) { return OperationResult<string>.Fail(ex.Message); }
        finally { _inventoryTestGate.Release(); }
    }
}
