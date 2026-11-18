using Roadhog.Core.Accounts;
using Roadhog.Infrastructure.WorkerProcesses;

namespace Roadhog;

public sealed partial class MultiAccountForm
{
    private static string CombatFlowName(CombatFlowVersion version) => version == CombatFlowVersion.CoordinatedV2
        ? "新版" : "稳定版";

    private static string CombatFlowDescription(AccountProcessView view) =>
        view.Worker?.ActiveCombatFlowVersion is { } active
            ? "当前战斗：" + CombatFlowName(active)
            : "账号选择：" + CombatFlowName(view.Config.CombatFlowVersion);

    private async Task ShowCombatFlowPolicyAsync()
    {
        var loaded = await _workspace.Processes.CombatFlowPolicy.LoadAsync(_operations.Token);
        using var dialog = new Form
        {
            Text = "战斗版本与回退", ClientSize = new Size(510, 220),
            Font = Font, StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false,
            BackColor = SettingsPalette.Page
        };
        var status = !loaded.Success ? "版本设置读取失败，新启动账号将使用稳定版。"
            : loaded.Value!.ForceLegacy ? "当前：全部账号强制使用稳定版。" : "当前：按各账号设置选择战斗版本。";
        dialog.Controls.Add(new Label
        {
            Text = status + "\n\n全部恢复稳定版会停止正在使用新版的账号。\n核对游戏状态后，点击账号的“启动”即可使用稳定版。\n恢复按账号选择只影响下一次启动。",
            Location = new Point(18, 18), Size = new Size(475, 128)
        });
        var legacy = new RoundedButton
        {
            Text = "全部恢复稳定版", Location = new Point(18, 166), Size = new Size(160, 34), DialogResult = DialogResult.OK
        };
        var perAccount = new RoundedButton
        {
            Text = "恢复按账号选择", Location = new Point(196, 166), Size = new Size(160, 34), DialogResult = DialogResult.Retry
        };
        var close = new RoundedButton
        {
            Text = "关闭", Location = new Point(374, 166), Size = new Size(110, 34), DialogResult = DialogResult.Cancel
        };
        dialog.Controls.AddRange([legacy, perAccount, close]);
        dialog.CancelButton = close;
        var choice = dialog.ShowDialog(this);
        if (choice is not (DialogResult.OK or DialogResult.Retry)) return;
        var forceLegacy = choice == DialogResult.OK;
        var results = await _workspace.Processes.SetForceLegacyCombatFlowAsync(forceLegacy, _operations.Token);
        var errors = results.Where(pair => !pair.Value.Success).Select(pair => pair.Key + "：" + pair.Value.Error).ToArray();
        Report(errors.Length > 0 ? "已保存全部稳定版；以下账号停止未完成：" + string.Join("；", errors)
            : forceLegacy ? $"已保存全部稳定版，并停止 {results.Count} 个新版账号；核对游戏状态后点击“启动”。"
            : "已恢复按账号选择；运行中的账号不受影响，下次启动生效。");
        RefreshRows();
    }
}
