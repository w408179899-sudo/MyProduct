using Roadhog.Application.Workers;
using Roadhog.Core.Common;
using Roadhog.Core.Input;
using Roadhog.Core.Model;

namespace Roadhog.Application.BagCleanup;

public sealed class BagCleanupDiscarder
{
    private readonly IKeyboardInput _input;
    private readonly BagCleanupSeller _inventoryWindow;
    private readonly Func<int, CancellationToken, Task>? _actionDelay;

    public BagCleanupDiscarder(IKeyboardInput input, BagCleanupSeller inventoryWindow,
        Func<int, CancellationToken, Task>? actionDelay = null)
    {
        _input = input;
        _inventoryWindow = inventoryWindow;
        _actionDelay = actionDelay;
    }

    public async Task<OperationResult> EnsureInventoryWindowOpenAsync(AccountWorkerContext context)
    {
        try
        {
            var ui = (await context.Snapshots.ReadInventoryInteractionAsync()).Value;
            InventoryDiscardActions.RequireIdle(ui);
            if (!ui.IsOpen)
            {
                InventoryDiscardActions.Check(await _input.PressKeyAsync("I", TimeSpan.FromMilliseconds(60), context.StopToken));
                await Task.Delay(300, context.StopToken);
                ui = (await context.Snapshots.ReadInventoryInteractionAsync()).Value;
                InventoryDiscardActions.RequireIdle(ui);
                InventoryDiscardActions.Require(ui.IsOpen, "背包未打开。");
            }
            return OperationResult.Ok();
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { return OperationResult.Fail(ex.Message); }
    }

    public async Task<OperationResult> DragItemAsync(AccountWorkerContext context, InventoryItemSnapshot item)
    {
        try
        {
            await Actions(context).DragAsync(item, context.StopToken);
            return OperationResult.Ok();
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { return OperationResult.Fail(ex.Message); }
    }

    public async Task<OperationResult> ClickDiscardConfirmAsync(AccountWorkerContext context,
        InventoryItemSnapshot item, InventoryDiscardConfirmSnapshot expected)
    {
        try
        {
            await Actions(context).ConfirmAsync(item, expected.Kind, context.StopToken, expected.DialogId);
            context.Logger.Info("bag_cleanup.discard.confirm.clicked", new Dictionary<string, object?>
            { ["account"] = context.Config.AccountName, ["instanceId"] = item.InstanceId, ["kind"] = expected.Kind.ToString() });
            return OperationResult.Ok();
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { return OperationResult.Fail(ex.Message); }
    }

    private InventoryDiscardActions Actions(AccountWorkerContext context) =>
        new(_input, context.Snapshots, context.Logger, context.Config.AccountName, _actionDelay);

    // Preserve the controller's existing state record, deriving it from the same official
    // UI channel used for button targeting. No independent confirmation read or cache.
    internal static async Task<InventoryDiscardConfirmSnapshot> ReadConfirmationAsync(AccountWorkerContext context)
    {
        var ui = (await context.Snapshots.ReadInventoryInteractionAsync().ConfigureAwait(false)).Value;
        return new(ui.DiscardDialog != null, ui.PendingDiscardInstanceId,
            ui.DiscardDialog?.Kind ?? (ui.PendingDiscardInstanceId == 0
                ? InventoryDiscardConfirmKind.None : InventoryDiscardConfirmKind.PendingWithoutVisibleDialog),
            ui.DiscardDialog?.DialogId ?? -1, 0, DateTimeOffset.UtcNow);
    }

    public async Task<OperationResult> CancelPendingDiscardAsync(AccountWorkerContext context)
    {
        await _input.MouseUpAsync(RoadhogMouseButton.Left, CancellationToken.None).ConfigureAwait(false);
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var read = await context.Snapshots.ReadInventoryInteractionAsync().ConfigureAwait(false);
            if (read.Value.PendingDiscardInstanceId == 0 && read.Value.DiscardDialog == null)
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

        var verify = await context.Snapshots.ReadInventoryInteractionAsync().ConfigureAwait(false);
        return verify.Value.PendingDiscardInstanceId == 0 && verify.Value.DiscardDialog == null
            ? OperationResult.Ok()
            : OperationResult.Fail("Discard confirmation remained pending after cancellation.");
    }

    public async Task<OperationResult> CloseInventoryWindowIfOpenAsync(AccountWorkerContext context)
    {
        await _input.MouseUpAsync(RoadhogMouseButton.Left, CancellationToken.None).ConfigureAwait(false);
        string? lastError = null;
        for (var attempt = 1; attempt <= 2; attempt++)
        {
            var read = await context.Snapshots.ReadInventoryInteractionAsync().ConfigureAwait(false);
            if (!read.Value.IsOpen)
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
                    var verify = await context.Snapshots.ReadInventoryInteractionAsync().ConfigureAwait(false);
                    if (!verify.Value.IsOpen)
                    {
                        return OperationResult.Ok();
                    }

                    lastError = "Inventory window did not close after discard.";
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
