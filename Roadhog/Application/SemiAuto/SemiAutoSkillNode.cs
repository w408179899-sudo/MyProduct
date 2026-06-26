using Roadhog.Core.Accounts;
using Roadhog.Core.Model;

namespace Roadhog.Application.SemiAuto;

public sealed class SemiAutoSkillNode
{
    public SemiAutoSkillNode(
        uint skillId,
        string name,
        string baseName,
        string type,
        int? chainTimeMs,
        string key,
        SemiAutoSkillNode? parent = null)
    {
        SkillId = skillId;
        Name = name ?? string.Empty;
        BaseName = baseName ?? string.Empty;
        Type = type ?? string.Empty;
        ChainTimeMs = chainTimeMs;
        Key = key ?? string.Empty;
        Parent = parent;
        NodeKey = SkillId + "|" + Name + "|" + Key;
    }

    public uint SkillId { get; }

    public string Name { get; }

    public string BaseName { get; }

    public string Type { get; }

    public int? ChainTimeMs { get; }

    public string Key { get; }

    public string NodeKey { get; }

    public SemiAutoSkillNode? Parent { get; }

    public List<SemiAutoSkillNode> Children { get; } = new();

    public bool IsTrigger => string.Equals(Type, "触发技能", StringComparison.Ordinal);

    public bool IsChainNode => Children.Count > 0 || Parent is not null || string.Equals(Type, "连续技", StringComparison.Ordinal);

    public static SemiAutoSkillNode FromConfigTree(SkillConfigNode config, string key, SemiAutoSkillNode? parent = null)
    {
        var node = new SemiAutoSkillNode(
            config.SkillId,
            config.Name,
            config.BaseName,
            config.Type,
            config.ChainTimeMs,
            key,
            parent);

        foreach (var childConfig in config.Children)
        {
            node.Children.Add(FromConfigTree(childConfig, key, node));
        }

        return node;
    }

    public SkillSnapshot? ResolveSkill(IReadOnlyList<SkillSnapshot> skills)
    {
        if (SkillId != 0)
        {
            var byId = skills.FirstOrDefault(skill => skill.SkillId == SkillId);
            if (byId is not null)
            {
                return byId;
            }
        }

        return skills.FirstOrDefault(MatchesName);
    }

    private bool MatchesName(SkillSnapshot skill)
    {
        return EqualsName(skill.Name, Name) ||
               EqualsName(skill.DisplayBaseName, Name) ||
               EqualsName(skill.Name, BaseName) ||
               EqualsName(skill.DisplayBaseName, BaseName);
    }

    private static bool EqualsName(string? left, string? right)
    {
        return !string.IsNullOrWhiteSpace(left) &&
               !string.IsNullOrWhiteSpace(right) &&
               string.Equals(left.Trim(), right.Trim(), StringComparison.Ordinal);
    }
}
