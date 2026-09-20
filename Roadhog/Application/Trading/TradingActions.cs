using Roadhog.Application.Input;
using Roadhog.Core.Api;
using Roadhog.Core.Common;
using Roadhog.Core.Input;
using Roadhog.Core.Model;

namespace Roadhog.Application.Trading;

/// <summary>Input only after official UI identity, cursor and player-state validation.</summary>
public sealed class TradingActions(IKeyboardInput input, IRoadhogSnapshotReader snapshots, CancellationToken token,
    Func<int, CancellationToken, Task>? delay = null)
{
    private string? character;
    public Task Pause(int ms, CancellationToken? cancellation = null) => delay?.Invoke(ms, cancellation ?? token) ?? Task.Delay(ms, cancellation ?? token);
    public async Task Alive()
    {
        token.ThrowIfCancellationRequested();
        var p = (await snapshots.ReadPlayerAsync().WaitAsync(token)).Value;
        character ??= p.CharacterName;
        Require(!p.IsDead && p.CharacterName == character, "角色死亡或身份改变，停止交易。");
    }
    public async Task Key(string key)
    {
        await Alive(); Check(await input.PressKeyAsync(key, TimeSpan.FromMilliseconds(60), token));
    }
    public async Task<T> Wait<T>(Func<Task<T>> read, Func<T, bool> condition, int timeoutMs = 8000)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(timeoutMs);
        try
        {
            while (true)
            {
                var value = await read().WaitAsync(deadline.Token);
                await Alive();
                if (condition(value)) return value;
                await Pause(100, deadline.Token);
            }
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        { throw new TimeoutException("游戏未确认操作，已停止；不会重复提交交易。"); }
    }
    public async Task Click<T>(Func<Task<T>> read, Func<T, GameUiPoint?> locate, Func<T, bool> guard,
        RoadhogMouseButton button = RoadhogMouseButton.Left, bool shift = false)
    {
        await Alive();
        var state = await read(); var point = locate(state);
        Require(point != null && guard(state), "交易对象或控件不可操作。");
        await new FeedbackMouseMover(input, snapshots, delay).MoveAsync(point!, token);
        await Alive();
        state = await read();
        var cursor = (await snapshots.ReadUiCursorAsync().WaitAsync(token)).Value.Position;
        Require(guard(state) && locate(state) == point && Math.Abs(cursor.X - point!.X) <= 1 && Math.Abs(cursor.Y - point.Y) <= 1,
            "点击前交易对象或光标改变。");
        try
        {
            if (shift) Check(await input.KeyDownAsync("ShiftKey", token));
            Check(await input.MouseDownAsync(button, token)); await Pause(35);
        }
        finally
        {
            await input.MouseUpAsync(button, CancellationToken.None);
            if (shift) await input.KeyUpAsync("ShiftKey", CancellationToken.None);
        }
    }
    public async Task Number<T>(Func<Task<T>> read, Func<T, GameUiPoint?> locate, Func<T, bool> guard, ulong value)
    {
        await Click(read, locate, guard);
        try { Check(await input.KeyDownAsync("ControlKey", token)); await Key("A"); }
        finally { await input.KeyUpAsync("ControlKey", CancellationToken.None); }
        foreach (var digit in value.ToString(System.Globalization.CultureInfo.InvariantCulture))
        {
            Require(guard(await read()), "输入时交易对象改变。");
            await Key("D" + digit);
        }
    }
    public async Task Move(GameUiPoint point)
    {
        await Alive();
        await new FeedbackMouseMover(input, snapshots, delay).MoveAsync(point, token);
    }
    public async Task Scroll(GameUiPoint point, int delta)
    {
        await Move(point); await Alive(); Check(await input.ScrollMouseAsync(delta, token));
    }
    public async Task Reset()
    {
        if (input is IInputStateReset reset) Check(await reset.ReleaseAllAsync(CancellationToken.None));
        else
        {
            foreach (var key in new[] { "W", "A", "S", "D", "ShiftKey", "ControlKey" }) await input.KeyUpAsync(key, CancellationToken.None);
            await input.MouseUpAsync(RoadhogMouseButton.Left, CancellationToken.None);
            await input.MouseUpAsync(RoadhogMouseButton.Right, CancellationToken.None);
        }
    }
    public async Task RightClickBag(InventoryItemSnapshot item, Func<Task<bool>> guard)
    {
        async Task<InventoryInteractionSnapshot> Bag() => (await snapshots.ReadInventoryInteractionAsync().WaitAsync(token)).Value;
        if (!(await Bag()).IsOpen) { await Key("I"); await Wait(Bag, s => s.IsOpen); }
        Require(await guard(), "登记前交易页面变化。");
        GameUiPoint? Locate(InventoryInteractionSnapshot s) => s.Items.SingleOrDefault(i => i.InstanceId == item.InstanceId && i.TemplateId == item.TemplateId && i.Quantity == item.Count)?.Point;
        var point = Locate(await Bag()) ?? throw new InvalidOperationException("目标物品不在可见背包中。");
        await new FeedbackMouseMover(input, snapshots, delay).MoveAsync(point, token);
        await Wait(Bag, s => s.HoveredInstanceId == item.InstanceId);
        Require(await guard(), "登记前交易页面变化。");
        await Click(Bag, Locate, s => s.IsOpen && !s.OtherModalOpen && s.DiscardDialog == null && s.HoveredInstanceId == item.InstanceId, RoadhogMouseButton.Right);
    }
    public static void Require(bool condition, string error) { if (!condition) throw new InvalidOperationException(error); }
    public static void Check(OperationResult result) { if (!result.Success) throw new InvalidOperationException(result.Error); }
}
