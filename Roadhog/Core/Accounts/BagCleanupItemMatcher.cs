using Roadhog.Core.Model;

namespace Roadhog.Core.Accounts;

public static class BagCleanupItemMatcher
{
    private const uint SkillBookItemType = 31;

    public static IReadOnlyList<InventoryItemSnapshot> SelectSellRegistrationItems(
        IEnumerable<InventoryItemSnapshot> items,
        MaintenanceScriptSettings settings)
    {
        return SelectItems(items, settings, BagCleanupAction.Sell, resolveSellDiscardConflict: false);
    }

    public static IReadOnlyList<InventoryItemSnapshot> SelectDiscardItems(
        IEnumerable<InventoryItemSnapshot> items,
        MaintenanceScriptSettings settings)
    {
        return SelectItems(items, settings, BagCleanupAction.Discard, resolveSellDiscardConflict: true);
    }

    public static IReadOnlyList<InventoryItemSnapshot> SelectSellDiscardConflicts(
        IEnumerable<InventoryItemSnapshot> items,
        MaintenanceScriptSettings settings)
    {
        var rules = BagCleanupRuleCatalog
            .MergeWithDefaults(settings.BagCleanupRules)
            .Where(rule => rule.Enabled)
            .ToArray();
        var sellRules = rules.Where(rule => rule.Action == BagCleanupAction.Sell).ToArray();
        var discardRules = rules.Where(rule => rule.Action == BagCleanupAction.Discard).ToArray();
        return FilterBagItems(items, settings)
            .Where(item => sellRules.Any(rule => MatchesRule(item, rule)) &&
                           discardRules.Any(rule => MatchesRule(item, rule)))
            .OrderBy(item => item.Slot)
            .ThenBy(item => item.TemplateId)
            .ThenBy(item => item.InstanceId)
            .ToArray();
    }

    private static IReadOnlyList<InventoryItemSnapshot> SelectItems(
        IEnumerable<InventoryItemSnapshot> items,
        MaintenanceScriptSettings settings,
        BagCleanupAction action,
        bool resolveSellDiscardConflict)
    {
        var excludedNameKeywords = settings.BagCleanupExcludedItemNames
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var allRules = BagCleanupRuleCatalog
            .MergeWithDefaults(settings.BagCleanupRules)
            .Where(rule => rule.Enabled)
            .ToArray();
        var rules = allRules.Where(rule => rule.Action == action).ToArray();
        var sellRules = resolveSellDiscardConflict
            ? allRules.Where(rule => rule.Action == BagCleanupAction.Sell).ToArray()
            : Array.Empty<BagCleanupRuleConfig>();

        if (rules.Length == 0)
        {
            return Array.Empty<InventoryItemSnapshot>();
        }

        return items
            .Where(item => IsBagItem(item) && !IsExcludedByNameKeyword(item.Name, excludedNameKeywords))
            .Where(item => rules.Any(rule => MatchesRule(item, rule)))
            .Where(item => !resolveSellDiscardConflict || !sellRules.Any(rule => MatchesRule(item, rule)))
            .OrderBy(item => item.Slot)
            .ThenBy(item => item.TemplateId)
            .ThenBy(item => item.InstanceId)
            .ToArray();
    }

    private static IEnumerable<InventoryItemSnapshot> FilterBagItems(
        IEnumerable<InventoryItemSnapshot> items,
        MaintenanceScriptSettings settings)
    {
        var excludedNameKeywords = settings.BagCleanupExcludedItemNames
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return items.Where(item =>
            IsBagItem(item) && !IsExcludedByNameKeyword(item.Name, excludedNameKeywords));
    }

    public static bool MatchesRule(InventoryItemSnapshot item, BagCleanupRuleConfig rule)
    {
        return MatchesCategory(item, rule) && MatchesQuality(item, rule.Quality);
    }

    private static bool IsBagItem(InventoryItemSnapshot item)
    {
        return !item.IsEquipped &&
               item.Slot >= 0 &&
               !string.IsNullOrWhiteSpace(item.Name);
    }

    private static bool IsExcludedByNameKeyword(string itemName, IReadOnlyList<string> keywords)
    {
        return keywords.Any(keyword => itemName.Contains(keyword, StringComparison.OrdinalIgnoreCase));
    }

    private static bool MatchesCategory(InventoryItemSnapshot item, BagCleanupRuleConfig rule)
    {
        return rule.Category switch
        {
            "equipment" => IsEquipment(item),
            "manastone" => IsManastone(item),
            "scroll" => IsScroll(item, rule),
            "book" => IsBook(item, rule) || IsSkillBookItemType(item, rule),
            "extraction_stone" => ContainsAny(item.Name, "提炼石", "精炼石"),
            "consumable" => ContainsAny(item.Name, "药水", "仙药", "灵药"),
            _ => rule.ItemKinds.Any(kind => ContainsNormalized(item.Name, kind))
        };
    }

    public static bool IsEquipment(InventoryItemSnapshot item)
    {
        if (IsRecipeOrDesignScroll(item))
        {
            return false;
        }

        return IsKnownEquipmentType(item.ItemType) ||
               ContainsAny(
                   item.Name,
                   "剑",
                   "刀",
                   "弓",
                   "杖",
                   "法书",
                   "法珠",
                   "盾",
                   "头盔",
                   "护肩",
                   "上衣",
                   "下装",
                   "手套",
                   "鞋",
                   "腰带",
                   "项链",
                   "耳环",
                   "戒指");
    }

    private static bool IsKnownEquipmentType(uint itemType)
    {
        return itemType == 1 ||
               itemType == 2 ||
               itemType == 6 ||
               itemType == 7 ||
               itemType == 8;
    }

    private static bool IsManastone(InventoryItemSnapshot item)
    {
        return item.ItemType == 24 ||
               item.ItemType == 60 ||
               ContainsAny(item.Name, "魔石");
    }

    private static bool IsScroll(InventoryItemSnapshot item, BagCleanupRuleConfig rule)
    {
        if (string.Equals(rule.Key, BagCleanupRuleCatalog.Stigma, StringComparison.OrdinalIgnoreCase))
        {
            return item.ItemType == 9 ||
                   ContainsAny(item.Name, "烙印");
        }

        return IsRecipeOrDesignScroll(item);
    }

    private static bool IsRecipeOrDesignScroll(InventoryItemSnapshot item)
    {
        return item.ItemType == 27 ||
               ContainsAny(item.Name, "图纸", "图案", "卷轴", "制作");
    }

    private static bool IsBook(InventoryItemSnapshot item, BagCleanupRuleConfig rule)
    {
        if (string.Equals(rule.Key, BagCleanupRuleCatalog.SpellBook, StringComparison.OrdinalIgnoreCase))
        {
            return ContainsAny(item.Name, "咒语书");
        }

        return ContainsAny(item.Name, "技能书");
    }

    private static bool IsSkillBookItemType(InventoryItemSnapshot item, BagCleanupRuleConfig rule)
    {
        return item.ItemType == SkillBookItemType &&
               string.Equals(rule.Key, BagCleanupRuleCatalog.SkillBook, StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesQuality(InventoryItemSnapshot item, string? quality)
    {
        if (string.IsNullOrWhiteSpace(quality))
        {
            return true;
        }

        var rank = QualityToRank(quality);
        if (rank != 0 && item.QualityRank != 0)
        {
            return item.QualityRank == rank;
        }

        return quality.Trim().ToLowerInvariant() switch
        {
            "white" => ContainsAny(item.Name, "白色", "白", "普通"),
            "green" => ContainsAny(item.Name, "绿色", "绿"),
            "blue" => ContainsAny(item.Name, "蓝色", "蓝"),
            "gold" => ContainsAny(item.Name, "金色", "金", "黄色", "黄"),
            _ => false
        };
    }

    private static byte QualityToRank(string quality)
    {
        return quality.Trim().ToLowerInvariant() switch
        {
            "white" => 1,
            "green" => 2,
            "blue" => 3,
            "gold" => 4,
            _ => 0
        };
    }

    private static bool ContainsAny(string text, params string[] values)
    {
        return values.Any(value => text.Contains(value, StringComparison.OrdinalIgnoreCase));
    }

    private static bool ContainsNormalized(string text, string token)
    {
        return !string.IsNullOrWhiteSpace(token) &&
               text.Contains(token.Trim().Replace("_", string.Empty), StringComparison.OrdinalIgnoreCase);
    }
}
