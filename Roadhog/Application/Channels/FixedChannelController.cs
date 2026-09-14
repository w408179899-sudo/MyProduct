using Roadhog.Application.StationaryCombat;
using Roadhog.Application.Workers;
using Roadhog.Core.Accounts;
using Roadhog.Core.Common;

namespace Roadhog.Application.Channels;

public sealed class FixedChannelController(IFixedChannelSwitchExecutor switchExecutor, TimeProvider? timeProvider = null)
{
    public static readonly TimeSpan RequiredPeaceDuration = TimeSpan.FromSeconds(15);
    public static readonly TimeSpan RetryInterval = TimeSpan.FromMinutes(1);
    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;

    // Null returns control to normal life guard, combat, loot and path work.
    public async Task<TimeSpan?> TickAsync(AccountWorkerContext context, ScriptSettings settings,
        FixedChannelState state, StationaryCombatState combatState, Func<Task> prepareMouseAsync)
    {
        var target = settings.FixedChannelNumber;
        if (target is < ScriptSettings.MinimumFixedChannelNumber or > ScriptSettings.MaximumFixedChannelNumber || target == 0)
        {
            state.Reset();
            return null;
        }
        if (target != state.TargetChannelNumber) state.Reset();
        if (state.Completed) return null;
        var now = _clock.GetUtcNow();
        if (ChannelSwitchSafety.IsWorkingOnCombat(combatState)) state.BreakPeace();
        if (now < state.NextChannelReadAt) return null;
        // Channel location is inspected at startup and once per retry interval only.
        // Combat observation retains its existing cadence while the task is pending.
        if (!state.LocationObserved || now >= state.NextLocationReadAt)
        {
            var channel = (await context.Snapshots.ReadChannelAsync().ConfigureAwait(false)).Value;
            now = _clock.GetUtcNow();
            state.ObserveLocation(target, channel.MapId, channel.Number);
            state.NextLocationReadAt = now + RetryInterval;
            if (channel.Number == target)
            {
                state.Complete();
                context.Logger.Info("fixed_channel.target_confirmed", new Dictionary<string, object?>
                {
                    ["account"] = context.Config.AccountName, ["channelNumber"] = channel.Number,
                    ["mapId"] = channel.MapId, ["attemptCount"] = state.SwitchAttemptCount,
                    ["stopChannelReads"] = true
                });
                return null;
            }
            if (target > channel.Count)
            {
                state.CancelWaitingForPeace();
                state.NextChannelReadAt = now + RetryInterval;
                return null;
            }
        }
        if (now >= state.NextAttemptAt && state.BeginWaitingForPeace())
            context.Logger.Info("fixed_channel.wait.begin", new Dictionary<string, object?>
            {
                ["account"] = context.Config.AccountName, ["target"] = target,
                ["reason"] = "finish_current_combat_then_hold_for_peace", ["requiredPeaceSeconds"] = 15
            });
        var activity = await ChannelSwitchSafety.ReadAsync(context.Snapshots, combatState).ConfigureAwait(false);
        now = _clock.GetUtcNow();
        state.ObserveActivity(activity.Hp, activity.Busy, now);
        state.NextChannelReadAt = now + TimeSpan.FromSeconds(1);
        if (now < state.NextAttemptAt || !state.IsPeaceful(now) ||
            ChannelSwitchSafety.HasExclusiveWork(combatState)) return null;

        state.StartAttempt(now);
        state.NextLocationReadAt = state.NextAttemptAt;
        OperationResult result;
        try
        {
            await prepareMouseAsync().ConfigureAwait(false);
            result = await switchExecutor.ExecuteAsync(new FixedChannelSwitchRequest(
                context.Config.AccountName, target, state.MapId, state.SwitchAttemptCount,
                Array.Empty<FixedChannelClickPoint>())
            {
                Config = context.Config,
                CanUseMouseAsync = async snapshots =>
                {
                    var latest = await ChannelSwitchSafety.ReadAsync(snapshots, combatState).ConfigureAwait(false);
                    state.ObserveActivity(latest.Hp, latest.Busy, _clock.GetUtcNow());
                    return state.IsPeaceful(_clock.GetUtcNow()) && !ChannelSwitchSafety.HasExclusiveWork(combatState);
                }
            }, context.StopToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (context.StopToken.IsCancellationRequested) { throw; }
        catch (Exception ex) { result = OperationResult.Fail(ex.Message); }
        if (result.Success) state.Complete();
        else state.AwaitingConfirmation = false;
        context.Logger.Info("fixed_channel.switch.attempt", new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName, ["targetChannelNumber"] = target,
            ["currentChannelNumber"] = state.ObservedChannelNumber, ["attemptNumber"] = state.SwitchAttemptCount,
            ["success"] = result.Success, ["error"] = result.Error,
            ["nextAttemptAt"] = state.NextAttemptAt, ["resumeNormalWork"] = true
        });
        return null;
    }
}
