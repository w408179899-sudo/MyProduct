using Roadhog.Application.Input;
using Roadhog.Application.Workers;
using Roadhog.Core.Accounts;
using Roadhog.Core.Common;
using Roadhog.Core.Input;
using Roadhog.Core.Model;

namespace Roadhog.Application.BagCleanup;

public sealed class BagCleanupDiscarder
{
    private static readonly TimeSpan MouseClickHoldDelay = TimeSpan.FromMilliseconds(35);
    private readonly IKeyboardInput _input;
    private readonly BagCleanupSeller _inventoryWindow;

    public BagCleanupDiscarder(IKeyboardInput input, BagCleanupSeller inventoryWindow)
    {
        _input = input;
        _inventoryWindow = inventoryWindow;
    }

    public async Task<OperationResult<InventoryWindowSnapshot>> EnsureInventoryWindowTopLeftAsync(
        AccountWorkerContext context)
    {
        var read = await BagCleanupGameApi.ReadInventoryWindowAsync(context).ConfigureAwait(false);
        if (!read.Success || read.Value is null)
        {
            return OperationResult<InventoryWindowSnapshot>.Fail(
                "Inventory window read before discard failed: " + read.Error);
        }

        if (read.Value.IsOpen && read.Value.IsAtTopLeft())
        {
            return OperationResult<InventoryWindowSnapshot>.Ok(read.Value);
        }

        if (read.Value.IsOpen)
        {
            var close = await _inventoryWindow.CloseInventoryWindowAsync(context).ConfigureAwait(false);
            if (!close.Success)
            {
                return OperationResult<InventoryWindowSnapshot>.Fail(
                    "Inventory window close before discard normalization failed: " + close.Error);
            }
        }

        return await _inventoryWindow.NormalizeInventoryWindowToTopLeftAsync(context).ConfigureAwait(false);
    }

    public async Task<OperationResult> DragItemToDiscardPointAsync(
        AccountWorkerContext context,
        MaintenanceScriptSettings settings,
        InventoryItemSnapshot item,
        InventoryWindowSnapshot? window,
        int destinationX,
        int destinationY)
    {
        if (destinationX <= 0 || destinationY <= 0)
        {
            return OperationResult.Fail("Discard destination is not configured.");
        }

        var source = BagCleanupSeller.EstimateBagItemScreenPoint(
            item.Slot,
            settings.BagCleanupItemCoordinateMode,
            window);
        var move = await ScreenPointMouseMover
            .MoveToAsync(_input, source.X, source.Y, cancellationToken: context.StopToken)
            .ConfigureAwait(false);
        if (!move.Success)
        {
            return OperationResult.Fail("Move to discard item failed: " + move.Error);
        }

        await DelayAsync(ReadDelayMs("ROADHOG_BAG_DISCARD_HOVER_MS", 160), context.StopToken)
            .ConfigureAwait(false);
        var down = await _input.MouseDownAsync(RoadhogMouseButton.Left, context.StopToken).ConfigureAwait(false);
        if (!down.Success)
        {
            return OperationResult.Fail("Discard item mouse down failed: " + down.Error);
        }

        OperationResult? drag = null;
        OperationResult? up = null;
        try
        {
            await DelayAsync(ReadDelayMs("ROADHOG_BAG_DISCARD_MOUSE_DOWN_MS", 80), context.StopToken)
                .ConfigureAwait(false);
            drag = await _input
                .MoveMouseRelativeAsync(destinationX - source.X, destinationY - source.Y, context.StopToken)
                .ConfigureAwait(false);
            await DelayAsync(ReadDelayMs("ROADHOG_BAG_DISCARD_DROP_MS", 120), context.StopToken)
                .ConfigureAwait(false);
        }
        finally
        {
            up = await _input
                .MouseUpAsync(RoadhogMouseButton.Left, CancellationToken.None)
                .ConfigureAwait(false);
        }

        if (drag is null || !drag.Success)
        {
            return OperationResult.Fail("Discard item drag failed: " + drag?.Error);
        }

        if (up is null || !up.Success)
        {
            return OperationResult.Fail("Discard item mouse up failed: " + up?.Error);
        }

        context.Logger.Info("bag_cleanup.discard.dragged", new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["name"] = item.Name,
            ["instanceId"] = item.InstanceId,
            ["slot"] = item.Slot,
            ["sourceX"] = source.X,
            ["sourceY"] = source.Y,
            ["destinationX"] = destinationX,
            ["destinationY"] = destinationY
        });
        return OperationResult.Ok();
    }

    public async Task<OperationResult> ClickDiscardConfirmAsync(
        AccountWorkerContext context,
        int x,
        int y,
        uint itemInstanceId,
        InventoryDiscardConfirmKind kind)
    {
        if (x <= 0 || y <= 0)
        {
            return OperationResult.Fail("Discard confirmation point is not configured.");
        }

        var move = await ScreenPointMouseMover
            .MoveToAsync(_input, x, y, cancellationToken: context.StopToken)
            .ConfigureAwait(false);
        if (!move.Success)
        {
            return OperationResult.Fail("Move to discard confirmation failed: " + move.Error);
        }

        await DelayAsync(ReadDelayMs("ROADHOG_BAG_DISCARD_CONFIRM_HOVER_MS", 120), context.StopToken)
            .ConfigureAwait(false);
        var down = await _input.MouseDownAsync(RoadhogMouseButton.Left, context.StopToken).ConfigureAwait(false);
        if (!down.Success)
        {
            return OperationResult.Fail("Discard confirmation mouse down failed: " + down.Error);
        }

        OperationResult? up = null;
        try
        {
            await DelayAsync(MouseClickHoldDelay, context.StopToken).ConfigureAwait(false);
        }
        finally
        {
            up = await _input
                .MouseUpAsync(RoadhogMouseButton.Left, CancellationToken.None)
                .ConfigureAwait(false);
        }

        if (up is null || !up.Success)
        {
            return OperationResult.Fail("Discard confirmation mouse up failed: " + up?.Error);
        }

        context.Logger.Info("bag_cleanup.discard.confirm.clicked", new Dictionary<string, object?>
        {
            ["account"] = context.Config.AccountName,
            ["instanceId"] = itemInstanceId,
            ["kind"] = kind.ToString(),
            ["x"] = x,
            ["y"] = y
        });
        return OperationResult.Ok();
    }

    public async Task<OperationResult> CancelPendingDiscardAsync(AccountWorkerContext context)
    {
        await _input.MouseUpAsync(RoadhogMouseButton.Left, CancellationToken.None).ConfigureAwait(false);
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var read = await BagCleanupGameApi.ReadInventoryDiscardConfirmAsync(context).ConfigureAwait(false);
            if (read.Success && read.Value?.PendingItemInstanceId == 0)
            {
                return OperationResult.Ok();
            }

            var escape = await _input
                .PressKeyAsync("Escape", TimeSpan.FromMilliseconds(35), context.StopToken)
                .ConfigureAwait(false);
            if (!escape.Success)
            {
                return OperationResult.Fail("Discard confirmation cancel failed: " + escape.Error);
            }

            await DelayAsync(ReadDelayMs("ROADHOG_BAG_DISCARD_CANCEL_SETTLE_MS", 100), context.StopToken)
                .ConfigureAwait(false);
        }

        var verify = await BagCleanupGameApi.ReadInventoryDiscardConfirmAsync(context).ConfigureAwait(false);
        return verify.Success && verify.Value?.PendingItemInstanceId == 0
            ? OperationResult.Ok()
            : OperationResult.Fail(
                "Discard confirmation remained pending after cancellation. " + verify.Error);
    }

    public async Task<OperationResult> CloseInventoryWindowIfOpenAsync(AccountWorkerContext context)
    {
        await _input.MouseUpAsync(RoadhogMouseButton.Left, CancellationToken.None).ConfigureAwait(false);
        string? lastError = null;
        for (var attempt = 1; attempt <= 2; attempt++)
        {
            var read = await BagCleanupGameApi.ReadInventoryWindowAsync(context).ConfigureAwait(false);
            if (!read.Success || read.Value is null)
            {
                lastError = "Inventory window read before close failed: " + read.Error;
            }
            else if (!read.Value.IsOpen)
            {
                return OperationResult.Ok();
            }
            else
            {
                var close = await _inventoryWindow.CloseInventoryWindowAsync(context).ConfigureAwait(false);
                if (!close.Success)
                {
                    lastError = close.Error;
                }
                else
                {
                    var verify = await BagCleanupGameApi.ReadInventoryWindowAsync(context).ConfigureAwait(false);
                    if (verify.Success && verify.Value is { IsOpen: false })
                    {
                        return OperationResult.Ok();
                    }

                    lastError = "Inventory window did not close after discard. " + verify.Error;
                }
            }

            if (attempt < 2)
            {
                context.Logger.Warn("bag_cleanup.discard.inventory_close_retry", new Dictionary<string, object?>
                {
                    ["account"] = context.Config.AccountName,
                    ["attempt"] = attempt,
                    ["error"] = lastError
                });
                await DelayAsync(TimeSpan.FromMilliseconds(100), context.StopToken).ConfigureAwait(false);
            }
        }

        return OperationResult.Fail(lastError ?? "Inventory window close failed after discard.");
    }

    private static TimeSpan ReadDelayMs(string name, int fallback)
    {
        var text = Environment.GetEnvironmentVariable(name);
        return int.TryParse(text, out var parsed)
            ? TimeSpan.FromMilliseconds(Math.Clamp(parsed, 0, 60000))
            : TimeSpan.FromMilliseconds(fallback);
    }

    private static Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        return delay <= TimeSpan.Zero ? Task.CompletedTask : Task.Delay(delay, cancellationToken);
    }
}
