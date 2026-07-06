using Roadhog;
using Roadhog.Application;
using Roadhog.Application.SemiAuto;
using Roadhog.Application.StationaryCombat;
using Roadhog.Application.Workers;
using Roadhog.Core.Accounts;
using Roadhog.Core.Api;
using Roadhog.Core.Common;
using Roadhog.Core.Diagnostics;
using Roadhog.Core.Hardware;
using Roadhog.Core.Input;
using Roadhog.Core.Model;
using Roadhog.Core.Paths;
using Roadhog.Core.Processes;
using Roadhog.Core.Profiles;
using Roadhog.Infrastructure.Config;
using Roadhog.Infrastructure.Composition;
using Roadhog.Infrastructure.Diagnostics;
using Roadhog.Infrastructure.Input;
using Roadhog.Infrastructure.Paths;
using Roadhog.Infrastructure.Profiles;

var tests = new (string Name, Func<Task> Run)[]
{
    ("path recorder enforces five meter minimum", TestPathRecorderMinimumDistanceAsync),
    ("shared path store saves loads and deletes path files", TestSharedPathStoreRoundTripAsync),
    ("script profile store saves loads and deletes profile files", TestScriptProfileStoreRoundTripAsync),
    ("runtime player read uses account scoped context", TestRuntimePlayerReadUsesAccountScopeAsync),
    ("runtime skill read uses saved account scope when idle", TestRuntimeSkillReadUsesSavedAccountScopeWhenIdleAsync),
    ("runtime skill read maps saved hardware key to indexed fpga device", TestRuntimeSkillReadMapsSavedHardwareKeyToIndexedFpgaDeviceAsync),
    ("account start preserves configured indexed fpga device", TestAccountStartPreservesConfiguredIndexedFpgaDeviceAsync),
    ("account start lets configured vmm override hardware indexed device", TestAccountStartConfiguredVmmOverridesHardwareIndexedDeviceAsync),
    ("runtime world object read uses account scoped context", TestRuntimeWorldObjectReadUsesAccountScopeAsync),
    ("runtime summoned pet read uses account scoped context", TestRuntimeSummonedPetReadUsesAccountScopeAsync),
    ("runtime summoned pet roster read uses account scoped context", TestRuntimeSummonedPetRosterReadUsesAccountScopeAsync),
    ("runtime locked target abnormal read uses account scoped context", TestRuntimeLockedTargetAbnormalReadUsesAccountScopeAsync),
    ("aion class catalog maps old twelve classes", TestAionClassCatalogAsync),
    ("runtime player read returns character name", TestRuntimePlayerReadReturnsCharacterNameAsync),
    ("runtime kill efficiency tracks kill intervals", TestRuntimeKillEfficiencyTracksKillIntervalsAsync),
    ("file logger rotates when max size is reached", TestFileLoggerRotatesWhenMaxSizeIsReachedAsync),
    ("file logger samples noisy vmm reads", TestFileLoggerSamplesNoisyVmmReadsAsync),
    ("input key map preserves Roadhog supported HID codes", TestInputKeyMapAsync),
    ("window title formats character identity", TestWindowTitleFormatsCharacterIdentityAsync),
    ("kmbox net keyboard input validates unsupported local inputs", TestKmBoxNetKeyboardInputValidationAsync),
    ("kmbox net config store saves and loads endpoint", TestKmBoxNetConfigStoreRoundTripAsync),
    ("service options use client root environment", TestRoadhogServiceOptionsUseClientRootEnvironmentAsync),
    ("services load kmbox net config before input creation", TestRoadhogServicesLoadsKmBoxNetConfigAsync),
    ("account config stores shared path names only", TestAccountConfigStoresSharedPathNamesOnlyAsync),
    ("account config persists stationary combat position", TestAccountConfigPersistsStationaryCombatPositionAsync),
    ("stationary combat target selector keeps monsters inside radius", TestStationaryTargetSelectorAsync),
    ("stationary combat derives home from revive path endpoint", TestStationaryCombatDerivesHomeFromRevivePathEndpointAsync),
    ("stationary combat skips active filtered monsters", TestStationaryCombatSkipsActiveFilteredMonstersAsync),
    ("stationary combat state uses server object id identity", TestStationaryCombatStateUsesServerObjectIdIdentityAsync),
    ("tool bridge world parser reads aggressive monster flags", TestToolBridgeWorldParserReadsAggressiveFlagsAsync),
    ("vmm skill options group learned ranks by default", TestVmmSkillOptionsGroupLearnedRanksByDefaultAsync),
    ("stationary combat startup recovery follows nearest revive path point", TestStationaryCombatStartupRecoveryFollowsNearestRevivePointAsync),
    ("stationary combat startup recovery path jumps when stuck", TestStationaryCombatStartupRecoveryPathJumpsWhenStuckAsync),
    ("stationary combat startup recovery skips revive path when home is nearest", TestStationaryCombatStartupRecoverySkipsWhenHomeNearestAsync),
    ("stationary combat startup recovery defends when targeted", TestStationaryCombatStartupRecoveryDefendsWhenTargetedAsync),
    ("stationary combat death recovery clicks revive and recovers before path", TestStationaryCombatDeathRecoveryClicksReviveAndRecoversBeforePathAsync),
    ("stationary combat death recovery summons spiritmaster pet before revive path", TestStationaryCombatDeathRecoverySummonsSpiritmasterPetBeforeRevivePathAsync),
    ("stationary combat death recovery path defends when targeted", TestStationaryCombatDeathRecoveryPathDefendsWhenTargetedAsync),
    ("stationary combat death recovery path jumps when stuck", TestStationaryCombatDeathRecoveryPathJumpsWhenStuckAsync),
    ("worker life guard revives before semi-auto combat", TestWorkerLifeGuardRevivesBeforeSemiAutoAsync),
    ("worker life guard revives before stationary position validation", TestWorkerLifeGuardRevivesBeforeStationaryPositionValidationAsync),
    ("worker ensures spiritmaster pet before normal work", TestWorkerEnsuresSpiritmasterPetBeforeNormalWorkAsync),
    ("worker waits for spiritmaster pet summon verification", TestWorkerWaitsForSpiritmasterPetSummonVerificationAsync),
    ("stationary combat faces selected target before tab", TestStationaryCombatFacesTargetBeforeTabAsync),
    ("stationary combat target pitch follows target height", TestStationaryCombatTargetPitchFollowsTargetHeightAsync),
    ("stationary combat accepts twenty five degree pre-lock face tolerance", TestStationaryCombatAcceptsTwentyFiveDegreePreLockFaceToleranceAsync),
    ("stationary combat tabs until selected target is verified", TestStationaryCombatTabsUntilTargetVerifiedAsync),
    ("stationary combat verifies target after each tab press", TestStationaryCombatVerifiesAfterEachTabAsync),
    ("stationary combat nudges then accepts unchanged locked target after tab", TestStationaryCombatNudgesThenAcceptsUnchangedLockedTargetAfterTabAsync),
    ("stationary combat nudges forward when tab locks corpse", TestStationaryCombatNudgesForwardWhenTabLocksCorpseAsync),
    ("stationary combat pending tab verify blocks pre-acquire", TestStationaryCombatPendingTabVerifyBlocksPreAcquireAsync),
    ("stationary combat releases path follow movement after target is verified", TestStationaryCombatReleasesMovementAfterAcquireAsync),
    ("stationary combat does not pulse W while approaching same target", TestStationaryCombatDoesNotPulseWWhileApproachingAsync),
    ("stationary combat jumps while stuck approaching target", TestStationaryCombatJumpsWhileStuckApproachingTargetAsync),
    ("stationary combat ignores target when lock times out", TestStationaryCombatIgnoresTargetWhenLockTimesOutAsync),
    ("stationary combat ignores target when kill times out", TestStationaryCombatIgnoresTargetWhenKillTimesOutAsync),
    ("stationary combat keeps fight when locked target server id matches", TestStationaryCombatKeepsFightWhenLockedServerIdMatchesAsync),
    ("stationary combat keeps current fight target when lock switches", TestStationaryCombatKeepsCurrentFightTargetWhenLockSwitchesAsync),
    ("stationary combat presses C until locked target targets player", TestStationaryCombatPressesCUntilLockedTargetTargetsPlayerAsync),
    ("stationary combat switches away from target claimed by other", TestStationaryCombatSwitchesAwayFromTargetClaimedByOtherAsync),
    ("stationary combat keeps previously engaged target while it self targets", TestStationaryCombatKeepsPreviouslyEngagedTargetWhileSelfTargetingAsync),
    ("stationary combat keeps spiritmaster pet targeted fight", TestStationaryCombatKeepsSpiritmasterPetTargetedFightAsync),
    ("stationary combat treats locked zero hp target as combat", TestStationaryCombatTreatsLockedZeroHpTargetAsCombatAsync),
    ("stationary combat loots locked dead target directly", TestStationaryCombatLootsLockedDeadTargetDirectlyAsync),
    ("stationary combat waits after kill before loot key", TestStationaryCombatWaitsAfterKillBeforeLootKeyAsync),
    ("stationary combat waits near corpse after loot key", TestStationaryCombatWaitsNearCorpseAfterLootKeyAsync),
    ("stationary combat runs after-combat maintenance after loot", TestStationaryCombatRunsAfterCombatMaintenanceAfterLootAsync),
    ("stationary combat finishes current fight before returning home", TestStationaryCombatFinishesFightBeforeReturningHomeAsync),
    ("stationary combat interrupts sit when targeted by monster", TestStationaryCombatInterruptsSitWhenTargetedAsync),
    ("stationary combat hp rule runs before defense target workflow", TestStationaryCombatHpRuleRunsBeforeDefenseTargetWorkflowAsync),
    ("stationary combat stops movement before hp maintenance", TestStationaryCombatStopsMovementBeforeHpMaintenanceAsync),
    ("stationary combat mp sit maintenance runs without defense target", TestStationaryCombatMpSitMaintenanceRunsWithoutDefenseTargetAsync),
    ("skill tree assigns keys by root order and chain children inherit root key", TestSkillTreeKeyMappingAsync),
    ("available skill tree keeps chain roots in normal category", TestAvailableSkillTreeKeepsChainRootsInNormalCategoryAsync),
    ("selected skill refresh removes unavailable current skills", TestSelectedSkillRefreshRemovesUnavailableCurrentSkillsAsync),
    ("skill tree maps at most configured roots across the 24 supported keys", TestConfiguredRootKeyBoundaryAsync),
    ("combat tick presses trigger prefix then first ready root", TestCombatTickPressesPrefixThenReadyRootAsync),
    ("knockdown trigger saved as status is treated as trigger", TestKnockdownTriggerSavedAsStatusIsTreatedAsTriggerAsync),
    ("combat tick requests only configured skill ids", TestCombatTickRequestsOnlyConfiguredSkillIdsAsync),
    ("observed configured cooldown advance calibrates clock", TestObservedConfiguredCooldownAdvanceCalibratesClockAsync),
    ("uncalibrated nonzero cooldown falls back to first configured root", TestUncalibratedNonzeroCooldownFallsBackToFirstRootAsync),
    ("uncalibrated unknown cooldown rotates after failed attempt", TestUncalibratedUnknownCooldownRotatesAfterFailedAttemptAsync),
    ("calibrated nonzero cooldown skips cooling roots", TestCalibratedNonzeroCooldownSkipsCoolingRootsAsync),
    ("calibrated cooldown tolerance treats near-ready as ready", TestCalibratedCooldownToleranceTreatsNearReadyAsReadyAsync),
    ("observed cooldown survives zero end tick read", TestObservedCooldownSurvivesZeroEndTickReadAsync),
    ("opening attack key switch presses C once", TestOpeningAttackKeySwitchPressesCOnceAsync),
    ("opening skill presses before C once", TestOpeningSkillPressesBeforeCOnceAsync),
    ("opening skill uses server object id identity", TestOpeningSkillUsesServerObjectIdIdentityAsync),
    ("stale opening skill cooldown is ready before calibration", TestStaleOpeningSkillCooldownIsReadyBeforeCalibrationAsync),
    ("cooling opening skill skips to C", TestCoolingOpeningSkillSkipsToCAsync),
    ("cooling opening skill retries on same target after cooldown", TestCoolingOpeningSkillRetriesOnSameTargetAfterCooldownAsync),
    ("maintenance hp rule presses configured key before skills", TestMaintenanceHpRulePressesConfiguredKeyAsync),
    ("maintenance hp rule exits rest before configured key", TestMaintenanceHpRuleExitsRestBeforeConfiguredKeyAsync),
    ("maintenance hp rule runs without attackable target", TestMaintenanceHpRuleRunsWithoutAttackableTargetAsync),
    ("maintenance in-combat rule skips without attackable target", TestMaintenanceInCombatRuleSkipsWithoutAttackableTargetAsync),
    ("maintenance in-combat rule runs before skills", TestMaintenanceInCombatRuleRunsBeforeSkillsAsync),
    ("maintenance mp rule presses configured key before skills", TestMaintenanceMpRulePressesConfiguredKeyAsync),
    ("maintenance selected skill confirms by skill id", TestMaintenanceSelectedSkillConfirmsBySkillIdAsync),
    ("maintenance selected cooling skill skips key and continues combat", TestMaintenanceSelectedCoolingSkillSkipsKeyAsync),
    ("maintenance cooldown calibration ignores unrelated skill advance", TestMaintenanceCooldownCalibrationIgnoresUnrelatedSkillAdvanceAsync),
    ("stationary combat skips skill maintenance before cooldown calibration", TestStationaryCombatSkipsSkillMaintenanceBeforeCooldownCalibrationAsync),
    ("maintenance sit enters with comma and exits with x", TestMaintenanceSitEnterExitAsync),
    ("maintenance sit enters for low mp and exits on recovery", TestMaintenanceSitMpEnterExitAsync),
    ("maintenance sit re-enters when poison interrupts rest", TestMaintenanceSitReentersWhenInterruptedAsync),
    ("maintenance sit waits for harmful abnormal before comma", TestMaintenanceSitWaitsForHarmfulAbnormalAsync),
    ("semi auto skips sit maintenance", TestSemiAutoSkipsSitMaintenanceAsync),
    ("poll result advances root order", TestPollResultAdvancesRootOrderAsync),
    ("manual skill mapping plan uses explicit order and keys", TestManualSkillMappingPlanAsync),
    ("system skill plan uses system execution tree only", TestSystemSkillPlanAsync),
    ("spiritmaster auto switch uses dedicated plan branch", TestSpiritmasterAutoSwitchPlanAsync),
    ("spiritmaster plan reads dedicated skill ids", TestSpiritmasterPlanReadsDedicatedSkillIdsAsync),
    ("spiritmaster selector skips active dot", TestSpiritmasterSelectorSkipsActiveDotAsync),
    ("spiritmaster selector trusts target abnormal snapshot over dot window", TestSpiritmasterSelectorTrustsTargetSnapshotOverDotWindowAsync),
    ("spiritmaster selector skips command without pet", TestSpiritmasterSelectorSkipsCommandWithoutPetAsync),
    ("spiritmaster tick summons missing pet", TestSpiritmasterTickSummonsMissingPetAsync),
    ("spiritmaster tick prioritizes lowest pet hp rule", TestSpiritmasterTickPrioritizesLowestPetHpRuleAsync),
    ("spiritmaster pet hp local cooldown yields to normal skills", TestSpiritmasterPetHpLocalCooldownYieldsToNormalSkillsAsync),
    ("spiritmaster tick gates pet buff by dp", TestSpiritmasterTickGatesPetBuffByDpAsync),
    ("spiritmaster pet buff suppresses repeated unknown cooldown", TestSpiritmasterPetBuffSuppressesRepeatedUnknownCooldownAsync),
    ("spiritmaster dot learning prefers skill id", TestSpiritmasterDotLearningPrefersSkillIdAsync),
    ("spiritmaster dot hit blocks repeat on next tick", TestSpiritmasterDotHitBlocksRepeatOnNextTickAsync),
    ("spiritmaster opening attack key presses twice before opening skill", TestSpiritmasterOpeningAttackKeyPressesTwiceBeforeOpeningSkillAsync),
    ("spiritmaster waits for pressed skill cooldown before next root", TestSpiritmasterWaitsForPressedSkillCooldownBeforeNextRootAsync),
    ("spiritmaster combat runs global mp maintenance before skills", TestSpiritmasterCombatRunsGlobalMpMaintenanceBeforeSkillsAsync),
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

static async Task TestScriptProfileStoreRoundTripAsync()
{
    var directory = CreateTempDirectory("roadhog-profiles-");
    try
    {
        var store = new JsonScriptProfileStore(directory);
        var name = "test/profile";
        var settings = CreateScriptSettings();
        settings.ProfileName = name;
        settings.MainMode = AccountMainMode.CustomCombat;
        settings.CombatMode = AccountCombatMode.Stationary;
        settings.Combat.HasStationaryCombatPosition = true;
        settings.Combat.StationaryCombatX = 12.5D;

        var save = await store.SaveAsync(new ScriptProfileDocument
        {
            Name = name,
            Settings = settings
        }).ConfigureAwait(false);
        AssertFalse(!save.Success, "profile save should succeed");

        var summaries = await store.LoadSummariesAsync().ConfigureAwait(false);
        AssertFalse(!summaries.Success, "profile summaries should load");
        AssertEqual(1, summaries.Value?.Count ?? 0, "profile summary count");
        AssertEqual(name, summaries.Value![0].Name, "profile summary name");

        var loaded = await store.LoadAsync(name).ConfigureAwait(false);
        AssertFalse(!loaded.Success, "saved profile should load");
        AssertEqual(name, loaded.Value?.Settings.ProfileName ?? string.Empty, "loaded profile name");
        AssertEqual(AccountMainMode.CustomCombat, loaded.Value?.Settings.MainMode ?? AccountMainMode.SemiAuto, "loaded main mode");
        AssertEqual(12.5D, loaded.Value?.Settings.Combat.StationaryCombatX ?? 0.0D, "loaded combat x");

        var delete = await store.DeleteAsync(name).ConfigureAwait(false);
        AssertFalse(!delete.Success, "profile delete should succeed");
        summaries = await store.LoadSummariesAsync().ConfigureAwait(false);
        AssertEqual(0, summaries.Value?.Count ?? -1, "profile summary count after delete");
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

static async Task TestRuntimeSkillReadUsesSavedAccountScopeWhenIdleAsync()
{
    var logger = new InMemoryRoadhogLogger();
    var accounts = new AccountRuntimeManager(logger);
    var gameApi = new FakeGameApi
    {
        Skills = new[]
        {
            new SkillSnapshot(101, "Saved Scope Skill", 1, 1, "Saved Scope Skill", 1, false, 0, 0)
        }
    };
    var configStore = new InMemoryAccountConfigStore(new AccountConfig
    {
        AccountName = "account-scope",
        ProcessId = 812,
        TargetProcessName = "Aion.bin",
        VmmDeviceName = "fpga://devindex=1"
    });
    var runtime = new RoadhogRuntime(gameApi, logger, accounts, null!, configStore);

    var result = await runtime.RefreshSkillsAsync("account-scope").ConfigureAwait(false);

    AssertFalse(!result.Success, "runtime skill read should succeed from saved account scope");
    AssertEqual(812, gameApi.LastSkillsContext?.ProcessId ?? 0, "saved scoped process id");
    AssertEqual("Aion.bin", gameApi.LastSkillsContext?.TargetProcessName ?? string.Empty, "saved scoped process name");
    AssertEqual("fpga://devindex=1", gameApi.LastSkillsContext?.VmmDeviceName ?? string.Empty, "saved scoped vmm device");
}

static async Task TestRuntimeSkillReadMapsSavedHardwareKeyToIndexedFpgaDeviceAsync()
{
    var logger = new InMemoryRoadhogLogger();
    var accounts = new AccountRuntimeManager(logger);
    var gameApi = new FakeGameApi
    {
        Skills = new[]
        {
            new SkillSnapshot(102, "Mapped Scope Skill", 1, 1, "Mapped Scope Skill", 1, false, 0, 0)
        }
    };
    var configStore = new InMemoryAccountConfigStore(new AccountConfig
    {
        AccountName = "account-scope",
        HardwareKey = "port:Port_#0004.Hub_#0002",
        ProcessId = 813,
        TargetProcessName = "Aion.bin",
        VmmDeviceName = "fpga"
    });
    var hardwareResolver = new InMemoryHardwareDeviceResolver(new HardwareDeviceFeature(
        "port:Port_#0004.Hub_#0002",
        "usb-port",
        "medium",
        "USB\\VID_0403&PID_601F\\0000",
        "USB\\VID_0403&PID_601F\\0000",
        "{container}",
        "USB\\VID_0403&PID_601F",
        "port:Port_#0004.Hub_#0002",
        "fpga FTDI FT601 USB 3.0 Bridge Device",
        "FTDI",
        "fpga://devindex=1",
        new[] { "port:Port_#0004.Hub_#0002" }));
    var runtime = new RoadhogRuntime(gameApi, logger, accounts, null!, configStore, hardwareResolver);

    var result = await runtime.RefreshSkillsAsync("account-scope").ConfigureAwait(false);

    AssertFalse(!result.Success, "runtime skill read should succeed from mapped hardware scope");
    AssertEqual("fpga://devindex=1", gameApi.LastSkillsContext?.VmmDeviceName ?? string.Empty, "mapped scoped vmm device");
}

static async Task TestAccountStartPreservesConfiguredIndexedFpgaDeviceAsync()
{
    var logger = new InMemoryRoadhogLogger();
    var accounts = new AccountRuntimeManager(logger);
    var hardwareResolver = new InMemoryHardwareDeviceResolver(new HardwareDeviceFeature(
        "port:Port_#0004.Hub_#0002",
        "usb-port",
        "medium",
        "USB\\VID_0403&PID_601F\\0000",
        "USB\\VID_0403&PID_601F\\0000",
        "{container}",
        "USB\\VID_0403&PID_601F",
        "port:Port_#0004.Hub_#0002",
        "fpga FTDI FT601 USB 3.0 Bridge Device",
        "FTDI",
        "fpga",
        new[] { "port:Port_#0004.Hub_#0002" }));
    var orchestrator = new AccountOrchestrator(
        new FakeGameApi(),
        logger,
        accounts,
        hardwareResolver,
        new InMemoryTargetProcessResolver(),
        new CapturingAccountWorkerLoop(),
        new AccountWorkerOptions());

    var result = orchestrator.Start(new AccountConfig
    {
        AccountName = "account-scope",
        HardwareKey = "port:Port_#0004.Hub_#0002",
        VmmDeviceName = "fpga://devindex=1",
        Enabled = true
    });

    await Task.Delay(50).ConfigureAwait(false);

    AssertFalse(!result.Success, "account start should succeed");
    var bound = logger.Entries.LastOrDefault(entry => entry.EventName == "account.hardware.bound");
    AssertFalse(bound is null, "account hardware binding should be logged");
    AssertEqual("fpga://devindex=1", Convert.ToString(bound!.Fields["vmmDevice"]) ?? string.Empty, "bound vmm device should preserve indexed config");
}

static async Task TestAccountStartConfiguredVmmOverridesHardwareIndexedDeviceAsync()
{
    var logger = new InMemoryRoadhogLogger();
    var accounts = new AccountRuntimeManager(logger);
    var hardwareResolver = new InMemoryHardwareDeviceResolver(new HardwareDeviceFeature(
        "port:Port_#0005.Hub_#0004",
        "usb-port",
        "medium",
        "USB\\VID_0403&PID_601F\\0001",
        "USB\\VID_0403&PID_601F\\0001",
        "{container}",
        "USB\\VID_0403&PID_601F",
        "port:Port_#0005.Hub_#0004",
        "fpga FTDI FT601 USB 3.0 Bridge Device",
        "FTDI",
        "fpga://devindex=1",
        new[] { "port:Port_#0005.Hub_#0004" }));
    var orchestrator = new AccountOrchestrator(
        new FakeGameApi(),
        logger,
        accounts,
        hardwareResolver,
        new InMemoryTargetProcessResolver(),
        new CapturingAccountWorkerLoop(),
        new AccountWorkerOptions());

    var result = orchestrator.Start(new AccountConfig
    {
        AccountName = "account-scope",
        HardwareKey = "port:Port_#0005.Hub_#0004",
        VmmDeviceName = "fpga://devindex=0",
        Enabled = true
    });

    await Task.Delay(50).ConfigureAwait(false);

    AssertFalse(!result.Success, "account start should succeed");
    var bound = logger.Entries.LastOrDefault(entry => entry.EventName == "account.hardware.bound");
    AssertFalse(bound is null, "account hardware binding should be logged");
    AssertEqual("fpga://devindex=0", Convert.ToString(bound!.Fields["vmmDevice"]) ?? string.Empty, "configured vmm should override hardware mapped vmm");
}

static async Task TestRuntimeWorldObjectReadUsesAccountScopeAsync()
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
        WorldObjects = new[]
        {
            new WorldObjectSnapshot(100, 100, "normal target", "monster", new Vector3Snapshot(1, 2, 3), 4, 100, 100)
        }
    };
    var runtime = new RoadhogRuntime(gameApi, logger, accounts, null!);

    var result = await runtime.RefreshWorldObjectsAsync("account-scope").ConfigureAwait(false);

    AssertFalse(!result.Success, "runtime world object read should succeed");
    AssertEqual(712, gameApi.LastWorldObjectsContext?.ProcessId ?? 0, "scoped process id");
    AssertEqual("Aion.bin", gameApi.LastWorldObjectsContext?.TargetProcessName ?? string.Empty, "scoped process name");
    AssertEqual("fpga", gameApi.LastWorldObjectsContext?.VmmDeviceName ?? string.Empty, "scoped vmm device");
}

static async Task TestRuntimeSummonedPetReadUsesAccountScopeAsync()
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
        SummonedPet = new SummonedPetSnapshot(
            true,
            65522,
            2160282797,
            46,
            SummonedPetSnapshot.ActorObjectType,
            201035,
            "火之精灵",
            "Dark_Summon_FireElemental_G4",
            "Summon_Pet",
            "Pet_Dark",
            50,
            6870,
            6870,
            100,
            new Vector3Snapshot(1, 2, 3),
            2.95,
            1711370025,
            DateTimeOffset.Now,
            2160282797,
            true,
            "local-link+owner+static-summon-pet")
    };
    var runtime = new RoadhogRuntime(gameApi, logger, accounts, null!);

    var result = await runtime.ReadSummonedPetAsync("account-scope").ConfigureAwait(false);

    AssertFalse(!result.Success, "runtime summoned pet read should succeed");
    AssertFalse(result.Value is null || !result.Value.IsSummoned, "summoned pet should be present");
    AssertEqual(712, gameApi.LastSummonedPetContext?.ProcessId ?? 0, "scoped process id");
    AssertEqual("Aion.bin", gameApi.LastSummonedPetContext?.TargetProcessName ?? string.Empty, "scoped process name");
    AssertEqual("fpga", gameApi.LastSummonedPetContext?.VmmDeviceName ?? string.Empty, "scoped vmm device");
}

static async Task TestRuntimeSummonedPetRosterReadUsesAccountScopeAsync()
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

    var capturedAt = DateTimeOffset.Now;
    var localPet = new OwnedSummonedPetSnapshot(
        SummonedPetOwnerKind.LocalPlayer,
        1711370025,
        "local",
        string.Empty,
        new SummonedPetSnapshot(
            true,
            65522,
            2160282797,
            46,
            SummonedPetSnapshot.ActorObjectType,
            201035,
            "鐏箣绮剧伒",
            "Dark_Summon_FireElemental_G4",
            "Summon_Pet",
            "Pet_Dark",
            50,
            6870,
            6870,
            100,
            new Vector3Snapshot(1, 2, 3),
            2.95,
            1711370025,
            capturedAt,
            2160282797,
            true,
            "local-link+owner+static-summon-pet"),
        1,
        new[] { new AbnormalStatusEntrySnapshot(0, 424, 2, 0, 1, 0) });
    var partyPet = new OwnedSummonedPetSnapshot(
        SummonedPetOwnerKind.PartyMember,
        1234,
        "party",
        "primary",
        new SummonedPetSnapshot(
            true,
            65510,
            2160000010,
            46,
            SummonedPetSnapshot.ActorObjectType,
            201019,
            "鍦颁箣绮剧伒",
            "Dark_Summon_EarthElemental_G3",
            "Summon_Pet",
            "Pet_Dark",
            48,
            9000,
            9328,
            96,
            new Vector3Snapshot(4, 5, 6),
            8.5,
            1711370025,
            capturedAt,
            0,
            true,
            "owner+static-summon-pet"),
        0,
        Array.Empty<AbnormalStatusEntrySnapshot>());

    var gameApi = new FakeGameApi
    {
        SummonedPetRoster = new SummonedPetRosterSnapshot(
            1711370025,
            2160282797,
            capturedAt,
            localPet,
            new[] { partyPet },
            new uint[] { 1234 })
    };
    var runtime = new RoadhogRuntime(gameApi, logger, accounts, null!);

    var result = await runtime.ReadSummonedPetRosterAsync("account-scope").ConfigureAwait(false);

    AssertFalse(!result.Success, "runtime summoned pet roster read should succeed");
    AssertFalse(result.Value is null || !result.Value.LocalPlayerPet.IsSummoned, "local pet should be present");
    AssertEqual(1, result.Value?.PartyMemberPetCount ?? 0, "party pet count");
    AssertEqual(712, gameApi.LastSummonedPetRosterContext?.ProcessId ?? 0, "scoped process id");
    AssertEqual("Aion.bin", gameApi.LastSummonedPetRosterContext?.TargetProcessName ?? string.Empty, "scoped process name");
    AssertEqual("fpga", gameApi.LastSummonedPetRosterContext?.VmmDeviceName ?? string.Empty, "scoped vmm device");
}

static async Task TestRuntimeLockedTargetAbnormalReadUsesAccountScopeAsync()
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

    var capturedAt = DateTimeOffset.Now;
    var target = new LockedTargetSnapshot(
        100,
        2160282797,
        3,
        LockedTargetSnapshot.MonsterObjectType,
        "训练用稻草人",
        1000,
        1000,
        new Vector3Snapshot(1, 2, 3),
        8.5D,
        capturedAt,
        1711370025,
        true,
        1711370025);
    var gameApi = new FakeGameApi
    {
        LockedTargetAbnormalStatuses = new LockedTargetAbnormalStatusSnapshot(
            target,
            1,
            new[] { new AbnormalStatusEntrySnapshot(0, 113582, 2, 0, 1, 0x1234) },
            capturedAt)
    };
    var runtime = new RoadhogRuntime(gameApi, logger, accounts, null!);

    var result = await runtime.ReadLockedTargetAbnormalStatusesAsync("account-scope").ConfigureAwait(false);

    AssertFalse(!result.Success, "runtime locked target abnormal read should succeed");
    AssertFalse(result.Value is null || !result.Value.HasAbnormalId(113582), "target abnormal id should be present");
    AssertEqual(712, gameApi.LastLockedTargetAbnormalContext?.ProcessId ?? 0, "scoped process id");
    AssertEqual("Aion.bin", gameApi.LastLockedTargetAbnormalContext?.TargetProcessName ?? string.Empty, "scoped process name");
    AssertEqual("fpga", gameApi.LastLockedTargetAbnormalContext?.VmmDeviceName ?? string.Empty, "scoped vmm device");
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
            DateTimeOffset.Now,
            Level: 25,
            CharacterClass: "Cleric")
    };
    var runtime = new RoadhogRuntime(gameApi, logger, new AccountRuntimeManager(logger), null!);

    var result = await runtime.ReadPlayerAsync("account-character").ConfigureAwait(false);

    AssertFalse(!result.Success, "runtime player read should succeed");
    AssertEqual("测试角色", result.Value?.CharacterName ?? string.Empty, "character name");
    AssertEqual((ushort)25, result.Value?.Level ?? 0, "character level");
    AssertEqual("Cleric", result.Value?.CharacterClass ?? string.Empty, "character class");
}

static Task TestAionClassCatalogAsync()
{
    AssertFalse(!AionClassCatalog.TryFromRaw(8, out var spiritmaster), "class id 8 should be valid");
    AssertEqual(AionClassId.Spiritmaster, spiritmaster, "class id 8 mapping");
    AssertEqual("精灵星", AionClassCatalog.GetChineseName(spiritmaster), "class id 8 chinese name");
    AssertFalse(AionClassCatalog.TryFromRaw(12, out _), "class id 12 should be invalid for this client enum");

    var snapshot = new PlayerSnapshot(
        1,
        0,
        "Fake",
        100,
        100,
        100,
        100,
        0,
        new Vector3Snapshot(0, 0, 0),
        DateTimeOffset.Now,
        Level: 50,
        CharacterClass: AionClassCatalog.GetChineseName(AionClassId.Spiritmaster),
        CharacterClassId: AionClassId.Spiritmaster);

    AssertFalse(!snapshot.IsSpiritmaster, "player snapshot should expose spiritmaster predicate");
    return Task.CompletedTask;
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

static Task TestFileLoggerSamplesNoisyVmmReadsAsync()
{
    var directory = CreateTempDirectory("roadhog-logs-");
    try
    {
        var logger = new FileRoadhogLogger(directory);
        for (var index = 0; index < 5; index++)
        {
            logger.Info("vmm.player.read", new Dictionary<string, object?>
            {
                ["account"] = "account1",
                ["hp"] = 100 - index
            });
        }

        for (var index = 0; index < 3; index++)
        {
            logger.Info("semi_auto.key.pressed", new Dictionary<string, object?>
            {
                ["account"] = "account1",
                ["key"] = "D1",
                ["phase"] = "skill",
                ["skill"] = "skill1"
            });
        }

        logger.Info("semi_auto.key.pressed", new Dictionary<string, object?>
        {
            ["account"] = "account1",
            ["key"] = "D2",
            ["phase"] = "skill",
            ["skill"] = "skill2"
        });
        logger.Warn("vmm.player.read", new Dictionary<string, object?>
        {
            ["account"] = "account1",
            ["error"] = "read failed"
        });

        var latestPath = Path.Combine(directory, "latest.log");
        var text = File.ReadAllText(latestPath);

        AssertEqual(1, CountOccurrences(text, "\"EventName\":\"vmm.player.read\",\"Fields\":{\"account\":\"account1\",\"hp\""), "noisy vmm player reads should be sampled");
        AssertEqual(1, CountOccurrences(text, "\"EventName\":\"semi_auto.key.pressed\",\"Fields\":{\"account\":\"account1\",\"key\":\"D1\",\"phase\":\"skill\",\"skill\":\"skill1\""), "repeated key press reads should be sampled");
        AssertFalse(!text.Contains("\"EventName\":\"semi_auto.key.pressed\",\"Fields\":{\"account\":\"account1\",\"key\":\"D2\",\"phase\":\"skill\",\"skill\":\"skill2\"", StringComparison.Ordinal), "different key press should use a separate sample key");
        AssertFalse(!text.Contains("\"Level\":\"warn\",\"EventName\":\"vmm.player.read\"", StringComparison.Ordinal), "warn event should bypass noisy sampling");
    }
    finally
    {
        DeleteDirectoryIfExists(directory);
    }

    return Task.CompletedTask;
}

static Task TestInputKeyMapAsync()
{
    AssertHidCode("C", 0x06);
    AssertHidCode("S", 0x16);
    AssertHidCode(" W ", 0x1A);
    AssertHidCode("Space", 0x2C);
    AssertHidCode("D1", 0x1E);
    AssertHidCode("D0", 0x27);
    AssertHidCode("OemMinus", 0x2D);
    AssertHidCode("OemPlus", 0x2E);
    AssertHidCode("OemComma", 0x36);
    AssertHidCode("Tab", 0x2B);
    AssertHidCode("F9", 0x42);
    AssertHidCode("NumPad0", 0x62);
    AssertHidCode("NumPadSubtract", 0x56);
    AssertHidCode("NumPadAdd", 0x57);
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

static Task TestWindowTitleFormatsCharacterIdentityAsync()
{
    var title = RoadhogWindowTitleFormatter.Build(
        "port:Port_#0004.Hub_#000",
        "192.168.2.188:4967/5BF7E466");

    AssertEqual("GreenPlayer", title, "base title");
    AssertEqual("GreenPlayer 路哥", RoadhogWindowTitleFormatter.Build("GreenPlayer", "port:Port_#0004.Hub_#000", "192.168.2.188:4967/5BF7E466", "路哥"), "title with character name");
    AssertEqual("GreenPlayer", RoadhogWindowTitleFormatter.Build("", "(unconfigured)"), "unconfigured title");

    return Task.CompletedTask;
}

static async Task TestKmBoxNetConfigStoreRoundTripAsync()
{
    var directory = CreateTempDirectory("roadhog-kmbox-net-");
    try
    {
        var path = Path.Combine(directory, "kmbox-net.json");
        var store = new JsonKmBoxNetDeviceConfigStore(path);
        var config = new KmBoxNetDeviceConfig
        {
            IpAddress = "192.168.2.188",
            Port = 4967,
            Mac = "5BF7E466"
        };

        var save = await store.SaveAsync(config).ConfigureAwait(false);
        AssertFalse(!save.Success, "kmbox net config save should succeed");

        var load = await store.LoadAsync().ConfigureAwait(false);
        AssertFalse(!load.Success, "kmbox net config load should succeed");
        AssertEqual("192.168.2.188", load.Value?.IpAddress ?? string.Empty, "kmbox net ip");
        AssertEqual(4967, load.Value?.Port ?? 0, "kmbox net port");
        AssertEqual("5BF7E466", load.Value?.Mac ?? string.Empty, "kmbox net mac");
    }
    finally
    {
        DeleteDirectoryIfExists(directory);
    }
}

static Task TestRoadhogServiceOptionsUseClientRootEnvironmentAsync()
{
    var directory = CreateTempDirectory("roadhog-client-root-");
    var previousClientRoot = Environment.GetEnvironmentVariable(RoadhogServiceOptions.ClientRootEnvironmentVariable);
    var previousConfigRoot = Environment.GetEnvironmentVariable(RoadhogServiceOptions.ConfigRootEnvironmentVariable);
    var previousAccountConfig = Environment.GetEnvironmentVariable(RoadhogServiceOptions.AccountConfigPathEnvironmentVariable);
    var previousPathLibrary = Environment.GetEnvironmentVariable(RoadhogServiceOptions.PathLibraryDirectoryEnvironmentVariable);
    var previousProfileLibrary = Environment.GetEnvironmentVariable(RoadhogServiceOptions.ProfileLibraryDirectoryEnvironmentVariable);
    var previousKmBoxConfig = Environment.GetEnvironmentVariable(RoadhogServiceOptions.KmBoxNetConfigPathEnvironmentVariable);
    var previousLogDirectory = Environment.GetEnvironmentVariable(RoadhogServiceOptions.LogDirectoryEnvironmentVariable);
    try
    {
        Environment.SetEnvironmentVariable(RoadhogServiceOptions.ClientRootEnvironmentVariable, directory);
        Environment.SetEnvironmentVariable(RoadhogServiceOptions.ConfigRootEnvironmentVariable, null);
        Environment.SetEnvironmentVariable(RoadhogServiceOptions.AccountConfigPathEnvironmentVariable, null);
        Environment.SetEnvironmentVariable(RoadhogServiceOptions.PathLibraryDirectoryEnvironmentVariable, null);
        Environment.SetEnvironmentVariable(RoadhogServiceOptions.ProfileLibraryDirectoryEnvironmentVariable, null);
        Environment.SetEnvironmentVariable(RoadhogServiceOptions.KmBoxNetConfigPathEnvironmentVariable, null);
        Environment.SetEnvironmentVariable(RoadhogServiceOptions.LogDirectoryEnvironmentVariable, null);

        var options = RoadhogServiceOptions.FromEnvironment();

        AssertEqual(Path.Combine(directory, "config", "accounts.json"), options.AccountConfigPath, "client root account config path");
        AssertEqual(Path.Combine(directory, "config", "paths"), options.PathLibraryDirectory, "client root path library");
        AssertEqual(Path.Combine(directory, "config", "profiles"), options.ProfileLibraryDirectory, "client root profile library");
        AssertEqual(Path.Combine(directory, "config", "kmbox-net.json"), options.KmBoxNetConfigPath, "client root kmbox config path");
        AssertEqual(Path.Combine(directory, "logs"), options.LogDirectory, "client root log directory");
    }
    finally
    {
        Environment.SetEnvironmentVariable(RoadhogServiceOptions.ClientRootEnvironmentVariable, previousClientRoot);
        Environment.SetEnvironmentVariable(RoadhogServiceOptions.ConfigRootEnvironmentVariable, previousConfigRoot);
        Environment.SetEnvironmentVariable(RoadhogServiceOptions.AccountConfigPathEnvironmentVariable, previousAccountConfig);
        Environment.SetEnvironmentVariable(RoadhogServiceOptions.PathLibraryDirectoryEnvironmentVariable, previousPathLibrary);
        Environment.SetEnvironmentVariable(RoadhogServiceOptions.ProfileLibraryDirectoryEnvironmentVariable, previousProfileLibrary);
        Environment.SetEnvironmentVariable(RoadhogServiceOptions.KmBoxNetConfigPathEnvironmentVariable, previousKmBoxConfig);
        Environment.SetEnvironmentVariable(RoadhogServiceOptions.LogDirectoryEnvironmentVariable, previousLogDirectory);
        DeleteDirectoryIfExists(directory);
    }

    return Task.CompletedTask;
}

static async Task TestRoadhogServicesLoadsKmBoxNetConfigAsync()
{
    var directory = CreateTempDirectory("roadhog-kmbox-services-");
    try
    {
        var configPath = Path.Combine(directory, "config", "kmbox-net.json");
        var store = new JsonKmBoxNetDeviceConfigStore(configPath);
        var save = await store.SaveAsync(new KmBoxNetDeviceConfig
        {
            IpAddress = "192.168.2.199",
            Port = 5001,
            Mac = "AABBCCDD"
        }).ConfigureAwait(false);
        AssertFalse(!save.Success, "service kmbox config save should succeed");

        var options = new RoadhogServiceOptions
        {
            UseMockGameApi = true,
            KmBoxNetConfigPath = configPath,
            AccountConfigPath = Path.Combine(directory, "config", "accounts.json"),
            PathLibraryDirectory = Path.Combine(directory, "config", "paths"),
            LogDirectory = Path.Combine(directory, "logs")
        };

        using var services = RoadhogServices.Create(options);
        AssertEqual("192.168.2.199:5001/AABBCCDD", services.KeyboardDeviceText, "service kmbox endpoint");
        AssertEqual("192.168.2.199", services.KmBoxNetConfig.IpAddress, "service kmbox ip");
        AssertEqual(5001, services.KmBoxNetConfig.Port, "service kmbox port");
        AssertEqual("AABBCCDD", services.KmBoxNetConfig.Mac, "service kmbox mac");
    }
    finally
    {
        DeleteDirectoryIfExists(directory);
    }
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
                    PreferAggressiveMonsters = true,
                    HasStationaryCombatPosition = true,
                    StationaryCombatX = 1307.758D,
                    StationaryCombatY = 2844.230D,
                    StationaryCombatZ = 259.832D,
                    StationaryCombatRadius = 42.5D,
                    CameraYawPixelsPerDegree = 11.5D,
                    CameraPitchPixelsPerDegree = 13.25D
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
        AssertEqual(11.5D, combat.CameraYawPixelsPerDegree, "camera yaw pixels per degree");
        AssertEqual(13.25D, combat.CameraPitchPixelsPerDegree, "camera pitch pixels per degree");
        AssertFalse(!combat.PreferAggressiveMonsters, "prefer aggressive monsters should persist");

        var clone = account.ScriptSettings.Combat.Clone();
        AssertFalse(!clone.HasStationaryCombatPosition, "stationary combat position flag should clone");
        AssertEqual(1307.758D, clone.StationaryCombatX, "cloned stationary x");
        AssertEqual(42.5D, clone.StationaryCombatRadius, "cloned stationary radius");
        AssertEqual(11.5D, clone.CameraYawPixelsPerDegree, "cloned camera yaw pixels per degree");
        AssertEqual(13.25D, clone.CameraPitchPixelsPerDegree, "cloned camera pitch pixels per degree");
        AssertFalse(!clone.PreferAggressiveMonsters, "prefer aggressive monsters should clone");
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

    var activeSelected = StationaryCombatTargetSelector.SelectNearest(
        new[]
        {
            new WorldObjectSnapshot(20, 20, "passive-near", "monster", new Vector3Snapshot(3, 0, 0), 3, 100, 100, AggressiveKnown: true, IsAggressiveToPlayer: false),
            new WorldObjectSnapshot(21, 21, "active-far", "monster", new Vector3Snapshot(8, 0, 0), 8, 100, 100, AggressiveKnown: true, IsAggressiveToPlayer: true)
        },
        player,
        home,
        30,
        preferAggressiveMonsters: true);

    AssertEqual((ushort)21, activeSelected?.EntityId ?? 0, "aggressive monster should outrank nearer passive monster");
    return Task.CompletedTask;
}

static async Task TestStationaryCombatDerivesHomeFromRevivePathEndpointAsync()
{
    var settings = CreateScriptSettings();
    settings.MainMode = AccountMainMode.CustomCombat;
    settings.CombatMode = AccountCombatMode.Stationary;
    settings.Paths.RevivePathName = "revive-home";
    settings.Combat = new CombatScriptSettings
    {
        HasStationaryCombatPosition = true,
        StationaryCombatX = 0,
        StationaryCombatY = 0,
        StationaryCombatZ = 0,
        StationaryCombatRadius = 12
    };

    var pathStore = new InMemorySharedPathStore(
        CreatePath("revive-home",
            new Vector3Snapshot(10, 0, 0),
            new Vector3Snapshot(100, 0, 0)));
    var keyboard = new RecordingKeyboardInput();
    var logger = new InMemoryRoadhogLogger();
    var gameApi = new FakeGameApi
    {
        Player = new PlayerSnapshot(1, 0, "Fake", 100, 100, 100, 100, 0, new Vector3Snapshot(100, 0, 0), DateTimeOffset.Now, 90, 10, 90),
        TargetEntityId = 0,
        TargetCurrentHp = 1000,
        TargetMaxHp = 1000,
        TargetPosition = new Vector3Snapshot(105, 0, 0),
        WorldObjects = new[]
        {
            new WorldObjectSnapshot(100, 100, "legacy-home-target", "monster", new Vector3Snapshot(5, 0, 0), 95, 1000, 1000),
            new WorldObjectSnapshot(200, 200, "revive-home-target", "monster", new Vector3Snapshot(105, 0, 0), 5, 1000, 1000)
        },
        Skills = CreateSkillSnapshotsById(new Dictionary<uint, uint>
        {
            [1] = 0,
            [5] = 0,
            [6] = 0
        })
    };
    var semiAuto = new SemiAutoCombatController(keyboard);
    var controller = new StationaryCombatController(keyboard, semiAuto, pathStore);
    var state = new StationaryCombatState();

    await controller
        .TickAsync(CreateContext(settings, gameApi, logger), SemiAutoSkillPlan.FromSettings(settings.Skills), new SemiAutoCombatState(), state)
        .ConfigureAwait(false);

    AssertEqual((ushort)200, state.CandidateEntityId, "revive path endpoint should override legacy stationary position");
    AssertFalse(logger.Entries.Any(entry => entry.EventName == "stationary_combat.position.missing"), "revive path endpoint should satisfy stationary home");
}

static async Task TestStationaryCombatSkipsActiveFilteredMonstersAsync()
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
        StationaryCombatRadius = 30,
        ActiveMonsterNameFilters = new List<string> { "stealth watcher" }
    };

    var keyboard = new RecordingKeyboardInput();
    var logger = new InMemoryRoadhogLogger();
    var gameApi = new FakeGameApi
    {
        Player = new PlayerSnapshot(1, 0, "Fake", 100, 100, 100, 100, 0, new Vector3Snapshot(0, 0, 0), DateTimeOffset.Now, 90, 10, 90),
        TargetEntityId = 0,
        TargetCurrentHp = 0,
        TargetMaxHp = 0,
        TargetPosition = null,
        WorldObjects = new[]
        {
            new WorldObjectSnapshot(100, 100, "stealth watcher", "monster", new Vector3Snapshot(4, 0, 0), 4, 1000, 1000),
            new WorldObjectSnapshot(101, 101, "normal target", "monster", new Vector3Snapshot(8, 0, 0), 8, 1000, 1000)
        }
    };
    var controller = new StationaryCombatController(keyboard, new SemiAutoCombatController(keyboard));
    var plan = SemiAutoSkillPlan.FromSettings(settings.Skills);
    var state = new StationaryCombatState();
    var semiAutoState = new SemiAutoCombatState();
    var context = CreateContext(settings, gameApi, logger);

    await controller.TickAsync(context, plan, semiAutoState, state).ConfigureAwait(false);

    AssertEqual((ushort)101, state.CandidateEntityId, "active selection should skip filtered monster name");

    var defenseState = new StationaryCombatState();
    gameApi.WorldObjects = new[]
    {
        new WorldObjectSnapshot(100, 100, "stealth watcher", "monster", new Vector3Snapshot(4, 0, 0), 4, 1000, 1000, 1, true)
    };

    await controller.TickAsync(context, plan, semiAutoState, defenseState).ConfigureAwait(false);

    AssertEqual((ushort)100, defenseState.CandidateEntityId, "filtered monster targeting player should still be defended");
}

static Task TestStationaryCombatStateUsesServerObjectIdIdentityAsync()
{
    var state = new StationaryCombatState();
    var firstAt = new DateTimeOffset(2026, 7, 1, 12, 0, 0, TimeSpan.Zero);
    var serverObjectId = 2246150598u;

    AssertFalse(!state.MarkCandidate(65498, serverObjectId, firstAt), "first candidate should be marked as changed");
    AssertFalse(
        state.MarkCandidate(65519, serverObjectId, firstAt.AddSeconds(1)),
        "same server object id should keep target identity even when entity id changes");
    AssertEqual((ushort)65519, state.CandidateEntityId, "candidate entity id should still refresh");
    AssertEqual(serverObjectId, state.CandidateServerObjectId, "candidate server object id");

    state.SetCurrentTarget(65498, serverObjectId);
    var locked = new LockedTargetSnapshot(
        65519,
        serverObjectId,
        0,
        LockedTargetSnapshot.MonsterObjectType,
        "same-server",
        1000,
        1000,
        new Vector3Snapshot(8, 0, 0),
        8,
        firstAt);
    AssertFalse(!state.IsCurrentTarget(locked), "current target should match by server object id");

    state.IgnoreTarget(65498, serverObjectId);
    var world = new WorldObjectSnapshot(
        65519,
        serverObjectId,
        "same-server",
        "monster",
        new Vector3Snapshot(8, 0, 0),
        8,
        1000,
        1000);
    AssertFalse(!state.IsTargetIgnored(world), "ignored target should match by server object id");
    return Task.CompletedTask;
}

static Task TestToolBridgeWorldParserReadsAggressiveFlagsAsync()
{
    var parserType = typeof(JsonAccountConfigStore).Assembly.GetType("Roadhog.Infrastructure.ToolBridge.ToolOutputParsers");
    AssertFalse(parserType is null, "tool output parser type should exist");
    var parseMethod = parserType!.GetMethod(
        "ParseWorldObjects",
        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
    AssertFalse(parseMethod is null, "world object parser method should exist");

    var lines = new[]
    {
        "#01 Dist=5.00 EntityId=100 ServerId=200 TargetServerId=0 TargetingMe=no CEntityType=3 Entity=0x1 Actor=0x2 ObjType=2 TemplateId=300 StaticName=\"Passive\" NpcType=monster UiType=attack Cursor=attack Tribe=PassiveMonster IsMonster=yes Level=10 HP=100/100 HpPercent=100 Alive=yes Locked=no Aggressive=no(tribe_relation) Name=\"Passive\" Pos=X=1.00 Y=2.00 Z=3.00 Offset=0x10"
    };
    var objects = (IReadOnlyList<WorldObjectSnapshot>)parseMethod!.Invoke(null, new object[] { lines })!;

    AssertEqual(1, objects.Count, "one parsed monster");
    AssertFalse(!objects[0].AggressiveKnown, "aggressive flag should be known");
    AssertFalse(objects[0].IsAggressiveToPlayer, "passive monster should not be aggressive to player");
    AssertFalse(!objects[0].IsPassiveToPlayer, "passive monster property should be true");
    AssertEqual("tribe_relation", objects[0].AggressiveSource ?? string.Empty, "aggressive source");
    return Task.CompletedTask;
}

static Task TestVmmSkillOptionsGroupLearnedRanksByDefaultAsync()
{
    var optionsType = typeof(JsonAccountConfigStore).Assembly.GetType("Roadhog.Infrastructure.Vmm.AionVmmGameApiOptions");
    AssertFalse(optionsType is null, "vmm options type should exist");
    var options = Activator.CreateInstance(optionsType!);
    var groupByDisplayName = (bool)optionsType!.GetProperty("GroupByDisplayName")!.GetValue(options)!;
    var filterUtilitySkills = (bool)optionsType.GetProperty("FilterUtilitySkills")!.GetValue(options)!;

    AssertFalse(!groupByDisplayName, "skill refresh should collapse learned ranks by default");
    AssertFalse(!filterUtilitySkills, "skill refresh should still filter utility skills by default");
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

static async Task TestStationaryCombatStartupRecoveryPathJumpsWhenStuckAsync()
{
    var previousStuckMs = Environment.GetEnvironmentVariable("ROADHOG_DEATH_REVIVE_PATH_STUCK_MS");
    var previousStuckDistance = Environment.GetEnvironmentVariable("ROADHOG_DEATH_REVIVE_PATH_STUCK_DISTANCE");
    var previousJumpHold = Environment.GetEnvironmentVariable("ROADHOG_DEATH_REVIVE_PATH_JUMP_HOLD_MS");
    var previousBearingMode = Environment.GetEnvironmentVariable("AION_FACE_TARGET_BEARING_MODE");
    Environment.SetEnvironmentVariable("ROADHOG_DEATH_REVIVE_PATH_STUCK_MS", "20");
    Environment.SetEnvironmentVariable("ROADHOG_DEATH_REVIVE_PATH_STUCK_DISTANCE", "0.5");
    Environment.SetEnvironmentVariable("ROADHOG_DEATH_REVIVE_PATH_JUMP_HOLD_MS", "1");
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
                new Vector3Snapshot(100, 0, 0)));
        var keyboard = new RecordingKeyboardInput();
        var logger = new InMemoryRoadhogLogger();
        var gameApi = new FakeGameApi
        {
            Player = new PlayerSnapshot(1, 0, "Fake", 100, 100, 100, 100, 0, new Vector3Snapshot(0, 0, 0), DateTimeOffset.Now, 90, 10, 90),
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

        AssertFalse(!stationaryState.StartupRecoveryActive, "startup recovery should stay active");
        AssertEqual(1, stationaryState.StartupRecoveryPointIndex, "startup recovery should track the first distant point");
        AssertFalse(!keyboard.KeyDowns.Contains("W"), "first startup stuck-tracking tick should hold W");
        AssertFalse(keyboard.Keys.Contains("Space"), "first startup stuck-tracking tick should not jump before threshold");

        await Task.Delay(30).ConfigureAwait(false);
        await controller.TickAsync(context, plan, semiAutoState, stationaryState).ConfigureAwait(false);

        AssertFalse(!stationaryState.IsMovingForward, "startup stuck jump should keep W held");
        AssertFalse(!keyboard.Keys.Contains("Space"), "stuck startup recovery should press Space");
        AssertFalse(keyboard.KeyUps.Contains("W"), "startup stuck jump must not release W");
        AssertFalse(!logger.Entries.Any(entry => entry.EventName == "stationary_combat.startup_recovery.path_stuck_jump"),
            "startup stuck jump should be logged");
    }
    finally
    {
        Environment.SetEnvironmentVariable("ROADHOG_DEATH_REVIVE_PATH_STUCK_MS", previousStuckMs);
        Environment.SetEnvironmentVariable("ROADHOG_DEATH_REVIVE_PATH_STUCK_DISTANCE", previousStuckDistance);
        Environment.SetEnvironmentVariable("ROADHOG_DEATH_REVIVE_PATH_JUMP_HOLD_MS", previousJumpHold);
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
            new Vector3Snapshot(50, 0, 0),
            new Vector3Snapshot(100, 0, 0)));
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

static async Task TestStationaryCombatStartupRecoveryDefendsWhenTargetedAsync()
{
    var settings = CreateScriptSettings();
    settings.MainMode = AccountMainMode.CustomCombat;
    settings.CombatMode = AccountCombatMode.Stationary;
    settings.Paths.RevivePathName = "revive-a";
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
        Player = new PlayerSnapshot(1, 0, "Fake", 100, 100, 100, 100, 0, new Vector3Snapshot(1, 0, 0), DateTimeOffset.Now, 90, 10, 90),
        TargetEntityId = 200,
        TargetOwnServerObjectId = 2000,
        TargetCurrentHp = 1000,
        TargetMaxHp = 1000,
        TargetPosition = new Vector3Snapshot(3, 0, 0),
        TargetServerObjectId = 100,
        TargetIsTargetingLocalPlayer = true,
        WorldObjects = new[]
        {
            new WorldObjectSnapshot(
                200,
                2000,
                "attacker",
                "monster",
                new Vector3Snapshot(3, 0, 0),
                2,
                1000,
                1000,
                TargetServerObjectId: 100,
                IsTargetingLocalPlayer: true,
                AggressiveKnown: true,
                IsAggressiveToPlayer: true)
        },
        Skills = CreateSkillSnapshotsById(new Dictionary<uint, uint>
        {
            [1] = 0,
            [5] = 0,
            [6] = 0
        })
    };
    var semiAuto = new SemiAutoCombatController(keyboard);
    var controller = new StationaryCombatController(keyboard, semiAuto, pathStore);
    var state = new StationaryCombatState();
    var plan = SemiAutoSkillPlan.FromSettings(settings.Skills);

    await controller
        .TickAsync(CreateContext(settings, gameApi, logger), plan, new SemiAutoCombatState(), state)
        .ConfigureAwait(false);

    AssertFalse(!state.StartupRecoveryActive, "startup recovery should remain active while defending");
    AssertFalse(!state.Fighting, "targeting monster should interrupt startup recovery into combat");
    AssertEqual((ushort)200, state.CandidateEntityId, "targeting monster should become startup recovery defense candidate");
    AssertFalse(keyboard.KeyDowns.Contains("W"), "startup recovery should not continue path movement before defense");
    AssertFalse(!keyboard.Keys.Any(key => key.StartsWith("D", StringComparison.OrdinalIgnoreCase)), "startup recovery defense should release combat skills");
    AssertFalse(!logger.Entries.Any(entry =>
            entry.EventName == "stationary_combat.recovery_defense.target_selected" &&
            string.Equals(Convert.ToString(entry.Fields["phase"]), "startup_recovery", StringComparison.Ordinal)),
        "startup recovery defense target should be logged");
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
            new[] { "move:-2000,-2000", "move:-2000,-2000", "move:470,300", "down:Left", "up:Left" },
            keyboard.MouseCommands.ToArray(),
            "death recovery should absolute-click revive button");
        AssertFalse(keyboard.Keys.Contains("Tab"), "death recovery must not enter target acquisition");
        AssertFalse(keyboard.Keys.Any(key => key.StartsWith("D", StringComparison.OrdinalIgnoreCase)), "death recovery must not release combat skills");

        await controller.TickAsync(context, plan, semiAutoState, stationaryState).ConfigureAwait(false);
        AssertSequence(
            new[]
            {
                "move:-2000,-2000",
                "move:-2000,-2000",
                "move:550,375",
                "down:Left",
                "up:Left"
            },
            keyboard.MouseCommands.Skip(5).Take(5).ToArray(),
            "death recovery should retry fallback revive click when player is still dead after retry delay");
        AssertEqual(2, stationaryState.DeathRecovery.ReviveClickCount, "death recovery should record retry revive click count");

        await controller.TickAsync(context, plan, semiAutoState, stationaryState).ConfigureAwait(false);
        AssertSequence(
            new[]
            {
                "move:-2000,-2000",
                "move:-2000,-2000",
                "move:690,468",
                "down:Left",
                "up:Left"
            },
            keyboard.MouseCommands.Skip(10).Take(5).ToArray(),
            "death recovery should retry third revive click when player is still dead after second retry");
        AssertEqual(3, stationaryState.DeathRecovery.ReviveClickCount, "death recovery should record third revive click count");

        gameApi.Player = gameApi.Player with { CurrentHp = 10 };
        await controller.TickAsync(context, plan, semiAutoState, stationaryState).ConfigureAwait(false);
        AssertSequence(
            Enumerable.Repeat("wheel:-1", 10).ToArray(),
            keyboard.MouseCommands.Skip(15).Take(10).ToArray(),
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

static async Task TestStationaryCombatDeathRecoverySummonsSpiritmasterPetBeforeRevivePathAsync()
{
    var settings = CreateSpiritmasterScriptSettings();
    settings.MainMode = AccountMainMode.CustomCombat;
    settings.CombatMode = AccountCombatMode.Stationary;
    settings.Paths.RevivePathName = "revive-a";
    settings.Skills.Spiritmaster.SummonSkills = new List<SpiritmasterSkillKeyRuleConfig>
    {
        new() { Key = "NumPad6" }
    };
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
        Player = CreateSpiritmasterPlayer(),
        SummonedPetRoster = SummonedPetRosterSnapshot.Empty(1000, DateTimeOffset.Now),
        TargetEntityId = 0,
        TargetCurrentHp = 0,
        TargetPosition = null,
        WorldObjects = Array.Empty<WorldObjectSnapshot>(),
        Skills = CreateSpiritmasterSkillSnapshots()
    };
    var semiAuto = new SemiAutoCombatController(keyboard);
    var controller = new StationaryCombatController(keyboard, semiAuto, pathStore);
    var stationaryState = new StationaryCombatState();
    var semiAutoState = new SemiAutoCombatState();
    var plan = SemiAutoSkillPlan.FromSettings(settings.Skills);
    var context = CreateContext(settings, gameApi, logger);

    stationaryState.EnterDeathRecovery(DateTimeOffset.Now);
    for (var step = 0; step < 6; step++)
    {
        stationaryState.DeathRecovery.Advance(DateTimeOffset.Now);
    }

    AssertEqual(StationaryCombatDeathRecoveryStep.FollowRevivePath, stationaryState.DeathRecovery.Step, "test should start at revive path");

    await controller.TickAsync(context, plan, semiAutoState, stationaryState).ConfigureAwait(false);

    AssertSequence(new[] { "NumPad6" }, keyboard.Keys.ToArray(), "revive path should summon missing spiritmaster pet first");
    AssertFalse(keyboard.KeyDowns.Contains("W"), "revive path must wait for spiritmaster pet verification before moving");

    gameApi.SummonedPetRoster = CreateLocalPetRoster(isSummoned: true);
    keyboard.Keys.Clear();
    await controller.TickAsync(context, plan, semiAutoState, stationaryState).ConfigureAwait(false);

    AssertFalse(!keyboard.KeyDowns.Contains("W"), "revive path should move after spiritmaster pet summon is verified");
    AssertFalse(
        !logger.Entries.Any(entry => entry.EventName == "semi_auto.spiritmaster.summon_verified"),
        "revive path should log spiritmaster pet summon verification");
}

static async Task TestStationaryCombatDeathRecoveryPathDefendsWhenTargetedAsync()
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
            Skills = CreateSkillSnapshotsById(new Dictionary<uint, uint>
            {
                [1] = 0,
                [5] = 0,
                [6] = 0
            })
        };
        var semiAuto = new SemiAutoCombatController(keyboard);
        var controller = new StationaryCombatController(keyboard, semiAuto, pathStore);
        var stationaryState = new StationaryCombatState();
        var semiAutoState = new SemiAutoCombatState();
        var plan = SemiAutoSkillPlan.FromSettings(settings.Skills);
        var context = CreateContext(settings, gameApi, logger);

        await controller.TickAsync(context, plan, semiAutoState, stationaryState).ConfigureAwait(false);
        gameApi.Player = gameApi.Player with { CurrentHp = 75 };
        await controller.TickAsync(context, plan, semiAutoState, stationaryState).ConfigureAwait(false);
        await controller.TickAsync(context, plan, semiAutoState, stationaryState).ConfigureAwait(false);

        AssertEqual(StationaryCombatDeathRecoveryStep.FollowRevivePath, stationaryState.DeathRecovery.Step, "revived player should be following revive path");
        AssertFalse(!keyboard.KeyDowns.Contains("W"), "death recovery should start path movement before the attacker appears");

        keyboard.Keys.Clear();
        keyboard.KeyUps.Clear();
        gameApi.TargetEntityId = 200;
        gameApi.TargetOwnServerObjectId = 2000;
        gameApi.TargetCurrentHp = 1000;
        gameApi.TargetMaxHp = 1000;
        gameApi.TargetPosition = new Vector3Snapshot(3, 0, 0);
        gameApi.TargetServerObjectId = 100;
        gameApi.TargetIsTargetingLocalPlayer = true;
        gameApi.WorldObjects = new[]
        {
            new WorldObjectSnapshot(
                200,
                2000,
                "attacker",
                "monster",
                new Vector3Snapshot(3, 0, 0),
                3,
                1000,
                1000,
                TargetServerObjectId: 100,
                IsTargetingLocalPlayer: true,
                AggressiveKnown: true,
                IsAggressiveToPlayer: true)
        };

        await controller.TickAsync(context, plan, semiAutoState, stationaryState).ConfigureAwait(false);

        AssertEqual(StationaryCombatTopLevelState.DeathRecovery, stationaryState.TopLevelState, "defense should stay inside death recovery path state");
        AssertEqual(StationaryCombatDeathRecoveryStep.FollowRevivePath, stationaryState.DeathRecovery.Step, "defense should not complete revive path");
        AssertFalse(!stationaryState.Fighting, "targeting monster should interrupt death recovery path into combat");
        AssertFalse(!keyboard.KeyUps.Contains("W"), "death recovery defense should stop path movement first");
        AssertFalse(!keyboard.Keys.Any(key => key.StartsWith("D", StringComparison.OrdinalIgnoreCase)), "death recovery defense should release combat skills");
        AssertFalse(!logger.Entries.Any(entry =>
                entry.EventName == "stationary_combat.recovery_defense.target_selected" &&
                string.Equals(Convert.ToString(entry.Fields["phase"]), "death_recovery", StringComparison.Ordinal)),
            "death recovery defense target should be logged");
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

static async Task TestStationaryCombatDeathRecoveryPathJumpsWhenStuckAsync()
{
    var previousStuckMs = Environment.GetEnvironmentVariable("ROADHOG_DEATH_REVIVE_PATH_STUCK_MS");
    var previousStuckDistance = Environment.GetEnvironmentVariable("ROADHOG_DEATH_REVIVE_PATH_STUCK_DISTANCE");
    var previousJumpHold = Environment.GetEnvironmentVariable("ROADHOG_DEATH_REVIVE_PATH_JUMP_HOLD_MS");
    var previousBearingMode = Environment.GetEnvironmentVariable("AION_FACE_TARGET_BEARING_MODE");
    Environment.SetEnvironmentVariable("ROADHOG_DEATH_REVIVE_PATH_STUCK_MS", "20");
    Environment.SetEnvironmentVariable("ROADHOG_DEATH_REVIVE_PATH_STUCK_DISTANCE", "0.5");
    Environment.SetEnvironmentVariable("ROADHOG_DEATH_REVIVE_PATH_JUMP_HOLD_MS", "1");
    Environment.SetEnvironmentVariable("AION_FACE_TARGET_BEARING_MODE", "y-x");
    try
    {
        var settings = CreateScriptSettings();
        settings.MainMode = AccountMainMode.CustomCombat;
        settings.CombatMode = AccountCombatMode.Stationary;
        settings.Combat = new CombatScriptSettings
        {
            HasStationaryCombatPosition = true,
            StationaryCombatX = 20,
            StationaryCombatY = 0,
            StationaryCombatZ = 0,
            StationaryCombatRadius = 10
        };

        var keyboard = new RecordingKeyboardInput();
        var logger = new InMemoryRoadhogLogger();
        var gameApi = new FakeGameApi
        {
            Player = new PlayerSnapshot(1, 0, "Fake", 75, 100, 100, 100, 0, new Vector3Snapshot(0, 0, 0), DateTimeOffset.Now, 90, 10, 90),
            TargetEntityId = 0,
            TargetCurrentHp = 0,
            TargetPosition = null,
            WorldObjects = Array.Empty<WorldObjectSnapshot>(),
            Skills = CreateSkillSnapshotsById(new Dictionary<uint, uint>())
        };
        var semiAuto = new SemiAutoCombatController(keyboard);
        var controller = new StationaryCombatController(keyboard, semiAuto);
        var stationaryState = new StationaryCombatState();
        var semiAutoState = new SemiAutoCombatState();
        stationaryState.EnterDeathRecovery(DateTimeOffset.Now);
        for (var i = 0; i < 6; i++)
        {
            stationaryState.DeathRecovery.Advance(DateTimeOffset.Now);
        }

        stationaryState.DeathRecovery.RevivePathName = "revive-a";
        stationaryState.DeathRecovery.RevivePathPoints = new[]
        {
            new Vector3Snapshot(0, 0, 0),
            new Vector3Snapshot(10, 0, 0)
        };
        stationaryState.DeathRecovery.RevivePathPointIndex = 1;
        var plan = SemiAutoSkillPlan.FromSettings(settings.Skills);
        var context = CreateContext(settings, gameApi, logger);

        await controller.TickAsync(context, plan, semiAutoState, stationaryState).ConfigureAwait(false);

        AssertEqual(StationaryCombatDeathRecoveryStep.FollowRevivePath, stationaryState.DeathRecovery.Step, "death recovery should stay on revive path");
        AssertFalse(!keyboard.KeyDowns.Contains("W"), "first stuck-tracking tick should hold W");
        AssertFalse(keyboard.Keys.Contains("Space"), "first stuck-tracking tick should not jump before threshold");

        await Task.Delay(30).ConfigureAwait(false);
        await controller.TickAsync(context, plan, semiAutoState, stationaryState).ConfigureAwait(false);

        AssertFalse(!stationaryState.IsMovingForward, "stuck jump should keep W held");
        AssertFalse(!keyboard.Keys.Contains("Space"), "stuck revive path should press Space");
        AssertFalse(keyboard.KeyUps.Contains("W"), "stuck jump must not release W");
        AssertFalse(!logger.Entries.Any(entry => entry.EventName == "stationary_combat.death_recovery.path_stuck_jump"),
            "stuck jump should be logged");
    }
    finally
    {
        Environment.SetEnvironmentVariable("ROADHOG_DEATH_REVIVE_PATH_STUCK_MS", previousStuckMs);
        Environment.SetEnvironmentVariable("ROADHOG_DEATH_REVIVE_PATH_STUCK_DISTANCE", previousStuckDistance);
        Environment.SetEnvironmentVariable("ROADHOG_DEATH_REVIVE_PATH_JUMP_HOLD_MS", previousJumpHold);
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
            new[] { "move:-2000,-2000", "move:-2000,-2000", "move:470,300", "down:Left", "up:Left" },
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

static async Task TestWorkerEnsuresSpiritmasterPetBeforeNormalWorkAsync()
{
    var settings = CreateSpiritmasterScriptSettings();
    settings.MainMode = AccountMainMode.CustomCombat;
    settings.CombatMode = AccountCombatMode.Stationary;
    settings.Combat.HasStationaryCombatPosition = false;
    settings.Skills.Spiritmaster.SummonSkills = new List<SpiritmasterSkillKeyRuleConfig>
    {
        new() { Key = "NumPad6" }
    };

    var keyboard = new RecordingKeyboardInput();
    var logger = new InMemoryRoadhogLogger();
    var gameApi = new FakeGameApi
    {
        Player = CreateSpiritmasterPlayer(),
        SummonedPetRoster = SummonedPetRosterSnapshot.Empty(1000, DateTimeOffset.Now),
        TargetEntityId = 0,
        TargetCurrentHp = 0,
        TargetPosition = null,
        Skills = CreateSpiritmasterSkillSnapshots()
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
            () => keyboard.Keys.Contains("NumPad6"),
            "spiritmaster pet ensure summon key")
        .ConfigureAwait(false);
    cts.Cancel();
    await IgnoreCancellationAsync(runTask).ConfigureAwait(false);

    AssertSequence(new[] { "NumPad6" }, keyboard.Keys.ToArray(), "worker should summon missing spiritmaster pet outside combat");
    AssertFalse(
        logger.Entries.Any(entry => entry.EventName == "stationary_combat.position.missing"),
        "spiritmaster pet ensure should run before ordinary stationary validation");
    AssertFalse(
        !logger.Entries.Any(entry =>
            entry.EventName == "semi_auto.spiritmaster.key_pressed" &&
            Convert.ToString(entry.Fields["phase"]) == "summon_speed"),
        "spiritmaster pet ensure should log summon key press");
}

static async Task TestWorkerWaitsForSpiritmasterPetSummonVerificationAsync()
{
    var settings = CreateSpiritmasterScriptSettings();
    settings.MainMode = AccountMainMode.CustomCombat;
    settings.CombatMode = AccountCombatMode.Stationary;
    settings.Combat.HasStationaryCombatPosition = false;
    settings.Skills.Spiritmaster.SummonSkills = new List<SpiritmasterSkillKeyRuleConfig>
    {
        new() { Key = "NumPad6" }
    };

    var keyboard = new RecordingKeyboardInput();
    var logger = new InMemoryRoadhogLogger();
    var gameApi = new FakeGameApi
    {
        Player = CreateSpiritmasterPlayer(),
        SummonedPetRoster = SummonedPetRosterSnapshot.Empty(1000, DateTimeOffset.Now),
        TargetEntityId = 0,
        TargetCurrentHp = 0,
        TargetPosition = null,
        Skills = CreateSpiritmasterSkillSnapshots()
    };
    var semiAuto = new SemiAutoCombatController(keyboard);
    var stationary = new StationaryCombatController(keyboard, semiAuto);
    var worker = new DefaultAccountWorkerLoop(keyboard, semiAuto, stationary);
    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
    var context = CreateContext(
        settings,
        gameApi,
        logger,
        options: new AccountWorkerOptions { TickInterval = TimeSpan.FromMilliseconds(40) },
        stopToken: cts.Token);

    var runTask = worker.RunAsync(context);
    await WaitUntilAsync(
            () => keyboard.Keys.Contains("NumPad6"),
            "spiritmaster summon key")
        .ConfigureAwait(false);
    await Task.Delay(250).ConfigureAwait(false);
    AssertFalse(
        logger.Entries.Any(entry => entry.EventName == "stationary_combat.position.missing"),
        "worker should not continue ordinary work before summon is verified");

    gameApi.SummonedPetRoster = CreateLocalPetRoster(isSummoned: true);
    await WaitUntilAsync(
            () => logger.Entries.Any(entry => entry.EventName == "stationary_combat.position.missing"),
            "ordinary work after spiritmaster summon verification")
        .ConfigureAwait(false);
    cts.Cancel();
    await IgnoreCancellationAsync(runTask).ConfigureAwait(false);

    AssertFalse(
        !logger.Entries.Any(entry => entry.EventName == "semi_auto.spiritmaster.summon_verified"),
        "summon verification should be logged");
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

static async Task TestStationaryCombatAcceptsTwentyFiveDegreePreLockFaceToleranceAsync()
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
            Math.Abs(Convert.ToDouble(entry.Fields["yawTolerance"]) - 25.0D) < 0.001D),
            "face target log should show 25 degree yaw tolerance");
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

static async Task TestStationaryCombatJumpsWhileStuckApproachingTargetAsync()
{
    var previousBearingMode = Environment.GetEnvironmentVariable("AION_FACE_TARGET_BEARING_MODE");
    var previousStuckMs = Environment.GetEnvironmentVariable("ROADHOG_COMBAT_APPROACH_STUCK_MS");
    var previousStuckDistance = Environment.GetEnvironmentVariable("ROADHOG_COMBAT_APPROACH_STUCK_DISTANCE");
    var previousJumpHold = Environment.GetEnvironmentVariable("ROADHOG_COMBAT_APPROACH_JUMP_HOLD_MS");
    var previousJumpInterval = Environment.GetEnvironmentVariable("ROADHOG_COMBAT_APPROACH_JUMP_INTERVAL_MS");
    var previousJumpCount = Environment.GetEnvironmentVariable("ROADHOG_COMBAT_APPROACH_JUMP_COUNT");
    Environment.SetEnvironmentVariable("AION_FACE_TARGET_BEARING_MODE", "y-x");
    Environment.SetEnvironmentVariable("ROADHOG_COMBAT_APPROACH_STUCK_MS", "20");
    Environment.SetEnvironmentVariable("ROADHOG_COMBAT_APPROACH_STUCK_DISTANCE", "0.5");
    Environment.SetEnvironmentVariable("ROADHOG_COMBAT_APPROACH_JUMP_HOLD_MS", "1");
    Environment.SetEnvironmentVariable("ROADHOG_COMBAT_APPROACH_JUMP_INTERVAL_MS", "1");
    Environment.SetEnvironmentVariable("ROADHOG_COMBAT_APPROACH_JUMP_COUNT", "3");
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

        AssertFalse(!state.IsMovingForward, "first approach tick should hold W");
        AssertFalse(keyboard.Keys.Contains("Space"), "first approach tick should only start stuck tracking");

        await Task.Delay(30).ConfigureAwait(false);
        await controller.TickAsync(context, plan, semiAutoState, state).ConfigureAwait(false);

        AssertFalse(!state.IsMovingForward, "stuck approach jump should keep W held");
        AssertFalse(keyboard.KeyUps.Contains("W"), "stuck approach jump must not release W");
        AssertEqual(3, keyboard.Keys.Count(key => string.Equals(key, "Space", StringComparison.OrdinalIgnoreCase)),
            "stuck approach should press Space three times");
        AssertFalse(
            !logger.Entries.Any(entry => entry.EventName == "stationary_combat.combat_approach.stuck_jump"),
            "stuck approach jump should be logged");
    }
    finally
    {
        Environment.SetEnvironmentVariable("AION_FACE_TARGET_BEARING_MODE", previousBearingMode);
        Environment.SetEnvironmentVariable("ROADHOG_COMBAT_APPROACH_STUCK_MS", previousStuckMs);
        Environment.SetEnvironmentVariable("ROADHOG_COMBAT_APPROACH_STUCK_DISTANCE", previousStuckDistance);
        Environment.SetEnvironmentVariable("ROADHOG_COMBAT_APPROACH_JUMP_HOLD_MS", previousJumpHold);
        Environment.SetEnvironmentVariable("ROADHOG_COMBAT_APPROACH_JUMP_INTERVAL_MS", previousJumpInterval);
        Environment.SetEnvironmentVariable("ROADHOG_COMBAT_APPROACH_JUMP_COUNT", previousJumpCount);
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

static async Task TestStationaryCombatKeepsFightWhenLockedServerIdMatchesAsync()
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

    var serverObjectId = 2246150598u;
    var keyboard = new RecordingKeyboardInput();
    var logger = new InMemoryRoadhogLogger();
    var gameApi = new FakeGameApi
    {
        Player = new PlayerSnapshot(1, 100, "Fake", 100, 100, 100, 100, 0, new Vector3Snapshot(0, 0, 0), DateTimeOffset.Now, 90, 10, 90),
        TargetEntityId = 65519,
        TargetOwnServerObjectId = serverObjectId,
        TargetCurrentHp = 1000,
        TargetMaxHp = 1000,
        TargetPosition = new Vector3Snapshot(8, 0, 0),
        TargetServerObjectId = 1,
        TargetIsTargetingLocalPlayer = true,
        WorldObjects = new[]
        {
            new WorldObjectSnapshot(65498, serverObjectId, "same-server", "monster", new Vector3Snapshot(8, 0, 0), 8, 1000, 1000, 1, true)
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
        CurrentTargetEntityId = 65498,
        CurrentTargetServerObjectId = serverObjectId,
        CandidateEntityId = 65498,
        CandidateServerObjectId = serverObjectId
    };
    state.MarkCandidate(65498, serverObjectId, DateTimeOffset.Now);

    await controller
        .TickAsync(CreateContext(settings, gameApi, logger), plan, new SemiAutoCombatState(), state)
        .ConfigureAwait(false);

    AssertFalse(!state.Fighting, "same server object target should keep fight state");
    AssertEqual((ushort)65519, state.CurrentTargetEntityId, "current target entity id should refresh from locked target");
    AssertEqual(serverObjectId, state.CurrentTargetServerObjectId, "current target server object id");
    AssertFalse(keyboard.Keys.Contains("Tab"), "same server object target should not tab reacquire");
    AssertFalse(!keyboard.Keys.Contains("D2"), "same server object target should continue skill release");
    AssertFalse(
        logger.Entries.Any(entry => entry.EventName == "stationary_combat.target.reacquire"),
        "same server object target should not log reacquire");
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
        LocalServerObjectId = 1,
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
    gameApi.TargetIsTargetingLocalPlayer = false;
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

static async Task TestStationaryCombatKeepsPreviouslyEngagedTargetWhileSelfTargetingAsync()
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

    var targetServerObjectId = 100u;
    var keyboard = new RecordingKeyboardInput();
    var logger = new InMemoryRoadhogLogger();
    var gameApi = new FakeGameApi
    {
        Player = new PlayerSnapshot(1, 100, "Fake", 100, 100, 100, 100, 0, new Vector3Snapshot(0, 0, 0), DateTimeOffset.Now, 90, 10, 90),
        TargetEntityId = 100,
        TargetOwnServerObjectId = targetServerObjectId,
        TargetCurrentHp = 1000,
        TargetMaxHp = 1000,
        TargetPosition = new Vector3Snapshot(8, 0, 0),
        TargetServerObjectId = targetServerObjectId,
        LocalServerObjectId = 1,
        TargetIsTargetingLocalPlayer = false,
        WorldObjects = new[]
        {
            new WorldObjectSnapshot(100, targetServerObjectId, "self-targeting", "monster", new Vector3Snapshot(8, 0, 0), 8, 1000, 1000, targetServerObjectId, false),
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
        CurrentTargetEntityId = 100,
        CurrentTargetServerObjectId = targetServerObjectId,
        CandidateEntityId = 100,
        CandidateServerObjectId = targetServerObjectId,
        CurrentTargetIsMaintenanceDefense = true
    };
    state.MarkCandidate(100, targetServerObjectId, DateTimeOffset.Now);
    var semiAutoState = new SemiAutoCombatState();
    var context = CreateContext(settings, gameApi, logger);

    await controller.TickAsync(context, plan, semiAutoState, state).ConfigureAwait(false);

    AssertFalse(!state.Fighting, "previously engaged target should remain the current fight target");
    AssertEqual((ushort)100, state.CurrentTargetEntityId, "current fight target should not switch");
    AssertEqual(targetServerObjectId, state.CurrentTargetServerObjectId, "current fight target server id should not switch");
    AssertFalse(state.IsTargetIgnored(100, targetServerObjectId), "previously engaged target should not be ignored as claimed");
    AssertFalse(keyboard.Keys.Contains("C"), "previously engaged self-targeting monster should not re-enter opening attack wait");
    AssertFalse(!keyboard.Keys.Contains("D2"), "previously engaged self-targeting monster should continue skill release");
    AssertFalse(logger.Entries.Any(entry =>
        entry.EventName == "stationary_combat.target.ignored" &&
        string.Equals(Convert.ToString(entry.Fields["reason"]), "target_owned_by_other", StringComparison.Ordinal)),
        "previously engaged target should not log claimed-target ignore");
}

static async Task TestStationaryCombatKeepsSpiritmasterPetTargetedFightAsync()
{
    var settings = CreateSpiritmasterScriptSettings();
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

    const uint targetServerObjectId = 100;
    const uint petServerObjectId = 2000;
    var keyboard = new RecordingKeyboardInput();
    var logger = new InMemoryRoadhogLogger();
    var gameApi = new FakeGameApi
    {
        Player = CreateSpiritmasterPlayer(),
        SummonedPetRoster = CreateLocalPetRoster(isSummoned: true),
        TargetEntityId = 100,
        TargetOwnServerObjectId = targetServerObjectId,
        TargetCurrentHp = 1000,
        TargetMaxHp = 1000,
        TargetPosition = new Vector3Snapshot(8, 0, 0),
        TargetServerObjectId = petServerObjectId,
        LocalServerObjectId = 1000,
        TargetIsTargetingLocalPlayer = false,
        WorldObjects = new[]
        {
            new WorldObjectSnapshot(100, targetServerObjectId, "pet-targeting", "monster", new Vector3Snapshot(8, 0, 0), 8, 1000, 1000, petServerObjectId, false),
            new WorldObjectSnapshot(101, 101, "next", "monster", new Vector3Snapshot(12, 0, 0), 12, 1000, 1000)
        },
        Skills = CreateSpiritmasterSkillSnapshots()
    };
    var semiAuto = new SemiAutoCombatController(keyboard);
    var controller = new StationaryCombatController(keyboard, semiAuto);
    var plan = SemiAutoSkillPlan.FromSettings(settings.Skills);
    var state = new StationaryCombatState
    {
        Fighting = true,
        CurrentTargetEntityId = 100,
        CurrentTargetServerObjectId = targetServerObjectId,
        CandidateEntityId = 100,
        CandidateServerObjectId = targetServerObjectId
    };
    state.MarkCandidate(100, targetServerObjectId, DateTimeOffset.Now);
    var semiAutoState = new SemiAutoCombatState();
    CalibrateCooldownClock(semiAutoState);
    var context = CreateContext(settings, gameApi, logger);

    await controller.TickAsync(context, plan, semiAutoState, state).ConfigureAwait(false);

    AssertFalse(!state.Fighting, "target that locked local spiritmaster pet should remain the current fight target");
    AssertFalse(!state.CurrentTargetIsMaintenanceDefense, "pet-targeted monster should be marked as local-side defense");
    AssertEqual((ushort)100, state.CurrentTargetEntityId, "current fight target should stay on pet-targeted monster");
    AssertEqual(targetServerObjectId, state.CurrentTargetServerObjectId, "current fight target server id should stay on pet-targeted monster");
    AssertFalse(state.IsTargetIgnored(100, targetServerObjectId), "pet-targeted monster should not be ignored as claimed");
    AssertFalse(keyboard.Keys.Contains("C"), "pet-targeted monster should not wait for player body targeting via C loop");
    AssertFalse(!keyboard.Keys.Contains("D1"), "pet-targeted monster should continue skill release");
    AssertFalse(logger.Entries.Any(entry =>
        entry.EventName == "stationary_combat.target.ignored" &&
        string.Equals(Convert.ToString(entry.Fields["reason"]), "target_owned_by_other", StringComparison.Ordinal)),
        "pet-targeted monster should not log claimed-target ignore");
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

static async Task TestStationaryCombatRunsAfterCombatMaintenanceAfterLootAsync()
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
        settings.Maintenance.SitMaintenanceEnabled = false;
        settings.Maintenance.HpMaintenanceRules.Add(new MaintenanceKeyRuleConfig
        {
            BelowPercent = 50,
            Key = "D8",
            RunTiming = MaintenanceRuleRunTiming.AfterCombat
        });

        var keyboard = new RecordingKeyboardInput();
        var logger = new InMemoryRoadhogLogger();
        var gameApi = new FakeGameApi
        {
            Player = new PlayerSnapshot(1, 100, "Fake", 40, 100, 100, 100, 0, new Vector3Snapshot(0, 0, 0), DateTimeOffset.Now, 90, 10, 90),
            TargetEntityId = 100,
            TargetCurrentHp = 0,
            TargetMaxHp = 4430,
            TargetPosition = new Vector3Snapshot(2.5f, 0, 0),
            Skills = CreateSkillSnapshotsById(new Dictionary<uint, uint>
            {
                [1] = 0,
                [5] = 0,
                [6] = 0,
                [8] = 0
            })
        };
        keyboard.AfterPress = key =>
        {
            if (string.Equals(key, "D8", StringComparison.Ordinal))
            {
                gameApi.Skills = CreateSkillSnapshotsById(new Dictionary<uint, uint>
                {
                    [1] = 0,
                    [5] = 0,
                    [6] = 0,
                    [8] = ActiveCooldownEnd()
                });
            }
        };

        var semiAuto = new SemiAutoCombatController(keyboard);
        var controller = new StationaryCombatController(keyboard, semiAuto);
        var plan = SemiAutoSkillPlan.FromSettings(settings.Skills);
        var semiAutoState = new SemiAutoCombatState();
        CalibrateCooldownClock(semiAutoState);
        var state = new StationaryCombatState
        {
            Fighting = true,
            CurrentTargetEntityId = 100,
            CandidateEntityId = 100
        };

        await controller
            .TickAsync(CreateContext(settings, gameApi, logger), plan, semiAutoState, state)
            .ConfigureAwait(false);

        AssertSequence(new[] { "NumPadDecimal", "D8" }, keyboard.Keys, "after-combat maintenance should run after loot key");
        AssertFalse(!logger.Entries.Any(entry => entry.EventName == "stationary_combat.loot.post_combat_maintenance"), "post-combat maintenance should be logged");
        AssertFalse(state.LootAfterKill.Active, "loot state should finish after post-combat maintenance");
    }
    finally
    {
        Environment.SetEnvironmentVariable("ROADHOG_LOOT_AFTER_KILL_WAIT_MS", previousAfterKillWait);
        Environment.SetEnvironmentVariable("ROADHOG_LOOT_AFTER_PICK_WAIT_MS", previousWait);
        Environment.SetEnvironmentVariable("ROADHOG_LOOT_PRESS_COUNT", previousPressCount);
        Environment.SetEnvironmentVariable("ROADHOG_LOOT_PRESS_INTERVAL_MS", previousPressInterval);
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

static Task TestAvailableSkillTreeKeepsChainRootsInNormalCategoryAsync()
{
    Exception? failure = null;
    var thread = new Thread(() =>
    {
        try
        {
            using var form = CreateAccountSettingsFormForTests();
            using var tree = new System.Windows.Forms.TreeView();
            var rootSkill = new SkillSnapshot(
                1001,
                "Counter Root III",
                3,
                3,
                "Counter Root",
                3,
                false,
                1000,
                0,
                XmlActivation: "Active",
                XmlChainCategory: "test_counter_chain",
                XmlCounterSkill: "Block");
            var chainSkill = new SkillSnapshot(
                1002,
                "Chain Child I",
                1,
                1,
                "Chain Child",
                1,
                false,
                1000,
                0,
                XmlActivation: "Active",
                XmlPrechainCategory: "test_counter_chain",
                XmlChainTime: "5000");

            InvokePopulateAvailableSkillTree(form, tree, new[] { rootSkill, chainSkill });

            var rootCategory = GetManualSkillCategoryForTest(rootSkill);
            var chainCategory = GetManualSkillCategoryForTest(chainSkill);
            AssertFalse(string.Equals(rootCategory, chainCategory, StringComparison.Ordinal), "root and child should belong to different manual categories");

            var rootCategoryNode = FindDirectTreeNode(tree.Nodes, rootCategory);
            AssertFalse(rootCategoryNode is null, "available tree should keep chain root in its normal category");
            AssertFalse(
                !ContainsDirectTreeNode(rootCategoryNode!.Nodes, rootSkill.Name),
                "normal category should contain the chain root skill");

            var chainCategoryNode = FindDirectTreeNode(tree.Nodes, chainCategory);
            AssertFalse(chainCategoryNode is null, "available tree should still expose chain category");
            var chainRootNode = FindDirectTreeNode(chainCategoryNode!.Nodes, rootSkill.Name);
            AssertFalse(chainRootNode is null, "chain category should contain the chain root entry");
            AssertFalse(
                !ContainsDirectTreeNode(chainRootNode!.Nodes, chainSkill.Name),
                "chain root entry should contain the chain child");
        }
        catch (Exception ex)
        {
            failure = ex;
        }
    });

    thread.SetApartmentState(ApartmentState.STA);
    thread.Start();
    thread.Join();

    if (failure is not null)
    {
        System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }

    return Task.CompletedTask;
}

static Task TestSelectedSkillRefreshRemovesUnavailableCurrentSkillsAsync()
{
    Exception? failure = null;
    var thread = new Thread(() =>
    {
        try
        {
            using var tree = new System.Windows.Forms.TreeView();
            tree.Nodes.Add("Old Skill V", "Old Skill V");
            tree.Nodes.Add("Missing Skill V", "Missing Skill V");
            var root = tree.Nodes.Add("Chain Root V", "Chain Root V");
            root.Nodes.Add("Known Child V", "Known Child V");
            root.Nodes.Add("Missing Child V", "Missing Child V");

            var currentSkills = new[]
            {
                new SkillSnapshot(2001, "Old Skill II", 2, 2, "Old Skill", 2, false, 1000, 0),
                new SkillSnapshot(2002, "Chain Root II", 2, 2, "Chain Root", 2, false, 1000, 0),
                new SkillSnapshot(2003, "Known Child I", 1, 1, "Known Child", 1, false, 1000, 0)
            };

            var result = InvokeRefreshSelectedSkillTreeToHighestCurrentSkills(tree, currentSkills);

            AssertEqual(3, result.UpdatedCount, "current skills should replace selected skills with current highest ranks");
            AssertEqual(2, result.DeletedCount, "unavailable selected skills should be removed");
            AssertEqual(2, tree.Nodes.Count, "missing root skill should be removed");
            AssertFalse(FindDirectTreeNode(tree.Nodes, "Missing Skill V") is not null, "missing root should not remain");
            AssertFalse(FindDirectTreeNode(tree.Nodes, "Old Skill II") is null, "known root should be downgraded to current highest");

            var refreshedRoot = FindDirectTreeNode(tree.Nodes, "Chain Root II");
            AssertFalse(refreshedRoot is null, "chain root should be downgraded to current highest");
            AssertEqual(1, refreshedRoot!.Nodes.Count, "missing chain child should be removed");
            AssertFalse(FindDirectTreeNode(refreshedRoot.Nodes, "Known Child I") is null, "known chain child should be downgraded to current highest");
            AssertFalse(FindDirectTreeNode(refreshedRoot.Nodes, "Missing Child V") is not null, "missing chain child should not remain");
        }
        catch (Exception ex)
        {
            failure = ex;
        }
    });

    thread.SetApartmentState(ApartmentState.STA);
    thread.Start();
    thread.Join();

    if (failure is not null)
    {
        System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }

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

static async Task TestKnockdownTriggerSavedAsStatusIsTreatedAsTriggerAsync()
{
    var settings = CreateScriptSettings();
    settings.Skills.ExecutionTree.Insert(1, Node(410, "脚踝重击 I", "状态技能"));
    var plan = SemiAutoSkillPlan.FromSettings(settings.Skills);
    var keyboard = new RecordingKeyboardInput();
    var logger = new InMemoryRoadhogLogger();
    var skills = Flatten(plan.Roots)
        .Select(node =>
        {
            var cooldownEnd = node.SkillId == 1
                ? ActiveCooldownEnd()
                : 0u;
            return new SkillSnapshot(
                node.SkillId,
                node.Name,
                1,
                1,
                node.BaseName,
                1,
                false,
                node.SkillId == 1 ? 1000u : 0u,
                cooldownEnd);
        })
        .ToArray();
    var gameApi = new FakeGameApi
    {
        Skills = skills
    };
    var controller = new SemiAutoCombatController(keyboard);
    var state = new SemiAutoCombatState();
    CalibrateCooldownClock(state);

    await controller.TickAsync(CreateContext(settings, gameApi, logger), plan, state).ConfigureAwait(false);

    AssertFalse(!plan.Roots[1].IsTrigger, "knockdown trigger should be normalized as trigger");
    AssertSequence(
        new[] { "D2", "D3", "D4", "D5", "D6" },
        keyboard.Keys.ToArray(),
        "knockdown trigger should be prefix before first active root");
    AssertFalse(gameApi.LastRequestedSkillIds?.Contains(410u) == true, "knockdown trigger should not be read as a normal root");
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

    for (var i = 1; i <= 24; i++)
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
            "NumPad0",
            "NumPadSubtract",
            "NumPadAdd"
        },
        plan.Roots.Select(root => root.Key).ToArray(),
        "24-key order");

    var tenRootPlan = SemiAutoSkillPlan.FromSettings(CreateSkillSettings());
    AssertEqual("D0", tenRootPlan.Roots.Last().Key, "10th configured root key");
    AssertFalse(tenRootPlan.Roots.Any(root => root.Key is "OemMinus" or "OemPlus" or "NumPad1"), "10 roots must not use keys after D0");

    return Task.CompletedTask;
}

static Task TestManualSkillMappingPlanAsync()
{
    var settings = new SkillScriptSettings
    {
        Mode = SkillConfigurationMode.ManualMapping,
        ExecutionTree = new List<SkillConfigNode>
        {
            Node(99, "自动技能不应执行 I", "主动技能")
        },
        ManualMappings = new List<ManualSkillMappingConfig>
        {
            new()
            {
                SkillType = "状态技能",
                SkillName = "保护之盾 I",
                Key = "NumPadSubtract"
            },
            new()
            {
                SkillType = "主动技能",
                SkillName = "弱化之猛击 II",
                Key = "D3"
            },
            new()
            {
                SkillType = "主动技能",
                SkillName = string.Empty,
                Key = "D4"
            }
        }
    };

    var plan = SemiAutoSkillPlan.FromSettings(settings);

    AssertSequence(
        new[] { "保护之盾 I", "弱化之猛击 II" },
        plan.Roots.Select(root => root.Name).ToArray(),
        "manual mapping root names");
    AssertSequence(
        new[] { "NumPadSubtract", "D3" },
        plan.Roots.Select(root => root.Key).ToArray(),
        "manual mapping keys");
    AssertSequence(
        new[] { "状态技能", "主动技能" },
        plan.Roots.Select(root => root.Type).ToArray(),
        "manual mapping categories");
    AssertFalse(plan.Roots.Any(root => root.Name.Contains("自动技能", StringComparison.Ordinal)), "manual mode must ignore auto execution tree");

    return Task.CompletedTask;
}

static Task TestSystemSkillPlanAsync()
{
    var settings = new SkillScriptSettings
    {
        Mode = SkillConfigurationMode.SystemClassification,
        ExecutionTree = new List<SkillConfigNode>
        {
            Node(99, "自动技能不应执行 I", "主动技能")
        },
        ManualMappings = new List<ManualSkillMappingConfig>
        {
            new()
            {
                SkillType = "主动技能",
                SkillName = "手动技能不应执行 I",
                Key = "D8"
            }
        },
        SystemExecutionTree = new List<SkillConfigNode>
        {
            Node(1389, "重力束缚 V", "持续伤害"),
            Node(778, "毒箭 I", "持续伤害")
        }
    };

    var plan = SemiAutoSkillPlan.FromSettings(settings);

    AssertSequence(
        new[] { "重力束缚 V", "毒箭 I" },
        plan.Roots.Select(root => root.Name).ToArray(),
        "system root names");
    AssertSequence(
        new[] { "D1", "D2" },
        plan.Roots.Select(root => root.Key).ToArray(),
        "system root keys");
    AssertSequence(
        new[] { "持续伤害", "持续伤害" },
        plan.Roots.Select(root => root.Type).ToArray(),
        "system root categories");
    AssertFalse(plan.Roots.Any(root => root.Name.Contains("自动技能", StringComparison.Ordinal)), "system mode must ignore auto execution tree");
    AssertFalse(plan.Roots.Any(root => root.Name.Contains("手动技能", StringComparison.Ordinal)), "system mode must ignore manual mappings");

    return Task.CompletedTask;
}

static Task TestSpiritmasterAutoSwitchPlanAsync()
{
    var settings = new SkillScriptSettings
    {
        Mode = SkillConfigurationMode.Auto,
        SpiritmasterAutoSkillLogicEnabled = true,
        ExecutionTree = new List<SkillConfigNode>
        {
            Node(1674, "命令:愤怒之气势 I", "主动技能"),
            Node(1389, "重力束缚 V", "持续伤害")
        },
        ManualMappings = new List<ManualSkillMappingConfig>
        {
            new()
            {
                SkillType = "主动技能",
                SkillName = "手动技能不应执行 I",
                Key = "D8"
            }
        },
        SystemExecutionTree = new List<SkillConfigNode>
        {
            Node(778, "系统技能不应执行 I", "持续伤害")
        }
    };

    var plan = SemiAutoSkillPlan.FromSettings(settings);

    AssertFalse(!plan.UsesSpiritmasterAutoLogic, "auto mode should use spiritmaster branch when switch is enabled");
    AssertSequence(
        new[] { "命令:愤怒之气势 I", "重力束缚 V" },
        plan.Roots.Select(root => root.Name).ToArray(),
        "spiritmaster auto root names");
    AssertSequence(
        new[] { "D1", "D2" },
        plan.Roots.Select(root => root.Key).ToArray(),
        "spiritmaster auto root keys");
    AssertFalse(plan.Roots.Any(root => root.Name.Contains("手动技能", StringComparison.Ordinal)), "spiritmaster auto mode must ignore manual mappings");
    AssertFalse(plan.Roots.Any(root => root.Name.Contains("系统技能", StringComparison.Ordinal)), "spiritmaster auto mode must ignore system execution tree");
    AssertFalse(!settings.Clone().SpiritmasterAutoSkillLogicEnabled, "skill settings clone should preserve spiritmaster switch");

    settings.Mode = SkillConfigurationMode.ManualMapping;
    var manualPlan = SemiAutoSkillPlan.FromSettings(settings);
    AssertFalse(manualPlan.UsesSpiritmasterAutoLogic, "manual mode must ignore spiritmaster auto switch");

    settings.Mode = SkillConfigurationMode.SystemClassification;
    var systemPlan = SemiAutoSkillPlan.FromSettings(settings);
    AssertFalse(systemPlan.UsesSpiritmasterAutoLogic, "system mode must ignore spiritmaster auto switch");

    return Task.CompletedTask;
}

static Task TestSpiritmasterPlanReadsDedicatedSkillIdsAsync()
{
    var settings = new SkillScriptSettings
    {
        Mode = SkillConfigurationMode.Auto,
        SpiritmasterAutoSkillLogicEnabled = true,
        ExecutionTree = new List<SkillConfigNode>
        {
            Node(1389, "Erosion V", "active")
        },
        Spiritmaster = new SpiritmasterSkillSettings
        {
            DotSkills = new List<SpiritmasterSkillRefConfig>
            {
                new() { SkillId = 1389, SkillName = "Erosion V" }
            },
            SummonSkills = new List<SpiritmasterSkillKeyRuleConfig>
            {
                new() { Key = "NumPad6" },
                new() { Key = "NumPad8" }
            },
            PetHpMaintenanceRules = new List<SpiritmasterPetHpRuleConfig>
            {
                new() { SkillId = 1678, SkillName = "Pet Heal", Key = "NumPad4" }
            },
            PetBuffRules = new List<SpiritmasterPetBuffRuleConfig>
            {
                new() { SkillId = 1787, SkillName = "Pet Armor", Key = "NumPad9" }
            }
        }
    };

    var plan = SemiAutoSkillPlan.FromSettings(settings);

    AssertSequence(
        new uint[] { 1389, 1678, 1787 },
        plan.SkillReadIds,
        "spiritmaster skill read ids");
    AssertFalse(plan.SkillReadIds.Contains(0u), "summon keys without skill ids should not add zero");
    return Task.CompletedTask;
}

static Task TestSpiritmasterSelectorSkipsActiveDotAsync()
{
    var settings = new SkillScriptSettings
    {
        Mode = SkillConfigurationMode.Auto,
        SpiritmasterAutoSkillLogicEnabled = true,
        ExecutionTree = new List<SkillConfigNode>
        {
            Node(1389, "Erosion V", "active"),
            Node(1600, "Next Skill", "active")
        },
        Spiritmaster = new SpiritmasterSkillSettings
        {
            DotSkills = new List<SpiritmasterSkillRefConfig>
            {
                new() { SkillId = 1389, SkillName = "Erosion V" }
            }
        }
    };
    var plan = SemiAutoSkillPlan.FromSettings(settings);
    var state = new SemiAutoCombatState();
    CalibrateCooldownClock(state);
    var skills = new[]
    {
        new SkillSnapshot(1389, "Erosion V", 1, 1, "Erosion", 5, false, 1_000, 0, XmlEffectRemainMs: 15_000),
        new SkillSnapshot(1600, "Next Skill", 1, 1, "Next Skill", 1, false, 1_000, 0)
    };
    var targetAbnormal = new LockedTargetAbnormalStatusSnapshot(
        new LockedTargetSnapshot(100, 5000, 0, LockedTargetSnapshot.MonsterObjectType, "target", 100, 100, null, null, DateTimeOffset.Now),
        1,
        new[] { Abnormal(1389, PlayerAbnormalStatusSnapshot.PhysicalDebuffCategory) },
        DateTimeOffset.Now);
    var context = new SpiritmasterCombatContext(
        CreateSpiritmasterPlayer(),
        CreateLocalPetRoster(isSummoned: true),
        targetAbnormal);

    var decision = SpiritmasterAutoSkillReleasePriority.SelectNext(
        plan,
        state,
        skills,
        new SemiAutoScriptSettings(),
        settings.Spiritmaster,
        context,
        DateTimeOffset.Now);

    AssertEqual(SemiAutoSkillReleaseDecisionKind.PressRoot, decision.Kind, "dot skip decision kind");
    AssertEqual(1600u, decision.Skill?.SkillId ?? 0, "active dot should yield to next skill");
    return Task.CompletedTask;
}

static Task TestSpiritmasterSelectorTrustsTargetSnapshotOverDotWindowAsync()
{
    var settings = new SkillScriptSettings
    {
        Mode = SkillConfigurationMode.Auto,
        SpiritmasterAutoSkillLogicEnabled = true,
        ExecutionTree = new List<SkillConfigNode>
        {
            Node(1389, "Erosion V", "active"),
            Node(1600, "Next Skill", "active")
        },
        Spiritmaster = new SpiritmasterSkillSettings
        {
            DotSkills = new List<SpiritmasterSkillRefConfig>
            {
                new() { SkillId = 1389, SkillName = "Erosion V" }
            }
        }
    };
    var plan = SemiAutoSkillPlan.FromSettings(settings);
    var state = new SemiAutoCombatState();
    CalibrateCooldownClock(state);
    var skills = new[]
    {
        new SkillSnapshot(1389, "Erosion V", 1, 1, "Erosion", 5, false, 1_000, 0, XmlEffectRemainMs: 15_000),
        new SkillSnapshot(1600, "Next Skill", 1, 1, "Next Skill", 1, false, 1_000, 0)
    };
    var targetWithDot = CreateLockedTargetAbnormalSnapshot(
        Abnormal(1389, PlayerAbnormalStatusSnapshot.BuffCategory));
    var contextWithDot = new SpiritmasterCombatContext(
        CreateSpiritmasterPlayer(),
        CreateLocalPetRoster(isSummoned: true),
        targetWithDot);

    var firstDecision = SpiritmasterAutoSkillReleasePriority.SelectNext(
        plan,
        state,
        skills,
        new SemiAutoScriptSettings(),
        settings.Spiritmaster,
        contextWithDot,
        DateTimeOffset.Now);
    AssertEqual(1600u, firstDecision.Skill?.SkillId ?? 0, "active dot should initially skip erosion");

    var contextWithoutDot = new SpiritmasterCombatContext(
        CreateSpiritmasterPlayer(),
        CreateLocalPetRoster(isSummoned: true),
        CreateLockedTargetAbnormalSnapshot());
    var secondDecision = SpiritmasterAutoSkillReleasePriority.SelectNext(
        plan,
        state,
        skills,
        new SemiAutoScriptSettings(),
        settings.Spiritmaster,
        contextWithoutDot,
        DateTimeOffset.Now.AddSeconds(1));

    AssertEqual(SemiAutoSkillReleaseDecisionKind.PressRoot, secondDecision.Kind, "missing target dot should allow erosion");
    AssertEqual(1389u, secondDecision.Skill?.SkillId ?? 0, "target snapshot without 1389 must override local dot window");
    return Task.CompletedTask;
}

static Task TestSpiritmasterSelectorSkipsCommandWithoutPetAsync()
{
    var settings = new SkillScriptSettings
    {
        Mode = SkillConfigurationMode.Auto,
        SpiritmasterAutoSkillLogicEnabled = true,
        ExecutionTree = new List<SkillConfigNode>
        {
            Node(1674, "\u547d\u4ee4:\u6124\u6012\u4e4b\u6c14\u52bf I", "active"),
            Node(1600, "Next Skill", "active")
        }
    };
    var plan = SemiAutoSkillPlan.FromSettings(settings);
    var state = new SemiAutoCombatState();
    CalibrateCooldownClock(state);
    var skills = new[]
    {
        new SkillSnapshot(1674, "\u547d\u4ee4:\u6124\u6012\u4e4b\u6c14\u52bf I", 1, 1, "\u547d\u4ee4:\u6124\u6012\u4e4b\u6c14\u52bf", 1, false, 1_000, 0),
        new SkillSnapshot(1600, "Next Skill", 1, 1, "Next Skill", 1, false, 1_000, 0)
    };
    var context = new SpiritmasterCombatContext(
        CreateSpiritmasterPlayer(),
        SummonedPetRosterSnapshot.Empty(1000, DateTimeOffset.Now),
        LockedTargetAbnormalStatusSnapshot.Empty(DateTimeOffset.Now));

    var decision = SpiritmasterAutoSkillReleasePriority.SelectNext(
        plan,
        state,
        skills,
        new SemiAutoScriptSettings(),
        settings.Spiritmaster,
        context,
        DateTimeOffset.Now);

    AssertEqual(1600u, decision.Skill?.SkillId ?? 0, "command skill should require pet and skip to next");
    return Task.CompletedTask;
}

static async Task TestSpiritmasterTickSummonsMissingPetAsync()
{
    var settings = CreateSpiritmasterScriptSettings();
    settings.Skills.Spiritmaster.SummonSkills = new List<SpiritmasterSkillKeyRuleConfig>
    {
        new() { Key = "NumPad6" },
        new() { Key = "NumPad8" }
    };
    var plan = SemiAutoSkillPlan.FromSettings(settings.Skills);
    var keyboard = new RecordingKeyboardInput();
    var logger = new InMemoryRoadhogLogger();
    var gameApi = new FakeGameApi
    {
        Player = CreateSpiritmasterPlayer(),
        SummonedPetRoster = SummonedPetRosterSnapshot.Empty(1000, DateTimeOffset.Now),
        Skills = CreateSpiritmasterSkillSnapshots()
    };
    var controller = new SemiAutoCombatController(keyboard);

    var startedAt = DateTimeOffset.Now;
    await controller.TickAsync(CreateContext(settings, gameApi, logger), plan, new SemiAutoCombatState()).ConfigureAwait(false);
    var elapsed = DateTimeOffset.Now - startedAt;

    AssertSequence(new[] { "NumPad6", "NumPad8" }, keyboard.Keys.ToArray(), "summon key sequence");
    AssertFalse(elapsed < TimeSpan.FromMilliseconds(1900), "summon speed and summon pet keys should be separated by about two seconds");
}

static async Task TestSpiritmasterTickPrioritizesLowestPetHpRuleAsync()
{
    var settings = CreateSpiritmasterScriptSettings();
    settings.Skills.Spiritmaster.PetHpMaintenanceRules = new List<SpiritmasterPetHpRuleConfig>
    {
        new() { BelowPercent = 68, SkillId = 1678, SkillName = "Pet Heal", Key = "NumPad4" },
        new() { BelowPercent = 15, SkillId = 1785, SkillName = "Emergency Pet Heal", Key = "NumPad3" }
    };
    var plan = SemiAutoSkillPlan.FromSettings(settings.Skills);
    var keyboard = new RecordingKeyboardInput();
    var logger = new InMemoryRoadhogLogger();
    var gameApi = new FakeGameApi
    {
        Player = CreateSpiritmasterPlayer(),
        SummonedPetRoster = CreateLocalPetRoster(isSummoned: true, hpPercent: 10),
        Skills = CreateSpiritmasterSkillSnapshots(
            new SkillSnapshot(1678, "Pet Heal", 1, 1, "Pet Heal", 1, false, 1_000, 0),
            new SkillSnapshot(1785, "Emergency Pet Heal", 1, 1, "Emergency Pet Heal", 1, false, 1_000, 0))
    };
    var controller = new SemiAutoCombatController(keyboard);

    await controller.TickAsync(CreateContext(settings, gameApi, logger), plan, new SemiAutoCombatState()).ConfigureAwait(false);

    AssertSequence(new[] { "NumPad3" }, keyboard.Keys.ToArray(), "lowest pet hp threshold should run first");
}

static async Task TestSpiritmasterPetHpLocalCooldownYieldsToNormalSkillsAsync()
{
    var settings = CreateSpiritmasterScriptSettings();
    settings.Skills.Spiritmaster.PetHpMaintenanceRules = new List<SpiritmasterPetHpRuleConfig>
    {
        new()
        {
            BelowPercent = 68,
            SkillId = 1678,
            SkillName = "Pet Heal",
            Key = "NumPad4",
            CooldownMs = 10_300
        }
    };
    settings.Skills.ExecutionTree[0] = Node(1600, "Normal Skill", "active");
    var plan = SemiAutoSkillPlan.FromSettings(settings.Skills);
    var keyboard = new RecordingKeyboardInput();
    var logger = new InMemoryRoadhogLogger();
    var gameApi = new FakeGameApi
    {
        Player = CreateSpiritmasterPlayer(),
        SummonedPetRoster = CreateLocalPetRoster(isSummoned: true, hpPercent: 10),
        Skills = CreateSpiritmasterSkillSnapshots(
            new SkillSnapshot(1600, "Normal Skill", 1, 1, "Normal Skill", 1, false, 1_000, 0),
            new SkillSnapshot(1678, "Pet Heal", 1, 1, "Pet Heal", 1, false, 0, 0))
    };
    var controller = new SemiAutoCombatController(keyboard);
    var state = new SemiAutoCombatState();
    var context = CreateContext(settings, gameApi, logger);

    await controller.TickAsync(context, plan, state).ConfigureAwait(false);
    AssertSequence(new[] { "NumPad4" }, keyboard.Keys.ToArray(), "first low pet hp tick should press pet heal");

    keyboard.Keys.Clear();
    await controller.TickAsync(context, plan, state).ConfigureAwait(false);
    AssertSequence(new[] { "D1" }, keyboard.Keys.ToArray(), "local pet heal cooldown should let normal skill run");
}

static async Task TestSpiritmasterTickGatesPetBuffByDpAsync()
{
    var settings = CreateSpiritmasterScriptSettings();
    settings.Skills.Spiritmaster.PetBuffRules = new List<SpiritmasterPetBuffRuleConfig>
    {
        new() { SkillId = 1787, SkillName = "Pet DP Buff", Key = "NumPad9" }
    };
    settings.Skills.ExecutionTree[0] = Node(1600, "Normal Skill", "active");
    var plan = SemiAutoSkillPlan.FromSettings(settings.Skills);
    var keyboard = new RecordingKeyboardInput();
    var logger = new InMemoryRoadhogLogger();
    var gameApi = new FakeGameApi
    {
        Player = CreateSpiritmasterPlayer(currentDp: 1000),
        SummonedPetRoster = CreateLocalPetRoster(isSummoned: true),
        Skills = CreateSpiritmasterSkillSnapshots(
            new SkillSnapshot(1600, "Normal Skill", 1, 1, "Normal Skill", 1, false, 1_000, ActiveCooldownEnd()),
            new SkillSnapshot(1787, "Pet DP Buff", 1, 1, "Pet DP Buff", 1, false, 60_000, 0, XmlCostDp: "2000"))
    };
    var controller = new SemiAutoCombatController(keyboard);
    var state = new SemiAutoCombatState();
    CalibrateCooldownClock(state);

    await controller.TickAsync(CreateContext(settings, gameApi, logger), plan, state).ConfigureAwait(false);
    AssertSequence(Array.Empty<string>(), keyboard.Keys.ToArray(), "insufficient dp should skip pet buff");

    keyboard.Keys.Clear();
    gameApi.Player = CreateSpiritmasterPlayer(currentDp: 2000);
    await controller.TickAsync(CreateContext(settings, gameApi, logger), plan, state).ConfigureAwait(false);
    AssertSequence(new[] { "NumPad9" }, keyboard.Keys.ToArray(), "sufficient dp should press pet buff key");
}

static async Task TestSpiritmasterPetBuffSuppressesRepeatedUnknownCooldownAsync()
{
    var settings = CreateSpiritmasterScriptSettings();
    settings.SemiAuto.ConfirmTimeoutMs = 1000;
    settings.Skills.Spiritmaster.PetBuffRules = new List<SpiritmasterPetBuffRuleConfig>
    {
        new() { SkillId = 1662, SkillName = "Pet Buff", Key = "NumPad0" }
    };
    settings.Skills.ExecutionTree[0] = Node(1600, "Normal Skill", "active");
    var plan = SemiAutoSkillPlan.FromSettings(settings.Skills);
    var keyboard = new RecordingKeyboardInput();
    var logger = new InMemoryRoadhogLogger();
    var gameApi = new FakeGameApi
    {
        Player = CreateSpiritmasterPlayer(),
        SummonedPetRoster = CreateLocalPetRoster(isSummoned: true),
        Skills = CreateSpiritmasterSkillSnapshots(
            new SkillSnapshot(1600, "Normal Skill", 1, 1, "Normal Skill", 1, false, 1_000, 0),
            new SkillSnapshot(1662, "Pet Buff", 1, 1, "Pet Buff", 1, false, 30_000, StaleCooldownEnd()))
    };
    var controller = new SemiAutoCombatController(keyboard);
    var state = new SemiAutoCombatState();

    await controller.TickAsync(CreateContext(settings, gameApi, logger), plan, state).ConfigureAwait(false);
    AssertSequence(new[] { "NumPad0" }, keyboard.Keys.ToArray(), "first pet buff attempt should press configured key");

    keyboard.Keys.Clear();
    await controller.TickAsync(CreateContext(settings, gameApi, logger), plan, state).ConfigureAwait(false);
    AssertFalse(keyboard.Keys.Contains("NumPad0"), "same pet buff should not repeat while uncalibrated cooldown is suppressed");
}

static async Task TestSpiritmasterDotHitBlocksRepeatOnNextTickAsync()
{
    var settings = CreateSpiritmasterScriptSettings();
    settings.Skills.ExecutionTree = new List<SkillConfigNode>
    {
        Node(1389, "Erosion V", "active"),
        Node(1600, "Next Skill", "active")
    };
    settings.Skills.Spiritmaster.DotSkills = new List<SpiritmasterSkillRefConfig>
    {
        new() { SkillId = 1389, SkillName = "Erosion V" }
    };
    var plan = SemiAutoSkillPlan.FromSettings(settings.Skills);
    var keyboard = new RecordingKeyboardInput();
    var logger = new InMemoryRoadhogLogger();
    var gameApi = new FakeGameApi
    {
        Player = CreateSpiritmasterPlayer(),
        SummonedPetRoster = CreateLocalPetRoster(isSummoned: true),
        Skills = CreateSpiritmasterSkillSnapshots(
            new SkillSnapshot(1389, "Erosion V", 1, 1, "Erosion", 5, false, 1_000, 0, XmlEffectRemainMs: 15_000),
            new SkillSnapshot(1600, "Next Skill", 1, 1, "Next Skill", 1, false, 1_000, 0))
    };
    keyboard.AfterPress = key =>
    {
        if (key == "D1")
        {
            gameApi.LockedTargetAbnormalStatuses = CreateLockedTargetAbnormalSnapshot(
                Abnormal(1389, PlayerAbnormalStatusSnapshot.PhysicalDebuffCategory));
            gameApi.Skills = CreateSpiritmasterSkillSnapshots(
                new SkillSnapshot(1389, "Erosion V", 1, 1, "Erosion", 5, false, 1_000, ActiveCooldownEnd(), XmlEffectRemainMs: 15_000),
                new SkillSnapshot(1600, "Next Skill", 1, 1, "Next Skill", 1, false, 1_000, 0));
        }
    };
    var controller = new SemiAutoCombatController(keyboard);
    var state = new SemiAutoCombatState();
    CalibrateCooldownClock(state);

    await controller.TickAsync(CreateContext(settings, gameApi, logger), plan, state).ConfigureAwait(false);
    AssertSequence(new[] { "D1" }, keyboard.Keys.ToArray(), "first dot should be pressed before abnormal exists");

    keyboard.Keys.Clear();
    await controller.TickAsync(CreateContext(settings, gameApi, logger), plan, state).ConfigureAwait(false);
    AssertSequence(new[] { "D2" }, keyboard.Keys.ToArray(), "active dot should be skipped on next tick");
}

static async Task TestSpiritmasterOpeningAttackKeyPressesTwiceBeforeOpeningSkillAsync()
{
    var settings = CreateSpiritmasterScriptSettings();
    settings.Skills.Spiritmaster.OpeningAttackKey = "NumPad5";
    settings.Skills.OpeningSkill = new OpeningSkillConfig
    {
        Enabled = true,
        SkillId = 1701,
        SkillName = "Opening Skill",
        Key = "NumPad0"
    };
    settings.Skills.ExecutionTree = new List<SkillConfigNode>
    {
        Node(1702, "Normal Skill", "active")
    };
    var plan = SemiAutoSkillPlan.FromSettings(settings.Skills);
    var keyboard = new RecordingKeyboardInput();
    var logger = new InMemoryRoadhogLogger();
    var gameApi = new FakeGameApi
    {
        Player = CreateSpiritmasterPlayer(),
        SummonedPetRoster = CreateLocalPetRoster(isSummoned: true),
        Skills = CreateSpiritmasterSkillSnapshots(
            new SkillSnapshot(1701, "Opening Skill", 1, 1, "Opening Skill", 1, false, 1_000, 0),
            new SkillSnapshot(1702, "Normal Skill", 1, 1, "Normal Skill", 1, false, 1_000, ActiveCooldownEnd()))
    };
    var controller = new SemiAutoCombatController(keyboard);
    var state = new SemiAutoCombatState();
    CalibrateCooldownClock(state);
    var context = CreateContext(settings, gameApi, logger);

    await controller.TickAsync(context, plan, state).ConfigureAwait(false);
    AssertSequence(new[] { "NumPad5", "NumPad5" }, keyboard.Keys.ToArray(), "spiritmaster opening attack key should press twice first");

    await controller.TickAsync(context, plan, state).ConfigureAwait(false);
    AssertSequence(
        new[] { "NumPad5", "NumPad5", "NumPad0" },
        keyboard.Keys.ToArray(),
        "opening skill should run after spiritmaster opening attack key");

    keyboard.Keys.Clear();
    await controller.TickAsync(context, plan, state).ConfigureAwait(false);
    AssertFalse(keyboard.Keys.Contains("NumPad5"), "same target must not repeat spiritmaster opening attack key");
}

static Task TestSpiritmasterDotLearningPrefersSkillIdAsync()
{
    var state = new SemiAutoCombatState();
    var now = DateTimeOffset.Now;
    state.BeginSpiritmasterDotObservation(
        1389,
        1000,
        new[] { Abnormal(4000, PlayerAbnormalStatusSnapshot.PhysicalDebuffCategory) },
        now.AddSeconds(3));

    var completed = state.TryCompleteSpiritmasterDotObservation(
        1000,
        new[]
        {
            Abnormal(113582, PlayerAbnormalStatusSnapshot.PhysicalDebuffCategory),
            Abnormal(1389, PlayerAbnormalStatusSnapshot.PhysicalDebuffCategory)
        },
        now,
        out var skillId,
        out var abnormalId);

    AssertFalse(!completed, "dot observation should complete");
    AssertEqual(1389u, skillId, "learned skill id");
    AssertEqual(1389u, abnormalId, "skill id should win over XML effect id candidate");
    AssertFalse(
        !state.TryGetSpiritmasterDotAbnormalId(1389, out var remembered) || remembered != 1389,
        "remembered dot abnormal id");

    return Task.CompletedTask;
}

static async Task TestSpiritmasterWaitsForPressedSkillCooldownBeforeNextRootAsync()
{
    var settings = CreateSpiritmasterScriptSettings();
    settings.SemiAuto.ConfirmTimeoutMs = 1000;
    settings.Skills.ExecutionTree = new List<SkillConfigNode>
    {
        Node(1701, "First Skill", "active"),
        Node(1702, "Second Skill", "active")
    };
    var plan = SemiAutoSkillPlan.FromSettings(settings.Skills);
    var keyboard = new RecordingKeyboardInput();
    var logger = new InMemoryRoadhogLogger();
    var gameApi = new FakeGameApi
    {
        Player = CreateSpiritmasterPlayer(),
        SummonedPetRoster = CreateLocalPetRoster(isSummoned: true),
        Skills = CreateSpiritmasterSkillSnapshots(
            new SkillSnapshot(1701, "First Skill", 1, 1, "First Skill", 1, false, 1_000, 0),
            new SkillSnapshot(1702, "Second Skill", 1, 1, "Second Skill", 1, false, 1_000, 0))
    };
    var controller = new SemiAutoCombatController(keyboard);
    var state = new SemiAutoCombatState();
    CalibrateCooldownClock(state);
    var context = CreateContext(settings, gameApi, logger);

    await controller.TickAsync(context, plan, state).ConfigureAwait(false);
    AssertSequence(new[] { "D1" }, keyboard.Keys.ToArray(), "first root should press first");

    keyboard.Keys.Clear();
    await Task.Delay(60).ConfigureAwait(false);
    await controller.TickAsync(context, plan, state).ConfigureAwait(false);
    AssertSequence(new[] { "D1" }, keyboard.Keys.ToArray(), "first root should retry until its cooldown advances");

    keyboard.Keys.Clear();
    gameApi.Skills = CreateSpiritmasterSkillSnapshots(
        new SkillSnapshot(1701, "First Skill", 1, 1, "First Skill", 1, false, 1_000, ActiveCooldownEnd()),
        new SkillSnapshot(1702, "Second Skill", 1, 1, "Second Skill", 1, false, 1_000, 0));
    await controller.TickAsync(context, plan, state).ConfigureAwait(false);
    AssertSequence(new[] { "D2" }, keyboard.Keys.ToArray(), "next root should run after first root cooldown confirms");
}

static async Task TestSpiritmasterCombatRunsGlobalMpMaintenanceBeforeSkillsAsync()
{
    var settings = CreateSpiritmasterScriptSettings();
    settings.Maintenance.MpMaintenanceRules.Add(new MaintenanceKeyRuleConfig
    {
        BelowPercent = 95,
        Key = "NumPadAdd",
        SkillId = 1370,
        SkillName = "Mana Maintenance"
    });
    settings.Skills.ExecutionTree = new List<SkillConfigNode>
    {
        Node(1701, "First Skill", "active")
    };
    var plan = SemiAutoSkillPlan.FromSettings(settings.Skills);
    var keyboard = new RecordingKeyboardInput();
    var logger = new InMemoryRoadhogLogger();
    var gameApi = new FakeGameApi
    {
        Player = new PlayerSnapshot(
            1,
            100,
            "Spirit",
            100,
            100,
            90,
            100,
            0,
            new Vector3Snapshot(0, 0, 0),
            DateTimeOffset.Now,
            CharacterClass: AionClassCatalog.GetChineseName(AionClassId.Spiritmaster),
            CharacterClassId: AionClassId.Spiritmaster),
        SummonedPetRoster = CreateLocalPetRoster(isSummoned: true),
        Skills = CreateSpiritmasterSkillSnapshots(
            new SkillSnapshot(1701, "First Skill", 1, 1, "First Skill", 1, false, 1_000, 0),
            new SkillSnapshot(1370, "Mana Maintenance", 1, 1, "Mana Maintenance", 1, false, 1_000, 0))
    };
    var controller = new SemiAutoCombatController(keyboard);
    var state = new SemiAutoCombatState();
    CalibrateCooldownClock(state);
    keyboard.AfterPress = key =>
    {
        if (key == "NumPadAdd")
        {
            gameApi.Skills = CreateSpiritmasterSkillSnapshots(
                new SkillSnapshot(1701, "First Skill", 1, 1, "First Skill", 1, false, 1_000, 0),
                new SkillSnapshot(1370, "Mana Maintenance", 1, 1, "Mana Maintenance", 1, false, 1_000, ActiveCooldownEnd()));
        }
    };

    await controller.TickAsync(CreateContext(settings, gameApi, logger), plan, state).ConfigureAwait(false);

    AssertSequence(new[] { "NumPadAdd" }, keyboard.Keys.ToArray(), "spiritmaster combat should run global mp maintenance before ordinary skills");
    AssertFalse(
        !logger.Entries.Any(entry => entry.EventName == "semi_auto.maintenance.key_pressed"),
        "global maintenance should log key press in spiritmaster combat");
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

static async Task TestOpeningSkillPressesBeforeCOnceAsync()
{
    var settings = CreateScriptSettings();
    settings.SemiAuto.AttackKeyLoopEnabled = true;
    var openingSkill = settings.Skills.ExecutionTree.First(node => node.SkillId == 8);
    settings.Skills.OpeningSkill = new OpeningSkillConfig
    {
        Enabled = true,
        SkillId = openingSkill.SkillId,
        SkillName = openingSkill.Name,
        Key = "NumPad0"
    };

    var plan = SemiAutoSkillPlan.FromSettings(settings.Skills);
    var keyboard = new RecordingKeyboardInput();
    var logger = new InMemoryRoadhogLogger();
    var gameApi = new FakeGameApi
    {
        Skills = CreateSkillSnapshotsById(new Dictionary<uint, uint>
        {
            [8] = 0
        })
    };
    var controller = new SemiAutoCombatController(keyboard);
    var state = new SemiAutoCombatState();
    CalibrateCooldownClock(state);

    await controller.TickAsync(CreateContext(settings, gameApi, logger), plan, state).ConfigureAwait(false);
    AssertSequence(new[] { "NumPad0" }, keyboard.Keys.ToArray(), "opening skill should press before opening C");

    await controller.TickAsync(CreateContext(settings, gameApi, logger), plan, state).ConfigureAwait(false);
    AssertSequence(new[] { "NumPad0", "C" }, keyboard.Keys.ToArray(), "opening C should run after opening skill was handled");
}

static async Task TestOpeningSkillUsesServerObjectIdIdentityAsync()
{
    var settings = CreateScriptSettings();
    settings.SemiAuto.AttackKeyLoopEnabled = false;
    settings.Skills.OpeningSkill = new OpeningSkillConfig
    {
        Enabled = true,
        SkillId = 999,
        SkillName = "Opening Skill",
        Key = "NumPad0"
    };
    var plan = SemiAutoSkillPlan.FromSettings(settings.Skills);
    var keyboard = new RecordingKeyboardInput();
    var logger = new InMemoryRoadhogLogger();
    var gameApi = new FakeGameApi
    {
        TargetEntityId = 100,
        TargetOwnServerObjectId = 5000,
        Skills = new[]
        {
            new SkillSnapshot(999, "Opening Skill", 1, 1, "Opening Skill", 1, false, 1_000, 0)
        }
    };
    var controller = new SemiAutoCombatController(keyboard);
    var state = new SemiAutoCombatState();
    CalibrateCooldownClock(state);

    await controller.TickAsync(CreateContext(settings, gameApi, logger), plan, state).ConfigureAwait(false);
    AssertSequence(new[] { "NumPad0" }, keyboard.Keys.ToArray(), "opening skill should press on first server object");

    keyboard.Keys.Clear();
    gameApi.TargetEntityId = 101;
    await controller.TickAsync(CreateContext(settings, gameApi, logger), plan, state).ConfigureAwait(false);
    AssertSequence(Array.Empty<string>(), keyboard.Keys.ToArray(), "same server object should not repeat opening skill");

    gameApi.TargetOwnServerObjectId = 6000;
    await controller.TickAsync(CreateContext(settings, gameApi, logger), plan, state).ConfigureAwait(false);
    AssertSequence(new[] { "NumPad0" }, keyboard.Keys.ToArray(), "new server object should allow opening skill again");
}

static async Task TestStaleOpeningSkillCooldownIsReadyBeforeCalibrationAsync()
{
    var settings = CreateScriptSettings();
    settings.SemiAuto.AttackKeyLoopEnabled = false;
    settings.Skills.OpeningSkill = new OpeningSkillConfig
    {
        Enabled = true,
        SkillId = 999,
        SkillName = "Opening Skill",
        Key = "NumPad0"
    };
    var plan = SemiAutoSkillPlan.FromSettings(settings.Skills);
    var keyboard = new RecordingKeyboardInput();
    var logger = new InMemoryRoadhogLogger();
    var gameApi = new FakeGameApi
    {
        TargetEntityId = 100,
        TargetOwnServerObjectId = 5000,
        Skills = new[]
        {
            new SkillSnapshot(999, "Opening Skill", 1, 1, "Opening Skill", 1, false, 120_000, StaleCooldownEnd())
        }
    };
    var controller = new SemiAutoCombatController(keyboard);

    await controller.TickAsync(CreateContext(settings, gameApi, logger), plan, new SemiAutoCombatState()).ConfigureAwait(false);

    AssertSequence(new[] { "NumPad0" }, keyboard.Keys.ToArray(), "stale uncalibrated opening cooldown should be treated as ready");
}

static async Task TestCoolingOpeningSkillSkipsToCAsync()
{
    var settings = CreateScriptSettings();
    settings.SemiAuto.AttackKeyLoopEnabled = true;
    var openingSkill = settings.Skills.ExecutionTree.First(node => node.SkillId == 8);
    settings.Skills.OpeningSkill = new OpeningSkillConfig
    {
        Enabled = true,
        SkillId = openingSkill.SkillId,
        SkillName = openingSkill.Name,
        Key = "NumPad0"
    };

    var plan = SemiAutoSkillPlan.FromSettings(settings.Skills);
    var keyboard = new RecordingKeyboardInput();
    var logger = new InMemoryRoadhogLogger();
    var gameApi = new FakeGameApi
    {
        Skills = CreateSkillSnapshotsById(new Dictionary<uint, uint>
        {
            [8] = ActiveCooldownEnd()
        })
    };
    var controller = new SemiAutoCombatController(keyboard);
    var state = new SemiAutoCombatState();
    CalibrateCooldownClock(state);

    await controller.TickAsync(CreateContext(settings, gameApi, logger), plan, state).ConfigureAwait(false);

    AssertSequence(new[] { "C" }, keyboard.Keys.ToArray(), "cooling opening skill should skip directly to opening C");
    AssertFalse(
        logger.Entries.Any(entry =>
            entry.EventName == "semi_auto.key.pressed" &&
            string.Equals(entry.Fields.GetValueOrDefault("phase")?.ToString(), "opening_skill", StringComparison.Ordinal)),
        "cooling opening skill must not press the configured key");
}

static async Task TestCoolingOpeningSkillRetriesOnSameTargetAfterCooldownAsync()
{
    var settings = CreateScriptSettings();
    settings.SemiAuto.AttackKeyLoopEnabled = false;
    settings.Skills.OpeningSkill = new OpeningSkillConfig
    {
        Enabled = true,
        SkillId = 999,
        SkillName = "Opening Skill",
        Key = "NumPad0"
    };
    var plan = SemiAutoSkillPlan.FromSettings(settings.Skills);
    var keyboard = new RecordingKeyboardInput();
    var logger = new InMemoryRoadhogLogger();
    var gameApi = new FakeGameApi
    {
        TargetEntityId = 100,
        TargetOwnServerObjectId = 5000,
        Skills = new[]
        {
            new SkillSnapshot(999, "Opening Skill", 1, 1, "Opening Skill", 1, false, 1_000, CooldownEndIn(400))
        }
    };
    var controller = new SemiAutoCombatController(keyboard);
    var state = new SemiAutoCombatState();
    CalibrateCooldownClock(state);

    await controller.TickAsync(CreateContext(settings, gameApi, logger), plan, state).ConfigureAwait(false);
    AssertSequence(Array.Empty<string>(), keyboard.Keys.ToArray(), "cooling opening skill should not press");

    await Task.Delay(450).ConfigureAwait(false);
    gameApi.Skills = new[]
    {
        new SkillSnapshot(999, "Opening Skill", 1, 1, "Opening Skill", 1, false, 1_000, 0)
    };
    await controller.TickAsync(CreateContext(settings, gameApi, logger), plan, state).ConfigureAwait(false);
    AssertSequence(new[] { "NumPad0" }, keyboard.Keys.ToArray(), "same target should retry opening skill after cooldown becomes ready");

    gameApi.TargetOwnServerObjectId = 6000;
    await controller.TickAsync(CreateContext(settings, gameApi, logger), plan, state).ConfigureAwait(false);
    AssertSequence(new[] { "NumPad0", "NumPad0" }, keyboard.Keys.ToArray(), "next target should still press opening skill when ready");
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

static async Task TestMaintenanceHpRuleExitsRestBeforeConfiguredKeyAsync()
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
        Player = new PlayerSnapshot(
            1,
            100,
            "Fake",
            40,
            100,
            100,
            100,
            0,
            new Vector3Snapshot(0, 0, 0),
            DateTimeOffset.Now,
            StanceFlags: 5,
            MotionMode: 1),
        Skills = CreateSkillSnapshotsById(new Dictionary<uint, uint>
        {
            [1] = 0,
            [5] = 0,
            [6] = 0
        })
    };
    keyboard.AfterPress = key =>
    {
        if (string.Equals(key, "X", StringComparison.Ordinal))
        {
            gameApi.Player = gameApi.Player with { StanceFlags = 0, MotionMode = 0 };
        }

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

    var controller = new SemiAutoCombatController(keyboard);
    var state = new SemiAutoCombatState();
    CalibrateCooldownClock(state);

    await controller.TickAsync(CreateContext(settings, gameApi, logger), plan, state).ConfigureAwait(false);

    AssertSequence(new[] { "X", "D8" }, keyboard.Keys.ToArray(), "resting player should stand before hp maintenance key");
    AssertFalse(
        !logger.Entries.Any(entry => entry.EventName == "semi_auto.maintenance.rest_exit_before_key"),
        "maintenance rest exit should be logged before key press");
    AssertFalse(!logger.Entries.Any(entry => entry.EventName == "semi_auto.maintenance.key_pressed"), "maintenance key press should still be logged");
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

static async Task TestMaintenanceInCombatRuleSkipsWithoutAttackableTargetAsync()
{
    var settings = CreateScriptSettings();
    settings.Maintenance.SitMaintenanceEnabled = false;
    settings.Maintenance.HpMaintenanceRules.Add(new MaintenanceKeyRuleConfig
    {
        BelowPercent = 50,
        Key = "D8",
        RunTiming = MaintenanceRuleRunTiming.InCombat
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
            [6] = 0,
            [8] = 0
        })
    };
    var controller = new SemiAutoCombatController(keyboard);
    var state = new SemiAutoCombatState();

    await controller.TickAsync(CreateContext(settings, gameApi, logger), plan, state).ConfigureAwait(false);

    AssertFalse(keyboard.Keys.Contains("D8"), "in-combat maintenance must not run without an attackable target");
    AssertFalse(logger.Entries.Any(entry => entry.EventName == "semi_auto.maintenance.key_pressed"), "skipped in-combat maintenance should not log a key press");
}

static async Task TestMaintenanceInCombatRuleRunsBeforeSkillsAsync()
{
    var settings = CreateScriptSettings();
    settings.Maintenance.SitMaintenanceEnabled = false;
    settings.Maintenance.HpMaintenanceRules.Add(new MaintenanceKeyRuleConfig
    {
        BelowPercent = 50,
        Key = "D8",
        RunTiming = MaintenanceRuleRunTiming.InCombat
    });
    var plan = SemiAutoSkillPlan.FromSettings(settings.Skills);
    var keyboard = new RecordingKeyboardInput();
    var logger = new InMemoryRoadhogLogger();
    var gameApi = new FakeGameApi
    {
        Player = new PlayerSnapshot(1, 100, "Fake", 40, 100, 100, 100, 0, new Vector3Snapshot(0, 0, 0), DateTimeOffset.Now),
        TargetEntityId = 100,
        TargetCurrentHp = 1000,
        TargetMaxHp = 1000,
        TargetPosition = new Vector3Snapshot(1, 0, 0),
        Skills = CreateSkillSnapshotsById(new Dictionary<uint, uint>
        {
            [1] = 0,
            [5] = 0,
            [6] = 0,
            [8] = 0
        })
    };
    keyboard.AfterPress = key =>
    {
        if (string.Equals(key, "D8", StringComparison.Ordinal))
        {
            gameApi.Skills = CreateSkillSnapshotsById(new Dictionary<uint, uint>
            {
                [1] = 0,
                [5] = 0,
                [6] = 0,
                [8] = ActiveCooldownEnd()
            });
        }
    };
    var controller = new SemiAutoCombatController(keyboard);
    var state = new SemiAutoCombatState();

    await controller.TickAsync(CreateContext(settings, gameApi, logger), plan, state).ConfigureAwait(false);

    AssertSequence(new[] { "D8" }, keyboard.Keys.ToArray(), "in-combat maintenance key should run before ordinary skills");
    AssertFalse(!logger.Entries.Any(entry => entry.EventName == "semi_auto.maintenance.key_pressed"), "in-combat maintenance should log key press");
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

static async Task TestMaintenanceCooldownCalibrationIgnoresUnrelatedSkillAdvanceAsync()
{
    var settings = CreateScriptSettings();
    settings.Maintenance.SitMaintenanceEnabled = false;
    settings.Maintenance.HpMaintenanceRules.Add(new MaintenanceKeyRuleConfig
    {
        BelowPercent = 50,
        Key = "NumPad0",
        SkillId = 1,
        SkillName = "娣囨繃濮㈡稊瀣禈 I",
    });

    var keyboard = new RecordingKeyboardInput();
    var logger = new InMemoryRoadhogLogger();
    var unrelatedBefore = CreateSkillSnapshotsById(new Dictionary<uint, uint>
    {
        [5] = CooldownEndIn(5_000)
    }).First(skill => skill.SkillId == 5);
    var gameApi = new FakeGameApi
    {
        Player = new PlayerSnapshot(1, 100, "Fake", 40, 100, 100, 100, 0, new Vector3Snapshot(0, 0, 0), DateTimeOffset.Now),
        Skills = CreateSkillSnapshotsById(new Dictionary<uint, uint>
        {
            [1] = CooldownEndIn(30_000),
            [5] = unchecked(unrelatedBefore.CooldownEndTime + 20_000u),
            [6] = 0
        })
    };
    var controller = new SemiAutoCombatController(keyboard);
    var state = new SemiAutoCombatState();
    CalibrateCooldownClock(state);
    var originalOffset = state.CooldownTickOffsetMs;
    var observedOnly = state.TryUpdateCooldownTickCalibration(
        new[] { unrelatedBefore },
        unchecked((uint)Environment.TickCount64),
        DateTimeOffset.Now,
        out _);
    AssertFalse(observedOnly, "first unrelated cooldown observation should not calibrate");

    await controller.TryHandleMaintenanceAsync(CreateContext(settings, gameApi, logger), state, gameApi.Player).ConfigureAwait(false);

    AssertSequence(Array.Empty<string>(), keyboard.Keys.ToArray(), "cooling maintenance skill should not press key");
    AssertEqual(originalOffset, state.CooldownTickOffsetMs, "unrelated maintenance skill advance should not recalibrate cooldown offset");
    AssertFalse(
        logger.Entries.Any(entry =>
            entry.EventName == "semi_auto.cooldown.calibrated" &&
            Convert.ToUInt32(entry.Fields.GetValueOrDefault("skillId")) == 5u),
        "maintenance cooldown calibration should ignore unrelated skill advance");
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

static async Task TestMaintenanceSitReentersWhenInterruptedAsync()
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
        Player = new PlayerSnapshot(
            1,
            100,
            "Fake",
            40,
            100,
            100,
            100,
            0,
            new Vector3Snapshot(0, 0, 0),
            DateTimeOffset.Now,
            StanceFlags: 1,
            MotionMode: 0),
        Skills = CreateSkillSnapshotsById(new Dictionary<uint, uint>
        {
            [1] = 0,
            [5] = 0,
            [6] = 0
        })
    };
    var controller = new SemiAutoCombatController(keyboard);
    var state = new SemiAutoCombatState();
    state.StartMaintenanceRest(forHp: true, forMp: false);
    CalibrateCooldownClock(state);
    var context = CreateContext(settings, gameApi, logger);

    await controller.TryHandleMaintenanceAsync(context, state, gameApi.Player).ConfigureAwait(false);

    AssertSequence(new[] { "OemComma" }, keyboard.Keys.ToArray(), "interrupted rest should press comma again");
    AssertFalse(!state.IsMaintenanceResting, "maintenance rest should remain active after re-enter");
    AssertFalse(!logger.Entries.Any(entry => entry.EventName == "semi_auto.maintenance.rest_reenter"), "rest re-enter should be logged");

    keyboard.Keys.Clear();
    await controller.TryHandleMaintenanceAsync(context, state, gameApi.Player).ConfigureAwait(false);
    AssertSequence(Array.Empty<string>(), keyboard.Keys.ToArray(), "rest re-enter should be throttled");

    var restingState = new SemiAutoCombatState();
    restingState.StartMaintenanceRest(forHp: true, forMp: false);
    gameApi.Player = gameApi.Player with { StanceFlags = 5, MotionMode = 1 };
    await controller.TryHandleMaintenanceAsync(context, restingState, gameApi.Player).ConfigureAwait(false);
    AssertSequence(Array.Empty<string>(), keyboard.Keys.ToArray(), "actual resting state should not press comma again");
}

static async Task TestMaintenanceSitWaitsForHarmfulAbnormalAsync()
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
        Player = new PlayerSnapshot(
            1,
            100,
            "Fake",
            20,
            100,
            100,
            100,
            0,
            new Vector3Snapshot(0, 0, 0),
            DateTimeOffset.Now),
        PlayerAbnormalStatuses = new PlayerAbnormalStatusSnapshot(
            1,
            DateTimeOffset.Now,
            0,
            new[]
            {
                new AbnormalStatusEntrySnapshot(0, 506, 0, 0, 1, 0),
                new AbnormalStatusEntrySnapshot(0, 12345, 2, 0, 1, 0)
            }),
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

    var waiting = await controller.TryHandleMaintenanceAsync(context, state, gameApi.Player).ConfigureAwait(false);
    AssertFalse(!waiting, "harmful abnormal wait should count as maintenance work");
    AssertSequence(Array.Empty<string>(), keyboard.Keys.ToArray(), "harmful abnormal should block sit key");
    AssertFalse(state.IsMaintenanceResting, "harmful abnormal wait should not enter rest state");

    gameApi.PlayerAbnormalStatuses = new PlayerAbnormalStatusSnapshot(
        1,
        DateTimeOffset.Now,
        0,
        new[]
        {
            new AbnormalStatusEntrySnapshot(0, 506, 0, 0, 1, 0),
            new AbnormalStatusEntrySnapshot(0, 424, 0, 0, 1, 0)
        });
    var entered = await controller.TryHandleMaintenanceAsync(context, state, gameApi.Player).ConfigureAwait(false);
    AssertFalse(!entered, "category zero abnormalities should allow sit maintenance");
    AssertSequence(new[] { "OemComma" }, keyboard.Keys.ToArray(), "sit key should press when only category zero abnormalities remain");
    AssertFalse(!state.IsMaintenanceResting, "sit maintenance should enter rest when only category zero abnormalities remain");
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

static ScriptSettings CreateSpiritmasterScriptSettings()
{
    var settings = CreateScriptSettings();
    settings.Skills = new SkillScriptSettings
    {
        Mode = SkillConfigurationMode.Auto,
        SpiritmasterAutoSkillLogicEnabled = true,
        TriggerPrefixMode = "TopContiguousTriggerSkills",
        ExecutionTree = new List<SkillConfigNode>
        {
            Node(1600, "Normal Skill", "active")
        },
        Spiritmaster = new SpiritmasterSkillSettings()
    };
    return settings;
}

static PlayerSnapshot CreateSpiritmasterPlayer(ushort currentDp = 0)
{
    return new PlayerSnapshot(
        1,
        100,
        "Spirit",
        100,
        100,
        100,
        100,
        currentDp,
        new Vector3Snapshot(0, 0, 0),
        DateTimeOffset.Now,
        CharacterClass: AionClassCatalog.GetChineseName(AionClassId.Spiritmaster),
        CharacterClassId: AionClassId.Spiritmaster);
}

static SummonedPetRosterSnapshot CreateLocalPetRoster(
    bool isSummoned,
    byte hpPercent = 100,
    IReadOnlyList<AbnormalStatusEntrySnapshot>? abnormalStatuses = null)
{
    var now = DateTimeOffset.Now;
    const uint localServerObjectId = 1000;
    const uint petServerObjectId = 2000;
    var pet = isSummoned
        ? new SummonedPetSnapshot(
            true,
            2,
            petServerObjectId,
            0,
            SummonedPetSnapshot.ActorObjectType,
            0,
            "Pet",
            "Pet",
            "pet",
            "pet",
            50,
            hpPercent,
            100,
            hpPercent,
            new Vector3Snapshot(1, 0, 0),
            1,
            localServerObjectId,
            now,
            petServerObjectId,
            true,
            "test")
        : SummonedPetSnapshot.NotSummoned(localServerObjectId, now);

    return new SummonedPetRosterSnapshot(
        localServerObjectId,
        isSummoned ? petServerObjectId : 0,
        now,
        new OwnedSummonedPetSnapshot(
            SummonedPetOwnerKind.LocalPlayer,
            localServerObjectId,
            "Spirit",
            "Spirit",
            pet,
            0,
            abnormalStatuses ?? Array.Empty<AbnormalStatusEntrySnapshot>(),
            OwnerClassId: AionClassId.Spiritmaster,
            OwnerClassName: AionClassCatalog.GetChineseName(AionClassId.Spiritmaster)),
        Array.Empty<OwnedSummonedPetSnapshot>(),
        Array.Empty<uint>());
}

static LockedTargetAbnormalStatusSnapshot CreateLockedTargetAbnormalSnapshot(
    params AbnormalStatusEntrySnapshot[] entries)
{
    return new LockedTargetAbnormalStatusSnapshot(
        new LockedTargetSnapshot(
            100,
            100,
            0,
            LockedTargetSnapshot.MonsterObjectType,
            "target",
            100,
            100,
            null,
            null,
            DateTimeOffset.Now,
            1000,
            true,
            1000),
        (uint)entries.Count(entry => entry.Category == PlayerAbnormalStatusSnapshot.PhysicalDebuffCategory),
        entries,
        DateTimeOffset.Now);
}

static AbnormalStatusEntrySnapshot Abnormal(uint abnormalId, uint category)
{
    return new AbnormalStatusEntrySnapshot(0, abnormalId, category, 0, 0, 0);
}

static IReadOnlyList<SkillSnapshot> CreateSpiritmasterSkillSnapshots(params SkillSnapshot[] extraSkills)
{
    var result = new List<SkillSnapshot>
    {
        new(1600, "Normal Skill", 1, 1, "Normal Skill", 1, false, 1_000, 0)
    };

    foreach (var skill in extraSkills)
    {
        result.RemoveAll(item => item.SkillId == skill.SkillId);
        result.Add(skill);
    }

    return result;
}

static ScriptSettings CreateScriptSettings()
{
    return new ScriptSettings
    {
        MainMode = AccountMainMode.SemiAuto,
        Skills = CreateSkillSettings(),
        SemiAuto = new SemiAutoScriptSettings
        {
            TickIntervalMs = 30,
            ChainTickIntervalMs = 30,
            TargetIdleDelayMs = 50,
            KeyHoldMs = 1,
            AttackKeyLoopEnabled = false,
            AttackKeyLoopIntervalMs = 300,
            KeyGapMs = 1,
            RepeatGuardMs = 1,
            PostPressSuppressMs = 1,
            DefaultChainTimeMs = 2500
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
        ChainTimeMs = 2500,
        Children = children.ToList()
    };
}

static AccountSettingsForm CreateAccountSettingsFormForTests()
{
    var logger = new InMemoryRoadhogLogger();
    var accounts = new AccountRuntimeManager(logger);
    var runtime = new RoadhogRuntime(new FakeGameApi(), logger, accounts, null!);
    var configStore = new InMemoryAccountConfigStore(new AccountConfig
    {
        AccountName = "account1",
        ScriptSettings = CreateScriptSettings()
    });
    return new AccountSettingsForm(
        "account1",
        runtime,
        configStore,
        new InMemorySharedPathStore(),
        new InMemoryScriptProfileStore());
}

static void InvokePopulateAvailableSkillTree(
    AccountSettingsForm form,
    System.Windows.Forms.TreeView tree,
    IReadOnlyList<SkillSnapshot> skills)
{
    var method = typeof(AccountSettingsForm).GetMethod(
        "PopulateAvailableSkillTreeFromSkills",
        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
    AssertFalse(method is null, "available skill tree population method should exist");
    method!.Invoke(form, new object[] { tree, skills });
}

static (int UpdatedCount, int DeletedCount) InvokeRefreshSelectedSkillTreeToHighestCurrentSkills(
    System.Windows.Forms.TreeView tree,
    IReadOnlyList<SkillSnapshot> skills)
{
    var method = typeof(AccountSettingsForm).GetMethod(
        "RefreshSelectedSkillTreeToHighestCurrentSkills",
        System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
    AssertFalse(method is null, "selected skill tree refresh method should exist");
    var result = ((int UpdatedCount, int DeletedCount))method!.Invoke(null, new object[] { tree, skills })!;
    return result;
}

static string GetManualSkillCategoryForTest(SkillSnapshot skill)
{
    var method = typeof(AccountSettingsForm).GetMethod(
        "GetManualSkillCategory",
        System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
    AssertFalse(method is null, "manual skill category method should exist");
    return (string)method!.Invoke(null, new object[] { skill })!;
}

static System.Windows.Forms.TreeNode? FindDirectTreeNode(System.Windows.Forms.TreeNodeCollection nodes, string text)
{
    foreach (System.Windows.Forms.TreeNode node in nodes)
    {
        if (string.Equals(node.Text, text, StringComparison.Ordinal))
        {
            return node;
        }
    }

    return null;
}

static bool ContainsDirectTreeNode(System.Windows.Forms.TreeNodeCollection nodes, string text)
{
    return FindDirectTreeNode(nodes, text) is not null;
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

static int CountOccurrences(string text, string pattern)
{
    var count = 0;
    var startIndex = 0;
    while (true)
    {
        var index = text.IndexOf(pattern, startIndex, StringComparison.Ordinal);
        if (index < 0)
        {
            return count;
        }

        count++;
        startIndex = index + pattern.Length;
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

sealed class InMemoryAccountConfigStore : IAccountConfigStore
{
    private readonly Dictionary<string, AccountConfig> _accounts;

    public InMemoryAccountConfigStore(params AccountConfig[] accounts)
    {
        _accounts = accounts.ToDictionary(account => account.AccountName, account => account.Clone(), StringComparer.OrdinalIgnoreCase);
    }

    public Task<OperationResult<IReadOnlyList<AccountConfig>>> LoadAllAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<AccountConfig> accounts = _accounts.Values
            .Select(account => account.Clone())
            .ToArray();
        return Task.FromResult(OperationResult<IReadOnlyList<AccountConfig>>.Ok(accounts));
    }

    public Task<OperationResult> SaveAllAsync(IReadOnlyList<AccountConfig> accounts, CancellationToken cancellationToken = default)
    {
        _accounts.Clear();
        foreach (var account in accounts)
        {
            _accounts[account.AccountName] = account.Clone();
        }

        return Task.FromResult(OperationResult.Ok());
    }

    public Task<OperationResult> UpsertAsync(AccountConfig account, CancellationToken cancellationToken = default)
    {
        _accounts[account.AccountName] = account.Clone();
        return Task.FromResult(OperationResult.Ok());
    }
}

sealed class InMemoryHardwareDeviceResolver : IHardwareDeviceResolver
{
    private readonly IReadOnlyList<HardwareDeviceFeature> _devices;

    public InMemoryHardwareDeviceResolver(params HardwareDeviceFeature[] devices)
    {
        _devices = devices;
    }

    public IReadOnlyList<HardwareDeviceFeature> ListDevices()
    {
        return _devices;
    }

    public OperationResult<HardwareBinding> BindByKey(string accountName, string hardwareKey)
    {
        var device = _devices.FirstOrDefault(item =>
            string.Equals(item.BindingKey.Trim(), hardwareKey.Trim(), StringComparison.OrdinalIgnoreCase) ||
            item.AliasKeys.Any(alias => string.Equals(alias.Trim(), hardwareKey.Trim(), StringComparison.OrdinalIgnoreCase)));
        return device is null
            ? OperationResult<HardwareBinding>.Fail("Hardware device not found: " + hardwareKey)
            : OperationResult<HardwareBinding>.Ok(CreateBinding(accountName, device));
    }

    public OperationResult<HardwareBinding> TryAutoBind(string accountName)
    {
        return _devices.Count == 0
            ? OperationResult<HardwareBinding>.Fail("Hardware device not found.")
            : OperationResult<HardwareBinding>.Ok(CreateBinding(accountName, _devices[0]));
    }

    private static HardwareBinding CreateBinding(string accountName, HardwareDeviceFeature device)
    {
        return new HardwareBinding(
            accountName,
            device.BindingKey,
            device.BindingKind,
            device.BindingConfidence,
            device.DeviceInstanceId,
            device.ParentInstanceId,
            device.ContainerId,
            device.HardwareId,
            device.LocationKey,
            device.DisplayName,
            device.Manufacturer,
            device.VmmDeviceName,
            device.AliasKeys,
            DateTimeOffset.Now);
    }
}

sealed class InMemoryTargetProcessResolver : ITargetProcessResolver
{
    public string ResolveTargetProcessName(string? overrideProcessName = null)
    {
        return string.IsNullOrWhiteSpace(overrideProcessName) ? "Aion.bin" : overrideProcessName.Trim();
    }

    public IReadOnlyList<TargetProcessInfo> ListTargets(string? overrideProcessName = null)
    {
        return Array.Empty<TargetProcessInfo>();
    }

    public OperationResult<ProcessBinding> BindByPid(string accountName, int processId, string? overrideProcessName = null)
    {
        return OperationResult<ProcessBinding>.Fail("not used");
    }

    public OperationResult<ProcessBinding> TryAutoBind(string accountName, string? overrideProcessName = null)
    {
        return OperationResult<ProcessBinding>.Fail("not used");
    }
}

sealed class CapturingAccountWorkerLoop : IAccountWorkerLoop
{
    public AccountWorkerContext? LastContext { get; private set; }

    public Task RunAsync(AccountWorkerContext context)
    {
        LastContext = context;
        return Task.CompletedTask;
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

sealed class InMemoryScriptProfileStore : IScriptProfileStore
{
    private readonly Dictionary<string, ScriptProfileDocument> _profiles;

    public InMemoryScriptProfileStore(params ScriptProfileDocument[] profiles)
    {
        _profiles = profiles.ToDictionary(profile => profile.Name, profile => profile.Clone(), StringComparer.OrdinalIgnoreCase);
    }

    public Task<OperationResult<IReadOnlyList<ScriptProfileSummary>>> LoadSummariesAsync(
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<ScriptProfileSummary> summaries = _profiles.Values
            .Select(profile => new ScriptProfileSummary(profile.Name, profile.UpdatedAt))
            .ToArray();
        return Task.FromResult(OperationResult<IReadOnlyList<ScriptProfileSummary>>.Ok(summaries));
    }

    public Task<OperationResult<ScriptProfileDocument>> LoadAsync(
        string name,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_profiles.TryGetValue(name, out var profile)
            ? OperationResult<ScriptProfileDocument>.Ok(profile.Clone())
            : OperationResult<ScriptProfileDocument>.Fail("Profile file was not found: " + name));
    }

    public Task<OperationResult> SaveAsync(
        ScriptProfileDocument profile,
        CancellationToken cancellationToken = default)
    {
        _profiles[profile.Name] = profile.Clone();
        return Task.FromResult(OperationResult.Ok());
    }

    public Task<OperationResult> DeleteAsync(
        string name,
        CancellationToken cancellationToken = default)
    {
        _profiles.Remove(name);
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

    public PlayerAbnormalStatusSnapshot PlayerAbnormalStatuses { get; set; } =
        PlayerAbnormalStatusSnapshot.Empty(1);

    public SummonedPetSnapshot SummonedPet { get; set; } =
        SummonedPetSnapshot.NotSummoned(0, DateTimeOffset.Now);

    public SummonedPetRosterSnapshot SummonedPetRoster { get; set; } =
        SummonedPetRosterSnapshot.Empty(0, DateTimeOffset.Now);

    public IReadOnlyList<uint>? LastRequestedSkillIds { get; private set; }

    public GameApiReadContext? LastPlayerContext { get; private set; }

    public GameApiReadContext? LastSkillsContext { get; private set; }

    public GameApiReadContext? LastSummonedPetContext { get; private set; }

    public GameApiReadContext? LastSummonedPetRosterContext { get; private set; }

    public GameApiReadContext? LastLockedTargetAbnormalContext { get; private set; }

    public GameApiReadContext? LastWorldObjectsContext { get; private set; }

    public ushort TargetEntityId { get; set; } = 100;

    public uint TargetCurrentHp { get; set; } = 1000;

    public uint TargetMaxHp { get; set; } = 1000;

    public Vector3Snapshot? TargetPosition { get; set; }

    public uint TargetOwnServerObjectId { get; set; }

    public uint TargetServerObjectId { get; set; } = 1;

    public uint LocalServerObjectId { get; set; }

    public bool TargetIsTargetingLocalPlayer { get; set; } = true;

    public LockedTargetAbnormalStatusSnapshot? LockedTargetAbnormalStatuses { get; set; }

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

    public Task<OperationResult<PlayerAbnormalStatusSnapshot>> ReadPlayerAbnormalStatusesAsync(
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(OperationResult<PlayerAbnormalStatusSnapshot>.Ok(PlayerAbnormalStatuses));
    }

    public Task<OperationResult<PlayerAbnormalStatusSnapshot>> ReadPlayerAbnormalStatusesAsync(
        GameApiReadContext context,
        CancellationToken cancellationToken = default)
    {
        return ReadPlayerAbnormalStatusesAsync(cancellationToken);
    }

    public Task<OperationResult<SummonedPetSnapshot>> ReadSummonedPetAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(OperationResult<SummonedPetSnapshot>.Ok(SummonedPet));
    }

    public Task<OperationResult<SummonedPetSnapshot>> ReadSummonedPetAsync(
        GameApiReadContext context,
        CancellationToken cancellationToken = default)
    {
        LastSummonedPetContext = context;
        return ReadSummonedPetAsync(cancellationToken);
    }

    public Task<OperationResult<SummonedPetRosterSnapshot>> ReadSummonedPetRosterAsync(
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(OperationResult<SummonedPetRosterSnapshot>.Ok(SummonedPetRoster));
    }

    public Task<OperationResult<SummonedPetRosterSnapshot>> ReadSummonedPetRosterAsync(
        GameApiReadContext context,
        CancellationToken cancellationToken = default)
    {
        LastSummonedPetRosterContext = context;
        return ReadSummonedPetRosterAsync(cancellationToken);
    }

    public Task<OperationResult<LockedTargetSnapshot>> ReadLockedTargetAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(OperationResult<LockedTargetSnapshot>.Ok(new LockedTargetSnapshot(
            TargetEntityId,
            TargetOwnServerObjectId != 0 ? TargetOwnServerObjectId : TargetEntityId,
            0,
            LockedTargetSnapshot.MonsterObjectType,
            "训练用稻草人",
            TargetCurrentHp,
            TargetMaxHp,
            TargetPosition,
            null,
            DateTimeOffset.Now,
            TargetServerObjectId,
            TargetIsTargetingLocalPlayer,
            LocalServerObjectId)));
    }

    public Task<OperationResult<LockedTargetSnapshot>> ReadLockedTargetAsync(
        GameApiReadContext context,
        CancellationToken cancellationToken = default)
    {
        return ReadLockedTargetAsync(cancellationToken);
    }

    public Task<OperationResult<LockedTargetAbnormalStatusSnapshot>> ReadLockedTargetAbnormalStatusesAsync(
        CancellationToken cancellationToken = default)
    {
        var snapshot = LockedTargetAbnormalStatuses ?? new LockedTargetAbnormalStatusSnapshot(
            new LockedTargetSnapshot(
                TargetEntityId,
                TargetOwnServerObjectId != 0 ? TargetOwnServerObjectId : TargetEntityId,
                0,
                LockedTargetSnapshot.MonsterObjectType,
                "训练用稻草人",
                TargetCurrentHp,
                TargetMaxHp,
                TargetPosition,
                null,
                DateTimeOffset.Now,
                TargetServerObjectId,
                TargetIsTargetingLocalPlayer,
                LocalServerObjectId),
            0,
            Array.Empty<AbnormalStatusEntrySnapshot>(),
            DateTimeOffset.Now);

        return Task.FromResult(OperationResult<LockedTargetAbnormalStatusSnapshot>.Ok(snapshot));
    }

    public Task<OperationResult<LockedTargetAbnormalStatusSnapshot>> ReadLockedTargetAbnormalStatusesAsync(
        GameApiReadContext context,
        CancellationToken cancellationToken = default)
    {
        LastLockedTargetAbnormalContext = context;
        return ReadLockedTargetAbnormalStatusesAsync(cancellationToken);
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
        LastSkillsContext = context;
        return ReadSkillsAsync(cancellationToken);
    }

    public Task<OperationResult<IReadOnlyList<SkillSnapshot>>> ReadSkillsAsync(
        GameApiReadContext context,
        IReadOnlyCollection<uint> skillIds,
        CancellationToken cancellationToken = default)
    {
        LastSkillsContext = context;
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
        LastWorldObjectsContext = context;
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
