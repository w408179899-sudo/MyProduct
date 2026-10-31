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

internal static class QuickbarKeyModeTests
{
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static SkillSnapshot Skill(uint id, string? chain = null, string? prechain = null) =>
        new(id, "skill" + id, 1, 1, "skill" + id, 1, false, 0, 0, XmlActivation: "Active", XmlChainCategory: chain, XmlPrechainCategory: prechain);
    private static SkillConfigNode Node(uint id) => new() { SkillId = id, Name = "skill" + id };
    private static readonly SkillSnapshot[] Skills = { Skill(101), Skill(102), Skill(103, "third-chain"), Skill(104, prechain: "third-chain") };
    private static QuickbarSnapshot Bars => new(0, new QuickbarSlotSnapshot[]
    {
        new(SkillQuickbar.Main, 0, 21, 101), new(SkillQuickbar.Alt, 10, 21, 102),
        new(SkillQuickbar.Ctrl, 11, 21, 103), new(SkillQuickbar.Ctrl, 0, 21, 101)
    });

    public static Task MappingAsync()
    {
        var old = JsonSerializer.Deserialize<ScriptSettings>("{\"QuickbarKeyMode\":0}")!.Clone();
        Check(!JsonSerializer.Serialize(old).Contains("QuickbarKeyMode"), "obsolete saved numpad mode is ignored and no longer persisted");
        Check(!JsonSerializer.Serialize(JsonSerializer.Deserialize<ScriptSettings>("{}")!.Clone()).Contains("QuickbarKeyMode"),
            "profiles without a mode use the same defaults");
        var main = new[] { "D1", "D2", "D3", "D4", "D5", "D6", "D7", "D8", "D9", "D0", "OemMinus", "OemPlus" };
        for (var slot = 0; slot < 12; slot++)
            Check(SkillKeyBindings.GetSlotKey(SkillQuickbar.Main, slot) == main[slot] &&
                SkillKeyBindings.GetSlotKey(SkillQuickbar.Alt, slot) == "Alt+" + main[slot] &&
                SkillKeyBindings.GetSlotKey(SkillQuickbar.Ctrl, slot) == "Ctrl+" + main[slot], "all thirty-six slots use game defaults");
        Check(SkillKeyBindings.GetSlotKey(SkillQuickbar.Ctrl, 12) is null &&
            SkillKeyBindings.GetSlotKey((SkillQuickbar)9, 0) is null, "unsupported slots are not bound");
        var bindings = new SkillKeyBindings(Bars, Skills);
        var settings = new QuickbarSkillScriptSettings { ExecutionTree = new() { Node(102), Node(103), Node(101) } };
        settings.ExecutionTree[1].Children.Add(Node(104));
        var plan = QuickbarSkillPlan.FromSettings(settings, bindings);
        Check(plan.Roots.Select(n => n.Key).SequenceEqual(new[] { "Alt+OemMinus", "Ctrl+OemPlus", "D1" }), "tree order and main duplicate preference retained");
        var child = plan.Roots[1].Children.Single();
        Check(child.Key == "Ctrl+OemPlus" && child.Bar == SkillQuickbar.Ctrl && child.Slot == 11 && child.BaseSkillId == 103, "third-row chain inherits physical slot and anchor");
        old.Maintenance.HpMaintenanceRules.Add(new() { SkillId = 103, Key = "D1", BelowPercent = 51 });
        old.Team.Support.MentalCleanseSkillId = 103;
        old.Skills.Spiritmaster.OpeningAttackSkillId = 103;
        var mapped = bindings.ApplyTo(old);
        Check(mapped.Maintenance.HpMaintenanceRules.Single().Key == "Ctrl+OemPlus" && mapped.Maintenance.HpMaintenanceRules[0].BelowPercent == 51 &&
            mapped.Team.Support.MentalCleanseKey == "Ctrl+OemPlus" && mapped.Skills.Spiritmaster.OpeningAttackKey == "Ctrl+OemPlus", "maintenance team spirit share third-row binding");
        Check(old.Maintenance.HpMaintenanceRules[0].Key == "D1", "mapping does not mutate saved settings");
        Check(typeof(IRoadhogRuntime).GetMethods().GroupBy(m => m.Name).All(g => g.Count() == 1), "runtime RPC method names remain unique");
        return Task.CompletedTask;
    }

    public static Task DecoderAsync()
    {
        var memory = new SkillBindingTests.Memory(3);
        var old = new QuickbarDecoder(memory.Read).Read(SkillBindingTests.Memory.Module);
        var all = new QuickbarDecoder(memory.Read).Read(SkillBindingTests.Memory.Module, true);
        Check(old.Slots.Count == 24 && all.Slots.Count == 36 && all.Slots.Last().SkillId == 103 && all.Slots.Last().Bar == SkillQuickbar.Ctrl, "explicit third-row read retains actual location");
        foreach (var fault in new[] { "third-short", "third-mismatch", "third-kind" })
        {
            memory = new(3) { Fault = fault };
            Check(new QuickbarDecoder(memory.Read).Read(SkillBindingTests.Memory.Module).Slots.Count == 24, "default does not depend on third row " + fault);
            try { new QuickbarDecoder(memory.Read).Read(SkillBindingTests.Memory.Module, true); throw new Exception("invalid third row accepted " + fault); }
            catch (InvalidDataException) { }
        }
        var store = new DmaStableSnapshotStore(AionVmmSnapshotChannels.Registry);
        var context = new GameApiReadContext("a", 1, "Aion.bin", "device");
        var now = DateTimeOffset.UtcNow;
        store.Resolve("a", AionVmmSnapshotChannels.Quickbar, context, OperationResult<QuickbarSnapshot>.Ok(old), now);
        Check(!store.Resolve("a", AionVmmSnapshotChannels.QuickbarThreeRows, context with { IncludeThirdQuickbar = true },
            OperationResult<QuickbarSnapshot>.Fail("partial"), now).Result.Success, "two rows cannot satisfy cold three-row read");
        store.Resolve("a", AionVmmSnapshotChannels.QuickbarThreeRows, context, OperationResult<QuickbarSnapshot>.Ok(all), now);
        Check(store.Resolve("a", AionVmmSnapshotChannels.Quickbar, context, OperationResult<QuickbarSnapshot>.Fail("partial"), now).Result.Value!.Slots.Count == 24,
            "three-row publication cannot contaminate default hold");
        return Task.CompletedTask;
    }

    public static async Task StartupAsync()
    {
        var api = new FakeGameApi { Quickbar = Bars, Skills = Skills };
        var log = new InMemoryRoadhogLogger();
        var source = new AccountConfig { AccountName = "account2", ScriptSettings = JsonSerializer.Deserialize<ScriptSettings>("{\"QuickbarKeyMode\":0}") };
        var worker = new AccountWorkerContext(source.Clone(), api, log, new AccountRuntimeManager(log), new(), CancellationToken.None);
        await worker.PrepareSkillBindingsAsync();
        Check(worker.SkillBindings!.Resolve(103, "")!.Key == "Ctrl+OemPlus", "old numpad configuration starts with default Ctrl binding");
        Check(api.LastQuickbarReadContext?.IncludeThirdQuickbar == true, "startup reader requests third row");
        await ((ISkillAvailabilitySnapshotReader)worker.Snapshots).ReadSkillAvailabilityAsync();
        Check(api.LastSkillAvailabilityReadContext?.IncludeThirdQuickbar == true, "availability reader requests all three rows");
        var accounts = new AccountRuntimeManager(log);
        accounts.MarkStarting(source);
        var runtime = new RoadhogRuntime(api, log, accounts, null!);
        await runtime.ReadQuickbarAsync("account2");
        Check(api.LastQuickbarReadContext?.IncludeThirdQuickbar == true, "active-account preview requests all three rows without settings");

    }

    public static Task UiAsync()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                var log = new InMemoryRoadhogLogger();
                var store = new InMemoryAccountConfigStore(new AccountConfig { AccountName = "account2", ScriptSettings = new() });
                var runtime = DispatchProxy.Create<IRoadhogRuntime, PreviewRuntime>();
                var preview = (PreviewRuntime)runtime;
                preview.Inner = new RoadhogRuntime(new FakeGameApi { Skills = Skills, Quickbar = Bars }, log, new AccountRuntimeManager(log), null!);
                using var form = new AccountSettingsForm("account2", runtime,
                    store, new InMemorySharedPathStore(), new InMemoryScriptProfileStore(), new RecordingFolderLauncher(), "test-paths");
                object? Get(string name) => typeof(AccountSettingsForm).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form);
                object? Call(string name, params object?[] values) => typeof(AccountSettingsForm).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form, values);
                Check(!Controls(form).Any(c => c.Name is "numpadKeyModeRadio" or "gameDefaultKeyModeRadio" or "quickbarKeyModePanel"),
                    "obsolete quickbar mode controls are removed");
                Check((bool)Call("SaveCurrentSettings", new object?[] { null })!, "actual save succeeds");
                Check(!JsonSerializer.Serialize(store.LoadAllAsync().GetAwaiter().GetResult().Value).Contains("QuickbarKeyMode"),
                    "account save omits removed mode");
                typeof(AccountSettingsForm).GetField("previewSkillBindings", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .SetValue(form, new SkillKeyBindings(Bars, Skills));
                Call("ApplyScriptSettings", JsonSerializer.Deserialize<ScriptSettings>("{\"QuickbarKeyMode\":0}")!);
                Check(Get("previewSkillBindings") is null, "loading another profile invalidates old preview");

                var storedBefore = JsonSerializer.Serialize(store.LoadAllAsync().GetAwaiter().GetResult().Value);
                var pending = new TaskCompletionSource<QuickbarSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
                preview.PendingBar = pending.Task;
                var refresh = (Task<(int UpdatedCount, int DeletedCount, bool Saved, string Error)>)Call("RefreshConfiguredSkillsCoreAsync")!;
                Check(!refresh.IsCompleted, "refresh awaits official bar snapshot");
                Call("ApplyScriptSettings", new ScriptSettings());
                pending.SetResult(Bars);
                var deadline = DateTime.UtcNow.AddSeconds(3);
                while (!refresh.IsCompleted && DateTime.UtcNow < deadline) { System.Windows.Forms.Application.DoEvents(); Thread.Sleep(1); }
                Check(refresh.IsCompleted, "stale refresh terminates");
                var result = refresh.GetAwaiter().GetResult();
                Check(!result.Saved && result.DeletedCount == 0 && Get("previewSkillBindings") is null &&
                    JsonSerializer.Serialize(store.LoadAllAsync().GetAwaiter().GetResult().Value) == storedBefore,
                    "profile change during refresh cannot delete skills or persist stale bindings");
                completion.SetResult();
            }
            catch (Exception ex) { completion.SetException(ex); }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); return completion.Task;
    }

    private static IEnumerable<Control> Controls(Control parent) =>
        parent.Controls.Cast<Control>().SelectMany(c => new[] { c }.Concat(Controls(c)));

    public class PreviewRuntime : DispatchProxy
    {
        public IRoadhogRuntime Inner { get; set; } = null!;
        public Task<QuickbarSnapshot>? PendingBar { get; set; }
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            if (method!.Name == nameof(IRoadhogRuntime.ReadQuickbarAsync) && PendingBar is not null)
            {
                return PendingBar;
            }
            return method.Invoke(Inner, args);
        }
    }
}
