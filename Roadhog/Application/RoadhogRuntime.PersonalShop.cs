using Roadhog.Application.PersonalShop;
using Roadhog.Core.Accounts;
using Roadhog.Core.Common;

namespace Roadhog.Application;

public sealed partial class RoadhogRuntime
{
    private readonly SemaphoreSlim _personalShopTestGate = new(1, 1);

    public async Task<OperationResult<PersonalShopTestResult>> TestPersonalShopAsync(string accountName,
        MaintenanceScriptSettings settings, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        if (_keyboardInput == null) return OperationResult<PersonalShopTestResult>.Fail("鼠标键盘输入不可用。");
        if (!await _personalShopTestGate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
            return OperationResult<PersonalShopTestResult>.Fail("摆摊测试正在进行，请等待完成。");
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(TimeSpan.FromMinutes(2));
            OperationResult<PersonalShopTestResult>? result = null;
            async Task<OperationResult> Execute()
            {
                var config = ResolveSnapshotConfig(accountName);
                var snapshots = _snapshotReaders.Create(config, _logger, deadline.Token);
                result = await new PersonalShopSequence(_keyboardInput, _logger)
                    .RunAsync(snapshots, accountName, settings, progress, deadline.Token).ConfigureAwait(false);
                return result.Success ? OperationResult.Ok() : OperationResult.Fail(result.Error ?? "摆摊失败。");
            }
            var operation = Orchestrator == null ? await Execute().ConfigureAwait(false)
                : await Orchestrator.RunManualInputAsync(Execute).ConfigureAwait(false);
            return result ?? OperationResult<PersonalShopTestResult>.Fail(operation.Error ?? "无法开始摆摊测试。");
        }
        catch (OperationCanceledException)
        {
            return OperationResult<PersonalShopTestResult>.Fail(cancellationToken.IsCancellationRequested ? "摆摊测试已取消。" : "摆摊测试超时，已释放输入。");
        }
        catch (Exception ex) { return OperationResult<PersonalShopTestResult>.Fail(ex.Message); }
        finally { _personalShopTestGate.Release(); }
    }
}
