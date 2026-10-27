using Roadhog.Core.Accounts;
using Roadhog.Core.Api;
using Roadhog.Core.Common;
using Roadhog.Core.Model;

namespace Roadhog.Infrastructure.WorkerProcesses;

/// <summary>A saved driver index is only a candidate; confirm the account before starting input or business work.</summary>
internal static class AccountIdentityStartGuard
{
    public static async Task<OperationResult> RunAsync(AccountConfig saved,
        Func<CancellationToken, Task<PlayerSnapshot>> readPlayer,
        Func<OperationResult> start, CancellationToken cancellationToken, TimeSpan? readTimeout = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!Roadhog.Infrastructure.Hardware.HardwareVerificationSession.IsCurrent(saved))
            return OperationResult.Fail(Roadhog.Infrastructure.Hardware.HardwareVerificationSession.RequiredMessage);
        if (string.IsNullOrWhiteSpace(saved.CharacterName))
            return OperationResult.Fail("尚未保存已确认的角色。请打开“设备/角色”，测试读取角色、勾选确认并保存硬件配置，再启动。");
        // ReadPlayer returns the provider-owned official snapshot from this worker's device session.
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(readTimeout ?? TimeSpan.FromSeconds(30));
        PlayerSnapshot player;
        using (SnapshotReadBudget.EnterCancellation(deadline.Token))
        {
            try
            {
                var pending = readPlayer(deadline.Token);
                _ = pending.ContinueWith(done => { _ = done.Exception; }, CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                player = await pending.WaitAsync(deadline.Token).ConfigureAwait(false);
                deadline.Token.ThrowIfCancellationRequested();
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            { return OperationResult.Fail("启动前角色核验等待超时，未启动挂机。请检查当前角色页面后重试。"); }
        }
        // The read-only cancellation scope must end before starting a worker: a new
        // worker inherits ExecutionContext and must not inherit this short deadline.
        cancellationToken.ThrowIfCancellationRequested();
        if (!string.Equals(player.CharacterName, saved.CharacterName, StringComparison.Ordinal))
            return OperationResult.Fail($"读取角色“{player.CharacterName}”与已保存角色“{saved.CharacterName}”不一致，已阻止启动。重启或插拔后读取编号可能变化，请在“设备/角色”中重新选择编号、测试并保存硬件配置。");
        return start();
    }
}
