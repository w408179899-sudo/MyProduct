using System.Text.Json;
using Roadhog.Core.Accounts;

namespace Roadhog;

public sealed partial class AccountSettingsForm
{
    private Label? accountSaveStatus;
    private string? savedAccountDraft;
    private string? savedProfileDraft;
    private string cleanAccountStatus = "当前账号 · 无未保存修改";
    private readonly System.Windows.Forms.Timer accountDraftTimer = new() { Interval = 150 };
    internal Func<string, DialogResult>? AccountDraftChoiceForTesting { get; set; }

    private void InitializeAccountDraftTracking()
    {
        MarkAccountDraftSaved(cleanAccountStatus);
        accountDraftTimer.Tick += (_, _) =>
        {
            accountDraftTimer.Stop();
            RefreshAccountDraftStatus();
        };
        Disposed += (_, _) => accountDraftTimer.Dispose();
        TrackAccountControl(settingsTabs);
    }

    private void TrackAccountControl(Control control)
    {
        switch (control)
        {
            case RoundedComboBox combo:
                combo.SelectedIndexChanged += AccountDraftEdited;
                combo.TextChanged += AccountDraftEdited;
                break;
            case RoundedCheckBox check:
                check.Click += AccountDraftEdited;
                break;
            case TextBox or RoundedTextBox:
                control.TextChanged += AccountDraftEdited;
                break;
            case CheckBox check:
                check.CheckedChanged += AccountDraftEdited;
                break;
            case RadioButton radio:
                radio.CheckedChanged += AccountDraftEdited;
                break;
            case NumericUpDown numeric:
                numeric.ValueChanged += AccountDraftEdited;
                break;
            case Button button:
                button.Click += AccountDraftEdited;
                break;
            case TreeView tree:
                tree.AfterSelect += (_, _) => AccountDraftEdited(tree, EventArgs.Empty);
                break;
        }

        control.ControlAdded += (_, args) =>
        {
            if (args.Control is { } child) TrackAccountControl(child);
            AccountDraftEdited(control, EventArgs.Empty);
        };
        control.ControlRemoved += AccountDraftEdited;
        foreach (Control child in control.Controls) TrackAccountControl(child);
    }

    private void AccountDraftEdited(object? sender, EventArgs e)
    {
        if (IsDisposed || Disposing || savedAccountDraft is null) return;
        accountDraftTimer.Stop();
        accountDraftTimer.Start();
    }

    private string AccountDraftFingerprint(bool profileOnly = false)
    {
        var settings = CaptureScriptSettings();
        if (_bagCleanupNameListStore is ISharedAccountConfiguration)
            settings.Combat.ActiveMonsterNameFilters.Clear();
        if (_bagCleanupNameListStore is not null)
        {
            settings.Maintenance.BagCleanupExcludedItemNames.Clear();
            settings.Maintenance.BagCleanupDiscardItemNameKeywords.Clear();
            settings.Maintenance.BagCleanupSellItemNameKeywords.Clear();
            settings.Maintenance.ItemTransferItemNameKeywords.Clear();
            settings.Maintenance.BagCleanupStallItems.Clear();
            settings.Maintenance.BagCleanupAuctionHouseItems.Clear();
        }
        return JsonSerializer.Serialize(new
        {
            Settings = settings,
            Region = profileOnly ? null : regionCombo?.Text,
            Schedule = profileOnly ? null : ReadDailyScheduleDraft(),
            TargetDistance = targetAcquireDistanceTextBox?.Text.Trim(),
            ReturnDistance = returnHomeAfterCombatDistanceTextBox?.Text.Trim()
        });
    }

    private bool HasAccountDraftChanges() => savedAccountDraft is not null && savedAccountDraft != AccountDraftFingerprint();

    private void MarkAccountDraftSaved(string status)
    {
        savedAccountDraft = AccountDraftFingerprint();
        savedProfileDraft = AccountDraftFingerprint(profileOnly: true);
        cleanAccountStatus = status;
        RefreshAccountDraftStatus();
    }

    private void RefreshAccountDraftStatus()
    {
        if (accountSaveStatus is null || IsDisposed || Disposing) return;
        var changed = HasAccountDraftChanges();
        accountSaveStatus.Text = changed ? "当前账号 · 有未保存修改" : cleanAccountStatus;
        accountSaveStatus.ForeColor = changed ? Color.SaddleBrown : _textGreen;
    }

    private bool ResolveAccountDraft(string action, bool profileOnly = false)
    {
        if (profileOnly
            ? savedProfileDraft is null || savedProfileDraft == AccountDraftFingerprint(profileOnly: true)
            : !HasAccountDraftChanges()) return true;
        var scope = profileOnly ? "当前方案" : "当前账号";
        var choice = AccountDraftChoiceForTesting?.Invoke(action) ?? MessageBox.Show(this,
            $"{scope}有未保存修改。\n是：保存后{action}\n否：放弃修改并{action}\n取消：继续编辑",
            "未保存的账号设置", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question, MessageBoxDefaultButton.Button3);
        return ResolveAccountDraftChoice(choice);
    }

    private bool ResolveAccountDraftChoice(DialogResult choice)
    {
        if (choice == DialogResult.No) return true;
        if (choice != DialogResult.Yes) return false;
        if (SaveCurrentSettings(out var error)) return true;
        MessageBox.Show(this, error, "保存账号设置失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        return false;
    }
}
