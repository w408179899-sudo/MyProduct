using System.Text.Json;
using Roadhog.Core.Accounts;
using Roadhog.Core.Profiles;

namespace Roadhog;

public sealed partial class AccountSettingsForm
{
    private readonly IAdvancedSkillProfileStore? _advancedSkillProfiles;
    private string currentAdvancedSkillProfileName = string.Empty;
    private bool currentAdvancedSkillProfileEnabled;
    private AdvancedSkillProfile? currentAdvancedSkillProfile;
    private AdvancedSkillSettings localAdvancedSkillSettings = new();

    private void ApplyAdvancedSkillSettings(AdvancedSkillSettings local)
    {
        localAdvancedSkillSettings = local.Clone();
        currentAdvancedSkillSettings = local.Clone();
        currentAdvancedSkillProfile = null;
        if (string.IsNullOrWhiteSpace(currentAdvancedSkillProfileName)) return;
        var result = _advancedSkillProfiles?.LoadAllAsync().GetAwaiter().GetResult();
        currentAdvancedSkillProfile = result?.Success == true ? result.Value?.FirstOrDefault(p =>
            string.Equals(p.Name, currentAdvancedSkillProfileName, StringComparison.OrdinalIgnoreCase)) : null;
        currentAdvancedSkillSettings = currentAdvancedSkillProfile?.Settings.Clone() ?? new();
        currentAdvancedSkillSettings.Enabled = currentAdvancedSkillProfileEnabled;
    }

    private AdvancedSkillSettings CaptureAdvancedSkillSettings() =>
        (string.IsNullOrWhiteSpace(currentAdvancedSkillProfileName) ? currentAdvancedSkillSettings : localAdvancedSkillSettings).Clone();

    private static string AdvancedProfileContent(AdvancedSkillSettings settings)
    {
        var content = settings.Clone(); content.Enabled = false;
        return JsonSerializer.Serialize(content);
    }

    private bool SaveAdvancedSkillSelection(AdvancedSkillSettings draft, string name, AdvancedSkillProfile? baseline, out string error)
    {
        name = name.Trim();
        var oldSettings = currentAdvancedSkillSettings;
        var oldName = currentAdvancedSkillProfileName;
        var oldProfile = currentAdvancedSkillProfile;
        var oldEnabled = currentAdvancedSkillProfileEnabled;
        AdvancedSkillProfile? saved = null;
        var libraryChanged = false;
        if (name.Length > 0)
        {
            if (_advancedSkillProfiles is null) { error = "高级方案库不可用。"; return false; }
            if (baseline is not null && string.Equals(name, baseline.Name, StringComparison.OrdinalIgnoreCase) &&
                AdvancedProfileContent(draft) == AdvancedProfileContent(baseline.Settings))
            {
                var read = _advancedSkillProfiles.LoadAllAsync().GetAwaiter().GetResult();
                saved = read.Success ? read.Value?.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) : null;
                if (saved is null) { error = read.Error ?? "方案已删除，请重新选择。"; return false; }
            }
            else
            {
                var write = _advancedSkillProfiles.SaveAsync(new() { Name = name, Settings = draft.Clone(),
                    Revision = string.Equals(name, baseline?.Name, StringComparison.OrdinalIgnoreCase) ? baseline!.Revision : "" })
                    .GetAwaiter().GetResult();
                if (!write.Success || write.Value is null) { error = write.Error ?? "保存高级方案失败。"; return false; }
                saved = write.Value; libraryChanged = true;
            }
        }
        currentAdvancedSkillSettings = saved?.Settings.Clone() ?? draft.Clone();
        currentAdvancedSkillSettings.Enabled = draft.Enabled;
        currentAdvancedSkillProfileName = saved?.Name ?? "";
        currentAdvancedSkillProfile = saved;
        currentAdvancedSkillProfileEnabled = draft.Enabled;
        if (SaveCurrentSettings(out error))
        {
            if (name.Length == 0) localAdvancedSkillSettings = currentAdvancedSkillSettings.Clone();
            return true;
        }

        // Compensate only our own revision; never overwrite a newer editor's save.
        if (libraryChanged && saved is not null)
        {
            bool restored;
            if (baseline is not null && string.Equals(baseline.Name, saved.Name, StringComparison.OrdinalIgnoreCase))
            {
                var restore = baseline.Clone(); restore.Revision = saved.Revision;
                var rollback = _advancedSkillProfiles!.SaveAsync(restore).GetAwaiter().GetResult();
                restored = rollback.Success;
                if (restored)
                {
                    baseline.Revision = rollback.Value!.Revision;
                    if (oldProfile?.Name == baseline.Name) oldProfile = rollback.Value;
                }
            }
            else restored = _advancedSkillProfiles!.DeleteAsync(saved.Name, saved.Revision).GetAwaiter().GetResult().Success;
            error += restored ? "；高级方案已恢复。" : "；高级方案已保存，回滚未完成，请重新打开核对。";
        }
        currentAdvancedSkillSettings = oldSettings; currentAdvancedSkillProfileName = oldName;
        currentAdvancedSkillProfile = oldProfile; currentAdvancedSkillProfileEnabled = oldEnabled;
        return false;
    }

    private sealed class AdvancedProfileEditor
    {
        public string Name = "";
        public AdvancedSkillProfile? Baseline;
        public bool RequiresName;
    }

    private AdvancedProfileEditor CreateAdvancedProfileControls(Form dialog, Panel header, Label status,
        Func<AdvancedSkillSettings> capture, Action<AdvancedSkillSettings> show)
    {
        var state = new AdvancedProfileEditor { Name = currentAdvancedSkillProfileName, Baseline = currentAdvancedSkillProfile?.Clone() };
        var originalLocal = localAdvancedSkillSettings.Clone();
        AddLabel(header, "方案", 20, 76, 44, 28);
        var combo = AddCombo(header, 66, 76, 282, 30); combo.Name = "advancedProfileCombo";
        AddLabel(header, "名称", 360, 76, 44, 28);
        var name = AddTextBox(header, state.Name, 404, 76, 186, 30); name.Name = "advancedProfileName";
        var create = AddSpiritmasterSecondaryButton(header, "新建", 606, 76, 72); create.Name = "advancedProfileNew";
        var copy = AddSpiritmasterSecondaryButton(header, "另存为", 690, 76, 94); copy.Name = "advancedProfileSaveAs";
        var delete = AddSpiritmasterSecondaryButton(header, "删除", 796, 76, 72); delete.Name = "advancedProfileDelete";
        var loading = false;
        const string localText = "本账号配置（未共享）";
        void ReloadNames()
        {
            var result = _advancedSkillProfiles?.LoadAllAsync().GetAwaiter().GetResult();
            if (result?.Success != true) { status.Text = result?.Error ?? "当前环境未提供共享方案库，可继续保存本账号配置。"; return; }
            loading = true;
            try
            {
                combo.Items.Clear(); combo.Items.Add(localText);
                foreach (var profile in result.Value!) combo.Items.Add(profile.Name);
                if (state.Name.Length > 0 && !combo.Items.Contains(state.Name)) combo.Items.Add(state.Name);
                combo.SelectedItem = state.Name.Length == 0 ? localText : state.Name;
            }
            finally { loading = false; }
        }
        string clean = AdvancedProfileContent(capture());
        bool CanReplaceDraft() => AdvancedProfileContent(capture()) == clean ||
            MessageBox.Show(dialog, "当前方案有未保存修改，是否放弃这些修改？", "切换高级方案", MessageBoxButtons.YesNo,
                MessageBoxIcon.Question) == DialogResult.Yes;
        void LoadDraft(string selected, AdvancedSkillProfile? profile, AdvancedSkillSettings settings)
        {
            state.Name = selected; state.Baseline = profile?.Clone(); state.RequiresName = false;
            name.Text = selected; show(settings.Clone()); clean = AdvancedProfileContent(capture());
            status.Text = selected.Length == 0 ? "当前为本账号配置；填写名称保存后可供其他脚本选择。" :
                "共享方案：" + selected + "；保存修改会影响引用账号，下次启动生效。";
        }
        combo.SelectedIndexChanged += (_, _) =>
        {
            if (loading || combo.SelectedIndex < 0) return;
            var selected = combo.SelectedIndex == 0 ? "" : combo.Text;
            if (!CanReplaceDraft()) { ReloadNames(); return; }
            if (selected.Length == 0) { LoadDraft("", null, originalLocal); return; }
            var result = _advancedSkillProfiles?.LoadAllAsync().GetAwaiter().GetResult();
            var profile = result?.Success == true ? result.Value?.FirstOrDefault(p => p.Name == selected) : null;
            if (profile is null) { status.Text = result?.Error ?? "方案已删除，请重新选择。"; ReloadNames(); return; }
            LoadDraft(profile.Name, profile, profile.Settings);
        };
        foreach (var control in combo.Controls.OfType<ComboBox>()) control.DropDown += (_, _) => ReloadNames();
        foreach (var input in name.Controls.OfType<TextBox>())
            input.TextChanged += (_, _) => state.Name = name.Text.Trim();
        create.Click += (_, _) =>
        {
            if (!CanReplaceDraft()) return;
            LoadDraft("", null, new()); state.RequiresName = true;
            loading = true; combo.SelectedIndex = -1; loading = false; name.Focus();
            status.Text = "新建空白方案：填写名称后点击下方保存。";
        };
        copy.Click += (_, _) =>
        {
            var suggestion = (name.Text.Length > 0 ? name.Text : "高级方案") + " 副本";
            state.Baseline = null; state.RequiresName = true; name.Text = suggestion;
            loading = true; combo.SelectedIndex = -1; loading = false; name.Focus();
            status.Text = "另存为独立方案，不修改原方案；下方保存后生效。";
        };
        delete.Click += (_, _) =>
        {
            if (state.Baseline is null || _advancedSkillProfiles is null) { status.Text = "请先选择已保存的方案。"; return; }
            var accounts = _configStore.LoadAllAsync().GetAwaiter().GetResult();
            if (!accounts.Success) { status.Text = accounts.Error; return; }
            var users = accounts.Value!.Where(a => string.Equals(a.AdvancedSkillProfileName, state.Baseline.Name,
                StringComparison.OrdinalIgnoreCase)).Select(a => a.AccountName).ToArray();
            if (users.Length > 0) { status.Text = "方案被 " + string.Join("、", users) + " 使用，请先切换并保存这些账号的方案。"; return; }
            if (MessageBox.Show(dialog, "删除共享方案“" + state.Baseline.Name + "”？", "删除高级方案",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            var deleted = _advancedSkillProfiles.DeleteAsync(state.Baseline.Name, state.Baseline.Revision).GetAwaiter().GetResult();
            if (!deleted.Success) { status.Text = deleted.Error; return; }
            LoadDraft("", null, originalLocal); ReloadNames();
        };
        if (_advancedSkillProfiles is null) { combo.Enabled = name.Enabled = create.Enabled = copy.Enabled = delete.Enabled = false; }
        ReloadNames();
        return state;
    }
}
