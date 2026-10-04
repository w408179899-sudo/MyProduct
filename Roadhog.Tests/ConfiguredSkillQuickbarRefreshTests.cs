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
        settings.QuickbarSkills.ExecutionTree = new() { Node(9904, "Independent New Tree") };
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
        Check(saved.Skills.ExecutionTree.Select(node => node.SkillId).SequenceEqual(new[] { SecondLowId, LowId }) &&
            saved.Skills.ExecutionTree[1].Children.Single().SkillId == SecondLowId,
            "root order and existing child structure remain unchanged");
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

    public static Task ButtonAsync() => OnSta(() =>
    {
        using var fixture = new Fixture();
        var button = fixture.Form.Controls.Find("autoSkillPanel", true).Single().Controls.OfType<Button>()
            .Single(item => item.Text == "刷新全部已配置技能");
        var original = button.Text;
        var task = (Task)fixture.Call("RefreshConfiguredSkillsAsync", button)!;
        Check(!button.Enabled, "actual refresh button is disabled while completing the action");
        Pump(task);
        Check(button.Enabled && button.Text == original, "actual button restores its text and enabled state");
        Check(fixture.Saved().Maintenance.StatusMaintenanceRules.Single().SkillId == LowId &&
            fixture.Saved().Skills.Spiritmaster.OpeningAttackSkillId == LowId,
            "actual button reaches the exact-rank refresh and save path");
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
        Check(empty.Saved && empty.DeletedCount > 0 && fixture.Saved().Skills.ExecutionTree.Count == 0 &&
            fixture.Saved().Skills.OpeningSkill.GetEffectiveSkills().Count == 0 &&
            fixture.Saved().Maintenance.StatusMaintenanceRules.Single().SkillId == 0,
            "confirmed empty bar retains existing remove/clear-and-save behavior");
        Check(fixture.Saved().Skills.Spiritmaster.SummonSkills[1].Key == "NumPad5" &&
            fixture.Runtime.Requests.Count == exactCount, "empty bars require no exact read and retain manual keys");
    });

    public static Task CandidateCompatibilityAsync() => OnSta(() =>
    {
        using var fixture = new Fixture();
        fixture.Call("ShowSpiritmasterSettingsDialog");
        var draft = fixture.Draft();
        var saved = fixture.Saved();
        using var button = new Button { Text = "刷新技能" };
        Pump((Task)fixture.Call("RefreshCurrentSkillsAsync", button, null, null)!);
        Same(fixture.Draft(), draft, "ordinary candidate refresh preserves draft ranks");
        Same(fixture.Saved(), saved, "ordinary candidate refresh does not save configuration");
        Check(fixture.Runtime.Requests.Count == 0, "ordinary candidate refresh retains its previous read scope");
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
                (node.SkillId, node.Name) = Replace(node.SkillId, node.Name);
                node.ChainTimeMs = null;
                Tree(node.Children);
            }
        }
        Tree(settings.Skills.ExecutionTree);
        Tree(settings.Skills.SystemExecutionTree);
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
        foreach (var item in settings.Skills.ManualMappings) item.SkillName = "Configured Strike III";
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

    private static Task OnSta(Action action)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try { action(); done.SetResult(); }
            catch (Exception exception) { done.SetException(exception); }
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
        public Fixture()
        {
            Store = new(new AccountConfig { AccountName = "configured-bar-refresh", ScriptSettings = Settings() });
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
        public IReadOnlyList<SkillSnapshot> LearnedSkills { get; set; } = new[] { Rank(HighId, 4), Rank(SecondHighId, 4, "Second Strike") };
        public IReadOnlyList<SkillSnapshot> ExactSkills { get; set; } = new[] { Rank(LowId, 3), Rank(SecondLowId, 3, "Second Strike") };
        public QuickbarSnapshot Bar { get; set; } = ConfiguredSkillQuickbarRefreshTests.Bar();
        public string? FailureStage { get; set; }
        public List<uint[]> Requests { get; } = new();
        public List<string?> Accounts { get; } = new();
        public bool AllTokensCancelable { get; private set; } = true;
        public bool ReturnedBarAfterCancellation { get; private set; }
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
                if (stage == "learned") return Task.FromResult(LearnedSkills);
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
