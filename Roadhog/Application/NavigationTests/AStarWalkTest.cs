using Roadhog.Core.Common;
using Roadhog.Core.Model;
using Roadhog.Core.NavigationTests;

namespace Roadhog.Application.NavigationTests;

public sealed record AStarWalkTestRequest(uint MapId, string MapName, Vector3Snapshot[] Points);

public interface IAStarTestMovement
{
    Task<ChannelTransitionSnapshot> ReadSceneAsync(CancellationToken token);
    Task MoveStepAsync(PlayerSnapshot player, Vector3Snapshot target, double reachDistance, CancellationToken token);
    Task StopAsync();
}

/// <summary>One explicit test route, separate from combat/cleanup. Never skips to a nearby later waypoint.</summary>
public sealed class AStarWalkTest(TimeProvider? timeProvider = null)
{
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    public const double ReachDistance = 0.35;
    public static readonly TimeSpan MaximumDuration = TimeSpan.FromMinutes(30);

    public async Task<OperationResult> RunAsync(AStarWalkTestRequest request, IAStarTestMovement movement,
        IProgress<string>? progress, CancellationToken token)
    {
        if (request.MapId == 0 || request.Points is not { Length: > 0 and <= 20_000 } ||
            request.Points.Any(p => !AStarTestMap.IsFinite(p)))
            return OperationResult.Fail("测试路线无效。");
        // Snapshot caller-owned data before the first await.
        var points = request.Points.ToArray();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(MaximumDuration);
        var ct = timeout.Token;
        var index = 0;
        var best = double.PositiveInfinity;
        var lastProgress = _time.GetTimestamp();
        var lastReport = _time.GetTimestamp();
        int? channel = null;
        try
        {
            while (index < points.Length)
            {
                ct.ThrowIfCancellationRequested();
                var scene = await movement.ReadSceneAsync(ct).ConfigureAwait(false);
                if (!scene.IsReady || scene.Channel!.MapId != request.MapId)
                    return OperationResult.Fail("已离开测试地图，停止行走。");
                var player = scene.Player!;
                if (player.IsDead) return OperationResult.Fail("角色死亡，停止测试。");
                if (channel is { } old && old != scene.Channel.Index)
                    return OperationResult.Fail("频道已变化，停止测试。");
                var pos = player.Position!.Value;
                if (channel is null)
                {
                    if (AStarTestMap.HorizontalDistance(pos, points[0]) > 1.5 || Math.Abs(pos.Z - points[0].Z) > 1)
                        return OperationResult.Fail("角色已离开路线起点，请读取当前位置并重新计算。");
                    channel = scene.Channel.Index;
                    progress?.Report($"开始行走，共 {points.Length} 个路线点。");
                }
                var target = points[index];
                var horizontal = AStarTestMap.HorizontalDistance(pos, target);
                var height = Math.Abs(pos.Z - target.Z);
                if (horizontal <= ReachDistance && height <= 0.75)
                {
                    // Stop at every node; no corner cutting through a smoothed diagonal.
                    await movement.StopAsync().ConfigureAwait(false);
                    index++;
                    best = double.PositiveInfinity;
                    lastProgress = _time.GetTimestamp();
                    progress?.Report($"已到达 {index}/{points.Length}，坐标 {pos.X:F2}, {pos.Y:F2}, {pos.Z:F2}");
                    continue;
                }
                var distance = AStarTestMap.Distance(pos, target);
                if (_time.GetElapsedTime(lastReport) >= TimeSpan.FromSeconds(1))
                {
                    progress?.Report($"路线点 {index + 1}/{points.Length}：当前位置 {pos.X:F2}, {pos.Y:F2}, {pos.Z:F2}；距目标 {horizontal:F2} 米，高差 {height:F2} 米。");
                    lastReport = _time.GetTimestamp();
                }
                if (distance < best - 0.1) { best = distance; lastProgress = _time.GetTimestamp(); }
                if (_time.GetElapsedTime(lastProgress) >= TimeSpan.FromSeconds(10))
                    return OperationResult.Fail($"路线点 {index + 1} 持续 10 秒没有接近，测试已停止。剩余水平距离 {horizontal:F2} 米，高差 {height:F2} 米。");
                if (height > 3) return OperationResult.Fail("角色与路线高度偏差过大，停止测试。");
                if (index > 0 && DistanceToSegment(pos, points[index - 1], target) > 2)
                    return OperationResult.Fail("角色偏离路线超过 2 米，停止测试。");
                await movement.MoveStepAsync(player, target, ReachDistance, ct).ConfigureAwait(false);
                await Task.Delay(TimeSpan.FromMilliseconds(40), _time, ct).ConfigureAwait(false);
            }
            return OperationResult.Ok();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return OperationResult.Fail(token.IsCancellationRequested ? "A* 行走测试已停止。" : "A* 行走测试超过 30 分钟，已停止。");
        }
        finally { await movement.StopAsync().ConfigureAwait(false); }
    }

    private static double DistanceToSegment(Vector3Snapshot p, Vector3Snapshot a, Vector3Snapshot b)
    {
        var dx = (double)b.X - a.X; var dy = (double)b.Y - a.Y;
        var length = dx * dx + dy * dy;
        var t = length == 0 ? 0 : Math.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / length, 0, 1);
        return Math.Sqrt(Math.Pow(p.X - a.X - t * dx, 2) + Math.Pow(p.Y - a.Y - t * dy, 2));
    }
}
