using static Roadhog.Application.BagCleanup.InventoryDiscardActions;
using Roadhog.Core.Accounts;
using Roadhog.Core.Api;
using Roadhog.Core.Common;
using Roadhog.Core.Diagnostics;
using Roadhog.Core.Input;
using Roadhog.Core.Model;

namespace Roadhog.Application.BagCleanup;

public sealed record InventoryDiscardTestResult(int DiscardedCount, int RemainingCount);

/// <summary>Bounded manual test. Uses UI identities and cursor feedback, never legacy slot estimates.</summary>
public sealed class InventoryDiscardSequence(IKeyboardInput input, IRoadhogLogger logger,
    Func<int, CancellationToken, Task>? delay = null)
{
    public const int TestLimit = 3;

    public async Task<OperationResult<InventoryDiscardTestResult>> RunAsync(IRoadhogSnapshotReader snapshots,
        string account, MaintenanceScriptSettings settings, IProgress<string>? progress, CancellationToken token)
    {
        var stage = "读取丢弃规则";
        var started = false;
        var discarded = 0;
        var actions = new InventoryDiscardActions(input, snapshots, logger, account, delay);
        try
        {
            var inventory = (await snapshots.ReadInventoryAsync().WaitAsync(token).ConfigureAwait(false)).Value;
            var candidates = BagCleanupItemMatcher.SelectDiscardItems(inventory, settings);
            var plan = candidates.Take(TestLimit).ToArray();
            if (plan.Length == 0) return OperationResult<InventoryDiscardTestResult>.Ok(new(0, 0));
            var ui = await Ui();
            RequireIdle(ui);
            var player = (await snapshots.ReadPlayerAsync().WaitAsync(token).ConfigureAwait(false)).Value;
            Require(!player.IsDead, "角色已死亡。");
            started = true;
            if (input is IInputStateReset reset) Check(await reset.ReleaseAllAsync(token).ConfigureAwait(false));
            else
            {
                foreach (var key in new[] { "W", "A", "S", "D", "ControlKey" }) Check(await input.KeyUpAsync(key, token).ConfigureAwait(false));
                Check(await input.MouseUpAsync(RoadhogMouseButton.Left, token).ConfigureAwait(false));
                Check(await input.MouseUpAsync(RoadhogMouseButton.Right, token).ConfigureAwait(false));
            }
            Report("打开背包，最多丢弃 3 项");
            if (!ui.IsOpen)
            {
                Check(await input.PressKeyAsync("I", TimeSpan.FromMilliseconds(60), token).ConfigureAwait(false));
                await Pause(300, token);
                ui = await Wait(u => u.IsOpen);
            }
            foreach (var item in plan)
            {
                Report($"丢弃 {discarded + 1}/{plan.Length}：{item.Name}（整叠 {item.Count}）");
                var currentPlayer = (await snapshots.ReadPlayerAsync().WaitAsync(token).ConfigureAwait(false)).Value;
                Require(!currentPlayer.IsDead && currentPlayer.CharacterName == player.CharacterName, "角色状态已变化。");
                var before = await snapshots.ReadInventoryAsync().WaitAsync(token).ConfigureAwait(false);
                Require(BagCleanupItemMatcher.SelectDiscardItems(before.Value, settings).Any(i => SameItem(i, item)), "物品数量或丢弃资格已变化。");
                await actions.DragAsync(item, token);
                ui = await Wait(u => u.PendingDiscardInstanceId != 0 && u.DiscardDialog != null);
                var confirmed = new HashSet<(int DialogId, InventoryDiscardConfirmKind Kind)>();
                for (var layer = 0; layer < 2; layer++)
                {
                    var dialog = RequireDialog(ui, item);
                    Require(confirmed.Add((dialog.DialogId, dialog.Kind)), "确认框未切换，不重复点击。");
                    await actions.ConfirmAsync(item, dialog.Kind, token);
                    ui = await Wait(u => u.PendingDiscardInstanceId == 0 ||
                        u.DiscardDialog is { } next && (next.DialogId != dialog.DialogId || next.Kind != dialog.Kind));
                    if (ui.PendingDiscardInstanceId == 0) break;
                }
                Require(ui.PendingDiscardInstanceId == 0, "超过两层丢弃确认，已停止。");
                using var verify = CancellationTokenSource.CreateLinkedTokenSource(token);
                verify.CancelAfter(TimeSpan.FromSeconds(4));
                while (true)
                {
                    var after = await snapshots.ReadInventoryAsync(before.Version).WaitAsync(verify.Token).ConfigureAwait(false);
                    if (after.Value.All(i => i.InstanceId != item.InstanceId)) break;
                    await Pause(100, verify.Token);
                }
                discarded++;
                logger.Info("inventory_discard.removed", Fields(item));
            }
            Report($"已丢弃 {discarded} 项，测试结束");
            return OperationResult<InventoryDiscardTestResult>.Ok(new(discarded, candidates.Count - discarded));
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            logger.Warn("inventory_discard.failed", new Dictionary<string, object?>
            { ["account"] = account, ["stage"] = stage, ["discarded"] = discarded, ["error"] = ex.Message });
            return OperationResult<InventoryDiscardTestResult>.Fail($"已丢弃 {discarded} 项；{stage}：{ex.Message}");
        }
        finally
        {
            if (started)
            {
                if (input is IInputStateReset reset) await reset.ReleaseAllAsync(CancellationToken.None).ConfigureAwait(false);
                else
                {
                    await input.MouseUpAsync(RoadhogMouseButton.Left, CancellationToken.None).ConfigureAwait(false);
                    await input.MouseUpAsync(RoadhogMouseButton.Right, CancellationToken.None).ConfigureAwait(false);
                }
            }
        }

        void Report(string text) { stage = text; progress?.Report(text); }
        Dictionary<string, object?> Fields(InventoryItemSnapshot item) => new()
        { ["account"] = account, ["name"] = item.Name, ["instanceId"] = item.InstanceId, ["templateId"] = item.TemplateId, ["quantity"] = item.Count };
        async Task<InventoryInteractionSnapshot> Ui() => (await snapshots.ReadInventoryInteractionAsync().WaitAsync(token).ConfigureAwait(false)).Value;
        async Task<InventoryInteractionSnapshot> Wait(Func<InventoryInteractionSnapshot, bool> condition)
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
            deadline.CancelAfter(TimeSpan.FromSeconds(4));
            try
            {
                while (true)
                {
                    var value = (await snapshots.ReadInventoryInteractionAsync().WaitAsync(deadline.Token).ConfigureAwait(false)).Value;
                    if (condition(value)) return value;
                    await Pause(100, deadline.Token);
                }
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested)
            { throw new TimeoutException("游戏未确认操作，已停止，不重复点击。"); }
        }
    }

    private Task Pause(int ms, CancellationToken token) => delay?.Invoke(ms, token) ?? Task.Delay(ms, token);
}
