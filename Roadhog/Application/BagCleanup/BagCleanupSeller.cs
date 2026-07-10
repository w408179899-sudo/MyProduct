using Roadhog.Application.Input;
using Roadhog.Application.Workers;
using Roadhog.Core.Accounts;
using Roadhog.Core.Common;
using Roadhog.Core.Input;
using Roadhog.Core.Model;
using System.Globalization;

namespace Roadhog.Application.BagCleanup;

public sealed class BagCleanupSeller
{
    public const int MaxSellRegistrationItemsPerBatch = 30;

    private const string InventoryToggleKey = "I";
    private const double InventoryUiMaxWindowX = 699.2;
    private const double InventoryUiMaxWindowY = 324.8;
    private const double InventoryScreenTopLeftTlX = 0.0;
    private const double InventoryScreenTopLeftTlY = 0.0;
    private const double InventoryScreenTopLeftTrX = 808.0;
    private const double InventoryScreenTopLeftTrY = 0.0;
    private const double InventoryScreenTopLeftBlX = 0.0;
    private const double InventoryScreenTopLeftBlY = 378.0;
    private const double InventoryScreenTopLeftBrX = 793.0;
    private const double InventoryScreenTopLeftBrY = 380.0;
    private const int BagSlotColumns = 9;
    private const int BagSlotsPerPage = 27;
    private const double DefaultBagSlot0CenterX = 30.0;
    private const double DefaultBagSlot0CenterY = 86.0;
    private const double DefaultBagSlotStepX = 40.875;
    private const double DefaultBagSlotStepY = 35.5;
    private const double DefaultBagPage1OffsetY = 151.0;
    private const double DefaultBagPage2OffsetY = 298.0;
    private static readonly TimeSpan InventoryToggleHoldDuration = TimeSpan.FromMilliseconds(35);
    private static readonly TimeSpan InventoryOpenSettleDelay = TimeSpan.FromMilliseconds(700);
    private static readonly TimeSpan InventoryCloseSettleDelay = TimeSpan.FromMilliseconds(1100);
    private static readonly TimeSpan InventoryDragStartSettleDelay = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan InventoryDragSettleDelay = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan MouseClickHoldDelay = TimeSpan.FromMilliseconds(35);
    private static readonly TimeSpan BagCleanupSellRegisterAfterClickDelay = TimeSpan.FromMilliseconds(80);
    private static readonly int[] InventoryTitleDragXOffsets = { 160 };
    private static readonly int[] InventoryTitleDragYOffsets = { 10, 5, 0, -5, -10, -15, -20, -25, -30 };

    private readonly IKeyboardInput _input;

    public BagCleanupSeller(IKeyboardInput input)
    {
        _input = input;
    }

    public async Task<OperationResult> ClickScreenPointAsync(
        AccountWorkerContext context,
        int x,
        int y,
        string action)
    {
        if (x <= 0 || y <= 0)
        {
            return OperationResult.Fail(action + " point is not configured.");
        }

        var move = await ScreenPointMouseMover
            .MoveToAsync(
                _input,
                x,
                y,
                ReadMouseResetCount(),
                TimeSpan.FromMilliseconds(ReadMouseStepDelayMs()),
                context.StopToken)
            .ConfigureAwait(false);
        if (!move.Success)
        {
            return OperationResult.Fail(action + " move failed: " + move.Error);
        }

        await DelayAsync(TimeSpan.FromMilliseconds(ReadPointHoverMs()), context.StopToken).ConfigureAwait(false);
        var down = await _input.MouseDownAsync(RoadhogMouseButton.Left, context.StopToken).ConfigureAwait(false);
        if (!down.Success)
        {
            return OperationResult.Fail(action + " mouse down failed: " + down.Error);
        }

        await DelayAsync(MouseClickHoldDelay, context.StopToken).ConfigureAwait(false);
        var up = await _input.MouseUpAsync(RoadhogMouseButton.Left, context.StopToken).ConfigureAwait(false);
        if (!up.Success)
        {
            return OperationResult.Fail(action + " mouse up failed: " + up.Error);
        }

        context.Logger.Info("bag_cleanup.point.click", new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["action"] = action,
            ["x"] = x,
            ["y"] = y
        });
        return OperationResult.Ok();
    }

    public async Task<OperationResult<InventoryWindowSnapshot>> NormalizeInventoryWindowToTopLeftAsync(
        AccountWorkerContext context)
    {
        var open = await _input
            .PressKeyAsync(InventoryToggleKey, InventoryToggleHoldDuration, context.StopToken)
            .ConfigureAwait(false);
        if (!open.Success)
        {
            return OperationResult<InventoryWindowSnapshot>.Fail(open.Error ?? "Inventory open toggle failed.");
        }

        await DelayAsync(InventoryOpenSettleDelay, context.StopToken).ConfigureAwait(false);
        context.Logger.Info("bag_cleanup.inventory.open.requested", new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["key"] = InventoryToggleKey,
            ["settleMs"] = (int)InventoryOpenSettleDelay.TotalMilliseconds,
            ["source"] = "normalize"
        });

        var read = await BagCleanupGameApi.ReadInventoryWindowAsync(context).ConfigureAwait(false);
        if (!read.Success || read.Value is null)
        {
            return OperationResult<InventoryWindowSnapshot>.Fail("Inventory window read failed: " + read.Error);
        }

        var snapshot = read.Value;
        if (!snapshot.IsOpen)
        {
            return OperationResult<InventoryWindowSnapshot>.Fail(
                "Inventory window did not read as open after blind pressing " + InventoryToggleKey + ".");
        }

        if (!snapshot.IsAtTopLeft())
        {
            var drag = await DragInventoryWindowToTopLeftAsync(context, snapshot).ConfigureAwait(false);
            if (!drag.Success || drag.Value is null)
            {
                return OperationResult<InventoryWindowSnapshot>.Fail(drag.Error ?? "Inventory window drag failed.");
            }

            snapshot = drag.Value;
        }

        context.Logger.Info("bag_cleanup.inventory.normalize.ok", new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["x"] = snapshot.X,
            ["y"] = snapshot.Y,
            ["width"] = snapshot.Width,
            ["height"] = snapshot.Height
        });
        return OperationResult<InventoryWindowSnapshot>.Ok(snapshot);
    }

    public async Task<OperationResult> OpenInventoryWindowAsync(AccountWorkerContext context)
    {
        var open = await _input
            .PressKeyAsync(InventoryToggleKey, InventoryToggleHoldDuration, context.StopToken)
            .ConfigureAwait(false);
        if (!open.Success)
        {
            return OperationResult.Fail("Inventory open toggle failed: " + open.Error);
        }

        await DelayAsync(InventoryOpenSettleDelay, context.StopToken).ConfigureAwait(false);
        context.Logger.Info("bag_cleanup.inventory.open.requested", new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["key"] = InventoryToggleKey,
            ["settleMs"] = (int)InventoryOpenSettleDelay.TotalMilliseconds
        });
        return OperationResult.Ok();
    }

    public async Task<OperationResult> CloseInventoryWindowAsync(AccountWorkerContext context)
    {
        var close = await _input
            .PressKeyAsync(InventoryToggleKey, InventoryToggleHoldDuration, context.StopToken)
            .ConfigureAwait(false);
        if (!close.Success)
        {
            return OperationResult.Fail("Inventory close toggle failed: " + close.Error);
        }

        await DelayAsync(InventoryCloseSettleDelay, context.StopToken).ConfigureAwait(false);
        context.Logger.Info("bag_cleanup.inventory.close.requested", new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["key"] = InventoryToggleKey,
            ["settleMs"] = (int)InventoryCloseSettleDelay.TotalMilliseconds
        });
        return OperationResult.Ok();
    }

    public async Task WaitAfterSellItemEntryAsync(AccountWorkerContext context)
    {
        var delayMs = ReadSellItemEntryDelayMs();
        await DelayAsync(TimeSpan.FromMilliseconds(delayMs), context.StopToken).ConfigureAwait(false);
        context.Logger.Info("bag_cleanup.sell_item_entry.wait", new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["delayMs"] = delayMs
        });
    }

    public async Task WaitAfterSellRegistrationAsync(AccountWorkerContext context)
    {
        var delayMs = ReadAfterSellRegistrationDelayMs();
        await DelayAsync(TimeSpan.FromMilliseconds(delayMs), context.StopToken).ConfigureAwait(false);
        context.Logger.Info("bag_cleanup.sell_registration.wait", new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["delayMs"] = delayMs
        });
    }

    public async Task<OperationResult<IReadOnlyList<BagCleanupRegisteredItem>>> RegisterSellItemsAsync(
        AccountWorkerContext context,
        MaintenanceScriptSettings settings,
        IReadOnlyList<InventoryItemSnapshot> candidates,
        InventoryWindowSnapshot? window)
    {
        var registered = new List<BagCleanupRegisteredItem>();
        foreach (var item in candidates)
        {
            var point = EstimateBagItemScreenPoint(item.Slot, settings.BagCleanupItemCoordinateMode, window);
            context.Logger.Info("bag_cleanup.sell.register.item", new Dictionary<string, object?>
            {
                ["account"] = context.Config.AccountName,
                ["name"] = item.Name,
                ["templateId"] = item.TemplateId,
                ["slot"] = item.Slot,
                ["itemType"] = item.ItemType,
                ["qualityRank"] = item.QualityRank,
                ["x"] = point.X,
                ["y"] = point.Y,
                ["coordinateMode"] = settings.BagCleanupItemCoordinateMode.ToString(),
                ["rectSource"] = window?.RectSource.ToString() ?? InventoryWindowRectSource.LegacyDialogRect.ToString()
            });

            var move = await ScreenPointMouseMover
                .MoveToAsync(
                    _input,
                    point.X,
                    point.Y,
                    ReadMouseResetCount(),
                    TimeSpan.FromMilliseconds(ReadMouseStepDelayMs()),
                    context.StopToken)
                .ConfigureAwait(false);
            if (!move.Success)
            {
                return OperationResult<IReadOnlyList<BagCleanupRegisteredItem>>.Fail(
                    "Move to bag item failed: " + move.Error);
            }

            await DelayAsync(TimeSpan.FromMilliseconds(ReadBagSellRegisterHoverMs()), context.StopToken)
                .ConfigureAwait(false);

            var down = await _input.MouseDownAsync(RoadhogMouseButton.Right, context.StopToken).ConfigureAwait(false);
            if (!down.Success)
            {
                return OperationResult<IReadOnlyList<BagCleanupRegisteredItem>>.Fail(
                    "Right mouse down failed: " + down.Error);
            }

            await DelayAsync(MouseClickHoldDelay, context.StopToken).ConfigureAwait(false);
            var up = await _input.MouseUpAsync(RoadhogMouseButton.Right, context.StopToken).ConfigureAwait(false);
            if (!up.Success)
            {
                return OperationResult<IReadOnlyList<BagCleanupRegisteredItem>>.Fail(
                    "Right mouse up failed: " + up.Error);
            }

            registered.Add(new BagCleanupRegisteredItem(
                item.TemplateId,
                item.InstanceId,
                item.Name,
                item.Slot,
                point.X,
                point.Y));
            await DelayAsync(BagCleanupSellRegisterAfterClickDelay, context.StopToken).ConfigureAwait(false);
        }

        context.Logger.Info("bag_cleanup.sell.register.ok", new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["count"] = registered.Count
        });
        return OperationResult<IReadOnlyList<BagCleanupRegisteredItem>>.Ok(registered);
    }

    private async Task<OperationResult<InventoryWindowSnapshot>> DragInventoryWindowToTopLeftAsync(
        AccountWorkerContext context,
        InventoryWindowSnapshot initialSnapshot)
    {
        var current = initialSnapshot;
        foreach (var xOffset in InventoryTitleDragXOffsets)
        {
            foreach (var yOffset in InventoryTitleDragYOffsets)
            {
                var start = EstimateInventoryTitleDragPoint(current, xOffset, yOffset);
                context.Logger.Info("bag_cleanup.inventory.drag.attempt", new Dictionary<string, object?>
                {
                    ["account"] = context.Config.AccountName,
                    ["rectX"] = current.X,
                    ["rectY"] = current.Y,
                    ["startX"] = start.X,
                    ["startY"] = start.Y,
                    ["xOffset"] = xOffset,
                    ["yOffset"] = yOffset
                });

                var moveToStart = await ScreenPointMouseMover
                    .MoveToAsync(
                        _input,
                        start.X,
                        start.Y,
                        ReadMouseResetCount(),
                        TimeSpan.FromMilliseconds(ReadMouseStepDelayMs()),
                        context.StopToken)
                    .ConfigureAwait(false);
                if (!moveToStart.Success)
                {
                    return OperationResult<InventoryWindowSnapshot>.Fail(
                        "Inventory drag start move failed: " + moveToStart.Error);
                }

                await DelayAsync(InventoryDragStartSettleDelay, context.StopToken).ConfigureAwait(false);
                var down = await _input.MouseDownAsync(RoadhogMouseButton.Left, context.StopToken).ConfigureAwait(false);
                if (!down.Success)
                {
                    return OperationResult<InventoryWindowSnapshot>.Fail("Inventory drag mouse down failed: " + down.Error);
                }

                OperationResult? drag = null;
                try
                {
                    drag = await _input
                        .MoveMouseRelativeAsync(
                            ScreenPointMouseMover.AbsoluteMouseResetDelta,
                            ScreenPointMouseMover.AbsoluteMouseResetDelta,
                            context.StopToken)
                        .ConfigureAwait(false);
                }
                finally
                {
                    await _input.MouseUpAsync(RoadhogMouseButton.Left, context.StopToken).ConfigureAwait(false);
                }

                if (drag is null || !drag.Success)
                {
                    return OperationResult<InventoryWindowSnapshot>.Fail("Inventory drag move failed: " + drag?.Error);
                }

                await DelayAsync(InventoryDragSettleDelay, context.StopToken).ConfigureAwait(false);
                var read = await BagCleanupGameApi.ReadInventoryWindowAsync(context).ConfigureAwait(false);
                if (!read.Success || read.Value is null)
                {
                    return OperationResult<InventoryWindowSnapshot>.Fail(
                        "Inventory window read after drag failed: " + read.Error);
                }

                current = read.Value;
                if (current.IsAtTopLeft())
                {
                    context.Logger.Info("bag_cleanup.inventory.drag.ok", new Dictionary<string, object?>
                    {
                        ["account"] = context.Config.AccountName,
                        ["startX"] = start.X,
                        ["startY"] = start.Y,
                        ["xOffset"] = xOffset,
                        ["yOffset"] = yOffset
                    });
                    return OperationResult<InventoryWindowSnapshot>.Ok(current);
                }
            }
        }

        return OperationResult<InventoryWindowSnapshot>.Fail(
            "Inventory window did not reach top-left. Last rect=(" +
            current.X.ToString("0.###") +
            "," +
            current.Y.ToString("0.###") +
            ").");
    }

    private static (int X, int Y) EstimateInventoryTitleDragPoint(
        InventoryWindowSnapshot snapshot,
        int xOffset,
        int yOffset)
    {
        var topLeft = EstimateInventoryWindowTopLeftScreen(snapshot.X, snapshot.Y);
        return (
            ClampInt((int)Math.Round(topLeft.X + xOffset), 0, short.MaxValue),
            ClampInt((int)Math.Round(topLeft.Y + yOffset), 0, short.MaxValue));
    }

    private static (int X, int Y) EstimateBagItemScreenPoint(
        int slot,
        BagCleanupItemCoordinateMode coordinateMode,
        InventoryWindowSnapshot? window)
    {
        var normalizedSlot = Math.Max(0, slot);
        var page = normalizedSlot / BagSlotsPerPage;
        var indexInPage = normalizedSlot % BagSlotsPerPage;
        var column = indexInPage % BagSlotColumns;
        var rowInPage = indexInPage / BagSlotColumns;
        var slotOriginX = ReadRawDoubleFromEnv("ROADHOG_BAG_SLOT0_CENTER_X", DefaultBagSlot0CenterX);
        var slotOriginY = ReadRawDoubleFromEnv("ROADHOG_BAG_SLOT0_CENTER_Y", DefaultBagSlot0CenterY);
        var x = slotOriginX + (column * ReadRawDoubleFromEnv("ROADHOG_BAG_SLOT_STEP_X", DefaultBagSlotStepX));
        var y = slotOriginY +
            (rowInPage * ReadRawDoubleFromEnv("ROADHOG_BAG_SLOT_STEP_Y", DefaultBagSlotStepY)) +
            ReadBagPageOffsetY(page);
        if (coordinateMode == BagCleanupItemCoordinateMode.WindowRectRelativeExperimental && window is not null)
        {
            var topLeft = EstimateInventoryWindowTopLeftScreen(window.X, window.Y);
            x += topLeft.X;
            y += topLeft.Y;
        }

        return (
            ClampInt((int)Math.Round(x), 0, short.MaxValue),
            ClampInt((int)Math.Round(y), 0, short.MaxValue));
    }

    private static double ReadBagPageOffsetY(int page)
    {
        if (page <= 0)
        {
            return 0.0;
        }

        var page1Offset = ReadRawDoubleFromEnv("ROADHOG_BAG_PAGE1_OFFSET_Y", DefaultBagPage1OffsetY);
        if (page == 1)
        {
            return page1Offset;
        }

        var page2Offset = ReadRawDoubleFromEnv("ROADHOG_BAG_PAGE2_OFFSET_Y", DefaultBagPage2OffsetY);
        return page == 2
            ? page2Offset
            : page2Offset + ((page - 2) * (page2Offset - page1Offset));
    }

    private static (double X, double Y) EstimateInventoryWindowTopLeftScreen(double uiX, double uiY)
    {
        var u = ClampDouble(uiX / InventoryUiMaxWindowX, 0.0, 1.0);
        var v = ClampDouble(uiY / InventoryUiMaxWindowY, 0.0, 1.0);
        return (
            Bilinear(
                InventoryScreenTopLeftTlX,
                InventoryScreenTopLeftTrX,
                InventoryScreenTopLeftBlX,
                InventoryScreenTopLeftBrX,
                u,
                v),
            Bilinear(
                InventoryScreenTopLeftTlY,
                InventoryScreenTopLeftTrY,
                InventoryScreenTopLeftBlY,
                InventoryScreenTopLeftBrY,
                u,
                v));
    }

    private static double Bilinear(
        double topLeft,
        double topRight,
        double bottomLeft,
        double bottomRight,
        double u,
        double v)
    {
        return ((1.0 - u) * (1.0 - v) * topLeft) +
            (u * (1.0 - v) * topRight) +
            ((1.0 - u) * v * bottomLeft) +
            (u * v * bottomRight);
    }

    private static int ReadMouseResetCount()
    {
        return ClampInt(ReadRawIntFromEnv(
                "ROADHOG_DEATH_REVIVE_MOUSE_RESET_COUNT",
                ScreenPointMouseMover.DefaultResetCount),
            1,
            10);
    }

    private static int ReadMouseStepDelayMs()
    {
        return ClampInt(ReadRawIntFromEnv(
                "ROADHOG_DEATH_REVIVE_MOUSE_STEP_DELAY_MS",
                ScreenPointMouseMover.DefaultStepDelayMs),
            0,
            1000);
    }

    private static int ReadPointHoverMs()
    {
        return ClampInt(ReadRawIntFromEnv("ROADHOG_BAG_CLEANUP_POINT_HOVER_MS", 500), 0, 5000);
    }

    private static int ReadBagSellRegisterHoverMs()
    {
        return ClampInt(ReadRawIntFromEnv("ROADHOG_BAG_SELL_REGISTER_HOVER_MS", 200), 0, 5000);
    }

    private static int ReadSellItemEntryDelayMs()
    {
        var min = ClampInt(ReadRawIntFromEnv("ROADHOG_BAG_CLEANUP_SELL_ITEM_ENTRY_DELAY_MIN_MS", 1200), 0, 10000);
        var max = ClampInt(ReadRawIntFromEnv("ROADHOG_BAG_CLEANUP_SELL_ITEM_ENTRY_DELAY_MAX_MS", 1800), 0, 10000);
        if (max < min)
        {
            max = min;
        }

        return min == max ? min : Random.Shared.Next(min, max + 1);
    }

    private static int ReadAfterSellRegistrationDelayMs()
    {
        var min = ClampInt(ReadRawIntFromEnv("ROADHOG_BAG_CLEANUP_SELL_REGISTER_DONE_DELAY_MIN_MS", 300), 0, 10000);
        var max = ClampInt(ReadRawIntFromEnv("ROADHOG_BAG_CLEANUP_SELL_REGISTER_DONE_DELAY_MAX_MS", 800), 0, 10000);
        if (max < min)
        {
            max = min;
        }

        return min == max ? min : Random.Shared.Next(min, max + 1);
    }

    private static int ReadRawIntFromEnv(string name, int defaultValue)
    {
        return int.TryParse(Environment.GetEnvironmentVariable(name), out var value) ? value : defaultValue;
    }

    private static double ReadRawDoubleFromEnv(string name, double defaultValue)
    {
        return double.TryParse(
            Environment.GetEnvironmentVariable(name),
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out var value)
            ? value
            : defaultValue;
    }

    private static int ClampInt(int value, int min, int max)
    {
        return Math.Max(min, Math.Min(max, value));
    }

    private static double ClampDouble(double value, double min, double max)
    {
        return Math.Max(min, Math.Min(max, value));
    }

    private static Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        return delay <= TimeSpan.Zero ? Task.CompletedTask : Task.Delay(delay, cancellationToken);
    }
}

public sealed record BagCleanupRegisteredItem(
    uint TemplateId,
    ulong InstanceId,
    string Name,
    int Slot,
    int X,
    int Y);
