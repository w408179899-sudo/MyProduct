using Roadhog.Application.SemiAuto;
using Roadhog.Core.Accounts;

namespace Roadhog;

public sealed partial class AccountSettingsForm
{
    private const string SkillModeApplyHint = "保存配置后重启该账号脚本生效。";
    private const string QuickbarPollingHint = "新版每80ms轮询；技能仍可用且未进冷却时继续重试，维护优先执行。";

    private RoundedComboBox? skillTreeReleaseModeCombo;
    private Panel? quickbarSkillPanel;
    private TreeView? quickbarAvailableSkillTree;
    private TreeView? quickbarSelectedSkillTree;
    private Label? quickbarSkillStatusLabel;
    private Action? layoutQuickbarSkillPage;
    private QuickbarSkillScriptSettings currentQuickbarSkillSettings = new();
    private List<SkillConfigNode> loadedLegacyExecutionTree = new();
    private List<SkillConfigNode> loadedLegacyTreeView = new();

    private void CreateQuickbarSkillModeEditor(Panel page, Panel options)
    {
        skillTreeReleaseModeCombo = AddCombo(options, 12, 30, 148, 30);
        skillTreeReleaseModeCombo.Name = "skillTreeReleaseModeCombo";
        skillTreeReleaseModeCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        skillTreeReleaseModeCombo.Items.AddRange(new object[] { "旧版释放", "技能栏可用" });
        skillTreeReleaseModeCombo.SelectedIndex = 0;
        var modeTip = new ToolTip();
        modeTip.SetToolTip(skillTreeReleaseModeCombo, QuickbarPollingHint + "\n" + SkillModeApplyHint);
        foreach (Control child in skillTreeReleaseModeCombo.Controls)
            modeTip.SetToolTip(child, QuickbarPollingHint + "\n" + SkillModeApplyHint);
        skillTreeReleaseModeCombo.Disposed += (_, _) => modeTip.Dispose();

        var panel = CreateSkillModePanel(page, "quickbarSkillPanel", false);
        quickbarSkillPanel = panel;
        panel.SetBounds(12, 136, 828, 420);
        AddLabel(panel, "可选技能", 0, 2, 120, 24, _textGreen, FontStyle.Bold);
        var priorityLabel = AddLabel(panel, "新版技能执行顺序", 416, 2, 160, 24, _textGreen, FontStyle.Bold);
        var source = CreateSkillTree(panel, "quickbarAvailableSkillTree", 0, 38, 316, 292);
        var selected = CreateSkillTree(panel, "quickbarSelectedSkillTree", 416, 38, 316, 292);
        quickbarAvailableSkillTree = source;
        quickbarSelectedSkillTree = selected;
        AddSkillTreeBindingDisplay(selected);
        var refresh = AddButton(panel, "刷新技能栏", 182, 0, 134, 30);
        refresh.Name = "quickbarRefreshSkillsButton";
        refresh.Click += async (_, _) => await RefreshQuickbarSkillCandidatesAsync(refresh).ConfigureAwait(true);
        var copy = AddButton(panel, "从旧技能树复制", 588, 0, 144, 30, (_, _) => CopyLegacySkillTreeToQuickbar());
        copy.Name = "quickbarCopyLegacyTreeButton";
        var add = AddButton(panel, "添加 >", 328, 169, 76, 30, (_, _) => AddSkillSelection(source, selected));
        add.Name = "quickbarAddSkillButton";
        var moves = new[]
        {
            ("置顶", SkillMove.Top), ("上移", SkillMove.Up),
            ("下移", SkillMove.Down), ("置底", SkillMove.Bottom)
        };
        foreach (var (text, move) in moves)
            AddButton(panel, text, 744, 69, 84, 30, (_, _) => MoveSelectedSkill(selected, move));
        AddButton(panel, "移除", 744, 229, 84, 30, (_, _) => RemoveSelectedSkill(selected));
        AddButton(panel, "清空", 744, 269, 84, 30, (_, _) => selected.Nodes.Clear());
        quickbarSkillStatusLabel = AddLabel(panel,
            "80ms轮询，仍可用且未进冷却会重试；普通看冷却，连续／触发看开放状态，维护优先。", 0, 332, 828, 16);
        quickbarSkillStatusLabel.Font = new Font(quickbarSkillStatusLabel.Font.FontFamily, 8F);

        var resizing = false;
        void ResizeQuickbarEditor()
        {
            if (resizing || autoSkillPanel is null || availableSkillTree is null || selectedSkillTree is null) return;
            resizing = true;
            try
            {
                panel.SetBounds(autoSkillPanel.Left, autoSkillPanel.Top, autoSkillPanel.Width, autoSkillPanel.Height);
                source.Bounds = availableSkillTree.Bounds;
                selected.Bounds = selectedSkillTree.Bounds;
                priorityLabel.Left = selected.Left;
                refresh.Left = source.Right - refresh.Width;
                copy.Left = selected.Right - copy.Width;
                add.Location = new Point(source.Right + 12, source.Top + (source.Height - add.Height) / 2);
                var order = new[] { "置顶", "上移", "下移", "置底", "移除", "清空" };
                for (var i = 0; i < order.Length; i++)
                    if (panel.Controls.OfType<Button>().SingleOrDefault(button => button.Text == order[i]) is { } button)
                        button.Location = new Point(panel.Width - button.Width, source.Top + (source.Height - 230) / 2 + i * 40);
                quickbarSkillStatusLabel?.SetBounds(0, source.Bottom + 1, panel.Width, 15);
            }
            finally { resizing = false; }
        }

        if (autoSkillPanel is not null) autoSkillPanel.SizeChanged += (_, _) => ResizeQuickbarEditor();
        page.ClientSizeChanged += (_, _) => ResizeQuickbarEditor();
        layoutQuickbarSkillPage = ResizeQuickbarEditor;
        skillTreeReleaseModeCombo.SelectedIndexChanged += (_, _) =>
        {
            ApplySkillTreeReleaseModeVisibility();
            if (quickbarSkillStatusLabel is not null)
                quickbarSkillStatusLabel.Text = "新版80ms轮询，仍可用且未进冷却会重试；" + SkillModeApplyHint;
            layoutSkillPage?.Invoke();
            ResizeQuickbarEditor();
        };
        ResizeQuickbarEditor();
    }

    private void ApplyQuickbarSkillSettings(ScriptSettings settings)
    {
        currentQuickbarSkillSettings = (settings.QuickbarSkills ?? new()).Clone();
        loadedLegacyExecutionTree = settings.Skills.ExecutionTree.Select(node => node.Clone()).ToList();
        loadedLegacyTreeView = selectedSkillTree is null ? new() : CaptureSkillTree(selectedSkillTree.Nodes);
        if (quickbarSelectedSkillTree is not null)
            PopulateSelectedSkillTreeFromConfig(quickbarSelectedSkillTree, currentQuickbarSkillSettings.ExecutionTree);
        if (skillTreeReleaseModeCombo is not null)
            skillTreeReleaseModeCombo.SelectedIndex = settings.SkillTreeReleaseMode == SkillTreeReleaseMode.QuickbarAvailability ? 1 : 0;
        ApplySkillTreeReleaseModeVisibility();
    }

    private SkillTreeReleaseMode CaptureSkillTreeReleaseMode() => skillTreeReleaseModeCombo?.SelectedIndex == 1
        ? SkillTreeReleaseMode.QuickbarAvailability : SkillTreeReleaseMode.Legacy;

    private QuickbarSkillScriptSettings CaptureQuickbarSkillSettings() => quickbarSelectedSkillTree is null
        ? currentQuickbarSkillSettings.Clone()
        : new() { ExecutionTree = CaptureSkillTree(quickbarSelectedSkillTree.Nodes) };

    private List<SkillConfigNode> CaptureLegacyExecutionTree()
    {
        var captured = selectedSkillTree is null ? new List<SkillConfigNode>() : CaptureSkillTree(selectedSkillTree.Nodes);
        if (CaptureSkillTreeReleaseMode() == SkillTreeReleaseMode.QuickbarAvailability && SkillTreesEqual(captured, loadedLegacyTreeView))
            return loadedLegacyExecutionTree.Select(node => node.Clone()).ToList();
        return captured;
    }

    private static bool SkillTreesEqual(IReadOnlyList<SkillConfigNode> left, IReadOnlyList<SkillConfigNode> right) =>
        left.Count == right.Count && left.Zip(right).All(pair =>
            pair.First.SkillId == pair.Second.SkillId && pair.First.Name == pair.Second.Name &&
            pair.First.BaseName == pair.Second.BaseName && pair.First.Type == pair.Second.Type &&
            pair.First.ChainTimeMs == pair.Second.ChainTimeMs && SkillTreesEqual(pair.First.Children, pair.Second.Children));

    private void ApplySkillTreeReleaseModeVisibility()
    {
        if (quickbarSkillPanel is null) return;
        var useQuickbar = CaptureSkillTreeReleaseMode() == SkillTreeReleaseMode.QuickbarAvailability;
        quickbarSkillPanel.Visible = useQuickbar;
        if (autoSkillPanel is not null) autoSkillPanel.Visible = !useQuickbar;
        if (manualSkillPanel is not null) manualSkillPanel.Visible = false;
        if (systemSkillPanel is not null) systemSkillPanel.Visible = false;
        if (conditionSkillPreemptsChainCheckBox is not null) conditionSkillPreemptsChainCheckBox.Enabled = !useQuickbar;
        if (chainWindowPerLinkTextBox is not null) chainWindowPerLinkTextBox.Enabled = !useQuickbar;
        if (attackWeaveCheckBox is not null) attackWeaveCheckBox.Enabled = true;
        if (attackWeaveDelayTextBox is not null) attackWeaveDelayTextBox.Enabled = attackWeaveCheckBox?.Checked == true;

        // The opening editor is shared; switching engines retains the same controls and unsaved values.
        if (openingSkillRows?.Parent is Panel opening && (useQuickbar ? quickbarSkillPanel : autoSkillPanel) is { } owner && opening.Parent != owner)
            owner.Controls.Add(opening);
        if (useQuickbar) quickbarSkillPanel.BringToFront();
    }

    private void CopyLegacySkillTreeToQuickbar()
    {
        if (quickbarSelectedSkillTree is null || selectedSkillTree is null) return;
        PopulateSelectedSkillTreeFromConfig(quickbarSelectedSkillTree, CaptureSkillTree(selectedSkillTree.Nodes));
        if (quickbarSkillStatusLabel is not null) quickbarSkillStatusLabel.Text = "已复制旧技能树；两套配置独立，" + SkillModeApplyHint;
    }

    private async Task RefreshQuickbarSkillCandidatesAsync(Button button)
    {
        var originalText = button.Text;
        button.Enabled = false;
        button.Text = "刷新中...";
        try
        {
            currentManualSkills = await _runtime.RefreshSkillsAsync(_account).ConfigureAwait(true);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            var quickbar = await _runtime.ReadQuickbarAsync(_account, timeout.Token).ConfigureAwait(true);
            // Learned lists contain the highest ranks; the bar may deliberately bind a lower rank.
            // Read the actual IDs instead of relabeling a highest-rank snapshot as a different skill.
            var missingIds = quickbar.Slots.Where(slot => slot.ContentType == 21 && slot.SkillId != 0 &&
                    !currentManualSkills.Any(skill => skill.SkillId == slot.SkillId))
                .Select(slot => slot.SkillId).Distinct().ToArray();
            if (missingIds.Length > 0)
                currentManualSkills = currentManualSkills
                    .Concat(await _runtime.RefreshSkillsByIdsAsync(missingIds, _account, timeout.Token).ConfigureAwait(true))
                    .DistinctBy(skill => skill.SkillId).ToArray();
            previewSkillBindings = new SkillKeyBindings(quickbar, currentManualSkills);
            if (quickbarAvailableSkillTree is not null)
                PopulateAvailableSkillTreeFromSkills(quickbarAvailableSkillTree, currentManualSkills);
            RefreshSkillCandidateCombos();
            RefreshAutomaticSkillDisplays();
            if (quickbarSkillStatusLabel is not null)
                quickbarSkillStatusLabel.Text = missingIds.Any(id => !currentManualSkills.Any(skill => skill.SkillId == id))
                    ? "部分栏位未取得技能详情；两套配置均保留，请重试，未用其他等级替代。"
                    : "已刷新实际栏位等级；80ms轮询，仍可用且未进冷却会重试；未自动添加或保存。";
        }
        catch (Exception exception)
        {
            previewSkillBindings = null;
            RefreshAutomaticSkillDisplays();
            if (quickbarSkillStatusLabel is { IsDisposed: false })
                quickbarSkillStatusLabel.Text = "刷新失败；两套配置均保留：" + exception.Message;
        }
        finally
        {
            if (!button.IsDisposed) { button.Text = originalText; button.Enabled = true; }
        }
    }
}
