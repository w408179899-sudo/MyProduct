using Roadhog.Core.Accounts;

namespace Roadhog;

public partial class AccountSettingsForm
{
    private RoundedCheckBox? cleanupNpc, cleanupAuction, cleanupTransfer, cleanupShop;
    private TextBox? warehouseName;
    private Button? warehouseKey;
    private ComboBox? oldListingAction;
    private NumericUpDown? oldListingHours;

    private void BuildCleanupWorkflowOptions(Panel page, Panel options, Panel rules, Panel names)
    {
        cleanupNpc = AddCheckBox(options, "出售 / 丢弃", 344, 6, 124, true);
        cleanupAuction = AddCheckBox(options, "拍卖行", 472, 6, 92, false);
        cleanupTransfer = AddCheckBox(options, "转移金币", 576, 6, 110, false);
        cleanupShop = AddCheckBox(options, "摆摊", 700, 6, 90, false);
        AddLabel(options, "自动仅执行出售、丢弃和拍卖行；手动完成后继续挂机", 344, 34, 480, 24);
        var toggle = AddButton(page, "▶ 仓库号与旧挂售配置", 12, 78, 250, 28);
        toggle.Name = "cleanupWorkflowOptionsButton";
        var detail = new Panel { Location = new Point(12, 110), Size = new Size(828, 96), Visible = false, BackColor = _inputBackground };
        page.Controls.Add(detail);
        AddLabel(detail, "仓库角色名", 8, 8, 95, 24);
        warehouseName = new TextBox { Location = new Point(105, 8), Width = 200 };
        AddLabel(detail, "选仓库号按键", 330, 8, 104, 24);
        warehouseKey = AddTeamKeyButton(detail, 440, 6, string.Empty);
        detail.Controls.Add(warehouseName);
        AddLabel(detail, "旧挂售处理", 8, 42, 95, 24);
        oldListingAction = new ComboBox { Location = new Point(105, 42), Width = 150, DropDownStyle = ComboBoxStyle.DropDownList };
        oldListingAction.Items.AddRange(new object[] { "保持原挂售", "取回并重新定价", "取回，不再上架" });
        oldListingAction.SelectedIndex = 0;
        detail.Controls.Add(oldListingAction);
        AddLabel(detail, "挂售满", 270, 42, 65, 24);
        oldListingHours = new NumericUpDown { Location = new Point(336, 42), Width = 65, Minimum = 1, Maximum = 168, Value = 24 };
        detail.Controls.Add(oldListingHours);
        AddLabel(detail, "小时（仅处理脚本记录时间的挂售）", 407, 42, 395, 24);
        AddLabel(detail, "仓库未到会持续等待；购买第一件物品，按现有金币和单价计算数量。", 8, 70, 808, 24);
        void LayoutOptions()
        {
            rules.Top = names.Top = detail.Visible ? 214 : 112;
            page.AutoScrollMinSize = new Size(852, rules.Bottom + 12);
        }
        toggle.Click += (_, _) => { detail.Visible = !detail.Visible; toggle.Text = (detail.Visible ? "▼" : "▶") + " 仓库号与旧挂售配置"; LayoutOptions(); };
        LayoutOptions();
    }

    private void LoadCleanupWorkflow(CleanupWorkflowSettings? value)
    {
        var s = value ?? new();
        cleanupNpc!.Checked = s.NpcCleanup; cleanupAuction!.Checked = s.Auction;
        cleanupTransfer!.Checked = s.TransferGold; cleanupShop!.Checked = s.PersonalShop;
        warehouseName!.Text = s.WarehouseName; SetKeyButton(warehouseKey, s.WarehouseSelectionKey);
        oldListingAction!.SelectedIndex = (int)s.OldListingAction;
        oldListingHours!.Value = Math.Clamp(s.OldListingHours, 1, 168);
    }
    private CleanupWorkflowSettings CaptureCleanupWorkflow() => new()
    {
        NpcCleanup = cleanupNpc!.Checked, Auction = cleanupAuction!.Checked,
        TransferGold = cleanupTransfer!.Checked, PersonalShop = cleanupShop!.Checked,
        WarehouseName = warehouseName!.Text.Trim(), WarehouseSelectionKey = warehouseKey!.Tag as string ?? string.Empty,
        OldListingAction = (AuctionOldListingAction)oldListingAction!.SelectedIndex,
        OldListingHours = (int)oldListingHours!.Value
    };
}
