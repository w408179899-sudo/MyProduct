namespace Roadhog.Core.Accounts;

/// <summary>One public set of lists, with auction lists isolated by region.</summary>
public sealed class SharedCleanupConfiguration
{
    public int Version { get; set; } = 1;
    public BagCleanupNameListsDocument Common { get; set; } = new();
    public List<string> MonsterFilters { get; set; } = new();
    public Dictionary<string, List<BagCleanupTradeItemConfig>> Auctions { get; set; } = new();
    public List<string> MigratedAccounts { get; set; } = new();

    public BagCleanupNameListsDocument ForRegion(string region)
    {
        var result = Common.Clone();
        result.AuctionHouse = BagCleanupTradeItemConfig.Normalize(Auctions.GetValueOrDefault(region));
        return result;
    }

    public void Merge(string region, BagCleanupNameListsDocument lists, IEnumerable<string> monsters)
    {
        Common.Whitelist = Union(Common.Whitelist, lists.Whitelist);
        Common.Blacklist = Union(Common.Blacklist, lists.Blacklist);
        Common.Sell = Union(Common.Sell, lists.Sell);
        Common.Stall = MergePrices(Common.Stall.Concat(lists.Stall));
        MonsterFilters = Union(MonsterFilters, monsters);
        Auctions[region] = MergePrices((Auctions.GetValueOrDefault(region) ?? new()).Concat(lists.AuctionHouse));
    }

    public static List<string> Union(IEnumerable<string> first, IEnumerable<string> second) =>
        BagCleanupNameListsDocument.NormalizeKeywords(first.Concat(second));

    public static List<BagCleanupTradeItemConfig> MergePrices(IEnumerable<BagCleanupTradeItemConfig> items) =>
        items.Where(i => !string.IsNullOrWhiteSpace(i.Name)).GroupBy(i => i.Name.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(g => BagCleanupTradeItemConfig.Normalize(new[] { g
                .OrderByDescending(i => i.PriceLookupMethod == AuctionPriceLookupMethod.DialogMinimum ? 2 :
                    i.PriceLookupMethod == AuctionPriceLookupMethod.SearchCalculation ? 1 : 0)
                .ThenByDescending(i => i.UnitPrice ?? 0).First() }).Single()).ToList();

    // Apply edits relative to the editor's original view, preserving concurrent changes
    // to other entries and lists. An explicit edit to the same entry wins last.
    public static List<string> PatchNames(IEnumerable<string> current, IEnumerable<string> before, IEnumerable<string> after)
    {
        var old = BagCleanupNameListsDocument.NormalizeKeywords(before);
        var next = BagCleanupNameListsDocument.NormalizeKeywords(after);
        var removed = old.Except(next, StringComparer.OrdinalIgnoreCase).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return Union(current.Where(n => !removed.Contains(n)), next.Except(old, StringComparer.OrdinalIgnoreCase));
    }

    public static List<BagCleanupTradeItemConfig> PatchPrices(IEnumerable<BagCleanupTradeItemConfig> current,
        IEnumerable<BagCleanupTradeItemConfig> before, IEnumerable<BagCleanupTradeItemConfig> after)
    {
        var old = BagCleanupTradeItemConfig.Normalize(before).ToDictionary(i => i.Name, StringComparer.OrdinalIgnoreCase);
        var next = BagCleanupTradeItemConfig.Normalize(after).ToDictionary(i => i.Name, StringComparer.OrdinalIgnoreCase);
        var result = BagCleanupTradeItemConfig.Normalize(current).ToDictionary(i => i.Name, StringComparer.OrdinalIgnoreCase);
        foreach (var name in old.Keys.Except(next.Keys, StringComparer.OrdinalIgnoreCase)) result.Remove(name);
        foreach (var (name, item) in next)
            if (!old.TryGetValue(name, out var previous) || previous.UnitPrice != item.UnitPrice || previous.PriceLookupMethod != item.PriceLookupMethod)
                result[name] = item;
        return result.Values.ToList();
    }
}
