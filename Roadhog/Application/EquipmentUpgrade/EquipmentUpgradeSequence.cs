using Roadhog.Application.Input;
using Roadhog.Core.Accounts;
using Roadhog.Core.Api;
using Roadhog.Core.Common;
using Roadhog.Core.Diagnostics;
using Roadhog.Core.Input;
using Roadhog.Core.Model;

namespace Roadhog.Application.EquipmentUpgrade;

public sealed record EquipmentUpgradeResult(int Attempts, int Completed, string Message);

public sealed class EquipmentUpgradeSequence(IKeyboardInput input, IRoadhogLogger logger,
    Func<int, CancellationToken, Task>? delay = null)
{
    public const int CastWaitMilliseconds = 6000;
    public const int MaximumEnchantLevel = 10;

    public static bool Finished(EquipmentUpgradeItem item, EquipmentUpgradeKind kind) =>
        kind == EquipmentUpgradeKind.Enchant ? item.EnchantLevel >= MaximumEnchantLevel : item.UsedSockets >= item.SocketCount;

    public async Task<OperationResult<EquipmentUpgradeResult>> RunAsync(IRoadhogSnapshotReader snapshots,
        string account, EquipmentUpgradeSettings settings, EquipmentUpgradeKind kind,
        IProgress<string>? progress, CancellationToken token, int? maximumAttempts = null)
    {
        var plan = settings.Clone();
        var targets = (kind == EquipmentUpgradeKind.Enchant ? plan.EnchantTargets : plan.ManastoneTargets).DistinctBy(x => x.InstanceId).ToArray();
        var attempts = 0; var completed = 0; var shortages = new List<string>(); var stage = "准备"; var started = false;
        var mover = new FeedbackMouseMover(input, snapshots, delay);
        try
        {
            ValidateTargets(targets, kind);
            var character = (await snapshots.ReadPlayerAsync().WaitAsync(token)).Value;
            Require(!character.IsDead, "角色已死亡。");
            var ui = await Ui(); RequireIdle(ui);
            started = true;
            if (input is IInputStateReset reset) Check(await reset.ReleaseAllAsync(token));
            if (!ui.BagOpen)
            {
                Check(await input.PressKeyAsync("I", TimeSpan.FromMilliseconds(60), token));
                await Wait(u => u.BagOpen);
            }
            foreach (var target in targets)
            {
                while (true)
                {
                    token.ThrowIfCancellationRequested();
                    var player = (await snapshots.ReadPlayerAsync().WaitAsync(token)).Value;
                    Require(!player.IsDead && player.CharacterName == character.CharacterName, "角色状态已变化。");
                    var inventory = (await snapshots.ReadEquipmentUpgradeInventoryAsync().WaitAsync(token)).Value;
                    var equipment = inventory.Items.SingleOrDefault(i => i.InstanceId == target.InstanceId && i.TemplateId == target.TemplateId);
                    Require(equipment != null && !equipment.IsEquipped && equipment.Slot >= 0, $"目标装备已离开背包：{target.Name}");
                    if (Finished(equipment!, kind)) { completed++; break; }
                    Require(kind == EquipmentUpgradeKind.Enchant ? equipment!.CanEnchant : equipment!.CanSocket, "当前装备不支持此操作。");
                    var material = inventory.Items.Where(i => !i.IsEquipped && i.Slot >= 0 && i.Count > 0)
                        .Where(i => kind == EquipmentUpgradeKind.Enchant ? i.IsEnchantStone && target.EnchantStoneLevels.Contains(i.EnchantStoneLevel)
                            : i.IsManastone && i.TemplateId == target.ManastoneId)
                        .OrderBy(i => i.EnchantStoneLevel).ThenBy(i => i.Slot).FirstOrDefault();
                    if (material == null) { shortages.Add($"{target.Name} [#{target.InstanceId}]"); Report($"{target.Name} 指定材料已用完，跳过此件"); break; }
                    Report($"{target.Name}：+{equipment!.EnchantLevel}，魔石 {equipment.UsedSockets}/{equipment.SocketCount}；使用 {material.Name}");
                    RequireIdle(await Ui());
                    await ClickItem(material, RoadhogMouseButton.Right);
                    await Pause(200, token);
                    await ClickItem(equipment, RoadhogMouseButton.Left);
                    await Confirm(false, target.InstanceId, material.InstanceId);
                    await Confirm(true, target.InstanceId, material.InstanceId);
                    Report($"{target.Name}：等待读条 6 秒");
                    await Pause(CastWaitMilliseconds, token);
                    await Wait(u => u.Dialog == null && !u.Busy && !u.OtherModalOpen);
                    // Consumption proves this attempt settled, including failures. Never infer +1 or a filled socket from clicks.
                    using var settled = CancellationTokenSource.CreateLinkedTokenSource(token);
                    settled.CancelAfter(TimeSpan.FromSeconds(4));
                    while (true)
                    {
                        var after = (await snapshots.ReadEquipmentUpgradeInventoryAsync().WaitAsync(settled.Token)).Value;
                        var remaining = after.Items.SingleOrDefault(i => i.InstanceId == material.InstanceId);
                        if (remaining == null || remaining.TemplateId == material.TemplateId && remaining.Count < material.Count) break;
                        await Pause(100, settled.Token);
                    }
                    attempts++;
                    logger.Info("equipment_upgrade.attempt_settled", new Dictionary<string, object?>
                    { ["account"] = account, ["kind"] = kind.ToString(), ["equipmentId"] = target.InstanceId,
                        ["materialId"] = material.InstanceId, ["materialTemplateId"] = material.TemplateId, ["attempts"] = attempts });
                    if (maximumAttempts.HasValue && attempts >= maximumAttempts.Value) return Success("本次操作已结算，重新排序");
                }
            }
            return Success(shortages.Count == 0 ? "全部选中装备已完成" : $"处理结束；缺少指定材料：{string.Join("、", shortages)}");
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        { return OperationResult<EquipmentUpgradeResult>.Fail($"已停止；已执行 {attempts} 次，完成 {completed} 件。"); }
        catch (Exception ex)
        {
            logger.Warn("equipment_upgrade.stopped", new Dictionary<string, object?>
            { ["account"] = account, ["stage"] = stage, ["attempts"] = attempts, ["error"] = ex.Message });
            return OperationResult<EquipmentUpgradeResult>.Fail($"{stage}：{(ex is OperationCanceledException ? "等待操作结果超时" : ex.Message)}；已执行 {attempts} 次。");
        }
        finally
        {
            if (started)
            {
                if (input is IInputStateReset reset) await reset.ReleaseAllAsync(CancellationToken.None);
                else
                {
                    await input.MouseUpAsync(RoadhogMouseButton.Left, CancellationToken.None);
                    await input.MouseUpAsync(RoadhogMouseButton.Right, CancellationToken.None);
                }
            }
        }

        OperationResult<EquipmentUpgradeResult> Success(string message)
        { Report($"{message}；已执行 {attempts} 次，完成 {completed} 件。"); return OperationResult<EquipmentUpgradeResult>.Ok(new(attempts, completed, stage)); }
        void Report(string text) { stage = text; progress?.Report(text); }
        async Task<EquipmentUpgradeUi> Ui() => (await snapshots.ReadEquipmentUpgradeUiAsync().WaitAsync(token)).Value;
        async Task<EquipmentUpgradeUi> Wait(Func<EquipmentUpgradeUi, bool> condition)
        {
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(token);
            limit.CancelAfter(TimeSpan.FromSeconds(5));
            while (true)
            {
                var value = (await snapshots.ReadEquipmentUpgradeUiAsync().WaitAsync(limit.Token)).Value;
                Require(!value.OtherModalOpen, "其他窗口挡住操作。");
                if (condition(value)) return value;
                await Pause(100, limit.Token);
            }
        }
        async Task ClickItem(EquipmentUpgradeItem item, RoadhogMouseButton button)
        {
            GameUiPoint Locate(EquipmentUpgradeUi u)
            {
                RequireIdle(u);
                return u.Items.SingleOrDefault(i => i.InstanceId == item.InstanceId && i.TemplateId == item.TemplateId && i.Quantity == item.Count)?.Point
                    ?? throw new InvalidOperationException("目标物品不在可见背包内或数量已变化。");
            }
            var point = Locate(await Ui());
            await mover.MoveAsync(point, token); await Pause(180, token);
            var value = await Ui();
            Require(Locate(value) == point && value.HoveredInstanceId == item.InstanceId, "鼠标悬停物品与目标不符。");
            await Click(point, button);
        }
        async Task Confirm(bool final, uint equipment, uint material)
        {
            var value = await Wait(u => u.Dialog is { } d && d.FinalConfirmation == final);
            var dialog = value.Dialog!;
            Require(dialog.Kind == kind && dialog.EquipmentId == equipment && dialog.MaterialId == material, "确认窗口的装备或材料不符。");
            var point = dialog.ConfirmButton ?? throw new InvalidOperationException("确认按钮不可点击。");
            await mover.MoveAsync(point, token);
            value = await Ui();
            Require(value.Dialog == dialog && !value.OtherModalOpen, "确认窗口已变化。");
            await Click(point, RoadhogMouseButton.Left);
        }
        async Task Click(GameUiPoint point, RoadhogMouseButton button)
        {
            var cursor = (await snapshots.ReadUiCursorAsync().WaitAsync(token)).Value.Position;
            Require(Math.Abs(cursor.X - point.X) <= 1 && Math.Abs(cursor.Y - point.Y) <= 1, "鼠标位置已变化。");
            try { Check(await input.MouseDownAsync(button, token)); await Pause(50, token); }
            finally { Check(await input.MouseUpAsync(button, CancellationToken.None)); }
        }
    }
    internal static void ValidateTargets(IReadOnlyList<EquipmentUpgradeTarget> targets, EquipmentUpgradeKind kind)
    {
        Require(targets.Count > 0, "请勾选目标装备。");
        foreach (var target in targets)
            Require(kind == EquipmentUpgradeKind.Enchant ? target.EnchantStoneLevels.Count > 0 && target.EnchantStoneLevels.All(l => l is > 0 and <= 255)
                : target.ManastoneId is >= 167000000 and <= 167099999, $"请为 {target.Name} 选择{(kind == EquipmentUpgradeKind.Enchant ? "强化石等级" : "魔石种类")}。");
    }
    private Task Pause(int ms, CancellationToken token) => delay?.Invoke(ms, token) ?? Task.Delay(ms, token);
    private static void RequireIdle(EquipmentUpgradeUi ui) => Require(!ui.Busy && ui.Dialog == null && !ui.OtherModalOpen, "请先关闭已有强化或交易窗口。");
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Check(OperationResult result) { if (!result.Success) throw new InvalidOperationException(result.Error); }
}
