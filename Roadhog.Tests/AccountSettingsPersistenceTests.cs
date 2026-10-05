using System.Reflection;
using System.Text.Json;
using Roadhog;
using Roadhog.Application;
using Roadhog.Application.Workers;
using Roadhog.Core.Accounts;
using Roadhog.Core.Common;
using Roadhog.Core.Diagnostics;
using Roadhog.Core.Model;
using Roadhog.Core.Profiles;
using Roadhog.Infrastructure.Config;
using Roadhog.Infrastructure.Profiles;

internal static class AccountSettingsPersistenceTests
{
    private const string ProfileName = "pair-profile";
    private static ScriptSettings Settings(uint id, string name = ProfileName) => new()
    {
        ProfileName = name,
        SkillTreeReleaseMode = SkillTreeReleaseMode.QuickbarAvailability,
        QuickbarSkills = new() { ExecutionTree = new() { new() { SkillId = id, Name = "Pair Strike " + id, BaseName = "Pair Strike" } } },
        Skills = new() { ExecutionTree = new() { new() { SkillId = 999, Name = "Archived Strike" } } },
        SemiAuto = new() { AttackWeaveEnabled = true, AttackWeaveDelayMs = 613 }
    };
    private static ScriptProfileDocument Profile(uint id, string name = ProfileName) => new()
        { Name = name, Settings = Settings(id, name) };
    private static AccountConfig Account(uint id, string name = ProfileName) => new()
        { AccountName = "pair-account", ProfileName = name, ScriptSettings = Settings(id, name) };
    private static string Json(object? value) => JsonSerializer.Serialize(value);
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static async Task<ScriptProfileDocument> Read(IScriptProfileStore store, string name = ProfileName)
    {
        var result = await store.LoadAsync(name);
        Check(result.Success && result.Value is not null, "profile remains readable: " + result.Error);
        return result.Value!;
    }

    public static async Task SnapshotAndProfileFailureAsync()
    {
        foreach (var stage in new[] { "load", "load-throw", "save", "save-throw" })
        {
            var original = Profile(101);
            var profiles = new ProfileProbe(new InMemoryScriptProfileStore(original)) { Failure = stage };
            var accounts = new AccountProbe(new InMemoryAccountConfigStore(Account(101)));
            var profile = Profile(202);
            var account = Account(202);
            var beforeProfile = Json(profile);
            var beforeAccount = Json(account);
            var result = await AccountSettingsPersistence.SaveAsync(profiles, accounts, profile, account);
            Check(!result.Success && !string.IsNullOrWhiteSpace(result.Error) && accounts.Upserts == 0,
                "read/profile failure cannot write an account (" + stage + ")");
            Check(profiles.Saves == (stage.StartsWith("save", StringComparison.Ordinal) ? 1 : 0) && profiles.Deletes == 0,
                "failed snapshot reads perform no write or rollback (" + stage + ")");
            Check(Json((await Read(profiles.Inner)).Settings) == Json(original.Settings), "failed operation preserves the existing profile");
            Check(Json(profile) == beforeProfile && Json(account) == beforeAccount, "service does not mutate caller inputs");
        }
    }

    public static async Task ExistingProfileRollbackAsync()
    {
        foreach (var failure in new[] { "returned", "throw", "cancel" })
        {
            using var cancellation = new CancellationTokenSource();
            var original = Profile(101);
            var profiles = new ProfileProbe(new InMemoryScriptProfileStore(original));
            var accounts = new AccountProbe(new InMemoryAccountConfigStore(Account(101)))
            {
                Fail = failure == "returned", Throw = failure == "throw",
                Cancel = failure == "cancel" ? cancellation : null
            };
            var result = await AccountSettingsPersistence.SaveAsync(profiles, accounts, Profile(202), Account(202), cancellation.Token);
            Check(!result.Success && result.Error!.Contains("方案已恢复") && profiles.Saves == 2 && profiles.Deletes == 0,
                "account failure restores the existing profile (" + failure + ")");
            Check(profiles.SaveTokens.Last() == CancellationToken.None &&
                Json((await Read(profiles.Inner)).Settings) == Json(original.Settings),
                "rollback retains all prior settings even after cancellation");
            Check((await accounts.Inner.LoadAllAsync()).Value!.Single().ScriptSettings!.QuickbarSkills.ExecutionTree.Single().SkillId == 101,
                "failed account write leaves the previous persisted tree");
        }
    }

    public static async Task UnlistedReadFailureAndMalformedAliasAsync()
    {
        foreach (var failure in new[] { "load", "load-throw" })
        {
            var profiles = new ProfileProbe(new InMemoryScriptProfileStore()) { Failure = failure };
            var accounts = new AccountProbe(new InMemoryAccountConfigStore(Account(101))) { Fail = true };
            var result = await AccountSettingsPersistence.SaveAsync(profiles, accounts, Profile(202), Account(202));
            Check(!result.Success && profiles.Saves == 0 && profiles.Deletes == 0 && accounts.Upserts == 0,
                "an unlisted snapshot read failure is never treated as a new profile");
        }
        var directory = Path.Combine(Path.GetTempPath(), "RoadhogProfileLookup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var real = new JsonScriptProfileStore(directory);
            var absent = await real.LoadOptionalAsync("absent-profile");
            var oldLoad = await real.LoadAsync("absent-profile");
            Check(absent.Success && absent.Value is null && !oldLoad.Success,
                "typed lookup distinguishes true absence while the old required-load API still fails");
            foreach (var content in new[] { "null", "", "{invalid" })
            {
                var file = Path.Combine(directory, "malformed_profile.json");
                await File.WriteAllTextAsync(file, content);
                var optional = await real.LoadOptionalAsync("malformed:profile");
                Check(!optional.Success, "a present empty/null/invalid alias document is a read failure, never typed absence");
                var profiles = new ProfileProbe(real);
                var accounts = new AccountProbe(new InMemoryAccountConfigStore(Account(101))) { Fail = true };
                var result = await AccountSettingsPersistence.SaveAsync(profiles, accounts,
                    Profile(202, "malformed:profile"), Account(202, "malformed:profile"));
                Check(!result.Success && profiles.Saves == 0 && profiles.Deletes == 0 && accounts.Upserts == 0 &&
                    await File.ReadAllTextAsync(file) == content,
                    "unlisted invalid alias files remain byte-for-byte intact without a write or deletion");
            }
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
    }

    public static async Task NewProfileAndRollbackFailureAsync()
    {
        foreach (var rollbackFails in new[] { false, true })
        {
            var profiles = new ProfileProbe(new InMemoryScriptProfileStore()) { FailDelete = rollbackFails };
            var accounts = new AccountProbe(new InMemoryAccountConfigStore(Account(101))) { Fail = true };
            var result = await AccountSettingsPersistence.SaveAsync(profiles, accounts, Profile(202, "new-profile"), Account(202, "new-profile"));
            var summaries = await profiles.Inner.LoadSummariesAsync();
            Check(!result.Success && profiles.Saves == 1 && profiles.Deletes == 1,
                "a failed account write attempts to undo a newly created profile");
            Check(rollbackFails
                ? result.Error!.Contains("部分保存") && result.Error.Contains("恢复失败") && summaries.Value!.Count == 1
                : result.Error!.Contains("方案已恢复") && summaries.Value!.Count == 0,
                "rollback failure explicitly reports the remaining partial save");
        }
        foreach (var rollbackThrows in new[] { false, true })
        {
            var profiles = new ProfileProbe(new InMemoryScriptProfileStore(Profile(101)))
                { FailRollback = !rollbackThrows, ThrowRollback = rollbackThrows };
            var accounts = new AccountProbe(new InMemoryAccountConfigStore(Account(101))) { Throw = true };
            var result = await AccountSettingsPersistence.SaveAsync(profiles, accounts, Profile(202), Account(202));
            Check(!result.Success && result.Error!.Contains("部分保存") && result.Error.Contains("恢复失败") &&
                (await Read(profiles.Inner)).Settings.QuickbarSkills.ExecutionTree.Single().SkillId == 202,
                "failed or throwing restoration cannot be reported as an ordinary failed save");
        }
    }

    public static async Task PairSerializationAndInputIsolationAsync()
    {
        var profiles = new ProfileProbe(new InMemoryScriptProfileStore(Profile(101)));
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var accounts = new AccountProbe(new InMemoryAccountConfigStore(Account(101)))
            { Gate = release.Task, FailuresRemaining = 1 };
        var first = AccountSettingsPersistence.SaveAsync(profiles, accounts, Profile(202), Account(202));
        await accounts.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var queuedProfile = Profile(303);
        var queuedAccount = Account(303);
        var second = AccountSettingsPersistence.SaveAsync(profiles, accounts, queuedProfile, queuedAccount);
        queuedProfile.Settings.QuickbarSkills.ExecutionTree[0].SkillId = 888;
        queuedAccount.ScriptSettings!.QuickbarSkills.ExecutionTree[0].SkillId = 888;
        Check(!second.IsCompleted && profiles.Saves == 1 && accounts.Upserts == 1,
            "another save of the same store pair waits through the first account write and compensation");
        release.SetResult();
        var results = await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(5));
        Check(!results[0].Success && results[1].Success && profiles.Saves == 3 && accounts.Upserts == 2,
            "the following save succeeds only after the failed save restores its snapshot");
        Check((await Read(profiles.Inner)).Settings.QuickbarSkills.ExecutionTree.Single().SkillId == 303 &&
            (await accounts.Inner.LoadAllAsync()).Value!.Single().ScriptSettings!.QuickbarSkills.ExecutionTree.Single().SkillId == 303,
            "a queued save owns independent copies and cannot be overwritten by an earlier rollback");
    }

    public static async Task RealStoresAndFilenameAliasAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), "RoadhogSettingsPair-" + Guid.NewGuid().ToString("N"));
        try
        {
            var profiles = new JsonScriptProfileStore(Path.Combine(directory, "profiles"));
            var accounts = new JsonAccountConfigStore(Path.Combine(directory, "accounts.json"));
            Check((await profiles.SaveAsync(Profile(101))).Success && (await accounts.UpsertAsync(Account(101))).Success,
                "real profile/account baseline saves");
            var result = await AccountSettingsPersistence.SaveAsync(profiles, accounts, Profile(202), Account(202));
            var saved = (await accounts.LoadAllAsync()).Value!.Single();
            var effectiveProfile = await Read(profiles, saved.ScriptSettings!.ProfileName);
            Check(result.Success && Json(saved.ScriptSettings) == Json(effectiveProfile.Settings) &&
                effectiveProfile.Settings.QuickbarSkills.ExecutionTree.Single().SkillId == 202,
                "successful real-store reload sees the same complete settings through account and effective profile");
            var previousAlias = Profile(401, "alias?profile");
            Check((await profiles.SaveAsync(previousAlias)).Success, "existing alias file saves");
            var failedAccounts = new AccountProbe(accounts) { Fail = true };
            result = await AccountSettingsPersistence.SaveAsync(profiles, failedAccounts,
                Profile(402, "alias:profile"), Account(402, "alias:profile"));
            var restored = await Read(profiles, "alias:profile");
            var summaries = (await profiles.LoadSummariesAsync()).Value!;
            Check(!result.Success && result.Error!.Contains("方案已恢复") && restored.Name == "alias?profile" &&
                Json(restored.Settings) == Json(previousAlias.Settings) &&
                summaries.Count(summary => summary.Name.StartsWith("alias", StringComparison.Ordinal)) == 1,
                "different names of the same safe filename restore the original document instead of deleting it");
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
    }

    public static async Task StoredNameMismatchLeavesFilesIntactAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), "RoadhogProfileIdentity-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var real = new JsonScriptProfileStore(directory);
            Check((await real.SaveAsync(Profile(301, "other-profile"))).Success, "other physical profile baseline saves");
            var requestedFile = Path.Combine(directory, ProfileName + ".json");
            var otherFile = Path.Combine(directory, "other-profile.json");
            var requestedBytes = Json(Profile(101, "other-profile"));
            await File.WriteAllTextAsync(requestedFile, requestedBytes);
            var otherBytes = await File.ReadAllTextAsync(otherFile);
            var required = await real.LoadAsync(ProfileName);
            var optional = await real.LoadOptionalAsync(ProfileName);
            Check(required.Success && required.Value!.Name == "other-profile" && !optional.Success,
                "required legacy loading keeps its original contract while save snapshots reject a different physical identity");
            var profiles = new ProfileProbe(real);
            var accounts = new AccountProbe(new InMemoryAccountConfigStore(Account(101))) { Fail = true };
            var result = await AccountSettingsPersistence.SaveAsync(profiles, accounts, Profile(202), Account(202));
            Check(!result.Success && profiles.Saves == 0 && profiles.Deletes == 0 && accounts.Upserts == 0 &&
                await File.ReadAllTextAsync(requestedFile) == requestedBytes &&
                await File.ReadAllTextAsync(otherFile) == otherBytes,
                "a mismatched document name cannot overwrite the requested file or compensate into another file");
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
    }

    public static async Task UnrelatedInvalidProfileDoesNotBlockSaveAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), "RoadhogIndependentProfile-" + Guid.NewGuid().ToString("N"));
        try
        {
            var real = new JsonScriptProfileStore(Path.Combine(directory, "profiles"));
            var accounts = new JsonAccountConfigStore(Path.Combine(directory, "accounts.json"));
            Check((await real.SaveAsync(Profile(101))).Success && (await accounts.UpsertAsync(Account(101))).Success,
                "valid real-store baseline saves");
            var corruptFile = Path.Combine(directory, "profiles", "unrelated.json");
            await File.WriteAllTextAsync(corruptFile, "{unrelated-invalid-json");
            Check(!(await real.LoadSummariesAsync()).Success, "the unrelated corrupt file reproduces a failed whole-library listing");
            foreach (var name in new[] { ProfileName, "new-pair-profile" })
            {
                var profiles = new ProfileProbe(real) { Failure = "summaries" };
                var result = await AccountSettingsPersistence.SaveAsync(profiles, accounts, Profile(202, name), Account(202, name));
                Check(result.Success && profiles.SummaryReads == 0 &&
                    (await Read(real, name)).Settings.QuickbarSkills.ExecutionTree.Single().SkillId == 202 &&
                    (await accounts.LoadAllAsync()).Value!.Single().ScriptSettings!.ProfileName == name &&
                    await File.ReadAllTextAsync(corruptFile) == "{unrelated-invalid-json",
                    "saving an existing or new valid profile does not depend on unrelated library files");
            }
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
    }

    public static Task RealStoreUiRefreshRollbackAsync() => OnSta(() =>
    {
        var directory = Path.Combine(Path.GetTempPath(), "RoadhogRefreshPair-" + Guid.NewGuid().ToString("N"));
        try
        {
            var profiles = new JsonScriptProfileStore(Path.Combine(directory, "profiles"));
            var realAccounts = new JsonAccountConfigStore(Path.Combine(directory, "accounts.json"));
            var original = Settings(202);
            original.QuickbarSkills.ExecutionTree[0].Name = "Pair Strike II";
            original.Maintenance.StatusMaintenanceRules.Add(new() { SkillId = 202, SkillName = "Pair Strike II", Key = "F7" });
            var baseline = new AccountConfig { AccountName = "pair-account", ProfileName = ProfileName, ScriptSettings = original };
            Check(profiles.SaveAsync(new() { Name = ProfileName, Settings = original }).GetAwaiter().GetResult().Success &&
                realAccounts.UpsertAsync(baseline).GetAwaiter().GetResult().Success, "UI real-store baseline saves");
            var accounts = new AccountProbe(realAccounts) { Fail = true };
            var logger = new InMemoryRoadhogLogger();
            var runtime = DispatchProxy.Create<IRoadhogRuntime, ConfiguredSkillQuickbarRefreshTests.PreviewRuntime>();
            var preview = (ConfiguredSkillQuickbarRefreshTests.PreviewRuntime)runtime;
            preview.Inner = new RoadhogRuntime(new FakeGameApi(), logger, new AccountRuntimeManager(logger), null!);
            preview.LearnedSkills = new[] { new SkillSnapshot(202, "Pair Strike II", 2, 1, "Pair Strike", 2, false, 0, 0) };
            preview.ExactSkills = new[] { new SkillSnapshot(101, "Pair Strike I", 1, 1, "Pair Strike", 1, false, 0, 0) };
            preview.Bar = new(0, new[] { new QuickbarSlotSnapshot(SkillQuickbar.Main, 0, 21, 101) });
            using var form = new AccountSettingsForm("pair-account", runtime, accounts, new InMemorySharedPathStore(),
                profiles, new RecordingFolderLauncher(), "test-paths");
            var drafted = original.Clone();
            drafted.Skills.OpeningSkill.Enabled = true;
            typeof(AccountSettingsForm).GetMethod("ApplyScriptSettings", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(form, new object[] { drafted });
            var refresh = (Task<(int UpdatedCount, int DeletedCount, bool Saved, string Error)>)typeof(AccountSettingsForm)
                .GetMethod("RefreshConfiguredSkillsCoreAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form, null)!;
            var refreshResult = refresh.GetAwaiter().GetResult();
            var persisted = realAccounts.LoadAllAsync().GetAwaiter().GetResult().Value!.Single().ScriptSettings!;
            var profileSettings = profiles.LoadAsync(ProfileName).GetAwaiter().GetResult().Value!.Settings;
            var capture = (ScriptSettings)typeof(AccountSettingsForm).GetMethod("CaptureScriptSettings", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(form, null)!;
            Check(!refreshResult.Saved && refreshResult.Error.Contains("方案已恢复") &&
                Json(persisted) == Json(original) && Json(profileSettings) == Json(original),
                "failed real UI refresh restores the profile and leaves every persisted account field unchanged");
            Check(capture.QuickbarSkills.ExecutionTree.Single().SkillId == 101 && capture.Skills.OpeningSkill.Enabled,
                "failed persistence retains the refreshed skill draft and unrelated user edits for retry");
            using var reloaded = new AccountSettingsForm("pair-account", runtime, accounts, new InMemorySharedPathStore(),
                profiles, new RecordingFolderLauncher(), "test-paths");
            var effective = (ScriptSettings)typeof(AccountSettingsForm).GetMethod("CaptureScriptSettings", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(reloaded, null)!;
            Check(effective.QuickbarSkills.ExecutionTree.Single().SkillId == 202 &&
                effective.Maintenance.StatusMaintenanceRules.Single().SkillId == 202 && !effective.Skills.OpeningSkill.Enabled,
                "effective profile reload cannot resurrect a refresh that reported a failed save");
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
    });

    private static Task OnSta(Action action)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try { action(); completion.SetResult(); }
            catch (Exception exception) { completion.SetException(new InvalidOperationException(exception.ToString(), exception)); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }

    private sealed class ProfileProbe(IScriptProfileStore inner) : IScriptProfileStore
    {
        public IScriptProfileStore Inner { get; } = inner;
        public string? Failure { get; init; }
        public bool FailDelete { get; init; }
        public bool FailRollback { get; init; }
        public bool ThrowRollback { get; init; }
        public int Saves { get; private set; }
        public int Deletes { get; private set; }
        public int SummaryReads { get; private set; }
        public List<CancellationToken> SaveTokens { get; } = new();
        public Task<OperationResult<IReadOnlyList<ScriptProfileSummary>>> LoadSummariesAsync(CancellationToken token = default)
        {
            SummaryReads++;
            return Failure == "summaries" ? Task.FromResult(OperationResult<IReadOnlyList<ScriptProfileSummary>>.Fail("mock summaries")) : Inner.LoadSummariesAsync(token);
        }
        public Task<OperationResult<ScriptProfileDocument>> LoadAsync(string name, CancellationToken token = default)
        {
            if (Failure == "load-throw") throw new InvalidOperationException("mock load throw");
            return Failure == "load" ? Task.FromResult(OperationResult<ScriptProfileDocument>.Fail("mock snapshot read")) : Inner.LoadAsync(name, token);
        }
        public Task<OperationResult<ScriptProfileDocument?>> LoadOptionalAsync(string name, CancellationToken token = default)
        {
            if (Failure == "load-throw") throw new InvalidOperationException("mock load throw");
            return Failure == "load" ? Task.FromResult(OperationResult<ScriptProfileDocument?>.Fail("mock snapshot read")) : Inner.LoadOptionalAsync(name, token);
        }
        public Task<OperationResult> SaveAsync(ScriptProfileDocument profile, CancellationToken token = default)
        {
            Saves++;
            SaveTokens.Add(token);
            if (Failure == "save-throw" || Saves == 2 && ThrowRollback) throw new InvalidOperationException("mock profile write throw");
            return Failure == "save" || Saves == 2 && FailRollback
                ? Task.FromResult(OperationResult.Fail("mock profile write")) : Inner.SaveAsync(profile, token);
        }
        public Task<OperationResult> DeleteAsync(string name, CancellationToken token = default)
        {
            Deletes++;
            return FailDelete ? Task.FromResult(OperationResult.Fail("mock profile delete")) : Inner.DeleteAsync(name, token);
        }
    }

    private sealed class AccountProbe(IAccountConfigStore inner) : IAccountConfigStore
    {
        public IAccountConfigStore Inner { get; } = inner;
        public bool Fail { get; init; }
        public bool Throw { get; init; }
        public CancellationTokenSource? Cancel { get; init; }
        public int FailuresRemaining { get; set; }
        public int Upserts { get; private set; }
        public Task? Gate { get; set; }
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<OperationResult<IReadOnlyList<AccountConfig>>> LoadAllAsync(CancellationToken token = default) => Inner.LoadAllAsync(token);
        public Task<OperationResult> SaveAllAsync(IReadOnlyList<AccountConfig> accounts, CancellationToken token = default) => Inner.SaveAllAsync(accounts, token);
        public async Task<OperationResult> UpsertAsync(AccountConfig account, CancellationToken token = default)
        {
            Upserts++;
            var fail = Fail || FailuresRemaining > 0;
            if (FailuresRemaining > 0) FailuresRemaining--;
            var gate = Gate;
            Gate = null;
            Entered.TrySetResult();
            if (gate is not null) await gate.ConfigureAwait(false);
            if (Cancel is not null) { Cancel.Cancel(); throw new OperationCanceledException(token); }
            if (Throw) throw new InvalidOperationException("mock account exception");
            return fail ? OperationResult.Fail("mock account failure") : await Inner.UpsertAsync(account, token).ConfigureAwait(false);
        }
    }
}
