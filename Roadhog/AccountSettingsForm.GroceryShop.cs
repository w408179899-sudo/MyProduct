using System.Globalization;
using Roadhog.Core.Accounts;
using Roadhog.Core.Common;
using Roadhog.Core.Paths;

namespace Roadhog;

public partial class AccountSettingsForm
{
    // 18 是卷轴大类；静态使用组 36 才是返程，疾走/狂风等增益卷轴是组 31。
    private const uint GroceryScrollItemType = 18;
    private const uint GroceryReturnUseGroup = 36;
    private readonly Func<ScriptSettings, Task<OperationResult>>? _startGroceryShop;
    private RoundedTextBox? groceryStallPathNameTextBox;
    private RoundedComboBox? groceryReturnItemCombo;
    private RoundedComboBox? stallReturnItemCombo;
    private RadioButton? normalCleanupModeRadio, groceryCleanupModeRadio;
    private Panel? groceryOptionsPanel;
    private Label? cleanupModeHintLabel;
    private RoundedCheckBox? groceryScheduleEnabled;
    private ListBox? groceryScheduleTimes;
    private DateTimePicker? groceryScheduleTime;

    private void BuildGroceryPathOptions(Panel page) =>
        groceryReturnItemCombo = BuildReturnScrollPathOptions(page, SharedPathKind.GroceryStall,
            "groceryReturnItemCombo", "groceryRefreshScrollButton");

    private void BuildWarehousePathOptions(Panel page) =>
        stallReturnItemCombo = BuildReturnScrollPathOptions(page, SharedPathKind.Stall,
            "stallReturnItemCombo", "stallRefreshScrollButton");

    private RoundedComboBox BuildReturnScrollPathOptions(Panel page, SharedPathKind kind, string comboName, string buttonName)
    {
        var options = new Panel { Location = new Point(12, 110), Size = new Size(800, 36), BackColor = _softGreen };
        page.Controls.Add(options);
        AddLabel(options, "回程卷轴", 8, 6, 90, 24);
        var scrollCombo = AddCombo(options, 102, 3, 408, 28);
        scrollCombo.Name = comboName;
        scrollCombo.DropDownStyle = ComboBoxStyle.DropDown;
        var refresh = AddButton(options, "刷新背包卷轴", 520, 2, 126, 30);
        refresh.Name = buttonName;
        refresh.Click += async (_, _) =>
        {
            refresh.Enabled = false;
            var editor = pathEditors[kind];
            SetPathStatus(editor, "正在刷新背包返程物品…", false);
            try
            {
                var selected = scrollCombo.Text;
                var items = await _runtime.RefreshInventoryAsync(_account);
                if (IsDisposed) return;
                var names = items.Where(i => !i.IsEquipped && i.Count > 0 && i.Slot >= 0 &&
                    i.ItemType == GroceryScrollItemType && i.UseGroup == GroceryReturnUseGroup && !string.IsNullOrWhiteSpace(i.Name))
                    .Select(i => i.Name).Distinct().Order().ToArray();
                scrollCombo.Items.Clear();
                scrollCombo.Items.AddRange(names);
                SetComboText(scrollCombo, selected);
                SetPathStatus(editor, names.Length == 0 ? "背包中没有返程物品"
                    : $"已刷新 {names.Length} 种返程物品", false);
            }
            catch (Exception ex) { if (!IsDisposed) SetPathStatus(editor, "刷新卷轴失败：" + ex.Message, true); }
            finally { if (!refresh.IsDisposed) refresh.Enabled = true; }
        };
        AddLabel(options, "缺少所选卷轴不出发", 652, 7, 145, 22);
        return scrollCombo;
    }

    private void BuildGroceryShopControls(Panel page)
    {
        var modePanel = new Panel
        {
            Name = "cleanupModePanel", Location = new Point(12, 10),
            Size = new Size(828, 36), BackColor = _inputBackground
        };
        page.Controls.Add(modePanel);
        AddLabel(modePanel, "清包方式", 8, 5, 82, 26, _textGreen, FontStyle.Bold);
        normalCleanupModeRadio = AddRadioButton(modePanel, "正常清包", 100, 5, 116, true);
        normalCleanupModeRadio.Name = "normalCleanupModeRadio";
        groceryCleanupModeRadio = AddRadioButton(modePanel, "杂货摆摊", 236, 5, 132, false);
        groceryCleanupModeRadio.Name = "groceryCleanupModeRadio";
        normalCleanupModeRadio.BackColor = groceryCleanupModeRadio.BackColor = modePanel.BackColor;
        cleanupModeHintLabel = AddLabel(modePanel, "", 392, 5, 424, 26);

        groceryOptionsPanel = new Panel
        {
            Name = "groceryOptionsPanel", Location = new Point(12, 124),
            Size = new Size(828, 104), BackColor = _softGreen, Visible = false
        };
        page.Controls.Add(groceryOptionsPanel);
        var manual = AddButton(groceryOptionsPanel, "手动杂货摆摊", 420, 2, 144, 30);
        manual.Name = "manualGroceryShopButton";
        manual.Enabled = _startGroceryShop != null;
        manual.Click += async (_, _) => await StartManualGroceryShopAsync(manual);
        groceryScheduleEnabled = AddCheckBox(groceryOptionsPanel, "定时杂货摆摊", 8, 4, 152, false);
        groceryScheduleEnabled.Name = "groceryScheduleEnabled";
        groceryScheduleEnabled.BackColor = groceryOptionsPanel.BackColor;
        AddLabel(groceryOptionsPanel, "北京时间，每天按列表执行", 168, 5, 242, 26);
        AddLabel(groceryOptionsPanel, "每日时间", 8, 42, 76, 26);
        groceryScheduleTime = new DateTimePicker
        {
            Name = "groceryScheduleTime", Location = new Point(84, 40), Size = new Size(78, 28),
            Format = DateTimePickerFormat.Custom, CustomFormat = "HH:mm", ShowUpDown = true,
            Value = DateTime.Today.AddHours(12), Font = new Font("Microsoft YaHei UI", 9F)
        };
        groceryOptionsPanel.Controls.Add(groceryScheduleTime);
        var add = AddButton(groceryOptionsPanel, "添加 / 修改", 174, 38, 104, 30);
        add.Name = "groceryScheduleAddButton";
        var remove = AddButton(groceryOptionsPanel, "删除选中", 290, 38, 104, 30);
        remove.Name = "groceryScheduleRemoveButton";
        groceryScheduleTimes = new ListBox
        {
            Name = "groceryScheduleTimes", Location = new Point(580, 6), Size = new Size(236, 92),
            Font = new Font("Microsoft YaHei UI", 9F), ForeColor = _textGreen,
            BackColor = Color.White, BorderStyle = BorderStyle.FixedSingle, IntegralHeight = false
        };
        groceryOptionsPanel.Controls.Add(groceryScheduleTimes);
        groceryScheduleTimes.SelectedIndexChanged += (_, _) =>
        {
            if (groceryScheduleTimes.SelectedItem is string time)
                groceryScheduleTime.Value = DateTime.Today.Add(TimeOnly.ParseExact(time, "HH:mm", CultureInfo.InvariantCulture).ToTimeSpan());
        };
        add.Click += (_, _) =>
        {
            var time = groceryScheduleTime.Value.ToString("HH:mm", CultureInfo.InvariantCulture);
            var values = groceryScheduleTimes.Items.Cast<string>().ToList();
            if (groceryScheduleTimes.SelectedIndex >= 0) values.RemoveAt(groceryScheduleTimes.SelectedIndex);
            values.Add(time);
            groceryScheduleTimes.Items.Clear();
            groceryScheduleTimes.Items.AddRange(GroceryShopSchedule.Normalize(values).Cast<object>().ToArray());
        };
        remove.Click += (_, _) => { if (groceryScheduleTimes.SelectedIndex >= 0) groceryScheduleTimes.Items.RemoveAt(groceryScheduleTimes.SelectedIndex); };
        var routeLink = new LinkLabel
        {
            Name = "groceryRouteSettingsLink", Text = "回程 / 路线设置", Location = new Point(420, 41),
            Size = new Size(144, 26), Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold),
            LinkColor = _textGreen, ActiveLinkColor = _textGreen, TextAlign = ContentAlignment.MiddleCenter
        };
        groceryOptionsPanel.Controls.Add(routeLink);
        routeLink.LinkClicked += (_, _) =>
        {
            settingsTabs.SelectedTab = settingsTabs.TabPages.Cast<TabPage>().Single(p => p.Text == "路径");
            var pathTabs = (TabControl)Controls.Find("pathTabs", true).Single();
            pathTabs.SelectedTab = pathTabs.TabPages.Cast<TabPage>().Single(p => p.Text == "杂货摆摊路径");
        };
        AddLabel(groceryOptionsPanel, "回程 → 路线摆摊 → 全部售罄 → 丢弃 → 重启", 8, 76, 556, 22);
    }

    private void LoadGroceryShopControls(CleanupWorkflowSettings settings)
    {
        if (settings.Mode == CleanupMode.GroceryShop) groceryCleanupModeRadio!.Checked = true;
        else normalCleanupModeRadio!.Checked = true;
        groceryScheduleEnabled!.Checked = settings.GroceryScheduleEnabled;
        groceryScheduleTimes!.Items.Clear();
        groceryScheduleTimes.Items.AddRange(GroceryShopSchedule.Normalize(settings.GroceryScheduleTimes).Cast<object>().ToArray());
    }

    private void CaptureGroceryShopControls(CleanupWorkflowSettings settings)
    {
        settings.Mode = groceryCleanupModeRadio!.Checked ? CleanupMode.GroceryShop : CleanupMode.Normal;
        settings.GroceryScheduleEnabled = groceryScheduleEnabled!.Checked;
        settings.GroceryScheduleTimes = groceryScheduleTimes!.Items.Cast<string>().ToList();
    }

    private async Task StartManualGroceryShopAsync(Button button)
    {
        if (_startGroceryShop == null) return;
        if (_personalShopTestCts != null || _auctionTestCts != null || _inventoryDiscardTestCts != null)
        { SetBagCleanupInventoryStatus("手动测试正在进行，请等待完成。", true); return; }
        button.Enabled = false;
        try
        {
            if (!SaveCurrentSettings(out var error)) { SetBagCleanupInventoryStatus(error, true); return; }
            var settings = LoadAccountConfigOrDefault().ScriptSettings!.Clone();
            settings.Maintenance.CleanupWorkflow.Mode = CleanupMode.GroceryShop;
            var result = await _startGroceryShop(settings);
            if (!IsDisposed) SetBagCleanupInventoryStatus(result.Success
                ? "手动杂货摆摊已提交，售罄后丢弃并重启。"
                : "无法启动杂货摆摊：" + result.Error, !result.Success);
        }
        catch (Exception ex) { if (!IsDisposed) SetBagCleanupInventoryStatus("无法启动杂货摆摊：" + ex.Message, true); }
        finally { if (!button.IsDisposed) button.Enabled = true; }
    }
}
