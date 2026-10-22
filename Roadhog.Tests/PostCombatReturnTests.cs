using System.Drawing;
using System.Reflection;
using System.Text.Json;
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
using Roadhog.Infrastructure.WorkerProcesses;

internal static class PostCombatReturnTests
{
    public static async Task BoundaryAsync()
    {
        using var timing = new FastTiming();
        foreach (var (enabled, limit, distance, expected) in new[]
        {
            (true, 5d, 4.99f, false), (true, 5d, 5f, false), (true, 5d, 5.01f, true),
            (true, 7.5d, 7.5f, false), (true, 7.5d, 7.51f, true),
            (true, 0d, 2f, false), (true, 0d, 3f, true), (false, 5d, 8f, false)
        })
        {
            using var game = new Fixture(distance);
            game.Settings.Combat.ReturnHomeAfterCombat = enabled;
            game.Settings.Combat.ReturnHomeAfterCombatDistance = limit;
            await game.FinishFight();
            await game.Tick();
            Check(game.State.PostCombatReturningHome == expected,
                $"enabled={enabled}, limit={limit}, distance={distance}: expected return={expected}");
            Check(expected ? game.State.CandidateEntityId == 0 && game.State.IsMovingForward
                : game.State.CandidateEntityId == game.Next.EntityId,
                "outside limit returns despite another aggressive monster; inside limit continues selecting");
        }
    }

    public static async Task ReturnAndDefenseAsync()
    {
        using var timing = new FastTiming();
        using var game = new Fixture(8);
        game.Settings.Combat.SmartPreAimEnabled = true;
        await game.FinishFight();
        await game.Tick();
        Check(game.State.PostCombatReturningHome && game.State.IsMovingForward, "return begins after kill");
        game.MovePlayer(4);
        await game.Tick();
        Check(game.State.PostCombatReturningHome && game.State.CandidateEntityId == 0,
            "crossing trigger distance does not finish return or select a nearby aggressive monster");

        var defender = new WorldObjectSnapshot(300, 300, "attacker", "monster", new(4, 1, 0), 1, 100, 100,
            TargetServerObjectId: 1, IsTargetingLocalPlayer: true);
        game.Api.WorldObjects = new[] { game.Next, defender };
        game.Lock(defender);
        await game.Tick();
        Check(game.State.Fighting && game.State.CurrentTargetEntityId == defender.EntityId &&
            game.State.PostCombatReturningHome, "return preserves local self-defense");
        game.Lock(defender with { CurrentHp = 0 });
        game.Api.WorldObjects = new[] { game.Next };
        await game.FinishFight();
        await game.Tick();
        Check(game.State.PostCombatReturningHome && game.State.IsMovingForward,
            "defense ending inside the trigger distance still resumes the original return");
        game.MovePlayer(2);
        await game.Tick();
        Check(!game.State.PostCombatReturningHome && !game.State.PostCombatReturnPending && !game.State.IsMovingForward,
            "arrival within two meters releases movement and clears the request");
        await game.Tick();
        Check(game.State.CandidateEntityId == game.Next.EntityId, "ordinary selection resumes after arrival");
        Check(game.Logger.Entries.Count(e => e.EventName == "stationary_combat.post_combat_return.started") == 1 &&
            game.Logger.Entries.Count(e => e.EventName == "stationary_combat.post_combat_return.completed") == 1,
            "one persistent return has one start and one completion");
    }

    public static async Task LootAndModeAsync()
    {
        using var timing = new FastTiming();
        foreach (var (start, afterLoot, expected) in new[] { (4f, 8f, true), (8f, 4f, false) })
        {
            using var game = new Fixture(start, loot: true);
            game.Input.AfterPress = key =>
            {
                if (key == "NumPadDecimal")
                {
                    Check(!game.State.PostCombatReturningHome, "loot completes before returning");
                    game.MovePlayer(afterLoot);
                    game.Api.TargetPosition = game.Api.Player.Position;
                }
            };
            await game.FinishFight();
            Check(game.Input.Keys.Contains("NumPadDecimal"), "real loot branch presses loot");
            await game.Tick();
            Check(game.State.PostCombatReturningHome == expected, "threshold uses position after loot, not kill position");
        }
        using (var skipped = new Fixture(8, loot: true))
        {
            skipped.Api.TargetLootableRaw = 0;
            await skipped.FinishFight();
            await skipped.Tick();
            Check(skipped.State.PostCombatReturningHome, "unlootable corpse still completes a battle and returns");
        }
        using (var path = new Fixture(8))
        {
            path.Settings.CombatMode = AccountCombatMode.Path;
            await path.Controller.TickPathAsync(path.Context, path.Plan, path.SemiAuto, path.State);
            Check(!path.State.PostCombatReturnPending && !path.State.PostCombatReturningHome,
                "path combat kill cannot request stationary return");
        }
    }

    public static async Task PriorityAsync()
    {
        using var timing = new FastTiming();
        using var game = new Fixture(8);
        game.Settings.Combat.SmartPreAimEnabled = true;
        game.Settings.Gather.StationaryPriorityEnabled = true;
        game.Settings.Gather.Rules.Add(new() { Enabled = true, GatherSourceId = 400951, Name = "shell", GatherKey = "NumPad1" });
        var gatherTarget = new GatherObjectSnapshot(10, 10010, 400951, "shell", 1, 1, 2, 19,
            new(8, 1, 0), new(8, 1, 0), 1, false, null, DateTimeOffset.Now);
        game.Api.Gather = new GatherSnapshot(1, 100, game.Api.Player.Position, new[] { gatherTarget },
            Array.Empty<GatherCompetitionPlayerSnapshot>(), game.Api.WorldObjects, true, true,
            new LocalGatheringSnapshot(true, false, 0, false, 0, null, null), DateTimeOffset.Now);
        await game.FinishFight();
        game.State.StartSmartPreAimHandoff(game.Next.EntityId, game.Next.ServerObjectId);
        await game.Tick();
        Check(game.State.PostCombatReturningHome && !game.State.HasSmartPreAimHandoff && !game.State.Gather.Active &&
            !game.Input.Keys.Contains("NumPad1"),
            "return clears a previous preaim handoff and takes priority over gathering");

        var defender = new WorldObjectSnapshot(300, 300, "attacker", "monster", new(8, 6, 0), 6, 100, 100,
            TargetServerObjectId: 1, IsTargetingLocalPlayer: true);
        var ordinary = game.Next with { Position = new Vector3Snapshot(8, 1, 0), DistanceToLocalPlayer = 1 };
        game.Api.WorldObjects = new[] { defender, ordinary };
        game.Api.Gather = game.Api.Gather with { NearbyMonsters = game.Api.WorldObjects };
        game.Api.Player = game.Api.Player with { CameraYawDegrees = 180, ActorYawDegrees = 180 };
        game.Input.AfterPress = key => { if (key == "Tab") game.Lock(ordinary); };
        await game.Tick();
        Check(game.Input.Keys.Contains("Tab"), "defense fixture attempts to lock the attacker");
        Check(!game.State.Fighting && game.State.CandidateEntityId == defender.EntityId && game.State.PostCombatReturningHome,
            "a closer aggressive wrong Tab lock cannot divert return into ordinary combat");
    }

    public static async Task LifecycleAndRuntimeAsync()
    {
        using var timing = new FastTiming();
        using (var missing = new Fixture(8))
        {
            missing.Api.TargetEntityId = 0;
            missing.Api.TargetOwnServerObjectId = 0;
            missing.Api.TargetCurrentHp = 0;
            missing.Api.TargetMaxHp = 0;
            await missing.Tick();
            Check(!missing.State.PostCombatReturnPending && !missing.State.PostCombatReturningHome,
                "losing a target is not a confirmed kill");
        }
        using (var disabled = new Fixture(8))
        {
            await disabled.FinishFight(); await disabled.Tick();
            var runtime = new RoadhogRuntime(disabled.Api, disabled.Logger, new AccountRuntimeManager(disabled.Logger),
                null!, stationaryCombatController: disabled.Controller);
            var dispatcher = new RuntimeRpcDispatcher(runtime, "test", "");
            var args = new[] { JsonSerializer.SerializeToElement("test"), JsonSerializer.SerializeToElement(false), JsonSerializer.SerializeToElement(5d) };
            await dispatcher.InvokeAsync(nameof(IRoadhogRuntime.ApplyPostCombatReturnSettings), args, new Progress<string>(), default);
            await disabled.Tick();
            Check(!disabled.State.PostCombatReturningHome && disabled.State.CandidateEntityId == disabled.Next.EntityId,
                "RPC setting update cancels an active return and resumes selection");
            args[0] = JsonSerializer.SerializeToElement("other");
            try { await dispatcher.InvokeAsync(nameof(IRoadhogRuntime.ApplyPostCombatReturnSettings), args, new Progress<string>(), default); throw new Exception("cross-account update accepted"); }
            catch (InvalidOperationException ex) { Check(ex.Message.Contains("another account"), "RPC account guard"); }
        }
        using (var wrongKind = new Fixture(8))
        {
            wrongKind.Api.TargetObjectType = LockedTargetSnapshot.PlayerObjectType;
            await wrongKind.Tick();
            Check(!wrongKind.State.PostCombatReturnPending, "a non-monster lock cannot request return as a kill");
        }
        using (var changed = new Fixture(8))
        {
            changed.Controller.SetPostCombatReturnSettings("other", true, 20);
            await changed.FinishFight(); await changed.Tick();
            Check(changed.State.PostCombatReturningHome, "another account's override is isolated");
            changed.State.EnterDeathRecovery(DateTimeOffset.Now);
            Check(!changed.State.PostCombatReturningHome && !changed.State.PostCombatReturnPending, "death clears return state");
        }
        using (var custom = new Fixture(8))
        {
            custom.Controller.SetPostCombatReturnSettings("test", true, 10);
            await custom.FinishFight(); await custom.Tick();
            Check(!custom.State.PostCombatReturningHome, "live distance override applies to the next battle end");
        }
        using (var cleanup = new Fixture(8))
        {
            await cleanup.FinishFight(); await cleanup.Tick();
            cleanup.State.StartCleanupReturnToCombat();
            Check(!cleanup.State.PostCombatReturningHome && !cleanup.State.PostCombatReturnPending, "cleanup replaces return state");
        }
        using (var liveFight = new Fixture(8))
        {
            liveFight.Api.TargetCurrentHp = 100;
            await liveFight.Tick();
            Check(liveFight.State.Fighting && !liveFight.State.PostCombatReturnPending && !liveFight.State.PostCombatReturningHome,
                "exceeding the distance during a fight does not interrupt the current monster");
        }
        using (var stopped = new Fixture(8))
        {
            await stopped.FinishFight(); await stopped.Tick();
            stopped.Controller.StopWorkerBackgroundWork(stopped.Context, stopped.State);
            Check(!stopped.State.PostCombatReturnPending && !stopped.State.PostCombatReturningHome, "worker exit clears return state");
        }
    }

    public static async Task PersistenceAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "RoadhogPostCombatReturn-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var accountPath = Path.Combine(root, "accounts.json");
            await File.WriteAllTextAsync(accountPath, "{\"Accounts\":[{\"AccountName\":\"test\",\"ScriptSettings\":{\"Combat\":{}}}]}");
            var accounts = new JsonAccountConfigStore(accountPath);
            var account = (await accounts.LoadAllAsync()).Value!.Single();
            Check(!account.ScriptSettings!.Combat.ReturnHomeAfterCombat && account.ScriptSettings.Combat.ReturnHomeAfterCombatDistance == 5,
                "legacy account defaults to unchecked and five meters");
            var profiles = new JsonScriptProfileStore(Path.Combine(root, "profiles"));
            Directory.CreateDirectory(Path.Combine(root, "profiles"));
            await File.WriteAllTextAsync(Path.Combine(root, "profiles", "old.json"), "{\"Name\":\"old\",\"Settings\":{}}");
            var legacy = (await profiles.LoadAsync("old")).Value!.Settings.Combat;
            Check(!legacy.ReturnHomeAfterCombat && legacy.ReturnHomeAfterCombatDistance == 5, "legacy profile defaults");
            foreach (var enabled in new[] { true, false })
            {
                account.ScriptSettings.Combat.ReturnHomeAfterCombat = enabled;
                account.ScriptSettings.Combat.ReturnHomeAfterCombatDistance = 7.5;
                Check((await accounts.UpsertAsync(account)).Success, "save account");
                Check((await profiles.SaveAsync(new ScriptProfileDocument { Name = "custom", Settings = account.ScriptSettings.Clone() })).Success, "save profile");
                foreach (var value in new[] { (await accounts.LoadAllAsync()).Value!.Single().ScriptSettings!.Combat,
                    (await profiles.LoadAsync("custom")).Value!.Settings.Combat })
                    Check(value.ReturnHomeAfterCombat == enabled && value.ReturnHomeAfterCombatDistance == 7.5,
                        "account, profile and clone preserve checkbox and custom distance");
            }
        }
        finally { Directory.Delete(root, true); }
    }

    public static Task SummaryUiAsync()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                using var game = new Fixture(8);
                game.Settings.Combat.ReturnHomeAfterCombat = false;
                var store = new InMemoryAccountConfigStore(game.Context.Config.Clone());
                var profiles = new InMemoryScriptProfileStore();
                var runtime = new RoadhogRuntime(game.Api, game.Logger, new AccountRuntimeManager(game.Logger), null!,
                    stationaryCombatController: game.Controller);
                AccountSettingsForm Create() => new("test", runtime, store, new InMemorySharedPathStore(), profiles,
                    new RecordingFolderLauncher(), "test-paths");
                using var form = Create();
                form.ShowInTaskbar = false; form.StartPosition = FormStartPosition.Manual; form.Location = new(-20000, -20000);
                form.Show(); Application.DoEvents();
                var check = Field<RoundedCheckBox>(form, "returnHomeAfterCombatCheckBox");
                var distance = Field<RoundedTextBox>(form, "returnHomeAfterCombatDistanceTextBox");
                Check(check.Visible && check.Text == "战斗结束回原点" && !check.Checked && distance.Text == "5" && !distance.Enabled,
                    "summary exposes one unchecked option and a five meter editor");
                var mode = Field<RoundedComboBox>(form, "combatModeCombo");
                mode.SelectedIndex = 1; Application.DoEvents();
                Check(!check.Visible && !distance.Visible, "path combat hides the stationary return option");
                mode.SelectedIndex = 0; Application.DoEvents();
                typeof(RoundedCheckBox).GetMethod("OnClick", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(check, new object[] { EventArgs.Empty });
                Check(check.Checked && distance.Enabled, "click enables the distance editor");
                distance.Text = "7.5";
                Save(form, true);
                Check(store.LoadAllAsync().GetAwaiter().GetResult().Value!.Single().ScriptSettings!.Combat is
                    { ReturnHomeAfterCombat: true, ReturnHomeAfterCombatDistance: 7.5 }, "summary saves custom threshold");
                foreach (var invalid in new[] { "-1", "NaN", "Infinity", "abc" })
                { distance.Text = invalid; Save(form, false); }
                distance.Text = "7.5";
                foreach (var size in new[] { new Size(1000, 800), new Size(1200, 900) })
                {
                    form.ClientSize = size; Application.DoEvents();
                    Check(!check.Bounds.IntersectsWith(distance.Bounds) && distance.Right < check.Parent!.ClientSize.Width,
                        "return checkbox and distance do not overlap or clip");
                }
                var preview = Environment.GetEnvironmentVariable("ROADHOG_RETURN_PREVIEW");
                if (!string.IsNullOrEmpty(preview))
                {
                    form.ClientSize = new(1000, 800); Application.DoEvents();
                    using var bitmap = new Bitmap(form.Width, form.Height);
                    form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size)); bitmap.Save(preview);
                }
                using var reopened = Create();
                Check(Field<RoundedCheckBox>(reopened, "returnHomeAfterCombatCheckBox").Checked &&
                    Field<RoundedTextBox>(reopened, "returnHomeAfterCombatDistanceTextBox").Text == "7.5" &&
                    Field<RoundedTextBox>(reopened, "returnHomeAfterCombatDistanceTextBox").Enabled, "reopen restores both values and editing");
                Field<RoundedCheckBox>(reopened, "returnHomeAfterCombatCheckBox").Checked = false;
                Save(reopened, true);
                Check(store.LoadAllAsync().GetAwaiter().GetResult().Value!.Single().ScriptSettings!.Combat.ReturnHomeAfterCombatDistance == 7.5,
                    "unchecking retains chosen distance");
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        return Task.CompletedTask;
    }

    private static T Field<T>(AccountSettingsForm form, string name) =>
        (T)typeof(AccountSettingsForm).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(form)!;

    private static void Save(AccountSettingsForm form, bool expected)
    {
        var args = new object?[] { null };
        var result = (bool)typeof(AccountSettingsForm).GetMethod("SaveCurrentSettings", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(form, args)!;
        Check(result == expected, "save result: " + args[0]);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly CancellationTokenSource _timeout = new(TimeSpan.FromSeconds(30));
        public ScriptSettings Settings { get; } = new()
        {
            MainMode = AccountMainMode.CustomCombat, CombatMode = AccountCombatMode.Stationary,
            Combat = new() { ReturnHomeAfterCombat = true, ReturnHomeWhenNoTarget = false, HasStationaryCombatPosition = true, StationaryCombatRadius = 60 },
            Maintenance = new() { SitMaintenanceEnabled = false }
        };
        public FakeGameApi Api { get; } = new();
        public RecordingKeyboardInput Input { get; } = new();
        public InMemoryRoadhogLogger Logger { get; } = new();
        public StationaryCombatState State { get; } = new() { Fighting = true, CurrentTargetEntityId = 100, CurrentTargetServerObjectId = 100 };
        public SemiAutoCombatState SemiAuto { get; } = new();
        public StationaryCombatController Controller { get; }
        public AccountWorkerContext Context { get; }
        public SemiAutoSkillPlan Plan { get; }
        public WorldObjectSnapshot Next { get; } = new(200, 200, "nearby-aggressive", "monster", new(3, 3, 0), 3, 100, 100, AggressiveKnown: true, IsAggressiveToPlayer: true);

        public Fixture(float distance, bool loot = false)
        {
            Settings.Combat.EnableLoot = loot;
            Api.Player = new(1, 100, "test", 100, 100, 100, 100, 0, new(distance, 0, 0), DateTimeOffset.Now, 270, 10, 270);
            Api.WorldObjects = new[] { Next };
            Lock(new(100, 100, "killed", "monster", Api.Player.Position, 0, 0, 100));
            Api.TargetLootableRaw = 1;
            State.MarkCandidate(100, 100, DateTimeOffset.Now);
            State.MarkStartupRecoveryChecked();
            Controller = new(Input, new SemiAutoCombatController(Input));
            Context = new(new AccountConfig { AccountName = "test", ScriptSettings = Settings }, Api, Logger,
                new AccountRuntimeManager(Logger), new AccountWorkerOptions(), _timeout.Token) { WorkflowOwnsCleanup = true };
            Plan = SemiAutoSkillPlan.FromSettings(Settings.Skills);
        }
        public void MovePlayer(float distance) => Api.Player = Api.Player with { Position = new Vector3Snapshot(distance, 0, 0) };
        public void Lock(WorldObjectSnapshot target)
        {
            Api.TargetEntityId = target.EntityId; Api.TargetOwnServerObjectId = target.ServerObjectId;
            Api.TargetCurrentHp = target.CurrentHp; Api.TargetMaxHp = target.MaxHp; Api.TargetName = target.Name;
            Api.TargetPosition = target.Position; Api.TargetServerObjectId = target.TargetServerObjectId;
            Api.TargetIsTargetingLocalPlayer = target.IsTargetingLocalPlayer;
        }
        public Task<TimeSpan> Tick() => Controller.TickAsync(Context, Plan, SemiAuto, State);
        public async Task FinishFight()
        {
            await Tick();
            for (var i = 0; State.LootAfterKill.Active && i < 20; i++) await Tick();
            Check(!State.LootAfterKill.Active && !State.Fighting, "kill and loot fixture must finish");
        }
        public void Dispose()
        {
            Controller.StopWorkerBackgroundWork(Context, State);
            _timeout.Cancel();
            _timeout.Dispose();
        }
    }

    private sealed class FastTiming : IDisposable
    {
        private readonly Dictionary<string, string?> _before = new();
        public FastTiming()
        {
            foreach (var (key, value) in new[] { ("ROADHOG_LOOT_AFTER_KILL_WAIT_MS", "0"), ("ROADHOG_LOOT_AFTER_PICK_WAIT_MS", "0"),
                ("ROADHOG_LOOT_PRESS_INTERVAL_MS", "0"), ("ROADHOG_STATIONARY_TAB_VERIFY_DELAY_MS", "0"), ("AION_FACE_TARGET_BEARING_MODE", "y-x") })
            { _before[key] = Environment.GetEnvironmentVariable(key); Environment.SetEnvironmentVariable(key, value); }
        }
        public void Dispose() { foreach (var (key, value) in _before) Environment.SetEnvironmentVariable(key, value); }
    }
    private static void Check(bool valid, string message) { if (!valid) throw new InvalidOperationException(message); }
}
