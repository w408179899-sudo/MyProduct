using Roadhog.Core.Model;

namespace Roadhog;

public sealed partial class AccountSettingsForm
{
    private static (int UpdatedCount, int DeletedCount) RefreshConfiguredQuickbarSkillTree(
        TreeView tree, IReadOnlyList<SkillSnapshot> barSkills)
    {
        var visibleSkills = barSkills.Where(skill => !ShouldHideManualSkillCandidate(skill)).ToArray();
        var candidates = BuildHighestSkillCandidates(visibleSkills);
        var updated = 0;
        var deleted = 0;

        void RefreshNode(TreeNode node)
        {
            var data = node.Tag as SkillTreeNodeData;
            var key = NormalizeSkillBaseName(!string.IsNullOrWhiteSpace(data?.BaseName)
                ? data.BaseName : data?.Name ?? node.Text);
            candidates.TryGetValue(key, out var skill);
            // Old DTOs may have only a valid ID. An absent display name must not
            // remove a skill that is actually present on a supported bar.
            if (skill is null && data?.SkillId is > 0)
            {
                skill = visibleSkills.FirstOrDefault(candidate => candidate.SkillId == data.SkillId);
                if (skill is not null && candidates.TryGetValue(
                        NormalizeSkillBaseName(GetSkillBaseName(skill)), out var highestBarRank))
                    skill = highestBarRank;
            }
            if (skill is not null)
            {
                var next = CreateSkillTreeNodeData(skill);
                if (data != next || !string.Equals(node.Text, next.Name, StringComparison.Ordinal))
                {
                    node.Text = node.Name = next.Name;
                    node.Tag = next;
                    updated++;
                }
            }
            else if (node.Parent is null)
            {
                deleted += CountSkillTreeNodes(node);
                node.Remove();
                return;
            }
            // A chain child can inherit its parent's slot. Without a slot of its own,
            // retain the configured identity and metadata rather than replacing it with
            // the learned highest rank or treating it as an unbound root.
            for (var i = node.Nodes.Count - 1; i >= 0; i--)
                RefreshNode(node.Nodes[i]);
        }

        tree.BeginUpdate();
        try
        {
            for (var i = tree.Nodes.Count - 1; i >= 0; i--)
                RefreshNode(tree.Nodes[i]);
            tree.ExpandAll();
        }
        finally { tree.EndUpdate(); }
        return (updated, deleted);
    }
}
