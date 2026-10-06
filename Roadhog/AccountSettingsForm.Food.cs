using Roadhog.Application.Food;
using Roadhog.Core.Accounts;
using Roadhog.Core.Model;

namespace Roadhog;

public sealed partial class AccountSettingsForm
{
    private FoodPreferencePicker? drinkPreferencePicker;
    private FoodPreferencePicker? foodPreferencePicker;
    private bool refreshingFoodCandidates;

    private void CreateFoodPreferencePickers(Control page)
    {
        drinkPreferencePicker = CreateFoodPreferencePicker(page, FoodKind.Drink, 112);
        foodPreferencePicker = CreateFoodPreferencePicker(page, FoodKind.Food, 146);
    }

    private FoodPreferencePicker CreateFoodPreferencePicker(Control page, FoodKind kind, int y)
    {
        var combo = AddCombo(page, 112, y, 260, 28);
        combo.Name = kind == FoodKind.Drink ? "drinkPreferenceCombo" : "foodPreferenceCombo";
        combo.DropDownWidth = 360;
        var key = AddButton(page, "启动时识别", 384, y, 142, 28);
        key.Name = kind == FoodKind.Drink ? "drinkPreferenceKey" : "foodPreferenceKey";
        key.Enabled = false;
        var picker = new FoodPreferencePicker(combo, key);
        combo.Controls.OfType<ComboBox>().Single().DropDown += async (_, _) => await RefreshFoodCandidatesAsync();
        return picker;
    }

    private async Task RefreshFoodCandidatesAsync()
    {
        if (refreshingFoodCandidates) return;
        refreshingFoodCandidates = true;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            var inventory = await _runtime.RefreshInventoryAsync(_account, timeout.Token).ConfigureAwait(true);
            var quickbar = await _runtime.ReadQuickbarAsync(_account, timeout.Token).ConfigureAwait(true);
            if (!IsDisposed) UpdateFoodCandidates(quickbar, inventory);
        }
        catch (Exception)
        {
            if (!IsDisposed)
            {
                drinkPreferencePicker?.MarkUnresolved();
                foodPreferencePicker?.MarkUnresolved();
            }
        }
        finally { refreshingFoodCandidates = false; }
    }

    private void UpdateFoodCandidates(QuickbarSnapshot quickbar, IReadOnlyList<InventoryItemSnapshot> inventory)
    {
        drinkPreferencePicker?.SetCandidates(quickbar, inventory, FoodKind.Drink);
        foodPreferencePicker?.SetCandidates(quickbar, inventory, FoodKind.Food);
    }

    private sealed class FoodPreferencePicker
    {
        private readonly RoundedComboBox combo;
        private readonly Button key;
        private FoodQuickbarPreference? selected;
        private IReadOnlyList<FoodQuickbarPreference> candidates = Array.Empty<FoodQuickbarPreference>();
        private IReadOnlyList<FoodQuickbarBinding>? bindings;
        private bool loading;

        public FoodPreferencePicker(RoundedComboBox combo, Button key)
        {
            this.combo = combo;
            this.key = key;
            combo.SelectedIndexChanged += (_, _) =>
            {
                if (loading) return;
                selected = (combo.SelectedItem as Choice)?.Preference;
                UpdateKey();
            };
            PopulateChoices();
        }

        // Retain the existing JSON shape; old multi-selections migrate to their first item.
        public List<FoodQuickbarPreference> Capture() => selected == null ? new() : new() { selected with { } };
        public void Load(IEnumerable<FoodQuickbarPreference>? preferences)
        {
            selected = preferences?.FirstOrDefault();
            PopulateChoices();
        }

        public void SetCandidates(QuickbarSnapshot quickbar, IReadOnlyList<InventoryItemSnapshot> inventory, FoodKind kind)
        {
            var catalog = FoodCatalog.Default;
            candidates = inventory.Where(i => catalog.Select(new[] { i }, kind, int.MaxValue) != null)
                .OrderBy(i => i.Food!.Level).ThenBy(i => i.TemplateId).DistinctBy(i => i.TemplateId)
                .Select(i => new FoodQuickbarPreference(i.TemplateId, i.Name)).ToArray();
            bindings = FoodQuickbarBindings.Candidates(quickbar, inventory, catalog, kind, int.MaxValue);
            PopulateChoices();
        }

        public void MarkUnresolved()
        {
            bindings = null;
            key.Text = "识别未完成";
        }

        private void UpdateKey()
        {
            var binding = bindings?.FirstOrDefault(b => b.Item.TemplateId == selected?.TemplateId);
            key.Text = selected == null ? "背包自动选择" : bindings == null ? "启动时识别" :
                binding == null ? "未放入技能栏" : "自动: " + FormatSkillKey(binding.Key);
        }

        private void PopulateChoices()
        {
            loading = true;
            combo.Items.Clear();
            combo.Items.Add(new Choice(null));
            var all = candidates.ToList();
            if (selected != null && all.All(p => p.TemplateId != selected.TemplateId)) all.Add(selected);
            foreach (var item in all) combo.Items.Add(new Choice(item));
            combo.SelectedIndex = selected == null ? 0 : all.FindIndex(p => p.TemplateId == selected.TemplateId) + 1;
            loading = false;
            UpdateKey();
        }

        private sealed record Choice(FoodQuickbarPreference? Preference)
        {
            public override string ToString() => Preference == null ? "自动选择（背包）" : Preference.Name + " #" + Preference.TemplateId;
        }
    }
}
