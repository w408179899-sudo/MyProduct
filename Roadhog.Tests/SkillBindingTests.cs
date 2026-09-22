using System.Reflection;
using System.Text.Json;
using System.Windows.Forms;
using Roadhog;
using Roadhog.Application;
using Roadhog.Application.SemiAuto;
using Roadhog.Application.Workers;
using Roadhog.Core.Accounts;
using Roadhog.Core.Api;
using Roadhog.Core.Common;
using Roadhog.Core.Diagnostics;
using Roadhog.Core.Model;
using Roadhog.Infrastructure.Vmm;

internal static class SkillBindingTests
{
    static SkillSnapshot Skill(uint id, string name, string? chain = null, string? prechain = null) =>
        new(id, name, 1, 1, name, 1, false, 0, 0, XmlActivation: "Active", XmlChainCategory: chain, XmlPrechainCategory: prechain);
    static readonly SkillSnapshot[] Skills = { Skill(101, "attack", "chain-a"), Skill(102, "heal"), Skill(103, "summon"), Skill(104, "chain", prechain: "chain-a"), Skill(105, "unbound") };
    static QuickbarSnapshot Bar(int main = 7, int alt = 3) => new(0, new QuickbarSlotSnapshot[]
    {
        new(SkillQuickbar.Main, main, 21, 101), new(SkillQuickbar.Alt, alt, 21, 102),
        new(SkillQuickbar.Alt, 10, 21, 103), new(SkillQuickbar.Alt, 0, 21, 101),
        new(SkillQuickbar.Main, 0, 1, 105)
    });
    static SkillConfigNode Node(uint id, string name) => new() { SkillId = id, Name = name, BaseName = name, Type = "主动技能" };
    static void Check(bool value, string message) { if (!value) throw new Exception(message); }

    public static Task MappingAsync()
    {
        var bindings = new SkillKeyBindings(Bar(), Skills);
        var settings = new SkillScriptSettings { ExecutionTree = new() { Node(102, "heal"), Node(101, "attack"), Node(105, "unbound") } };
        settings.ExecutionTree[1].Children.Add(Node(104, "chain"));
        settings.ExecutionTree[1].Children.Add(Node(105, "unbound"));
        var missing = new List<string>();
        var plan = SemiAutoSkillPlan.FromSettings(settings, bindings, missing.Add);
        Check(plan.Roots.Select(n => n.SkillId).SequenceEqual(new uint[] { 102, 101 }), "tree order survives placement; missing root omitted");
        Check(plan.Roots.Select(n => n.Key).SequenceEqual(new[] { "NumPad4", "D8" }), "fixed bar keys and main preference");
        Check(plan.Roots[1].Children.Single().Key == "D8", "confirmed chain inherits source slot only");
        Check(missing.Count == 2, "unbound nonchain child cannot blindly inherit");
        Check(bindings.Resolve(103, "summon")?.Key == "NumPadAdd", "Alt eleventh slot uses agreed Num+ key");
        var lastSlot = new SkillKeyBindings(new(0, new[] { new QuickbarSlotSnapshot(SkillQuickbar.Alt, 11, 21, 102) }), Skills);
        Check(lastSlot.Resolve(102, "heal")?.Key == "NumPadSubtract", "Alt twelfth slot uses agreed Num- key");
        Check(bindings.Resolve(105, "unbound") is null, "item with same id is never a skill");
        var duplicate = new SkillKeyBindings(new(0, new[] { new QuickbarSlotSnapshot(SkillQuickbar.Main, 11, 21, 102), new(SkillQuickbar.Main, 2, 21, 102) }), Skills);
        Check(duplicate.Resolve(102, "heal")?.Key == "D3", "duplicate prefers leftmost");
        var childOwnSlot = new SkillKeyBindings(Bar() with { Slots = Bar().Slots.Append(new(SkillQuickbar.Main, 5, 21, 104)).ToArray() }, Skills);
        Check(SemiAutoSkillPlan.FromSettings(settings, childOwnSlot).Roots[1].Children.Single().Key == "D6", "independent chain slot wins");
        var many = new SkillScriptSettings { KeyOrder = new() { "wrong" }, ExecutionTree = Enumerable.Range(0, 30).Select(_ => Node(101, "attack")).ToList() };
        Check(SemiAutoSkillPlan.FromSettings(many, bindings).Roots.Count == 30, "key-order length no longer truncates tree");
        Check(new SkillKeyBindings(new(0, Array.Empty<QuickbarSlotSnapshot>()), Skills).Resolve(101, "attack") is null, "empty binding does not fall back to old key");
        Check(bindings.Resolve(999, "attack") is null, "explicit unavailable rank cannot be replaced by a same-name skill");
        Check(bindings.Resolve(0, "heal")?.SkillId == 102, "legacy unique name resolves to an actual skill identity");
        var ambiguous = new SkillKeyBindings(Bar(), Skills.Select(s => s with { DisplayBaseName = "same" }).ToArray());
        Check(ambiguous.Resolve(0, "same") is null, "ambiguous legacy names never pick an arbitrary slot");
        settings.OpeningSkill = new() { Enabled = true, SkillId = 103, SkillName = "summon", Key = "" };
        Check(SemiAutoSkillPlan.FromSettings(settings, bindings).OpeningSkill?.Key == "NumPadAdd", "opening skill needs no saved manual key");
        settings.Mode = SkillConfigurationMode.SystemClassification;
        settings.SystemExecutionTree.Add(Node(103, "summon"));
        Check(SemiAutoSkillPlan.FromSettings(settings, bindings).Roots.Single().SkillId == 103, "system tree retains its own sequence");
        return Task.CompletedTask;
    }

    static ScriptSettings Settings()
    {
        var s = new ScriptSettings();
        s.Skills.ExecutionTree.Add(Node(101, "attack"));
        s.Maintenance.HpMaintenanceRules.Add(new() { BelowPercent = 61, SkillId = 102, SkillName = "heal", Key = "D1" });
        s.Maintenance.MpMaintenanceRules.Add(new() { SkillId = 102, SkillName = "heal", Key = "D2" });
        s.Maintenance.MpMaintenanceRules.Add(new() { ActionType = MaintenanceRuleActionType.Potion, Key = "F8" });
        s.Maintenance.StatusMaintenanceRules.Add(new() { SkillId = 102, SkillName = "heal", Key = "D3", AbnormalStatusId = 999 });
        s.Maintenance.DpMaintenanceRules.Add(new() { SkillId = 102, SkillName = "heal", Key = "D4" });
        s.Skills.Spiritmaster.SummonSkills.Add(new() { SkillId = 103, SkillName = "summon", Key = "D5" });
        s.Skills.Spiritmaster.SummonSkills.Add(new() { Key = "F7" });
        s.Skills.Spiritmaster.OpeningAttackSkillId = 101;
        s.Skills.Spiritmaster.OpeningAttackKey = "F6";
        s.Skills.Spiritmaster.PetHpMaintenanceRules.Add(new() { SkillId = 102, SkillName = "heal", Key = "D6" });
        s.Skills.Spiritmaster.PetBuffRules.Add(new() { SkillId = 102, SkillName = "heal", Key = "D7" });
        s.Team.Support.HealSkillRules.Add(new() { SkillId = 102, SkillName = "heal", Key = "D8" });
        s.Team.Support.MentalCleanseSkillId = 102;
        s.Team.Support.MentalCleanseKey = "D9";
        s.Team.Support.GroupCleanseKey = "F5";
        return s;
    }

    public static Task ConsumersAsync()
    {
        var source = Settings();
        var before = JsonSerializer.Serialize(source);
        var result = new SkillKeyBindings(Bar(), Skills).ApplyTo(source);
        Check(result.Maintenance.HpMaintenanceRules[0].Key == "NumPad4" && result.Maintenance.HpMaintenanceRules[0].BelowPercent == 61, "HP key changes without threshold changes");
        Check(result.Maintenance.MpMaintenanceRules[0].Key == "NumPad4" && result.Maintenance.MpMaintenanceRules[1].Key == "F8", "MP skill remaps, potion stays manual");
        Check(result.Maintenance.StatusMaintenanceRules[0].Key == "NumPad4" && result.Maintenance.StatusMaintenanceRules[0].AbnormalStatusId == 999, "buff policy retained");
        Check(result.Maintenance.DpMaintenanceRules[0].Key == "NumPad4", "DP remapped");
        Check(result.Skills.Spiritmaster.SummonSkills.Select(r => r.Key).SequenceEqual(new[] { "NumPadAdd", "F7" }), "summon skill and manual macro coexist");
        Check(result.Skills.Spiritmaster.OpeningAttackKey == "D8", "spirit opening skill remapped");
        Check(result.Skills.Spiritmaster.PetHpMaintenanceRules[0].Key == "NumPad4" && result.Skills.Spiritmaster.PetBuffRules[0].Key == "NumPad4", "pet HP and buffs remapped");
        Check(result.Team.Support.HealSkillRules[0].Key == "NumPad4" && result.Team.Support.MentalCleanseKey == "NumPad4" && result.Team.Support.GroupCleanseKey == "F5", "team skill and manual cleanse coexist");
        Check(JsonSerializer.Serialize(source) == before, "saved config untouched");
        var restored = JsonSerializer.Deserialize<ScriptSettings>(before)!.Clone();
        Check(restored.Skills.Spiritmaster.OpeningAttackSkillId == 101 && restored.Team.Support.MentalCleanseSkillId == 102, "new selections roundtrip and clone");
        source.Maintenance.HpMaintenanceRules[0].SkillId = 105;
        Check(new SkillKeyBindings(Bar(), Skills).ApplyTo(source).Maintenance.HpMaintenanceRules[0].Key == "", "missing skill cannot press old slot");
        source.Maintenance.HpMaintenanceRules[0].SkillId = 0;
        var named = new SkillKeyBindings(Bar(), Skills).ApplyTo(source).Maintenance.HpMaintenanceRules[0];
        Check(named.SkillId == 102 && named.Key == "NumPad4", "legacy name binds identity and key together for cooldown confirmation");
        return Task.CompletedTask;
    }

    public static Task DecoderAsync()
    {
        var memory = new Memory();
        var decoded = new QuickbarDecoder(memory.Read).Read(Memory.Module);
        Check(decoded.Slots.Count == 24 && decoded.Slots[3].SkillId == 101 && decoded.Slots[12 + 4].SkillId == 102, "both current bars decoded");
        Check(decoded.Slots[0].ContentType == 0 && decoded.Slots[0].SkillId == 0, "empty preserved");
        foreach (var fault in new[] { "short", "page", "type", "mismatch", "changed", "pointer" })
        {
            memory = new Memory { Fault = fault };
            var rejected = false;
            try { new QuickbarDecoder(memory.Read).Read(Memory.Module); } catch (InvalidDataException) { rejected = true; }
            Check(rejected, "reject " + fault);
        }
        return Task.CompletedTask;
    }

    public static async Task LifecycleAsync()
    {
        var store = new DmaStableSnapshotStore(AionVmmSnapshotChannels.Registry);
        var context = new GameApiReadContext("a", 1, "Aion.bin", "device-a");
        var channel = AionVmmSnapshotChannels.Quickbar;
        var now = DateTimeOffset.Now;
        var value = Bar();
        var fail = OperationResult<QuickbarSnapshot>.Fail("partial");
        Check(!store.Resolve("a", channel, context, fail, now).Result.Success, "cold failure has no invented empty snapshot");
        store.Resolve("a", channel, context, OperationResult<QuickbarSnapshot>.Ok(value), now);
        Check(ReferenceEquals(store.Resolve("a", channel, context, fail, now.AddDays(2)).Result.Value, value), "failed capture holds official snapshot without TTL");
        Check(!store.Resolve("b", channel, context, fail, now).Result.Success, "sessions isolated");
        var empty = new QuickbarSnapshot(0, Array.Empty<QuickbarSlotSnapshot>());
        Check(ReferenceEquals(store.Resolve("a", channel, context, OperationResult<QuickbarSnapshot>.Ok(empty), now).Result.Value, empty), "valid empty replaces old bindings");
        var api = new FakeGameApi { Quickbar = value };
        var attempts = 0;
        api.QuickbarRead = () => ++attempts < 3 ? throw new IOException("partial") : value;
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var reader = new RoadhogSnapshotReader(new(), api, new InMemoryRoadhogLogger(), stop.Token);
        Check((await reader.ReadQuickbarAsync()).Value == value && attempts == 3, "cold retry is below business boundary");
    }

    public static async Task StartupAsync()
    {
        var api = new FakeGameApi { Quickbar = Bar(), Skills = Skills };
        var saved = new AccountConfig { AccountName = "account2", ScriptSettings = Settings() };
        var before = JsonSerializer.Serialize(saved);
        var log = new InMemoryRoadhogLogger();
        AccountWorkerContext Context() => new(saved.Clone(), api, log, new AccountRuntimeManager(log), new(), CancellationToken.None);
        var first = Context();
        await first.PrepareSkillBindingsAsync();
        api.Quickbar = Bar(2, 5);
        await first.PrepareSkillBindingsAsync();
        Check(api.QuickbarReadCount == 1 && first.Config.ScriptSettings!.Maintenance.HpMaintenanceRules[0].Key == "NumPad4", "one mapping per startup");
        var second = Context();
        await second.PrepareSkillBindingsAsync();
        Check(api.QuickbarReadCount == 2 && second.Config.ScriptSettings!.Maintenance.HpMaintenanceRules[0].Key == "NumPad6", "new session sees moved skill");
        Check(first.SkillBindings!.Resolve(101, "attack")!.Key == "D8" && second.SkillBindings!.Resolve(101, "attack")!.Key == "D3", "account sessions keep independent plans");
        Check(first.ForCleanup(saved.ScriptSettings!).Config.ScriptSettings!.Maintenance.HpMaintenanceRules[0].Key == "NumPad4", "manual cleanup cannot restore obsolete skill keys");
        Check(JsonSerializer.Serialize(saved) == before, "startup never rewrites saved settings");
        api.QuickbarRead = () => throw new IOException("not available yet");
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));
        var waiting = new AccountWorkerContext(saved.Clone(), api, log, new AccountRuntimeManager(log), new(), cancel.Token);
        var cancelled = false;
        try { await waiting.PrepareSkillBindingsAsync(); } catch (OperationCanceledException) { cancelled = true; }
        Check(cancelled && waiting.SkillBindings is null, "startup can stop while waiting for its first official binding map");
    }

    public static async Task ControllerAsync()
    {
        var api = new FakeGameApi { Quickbar = Bar(), Skills = Skills };
        var settings = new ScriptSettings();
        settings.Maintenance.SitMaintenanceEnabled = false;
        settings.SemiAuto.AttackKeyLoopEnabled = false;
        settings.Skills.ExecutionTree.Add(Node(102, "heal"));
        settings.Skills.ExecutionTree.Add(Node(101, "attack"));
        var log = new InMemoryRoadhogLogger();
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(12));
        AccountWorkerContext Context(ScriptSettings s) => new(new AccountConfig { ScriptSettings = s }, api, log, new AccountRuntimeManager(log), new(), stop.Token);
        var context = Context(settings);
        await context.PrepareSkillBindingsAsync();
        var input = new RecordingKeyboardInput();
        var controller = new SemiAutoCombatController(input);
        var plan = SemiAutoSkillPlan.FromSettings(context.Config.ScriptSettings!.Skills, context.SkillBindings);
        await controller.TickAsync(context, plan, new());
        Check(input.Keys.SequenceEqual(new[] { "NumPad4" }), "real combat controller follows first tree skill instead of first slot");

        settings = new ScriptSettings();
        settings.Maintenance.SitMaintenanceEnabled = false;
        settings.Maintenance.HpMaintenanceRules.Add(new() { SkillId = 102, SkillName = "heal", Key = "D1", BelowPercent = 50 });
        api.Player = api.Player with { CurrentHp = 40 };
        context = Context(settings);
        await context.PrepareSkillBindingsAsync();
        input = new RecordingKeyboardInput { AfterPress = _ =>
        {
            api.Player = api.Player with { CurrentHp = 100 };
            api.Skills = Skills.Select(s => s.SkillId == 102 ? s with { CooldownDuration = 1000, CooldownEndTime = 1000 } : s).ToArray();
        } };
        controller = new(input);
        await controller.TryHandleMaintenanceAsync(context, new(), api.Player, allowSitMaintenance: false);
        Check(input.Keys.SequenceEqual(new[] { "NumPad4" }), "real HP controller sends resolved key: " + string.Join(",", input.Keys));

        settings = new ScriptSettings();
        settings.Skills.SpiritmasterAutoSkillLogicEnabled = true;
        settings.Skills.Spiritmaster.SummonSkills.Add(new() { SkillId = 105, SkillName = "unbound", Key = "D1" });
        settings.Skills.Spiritmaster.SummonSkills.Add(new() { SkillId = 103, SkillName = "summon", Key = "D2" });
        api.Player = api.Player with { CharacterClassId = AionClassId.Spiritmaster };
        context = Context(settings);
        await context.PrepareSkillBindingsAsync();
        input = new(); controller = new(input);
        plan = SemiAutoSkillPlan.FromSettings(context.Config.ScriptSettings!.Skills, context.SkillBindings);
        await controller.EnsureSpiritmasterPetAsync(context, plan, new());
        Check(input.Keys.SequenceEqual(new[] { "NumPadAdd" }), "unbound speed does not shift summon or press obsolete key");
        Check(log.Entries.Any(e => e.EventName == "semi_auto.spiritmaster.key_pressed" && Convert.ToString(e.Fields["phase"]) == "summon_pet"), "summon phase retains second-slot meaning");
    }

    public static Task UiAsync()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                var log = new InMemoryRoadhogLogger();
                var settings = Settings();
                using var form = new AccountSettingsForm("account2", new RoadhogRuntime(new FakeGameApi(), log, new AccountRuntimeManager(log), null!),
                    new InMemoryAccountConfigStore(new AccountConfig { AccountName = "account2", ScriptSettings = settings }),
                    new InMemorySharedPathStore(), new InMemoryScriptProfileStore(), new RecordingFolderLauncher(), "test-paths");
                object? Get(string name) => typeof(AccountSettingsForm).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form);
                object? Call(string name, params object?[] values) => typeof(AccountSettingsForm).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form, values);
                typeof(AccountSettingsForm).GetField("previewSkillBindings", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(form, new SkillKeyBindings(Bar(), Skills));
                Call("RefreshAutomaticSkillDisplays");
                var hp = ((FlowLayoutPanel)Get("hpMaintenanceRuleList")!).Controls.OfType<Panel>().First();
                var hpButton = (Button)hp.Controls.Find("maintenanceRuleKeyButton", false).Single();
                Check(!hpButton.Enabled && hpButton.Text.Contains("Num4") && (string?)hpButton.Tag == "D1", "selected skill auto display keeps stored config intact");
                var mp = ((FlowLayoutPanel)Get("mpMaintenanceRuleList")!).Controls.OfType<Panel>().Last();
                Check(((Button)mp.Controls.Find("maintenanceRuleKeyButton", false).Single()).Enabled, "potion retains keyboard picker");
                Check(((Button)Get("teamGroupCleanseKeyButton")!).Enabled, "manual system/macro key remains selectable");
                using var spirit = (Form)Call("CreateSpiritmasterSettingsDialog")!;
                var capture = (SpiritmasterSkillSettings)Call("CaptureSpiritmasterSettings")!;
                Check(capture.SummonSkills.Count == 2 && capture.SummonSkills[0].SkillId == 103 && capture.SummonSkills[1].Key == "F7", "summon selections and manual macro roundtrip");
                Check(capture.OpeningAttackSkillId == 101 && capture.OpeningAttackKey == "F6", "opening row is separate from summon slots");
                Check(typeof(AccountSettingsForm).GetMethod("ShowKeyboardPicker", BindingFlags.Instance | BindingFlags.NonPublic) != null, "keyboard picker is retained");
                completion.SetResult();
            }
            catch (Exception ex) { completion.SetException(ex); }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); return completion.Task;
    }

    public static Task SpiritLayoutAsync()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                var log = new InMemoryRoadhogLogger();
                var source = Settings();
                source.Skills.Spiritmaster.DotSkills.Add(new() { SkillId = 101, SkillName = "attack" });
                source.Skills.Spiritmaster.PetHpMaintenanceRules[0].BelowPercent = 57;
                source.Skills.Spiritmaster.PetHpMaintenanceRules[0].CooldownMs = 12345;
                var store = new InMemoryAccountConfigStore(new AccountConfig { AccountName = "account2", ScriptSettings = source });
                using var form = new AccountSettingsForm("account2", new RoadhogRuntime(new FakeGameApi(), log, new AccountRuntimeManager(log), null!),
                    store, new InMemorySharedPathStore(), new InMemoryScriptProfileStore(), new RecordingFolderLauncher(), "test-paths");
                object? Get(string name) => typeof(AccountSettingsForm).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form);
                object? Call(string name, params object?[] args) => typeof(AccountSettingsForm).GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
                    .Single(m => m.Name == name && m.GetParameters().Length == args.Length).Invoke(form, args);
                typeof(AccountSettingsForm).GetField("previewSkillBindings", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(form, new SkillKeyBindings(Bar(), Skills));
                using var dialog = (Form)Call("CreateSpiritmasterSettingsDialog")!;
                dialog.ShowInTaskbar = false; dialog.StartPosition = FormStartPosition.Manual; dialog.Location = new(-32000, -32000);
                dialog.Show(); System.Windows.Forms.Application.DoEvents();
                var page = (FlowLayoutPanel)dialog.Controls.Find("spiritmasterSections", true).Single();
                var summon = (FlowLayoutPanel)Get("spiritmasterSummonRuleList")!;
                var titles = summon.Controls.OfType<Panel>().Select(row => row.Controls.OfType<Label>().First().Text).ToArray();
                Check(titles.SequenceEqual(new[] { "服从手印", "精灵召唤", "宝宝攻击" }), "user-facing skill and command labels");
                var delayInput = dialog.Controls.Find("spiritmasterOpeningAttackDelayTextBox", true).Single();
                Check(delayInput.Text == "0", "legacy spirit settings default to no extra delay");
                delayInput.Text = "500";
                var automatic = summon.Controls[0].Controls.OfType<Label>().Single(l => l.Name == "spiritmasterAutomaticKeyLabel");
                var manual = summon.Controls[1].Controls.OfType<Button>().Single();
                Check(automatic.Visible && automatic.Text.Contains("Num+") && manual.Visible && manual.Enabled, "automatic badge and manual button coexist");
                var combo = summon.Controls[0].Controls.OfType<RoundedComboBox>().Single();
                var originalSelection = combo.SelectedIndex;
                combo.SelectedIndex = 0;
                Check(!automatic.Visible && summon.Controls[0].Controls.OfType<Button>().Single().Visible, "clearing skill immediately restores manual action");
                combo.SelectedIndex = originalSelection;
                Check(automatic.Visible, "selecting skill restores automatic badge");

                void CheckLayout()
                {
                    System.Windows.Forms.Application.DoEvents();
                    var cards = page.Controls.Cast<Control>().OrderBy(c => c.Top).ToArray();
                    for (var i = 1; i < cards.Length; i++) Check(cards[i].Top >= cards[i - 1].Bottom, "cards never overlap");
                    foreach (var list in cards.SelectMany(c => c.Controls.OfType<FlowLayoutPanel>()))
                    {
                        Check(!list.AutoScroll, "only the whole dialog scrolls");
                        foreach (Control row in list.Controls)
                        {
                            Check(row.Bottom <= list.ClientSize.Height, "every rule remains reachable without nested scrolling");
                            foreach (Control control in row.Controls)
                                Check(control.Left >= 0 && control.Right <= row.ClientSize.Width && control.Bottom <= row.ClientSize.Height, "row control fits after resize: " + control.Name);
                        }
                    }
                    var save = dialog.Controls.Find("spiritmasterSaveButton", true).Single();
                    var saveBottom = dialog.PointToClient(save.PointToScreen(new(0, save.Height))).Y;
                    Check(saveBottom <= dialog.ClientSize.Height && save.Visible, "save action stays visible outside scroll area");
                    Check(!page.HorizontalScroll.Visible, "no horizontal scrollbar at supported window sizes");
                }
                CheckLayout();
                var hp = (FlowLayoutPanel)Get("spiritmasterPetHpRuleList")!;
                var initialHeight = hp.Parent!.Height;
                for (var i = 0; i < 4; i++) ((Button)dialog.Controls.Find("spiritmasterAdd_hp", true).Single()).PerformClick();
                Check(hp.Controls.Count == 5 && hp.Parent.Height > initialHeight, "add grows rule section");
                dialog.Size = dialog.MinimumSize;
                CheckLayout();
                Check(page.VerticalScroll.Visible, "smaller window scrolls expanded sections");
                while (hp.Controls.Count > 1)
                    hp.Controls[^1].Controls.OfType<Button>().Single(b => b.Name == "spiritmasterRemoveRuleButton").PerformClick();
                Check(hp.Parent.Height == initialHeight, "remove restores section height");
                dialog.ClientSize = new(1040, 780);
                CheckLayout();
                var saved = (SpiritmasterSkillSettings)Call("CaptureSpiritmasterSettings")!;
                Check(saved.OpeningAttackDelayMs == 500 && saved.Clone().OpeningAttackDelayMs == 500, "post-command delay survives capture and clone");
                Check(saved.SummonSkills[0].SkillId == 103 && saved.SummonSkills[1].Key == "F7" && saved.OpeningAttackSkillId == 101 && saved.OpeningAttackKey == "F6", "all three action identities survive layout editing");
                Check(saved.PetHpMaintenanceRules.Single().BelowPercent == 57 && saved.PetHpMaintenanceRules.Single().CooldownMs == 12345, "HP threshold and millisecond interval remain unchanged");
                var saveArgs = new object?[] { null };
                Check((bool)Call("SaveCurrentSettings", saveArgs)!, "settings save succeeds");
                var persisted = store.LoadAllAsync().GetAwaiter().GetResult().Value!.Single().ScriptSettings!.Skills.Spiritmaster;
                Check(JsonSerializer.Serialize(persisted) == JsonSerializer.Serialize(saved), "real save path retains spirit editor data");
                completion.SetResult();
            }
            catch (Exception ex) { completion.SetException(ex); }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); return completion.Task;
    }

    sealed class Memory
    {
        public const ulong Module = 0x180000000;
        readonly Dictionary<ulong, byte[]> blocks = new();
        int pageReads;
        public string Fault { get; set; } = "";
        public Memory()
        {
            blocks[Module + 0xD4AE0C] = BitConverter.GetBytes(0);
            var table = new byte[384];
            blocks[Module + 0xD61260] = table;
            blocks[Module + 0x6E2180] = BitConverter.GetBytes(16u).Concat(BitConverter.GetBytes(18u)).ToArray();
            for (var bar = 0; bar < 2; bar++)
            {
                ulong panel = 0x200000u + (ulong)bar * 0x10000;
                blocks[Module + 0xD63990 + (bar == 0 ? 16u : 18u) * 8] = BitConverter.GetBytes(panel);
                blocks[panel + 1344] = BitConverter.GetBytes(bar);
                var pointers = new byte[96]; blocks[panel + 1240] = pointers;
                for (var slot = 0; slot < 12; slot++)
                {
                    ulong control = panel + 0x1000 + (ulong)slot * 0x600;
                    BitConverter.GetBytes(control).CopyTo(pointers, slot * 8);
                    uint id = bar == 0 && slot == 3 ? 101u : bar == 1 && slot == 4 ? 102u : 0;
                    uint type = id == 0 ? 0u : 21u;
                    blocks[control + 952] = BitConverter.GetBytes(id).Concat(BitConverter.GetBytes(type)).ToArray();
                    BitConverter.GetBytes(type).CopyTo(table, (bar * 12 + slot) * 16);
                    BitConverter.GetBytes(id).CopyTo(table, (bar * 12 + slot) * 16 + 4);
                }
            }
        }
        public byte[] Read(ulong address, int count)
        {
            var value = blocks[address].ToArray();
            if (Fault == "short" && count == 384) return new byte[1];
            if (address == Module + 0xD4AE0C && (Fault == "page" || Fault == "changed" && ++pageReads > 1)) return BitConverter.GetBytes(Fault == "page" ? 10 : 1);
            if (address == 0x200000 + 0x1000 + 3 * 0x600 + 952)
            {
                if (Fault == "type") BitConverter.GetBytes(999u).CopyTo(value, 4);
                if (Fault == "mismatch") BitConverter.GetBytes(999u).CopyTo(value, 0);
            }
            if (Fault == "pointer" && address == Module + 0xD63990 + 16 * 8) return new byte[8];
            return value;
        }
    }
}
