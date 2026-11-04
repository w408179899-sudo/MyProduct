using System.Text.RegularExpressions;
using Roadhog.Application.Trading;
using Roadhog.Core.Api;
using Roadhog.Core.Common;
using Roadhog.Core.Diagnostics;
using Roadhog.Core.Input;
using Roadhog.Core.Model;

namespace Roadhog.Application.ManastoneExchange;

public sealed class ManastoneExchangeSequence(IKeyboardInput input, IRoadhogLogger logger,
    Func<int, CancellationToken, Task>? delay = null)
{
    public async Task<OperationResult<ManastoneExchangeResult>> RunAsync(IRoadhogSnapshotReader snapshots, string account,
        IProgress<string>? progress, CancellationToken token, int maximumCompletions = int.MaxValue)
    {
        var completed = new int[2];
        var actions = new TradingActions(input, snapshots, token, delay);
        try
        {
            TradingActions.Require(maximumCompletions > 0, "兑换次数必须大于零。");
            var target = await ManastoneExchangeNpcSelector.SelectAsync(snapshots, actions, progress, token);
            var npc = target.ServerObjectId;
            async Task<ManastoneExchangeSnapshot> Ui()
            {
                token.ThrowIfCancellationRequested();
                var locked = (await snapshots.ReadLockedTargetAsync().WaitAsync(token)).Value;
                TradingActions.Require(locked.HasTarget && locked.ServerObjectId == npc && locked.Name == target.Name,
                    "选中的兑换 NPC 已改变，兑换停止。");
                return (await snapshots.ReadManastoneExchangeAsync().WaitAsync(token)).Value;
            }
            Task<IReadOnlyList<InventoryItemSnapshot>> Inventory() => ReadInventory(snapshots, token);
            bool Owned(ManastoneExchangeSnapshot s) => s.IsOpen && !s.OtherModalOpen && s.NpcServerObjectId == npc;
            async Task<ManastoneExchangeSnapshot> Open()
            {
                var state = await Ui();
                TradingActions.Require(!state.OtherModalOpen, "请先关闭其他确认窗口。");
                if (!state.IsOpen)
                {
                    await actions.Key("C");
                    state = await actions.Wait(Ui, s => s.IsOpen);
                }
                TradingActions.Require(Owned(state), "当前对话不属于选中的兑换 NPC。");
                return state;
            }
            var next = 0;
            while (completed.Sum() < maximumCompletions)
            {
                await actions.Alive();
                var before = await Inventory();
                var state = await Ui();
                TradingActions.Require(!state.OtherModalOpen && (!state.IsOpen || Owned(state)), "当前对话不属于选中的兑换 NPC 或被其他窗口遮挡。");
                var pendingIndex = state.IsOpen && state.PageType == 4
                    ? ManastoneExchangePlan.Quests.ToList().FindIndex(q => q.QuestId == state.QuestId) : -1;
                var materialsAlreadySubmitted = pendingIndex >= 0;
                var eligible = Enumerable.Range(0, 2).Select(i => (next + i) % 2)
                    .Where(i => ManastoneExchangePlan.Quests[i].Available(before) > 0).ToArray();
                if (eligible.Length == 0 && !materialsAlreadySubmitted)
                {
                    var message = $"兑换完成：魔石袋 {completed[0]} 个，魔石包袱 {completed[1]} 个；两个批量任务的材料均不足。";
                    progress?.Report(message);
                    logger.Info("manastone_exchange.exhausted", new Dictionary<string, object?>
                        { ["account"] = account, ["bags"] = completed[0], ["bundles"] = completed[1] });
                    return OperationResult<ManastoneExchangeResult>.Ok(new(completed[0], completed[1], true, message));
                }
                state = await Open();
                var index = materialsAlreadySubmitted ? pendingIndex : eligible[0];
                if (state.QuestId != 0)
                {
                    var activeIndex = ManastoneExchangePlan.Quests.ToList().FindIndex(q => q.QuestId == state.QuestId);
                    TradingActions.Require(activeIndex >= 0, "当前打开的是其他任务，请关闭后重新开始。");
                    if (materialsAlreadySubmitted || eligible.Contains(activeIndex)) index = activeIndex;
                    else
                    {
                        await actions.Click(Ui, s => s.CloseButton, s => Owned(s) && s.QuestId == state.QuestId);
                        await actions.Wait(Ui, s => !s.IsOpen);
                        state = await Open();
                    }
                }
                var quest = ManastoneExchangePlan.Quests[index];
                bool QuestPage(ManastoneExchangeSnapshot s) => Owned(s) && s.QuestId == quest.QuestId;
                ManastoneDialogEntry? Entry(ManastoneExchangeSnapshot s, string text) =>
                    s.Entries.SingleOrDefault(e => e.Href == "select1" && e.Text.TrimEnd('。', '.', ' ') == text);
                progress?.Report($"正在兑换{quest.Name}；已完成魔石袋 {completed[0]} 个、魔石包袱 {completed[1]} 个。");
                if (state.QuestId == 0)
                {
                    var href = "QuestSelect" + quest.QuestId;
                    await actions.Click(Ui, s => s.Entries.SingleOrDefault(e => e.Href == href && e.Text.Contains("[魔石批量交换]"))?.Point,
                        s => Owned(s) && s.QuestId == 0);
                    state = await actions.Wait(Ui, QuestPage);
                }
                if (Entry(state, "接受") is not null)
                {
                    await actions.Click(Ui, s => Entry(s, "接受")?.Point, s => QuestPage(s) && s.PageType == 1);
                    state = await actions.Wait(Ui, s => QuestPage(s) && Entry(s, "交出魔石") is not null);
                }
                if (Entry(state, "交出魔石") is not null)
                {
                    TradingActions.Require(quest.Available(await Inventory()) > 0, "提交前材料数量已改变，兑换停止。");
                    await actions.Click(Ui, s => Entry(s, "交出魔石")?.Point, s => QuestPage(s) && s.PageType == 1);
                    state = await actions.Wait(Ui, s => QuestPage(s) && s.PageType == 4);
                }
                bool Reward(ManastoneExchangeSnapshot s) => QuestPage(s) && s.PageType == 4 &&
                    s.PageName == "select_quest_reward1" && Regex.IsMatch(s.Html,
                        "<item\\b[^>]*\\bitemnameid=\"" + quest.RewardId + "\"[^>]*\\bitemcount=\"1\"", RegexOptions.CultureInvariant);
                TradingActions.Require(Reward(state), "任务奖励页面与预期魔石包不符，兑换停止。");
                if (!materialsAlreadySubmitted)
                    await actions.Wait(Inventory, after => quest.MaterialIds.All(id =>
                        ManastoneExchangePlan.Count(before, id, true) >= 10 &&
                        ManastoneExchangePlan.Count(after, id, true) == ManastoneExchangePlan.Count(before, id, true) - 10));
                progress?.Report($"{quest.Name}：材料已交出，正在领取奖励。");
                // Exactly one submission; wait for both consumption and reward before considering another round.
                await actions.Click(Ui, s => s.ConfirmButton, Reward);
                await actions.Wait(Inventory, after => ManastoneExchangePlan.Confirmed(quest, before, after, materialsAlreadySubmitted));
                completed[index]++;
                logger.Info("manastone_exchange.completed", new Dictionary<string, object?>
                    { ["account"] = account, ["questId"] = quest.QuestId, ["rewardId"] = quest.RewardId,
                        ["bags"] = completed[0], ["bundles"] = completed[1] });
                await actions.Wait(Ui, s => !s.IsOpen);
                next = (index + 1) % 2;
            }
            return OperationResult<ManastoneExchangeResult>.Ok(new(completed[0], completed[1], false,
                $"本轮验证完成：魔石袋 {completed[0]} 个，魔石包袱 {completed[1]} 个。"));
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        { return OperationResult<ManastoneExchangeResult>.Ok(new(completed[0], completed[1], false, $"已停止兑换：魔石袋 {completed[0]} 个，魔石包袱 {completed[1]} 个。")); }
        catch (Exception ex)
        {
            logger.Warn("manastone_exchange.stopped", new Dictionary<string, object?> { ["account"] = account, ["reason"] = ex.Message });
            return OperationResult<ManastoneExchangeResult>.Fail($"{ex.Message} 已确认魔石袋 {completed[0]} 个、魔石包袱 {completed[1]} 个。");
        }
        finally
        {
            if (input is IInputStateReset reset)
            {
                using var release = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                try { await reset.ReleaseAllAsync(release.Token).WaitAsync(release.Token); }
                catch (Exception ex) { logger.Warn("manastone_exchange.input_release_failed", new Dictionary<string, object?> { ["account"] = account, ["reason"] = ex.Message }); }
            }
        }
    }

    private static async Task<IReadOnlyList<InventoryItemSnapshot>> ReadInventory(IRoadhogSnapshotReader snapshots, CancellationToken token) =>
        (await snapshots.ReadInventoryAsync().WaitAsync(token)).Value;
}
