using Roadhog.Core.Accounts;

namespace Roadhog;

public sealed partial class AccountSettingsForm
{
    private Form CreateAdvancedMonsterDialog(AdvancedSkillSettings draft, AdvancedMonsterCategory category)
    {
        var group = draft.Group(category);
        var dialog = new Form
        {
            Name = "advancedMonsterDialog", Text = "管理怪物 - " + AdvancedCategoryName(category),
            ClientSize = new Size(860, 490), MinimumSize = new Size(876, 529),
            BackColor = _pageBackground, Font = new Font("Microsoft YaHei UI", 9F),
            AutoScaleDimensions = new SizeF(7F, 17F), AutoScaleMode = AutoScaleMode.Font,
            StartPosition = FormStartPosition.CenterParent, ShowIcon = false
        };
        var lifetime = new CancellationTokenSource();
        var token = lifetime.Token;
        dialog.FormClosed += (_, _) => lifetime.Cancel();
        dialog.Disposed += (_, _) => lifetime.Dispose();
        AddLabel(dialog, "当前遍历结果（同名合并，可多选）", 16, 12, 320, 28, _textGreen, FontStyle.Bold);
        var refresh = AddButton(dialog, "遍历怪物", 256, 10, 116, 30);
        refresh.Name = "advancedScanMonsters";
        AddLabel(dialog, AdvancedCategoryName(category) + "名单", 484, 12, 350, 28, _textGreen, FontStyle.Bold);
        var available = new ListBox
        {
            Name = "advancedAvailableMonsters", Bounds = new Rectangle(16, 49, 356, 304),
            SelectionMode = SelectionMode.MultiExtended, IntegralHeight = false,
            ForeColor = _textGreen, BackColor = Color.White
        };
        var assigned = new ListBox
        {
            Name = "advancedAssignedMonsters", Bounds = new Rectangle(484, 49, 356, 304),
            SelectionMode = SelectionMode.MultiExtended, IntegralHeight = false,
            ForeColor = _textGreen, BackColor = Color.White
        };
        dialog.Controls.Add(available); dialog.Controls.Add(assigned);
        var status = AddLabel(dialog, "仅按完整怪物名称匹配；一个怪物只能归属一个类别。", 16, 410, 820, 28);
        status.Name = "advancedMonsterStatus";
        void Reload()
        {
            assigned.Items.Clear();
            assigned.Items.AddRange(group.MonsterNames.OrderBy(name => name, StringComparer.CurrentCulture).Cast<object>().ToArray());
        }
        bool AddName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) { status.Text = "请输入怪物名称。"; return false; }
            if (!draft.TryAddMonster(category, name, out var existing))
            {
                status.Text = name.Trim() + " 已属于“" + AdvancedCategoryName(existing!.Value) + "”，不能重复添加。";
                return false;
            }
            status.Text = "已添加：" + name.Trim() + "；返回高级设置后保存生效。";
            Reload(); return true;
        }
        AddButton(dialog, "添加 >", 388, 139, 80, 30, (_, _) =>
        {
            foreach (var name in available.SelectedItems.Cast<string>().ToArray()) AddName(name);
        }).Name = "advancedAddSelectedMonster";
        AddSpiritmasterSecondaryButton(dialog, "移除", 388, 181, 80).Click += (_, _) =>
        {
            foreach (var name in assigned.SelectedItems.Cast<string>().ToArray()) group.MonsterNames.Remove(name);
            Reload(); status.Text = "已更新名单，返回高级设置后保存生效。";
        };
        AddLabel(dialog, "手动名称", 16, 369, 86, 28);
        var input = AddTextBox(dialog, "", 102, 369, 610, 30);
        input.Name = "advancedManualMonsterName";
        void AddManual() { if (AddName(input.Text)) input.Text = ""; }
        AddButton(dialog, "添加到本类", 728, 369, 112, 30, (_, _) => AddManual()).Name = "advancedAddManualMonster";
        foreach (var textbox in input.Controls.OfType<TextBox>())
            textbox.KeyDown += (_, args) =>
            {
                if (args.KeyCode != Keys.Enter) return;
                args.SuppressKeyPress = true; AddManual();
            };
        var done = AddButton(dialog, "完成", 728, 449, 112, 30, (_, _) => dialog.Close());
        dialog.CancelButton = done;
        refresh.Click += async (_, _) =>
        {
            refresh.Enabled = false;
            try
            {
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
                deadline.CancelAfter(TimeSpan.FromSeconds(8));
                var monsters = await _runtime.RefreshWorldObjectsAsync(_account, deadline.Token).ConfigureAwait(true);
                if (dialog.IsDisposed || token.IsCancellationRequested) return;
                var names = monsters.Where(IsMonsterFilterCandidate).Select(monster => monster.Name.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(name => name, StringComparer.CurrentCulture).ToArray();
                available.Items.Clear(); available.Items.AddRange(names);
                status.Text = "遍历到 " + names.Length + " 种怪物；也可在下方手动输入名称。";
            }
            catch (Exception ex)
            {
                if (!dialog.IsDisposed && !token.IsCancellationRequested) status.Text = "遍历未完成：" + ex.Message;
            }
            finally { if (!refresh.IsDisposed) refresh.Enabled = true; }
        };
        Reload();
        return dialog;
    }
}
