using Roadhog.Application.BagCleanup;
using Roadhog.Application.Travel;
using Roadhog.Application.Workers;
using Roadhog.Core.Input;
using Roadhog.Core.Model;
using Roadhog.Core.Paths;
using static Roadhog.Application.Trading.TradingActions;

namespace Roadhog.Application.Trading;

public sealed class GroceryTripInterruptedException(string reason) : Exception(reason);
public sealed class GroceryTripDepartureFailedException(string reason) : Exception(reason);

public sealed class GroceryReturnSequence(IKeyboardInput input,
    Func<int, CancellationToken, Task>? delay = null, Func<DateTimeOffset>? clock = null)
{
    public static InventoryItemSnapshot? FindScroll(IEnumerable<InventoryItemSnapshot> bag, string name) =>
        bag.Where(i => !i.IsEquipped && i.Slot >= 0 && i.Count > 0 &&
            string.Equals(i.Name, name.Trim(), StringComparison.OrdinalIgnoreCase)).OrderBy(i => i.Slot).FirstOrDefault();

    public async Task RunAsync(AccountWorkerContext context, SharedPathDocument destination, string itemName, Action<string> report)
    {
        var token = context.StopToken;
        var actions = new TradingActions(input, context.Snapshots, token, delay);
        var safety = new BagCleanupSafetyChecker();
        DateTimeOffset Now() => clock?.Invoke() ?? DateTimeOffset.UtcNow;
        async Task<ChannelTransitionSnapshot> Scene() => (await context.Snapshots.ReadChannelTransitionAsync().WaitAsync(token)).Value;
        async Task<InventoryInteractionSnapshot> Bag() => (await context.Snapshots.ReadInventoryInteractionAsync().WaitAsync(token)).Value;
        var departure = await Scene();
        Require(departure.IsReady && !departure.Player!.IsDead, "杂货回程前角色尚未就绪。");
        Require(destination.MapId is > 0 && destination.PointCount > 0, "杂货摆摊路径需要录制地图及入口坐标。");
        var item = FindScroll((await context.Snapshots.ReadInventoryAsync().WaitAsync(token)).Value, itemName)
            ?? throw new GroceryTripDepartureFailedException("背包没有配置的回程卷轴：" + itemName);
        async Task<bool> BeforeClick()
        {
            var scene = await Scene();
            Require(scene.IsReady && !scene.Player!.IsDead && scene.Channel!.MapId == departure.Channel!.MapId &&
                scene.Player!.CharacterName == departure.Player!.CharacterName, "使用卷轴前角色场景改变。");
            if (await safety.FindAttackingTargetNameAsync(context) is { Length: > 0 } attacker)
                throw new GroceryTripInterruptedException("杂货回程被攻击打断，先战斗：" + attacker);
            var ui = await Bag();
            return !ui.ShopIsOpen && !ui.IsSelling && ui.DiscardDialog == null && !ui.OtherModalOpen;
        }
        try
        {
            await actions.Reset();
            Require(await BeforeClick(), "使用卷轴前存在其他背包操作。");
            if (!(await Bag()).IsOpen)
            {
                await actions.Key("I");
                await actions.Wait(Bag, s => s.IsOpen && !s.OtherModalOpen);
            }
            var transition = new TownReturnTransition();
            transition.Start(departure, destination, Now());
            report("使用回程卷轴：" + item.Name + "，等待读条和落点确认");
            await actions.RightClickBag(item, BeforeClick);
            var clickedAt = Now();
            var hp = departure.Player!.CurrentHp;
            while (true)
            {
                token.ThrowIfCancellationRequested();
                var scene = await Scene();
                var phase = transition.Observe(scene);
                if (phase == TownReturnPhase.Dead) throw new CleanupDeathInterruptionException();
                if (phase == TownReturnPhase.Arrived)
                {
                    var bag = await Bag();
                    Require(!bag.OtherModalOpen && bag.DiscardDialog == null && !bag.ShopIsOpen && !bag.IsSelling, "回程后背包操作状态改变。");
                    if (bag.IsOpen) { await actions.Key("I"); await actions.Wait(Bag, s => !s.IsOpen); }
                    report("卷轴回程落点已确认");
                    return;
                }
                // Do not inspect attackers from the departure map once loading starts.
                if (!transition.SawLoading && scene.IsReady && scene.Channel!.MapId == departure.Channel!.MapId)
                {
                    var attacker = await safety.FindAttackingTargetNameAsync(context);
                    if (!string.IsNullOrWhiteSpace(attacker) || scene.Player!.CurrentHp < hp)
                    {
                        await actions.Key("Escape");
                        throw new GroceryTripInterruptedException("杂货回程读条被攻击打断，结束本次出发，先战斗。");
                    }
                    hp = scene.Player!.CurrentHp;
                    if (phase == TownReturnPhase.WaitingForDeparture && Now() - clickedAt >= TimeSpan.FromSeconds(20))
                    {
                        await actions.Key("Escape");
                        // A delayed transfer can race cancellation. Confirm a ready original
                        // scene before returning the worker to normal work.
                        scene = await Scene(); phase = transition.Observe(scene);
                        if (phase == TownReturnPhase.WaitingForDeparture && scene.IsReady)
                            throw new GroceryTripDepartureFailedException("卷轴出发 20 秒仍未完成，本次已结束，之后重新判断触发条件。");
                    }
                }
                report(transition.SawLoading ? "杂货回程正在加载地图，等待落点确认" : "杂货回程等待传送和正确落点");
                await actions.Pause(100);
            }
        }
        finally
        {
            // Closing a still-open bag makes the normal combat shortcut usable again.
            try
            {
                if (!token.IsCancellationRequested)
                {
                    var scene = await Scene();
                    if (scene.IsReady && !scene.Player!.IsDead)
                    {
                        var bag = await Bag();
                        if (bag.IsOpen && !bag.OtherModalOpen && bag.DiscardDialog == null && !bag.ShopIsOpen && !bag.IsSelling)
                        { await actions.Key("I"); await actions.Wait(Bag, s => !s.IsOpen); }
                    }
                }
            }
            finally { await actions.Reset(); }
        }
    }
}
