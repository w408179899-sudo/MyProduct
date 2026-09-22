using Roadhog.Core.Accounts;
using Roadhog.Core.Model;

namespace Roadhog.Application.Trading;

public static class AuctionPricePolicy
{
    public static ulong? Resolve(BagCleanupTradeItemConfig rule, InventoryItemSnapshot item, AuctionEditor editor)
    {
        if (rule.PriceLookupMethod == AuctionPriceLookupMethod.Manual)
            return rule.EffectiveUnitPrice is > 0 ? checked((ulong)rule.EffectiveUnitPrice.Value) : null;
        if (rule.PriceLookupMethod != AuctionPriceLookupMethod.DialogMinimum || editor.MarketMinimum is not > 0)
            return null;
        if (!BagCleanupTradeItemConfig.IsValidDiscount(rule.AuctionDiscount))
            throw new ArgumentException("拍卖折扣必须为 8.01～9.99 折，最多两位小数。");
        if (rule.AuctionDiscount is not { } discount) return editor.MarketMinimum;

        var floor = item.VendorSellUnitPrice / 20 + (item.VendorSellUnitPrice % 20 == 0 ? 0UL : 1UL);
        floor = Math.Max(1UL, Math.Max(floor, editor.MinimumAllowedPrice));
        var discounted = checked((ulong)decimal.Floor(editor.MarketMinimum.Value * discount / 10m));
        return Math.Max(floor, discounted);
    }
}
