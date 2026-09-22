using System.Text.Json;
using System.Text.Json.Serialization;
using Roadhog.Application;
using Roadhog.Core.Accounts;
using Roadhog.Core.Diagnostics;
using Roadhog.Core.Paths;
using Roadhog.Core.Profiles;
using Roadhog.Core.Radar;
using Roadhog.Infrastructure.Composition;
using Roadhog.Infrastructure.Config;
using Roadhog.Infrastructure.Paths;
using Roadhog.Infrastructure.Profiles;
using Roadhog.Infrastructure.Radar;
using Roadhog.Infrastructure.WorkerProcesses;

internal static class MigrationSnapshotTests
{
    public static async Task AuditCopyAsync(string root)
    {
        root = Path.GetFullPath(root);
        var configRoot = Path.Combine(root, "config");
        var json = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true, AllowTrailingCommas = true, ReadCommentHandling = JsonCommentHandling.Skip,
            Converters = { new JsonStringEnumConverter() }
        };
        using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(root, "manifest.json")));
        var migrated = await new JsonAccountConfigStore(Path.Combine(configRoot, "accounts.json")).LoadAllAsync();
        Require(migrated.Success && migrated.Value!.Count == 6, "migrated account store has six valid independent accounts");
        Require(!Directory.Exists(Path.Combine(configRoot, "workers")), "migration package contains no worker manifests, launch requests or persisted run intent");
        var profiles = new JsonScriptProfileStore(Path.Combine(configRoot, "profiles"));
        var paths = new JsonSharedPathStore(Path.Combine(configRoot, "paths"));
        var pathProperties = typeof(PathScriptSettings).GetProperties().Where(property => property.Name.EndsWith("PathName", StringComparison.Ordinal)).ToArray();
        var checkedPaths = 0;
        var missingPaths = new List<string>();
        foreach (var entry in manifest.RootElement.GetProperty("accounts").EnumerateArray())
        {
            var number = entry.GetProperty("script").GetInt32();
            var backupConfig = Path.Combine(root, "source-backup", number.ToString(), "config");
            var sourceAccounts = JsonSerializer.Deserialize<AccountConfigDocument>(await File.ReadAllTextAsync(Path.Combine(backupConfig, "accounts.json")), json)!;
            var original = sourceAccounts.Accounts.Single(account => account.AccountName == entry.GetProperty("sourceAccountName").GetString());
            var account = migrated.Value!.Single(account => account.AccountName == entry.GetProperty("accountName").GetString());
            using var beforeFile = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(configRoot, "preserved", number.ToString(), "effective-before.json")));
            var recordedSource = beforeFile.RootElement.GetProperty("SourceAccount").Deserialize<AccountConfig>(json)!;
            Require(JsonSerializer.Serialize(recordedSource) == JsonSerializer.Serialize(original), "effective-before source agrees with immutable account backup for script " + number);
            var originalBuilder = new AccountStartConfigBuilder(new JsonScriptProfileStore(Path.Combine(backupConfig, "profiles")),
                new JsonBagCleanupNameListStore(Path.Combine(backupConfig, JsonBagCleanupNameListStore.DefaultFileName)), NoOpRoadhogLogger.Instance,
                relative => new JsonBagCleanupNameListStore(Path.GetFullPath(relative, backupConfig)));
            var before = await originalBuilder.BuildAsync(original);
            Require(before.Success && before.Value?.ScriptSettings is not null, "source snapshot can build effective settings for script " + number);
            var builder = new AccountStartConfigBuilder(profiles,
                new JsonBagCleanupNameListStore(Path.Combine(configRoot, JsonBagCleanupNameListStore.DefaultFileName)), NoOpRoadhogLogger.Instance,
                relative => new JsonBagCleanupNameListStore(Path.GetFullPath(relative, configRoot)));
            var after = await builder.BuildAsync(account);
            Require(after.Success && after.Value?.ScriptSettings is not null, "migration snapshot can build effective settings for script " + number);
            Require(after.Value!.ProfileName == entry.GetProperty("preservedProfile").GetString(), "preserved profile is selected for script " + number);
            var beforeSettings = before.Value!.ScriptSettings!;
            var afterSettings = after.Value.ScriptSettings!.Clone();
            afterSettings.ProfileName = beforeSettings.ProfileName;
            var aliases = entry.GetProperty("pathAliases").EnumerateObject().ToDictionary(value => value.Name, value => value.Value.GetString()!);
            var reverseAliases = aliases.ToDictionary(value => value.Value, value => value.Key);
            foreach (var property in pathProperties)
            {
                var selected = (string?)property.GetValue(afterSettings.Paths) ?? string.Empty;
                if (reverseAliases.TryGetValue(selected, out var originalName)) property.SetValue(afterSettings.Paths, originalName);
            }
            Require(JsonSerializer.Serialize(beforeSettings) == JsonSerializer.Serialize(afterSettings),
                "normalized effective settings, lists and all path references match source script " + number);
            Require(after.Value.RevivePathName == after.Value.ScriptSettings.Paths.RevivePathName
                && after.Value.CombatPathName == after.Value.ScriptSettings.Paths.CombatPathName
                && after.Value.MaintenancePathName == after.Value.ScriptSettings.Paths.MaintenancePathName,
                "legacy path mirrors agree with preserved references for script " + number);
            var oldPaths = new JsonSharedPathStore(Path.Combine(backupConfig, "paths"));
            var declaredMissingPaths = entry.GetProperty("preExistingMissingPaths").EnumerateArray().Select(value => value.GetString()!).ToHashSet();
            foreach (var alias in aliases)
            {
                var previous = await oldPaths.LoadAsync(alias.Key);
                var current = await paths.LoadAsync(alias.Value);
                if (declaredMissingPaths.Contains(alias.Key))
                {
                    Require(!previous.Success && !current.Success, "missing source path remains missing under its isolated alias: " + alias.Key);
                    continue;
                }
                Require(previous.Success && current.Success, "preserved path remains readable: script " + number + " / " + alias.Key);
                var normalized = current.Value!.Clone();
                normalized.Name = previous.Value!.Name;
                Require(JsonSerializer.Serialize(normalized) == JsonSerializer.Serialize(previous.Value),
                    "path geometry, metadata and timestamps match source: script " + number + " / " + alias.Key);
                checkedPaths++;
            }
            foreach (var missing in entry.GetProperty("preExistingMissingPaths").EnumerateArray())
            {
                var name = missing.GetString()!;
                var selected = aliases.GetValueOrDefault(name, name);
                Require(!(await oldPaths.LoadAsync(name)).Success && !(await paths.LoadAsync(selected)).Success,
                    "previously missing path cannot silently bind another account's unrelated path: " + name);
                missingPaths.Add(number + ":" + name);
            }
            Console.WriteLine("PASS migration snapshot audit effective settings and referenced paths for script " + number);
        }
        using (var probe = new Files())
        {
            await using var manager = new WorkerProcessManager(probe.Options, NoOpRoadhogLogger.Instance, new()
            {
                ExecutablePath = Path.Combine(probe.Root, "never-start-a-migration.exe"),
                PollInterval = TimeSpan.FromMilliseconds(25), RecoveryDelay = TimeSpan.FromMilliseconds(25)
            });
            await manager.InitializeAsync(migrated.Value!);
            await Task.Delay(250);
            Require(manager.Snapshot().Count == 6 && manager.Snapshot().All(account => !account.DesiredRunning && account.WorkerProcessId is null
                && account.State == "verification_required" && account.Error == Roadhog.Infrastructure.Hardware.HardwareVerificationSession.RequiredMessage),
                "opening the migrated account document without run intent does not start any account or contact authorization");
            Require(!File.Exists(Path.Combine(probe.Root, "config", "workers", "run-intent.json")), "opening migrated accounts does not create start intent");
        }
        Console.WriteLine(JsonSerializer.Serialize(new { Accounts = 6, PreservedPaths = checkedPaths, PreExistingMissingPaths = missingPaths, EffectiveSettingsEqual = true, StartsNoWorkers = true }));
    }

    public static async Task CloneAndJsonPreserveOverridesAsync()
    {
        using var files = new Files();
        var account = files.Account(1);
        account.BagCleanupNameListPath = "preserved/1/bag-cleanup-name-lists.json";
        account.RadarMapDirectory = "preserved/1/radar-maps";
        account.OwnerLicenseGrantPath = "preserved/1/owner-license.json";
        account.LicenseCredentialPath = "preserved/1/license.dat";
        var before = JsonSerializer.Serialize(account);
        Require(JsonSerializer.Serialize(account.Clone()) == before, "cloning preserves every account resource reference");
        var store = new JsonAccountConfigStore(files.Options.AccountConfigPath);
        Require((await store.SaveAllAsync(new[] { account })).Success, "save migrated account");
        var loaded = await store.LoadAllAsync();
        Require(loaded.Success && JsonSerializer.Serialize(loaded.Value!.Single()) == before,
            "account JSON round trip preserves all resource references and settings");
        loaded.Value!.Single().BagCleanupNameListPath = "changed.json";
        Require(JsonSerializer.Serialize(account) == before, "loaded account cannot mutate source snapshot");
        var legacy = JsonSerializer.Deserialize<AccountConfig>("{\"AccountName\":\"legacy\"}")!;
        Require(legacy.BagCleanupNameListPath.Length == 0 && legacy.RadarMapDirectory.Length == 0 && legacy.OwnerLicenseGrantPath.Length == 0,
            "legacy accounts retain empty overrides for shared resources");
    }

    public static async Task AccountResourcesResolveWithoutChangingSharedLibrariesAsync()
    {
        using var files = new Files();
        await using var manager = new WorkerProcessManager(files.Options, NoOpRoadhogLogger.Instance);
        var first = files.Account(1);
        first.BagCleanupNameListPath = "preserved/1/lists.json";
        first.RadarMapDirectory = "preserved/1/radar-maps";
        first.OwnerLicenseGrantPath = "preserved/1/owner-license.json";
        first.LicenseCredentialPath = "preserved/1/license.dat";
        var second = files.Account(2);
        second.BagCleanupNameListPath = " \t";
        second.RadarMapDirectory = " ";
        second.OwnerLicenseGrantPath = "\t";
        var firstBefore = JsonSerializer.Serialize(first);
        var selected = manager.PathsFor(first);
        var shared = manager.PathsFor(second);
        var configRoot = Path.GetDirectoryName(files.Options.AccountConfigPath)!;
        Require(selected.BagCleanupNameListPath == Path.GetFullPath(first.BagCleanupNameListPath, configRoot)
            && selected.RadarMapDirectory == Path.GetFullPath(first.RadarMapDirectory, configRoot)
            && selected.OwnerLicenseGrantPath == Path.GetFullPath(first.OwnerLicenseGrantPath, configRoot)
            && selected.LicenseCredentialPath == Path.GetFullPath(first.LicenseCredentialPath, configRoot),
            "relative account resources resolve against the shared account configuration directory");
        Require(shared.BagCleanupNameListPath == files.Options.BagCleanupNameListPath
            && shared.RadarMapDirectory == files.Options.RadarMapDirectory
            && shared.OwnerLicenseGrantPath == files.Options.OwnerLicenseGrantPath,
            "empty overrides retain configured shared resource locations");
        Require(shared.LicenseCredentialPath == Path.Combine(configRoot, "workers", second.InstanceId, "license.dat"),
            "empty credential still belongs to the account instance");
        Require(selected.PathLibraryDirectory == shared.PathLibraryDirectory && selected.ProfileLibraryDirectory == shared.ProfileLibraryDirectory,
            "per-account resources do not split the shared profile and path libraries");
        var launch = JsonSerializer.Deserialize<WorkerLaunchSpec>(JsonSerializer.Serialize(new WorkerLaunchSpec { Account = first, Paths = selected }))!;
        var reconstructed = RoadhogWorkerProcessBackend.CreateOptions(launch);
        Require(reconstructed.BagCleanupNameListPath == selected.BagCleanupNameListPath
            && reconstructed.RadarMapDirectory == selected.RadarMapDirectory
            && reconstructed.OwnerLicenseGrantPath == selected.OwnerLicenseGrantPath
            && reconstructed.LicenseCredentialPath == selected.LicenseCredentialPath,
            "launch serialization and production option reconstruction preserve selected account resources");
        Require(JsonSerializer.Serialize(first) == firstBefore, "resource selection never rewrites the saved relative paths");

        first.BagCleanupNameListPath = Path.Combine(files.Root, "absolute", "lists.json");
        first.RadarMapDirectory = Path.Combine(files.Root, "absolute", "radar");
        first.OwnerLicenseGrantPath = Path.Combine(files.Root, "absolute", "owner.json");
        first.LicenseCredentialPath = Path.Combine(files.Root, "absolute", "license.dat");
        selected = manager.PathsFor(first);
        Require(selected.BagCleanupNameListPath == first.BagCleanupNameListPath && selected.RadarMapDirectory == first.RadarMapDirectory
            && selected.OwnerLicenseGrantPath == first.OwnerLicenseGrantPath && selected.LicenseCredentialPath == first.LicenseCredentialPath,
            "explicit absolute resources remain absolute and unchanged");
    }

    public static async Task SixAccountSnapshotsKeepEffectiveProfilesAndPathsAsync()
    {
        using var files = new Files();
        await using var manager = new WorkerProcessManager(files.Options, NoOpRoadhogLogger.Instance);
        var profiles = new JsonScriptProfileStore(files.Options.ProfileLibraryDirectory);
        var paths = new JsonSharedPathStore(files.Options.PathLibraryDirectory);
        Require((await profiles.SaveAsync(new ScriptProfileDocument { Name = "同名方案", Settings = new() { FixedChannelNumber = 9 } })).Success,
            "create a newer conflicting global profile");
        var sharedLists = new JsonBagCleanupNameListStore(files.Options.BagCleanupNameListPath!);
        Require((await sharedLists.SaveAsync(new() { Whitelist = new() { "global-item" } })).Success, "create conflicting global cleanup list");
        Require((await new JsonRadarMapStore(files.Options.RadarMapDirectory).SaveAsync(new() { MapId = 47, MapCode = "global" })).Success,
            "create conflicting global radar map");
        var pathProperties = typeof(PathScriptSettings).GetProperties().Where(property => property.Name.EndsWith("PathName", StringComparison.Ordinal)).ToArray();
        Require(pathProperties.Length == 6, "every currently supported path reference participates in the snapshot test");
        for (var number = 1; number <= 6; number++)
        {
            var account = files.Account(number);
            var profileName = $"保留_脚本{number}_同名方案";
            account.ProfileName = profileName;
            account.ScriptSettings = new() { ProfileName = profileName, FixedChannelNumber = 0 };
            account.BagCleanupNameListPath = $"preserved/{number}/lists.json";
            account.RadarMapDirectory = $"preserved/{number}/radar-maps";
            account.OwnerLicenseGrantPath = $"preserved/{number}/owner-license.json";
            var settings = new ScriptSettings { ProfileName = profileName, FixedChannelNumber = number,
                Maintenance = new() { BagCleanupEnabled = true },
                Paths = new() { DeathReviveClickX = 400 + number, DeathReviveClickY = 300 + number } };
            for (var index = 0; index < pathProperties.Length; index++)
            {
                var alias = $"保留_脚本{number}_{pathProperties[index].Name}";
                pathProperties[index].SetValue(settings.Paths, alias);
                Require((await paths.SaveAsync(new SharedPathDocument
                {
                    Name = alias, MapId = 47, BoundStationaryCombatRadius = number + 20,
                    CleanupNpcName = "清包" + number, AuctionNpcName = "交易" + number,
                    Points = new() { new() { X = number * 100 + index, Y = index + 10, Z = number } }
                })).Success, "save preserved path " + alias);
            }
            Require((await profiles.SaveAsync(new() { Name = profileName, Settings = settings })).Success, "save preserved profile");
            var selected = manager.PathsFor(account);
            var accountLists = new JsonBagCleanupNameListStore(selected.BagCleanupNameListPath);
            Require((await accountLists.SaveAsync(new() { Whitelist = new() { "keep-" + number }, Blacklist = new() { "discard-" + number } })).Success,
                "save independent cleanup rules");
            Require((await new JsonRadarMapStore(selected.RadarMapDirectory).SaveAsync(new() { MapId = 47, MapCode = "account-" + number })).Success,
                "save independent radar map");
            var before = JsonSerializer.Serialize(account);
            var builder = new AccountStartConfigBuilder(profiles, sharedLists, NoOpRoadhogLogger.Instance,
                path => new JsonBagCleanupNameListStore(Path.GetFullPath(path, Path.GetDirectoryName(files.Options.AccountConfigPath)!)));
            var result = await builder.BuildAsync(account);
            Require(result.Success && result.Value is not null, "build migrated account: " + result.Error);
            var effective = result.Value!;
            Require(effective.ProfileName == profileName && effective.ScriptSettings!.FixedChannelNumber == number
                && effective.ScriptSettings.Paths.DeathReviveClickX == 400 + number && effective.ScriptSettings.Paths.DeathReviveClickY == 300 + number,
                "the preserved profile remains effective despite a conflicting global profile and inline settings");
            Require(effective.ScriptSettings!.Maintenance.BagCleanupExcludedItemNames.SequenceEqual(new[] { "keep-" + number })
                && effective.ScriptSettings.Maintenance.BagCleanupDiscardItemNameKeywords.SequenceEqual(new[] { "discard-" + number }),
                "startup uses the selected account's cleanup list rather than the newest shared list");
            Require(effective.RevivePathName == settings.Paths.RevivePathName && effective.CombatPathName == settings.Paths.CombatPathName
                && effective.MaintenancePathName == settings.Paths.MaintenancePathName, "legacy path mirrors are synchronized to preserved aliases");
            for (var index = 0; index < pathProperties.Length; index++)
            {
                var name = (string)pathProperties[index].GetValue(effective.ScriptSettings.Paths)!;
                var loaded = await paths.LoadAsync(name);
                Require(loaded.Success && loaded.Value!.Points.Single().X == number * 100 + index
                    && loaded.Value.BoundStationaryCombatRadius == number + 20 && loaded.Value.CleanupNpcName == "清包" + number,
                    "each effective path alias resolves the original account's geometry and metadata");
            }
            var radar = await new JsonRadarMapStore(selected.RadarMapDirectory).LoadAsync(47);
            Require(radar.Success && radar.Value!.Document.MapCode == "account-" + number, "selected radar resource remains account-specific");
            Require(JsonSerializer.Serialize(account) == before, "building the effective snapshot leaves the stored account untouched");
        }
        var legacyAccount = files.Account(7);
        legacyAccount.BagCleanupNameListPath = "preserved/7/missing-lists.json";
        var selectedLegacyFile = Path.Combine(Path.GetDirectoryName(files.Options.AccountConfigPath)!, "preserved", "7", JsonBagCleanupNameListStore.LegacyFileName);
        Directory.CreateDirectory(Path.GetDirectoryName(selectedLegacyFile)!);
        await File.WriteAllTextAsync(selectedLegacyFile, "account-seven-legacy");
        var legacyBuilder = new AccountStartConfigBuilder(profiles, sharedLists, NoOpRoadhogLogger.Instance,
            path => new JsonBagCleanupNameListStore(Path.GetFullPath(path, Path.GetDirectoryName(files.Options.AccountConfigPath)!)));
        var legacyResult = await legacyBuilder.BuildAsync(legacyAccount);
        Require(legacyResult.Success && legacyResult.Value!.ScriptSettings!.Maintenance.BagCleanupExcludedItemNames.SequenceEqual(new[] { "account-seven-legacy" }),
            "missing account JSON falls back only to the selected directory's legacy TXT, without borrowing global lists");
        Require(!(await new AccountStartConfigBuilder(profiles, sharedLists, NoOpRoadhogLogger.Instance).BuildAsync(legacyAccount)).Success,
            "a builder lacking an account resource resolver fails rather than silently applying unrelated shared rules");
    }

    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private sealed class Files : IDisposable
    {
        public string Root { get; } = Path.Combine(FindWorkspace(), ".tmp", "multi-account-tests", "migration-fixtures", Guid.NewGuid().ToString("N"));
        public RoadhogServiceOptions Options { get; }
        public Files()
        {
            var config = Path.Combine(Root, "config");
            Directory.CreateDirectory(config);
            Options = new()
            {
                AccountConfigPath = Path.Combine(config, "accounts.json"), ProfileLibraryDirectory = Path.Combine(config, "profiles"),
                PathLibraryDirectory = Path.Combine(config, "paths"), RadarMapDirectory = Path.Combine(config, "radar-maps"),
                BagCleanupNameListPath = Path.Combine(config, "lists.json"), OwnerLicenseGrantPath = Path.Combine(config, "owner-license.json"),
                LogDirectory = Path.Combine(Root, "logs")
            };
        }
        public AccountConfig Account(int number) => new() { InstanceId = Guid.NewGuid().ToString("N"), AccountName = "snapshot-" + number };
        public void Dispose() => Directory.Delete(Root, recursive: true);
        private static string FindWorkspace()
        {
            for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
                if (File.Exists(Path.Combine(directory.FullName, "Roadhog.Tests", "Roadhog.Tests.csproj"))) return directory.FullName;
            throw new InvalidOperationException("Migration tests must run from the workspace test output.");
        }
    }
}
