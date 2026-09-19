using Roadhog.Core.Accounts;
using Roadhog.Core.Api;
using Roadhog.Core.Common;
using Roadhog.Core.Diagnostics;
using Roadhog.Core.Input;
using Roadhog.Core.Model;

namespace Roadhog.Application.PersonalShop;

public sealed record PersonalShopTestResult(int RegisteredCount, int RemainingCount, bool AlreadySelling);

/// <summary>Validated cursor feedback and hover timing; confirms each action through official UI snapshots.</summary>
public sealed class PersonalShopSequence(IKeyboardInput input, IRoadhogLogger logger,
    Func<int, CancellationToken, Task>? delay = null)
{
    public async Task<OperationResult<PersonalShopTestResult>> RunAsync(IRoadhogSnapshotReader snapshots, string account,
        MaintenanceScriptSettings settings, IProgress<string>? progress, CancellationToken token)
    {
        var stage = "读取摊位";
        var inputStarted = false;
        try
        {
            var ui = await Ui();
            if (ui.IsSelling) return OperationResult<PersonalShopTestResult>.Ok(new(ui.Listings.Count, 0, true));
            if (ui.Editor != null || ui.Listings.Count != 0) throw new InvalidOperationException("请先取消已有登记并关闭价格输入框，再测试摆摊。");
            var player = (await snapshots.ReadPlayerAsync().WaitAsync(token).ConfigureAwait(false)).Value;
            if (player.IsDead) throw new InvalidOperationException("角色已死亡，无法摆摊。");
            var inventory = (await snapshots.ReadInventoryAsync().WaitAsync(token).ConfigureAwait(false)).Value;
            var candidates = BagCleanupItemMatcher.SelectSellRegistrationItems(inventory, settings);
            var plan = candidates.Take(10).ToArray();
            if (plan.Length == 0) return OperationResult<PersonalShopTestResult>.Ok(new(0, 0, false));

            inputStarted = true;
            foreach (var key in new[] { "W", "A", "S", "D", "ControlKey" }) Check(await input.KeyUpAsync(key, token).ConfigureAwait(false));
            Check(await input.MouseUpAsync(RoadhogMouseButton.Right, token).ConfigureAwait(false));
            Check(await input.MouseUpAsync(RoadhogMouseButton.Left, token).ConfigureAwait(false));
            Report("打开摊位和背包");
            if (!ui.IsOpen) { await Key("Y"); await Pause(300, token); ui = await Wait(u => u.IsOpen); }
            if (!ui.InventoryOpen) { await Key("I"); await Pause(300, token); ui = await Wait(u => u.InventoryOpen); }
            for (var index = 0; index < plan.Length; index++)
            {
                var item = plan[index];
                Report($"登记 {index + 1}/{plan.Length}：{item.Name}（整叠 {item.Count}，单价 1）");
                var current = (await snapshots.ReadPlayerAsync().WaitAsync(token).ConfigureAwait(false)).Value;
                if (current.IsDead || current.CharacterName != player.CharacterName) throw new InvalidOperationException("角色状态已变化，本次停止摆摊。");
                ui = await Ui();
                if (ui.IsSelling || !ui.IsOpen || !ui.InventoryOpen || ui.Editor != null) throw new InvalidOperationException("摆摊界面已变化。");
                PersonalShopPoint? Locate(PersonalShopSnapshot value) => value.BagItems.SingleOrDefault(i =>
                    i.InstanceId == item.InstanceId && i.TemplateId == item.TemplateId && i.Quantity == item.Count)?.Point;
                var point = Locate(ui) ?? throw new InvalidOperationException("物品不在可见背包格子中。");
                // Reopening inventory can leave hover empty until a real mouse event arrives.
                await Move(new(point.X + (point.X >= 5 ? -5 : 5), point.Y + (point.Y >= 5 ? -5 : 5)));
                await Move(point);
                await Pause(350, token);
                await Click(Locate, RoadhogMouseButton.Right, u => u.HoveredInstanceId == item.InstanceId && u.IsOpen && !u.IsSelling && u.Editor == null);
                await Pause(300, token);
                ui = await Wait(u => u.Editor != null);
                CheckEditor(ui, item, requirePrice: false);
                await Click(u => u.Editor?.PriceInput, RoadhogMouseButton.Left, u => EditorMatches(u, item, false), ui);
                Check(await input.KeyDownAsync("ControlKey", token).ConfigureAwait(false));
                try { await Key("A", 40); }
                finally { Check(await input.KeyUpAsync("ControlKey", CancellationToken.None).ConfigureAwait(false)); }
                await Key("D1");
                await Pause(250, token);
                ui = await Wait(u => EditorMatches(u, item, true));
                CheckEditor(ui, item, requirePrice: true);
                await Click(u => u.Editor?.ConfirmButton, RoadhogMouseButton.Left, u => EditorMatches(u, item, true), ui);
                await Pause(300, token);
                ui = await Wait(u => u.Editor == null && u.Listings.Any(e => Matches(e, item)));
                ValidatePlan(plan.Take(index + 1).ToArray(), ui.Listings);
                logger.Info("personal_shop.item_registered", new Dictionary<string, object?>
                { ["account"] = account, ["name"] = item.Name, ["instanceId"] = item.InstanceId, ["quantity"] = item.Count, ["unitPrice"] = 1 });
            }

            Report("开始出售");
            if (ui.InventoryOpen) { await Key("I"); await Pause(300, token); ui = await Wait(u => !u.InventoryOpen); }
            ValidatePlan(plan, ui.Listings);
            await Click(u => u.StartButton, RoadhogMouseButton.Left, u =>
            {
                ValidatePlan(plan, u.Listings);
                return u.IsOpen && !u.IsSelling && !u.InventoryOpen && u.Editor == null;
            }, ui);
            ui = await Wait(u => u.IsSelling);
            logger.Info("personal_shop.selling", new Dictionary<string, object?> { ["account"] = account, ["registered"] = plan.Length, ["remaining"] = candidates.Count - plan.Length });
            return OperationResult<PersonalShopTestResult>.Ok(new(plan.Length, candidates.Count - plan.Length, false));
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            logger.Info("personal_shop.cancelled", new Dictionary<string, object?> { ["account"] = account, ["stage"] = stage });
            throw;
        }
        catch (Exception ex)
        {
            logger.Warn("personal_shop.failed", new Dictionary<string, object?> { ["account"] = account, ["stage"] = stage, ["error"] = ex.Message });
            return OperationResult<PersonalShopTestResult>.Fail(stage + "：" + ex.Message);
        }
        finally
        {
            if (inputStarted)
            {
                if (input is IInputStateReset reset) await reset.ReleaseAllAsync(CancellationToken.None).ConfigureAwait(false);
                else
                {
                    await input.KeyUpAsync("ControlKey", CancellationToken.None).ConfigureAwait(false);
                    await input.MouseUpAsync(RoadhogMouseButton.Left, CancellationToken.None).ConfigureAwait(false);
                    await input.MouseUpAsync(RoadhogMouseButton.Right, CancellationToken.None).ConfigureAwait(false);
                }
            }
        }

        void Report(string text) { stage = text; progress?.Report(text); }
        async Task Key(string key, int ms = 60) => Check(await input.PressKeyAsync(key, TimeSpan.FromMilliseconds(ms), token).ConfigureAwait(false));
        async Task<PersonalShopSnapshot> Ui() => (await snapshots.ReadPersonalShopAsync().WaitAsync(token).ConfigureAwait(false)).Value;
        async Task<PersonalShopSnapshot> Wait(Func<PersonalShopSnapshot, bool> condition)
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
            deadline.CancelAfter(TimeSpan.FromSeconds(4));
            try
            {
                while (true)
                {
                    var state = (await snapshots.ReadPersonalShopAsync().WaitAsync(deadline.Token).ConfigureAwait(false)).Value;
                    if (condition(state)) return state;
                    await Pause(100, deadline.Token);
                }
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested) { throw new TimeoutException("游戏未确认操作，已停止，不重复点击。"); }
        }
        async Task Move(PersonalShopPoint point)
        {
            for (var attempt = 0; attempt < 60; attempt++)
            {
                var cursor = (await snapshots.ReadPersonalShopCursorAsync().WaitAsync(token).ConfigureAwait(false)).Value;
                if (point.X < 0 || point.Y < 0 || point.X >= cursor.Width || point.Y >= cursor.Height) throw new InvalidOperationException("目标在游戏窗口外。");
                var dx = point.X - cursor.Position.X; var dy = point.Y - cursor.Position.Y;
                if (Math.Abs(dx) <= 1 && Math.Abs(dy) <= 1) return;
                Check(await input.MoveMouseRelativeAsync(Math.Clamp(dx, -70, 70), Math.Clamp(dy, -70, 70), token).ConfigureAwait(false));
                await Pause(70, token);
            }
            throw new TimeoutException("鼠标未到达目标。");
        }
        async Task Click(Func<PersonalShopSnapshot, PersonalShopPoint?> locate, RoadhogMouseButton button,
            Func<PersonalShopSnapshot, bool> guard, PersonalShopSnapshot? captured = null)
        {
            var before = captured ?? await Ui();
            var point = locate(before) ?? throw new InvalidOperationException("目标控件不可点击。");
            if (!guard(before)) throw new InvalidOperationException("操作对象已变化。");
            await Move(point);
            var after = await Ui();
            var cursor = (await snapshots.ReadPersonalShopCursorAsync().WaitAsync(token).ConfigureAwait(false)).Value.Position;
            var actualPoint = locate(after);
            if (!guard(after) || actualPoint == null || Math.Abs(actualPoint.X - cursor.X) > 1 || Math.Abs(actualPoint.Y - cursor.Y) > 1)
                throw new InvalidOperationException("点击前目标或鼠标位置已变化。");
            token.ThrowIfCancellationRequested();
            try
            {
                Check(await input.MouseDownAsync(button, token).ConfigureAwait(false));
                await Pause(35, token);
            }
            finally { Check(await input.MouseUpAsync(button, CancellationToken.None).ConfigureAwait(false)); }
        }
    }

    private Task Pause(int ms, CancellationToken token) => delay?.Invoke(ms, token) ?? Task.Delay(ms, token);
    private static bool Matches(PersonalShopListing listing, InventoryItemSnapshot item) =>
        listing.InstanceId == item.InstanceId && listing.TemplateId == item.TemplateId && listing.Quantity == item.Count && listing.UnitPrice == 1;
    private static bool EditorMatches(PersonalShopSnapshot state, InventoryItemSnapshot item, bool requirePrice) =>
        state.Editor is { } e && state.IsOpen && !state.IsSelling && e.InstanceId == item.InstanceId && e.Quantity == item.Count &&
        (!requirePrice || e.UnitPriceMode && e.UnitPrice == 1 && e.TotalPrice == item.Count);
    private static void CheckEditor(PersonalShopSnapshot state, InventoryItemSnapshot item, bool requirePrice)
    {
        if (!EditorMatches(state, item, requirePrice)) throw new InvalidOperationException("登记物品、数量或单价与计划不一致。");
    }
    internal static void ValidatePlan(IReadOnlyList<InventoryItemSnapshot> plan, IReadOnlyList<PersonalShopListing> listings)
    {
        if (plan.Count is < 1 or > 10 || listings.Count != plan.Count || listings.Select(i => i.InstanceId).Distinct().Count() != listings.Count ||
            listings.Any(e => !plan.Any(i => Matches(e, i)))) throw new InvalidOperationException("登记清单与本次出售计划不一致。");
    }
    private static void Check(OperationResult result) { if (!result.Success) throw new InvalidOperationException(result.Error ?? "输入失败。"); }
}
