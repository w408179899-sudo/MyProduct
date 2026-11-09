namespace Roadhog.Core.Accounts;

public enum AdvancedMonsterCategory { Elite, Ranged, Melee }

/// <summary>Opt-in, exact-name manual groups. Category labels never infer monster attributes.</summary>
public sealed class AdvancedSkillSettings
{
    public bool Enabled { get; set; }
    public AdvancedSkillGroup Elite { get; set; } = new();
    public AdvancedSkillGroup Ranged { get; set; } = new();
    public AdvancedSkillGroup Melee { get; set; } = new();

    public AdvancedSkillGroup Group(AdvancedMonsterCategory category) => category switch
    {
        AdvancedMonsterCategory.Elite => Elite,
        AdvancedMonsterCategory.Ranged => Ranged,
        AdvancedMonsterCategory.Melee => Melee,
        _ => throw new ArgumentOutOfRangeException(nameof(category))
    };

    public AdvancedSkillSettings Clone() => new()
    {
        Enabled = Enabled, Elite = (Elite ?? new()).Clone(),
        Ranged = (Ranged ?? new()).Clone(), Melee = (Melee ?? new()).Clone()
    };

    public bool TryAddMonster(AdvancedMonsterCategory category, string name, out AdvancedMonsterCategory? existing)
    {
        existing = null;
        if (string.IsNullOrWhiteSpace(name)) return false;
        name = name.Trim();
        foreach (var candidate in Enum.GetValues<AdvancedMonsterCategory>())
            if (Group(candidate)?.ContainsMonster(name) == true) { existing = candidate; return false; }
        var group = Group(category);
        (group.MonsterNames ??= new()).Add(name);
        return true;
    }

    public AdvancedMonsterCategory? Match(string name)
    {
        if (!Enabled || string.IsNullOrWhiteSpace(name)) return null;
        var matches = Enum.GetValues<AdvancedMonsterCategory>()
            .Where(category => Group(category)?.ContainsMonster(name) == true).ToArray();
        // Corrupt/imported duplicate groups fail closed to the original flow.
        if (matches.Length != 1) return null;
        var group = Group(matches[0]);
        return group.Enabled && group.Skills?.Any(node => node is not null &&
            (node.SkillId != 0 || !string.IsNullOrWhiteSpace(node.Name))) == true ? matches[0] : null;
    }

    public string? DuplicateMonster() => Enum.GetValues<AdvancedMonsterCategory>()
        .SelectMany(category => Group(category)?.MonsterNames ?? new())
        .Where(name => !string.IsNullOrWhiteSpace(name)).Select(name => name.Trim())
        .GroupBy(name => name, StringComparer.OrdinalIgnoreCase).FirstOrDefault(group => group.Count() > 1)?.Key;
}

public sealed class AdvancedSkillGroup
{
    public bool Enabled { get; set; } = true;
    public List<string> MonsterNames { get; set; } = new();
    public List<SkillConfigNode> Skills { get; set; } = new();
    public bool ContainsMonster(string name) => MonsterNames?.Any(value =>
        string.Equals(value?.Trim(), name.Trim(), StringComparison.OrdinalIgnoreCase)) == true;
    public AdvancedSkillGroup Clone() => new()
    {
        Enabled = Enabled,
        MonsterNames = MonsterNames?.Where(name => !string.IsNullOrWhiteSpace(name)).Select(name => name.Trim()).ToList() ?? new(),
        Skills = Skills?.Where(node => node is not null).Select(node => node.Clone()).ToList() ?? new()
    };
}
