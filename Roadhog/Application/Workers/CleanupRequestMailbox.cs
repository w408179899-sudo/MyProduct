using Roadhog.Application.Trading;
using Roadhog.Core.Accounts;
using Roadhog.Core.Common;

namespace Roadhog.Application.Workers;

internal enum CleanupPreparationStage { None, ReturningToTown, Discarding }
public enum GroceryShopTrigger { None, Backpack, Scheduled, Manual }
internal sealed record ShopRestartRequest(Guid Id, bool ScheduledGrocerySellout);

public sealed record CleanupRequest(ScriptSettings Settings, bool Manual, bool ResetsCooldown = true, bool AllowNpcSell = true)
{
    public Guid RequestId { get; } = Guid.NewGuid();
    // Execution progress belongs to this worker request, never to persisted settings.
    public bool StandaloneShop { get; init; }
    public bool ItemTransfer { get; init; }
    public bool ArrowRestock { get; init; }
    public GroceryShopTrigger GroceryTrigger { get; init; }
    public bool GroceryShop => GroceryTrigger != GroceryShopTrigger.None;
    public bool SoldOutConfirmed { get; internal set; }
    public bool GroceryProcessingCompleted { get; internal set; }
    internal bool GroceryNpcStageStarted { get; set; }
    internal GroceryReturnSequence.CombatResume? ItemTransferReturn { get; set; }
    internal GroceryReturnSequence.CombatResume? ScheduledGroceryReturn { get; set; }
    public bool RequestsRestart => StandaloneShop || ItemTransfer && SoldOutConfirmed || GroceryShop && (SoldOutConfirmed || GroceryProcessingCompleted);
    public string? Failure { get; set; }
    internal CleanupPreparationStage PreparationStage { get; set; }
    internal bool TownReturnCompleted { get; set; }
    internal bool FullCleanupStarted { get; set; }
    internal bool SharedConfigurationLoaded { get; set; }
    internal bool WorkflowStarted { get; set; }
}

/// <summary>Worker-session cleanup requests, with priority for scheduled grocery cleanup.</summary>
public sealed class CleanupRequestMailbox
{
    private readonly object sync = new();
    private CleanupRequest? request;
    private CleanupRequest? scheduledRequest;
    private ShopRestartRequest? shopRestart;
    private DateTimeOffset automaticRetryAfter;
    private DateTimeOffset backpackRetryAfter;
    public CleanupRequest? Current { get { lock (sync) return shopRestart is null ? scheduledRequest ?? request : null; } }
    internal ShopRestartRequest? ShopRestart { get { lock (sync) return shopRestart; } }
    public Guid? StandaloneShopRestartRequestId => ShopRestart?.Id;
    public DateTimeOffset AutomaticRetryAfter { get { lock (sync) return automaticRetryAfter; } }
    public DateTimeOffset BackpackRetryAfter { get { lock (sync) return backpackRetryAfter; } }

    public void DeferBackpackCleanup(TimeSpan delay)
    {
        lock (sync)
        {
            var retryAt = DateTimeOffset.UtcNow + delay;
            if (retryAt > backpackRetryAfter) backpackRetryAfter = retryAt;
        }
    }
    public OperationResult Request(ScriptSettings settings, bool manual, bool resetsCooldown = true, bool allowNpcSell = true, bool standaloneShop = false,
        GroceryShopTrigger groceryTrigger = GroceryShopTrigger.None, bool arrowRestock = false, bool itemTransfer = false)
    {
        lock (sync)
        {
            if (shopRestart is not null) return OperationResult.Fail("摆摊已售罄，正在等待后续操作。");
            var scheduled = groceryTrigger == GroceryShopTrigger.Scheduled;
            if (scheduledRequest != null || (scheduled ? request?.WorkflowStarted == true : request != null))
                return OperationResult.Fail("清包流程已在等待或执行，请勿重复启动。");
            if (itemTransfer)
            {
                if (!manual || standaloneShop || groceryTrigger != GroceryShopTrigger.None || arrowRestock)
                    return OperationResult.Fail("倒货只能作为独立的手动任务提交。");
                if (string.IsNullOrWhiteSpace(settings.Paths.StallPathName) || string.IsNullOrWhiteSpace(settings.Paths.StallReturnItemName))
                    return OperationResult.Fail("请配置倒货路线（转移到仓库号路径）和回程卷轴。");
                request = new(settings.Clone(), true, ResetsCooldown: false, AllowNpcSell: false) { ItemTransfer = true };
                return OperationResult.Ok();
            }
            if (standaloneShop && groceryTrigger != GroceryShopTrigger.None || arrowRestock && (standaloneShop || groceryTrigger != GroceryShopTrigger.None))
                return OperationResult.Fail("不能同时提交两种摆摊任务。");
            var copy = settings.Clone();
            copy.Maintenance.CleanupWorkflow = copy.Maintenance.CleanupWorkflow.ForTrigger(manual);
            if (arrowRestock)
            {
                copy.Maintenance.CleanupWorkflow = new() { Mode = CleanupMode.Normal, NpcCleanup = true };
                allowNpcSell = false;
                resetsCooldown = false;
            }
            if (!arrowRestock && !standaloneShop && groceryTrigger == GroceryShopTrigger.None && copy.Maintenance.CleanupWorkflow.Mode == CleanupMode.GroceryShop)
                groceryTrigger = manual ? GroceryShopTrigger.Manual : GroceryShopTrigger.Backpack;
            if (groceryTrigger != GroceryShopTrigger.None)
            {
                if (copy.Maintenance.CleanupWorkflow.StandaloneShopDiscount is < 4 or > 9)
                    return OperationResult.Fail("摆摊折扣必须为 4～9 折。");
                try { GroceryShopSchedule.Normalize(copy.Maintenance.CleanupWorkflow.GroceryScheduleTimes); }
                catch (FormatException) { return OperationResult.Fail("定时时间必须为 HH:mm。"); }
                copy.Maintenance.CleanupWorkflow.Mode = CleanupMode.GroceryShop;
            }
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
            var queued = new CleanupRequest(copy, manual, resetsCooldown, allowNpcSell)
                { StandaloneShop = standaloneShop, GroceryTrigger = groceryTrigger, ArrowRestock = arrowRestock };
            if (scheduled) scheduledRequest = queued;
            else request = queued;
            return OperationResult.Ok();
        }
    }
    public void Complete() { lock (sync) RemoveCurrent(); }

    private void RemoveCurrent()
    {
        if (scheduledRequest != null) scheduledRequest = null;
        else request = null;
    }

    public void Abandon()
    {
        lock (sync)
        {
            RemoveCurrent();
            shopRestart = null;
            automaticRetryAfter = DateTimeOffset.UtcNow.AddSeconds(30);
        }
    }

    public void CompleteStandaloneShopForRestart(CleanupRequest completed)
    {
        lock (sync)
        {
            if (!ReferenceEquals(scheduledRequest ?? request, completed) || !completed.RequestsRestart || completed.Failure != null)
                throw new InvalidOperationException("自动摆摊完成请求与当前任务不一致。");
            shopRestart = new(completed.RequestId, completed.GroceryTrigger == GroceryShopTrigger.Scheduled);
            RemoveCurrent();
        }
    }

    internal bool ReleaseScheduledGrocerySellout(Guid requestId)
    {
        lock (sync)
        {
            if (shopRestart is not { ScheduledGrocerySellout: true } completion || completion.Id != requestId) return false;
            shopRestart = null;
            return true;
        }
    }
}
