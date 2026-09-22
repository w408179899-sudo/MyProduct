using Roadhog.Core.Accounts;
using Roadhog.Core.Model;

namespace Roadhog.Application.SemiAuto;

public sealed record BoundSkill(uint SkillId, string Name, string Key);

/// <summary>An immutable execution plan built from the official startup snapshot, not a read-quality cache.</summary>
public sealed class SkillKeyBindings
{
    private static readonly string[] MainKeys = { "D1", "D2", "D3", "D4", "D5", "D6", "D7", "D8", "D9", "D0", "OemMinus", "OemPlus" };
    // The user's second bindings end with Num+ then Num- (the keyboard picker order).
    private static readonly string[] AltKeys = { "NumPad1", "NumPad2", "NumPad3", "NumPad4", "NumPad5", "NumPad6", "NumPad7", "NumPad8", "NumPad9", "NumPad0", "NumPadAdd", "NumPadSubtract" };
    private readonly IReadOnlyList<SkillSnapshot> _skills;
    private readonly Dictionary<uint, string> _keys;
    public int Page { get; }

    public SkillKeyBindings(QuickbarSnapshot quickbar, IReadOnlyList<SkillSnapshot> skills)
    {
        Page = quickbar.Page;
        _skills = skills.ToArray();
        _keys = new();
        // Prefer main bar, then the leftmost occurrence. Never bind items or Ctrl/hidden pages.
        foreach (var slot in quickbar.Slots.OrderBy(s => s.Bar).ThenBy(s => s.Slot))
            if (slot.ContentType == 21 && slot.SkillId != 0 && slot.Slot is >= 0 and < 12 && slot.Bar is SkillQuickbar.Main or SkillQuickbar.Alt)
                _keys.TryAdd(slot.SkillId, (slot.Bar == SkillQuickbar.Main ? MainKeys : AltKeys)[slot.Slot]);
    }

    public BoundSkill? Resolve(uint id, string? name)
    {
        if (id != 0 && _keys.TryGetValue(id, out var exact))
            return new(id, _skills.FirstOrDefault(s => s.SkillId == id)?.Name ?? name ?? "", exact);
        // An explicit skill must not silently switch to another rank on the bar.
        if (id != 0) return null;
        var candidates = _skills.Where(s => Matches(s.Name, name) || Matches(s.DisplayBaseName, name))
            .Where(s => _keys.ContainsKey(s.SkillId)).GroupBy(s => s.SkillId).Select(g => g.First()).ToArray();
        return candidates.Length == 1 ? new(candidates[0].SkillId, candidates[0].Name, _keys[candidates[0].SkillId]) : null;
    }

    public BoundSkill? ResolveChain(uint id, string? name, SemiAutoSkillNode? parent)
    {
        var direct = Resolve(id, name);
        if (direct is not null || parent is null) return direct;
        var child = _skills.FirstOrDefault(s => id != 0 ? s.SkillId == id : Matches(s.Name, name));
        var source = _skills.FirstOrDefault(s => s.SkillId == parent.SkillId);
        if (child is null || source is null || string.IsNullOrWhiteSpace(child.XmlPrechainCategory) || string.IsNullOrWhiteSpace(source.XmlChainCategory)) return null;
        var requirements = child.XmlPrechainCategory.Split(new[] { ',', ';', '|', ' ' }, StringSplitOptions.RemoveEmptyEntries);
        return requirements.Contains(source.XmlChainCategory, StringComparer.OrdinalIgnoreCase)
            ? new(child.SkillId, child.Name, parent.Key) : null;
    }

    private static bool Matches(string? left, string? right) => !string.IsNullOrWhiteSpace(right) &&
        string.Equals(left?.Trim(), right.Trim(), StringComparison.Ordinal);

    public ScriptSettings ApplyTo(ScriptSettings original, Action<string>? missing = null)
    {
        var settings = original.Clone();
        (uint Id, string Name, string Key) Bind(uint id, string name, string old, bool allowManual = false)
        {
            if (id == 0 && string.IsNullOrWhiteSpace(name))
            {
                if (allowManual) return (id, name, old);
                if (!string.IsNullOrWhiteSpace(old)) missing?.Invoke("未选择技能（旧按键 " + old + "）");
                return (id, name, "");
            }
            var binding = Resolve(id, name);
            if (binding is null) missing?.Invoke((string.IsNullOrWhiteSpace(name) ? id.ToString() : name) + "：未放入主栏或 Alt 栏");
            return binding is null ? (id, name, "") : (binding.SkillId, binding.Name, binding.Key);
        }
        var maintenance = settings.Maintenance;
        foreach (var rule in maintenance.HpMaintenanceRules.Concat(maintenance.MpMaintenanceRules))
            if (rule.ActionType == MaintenanceRuleActionType.Skill) (rule.SkillId, rule.SkillName, rule.Key) = Bind(rule.SkillId, rule.SkillName, rule.Key);
        foreach (var rule in maintenance.StatusMaintenanceRules) (rule.SkillId, rule.SkillName, rule.Key) = Bind(rule.SkillId, rule.SkillName, rule.Key);
        foreach (var rule in maintenance.DpMaintenanceRules) (rule.SkillId, rule.SkillName, rule.Key) = Bind(rule.SkillId, rule.SkillName, rule.Key);
        var spirit = settings.Skills.Spiritmaster;
        foreach (var rule in spirit.SummonSkills) (rule.SkillId, rule.SkillName, rule.Key) = Bind(rule.SkillId, rule.SkillName, rule.Key, allowManual: true);
        foreach (var rule in spirit.PetHpMaintenanceRules) (rule.SkillId, rule.SkillName, rule.Key) = Bind(rule.SkillId, rule.SkillName, rule.Key);
        foreach (var rule in spirit.PetBuffRules) (rule.SkillId, rule.SkillName, rule.Key) = Bind(rule.SkillId, rule.SkillName, rule.Key);
        (spirit.OpeningAttackSkillId, spirit.OpeningAttackSkillName, spirit.OpeningAttackKey) = Bind(spirit.OpeningAttackSkillId, spirit.OpeningAttackSkillName, spirit.OpeningAttackKey, allowManual: true);
        foreach (var rule in settings.Team.Support.HealSkillRules) (rule.SkillId, rule.SkillName, rule.Key) = Bind(rule.SkillId, rule.SkillName, rule.Key);
        var support = settings.Team.Support;
        (support.MentalCleanseSkillId, support.MentalCleanseSkillName, support.MentalCleanseKey) = Bind(support.MentalCleanseSkillId, support.MentalCleanseSkillName, support.MentalCleanseKey, allowManual: true);
        (support.PhysicalCleanseSkillId, support.PhysicalCleanseSkillName, support.PhysicalCleanseKey) = Bind(support.PhysicalCleanseSkillId, support.PhysicalCleanseSkillName, support.PhysicalCleanseKey, allowManual: true);
        (support.GroupCleanseSkillId, support.GroupCleanseSkillName, support.GroupCleanseKey) = Bind(support.GroupCleanseSkillId, support.GroupCleanseSkillName, support.GroupCleanseKey, allowManual: true);
        return settings;
    }
}
