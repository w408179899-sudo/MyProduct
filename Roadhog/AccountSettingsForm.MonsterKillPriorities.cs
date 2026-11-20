using Roadhog.Core.Accounts;

namespace Roadhog;

public sealed partial class AccountSettingsForm
{
    private ListBox monsterKillPriorityListBox = null!;
    private Label monsterKillPriorityPathLabel = null!;
    private Control[] monsterKillPriorityControls = Array.Empty<Control>();
    private string monsterKillPriorityPathName = string.Empty;

    private void BuildMonsterKillPriorityEditor(Panel page, Button addPriority)
    {
        AddLabel(page, "怪物击杀优先级", 424, 90, 194, 24, _textGreen, FontStyle.Bold);
        var moveUp = AddButton(page, "上移", 620, 86, 48, 30, (_, _) => MoveMonsterKillPriority(-1));
        var moveDown = AddButton(page, "下移", 672, 86, 48, 30, (_, _) => MoveMonsterKillPriority(1));
        var remove = AddButton(page, "移除", 724, 86, 48, 30, (_, _) => RemoveMonsterKillPriority());
        var clear = AddButton(page, "清空", 776, 86, 48, 30, (_, _) => SaveMonsterKillPriorities(new List<string>()));
        monsterKillPriorityPathLabel = AddLabel(page, string.Empty, 424, 116, 400, 22, _textGreen, FontStyle.Bold);
        monsterKillPriorityPathLabel.AutoEllipsis = true;
        AddLabel(page, "越靠上越先打，名单内没有可打的怪物时再打其他怪物。", 424, 139, 400, 24);
        monsterKillPriorityListBox = CreateFilterListBox(page, 424, 166, 400, 274);
        monsterKillPriorityListBox.Name = "monsterKillPriorityListBox";
        monsterKillPriorityListBox.SelectionMode = SelectionMode.One;
        monsterKillPriorityListBox.BackColor = Color.White;
        monsterKillPriorityControls = new Control[] { addPriority, moveUp, moveDown, remove, clear, monsterKillPriorityListBox };
        page.VisibleChanged += (_, _) => { if (page.Visible) RefreshMonsterKillPriorities(); };
    }

    private void RefreshMonsterKillPriorities()
    {
        var pathName = GetText(revivePathNameTextBox, string.Empty);
        monsterKillPriorityPathName = string.Empty;
        foreach (var control in monsterKillPriorityControls) control.Enabled = false;
        PopulateMonsterKillPriorities(null);
        if (pathName.Length == 0)
        {
            monsterKillPriorityPathLabel.Text = "请在「路径 → 复活路径」选择已保存的路径";
            return;
        }

        var loaded = _pathStore.LoadMonsterKillPrioritiesAsync(pathName).GetAwaiter().GetResult();
        if (!loaded.Success)
        {
            monsterKillPriorityPathLabel.Text = "复活路径未保存或无法读取 · " + pathName;
            return;
        }

        monsterKillPriorityPathName = pathName;
        monsterKillPriorityPathLabel.Text = "绑定复活路径 · " + pathName;
        PopulateMonsterKillPriorities(loaded.Value);
        foreach (var control in monsterKillPriorityControls) control.Enabled = true;
    }

    private void AddMonsterKillPriority(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return;

        var next = CaptureMonsterKillPriorities();
        var index = next.FindIndex(existing => string.Equals(existing, name, StringComparison.OrdinalIgnoreCase));
        if (index >= 0)
        {
            monsterKillPriorityListBox.SelectedIndex = index;
            return;
        }

        next.Add(name);
        SaveMonsterKillPriorities(next, next.Count - 1);
    }

    private void MoveMonsterKillPriority(int offset)
    {
        var index = monsterKillPriorityListBox.SelectedIndex;
        var destination = index + offset;
        var next = CaptureMonsterKillPriorities();
        if (index < 0 || destination < 0 || destination >= next.Count) return;

        (next[index], next[destination]) = (next[destination], next[index]);
        SaveMonsterKillPriorities(next, destination);
    }

    private void RemoveMonsterKillPriority()
    {
        var index = monsterKillPriorityListBox.SelectedIndex;
        if (index < 0) return;

        var next = CaptureMonsterKillPriorities();
        next.RemoveAt(index);
        SaveMonsterKillPriorities(next, Math.Min(index, next.Count - 1));
    }

    private void SaveMonsterKillPriorities(List<string> next, int selectedIndex = -1)
    {
        var pathName = GetText(revivePathNameTextBox, string.Empty);
        if (monsterKillPriorityPathName.Length == 0 ||
            !string.Equals(monsterKillPriorityPathName, pathName, StringComparison.OrdinalIgnoreCase))
        {
            RefreshMonsterKillPriorities();
            return;
        }

        var result = _pathStore.SaveMonsterKillPrioritiesAsync(pathName, CaptureMonsterKillPriorities(), next).GetAwaiter().GetResult();
        if (!result.Success)
        {
            RefreshMonsterKillPriorities();
            SetActiveMonsterFilterStatus("优先名单保存失败：" + result.Error, true);
            return;
        }

        SetActiveMonsterFilterStatus("击杀优先级已自动保存到复活路径 · " + pathName, false);
        PopulateMonsterKillPriorities(next);
        monsterKillPriorityListBox.SelectedIndex = selectedIndex;
    }

    private void PopulateMonsterKillPriorities(IEnumerable<string>? priorities)
    {
        monsterKillPriorityListBox.BeginUpdate();
        monsterKillPriorityListBox.Items.Clear();
        monsterKillPriorityListBox.Items.AddRange(BagCleanupNameListsDocument.NormalizeKeywords(priorities).ToArray());
        monsterKillPriorityListBox.EndUpdate();
    }

    private List<string> CaptureMonsterKillPriorities() => monsterKillPriorityListBox.Items.Cast<string>().ToList();
}
