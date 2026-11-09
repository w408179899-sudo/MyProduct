using Roadhog.Core.Accounts;

namespace Roadhog;

public sealed partial class AccountSettingsForm
{
    private AdvancedSkillSettings currentAdvancedSkillSettings = new();

    private static string AdvancedCategoryName(AdvancedMonsterCategory category) => category switch
    {
        AdvancedMonsterCategory.Elite => "精英怪物",
        AdvancedMonsterCategory.Ranged => "远程怪物",
        AdvancedMonsterCategory.Melee => "近战怪物",
        _ => throw new ArgumentOutOfRangeException(nameof(category))
    };

    private Form CreateAdvancedSkillSettingsDialog()
    {
        var draft = currentAdvancedSkillSettings.Clone();
        var dialog = new Form
        {
            Name = "advancedSkillSettingsDialog", Text = "高级技能设置 - " + _account,
            AutoScaleDimensions = new SizeF(7F, 17F), AutoScaleMode = AutoScaleMode.Font,
            BackColor = _pageBackground, Font = new Font("Microsoft YaHei UI", 9F),
            ClientSize = new Size(1000, 700), MinimumSize = new Size(940, 650),
            ShowIcon = false, StartPosition = FormStartPosition.CenterParent
        };
        var header = new Panel { Name = "advancedHeader", Dock = DockStyle.Top, Size = new Size(1000, 112), BackColor = _pageBackground };
        AddLabel(header, "高级技能设置", 20, 12, 300, 28, _textGreen, FontStyle.Bold);
        var hint = AddLabel(header, "仅手动名单生效；匹配后替代起手技能，当前技能和连续技结束后再检查高级技能。", 20, 43, 950, 25);
        hint.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        var enabled = AddCheckBox(header, "启用高级技能", 816, 15, 170, draft.Enabled);
        enabled.Name = "advancedSkillsEnabled";
        enabled.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        var order = AddLabel(header, "高级技能优先循环  →  无可用高级技能时，普通列表从头检查", 20, 78, 954, 28, _textGreen, FontStyle.Bold);
        order.BackColor = _inputBackground; order.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        var footer = new Panel { Name = "advancedFooter", Dock = DockStyle.Bottom, Size = new Size(1000, 58), BackColor = Color.White };
        var status = AddLabel(footer, "保存后，下次启动该账号脚本生效。", 20, 15, 700, 28);
        status.Name = "advancedSkillStatus"; status.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top;
        var close = AddSpiritmasterSecondaryButton(footer, "取消", 742, 13, 100);
        close.Anchor = AnchorStyles.Top | AnchorStyles.Right; close.Click += (_, _) => dialog.Close();
        var save = AddButton(footer, "保存设置", 854, 13, 124, 32);
        save.Name = "advancedSkillSave"; save.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Padding = new Padding(10, 0, 10, 8),
            BackColor = _pageBackground, Name = "advancedSkillCards"
        };
        grid.ColumnStyles.Add(new(SizeType.Percent, 50)); grid.ColumnStyles.Add(new(SizeType.Percent, 50));
        grid.RowStyles.Add(new(SizeType.Percent, 50)); grid.RowStyles.Add(new(SizeType.Percent, 50));
        dialog.Controls.Add(grid); dialog.Controls.Add(footer); dialog.Controls.Add(header);
        var editors = new Dictionary<AdvancedMonsterCategory, FlowLayoutPanel>();
        var refresh = AddSpiritmasterSecondaryButton(header, "刷新技能栏", 664, 14, 132);
        refresh.Name = "advancedRefreshSkills"; refresh.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        refresh.Click += async (_, _) =>
        {
            try
            {
                await RefreshQuickbarSkillCandidatesAsync(refresh).ConfigureAwait(true);
                if (dialog.IsDisposed) return;
                foreach (var rows in editors.Values)
                    foreach (Control row in rows.Controls)
                    {
                        var combo = (RoundedComboBox)row.Controls["advancedSkillCombo"]!;
                        var selected = GetSelectedOpeningSkill(combo);
                        combo.Tag = true;
                        try { PopulateOpeningSkillCombo(combo, selected.SkillId, selected.SkillName); }
                        finally { combo.Tag = null; }
                    }
                RefreshAutomaticSkillDisplays();
            }
            catch (Exception ex) { if (!dialog.IsDisposed) status.Text = "刷新未完成：" + ex.Message; }
        };
        foreach (var category in Enum.GetValues<AdvancedMonsterCategory>())
        {
            var group = draft.Group(category);
            var card = new RoundedPanel
            {
                Name = "advancedCard_" + category, Dock = DockStyle.Fill, Margin = new Padding(6),
                BackColor = Color.White, BorderColor = Color.FromArgb(208, 229, 216), CornerRadius = 10, ShadowDepth = 0
            };
            grid.Controls.Add(card, (int)category % 2, (int)category / 2);
            var toggle = AddCheckBox(card, AdvancedCategoryName(category), 14, 10, 220, group.Enabled);
            toggle.Name = "advancedGroupEnabled";
            toggle.Click += (_, _) => group.Enabled = toggle.Checked;
            var manage = AddSpiritmasterSecondaryButton(card, "管理怪物", 260, 10, 188);
            manage.Name = "advancedManageMonsters"; manage.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            void UpdateCount() => manage.Text = "管理怪物（" + group.MonsterNames.Count + "）";
            UpdateCount();
            manage.Click += (_, _) =>
            {
                using var monsters = CreateAdvancedMonsterDialog(draft, category);
                monsters.ShowDialog(dialog); UpdateCount();
            };
            AddLabel(card, "组内按顺序检查，自动匹配技能栏按键", 14, 45, 340, 24);
            var add = AddSpiritmasterSecondaryButton(card, "+ 添加技能", 348, 74, 100);
            add.Name = "advancedAddSkill"; add.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            AddLabel(card, "顺序   技能 / 连续技", 14, 76, 300, 24, _textGreen, FontStyle.Bold);
            var rows = new FlowLayoutPanel
            {
                Name = "advancedSkillRows", Bounds = new Rectangle(12, 111, 438, 110),
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                AutoScroll = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, BackColor = Color.White
            };
            card.Controls.Add(rows); editors.Add(category, rows);
            void ResizeRows()
            {
                var width = Math.Max(400, rows.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 2);
                foreach (Control row in rows.Controls)
                {
                    row.Width = width;
                    row.Controls["advancedSkillCombo"]!.Width = width - 226;
                    row.Controls["advancedSkillKey"]!.Left = width - 194;
                    row.Controls["advancedSkillUp"]!.Left = width - 92;
                    row.Controls["advancedSkillDown"]!.Left = width - 62;
                    row.Controls["advancedSkillRemove"]!.Left = width - 32;
                }
            }
            add.Click += (_, _) => { AddAdvancedSkillRow(rows, new()); ResizeRows(); };
            rows.SizeChanged += (_, _) => ResizeRows();
            foreach (var node in group.Skills) AddAdvancedSkillRow(rows, node);
            card.SizeChanged += (_, _) => { rows.Height = Math.Max(70, card.ClientSize.Height - 125); ResizeRows(); };
        }
        var reserved = new RoundedPanel
        {
            Name = "advancedSpecialReserved", Dock = DockStyle.Fill, Margin = new Padding(6),
            BackColor = Color.White, BorderColor = Color.FromArgb(208, 229, 216), CornerRadius = 10, ShadowDepth = 0
        };
        grid.Controls.Add(reserved, 1, 1);
        AddLabel(reserved, "特殊怪物 · 预留", 16, 12, 330, 28, Color.Gray, FontStyle.Bold);
        AddLabel(reserved, "暂时预留，后续再细化配置。", 28, 92, 400, 32, Color.Gray);
        save.Click += (_, _) =>
        {
            foreach (var (category, rows) in editors)
                draft.Group(category).Skills = rows.Controls.Cast<Control>().Select(CaptureAdvancedSkillRow)
                    .Where(node => node.SkillId != 0 || !string.IsNullOrWhiteSpace(node.Name)).ToList();
            draft.Enabled = enabled.Checked;
            if (draft.DuplicateMonster() is { } duplicate) { status.Text = "怪物重复归类：" + duplicate + "，请先移除重复项。"; return; }
            var previous = currentAdvancedSkillSettings;
            currentAdvancedSkillSettings = draft.Clone();
            if (!SaveCurrentSettings(out var error)) { currentAdvancedSkillSettings = previous; status.Text = error; return; }
            dialog.DialogResult = DialogResult.OK; dialog.Close();
        };
        dialog.CancelButton = close;
        return dialog;
    }

    private void AddAdvancedSkillRow(FlowLayoutPanel rows, SkillConfigNode node)
    {
        var row = new Panel { Name = "advancedSkillRow", Width = 430, Height = 36, Margin = Padding.Empty, Tag = node.Clone() };
        AddLabel(row, "", 0, 3, 24, 26).Name = "advancedSkillOrder";
        var combo = AddCombo(row, 28, 1, 204, 30); combo.Name = "advancedSkillCombo";
        PopulateOpeningSkillCombo(combo, node.SkillId, node.Name);
        var key = AddButton(row, "", 236, 0, 94, 30); key.Name = "advancedSkillKey";
        BindAutomaticSkillButton(key, combo);
        void Reorder(int offset)
        {
            rows.Controls.SetChildIndex(row, Math.Clamp(rows.Controls.GetChildIndex(row) + offset, 0, rows.Controls.Count - 1));
            RenumberAdvancedRows(rows);
        }
        AddSpiritmasterSecondaryButton(row, "↑", 338, 0, 28).Click += (_, _) => Reorder(-1);
        row.Controls[row.Controls.Count - 1].Name = "advancedSkillUp";
        AddSpiritmasterSecondaryButton(row, "↓", 368, 0, 28).Click += (_, _) => Reorder(1);
        row.Controls[row.Controls.Count - 1].Name = "advancedSkillDown";
        var remove = AddSpiritmasterSecondaryButton(row, "×", 398, 0, 28); remove.Name = "advancedSkillRemove";
        remove.Click += (_, _) => { rows.Controls.Remove(row); row.Dispose(); RenumberAdvancedRows(rows); };
        combo.SelectedIndexChanged += (_, _) => { if (combo.Tag is not true) row.Tag = CaptureAdvancedSkillRow(row); };
        rows.Controls.Add(row); RenumberAdvancedRows(rows);
    }

    private static void RenumberAdvancedRows(FlowLayoutPanel rows)
    {
        for (var i = 0; i < rows.Controls.Count; i++) rows.Controls[i].Controls["advancedSkillOrder"]!.Text = (i + 1).ToString();
    }

    private SkillConfigNode CaptureAdvancedSkillRow(Control row)
    {
        var selected = GetSelectedOpeningSkill((RoundedComboBox)row.Controls["advancedSkillCombo"]!);
        if (row.Tag is SkillConfigNode existing && existing.SkillId == selected.SkillId && existing.Name == selected.SkillName)
            return existing.Clone();
        // Reuse the editor's learned chain tree so selecting a starter retains its configured continuations.
        using var candidates = new TreeView();
        PopulateAvailableSkillTreeFromSkills(candidates, currentManualSkills);
        var match = FlattenSkillTree(candidates.Nodes).FirstOrDefault(node => node.Tag is SkillTreeNodeData data && data.SkillId == selected.SkillId);
        return match is not null ? CaptureSkillNode(match) : new() { SkillId = selected.SkillId, Name = selected.SkillName, BaseName = selected.SkillName };
    }

    private static IEnumerable<TreeNode> FlattenSkillTree(TreeNodeCollection nodes)
    {
        foreach (TreeNode node in nodes)
        {
            yield return node;
            foreach (var child in FlattenSkillTree(node.Nodes)) yield return child;
        }
    }
}
