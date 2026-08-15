using Roadhog.Application.StationaryCombat;
using Roadhog.Application.Workers;
using Roadhog.Core.Accounts;
using Roadhog.Core.Api;
using Roadhog.Core.Common;
using Roadhog.Core.Input;
using Roadhog.Core.Model;
using Roadhog.Core.Paths;

namespace Roadhog.Application.Channels;

public sealed class FixedChannelController
{
    public const double RevivalPointRadiusMeters = 20.0D;

    public static readonly TimeSpan InitialSwitchWait = TimeSpan.FromMinutes(5) + TimeSpan.FromSeconds(10);

    public static readonly TimeSpan SwitchVerificationWindow = TimeSpan.FromSeconds(30);

    private static readonly TimeSpan ActivePollInterval = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan NormalPollInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan ReturnRetryInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ReturnKeyHoldDuration = TimeSpan.FromMilliseconds(35);
    private readonly IKeyboardInput _input;
    private readonly ISharedPathStore _pathStore;
    private readonly IFixedChannelSwitchExecutor _switchExecutor;
    private readonly TimeProvider _timeProvider;

    public FixedChannelController(
        IKeyboardInput input,
        ISharedPathStore pathStore,
        IFixedChannelSwitchExecutor? switchExecutor = null,
        TimeProvider? timeProvider = null)
    {
        _input = input;
        _pathStore = pathStore;
        _switchExecutor = switchExecutor ?? new PendingFixedChannelSwitchExecutor();
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<TimeSpan?> TickAsync(
        AccountWorkerContext context,
        ScriptSettings settings,
        FixedChannelState state,
        StationaryCombatState combatState,
        Func<Task> suspendNormalWorkAsync)
    {
        var now = _timeProvider.GetUtcNow();
        var targetChannelNumber = settings.FixedChannelNumber;
        if (targetChannelNumber == 0)
        {
            if (state.CorrectionActive || state.NormalWorkSuspended)
            {
                state.Reset(DateTimeOffset.MinValue);
            }

            return null;
        }

        if (targetChannelNumber is < ScriptSettings.MinimumFixedChannelNumber or > ScriptSettings.MaximumFixedChannelNumber)
        {
            await EnsureNormalWorkSuspendedAsync(state, suspendNormalWorkAsync).ConfigureAwait(false);
            LogOnce(context, state, "invalid_config", "fixed_channel.config.invalid", new Dictionary<string, object?>
            {
                ["account"] = context.Config.AccountName,
                ["targetChannelNumber"] = targetChannelNumber,
                ["minimum"] = ScriptSettings.MinimumFixedChannelNumber,
                ["maximum"] = ScriptSettings.MaximumFixedChannelNumber
            });
            return ActivePollInterval;
        }

        if (now < state.NextChannelReadAt)
        {
            return state.CorrectionActive || state.NormalWorkSuspended
                ? ActivePollInterval
                : null;
        }

        var channel = await ReadChannelAsync(context).ConfigureAwait(false);
        state.NextChannelReadAt = now + (state.CorrectionActive ? ActivePollInterval : NormalPollInterval);

        if (channel.Number == targetChannelNumber &&
            (state.Step != FixedChannelCorrectionStep.VerifyingSwitch ||
             (channel.MapId == state.SwitchAttemptMapId && channel.CapturedAt >= state.SwitchAttemptStartedAt)))
        {
            CompleteCorrection(context, settings, state, combatState, channel, now);
            return null;
        }

        if (!state.CorrectionActive)
        {
            var player = await ReadPlayerAsync(context).ConfigureAwait(false);
            if (player.IsDead)
            {
                return null;
            }

            var revivePathName = settings.Paths?.RevivePathName?.Trim() ?? string.Empty;
            state.BeginCorrection(revivePathName, Array.Empty<Vector3Snapshot>());
            state.NextChannelReadAt = now + ActivePollInterval;
            await EnsureNormalWorkSuspendedAsync(state, suspendNormalWorkAsync).ConfigureAwait(false);
            context.Logger.Warn("fixed_channel.correction.started", new Dictionary<string, object?>
            {
                ["account"] = context.Config.AccountName,
                ["currentChannelNumber"] = channel.Number,
                ["targetChannelNumber"] = targetChannelNumber,
                ["channelCount"] = channel.Count,
                ["mapId"] = channel.MapId,
                ["revivalPointRadiusMeters"] = RevivalPointRadiusMeters
            });
        }

        if (!await EnsureRevivePathAsync(context, state).ConfigureAwait(false))
        {
            return ActivePollInterval;
        }

        return state.Step switch
        {
            FixedChannelCorrectionStep.ReturningToRevivalPoint => await TickReturnToRevivalPointAsync(
                    context,
                    settings,
                    state,
                    channel,
                    now)
                .ConfigureAwait(false),
            FixedChannelCorrectionStep.WaitingBeforeSwitch => await TickInitialWaitAsync(
                    context,
                    settings,
                    state,
                    channel,
                    now)
                .ConfigureAwait(false),
            FixedChannelCorrectionStep.VerifyingSwitch => await TickSwitchVerificationAsync(
                    context,
                    settings,
                    state,
                    channel,
                    now)
                .ConfigureAwait(false),
            _ => ActivePollInterval
        };
    }

    private async Task<TimeSpan?> TickReturnToRevivalPointAsync(
        AccountWorkerContext context,
        ScriptSettings settings,
        FixedChannelState state,
        ChannelSnapshot channel,
        DateTimeOffset now)
    {
        var player = await ReadPlayerAsync(context).ConfigureAwait(false);
        if (player.IsDead)
        {
            LogOnce(context, state, "return_player_dead", "fixed_channel.return.deferred_for_death", new Dictionary<string, object?>
            {
                ["account"] = context.Config.AccountName
            });
            return null;
        }

        var distance = HorizontalDistance(player.Position!.Value, state.RevivePoints[0]);
        if (distance <= RevivalPointRadiusMeters)
        {
            state.EnterInitialWait(now, InitialSwitchWait, channel.MapId);
            context.Logger.Info("fixed_channel.wait.started", new Dictionary<string, object?>
            {
                ["account"] = context.Config.AccountName,
                ["targetChannelNumber"] = settings.FixedChannelNumber,
                ["distanceToRevivalPoint"] = Math.Round(distance, 2),
                ["revivalPointRadiusMeters"] = RevivalPointRadiusMeters,
                ["waitSeconds"] = InitialSwitchWait.TotalSeconds,
                ["mapId"] = channel.MapId,
                ["initialWaitCompleted"] = state.InitialWaitCompleted
            });
            return ActivePollInterval;
        }

        if (now < state.NextReturnAttemptAt)
        {
            return ActivePollInterval;
        }

        var returnKey = settings.Paths?.TownReturnKey?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(returnKey))
        {
            LogOnce(context, state, "return_key_missing", "fixed_channel.return.blocked", new Dictionary<string, object?>
            {
                ["account"] = context.Config.AccountName,
                ["reason"] = "town_return_key_missing",
                ["distanceToRevivalPoint"] = Math.Round(distance, 2),
                ["revivalPointRadiusMeters"] = RevivalPointRadiusMeters
            });
            return ActivePollInterval;
        }

        var press = await _input
            .PressKeyAsync(returnKey, ReturnKeyHoldDuration, context.StopToken)
            .ConfigureAwait(false);
        state.MarkReturnAttempt(now + ReturnRetryInterval);
        var fields = new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["key"] = returnKey,
            ["distanceToRevivalPoint"] = Math.Round(distance, 2),
            ["revivalPointRadiusMeters"] = RevivalPointRadiusMeters,
            ["retrySeconds"] = ReturnRetryInterval.TotalSeconds
        };
        if (press.Success)
        {
            context.Logger.Warn("fixed_channel.return.pressed", fields);
        }
        else
        {
            fields["error"] = press.Error;
            context.Logger.Warn("fixed_channel.return.press_failed", fields);
        }

        return ActivePollInterval;
    }

    private async Task<TimeSpan?> TickInitialWaitAsync(
        AccountWorkerContext context,
        ScriptSettings settings,
        FixedChannelState state,
        ChannelSnapshot channel,
        DateTimeOffset now)
    {
        var player = await ReadPlayerAsync(context).ConfigureAwait(false);
        if (player.IsDead)
        {
            state.LeaveRevivalPoint();
            return null;
        }

        var distance = HorizontalDistance(player.Position!.Value, state.RevivePoints[0]);
        if (distance > RevivalPointRadiusMeters)
        {
            state.LeaveRevivalPoint();
            context.Logger.Warn("fixed_channel.wait.left_revival_point", new Dictionary<string, object?>
            {
                ["account"] = context.Config.AccountName,
                ["distanceToRevivalPoint"] = Math.Round(distance, 2),
                ["revivalPointRadiusMeters"] = RevivalPointRadiusMeters
            });
            return ActivePollInterval;
        }

        if (channel.MapId != state.WaitingMapId)
        {
            var previousMapId = state.WaitingMapId;
            state.RestartInitialWait(now, InitialSwitchWait, channel.MapId);
            context.Logger.Warn("fixed_channel.wait.map_changed", new Dictionary<string, object?>
            {
                ["account"] = context.Config.AccountName,
                ["previousMapId"] = previousMapId,
                ["currentMapId"] = channel.MapId,
                ["waitSeconds"] = InitialSwitchWait.TotalSeconds
            });
            return ActivePollInterval;
        }

        if (settings.FixedChannelNumber > channel.Count)
        {
            LogTargetUnavailable(context, state, settings.FixedChannelNumber, channel);
            return ActivePollInterval;
        }

        if (now < state.InitialWaitUntil)
        {
            return ActivePollInterval;
        }

        return await ExecuteSwitchAttemptAsync(context, settings, state, channel).ConfigureAwait(false);
    }

    private async Task<TimeSpan?> TickSwitchVerificationAsync(
        AccountWorkerContext context,
        ScriptSettings settings,
        FixedChannelState state,
        ChannelSnapshot channel,
        DateTimeOffset now)
    {
        var player = await ReadPlayerAsync(context).ConfigureAwait(false);
        if (player.IsDead)
        {
            state.LeaveRevivalPoint();
            return null;
        }
        else
        {
            var distance = HorizontalDistance(player.Position!.Value, state.RevivePoints[0]);
            if (distance > RevivalPointRadiusMeters)
            {
                state.LeaveRevivalPoint();
                return ActivePollInterval;
            }
        }

        if (now < state.SwitchVerificationDeadline)
        {
            return ActivePollInterval;
        }

        context.Logger.Warn("fixed_channel.switch.verify_timeout", new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["attemptNumber"] = state.SwitchAttemptCount,
            ["currentChannelNumber"] = channel.Number,
            ["targetChannelNumber"] = settings.FixedChannelNumber,
            ["attemptMapId"] = state.SwitchAttemptMapId,
            ["currentMapId"] = channel.MapId,
            ["verificationSeconds"] = SwitchVerificationWindow.TotalSeconds
        });

        if (settings.FixedChannelNumber > channel.Count)
        {
            LogTargetUnavailable(context, state, settings.FixedChannelNumber, channel);
            return ActivePollInterval;
        }

        return await ExecuteSwitchAttemptAsync(context, settings, state, channel).ConfigureAwait(false);
    }

    private async Task<TimeSpan> ExecuteSwitchAttemptAsync(
        AccountWorkerContext context,
        ScriptSettings settings,
        FixedChannelState state,
        ChannelSnapshot channel)
    {
        var attemptNumber = state.SwitchAttemptCount + 1;
        OperationResult result;
        try
        {
            result = await _switchExecutor
                .ExecuteAsync(
                    new FixedChannelSwitchRequest(
                        context.Config.AccountName,
                        settings.FixedChannelNumber,
                        channel.MapId,
                        attemptNumber,
                        FixedChannelClickPlan.FromSettings(settings.FixedChannelMouse)),
                    context.StopToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (context.StopToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            result = OperationResult.Fail("Fixed-channel switch action threw: " + ex.Message);
        }

        var verificationStartedAt = _timeProvider.GetUtcNow();
        state.StartSwitchAttempt(verificationStartedAt, SwitchVerificationWindow, channel.MapId);

        var fields = new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["attemptNumber"] = attemptNumber,
            ["currentChannelNumber"] = channel.Number,
            ["targetChannelNumber"] = settings.FixedChannelNumber,
            ["channelCount"] = channel.Count,
            ["mapId"] = channel.MapId,
            ["clickCount"] = FixedChannelClickPlan.OrderedSteps.Count,
            ["verificationStartedAt"] = verificationStartedAt,
            ["verificationSeconds"] = SwitchVerificationWindow.TotalSeconds
        };
        if (result.Success)
        {
            context.Logger.Warn("fixed_channel.switch.executed", fields);
        }
        else
        {
            fields["error"] = result.Error;
            context.Logger.Warn("fixed_channel.switch.execute_failed", fields);
        }

        return ActivePollInterval;
    }

    private async Task<bool> EnsureRevivePathAsync(AccountWorkerContext context, FixedChannelState state)
    {
        if (state.RevivePoints.Count >= 2)
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(state.RevivePathName))
        {
            LogOnce(context, state, "revive_path_name_missing", "fixed_channel.path.blocked", new Dictionary<string, object?>
            {
                ["account"] = context.Config.AccountName,
                ["reason"] = "revive_path_name_missing"
            });
            return false;
        }

        var pathResult = await _pathStore
            .LoadAsync(state.RevivePathName, context.StopToken)
            .ConfigureAwait(false);
        if (!pathResult.Success || pathResult.Value?.Points is not { Count: >= 2 } points)
        {
            LogOnce(context, state, "revive_path_unavailable:" + pathResult.Error, "fixed_channel.path.blocked", new Dictionary<string, object?>
            {
                ["account"] = context.Config.AccountName,
                ["reason"] = "revive_path_unavailable",
                ["pathName"] = state.RevivePathName,
                ["pointCount"] = pathResult.Value?.PointCount ?? 0,
                ["error"] = pathResult.Error
            });
            return false;
        }

        state.SetRevivePath(
            state.RevivePathName,
            points.Select(point => point.ToVector3()).ToArray());
        return true;
    }

    private static void CompleteCorrection(
        AccountWorkerContext context,
        ScriptSettings settings,
        FixedChannelState state,
        StationaryCombatState combatState,
        ChannelSnapshot channel,
        DateTimeOffset now)
    {
        var wasActive = state.CorrectionActive;
        var reachedRevivalPoint = state.ReachedRevivalPoint;
        var revivePathName = state.RevivePathName;
        var revivePoints = state.RevivePoints;
        var attemptCount = state.SwitchAttemptCount;
        state.Reset(now + NormalPollInterval);

        if (!wasActive)
        {
            return;
        }

        if (reachedRevivalPoint &&
            combatState.TopLevelState != StationaryCombatTopLevelState.DeathRecovery &&
            settings.MainMode == AccountMainMode.CustomCombat &&
            settings.CombatMode is AccountCombatMode.Stationary or AccountCombatMode.Path &&
            revivePoints.Count >= 2)
        {
            combatState.StartStartupRecovery(revivePathName, revivePoints, 0);
            combatState.ReturningHome = false;
            combatState.ClearTarget();
        }

        context.Logger.Info("fixed_channel.switch.verify_ok", new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["channelNumber"] = channel.Number,
            ["channelIndex"] = channel.Index,
            ["channelCount"] = channel.Count,
            ["mapId"] = channel.MapId,
            ["attemptCount"] = attemptCount,
            ["resumeFromRevivalPath"] = reachedRevivalPoint && revivePoints.Count >= 2
        });
    }

    private static void LogTargetUnavailable(
        AccountWorkerContext context,
        FixedChannelState state,
        int targetChannelNumber,
        ChannelSnapshot channel)
    {
        LogOnce(context, state, "target_unavailable:" + targetChannelNumber + ":" + channel.Count + ":" + channel.MapId, "fixed_channel.target_unavailable", new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["targetChannelNumber"] = targetChannelNumber,
            ["channelCount"] = channel.Count,
            ["mapId"] = channel.MapId
        });
    }

    private static async Task EnsureNormalWorkSuspendedAsync(
        FixedChannelState state,
        Func<Task> suspendNormalWorkAsync)
    {
        if (state.MarkNormalWorkSuspended())
        {
            await suspendNormalWorkAsync().ConfigureAwait(false);
        }
    }

    private static void LogOnce(
        AccountWorkerContext context,
        FixedChannelState state,
        string diagnosticKey,
        string eventName,
        IReadOnlyDictionary<string, object?> fields)
    {
        if (state.ShouldLog(diagnosticKey))
        {
            context.Logger.Warn(eventName, fields);
        }
    }

    private static async Task<ChannelSnapshot> ReadChannelAsync(AccountWorkerContext context) =>
        (await context.Snapshots.ReadCurrentChannelAsync().ConfigureAwait(false)).Value;

    private static async Task<PlayerSnapshot> ReadPlayerAsync(AccountWorkerContext context) =>
        (await context.Snapshots.ReadCurrentPlayerAsync().ConfigureAwait(false)).Value;

    private static double HorizontalDistance(Vector3Snapshot left, Vector3Snapshot right)
    {
        var dx = left.X - right.X;
        var dy = left.Y - right.Y;
        return Math.Sqrt((dx * dx) + (dy * dy));
    }
}
