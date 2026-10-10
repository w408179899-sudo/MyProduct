using Roadhog.Application.Input;
using Roadhog.Core.Api;
using Roadhog.Core.Common;
using Roadhog.Core.Diagnostics;
using Roadhog.Core.Input;
using Roadhog.Core.Model;

namespace Roadhog.Application.BagCleanup;

/// <summary>Shared UI-based actions used by automatic cleanup and the bounded manual test.</summary>
public sealed class InventoryDiscardActions(IKeyboardInput input, IRoadhogSnapshotReader snapshots,
    IRoadhogLogger logger, string account, Func<int, CancellationToken, Task>? delay = null,
    TimeSpan? dialogTimeout = null)
{
    public async Task DragAsync(InventoryItemSnapshot item, CancellationToken token)
    {
        var mover = new FeedbackMouseMover(input, snapshots, delay);
        var ui = await Ui(token);
        RequireIdle(ui);
        var point = Locate(ui, item);
        var drop = ui.DropPoint ?? throw new InvalidOperationException("没有找到背包外可用的空白释放位置。");
        await mover.MoveAsync(new(point.X + (point.X >= 5 ? -5 : 5), point.Y + (point.Y >= 5 ? -5 : 5)), token);
        await mover.MoveAsync(point, token);
        await Pause(350, token);
        ui = await Ui(token); RequireIdle(ui);
        var currentPoint = Locate(ui, item);
        await RequireCursor(currentPoint, token);
        Require(ui.HoveredInstanceId == item.InstanceId && ui.DropPoint == drop, "悬停物品或释放位置已变化，未开始拖拽。");
        logger.Info("inventory_discard.hover_verified", Fields(item));
        try
        {
            Check(await input.MouseDownAsync(RoadhogMouseButton.Left, token).ConfigureAwait(false));
            await Pause(80, token);
            await mover.MoveAsync(drop, token);
            await Pause(120, token);
            ui = await Ui(token); RequireIdle(ui);
            Require(ui.DropPoint == drop, "拖拽期间界面发生变化。");
            await RequireCursor(drop, token);
        }
        finally { Check(await input.MouseUpAsync(RoadhogMouseButton.Left, CancellationToken.None).ConfigureAwait(false)); }
        logger.Info("inventory_discard.dragged", Fields(item));
    }

    public async Task<InventoryDiscardDialog> ConfirmAsync(InventoryItemSnapshot item,
        InventoryDiscardConfirmKind expectedKind, CancellationToken token, int? expectedDialogId = null)
    {
        var mover = new FeedbackMouseMover(input, snapshots, delay);
        var dialog = await WaitForDialogAsync(item, token);
        Require(dialog.Kind == expectedKind, "确认框类型已变化。");
        Require(expectedDialogId == null || dialog.DialogId == expectedDialogId, "确认框已切换。");
        var confirmPoint = dialog.ConfirmButton ?? throw new InvalidOperationException("丢弃确认按钮不可点击。");
        await mover.MoveAsync(confirmPoint, token);
        var actual = await WaitForDialogAsync(item, token);
        Require(actual.DialogId == dialog.DialogId && actual.Kind == dialog.Kind && actual.ConfirmButton == confirmPoint, "确认框已变化。");
        await RequireCursor(confirmPoint, token);
        try
        {
            Check(await input.MouseDownAsync(RoadhogMouseButton.Left, token).ConfigureAwait(false));
            await Pause(35, token);
        }
        finally { Check(await input.MouseUpAsync(RoadhogMouseButton.Left, CancellationToken.None).ConfigureAwait(false)); }
        logger.Info("inventory_discard.confirmed", Fields(item));
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(4));
        try
        {
            while (true)
            {
                var after = await Ui(deadline.Token);
                if (after.PendingDiscardInstanceId == 0 || after.DiscardDialog is { } next &&
                    (next.DialogId != dialog.DialogId || next.Kind != dialog.Kind)) break;
                await Pause(100, deadline.Token);
            }
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        { throw new TimeoutException("确认框未响应，停止操作，不重复点击。"); }
        return dialog;
    }

    public async Task<InventoryDiscardDialog> WaitForDialogAsync(InventoryItemSnapshot item, CancellationToken token)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(dialogTimeout ?? TimeSpan.FromSeconds(4));
        var loggedWait = false;
        try
        {
            while (true)
            {
                var ui = await Ui(deadline.Token);
                Require(!ui.OtherModalOpen && !ui.ShopIsOpen && !ui.IsSelling,
                    "其他窗口阻挡丢弃确认，已停止。");
                Require(ui.PendingDiscardInstanceId == 0 || ui.PendingDiscardInstanceId == item.InstanceId,
                    $"待丢弃物品已变化：目标 {item.InstanceId}，实际 {ui.PendingDiscardInstanceId}。");
                Require(ui.DiscardDialog == null || ui.DiscardDialog.InstanceId == item.InstanceId,
                    $"确认框物品已变化：目标 {item.InstanceId}，实际 {ui.DiscardDialog?.InstanceId}。");
                if (ui.PendingDiscardInstanceId == item.InstanceId && ui.DiscardDialog is { ConfirmButton: not null } dialog)
                    return dialog;
                if (!loggedWait)
                {
                    logger.Info("inventory_discard.confirm.waiting", Fields(item));
                    loggedWait = true;
                }
                await Pause(100, deadline.Token);
            }
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        { throw new TimeoutException("等待丢弃确认框就绪超时，未点击确认。"); }
    }

    private Dictionary<string, object?> Fields(InventoryItemSnapshot item) => new()
    { ["account"] = account, ["name"] = item.Name, ["instanceId"] = item.InstanceId, ["templateId"] = item.TemplateId, ["quantity"] = item.Count };
    private async Task<InventoryInteractionSnapshot> Ui(CancellationToken token) =>
        (await snapshots.ReadInventoryInteractionAsync().WaitAsync(token).ConfigureAwait(false)).Value;
    private async Task RequireCursor(GameUiPoint point, CancellationToken token)
    {
        var cursor = (await snapshots.ReadUiCursorAsync().WaitAsync(token).ConfigureAwait(false)).Value.Position;
        Require(Math.Abs(cursor.X - point.X) <= 1 && Math.Abs(cursor.Y - point.Y) <= 1, "鼠标位置发生变化。");
    }
    internal static InventoryDiscardDialog RequireDialog(InventoryInteractionSnapshot ui, InventoryItemSnapshot item)
    {
        Require(!ui.OtherModalOpen && ui.PendingDiscardInstanceId == item.InstanceId && ui.DiscardDialog?.InstanceId == item.InstanceId,
            "确认框对应的物品与本次目标不一致。");
        return ui.DiscardDialog!;
    }
    internal static GameUiPoint Locate(InventoryInteractionSnapshot ui, InventoryItemSnapshot item) =>
        ui.Items.SingleOrDefault(i => i.InstanceId == item.InstanceId && i.TemplateId == item.TemplateId && i.Quantity == item.Count)?.Point
        ?? throw new InvalidOperationException("物品不在可见背包格子中或数量已变化。");
    internal static bool SameItem(InventoryItemSnapshot a, InventoryItemSnapshot b) => a.InstanceId == b.InstanceId && a.TemplateId == b.TemplateId && a.Count == b.Count;
    internal static void RequireIdle(InventoryInteractionSnapshot ui)
    {
        Require(!ui.IsSelling && !ui.ShopIsOpen, "请先停止出售并关闭摊位窗口。");
        Require(ui.PendingDiscardInstanceId == 0 && ui.DiscardDialog == null && !ui.OtherModalOpen, "请先关闭已有确认窗口。");
    }
    internal static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    internal static void Check(OperationResult result) { if (!result.Success) throw new InvalidOperationException(result.Error ?? "输入失败。"); }
    private Task Pause(int ms, CancellationToken token) => delay?.Invoke(ms, token) ?? Task.Delay(ms, token);
}
