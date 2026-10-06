using Roadhog.Core.Accounts;
using Roadhog.Core.Model;

namespace Roadhog.Application.EquipmentUpgrade;

public sealed record EquipmentUpgradeTask(EquipmentUpgradeKind Kind, EquipmentUpgradeTarget Target);

public static class EquipmentUpgradePlan
{
    public static IReadOnlyList<EquipmentUpgradeTask> Build(EquipmentUpgradeSettings settings) =>
        settings.EnchantTargets.Select(t => new EquipmentUpgradeTask(EquipmentUpgradeKind.Enchant, t))
            .Concat(settings.ManastoneTargets.Select(t => new EquipmentUpgradeTask(EquipmentUpgradeKind.Manastone, t)))
            .DistinctBy(t => (t.Kind, t.Target.InstanceId, t.Target.TemplateId)).ToArray();

    public static IReadOnlyList<(EquipmentUpgradeTask Task, EquipmentUpgradeItem Item)> Order(
        IEnumerable<EquipmentUpgradeTask> tasks, EquipmentUpgradeInventory inventory)
    {
        var items = inventory.Items.ToDictionary(i => (i.InstanceId, i.TemplateId));
        return tasks.Select(task =>
        {
            if (!items.TryGetValue((task.Target.InstanceId, task.Target.TemplateId), out var item) || item.IsEquipped || item.Slot < 0)
                throw new InvalidOperationException($"目标装备已离开背包：{task.Target.Name}");
            if (item.EquipmentLevel is <= 0 or > 255) throw new InvalidOperationException($"装备等级不可用：{item.Name}");
            return (Task: task, Item: item);
        }).OrderBy(x => x.Item.EquipmentLevel).ThenBy(x => x.Item.EnchantLevel)
            .ThenBy(x => x.Task.Kind).ThenBy(x => x.Item.Slot).ThenBy(x => x.Item.InstanceId).ToArray();
    }
}
