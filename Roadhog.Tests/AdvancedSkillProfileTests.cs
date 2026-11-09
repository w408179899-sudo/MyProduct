using System.Reflection;
using System.Text.Json;
using System.Windows.Forms;
using Roadhog;
using Roadhog.Application;
using Roadhog.Application.SemiAuto;
using Roadhog.Application.StationaryCombat;
using Roadhog.Application.Workers;
using Roadhog.Core.Model;
using Roadhog.Core.Accounts;
using Roadhog.Core.Common;
using Roadhog.Core.Diagnostics;
using Roadhog.Core.Profiles;
using Roadhog.Infrastructure.Profiles;

internal static class AdvancedSkillProfileTests
{
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static AdvancedSkillSettings Content(string monster = "怪物甲") => new()
    {
        Enabled = true,
        Elite = new() { MonsterNames = new() { monster }, Skills = new() { new() { SkillId = 101, Name = "测试技能 III",
            Children = new() { new() { SkillId = 102, Name = "后续技能" } } } } }
    };
    private sealed class Files : IDisposable
    {
        public string Directory { get; } = Path.Combine(Path.GetTempPath(), "Roadhog-advanced-profiles-" + Guid.NewGuid().ToString("N"));
        public string PathName => Path.Combine(Directory, "advanced-skill-profiles.json");
        public JsonAdvancedSkillProfileStore Store => new(PathName);
        public void Dispose() { if (System.IO.Directory.Exists(Directory)) System.IO.Directory.Delete(Directory, true); }
    }

    public static async Task StoreAsync()
    {
        using var files = new Files(); var first = files.Store; var second = files.Store;
        Check((await first.LoadAllAsync()).Value!.Count == 0 && !File.Exists(files.PathName), "empty library does not create or migrate configurations");
        var saved = await first.SaveAsync(new() { Name = " 精灵 / 山谷 ", Settings = Content() });
        Check(saved.Success && saved.Value!.Name == "精灵 / 山谷" && !saved.Value.Settings.Enabled, "name is independent of path and master enable is never shared");
        var read = (await second.LoadAllAsync()).Value!.Single();
        Check(read.Settings.Elite.Skills.Single().Children.Single().SkillId == 102, "independent stores retain complete skill trees");
        read.Settings.Elite.MonsterNames.Add("怪物乙");
        var updated = await second.SaveAsync(read); Check(updated.Success, "existing revision saves changes");
        Check(!(await first.SaveAsync(saved.Value!)).Success, "stale editor cannot overwrite a newer save");
        Check(!(await first.SaveAsync(new() { Name = saved.Value!.Name, Settings = Content() })).Success, "new and save-as cannot overwrite an existing name");
        Check(!(await first.DeleteAsync(saved.Value!.Name, saved.Value.Revision)).Success, "stale delete cannot remove a changed profile");
        var duplicate = Content(); duplicate.Ranged.MonsterNames.Add("怪物甲");
        Check(!(await first.SaveAsync(new() { Name = "duplicate", Settings = duplicate })).Success, "duplicate monster categories remain rejected");
        var copies = await Task.WhenAll(Enumerable.Range(0, 8).Select(i => files.Store.SaveAsync(new() { Name = "copy" + i, Settings = Content() })));
        Check(copies.All(r => r.Success) && (await first.LoadAllAsync()).Value!.Count == 9, "concurrent editors preserve other names");
        Check((await second.DeleteAsync(updated.Value!.Name, updated.Value.Revision)).Success, "latest revision can be deleted");
        foreach (var broken in new[] { "{broken", "{}", "{\"Version\":2,\"Profiles\":[]}" })
        {
            await File.WriteAllTextAsync(files.PathName, broken);
            Check(!(await first.LoadAllAsync()).Success && !(await first.SaveAsync(new() { Name = "new", Settings = Content() })).Success,
                "invalid library is not replaced by an empty one");
            Check(await File.ReadAllTextAsync(files.PathName) == broken, "failed read and save preserve bytes");
        }
    }

    public static async Task StartupAsync()
    {
        using var files = new Files(); var store = files.Store;
        var saved = (await store.SaveAsync(new() { Name = "共享", Settings = Content() })).Value!;
        using (var workerFixture = new SharedSkillAuditFixture())
        {
            workerFixture.Context.Config.AdvancedSkillProfileName = "共享";
            workerFixture.Context.Config.AdvancedSkillProfileEnabled = true;
            var observed = false;
            workerFixture.Keyboard.AfterPress = key =>
            {
                if (key != "F1") return;
                observed = workerFixture.Settings.AdvancedSkills.Elite.MonsterNames.Single() == "怪物甲";
                workerFixture.Cancel();
            };
            var worker = new DefaultAccountWorkerLoop(workerFixture.Keyboard, workerFixture.Controller,
                new StationaryCombatController(workerFixture.Keyboard, workerFixture.Controller), advancedSkillProfiles: store);
            try { await worker.RunAsync(workerFixture.Context); } catch (OperationCanceledException) { }
            Check(observed, "actual worker entry resolves the selected shared plan before combat input");
        }
        var original = new AccountConfig { AccountName = "a", AdvancedSkillProfileName = "共享", AdvancedSkillProfileEnabled = true,
            ScriptSettings = new() { AdvancedSkills = Content("过期副本") } };
        original.ScriptSettings.Paths.CombatPathName = "path-a";
        original.ScriptSettings.QuickbarSkills.ExecutionTree.Add(new() { SkillId = 900, Name = "普通技能" });
        var first = original.Clone(); var other = original.Clone(); other.AccountName = "b";
        await AdvancedSkillProfileLoader.ApplyAsync(first, store, NoOpRoadhogLogger.Instance);
        await AdvancedSkillProfileLoader.ApplyAsync(other, store, NoOpRoadhogLogger.Instance);
        Check(first.ScriptSettings!.AdvancedSkills.Elite.MonsterNames.Single() == "怪物甲" && other.ScriptSettings!.AdvancedSkills.Enabled,
            "both scripts resolve the same shared profile instead of copied inline settings");
        saved.Settings.Elite.MonsterNames.Add("怪物乙"); saved = (await store.SaveAsync(saved)).Value!;
        var restarted = original.Clone(); await AdvancedSkillProfileLoader.ApplyAsync(restarted, store, NoOpRoadhogLogger.Instance);
        Check(restarted.ScriptSettings!.AdvancedSkills.Elite.MonsterNames.Count == 2 && first.ScriptSettings.AdvancedSkills.Elite.MonsterNames.Count == 1,
            "next start sees changes without mutating running snapshots");
        Check(restarted.ScriptSettings.Paths.CombatPathName == "path-a" && restarted.ScriptSettings.QuickbarSkills.ExecutionTree.Single().SkillId == 900,
            "shared advanced profile never changes paths or ordinary skills");
        other.AdvancedSkillProfileEnabled = false;
        await AdvancedSkillProfileLoader.ApplyAsync(other, store, NoOpRoadhogLogger.Instance);
        Check(!other.ScriptSettings!.AdvancedSkills.Enabled && first.ScriptSettings.AdvancedSkills.Enabled, "master enable is account-local");
        var local = original.Clone(); local.AdvancedSkillProfileName = "";
        await AdvancedSkillProfileLoader.ApplyAsync(local, null, NoOpRoadhogLogger.Instance);
        Check(local.ScriptSettings!.AdvancedSkills.Elite.MonsterNames.Single() == "过期副本", "old local configurations retain exact prior behavior");
        await store.DeleteAsync(saved.Name, saved.Revision);
        await AdvancedSkillProfileLoader.ApplyAsync(restarted, store, NoOpRoadhogLogger.Instance);
        Check(!restarted.ScriptSettings.AdvancedSkills.Enabled, "deleted shared profile disables advanced flow rather than using stale contents");
        await File.WriteAllTextAsync(files.PathName, "{broken");
        await AdvancedSkillProfileLoader.ApplyAsync(restarted, store, NoOpRoadhogLogger.Instance);
        Check(!restarted.ScriptSettings.AdvancedSkills.Enabled, "corrupt shared profile preserves normal combat flow");
        var roundtrip = JsonSerializer.Deserialize<AccountConfig>(JsonSerializer.Serialize(original))!.Clone();
        Check(roundtrip.AdvancedSkillProfileName == "共享" && roundtrip.AdvancedSkillProfileEnabled, "account/RPC clones retain selection and enable");
    }

    public static Task UiAsync() => OnSta(() =>
    {
        using var files = new Files(); var library = files.Store;
        var accounts = new InMemoryAccountConfigStore(Account("a"), Account("b"));
        var profiles = new InMemoryScriptProfileStore();
        AccountSettingsForm Form(string name) => NewForm(name, accounts, profiles, library);
        using (var a = Form("a"))
        using (var dialog = Open(a))
        {
            Find<RoundedTextBox>(dialog, "advancedProfileName").Text = "山谷 精灵";
            Find<RoundedCheckBox>(dialog, "advancedSkillsEnabled").Checked = true;
            Save(dialog);
        }
        var shared = library.LoadAllAsync().Result.Value!.Single();
        Check(!shared.Settings.Enabled && shared.Settings.Elite.MonsterNames.Single() == "怪物甲", "create from original local settings shares only contents");
        Check(!profiles.LoadAsync("a").Result.Value!.Settings.AdvancedSkills.Enabled,
            "selecting and enabling shared advanced skills cannot enable unselected accounts using the same full script profile");
        using (var b = Form("b"))
        using (var dialog = Open(b))
        {
            var combo = Find<RoundedComboBox>(dialog, "advancedProfileCombo"); combo.SelectedItem = shared.Name;
            Check(!Find<RoundedCheckBox>(dialog, "advancedSkillsEnabled").Checked, "selecting a shared plan does not enable another account");
            var card = Find<Control>(dialog, "advancedCard_Elite");
            Check(Find<FlowLayoutPanel>(card, "advancedSkillRows").Controls.Count == 1, "dropdown loads shared skill order");
            if (Environment.GetEnvironmentVariable("ROADHOG_ADVANCED_UI_PREVIEW") is { Length: > 0 } preview)
            {
                Directory.CreateDirectory(preview);
                using var bitmap = new System.Drawing.Bitmap(dialog.Width, dialog.Height);
                dialog.DrawToBitmap(bitmap, new(System.Drawing.Point.Empty, dialog.Size));
                bitmap.Save(Path.Combine(preview, "04-共享高级方案.png"));
            }
            Save(dialog);
        }
        var savedAccounts = accounts.LoadAllAsync().Result.Value!;
        Check(savedAccounts.All(a => a.AdvancedSkillProfileName == shared.Name) && savedAccounts.Single(a => a.AccountName == "a").AdvancedSkillProfileEnabled &&
            !savedAccounts.Single(a => a.AccountName == "b").AdvancedSkillProfileEnabled, "each account persists its own selection and switch");
        Check(library.LoadAllAsync().Result.Value!.Single().Revision == shared.Revision, "selecting a plan does not rewrite its contents");
        using (var a = Form("a"))
        using (var dialog = Open(a))
        {
            Find<Button>(dialog, "advancedProfileDelete").PerformClick();
            Check(Find<Label>(dialog, "advancedSkillStatus").Text.Contains("请先切换"), "referenced plans cannot be accidentally deleted");
            Find<Button>(dialog, "advancedProfileSaveAs").PerformClick();
            Find<RoundedTextBox>(dialog, "advancedProfileName").Text = "精灵 小号";
            Find<RoundedCheckBox>(Find<Control>(dialog, "advancedCard_Elite"), "advancedGroupEnabled").Checked = false;
            Save(dialog);
        }
        var all = library.LoadAllAsync().Result.Value!;
        Check(all.Count == 2 && all.Single(p => p.Name == shared.Name).Settings.Elite.Enabled && !all.Single(p => p.Name == "精灵 小号").Settings.Elite.Enabled,
            "save-as preserves the original shared plan and retains independent group switches");
        using (var b = Form("b"))
        using (var dialog = Open(b))
        {
            Find<Button>(dialog, "advancedProfileNew").PerformClick();
            Check(Find<FlowLayoutPanel>(Find<Control>(dialog, "advancedCard_Elite"), "advancedSkillRows").Controls.Count == 0, "new creates an empty draft");
            dialog.Close();
        }
        Check(library.LoadAllAsync().Result.Value!.Count == 2 && accounts.LoadAllAsync().Result.Value!.Single(a => a.AccountName == "b").AdvancedSkillProfileName == shared.Name,
            "cancelled new draft does not create a plan or change selection");
        using var firstWindow = Form("b"); using var secondWindow = Form("b");
        using var first = Open(firstWindow); using var second = Open(secondWindow);
        Find<RoundedCheckBox>(Find<Control>(first, "advancedCard_Ranged"), "advancedGroupEnabled").Checked = false; Save(first);
        Find<RoundedCheckBox>(Find<Control>(second, "advancedCard_Melee"), "advancedGroupEnabled").Checked = false;
        Find<Button>(second, "advancedSkillSave").PerformClick();
        Check(second.DialogResult != DialogResult.OK && Find<Label>(second, "advancedSkillStatus").Text.Contains("已被修改"), "stale GUI save is blocked without closing the editor");
        Check(library.LoadAllAsync().Result.Value!.Single(p => p.Name == shared.Name).Settings.Melee.Enabled, "stale dialog does not overwrite newer settings");
    });

    public static async Task AccountBindingsAsync()
    {
        using var files = new Files(); var store = files.Store;
        AdvancedSkillProfile? shared = null;
        foreach (var slot in new[] { 0, 4 })
        {
            using var fixture = new SharedSkillAuditFixture();
            fixture.Settings.Skills.SpiritmasterAutoSkillLogicEnabled = false;
            fixture.Settings.QuickbarSkills.ExecutionTree.Clear();
            if (shared is null)
            {
                var settings = Content(fixture.Api.TargetName);
                settings.Elite.Skills = new() { new() { SkillId = SharedSkillAuditFixture.AttackId, Name = "attack" } };
                shared = (await store.SaveAsync(new() { Name = "绑定测试", Settings = settings })).Value!;
            }
            fixture.Context.Config.AdvancedSkillProfileName = shared.Name;
            fixture.Context.Config.AdvancedSkillProfileEnabled = true;
            fixture.Api.Quickbar = fixture.Api.Quickbar with { Slots = fixture.Api.Quickbar.Slots.Select(s => s.SkillId == SharedSkillAuditFixture.AttackId ? s with { Slot = slot } : s).ToArray() };
            fixture.Api.SkillAvailability = fixture.Api.SkillAvailability with { BindingSlots = fixture.Api.Quickbar.Slots.Select(s => new SkillAvailabilityBindingSnapshot(s.Bar, s.Slot, 21, s.SkillId, s.SkillId)).ToArray() };
            await AdvancedSkillProfileLoader.ApplyAsync(fixture.Context.Config, store, fixture.Logger);
            await fixture.PrepareAsync(); await fixture.TickAsync();
            Check(fixture.Keyboard.Keys.SequenceEqual(new[] { slot == 0 ? "D1" : "D5" }), "shared skill uses each account's own current quickbar binding");
        }
        Check((await store.LoadAllAsync()).Value!.Single().Revision == shared!.Revision, "per-account binding never modifies shared configuration");
    }

    public static Task RollbackAndRefreshAsync() => OnSta(() =>
    {
        using var files = new Files(); var library = files.Store;
        var accounts = new FailingAccounts(new InMemoryAccountConfigStore(Account("a")));
        using var form = NewForm("a", accounts, new InMemoryScriptProfileStore(), library);
        using (var dialog = Open(form))
        {
            Find<RoundedTextBox>(dialog, "advancedProfileName").Text = "失败创建";
            Find<Button>(dialog, "advancedSkillSave").PerformClick();
            Check(dialog.DialogResult != DialogResult.OK && library.LoadAllAsync().Result.Value!.Count == 0, "failed account save rolls back newly created shared profile");
            accounts.Fail = false; Find<RoundedTextBox>(dialog, "advancedProfileName").Text = "成功创建"; Save(dialog);
        }
        var initial = library.LoadAllAsync().Result.Value!.Single();
        using (var dialog = Open(form))
        {
            Find<RoundedCheckBox>(Find<Control>(dialog, "advancedCard_Melee"), "advancedGroupEnabled").Checked = false;
            accounts.Fail = true; Find<Button>(dialog, "advancedSkillSave").PerformClick();
            Check(library.LoadAllAsync().Result.Value!.Single().Settings.Melee.Enabled, "failed account save restores an existing profile");
            accounts.Fail = false; Save(dialog);
        }
        // Existing refresh-all still writes the selected advanced plan through its revision guard.
        var skills = new[] { SharedSkillAuditFixture.Skill(201, "测试技能 IV", 1) with { DisplayBaseName = "测试技能" } };
        var bindings = new SkillKeyBindings(new(0, new Roadhog.Core.Model.QuickbarSlotSnapshot[] { new(Roadhog.Core.Model.SkillQuickbar.Main, 0, 21, 201) }), skills);
        Set(form, "currentManualSkills", skills); Set(form, "previewSkillBindings", bindings);
        var result = ((int UpdatedCount, int DeletedCount, bool Saved, string Error))Call(form, "RefreshAndSaveConfiguredSkills")!;
        Check(result.Saved && library.LoadAllAsync().Result.Value!.Single().Settings.Elite.Skills.Single().SkillId == 201,
            "manual refresh-all updates the selected shared advanced profile to the actual bar rank");
    });

    private static AccountConfig Account(string name)
    {
        var advanced = Content(); advanced.Enabled = false;
        return new() { AccountName = name, ProfileName = name, ScriptSettings = new() { ProfileName = name, AdvancedSkills = advanced } };
    }
    private static AccountSettingsForm NewForm(string name, IAccountConfigStore accounts, IScriptProfileStore profiles, IAdvancedSkillProfileStore library)
    {
        var log = new InMemoryRoadhogLogger();
        var form = new AccountSettingsForm(name, new RoadhogRuntime(new FakeGameApi(), log, new AccountRuntimeManager(log), null!),
            accounts, new InMemorySharedPathStore(), profiles, new RecordingFolderLauncher(), "test-paths", advancedSkillProfiles: library);
        Show(form); return form;
    }
    private static void Show(Form form) { form.ShowInTaskbar = false; form.StartPosition = FormStartPosition.Manual; form.Location = new(-20000, -20000); form.Show(); Application.DoEvents(); }
    private static Form Open(AccountSettingsForm form) { var dialog = (Form)Call(form, "CreateAdvancedSkillSettingsDialog")!; Show(dialog); return dialog; }
    private static T Find<T>(Control parent, string name) where T : Control => (T)parent.Controls.Find(name, true).Single();
    private static object? Call(object target, string name, params object?[] args) => target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(target, args);
    private static void Set(object target, string name, object value) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(target, value);
    private static void Save(Form dialog)
    {
        Find<Button>(dialog, "advancedSkillSave").PerformClick();
        if (dialog.DialogResult != DialogResult.OK) throw new InvalidOperationException("save failed: " + Find<Label>(dialog, "advancedSkillStatus").Text);
    }
    private static Task OnSta(Action action)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() => { try { action(); completion.SetResult(); } catch (Exception e) { completion.SetException(new InvalidOperationException(e.ToString(), e)); } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); return completion.Task;
    }
    private sealed class FailingAccounts(IAccountConfigStore inner) : IAccountConfigStore
    {
        public bool Fail = true;
        public Task<OperationResult<IReadOnlyList<AccountConfig>>> LoadAllAsync(CancellationToken cancellationToken = default) => inner.LoadAllAsync(cancellationToken);
        public Task<OperationResult> SaveAllAsync(IReadOnlyList<AccountConfig> accounts, CancellationToken cancellationToken = default) => inner.SaveAllAsync(accounts, cancellationToken);
        public Task<OperationResult> UpsertAsync(AccountConfig account, CancellationToken cancellationToken = default) => Fail ? Task.FromResult(OperationResult.Fail("mock failure")) : inner.UpsertAsync(account, cancellationToken);
    }
}
