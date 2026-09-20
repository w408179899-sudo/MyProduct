using Roadhog.Core.Accounts;

namespace Roadhog;

public partial class AccountSettingsForm
{
    private async Task TestAuctionHouseAsync(Button button)
    {
        if (_auctionTestCts != null || _personalShopTestCts != null || _inventoryDiscardTestCts != null) return;
        if (bagCleanupNameListMutationInFlight) { SetBagCleanupInventoryStatus("名单正在保存，请稍后测试。", true); return; }
        if (bagCleanupTradeItemGrid?.IsCurrentCellInEditMode == true && !bagCleanupTradeItemGrid.EndEdit()) return;
        if (bagCleanupNameListMutationInFlight) { SetBagCleanupInventoryStatus("名单正在保存，请稍后测试。", true); return; }
        var items = BagCleanupTradeItemConfig.Normalize(bagCleanupAuctionHouseItemNames);
        using var cancellation = new CancellationTokenSource(); _auctionTestCts = cancellation;
        button.Enabled = false; button.Text = "测试中...";
        SetBagCleanupInventoryStatus("测试拍卖行：验证流程和查价，不提交出售或领取金币。", false);
        try
        {
            var pathName = GetText(auctionPathNameTextBox, string.Empty);
            string? npcName = null;
            if (!string.IsNullOrWhiteSpace(pathName))
            {
                var path = await _pathStore.LoadAsync(pathName, cancellation.Token).ConfigureAwait(true);
                if (!path.Success || path.Value == null)
                    throw new InvalidOperationException(path.Error ?? "拍卖行路径读取失败。");
                npcName = path.Value.AuctionNpcName;
            }
            var progress = new Progress<string>(text => { if (!IsDisposed && _auctionTestCts == cancellation && !cancellation.IsCancellationRequested) SetBagCleanupInventoryStatus(text, false); });
            var result = await _runtime.TestAuctionHouseAsync(_account, items, progress, cancellation.Token, npcName).ConfigureAwait(true);
            if (!IsDisposed) SetBagCleanupInventoryStatus(result.Success ? result.Value! : "测试拍卖行失败：" + result.Error, !result.Success);
        }
        catch (Exception ex) { if (!IsDisposed) SetBagCleanupInventoryStatus("测试拍卖行失败：" + ex.Message, true); }
        finally { _auctionTestCts = null; if (!button.IsDisposed) { button.Text = "测试拍卖行"; button.Enabled = true; } }
    }
}
