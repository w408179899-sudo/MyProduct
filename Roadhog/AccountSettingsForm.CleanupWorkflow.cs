using Roadhog.Core.Accounts;

namespace Roadhog;

public partial class AccountSettingsForm
{
    private RoundedCheckBox? cleanupNpc, cleanupAuction, cleanupTransfer, cleanupShop;
    private TextBox? warehouseName;
    private Button? warehouseKey;
    private CleanupWorkflowSettings loadedCleanupWorkflow = new();
    private Action? layoutCleanupPage;
    internal Func<string, string, bool>? ConfirmCleanupExecutionForTesting { get; set; }

    private void BuildCleanupExecutionMenu(Panel parent)
    {
        var tools = AddButton(parent, "执行工具 ▾", 298, 0, 106, 28);
        tools.Name = "cleanupExecutionTools";
        var menu = new ContextMenuStrip { Name = "cleanupExecutionMenu" };
        tools.ContextMenuStrip = menu;
        void Add(string label, string description, Func<Button, Task> execute)
        {
            menu.Items.Add(label, null, async (_, _) =>
            {
                if (ConfirmCleanupExecution(label, description))
                    await execute(tools).ConfigureAwait(true);
            });
        }
        Add("检查拍卖行", "会操作本账号的游戏界面，检查拍卖行流程和价格。", TestAuctionHouseAsync);
        Add("执行摆摊", "按当前出售规则整叠登记，每件 1 金币。", TestPersonalShopAsync);
        Add("执行丢弃", "按当前丢弃规则整叠丢弃，最多 3 项。丢弃后无法撤销。", TestInventoryDiscardAsync);
        tools.Click += (_, _) => menu.Show(tools, new Point(0, tools.Height));
        tools.Disposed += (_, _) => menu.Dispose();
    }

    private bool ConfirmCleanupExecution(string title, string description) =>
        ConfirmCleanupExecutionForTesting?.Invoke(title, description) ??
        (MessageBox.Show(this, $"账号 {_account}\n{description}\n继续执行？", title,
            MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) == DialogResult.Yes);

    private void BuildCleanupWorkflowOptions(Panel page, Panel options, Panel rules, Panel names)
    {
        var methodLabel = AddLabel(options, "处理方式", 12, 44, 84, 24, _textGreen, FontStyle.Bold);
        cleanupNpc = AddCheckBox(options, "出售 / 丢弃", 102, 43, 124, true);
        cleanupAuction = AddCheckBox(options, "拍卖行", 236, 43, 92, false);
        cleanupTransfer = AddCheckBox(options, "倒货", 338, 43, 94, false);
        cleanupShop = AddCheckBox(options, "摆摊", 442, 43, 90, false);
        var auctionPath = AddButton(options, "拍卖行路径", 594, 41, 120, 28, (_, _) => OpenCleanupPathSettings("拍卖行路径"));
        bagCleanupReturnWhenFullCheckBox = AddCheckBox(options, "包满时回城清包", 344, 7, 160, false);
        bagCleanupReturnWhenFullCheckBox.Name = "bagCleanupReturnWhenFullCheckBox";
        var hint = AddLabel(options, "", 516, 8, 300, 24);
        var toggle = AddButton(page, "▶ 仓库号配置", 12, 116, 250, 28);
        toggle.Name = "cleanupWorkflowOptionsButton";
        var detail = new Panel { Name = "cleanupWarehousePanel", Location = new Point(12, 148), Size = new Size(828, 88), Visible = false, BackColor = _inputBackground };
        page.Controls.Add(detail);
        AddLabel(detail, "仓库角色名", 8, 8, 95, 24);
        warehouseName = new TextBox { Location = new Point(105, 8), Width = 200 };
        AddLabel(detail, "选仓库号按键", 330, 8, 104, 24);
        warehouseKey = AddTeamKeyButton(detail, 440, 6, string.Empty);
        detail.Controls.Add(warehouseName);
        AddLabel(detail, "拍卖行：全部撤单 → 按配置登录物品 → 计算领取金币", 8, 38, 808, 22);
        AddLabel(detail, "仓库未到会持续等待；购买第一件物品，按现有金币和单价计算数量。", 8, 62, 808, 22);
        var warehouseExpanded = false;
        BuildGroceryShopControls(page);
        void LayoutOptions()
        {
            var grocery = groceryCleanupModeRadio!.Checked;
            cleanupNpc.Visible = cleanupAuction.Visible = cleanupTransfer.Visible = cleanupShop.Visible = !grocery;
            methodLabel.Visible = !grocery;
            auctionPath.Visible = !grocery && cleanupAuction.Checked;
            toggle.Visible = !grocery;
            detail.Visible = !grocery && warehouseExpanded;
            groceryOptionsPanel!.Visible = grocery;
            options.Height = grocery ? 44 : 76;
            toggle.Top = options.Bottom + 4;
            detail.Top = toggle.Bottom + 4;
            groceryOptionsPanel.Top = options.Bottom + 4;
            cleanupModeHintLabel!.Text = grocery ? "按右侧摆摊名单和左侧折扣出售" : "丢弃 / 出售，按勾选项目清包";
            hint.Text = grocery ? "不勾选仅定时出发；原地丢弃照常" : "勾选需要执行的清包和交易项目";
            if (rules.Controls["standaloneShopButton"] is { } shopButton) shopButton.Visible = !grocery;
            if (rules.Controls["standaloneShopHintLabel"] is Label shopHint)
                shopHint.Text = grocery ? "杂货摆摊使用此折扣，路线在上方配置" : "仅摆摊名单物品，全部售罄后重启脚本";
            page.AutoScrollPosition = Point.Empty;
            var contentTop = (grocery ? groceryOptionsPanel.Bottom : warehouseExpanded ? detail.Bottom : toggle.Bottom) + 6;
            page.AutoScrollMinSize = new Size(852, contentTop + Math.Max(rules.Height, CleanupNamesMinimumHeight) + 12);
            rules.Top = names.Top = contentTop;
        }
        layoutCleanupPage = LayoutOptions;
        toggle.Click += (_, _) => { warehouseExpanded = !warehouseExpanded; toggle.Text = (warehouseExpanded ? "▼" : "▶") + " 仓库号配置"; LayoutOptions(); };
        normalCleanupModeRadio!.CheckedChanged += (_, _) => { if (normalCleanupModeRadio.Checked) LayoutOptions(); };
        groceryCleanupModeRadio!.CheckedChanged += (_, _) => { if (groceryCleanupModeRadio.Checked) LayoutOptions(); };
        cleanupAuction.Click += (_, _) => LayoutOptions();
        LayoutOptions();
    }

    private void LoadCleanupWorkflow(CleanupWorkflowSettings? value)
    {
        var s = value ?? new();
        loadedCleanupWorkflow = s.Clone();
        LoadGroceryShopControls(s);
        standaloneShopDiscount!.SelectedIndex = Math.Clamp(s.StandaloneShopDiscount, 4, 9) - 4;
        cleanupNpc!.Checked = s.NpcCleanup; cleanupAuction!.Checked = s.Auction;
        cleanupTransfer!.Checked = s.TransferGold; cleanupShop!.Checked = s.PersonalShop;
        warehouseName!.Text = s.WarehouseName; SetKeyButton(warehouseKey, s.WarehouseSelectionKey);
        layoutCleanupPage?.Invoke();
    }
    private CleanupWorkflowSettings CaptureCleanupWorkflow()
    {
        var value = loadedCleanupWorkflow.Clone();
        CaptureGroceryShopControls(value);
        value.StandaloneShopDiscount = standaloneShopDiscount!.SelectedIndex + 4;
        value.NpcCleanup = cleanupNpc!.Checked; value.Auction = cleanupAuction!.Checked;
        value.TransferGold = cleanupTransfer!.Checked; value.PersonalShop = cleanupShop!.Checked;
        value.WarehouseName = warehouseName!.Text.Trim(); value.WarehouseSelectionKey = warehouseKey!.Tag as string ?? string.Empty;
        return value;
    }
}
