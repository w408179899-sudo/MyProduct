using Roadhog.Application.Workers;
using Roadhog.Core.Model;
using Roadhog.Core.Paths;

namespace Roadhog.Application.Travel;

public enum TownReturnPhase { WaitingForDeparture, Loading, WaitingForDestination, Arrived, Dead }

/// <summary>Action confirmation only. Scene/player coherence is owned by the snapshot provider.</summary>
public sealed class TownReturnTransition
{
    public const double ArrivalRadius = 35;
    public const double ArrivalHeightTolerance = 20;
    public TownReturnPhase Phase { get; private set; }
    public ChannelTransitionSnapshot? Departure { get; private set; }
    public ChannelTransitionSnapshot? Arrival { get; private set; }
    private ChannelTransitionSnapshot? _observed;
    public SharedPathDocument? Destination { get; private set; }
    public DateTimeOffset StartedAt { get; private set; }
    public bool SawLoading { get; private set; }
    public bool Active => Departure != null && Phase is not (TownReturnPhase.Arrived or TownReturnPhase.Dead);
    private string? _reported;

    public void Start(ChannelTransitionSnapshot departure, SharedPathDocument destination, DateTimeOffset now)
    {
        if (!departure.IsReady || destination.Points.Count == 0)
            throw new InvalidOperationException("回城需要场景中的角色和目标路线入口。");
        Departure = departure;
        Destination = destination.Clone();
        StartedAt = now;
        Arrival = null;
        _observed = departure;
        SawLoading = false;
        _reported = null;
        Phase = TownReturnPhase.WaitingForDeparture;
    }

    public TownReturnPhase Observe(ChannelTransitionSnapshot scene)
    {
        if (!Active) return Phase;
        _observed = scene;
        if (!scene.IsReady)
        {
            SawLoading = true;
            return Phase = TownReturnPhase.Loading;
        }
        if (scene.Player!.IsDead)
        {
            Arrival = scene;
            return Phase = TownReturnPhase.Dead;
        }

        var mapChanged = scene.Channel!.MapId != Departure!.Channel!.MapId;
        var moved = HorizontalDistance(scene.Player.Position!.Value, Departure.Player!.Position!.Value);
        // Accept a missed loading frame only with a completed destination observation and
        // action evidence. No movement input is allowed while this operation owns the worker.
        var departed = SawLoading || mapChanged || moved >= 5;
        if (!departed) return Phase = TownReturnPhase.WaitingForDeparture;
        if (!MatchesDestination(scene, Destination!)) return Phase = TownReturnPhase.WaitingForDestination;
        Arrival = scene;
        return Phase = TownReturnPhase.Arrived;
    }

    public static bool MatchesDestination(ChannelTransitionSnapshot scene, SharedPathDocument path) =>
        scene.IsReady && path.Points.Count > 0 &&
        (path.MapId is not > 0 || path.MapId == scene.Channel!.MapId) &&
        HorizontalDistance(scene.Player!.Position!.Value, path.Points[0].ToVector3()) <= ArrivalRadius &&
        Math.Abs(scene.Player.Position.Value.Z - path.Points[0].Z) <= ArrivalHeightTolerance;

    public async Task<TownReturnPhase> TickAsync(AccountWorkerContext context)
    {
        var pending = context.Snapshots.ReadChannelTransitionAsync();
        // One outstanding request, even while the provider is waiting for its first publication.
        while (!pending.IsCompleted)
        {
            Report(context);
            context.RuntimeStates.MarkHeartbeat(context.Config.AccountName);
            await Task.WhenAny(pending, Task.Delay(200, context.StopToken)).ConfigureAwait(false);
            context.StopToken.ThrowIfCancellationRequested();
        }
        var previous = Phase;
        Observe((await pending.WaitAsync(context.StopToken).ConfigureAwait(false)).Value);
        if (previous != Phase)
            context.Logger.Info("town_return.phase", Fields(context));
        Report(context);
        return Phase;
    }

    private void Report(AccountWorkerContext context)
    {
        if (Phase is TownReturnPhase.Arrived or TownReturnPhase.Dead)
        {
            context.RuntimeStates.ClearWarning(context.Config.AccountName);
            return;
        }
        var elapsed = DateTimeOffset.UtcNow - StartedAt;
        var warning = Phase == TownReturnPhase.WaitingForDestination
            ? "回城后落点与路线入口不符，已停止移动，继续等待正确落点。"
            : SawLoading && elapsed >= TimeSpan.FromSeconds(60)
                ? "回城地图加载较慢，正在等待角色恢复；可停止任务。"
                : !SawLoading && elapsed >= TimeSpan.FromSeconds(30)
                    ? "回城尚未确认，已停止移动，继续观察延迟传送；请检查回城技能或停止任务。"
                    : null;
        if (warning == null || warning == _reported) return;
        _reported = warning;
        context.RuntimeStates.MarkWarning(context.Config.AccountName, warning);
        context.Logger.Warn("town_return.waiting", Fields(context));
    }

    private Dictionary<string, object?> Fields(AccountWorkerContext context) => new()
    {
        ["account"] = context.Config.AccountName, ["phase"] = Phase.ToString(),
        ["pathName"] = Destination?.Name, ["expectedMapId"] = Destination?.MapId,
        ["sourceMapId"] = Departure?.Channel?.MapId, ["observedMapId"] = _observed?.Channel?.MapId,
        ["position"] = _observed?.Player?.Position, ["entry"] = Destination?.Points.FirstOrDefault()?.ToVector3(),
        ["elapsedMs"] = (DateTimeOffset.UtcNow - StartedAt).TotalMilliseconds,
        ["sawLoading"] = SawLoading, ["legacyPathWithoutMap"] = Destination?.MapId is not > 0
    };

    private static double HorizontalDistance(Vector3Snapshot a, Vector3Snapshot b) =>
        Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2));
}
