using System.Reflection;
using System.Collections;
using System.Text.Json;
using System.Windows.Forms;
using Roadhog;
using Roadhog.Application;
using Roadhog.Core.Accounts;
using Roadhog.Core.Diagnostics;
using Roadhog.Core.Model;
using Roadhog.Infrastructure.Vmm;

internal static class ConfiguredSkillQuickbarRefreshTests
{
    private const uint HighId = 1004, LowId = 1003, SecondHighId = 2004, SecondLowId = 2003;

    private static void Check(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }

    private static SkillSnapshot Rank(uint id, int tier, string name = "Configured Strike") =>
        new(id, name + (tier == 4 ? " IV" : tier == 3 ? " III" : " II"), tier, tier, name, tier,
            false, 10000, 0, XmlActivation: "Active", XmlEffectRemainMs: tier == 3 ? 3000 : 4000);

    private static SkillConfigNode Node(uint id, string name) => new()
    {
        SkillId = id, Name = name + " IV", BaseName = name, Type = "主动技能", ChainTimeMs = 900
    };

    private static ScriptSettings Settings()
    {
        var settings = new ScriptSettings { ProfileName = "configured-bar-refresh" };
        settings.Skills.SpiritmasterAutoSkillLogicEnabled = true;
        settings.Skills.ExecutionTree = new()
        {
            Node(SecondHighId, "Second Strike"), Node(HighId, "Configured Strike")
        };
        settings.Skills.ExecutionTree[1].Children.Add(Node(SecondHighId, "Second Strike"));
        settings.Skills.SystemExecutionTree = new() { Node(HighId, "Configured Strike") };
        settings.QuickbarSkills.ExecutionTree = new()
        {
            Node(HighId, "Configured Strike"), Node(SecondHighId, "Second Strike")
        };
        var detached = new SkillConfigNode
        {
            SkillId = 3003, Name = "Detached Chain III", BaseName = "Detached Chain", Type = "连续技", ChainTimeMs = 1234,
            Children = new()
            {
                new() { SkillId = 4003, Name = "Detached Final III", BaseName = "Detached Final", Type = "触发技能", ChainTimeMs = 2345 },
                Node(SecondHighId, "Second Strike")
            }
        };
        settings.QuickbarSkills.ExecutionTree[0].Children.Add(detached);
        settings.QuickbarSkills.ExecutionTree[0].Children.Add(Node(SecondHighId, "Second Strike"));
        settings.Skills.ManualMappings = new()
        {
            new() { SkillType = "主动技能", SkillName = "Configured Strike IV", Key = "F4" }
        };
        settings.Skills.OpeningSkill = new()
        {
            Enabled = true, ReleaseAll = true, Skills = new()
            {
                new() { SkillId = SecondHighId, SkillName = "Second Strike IV", Key = "F2" },
                new() { SkillId = HighId, SkillName = "Configured Strike IV", Key = "F3" }
            }
        };
        settings.Maintenance.HpMaintenanceRules = new()
        {
            new() { SkillId = HighId, SkillName = "Configured Strike IV", Key = "F5", BelowPercent = 43,
                RunTiming = MaintenanceRuleRunTiming.InCombat }
        };
        settings.Maintenance.MpMaintenanceRules = new()
        {
            new() { SkillId = HighId, SkillName = "Configured Strike IV", Key = "F6", BelowPercent = 37 },
            new() { ActionType = MaintenanceRuleActionType.Potion, Key = "F7", BelowPercent = 19 }
        };
        settings.Maintenance.StatusMaintenanceRules = new()
        {
            new() { SkillId = HighId, SkillName = "Configured Strike IV", Key = "F8", AbnormalStatusId = 777,
                RunTiming = MaintenanceRuleRunTiming.AfterCombat }
        };
        settings.Maintenance.DpMaintenanceRules = new()
        {
            new() { SkillId = HighId, SkillName = "Configured Strike IV", Key = "F9", RequiredDp = 2500 }
        };
        var team = settings.Team.Support;
        team.Enabled = true;
        team.HealSkillRules.Add(new() { SkillId = HighId, SkillName = "Configured Strike IV", Key = "F10", BelowPercent = 58 });
        team.MentalCleanseSkillId = team.PhysicalCleanseSkillId = team.GroupCleanseSkillId = HighId;
        team.MentalCleanseSkillName = team.PhysicalCleanseSkillName = team.GroupCleanseSkillName = "Configured Strike IV";
        team.MentalCleanseKey = "NumPad1";
        team.PhysicalCleanseKey = "NumPad2";
        team.GroupCleanseKey = "NumPad3";
        var spirit = settings.Skills.Spiritmaster;
        spirit.DotSkills.Add(new() { SkillId = HighId, SkillName = "Configured Strike IV" });
        spirit.SummonSkills.Add(new() { SkillId = HighId, SkillName = "Configured Strike IV", Key = "NumPad4" });
        spirit.SummonSkills.Add(new() { Key = "NumPad5" });
        spirit.OpeningAttackSkillId = HighId;
        spirit.OpeningAttackSkillName = "Configured Strike IV";
        spirit.OpeningAttackKey = "NumPad6";
        spirit.OpeningAttackDelayMs = 321;
        spirit.PetHpMaintenanceRules.Add(new() { SkillId = HighId, SkillName = "Configured Strike IV", Key = "NumPad7",
            BelowPercent = 47, CooldownMs = 12345 });
        spirit.PetBuffRules.Add(new() { SkillId = HighId, SkillName = "Configured Strike IV", Key = "NumPad8" });
        settings.SemiAuto.AttackWeaveEnabled = true;
        settings.SemiAuto.AttackWeaveDelayMs = 456;
        return settings;
    }

    public static Task AllReferencesAsync(string spiritWindow) => OnSta(() =>
    {
        using var fixture = new Fixture();
        if (spiritWindow != "unopened")
        {
            fixture.Call("ShowSpiritmasterSettingsDialog");
            Check(fixture.Field<Form?>("spiritmasterSettingsDialog") is not null, "spirit settings dialog is actually open");
            if (spiritWindow == "closed") fixture.Call("CloseSpiritmasterSettingsDialog");
        }
        var expected = fixture.Draft().Clone();
        // CaptureScriptSettings omits the runtime-only semi-auto values; SaveCurrentSettings
        // deliberately carries them forward from the stored configuration.
        expected.SemiAuto = fixture.Saved().SemiAuto.Clone();
        ReplaceReferences(expected);
        var result = fixture.Refresh();
        Check(result.Saved && result.DeletedCount == 0 && result.UpdatedCount > 0,
            "all references save the lower bar ranks without deletion: " + result.Error);
        Check(fixture.Runtime.Requests.Single().SequenceEqual(new[] { LowId, SecondLowId }),
            "exact read is limited to missing supported skill IDs; duplicates, items and invalid slots are ignored");
        var saved = fixture.Saved();
        Same(saved, expected, "all maintenance, team, opening, mapping and five spirit families update while every unrelated draft field survives (" + spiritWindow + ")");
        Check(saved.Skills.ExecutionTree.Select(node => node.SkillId).SequenceEqual(new[] { SecondHighId, HighId }) &&
            saved.Skills.ExecutionTree[1].Children.Single().SkillId == SecondHighId &&
            saved.Skills.SystemExecutionTree.Single().SkillId == HighId &&
            saved.Skills.ManualMappings.Single().SkillName == "Configured Strike IV",
            "retired attack trees and manual mappings remain unchanged compatibility archives");
        Check(saved.QuickbarSkills.ExecutionTree.Select(node => node.SkillId).SequenceEqual(new[] { LowId, SecondLowId }),
            "new priorities update independently instead of copying the differently ordered legacy tree");
        var detached = saved.QuickbarSkills.ExecutionTree[0].Children[0];
        Check(detached.SkillId == 3003 && detached.Name == "Detached Chain III" && detached.Type == "连续技" && detached.ChainTimeMs == 1234 &&
            detached.Children[0].SkillId == 4003 && detached.Children[0].Name == "Detached Final III" &&
            detached.Children[0].Type == "触发技能" && detached.Children[0].ChainTimeMs == 2345,
            "three-level off-bar chains retain exact ranks and metadata instead of learned IV or deletion");
        Check(detached.Children[1].SkillId == SecondLowId && saved.QuickbarSkills.ExecutionTree[0].Children[1].SkillId == SecondLowId,
            "independently placed children update even below a preserved off-bar parent while sibling order survives");
        Check(saved.Skills.Spiritmaster.SummonSkills[1].SkillId == 0 && saved.Skills.Spiritmaster.SummonSkills[1].Key == "NumPad5" &&
            saved.Maintenance.MpMaintenanceRules[1].ActionType == MaintenanceRuleActionType.Potion &&
            saved.Maintenance.MpMaintenanceRules[1].Key == "F7", "manual summon and potion actions keep their original keys and modes");
        Check((string)fixture.Call("AutomaticKeyText", LowId, "Configured Strike III")! == "自动: 1",
            "lower rank has a real main bar key, with main/leftmost precedence");
        if (spiritWindow == "open")
        {
            var list = fixture.Field<FlowLayoutPanel>("spiritmasterDotRuleList");
            Check(list.Controls.OfType<Panel>().SelectMany(row => row.Controls.OfType<Label>())
                .Single(label => label.Name == "spiritmasterDotDurationLabel").Text == "持续 3秒",
                "DOT duration follows the actual lower-rank metadata");
        }
        var repeat = fixture.Refresh();
        Check(repeat.Saved && repeat.UpdatedCount == 0 && repeat.DeletedCount == 0,
            "repeated refresh is idempotent");
        Same(fixture.Saved(), saved, "repeated refresh retains all saved values");
        Check(fixture.Runtime.Accounts.All(account => account == "configured-bar-refresh") && fixture.Runtime.AllTokensCancelable,
            "every snapshot read retains the selected account and bounded cancellation");
    });

    public static Task ButtonAsync(string mode) => OnSta(() =>
    {
        using var fixture = new Fixture();
        fixture.ShowSkillMode(mode == "quickbar");
        var button = (Button)fixture.Form.Controls.Find("quickbarRefreshConfiguredSkillsButton", true).Single();
        Check(button.Text == "刷新全部已配置技能" && button.Visible && button.Enabled,
            "the sole visible editor exposes the configured-refresh action");
        Check(fixture.Form.Controls.Find("quickbarCopyLegacyTreeButton", true).Length == 0,
            "new mode no longer exposes a copy action");
        var expected = fixture.Draft().Clone();
        expected.SemiAuto = fixture.Saved().SemiAuto.Clone();
        ReplaceReferences(expected);
        var original = button.Text;
        button.PerformClick();
        Check(!button.Enabled, "actual refresh button is disabled while completing the action");
        PumpUntil(() => button.Enabled && button.Text == original && !fixture.Field<bool>("configuredSkillRefreshInProgress"));
        Check(button.Enabled && button.Text == original, "actual button restores its text and enabled state");
        Same(fixture.Saved(), expected, "actual button refreshes the active tree and every shared reference while preserving retired archives");
        Check(fixture.Saved().Maintenance.StatusMaintenanceRules.Single().SkillId == LowId &&
            fixture.Saved().Skills.Spiritmaster.OpeningAttackSkillId == LowId,
            "actual button reaches the exact-rank refresh and save path");
        static IEnumerable<TreeNode> Flatten(TreeNodeCollection nodes) => nodes.Cast<TreeNode>()
            .SelectMany(node => new[] { node }.Concat(Flatten(node.Nodes)));
        foreach (var tree in new[] { fixture.Field<TreeView>("quickbarAvailableSkillTree") })
            Check(Flatten(tree.Nodes).Any(node => node.Text == "Configured Strike III"),
                "configured refresh rebuilds the candidate tree with the actual placed lower rank");
        var saved = fixture.Saved();
        fixture.Call("ApplyScriptSettings", saved);
        var reloaded = fixture.Draft();
        Same(reloaded.Skills.ExecutionTree, saved.Skills.ExecutionTree, "legacy ranks and order reload after shared refresh");
        Same(reloaded.QuickbarSkills, saved.QuickbarSkills, "new ranks, detached chain metadata and order reload after shared refresh");
        fixture.ShowSkillMode(mode != "quickbar");
        var switched = fixture.Draft();
        Same(switched.Skills.ExecutionTree, saved.Skills.ExecutionTree, "switching modes does not copy or overwrite the old tree");
        Same(switched.QuickbarSkills, saved.QuickbarSkills, "switching modes does not copy or overwrite the new tree");
    });

    public static Task NewRootCleanupAsync() => OnSta(() =>
    {
        var settings = Settings();
        var missing = Node(9904, "Unplaced Root");
        missing.Children.Add(Node(HighId, "Configured Strike"));
        settings.QuickbarSkills.ExecutionTree.Insert(1, missing);
        using var fixture = new Fixture(settings);
        var result = fixture.Refresh();
        Check(result.Saved && result.DeletedCount == 2, "unplaced new root removes its full subtree with accurate deletion count");
        Check(fixture.Saved().QuickbarSkills.ExecutionTree.Select(node => node.SkillId).SequenceEqual(new[] { LowId, SecondLowId }),
            "missing new root clears without importing legacy order or extra learned skills");
        Check(fixture.Saved().QuickbarSkills.ExecutionTree[0].Children[0].Children[0].SkillId == 4003,
            "clearing an unplaced root does not clear valid roots' off-bar chain descendants");
        var refreshed = fixture.Saved();
        for (var i = 0; i < 3; i++)
        {
            fixture.Call("ApplyScriptSettings", fixture.Saved());
            Check((bool)fixture.Call("SaveCurrentSettings", new object?[] { null })!, "repeated reload and save succeeds");
            Same(fixture.Saved().Skills.ExecutionTree, refreshed.Skills.ExecutionTree,
                "repeated save never restores an older legacy baseline");
            Same(fixture.Saved().QuickbarSkills, refreshed.QuickbarSkills,
                "repeated save never revives a removed new root or copies the old tree");
        }
        fixture.Call("ApplyScriptSettings", fixture.Saved());
        Same(fixture.Draft().QuickbarSkills, refreshed.QuickbarSkills, "reloading retains the cleaned independent new tree");
    });

    public static Task EmptyNewTreeAsync() => OnSta(() =>
    {
        var settings = Settings();
        settings.SkillTreeReleaseMode = SkillTreeReleaseMode.QuickbarAvailability;
        settings.QuickbarSkills.ExecutionTree.Clear();
        using var fixture = new Fixture(settings);
        Check(fixture.Refresh().Saved, "empty new tree can use the shared refresh action");
        Check(fixture.Saved().QuickbarSkills.ExecutionTree.Count == 0 &&
            fixture.Saved().Skills.ExecutionTree.Select(node => node.SkillId).SequenceEqual(new[] { SecondHighId, HighId }),
            "global refresh preserves retired archives while an explicitly empty active tree remains empty");
        var saved = fixture.Saved();
        fixture.Call("ApplyScriptSettings", saved);
        fixture.ShowSkillMode(false);
        fixture.ShowSkillMode(true);
        Check(fixture.Draft().QuickbarSkills.ExecutionTree.Count == 0,
            "reload and mode roundtrip never seed an empty new tree from the old tree or candidate list");
    });

    public static Task ReentryAsync() => OnSta(() =>
    {
        using var fixture = new Fixture();
        fixture.ShowSkillMode(false);
        var quickbar = (Button)fixture.Form.Controls.Find("quickbarRefreshConfiguredSkillsButton", true).Single();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Runtime.LearnedReadGate = gate.Task;
        var first = (Task)fixture.Call("RefreshConfiguredSkillsAsync", quickbar)!;
        Check(!quickbar.Enabled && fixture.Runtime.LearnedReadCount == 1,
            "starting a refresh disables the button before the asynchronous read completes");
        fixture.ShowSkillMode(true);
        quickbar.PerformClick();
        var duplicate = (Task)fixture.Call("RefreshConfiguredSkillsAsync", quickbar)!;
        Check(duplicate.IsCompletedSuccessfully && fixture.Runtime.LearnedReadCount == 1 &&
            !quickbar.Enabled,
            "redisplaying the editor, clicking the disabled button and reentering the handler cannot start a second read");
        gate.SetResult();
        Pump(first);
        Check(quickbar.Enabled && quickbar.Text == "刷新全部已配置技能",
            "the shared refresh restores the button after asynchronous completion");
        var saved = fixture.Saved();
        fixture.Runtime.LearnedReadGate = null;
        quickbar.PerformClick();
        Check(!quickbar.Enabled && fixture.Runtime.LearnedReadCount == 2,
            "a completed action releases its gate and the real button can refresh again");
        PumpUntil(() => quickbar.Enabled && quickbar.Text == "刷新全部已配置技能" &&
            !fixture.Field<bool>("configuredSkillRefreshInProgress"));
        Check(quickbar.Enabled, "retry restores the sole configured-refresh button");
        Same(fixture.Saved(), saved, "subsequent refresh is idempotent across independent trees and shared settings");
    });

    public static Task FailureAsync(string stage) => OnSta(() =>
    {
        using var fixture = new Fixture();
        Check(fixture.Refresh().Saved, "seed a previous successful bar preview");
        fixture.Call("ShowSpiritmasterSettingsDialog");
        var draft = fixture.Draft();
        var saved = fixture.Saved();
        var candidates = fixture.Field<object>("currentManualSkills");
        var bindings = fixture.Field<object>("previewSkillBindings");
        var exactReads = fixture.Runtime.Requests.Count;
        if (stage == "missing")
        {
            fixture.Runtime.ExactSkills = Array.Empty<SkillSnapshot>();
            fixture.Runtime.Bar = Bar(3013, SecondLowId);
        }
        else if (stage == "partial") fixture.Runtime.ExactSkills = new[] { Rank(LowId, 3) };
        else if (stage == "late-bar")
        {
            fixture.Runtime.Bar = new(0, Array.Empty<QuickbarSlotSnapshot>());
            fixture.Runtime.FailureStage = stage;
        }
        else fixture.Runtime.FailureStage = stage;
        var result = fixture.Refresh();
        if (stage == "late-bar")
            Check(fixture.Runtime.ReturnedBarAfterCancellation && fixture.Runtime.Requests.Count == exactReads,
                "bar read successfully returns after the real timeout without triggering an exact read");
        Check(!result.Saved && result.UpdatedCount == 0 && result.DeletedCount == 0 && result.Error.Length > 0,
            "unresolvable/cancelled snapshot join rejects the refresh before replacement or saving (" + stage + ")");
        Same(fixture.Draft(), draft, "failed refresh preserves every editable field");
        Same(fixture.Saved(), saved, "failed refresh preserves every saved field");
        Check(ReferenceEquals(fixture.Field<object>("currentManualSkills"), candidates) &&
            ReferenceEquals(fixture.Field<object>("previewSkillBindings"), bindings),
            "failed refresh retains previous candidate and binding identities");
    });

    public static Task BarSelectionAsync() => OnSta(() =>
    {
        using var fixture = new Fixture();
        fixture.Runtime.Bar = new(1, new QuickbarSlotSnapshot[]
        {
            new(SkillQuickbar.Main, 0, 21, LowId), new(SkillQuickbar.Alt, 0, 21, 1002),
            new(SkillQuickbar.Alt, 1, 21, SecondLowId), new(SkillQuickbar.Main, 5, 1, HighId)
        });
        fixture.Runtime.ExactSkills = new[] { Rank(LowId, 3), Rank(1002, 2), Rank(SecondLowId, 3, "Second Strike") };
        Check(fixture.Refresh().Saved && fixture.Saved().Maintenance.StatusMaintenanceRules.Single().SkillId == LowId,
            "multiple placed ranks select the highest bar rank, not learned IV or an item ID");
        fixture.Runtime.Bar = new(1, new[]
        {
            new QuickbarSlotSnapshot(SkillQuickbar.Alt, 0, 21, HighId),
            new QuickbarSlotSnapshot(SkillQuickbar.Main, 0, 21, SecondHighId)
        });
        var exactCount = fixture.Runtime.Requests.Count;
        Check(fixture.Refresh().Saved && fixture.Saved().Maintenance.StatusMaintenanceRules.Single().SkillId == HighId &&
            fixture.Runtime.Requests.Count == exactCount, "a higher rank actually placed on the bar replaces III without an unnecessary exact read");
        fixture.Runtime.Bar = new(1, Array.Empty<QuickbarSlotSnapshot>());
        var empty = fixture.Refresh();
        Check(empty.Saved && empty.DeletedCount > 0 && fixture.Saved().Skills.ExecutionTree.Count == 2 &&
            fixture.Saved().QuickbarSkills.ExecutionTree.Count == 0 &&
            fixture.Saved().Skills.OpeningSkill.GetEffectiveSkills().Count == 0 &&
            fixture.Saved().Maintenance.StatusMaintenanceRules.Single().SkillId == 0,
            "confirmed empty bar retains existing remove/clear-and-save behavior");
        Check(fixture.Saved().Skills.Spiritmaster.SummonSkills[1].Key == "NumPad5" &&
            fixture.Runtime.Requests.Count == exactCount, "empty bars require no exact read and retain manual keys");
        fixture.Call("ApplyScriptSettings", fixture.Saved());
        Check((bool)fixture.Call("SaveCurrentSettings", new object?[] { null })!, "reload and save the empty active tree");
        Check(fixture.Saved().Skills.ExecutionTree.Count == 2 && fixture.Saved().QuickbarSkills.ExecutionTree.Count == 0,
            "reload and saving preserve archives without restoring them into the confirmed empty active tree");
    });

    public static Task CandidateCompatibilityAsync() => OnSta(() =>
    {
        using var fixture = new Fixture();
        fixture.Runtime.Bar = new(0, Bar().Slots.Take(4).ToArray());
        fixture.Call("ShowSpiritmasterSettingsDialog");
        var draft = fixture.Draft();
        var saved = fixture.Saved();
        using var button = new Button { Text = "刷新技能" };
        Pump((Task)fixture.Call("RefreshQuickbarSkillCandidatesAsync", button)!);
        Same(fixture.Draft(), draft, "ordinary candidate refresh preserves draft ranks");
        Same(fixture.Saved(), saved, "ordinary candidate refresh does not save configuration");
        Check(fixture.Runtime.Requests.Single().SequenceEqual(new[] { LowId, SecondLowId }),
            "candidate refresh obtains exact bound ranks without replacing configured references");
    });

    public static Task UtilityBarAsync() => OnSta(() =>
    {
        using var fixture = new Fixture();
        var expected = fixture.Draft().Clone();
        expected.SemiAuto = fixture.Saved().SemiAuto.Clone();
        ReplaceReferences(expected);
        fixture.Runtime.Bar = new(0, Bar().Slots.Concat(new[]
        {
            new QuickbarSlotSnapshot(SkillQuickbar.Main, 8, 21, 50300)
        }).ToArray());
        fixture.Runtime.ExactSkills = fixture.Runtime.ExactSkills.Concat(new[]
        {
            new SkillSnapshot(50300, "紧急返回", 1, 1, "紧急返回", 1, false, 0, 0, XmlActivation: "Active")
        }).ToArray();
        Check(fixture.Refresh().Saved, "an unrelated utility skill on the bar does not block configured-rank refresh");
        Same(fixture.Saved(), expected, "utility slots neither add configured references nor change unrelated settings");
    });

    public static Task ProviderFiltersAsync()
    {
        // Exercise the provider's post-read selection without creating a VMM connection.
        var infoType = typeof(AionVmmGameApi).GetNestedType("LearnedSkillInfo", BindingFlags.NonPublic)!;
        IList Skills(params (uint Id, string Name, int Tier)[] source)
        {
            var list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(infoType))!;
            foreach (var (id, name, tier) in source)
            {
                var info = Activator.CreateInstance(infoType)!;
                void Set(string field, object value) => infoType.GetField(field)!.SetValue(info, value);
                Set("SkillId", id);
                Set("Name", name + " " + tier);
                Set("DisplayBaseName", name);
                Set("DisplayTier", tier);
                Set("HighestLevel", (ushort)tier);
                Set("SkillLevel", (uint)tier);
                Set("CooldownDuration", 1000u);
                list.Add(info);
            }
            return list;
        }
        var options = new AionVmmGameApiOptions();
        var api = new AionVmmGameApi(options, new InMemoryRoadhogLogger());
        var method = typeof(AionVmmGameApi).GetMethod("SelectSkillsForRefresh", BindingFlags.Instance | BindingFlags.NonPublic)!;
        uint[] Select(IList skills, uint[]? ids) => ((IList)method.Invoke(api, new object?[] { skills, ids })!)
            .Cast<object>().Select(info => (uint)infoType.GetField("SkillId")!.GetValue(info)!).OrderBy(id => id).ToArray();
        var full = Skills((LowId, "Configured Strike", 3), (HighId, "Configured Strike", 4),
            (3001, "紧急返回", 1), (50300, "Utility", 1));
        Check(Select(full, null).SequenceEqual(new[] { HighId }),
            "ordinary full reads retain both highest-rank grouping and utility filtering");
        var requested = Skills((LowId, "Configured Strike", 3), (3001, "紧急返回", 1), (50300, "Utility", 1));
        Check(Select(requested, new uint[] { LowId, 3001, 50300 }).SequenceEqual(new uint[] { LowId, 3001, 50300 }),
            "explicit skill identities retain the actual low rank and utility metadata even under default full-list filters");
        options.GroupByDisplayName = false;
        options.FilterUtilitySkills = false;
        Check(Select(full, null).SequenceEqual(new uint[] { LowId, HighId, 3001, 50300 }),
            "disabled full-list filtering options retain their existing meaning");
        return Task.CompletedTask;
    }

    private static void ReplaceReferences(ScriptSettings settings)
    {
        (uint Id, string Name) Replace(uint id, string name) => id switch
        {
            HighId => (LowId, "Configured Strike III"),
            SecondHighId => (SecondLowId, "Second Strike III"),
            _ => (id, name)
        };
        void Tree(IEnumerable<SkillConfigNode> nodes)
        {
            foreach (var node in nodes)
            {
                var replaced = node.SkillId is HighId or SecondHighId;
                (node.SkillId, node.Name) = Replace(node.SkillId, node.Name);
                if (replaced) node.ChainTimeMs = null;
                Tree(node.Children);
            }
        }
        Tree(settings.QuickbarSkills.ExecutionTree);
        foreach (var item in settings.Skills.OpeningSkill.Skills!)
            (item.SkillId, item.SkillName) = Replace(item.SkillId, item.SkillName);
        var first = settings.Skills.OpeningSkill.Skills[0];
        settings.Skills.OpeningSkill.SkillId = first.SkillId;
        settings.Skills.OpeningSkill.SkillName = first.SkillName;
        foreach (var item in settings.Maintenance.HpMaintenanceRules.Concat(settings.Maintenance.MpMaintenanceRules))
            (item.SkillId, item.SkillName) = Replace(item.SkillId, item.SkillName);
        foreach (var item in settings.Maintenance.StatusMaintenanceRules)
            (item.SkillId, item.SkillName) = Replace(item.SkillId, item.SkillName);
        foreach (var item in settings.Maintenance.DpMaintenanceRules)
            (item.SkillId, item.SkillName) = Replace(item.SkillId, item.SkillName);
        var team = settings.Team.Support;
        foreach (var item in team.HealSkillRules) (item.SkillId, item.SkillName) = Replace(item.SkillId, item.SkillName);
        (team.MentalCleanseSkillId, team.MentalCleanseSkillName) = Replace(team.MentalCleanseSkillId, team.MentalCleanseSkillName);
        (team.PhysicalCleanseSkillId, team.PhysicalCleanseSkillName) = Replace(team.PhysicalCleanseSkillId, team.PhysicalCleanseSkillName);
        (team.GroupCleanseSkillId, team.GroupCleanseSkillName) = Replace(team.GroupCleanseSkillId, team.GroupCleanseSkillName);
        var spirit = settings.Skills.Spiritmaster;
        foreach (var item in spirit.DotSkills) (item.SkillId, item.SkillName) = Replace(item.SkillId, item.SkillName);
        foreach (var item in spirit.SummonSkills) (item.SkillId, item.SkillName) = Replace(item.SkillId, item.SkillName);
        (spirit.OpeningAttackSkillId, spirit.OpeningAttackSkillName) = Replace(spirit.OpeningAttackSkillId, spirit.OpeningAttackSkillName);
        foreach (var item in spirit.PetHpMaintenanceRules) (item.SkillId, item.SkillName) = Replace(item.SkillId, item.SkillName);
        foreach (var item in spirit.PetBuffRules) (item.SkillId, item.SkillName) = Replace(item.SkillId, item.SkillName);
    }

    private static QuickbarSnapshot Bar(uint first = LowId, uint second = SecondLowId) => new(0, new QuickbarSlotSnapshot[]
    {
        new(SkillQuickbar.Main, 0, 21, first), new(SkillQuickbar.Alt, 1, 21, second),
        new(SkillQuickbar.Main, 5, 21, first), new(SkillQuickbar.Main, 4, 1, HighId),
        new(SkillQuickbar.Main, 12, 21, 9998), new((SkillQuickbar)99, 0, 21, 9999)
    });

    private static void Same(object actual, object expected, string message)
    {
        var actualJson = JsonSerializer.Serialize(actual);
        var expectedJson = JsonSerializer.Serialize(expected);
        if (actualJson == expectedJson) return;
        var index = 0;
        while (index < Math.Min(actualJson.Length, expectedJson.Length) && actualJson[index] == expectedJson[index]) index++;
        var start = Math.Max(0, index - 80);
        throw new InvalidOperationException(message + "; expected " + expectedJson.Substring(start, Math.Min(180, expectedJson.Length - start)) +
            "; actual " + actualJson.Substring(start, Math.Min(180, actualJson.Length - start)));
    }

    private static void Pump(Task task)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!task.IsCompleted && DateTime.UtcNow < deadline) { Application.DoEvents(); Thread.Sleep(5); }
        Check(task.IsCompleted, "UI refresh must finish within the mock test deadline");
        task.GetAwaiter().GetResult();
    }

    private static void PumpUntil(Func<bool> completed)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!completed() && DateTime.UtcNow < deadline) { Application.DoEvents(); Thread.Sleep(5); }
        Check(completed(), "actual UI refresh action must finish within the mock test deadline");
    }

    private static Task OnSta(Action action)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            Exception? failure = null;
            try
            {
                using var dispatcher = new Form
                {
                    ShowInTaskbar = false, StartPosition = FormStartPosition.Manual,
                    Location = new(-32000, -32000), Size = new(1, 1), Opacity = 0
                };
                dispatcher.Shown += (_, _) => dispatcher.BeginInvoke((Action)(() =>
                {
                    var previous = SynchronizationContext.Current;
                    using var context = new WindowsFormsSynchronizationContext();
                    SynchronizationContext.SetSynchronizationContext(context);
                    try { action(); }
                    catch (Exception exception) { failure = exception; }
                    finally
                    {
                        SynchronizationContext.SetSynchronizationContext(previous);
                        Application.ExitThread();
                    }
                }));
                // Keep a real outer loop alive so nested DoEvents calls preserve the
                // WinForms context used by delayed runtime and button continuations.
                Application.Run(dispatcher);
            }
            catch (Exception exception) { failure = exception; }
            if (failure is null) done.SetResult();
            else done.SetException(new InvalidOperationException(failure.ToString(), failure));
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return done.Task;
    }

    private sealed class Fixture : IDisposable
    {
        public AccountSettingsForm Form { get; }
        public InMemoryAccountConfigStore Store { get; }
        public PreviewRuntime Runtime { get; }
        public Fixture(ScriptSettings? settings = null)
        {
            Store = new(new AccountConfig { AccountName = "configured-bar-refresh", ScriptSettings = settings ?? Settings() });
            var logger = new InMemoryRoadhogLogger();
            var runtime = DispatchProxy.Create<IRoadhogRuntime, PreviewRuntime>();
            Runtime = (PreviewRuntime)runtime;
            Runtime.Inner = new RoadhogRuntime(new FakeGameApi(), logger, new AccountRuntimeManager(logger), null!);
            Form = new("configured-bar-refresh", runtime, Store, new InMemorySharedPathStore(),
                new InMemoryScriptProfileStore(), new RecordingFolderLauncher(), "test-paths");
            Form.ShowInTaskbar = false;
            Form.StartPosition = FormStartPosition.Manual;
            Form.Location = new(-32000, -32000);
        }
        public void ShowSkillMode(bool quickbar)
        {
            Control? page = Field<Panel>("quickbarSkillPanel");
            while (page is not null && page is not TabPage) page = page.Parent;
            Check(page is TabPage && page.Parent is TabControl, "skill mode fixture finds its real tab page");
            ((TabControl)page!.Parent!).SelectedTab = (TabPage)page;
            if (!Form.Visible) Form.Show();
            Application.DoEvents();
        }
        public object? Call(string name, params object?[] args) => typeof(AccountSettingsForm)
            .GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(method => method.Name == name && method.GetParameters().Length == args.Length).Invoke(Form, args);
        public T Field<T>(string name) => (T)typeof(AccountSettingsForm)
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Form)!;
        public ScriptSettings Draft() => (ScriptSettings)Call("CaptureScriptSettings")!;
        public ScriptSettings Saved() => Store.LoadAllAsync().GetAwaiter().GetResult().Value!.Single().ScriptSettings!;
        public (int UpdatedCount, int DeletedCount, bool Saved, string Error) Refresh()
        {
            var task = (Task<(int, int, bool, string)>)Call("RefreshConfiguredSkillsCoreAsync")!;
            Pump(task);
            return task.GetAwaiter().GetResult();
        }
        public void Dispose() { Call("CloseSpiritmasterSettingsDialog"); Form.Dispose(); }
    }

    public class PreviewRuntime : DispatchProxy
    {
        public IRoadhogRuntime Inner { get; set; } = null!;
        public IReadOnlyList<SkillSnapshot> LearnedSkills { get; set; } = new[]
        {
            Rank(HighId, 4), Rank(SecondHighId, 4, "Second Strike"),
            Rank(3004, 4, "Detached Chain"), Rank(4004, 4, "Detached Final")
        };
        public IReadOnlyList<SkillSnapshot> ExactSkills { get; set; } = new[] { Rank(LowId, 3), Rank(SecondLowId, 3, "Second Strike") };
        public QuickbarSnapshot Bar { get; set; } = ConfiguredSkillQuickbarRefreshTests.Bar();
        public string? FailureStage { get; set; }
        public List<uint[]> Requests { get; } = new();
        public List<string?> Accounts { get; } = new();
        public Task? LearnedReadGate { get; set; }
        public int LearnedReadCount { get; private set; }
        public bool AllTokensCancelable { get; private set; } = true;
        public bool ReturnedBarAfterCancellation { get; private set; }
        private async Task<IReadOnlyList<SkillSnapshot>> ReadLearnedAfterGateAsync(Task gate)
        {
            await gate.ConfigureAwait(false);
            return LearnedSkills;
        }
        private async Task<QuickbarSnapshot> ReadBarAfterCancellationAsync(CancellationToken token)
        {
            try { await Task.Delay(Timeout.InfiniteTimeSpan, token).ConfigureAwait(false); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            ReturnedBarAfterCancellation = token.IsCancellationRequested;
            return Bar;
        }
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            if (method!.Name is nameof(IRoadhogRuntime.RefreshSkillsAsync) or nameof(IRoadhogRuntime.ReadQuickbarAsync) or
                nameof(IRoadhogRuntime.RefreshSkillsByIdsAsync))
            {
                var stage = method.Name == nameof(IRoadhogRuntime.RefreshSkillsAsync) ? "learned" :
                    method.Name == nameof(IRoadhogRuntime.ReadQuickbarAsync) ? "bar" : "exact";
                var accountIndex = stage == "exact" ? 1 : 0;
                Accounts.Add((string?)args![accountIndex]);
                AllTokensCancelable &= ((CancellationToken)args[accountIndex + 1]!).CanBeCanceled;
                if (stage == FailureStage || FailureStage == "cancelled" && stage == "exact")
                    throw FailureStage == "cancelled" ? new OperationCanceledException("mock cancellation") :
                        new InvalidOperationException("mock " + stage + " failure");
                if (stage == "learned")
                {
                    LearnedReadCount++;
                    return LearnedReadGate is { } gate ? ReadLearnedAfterGateAsync(gate) : Task.FromResult(LearnedSkills);
                }
                if (stage == "bar" && FailureStage == "late-bar")
                    return ReadBarAfterCancellationAsync((CancellationToken)args[accountIndex + 1]!);
                if (stage == "bar") return Task.FromResult(Bar);
                var ids = ((IReadOnlyCollection<uint>)args[0]!).ToArray();
                Requests.Add(ids);
                return Task.FromResult<IReadOnlyList<SkillSnapshot>>(ExactSkills.Where(skill => ids.Contains(skill.SkillId)).ToArray());
            }
            return method.Invoke(Inner, args);
        }
    }
}
