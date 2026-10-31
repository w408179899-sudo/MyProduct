using Roadhog.Application.Workers;
using Roadhog.Core.Model;

namespace Roadhog.Application.StationaryCombat;

public sealed partial class StationaryCombatController
{
    private async Task<StationaryCombatBehaviorStatus> TickLootReadyAsync(AccountWorkerContext context, StationaryCombatState state)
    {
        var loot = state.LootAfterKill;
        var now = DateTimeOffset.Now;
        if (state.HasTouchedLootCorpse(loot.KilledTargetEntityId, loot.KilledTargetServerObjectId, now,
                TimeSpan.FromMilliseconds(ReadLootAttemptCacheTtlMs())))
            return SkipLoot(context, state, "already_touched");
        if (state.IsLootCorpseDeferred(loot.KilledTargetEntityId, loot.KilledTargetServerObjectId, now))
            return SkipLoot(context, state, "retry_deferred");

        var eligibility = await ReadLootAttemptEligibilityAsync(context, state).ConfigureAwait(false);
        if (eligibility.HasLoot) return StationaryCombatBehaviorStatus.Success;
        // This is an upper bound for corpse readiness, never an unconditional delay.
        if (eligibility.Reason == "not_lootable" && DateTimeOffset.Now - loot.StepStartedAt < TimeSpan.FromMilliseconds(ReadLootAfterKillWaitMs()))
            return StationaryCombatBehaviorStatus.Running;
        return SkipLoot(context, state, eligibility.Reason);
    }

    private async Task<StationaryCombatBehaviorStatus> TickLootPressLootKeyNodeAsync(AccountWorkerContext context, StationaryCombatState state)
    {
        var loot = state.LootAfterKill;
        if (loot.KilledTargetServerObjectId == 0) return SkipLoot(context, state, "corpse_identity_missing");
        var eligibility = await ReadLootAttemptEligibilityAsync(context, state).ConfigureAwait(false);
        if (!eligibility.HasLoot) return SkipLoot(context, state, eligibility.Reason);
        try
        {
            loot.Observation ??= await LootTouchObservation.StartAsync(context.Snapshots, loot.KilledTargetServerObjectId,
                TimeSpan.FromMilliseconds(ReadLootConfirmationTimeoutMs() + 1500), context.StopToken, context.Logger, context.Config.AccountName).ConfigureAwait(false);
            loot.Observation.Arm();
            await PressLootOnceAsync(context, state).ConfigureAwait(false);
            return StationaryCombatBehaviorStatus.Success;
        }
        catch (OperationCanceledException) when (context.StopToken.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            context.Logger.Warn("stationary_combat.loot.confirmation_unfinished", new Dictionary<string, object?>
            { ["account"] = context.Config.AccountName, ["error"] = ex.Message });
            return SkipLoot(context, state, "confirmation_unfinished", defer: true);
        }
    }

    private async Task PressLootOnceAsync(AccountWorkerContext context, StationaryCombatState state)
    {
        var result = await _input.PressKeyAsync("NumPadDecimal", TimeSpan.FromMilliseconds(ReadLootKeyHoldMs()), context.StopToken).ConfigureAwait(false);
        state.LootAfterKill.RecordPress(DateTimeOffset.Now);
        context.Logger.Info("stationary_combat.loot.pick_pressed", new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["targetEntityId"] = state.LootAfterKill.KilledTargetEntityId,
            ["targetServerObjectId"] = state.LootAfterKill.KilledTargetServerObjectId,
            ["attempt"] = state.LootAfterKill.PressAttempts,
            ["inputAccepted"] = result.Success,
            ["error"] = result.Error
        });
    }

    private async Task<StationaryCombatBehaviorStatus> TickLootConfirmationAsync(AccountWorkerContext context, StationaryCombatState state)
    {
        var loot = state.LootAfterKill;
        var observation = loot.Observation;
        if (observation is null) return SkipLoot(context, state, "observation_ended", defer: true);
        // Inspect the latched acknowledgement before a new read or deadline.
        // A frame-length success remains valid after its window auto-closes.
        if (!observation.Touched)
        {
            try { await observation.CaptureAsync().ConfigureAwait(false); }
            catch (OperationCanceledException) when (context.StopToken.IsCancellationRequested) { throw; }
            catch (Exception) when (!context.StopToken.IsCancellationRequested)
            {
                if (!observation.Touched) return SkipLoot(context, state, "confirmation_timeout", defer: true);
            }
        }
        if (observation.Touched)
        {
            var now = DateTimeOffset.Now;
            state.MarkLootCorpseTouched(loot.KilledTargetEntityId, loot.KilledTargetServerObjectId, now);
            loot.ConfirmTouch(now);
            context.Logger.Info("stationary_combat.loot.touch_confirmed", new Dictionary<string, object?>
            {
                ["account"] = context.Config.AccountName, ["targetServerObjectId"] = loot.KilledTargetServerObjectId,
                ["attempts"] = loot.PressAttempts, ["elapsedMs"] = (now - loot.StartedAt).TotalMilliseconds
            });
            // Run cleanup immediately; no fixed post-pick delay.
            return await TickLootCloseWindowAsync(context, state).ConfigureAwait(false);
        }
        if (DateTimeOffset.Now - loot.StepStartedAt >= TimeSpan.FromMilliseconds(ReadLootConfirmationTimeoutMs()))
            return SkipLoot(context, state, "confirmation_timeout", defer: true);
        if (DateTimeOffset.Now - loot.LastPressAt < TimeSpan.FromMilliseconds(ReadLootRetryIntervalMs()))
            return StationaryCombatBehaviorStatus.Running;

        // Never send a retry to a newly selected monster or somebody else's corpse.
        var eligibility = await ReadLootAttemptEligibilityAsync(context, state).ConfigureAwait(false);
        if (observation.Touched) return await TickLootConfirmationAsync(context, state).ConfigureAwait(false);
        if (!eligibility.HasLoot) return SkipLoot(context, state, eligibility.Reason, defer: true);
        try { await PressLootOnceAsync(context, state).ConfigureAwait(false); }
        catch (OperationCanceledException) when (context.StopToken.IsCancellationRequested) { throw; }
        catch (Exception) when (!context.StopToken.IsCancellationRequested)
        { return SkipLoot(context, state, "input_failed", defer: true); }
        return StationaryCombatBehaviorStatus.Running;
    }

    private async Task<StationaryCombatBehaviorStatus> TickLootCloseWindowAsync(AccountWorkerContext context, StationaryCombatState state)
    {
        var loot = state.LootAfterKill;
        var observation = loot.Observation;
        if (observation is not null)
        {
            try { await observation.CaptureAsync().ConfigureAwait(false); }
            catch (OperationCanceledException) when (context.StopToken.IsCancellationRequested) { throw; }
            catch (Exception) when (!context.StopToken.IsCancellationRequested)
            {
                loot.Outcome = "touched_cleanup_timeout";
                loot.MoveToPostCombatMaintenance(DateTimeOffset.Now);
                return StationaryCombatBehaviorStatus.Running;
            }
            var current = observation.Latest;
            if (current.HasOpenUi && (current.CorpseServerObjectId == 0 || current.CorpseServerObjectId == loot.KilledTargetServerObjectId))
            {
                var now = DateTimeOffset.Now;
                // Ordinary auto-loot can close within one frame. Give only an
                // actually open UI a short settle window so Escape does not
                // arrive after that closure and open the game menu instead.
                if (now - loot.StepStartedAt < TimeSpan.FromMilliseconds(150))
                    return StationaryCombatBehaviorStatus.Running;
                if (now - loot.StepStartedAt < TimeSpan.FromMilliseconds(1500))
                {
                    if (now - loot.LastCloseAt >= TimeSpan.FromMilliseconds(300))
                    {
                        try { await _input.PressKeyAsync("Escape", TimeSpan.FromMilliseconds(ReadLootKeyHoldMs()), context.StopToken).ConfigureAwait(false); }
                        catch (OperationCanceledException) when (context.StopToken.IsCancellationRequested) { throw; }
                        catch (Exception) when (!context.StopToken.IsCancellationRequested)
                        {
                            loot.Outcome = "touched_cleanup_input_failed";
                            loot.MoveToPostCombatMaintenance(now);
                            return StationaryCombatBehaviorStatus.Running;
                        }
                        loot.LastCloseAt = now;
                    }
                    return StationaryCombatBehaviorStatus.Running;
                }
                loot.Outcome = "touched_cleanup_timeout";
            }
        }
        observation?.Dispose();
        loot.Observation = null;
        return StationaryCombatBehaviorStatus.Success;
    }

    private static StationaryCombatBehaviorStatus SkipLoot(AccountWorkerContext context, StationaryCombatState state, string reason, bool defer = false)
    {
        var loot = state.LootAfterKill;
        loot.Outcome = reason;
        if (defer) state.DeferLootCorpse(loot.KilledTargetEntityId, loot.KilledTargetServerObjectId, DateTimeOffset.Now);
        LogLootSkipped(context, state, reason, "loot_action", loot.KilledTargetLootableRaw, loot.KilledTargetInteractionState);
        loot.MoveToPostCombatMaintenance(DateTimeOffset.Now);
        return StationaryCombatBehaviorStatus.Running;
    }

    private static int ReadLootConfirmationTimeoutMs() => ClampInt(ReadRawIntFromEnv("ROADHOG_LOOT_CONFIRM_TIMEOUT_MS", 8_000), 100, 30_000);
    private static int ReadLootRetryIntervalMs() => ClampInt(ReadRawIntFromEnv("ROADHOG_LOOT_RETRY_INTERVAL_MS", 600), 100, 2_000);
}
