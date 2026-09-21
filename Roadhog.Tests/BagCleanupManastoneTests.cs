using Roadhog.Core.Accounts;
using Roadhog.Core.Model;

internal static class BagCleanupManastoneTests
{
    public static Task EquipmentRulesExcludeManastonesAsync()
    {
        var rules=BagCleanupRuleCatalog.CreateDefaultRules();
        foreach(var rule in rules)rule.Enabled=false;
        var equipment=rules.Single(r=>r.Key==BagCleanupRuleCatalog.GreenEquipment);
        equipment.Enabled=true;
        var stoneRule=rules.Single(r=>r.Key==BagCleanupRuleCatalog.GreenManastone);
        var settings=new MaintenanceScriptSettings{BagCleanupRules=rules};
        var shield=new InventoryItemSnapshot(115000001,1,"绿色盾牌",1,0,false,6,2);
        var stones=new[]
        {
            new InventoryItemSnapshot(167000521,2,"魔石:盾牌防御+25",14,1,false,24,2),
            new InventoryItemSnapshot(167000522,3,"盾牌防御强化",1,2,false,60,2),
            new InventoryItemSnapshot(167000523,4,"魔石：武器防御",1,3,false,0,2)
        };
        var items=new[]{shield}.Concat(stones).ToArray();
        foreach(var action in new[]{BagCleanupAction.Sell,BagCleanupAction.Discard})
        {
            equipment.Action=action;
            var selected=action==BagCleanupAction.Sell?BagCleanupItemMatcher.SelectSellRegistrationItems(items,settings):BagCleanupItemMatcher.SelectDiscardItems(items,settings);
            Require(selected.Count==1&&selected[0]==shield,"equipment rules must keep real shields but exclude every manastone");
        }
        Require(stones.All(s=>!BagCleanupItemMatcher.IsEquipment(s)),"NPC batch classification also excludes manastones");
        equipment.Enabled=false;stoneRule.Enabled=true;stoneRule.Action=BagCleanupAction.Sell;
        Require(BagCleanupItemMatcher.SelectSellRegistrationItems(items,settings).SequenceEqual(stones),"explicit manastone sale still works");
        stoneRule.Action=BagCleanupAction.Discard;
        Require(BagCleanupItemMatcher.SelectDiscardItems(items,settings).SequenceEqual(stones),"explicit manastone discard still works");
        stoneRule.Enabled=false;equipment.Enabled=true;equipment.Action=BagCleanupAction.Sell;
        settings.BagCleanupExcludedItemNames=new(){"盾牌防御"};
        settings.BagCleanupDiscardItemNameKeywords=new(){"魔石:盾牌防御+25"};
        Require(BagCleanupItemMatcher.SelectSellRegistrationItems(stones,settings).Count==0&&BagCleanupItemMatcher.SelectDiscardItems(stones,settings).Count==0,
            "account 4 whitelist protects discard without turning the manastone into equipment for sale");
        return Task.CompletedTask;
    }
    private static void Require(bool value,string message){if(!value)throw new Exception(message);}
}
