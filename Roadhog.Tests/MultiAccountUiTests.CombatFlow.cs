using System.Windows.Forms;

internal static partial class MultiAccountUiTests
{
    public static Task CombatFlowToolbarFitsMinimumWidthAsync() => Sta(() =>
    {
        using var test = new UiEnvironment();
        var accounts = new[] { test.Account(1) };
        test.Initialize(accounts);
        using var form = test.Console(accounts, renderWindow: true);
        var root = Field<TableLayoutPanel>(form, "_layout");
        var toolbar = root.Controls.OfType<FlowLayoutPanel>().Single(panel => panel.Name == "accountToolbar");
        foreach (var width in new[] { 920, 1120, 920 })
        {
            form.Width = width;
            form.PerformLayout(); toolbar.PerformLayout(); Application.DoEvents();
            var buttons = toolbar.Controls.OfType<Button>().ToArray();
            Require(buttons.Any(button => button.Text == "战斗版本") && buttons.Any(button => button.Text == "退出程序"),
                "version and exit controls remain available");
            Require(buttons.All(button => button.Left >= 0 && button.Right <= toolbar.ClientSize.Width &&
                button.Top >= 0 && button.Bottom <= toolbar.ClientSize.Height), "all toolbar actions fit at width " + width);
            Require(root.GetRowHeights()[0] >= toolbar.Controls.Cast<Control>().Max(control => control.Bottom),
                "wrapped toolbar receives sufficient row height");
        }
    });
}
