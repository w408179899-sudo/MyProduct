using Roadhog.Application.BagCleanup;
using Roadhog.Application.Trading;
using Roadhog.Application.Workers;
using Roadhog.Core.Common;
using Roadhog.Core.Model;
using Roadhog.Core.Paths;

namespace Roadhog.Application.Travel;

public static class CleanupPathTravel
{
    public static async Task<int> NearbyIndexAsync(AccountWorkerContext context, SharedPathDocument path) =>
        NearbyPathEntry.Find((await context.Snapshots.ReadChannelTransitionAsync().WaitAsync(context.StopToken)).Value, path);

    public static void RequireItems(ReturnItemPlan plan, IReadOnlyList<InventoryItemSnapshot> inventory)
    {
        foreach (var (name, count) in plan.Required)
        {
            TradingActions.Require(!string.IsNullOrWhiteSpace(name), "需要回程的路径尚未选择回程物品。");
            var quantity = inventory.Where(i => !i.IsEquipped && i.Slot >= 0 && string.Equals(i.Name, name, StringComparison.OrdinalIgnoreCase))
                .Sum(i => (long)i.Count);
            TradingActions.Require(quantity >= count, $"回程物品不足，需要 {count} 个：{name}");
        }
    }

    public static async Task<OperationResult> FollowAsync(AccountWorkerContext context, SharedPathDocument path, BagCleanupPathExecutor execute)
    {
        var index = Math.Max(0, await NearbyIndexAsync(context, path));
        context.Logger.Info("cleanup_path.entry", new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName, ["path"] = path.Name,
            ["startPointNumber"] = index + 1, ["pointCount"] = path.PointCount
        });
        return await execute(context, path.Name, path.Points.Skip(index).Select(p => p.ToVector3()).ToArray());
    }
}
