namespace Roadhog.Core.Accounts;

public enum AuctionOldListingAction { Keep, Reprice, Withdraw }

public sealed class CleanupWorkflowSettings
{
    public bool NpcCleanup { get; set; } = true;
    public bool Auction { get; set; }
    public bool TransferGold { get; set; }
    public bool PersonalShop { get; set; }
    public string WarehouseName { get; set; } = string.Empty;
    public string WarehouseSelectionKey { get; set; } = string.Empty;
    // Retained for legacy JSON round-trips only; auction always withdraws every current listing.
    public AuctionOldListingAction OldListingAction { get; set; }
    public int OldListingHours { get; set; } = 24;
    public CleanupWorkflowSettings Clone() => (CleanupWorkflowSettings)MemberwiseClone();
    public CleanupWorkflowSettings ForTrigger(bool manual)
    {
        var copy = Clone();
        if (!manual) { copy.TransferGold = false; copy.PersonalShop = false; }
        return copy;
    }
    public string Describe() => string.Join(" → ", new[]
    {
        NpcCleanup ? "出售 / 丢弃清包" : null, Auction ? "拍卖行（全部撤单 → 登录物品 → 计算金币）" : null,
        TransferGold ? "转移金币" : null, PersonalShop ? "摆摊并等待售罄" : null
    }.Where(s => s != null));
}
