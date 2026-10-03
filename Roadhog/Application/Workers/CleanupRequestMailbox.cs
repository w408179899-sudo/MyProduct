using Roadhog.Core.Accounts;
using Roadhog.Core.Common;

namespace Roadhog.Application.Workers;

internal enum CleanupPreparationStage { None, ReturningToTown, Discarding }

public sealed record CleanupRequest(ScriptSettings Settings, bool Manual, bool ResetsCooldown = true, bool AllowNpcSell = true)
{
    public Guid RequestId { get; } = Guid.NewGuid();
    // Execution progress belongs to this worker request, never to persisted settings.
    public bool StandaloneShop { get; init; }
    public string? Failure { get; set; }
    internal CleanupPreparationStage PreparationStage { get; set; }
    internal bool TownReturnCompleted { get; set; }
    internal bool FullCleanupStarted { get; set; }
    internal bool SharedConfigurationLoaded { get; set; }
}

/// <summary>One pending or executing request per worker session. Never survives Stop/Start.</summary>
public sealed class CleanupRequestMailbox
{
    private readonly object sync = new();
    private CleanupRequest? request;
    private Guid? standaloneShopRestartRequestId;
    public CleanupRequest? Current { get { lock (sync) return request; } }
    public Guid? StandaloneShopRestartRequestId { get { lock (sync) return standaloneShopRestartRequestId; } }
    public OperationResult Request(ScriptSettings settings, bool manual, bool resetsCooldown = true, bool allowNpcSell = true, bool standaloneShop = false)
    {
        lock (sync)
        {
            if (standaloneShopRestartRequestId.HasValue) return OperationResult.Fail("摆摊已售罄，正在等待脚本停止并重新启动。");
            if (request != null) return OperationResult.Fail("清包流程已在等待或执行，请勿重复启动。");
            var copy = settings.Clone();
            copy.Maintenance.CleanupWorkflow = copy.Maintenance.CleanupWorkflow.ForTrigger(manual);
            if (standaloneShop)
            {
                if (copy.Maintenance.CleanupWorkflow.StandaloneShopDiscount is < 4 or > 9)
                    return OperationResult.Fail("摆摊折扣必须为 4～9 折。");
                copy.Maintenance.CleanupWorkflow.NpcCleanup = false;
                copy.Maintenance.CleanupWorkflow.Auction = false;
                copy.Maintenance.CleanupWorkflow.TransferGold = false;
                copy.Maintenance.CleanupWorkflow.PersonalShop = true;
            }
            if (copy.Maintenance.CleanupWorkflow.Describe().Length == 0) return OperationResult.Fail("请先在清包页选择执行项目。");
            request = new(copy, manual, resetsCooldown, allowNpcSell) { StandaloneShop = standaloneShop }; return OperationResult.Ok();
        }
    }
    public void Complete() { lock (sync) request = null; }

    public void CompleteStandaloneShopForRestart(CleanupRequest completed)
    {
        lock (sync)
        {
            if (!ReferenceEquals(request, completed) || !completed.StandaloneShop || completed.Failure != null)
                throw new InvalidOperationException("自动摆摊完成请求与当前任务不一致。");
            standaloneShopRestartRequestId = completed.RequestId;
            request = null;
        }
    }
}
