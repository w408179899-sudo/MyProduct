using Roadhog.Core.Model;

namespace Roadhog;

public sealed partial class AccountSettingsForm
{
    private (int UpdatedCount, int DeletedCount, bool Saved, string Error) RefreshAndSaveConfiguredSkills()
    {
        var result = RefreshAllConfiguredSkills();
        var saved = SaveCurrentSettings(out var error);
        return (result.UpdatedCount, result.DeletedCount, saved, saved ? string.Empty : error);
    }

    private (int UpdatedCount, int DeletedCount) RefreshAllConfiguredSkills()
    {
        var updated = 0;
        var deleted = 0;
        void Add((int UpdatedCount, int DeletedCount) result)
        {
            updated += result.UpdatedCount;
            deleted += result.DeletedCount;
        }

        if (selectedSkillTree is not null)
            Add(RefreshSelectedSkillTreeToHighestCurrentSkills(selectedSkillTree, currentManualSkills));
        if (systemSelectedSkillTree is not null)
            Add(RefreshSelectedSkillTreeToHighestCurrentSkillsCore(systemSelectedSkillTree, currentManualSkills, systemTree: true));

        var candidates = BuildHighestSkillCandidates(currentManualSkills.Where(skill => !ShouldHideManualSkillCandidate(skill)));
        var allCandidates = BuildHighestSkillCandidates(currentManualSkills);
        var openingCandidates = BuildHighestSkillCandidates(currentManualSkills
            .Where(skill => !ShouldHideManualSkillCandidate(skill) && IsOpeningSkillCandidate(skill)));

        foreach (var list in new[] { hpMaintenanceRuleList, mpMaintenanceRuleList, statusMaintenanceRuleList,
                     dpMaintenanceRuleList, teamHealSkillRuleList })
        {
            if (list is null) continue;
            foreach (var row in list.Controls.OfType<Panel>())
            {
                var combo = row.Controls.OfType<RoundedComboBox>()
                    .FirstOrDefault(item => item.Name == "maintenanceRuleSkillCombo");
                if (combo is not null) Add(RefreshConfiguredMaintenanceCombo(combo, allCandidates));
            }
        }

        foreach (var combo in new[] { teamMentalCleanseSkillCombo, teamPhysicalCleanseSkillCombo, teamGroupCleanseSkillCombo })
            if (combo is not null) Add(RefreshConfiguredMaintenanceCombo(combo, allCandidates));

        var liveSpiritmasterLists = spiritmasterRuleLists.Where(list => !list.IsDisposed).ToArray();
        foreach (var list in liveSpiritmasterLists)
        {
            foreach (var row in list.Controls.OfType<Panel>())
            {
                var combo = row.Controls.OfType<RoundedComboBox>()
                    .FirstOrDefault(item => item.Name == "spiritmasterRuleSkillCombo");
                if (combo is null) continue;
                Add(RefreshConfiguredMaintenanceCombo(combo, allCandidates));
                var status = row.Controls.OfType<Label>().FirstOrDefault(label => label.Name == "spiritmasterDotStatusLabel");
                var duration = row.Controls.OfType<Label>().FirstOrDefault(label => label.Name == "spiritmasterDotDurationLabel");
                if (status is not null && duration is not null) UpdateSpiritmasterDotRuleInfo(combo, status, duration);
            }
        }
        if (liveSpiritmasterLists.Length == 0)
            Add(RefreshStoredSpiritmasterSkills(allCandidates));

        if (openingSkillRows is not null)
        {
            foreach (var row in openingSkillRows.Controls.OfType<Panel>())
            {
                if (row.Controls["openingSkillCombo"] is RoundedComboBox combo)
                    Add(RefreshConfiguredOpeningCombo(combo, openingCandidates));
            }
        }

        if (manualSkillMappingList is not null)
        {
            foreach (var row in manualSkillMappingList.Controls.OfType<Panel>())
            {
                if (row.Controls["manualSkillNameCombo"] is not RoundedComboBox combo ||
                    row.Controls["manualSkillTypeCombo"] is not RoundedComboBox typeCombo) continue;
                var previous = combo.Text.Trim();
                var skill = FindConfiguredSkill(0, previous, candidates);
                PopulateManualSkillNameCombo(combo, typeCombo.Text);
                var next = skill is null ? string.Empty : FormatManualSkillName(skill);
                combo.SelectedIndex = string.IsNullOrEmpty(next) ? -1 : combo.Items.IndexOf(next);
                if (combo.SelectedIndex < 0) combo.Text = string.Empty;
                if (!string.IsNullOrEmpty(previous))
                {
                    if (skill is null) deleted++;
                    else if (!string.Equals(previous, next, StringComparison.Ordinal)) updated++;
                }
            }
        }

        return (updated, deleted);
    }

    private (int UpdatedCount, int DeletedCount) RefreshConfiguredMaintenanceCombo(
        RoundedComboBox combo, IReadOnlyDictionary<string, SkillSnapshot> candidates)
    {
        var previous = GetSelectedMaintenanceSkill(combo);
        var hasSelection = previous.SkillId != 0 || !string.IsNullOrWhiteSpace(previous.SkillName);
        var skill = hasSelection ? FindConfiguredSkill(previous.SkillId, previous.SkillName, candidates) : null;
        PopulateMaintenanceSkillCombo(combo, skill?.SkillId ?? 0, skill is null ? string.Empty : FormatManualSkillName(skill));
        if (!hasSelection) return (0, 0);
        if (skill is null) return (0, 1);
        return previous.SkillId != skill.SkillId ||
               !string.Equals(previous.SkillName, FormatManualSkillName(skill), StringComparison.Ordinal)
            ? (1, 0) : (0, 0);
    }

    private (int UpdatedCount, int DeletedCount) RefreshConfiguredOpeningCombo(
        RoundedComboBox combo, IReadOnlyDictionary<string, SkillSnapshot> candidates)
    {
        var previous = GetSelectedOpeningSkill(combo);
        var hasSelection = previous.SkillId != 0 || !string.IsNullOrWhiteSpace(previous.SkillName);
        var skill = hasSelection ? FindConfiguredSkill(previous.SkillId, previous.SkillName, candidates) : null;
        PopulateOpeningSkillCombo(combo, skill?.SkillId ?? 0, skill is null ? string.Empty : FormatManualSkillName(skill));
        if (!hasSelection) return (0, 0);
        if (skill is null) return (0, 1);
        return previous.SkillId != skill.SkillId ||
               !string.Equals(previous.SkillName, FormatManualSkillName(skill), StringComparison.Ordinal)
            ? (1, 0) : (0, 0);
    }

    private static SkillSnapshot? FindConfiguredSkill(
        uint skillId, string? skillName, IReadOnlyDictionary<string, SkillSnapshot> candidates)
    {
        var key = NormalizeSkillBaseName(skillName);
        if (key.Length > 0 && candidates.TryGetValue(key, out var current)) return current;
        return skillId == 0 ? null : candidates.Values.FirstOrDefault(skill => skill.SkillId == skillId);
    }

    private (int UpdatedCount, int DeletedCount) RefreshStoredSpiritmasterSkills(
        IReadOnlyDictionary<string, SkillSnapshot> candidates)
    {
        var updated = 0;
        var deleted = 0;
        void Refresh(uint id, string name, Action<uint, string> set)
        {
            if (id == 0 && string.IsNullOrWhiteSpace(name)) return;
            var skill = FindConfiguredSkill(id, name, candidates);
            if (skill is null)
            {
                set(0, string.Empty);
                deleted++;
                return;
            }
            var currentName = FormatManualSkillName(skill);
            if (id == skill.SkillId && string.Equals(name, currentName, StringComparison.Ordinal)) return;
            set(skill.SkillId, currentName);
            updated++;
        }

        var settings = currentSpiritmasterSettings;
        foreach (var rule in settings.DotSkills)
            Refresh(rule.SkillId, rule.SkillName, (id, name) => { rule.SkillId = id; rule.SkillName = name; });
        foreach (var rule in settings.SummonSkills)
            Refresh(rule.SkillId, rule.SkillName, (id, name) => { rule.SkillId = id; rule.SkillName = name; });
        Refresh(settings.OpeningAttackSkillId, settings.OpeningAttackSkillName,
            (id, name) => { settings.OpeningAttackSkillId = id; settings.OpeningAttackSkillName = name; });
        foreach (var rule in settings.PetHpMaintenanceRules)
            Refresh(rule.SkillId, rule.SkillName, (id, name) => { rule.SkillId = id; rule.SkillName = name; });
        foreach (var rule in settings.PetBuffRules)
            Refresh(rule.SkillId, rule.SkillName, (id, name) => { rule.SkillId = id; rule.SkillName = name; });
        return (updated, deleted);
    }
}
