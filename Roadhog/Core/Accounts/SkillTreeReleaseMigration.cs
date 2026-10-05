namespace Roadhog.Core.Accounts;

/// <summary>Converts legacy attack configuration while retaining its original compatibility fields.</summary>
public static class SkillTreeReleaseMigration
{
    public static bool Migrate(ScriptSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var legacy = settings.SkillTreeReleaseMode == SkillTreeReleaseMode.Legacy;
        var changed = settings.SkillTreeReleaseMode != SkillTreeReleaseMode.QuickbarAvailability ||
            settings.QuickbarSkills is null || settings.QuickbarSkills.ExecutionTree is null;
        settings.QuickbarSkills ??= new();
        settings.QuickbarSkills.ExecutionTree ??= new();
        // An explicitly selected quickbar engine may intentionally have no attacks.
        // Existing new priorities always win over the archived legacy tree.
        if (legacy && settings.QuickbarSkills.ExecutionTree.Count == 0 && settings.Skills is { } old)
        {
            var roots = old.Mode switch
            {
                SkillConfigurationMode.SystemClassification => old.SystemExecutionTree,
                SkillConfigurationMode.ManualMapping => (old.ManualMappings ?? new())
                    .Where(mapping => mapping is not null && !string.IsNullOrWhiteSpace(mapping.SkillName) &&
                        !string.IsNullOrWhiteSpace(mapping.Key))
                    .Select(mapping => new SkillConfigNode
                    {
                        Name = mapping.SkillName, BaseName = mapping.SkillName, Type = mapping.SkillType
                    }).ToList(),
                _ => old.ExecutionTree
            };
            settings.QuickbarSkills.ExecutionTree = (roots ?? new())
                .Where(node => IsUsable(node) && !(node.Type ?? string.Empty).Contains("DP", StringComparison.OrdinalIgnoreCase))
                .Select(CloneNode).ToList();
        }
        settings.SkillTreeReleaseMode = SkillTreeReleaseMode.QuickbarAvailability;
        return changed;
    }

    private static bool IsUsable(SkillConfigNode? node) => node is not null &&
        (node.SkillId != 0 || !string.IsNullOrWhiteSpace(node.Name) || !string.IsNullOrWhiteSpace(node.BaseName));

    private static SkillConfigNode CloneNode(SkillConfigNode node) => new()
    {
        SkillId = node.SkillId,
        Name = node.SkillId == 0 && string.IsNullOrWhiteSpace(node.Name) ? node.BaseName : node.Name,
        BaseName = node.BaseName,
        Type = node.Type,
        ChainTimeMs = node.ChainTimeMs,
        Children = (node.Children ?? new()).Where(IsUsable).Select(CloneNode).ToList()
    };
}
