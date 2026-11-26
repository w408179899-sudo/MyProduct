using Roadhog.Application.SemiAuto;
using Roadhog.Application.Workers;
using Roadhog.Core.Api;
using Roadhog.Core.Model;
using Roadhog.Core.Paths;

namespace Roadhog.Application.Travel;

public enum TownReturnPhase { WaitingForDeparture, Loading, WaitingForDestination, Arrived, Dead }

/// <summary>Action confirmation only. Scene/player coherence is owned by the snapshot provider.</summary>
public sealed class TownReturnTransition
{
    private static readonly TimeSpan SkillReadBudget = TimeSpan.FromMilliseconds(800);
    public static readonly TimeSpan ConfirmationWait = TimeSpan.FromMinutes(1);
    public const int MaximumAttempts = 20;
    public const double ArrivalRadius = 35;
    public const double ArrivalHeightTolerance = 20;
    public TownReturnPhase Phase { get; private set; }
    public ChannelTransitionSnapshot? Departure { get; private set; }
    public ChannelTransitionSnapshot? Arrival { get; private set; }
    private ChannelTransitionSnapshot? _observed;
    private SkillCooldownBaseline? _skillBaseline;
    private bool _confirmedBySkill;
    public SharedPathDocument? Destination { get; private set; }
    public DateTimeOffset StartedAt { get; private set; }
    public DateTimeOffset LastAttemptAt { get; private set; }
    public int AttemptCount { get; private set; }
    public bool SawLoading { get; private set; }
    public bool HasDeparted { get; private set; }
    public bool Active => Departure != null && Phase is not (TownReturnPhase.Arrived or TownReturnPhase.Dead);
    private string? _reported;

    public void Start(ChannelTransitionSnapshot departure, SharedPathDocument destination, DateTimeOffset now)
    {
        if (!departure.IsReady || destination.Points.Count == 0)
            throw new InvalidOperationException("回城需要场景中的角色和目标路线入口。");
        Departure = departure;
        Destination = destination.Clone();
        StartedAt = now;
        LastAttemptAt = now;
        AttemptCount = 1;
        Arrival = null;
        _observed = departure;
        _skillBaseline = null;
        _confirmedBySkill = false;
        SawLoading = false;
        HasDeparted = false;
        _reported = null;
        Phase = TownReturnPhase.WaitingForDeparture;
    }

    internal static async Task<TownReturnTransition> StartRevivePathAsync(
        AccountWorkerContext context, ChannelTransitionSnapshot departure, SharedPathDocument destination,
        string key, DateTimeOffset now)
    {
        var transition = new TownReturnTransition();
        transition.Start(departure, destination, now);

        using var budget = SnapshotReadBudget.Enter(SkillReadBudget);
        try
        {
            var bar = (await context.Snapshots.ReadQuickbarAsync().WaitAsync(context.StopToken).ConfigureAwait(false)).Value;
            var slot = bar.Slots.FirstOrDefault(slot => slot.ContentType == 21 && slot.SkillId != 0 &&
                string.Equals(SkillKeyBindings.GetSlotKey(slot.Bar, slot.Slot), key, StringComparison.OrdinalIgnoreCase));
            if (slot is null) return transition;

            uint[] ids = [slot.SkillId];
            var snapshot = await context.Snapshots.ReadSkillsAsync(ids).WaitAsync(context.StopToken).ConfigureAwait(false);
            var skill = snapshot.Value.FirstOrDefault(skill => skill.SkillId == slot.SkillId);
            if (skill is { Name: "回程", CooldownDuration: > 0 })
                transition._skillBaseline = new(ids, skill.CooldownEndTime, snapshot.Version);
        }
        catch (SnapshotReadBudgetExceededException) when (!context.StopToken.IsCancellationRequested)
        {
            // The scene-based confirmation remains available without a skill baseline.
        }
        return transition;
    }

    // Reserve before sending input: an uncertain input acknowledgement must still count.
    // Only a completed scene observation at the original position permits another key press.
    public bool TryReserveRetry(DateTimeOffset now)
    {
        if (!Active || Phase != TownReturnPhase.WaitingForDeparture || HasDeparted ||
            now - LastAttemptAt < ConfirmationWait || AttemptCount >= MaximumAttempts)
            return false;
        AttemptCount++;
        LastAttemptAt = now;
        return true;
    }

    public TownReturnPhase Observe(ChannelTransitionSnapshot scene)
    {
        if (!Active) return Phase;
        _observed = scene;
        if (!scene.IsReady)
        {
            SawLoading = true;
            HasDeparted = true;
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
        HasDeparted |= mapChanged || moved >= 5;
        if (!HasDeparted) return Phase = TownReturnPhase.WaitingForDeparture;
        if (_confirmedBySkill && (!scene.Player.IsAlive ||
            !string.Equals(scene.Player.CharacterName, Departure.Player.CharacterName, StringComparison.Ordinal)))
            return Phase = TownReturnPhase.WaitingForDestination;
        if (!MatchesDestination(scene, Destination!)) return Phase = TownReturnPhase.WaitingForDestination;
        Arrival = scene;
        return Phase = TownReturnPhase.Arrived;
    }

    public static bool MatchesDestination(ChannelTransitionSnapshot scene, SharedPathDocument path) =>
        scene.IsReady && path.Points.Count > 0 &&
        (path.MapId is not > 0 || path.MapId == scene.Channel!.MapId) &&
        HorizontalDistance(scene.Player!.Position!.Value, path.Points[0].ToVector3()) <= ArrivalRadius &&
        Math.Abs(scene.Player.Position.Value.Z - path.Points[0].Z) <= ArrivalHeightTolerance;

    public async Task<TownReturnPhase> TickAsync(AccountWorkerContext context, CancellationToken cancellationToken = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(context.StopToken, cancellationToken);
        var token = linked.Token;
        token.ThrowIfCancellationRequested();
        if (Active && !HasDeparted && _skillBaseline is { } baseline)
            await ObserveReturnCooldownAsync(context, baseline, token).ConfigureAwait(false);

        var pending = context.Snapshots.ReadChannelTransitionAsync();
        // One outstanding request, even while the provider is waiting for its first publication.
        while (!pending.IsCompleted)
        {
            Report(context);
            context.RuntimeStates.MarkHeartbeat(context.Config.AccountName);
            await Task.WhenAny(pending, Task.Delay(200, token)).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
        }
        var previous = Phase;
        Observe((await pending.WaitAsync(token).ConfigureAwait(false)).Value);
        if (previous != Phase)
            context.Logger.Info("town_return.phase", Fields(context));
        Report(context);
        return Phase;
    }

    private async Task ObserveReturnCooldownAsync(
        AccountWorkerContext context, SkillCooldownBaseline baseline, CancellationToken token)
    {
        using var budget = SnapshotReadBudget.Enter(SkillReadBudget);
        using var cancellation = SnapshotReadBudget.EnterCancellation(token);
        try
        {
            var current = await context.Snapshots.ReadSkillsAsync(baseline.SkillIds, baseline.Version)
                .WaitAsync(token).ConfigureAwait(false);
            var skill = current.Value.FirstOrDefault(skill => skill.SkillId == baseline.SkillIds[0]);
            if (current.Version <= baseline.Version || skill is not { CooldownDuration: > 0, CooldownEndTime: > 0 } ||
                (baseline.EndTime != 0 && unchecked((int)(skill.CooldownEndTime - baseline.EndTime)) <= 0))
                return;

            // Action evidence permits the next scene observation to confirm the destination.
            _confirmedBySkill = true;
            HasDeparted = true;
            context.Logger.Info("town_return.skill_cooldown_advanced", new Dictionary<string, object?>
            {
                ["account"] = context.Config.AccountName, ["pathName"] = Destination?.Name,
                ["skillId"] = skill.SkillId, ["previousCooldownEndTime"] = baseline.EndTime,
                ["cooldownEndTime"] = skill.CooldownEndTime
            });
        }
        catch (SnapshotReadBudgetExceededException) when (!token.IsCancellationRequested)
        {
            // Keep waiting for either a published cooldown change or scene evidence.
        }
    }

    private sealed record SkillCooldownBaseline(uint[] SkillIds, uint EndTime, long Version);

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
                : !SawLoading && elapsed >= ConfirmationWait
                    ? "回城尚未确认，已停止移动，等待回城流程处理。"
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
        ["attempt"] = AttemptCount,
        ["sawLoading"] = SawLoading, ["legacyPathWithoutMap"] = Destination?.MapId is not > 0
    };

    private static double HorizontalDistance(Vector3Snapshot a, Vector3Snapshot b) =>
        Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2));
}
