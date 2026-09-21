using Roadhog.Application.Input;
using Roadhog.Application.Trading;
using Roadhog.Core.Accounts;
using Roadhog.Core.Api;
using Roadhog.Core.Common;
using Roadhog.Core.Diagnostics;
using Roadhog.Core.Input;
using Roadhog.Core.Model;

namespace Roadhog.Application.AuctionHouse;

/// <summary>Non-committing diagnostic: visits tabs, searches one configured item, opens/cancels its editor.</summary>
public sealed class AuctionHouseTestSequence(IKeyboardInput input, IRoadhogLogger logger, Func<int, CancellationToken, Task>? delay = null)
{
    public async Task<OperationResult<string>> RunAsync(IRoadhogSnapshotReader snapshots, IReadOnlyList<BagCleanupTradeItemConfig> configured,
        IProgress<string>? progress, CancellationToken token, string? configuredNpcName = null)
    {
        var stage = "读取拍卖行"; bool started = false;
        var inventoryActions = new TradingActions(input, snapshots, token, delay);
        try
        {
            var player = (await snapshots.ReadPlayerAsync().WaitAsync(token)).Value;
            if (player.IsDead) throw new InvalidOperationException("角色已死亡。");
            var ui = await Ui();
            if (ui.Editor != null || ui.RegistrationConfirmation != null || ui.WithdrawConfirmation != null || ui.OtherModalOpen)
                throw new InvalidOperationException("请先关闭已有交易弹窗。");
            started = true;
            if (input is IInputStateReset reset) Check(await reset.ReleaseAllAsync(token));
            else { foreach (var key in new[] { "W", "A", "S", "D", "ControlKey" }) Check(await input.KeyUpAsync(key, token)); await ReleaseMouse(); }
            if (!ui.IsOpen)
            {
                Report("选择拍卖行NPC");
                var broker = new AuctionBrokerSelector(input, snapshots, configuredNpcName, token, delay);
                await broker.SelectAsync();
                await CheckPlayer();
                ui = await Ui();
                if (!await broker.IsSelectedAsync()) throw new InvalidOperationException("未确认拍卖行NPC。");
                Report("等待NPC对话");
                if (!ui.DialogOpen) { await Key("C"); ui = await Wait(s => s.DialogOpen && s.TradeButton != null); }
                // Recheck selected NPC before clicking an existing conversation.
                if (!await broker.IsSelectedAsync()) throw new InvalidOperationException("选中NPC已变化。");
                await Click(s => s.TradeButton, s => s.DialogOpen && !s.IsOpen);
                ui = await Wait(s => s.IsOpen);
            }
            Report("检查计算页（不领取金币）");
            await Tab("account_btn", 2);
            ui = await Wait(s => s.ActiveTab == 2 && s.SettlementLoaded);
            var settlement = ui.SettlementMoney;
            Report($"已售待领取：{settlement:N0}；检查目录页");
            await Tab("item_list_btn", 0);
            var inventory = (await snapshots.ReadInventoryAsync().WaitAsync(token)).Value;
            var pair = configured.Select(rule => (Rule: rule, Item: inventory.FirstOrDefault(i => !i.IsEquipped && i.Count > 0 && i.Name.Contains(rule.Name, StringComparison.OrdinalIgnoreCase))))
                .FirstOrDefault(p => p.Item != null);
            string detail;
            if (pair.Item == null) detail = "已验证打开和页签；拍卖行名单没有匹配的背包物品";
            else
            {
                var item = pair.Item; var rule = pair.Rule;
                Report("查价：" + item.Name);
                var count = 0;
                if (rule.PriceLookupMethod == AuctionPriceLookupMethod.SearchCalculation)
                {
                    // Clear prior query/results so an old result cannot satisfy this test's completion condition.
                    await Click(s => s.Button("search_cancel_btn"), s => s.IsOpen && s.ActiveTab == 0 && s.Editor == null);
                    ui = await Wait(s => s.SearchName.Length == 0 && s.MarketRows.Count == 0);
                    var initialMessage = ui.ResultMessage;
                    await RightClickItem(item, 0);
                    ui = await Wait(s => s.SearchName == item.Name && s.ActiveTab == 0);
                    await Click(s => s.Button("search_btn"), s => s.ActiveTab == 0 && s.SearchName == item.Name);
                    ui = await Wait(s => s.ActiveTab == 0 && s.SearchName == item.Name &&
                        (s.MarketRows.Count > 0 || (s.ResultMessage.Length > 0 && s.ResultMessage != initialMessage)));
                    if (ui.MarketRows.Any(r => r.TemplateId != item.TemplateId)) throw new InvalidOperationException("搜索包含其他物品，停止查价。");
                    count = ui.MarketRows.Count;
                    Report($"已读取 {count} 条报价；检查上架弹窗");
                }
                else Report("检查上架弹窗：" + item.Name);
                await Tab("register_item_btn", 1);
                await RightClickItem(item, 1);
                ui = await Wait(s => s.Editor != null);
                if (ui.Editor!.TemplateId != item.TemplateId || ui.Editor.Quantity != item.Count) throw new InvalidOperationException("弹窗物品或数量不匹配。");
                var minimum = ui.Editor.MarketMinimum;
                detail = rule.PriceLookupMethod switch
                {
                    AuctionPriceLookupMethod.DialogMinimum => $"{item.Name}：弹窗最低价 " + (minimum?.ToString("N0") ?? "暂无报价"),
                    AuctionPriceLookupMethod.SearchCalculation => $"{item.Name}：已读 {count} 条报价，搜索定价算法待定义",
                    _ => $"{item.Name}：手动单价 " + (rule.EffectiveUnitPrice?.ToString("N0") ?? "未设置")
                };
                await Click(s => s.Editor?.CancelButton, s => s.Editor?.TemplateId == item.TemplateId);
                await Wait(s => s.Editor == null);
            }
            Report("关闭拍卖行");
            await Key("Space"); await Wait(s => !s.IsOpen && s.Editor == null);
            var result = $"{detail}；待领取 {settlement:N0}。未提交出售或领取金币。";
            logger.Info("auction_house.test_completed", new Dictionary<string, object?> { ["summary"] = result });
            return OperationResult<string>.Ok(result);

            async Task CheckPlayer()
            {
                var current = (await snapshots.ReadPlayerAsync().WaitAsync(token)).Value;
                if (current.IsDead || current.CharacterName != player.CharacterName) throw new InvalidOperationException("角色状态变化，测试停止。");
            }
            async Task Key(string key) { await CheckPlayer(); Check(await input.PressKeyAsync(key, TimeSpan.FromMilliseconds(60), token)); }
            async Task Click(Func<AuctionHouseSnapshot, GameUiPoint?> locate, Func<AuctionHouseSnapshot, bool> guard)
            {
                var before = await Ui(); var point = locate(before);
                if (point == null || !guard(before)) throw new InvalidOperationException("目标按钮不可点击。");
                await new FeedbackMouseMover(input, snapshots, delay).MoveAsync(point, token);
                await CheckPlayer(); var after = await Ui(); var cursor = (await snapshots.ReadUiCursorAsync().WaitAsync(token)).Value.Position;
                if (!guard(after) || locate(after) != point || Math.Abs(point.X - cursor.X) > 1 || Math.Abs(point.Y - cursor.Y) > 1)
                    throw new InvalidOperationException("点击前界面或光标变化。");
                await Mouse(RoadhogMouseButton.Left);
            }
            async Task Tab(string button, int tab)
            {
                if ((await Ui()).ActiveTab != tab) await Click(s => s.Button(button), s => s.IsOpen && s.Editor == null);
                await Wait(s => s.ActiveTab == tab && s.IsOpen);
            }
            async Task RightClickItem(InventoryItemSnapshot item, int tab)
            {
                bool MatchesAuction(AuctionHouseSnapshot s) => s.IsOpen && s.ActiveTab == tab && s.Editor == null &&
                    s.WithdrawConfirmation == null && s.RegistrationConfirmation == null && !s.OtherModalOpen &&
                    s.Button(tab == 0 ? "item_list_btn" : "register_item_btn") != null;
                await inventoryActions.BringBagToFront(async () =>
                {
                    await CheckPlayer();
                    return MatchesAuction(await Ui());
                });
                var bag = (await snapshots.ReadInventoryInteractionAsync().WaitAsync(token)).Value;
                var entry = bag.Items.SingleOrDefault(i => i.InstanceId == item.InstanceId && i.TemplateId == item.TemplateId && i.Quantity == item.Count)
                    ?? throw new InvalidOperationException("目标物品不在可见背包中。");
                await new FeedbackMouseMover(input, snapshots, delay).MoveAsync(entry.Point, token);
                bool MatchesBag(InventoryInteractionSnapshot s) => s.IsOpen && !s.OtherModalOpen && s.DiscardDialog == null &&
                    !s.ShopIsOpen && !s.IsSelling && s.HoveredInstanceId == item.InstanceId && s.Items.Contains(entry);
                await WaitBag(MatchesBag);
                await CheckPlayer(); var auction = await Ui();
                if (!MatchesAuction(auction))
                    throw new InvalidOperationException("右键前拍卖行状态变化。");
                if (!MatchesBag((await snapshots.ReadInventoryInteractionAsync().WaitAsync(token)).Value)) throw new InvalidOperationException("右键前背包物品变化。");
                var cursor = (await snapshots.ReadUiCursorAsync().WaitAsync(token)).Value.Position;
                if (Math.Abs(cursor.X - entry.Point.X) > 1 || Math.Abs(cursor.Y - entry.Point.Y) > 1) throw new InvalidOperationException("右键前光标变化。");
                await Mouse(RoadhogMouseButton.Right);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            logger.Warn("auction_house.test_failed", new Dictionary<string, object?> { ["stage"] = stage, ["error"] = ex.Message });
            return OperationResult<string>.Fail(stage + "：" + (ex is OperationCanceledException ? "操作未得到确认，已停止。" : ex.Message));
        }
        finally { if (started) { if (input is IInputStateReset reset) await reset.ReleaseAllAsync(CancellationToken.None); else await ReleaseMouse(); } }

        void Report(string text) { stage = text; progress?.Report(text); }
        async Task<AuctionHouseSnapshot> Ui() => (await snapshots.ReadAuctionHouseAsync().WaitAsync(token)).Value;
        async Task<AuctionHouseSnapshot> Wait(Func<AuctionHouseSnapshot, bool> predicate)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(8));
            while (true) { var state = (await snapshots.ReadAuctionHouseAsync().WaitAsync(timeout.Token)).Value; if (predicate(state)) return state; await Pause(100, timeout.Token); }
        }
        async Task WaitBag(Func<InventoryInteractionSnapshot, bool> predicate)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(4));
            while (true) { var state = (await snapshots.ReadInventoryInteractionAsync().WaitAsync(timeout.Token)).Value; if (predicate(state)) return; await Pause(100, timeout.Token); }
        }
        async Task Mouse(RoadhogMouseButton button)
        {
            try { Check(await input.MouseDownAsync(button, token)); await Pause(35, token); }
            finally { Check(await input.MouseUpAsync(button, CancellationToken.None)); }
        }
        async Task ReleaseMouse() { await input.MouseUpAsync(RoadhogMouseButton.Left, CancellationToken.None); await input.MouseUpAsync(RoadhogMouseButton.Right, CancellationToken.None); }
    }
    private Task Pause(int ms, CancellationToken token) => delay?.Invoke(ms, token) ?? Task.Delay(ms, token);
    private static void Check(OperationResult result) { if (!result.Success) throw new InvalidOperationException(result.Error); }
}
