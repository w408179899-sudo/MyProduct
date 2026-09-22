using Roadhog.Application.SemiAuto;

namespace Roadhog;

public sealed partial class AccountSettingsForm
{
    private SkillKeyBindings? previewSkillBindings;
    private readonly List<(Control Owner, Action Refresh)> skillBindingDisplays = new();
    private RoundedComboBox? spiritmasterOpeningSkillCombo;
    private RoundedComboBox? teamMentalCleanseSkillCombo, teamPhysicalCleanseSkillCombo, teamGroupCleanseSkillCombo;

    private string AutomaticKeyText(uint id, string name)
    {
        if (previewSkillBindings is null) return "启动时识别";
        var binding = previewSkillBindings.Resolve(id, name);
        return binding is null ? "未放入两栏" : "自动: " + FormatSkillKey(binding.Key);
    }

    private void BindAutomaticSkillButton(Button button, RoundedComboBox combo, Func<bool>? manualAction = null)
    {
        var refreshing = false;
        void Refresh()
        {
            if (refreshing || button.IsDisposed || combo.IsDisposed) return;
            refreshing = true;
            try
            {
                var id = combo.SelectedItem switch { MaintenanceSkillComboItem item => item.SkillId, OpeningSkillComboItem item => item.SkillId, _ => 0u };
                var name = combo.SelectedItem switch { MaintenanceSkillComboItem item => item.SkillName, OpeningSkillComboItem item => item.SkillName, _ => combo.Text.Trim() };
                var hasSkill = id != 0 || !string.IsNullOrWhiteSpace(name);
                var manual = manualAction?.Invoke() == true;
                button.Enabled = manual;
                button.Text = manual ? string.IsNullOrWhiteSpace(button.Tag as string) ? "选择按键" : FormatSkillKey(button.Tag as string)
                    : hasSkill ? AutomaticKeyText(id, name) : "请选择技能";
            }
            finally { refreshing = false; }
        }
        combo.SelectedIndexChanged += (_, _) => Refresh();
        combo.TextChanged += (_, _) => Refresh();
        button.TextChanged += (_, _) => Refresh();
        skillBindingDisplays.Add((button, Refresh));
        Refresh();
    }

    private void RefreshAutomaticSkillDisplays()
    {
        skillBindingDisplays.RemoveAll(entry => entry.Owner.IsDisposed);
        foreach (var entry in skillBindingDisplays) entry.Refresh();
        selectedSkillTree?.Invalidate();
        systemSelectedSkillTree?.Invalidate();
    }

    private async Task RefreshSkillBindingsPreviewAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        // Preview is optional; worker startup always captures its own official snapshot.
        try { previewSkillBindings = new(await _runtime.ReadQuickbarAsync(_account, timeout.Token).ConfigureAwait(true), currentManualSkills); }
        catch (OperationCanceledException) { previewSkillBindings = null; }
        RefreshAutomaticSkillDisplays();
    }

    private void AddSkillTreeBindingDisplay(TreeView tree)
    {
        tree.DrawMode = TreeViewDrawMode.OwnerDrawText;
        tree.ShowNodeToolTips = true;
        tree.DrawNode += (_, e) =>
        {
            if (e.Node?.Tag is not SkillTreeNodeData data) { e.DrawDefault = true; return; }
            var binding = ResolveTreeBinding(e.Node);
            var keyText = previewSkillBindings is null ? "启动时识别" : binding is null ? "未放入两栏" : "自动: " + FormatSkillKey(binding.Key);
            var text = e.Node.Text + "  [" + keyText + "]";
            var selected = (e.State & TreeNodeStates.Selected) != 0;
            var bounds = new Rectangle(e.Bounds.X, e.Bounds.Y, Math.Max(0, tree.ClientSize.Width - e.Bounds.X), e.Bounds.Height);
            using var background = new SolidBrush(selected ? SystemColors.Highlight : tree.BackColor);
            e.Graphics.FillRectangle(background, bounds);
            TextRenderer.DrawText(e.Graphics, text, tree.Font, bounds, selected ? SystemColors.HighlightText : tree.ForeColor,
                TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            e.Node.ToolTipText = text;
        };
    }

    private BoundSkill? ResolveTreeBinding(TreeNode node)
    {
        if (previewSkillBindings is null || node.Tag is not SkillTreeNodeData data) return null;
        var parent = node.Parent is null ? null : ResolveTreeBinding(node.Parent);
        var parentNode = parent is null ? null : new SemiAutoSkillNode(parent.SkillId, parent.Name, parent.Name, "", null, parent.Key);
        return previewSkillBindings.ResolveChain(data.SkillId, data.Name, parentNode);
    }
}
