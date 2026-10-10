using System.Reflection;
using System.Windows.Forms;
using Roadhog;
using Roadhog.Application;
using Roadhog.Core.Accounts;
using Roadhog.Core.Common;
using Roadhog.Core.Diagnostics;
using Roadhog.Core.Model;
using Roadhog.Core.Paths;

internal static partial class GroceryShopTests
{
    public static Task UiScrollRefreshByItemTypeAsync()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                const string returnBook = "伏魔殿返程咒语书";
                const string returnOrb = "贝达扎尔村返程珠";
                var names = new[]
                {
                    returnBook, returnOrb,
                    "阿尔特盖德要塞返程咒语书", "贝鲁斯兰要塞返程咒语书", "莫尔海姆要塞返程咒语书",
                    "同类物品无名称关键词"
                };
                var items = names.Select((name, slot) => new InventoryItemSnapshot(164000089, (ulong)slot + 1, name, 1, slot, false, 18, UseGroup: 36)).ToList();
                items.Add(new(164002011, 106, "[活动]高级疾走咒语书", 31, 33, false, 18, UseGroup: 31));
                items.Add(new(164002012, 107, "[活动]高级狂风咒语书", 8, 34, false, 18, UseGroup: 31));
                items.Add(new(6, 108, "返程咒语书但缺少细分类", 1, 25, false, 18));
                items.Add(new(7, 109, "返程字样但实际是增益卷轴", 1, 26, false, 18, UseGroup: 31));
                items.Add(items[0] with { InstanceId = 100, Slot = 20 });
                items.Add(new(1, 101, "返程卷轴名字但类型不同", 1, 21, false, 17, UseGroup: 36));
                items.Add(new(2, 102, "已装备", 1, 22, true, 18, UseGroup: 36));
                items.Add(new(3, 103, "数量为零", 0, 23, false, 18, UseGroup: 36));
                items.Add(new(4, 104, "不在背包格", 1, -1, false, 18, UseGroup: 36));
                items.Add(new(5, 105, " ", 1, 24, false, 18, UseGroup: 36));
                var api = new FakeGameApi { InventoryItems = items };
                var logger = new InMemoryRoadhogLogger();
                var settings = new ScriptSettings();
                settings.Paths.GroceryReturnItemName = returnBook;
                settings.Paths.StallReturnItemName = returnOrb;
                var store = new InMemoryAccountConfigStore(new AccountConfig { AccountName = "grocery-refresh", ProcessId = 712, ScriptSettings = settings });
                using var form = new AccountSettingsForm("grocery-refresh",
                    new RoadhogRuntime(api, logger, new AccountRuntimeManager(logger), null!, store), store,
                    new InMemorySharedPathStore(), new InMemoryScriptProfileStore(), new RecordingFolderLauncher(), "test-paths");
                form.ShowInTaskbar = false; form.Location = new(-32000, -32000); form.StartPosition = FormStartPosition.Manual;
                form.Show(); System.Windows.Forms.Application.DoEvents();
                T Find<T>(string name) where T : Control => (T)form.Controls.Find(name, true).Single();
                var refresh = Find<Button>("groceryRefreshScrollButton");
                for (Control? control = refresh.Parent; control != null; control = control.Parent)
                    if (control is TabPage page) ((TabControl)page.Parent!).SelectedTab = page;
                System.Windows.Forms.Application.DoEvents();
                var scroll = Find<RoundedComboBox>("groceryReturnItemCombo");
                void Refresh()
                {
                    var before = api.InventoryReadCount;
                    Check(refresh.Visible && refresh.Enabled, "refresh button is available on grocery path tab");
                    refresh.PerformClick();
                    var deadline = System.Diagnostics.Stopwatch.StartNew();
                    while ((!refresh.Enabled || api.InventoryReadCount == before) && deadline.Elapsed < TimeSpan.FromSeconds(5))
                    { System.Windows.Forms.Application.DoEvents(); Thread.Sleep(5); }
                    Check(refresh.Enabled && api.InventoryReadCount > before, "refresh finishes and re-enables button");
                }
                Refresh();
                Check(scroll.Items.Cast<string>().SequenceEqual(names.Order()), "use return group 36 rather than broad type 18 or names; exclude actual speed and flight buffs in group 31");
                IEnumerable<Control> Descendants(Control parent) => parent.Controls.Cast<Control>()
                    .SelectMany(child => new[] { child }.Concat(Descendants(child)));
                var status = Descendants(form).OfType<Label>().Single(label => label.Text == "已刷新 6 种返程物品");
                Check(status.Visible && !status.Parent!.Controls.Cast<Control>().Any(other => other != status && other.Visible && other.Bounds.IntersectsWith(status.Bounds)),
                    "refresh result remains visible without overlapping path editing controls");
                Check(scroll.Text == returnBook && api.LastInventoryContext?.ProcessId == 712, "keep saved selection and read the selected account");
                api.InventoryItems = new[] { items[1] };
                Refresh();
                Check(scroll.Items.Cast<string>().SequenceEqual(new[] { returnOrb }) && scroll.Text == returnBook, "refresh removes stale choices but preserves configured name as editable text");
                scroll.SelectedIndex = 0;
                api.InventoryItems = Array.Empty<InventoryItemSnapshot>();
                Refresh();
                Check(scroll.Items.Count == 0 && scroll.Text == returnOrb, "empty bag clears candidates without changing selected return item");

                refresh = Find<Button>("stallRefreshScrollButton");
                scroll = Find<RoundedComboBox>("stallReturnItemCombo");
                for (Control? control = refresh.Parent; control != null; control = control.Parent)
                    if (control is TabPage page) ((TabControl)page.Parent!).SelectedTab = page;
                System.Windows.Forms.Application.DoEvents();
                api.InventoryItems = items;
                Refresh();
                Check(scroll.Items.Cast<string>().SequenceEqual(names.Order()) && scroll.Text == returnOrb,
                    "warehouse path offers the same return-only candidates while retaining its independent selection");
                scroll.Text = returnBook;
                var captured = (ScriptSettings)typeof(AccountSettingsForm).GetMethod("CaptureScriptSettings", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form,null)!;
                Check(captured.Paths.StallReturnItemName == returnBook && captured.Paths.GroceryReturnItemName == returnOrb,
                    "warehouse and grocery scroll choices are captured independently");
                api.InventoryItems = Array.Empty<InventoryItemSnapshot>();
                Refresh();
                Check(scroll.Items.Count == 0 && scroll.Text == returnBook, "warehouse refresh also preserves selection for an empty bag");
                form.Close(); completion.SetResult();
            }
            catch (Exception ex) { completion.SetException(ex); }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        return completion.Task;
    }

    public static Task UiConfigurationAndManualAsync()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                var logger = new InMemoryRoadhogLogger();
                var settings = new ScriptSettings();
                settings.Paths.GroceryStallPathName = "grocery";
                settings.Paths.GroceryReturnItemName = "伏魔殿回程卷轴";
                settings.Paths.StallReturnItemName = "仓库回程卷轴";
                settings.Maintenance.CleanupWorkflow.GroceryScheduleTimes = new() { "12:00", "19:00" };
                settings.Maintenance.CleanupWorkflow.Auction = true;
                settings.Maintenance.CleanupWorkflow.TransferGold = true;
                settings.Maintenance.CleanupWorkflow.WarehouseName = "warehouse";
                settings.Maintenance.CleanupWorkflow.WarehouseSelectionKey = "F7";
                var store = new InMemoryAccountConfigStore(new AccountConfig { AccountName = "grocery-ui", ScriptSettings = settings });
                ScriptSettings? submitted = null;
                using var form = new AccountSettingsForm("grocery-ui", new RoadhogRuntime(new FakeGameApi(), logger, new AccountRuntimeManager(logger), null!), store,
                    new InMemorySharedPathStore(new SharedPathDocument { Name = "grocery", MapId = 2, Points = new() { new() { X = 1000 } } }),
                    new InMemoryScriptProfileStore(), new RecordingFolderLauncher(), "test-paths",
                    startGroceryShop: value => { submitted = value; return Task.FromResult(OperationResult.Ok()); });
                form.ShowInTaskbar = false; form.Location = new(-32000, -32000); form.StartPosition = FormStartPosition.Manual;
                form.Show(); System.Windows.Forms.Application.DoEvents();
                T Find<T>(string name) where T : Control => (T)form.Controls.Find(name, true).Single();
                var tabs = form.Controls.OfType<TabControl>().Single();
                tabs.SelectedTab = tabs.TabPages.Cast<TabPage>().Single(p => p.Text == "清包");
                var normal = Find<RadioButton>("normalCleanupModeRadio");
                var grocery = Find<RadioButton>("groceryCleanupModeRadio");
                var card = Find<Panel>("groceryOptionsPanel");
                var warehouseToggle = Find<Button>("cleanupWorkflowOptionsButton");
                var modePanel = Find<Panel>("cleanupModePanel");
                Check(normal.Checked && !grocery.Checked && !card.Visible && warehouseToggle.Visible,
                    "legacy normal mode loads with a separate radio group and normal actions");
                Check(modePanel.Top == modePanel.Parent!.Controls.Cast<Control>().Where(c => c.Visible).Min(c => c.Top),
                    "cleanup mode selection is the first row of the page");
                var output = Environment.GetEnvironmentVariable("ROADHOG_GROCERY_PREVIEW");
                void Preview(string path)
                {
                    System.Windows.Forms.Application.DoEvents();
                    using var bitmap = new System.Drawing.Bitmap(form.Width, form.Height);
                    form.DrawToBitmap(bitmap, new(System.Drawing.Point.Empty, form.Size)); bitmap.Save(path);
                }
                if (!string.IsNullOrEmpty(output)) Preview(System.IO.Path.ChangeExtension(output, "normal.png"));
                warehouseToggle.PerformClick(); System.Windows.Forms.Application.DoEvents();
                var warehousePanel = Find<Panel>("cleanupWarehousePanel");
                Check(warehousePanel.Visible && warehousePanel.Bottom < Find<RoundedComboBox>("standaloneShopDiscount").Parent!.Top,
                    "expanding warehouse options moves rules below the detail panel");
                grocery.Checked = true;
                Check(!normal.Checked && grocery.Checked && card.Visible && !warehouseToggle.Visible && Find<RadioButton>("bagCleanupWhitelistRadio").Checked,
                    "grocery mode is exclusive without changing the independent name-list selection");
                Check(!Find<Button>("standaloneShopButton").Visible, "grocery mode presents the route-aware action rather than the standalone action");
                var list = Find<ListBox>("groceryScheduleTimes");
                var time = Find<DateTimePicker>("groceryScheduleTime");
                var add = Find<Button>("groceryScheduleAddButton");
                var remove = Find<Button>("groceryScheduleRemoveButton");
                var manual = Find<Button>("manualGroceryShopButton");
                Check(list.Items.Cast<string>().SequenceEqual(new[] { "12:00", "19:00" }) && manual.Enabled && manual.Visible, "load multiple times and temporary manual action");
                time.Value = DateTime.Today.AddHours(8); add.PerformClick();
                Check(list.Items.Cast<string>().SequenceEqual(new[] { "08:00", "12:00", "19:00" }), "add sorted daily times");
                list.SelectedIndex = 0; time.Value = DateTime.Today.AddHours(9); add.PerformClick();
                Check(list.Items[0]!.ToString() == "09:00" && list.Items.Count == 3, "edit selected time");
                list.SelectedIndex = 0; remove.PerformClick();
                Check(list.Items.Count == 2, "delete selected time");
                var scroll = Find<RoundedComboBox>("groceryReturnItemCombo");
                Check(scroll.Text == settings.Paths.GroceryReturnItemName, "load saved return scroll");
                scroll.Text = "贝达尔回程卷轴";
                var warehouseScroll = Find<RoundedComboBox>("stallReturnItemCombo");
                Check(warehouseScroll.Text == "仓库回程卷轴", "load independent warehouse scroll");
                warehouseScroll.Text = "仓库传送石";
                manual.PerformClick(); System.Windows.Forms.Application.DoEvents();
                Check(submitted is { } && submitted.Maintenance.CleanupWorkflow.Mode == CleanupMode.GroceryShop && submitted.Paths.GroceryReturnItemName == "贝达尔回程卷轴", "manual command captures current path scroll and forces grocery mode");
                var saved = store.LoadAllAsync().GetAwaiter().GetResult().Value!.Single().ScriptSettings!;
                Check(saved.Maintenance.CleanupWorkflow.Mode == CleanupMode.GroceryShop && saved.Paths.GroceryStallPathName == "grocery" && saved.Paths.GroceryReturnItemName == "贝达尔回程卷轴", "manual command retains selected mode and captures current path options");
                Check(manual.Bottom <= manual.Parent!.Height && list.Bottom < manual.Parent.Height, "new controls fit their scrolling parent");
                Find<RoundedCheckBox>("groceryScheduleEnabled").Checked = true;
                normal.Checked = true;
                var normalCapture = (ScriptSettings)typeof(AccountSettingsForm).GetMethod("CaptureScriptSettings", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form, null)!;
                Check(!card.Visible && warehouseToggle.Visible && warehousePanel.Visible && normalCapture.Maintenance.CleanupWorkflow is
                    { Mode: CleanupMode.Normal, GroceryScheduleEnabled: true, Auction: true, TransferGold: true, WarehouseName: "warehouse", WarehouseSelectionKey: "F7" } &&
                    normalCapture.Maintenance.CleanupWorkflow.GroceryScheduleTimes.SequenceEqual(new[] { "12:00", "19:00" }),
                    "switching modes preserves hidden schedule and normal workflow settings");
                warehouseToggle.PerformClick(); System.Windows.Forms.Application.DoEvents();
                grocery.Checked = true;
                var capture = (ScriptSettings)typeof(AccountSettingsForm).GetMethod("CaptureScriptSettings", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form, null)!;
                Check(capture.Maintenance.CleanupWorkflow is { Mode: CleanupMode.GroceryShop, GroceryScheduleEnabled: true } && capture.Maintenance.CleanupWorkflow.GroceryScheduleTimes.SequenceEqual(new[] { "12:00", "19:00" }), "capture grocery and daily schedule configuration");
                manual.PerformClick(); System.Windows.Forms.Application.DoEvents();
                saved = store.LoadAllAsync().GetAwaiter().GetResult().Value!.Single().ScriptSettings!;
                Check(saved.Maintenance.CleanupWorkflow.Mode == CleanupMode.GroceryShop && saved.Maintenance.CleanupWorkflow.GroceryScheduleEnabled && saved.Maintenance.CleanupWorkflow.GroceryScheduleTimes.Count == 2, "persist selected mode and recurring times");
                if (!string.IsNullOrEmpty(output))
                {
                    Preview(output);
                }
                Check(card.Bottom < Find<RoundedComboBox>("standaloneShopDiscount").Parent!.Top,
                    "schedule settings sit above cleanup rules and discount controls");
                Check(modePanel.Parent!.ClientSize.Height >= manual.Parent!.Bottom &&
                    modePanel.Parent.ClientSize.Height >= Find<RoundedComboBox>("standaloneShopDiscount").Parent!.Bottom,
                    "mode, schedule, manual action and cleanup rules fit the default viewport");
                form.ClientSize = new(720, 420); System.Windows.Forms.Application.DoEvents();
                Check(!normal.Checked && grocery.Checked && card.Visible && manual.Enabled,
                    "narrow window keeps selected mode and available manual command");
                Check(((Panel)modePanel.Parent!).AutoScroll && ((Panel)modePanel.Parent).AutoScrollMinSize.Height >= card.Bottom,
                    "small windows retain scroll access to all mode settings");
                if (!string.IsNullOrEmpty(output)) Preview(System.IO.Path.ChangeExtension(output, "narrow.png"));
                form.ClientSize = new(1000, 800); System.Windows.Forms.Application.DoEvents();
                var routeLink = Find<LinkLabel>("groceryRouteSettingsLink");
                typeof(LinkLabel).GetMethod("OnLinkClicked", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(routeLink, new object[] { new LinkLabelLinkClickedEventArgs(routeLink.Links[0]) });
                Check(tabs.SelectedTab!.Text == "路径" && Find<TabControl>("pathTabs").SelectedTab!.Text == "杂货摆摊路径",
                    "route shortcut navigates to the grocery editor without losing unsaved settings");
                form.Close();
                using var reopened = new AccountSettingsForm("grocery-ui", new RoadhogRuntime(new FakeGameApi(), logger, new AccountRuntimeManager(logger), null!), store,
                    new InMemorySharedPathStore(), new InMemoryScriptProfileStore(), new RecordingFolderLauncher(), "test-paths");
                reopened.ShowInTaskbar = false; reopened.Location = new(-32000, -32000); reopened.StartPosition = FormStartPosition.Manual;
                reopened.Show();
                var reopenedTabs = reopened.Controls.OfType<TabControl>().Single();
                reopenedTabs.SelectedTab = reopenedTabs.TabPages.Cast<TabPage>().Single(p => p.Text == "清包");
                System.Windows.Forms.Application.DoEvents();
                Check(((RadioButton)reopened.Controls.Find("groceryCleanupModeRadio", true).Single()).Checked &&
                    ((Panel)reopened.Controls.Find("groceryOptionsPanel", true).Single()).Visible &&
                    ((ListBox)reopened.Controls.Find("groceryScheduleTimes", true).Single()).Items.Cast<string>().SequenceEqual(new[] { "12:00", "19:00" }),
                    "reopening restores the saved radio mode and time list");
                var restored = (ScriptSettings)typeof(AccountSettingsForm).GetMethod("CaptureScriptSettings", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(reopened, null)!;
                Check(restored.Paths.StallReturnItemName == "仓库传送石" && restored.Paths.GroceryReturnItemName == "贝达尔回程卷轴",
                    "save and reopen preserve independent warehouse and grocery scroll selections");
                Check(restored.Maintenance.CleanupWorkflow is { Mode: CleanupMode.GroceryShop, GroceryScheduleEnabled: true, Auction: true, TransferGold: true, WarehouseName: "warehouse", WarehouseSelectionKey: "F7" },
                    "reopening retains normal workflow settings while grocery controls are visible");
                reopened.Close(); completion.SetResult();
            }
            catch (Exception ex) { completion.SetException(ex); }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        return completion.Task;
    }
}
