using Roadhog.Application.SemiAuto;
using Roadhog.Core.Accounts;

namespace Roadhog;

public sealed partial class AccountSettingsForm
{
    private const string SkillModeApplyHint = "保存配置后重启该账号脚本生效。";
    private const string QuickbarPollingHint = "每80ms轮询；技能仍可用且未进冷却时继续重试，维护优先执行。";

    private Panel? quickbarSkillPanel;
    private TreeView? quickbarAvailableSkillTree;
    private TreeView? quickbarSelectedSkillTree;
    private Label? quickbarSkillStatusLabel;
    private QuickbarSkillScriptSettings currentQuickbarSkillSettings = new();
    private SkillScriptSettings loadedLegacySkillSettings = new();

    private void CreateQuickbarSkillModeEditor(Panel page, Panel options)
    {
        var modeLabel = AddLabel(options, "技能栏可用", 12, 30, 148, 30, _textGreen, FontStyle.Bold);
        modeLabel.Name = "skillReleaseModeLabel";
        var modeTip = new ToolTip();
        modeTip.SetToolTip(modeLabel, QuickbarPollingHint + "\n" + SkillModeApplyHint);
        modeLabel.Disposed += (_, _) => modeTip.Dispose();

        var panel = CreateSkillModePanel(page, "quickbarSkillPanel", true);
        quickbarSkillPanel = panel;
        panel.SetBounds(12, 136, 828, 420);
        AddLabel(panel, "可选技能", 0, 2, 120, 24, _textGreen, FontStyle.Bold);
        AddLabel(panel, "技能执行顺序", 416, 2, 160, 24, _textGreen, FontStyle.Bold);
        var source = CreateSkillTree(panel, "quickbarAvailableSkillTree", 0, 38, 316, 292);
        var selected = CreateSkillTree(panel, "quickbarSelectedSkillTree", 416, 38, 316, 292);
        quickbarAvailableSkillTree = source;
        quickbarSelectedSkillTree = selected;
        AddSkillTreeBindingDisplay(selected);
        var refresh = AddButton(panel, "刷新技能栏", 182, 0, 134, 30);
        refresh.Name = "quickbarRefreshSkillsButton";
        refresh.Click += async (_, _) => await RefreshQuickbarSkillCandidatesAsync(refresh).ConfigureAwait(true);
        var refreshConfigured = AddButton(panel, "刷新全部已配置技能", 588, 0, 144, 30);
        refreshConfigured.Name = "quickbarRefreshConfiguredSkillsButton";
        configuredSkillRefreshButtons.Add(refreshConfigured);
        refreshConfigured.Click += async (_, _) =>
            await RefreshConfiguredSkillsAsync(refreshConfigured).ConfigureAwait(true);
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
        CreateOpeningSkillEditor(panel);
        ConfigureSkillPageLayout(page, options, panel);
    }

    private void ApplyQuickbarSkillSettings(ScriptSettings settings)
    {
        currentQuickbarSkillSettings = (settings.QuickbarSkills ?? new()).Clone();
        loadedLegacySkillSettings = settings.Skills.Clone();
        if (quickbarSelectedSkillTree is not null)
            PopulateSelectedSkillTreeFromConfig(quickbarSelectedSkillTree, currentQuickbarSkillSettings.ExecutionTree);
        ApplySkillTreeReleaseModeVisibility();
    }

    private static SkillTreeReleaseMode CaptureSkillTreeReleaseMode() => SkillTreeReleaseMode.QuickbarAvailability;

    private QuickbarSkillScriptSettings CaptureQuickbarSkillSettings() => quickbarSelectedSkillTree is null
        ? currentQuickbarSkillSettings.Clone()
        : new() { ExecutionTree = CaptureSkillTree(quickbarSelectedSkillTree.Nodes) };

    private List<SkillConfigNode> CaptureLegacyExecutionTree()
    {
        return loadedLegacySkillSettings.ExecutionTree.Select(node => node.Clone()).ToList();
    }

    private void ApplySkillTreeReleaseModeVisibility()
    {
        if (quickbarSkillPanel is null) return;
        quickbarSkillPanel.Visible = true;
        if (autoSkillPanel is not null) autoSkillPanel.Visible = false;
        if (manualSkillPanel is not null) manualSkillPanel.Visible = false;
        if (systemSkillPanel is not null) systemSkillPanel.Visible = false;
        if (conditionSkillPreemptsChainCheckBox is not null) conditionSkillPreemptsChainCheckBox.Enabled = false;
        if (chainWindowPerLinkTextBox is not null) chainWindowPerLinkTextBox.Enabled = false;
        if (attackWeaveCheckBox is not null) attackWeaveCheckBox.Enabled = true;
        if (attackWeaveDelayTextBox is not null) attackWeaveDelayTextBox.Enabled = attackWeaveCheckBox?.Checked == true;

        quickbarSkillPanel.BringToFront();
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
                    ? "部分栏位未取得技能详情；配置已保留，请重试，未用其他等级替代。"
                    : "已刷新实际栏位等级；80ms轮询，仍可用且未进冷却会重试；未自动添加或保存。";
        }
        catch (Exception exception)
        {
            previewSkillBindings = null;
            RefreshAutomaticSkillDisplays();
            if (quickbarSkillStatusLabel is { IsDisposed: false })
                quickbarSkillStatusLabel.Text = "刷新失败；配置已保留：" + exception.Message;
        }
        finally
        {
            if (!button.IsDisposed) { button.Text = originalText; button.Enabled = true; }
        }
    }
}
