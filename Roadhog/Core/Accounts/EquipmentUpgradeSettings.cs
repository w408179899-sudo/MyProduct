using Roadhog.Core.Model;

namespace Roadhog.Core.Accounts;

public sealed class EquipmentUpgradeSettings
{
    public List<EquipmentUpgradeTarget> EnchantTargets { get; set; } = new();
    public List<EquipmentUpgradeTarget> ManastoneTargets { get; set; } = new();
    public EquipmentUpgradeSettings Clone() => new()
    {
        EnchantTargets = EnchantTargets?.Select(x => x.Copy()).ToList() ?? new(),
        ManastoneTargets = ManastoneTargets?.Select(x => x.Copy()).ToList() ?? new()
    };
}


public sealed record EquipmentUpgradeTarget(uint InstanceId, uint TemplateId, string Name)
{
    public List<int> EnchantStoneLevels { get; set; } = new();
    public uint ManastoneId { get; set; }
    public EquipmentUpgradeTarget Copy() => this with { EnchantStoneLevels = EnchantStoneLevels?.Distinct().Order().ToList() ?? new() };
}
