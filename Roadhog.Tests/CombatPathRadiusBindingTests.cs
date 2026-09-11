using System.Collections;
using System.Reflection;
using System.Text.Json;
using System.Windows.Forms;
using Roadhog;
using Roadhog.Application;
using Roadhog.Application.StationaryCombat;
using Roadhog.Application.Workers;
using Roadhog.Core.Accounts;
using Roadhog.Core.Common;
using Roadhog.Core.Diagnostics;
using Roadhog.Core.Paths;
using Roadhog.Infrastructure.Paths;

internal static class CombatPathRadiusBindingTests
{
    public static async Task JsonCompatibilityAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), "roadhog-path-radius-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var store = new JsonSharedPathStore(directory);
            await File.WriteAllTextAsync(Path.Combine(directory, "legacy.json"),
                "{\"Version\":1,\"Name\":\"legacy\",\"Points\":[]}");
            var legacy = await store.LoadAsync("legacy");
            Check(legacy.Success && legacy.Value!.BoundStationaryCombatRadius is null, "old JSON must remain unbound");

            var document = new SharedPathDocument
            {
                Name = "bound",
                BoundStationaryCombatRadius = 12.5,
                CleanupNpcName = "merchant",
                BagCleanupSellItemClickX = 123,
                BagCleanupSellItemClickY = 456,
                BagCleanupSellButtonClickX = 789,
                BagCleanupSellButtonClickY = 321,
                Points = new() { new SharedPathPoint { X = 10, Y = 20, Z = 30 } }
            };
            Check((await store.SaveAsync(document)).Success, "save bound path");
            var loaded = (await store.LoadAsync("bound")).Value!;
            Equal(12.5, loaded.BoundStationaryCombatRadius!.Value, "JSON round trip");
            Equal(12.5, loaded.Clone().BoundStationaryCombatRadius!.Value, "clone binding");
            Equal("merchant", loaded.CleanupNpcName, "NPC metadata");
            Equal(123, loaded.BagCleanupSellItemClickX!.Value, "click metadata");
            Equal(1, loaded.PointCount, "path points");

            loaded.BoundStationaryCombatRadius = null;
            Check((await store.SaveAsync(loaded)).Success, "save disabled binding");
            Check((await store.LoadAsync("bound")).Value!.BoundStationaryCombatRadius is null, "disable must survive reload");
            Check(!(await File.ReadAllTextAsync(Path.Combine(directory, "bound.json")))
                .Contains("BoundStationaryCombatRadius"), "unbound paths omit the new field");
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    public static async Task RuntimeFallbacksAndModesAsync()
    {
        var logger = new InMemoryRoadhogLogger();
        var path = new SharedPathDocument { Name = "combat", BoundStationaryCombatRadius = 12.5 };
        var store = new ObservedPathStore(path);
        foreach (var bound in new double?[] { null, 0, -1, 500.1, double.NaN, double.PositiveInfinity, 1, 500, 12.5 })
        {
            path.BoundStationaryCombatRadius = bound;
            await store.SaveAsync(path);
            var config = Config();
            await CombatPathRadiusBinding.ApplyAsync(config, store, logger, CancellationToken.None);
            var expected = bound.HasValue && double.IsFinite(bound.Value) && bound.Value is >= 1 and <= 500
                ? bound.Value : 33.5;
            Equal(expected, config.ScriptSettings!.Combat.StationaryCombatRadius, "valid binding or old radius");
            AssertOtherRadii(config);
        }

        foreach (var mode in new[] { AccountMainMode.Gather, AccountMainMode.Craft, AccountMainMode.SemiAuto })
        {
            var config = Config();
            config.ScriptSettings!.MainMode = mode;
            var reads = store.LoadCount;
            await CombatPathRadiusBinding.ApplyAsync(config, store, logger, CancellationToken.None);
            Equal(reads, store.LoadCount, "non-combat mode must not read the combat path");
            Equal(33.5, config.ScriptSettings.Combat.StationaryCombatRadius, "non-combat settings unchanged");
        }

        var pathMode = Config();
        pathMode.ScriptSettings!.CombatMode = AccountCombatMode.Path;
        var pathModeReads = store.LoadCount;
        await CombatPathRadiusBinding.ApplyAsync(pathMode, store, logger, CancellationToken.None);
        Equal(pathModeReads, store.LoadCount, "path combat must not apply stationary binding");
        Equal(33.5, pathMode.ScriptSettings.Combat.StationaryCombatRadius, "path mode stationary radius unchanged");
        AssertOtherRadii(pathMode);

        var missing = Config("missing");
        await CombatPathRadiusBinding.ApplyAsync(missing, store, logger, CancellationToken.None);
        Equal(33.5, missing.ScriptSettings!.Combat.StationaryCombatRadius, "missing file keeps profile value");
        Check(logger.Entries.Any(entry => entry.EventName == "stationary_combat.path_radius.load_failed"), "missing file diagnostic");
        Check(logger.Entries.Any(entry => entry.EventName == "stationary_combat.path_radius.invalid"), "invalid binding diagnostic");

        var noPath = Config("");
        noPath.ScriptSettings!.Paths.RevivePathName = string.Empty;
        var readsBeforeEmpty = store.LoadCount;
        await CombatPathRadiusBinding.ApplyAsync(noPath, store, logger, CancellationToken.None);
        Equal(readsBeforeEmpty, store.LoadCount, "no path must not cause file reads");
        Equal(33.5, noPath.ScriptSettings!.Combat.StationaryCombatRadius, "no path keeps profile value");

        var legacyReference = Config("");
        legacyReference.CombatPathName = " combat ";
        await CombatPathRadiusBinding.ApplyAsync(legacyReference, store, logger, CancellationToken.None);
        Equal(12.5, legacyReference.ScriptSettings!.Combat.StationaryCombatRadius, "legacy path-name fallback");

        var legacyAccount = new AccountConfig { AccountName = "legacy", CombatPathName = "combat" };
        await CombatPathRadiusBinding.ApplyAsync(legacyAccount, store, logger, CancellationToken.None);
        Check(legacyAccount.ScriptSettings is null, "do not synthesize new defaults for legacy accounts");
    }

    public static async Task WorkerStartupIsolationAsync()
    {
        await VerifyWorkerStartupIsolationAsync("combat");
        await VerifyWorkerStartupIsolationAsync("revive");
    }

    private static async Task VerifyWorkerStartupIsolationAsync(string boundPathName)
    {
        var store = new ObservedPathStore(new SharedPathDocument { Name = boundPathName, BoundStationaryCombatRadius = 12.5 });
        var config = Config();
        var profileStore = new InMemoryScriptProfileStore(new Roadhog.Core.Profiles.ScriptProfileDocument
        {
            Name = "profile",
            Settings = config.ScriptSettings!.Clone()
        });
        // This is the same profile-before-worker order used by Form1 at account startup.
        config.ScriptSettings = (await profileStore.LoadAsync("profile")).Value!.Settings.Clone();
        var logger = new InMemoryRoadhogLogger();
        var loop = new HoldingWorkerLoop();
        var host = new AccountWorkerHost(new FakeGameApi(), logger, new AccountRuntimeManager(logger),
            loop, new AccountWorkerOptions(), store);
        Check(host.Start(config).Success, "worker start");
        try
        {
            var first = await loop.NextContext.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Equal(12.5, first.Config.ScriptSettings!.Combat.StationaryCombatRadius, "binding reaches worker before loop");
            Equal(33.5, config.ScriptSettings.Combat.StationaryCombatRadius, "caller settings remain unchanged");
            Equal(33.5, (await profileStore.LoadAsync("profile")).Value!.Settings.Combat.StationaryCombatRadius, "profile remains unchanged");
            AssertOtherRadii(first.Config);

            await store.SaveAsync(new SharedPathDocument { Name = boundPathName, BoundStationaryCombatRadius = 18.5 });
            Equal(12.5, first.Config.ScriptSettings.Combat.StationaryCombatRadius, "running worker keeps startup value");
            Check((await host.StopAsync()).Success, "stop first worker");

            loop.NextContext = new(TaskCreationOptions.RunContinuationsAsynchronously);
            Check(host.Start(config).Success, "restart worker");
            var second = await loop.NextContext.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Equal(18.5, second.Config.ScriptSettings!.Combat.StationaryCombatRadius, "restart reloads saved binding");
            Equal(12.5, first.Config.ScriptSettings.Combat.StationaryCombatRadius, "previous session is isolated");
            Check((await host.StopAsync()).Success, "stop second worker");

            await store.SaveAsync(new SharedPathDocument { Name = boundPathName });
            loop.NextContext = new(TaskCreationOptions.RunContinuationsAsynchronously);
            Check(host.Start(config).Success, "restart after unbind");
            var unbound = await loop.NextContext.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Equal(33.5, unbound.Config.ScriptSettings!.Combat.StationaryCombatRadius, "unbind restores original profile radius");
        }
        finally
        {
            await host.StopAsync();
        }
    }

    public static async Task RevivePriorityAndFallbackAsync()
    {
        var store = new ObservedPathStore(
            new SharedPathDocument { Name = "combat", BoundStationaryCombatRadius = 12.5 },
            new SharedPathDocument { Name = "revive", BoundStationaryCombatRadius = 8.5 });
        var logger = new InMemoryRoadhogLogger();
        var config = Config();
        await CombatPathRadiusBinding.ApplyAsync(config, store, logger, CancellationToken.None);
        Equal(8.5, config.ScriptSettings!.Combat.StationaryCombatRadius, "revive binding takes priority over combat binding");
        Equal(1, store.LoadCount, "valid revive binding avoids reading combat path");
        Equal("revive", (string)logger.Entries.Last(entry => entry.EventName == "stationary_combat.path_radius.applied").Fields["pathKind"]!, "log identifies selected binding source");
        AssertOtherRadii(config);

        var reviveOnly = Config("");
        await CombatPathRadiusBinding.ApplyAsync(reviveOnly, store, logger, CancellationToken.None);
        Equal(8.5, reviveOnly.ScriptSettings!.Combat.StationaryCombatRadius, "revive-only configuration applies binding");

        foreach (var radius in new double?[] { null, 0, 501, double.NaN, double.PositiveInfinity })
        {
            await store.SaveAsync(new SharedPathDocument { Name = "revive", BoundStationaryCombatRadius = radius });
            var fallback = Config();
            await CombatPathRadiusBinding.ApplyAsync(fallback, store, logger, CancellationToken.None);
            Equal(12.5, fallback.ScriptSettings!.Combat.StationaryCombatRadius, "unbound or invalid revive path preserves existing combat binding");
            AssertOtherRadii(fallback);
        }

        await store.DeleteAsync("revive");
        var missingRevive = Config();
        await CombatPathRadiusBinding.ApplyAsync(missingRevive, store, logger, CancellationToken.None);
        Equal(12.5, missingRevive.ScriptSettings!.Combat.StationaryCombatRadius, "missing revive file falls back to combat binding");

        await store.SaveAsync(new SharedPathDocument { Name = "combat" });
        var unbound = Config();
        await CombatPathRadiusBinding.ApplyAsync(unbound, store, logger, CancellationToken.None);
        Equal(33.5, unbound.ScriptSettings!.Combat.StationaryCombatRadius, "both unbound keep profile radius");

        var samePath = Config(" combat ");
        samePath.ScriptSettings!.Paths.RevivePathName = "COMBAT";
        var previousReads = store.LoadCount;
        await CombatPathRadiusBinding.ApplyAsync(samePath, store, logger, CancellationToken.None);
        Equal(previousReads + 1, store.LoadCount, "same shared file is read only once even when unbound");

        await store.SaveAsync(new SharedPathDocument { Name = "revive", BoundStationaryCombatRadius = 9.5 });
        var legacyName = Config();
        legacyName.ScriptSettings!.Paths.RevivePathName = "";
        legacyName.RevivePathName = " revive ";
        await CombatPathRadiusBinding.ApplyAsync(legacyName, store, logger, CancellationToken.None);
        Equal(9.5, legacyName.ScriptSettings.Combat.StationaryCombatRadius, "legacy revive path name fallback");
    }

    public static async Task MultipleAccountIsolationAsync()
    {
        var store = new ObservedPathStore(
            new SharedPathDocument { Name = "combat", BoundStationaryCombatRadius = 12.5 },
            new SharedPathDocument { Name = "other", BoundStationaryCombatRadius = 8.5 });
        var logger = new InMemoryRoadhogLogger();
        var runtime = new AccountRuntimeManager(logger);
        var hosts = new List<AccountWorkerHost>();
        try
        {
            var running = new List<AccountWorkerContext>();
            foreach (var (name, path, expected) in new[]
            {
                ("account1", "combat", 12.5), ("account2", "combat", 12.5),
                ("account3", "other", 8.5), ("account4", "", 33.5)
            })
            {
                var config = Config(path);
                config.AccountName = name;
                var loop = new HoldingWorkerLoop();
                var host = new AccountWorkerHost(new FakeGameApi(), logger, runtime, loop, new AccountWorkerOptions(), store);
                hosts.Add(host);
                Check(host.Start(config).Success, "start " + name);
                var context = await loop.NextContext.Task.WaitAsync(TimeSpan.FromSeconds(5));
                running.Add(context);
                Equal(expected, context.Config.ScriptSettings!.Combat.StationaryCombatRadius, "per-account selected path binding");
                Equal(33.5, config.ScriptSettings!.Combat.StationaryCombatRadius, "per-account original settings preserved");
                AssertOtherRadii(context.Config);
            }

            Check(!ReferenceEquals(running[0].Config.ScriptSettings!.Combat, running[1].Config.ScriptSettings!.Combat),
                "accounts sharing a path must have separate settings objects");
            Check((await hosts[0].StopAsync()).Success, "stop one account");
            Check(hosts.Skip(1).All(host => host.IsRunning), "stopping one account does not stop others");
            Equal(12.5, running[1].Config.ScriptSettings!.Combat.StationaryCombatRadius, "other account keeps shared binding");
        }
        finally
        {
            foreach (var host in hosts) await host.StopAsync();
        }
    }

    public static Task UiSaveSwitchAndValidationAsync() => RunSta(() =>
    {
        var store = new ObservedPathStore(
            new SharedPathDocument { Name = "combat", BoundStationaryCombatRadius = 12.5 },
            new SharedPathDocument { Name = "legacy" });
        var configs = new InMemoryAccountConfigStore(Config());
        var profiles = new InMemoryScriptProfileStore();
        using var form = CreateForm(configs, store, profiles);
        var editor = Editor(form, SharedPathKind.Combat);
        var toggle = Find(form, "bindPathStationaryRadiusCheckBox");
        var input = Find(form, "pathBoundStationaryRadiusTextBox");
        Check(Checked(toggle) && input.Enabled, "bound path loads checked and editable");
        Equal("12.5", input.Text, "bound value displayed");
        Control? parent = toggle;
        while (parent is not null && parent is not TabPage) parent = parent.Parent;
        Equal("打怪路径", parent?.Text ?? "", "control only belongs to combat path tab");

        ((TabControl)Field(form, "settingsTabs")).SelectedIndex = 1;
        ((TabControl)Find(form, "pathTabs")).SelectedIndex = 1;
        form.Show();
        System.Windows.Forms.Application.DoEvents();
        Check(toggle.Visible && input.Visible, "new controls visible on combat tab");
        Check(toggle.Right <= input.Left, "checkbox must not overlap radius input");
        Check(input.Right <= input.Parent!.ClientSize.Width, "radius input fits path editor");
        var artifactDirectory = Environment.GetEnvironmentVariable("ROADHOG_TEST_ARTIFACT_DIRECTORY");
        if (!string.IsNullOrWhiteSpace(artifactDirectory))
        {
            Directory.CreateDirectory(artifactDirectory);
            using var bitmap = new System.Drawing.Bitmap(form.Width, form.Height);
            form.DrawToBitmap(bitmap, form.ClientRectangle with { Width = form.Width, Height = form.Height });
            bitmap.Save(Path.Combine(artifactDirectory, "combat-path-radius.png"));
        }

        Invoke(form, "LoadPathByName", editor, "legacy");
        Check(!Checked(toggle) && !input.Enabled, "legacy path clears previous binding");
        Equal("33.5", input.Text, "legacy display uses profile radius");
        SetChecked(toggle, true);
        input.Text = "21.5";
        Save(form, editor);
        Equal(21.5, store.LoadAsync("legacy").Result.Value!.BoundStationaryCombatRadius!.Value, "save-to-list persists binding");

        foreach (var invalid in new[] { "", "abc", "NaN", "Infinity", "0", "-1", "500.1" })
        {
            input.Text = invalid;
            Save(form, editor);
            Equal(21.5, store.LoadAsync("legacy").Result.Value!.BoundStationaryCombatRadius!.Value, "invalid UI value must not overwrite saved path");
        }

        input.Text = "22.5";
        Save(form, editor);
        object?[] saveArguments = { "" };
        var saved = (bool)Invoke(form, "SaveCurrentSettings", saveArguments)!;
        Check(saved, "save account/profile settings: " + saveArguments[0]);
        var account = configs.LoadAllAsync().Result.Value!.Single();
        Equal(33.5, account.ScriptSettings!.Combat.StationaryCombatRadius, "UI must not copy binding into profile fallback");
        AssertOtherRadii(account);
        Equal(33.5, profiles.LoadAsync(account.ProfileName).Result.Value!.Settings.Combat.StationaryCombatRadius, "profile fallback preserved");

        Invoke(form, "LoadPathByName", editor, "combat");
        Equal("12.5", input.Text, "switch back restores that path's binding");
        SetChecked(toggle, false);
        Check(!input.Enabled, "unchecked input disabled");
        Save(form, editor);
        Check(store.LoadAsync("combat").Result.Value!.BoundStationaryCombatRadius is null, "uncheck saves unbound");

        Invoke(form, "LoadPathByName", editor, "legacy");
        Check(Checked(toggle), "other path remains bound");
        var noPathSettings = Config("").ScriptSettings!;
        Invoke(form, "ApplyScriptSettings", noPathSettings);
        Check(!Checked(toggle) && !input.Enabled, "switch to profile without a path clears stale binding");
        Equal("33.5", input.Text, "no-path profile fallback displayed");
        var savedCombo = editor.GetType().GetProperty("SavedPathCombo")!.GetValue(editor)!;
        Equal(-1, (int)savedCombo.GetType().GetProperty("SelectedIndex")!.GetValue(savedCombo)!, "no-path profile clears stale selection");
        form.Close();
    });

    public static Task UiReviveSaveSwitchAndValidationAsync() => RunSta(() =>
    {
        var store = new ObservedPathStore(
            new SharedPathDocument { Name = "combat", BoundStationaryCombatRadius = 12.5 },
            new SharedPathDocument { Name = "revive", BoundStationaryCombatRadius = 7.5 },
            new SharedPathDocument { Name = "old-revive" });
        var configs = new InMemoryAccountConfigStore(Config());
        var profiles = new InMemoryScriptProfileStore();
        using var form = CreateForm(configs, store, profiles);
        var editor = Editor(form, SharedPathKind.Revive);
        var toggle = Find(form, "bindRevivePathStationaryRadiusCheckBox");
        var input = Find(form, "revivePathBoundStationaryRadiusTextBox");
        var combatInput = Find(form, "pathBoundStationaryRadiusTextBox");
        Check(Checked(toggle) && input.Enabled, "revive binding loads enabled");
        Equal("7.5", input.Text, "revive binding displayed");
        Equal("12.5", combatInput.Text, "combat editor retains its independent value");
        ((TabControl)Field(form, "settingsTabs")).SelectedIndex = 1;
        ((TabControl)Find(form, "pathTabs")).SelectedIndex = 0;
        form.Show();
        System.Windows.Forms.Application.DoEvents();
        Check(toggle.Visible && input.Visible && !combatInput.Visible, "revive binding is visible only on revive tab");
        Check(toggle.Right <= input.Left && input.Right <= input.Parent!.ClientSize.Width, "revive controls fit without overlap");
        var artifactDirectory = Environment.GetEnvironmentVariable("ROADHOG_TEST_ARTIFACT_DIRECTORY");
        if (!string.IsNullOrWhiteSpace(artifactDirectory))
        {
            Directory.CreateDirectory(artifactDirectory);
            using var bitmap = new System.Drawing.Bitmap(form.Width, form.Height);
            form.DrawToBitmap(bitmap, new System.Drawing.Rectangle(System.Drawing.Point.Empty, form.Size));
            bitmap.Save(Path.Combine(artifactDirectory, "revive-path-radius.png"));
        }

        SetChecked(toggle, false);
        Check(!input.Enabled, "revive input disables when unchecked");
        Save(form, editor);
        Check(store.LoadAsync("revive").Result.Value!.BoundStationaryCombatRadius is null, "revive unbind persists");
        Equal(12.5, store.LoadAsync("combat").Result.Value!.BoundStationaryCombatRadius!.Value, "revive save leaves combat binding unchanged");

        Invoke(form, "LoadPathByName", editor, "old-revive");
        Check(!Checked(toggle) && !input.Enabled, "legacy revive path defaults unbound");
        Equal("33.5", input.Text, "unbound input shows profile value");
        SetChecked(toggle, true);
        input.Text = "17.55555";
        Save(form, editor);
        Equal(17.55555, store.LoadAsync("old-revive").Result.Value!.BoundStationaryCombatRadius!.Value, "revive binding saves full precision");
        Equal("17.55555", input.Text, "display does not round the saved binding");
        foreach (var invalid in new[] { "abc", "NaN", "Infinity", "0", "501" })
        {
            input.Text = invalid;
            Save(form, editor);
            Equal(17.55555, store.LoadAsync("old-revive").Result.Value!.BoundStationaryCombatRadius!.Value, "invalid revive input must not overwrite saved value");
        }

        Invoke(form, "LoadPathByName", editor, "revive");
        Check(!Checked(toggle) && !input.Enabled, "switch reloads unbound revive path");
        Invoke(form, "LoadPathByName", editor, "old-revive");
        Equal("17.55555", input.Text, "switch back reloads bound revive path");
        object?[] saveArguments = { "" };
        Check((bool)Invoke(form, "SaveCurrentSettings", saveArguments)!, "save account config: " + saveArguments[0]);
        var account = configs.LoadAllAsync().Result.Value!.Single();
        Equal("old-revive", account.ScriptSettings!.Paths.RevivePathName, "revive selection saved to account");
        Equal(33.5, account.ScriptSettings.Combat.StationaryCombatRadius, "profile fallback is not overwritten");
        AssertOtherRadii(account);
        Equal("12.5", combatInput.Text, "revive edits do not change combat editor");
        form.Close();
    });

    public static Task UiPreservesSharedMetadataAsync() => RunSta(() =>
    {
        var store = new ObservedPathStore(new SharedPathDocument
        {
            Name = "combat", BoundStationaryCombatRadius = 12.5,
            CleanupNpcName = "merchant", BagCleanupSellItemClickX = 123,
            BagCleanupSellItemClickY = 456, BagCleanupSellButtonClickX = 789, BagCleanupSellButtonClickY = 321
        });
        using var form = CreateForm(new InMemoryAccountConfigStore(Config()), store, new InMemoryScriptProfileStore());
        var combat = Editor(form, SharedPathKind.Combat);
        var input = Find(form, "pathBoundStationaryRadiusTextBox");
        foreach (var kind in new[] { SharedPathKind.Revive, SharedPathKind.Maintenance, SharedPathKind.Gather })
        {
            var other = Editor(form, kind);
            Invoke(form, "LoadPathByName", other, "combat");
            Check((other.GetType().GetProperty("BindStationaryRadiusCheckBox")!.GetValue(other) is not null) == (kind == SharedPathKind.Revive),
                "only revive and combat path tabs expose the binding control");
            // The other editor now has stale metadata; it must preserve the latest saved binding.
            var newRadius = 20.5 + (int)kind;
            input.Text = newRadius.ToString(System.Globalization.CultureInfo.InvariantCulture);
            Save(form, combat);
            var bound = store.LoadAsync("combat").Result.Value!;
            Equal("merchant", bound.CleanupNpcName, "combat save preserves cleanup NPC");
            Equal(123, bound.BagCleanupSellItemClickX!.Value, "combat save preserves cleanup click points");
            Save(form, other);
            Equal(newRadius, store.LoadAsync("combat").Result.Value!.BoundStationaryCombatRadius!.Value,
                "saving another tab must preserve latest binding");
            Save(form, other);
            Equal(newRadius, store.LoadAsync("combat").Result.Value!.BoundStationaryCombatRadius!.Value,
                "repeated save must not restore stale binding");
        }

        var before = store.LoadAsync("combat").Result.Value!;
        input.Text = "44.5";
        store.FailLoads = true;
        Save(form, combat);
        store.FailLoads = false;
        Equal(before.BoundStationaryCombatRadius, store.LoadAsync("combat").Result.Value!.BoundStationaryCombatRadius,
            "failed metadata read must prevent destructive overwrite");
    });

    private static AccountSettingsForm CreateForm(InMemoryAccountConfigStore configs, ISharedPathStore paths, InMemoryScriptProfileStore profiles)
    {
        var logger = new InMemoryRoadhogLogger();
        var runtime = new RoadhogRuntime(new FakeGameApi(), logger, new AccountRuntimeManager(logger), null!);
        return new AccountSettingsForm("account1", runtime, configs, paths, profiles, new RecordingFolderLauncher(), "test-paths");
    }

    private static object Field(object target, string name) => target.GetType()
        .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;

    private static object Editor(AccountSettingsForm form, SharedPathKind kind) => ((IDictionary)Field(form, "pathEditors"))[kind]!;

    private static object? Invoke(AccountSettingsForm form, string name, params object?[] arguments) => typeof(AccountSettingsForm)
        .GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form, arguments);

    private static Control Find(Control parent, string name) => Descendants(parent).First(control => control.Name == name);
    private static IEnumerable<Control> Descendants(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    private static bool Checked(Control toggle) => (bool)toggle.GetType().GetProperty("Checked")!.GetValue(toggle)!;
    private static void SetChecked(Control toggle, bool value)
    {
        if (Checked(toggle) != value)
            toggle.GetType().GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(toggle, new object[] { EventArgs.Empty });
    }

    private static void Save(AccountSettingsForm form, object editor)
    {
        var task = (Task)Invoke(form, "SavePathAsync", editor)!;
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!task.IsCompleted && DateTime.UtcNow < deadline)
        {
            System.Windows.Forms.Application.DoEvents();
            Thread.Sleep(1);
        }
        Check(task.IsCompleted, "UI save completion timeout");
        task.GetAwaiter().GetResult();
    }

    private static Task RunSta(Action action)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try { action(); completion.SetResult(); }
            catch (Exception ex) { completion.SetException(ex); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }

    private static AccountConfig Config(string pathName = "combat") => new()
    {
        AccountName = "account1",
        ScriptSettings = new ScriptSettings
        {
            MainMode = AccountMainMode.CustomCombat,
            CombatMode = AccountCombatMode.Stationary,
            Combat = new CombatScriptSettings { StationaryCombatRadius = 33.5, PathCombatRadius = 47.5 },
            Paths = new PathScriptSettings
            {
                CombatPathName = pathName,
                RevivePathName = "revive",
                MaintenancePathName = "maintenance",
                RevivePathAggressiveClearRadius = 13.5
            }
        }
    };

    private static void AssertOtherRadii(AccountConfig config)
    {
        Equal(47.5, config.ScriptSettings!.Combat.PathCombatRadius, "path-combat radius unchanged");
        Equal(13.5, config.ScriptSettings.Paths.RevivePathAggressiveClearRadius, "revive clear radius unchanged");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void Equal<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"{message}: expected {expected}, actual {actual}");
    }

    private sealed class HoldingWorkerLoop : IAccountWorkerLoop
    {
        public TaskCompletionSource<AccountWorkerContext> NextContext { get; set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task RunAsync(AccountWorkerContext context)
        {
            NextContext.TrySetResult(context);
            await Task.Delay(Timeout.InfiniteTimeSpan, context.StopToken);
        }
    }

    private sealed class ObservedPathStore : ISharedPathStore
    {
        private readonly InMemorySharedPathStore _inner;
        public int LoadCount { get; private set; }
        public bool FailLoads { get; set; }
        public ObservedPathStore(params SharedPathDocument[] paths) => _inner = new(paths);
        public Task<OperationResult<SharedPathDocument>> LoadAsync(string name, CancellationToken cancellationToken = default)
        {
            LoadCount++;
            return FailLoads ? Task.FromResult(OperationResult<SharedPathDocument>.Fail("injected read failure"))
                : _inner.LoadAsync(name, cancellationToken);
        }
        public Task<OperationResult<IReadOnlyList<SharedPathSummary>>> LoadSummariesAsync(CancellationToken cancellationToken = default) => _inner.LoadSummariesAsync(cancellationToken);
        public Task<OperationResult> SaveAsync(SharedPathDocument path, CancellationToken cancellationToken = default) => _inner.SaveAsync(path, cancellationToken);
        public Task<OperationResult> DeleteAsync(string name, CancellationToken cancellationToken = default) => _inner.DeleteAsync(name, cancellationToken);
    }
}
