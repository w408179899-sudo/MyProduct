using Roadhog.Core.Accounts;

namespace Roadhog;

public sealed partial class AccountSettingsForm
{
    private void LoadRegion(AccountConfig account)
    {
        if (regionCombo is null) return;
        loadingRegion = true;
        try
        {
            var region = string.IsNullOrWhiteSpace(account.Region) ? "未分区" : account.Region.Trim();
            if (!regionCombo.Items.Contains(region)) regionCombo.Items.Add(region);
            regionCombo.SelectedItem = region;
            if (_bagCleanupNameListStore is ISharedAccountConfiguration shared) shared.Region = region;
        }
        finally { loadingRegion = false; }
    }

    private void ChangeRegion()
    {
        if (loadingRegion || regionCombo is null || _bagCleanupNameListStore is not ISharedAccountConfiguration shared) return;
        var previous = shared.Region;
        shared.Region = regionCombo.Text;
        var loaded = shared.LoadAsync().GetAwaiter().GetResult();
        if (!loaded.Success || loaded.Value?.Document is not { } document)
        {
            shared.Region = previous;
            loadingRegion = true;
            try { regionCombo.SelectedItem = previous; }
            finally { loadingRegion = false; }
            SetBagCleanupInventoryStatus("区服配置读取失败：" + loaded.Error, true);
            return;
        }
        PopulateBagCleanupNameLists(document);
        SetBagCleanupInventoryStatus("当前编辑 " + shared.Region + " 拍卖行；公共名单仍为全账号共享。", false);
    }

    private void SaveSharedMonsterFilters(List<string> before)
    {
        if (_bagCleanupNameListStore is not ISharedAccountConfiguration shared) return;
        var result = shared.SaveMonsterFiltersAsync(before, CaptureActiveMonsterFilterList()).GetAwaiter().GetResult();
        if (!result.Success) PopulateActiveMonsterFilterList(before);
        SetActiveMonsterFilterStatus(result.Success ? "已自动保存，全账号共享" : "保存失败，已恢复：" + result.Error, !result.Success);
    }
}
