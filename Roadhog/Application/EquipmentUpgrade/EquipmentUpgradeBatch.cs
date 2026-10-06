using Roadhog.Core.Accounts;
using Roadhog.Core.Api;
using Roadhog.Core.Common;
using Roadhog.Core.Diagnostics;
using Roadhog.Core.Input;
using Roadhog.Core.Model;

namespace Roadhog.Application.EquipmentUpgrade;

/// <summary>One manual-input session covers the ordered tasks and the completion signal until Stop.</summary>
public sealed class EquipmentUpgradeBatch(IKeyboardInput input, IRoadhogLogger logger,
    Func<int, CancellationToken, Task>? delay = null)
{
    public const int SpaceIntervalMilliseconds = 1500;

    public async Task<OperationResult<EquipmentUpgradeResult>> RunAsync(IRoadhogSnapshotReader snapshots,
        string account, EquipmentUpgradeSettings settings, IProgress<string>? progress, CancellationToken token)
    {
        var plan = settings.Clone();
        var attempts = 0; var completed = 0; var jumping = false;
        var incomplete = new List<string>();
        try
        {
            var tasks = EquipmentUpgradePlan.Build(plan);
            if (tasks.Count == 0) throw new InvalidOperationException("请先勾选装备并配置材料。");
            // Validate the entire saved plan before consuming any material in the first task.
            foreach (var task in tasks) EquipmentUpgradeSequence.ValidateTargets(new[] { task.Target }, task.Kind);
            var character = (await snapshots.ReadPlayerAsync().WaitAsync(token)).Value.CharacterName;
            var pending = tasks.ToList();
            while (pending.Count > 0)
            {
                token.ThrowIfCancellationRequested();
                var inventory = (await snapshots.ReadEquipmentUpgradeInventoryAsync().WaitAsync(token)).Value;
                var ordered = EquipmentUpgradePlan.Order(pending, inventory);
                foreach (var done in ordered.Where(t => EquipmentUpgradeSequence.Finished(t.Item, t.Task.Kind)))
                { pending.Remove(done.Task); completed++; }
                var next = ordered.FirstOrDefault(t => pending.Contains(t.Task));
                if (next.Task == null) break;
                var task = next.Task; var kind = task.Kind;
                var single = new EquipmentUpgradeSettings();
                (kind == EquipmentUpgradeKind.Enchant ? single.EnchantTargets : single.ManastoneTargets).Add(task.Target);
                progress?.Report($"优先处理 {next.Item.EquipmentLevel} 级 +{next.Item.EnchantLevel}：{task.Target.Name}，{(kind == EquipmentUpgradeKind.Enchant ? "强化" : "镶嵌魔石")}");
                logger.Info("equipment_upgrade.task_started", new Dictionary<string, object?>
                    { ["account"] = account, ["kind"] = kind.ToString(), ["equipmentId"] = task.Target.InstanceId,
                        ["equipmentLevel"] = next.Item.EquipmentLevel, ["enchantLevel"] = next.Item.EnchantLevel });
                var result = await new EquipmentUpgradeSequence(input, logger, delay)
                    .RunAsync(snapshots, account, single, kind, progress, token, maximumAttempts: 1);
                if (!result.Success) return result;
                attempts += result.Value!.Attempts;
                if (result.Value.Attempts == 0)
                {
                    pending.Remove(task); completed += result.Value.Completed;
                    if (result.Value.Completed == 0) incomplete.Add(result.Value.Message);
                }
            }
            if (incomplete.Count > 0)
                return OperationResult<EquipmentUpgradeResult>.Ok(new(attempts, completed,
                    $"本轮处理结束，仍有装备缺料，未启动空格循环：{string.Join("；", incomplete)}"));

            progress?.Report($"装备任务全部完成（{attempts} 次操作）；每 1.5 秒按空格，点击停止结束。");
            logger.Info("equipment_upgrade.space_loop_started", new Dictionary<string, object?> { ["account"] = account, ["attempts"] = attempts });
            jumping = true;
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(SpaceIntervalMilliseconds));
            while (true)
            {
                if (delay == null) await timer.WaitForNextTickAsync(token);
                else await delay(SpaceIntervalMilliseconds, token);
                token.ThrowIfCancellationRequested();
                var player = (await snapshots.ReadPlayerAsync().WaitAsync(token)).Value;
                if (player.IsDead || player.CharacterName != character) throw new InvalidOperationException("角色状态已变化，空格循环停止。");
                var pressed = await input.PressKeyAsync("Space", TimeSpan.FromMilliseconds(60), token);
                if (!pressed.Success) throw new InvalidOperationException(pressed.Error ?? "空格输入失败。");
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        { return OperationResult<EquipmentUpgradeResult>.Ok(new(attempts, completed, $"已停止；已执行 {attempts} 次，完成 {completed} 项装备任务。")); }
        catch (Exception ex)
        { return OperationResult<EquipmentUpgradeResult>.Fail(ex.Message); }
        finally
        {
            if (jumping)
            {
                await input.KeyUpAsync("Space", CancellationToken.None);
                if (input is IInputStateReset reset) await reset.ReleaseAllAsync(CancellationToken.None);
                logger.Info("equipment_upgrade.space_loop_stopped", new Dictionary<string, object?> { ["account"] = account });
            }
        }
    }
}
