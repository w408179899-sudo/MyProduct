using Roadhog.Core.Model;

namespace Roadhog.Core.NavigationTests;

public sealed record AStarTestPlan(bool Success, string Message, Vector3Snapshot[] Points,
    double Length, int ExpandedNodes, double StartSnapDistance, double GoalSnapDistance);

public sealed class AStarTestPlanner
{
    public AStarTestPlan Plan(AStarTestMap map, Vector3Snapshot start, Vector3Snapshot goal, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        var a = map.FindNearest(start, token);
        var b = map.FindNearest(goal, token);
        AStarTestPlan Fail(string text, int expanded = 0) => new(false, text, Array.Empty<Vector3Snapshot>(), 0, expanded, 0, 0);
        if (a < 0) return Fail("起点不在导航覆盖范围或对应楼层内。");
        if (b < 0) return Fail("终点不在导航覆盖范围或对应楼层内。");
        var cost = Enumerable.Repeat(double.PositiveInfinity, map.Count).ToArray();
        var previous = Enumerable.Repeat(-1, map.Count).ToArray();
        var queue = new PriorityQueue<(int Index, double Cost), double>();
        cost[a] = 0;
        queue.Enqueue((a, 0), AStarTestMap.Distance(map.Position(a), map.Position(b)));
        var count = 0;
        while (queue.TryDequeue(out var item, out _))
        {
            token.ThrowIfCancellationRequested();
            if (item.Cost != cost[item.Index]) continue;
            if (++count > AStarTestMap.MaximumNodes) return Fail("搜索超出测试节点预算。", count);
            if (item.Index == b)
            {
                var route = new List<Vector3Snapshot>();
                for (var i = b; i != -1; i = previous[i]) route.Add(map.Position(i));
                route.Reverse();
                return new(true, "路线计算完成。", route.ToArray(), cost[b], count,
                    AStarTestMap.Distance(start, map.Position(a)), AStarTestMap.Distance(goal, map.Position(b)));
            }
            foreach (var next in map.Neighbors(item.Index))
            {
                var candidate = item.Cost + AStarTestMap.Distance(map.Position(item.Index), map.Position(next));
                if (candidate >= cost[next]) continue;
                cost[next] = candidate;
                previous[next] = item.Index;
                queue.Enqueue((next, candidate), candidate + AStarTestMap.Distance(map.Position(next), map.Position(b)));
            }
        }
        return Fail("起终点之间没有已连接的路线。", count);
    }
}
