using Roadhog.Core.Accounts;
using Roadhog.Core.Model;

namespace Roadhog;

public sealed partial class AccountSettingsForm
{
    private EquipmentUpgradeSettings _equipmentUpgradeSettings = new();
    private CancellationTokenSource? _equipmentUpgradeCts;
    private readonly Dictionary<EquipmentUpgradeKind, UpgradeEditor> _equipmentUpgradeEditors = new();
    private Label? _equipmentUpgradeStatus;
    private Button? _equipmentUpgradeRefresh;
    private readonly List<Button> _equipmentUpgradeStarts = new();

    private TabPage CreateEquipmentUpgradeTab()
    {
        var tab = CreateBaseTab("其他");
        var page = CreatePagePanel(); page.AutoScroll = true; page.AutoScrollMinSize = new(872, 714); tab.Controls.Add(page);
        _equipmentUpgradeRefresh = AddButton(page, "刷新背包", 24, 18, 110, 30);
        _equipmentUpgradeRefresh.Click += async (_, _) => await RefreshEquipmentUpgradeListsAsync();
        var start = AddButton(page, "一键执行", 146, 18, 110, 30);
        start.Click += async (_, _) => await StartEquipmentUpgradeAsync(); _equipmentUpgradeStarts.Add(start);
        var stop = AddButton(page, "停止", 268, 18, 90, 30);
        stop.Click += (_, _) => _equipmentUpgradeCts?.Cancel();
        _equipmentUpgradeStatus = new Label { Text = "执行顺序：装备自身等级由低到高；同等级先砸强化值低的，每次砸完重新排序。完成后每 1.5 秒按空格。", AutoSize = false,
            Location = new(24, 54), Size = new(840, 42), ForeColor = _textGreen };
        page.Controls.Add(_equipmentUpgradeStatus);
        foreach (var kind in Enum.GetValues<EquipmentUpgradeKind>())
        {
            var enchant = kind == EquipmentUpgradeKind.Enchant;
            var group = new GroupBox { Text = enchant ? "装备强化 · 低等级优先，+10 完成" : "魔石镶嵌 · 低等级优先，孔满完成",
                Location = new(24, enchant ? 102 : 405), Size = new(880, 284), ForeColor = _textGreen };
            page.Controls.Add(group);
            group.Controls.Add(new Label { Text = "勾选要处理的装备；点选一行设置材料", Location = new(12, 26), AutoSize = true });
            var equipment = new CheckedListBox { Location = new(12, 51), Size = new(500, 182), CheckOnClick = true,
                HorizontalScrollbar = true, BackColor = _inputBackground, ForeColor = _textGreen };
            var label = new Label { Text = "先点选左侧装备", Location = new(530, 26), Size = new(325, 25), AutoEllipsis = true,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
            var levels = new CheckedListBox { Location = new(530, 55), Size = new(325, 176), CheckOnClick = true,
                BackColor = _inputBackground, ForeColor = _textGreen, Visible = enchant, Enabled = false,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
            var stones = new ComboBox { Location = new(530, 59), Width = 325, DropDownStyle = ComboBoxStyle.DropDownList,
                Visible = !enchant, Enabled = false, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
            group.Controls.AddRange(new Control[] { equipment, label, levels, stones });
            var editor = new UpgradeEditor(equipment, label, levels, stones); _equipmentUpgradeEditors[kind] = editor;
            equipment.SelectedIndexChanged += (_, _) => SelectUpgradeTarget(kind, editor);
            group.Controls.Add(new Label { Text = enchant ? "仅用该装备勾选的等级，从低到高使用；每次等待 6 秒。" : "仅用该装备指定的魔石；缺料跳过，孔满完成该任务。",
                Location = new(12, 247), AutoSize = true });
        }
        void ResizeContent()
        {
            var width = Math.Max(824, page.ClientSize.Width - 48);
            foreach (var group in page.Controls.OfType<GroupBox>()) group.Width = width;
            _equipmentUpgradeStatus.Width = width;
        }
        page.ClientSizeChanged += (_, _) => ResizeContent(); ResizeContent();
        return tab;
    }

    private static void CommitUpgradeTarget(UpgradeEditor editor)
    {
        if (editor.Current == null) return;
        editor.Current.EnchantStoneLevels = editor.Levels.CheckedItems.Cast<LevelChoice>().Select(x => x.Level).ToList();
        editor.Current.ManastoneId = (editor.Stones.SelectedItem as StoneChoice)?.Id ?? 0;
    }

    private void SelectUpgradeTarget(EquipmentUpgradeKind kind, UpgradeEditor editor)
    {
        if (editor.Loading) return;
        CommitUpgradeTarget(editor);
        editor.Current = (editor.Equipment.SelectedItem as UpgradeChoice)?.Target;
        var target = editor.Current;
        editor.Label.Text = target == null ? "先点选左侧装备" : $"{target.Name}：{(kind == EquipmentUpgradeKind.Enchant ? "可用强化石等级" : "指定魔石")}";
        editor.Levels.Enabled = editor.Stones.Enabled = target != null && _equipmentUpgradeCts == null;
        editor.Levels.Items.Clear(); editor.Stones.Items.Clear();
        if (target == null) return;
        foreach (var level in editor.AvailableLevels.Union(target.EnchantStoneLevels).Distinct().Order())
            editor.Levels.Items.Add(new LevelChoice(level, editor.LevelCounts.GetValueOrDefault(level)), target.EnchantStoneLevels.Contains(level));
        editor.Stones.Items.Add(new StoneChoice(0, "请选择该装备的魔石"));
        foreach (var stone in editor.AvailableStones) editor.Stones.Items.Add(stone);
        if (target.ManastoneId != 0 && !editor.AvailableStones.Any(x => x.Id == target.ManastoneId))
            editor.Stones.Items.Add(new StoneChoice(target.ManastoneId, $"魔石 {target.ManastoneId}（背包中无剩余）"));
        editor.Stones.SelectedIndex = editor.Stones.Items.Cast<StoneChoice>().Select((x, index) => (x, index))
            .First(x => x.x.Id == target.ManastoneId).index;
    }

    private void LoadEquipmentUpgradeSettings(EquipmentUpgradeSettings settings)
    {
        _equipmentUpgradeSettings = settings.Clone();
        RenderUpgradeInventory(_equipmentUpgradeSettings, null);
    }

    private EquipmentUpgradeSettings CaptureEquipmentUpgradeSettings()
    {
        foreach (var editor in _equipmentUpgradeEditors.Values) CommitUpgradeTarget(editor);
        List<EquipmentUpgradeTarget> Targets(EquipmentUpgradeKind kind) => _equipmentUpgradeEditors[kind].Equipment.CheckedItems
            .Cast<UpgradeChoice>().Select(x => x.Target.Copy()).ToList();
        return new() { EnchantTargets = Targets(EquipmentUpgradeKind.Enchant), ManastoneTargets = Targets(EquipmentUpgradeKind.Manastone) };
    }

    private void RenderUpgradeInventory(EquipmentUpgradeSettings selected, EquipmentUpgradeInventory? inventory)
    {
        var bag = inventory?.Items.Where(i => !i.IsEquipped && i.Slot >= 0).OrderBy(i => i.EquipmentLevel)
            .ThenBy(i => i.EnchantLevel).ThenBy(i => i.Slot).ToArray() ?? Array.Empty<EquipmentUpgradeItem>();
        foreach (var (kind, editor) in _equipmentUpgradeEditors)
        {
            var targets = kind == EquipmentUpgradeKind.Enchant ? selected.EnchantTargets : selected.ManastoneTargets;
            editor.Loading = true; editor.Current = null; editor.Equipment.Items.Clear();
            editor.LevelCounts = bag.Where(i => i.IsEnchantStone && i.EnchantStoneLevel > 0).GroupBy(i => i.EnchantStoneLevel)
                .ToDictionary(g => g.Key, g => g.Sum(i => (decimal)i.Count));
            editor.AvailableLevels = editor.LevelCounts.Keys.Union(targets.SelectMany(t => t.EnchantStoneLevels)).Order().ToArray();
            editor.AvailableStones = bag.Where(i => i.IsManastone).GroupBy(i => i.TemplateId)
                .Select(g => new StoneChoice(g.Key, $"{g.First().Name} ×{g.Sum(i => (decimal)i.Count)}")).ToArray();
            foreach (var item in bag.Where(i => kind == EquipmentUpgradeKind.Enchant ? i.CanEnchant : i.CanSocket))
            {
                var saved = targets.FirstOrDefault(t => t.InstanceId == item.InstanceId && t.TemplateId == item.TemplateId);
                var target = saved?.Copy() ?? new EquipmentUpgradeTarget(item.InstanceId, item.TemplateId, item.Name);
                editor.Equipment.Items.Add(new UpgradeChoice(target,
                    $"{item.Name}  {item.EquipmentLevel}级 +{item.EnchantLevel}  魔石 {item.UsedSockets}/{item.SocketCount}  [格{item.Slot + 1} #{item.InstanceId}]"), saved != null);
            }
            foreach (var target in targets.Where(t => !editor.Equipment.Items.Cast<UpgradeChoice>().Any(x => x.Target.InstanceId == t.InstanceId && x.Target.TemplateId == t.TemplateId)))
                editor.Equipment.Items.Add(new UpgradeChoice(target.Copy(), $"{target.Name} [#{target.InstanceId}]（{(inventory == null ? "刷新查看" : "不在背包")}）"), true);
            editor.Loading = false;
            if (editor.Equipment.Items.Count > 0) editor.Equipment.SelectedIndex = 0;
            else SelectUpgradeTarget(kind, editor);
        }
    }

    private async Task RefreshEquipmentUpgradeListsAsync()
    {
        if (_equipmentUpgradeCts != null) return;
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        _equipmentUpgradeCts = stop; SetEquipmentUpgradeBusy(true);
        try
        {
            var selected = CaptureEquipmentUpgradeSettings();
            var inventory = await _runtime.RefreshEquipmentUpgradeAsync(_account, stop.Token);
            if (IsDisposed) return;
            RenderUpgradeInventory(selected, inventory);
            SetEquipmentUpgradeStatus("背包已刷新。按装备等级、强化值从低到高自动执行，每次砸完重新排序；全部完成后每 1.5 秒按空格。");
        }
        catch (Exception ex) { SetEquipmentUpgradeStatus(ex is OperationCanceledException ? "刷新已停止或超时。" : ex.Message); }
        finally { _equipmentUpgradeCts = null; SetEquipmentUpgradeBusy(false); }
    }

    private async Task StartEquipmentUpgradeAsync()
    {
        if (_equipmentUpgradeCts != null) return;
        var settings = CaptureEquipmentUpgradeSettings();
        using var stop = new CancellationTokenSource(); _equipmentUpgradeCts = stop; SetEquipmentUpgradeBusy(true);
        try
        {
            var result = await _runtime.RunEquipmentUpgradeBatchAsync(_account, settings,
                new Progress<string>(SetEquipmentUpgradeStatus), stop.Token);
            SetEquipmentUpgradeStatus(result.Success ? result.Value!.Message : result.Error ?? "装备操作已停止。");
        }
        catch (Exception ex) { SetEquipmentUpgradeStatus(ex is OperationCanceledException ? "已请求停止装备操作。" : ex.Message); }
        finally { _equipmentUpgradeCts = null; SetEquipmentUpgradeBusy(false); }
    }
    private void SetEquipmentUpgradeStatus(string text) { if (!IsDisposed && _equipmentUpgradeStatus != null) _equipmentUpgradeStatus.Text = text; }
    private void SetEquipmentUpgradeBusy(bool busy)
    {
        if (IsDisposed) return;
        if (_equipmentUpgradeRefresh != null) _equipmentUpgradeRefresh.Enabled = !busy;
        foreach (var button in _equipmentUpgradeStarts) button.Enabled = !busy;
        foreach (var editor in _equipmentUpgradeEditors.Values)
        {
            editor.Equipment.Enabled = !busy;
            editor.Levels.Enabled = editor.Stones.Enabled = !busy && editor.Current != null;
        }
    }
    private sealed record UpgradeChoice(EquipmentUpgradeTarget Target, string Text) { public override string ToString() => Text; }
    private sealed record LevelChoice(int Level, decimal Count) { public override string ToString() => $"{Level} 级强化石 ×{Count}"; }
    private sealed record StoneChoice(uint Id, string Text) { public override string ToString() => Text; }
    private sealed class UpgradeEditor(CheckedListBox equipment, Label label, CheckedListBox levels, ComboBox stones)
    {
        public CheckedListBox Equipment { get; } = equipment;
        public Label Label { get; } = label;
        public CheckedListBox Levels { get; } = levels;
        public ComboBox Stones { get; } = stones;
        public bool Loading;
        public EquipmentUpgradeTarget? Current;
        public int[] AvailableLevels = Array.Empty<int>();
        public Dictionary<int, decimal> LevelCounts = new();
        public StoneChoice[] AvailableStones = Array.Empty<StoneChoice>();
    }
}
