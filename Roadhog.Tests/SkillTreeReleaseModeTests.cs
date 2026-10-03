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
            "missing mode defaults to legacy and never imports the old tree automatically");
        Check(legacy.Skills.ExecutionTree.Single().Children.Single().SkillId == 102, "legacy tree remains unchanged");
        var source = Settings();
        source.SkillTreeReleaseMode = SkillTreeReleaseMode.QuickbarAvailability;
        var original = JsonSerializer.Serialize(source);
        var copy = source.Clone();
        copy.QuickbarSkills.ExecutionTree[0].Children[0].Name = "changed-new";
        copy.Skills.ExecutionTree[0].Children[0].Name = "changed-old";
        Check(JsonSerializer.Serialize(source) == original, "both trees are deep clones with independent children");
        Check(copy.SkillTreeReleaseMode == SkillTreeReleaseMode.QuickbarAvailability, "clone retains selected engine");
        var options = new JsonSerializerOptions { Converters = { new JsonStringEnumConverter() } };
        var roundtrip = JsonSerializer.Deserialize<ScriptSettings>(JsonSerializer.Serialize(source, options), options)!;
        Check(roundtrip.SkillTreeReleaseMode == SkillTreeReleaseMode.QuickbarAvailability &&
            roundtrip.QuickbarSkills.ExecutionTree.Single().Children.Single().SkillId == 202 &&
            roundtrip.Skills.ExecutionTree.Single().Children.Single().SkillId == 102, "JSON retains engine and separate chain trees");
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
            settings.SkillTreeReleaseMode = SkillTreeReleaseMode.QuickbarAvailability;
            var oldTree = JsonSerializer.Serialize(settings.Skills.ExecutionTree);
            Check((await profiles.SaveAsync(new ScriptProfileDocument { Name = "skill-mode", Settings = settings })).Success, "profile saves new mode");
            Check((await configs.UpsertAsync(new AccountConfig { AccountName = "modes", ScriptSettings = settings })).Success, "account saves new mode");
            var profile = (await profiles.LoadAsync("skill-mode")).Value!.Settings;
            var account = (await configs.LoadAllAsync()).Value!.Single().ScriptSettings!;
            Check(profile.SkillTreeReleaseMode == SkillTreeReleaseMode.QuickbarAvailability &&
                account.SkillTreeReleaseMode == SkillTreeReleaseMode.QuickbarAvailability, "account and profile independently restore new mode");
            Check(JsonSerializer.Serialize(profile.Skills.ExecutionTree) == oldTree && JsonSerializer.Serialize(account.Skills.ExecutionTree) == oldTree,
                "both stores preserve old tree while new mode is selected");
            account.SkillTreeReleaseMode = SkillTreeReleaseMode.Legacy;
            Check((await configs.UpsertAsync(new AccountConfig { AccountName = "modes", ScriptSettings = account })).Success, "switch back persists");
            var back = (await configs.LoadAllAsync()).Value!.Single().ScriptSettings!;
            Check(back.SkillTreeReleaseMode == SkillTreeReleaseMode.Legacy && back.QuickbarSkills.ExecutionTree.Single().Children.Single().SkillId == 202,
                "returning to legacy retains the new tree for a later switch");
            Check(back.Maintenance.HpMaintenanceRules.Single().BelowPercent == 61 && back.Skills.Spiritmaster.PetBuffRules.Single().SkillId == 401,
                "persistence does not replace maintenance or existing spiritmaster configuration");
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    public static Task UiAsync()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                var source = Settings();
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
                var mode = (RoundedComboBox)Find("skillTreeReleaseModeCombo");
                var oldSelected = (TreeView)Find("selectedSkillTree");
                var newSelected = (TreeView)Find("quickbarSelectedSkillTree");
                var oldPanel = Find("autoSkillPanel");
                var newPanel = Find("quickbarSkillPanel");
                var opening = Find("openingSkillPanel");
                var weave = (RoundedCheckBox)Find("attackWeaveCheckBox");
                var weaveDelay = Find("attackWeaveDelayTextBox");
                void ToggleWeave() => typeof(RoundedCheckBox)
                    .GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(weave, new object[] { EventArgs.Empty });
                var tabs = (TabControl)oldPanel.Parent!.Parent!.Parent!;
                tabs.SelectedTab = (TabPage)oldPanel.Parent.Parent;
                form.ShowInTaskbar = false;
                form.StartPosition = FormStartPosition.Manual;
                form.Location = new(-32000, -32000);
                form.Show(); Application.DoEvents();
                Check(mode.SelectedIndex == 0 && oldPanel.Visible && !newPanel.Visible && newSelected.Nodes.Count == 0,
                    "legacy loads by default with no implicit new configuration");
                mode.SelectedIndex = 1; Application.DoEvents();
                Check(newPanel.Visible && !oldPanel.Visible && ReferenceEquals(opening.Parent, newPanel), "switch shows independent tree and shared opening editor");
                Check(!((Control)typeof(AccountSettingsForm)
                    .GetField("conditionSkillPreemptsChainCheckBox", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!).Enabled &&
                    !((Control)typeof(AccountSettingsForm)
                        .GetField("chainWindowPerLinkTextBox", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!).Enabled,
                    "new mode keeps legacy condition and chain-window options disabled");
                Check(weave.Enabled && weave.Checked && weaveDelay.Enabled && weaveDelay.Text == "725",
                    "new mode enables weaving and loads the saved delay");
                ToggleWeave();
                Check(weave.Enabled && !weave.Checked && !weaveDelay.Enabled,
                    "turning off weaving in new mode disables only its delay input");
                weaveDelay.Text = "530";
                mode.SelectedIndex = 0; Application.DoEvents();
                Check(weave.Enabled && !weave.Checked && !weaveDelay.Enabled && weaveDelay.Text == "530",
                    "switching to legacy retains the unchecked switch and draft delay");
                mode.SelectedIndex = 1; Application.DoEvents();
                Check(weave.Enabled && !weave.Checked && !weaveDelay.Enabled && weaveDelay.Text == "530",
                    "switching back to new mode retains the unchecked switch and draft delay");
                var saveArgs = new object?[] { null };
                Check((bool)Call("SaveCurrentSettings", saveArgs)!, "new mode with empty list saves");
                var saved = configs.LoadAllAsync().GetAwaiter().GetResult().Value!.Single().ScriptSettings!;
                Check(saved.SkillTreeReleaseMode == SkillTreeReleaseMode.QuickbarAvailability && saved.QuickbarSkills.ExecutionTree.Count == 0 &&
                    JsonSerializer.Serialize(saved.Skills.ExecutionTree) == oldTree, "saving new mode never normalizes or clears the untouched old tree");
                Check(!saved.SemiAuto.AttackWeaveEnabled && saved.SemiAuto.AttackWeaveDelayMs == 530,
                    "new-mode save persists the unchecked weaving switch and delay");
                ToggleWeave();
                Check(weave.Checked && weaveDelay.Enabled, "turning on weaving in new mode enables its delay input");
                ((Button)Find("quickbarCopyLegacyTreeButton")).PerformClick();
                Check(newSelected.Nodes.Count == 1 && newSelected.Nodes[0].Nodes.Count == 1 && oldSelected.Nodes.Count == 1,
                    "explicit copy retains chain structure without moving old controls");
                newSelected.Nodes.Clear();
                Call("PopulateSelectedSkillTreeFromConfig", newSelected, new List<SkillConfigNode> { Node(201, "new"), Node(202, "new-chain") });
                var oldBeforeRefresh = JsonSerializer.Serialize(Call("CaptureSkillTree", oldSelected.Nodes));
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
                Check(JsonSerializer.Serialize(Call("CaptureSkillTree", oldSelected.Nodes)) == oldBeforeRefresh &&
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
                Check(JsonSerializer.Serialize(Call("CaptureSkillTree", oldSelected.Nodes)) == oldBeforeRefresh &&
                    JsonSerializer.Serialize(Call("CaptureSkillTree", newSelected.Nodes)) == newBeforeRefresh,
                    "partial metadata retains both configured trees");
                newSelected.SelectedNode = newSelected.Nodes[1];
                newPanel.Controls.OfType<Button>().Single(button => button.Text == "上移").PerformClick();
                Check(((ScriptSettings)Call("CaptureScriptSettings")!).QuickbarSkills.ExecutionTree[0].SkillId == 202, "new priority order can be edited");
                Check((bool)Call("SaveCurrentSettings", new object?[] { null })!, "new priority order saves");
                saved = configs.LoadAllAsync().GetAwaiter().GetResult().Value!.Single().ScriptSettings!;
                Check(saved.QuickbarSkills.ExecutionTree.Select(node => node.SkillId).SequenceEqual(new uint[] { 202, 201 }) &&
                    JsonSerializer.Serialize(saved.Skills.ExecutionTree) == oldTree, "new priorities persist independently of the old chain tree");
                Check(saved.SemiAuto.AttackWeaveEnabled && saved.SemiAuto.AttackWeaveDelayMs == 530 && saved.Skills.OpeningSkill.SkillId == 101 &&
                    saved.Skills.OpeningSkill.ReleaseAll && saved.Skills.OpeningSkill.GetEffectiveSkills().Select(skill => skill.SkillId).SequenceEqual(new uint[] { 101, 201 }) &&
                    saved.Maintenance.HpMaintenanceRules.Single().BelowPercent == 61 && saved.Maintenance.MpMaintenanceRules.Single().SkillId == 302 &&
                    saved.Maintenance.StatusMaintenanceRules.Single().SkillId == 303 && saved.Maintenance.DpMaintenanceRules.Single().SkillId == 304 &&
                    saved.Team.Support.HealSkillRules.Single().SkillId == 601 && saved.Skills.Spiritmaster.PetHpMaintenanceRules.Single().SkillId == 402 &&
                    saved.Skills.Spiritmaster.PetBuffRules.Single().SkillId == 401 && saved.Team.Support.MentalCleanseSkillId == 501 &&
                    saved.Team.Support.PhysicalCleanseSkillId == 502 && saved.Team.Support.GroupCleanseSkillId == 503,
                    "weaving and shared opening, maintenance and spiritmaster settings retain their values");
                mode.SelectedIndex = 0; Application.DoEvents();
                Check(oldPanel.Visible && !newPanel.Visible && ReferenceEquals(opening.Parent, oldPanel) && Find("attackWeaveCheckBox").Enabled,
                    "switching back restores old controls and the same opening editor");
                Check(weave.Checked && weaveDelay.Enabled && weaveDelay.Text == "530",
                    "switching back retains the enabled weaving switch and delay");
                Check((bool)Call("SaveCurrentSettings", new object?[] { null })!, "return to old mode saves");
                saved = configs.LoadAllAsync().GetAwaiter().GetResult().Value!.Single().ScriptSettings!;
                Check(saved.SkillTreeReleaseMode == SkillTreeReleaseMode.Legacy && saved.QuickbarSkills.ExecutionTree[0].SkillId == 202,
                    "legacy save preserves hidden new priorities");
                Call("ApplyScriptSettings", saved);
                mode.SelectedIndex = 1; Application.DoEvents();
                Check(newSelected.Nodes.Count == 2 && opening.Parent == newPanel, "reload and switch restore both trees");
                Check(weave.Enabled && weave.Checked && weaveDelay.Enabled && weaveDelay.Text == "530",
                    "reload and switch restore weaving settings in new mode");
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
