using Roadhog.Application.Food;
using Roadhog.Core.Accounts;
using Roadhog.Core.Model;

namespace Roadhog;

public sealed partial class AccountSettingsForm
{
    private FoodPreferencePicker? drinkPreferencePicker;
    private FoodPreferencePicker? foodPreferencePicker;

    private void CreateFoodPreferencePickers(Control page)
    {
        drinkPreferencePicker = CreateFoodPreferencePicker(page, FoodKind.Drink, 294);
        foodPreferencePicker = CreateFoodPreferencePicker(page, FoodKind.Food, 574);
    }

    private FoodPreferencePicker CreateFoodPreferencePicker(Control page, FoodKind kind, int x)
    {
        var button = AddButton(page, "技能栏优先 ▾", x, 6, 140, 28);
        button.Name = kind == FoodKind.Drink ? "drinkPreferenceButton" : "foodPreferenceButton";
        var picker = new FoodPreferencePicker(button);
        button.Click += async (_, _) =>
        {
            button.Enabled = false;
            button.Text = "识别中...";
            try
            {
                await RefreshFoodCandidatesAsync();
                if (!button.IsDisposed) picker.Show();
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                if (!button.IsDisposed) MessageBox.Show(this, "未能识别技能栏料理，已保留勾选。\n" + ex.Message,
                    "料理识别", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            finally
            {
                if (!button.IsDisposed) { button.Enabled = true; picker.UpdateText(); }
            }
        };
        return picker;
    }

    private async Task RefreshFoodCandidatesAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        var quickbar = await _runtime.ReadQuickbarAsync(_account, timeout.Token).ConfigureAwait(true);
        var inventory = await _runtime.RefreshInventoryAsync(_account, timeout.Token).ConfigureAwait(true);
        if (IsDisposed) return;
        UpdateFoodCandidates(quickbar, inventory);
    }

    private void UpdateFoodCandidates(QuickbarSnapshot quickbar, IReadOnlyList<InventoryItemSnapshot> inventory)
    {
        drinkPreferencePicker?.SetCandidates(FoodQuickbarBindings.Candidates(quickbar, inventory,
            FoodCatalog.Default, FoodKind.Drink, int.MaxValue));
        foodPreferencePicker?.SetCandidates(FoodQuickbarBindings.Candidates(quickbar, inventory,
            FoodCatalog.Default, FoodKind.Food, int.MaxValue));
    }

    private sealed class FoodPreferencePicker
    {
        private readonly Button button;
        private readonly ToolStripDropDown popup = new() { Padding = Padding.Empty };
        private readonly CheckedListBox list = new()
        {
            CheckOnClick = true, BorderStyle = BorderStyle.None, IntegralHeight = false,
            Size = new(370, 180), BackColor = Color.White,
            Font = new Font("Microsoft YaHei UI", 9F)
        };
        private List<FoodQuickbarPreference> selected = new();
        private IReadOnlyList<FoodQuickbarBinding> candidates = Array.Empty<FoodQuickbarBinding>();
        private bool loading;

        public FoodPreferencePicker(Button button)
        {
            this.button = button;
            var panel = new Panel { Size = new(374, 226), BackColor = Color.White };
            panel.Controls.Add(new Label
            {
                Text = "可多选；优先按技能栏使用，未选或用完则从背包选。",
                Location = new(4, 4), Size = new(366, 38), ForeColor = Color.DarkGreen
            });
            list.Location = new(2, 44);
            panel.Controls.Add(list);
            popup.Items.Add(new ToolStripControlHost(panel) { Margin = Padding.Empty, Padding = Padding.Empty });
            list.ItemCheck += (_, e) =>
            {
                if (loading) return;
                if (list.Items[e.Index] is not Choice choice) { e.NewValue = CheckState.Unchecked; return; }
                selected.RemoveAll(p => p.TemplateId == choice.Preference.TemplateId);
                if (e.NewValue == CheckState.Checked) selected.Add(choice.Preference);
                UpdateText();
            };
            button.Disposed += (_, _) => popup.Dispose();
            UpdateText();
        }

        public List<FoodQuickbarPreference> Capture() => selected.Select(p => p with { }).ToList();
        public void Load(IEnumerable<FoodQuickbarPreference>? preferences)
        {
            selected = preferences?.DistinctBy(p => p.TemplateId).Select(p => p with { }).ToList() ?? new();
            UpdateText();
        }
        public void SetCandidates(IReadOnlyList<FoodQuickbarBinding> value) => candidates = value;
        public void UpdateText() => button.Text = selected.Count == 0 ? "技能栏优先 ▾" : "已勾选 " + selected.Count + " 项 ▾";

        public void Show()
        {
            PopulateChoices();
            popup.Show(button, new Point(0, button.Height));
        }

        private void PopulateChoices()
        {
            loading = true;
            list.Items.Clear();
            var choices = candidates.GroupBy(c => c.Item.TemplateId).Select(g =>
            {
                var c = g.First();
                return new Choice(new(c.Item.TemplateId, c.Item.Name),
                    c.Item.Name + "  [" + FormatSkillKey(c.Key) + "]");
            }).ToList();
            choices.AddRange(selected.Where(p => choices.All(c => c.Preference.TemplateId != p.TemplateId))
                .Select(p => new Choice(p, p.Name + "  [当前栏位不可用]")));
            foreach (var choice in choices)
                list.Items.Add(choice, selected.Any(p => p.TemplateId == choice.Preference.TemplateId));
            if (choices.Count == 0) list.Items.Add("请将中级以上料理放入主栏或 Alt 栏后重新展开。");
            loading = false;
        }

        private sealed record Choice(FoodQuickbarPreference Preference, string Label)
        {
            public override string ToString() => Label;
        }
    }
}
