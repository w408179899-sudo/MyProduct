using Roadhog.Core.Accounts;

namespace Roadhog;

public sealed partial class AccountSettingsForm
{
    private RoundedCheckBox? openingSkillReleaseAllCheckBox;
    private FlowLayoutPanel? openingSkillRows;

    private void CreateOpeningSkillEditor(Panel parent)
    {
        var panel = new Panel
        {
            Name = "openingSkillPanel", BackColor = _inputBackground,
            Location = new Point(0, 346), Size = new Size(828, 82)
        };
        parent.Controls.Add(panel);
        openingSkillEnabledCheckBox = AddCheckBox(panel, "启用起手技能", 12, 8, 128, false);
        openingSkillReleaseAllCheckBox = AddCheckBox(panel, "依次释放全部", 152, 8, 132, false);
        openingSkillReleaseAllCheckBox.Name = "openingSkillReleaseAllCheckBox";
        openingSkillEnabledCheckBox.BackColor = openingSkillReleaseAllCheckBox.BackColor = panel.BackColor;
        AddLabel(panel, "不勾选：只放首个可用技能；冷却中跳过", 296, 8, 380, 24);
        var add = AddButton(panel, "添加技能", 716, 5, 100, 28, (_, _) => AddOpeningSkillRow(new()));
        add.Name = "openingSkillAddButton";
        openingSkillRows = new FlowLayoutPanel
        {
            Name = "openingSkillRows", Location = new Point(12, 40), Width = 804,
            FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = Padding.Empty,
            BackColor = panel.BackColor
        };
        panel.Controls.Add(openingSkillRows);
        ResizeOpeningSkillEditor();
    }

    private void AddOpeningSkillRow(OpeningSkillEntryConfig config)
    {
        if (openingSkillRows is null) return;
        var row = new Panel { Width = 804, Height = 36, Margin = Padding.Empty, Name = "openingSkillRow" };
        AddLabel(row, (openingSkillRows.Controls.Count + 1).ToString(), 0, 3, 28, 24).Name = "openingSkillOrder";
        var combo = AddCombo(row, 32, 1, 408, 28);
        combo.Name = "openingSkillCombo";
        PopulateOpeningSkillCombo(combo, config.SkillId, config.SkillName);
        var key = AddButton(row, "", 450, 0, 136, 30);
        key.Name = "openingSkillKeyButton";
        SetKeyButton(key, config.Key);
        BindAutomaticSkillButton(key, combo);
        AddButton(row, "上移", 596, 0, 62, 30, (_, _) => MoveOpeningSkillRow(row, -1)).Name = "openingSkillUp";
        AddButton(row, "下移", 664, 0, 62, 30, (_, _) => MoveOpeningSkillRow(row, 1)).Name = "openingSkillDown";
        AddButton(row, "删除", 732, 0, 62, 30, (_, _) =>
        {
            openingSkillRows.Controls.Remove(row);
            row.Dispose();
            ResizeOpeningSkillEditor();
        }).Name = "openingSkillRemove";
        openingSkillRows.Controls.Add(row);
        ResizeOpeningSkillEditor();
    }

    private void MoveOpeningSkillRow(Panel row, int offset)
    {
        if (openingSkillRows is null) return;
        var index = openingSkillRows.Controls.GetChildIndex(row);
        openingSkillRows.Controls.SetChildIndex(row, Math.Clamp(index + offset, 0, openingSkillRows.Controls.Count - 1));
        ResizeOpeningSkillEditor();
    }

    private void ResizeOpeningSkillEditor()
    {
        if (openingSkillRows?.Parent is not Panel panel) return;
        for (var i = 0; i < openingSkillRows.Controls.Count; i++)
            openingSkillRows.Controls[i].Controls["openingSkillOrder"]!.Text = (i + 1).ToString();
        openingSkillRows.Height = Math.Max(36, openingSkillRows.Controls.Count * 36);
        panel.Height = openingSkillRows.Bottom + 6;
        if (panel.Parent is Control parent) parent.Height = panel.Bottom + 14;
    }

    private void ApplyOpeningSkillSettings(OpeningSkillConfig? config)
    {
        config ??= new();
        if (openingSkillEnabledCheckBox is not null) openingSkillEnabledCheckBox.Checked = config.Enabled;
        if (openingSkillReleaseAllCheckBox is not null) openingSkillReleaseAllCheckBox.Checked = config.ReleaseAll;
        if (openingSkillRows is null) return;
        foreach (Control row in openingSkillRows.Controls.Cast<Control>().ToArray()) row.Dispose();
        foreach (var skill in config.GetEffectiveSkills()) AddOpeningSkillRow(skill);
        if (config.Skills is null && openingSkillRows.Controls.Count == 0) AddOpeningSkillRow(new());
        ResizeOpeningSkillEditor();
    }

    private OpeningSkillConfig CaptureOpeningSkill()
    {
        var skills = new List<OpeningSkillEntryConfig>();
        if (openingSkillRows is not null)
            foreach (Control row in openingSkillRows.Controls)
            {
                var selected = GetSelectedOpeningSkill((RoundedComboBox)row.Controls["openingSkillCombo"]!);
                if (selected.SkillId == 0 && string.IsNullOrWhiteSpace(selected.SkillName)) continue;
                skills.Add(new()
                {
                    SkillId = selected.SkillId, SkillName = selected.SkillName,
                    Key = row.Controls["openingSkillKeyButton"]!.Tag as string ?? string.Empty
                });
            }
        var first = skills.FirstOrDefault();
        return new()
        {
            Enabled = openingSkillEnabledCheckBox?.Checked ?? false,
            ReleaseAll = openingSkillReleaseAllCheckBox?.Checked ?? false, Skills = skills,
            // Retain the first entry for older versions that only understand a single opener.
            SkillId = first?.SkillId ?? 0, SkillName = first?.SkillName ?? string.Empty, Key = first?.Key ?? string.Empty
        };
    }

    private void RefreshOpeningSkillCombo()
    {
        if (openingSkillRows is null) return;
        foreach (Control row in openingSkillRows.Controls)
        {
            var combo = (RoundedComboBox)row.Controls["openingSkillCombo"]!;
            var selected = GetSelectedOpeningSkill(combo);
            PopulateOpeningSkillCombo(combo, selected.SkillId, selected.SkillName);
        }
    }
}
