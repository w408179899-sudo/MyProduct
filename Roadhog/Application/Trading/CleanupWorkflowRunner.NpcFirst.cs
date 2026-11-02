using Roadhog.Application.BagCleanup;
using Roadhog.Application.Travel;
using Roadhog.Application.Workers;
using Roadhog.Core.Accounts;
using Roadhog.Core.Model;
using Roadhog.Core.Paths;
using static Roadhog.Application.Trading.TradingActions;

namespace Roadhog.Application.Trading;

public sealed partial class CleanupWorkflowRunner
{
    private async Task RunNpcFirstGroceryAsync(AccountWorkerContext context, CleanupRequest request, Action<string> report,
        Func<AccountWorkerContext, Task>? returnToCombat)
    {
        var settings = context.Config.ScriptSettings!;
        var token = context.StopToken;
        var actions = new TradingActions(input, context.Snapshots, token, groceryDelay);
        settings.Paths.BagCleanupReturnItemName = settings.Paths.BagCleanupReturnItemName.Trim();
        settings.Paths.GroceryReturnItemName = settings.Paths.GroceryReturnItemName.Trim();
        // Automatic local discard must protect both trip legs before matching candidates.
        foreach (var name in new[] { settings.Paths.BagCleanupReturnItemName, settings.Paths.GroceryReturnItemName })
            if (!string.IsNullOrWhiteSpace(name) && !settings.Maintenance.BagCleanupExcludedItemNames.Contains(name, StringComparer.OrdinalIgnoreCase))
                settings.Maintenance.BagCleanupExcludedItemNames.Add(name);
        bool IsReturnItem(InventoryItemSnapshot item) =>
            string.Equals(item.Name, settings.Paths.GroceryReturnItemName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(item.Name, settings.Paths.BagCleanupReturnItemName, StringComparison.OrdinalIgnoreCase);
        try
        {
            await actions.Reset();
            if (request.GroceryTrigger == GroceryShopTrigger.Backpack)
            {
                if (await PrepareBackpackGroceryAsync(context, request, report) == null) return;
            }
            request.GroceryNpcStageStarted = true;
            var outcome = await new GroceryNpcSaleSequence(input, paths, executePath, groceryDelay, groceryClock).RunAsync(context, report);
            token.ThrowIfCancellationRequested();
            Require(outcome != GroceryNpcSaleOutcome.Failed, "NPC 出售未确认完成，放弃本次清包；不重复交易，也不作为限卖转入摆摊。");
            // A later release/closure fault must not erase already verified NPC completion.
            request.GroceryProcessingCompleted = outcome == GroceryNpcSaleOutcome.AllItemsSold;
            Require(await new TradingWindowRecovery(input, context.Snapshots, groceryDelay).TryCloseAsync(report, token, includeShop: false),
                "NPC 收尾界面尚未关闭，不能继续移动或登记摆摊。");
            if (outcome == GroceryNpcSaleOutcome.AllItemsSold)
            {
                report("NPC 两组物品已售空，跳过摆摊，准备停止并重新启动脚本");
                context.Logger.Info("grocery_shop.npc_only.complete", new Dictionary<string, object?> { ["account"] = context.Config.AccountName });
                return;
            }

            // This leg is conditional. Validate it from the actual remaining inventory after NPC sale.
            var bag = (await context.Snapshots.ReadInventoryAsync().WaitAsync(token)).Value;
            var plan = DiscountedPersonalShopWorkflow.Plan(bag.Where(i => !IsReturnItem(i)).ToArray(), settings.Maintenance, report);
            if (plan.Count == 0)
            {
                report("NPC 尚有剩余物品，但没有符合摆摊名单的物品，本次结束并保留剩余物品");
                context.Logger.Info("grocery_shop.skipped", new Dictionary<string, object?>
                    { ["account"] = context.Config.AccountName, ["reason"] = "no_stall_items_after_npc", ["npcOutcome"] = outcome.ToString() });
                await ReturnGroceryToCombatAsync(context, report, returnToCombat);
                return;
            }
            Require(grocerySuccessStore != null, "杂货摆摊成功记录存储未配置。");
            var loaded = await paths.LoadAsync(settings.Paths.GroceryStallPathName, token);
            Require(loaded.Success && loaded.Value is { MapId: > 0, PointCount: > 0 }, "请录制并选择带有地图的杂货摆摊路径。");
            var route = loaded.Value!.Clone();
            var travelPlan = new ReturnItemPlan((await context.Snapshots.ReadChannelTransitionAsync().WaitAsync(token)).Value);
            travelPlan.Add(route, settings.Paths.GroceryReturnItemName);
            CleanupPathTravel.RequireItems(travelPlan, bag);
            await new GroceryReturnSequence(input, groceryDelay, groceryClock).RunAsync(context, route, settings.Paths.GroceryReturnItemName, report);
            report("前往杂货摆摊终点，按原名单和折扣出售");
            await FollowGroceryStallPathAsync(context, route);
            token.ThrowIfCancellationRequested();
            bag = (await context.Snapshots.ReadInventoryAsync().WaitAsync(token)).Value;
            plan = DiscountedPersonalShopWorkflow.Plan(bag.Where(i => !IsReturnItem(i)).ToArray(), settings.Maintenance, report);
            Require(plan.Count > 0, "到达后没有可摆摊物品，未确认售罄。");
            await new ConfiguredPersonalShopSequence(input, groceryDelay).RunAsync(context.Snapshots, plan,
                report, token, settings.Maintenance.CleanupWorkflow.StandaloneShopDiscount);
            var soldOutAt = groceryClock?.Invoke() ?? DateTimeOffset.UtcNow;
            request.SoldOutConfirmed = true;
            await new GroceryStageContinuation(context, input, groceryDelay).TryAsync("sold_out_record",
                () => grocerySuccessStore!.RecordAsync(context.Config.InstanceId, soldOutAt, token));
            token.ThrowIfCancellationRequested();
            request.GroceryProcessingCompleted = true;
            report("杂货摆摊全部售罄，准备停止并重新启动脚本");
            context.Logger.Info("grocery_shop.sold_out", new Dictionary<string, object?>
                { ["account"] = context.Config.AccountName, ["soldOutAt"] = soldOutAt, ["npcOutcome"] = outcome.ToString() });
        }
        catch (Exception) when (!token.IsCancellationRequested)
        {
            await new TradingWindowRecovery(input, context.Snapshots, groceryDelay).TryCloseAsync(message =>
            {
                report(message);
                context.Logger.Warn("grocery_shop.recovery.ui_failed", new Dictionary<string, object?>
                    { ["account"] = context.Config.AccountName, ["error"] = message });
            }, token, includeShop: false);
            throw;
        }
        finally { await actions.Reset(); }
    }

    private async Task ReturnGroceryToCombatAsync(AccountWorkerContext context, Action<string> report,
        Func<AccountWorkerContext, Task>? returnToCombat)
    {
        report("没有可摆摊物品，保留剩余物品，回城后沿复活路径返回挂机点");
        await new BagCleanupController(input, paths, executePath).ReturnToReviveRequestedAsync(context, report);
        if (returnToCombat != null) await returnToCombat(context);
        else
        {
            var route = await paths.LoadAsync(context.Config.ScriptSettings!.Paths.RevivePathName, context.StopToken);
            Require(route.Success && route.Value is { PointCount: > 0 }, "返回挂机点的复活路径不可用。");
            Check(await CleanupPathTravel.FollowAsync(context, route.Value!, executePath));
        }
        context.Logger.Info("grocery_shop.return_to_combat.complete", new Dictionary<string, object?>
            { ["account"] = context.Config.AccountName });
    }
}
