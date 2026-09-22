using System.Text.Json;
using Roadhog.Application.Trading;
using Roadhog.Core.Accounts;
using Roadhog.Core.Model;

internal static class AuctionDiscountTests
{
    private static void Check(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }

    public static Task PricesAsync()
    {
        var rule = new BagCleanupTradeItemConfig { Name = "item", PriceLookupMethod = AuctionPriceLookupMethod.DialogMinimum, AuctionDiscount = 9.50m };
        var item = new InventoryItemSnapshot(1, 11, "item", 99, 0, false, VendorSellUnitPrice: 20000);
        var editor = new AuctionEditor(1, 99, 0, 2000, null) { MinimumAllowedPrice = 1 };
        Check(AuctionPricePolicy.Resolve(rule, item, editor) == 1900, "above floor uses discounted unit price, never stack total");
        Check(AuctionPricePolicy.Resolve(rule, item, editor with { MarketMinimum = 1000 }) == 1000, "below floor uses system unit price divided by 20");
        Check(AuctionPricePolicy.Resolve(rule, item with { VendorSellUnitPrice = 38000 }, editor) == 1900, "exact floor stays equal");
        Check(AuctionPricePolicy.Resolve(rule, item with { VendorSellUnitPrice = 20001 }, editor with { MarketMinimum = 1000 }) == 1001, "floor rounds upward");
        Check(AuctionPricePolicy.Resolve(rule, item, editor with { MarketMinimum = 2001 }) == 1900, "discount rounds down to integer gold");
        Check(AuctionPricePolicy.Resolve(rule, item, editor with { MinimumAllowedPrice = 1950 }) == 1950, "game price floor still applies");
        Check(AuctionPricePolicy.Resolve(rule, item, editor with { MarketMinimum = null }) is null &&
            AuctionPricePolicy.Resolve(rule, item, editor with { MarketMinimum = 0 }) is null, "missing quote cannot cause cheap fallback listing");
        Check(AuctionPricePolicy.Resolve(rule, item with { VendorSellUnitPrice = 0 }, editor with { MarketMinimum = 1 }) == 1, "small positive price remains legal");
        var huge = AuctionPricePolicy.Resolve(rule, item with { VendorSellUnitPrice = ulong.MaxValue }, editor with { MarketMinimum = ulong.MaxValue });
        Check(huge == (ulong)decimal.Floor(ulong.MaxValue * 0.95m), "large values use decimal without overflow or precision loss");
        rule.AuctionDiscount = null;
        Check(AuctionPricePolicy.Resolve(rule, item, editor) == 2000, "old automatic settings remain undiscounted");
        Check(AuctionPricePolicy.Resolve(rule, item, editor with { MarketMinimum = 5, MinimumAllowedPrice = 10 }) == 5, "legacy below-floor candidate retains existing skip behavior");
        rule.PriceLookupMethod = AuctionPriceLookupMethod.Manual; rule.UnitPrice = 123; rule.AuctionDiscount = 9.5m;
        Check(AuctionPricePolicy.Resolve(rule, item, editor) == 123, "manual price ignores stored discount");
        rule.PriceLookupMethod = AuctionPriceLookupMethod.SearchCalculation;
        Check(AuctionPricePolicy.Resolve(rule, item, editor) is null, "search mode remains unsupported");
        foreach (var valid in new[] { "8.01", "9.99", "9", "9.5", " 9.50 " })
            Check(BagCleanupTradeItemConfig.TryParseDiscount(valid, out var d) && d.HasValue, "valid discount " + valid);
        foreach (var invalid in new[] { "8", "8.00", "10", "0.95", "9.999", "9.500", "NaN", "-9", "9,50", "9折" })
            Check(!BagCleanupTradeItemConfig.TryParseDiscount(invalid, out _), "invalid discount " + invalid);
        Check(BagCleanupTradeItemConfig.TryParseDiscount("", out var cleared) && cleared is null, "blank clears discount");
        return Task.CompletedTask;
    }

    public static Task CompatibilityAsync()
    {
        foreach (var json in new[] { "\"item\"", "{\"name\":\"item\",\"unitPrice\":123}", "{\"name\":\"item\",\"priceLookupMethod\":\"DialogMinimum\"}" })
            Check(JsonSerializer.Deserialize<BagCleanupTradeItemConfig>(json)!.AuctionDiscount is null, "legacy JSON never gains a discount");
        var item = new BagCleanupTradeItemConfig { Name = "item", UnitPrice = 123, PriceLookupMethod = AuctionPriceLookupMethod.DialogMinimum, AuctionDiscount = 9.5m };
        var document = new BagCleanupNameListsDocument { AuctionHouse = new() { item } };
        var copy = JsonSerializer.Deserialize<BagCleanupNameListsDocument>(JsonSerializer.Serialize(document.Clone()))!;
        Check(copy.AuctionHouse.Single().AuctionDiscount == 9.5m && copy.AuctionHouse.Single().UnitPrice == 123, "clone and JSON retain discount and unused manual price");
        copy.AuctionHouse[0].AuctionDiscount = 8.5m;
        Check(item.AuctionDiscount == 9.5m, "clone isolates original");
        var patched = SharedCleanupConfiguration.PatchPrices(document.AuctionHouse, document.AuctionHouse, copy.AuctionHouse);
        Check(patched.Single().AuctionDiscount == 8.5m, "discount-only edits survive shared configuration patch");
        copy.AuctionHouse[0].AuctionDiscount = null;
        Check(SharedCleanupConfiguration.PatchPrices(patched, patched, copy.AuctionHouse).Single().AuctionDiscount is null, "discount clearing propagates to shared store");
        var shared = new SharedCleanupConfiguration();
        shared.Merge("region-a", document, Array.Empty<string>());
        shared.Merge("region-b", copy, Array.Empty<string>());
        Check(shared.ForRegion("region-a").AuctionHouse.Single().AuctionDiscount == 9.5m &&
            shared.ForRegion("region-b").AuctionHouse.Single().AuctionDiscount is null, "region isolation retains independent discounts");
        Check(BagCleanupTradeItemConfig.Normalize(new[] { item, new BagCleanupTradeItemConfig { Name = "item" } }).Single().AuctionDiscount == 9.5m, "duplicate insert preserves existing discount");
        foreach (var value in new[] { "8.00", "10", "9.999", "\"9.50\"" })
        {
            var rejected = false;
            try { JsonSerializer.Deserialize<BagCleanupTradeItemConfig>("{\"name\":\"item\",\"auctionDiscount\":" + value + "}"); }
            catch (JsonException) { rejected = true; }
            Check(rejected, "invalid stored discount rejected " + value);
        }
        return Task.CompletedTask;
    }
}
