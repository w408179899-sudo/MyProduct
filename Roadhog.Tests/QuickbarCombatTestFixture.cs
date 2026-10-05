using Roadhog.Application.SemiAuto;
using Roadhog.Core.Accounts;
using Roadhog.Core.Model;

internal static class QuickbarCombatTestFixture
{
    private static readonly string[] MainKeys = { "D1", "D2", "D3", "D4", "D5", "D6", "D7", "D8", "D9", "D0", "OemMinus", "OemPlus" };
    private static readonly string[] AltKeys = { "NumPad1", "NumPad2", "NumPad3", "NumPad4", "NumPad5", "NumPad6", "NumPad7", "NumPad8", "NumPad9", "NumPad0", "NumPadAdd", "NumPadSubtract" };

    // Existing shared-controller tests used manual keys before all combat used
    // the official quickbar. Give those fixtures an explicit matching bar.
    // Explicit availability/bar fixtures remain untouched.
    public static void AddBarForSharedTests(ScriptSettings settings, FakeGameApi api)
    {
        if (api.Quickbar.Slots.Count != 0 || api.QuickbarRead is not null ||
            settings.SkillTreeReleaseMode == SkillTreeReleaseMode.QuickbarAvailability) return;
        var slots = new Dictionary<(SkillQuickbar Bar, int Slot), QuickbarSlotSnapshot>();
        void Add(uint id, string? name, string? key)
        {
            if (id == 0)
                id = api.Skills.FirstOrDefault(skill => skill.Name == name || skill.DisplayBaseName == name)?.SkillId ?? 0;
            if (id == 0 || key is null) return;
            var slot = Array.IndexOf(MainKeys, key);
            var bar = SkillQuickbar.Main;
            if (slot < 0) { slot = Array.IndexOf(AltKeys, key); bar = SkillQuickbar.Alt; }
            if (slot >= 0) slots[(bar, slot)] = new(bar, slot, 21, id);
        }
        var plan = SemiAutoSkillPlan.FromSettings(settings.Skills);
        var conditionalIds = plan.Roots.Where(root => root.IsTrigger || root.IsCondition).Select(root => root.SkillId).ToHashSet();
        foreach (var root in plan.Roots) Add(root.SkillId, root.Name, root.Key);
        foreach (var opening in plan.OpeningSkills) Add(opening.SkillId, opening.Name, opening.Key);
        foreach (var rule in settings.Maintenance.HpMaintenanceRules.Concat(settings.Maintenance.MpMaintenanceRules))
            Add(rule.SkillId, rule.SkillName, rule.Key);
        foreach (var rule in settings.Maintenance.StatusMaintenanceRules) Add(rule.SkillId, rule.SkillName, rule.Key);
        foreach (var rule in settings.Maintenance.DpMaintenanceRules) Add(rule.SkillId, rule.SkillName, rule.Key);
        var spirit = settings.Skills.Spiritmaster;
        foreach (var rule in spirit.SummonSkills) Add(rule.SkillId, rule.SkillName, rule.Key);
        foreach (var rule in spirit.PetHpMaintenanceRules) Add(rule.SkillId, rule.SkillName, rule.Key);
        foreach (var rule in spirit.PetBuffRules) Add(rule.SkillId, rule.SkillName, rule.Key);
        Add(spirit.OpeningAttackSkillId, spirit.OpeningAttackSkillName, spirit.OpeningAttackKey);
        api.Quickbar = new(0, slots.Values.ToArray());
        if (api.SkillAvailability.Slots.Count == 0 && api.SkillAvailabilityRead is null)
            api.SkillAvailabilityRead = () => new(0, slots.Values.Select(slot =>
            {
                var skill = api.Skills.FirstOrDefault(value => value.SkillId == slot.SkillId);
                var ready = skill is not null && !conditionalIds.Contains(skill.SkillId) &&
                    (skill.CooldownEndTime == 0 || unchecked((int)(skill.CooldownEndTime - (uint)Environment.TickCount64)) <= 0);
                return new SkillAvailabilitySlotSnapshot(slot.Bar, slot.Slot, slot.ContentType, slot.SkillId, slot.SkillId, ready);
            }).ToArray());
    }
}
