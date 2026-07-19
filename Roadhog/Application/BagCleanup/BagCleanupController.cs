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
    private static readonly TimeSpan DefaultTownReturnTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan DefaultSafeWaitTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan DefaultCleanupCooldown = TimeSpan.FromMinutes(25);

    private readonly IKeyboardInput _input;
    private readonly ISharedPathStore _pathStore;
    private readonly BagCleanupPathExecutor _pathExecutor;
    private readonly BagCleanupSafetyChecker _safetyChecker;
    private readonly BagCleanupNpcInteractor _npcInteractor;
    private readonly BagCleanupSeller _seller;

    public BagCleanupController(
        IKeyboardInput input,
        ISharedPathStore pathStore,
        BagCleanupPathExecutor pathExecutor,
        BagCleanupSafetyChecker? safetyChecker = null,
        BagCleanupNpcInteractor? npcInteractor = null,
        BagCleanupSeller? seller = null)
    {
        _input = input;
        _pathStore = pathStore;
        _pathExecutor = pathExecutor;
        _safetyChecker = safetyChecker ?? new BagCleanupSafetyChecker();
        _npcInteractor = npcInteractor ?? new BagCleanupNpcInteractor(input);
        _seller = seller ?? new BagCleanupSeller(input);
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
        var candidates = BagCleanupItemMatcher.SelectSellRegistrationItems(read.Value, settings);
        context.Logger.Info("bag_cleanup.check", new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["freeSlots"] = freeSlots,
            ["threshold"] = threshold,
            ["totalSlots"] = totalSlots,
            ["totalSlotsSource"] = "vmm",
            ["inventoryItemCount"] = read.Value.Count,
            ["occupiedSlots"] = occupiedSlots,
            ["candidateCount"] = candidates.Count
        });

        if (freeSlots >= threshold)
        {
            return BagCleanupTickResult.NotStarted("enough_free_slots");
        }

        var paths = context.Config.ScriptSettings?.Paths ?? new PathScriptSettings();
        if (string.IsNullOrWhiteSpace(paths.TownReturnKey))
        {
            return RecoverableFailure(context, state, "town_return_key_missing", "Town return key is not configured.");
        }

        if (string.IsNullOrWhiteSpace(paths.MaintenancePathName))
        {
            return RecoverableFailure(context, state, "maintenance_path_missing", "Cleanup path is not configured.");
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
        var key = context.Config.ScriptSettings?.Paths?.TownReturnKey ?? string.Empty;
        if (string.IsNullOrWhiteSpace(key))
        {
            return RecoverableFailure(context, state, "town_return_key_missing", "Town return key is not configured.");
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
                state.ReturnAfterFailure("cleanup_npc_select_failed", error);
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
        var result = await _seller
            .ClickScreenPointAsync(
                context,
                maintenance.BagCleanupSellItemClickX,
                maintenance.BagCleanupSellItemClickY,
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
            state.Advance(BagCleanupStep.ReturnByReversePath);
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
                maintenance.BagCleanupSellButtonClickX,
                maintenance.BagCleanupSellButtonClickY,
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

            state.Advance(BagCleanupStep.ReturnByReversePath);
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
        state.ReturnAfterFailure(reason, error);
        return BagCleanupTickResult.Running(reason + "_returning");
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
                ["returnedByReversePath"] = true,
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
