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
using Roadhog.Infrastructure.Paths;

var tests = new (string Name, Func<Task> Run)[]
{
    ("path recorder enforces five meter minimum", TestPathRecorderMinimumDistanceAsync),
    ("shared path store saves loads and deletes path files", TestSharedPathStoreRoundTripAsync),
    ("runtime player read uses account scoped context", TestRuntimePlayerReadUsesAccountScopeAsync),
    ("runtime player read returns character name", TestRuntimePlayerReadReturnsCharacterNameAsync),
    ("account config stores shared path names only", TestAccountConfigStoresSharedPathNamesOnlyAsync),
    ("account config persists stationary combat position", TestAccountConfigPersistsStationaryCombatPositionAsync),
    ("stationary combat target selector keeps monsters inside radius", TestStationaryTargetSelectorAsync),
    ("stationary combat faces selected target before tab", TestStationaryCombatFacesTargetBeforeTabAsync),
    ("stationary combat accepts twenty degree pre-lock face tolerance", TestStationaryCombatAcceptsTwentyDegreePreLockFaceToleranceAsync),
    ("stationary combat tabs until selected target is verified", TestStationaryCombatTabsUntilTargetVerifiedAsync),
    ("stationary combat verifies target after each tab press", TestStationaryCombatVerifiesAfterEachTabAsync),
    ("stationary combat releases path follow movement after target is verified", TestStationaryCombatReleasesMovementAfterAcquireAsync),
    ("stationary combat does not pulse W while approaching same target", TestStationaryCombatDoesNotPulseWWhileApproachingAsync),
    ("stationary combat treats locked zero hp target as combat", TestStationaryCombatTreatsLockedZeroHpTargetAsCombatAsync),
    ("stationary combat finishes current fight before returning home", TestStationaryCombatFinishesFightBeforeReturningHomeAsync),
    ("skill tree assigns keys by root order and chain children inherit root key", TestSkillTreeKeyMappingAsync),
    ("skill tree maps at most configured roots across the 22 supported keys", TestConfiguredRootKeyBoundaryAsync),
    ("combat tick presses trigger prefix then first ready root", TestCombatTickPressesPrefixThenReadyRootAsync),
    ("combat tick requests only configured skill ids", TestCombatTickRequestsOnlyConfiguredSkillIdsAsync),
    ("observed configured cooldown advance calibrates clock", TestObservedConfiguredCooldownAdvanceCalibratesClockAsync),
    ("uncalibrated nonzero cooldown falls back to first configured root", TestUncalibratedNonzeroCooldownFallsBackToFirstRootAsync),
    ("calibrated nonzero cooldown skips cooling roots", TestCalibratedNonzeroCooldownSkipsCoolingRootsAsync),
    ("calibrated cooldown tolerance treats near-ready as ready", TestCalibratedCooldownToleranceTreatsNearReadyAsReadyAsync),
    ("observed cooldown survives zero end tick read", TestObservedCooldownSurvivesZeroEndTickReadAsync),
    ("attack key fallback presses C synchronously", TestAttackKeyFallbackPressesCSynchronouslyAsync),
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
            Player = new PlayerSnapshot(1, 100, "Fake", 100, 100, 100, 100, 0, new Vector3Snapshot(0, 0, 0), DateTimeOffset.Now, 0, 20, 0),
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
            Player = new PlayerSnapshot(1, 999, "Fake", 100, 100, 100, 100, 0, new Vector3Snapshot(0, 0, 0), DateTimeOffset.Now, 75, 20, 75),
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
            Player = new PlayerSnapshot(1, 0, "Fake", 100, 100, 100, 100, 0, new Vector3Snapshot(0, 0, 0), DateTimeOffset.Now, 90, 20, 90),
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
            Player = new PlayerSnapshot(1, 0, "Fake", 100, 100, 100, 100, 0, new Vector3Snapshot(0, 0, 0), DateTimeOffset.Now, 90, 20, 90),
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
            Player = new PlayerSnapshot(1, 0, "Fake", 100, 100, 100, 100, 0, new Vector3Snapshot(0, 0, 0), DateTimeOffset.Now, 90, 20, 90),
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
            Player = new PlayerSnapshot(1, 999, "Fake", 100, 100, 100, 100, 0, new Vector3Snapshot(0, 0, 0), DateTimeOffset.Now, 90, 20, 90),
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
        Player = new PlayerSnapshot(1, 100, "Fake", 100, 100, 100, 100, 0, new Vector3Snapshot(0, 0, 0), DateTimeOffset.Now, 90, 20, 90),
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
        Player = new PlayerSnapshot(1, 100, "Fake", 100, 100, 100, 100, 0, new Vector3Snapshot(50, 0, 0), DateTimeOffset.Now, 0, 0, 0),
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
        WithPreSkillKey("D2", "D3", "D4", "D5"),
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

    AssertSequence(WithPreSkillKey("D2", "D3", "D4", "D1"), keyboard.Keys.ToArray(), "uncalibrated stale cooldown should not block all roots");
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

    AssertSequence(WithPreSkillKey("D2", "D3", "D4", "D5"), keyboard.Keys.ToArray(), "calibrated active cooldown should skip D1");
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

    AssertSequence(WithPreSkillKey("D2", "D3", "D4", "D1"), keyboard.Keys.ToArray(), "near-ready calibrated cooldown should be treated as ready");
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

static async Task TestAttackKeyFallbackPressesCSynchronouslyAsync()
{
    var settings = CreateScriptSettings();
    settings.SemiAuto.AttackKeyLoopEnabled = true;
    settings.SemiAuto.AttackKeyLoopIntervalMs = 10;
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
    AssertSequence(new[] { "C" }, keyboard.Keys.ToArray(), "attack key fallback should press C in the combat tick");

    await Task.Delay(30).ConfigureAwait(false);
    AssertEqual(1, keyboard.Keys.Count, "attack key fallback must not run on a background loop");

    await controller.TickAsync(CreateContext(settings, gameApi, logger), plan, state).ConfigureAwait(false);

    AssertSequence(new[] { "C", "C" }, keyboard.Keys.ToArray(), "attack key fallback should only advance on the next combat tick");
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
    AssertSequence(WithPreSkillKey("D2", "D3", "D4", "D1"), keyboard.Keys.ToArray(), "first ready root");

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
    AssertSequence(WithPreSkillKey("D2", "D3", "D4", "D5"), keyboard.Keys.ToArray(), "second ready root");

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
    AssertSequence(WithPreSkillKey("D2", "D3", "D4", "D6"), keyboard.Keys.ToArray(), "poll-unavailable early roots allow D6");
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

    AssertSequence(WithPreSkillKey("D2", "D3", "D4"), keyboard.Keys.ToArray(), "trigger fallback should run when only dp is ready");
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
    AssertSequence(WithPreSkillKey("D2", "D3", "D4", "D6"), keyboard.Keys.ToArray(), "first stage root press");

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
    AssertSequence(WithPreSkillKey("D2", "D3", "D4", "D6"), keyboard.Keys.ToArray(), "source press before target gap");

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
    AssertSequence(WithPreSkillKey("D2", "D3", "D4", "D6"), keyboard.Keys.ToArray(), "root chain start order");

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

static AccountWorkerContext CreateContext(
    ScriptSettings settings,
    IRoadhogGameApi gameApi,
    IRoadhogLogger logger,
    AccountRuntimeManager? runtimeStates = null)
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
        new AccountWorkerOptions(),
        CancellationToken.None);
}

static string[] WithPreSkillKey(params string[] keys)
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

    public IReadOnlyList<WorldObjectSnapshot> WorldObjects { get; set; } = Array.Empty<WorldObjectSnapshot>();

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
            DateTimeOffset.Now)));
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
}
