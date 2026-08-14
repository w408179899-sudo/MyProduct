using Roadhog.Application.Workers;
using Roadhog.Core.Accounts;
using Roadhog.Core.Common;
using Roadhog.Core.Input;
using Roadhog.Core.Model;
using Roadhog.Core.Paths;

namespace Roadhog.Application.BagCleanup;

public delegate Task<OperationResult> BagCleanupPathExecutor(
    AccountWorkerContext context,
    string pathName,
    IReadOnlyList<Vector3Snapshot> points);

public sealed class BagCleanupController
{
    private static readonly TimeSpan TownReturnHoldDuration = TimeSpan.FromMilliseconds(35);
    private static readonly TimeSpan TownReturnInterruptEscapeHoldDuration = TimeSpan.FromMilliseconds(35);
    private static readonly TimeSpan TownReturnInterruptEscapeInterval = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan CompletionJumpPreDelay = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan CompletionJumpHoldDuration = TimeSpan.FromMilliseconds(35);
    private static readonly TimeSpan CompletionJumpPostDelay = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan DefaultTownReturnTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan DefaultSafeWaitTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan DefaultCleanupCooldown = TimeSpan.FromMinutes(25);
    private const int MaxDiscardConfirmClicksPerItem = 2;
    private const int MaxConsecutiveDiscardSafetyReadFailures = 3;

    private readonly IKeyboardInput _input;
    private readonly ISharedPathStore _pathStore;
    private readonly BagCleanupPathExecutor _pathExecutor;
    private readonly BagCleanupSafetyChecker _safetyChecker;
    private readonly BagCleanupNpcInteractor _npcInteractor;
    private readonly BagCleanupSeller _seller;
    private readonly BagCleanupDiscarder _discarder;

    public BagCleanupController(
        IKeyboardInput input,
        ISharedPathStore pathStore,
        BagCleanupPathExecutor pathExecutor,
        BagCleanupSafetyChecker? safetyChecker = null,
        BagCleanupNpcInteractor? npcInteractor = null,
        BagCleanupSeller? seller = null,
        BagCleanupDiscarder? discarder = null)
    {
        _input = input;
        _pathStore = pathStore;
        _pathExecutor = pathExecutor;
        _safetyChecker = safetyChecker ?? new BagCleanupSafetyChecker();
        _npcInteractor = npcInteractor ?? new BagCleanupNpcInteractor(input);
        _seller = seller ?? new BagCleanupSeller(input);
        _discarder = discarder ?? new BagCleanupDiscarder(input, _seller);
    }

    public async Task<BagCleanupTickResult> TickAfterLootAsync(
        AccountWorkerContext context,
        BagCleanupState state)
    {
        if (!state.Active)
        {
            return await TryStartAsync(context, state).ConfigureAwait(false);
        }

        return state.Step switch
        {
            BagCleanupStep.PrepareDiscardInventory => await TickPrepareDiscardInventoryAsync(context, state).ConfigureAwait(false),
            BagCleanupStep.ReadDiscardCandidates => await TickReadDiscardCandidatesAsync(context, state).ConfigureAwait(false),
            BagCleanupStep.DragDiscardItem => await TickDragDiscardItemAsync(context, state).ConfigureAwait(false),
            BagCleanupStep.WaitDiscardConfirm => await TickWaitDiscardConfirmAsync(context, state).ConfigureAwait(false),
            BagCleanupStep.ClickDiscardConfirm => await TickClickDiscardConfirmAsync(context, state).ConfigureAwait(false),
            BagCleanupStep.VerifyDiscardItem => await TickVerifyDiscardItemAsync(context, state).ConfigureAwait(false),
            BagCleanupStep.CloseDiscardInventory => await TickCloseDiscardInventoryAsync(context, state).ConfigureAwait(false),
            BagCleanupStep.WaitSafeToReturn => await TickWaitSafeToReturnAsync(context, state).ConfigureAwait(false),
            BagCleanupStep.PressTownReturn => await TickPressTownReturnAsync(context, state).ConfigureAwait(false),
            BagCleanupStep.WaitTownReturnSettle => await TickWaitTownReturnSettleAsync(context, state).ConfigureAwait(false),
            BagCleanupStep.LoadCleanupPath => await TickLoadCleanupPathAsync(context, state).ConfigureAwait(false),
            BagCleanupStep.FollowCleanupPath => await TickFollowCleanupPathAsync(context, state).ConfigureAwait(false),
            BagCleanupStep.SelectCleanupNpc => await TickSelectCleanupNpcAsync(context, state).ConfigureAwait(false),
            BagCleanupStep.OpenNpcDialog => await TickOpenNpcDialogAsync(context, state).ConfigureAwait(false),
            BagCleanupStep.ClickSellItemEntry => await TickClickSellItemEntryAsync(context, state).ConfigureAwait(false),
            BagCleanupStep.NormalizeInventoryWindow => await TickNormalizeInventoryWindowAsync(context, state).ConfigureAwait(false),
            BagCleanupStep.OpenInventoryWindow => await TickOpenInventoryWindowAsync(context, state).ConfigureAwait(false),
            BagCleanupStep.ReadSellCandidates => await TickReadSellCandidatesAsync(context, state).ConfigureAwait(false),
            BagCleanupStep.RegisterSellItems => await TickRegisterSellItemsAsync(context, state).ConfigureAwait(false),
            BagCleanupStep.CloseInventoryWindow => await TickCloseInventoryWindowAsync(context, state).ConfigureAwait(false),
            BagCleanupStep.ClickSellButton => await TickClickSellButtonAsync(context, state).ConfigureAwait(false),
            BagCleanupStep.VerifyInventory => await TickVerifyInventoryAsync(context, state).ConfigureAwait(false),
            BagCleanupStep.PostCleanupJump => await TickPostCleanupJumpAsync(context, state).ConfigureAwait(false),
            BagCleanupStep.PressReturnToRevive => await TickPressReturnToReviveAsync(context, state).ConfigureAwait(false),
            BagCleanupStep.WaitReturnToReviveSettle => await TickWaitReturnToReviveSettleAsync(context, state).ConfigureAwait(false),
            BagCleanupStep.ReturnByReversePath => await TickReturnByReversePathAsync(context, state).ConfigureAwait(false),
            _ => BagCleanupTickResult.NotStarted("inactive")
        };
    }

    public static int CountFreeSlots(
        IEnumerable<InventoryItemSnapshot> items,
        int totalSlots)
    {
        if (totalSlots <= 0)
        {
            return 0;
        }

        var occupied = CountOccupiedSlots(items, totalSlots);
        return Math.Max(0, totalSlots - occupied);
    }

    public static int CountOccupiedSlots(
        IEnumerable<InventoryItemSnapshot> items,
        int totalSlots)
    {
        if (totalSlots <= 0)
        {
            return 0;
        }

        return items
            .Where(item => !item.IsEquipped && item.Slot >= 0 && item.Slot < totalSlots)
            .Select(item => item.Slot)
            .Distinct()
            .Count();
    }

    private async Task<BagCleanupTickResult> TryStartAsync(
        AccountWorkerContext context,
        BagCleanupState state)
    {
        var settings = context.Config.ScriptSettings?.Maintenance ?? new MaintenanceScriptSettings();
        if (!settings.BagCleanupEnabled)
        {
            return BagCleanupTickResult.NotStarted("disabled");
        }

        var threshold = Math.Max(0, settings.BagCleanupThreshold);
        if (threshold <= 0)
        {
            return BagCleanupTickResult.Skipped("threshold_disabled");
        }

        // Keep the original full-cleanup cooldown fast path when no discard rule is configured.
        // Configured discard work remains eligible during that cooldown; once it is exhausted,
        // TryBeginFullCleanup applies the cooldown before any town-return work can begin.
        var hasConfiguredDiscardWork = BagCleanupRuleCatalog
            .MergeWithDefaults(settings.BagCleanupRules)
            .Any(rule => rule.Enabled && rule.Action == BagCleanupAction.Discard) ||
            BagCleanupNameListsDocument
                .NormalizeKeywords(settings.BagCleanupDiscardItemNameKeywords)
                .Count > 0;
        if (!hasConfiguredDiscardWork)
        {
            var cooldownResult = TryGetFullCleanupCooldownResult(context, state);
            if (cooldownResult is not null)
            {
                return cooldownResult;
            }
        }

        var read = await BagCleanupGameApi.ReadInventoryAsync(context).ConfigureAwait(false);
        if (!read.Success || read.Value is null)
        {
            return RecoverableFailure(context, state, "inventory_read_failed", read.Error ?? "Inventory read failed.");
        }

        var capacity = await ReadInventoryCapacityAsync(context).ConfigureAwait(false);
        if (!capacity.Success)
        {
            return RecoverableFailure(context, state, "inventory_capacity_read_failed", capacity.Error ?? "Inventory capacity read failed.");
        }

        var totalSlots = capacity.Value;
        var occupiedSlots = CountOccupiedSlots(read.Value, totalSlots);
        var freeSlots = CountFreeSlots(read.Value, totalSlots);
        var sellCandidates = BagCleanupItemMatcher.SelectSellRegistrationItems(read.Value, settings);
        var discardCandidates = BagCleanupItemMatcher.SelectDiscardItems(read.Value, settings);
        var conflicts = BagCleanupItemMatcher.SelectSellDiscardConflicts(read.Value, settings);
        var whitelistMatchCount = BagCleanupItemMatcher.CountWhitelistedBagItems(read.Value, settings);
        var blacklistMatchCount = BagCleanupItemMatcher.CountBlacklistedBagItems(read.Value, settings);
        context.Logger.Info("bag_cleanup.check", new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["freeSlots"] = freeSlots,
            ["threshold"] = threshold,
            ["totalSlots"] = totalSlots,
            ["totalSlotsSource"] = "vmm",
            ["inventoryItemCount"] = read.Value.Count,
            ["occupiedSlots"] = occupiedSlots,
            ["candidateCount"] = sellCandidates.Count,
            ["sellCandidateCount"] = sellCandidates.Count,
            ["discardCandidateCount"] = discardCandidates.Count,
            ["whitelistMatchCount"] = whitelistMatchCount,
            ["blacklistMatchCount"] = blacklistMatchCount,
            ["sellDiscardConflictCount"] = conflicts.Count
        });

        if (freeSlots >= threshold)
        {
            return BagCleanupTickResult.NotStarted("enough_free_slots");
        }

        if (conflicts.Count > 0)
        {
            context.Logger.Warn("bag_cleanup.discard.conflict", new Dictionary<string, object?>
            {
                ["account"] = context.Config.AccountName,
                ["count"] = conflicts.Count,
                ["instanceIds"] = string.Join(",", conflicts.Select(item => item.InstanceId)),
                ["resolution"] = "sell_wins"
            });
        }

        if (discardCandidates.Count > 0)
        {
            var paths = context.Config.ScriptSettings?.Paths ?? new PathScriptSettings();
            if (paths.DeathReviveClickX <= 0 || paths.DeathReviveClickY <= 0 ||
                settings.BagCleanupDiscardConfirmClickX <= 0 ||
                settings.BagCleanupDiscardConfirmClickY <= 0)
            {
                context.Logger.Warn("bag_cleanup.discard.config.invalid", new Dictionary<string, object?>
                {
                    ["account"] = context.Config.AccountName,
                    ["destinationX"] = paths.DeathReviveClickX,
                    ["destinationY"] = paths.DeathReviveClickY,
                    ["confirmX"] = settings.BagCleanupDiscardConfirmClickX,
                    ["confirmY"] = settings.BagCleanupDiscardConfirmClickY
                });
                return BagCleanupTickResult.Skipped("discard_coordinates_invalid");
            }

            var safe = await _safetyChecker.CheckSafeToReturnAsync(context).ConfigureAwait(false);
            if (!safe.Success)
            {
                context.Logger.Info("bag_cleanup.discard.start.blocked", new Dictionary<string, object?>
                {
                    ["account"] = context.Config.AccountName,
                    ["reason"] = safe.Error
                });
                return BagCleanupTickResult.Skipped("discard_unsafe");
            }

            state.StartDiscard(freeSlots, threshold, discardCandidates.Count);
            context.Logger.Info("bag_cleanup.discard.start", new Dictionary<string, object?>
            {
                ["account"] = context.Config.AccountName,
                ["freeSlots"] = freeSlots,
                ["threshold"] = threshold,
                ["candidateCount"] = discardCandidates.Count
            });
            return BagCleanupTickResult.Running("discard_started");
        }

        return TryBeginFullCleanup(
            context,
            state,
            freeSlots,
            threshold,
            sellCandidates);
    }

    private static BagCleanupTickResult TryBeginFullCleanup(
        AccountWorkerContext context,
        BagCleanupState state,
        int freeSlots,
        int threshold,
        IReadOnlyList<InventoryItemSnapshot> candidates)
    {
        var cooldownResult = TryGetFullCleanupCooldownResult(context, state);
        if (cooldownResult is not null)
        {
            return cooldownResult;
        }

        var paths = context.Config.ScriptSettings?.Paths ?? new PathScriptSettings();
        if (string.IsNullOrWhiteSpace(ResolveBagCleanupTownReturnKey(paths)))
        {
            return RecoverableFailure(
                context,
                state,
                "bag_cleanup_town_return_key_missing",
                "Bag cleanup town return key is not configured.");
        }

        if (string.IsNullOrWhiteSpace(paths.MaintenancePathName))
        {
            return RecoverableFailure(context, state, "maintenance_path_missing", "Cleanup path is not configured.");
        }

        if (!paths.BagCleanupReturnByReversePath && string.IsNullOrWhiteSpace(paths.TownReturnKey))
        {
            return RecoverableFailure(
                context,
                state,
                "return_to_revive_key_missing",
                "Town return key is required when reverse-path return is disabled.");
        }

        if (candidates.Count == 0)
        {
            return RecoverableFailure(context, state, "no_sell_candidates", "No configured sell candidates were found.");
        }

        state.Start(freeSlots, threshold);
        var batch = BagCleanupSellBatchPlanner.SelectNextBatch(candidates);
        state.SetSellCandidates(
            batch.Items,
            candidates.Count);
        context.Logger.Info("bag_cleanup.start", new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["freeSlots"] = freeSlots,
            ["threshold"] = threshold,
            ["pathName"] = paths.MaintenancePathName,
            ["candidateCount"] = candidates.Count
        });
        return BagCleanupTickResult.Running("started");
    }

    private static BagCleanupTickResult? TryGetFullCleanupCooldownResult(
        AccountWorkerContext context,
        BagCleanupState state)
    {
        var now = DateTimeOffset.Now;
        var cleanupCooldown = ReadCleanupCooldown();
        if (state.IsCompletionCooldownActive(now, cleanupCooldown))
        {
            var remaining = cleanupCooldown - (now - state.LastCompletedAt);
            context.Logger.Info("bag_cleanup.skip.cooldown", new Dictionary<string, object?>
            {
                ["account"] = context.Config.AccountName,
                ["lastCompletedAt"] = state.LastCompletedAt,
                ["remainingMs"] = Math.Max(0, (int)remaining.TotalMilliseconds)
            });
            return BagCleanupTickResult.Skipped("cleanup_cooldown");
        }

        var failureCooldown = ReadFailureCooldown(cleanupCooldown);
        if (state.IsFailureCooldownActive(now, failureCooldown))
        {
            var remaining = failureCooldown - (now - state.LastFailedAt);
            context.Logger.Info("bag_cleanup.skip.failure_cooldown", new Dictionary<string, object?>
            {
                ["account"] = context.Config.AccountName,
                ["lastFailedAt"] = state.LastFailedAt,
                ["lastFailureReason"] = state.LastFailureReason,
                ["remainingMs"] = Math.Max(0, (int)remaining.TotalMilliseconds)
            });
            return BagCleanupTickResult.Skipped("cleanup_failure_cooldown");
        }

        return null;
    }

    private async Task<BagCleanupTickResult> TickPrepareDiscardInventoryAsync(
        AccountWorkerContext context,
        BagCleanupState state)
    {
        var interrupted = await TryAbortDiscardIfUnsafeAsync(context, state).ConfigureAwait(false);
        if (interrupted is not null)
        {
            return interrupted;
        }

        var result = await _discarder.EnsureInventoryWindowTopLeftAsync(context).ConfigureAwait(false);
        if (!result.Success || result.Value is null)
        {
            return await FailDiscardLocallyAsync(
                context,
                state,
                "discard_inventory_prepare_failed",
                result.Error ?? "Inventory window preparation failed.").ConfigureAwait(false);
        }

        state.SetDiscardWindow(result.Value);
        state.Advance(BagCleanupStep.ReadDiscardCandidates);
        return BagCleanupTickResult.Running("discard_inventory_ready");
    }

    private async Task<BagCleanupTickResult> TickReadDiscardCandidatesAsync(
        AccountWorkerContext context,
        BagCleanupState state)
    {
        var interrupted = await TryAbortDiscardIfUnsafeAsync(context, state).ConfigureAwait(false);
        if (interrupted is not null)
        {
            return interrupted;
        }

        var read = await BagCleanupGameApi.ReadInventoryAsync(context).ConfigureAwait(false);
        if (!read.Success || read.Value is null)
        {
            return await FailDiscardLocallyAsync(
                context,
                state,
                "discard_inventory_read_failed",
                read.Error ?? "Inventory read failed during discard.").ConfigureAwait(false);
        }

        var maintenance = context.Config.ScriptSettings?.Maintenance ?? new MaintenanceScriptSettings();
        var candidates = BagCleanupItemMatcher.SelectDiscardItems(read.Value, maintenance);
        if (candidates.Count == 0)
        {
            state.Advance(BagCleanupStep.CloseDiscardInventory);
            return BagCleanupTickResult.Running("discard_candidates_exhausted");
        }

        var target = candidates[0];
        if (target.InstanceId == 0 || target.InstanceId > uint.MaxValue)
        {
            return await FailDiscardLocallyAsync(
                context,
                state,
                "discard_instance_id_invalid",
                "Discard target instance id is outside uint32 range: " + target.InstanceId)
                .ConfigureAwait(false);
        }

        state.SetDiscardTarget(target);
        state.Advance(BagCleanupStep.DragDiscardItem);
        context.Logger.Info("bag_cleanup.discard.target.selected", new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["name"] = target.Name,
            ["instanceId"] = target.InstanceId,
            ["slot"] = target.Slot,
            ["remainingCandidateCount"] = candidates.Count
        });
        return BagCleanupTickResult.Running("discard_target_selected");
    }

    private async Task<BagCleanupTickResult> TickDragDiscardItemAsync(
        AccountWorkerContext context,
        BagCleanupState state)
    {
        var interrupted = await TryAbortDiscardIfUnsafeAsync(context, state).ConfigureAwait(false);
        if (interrupted is not null)
        {
            return interrupted;
        }

        if (state.DiscardTarget is not { } lockedTarget)
        {
            return await FailDiscardLocallyAsync(
                context,
                state,
                "discard_target_missing",
                "Discard target state is missing before drag.").ConfigureAwait(false);
        }

        var inventoryRead = await BagCleanupGameApi.ReadInventoryAsync(context).ConfigureAwait(false);
        var currentTarget = inventoryRead.Value?.FirstOrDefault(item =>
            !item.IsEquipped && item.InstanceId == lockedTarget.InstanceId);
        if (!inventoryRead.Success || currentTarget is null)
        {
            return await FailDiscardLocallyAsync(
                context,
                state,
                "discard_target_changed",
                inventoryRead.Error ?? "Discard target disappeared before drag.").ConfigureAwait(false);
        }

        var maintenance = context.Config.ScriptSettings?.Maintenance ?? new MaintenanceScriptSettings();
        InventoryWindowSnapshot? coordinateWindow = state.DiscardWindow;
        if (maintenance.BagCleanupItemCoordinateMode == BagCleanupItemCoordinateMode.WindowRectRelativeExperimental)
        {
            var windowRead = await BagCleanupGameApi
                .ReadInventoryWindowAsync(context, InventoryWindowRectSource.RootWidgetRectExperimental)
                .ConfigureAwait(false);
            if (!windowRead.Success || windowRead.Value is null)
            {
                return await FailDiscardLocallyAsync(
                    context,
                    state,
                    "discard_inventory_rect_failed",
                    windowRead.Error ?? "Discard inventory Rect read failed.").ConfigureAwait(false);
            }

            coordinateWindow = windowRead.Value;
        }

        var paths = context.Config.ScriptSettings?.Paths ?? new PathScriptSettings();
        var drag = await _discarder
            .DragItemToDiscardPointAsync(
                context,
                maintenance,
                currentTarget,
                coordinateWindow,
                paths.DeathReviveClickX,
                paths.DeathReviveClickY)
            .ConfigureAwait(false);
        if (!drag.Success)
        {
            return await FailDiscardLocallyAsync(
                context,
                state,
                "discard_drag_failed",
                drag.Error ?? "Discard drag failed.").ConfigureAwait(false);
        }

        state.SetDiscardTarget(currentTarget);
        state.Advance(BagCleanupStep.WaitDiscardConfirm);
        return BagCleanupTickResult.Running("discard_dragged_waiting_confirm");
    }

    private async Task<BagCleanupTickResult> TickWaitDiscardConfirmAsync(
        AccountWorkerContext context,
        BagCleanupState state)
    {
        if (state.DiscardTarget is not { } target)
        {
            return await FailDiscardLocallyAsync(
                context,
                state,
                "discard_target_missing",
                "Discard target state is missing while waiting for confirmation.").ConfigureAwait(false);
        }

        var confirmRead = await BagCleanupGameApi.ReadInventoryDiscardConfirmAsync(context).ConfigureAwait(false);
        if (!confirmRead.Success || confirmRead.Value is null)
        {
            return await FailDiscardLocallyAsync(
                context,
                state,
                "discard_confirm_read_failed",
                confirmRead.Error ?? "Discard confirmation read failed.").ConfigureAwait(false);
        }

        var targetId = checked((uint)target.InstanceId);
        var confirm = confirmRead.Value;
        if (confirm.IsOpen && confirm.PendingItemInstanceId == targetId)
        {
            state.MarkDiscardConfirmSeen(confirm);
            state.Advance(BagCleanupStep.ClickDiscardConfirm);
            return BagCleanupTickResult.Running("discard_confirm_visible");
        }

        if (confirm.PendingItemInstanceId != 0 && confirm.PendingItemInstanceId != targetId)
        {
            return await FailDiscardLocallyAsync(
                context,
                state,
                "discard_confirm_target_mismatch",
                "Discard confirmation belongs to another item. expected=" + targetId +
                ", actual=" + confirm.PendingItemInstanceId).ConfigureAwait(false);
        }

        var interrupted = await TryAbortDiscardIfUnsafeAsync(context, state).ConfigureAwait(false);
        if (interrupted is not null)
        {
            return interrupted;
        }

        if (DateTimeOffset.Now - state.StepStartedAt < ReadDiscardConfirmTimeout())
        {
            return BagCleanupTickResult.Running("waiting_for_discard_confirm");
        }

        return await FailDiscardLocallyAsync(
            context,
            state,
            "discard_confirm_timeout",
            "Discard confirmation did not appear for item " + targetId).ConfigureAwait(false);
    }

    private async Task<BagCleanupTickResult> TickClickDiscardConfirmAsync(
        AccountWorkerContext context,
        BagCleanupState state)
    {
        if (state.DiscardTarget is not { } target)
        {
            return await FailDiscardLocallyAsync(
                context,
                state,
                "discard_target_missing",
                "Discard target state is missing before confirmation click.").ConfigureAwait(false);
        }

        var targetId = checked((uint)target.InstanceId);
        var confirmRead = await BagCleanupGameApi.ReadInventoryDiscardConfirmAsync(context).ConfigureAwait(false);
        InventoryDiscardConfirmSnapshot confirm;
        if (confirmRead.Success && confirmRead.Value is not null)
        {
            confirm = confirmRead.Value;
            if (!confirm.IsOpen || confirm.PendingItemInstanceId != targetId)
            {
                state.ClearLatchedDiscardConfirm();
            }
        }
        else if (state.LatchedDiscardConfirm is
                 {
                     IsOpen: true
                 } latched && latched.PendingItemInstanceId == targetId)
        {
            confirm = latched;
            context.Logger.Warn("bag_cleanup.discard.confirm_latch_fallback", new Dictionary<string, object?>
            {
                ["account"] = context.Config.AccountName,
                ["instanceId"] = targetId,
                ["kind"] = latched.Kind.ToString(),
                ["dialogId"] = latched.DialogId,
                ["readError"] = confirmRead.Error
            });
        }
        else
        {
            return await FailDiscardLocallyAsync(
                context,
                state,
                "discard_confirm_read_failed",
                confirmRead.Error ?? "Discard confirmation re-read failed.").ConfigureAwait(false);
        }

        if (confirm.PendingItemInstanceId == 0)
        {
            state.Advance(BagCleanupStep.VerifyDiscardItem);
            return BagCleanupTickResult.Running("discard_confirm_already_cleared");
        }

        if (!confirm.IsOpen || confirm.PendingItemInstanceId != targetId)
        {
            return await FailDiscardLocallyAsync(
                context,
                state,
                "discard_confirm_changed",
                "Discard confirmation changed before click. expected=" + targetId +
                ", actual=" + confirm.PendingItemInstanceId).ConfigureAwait(false);
        }

        if (state.DiscardConfirmClickCount >= MaxDiscardConfirmClicksPerItem)
        {
            return await FailDiscardLocallyAsync(
                context,
                state,
                "discard_confirm_click_limit",
                "Discard confirmation exceeded the two-layer safety limit. instanceId=" + targetId)
                .ConfigureAwait(false);
        }

        state.MarkDiscardConfirmSeen(confirm);
        var maintenance = context.Config.ScriptSettings?.Maintenance ?? new MaintenanceScriptSettings();
        var click = await _discarder
            .ClickDiscardConfirmAsync(
                context,
                maintenance.BagCleanupDiscardConfirmClickX,
                maintenance.BagCleanupDiscardConfirmClickY,
                targetId,
                confirm.Kind)
            .ConfigureAwait(false);
        if (!click.Success)
        {
            return await FailDiscardLocallyAsync(
                context,
                state,
                "discard_confirm_click_failed",
                click.Error ?? "Discard confirmation click failed.").ConfigureAwait(false);
        }

        state.MarkDiscardConfirmClicked();
        state.Advance(confirm.Kind == InventoryDiscardConfirmKind.Special
            ? BagCleanupStep.WaitDiscardConfirm
            : BagCleanupStep.VerifyDiscardItem);
        return BagCleanupTickResult.Running(confirm.Kind == InventoryDiscardConfirmKind.Special
            ? "discard_special_confirm_clicked"
            : "discard_confirm_clicked");
    }

    private async Task<BagCleanupTickResult> TickVerifyDiscardItemAsync(
        AccountWorkerContext context,
        BagCleanupState state)
    {
        if (state.DiscardTarget is not { } target)
        {
            return await FailDiscardLocallyAsync(
                context,
                state,
                "discard_target_missing",
                "Discard target state is missing during verification.").ConfigureAwait(false);
        }

        var confirmRead = await BagCleanupGameApi.ReadInventoryDiscardConfirmAsync(context).ConfigureAwait(false);
        var inventoryRead = await BagCleanupGameApi.ReadInventoryAsync(context).ConfigureAwait(false);
        if (!confirmRead.Success || confirmRead.Value is null ||
            !inventoryRead.Success || inventoryRead.Value is null)
        {
            return await FailDiscardLocallyAsync(
                context,
                state,
                "discard_verify_read_failed",
                confirmRead.Error ?? inventoryRead.Error ?? "Discard verification read failed.")
                .ConfigureAwait(false);
        }

        var targetId = checked((uint)target.InstanceId);
        var targetStillPresent = inventoryRead.Value.Any(item => item.InstanceId == target.InstanceId);
        if (!targetStillPresent && confirmRead.Value.PendingItemInstanceId == 0)
        {
            context.Logger.Info("bag_cleanup.discard.verified", new Dictionary<string, object?>
            {
                ["account"] = context.Config.AccountName,
                ["name"] = target.Name,
                ["instanceId"] = target.InstanceId,
                ["confirmClickCount"] = state.DiscardConfirmClickCount,
                ["discardedCount"] = state.DiscardedItemCount + 1
            });
            state.MarkDiscardVerified();
            state.Advance(BagCleanupStep.ReadDiscardCandidates);
            return BagCleanupTickResult.Running("discard_verified");
        }

        if (confirmRead.Value.IsOpen && confirmRead.Value.PendingItemInstanceId == targetId)
        {
            state.MarkDiscardConfirmSeen(confirmRead.Value);
            state.Advance(BagCleanupStep.ClickDiscardConfirm);
            return BagCleanupTickResult.Running("discard_additional_confirm_visible");
        }

        if (confirmRead.Value.PendingItemInstanceId != 0 &&
            confirmRead.Value.PendingItemInstanceId != targetId)
        {
            return await FailDiscardLocallyAsync(
                context,
                state,
                "discard_verify_foreign_confirm",
                "Another discard confirmation appeared during verification.").ConfigureAwait(false);
        }

        if (DateTimeOffset.Now - state.StepStartedAt < ReadDiscardVerifyTimeout())
        {
            return BagCleanupTickResult.Running("waiting_for_discard_verification");
        }

        return await FailDiscardLocallyAsync(
            context,
            state,
            "discard_verify_timeout",
            "Discarded item remained present or confirmation remained pending. instanceId=" + targetId)
            .ConfigureAwait(false);
    }

    private async Task<BagCleanupTickResult> TickCloseDiscardInventoryAsync(
        AccountWorkerContext context,
        BagCleanupState state)
    {
        var discardedCount = state.DiscardedItemCount;
        var close = await _discarder.CloseInventoryWindowIfOpenAsync(context).ConfigureAwait(false);
        if (!close.Success)
        {
            return await FailDiscardLocallyAsync(
                context,
                state,
                "discard_inventory_close_failed",
                close.Error ?? "Inventory window close failed after discard.").ConfigureAwait(false);
        }

        var inventoryRead = await BagCleanupGameApi.ReadInventoryAsync(context).ConfigureAwait(false);
        var capacityRead = await ReadInventoryCapacityAsync(context).ConfigureAwait(false);
        if (!inventoryRead.Success || inventoryRead.Value is null || !capacityRead.Success)
        {
            state.Reset();
            context.Logger.Warn("bag_cleanup.discard.complete_read_failed", new Dictionary<string, object?>
            {
                ["account"] = context.Config.AccountName,
                ["inventoryError"] = inventoryRead.Error,
                ["capacityError"] = capacityRead.Error
            });
            return BagCleanupTickResult.Skipped("discard_complete_read_failed");
        }

        var maintenance = context.Config.ScriptSettings?.Maintenance ?? new MaintenanceScriptSettings();
        var discardCandidates = BagCleanupItemMatcher.SelectDiscardItems(inventoryRead.Value, maintenance);
        var sellCandidates = BagCleanupItemMatcher.SelectSellRegistrationItems(inventoryRead.Value, maintenance);
        var freeSlots = CountFreeSlots(inventoryRead.Value, capacityRead.Value);
        var threshold = state.TriggerThreshold;
        state.Reset();

        context.Logger.Info("bag_cleanup.discard.complete", new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["discardedCount"] = discardedCount,
            ["freeSlots"] = freeSlots,
            ["threshold"] = threshold,
            ["remainingDiscardCandidateCount"] = discardCandidates.Count,
            ["inventoryClosed"] = true
        });

        if (freeSlots < threshold && discardCandidates.Count > 0)
        {
            state.StartDiscard(freeSlots, threshold, discardCandidates.Count);
            return BagCleanupTickResult.Running("discard_candidates_refreshed");
        }

        if (freeSlots < threshold)
        {
            return TryBeginFullCleanup(context, state, freeSlots, threshold, sellCandidates);
        }

        return BagCleanupTickResult.Skipped("discard_completed_capacity_recovered");
    }

    private async Task<BagCleanupTickResult?> TryAbortDiscardIfUnsafeAsync(
        AccountWorkerContext context,
        BagCleanupState state)
    {
        var attacker = await _safetyChecker.FindAttackingTargetNameAsync(context).ConfigureAwait(false);
        if (!attacker.Success)
        {
            var failureCount = state.RecordDiscardSafetyReadFailure();
            if (failureCount < MaxConsecutiveDiscardSafetyReadFailures)
            {
                context.Logger.Warn("bag_cleanup.discard.safety_read.retry", new Dictionary<string, object?>
                {
                    ["account"] = context.Config.AccountName,
                    ["failureCount"] = failureCount,
                    ["failureLimit"] = MaxConsecutiveDiscardSafetyReadFailures,
                    ["error"] = attacker.Error,
                    ["step"] = state.Step.ToString()
                });
                return BagCleanupTickResult.Running("discard_safety_read_retry");
            }

            return await FailDiscardLocallyAsync(
                context,
                state,
                "discard_safety_read_failed",
                attacker.Error ?? "Discard safety read failed.").ConfigureAwait(false);
        }

        state.ClearDiscardSafetyReadFailures();

        return string.IsNullOrWhiteSpace(attacker.Value)
            ? null
            : await AbortDiscardForAttackAsync(context, state, attacker.Value).ConfigureAwait(false);
    }

    public Task<BagCleanupTickResult> AbortDiscardForAttackAsync(
        AccountWorkerContext context,
        BagCleanupState state,
        string? attackerName)
    {
        return AbortDiscardForInterruptionAsync(
            context,
            state,
            "attack",
            attackerName,
            "bag_cleanup.discard.interrupted_by_attack",
            "discard_interrupted_by_attack");
    }

    public Task<BagCleanupTickResult> AbortDiscardForExternalInterruptionAsync(
        AccountWorkerContext context,
        BagCleanupState state,
        string reason)
    {
        return AbortDiscardForInterruptionAsync(
            context,
            state,
            reason,
            null,
            "bag_cleanup.discard.interrupted",
            "discard_interrupted");
    }

    private async Task<BagCleanupTickResult> AbortDiscardForInterruptionAsync(
        AccountWorkerContext context,
        BagCleanupState state,
        string interruptionReason,
        string? attackerName,
        string eventName,
        string resultReason)
    {
        var target = state.DiscardTarget;
        var targetId = target is { InstanceId: > 0 and <= uint.MaxValue }
            ? checked((uint)target.InstanceId)
            : 0;
        var confirmRead = await BagCleanupGameApi.ReadInventoryDiscardConfirmAsync(context).ConfigureAwait(false);
        var liveConfirmVisible = confirmRead.Success && confirmRead.Value is
        {
            IsOpen: true
        } confirm && targetId != 0 && confirm.PendingItemInstanceId == targetId;
        if (liveConfirmVisible && confirmRead.Value is not null)
        {
            state.MarkDiscardConfirmSeen(confirmRead.Value);
        }
        else if (confirmRead.Success)
        {
            state.ClearLatchedDiscardConfirm();
        }

        var completionAttempted = state.DiscardConfirmSeen || liveConfirmVisible;
        OperationResult completion = OperationResult.Ok();
        if (completionAttempted && target is not null)
        {
            completion = await CompleteCurrentDiscardBeforeCombatAsync(context, state, target).ConfigureAwait(false);
        }

        OperationResult cancel = OperationResult.Ok();
        if (!completionAttempted || !completion.Success)
        {
            cancel = await _discarder.CancelPendingDiscardAsync(context).ConfigureAwait(false);
        }

        var close = await _discarder.CloseInventoryWindowIfOpenAsync(context).ConfigureAwait(false);
        context.Logger.Warn(eventName, new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["attackerName"] = attackerName,
            ["interruptionReason"] = interruptionReason,
            ["targetInstanceId"] = target?.InstanceId,
            ["confirmSeen"] = state.DiscardConfirmSeen || liveConfirmVisible,
            ["completionSuccess"] = completion.Success,
            ["completionError"] = completion.Error,
            ["cancelSuccess"] = cancel.Success,
            ["cancelError"] = cancel.Error,
            ["inventoryCloseSuccess"] = close.Success,
            ["inventoryCloseError"] = close.Error,
            ["resumePolicy"] = "restart_on_next_bag_check"
        });
        state.Reset();
        return BagCleanupTickResult.Skipped(resultReason);
    }

    private async Task<OperationResult> CompleteCurrentDiscardBeforeCombatAsync(
        AccountWorkerContext context,
        BagCleanupState state,
        InventoryItemSnapshot target)
    {
        if (target.InstanceId == 0 || target.InstanceId > uint.MaxValue)
        {
            return OperationResult.Fail("Interrupted discard target instance id is invalid.");
        }

        var targetId = checked((uint)target.InstanceId);
        var maintenance = context.Config.ScriptSettings?.Maintenance ?? new MaintenanceScriptSettings();
        var deadline = DateTimeOffset.Now + ReadDiscardVerifyTimeout();
        while (DateTimeOffset.Now < deadline)
        {
            var confirmRead = await BagCleanupGameApi.ReadInventoryDiscardConfirmAsync(context).ConfigureAwait(false);
            var inventoryRead = await BagCleanupGameApi.ReadInventoryAsync(context).ConfigureAwait(false);
            if (!inventoryRead.Success || inventoryRead.Value is null)
            {
                return OperationResult.Fail(
                    inventoryRead.Error ?? "Interrupted discard inventory verification read failed.");
            }

            var targetStillPresent = inventoryRead.Value.Any(item => item.InstanceId == target.InstanceId);
            if (!confirmRead.Success || confirmRead.Value is null)
            {
                if (targetStillPresent &&
                    state.DiscardConfirmClickCount < MaxDiscardConfirmClicksPerItem &&
                    state.LatchedDiscardConfirm is
                    {
                        IsOpen: true
                    } latched && latched.PendingItemInstanceId == targetId)
                {
                    context.Logger.Warn("bag_cleanup.discard.confirm_latch_fallback", new Dictionary<string, object?>
                    {
                        ["account"] = context.Config.AccountName,
                        ["instanceId"] = targetId,
                        ["kind"] = latched.Kind.ToString(),
                        ["dialogId"] = latched.DialogId,
                        ["readError"] = confirmRead.Error,
                        ["interruptionReason"] = "combat_handoff"
                    });
                    var latchedClick = await _discarder
                        .ClickDiscardConfirmAsync(
                            context,
                            maintenance.BagCleanupDiscardConfirmClickX,
                            maintenance.BagCleanupDiscardConfirmClickY,
                            targetId,
                            latched.Kind)
                        .ConfigureAwait(false);
                    if (!latchedClick.Success)
                    {
                        return latchedClick;
                    }

                    state.MarkDiscardConfirmClicked();
                }

                await DelayAsync(TimeSpan.FromMilliseconds(ReadDiscardPollDelayMs()), context.StopToken)
                    .ConfigureAwait(false);
                continue;
            }

            var confirm = confirmRead.Value;
            if (!confirm.IsOpen || confirm.PendingItemInstanceId != targetId)
            {
                state.ClearLatchedDiscardConfirm();
            }

            if (!targetStillPresent && confirm.PendingItemInstanceId == 0)
            {
                return OperationResult.Ok();
            }

            if (confirm.PendingItemInstanceId != 0 &&
                confirm.PendingItemInstanceId != targetId)
            {
                return OperationResult.Fail("Interrupted discard confirmation belongs to another item.");
            }

            if (confirm.IsOpen && confirm.PendingItemInstanceId == targetId)
            {
                if (state.DiscardConfirmClickCount >= MaxDiscardConfirmClicksPerItem)
                {
                    return OperationResult.Fail("Interrupted discard exceeded the two-layer confirmation safety limit.");
                }

                state.MarkDiscardConfirmSeen(confirm);
                var click = await _discarder
                    .ClickDiscardConfirmAsync(
                        context,
                        maintenance.BagCleanupDiscardConfirmClickX,
                        maintenance.BagCleanupDiscardConfirmClickY,
                        targetId,
                        confirm.Kind)
                    .ConfigureAwait(false);
                if (!click.Success)
                {
                    return click;
                }

                state.MarkDiscardConfirmClicked();
                await DelayAsync(TimeSpan.FromMilliseconds(ReadDiscardPollDelayMs()), context.StopToken)
                    .ConfigureAwait(false);
                continue;
            }

            await DelayAsync(TimeSpan.FromMilliseconds(ReadDiscardPollDelayMs()), context.StopToken)
                .ConfigureAwait(false);
        }

        var finalConfirm = await BagCleanupGameApi.ReadInventoryDiscardConfirmAsync(context).ConfigureAwait(false);
        var finalInventory = await BagCleanupGameApi.ReadInventoryAsync(context).ConfigureAwait(false);
        var removed = finalInventory.Success && finalInventory.Value is not null &&
            finalInventory.Value.All(item => item.InstanceId != target.InstanceId);
        return removed && finalConfirm.Success && finalConfirm.Value?.PendingItemInstanceId == 0
            ? OperationResult.Ok()
            : OperationResult.Fail("Interrupted discard could not be completed before combat.");
    }

    private async Task<BagCleanupTickResult> FailDiscardLocallyAsync(
        AccountWorkerContext context,
        BagCleanupState state,
        string reason,
        string error)
    {
        var cancel = await _discarder.CancelPendingDiscardAsync(context).ConfigureAwait(false);
        var close = await _discarder.CloseInventoryWindowIfOpenAsync(context).ConfigureAwait(false);
        context.Logger.Warn("bag_cleanup.discard.failed", new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["reason"] = reason,
            ["error"] = error,
            ["cancelSuccess"] = cancel.Success,
            ["cancelError"] = cancel.Error,
            ["inventoryCloseSuccess"] = close.Success,
            ["inventoryCloseError"] = close.Error,
            ["step"] = state.Step.ToString()
        });
        state.Reset();
        return BagCleanupTickResult.Skipped(reason);
    }

    private async Task<BagCleanupTickResult> TickWaitSafeToReturnAsync(
        AccountWorkerContext context,
        BagCleanupState state)
    {
        var safe = await _safetyChecker.CheckSafeToReturnAsync(context).ConfigureAwait(false);
        if (safe.Success)
        {
            state.Advance(BagCleanupStep.PressTownReturn);
            context.Logger.Info("bag_cleanup.safe.ok", new Dictionary<string, object?>
            {
                ["account"] = context.Config.AccountName
            });
            return BagCleanupTickResult.Running("safe_to_return");
        }

        state.IncrementRetry();
        context.Logger.Info("bag_cleanup.safe_wait", new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["retry"] = state.RetryCount,
            ["error"] = safe.Error
        });

        if (DateTimeOffset.Now - state.StepStartedAt < ReadSafeWaitTimeout())
        {
            return BagCleanupTickResult.Running("waiting_for_safety");
        }

        return RecoverableFailure(context, state, "safe_wait_timeout", safe.Error ?? "Safe wait timed out.");
    }

    private async Task<BagCleanupTickResult> TickPressTownReturnAsync(
        AccountWorkerContext context,
        BagCleanupState state)
    {
        var paths = context.Config.ScriptSettings?.Paths ?? new PathScriptSettings();
        var key = ResolveBagCleanupTownReturnKey(paths);
        if (string.IsNullOrWhiteSpace(key))
        {
            return RecoverableFailure(
                context,
                state,
                "bag_cleanup_town_return_key_missing",
                "Bag cleanup town return key is not configured.");
        }

        var before = await BagCleanupGameApi.ReadPlayerAsync(context).ConfigureAwait(false);
        if (!before.Success || before.Value?.Position is not { } startPosition)
        {
            return RecoverableFailure(
                context,
                state,
                "town_return_start_position_missing",
                before.Error ?? "Player position before town return is not available.");
        }

        var press = await _input.PressKeyAsync(key, TownReturnHoldDuration, context.StopToken).ConfigureAwait(false);
        if (!press.Success)
        {
            return RecoverableFailure(context, state, "town_return_press_failed", press.Error ?? "Town return key press failed.");
        }

        state.MarkPressedTownReturn(startPosition);
        state.Advance(BagCleanupStep.WaitTownReturnSettle);
        context.Logger.Info("bag_cleanup.return.press", new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["key"] = key,
            ["usedLegacyFallback"] = string.IsNullOrWhiteSpace(paths.BagCleanupTownReturnKey),
            ["startX"] = startPosition.X,
            ["startY"] = startPosition.Y,
            ["startZ"] = startPosition.Z
        });
        return BagCleanupTickResult.Running("town_return_pressed");
    }

    private async Task<BagCleanupTickResult> TickWaitTownReturnSettleAsync(
        AccountWorkerContext context,
        BagCleanupState state)
    {
        var interrupted = await TryAbandonTownReturnIfInterruptedAsync(context, state).ConfigureAwait(false);
        if (interrupted is not null)
        {
            return interrupted;
        }

        if (state.TownReturnStartPosition is not { } startPosition)
        {
            return RecoverableFailure(
                context,
                state,
                "town_return_start_position_missing",
                "Player position before town return was not recorded.");
        }

        var after = await BagCleanupGameApi.ReadPlayerAsync(context).ConfigureAwait(false);
        if (!after.Success || after.Value?.Position is not { } endPosition)
        {
            if (DateTimeOffset.Now - state.StepStartedAt < ReadTownReturnTimeout())
            {
                return BagCleanupTickResult.Running("waiting_for_town_return_position");
            }

            return RecoverableFailure(
                context,
                state,
                "town_return_end_position_missing",
                after.Error ?? "Player position after town return is not available.");
        }

        var distance = Distance(startPosition, endPosition);
        var requiredDistance = ReadTownReturnMinDistance();
        if (distance < requiredDistance)
        {
            if (DateTimeOffset.Now - state.StepStartedAt < ReadTownReturnTimeout())
            {
                return BagCleanupTickResult.Running("waiting_for_town_return");
            }

            return RecoverableFailure(
                context,
                state,
                "town_return_position_unchanged",
                "Town return did not move the character enough. distance=" +
                distance.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) +
                ", required=" +
                requiredDistance.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
        }

        context.Logger.Info("bag_cleanup.return.verify.ok", new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["startX"] = startPosition.X,
            ["startY"] = startPosition.Y,
            ["startZ"] = startPosition.Z,
            ["endX"] = endPosition.X,
            ["endY"] = endPosition.Y,
            ["endZ"] = endPosition.Z,
            ["distance"] = distance
        });
        state.Advance(BagCleanupStep.LoadCleanupPath);
        return BagCleanupTickResult.Running("town_return_settled");
    }

    private async Task<BagCleanupTickResult?> TryAbandonTownReturnIfInterruptedAsync(
        AccountWorkerContext context,
        BagCleanupState state)
    {
        var attack = await _safetyChecker.FindAttackingTargetNameAsync(context).ConfigureAwait(false);
        if (!attack.Success)
        {
            context.Logger.Warn("bag_cleanup.return.attack_check_failed", new Dictionary<string, object?>
            {
                ["account"] = context.Config.AccountName,
                ["error"] = attack.Error
            });
            return null;
        }

        if (string.IsNullOrWhiteSpace(attack.Value))
        {
            return null;
        }

        var firstEscape = await _input
            .PressKeyAsync("Escape", TownReturnInterruptEscapeHoldDuration, context.StopToken)
            .ConfigureAwait(false);
        await DelayAsync(TownReturnInterruptEscapeInterval, context.StopToken).ConfigureAwait(false);
        var secondEscape = await _input
            .PressKeyAsync("Escape", TownReturnInterruptEscapeHoldDuration, context.StopToken)
            .ConfigureAwait(false);

        context.Logger.Warn("bag_cleanup.return.interrupted_by_attack", new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["attackerName"] = attack.Value,
            ["firstEscapeSuccess"] = firstEscape.Success,
            ["firstEscapeError"] = firstEscape.Error,
            ["secondEscapeSuccess"] = secondEscape.Success,
            ["secondEscapeError"] = secondEscape.Error
        });

        state.Reset();
        return BagCleanupTickResult.Skipped("town_return_interrupted_by_attack");
    }

    private async Task<BagCleanupTickResult> TickLoadCleanupPathAsync(
        AccountWorkerContext context,
        BagCleanupState state)
    {
        var pathName = context.Config.ScriptSettings?.Paths?.MaintenancePathName ?? string.Empty;
        var load = await _pathStore.LoadAsync(pathName, context.StopToken).ConfigureAwait(false);
        if (!load.Success || load.Value is null)
        {
            return CleanupFailure(context, state, "cleanup_path_load_failed", load.Error ?? "Cleanup path load failed.");
        }

        if (load.Value.PointCount == 0)
        {
            return CleanupFailure(context, state, "cleanup_path_empty", "Cleanup path has no points.");
        }

        if (string.IsNullOrWhiteSpace(load.Value.CleanupNpcName))
        {
            return CleanupFailure(context, state, "cleanup_npc_missing", "Cleanup NPC name is not configured.");
        }

        state.SetPath(load.Value);
        state.Advance(BagCleanupStep.FollowCleanupPath);
        context.Logger.Info("bag_cleanup.path.loaded", new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["pathName"] = state.PathName,
            ["pointCount"] = load.Value.PointCount,
            ["npcName"] = state.CleanupNpcName
        });
        return BagCleanupTickResult.Running("path_loaded");
    }

    private async Task<BagCleanupTickResult> TickFollowCleanupPathAsync(
        AccountWorkerContext context,
        BagCleanupState state)
    {
        if (state.CleanupPath is null)
        {
            return CleanupFailure(context, state, "cleanup_path_missing", "Cleanup path is not loaded.");
        }

        context.Logger.Info("bag_cleanup.path.follow.start", new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["pathName"] = state.PathName,
            ["pointCount"] = state.CleanupPath.PointCount
        });
        var result = await _pathExecutor(
                context,
                state.PathName,
                state.CleanupPath.Points.Select(point => point.ToVector3()).ToArray())
            .ConfigureAwait(false);
        if (!result.Success)
        {
            return ReturnByReversePathAfterFailure(
                context,
                state,
                "cleanup_path_follow_failed",
                result.Error ?? "Cleanup path follow failed.");
        }

        state.Advance(BagCleanupStep.SelectCleanupNpc);
        context.Logger.Info("bag_cleanup.path.follow.complete", new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["pathName"] = state.PathName
        });
        return BagCleanupTickResult.Running("cleanup_path_followed");
    }

    private async Task<BagCleanupTickResult> TickSelectCleanupNpcAsync(
        AccountWorkerContext context,
        BagCleanupState state)
    {
        var result = await _npcInteractor
            .SelectConfiguredNpcAsync(context, state.CleanupNpcName)
            .ConfigureAwait(false);
        if (!result.Success)
        {
            var error = result.Error ?? "Cleanup NPC select failed.";
            if (state.CleanupPath is not null)
            {
                context.Logger.Warn("bag_cleanup.npc.select.failed_returning", new Dictionary<string, object?>
                {
                    ["account"] = context.Config.AccountName,
                    ["pathName"] = state.PathName,
                    ["npcName"] = state.CleanupNpcName,
                    ["error"] = error
                });
                state.PrepareReturnAfterFailure("cleanup_npc_select_failed", error);
                return BagCleanupTickResult.Running("cleanup_npc_select_failed_returning");
            }

            return CleanupFailure(context, state, "cleanup_npc_select_failed", error);
        }

        state.Advance(BagCleanupStep.OpenNpcDialog);
        return BagCleanupTickResult.Running("cleanup_npc_selected");
    }

    private async Task<BagCleanupTickResult> TickOpenNpcDialogAsync(
        AccountWorkerContext context,
        BagCleanupState state)
    {
        var result = await _npcInteractor.OpenDialogAsync(context).ConfigureAwait(false);
        if (!result.Success)
        {
            return ReturnByReversePathAfterFailure(
                context,
                state,
                "npc_dialog_open_failed",
                result.Error ?? "NPC dialog open failed.");
        }

        state.MarkNpcDialogOpened();
        state.Advance(BagCleanupStep.ClickSellItemEntry);
        return BagCleanupTickResult.Running("npc_dialog_opened");
    }

    private async Task<BagCleanupTickResult> TickClickSellItemEntryAsync(
        AccountWorkerContext context,
        BagCleanupState state)
    {
        var maintenance = context.Config.ScriptSettings?.Maintenance ?? new MaintenanceScriptSettings();
        var point = ResolveBagCleanupSellItemClickPoint(state.CleanupPath, maintenance);
        var result = await _seller
            .ClickScreenPointAsync(
                context,
                point.X,
                point.Y,
                "sell_item_entry")
            .ConfigureAwait(false);
        if (!result.Success)
        {
            return ReturnByReversePathAfterFailure(
                context,
                state,
                "sell_item_entry_click_failed",
                result.Error ?? "Sell item entry click failed.");
        }

        await _seller.WaitAfterSellItemEntryAsync(context).ConfigureAwait(false);
        state.MarkSellItemEntryClicked();
        state.Advance(BagCleanupStep.NormalizeInventoryWindow);
        return BagCleanupTickResult.Running("sell_item_entry_clicked");
    }

    private async Task<BagCleanupTickResult> TickNormalizeInventoryWindowAsync(
        AccountWorkerContext context,
        BagCleanupState state)
    {
        var result = await _seller.NormalizeInventoryWindowToTopLeftAsync(context).ConfigureAwait(false);
        if (!result.Success)
        {
            return ReturnByReversePathAfterFailure(
                context,
                state,
                "inventory_window_normalize_failed",
                result.Error ?? "Inventory normalize failed.");
        }

        state.MarkInventoryWindowNormalized();
        state.Advance(BagCleanupStep.ReadSellCandidates);
        return BagCleanupTickResult.Running("inventory_window_normalized");
    }

    private async Task<BagCleanupTickResult> TickOpenInventoryWindowAsync(
        AccountWorkerContext context,
        BagCleanupState state)
    {
        var result = await _seller.OpenInventoryWindowAsync(context).ConfigureAwait(false);
        if (!result.Success)
        {
            return ReturnByReversePathAfterFailure(
                context,
                state,
                "inventory_window_open_failed",
                result.Error ?? "Inventory window open failed.");
        }

        state.Advance(BagCleanupStep.ReadSellCandidates);
        return BagCleanupTickResult.Running("inventory_window_opened");
    }

    private async Task<BagCleanupTickResult> TickReadSellCandidatesAsync(
        AccountWorkerContext context,
        BagCleanupState state)
    {
        var read = await BagCleanupGameApi.ReadInventoryAsync(context).ConfigureAwait(false);
        if (!read.Success || read.Value is null)
        {
            return ReturnByReversePathAfterFailure(
                context,
                state,
                "inventory_read_before_sell_failed",
                read.Error ?? "Inventory read failed.");
        }

        var maintenance = context.Config.ScriptSettings?.Maintenance ?? new MaintenanceScriptSettings();
        var candidates = BagCleanupItemMatcher.SelectSellRegistrationItems(read.Value, maintenance);
        var batch = BagCleanupSellBatchPlanner.SelectNextBatch(candidates);
        context.Logger.Info("bag_cleanup.sell.candidates", new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["count"] = candidates.Count,
            ["batchCount"] = batch.Items.Count,
            ["batchKind"] = batch.KindName,
            ["batchIndex"] = state.SellBatchCount + 1,
            ["maxBatchCount"] = batch.MaxBatchCount
        });

        if (candidates.Count == 0)
        {
            state.PrepareReturnAfterSuccess();
            return BagCleanupTickResult.Running("no_sell_candidates_after_return");
        }

        state.SetSellCandidates(batch.Items, candidates.Count);
        state.Advance(BagCleanupStep.RegisterSellItems);
        return BagCleanupTickResult.Running("sell_candidates_loaded");
    }

    private async Task<BagCleanupTickResult> TickRegisterSellItemsAsync(
        AccountWorkerContext context,
        BagCleanupState state)
    {
        var maintenance = context.Config.ScriptSettings?.Maintenance ?? new MaintenanceScriptSettings();
        InventoryWindowSnapshot? coordinateWindow = null;
        if (maintenance.BagCleanupItemCoordinateMode == BagCleanupItemCoordinateMode.WindowRectRelativeExperimental)
        {
            var read = await BagCleanupGameApi
                .ReadInventoryWindowAsync(context, InventoryWindowRectSource.RootWidgetRectExperimental)
                .ConfigureAwait(false);
            if (!read.Success || read.Value is null)
            {
                return ReturnByReversePathAfterFailure(
                    context,
                    state,
                    "inventory_window_rect_failed",
                    read.Error ?? "Experimental inventory Rect read failed.");
            }

            coordinateWindow = read.Value;
        }

        var result = await _seller
            .RegisterSellItemsAsync(context, maintenance, state.SellCandidates, coordinateWindow)
            .ConfigureAwait(false);
        if (!result.Success)
        {
            return ReturnByReversePathAfterFailure(
                context,
                state,
                "sell_register_failed",
                result.Error ?? "Sell register failed.");
        }

        state.MarkSellItemsRegistered(result.Value?.Count ?? state.SellCandidates.Count);
        await _seller.WaitAfterSellRegistrationAsync(context).ConfigureAwait(false);
        state.Advance(BagCleanupStep.CloseInventoryWindow);
        return BagCleanupTickResult.Running("sell_items_registered");
    }

    private async Task<BagCleanupTickResult> TickCloseInventoryWindowAsync(
        AccountWorkerContext context,
        BagCleanupState state)
    {
        var result = await _seller.CloseInventoryWindowAsync(context).ConfigureAwait(false);
        if (!result.Success)
        {
            return ReturnByReversePathAfterFailure(
                context,
                state,
                "inventory_window_close_failed",
                result.Error ?? "Inventory window close failed.");
        }

        state.MarkInventoryWindowClosed();
        state.Advance(BagCleanupStep.ClickSellButton);
        return BagCleanupTickResult.Running("inventory_window_closed");
    }

    private async Task<BagCleanupTickResult> TickClickSellButtonAsync(
        AccountWorkerContext context,
        BagCleanupState state)
    {
        var maintenance = context.Config.ScriptSettings?.Maintenance ?? new MaintenanceScriptSettings();
        var point = ResolveBagCleanupSellButtonClickPoint(state.CleanupPath, maintenance);
        var moneyBefore = await BagCleanupGameApi.ReadInventoryMoneyAsync(context).ConfigureAwait(false);
        if (!moneyBefore.Success)
        {
            return ReturnByReversePathAfterFailure(
                context,
                state,
                "money_read_before_sell_failed",
                moneyBefore.Error ?? "Inventory money read before sell failed.");
        }

        state.SetInitialMoney(moneyBefore.Value);
        context.Logger.Info("bag_cleanup.sell.money.before", new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["money"] = moneyBefore.Value
        });

        var result = await _seller
            .ClickScreenPointAsync(
                context,
                point.X,
                point.Y,
                "sell_button")
            .ConfigureAwait(false);
        if (!result.Success)
        {
            return ReturnByReversePathAfterFailure(
                context,
                state,
                "sell_button_click_failed",
                result.Error ?? "Sell button click failed.");
        }

        state.MarkSellButtonClicked();
        state.Advance(BagCleanupStep.VerifyInventory);
        return BagCleanupTickResult.Running("sell_button_clicked");
    }

    private async Task<BagCleanupTickResult> TickVerifyInventoryAsync(
        AccountWorkerContext context,
        BagCleanupState state)
    {
        await DelayAsync(TimeSpan.FromMilliseconds(ReadSellVerifyDelayMs()), context.StopToken).ConfigureAwait(false);
        var moneyAfter = await BagCleanupGameApi.ReadInventoryMoneyAsync(context).ConfigureAwait(false);
        if (!moneyAfter.Success)
        {
            return ReturnByReversePathAfterFailure(
                context,
                state,
                "money_verify_read_failed",
                moneyAfter.Error ?? "Inventory money verify read failed.");
        }

        if (state.InitialMoney is not { } initialMoney)
        {
            return ReturnByReversePathAfterFailure(
                context,
                state,
                "money_baseline_missing",
                "Money before sell was not recorded.");
        }

        IReadOnlyList<InventoryItemSnapshot>? inventory = null;
        var inventoryRead = await BagCleanupGameApi.ReadInventoryAsync(context).ConfigureAwait(false);
        if (inventoryRead.Success && inventoryRead.Value is not null)
        {
            inventory = inventoryRead.Value;
        }
        else
        {
            context.Logger.Warn("bag_cleanup.verify.inventory_read_failed", new Dictionary<string, object?>
            {
                ["account"] = context.Config.AccountName,
                ["error"] = inventoryRead.Error
            });
        }

        int? freeSlots = null;
        int? occupiedSlots = null;
        int? totalSlots = null;
        if (inventory is not null)
        {
            var capacity = await ReadInventoryCapacityAsync(context).ConfigureAwait(false);
            if (capacity.Success)
            {
                totalSlots = capacity.Value;
                occupiedSlots = CountOccupiedSlots(inventory, capacity.Value);
                freeSlots = CountFreeSlots(inventory, capacity.Value);
            }
            else
            {
                context.Logger.Warn("bag_cleanup.verify.capacity_read_failed", new Dictionary<string, object?>
                {
                    ["account"] = context.Config.AccountName,
                    ["error"] = capacity.Error
                });
            }
        }

        var initialIds = state.SellCandidates.Select(item => item.InstanceId).ToHashSet();
        var remaining = inventory?.Count(item => initialIds.Contains(item.InstanceId));
        int? remainingSellCandidateCount = null;
        if (inventory is not null)
        {
            var maintenance = context.Config.ScriptSettings?.Maintenance ?? new MaintenanceScriptSettings();
            remainingSellCandidateCount = BagCleanupItemMatcher
                .SelectSellRegistrationItems(inventory, maintenance)
                .Count;
        }

        if (moneyAfter.Value > initialMoney)
        {
            var moneyDelta = moneyAfter.Value - initialMoney;
            state.MarkSellBatchVerified(moneyDelta);
            context.Logger.Info("bag_cleanup.verify.ok", new Dictionary<string, object?>
            {
                ["account"] = context.Config.AccountName,
                ["initialMoney"] = initialMoney,
                ["money"] = moneyAfter.Value,
                ["moneyDelta"] = moneyDelta,
                ["totalMoneyDelta"] = state.TotalMoneyDelta,
                ["initialFreeSlots"] = state.InitialFreeSlots,
                ["freeSlots"] = freeSlots,
                ["totalSlots"] = totalSlots,
                ["occupiedSlots"] = occupiedSlots,
                ["initialCandidateCount"] = state.InitialCandidateCount,
                ["batchIndex"] = state.SellBatchCount,
                ["batchRegisteredCount"] = state.SellCandidates.Count,
                ["totalRegisteredCount"] = state.TotalRegisteredSellItemCount,
                ["remainingCandidateCount"] = remaining,
                ["remainingSellCandidateCount"] = remainingSellCandidateCount
            });

            if (remainingSellCandidateCount is > 0)
            {
                state.Advance(BagCleanupStep.OpenInventoryWindow);
                return BagCleanupTickResult.Running("money_verified_more_sell_candidates");
            }

            state.PrepareReturnAfterSuccess();
            return BagCleanupTickResult.Running("money_verified");
        }

        return ReturnByReversePathAfterFailure(
            context,
            state,
            "money_verify_failed",
            "Money did not increase after selling. before=" +
            initialMoney.ToString(System.Globalization.CultureInfo.InvariantCulture) +
            ", after=" +
            moneyAfter.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) +
            ", remainingCandidateCount=" +
            (remaining?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "unknown"));
    }

    private static BagCleanupTickResult ReturnByReversePathAfterFailure(
        AccountWorkerContext context,
        BagCleanupState state,
        string reason,
        string error)
    {
        if (state.CleanupPath is null)
        {
            return CleanupFailure(context, state, reason, error);
        }

        context.Logger.Warn("bag_cleanup.failure.returning", new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["reason"] = reason,
            ["error"] = error,
            ["fatal"] = false,
            ["pathName"] = state.PathName,
            ["step"] = state.Step.ToString()
        });
        state.PrepareReturnAfterFailure(reason, error);
        return BagCleanupTickResult.Running(reason + "_returning");
    }

    private async Task<BagCleanupTickResult> TickPostCleanupJumpAsync(
        AccountWorkerContext context,
        BagCleanupState state)
    {
        await DelayAsync(CompletionJumpPreDelay, context.StopToken).ConfigureAwait(false);
        var jump = await _input
            .PressKeyAsync("Space", CompletionJumpHoldDuration, context.StopToken)
            .ConfigureAwait(false);
        if (jump.Success)
        {
            context.Logger.Info("bag_cleanup.completion_jump.pressed", new Dictionary<string, object?>
            {
                ["account"] = context.Config.AccountName,
                ["cleanupSucceeded"] = !state.IsReturningAfterFailure,
                ["preDelayMs"] = (int)CompletionJumpPreDelay.TotalMilliseconds,
                ["postDelayMs"] = (int)CompletionJumpPostDelay.TotalMilliseconds
            });
        }
        else
        {
            context.Logger.Warn("bag_cleanup.completion_jump.failed", new Dictionary<string, object?>
            {
                ["account"] = context.Config.AccountName,
                ["cleanupSucceeded"] = !state.IsReturningAfterFailure,
                ["error"] = jump.Error
            });
        }

        await DelayAsync(CompletionJumpPostDelay, context.StopToken).ConfigureAwait(false);
        var returnByReversePath = context.Config.ScriptSettings?.Paths?.BagCleanupReturnByReversePath ?? true;
        state.Advance(returnByReversePath
            ? BagCleanupStep.ReturnByReversePath
            : BagCleanupStep.PressReturnToRevive);
        context.Logger.Info("bag_cleanup.completion_jump.complete", new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["cleanupSucceeded"] = !state.IsReturningAfterFailure,
            ["jumpSuccess"] = jump.Success,
            ["returnMode"] = returnByReversePath ? "reverse_path" : "town_return"
        });
        return BagCleanupTickResult.Running("completion_jump_finished");
    }

    private async Task<BagCleanupTickResult> TickPressReturnToReviveAsync(
        AccountWorkerContext context,
        BagCleanupState state)
    {
        var key = context.Config.ScriptSettings?.Paths?.TownReturnKey?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(key))
        {
            return FallbackToReversePathAfterTownReturnFailure(
                context,
                state,
                "return_to_revive_key_missing",
                "Town return key is not configured.");
        }

        var before = await BagCleanupGameApi.ReadPlayerAsync(context).ConfigureAwait(false);
        if (!before.Success || before.Value?.Position is not { } startPosition)
        {
            return FallbackToReversePathAfterTownReturnFailure(
                context,
                state,
                "return_to_revive_start_position_missing",
                before.Error ?? "Player position before returning to revive point is not available.");
        }

        var press = await _input.PressKeyAsync(key, TownReturnHoldDuration, context.StopToken).ConfigureAwait(false);
        if (!press.Success)
        {
            return FallbackToReversePathAfterTownReturnFailure(
                context,
                state,
                "return_to_revive_press_failed",
                press.Error ?? "Town return key press failed.");
        }

        state.MarkPressedTownReturn(startPosition);
        state.Advance(BagCleanupStep.WaitReturnToReviveSettle);
        context.Logger.Info("bag_cleanup.return_to_revive.press", new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["key"] = key,
            ["startX"] = startPosition.X,
            ["startY"] = startPosition.Y,
            ["startZ"] = startPosition.Z
        });
        return BagCleanupTickResult.Running("return_to_revive_pressed");
    }

    private async Task<BagCleanupTickResult> TickWaitReturnToReviveSettleAsync(
        AccountWorkerContext context,
        BagCleanupState state)
    {
        if (state.TownReturnStartPosition is not { } startPosition)
        {
            return FallbackToReversePathAfterTownReturnFailure(
                context,
                state,
                "return_to_revive_start_position_missing",
                "Player position before returning to revive point was not recorded.");
        }

        var after = await BagCleanupGameApi.ReadPlayerAsync(context).ConfigureAwait(false);
        if (!after.Success || after.Value?.Position is not { } endPosition)
        {
            if (DateTimeOffset.Now - state.StepStartedAt < ReadTownReturnTimeout())
            {
                return BagCleanupTickResult.Running("waiting_for_return_to_revive_position");
            }

            return FallbackToReversePathAfterTownReturnFailure(
                context,
                state,
                "return_to_revive_end_position_missing",
                after.Error ?? "Player position after returning to revive point is not available.");
        }

        var distance = Distance(startPosition, endPosition);
        var requiredDistance = ReadTownReturnMinDistance();
        if (distance < requiredDistance)
        {
            if (DateTimeOffset.Now - state.StepStartedAt < ReadTownReturnTimeout())
            {
                return BagCleanupTickResult.Running("waiting_for_return_to_revive");
            }

            return FallbackToReversePathAfterTownReturnFailure(
                context,
                state,
                "return_to_revive_position_unchanged",
                "Town return did not move the character enough. distance=" +
                distance.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) +
                ", required=" +
                requiredDistance.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
        }

        context.Logger.Info("bag_cleanup.return_to_revive.verify.ok", new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["startX"] = startPosition.X,
            ["startY"] = startPosition.Y,
            ["startZ"] = startPosition.Z,
            ["endX"] = endPosition.X,
            ["endY"] = endPosition.Y,
            ["endZ"] = endPosition.Z,
            ["distance"] = distance
        });
        return FinishCleanupAfterReturn(context, state, returnedByReversePath: false);
    }

    private static BagCleanupTickResult FallbackToReversePathAfterTownReturnFailure(
        AccountWorkerContext context,
        BagCleanupState state,
        string reason,
        string error)
    {
        context.Logger.Warn("bag_cleanup.return_to_revive.failed_returning_by_path", new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["reason"] = reason,
            ["error"] = error,
            ["cleanupAlreadyFailed"] = state.IsReturningAfterFailure,
            ["pathName"] = state.PathName
        });

        if (state.CleanupPath is null)
        {
            return CleanupFailure(context, state, reason, error);
        }

        if (state.IsReturningAfterFailure)
        {
            state.Advance(BagCleanupStep.ReturnByReversePath);
        }
        else
        {
            state.ReturnAfterFailure(reason, error);
        }

        return BagCleanupTickResult.Running(reason + "_returning_by_path");
    }

    private static string ResolveBagCleanupTownReturnKey(PathScriptSettings paths)
    {
        return string.IsNullOrWhiteSpace(paths.BagCleanupTownReturnKey)
            ? paths.TownReturnKey?.Trim() ?? string.Empty
            : paths.BagCleanupTownReturnKey.Trim();
    }

    private static (int X, int Y) ResolveBagCleanupSellItemClickPoint(
        SharedPathDocument? cleanupPath,
        MaintenanceScriptSettings maintenance)
    {
        return cleanupPath?.TryGetBagCleanupClickPoints(
                out var sellItemClickX,
                out var sellItemClickY,
                out _,
                out _) == true
            ? (sellItemClickX, sellItemClickY)
            : (maintenance.BagCleanupSellItemClickX, maintenance.BagCleanupSellItemClickY);
    }

    private static (int X, int Y) ResolveBagCleanupSellButtonClickPoint(
        SharedPathDocument? cleanupPath,
        MaintenanceScriptSettings maintenance)
    {
        return cleanupPath?.TryGetBagCleanupClickPoints(
                out _,
                out _,
                out var sellButtonClickX,
                out var sellButtonClickY) == true
            ? (sellButtonClickX, sellButtonClickY)
            : (maintenance.BagCleanupSellButtonClickX, maintenance.BagCleanupSellButtonClickY);
    }

    private async Task<BagCleanupTickResult> TickReturnByReversePathAsync(
        AccountWorkerContext context,
        BagCleanupState state)
    {
        if (state.CleanupPath is null)
        {
            return CleanupFailure(context, state, "cleanup_return_path_missing", "Cleanup path is not loaded.");
        }

        var points = state.CleanupPath.Points
            .Select(point => point.ToVector3())
            .Reverse()
            .ToArray();
        var result = await _pathExecutor(context, state.PathName + " 返回", points).ConfigureAwait(false);
        if (!result.Success)
        {
            return CleanupFailure(context, state, "cleanup_return_path_failed", result.Error ?? "Cleanup return path failed.");
        }

        context.Logger.Info("bag_cleanup.return_path.complete", new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["pathName"] = state.PathName
        });

        return FinishCleanupAfterReturn(context, state, returnedByReversePath: true);
    }

    private static BagCleanupTickResult FinishCleanupAfterReturn(
        AccountWorkerContext context,
        BagCleanupState state,
        bool returnedByReversePath)
    {
        if (state.IsReturningAfterFailure)
        {
            var reason = state.ReturnAfterFailureReason;
            var error = string.IsNullOrWhiteSpace(state.ReturnAfterFailureError)
                ? reason
                : state.ReturnAfterFailureError;
            context.Logger.Warn("bag_cleanup.failed", new Dictionary<string, object?>
            {
                ["account"] = context.Config.AccountName,
                ["reason"] = reason,
                ["error"] = error,
                ["fatal"] = false,
                ["returnedByReversePath"] = returnedByReversePath,
                ["returnedByTownReturn"] = !returnedByReversePath,
                ["step"] = state.Step.ToString()
            });
            state.MarkFailed(DateTimeOffset.Now, reason);
            state.Reset();
            return BagCleanupTickResult.RecoverableFailure(reason, error);
        }

        state.MarkCompleted(DateTimeOffset.Now);
        state.Complete();
        state.Reset();
        context.Logger.Info("bag_cleanup.complete", new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["returnMode"] = returnedByReversePath ? "reverse_path" : "town_return",
            ["cooldownMs"] = (int)ReadCleanupCooldown().TotalMilliseconds
        });
        return BagCleanupTickResult.Completed("complete");
    }

    private static BagCleanupTickResult RecoverableFailure(
        AccountWorkerContext context,
        BagCleanupState state,
        string reason,
        string error)
    {
        context.Logger.Warn("bag_cleanup.failed", new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["reason"] = reason,
            ["error"] = error,
            ["fatal"] = false,
            ["step"] = state.Step.ToString()
        });
        state.MarkFailed(DateTimeOffset.Now, reason);
        state.Reset();
        return BagCleanupTickResult.RecoverableFailure(reason, error);
    }

    private static BagCleanupTickResult CleanupFailure(
        AccountWorkerContext context,
        BagCleanupState state,
        string reason,
        string error)
    {
        context.Logger.Warn("bag_cleanup.failed", new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["reason"] = reason,
            ["error"] = error,
            ["fatal"] = false,
            ["step"] = state.Step.ToString()
        });
        state.MarkFailed(DateTimeOffset.Now, reason);
        state.Reset();
        return BagCleanupTickResult.RecoverableFailure(reason, error);
    }

    private static async Task<OperationResult<int>> ReadInventoryCapacityAsync(
        AccountWorkerContext context)
    {
        var read = await BagCleanupGameApi.ReadInventoryCapacityAsync(context).ConfigureAwait(false);
        if (!read.Success)
        {
            return read;
        }

        if (read.Value <= 0)
        {
            return OperationResult<int>.Fail("Inventory capacity is invalid: " + read.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        return read;
    }

    private static double Distance(Vector3Snapshot left, Vector3Snapshot right)
    {
        var dx = left.X - right.X;
        var dy = left.Y - right.Y;
        var dz = left.Z - right.Z;
        return Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
    }

    private static TimeSpan ReadCleanupCooldown()
    {
        return TimeSpan.FromMilliseconds(ReadIntFromEnv(
            "ROADHOG_BAG_CLEANUP_COOLDOWN_MS",
            (int)DefaultCleanupCooldown.TotalMilliseconds));
    }

    private static TimeSpan ReadFailureCooldown(TimeSpan fallback)
    {
        return TimeSpan.FromMilliseconds(ReadIntFromEnv(
            "ROADHOG_BAG_CLEANUP_FAILURE_COOLDOWN_MS",
            (int)fallback.TotalMilliseconds));
    }

    private static TimeSpan ReadTownReturnTimeout()
    {
        return TimeSpan.FromMilliseconds(ReadIntFromEnv(
            "ROADHOG_BAG_CLEANUP_TOWN_RETURN_SETTLE_MS",
            (int)DefaultTownReturnTimeout.TotalMilliseconds));
    }

    private static TimeSpan ReadSafeWaitTimeout()
    {
        return TimeSpan.FromMilliseconds(ReadIntFromEnv(
            "ROADHOG_BAG_CLEANUP_SAFE_WAIT_TIMEOUT_MS",
            (int)DefaultSafeWaitTimeout.TotalMilliseconds));
    }

    private static int ReadSellVerifyDelayMs()
    {
        return Math.Clamp(ReadIntFromEnv("ROADHOG_BAG_CLEANUP_SELL_VERIFY_DELAY_MS", 1000), 0, 10000);
    }

    private static TimeSpan ReadDiscardConfirmTimeout()
    {
        return TimeSpan.FromMilliseconds(Math.Clamp(
            ReadIntFromEnv("ROADHOG_BAG_DISCARD_CONFIRM_TIMEOUT_MS", 8000),
            100,
            60000));
    }

    private static TimeSpan ReadDiscardVerifyTimeout()
    {
        return TimeSpan.FromMilliseconds(Math.Clamp(
            ReadIntFromEnv("ROADHOG_BAG_DISCARD_VERIFY_TIMEOUT_MS", 6000),
            100,
            60000));
    }

    private static int ReadDiscardPollDelayMs()
    {
        return Math.Clamp(ReadIntFromEnv("ROADHOG_BAG_DISCARD_POLL_MS", 150), 0, 2000);
    }

    private static double ReadTownReturnMinDistance()
    {
        return ReadDoubleFromEnv("ROADHOG_BAG_CLEANUP_TOWN_RETURN_MIN_DISTANCE", 20.0D);
    }

    private static int ReadIntFromEnv(string name, int fallback)
    {
        return int.TryParse(Environment.GetEnvironmentVariable(name), out var value) ? value : fallback;
    }

    private static double ReadDoubleFromEnv(string name, double fallback)
    {
        return double.TryParse(
            Environment.GetEnvironmentVariable(name),
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture,
            out var value)
            ? value
            : fallback;
    }

    private static Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        return delay <= TimeSpan.Zero ? Task.CompletedTask : Task.Delay(delay, cancellationToken);
    }
}
