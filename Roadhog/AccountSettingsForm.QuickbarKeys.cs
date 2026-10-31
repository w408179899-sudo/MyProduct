using Roadhog.Core.Accounts;

namespace Roadhog;

public sealed partial class AccountSettingsForm
{
    private RadioButton? numpadKeyModeRadio, gameDefaultKeyModeRadio;
    private Label? quickbarKeyHintLabel;
    private int quickbarKeyModeRevision;

    private QuickbarKeyMode SelectedQuickbarKeyMode => gameDefaultKeyModeRadio?.Checked == true
        ? QuickbarKeyMode.GameDefault : QuickbarKeyMode.Numpad;

    private void CreateQuickbarKeyModeEditor(Control options)
    {
        var group = new Panel { Name = "quickbarKeyModePanel", Location = new(574, 29), Size = new(250, 32) };
        options.Controls.Add(group);
        numpadKeyModeRadio = AddRadioButton(group, "小键盘改键", 0, 2, 124, true);
        numpadKeyModeRadio.Name = "numpadKeyModeRadio";
        gameDefaultKeyModeRadio = AddRadioButton(group, "游戏默认键", 126, 2, 124, false);
        gameDefaultKeyModeRadio.Name = "gameDefaultKeyModeRadio";
        numpadKeyModeRadio.CheckedChanged += (_, _) => { if (numpadKeyModeRadio.Checked) QuickbarKeyModeChanged(); };
        gameDefaultKeyModeRadio.CheckedChanged += (_, _) => { if (gameDefaultKeyModeRadio.Checked) QuickbarKeyModeChanged(); };
        var tip = new ToolTip();
        tip.SetToolTip(numpadKeyModeRadio, "第一排数字键；第二排小键盘。" + SkillModeApplyHint);
        tip.SetToolTip(gameDefaultKeyModeRadio, "第一排数字键；第二排 Alt+数字；第三排 Ctrl+数字（含 -、=）。" + SkillModeApplyHint);
        group.Disposed += (_, _) => tip.Dispose();
        QuickbarKeyModeChanged();
    }

    private void LoadQuickbarKeyMode(QuickbarKeyMode mode)
    {
        if (gameDefaultKeyModeRadio is null || numpadKeyModeRadio is null) return;
        gameDefaultKeyModeRadio.Checked = mode == QuickbarKeyMode.GameDefault;
        numpadKeyModeRadio.Checked = mode != QuickbarKeyMode.GameDefault;
        QuickbarKeyModeChanged();
    }

    private void QuickbarKeyModeChanged()
    {
        quickbarKeyModeRevision++;
        previewSkillBindings = null;
        drinkPreferencePicker?.MarkUnresolved();
        foodPreferencePicker?.MarkUnresolved();
        if (quickbarKeyHintLabel is not null)
            quickbarKeyHintLabel.Text = SelectedQuickbarKeyMode == QuickbarKeyMode.GameDefault
                ? "匹配三排：数字 / Alt+数字 / Ctrl+数字；保存后重启脚本"
                : "匹配两排：数字 / 小键盘；移动技能或切换模式后重启脚本";
        RefreshAutomaticSkillDisplays();
    }
}
