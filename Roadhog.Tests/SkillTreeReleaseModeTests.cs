using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Forms;
using Roadhog;
using Roadhog.Application;
using Roadhog.Application.Workers;
using Roadhog.Core.Accounts;
using Roadhog.Core.Diagnostics;
using Roadhog.Core.Model;
using Roadhog.Core.Profiles;
using Roadhog.Infrastructure.Config;
using Roadhog.Infrastructure.Profiles;

internal static class SkillTreeReleaseModeTests
{
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static SkillConfigNode Node(uint id, string name) => new()
    {
        SkillId = id, Name = name, BaseName = name, Type = "主动技能"
    };

    private static ScriptSettings Settings()
    {
        var settings = new ScriptSettings { ProfileName = "skill-mode" };
        settings.Skills.ExecutionTree.Add(Node(101, "old"));
        settings.Skills.ExecutionTree[0].Children.Add(Node(102, "old-chain"));
        settings.QuickbarSkills.ExecutionTree.Add(Node(201, "new"));
        settings.QuickbarSkills.ExecutionTree[0].Children.Add(Node(202, "new-chain"));
        settings.Maintenance.HpMaintenanceRules.Add(new() { BelowPercent = 61, SkillId = 301, SkillName = "heal", Key = "F1" });
        settings.Skills.Spiritmaster.PetBuffRules.Add(new() { SkillId = 401, SkillName = "pet-buff", Key = "F2" });
        return settings;
    }

    public static Task CompatibilityAsync()
    {
        var legacy = JsonSerializer.Deserialize<ScriptSettings>("""
            {"Skills":{"ExecutionTree":[{"SkillId":101,"Name":"old","Children":[{"SkillId":102,"Name":"old-chain"}]}]}}
            """)!;
        Check(legacy.SkillTreeReleaseMode == SkillTreeReleaseMode.Legacy && legacy.QuickbarSkills.ExecutionTree.Count == 0,
            "raw missing mode retains the compatibility marker until load normalization");
        Check(legacy.QuickbarSkills.TriggerConditionSkillsPreemptChain,
            "missing priority switch defaults to checked for existing configurations");
        var migrated = legacy.Clone();
        Check(migrated.SkillTreeReleaseMode == SkillTreeReleaseMode.QuickbarAvailability &&
            migrated.QuickbarSkills.ExecutionTree.Single().Children.Single().SkillId == 102,
            "normalizing a legacy-only document imports its complete attack chain into the sole engine");
        Check(legacy.SkillTreeReleaseMode == SkillTreeReleaseMode.Legacy && legacy.QuickbarSkills.ExecutionTree.Count == 0 &&
            legacy.Skills.ExecutionTree.Single().Children.Single().SkillId == 102, "normalization leaves the source archive unchanged");
        var source = Settings();
        source.QuickbarSkills.TriggerConditionSkillsPreemptChain = false;
        source.SkillTreeReleaseMode = SkillTreeReleaseMode.QuickbarAvailability;
        var original = JsonSerializer.Serialize(source);
        var copy = source.Clone();
        copy.QuickbarSkills.ExecutionTree[0].Children[0].Name = "changed-new";
        copy.Skills.ExecutionTree[0].Children[0].Name = "changed-old";
        Check(JsonSerializer.Serialize(source) == original, "both trees are deep clones with independent children");
        Check(copy.SkillTreeReleaseMode == SkillTreeReleaseMode.QuickbarAvailability, "clone retains selected engine");
        Check(!copy.QuickbarSkills.TriggerConditionSkillsPreemptChain, "clone retains an explicitly unchecked priority switch");
        var options = new JsonSerializerOptions { Converters = { new JsonStringEnumConverter() } };
        var roundtrip = JsonSerializer.Deserialize<ScriptSettings>(JsonSerializer.Serialize(source, options), options)!;
        Check(roundtrip.SkillTreeReleaseMode == SkillTreeReleaseMode.QuickbarAvailability &&
            roundtrip.QuickbarSkills.ExecutionTree.Single().Children.Single().SkillId == 202 &&
            roundtrip.Skills.ExecutionTree.Single().Children.Single().SkillId == 102, "JSON retains engine and separate chain trees");
        Check(!roundtrip.QuickbarSkills.TriggerConditionSkillsPreemptChain, "JSON retains unchecked priority instead of restoring the default");
        source.QuickbarSkills = null!;
        Check(source.Clone().QuickbarSkills.ExecutionTree.Count == 0, "null new configuration normalizes to empty");
        return Task.CompletedTask;
    }

    public static async Task PersistenceAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), "RoadhogSkillModes-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var configs = new JsonAccountConfigStore(Path.Combine(directory, "accounts.json"));
            var profiles = new JsonScriptProfileStore(Path.Combine(directory, "profiles"));
            var settings = Settings();
            settings.QuickbarSkills.TriggerConditionSkillsPreemptChain = false;
            settings.SkillTreeReleaseMode = SkillTreeReleaseMode.QuickbarAvailability;
            var oldTree = JsonSerializer.Serialize(settings.Skills.ExecutionTree);
            Check((await profiles.SaveAsync(new ScriptProfileDocument { Name = "skill-mode", Settings = settings })).Success, "profile saves new mode");
            Check((await configs.UpsertAsync(new AccountConfig { AccountName = "modes", ScriptSettings = settings })).Success, "account saves new mode");
            var profile = (await profiles.LoadAsync("skill-mode")).Value!.Settings;
            var account = (await configs.LoadAllAsync()).Value!.Single().ScriptSettings!;
            Check(profile.SkillTreeReleaseMode == SkillTreeReleaseMode.QuickbarAvailability &&
                account.SkillTreeReleaseMode == SkillTreeReleaseMode.QuickbarAvailability, "account and profile independently restore new mode");
            Check(!profile.QuickbarSkills.TriggerConditionSkillsPreemptChain && !account.QuickbarSkills.TriggerConditionSkillsPreemptChain,
                "account and profile stores retain the unchecked priority switch");
            Check(JsonSerializer.Serialize(profile.Skills.ExecutionTree) == oldTree && JsonSerializer.Serialize(account.Skills.ExecutionTree) == oldTree,
                "both stores preserve old tree while new mode is selected");
            account.SkillTreeReleaseMode = SkillTreeReleaseMode.Legacy;
            Check((await configs.UpsertAsync(new AccountConfig { AccountName = "modes", ScriptSettings = account })).Success, "old mode marker normalizes on save");
            var back = (await configs.LoadAllAsync()).Value!.Single().ScriptSettings!;
            Check(back.SkillTreeReleaseMode == SkillTreeReleaseMode.QuickbarAvailability && back.QuickbarSkills.ExecutionTree.Single().Children.Single().SkillId == 202,
                "a stale legacy marker cannot overwrite existing new priorities");
            Check(back.Maintenance.HpMaintenanceRules.Single().BelowPercent == 61 && back.Skills.Spiritmaster.PetBuffRules.Single().SkillId == 401,
                "persistence does not replace maintenance or existing spiritmaster configuration");
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    public static Task ArchivedModesUiAsync()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                foreach (var legacyMode in new[] { SkillConfigurationMode.ManualMapping, SkillConfigurationMode.SystemClassification })
                {
                    var source = Settings();
                    source.Skills.Mode = legacyMode;
                    source.QuickbarSkills.ExecutionTree.Clear();
                    source.Skills.SystemExecutionTree.Add(Node(701, "system-attack"));
                    source.Skills.ManualMappings.Add(new() { SkillName = "manual-attack", SkillType = "主动技能", Key = "F1" });
                    source.Skills.KeyOrder = new() { "F8", "NumPad1" };
                    source.Skills.TriggerPrefixMode = "saved-prefix-mode";
                    source.Skills.SpiritmasterAutoSkillLogicEnabled = true;
                    source.Skills.OpeningSkill.Skills = new();
                    var archive = JsonSerializer.Serialize(source.Skills);
                    var store = new InMemoryAccountConfigStore(new AccountConfig { AccountName = "archive-ui", ScriptSettings = source });
                    var log = new InMemoryRoadhogLogger();
                    using var form = new AccountSettingsForm("archive-ui",
                        new RoadhogRuntime(new FakeGameApi(), log, new AccountRuntimeManager(log), null!),
                        store, new InMemorySharedPathStore(), new InMemoryScriptProfileStore(), new RecordingFolderLauncher(), "test-paths");
                    var selected = (TreeView)form.Controls.Find("quickbarSelectedSkillTree", true).Single();
                    Check(selected.Nodes.Count == 1, "old alternate mode has a migrated editable attack root");
                    var spirit = (RoundedCheckBox)typeof(AccountSettingsForm)
                        .GetField("spiritmasterAutoSkillCheckBox", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
                    Check(spirit.Enabled && spirit.Checked, "archived manual/system mode cannot disable the shared spiritmaster switch");
                    var args = new object?[] { null };
                    Check((bool)typeof(AccountSettingsForm).GetMethod("SaveCurrentSettings", BindingFlags.Instance | BindingFlags.NonPublic)!
                        .Invoke(form, args)!, "migrated alternate mode UI saves");
                    var saved = store.LoadAllAsync().GetAwaiter().GetResult().Value!.Single().ScriptSettings!;
                    Check(saved.SkillTreeReleaseMode == SkillTreeReleaseMode.QuickbarAvailability &&
                        JsonSerializer.Serialize(saved.Skills) == archive,
                        "single-editor save retains mode, key order, prefix, old trees and all shared skill settings");
                    Check(legacyMode == SkillConfigurationMode.ManualMapping
                        ? saved.QuickbarSkills.ExecutionTree.Single().Name == "manual-attack"
                        : saved.QuickbarSkills.ExecutionTree.Single().SkillId == 701,
                        "single-editor save retains the migrated manual/system skill identity");
                    typeof(AccountSettingsForm).GetMethod("CloseSpiritmasterSettingsDialog", BindingFlags.Instance | BindingFlags.NonPublic)!
                        .Invoke(form, null);
                }
                completion.SetResult();
            }
            catch (Exception exception) { completion.SetException(new InvalidOperationException(exception.ToString(), exception)); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }

    public static Task UiAsync()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                var source = Settings();
                source.SkillTreeReleaseMode = SkillTreeReleaseMode.QuickbarAvailability;
                source.QuickbarSkills.ExecutionTree.Clear();
                source.Skills.ExecutionTree[0].Name = string.Empty; // Loading formats this for display; new-mode saving must retain the original configuration.
                source.SemiAuto.AttackWeaveEnabled = true;
                source.SemiAuto.AttackWeaveDelayMs = 725;
                source.Skills.OpeningSkill = new() { Enabled = true, SkillId = 101, SkillName = "old", Key = "F1" };
                source.Maintenance.MpMaintenanceRules.Add(new() { BelowPercent = 62, SkillId = 302, SkillName = "saved-mp", Key = "F4" });
                source.Maintenance.StatusMaintenanceRules.Add(new() { SkillId = 303, SkillName = "saved-status", Key = "F5", AbnormalStatusId = 1 });
                source.Maintenance.DpMaintenanceRules.Add(new() { SkillId = 304, SkillName = "saved-dp", Key = "F6", RequiredDp = 2000 });
                source.Team.Support.HealSkillRules.Add(new() { BelowPercent = 63, SkillId = 601, SkillName = "saved-team-heal", Key = "F7" });
                source.Skills.Spiritmaster.PetHpMaintenanceRules.Add(new() { BelowPercent = 64, SkillId = 402, SkillName = "saved-pet-hp", Key = "F8" });
                source.Team.Support.MentalCleanseSkillId = 501;
                source.Team.Support.MentalCleanseSkillName = "saved-mental-cleanse";
                source.Team.Support.PhysicalCleanseSkillId = 502;
                source.Team.Support.PhysicalCleanseSkillName = "saved-physical-cleanse";
                source.Team.Support.GroupCleanseSkillId = 503;
                source.Team.Support.GroupCleanseSkillName = "saved-group-cleanse";
                var oldTree = JsonSerializer.Serialize(source.Skills.ExecutionTree);
                var configs = new InMemoryAccountConfigStore(new AccountConfig { AccountName = "skill-modes", ScriptSettings = source });
                var profiles = new InMemoryScriptProfileStore();
                var log = new InMemoryRoadhogLogger();
                var api = new FakeGameApi
                {
                    Skills = new[]
                    {
                        new SkillSnapshot(201, "new", 1, 1, "new", 1, false, 0, 0, XmlActivation: "Active"),
                        new SkillSnapshot(1271, "裂破击 III", 3, 3, "裂破击", 3, false, 30000, 0, XmlActivation: "Active", XmlCounterSkill: "Parry")
                    },
                    Quickbar = new(0, new[]
                    {
                        new QuickbarSlotSnapshot(SkillQuickbar.Main, 4, 21, 201),
                        new QuickbarSlotSnapshot(SkillQuickbar.Main, 5, 21, 1227),
                        new QuickbarSlotSnapshot(SkillQuickbar.Main, 0, 1, 999) // Items must not cause skill metadata reads.
                    })
                };
                var runtime = DispatchProxy.Create<IRoadhogRuntime, ExactRankPreviewRuntime>();
                var previewRuntime = (ExactRankPreviewRuntime)runtime;
                previewRuntime.Inner = new RoadhogRuntime(api, log, new AccountRuntimeManager(log), null!);
                previewRuntime.ExactSkills = new[]
                {
                    new SkillSnapshot(1227, "裂破击 I", 1, 1, "裂破击", 1, false, 30000, 0, XmlActivation: "Active", XmlCounterSkill: "Parry")
                };
                using var form = new AccountSettingsForm("skill-modes", runtime,
                    configs, new InMemorySharedPathStore(), profiles, new RecordingFolderLauncher(), "test-paths");
                object? Call(string name, params object?[] args) => typeof(AccountSettingsForm)
                    .GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic)
                    .Single(method => method.Name == name && method.GetParameters().Length == args.Length).Invoke(form, args);
                Control Find(string name) => form.Controls.Find(name, true).Single();
                var newSelected = (TreeView)Find("quickbarSelectedSkillTree");
                var newPanel = Find("quickbarSkillPanel");
                var opening = Find("openingSkillPanel");
                var weave = (RoundedCheckBox)Find("attackWeaveCheckBox");
                var priority = (RoundedCheckBox)Find("triggerConditionSkillsPreemptChainCheckBox");
                var weaveDelay = Find("attackWeaveDelayTextBox");
                void ToggleWeave() => typeof(RoundedCheckBox)
                    .GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(weave, new object[] { EventArgs.Empty });
                var tabs = (TabControl)newPanel.Parent!.Parent!.Parent!;
                tabs.SelectedTab = (TabPage)newPanel.Parent.Parent;
                form.ShowInTaskbar = false;
                form.StartPosition = FormStartPosition.Manual;
                form.Location = new(-32000, -32000);
                form.Show(); Application.DoEvents();
                Check(newPanel.Visible && newSelected.Nodes.Count == 0 && ReferenceEquals(opening.Parent, newPanel),
                    "the sole editor retains an explicitly empty new tree and the shared opening section");
                foreach (var retired in new[] { "skillTreeReleaseModeCombo", "autoSkillPanel", "manualSkillPanel",
                    "systemSkillPanel", "selectedSkillTree" })
                    Check(form.Controls.Find(retired, true).Length == 0, "retired editor is not constructed: " + retired);
                Check(Find("skillReleaseModeLabel").Text == "技能栏可用", "the supported engine has one static label");
                Check(typeof(AccountSettingsForm).GetField("conditionSkillPreemptsChainCheckBox", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .GetValue(form) is null && typeof(AccountSettingsForm)
                    .GetField("chainWindowPerLinkTextBox", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form) is null,
                    "retired condition preemption and chain-window controls are not constructed");
                Check(weave.Enabled && weave.Checked && weaveDelay.Enabled && weaveDelay.Text == "725",
                    "new mode enables weaving and loads the saved delay");
                Check(priority.Enabled && priority.Visible && priority.Checked &&
                    priority.Parent == weave.Parent && !priority.Bounds.IntersectsWith(weaveDelay.Bounds),
                    "priority switch defaults to checked and fits beside weaving without overlap");
                priority.Checked = false;
                ToggleWeave();
                Check(weave.Enabled && !weave.Checked && !weaveDelay.Enabled,
                    "turning off weaving in new mode disables only its delay input");
                weaveDelay.Text = "530";
                Application.DoEvents();
                Check(weave.Enabled && !weave.Checked && !weaveDelay.Enabled && weaveDelay.Text == "530",
                    "the sole editor retains the unchecked switch and draft delay");
                var saveArgs = new object?[] { null };
                Check((bool)Call("SaveCurrentSettings", saveArgs)!, "new mode with empty list saves");
                var saved = configs.LoadAllAsync().GetAwaiter().GetResult().Value!.Single().ScriptSettings!;
                Check(saved.SkillTreeReleaseMode == SkillTreeReleaseMode.QuickbarAvailability && saved.QuickbarSkills.ExecutionTree.Count == 0 &&
                    JsonSerializer.Serialize(saved.Skills.ExecutionTree) == oldTree, "saving new mode never normalizes or clears the untouched old tree");
                Check(!saved.SemiAuto.AttackWeaveEnabled && saved.SemiAuto.AttackWeaveDelayMs == 530,
                    "new-mode save persists the unchecked weaving switch and delay");
                Check(!saved.QuickbarSkills.TriggerConditionSkillsPreemptChain, "UI save retains the unchecked priority switch");
                Call("ApplyQuickbarSkillSettings", saved);
                Check(!priority.Checked, "UI restores the saved unchecked priority switch");
                priority.Checked = true;
                ToggleWeave();
                Check(weave.Checked && weaveDelay.Enabled, "turning on weaving in new mode enables its delay input");
                var configuredRefresh = (Button)Find("quickbarRefreshConfiguredSkillsButton");
                Check(configuredRefresh.Text == "刷新全部已配置技能" && configuredRefresh.Visible && configuredRefresh.Enabled &&
                    form.Controls.Find("quickbarCopyLegacyTreeButton", true).Length == 0,
                    "new mode exposes the shared configured-refresh action instead of a copy button");
                Check(newSelected.Nodes.Count == 0 &&
                    JsonSerializer.Serialize(((ScriptSettings)Call("CaptureScriptSettings")!).Skills.ExecutionTree) == oldTree,
                    "showing the refresh action preserves an empty active tree and its untouched archive");
                Call("PopulateSelectedSkillTreeFromConfig", newSelected, new List<SkillConfigNode> { Node(201, "new"), Node(202, "new-chain") });
                var oldBeforeRefresh = JsonSerializer.Serialize(((ScriptSettings)Call("CaptureScriptSettings")!).Skills.ExecutionTree);
                var newBeforeRefresh = JsonSerializer.Serialize(Call("CaptureSkillTree", newSelected.Nodes));
                uint ComboSkillId(object item) => (uint)item.GetType().GetProperty("SkillId")!.GetValue(item)!;
                uint[] ComboSkillIds(RoundedComboBox combo) => combo.Items.Cast<object>().Select(ComboSkillId).ToArray();
                var openingRows = (FlowLayoutPanel)Find("openingSkillRows");
                var firstOpeningCombo = (RoundedComboBox)openingRows.Controls[0].Controls["openingSkillCombo"]!;
                Check(ComboSkillIds(firstOpeningCombo).SequenceEqual(new uint[] { 0, 101 }),
                    "before refresh the opening list contains only the placeholder and saved missing skill");
                Call("AddOpeningSkillRow", new OpeningSkillEntryConfig { SkillId = 201, SkillName = "new", Key = "F3" });
                ((RoundedCheckBox)Find("openingSkillReleaseAllCheckBox")).Checked = true;
                // Pet rule controls are created in the separate settings dialog, not in the account form.
                using var spiritmasterDialog = (Form)Call("CreateSpiritmasterSettingsDialog")!;
                var sharedCombos = new List<(RoundedComboBox Combo, uint SelectedId, string Kind)>();
                foreach (var (fieldName, selectedId, kind) in new[]
                {
                    ("hpMaintenanceRuleList", 301u, "HP maintenance"),
                    ("mpMaintenanceRuleList", 302u, "MP maintenance"),
                    ("statusMaintenanceRuleList", 303u, "status maintenance"),
                    ("dpMaintenanceRuleList", 304u, "DP maintenance"),
                    ("teamHealSkillRuleList", 601u, "team healing"),
                    ("spiritmasterPetHpRuleList", 402u, "pet HP maintenance"),
                    ("spiritmasterPetBuffRuleList", 401u, "pet buff maintenance")
                })
                {
                    if (typeof(AccountSettingsForm).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)
                        is not FlowLayoutPanel list)
                        throw new InvalidOperationException(kind + " fixture must create its rule list");
                    var combo = list.Controls.OfType<Panel>().SelectMany(row => row.Controls.OfType<RoundedComboBox>())
                        .Where(combo => combo.Name is "maintenanceRuleSkillCombo" or "spiritmasterRuleSkillCombo")
                        .Single(combo => ComboSkillId(combo.SelectedItem!) == selectedId);
                    sharedCombos.Add((combo, selectedId, kind));
                }
                foreach (var (fieldName, selectedId, kind) in new[]
                {
                    ("teamMentalCleanseSkillCombo", 501u, "mental cleanse"),
                    ("teamPhysicalCleanseSkillCombo", 502u, "physical cleanse"),
                    ("teamGroupCleanseSkillCombo", 503u, "group cleanse")
                })
                {
                    var combo = (RoundedComboBox)typeof(AccountSettingsForm)
                        .GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
                    Check(ComboSkillId(combo.SelectedItem!) == selectedId, kind + " loads its saved missing skill");
                    sharedCombos.Add((combo, selectedId, kind));
                }
                var draftBeforeRefresh = JsonSerializer.Serialize(Call("CaptureScriptSettings"));
                var storedBeforeRefresh = JsonSerializer.Serialize(configs.LoadAllAsync().GetAwaiter().GetResult().Value!.Single().ScriptSettings);
                void CheckSharedCandidates()
                {
                    foreach (Control row in openingRows.Controls)
                    {
                        var combo = (RoundedComboBox)row.Controls["openingSkillCombo"]!;
                        Check(ComboSkillIds(combo).Contains(201u), "quickbar refresh repopulates every shared opening list with learned skills");
                    }
                    Check(ComboSkillId(firstOpeningCombo.SelectedItem!) == 101 && ComboSkillIds(firstOpeningCombo).Count(id => id == 101) == 1,
                        "opening refresh keeps the missing saved ID selected exactly once");
                    foreach (var (combo, selectedId, kind) in sharedCombos)
                        Check(ComboSkillIds(combo).Contains(201u) && ComboSkillId(combo.SelectedItem!) == selectedId &&
                            ComboSkillIds(combo).Count(id => id == selectedId) == 1,
                            "quickbar refresh updates " + kind + " candidates while preserving the saved ID exactly once");
                    Check(JsonSerializer.Serialize(Call("CaptureScriptSettings")) == draftBeforeRefresh,
                        "candidate refresh retains draft opening order, selections, keys, switches and both configured trees");
                    Check(JsonSerializer.Serialize(configs.LoadAllAsync().GetAwaiter().GetResult().Value!.Single().ScriptSettings) == storedBeforeRefresh,
                        "candidate refresh does not persist any shared skill or tree drafts");
                }
                ((Task)Call("RefreshQuickbarSkillCandidatesAsync", Find("quickbarRefreshSkillsButton"))!).GetAwaiter().GetResult();
                CheckSharedCandidates();
                Check(previewRuntime.RequestedIds.SequenceEqual(new uint[] { 1227 }), "new UI requests the exact bound lower rank and ignores item IDs");
                var candidates = (List<SkillConfigNode>)Call("CaptureSkillTree", ((TreeView)Find("quickbarAvailableSkillTree")).Nodes)!;
                IEnumerable<SkillConfigNode> Flatten(IEnumerable<SkillConfigNode> nodes) => nodes.SelectMany(node => new[] { node }.Concat(Flatten(node.Children)));
                var rankCandidates = Flatten(candidates).Where(node => node.SkillId is 1227 or 1271).ToArray();
                Check(rankCandidates.Select(node => node.SkillId).OrderBy(id => id).SequenceEqual(new uint[] { 1227, 1271 }) &&
                    rankCandidates.Single(node => node.SkillId == 1227).Name != rankCandidates.Single(node => node.SkillId == 1271).Name,
                    "actual I rank and highest III rank remain separate selectable identities");
                Check(((string)Call("AutomaticKeyText", 1227u, "裂破击 I")!).Contains("6") &&
                    (string)Call("AutomaticKeyText", 1271u, "裂破击 III")! == "未放入两栏",
                    "slot 6 binds actual rank I and never substitutes rank III");
                Check(JsonSerializer.Serialize(((ScriptSettings)Call("CaptureScriptSettings")!).Skills.ExecutionTree) == oldBeforeRefresh &&
                    JsonSerializer.Serialize(Call("CaptureSkillTree", newSelected.Nodes)) == newBeforeRefresh, "new refresh neither deletes nor rewrites either configured tree");
                Check(configs.LoadAllAsync().GetAwaiter().GetResult().Value!.Single().ScriptSettings!.QuickbarSkills.ExecutionTree.Count == 0,
                    "refresh does not save draft trees automatically");
                previewRuntime.ExactSkills = Array.Empty<SkillSnapshot>();
                ((Task)Call("RefreshQuickbarSkillCandidatesAsync", Find("quickbarRefreshSkillsButton"))!).GetAwaiter().GetResult();
                var partialStatus = (Label)typeof(AccountSettingsForm)
                    .GetField("quickbarSkillStatusLabel", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
                Check(partialStatus.Text.Contains("部分栏位未取得技能详情"), "missing exact rank reports partial metadata separately from candidate refresh");
                CheckSharedCandidates();
                candidates = (List<SkillConfigNode>)Call("CaptureSkillTree", ((TreeView)Find("quickbarAvailableSkillTree")).Nodes)!;
                Check(!Flatten(candidates).Any(node => node.SkillId == 1227) && Flatten(candidates).Any(node => node.SkillId == 1271),
                    "partial metadata never relabels the learned highest rank as the missing bound lower rank");
                Check(JsonSerializer.Serialize(((ScriptSettings)Call("CaptureScriptSettings")!).Skills.ExecutionTree) == oldBeforeRefresh &&
                    JsonSerializer.Serialize(Call("CaptureSkillTree", newSelected.Nodes)) == newBeforeRefresh,
                    "partial metadata retains both configured trees");
                newSelected.SelectedNode = newSelected.Nodes[1];
                newPanel.Controls.OfType<Button>().Single(button => button.Text == "上移").PerformClick();
                Check(((ScriptSettings)Call("CaptureScriptSettings")!).QuickbarSkills.ExecutionTree[0].SkillId == 202, "new priority order can be edited");
                Check((bool)Call("SaveCurrentSettings", new object?[] { null })!, "new priority order saves");
                saved = configs.LoadAllAsync().GetAwaiter().GetResult().Value!.Single().ScriptSettings!;
                Check(saved.QuickbarSkills.ExecutionTree.Select(node => node.SkillId).SequenceEqual(new uint[] { 202, 201 }) &&
                    JsonSerializer.Serialize(saved.Skills.ExecutionTree) == oldTree, "new priorities persist independently of the old chain tree");
                Check(saved.QuickbarSkills.TriggerConditionSkillsPreemptChain, "UI saves the priority switch after checking it again");
                Check(saved.SemiAuto.AttackWeaveEnabled && saved.SemiAuto.AttackWeaveDelayMs == 530 && saved.Skills.OpeningSkill.SkillId == 101 &&
                    saved.Skills.OpeningSkill.ReleaseAll && saved.Skills.OpeningSkill.GetEffectiveSkills().Select(skill => skill.SkillId).SequenceEqual(new uint[] { 101, 201 }) &&
                    saved.Maintenance.HpMaintenanceRules.Single().BelowPercent == 61 && saved.Maintenance.MpMaintenanceRules.Single().SkillId == 302 &&
                    saved.Maintenance.StatusMaintenanceRules.Single().SkillId == 303 && saved.Maintenance.DpMaintenanceRules.Single().SkillId == 304 &&
                    saved.Team.Support.HealSkillRules.Single().SkillId == 601 && saved.Skills.Spiritmaster.PetHpMaintenanceRules.Single().SkillId == 402 &&
                    saved.Skills.Spiritmaster.PetBuffRules.Single().SkillId == 401 && saved.Team.Support.MentalCleanseSkillId == 501 &&
                    saved.Team.Support.PhysicalCleanseSkillId == 502 && saved.Team.Support.GroupCleanseSkillId == 503,
                    "weaving and shared opening, maintenance and spiritmaster settings retain their values");
                Check((bool)Call("SaveCurrentSettings", new object?[] { null })!, "repeated single-editor save succeeds");
                saved = configs.LoadAllAsync().GetAwaiter().GetResult().Value!.Single().ScriptSettings!;
                Check(saved.SkillTreeReleaseMode == SkillTreeReleaseMode.QuickbarAvailability &&
                    saved.QuickbarSkills.ExecutionTree[0].SkillId == 202 && JsonSerializer.Serialize(saved.Skills.ExecutionTree) == oldTree,
                    "repeated saves preserve active priorities and the retired tree archive");
                Call("ApplyScriptSettings", saved); Application.DoEvents();
                Check(newSelected.Nodes.Count == 2 && opening.Parent == newPanel, "reload restores the sole tree and shared editor");
                Check(weave.Enabled && weave.Checked && weaveDelay.Enabled && weaveDelay.Text == "530",
                    "reload restores weaving settings in the sole editor");
                form.Size = form.MinimumSize; Application.DoEvents();
                foreach (var control in new[] { (Control)newSelected, Find("quickbarAvailableSkillTree"), opening })
                    Check(control.Right <= newPanel.Width && control.Bottom <= newPanel.Height, "new controls fit the scrollable panel at minimum window size");
                var preview = Environment.GetEnvironmentVariable("ROADHOG_QUICKBAR_SKILL_PREVIEW");
                if (!string.IsNullOrWhiteSpace(preview))
                {
                    form.ClientSize = new(1000, 800); Application.DoEvents();
                    using var bitmap = new System.Drawing.Bitmap(form.Width, form.Height);
                    form.DrawToBitmap(bitmap, new System.Drawing.Rectangle(System.Drawing.Point.Empty, bitmap.Size));
                    bitmap.Save(preview);
                }
                completion.SetResult();
            }
            catch (Exception exception) { completion.SetException(new InvalidOperationException(exception.ToString(), exception)); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }

    public class ExactRankPreviewRuntime : DispatchProxy
    {
        public IRoadhogRuntime Inner { get; set; } = null!;
        public IReadOnlyList<SkillSnapshot> ExactSkills { get; set; } = Array.Empty<SkillSnapshot>();
        public IReadOnlyList<uint> RequestedIds { get; private set; } = Array.Empty<uint>();

        protected override object? Invoke(MethodInfo? method, object?[]? arguments)
        {
            if (method!.Name == nameof(IRoadhogRuntime.RefreshSkillsByIdsAsync))
            {
                RequestedIds = ((IReadOnlyCollection<uint>)arguments![0]!).ToArray();
                return Task.FromResult<IReadOnlyList<SkillSnapshot>>(ExactSkills.Where(skill => RequestedIds.Contains(skill.SkillId)).ToArray());
            }
            return method.Invoke(Inner, arguments);
        }
    }
}
