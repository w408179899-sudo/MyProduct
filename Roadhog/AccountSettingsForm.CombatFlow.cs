using Roadhog.Core.Accounts;

namespace Roadhog;

public partial class AccountSettingsForm
{
    private RoundedComboBox? combatFlowVersionCombo;

    private void BuildCombatFlowSelection(Panel panel)
    {
        AddLabel(panel, "战斗逻辑", 400, 1, 80, 24).BringToFront();
        combatFlowVersionCombo = AddCombo(panel, 484, 0, 164, 26, "新版（默认）", "稳定版（回退）");
        combatFlowVersionCombo.Name = "combatFlowVersionCombo";
        combatFlowVersionCombo.BringToFront();
        AddLabel(panel, "下次启动账号生效", 660, 1, 158, 24).BringToFront();
    }

    private void LoadCombatFlowSelection(AccountConfig account)
    {
        if (combatFlowVersionCombo is not null)
            combatFlowVersionCombo.SelectedIndex = account.CombatFlowVersion == CombatFlowVersion.CoordinatedV2 ? 0 : 1;
    }

    private CombatFlowVersion CaptureCombatFlowVersion() => combatFlowVersionCombo?.SelectedIndex == 1
        ? CombatFlowVersion.Legacy : CombatFlowVersion.CoordinatedV2;
}
