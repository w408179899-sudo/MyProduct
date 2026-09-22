using System.Text.Json;
using Roadhog.Core.Accounts;
using Roadhog.Core.Common;
using Roadhog.Core.Hardware;
using Roadhog.Core.Licensing;
using Roadhog.Infrastructure.Composition;
using Roadhog.Infrastructure.Config;
using Roadhog.Infrastructure.Input;
using Roadhog.Infrastructure.Licensing;

internal static class LegacyAccountImporterTests
{
    public static async Task ImportsPreserveSettingsAndLibrariesAsync()
    {
        using var fixture = new ImportFixture();
        var source = fixture.SourceAccount();
        source.ScriptSettings = new ScriptSettings { Combat = new CombatScriptSettings { StalledTargetExclusionSeconds = 137 } };
        var originalSettings = JsonSerializer.Serialize(source.ScriptSettings);
        source.KmBox = null;
        await fixture.SaveSourceAsync(source);
        await new JsonKmBoxNetDeviceConfigStore(Path.Combine(fixture.Source, "kmbox-net.json"))
            .SaveAsync(new KmBoxNetDeviceConfig { IpAddress = "127.0.0.2", Port = 5000, Mac = "aa-bb-01" });
        var credential = Path.Combine(fixture.Source, "license.dat");
        await File.WriteAllTextAsync(credential, "opaque-legacy-credential");
        fixture.SourceLibrary("paths", "route.json", "{\"route\":1}");
        fixture.SourceLibrary("profiles", "profile.json", "{\"profile\":2}");
        fixture.SourceLibrary("radar-maps", "47.json", "{\"mapId\":47}");
        var originalFile = await File.ReadAllTextAsync(fixture.AccountPath);
        var existing = fixture.ExistingAccount();
        existing.AccountName = source.AccountName;
        var result = await new LegacyAccountImporter(fixture.Options).ImportAsync(fixture.AccountPath, new[] { existing });
        Require(result.Count == 1 && result[0].AccountName == source.AccountName + " (old-client)", "duplicate names need a readable source suffix");
        Require(Guid.TryParse(result[0].InstanceId, out _) && result[0].InstanceId != source.InstanceId, "imported accounts need a new instance identity");
        Require(JsonSerializer.Serialize(result[0].ScriptSettings) == originalSettings, "business settings must remain unchanged");
        Require(result[0].KmBox?.IpAddress == "127.0.0.2", "legacy sibling KMBox settings must migrate into the account");
        Require(result[0].LicenseCredentialPath == credential, "credential identity must remain at its original path");
        Require(await File.ReadAllTextAsync(fixture.AccountPath) == originalFile, "the old client configuration must remain unchanged");
        Require(!File.Exists(fixture.Options.AccountConfigPath), "only the main UI may save consolidated accounts");
        Require(File.Exists(Path.Combine(fixture.Options.PathLibraryDirectory, "route.json"))
            && File.Exists(Path.Combine(fixture.Options.ProfileLibraryDirectory, "profile.json"))
            && File.Exists(Path.Combine(fixture.Options.RadarMapDirectory, "47.json")), "all shared libraries must be copied");
    }

    public static async Task SharedConflictIsPreflightedAsync()
    {
        using var fixture = new ImportFixture();
        await fixture.SaveSourceAsync(fixture.SourceAccount());
        fixture.SourceLibrary("paths", "would-copy.json", "same");
        fixture.SourceLibrary("radar-maps", "47.json", "old-map");
        Directory.CreateDirectory(fixture.Options.RadarMapDirectory);
        var targetMap = Path.Combine(fixture.Options.RadarMapDirectory, "47.json");
        await File.WriteAllTextAsync(targetMap, "new-map");
        await ExpectFailure(() => new LegacyAccountImporter(fixture.Options).ImportAsync(fixture.AccountPath, Array.Empty<AccountConfig>()), "同名");
        Require(!File.Exists(Path.Combine(fixture.Options.PathLibraryDirectory, "would-copy.json")), "late library conflict must prevent earlier library writes");
        Require(await File.ReadAllTextAsync(targetMap) == "new-map", "shared conflicts must never overwrite existing data");
        await File.WriteAllTextAsync(targetMap, "old-map");
        var result = await new LegacyAccountImporter(fixture.Options).ImportAsync(fixture.AccountPath, Array.Empty<AccountConfig>());
        Require(result.Count == 1 && File.Exists(Path.Combine(fixture.Options.PathLibraryDirectory, "would-copy.json")),
            "identical shared files must be reusable");
    }

    public static async Task DuplicateBindingsAndCredentialsRejectedAsync()
    {
        foreach (var collision in new[] { "hardware", "vmm", "mac", "endpoint", "credential-path", "credential-copy", "credential-identity" })
        {
            using var fixture = new ImportFixture();
            var source = fixture.SourceAccount();
            var existing = fixture.ExistingAccount();
            var credential = Path.Combine(fixture.Source, "license.dat");
            Directory.CreateDirectory(fixture.Source);
            await File.WriteAllTextAsync(credential, "opaque-credential");
            switch (collision)
            {
                case "hardware": existing.HardwareKey = source.HardwareKey; break;
                case "vmm": existing.VmmDeviceName = "fpga://devindex=001"; break;
                case "mac": existing.KmBox!.Mac = "AA:BB:01"; break;
                case "endpoint": existing.KmBox!.IpAddress = source.KmBox!.IpAddress; existing.KmBox.Port = source.KmBox.Port; break;
                case "credential-path": existing.LicenseCredentialPath = credential; break;
                case "credential-copy":
                    existing.LicenseCredentialPath = Path.Combine(fixture.Root, "copied-license.dat");
                    File.Copy(credential, existing.LicenseCredentialPath);
                    break;
                case "credential-identity":
                    existing.LicenseCredentialPath = Path.Combine(fixture.Root, "reencrypted-license.dat");
                    var shared = LicenseCredential.Create("TEST-IMPORT-SHARED-IDENTITY-2026");
                    Require((await new DpapiLicenseCredentialStore(credential).SaveAsync(shared)).Success, "first credential encryption must succeed");
                    Require((await new DpapiLicenseCredentialStore(existing.LicenseCredentialPath).SaveAsync(shared)).Success, "second credential encryption must succeed");
                    Require(!File.ReadAllBytes(credential).SequenceEqual(File.ReadAllBytes(existing.LicenseCredentialPath)),
                        "fixture must prove independent encryptions differ before checking client identity");
                    break;
            }
            await fixture.SaveSourceAsync(source);
            fixture.SourceLibrary("paths", "route.json", "route");
            await ExpectFailure(() => new LegacyAccountImporter(fixture.Options).ImportAsync(fixture.AccountPath, new[] { existing }), "相同");
            Require(!File.Exists(Path.Combine(fixture.Options.PathLibraryDirectory, "route.json")), collision + " must fail before shared writes");
        }
    }

    public static async Task GenericFpgaNeedsPhysicalResolutionAsync()
    {
        using var fixture = new ImportFixture();
        var source = fixture.SourceAccount();
        source.VmmDeviceName = "fpga";
        await fixture.SaveSourceAsync(source);
        var unavailable = new HardwareResolver(null);
        await ExpectFailure(() => new LegacyAccountImporter(fixture.Options, unavailable).ImportAsync(fixture.AccountPath, Array.Empty<AccountConfig>()), "重新选择");
        var ambiguous = new HardwareResolver("fpga");
        await ExpectFailure(() => new LegacyAccountImporter(fixture.Options, ambiguous).ImportAsync(fixture.AccountPath, Array.Empty<AccountConfig>()), "重新选择");
        var resolved = new HardwareResolver("fpga://devindex=3");
        var result = await new LegacyAccountImporter(fixture.Options, resolved).ImportAsync(fixture.AccountPath, Array.Empty<AccountConfig>());
        Require(result[0].VmmDeviceName == "fpga://devindex=3" && resolved.LastKey == source.HardwareKey,
            "generic fpga must resolve its exact physical key instead of assuming device zero");
    }

    public static async Task InvalidLaterAccountPreventsImportAsync()
    {
        using var fixture = new ImportFixture();
        var first = fixture.SourceAccount();
        var second = fixture.ExistingAccount();
        second.AccountName = "second";
        second.VmmDeviceName = first.VmmDeviceName;
        await fixture.SaveSourceAsync(first, second);
        fixture.SourceLibrary("paths", "route.json", "route");
        await ExpectFailure(() => new LegacyAccountImporter(fixture.Options).ImportAsync(fixture.AccountPath, Array.Empty<AccountConfig>()), "相同");
        Require(!Directory.Exists(fixture.Options.PathLibraryDirectory), "all accounts must be checked before any shared copy");
    }

    public static async Task NameListsCopiedAndConflictPreflightedAsync()
    {
        using var fixture = new ImportFixture();
        await fixture.SaveSourceAsync(fixture.SourceAccount());
        fixture.SourceLibrary("paths", "route.json", "route");
        var sourceJson = Path.Combine(fixture.Source, JsonBagCleanupNameListStore.DefaultFileName);
        var sourceLegacy = Path.Combine(fixture.Source, JsonBagCleanupNameListStore.LegacyFileName);
        await File.WriteAllTextAsync(sourceJson, "{\"whitelist\":[\"保留\"]}");
        await File.WriteAllTextAsync(sourceLegacy, "legacy whitelist");
        var targetDirectory = Path.GetDirectoryName(fixture.Options.AccountConfigPath)!;
        Directory.CreateDirectory(targetDirectory);
        var targetJson = Path.Combine(targetDirectory, JsonBagCleanupNameListStore.DefaultFileName);
        await File.WriteAllTextAsync(targetJson, "{\"whitelist\":[\"主配置\"]}");
        await ExpectFailure(() => new LegacyAccountImporter(fixture.Options).ImportAsync(fixture.AccountPath, Array.Empty<AccountConfig>()), "bag-cleanup-name-lists.json");
        Require(!Directory.Exists(fixture.Options.PathLibraryDirectory), "name list conflict must be detected before copying unrelated libraries");
        await File.WriteAllTextAsync(targetJson, await File.ReadAllTextAsync(sourceJson));
        await new LegacyAccountImporter(fixture.Options).ImportAsync(fixture.AccountPath, Array.Empty<AccountConfig>());
        Require(await File.ReadAllTextAsync(Path.Combine(targetDirectory, JsonBagCleanupNameListStore.LegacyFileName)) == "legacy whitelist",
            "legacy fallback name list must also be preserved");
    }

    private sealed class ImportFixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "roadhog-import-tests-" + Guid.NewGuid().ToString("N"));
        public string Source => Path.Combine(Root, "old-client", "config");
        public string AccountPath => Path.Combine(Source, "accounts.json");
        public RoadhogServiceOptions Options { get; }
        public ImportFixture()
        {
            var target = Path.Combine(Root, "console", "config");
            Options = new RoadhogServiceOptions
            {
                AccountConfigPath = Path.Combine(target, "accounts.json"),
                PathLibraryDirectory = Path.Combine(target, "paths"),
                ProfileLibraryDirectory = Path.Combine(target, "profiles"),
                RadarMapDirectory = Path.Combine(target, "radar-maps"),
                LicenseCredentialPath = Path.Combine(target, "license.dat")
            };
        }
        public AccountConfig SourceAccount() => new()
        {
            InstanceId = Guid.NewGuid().ToString("N"), AccountName = "account", HardwareKey = "port:old",
            VmmDeviceName = "fpga://devindex=1", KmBox = new() { IpAddress = "127.0.0.2", Port = 5000, Mac = "aa-bb-01" }
        };
        public AccountConfig ExistingAccount() => new()
        {
            InstanceId = Guid.NewGuid().ToString("N"), AccountName = "existing", HardwareKey = "port:existing",
            VmmDeviceName = "fpga://devindex=9", KmBox = new() { IpAddress = "127.0.0.9", Port = 5000, Mac = "aa-bb-09" }
        };
        public async Task SaveSourceAsync(params AccountConfig[] accounts)
        {
            var result = await new JsonAccountConfigStore(AccountPath).SaveAllAsync(accounts);
            Require(result.Success, result.Error ?? "fixture account save failed");
        }
        public void SourceLibrary(string library, string name, string value)
        {
            var path = Path.Combine(Source, library, name);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, value);
        }
        public void Dispose()
        {
            var path = Path.GetFullPath(Root);
            Require(path.StartsWith(Path.Combine(Path.GetFullPath(Path.GetTempPath()), "roadhog-import-tests-"), StringComparison.OrdinalIgnoreCase), "test cleanup must remain under its temporary root");
            if (Directory.Exists(path)) Directory.Delete(path, true);
        }
    }

    private sealed class HardwareResolver(string? device) : IHardwareDeviceResolver
    {
        public string? LastKey { get; private set; }
        public IReadOnlyList<HardwareDeviceFeature> ListDevices() => Array.Empty<HardwareDeviceFeature>();
        public OperationResult<HardwareBinding> TryAutoBind(string accountName) => throw new InvalidOperationException("Importer must not auto-bind");
        public OperationResult<HardwareBinding> BindByKey(string accountName, string hardwareKey)
        {
            LastKey = hardwareKey;
            return device is null ? OperationResult<HardwareBinding>.Fail("offline")
                : OperationResult<HardwareBinding>.Ok(new(accountName, hardwareKey, "port", "exact", "", "", "", "", "", "", "", device,
                    new[] { hardwareKey }, DateTimeOffset.UtcNow));
        }
    }

    private static async Task ExpectFailure(Func<Task> action, string fragment)
    {
        try { await action(); }
        catch (InvalidDataException exception)
        {
            Require(exception.Message.Contains(fragment, StringComparison.Ordinal), "missing diagnostic: " + exception.Message);
            return;
        }
        throw new InvalidOperationException("Expected import rejection: " + fragment);
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
