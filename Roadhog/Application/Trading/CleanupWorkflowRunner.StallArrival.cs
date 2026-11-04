using Roadhog.Application.Travel;
using Roadhog.Application.Workers;
using Roadhog.Core.Paths;
using static Roadhog.Application.Trading.TradingActions;

namespace Roadhog.Application.Trading;

public sealed partial class CleanupWorkflowRunner
{
    private async Task FollowGroceryStallPathAsync(AccountWorkerContext context, SharedPathDocument route)
    {
        const double finalReachDistance = 1;
        const double arrivalDistance = 3;
        Check(await CleanupPathTravel.FollowAsync(context, route, executePath, finalReachDistance: finalReachDistance));
        context.StopToken.ThrowIfCancellationRequested();
        await new TradingActions(input, context.Snapshots, context.StopToken, groceryDelay).Reset();
        var scene = (await context.Snapshots.ReadChannelTransitionAsync().WaitAsync(context.StopToken)).Value;
        Require(scene.IsReady && !scene.Player!.IsDead && scene.Channel!.MapId == route.MapId,
            "摆摊终点所在场景尚未确认，放弃本次摆摊。");
        var position = scene.Player!.Position!.Value;
        var endpoint = route.Points[^1];
        var distance = Math.Sqrt(Math.Pow(position.X - endpoint.X, 2) + Math.Pow(position.Y - endpoint.Y, 2));
        var height = Math.Abs(position.Z - endpoint.Z);
        Require(distance <= arrivalDistance && height <= NearbyPathEntry.HeightTolerance,
            $"尚未到达摆摊路径最后一点（距离 {distance:0.00} 米，高差 {height:0.00} 米），放弃本次摆摊。");
        context.Logger.Info("grocery_shop.stall.arrived", new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName, ["path"] = route.Name, ["pointNumber"] = route.PointCount,
            ["distance"] = distance, ["reachDistance"] = arrivalDistance
        });
    }
}
