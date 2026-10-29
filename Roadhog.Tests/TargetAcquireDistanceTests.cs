using System.Reflection;
using System.Windows.Forms;
using Roadhog;
using Roadhog.Application;
using Roadhog.Application.SemiAuto;
using Roadhog.Application.StationaryCombat;
using Roadhog.Application.Workers;
using Roadhog.Core.Accounts;
using Roadhog.Core.Diagnostics;
using Roadhog.Core.Model;
using Roadhog.Core.Profiles;
using Roadhog.Infrastructure.Config;
using Roadhog.Infrastructure.Profiles;

internal static class TargetAcquireDistanceTests
{
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }

    public static async Task PersistenceAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "RoadhogAcquireDistance-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "accounts.json");
            await File.WriteAllTextAsync(path, "{\"Accounts\":[{\"AccountName\":\"test\",\"ScriptSettings\":{\"Combat\":{}}}]}");
            var accounts = new JsonAccountConfigStore(path);
            var account = (await accounts.LoadAllAsync()).Value!.Single();
            Check(account.ScriptSettings!.Combat.TargetAcquireDistance == 34, "legacy accounts default to 34 meters");
            var profiles = new JsonScriptProfileStore(Path.Combine(root, "profiles"));
            Directory.CreateDirectory(Path.Combine(root, "profiles"));
            await File.WriteAllTextAsync(Path.Combine(root, "profiles", "old.json"), "{\"Name\":\"old\",\"Settings\":{}}");
            Check((await profiles.LoadAsync("old")).Value!.Settings.Combat.TargetAcquireDistance == 34,
                "legacy profiles default to 34 meters");
            account.ScriptSettings.Combat.TargetAcquireDistance = 22.5;
            Check((await accounts.UpsertAsync(account)).Success, "account save");
            Check((await profiles.SaveAsync(new ScriptProfileDocument { Name = "custom", Settings = account.ScriptSettings.Clone() })).Success,
                "profile save");
            Check((await accounts.LoadAllAsync()).Value!.Single().ScriptSettings!.Combat.TargetAcquireDistance == 22.5 &&
                (await profiles.LoadAsync("custom")).Value!.Settings.Combat.TargetAcquireDistance == 22.5,
                "account and profile cloning/persistence retain custom distance");
        }
        finally { Directory.Delete(root, true); }
    }

    public static async Task AcquisitionBoundaryAsync()
    {
        var bearing = Environment.GetEnvironmentVariable("AION_FACE_TARGET_BEARING_MODE");
        Environment.SetEnvironmentVariable("AION_FACE_TARGET_BEARING_MODE", "y-x");
        try
        {
            foreach (var (limit, distance, tab) in new (double, float, bool)[]
            {
                (30, 26, true), (30, 30, true), (30, 30.1f, false),
                (20, 20, true), (20, 21, false), (35, 34, true),
                (double.NaN, 26, true), (0, 26, true)
            })
            {
                using var h = new Harness(distance);
                h.Settings.Combat.TargetAcquireDistance = limit;
                await h.Controller.TickAsync(h.Context, h.Plan, h.SemiState, h.State);
                Check(h.Input.Keys.Contains("Tab") == tab, $"limit={limit}, target={distance}: expected Tab={tab}");
                Check(h.Input.KeyDowns.Contains("W") == !tab, "only targets outside the configured acquisition distance require approach");
                Check(h.Settings.Combat.StationaryCombatRadius == 100 && h.Settings.Combat.PathFollowReachDistance == 5,
                    "acquisition distance does not mutate search radius or waypoint precision");
            }
        }
        finally { Environment.SetEnvironmentVariable("AION_FACE_TARGET_BEARING_MODE", bearing); }
    }

    public static Task OverviewUiAsync()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                using var h = new Harness(26);
                var store = new InMemoryAccountConfigStore(h.Context.Config.Clone());
                var runtime = new RoadhogRuntime(h.Api, h.Logger, new AccountRuntimeManager(h.Logger), null!,
                    stationaryCombatController: h.Controller);
                AccountSettingsForm Create() => new("test", runtime, store, new InMemorySharedPathStore(),
                    new InMemoryScriptProfileStore(), new RecordingFolderLauncher(), "test-paths");
                using var form = Create();
                form.ShowInTaskbar = false; form.StartPosition = FormStartPosition.Manual; form.Location = new(-20000, -20000);
                form.Show(); Application.DoEvents();
                var input = form.Controls.Find("targetAcquireDistanceTextBox", true).Single();
                var ancestor = input.Parent;
                while (ancestor is not null && ancestor is not TabPage) ancestor = ancestor.Parent;
                Check(ancestor?.Text == "总览" && input.Visible && input.Enabled && input.Text == "34",
                    "overview exposes the enabled 34-meter acquisition editor");
                Check(input.Parent!.ClientRectangle.Contains(input.Bounds) &&
                    !input.Parent.Controls.Cast<Control>().Any(c => c != input && c.Visible && c.Bounds.IntersectsWith(input.Bounds)),
                    "acquisition editor fits without overlapping neighboring controls");
                var mode = (RoundedComboBox)typeof(AccountSettingsForm).GetField("combatModeCombo", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
                mode.SelectedIndex = 1; Application.DoEvents();
                Check(input.Visible && input.Enabled, "path combat shares the same overview setting");
                mode.SelectedIndex = 0; Application.DoEvents();
                input.Text = "22.5";
                Save(form, true);
                Check(store.LoadAllAsync().Result.Value!.Single().ScriptSettings!.Combat.TargetAcquireDistance == 22.5,
                    "overview saves the configured acquisition distance");
                foreach (var invalid in new[] { "0", "-1", "501", "NaN", "Infinity", "abc" })
                {
                    input.Text = invalid; Save(form, false);
                    Check(store.LoadAllAsync().Result.Value!.Single().ScriptSettings!.Combat.TargetAcquireDistance == 22.5,
                        "invalid input cannot overwrite the saved distance");
                }
                using var reopened = Create();
                Check(reopened.Controls.Find("targetAcquireDistanceTextBox", true).Single().Text == "22.5", "reopening restores the distance");
                form.Close();
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        return Task.CompletedTask;
    }

    private static void Save(AccountSettingsForm form, bool expected)
    {
        var args = new object?[] { null };
        var saved = (bool)typeof(AccountSettingsForm).GetMethod("SaveCurrentSettings", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(form, args)!;
        Check(saved == expected, "save result: " + args[0]);
    }

    private sealed class Harness : IDisposable
    {
        private readonly CancellationTokenSource _stop = new(TimeSpan.FromSeconds(10));
        public readonly ScriptSettings Settings = new()
        {
            MainMode = AccountMainMode.CustomCombat, CombatMode = AccountCombatMode.Stationary,
            Combat = new() { HasStationaryCombatPosition = true, StationaryCombatRadius = 100 },
            Maintenance = new() { SitMaintenanceEnabled = false }
        };
        public readonly FakeGameApi Api;
        public readonly RecordingKeyboardInput Input = new();
        public readonly InMemoryRoadhogLogger Logger = new();
        public readonly StationaryCombatState State = new();
        public readonly SemiAutoCombatState SemiState = new();
        public readonly StationaryCombatController Controller;
        public readonly AccountWorkerContext Context;
        public readonly SemiAutoSkillPlan Plan;
        public Harness(float distance)
        {
            Api = new()
            {
                Player = new(1, 0, "test", 100, 100, 100, 100, 0, new(0, 0, 0), DateTimeOffset.Now, 90, 10, 90),
                TargetEntityId = 0, TargetPosition = new(distance, 0, 0),
                WorldObjects = new[] { new WorldObjectSnapshot(100, 100, "target", "monster", new(distance, 0, 0), distance, 100, 100) },
                Skills = Array.Empty<SkillSnapshot>()
            };
            Input.AfterPress = key => { if (key == "Tab") { Api.TargetEntityId = 100; Api.TargetOwnServerObjectId = 100; } };
            Controller = new(Input, new SemiAutoCombatController(Input));
            Context = new(new() { AccountName = "test", ScriptSettings = Settings }, Api, Logger, new AccountRuntimeManager(Logger), new(), _stop.Token);
            Plan = SemiAutoSkillPlan.FromSettings(Settings.Skills);
            State.MarkStartupRecoveryChecked();
        }
        public void Dispose() { _stop.Cancel(); Controller.StopWorkerBackgroundWork(Context, State); _stop.Dispose(); }
    }
}
