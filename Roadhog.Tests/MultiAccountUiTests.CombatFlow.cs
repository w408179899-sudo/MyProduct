using System.Windows.Forms;

internal static partial class MultiAccountUiTests
{
    public static Task ConsoleToolbarFitsFixedWidthAsync() => Sta(() =>
    {
        using var test = new UiEnvironment();
        var accounts = new[] { test.Account(1) };
        test.Initialize(accounts);
        using var form = test.Console(accounts, renderWindow: true);
        var root = Field<TableLayoutPanel>(form, "_layout");
        var toolbar = root.Controls.OfType<FlowLayoutPanel>().Single(panel => panel.Name == "accountToolbar");
        var initialHeight = form.Height;
        var fixedWidth = form.Width;
        Require(!form.MaximizeBox, "the console does not expose maximize");
        foreach (var width in new[] { fixedWidth - 200, fixedWidth + 200 })
        {
            form.Width = width;
            Require(form.Width == fixedWidth, "the console rejects horizontal resizing in both directions");
        }
        form.Height = initialHeight + 80;
        Require(form.Height == initialHeight + 80 && form.Width == fixedWidth, "the console can grow vertically");
        form.Height = initialHeight;
        Require(form.Height == initialHeight && form.Width == fixedWidth, "the console can shrink vertically");

        form.PerformLayout(); toolbar.PerformLayout(); Application.DoEvents();
        var buttons = toolbar.Controls.OfType<Button>().ToArray();
        Require(buttons.All(button => button.Text != "战斗版本") && buttons.Last().Text == "退出程序",
            "the retired version control is gone and exit is the final toolbar action");
        Require(buttons.Any(button => button.Text == "添加账号") && buttons.All(button => button.Text != "导入旧配置"),
            "accounts can be added manually without a legacy import action");
        Require(buttons.All(button => button.Left >= 0 && button.Right <= toolbar.ClientSize.Width &&
            button.Top >= 0 && button.Bottom <= toolbar.ClientSize.Height), "all toolbar actions fit at the fixed width");
        Require(root.GetRowHeights()[0] >= toolbar.Controls.Cast<Control>().Max(control => control.Bottom),
            "toolbar receives sufficient row height");
        var exit = buttons.Single(button => button.Text == "退出程序");
        var cleanup = buttons.Single(button => button.Text == "清包");
        var transfer = buttons.Single(button => button.Text == "倒货");
        Require(cleanup.Top == exit.Top && transfer.Top == exit.Top && transfer.Left > cleanup.Right && exit.Left > transfer.Right,
            "cleanup, transfer and exit fit on the same row in that order");
    });
}
