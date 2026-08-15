using Roadhog.Application.Workers;
using Roadhog.Core.Api;
using Roadhog.Core.Common;
using Roadhog.Core.Input;
using Roadhog.Core.Model;

namespace Roadhog.Application.Team;

public sealed class TacticalMarkCoordinator
{
    private static readonly TimeSpan LeaderVerifyInterval = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan LeaderRetryInterval = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan SelectionInitialConfirmDelay = TimeSpan.FromMilliseconds(50);
    private static readonly TimeSpan SelectionConfirmRetryDelay = TimeSpan.FromMilliseconds(80);
    private const int SelectionConfirmPolls = 3;

    private readonly IKeyboardInput _keyboard;

    public TacticalMarkCoordinator(IKeyboardInput keyboard)
    {
        _keyboard = keyboard;
    }

    public async Task MaintainLeaderTargetMarkAsync(
        AccountWorkerContext context,
        LeaderTacticalMarkState state,
        LockedTargetSnapshot target,
        string markKey,
        TimeSpan keyHold)
    {
        if (!IsStrictlyLivingMonster(target) || target.ServerObjectId == 0)
        {
            state.Reset();
            return;
        }

        var now = DateTimeOffset.Now;
        if (state.TargetServerObjectId != target.ServerObjectId)
        {
            state.Start(target.ServerObjectId);
            await PressLeaderMarkKeyAsync(context, state, target, markKey, keyHold, "new_target")
                .ConfigureAwait(false);
            return;
        }

        if (now - state.LastVerifyAt < LeaderVerifyInterval)
        {
            return;
        }

        state.MarkVerifiedAt(now);
        var signs = await ReadTacticsSignsAsync(context).ConfigureAwait(false);

        if (signs.Contains(target.ServerObjectId))
        {
            if (!state.Verified)
            {
                state.MarkVerified();
                context.Logger.Info("team_leader.tactical_mark.verified", new Dictionary<string, object?>
                {
                    ["account"] = context.Config.AccountName,
                    ["targetServerObjectId"] = target.ServerObjectId,
                    ["targetName"] = target.Name
                });
            }

            return;
        }

        if (state.Verified)
        {
            state.MarkLost();
            context.Logger.Warn("team_leader.tactical_mark.lost", new Dictionary<string, object?>
            {
                ["account"] = context.Config.AccountName,
                ["targetServerObjectId"] = target.ServerObjectId,
                ["targetName"] = target.Name
            });
        }

        if (now - state.LastPressAt >= LeaderRetryInterval)
        {
            await PressLeaderMarkKeyAsync(context, state, target, markKey, keyHold, "verify_failed")
                .ConfigureAwait(false);
        }
    }

    public async Task<TacticalMarkedTargetSelectionResult> TrySelectMarkedTargetAsync(
        AccountWorkerContext context,
        string selectKey,
        TimeSpan keyHold)
    {
        var signs = await ReadTacticsSignsAsync(context).ConfigureAwait(false);
        var activeSignCount = signs.ServerObjectIds.Count(serverObjectId => serverObjectId != 0);
        if (activeSignCount == 0)
        {
            return TacticalMarkedTargetSelectionResult.NotSelected(
                TacticalMarkedTargetSelectionStatus.NoActiveSign,
                null);
        }

        var pressResult = await _keyboard
            .PressKeyAsync(selectKey, keyHold, context.StopToken)
            .ConfigureAwait(false);
        if (!pressResult.Success)
        {
            return TacticalMarkedTargetSelectionResult.NotSelected(
                TacticalMarkedTargetSelectionStatus.InputFailed,
                pressResult.Error,
                activeSignCount);
        }

        await Task.Delay(SelectionInitialConfirmDelay, context.StopToken).ConfigureAwait(false);
        LockedTargetSnapshot? lastTarget = null;
        for (var poll = 1; poll <= SelectionConfirmPolls; poll++)
        {
            if (poll > 1)
            {
                await Task.Delay(SelectionConfirmRetryDelay, context.StopToken).ConfigureAwait(false);
            }

            lastTarget = await ReadLockedTargetAsync(context).ConfigureAwait(false);
            if (IsStrictlyLivingMonster(lastTarget))
            {
                return TacticalMarkedTargetSelectionResult.Selected(
                    lastTarget,
                    activeSignCount,
                    poll);
            }
        }

        return new TacticalMarkedTargetSelectionResult(
            TacticalMarkedTargetSelectionStatus.TargetNotLivingMonster,
            lastTarget,
            activeSignCount,
            SelectionConfirmPolls,
            null);
    }

    public static bool IsStrictlyLivingMonster(LockedTargetSnapshot? target)
    {
        return target is
        {
            IsLockedMonster: true,
            CurrentHp: > 0
        };
    }

    private async Task PressLeaderMarkKeyAsync(
        AccountWorkerContext context,
        LeaderTacticalMarkState state,
        LockedTargetSnapshot target,
        string markKey,
        TimeSpan keyHold,
        string reason)
    {
        var result = await _keyboard
            .PressKeyAsync(markKey, keyHold, context.StopToken)
            .ConfigureAwait(false);
        if (!result.Success)
        {
            context.Logger.Warn("team_leader.tactical_mark.input_failed", new Dictionary<string, object?>
            {
                ["account"] = context.Config.AccountName,
                ["targetServerObjectId"] = target.ServerObjectId,
                ["targetName"] = target.Name,
                ["key"] = markKey,
                ["reason"] = reason,
                ["error"] = result.Error
            });
            return;
        }

        state.MarkPressed(DateTimeOffset.Now);
        context.Logger.Info("team_leader.tactical_mark.pressed", new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["targetServerObjectId"] = target.ServerObjectId,
            ["targetName"] = target.Name,
            ["key"] = markKey,
            ["reason"] = reason
        });
    }

    private static async Task<TacticsSignSnapshot> ReadTacticsSignsAsync(AccountWorkerContext context) =>
        (await context.Snapshots.ReadCurrentTacticsSignsAsync().ConfigureAwait(false)).Value;

    private static async Task<LockedTargetSnapshot> ReadLockedTargetAsync(AccountWorkerContext context) =>
        (await context.Snapshots.ReadCurrentLockedTargetAsync().ConfigureAwait(false)).Value;
}

public sealed class LeaderTacticalMarkState
{
    public uint TargetServerObjectId { get; private set; }

    public bool Verified { get; private set; }

    public DateTimeOffset LastPressAt { get; private set; } = DateTimeOffset.MinValue;

    public DateTimeOffset LastVerifyAt { get; private set; } = DateTimeOffset.MinValue;

    public DateTimeOffset LastWarningAt { get; set; } = DateTimeOffset.MinValue;

    public void Start(uint targetServerObjectId)
    {
        Reset();
        TargetServerObjectId = targetServerObjectId;
    }

    public void MarkPressed(DateTimeOffset pressedAt)
    {
        LastPressAt = pressedAt;
    }

    public void MarkVerifiedAt(DateTimeOffset verifiedAt)
    {
        LastVerifyAt = verifiedAt;
    }

    public void MarkVerified()
    {
        Verified = true;
    }

    public void MarkLost()
    {
        Verified = false;
    }

    public void Reset()
    {
        TargetServerObjectId = 0;
        Verified = false;
        LastPressAt = DateTimeOffset.MinValue;
        LastVerifyAt = DateTimeOffset.MinValue;
        LastWarningAt = DateTimeOffset.MinValue;
    }
}

public enum TacticalMarkedTargetSelectionStatus
{
    Selected,
    NoActiveSign,
    SignReadFailed,
    InputFailed,
    TargetNotLivingMonster
}

public sealed record TacticalMarkedTargetSelectionResult(
    TacticalMarkedTargetSelectionStatus Status,
    LockedTargetSnapshot? LockedTarget,
    int ActiveSignCount,
    int PollCount,
    string? Error)
{
    public bool Accepted => Status == TacticalMarkedTargetSelectionStatus.Selected;

    public static TacticalMarkedTargetSelectionResult Selected(
        LockedTargetSnapshot lockedTarget,
        int activeSignCount,
        int pollCount)
    {
        return new TacticalMarkedTargetSelectionResult(
            TacticalMarkedTargetSelectionStatus.Selected,
            lockedTarget,
            activeSignCount,
            pollCount,
            null);
    }

    public static TacticalMarkedTargetSelectionResult NotSelected(
        TacticalMarkedTargetSelectionStatus status,
        string? error,
        int activeSignCount = 0)
    {
        return new TacticalMarkedTargetSelectionResult(
            status,
            null,
            activeSignCount,
            0,
            error);
    }
}
