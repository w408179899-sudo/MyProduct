using System.Text.Json;
using Roadhog.Application;
using Roadhog.Core.Accounts;
using Roadhog.Core.Diagnostics;
using Roadhog.Core.Paths;
using Roadhog.Core.Profiles;
using Roadhog.Infrastructure.Composition;
using Roadhog.Infrastructure.Config;
using Roadhog.Infrastructure.Paths;
using Roadhog.Infrastructure.Profiles;

internal static class LegacyImportPreservationTests
{
    public static async Task ConflictingRoutesAndProfilesRemainEffectiveAsync()
    {
        using var f = new Fixture();
        var account = f.Account();
        account.ProcessId = 987;
        account.HardwareVerificationSessionId = "previously-verified";
        await f.SaveAccount(account);
        var routeBytes = Route("人马1", 22);
        f.SourceFile("paths/人马1.json", routeBytes);
        f.TargetFile("paths/人马1.json", Route("人马1", 11));
        // The profiles are byte-identical, but their same-named routes refer to different coordinates.
        var profileBytes = Profile("方案", "人马1", 37);
        f.SourceFile("profiles/方案.json", profileBytes);
        f.TargetFile("profiles/方案.json", profileBytes);
        var sourceBefore = f.Snapshot(f.Source);
        var targetBefore = f.Snapshot(f.Target);
        var imported = (await f.Import()).Single();
        Require(imported.ProfileName != "方案", "route remapping must also isolate an otherwise identical profile");
        Require(imported.ScriptSettings!.ProfileName == imported.ProfileName, "embedded and top-level selected profile must agree");
        Require(imported.HardwareVerificationSessionId == "" && imported.ProcessId == 0, "import always requires fresh hardware verification");
        Require(imported.RevivePathName != "人马1" && imported.CombatPathName == imported.RevivePathName && imported.MaintenancePathName == imported.RevivePathName,
            "legacy account path references must follow the preserved route");
        var runtime = await f.Build(imported);
        Require(runtime.ScriptSettings!.Combat.StationaryCombatRadius == 37, "runtime must load the imported effective profile");
        foreach (var name in RouteNames(runtime.ScriptSettings.Paths)) Require(name == imported.RevivePathName, "all six runtime path references must follow the source");
        var path = await new JsonSharedPathStore(f.Options.PathLibraryDirectory).LoadAsync(imported.RevivePathName);
        Require(path.Success && path.Value!.Points[0].X == 22 && path.Value.BoundStationaryCombatRadius == 48, "preserved route keeps coordinates and metadata");
        var raw = File.ReadAllText(Path.Combine(f.Options.PathLibraryDirectory, imported.RevivePathName + ".json"));
        Require(raw.Contains("futureMetadata") && raw.Contains("source-extra"), "renaming must retain unknown JSON fields");
        f.Unchanged(sourceBefore, f.Source);
        f.Unchanged(targetBefore, f.Target);
        Require(!File.Exists(f.Options.AccountConfigPath), "only UI saves account document");
    }

    public static async Task IdenticalRoutesReuseAndDifferentProfilesSeparateAsync()
    {
        using var f = new Fixture();
        await f.SaveAccount(f.Account());
        var same = Route("人马1", 22);
        f.SourceFile("paths/人马1.json", same);
        f.TargetFile("paths/人马1.json", same);
        f.SourceFile("profiles/方案.json", Profile("方案", "人马1", 37));
        f.TargetFile("profiles/方案.json", Profile("方案", "人马1", 99));
        var imported = (await f.Import()).Single();
        Require(imported.CombatPathName == "人马1" && Directory.GetFiles(f.Options.PathLibraryDirectory).Length == 1, "identical routes are reused");
        Require(imported.ProfileName != "方案" && (await f.Build(imported)).ScriptSettings!.Combat.StationaryCombatRadius == 37, "profile conflict preserves source settings");
        var existing = await new JsonScriptProfileStore(f.Options.ProfileLibraryDirectory).LoadAsync("方案");
        Require(existing.Value!.Settings.Combat.StationaryCombatRadius == 99, "existing profile remains unchanged");
    }

    public static async Task AliasNamesDoNotStealSourceOrExistingNamesAsync()
    {
        using var f = new Fixture();
        await f.SaveAccount(f.Account());
        f.SourceFile("paths/人马1.json", Route("人马1", 22));
        f.TargetFile("paths/人马1.json", Route("人马1", 11));
        f.SourceFile("paths/人马1（old-client）.json", Route("人马1（old-client）", 33));
        f.TargetFile("paths/人马1（old-client）_2.json", Route("人马1（old-client）_2", 44));
        var imported = (await f.Import()).Single();
        Require(imported.CombatPathName == "人马1（old-client）_3", "alias must reserve source names and existing numbered suffixes");
        var store = new JsonSharedPathStore(f.Options.PathLibraryDirectory);
        Require((await store.LoadAsync(imported.CombatPathName)).Value!.Points[0].X == 22, "conflict alias selects original route");
        Require((await store.LoadAsync("人马1（old-client）")).Value!.Points[0].X == 33, "other source document keeps its own name");
        Require((await store.LoadAsync("人马1（old-client）_2")).Value!.Points[0].X == 44, "existing alias never overwritten");
    }

    public static async Task MissingResourcesDoNotAcquireAnotherAccountsFilesAsync()
    {
        using var f = new Fixture();
        await f.SaveAccount(f.Account());
        f.TargetFile("paths/人马1.json", Route("人马1", 11));
        f.TargetFile("profiles/方案.json", Profile("方案", "人马1", 99));
        f.TargetFile("bag-cleanup-name-lists.json", "{\"version\":2,\"whitelist\":[\"other-account\"],\"blacklist\":[]}");
        f.TargetFile("radar-maps/47.json", "{\"MapId\":47}");
        var imported = (await f.Import()).Single();
        Require(imported.ProfileName != "方案" && imported.CombatPathName != "人马1", "missing source references remain separate");
        Require(!File.Exists(Path.Combine(f.Options.ProfileLibraryDirectory, imported.ProfileName + ".json")), "missing profile is not fabricated");
        Require(!File.Exists(Path.Combine(f.Options.PathLibraryDirectory, imported.CombatPathName + ".json")), "missing route is not fabricated");
        var runtime = await f.Build(imported);
        Require(runtime.ScriptSettings!.Combat.StationaryCombatRadius == 37, "missing source profile keeps embedded settings rather than another profile");
        Require(!runtime.ScriptSettings.Maintenance.BagCleanupExcludedItemNames.Contains("other-account"), "missing source lists never inherit unrelated target lists");
        Require(!File.Exists(Path.Combine(f.Resolve(imported.RadarMapDirectory), "47.json")), "missing source map stays missing");
    }

    public static async Task SourceListsMapsAndMachineGrantStayBoundAsync()
    {
        using var f = new Fixture();
        await f.SaveAccount(f.Account());
        f.SourceFile("bag-cleanup-name-lists.json", "{\"version\":2,\"whitelist\":[\"customer-item\"],\"blacklist\":[]}");
        f.SourceFile("bag-cleanup-excluded.txt", "old-customer-item\r\n");
        f.SourceFile("radar-maps/47.json", "{\"MapId\":47,\"futureSourceValue\":123}");
        // Only an opaque original path is retained; the regular signature/device validation still runs at authorization time.
        f.SourceFile("owner-license.json", "{\"testOnlyInvalidGrant\":true}");
        f.TargetFile("bag-cleanup-name-lists.json", "{\"version\":2,\"whitelist\":[\"existing-item\"],\"blacklist\":[]}");
        f.TargetFile("radar-maps/47.json", "{\"MapId\":47,\"existing\":true}");
        var sourceBefore = f.Snapshot(f.Source);
        var targetBefore = f.Snapshot(f.Target);
        var imported = (await f.Import()).Single();
        Require(imported.OwnerLicenseGrantPath == Path.Combine(f.Source, "owner-license.json"), "default legacy machine grant must not be lost");
        Require(imported.LicenseCredentialPath == Path.Combine(f.Source, "license.dat"), "CDKey credential identity remains at original source");
        foreach (var name in new[] { "bag-cleanup-name-lists.json", "bag-cleanup-excluded.txt" })
            Require(File.ReadAllBytes(Path.Combine(Path.GetDirectoryName(f.Resolve(imported.BagCleanupNameListPath))!, name))
                .SequenceEqual(File.ReadAllBytes(Path.Combine(f.Source, name))), "original list bytes retained");
        Require(File.ReadAllBytes(Path.Combine(f.Resolve(imported.RadarMapDirectory), "47.json"))
            .SequenceEqual(File.ReadAllBytes(Path.Combine(f.Source, "radar-maps", "47.json"))), "original map bytes retained");
        Require((await f.Build(imported)).ScriptSettings!.Maintenance.BagCleanupExcludedItemNames.Contains("customer-item"), "runtime selects source list");
        f.Unchanged(sourceBefore, f.Source);
        f.Unchanged(targetBefore, f.Target);
        // Strict import remains available for callers that require all shared files to match.
        await Reject(() => new LegacyAccountImporter(f.Options).ImportAsync(f.AccountPath, []), "strict mode still rejects conflicting shared files");
    }

    public static async Task MalformedSourceAndWriteFailureLeaveExistingFilesAsync()
    {
        using var f = new Fixture();
        await f.SaveAccount(f.Account());
        f.SourceFile("paths/人马1.json", Route("人马1", 22));
        f.SourceFile("profiles/方案.json", "{broken");
        f.TargetFile("keep.json", "untouched");
        var before = f.Snapshot(f.Target);
        await Reject(f.Import, "malformed source must fail during planning");
        Require(f.Snapshot(f.Target).Count == before.Count, "failed preflight writes nothing");
        f.SourceFile("profiles/方案.json", Profile("方案", "人马1", 37));
        // Fail after shared copies are created: private import directory is blocked by an existing regular file.
        f.TargetFile("imported", "directory-blocker");
        await Reject(f.Import, "late write failure must fail the import");
        Require(!File.Exists(Path.Combine(f.Options.PathLibraryDirectory, "人马1.json")) &&
            !File.Exists(Path.Combine(f.Options.ProfileLibraryDirectory, "方案.json")), "late failure rolls back new library bytes");
        f.Unchanged(before, f.Target);
        Require(!File.Exists(f.Options.AccountConfigPath), "failed import cannot save accounts");
    }

    public static async Task ExternalResourcesAndDeviceRejectionRemainExplicitAsync()
    {
        using var f = new Fixture();
        var account = f.Account();
        account.OwnerLicenseGrantPath = "../selected-owner.json";
        account.BagCleanupNameListPath = "../selected-lists.json";
        account.RadarMapDirectory = "../selected-maps";
        await f.SaveAccount(account);
        f.SourceFile("owner-license.json", "must-not-replace-selected-grant");
        var imported = (await f.Import()).Single();
        Require(imported.OwnerLicenseGrantPath == Path.GetFullPath(account.OwnerLicenseGrantPath, f.Source), "explicit owner grant selection wins");
        Require(imported.BagCleanupNameListPath == Path.GetFullPath(account.BagCleanupNameListPath, f.Source), "explicit lists selection wins");
        Require(imported.RadarMapDirectory == Path.GetFullPath(account.RadarMapDirectory, f.Source), "explicit map selection wins");
        var before = f.Snapshot(f.Target);
        await Reject(() => new LegacyAccountImporter(f.Options).ImportPreservingConflictsAsync(f.AccountPath, [account]), "duplicate hardware is still rejected");
        Require(f.Snapshot(f.Target).Count == before.Count, "binding rejection writes no additional files");
        f.Unchanged(before, f.Target);
    }

    public static async Task SequentialSameNamedCustomerAccountsImportAsync()
    {
        using var f = new Fixture();
        var source = f.Account();
        await f.SaveAccount(source);
        f.SourceFile("paths/人马1.json", Route("人马1", 11));
        f.SourceFile("profiles/方案.json", Profile("方案", "人马1", 37));
        var first = (await f.Import()).Single();
        var store = new JsonAccountConfigStore(f.Options.AccountConfigPath);
        Require((await store.SaveAllAsync([first])).Success, "first account saves");
        var firstJson = JsonSerializer.Serialize(first);
        var firstFiles = f.Snapshot(f.Target);

        var secondSource = Path.Combine(f.Root, "77", "config");
        Directory.CreateDirectory(secondSource);
        foreach (var file in Directory.GetFiles(f.Source, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(secondSource, Path.GetRelativePath(f.Source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
        source.HardwareKey = "port:customer2";
        source.VmmDeviceName = "fpga://devindex=3";
        source.KmBox = new() { IpAddress = "127.0.0.3", Port = 5000, Mac = "87654321" };
        var secondAccountPath = Path.Combine(secondSource, "accounts.json");
        Require((await new JsonAccountConfigStore(secondAccountPath).SaveAllAsync([source])).Success, "second source saves");
        File.WriteAllText(Path.Combine(secondSource, "paths", "人马1.json"), Route("人马1", 22));
        var second = (await new LegacyAccountImporter(f.Options).ImportPreservingConflictsAsync(secondAccountPath, [first])).Single();
        Require(second.AccountName == "account1 (77)" && second.InstanceId != first.InstanceId, "same account name gets its own identity");
        Require(JsonSerializer.Serialize(first) == firstJson, "first account configuration is untouched");
        f.Unchanged(firstFiles, f.Target);
        Require((await store.SaveAllAsync([first, second])).Success && (await store.LoadAllAsync()).Value!.Count == 2, "two customer accounts save together");
        var paths = new JsonSharedPathStore(f.Options.PathLibraryDirectory);
        var firstRuntime = await f.Build(first);
        var secondRuntime = await f.Build(second);
        Require((await paths.LoadAsync(firstRuntime.ScriptSettings!.Paths.CombatPathName)).Value!.Points[0].X == 11, "first account still uses first route");
        Require((await paths.LoadAsync(secondRuntime.ScriptSettings!.Paths.CombatPathName)).Value!.Points[0].X == 22, "second account uses its own route");
    }

    private static string Route(string name, int x) => JsonSerializer.Serialize(new
    {
        Version = 1, Name = name, MapId = 47, BoundStationaryCombatRadius = 48,
        futureMetadata = "source-extra", Points = new[] { new { Index = 0, X = x, Y = 2, Z = 3 } }
    });
    private static string Profile(string name, string route, int radius) => JsonSerializer.Serialize(new
    {
        Version = 1, Name = name, futureMetadata = "profile-extra", Settings = Settings(name, route, radius)
    });
    private static ScriptSettings Settings(string name, string route, int radius) => new()
    {
        ProfileName = name, Combat = new() { StationaryCombatRadius = radius },
        Paths = new() { RevivePathName = route, CombatPathName = route, MaintenancePathName = route, GatherPathName = route, AuctionPathName = route, StallPathName = route }
    };
    private static IEnumerable<string> RouteNames(PathScriptSettings p) => [p.RevivePathName, p.CombatPathName, p.MaintenancePathName, p.GatherPathName, p.AuctionPathName, p.StallPathName];
    private static async Task Reject(Func<Task<IReadOnlyList<AccountConfig>>> action, string message)
    {
        try { await action(); }
        catch (Exception e) when (e is InvalidDataException or IOException) { return; }
        throw new InvalidOperationException(message);
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "roadhog-preserve-import-" + Guid.NewGuid().ToString("N"));
        public string Source => Path.Combine(Root, "old-client", "config");
        public string Target => Path.Combine(Root, "console", "config");
        public string AccountPath => Path.Combine(Source, "accounts.json");
        public RoadhogServiceOptions Options => new()
        {
            AccountConfigPath = Path.Combine(Target, "accounts.json"), PathLibraryDirectory = Path.Combine(Target, "paths"),
            ProfileLibraryDirectory = Path.Combine(Target, "profiles"), RadarMapDirectory = Path.Combine(Target, "radar-maps"),
            LicenseCredentialPath = Path.Combine(Target, "license.dat"), OwnerLicenseGrantPath = Path.Combine(Target, "owner-license.json")
        };
        public AccountConfig Account() => new()
        {
            InstanceId = Guid.NewGuid().ToString("N"), AccountName = "account1", HardwareKey = "port:customer", VmmDeviceName = "fpga://devindex=2",
            KmBox = new() { IpAddress = "127.0.0.2", Port = 5000, Mac = "12345678" }, ProfileName = "方案",
            RevivePathName = "人马1", CombatPathName = "人马1", MaintenancePathName = "人马1", ScriptSettings = Settings("方案", "人马1", 37)
        };
        public async Task SaveAccount(AccountConfig account) => Require((await new JsonAccountConfigStore(AccountPath).SaveAllAsync([account])).Success, "fixture save failed");
        public Task<IReadOnlyList<AccountConfig>> Import() => new LegacyAccountImporter(Options).ImportPreservingConflictsAsync(AccountPath, []);
        public string Resolve(string relative) => Path.GetFullPath(relative, Target);
        public async Task<AccountConfig> Build(AccountConfig account)
        {
            var builder = new AccountStartConfigBuilder(new JsonScriptProfileStore(Options.ProfileLibraryDirectory),
                new JsonBagCleanupNameListStore(Path.Combine(Target, "bag-cleanup-name-lists.json")), NoOpRoadhogLogger.Instance,
                path => new JsonBagCleanupNameListStore(Resolve(path)));
            var built = await builder.BuildAsync(account);
            Require(built.Success && built.Value is not null, built.Error ?? "runtime build failed");
            return built.Value!;
        }
        public void SourceFile(string relative, string text) => Write(Path.Combine(Source, relative), text);
        public void TargetFile(string relative, string text) => Write(Path.Combine(Target, relative), text);
        private static void Write(string path, string text) { Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, text); }
        public Dictionary<string, byte[]> Snapshot(string root) => Directory.Exists(root)
            ? Directory.GetFiles(root, "*", SearchOption.AllDirectories).ToDictionary(p => Path.GetRelativePath(root, p), File.ReadAllBytes)
            : [];
        public void Unchanged(Dictionary<string, byte[]> before, string root)
        { foreach (var pair in before) Require(pair.Value.SequenceEqual(File.ReadAllBytes(Path.Combine(root, pair.Key))), "source/existing file changed: " + pair.Key); }
        public void Dispose()
        {
            Require(Path.GetFullPath(Root).StartsWith(Path.Combine(Path.GetFullPath(Path.GetTempPath()), "roadhog-preserve-import-"), StringComparison.OrdinalIgnoreCase), "test cleanup root invalid");
            if (Directory.Exists(Root)) Directory.Delete(Root, true);
        }
    }
}
