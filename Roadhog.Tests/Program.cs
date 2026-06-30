using Roadhog.Application;
using Roadhog.Application.SemiAuto;
using Roadhog.Application.StationaryCombat;
using Roadhog.Application.Workers;
using Roadhog.Core.Accounts;
using Roadhog.Core.Api;
using Roadhog.Core.Common;
using Roadhog.Core.Diagnostics;
using Roadhog.Core.Input;
using Roadhog.Core.Model;
using Roadhog.Core.Paths;
using Roadhog.Infrastructure.Config;
using Roadhog.Infrastructure.Diagnostics;
using Roadhog.Infrastructure.Input;
using Roadhog.Infrastructure.Paths;

var tests = new (string Name, Func<Task> Run)[]
{
    ("path recorder enforces five meter minimum", TestPathRecorderMinimumDistanceAsync),
    ("shared path store saves loads and deletes path files", TestSharedPathStoreRoundTripAsync),
    ("runtime player read uses account scoped context", TestRuntimePlayerReadUsesAccountScopeAsync),
    ("runtime player read returns character name", TestRuntimePlayerReadReturnsCharacterNameAsync),
    ("runtime kill efficiency tracks kill intervals", TestRuntimeKillEfficiencyTracksKillIntervalsAsync),
    ("file logger rotates when max size is reached", TestFileLoggerRotatesWhenMaxSizeIsReachedAsync),
    ("input backend parser accepts compatible backend names", TestInputBackendParserAsync),
    ("input key map preserves Roadhog supported HID codes", TestInputKeyMapAsync),
    ("kmbox net keyboard input validates unsupported local inputs", TestKmBoxNetKeyboardInputValidationAsync),
    ("account config stores shared path names only", TestAccountConfigStoresSharedPathNamesOnlyAsync),
    ("account config persists stationary combat position", TestAccountConfigPersistsStationaryCombatPositionAsync),
    ("stationary combat target selector keeps monsters inside radius", TestStationaryTargetSelectorAsync),
    ("stationary combat startup recovery follows nearest revive path point", TestStationaryCombatStartupRecoveryFollowsNearestRevivePointAsync),
    ("stationary combat startup recovery skips revive path when home is nearest", TestStationaryCombatStartupRecoverySkipsWhenHomeNearestAsync),
    ("stationary combat death recovery clicks revive and recovers before path", TestStationaryCombatDeathRecoveryClicksReviveAndRecoversBeforePathAsync),
    ("worker life guard revives before semi-auto combat", TestWorkerLifeGuardRevivesBeforeSemiAutoAsync),
    ("worker life guard revives before stationary position validation", TestWorkerLifeGuardRevivesBeforeStationaryPositionValidationAsync),
    ("stationary combat faces selected target before tab", TestStationaryCombatFacesTargetBeforeTabAsync),
    ("stationary combat target pitch follows target height", TestStationaryCombatTargetPitchFollowsTargetHeightAsync),
    ("stationary combat accepts twenty degree pre-lock face tolerance", TestStationaryCombatAcceptsTwentyDegreePreLockFaceToleranceAsync),
    ("stationary combat tabs until selected target is verified", TestStationaryCombatTabsUntilTargetVerifiedAsync),
    ("stationary combat verifies target after each tab press", TestStationaryCombatVerifiesAfterEachTabAsync),
    ("stationary combat nudges then accepts unchanged locked target after tab", TestStationaryCombatNudgesThenAcceptsUnchangedLockedTargetAfterTabAsync),
    ("stationary combat nudges forward when tab locks corpse", TestStationaryCombatNudgesForwardWhenTabLocksCorpseAsync),
    ("stationary combat pending tab verify blocks pre-acquire", TestStationaryCombatPendingTabVerifyBlocksPreAcquireAsync),
    ("stationary combat releases path follow movement after target is verified", TestStationaryCombatReleasesMovementAfterAcquireAsync),
    ("stationary combat does not pulse W while approaching same target", TestStationaryCombatDoesNotPulseWWhileApproachingAsync),
    ("stationary combat ignores target when lock times out", TestStationaryCombatIgnoresTargetWhenLockTimesOutAsync),
    ("stationary combat ignores target when kill times out", TestStationaryCombatIgnoresTargetWhenKillTimesOutAsync),
    ("stationary combat keeps current fight target when lock switches", TestStationaryCombatKeepsCurrentFightTargetWhenLockSwitchesAsync),
    ("stationary combat presses C until locked target targets player", TestStationaryCombatPressesCUntilLockedTargetTargetsPlayerAsync),
    ("stationary combat switches away from target claimed by other", TestStationaryCombatSwitchesAwayFromTargetClaimedByOtherAsync),
    ("stationary combat treats locked zero hp target as combat", TestStationaryCombatTreatsLockedZeroHpTargetAsCombatAsync),
    ("stationary combat loots locked dead target directly", TestStationaryCombatLootsLockedDeadTargetDirectlyAsync),
    ("stationary combat waits after kill before loot key", TestStationaryCombatWaitsAfterKillBeforeLootKeyAsync),
    ("stationary combat waits near corpse after loot key", TestStationaryCombatWaitsNearCorpseAfterLootKeyAsync),
    ("stationary combat finishes current fight before returning home", TestStationaryCombatFinishesFightBeforeReturningHomeAsync),
    ("stationary combat interrupts sit when targeted by monster", TestStationaryCombatInterruptsSitWhenTargetedAsync),
    ("stationary combat hp rule runs before defense target workflow", TestStationaryCombatHpRuleRunsBeforeDefenseTargetWorkflowAsync),
    ("stationary combat stops movement before hp maintenance", TestStationaryCombatStopsMovementBeforeHpMaintenanceAsync),
    ("stationary combat mp sit maintenance runs without defense target", TestStationaryCombatMpSitMaintenanceRunsWithoutDefenseTargetAsync),
    ("skill tree assigns keys by root order and chain children inherit root key", TestSkillTreeKeyMappingAsync),
    ("skill tree maps at most configured roots across the 22 supported keys", TestConfiguredRootKeyBoundaryAsync),
    ("combat tick presses trigger prefix then first ready root", TestCombatTickPressesPrefixThenReadyRootAsync),
    ("combat tick requests only configured skill ids", TestCombatTickRequestsOnlyConfiguredSkillIdsAsync),
    ("observed configured cooldown advance calibrates clock", TestObservedConfiguredCooldownAdvanceCalibratesClockAsync),
    ("uncalibrated nonzero cooldown falls back to first configured root", TestUncalibratedNonzeroCooldownFallsBackToFirstRootAsync),
    ("uncalibrated unknown cooldown rotates after failed attempt", TestUncalibratedUnknownCooldownRotatesAfterFailedAttemptAsync),
    ("calibrated nonzero cooldown skips cooling roots", TestCalibratedNonzeroCooldownSkipsCoolingRootsAsync),
    ("calibrated cooldown tolerance treats near-ready as ready", TestCalibratedCooldownToleranceTreatsNearReadyAsReadyAsync),
    ("observed cooldown survives zero end tick read", TestObservedCooldownSurvivesZeroEndTickReadAsync),
    ("opening attack key switch presses C once", TestOpeningAttackKeySwitchPressesCOnceAsync),
    ("maintenance hp rule presses configured key before skills", TestMaintenanceHpRulePressesConfiguredKeyAsync),
    ("maintenance hp rule runs without attackable target", TestMaintenanceHpRuleRunsWithoutAttackableTargetAsync),
    ("maintenance mp rule presses configured key before skills", TestMaintenanceMpRulePressesConfiguredKeyAsync),
    ("maintenance selected skill confirms by skill id", TestMaintenanceSelectedSkillConfirmsBySkillIdAsync),
    ("maintenance selected cooling skill skips key and continues combat", TestMaintenanceSelectedCoolingSkillSkipsKeyAsync),
    ("stationary combat skips skill maintenance before cooldown calibration", TestStationaryCombatSkipsSkillMaintenanceBeforeCooldownCalibrationAsync),
    ("maintenance sit enters with comma and exits with x", TestMaintenanceSitEnterExitAsync),
    ("maintenance sit enters for low mp and exits on recovery", TestMaintenanceSitMpEnterExitAsync),
    ("semi auto skips sit maintenance", TestSemiAutoSkipsSitMaintenanceAsync),
    ("poll result advances root order", TestPollResultAdvancesRootOrderAsync),
    ("dp skill is skipped until dp value support exists", TestDpSkillSkippedAsync),
    ("chain presses next stage without waiting for source cooldown", TestChainPressesNextStageWithoutSourceCooldownAsync),
    ("chain presses configured child without cooldown filter", TestChainPressesConfiguredChildWithoutCooldownFilterAsync),
    ("chain survives target gap and target switch", TestChainSurvivesTargetGapAsync),
    ("chain lock prevents root fallback while child is missing", TestChainLockPreventsRootFallbackWhileChildMissingAsync),
    ("chain keeps root key and does not fall back in same tick when chain breaks", TestChainStrictOrderAsync),
    ("combat tick counts kill when monster target dies", TestCombatTickCountsKillAsync)
};

var failures = 0;
foreach (var test in tests)
{
    try
    {
        await test.Run().ConfigureAwait(false);
        Console.WriteLine("PASS " + test.Name);
    }
    catch (Exception ex)
    {
        failures++;
        Console.WriteLine("FAIL " + test.Name);
        Console.WriteLine("     " + ex.Message);
    }
}

if (failures > 0)
{
    Environment.ExitCode = 1;
    Console.WriteLine("FAILED " + failures + " test(s).");
}
else
{
    Console.WriteLine("All Roadhog tests passed.");
}

static Task TestPathRecorderMinimumDistanceAsync()
{
    var buffer = new PathRecordingBuffer();
    var now = DateTimeOffset.Parse("2026-06-27T12:00:00+08:00");

    var first = buffer.TryAdd(new Vector3Snapshot(0, 0, 0), now);
    AssertFalse(!first.Success, "first path point should always be accepted");

    var tooClose = buffer.TryAdd(new Vector3Snapshot(4.9F, 0, 0), now.AddSeconds(1));
    AssertFalse(tooClose.Success, "point below five meters must be skipped");

    var exactMinimum = buffer.TryAdd(new Vector3Snapshot(3, 4, 0), now.AddSeconds(2));
    AssertFalse(!exactMinimum.Success, "point at five meters must be accepted");

    AssertEqual(2, buffer.Count, "accepted point count");
    AssertEqual(5.0D, Math.Round(buffer.TotalDistance, 2), "total distance");
    AssertSequence(
        new[] { "0.000, 0.000, 0.000", "3.000, 4.000, 0.000" },
        buffer.ToCoordinateText().Split(Environment.NewLine),
        "coordinate export");

    return Task.CompletedTask;
}

static async Task TestSharedPathStoreRoundTripAsync()
{
    var directory = CreateTempDirectory("roadhog-paths-");
    try
    {
        var store = new JsonSharedPathStore(directory);
        var buffer = new PathRecordingBuffer();
        buffer.TryAdd(new Vector3Snapshot(10, 20, 30), DateTimeOffset.Now);
        buffer.TryAdd(new Vector3Snapshot(16, 20, 30), DateTimeOffset.Now);
        var name = "测试路径/共享";

        var save = await store.SaveAsync(buffer.ToDocument(name)).ConfigureAwait(false);
        AssertFalse(!save.Success, "path save should succeed");

        var summaries = await store.LoadSummariesAsync().ConfigureAwait(false);
        AssertFalse(!summaries.Success, "path summaries should load");
        AssertEqual(1, summaries.Value?.Count ?? 0, "summary count");
        AssertEqual(name, summaries.Value![0].Name, "summary name");
        AssertEqual(2, summaries.Value[0].PointCount, "summary point count");

        var loaded = await store.LoadAsync(name).ConfigureAwait(false);
        AssertFalse(!loaded.Success, "saved path should load");
        AssertEqual(2, loaded.Value?.PointCount ?? 0, "loaded point count");
        AssertEqual(6.0D, Math.Round(loaded.Value?.TotalDistance ?? 0, 2), "loaded total distance");

        var delete = await store.DeleteAsync(name).ConfigureAwait(false);
        AssertFalse(!delete.Success, "path delete should succeed");
        summaries = await store.LoadSummariesAsync().ConfigureAwait(false);
        AssertEqual(0, summaries.Value?.Count ?? -1, "summary count after delete");
    }
    finally
    {
        DeleteDirectoryIfExists(directory);
    }
}

static async Task TestRuntimePlayerReadUsesAccountScopeAsync()
{
    var logger = new InMemoryRoadhogLogger();
    var accounts = new AccountRuntimeManager(logger);
    accounts.MarkStarting(new AccountConfig
    {
        AccountName = "account-scope",
        ProcessId = 712,
        TargetProcessName = "Aion.bin",
        VmmDeviceName = "fpga"
    });

    var gameApi = new FakeGameApi
    {
        Player = new PlayerSnapshot(
            10,
            11,
            "Scoped Character",
            100,
            100,
            50,
            50,
            0,
            new Vector3Snapshot(1, 2, 3),
            DateTimeOffset.Now)
    };
    var runtime = new RoadhogRuntime(gameApi, logger, accounts, null!);

    var result = await runtime.ReadPlayerAsync("account-scope").ConfigureAwait(false);

    AssertFalse(!result.Success, "runtime player read should succeed");
    AssertEqual(712, gameApi.LastPlayerContext?.ProcessId ?? 0, "scoped process id");
    AssertEqual("Aion.bin", gameApi.LastPlayerContext?.TargetProcessName ?? string.Empty, "scoped process name");
    AssertEqual("fpga", gameApi.LastPlayerContext?.VmmDeviceName ?? string.Empty, "scoped vmm device");
}

static async Task TestRuntimePlayerReadReturnsCharacterNameAsync()
{
    var logger = new InMemoryRoadhogLogger();
    var gameApi = new FakeGameApi
    {
        Player = new PlayerSnapshot(
            10,
            0,
            "测试角色",
            100,
            100,
            50,
            50,
            0,
            new Vector3Snapshot(1, 2, 3),
            DateTimeOffset.Now)
    };
    var runtime = new RoadhogRuntime(gameApi, logger, new AccountRuntimeManager(logger), null!);

    var result = await runtime.ReadPlayerAsync("account-character").ConfigureAwait(false);

    AssertFalse(!result.Success, "runtime player read should succeed");
    AssertEqual("测试角色", result.Value?.CharacterName ?? string.Empty, "character name");
}

static Task TestRuntimeKillEfficiencyTracksKillIntervalsAsync()
{
    var logger = new InMemoryRoadhogLogger();
    var runtimeStates = new AccountRuntimeManager(logger);
    runtimeStates.GetOrCreate("account1");

    var firstKillAt = new DateTimeOffset(2026, 6, 30, 8, 0, 0, TimeSpan.Zero);
    var duplicateAt = firstKillAt.AddMilliseconds(200);
    var secondKillAt = firstKillAt.AddSeconds(20);

    AssertFalse(
        !runtimeStates.MarkKill("account1", 100, 1000, firstKillAt),
        "first kill should count");
    AssertFalse(
        runtimeStates.MarkKill("account1", 100, 1000, duplicateAt),
        "same server object kill should be suppressed");
    AssertFalse(
        !runtimeStates.MarkKill("account1", 101, 1001, secondKillAt),
        "second kill should count");

    var snapshot = runtimeStates.Snapshot().First();
    AssertEqual(2, snapshot.KillCount, "kill count");
    AssertEqual(firstKillAt, snapshot.FirstKillAt!.Value, "first kill at");
    AssertEqual(secondKillAt, snapshot.LastKillAt!.Value, "last kill at");

    return Task.CompletedTask;
}

static Task TestFileLoggerRotatesWhenMaxSizeIsReachedAsync()
{
    var directory = CreateTempDirectory("roadhog-logs-");
    try
    {
        var logger = new FileRoadhogLogger(directory, maxLogFileBytes: 512);
        var payload = new string('x', 180);
        for (var index = 0; index < 8; index++)
        {
            logger.Info("test.large_log_entry", new Dictionary<string, object?>
            {
                ["index"] = index,
                ["payload"] = payload
            });
        }

        var archiveLogs = Directory.GetFiles(directory, "roadhog-*.log")
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
        AssertFalse(archiveLogs.Length < 2, "logger should create rotated archive log files");

        foreach (var path in archiveLogs)
        {
            AssertFalse(new FileInfo(path).Length > 512, Path.GetFileName(path) + " should not exceed configured max bytes");
        }

        var latestPath = Path.Combine(directory, "latest.log");
        AssertFalse(!File.Exists(latestPath), "latest.log should be written");
        AssertFalse(new FileInfo(latestPath).Length > 512, "latest.log should not exceed configured max bytes");
        AssertFalse(
            !archiveLogs.Any(path => Path.GetFileName(path).Count(ch => ch == '-') >= 2),
            "rotated archive log should include a timestamp suffix");
    }
    finally
    {
        DeleteDirectoryIfExists(directory);
    }

    return Task.CompletedTask;
}

static Task TestInputBackendParserAsync()
{
    AssertFalse(
        !RoadhogInputBackendParser.TryParse("hardware_box", out var hardwareBox),
        "hardware_box backend should parse");
    AssertEqual(RoadhogInputBackend.HardwareBox, hardwareBox, "hardware_box backend");

    AssertFalse(
        !RoadhogInputBackendParser.TryParse("kmbox net", out var kmBoxNet),
        "kmbox net backend should parse");
    AssertEqual(RoadhogInputBackend.KmBoxNet, kmBoxNet, "kmbox net backend");

    AssertFalse(
        !RoadhogInputBackendParser.TryParse("udp", out var udp),
        "udp backend alias should parse");
    AssertEqual(RoadhogInputBackend.KmBoxNet, udp, "udp backend alias");

    AssertFalse(
        RoadhogInputBackendParser.TryParse("unknown_backend", out _),
        "unknown backend should not parse");

    AssertEqual(
        RoadhogInputBackend.KmBoxNet,
        RoadhogInputBackendParser.ParseOrDefault("unknown_backend", RoadhogInputBackend.KmBoxNet),
        "unknown backend should use fallback");

    return Task.CompletedTask;
}

static Task TestInputKeyMapAsync()
{
    AssertHidCode("C", 0x06);
    AssertHidCode("S", 0x16);
    AssertHidCode(" W ", 0x1A);
    AssertHidCode("D1", 0x1E);
    AssertHidCode("D0", 0x27);
    AssertHidCode("OemMinus", 0x2D);
    AssertHidCode("OemPlus", 0x2E);
    AssertHidCode("OemComma", 0x36);
    AssertHidCode("Tab", 0x2B);
    AssertHidCode("F9", 0x42);
    AssertHidCode("NumPad0", 0x62);
    AssertHidCode("NumPadDecimal", 0x63);

    AssertFalse(
        RoadhogInputKeyMap.TryResolveHidCode("Enter", out _),
        "unsupported key should not resolve");

    return Task.CompletedTask;
}

static async Task TestKmBoxNetKeyboardInputValidationAsync()
{
    using var input = new KmBoxNetKeyboardInput(new KmBoxNetKeyboardInputOptions
    {
        IpAddress = "127.0.0.1",
        Port = 1,
        Mac = "00112233"
    });

    var unsupportedKey = await input.PressKeyAsync("Enter", TimeSpan.Zero).ConfigureAwait(false);
    AssertFalse(unsupportedKey.Success, "unsupported KMBox Net key should fail before connect");

    var unsupportedButton = await input.MouseDownAsync(RoadhogMouseButton.Side1).ConfigureAwait(false);
    AssertFalse(unsupportedButton.Success, "unsupported KMBox Net mouse button should fail before connect");
}

static async Task TestAccountConfigStoresSharedPathNamesOnlyAsync()
{
    var directory = CreateTempDirectory("roadhog-account-paths-");
    try
    {
        var accountPath = Path.Combine(directory, "accounts.json");
        var store = new JsonAccountConfigStore(accountPath);
        var pathName = "共享打怪路径001";
        var account = new AccountConfig
        {
            AccountName = "account-path",
            ScriptSettings = new ScriptSettings
            {
                Paths = new PathScriptSettings
                {
                    CombatPathName = pathName
                }
            }
        };

        var save = await store.UpsertAsync(account).ConfigureAwait(false);
        AssertFalse(!save.Success, "account save should succeed");

        var text = await File.ReadAllTextAsync(accountPath).ConfigureAwait(false);
        AssertFalse(!text.Contains("\"CombatPathName\"", StringComparison.Ordinal), "account config should contain shared path reference field");
        AssertFalse(text.Contains("\"Points\"", StringComparison.Ordinal), "account config should not contain path points");

        var load = await store.LoadAllAsync().ConfigureAwait(false);
        AssertFalse(!load.Success, "account config should load");
        AssertEqual(pathName, load.Value?[0].ScriptSettings?.Paths.CombatPathName ?? string.Empty, "loaded combat path name");
    }
    finally
    {
        DeleteDirectoryIfExists(directory);
    }
}

static async Task TestAccountConfigPersistsStationaryCombatPositionAsync()
{
    var directory = CreateTempDirectory("roadhog-stationary-combat-");
    try
    {
        var accountPath = Path.Combine(directory, "accounts.json");
        var store = new JsonAccountConfigStore(accountPath);
        var account = new AccountConfig
        {
            AccountName = "account-stationary",
            ScriptSettings = new ScriptSettings
            {
                MainMode = AccountMainMode.CustomCombat,
                CombatMode = AccountCombatMode.Stationary,
                Combat = new CombatScriptSettings
                {
                    EnableLoot = true,
                    HasStationaryCombatPosition = true,
                    StationaryCombatX = 1307.758D,
                    StationaryCombatY = 2844.230D,
                    StationaryCombatZ = 259.832D,
                    StationaryCombatRadius = 42.5D
                }
            }
        };

        var save = await store.UpsertAsync(account).ConfigureAwait(false);
        AssertFalse(!save.Success, "stationary combat account save should succeed");

        var load = await store.LoadAllAsync().ConfigureAwait(false);
        AssertFalse(!load.Success, "stationary combat account should load");
        var combat = load.Value?[0].ScriptSettings?.Combat;
        AssertFalse(combat is null, "combat settings should load");
        AssertFalse(!combat!.HasStationaryCombatPosition, "stationary combat position flag should persist");
        AssertEqual(1307.758D, combat.StationaryCombatX, "stationary x");
        AssertEqual(2844.230D, combat.StationaryCombatY, "stationary y");
        AssertEqual(259.832D, combat.StationaryCombatZ, "stationary z");
        AssertEqual(42.5D, combat.StationaryCombatRadius, "stationary radius");

        var clone = account.ScriptSettings.Combat.Clone();
        AssertFalse(!clone.HasStationaryCombatPosition, "stationary combat position flag should clone");
        AssertEqual(1307.758D, clone.StationaryCombatX, "cloned stationary x");
        AssertEqual(42.5D, clone.StationaryCombatRadius, "cloned stationary radius");
    }
    finally
    {
        DeleteDirectoryIfExists(directory);
    }
}

static Task TestStationaryTargetSelectorAsync()
{
    var player = new Vector3Snapshot(0, 0, 0);
    var home = new Vector3Snapshot(0, 0, 0);
    var objects = new[]
    {
        new WorldObjectSnapshot(10, 10, "npc", "npc", new Vector3Snapshot(1, 0, 0), 1, 100, 100),
        new WorldObjectSnapshot(11, 11, "dead", "monster", new Vector3Snapshot(2, 0, 0), 2, 0, 100),
        new WorldObjectSnapshot(12, 12, "outside", "monster", new Vector3Snapshot(40, 0, 0), 40, 100, 100),
        new WorldObjectSnapshot(13, 13, "farther", "monster", new Vector3Snapshot(8, 0, 0), 8, 100, 100),
        new WorldObjectSnapshot(14, 14, "nearest", "monster", new Vector3Snapshot(5, 0, 0), 5, 100, 100)
    };

    var selected = StationaryCombatTargetSelector.SelectNearest(objects, player, home, 30);

    AssertEqual((ushort)14, selected?.EntityId ?? 0, "nearest selectable monster");
    return Task.CompletedTask;
}

static async Task TestStationaryCombatStartupRecoveryFollowsNearestRevivePointAsync()
{
    var previousBearingMode = Environment.GetEnvironmentVariable("AION_FACE_TARGET_BEARING_MODE");
    Environment.SetEnvironmentVariable("AION_FACE_TARGET_BEARING_MODE", "y-x");
    try
    {
        var settings = CreateScriptSettings();
        settings.MainMode = AccountMainMode.CustomCombat;
        settings.CombatMode = AccountCombatMode.Stationary;
        settings.Paths.RevivePathName = "revive-a";
        settings.Combat = new CombatScriptSettings
        {
            HasStationaryCombatPosition = true,
            StationaryCombatX = 100,
            StationaryCombatY = 0,
            StationaryCombatZ = 0,
            StationaryCombatRadius = 20
        };

        var pathStore = new InMemorySharedPathStore(
            CreatePath("revive-a",
                new Vector3Snapshot(0, 0, 0),
                new Vector3Snapshot(10, 0, 0),
                new Vector3Snapshot(50, 0, 0),
                new Vector3Snapshot(100, 0, 0)));
        var keyboard = new RecordingKeyboardInput();
        var logger = new InMemoryRoadhogLogger();
        var gameApi = new FakeGameApi
        {
            Player = new PlayerSnapshot(1, 0, "Fake", 100, 100, 100, 100, 0, new Vector3Snapshot(10, 0, 0), DateTimeOffset.Now, 90, 10, 90),
            WorldObjects = Array.Empty<WorldObjectSnapshot>(),
            Skills = CreateSkillSnapshotsById(new Dictionary<uint, uint>())
        };
        var semiAuto = new SemiAutoCombatController(keyboard);
        var controller = new StationaryCombatController(keyboard, semiAuto, pathStore);
        var state = new StationaryCombatState();

        await controller
            .TickAsync(CreateContext(settings, gameApi, logger), SemiAutoSkillPlan.FromSettings(settings.Skills), new SemiAutoCombatState(), state)
            .ConfigureAwait(false);

        AssertFalse(!state.StartupRecoveryActive, "startup recovery should be active");
        AssertEqual(2, state.StartupRecoveryPointIndex, "second path point should be reached and next point should be active");
        AssertFalse(!keyboard.KeyDowns.Contains("W"), "startup recovery should move along revive path");
        AssertFalse(!logger.Entries.Any(entry =>
            entry.EventName == "stationary_combat.startup_recovery.selected" &&
            Convert.ToInt32(entry.Fields["startPointIndex"]) == 1),
            "nearest revive path point should be logged");
    }
    finally
    {
        Environment.SetEnvironmentVariable("AION_FACE_TARGET_BEARING_MODE", previousBearingMode);
    }
}

static async Task TestStationaryCombatStartupRecoverySkipsWhenHomeNearestAsync()
{
    var settings = CreateScriptSettings();
    settings.MainMode = AccountMainMode.CustomCombat;
    settings.CombatMode = AccountCombatMode.Stationary;
    settings.Paths.RevivePathName = "revive-a";
    settings.Combat = new CombatScriptSettings
    {
        HasStationaryCombatPosition = true,
        StationaryCombatX = 100,
        StationaryCombatY = 0,
        StationaryCombatZ = 0,
        StationaryCombatRadius = 20
    };

    var pathStore = new InMemorySharedPathStore(
        CreatePath("revive-a",
            new Vector3Snapshot(0, 0, 0),
            new Vector3Snapshot(10, 0, 0),
            new Vector3Snapshot(50, 0, 0)));
    var keyboard = new RecordingKeyboardInput();
    var logger = new InMemoryRoadhogLogger();
    var gameApi = new FakeGameApi
    {
        Player = new PlayerSnapshot(1, 0, "Fake", 100, 100, 100, 100, 0, new Vector3Snapshot(100, 0, 0), DateTimeOffset.Now, 90, 10, 90),
        TargetEntityId = 0,
        TargetCurrentHp = 1000,
        TargetPosition = new Vector3Snapshot(105, 0, 0),
        WorldObjects = new[]
        {
            new WorldObjectSnapshot(100, 100, "target", "monster", new Vector3Snapshot(105, 0, 0), 5, 1000, 1000)
        },
        Skills = CreateSkillSnapshotsById(new Dictionary<uint, uint>
        {
            [1] = 0,
            [5] = 0,
            [6] = 0,
            [7] = 0,
            [8] = 0,
            [9] = 0,
            [10] = 0
        })
    };
    var semiAuto = new SemiAutoCombatController(keyboard);
    var controller = new StationaryCombatController(keyboard, semiAuto, pathStore);
    var state = new StationaryCombatState();

    await controller
        .TickAsync(CreateContext(settings, gameApi, logger), SemiAutoSkillPlan.FromSettings(settings.Skills), new SemiAutoCombatState(), state)
        .ConfigureAwait(false);

    AssertFalse(state.StartupRecoveryActive, "startup recovery should not be active when home is nearest");
    AssertFalse(!state.StartupRecoveryChecked, "startup recovery should be checked once");
    AssertEqual((ushort)100, state.CandidateEntityId, "home-nearest startup should continue normal stationary target selection");
    AssertFalse(!logger.Entries.Any(entry => entry.EventName == "stationary_combat.startup_recovery.home_nearest"),
        "home-nearest recovery decision should be logged");
}

static async Task TestStationaryCombatDeathRecoveryClicksReviveAndRecoversBeforePathAsync()
{
    var previousClickDelay = Environment.GetEnvironmentVariable("ROADHOG_DEATH_REVIVE_CLICK_DELAY_MS");
    var previousStepDelay = Environment.GetEnvironmentVariable("ROADHOG_DEATH_REVIVE_MOUSE_STEP_DELAY_MS");
    var previousClickHold = Environment.GetEnvironmentVariable("ROADHOG_DEATH_REVIVE_CLICK_HOLD_MS");
    var previousRetry = Environment.GetEnvironmentVariable("ROADHOG_DEATH_REVIVE_RETRY_MS");
    var previousScrollInterval = Environment.GetEnvironmentVariable("ROADHOG_DEATH_POST_REVIVE_SCROLL_INTERVAL_MS");
    var previousBearingMode = Environment.GetEnvironmentVariable("AION_FACE_TARGET_BEARING_MODE");
    Environment.SetEnvironmentVariable("ROADHOG_DEATH_REVIVE_CLICK_DELAY_MS", "0");
    Environment.SetEnvironmentVariable("ROADHOG_DEATH_REVIVE_MOUSE_STEP_DELAY_MS", "0");
    Environment.SetEnvironmentVariable("ROADHOG_DEATH_REVIVE_CLICK_HOLD_MS", "1");
    Environment.SetEnvironmentVariable("ROADHOG_DEATH_REVIVE_RETRY_MS", "0");
    Environment.SetEnvironmentVariable("ROADHOG_DEATH_POST_REVIVE_SCROLL_INTERVAL_MS", "0");
    Environment.SetEnvironmentVariable("AION_FACE_TARGET_BEARING_MODE", "y-x");
    try
    {
        var settings = CreateScriptSettings();
        settings.MainMode = AccountMainMode.CustomCombat;
        settings.CombatMode = AccountCombatMode.Stationary;
        settings.Paths.RevivePathName = "revive-a";
        settings.Maintenance.SitMaintenanceEnabled = true;
        settings.Maintenance.SitHpRecoverToPercent = 75;
        settings.Combat = new CombatScriptSettings
        {
            HasStationaryCombatPosition = true,
            StationaryCombatX = 20,
            StationaryCombatY = 0,
            StationaryCombatZ = 0,
            StationaryCombatRadius = 10
        };

        var pathStore = new InMemorySharedPathStore(
            CreatePath("revive-a",
                new Vector3Snapshot(0, 0, 0),
                new Vector3Snapshot(10, 0, 0),
                new Vector3Snapshot(20, 0, 0)));
        var keyboard = new RecordingKeyboardInput();
        var logger = new InMemoryRoadhogLogger();
        var gameApi = new FakeGameApi
        {
            Player = new PlayerSnapshot(1, 0, "Fake", 0, 100, 100, 100, 0, new Vector3Snapshot(0, 0, 0), DateTimeOffset.Now, 90, 10, 90),
            TargetEntityId = 0,
            TargetCurrentHp = 0,
            TargetPosition = null,
            WorldObjects = Array.Empty<WorldObjectSnapshot>(),
            Skills = CreateSkillSnapshotsById(new Dictionary<uint, uint>())
        };
        var semiAuto = new SemiAutoCombatController(keyboard);
        var controller = new StationaryCombatController(keyboard, semiAuto, pathStore);
        var stationaryState = new StationaryCombatState();
        var semiAutoState = new SemiAutoCombatState();
        var plan = SemiAutoSkillPlan.FromSettings(settings.Skills);
        var context = CreateContext(settings, gameApi, logger);

        await controller.TickAsync(context, plan, semiAutoState, stationaryState).ConfigureAwait(false);

        AssertEqual(StationaryCombatTopLevelState.DeathRecovery, stationaryState.TopLevelState, "dead player should enter death recovery");
        AssertSequence(
            new[] { "move:-32768,-32768", "move:-32768,-32768", "move:680,460", "down:Left", "up:Left" },
            keyboard.MouseCommands.ToArray(),
            "death recovery should absolute-click revive button");
        AssertFalse(keyboard.Keys.Contains("Tab"), "death recovery must not enter target acquisition");
        AssertFalse(keyboard.Keys.Any(key => key.StartsWith("D", StringComparison.OrdinalIgnoreCase)), "death recovery must not release combat skills");

        await controller.TickAsync(context, plan, semiAutoState, stationaryState).ConfigureAwait(false);
        AssertSequence(
            new[]
            {
                "move:-32768,-32768",
                "move:-32768,-32768",
                "move:680,460",
                "down:Left",
                "up:Left"
            },
            keyboard.MouseCommands.Skip(5).Take(5).ToArray(),
            "death recovery should retry revive click when player is still dead after retry delay");
        AssertEqual(2, stationaryState.DeathRecovery.ReviveClickCount, "death recovery should record retry revive click count");

        gameApi.Player = gameApi.Player with { CurrentHp = 10 };
        await controller.TickAsync(context, plan, semiAutoState, stationaryState).ConfigureAwait(false);
        AssertSequence(
            Enumerable.Repeat("wheel:-1", 10).ToArray(),
            keyboard.MouseCommands.Skip(10).Take(10).ToArray(),
            "revived player should scroll wheel down ten times before maintenance");
        AssertSequence(new[] { "OemComma" }, keyboard.Keys.ToArray(), "revived low hp should sit for recovery");
        AssertFalse(!semiAutoState.IsMaintenanceResting, "revive recovery should track resting state");

        gameApi.Player = gameApi.Player with { CurrentHp = 74 };
        await controller.TickAsync(context, plan, semiAutoState, stationaryState).ConfigureAwait(false);
        AssertSequence(new[] { "OemComma" }, keyboard.Keys.ToArray(), "revive recovery should keep sitting below recover percent");

        gameApi.Player = gameApi.Player with { CurrentHp = 75 };
        await controller.TickAsync(context, plan, semiAutoState, stationaryState).ConfigureAwait(false);
        AssertSequence(new[] { "OemComma", "X" }, keyboard.Keys.ToArray(), "revive recovery should stand up at recover percent");

        await controller.TickAsync(context, plan, semiAutoState, stationaryState).ConfigureAwait(false);
        AssertEqual(StationaryCombatDeathRecoveryStep.FollowRevivePath, stationaryState.DeathRecovery.Step, "recovered player should start revive path follow");
        AssertFalse(!keyboard.KeyDowns.Contains("W"), "recovered player should move along revive path");
    }
    finally
    {
        Environment.SetEnvironmentVariable("ROADHOG_DEATH_REVIVE_CLICK_DELAY_MS", previousClickDelay);
        Environment.SetEnvironmentVariable("ROADHOG_DEATH_REVIVE_MOUSE_STEP_DELAY_MS", previousStepDelay);
        Environment.SetEnvironmentVariable("ROADHOG_DEATH_REVIVE_CLICK_HOLD_MS", previousClickHold);
        Environment.SetEnvironmentVariable("ROADHOG_DEATH_REVIVE_RETRY_MS", previousRetry);
        Environment.SetEnvironmentVariable("ROADHOG_DEATH_POST_REVIVE_SCROLL_INTERVAL_MS", previousScrollInterval);
        Environment.SetEnvironmentVariable("AION_FACE_TARGET_BEARING_MODE", previousBearingMode);
    }
}

static async Task TestWorkerLifeGuardRevivesBeforeSemiAutoAsync()
{
    var previousClickDelay = Environment.GetEnvironmentVariable("ROADHOG_DEATH_REVIVE_CLICK_DELAY_MS");
    var previousStepDelay = Environment.GetEnvironmentVariable("ROADHOG_DEATH_REVIVE_MOUSE_STEP_DELAY_MS");
    var previousClickHold = Environment.GetEnvironmentVariable("ROADHOG_DEATH_REVIVE_CLICK_HOLD_MS");
    Environment.SetEnvironmentVariable("ROADHOG_DEATH_REVIVE_CLICK_DELAY_MS", "0");
    Environment.SetEnvironmentVariable("ROADHOG_DEATH_REVIVE_MOUSE_STEP_DELAY_MS", "0");
    Environment.SetEnvironmentVariable("ROADHOG_DEATH_REVIVE_CLICK_HOLD_MS", "1");
    try
    {
        var settings = CreateScriptSettings();
        settings.MainMode = AccountMainMode.SemiAuto;
        var keyboard = new RecordingKeyboardInput();
        var logger = new InMemoryRoadhogLogger();
        var gameApi = new FakeGameApi
        {
            Player = new PlayerSnapshot(1, 100, "Fake", 0, 100, 100, 100, 0, new Vector3Snapshot(0, 0, 0), DateTimeOffset.Now),
            TargetEntityId = 100,
            TargetCurrentHp = 1000,
            TargetMaxHp = 1000,
            TargetPosition = new Vector3Snapshot(5, 0, 0),
            Skills = CreateSkillSnapshotsById(new Dictionary<uint, uint>
            {
                [1] = 0,
                [5] = 0,
                [6] = 0
            })
        };
        var semiAuto = new SemiAutoCombatController(keyboard);
        var stationary = new StationaryCombatController(keyboard, semiAuto);
        var worker = new DefaultAccountWorkerLoop(keyboard, semiAuto, stationary);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var context = CreateContext(
            settings,
            gameApi,
            logger,
            options: new AccountWorkerOptions { TickInterval = TimeSpan.FromMilliseconds(40) },
            stopToken: cts.Token);

        var runTask = worker.RunAsync(context);
        await WaitUntilAsync(
                () => keyboard.MouseCommands.Contains("up:Left"),
                "semi-auto worker death guard revive click")
            .ConfigureAwait(false);
        cts.Cancel();
        await IgnoreCancellationAsync(runTask).ConfigureAwait(false);

        AssertSequence(
            new[] { "move:-32768,-32768", "move:-32768,-32768", "move:680,460", "down:Left", "up:Left" },
            keyboard.MouseCommands.Take(5).ToArray(),
            "semi-auto death guard should absolute-click revive button");
        AssertFalse(keyboard.Keys.Any(key => key.StartsWith("D", StringComparison.OrdinalIgnoreCase)), "semi-auto combat keys must not run while dead");
        AssertFalse(!logger.Entries.Any(entry => entry.EventName == "player_life.death.detected"), "worker life guard should log death detection");
    }
    finally
    {
        Environment.SetEnvironmentVariable("ROADHOG_DEATH_REVIVE_CLICK_DELAY_MS", previousClickDelay);
        Environment.SetEnvironmentVariable("ROADHOG_DEATH_REVIVE_MOUSE_STEP_DELAY_MS", previousStepDelay);
        Environment.SetEnvironmentVariable("ROADHOG_DEATH_REVIVE_CLICK_HOLD_MS", previousClickHold);
    }
}

static async Task TestWorkerLifeGuardRevivesBeforeStationaryPositionValidationAsync()
{
    var previousClickDelay = Environment.GetEnvironmentVariable("ROADHOG_DEATH_REVIVE_CLICK_DELAY_MS");
    var previousStepDelay = Environment.GetEnvironmentVariable("ROADHOG_DEATH_REVIVE_MOUSE_STEP_DELAY_MS");
    var previousClickHold = Environment.GetEnvironmentVariable("ROADHOG_DEATH_REVIVE_CLICK_HOLD_MS");
    Environment.SetEnvironmentVariable("ROADHOG_DEATH_REVIVE_CLICK_DELAY_MS", "0");
    Environment.SetEnvironmentVariable("ROADHOG_DEATH_REVIVE_MOUSE_STEP_DELAY_MS", "0");
    Environment.SetEnvironmentVariable("ROADHOG_DEATH_REVIVE_CLICK_HOLD_MS", "1");
    try
    {
        var settings = CreateScriptSettings();
        settings.MainMode = AccountMainMode.CustomCombat;
        settings.CombatMode = AccountCombatMode.Stationary;
        settings.Combat.HasStationaryCombatPosition = false;
        var keyboard = new RecordingKeyboardInput();
        var logger = new InMemoryRoadhogLogger();
        var gameApi = new FakeGameApi
        {
            Player = new PlayerSnapshot(1, 0, "Fake", 0, 100, 100, 100, 0, new Vector3Snapshot(0, 0, 0), DateTimeOffset.Now),
            TargetEntityId = 0,
            TargetCurrentHp = 0,
            TargetPosition = null,
            Skills = CreateSkillSnapshotsById(new Dictionary<uint, uint>())
        };
        var semiAuto = new SemiAutoCombatController(keyboard);
        var stationary = new StationaryCombatController(keyboard, semiAuto);
        var worker = new DefaultAccountWorkerLoop(keyboard, semiAuto, stationary);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var context = CreateContext(
            settings,
            gameApi,
            logger,
            options: new AccountWorkerOptions { TickInterval = TimeSpan.FromMilliseconds(40) },
            stopToken: cts.Token);

        var runTask = worker.RunAsync(context);
        await WaitUntilAsync(
                () => keyboard.MouseCommands.Contains("up:Left"),
                "stationary worker death guard revive click")
            .ConfigureAwait(false);
        cts.Cancel();
        await IgnoreCancellationAsync(runTask).ConfigureAwait(false);

        AssertFalse(!keyboard.MouseCommands.Contains("up:Left"), "stationary worker should revive before checking combat position");
        AssertFalse(logger.Entries.Any(entry => entry.EventName == "stationary_combat.position.missing"), "death recovery should block normal stationary validation while dead");
    }
    finally
    {
        Environment.SetEnvironmentVariable("ROADHOG_DEATH_REVIVE_CLICK_DELAY_MS", previousClickDelay);
        Environment.SetEnvironmentVariable("ROADHOG_DEATH_REVIVE_MOUSE_STEP_DELAY_MS", previousStepDelay);
        Environment.SetEnvironmentVariable("ROADHOG_DEATH_REVIVE_CLICK_HOLD_MS", previousClickHold);
    }
}

static async Task TestStationaryCombatFacesTargetBeforeTabAsync()
{
    var previousBearingMode = Environment.GetEnvironmentVariable("AION_FACE_TARGET_BEARING_MODE");
    Environment.SetEnvironmentVariable("AION_FACE_TARGET_BEARING_MODE", "y-x");
    try
    {
        var settings = CreateScriptSettings();
        settings.MainMode = AccountMainMode.CustomCombat;
        settings.CombatMode = AccountCombatMode.Stationary;
        settings.Combat = new CombatScriptSettings
        {
            HasStationaryCombatPosition = true,
            StationaryCombatX = 0,
            StationaryCombatY = 0,
            StationaryCombatZ = 0,
            StationaryCombatRadius = 30
        };

        var keyboard = new RecordingKeyboardInput();
        var logger = new InMemoryRoadhogLogger();
        var gameApi = new FakeGameApi
        {
            Player = new PlayerSnapshot(1, 100, "Fake", 100, 100, 100, 100, 0, new Vector3Snapshot(0, 0, 0), DateTimeOffset.Now, 0, 10, 0),
            TargetEntityId = 0,
            TargetCurrentHp = 1000,
            TargetPosition = new Vector3Snapshot(5, 0, 0),
            WorldObjects = new[]
            {
                new WorldObjectSnapshot(100, 100, "target", "monster", new Vector3Snapshot(5, 0, 0), 5, 1000, 1000)
            },
            Skills = CreateSkillSnapshotsById(new Dictionary<uint, uint>
            {
                [1] = 0,
                [5] = 0,
                [6] = 0,
                [7] = 0,
                [8] = 0,
                [9] = 0,
                [10] = 0
            })
        };
        var semiAuto = new SemiAutoCombatController(keyboard);
        var controller = new StationaryCombatController(keyboard, semiAuto);
        var plan = SemiAutoSkillPlan.FromSettings(settings.Skills);

        await controller
            .TickAsync(CreateContext(settings, gameApi, logger), plan, new SemiAutoCombatState(), new StationaryCombatState())
            .ConfigureAwait(false);

        AssertFalse(!keyboard.MouseCommands.Contains("down:Right"), "unfaced target should hold right mouse");
        AssertFalse(!keyboard.MouseCommands.Any(command => command.StartsWith("move:", StringComparison.Ordinal)), "unfaced target should move mouse");
        AssertFalse(keyboard.Keys.Contains("Tab"), "unfaced target should not Tab yet");
        AssertFalse(keyboard.Keys.Contains("D2"), "unfaced target should not release skills yet");
        AssertFalse(!logger.Entries.Any(entry => entry.EventName == "stationary_combat.face_target"), "face target action should be logged");
    }
    finally
    {
        Environment.SetEnvironmentVariable("AION_FACE_TARGET_BEARING_MODE", previousBearingMode);
    }
}

static async Task TestStationaryCombatTargetPitchFollowsTargetHeightAsync()
{
    var previousBearingMode = Environment.GetEnvironmentVariable("AION_FACE_TARGET_BEARING_MODE");
    var previousUseWorldPitch = Environment.GetEnvironmentVariable("AION_CAMERA_USE_WORLD_TARGET_PITCH");
    Environment.SetEnvironmentVariable("AION_FACE_TARGET_BEARING_MODE", "y-x");
    Environment.SetEnvironmentVariable("AION_CAMERA_USE_WORLD_TARGET_PITCH", "true");
    try
    {
        var settings = CreateScriptSettings();
        settings.MainMode = AccountMainMode.CustomCombat;
        settings.CombatMode = AccountCombatMode.Stationary;
        settings.Combat = new CombatScriptSettings
        {
            HasStationaryCombatPosition = true,
            StationaryCombatX = 0,
            StationaryCombatY = 0,
            StationaryCombatZ = 0,
            StationaryCombatRadius = 30
        };

        var keyboard = new RecordingKeyboardInput();
        var logger = new InMemoryRoadhogLogger();
        var gameApi = new FakeGameApi
        {
            Player = new PlayerSnapshot(1, 100, "Fake", 100, 100, 100, 100, 0, new Vector3Snapshot(0, 0, 0), DateTimeOffset.Now, 90, 20, 90),
            TargetEntityId = 0,
            TargetCurrentHp = 1000,
            TargetPosition = new Vector3Snapshot(5, 0, 5),
            WorldObjects = new[]
            {
                new WorldObjectSnapshot(100, 100, "target", "monster", new Vector3Snapshot(5, 0, 5), 7.07, 1000, 1000)
            },
            Skills = CreateSkillSnapshotsById(new Dictionary<uint, uint>
            {
                [1] = 0,
                [5] = 0,
                [6] = 0,
                [7] = 0,
                [8] = 0,
                [9] = 0,
                [10] = 0
            })
        };
        var semiAuto = new SemiAutoCombatController(keyboard);
        var controller = new StationaryCombatController(keyboard, semiAuto);
        var plan = SemiAutoSkillPlan.FromSettings(settings.Skills);

        await controller
            .TickAsync(CreateContext(settings, gameApi, logger), plan, new SemiAutoCombatState(), new StationaryCombatState())
            .ConfigureAwait(false);

        AssertFalse(!keyboard.MouseCommands.Any(command => command.StartsWith("move:", StringComparison.Ordinal)), "height-derived pitch should move mouse even when yaw is aligned");
        AssertFalse(keyboard.Keys.Contains("Tab"), "height-derived pitch should align camera before Tab");
        var faceEntry = logger.Entries.LastOrDefault(entry => entry.EventName == "stationary_combat.face_target");
        AssertFalse(faceEntry is null, "face target log should exist");
        AssertFalse(Math.Abs(Convert.ToDouble(faceEntry!.Fields["worldPitch"]) - (-45.0D)) > 0.01D, "world pitch should use camera pitch quadrant");
        AssertFalse(Math.Abs(Convert.ToDouble(faceEntry.Fields["targetPitch"]) - (-35.0D)) > 0.01D, "target pitch should follow world pitch plus 10 degrees");
    }
    finally
    {
        Environment.SetEnvironmentVariable("AION_FACE_TARGET_BEARING_MODE", previousBearingMode);
        Environment.SetEnvironmentVariable("AION_CAMERA_USE_WORLD_TARGET_PITCH", previousUseWorldPitch);
    }
}

static async Task TestStationaryCombatAcceptsTwentyDegreePreLockFaceToleranceAsync()
{
    var previousBearingMode = Environment.GetEnvironmentVariable("AION_FACE_TARGET_BEARING_MODE");
    var previousYawOffset = Environment.GetEnvironmentVariable("AION_FACE_TARGET_YAW_OFFSET_DEG");
    var previousPathPitch = Environment.GetEnvironmentVariable("AION_PATH_FOLLOW_PITCH_DEG");
    var previousCameraPitch = Environment.GetEnvironmentVariable("AION_CAMERA_FIXED_PITCH_DEG");
    Environment.SetEnvironmentVariable("AION_FACE_TARGET_BEARING_MODE", "y-x");
    Environment.SetEnvironmentVariable("AION_FACE_TARGET_YAW_OFFSET_DEG", "0");
    Environment.SetEnvironmentVariable("AION_PATH_FOLLOW_PITCH_DEG", "20");
    Environment.SetEnvironmentVariable("AION_CAMERA_FIXED_PITCH_DEG", "20");
    try
    {
        var settings = CreateScriptSettings();
        settings.MainMode = AccountMainMode.CustomCombat;
        settings.CombatMode = AccountCombatMode.Stationary;
        settings.Combat = new CombatScriptSettings
        {
            HasStationaryCombatPosition = true,
            StationaryCombatX = 0,
            StationaryCombatY = 0,
            StationaryCombatZ = 0,
            StationaryCombatRadius = 30
        };

        var keyboard = new RecordingKeyboardInput();
        var logger = new InMemoryRoadhogLogger();
        var gameApi = new FakeGameApi
        {
            Player = new PlayerSnapshot(1, 999, "Fake", 100, 100, 100, 100, 0, new Vector3Snapshot(0, 0, 0), DateTimeOffset.Now, 75, 10, 75),
            TargetEntityId = 999,
            TargetCurrentHp = 1000,
            TargetPosition = new Vector3Snapshot(5, 0, 0),
            WorldObjects = new[]
            {
                new WorldObjectSnapshot(100, 100, "target", "monster", new Vector3Snapshot(5, 0, 0), 5, 1000, 1000)
            },
            Skills = CreateSkillSnapshotsById(new Dictionary<uint, uint>
            {
                [1] = 0,
                [5] = 0,
                [6] = 0,
                [7] = 0,
                [8] = 0,
                [9] = 0,
                [10] = 0
            })
        };
        var semiAuto = new SemiAutoCombatController(keyboard);
        var controller = new StationaryCombatController(keyboard, semiAuto);
        var plan = SemiAutoSkillPlan.FromSettings(settings.Skills);

        await controller
            .TickAsync(CreateContext(settings, gameApi, logger), plan, new SemiAutoCombatState(), new StationaryCombatState())
            .ConfigureAwait(false);

        AssertFalse(keyboard.MouseCommands.Any(command => command.StartsWith("move:", StringComparison.Ordinal)), "15 degree pre-lock yaw error should not move mouse");
        AssertFalse(!keyboard.Keys.Contains("Tab"), "15 degree pre-lock yaw error should continue to Tab verification");
        AssertFalse(!logger.Entries.Any(entry =>
            entry.EventName == "stationary_combat.face_target" &&
            string.Equals(Convert.ToString(entry.Fields["action"]), "face_aligned", StringComparison.Ordinal) &&
            Math.Abs(Convert.ToDouble(entry.Fields["yawTolerance"]) - 20.0D) < 0.001D),
            "face target log should show 20 degree yaw tolerance");
    }
    finally
    {
        Environment.SetEnvironmentVariable("AION_FACE_TARGET_BEARING_MODE", previousBearingMode);
        Environment.SetEnvironmentVariable("AION_FACE_TARGET_YAW_OFFSET_DEG", previousYawOffset);
        Environment.SetEnvironmentVariable("AION_PATH_FOLLOW_PITCH_DEG", previousPathPitch);
        Environment.SetEnvironmentVariable("AION_CAMERA_FIXED_PITCH_DEG", previousCameraPitch);
    }
}

static async Task TestStationaryCombatTabsUntilTargetVerifiedAsync()
{
    var previousBearingMode = Environment.GetEnvironmentVariable("AION_FACE_TARGET_BEARING_MODE");
    Environment.SetEnvironmentVariable("AION_FACE_TARGET_BEARING_MODE", "y-x");
    try
    {
        var settings = CreateScriptSettings();
        settings.MainMode = AccountMainMode.CustomCombat;
        settings.CombatMode = AccountCombatMode.Stationary;
        settings.Combat = new CombatScriptSettings
        {
            HasStationaryCombatPosition = true,
            StationaryCombatX = 0,
            StationaryCombatY = 0,
            StationaryCombatZ = 0,
            StationaryCombatRadius = 30
        };

        var keyboard = new RecordingKeyboardInput();
        var logger = new InMemoryRoadhogLogger();
        var gameApi = new FakeGameApi
        {
            Player = new PlayerSnapshot(1, 0, "Fake", 100, 100, 100, 100, 0, new Vector3Snapshot(0, 0, 0), DateTimeOffset.Now, 90, 10, 90),
            TargetEntityId = 999,
            TargetCurrentHp = 1000,
            TargetPosition = new Vector3Snapshot(5, 0, 0),
            WorldObjects = new[]
            {
                new WorldObjectSnapshot(100, 100, "target", "monster", new Vector3Snapshot(5, 0, 0), 5, 1000, 1000)
            },
            Skills = CreateSkillSnapshotsById(new Dictionary<uint, uint>
            {
                [1] = 0,
                [5] = 0,
                [6] = 0,
                [7] = 0,
                [8] = 0,
                [9] = 0,
                [10] = 0
            })
        };
        var semiAuto = new SemiAutoCombatController(keyboard);
        var controller = new StationaryCombatController(keyboard, semiAuto);
        var plan = SemiAutoSkillPlan.FromSettings(settings.Skills);
        var state = new StationaryCombatState();
        var semiAutoState = new SemiAutoCombatState();
        var context = CreateContext(settings, gameApi, logger);

        await controller.TickAsync(context, plan, semiAutoState, state).ConfigureAwait(false);
        AssertFalse(!keyboard.Keys.Contains("Tab"), "first acquire tick should press Tab when target is wrong");

        gameApi.TargetEntityId = 100;
        await controller.TickAsync(context, plan, semiAutoState, state).ConfigureAwait(false);
        AssertFalse(!keyboard.Keys.Contains("D2"), "verified target should enter semi-auto skill release");
    }
    finally
    {
        Environment.SetEnvironmentVariable("AION_FACE_TARGET_BEARING_MODE", previousBearingMode);
    }
}

static async Task TestStationaryCombatVerifiesAfterEachTabAsync()
{
    var previousBearingMode = Environment.GetEnvironmentVariable("AION_FACE_TARGET_BEARING_MODE");
    var previousTabDelay = Environment.GetEnvironmentVariable("ROADHOG_STATIONARY_TAB_VERIFY_DELAY_MS");
    Environment.SetEnvironmentVariable("AION_FACE_TARGET_BEARING_MODE", "y-x");
    Environment.SetEnvironmentVariable("ROADHOG_STATIONARY_TAB_VERIFY_DELAY_MS", "0");
    try
    {
        var settings = CreateScriptSettings();
        settings.MainMode = AccountMainMode.CustomCombat;
        settings.CombatMode = AccountCombatMode.Stationary;
        settings.Combat = new CombatScriptSettings
        {
            HasStationaryCombatPosition = true,
            StationaryCombatX = 0,
            StationaryCombatY = 0,
            StationaryCombatZ = 0,
            StationaryCombatRadius = 30
        };

        var logger = new InMemoryRoadhogLogger();
        var gameApi = new FakeGameApi
        {
            Player = new PlayerSnapshot(1, 0, "Fake", 100, 100, 100, 100, 0, new Vector3Snapshot(0, 0, 0), DateTimeOffset.Now, 90, 10, 90),
            TargetEntityId = 999,
            TargetCurrentHp = 1000,
            TargetPosition = new Vector3Snapshot(5, 0, 0),
            WorldObjects = new[]
            {
                new WorldObjectSnapshot(100, 100, "target", "monster", new Vector3Snapshot(5, 0, 0), 5, 1000, 1000)
            },
            Skills = CreateSkillSnapshotsById(new Dictionary<uint, uint>
            {
                [1] = 0,
                [5] = 0,
                [6] = 0,
                [7] = 0,
                [8] = 0,
                [9] = 0,
                [10] = 0
            })
        };
        var keyboard = new RecordingKeyboardInput
        {
            AfterPress = key =>
            {
                if (string.Equals(key, "Tab", StringComparison.OrdinalIgnoreCase))
                {
                    gameApi.TargetEntityId = 100;
                }
            }
        };
        var semiAuto = new SemiAutoCombatController(keyboard);
        var controller = new StationaryCombatController(keyboard, semiAuto);
        var plan = SemiAutoSkillPlan.FromSettings(settings.Skills);

        await controller
            .TickAsync(CreateContext(settings, gameApi, logger), plan, new SemiAutoCombatState(), new StationaryCombatState())
            .ConfigureAwait(false);

        AssertFalse(!keyboard.Keys.Contains("Tab"), "acquire tick should press Tab");
        AssertFalse(!keyboard.Keys.Contains("D2"), "same tick should release skills after after-tab verify succeeds");
        AssertFalse(!logger.Entries.Any(entry => entry.EventName == "stationary_combat.tab.verify"), "after-tab verify should be logged");
        AssertFalse(!logger.Entries.Any(entry =>
            entry.EventName == "stationary_combat.target.acquired" &&
            string.Equals(Convert.ToString(entry.Fields["phase"]), "after_tab", StringComparison.Ordinal)),
            "after-tab target acquisition should be logged");
    }
    finally
    {
        Environment.SetEnvironmentVariable("AION_FACE_TARGET_BEARING_MODE", previousBearingMode);
        Environment.SetEnvironmentVariable("ROADHOG_STATIONARY_TAB_VERIFY_DELAY_MS", previousTabDelay);
    }
}

static async Task TestStationaryCombatNudgesThenAcceptsUnchangedLockedTargetAfterTabAsync()
{
    var previousBearingMode = Environment.GetEnvironmentVariable("AION_FACE_TARGET_BEARING_MODE");
    var previousTabDelay = Environment.GetEnvironmentVariable("ROADHOG_STATIONARY_TAB_VERIFY_DELAY_MS");
    var previousWrongLockNudgeHold = Environment.GetEnvironmentVariable("ROADHOG_STATIONARY_TAB_WRONG_LOCK_NUDGE_HOLD_MS");
    Environment.SetEnvironmentVariable("AION_FACE_TARGET_BEARING_MODE", "y-x");
    Environment.SetEnvironmentVariable("ROADHOG_STATIONARY_TAB_VERIFY_DELAY_MS", "0");
    Environment.SetEnvironmentVariable("ROADHOG_STATIONARY_TAB_WRONG_LOCK_NUDGE_HOLD_MS", "1");
    try
    {
        var settings = CreateScriptSettings();
        settings.MainMode = AccountMainMode.CustomCombat;
        settings.CombatMode = AccountCombatMode.Stationary;
        settings.Combat = new CombatScriptSettings
        {
            HasStationaryCombatPosition = true,
            StationaryCombatX = 0,
            StationaryCombatY = 0,
            StationaryCombatZ = 0,
            StationaryCombatRadius = 30
        };

        var logger = new InMemoryRoadhogLogger();
        var gameApi = new FakeGameApi
        {
            Player = new PlayerSnapshot(1, 0, "Fake", 100, 100, 100, 100, 0, new Vector3Snapshot(0, 0, 0), DateTimeOffset.Now, 90, 10, 90),
            TargetEntityId = 200,
            TargetCurrentHp = 1000,
            TargetMaxHp = 1000,
            TargetPosition = new Vector3Snapshot(8, 0, 0),
            WorldObjects = new[]
            {
                new WorldObjectSnapshot(100, 100, "candidate", "monster", new Vector3Snapshot(5, 0, 0), 5, 1000, 1000),
                new WorldObjectSnapshot(200, 200, "locked", "monster", new Vector3Snapshot(8, 0, 0), 8, 1000, 1000)
            },
            Skills = CreateSkillSnapshotsById(new Dictionary<uint, uint>
            {
                [1] = 0,
                [5] = 0,
                [6] = 0,
                [7] = 0,
                [8] = 0,
                [9] = 0,
                [10] = 0
            })
        };
        var keyboard = new RecordingKeyboardInput
        {
            AfterPress = key =>
            {
                if (string.Equals(key, "Tab", StringComparison.OrdinalIgnoreCase))
                {
                    gameApi.TargetEntityId = 200;
                    gameApi.TargetPosition = new Vector3Snapshot(8, 0, 0);
                }
            }
        };
        var semiAuto = new SemiAutoCombatController(keyboard);
        var controller = new StationaryCombatController(keyboard, semiAuto);
        var plan = SemiAutoSkillPlan.FromSettings(settings.Skills);
        var state = new StationaryCombatState();

        await controller
            .TickAsync(CreateContext(settings, gameApi, logger), plan, new SemiAutoCombatState(), state)
            .ConfigureAwait(false);

        AssertSequence(new[] { "Tab", "W" }, keyboard.Keys, "first unchanged wrong lock should nudge forward");
        AssertFalse(keyboard.Keys.Contains("D2"), "first unchanged wrong lock must not enter skill release");
        AssertFalse(state.Fighting, "first unchanged wrong lock must not enter fight state");
        AssertEqual((ushort)100, state.CandidateEntityId, "first unchanged wrong lock should keep original candidate");
        AssertFalse(!logger.Entries.Any(entry =>
            entry.EventName == "stationary_combat.tab.wrong_lock_nudge_pressed" &&
            Equals(Convert.ToUInt16(entry.Fields["candidateEntityId"]), (ushort)100) &&
            Equals(Convert.ToUInt16(entry.Fields["lockedEntityId"]), (ushort)200)),
            "first unchanged wrong lock should log W nudge");
        AssertFalse(logger.Entries.Any(entry => entry.EventName == "stationary_combat.target.switched_to_locked"), "first unchanged wrong lock must not accept locked target");

        await controller
            .TickAsync(CreateContext(settings, gameApi, logger), plan, new SemiAutoCombatState(), state)
            .ConfigureAwait(false);

        AssertEqual(2, keyboard.Keys.Count(key => string.Equals(key, "Tab", StringComparison.OrdinalIgnoreCase)), "second acquire tick should press Tab again");
        AssertFalse(!keyboard.Keys.Contains("D2"), "second unchanged wrong lock should enter skill release through fallback");
        AssertFalse(!state.Fighting, "second unchanged wrong lock should enter fight state");
        AssertEqual((ushort)200, state.CurrentTargetEntityId, "current target should switch to the locked target");
        AssertEqual((ushort)200, state.CandidateEntityId, "candidate should switch to the locked target");
        AssertFalse(!logger.Entries.Any(entry =>
            entry.EventName == "stationary_combat.target.switched_to_locked" &&
            Equals(Convert.ToUInt16(entry.Fields["candidateEntityId"]), (ushort)100) &&
            Equals(Convert.ToUInt16(entry.Fields["lockedEntityId"]), (ushort)200) &&
            string.Equals(Convert.ToString(entry.Fields["phase"]), "after_tab_fallback", StringComparison.Ordinal)),
            "locked target fallback switch should be logged");
        AssertFalse(!logger.Entries.Any(entry =>
            entry.EventName == "stationary_combat.target.acquired" &&
            Equals(Convert.ToUInt16(entry.Fields["targetEntityId"]), (ushort)200) &&
            string.Equals(Convert.ToString(entry.Fields["phase"]), "after_tab_fallback", StringComparison.Ordinal)),
            "fallback acquired target should be the locked target");
    }
    finally
    {
        Environment.SetEnvironmentVariable("AION_FACE_TARGET_BEARING_MODE", previousBearingMode);
        Environment.SetEnvironmentVariable("ROADHOG_STATIONARY_TAB_VERIFY_DELAY_MS", previousTabDelay);
        Environment.SetEnvironmentVariable("ROADHOG_STATIONARY_TAB_WRONG_LOCK_NUDGE_HOLD_MS", previousWrongLockNudgeHold);
    }
}

static async Task TestStationaryCombatNudgesForwardWhenTabLocksCorpseAsync()
{
    var previousBearingMode = Environment.GetEnvironmentVariable("AION_FACE_TARGET_BEARING_MODE");
    var previousTabDelay = Environment.GetEnvironmentVariable("ROADHOG_STATIONARY_TAB_VERIFY_DELAY_MS");
    Environment.SetEnvironmentVariable("AION_FACE_TARGET_BEARING_MODE", "y-x");
    Environment.SetEnvironmentVariable("ROADHOG_STATIONARY_TAB_VERIFY_DELAY_MS", "0");
    try
    {
        var settings = CreateScriptSettings();
        settings.MainMode = AccountMainMode.CustomCombat;
        settings.CombatMode = AccountCombatMode.Stationary;
        settings.Combat = new CombatScriptSettings
        {
            HasStationaryCombatPosition = true,
            StationaryCombatX = 0,
            StationaryCombatY = 0,
            StationaryCombatZ = 0,
            StationaryCombatRadius = 30
        };

        var logger = new InMemoryRoadhogLogger();
        var keyboard = new RecordingKeyboardInput();
        var gameApi = new FakeGameApi
        {
            Player = new PlayerSnapshot(1, 0, "Fake", 100, 100, 100, 100, 0, new Vector3Snapshot(0, 0, 0), DateTimeOffset.Now, 90, 10, 90),
            TargetEntityId = 200,
            TargetCurrentHp = 0,
            TargetMaxHp = 1000,
            TargetPosition = new Vector3Snapshot(3, 0, 0),
            WorldObjects = new[]
            {
                new WorldObjectSnapshot(100, 100, "target", "monster", new Vector3Snapshot(5, 0, 0), 5, 1000, 1000)
            },
            Skills = CreateSkillSnapshotsById(new Dictionary<uint, uint>
            {
                [1] = 0,
                [5] = 0,
                [6] = 0,
                [7] = 0,
                [8] = 0,
                [9] = 0,
                [10] = 0
            })
        };
        var controller = new StationaryCombatController(keyboard, new SemiAutoCombatController(keyboard));
        var plan = SemiAutoSkillPlan.FromSettings(settings.Skills);
        var state = new StationaryCombatState();
        var context = CreateContext(settings, gameApi, logger);

        await controller
            .TickAsync(context, plan, new SemiAutoCombatState(), state)
            .ConfigureAwait(false);

        AssertSequence(new[] { "Tab", "W" }, keyboard.Keys, "tab corpse nudge key sequence");
        AssertFalse(keyboard.Keys.Contains("D2"), "corpse lock must not release skills");
        AssertFalse(!logger.Entries.Any(entry => entry.EventName == "stationary_combat.tab.corpse_nudge_pressed"), "corpse nudge should be logged");

        await controller
            .TickAsync(context, plan, new SemiAutoCombatState(), state)
            .ConfigureAwait(false);

        AssertEqual(1, keyboard.Keys.Count(key => string.Equals(key, "W", StringComparison.OrdinalIgnoreCase)), "same pending tab verify should nudge only once");
    }
    finally
    {
        Environment.SetEnvironmentVariable("AION_FACE_TARGET_BEARING_MODE", previousBearingMode);
        Environment.SetEnvironmentVariable("ROADHOG_STATIONARY_TAB_VERIFY_DELAY_MS", previousTabDelay);
    }
}

static async Task TestStationaryCombatPendingTabVerifyBlocksPreAcquireAsync()
{
    var previousBearingMode = Environment.GetEnvironmentVariable("AION_FACE_TARGET_BEARING_MODE");
    var previousTabDelay = Environment.GetEnvironmentVariable("ROADHOG_STATIONARY_TAB_VERIFY_DELAY_MS");
    Environment.SetEnvironmentVariable("AION_FACE_TARGET_BEARING_MODE", "y-x");
    Environment.SetEnvironmentVariable("ROADHOG_STATIONARY_TAB_VERIFY_DELAY_MS", "0");
    try
    {
        var settings = CreateScriptSettings();
        settings.MainMode = AccountMainMode.CustomCombat;
        settings.CombatMode = AccountCombatMode.Stationary;
        settings.Combat = new CombatScriptSettings
        {
            HasStationaryCombatPosition = true,
            StationaryCombatX = 0,
            StationaryCombatY = 0,
            StationaryCombatZ = 0,
            StationaryCombatRadius = 30
        };

        var logger = new InMemoryRoadhogLogger();
        var keyboard = new RecordingKeyboardInput();
        var gameApi = new FakeGameApi
        {
            Player = new PlayerSnapshot(1, 0, "Fake", 100, 100, 100, 100, 0, new Vector3Snapshot(0, 0, 0), DateTimeOffset.Now, 90, 10, 90),
            TargetEntityId = 999,
            TargetCurrentHp = 1000,
            TargetPosition = new Vector3Snapshot(5, 0, 0),
            WorldObjects = new[]
            {
                new WorldObjectSnapshot(100, 100, "target", "monster", new Vector3Snapshot(5, 0, 0), 5, 1000, 1000)
            },
            Skills = CreateSkillSnapshotsById(new Dictionary<uint, uint>
            {
                [1] = 0,
                [5] = 0,
                [6] = 0,
                [7] = 0,
                [8] = 0,
                [9] = 0,
                [10] = 0
            })
        };
        var semiAuto = new SemiAutoCombatController(keyboard);
        var controller = new StationaryCombatController(keyboard, semiAuto);
        var plan = SemiAutoSkillPlan.FromSettings(settings.Skills);
        var state = new StationaryCombatState();
        var semiAutoState = new SemiAutoCombatState();
        var context = CreateContext(settings, gameApi, logger);

        await controller.TickAsync(context, plan, semiAutoState, state).ConfigureAwait(false);

        AssertFalse(!keyboard.Keys.Contains("Tab"), "first acquire tick should press Tab");
        AssertFalse(keyboard.Keys.Contains("D2"), "failed immediate tab verify must not release skills");
        AssertFalse(logger.Entries.Any(entry => entry.EventName == "stationary_combat.target.acquired"), "failed immediate tab verify must not acquire");

        gameApi.TargetEntityId = 100;
        await controller.TickAsync(context, plan, semiAutoState, state).ConfigureAwait(false);

        AssertFalse(!keyboard.Keys.Contains("D2"), "delayed tab match should enter semi-auto skill release");
        AssertFalse(!logger.Entries.Any(entry =>
            entry.EventName == "stationary_combat.target.acquired" &&
            string.Equals(Convert.ToString(entry.Fields["phase"]), "after_tab", StringComparison.Ordinal)),
            "delayed target update must acquire through tab verification");
        AssertFalse(logger.Entries.Any(entry =>
            entry.EventName == "stationary_combat.target.acquired" &&
            !string.Equals(Convert.ToString(entry.Fields["phase"]), "after_tab", StringComparison.Ordinal)),
            "pending tab verification must block pre-move/pre-tab acquire");
    }
    finally
    {
        Environment.SetEnvironmentVariable("AION_FACE_TARGET_BEARING_MODE", previousBearingMode);
        Environment.SetEnvironmentVariable("ROADHOG_STATIONARY_TAB_VERIFY_DELAY_MS", previousTabDelay);
    }
}

static async Task TestStationaryCombatReleasesMovementAfterAcquireAsync()
{
    var previousBearingMode = Environment.GetEnvironmentVariable("AION_FACE_TARGET_BEARING_MODE");
    Environment.SetEnvironmentVariable("AION_FACE_TARGET_BEARING_MODE", "y-x");
    try
    {
        var settings = CreateScriptSettings();
        settings.MainMode = AccountMainMode.CustomCombat;
        settings.CombatMode = AccountCombatMode.Stationary;
        settings.Combat = new CombatScriptSettings
        {
            HasStationaryCombatPosition = true,
            StationaryCombatX = 0,
            StationaryCombatY = 0,
            StationaryCombatZ = 0,
            StationaryCombatRadius = 60
        };

        var keyboard = new RecordingKeyboardInput();
        var logger = new InMemoryRoadhogLogger();
        var gameApi = new FakeGameApi
        {
            Player = new PlayerSnapshot(1, 0, "Fake", 100, 100, 100, 100, 0, new Vector3Snapshot(0, 0, 0), DateTimeOffset.Now, 90, 10, 90),
            TargetEntityId = 999,
            TargetCurrentHp = 1000,
            TargetPosition = new Vector3Snapshot(40, 0, 0),
            WorldObjects = new[]
            {
                new WorldObjectSnapshot(100, 100, "target", "monster", new Vector3Snapshot(40, 0, 0), 40, 1000, 1000)
            },
            Skills = CreateSkillSnapshotsById(new Dictionary<uint, uint>
            {
                [1] = 0,
                [5] = 0,
                [6] = 0,
                [7] = 0,
                [8] = 0,
                [9] = 0,
                [10] = 0
            })
        };
        var semiAuto = new SemiAutoCombatController(keyboard);
        var controller = new StationaryCombatController(keyboard, semiAuto);
        var plan = SemiAutoSkillPlan.FromSettings(settings.Skills);
        var state = new StationaryCombatState();
        var semiAutoState = new SemiAutoCombatState();
        var context = CreateContext(settings, gameApi, logger);

        await controller.TickAsync(context, plan, semiAutoState, state).ConfigureAwait(false);

        AssertFalse(!state.IsMovingForward, "approach should hold W while outside acquire distance");
        AssertFalse(!state.IsRightMouseDown, "approach should hold right mouse while outside acquire distance");
        AssertFalse(!keyboard.KeyDowns.Contains("W"), "approach should send W down");

        gameApi.TargetEntityId = 100;
        gameApi.Player = gameApi.Player with { Position = new Vector3Snapshot(20, 0, 0), TargetEntityId = 100 };
        await controller.TickAsync(context, plan, semiAutoState, state).ConfigureAwait(false);

        AssertFalse(state.IsMovingForward, "verified target should release W");
        AssertFalse(state.IsRightMouseDown, "verified target should release right mouse");
        AssertFalse(!keyboard.KeyUps.Contains("W"), "verified target should send W up");
        AssertFalse(!keyboard.MouseCommands.Contains("up:Right"), "verified target should send right mouse up");
        AssertFalse(!keyboard.Keys.Contains("D2"), "verified target should enter semi-auto skill release");
    }
    finally
    {
        Environment.SetEnvironmentVariable("AION_FACE_TARGET_BEARING_MODE", previousBearingMode);
    }
}

static async Task TestStationaryCombatDoesNotPulseWWhileApproachingAsync()
{
    var previousBearingMode = Environment.GetEnvironmentVariable("AION_FACE_TARGET_BEARING_MODE");
    Environment.SetEnvironmentVariable("AION_FACE_TARGET_BEARING_MODE", "y-x");
    try
    {
        var settings = CreateScriptSettings();
        settings.MainMode = AccountMainMode.CustomCombat;
        settings.CombatMode = AccountCombatMode.Stationary;
        settings.Combat = new CombatScriptSettings
        {
            HasStationaryCombatPosition = true,
            StationaryCombatX = 0,
            StationaryCombatY = 0,
            StationaryCombatZ = 0,
            StationaryCombatRadius = 60
        };

        var keyboard = new RecordingKeyboardInput();
        var logger = new InMemoryRoadhogLogger();
        var gameApi = new FakeGameApi
        {
            Player = new PlayerSnapshot(1, 999, "Fake", 100, 100, 100, 100, 0, new Vector3Snapshot(0, 0, 0), DateTimeOffset.Now, 90, 10, 90),
            TargetEntityId = 999,
            TargetCurrentHp = 1000,
            TargetPosition = new Vector3Snapshot(40, 0, 0),
            WorldObjects = new[]
            {
                new WorldObjectSnapshot(100, 100, "target", "monster", new Vector3Snapshot(40, 0, 0), 40, 1000, 1000)
            },
            Skills = CreateSkillSnapshotsById(new Dictionary<uint, uint>
            {
                [1] = 0,
                [5] = 0,
                [6] = 0,
                [7] = 0,
                [8] = 0,
                [9] = 0,
                [10] = 0
            })
        };
        var semiAuto = new SemiAutoCombatController(keyboard);
        var controller = new StationaryCombatController(keyboard, semiAuto);
        var plan = SemiAutoSkillPlan.FromSettings(settings.Skills);
        var state = new StationaryCombatState();
        var semiAutoState = new SemiAutoCombatState();
        var context = CreateContext(settings, gameApi, logger);

        await controller.TickAsync(context, plan, semiAutoState, state).ConfigureAwait(false);
        var firstKeyUpCount = keyboard.KeyUps.Count(key => string.Equals(key, "W", StringComparison.OrdinalIgnoreCase));
        var firstKeyDownCount = keyboard.KeyDowns.Count(key => string.Equals(key, "W", StringComparison.OrdinalIgnoreCase));
        AssertFalse(!state.IsMovingForward, "first approach tick should hold W");

        await controller.TickAsync(context, plan, semiAutoState, state).ConfigureAwait(false);
        var secondKeyUpCount = keyboard.KeyUps.Count(key => string.Equals(key, "W", StringComparison.OrdinalIgnoreCase));
        var secondKeyDownCount = keyboard.KeyDowns.Count(key => string.Equals(key, "W", StringComparison.OrdinalIgnoreCase));

        AssertEqual(firstKeyUpCount, secondKeyUpCount, "same candidate approach must not send another W up");
        AssertEqual(firstKeyDownCount, secondKeyDownCount, "same candidate approach must not send another W down");
        AssertEqual((ushort)100, state.FacedCandidateEntityId, "candidate should stay marked as initially faced");
    }
    finally
    {
        Environment.SetEnvironmentVariable("AION_FACE_TARGET_BEARING_MODE", previousBearingMode);
    }
}

static async Task TestStationaryCombatIgnoresTargetWhenLockTimesOutAsync()
{
    var previousBearingMode = Environment.GetEnvironmentVariable("AION_FACE_TARGET_BEARING_MODE");
    Environment.SetEnvironmentVariable("AION_FACE_TARGET_BEARING_MODE", "y-x");
    try
    {
        var settings = CreateScriptSettings();
        settings.MainMode = AccountMainMode.CustomCombat;
        settings.CombatMode = AccountCombatMode.Stationary;
        settings.Combat = new CombatScriptSettings
        {
            HasStationaryCombatPosition = true,
            StationaryCombatX = 0,
            StationaryCombatY = 0,
            StationaryCombatZ = 0,
            StationaryCombatRadius = 60
        };

        var keyboard = new RecordingKeyboardInput();
        var logger = new InMemoryRoadhogLogger();
        var gameApi = new FakeGameApi
        {
            Player = new PlayerSnapshot(1, 999, "Fake", 100, 100, 100, 100, 0, new Vector3Snapshot(0, 0, 0), DateTimeOffset.Now, 90, 10, 90),
            TargetEntityId = 999,
            TargetCurrentHp = 1000,
            TargetPosition = new Vector3Snapshot(40, 0, 0),
            WorldObjects = new[]
            {
                new WorldObjectSnapshot(100, 100, "stuck", "monster", new Vector3Snapshot(40, 0, 0), 40, 1000, 1000),
                new WorldObjectSnapshot(101, 101, "next", "monster", new Vector3Snapshot(42, 0, 0), 42, 1000, 1000)
            },
            Skills = CreateSkillSnapshotsById(new Dictionary<uint, uint>
            {
                [1] = 0,
                [5] = 0,
                [6] = 0,
                [7] = 0,
                [8] = 0,
                [9] = 0,
                [10] = 0
            })
        };
        var semiAuto = new SemiAutoCombatController(keyboard);
        var controller = new StationaryCombatController(keyboard, semiAuto);
        var plan = SemiAutoSkillPlan.FromSettings(settings.Skills);
        var state = new StationaryCombatState();
        state.MarkCandidate(100, DateTimeOffset.Now - TimeSpan.FromMinutes(2));
        var semiAutoState = new SemiAutoCombatState();
        var context = CreateContext(settings, gameApi, logger);

        await controller.TickAsync(context, plan, semiAutoState, state).ConfigureAwait(false);

        AssertFalse(!state.IsTargetIgnored(100), "unlocked timed-out target should be ignored");
        AssertEqual((ushort)0, state.CandidateEntityId, "timed-out target should clear current candidate");
        AssertFalse(!logger.Entries.Any(entry =>
            entry.EventName == "stationary_combat.target.ignored" &&
            string.Equals(Convert.ToString(entry.Fields["reason"]), "not_locked", StringComparison.Ordinal)),
            "lock timeout should be logged");

        await controller.TickAsync(context, plan, semiAutoState, state).ConfigureAwait(false);

        AssertEqual((ushort)101, state.CandidateEntityId, "next tick should select a non-ignored target");
    }
    finally
    {
        Environment.SetEnvironmentVariable("AION_FACE_TARGET_BEARING_MODE", previousBearingMode);
    }
}

static async Task TestStationaryCombatIgnoresTargetWhenKillTimesOutAsync()
{
    var settings = CreateScriptSettings();
    settings.MainMode = AccountMainMode.CustomCombat;
    settings.CombatMode = AccountCombatMode.Stationary;
    settings.Combat = new CombatScriptSettings
    {
        HasStationaryCombatPosition = true,
        StationaryCombatX = 0,
        StationaryCombatY = 0,
        StationaryCombatZ = 0,
        StationaryCombatRadius = 60
    };

    var keyboard = new RecordingKeyboardInput();
    var logger = new InMemoryRoadhogLogger();
    var gameApi = new FakeGameApi
    {
        Player = new PlayerSnapshot(1, 100, "Fake", 100, 100, 100, 100, 0, new Vector3Snapshot(0, 0, 0), DateTimeOffset.Now, 90, 10, 90),
        TargetEntityId = 100,
        TargetCurrentHp = 1000,
        TargetMaxHp = 1000,
        TargetPosition = new Vector3Snapshot(40, 0, 0),
        WorldObjects = new[]
        {
            new WorldObjectSnapshot(100, 100, "slow", "monster", new Vector3Snapshot(40, 0, 0), 40, 1000, 1000)
        },
        Skills = CreateSkillSnapshotsById(new Dictionary<uint, uint>
        {
            [1] = 0,
            [5] = 0,
            [6] = 0,
            [7] = 0,
            [8] = 0,
            [9] = 0,
            [10] = 0
        })
    };
    var semiAuto = new SemiAutoCombatController(keyboard);
    var controller = new StationaryCombatController(keyboard, semiAuto);
    var plan = SemiAutoSkillPlan.FromSettings(settings.Skills);
    var state = new StationaryCombatState
    {
        Fighting = true,
        CurrentTargetEntityId = 100
    };
    state.MarkCandidate(100, DateTimeOffset.Now - TimeSpan.FromMinutes(2));
    var semiAutoState = new SemiAutoCombatState();

    await controller.TickAsync(CreateContext(settings, gameApi, logger), plan, semiAutoState, state).ConfigureAwait(false);

    AssertFalse(state.Fighting, "alive timed-out fight should clear fighting state");
    AssertFalse(!state.IsTargetIgnored(100), "alive timed-out fight target should be ignored");
    AssertFalse(keyboard.Keys.Contains("D2"), "timed-out fight should not continue releasing skills");
    AssertFalse(!logger.Entries.Any(entry =>
        entry.EventName == "stationary_combat.target.ignored" &&
        string.Equals(Convert.ToString(entry.Fields["reason"]), "not_dead", StringComparison.Ordinal)),
        "kill timeout should be logged");
}

static async Task TestStationaryCombatKeepsCurrentFightTargetWhenLockSwitchesAsync()
{
    var previousTabDelay = Environment.GetEnvironmentVariable("ROADHOG_STATIONARY_TAB_VERIFY_DELAY_MS");
    Environment.SetEnvironmentVariable("ROADHOG_STATIONARY_TAB_VERIFY_DELAY_MS", "0");
    try
    {
        var settings = CreateScriptSettings();
        settings.MainMode = AccountMainMode.CustomCombat;
        settings.CombatMode = AccountCombatMode.Stationary;
        settings.Combat = new CombatScriptSettings
        {
            HasStationaryCombatPosition = true,
            StationaryCombatX = 0,
            StationaryCombatY = 0,
            StationaryCombatZ = 0,
            StationaryCombatRadius = 60
        };

        var keyboard = new RecordingKeyboardInput();
        var logger = new InMemoryRoadhogLogger();
        var gameApi = new FakeGameApi
        {
            Player = new PlayerSnapshot(1, 200, "Fake", 100, 100, 100, 100, 0, new Vector3Snapshot(0, 0, 0), DateTimeOffset.Now, 90, 10, 90),
            TargetEntityId = 200,
            TargetCurrentHp = 1000,
            TargetMaxHp = 1000,
            TargetPosition = new Vector3Snapshot(8, 0, 0),
            WorldObjects = new[]
            {
                new WorldObjectSnapshot(100, 100, "original", "monster", new Vector3Snapshot(40, 0, 0), 40, 1000, 1000),
                new WorldObjectSnapshot(200, 200, "nearby", "monster", new Vector3Snapshot(8, 0, 0), 8, 1000, 1000)
            },
            Skills = CreateSkillSnapshotsById(new Dictionary<uint, uint>
            {
                [1] = 0,
                [5] = 0,
                [6] = 0,
                [7] = 0,
                [8] = 0,
                [9] = 0,
                [10] = 0
            })
        };
        var semiAuto = new SemiAutoCombatController(keyboard);
        var controller = new StationaryCombatController(keyboard, semiAuto);
        var plan = SemiAutoSkillPlan.FromSettings(settings.Skills);
        var state = new StationaryCombatState
        {
            Fighting = true,
            CurrentTargetEntityId = 100,
            CandidateEntityId = 100
        };
        state.MarkCandidate(100, DateTimeOffset.Now);

        await controller
            .TickAsync(CreateContext(settings, gameApi, logger), plan, new SemiAutoCombatState(), state)
            .ConfigureAwait(false);

        AssertFalse(!state.Fighting, "lock mismatch should keep fight state for reacquire");
        AssertEqual((ushort)100, state.CurrentTargetEntityId, "lock mismatch should keep current fight target");
        AssertEqual((ushort)100, state.CandidateEntityId, "lock mismatch should keep original candidate");
        AssertFalse(!keyboard.Keys.Contains("Tab"), "lock mismatch should try to reacquire the original target");
        AssertFalse(keyboard.Keys.Contains("D2"), "wrong locked target must not enter skill release");
        AssertFalse(!logger.Entries.Any(entry =>
            entry.EventName == "stationary_combat.target.reacquire" &&
            Equals(Convert.ToUInt16(entry.Fields["targetEntityId"]), (ushort)100)),
            "lock mismatch should log original target reacquire");
    }
    finally
    {
        Environment.SetEnvironmentVariable("ROADHOG_STATIONARY_TAB_VERIFY_DELAY_MS", previousTabDelay);
    }
}

static async Task TestStationaryCombatPressesCUntilLockedTargetTargetsPlayerAsync()
{
    var settings = CreateScriptSettings();
    settings.MainMode = AccountMainMode.CustomCombat;
    settings.CombatMode = AccountCombatMode.Stationary;
    settings.Combat = new CombatScriptSettings
    {
        HasStationaryCombatPosition = true,
        StationaryCombatX = 0,
        StationaryCombatY = 0,
        StationaryCombatZ = 0,
        StationaryCombatRadius = 60
    };
    settings.SemiAuto.AttackKeyLoopEnabled = true;
    settings.SemiAuto.AttackKeyLoopIntervalMs = 1;

    var keyboard = new RecordingKeyboardInput();
    var logger = new InMemoryRoadhogLogger();
    var gameApi = new FakeGameApi
    {
        Player = new PlayerSnapshot(1, 100, "Fake", 100, 100, 100, 100, 0, new Vector3Snapshot(0, 0, 0), DateTimeOffset.Now, 90, 10, 90),
        TargetEntityId = 100,
        TargetCurrentHp = 1000,
        TargetMaxHp = 1000,
        TargetPosition = new Vector3Snapshot(8, 0, 0),
        TargetServerObjectId = 0,
        TargetIsTargetingLocalPlayer = false,
        WorldObjects = new[]
        {
            new WorldObjectSnapshot(100, 100, "target", "monster", new Vector3Snapshot(8, 0, 0), 8, 1000, 1000)
        },
        Skills = CreateSkillSnapshotsById(new Dictionary<uint, uint>
        {
            [1] = 0,
            [5] = 0,
            [6] = 0,
            [7] = 0,
            [8] = 0,
            [9] = 0,
            [10] = 0
        })
    };
    var semiAuto = new SemiAutoCombatController(keyboard);
    var controller = new StationaryCombatController(keyboard, semiAuto);
    var plan = SemiAutoSkillPlan.FromSettings(settings.Skills);
    var state = new StationaryCombatState
    {
        Fighting = true,
        CurrentTargetEntityId = 100
    };
    state.MarkCandidate(100, DateTimeOffset.Now);
    var semiAutoState = new SemiAutoCombatState();
    var context = CreateContext(settings, gameApi, logger);

    await controller.TickAsync(context, plan, semiAutoState, state).ConfigureAwait(false);

    AssertSequence(new[] { "C" }, keyboard.Keys.ToArray(), "unclaimed locked target should only press C before it targets player");
    AssertFalse(keyboard.Keys.Contains("D2"), "unclaimed locked target must not release skills before targeting player");
    AssertFalse(!logger.Entries.Any(entry => entry.EventName == "stationary_combat.opening_attack.wait_targeting"), "opening attack wait should be logged");

    gameApi.TargetServerObjectId = 1;
    gameApi.TargetIsTargetingLocalPlayer = true;
    await Task.Delay(2).ConfigureAwait(false);
    await controller.TickAsync(context, plan, semiAutoState, state).ConfigureAwait(false);

    AssertEqual(1, keyboard.Keys.Count(key => key == "C"), "C should stop after the locked target targets player");
    AssertFalse(!keyboard.Keys.Contains("D2"), "targeting player should enter skill release");
}

static async Task TestStationaryCombatSwitchesAwayFromTargetClaimedByOtherAsync()
{
    var settings = CreateScriptSettings();
    settings.MainMode = AccountMainMode.CustomCombat;
    settings.CombatMode = AccountCombatMode.Stationary;
    settings.Combat = new CombatScriptSettings
    {
        HasStationaryCombatPosition = true,
        StationaryCombatX = 0,
        StationaryCombatY = 0,
        StationaryCombatZ = 0,
        StationaryCombatRadius = 60
    };

    var keyboard = new RecordingKeyboardInput();
    var logger = new InMemoryRoadhogLogger();
    var gameApi = new FakeGameApi
    {
        Player = new PlayerSnapshot(1, 100, "Fake", 100, 100, 100, 100, 0, new Vector3Snapshot(0, 0, 0), DateTimeOffset.Now, 90, 10, 90),
        TargetEntityId = 100,
        TargetCurrentHp = 1000,
        TargetMaxHp = 1000,
        TargetPosition = new Vector3Snapshot(8, 0, 0),
        TargetServerObjectId = 999,
        TargetIsTargetingLocalPlayer = false,
        WorldObjects = new[]
        {
            new WorldObjectSnapshot(100, 100, "claimed", "monster", new Vector3Snapshot(8, 0, 0), 8, 1000, 1000, 999, false),
            new WorldObjectSnapshot(101, 101, "next", "monster", new Vector3Snapshot(12, 0, 0), 12, 1000, 1000)
        },
        Skills = CreateSkillSnapshotsById(new Dictionary<uint, uint>
        {
            [1] = 0,
            [5] = 0,
            [6] = 0,
            [7] = 0,
            [8] = 0,
            [9] = 0,
            [10] = 0
        })
    };
    var semiAuto = new SemiAutoCombatController(keyboard);
    var controller = new StationaryCombatController(keyboard, semiAuto);
    var plan = SemiAutoSkillPlan.FromSettings(settings.Skills);
    var state = new StationaryCombatState
    {
        Fighting = true,
        CurrentTargetEntityId = 100
    };
    state.MarkCandidate(100, DateTimeOffset.Now);
    var semiAutoState = new SemiAutoCombatState();
    var context = CreateContext(settings, gameApi, logger);

    await controller.TickAsync(context, plan, semiAutoState, state).ConfigureAwait(false);

    AssertFalse(state.Fighting, "claimed locked target should clear current fight state");
    AssertFalse(!state.IsTargetIgnored(100), "claimed locked target should be ignored");
    AssertFalse(keyboard.Keys.Contains("D2"), "claimed locked target must not release skills");
    AssertFalse(!logger.Entries.Any(entry =>
        entry.EventName == "stationary_combat.target.ignored" &&
        string.Equals(Convert.ToString(entry.Fields["reason"]), "target_owned_by_other", StringComparison.Ordinal)),
        "claimed target ignore should be logged");

    gameApi.TargetEntityId = 0;
    gameApi.TargetServerObjectId = 0;
    gameApi.TargetIsTargetingLocalPlayer = false;
    await controller.TickAsync(context, plan, semiAutoState, state).ConfigureAwait(false);

    AssertEqual((ushort)101, state.CandidateEntityId, "next tick should switch to the next unclaimed target");
}

static async Task TestStationaryCombatTreatsLockedZeroHpTargetAsCombatAsync()
{
    var settings = CreateScriptSettings();
    settings.MainMode = AccountMainMode.CustomCombat;
    settings.CombatMode = AccountCombatMode.Stationary;
    settings.Combat = new CombatScriptSettings
    {
        HasStationaryCombatPosition = true,
        StationaryCombatX = 0,
        StationaryCombatY = 0,
        StationaryCombatZ = 0,
        StationaryCombatRadius = 60
    };

    var keyboard = new RecordingKeyboardInput();
    var logger = new InMemoryRoadhogLogger();
    var gameApi = new FakeGameApi
    {
        Player = new PlayerSnapshot(1, 100, "Fake", 100, 100, 100, 100, 0, new Vector3Snapshot(0, 0, 0), DateTimeOffset.Now, 90, 10, 90),
        TargetEntityId = 100,
        TargetCurrentHp = 0,
        TargetMaxHp = 0,
        TargetPosition = new Vector3Snapshot(40, 0, 0),
        WorldObjects = new[]
        {
            new WorldObjectSnapshot(100, 100, "target", "monster", new Vector3Snapshot(40, 0, 0), 40, 1000, 1000)
        },
        Skills = CreateSkillSnapshotsById(new Dictionary<uint, uint>
        {
            [1] = 0,
            [5] = 0,
            [6] = 0,
            [7] = 0,
            [8] = 0,
            [9] = 0,
            [10] = 0
        })
    };
    var semiAuto = new SemiAutoCombatController(keyboard);
    var controller = new StationaryCombatController(keyboard, semiAuto);
    var plan = SemiAutoSkillPlan.FromSettings(settings.Skills);
    var state = new StationaryCombatState();
    var semiAutoState = new SemiAutoCombatState();

    await controller
        .TickAsync(CreateContext(settings, gameApi, logger), plan, semiAutoState, state)
        .ConfigureAwait(false);

    AssertFalse(!state.Fighting, "locked zero-hp monster snapshot should enter fighting state");
    AssertFalse(keyboard.KeyDowns.Contains("W"), "locked target should not start W movement");
    AssertFalse(keyboard.KeyUps.Contains("W"), "locked target should not pulse W before combat");
    AssertFalse(!keyboard.Keys.Contains("D2"), "locked zero-hp monster snapshot should enter skill logic");
}

static async Task TestStationaryCombatLootsLockedDeadTargetDirectlyAsync()
{
    var previousAfterKillWait = Environment.GetEnvironmentVariable("ROADHOG_LOOT_AFTER_KILL_WAIT_MS");
    var previousWait = Environment.GetEnvironmentVariable("ROADHOG_LOOT_AFTER_PICK_WAIT_MS");
    var previousPressCount = Environment.GetEnvironmentVariable("ROADHOG_LOOT_PRESS_COUNT");
    var previousPressInterval = Environment.GetEnvironmentVariable("ROADHOG_LOOT_PRESS_INTERVAL_MS");
    Environment.SetEnvironmentVariable("ROADHOG_LOOT_AFTER_KILL_WAIT_MS", "0");
    Environment.SetEnvironmentVariable("ROADHOG_LOOT_AFTER_PICK_WAIT_MS", "0");
    Environment.SetEnvironmentVariable("ROADHOG_LOOT_PRESS_COUNT", null);
    Environment.SetEnvironmentVariable("ROADHOG_LOOT_PRESS_INTERVAL_MS", "0");
    try
    {
        var settings = CreateScriptSettings();
        settings.MainMode = AccountMainMode.CustomCombat;
        settings.CombatMode = AccountCombatMode.Stationary;
        settings.Combat = new CombatScriptSettings
        {
            EnableLoot = true,
            HasStationaryCombatPosition = true,
            StationaryCombatX = 0,
            StationaryCombatY = 0,
            StationaryCombatZ = 0,
            StationaryCombatRadius = 60
        };

        var keyboard = new RecordingKeyboardInput();
        var logger = new InMemoryRoadhogLogger();
        var runtimeStates = new AccountRuntimeManager(logger);
        runtimeStates.GetOrCreate("account1");
        var gameApi = new FakeGameApi
        {
            Player = new PlayerSnapshot(1, 100, "Fake", 100, 100, 100, 100, 0, new Vector3Snapshot(0, 0, 0), DateTimeOffset.Now, 90, 10, 90),
            TargetEntityId = 100,
            TargetCurrentHp = 0,
            TargetMaxHp = 4430,
            TargetPosition = new Vector3Snapshot(2.5f, 0, 0),
            Skills = CreateSkillSnapshotsById(new Dictionary<uint, uint>
            {
                [1] = 0,
                [5] = 0,
                [6] = 0,
                [7] = 0,
                [8] = 0,
                [9] = 0,
                [10] = 0
            })
        };
        var semiAuto = new SemiAutoCombatController(keyboard);
        var controller = new StationaryCombatController(keyboard, semiAuto);
        var plan = SemiAutoSkillPlan.FromSettings(settings.Skills);
        var state = new StationaryCombatState
        {
            Fighting = true,
            CurrentTargetEntityId = 100,
            CandidateEntityId = 100
        };

        await controller
            .TickAsync(CreateContext(settings, gameApi, logger, runtimeStates), plan, new SemiAutoCombatState(), state)
            .ConfigureAwait(false);

        AssertSequence(new[] { "NumPadDecimal" }, keyboard.Keys, "post-kill loot key sequence");
        AssertEqual(1, runtimeStates.Snapshot().First().KillCount, "stationary dead target should count as kill");
        AssertFalse(!logger.Entries.Any(entry => entry.EventName == "stationary_combat.target.kill_counted"), "stationary kill should be logged");
        AssertEqual(0, gameApi.LootReadCount, "locked dead target loot should not scan corpse list");
        AssertFalse(state.LootAfterKill.Active, "loot state should finish in one zero-wait test tick");
        AssertEqual((ushort)0, state.CurrentTargetEntityId, "combat target should clear after loot");
    }
    finally
    {
        Environment.SetEnvironmentVariable("ROADHOG_LOOT_AFTER_KILL_WAIT_MS", previousAfterKillWait);
        Environment.SetEnvironmentVariable("ROADHOG_LOOT_AFTER_PICK_WAIT_MS", previousWait);
        Environment.SetEnvironmentVariable("ROADHOG_LOOT_PRESS_COUNT", previousPressCount);
        Environment.SetEnvironmentVariable("ROADHOG_LOOT_PRESS_INTERVAL_MS", previousPressInterval);
    }
}

static async Task TestStationaryCombatWaitsAfterKillBeforeLootKeyAsync()
{
    var previousAfterKillWait = Environment.GetEnvironmentVariable("ROADHOG_LOOT_AFTER_KILL_WAIT_MS");
    Environment.SetEnvironmentVariable("ROADHOG_LOOT_AFTER_KILL_WAIT_MS", "1000");
    try
    {
        var settings = CreateScriptSettings();
        settings.MainMode = AccountMainMode.CustomCombat;
        settings.CombatMode = AccountCombatMode.Stationary;
        settings.Combat = new CombatScriptSettings
        {
            EnableLoot = true,
            HasStationaryCombatPosition = true,
            StationaryCombatX = 0,
            StationaryCombatY = 0,
            StationaryCombatZ = 0,
            StationaryCombatRadius = 60
        };

        var gameApi = new FakeGameApi
        {
            Player = new PlayerSnapshot(1, 100, "Fake", 100, 100, 100, 100, 0, new Vector3Snapshot(1, 0, 0), DateTimeOffset.Now, 90, 10, 90),
            TargetEntityId = 100,
            TargetCurrentHp = 0,
            TargetMaxHp = 4430,
            TargetPosition = new Vector3Snapshot(1, 0, 0),
            Skills = CreateSkillSnapshotsById(new Dictionary<uint, uint>())
        };
        var keyboard = new RecordingKeyboardInput();
        var controller = new StationaryCombatController(keyboard, new SemiAutoCombatController(keyboard));
        var plan = SemiAutoSkillPlan.FromSettings(settings.Skills);
        var state = new StationaryCombatState
        {
            Fighting = true,
            CurrentTargetEntityId = 100,
            CandidateEntityId = 100
        };

        await controller
            .TickAsync(CreateContext(settings, gameApi, new InMemoryRoadhogLogger()), plan, new SemiAutoCombatState(), state)
            .ConfigureAwait(false);

        AssertSequence(Array.Empty<string>(), keyboard.Keys, "loot key should wait after kill");
        AssertEqual(0, gameApi.LootReadCount, "locked dead target loot should not scan corpse list");
        AssertEqual(StationaryCombatLootAfterKillStep.WaitAfterKill, state.LootAfterKill.Step, "loot should be waiting after kill");
    }
    finally
    {
        Environment.SetEnvironmentVariable("ROADHOG_LOOT_AFTER_KILL_WAIT_MS", previousAfterKillWait);
    }
}

static async Task TestStationaryCombatWaitsNearCorpseAfterLootKeyAsync()
{
    var previousAfterKillWait = Environment.GetEnvironmentVariable("ROADHOG_LOOT_AFTER_KILL_WAIT_MS");
    var previousPressCount = Environment.GetEnvironmentVariable("ROADHOG_LOOT_PRESS_COUNT");
    var previousPressInterval = Environment.GetEnvironmentVariable("ROADHOG_LOOT_PRESS_INTERVAL_MS");
    var previousApproachTimeout = Environment.GetEnvironmentVariable("ROADHOG_LOOT_APPROACH_TIMEOUT_MS");
    Environment.SetEnvironmentVariable("ROADHOG_LOOT_AFTER_KILL_WAIT_MS", "0");
    Environment.SetEnvironmentVariable("ROADHOG_LOOT_PRESS_COUNT", null);
    Environment.SetEnvironmentVariable("ROADHOG_LOOT_PRESS_INTERVAL_MS", "0");
    Environment.SetEnvironmentVariable("ROADHOG_LOOT_APPROACH_TIMEOUT_MS", "5000");
    try
    {
        var settings = CreateScriptSettings();
        settings.MainMode = AccountMainMode.CustomCombat;
        settings.CombatMode = AccountCombatMode.Stationary;
        settings.Combat = new CombatScriptSettings
        {
            EnableLoot = true,
            HasStationaryCombatPosition = true,
            StationaryCombatX = 0,
            StationaryCombatY = 0,
            StationaryCombatZ = 0,
            StationaryCombatRadius = 60
        };

        var gameApi = new FakeGameApi
        {
            Player = new PlayerSnapshot(1, 100, "Fake", 100, 100, 100, 100, 0, new Vector3Snapshot(0, 0, 0), DateTimeOffset.Now, 90, 10, 90),
            TargetEntityId = 100,
            TargetCurrentHp = 0,
            TargetMaxHp = 4430,
            TargetPosition = new Vector3Snapshot(10, 0, 0),
            Skills = CreateSkillSnapshotsById(new Dictionary<uint, uint>())
        };
        var keyboard = new RecordingKeyboardInput();
        var controller = new StationaryCombatController(keyboard, new SemiAutoCombatController(keyboard));
        var plan = SemiAutoSkillPlan.FromSettings(settings.Skills);
        var state = new StationaryCombatState
        {
            Fighting = true,
            CurrentTargetEntityId = 100,
            CandidateEntityId = 100
        };

        await controller
            .TickAsync(CreateContext(settings, gameApi, new InMemoryRoadhogLogger()), plan, new SemiAutoCombatState(), state)
            .ConfigureAwait(false);

        AssertSequence(new[] { "NumPadDecimal" }, keyboard.Keys, "loot key should be pressed before approach wait");
        AssertEqual(StationaryCombatLootAfterKillStep.WaitNearCorpse, state.LootAfterKill.Step, "loot should wait until player is near corpse");
        AssertFalse(!state.LootAfterKill.Active, "loot state should stay active while approaching corpse");
    }
    finally
    {
        Environment.SetEnvironmentVariable("ROADHOG_LOOT_AFTER_KILL_WAIT_MS", previousAfterKillWait);
        Environment.SetEnvironmentVariable("ROADHOG_LOOT_PRESS_COUNT", previousPressCount);
        Environment.SetEnvironmentVariable("ROADHOG_LOOT_PRESS_INTERVAL_MS", previousPressInterval);
        Environment.SetEnvironmentVariable("ROADHOG_LOOT_APPROACH_TIMEOUT_MS", previousApproachTimeout);
    }
}

static async Task TestStationaryCombatFinishesFightBeforeReturningHomeAsync()
{
    var settings = CreateScriptSettings();
    settings.MainMode = AccountMainMode.CustomCombat;
    settings.CombatMode = AccountCombatMode.Stationary;
    settings.Combat = new CombatScriptSettings
    {
        HasStationaryCombatPosition = true,
        StationaryCombatX = 0,
        StationaryCombatY = 0,
        StationaryCombatZ = 0,
        StationaryCombatRadius = 30
    };

    var keyboard = new RecordingKeyboardInput();
    var logger = new InMemoryRoadhogLogger();
    var gameApi = new FakeGameApi
    {
        Player = new PlayerSnapshot(1, 100, "Fake", 100, 100, 100, 100, 0, new Vector3Snapshot(50, 0, 0), DateTimeOffset.Now, 0, 10, 0),
        TargetEntityId = 100,
        TargetCurrentHp = 1000,
        TargetPosition = new Vector3Snapshot(10, 0, 0),
        Skills = CreateSkillSnapshotsById(new Dictionary<uint, uint>
        {
            [1] = 0,
            [5] = 0,
            [6] = 0,
            [7] = 0,
            [8] = 0,
            [9] = 0,
            [10] = 0
        })
    };
    var semiAuto = new SemiAutoCombatController(keyboard);
    var controller = new StationaryCombatController(keyboard, semiAuto);
    var plan = SemiAutoSkillPlan.FromSettings(settings.Skills);
    var state = new StationaryCombatState
    {
        Fighting = true,
        CurrentTargetEntityId = 100,
        CandidateEntityId = 100
    };

    await controller.TickAsync(CreateContext(settings, gameApi, logger), plan, new SemiAutoCombatState(), state).ConfigureAwait(false);

    AssertFalse(keyboard.Keys.Contains("W"), "fighting outside radius should not return home before target dies");
    AssertFalse(!keyboard.Keys.Contains("D2"), "fighting outside radius should keep releasing skills");
}

static async Task TestStationaryCombatInterruptsSitWhenTargetedAsync()
{
    var settings = CreateScriptSettings();
    settings.MainMode = AccountMainMode.CustomCombat;
    settings.CombatMode = AccountCombatMode.Stationary;
    settings.Combat = new CombatScriptSettings
    {
        HasStationaryCombatPosition = true,
        StationaryCombatX = 0,
        StationaryCombatY = 0,
        StationaryCombatZ = 0,
        StationaryCombatRadius = 30
    };
    settings.Maintenance.SitMaintenanceEnabled = true;
    settings.Maintenance.SitHpBelowPercent = 25;
    settings.Maintenance.SitHpRecoverToPercent = 75;

    var keyboard = new RecordingKeyboardInput();
    var logger = new InMemoryRoadhogLogger();
    var gameApi = new FakeGameApi
    {
        Player = new PlayerSnapshot(1, 100, "Fake", 20, 100, 100, 100, 0, new Vector3Snapshot(0, 0, 0), DateTimeOffset.Now, 0, 10, 0),
        TargetEntityId = 0,
        TargetCurrentHp = 0,
        TargetPosition = null,
        Skills = CreateSkillSnapshotsById(new Dictionary<uint, uint>
        {
            [1] = 0,
            [5] = 0,
            [6] = 0
        }),
        WorldObjects = new[]
        {
            new WorldObjectSnapshot(
                200,
                2000,
                "attacker",
                "monster",
                new Vector3Snapshot(5, 0, 0),
                5,
                1000,
                1000,
                TargetServerObjectId: 100,
                IsTargetingLocalPlayer: true)
        }
    };
    var semiAuto = new SemiAutoCombatController(keyboard);
    var controller = new StationaryCombatController(keyboard, semiAuto);
    var plan = SemiAutoSkillPlan.FromSettings(settings.Skills);
    var stationaryState = new StationaryCombatState();
    var semiAutoState = new SemiAutoCombatState();
    semiAutoState.StartMaintenanceRest(forHp: true, forMp: false);

    await controller
        .TickAsync(CreateContext(settings, gameApi, logger), plan, semiAutoState, stationaryState)
        .ConfigureAwait(false);

    AssertFalse(!keyboard.Keys.Contains("X"), "targeting monster should interrupt sit maintenance with x");
    AssertFalse(keyboard.Keys.Contains("OemComma"), "targeting monster should block re-entering sit maintenance");
    AssertFalse(semiAutoState.IsMaintenanceResting, "interrupted sit maintenance should clear rest state");
    AssertEqual((ushort)200, stationaryState.CandidateEntityId, "targeting monster should become the combat candidate");
}

static async Task TestStationaryCombatHpRuleRunsBeforeDefenseTargetWorkflowAsync()
{
    var settings = CreateScriptSettings();
    settings.MainMode = AccountMainMode.CustomCombat;
    settings.CombatMode = AccountCombatMode.Stationary;
    settings.Combat = new CombatScriptSettings
    {
        HasStationaryCombatPosition = true,
        StationaryCombatX = 0,
        StationaryCombatY = 0,
        StationaryCombatZ = 0,
        StationaryCombatRadius = 30
    };
    settings.Maintenance.SitMaintenanceEnabled = true;
    settings.Maintenance.HpMaintenanceRules.Add(new MaintenanceKeyRuleConfig
    {
        BelowPercent = 50,
        Key = "D8",
    });

    var keyboard = new RecordingKeyboardInput();
    var logger = new InMemoryRoadhogLogger();
    var gameApi = new FakeGameApi
    {
        Player = new PlayerSnapshot(1, 100, "Fake", 40, 100, 100, 100, 0, new Vector3Snapshot(0, 0, 0), DateTimeOffset.Now, 0, 10, 0),
        TargetEntityId = 0,
        TargetCurrentHp = 0,
        TargetPosition = null,
        Skills = CreateSkillSnapshotsById(new Dictionary<uint, uint>
        {
            [1] = 0,
            [5] = 0,
            [6] = 0
        }),
        WorldObjects = new[]
        {
            new WorldObjectSnapshot(
                201,
                2001,
                "attacker",
                "monster",
                new Vector3Snapshot(5, 0, 0),
                5,
                1000,
                1000,
                TargetServerObjectId: 100,
                IsTargetingLocalPlayer: true)
        }
    };
    var semiAuto = new SemiAutoCombatController(keyboard);
    var controller = new StationaryCombatController(keyboard, semiAuto);
    var plan = SemiAutoSkillPlan.FromSettings(settings.Skills);
    var stationaryState = new StationaryCombatState();
    var semiAutoState = new SemiAutoCombatState();
    CalibrateCooldownClock(semiAutoState);
    keyboard.AfterPress = key =>
    {
        if (string.Equals(key, "D8", StringComparison.Ordinal))
        {
            gameApi.Skills = CreateSkillSnapshotsById(new Dictionary<uint, uint>
            {
                [8] = ActiveCooldownEnd(),
                [5] = 0,
                [6] = 0
            });
        }
    };

    await controller
        .TickAsync(CreateContext(settings, gameApi, logger), plan, semiAutoState, stationaryState)
        .ConfigureAwait(false);

    AssertSequence(new[] { "D8" }, keyboard.Keys.ToArray(), "stationary hp maintenance key should run before target workflow");
    AssertFalse(keyboard.Keys.Contains("OemComma"), "stationary hp key maintenance must not enter sit maintenance");
    AssertEqual((ushort)0, stationaryState.CandidateEntityId, "target workflow should wait until maintenance key tick finishes");
}

static async Task TestStationaryCombatStopsMovementBeforeHpMaintenanceAsync()
{
    var settings = CreateScriptSettings();
    settings.MainMode = AccountMainMode.CustomCombat;
    settings.CombatMode = AccountCombatMode.Stationary;
    settings.Combat = new CombatScriptSettings
    {
        HasStationaryCombatPosition = true,
        StationaryCombatX = 0,
        StationaryCombatY = 0,
        StationaryCombatZ = 0,
        StationaryCombatRadius = 30
    };
    settings.Maintenance.SitMaintenanceEnabled = false;
    settings.Maintenance.HpMaintenanceRules.Add(new MaintenanceKeyRuleConfig
    {
        BelowPercent = 50,
        Key = "D8",
    });

    var keyboard = new RecordingKeyboardInput();
    var logger = new InMemoryRoadhogLogger();
    var gameApi = new FakeGameApi
    {
        Player = new PlayerSnapshot(1, 100, "Fake", 40, 100, 100, 100, 0, new Vector3Snapshot(0, 0, 0), DateTimeOffset.Now, 0, 10, 0),
        Skills = CreateSkillSnapshotsById(new Dictionary<uint, uint>
        {
            [1] = 0,
            [5] = 0,
            [6] = 0
        }),
        WorldObjects = Array.Empty<WorldObjectSnapshot>()
    };
    var semiAuto = new SemiAutoCombatController(keyboard);
    var controller = new StationaryCombatController(keyboard, semiAuto);
    var plan = SemiAutoSkillPlan.FromSettings(settings.Skills);
    var stationaryState = new StationaryCombatState
    {
        IsMovingForward = true,
        IsRightMouseDown = true
    };
    var semiAutoState = new SemiAutoCombatState();
    CalibrateCooldownClock(semiAutoState);
    keyboard.AfterPress = key =>
    {
        if (string.Equals(key, "D8", StringComparison.Ordinal))
        {
            gameApi.Skills = CreateSkillSnapshotsById(new Dictionary<uint, uint>
            {
                [8] = ActiveCooldownEnd(),
                [5] = 0,
                [6] = 0
            });
        }
    };

    await controller
        .TickAsync(CreateContext(settings, gameApi, logger), plan, semiAutoState, stationaryState)
        .ConfigureAwait(false);

    AssertSequence(new[] { "W" }, keyboard.KeyUps.ToArray(), "hp maintenance should release W before pressing key");
    AssertSequence(new[] { "up:Right" }, keyboard.MouseCommands.ToArray(), "hp maintenance should release right mouse before pressing key");
    AssertSequence(new[] { "D8" }, keyboard.Keys.ToArray(), "hp maintenance key");
    AssertFalse(stationaryState.IsMovingForward, "hp maintenance should clear moving state");
    AssertFalse(stationaryState.IsRightMouseDown, "hp maintenance should clear right mouse state");
}

static async Task TestStationaryCombatMpSitMaintenanceRunsWithoutDefenseTargetAsync()
{
    var settings = CreateScriptSettings();
    settings.MainMode = AccountMainMode.CustomCombat;
    settings.CombatMode = AccountCombatMode.Stationary;
    settings.Combat = new CombatScriptSettings
    {
        HasStationaryCombatPosition = true,
        StationaryCombatX = 0,
        StationaryCombatY = 0,
        StationaryCombatZ = 0,
        StationaryCombatRadius = 30
    };
    settings.Maintenance.SitMaintenanceEnabled = true;
    settings.Maintenance.SitHpBelowPercent = 25;
    settings.Maintenance.SitMpBelowPercent = 30;
    settings.Maintenance.SitMpRecoverToPercent = 60;

    var keyboard = new RecordingKeyboardInput();
    var logger = new InMemoryRoadhogLogger();
    var gameApi = new FakeGameApi
    {
        Player = new PlayerSnapshot(1, 0, "Fake", 100, 100, 20, 100, 0, new Vector3Snapshot(0, 0, 0), DateTimeOffset.Now, 0, 10, 0),
        TargetEntityId = 0,
        TargetCurrentHp = 0,
        TargetPosition = null,
        Skills = CreateSkillSnapshotsById(new Dictionary<uint, uint>
        {
            [1] = 0,
            [5] = 0,
            [6] = 0
        }),
        WorldObjects = Array.Empty<WorldObjectSnapshot>()
    };
    var semiAuto = new SemiAutoCombatController(keyboard);
    var controller = new StationaryCombatController(keyboard, semiAuto);
    var plan = SemiAutoSkillPlan.FromSettings(settings.Skills);
    var stationaryState = new StationaryCombatState();
    var semiAutoState = new SemiAutoCombatState();
    CalibrateCooldownClock(semiAutoState);

    await controller
        .TickAsync(CreateContext(settings, gameApi, logger), plan, semiAutoState, stationaryState)
        .ConfigureAwait(false);

    AssertSequence(new[] { "OemComma" }, keyboard.Keys.ToArray(), "low mp should enter sit maintenance with comma");
    AssertFalse(!semiAutoState.IsMaintenanceResting, "low mp sit maintenance should stay active");
    AssertFalse(!semiAutoState.MaintenanceRestingForMp, "stationary mp sit should track mp recovery");
    AssertFalse(semiAutoState.MaintenanceRestingForHp, "full hp should not be tracked for mp sit maintenance");
    AssertEqual((ushort)0, stationaryState.CandidateEntityId, "target workflow should stay idle while mp sit maintenance starts");
}

static async Task TestStationaryCombatSkipsSkillMaintenanceBeforeCooldownCalibrationAsync()
{
    var settings = CreateScriptSettings();
    settings.MainMode = AccountMainMode.CustomCombat;
    settings.CombatMode = AccountCombatMode.Stationary;
    settings.Combat = new CombatScriptSettings
    {
        HasStationaryCombatPosition = true,
        StationaryCombatX = 0,
        StationaryCombatY = 0,
        StationaryCombatZ = 0,
        StationaryCombatRadius = 30
    };
    settings.Maintenance.SitMaintenanceEnabled = false;
    settings.Maintenance.HpMaintenanceRules.Add(new MaintenanceKeyRuleConfig
    {
        BelowPercent = 73,
        Key = "NumPad0",
        SkillId = 506,
        SkillName = "主神之盔甲 I"
    });

    var keyboard = new RecordingKeyboardInput();
    var logger = new InMemoryRoadhogLogger();
    var learnedSkills = CreateSkillSnapshotsById(new Dictionary<uint, uint>
    {
        [1] = 0,
        [5] = 0,
        [6] = 0
    }).Concat(new[]
    {
        new SkillSnapshot(
            506,
            "主神之盔甲 I",
            1,
            1,
            "主神之盔甲 I",
            1,
            false,
            360_000,
            0)
    }).ToArray();
    var gameApi = new FakeGameApi
    {
        Player = new PlayerSnapshot(1, 100, "Fake", 40, 100, 100, 100, 0, new Vector3Snapshot(0, 0, 0), DateTimeOffset.Now, 0, 10, 0),
        TargetEntityId = 100,
        TargetCurrentHp = 1000,
        TargetMaxHp = 1000,
        TargetPosition = new Vector3Snapshot(5, 0, 0),
        Skills = learnedSkills,
        WorldObjects = Array.Empty<WorldObjectSnapshot>()
    };
    var semiAuto = new SemiAutoCombatController(keyboard);
    var controller = new StationaryCombatController(keyboard, semiAuto);
    var plan = SemiAutoSkillPlan.FromSettings(settings.Skills);
    var stationaryState = new StationaryCombatState
    {
        Fighting = true,
        CurrentTargetEntityId = 100
    };
    var semiAutoState = new SemiAutoCombatState();

    await controller
        .TickAsync(CreateContext(settings, gameApi, logger), plan, semiAutoState, stationaryState)
        .ConfigureAwait(false);

    AssertFalse(keyboard.Keys.Contains("NumPad0"), "stationary combat should skip skill maintenance before cooldown calibration");
    AssertFalse(keyboard.Keys.Count == 0, "stationary combat should continue releasing combat skills before cooldown calibration");
}

static Task TestSkillTreeKeyMappingAsync()
{
    var plan = SemiAutoSkillPlan.FromSettings(CreateSkillSettings());

    AssertEqual(10, plan.Roots.Count, "root count");
    AssertSequence(
        new[] { "D1", "D2", "D3", "D4", "D5", "D6", "D7", "D8", "D9", "D0" },
        plan.Roots.Select(root => root.Key).ToArray(),
        "root key order");
    AssertSequence(
        new[] { "惩戒一击 I", "盾牌反击 II", "盾牌猛击 II" },
        plan.TriggerPrefixRoots.Select(root => root.Name).ToArray(),
        "trigger prefix block");

    var violent = plan.Roots.Single(root => root.Name == "猛烈一击 III");
    AssertEqual("D6", violent.Key, "violent root key");
    AssertEqual("D6", violent.Children[0].Key, "chain second key");
    AssertEqual("D6", violent.Children[0].Children[0].Key, "chain third key");

    return Task.CompletedTask;
}

static async Task TestCombatTickPressesPrefixThenReadyRootAsync()
{
    var settings = CreateScriptSettings();
    var plan = SemiAutoSkillPlan.FromSettings(settings.Skills);
    var keyboard = new RecordingKeyboardInput();
    var logger = new InMemoryRoadhogLogger();
    var gameApi = new FakeGameApi
    {
        Skills = CreateSkillSnapshots(new Dictionary<string, uint>
        {
            ["保护之盾 I"] = 1000,
            ["盾牌重击 II"] = 0,
            ["弱化之猛击 II"] = 0,
            ["猛烈一击 III"] = 0,
            ["挑衅猛击 I"] = 0,
            ["闪光斩 I"] = 0,
            ["暗黑之惩戒 II"] = 0
        })
    };
    var controller = new SemiAutoCombatController(keyboard);
    var state = new SemiAutoCombatState();
    CalibrateCooldownClock(state);

    await controller.TickAsync(CreateContext(settings, gameApi, logger), plan, state).ConfigureAwait(false);

    AssertSequence(
        WithPreSkillAttackKey("D2", "D3", "D4", "D5"),
        keyboard.Keys.ToArray(),
        "key press order");
    AssertFalse(keyboard.Keys.Contains("D9"), "late trigger D9 should not be used as prefix");
    AssertFalse(keyboard.Keys.Contains("D0"), "dp skill should not be pressed");
}

static async Task TestCombatTickRequestsOnlyConfiguredSkillIdsAsync()
{
    var settings = CreateScriptSettings();
    var plan = SemiAutoSkillPlan.FromSettings(settings.Skills);
    var keyboard = new RecordingKeyboardInput();
    var logger = new InMemoryRoadhogLogger();
    var gameApi = new FakeGameApi
    {
        Skills = CreateSkillSnapshotsById(new Dictionary<uint, uint>
        {
            [1] = 0,
            [5] = 0,
            [6] = 0,
            [7] = 0,
            [8] = 0,
            [9] = 0,
            [10] = 0,
            [51] = 0,
            [61] = 0,
            [62] = 0
        })
        .Concat(new[] { new SkillSnapshot(999, "unconfigured", 1, 1, "unconfigured", 1, false, 0, 0) })
        .ToArray()
    };
    var controller = new SemiAutoCombatController(keyboard);

    await controller.TickAsync(CreateContext(settings, gameApi, logger), plan, new SemiAutoCombatState()).ConfigureAwait(false);

    AssertSequence(
        new uint[] { 1, 5, 6, 7, 8, 9, 51, 61, 62 },
        gameApi.LastRequestedSkillIds ?? Array.Empty<uint>(),
        "configured skill read ids");
    AssertFalse(gameApi.LastRequestedSkillIds?.Contains(2u) == true, "trigger skill D2 must not be read");
    AssertFalse(gameApi.LastRequestedSkillIds?.Contains(3u) == true, "trigger skill D3 must not be read");
    AssertFalse(gameApi.LastRequestedSkillIds?.Contains(4u) == true, "trigger skill D4 must not be read");
    AssertFalse(gameApi.LastRequestedSkillIds?.Contains(10u) == true, "dp skill must not be read until dp value is supported");
    AssertFalse(gameApi.LastRequestedSkillIds?.Contains(999u) == true, "unconfigured skill must not be read");
}

static Task TestConfiguredRootKeyBoundaryAsync()
{
    var settings = new SkillScriptSettings
    {
        Mode = SkillConfigurationMode.Auto,
        TriggerPrefixMode = "TopContiguousTriggerSkills"
    };

    for (var i = 1; i <= 22; i++)
    {
        settings.ExecutionTree.Add(Node((uint)i, "技能" + i, "主动技能"));
    }

    var plan = SemiAutoSkillPlan.FromSettings(settings);
    AssertSequence(
        new[]
        {
            "D1",
            "D2",
            "D3",
            "D4",
            "D5",
            "D6",
            "D7",
            "D8",
            "D9",
            "D0",
            "OemMinus",
            "OemPlus",
            "NumPad1",
            "NumPad2",
            "NumPad3",
            "NumPad4",
            "NumPad5",
            "NumPad6",
            "NumPad7",
            "NumPad8",
            "NumPad9",
            "NumPad0"
        },
        plan.Roots.Select(root => root.Key).ToArray(),
        "22-key order");

    var tenRootPlan = SemiAutoSkillPlan.FromSettings(CreateSkillSettings());
    AssertEqual("D0", tenRootPlan.Roots.Last().Key, "10th configured root key");
    AssertFalse(tenRootPlan.Roots.Any(root => root.Key is "OemMinus" or "OemPlus" or "NumPad1"), "10 roots must not use keys after D0");

    return Task.CompletedTask;
}

static Task TestObservedConfiguredCooldownAdvanceCalibratesClockAsync()
{
    var state = new SemiAutoCombatState();
    var osTick = 207_728_500u;
    var before = new SkillSnapshot(
        424,
        "保护之盾 I",
        1,
        1,
        "保护之盾",
        1,
        false,
        60_000,
        807_375);
    var after = before with { CooldownEndTime = 1_346_921 };

    var firstUpdated = state.TryUpdateCooldownTickCalibration(
        new[] { before },
        osTick,
        DateTimeOffset.Now,
        out _);
    AssertFalse(firstUpdated, "first observation should not calibrate without a previous EndTick");

    var secondUpdated = state.TryUpdateCooldownTickCalibration(
        new[] { after },
        osTick,
        DateTimeOffset.Now,
        out var calibration);

    AssertFalse(!secondUpdated, "configured skill EndTick advance should calibrate cooldown clock");
    AssertEqual(424u, calibration.SkillId, "calibration skill id");
    AssertEqual(1_286_921u, calibration.CooldownStartTick, "calibration start tick");
    AssertEqual(-206_441_579, calibration.OffsetMs, "calibration offset");
    AssertEqual(-206_441_579, state.CooldownTickOffsetMs, "state calibration offset");
    return Task.CompletedTask;
}

static async Task TestUncalibratedNonzeroCooldownFallsBackToFirstRootAsync()
{
    var settings = CreateScriptSettings();
    var plan = SemiAutoSkillPlan.FromSettings(settings.Skills);
    var keyboard = new RecordingKeyboardInput();
    var logger = new InMemoryRoadhogLogger();
    var gameApi = new FakeGameApi
    {
        Skills = CreateSkillSnapshotsById(new Dictionary<uint, uint>
        {
            [1] = StaleCooldownEnd(),
            [5] = StaleCooldownEnd(),
            [51] = StaleCooldownEnd(),
            [6] = StaleCooldownEnd(),
            [7] = StaleCooldownEnd(),
            [8] = StaleCooldownEnd(),
            [9] = StaleCooldownEnd(),
            [10] = 0
        })
    };
    var controller = new SemiAutoCombatController(keyboard);

    await controller.TickAsync(CreateContext(settings, gameApi, logger), plan, new SemiAutoCombatState()).ConfigureAwait(false);

    AssertSequence(WithPreSkillAttackKey("D2", "D3", "D4", "D1"), keyboard.Keys.ToArray(), "uncalibrated stale cooldown should not block all roots");
}

static async Task TestUncalibratedUnknownCooldownRotatesAfterFailedAttemptAsync()
{
    var settings = CreateScriptSettings();
    var plan = SemiAutoSkillPlan.FromSettings(settings.Skills);
    var keyboard = new RecordingKeyboardInput();
    var logger = new InMemoryRoadhogLogger();
    var gameApi = new FakeGameApi
    {
        Skills = CreateSkillSnapshotsById(new Dictionary<uint, uint>
        {
            [1] = StaleCooldownEnd(),
            [5] = StaleCooldownEnd(),
            [51] = StaleCooldownEnd(),
            [6] = StaleCooldownEnd(),
            [7] = StaleCooldownEnd(),
            [8] = StaleCooldownEnd(),
            [9] = StaleCooldownEnd(),
            [10] = StaleCooldownEnd()
        })
    };
    var controller = new SemiAutoCombatController(keyboard);
    var state = new SemiAutoCombatState();
    var context = CreateContext(settings, gameApi, logger);

    await controller.TickAsync(context, plan, state).ConfigureAwait(false);
    AssertSequence(WithPreSkillAttackKey("D2", "D3", "D4", "D1"), keyboard.Keys.ToArray(), "first unknown root should be tried once");

    keyboard.Keys.Clear();
    await controller.TickAsync(context, plan, state).ConfigureAwait(false);

    AssertSequence(WithPreSkillAttackKey("D2", "D3", "D4", "D5"), keyboard.Keys.ToArray(), "failed unknown root should yield to the next unknown root");
}

static async Task TestCalibratedNonzeroCooldownSkipsCoolingRootsAsync()
{
    var settings = CreateScriptSettings();
    var plan = SemiAutoSkillPlan.FromSettings(settings.Skills);
    var keyboard = new RecordingKeyboardInput();
    var logger = new InMemoryRoadhogLogger();
    var gameApi = new FakeGameApi
    {
        Skills = CreateSkillSnapshotsById(new Dictionary<uint, uint>
        {
            [1] = ActiveCooldownEnd(),
            [5] = 0,
            [51] = 0,
            [6] = 0,
            [7] = 0,
            [8] = 0,
            [9] = 0,
            [10] = 0
        })
    };
    var controller = new SemiAutoCombatController(keyboard);
    var state = new SemiAutoCombatState();
    CalibrateCooldownClock(state);

    await controller.TickAsync(CreateContext(settings, gameApi, logger), plan, state).ConfigureAwait(false);

    AssertSequence(WithPreSkillAttackKey("D2", "D3", "D4", "D5"), keyboard.Keys.ToArray(), "calibrated active cooldown should skip D1");
}

static async Task TestCalibratedCooldownToleranceTreatsNearReadyAsReadyAsync()
{
    var settings = CreateScriptSettings();
    var plan = SemiAutoSkillPlan.FromSettings(settings.Skills);
    var keyboard = new RecordingKeyboardInput();
    var logger = new InMemoryRoadhogLogger();
    var gameApi = new FakeGameApi
    {
        Skills = CreateSkillSnapshotsById(new Dictionary<uint, uint>
        {
            [1] = CooldownEndIn(SemiAutoSkillReleasePriority.CooldownReadyToleranceMs),
            [5] = 0,
            [51] = 0,
            [6] = 0,
            [7] = 0,
            [8] = 0,
            [9] = 0,
            [10] = 0
        })
    };
    var controller = new SemiAutoCombatController(keyboard);
    var state = new SemiAutoCombatState();
    CalibrateCooldownClock(state);

    await controller.TickAsync(CreateContext(settings, gameApi, logger), plan, state).ConfigureAwait(false);

    AssertSequence(WithPreSkillAttackKey("D2", "D3", "D4", "D1"), keyboard.Keys.ToArray(), "near-ready calibrated cooldown should be treated as ready");
}

static async Task TestObservedCooldownSurvivesZeroEndTickReadAsync()
{
    var settings = CreateScriptSettings();
    var plan = SemiAutoSkillPlan.FromSettings(settings.Skills);
    var keyboard = new RecordingKeyboardInput();
    var logger = new InMemoryRoadhogLogger();
    var gameApi = new FakeGameApi();
    var controller = new SemiAutoCombatController(keyboard);
    var state = new SemiAutoCombatState();
    CalibrateCooldownClock(state);

    var observedCoolingSkill = CreateSkillSnapshotsById(new Dictionary<uint, uint>
    {
        [1] = CooldownEndIn(30_000)
    }).Single(skill => skill.SkillId == 1);
    state.TryUpdateCooldownTickCalibration(
        new[] { observedCoolingSkill },
        unchecked((uint)Environment.TickCount64),
        DateTimeOffset.Now,
        out _);

    gameApi.Skills = CreateSkillSnapshotsById(new Dictionary<uint, uint>
    {
        [1] = 0,
        [5] = ActiveCooldownEnd(),
        [51] = ActiveCooldownEnd(),
        [6] = ActiveCooldownEnd(),
        [61] = ActiveCooldownEnd(),
        [62] = ActiveCooldownEnd(),
        [7] = ActiveCooldownEnd(),
        [8] = ActiveCooldownEnd(),
        [9] = ActiveCooldownEnd(),
        [10] = ActiveCooldownEnd()
    });

    await controller.TickAsync(CreateContext(settings, gameApi, logger), plan, state).ConfigureAwait(false);

    AssertFalse(keyboard.Keys.Contains("D1"), "known future cooldown should block a zero end-tick read");
}

static async Task TestOpeningAttackKeySwitchPressesCOnceAsync()
{
    var settings = CreateScriptSettings();
    settings.SemiAuto.AttackKeyLoopEnabled = true;
    var plan = SemiAutoSkillPlan.FromSettings(settings.Skills);
    var keyboard = new RecordingKeyboardInput();
    var logger = new InMemoryRoadhogLogger();
    var gameApi = new FakeGameApi
    {
        Skills = Array.Empty<SkillSnapshot>()
    };
    var controller = new SemiAutoCombatController(keyboard);
    var state = new SemiAutoCombatState();
    CalibrateCooldownClock(state);

    await controller.TickAsync(CreateContext(settings, gameApi, logger), plan, state).ConfigureAwait(false);
    AssertSequence(new[] { "C" }, keyboard.Keys.ToArray(), "opening attack key should press C once when enabled");

    await Task.Delay(30).ConfigureAwait(false);
    AssertEqual(1, keyboard.Keys.Count, "opening attack key must not run on a background loop");

    await controller.TickAsync(CreateContext(settings, gameApi, logger), plan, state).ConfigureAwait(false);

    AssertSequence(new[] { "C" }, keyboard.Keys.ToArray(), "fallback should not press C after opening key was attempted");
}

static async Task TestMaintenanceHpRulePressesConfiguredKeyAsync()
{
    var settings = CreateScriptSettings();
    settings.Maintenance.SitMaintenanceEnabled = false;
    settings.Maintenance.HpMaintenanceRules.Add(new MaintenanceKeyRuleConfig
    {
        BelowPercent = 50,
        Key = "D8",
    });
    var plan = SemiAutoSkillPlan.FromSettings(settings.Skills);
    var keyboard = new RecordingKeyboardInput();
    var logger = new InMemoryRoadhogLogger();
    var gameApi = new FakeGameApi
    {
        Player = new PlayerSnapshot(1, 100, "Fake", 40, 100, 100, 100, 0, new Vector3Snapshot(0, 0, 0), DateTimeOffset.Now),
        Skills = CreateSkillSnapshotsById(new Dictionary<uint, uint>
        {
            [1] = 0,
            [5] = 0,
            [6] = 0
        })
    };
    var controller = new SemiAutoCombatController(keyboard);
    var state = new SemiAutoCombatState();
    CalibrateCooldownClock(state);
    keyboard.AfterPress = key =>
    {
        if (string.Equals(key, "D8", StringComparison.Ordinal))
        {
            gameApi.Skills = CreateSkillSnapshotsById(new Dictionary<uint, uint>
            {
                [8] = ActiveCooldownEnd(),
                [5] = 0,
                [6] = 0
            });
        }
    };

    await controller.TickAsync(CreateContext(settings, gameApi, logger), plan, state).ConfigureAwait(false);

    AssertSequence(new[] { "D8" }, keyboard.Keys.ToArray(), "low hp maintenance key");
    AssertFalse(!logger.Entries.Any(entry => entry.EventName == "semi_auto.maintenance.key_pressed"), "maintenance key press should be logged");

    await controller.TickAsync(CreateContext(settings, gameApi, logger), plan, state).ConfigureAwait(false);
    AssertEqual(1, keyboard.Keys.Count(key => key == "D8"), "maintenance key should not repeat while the mapped skill is cooling");
}

static async Task TestMaintenanceHpRuleRunsWithoutAttackableTargetAsync()
{
    var settings = CreateScriptSettings();
    settings.Maintenance.SitMaintenanceEnabled = false;
    settings.Maintenance.HpMaintenanceRules.Add(new MaintenanceKeyRuleConfig
    {
        BelowPercent = 50,
        Key = "D8",
    });
    var plan = SemiAutoSkillPlan.FromSettings(settings.Skills);
    var keyboard = new RecordingKeyboardInput();
    var logger = new InMemoryRoadhogLogger();
    var gameApi = new FakeGameApi
    {
        Player = new PlayerSnapshot(1, 0, "Fake", 40, 100, 100, 100, 0, new Vector3Snapshot(0, 0, 0), DateTimeOffset.Now),
        TargetEntityId = 0,
        TargetCurrentHp = 0,
        TargetMaxHp = 0,
        TargetPosition = null,
        Skills = CreateSkillSnapshotsById(new Dictionary<uint, uint>
        {
            [1] = 0,
            [5] = 0,
            [6] = 0
        })
    };
    var controller = new SemiAutoCombatController(keyboard);
    var state = new SemiAutoCombatState();
    CalibrateCooldownClock(state);
    keyboard.AfterPress = key =>
    {
        if (string.Equals(key, "D8", StringComparison.Ordinal))
        {
            gameApi.Skills = CreateSkillSnapshotsById(new Dictionary<uint, uint>
            {
                [8] = ActiveCooldownEnd(),
                [5] = 0,
                [6] = 0
            });
        }
    };

    await controller.TickAsync(CreateContext(settings, gameApi, logger), plan, state).ConfigureAwait(false);

    AssertSequence(new[] { "D8" }, keyboard.Keys.ToArray(), "low hp maintenance key should run before target validation");
    AssertFalse(!logger.Entries.Any(entry => entry.EventName == "semi_auto.maintenance.key_pressed"), "maintenance key press should be logged without target");
}

static async Task TestMaintenanceMpRulePressesConfiguredKeyAsync()
{
    var settings = CreateScriptSettings();
    settings.Maintenance.SitMaintenanceEnabled = false;
    settings.Maintenance.MpMaintenanceRules.Add(new MaintenanceKeyRuleConfig
    {
        BelowPercent = 30,
        Key = "NumPad1",
    });
    var plan = SemiAutoSkillPlan.FromSettings(settings.Skills);
    var keyboard = new RecordingKeyboardInput();
    var logger = new InMemoryRoadhogLogger();
    var gameApi = new FakeGameApi
    {
        Player = new PlayerSnapshot(1, 100, "Fake", 100, 100, 20, 100, 0, new Vector3Snapshot(0, 0, 0), DateTimeOffset.Now),
        Skills = CreateSkillSnapshotsById(new Dictionary<uint, uint>
        {
            [1] = 0,
            [5] = 0,
            [6] = 0
        })
    };
    var controller = new SemiAutoCombatController(keyboard);
    var state = new SemiAutoCombatState();
    CalibrateCooldownClock(state);
    keyboard.AfterPress = key =>
    {
        if (string.Equals(key, "NumPad1", StringComparison.Ordinal))
        {
            gameApi.Skills = CreateSkillSnapshotsById(new Dictionary<uint, uint>
            {
                [1] = ActiveCooldownEnd(),
                [5] = 0,
                [6] = 0
            });
        }
    };

    await controller.TickAsync(CreateContext(settings, gameApi, logger), plan, state).ConfigureAwait(false);

    AssertSequence(new[] { "NumPad1" }, keyboard.Keys.ToArray(), "low mp maintenance key");
}

static async Task TestMaintenanceSelectedSkillConfirmsBySkillIdAsync()
{
    var settings = CreateScriptSettings();
    settings.Maintenance.SitMaintenanceEnabled = false;
    settings.Maintenance.HpMaintenanceRules.Add(new MaintenanceKeyRuleConfig
    {
        BelowPercent = 50,
        Key = "NumPad0",
        SkillId = 1,
        SkillName = "淇濇姢涔嬬浘 I",
    });
    var plan = SemiAutoSkillPlan.FromSettings(settings.Skills);
    var keyboard = new RecordingKeyboardInput();
    var logger = new InMemoryRoadhogLogger();
    var gameApi = new FakeGameApi
    {
        Player = new PlayerSnapshot(1, 100, "Fake", 40, 100, 100, 100, 0, new Vector3Snapshot(0, 0, 0), DateTimeOffset.Now),
        Skills = CreateSkillSnapshotsById(new Dictionary<uint, uint>
        {
            [1] = 0,
            [5] = 0,
            [6] = 0
        })
    };
    keyboard.AfterPress = key =>
    {
        if (string.Equals(key, "NumPad0", StringComparison.Ordinal))
        {
            gameApi.Skills = CreateSkillSnapshotsById(new Dictionary<uint, uint>
            {
                [1] = ActiveCooldownEnd(),
                [5] = 0,
                [6] = 0
            });
        }
    };

    var controller = new SemiAutoCombatController(keyboard);
    var state = new SemiAutoCombatState();
    CalibrateCooldownClock(state);

    await controller.TickAsync(CreateContext(settings, gameApi, logger), plan, state).ConfigureAwait(false);

    AssertSequence(new[] { "NumPad0" }, keyboard.Keys.ToArray(), "selected maintenance skill key");
    var entry = logger.Entries.LastOrDefault(entry => entry.EventName == "semi_auto.maintenance.key_pressed");
    AssertFalse(entry is null, "selected maintenance skill should log a confirmed key press");
    AssertEqual(1u, Convert.ToUInt32(entry!.Fields["confirmedSkillId"]), "confirmed maintenance skill id");
}

static async Task TestMaintenanceSelectedCoolingSkillSkipsKeyAsync()
{
    var settings = CreateScriptSettings();
    settings.Maintenance.SitMaintenanceEnabled = false;
    settings.Maintenance.HpMaintenanceRules.Add(new MaintenanceKeyRuleConfig
    {
        BelowPercent = 50,
        Key = "NumPad0",
        SkillId = 1,
        SkillName = "淇濇姢涔嬬浘 I",
    });
    var plan = SemiAutoSkillPlan.FromSettings(settings.Skills);
    var keyboard = new RecordingKeyboardInput();
    var logger = new InMemoryRoadhogLogger();
    var gameApi = new FakeGameApi
    {
        Player = new PlayerSnapshot(1, 100, "Fake", 40, 100, 100, 100, 0, new Vector3Snapshot(0, 0, 0), DateTimeOffset.Now),
        Skills = CreateSkillSnapshotsById(new Dictionary<uint, uint>
        {
            [1] = CooldownEndIn(30_000),
            [5] = 0,
            [6] = 0
        })
    };

    var controller = new SemiAutoCombatController(keyboard);
    var state = new SemiAutoCombatState();

    await controller.TickAsync(CreateContext(settings, gameApi, logger), plan, state).ConfigureAwait(false);

    AssertFalse(keyboard.Keys.Contains("NumPad0"), "cooling selected maintenance skill should not press maintenance key");
    AssertFalse(keyboard.Keys.Count == 0, "combat should continue when selected maintenance skill is cooling");
}

static async Task TestMaintenanceSitEnterExitAsync()
{
    var settings = CreateScriptSettings();
    settings.Maintenance.SitMaintenanceEnabled = true;
    settings.Maintenance.SitHpBelowPercent = 25;
    settings.Maintenance.SitHpRecoverToPercent = 75;
    settings.Maintenance.SitMpBelowPercent = 10;
    settings.Maintenance.SitMpRecoverToPercent = 60;
    var keyboard = new RecordingKeyboardInput();
    var logger = new InMemoryRoadhogLogger();
    var gameApi = new FakeGameApi
    {
        Player = new PlayerSnapshot(1, 100, "Fake", 20, 100, 100, 100, 0, new Vector3Snapshot(0, 0, 0), DateTimeOffset.Now),
        Skills = CreateSkillSnapshotsById(new Dictionary<uint, uint>
        {
            [1] = 0,
            [5] = 0,
            [6] = 0
        })
    };
    var controller = new SemiAutoCombatController(keyboard);
    var state = new SemiAutoCombatState();
    CalibrateCooldownClock(state);
    var context = CreateContext(settings, gameApi, logger);

    await controller.TryHandleMaintenanceAsync(context, state, gameApi.Player).ConfigureAwait(false);
    AssertSequence(new[] { "OemComma" }, keyboard.Keys.ToArray(), "low hp should enter sit maintenance with comma");
    AssertFalse(!state.IsMaintenanceResting, "maintenance rest should stay active after sitting down");

    gameApi.Player = gameApi.Player with { CurrentHp = 74 };
    await controller.TryHandleMaintenanceAsync(context, state, gameApi.Player).ConfigureAwait(false);
    AssertSequence(new[] { "OemComma" }, keyboard.Keys.ToArray(), "rest should continue until hp reaches recovery threshold");

    gameApi.Player = gameApi.Player with { CurrentHp = 75 };
    await controller.TryHandleMaintenanceAsync(context, state, gameApi.Player).ConfigureAwait(false);
    AssertSequence(new[] { "OemComma", "X" }, keyboard.Keys.ToArray(), "recovered hp should exit sit maintenance with x");
    AssertFalse(state.IsMaintenanceResting, "maintenance rest should clear after standing up");
}

static async Task TestMaintenanceSitMpEnterExitAsync()
{
    var settings = CreateScriptSettings();
    settings.Maintenance.SitMaintenanceEnabled = true;
    settings.Maintenance.SitHpBelowPercent = 25;
    settings.Maintenance.SitHpRecoverToPercent = 75;
    settings.Maintenance.SitMpBelowPercent = 30;
    settings.Maintenance.SitMpRecoverToPercent = 60;
    var keyboard = new RecordingKeyboardInput();
    var logger = new InMemoryRoadhogLogger();
    var gameApi = new FakeGameApi
    {
        Player = new PlayerSnapshot(1, 100, "Fake", 100, 100, 20, 100, 0, new Vector3Snapshot(0, 0, 0), DateTimeOffset.Now),
        Skills = CreateSkillSnapshotsById(new Dictionary<uint, uint>
        {
            [1] = 0,
            [5] = 0,
            [6] = 0
        })
    };
    var controller = new SemiAutoCombatController(keyboard);
    var state = new SemiAutoCombatState();
    CalibrateCooldownClock(state);
    var context = CreateContext(settings, gameApi, logger);

    await controller.TryHandleMaintenanceAsync(context, state, gameApi.Player).ConfigureAwait(false);
    AssertSequence(new[] { "OemComma" }, keyboard.Keys.ToArray(), "low mp should enter sit maintenance with comma");
    AssertFalse(!state.IsMaintenanceResting, "mp maintenance rest should stay active after sitting down");
    AssertFalse(!state.MaintenanceRestingForMp, "mp maintenance rest should track mp recovery");
    AssertFalse(state.MaintenanceRestingForHp, "full hp should not be tracked for mp maintenance rest");

    gameApi.Player = gameApi.Player with { CurrentMp = 59 };
    await controller.TryHandleMaintenanceAsync(context, state, gameApi.Player).ConfigureAwait(false);
    AssertSequence(new[] { "OemComma" }, keyboard.Keys.ToArray(), "rest should continue until mp reaches recovery threshold");

    gameApi.Player = gameApi.Player with { CurrentMp = 60 };
    await controller.TryHandleMaintenanceAsync(context, state, gameApi.Player).ConfigureAwait(false);
    AssertSequence(new[] { "OemComma", "X" }, keyboard.Keys.ToArray(), "recovered mp should exit sit maintenance with x");
    AssertFalse(state.IsMaintenanceResting, "mp maintenance rest should clear after standing up");
}

static async Task TestSemiAutoSkipsSitMaintenanceAsync()
{
    var settings = CreateScriptSettings();
    settings.Maintenance.SitMaintenanceEnabled = true;
    settings.Maintenance.SitHpBelowPercent = 25;
    settings.Maintenance.SitHpRecoverToPercent = 75;
    var plan = SemiAutoSkillPlan.FromSettings(settings.Skills);
    var keyboard = new RecordingKeyboardInput();
    var logger = new InMemoryRoadhogLogger();
    var gameApi = new FakeGameApi
    {
        Player = new PlayerSnapshot(1, 100, "Fake", 20, 100, 100, 100, 0, new Vector3Snapshot(0, 0, 0), DateTimeOffset.Now),
        Skills = CreateSkillSnapshotsById(new Dictionary<uint, uint>
        {
            [1] = 0,
            [5] = 0,
            [6] = 0
        })
    };
    var controller = new SemiAutoCombatController(keyboard);
    var state = new SemiAutoCombatState();
    CalibrateCooldownClock(state);

    await controller.TickAsync(CreateContext(settings, gameApi, logger), plan, state).ConfigureAwait(false);

    AssertFalse(keyboard.Keys.Contains("OemComma"), "semi-auto tick should not enter sit maintenance");
    AssertFalse(state.IsMaintenanceResting, "semi-auto tick should not start rest state");
    AssertFalse(!keyboard.Keys.Any(key => key.StartsWith("D", StringComparison.OrdinalIgnoreCase)), "semi-auto should continue skill release");
}

static async Task TestPollResultAdvancesRootOrderAsync()
{
    var settings = CreateScriptSettings();
    var plan = SemiAutoSkillPlan.FromSettings(settings.Skills);
    var keyboard = new RecordingKeyboardInput();
    var logger = new InMemoryRoadhogLogger();
    var gameApi = new FakeGameApi
    {
        Skills = CreateSkillSnapshotsById(new Dictionary<uint, uint>
        {
            [1] = 0,
            [5] = 0,
            [51] = 0,
            [6] = 0,
            [7] = 0,
            [8] = 0,
            [9] = 0,
            [10] = 0
        })
    };
    var controller = new SemiAutoCombatController(keyboard);
    var state = new SemiAutoCombatState();
    var context = CreateContext(settings, gameApi, logger);

    await controller.TickAsync(context, plan, state).ConfigureAwait(false);
    AssertSequence(WithPreSkillAttackKey("D2", "D3", "D4", "D1"), keyboard.Keys.ToArray(), "first ready root");

    keyboard.Keys.Clear();
    gameApi.Skills = CreateSkillSnapshotsById(new Dictionary<uint, uint>
    {
        [1] = ActiveCooldownEnd(),
        [5] = 0,
        [51] = 0,
        [6] = 0,
        [7] = 0,
        [8] = 0,
        [9] = 0,
        [10] = 0
    });
    await controller.TickAsync(context, plan, state).ConfigureAwait(false);
    AssertSequence(WithPreSkillAttackKey("D2", "D3", "D4", "D5"), keyboard.Keys.ToArray(), "second ready root");

    keyboard.Keys.Clear();
    gameApi.Skills = CreateSkillSnapshotsById(new Dictionary<uint, uint>
    {
        [1] = ActiveCooldownEnd(),
        [5] = ActiveCooldownEnd(),
        [51] = 0,
        [6] = 0,
        [7] = 0,
        [8] = 0,
        [9] = 0,
        [10] = 0
    });
    await controller.TickAsync(context, plan, state).ConfigureAwait(false);
    AssertSequence(WithPreSkillKey("D5"), keyboard.Keys.ToArray(), "chain child inherits D5 without trigger prefix");

    keyboard.Keys.Clear();
    gameApi.Skills = CreateSkillSnapshotsById(new Dictionary<uint, uint>
    {
        [1] = ActiveCooldownEnd(),
        [5] = ActiveCooldownEnd(),
        [51] = ActiveCooldownEnd(),
        [6] = 0,
        [7] = 0,
        [8] = 0,
        [9] = 0,
        [10] = 0
    });
    await controller.TickAsync(context, plan, state).ConfigureAwait(false);
    AssertSequence(Array.Empty<string>(), keyboard.Keys.ToArray(), "confirmed terminal chain clears before ordinary fallback");

    keyboard.Keys.Clear();
    await controller.TickAsync(context, plan, state).ConfigureAwait(false);
    AssertSequence(WithPreSkillAttackKey("D2", "D3", "D4", "D6"), keyboard.Keys.ToArray(), "poll-unavailable early roots allow D6");
}

static async Task TestDpSkillSkippedAsync()
{
    var settings = CreateScriptSettings();
    var plan = SemiAutoSkillPlan.FromSettings(settings.Skills);
    var keyboard = new RecordingKeyboardInput();
    var logger = new InMemoryRoadhogLogger();
    var gameApi = new FakeGameApi
    {
        Skills = CreateSkillSnapshots(new Dictionary<string, uint>
        {
            ["保护之盾 I"] = 1000,
            ["盾牌重击 II"] = 1000,
            ["弱化之猛击 II"] = 1000,
            ["猛烈一击 III"] = 1000,
            ["挑衅猛击 I"] = 1000,
            ["闪光斩 I"] = 1000,
            ["暗黑之惩戒 II"] = 0
        })
    };
    var controller = new SemiAutoCombatController(keyboard);
    var state = new SemiAutoCombatState();
    CalibrateCooldownClock(state);

    await controller.TickAsync(CreateContext(settings, gameApi, logger), plan, state).ConfigureAwait(false);

    AssertSequence(WithTriggerFallbackAttackKey("D2", "D3", "D4"), keyboard.Keys.ToArray(), "trigger fallback should run when only dp is ready");
    AssertFalse(keyboard.Keys.Contains("D0"), "dp skill should not be pressed by trigger fallback");
}

static async Task TestChainPressesNextStageWithoutSourceCooldownAsync()
{
    var settings = CreateScriptSettings();
    var plan = SemiAutoSkillPlan.FromSettings(settings.Skills);
    var keyboard = new RecordingKeyboardInput();
    var logger = new InMemoryRoadhogLogger();
    var gameApi = new FakeGameApi();
    var controller = new SemiAutoCombatController(keyboard);
    var state = new SemiAutoCombatState();
    var context = CreateContext(settings, gameApi, logger);

    gameApi.Skills = CreateSkillSnapshotsById(new Dictionary<uint, uint>
    {
        [1] = ActiveCooldownEnd(),
        [5] = ActiveCooldownEnd(),
        [6] = 0,
        [61] = 0,
        [62] = 0,
        [7] = 0,
        [8] = 0,
        [9] = ActiveCooldownEnd(),
        [10] = 0
    });
    await controller.TickAsync(context, plan, state).ConfigureAwait(false);
    AssertSequence(WithPreSkillAttackKey("D2", "D3", "D4", "D6"), keyboard.Keys.ToArray(), "first stage root press");

    keyboard.Keys.Clear();
    gameApi.Skills = CreateSkillSnapshotsById(new Dictionary<uint, uint>
    {
        [1] = ActiveCooldownEnd(),
        [5] = ActiveCooldownEnd(),
        [6] = 0,
        [61] = 0,
        [62] = 0,
        [7] = 0,
        [8] = 0,
        [9] = ActiveCooldownEnd(),
        [10] = 0
    });
    await controller.TickAsync(context, plan, state).ConfigureAwait(false);
    AssertSequence(WithPreSkillKey("D6"), keyboard.Keys.ToArray(), "chain next stage skips trigger prefix");
    AssertEqual(plan.Roots.Single(root => root.SkillId == 6).Children[0].Name, LastPressedSkill(logger), "next stage skill");

    keyboard.Keys.Clear();
    gameApi.Skills = CreateSkillSnapshotsById(new Dictionary<uint, uint>
    {
        [1] = ActiveCooldownEnd(),
        [5] = ActiveCooldownEnd(),
        [6] = ActiveCooldownEnd(),
        [61] = 0,
        [62] = 0,
        [7] = 0,
        [8] = 0,
        [9] = ActiveCooldownEnd(),
        [10] = 0
    });
    await controller.TickAsync(context, plan, state).ConfigureAwait(false);
    AssertSequence(WithPreSkillKey("D6"), keyboard.Keys.ToArray(), "unconfirmed second stage repeats");
    AssertEqual(plan.Roots.Single(root => root.SkillId == 6).Children[0].Name, LastPressedSkill(logger), "unconfirmed second stage skill");

    keyboard.Keys.Clear();
    gameApi.Skills = CreateSkillSnapshotsById(new Dictionary<uint, uint>
    {
        [1] = ActiveCooldownEnd(),
        [5] = ActiveCooldownEnd(),
        [6] = ActiveCooldownEnd(),
        [61] = ActiveCooldownEnd(),
        [62] = 0,
        [7] = 0,
        [8] = 0,
        [9] = ActiveCooldownEnd(),
        [10] = 0
    });
    await controller.TickAsync(context, plan, state).ConfigureAwait(false);
    AssertSequence(WithPreSkillKey("D6"), keyboard.Keys.ToArray(), "third stage skips trigger prefix after second stage confirms");
    AssertEqual(plan.Roots.Single(root => root.SkillId == 6).Children[0].Children[0].Name, LastPressedSkill(logger), "third stage skill");
}

static async Task TestChainPressesConfiguredChildWithoutCooldownFilterAsync()
{
    var settings = CreateScriptSettings();
    var robustRoot = settings.Skills.ExecutionTree.Single(node => node.SkillId == 6);
    robustRoot.Children.Add(Node(63, "澶囩敤杩炴妧 I", "杩炵画鎶€"));

    var plan = SemiAutoSkillPlan.FromSettings(settings.Skills);
    var keyboard = new RecordingKeyboardInput();
    var logger = new InMemoryRoadhogLogger();
    var gameApi = new FakeGameApi();
    var controller = new SemiAutoCombatController(keyboard);
    var state = new SemiAutoCombatState();
    var context = CreateContext(settings, gameApi, logger);
    var root = plan.Roots.Single(node => node.SkillId == 6);

    state.StartPendingChainAdvance(root, root.Children[0], DateTimeOffset.Now.AddSeconds(5), 0);
    gameApi.Skills = CreateSkillSnapshotsById(new Dictionary<uint, uint>
    {
        [1] = ActiveCooldownEnd(),
        [5] = ActiveCooldownEnd(),
        [6] = ActiveCooldownEnd(),
        [61] = ActiveCooldownEnd(),
        [62] = ActiveCooldownEnd(),
        [63] = 0,
        [7] = ActiveCooldownEnd(),
        [8] = ActiveCooldownEnd(),
        [9] = ActiveCooldownEnd(),
        [10] = 0
    }).Concat(new[]
    {
        new SkillSnapshot(63, "澶囩敤杩炴妧 I", 1, 1, "澶囩敤杩炴妧", 1, false, 0, 0)
    }).ToArray();

    await controller.TickAsync(context, plan, state).ConfigureAwait(false);

    AssertSequence(WithPreSkillKey("D6"), keyboard.Keys.ToArray(), "chain child skips trigger prefix");
    AssertEqual(root.Children[0].Name, LastPressedSkill(logger), "chain child ignores ordinary cooldown filter");
}

static async Task TestChainSurvivesTargetGapAsync()
{
    var settings = CreateScriptSettings();
    var plan = SemiAutoSkillPlan.FromSettings(settings.Skills);
    var keyboard = new RecordingKeyboardInput();
    var logger = new InMemoryRoadhogLogger();
    var gameApi = new FakeGameApi();
    var controller = new SemiAutoCombatController(keyboard);
    var state = new SemiAutoCombatState();
    var context = CreateContext(settings, gameApi, logger);

    gameApi.Skills = CreateSkillSnapshotsById(new Dictionary<uint, uint>
    {
        [1] = ActiveCooldownEnd(),
        [5] = ActiveCooldownEnd(),
        [6] = 0,
        [61] = 0,
        [62] = ActiveCooldownEnd(),
        [7] = 0,
        [8] = 0,
        [9] = ActiveCooldownEnd(),
        [10] = 0
    });
    await controller.TickAsync(context, plan, state).ConfigureAwait(false);
    AssertSequence(WithPreSkillAttackKey("D2", "D3", "D4", "D6"), keyboard.Keys.ToArray(), "source press before target gap");

    keyboard.Keys.Clear();
    gameApi.TargetCurrentHp = 0;
    await controller.TickAsync(context, plan, state).ConfigureAwait(false);
    AssertEqual(0, keyboard.Keys.Count, "dead target should not press");

    gameApi.TargetEntityId = 200;
    gameApi.TargetCurrentHp = 1000;
    gameApi.Skills = CreateSkillSnapshotsById(new Dictionary<uint, uint>
    {
        [1] = ActiveCooldownEnd(),
        [5] = ActiveCooldownEnd(),
        [6] = ActiveCooldownEnd(),
        [61] = 0,
        [62] = ActiveCooldownEnd(),
        [7] = 0,
        [8] = 0,
        [9] = ActiveCooldownEnd(),
        [10] = 0
    });
    await controller.TickAsync(context, plan, state).ConfigureAwait(false);
    AssertSequence(WithPreSkillKey("D6"), keyboard.Keys.ToArray(), "target switch should keep chain next stage without trigger prefix");
    AssertEqual(plan.Roots.Single(root => root.SkillId == 6).Children[0].Name, LastPressedSkill(logger), "target switch second stage skill");
}

static async Task TestChainLockPreventsRootFallbackWhileChildMissingAsync()
{
    var settings = CreateScriptSettings();
    var plan = SemiAutoSkillPlan.FromSettings(settings.Skills);
    var keyboard = new RecordingKeyboardInput();
    var logger = new InMemoryRoadhogLogger();
    var gameApi = new FakeGameApi();
    var controller = new SemiAutoCombatController(keyboard);
    var state = new SemiAutoCombatState();
    var context = CreateContext(settings, gameApi, logger);
    var root = plan.Roots.Single(node => node.SkillId == 6);

    state.StartPendingChainAdvance(root, root.Children[0], DateTimeOffset.Now.AddSeconds(5), 0);
    gameApi.Skills = Flatten(plan.Roots)
        .Where(node => node.SkillId is 1 or 5 or 6 or 7 or 8 or 9 or 10)
        .Select(node => new SkillSnapshot(
            node.SkillId,
            node.Name,
            1,
            1,
            node.BaseName,
            1,
            false,
            0,
            0))
        .ToArray();

    await controller.TickAsync(context, plan, state).ConfigureAwait(false);

    AssertEqual(0, keyboard.Keys.Count, "pending chain must not fall back to ready root when child is missing");
    AssertFalse(!state.HasChainWork, "pending chain should remain locked while waiting for child snapshot");
    AssertFalse(logger.Entries.Any(entry => entry.EventName == "semi_auto.chain.ended"), "missing child should not clear chain before timeout");
}

static async Task TestChainStrictOrderAsync()
{
    var settings = CreateScriptSettings();
    var plan = SemiAutoSkillPlan.FromSettings(settings.Skills);
    var keyboard = new RecordingKeyboardInput();
    var logger = new InMemoryRoadhogLogger();
    var gameApi = new FakeGameApi
    {
        Skills = CreateSkillSnapshots(new Dictionary<string, uint>
        {
            ["保护之盾 I"] = 1000,
            ["盾牌重击 II"] = 1000,
            ["弱化之猛击 II"] = 1000,
            ["猛烈一击 III"] = 0,
            ["会心一击 III"] = 0,
            ["必灭一击 I"] = 1000,
            ["挑衅猛击 I"] = 0,
            ["闪光斩 I"] = 0,
            ["暗黑之惩戒 II"] = 0
        })
    };
    var controller = new SemiAutoCombatController(keyboard);
    var state = new SemiAutoCombatState();
    var context = CreateContext(settings, gameApi, logger);

    gameApi.Skills = CreateSkillSnapshotsById(new Dictionary<uint, uint>
    {
        [1] = ActiveCooldownEnd(),
        [5] = ActiveCooldownEnd(),
        [6] = 0,
        [61] = 0,
        [62] = ActiveCooldownEnd(),
        [7] = 0,
        [8] = 0,
        [9] = ActiveCooldownEnd(),
        [10] = 0
    });

    await controller.TickAsync(context, plan, state).ConfigureAwait(false);
    AssertSequence(WithPreSkillAttackKey("D2", "D3", "D4", "D6"), keyboard.Keys.ToArray(), "root chain start order");

    keyboard.Keys.Clear();
    gameApi.Skills = CreateSkillSnapshotsById(new Dictionary<uint, uint>
    {
        [1] = ActiveCooldownEnd(),
        [5] = ActiveCooldownEnd(),
        [6] = ActiveCooldownEnd(),
        [61] = 0,
        [62] = ActiveCooldownEnd(),
        [7] = 0,
        [8] = 0,
        [9] = ActiveCooldownEnd(),
        [10] = 0
    });
    await controller.TickAsync(context, plan, state).ConfigureAwait(false);
    AssertSequence(WithPreSkillKey("D6"), keyboard.Keys.ToArray(), "chain second stage skips trigger prefix");

    keyboard.Keys.Clear();
    gameApi.Skills = CreateSkillSnapshotsById(new Dictionary<uint, uint>
    {
        [1] = ActiveCooldownEnd(),
        [5] = ActiveCooldownEnd(),
        [6] = ActiveCooldownEnd(),
        [61] = ActiveCooldownEnd(),
        [62] = ActiveCooldownEnd(),
        [7] = 0,
        [8] = 0,
        [9] = ActiveCooldownEnd(),
        [10] = 0
    });
    await controller.TickAsync(context, plan, state).ConfigureAwait(false);
    AssertSequence(WithPreSkillKey("D6"), keyboard.Keys.ToArray(), "chain child should be attempted without ordinary cooldown filter");
    AssertEqual(
        plan.Roots.Single(root => root.SkillId == 6).Children[0].Children[0].Name,
        LastPressedSkill(logger),
        "third stage skill");
}

static async Task TestCombatTickCountsKillAsync()
{
    var settings = CreateScriptSettings();
    var plan = SemiAutoSkillPlan.FromSettings(settings.Skills);
    var keyboard = new RecordingKeyboardInput();
    var logger = new InMemoryRoadhogLogger();
    var runtimeStates = new AccountRuntimeManager(logger);
    runtimeStates.GetOrCreate("account1");
    var gameApi = new FakeGameApi
    {
        Skills = Array.Empty<SkillSnapshot>(),
        TargetEntityId = 321,
        TargetCurrentHp = 1000
    };
    var controller = new SemiAutoCombatController(keyboard);
    var state = new SemiAutoCombatState();
    var context = CreateContext(settings, gameApi, logger, runtimeStates);

    await controller.TickAsync(context, plan, state).ConfigureAwait(false);
    AssertEqual(0, runtimeStates.Snapshot().First().KillCount, "alive target should not count as kill");

    gameApi.TargetCurrentHp = 0;
    await controller.TickAsync(context, plan, state).ConfigureAwait(false);
    AssertEqual(1, runtimeStates.Snapshot().First().KillCount, "dead transition should count one kill");

    await controller.TickAsync(context, plan, state).ConfigureAwait(false);
    AssertEqual(1, runtimeStates.Snapshot().First().KillCount, "same dead target must not count repeatedly");
}

static ScriptSettings CreateScriptSettings()
{
    return new ScriptSettings
    {
        MainMode = AccountMainMode.SemiAuto,
        Skills = CreateSkillSettings(),
        SemiAuto = new SemiAutoScriptSettings
        {
            TickIntervalMs = 50,
            ChainTickIntervalMs = 30,
            TargetIdleDelayMs = 50,
            KeyHoldMs = 1,
            AttackKeyLoopEnabled = false,
            AttackKeyLoopIntervalMs = 300,
            KeyGapMs = 1,
            RepeatGuardMs = 1,
            PostPressSuppressMs = 1,
            DefaultChainTimeMs = 5000
        }
    };
}

static SkillScriptSettings CreateSkillSettings()
{
    return new SkillScriptSettings
    {
        Mode = SkillConfigurationMode.Auto,
        TriggerPrefixMode = "TopContiguousTriggerSkills",
        ExecutionTree = new List<SkillConfigNode>
        {
            Node(1, "保护之盾 I", "状态技能"),
            Node(2, "惩戒一击 I", "触发技能"),
            Node(3, "盾牌反击 II", "触发技能"),
            Node(4, "盾牌猛击 II", "触发技能"),
            Node(5, "弱化之猛击 II", "主动技能", Node(51, "连续乱打 I", "连续技")),
            Node(6, "猛烈一击 III", "主动技能", Node(61, "会心一击 III", "连续技", Node(62, "必灭一击 I", "连续技"))),
            Node(7, "挑衅猛击 I", "主动技能"),
            Node(8, "闪光斩 I", "主动技能"),
            Node(9, "盾牌重击 II", "主动技能"),
            Node(10, "暗黑之惩戒 II", "DP技能")
        }
    };
}

static SkillConfigNode Node(uint id, string name, string type, params SkillConfigNode[] children)
{
    return new SkillConfigNode
    {
        SkillId = id,
        Name = name,
        BaseName = name,
        Type = type,
        ChainTimeMs = 5000,
        Children = children.ToList()
    };
}

static IReadOnlyList<SkillSnapshot> CreateSkillSnapshots(IReadOnlyDictionary<string, uint> cooldowns)
{
    var plan = SemiAutoSkillPlan.FromSettings(CreateSkillSettings());
    return Flatten(plan.Roots)
        .Select(node => new SkillSnapshot(
            node.SkillId,
            node.Name,
            1,
            1,
            node.BaseName,
            1,
            false,
            cooldowns.ContainsKey(node.Name) ? 1000u : 0u,
            cooldowns.TryGetValue(node.Name, out var cooldownEnd) ? NormalizeCooldownEnd(cooldownEnd) : 0u))
        .ToArray();
}

static IReadOnlyList<SkillSnapshot> CreateSkillSnapshotsById(IReadOnlyDictionary<uint, uint> cooldowns)
{
    var plan = SemiAutoSkillPlan.FromSettings(CreateSkillSettings());
    return Flatten(plan.Roots)
        .Select(node =>
        {
            var configured = cooldowns.TryGetValue(node.SkillId, out var cooldownEnd)
                ? NormalizeCooldownEnd(cooldownEnd)
                : 0u;

            return new SkillSnapshot(
                node.SkillId,
                node.Name,
                1,
                1,
                node.BaseName,
                1,
                false,
                cooldowns.ContainsKey(node.SkillId) ? 1000u : 0u,
                configured);
        })
        .ToArray();
}

static uint NormalizeCooldownEnd(uint cooldownEnd)
{
    if (cooldownEnd == 0)
    {
        return 0;
    }

    return cooldownEnd == 1000u
        ? ActiveCooldownEnd()
        : cooldownEnd;
}

static uint ActiveCooldownEnd()
{
    return unchecked((uint)Environment.TickCount64 + 1_000u);
}

static uint CooldownEndIn(int remainingMs)
{
    return unchecked((uint)Environment.TickCount64 + (uint)Math.Max(0, remainingMs));
}

static uint StaleCooldownEnd()
{
    var now = unchecked((uint)Environment.TickCount64);
    return now > 60_000u ? now - 60_000u : 1u;
}

static void CalibrateCooldownClock(SemiAutoCombatState state)
{
    var osTick = unchecked((uint)Environment.TickCount64);
    var before = new SkillSnapshot(
        900001,
        "calibration-marker",
        1,
        1,
        "calibration-marker",
        1,
        false,
        1_000,
        0);
    var after = before with { CooldownEndTime = unchecked(osTick + 1_000u) };

    state.MarkSkillPressed(before, DateTimeOffset.Now.AddSeconds(1));
    if (!state.TryUpdateCooldownTickCalibration(
            new[] { after },
            osTick,
            DateTimeOffset.Now,
            out _))
    {
        throw new InvalidOperationException("failed to calibrate test cooldown clock");
    }
}

static IEnumerable<SemiAutoSkillNode> Flatten(IEnumerable<SemiAutoSkillNode> roots)
{
    foreach (var root in roots)
    {
        yield return root;
        foreach (var child in Flatten(root.Children))
        {
            yield return child;
        }
    }
}

static SharedPathDocument CreatePath(string name, params Vector3Snapshot[] points)
{
    var document = new SharedPathDocument
    {
        Name = name,
        CreatedAt = DateTimeOffset.Now,
        UpdatedAt = DateTimeOffset.Now
    };

    double totalDistance = 0.0D;
    for (var i = 0; i < points.Length; i++)
    {
        var segmentDistance = i == 0
            ? 0.0D
            : StationaryCombatTargetSelector.HorizontalDistance(points[i - 1], points[i]);
        totalDistance += segmentDistance;
        document.Points.Add(new SharedPathPoint
        {
            Index = i,
            X = points[i].X,
            Y = points[i].Y,
            Z = points[i].Z,
            SegmentDistance = segmentDistance,
            TotalDistance = totalDistance,
            RecordedAt = DateTimeOffset.Now
        });
    }

    return document;
}

static AccountWorkerContext CreateContext(
    ScriptSettings settings,
    IRoadhogGameApi gameApi,
    IRoadhogLogger logger,
    AccountRuntimeManager? runtimeStates = null,
    AccountWorkerOptions? options = null,
    CancellationToken stopToken = default)
{
    var account = new AccountConfig
    {
        AccountName = "account1",
        ScriptSettings = settings
    };

    return new AccountWorkerContext(
        account,
        gameApi,
        logger,
        runtimeStates ?? new AccountRuntimeManager(logger),
        options ?? new AccountWorkerOptions(),
        stopToken);
}

static async Task WaitUntilAsync(Func<bool> predicate, string label)
{
    var deadline = DateTimeOffset.Now + TimeSpan.FromSeconds(2);
    while (DateTimeOffset.Now < deadline)
    {
        if (predicate())
        {
            return;
        }

        await Task.Delay(10).ConfigureAwait(false);
    }

    throw new InvalidOperationException("Timed out waiting for " + label + ".");
}

static async Task IgnoreCancellationAsync(Task task)
{
    try
    {
        await task.ConfigureAwait(false);
    }
    catch (OperationCanceledException)
    {
    }
}

static string[] WithPreSkillKey(params string[] keys)
{
    return keys.ToArray();
}

static string[] WithPreSkillAttackKey(params string[] keys)
{
    return keys.ToArray();
}

static string[] WithTriggerFallbackAttackKey(params string[] keys)
{
    return keys.ToArray();
}

static string CreateTempDirectory(string prefix)
{
    var directory = Path.Combine(Path.GetTempPath(), prefix + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(directory);
    return directory;
}

static void DeleteDirectoryIfExists(string directory)
{
    if (Directory.Exists(directory))
    {
        Directory.Delete(directory, recursive: true);
    }
}

static void AssertSequence<T>(IReadOnlyList<T> expected, IReadOnlyList<T> actual, string label)
{
    if (!expected.SequenceEqual(actual))
    {
        throw new InvalidOperationException(label + ": expected [" + string.Join(", ", expected) + "] but got [" + string.Join(", ", actual) + "]");
    }
}

static void AssertEqual<T>(T expected, T actual, string label)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException(label + ": expected " + expected + " but got " + actual);
    }
}

static void AssertHidCode(string key, int expected)
{
    AssertFalse(
        !RoadhogInputKeyMap.TryResolveHidCode(key, out var actual),
        "key should resolve: " + key);
    AssertEqual(expected, actual, "hid code for " + key);
}

static void AssertFalse(bool value, string label)
{
    if (value)
    {
        throw new InvalidOperationException(label);
    }
}

static string LastPressedSkill(InMemoryRoadhogLogger logger)
{
    var entry = logger.Entries.LastOrDefault(entry => entry.EventName == "semi_auto.key.pressed");
    return entry is null ? string.Empty : Convert.ToString(entry.Fields["skill"]) ?? string.Empty;
}

sealed class RecordingKeyboardInput : IKeyboardInput
{
    public List<string> Keys { get; } = new();

    public List<string> KeyDowns { get; } = new();

    public List<string> KeyUps { get; } = new();

    public List<string> MouseCommands { get; } = new();

    public Action<string>? AfterPress { get; set; }

    public Task<OperationResult> PressKeyAsync(
        string key,
        TimeSpan holdDuration,
        CancellationToken cancellationToken = default)
    {
        Keys.Add(key);
        AfterPress?.Invoke(key);
        return Task.FromResult(OperationResult.Ok());
    }

    public Task<OperationResult> KeyDownAsync(
        string key,
        CancellationToken cancellationToken = default)
    {
        KeyDowns.Add(key);
        return Task.FromResult(OperationResult.Ok());
    }

    public Task<OperationResult> KeyUpAsync(
        string key,
        CancellationToken cancellationToken = default)
    {
        KeyUps.Add(key);
        return Task.FromResult(OperationResult.Ok());
    }

    public Task<OperationResult> MouseDownAsync(
        RoadhogMouseButton button,
        CancellationToken cancellationToken = default)
    {
        MouseCommands.Add("down:" + button);
        return Task.FromResult(OperationResult.Ok());
    }

    public Task<OperationResult> MouseUpAsync(
        RoadhogMouseButton button,
        CancellationToken cancellationToken = default)
    {
        MouseCommands.Add("up:" + button);
        return Task.FromResult(OperationResult.Ok());
    }

    public Task<OperationResult> MoveMouseRelativeAsync(
        int deltaX,
        int deltaY,
        CancellationToken cancellationToken = default)
    {
        MouseCommands.Add("move:" + deltaX + "," + deltaY);
        return Task.FromResult(OperationResult.Ok());
    }

    public Task<OperationResult> ScrollMouseAsync(
        int wheelDelta,
        CancellationToken cancellationToken = default)
    {
        MouseCommands.Add("wheel:" + wheelDelta);
        return Task.FromResult(OperationResult.Ok());
    }
}

sealed class InMemorySharedPathStore : ISharedPathStore
{
    private readonly Dictionary<string, SharedPathDocument> _paths;

    public InMemorySharedPathStore(params SharedPathDocument[] paths)
    {
        _paths = paths.ToDictionary(path => path.Name, path => path.Clone(), StringComparer.OrdinalIgnoreCase);
    }

    public Task<OperationResult<IReadOnlyList<SharedPathSummary>>> LoadSummariesAsync(
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<SharedPathSummary> summaries = _paths.Values
            .Select(path => new SharedPathSummary(path.Name, path.PointCount, path.TotalDistance, path.UpdatedAt))
            .ToArray();
        return Task.FromResult(OperationResult<IReadOnlyList<SharedPathSummary>>.Ok(summaries));
    }

    public Task<OperationResult<SharedPathDocument>> LoadAsync(
        string name,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_paths.TryGetValue(name, out var path)
            ? OperationResult<SharedPathDocument>.Ok(path.Clone())
            : OperationResult<SharedPathDocument>.Fail("Path file was not found: " + name));
    }

    public Task<OperationResult> SaveAsync(
        SharedPathDocument path,
        CancellationToken cancellationToken = default)
    {
        _paths[path.Name] = path.Clone();
        return Task.FromResult(OperationResult.Ok());
    }

    public Task<OperationResult> DeleteAsync(
        string name,
        CancellationToken cancellationToken = default)
    {
        _paths.Remove(name);
        return Task.FromResult(OperationResult.Ok());
    }
}

sealed class FakeGameApi : IRoadhogScopedGameApi
{
    public PlayerSnapshot Player { get; set; } = new(
        1,
        0,
        "Fake Character",
        100,
        100,
        100,
        100,
        0,
        new Vector3Snapshot(0, 0, 0),
        DateTimeOffset.Now);

    public IReadOnlyList<SkillSnapshot> Skills { get; set; } = Array.Empty<SkillSnapshot>();

    public IReadOnlyList<uint>? LastRequestedSkillIds { get; private set; }

    public GameApiReadContext? LastPlayerContext { get; private set; }

    public ushort TargetEntityId { get; set; } = 100;

    public uint TargetCurrentHp { get; set; } = 1000;

    public uint TargetMaxHp { get; set; } = 1000;

    public Vector3Snapshot? TargetPosition { get; set; }

    public uint TargetServerObjectId { get; set; } = 1;

    public bool TargetIsTargetingLocalPlayer { get; set; } = true;

    public IReadOnlyList<WorldObjectSnapshot> WorldObjects { get; set; } = Array.Empty<WorldObjectSnapshot>();

    public IReadOnlyList<LootCorpseSnapshot> LootCorpses { get; set; } = Array.Empty<LootCorpseSnapshot>();

    public int LootReadCount { get; private set; }

    public Task<OperationResult<PlayerSnapshot>> ReadPlayerAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(OperationResult<PlayerSnapshot>.Ok(Player));
    }

    public Task<OperationResult<PlayerSnapshot>> ReadPlayerAsync(
        GameApiReadContext context,
        CancellationToken cancellationToken = default)
    {
        LastPlayerContext = context;
        return ReadPlayerAsync(cancellationToken);
    }

    public Task<OperationResult<LockedTargetSnapshot>> ReadLockedTargetAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(OperationResult<LockedTargetSnapshot>.Ok(new LockedTargetSnapshot(
            TargetEntityId,
            TargetEntityId,
            0,
            LockedTargetSnapshot.MonsterObjectType,
            "训练用稻草人",
            TargetCurrentHp,
            TargetMaxHp,
            TargetPosition,
            null,
            DateTimeOffset.Now,
            TargetServerObjectId,
            TargetIsTargetingLocalPlayer)));
    }

    public Task<OperationResult<LockedTargetSnapshot>> ReadLockedTargetAsync(
        GameApiReadContext context,
        CancellationToken cancellationToken = default)
    {
        return ReadLockedTargetAsync(cancellationToken);
    }

    public Task<OperationResult<IReadOnlyList<SkillSnapshot>>> ReadSkillsAsync(CancellationToken cancellationToken = default)
    {
        LastRequestedSkillIds = null;
        return Task.FromResult(OperationResult<IReadOnlyList<SkillSnapshot>>.Ok(Skills));
    }

    public Task<OperationResult<IReadOnlyList<SkillSnapshot>>> ReadSkillsAsync(
        GameApiReadContext context,
        CancellationToken cancellationToken = default)
    {
        return ReadSkillsAsync(cancellationToken);
    }

    public Task<OperationResult<IReadOnlyList<SkillSnapshot>>> ReadSkillsAsync(
        GameApiReadContext context,
        IReadOnlyCollection<uint> skillIds,
        CancellationToken cancellationToken = default)
    {
        LastRequestedSkillIds = skillIds.ToArray();
        var requested = LastRequestedSkillIds.ToHashSet();
        IReadOnlyList<SkillSnapshot> skills = Skills
            .Where(skill => requested.Contains(skill.SkillId))
            .ToArray();
        return Task.FromResult(OperationResult<IReadOnlyList<SkillSnapshot>>.Ok(skills));
    }

    public Task<OperationResult<IReadOnlyList<InventoryItemSnapshot>>> ReadInventoryAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(OperationResult<IReadOnlyList<InventoryItemSnapshot>>.Fail("not used"));
    }

    public Task<OperationResult<IReadOnlyList<WorldObjectSnapshot>>> ReadWorldObjectsAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(OperationResult<IReadOnlyList<WorldObjectSnapshot>>.Ok(WorldObjects));
    }

    public Task<OperationResult<IReadOnlyList<WorldObjectSnapshot>>> ReadWorldObjectsAsync(
        GameApiReadContext context,
        CancellationToken cancellationToken = default)
    {
        return ReadWorldObjectsAsync(cancellationToken);
    }

    public Task<OperationResult<IReadOnlyList<LootCorpseSnapshot>>> ReadLootCorpsesAsync(CancellationToken cancellationToken = default)
    {
        LootReadCount++;
        return Task.FromResult(OperationResult<IReadOnlyList<LootCorpseSnapshot>>.Ok(LootCorpses));
    }

    public Task<OperationResult<IReadOnlyList<LootCorpseSnapshot>>> ReadLootCorpsesAsync(
        GameApiReadContext context,
        CancellationToken cancellationToken = default)
    {
        return ReadLootCorpsesAsync(cancellationToken);
    }
}
