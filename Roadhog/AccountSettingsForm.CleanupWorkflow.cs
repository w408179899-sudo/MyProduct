using Roadhog.Core.Accounts;

namespace Roadhog;

public partial class AccountSettingsForm
{
    private RoundedCheckBox? cleanupNpc, cleanupAuction, cleanupTransfer, cleanupShop;
    private TextBox? warehouseName;
    private Button? warehouseKey;
    private CleanupWorkflowSettings loadedCleanupWorkflow = new();
    private Action? layoutCleanupPage;

    private void BuildCleanupWorkflowOptions(Panel page, Panel options, Panel rules, Panel names)
    {
        cleanupNpc = AddCheckBox(options, "出售 / 丢弃", 344, 6, 124, true);
        cleanupAuction = AddCheckBox(options, "拍卖行", 472, 6, 92, false);
        cleanupTransfer = AddCheckBox(options, "转移金币", 576, 6, 110, false);
        cleanupShop = AddCheckBox(options, "摆摊", 700, 6, 90, false);
        var hint = AddLabel(options, "", 344, 34, 480, 24);
        var toggle = AddButton(page, "▶ 仓库号配置", 12, 124, 250, 28);
        toggle.Name = "cleanupWorkflowOptionsButton";
        var detail = new Panel { Name = "cleanupWarehousePanel", Location = new Point(12, 156), Size = new Size(828, 96), Visible = false, BackColor = _inputBackground };
        page.Controls.Add(detail);
        AddLabel(detail, "仓库角色名", 8, 8, 95, 24);
        warehouseName = new TextBox { Location = new Point(105, 8), Width = 200 };
        AddLabel(detail, "选仓库号按键", 330, 8, 104, 24);
        warehouseKey = AddTeamKeyButton(detail, 440, 6, string.Empty);
        detail.Controls.Add(warehouseName);
        AddLabel(detail, "拍卖行：全部撤单 → 按配置登录物品 → 计算领取金币", 8, 42, 808, 24);
        AddLabel(detail, "仓库未到会持续等待；购买第一件物品，按现有金币和单价计算数量。", 8, 70, 808, 24);
        var warehouseExpanded = false;
        BuildGroceryShopControls(page);
        void LayoutOptions()
        {
            var grocery = groceryCleanupModeRadio!.Checked;
            cleanupNpc.Visible = cleanupAuction.Visible = cleanupTransfer.Visible = cleanupShop.Visible = !grocery;
            toggle.Visible = !grocery;
            detail.Visible = !grocery && warehouseExpanded;
            groceryOptionsPanel!.Visible = grocery;
            options.Height = grocery ? 44 : 64;
            hint.Top = grocery ? 20 : 34;
            groceryOptionsPanel.Top = options.Bottom + 8;
            cleanupModeHintLabel!.Text = grocery ? "按右侧摆摊名单和左侧折扣出售" : "丢弃 / 出售，按勾选项目清包";
            hint.Text = grocery ? "丢弃后空位仍不足才出发；定时直接出发" : "勾选需要执行的清包和交易项目";
            if (rules.Controls["standaloneShopButton"] is { } shopButton) shopButton.Visible = !grocery;
            if (rules.Controls["standaloneShopHintLabel"] is Label shopHint)
                shopHint.Text = grocery ? "杂货摆摊使用此折扣，路线在上方配置" : "仅摆摊名单物品，全部售罄后重启脚本";
            page.AutoScrollPosition = Point.Empty;
            rules.Top = names.Top = grocery ? groceryOptionsPanel.Bottom + 12 : detail.Visible ? detail.Bottom + 8 : toggle.Bottom + 8;
            page.AutoScrollMinSize = new Size(852, Math.Max(rules.Bottom, names.Top + 460) + 12);
        }
        layoutCleanupPage = LayoutOptions;
        toggle.Click += (_, _) => { warehouseExpanded = !warehouseExpanded; toggle.Text = (warehouseExpanded ? "▼" : "▶") + " 仓库号配置"; LayoutOptions(); };
        normalCleanupModeRadio!.CheckedChanged += (_, _) => { if (normalCleanupModeRadio.Checked) LayoutOptions(); };
        groceryCleanupModeRadio!.CheckedChanged += (_, _) => { if (groceryCleanupModeRadio.Checked) LayoutOptions(); };
        LayoutOptions();
    }

    private void LoadCleanupWorkflow(CleanupWorkflowSettings? value)
    {
        var s = value ?? new();
        loadedCleanupWorkflow = s.Clone();
        LoadGroceryShopControls(s);
        layoutCleanupPage?.Invoke();
        standaloneShopDiscount!.SelectedIndex = Math.Clamp(s.StandaloneShopDiscount, 4, 9) - 4;
        cleanupNpc!.Checked = s.NpcCleanup; cleanupAuction!.Checked = s.Auction;
        cleanupTransfer!.Checked = s.TransferGold; cleanupShop!.Checked = s.PersonalShop;
        warehouseName!.Text = s.WarehouseName; SetKeyButton(warehouseKey, s.WarehouseSelectionKey);
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
