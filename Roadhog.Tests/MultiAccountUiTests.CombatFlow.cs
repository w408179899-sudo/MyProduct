using System.Windows.Forms;
using Roadhog.Core.Diagnostics;
using Roadhog.Infrastructure.Config;
using Roadhog.Infrastructure.WorkerProcesses;

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

        var cacheField = toolbar.Controls.Find("dmaReadCacheField", false).Single();
        var cacheInput = cacheField.Controls.Find("dmaReadCacheInput", false).Single().Controls.OfType<TextBox>().Single();
        Require(cacheField.Left > exit.Right && cacheField.Top < exit.Bottom && cacheField.Right <= toolbar.ClientSize.Width,
            "the cache input fits to the right of exit on the first row");
        Require(cacheInput.Enabled && cacheInput.Text == "150", "the default cache period is editable");

        var workerOptions = RoadhogWorkerProcessBackend.CreateOptions(new WorkerLaunchSpec
        {
            Account = accounts.Single(),
            Paths = test.Workspace.Processes.PathsFor(accounts.Single())
        });
        var workerSettings = new JsonDmaReadCacheStore(workerOptions.AccountConfigPath);
        var logger = new InMemoryRoadhogLogger();
        workerSettings.Refresh(logger);
        Require(workerSettings.CurrentPeriodMs == 150, "a worker starts with the default cache period");

        cacheInput.Text = "125";
        Invoke(cacheInput, "OnKeyDown", new KeyEventArgs(Keys.Enter));
        Require(test.Workspace.DmaReadCache.Load() == 150, "a period that cannot align with the VMM tick is rejected");

        cacheInput.Text = "100";
        Invoke(cacheInput, "OnKeyDown", new KeyEventArgs(Keys.Enter));
        Until(() => !cacheInput.ReadOnly, "cache save completes");
        Require(test.Workspace.DmaReadCache.Load() == 100, "Enter saves the new cache period");
        Until(() =>
        {
            workerSettings.Refresh(logger);
            return workerSettings.CurrentPeriodMs == 100;
        }, "a separate worker settings instance reloads the saved value");

        using var reopened = test.Console(accounts, renderWindow: true);
        var reopenedInput = reopened.Controls.Find("dmaReadCacheInput", true).Single();
        Require(reopenedInput.Text == "100", "the console loads the saved period on startup");
    });
}
