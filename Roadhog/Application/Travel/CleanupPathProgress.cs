using Roadhog.Application.Workers;

namespace Roadhog.Application.Travel;

/// <summary>Bounds an unreached cleanup waypoint, including repeated detours without arrival.</summary>
internal sealed class CleanupPathProgress
{
    private int point = -1;
    private double bestDistance;
    private TimeSpan enteredAt, progressedAt;

    public void Observe(int pointIndex, double distance, TimeSpan elapsed, string pathName)
    {
        if (point != pointIndex)
        {
            point = pointIndex;
            bestDistance = distance;
            enteredAt = progressedAt = elapsed;
        }
        else if (distance <= bestDistance - .5)
        {
            bestDistance = distance;
            progressedAt = elapsed;
        }
        if (elapsed - progressedAt >= TimeSpan.FromSeconds(60) || elapsed - enteredAt >= TimeSpan.FromMinutes(5))
            throw new CleanupExecutionFailedException($"清包路径 {pathName} 第 {pointIndex + 1} 点长时间未到达，距离 {distance:0.0} 米；放弃本次清包，返回主流程。", new TimeoutException());
    }
}
