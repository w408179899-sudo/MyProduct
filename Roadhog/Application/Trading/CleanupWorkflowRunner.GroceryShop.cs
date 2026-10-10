using Roadhog.Application.BagCleanup;
using Roadhog.Application.Workers;
using Roadhog.Core.Accounts;
using Roadhog.Core.Model;
using static Roadhog.Application.Trading.TradingActions;

namespace Roadhog.Application.Trading;

public sealed partial class CleanupWorkflowRunner
{
    public async Task<bool> GroceryScheduleDueAsync(AccountWorkerContext context, DateTimeOffset now)
    {
        var settings = context.Config.ScriptSettings!.Maintenance.CleanupWorkflow;
        if (settings.Mode != CleanupMode.GroceryShop || !settings.GroceryScheduleEnabled) return false;
        Require(grocerySuccessStore != null, "杂货摆摊成功记录存储未配置。");
        var last = await grocerySuccessStore!.LoadAsync(context.Config.InstanceId, context.StopToken);
        return GroceryShopSchedule.LatestDue(settings, now, last) != null;
    }

    private async Task RunGroceryShopAsync(AccountWorkerContext context, CleanupRequest request, Action<string> report)
    {
        var settings = context.Config.ScriptSettings!;
        var token = context.StopToken;
        var actions = new TradingActions(input, context.Snapshots, token, groceryDelay);
        void LogSkip(string message)
        {
            report(message);
            context.Logger.Info("grocery_shop.skipped", new Dictionary<string, object?>
                { ["account"] = context.Config.AccountName, ["trigger"] = request.GroceryTrigger.ToString(), ["reason"] = message });
        }
        try
        {
            Require(grocerySuccessStore != null, "杂货摆摊成功记录存储未配置。");
            var bag = (await context.Snapshots.ReadInventoryAsync().WaitAsync(token)).Value;
            if (string.IsNullOrWhiteSpace(settings.Paths.GroceryReturnItemName) ||
                GroceryReturnSequence.FindScroll(bag, settings.Paths.GroceryReturnItemName) == null)
            {
                LogSkip("没有配置的回程卷轴，跳过本次杂货摆摊：" + settings.Paths.GroceryReturnItemName);
                return;
            }
            settings.Paths.GroceryReturnItemName = settings.Paths.GroceryReturnItemName.Trim();
            // Exclude the selected return scroll from this request's discard and stall plans.
            if (!settings.Maintenance.BagCleanupExcludedItemNames.Contains(settings.Paths.GroceryReturnItemName, StringComparer.OrdinalIgnoreCase))
                settings.Maintenance.BagCleanupExcludedItemNames.Add(settings.Paths.GroceryReturnItemName);
            if (request.GroceryTrigger == GroceryShopTrigger.Backpack)
            {
                await actions.Reset();
                var afterDiscard = await PrepareBackpackGroceryAsync(context, request, report);
                if (afterDiscard == null) return;
                bag = afterDiscard;
                if (GroceryReturnSequence.FindScroll(bag, settings.Paths.GroceryReturnItemName) == null)
                { LogSkip("丢弃后缺少所选回程卷轴，本次不出发"); return; }
            }
            var loaded = await paths.LoadAsync(settings.Paths.GroceryStallPathName, token);
            Require(loaded.Success && loaded.Value is { MapId: > 0, PointCount: > 0 }, "请录制并选择带有地图的杂货摆摊路径。");
            var route = loaded.Value!.Clone();
            var plan = DiscountedPersonalShopWorkflow.Plan(bag.Where(i =>
                !string.Equals(i.Name, settings.Paths.GroceryReturnItemName, StringComparison.OrdinalIgnoreCase)).ToArray(),
                settings.Maintenance, report);
            if (plan.Count == 0) { LogSkip("没有可按折扣摆摊的物品，本次不出发"); return; }
            await new GroceryReturnSequence(input, groceryDelay, groceryClock).RunAsync(context, route, settings.Paths.GroceryReturnItemName, report);
            report("卷轴回程已确认，前往杂货摆摊终点");
            Check(await executePath(context, route.Name, route.Points.Select(p => p.ToVector3()).ToArray()));
            token.ThrowIfCancellationRequested();
            // Replan after travel; the used scroll and any changed inventory cannot enter the stall.
            await DiscountedPersonalShopWorkflow.RunAsync(context.Snapshots, settings.Maintenance,
                items =>
                {
                    var filtered = items.Where(p => !string.Equals(p.Item.Name, settings.Paths.GroceryReturnItemName, StringComparison.OrdinalIgnoreCase)).ToArray();
                    Require(filtered.Length > 0, "到达后没有可摆摊物品，未确认售罄。");
                    return new ConfiguredPersonalShopSequence(input, groceryDelay).RunAsync(context.Snapshots, filtered,
                        report, token, settings.Maintenance.CleanupWorkflow.StandaloneShopDiscount);
                },
                () => Task.CompletedTask, report, token);
            var soldOutAt = groceryClock?.Invoke() ?? DateTimeOffset.UtcNow;
            await grocerySuccessStore!.RecordAsync(context.Config.InstanceId, soldOutAt, token);
            report("杂货摆摊全部售罄，开始按配置丢弃剩余背包物品");
            await new BagCleanupController(input, paths, executePath).RunDiscardRequestedAsync(context, report);
            token.ThrowIfCancellationRequested();
            request.SoldOutConfirmed = true;
            report("杂货摆摊售罄时间已保存，丢弃完成，准备停止并重新启动脚本");
            context.Logger.Info("grocery_shop.sold_out", new Dictionary<string, object?>
                { ["account"] = context.Config.AccountName, ["soldOutAt"] = soldOutAt });
        }
        finally { await actions.Reset(); }
    }

    private async Task<IReadOnlyList<InventoryItemSnapshot>?> PrepareBackpackGroceryAsync(AccountWorkerContext context, CleanupRequest request, Action<string> report)
    {
        var settings = context.Config.ScriptSettings!.Maintenance;
        var token = context.StopToken;
        request.PreparationStage = CleanupPreparationStage.Discarding;
        while (true)
        {
            var bag = (await context.Snapshots.ReadInventoryAsync().WaitAsync(token)).Value;
            var capacity = (await context.Snapshots.ReadInventoryCapacityAsync().WaitAsync(token)).Value;
            var freeSlots = BagCleanupController.CountFreeSlots(bag, capacity);
            if (!settings.BagCleanupEnabled || settings.BagCleanupThreshold <= 0 || freeSlots >= settings.BagCleanupThreshold)
            {
                request.PreparationStage = CleanupPreparationStage.None;
                report("背包空位已满足阈值，本次不出发摆摊，继续挂机");
                return null;
            }
            var candidates = BagCleanupItemMatcher.SelectDiscardItems(bag, settings);
            if (candidates.Count == 0)
            {
                request.PreparationStage = CleanupPreparationStage.None;
                context.Logger.Info("grocery_shop.backpack.discard_exhausted", new Dictionary<string, object?>
                {
                    ["account"] = context.Config.AccountName, ["freeSlots"] = freeSlots,
                    ["threshold"] = settings.BagCleanupThreshold, ["remainingDiscardCandidateCount"] = 0
                });
                report("已无可丢弃物品，背包空位仍低于阈值，准备出发摆摊");
                return bag;
            }
            report("背包空位不足，先按配置原地丢弃，再判断是否出发摆摊");
            await new BagCleanupController(input, paths, executePath).RunDiscardRequestedAsync(context, report);
            // Only confirmed exhaustion plus a new capacity observation may authorize departure.
        }
    }
}
