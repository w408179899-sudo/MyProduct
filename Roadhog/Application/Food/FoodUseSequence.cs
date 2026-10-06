using Roadhog.Application.Input;
using Roadhog.Core.Api;
using Roadhog.Core.Input;
using Roadhog.Core.Model;

namespace Roadhog.Application.Food;

/// <summary>One right-click, followed by confirmation of the category status; never blind retries.</summary>
public sealed class FoodUseSequence(IKeyboardInput input, FoodCatalog catalog,
    Func<int, CancellationToken, Task>? delay = null, int timeoutMs = 12000)
{
    public async Task<bool> RunAsync(IRoadhogSnapshotReader snapshots, FoodKind kind, InventoryItemSnapshot item,
        Func<Task<bool>> canUse, CancellationToken token, FoodQuickbarBinding? binding = null,
        FoodInventorySession? sharedInventory = null)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(timeoutMs);
        var ct = deadline.Token;
        var initial = (await snapshots.ReadChannelTransitionAsync().WaitAsync(ct)).Value;
        var inventorySession = sharedInventory ?? new FoodInventorySession(snapshots, input, token);
        async Task Pause(int ms) => await (delay?.Invoke(ms, ct) ?? Task.Delay(ms, ct));
        async Task Guard()
        {
            ct.ThrowIfCancellationRequested();
            var scene = (await snapshots.ReadChannelTransitionAsync().WaitAsync(ct)).Value;
            if (!initial.IsReady || !scene.IsReady || scene.Player!.IsDead ||
                scene.Player.CharacterName != initial.Player!.CharacterName ||
                scene.Channel!.MapId != initial.Channel!.MapId || scene.Channel.Index != initial.Channel.Index ||
                scene.Player.CurrentHp < initial.Player.CurrentHp ||
                !await canUse().WaitAsync(ct)) throw new InvalidOperationException("料理维护被战斗、过图或其他任务打断。");
        }
        async Task<InventoryInteractionSnapshot> Bag()
        {
            await Guard();
            var bag = (await snapshots.ReadInventoryInteractionAsync().WaitAsync(ct)).Value;
            if (bag.ShopIsOpen || bag.IsSelling || bag.OtherModalOpen || bag.DiscardDialog != null ||
                bag.PendingDiscardInstanceId != 0) throw new InvalidOperationException("背包正在交易或有其他弹窗。");
            return bag;
        }
        async Task<bool> HasStatus() => catalog.HasStatus(
            (await snapshots.ReadPlayerAbnormalStatusesAsync().WaitAsync(ct)).Value, kind);
        async Task Key()
        {
            await Guard();
            var result = await input.PressKeyAsync("I", TimeSpan.FromMilliseconds(60), ct);
            if (!result.Success) throw new InvalidOperationException(result.Error);
        }
        try
        {
            await Guard();
            if (await HasStatus()) return false;
            if (binding != null)
            {
                // Modal/scene/combat guards apply equally to hotkeys and inventory clicks.
                await Bag();
                var inventory = (await snapshots.ReadInventoryAsync().WaitAsync(ct)).Value;
                if (!inventory.Any(i => i == item)) throw new InvalidOperationException("Food item changed before hotkey.");
                await Guard();
                if (await HasStatus()) return false;
                var quickbar = (await snapshots.ReadQuickbarAsync().WaitAsync(ct)).Value;
                if (quickbar.Page != binding.Page || !quickbar.Slots.Contains(binding.Slot))
                    throw new InvalidOperationException("Food quickbar binding changed before hotkey.");
                var result = await input.PressKeyAsync(binding.Key, TimeSpan.FromMilliseconds(60), ct);
                if (!result.Success) throw new InvalidOperationException(result.Error);
            }
            else
            {
                if (!(await Bag()).IsOpen)
                {
                    await Key(); inventorySession.RecordOpened(initial);
                    while (!(await Bag()).IsOpen) await Pause(100);
                }
                var bag = await Bag();
                GameUiPoint? Locate(InventoryInteractionSnapshot b) => b.Items.SingleOrDefault(i =>
                    i.InstanceId == item.InstanceId && i.TemplateId == item.TemplateId && i.Quantity == item.Count)?.Point;
                var point = Locate(bag) ?? throw new InvalidOperationException("料理不在可见背包中。");
                var mover = new FeedbackMouseMover(input, snapshots, delay);
                var beforeMove = (await snapshots.ReadUiCursorAsync().WaitAsync(ct)).Value;
                // Reopening the bag under a stationary cursor may not refresh the game's hover object.
                // Leave the item before returning, including when the cursor is near its center.
                if (Math.Abs(beforeMove.Position.X - point.X) <= 32 &&
                    Math.Abs(beforeMove.Position.Y - point.Y) <= 32)
                {
                    var away = new GameUiPoint(point.X + (point.X + 64 < beforeMove.Width ? 64 : -64), point.Y);
                    await mover.MoveAsync(away, ct);
                    await Guard();
                }
                await mover.MoveAsync(point, ct);
                while ((await Bag()).HoveredInstanceId != item.InstanceId) await Pause(100);
                if (await HasStatus()) return false;
                var inventory = (await snapshots.ReadInventoryAsync().WaitAsync(ct)).Value;
                if (!inventory.Any(i => i == item)) throw new InvalidOperationException("点击前料理物品已变化。");
                bag = await Bag();
                var cursor = (await snapshots.ReadUiCursorAsync().WaitAsync(ct)).Value.Position;
                if (!bag.IsOpen || Locate(bag) != point || bag.HoveredInstanceId != item.InstanceId ||
                    Math.Abs(cursor.X - point.X) > 1 || Math.Abs(cursor.Y - point.Y) > 1)
                    throw new InvalidOperationException("点击前料理位置或悬停物品改变。");
                // Bag() already checked combat/scene. Do not insert slow reads between final cursor verification and click.
                try
                {
                    var down = await input.MouseDownAsync(RoadhogMouseButton.Right, ct);
                    if (!down.Success) throw new InvalidOperationException(down.Error);
                    await Pause(60);
                }
                finally { await input.MouseUpAsync(RoadhogMouseButton.Right, CancellationToken.None); }
            }
            while (!await HasStatus()) { await Guard(); await Pause(100); }
            return true;
        }
        finally
        {
            if (sharedInventory == null) await inventorySession.DisposeAsync();
        }
    }
}
