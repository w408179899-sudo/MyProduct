using Roadhog.Core.Accounts;
using Roadhog.Core.Api;
using Roadhog.Core.Model;

namespace Roadhog.Application.Trading;

public static class DiscountedPersonalShopWorkflow
{
    // VendorSellUnitPrice is the raw system price, also divided by 20 in AuctionPricePolicy.
    public static ulong UnitPrice(ulong systemPrice, int discount)
    {
        if (discount is < 4 or > 9) throw new ArgumentOutOfRangeException(nameof(discount), "摆摊折扣必须为 4～9 折。");
        return (ulong)decimal.Floor(systemPrice * (decimal)discount / 200m);
    }

    public static IReadOnlyList<PlannedShopItem> Plan(IReadOnlyList<InventoryItemSnapshot> inventory,
        MaintenanceScriptSettings settings, Action<string> report)
    {
        var discount = settings.CleanupWorkflow.StandaloneShopDiscount;
        _ = UnitPrice(0, discount);
        var plan = new List<PlannedShopItem>();
        foreach (var item in inventory)
        {
            if (CleanupTradePolicy.Rule(item, settings, false) is null) continue;
            var price = UnitPrice(item.VendorSellUnitPrice, discount);
            if (price == 0)
            {
                report($"跳过 {item.Name}：系统价无效或折后单价不足 1 金币。");
                continue;
            }
            _ = checked(price * item.Count);
            plan.Add(new(item, price));
        }
        return plan;
    }

    public static async Task RunAsync(IRoadhogSnapshotReader snapshots, MaintenanceScriptSettings settings,
        Func<IReadOnlyList<PlannedShopItem>, Task> sell, Func<Task> returnToCombat,
        Action<string> report, CancellationToken token)
    {
        var inventory = (await snapshots.ReadInventoryAsync().WaitAsync(token)).Value;
        var plan = Plan(inventory, settings, report);
        if (plan.Count == 0) throw new InvalidOperationException("没有可按折扣摆摊的物品，请检查摆摊过滤和系统价。");
        report($"自动摆摊：{settings.CleanupWorkflow.StandaloneShopDiscount} 折，共 {plan.Count} 项，等待全部售罄。");
        await sell(plan);
        token.ThrowIfCancellationRequested();
        await returnToCombat();
    }
}
