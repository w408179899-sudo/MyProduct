using Roadhog.Core.Accounts;

namespace Roadhog;

public partial class AccountSettingsForm
{
    private RoundedCheckBox? cleanupNpc, cleanupAuction, cleanupTransfer, cleanupShop;
    private TextBox? warehouseName;
    private Button? warehouseKey;
    private CleanupWorkflowSettings loadedCleanupWorkflow = new();

    private void BuildCleanupWorkflowOptions(Panel page, Panel options, Panel rules, Panel names)
    {
        cleanupNpc = AddCheckBox(options, "出售 / 丢弃", 344, 6, 124, true);
        cleanupAuction = AddCheckBox(options, "拍卖行", 472, 6, 92, false);
        cleanupTransfer = AddCheckBox(options, "转移金币", 576, 6, 110, false);
        cleanupShop = AddCheckBox(options, "摆摊", 700, 6, 90, false);
        AddLabel(options, "自动仅执行出售、丢弃和拍卖行；手动完成后继续挂机", 344, 34, 480, 24);
        var toggle = AddButton(page, "▶ 仓库号配置", 12, 78, 250, 28);
        toggle.Name = "cleanupWorkflowOptionsButton";
        var detail = new Panel { Location = new Point(12, 110), Size = new Size(828, 96), Visible = false, BackColor = _inputBackground };
        page.Controls.Add(detail);
        AddLabel(detail, "仓库角色名", 8, 8, 95, 24);
        warehouseName = new TextBox { Location = new Point(105, 8), Width = 200 };
        AddLabel(detail, "选仓库号按键", 330, 8, 104, 24);
        warehouseKey = AddTeamKeyButton(detail, 440, 6, string.Empty);
        detail.Controls.Add(warehouseName);
        AddLabel(detail, "拍卖行：全部撤单 → 按配置登录物品 → 计算领取金币", 8, 42, 808, 24);
        AddLabel(detail, "仓库未到会持续等待；购买第一件物品，按现有金币和单价计算数量。", 8, 70, 808, 24);
        void LayoutOptions()
        {
            rules.Top = names.Top = detail.Visible ? 214 : 112;
            page.AutoScrollMinSize = new Size(852, rules.Bottom + 12);
        }
        toggle.Click += (_, _) => { detail.Visible = !detail.Visible; toggle.Text = (detail.Visible ? "▼" : "▶") + " 仓库号配置"; LayoutOptions(); };
        LayoutOptions();
    }

    private void LoadCleanupWorkflow(CleanupWorkflowSettings? value)
    {
        var s = value ?? new();
        loadedCleanupWorkflow = s.Clone();
        cleanupNpc!.Checked = s.NpcCleanup; cleanupAuction!.Checked = s.Auction;
        cleanupTransfer!.Checked = s.TransferGold; cleanupShop!.Checked = s.PersonalShop;
        warehouseName!.Text = s.WarehouseName; SetKeyButton(warehouseKey, s.WarehouseSelectionKey);
    }
    private CleanupWorkflowSettings CaptureCleanupWorkflow()
    {
        var value = loadedCleanupWorkflow.Clone();
        value.NpcCleanup = cleanupNpc!.Checked; value.Auction = cleanupAuction!.Checked;
        value.TransferGold = cleanupTransfer!.Checked; value.PersonalShop = cleanupShop!.Checked;
        value.WarehouseName = warehouseName!.Text.Trim(); value.WarehouseSelectionKey = warehouseKey!.Tag as string ?? string.Empty;
        return value;
    }
}
