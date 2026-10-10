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
    public const int MaxScrollClicks = 3;
    public static readonly TimeSpan DepartureTimeout = TimeSpan.FromSeconds(20);
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
        var transition = new TownReturnTransition();
        transition.Start(departure, destination, Now());
        async Task<bool> BeforeClick()
        {
            var scene = await Scene();
            if (scene.IsReady && scene.Player!.IsDead) throw new CleanupDeathInterruptionException();
            if (transition.Observe(scene) != TownReturnPhase.WaitingForDeparture) return false;
            Require(scene.IsReady && !scene.Player!.IsDead && scene.Channel!.MapId == departure.Channel!.MapId &&
                scene.Channel.Index == departure.Channel.Index &&
                scene.Player!.CharacterName == departure.Player!.CharacterName && scene.Player.EntityId == departure.Player.EntityId,
                "使用卷轴前角色场景改变。");
            await CheckAttack(scene);
            var ui = await Bag();
            return !ui.ShopIsOpen && !ui.IsSelling && ui.DiscardDialog == null && !ui.OtherModalOpen;
        }
        var hp = departure.Player!.CurrentHp;
        async Task CheckAttack(ChannelTransitionSnapshot scene)
        {
            var attacker = await safety.FindAttackingTargetNameAsync(context);
            if (!string.IsNullOrWhiteSpace(attacker) || scene.Player!.CurrentHp < hp)
            {
                await actions.Key("Escape");
                throw new GroceryTripInterruptedException("杂货回程被攻击打断，先战斗：" + attacker);
            }
            hp = scene.Player!.CurrentHp;
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
            report("使用回程卷轴：" + item.Name + "，等待读条和落点确认");
            await actions.RightClickBag(item, BeforeClick);
            var clickedAt = Now();
            var clicks = 1;
            while (true)
            {
                token.ThrowIfCancellationRequested();
                var scene = await Scene();
                if (scene.IsReady)
                    Require(scene.Player!.CharacterName == departure.Player!.CharacterName,
                        "回程期间角色身份改变。");
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
                    await CheckAttack(scene);
                    if (phase == TownReturnPhase.WaitingForDeparture && Now() - clickedAt >= DepartureTimeout)
                    {
                        await actions.Key("Escape");
                        // A delayed transfer can race cancellation. Confirm a ready original
                        // scene before returning the worker to normal work.
                        await actions.Pause(500);
                        scene = await Scene(); phase = transition.Observe(scene);
                        if (phase == TownReturnPhase.Dead) throw new CleanupDeathInterruptionException();
                        if (phase == TownReturnPhase.WaitingForDeparture && scene.IsReady)
                        {
                            await CheckAttack(scene);
                            Require(scene.Channel!.Index == departure.Channel!.Index &&
                                scene.Player!.EntityId == departure.Player!.EntityId && scene.Player.CharacterName == departure.Player.CharacterName,
                                "重试回程前角色场景改变。");
                            if (clicks >= MaxScrollClicks)
                                throw new GroceryTripDepartureFailedException("回程卷轴已尝试 3 次仍未出发，结束本次任务，之后重新判断触发条件。");
                            if (!await BeforeClick())
                            {
                                if (transition.Phase != TownReturnPhase.WaitingForDeparture) continue;
                                throw new InvalidOperationException("重试卷轴前存在其他背包操作。");
                            }
                            var retryInventory = (await context.Snapshots.ReadInventoryAsync().WaitAsync(token)).Value;
                            if (transition.Observe(await Scene()) != TownReturnPhase.WaitingForDeparture) continue;
                            item = FindScroll(retryInventory, itemName)
                                ?? throw new GroceryTripDepartureFailedException("重试时所选回程卷轴已不存在：" + itemName);
                            if (!(await Bag()).IsOpen)
                            {
                                await actions.Key("I");
                                await actions.Wait(Bag, s => s.IsOpen && !s.OtherModalOpen);
                            }
                            report($"回程尚未出发，重新右键卷轴（{clicks + 1}/{MaxScrollClicks}）");
                            // Recheck the transition immediately before the input, including delayed loading.
                            try { await actions.RightClickBag(item, BeforeClick); }
                            catch (InvalidOperationException) when (transition.Phase is TownReturnPhase.Loading or TownReturnPhase.WaitingForDestination or TownReturnPhase.Arrived)
                            { continue; }
                            clicks++;
                            clickedAt = Now();
                            context.Logger.Info("grocery_shop.return.retry", new Dictionary<string, object?>
                                { ["account"] = context.Config.AccountName, ["clicks"] = clicks, ["instanceId"] = item.InstanceId });
                        }
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
