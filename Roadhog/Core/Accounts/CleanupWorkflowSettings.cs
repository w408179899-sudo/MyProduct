namespace Roadhog.Core.Accounts;

public enum AuctionOldListingAction { Keep, Reprice, Withdraw }
public enum CleanupMode { Normal, GroceryShop }

public sealed class CleanupWorkflowSettings
{
    public CleanupMode Mode { get; set; }
    public bool GroceryScheduleEnabled { get; set; }
    public List<string> GroceryScheduleTimes { get; set; } = new();
    public bool NpcCleanup { get; set; } = true;
    public bool Auction { get; set; }
    public bool TransferGold { get; set; }
    public bool PersonalShop { get; set; }
    public int StandaloneShopDiscount { get; set; } = 5;
    public string WarehouseName { get; set; } = string.Empty;
    public string WarehouseSelectionKey { get; set; } = string.Empty;
    // Retained for legacy JSON round-trips only; auction always withdraws every current listing.
    public AuctionOldListingAction OldListingAction { get; set; }
    public int OldListingHours { get; set; } = 24;
    public CleanupWorkflowSettings Clone()
    {
        var copy = (CleanupWorkflowSettings)MemberwiseClone();
        copy.GroceryScheduleTimes = GroceryScheduleTimes?.ToList() ?? new();
        return copy;
    }
    public CleanupWorkflowSettings ForTrigger(bool manual)
    {
        var copy = Clone();
        if (!manual) { copy.Auction = false; copy.TransferGold = false; copy.PersonalShop = false; }
        return copy;
    }
    public string Describe() => Mode == CleanupMode.GroceryShop ? "丢弃 → 卷轴回程 → 杂货摆摊" : string.Join(" → ", new[]
    {
        NpcCleanup ? "出售 / 丢弃清包" : null, Auction ? "拍卖行（全部撤单 → 登录物品 → 计算金币）" : null,
        TransferGold ? "转移金币" : null, PersonalShop ? "摆摊并等待售罄" : null
    }.Where(s => s != null));
}
