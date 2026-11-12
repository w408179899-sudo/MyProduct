using Roadhog.Application.Input;

namespace Roadhog.Application.Trading;

/// <summary>Retains the current shop operation; never restarts the cleanup workflow.</summary>
internal sealed class PersonalShopRecovery(TradingActions actions, Action<string> report, CancellationToken token)
{
    // Only wrap an operation whose mouse movement precedes every submission.
    // A timeout after a click must go through ConfirmAsync instead.
    public async Task BeforeInputAsync(string stage, Func<Task> operation, Func<Task<bool>> ready)
    {
        var failures = 0;
        while (true)
        {
            token.ThrowIfCancellationRequested();
            await actions.Alive();
            if (failures > 0 && !await ready())
            {
                report($"摆摊恢复中：{stage}界面尚未确认，30 秒后复查，不重复提交");
                await actions.Pause(30000);
                continue;
            }
            try { await operation(); return; }
            catch (MouseMovementTimeoutException ex) when (!token.IsCancellationRequested)
            {
                failures++;
                // Three local retries, then keep the task and retry at a low rate.
                var wait = failures <= 3 ? 1000 : 30000;
                report($"摆摊恢复中：{stage}鼠标定位失败（第 {failures} 次），{wait / 1000} 秒后重试：{ex.Message}");
                await actions.Pause(wait);
            }
        }
    }

    public async Task<T> ConfirmAsync<T>(string stage, Func<Task<T>> confirmation)
    {
        while (true)
        {
            token.ThrowIfCancellationRequested();
            try { return await confirmation(); }
            catch (TradingActions.ConfirmationTimeoutException) when (!token.IsCancellationRequested)
            {
                report($"摆摊恢复中：等待{stage}确认，30 秒后复查，不重复提交");
                await actions.Pause(30000);
            }
        }
    }
}
