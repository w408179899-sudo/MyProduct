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
        var old = JsonSerializer.Deserialize<ScriptSettings>("{}")!;
        Check(old.QuickbarKeyMode == QuickbarKeyMode.Numpad && old.Clone().QuickbarKeyMode == QuickbarKeyMode.Numpad, "old JSON defaults to numpad");
        old.QuickbarKeyMode = QuickbarKeyMode.GameDefault;
        Check(JsonSerializer.Deserialize<ScriptSettings>(JsonSerializer.Serialize(old.Clone()))!.QuickbarKeyMode == QuickbarKeyMode.GameDefault, "new mode roundtrips and clones");
        var main = new[] { "D1", "D2", "D3", "D4", "D5", "D6", "D7", "D8", "D9", "D0", "OemMinus", "OemPlus" };
        var numpad = new[] { "NumPad1", "NumPad2", "NumPad3", "NumPad4", "NumPad5", "NumPad6", "NumPad7", "NumPad8", "NumPad9", "NumPad0", "NumPadAdd", "NumPadSubtract" };
        for (var slot = 0; slot < 12; slot++)
        {
            Check(SkillKeyBindings.GetSlotKey(SkillQuickbar.Main, slot) == main[slot] &&
                SkillKeyBindings.GetSlotKey(SkillQuickbar.Alt, slot) == numpad[slot] &&
                SkillKeyBindings.GetSlotKey(SkillQuickbar.Ctrl, slot) is null, "all old slots unchanged");
            Check(SkillKeyBindings.GetSlotKey(SkillQuickbar.Alt, slot, QuickbarKeyMode.GameDefault) == "Alt+" + main[slot] &&
                SkillKeyBindings.GetSlotKey(SkillQuickbar.Ctrl, slot, QuickbarKeyMode.GameDefault) == "Ctrl+" + main[slot], "all twelve chord slots map");
        }
        var bindings = new SkillKeyBindings(Bars, Skills, QuickbarKeyMode.GameDefault);
        var settings = new QuickbarSkillScriptSettings { ExecutionTree = new() { Node(102), Node(103), Node(101) } };
        settings.ExecutionTree[1].Children.Add(Node(104));
        var plan = QuickbarSkillPlan.FromSettings(settings, bindings);
        Check(plan.Roots.Select(n => n.Key).SequenceEqual(new[] { "Alt+OemMinus", "Ctrl+OemPlus", "D1" }), "tree order and main duplicate preference retained");
        var child = plan.Roots[1].Children.Single();
        Check(child.Key == "Ctrl+OemPlus" && child.Bar == SkillQuickbar.Ctrl && child.Slot == 11 && child.BaseSkillId == 103, "third-row chain inherits physical slot and anchor");
        Check(QuickbarSkillPlan.FromSettings(settings, new(Bars, Skills)).Roots.Count == 2, "legacy mode omits third-row skill and chain");
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
        var source = new AccountConfig { AccountName = "account2", ScriptSettings = new() { QuickbarKeyMode = QuickbarKeyMode.GameDefault } };
        var worker = new AccountWorkerContext(source.Clone(), api, log, new AccountRuntimeManager(log), new(), CancellationToken.None);
        await worker.PrepareSkillBindingsAsync();
        Check(worker.SkillBindings!.Resolve(103, "")!.Key == "Ctrl+OemPlus", "worker startup applies account mode");
        Check(api.LastQuickbarReadContext?.IncludeThirdQuickbar == true, "startup reader requests third row");
        await ((ISkillAvailabilitySnapshotReader)worker.Snapshots).ReadSkillAvailabilityAsync();
        Check(api.LastSkillAvailabilityReadContext?.IncludeThirdQuickbar == true, "availability reader requests the same mode");
        Check(source.ScriptSettings!.QuickbarKeyMode == QuickbarKeyMode.GameDefault, "startup preserves saved mode");

        source.ScriptSettings.QuickbarKeyMode = QuickbarKeyMode.Numpad;
        var accounts = new AccountRuntimeManager(log);
        accounts.MarkStarting(source);
        var runtime = new RoadhogRuntime(api, log, accounts, null!);
        await runtime.ReadQuickbarForKeyModeAsync(QuickbarKeyMode.GameDefault, "account2");
        Check(api.LastQuickbarReadContext?.IncludeThirdQuickbar == true, "unsaved preview mode reaches provider with active account");
        await runtime.ReadQuickbarForKeyModeAsync(QuickbarKeyMode.Numpad, "account2");
        Check(api.LastQuickbarReadContext?.IncludeThirdQuickbar == false && source.ScriptSettings.QuickbarKeyMode == QuickbarKeyMode.Numpad,
            "returning preview to default reads two rows and does not mutate config");
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
                var numpad = (RadioButton)Get("numpadKeyModeRadio")!;
                var game = (RadioButton)Get("gameDefaultKeyModeRadio")!;
                Check(numpad.Checked && !game.Checked, "old account loads numpad by default");
                typeof(AccountSettingsForm).GetField("previewSkillBindings", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(form, new SkillKeyBindings(Bars, Skills));
                game.Checked = true;
                Check(!numpad.Checked && game.Checked && Get("previewSkillBindings") is null, "radios exclusive and stale preview invalidated");
                Check(((ScriptSettings)Call("CaptureScriptSettings")!).QuickbarKeyMode == QuickbarKeyMode.GameDefault, "capture saves selected mode");
                Check((bool)Call("SaveCurrentSettings", new object?[] { null })!, "actual save succeeds");
                Check(store.LoadAllAsync().GetAwaiter().GetResult().Value!.Single().ScriptSettings!.QuickbarKeyMode == QuickbarKeyMode.GameDefault, "account persistence keeps mode");
                Call("ApplyScriptSettings", new ScriptSettings());
                Check(numpad.Checked && !game.Checked, "loading old profile restores numpad");
                var spiritButton = (Button)Get("spiritmasterSettingsButton")!;
                Check(!game.Parent!.Bounds.IntersectsWith(spiritButton.Bounds), "mode group does not overlap spirit settings");
                foreach (Control control in game.Parent.Controls)
                    Check(control.Right <= game.Parent.Width && control.Bottom <= game.Parent.Height, "radio fits group");

                var storedBefore = JsonSerializer.Serialize(store.LoadAllAsync().GetAwaiter().GetResult().Value);
                game.Checked = true;
                var pending = new TaskCompletionSource<QuickbarSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
                preview.PendingBar = pending.Task;
                var refresh = (Task<(int UpdatedCount, int DeletedCount, bool Saved, string Error)>)Call("RefreshConfiguredSkillsCoreAsync")!;
                Check(!refresh.IsCompleted && preview.RequestedMode == QuickbarKeyMode.GameDefault, "refresh captures unsaved mode before awaiting bar");
                numpad.Checked = true;
                pending.SetResult(Bars);
                var deadline = DateTime.UtcNow.AddSeconds(3);
                while (!refresh.IsCompleted && DateTime.UtcNow < deadline) { System.Windows.Forms.Application.DoEvents(); Thread.Sleep(1); }
                Check(refresh.IsCompleted, "stale refresh terminates");
                var result = refresh.GetAwaiter().GetResult();
                Check(!result.Saved && result.DeletedCount == 0 && Get("previewSkillBindings") is null &&
                    JsonSerializer.Serialize(store.LoadAllAsync().GetAwaiter().GetResult().Value) == storedBefore,
                    "mode change during refresh cannot delete skills or persist stale bindings");
                completion.SetResult();
            }
            catch (Exception ex) { completion.SetException(ex); }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); return completion.Task;
    }

    public class PreviewRuntime : DispatchProxy
    {
        public IRoadhogRuntime Inner { get; set; } = null!;
        public Task<QuickbarSnapshot>? PendingBar { get; set; }
        public QuickbarKeyMode RequestedMode { get; private set; }
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            if (method!.Name == nameof(IRoadhogRuntime.ReadQuickbarForKeyModeAsync) && PendingBar is not null)
            {
                RequestedMode = (QuickbarKeyMode)args![0]!;
                return PendingBar;
            }
            return method.Invoke(Inner, args);
        }
    }
}
