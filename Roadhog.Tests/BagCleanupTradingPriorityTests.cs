using Roadhog.Core.Accounts;
using Roadhog.Core.Model;

internal static class BagCleanupTradingPriorityTests
{
    public static Task NpcSaleBeforeStallAsync()
    {
        var settings = new MaintenanceScriptSettings
        {
            BagCleanupRules = new()
            {
                new() { Key = BagCleanupRuleCatalog.GreenEquipment, Enabled = true, Action = BagCleanupAction.Sell },
                new() { Key = BagCleanupRuleCatalog.WhiteEquipment, Enabled = true, Action = BagCleanupAction.Discard }
            },
            BagCleanupStallItems = new() { new() { Name = "黄昏", UnitPrice = 1 }, new() { Name = "保留", UnitPrice = 1 } },
            BagCleanupAuctionHouseItems = new() { new() { Name = "拍卖", UnitPrice = 10 } },
            BagCleanupDiscardItemNameKeywords = new() { "黑名单" },
            BagCleanupExcludedItemNames = new() { "白名单" }
        };
        var items = new[]
        {
            new InventoryItemSnapshot(100500348, 1, "黄昏之宝玉", 1, 0, false, 3, 2),
            new InventoryItemSnapshot(101300336, 2, "黄昏之枪", 1, 1, false, 3, 2),
            new InventoryItemSnapshot(115000364, 3, "黄昏之盾", 1, 2, false, 6, 2),
            new InventoryItemSnapshot(115000365, 4, "黄昏拍卖盾", 1, 3, false, 6, 2),
            new InventoryItemSnapshot(115000366, 5, "黄昏黑名单盾", 1, 4, false, 6, 2),
            new InventoryItemSnapshot(115000367, 6, "黄昏白名单黑名单盾", 1, 5, false, 6, 2),
            new InventoryItemSnapshot(115000368, 7, "保留白色盾", 1, 6, false, 6, 1),
            new InventoryItemSnapshot(115000369, 8, "保留蓝色盾", 1, 7, false, 6, 3),
            new InventoryItemSnapshot(115000370, 9, "黄昏拍卖黑名单盾", 1, 8, false, 6, 2)
        };
        var sold = BagCleanupItemMatcher.SelectSellRegistrationItems(items, settings);
        Require(sold.Select(i => i.InstanceId).SequenceEqual(new ulong[] { 1, 2, 3, 4, 6 }),
            "NPC sell rules own stall overlaps, including type 3 weapons; whitelist only prevents discard");
        var discarded = BagCleanupItemMatcher.SelectDiscardItems(items, settings);
        Require(discarded.Select(i => i.InstanceId).SequenceEqual(new ulong[] { 5, 7, 9 }),
            "discard rules precede sale auction and stall, while whitelist prevents discard");
        var remaining = items.Except(sold).Except(discarded).ToArray();
        Require(remaining.Where(i => CleanupTradePolicy.Rule(i, settings, false) != null)
                .Select(i => i.InstanceId).SequenceEqual(new ulong[] { 8 }),
            "only remaining items enter the subsequent stall plan");
        settings.BagCleanupRules.Single(r => r.Key == BagCleanupRuleCatalog.GreenEquipment).Enabled = false;
        Require(BagCleanupItemMatcher.SelectSellRegistrationItems(items, settings).Count == 0,
            "stall membership never enables an unconfigured NPC sale");
        return Task.CompletedTask;
    }

    private static void Require(bool value, string message) { if (!value) throw new Exception(message); }
}
