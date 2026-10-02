using Roadhog.Core.Accounts;
using Roadhog.Core.Common;

namespace Roadhog;

public partial class AccountSettingsForm
{
    private readonly Func<ScriptSettings, Task<OperationResult>>? _startStandaloneShop;
    private RoundedComboBox? standaloneShopDiscount;

    private void BuildStandaloneShopControls(Panel rules)
    {
        rules.Height = Math.Max(rules.Height, 552);
        AddLabel(rules, "摆摊折扣", 12, 490, 80, 28);
        standaloneShopDiscount = AddCombo(rules, 94, 490, 80, 28, "4 折", "5 折", "6 折", "7 折", "8 折", "9 折");
        standaloneShopDiscount.Name = "standaloneShopDiscount";
        standaloneShopDiscount.SelectedIndex = 1;
        var button = AddButton(rules, "自动摆摊", 190, 488, 120, 30);
        button.Name = "standaloneShopButton";
        button.Enabled = _startStandaloneShop != null;
        button.Click += async (_, _) => await StartStandaloneShopAsync(button);
        AddLabel(rules, "仅摆摊过滤内物品；全部售罄后自动挂机", 12, 522, 380, 26);
    }

    private async Task StartStandaloneShopAsync(Button button)
    {
        if (_startStandaloneShop == null) return;
        if (_personalShopTestCts != null || _auctionTestCts != null || _inventoryDiscardTestCts != null)
        {
            SetBagCleanupInventoryStatus("手动测试正在进行，请等待完成。", true);
            return;
        }
        button.Enabled = false;
        try
        {
            if (!SaveCurrentSettings(out var error))
            {
                SetBagCleanupInventoryStatus(error, true);
                return;
            }
            var settings = LoadAccountConfigOrDefault().ScriptSettings!.Clone();
            var result = await _startStandaloneShop(settings);
            if (!IsDisposed) SetBagCleanupInventoryStatus(result.Success
                ? "自动摆摊任务已提交，全部售罄后挂机；停止账号可取消。"
                : "无法启动自动摆摊：" + result.Error, !result.Success);
        }
        catch (Exception ex) { if (!IsDisposed) SetBagCleanupInventoryStatus("无法启动自动摆摊：" + ex.Message, true); }
        finally { if (!button.IsDisposed) button.Enabled = true; }
    }
}
