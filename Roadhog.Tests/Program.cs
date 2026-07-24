using Roadhog;
using Roadhog.Application;
using Roadhog.Application.BagCleanup;
using Roadhog.Application.Input;
using Roadhog.Application.SemiAuto;
using Roadhog.Application.StationaryCombat;
using Roadhog.Application.Team;
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
using Roadhog.Infrastructure.Hardware;
using Roadhog.Infrastructure.Input;
using Roadhog.Infrastructure.Paths;
using Roadhog.Infrastructure.Profiles;

if (LicenseLiveProbe.ShouldRun(args))
{
    Environment.ExitCode = LicenseLiveProbe.RunAsync().GetAwaiter().GetResult();
    return;
}

if (TeamMonitorLiveProbe.ShouldRun(args))
{
    Environment.ExitCode = TeamMonitorLiveProbe.RunAsync(args).GetAwaiter().GetResult();
    return;
}

if (VmmGameApiLiveProbe.ShouldRun(args))
{
    Environment.ExitCode = VmmGameApiLiveProbe.RunAsync(args).GetAwaiter().GetResult();
    return;
}

if (PartyMemberLiveProbe.ShouldRun(args))
{
    Environment.ExitCode = PartyMemberLiveProbe.Run(args);
    return;
}

if (TacticsSignLiveProbe.ShouldRun(args))
{
    Environment.ExitCode = TacticsSignLiveProbe.Run(args);
    return;
}

if (KmboxKeyPressProbe.ShouldRun(args))
{
    Environment.ExitCode = KmboxKeyPressProbe.RunAsync(args).GetAwaiter().GetResult();
    return;
}

var tests = new (string Name, Func<Task> Run)[]
{
    ("path recorder enforces five meter minimum", TestPathRecorderMinimumDistanceAsync),
    ("shared path store saves loads and deletes path files", TestSharedPathStoreRoundTripAsync),
    ("script profile store saves loads and deletes profile files", TestScriptProfileStoreRoundTripAsync),
    ("runtime player read uses account scoped context", TestRuntimePlayerReadUsesAccountScopeAsync),
    ("runtime path recording player read bypasses memory cache", TestRuntimePathRecordingPlayerReadBypassesMemoryCacheAsync),
    ("runtime skill read uses saved account scope when idle", TestRuntimeSkillReadUsesSavedAccountScopeWhenIdleAsync),
    ("runtime skill read maps saved hardware key to indexed fpga device", TestRuntimeSkillReadMapsSavedHardwareKeyToIndexedFpgaDeviceAsync),
    ("account start preserves configured indexed fpga device", TestAccountStartPreservesConfiguredIndexedFpgaDeviceAsync),
    ("account start lets configured vmm override hardware indexed device", TestAccountStartConfiguredVmmOverridesHardwareIndexedDeviceAsync),
    ("runtime inventory read uses account scoped context", TestRuntimeInventoryReadUsesAccountScopeAsync),
    ("runtime world object read uses account scoped context", TestRuntimeWorldObjectReadUsesAccountScopeAsync),
    ("runtime summoned pet read uses account scoped context", TestRuntimeSummonedPetReadUsesAccountScopeAsync),
    ("runtime summoned pet roster read uses account scoped context", TestRuntimeSummonedPetRosterReadUsesAccountScopeAsync),
    ("runtime team snapshot uses account scoped context", TestRuntimeTeamSnapshotUsesAccountScopeAsync),
    ("team support prioritizes mental physical then heal", TestTeamSupportPrioritizesMentalPhysicalThenHealAsync),
    ("team support ignores positive physical-category status", TestTeamSupportIgnoresPositivePhysicalCategoryStatusAsync),
    ("team support skips maintenance outside group distance", TestTeamSupportSkipsMaintenanceOutsideGroupDistanceAsync),
    ("team support retries function key until spiritmaster body selected", TestTeamSupportRetriesFunctionKeyUntilSpiritmasterBodySelectedAsync),
    ("team support uses group cleanse when multiple members need cleanse", TestTeamSupportUsesGroupCleanseWhenMultipleMembersNeedCleanseAsync),
    ("team support uses group heal without target select", TestTeamSupportUsesGroupHealWithoutTargetSelectAsync),
    ("team support heals while fighting when always", TestTeamSupportHealsWhileFightingWhenAlwaysAsync),
    ("team support heals while fighting when in combat", TestTeamSupportHealsWhileFightingWhenInCombatAsync),
    ("team support defers after-combat heal while fighting", TestTeamSupportDefersAfterCombatHealWhileFightingAsync),
    ("team support applies whitelisted maintenance buff", TestTeamSupportAppliesWhitelistedMaintenanceBuffAsync),
    ("team support defers team buff while fighting", TestTeamSupportDefersTeamBuffWhileFightingAsync),
    ("team support postpones after-combat buff while loot active", TestTeamSupportPostponesAfterCombatBuffWhileLootActiveAsync),
    ("team support skips non whitelist maintenance buff", TestTeamSupportSkipsNonWhitelistMaintenanceBuffAsync),
    ("team support skips active whitelist maintenance buff", TestTeamSupportSkipsActiveWhitelistMaintenanceBuffAsync),
    ("team support throttles missing whitelist buff retry", TestTeamSupportThrottlesMissingWhitelistBuffRetryAsync),
    ("team support keeps already selected leader", TestTeamSupportKeepsAlreadySelectedLeaderAsync),
    ("team support leader jump requires consecutive assists", TestTeamSupportLeaderJumpRequiresConsecutiveAssistsAsync),
    ("team support join combat continues while leader outside group range", TestTeamSupportJoinCombatContinuesWhileLeaderOutsideGroupRangeAsync),
    ("team support stays grouped until leader exit distance", TestTeamSupportStaysGroupedUntilLeaderExitDistanceAsync),
    ("team support waits for five consecutive leader unavailable ticks", TestTeamSupportWaitsForFiveConsecutiveLeaderUnavailableTicksAsync),
    ("team support join combat defers follow while fighting", TestTeamSupportJoinCombatDefersFollowWhileFightingAsync),
    ("team support join combat selects leader target inside group range", TestTeamSupportJoinCombatSelectsLeaderTargetInsideGroupRangeAsync),
    ("team support self defense accepts leader target attacking local player", TestTeamSupportSelfDefenseAcceptsLeaderTargetAttackingLocalPlayerAsync),
    ("team support self defense disabled rejects local target", TestTeamSupportSelfDefenseDisabledRejectsLocalTargetAsync),
    ("team support self defense scans local attacker before maintenance", TestTeamSupportSelfDefenseScansLocalAttackerBeforeMaintenanceAsync),
    ("team support self defense disabled keeps maintenance", TestTeamSupportSelfDefenseDisabledKeepsMaintenanceAsync),
    ("team support join combat waits after assist target key", TestTeamSupportJoinCombatWaitsAfterAssistTargetKeyAsync),
    ("team support join combat accepts already locked leader target", TestTeamSupportJoinCombatAcceptsAlreadyLockedLeaderTargetAsync),
    ("team support accepts leader pet target when class unknown", TestTeamSupportAcceptsLeaderPetTargetWhenClassUnknownAsync),
    ("team support join combat skips party member leader target", TestTeamSupportJoinCombatSkipsPartyMemberLeaderTargetAsync),
    ("team support sits when leader rests", TestTeamSupportSitsWhenLeaderRestsAsync),
    ("team support holds while leader rests", TestTeamSupportHoldsWhileLeaderRestsAsync),
    ("team support stands when leader stands", TestTeamSupportStandsWhenLeaderStandsAsync),
    ("team output sits when leader rests", TestTeamOutputSitsWhenLeaderRestsAsync),
    ("team output holds while leader rests", TestTeamOutputHoldsWhileLeaderRestsAsync),
    ("team output stands when leader stands", TestTeamOutputStandsWhenLeaderStandsAsync),
    ("team output assists only leader attacked monster", TestTeamOutputAssistsOnlyLeaderAttackedMonsterAsync),
    ("team output uses configured assist target key", TestTeamOutputUsesConfiguredAssistTargetKeyAsync),
    ("team output rejects non monster leader target", TestTeamOutputRejectsNonMonsterLeaderTargetAsync),
    ("team output accepts already locked leader target", TestTeamOutputAcceptsAlreadyLockedLeaderTargetAsync),
    ("team output skips party member leader target", TestTeamOutputSkipsPartyMemberLeaderTargetAsync),
    ("team output accepts monster targeting spiritmaster leader pet", TestTeamOutputAcceptsMonsterTargetingSpiritmasterLeaderPetAsync),
    ("team output accepts leader pet target when class unknown", TestTeamOutputAcceptsLeaderPetTargetWhenClassUnknownAsync),
    ("team output assists already selected leader", TestTeamOutputAssistsAlreadySelectedLeaderAsync),
    ("team output jumps every five leader assists", TestTeamOutputJumpsEveryFiveLeaderAssistsAsync),
    ("team output falls back outside group range", TestTeamOutputFallsBackOutsideGroupRangeAsync),
    ("team output stays grouped until leader exit distance", TestTeamOutputStaysGroupedUntilLeaderExitDistanceAsync),
    ("team output waits for five consecutive leader unavailable ticks", TestTeamOutputWaitsForFiveConsecutiveLeaderUnavailableTicksAsync),
    ("team output continues when leader invisible stop disabled", TestTeamOutputContinuesWhenLeaderInvisibleStopDisabledAsync),
    ("team output stops when leader dead", TestTeamOutputStopsWhenLeaderDeadAsync),
    ("team output defers follow while fighting", TestTeamOutputDefersFollowWhileFightingAsync),
    ("team output allows self defense before leader assist", TestTeamOutputAllowsSelfDefenseBeforeLeaderAssistAsync),
    ("team output follows leader when self defense disabled", TestTeamOutputFollowsLeaderWhenSelfDefenseDisabledAsync),
    ("team leader protection prioritizes healer threats", TestTeamLeaderProtectionPrioritizesHealerThreatsAsync),
    ("team leader protection only includes spiritmaster pets", TestTeamLeaderProtectionOnlyIncludesSpiritmasterPetsAsync),
    ("team leader protection ignores members outside group distance", TestTeamLeaderProtectionIgnoresMembersOutsideGroupDistanceAsync),
    ("team leader protection drives stationary defense selection", TestTeamLeaderProtectionDrivesStationaryDefenseSelectionAsync),
    ("team group distance persists from ui", TestTeamGroupDistancePersistsFromUiAsync),
    ("runtime locked target abnormal read uses account scoped context", TestRuntimeLockedTargetAbnormalReadUsesAccountScopeAsync),
#if DEBUG
    ("runtime api probe covers vmm read paths", TestRuntimeApiProbeCoversVmmReadPathsAsync),
#endif
    ("aion class catalog maps old twelve classes", TestAionClassCatalogAsync),
    ("runtime player read returns character name", TestRuntimePlayerReadReturnsCharacterNameAsync),
    ("runtime kill efficiency tracks kill intervals", TestRuntimeKillEfficiencyTracksKillIntervalsAsync),
    ("runtime warning records and clears read failures", TestRuntimeWarningRecordsAndClearsReadFailuresAsync),
    ("stationary combat records player read warning", TestStationaryCombatRecordsPlayerReadWarningAsync),
    ("service options enable logging by default", TestRoadhogServiceOptionsEnableLoggingByDefaultAsync),
    ("file logger rotates when max size is reached", TestFileLoggerRotatesWhenMaxSizeIsReachedAsync),
    ("file logger deletes expired log files", TestFileLoggerDeletesExpiredLogFilesAsync),
    ("file logger samples noisy vmm reads", TestFileLoggerSamplesNoisyVmmReadsAsync),
    ("input key map preserves Roadhog supported HID codes", TestInputKeyMapAsync),
    ("runtime test move uses configured screen point", TestRuntimeTestMoveUsesScreenPointAsync),
    ("runtime normalizes inventory window then closes", TestRuntimeNormalizesInventoryWindowThenClosesAsync),
    ("runtime normalizes inventory window and leaves open", TestRuntimeNormalizesInventoryWindowLeavesOpenAsync),
    ("runtime registers configured bag cleanup sell items", TestRuntimeRegistersConfiguredBagCleanupSellItemsAsync),
    ("runtime tests bag cleanup from npc through sell", TestRuntimeTestsBagCleanupFromNpcThroughSellAsync),
    ("bag cleanup controller stays inactive when disabled", TestBagCleanupControllerSkipsWhenDisabledAsync),
    ("bag cleanup controller sells configured items and returns", TestBagCleanupControllerSellsItemsAndReturnsAsync),
    ("bag cleanup controller detects town return before timeout", TestBagCleanupControllerDetectsTownReturnBeforeTimeoutAsync),
    ("bag cleanup controller abandons town return when attacked", TestBagCleanupControllerAbandonsTownReturnWhenAttackedAsync),
    ("bag cleanup controller reverses cleanup path when follow fails", TestBagCleanupControllerReversesCleanupPathWhenFollowFailsAsync),
    ("bag cleanup controller sells more than three items in batches", TestBagCleanupControllerSellsMoreThanThreeItemsInBatchesAsync),
    ("bag cleanup controller sells non-equipment before equipment batches", TestBagCleanupControllerSellsNonEquipmentBeforeEquipmentBatchesAsync),
    ("bag cleanup controller returns by reverse path when npc is not found", TestBagCleanupControllerReturnsWhenNpcNotFoundAsync),
    ("bag cleanup controller skips within cooldown", TestBagCleanupControllerSkipsWithinCooldownAsync),
    ("bag cleanup controller failure cools down instead of stopping", TestBagCleanupControllerFailureCoolsDownAsync),
    ("bag cleanup matcher groups weapon armor and accessory as equipment", TestBagCleanupMatcherGroupsEquipmentTypesAsync),
    ("bag cleanup matcher maps stigma item type", TestBagCleanupMatcherMapsStigmaItemTypeAsync),
    ("bag cleanup matcher excludes name keywords", TestBagCleanupMatcherExcludesNameKeywordsAsync),
    ("bag cleanup matcher maps skill book item type", TestBagCleanupMatcherMapsSkillBookItemTypeAsync),
    ("window title formats character identity", TestWindowTitleFormatsCharacterIdentityAsync),
    ("kmbox net keyboard input validates unsupported local inputs", TestKmBoxNetKeyboardInputValidationAsync),
    ("kmbox net keyboard input accepts team keys", TestKmBoxNetKeyboardInputAcceptsTeamKeysAsync),
    ("kmbox net config store saves and loads endpoint", TestKmBoxNetConfigStoreRoundTripAsync),
    ("device lease store prevents cross process device reuse", TestDeviceLeaseStorePreventsCrossProcessReuseAsync),
    ("service options use client root environment", TestRoadhogServiceOptionsUseClientRootEnvironmentAsync),
    ("license credential store encrypts and restores credential", LicenseTests.TestDpapiCredentialStoreRoundTripAsync),
    ("license activation persists credential before server request", LicenseTests.TestActivationPersistsCredentialBeforeRequestAsync),
    ("license dispose cancels pending initialize", LicenseTests.TestDisposeCancelsPendingInitializeAsync),
    ("license heartbeat denial changes runtime state", LicenseTests.TestHeartbeatDenialChangesRuntimeStateAsync),
    ("license heartbeat transient failure retries then denies", LicenseTests.TestHeartbeatTransientFailureRetriesThenDeniesAsync),
    ("account orchestrator rejects unauthorized start", LicenseTests.TestAccountOrchestratorRejectsUnauthorizedStartAsync),
    ("services load kmbox net config before input creation", TestRoadhogServicesLoadsKmBoxNetConfigAsync),
    ("account config stores shared path names only", TestAccountConfigStoresSharedPathNamesOnlyAsync),
    ("account config persists bag cleanup rules", TestAccountConfigPersistsBagCleanupRulesAsync),
    ("account config persists stationary combat position", TestAccountConfigPersistsStationaryCombatPositionAsync),
    ("stationary combat target selector keeps monsters inside radius", TestStationaryTargetSelectorAsync),
    ("stationary combat derives home from revive path endpoint", TestStationaryCombatDerivesHomeFromRevivePathEndpointAsync),
    ("stationary combat returns home when no target is available", TestStationaryCombatReturnsHomeWhenNoTargetAvailableAsync),
    ("stationary combat jumps when stuck returning home with no target", TestStationaryCombatJumpsWhenStuckReturningHomeWithNoTargetAsync),
    ("stationary combat does not return home when no target switch is disabled", TestStationaryCombatDoesNotReturnHomeWhenNoTargetSwitchDisabledAsync),
    ("stationary combat skips active filtered monsters", TestStationaryCombatSkipsActiveFilteredMonstersAsync),
    ("stationary combat state uses server object id identity", TestStationaryCombatStateUsesServerObjectIdIdentityAsync),
    ("tool bridge inventory parser reads bag items", TestToolBridgeInventoryParserReadsBagItemsAsync),
    ("tool bridge world parser reads aggressive monster flags", TestToolBridgeWorldParserReadsAggressiveFlagsAsync),
    ("vmm skill options group learned ranks by default", TestVmmSkillOptionsGroupLearnedRanksByDefaultAsync),
    ("stationary combat startup recovery follows nearest revive path point", TestStationaryCombatStartupRecoveryFollowsNearestRevivePointAsync),
    ("stationary combat startup recovery path jumps when stuck", TestStationaryCombatStartupRecoveryPathJumpsWhenStuckAsync),
    ("stationary combat startup recovery skips revive path when home is nearest", TestStationaryCombatStartupRecoverySkipsWhenHomeNearestAsync),
    ("stationary combat startup recovery defends when targeted", TestStationaryCombatStartupRecoveryDefendsWhenTargetedAsync),
    ("stationary combat startup recovery path clears nearby aggressive monsters", TestStationaryCombatStartupRecoveryPathClearsNearbyAggressiveMonstersAsync),
    ("stationary combat death recovery clicks revive and recovers before path", TestStationaryCombatDeathRecoveryClicksReviveAndRecoversBeforePathAsync),
    ("stationary combat death recovery sits before mp maintenance rule", TestStationaryCombatDeathRecoverySitsBeforeMpMaintenanceRuleAsync),
    ("stationary combat death recovery summons spiritmaster pet before revive path", TestStationaryCombatDeathRecoverySummonsSpiritmasterPetBeforeRevivePathAsync),
    ("stationary combat death recovery path defends when targeted", TestStationaryCombatDeathRecoveryPathDefendsWhenTargetedAsync),
    ("stationary combat death recovery path clears nearby aggressive monsters", TestStationaryCombatDeathRecoveryPathClearsNearbyAggressiveMonstersAsync),
    ("stationary combat death recovery path rests before continuing low hp", TestStationaryCombatDeathRecoveryPathRestsBeforeContinuingLowHpAsync),
    ("stationary combat death recovery path jumps when stuck", TestStationaryCombatDeathRecoveryPathJumpsWhenStuckAsync),
    ("stationary combat death recovery leader siphon pauses and resumes revive path", TestStationaryCombatDeathRecoveryLeaderSiphonPausesAndResumesRevivePathAsync),
    ("worker runs team output during revive path leader siphon", TestWorkerRunsTeamOutputDuringRevivePathLeaderSiphonAsync),
    ("worker continues loot during revive path leader siphon", TestWorkerContinuesLootDuringRevivePathLeaderSiphonAsync),
    ("manual path retries transient player read failures", TestManualPathRetriesTransientPlayerReadFailuresAsync),
    ("manual path fails after player read retry timeout", TestManualPathFailsAfterPlayerReadRetryTimeoutAsync),
    ("path combat worker follows configured combat path", TestPathCombatWorkerFollowsConfiguredCombatPathAsync),
    ("path combat follows revive path before distant combat path", TestPathCombatFollowsRevivePathBeforeDistantCombatPathAsync),
    ("path combat starts combat path after access path completes", TestPathCombatStartsCombatPathAfterAccessPathCompletesAsync),
    ("path combat uses configured path radius before clearing monsters", TestPathCombatUsesConfiguredRadiusBeforeClearingMonstersAsync),
    ("path combat uses configured path follow precision", TestPathCombatUsesConfiguredPathFollowPrecisionAsync),
    ("path combat returns after twenty minutes without a kill", TestPathCombatNoKillReturnStartsRevivePathAtFirstPointAsync),
    ("path combat recent kill prevents no kill return", TestPathCombatRecentKillPreventsNoKillReturnAsync),
    ("path combat failed no kill return waits before retry", TestPathCombatFailedNoKillReturnWaitsBeforeRetryAsync),
    ("path combat resumes path after kill", TestPathCombatResumesPathAfterKillAsync),
    ("worker life guard revives before semi-auto combat", TestWorkerLifeGuardRevivesBeforeSemiAutoAsync),
    ("worker life guard revives before stationary position validation", TestWorkerLifeGuardRevivesBeforeStationaryPositionValidationAsync),
    ("worker ensures spiritmaster pet before normal work", TestWorkerEnsuresSpiritmasterPetBeforeNormalWorkAsync),
    ("worker waits for spiritmaster pet summon verification", TestWorkerWaitsForSpiritmasterPetSummonVerificationAsync),
    ("stationary combat faces selected target before tab", TestStationaryCombatFacesTargetBeforeTabAsync),
    ("stationary combat resets right mouse after repeated unchanged turns", TestStationaryCombatResetsRightMouseAfterRepeatedUnchangedTurnsAsync),
    ("stationary combat target pitch follows target height", TestStationaryCombatTargetPitchFollowsTargetHeightAsync),
    ("stationary combat accepts twenty five degree pre-lock face tolerance", TestStationaryCombatAcceptsTwentyFiveDegreePreLockFaceToleranceAsync),
    ("stationary combat tabs until selected target is verified", TestStationaryCombatTabsUntilTargetVerifiedAsync),
    ("stationary combat verifies target after each tab press", TestStationaryCombatVerifiesAfterEachTabAsync),
    ("stationary combat accepts closer aggressive wrong lock after tab", TestStationaryCombatAcceptsCloserAggressiveWrongLockAfterTabAsync),
    ("stationary combat rejects closer passive wrong lock after tab", TestStationaryCombatRejectsCloserPassiveWrongLockAfterTabAsync),
    ("stationary combat nudges then accepts unchanged locked target after tab", TestStationaryCombatNudgesThenAcceptsUnchangedLockedTargetAfterTabAsync),
    ("stationary combat nudges forward when tab locks corpse", TestStationaryCombatNudgesForwardWhenTabLocksCorpseAsync),
    ("stationary combat nudges forward when tab stays on attempted corpse", TestStationaryCombatNudgesForwardWhenTabStaysOnAttemptedCorpseAsync),
    ("stationary combat nudges forward when tab lock is empty", TestStationaryCombatNudgesForwardWhenTabLockIsEmptyAsync),
    ("stationary combat pending tab verify blocks pre-acquire", TestStationaryCombatPendingTabVerifyBlocksPreAcquireAsync),
    ("stationary combat releases path follow movement after target is verified", TestStationaryCombatReleasesMovementAfterAcquireAsync),
    ("stationary combat does not pulse W while approaching same target", TestStationaryCombatDoesNotPulseWWhileApproachingAsync),
    ("stationary combat jumps while stuck approaching target", TestStationaryCombatJumpsWhileStuckApproachingTargetAsync),
    ("stationary combat ignores target when lock times out", TestStationaryCombatIgnoresTargetWhenLockTimesOutAsync),
    ("stationary combat ignores target when kill times out", TestStationaryCombatIgnoresTargetWhenKillTimesOutAsync),
    ("stationary combat ignores locked target with no damage and no targeting", TestStationaryCombatIgnoresNoDamageNoTargetingTargetAsync),
    ("stationary combat keeps locked target after damage progress", TestStationaryCombatKeepsNoTargetingTargetAfterDamageProgressAsync),
    ("stationary combat defense can select ignored local target", TestStationaryCombatDefenseCanSelectIgnoredLocalTargetAsync),
    ("stationary combat keeps fight when locked target server id matches", TestStationaryCombatKeepsFightWhenLockedServerIdMatchesAsync),
    ("stationary combat keeps current fight target when lock switches", TestStationaryCombatKeepsCurrentFightTargetWhenLockSwitchesAsync),
    ("stationary combat clears missing current fight target quickly", TestStationaryCombatClearsMissingCurrentFightTargetQuicklyAsync),
    ("stationary combat presses C until locked target targets player", TestStationaryCombatPressesCUntilLockedTargetTargetsPlayerAsync),
    ("stationary combat accepts self targeting locked target after opening attack", TestStationaryCombatAcceptsSelfTargetingLockedTargetAfterOpeningAttackAsync),
    ("stationary combat switches away from target claimed by other", TestStationaryCombatSwitchesAwayFromTargetClaimedByOtherAsync),
    ("stationary combat treats self targeting monster as unclaimed", TestStationaryCombatTreatsSelfTargetingMonsterAsUnclaimedAsync),
    ("stationary combat keeps previously engaged target while it self targets", TestStationaryCombatKeepsPreviouslyEngagedTargetWhileSelfTargetingAsync),
    ("stationary combat keeps current target that previously targeted player", TestStationaryCombatKeepsCurrentTargetThatPreviouslyTargetedPlayerAsync),
    ("stationary combat keeps spiritmaster pet targeted fight", TestStationaryCombatKeepsSpiritmasterPetTargetedFightAsync),
    ("stationary combat keeps revive path clear target claimed by other", TestStationaryCombatKeepsRevivePathClearTargetClaimedByOtherAsync),
    ("stationary combat reacquires revive path clear target claimed by other", TestStationaryCombatReacquiresRevivePathClearTargetClaimedByOtherAsync),
    ("stationary combat treats locked zero hp target as combat", TestStationaryCombatTreatsLockedZeroHpTargetAsCombatAsync),
    ("stationary combat loots locked dead target directly", TestStationaryCombatLootsLockedDeadTargetDirectlyAsync),
    ("stationary combat skips dead target when corpse is not lootable", TestStationaryCombatSkipsDeadTargetWhenCorpseIsNotLootableAsync),
    ("stationary combat attempts same loot corpse once", TestStationaryCombatAttemptsSameLootCorpseOnceAsync),
    ("stationary combat waits after kill before loot key", TestStationaryCombatWaitsAfterKillBeforeLootKeyAsync),
    ("stationary combat waits near corpse after loot key", TestStationaryCombatWaitsNearCorpseAfterLootKeyAsync),
    ("stationary combat runs after-combat maintenance after loot", TestStationaryCombatRunsAfterCombatMaintenanceAfterLootAsync),
    ("stationary combat runs after-combat maintenance without loot", TestStationaryCombatRunsAfterCombatMaintenanceWithoutLootAsync),
    ("stationary combat runs after-combat maintenance round", TestStationaryCombatRunsAfterCombatMaintenanceRoundAsync),
    ("stationary combat returns from bag cleanup through revive path before finishing loot", TestStationaryCombatReturnsFromBagCleanupThroughRevivePathBeforeFinishingLootAsync),
    ("stationary combat postpones after-combat maintenance while pet is targeted", TestStationaryCombatPostponesAfterCombatMaintenanceWhilePetIsTargetedAsync),
    ("stationary combat finishes current fight before returning home", TestStationaryCombatFinishesFightBeforeReturningHomeAsync),
    ("stationary combat reacquires adopted defense target when locked on party member", TestStationaryCombatReacquiresAdoptedDefenseTargetWhenLockedOnPartyMemberAsync),
    ("stationary combat faces adopted defense target before reacquire tab", TestStationaryCombatFacesAdoptedDefenseTargetBeforeReacquireTabAsync),
    ("stationary combat interrupts sit when targeted by monster", TestStationaryCombatInterruptsSitWhenTargetedAsync),
    ("stationary combat hp rule runs before defense target workflow", TestStationaryCombatHpRuleRunsBeforeDefenseTargetWorkflowAsync),
    ("stationary combat stops movement before hp maintenance", TestStationaryCombatStopsMovementBeforeHpMaintenanceAsync),
    ("stationary combat mp sit maintenance runs without defense target", TestStationaryCombatMpSitMaintenanceRunsWithoutDefenseTargetAsync),
    ("skill tree assigns keys by root order and chain children inherit root key", TestSkillTreeKeyMappingAsync),
    ("available skill tree keeps chain roots in normal category", TestAvailableSkillTreeKeepsChainRootsInNormalCategoryAsync),
    ("manual skill category maps target valid status as condition", TestManualSkillCategoryMapsTargetValidStatusAsConditionAsync),
    ("condition skill preempt switch persists from skill UI", TestConditionSkillPreemptSwitchPersistsFromSkillUiAsync),
    ("return home when no target switch persists from summary UI", TestReturnHomeWhenNoTargetSwitchPersistsFromSummaryUiAsync),
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
    ("stale cooldown calibration invalidates impossible combat cooldowns", TestStaleCooldownCalibrationInvalidatesImpossibleCombatCooldownsAsync),
    ("stale cooldown calibration skips zero-duration skills when invalidating", TestStaleCooldownCalibrationSkipsZeroDurationSkillsWhenInvalidatingAsync),
    ("valid cooldown calibration keeps plausible combat cooldowns cooling", TestValidCooldownCalibrationKeepsPlausibleCombatCooldownsCoolingAsync),
    ("invalidated cooldown calibration rebuilds after pressed skill advances", TestInvalidatedCooldownCalibrationRebuildsAfterPressedSkillAdvancesAsync),
    ("opening attack key switch presses C once", TestOpeningAttackKeySwitchPressesCOnceAsync),
    ("opening skill presses before C once", TestOpeningSkillPressesBeforeCOnceAsync),
    ("opening skill confirm timeout releases normal skill loop", TestOpeningSkillConfirmTimeoutReleasesNormalSkillLoopAsync),
    ("opening skill uses server object id identity", TestOpeningSkillUsesServerObjectIdIdentityAsync),
    ("stale opening skill cooldown is ready before calibration", TestStaleOpeningSkillCooldownIsReadyBeforeCalibrationAsync),
    ("cooling opening skill skips to C", TestCoolingOpeningSkillSkipsToCAsync),
    ("cooling opening skill does not retry on same target after cooldown", TestCoolingOpeningSkillDoesNotRetrySameTargetAfterCooldownAsync),
    ("maintenance hp rule presses configured key before skills", TestMaintenanceHpRulePressesConfiguredKeyAsync),
    ("maintenance hp rule exits rest before configured key", TestMaintenanceHpRuleExitsRestBeforeConfiguredKeyAsync),
    ("maintenance hp rule runs without attackable target", TestMaintenanceHpRuleRunsWithoutAttackableTargetAsync),
    ("maintenance in-combat rule skips without attackable target", TestMaintenanceInCombatRuleSkipsWithoutAttackableTargetAsync),
    ("maintenance in-combat rule runs before skills", TestMaintenanceInCombatRuleRunsBeforeSkillsAsync),
    ("maintenance mp rule presses configured key before skills", TestMaintenanceMpRulePressesConfiguredKeyAsync),
    ("maintenance mp potion matches recovery and secret potion names", TestMaintenanceMpPotionMatchesAdditionalNamesAsync),
    ("maintenance mp potion rejects wrong item type and falls back to skill", TestMaintenanceMpPotionRejectsWrongTypeAndFallsBackAsync),
    ("maintenance mp potion runs before skill and skill retries next tick", TestMaintenanceMpPotionRunsBeforeSkillAsync),
    ("maintenance global interval throttles different selected skill", TestMaintenanceGlobalIntervalThrottlesDifferentSelectedSkillAsync),
    ("after-combat mp potion skips inventory and presses once", TestAfterCombatMpPotionSkipsInventoryAndPressesOnceAsync),
    ("maintenance selected skill confirms by skill id", TestMaintenanceSelectedSkillConfirmsBySkillIdAsync),
    ("maintenance selected cooling skill skips key and continues combat", TestMaintenanceSelectedCoolingSkillSkipsKeyAsync),
    ("dp maintenance skips below required dp", TestDpMaintenanceSkipsBelowRequiredDpAsync),
    ("dp maintenance presses configured key at required dp", TestDpMaintenancePressesConfiguredKeyAtRequiredDpAsync),
    ("dp maintenance selected cooling skill skips key", TestDpMaintenanceSelectedCoolingSkillSkipsKeyAsync),
    ("status maintenance presses missing buff and learns abnormal id", TestStatusMaintenancePressesMissingBuffAndLearnsAbnormalIdAsync),
    ("support status maintenance selects self before buff", TestSupportStatusMaintenanceSelectsSelfBeforeBuffAsync),
    ("status maintenance chant follows active status", TestStatusMaintenanceChantFollowsActiveStatusAsync),
    ("status maintenance skips active category zero buff", TestStatusMaintenanceSkipsActiveCategoryZeroBuffAsync),
    ("status maintenance in-combat rule skips without target", TestStatusMaintenanceInCombatRuleSkipsWithoutTargetAsync),
    ("status maintenance cooldown does not recalibrate combat clock", TestStatusMaintenanceCooldownDoesNotRecalibrateCombatClockAsync),
    ("maintenance cooling skill observation does not recalibrate combat clock", TestMaintenanceCoolingSkillObservationDoesNotRecalibrateCombatClockAsync),
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
    ("spiritmaster tick summons when pet roster is unconfirmed", TestSpiritmasterTickSummonsWhenPetRosterIsUnconfirmedAsync),
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
    ("chain clears across target gap and target switch", TestChainClearsAcrossTargetGapAsync),
    ("chain lock prevents root fallback while child is missing", TestChainLockPreventsRootFallbackWhileChildMissingAsync),
    ("chain keeps root key and does not fall back in same tick when chain breaks", TestChainStrictOrderAsync),
    ("condition skill preempts pending chain and clears it", TestConditionSkillPreemptsPendingChainAsync),
    ("condition skill preempt switch keeps pending chain priority", TestConditionSkillPreemptSwitchKeepsPendingChainPriorityAsync),
    ("condition skill waits for target status", TestConditionSkillWaitsForTargetStatusAsync),
    ("condition skill respects cooldown", TestConditionSkillRespectsCooldownAsync),
    ("chain window uses configured chain depth", TestChainWindowUsesConfiguredDepthAsync),
    ("chain window starts when root cooldown advances", TestChainWindowStartsFromRootCooldownAsync),
    ("chain window does not reset after child advance", TestChainWindowDoesNotResetAfterChildAdvanceAsync),
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

    var customBuffer = new PathRecordingBuffer();
    customBuffer.TryAdd(new Vector3Snapshot(0, 0, 0), now);
    var belowCustomMinimum = customBuffer.TryAdd(new Vector3Snapshot(1.9F, 0, 0), now.AddSeconds(1), 2.0D);
    AssertFalse(belowCustomMinimum.Success, "point below configured minimum must be skipped");
    var atCustomMinimum = customBuffer.TryAdd(new Vector3Snapshot(2.0F, 0, 0), now.AddSeconds(2), 2.0D);
    AssertFalse(!atCustomMinimum.Success, "point at configured minimum below five meters must be accepted");
    AssertEqual(2, customBuffer.Count, "custom minimum accepted point count");

    var denseBuffer = new PathRecordingBuffer();
    denseBuffer.TryAdd(new Vector3Snapshot(0, 0, 0), now);
    var belowDenseMinimum = denseBuffer.TryAdd(new Vector3Snapshot(0.29F, 0, 0), now.AddMilliseconds(100), 0.3D);
    AssertFalse(belowDenseMinimum.Success, "point below 0.3 meter configured minimum must be skipped");
    var atDenseMinimum = denseBuffer.TryAdd(new Vector3Snapshot(0.3F, 0, 0), now.AddMilliseconds(200), 0.3D);
    AssertFalse(!atDenseMinimum.Success, "point at 0.3 meter configured minimum must be accepted");
    AssertEqual(2, denseBuffer.Count, "dense minimum accepted point count");

    var interpolatedBuffer = new PathRecordingBuffer();
    interpolatedBuffer.TryAddDense(new Vector3Snapshot(0, 0, 0), now, 0.3D);
    var interpolated = interpolatedBuffer.TryAddDense(new Vector3Snapshot(1.2F, 0, 0), now.AddMilliseconds(400), 0.3D);
    AssertFalse(!interpolated.Success, "dense recording should accept a long segment");
    AssertEqual(5, interpolatedBuffer.Count, "dense recording should insert intermediate points");
    AssertEqual(0.3D, Math.Round(interpolatedBuffer.Points[1].SegmentDistance, 2), "first dense segment distance");
    AssertEqual(1.2D, Math.Round(interpolatedBuffer.TotalDistance, 2), "dense recording total distance");

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
        settings.Paths.DeathReviveClickX = 620;
        settings.Paths.DeathReviveClickY = 340;

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
        AssertEqual(620, loaded.Value?.Settings.Paths.DeathReviveClickX ?? 0, "loaded death revive click x");
        AssertEqual(340, loaded.Value?.Settings.Paths.DeathReviveClickY ?? 0, "loaded death revive click y");

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

static async Task TestRuntimePathRecordingPlayerReadBypassesMemoryCacheAsync()
{
    var logger = new InMemoryRoadhogLogger();
    var accounts = new AccountRuntimeManager(logger);
    accounts.MarkStarting(new AccountConfig
    {
        AccountName = "record-scope",
        ProcessId = 713,
        TargetProcessName = "Aion.bin",
        VmmDeviceName = "fpga"
    });

    var gameApi = new FakeGameApi
    {
        Player = new PlayerSnapshot(
            10,
            11,
            "Record Character",
            100,
            100,
            50,
            50,
            0,
            new Vector3Snapshot(1, 2, 3),
            DateTimeOffset.Now)
    };
    var runtime = new RoadhogRuntime(gameApi, logger, accounts, null!);

    var normalResult = await runtime.ReadPlayerAsync("record-scope").ConfigureAwait(false);

    AssertFalse(!normalResult.Success, "normal player read should succeed");
    AssertFalse(gameApi.LastPlayerContext?.BypassMemoryCache ?? true, "normal player read should use default VMM cache");

    var recordingResult = await runtime.ReadPlayerForPathRecordingAsync("record-scope").ConfigureAwait(false);

    AssertFalse(!recordingResult.Success, "path recording player read should succeed");
    AssertFalse(!(gameApi.LastPlayerContext?.BypassMemoryCache ?? false), "path recording player read should bypass VMM cache");
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

static async Task TestRuntimeInventoryReadUsesAccountScopeAsync()
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
        InventoryItems = new[]
        {
            new InventoryItemSnapshot(100, 200, "白色魔石", 1, 0, false)
        }
    };
    var runtime = new RoadhogRuntime(gameApi, logger, accounts, null!);

    var result = await runtime.RefreshInventoryAsync("account-scope").ConfigureAwait(false);

    AssertFalse(!result.Success, "runtime inventory read should succeed");
    AssertEqual(1, result.Value?.Count ?? 0, "inventory item count");
    AssertEqual(712, gameApi.LastInventoryContext?.ProcessId ?? 0, "scoped process id");
    AssertEqual("Aion.bin", gameApi.LastInventoryContext?.TargetProcessName ?? string.Empty, "scoped process name");
    AssertEqual("fpga", gameApi.LastInventoryContext?.VmmDeviceName ?? string.Empty, "scoped vmm device");
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

static async Task TestRuntimeTeamSnapshotUsesAccountScopeAsync()
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
    var localServerObjectId = 1711370025U;
    var partyServerObjectId = 1711370100U;
    var partyPet = new OwnedSummonedPetSnapshot(
        SummonedPetOwnerKind.PartyMember,
        partyServerObjectId,
        "KiraHa",
        "primary",
        new SummonedPetSnapshot(
            true,
            65510,
            2160000010,
            46,
            SummonedPetSnapshot.ActorObjectType,
            201019,
            "Earth Spirit",
            "Dark_Summon_EarthElemental_G3",
            "Summon_Pet",
            "Pet_Dark",
            42,
            5683,
            5740,
            99,
            new Vector3Snapshot(4, 5, 6),
            5.5,
            localServerObjectId,
            capturedAt,
            0,
            true,
            "owner+static-summon-pet"),
        0,
        Array.Empty<AbnormalStatusEntrySnapshot>(),
        OwnerClassId: AionClassId.Spiritmaster,
        OwnerClassName: "Spiritmaster");

    var gameApi = new FakeGameApi
    {
        Party = new PartySnapshot(
            1141852,
            0x3F,
            3,
            localServerObjectId,
            localServerObjectId,
            100,
            "HiApple",
            new Vector3Snapshot(1, 2, 3),
            0,
            3,
            capturedAt,
            new[]
            {
                CreatePartyMemberSnapshot(localServerObjectId, "HiApple", true, true, 0.0),
                CreatePartyMemberSnapshot(partyServerObjectId, "KiraHa", false, false, 2.4),
                CreatePartyMemberSnapshot(1711370200, "Jone", false, false, 2.8)
            }),
        SummonedPetRoster = new SummonedPetRosterSnapshot(
            localServerObjectId,
            0,
            capturedAt,
            new OwnedSummonedPetSnapshot(
                SummonedPetOwnerKind.LocalPlayer,
                localServerObjectId,
                "HiApple",
                string.Empty,
                SummonedPetSnapshot.NotSummoned(localServerObjectId, capturedAt),
                0,
                Array.Empty<AbnormalStatusEntrySnapshot>()),
            new[] { partyPet },
            new[] { partyServerObjectId, 1711370200U })
    };
    var runtime = new RoadhogRuntime(gameApi, logger, accounts, null!);

    var result = await runtime.ReadTeamSnapshotAsync("account-scope").ConfigureAwait(false);

    AssertFalse(!result.Success, "runtime team snapshot read should succeed");
    var snapshot = result.Value ?? throw new InvalidOperationException("team snapshot was null");
    AssertEqual(3, snapshot.Members.Count, "team member count");
    AssertEqual(1, snapshot.Members[0].FunctionKeyNumber, "self function key");
    AssertEqual(2, snapshot.Members[1].FunctionKeyNumber, "first teammate function key");
    AssertEqual(3, snapshot.Members[2].FunctionKeyNumber, "second teammate function key");
    AssertEqual(partyPet.Pet.ServerObjectId, snapshot.Members[1].SummonedPet?.Pet.ServerObjectId ?? 0, "party member pet");
    AssertFalse(!snapshot.Party.LocalIsLeader, "local should be leader");
    AssertEqual(712, gameApi.LastPartyContext?.ProcessId ?? 0, "party scoped process id");
    AssertEqual(712, gameApi.LastSummonedPetRosterContext?.ProcessId ?? 0, "pet roster scoped process id");
    AssertEqual("Aion.bin", gameApi.LastPartyContext?.TargetProcessName ?? string.Empty, "party scoped process name");
    AssertEqual("fpga", gameApi.LastPartyContext?.VmmDeviceName ?? string.Empty, "party scoped vmm device");
}

static async Task TestTeamSupportPrioritizesMentalPhysicalThenHealAsync()
{
    var keyboard = new RecordingKeyboardInput();
    var logger = new InMemoryRoadhogLogger();
    var self = CreatePartyMemberSnapshot(1000, "Healer", true, false, 0.0) with
    {
        Class = AionClassId.Cleric,
        ClassId = (byte)AionClassId.Cleric,
        ClassName = "Cleric"
    };
    var leader = CreatePartyMemberSnapshot(2000, "Leader", false, true, 4.0) with
    {
        CurrentHp = 50,
        MaxHp = 100,
        AbnormalStatuses = new[]
        {
            Abnormal(1636, 0),
            Abnormal(1632, PlayerAbnormalStatusSnapshot.PhysicalDebuffCategory)
        }
    };
    var gameApi = CreateTeamSupportGameApi(self, leader);
    keyboard.AfterPress = key =>
    {
        if (string.Equals(key, "F2", StringComparison.Ordinal))
        {
            gameApi.TargetOwnServerObjectId = leader.ServerObjectId;
        }
    };

    var controller = new TeamSupportController(keyboard, CreateTeamSupportAbnormalCatalog());
    var context = CreateContext(CreateTeamSupportSettings(), gameApi, logger);
    var state = new TeamSupportState();

    var mentalResult = await controller.TickAsync(context, state).ConfigureAwait(false);
    AssertFalse(!mentalResult.ShouldSkipNormalWork, "mental cleanse should consume the team support tick");
    AssertSequence(new[] { "F2", "NumPad8" }, keyboard.Keys.ToArray(), "mental cleanse key order");

    keyboard.Keys.Clear();
    gameApi.Party = CreateTeamSupportParty(self, leader with
    {
        AbnormalStatuses = new[] { Abnormal(1632, PlayerAbnormalStatusSnapshot.PhysicalDebuffCategory) }
    });

    await controller.TickAsync(context, state).ConfigureAwait(false);
    AssertSequence(new[] { "NumPad7" }, keyboard.Keys.ToArray(), "physical cleanse should run after mental clears");

    keyboard.Keys.Clear();
    gameApi.Party = CreateTeamSupportParty(self, leader with
    {
        CurrentHp = 50,
        MaxHp = 100,
        AbnormalStatuses = Array.Empty<AbnormalStatusEntrySnapshot>()
    });

    await controller.TickAsync(context, state).ConfigureAwait(false);
    AssertSequence(new[] { "NumPad1" }, keyboard.Keys.ToArray(), "heal should run after cleanse candidates clear");
}

static async Task TestTeamSupportIgnoresPositivePhysicalCategoryStatusAsync()
{
    var keyboard = new RecordingKeyboardInput();
    var logger = new InMemoryRoadhogLogger();
    var self = CreatePartyMemberSnapshot(1000, "Healer", true, false, 0.0) with
    {
        Class = AionClassId.Cleric,
        ClassId = (byte)AionClassId.Cleric,
        ClassName = "Cleric"
    };
    var leader = CreatePartyMemberSnapshot(2000, "Leader", false, true, 4.0) with
    {
        CurrentHp = 100,
        MaxHp = 100,
        AbnormalStatuses = new[] { Abnormal(8232, PlayerAbnormalStatusSnapshot.PhysicalDebuffCategory) }
    };
    var gameApi = CreateTeamSupportGameApi(self, leader);
    keyboard.AfterPress = key =>
    {
        if (string.Equals(key, "F2", StringComparison.Ordinal))
        {
            SetFakeLockedTarget(gameApi, leader.ServerObjectId, 0, 0, 0);
        }
    };
    var controller = new TeamSupportController(keyboard, CreateTeamSupportAbnormalCatalog());
    var context = CreateContext(CreateTeamSupportSettings(), gameApi, logger);

    await controller.TickAsync(context, new TeamSupportState()).ConfigureAwait(false);

    AssertSequence(new[] { "F2", "C" }, keyboard.Keys.ToArray(), "positive category-2 status should only return to leader assist");
    AssertFalse(keyboard.Keys.Contains("NumPad7"), "positive category-2 status must not press cleanse");
}

static async Task TestTeamSupportSkipsMaintenanceOutsideGroupDistanceAsync()
{
    var keyboard = new RecordingKeyboardInput();
    var logger = new InMemoryRoadhogLogger();
    var self = CreatePartyMemberSnapshot(1000, "Healer", true, false, 0.0) with
    {
        Class = AionClassId.Cleric,
        ClassId = (byte)AionClassId.Cleric,
        ClassName = "Cleric",
        CurrentHp = 50,
        MaxHp = 100
    };
    var farLeader = CreatePartyMemberSnapshot(2000, "Leader", false, true, 30.0) with
    {
        CurrentHp = 100,
        MaxHp = 100
    };
    var settings = CreateTeamSupportSettings();
    settings.Team.GroupDistanceMeters = 20.0D;
    var controller = new TeamSupportController(keyboard, CreateTeamSupportAbnormalCatalog());

    var result = await controller
        .TickAsync(CreateContext(settings, CreateTeamSupportGameApi(self, farLeader), logger), new TeamSupportState())
        .ConfigureAwait(false);

    AssertFalse(result.ShouldSkipNormalWork, "leader outside group range should fall back to ordinary work");
    AssertEqual(0, keyboard.Keys.Count, "support maintenance should not heal self before the leader is inside group range");
}

static async Task TestTeamSupportRetriesFunctionKeyUntilSpiritmasterBodySelectedAsync()
{
    var keyboard = new RecordingKeyboardInput();
    var logger = new InMemoryRoadhogLogger();
    const uint petServerObjectId = 3000;
    var self = CreatePartyMemberSnapshot(1000, "Healer", true, false, 0.0) with
    {
        Class = AionClassId.Cleric,
        ClassId = (byte)AionClassId.Cleric,
        ClassName = "Cleric"
    };
    var spiritmaster = CreatePartyMemberSnapshot(2000, "Spirit", false, true, 4.0) with
    {
        CurrentHp = 40,
        MaxHp = 100,
        Class = AionClassId.Spiritmaster,
        ClassId = (byte)AionClassId.Spiritmaster,
        ClassName = "Spiritmaster"
    };
    var gameApi = CreateTeamSupportGameApi(self, spiritmaster);
    gameApi.SummonedPetRoster = CreateTeamSupportRoster(self.ServerObjectId, spiritmaster, petServerObjectId);

    var f2PressCount = 0;
    keyboard.AfterPress = key =>
    {
        if (!string.Equals(key, "F2", StringComparison.Ordinal))
        {
            return;
        }

        f2PressCount++;
        gameApi.TargetOwnServerObjectId = f2PressCount == 1
            ? petServerObjectId
            : spiritmaster.ServerObjectId;
    };

    var controller = new TeamSupportController(keyboard, CreateTeamSupportAbnormalCatalog());
    var context = CreateContext(CreateTeamSupportSettings(), gameApi, logger);

    await controller.TickAsync(context, new TeamSupportState()).ConfigureAwait(false);

    AssertSequence(new[] { "F2", "F2", "NumPad1" }, keyboard.Keys.ToArray(), "spiritmaster party key should retry until body is selected");
}

static async Task TestTeamSupportUsesGroupCleanseWhenMultipleMembersNeedCleanseAsync()
{
    var keyboard = new RecordingKeyboardInput();
    var logger = new InMemoryRoadhogLogger();
    var self = CreatePartyMemberSnapshot(1000, "Healer", true, false, 0.0) with
    {
        Class = AionClassId.Cleric,
        ClassId = (byte)AionClassId.Cleric,
        ClassName = "Cleric",
        AbnormalStatuses = new[] { Abnormal(1636, 0) }
    };
    var leader = CreatePartyMemberSnapshot(2000, "Leader", false, true, 4.0) with
    {
        AbnormalStatuses = new[] { Abnormal(1632, PlayerAbnormalStatusSnapshot.PhysicalDebuffCategory) }
    };
    var settings = CreateTeamSupportSettings();
    settings.Team.Support!.GroupCleanseKey = "NumPad9";
    var controller = new TeamSupportController(keyboard, CreateTeamSupportAbnormalCatalog());

    await controller
        .TickAsync(CreateContext(settings, CreateTeamSupportGameApi(self, leader), logger), new TeamSupportState())
        .ConfigureAwait(false);

    AssertSequence(new[] { "NumPad9" }, keyboard.Keys.ToArray(), "group cleanse should not select an individual member first");
}

static async Task TestTeamSupportUsesGroupHealWithoutTargetSelectAsync()
{
    var keyboard = new RecordingKeyboardInput();
    var logger = new InMemoryRoadhogLogger();
    var self = CreatePartyMemberSnapshot(1000, "Healer", true, false, 0.0) with
    {
        Class = AionClassId.Cleric,
        ClassId = (byte)AionClassId.Cleric,
        ClassName = "Cleric"
    };
    var leader = CreatePartyMemberSnapshot(2000, "Leader", false, true, 4.0) with
    {
        CurrentHp = 50,
        MaxHp = 100
    };
    var settings = CreateTeamSupportSettings();
    settings.Team.Support!.HealSkillRules = new List<TeamHealSkillRuleConfig>
    {
        new()
        {
            BelowPercent = 90,
            Key = "NumPad2",
            RunTiming = MaintenanceRuleRunTiming.Always,
            TargetType = TeamHealSkillTargetType.Group
        }
    };
    var controller = new TeamSupportController(keyboard, CreateTeamSupportAbnormalCatalog());

    await controller
        .TickAsync(CreateContext(settings, CreateTeamSupportGameApi(self, leader), logger), new TeamSupportState())
        .ConfigureAwait(false);

    AssertSequence(new[] { "NumPad2" }, keyboard.Keys.ToArray(), "group heal should press the configured key without F-key targeting");
}

static async Task TestTeamSupportHealsWhileFightingWhenAlwaysAsync()
{
    var keyboard = new RecordingKeyboardInput();
    var logger = new InMemoryRoadhogLogger();
    var self = CreatePartyMemberSnapshot(1000, "Healer", true, false, 0.0) with
    {
        Class = AionClassId.Cleric,
        ClassId = (byte)AionClassId.Cleric,
        ClassName = "Cleric"
    };
    var leader = CreatePartyMemberSnapshot(2000, "Leader", false, true, 4.0) with
    {
        CurrentHp = 50,
        MaxHp = 100
    };
    var gameApi = CreateTeamSupportGameApi(self, leader);
    keyboard.AfterPress = key =>
    {
        if (string.Equals(key, "F2", StringComparison.Ordinal))
        {
            gameApi.TargetOwnServerObjectId = leader.ServerObjectId;
        }
    };
    var combatState = new StationaryCombatState
    {
        Fighting = true,
        CurrentTargetEntityId = 100,
        CurrentTargetServerObjectId = 5000
    };
    var controller = new TeamSupportController(keyboard, CreateTeamSupportAbnormalCatalog());

    var result = await controller
        .TickAsync(CreateContext(CreateTeamSupportSettings(), gameApi, logger), new TeamSupportState(), combatState)
        .ConfigureAwait(false);

    AssertFalse(!result.ShouldSkipNormalWork, "always heal should consume the active-combat team support tick");
    AssertSequence(new[] { "F2", "NumPad1" }, keyboard.Keys.ToArray(), "always heal should select the injured member during combat");
    AssertFalse(!combatState.Fighting, "team heal should preserve the current combat state for the next tick");
    AssertEqual((uint)5000, combatState.CurrentTargetServerObjectId, "team heal should preserve the current combat target");
    AssertFalse(
        !logger.Entries.Any(entry =>
            entry.EventName == "team_support.action_pressed" &&
            string.Equals(entry.Fields["action"]?.ToString(), "Heal", StringComparison.Ordinal)),
        "active-combat team heal should log its action press");
}

static async Task TestTeamSupportHealsWhileFightingWhenInCombatAsync()
{
    var keyboard = new RecordingKeyboardInput();
    var logger = new InMemoryRoadhogLogger();
    var self = CreatePartyMemberSnapshot(1000, "Healer", true, false, 0.0) with
    {
        Class = AionClassId.Cleric,
        ClassId = (byte)AionClassId.Cleric,
        ClassName = "Cleric"
    };
    var leader = CreatePartyMemberSnapshot(2000, "Leader", false, true, 4.0) with
    {
        CurrentHp = 50,
        MaxHp = 100
    };
    var settings = CreateTeamSupportSettings();
    settings.Team.Support!.HealSkillRules[0].RunTiming = MaintenanceRuleRunTiming.InCombat;
    settings.Team.Support.HealSkillRules[0].TargetType = TeamHealSkillTargetType.Group;
    var combatState = new StationaryCombatState
    {
        Fighting = true,
        CurrentTargetEntityId = 100,
        CurrentTargetServerObjectId = 5000
    };
    var controller = new TeamSupportController(keyboard, CreateTeamSupportAbnormalCatalog());

    var result = await controller
        .TickAsync(CreateContext(settings, CreateTeamSupportGameApi(self, leader), logger), new TeamSupportState(), combatState)
        .ConfigureAwait(false);

    AssertFalse(!result.ShouldSkipNormalWork, "in-combat heal should consume the active-combat team support tick");
    AssertSequence(new[] { "NumPad1" }, keyboard.Keys.ToArray(), "in-combat group heal should press without changing target");
}

static async Task TestTeamSupportDefersAfterCombatHealWhileFightingAsync()
{
    var keyboard = new RecordingKeyboardInput();
    var logger = new InMemoryRoadhogLogger();
    var self = CreatePartyMemberSnapshot(1000, "Healer", true, false, 0.0) with
    {
        Class = AionClassId.Cleric,
        ClassId = (byte)AionClassId.Cleric,
        ClassName = "Cleric"
    };
    var leader = CreatePartyMemberSnapshot(2000, "Leader", false, true, 4.0) with
    {
        CurrentHp = 50,
        MaxHp = 100
    };
    var settings = CreateTeamSupportSettings();
    settings.Team.Support!.HealSkillRules[0].RunTiming = MaintenanceRuleRunTiming.AfterCombat;
    var gameApi = CreateTeamSupportGameApi(self, leader);
    keyboard.AfterPress = key =>
    {
        if (string.Equals(key, "F2", StringComparison.Ordinal))
        {
            gameApi.TargetOwnServerObjectId = leader.ServerObjectId;
        }
    };
    var combatState = new StationaryCombatState
    {
        Fighting = true,
        CurrentTargetEntityId = 100,
        CurrentTargetServerObjectId = 5000
    };
    var controller = new TeamSupportController(keyboard, CreateTeamSupportAbnormalCatalog());

    var result = await controller
        .TickAsync(CreateContext(settings, gameApi, logger), new TeamSupportState(), combatState)
        .ConfigureAwait(false);

    AssertFalse(result.ShouldSkipNormalWork, "after-combat heal should let active combat continue");
    AssertEqual(0, keyboard.Keys.Count, "after-combat heal should not select or heal a member during combat");
    AssertFalse(
        logger.Entries.Any(entry => entry.EventName == "team_support.action_pressed"),
        "deferred after-combat heal should not log an action press");
    AssertFalse(
        !logger.Entries.Any(entry =>
            entry.EventName == "team_support.follow.deferred" &&
            string.Equals(entry.Fields["reason"]?.ToString(), "active_combat_target", StringComparison.Ordinal)),
        "active combat defer reason should be logged");

    combatState.Fighting = false;
    combatState.CurrentTargetEntityId = 0;
    combatState.CurrentTargetServerObjectId = 0;
    keyboard.Keys.Clear();

    var afterCombatResult = await controller
        .TickAsync(CreateContext(settings, gameApi, logger), new TeamSupportState(), combatState)
        .ConfigureAwait(false);

    AssertFalse(!afterCombatResult.ShouldSkipNormalWork, "after-combat heal should consume the first idle support tick");
    AssertSequence(new[] { "F2", "NumPad1" }, keyboard.Keys.ToArray(), "after-combat heal should run after combat state clears");
}

static async Task TestTeamSupportAppliesWhitelistedMaintenanceBuffAsync()
{
    var keyboard = new RecordingKeyboardInput();
    var logger = new InMemoryRoadhogLogger();
    const uint lifeBlessingStatusId = 8101;
    var self = CreatePartyMemberSnapshot(1000, "Healer", true, false, 0.0) with
    {
        Class = AionClassId.Cleric,
        ClassId = (byte)AionClassId.Cleric,
        ClassName = "Cleric"
    };
    var leader = CreatePartyMemberSnapshot(2000, "Leader", false, true, 4.0) with
    {
        CurrentHp = 100,
        MaxHp = 100,
        AbnormalStatuses = Array.Empty<AbnormalStatusEntrySnapshot>()
    };
    var gameApi = CreateTeamSupportGameApi(self, leader);
    keyboard.AfterPress = key =>
    {
        if (string.Equals(key, "F2", StringComparison.Ordinal))
        {
            SetFakeLockedTarget(gameApi, leader.ServerObjectId, 0, 0, 0);
        }
    };

    var settings = CreateTeamSupportSettings();
    settings.Maintenance.StatusMaintenanceRules.Add(new StatusMaintenanceRuleConfig
    {
        Key = "NumPad4",
        SkillId = 9100,
        SkillName = "\u751F\u547D\u7684\u795D\u798F IV",
        AbnormalStatusId = lifeBlessingStatusId,
        RunTiming = MaintenanceRuleRunTiming.Always
    });
    var controller = new TeamSupportController(keyboard, CreateTeamSupportAbnormalCatalog());

    await controller.TickAsync(CreateContext(settings, gameApi, logger), new TeamSupportState()).ConfigureAwait(false);

    AssertSequence(new[] { "F2", "NumPad4" }, keyboard.Keys.ToArray(), "missing whitelisted team buff should target teammate and press configured key");
    var entry = logger.Entries.LastOrDefault(entry => entry.EventName == "team_support.action_pressed");
    AssertFalse(entry is null, "team buff should log action press");
    AssertEqual("TeamBuff", entry!.Fields["action"]?.ToString() ?? string.Empty, "team buff action kind");
    AssertEqual(lifeBlessingStatusId, Convert.ToUInt32(entry.Fields["abnormalStatusId"]), "team buff abnormal id");
}

static async Task TestTeamSupportDefersTeamBuffWhileFightingAsync()
{
    var keyboard = new RecordingKeyboardInput();
    var logger = new InMemoryRoadhogLogger();
    const uint lifeBlessingStatusId = 8106;
    var self = CreatePartyMemberSnapshot(1000, "Healer", true, false, 0.0) with
    {
        Class = AionClassId.Cleric,
        ClassId = (byte)AionClassId.Cleric,
        ClassName = "Cleric"
    };
    var leader = CreatePartyMemberSnapshot(2000, "Leader", false, true, 4.0) with
    {
        CurrentHp = 100,
        MaxHp = 100,
        AbnormalStatuses = Array.Empty<AbnormalStatusEntrySnapshot>()
    };
    var settings = CreateTeamSupportSettings();
    settings.Maintenance.StatusMaintenanceRules.Add(new StatusMaintenanceRuleConfig
    {
        Key = "NumPad4",
        SkillId = 9106,
        SkillName = "\u751F\u547D\u7684\u795D\u798F VI",
        AbnormalStatusId = lifeBlessingStatusId,
        RunTiming = MaintenanceRuleRunTiming.Always
    });
    var combatState = new StationaryCombatState
    {
        Fighting = true,
        CurrentTargetEntityId = 100,
        CurrentTargetServerObjectId = 5000
    };

    var controller = new TeamSupportController(keyboard, CreateTeamSupportAbnormalCatalog());
    var result = await controller
        .TickAsync(CreateContext(settings, CreateTeamSupportGameApi(self, leader), logger), new TeamSupportState(), combatState)
        .ConfigureAwait(false);

    AssertFalse(result.ShouldSkipNormalWork, "active combat should finish before team buff maintenance");
    AssertEqual(0, keyboard.Keys.Count, "active combat should not be interrupted by team buff target selection");
    AssertFalse(
        logger.Entries.Any(entry => entry.EventName == "team_support.action_pressed"),
        "deferred team buff should not log an action press");
}

static async Task TestTeamSupportPostponesAfterCombatBuffWhileLootActiveAsync()
{
    var keyboard = new RecordingKeyboardInput();
    var logger = new InMemoryRoadhogLogger();
    const uint lifeBlessingStatusId = 8105;
    var self = CreatePartyMemberSnapshot(1000, "Healer", true, false, 0.0) with
    {
        Class = AionClassId.Cleric,
        ClassId = (byte)AionClassId.Cleric,
        ClassName = "Cleric"
    };
    var leader = CreatePartyMemberSnapshot(2000, "Leader", false, true, 4.0) with
    {
        CurrentHp = 100,
        MaxHp = 100,
        AbnormalStatuses = Array.Empty<AbnormalStatusEntrySnapshot>()
    };
    var settings = CreateTeamSupportSettings();
    settings.Maintenance.StatusMaintenanceRules.Add(new StatusMaintenanceRuleConfig
    {
        Key = "NumPad4",
        SkillId = 9105,
        SkillName = "\u751F\u547D\u7684\u795D\u798F V",
        AbnormalStatusId = lifeBlessingStatusId,
        RunTiming = MaintenanceRuleRunTiming.AfterCombat
    });
    var combatState = new StationaryCombatState();
    combatState.StartLootAfterKill(
        new LockedTargetSnapshot(
            100,
            100,
            0,
            LockedTargetSnapshot.MonsterObjectType,
            "Monster",
            0,
            100,
            new Vector3Snapshot(1, 0, 0),
            null,
            DateTimeOffset.Now,
            5000,
            true,
            self.ServerObjectId),
        DateTimeOffset.Now);

    var controller = new TeamSupportController(keyboard, CreateTeamSupportAbnormalCatalog());
    var result = await controller
        .TickAsync(CreateContext(settings, CreateTeamSupportGameApi(self, leader), logger), new TeamSupportState(), combatState)
        .ConfigureAwait(false);

    AssertFalse(result.ShouldSkipNormalWork, "loot-active combat workflow should continue before team after-combat buff");
    AssertEqual(0, keyboard.Keys.Count, "team after-combat buff should not run before loot workflow finishes");
    AssertFalse(
        logger.Entries.Any(entry => entry.EventName == "team_support.action_pressed"),
        "postponed team buff should not log an action press");
}

static async Task TestTeamSupportSkipsNonWhitelistMaintenanceBuffAsync()
{
    var keyboard = new RecordingKeyboardInput();
    var logger = new InMemoryRoadhogLogger();
    var self = CreatePartyMemberSnapshot(1000, "Healer", true, false, 0.0) with
    {
        Class = AionClassId.Cleric,
        ClassId = (byte)AionClassId.Cleric,
        ClassName = "Cleric"
    };
    var leader = CreatePartyMemberSnapshot(2000, "Leader", false, true, 4.0) with
    {
        CurrentHp = 100,
        MaxHp = 100,
        AbnormalStatuses = Array.Empty<AbnormalStatusEntrySnapshot>()
    };
    var gameApi = CreateTeamSupportGameApi(self, leader);
    keyboard.AfterPress = key =>
    {
        if (string.Equals(key, "F2", StringComparison.Ordinal))
        {
            SetFakeLockedTarget(gameApi, leader.ServerObjectId, 0, 0, 0);
        }
    };

    var settings = CreateTeamSupportSettings();
    settings.Maintenance.StatusMaintenanceRules.Add(new StatusMaintenanceRuleConfig
    {
        Key = "NumPad5",
        SkillId = 9101,
        SkillName = "Status Buff",
        AbnormalStatusId = 8102,
        RunTiming = MaintenanceRuleRunTiming.Always
    });
    var controller = new TeamSupportController(keyboard, CreateTeamSupportAbnormalCatalog());

    await controller.TickAsync(CreateContext(settings, gameApi, logger), new TeamSupportState()).ConfigureAwait(false);

    AssertSequence(new[] { "F2", "C" }, keyboard.Keys.ToArray(), "non-whitelist maintenance status should be ignored by team buff extension");
    AssertFalse(keyboard.Keys.Contains("NumPad5"), "non-whitelist maintenance status must not be cast on team");
}

static async Task TestTeamSupportSkipsActiveWhitelistMaintenanceBuffAsync()
{
    var keyboard = new RecordingKeyboardInput();
    var logger = new InMemoryRoadhogLogger();
    const uint protectionStatusId = 8103;
    var self = CreatePartyMemberSnapshot(1000, "Healer", true, false, 0.0) with
    {
        Class = AionClassId.Cleric,
        ClassId = (byte)AionClassId.Cleric,
        ClassName = "Cleric",
        AbnormalStatuses = new[] { Abnormal(protectionStatusId, PlayerAbnormalStatusSnapshot.BuffCategory) }
    };
    var leader = CreatePartyMemberSnapshot(2000, "Leader", false, true, 4.0) with
    {
        CurrentHp = 100,
        MaxHp = 100,
        AbnormalStatuses = new[] { Abnormal(protectionStatusId, PlayerAbnormalStatusSnapshot.BuffCategory) }
    };
    var gameApi = CreateTeamSupportGameApi(self, leader);
    keyboard.AfterPress = key =>
    {
        if (string.Equals(key, "F2", StringComparison.Ordinal))
        {
            SetFakeLockedTarget(gameApi, leader.ServerObjectId, 0, 0, 0);
        }
    };

    var settings = CreateTeamSupportSettings();
    settings.Maintenance.StatusMaintenanceRules.Add(new StatusMaintenanceRuleConfig
    {
        Key = "NumPad6",
        SkillId = 9102,
        SkillName = "\u4FDD\u62A4\u795D\u798F III",
        AbnormalStatusId = protectionStatusId,
        RunTiming = MaintenanceRuleRunTiming.Always
    });
    var controller = new TeamSupportController(keyboard, CreateTeamSupportAbnormalCatalog());

    await controller.TickAsync(CreateContext(settings, gameApi, logger), new TeamSupportState()).ConfigureAwait(false);

    AssertSequence(new[] { "F2", "C" }, keyboard.Keys.ToArray(), "active whitelisted buff should not be re-cast");
    AssertFalse(keyboard.Keys.Contains("NumPad6"), "active whitelisted buff must not repeat");
}

static async Task TestTeamSupportThrottlesMissingWhitelistBuffRetryAsync()
{
    var keyboard = new RecordingKeyboardInput();
    var logger = new InMemoryRoadhogLogger();
    const uint lifeBlessingStatusId = 8104;
    var self = CreatePartyMemberSnapshot(1000, "Healer", true, false, 0.0) with
    {
        Class = AionClassId.Cleric,
        ClassId = (byte)AionClassId.Cleric,
        ClassName = "Cleric",
        AbnormalStatuses = new[] { Abnormal(lifeBlessingStatusId, PlayerAbnormalStatusSnapshot.BuffCategory) }
    };
    var leader = CreatePartyMemberSnapshot(2000, "Leader", false, true, 4.0) with
    {
        CurrentHp = 100,
        MaxHp = 100,
        AbnormalStatuses = Array.Empty<AbnormalStatusEntrySnapshot>()
    };
    var gameApi = CreateTeamSupportGameApi(self, leader);
    keyboard.AfterPress = key =>
    {
        if (string.Equals(key, "F2", StringComparison.Ordinal))
        {
            SetFakeLockedTarget(gameApi, leader.ServerObjectId, 0, 0, 0);
        }
    };

    var settings = CreateTeamSupportSettings();
    settings.Maintenance.StatusMaintenanceRules.Add(new StatusMaintenanceRuleConfig
    {
        Key = "NumPad4",
        SkillId = 9104,
        SkillName = "\u751F\u547D\u7684\u795D\u798F V",
        AbnormalStatusId = lifeBlessingStatusId,
        RunTiming = MaintenanceRuleRunTiming.Always
    });
    var controller = new TeamSupportController(keyboard, CreateTeamSupportAbnormalCatalog());
    var state = new TeamSupportState();
    var context = CreateContext(settings, gameApi, logger);

    await controller.TickAsync(context, state).ConfigureAwait(false);
    AssertSequence(new[] { "F2", "NumPad4" }, keyboard.Keys.ToArray(), "first missing whitelist buff should press configured key");

    keyboard.Keys.Clear();
    await controller.TickAsync(context, state).ConfigureAwait(false);

    AssertSequence(new[] { "C" }, keyboard.Keys.ToArray(), "recent missing whitelist buff retry should keep leader selected and press follow");
    AssertFalse(keyboard.Keys.Contains("NumPad4"), "recent whitelist buff retry must not repeat immediately");
}

static async Task TestTeamSupportKeepsAlreadySelectedLeaderAsync()
{
    var keyboard = new RecordingKeyboardInput();
    var logger = new InMemoryRoadhogLogger();
    var self = CreatePartyMemberSnapshot(1000, "Chanter", true, false, 0.0) with
    {
        Class = AionClassId.Chanter,
        ClassId = (byte)AionClassId.Chanter,
        ClassName = "Chanter"
    };
    var leader = CreatePartyMemberSnapshot(2000, "Guardian", false, true, 10.0);
    var gameApi = CreateTeamSupportGameApi(self, leader);
    SetFakeLockedTarget(gameApi, leader.ServerObjectId, 0, 0, 0);

    var settings = CreateTeamSupportSettings();
    settings.Team.GroupDistanceMeters = 20.0D;
    var controller = new TeamSupportController(keyboard, CreateTeamSupportAbnormalCatalog());

    var result = await controller
        .TickAsync(CreateContext(settings, gameApi, logger), new TeamSupportState())
        .ConfigureAwait(false);

    AssertFalse(!result.ShouldSkipNormalWork, "support should still own the idle follow tick");
    AssertSequence(new[] { "C" }, keyboard.Keys.ToArray(), "already selected leader should press follow without another F-key");
}

static async Task TestTeamSupportLeaderJumpRequiresConsecutiveAssistsAsync()
{
    var keyboard = new RecordingKeyboardInput();
    var logger = new InMemoryRoadhogLogger();
    var self = CreatePartyMemberSnapshot(1000, "Chanter", true, false, 0.0) with
    {
        Class = AionClassId.Chanter,
        ClassId = (byte)AionClassId.Chanter,
        ClassName = "Chanter"
    };
    var leader = CreatePartyMemberSnapshot(2000, "Guardian", false, true, 10.0);
    var gameApi = CreateTeamSupportGameApi(self, leader);
    SetFakeLockedTarget(gameApi, leader.ServerObjectId, LockedTargetSnapshot.PlayerObjectType, 0, 100);

    var controller = new TeamSupportController(keyboard, CreateTeamSupportAbnormalCatalog());
    var state = new TeamSupportState();
    var context = CreateContext(CreateTeamSupportSettings(), gameApi, logger);

    for (var i = 1; i <= 4; i++)
    {
        var result = await controller.TickAsync(context, state).ConfigureAwait(false);
        AssertFalse(!result.ShouldSkipNormalWork, "support should keep following leader before jump interval");
    }

    AssertEqual(4, keyboard.Keys.Count(key => string.Equals(key, "C", StringComparison.Ordinal)), "support leader assist count before reset");
    AssertEqual(0, keyboard.Keys.Count(key => string.Equals(key, "Space", StringComparison.Ordinal)), "support jump count before reset");
    AssertEqual(4, state.LeaderAssistPressCountSinceJump, "support state count before reset");

    var injuredLeader = leader with { CurrentHp = 50, MaxHp = 100 };
    gameApi.Party = CreateTeamSupportParty(self, injuredLeader);
    var heal = await controller.TickAsync(context, state).ConfigureAwait(false);

    AssertFalse(!heal.ShouldSkipNormalWork, "support heal should block normal work");
    AssertEqual(1, keyboard.Keys.Count(key => string.Equals(key, "NumPad1", StringComparison.Ordinal)), "support heal should press the heal key");
    AssertEqual(0, keyboard.Keys.Count(key => string.Equals(key, "Space", StringComparison.Ordinal)), "support jump should not fire before reset");
    AssertEqual(0, state.LeaderAssistPressCountSinceJump, "support heal should reset leader assist count");

    keyboard.Keys.Clear();
    gameApi.Party = CreateTeamSupportParty(self, leader);
    SetFakeLockedTarget(gameApi, leader.ServerObjectId, LockedTargetSnapshot.PlayerObjectType, 0, 100);

    for (var i = 1; i <= 4; i++)
    {
        var result = await controller.TickAsync(context, state).ConfigureAwait(false);
        AssertFalse(!result.ShouldSkipNormalWork, "support should restart consecutive leader assists after reset");
    }

    AssertEqual(4, keyboard.Keys.Count(key => string.Equals(key, "C", StringComparison.Ordinal)), "support leader assist count after reset");
    AssertEqual(0, keyboard.Keys.Count(key => string.Equals(key, "Space", StringComparison.Ordinal)), "support jump should wait for five fresh assists");

    var fifth = await controller.TickAsync(context, state).ConfigureAwait(false);

    AssertFalse(!fifth.ShouldSkipNormalWork, "support should keep following leader on jump interval");
    AssertEqual(5, keyboard.Keys.Count(key => string.Equals(key, "C", StringComparison.Ordinal)), "support leader assist count at jump");
    AssertEqual(1, keyboard.Keys.Count(key => string.Equals(key, "Space", StringComparison.Ordinal)), "support jump count at interval");
    AssertEqual("C", keyboard.Keys[4], "fifth support leader assist should press C first");
    AssertEqual("Space", keyboard.Keys[5], "fifth support leader assist should press Space after C");
    AssertEqual(0, state.LeaderAssistPressCountSinceJump, "support count should reset after jump");
    AssertFalse(
        !logger.Entries.Any(entry => entry.EventName == "team_support.leader_jump.pressed"),
        "support leader jump should be logged");
}

static async Task TestTeamSupportJoinCombatContinuesWhileLeaderOutsideGroupRangeAsync()
{
    var keyboard = new RecordingKeyboardInput();
    var logger = new InMemoryRoadhogLogger();
    var self = CreatePartyMemberSnapshot(1000, "Chanter", true, false, 0.0) with
    {
        Class = AionClassId.Chanter,
        ClassId = (byte)AionClassId.Chanter,
        ClassName = "Chanter"
    };
    var leader = CreatePartyMemberSnapshot(2000, "Guardian", false, true, 30.0);
    var settings = CreateTeamSupportSettings();
    settings.Team.GroupDistanceMeters = 20.0D;
    settings.Team.Support!.JoinCombat = true;
    settings.Team.Support.LeaderDistanceMeters = 60.0D;

    var controller = new TeamSupportController(keyboard, CreateTeamSupportAbnormalCatalog());
    var result = await controller
        .TickAsync(CreateContext(settings, CreateTeamSupportGameApi(self, leader), logger), new TeamSupportState())
        .ConfigureAwait(false);

    AssertFalse(result.ShouldSkipNormalWork, "leader outside group range should allow support to keep normal combat");
    AssertEqual(0, keyboard.Keys.Count, "leader outside group range should not press follow keys");
}

static async Task TestTeamSupportStaysGroupedUntilLeaderExitDistanceAsync()
{
    var keyboard = new RecordingKeyboardInput();
    var logger = new InMemoryRoadhogLogger();
    var self = CreatePartyMemberSnapshot(1000, "Chanter", true, false, 0.0) with
    {
        Class = AionClassId.Chanter,
        ClassId = (byte)AionClassId.Chanter,
        ClassName = "Chanter"
    };
    var leader = CreatePartyMemberSnapshot(2000, "Guardian", false, true, 10.0);
    var gameApi = CreateTeamSupportGameApi(self, leader);
    SetFakeLockedTarget(gameApi, leader.ServerObjectId, 0, 0, 0);

    var settings = CreateTeamSupportSettings();
    settings.Team.GroupDistanceMeters = 20.0D;
    settings.Team.Support!.JoinCombat = false;
    var controller = new TeamSupportController(keyboard, CreateTeamSupportAbnormalCatalog());
    var state = new TeamSupportState();
    var context = CreateContext(settings, gameApi, logger);

    var first = await controller.TickAsync(context, state).ConfigureAwait(false);
    AssertFalse(!first.ShouldSkipNormalWork, "leader inside enter distance should start group follow");
    AssertSequence(new[] { "C" }, keyboard.Keys.ToArray(), "already selected leader should only press follow inside enter distance");

    keyboard.Keys.Clear();
    leader = leader with { DistanceToLocalPlayer = 25.0D };
    gameApi.Party = CreateTeamSupportParty(self, leader);
    var second = await controller.TickAsync(context, state).ConfigureAwait(false);

    AssertFalse(!second.ShouldSkipNormalWork, "active group should stay grouped past enter distance");
    AssertSequence(new[] { "C" }, keyboard.Keys.ToArray(), "active group should still follow before exit distance");

    keyboard.Keys.Clear();
    leader = leader with { DistanceToLocalPlayer = 60.0D };
    gameApi.Party = CreateTeamSupportParty(self, leader);
    var third = await controller.TickAsync(context, state).ConfigureAwait(false);

    AssertFalse(third.ShouldSkipNormalWork, "leader beyond exit distance should release support back to ordinary work");
    AssertEqual(0, keyboard.Keys.Count, "leader beyond exit distance should not press follow keys");
}

static async Task TestTeamSupportWaitsForFiveConsecutiveLeaderUnavailableTicksAsync()
{
    var keyboard = new RecordingKeyboardInput();
    var logger = new InMemoryRoadhogLogger();
    var self = CreatePartyMemberSnapshot(1000, "Chanter", true, false, 0.0);
    var leader = CreatePartyMemberSnapshot(2000, "Guardian", false, true, 25.0);
    var gameApi = CreateTeamSupportGameApi(self);
    var settings = CreateTeamSupportSettings();
    settings.Team.GroupDistanceMeters = 20.0D;
    settings.Team.Support!.JoinCombat = false;
    var controller = new TeamSupportController(keyboard, CreateTeamSupportAbnormalCatalog());
    var state = new TeamSupportState { LeaderGroupActive = true };
    var context = CreateContext(settings, gameApi, logger);

    gameApi.Party = CreateTeamPartyWithoutLeader(self);
    for (var i = 1; i <= 4; i++)
    {
        var missing = await controller.TickAsync(context, state).ConfigureAwait(false);
        AssertFalse(!missing.ShouldSkipNormalWork, "support should block normal work before five consecutive leader misses");
    }

    AssertEqual(4, state.ConsecutiveLeaderUnavailableTicks, "support leader unavailable tick count before recovery");
    AssertFalse(!state.LeaderGroupActive, "support should keep active group state during transient leader misses");

    gameApi.Party = CreateTeamSupportParty(self, leader);
    SetFakeLockedTarget(gameApi, leader.ServerObjectId, 0, 0, 0);
    var recovered = await controller.TickAsync(context, state).ConfigureAwait(false);

    AssertFalse(!recovered.ShouldSkipNormalWork, "support should recover leader follow when the next valid tick arrives");
    AssertEqual(0, state.ConsecutiveLeaderUnavailableTicks, "support should reset missing count after a valid leader tick");
    AssertFalse(!state.LeaderGroupActive, "support should use exit distance after transient leader misses");
    AssertSequence(new[] { "C" }, keyboard.Keys.ToArray(), "support should resume leader follow without pressing another F-key");

    keyboard.Keys.Clear();
    gameApi.Party = CreateTeamPartyWithoutLeader(self);
    for (var i = 1; i <= 4; i++)
    {
        var missing = await controller.TickAsync(context, state).ConfigureAwait(false);
        AssertFalse(!missing.ShouldSkipNormalWork, "support should keep blocking normal work for the first four misses");
    }

    var fifth = await controller.TickAsync(context, state).ConfigureAwait(false);

    AssertFalse(fifth.ShouldSkipNormalWork, "support should release normal work on the fifth consecutive leader miss");
    AssertEqual(5, state.ConsecutiveLeaderUnavailableTicks, "support missing count at fallback threshold");
    AssertFalse(state.LeaderGroupActive, "support should clear active group state when leader has been missing for five ticks");
    AssertEqual(0, keyboard.Keys.Count, "support should not press follow keys while the leader is unavailable");
}

static async Task TestTeamSupportJoinCombatDefersFollowWhileFightingAsync()
{
    var keyboard = new RecordingKeyboardInput();
    var logger = new InMemoryRoadhogLogger();
    var self = CreatePartyMemberSnapshot(1000, "Chanter", true, false, 0.0) with
    {
        Class = AionClassId.Chanter,
        ClassId = (byte)AionClassId.Chanter,
        ClassName = "Chanter"
    };
    var leader = CreatePartyMemberSnapshot(2000, "Guardian", false, true, 10.0);
    var settings = CreateTeamSupportSettings();
    settings.Team.GroupDistanceMeters = 20.0D;
    settings.Team.Support!.JoinCombat = true;
    settings.Team.Support.LeaderDistanceMeters = 5.0D;
    var combatState = new StationaryCombatState
    {
        Fighting = true,
        CandidateEntityId = 100
    };

    var controller = new TeamSupportController(keyboard, CreateTeamSupportAbnormalCatalog());
    var result = await controller
        .TickAsync(CreateContext(settings, CreateTeamSupportGameApi(self, leader), logger), new TeamSupportState(), combatState)
        .ConfigureAwait(false);

    AssertFalse(result.ShouldSkipNormalWork, "active combat should be allowed to finish before leader follow");
    AssertEqual(0, keyboard.Keys.Count, "active combat should not be interrupted by follow keys");
}

static async Task TestTeamSupportJoinCombatSelectsLeaderTargetInsideGroupRangeAsync()
{
    var keyboard = new RecordingKeyboardInput();
    var logger = new InMemoryRoadhogLogger();
    const uint targetServerObjectId = 5000;
    var self = CreatePartyMemberSnapshot(1000, "Chanter", true, false, 0.0) with
    {
        Class = AionClassId.Chanter,
        ClassId = (byte)AionClassId.Chanter,
        ClassName = "Chanter"
    };
    var leader = CreatePartyMemberSnapshot(2000, "Guardian", false, true, 10.0) with
    {
        LiveTargetServerObjectId = targetServerObjectId
    };
    var gameApi = CreateTeamSupportGameApi(self, leader);
    keyboard.AfterPress = key =>
    {
        if (string.Equals(key, "F2", StringComparison.Ordinal))
        {
            SetFakeLockedTarget(gameApi, leader.ServerObjectId, 0, 0, 0);
        }
        else if (string.Equals(key, "Oem3", StringComparison.Ordinal))
        {
            SetFakeLockedTarget(
                gameApi,
                targetServerObjectId,
                LockedTargetSnapshot.MonsterObjectType,
                leader.ServerObjectId,
                100);
        }
    };

    var settings = CreateTeamSupportSettings();
    settings.Team.GroupDistanceMeters = 20.0D;
    settings.Team.Support!.JoinCombat = true;
    settings.Team.Support.LeaderDistanceMeters = 5.0D;
    var combatState = new StationaryCombatState();
    var controller = new TeamSupportController(keyboard, CreateTeamSupportAbnormalCatalog());
    var result = await controller
        .TickAsync(CreateContext(settings, gameApi, logger), new TeamSupportState(), combatState)
        .ConfigureAwait(false);

    AssertFalse(result.ShouldSkipNormalWork, "accepted leader target should allow support to enter normal combat");
    AssertSequence(new[] { "F2", "C", "Oem3" }, keyboard.Keys.ToArray(), "support join combat should follow leader then assist target");
    AssertLeaderTargetAdopted(combatState, targetServerObjectId, "support join combat should hand target to normal combat");
    AssertFalse(
        !(gameApi.LastLockedTargetContext?.BypassMemoryCache ?? false),
        "support assist target verification should bypass VMM cache");
}

static async Task TestTeamSupportSelfDefenseAcceptsLeaderTargetAttackingLocalPlayerAsync()
{
    var keyboard = new RecordingKeyboardInput();
    var logger = new InMemoryRoadhogLogger();
    const uint targetServerObjectId = 5000;
    var self = CreatePartyMemberSnapshot(1000, "Chanter", true, false, 0.0) with
    {
        Class = AionClassId.Chanter,
        ClassId = (byte)AionClassId.Chanter,
        ClassName = "Chanter"
    };
    var leader = CreatePartyMemberSnapshot(2000, "Guardian", false, true, 10.0) with
    {
        LiveTargetServerObjectId = targetServerObjectId
    };
    var gameApi = CreateTeamSupportGameApi(self, leader);
    keyboard.AfterPress = key =>
    {
        if (string.Equals(key, "F2", StringComparison.Ordinal))
        {
            SetFakeLockedTarget(gameApi, leader.ServerObjectId, 0, 0, 0);
        }
        else if (string.Equals(key, "Oem3", StringComparison.Ordinal))
        {
            SetFakeLockedTarget(
                gameApi,
                targetServerObjectId,
                LockedTargetSnapshot.MonsterObjectType,
                self.ServerObjectId,
                100);
        }
    };

    var settings = CreateTeamSupportSettings();
    settings.Team.GroupDistanceMeters = 20.0D;
    settings.Team.Support!.JoinCombat = true;
    settings.Team.Support.AllowSelfDefense = true;
    var combatState = new StationaryCombatState();
    var controller = new TeamSupportController(keyboard, CreateTeamSupportAbnormalCatalog());
    var result = await controller
        .TickAsync(CreateContext(settings, gameApi, logger), new TeamSupportState(), combatState)
        .ConfigureAwait(false);

    AssertFalse(result.ShouldSkipNormalWork, "support self defense should allow normal combat work");
    AssertSequence(new[] { "F2", "C", "Oem3" }, keyboard.Keys.ToArray(), "support should assist leader target when it is attacking self");
    AssertLeaderTargetAdopted(combatState, targetServerObjectId, "support should adopt leader target attacking local player");
}

static async Task TestTeamSupportSelfDefenseDisabledRejectsLocalTargetAsync()
{
    var keyboard = new RecordingKeyboardInput();
    var logger = new InMemoryRoadhogLogger();
    const uint targetServerObjectId = 5000;
    var self = CreatePartyMemberSnapshot(1000, "Chanter", true, false, 0.0) with
    {
        Class = AionClassId.Chanter,
        ClassId = (byte)AionClassId.Chanter,
        ClassName = "Chanter"
    };
    var leader = CreatePartyMemberSnapshot(2000, "Guardian", false, true, 10.0) with
    {
        LiveTargetServerObjectId = targetServerObjectId
    };
    var gameApi = CreateTeamSupportGameApi(self, leader);
    keyboard.AfterPress = key =>
    {
        if (string.Equals(key, "F2", StringComparison.Ordinal))
        {
            SetFakeLockedTarget(gameApi, leader.ServerObjectId, 0, 0, 0);
        }
        else if (string.Equals(key, "Oem3", StringComparison.Ordinal))
        {
            SetFakeLockedTarget(
                gameApi,
                targetServerObjectId,
                LockedTargetSnapshot.MonsterObjectType,
                self.ServerObjectId,
                100);
        }
    };

    var settings = CreateTeamSupportSettings();
    settings.Team.GroupDistanceMeters = 20.0D;
    settings.Team.Support!.JoinCombat = true;
    settings.Team.Support.AllowSelfDefense = false;
    var combatState = new StationaryCombatState();
    var controller = new TeamSupportController(keyboard, CreateTeamSupportAbnormalCatalog());
    var result = await controller
        .TickAsync(CreateContext(settings, gameApi, logger), new TeamSupportState(), combatState)
        .ConfigureAwait(false);

    AssertFalse(!result.ShouldSkipNormalWork, "support should keep owning the tick after rejected assist target");
    AssertSequence(new[] { "F2", "C", "Oem3", "F2", "C" }, keyboard.Keys.ToArray(), "disabled self defense should return to leader follow");
    AssertFalse(combatState.Fighting, "disabled self defense should not adopt target attacking local player");
}

static async Task TestTeamSupportSelfDefenseScansLocalAttackerBeforeMaintenanceAsync()
{
    var keyboard = new RecordingKeyboardInput();
    var logger = new InMemoryRoadhogLogger();
    const uint attackerServerObjectId = 9000;
    var self = CreatePartyMemberSnapshot(1000, "Chanter", true, false, 0.0) with
    {
        Class = AionClassId.Chanter,
        ClassId = (byte)AionClassId.Chanter,
        ClassName = "Chanter",
        CurrentHp = 40,
        MaxHp = 100
    };
    var leader = CreatePartyMemberSnapshot(2000, "Guardian", false, true, 60.0);
    var gameApi = CreateTeamSupportGameApi(self, leader);
    gameApi.WorldObjects = new[]
    {
        CreateMonsterWorldObject(100, attackerServerObjectId, self.ServerObjectId, new Vector3Snapshot(2, 0, 0))
    };

    var settings = CreateTeamSupportSettings();
    settings.Team.Support!.AllowSelfDefense = true;
    settings.Team.Support.JoinCombat = false;
    var combatState = new StationaryCombatState();
    var controller = new TeamSupportController(keyboard, CreateTeamSupportAbnormalCatalog());
    var result = await controller
        .TickAsync(CreateContext(settings, gameApi, logger), new TeamSupportState(), combatState)
        .ConfigureAwait(false);

    AssertFalse(result.ShouldSkipNormalWork, "support self defense should hand the tick back to normal combat");
    AssertEqual(0, keyboard.Keys.Count, "self defense should not press heal or follow keys before combat");
    AssertLeaderTargetAdopted(combatState, attackerServerObjectId, "support self defense should adopt local attacker");
    AssertFalse(
        !logger.Entries.Any(entry => entry.EventName == "team_support.self_defense.threat_accepted"),
        "support self defense should log accepted local attacker");
}

static async Task TestTeamSupportSelfDefenseDisabledKeepsMaintenanceAsync()
{
    var keyboard = new RecordingKeyboardInput();
    var logger = new InMemoryRoadhogLogger();
    var self = CreatePartyMemberSnapshot(1000, "Chanter", true, false, 0.0) with
    {
        Class = AionClassId.Chanter,
        ClassId = (byte)AionClassId.Chanter,
        ClassName = "Chanter",
        CurrentHp = 40,
        MaxHp = 100
    };
    var leader = CreatePartyMemberSnapshot(2000, "Guardian", false, true, 4.0);
    var gameApi = CreateTeamSupportGameApi(self, leader);
    gameApi.WorldObjects = new[]
    {
        CreateMonsterWorldObject(100, 9000, self.ServerObjectId, new Vector3Snapshot(2, 0, 0))
    };
    keyboard.AfterPress = key =>
    {
        if (string.Equals(key, "F1", StringComparison.Ordinal))
        {
            SetFakeLockedTarget(gameApi, self.ServerObjectId, LockedTargetSnapshot.PlayerObjectType, 0, 100);
        }
    };

    var settings = CreateTeamSupportSettings();
    settings.Team.Support!.AllowSelfDefense = false;
    settings.Team.Support.JoinCombat = false;
    var combatState = new StationaryCombatState();
    var controller = new TeamSupportController(keyboard, CreateTeamSupportAbnormalCatalog());
    var result = await controller
        .TickAsync(CreateContext(settings, gameApi, logger), new TeamSupportState(), combatState)
        .ConfigureAwait(false);

    AssertFalse(!result.ShouldSkipNormalWork, "disabled self defense should keep normal support maintenance");
    AssertSequence(new[] { "F1", "NumPad1" }, keyboard.Keys.ToArray(), "disabled self defense should still heal");
    AssertFalse(combatState.Fighting, "disabled self defense should not adopt local attacker");
}

static async Task TestTeamSupportJoinCombatWaitsAfterAssistTargetKeyAsync()
{
    var keyboard = new RecordingKeyboardInput();
    var logger = new InMemoryRoadhogLogger();
    const uint targetServerObjectId = 5000;
    var self = CreatePartyMemberSnapshot(1000, "Chanter", true, false, 0.0) with
    {
        Class = AionClassId.Chanter,
        ClassId = (byte)AionClassId.Chanter,
        ClassName = "Chanter"
    };
    var leader = CreatePartyMemberSnapshot(2000, "Guardian", false, true, 10.0) with
    {
        LiveTargetServerObjectId = targetServerObjectId
    };
    var gameApi = CreateTeamSupportGameApi(self, leader);
    keyboard.AfterPress = key =>
    {
        if (string.Equals(key, "F2", StringComparison.Ordinal))
        {
            SetFakeLockedTarget(gameApi, leader.ServerObjectId, 0, 0, 0);
        }
        else if (string.Equals(key, "Oem3", StringComparison.Ordinal))
        {
            gameApi.LockedTargetReadResults.Enqueue(CreateFakeLockedTargetResult(
                leader.ServerObjectId,
                LockedTargetSnapshot.PlayerObjectType,
                targetServerObjectId,
                0));
            gameApi.LockedTargetReadResults.Enqueue(CreateFakeLockedTargetResult(
                targetServerObjectId,
                LockedTargetSnapshot.MonsterObjectType,
                leader.ServerObjectId,
                100));
        }
    };

    var settings = CreateTeamSupportSettings();
    settings.Team.GroupDistanceMeters = 20.0D;
    settings.Team.Support!.JoinCombat = true;
    settings.Team.Support.LeaderDistanceMeters = 5.0D;
    var combatState = new StationaryCombatState();
    var controller = new TeamSupportController(keyboard, CreateTeamSupportAbnormalCatalog());
    var result = await controller
        .TickAsync(CreateContext(settings, gameApi, logger), new TeamSupportState(), combatState)
        .ConfigureAwait(false);

    AssertFalse(result.ShouldSkipNormalWork, "delayed assist target confirmation should still allow combat");
    AssertEqual(1, keyboard.Keys.Count(key => string.Equals(key, "Oem3", StringComparison.Ordinal)), "assist target key should be pressed once");
    AssertLeaderTargetAdopted(combatState, targetServerObjectId, "support should adopt target after confirm poll");
    AssertFalse(
        !(gameApi.LastLockedTargetContext?.BypassMemoryCache ?? false),
        "support delayed assist confirmation should bypass VMM cache");
}

static async Task TestTeamSupportJoinCombatAcceptsAlreadyLockedLeaderTargetAsync()
{
    var keyboard = new RecordingKeyboardInput();
    var logger = new InMemoryRoadhogLogger();
    const uint targetServerObjectId = 5000;
    var self = CreatePartyMemberSnapshot(1000, "Chanter", true, false, 0.0) with
    {
        Class = AionClassId.Chanter,
        ClassId = (byte)AionClassId.Chanter,
        ClassName = "Chanter"
    };
    var leader = CreatePartyMemberSnapshot(2000, "Guardian", false, true, 10.0) with
    {
        LiveTargetServerObjectId = targetServerObjectId
    };
    var gameApi = CreateTeamSupportGameApi(self, leader);
    SetFakeLockedTarget(
        gameApi,
        targetServerObjectId,
        LockedTargetSnapshot.MonsterObjectType,
        leader.ServerObjectId,
        100);

    var settings = CreateTeamSupportSettings();
    settings.Team.GroupDistanceMeters = 20.0D;
    settings.Team.Support!.JoinCombat = true;
    settings.Team.Support.LeaderDistanceMeters = 5.0D;
    var combatState = new StationaryCombatState();
    var controller = new TeamSupportController(keyboard, CreateTeamSupportAbnormalCatalog());
    var result = await controller
        .TickAsync(CreateContext(settings, gameApi, logger), new TeamSupportState(), combatState)
        .ConfigureAwait(false);

    AssertFalse(result.ShouldSkipNormalWork, "already locked leader target should enter normal combat");
    AssertEqual(0, keyboard.Keys.Count, "already locked leader target should not switch back to leader or press assist key");
    AssertLeaderTargetAdopted(combatState, targetServerObjectId, "support should adopt already locked leader target");
    AssertFalse(
        !(gameApi.LastLockedTargetContext?.BypassMemoryCache ?? false),
        "support already-locked target check should bypass VMM cache");
}

static async Task TestTeamSupportAcceptsLeaderPetTargetWhenClassUnknownAsync()
{
    var keyboard = new RecordingKeyboardInput();
    var logger = new InMemoryRoadhogLogger();
    const uint petServerObjectId = 3000;
    const uint targetServerObjectId = 5000;
    var self = CreatePartyMemberSnapshot(1000, "Chanter", true, false, 0.0) with
    {
        Class = AionClassId.Chanter,
        ClassId = (byte)AionClassId.Chanter,
        ClassName = "Chanter"
    };
    var leader = CreatePartyMemberSnapshot(2000, "Leader", false, true, 10.0) with
    {
        Class = null,
        ClassId = 0,
        ClassName = string.Empty,
        LiveTargetServerObjectId = targetServerObjectId
    };
    var gameApi = CreateTeamSupportGameApi(self, leader);
    gameApi.SummonedPetRoster = CreateTeamSupportRoster(self.ServerObjectId, leader, petServerObjectId);
    keyboard.AfterPress = key =>
    {
        if (string.Equals(key, "F2", StringComparison.Ordinal))
        {
            SetFakeLockedTarget(gameApi, leader.ServerObjectId, LockedTargetSnapshot.PlayerObjectType, 0, 100);
        }
        else if (string.Equals(key, "Oem3", StringComparison.Ordinal))
        {
            SetFakeLockedTarget(
                gameApi,
                targetServerObjectId,
                LockedTargetSnapshot.MonsterObjectType,
                petServerObjectId,
                100);
        }
    };

    var settings = CreateTeamSupportSettings();
    settings.Team.GroupDistanceMeters = 20.0D;
    settings.Team.Support!.JoinCombat = true;
    settings.Team.Support.LeaderDistanceMeters = 5.0D;
    var combatState = new StationaryCombatState();
    var controller = new TeamSupportController(keyboard, CreateTeamSupportAbnormalCatalog());
    var result = await controller
        .TickAsync(CreateContext(settings, gameApi, logger), new TeamSupportState(), combatState)
        .ConfigureAwait(false);

    AssertFalse(result.ShouldSkipNormalWork, "leader pet target should allow support combat even when leader class is unknown");
    AssertSequence(new[] { "F2", "C", "Oem3" }, keyboard.Keys.ToArray(), "support should assist leader target");
    AssertLeaderTargetAdopted(combatState, targetServerObjectId, "support should adopt target that is attacking leader pet");
}

static async Task TestTeamSupportJoinCombatSkipsPartyMemberLeaderTargetAsync()
{
    var keyboard = new RecordingKeyboardInput();
    var logger = new InMemoryRoadhogLogger();
    var self = CreatePartyMemberSnapshot(1000, "Chanter", true, false, 0.0) with
    {
        Class = AionClassId.Chanter,
        ClassId = (byte)AionClassId.Chanter,
        ClassName = "Chanter"
    };
    var leader = CreatePartyMemberSnapshot(2000, "Guardian", false, true, 10.0) with
    {
        LiveTargetServerObjectId = self.ServerObjectId
    };
    var gameApi = CreateTeamSupportGameApi(self, leader);
    keyboard.AfterPress = key =>
    {
        if (string.Equals(key, "F2", StringComparison.Ordinal))
        {
            SetFakeLockedTarget(gameApi, leader.ServerObjectId, 0, 0, 0);
        }
    };

    var settings = CreateTeamSupportSettings();
    settings.Team.GroupDistanceMeters = 20.0D;
    settings.Team.Support!.JoinCombat = true;
    settings.Team.Support.LeaderDistanceMeters = 5.0D;
    var combatState = new StationaryCombatState();
    var controller = new TeamSupportController(keyboard, CreateTeamSupportAbnormalCatalog());
    var result = await controller
        .TickAsync(CreateContext(settings, gameApi, logger), new TeamSupportState(), combatState)
        .ConfigureAwait(false);

    AssertFalse(!result.ShouldSkipNormalWork, "party-member leader target should keep support in follow tick");
    AssertSequence(new[] { "F2", "C" }, keyboard.Keys.ToArray(), "support should follow leader without assisting a party-member target");
    AssertFalse(combatState.Fighting, "party-member leader target should not enter combat");
    AssertFalse(
        !logger.Entries.Any(entry =>
            entry.EventName == "team_support.leader_target.skipped" &&
            string.Equals(entry.Fields["reason"]?.ToString(), "known_team_side_target", StringComparison.Ordinal)),
        "support should log known team-side target skip");
}

static async Task TestTeamSupportSitsWhenLeaderRestsAsync()
{
    var keyboard = new RecordingKeyboardInput();
    var logger = new InMemoryRoadhogLogger();
    var self = WithLiveRestState(CreatePartyMemberSnapshot(1000, "Healer", true, false, 0.0), resting: false);
    var leader = WithLiveRestState(CreatePartyMemberSnapshot(2000, "Leader", false, true, 4.0), resting: true);
    var gameApi = CreateTeamSupportGameApi(self, leader);
    var combatState = new StationaryCombatState
    {
        Fighting = true,
        CurrentTargetEntityId = 100,
        CurrentTargetServerObjectId = 5000,
        IsMovingForward = true,
        IsRightMouseDown = true
    };

    var controller = new TeamSupportController(keyboard, CreateTeamSupportAbnormalCatalog());
    var result = await controller
        .TickAsync(CreateContext(CreateTeamSupportSettings(), gameApi, logger), new TeamSupportState(), combatState)
        .ConfigureAwait(false);

    AssertFalse(!result.ShouldSkipNormalWork, "support should block normal work while syncing leader rest");
    AssertSequence(new[] { "OemComma" }, keyboard.Keys.ToArray(), "support should sit when leader is resting");
    AssertSequence(new[] { "W" }, keyboard.KeyUps.ToArray(), "support should release forward movement before sitting");
    AssertSequence(new[] { "up:Right" }, keyboard.MouseCommands.ToArray(), "support should release right mouse before sitting");
    AssertFalse(combatState.IsMovingForward, "support rest sync should clear movement state");
    AssertFalse(combatState.IsRightMouseDown, "support rest sync should clear right mouse state");
    AssertFalse(combatState.Fighting, "support rest sync should clear stale combat target");
}

static async Task TestTeamSupportHoldsWhileLeaderRestsAsync()
{
    var keyboard = new RecordingKeyboardInput();
    var logger = new InMemoryRoadhogLogger();
    var self = WithLiveRestState(CreatePartyMemberSnapshot(1000, "Healer", true, false, 0.0), resting: true);
    var leader = WithLiveRestState(CreatePartyMemberSnapshot(2000, "Leader", false, true, 4.0), resting: true);
    var gameApi = CreateTeamSupportGameApi(self, leader);
    var combatState = new StationaryCombatState
    {
        Fighting = true,
        CurrentTargetEntityId = 100,
        CurrentTargetServerObjectId = 5000,
        IsMovingForward = true
    };

    var controller = new TeamSupportController(keyboard, CreateTeamSupportAbnormalCatalog());
    var result = await controller
        .TickAsync(CreateContext(CreateTeamSupportSettings(), gameApi, logger), new TeamSupportState(), combatState)
        .ConfigureAwait(false);

    AssertFalse(!result.ShouldSkipNormalWork, "support should keep blocking normal work while leader remains resting");
    AssertEqual(0, keyboard.Keys.Count, "support should not press another rest key once already resting with leader");
    AssertSequence(new[] { "W" }, keyboard.KeyUps.ToArray(), "support should release stale movement while holding rest");
    AssertFalse(combatState.Fighting, "support hold rest should clear stale combat target");
}

static async Task TestTeamSupportStandsWhenLeaderStandsAsync()
{
    var keyboard = new RecordingKeyboardInput();
    var logger = new InMemoryRoadhogLogger();
    var self = WithLiveRestState(CreatePartyMemberSnapshot(1000, "Healer", true, false, 0.0), resting: true);
    var leader = WithLiveRestState(CreatePartyMemberSnapshot(2000, "Leader", false, true, 4.0), resting: false);
    var gameApi = CreateTeamSupportGameApi(self, leader);

    var controller = new TeamSupportController(keyboard, CreateTeamSupportAbnormalCatalog());
    var result = await controller
        .TickAsync(CreateContext(CreateTeamSupportSettings(), gameApi, logger), new TeamSupportState())
        .ConfigureAwait(false);

    AssertFalse(!result.ShouldSkipNormalWork, "support should block normal work while standing with leader");
    AssertSequence(new[] { "X" }, keyboard.Keys.ToArray(), "support should stand when leader is standing");
    AssertEqual(0, keyboard.KeyUps.Count, "standing sync should not release movement keys");
}

static async Task TestTeamOutputSitsWhenLeaderRestsAsync()
{
    var keyboard = new RecordingKeyboardInput();
    var logger = new InMemoryRoadhogLogger();
    var self = WithLiveRestState(CreatePartyMemberSnapshot(1000, "Dps", true, false, 0.0), resting: false);
    var leader = WithLiveRestState(CreatePartyMemberSnapshot(2000, "Leader", false, true, 4.0), resting: true) with
    {
        LiveTargetServerObjectId = 5000
    };
    var gameApi = CreateTeamSupportGameApi(self, leader);
    var combatState = new StationaryCombatState
    {
        Fighting = true,
        CurrentTargetEntityId = 100,
        CurrentTargetServerObjectId = 5000,
        IsMovingForward = true
    };

    var controller = new TeamOutputController(keyboard);
    var result = await controller
        .TickAsync(CreateContext(CreateTeamOutputSettings(), gameApi, logger), new TeamOutputState(), combatState)
        .ConfigureAwait(false);

    AssertFalse(!result.ShouldSkipNormalWork, "output should block normal work while syncing leader rest");
    AssertSequence(new[] { "OemComma" }, keyboard.Keys.ToArray(), "output should sit when leader is resting");
    AssertSequence(new[] { "W" }, keyboard.KeyUps.ToArray(), "output should release forward movement before sitting");
    AssertFalse(combatState.IsMovingForward, "output rest sync should clear movement state");
    AssertFalse(combatState.Fighting, "output rest sync should clear stale combat target");
}

static async Task TestTeamOutputHoldsWhileLeaderRestsAsync()
{
    var keyboard = new RecordingKeyboardInput();
    var logger = new InMemoryRoadhogLogger();
    var self = WithLiveRestState(CreatePartyMemberSnapshot(1000, "Dps", true, false, 0.0), resting: true);
    var leader = WithLiveRestState(CreatePartyMemberSnapshot(2000, "Leader", false, true, 4.0), resting: true) with
    {
        LiveTargetServerObjectId = 5000
    };
    var gameApi = CreateTeamSupportGameApi(self, leader);
    var combatState = new StationaryCombatState
    {
        Fighting = true,
        CurrentTargetEntityId = 100,
        CurrentTargetServerObjectId = 5000,
        IsMovingForward = true
    };

    var controller = new TeamOutputController(keyboard);
    var result = await controller
        .TickAsync(CreateContext(CreateTeamOutputSettings(), gameApi, logger), new TeamOutputState(), combatState)
        .ConfigureAwait(false);

    AssertFalse(!result.ShouldSkipNormalWork, "output should keep blocking normal work while leader remains resting");
    AssertEqual(0, keyboard.Keys.Count, "output should not press another rest key once already resting with leader");
    AssertSequence(new[] { "W" }, keyboard.KeyUps.ToArray(), "output should release stale movement while holding rest");
    AssertFalse(combatState.Fighting, "output hold rest should clear stale combat target");
}

static async Task TestTeamOutputStandsWhenLeaderStandsAsync()
{
    var keyboard = new RecordingKeyboardInput();
    var logger = new InMemoryRoadhogLogger();
    var self = WithLiveRestState(CreatePartyMemberSnapshot(1000, "Dps", true, false, 0.0), resting: true);
    var leader = WithLiveRestState(CreatePartyMemberSnapshot(2000, "Leader", false, true, 4.0), resting: false) with
    {
        LiveTargetServerObjectId = 5000
    };
    var gameApi = CreateTeamSupportGameApi(self, leader);

    var controller = new TeamOutputController(keyboard);
    var result = await controller
        .TickAsync(CreateContext(CreateTeamOutputSettings(), gameApi, logger), new TeamOutputState())
        .ConfigureAwait(false);

    AssertFalse(!result.ShouldSkipNormalWork, "output should block normal work while standing with leader");
    AssertSequence(new[] { "X" }, keyboard.Keys.ToArray(), "output should stand when leader is standing");
    AssertEqual(0, keyboard.KeyUps.Count, "standing sync should not release movement keys");
}

static async Task TestTeamOutputAssistsOnlyLeaderAttackedMonsterAsync()
{
    var keyboard = new RecordingKeyboardInput();
    var logger = new InMemoryRoadhogLogger();
    const uint targetServerObjectId = 5000;
    var self = CreatePartyMemberSnapshot(1000, "Dps", true, false, 0.0);
    var leader = CreatePartyMemberSnapshot(2000, "Leader", false, true, 4.0) with
    {
        LiveTargetServerObjectId = targetServerObjectId
    };
    var gameApi = CreateTeamSupportGameApi(self, leader);
    keyboard.AfterPress = key =>
    {
        if (string.Equals(key, "F2", StringComparison.Ordinal))
        {
            SetFakeLockedTarget(gameApi, leader.ServerObjectId, 0, 0, 0);
        }
        else if (string.Equals(key, TeamOutputScriptSettings.DefaultAssistTargetKey, StringComparison.Ordinal))
        {
            SetFakeLockedTarget(
                gameApi,
                targetServerObjectId,
                LockedTargetSnapshot.MonsterObjectType,
                leader.ServerObjectId,
                100);
        }
    };

    var combatState = new StationaryCombatState();
    var controller = new TeamOutputController(keyboard);
    var result = await controller
        .TickAsync(CreateContext(CreateTeamOutputSettings(), gameApi, logger), new TeamOutputState(), combatState)
        .ConfigureAwait(false);

    AssertFalse(result.ShouldSkipNormalWork, "valid leader target should allow normal combat work");
    AssertSequence(
        new[] { "F2", "C", TeamOutputScriptSettings.DefaultAssistTargetKey },
        keyboard.Keys.ToArray(),
        "output assist key sequence");
    AssertLeaderTargetAdopted(combatState, targetServerObjectId, "output should hand leader target to normal combat");
    AssertFalse(
        !(gameApi.LastLockedTargetContext?.BypassMemoryCache ?? false),
        "output assist target verification should bypass VMM cache");
}

static async Task TestTeamOutputUsesConfiguredAssistTargetKeyAsync()
{
    var keyboard = new RecordingKeyboardInput();
    var logger = new InMemoryRoadhogLogger();
    const uint targetServerObjectId = 5000;
    const string assistTargetKey = "G";
    var self = CreatePartyMemberSnapshot(1000, "Dps", true, false, 0.0);
    var leader = CreatePartyMemberSnapshot(2000, "Leader", false, true, 4.0) with
    {
        LiveTargetServerObjectId = targetServerObjectId
    };
    var gameApi = CreateTeamSupportGameApi(self, leader);
    keyboard.AfterPress = key =>
    {
        if (string.Equals(key, "F2", StringComparison.Ordinal))
        {
            SetFakeLockedTarget(gameApi, leader.ServerObjectId, 0, 0, 0);
        }
        else if (string.Equals(key, assistTargetKey, StringComparison.Ordinal))
        {
            SetFakeLockedTarget(
                gameApi,
                targetServerObjectId,
                LockedTargetSnapshot.MonsterObjectType,
                leader.ServerObjectId,
                100);
        }
    };

    var settings = CreateTeamOutputSettings();
    settings.Team.Output!.AssistTargetKey = assistTargetKey;
    var combatState = new StationaryCombatState();
    var controller = new TeamOutputController(keyboard);
    var result = await controller
        .TickAsync(CreateContext(settings, gameApi, logger), new TeamOutputState(), combatState)
        .ConfigureAwait(false);

    AssertFalse(result.ShouldSkipNormalWork, "configured assist key should allow normal combat work");
    AssertSequence(new[] { "F2", "C", assistTargetKey }, keyboard.Keys.ToArray(), "output should use configured assist key");
    AssertLeaderTargetAdopted(combatState, targetServerObjectId, "output should adopt target selected by configured assist key");
}

static async Task TestTeamOutputRejectsNonMonsterLeaderTargetAsync()
{
    var keyboard = new RecordingKeyboardInput();
    var logger = new InMemoryRoadhogLogger();
    const uint playerTargetServerObjectId = 5000;
    var self = CreatePartyMemberSnapshot(1000, "Dps", true, false, 0.0);
    var leader = CreatePartyMemberSnapshot(2000, "Leader", false, true, 4.0) with
    {
        LiveTargetServerObjectId = playerTargetServerObjectId
    };
    var gameApi = CreateTeamSupportGameApi(self, leader);
    keyboard.AfterPress = key =>
    {
        if (string.Equals(key, "F2", StringComparison.Ordinal))
        {
            SetFakeLockedTarget(gameApi, leader.ServerObjectId, 0, 0, 0);
        }
        else if (string.Equals(key, TeamOutputScriptSettings.DefaultAssistTargetKey, StringComparison.Ordinal))
        {
            SetFakeLockedTarget(gameApi, playerTargetServerObjectId, 1, leader.ServerObjectId, 100);
        }
    };

    var controller = new TeamOutputController(keyboard);
    var result = await controller
        .TickAsync(CreateContext(CreateTeamOutputSettings(), gameApi, logger), new TeamOutputState())
        .ConfigureAwait(false);

    AssertFalse(!result.ShouldSkipNormalWork, "non-monster leader target should block normal combat work");
    AssertSequence(
        new[] { "F2", "C", TeamOutputScriptSettings.DefaultAssistTargetKey, "F2", "C" },
        keyboard.Keys.ToArray(),
        "output should return to leader follow after reject");
}

static async Task TestTeamOutputAcceptsAlreadyLockedLeaderTargetAsync()
{
    var keyboard = new RecordingKeyboardInput();
    var logger = new InMemoryRoadhogLogger();
    const uint targetServerObjectId = 5000;
    var self = CreatePartyMemberSnapshot(1000, "Dps", true, false, 0.0);
    var leader = CreatePartyMemberSnapshot(2000, "Leader", false, true, 4.0) with
    {
        LiveTargetServerObjectId = targetServerObjectId
    };
    var gameApi = CreateTeamSupportGameApi(self, leader);
    SetFakeLockedTarget(
        gameApi,
        targetServerObjectId,
        LockedTargetSnapshot.MonsterObjectType,
        leader.ServerObjectId,
        100);

    var combatState = new StationaryCombatState();
    var controller = new TeamOutputController(keyboard);
    var result = await controller
        .TickAsync(CreateContext(CreateTeamOutputSettings(), gameApi, logger), new TeamOutputState(), combatState)
        .ConfigureAwait(false);

    AssertFalse(result.ShouldSkipNormalWork, "already locked leader target should allow normal combat work");
    AssertEqual(0, keyboard.Keys.Count, "already locked leader target should not switch back to leader or press assist key");
    AssertLeaderTargetAdopted(combatState, targetServerObjectId, "output should adopt already locked leader target");
    AssertFalse(
        !(gameApi.LastLockedTargetContext?.BypassMemoryCache ?? false),
        "output already-locked target check should bypass VMM cache");
}

static async Task TestTeamOutputSkipsPartyMemberLeaderTargetAsync()
{
    var keyboard = new RecordingKeyboardInput();
    var logger = new InMemoryRoadhogLogger();
    var self = CreatePartyMemberSnapshot(1000, "Dps", true, false, 0.0);
    var leader = CreatePartyMemberSnapshot(2000, "Leader", false, true, 4.0) with
    {
        LiveTargetServerObjectId = self.ServerObjectId
    };
    var gameApi = CreateTeamSupportGameApi(self, leader);
    keyboard.AfterPress = key =>
    {
        if (string.Equals(key, "F2", StringComparison.Ordinal))
        {
            SetFakeLockedTarget(gameApi, leader.ServerObjectId, 0, 0, 0);
        }
    };

    var combatState = new StationaryCombatState();
    var controller = new TeamOutputController(keyboard);
    var result = await controller
        .TickAsync(CreateContext(CreateTeamOutputSettings(), gameApi, logger), new TeamOutputState(), combatState)
        .ConfigureAwait(false);

    AssertFalse(!result.ShouldSkipNormalWork, "party-member leader target should block output normal combat when stop is enabled");
    AssertSequence(new[] { "F2", "C" }, keyboard.Keys.ToArray(), "output should follow leader without assisting a party-member target");
    AssertFalse(combatState.Fighting, "party-member leader target should not enter combat");
    AssertFalse(
        !logger.Entries.Any(entry =>
            entry.EventName == "team_output.leader_target.skipped" &&
            string.Equals(entry.Fields["reason"]?.ToString(), "known_team_side_target", StringComparison.Ordinal)),
        "output should log known team-side target skip");
}

static async Task TestTeamOutputAcceptsMonsterTargetingSpiritmasterLeaderPetAsync()
{
    var keyboard = new RecordingKeyboardInput();
    var logger = new InMemoryRoadhogLogger();
    const uint petServerObjectId = 3000;
    const uint targetServerObjectId = 5000;
    var self = CreatePartyMemberSnapshot(1000, "Dps", true, false, 0.0);
    var leader = CreatePartyMemberSnapshot(2000, "Leader", false, true, 4.0) with
    {
        Class = AionClassId.Spiritmaster,
        ClassId = (byte)AionClassId.Spiritmaster,
        ClassName = "Spiritmaster",
        LiveTargetServerObjectId = targetServerObjectId
    };
    var gameApi = CreateTeamSupportGameApi(self, leader);
    gameApi.SummonedPetRoster = CreateTeamSupportRoster(self.ServerObjectId, leader, petServerObjectId);

    var f2PressCount = 0;
    keyboard.AfterPress = key =>
    {
        if (string.Equals(key, "F2", StringComparison.Ordinal))
        {
            f2PressCount++;
            SetFakeLockedTarget(
                gameApi,
                f2PressCount == 1 ? petServerObjectId : leader.ServerObjectId,
                0,
                0,
                0);
        }
        else if (string.Equals(key, TeamOutputScriptSettings.DefaultAssistTargetKey, StringComparison.Ordinal))
        {
            SetFakeLockedTarget(
                gameApi,
                targetServerObjectId,
                LockedTargetSnapshot.MonsterObjectType,
                petServerObjectId,
                100);
        }
    };

    var combatState = new StationaryCombatState();
    var controller = new TeamOutputController(keyboard);
    var result = await controller
        .TickAsync(CreateContext(CreateTeamOutputSettings(), gameApi, logger), new TeamOutputState(), combatState)
        .ConfigureAwait(false);

    AssertFalse(result.ShouldSkipNormalWork, "monster targeting leader pet should allow output combat");
    AssertSequence(
        new[] { "F2", "F2", "C", TeamOutputScriptSettings.DefaultAssistTargetKey },
        keyboard.Keys.ToArray(),
        "spiritmaster leader body should be confirmed before assist");
    AssertLeaderTargetAdopted(combatState, targetServerObjectId, "output should adopt monster targeting spiritmaster pet");
}

static async Task TestTeamOutputAcceptsLeaderPetTargetWhenClassUnknownAsync()
{
    var keyboard = new RecordingKeyboardInput();
    var logger = new InMemoryRoadhogLogger();
    const uint petServerObjectId = 3000;
    const uint targetServerObjectId = 5000;
    var self = CreatePartyMemberSnapshot(1000, "Dps", true, false, 0.0);
    var leader = CreatePartyMemberSnapshot(2000, "Leader", false, true, 4.0) with
    {
        Class = null,
        ClassId = 0,
        ClassName = string.Empty,
        LiveTargetServerObjectId = targetServerObjectId
    };
    var gameApi = CreateTeamSupportGameApi(self, leader);
    gameApi.SummonedPetRoster = CreateTeamSupportRoster(self.ServerObjectId, leader, petServerObjectId);
    keyboard.AfterPress = key =>
    {
        if (string.Equals(key, "F2", StringComparison.Ordinal))
        {
            SetFakeLockedTarget(gameApi, leader.ServerObjectId, LockedTargetSnapshot.PlayerObjectType, 0, 100);
        }
        else if (string.Equals(key, TeamOutputScriptSettings.DefaultAssistTargetKey, StringComparison.Ordinal))
        {
            SetFakeLockedTarget(
                gameApi,
                targetServerObjectId,
                LockedTargetSnapshot.MonsterObjectType,
                petServerObjectId,
                100);
        }
    };

    var combatState = new StationaryCombatState();
    var controller = new TeamOutputController(keyboard);
    var result = await controller
        .TickAsync(CreateContext(CreateTeamOutputSettings(), gameApi, logger), new TeamOutputState(), combatState)
        .ConfigureAwait(false);

    AssertFalse(result.ShouldSkipNormalWork, "leader pet target should allow output combat even when leader class is unknown");
    AssertSequence(
        new[] { "F2", "C", TeamOutputScriptSettings.DefaultAssistTargetKey },
        keyboard.Keys.ToArray(),
        "output should assist leader target");
    AssertLeaderTargetAdopted(combatState, targetServerObjectId, "output should adopt target that is attacking leader pet");
}

static async Task TestTeamOutputAssistsAlreadySelectedLeaderAsync()
{
    var keyboard = new RecordingKeyboardInput();
    var logger = new InMemoryRoadhogLogger();
    const uint targetServerObjectId = 5000;
    var self = CreatePartyMemberSnapshot(1000, "Dps", true, false, 0.0);
    var leader = CreatePartyMemberSnapshot(2000, "Leader", false, true, 4.0) with
    {
        LiveTargetServerObjectId = targetServerObjectId
    };
    var gameApi = CreateTeamSupportGameApi(self, leader);
    SetFakeLockedTarget(gameApi, leader.ServerObjectId, 0, 0, 0);
    keyboard.AfterPress = key =>
    {
        if (string.Equals(key, TeamOutputScriptSettings.DefaultAssistTargetKey, StringComparison.Ordinal))
        {
            SetFakeLockedTarget(
                gameApi,
                targetServerObjectId,
                LockedTargetSnapshot.MonsterObjectType,
                leader.ServerObjectId,
                100);
        }
    };

    var combatState = new StationaryCombatState();
    var controller = new TeamOutputController(keyboard);
    var result = await controller
        .TickAsync(CreateContext(CreateTeamOutputSettings(), gameApi, logger), new TeamOutputState(), combatState)
        .ConfigureAwait(false);

    AssertFalse(result.ShouldSkipNormalWork, "already selected leader target should allow normal combat work");
    AssertSequence(
        new[] { "C", TeamOutputScriptSettings.DefaultAssistTargetKey },
        keyboard.Keys.ToArray(),
        "already selected leader should press follow without another F-key before assist");
    AssertLeaderTargetAdopted(combatState, targetServerObjectId, "already selected leader assist should adopt target");
}

static async Task TestTeamOutputJumpsEveryFiveLeaderAssistsAsync()
{
    var keyboard = new RecordingKeyboardInput();
    var logger = new InMemoryRoadhogLogger();
    var self = CreatePartyMemberSnapshot(1000, "Dps", true, false, 0.0);
    var leader = CreatePartyMemberSnapshot(2000, "Leader", false, true, 4.0);
    var gameApi = CreateTeamSupportGameApi(self, leader);
    SetFakeLockedTarget(gameApi, leader.ServerObjectId, 0, 0, 0);

    var controller = new TeamOutputController(keyboard);
    var state = new TeamOutputState();
    var context = CreateContext(CreateTeamOutputSettings(), gameApi, logger);

    for (var i = 1; i <= 4; i++)
    {
        var result = await controller.TickAsync(context, state).ConfigureAwait(false);
        AssertFalse(!result.ShouldSkipNormalWork, "output should keep following leader before jump interval");
    }

    AssertEqual(4, keyboard.Keys.Count(key => string.Equals(key, "C", StringComparison.Ordinal)), "leader assist count before jump");
    AssertEqual(0, keyboard.Keys.Count(key => string.Equals(key, "Space", StringComparison.Ordinal)), "leader jump count before interval");
    AssertEqual(4, state.LeaderAssistPressCountSinceJump, "state count before jump");

    var fifth = await controller.TickAsync(context, state).ConfigureAwait(false);

    AssertFalse(!fifth.ShouldSkipNormalWork, "output should keep following leader on jump interval");
    AssertEqual(5, keyboard.Keys.Count(key => string.Equals(key, "C", StringComparison.Ordinal)), "leader assist count at jump");
    AssertEqual(1, keyboard.Keys.Count(key => string.Equals(key, "Space", StringComparison.Ordinal)), "leader jump count at interval");
    AssertEqual("C", keyboard.Keys[4], "fifth leader assist should press C first");
    AssertEqual("Space", keyboard.Keys[5], "fifth leader assist should press Space after C");
    AssertEqual(0, state.LeaderAssistPressCountSinceJump, "state count should reset after jump");
    AssertFalse(
        !logger.Entries.Any(entry => entry.EventName == "team_output.leader_jump.pressed"),
        "leader jump should be logged");

    var afterReset = await controller.TickAsync(context, state).ConfigureAwait(false);

    AssertFalse(!afterReset.ShouldSkipNormalWork, "output should continue following after jump");
    AssertEqual(6, keyboard.Keys.Count(key => string.Equals(key, "C", StringComparison.Ordinal)), "leader assist count after reset");
    AssertEqual(1, keyboard.Keys.Count(key => string.Equals(key, "Space", StringComparison.Ordinal)), "leader jump should not repeat immediately after reset");
    AssertEqual("C", keyboard.Keys[6], "first assist after reset should only press C");
    AssertEqual(1, state.LeaderAssistPressCountSinceJump, "state count after reset");
}

static async Task TestTeamOutputFallsBackOutsideGroupRangeAsync()
{
    var keyboard = new RecordingKeyboardInput();
    var logger = new InMemoryRoadhogLogger();
    const uint targetServerObjectId = 5000;
    var self = CreatePartyMemberSnapshot(1000, "Dps", true, false, 0.0);
    var leader = CreatePartyMemberSnapshot(2000, "Leader", false, true, 60.0) with
    {
        LiveTargetServerObjectId = targetServerObjectId
    };
    var settings = CreateTeamOutputSettings();
    settings.Team.GroupDistanceMeters = 20.0D;
    var controller = new TeamOutputController(keyboard);

    var result = await controller
        .TickAsync(CreateContext(settings, CreateTeamSupportGameApi(self, leader), logger), new TeamOutputState())
        .ConfigureAwait(false);

    AssertFalse(result.ShouldSkipNormalWork, "leader outside group range should fall back to ordinary work");
    AssertEqual(0, keyboard.Keys.Count, "leader outside group range should not press follow or assist keys");
}

static async Task TestTeamOutputStaysGroupedUntilLeaderExitDistanceAsync()
{
    var keyboard = new RecordingKeyboardInput();
    var logger = new InMemoryRoadhogLogger();
    var self = CreatePartyMemberSnapshot(1000, "Dps", true, false, 0.0);
    var leader = CreatePartyMemberSnapshot(2000, "Leader", false, true, 10.0);
    var gameApi = CreateTeamSupportGameApi(self, leader);
    SetFakeLockedTarget(gameApi, leader.ServerObjectId, 0, 0, 0);

    var settings = CreateTeamOutputSettings();
    settings.Team.GroupDistanceMeters = 20.0D;
    settings.Team.Output!.StopWhenLeaderHasNoTarget = true;
    var controller = new TeamOutputController(keyboard);
    var state = new TeamOutputState();
    var context = CreateContext(settings, gameApi, logger);

    var first = await controller.TickAsync(context, state).ConfigureAwait(false);
    AssertFalse(!first.ShouldSkipNormalWork, "leader inside enter distance should start output follow");
    AssertSequence(new[] { "C" }, keyboard.Keys.ToArray(), "already selected leader should only press follow inside enter distance");

    keyboard.Keys.Clear();
    leader = leader with { DistanceToLocalPlayer = 25.0D };
    gameApi.Party = CreateTeamSupportParty(self, leader);
    var second = await controller.TickAsync(context, state).ConfigureAwait(false);

    AssertFalse(!second.ShouldSkipNormalWork, "active output group should stay grouped past enter distance");
    AssertSequence(new[] { "C" }, keyboard.Keys.ToArray(), "active output group should still follow before exit distance");

    keyboard.Keys.Clear();
    leader = leader with { DistanceToLocalPlayer = 60.0D };
    gameApi.Party = CreateTeamSupportParty(self, leader);
    var third = await controller.TickAsync(context, state).ConfigureAwait(false);

    AssertFalse(third.ShouldSkipNormalWork, "leader beyond exit distance should release output back to ordinary work");
    AssertEqual(0, keyboard.Keys.Count, "leader beyond exit distance should not press follow keys");
}

static async Task TestTeamOutputWaitsForFiveConsecutiveLeaderUnavailableTicksAsync()
{
    var keyboard = new RecordingKeyboardInput();
    var logger = new InMemoryRoadhogLogger();
    var self = CreatePartyMemberSnapshot(1000, "Dps", true, false, 0.0);
    var leader = CreatePartyMemberSnapshot(2000, "Leader", false, true, 25.0);
    var gameApi = CreateTeamSupportGameApi(self);
    var settings = CreateTeamOutputSettings();
    settings.Team.GroupDistanceMeters = 20.0D;
    settings.Team.Output!.StopWhenLeaderHasNoTarget = true;
    var controller = new TeamOutputController(keyboard);
    var state = new TeamOutputState { LeaderGroupActive = true };
    var context = CreateContext(settings, gameApi, logger);

    gameApi.Party = CreateTeamPartyWithoutLeader(self);
    for (var i = 1; i <= 4; i++)
    {
        var missing = await controller.TickAsync(context, state).ConfigureAwait(false);
        AssertFalse(!missing.ShouldSkipNormalWork, "output should block normal work before five consecutive leader misses");
    }

    AssertEqual(4, state.ConsecutiveLeaderUnavailableTicks, "output leader unavailable tick count before recovery");
    AssertFalse(!state.LeaderGroupActive, "output should keep active group state during transient leader misses");

    gameApi.Party = CreateTeamSupportParty(self, leader);
    SetFakeLockedTarget(gameApi, leader.ServerObjectId, 0, 0, 0);
    var recovered = await controller.TickAsync(context, state).ConfigureAwait(false);

    AssertFalse(!recovered.ShouldSkipNormalWork, "output should recover leader follow when the next valid tick arrives");
    AssertEqual(0, state.ConsecutiveLeaderUnavailableTicks, "output should reset missing count after a valid leader tick");
    AssertFalse(!state.LeaderGroupActive, "output should use exit distance after transient leader misses");
    AssertSequence(new[] { "C" }, keyboard.Keys.ToArray(), "output should resume leader follow without pressing another F-key");

    keyboard.Keys.Clear();
    gameApi.Party = CreateTeamPartyWithoutLeader(self);
    for (var i = 1; i <= 4; i++)
    {
        var missing = await controller.TickAsync(context, state).ConfigureAwait(false);
        AssertFalse(!missing.ShouldSkipNormalWork, "output should keep blocking normal work for the first four misses");
    }

    var fifth = await controller.TickAsync(context, state).ConfigureAwait(false);

    AssertFalse(fifth.ShouldSkipNormalWork, "output should release normal work on the fifth consecutive leader miss");
    AssertEqual(5, state.ConsecutiveLeaderUnavailableTicks, "output missing count at fallback threshold");
    AssertFalse(state.LeaderGroupActive, "output should clear active group state when leader has been missing for five ticks");
    AssertEqual(0, keyboard.Keys.Count, "output should not press follow keys while the leader is unavailable");
}

static async Task TestTeamOutputContinuesWhenLeaderInvisibleStopDisabledAsync()
{
    var keyboard = new RecordingKeyboardInput();
    var logger = new InMemoryRoadhogLogger();
    const uint targetServerObjectId = 5000;
    var self = CreatePartyMemberSnapshot(1000, "Dps", true, false, 0.0);
    var leader = CreatePartyMemberSnapshot(2000, "Leader", false, true, 60.0) with
    {
        LiveTargetServerObjectId = targetServerObjectId
    };
    var gameApi = CreateTeamSupportGameApi(self, leader);
    keyboard.AfterPress = key =>
    {
        if (string.Equals(key, "F2", StringComparison.Ordinal))
        {
            SetFakeLockedTarget(gameApi, leader.ServerObjectId, 0, 0, 0);
        }
    };

    var settings = CreateTeamOutputSettings();
    settings.Team.Output!.StopWhenLeaderHasNoTarget = false;
    settings.Team.Output.LeaderDistanceMeters = 100.0D;
    var controller = new TeamOutputController(keyboard);
    var result = await controller
        .TickAsync(CreateContext(settings, gameApi, logger), new TeamOutputState())
        .ConfigureAwait(false);

    AssertFalse(result.ShouldSkipNormalWork, "leader outside group range with stop disabled should allow normal combat work");
    AssertEqual(0, keyboard.Keys.Count, "leader outside group range should not press follow keys");
}

static async Task TestTeamOutputStopsWhenLeaderDeadAsync()
{
    var keyboard = new RecordingKeyboardInput();
    var logger = new InMemoryRoadhogLogger();
    var self = CreatePartyMemberSnapshot(1000, "Dps", true, false, 0.0);
    var leader = CreatePartyMemberSnapshot(2000, "Leader", false, true, 4.0) with
    {
        CurrentHp = 0,
        MaxHp = 100
    };
    var gameApi = CreateTeamSupportGameApi(self, leader);
    keyboard.AfterPress = key =>
    {
        if (string.Equals(key, "F2", StringComparison.Ordinal))
        {
            SetFakeLockedTarget(gameApi, leader.ServerObjectId, 0, 0, 0);
        }
    };

    var controller = new TeamOutputController(keyboard);
    var result = await controller
        .TickAsync(CreateContext(CreateTeamOutputSettings(), gameApi, logger), new TeamOutputState())
        .ConfigureAwait(false);

    AssertFalse(!result.ShouldSkipNormalWork, "dead leader should block normal output work");
    AssertSequence(new[] { "F2", "C" }, keyboard.Keys.ToArray(), "dead leader branch should return to leader follow");
}

static async Task TestTeamOutputDefersFollowWhileFightingAsync()
{
    var keyboard = new RecordingKeyboardInput();
    var logger = new InMemoryRoadhogLogger();
    const uint targetServerObjectId = 5000;
    var self = CreatePartyMemberSnapshot(1000, "Dps", true, false, 0.0);
    var leader = CreatePartyMemberSnapshot(2000, "Leader", false, true, 10.0) with
    {
        LiveTargetServerObjectId = targetServerObjectId
    };
    var combatState = new StationaryCombatState
    {
        Fighting = true,
        CandidateEntityId = 100
    };

    var controller = new TeamOutputController(keyboard);
    var result = await controller
        .TickAsync(CreateContext(CreateTeamOutputSettings(), CreateTeamSupportGameApi(self, leader), logger), new TeamOutputState(), combatState)
        .ConfigureAwait(false);

    AssertFalse(result.ShouldSkipNormalWork, "active combat should finish before output follows leader");
    AssertEqual(0, keyboard.Keys.Count, "active combat should not be interrupted by output follow keys");
}

static async Task TestTeamOutputAllowsSelfDefenseBeforeLeaderAssistAsync()
{
    var keyboard = new RecordingKeyboardInput();
    var logger = new InMemoryRoadhogLogger();
    const uint targetServerObjectId = 5000;
    var self = CreatePartyMemberSnapshot(1000, "Dps", true, false, 0.0) with
    {
        Class = AionClassId.Gladiator,
        ClassId = (byte)AionClassId.Gladiator,
        ClassName = "Gladiator"
    };
    var leader = CreatePartyMemberSnapshot(2000, "Leader", false, true, 4.0) with
    {
        LiveTargetServerObjectId = targetServerObjectId
    };
    var gameApi = CreateTeamSupportGameApi(self, leader);
    gameApi.WorldObjects = new[]
    {
        CreateMonsterWorldObject(100, 9000, self.ServerObjectId, new Vector3Snapshot(2, 0, 0))
    };

    var controller = new TeamOutputController(keyboard);
    var result = await controller
        .TickAsync(CreateContext(CreateTeamOutputSettings(), gameApi, logger), new TeamOutputState())
        .ConfigureAwait(false);

    AssertFalse(result.ShouldSkipNormalWork, "self defense threat should hand this tick back to normal combat");
    AssertEqual(0, keyboard.Keys.Count, "self defense branch should not press leader assist keys");
}

static async Task TestTeamOutputFollowsLeaderWhenSelfDefenseDisabledAsync()
{
    var keyboard = new RecordingKeyboardInput();
    var logger = new InMemoryRoadhogLogger();
    const uint targetServerObjectId = 5000;
    var self = CreatePartyMemberSnapshot(1000, "Dps", true, false, 0.0) with
    {
        Class = AionClassId.Gladiator,
        ClassId = (byte)AionClassId.Gladiator,
        ClassName = "Gladiator"
    };
    var leader = CreatePartyMemberSnapshot(2000, "Leader", false, true, 4.0) with
    {
        LiveTargetServerObjectId = targetServerObjectId
    };
    var gameApi = CreateTeamSupportGameApi(self, leader);
    gameApi.WorldObjects = new[]
    {
        CreateMonsterWorldObject(100, 9000, self.ServerObjectId, new Vector3Snapshot(2, 0, 0))
    };
    keyboard.AfterPress = key =>
    {
        if (string.Equals(key, "F2", StringComparison.Ordinal))
        {
            SetFakeLockedTarget(gameApi, leader.ServerObjectId, 0, 0, 0);
        }
        else if (string.Equals(key, TeamOutputScriptSettings.DefaultAssistTargetKey, StringComparison.Ordinal))
        {
            SetFakeLockedTarget(
                gameApi,
                targetServerObjectId,
                LockedTargetSnapshot.MonsterObjectType,
                leader.ServerObjectId,
                100);
        }
    };

    var settings = CreateTeamOutputSettings();
    settings.Team.Output!.AllowSelfDefense = false;
    var controller = new TeamOutputController(keyboard);
    var result = await controller
        .TickAsync(CreateContext(settings, gameApi, logger), new TeamOutputState())
        .ConfigureAwait(false);

    AssertFalse(result.ShouldSkipNormalWork, "disabled self defense should keep normal leader assist logic");
    AssertSequence(
        new[] { "F2", "C", TeamOutputScriptSettings.DefaultAssistTargetKey },
        keyboard.Keys.ToArray(),
        "output should still assist leader when self defense is disabled");
}

static async Task TestTeamLeaderProtectionPrioritizesHealerThreatsAsync()
{
    var self = CreatePartyMemberSnapshot(1000, "Leader", true, true, 0.0) with
    {
        Class = AionClassId.Gladiator,
        ClassId = (byte)AionClassId.Gladiator,
        ClassName = "Gladiator"
    };
    var cleric = CreatePartyMemberSnapshot(2000, "Cleric", false, false, 12.0) with
    {
        Class = AionClassId.Cleric,
        ClassId = (byte)AionClassId.Cleric,
        ClassName = "Cleric"
    };
    var gladiator = CreatePartyMemberSnapshot(3000, "Gladiator", false, false, 4.0) with
    {
        Class = AionClassId.Gladiator,
        ClassId = (byte)AionClassId.Gladiator,
        ClassName = "Gladiator"
    };
    var gameApi = CreateTeamSupportGameApi(self, cleric, gladiator);
    var snapshot = (await new TeamMonitor(gameApi, new InMemoryRoadhogLogger())
            .ReadSnapshotAsync(new GameApiReadContext("account", 0, string.Empty, string.Empty))
            .ConfigureAwait(false))
        .Value!;
    var threat = TeamLeaderProtectionSelector.SelectThreat(
        snapshot,
        new[]
        {
            CreateMonsterWorldObject(100, 9000, gladiator.ServerObjectId, new Vector3Snapshot(2, 0, 0)),
            CreateMonsterWorldObject(101, 9001, cleric.ServerObjectId, new Vector3Snapshot(18, 0, 0))
        },
        new Vector3Snapshot(0, 0, 0),
        20.0D);

    AssertEqual((ushort)101, threat?.Target.EntityId ?? 0, "leader should protect cleric before nearer non-healer");
    AssertEqual(0, threat?.Priority ?? -1, "cleric should have highest protection priority");
}

static async Task TestTeamLeaderProtectionOnlyIncludesSpiritmasterPetsAsync()
{
    var self = CreatePartyMemberSnapshot(1000, "Leader", true, true, 0.0) with
    {
        Class = AionClassId.Gladiator,
        ClassId = (byte)AionClassId.Gladiator,
        ClassName = "Gladiator"
    };
    var chanter = CreatePartyMemberSnapshot(2000, "Chanter", false, false, 4.0) with
    {
        Class = AionClassId.Chanter,
        ClassId = (byte)AionClassId.Chanter,
        ClassName = "Chanter"
    };
    var spiritmaster = CreatePartyMemberSnapshot(3000, "Spirit", false, false, 6.0) with
    {
        Class = AionClassId.Spiritmaster,
        ClassId = (byte)AionClassId.Spiritmaster,
        ClassName = "Spiritmaster"
    };
    var gameApi = CreateTeamSupportGameApi(self, chanter, spiritmaster);
    var now = DateTimeOffset.Now;
    gameApi.SummonedPetRoster = new SummonedPetRosterSnapshot(
        self.ServerObjectId,
        0,
        now,
        new OwnedSummonedPetSnapshot(
            SummonedPetOwnerKind.LocalPlayer,
            self.ServerObjectId,
            self.Name,
            string.Empty,
            SummonedPetSnapshot.NotSummoned(self.ServerObjectId, now),
            0,
            Array.Empty<AbnormalStatusEntrySnapshot>()),
        new[]
        {
            CreatePartyPet(chanter, 8100),
            CreatePartyPet(spiritmaster, 8200)
        },
        new[] { chanter.ServerObjectId, spiritmaster.ServerObjectId });
    var snapshot = (await new TeamMonitor(gameApi, new InMemoryRoadhogLogger())
            .ReadSnapshotAsync(new GameApiReadContext("account", 0, string.Empty, string.Empty))
            .ConfigureAwait(false))
        .Value!;
    var threat = TeamLeaderProtectionSelector.SelectThreat(
        snapshot,
        new[]
        {
            CreateMonsterWorldObject(100, 9000, 8100, new Vector3Snapshot(1, 0, 0)),
            CreateMonsterWorldObject(101, 9001, 8200, new Vector3Snapshot(12, 0, 0))
        },
        new Vector3Snapshot(0, 0, 0),
        20.0D);

    AssertEqual((ushort)101, threat?.Target.EntityId ?? 0, "only spiritmaster party member pets should be protected");
    AssertFalse(threat?.ProtectedObjectIsPet != true, "spiritmaster pet threat should be marked as pet protection");
}

static async Task TestTeamLeaderProtectionIgnoresMembersOutsideGroupDistanceAsync()
{
    var self = CreatePartyMemberSnapshot(1000, "Leader", true, true, 0.0) with
    {
        Class = AionClassId.Gladiator,
        ClassId = (byte)AionClassId.Gladiator,
        ClassName = "Gladiator"
    };
    var farCleric = CreatePartyMemberSnapshot(2000, "FarCleric", false, false, 30.0) with
    {
        Class = AionClassId.Cleric,
        ClassId = (byte)AionClassId.Cleric,
        ClassName = "Cleric"
    };
    var nearGladiator = CreatePartyMemberSnapshot(3000, "NearGladiator", false, false, 10.0) with
    {
        Class = AionClassId.Gladiator,
        ClassId = (byte)AionClassId.Gladiator,
        ClassName = "Gladiator"
    };
    var gameApi = CreateTeamSupportGameApi(self, farCleric, nearGladiator);
    var snapshot = (await new TeamMonitor(gameApi, new InMemoryRoadhogLogger())
            .ReadSnapshotAsync(new GameApiReadContext("account", 0, string.Empty, string.Empty))
            .ConfigureAwait(false))
        .Value!;
    var threat = TeamLeaderProtectionSelector.SelectThreat(
        snapshot,
        new[]
        {
            CreateMonsterWorldObject(100, 9000, farCleric.ServerObjectId, new Vector3Snapshot(5, 0, 0)),
            CreateMonsterWorldObject(101, 9001, nearGladiator.ServerObjectId, new Vector3Snapshot(12, 0, 0))
        },
        new Vector3Snapshot(0, 0, 0),
        20.0D);

    AssertEqual((ushort)101, threat?.Target.EntityId ?? 0, "leader should ignore higher-priority member outside group distance");
    AssertEqual(nearGladiator.ServerObjectId, threat?.ProtectedMember.ServerObjectId ?? 0, "near member should be protected");
}

static async Task TestTeamLeaderProtectionDrivesStationaryDefenseSelectionAsync()
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
        StationaryCombatRadius = 2
    };
    settings.Team = new TeamScriptSettings
    {
        Role = TeamRole.Leader,
        Leader = new TeamLeaderScriptSettings
        {
            Enabled = true
        }
    };

    var leader = CreatePartyMemberSnapshot(1000, "Leader", true, true, 0.0) with
    {
        Class = AionClassId.Gladiator,
        ClassId = (byte)AionClassId.Gladiator,
        ClassName = "Gladiator"
    };
    var cleric = CreatePartyMemberSnapshot(2000, "Cleric", false, false, 6.0) with
    {
        Class = AionClassId.Cleric,
        ClassId = (byte)AionClassId.Cleric,
        ClassName = "Cleric"
    };
    var gameApi = CreateTeamSupportGameApi(leader, cleric);
    gameApi.Player = new PlayerSnapshot(
        1,
        0,
        leader.Name,
        100,
        100,
        100,
        100,
        0,
        new Vector3Snapshot(0, 0, 0),
        DateTimeOffset.Now);
    gameApi.WorldObjects = new[]
    {
        CreateMonsterWorldObject(101, 9001, cleric.ServerObjectId, new Vector3Snapshot(8, 0, 0))
    };

    var keyboard = new RecordingKeyboardInput();
    var controller = new StationaryCombatController(keyboard, new SemiAutoCombatController(keyboard));
    var state = new StationaryCombatState();

    await controller
        .TickAsync(
            CreateContext(settings, gameApi, new InMemoryRoadhogLogger()),
            SemiAutoSkillPlan.FromSettings(settings.Skills),
            new SemiAutoCombatState(),
            state)
        .ConfigureAwait(false);

    AssertEqual((ushort)101, state.CandidateEntityId, "leader should select monster targeting protected cleric");
    AssertFalse(!state.CurrentTargetIsMaintenanceDefense, "team protection target should be treated as maintenance defense");
    AssertFalse(!state.CurrentTargetBypassesHomeLeash, "team protection target should bypass stationary home leash");
}

static Task TestTeamGroupDistancePersistsFromUiAsync()
{
    Exception? failure = null;
    var thread = new Thread(() =>
    {
        try
        {
            var settings = CreateScriptSettings();
            settings.Team = new TeamScriptSettings
            {
                Role = TeamRole.Leader,
                GroupDistanceMeters = 18.5D,
                Leader = new TeamLeaderScriptSettings
                {
                    Enabled = true
                }
            };
            var configStore = new InMemoryAccountConfigStore(new AccountConfig
            {
                AccountName = "account1",
                ScriptSettings = settings
            });

            using var form = CreateAccountSettingsFormForTestsWithStore(configStore);
            AssertEqual("18.5", GetTextBoxTextForTest(form, "teamGroupDistanceTextBox"), "team group distance should load into UI");

            SetTextBoxTextForTest(form, "teamGroupDistanceTextBox", "22.5");
            var saved = InvokeSaveCurrentSettingsForTest(form, out var error);
            AssertFalse(!saved, "team group distance save failed: " + error);

            var load = configStore.LoadAllAsync().GetAwaiter().GetResult();
            AssertFalse(!load.Success, "saved config should load");
            var savedDistance = load.Value!
                .Single(account => string.Equals(account.AccountName, "account1", StringComparison.OrdinalIgnoreCase))
                .ScriptSettings
                ?.Team
                ?.GroupDistanceMeters ?? 0.0D;
            AssertEqual(22.5D, savedDistance, "team group distance should persist from UI");
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

#if DEBUG
static async Task TestRuntimeApiProbeCoversVmmReadPathsAsync()
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
            capturedAt),
        PlayerAbnormalStatuses = new PlayerAbnormalStatusSnapshot(
            10,
            capturedAt,
            1,
            new[] { new AbnormalStatusEntrySnapshot(0, 9001, 2, 0, 1, 0x1234) }),
        SummonedPet = SummonedPetSnapshot.NotSummoned(1711370025, capturedAt),
        SummonedPetRoster = SummonedPetRosterSnapshot.Empty(1711370025, capturedAt),
        Skills = new[]
        {
            new SkillSnapshot(101, "Probe Skill", 1, 1, "Probe Skill", 1, false, 0, 0)
        },
        InventoryItems = new[]
        {
            new InventoryItemSnapshot(100, 200, "Probe Item", 1, 0, false)
        },
        InventoryMoney = 12345,
        InventoryCapacity = 100,
        WorldObjects = new[]
        {
            new WorldObjectSnapshot(100, 1000, "Probe Object", "monster", new Vector3Snapshot(1, 2, 3), 4, 100, 100)
        },
        LootCorpses = new[]
        {
            new LootCorpseSnapshot(
                101,
                1001,
                3,
                LootCorpseSnapshot.MonsterObjectType,
                200001,
                50,
                "Probe Corpse",
                new Vector3Snapshot(2, 3, 4),
                5,
                0,
                100,
                0,
                1,
                0x25,
                capturedAt)
        },
        InventoryWindow = new InventoryWindowSnapshot(
            true,
            0,
            0,
            324.8,
            443.2,
            0x1000,
            0x2000,
            capturedAt,
            InventoryWindowRectSource.LegacyDialogRect,
            0x3000,
            0x4000)
    };
    var runtime = new RoadhogRuntime(gameApi, logger, accounts, null!);

    var result = await runtime.RunApiProbeAsync("account-scope").ConfigureAwait(false);

    AssertFalse(!result.Success, "runtime api probe should return a result");
    var probe = result.Value ?? throw new InvalidOperationException("runtime api probe returned null");
    AssertFalse(!probe.AllPassed, "all fake api probe checks should pass");
    AssertEqual(RoadhogApiProbeResult.RequiredCheckNames.Count, probe.TotalCount, "api probe check count");
    AssertSequence(
        RoadhogApiProbeResult.RequiredCheckNames,
        probe.Checks.Select(check => check.Name).ToArray(),
        "api probe check names");
    AssertFalse(
        !probe.ToDisplayText().Contains("address=0x10001000", StringComparison.Ordinal),
        "api probe display should include the exact resolved address");
    AssertFalse(!logger.Entries.Any(entry => entry.EventName == "api_probe.completed"), "api probe should log completion");

    AssertEqual(712, gameApi.LastPlayerContext?.ProcessId ?? 0, "player probe should use scoped process id");
    AssertEqual(712, gameApi.LastPlayerAbnormalContext?.ProcessId ?? 0, "player abnormal probe should use scoped process id");
    AssertEqual(712, gameApi.LastLockedTargetContext?.ProcessId ?? 0, "locked target probe should use scoped process id");
    AssertEqual(712, gameApi.LastLockedTargetAbnormalContext?.ProcessId ?? 0, "locked target abnormal probe should use scoped process id");
    AssertEqual(712, gameApi.LastSummonedPetContext?.ProcessId ?? 0, "summoned pet probe should use scoped process id");
    AssertEqual(712, gameApi.LastSummonedPetRosterContext?.ProcessId ?? 0, "summoned pet roster probe should use scoped process id");
    AssertEqual(712, gameApi.LastSkillsContext?.ProcessId ?? 0, "skills probe should use scoped process id");
    AssertEqual(712, gameApi.LastInventoryContext?.ProcessId ?? 0, "inventory probe should use scoped process id");
    AssertEqual(712, gameApi.LastInventoryMoneyContext?.ProcessId ?? 0, "money probe should use scoped process id");
    AssertEqual(712, gameApi.LastInventoryCapacityContext?.ProcessId ?? 0, "capacity probe should use scoped process id");
    AssertEqual(712, gameApi.LastWorldObjectsContext?.ProcessId ?? 0, "world objects probe should use scoped process id");
    AssertEqual(712, gameApi.LastLootCorpsesContext?.ProcessId ?? 0, "loot corpses probe should use scoped process id");
    AssertEqual(712, gameApi.LastInventoryWindowContext?.ProcessId ?? 0, "inventory window probe should use scoped process id");
    AssertEqual(712, gameApi.LastAddressProbeContext?.ProcessId ?? 0, "address probe should use scoped process id");
    AssertSequence(
        new[]
        {
            InventoryWindowRectSource.LegacyDialogRect,
            InventoryWindowRectSource.RootWidgetRectExperimental
        },
        gameApi.InventoryWindowRectSources,
        "inventory window probe rect sources");
}
#endif

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

static Task TestRuntimeWarningRecordsAndClearsReadFailuresAsync()
{
    var logger = new InMemoryRoadhogLogger();
    var runtimeStates = new AccountRuntimeManager(logger);
    runtimeStates.GetOrCreate("account1");

    runtimeStates.MarkWarning(
        "account1",
        RuntimeWarningText.FromPlayerReadFailure("Target process not found by PID: 1234"));

    var warningSnapshot = runtimeStates.Snapshot().Single();
    AssertEqual("游戏进程不存在或已退出", warningSnapshot.LastWarning ?? string.Empty, "runtime warning text");
    AssertFalse(warningSnapshot.LastWarningAt is null, "runtime warning timestamp");
    AssertEqual("idle", warningSnapshot.Status, "warning should not change account status");

    runtimeStates.ClearWarning("account1");

    var clearedSnapshot = runtimeStates.Snapshot().Single();
    AssertEqual(string.Empty, clearedSnapshot.LastWarning ?? string.Empty, "runtime warning should clear");
    AssertFalse(clearedSnapshot.LastWarningAt is not null, "runtime warning timestamp should clear");

    return Task.CompletedTask;
}

static async Task TestStationaryCombatRecordsPlayerReadWarningAsync()
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
    var runtimeStates = new AccountRuntimeManager(logger);
    runtimeStates.GetOrCreate("account1");
    var gameApi = new FakeGameApi
    {
        PlayerReadFallback = OperationResult<PlayerSnapshot>.Fail("failed to read local entity id at Game.dll+0x1234")
    };
    var controller = new StationaryCombatController(keyboard, new SemiAutoCombatController(keyboard));
    var context = CreateContext(settings, gameApi, logger, runtimeStates);

    await controller
        .TickAsync(context, SemiAutoSkillPlan.FromSettings(settings.Skills), new SemiAutoCombatState(), new StationaryCombatState())
        .ConfigureAwait(false);

    var warningSnapshot = runtimeStates.Snapshot().Single();
    AssertEqual("读取不到角色，疑似掉线或未进游戏", warningSnapshot.LastWarning ?? string.Empty, "stationary player warning");
    AssertFalse(warningSnapshot.LastWarningAt is null, "stationary player warning timestamp");

    gameApi.PlayerReadFallback = OperationResult<PlayerSnapshot>.Ok(
        gameApi.Player with { Position = new Vector3Snapshot(0, 0, 0) });

    await controller
        .TickAsync(context, SemiAutoSkillPlan.FromSettings(settings.Skills), new SemiAutoCombatState(), new StationaryCombatState())
        .ConfigureAwait(false);

    var clearedSnapshot = runtimeStates.Snapshot().Single();
    AssertEqual(string.Empty, clearedSnapshot.LastWarning ?? string.Empty, "stationary player warning should clear");
    AssertFalse(clearedSnapshot.LastWarningAt is not null, "stationary player warning timestamp should clear");
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

static Task TestRoadhogServiceOptionsEnableLoggingByDefaultAsync()
{
    var options = new RoadhogServiceOptions();
    AssertFalse(!options.EnableLogging, "logging should be enabled by default for all build configurations");
    return Task.CompletedTask;
}

static Task TestFileLoggerDeletesExpiredLogFilesAsync()
{
    var directory = CreateTempDirectory("roadhog-logs-");
    try
    {
        var expiredPath = Path.Combine(directory, "roadhog-20260101.log");
        var recentPath = Path.Combine(directory, "roadhog-20260102.log");
        File.WriteAllText(expiredPath, "expired");
        File.WriteAllText(recentPath, "recent");
        File.SetLastWriteTimeUtc(expiredPath, DateTime.UtcNow.AddDays(-2));
        File.SetLastWriteTimeUtc(recentPath, DateTime.UtcNow.AddHours(-2));

        var logger = new FileRoadhogLogger(directory);
        logger.Info("test.log_cleanup");

        AssertFalse(File.Exists(expiredPath), "expired log file should be deleted");
        AssertFalse(!File.Exists(recentPath), "recent log file should be preserved");
        AssertFalse(!File.Exists(Path.Combine(directory, "latest.log")), "latest.log should still be written");
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
    for (var offset = 0; offset < 26; offset++)
    {
        AssertHidCode(((char)('A' + offset)).ToString(), 0x04 + offset);
    }

    AssertHidCode("C", 0x06);
    AssertHidCode("I", 0x0C);
    AssertHidCode("S", 0x16);
    AssertHidCode(" W ", 0x1A);
    AssertHidCode("Space", 0x2C);
    AssertHidCode("D1", 0x1E);
    AssertHidCode("D0", 0x27);
    AssertHidCode("Oem3", 0x35);
    AssertHidCode("Backquote", 0x35);
    AssertHidCode("`", 0x35);
    AssertHidCode("OemMinus", 0x2D);
    AssertHidCode("OemPlus", 0x2E);
    AssertHidCode("OemComma", 0x36);
    AssertHidCode("Tab", 0x2B);
    AssertHidCode("F1", 0x3A);
    AssertHidCode("F2", 0x3B);
    AssertHidCode("F3", 0x3C);
    AssertHidCode("F4", 0x3D);
    AssertHidCode("F5", 0x3E);
    AssertHidCode("F6", 0x3F);
    AssertHidCode("F8", 0x41);
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

static async Task TestRuntimeTestMoveUsesScreenPointAsync()
{
    var logger = new InMemoryRoadhogLogger();
    var keyboard = new RecordingKeyboardInput();
    var runtime = new RoadhogRuntime(
        new FakeGameApi(),
        logger,
        new AccountRuntimeManager(logger),
        null!,
        keyboardInput: keyboard);

    var result = await runtime.TestMoveMouseToScreenPointAsync(640, 360).ConfigureAwait(false);

    AssertFalse(!result.Success, "runtime test move should succeed");
    AssertSequence(
        new[] { "move:-2000,-2000", "move:-2000,-2000", "move:640,360" },
        keyboard.MouseCommands.ToArray(),
        "runtime test move should only move mouse to screen point");
    AssertFalse(keyboard.MouseCommands.Any(command => command.StartsWith("down:", StringComparison.Ordinal)), "runtime test move should not click mouse down");
    AssertFalse(keyboard.MouseCommands.Any(command => command.StartsWith("up:", StringComparison.Ordinal)), "runtime test move should not click mouse up");
}

static async Task TestRuntimeNormalizesInventoryWindowThenClosesAsync()
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
        InventoryWindow = CreateInventoryWindow(false, 594.2, 274.0)
    };
    var keyboard = new RecordingKeyboardInput();
    var mouseDown = false;
    var lastTargetX = 0;
    var lastTargetY = 0;

    keyboard.AfterPress = key =>
    {
        if (string.Equals(key, "I", StringComparison.OrdinalIgnoreCase))
        {
            gameApi.InventoryWindow = gameApi.InventoryWindow with
            {
                IsOpen = !gameApi.InventoryWindow.IsOpen,
                CapturedAt = DateTimeOffset.Now
            };
        }
    };
    keyboard.AfterMouseDown = _ => mouseDown = true;
    keyboard.AfterMouseUp = _ => mouseDown = false;
    keyboard.AfterMove = (deltaX, deltaY) =>
    {
        if (!mouseDown && deltaX >= 0 && deltaY >= 0)
        {
            lastTargetX = deltaX;
            lastTargetY = deltaY;
            return;
        }

        if (mouseDown &&
            deltaX == ScreenPointMouseMover.AbsoluteMouseResetDelta &&
            deltaY == ScreenPointMouseMover.AbsoluteMouseResetDelta &&
            lastTargetX > 0 &&
            lastTargetY > 0)
        {
            gameApi.InventoryWindow = CreateInventoryWindow(true, 0.0, 0.0);
        }
    };

    var runtime = new RoadhogRuntime(
        gameApi,
        logger,
        accounts,
        null!,
        keyboardInput: keyboard);

    var result = await runtime
        .NormalizeInventoryWindowToTopLeftAndCloseAsync("account-scope")
        .ConfigureAwait(false);

    AssertFalse(!result.Success, "inventory window normalization should succeed: " + result.Error);
    AssertFalse(gameApi.InventoryWindow.IsOpen, "inventory window should be closed after normalization");
    AssertFalse(!gameApi.InventoryWindow.IsAtTopLeft(), "inventory window rect should be normalized to top-left");
    AssertEqual(712, gameApi.LastInventoryWindowContext?.ProcessId ?? 0, "inventory window read should use scoped process id");
    AssertSequence(new[] { "I", "I" }, keyboard.Keys.ToArray(), "normalization should open and close the inventory");
    AssertFalse(!keyboard.MouseCommands.Contains("down:Left"), "normalization should hold left mouse for drag");
    AssertFalse(!keyboard.MouseCommands.Contains("up:Left"), "normalization should release left mouse after drag");
}

static async Task TestRuntimeNormalizesInventoryWindowLeavesOpenAsync()
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
        InventoryWindow = CreateInventoryWindow(false, 594.2, 274.0)
    };
    var keyboard = new RecordingKeyboardInput();
    var mouseDown = false;
    var lastTargetX = 0;
    var lastTargetY = 0;

    keyboard.AfterPress = key =>
    {
        if (string.Equals(key, "I", StringComparison.OrdinalIgnoreCase))
        {
            gameApi.InventoryWindow = gameApi.InventoryWindow with
            {
                IsOpen = !gameApi.InventoryWindow.IsOpen,
                CapturedAt = DateTimeOffset.Now
            };
        }
    };
    keyboard.AfterMouseDown = _ => mouseDown = true;
    keyboard.AfterMouseUp = _ => mouseDown = false;
    keyboard.AfterMove = (deltaX, deltaY) =>
    {
        if (!mouseDown && deltaX >= 0 && deltaY >= 0)
        {
            lastTargetX = deltaX;
            lastTargetY = deltaY;
            return;
        }

        if (mouseDown &&
            deltaX == ScreenPointMouseMover.AbsoluteMouseResetDelta &&
            deltaY == ScreenPointMouseMover.AbsoluteMouseResetDelta &&
            lastTargetX > 0 &&
            lastTargetY > 0)
        {
            gameApi.InventoryWindow = CreateInventoryWindow(true, 0.0, 0.0);
        }
    };

    var runtime = new RoadhogRuntime(
        gameApi,
        logger,
        accounts,
        null!,
        keyboardInput: keyboard);

    var result = await runtime
        .NormalizeInventoryWindowToTopLeftAsync("account-scope")
        .ConfigureAwait(false);

    AssertFalse(!result.Success, "inventory window normalization should succeed: " + result.Error);
    AssertFalse(!gameApi.InventoryWindow.IsOpen, "inventory window should remain open after normalization");
    AssertFalse(!gameApi.InventoryWindow.IsAtTopLeft(), "inventory window rect should be normalized to top-left");
    AssertEqual(712, gameApi.LastInventoryWindowContext?.ProcessId ?? 0, "inventory window read should use scoped process id");
    AssertSequence(new[] { "I" }, keyboard.Keys.ToArray(), "normalization should only open the inventory");
    AssertFalse(!keyboard.MouseCommands.Contains("down:Left"), "normalization should hold left mouse for drag");
    AssertFalse(!keyboard.MouseCommands.Contains("up:Left"), "normalization should release left mouse after drag");
}

static async Task TestRuntimeRegistersConfiguredBagCleanupSellItemsAsync()
{
    var previousResetCount = Environment.GetEnvironmentVariable("ROADHOG_DEATH_REVIVE_MOUSE_RESET_COUNT");
    var previousStepDelay = Environment.GetEnvironmentVariable("ROADHOG_DEATH_REVIVE_MOUSE_STEP_DELAY_MS");
    var previousHover = Environment.GetEnvironmentVariable("ROADHOG_BAG_SELL_REGISTER_HOVER_MS");
    var previousSlot0X = Environment.GetEnvironmentVariable("ROADHOG_BAG_SLOT0_CENTER_X");
    var previousSlot0Y = Environment.GetEnvironmentVariable("ROADHOG_BAG_SLOT0_CENTER_Y");
    var previousStepX = Environment.GetEnvironmentVariable("ROADHOG_BAG_SLOT_STEP_X");
    var previousStepY = Environment.GetEnvironmentVariable("ROADHOG_BAG_SLOT_STEP_Y");
    var previousPage1OffsetY = Environment.GetEnvironmentVariable("ROADHOG_BAG_PAGE1_OFFSET_Y");
    var previousPage2OffsetY = Environment.GetEnvironmentVariable("ROADHOG_BAG_PAGE2_OFFSET_Y");
    try
    {
        Environment.SetEnvironmentVariable("ROADHOG_DEATH_REVIVE_MOUSE_RESET_COUNT", "1");
        Environment.SetEnvironmentVariable("ROADHOG_DEATH_REVIVE_MOUSE_STEP_DELAY_MS", "0");
        Environment.SetEnvironmentVariable("ROADHOG_BAG_SELL_REGISTER_HOVER_MS", "0");
        Environment.SetEnvironmentVariable("ROADHOG_BAG_SLOT0_CENTER_X", "30");
        Environment.SetEnvironmentVariable("ROADHOG_BAG_SLOT0_CENTER_Y", "86");
        Environment.SetEnvironmentVariable("ROADHOG_BAG_SLOT_STEP_X", "40.875");
        Environment.SetEnvironmentVariable("ROADHOG_BAG_SLOT_STEP_Y", "35.5");
        Environment.SetEnvironmentVariable("ROADHOG_BAG_PAGE1_OFFSET_Y", "151");
        Environment.SetEnvironmentVariable("ROADHOG_BAG_PAGE2_OFFSET_Y", "298");

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
            InventoryWindow = CreateInventoryWindow(false, 0.0, 0.0),
            InventoryItems = new[]
            {
                new InventoryItemSnapshot(167000450, 10, "slot-0", 1, 0, false, 24, 2),
                new InventoryItemSnapshot(167000451, 11, "slot-26", 1, 26, false, 24, 2),
                new InventoryItemSnapshot(167000452, 12, "slot-53", 1, 53, false, 24, 2),
                new InventoryItemSnapshot(167000453, 13, "slot-80", 1, 80, false, 24, 2),
                new InventoryItemSnapshot(167000290, 14, "white", 10, 22, false, 24, 1),
                new InventoryItemSnapshot(167000454, 15, "excluded", 1, 32, false, 24, 2),
                new InventoryItemSnapshot(100100, 16, "green-equipment", 1, 3, false, 1, 2)
            }
        };
        var keyboard = new RecordingKeyboardInput();
        keyboard.AfterPress = key =>
        {
            if (string.Equals(key, "I", StringComparison.OrdinalIgnoreCase))
            {
                gameApi.InventoryWindow = gameApi.InventoryWindow with
                {
                    IsOpen = !gameApi.InventoryWindow.IsOpen,
                    CapturedAt = DateTimeOffset.Now
                };
            }
        };
        var runtime = new RoadhogRuntime(
            gameApi,
            logger,
            accounts,
            null!,
            keyboardInput: keyboard);
        var rules = BagCleanupRuleCatalog.CreateDefaultRules();
        rules.First(rule => rule.Key == BagCleanupRuleCatalog.GreenManastone).Enabled = true;
        rules.First(rule => rule.Key == BagCleanupRuleCatalog.GreenEquipment).Enabled = true;
        rules.First(rule => rule.Key == BagCleanupRuleCatalog.GreenEquipment).Action = BagCleanupAction.Discard;
        var settings = new MaintenanceScriptSettings
        {
            BagCleanupRules = rules,
            BagCleanupExcludedItemNames = new List<string> { "excluded" }
        };

        var result = await runtime
            .TestRegisterBagCleanupSellItemsAsync("account-scope", settings)
            .ConfigureAwait(false);

        AssertFalse(!result.Success, "sell registration should succeed: " + result.Error);
        AssertEqual(4, result.Value?.RegisteredCount ?? 0, "registered sell item count");
        AssertEqual("slot-0", result.Value?.Items[0].Name ?? string.Empty, "first registered sell item name");
        AssertEqual(30, result.Value?.Items[0].X ?? 0, "row 0 column 0 x");
        AssertEqual(86, result.Value?.Items[0].Y ?? 0, "row 0 column 0 y");
        AssertEqual(357, result.Value?.Items[1].X ?? 0, "row 2 column 8 x");
        AssertEqual(157, result.Value?.Items[1].Y ?? 0, "row 2 column 8 y");
        AssertEqual(357, result.Value?.Items[2].X ?? 0, "row 5 column 8 x");
        AssertEqual(308, result.Value?.Items[2].Y ?? 0, "row 5 column 8 y");
        AssertEqual(357, result.Value?.Items[3].X ?? 0, "row 8 column 8 x");
        AssertEqual(455, result.Value?.Items[3].Y ?? 0, "row 8 column 8 y");
        AssertSequence(
            new[]
            {
                "move:-2000,-2000", "move:30,86", "down:Right", "up:Right",
                "move:-2000,-2000", "move:357,157", "down:Right", "up:Right",
                "move:-2000,-2000", "move:357,308", "down:Right", "up:Right",
                "move:-2000,-2000", "move:357,455", "down:Right", "up:Right"
            },
            keyboard.MouseCommands.ToArray(),
            "fixed top-left registration should use the calibrated bag points");
        AssertEqual(712, gameApi.LastInventoryContext?.ProcessId ?? 0, "inventory read should use scoped process id");

        gameApi.InventoryWindow = CreateInventoryWindow(false, 349.6, 162.4);
        gameApi.InventoryItems = new[]
        {
            new InventoryItemSnapshot(167000450, 10, "slot-0", 1, 0, false, 24, 2)
        };
        keyboard.MouseCommands.Clear();
        settings.BagCleanupItemCoordinateMode = BagCleanupItemCoordinateMode.WindowRectRelativeExperimental;

        var experimentalResult = await runtime
            .TestRegisterBagCleanupSellItemsAsync("account-scope", settings)
            .ConfigureAwait(false);

        AssertFalse(!experimentalResult.Success, "experimental sell registration should succeed: " + experimentalResult.Error);
        AssertEqual(1, experimentalResult.Value?.RegisteredCount ?? 0, "experimental registered sell item count");
        AssertEqual(430, experimentalResult.Value?.Items[0].X ?? 0, "experimental registered sell item x");
        AssertEqual(276, experimentalResult.Value?.Items[0].Y ?? 0, "experimental registered sell item y");
        AssertEqual(
            InventoryWindowRectSource.RootWidgetRectExperimental,
            gameApi.LastInventoryWindowRectSource ?? InventoryWindowRectSource.LegacyDialogRect,
            "experimental registration should request root widget Rect");
        AssertSequence(
            new[] { "move:-2000,-2000", "move:430,276", "down:Right", "up:Right" },
            keyboard.MouseCommands.ToArray(),
            "experimental sell registration should use the window-relative bag point");
    }
    finally
    {
        Environment.SetEnvironmentVariable("ROADHOG_DEATH_REVIVE_MOUSE_RESET_COUNT", previousResetCount);
        Environment.SetEnvironmentVariable("ROADHOG_DEATH_REVIVE_MOUSE_STEP_DELAY_MS", previousStepDelay);
        Environment.SetEnvironmentVariable("ROADHOG_BAG_SELL_REGISTER_HOVER_MS", previousHover);
        Environment.SetEnvironmentVariable("ROADHOG_BAG_SLOT0_CENTER_X", previousSlot0X);
        Environment.SetEnvironmentVariable("ROADHOG_BAG_SLOT0_CENTER_Y", previousSlot0Y);
        Environment.SetEnvironmentVariable("ROADHOG_BAG_SLOT_STEP_X", previousStepX);
        Environment.SetEnvironmentVariable("ROADHOG_BAG_SLOT_STEP_Y", previousStepY);
        Environment.SetEnvironmentVariable("ROADHOG_BAG_PAGE1_OFFSET_Y", previousPage1OffsetY);
        Environment.SetEnvironmentVariable("ROADHOG_BAG_PAGE2_OFFSET_Y", previousPage2OffsetY);
    }
}

static async Task TestRuntimeTestsBagCleanupFromNpcThroughSellAsync()
{
    var previousVerify = Environment.GetEnvironmentVariable("ROADHOG_BAG_CLEANUP_SELL_VERIFY_DELAY_MS");
    var previousPointHover = Environment.GetEnvironmentVariable("ROADHOG_BAG_CLEANUP_POINT_HOVER_MS");
    var previousRegisterHover = Environment.GetEnvironmentVariable("ROADHOG_BAG_SELL_REGISTER_HOVER_MS");
    var previousSellItemEntryDelayMin = Environment.GetEnvironmentVariable("ROADHOG_BAG_CLEANUP_SELL_ITEM_ENTRY_DELAY_MIN_MS");
    var previousSellItemEntryDelayMax = Environment.GetEnvironmentVariable("ROADHOG_BAG_CLEANUP_SELL_ITEM_ENTRY_DELAY_MAX_MS");
    var previousMouseReset = Environment.GetEnvironmentVariable("ROADHOG_DEATH_REVIVE_MOUSE_RESET_COUNT");
    var previousMouseStep = Environment.GetEnvironmentVariable("ROADHOG_DEATH_REVIVE_MOUSE_STEP_DELAY_MS");
    var previousNpcAttempts = Environment.GetEnvironmentVariable("ROADHOG_BAG_CLEANUP_NPC_SELECT_ATTEMPTS");
    var previousNpcSelectDelay = Environment.GetEnvironmentVariable("ROADHOG_BAG_CLEANUP_NPC_SELECT_DELAY_MS");
    try
    {
        Environment.SetEnvironmentVariable("ROADHOG_BAG_CLEANUP_SELL_VERIFY_DELAY_MS", "0");
        Environment.SetEnvironmentVariable("ROADHOG_BAG_CLEANUP_POINT_HOVER_MS", "0");
        Environment.SetEnvironmentVariable("ROADHOG_BAG_SELL_REGISTER_HOVER_MS", "0");
        Environment.SetEnvironmentVariable("ROADHOG_BAG_CLEANUP_SELL_ITEM_ENTRY_DELAY_MIN_MS", "0");
        Environment.SetEnvironmentVariable("ROADHOG_BAG_CLEANUP_SELL_ITEM_ENTRY_DELAY_MAX_MS", "0");
        Environment.SetEnvironmentVariable("ROADHOG_DEATH_REVIVE_MOUSE_RESET_COUNT", "1");
        Environment.SetEnvironmentVariable("ROADHOG_DEATH_REVIVE_MOUSE_STEP_DELAY_MS", "0");
        Environment.SetEnvironmentVariable("ROADHOG_BAG_CLEANUP_NPC_SELECT_ATTEMPTS", "3");
        Environment.SetEnvironmentVariable("ROADHOG_BAG_CLEANUP_NPC_SELECT_DELAY_MS", "0");

        var logger = new InMemoryRoadhogLogger();
        var accounts = new AccountRuntimeManager(logger);
        accounts.MarkStarting(new AccountConfig
        {
            AccountName = "account-scope",
            ProcessId = 712,
            TargetProcessName = "Aion.bin",
            VmmDeviceName = "fpga"
        });

        var cleanupNpcName = "cleanup-vendor";
        var gameApi = new FakeGameApi
        {
            TargetEntityId = 0,
            TargetName = string.Empty,
            TargetCurrentHp = 0,
            TargetMaxHp = 0,
            TargetIsTargetingLocalPlayer = false,
            InventoryWindow = CreateInventoryWindow(false, 0.0, 0.0),
            InventoryMoney = 1000,
            InventoryItems = new[]
            {
                new InventoryItemSnapshot(167000450, 10, "green-manastone-a", 1, 0, false, 24, 2),
                new InventoryItemSnapshot(167000451, 11, "green-manastone-b", 1, 1, false, 24, 2),
                new InventoryItemSnapshot(1001, 12, "kept", 1, 2, false, 1, 4)
            }
        };
        var keyboard = new RecordingKeyboardInput();
        var f8Attempts = 0;
        keyboard.AfterPress = key =>
        {
            if (key == "I")
            {
                gameApi.InventoryWindow = CreateInventoryWindow(!gameApi.InventoryWindow.IsOpen, 0.0, 0.0);
                return;
            }

            if (key != "F8")
            {
                return;
            }

            f8Attempts++;
            gameApi.TargetEntityId = 77;
            gameApi.TargetServerObjectId = 7700;
            gameApi.TargetName = f8Attempts == 1 ? "other-player" : cleanupNpcName;
            gameApi.TargetCurrentHp = 0;
            gameApi.TargetMaxHp = 0;
        };

        var leftClicks = 0;
        keyboard.AfterMouseUp = button =>
        {
            if (button != RoadhogMouseButton.Left)
            {
                return;
            }

            leftClicks++;
            if (leftClicks == 2)
            {
                gameApi.InventoryItems = gameApi.InventoryItems
                    .Where(item => item.InstanceId is not 10UL and not 11UL)
                    .ToArray();
                gameApi.InventoryMoney += 200;
            }
        };

        var rules = BagCleanupRuleCatalog.CreateDefaultRules();
        rules.First(rule => rule.Key == BagCleanupRuleCatalog.GreenManastone).Enabled = true;
        var settings = new MaintenanceScriptSettings
        {
            BagCleanupRules = rules,
            BagCleanupSellItemClickX = 100,
            BagCleanupSellItemClickY = 200,
            BagCleanupSellButtonClickX = 300,
            BagCleanupSellButtonClickY = 400
        };
        var runtime = new RoadhogRuntime(
            gameApi,
            logger,
            accounts,
            null!,
            keyboardInput: keyboard);

        var result = await runtime
            .TestBagCleanupFromNpcAsync("account-scope", cleanupNpcName, settings)
            .ConfigureAwait(false);

        AssertFalse(!result.Success, "manual bag cleanup test should succeed: " + result.Error);
        AssertEqual(2, f8Attempts, "manual cleanup should keep pressing F8 until configured npc is selected");
        AssertSequence(new[] { "F8", "F8", "C", "I", "I" }, keyboard.Keys.ToArray(), "manual cleanup key sequence");
        AssertEqual(2, result.Value?.CandidateCount ?? 0, "manual cleanup candidate count");
        AssertEqual(2, result.Value?.RegisteredCount ?? 0, "manual cleanup registered count");
        AssertEqual(1000UL, result.Value?.InitialMoney ?? 0UL, "manual cleanup initial money");
        AssertEqual(1200UL, result.Value?.FinalMoney ?? 0UL, "manual cleanup final money");
        AssertEqual(200UL, result.Value?.MoneyDelta ?? 0UL, "manual cleanup money delta");
        AssertFalse(!keyboard.MouseCommands.Contains("down:Right"), "manual cleanup should right click bag items");
        AssertFalse(gameApi.InventoryWindow.IsOpen, "manual cleanup should close inventory before sell click");
        AssertEqual(712, gameApi.LastInventoryMoneyContext?.ProcessId ?? 0, "money read should use scoped process id");
        AssertFalse(!logger.Entries.Any(entry => entry.EventName == "bag_cleanup.manual_test.ok"), "manual cleanup should log success");
    }
    finally
    {
        Environment.SetEnvironmentVariable("ROADHOG_BAG_CLEANUP_SELL_VERIFY_DELAY_MS", previousVerify);
        Environment.SetEnvironmentVariable("ROADHOG_BAG_CLEANUP_POINT_HOVER_MS", previousPointHover);
        Environment.SetEnvironmentVariable("ROADHOG_BAG_SELL_REGISTER_HOVER_MS", previousRegisterHover);
        Environment.SetEnvironmentVariable("ROADHOG_BAG_CLEANUP_SELL_ITEM_ENTRY_DELAY_MIN_MS", previousSellItemEntryDelayMin);
        Environment.SetEnvironmentVariable("ROADHOG_BAG_CLEANUP_SELL_ITEM_ENTRY_DELAY_MAX_MS", previousSellItemEntryDelayMax);
        Environment.SetEnvironmentVariable("ROADHOG_DEATH_REVIVE_MOUSE_RESET_COUNT", previousMouseReset);
        Environment.SetEnvironmentVariable("ROADHOG_DEATH_REVIVE_MOUSE_STEP_DELAY_MS", previousMouseStep);
        Environment.SetEnvironmentVariable("ROADHOG_BAG_CLEANUP_NPC_SELECT_ATTEMPTS", previousNpcAttempts);
        Environment.SetEnvironmentVariable("ROADHOG_BAG_CLEANUP_NPC_SELECT_DELAY_MS", previousNpcSelectDelay);
    }
}

static async Task TestBagCleanupControllerSkipsWhenDisabledAsync()
{
    var logger = new InMemoryRoadhogLogger();
    var keyboard = new RecordingKeyboardInput();
    var gameApi = new FakeGameApi
    {
        InventoryItems = Enumerable.Range(0, 9)
            .Select(index => new InventoryItemSnapshot(1000u + (uint)index, (ulong)(100 + index), "item-" + index, 1, index, false, 24, 2))
            .ToArray()
    };
    var settings = CreateScriptSettings();
    settings.Maintenance = new MaintenanceScriptSettings
    {
        BagCleanupEnabled = false,
        BagCleanupThreshold = 5
    };
    var pathCalls = new List<string>();
    var controller = new BagCleanupController(
        keyboard,
        new InMemorySharedPathStore(),
        (context, pathName, points) =>
        {
            pathCalls.Add(pathName);
            return Task.FromResult(OperationResult.Ok());
        });
    var result = await controller
        .TickAfterLootAsync(CreateContext(settings, gameApi, logger), new BagCleanupState())
        .ConfigureAwait(false);

    AssertEqual(BagCleanupTickStatus.NotStarted, result.Status, "disabled cleanup should not start");
    AssertEqual(0, keyboard.Keys.Count, "disabled cleanup should not press keys");
    AssertEqual(0, pathCalls.Count, "disabled cleanup should not run paths");
}

static async Task TestBagCleanupControllerSellsItemsAndReturnsAsync()
{
    var previousTownSettle = Environment.GetEnvironmentVariable("ROADHOG_BAG_CLEANUP_TOWN_RETURN_SETTLE_MS");
    var previousVerify = Environment.GetEnvironmentVariable("ROADHOG_BAG_CLEANUP_SELL_VERIFY_DELAY_MS");
    var previousPointHover = Environment.GetEnvironmentVariable("ROADHOG_BAG_CLEANUP_POINT_HOVER_MS");
    var previousRegisterHover = Environment.GetEnvironmentVariable("ROADHOG_BAG_SELL_REGISTER_HOVER_MS");
    var previousSellItemEntryDelayMin = Environment.GetEnvironmentVariable("ROADHOG_BAG_CLEANUP_SELL_ITEM_ENTRY_DELAY_MIN_MS");
    var previousSellItemEntryDelayMax = Environment.GetEnvironmentVariable("ROADHOG_BAG_CLEANUP_SELL_ITEM_ENTRY_DELAY_MAX_MS");
    var previousMouseReset = Environment.GetEnvironmentVariable("ROADHOG_DEATH_REVIVE_MOUSE_RESET_COUNT");
    var previousMouseStep = Environment.GetEnvironmentVariable("ROADHOG_DEATH_REVIVE_MOUSE_STEP_DELAY_MS");
    var previousNpcAttempts = Environment.GetEnvironmentVariable("ROADHOG_BAG_CLEANUP_NPC_SELECT_ATTEMPTS");
    var previousNpcSelectDelay = Environment.GetEnvironmentVariable("ROADHOG_BAG_CLEANUP_NPC_SELECT_DELAY_MS");
    var previousTownMinDistance = Environment.GetEnvironmentVariable("ROADHOG_BAG_CLEANUP_TOWN_RETURN_MIN_DISTANCE");
    try
    {
        Environment.SetEnvironmentVariable("ROADHOG_BAG_CLEANUP_TOWN_RETURN_SETTLE_MS", "0");
        Environment.SetEnvironmentVariable("ROADHOG_BAG_CLEANUP_SELL_VERIFY_DELAY_MS", "0");
        Environment.SetEnvironmentVariable("ROADHOG_BAG_CLEANUP_POINT_HOVER_MS", "0");
        Environment.SetEnvironmentVariable("ROADHOG_BAG_SELL_REGISTER_HOVER_MS", "0");
        Environment.SetEnvironmentVariable("ROADHOG_BAG_CLEANUP_SELL_ITEM_ENTRY_DELAY_MIN_MS", "0");
        Environment.SetEnvironmentVariable("ROADHOG_BAG_CLEANUP_SELL_ITEM_ENTRY_DELAY_MAX_MS", "0");
        Environment.SetEnvironmentVariable("ROADHOG_DEATH_REVIVE_MOUSE_RESET_COUNT", "1");
        Environment.SetEnvironmentVariable("ROADHOG_DEATH_REVIVE_MOUSE_STEP_DELAY_MS", "0");
        Environment.SetEnvironmentVariable("ROADHOG_BAG_CLEANUP_NPC_SELECT_ATTEMPTS", "3");
        Environment.SetEnvironmentVariable("ROADHOG_BAG_CLEANUP_NPC_SELECT_DELAY_MS", "0");
        Environment.SetEnvironmentVariable("ROADHOG_BAG_CLEANUP_TOWN_RETURN_MIN_DISTANCE", "1");

        var logger = new InMemoryRoadhogLogger();
        var keyboard = new RecordingKeyboardInput();
        var cleanupNpcName = "清包商人";
        var gameApi = new FakeGameApi
        {
            TargetEntityId = 0,
            TargetCurrentHp = 0,
            TargetMaxHp = 0,
            TargetName = string.Empty,
            TargetIsTargetingLocalPlayer = false,
            InventoryWindow = CreateInventoryWindow(false, 0.0, 0.0),
            InventoryMoney = 1000,
            InventoryCapacity = 10,
            InventoryItems = new[]
            {
                new InventoryItemSnapshot(167000450, 10, "green-manastone-a", 1, 0, false, 24, 2),
                new InventoryItemSnapshot(167000451, 11, "green-manastone-b", 1, 1, false, 24, 2),
                new InventoryItemSnapshot(1001, 12, "kept", 1, 2, false, 1, 4),
                new InventoryItemSnapshot(1002, 13, "filler-3", 1, 3, false, 1, 1),
                new InventoryItemSnapshot(1003, 14, "filler-4", 1, 4, false, 1, 1),
                new InventoryItemSnapshot(1004, 15, "filler-5", 1, 5, false, 1, 1),
                new InventoryItemSnapshot(1005, 16, "filler-6", 1, 6, false, 1, 1)
            }
        };
        keyboard.AfterPress = key =>
        {
            if (key == "NumPad7")
            {
                gameApi.Player = gameApi.Player with
                {
                    Position = new Vector3Snapshot(100, 0, 0),
                    CapturedAt = DateTimeOffset.Now
                };
            }
            else if (key == "F8")
            {
                gameApi.TargetEntityId = 77;
                gameApi.TargetServerObjectId = 7700;
                gameApi.TargetName = cleanupNpcName;
                gameApi.TargetCurrentHp = 0;
                gameApi.TargetMaxHp = 0;
            }
            else if (key == "I")
            {
                gameApi.InventoryWindow = CreateInventoryWindow(!gameApi.InventoryWindow.IsOpen, 0.0, 0.0);
            }
        };

        var leftClicks = 0;
        keyboard.AfterMouseUp = button =>
        {
            if (button != RoadhogMouseButton.Left)
            {
                return;
            }

            leftClicks++;
            if (leftClicks == 2)
            {
                gameApi.InventoryItems = gameApi.InventoryItems
                    .Where(item => item.InstanceId is not 10UL and not 11UL)
                    .ToArray();
                gameApi.InventoryMoney += 200;
            }
        };

        var rules = BagCleanupRuleCatalog.CreateDefaultRules();
        rules.First(rule => rule.Key == BagCleanupRuleCatalog.GreenManastone).Enabled = true;
        var settings = CreateScriptSettings();
        settings.Paths.TownReturnKey = "NumPad7";
        settings.Paths.MaintenancePathName = "cleanup-path";
        settings.Maintenance = new MaintenanceScriptSettings
        {
            BagCleanupEnabled = true,
            BagCleanupThreshold = 5,
            BagCleanupRules = rules,
            BagCleanupSellItemClickX = 100,
            BagCleanupSellItemClickY = 200,
            BagCleanupSellButtonClickX = 300,
            BagCleanupSellButtonClickY = 400
        };
        var pathStore = new InMemorySharedPathStore(new SharedPathDocument
        {
            Name = "cleanup-path",
            CleanupNpcName = cleanupNpcName,
            Points = new List<SharedPathPoint>
            {
                new() { X = 1, Y = 0, Z = 0 },
                new() { X = 2, Y = 0, Z = 0 }
            }
        });
        var pathCalls = new List<string>();
        var controller = new BagCleanupController(
            keyboard,
            pathStore,
            (context, pathName, points) =>
            {
                pathCalls.Add(pathName + ":" + points.Count);
                return Task.FromResult(OperationResult.Ok());
            });
        var state = new BagCleanupState();
        var context = CreateContext(settings, gameApi, logger);
        BagCleanupTickResult? last = null;
        for (var i = 0; i < 30; i++)
        {
            last = await controller.TickAfterLootAsync(context, state).ConfigureAwait(false);
            if (last.Status == BagCleanupTickStatus.Completed ||
                last.Status == BagCleanupTickStatus.FatalFailure ||
                last.Status == BagCleanupTickStatus.RecoverableFailure)
            {
                break;
            }
        }

        AssertEqual(BagCleanupTickStatus.Completed, last?.Status ?? BagCleanupTickStatus.FatalFailure, "cleanup should complete");
        AssertSequence(new[] { "NumPad7", "F8", "C", "I", "I" }, keyboard.Keys.ToArray(), "cleanup should return, select npc, interact, and close inventory");
        AssertSequence(new[] { "cleanup-path:2", "cleanup-path 返回:2" }, pathCalls.ToArray(), "cleanup should follow path and reverse path");
        AssertFalse(gameApi.InventoryItems.Any(item => item.InstanceId is 10UL or 11UL), "sold items should be removed before verification");
        AssertEqual(1200UL, gameApi.InventoryMoney, "money should increase after sell");
        AssertFalse(gameApi.InventoryWindow.IsOpen, "cleanup should close inventory before sell click");
        AssertFalse(state.LastCompletedAt == DateTimeOffset.MinValue, "completion should start cleanup cooldown");
        var check = logger.Entries.FirstOrDefault(entry => entry.EventName == "bag_cleanup.check");
        AssertFalse(check is null, "cleanup check should be logged");
        AssertEqual(10, Convert.ToInt32(check!.Fields["totalSlots"]), "cleanup should use VMM inventory capacity");
        AssertEqual(7, Convert.ToInt32(check.Fields["occupiedSlots"]), "cleanup should count occupied slots from VMM inventory");
        AssertEqual("vmm", Convert.ToString(check.Fields["totalSlotsSource"]) ?? string.Empty, "cleanup capacity source");
        AssertFalse(!logger.Entries.Any(entry => entry.EventName == "bag_cleanup.return.verify.ok"), "cleanup should verify town return position");
        AssertFalse(!logger.Entries.Any(entry => entry.EventName == "bag_cleanup.inventory.close.requested"), "cleanup should request inventory close before sell");
        AssertFalse(!logger.Entries.Any(entry => entry.EventName == "bag_cleanup.verify.ok"), "cleanup should verify money increase");
        AssertFalse(!logger.Entries.Any(entry => entry.EventName == "bag_cleanup.complete"), "cleanup should log completion");
    }
    finally
    {
        Environment.SetEnvironmentVariable("ROADHOG_BAG_CLEANUP_TOWN_RETURN_SETTLE_MS", previousTownSettle);
        Environment.SetEnvironmentVariable("ROADHOG_BAG_CLEANUP_SELL_VERIFY_DELAY_MS", previousVerify);
        Environment.SetEnvironmentVariable("ROADHOG_BAG_CLEANUP_POINT_HOVER_MS", previousPointHover);
        Environment.SetEnvironmentVariable("ROADHOG_BAG_SELL_REGISTER_HOVER_MS", previousRegisterHover);
        Environment.SetEnvironmentVariable("ROADHOG_BAG_CLEANUP_SELL_ITEM_ENTRY_DELAY_MIN_MS", previousSellItemEntryDelayMin);
        Environment.SetEnvironmentVariable("ROADHOG_BAG_CLEANUP_SELL_ITEM_ENTRY_DELAY_MAX_MS", previousSellItemEntryDelayMax);
        Environment.SetEnvironmentVariable("ROADHOG_DEATH_REVIVE_MOUSE_RESET_COUNT", previousMouseReset);
        Environment.SetEnvironmentVariable("ROADHOG_DEATH_REVIVE_MOUSE_STEP_DELAY_MS", previousMouseStep);
        Environment.SetEnvironmentVariable("ROADHOG_BAG_CLEANUP_NPC_SELECT_ATTEMPTS", previousNpcAttempts);
        Environment.SetEnvironmentVariable("ROADHOG_BAG_CLEANUP_NPC_SELECT_DELAY_MS", previousNpcSelectDelay);
        Environment.SetEnvironmentVariable("ROADHOG_BAG_CLEANUP_TOWN_RETURN_MIN_DISTANCE", previousTownMinDistance);
    }
}

static async Task TestBagCleanupControllerDetectsTownReturnBeforeTimeoutAsync()
{
    var previousTownSettle = Environment.GetEnvironmentVariable("ROADHOG_BAG_CLEANUP_TOWN_RETURN_SETTLE_MS");
    var previousTownMinDistance = Environment.GetEnvironmentVariable("ROADHOG_BAG_CLEANUP_TOWN_RETURN_MIN_DISTANCE");
    try
    {
        Environment.SetEnvironmentVariable("ROADHOG_BAG_CLEANUP_TOWN_RETURN_SETTLE_MS", "30000");
        Environment.SetEnvironmentVariable("ROADHOG_BAG_CLEANUP_TOWN_RETURN_MIN_DISTANCE", "20");

        var logger = new InMemoryRoadhogLogger();
        var keyboard = new RecordingKeyboardInput();
        var gameApi = new FakeGameApi
        {
            TargetEntityId = 0,
            TargetCurrentHp = 0,
            TargetMaxHp = 0,
            TargetName = string.Empty,
            TargetIsTargetingLocalPlayer = false,
            InventoryCapacity = 10,
            InventoryItems = new[]
            {
                new InventoryItemSnapshot(167000450, 10, "green-manastone", 1, 0, false, 24, 2),
                new InventoryItemSnapshot(1001, 11, "filler-1", 1, 1, false, 1, 1),
                new InventoryItemSnapshot(1002, 12, "filler-2", 1, 2, false, 1, 1),
                new InventoryItemSnapshot(1003, 13, "filler-3", 1, 3, false, 1, 1),
                new InventoryItemSnapshot(1004, 14, "filler-4", 1, 4, false, 1, 1),
                new InventoryItemSnapshot(1005, 15, "filler-5", 1, 5, false, 1, 1),
                new InventoryItemSnapshot(1006, 16, "filler-6", 1, 6, false, 1, 1)
            }
        };
        var rules = BagCleanupRuleCatalog.CreateDefaultRules();
        rules.First(rule => rule.Key == BagCleanupRuleCatalog.GreenManastone).Enabled = true;
        var settings = CreateScriptSettings();
        settings.Paths.TownReturnKey = "NumPad7";
        settings.Paths.MaintenancePathName = "cleanup-path";
        settings.Maintenance = new MaintenanceScriptSettings
        {
            BagCleanupEnabled = true,
            BagCleanupThreshold = 5,
            BagCleanupRules = rules
        };
        var controller = new BagCleanupController(
            keyboard,
            new InMemorySharedPathStore(),
            (context, pathName, points) => Task.FromResult(OperationResult.Ok()));
        var state = new BagCleanupState();
        var context = CreateContext(settings, gameApi, logger);

        await controller.TickAfterLootAsync(context, state).ConfigureAwait(false);
        await controller.TickAfterLootAsync(context, state).ConfigureAwait(false);
        await controller.TickAfterLootAsync(context, state).ConfigureAwait(false);

        AssertEqual(BagCleanupStep.WaitTownReturnSettle, state.Step, "cleanup should wait after pressing town return");
        var unchanged = await controller.TickAfterLootAsync(context, state).ConfigureAwait(false);
        AssertEqual("waiting_for_town_return", unchanged.Reason, "unchanged position should keep waiting before timeout");

        gameApi.Player = gameApi.Player with { Position = new Vector3Snapshot(19, 0, 0) };
        var belowThreshold = await controller.TickAfterLootAsync(context, state).ConfigureAwait(false);
        AssertEqual("waiting_for_town_return", belowThreshold.Reason, "movement below twenty should keep waiting");

        gameApi.Player = gameApi.Player with { Position = new Vector3Snapshot(20, 0, 0) };
        var detected = await controller.TickAfterLootAsync(context, state).ConfigureAwait(false);
        AssertEqual("town_return_settled", detected.Reason, "movement of twenty should confirm town return immediately");
        AssertEqual(BagCleanupStep.LoadCleanupPath, state.Step, "confirmed town return should load cleanup path next");
        AssertFalse(!logger.Entries.Any(entry => entry.EventName == "bag_cleanup.return.verify.ok"), "early town return should be logged");
    }
    finally
    {
        Environment.SetEnvironmentVariable("ROADHOG_BAG_CLEANUP_TOWN_RETURN_SETTLE_MS", previousTownSettle);
        Environment.SetEnvironmentVariable("ROADHOG_BAG_CLEANUP_TOWN_RETURN_MIN_DISTANCE", previousTownMinDistance);
    }
}

static async Task TestBagCleanupControllerAbandonsTownReturnWhenAttackedAsync()
{
    var previousTownSettle = Environment.GetEnvironmentVariable("ROADHOG_BAG_CLEANUP_TOWN_RETURN_SETTLE_MS");
    try
    {
        Environment.SetEnvironmentVariable("ROADHOG_BAG_CLEANUP_TOWN_RETURN_SETTLE_MS", "5000");

        var logger = new InMemoryRoadhogLogger();
        var keyboard = new RecordingKeyboardInput();
        var gameApi = new FakeGameApi
        {
            TargetEntityId = 0,
            TargetCurrentHp = 0,
            TargetMaxHp = 0,
            TargetName = string.Empty,
            TargetIsTargetingLocalPlayer = false,
            InventoryCapacity = 10,
            InventoryItems = new[]
            {
                new InventoryItemSnapshot(167000450, 10, "green-manastone-a", 1, 0, false, 24, 2),
                new InventoryItemSnapshot(167000451, 11, "green-manastone-b", 1, 1, false, 24, 2),
                new InventoryItemSnapshot(1001, 12, "kept", 1, 2, false, 1, 4),
                new InventoryItemSnapshot(1002, 13, "filler-3", 1, 3, false, 1, 1),
                new InventoryItemSnapshot(1003, 14, "filler-4", 1, 4, false, 1, 1),
                new InventoryItemSnapshot(1004, 15, "filler-5", 1, 5, false, 1, 1),
                new InventoryItemSnapshot(1005, 16, "filler-6", 1, 6, false, 1, 1)
            }
        };
        keyboard.AfterPress = key =>
        {
            if (key != "NumPad7")
            {
                return;
            }

            gameApi.TargetEntityId = 200;
            gameApi.TargetOwnServerObjectId = 2200;
            gameApi.TargetServerObjectId = 100;
            gameApi.TargetName = "attacker";
            gameApi.TargetCurrentHp = 1000;
            gameApi.TargetMaxHp = 1000;
            gameApi.TargetIsTargetingLocalPlayer = true;
        };

        var rules = BagCleanupRuleCatalog.CreateDefaultRules();
        rules.First(rule => rule.Key == BagCleanupRuleCatalog.GreenManastone).Enabled = true;
        var settings = CreateScriptSettings();
        settings.Paths.TownReturnKey = "NumPad7";
        settings.Paths.MaintenancePathName = "cleanup-path";
        settings.Maintenance = new MaintenanceScriptSettings
        {
            BagCleanupEnabled = true,
            BagCleanupThreshold = 5,
            BagCleanupRules = rules,
            BagCleanupSellItemClickX = 100,
            BagCleanupSellItemClickY = 200,
            BagCleanupSellButtonClickX = 300,
            BagCleanupSellButtonClickY = 400
        };

        var pathCalls = new List<string>();
        var controller = new BagCleanupController(
            keyboard,
            new InMemorySharedPathStore(new SharedPathDocument
            {
                Name = "cleanup-path",
                CleanupNpcName = "cleanup-npc",
                Points = new List<SharedPathPoint>
                {
                    new() { X = 1, Y = 0, Z = 0 },
                    new() { X = 2, Y = 0, Z = 0 }
                }
            }),
            (context, pathName, points) =>
            {
                pathCalls.Add(pathName);
                return Task.FromResult(OperationResult.Ok());
            });
        var state = new BagCleanupState();
        var context = CreateContext(settings, gameApi, logger);
        BagCleanupTickResult? last = null;
        for (var i = 0; i < 5; i++)
        {
            last = await controller.TickAfterLootAsync(context, state).ConfigureAwait(false);
            if (last.Status != BagCleanupTickStatus.Running)
            {
                break;
            }
        }

        AssertEqual(BagCleanupTickStatus.Skipped, last?.Status ?? BagCleanupTickStatus.FatalFailure, "interrupted town return should abandon current cleanup");
        AssertEqual("town_return_interrupted_by_attack", last?.Reason ?? string.Empty, "interrupted town return reason");
        AssertSequence(new[] { "NumPad7", "Escape", "Escape" }, keyboard.Keys.ToArray(), "interrupted return should cancel cast with two Esc presses");
        AssertEqual(0, pathCalls.Count, "interrupted return should not follow cleanup path");
        AssertFalse(state.Active, "interrupted return should reset cleanup state");
        AssertFalse(state.LastFailedAt != DateTimeOffset.MinValue, "interrupted return should not enter failure cooldown");
        AssertFalse(!logger.Entries.Any(entry => entry.EventName == "bag_cleanup.return.interrupted_by_attack"), "interrupted return should be logged");
    }
    finally
    {
        Environment.SetEnvironmentVariable("ROADHOG_BAG_CLEANUP_TOWN_RETURN_SETTLE_MS", previousTownSettle);
    }
}

static async Task TestBagCleanupControllerReversesCleanupPathWhenFollowFailsAsync()
{
    var previousTownSettle = Environment.GetEnvironmentVariable("ROADHOG_BAG_CLEANUP_TOWN_RETURN_SETTLE_MS");
    var previousTownMinDistance = Environment.GetEnvironmentVariable("ROADHOG_BAG_CLEANUP_TOWN_RETURN_MIN_DISTANCE");
    try
    {
        Environment.SetEnvironmentVariable("ROADHOG_BAG_CLEANUP_TOWN_RETURN_SETTLE_MS", "0");
        Environment.SetEnvironmentVariable("ROADHOG_BAG_CLEANUP_TOWN_RETURN_MIN_DISTANCE", "1");

        var logger = new InMemoryRoadhogLogger();
        var keyboard = new RecordingKeyboardInput();
        var gameApi = new FakeGameApi
        {
            TargetEntityId = 0,
            TargetCurrentHp = 0,
            TargetMaxHp = 0,
            TargetName = string.Empty,
            TargetIsTargetingLocalPlayer = false,
            InventoryCapacity = 10,
            InventoryItems = new[]
            {
                new InventoryItemSnapshot(167000450, 10, "green-manastone-a", 1, 0, false, 24, 2),
                new InventoryItemSnapshot(167000451, 11, "green-manastone-b", 1, 1, false, 24, 2),
                new InventoryItemSnapshot(1001, 12, "kept", 1, 2, false, 1, 4),
                new InventoryItemSnapshot(1002, 13, "filler-3", 1, 3, false, 1, 1),
                new InventoryItemSnapshot(1003, 14, "filler-4", 1, 4, false, 1, 1),
                new InventoryItemSnapshot(1004, 15, "filler-5", 1, 5, false, 1, 1),
                new InventoryItemSnapshot(1005, 16, "filler-6", 1, 6, false, 1, 1)
            }
        };
        keyboard.AfterPress = key =>
        {
            if (key == "NumPad7")
            {
                gameApi.Player = gameApi.Player with
                {
                    Position = new Vector3Snapshot(100, 0, 0),
                    CapturedAt = DateTimeOffset.Now
                };
            }
        };

        var rules = BagCleanupRuleCatalog.CreateDefaultRules();
        rules.First(rule => rule.Key == BagCleanupRuleCatalog.GreenManastone).Enabled = true;
        var settings = CreateScriptSettings();
        settings.Paths.TownReturnKey = "NumPad7";
        settings.Paths.MaintenancePathName = "cleanup-path";
        settings.Maintenance = new MaintenanceScriptSettings
        {
            BagCleanupEnabled = true,
            BagCleanupThreshold = 5,
            BagCleanupRules = rules,
            BagCleanupSellItemClickX = 100,
            BagCleanupSellItemClickY = 200,
            BagCleanupSellButtonClickX = 300,
            BagCleanupSellButtonClickY = 400
        };

        var pathCalls = new List<string>();
        var controller = new BagCleanupController(
            keyboard,
            new InMemorySharedPathStore(new SharedPathDocument
            {
                Name = "cleanup-path",
                CleanupNpcName = "cleanup-npc",
                Points = new List<SharedPathPoint>
                {
                    new() { X = 1, Y = 0, Z = 0 },
                    new() { X = 2, Y = 0, Z = 0 }
                }
            }),
            (_, pathName, points) =>
            {
                pathCalls.Add(pathName + ":" + points.Count);
                return Task.FromResult(
                    string.Equals(pathName, "cleanup-path", StringComparison.Ordinal)
                        ? OperationResult.Fail("simulated path follow failure")
                        : OperationResult.Ok());
            });
        var state = new BagCleanupState();
        var context = CreateContext(settings, gameApi, logger);
        BagCleanupTickResult? last = null;
        for (var i = 0; i < 12; i++)
        {
            last = await controller.TickAfterLootAsync(context, state).ConfigureAwait(false);
            if (last.Status == BagCleanupTickStatus.RecoverableFailure ||
                last.Status == BagCleanupTickStatus.FatalFailure ||
                last.Status == BagCleanupTickStatus.Completed)
            {
                break;
            }
        }

        AssertEqual(BagCleanupTickStatus.RecoverableFailure, last?.Status ?? BagCleanupTickStatus.FatalFailure, "path follow failure should be recoverable after reverse path");
        AssertEqual("cleanup_path_follow_failed", last?.Reason ?? string.Empty, "path follow failure reason");
        AssertSequence(new[] { "NumPad7" }, keyboard.Keys.ToArray(), "path follow failure should not try npc interaction");
        AssertSequence(new[] { "cleanup-path:2", "cleanup-path 返回:2" }, pathCalls.ToArray(), "path follow failure should reverse the cleanup path");
        AssertFalse(state.LastCompletedAt != DateTimeOffset.MinValue, "path follow failure should not start completion cooldown");
        AssertFalse(state.LastFailedAt == DateTimeOffset.MinValue, "path follow failure should start failure cooldown after reverse path");
        AssertFalse(!logger.Entries.Any(entry => entry.EventName == "bag_cleanup.failure.returning"), "path follow failure should log return decision");
        AssertFalse(
            !logger.Entries.Any(entry =>
                entry.EventName == "bag_cleanup.failed" &&
                entry.Fields.TryGetValue("returnedByReversePath", out var returned) &&
                returned is bool boolValue &&
                boolValue),
            "path follow failure should log that reverse path completed");
    }
    finally
    {
        Environment.SetEnvironmentVariable("ROADHOG_BAG_CLEANUP_TOWN_RETURN_SETTLE_MS", previousTownSettle);
        Environment.SetEnvironmentVariable("ROADHOG_BAG_CLEANUP_TOWN_RETURN_MIN_DISTANCE", previousTownMinDistance);
    }
}

static async Task TestBagCleanupControllerSellsMoreThanThreeItemsInBatchesAsync()
{
    var previousTownSettle = Environment.GetEnvironmentVariable("ROADHOG_BAG_CLEANUP_TOWN_RETURN_SETTLE_MS");
    var previousVerify = Environment.GetEnvironmentVariable("ROADHOG_BAG_CLEANUP_SELL_VERIFY_DELAY_MS");
    var previousPointHover = Environment.GetEnvironmentVariable("ROADHOG_BAG_CLEANUP_POINT_HOVER_MS");
    var previousRegisterHover = Environment.GetEnvironmentVariable("ROADHOG_BAG_SELL_REGISTER_HOVER_MS");
    var previousSellItemEntryDelayMin = Environment.GetEnvironmentVariable("ROADHOG_BAG_CLEANUP_SELL_ITEM_ENTRY_DELAY_MIN_MS");
    var previousSellItemEntryDelayMax = Environment.GetEnvironmentVariable("ROADHOG_BAG_CLEANUP_SELL_ITEM_ENTRY_DELAY_MAX_MS");
    var previousMouseReset = Environment.GetEnvironmentVariable("ROADHOG_DEATH_REVIVE_MOUSE_RESET_COUNT");
    var previousMouseStep = Environment.GetEnvironmentVariable("ROADHOG_DEATH_REVIVE_MOUSE_STEP_DELAY_MS");
    var previousNpcAttempts = Environment.GetEnvironmentVariable("ROADHOG_BAG_CLEANUP_NPC_SELECT_ATTEMPTS");
    var previousNpcSelectDelay = Environment.GetEnvironmentVariable("ROADHOG_BAG_CLEANUP_NPC_SELECT_DELAY_MS");
    var previousTownMinDistance = Environment.GetEnvironmentVariable("ROADHOG_BAG_CLEANUP_TOWN_RETURN_MIN_DISTANCE");
    try
    {
        Environment.SetEnvironmentVariable("ROADHOG_BAG_CLEANUP_TOWN_RETURN_SETTLE_MS", "0");
        Environment.SetEnvironmentVariable("ROADHOG_BAG_CLEANUP_SELL_VERIFY_DELAY_MS", "0");
        Environment.SetEnvironmentVariable("ROADHOG_BAG_CLEANUP_POINT_HOVER_MS", "0");
        Environment.SetEnvironmentVariable("ROADHOG_BAG_SELL_REGISTER_HOVER_MS", "0");
        Environment.SetEnvironmentVariable("ROADHOG_BAG_CLEANUP_SELL_ITEM_ENTRY_DELAY_MIN_MS", "0");
        Environment.SetEnvironmentVariable("ROADHOG_BAG_CLEANUP_SELL_ITEM_ENTRY_DELAY_MAX_MS", "0");
        Environment.SetEnvironmentVariable("ROADHOG_DEATH_REVIVE_MOUSE_RESET_COUNT", "1");
        Environment.SetEnvironmentVariable("ROADHOG_DEATH_REVIVE_MOUSE_STEP_DELAY_MS", "0");
        Environment.SetEnvironmentVariable("ROADHOG_BAG_CLEANUP_NPC_SELECT_ATTEMPTS", "1");
        Environment.SetEnvironmentVariable("ROADHOG_BAG_CLEANUP_NPC_SELECT_DELAY_MS", "0");
        Environment.SetEnvironmentVariable("ROADHOG_BAG_CLEANUP_TOWN_RETURN_MIN_DISTANCE", "1");

        var logger = new InMemoryRoadhogLogger();
        var keyboard = new RecordingKeyboardInput();
        var cleanupNpcName = "cleanup-vendor";
        var sellItems = Enumerable.Range(0, 35)
            .Select(index => new InventoryItemSnapshot(
                167000450u + (uint)index,
                1000UL + (ulong)index,
                "green-manastone-" + index.ToString(System.Globalization.CultureInfo.InvariantCulture),
                1,
                index,
                false,
                24,
                2))
            .ToArray();
        var gameApi = new FakeGameApi
        {
            TargetEntityId = 0,
            TargetCurrentHp = 0,
            TargetMaxHp = 0,
            TargetName = string.Empty,
            TargetIsTargetingLocalPlayer = false,
            InventoryWindow = CreateInventoryWindow(false, 0.0, 0.0),
            InventoryMoney = 1000,
            InventoryCapacity = 60,
            InventoryItems = sellItems
        };
        keyboard.AfterPress = key =>
        {
            if (key == "NumPad7")
            {
                gameApi.Player = gameApi.Player with
                {
                    Position = new Vector3Snapshot(100, 0, 0),
                    CapturedAt = DateTimeOffset.Now
                };
            }
            else if (key == "F8")
            {
                gameApi.TargetEntityId = 77;
                gameApi.TargetServerObjectId = 7700;
                gameApi.TargetName = cleanupNpcName;
                gameApi.TargetCurrentHp = 0;
                gameApi.TargetMaxHp = 0;
            }
            else if (key == "I")
            {
                gameApi.InventoryWindow = CreateInventoryWindow(!gameApi.InventoryWindow.IsOpen, 0.0, 0.0);
            }
        };

        var saleBatches = new Queue<ulong[]>();
        for (var offset = 0; offset < sellItems.Length; offset += BagCleanupSeller.NonEquipmentSellRegistrationItemsPerBatch)
        {
            saleBatches.Enqueue(sellItems
                .Skip(offset)
                .Take(BagCleanupSeller.NonEquipmentSellRegistrationItemsPerBatch)
                .Select(item => item.InstanceId)
                .ToArray());
        }
        var leftClicks = 0;
        var sellClickCount = 0;
        keyboard.AfterMouseUp = button =>
        {
            if (button != RoadhogMouseButton.Left)
            {
                return;
            }

            leftClicks++;
            if (leftClicks <= 1 || saleBatches.Count == 0)
            {
                return;
            }

            sellClickCount++;
            var soldIds = saleBatches.Dequeue().ToHashSet();
            gameApi.InventoryItems = gameApi.InventoryItems
                .Where(item => !soldIds.Contains(item.InstanceId))
                .ToArray();
            gameApi.InventoryMoney += (ulong)soldIds.Count * 10UL;
        };

        var rules = BagCleanupRuleCatalog.CreateDefaultRules();
        rules.First(rule => rule.Key == BagCleanupRuleCatalog.GreenManastone).Enabled = true;
        var settings = CreateScriptSettings();
        settings.Paths.TownReturnKey = "NumPad7";
        settings.Paths.MaintenancePathName = "cleanup-path";
        settings.Maintenance = new MaintenanceScriptSettings
        {
            BagCleanupEnabled = true,
            BagCleanupThreshold = 30,
            BagCleanupRules = rules,
            BagCleanupSellItemClickX = 100,
            BagCleanupSellItemClickY = 200,
            BagCleanupSellButtonClickX = 300,
            BagCleanupSellButtonClickY = 400
        };
        var pathStore = new InMemorySharedPathStore(new SharedPathDocument
        {
            Name = "cleanup-path",
            CleanupNpcName = cleanupNpcName,
            Points = new List<SharedPathPoint>
            {
                new() { X = 1, Y = 0, Z = 0 },
                new() { X = 2, Y = 0, Z = 0 }
            }
        });
        var pathCalls = new List<string>();
        var controller = new BagCleanupController(
            keyboard,
            pathStore,
            (context, pathName, points) =>
            {
                pathCalls.Add(pathName + ":" + points.Count);
                return Task.FromResult(OperationResult.Ok());
            });
        var state = new BagCleanupState();
        var context = CreateContext(settings, gameApi, logger);
        BagCleanupTickResult? last = null;
        for (var i = 0; i < 120; i++)
        {
            last = await controller.TickAfterLootAsync(context, state).ConfigureAwait(false);
            if (last.Status == BagCleanupTickStatus.Completed ||
                last.Status == BagCleanupTickStatus.FatalFailure ||
                last.Status == BagCleanupTickStatus.RecoverableFailure)
            {
                break;
            }
        }

        AssertEqual(BagCleanupTickStatus.Completed, last?.Status ?? BagCleanupTickStatus.FatalFailure, "batched cleanup should complete");
        AssertSequence(new[] { "NumPad7", "F8", "C" }, keyboard.Keys.Take(3).ToArray(), "batched cleanup should return, select npc, and interact");
        AssertEqual(8, keyboard.Keys.Count(key => key == "I"), "batched cleanup should open, close each batch, and reopen between batches");
        AssertEqual(4, sellClickCount, "batched cleanup should click sell for every batch");
        AssertEqual(1350UL, gameApi.InventoryMoney, "batched cleanup should increase money for every batch");
        AssertFalse(gameApi.InventoryItems.Any(), "batched cleanup should sell every matching item");
        var candidateLogs = logger.Entries
            .Where(entry => entry.EventName == "bag_cleanup.sell.candidates")
            .ToArray();
        AssertSequence(
            new[] { 9, 9, 9, 8 },
            candidateLogs.Select(entry => Convert.ToInt32(entry.Fields["batchCount"])).ToArray(),
            "non-equipment cleanup should register at most nine items per batch");
        AssertSequence(
            new[] { "non_equipment", "non_equipment", "non_equipment", "non_equipment" },
            candidateLogs.Select(entry => Convert.ToString(entry.Fields["batchKind"]) ?? string.Empty).ToArray(),
            "manastone cleanup batches should be non-equipment");
        AssertEqual(
            4,
            logger.Entries.Count(entry => entry.EventName == "bag_cleanup.inventory.close.requested"),
            "batched cleanup should request inventory close before each sell click");
        AssertEqual(
            4,
            logger.Entries.Count(entry => entry.EventName == "bag_cleanup.inventory.open.requested"),
            "batched cleanup should blindly open inventory for the first and follow-up batches");
        AssertEqual(
            4,
            logger.Entries.Count(entry => entry.EventName == "bag_cleanup.verify.ok"),
            "batched cleanup should verify both sell batches");
    }
    finally
    {
        Environment.SetEnvironmentVariable("ROADHOG_BAG_CLEANUP_TOWN_RETURN_SETTLE_MS", previousTownSettle);
        Environment.SetEnvironmentVariable("ROADHOG_BAG_CLEANUP_SELL_VERIFY_DELAY_MS", previousVerify);
        Environment.SetEnvironmentVariable("ROADHOG_BAG_CLEANUP_POINT_HOVER_MS", previousPointHover);
        Environment.SetEnvironmentVariable("ROADHOG_BAG_SELL_REGISTER_HOVER_MS", previousRegisterHover);
        Environment.SetEnvironmentVariable("ROADHOG_BAG_CLEANUP_SELL_ITEM_ENTRY_DELAY_MIN_MS", previousSellItemEntryDelayMin);
        Environment.SetEnvironmentVariable("ROADHOG_BAG_CLEANUP_SELL_ITEM_ENTRY_DELAY_MAX_MS", previousSellItemEntryDelayMax);
        Environment.SetEnvironmentVariable("ROADHOG_DEATH_REVIVE_MOUSE_RESET_COUNT", previousMouseReset);
        Environment.SetEnvironmentVariable("ROADHOG_DEATH_REVIVE_MOUSE_STEP_DELAY_MS", previousMouseStep);
        Environment.SetEnvironmentVariable("ROADHOG_BAG_CLEANUP_NPC_SELECT_ATTEMPTS", previousNpcAttempts);
        Environment.SetEnvironmentVariable("ROADHOG_BAG_CLEANUP_NPC_SELECT_DELAY_MS", previousNpcSelectDelay);
        Environment.SetEnvironmentVariable("ROADHOG_BAG_CLEANUP_TOWN_RETURN_MIN_DISTANCE", previousTownMinDistance);
    }
}

static async Task TestBagCleanupControllerSellsNonEquipmentBeforeEquipmentBatchesAsync()
{
    var previousTownSettle = Environment.GetEnvironmentVariable("ROADHOG_BAG_CLEANUP_TOWN_RETURN_SETTLE_MS");
    var previousVerify = Environment.GetEnvironmentVariable("ROADHOG_BAG_CLEANUP_SELL_VERIFY_DELAY_MS");
    var previousPointHover = Environment.GetEnvironmentVariable("ROADHOG_BAG_CLEANUP_POINT_HOVER_MS");
    var previousRegisterHover = Environment.GetEnvironmentVariable("ROADHOG_BAG_SELL_REGISTER_HOVER_MS");
    var previousSellItemEntryDelayMin = Environment.GetEnvironmentVariable("ROADHOG_BAG_CLEANUP_SELL_ITEM_ENTRY_DELAY_MIN_MS");
    var previousSellItemEntryDelayMax = Environment.GetEnvironmentVariable("ROADHOG_BAG_CLEANUP_SELL_ITEM_ENTRY_DELAY_MAX_MS");
    var previousMouseReset = Environment.GetEnvironmentVariable("ROADHOG_DEATH_REVIVE_MOUSE_RESET_COUNT");
    var previousMouseStep = Environment.GetEnvironmentVariable("ROADHOG_DEATH_REVIVE_MOUSE_STEP_DELAY_MS");
    var previousNpcAttempts = Environment.GetEnvironmentVariable("ROADHOG_BAG_CLEANUP_NPC_SELECT_ATTEMPTS");
    var previousNpcSelectDelay = Environment.GetEnvironmentVariable("ROADHOG_BAG_CLEANUP_NPC_SELECT_DELAY_MS");
    var previousTownMinDistance = Environment.GetEnvironmentVariable("ROADHOG_BAG_CLEANUP_TOWN_RETURN_MIN_DISTANCE");
    try
    {
        Environment.SetEnvironmentVariable("ROADHOG_BAG_CLEANUP_TOWN_RETURN_SETTLE_MS", "0");
        Environment.SetEnvironmentVariable("ROADHOG_BAG_CLEANUP_SELL_VERIFY_DELAY_MS", "0");
        Environment.SetEnvironmentVariable("ROADHOG_BAG_CLEANUP_POINT_HOVER_MS", "0");
        Environment.SetEnvironmentVariable("ROADHOG_BAG_SELL_REGISTER_HOVER_MS", "0");
        Environment.SetEnvironmentVariable("ROADHOG_BAG_CLEANUP_SELL_ITEM_ENTRY_DELAY_MIN_MS", "0");
        Environment.SetEnvironmentVariable("ROADHOG_BAG_CLEANUP_SELL_ITEM_ENTRY_DELAY_MAX_MS", "0");
        Environment.SetEnvironmentVariable("ROADHOG_DEATH_REVIVE_MOUSE_RESET_COUNT", "1");
        Environment.SetEnvironmentVariable("ROADHOG_DEATH_REVIVE_MOUSE_STEP_DELAY_MS", "0");
        Environment.SetEnvironmentVariable("ROADHOG_BAG_CLEANUP_NPC_SELECT_ATTEMPTS", "1");
        Environment.SetEnvironmentVariable("ROADHOG_BAG_CLEANUP_NPC_SELECT_DELAY_MS", "0");
        Environment.SetEnvironmentVariable("ROADHOG_BAG_CLEANUP_TOWN_RETURN_MIN_DISTANCE", "1");

        var logger = new InMemoryRoadhogLogger();
        var keyboard = new RecordingKeyboardInput();
        var cleanupNpcName = "cleanup-vendor";
        var nonEquipmentItems = Enumerable.Range(0, 12)
            .Select(index => new InventoryItemSnapshot(
                167000450u + (uint)index,
                2000UL + (ulong)index,
                "non-equipment-" + index.ToString(System.Globalization.CultureInfo.InvariantCulture),
                1,
                index * 2,
                false,
                24,
                2))
            .ToArray();
        var equipmentItems = Enumerable.Range(0, 7)
            .Select(index => new InventoryItemSnapshot(
                100100300u + (uint)index,
                3000UL + (ulong)index,
                "equipment-" + index.ToString(System.Globalization.CultureInfo.InvariantCulture),
                1,
                (index * 2) + 1,
                false,
                2,
                2))
            .ToArray();
        var sellItems = nonEquipmentItems
            .Concat(equipmentItems)
            .OrderBy(item => item.Slot)
            .ToArray();
        var expectedNonEquipmentBatchCounts = BuildExpectedBatchCounts(
            nonEquipmentItems.Length,
            BagCleanupSeller.NonEquipmentSellRegistrationItemsPerBatch);
        var expectedEquipmentBatchCounts = BuildExpectedBatchCounts(
            equipmentItems.Length,
            BagCleanupSeller.EquipmentSellRegistrationItemsPerBatch);
        var expectedBatchCounts = expectedNonEquipmentBatchCounts
            .Concat(expectedEquipmentBatchCounts)
            .ToArray();
        var expectedBatchKinds = Enumerable
            .Repeat("non_equipment", expectedNonEquipmentBatchCounts.Length)
            .Concat(Enumerable.Repeat("equipment", expectedEquipmentBatchCounts.Length))
            .ToArray();
        var expectedBatchMaxCounts = Enumerable
            .Repeat(BagCleanupSeller.NonEquipmentSellRegistrationItemsPerBatch, expectedNonEquipmentBatchCounts.Length)
            .Concat(Enumerable.Repeat(BagCleanupSeller.EquipmentSellRegistrationItemsPerBatch, expectedEquipmentBatchCounts.Length))
            .ToArray();
        var gameApi = new FakeGameApi
        {
            TargetEntityId = 0,
            TargetCurrentHp = 0,
            TargetMaxHp = 0,
            TargetName = string.Empty,
            TargetIsTargetingLocalPlayer = false,
            InventoryWindow = CreateInventoryWindow(false, 0.0, 0.0),
            InventoryMoney = 1000,
            InventoryCapacity = 40,
            InventoryItems = sellItems
        };
        keyboard.AfterPress = key =>
        {
            if (key == "NumPad7")
            {
                gameApi.Player = gameApi.Player with
                {
                    Position = new Vector3Snapshot(100, 0, 0),
                    CapturedAt = DateTimeOffset.Now
                };
            }
            else if (key == "F8")
            {
                gameApi.TargetEntityId = 77;
                gameApi.TargetServerObjectId = 7700;
                gameApi.TargetName = cleanupNpcName;
                gameApi.TargetCurrentHp = 0;
                gameApi.TargetMaxHp = 0;
            }
            else if (key == "I")
            {
                gameApi.InventoryWindow = CreateInventoryWindow(!gameApi.InventoryWindow.IsOpen, 0.0, 0.0);
            }
        };

        var saleBatches = new Queue<ulong[]>();
        for (var offset = 0; offset < nonEquipmentItems.Length; offset += BagCleanupSeller.NonEquipmentSellRegistrationItemsPerBatch)
        {
            saleBatches.Enqueue(nonEquipmentItems
                .Skip(offset)
                .Take(BagCleanupSeller.NonEquipmentSellRegistrationItemsPerBatch)
                .Select(item => item.InstanceId)
                .ToArray());
        }

        for (var offset = 0; offset < equipmentItems.Length; offset += BagCleanupSeller.EquipmentSellRegistrationItemsPerBatch)
        {
            saleBatches.Enqueue(equipmentItems
                .Skip(offset)
                .Take(BagCleanupSeller.EquipmentSellRegistrationItemsPerBatch)
                .Select(item => item.InstanceId)
                .ToArray());
        }

        var leftClicks = 0;
        var sellClickCount = 0;
        keyboard.AfterMouseUp = button =>
        {
            if (button != RoadhogMouseButton.Left)
            {
                return;
            }

            leftClicks++;
            if (leftClicks <= 1 || saleBatches.Count == 0)
            {
                return;
            }

            sellClickCount++;
            var soldIds = saleBatches.Dequeue().ToHashSet();
            gameApi.InventoryItems = gameApi.InventoryItems
                .Where(item => !soldIds.Contains(item.InstanceId))
                .ToArray();
            gameApi.InventoryMoney += (ulong)soldIds.Count * 10UL;
        };

        var rules = BagCleanupRuleCatalog.CreateDefaultRules();
        rules.First(rule => rule.Key == BagCleanupRuleCatalog.GreenManastone).Enabled = true;
        rules.First(rule => rule.Key == BagCleanupRuleCatalog.GreenEquipment).Enabled = true;
        var settings = CreateScriptSettings();
        settings.Paths.TownReturnKey = "NumPad7";
        settings.Paths.MaintenancePathName = "cleanup-path";
        settings.Maintenance = new MaintenanceScriptSettings
        {
            BagCleanupEnabled = true,
            BagCleanupThreshold = 30,
            BagCleanupRules = rules,
            BagCleanupSellItemClickX = 100,
            BagCleanupSellItemClickY = 200,
            BagCleanupSellButtonClickX = 300,
            BagCleanupSellButtonClickY = 400
        };
        var controller = new BagCleanupController(
            keyboard,
            new InMemorySharedPathStore(new SharedPathDocument
            {
                Name = "cleanup-path",
                CleanupNpcName = cleanupNpcName,
                Points = new List<SharedPathPoint>
                {
                    new() { X = 1, Y = 0, Z = 0 },
                    new() { X = 2, Y = 0, Z = 0 }
                }
            }),
            (_, _, _) => Task.FromResult(OperationResult.Ok()));
        var state = new BagCleanupState();
        var context = CreateContext(settings, gameApi, logger);
        BagCleanupTickResult? last = null;
        for (var i = 0; i < 160; i++)
        {
            last = await controller.TickAfterLootAsync(context, state).ConfigureAwait(false);
            if (last.Status == BagCleanupTickStatus.Completed ||
                last.Status == BagCleanupTickStatus.FatalFailure ||
                last.Status == BagCleanupTickStatus.RecoverableFailure)
            {
                break;
            }
        }

        AssertEqual(BagCleanupTickStatus.Completed, last?.Status ?? BagCleanupTickStatus.FatalFailure, "mixed cleanup should complete");
        AssertEqual(expectedBatchCounts.Length, sellClickCount, "mixed cleanup should sell expected batches");
        AssertEqual(1190UL, gameApi.InventoryMoney, "mixed cleanup should increase money for every item");
        AssertFalse(gameApi.InventoryItems.Any(), "mixed cleanup should sell every matching item");
        var candidateLogs = logger.Entries
            .Where(entry => entry.EventName == "bag_cleanup.sell.candidates")
            .ToArray();
        AssertSequence(
            expectedBatchCounts,
            candidateLogs.Select(entry => Convert.ToInt32(entry.Fields["batchCount"])).ToArray(),
            "mixed cleanup batch sizes");
        AssertSequence(
            expectedBatchKinds,
            candidateLogs.Select(entry => Convert.ToString(entry.Fields["batchKind"]) ?? string.Empty).ToArray(),
            "mixed cleanup batch kinds");
        AssertSequence(
            expectedBatchMaxCounts,
            candidateLogs.Select(entry => Convert.ToInt32(entry.Fields["maxBatchCount"])).ToArray(),
            "mixed cleanup batch max sizes");
        var registeredNames = logger.Entries
            .Where(entry => entry.EventName == "bag_cleanup.sell.register.item")
            .Select(entry => Convert.ToString(entry.Fields["name"]) ?? string.Empty)
            .ToArray();
        AssertFalse(registeredNames.Take(12).Any(name => name.StartsWith("equipment-", StringComparison.Ordinal)), "non-equipment should register before equipment");
        AssertFalse(registeredNames.Skip(12).Any(name => name.StartsWith("non-equipment-", StringComparison.Ordinal)), "equipment should register after non-equipment");
    }
    finally
    {
        Environment.SetEnvironmentVariable("ROADHOG_BAG_CLEANUP_TOWN_RETURN_SETTLE_MS", previousTownSettle);
        Environment.SetEnvironmentVariable("ROADHOG_BAG_CLEANUP_SELL_VERIFY_DELAY_MS", previousVerify);
        Environment.SetEnvironmentVariable("ROADHOG_BAG_CLEANUP_POINT_HOVER_MS", previousPointHover);
        Environment.SetEnvironmentVariable("ROADHOG_BAG_SELL_REGISTER_HOVER_MS", previousRegisterHover);
        Environment.SetEnvironmentVariable("ROADHOG_BAG_CLEANUP_SELL_ITEM_ENTRY_DELAY_MIN_MS", previousSellItemEntryDelayMin);
        Environment.SetEnvironmentVariable("ROADHOG_BAG_CLEANUP_SELL_ITEM_ENTRY_DELAY_MAX_MS", previousSellItemEntryDelayMax);
        Environment.SetEnvironmentVariable("ROADHOG_DEATH_REVIVE_MOUSE_RESET_COUNT", previousMouseReset);
        Environment.SetEnvironmentVariable("ROADHOG_DEATH_REVIVE_MOUSE_STEP_DELAY_MS", previousMouseStep);
        Environment.SetEnvironmentVariable("ROADHOG_BAG_CLEANUP_NPC_SELECT_ATTEMPTS", previousNpcAttempts);
        Environment.SetEnvironmentVariable("ROADHOG_BAG_CLEANUP_NPC_SELECT_DELAY_MS", previousNpcSelectDelay);
        Environment.SetEnvironmentVariable("ROADHOG_BAG_CLEANUP_TOWN_RETURN_MIN_DISTANCE", previousTownMinDistance);
    }
}

static int[] BuildExpectedBatchCounts(int itemCount, int batchSize)
{
    var counts = new List<int>();
    for (var offset = 0; offset < itemCount; offset += batchSize)
    {
        counts.Add(Math.Min(batchSize, itemCount - offset));
    }

    return counts.ToArray();
}

static async Task TestBagCleanupControllerReturnsWhenNpcNotFoundAsync()
{
    var previousTownSettle = Environment.GetEnvironmentVariable("ROADHOG_BAG_CLEANUP_TOWN_RETURN_SETTLE_MS");
    var previousNpcAttempts = Environment.GetEnvironmentVariable("ROADHOG_BAG_CLEANUP_NPC_SELECT_ATTEMPTS");
    var previousNpcSelectDelay = Environment.GetEnvironmentVariable("ROADHOG_BAG_CLEANUP_NPC_SELECT_DELAY_MS");
    var previousTownMinDistance = Environment.GetEnvironmentVariable("ROADHOG_BAG_CLEANUP_TOWN_RETURN_MIN_DISTANCE");
    var previousFailureCooldown = Environment.GetEnvironmentVariable("ROADHOG_BAG_CLEANUP_FAILURE_COOLDOWN_MS");
    try
    {
        Environment.SetEnvironmentVariable("ROADHOG_BAG_CLEANUP_TOWN_RETURN_SETTLE_MS", "0");
        Environment.SetEnvironmentVariable("ROADHOG_BAG_CLEANUP_NPC_SELECT_ATTEMPTS", "2");
        Environment.SetEnvironmentVariable("ROADHOG_BAG_CLEANUP_NPC_SELECT_DELAY_MS", "0");
        Environment.SetEnvironmentVariable("ROADHOG_BAG_CLEANUP_TOWN_RETURN_MIN_DISTANCE", "1");
        Environment.SetEnvironmentVariable("ROADHOG_BAG_CLEANUP_FAILURE_COOLDOWN_MS", "1500000");

        var logger = new InMemoryRoadhogLogger();
        var keyboard = new RecordingKeyboardInput();
        var cleanupNpcName = "cleanup-vendor";
        var gameApi = new FakeGameApi
        {
            TargetEntityId = 0,
            TargetCurrentHp = 0,
            TargetMaxHp = 0,
            TargetName = string.Empty,
            TargetIsTargetingLocalPlayer = false,
            InventoryCapacity = 10,
            InventoryItems = new[]
            {
                new InventoryItemSnapshot(167000450, 10, "green-manastone-a", 1, 0, false, 24, 2),
                new InventoryItemSnapshot(167000451, 11, "green-manastone-b", 1, 1, false, 24, 2),
                new InventoryItemSnapshot(1001, 12, "kept", 1, 2, false, 1, 4),
                new InventoryItemSnapshot(1002, 13, "filler-3", 1, 3, false, 1, 1),
                new InventoryItemSnapshot(1003, 14, "filler-4", 1, 4, false, 1, 1),
                new InventoryItemSnapshot(1004, 15, "filler-5", 1, 5, false, 1, 1),
                new InventoryItemSnapshot(1005, 16, "filler-6", 1, 6, false, 1, 1)
            }
        };
        keyboard.AfterPress = key =>
        {
            if (key == "NumPad7")
            {
                gameApi.Player = gameApi.Player with
                {
                    Position = new Vector3Snapshot(100, 0, 0),
                    CapturedAt = DateTimeOffset.Now
                };
            }
            else if (key == "F8")
            {
                gameApi.TargetEntityId = 77;
                gameApi.TargetServerObjectId = 7700;
                gameApi.TargetName = "wrong-target";
                gameApi.TargetCurrentHp = 0;
                gameApi.TargetMaxHp = 0;
            }
        };

        var rules = BagCleanupRuleCatalog.CreateDefaultRules();
        rules.First(rule => rule.Key == BagCleanupRuleCatalog.GreenManastone).Enabled = true;
        var settings = CreateScriptSettings();
        settings.Paths.TownReturnKey = "NumPad7";
        settings.Paths.MaintenancePathName = "cleanup-path";
        settings.Maintenance = new MaintenanceScriptSettings
        {
            BagCleanupEnabled = true,
            BagCleanupThreshold = 5,
            BagCleanupRules = rules,
            BagCleanupSellItemClickX = 100,
            BagCleanupSellItemClickY = 200,
            BagCleanupSellButtonClickX = 300,
            BagCleanupSellButtonClickY = 400
        };
        var pathStore = new InMemorySharedPathStore(new SharedPathDocument
        {
            Name = "cleanup-path",
            CleanupNpcName = cleanupNpcName,
            Points = new List<SharedPathPoint>
            {
                new() { X = 1, Y = 0, Z = 0 },
                new() { X = 2, Y = 0, Z = 0 }
            }
        });
        var pathCalls = new List<string>();
        var controller = new BagCleanupController(
            keyboard,
            pathStore,
            (context, pathName, points) =>
            {
                pathCalls.Add(pathName + ":" + points.Count);
                return Task.FromResult(OperationResult.Ok());
            });
        var state = new BagCleanupState();
        var context = CreateContext(settings, gameApi, logger);
        BagCleanupTickResult? last = null;
        for (var i = 0; i < 30; i++)
        {
            last = await controller.TickAfterLootAsync(context, state).ConfigureAwait(false);
            if (last.Status == BagCleanupTickStatus.RecoverableFailure ||
                last.Status == BagCleanupTickStatus.FatalFailure ||
                last.Status == BagCleanupTickStatus.Completed)
            {
                break;
            }
        }

        AssertEqual(BagCleanupTickStatus.RecoverableFailure, last?.Status ?? BagCleanupTickStatus.FatalFailure, "npc miss should be recoverable after return path");
        AssertEqual("cleanup_npc_select_failed", last?.Reason ?? string.Empty, "npc miss failure reason");
        AssertSequence(new[] { "NumPad7", "F8", "F8" }, keyboard.Keys.ToArray(), "npc miss key sequence");
        AssertSequence(new[] { "cleanup-path:2", "cleanup-path 返回:2" }, pathCalls.ToArray(), "npc miss should follow cleanup path then reverse it");
        AssertFalse(state.LastCompletedAt != DateTimeOffset.MinValue, "npc miss should not start completion cooldown");
        AssertFalse(state.LastFailedAt == DateTimeOffset.MinValue, "npc miss should start failure cooldown after reverse path");
        AssertFalse(!logger.Entries.Any(entry => entry.EventName == "bag_cleanup.npc.select.failed_returning"), "npc miss should log return decision");
        AssertFalse(
            !logger.Entries.Any(entry =>
                entry.EventName == "bag_cleanup.failed" &&
                entry.Fields.TryGetValue("returnedByReversePath", out var returned) &&
                returned is bool boolValue &&
                boolValue),
            "npc miss failure should log that reverse path completed");

        var skipped = await controller.TickAfterLootAsync(context, state).ConfigureAwait(false);
        AssertEqual(BagCleanupTickStatus.Skipped, skipped.Status, "npc miss should skip during failure cooldown");
        AssertEqual("cleanup_failure_cooldown", skipped.Reason, "npc miss failure cooldown reason");
    }
    finally
    {
        Environment.SetEnvironmentVariable("ROADHOG_BAG_CLEANUP_TOWN_RETURN_SETTLE_MS", previousTownSettle);
        Environment.SetEnvironmentVariable("ROADHOG_BAG_CLEANUP_NPC_SELECT_ATTEMPTS", previousNpcAttempts);
        Environment.SetEnvironmentVariable("ROADHOG_BAG_CLEANUP_NPC_SELECT_DELAY_MS", previousNpcSelectDelay);
        Environment.SetEnvironmentVariable("ROADHOG_BAG_CLEANUP_TOWN_RETURN_MIN_DISTANCE", previousTownMinDistance);
        Environment.SetEnvironmentVariable("ROADHOG_BAG_CLEANUP_FAILURE_COOLDOWN_MS", previousFailureCooldown);
    }
}

static async Task TestBagCleanupControllerSkipsWithinCooldownAsync()
{
    var logger = new InMemoryRoadhogLogger();
    var keyboard = new RecordingKeyboardInput();
    var rules = BagCleanupRuleCatalog.CreateDefaultRules();
    rules.First(rule => rule.Key == BagCleanupRuleCatalog.GreenManastone).Enabled = true;
    var settings = CreateScriptSettings();
    settings.Paths.TownReturnKey = "NumPad7";
    settings.Paths.MaintenancePathName = "cleanup-path";
    settings.Maintenance = new MaintenanceScriptSettings
    {
        BagCleanupEnabled = true,
        BagCleanupThreshold = 5,
        BagCleanupRules = rules,
        BagCleanupSellItemClickX = 100,
        BagCleanupSellItemClickY = 200,
        BagCleanupSellButtonClickX = 300,
        BagCleanupSellButtonClickY = 400
    };
    var gameApi = new FakeGameApi
    {
        InventoryMoney = 1000,
        InventoryItems = new[]
        {
            new InventoryItemSnapshot(167000450, 10, "green-manastone-a", 1, 0, false, 24, 2),
            new InventoryItemSnapshot(167000451, 11, "green-manastone-b", 1, 1, false, 24, 2),
            new InventoryItemSnapshot(1001, 12, "kept", 1, 2, false, 1, 4),
            new InventoryItemSnapshot(1002, 13, "filler-3", 1, 3, false, 1, 1),
            new InventoryItemSnapshot(1003, 14, "filler-4", 1, 4, false, 1, 1),
            new InventoryItemSnapshot(1004, 15, "filler-5", 1, 5, false, 1, 1),
            new InventoryItemSnapshot(1005, 16, "filler-6", 1, 6, false, 1, 1)
        }
    };
    var state = new BagCleanupState();
    state.MarkCompleted(DateTimeOffset.Now);
    var controller = new BagCleanupController(
        keyboard,
        new InMemorySharedPathStore(),
        (_, _, _) => Task.FromResult(OperationResult.Ok()));

    var result = await controller
        .TickAfterLootAsync(CreateContext(settings, gameApi, logger), state)
        .ConfigureAwait(false);

    AssertEqual(BagCleanupTickStatus.Skipped, result.Status, "cleanup should skip during 25 minute cooldown");
    AssertEqual("cleanup_cooldown", result.Reason, "cleanup cooldown reason");
    AssertEqual(0, keyboard.Keys.Count, "cleanup cooldown should not press keys");
    AssertFalse(!logger.Entries.Any(entry => entry.EventName == "bag_cleanup.skip.cooldown"), "cooldown skip should be logged");
}

static async Task TestBagCleanupControllerFailureCoolsDownAsync()
{
    var previousTownSettle = Environment.GetEnvironmentVariable("ROADHOG_BAG_CLEANUP_TOWN_RETURN_SETTLE_MS");
    var previousVerify = Environment.GetEnvironmentVariable("ROADHOG_BAG_CLEANUP_SELL_VERIFY_DELAY_MS");
    var previousPointHover = Environment.GetEnvironmentVariable("ROADHOG_BAG_CLEANUP_POINT_HOVER_MS");
    var previousRegisterHover = Environment.GetEnvironmentVariable("ROADHOG_BAG_SELL_REGISTER_HOVER_MS");
    var previousSellItemEntryDelayMin = Environment.GetEnvironmentVariable("ROADHOG_BAG_CLEANUP_SELL_ITEM_ENTRY_DELAY_MIN_MS");
    var previousSellItemEntryDelayMax = Environment.GetEnvironmentVariable("ROADHOG_BAG_CLEANUP_SELL_ITEM_ENTRY_DELAY_MAX_MS");
    var previousMouseReset = Environment.GetEnvironmentVariable("ROADHOG_DEATH_REVIVE_MOUSE_RESET_COUNT");
    var previousMouseStep = Environment.GetEnvironmentVariable("ROADHOG_DEATH_REVIVE_MOUSE_STEP_DELAY_MS");
    var previousNpcAttempts = Environment.GetEnvironmentVariable("ROADHOG_BAG_CLEANUP_NPC_SELECT_ATTEMPTS");
    var previousNpcSelectDelay = Environment.GetEnvironmentVariable("ROADHOG_BAG_CLEANUP_NPC_SELECT_DELAY_MS");
    var previousTownMinDistance = Environment.GetEnvironmentVariable("ROADHOG_BAG_CLEANUP_TOWN_RETURN_MIN_DISTANCE");
    var previousFailureCooldown = Environment.GetEnvironmentVariable("ROADHOG_BAG_CLEANUP_FAILURE_COOLDOWN_MS");
    try
    {
        Environment.SetEnvironmentVariable("ROADHOG_BAG_CLEANUP_TOWN_RETURN_SETTLE_MS", "0");
        Environment.SetEnvironmentVariable("ROADHOG_BAG_CLEANUP_SELL_VERIFY_DELAY_MS", "0");
        Environment.SetEnvironmentVariable("ROADHOG_BAG_CLEANUP_POINT_HOVER_MS", "0");
        Environment.SetEnvironmentVariable("ROADHOG_BAG_SELL_REGISTER_HOVER_MS", "0");
        Environment.SetEnvironmentVariable("ROADHOG_BAG_CLEANUP_SELL_ITEM_ENTRY_DELAY_MIN_MS", "0");
        Environment.SetEnvironmentVariable("ROADHOG_BAG_CLEANUP_SELL_ITEM_ENTRY_DELAY_MAX_MS", "0");
        Environment.SetEnvironmentVariable("ROADHOG_DEATH_REVIVE_MOUSE_RESET_COUNT", "1");
        Environment.SetEnvironmentVariable("ROADHOG_DEATH_REVIVE_MOUSE_STEP_DELAY_MS", "0");
        Environment.SetEnvironmentVariable("ROADHOG_BAG_CLEANUP_NPC_SELECT_ATTEMPTS", "2");
        Environment.SetEnvironmentVariable("ROADHOG_BAG_CLEANUP_NPC_SELECT_DELAY_MS", "0");
        Environment.SetEnvironmentVariable("ROADHOG_BAG_CLEANUP_TOWN_RETURN_MIN_DISTANCE", "1");
        Environment.SetEnvironmentVariable("ROADHOG_BAG_CLEANUP_FAILURE_COOLDOWN_MS", "1500000");

        var logger = new InMemoryRoadhogLogger();
        var keyboard = new RecordingKeyboardInput();
        var cleanupNpcName = "清包商人";
        var gameApi = new FakeGameApi
        {
            TargetEntityId = 0,
            TargetCurrentHp = 0,
            TargetMaxHp = 0,
            TargetName = string.Empty,
            TargetIsTargetingLocalPlayer = false,
            InventoryWindow = CreateInventoryWindow(false, 0.0, 0.0),
            InventoryMoney = 1000,
            InventoryCapacity = 10,
            InventoryItems = new[]
            {
                new InventoryItemSnapshot(167000450, 10, "green-manastone-a", 1, 0, false, 24, 2),
                new InventoryItemSnapshot(167000451, 11, "green-manastone-b", 1, 1, false, 24, 2),
                new InventoryItemSnapshot(1001, 12, "kept", 1, 2, false, 1, 4),
                new InventoryItemSnapshot(1002, 13, "filler-3", 1, 3, false, 1, 1),
                new InventoryItemSnapshot(1003, 14, "filler-4", 1, 4, false, 1, 1),
                new InventoryItemSnapshot(1004, 15, "filler-5", 1, 5, false, 1, 1),
                new InventoryItemSnapshot(1005, 16, "filler-6", 1, 6, false, 1, 1)
            }
        };
        keyboard.AfterPress = key =>
        {
            if (key == "NumPad7")
            {
                gameApi.Player = gameApi.Player with
                {
                    Position = new Vector3Snapshot(100, 0, 0),
                    CapturedAt = DateTimeOffset.Now
                };
            }
            else if (key == "F8")
            {
                gameApi.TargetEntityId = 77;
                gameApi.TargetServerObjectId = 7700;
                gameApi.TargetName = cleanupNpcName;
                gameApi.TargetCurrentHp = 0;
                gameApi.TargetMaxHp = 0;
            }
            else if (key == "I")
            {
                gameApi.InventoryWindow = CreateInventoryWindow(!gameApi.InventoryWindow.IsOpen, 0.0, 0.0);
            }
        };

        var rules = BagCleanupRuleCatalog.CreateDefaultRules();
        rules.First(rule => rule.Key == BagCleanupRuleCatalog.GreenManastone).Enabled = true;
        var settings = CreateScriptSettings();
        settings.Paths.TownReturnKey = "NumPad7";
        settings.Paths.MaintenancePathName = "cleanup-path";
        settings.Maintenance = new MaintenanceScriptSettings
        {
            BagCleanupEnabled = true,
            BagCleanupThreshold = 5,
            BagCleanupRules = rules,
            BagCleanupSellItemClickX = 100,
            BagCleanupSellItemClickY = 200,
            BagCleanupSellButtonClickX = 300,
            BagCleanupSellButtonClickY = 400
        };
        var pathStore = new InMemorySharedPathStore(new SharedPathDocument
        {
            Name = "cleanup-path",
            CleanupNpcName = cleanupNpcName,
            Points = new List<SharedPathPoint>
            {
                new() { X = 1, Y = 0, Z = 0 },
                new() { X = 2, Y = 0, Z = 0 }
            }
        });
        var pathCalls = new List<string>();
        var controller = new BagCleanupController(
            keyboard,
            pathStore,
            (_, pathName, points) =>
            {
                pathCalls.Add(pathName + ":" + points.Count);
                return Task.FromResult(OperationResult.Ok());
            });
        var state = new BagCleanupState();
        var context = CreateContext(settings, gameApi, logger);
        BagCleanupTickResult? last = null;
        for (var i = 0; i < 30; i++)
        {
            last = await controller.TickAfterLootAsync(context, state).ConfigureAwait(false);
            if (last.Status == BagCleanupTickStatus.RecoverableFailure ||
                last.Status == BagCleanupTickStatus.FatalFailure ||
                last.Status == BagCleanupTickStatus.Completed)
            {
                break;
            }
        }

        AssertEqual(BagCleanupTickStatus.RecoverableFailure, last?.Status ?? BagCleanupTickStatus.FatalFailure, "cleanup failure should be recoverable");
        AssertEqual("money_verify_failed", last?.Reason ?? string.Empty, "money verify failure should be logged as clear reason");
        AssertSequence(new[] { "cleanup-path:2", "cleanup-path 返回:2" }, pathCalls.ToArray(), "money verify failure should reverse the cleanup path");
        AssertFalse(state.LastFailedAt == DateTimeOffset.MinValue, "failure should start failure cooldown");
        AssertFalse(!logger.Entries.Any(entry => entry.EventName == "bag_cleanup.failed"), "cleanup failure should be logged");
        AssertFalse(!logger.Entries.Any(entry => entry.EventName == "bag_cleanup.failure.returning"), "cleanup failure should log reverse-path recovery");
        AssertFalse(
            !logger.Entries.Any(entry =>
                entry.EventName == "bag_cleanup.failed" &&
                entry.Fields.TryGetValue("returnedByReversePath", out var returned) &&
                returned is bool boolValue &&
                boolValue),
            "cleanup failure should log that reverse path completed");

        var keyCountAfterFailure = keyboard.Keys.Count;
        var skipped = await controller.TickAfterLootAsync(context, state).ConfigureAwait(false);
        AssertEqual(BagCleanupTickStatus.Skipped, skipped.Status, "cleanup should skip during failure cooldown");
        AssertEqual("cleanup_failure_cooldown", skipped.Reason, "failure cooldown reason");
        AssertEqual(keyCountAfterFailure, keyboard.Keys.Count, "failure cooldown should not press more keys");
        AssertFalse(!logger.Entries.Any(entry => entry.EventName == "bag_cleanup.skip.failure_cooldown"), "failure cooldown skip should be logged");
    }
    finally
    {
        Environment.SetEnvironmentVariable("ROADHOG_BAG_CLEANUP_TOWN_RETURN_SETTLE_MS", previousTownSettle);
        Environment.SetEnvironmentVariable("ROADHOG_BAG_CLEANUP_SELL_VERIFY_DELAY_MS", previousVerify);
        Environment.SetEnvironmentVariable("ROADHOG_BAG_CLEANUP_POINT_HOVER_MS", previousPointHover);
        Environment.SetEnvironmentVariable("ROADHOG_BAG_SELL_REGISTER_HOVER_MS", previousRegisterHover);
        Environment.SetEnvironmentVariable("ROADHOG_BAG_CLEANUP_SELL_ITEM_ENTRY_DELAY_MIN_MS", previousSellItemEntryDelayMin);
        Environment.SetEnvironmentVariable("ROADHOG_BAG_CLEANUP_SELL_ITEM_ENTRY_DELAY_MAX_MS", previousSellItemEntryDelayMax);
        Environment.SetEnvironmentVariable("ROADHOG_DEATH_REVIVE_MOUSE_RESET_COUNT", previousMouseReset);
        Environment.SetEnvironmentVariable("ROADHOG_DEATH_REVIVE_MOUSE_STEP_DELAY_MS", previousMouseStep);
        Environment.SetEnvironmentVariable("ROADHOG_BAG_CLEANUP_NPC_SELECT_ATTEMPTS", previousNpcAttempts);
        Environment.SetEnvironmentVariable("ROADHOG_BAG_CLEANUP_NPC_SELECT_DELAY_MS", previousNpcSelectDelay);
        Environment.SetEnvironmentVariable("ROADHOG_BAG_CLEANUP_TOWN_RETURN_MIN_DISTANCE", previousTownMinDistance);
        Environment.SetEnvironmentVariable("ROADHOG_BAG_CLEANUP_FAILURE_COOLDOWN_MS", previousFailureCooldown);
    }
}

static Task TestBagCleanupMatcherGroupsEquipmentTypesAsync()
{
    var rules = BagCleanupRuleCatalog.CreateDefaultRules();
    rules.First(rule => rule.Key == BagCleanupRuleCatalog.GreenEquipment).Enabled = true;
    rules.First(rule => rule.Key == BagCleanupRuleCatalog.BlueEquipment).Enabled = true;
    rules.First(rule => rule.Key == BagCleanupRuleCatalog.WhiteEquipment).Enabled = true;
    var settings = new MaintenanceScriptSettings
    {
        BagCleanupRules = rules
    };
    var items = new[]
    {
        new InventoryItemSnapshot(100100336, 1, "堕落骑士之战斗锤", 1, 0, false, 2, 2),
        new InventoryItemSnapshot(115000435, 2, "蓝色盾牌", 1, 1, false, 6, 3),
        new InventoryItemSnapshot(110100687, 3, "修罗之长袍上衣", 1, 2, false, 7, 2),
        new InventoryItemSnapshot(121000371, 4, "背叛者之水晶项链", 1, 3, false, 8, 2),
        new InventoryItemSnapshot(111300629, 5, "命运之皮革手套", 1, 4, false, 7, 3),
        new InventoryItemSnapshot(152205278, 6, "图案:匠人之钛质锁链手套", 1, 5, false, 27, 2),
        new InventoryItemSnapshot(152206023, 7, "图案:黑湖水之项链", 1, 6, false, 27, 3),
        new InventoryItemSnapshot(100000001, 8, "White Sword", 1, 7, false, 1, 1),
        new InventoryItemSnapshot(110000001, 9, "White Armor", 1, 8, false, 7, 1),
        new InventoryItemSnapshot(120000001, 10, "White Ring", 1, 9, false, 8, 1)
    };

    var selected = BagCleanupItemMatcher.SelectSellRegistrationItems(items, settings);

    AssertSequence(
        new[] { "堕落骑士之战斗锤", "蓝色盾牌", "修罗之长袍上衣", "背叛者之水晶项链", "命运之皮革手套", "White Sword", "White Armor", "White Ring" },
        selected.Select(item => item.Name).ToArray(),
        "equipment cleanup should include white, green and blue weapon, shield, armor and accessory item types while excluding recipes");

    return Task.CompletedTask;
}

static Task TestBagCleanupMatcherMapsStigmaItemTypeAsync()
{
    var rules = BagCleanupRuleCatalog.CreateDefaultRules();
    rules.First(rule => rule.Key == BagCleanupRuleCatalog.Stigma).Enabled = true;
    var settings = new MaintenanceScriptSettings
    {
        BagCleanupRules = rules
    };
    var items = new[]
    {
        new InventoryItemSnapshot(140000100, 1, "痊愈之闪光 I", 1, 11, false, 9, 2),
        new InventoryItemSnapshot(140000100, 2, "痊愈之闪光 I", 1, 12, false, 9, 2),
        new InventoryItemSnapshot(140000150, 3, "生命力吸收 II", 1, 13, false, 9, 2),
        new InventoryItemSnapshot(152205278, 4, "图案:匠人之钛质锁链手套", 1, 18, false, 27, 2)
    };

    var selected = BagCleanupItemMatcher.SelectSellRegistrationItems(items, settings);

    AssertSequence(
        new[] { "痊愈之闪光 I", "痊愈之闪光 I", "生命力吸收 II" },
        selected.Select(item => item.Name).ToArray(),
        "stigma cleanup should map item type 9 even when the item name does not contain stigma");

    return Task.CompletedTask;
}

static Task TestBagCleanupMatcherExcludesNameKeywordsAsync()
{
    var rules = BagCleanupRuleCatalog.CreateDefaultRules();
    rules.First(rule => rule.Key == BagCleanupRuleCatalog.Stigma).Enabled = true;
    var settings = new MaintenanceScriptSettings
    {
        BagCleanupRules = rules,
        BagCleanupExcludedItemNames = new List<string> { "闪光" }
    };
    var items = new[]
    {
        new InventoryItemSnapshot(140000100, 1, "痊愈之闪光 I", 1, 11, false, 9, 2),
        new InventoryItemSnapshot(140000150, 2, "生命力吸收 II", 1, 12, false, 9, 2)
    };

    var selected = BagCleanupItemMatcher.SelectSellRegistrationItems(items, settings);

    AssertSequence(
        new[] { "生命力吸收 II" },
        selected.Select(item => item.Name).ToArray(),
        "bag cleanup excluded item names should match by keyword containment");

    return Task.CompletedTask;
}

static Task TestBagCleanupMatcherMapsSkillBookItemTypeAsync()
{
    var rules = BagCleanupRuleCatalog.CreateDefaultRules();
    rules.First(rule => rule.Key == BagCleanupRuleCatalog.SkillBook).Enabled = true;
    var settings = new MaintenanceScriptSettings
    {
        BagCleanupRules = rules
    };
    var items = new[]
    {
        new InventoryItemSnapshot(169500164, 1, "Quick Shield I", 1, 33, false, 31),
        new InventoryItemSnapshot(164000089, 2, "Return Spellbook", 1, 34, false, 18),
        new InventoryItemSnapshot(100100336, 3, "Green equipment", 1, 35, false, 2, 2)
    };

    var selected = BagCleanupItemMatcher.SelectSellRegistrationItems(items, settings);

    AssertSequence(
        new[] { "Quick Shield I" },
        selected.Select(item => item.Name).ToArray(),
        "skill book cleanup should map item type 31 even when the item name does not contain skill book");

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

static async Task TestKmBoxNetKeyboardInputAcceptsTeamKeysAsync()
{
    using var input = new KmBoxNetKeyboardInput(new KmBoxNetKeyboardInputOptions
    {
        IpAddress = "127.0.0.1",
        Port = 1,
        Mac = "00112233"
    });

    var teamKeys = new List<string> { "F1", "F2", "F3", "F4", "F5", "F6", "Oem3", "Backquote", "`" };
    for (var offset = 0; offset < 26; offset++)
    {
        teamKeys.Add(((char)('A' + offset)).ToString());
    }

    foreach (var key in teamKeys)
    {
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var reachedConnectionPath = false;
        try
        {
            await input.PressKeyAsync(key, TimeSpan.Zero, cancelled.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            reachedConnectionPath = true;
        }

        AssertFalse(!reachedConnectionPath, "supported KMBox Net team key should not fail local validation: " + key);
    }
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

static Task TestDeviceLeaseStorePreventsCrossProcessReuseAsync()
{
    var directory = CreateTempDirectory("roadhog-device-leases-");
    try
    {
        var path = Path.Combine(directory, "device-leases.json");
        var now = new DateTimeOffset(2026, 7, 19, 10, 0, 0, TimeSpan.Zero);
        var aliveProcesses = new HashSet<int> { 101, 202 };
        var processStarts = new Dictionary<int, DateTimeOffset>
        {
            [101] = now.AddMinutes(-2),
            [202] = now.AddMinutes(-1)
        };
        var store = new DeviceLeaseStore(
            path,
            () => now,
            (processId, processStartedAtUtc) =>
                aliveProcesses.Contains(processId) &&
                processStarts.TryGetValue(processId, out var expectedStart) &&
                expectedStart == processStartedAtUtc);

        var first = store.TryAcquire(101, processStarts[101], @"C:\script\1", "P0004.H0002", "fpga");
        AssertFalse(!first.Success, "first process should acquire its devices");

        var hardwareConflict = store.TryAcquire(202, processStarts[202], @"C:\script\2", "P0004.H0002", "fpga://devindex=1");
        AssertFalse(hardwareConflict.Success, "second process should not acquire occupied hardware");
        AssertEqual(101, hardwareConflict.Conflict?.ProcessId ?? 0, "hardware conflict owner pid");

        var vmmConflict = store.TryAcquire(202, processStarts[202], @"C:\script\2", "P0004.H0003", "fpga://devindex=0");
        AssertFalse(vmmConflict.Success, "fpga alias should conflict with indexed VMM zero");

        var second = store.TryAcquire(202, processStarts[202], @"C:\script\2", "P0004.H0003", "fpga://devindex=1");
        AssertFalse(!second.Success, "second process should acquire a different hardware and VMM pair");

        var active = store.ReadActive();
        AssertFalse(!active.Success, "active leases should load");
        AssertEqual(2, active.Value?.Count ?? 0, "active device lease count");

        aliveProcesses.Remove(101);
        var afterExit = store.ReadActive();
        AssertFalse(!afterExit.Success, "dead process lease cleanup should succeed");
        AssertEqual(1, afterExit.Value?.Count ?? 0, "dead process lease should be removed");

        var reclaimed = store.TryAcquire(202, processStarts[202], @"C:\script\2", "P0004.H0002", "fpga");
        AssertFalse(!reclaimed.Success, "remaining process should reclaim devices released by a dead process");

        var release = store.Release(202, processStarts[202]);
        AssertFalse(!release.Success, "device lease release should succeed");
        AssertEqual(0, store.ReadActive().Value?.Count ?? -1, "released lease count");
    }
    finally
    {
        DeleteDirectoryIfExists(directory);
    }

    return Task.CompletedTask;
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
    var previousLicenseCredential = Environment.GetEnvironmentVariable(RoadhogServiceOptions.LicenseCredentialPathEnvironmentVariable);
    try
    {
        Environment.SetEnvironmentVariable(RoadhogServiceOptions.ClientRootEnvironmentVariable, directory);
        Environment.SetEnvironmentVariable(RoadhogServiceOptions.ConfigRootEnvironmentVariable, null);
        Environment.SetEnvironmentVariable(RoadhogServiceOptions.AccountConfigPathEnvironmentVariable, null);
        Environment.SetEnvironmentVariable(RoadhogServiceOptions.PathLibraryDirectoryEnvironmentVariable, null);
        Environment.SetEnvironmentVariable(RoadhogServiceOptions.ProfileLibraryDirectoryEnvironmentVariable, null);
        Environment.SetEnvironmentVariable(RoadhogServiceOptions.KmBoxNetConfigPathEnvironmentVariable, null);
        Environment.SetEnvironmentVariable(RoadhogServiceOptions.LogDirectoryEnvironmentVariable, null);
        Environment.SetEnvironmentVariable(RoadhogServiceOptions.LicenseCredentialPathEnvironmentVariable, null);

        var options = RoadhogServiceOptions.FromEnvironment();

        AssertEqual(Path.Combine(directory, "config", "accounts.json"), options.AccountConfigPath, "client root account config path");
        AssertEqual(Path.Combine(directory, "config", "paths"), options.PathLibraryDirectory, "client root path library");
        AssertEqual(Path.Combine(directory, "config", "profiles"), options.ProfileLibraryDirectory, "client root profile library");
        AssertEqual(Path.Combine(directory, "config", "kmbox-net.json"), options.KmBoxNetConfigPath, "client root kmbox config path");
        AssertEqual(Path.Combine(directory, "config", "license.dat"), options.LicenseCredentialPath, "client root license credential path");
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
        Environment.SetEnvironmentVariable(RoadhogServiceOptions.LicenseCredentialPathEnvironmentVariable, previousLicenseCredential);
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
                    CombatPathName = pathName,
                    TownReturnKey = "NumPad7",
                    RecordingMinimumDistance = 2.5D,
                    DeathReviveClickX = 618,
                    DeathReviveClickY = 349
                }
            }
        };

        var save = await store.UpsertAsync(account).ConfigureAwait(false);
        AssertFalse(!save.Success, "account save should succeed");

        var text = await File.ReadAllTextAsync(accountPath).ConfigureAwait(false);
        AssertFalse(!text.Contains("\"CombatPathName\"", StringComparison.Ordinal), "account config should contain shared path reference field");
        AssertFalse(!text.Contains("\"TownReturnKey\": \"NumPad7\"", StringComparison.Ordinal), "account config should contain town return key");
        AssertFalse(!text.Contains("\"RecordingMinimumDistance\": 2.5", StringComparison.Ordinal), "account config should contain recording minimum distance");
        AssertFalse(!text.Contains("\"DeathReviveClickX\"", StringComparison.Ordinal), "account config should contain death revive click x");
        AssertFalse(!text.Contains("\"DeathReviveClickY\"", StringComparison.Ordinal), "account config should contain death revive click y");
        AssertFalse(text.Contains("\"Points\"", StringComparison.Ordinal), "account config should not contain path points");

        var load = await store.LoadAllAsync().ConfigureAwait(false);
        AssertFalse(!load.Success, "account config should load");
        AssertEqual(pathName, load.Value?[0].ScriptSettings?.Paths.CombatPathName ?? string.Empty, "loaded combat path name");
        AssertEqual("NumPad7", load.Value?[0].ScriptSettings?.Paths.TownReturnKey ?? string.Empty, "loaded town return key");
        AssertEqual(2.5D, load.Value?[0].ScriptSettings?.Paths.RecordingMinimumDistance ?? 0.0D, "loaded recording minimum distance");
        AssertEqual(618, load.Value?[0].ScriptSettings?.Paths.DeathReviveClickX ?? 0, "loaded death revive click x");
        AssertEqual(349, load.Value?[0].ScriptSettings?.Paths.DeathReviveClickY ?? 0, "loaded death revive click y");
    }
    finally
    {
        DeleteDirectoryIfExists(directory);
    }
}

static async Task TestAccountConfigPersistsBagCleanupRulesAsync()
{
    var directory = CreateTempDirectory("roadhog-bag-cleanup-");
    try
    {
        var accountPath = Path.Combine(directory, "accounts.json");
        var store = new JsonAccountConfigStore(accountPath);
        var rules = BagCleanupRuleCatalog.CreateDefaultRules();
        rules.First(rule => rule.Key == BagCleanupRuleCatalog.GreenEquipment).Enabled = true;
        rules.First(rule => rule.Key == BagCleanupRuleCatalog.GreenEquipment).Action = BagCleanupAction.Discard;
        rules.First(rule => rule.Key == BagCleanupRuleCatalog.Medicine).Enabled = true;

        var account = new AccountConfig
        {
            AccountName = "account-cleanup",
            ScriptSettings = new ScriptSettings
            {
                Maintenance = new MaintenanceScriptSettings
                {
                    BagCleanupEnabled = true,
                    BagCleanupThreshold = 3,
                    BagCleanupSellItemClickX = 111,
                    BagCleanupSellItemClickY = 222,
                    BagCleanupSellButtonClickX = 333,
                    BagCleanupSellButtonClickY = 444,
                    BagCleanupItemCoordinateMode = BagCleanupItemCoordinateMode.WindowRectRelativeExperimental,
                    DpMaintenanceRules = new List<DpMaintenanceRuleConfig>
                    {
                        new()
                        {
                            RequiredDp = 4000,
                            Key = "NumPad9",
                            SkillId = 1787,
                            SkillName = "Pet DP Buff"
                        }
                    },
                    MpMaintenanceRules = new List<MaintenanceKeyRuleConfig>
                    {
                        new()
                        {
                            BelowPercent = 60,
                            ActionType = MaintenanceRuleActionType.Potion,
                            Key = "NumPad2",
                            RunTiming = MaintenanceRuleRunTiming.AfterCombat
                        }
                    },
                    BagCleanupRules = rules
                }
            }
        };

        var save = await store.UpsertAsync(account).ConfigureAwait(false);
        AssertFalse(!save.Success, "account save should succeed");

        var text = await File.ReadAllTextAsync(accountPath).ConfigureAwait(false);
        AssertFalse(!text.Contains("\"BagCleanupEnabled\": true", StringComparison.Ordinal), "account config should contain bag cleanup enabled");
        AssertFalse(!text.Contains("\"BagCleanupRules\"", StringComparison.Ordinal), "account config should contain bag cleanup rules");
        AssertFalse(!text.Contains("\"BagCleanupSellItemClickX\": 111", StringComparison.Ordinal), "account config should contain sell item click x");
        AssertFalse(!text.Contains("\"BagCleanupSellItemClickY\": 222", StringComparison.Ordinal), "account config should contain sell item click y");
        AssertFalse(!text.Contains("\"BagCleanupSellButtonClickX\": 333", StringComparison.Ordinal), "account config should contain sell button click x");
        AssertFalse(!text.Contains("\"BagCleanupSellButtonClickY\": 444", StringComparison.Ordinal), "account config should contain sell button click y");
        AssertFalse(!text.Contains("\"DpMaintenanceRules\"", StringComparison.Ordinal), "account config should contain dp maintenance rules");
        AssertFalse(!text.Contains("\"RequiredDp\": 4000", StringComparison.Ordinal), "account config should contain dp requirement");
        AssertFalse(!text.Contains("\"Key\": \"NumPad9\"", StringComparison.Ordinal), "account config should contain dp maintenance key");
        AssertFalse(!text.Contains("\"ActionType\": \"Potion\"", StringComparison.Ordinal), "account config should contain potion maintenance type");
        AssertFalse(
            !text.Contains(
                "\"BagCleanupItemCoordinateMode\": \"WindowRectRelativeExperimental\"",
                StringComparison.Ordinal),
            "account config should contain bag item coordinate mode");
        AssertFalse(!text.Contains("\"Key\": \"equipment.green\"", StringComparison.Ordinal), "bag cleanup rule key should be persisted");
        AssertFalse(!text.Contains("\"Category\": \"equipment\"", StringComparison.Ordinal), "bag cleanup category should be persisted");
        AssertFalse(!text.Contains("\"Quality\": \"green\"", StringComparison.Ordinal), "bag cleanup quality should be persisted");
        AssertFalse(!text.Contains("\"Key\": \"equipment.white\"", StringComparison.Ordinal), "white equipment cleanup rule key should be persisted");
        AssertFalse(!text.Contains("\"Quality\": \"white\"", StringComparison.Ordinal), "white equipment cleanup quality should be persisted");
        AssertFalse(!text.Contains("\"weapon\"", StringComparison.Ordinal), "bag cleanup item kind should be persisted");
        AssertFalse(!text.Contains("\"accessory\"", StringComparison.Ordinal), "bag cleanup accessory kind should be persisted");
        AssertFalse(!text.Contains("\"Action\": \"Discard\"", StringComparison.Ordinal), "bag cleanup action should be persisted");

        var load = await store.LoadAllAsync().ConfigureAwait(false);
        AssertFalse(!load.Success, "account config should load");
        var maintenance = load.Value?[0].ScriptSettings?.Maintenance;
        AssertFalse(maintenance?.BagCleanupEnabled != true, "loaded bag cleanup enabled");
        AssertEqual(111, maintenance?.BagCleanupSellItemClickX ?? 0, "loaded sell item click x");
        AssertEqual(222, maintenance?.BagCleanupSellItemClickY ?? 0, "loaded sell item click y");
        AssertEqual(333, maintenance?.BagCleanupSellButtonClickX ?? 0, "loaded sell button click x");
        AssertEqual(444, maintenance?.BagCleanupSellButtonClickY ?? 0, "loaded sell button click y");
        AssertEqual(
            BagCleanupItemCoordinateMode.WindowRectRelativeExperimental,
            maintenance?.BagCleanupItemCoordinateMode ?? BagCleanupItemCoordinateMode.LegacyNormalizedTopLeft,
            "loaded bag item coordinate mode");
        AssertEqual(1, maintenance?.DpMaintenanceRules.Count ?? 0, "loaded dp maintenance rule count");
        AssertEqual(4000, maintenance?.DpMaintenanceRules[0].RequiredDp ?? 0, "loaded dp requirement");
        AssertEqual("NumPad9", maintenance?.DpMaintenanceRules[0].Key ?? string.Empty, "loaded dp maintenance key");
        AssertEqual(1, maintenance?.MpMaintenanceRules.Count ?? 0, "loaded mp maintenance rule count");
        AssertEqual(MaintenanceRuleActionType.Potion, maintenance?.MpMaintenanceRules[0].ActionType ?? MaintenanceRuleActionType.Skill, "loaded mp maintenance action type");
        AssertEqual(MaintenanceRuleRunTiming.AfterCombat, maintenance?.MpMaintenanceRules[0].RunTiming ?? MaintenanceRuleRunTiming.Always, "loaded mp maintenance timing");
        var loadedRules = BagCleanupRuleCatalog.MergeWithDefaults(load.Value?[0].ScriptSettings?.Maintenance.BagCleanupRules);
        var greenEquipment = loadedRules.First(rule => rule.Key == BagCleanupRuleCatalog.GreenEquipment);
        AssertFalse(!greenEquipment.Enabled, "green equipment cleanup should remain enabled");
        AssertEqual(BagCleanupAction.Discard, greenEquipment.Action, "green equipment cleanup action");
        AssertEqual("equipment", greenEquipment.Category, "green equipment category");
        AssertEqual("green", greenEquipment.Quality, "green equipment quality");
        AssertFalse(!greenEquipment.ItemKinds.Contains("accessory"), "green equipment map should include accessories");
        var whiteEquipment = loadedRules.First(rule => rule.Key == BagCleanupRuleCatalog.WhiteEquipment);
        AssertFalse(whiteEquipment.Enabled, "white equipment cleanup should default disabled");
        AssertEqual("equipment", whiteEquipment.Category, "white equipment category");
        AssertEqual("white", whiteEquipment.Quality, "white equipment quality");
        AssertFalse(!whiteEquipment.ItemKinds.Contains("weapon"), "white equipment map should include weapons");
        AssertFalse(!whiteEquipment.ItemKinds.Contains("armor"), "white equipment map should include armor");
        AssertFalse(!whiteEquipment.ItemKinds.Contains("accessory"), "white equipment map should include accessories");

        var medicine = loadedRules.First(rule => rule.Key == BagCleanupRuleCatalog.Medicine);
        AssertFalse(!medicine.Enabled, "medicine cleanup should remain enabled");
        AssertEqual(BagCleanupAction.Sell, medicine.Action, "medicine cleanup default action");
        AssertFalse(!medicine.ItemKinds.Contains("remedy"), "medicine map should include remedies");
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
                    ReturnHomeWhenNoTarget = false,
                    HasStationaryCombatPosition = true,
                    StationaryCombatX = 1307.758D,
                    StationaryCombatY = 2844.230D,
                    StationaryCombatZ = 259.832D,
                    StationaryCombatRadius = 42.5D,
                    PathCombatRadius = 37.5D,
                    PathFollowReachDistance = 6.5D,
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
        AssertEqual(37.5D, combat.PathCombatRadius, "path radius");
        AssertEqual(6.5D, combat.PathFollowReachDistance, "path follow precision");
        AssertEqual(11.5D, combat.CameraYawPixelsPerDegree, "camera yaw pixels per degree");
        AssertEqual(13.25D, combat.CameraPitchPixelsPerDegree, "camera pitch pixels per degree");
        AssertFalse(!combat.PreferAggressiveMonsters, "prefer aggressive monsters should persist");
        AssertFalse(combat.ReturnHomeWhenNoTarget, "return home when no target should persist");

        var clone = account.ScriptSettings.Combat.Clone();
        AssertFalse(!clone.HasStationaryCombatPosition, "stationary combat position flag should clone");
        AssertEqual(1307.758D, clone.StationaryCombatX, "cloned stationary x");
        AssertEqual(42.5D, clone.StationaryCombatRadius, "cloned stationary radius");
        AssertEqual(37.5D, clone.PathCombatRadius, "cloned path radius");
        AssertEqual(6.5D, clone.PathFollowReachDistance, "cloned path follow precision");
        AssertEqual(11.5D, clone.CameraYawPixelsPerDegree, "cloned camera yaw pixels per degree");
        AssertEqual(13.25D, clone.CameraPitchPixelsPerDegree, "cloned camera pitch pixels per degree");
        AssertFalse(!clone.PreferAggressiveMonsters, "prefer aggressive monsters should clone");
        AssertFalse(clone.ReturnHomeWhenNoTarget, "return home when no target should clone");
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

static async Task TestStationaryCombatReturnsHomeWhenNoTargetAvailableAsync()
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
        Player = new PlayerSnapshot(1, 0, "Fake", 100, 100, 100, 100, 0, new Vector3Snapshot(6, 0, 0), DateTimeOffset.Now, 270, 10, 270),
        TargetEntityId = 0,
        TargetCurrentHp = 0,
        TargetMaxHp = 0,
        TargetPosition = null,
        WorldObjects = Array.Empty<WorldObjectSnapshot>(),
        Skills = CreateSkillSnapshotsById(new Dictionary<uint, uint>())
    };
    var controller = new StationaryCombatController(keyboard, new SemiAutoCombatController(keyboard));
    var plan = SemiAutoSkillPlan.FromSettings(settings.Skills);
    var state = new StationaryCombatState();

    await controller
        .TickAsync(CreateContext(settings, gameApi, logger), plan, new SemiAutoCombatState(), state)
        .ConfigureAwait(false);

    AssertFalse(!state.IsMovingForward, "no-target stationary combat should move back toward home");
    AssertFalse(!keyboard.KeyDowns.Contains("W"), "no-target stationary combat should hold W while returning home");
    AssertFalse(!logger.Entries.Any(entry => entry.EventName == "stationary_combat.no_target.return_home"),
        "no-target return home action should be logged");
    AssertEqual((ushort)0, state.CandidateEntityId, "no target should leave no candidate selected");
}

static async Task TestStationaryCombatJumpsWhenStuckReturningHomeWithNoTargetAsync()
{
    var previousStuckMs = Environment.GetEnvironmentVariable("ROADHOG_DEATH_REVIVE_PATH_STUCK_MS");
    var previousStuckDistance = Environment.GetEnvironmentVariable("ROADHOG_DEATH_REVIVE_PATH_STUCK_DISTANCE");
    var previousJumpHold = Environment.GetEnvironmentVariable("ROADHOG_DEATH_REVIVE_PATH_JUMP_HOLD_MS");
    Environment.SetEnvironmentVariable("ROADHOG_DEATH_REVIVE_PATH_STUCK_MS", "20");
    Environment.SetEnvironmentVariable("ROADHOG_DEATH_REVIVE_PATH_STUCK_DISTANCE", "0.5");
    Environment.SetEnvironmentVariable("ROADHOG_DEATH_REVIVE_PATH_JUMP_HOLD_MS", "1");
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
            Player = new PlayerSnapshot(1, 0, "Fake", 100, 100, 100, 100, 0, new Vector3Snapshot(6, 0, 0), DateTimeOffset.Now, 270, 10, 270),
            TargetEntityId = 0,
            TargetCurrentHp = 0,
            TargetMaxHp = 0,
            TargetPosition = null,
            WorldObjects = Array.Empty<WorldObjectSnapshot>(),
            Skills = CreateSkillSnapshotsById(new Dictionary<uint, uint>())
        };
        var controller = new StationaryCombatController(keyboard, new SemiAutoCombatController(keyboard));
        var plan = SemiAutoSkillPlan.FromSettings(settings.Skills);
        var state = new StationaryCombatState();
        var semiAutoState = new SemiAutoCombatState();
        var context = CreateContext(settings, gameApi, logger);

        await controller.TickAsync(context, plan, semiAutoState, state).ConfigureAwait(false);

        AssertFalse(!state.IsMovingForward, "first no-target return tick should hold W");
        AssertFalse(keyboard.Keys.Contains("Space"), "first no-target return tick should only start stuck tracking");

        await Task.Delay(30).ConfigureAwait(false);
        await controller.TickAsync(context, plan, semiAutoState, state).ConfigureAwait(false);

        AssertFalse(!state.IsMovingForward, "stuck no-target return jump should keep W held");
        AssertFalse(keyboard.KeyUps.Contains("W"), "stuck no-target return jump must not release W");
        AssertEqual(1, keyboard.Keys.Count(key => string.Equals(key, "Space", StringComparison.OrdinalIgnoreCase)),
            "stuck no-target return should press Space once");
        AssertFalse(
            !logger.Entries.Any(entry => entry.EventName == "stationary_combat.return_home.path_stuck_jump"),
            "stuck no-target return jump should be logged");
    }
    finally
    {
        Environment.SetEnvironmentVariable("ROADHOG_DEATH_REVIVE_PATH_STUCK_MS", previousStuckMs);
        Environment.SetEnvironmentVariable("ROADHOG_DEATH_REVIVE_PATH_STUCK_DISTANCE", previousStuckDistance);
        Environment.SetEnvironmentVariable("ROADHOG_DEATH_REVIVE_PATH_JUMP_HOLD_MS", previousJumpHold);
    }
}

static async Task TestStationaryCombatDoesNotReturnHomeWhenNoTargetSwitchDisabledAsync()
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
        ReturnHomeWhenNoTarget = false
    };

    var keyboard = new RecordingKeyboardInput();
    var logger = new InMemoryRoadhogLogger();
    var gameApi = new FakeGameApi
    {
        Player = new PlayerSnapshot(1, 0, "Fake", 100, 100, 100, 100, 0, new Vector3Snapshot(6, 0, 0), DateTimeOffset.Now, 270, 10, 270),
        TargetEntityId = 0,
        TargetCurrentHp = 0,
        TargetMaxHp = 0,
        TargetPosition = null,
        WorldObjects = Array.Empty<WorldObjectSnapshot>(),
        Skills = CreateSkillSnapshotsById(new Dictionary<uint, uint>())
    };
    var controller = new StationaryCombatController(keyboard, new SemiAutoCombatController(keyboard));
    var plan = SemiAutoSkillPlan.FromSettings(settings.Skills);
    var state = new StationaryCombatState();

    await controller
        .TickAsync(CreateContext(settings, gameApi, logger), plan, new SemiAutoCombatState(), state)
        .ConfigureAwait(false);

    AssertFalse(state.IsMovingForward, "disabled no-target return should leave movement stopped");
    AssertFalse(keyboard.KeyDowns.Contains("W"), "disabled no-target return should not hold W");
    AssertFalse(logger.Entries.Any(entry => entry.EventName == "stationary_combat.no_target.return_home"),
        "disabled no-target return should not log return home");
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

static Task TestToolBridgeInventoryParserReadsBagItemsAsync()
{
    var parserType = typeof(JsonAccountConfigStore).Assembly.GetType("Roadhog.Infrastructure.ToolBridge.ToolOutputParsers");
    AssertFalse(parserType is null, "tool output parser type should exist");
    var parseMethod = parserType!.GetMethod(
        "ParseInventory",
        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
    AssertFalse(parseMethod is null, "inventory parser method should exist");

    var lines = new[]
    {
        "#001 Slot=12 Page=1 Cell=13 Row=2 Col=4 Addr=0x00000123456789AB InstanceId=987654 TemplateId=100200 Count=3 Name=\"白色魔石\" CustomName=\"\" Type=60 EquipMask=0x0 Equipped=no EquipArray=no Cash=no Flags=0x00000000 Value=0 ExpiryRaw=0 DurationSec=0 ExtraState=0x0",
        "#002 Slot=n/a Page=n/a Cell=n/a Row=n/a Col=n/a Addr=0x00000123456789AC InstanceId=987655 TemplateId=100201 Count=1 Name=\"已装备耳环\" CustomName=\"\" Type=1 EquipMask=0x1 Equipped=yes EquipArray=yes Cash=no Flags=0x00000000 Value=0 ExpiryRaw=0 DurationSec=0 ExtraState=0x0"
    };
    var items = (IReadOnlyList<InventoryItemSnapshot>)parseMethod!.Invoke(null, new object[] { lines })!;

    AssertEqual(2, items.Count, "two parsed inventory rows");
    AssertEqual("白色魔石", items[0].Name, "first item name");
    AssertEqual(100200U, items[0].TemplateId, "first item template");
    AssertEqual(987654UL, items[0].InstanceId, "first item instance");
    AssertEqual(3U, items[0].Count, "first item count");
    AssertEqual(12, items[0].Slot, "first item slot");
    AssertEqual(60U, items[0].ItemType, "first item type");
    AssertFalse(items[0].IsEquipped, "first item should be bag item");
    AssertEqual(-1, items[1].Slot, "equipped item has no bag slot");
    AssertEqual(1U, items[1].ItemType, "equipped item type");
    AssertFalse(!items[1].IsEquipped, "second item should be equipped");
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

static async Task TestStationaryCombatStartupRecoveryPathClearsNearbyAggressiveMonstersAsync()
{
    var previousBearingMode = Environment.GetEnvironmentVariable("AION_FACE_TARGET_BEARING_MODE");
    Environment.SetEnvironmentVariable("AION_FACE_TARGET_BEARING_MODE", "y-x");
    try
    {
        var settings = CreateScriptSettings();
        settings.MainMode = AccountMainMode.CustomCombat;
        settings.CombatMode = AccountCombatMode.Stationary;
        settings.Paths.RevivePathName = "revive-a";
        settings.SemiAuto.AttackKeyLoopEnabled = true;
        settings.SemiAuto.AttackKeyLoopIntervalMs = 1;
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
            Player = new PlayerSnapshot(1, 0, "Fake", 100, 100, 100, 100, 0, new Vector3Snapshot(0, 0, 0), DateTimeOffset.Now, 90, 10, 90),
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

        AssertFalse(!stationaryState.StartupRecoveryActive, "startup recovery should be active");
        AssertFalse(!keyboard.KeyDowns.Contains("W"), "startup recovery should start path movement before nearby aggressive monster appears");

        keyboard.Keys.Clear();
        keyboard.KeyUps.Clear();
        gameApi.TargetEntityId = 210;
        gameApi.TargetOwnServerObjectId = 2100;
        gameApi.TargetCurrentHp = 1000;
        gameApi.TargetMaxHp = 1000;
        gameApi.TargetPosition = new Vector3Snapshot(4, 0, 0);
        gameApi.TargetServerObjectId = 0;
        gameApi.TargetIsTargetingLocalPlayer = false;
        gameApi.WorldObjects = new[]
        {
            new WorldObjectSnapshot(
                209,
                2090,
                "passive-near",
                "monster",
                new Vector3Snapshot(2, 0, 0),
                2,
                1000,
                1000,
                AggressiveKnown: true,
                IsAggressiveToPlayer: false),
            new WorldObjectSnapshot(
                210,
                2100,
                "active-near",
                "monster",
                new Vector3Snapshot(4, 0, 0),
                4,
                1000,
                1000,
                AggressiveKnown: true,
                IsAggressiveToPlayer: true),
            new WorldObjectSnapshot(
                211,
                2110,
                "active-outside",
                "monster",
                new Vector3Snapshot(16, 0, 0),
                16,
                1000,
                1000,
                AggressiveKnown: true,
                IsAggressiveToPlayer: true)
        };

        await controller.TickAsync(context, plan, semiAutoState, stationaryState).ConfigureAwait(false);

        AssertFalse(!stationaryState.StartupRecoveryActive, "startup recovery should remain active while clearing");
        AssertFalse(!stationaryState.Fighting, "nearby aggressive monster should interrupt startup recovery path into combat");
        AssertFalse(!stationaryState.CurrentTargetIsRevivePathClear, "nearby aggressive monster should be tracked as a revive path clear target");
        AssertFalse(stationaryState.CurrentTargetIsMaintenanceDefense, "nearby aggressive monster should not be treated as targeting-defense yet");
        AssertEqual((ushort)210, stationaryState.CandidateEntityId, "nearest aggressive monster inside 15m should become candidate");
        AssertFalse(!keyboard.KeyUps.Contains("W"), "startup recovery clear should stop path movement first");
        AssertFalse(!keyboard.Keys.Contains("C"), "startup recovery clear should press the opening attack key while waiting for target aggro");
        AssertFalse(!logger.Entries.Any(entry =>
                entry.EventName == "stationary_combat.recovery_defense.target_selected" &&
                string.Equals(Convert.ToString(entry.Fields["phase"]), "startup_recovery", StringComparison.Ordinal) &&
                entry.Fields.TryGetValue("revivePathClear", out var value) &&
                value is true),
            "startup recovery clear target should be logged");

        keyboard.Keys.Clear();
        await controller.TickAsync(context, plan, semiAutoState, stationaryState).ConfigureAwait(false);

        AssertFalse(logger.Entries.Any(entry => entry.EventName == "stationary_combat.target.leash_wait"), "revive path clear target should bypass home leash while fighting");
        AssertFalse(!stationaryState.CurrentTargetIsRevivePathClear, "revive path clear flag should remain while fighting the clear target");
    }
    finally
    {
        Environment.SetEnvironmentVariable("AION_FACE_TARGET_BEARING_MODE", previousBearingMode);
    }
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
        settings.Paths.DeathReviveClickX = 612;
        settings.Paths.DeathReviveClickY = 345;
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
            new[] { "move:-2000,-2000", "move:-2000,-2000", "move:612,345", "down:Left", "up:Left" },
            keyboard.MouseCommands.ToArray(),
            "death recovery should absolute-click configured revive button");
        AssertFalse(keyboard.Keys.Contains("Tab"), "death recovery must not enter target acquisition");
        AssertFalse(keyboard.Keys.Any(key => key.StartsWith("D", StringComparison.OrdinalIgnoreCase)), "death recovery must not release combat skills");

        await controller.TickAsync(context, plan, semiAutoState, stationaryState).ConfigureAwait(false);
        AssertSequence(
            new[]
            {
                "move:-2000,-2000",
                "move:-2000,-2000",
                "move:612,345",
                "down:Left",
                "up:Left"
            },
            keyboard.MouseCommands.Skip(5).Take(5).ToArray(),
            "death recovery should retry configured revive click when player is still dead after retry delay");
        AssertEqual(2, stationaryState.DeathRecovery.ReviveClickCount, "death recovery should record retry revive click count");

        await controller.TickAsync(context, plan, semiAutoState, stationaryState).ConfigureAwait(false);
        AssertSequence(
            new[]
            {
                "move:-2000,-2000",
                "move:-2000,-2000",
                "move:612,345",
                "down:Left",
                "up:Left"
            },
            keyboard.MouseCommands.Skip(10).Take(5).ToArray(),
            "death recovery should keep using configured revive click when player is still dead after second retry");
        AssertEqual(3, stationaryState.DeathRecovery.ReviveClickCount, "death recovery should record third revive click count");

        await controller.TickAsync(context, plan, semiAutoState, stationaryState).ConfigureAwait(false);
        AssertSequence(
            new[]
            {
                "move:-2000,-2000",
                "move:-2000,-2000",
                "move:612,345",
                "down:Left",
                "up:Left"
            },
            keyboard.MouseCommands.Skip(15).Take(5).ToArray(),
            "death recovery should keep using configured revive click on later retries");
        AssertEqual(4, stationaryState.DeathRecovery.ReviveClickCount, "death recovery should record rotated revive click count");

        gameApi.Player = gameApi.Player with { CurrentHp = 10 };
        await controller.TickAsync(context, plan, semiAutoState, stationaryState).ConfigureAwait(false);
        AssertSequence(
            Enumerable.Repeat("wheel:-1", 30).ToArray(),
            keyboard.MouseCommands.Skip(20).Take(30).ToArray(),
            "revived player should scroll wheel down thirty times before maintenance");
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

static async Task TestStationaryCombatDeathRecoverySitsBeforeMpMaintenanceRuleAsync()
{
    var settings = CreateScriptSettings();
    settings.Maintenance.SitMaintenanceEnabled = true;
    settings.Maintenance.SitHpRecoverToPercent = 85;
    settings.Maintenance.SitMpRecoverToPercent = 85;
    settings.Maintenance.MpMaintenanceRules.Add(new MaintenanceKeyRuleConfig
    {
        BelowPercent = 69,
        Key = "D0",
        SkillId = 1771,
        SkillName = "精灵吸收 I"
    });

    var keyboard = new RecordingKeyboardInput();
    var logger = new InMemoryRoadhogLogger();
    var gameApi = new FakeGameApi
    {
        Player = new PlayerSnapshot(
            1,
            0,
            "Fake",
            25,
            100,
            25,
            100,
            0,
            new Vector3Snapshot(0, 0, 0),
            DateTimeOffset.Now,
            CharacterClassId: AionClassId.Spiritmaster),
        Skills = new[]
        {
            new SkillSnapshot(1771, "精灵吸收 I", 1, 1, "精灵吸收", 1, false, 120_000, 0)
        }
    };
    keyboard.AfterPress = key =>
    {
        if (string.Equals(key, "D0", StringComparison.Ordinal))
        {
            gameApi.Skills = new[]
            {
                new SkillSnapshot(1771, "精灵吸收 I", 1, 1, "精灵吸收", 1, false, 120_000, ActiveCooldownEnd())
            };
        }
    };

    var controller = new SemiAutoCombatController(keyboard);
    var state = new SemiAutoCombatState();
    var handled = await controller
        .TryRecoverAfterReviveAsync(
            CreateContext(settings, gameApi, logger),
            state,
            gameApi.Player,
            SemiAutoSkillPlan.FromSettings(settings.Skills))
        .ConfigureAwait(false);

    AssertFalse(!handled, "revive recovery should be handled by sit maintenance");
    AssertSequence(new[] { "OemComma" }, keyboard.Keys.ToArray(), "revive recovery should sit before mp maintenance skill");
    AssertFalse(keyboard.Keys.Contains("D0"), "revive recovery must not press mp maintenance before sitting to recovery values");
    AssertFalse(!state.IsMaintenanceResting, "revive recovery should track sitting state");
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

static async Task TestStationaryCombatDeathRecoveryPathClearsNearbyAggressiveMonstersAsync()
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
        settings.SemiAuto.AttackKeyLoopEnabled = true;
        settings.SemiAuto.AttackKeyLoopIntervalMs = 1;
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
        AssertFalse(!keyboard.KeyDowns.Contains("W"), "death recovery should start path movement before nearby aggressive monster appears");

        keyboard.Keys.Clear();
        keyboard.KeyUps.Clear();
        gameApi.TargetEntityId = 210;
        gameApi.TargetOwnServerObjectId = 2100;
        gameApi.TargetCurrentHp = 1000;
        gameApi.TargetMaxHp = 1000;
        gameApi.TargetPosition = new Vector3Snapshot(4, 0, 0);
        gameApi.TargetServerObjectId = 0;
        gameApi.TargetIsTargetingLocalPlayer = false;
        gameApi.WorldObjects = new[]
        {
            new WorldObjectSnapshot(
                209,
                2090,
                "passive-near",
                "monster",
                new Vector3Snapshot(2, 0, 0),
                2,
                1000,
                1000,
                AggressiveKnown: true,
                IsAggressiveToPlayer: false),
            new WorldObjectSnapshot(
                210,
                2100,
                "active-near",
                "monster",
                new Vector3Snapshot(4, 0, 0),
                4,
                1000,
                1000,
                AggressiveKnown: true,
                IsAggressiveToPlayer: true),
            new WorldObjectSnapshot(
                211,
                2110,
                "active-outside",
                "monster",
                new Vector3Snapshot(16, 0, 0),
                16,
                1000,
                1000,
                AggressiveKnown: true,
                IsAggressiveToPlayer: true)
        };

        await controller.TickAsync(context, plan, semiAutoState, stationaryState).ConfigureAwait(false);

        AssertEqual(StationaryCombatTopLevelState.DeathRecovery, stationaryState.TopLevelState, "clear should stay inside death recovery path state");
        AssertEqual(StationaryCombatDeathRecoveryStep.FollowRevivePath, stationaryState.DeathRecovery.Step, "clear should not complete revive path");
        AssertFalse(!stationaryState.Fighting, "nearby aggressive monster should interrupt death recovery path into combat");
        AssertFalse(!stationaryState.CurrentTargetIsRevivePathClear, "nearby aggressive monster should be tracked as a revive path clear target");
        AssertFalse(stationaryState.CurrentTargetIsMaintenanceDefense, "nearby aggressive monster should not be treated as targeting-defense yet");
        AssertEqual((ushort)210, stationaryState.CandidateEntityId, "nearest aggressive monster inside 15m should become candidate");
        AssertFalse(!keyboard.KeyUps.Contains("W"), "death recovery clear should stop path movement first");
        AssertFalse(!keyboard.Keys.Contains("C"), "death recovery clear should press the opening attack key while waiting for target aggro");
        AssertFalse(!logger.Entries.Any(entry =>
                entry.EventName == "stationary_combat.recovery_defense.target_selected" &&
                string.Equals(Convert.ToString(entry.Fields["phase"]), "death_recovery", StringComparison.Ordinal) &&
                entry.Fields.TryGetValue("revivePathClear", out var value) &&
                value is true),
            "death recovery clear target should be logged");

        keyboard.Keys.Clear();
        await controller.TickAsync(context, plan, semiAutoState, stationaryState).ConfigureAwait(false);

        AssertFalse(logger.Entries.Any(entry => entry.EventName == "stationary_combat.target.leash_wait"), "revive path clear target should bypass home leash while fighting");
        AssertFalse(!stationaryState.CurrentTargetIsRevivePathClear, "revive path clear flag should remain while fighting the clear target");
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

static async Task TestStationaryCombatDeathRecoveryPathRestsBeforeContinuingLowHpAsync()
{
    var previousBearingMode = Environment.GetEnvironmentVariable("AION_FACE_TARGET_BEARING_MODE");
    Environment.SetEnvironmentVariable("AION_FACE_TARGET_BEARING_MODE", "y-x");
    try
    {
        var settings = CreateScriptSettings();
        settings.MainMode = AccountMainMode.CustomCombat;
        settings.CombatMode = AccountCombatMode.Stationary;
        settings.Paths.RevivePathName = "revive-a";
        settings.Maintenance.SitMaintenanceEnabled = true;
        settings.Maintenance.SitHpBelowPercent = 30;
        settings.Maintenance.SitHpRecoverToPercent = 75;
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
            Player = new PlayerSnapshot(1, 0, "Fake", 80, 100, 100, 100, 0, new Vector3Snapshot(0, 0, 0), DateTimeOffset.Now, 90, 10, 90),
            TargetEntityId = 0,
            TargetCurrentHp = 0,
            TargetMaxHp = 0,
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

        AssertEqual(StationaryCombatDeathRecoveryStep.FollowRevivePath, stationaryState.DeathRecovery.Step, "test should stay on revive path");
        AssertFalse(!stationaryState.IsMovingForward, "revive path should start moving before hp drops");
        AssertFalse(!keyboard.KeyDowns.Contains("W"), "revive path should press W before hp drops");

        keyboard.Keys.Clear();
        keyboard.KeyDowns.Clear();
        keyboard.KeyUps.Clear();
        gameApi.Player = gameApi.Player with { CurrentHp = 20 };

        await controller.TickAsync(context, plan, semiAutoState, stationaryState).ConfigureAwait(false);

        AssertSequence(new[] { "OemComma" }, keyboard.Keys.ToArray(), "low hp during revive path should sit before continuing");
        AssertFalse(!keyboard.KeyUps.Contains("W"), "revive path rest should stop movement before sitting");
        AssertFalse(stationaryState.IsMovingForward, "revive path rest should clear moving state");
        AssertFalse(!semiAutoState.IsMaintenanceResting, "low hp revive path should track sitting state");
        AssertEqual(StationaryCombatDeathRecoveryStep.FollowRevivePath, stationaryState.DeathRecovery.Step, "rest should not complete revive path");

        keyboard.Keys.Clear();
        keyboard.KeyDowns.Clear();
        keyboard.KeyUps.Clear();
        gameApi.Player = gameApi.Player with { CurrentHp = 74 };

        await controller.TickAsync(context, plan, semiAutoState, stationaryState).ConfigureAwait(false);

        AssertSequence(Array.Empty<string>(), keyboard.Keys.ToArray(), "revive path should keep resting below recovery percent");
        AssertFalse(keyboard.KeyDowns.Contains("W"), "revive path should not move while still resting");
        AssertFalse(!semiAutoState.IsMaintenanceResting, "revive path should keep sitting below recovery percent");

        gameApi.Player = gameApi.Player with { CurrentHp = 75 };

        await controller.TickAsync(context, plan, semiAutoState, stationaryState).ConfigureAwait(false);

        AssertSequence(new[] { "X" }, keyboard.Keys.ToArray(), "revive path should stand after hp reaches recovery percent");
        AssertFalse(semiAutoState.IsMaintenanceResting, "revive path should clear rest after recovery");

        keyboard.Keys.Clear();
        keyboard.KeyDowns.Clear();
        keyboard.KeyUps.Clear();

        await controller.TickAsync(context, plan, semiAutoState, stationaryState).ConfigureAwait(false);

        AssertFalse(!keyboard.KeyDowns.Contains("W"), "revive path should continue moving after rest exits");
    }
    finally
    {
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

static async Task TestStationaryCombatDeathRecoveryLeaderSiphonPausesAndResumesRevivePathAsync()
{
    var previousBearingMode = Environment.GetEnvironmentVariable("AION_FACE_TARGET_BEARING_MODE");
    Environment.SetEnvironmentVariable("AION_FACE_TARGET_BEARING_MODE", "y-x");
    try
    {
        var settings = CreateTeamOutputSettings();
        settings.MainMode = AccountMainMode.CustomCombat;
        settings.CombatMode = AccountCombatMode.Stationary;
        settings.Paths.RevivePathName = "revive-a";
        settings.Team.GroupDistanceMeters = 12.0D;
        settings.Team.Output!.FollowLeader = true;
        settings.Combat = new CombatScriptSettings
        {
            HasStationaryCombatPosition = true,
            StationaryCombatX = 20,
            StationaryCombatY = 0,
            StationaryCombatZ = 0,
            StationaryCombatRadius = 10,
            PathFollowReachDistance = 1.0D
        };

        var self = CreatePartyMemberSnapshot(1000, "Dps", true, false, 0.0D);
        var leader = CreatePartyMemberSnapshot(2000, "Leader", false, true, 8.0D);
        var gameApi = CreateTeamSupportGameApi(self, leader);
        gameApi.Player = new PlayerSnapshot(
            1,
            0,
            self.Name,
            100,
            100,
            100,
            100,
            0,
            new Vector3Snapshot(0, 0, 0),
            DateTimeOffset.Now,
            90,
            10,
            90);
        gameApi.WorldObjects = Array.Empty<WorldObjectSnapshot>();
        gameApi.Skills = CreateSkillSnapshotsById(new Dictionary<uint, uint>());

        var keyboard = new RecordingKeyboardInput();
        var logger = new InMemoryRoadhogLogger();
        var semiAuto = new SemiAutoCombatController(keyboard);
        var controller = new StationaryCombatController(keyboard, semiAuto);
        var stationaryState = new StationaryCombatState
        {
            IsMovingForward = true
        };
        stationaryState.EnterDeathRecovery(DateTimeOffset.Now);
        for (var i = 0; i < 6; i++)
        {
            stationaryState.DeathRecovery.Advance(DateTimeOffset.Now);
        }

        stationaryState.DeathRecovery.RevivePathName = "revive-a";
        stationaryState.DeathRecovery.RevivePathPoints = new[]
        {
            new Vector3Snapshot(0, 0, 0),
            new Vector3Snapshot(10, 0, 0),
            new Vector3Snapshot(20, 0, 0)
        };
        stationaryState.DeathRecovery.RevivePathPointIndex = 1;
        var plan = SemiAutoSkillPlan.FromSettings(settings.Skills);
        var context = CreateContext(settings, gameApi, logger);

        await controller
            .TickPlayerLifeGuardAsync(context, plan, new SemiAutoCombatState(), stationaryState, followRevivePath: true)
            .ConfigureAwait(false);

        AssertFalse(!stationaryState.DeathRecovery.RevivePathLeaderSiphonActive, "near leader should pause revive path");
        AssertEqual(StationaryCombatDeathRecoveryStep.FollowRevivePath, stationaryState.DeathRecovery.Step, "siphon should keep revive path step paused");
        AssertFalse(!keyboard.KeyUps.Contains("W"), "siphon should release existing revive-path movement");
        AssertFalse(keyboard.KeyDowns.Contains("W"), "siphon must not continue revive-path movement while leader is in range");
        AssertFalse(
            !logger.Entries.Any(entry => entry.EventName == "stationary_combat.death_recovery.leader_siphon.enter"),
            "siphon entry should be logged");

        keyboard.KeyUps.Clear();
        keyboard.KeyDowns.Clear();
        var farLeader = leader with
        {
            DistanceToLocalPlayer = 25.0D,
            LivePosition = new Vector3Snapshot(25, 0, 0),
            VisibilityState = PartyMemberVisibilityState.ScreenVisible
        };
        gameApi.Party = CreateTeamSupportParty(self, farLeader);
        gameApi.Player = gameApi.Player with
        {
            Position = new Vector3Snapshot(6, 0, 0)
        };
        stationaryState.Fighting = true;
        stationaryState.CurrentTargetEntityId = 300;
        stationaryState.CandidateEntityId = 300;

        await controller
            .TickPlayerLifeGuardAsync(context, plan, new SemiAutoCombatState(), stationaryState, followRevivePath: true)
            .ConfigureAwait(false);

        AssertFalse(stationaryState.DeathRecovery.RevivePathLeaderSiphonActive, "far leader should release the siphon");
        AssertFalse(stationaryState.Fighting, "resumed revive path should drop leader-siphon combat target");
        AssertEqual(1, stationaryState.DeathRecovery.RevivePathPointIndex, "revive path should retarget from current position");
        AssertFalse(!keyboard.KeyDowns.Contains("W"), "released siphon should resume revive-path movement");
        AssertFalse(
            !logger.Entries.Any(entry =>
                entry.EventName == "stationary_combat.death_recovery.leader_siphon.exit" &&
                string.Equals(Convert.ToString(entry.Fields["reason"]), "leader_out_of_range", StringComparison.Ordinal)),
            "siphon exit should be logged when leader leaves group radius");
        AssertFalse(
            !logger.Entries.Any(entry => entry.EventName == "stationary_combat.death_recovery.leader_siphon.path_resumed"),
            "revive path retarget should be logged after siphon release");
    }
    finally
    {
        Environment.SetEnvironmentVariable("AION_FACE_TARGET_BEARING_MODE", previousBearingMode);
    }
}

static async Task TestWorkerRunsTeamOutputDuringRevivePathLeaderSiphonAsync()
{
    var previousClickDelay = Environment.GetEnvironmentVariable("ROADHOG_DEATH_REVIVE_CLICK_DELAY_MS");
    var previousStepDelay = Environment.GetEnvironmentVariable("ROADHOG_DEATH_REVIVE_MOUSE_STEP_DELAY_MS");
    var previousClickHold = Environment.GetEnvironmentVariable("ROADHOG_DEATH_REVIVE_CLICK_HOLD_MS");
    var previousRetry = Environment.GetEnvironmentVariable("ROADHOG_DEATH_REVIVE_RETRY_MS");
    var previousScrollCount = Environment.GetEnvironmentVariable("ROADHOG_DEATH_POST_REVIVE_SCROLL_COUNT");
    var previousScrollInterval = Environment.GetEnvironmentVariable("ROADHOG_DEATH_POST_REVIVE_SCROLL_INTERVAL_MS");
    var previousBearingMode = Environment.GetEnvironmentVariable("AION_FACE_TARGET_BEARING_MODE");
    Environment.SetEnvironmentVariable("ROADHOG_DEATH_REVIVE_CLICK_DELAY_MS", "0");
    Environment.SetEnvironmentVariable("ROADHOG_DEATH_REVIVE_MOUSE_STEP_DELAY_MS", "0");
    Environment.SetEnvironmentVariable("ROADHOG_DEATH_REVIVE_CLICK_HOLD_MS", "1");
    Environment.SetEnvironmentVariable("ROADHOG_DEATH_REVIVE_RETRY_MS", "0");
    Environment.SetEnvironmentVariable("ROADHOG_DEATH_POST_REVIVE_SCROLL_COUNT", "0");
    Environment.SetEnvironmentVariable("ROADHOG_DEATH_POST_REVIVE_SCROLL_INTERVAL_MS", "0");
    Environment.SetEnvironmentVariable("AION_FACE_TARGET_BEARING_MODE", "y-x");
    try
    {
        var settings = CreateTeamOutputSettings();
        settings.MainMode = AccountMainMode.CustomCombat;
        settings.CombatMode = AccountCombatMode.Stationary;
        settings.Paths.RevivePathName = "revive-a";
        settings.Team.GroupDistanceMeters = 12.0D;
        settings.Team.Output!.FollowLeader = true;
        settings.Team.Output.StopWhenLeaderHasNoTarget = true;
        settings.Combat = new CombatScriptSettings
        {
            HasStationaryCombatPosition = true,
            StationaryCombatX = 20,
            StationaryCombatY = 0,
            StationaryCombatZ = 0,
            StationaryCombatRadius = 10
        };

        var self = CreatePartyMemberSnapshot(1000, "Dps", true, false, 0.0D);
        var leader = CreatePartyMemberSnapshot(2000, "Leader", false, true, 4.0D);
        var gameApi = CreateTeamSupportGameApi(self, leader);
        gameApi.Player = new PlayerSnapshot(
            1,
            0,
            self.Name,
            0,
            100,
            100,
            100,
            0,
            new Vector3Snapshot(0, 0, 0),
            DateTimeOffset.Now,
            90,
            10,
            90);
        gameApi.WorldObjects = Array.Empty<WorldObjectSnapshot>();
        gameApi.Skills = CreateSkillSnapshotsById(new Dictionary<uint, uint>());

        var pathStore = new InMemorySharedPathStore(
            CreatePath("revive-a",
                new Vector3Snapshot(0, 0, 0),
                new Vector3Snapshot(10, 0, 0),
                new Vector3Snapshot(20, 0, 0)));
        var keyboard = new RecordingKeyboardInput();
        keyboard.AfterMouseUp = button =>
        {
            if (button == RoadhogMouseButton.Left)
            {
                gameApi.Player = gameApi.Player with
                {
                    CurrentHp = 100,
                    Position = new Vector3Snapshot(0, 0, 0)
                };
            }
        };
        keyboard.AfterPress = key =>
        {
            if (string.Equals(key, "F2", StringComparison.Ordinal))
            {
                SetFakeLockedTarget(gameApi, leader.ServerObjectId, 0, 0, 0);
            }
        };
        var logger = new InMemoryRoadhogLogger();
        var semiAuto = new SemiAutoCombatController(keyboard);
        var stationary = new StationaryCombatController(keyboard, semiAuto, pathStore);
        var worker = new DefaultAccountWorkerLoop(
            keyboard,
            semiAuto,
            stationary,
            teamOutput: new TeamOutputController(keyboard));
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var context = CreateContext(
            settings,
            gameApi,
            logger,
            options: new AccountWorkerOptions { TickInterval = TimeSpan.FromMilliseconds(40) },
            stopToken: cts.Token);

        var runTask = worker.RunAsync(context);
        await WaitUntilAsync(
                () => keyboard.Keys.Contains("C"),
                "team output follow during revive-path leader siphon")
            .ConfigureAwait(false);
        cts.Cancel();
        await IgnoreCancellationAsync(runTask).ConfigureAwait(false);

        AssertFalse(!keyboard.Keys.Contains("F2"), "siphon should let team output select the leader");
        AssertFalse(!keyboard.Keys.Contains("C"), "siphon should let team output press the leader follow key");
        AssertFalse(keyboard.KeyDowns.Contains("W"), "siphon should not continue revive-path W movement while leader is nearby");
        AssertFalse(
            !logger.Entries.Any(entry => entry.EventName == "stationary_combat.death_recovery.leader_siphon.enter"),
            "worker should log leader siphon entry");
    }
    finally
    {
        Environment.SetEnvironmentVariable("ROADHOG_DEATH_REVIVE_CLICK_DELAY_MS", previousClickDelay);
        Environment.SetEnvironmentVariable("ROADHOG_DEATH_REVIVE_MOUSE_STEP_DELAY_MS", previousStepDelay);
        Environment.SetEnvironmentVariable("ROADHOG_DEATH_REVIVE_CLICK_HOLD_MS", previousClickHold);
        Environment.SetEnvironmentVariable("ROADHOG_DEATH_REVIVE_RETRY_MS", previousRetry);
        Environment.SetEnvironmentVariable("ROADHOG_DEATH_POST_REVIVE_SCROLL_COUNT", previousScrollCount);
        Environment.SetEnvironmentVariable("ROADHOG_DEATH_POST_REVIVE_SCROLL_INTERVAL_MS", previousScrollInterval);
        Environment.SetEnvironmentVariable("AION_FACE_TARGET_BEARING_MODE", previousBearingMode);
    }
}

static async Task TestWorkerContinuesLootDuringRevivePathLeaderSiphonAsync()
{
    var previousClickDelay = Environment.GetEnvironmentVariable("ROADHOG_DEATH_REVIVE_CLICK_DELAY_MS");
    var previousStepDelay = Environment.GetEnvironmentVariable("ROADHOG_DEATH_REVIVE_MOUSE_STEP_DELAY_MS");
    var previousClickHold = Environment.GetEnvironmentVariable("ROADHOG_DEATH_REVIVE_CLICK_HOLD_MS");
    var previousRetry = Environment.GetEnvironmentVariable("ROADHOG_DEATH_REVIVE_RETRY_MS");
    var previousScrollCount = Environment.GetEnvironmentVariable("ROADHOG_DEATH_POST_REVIVE_SCROLL_COUNT");
    var previousScrollInterval = Environment.GetEnvironmentVariable("ROADHOG_DEATH_POST_REVIVE_SCROLL_INTERVAL_MS");
    var previousBearingMode = Environment.GetEnvironmentVariable("AION_FACE_TARGET_BEARING_MODE");
    var previousAfterKillWait = Environment.GetEnvironmentVariable("ROADHOG_LOOT_AFTER_KILL_WAIT_MS");
    var previousAfterPickWait = Environment.GetEnvironmentVariable("ROADHOG_LOOT_AFTER_PICK_WAIT_MS");
    var previousPressCount = Environment.GetEnvironmentVariable("ROADHOG_LOOT_PRESS_COUNT");
    var previousPressInterval = Environment.GetEnvironmentVariable("ROADHOG_LOOT_PRESS_INTERVAL_MS");
    Environment.SetEnvironmentVariable("ROADHOG_DEATH_REVIVE_CLICK_DELAY_MS", "0");
    Environment.SetEnvironmentVariable("ROADHOG_DEATH_REVIVE_MOUSE_STEP_DELAY_MS", "0");
    Environment.SetEnvironmentVariable("ROADHOG_DEATH_REVIVE_CLICK_HOLD_MS", "1");
    Environment.SetEnvironmentVariable("ROADHOG_DEATH_REVIVE_RETRY_MS", "0");
    Environment.SetEnvironmentVariable("ROADHOG_DEATH_POST_REVIVE_SCROLL_COUNT", "0");
    Environment.SetEnvironmentVariable("ROADHOG_DEATH_POST_REVIVE_SCROLL_INTERVAL_MS", "0");
    Environment.SetEnvironmentVariable("AION_FACE_TARGET_BEARING_MODE", "y-x");
    Environment.SetEnvironmentVariable("ROADHOG_LOOT_AFTER_KILL_WAIT_MS", "40");
    Environment.SetEnvironmentVariable("ROADHOG_LOOT_AFTER_PICK_WAIT_MS", "0");
    Environment.SetEnvironmentVariable("ROADHOG_LOOT_PRESS_COUNT", "1");
    Environment.SetEnvironmentVariable("ROADHOG_LOOT_PRESS_INTERVAL_MS", "0");
    try
    {
        var settings = CreateTeamSupportSettings();
        settings.MainMode = AccountMainMode.CustomCombat;
        settings.CombatMode = AccountCombatMode.Stationary;
        settings.Paths.RevivePathName = "revive-a";
        settings.Team.GroupDistanceMeters = 12.0D;
        settings.Team.Support!.JoinCombat = true;
        settings.Team.Support.LeaderDistanceMeters = 5.0D;
        settings.Maintenance.SitMaintenanceEnabled = false;
        settings.Maintenance.HpMaintenanceRules.Clear();
        settings.Maintenance.MpMaintenanceRules.Clear();
        settings.Maintenance.StatusMaintenanceRules.Clear();
        settings.Combat = new CombatScriptSettings
        {
            EnableLoot = true,
            HasStationaryCombatPosition = true,
            StationaryCombatX = 20,
            StationaryCombatY = 0,
            StationaryCombatZ = 0,
            StationaryCombatRadius = 10
        };
        settings.Skills.ExecutionTree = new List<SkillConfigNode>
        {
            Node(1, "Strike", "主动技能")
        };

        const ushort targetEntityId = 300;
        const uint targetServerObjectId = 3000;
        var self = CreatePartyMemberSnapshot(1000, "Chanter", true, false, 0.0D) with
        {
            Class = AionClassId.Chanter,
            ClassId = (byte)AionClassId.Chanter,
            ClassName = "Chanter"
        };
        var leader = CreatePartyMemberSnapshot(2000, "Leader", false, true, 4.0D) with
        {
            LiveTargetServerObjectId = targetServerObjectId
        };
        var gameApi = CreateTeamSupportGameApi(self, leader);
        gameApi.Player = new PlayerSnapshot(
            1,
            0,
            self.Name,
            0,
            100,
            100,
            100,
            0,
            new Vector3Snapshot(0, 0, 0),
            DateTimeOffset.Now,
            90,
            10,
            90);
        gameApi.WorldObjects = Array.Empty<WorldObjectSnapshot>();
        gameApi.Skills = CreateSkillSnapshotsById(new Dictionary<uint, uint>
        {
            [1] = 0
        });

        var pathStore = new InMemorySharedPathStore(
            CreatePath("revive-a",
                new Vector3Snapshot(0, 0, 0),
                new Vector3Snapshot(10, 0, 0),
                new Vector3Snapshot(20, 0, 0)));
        var keyboard = new RecordingKeyboardInput();
        keyboard.AfterMouseUp = button =>
        {
            if (button == RoadhogMouseButton.Left)
            {
                gameApi.Player = gameApi.Player with
                {
                    CurrentHp = 100,
                    Position = new Vector3Snapshot(0, 0, 0)
                };
            }
        };
        keyboard.AfterPress = key =>
        {
            if (string.Equals(key, "F2", StringComparison.Ordinal))
            {
                SetFakeLockedTarget(gameApi, leader.ServerObjectId, 0, 0, 0);
            }
            else if (string.Equals(key, "Oem3", StringComparison.Ordinal))
            {
                SetFakeLockedTarget(
                    gameApi,
                    targetServerObjectId,
                    LockedTargetSnapshot.MonsterObjectType,
                    leader.ServerObjectId,
                    100);
                gameApi.TargetEntityId = targetEntityId;
                gameApi.TargetName = "target";
                gameApi.TargetPosition = new Vector3Snapshot(2, 0, 0);
            }
            else if (string.Equals(key, "D1", StringComparison.Ordinal))
            {
                gameApi.TargetCurrentHp = 0;
            }
        };

        var logger = new InMemoryRoadhogLogger();
        var semiAuto = new SemiAutoCombatController(keyboard);
        var stationary = new StationaryCombatController(keyboard, semiAuto, pathStore);
        var worker = new DefaultAccountWorkerLoop(
            keyboard,
            semiAuto,
            stationary,
            teamSupport: new TeamSupportController(keyboard));
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var context = CreateContext(
            settings,
            gameApi,
            logger,
            options: new AccountWorkerOptions { TickInterval = TimeSpan.FromMilliseconds(10) },
            stopToken: cts.Token);

        var runTask = worker.RunAsync(context);
        await WaitUntilAsync(
                () => logger.Entries.Any(entry => entry.EventName == "stationary_combat.loot.finished"),
                "loot during revive-path leader siphon")
            .ConfigureAwait(false);
        cts.Cancel();
        await IgnoreCancellationAsync(runTask).ConfigureAwait(false);

        AssertFalse(
            !logger.Entries.Any(entry => entry.EventName == "stationary_combat.death_recovery.leader_siphon.enter"),
            "siphon should be active before combat loot");
        AssertFalse(
            !logger.Entries.Any(entry => entry.EventName == "stationary_combat.loot.pick_pressed"),
            "loot key should be pressed even while revive-path leader siphon is active");
        AssertFalse(!keyboard.Keys.Contains("NumPadDecimal"), "loot key should be recorded by keyboard input");
    }
    finally
    {
        Environment.SetEnvironmentVariable("ROADHOG_DEATH_REVIVE_CLICK_DELAY_MS", previousClickDelay);
        Environment.SetEnvironmentVariable("ROADHOG_DEATH_REVIVE_MOUSE_STEP_DELAY_MS", previousStepDelay);
        Environment.SetEnvironmentVariable("ROADHOG_DEATH_REVIVE_CLICK_HOLD_MS", previousClickHold);
        Environment.SetEnvironmentVariable("ROADHOG_DEATH_REVIVE_RETRY_MS", previousRetry);
        Environment.SetEnvironmentVariable("ROADHOG_DEATH_POST_REVIVE_SCROLL_COUNT", previousScrollCount);
        Environment.SetEnvironmentVariable("ROADHOG_DEATH_POST_REVIVE_SCROLL_INTERVAL_MS", previousScrollInterval);
        Environment.SetEnvironmentVariable("AION_FACE_TARGET_BEARING_MODE", previousBearingMode);
        Environment.SetEnvironmentVariable("ROADHOG_LOOT_AFTER_KILL_WAIT_MS", previousAfterKillWait);
        Environment.SetEnvironmentVariable("ROADHOG_LOOT_AFTER_PICK_WAIT_MS", previousAfterPickWait);
        Environment.SetEnvironmentVariable("ROADHOG_LOOT_PRESS_COUNT", previousPressCount);
        Environment.SetEnvironmentVariable("ROADHOG_LOOT_PRESS_INTERVAL_MS", previousPressInterval);
    }
}

static async Task TestManualPathRetriesTransientPlayerReadFailuresAsync()
{
    var previousTimeout = Environment.GetEnvironmentVariable("ROADHOG_MANUAL_PATH_PLAYER_READ_RETRY_TIMEOUT_MS");
    var previousInterval = Environment.GetEnvironmentVariable("ROADHOG_MANUAL_PATH_PLAYER_READ_RETRY_INTERVAL_MS");
    Environment.SetEnvironmentVariable("ROADHOG_MANUAL_PATH_PLAYER_READ_RETRY_TIMEOUT_MS", "1000");
    Environment.SetEnvironmentVariable("ROADHOG_MANUAL_PATH_PLAYER_READ_RETRY_INTERVAL_MS", "0");
    try
    {
        var settings = CreateScriptSettings();
        var keyboard = new RecordingKeyboardInput();
        var logger = new InMemoryRoadhogLogger();
        var gameApi = new FakeGameApi();
        var atStart = gameApi.Player with { Position = new Vector3Snapshot(0, 0, 0) };
        var atEnd = gameApi.Player with { Position = new Vector3Snapshot(10, 0, 0) };
        gameApi.Player = atEnd;
        gameApi.PlayerReadResults.Enqueue(OperationResult<PlayerSnapshot>.Ok(atStart));
        gameApi.PlayerReadResults.Enqueue(OperationResult<PlayerSnapshot>.Ok(atStart));
        gameApi.PlayerReadResults.Enqueue(OperationResult<PlayerSnapshot>.Ok(atStart));
        for (var index = 0; index < 10; index++)
        {
            gameApi.PlayerReadResults.Enqueue(OperationResult<PlayerSnapshot>.Fail("transient player read"));
        }
        var controller = new StationaryCombatController(keyboard, new SemiAutoCombatController(keyboard));

        var result = await controller
            .ExecutePathOnceAsync(
                CreateContext(settings, gameApi, logger),
                "cleanup-path",
                new[] { new Vector3Snapshot(0, 0, 0), new Vector3Snapshot(10, 0, 0) })
            .ConfigureAwait(false);

        AssertFalse(!result.Success, "transient player reads should not abort path: " + result.Error);
        AssertFalse(!keyboard.KeyDowns.Contains("W"), "path should begin moving before the transient read failure");
        AssertFalse(!keyboard.KeyUps.Contains("W"), "transient read failure should release W");
        AssertFalse(
            logger.Entries.Count(entry => entry.EventName == "manual_path.player_read.retry") <= 0,
            "player read retry should be logged");
        AssertFalse(
            !logger.Entries.Any(entry => entry.EventName == "manual_path.player_read.recovered"),
            "player read recovery should be logged");
    }
    finally
    {
        Environment.SetEnvironmentVariable("ROADHOG_MANUAL_PATH_PLAYER_READ_RETRY_TIMEOUT_MS", previousTimeout);
        Environment.SetEnvironmentVariable("ROADHOG_MANUAL_PATH_PLAYER_READ_RETRY_INTERVAL_MS", previousInterval);
    }
}

static async Task TestManualPathFailsAfterPlayerReadRetryTimeoutAsync()
{
    var previousTimeout = Environment.GetEnvironmentVariable("ROADHOG_MANUAL_PATH_PLAYER_READ_RETRY_TIMEOUT_MS");
    var previousInterval = Environment.GetEnvironmentVariable("ROADHOG_MANUAL_PATH_PLAYER_READ_RETRY_INTERVAL_MS");
    Environment.SetEnvironmentVariable("ROADHOG_MANUAL_PATH_PLAYER_READ_RETRY_TIMEOUT_MS", "0");
    Environment.SetEnvironmentVariable("ROADHOG_MANUAL_PATH_PLAYER_READ_RETRY_INTERVAL_MS", "0");
    try
    {
        var settings = CreateScriptSettings();
        var keyboard = new RecordingKeyboardInput();
        var logger = new InMemoryRoadhogLogger();
        var gameApi = new FakeGameApi();
        var atStart = gameApi.Player with { Position = new Vector3Snapshot(0, 0, 0) };
        gameApi.PlayerReadResults.Enqueue(OperationResult<PlayerSnapshot>.Ok(atStart));
        gameApi.PlayerReadResults.Enqueue(OperationResult<PlayerSnapshot>.Ok(atStart));
        gameApi.PlayerReadResults.Enqueue(OperationResult<PlayerSnapshot>.Ok(atStart));
        gameApi.PlayerReadFallback = OperationResult<PlayerSnapshot>.Fail("persistent player read failure");
        var controller = new StationaryCombatController(keyboard, new SemiAutoCombatController(keyboard));

        var result = await controller
            .ExecutePathOnceAsync(
                CreateContext(settings, gameApi, logger),
                "cleanup-path",
                new[] { new Vector3Snapshot(0, 0, 0), new Vector3Snapshot(10, 0, 0) })
            .ConfigureAwait(false);

        AssertFalse(result.Success, "player read should fail after the configured retry timeout");
        AssertFalse(
            !(result.Error ?? string.Empty).Contains("failed continuously", StringComparison.OrdinalIgnoreCase),
            "timeout failure should describe the continuous player read failure");
        AssertFalse(!keyboard.KeyUps.Contains("W"), "timed-out player read should release W");
        AssertEqual(1, logger.Entries.Count(entry => entry.EventName == "manual_path.player_read.retry"), "timeout retry log count");
    }
    finally
    {
        Environment.SetEnvironmentVariable("ROADHOG_MANUAL_PATH_PLAYER_READ_RETRY_TIMEOUT_MS", previousTimeout);
        Environment.SetEnvironmentVariable("ROADHOG_MANUAL_PATH_PLAYER_READ_RETRY_INTERVAL_MS", previousInterval);
    }
}

static async Task TestPathCombatWorkerFollowsConfiguredCombatPathAsync()
{
    var previousBearingMode = Environment.GetEnvironmentVariable("AION_FACE_TARGET_BEARING_MODE");
    Environment.SetEnvironmentVariable("AION_FACE_TARGET_BEARING_MODE", "y-x");
    try
    {
        var settings = CreateScriptSettings();
        settings.MainMode = AccountMainMode.CustomCombat;
        settings.CombatMode = AccountCombatMode.Path;
        settings.Paths.CombatPathName = "combat-a";
        settings.Combat.StationaryCombatRadius = 8;

        var pathStore = new InMemorySharedPathStore(
            CreatePath("combat-a",
                new Vector3Snapshot(0, 0, 0),
                new Vector3Snapshot(10, 0, 0)));
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
        var stationary = new StationaryCombatController(keyboard, semiAuto, pathStore);
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
                () => keyboard.KeyDowns.Contains("W"),
                "path combat worker path movement")
            .ConfigureAwait(false);
        cts.Cancel();
        await IgnoreCancellationAsync(runTask).ConfigureAwait(false);

        AssertFalse(!logger.Entries.Any(entry => entry.EventName == "stationary_combat.path_combat.path_selected"),
            "path worker should load the configured combat path");
        AssertFalse(logger.Entries.Any(entry => entry.EventName == "stationary_combat.position.missing"),
            "path worker must not enter stationary home validation");
    }
    finally
    {
        Environment.SetEnvironmentVariable("AION_FACE_TARGET_BEARING_MODE", previousBearingMode);
    }
}

static async Task TestPathCombatFollowsRevivePathBeforeDistantCombatPathAsync()
{
    var previousBearingMode = Environment.GetEnvironmentVariable("AION_FACE_TARGET_BEARING_MODE");
    var previousAccessDistance = Environment.GetEnvironmentVariable("ROADHOG_PATH_COMBAT_ACCESS_PATH_DISTANCE");
    Environment.SetEnvironmentVariable("AION_FACE_TARGET_BEARING_MODE", "y-x");
    Environment.SetEnvironmentVariable("ROADHOG_PATH_COMBAT_ACCESS_PATH_DISTANCE", "120");
    try
    {
        var settings = CreateScriptSettings();
        settings.MainMode = AccountMainMode.CustomCombat;
        settings.CombatMode = AccountCombatMode.Path;
        settings.Paths.RevivePathName = "revive-a";
        settings.Paths.CombatPathName = "combat-a";
        settings.Combat.StationaryCombatRadius = 8;

        var pathStore = new InMemorySharedPathStore(
            CreatePath("revive-a",
                new Vector3Snapshot(0, 0, 0),
                new Vector3Snapshot(100, 0, 0),
                new Vector3Snapshot(200, 0, 0)),
            CreatePath("combat-a",
                new Vector3Snapshot(205, 0, 0),
                new Vector3Snapshot(215, 0, 0)));
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
        var state = new StationaryCombatState();
        var context = CreateContext(settings, gameApi, logger);

        await controller
            .TickPathAsync(context, SemiAutoSkillPlan.FromSettings(settings.Skills), new SemiAutoCombatState(), state)
            .ConfigureAwait(false);

        AssertFalse(!state.StartupRecoveryActive, "distant path combat should start access path recovery");
        AssertEqual("revive-a", state.StartupRecoveryPathName, "path combat access path should use revive path");
        AssertEqual(1, state.StartupRecoveryPointIndex, "access path should advance from reached first point to next point");
        AssertFalse(state.PathCombat.Active, "combat path should not start until access path completes");
        AssertFalse(!logger.Entries.Any(entry => entry.EventName == "stationary_combat.path_combat.access_path_needed"),
            "path combat should log access path handoff");
        AssertFalse(!logger.Entries.Any(entry => entry.EventName == "stationary_combat.startup_recovery.selected"),
            "path combat access path should reuse startup recovery");
        AssertFalse(logger.Entries.Any(entry => entry.EventName == "stationary_combat.path_combat.path_selected"),
            "combat path should not be selected before access path finishes");
    }
    finally
    {
        Environment.SetEnvironmentVariable("AION_FACE_TARGET_BEARING_MODE", previousBearingMode);
        Environment.SetEnvironmentVariable("ROADHOG_PATH_COMBAT_ACCESS_PATH_DISTANCE", previousAccessDistance);
    }
}

static async Task TestPathCombatStartsCombatPathAfterAccessPathCompletesAsync()
{
    var previousBearingMode = Environment.GetEnvironmentVariable("AION_FACE_TARGET_BEARING_MODE");
    var previousAccessDistance = Environment.GetEnvironmentVariable("ROADHOG_PATH_COMBAT_ACCESS_PATH_DISTANCE");
    Environment.SetEnvironmentVariable("AION_FACE_TARGET_BEARING_MODE", "y-x");
    Environment.SetEnvironmentVariable("ROADHOG_PATH_COMBAT_ACCESS_PATH_DISTANCE", "120");
    try
    {
        var settings = CreateScriptSettings();
        settings.MainMode = AccountMainMode.CustomCombat;
        settings.CombatMode = AccountCombatMode.Path;
        settings.Paths.RevivePathName = "revive-a";
        settings.Paths.CombatPathName = "combat-a";
        settings.Combat.StationaryCombatRadius = 8;

        var revivePoints = new[]
        {
            new Vector3Snapshot(0, 0, 0),
            new Vector3Snapshot(100, 0, 0),
            new Vector3Snapshot(200, 0, 0)
        };
        var pathStore = new InMemorySharedPathStore(
            CreatePath("revive-a", revivePoints),
            CreatePath("combat-a",
                new Vector3Snapshot(206, 0, 0),
                new Vector3Snapshot(215, 0, 0)));
        var keyboard = new RecordingKeyboardInput();
        var logger = new InMemoryRoadhogLogger();
        var gameApi = new FakeGameApi
        {
            Player = new PlayerSnapshot(1, 0, "Fake", 100, 100, 100, 100, 0, new Vector3Snapshot(200, 0, 0), DateTimeOffset.Now, 90, 10, 90),
            TargetEntityId = 0,
            TargetCurrentHp = 0,
            TargetPosition = null,
            WorldObjects = Array.Empty<WorldObjectSnapshot>(),
            Skills = CreateSkillSnapshotsById(new Dictionary<uint, uint>())
        };
        var semiAuto = new SemiAutoCombatController(keyboard);
        var controller = new StationaryCombatController(keyboard, semiAuto, pathStore);
        var state = new StationaryCombatState();
        state.StartStartupRecovery("revive-a", revivePoints, 2);
        var context = CreateContext(settings, gameApi, logger);

        await controller
            .TickPathAsync(context, SemiAutoSkillPlan.FromSettings(settings.Skills), new SemiAutoCombatState(), state)
            .ConfigureAwait(false);

        AssertFalse(state.StartupRecoveryActive, "completed access path should be cleared");
        AssertFalse(!state.PathCombat.Active, "combat path should start after access path completes");
        AssertEqual(0, state.PathCombat.PointIndex, "combat path should start from nearest combat point after access");
        AssertFalse(!logger.Entries.Any(entry => entry.EventName == "stationary_combat.startup_recovery.complete"),
            "access path completion should be logged");
        AssertFalse(!logger.Entries.Any(entry => entry.EventName == "stationary_combat.path_combat.path_selected"),
            "combat path should be selected after access path completes");
    }
    finally
    {
        Environment.SetEnvironmentVariable("AION_FACE_TARGET_BEARING_MODE", previousBearingMode);
        Environment.SetEnvironmentVariable("ROADHOG_PATH_COMBAT_ACCESS_PATH_DISTANCE", previousAccessDistance);
    }
}

static async Task TestPathCombatUsesConfiguredRadiusBeforeClearingMonstersAsync()
{
    var previousBearingMode = Environment.GetEnvironmentVariable("AION_FACE_TARGET_BEARING_MODE");
    Environment.SetEnvironmentVariable("AION_FACE_TARGET_BEARING_MODE", "y-x");
    try
    {
        var settings = CreateScriptSettings();
        settings.MainMode = AccountMainMode.CustomCombat;
        settings.CombatMode = AccountCombatMode.Path;
        settings.Paths.CombatPathName = "combat-a";
        settings.SemiAuto.AttackKeyLoopEnabled = true;
        settings.SemiAuto.AttackKeyLoopIntervalMs = 1;
        settings.Combat.EnableLoot = false;
        settings.Combat.StationaryCombatRadius = 1;
        settings.Combat.PathCombatRadius = 3;

        var pathStore = new InMemorySharedPathStore(
            CreatePath("combat-a",
                new Vector3Snapshot(0, 0, 0),
                new Vector3Snapshot(10, 0, 0)));
        var keyboard = new RecordingKeyboardInput();
        var logger = new InMemoryRoadhogLogger();
        var gameApi = new FakeGameApi
        {
            Player = new PlayerSnapshot(1, 0, "Fake", 100, 100, 100, 100, 0, new Vector3Snapshot(0, 0, 0), DateTimeOffset.Now, 90, 10, 90),
            TargetEntityId = 220,
            TargetOwnServerObjectId = 2200,
            TargetCurrentHp = 1000,
            TargetMaxHp = 1000,
            TargetPosition = new Vector3Snapshot(4, 0, 0),
            TargetServerObjectId = 0,
            TargetIsTargetingLocalPlayer = false,
            WorldObjects = new[]
            {
                new WorldObjectSnapshot(
                    220,
                    2200,
                    "path-passive-inside-five",
                    "monster",
                    new Vector3Snapshot(4, 0, 0),
                    4,
                    1000,
                    1000,
                    AggressiveKnown: true,
                    IsAggressiveToPlayer: false)
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
        var semiAutoState = new SemiAutoCombatState();
        var plan = SemiAutoSkillPlan.FromSettings(settings.Skills);
        var context = CreateContext(settings, gameApi, logger);

        await controller.TickPathAsync(context, plan, semiAutoState, state).ConfigureAwait(false);

        AssertFalse(state.Fighting, "monster outside configured path radius should not be selected");
        AssertFalse(!state.PathCombat.Active, "path combat should stay active while no target is in radius");
        AssertEqual(1, state.PathCombat.PointIndex, "path combat should advance from nearest point to next waypoint");
        AssertFalse(!keyboard.KeyDowns.Contains("W"), "path combat should keep walking when target is outside radius");

        settings.Combat.PathCombatRadius = 5;
        keyboard.Keys.Clear();
        keyboard.KeyUps.Clear();

        await controller.TickPathAsync(context, plan, semiAutoState, state).ConfigureAwait(false);

        AssertFalse(!state.Fighting, "monster inside configured path radius should interrupt path into combat");
        AssertEqual((ushort)220, state.CandidateEntityId, "path combat should select the monster inside path UI radius");
        AssertFalse(state.CurrentTargetIsRevivePathClear, "path combat target should not be marked as revive path clear");
        AssertFalse(!keyboard.KeyUps.Contains("W"), "path combat should stop path movement before fighting");
        AssertFalse(!keyboard.Keys.Contains("C"), "path combat should press opening attack key while waiting for target aggro");
        AssertFalse(!logger.Entries.Any(entry =>
                entry.EventName == "stationary_combat.path_combat.target_selected" &&
                Convert.ToString(entry.Fields["targetName"]) == "path-passive-inside-five"),
            "path combat target selection should be logged");
    }
    finally
    {
        Environment.SetEnvironmentVariable("AION_FACE_TARGET_BEARING_MODE", previousBearingMode);
    }
}

static async Task TestPathCombatUsesConfiguredPathFollowPrecisionAsync()
{
    var previousBearingMode = Environment.GetEnvironmentVariable("AION_FACE_TARGET_BEARING_MODE");
    Environment.SetEnvironmentVariable("AION_FACE_TARGET_BEARING_MODE", "y-x");
    try
    {
        var settings = CreateScriptSettings();
        settings.MainMode = AccountMainMode.CustomCombat;
        settings.CombatMode = AccountCombatMode.Path;
        settings.Paths.CombatPathName = "combat-a";
        settings.Combat.PathFollowReachDistance = 7;

        var pathStore = new InMemorySharedPathStore(
            CreatePath("combat-a",
                new Vector3Snapshot(0, 0, 0),
                new Vector3Snapshot(20, 0, 0)));
        var keyboard = new RecordingKeyboardInput();
        var logger = new InMemoryRoadhogLogger();
        var gameApi = new FakeGameApi
        {
            Player = new PlayerSnapshot(1, 0, "Fake", 100, 100, 100, 100, 0, new Vector3Snapshot(6, 0, 0), DateTimeOffset.Now, 90, 10, 90),
            TargetEntityId = 0,
            TargetCurrentHp = 0,
            TargetPosition = null,
            WorldObjects = Array.Empty<WorldObjectSnapshot>(),
            Skills = CreateSkillSnapshotsById(new Dictionary<uint, uint>())
        };
        var semiAuto = new SemiAutoCombatController(keyboard);
        var controller = new StationaryCombatController(keyboard, semiAuto, pathStore);
        var state = new StationaryCombatState();
        var context = CreateContext(settings, gameApi, logger);

        await controller
            .TickPathAsync(context, SemiAutoSkillPlan.FromSettings(settings.Skills), new SemiAutoCombatState(), state)
            .ConfigureAwait(false);

        AssertFalse(!state.PathCombat.Active, "path combat should start with configured path");
        AssertEqual(1, state.PathCombat.PointIndex, "configured precision should treat first waypoint as reached");
        AssertFalse(!keyboard.KeyDowns.Contains("W"), "path combat should walk toward the next waypoint");
        AssertFalse(!(gameApi.LastPlayerContext?.BypassMemoryCache ?? false), "path follow player read should bypass VMM cache before holding W");
        AssertFalse(!logger.Entries.Any(entry =>
                entry.EventName == "stationary_combat.path_combat.point_reached" &&
                Convert.ToInt32(entry.Fields["pointIndex"]) == 0),
            "path combat should log the first waypoint as reached inside configured precision");
    }
    finally
    {
        Environment.SetEnvironmentVariable("AION_FACE_TARGET_BEARING_MODE", previousBearingMode);
    }
}

static async Task TestPathCombatNoKillReturnStartsRevivePathAtFirstPointAsync()
{
    var previousTimeout = Environment.GetEnvironmentVariable("ROADHOG_NO_KILL_RETURN_TIMEOUT_MS");
    var previousSettle = Environment.GetEnvironmentVariable("ROADHOG_NO_KILL_RETURN_SETTLE_MS");
    var previousMinDistance = Environment.GetEnvironmentVariable("ROADHOG_NO_KILL_RETURN_MIN_DISTANCE");
    var previousRetry = Environment.GetEnvironmentVariable("ROADHOG_NO_KILL_RETURN_RETRY_MS");
    Environment.SetEnvironmentVariable("ROADHOG_NO_KILL_RETURN_TIMEOUT_MS", "1200000");
    Environment.SetEnvironmentVariable("ROADHOG_NO_KILL_RETURN_SETTLE_MS", "0");
    Environment.SetEnvironmentVariable("ROADHOG_NO_KILL_RETURN_MIN_DISTANCE", "5");
    Environment.SetEnvironmentVariable("ROADHOG_NO_KILL_RETURN_RETRY_MS", "60000");
    try
    {
        var settings = CreateScriptSettings();
        settings.MainMode = AccountMainMode.CustomCombat;
        settings.CombatMode = AccountCombatMode.Path;
        settings.Paths.TownReturnKey = "NumPad7";
        settings.Paths.RevivePathName = "revive-a";
        settings.Paths.CombatPathName = "combat-a";
        var pathStore = new InMemorySharedPathStore(
            CreatePath("revive-a",
                new Vector3Snapshot(100, 0, 0),
                new Vector3Snapshot(200, 0, 0),
                new Vector3Snapshot(300, 0, 0)),
            CreatePath("combat-a",
                new Vector3Snapshot(300, 0, 0),
                new Vector3Snapshot(320, 0, 0)));
        var keyboard = new RecordingKeyboardInput();
        var logger = new InMemoryRoadhogLogger();
        var runtimeStates = new AccountRuntimeManager(logger);
        runtimeStates.GetOrCreate("account1");
        var gameApi = new FakeGameApi
        {
            Player = new PlayerSnapshot(
                1,
                100,
                "Fake",
                100,
                100,
                100,
                100,
                0,
                new Vector3Snapshot(900, 0, 0),
                DateTimeOffset.Now,
                90,
                10,
                90),
            WorldObjects = Array.Empty<WorldObjectSnapshot>(),
            Skills = Array.Empty<SkillSnapshot>()
        };
        var semiAuto = new SemiAutoCombatController(keyboard);
        var controller = new StationaryCombatController(keyboard, semiAuto, pathStore);
        var state = new StationaryCombatState();
        state.NoKillRecovery.ObserveCombatActivity(null, DateTimeOffset.Now.AddMinutes(-21));
        var plan = SemiAutoSkillPlan.FromSettings(settings.Skills);
        var context = CreateContext(settings, gameApi, logger, runtimeStates);

        await controller.TickPathAsync(context, plan, new SemiAutoCombatState(), state).ConfigureAwait(false);

        AssertSequence(new[] { "NumPad7" }, keyboard.Keys.ToArray(), "twenty-one minute kill gap should press town return once");
        AssertEqual(
            StationaryCombatNoKillRecoveryStep.WaitTownReturnSettle,
            state.NoKillRecovery.Step,
            "no-kill recovery should wait for town return");

        gameApi.Player = gameApi.Player with { Position = new Vector3Snapshot(100, 0, 0) };
        await controller.TickPathAsync(context, plan, new SemiAutoCombatState(), state).ConfigureAwait(false);

        AssertEqual(
            StationaryCombatNoKillRecoveryStep.FollowRevivePath,
            state.NoKillRecovery.Step,
            "verified town return should begin revive path");
        AssertEqual(1, state.StartupRecoveryPointIndex, "revive path should begin at point zero before advancing");
        var verifyLog = logger.Entries.Last(entry => entry.EventName == "stationary_combat.no_kill.return.verify.ok");
        AssertEqual(0, Convert.ToInt32(verifyLog.Fields["startPointIndex"]), "no-kill recovery start point index");

        gameApi.Player = gameApi.Player with { Position = new Vector3Snapshot(200, 0, 0) };
        await controller.TickPathAsync(context, plan, new SemiAutoCombatState(), state).ConfigureAwait(false);
        gameApi.Player = gameApi.Player with { Position = new Vector3Snapshot(300, 0, 0) };
        await controller.TickPathAsync(context, plan, new SemiAutoCombatState(), state).ConfigureAwait(false);

        AssertFalse(state.NoKillRecovery.Active, "completed revive path should end no-kill recovery");
        AssertEqual(1, keyboard.Keys.Count(key => key == "NumPad7"), "completed recovery should reset the twenty minute watch");
        AssertFalse(
            !logger.Entries.Any(entry => entry.EventName == "stationary_combat.no_kill.recovery.complete"),
            "completed no-kill recovery should be logged");
    }
    finally
    {
        Environment.SetEnvironmentVariable("ROADHOG_NO_KILL_RETURN_TIMEOUT_MS", previousTimeout);
        Environment.SetEnvironmentVariable("ROADHOG_NO_KILL_RETURN_SETTLE_MS", previousSettle);
        Environment.SetEnvironmentVariable("ROADHOG_NO_KILL_RETURN_MIN_DISTANCE", previousMinDistance);
        Environment.SetEnvironmentVariable("ROADHOG_NO_KILL_RETURN_RETRY_MS", previousRetry);
    }
}

static async Task TestPathCombatRecentKillPreventsNoKillReturnAsync()
{
    var previousTimeout = Environment.GetEnvironmentVariable("ROADHOG_NO_KILL_RETURN_TIMEOUT_MS");
    Environment.SetEnvironmentVariable("ROADHOG_NO_KILL_RETURN_TIMEOUT_MS", "1200000");
    try
    {
        var settings = CreateScriptSettings();
        settings.MainMode = AccountMainMode.CustomCombat;
        settings.CombatMode = AccountCombatMode.Path;
        settings.Paths.TownReturnKey = "NumPad7";
        settings.Paths.RevivePathName = "revive-a";
        settings.Paths.CombatPathName = "combat-a";
        var pathStore = new InMemorySharedPathStore(
            CreatePath("revive-a", new Vector3Snapshot(0, 0, 0), new Vector3Snapshot(100, 0, 0)),
            CreatePath("combat-a", new Vector3Snapshot(100, 0, 0), new Vector3Snapshot(120, 0, 0)));
        var keyboard = new RecordingKeyboardInput();
        var logger = new InMemoryRoadhogLogger();
        var runtimeStates = new AccountRuntimeManager(logger);
        runtimeStates.GetOrCreate("account1");
        runtimeStates.MarkKill("account1", 100, 1000, DateTimeOffset.Now);
        var gameApi = new FakeGameApi
        {
            Player = new PlayerSnapshot(
                1, 100, "Fake", 100, 100, 100, 100, 0,
                new Vector3Snapshot(0, 0, 0), DateTimeOffset.Now, 90, 10, 90),
            WorldObjects = Array.Empty<WorldObjectSnapshot>(),
            Skills = Array.Empty<SkillSnapshot>()
        };
        var semiAuto = new SemiAutoCombatController(keyboard);
        var controller = new StationaryCombatController(keyboard, semiAuto, pathStore);

        await controller
            .TickPathAsync(
                CreateContext(settings, gameApi, logger, runtimeStates),
                SemiAutoSkillPlan.FromSettings(settings.Skills),
                new SemiAutoCombatState(),
                new StationaryCombatState())
            .ConfigureAwait(false);

        AssertFalse(keyboard.Keys.Contains("NumPad7"), "recent kill should keep the no-kill return inactive");
        AssertFalse(
            logger.Entries.Any(entry => entry.EventName == "stationary_combat.no_kill.return.press"),
            "recent kill should not log a town return");
    }
    finally
    {
        Environment.SetEnvironmentVariable("ROADHOG_NO_KILL_RETURN_TIMEOUT_MS", previousTimeout);
    }
}

static async Task TestPathCombatFailedNoKillReturnWaitsBeforeRetryAsync()
{
    var previousTimeout = Environment.GetEnvironmentVariable("ROADHOG_NO_KILL_RETURN_TIMEOUT_MS");
    var previousSettle = Environment.GetEnvironmentVariable("ROADHOG_NO_KILL_RETURN_SETTLE_MS");
    var previousMinDistance = Environment.GetEnvironmentVariable("ROADHOG_NO_KILL_RETURN_MIN_DISTANCE");
    var previousRetry = Environment.GetEnvironmentVariable("ROADHOG_NO_KILL_RETURN_RETRY_MS");
    Environment.SetEnvironmentVariable("ROADHOG_NO_KILL_RETURN_TIMEOUT_MS", "1200000");
    Environment.SetEnvironmentVariable("ROADHOG_NO_KILL_RETURN_SETTLE_MS", "0");
    Environment.SetEnvironmentVariable("ROADHOG_NO_KILL_RETURN_MIN_DISTANCE", "5");
    Environment.SetEnvironmentVariable("ROADHOG_NO_KILL_RETURN_RETRY_MS", "60000");
    try
    {
        var settings = CreateScriptSettings();
        settings.MainMode = AccountMainMode.CustomCombat;
        settings.CombatMode = AccountCombatMode.Path;
        settings.Paths.TownReturnKey = "NumPad7";
        settings.Paths.RevivePathName = "revive-a";
        settings.Paths.CombatPathName = "combat-a";
        var pathStore = new InMemorySharedPathStore(
            CreatePath("revive-a", new Vector3Snapshot(0, 0, 0), new Vector3Snapshot(100, 0, 0)),
            CreatePath("combat-a", new Vector3Snapshot(100, 0, 0), new Vector3Snapshot(120, 0, 0)));
        var keyboard = new RecordingKeyboardInput();
        var logger = new InMemoryRoadhogLogger();
        var runtimeStates = new AccountRuntimeManager(logger);
        runtimeStates.GetOrCreate("account1");
        runtimeStates.MarkKill("account1", 100, 1000, DateTimeOffset.Now.AddMinutes(-21));
        var gameApi = new FakeGameApi
        {
            Player = new PlayerSnapshot(
                1, 100, "Fake", 100, 100, 100, 100, 0,
                new Vector3Snapshot(500, 0, 0), DateTimeOffset.Now, 90, 10, 90),
            WorldObjects = Array.Empty<WorldObjectSnapshot>(),
            Skills = Array.Empty<SkillSnapshot>()
        };
        var semiAuto = new SemiAutoCombatController(keyboard);
        var controller = new StationaryCombatController(keyboard, semiAuto, pathStore);
        var state = new StationaryCombatState();
        var plan = SemiAutoSkillPlan.FromSettings(settings.Skills);
        var context = CreateContext(settings, gameApi, logger, runtimeStates);

        await controller.TickPathAsync(context, plan, new SemiAutoCombatState(), state).ConfigureAwait(false);
        await controller.TickPathAsync(context, plan, new SemiAutoCombatState(), state).ConfigureAwait(false);
        await controller.TickPathAsync(context, plan, new SemiAutoCombatState(), state).ConfigureAwait(false);

        AssertFalse(state.NoKillRecovery.Active, "unchanged position should stop the failed return attempt");
        AssertEqual(1, keyboard.Keys.Count(key => key == "NumPad7"), "failed return should wait before pressing again");
        var postponed = logger.Entries.Last(entry => entry.EventName == "stationary_combat.no_kill.recovery.postponed");
        AssertEqual(
            "town_return_position_unchanged",
            Convert.ToString(postponed.Fields["reason"]) ?? string.Empty,
            "failed town return reason");
    }
    finally
    {
        Environment.SetEnvironmentVariable("ROADHOG_NO_KILL_RETURN_TIMEOUT_MS", previousTimeout);
        Environment.SetEnvironmentVariable("ROADHOG_NO_KILL_RETURN_SETTLE_MS", previousSettle);
        Environment.SetEnvironmentVariable("ROADHOG_NO_KILL_RETURN_MIN_DISTANCE", previousMinDistance);
        Environment.SetEnvironmentVariable("ROADHOG_NO_KILL_RETURN_RETRY_MS", previousRetry);
    }
}

static async Task TestPathCombatResumesPathAfterKillAsync()
{
    var previousBearingMode = Environment.GetEnvironmentVariable("AION_FACE_TARGET_BEARING_MODE");
    Environment.SetEnvironmentVariable("AION_FACE_TARGET_BEARING_MODE", "y-x");
    try
    {
        var settings = CreateScriptSettings();
        settings.MainMode = AccountMainMode.CustomCombat;
        settings.CombatMode = AccountCombatMode.Path;
        settings.Paths.CombatPathName = "combat-a";
        settings.SemiAuto.AttackKeyLoopEnabled = true;
        settings.SemiAuto.AttackKeyLoopIntervalMs = 1;
        settings.Combat.EnableLoot = false;
        settings.Combat.StationaryCombatRadius = 8;

        var pathStore = new InMemorySharedPathStore(
            CreatePath("combat-a",
                new Vector3Snapshot(0, 0, 0),
                new Vector3Snapshot(10, 0, 0)));
        var keyboard = new RecordingKeyboardInput();
        var logger = new InMemoryRoadhogLogger();
        var gameApi = new FakeGameApi
        {
            Player = new PlayerSnapshot(1, 0, "Fake", 100, 100, 100, 100, 0, new Vector3Snapshot(0, 0, 0), DateTimeOffset.Now, 90, 10, 90),
            TargetEntityId = 230,
            TargetOwnServerObjectId = 2300,
            TargetCurrentHp = 1000,
            TargetMaxHp = 1000,
            TargetPosition = new Vector3Snapshot(4, 0, 0),
            TargetServerObjectId = 0,
            TargetIsTargetingLocalPlayer = false,
            WorldObjects = new[]
            {
                new WorldObjectSnapshot(
                    230,
                    2300,
                    "path-target",
                    "monster",
                    new Vector3Snapshot(4, 0, 0),
                    4,
                    1000,
                    1000,
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
        var semiAutoState = new SemiAutoCombatState();
        var plan = SemiAutoSkillPlan.FromSettings(settings.Skills);
        var context = CreateContext(settings, gameApi, logger);

        await controller.TickPathAsync(context, plan, semiAutoState, state).ConfigureAwait(false);

        AssertFalse(!state.Fighting, "path combat should enter fighting before kill");
        AssertEqual(0, state.PathCombat.PointIndex, "path combat should keep nearest path cursor while fighting");

        gameApi.TargetCurrentHp = 0;
        gameApi.WorldObjects = Array.Empty<WorldObjectSnapshot>();
        keyboard.KeyDowns.Clear();
        keyboard.Keys.Clear();

        await controller.TickPathAsync(context, plan, semiAutoState, state).ConfigureAwait(false);

        AssertFalse(state.Fighting, "dead target should clear current fight");
        AssertFalse(!logger.Entries.Any(entry => entry.EventName == "stationary_combat.target.kill_counted"),
            "path combat kill should reuse existing kill counting");

        await controller.TickPathAsync(context, plan, semiAutoState, state).ConfigureAwait(false);

        AssertFalse(!state.PathCombat.Active, "path combat should remain active after kill");
        AssertEqual(1, state.PathCombat.PointIndex, "path combat should resume from previous waypoint");
        AssertFalse(!keyboard.KeyDowns.Contains("W"), "path combat should continue walking after kill");
    }
    finally
    {
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
        settings.Paths.DeathReviveClickX = 701;
        settings.Paths.DeathReviveClickY = 402;
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
            new[] { "move:-2000,-2000", "move:-2000,-2000", "move:701,402", "down:Left", "up:Left" },
            keyboard.MouseCommands.Take(5).ToArray(),
            "semi-auto death guard should absolute-click configured revive button");
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

static async Task TestStationaryCombatResetsRightMouseAfterRepeatedUnchangedTurnsAsync()
{
    var environment = new Dictionary<string, string?>
    {
        ["AION_FACE_TARGET_BEARING_MODE"] = Environment.GetEnvironmentVariable("AION_FACE_TARGET_BEARING_MODE"),
        ["AION_FACE_TARGET_SETTLE_MS"] = Environment.GetEnvironmentVariable("AION_FACE_TARGET_SETTLE_MS"),
        ["AION_FACE_TARGET_ADAPTIVE_READ_SETTLE_MS"] = Environment.GetEnvironmentVariable("AION_FACE_TARGET_ADAPTIVE_READ_SETTLE_MS"),
        ["AION_FACE_TARGET_ADAPTIVE_READ_TIMEOUT_MS"] = Environment.GetEnvironmentVariable("AION_FACE_TARGET_ADAPTIVE_READ_TIMEOUT_MS"),
        ["AION_FACE_TARGET_DRAG_STEP_DELAY_MS"] = Environment.GetEnvironmentVariable("AION_FACE_TARGET_DRAG_STEP_DELAY_MS")
    };
    Environment.SetEnvironmentVariable("AION_FACE_TARGET_BEARING_MODE", "y-x");
    Environment.SetEnvironmentVariable("AION_FACE_TARGET_SETTLE_MS", "0");
    Environment.SetEnvironmentVariable("AION_FACE_TARGET_ADAPTIVE_READ_SETTLE_MS", "0");
    Environment.SetEnvironmentVariable("AION_FACE_TARGET_ADAPTIVE_READ_TIMEOUT_MS", "0");
    Environment.SetEnvironmentVariable("AION_FACE_TARGET_DRAG_STEP_DELAY_MS", "0");
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
        var semiAutoState = new SemiAutoCombatState();
        var state = new StationaryCombatState();
        var context = CreateContext(settings, gameApi, logger);

        await controller.TickAsync(context, plan, semiAutoState, state).ConfigureAwait(false);
        await controller.TickAsync(context, plan, semiAutoState, state).ConfigureAwait(false);

        AssertFalse(
            logger.Entries.Any(entry => entry.EventName == "stationary_combat.right_mouse.recovered"),
            "right mouse recovery should not run before the third unchanged turn");
        AssertEqual(2, state.ConsecutiveCameraTurnNoChangeCount, "unchanged turn count before recovery");

        await controller.TickAsync(context, plan, semiAutoState, state).ConfigureAwait(false);

        var recovery = logger.Entries.SingleOrDefault(entry => entry.EventName == "stationary_combat.right_mouse.recovered");
        AssertFalse(recovery is null, "third unchanged turn should force right mouse recovery");
        AssertEqual(3, Convert.ToInt32(recovery!.Fields["consecutiveFailures"]), "right mouse recovery failure threshold");
        AssertEqual(0, state.ConsecutiveCameraTurnNoChangeCount, "successful recovery should reset unchanged turn count");
    }
    finally
    {
        foreach (var pair in environment)
        {
            Environment.SetEnvironmentVariable(pair.Key, pair.Value);
        }
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

static async Task TestStationaryCombatAcceptsCloserAggressiveWrongLockAfterTabAsync()
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
            TargetEntityId = 0,
            TargetCurrentHp = 0,
            TargetMaxHp = 0,
            TargetName = string.Empty,
            TargetPosition = null,
            TargetServerObjectId = 0,
            TargetIsTargetingLocalPlayer = false,
            WorldObjects = new[]
            {
                new WorldObjectSnapshot(100, 100, "candidate", "monster", new Vector3Snapshot(20, 0, 0), 20, 1000, 1000, AggressiveKnown: true, IsAggressiveToPlayer: true)
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
                if (!string.Equals(key, "Tab", StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                gameApi.TargetEntityId = 200;
                gameApi.TargetCurrentHp = 1000;
                gameApi.TargetMaxHp = 1000;
                gameApi.TargetName = "near-aggressive";
                gameApi.TargetPosition = new Vector3Snapshot(5, 0, 0);
                gameApi.TargetServerObjectId = 0;
                gameApi.TargetIsTargetingLocalPlayer = false;
                gameApi.WorldObjects = new[]
                {
                    new WorldObjectSnapshot(100, 100, "candidate", "monster", new Vector3Snapshot(20, 0, 0), 20, 1000, 1000, AggressiveKnown: true, IsAggressiveToPlayer: true),
                    new WorldObjectSnapshot(200, 200, "near-aggressive", "monster", new Vector3Snapshot(5, 0, 0), 5, 1000, 1000, AggressiveKnown: true, IsAggressiveToPlayer: true)
                };
            }
        };
        var semiAuto = new SemiAutoCombatController(keyboard);
        var controller = new StationaryCombatController(keyboard, semiAuto);
        var plan = SemiAutoSkillPlan.FromSettings(settings.Skills);
        var state = new StationaryCombatState();

        await controller
            .TickAsync(CreateContext(settings, gameApi, logger), plan, new SemiAutoCombatState(), state)
            .ConfigureAwait(false);

        AssertFalse(!keyboard.Keys.Contains("Tab"), "acquire tick should press Tab");
        AssertFalse(keyboard.Keys.Contains("W"), "closer aggressive wrong lock should not nudge forward");
        AssertFalse(!keyboard.Keys.Contains("D2"), "closer aggressive wrong lock should enter skill release");
        AssertFalse(!state.Fighting, "closer aggressive wrong lock should enter fight state");
        AssertEqual((ushort)200, state.CurrentTargetEntityId, "current target should switch to closer aggressive lock");
        AssertEqual((ushort)200, state.CandidateEntityId, "candidate should switch to closer aggressive lock");
        AssertFalse(!logger.Entries.Any(entry =>
            entry.EventName == "stationary_combat.target.accept_nearby_aggressive_lock" &&
            Equals(Convert.ToUInt16(entry.Fields["candidateEntityId"]), (ushort)100) &&
            Equals(Convert.ToUInt16(entry.Fields["lockedEntityId"]), (ushort)200)),
            "nearby aggressive lock acceptance should be logged");
        AssertFalse(!logger.Entries.Any(entry =>
            entry.EventName == "stationary_combat.target.acquired" &&
            Equals(Convert.ToUInt16(entry.Fields["targetEntityId"]), (ushort)200) &&
            string.Equals(Convert.ToString(entry.Fields["phase"]), "after_tab_aggressive", StringComparison.Ordinal)),
            "accepted nearby aggressive target should be acquired");
    }
    finally
    {
        Environment.SetEnvironmentVariable("AION_FACE_TARGET_BEARING_MODE", previousBearingMode);
        Environment.SetEnvironmentVariable("ROADHOG_STATIONARY_TAB_VERIFY_DELAY_MS", previousTabDelay);
    }
}

static async Task TestStationaryCombatRejectsCloserPassiveWrongLockAfterTabAsync()
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
            TargetEntityId = 0,
            TargetCurrentHp = 0,
            TargetMaxHp = 0,
            TargetName = string.Empty,
            TargetPosition = null,
            TargetServerObjectId = 0,
            TargetIsTargetingLocalPlayer = false,
            WorldObjects = new[]
            {
                new WorldObjectSnapshot(100, 100, "candidate", "monster", new Vector3Snapshot(20, 0, 0), 20, 1000, 1000, AggressiveKnown: true, IsAggressiveToPlayer: true)
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
                if (!string.Equals(key, "Tab", StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                gameApi.TargetEntityId = 200;
                gameApi.TargetCurrentHp = 1000;
                gameApi.TargetMaxHp = 1000;
                gameApi.TargetName = "near-passive";
                gameApi.TargetPosition = new Vector3Snapshot(5, 0, 0);
                gameApi.TargetServerObjectId = 0;
                gameApi.TargetIsTargetingLocalPlayer = false;
                gameApi.WorldObjects = new[]
                {
                    new WorldObjectSnapshot(100, 100, "candidate", "monster", new Vector3Snapshot(20, 0, 0), 20, 1000, 1000, AggressiveKnown: true, IsAggressiveToPlayer: true),
                    new WorldObjectSnapshot(200, 200, "near-passive", "monster", new Vector3Snapshot(5, 0, 0), 5, 1000, 1000, AggressiveKnown: true, IsAggressiveToPlayer: false)
                };
            }
        };
        var semiAuto = new SemiAutoCombatController(keyboard);
        var controller = new StationaryCombatController(keyboard, semiAuto);
        var plan = SemiAutoSkillPlan.FromSettings(settings.Skills);
        var state = new StationaryCombatState();

        await controller
            .TickAsync(CreateContext(settings, gameApi, logger), plan, new SemiAutoCombatState(), state)
            .ConfigureAwait(false);

        AssertFalse(!keyboard.Keys.Contains("Tab"), "acquire tick should press Tab");
        AssertFalse(keyboard.Keys.Contains("D2"), "closer passive wrong lock must not enter skill release");
        AssertFalse(state.Fighting, "closer passive wrong lock must not enter fight state");
        AssertEqual((ushort)100, state.CandidateEntityId, "closer passive wrong lock should keep original candidate");
        AssertFalse(logger.Entries.Any(entry => entry.EventName == "stationary_combat.target.accept_nearby_aggressive_lock"),
            "passive wrong lock should not log nearby aggressive acceptance");
        AssertFalse(logger.Entries.Any(entry => entry.EventName == "stationary_combat.target.switched_to_locked"),
            "passive wrong lock should not switch to locked target");
    }
    finally
    {
        Environment.SetEnvironmentVariable("AION_FACE_TARGET_BEARING_MODE", previousBearingMode);
        Environment.SetEnvironmentVariable("ROADHOG_STATIONARY_TAB_VERIFY_DELAY_MS", previousTabDelay);
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

static async Task TestStationaryCombatNudgesForwardWhenTabStaysOnAttemptedCorpseAsync()
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
            TargetLootableRaw = 1,
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
        state.MarkLootCorpseAttempted(200, 200, DateTimeOffset.Now);
        var context = CreateContext(settings, gameApi, logger);

        await controller
            .TickAsync(context, plan, new SemiAutoCombatState(), state)
            .ConfigureAwait(false);

        AssertSequence(new[] { "Tab", "W" }, keyboard.Keys, "attempted corpse should still nudge when it blocks tab lock");
        AssertFalse(keyboard.Keys.Contains("D2"), "attempted corpse lock must not release skills");
        AssertFalse(!logger.Entries.Any(entry =>
            entry.EventName == "stationary_combat.tab.corpse_nudge_pressed" &&
            string.Equals(Convert.ToString(entry.Fields["reason"]), "already_attempted", StringComparison.Ordinal)),
            "attempted corpse nudge should be logged with reason");

        await controller
            .TickAsync(context, plan, new SemiAutoCombatState(), state)
            .ConfigureAwait(false);

        AssertEqual(1, keyboard.Keys.Count(key => string.Equals(key, "W", StringComparison.OrdinalIgnoreCase)), "attempted corpse should nudge only once per pending tab verify");
    }
    finally
    {
        Environment.SetEnvironmentVariable("AION_FACE_TARGET_BEARING_MODE", previousBearingMode);
        Environment.SetEnvironmentVariable("ROADHOG_STATIONARY_TAB_VERIFY_DELAY_MS", previousTabDelay);
    }
}

static async Task TestStationaryCombatNudgesForwardWhenTabLockIsEmptyAsync()
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
            TargetEntityId = 0,
            TargetCurrentHp = 0,
            TargetMaxHp = 0,
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

        AssertSequence(new[] { "Tab", "W" }, keyboard.Keys, "empty tab lock should nudge forward");
        AssertFalse(keyboard.Keys.Contains("D2"), "empty lock must not release skills");
        AssertFalse(!logger.Entries.Any(entry =>
            entry.EventName == "stationary_combat.tab.lock_miss_nudge_pressed" &&
            string.Equals(Convert.ToString(entry.Fields["reason"]), "empty_lock", StringComparison.Ordinal)),
            "empty lock nudge should be logged");

        await controller
            .TickAsync(context, plan, new SemiAutoCombatState(), state)
            .ConfigureAwait(false);

        AssertEqual(1, keyboard.Keys.Count(key => string.Equals(key, "W", StringComparison.OrdinalIgnoreCase)), "empty lock should nudge only once per pending tab verify");
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

static async Task TestStationaryCombatIgnoresNoDamageNoTargetingTargetAsync()
{
    var previousTimeout = Environment.GetEnvironmentVariable("ROADHOG_NO_DAMAGE_NO_TARGETING_TIMEOUT_MS");
    try
    {
        Environment.SetEnvironmentVariable("ROADHOG_NO_DAMAGE_NO_TARGETING_TIMEOUT_MS", "1");
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
            TargetOwnServerObjectId = 100,
            TargetCurrentHp = 1000,
            TargetMaxHp = 1000,
            TargetPosition = new Vector3Snapshot(8, 0, 0),
            TargetServerObjectId = 0,
            TargetIsTargetingLocalPlayer = false,
            WorldObjects = new[]
            {
                new WorldObjectSnapshot(100, 100, "idle", "monster", new Vector3Snapshot(8, 0, 0), 8, 1000, 1000)
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
        var state = new StationaryCombatState { Fighting = true };
        state.SetCurrentTarget(100, 100);
        state.MarkCandidate(100, 100, DateTimeOffset.Now);
        var semiAutoState = new SemiAutoCombatState();
        var context = CreateContext(settings, gameApi, logger);

        await controller.TickAsync(context, plan, semiAutoState, state).ConfigureAwait(false);
        await Task.Delay(20).ConfigureAwait(false);
        await controller.TickAsync(context, plan, semiAutoState, state).ConfigureAwait(false);

        AssertFalse(state.Fighting, "no-damage no-targeting fight should clear fighting state");
        AssertFalse(!state.IsTargetIgnored(100, 100), "no-damage no-targeting target should be ignored");
        AssertFalse(!logger.Entries.Any(entry =>
            entry.EventName == "stationary_combat.target.ignored" &&
            string.Equals(Convert.ToString(entry.Fields["reason"]), "no_damage_no_targeting", StringComparison.Ordinal)),
            "no-damage no-targeting ignore should be logged");
    }
    finally
    {
        Environment.SetEnvironmentVariable("ROADHOG_NO_DAMAGE_NO_TARGETING_TIMEOUT_MS", previousTimeout);
    }
}

static async Task TestStationaryCombatKeepsNoTargetingTargetAfterDamageProgressAsync()
{
    var previousTimeout = Environment.GetEnvironmentVariable("ROADHOG_NO_DAMAGE_NO_TARGETING_TIMEOUT_MS");
    try
    {
        Environment.SetEnvironmentVariable("ROADHOG_NO_DAMAGE_NO_TARGETING_TIMEOUT_MS", "1");
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
            TargetOwnServerObjectId = 100,
            TargetCurrentHp = 1000,
            TargetMaxHp = 1000,
            TargetPosition = new Vector3Snapshot(8, 0, 0),
            TargetServerObjectId = 0,
            TargetIsTargetingLocalPlayer = false,
            WorldObjects = new[]
            {
                new WorldObjectSnapshot(100, 100, "damaged", "monster", new Vector3Snapshot(8, 0, 0), 8, 1000, 1000)
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
        var state = new StationaryCombatState { Fighting = true };
        state.SetCurrentTarget(100, 100);
        state.MarkCandidate(100, 100, DateTimeOffset.Now);
        var semiAutoState = new SemiAutoCombatState();
        var context = CreateContext(settings, gameApi, logger);

        await controller.TickAsync(context, plan, semiAutoState, state).ConfigureAwait(false);
        gameApi.TargetCurrentHp = 900;
        await Task.Delay(20).ConfigureAwait(false);
        await controller.TickAsync(context, plan, semiAutoState, state).ConfigureAwait(false);

        AssertFalse(!state.Fighting, "damaged no-targeting fight should continue");
        AssertFalse(state.IsTargetIgnored(100, 100), "damaged no-targeting target should not be ignored");
        AssertFalse(logger.Entries.Any(entry =>
            entry.EventName == "stationary_combat.target.ignored" &&
            string.Equals(Convert.ToString(entry.Fields["reason"]), "no_damage_no_targeting", StringComparison.Ordinal)),
            "damaged no-targeting target should not log no-damage ignore");
    }
    finally
    {
        Environment.SetEnvironmentVariable("ROADHOG_NO_DAMAGE_NO_TARGETING_TIMEOUT_MS", previousTimeout);
    }
}

static async Task TestStationaryCombatDefenseCanSelectIgnoredLocalTargetAsync()
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
        TargetOwnServerObjectId = 100,
        TargetCurrentHp = 1000,
        TargetMaxHp = 1000,
        TargetPosition = new Vector3Snapshot(8, 0, 0),
        TargetServerObjectId = 1,
        TargetIsTargetingLocalPlayer = true,
        WorldObjects = new[]
        {
            new WorldObjectSnapshot(100, 100, "ignored-local", "monster", new Vector3Snapshot(8, 0, 0), 8, 1000, 1000, 1, true)
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
    state.IgnoreTarget(100, 100);
    var semiAutoState = new SemiAutoCombatState();
    CalibrateCooldownClock(semiAutoState);

    await controller.TickAsync(CreateContext(settings, gameApi, logger), plan, semiAutoState, state).ConfigureAwait(false);

    AssertFalse(!state.Fighting, "ignored local-side target should be selected for defense");
    AssertEqual((ushort)100, state.CurrentTargetEntityId, "ignored local-side target entity");
    AssertEqual(100u, state.CurrentTargetServerObjectId, "ignored local-side target server id");
    AssertFalse(!state.CurrentTargetIsMaintenanceDefense, "ignored local-side target should be marked as defense");
    AssertFalse(!state.IsTargetIgnored(100, 100), "defense selection should not remove the ignored marker");
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

static async Task TestStationaryCombatClearsMissingCurrentFightTargetQuicklyAsync()
{
    var previousMissingTimeout = Environment.GetEnvironmentVariable("ROADHOG_MISSING_FIGHT_TARGET_TIMEOUT_MS");
    try
    {
        Environment.SetEnvironmentVariable("ROADHOG_MISSING_FIGHT_TARGET_TIMEOUT_MS", "1");

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
            TargetEntityId = 0,
            TargetOwnServerObjectId = 0,
            TargetCurrentHp = 0,
            TargetMaxHp = 0,
            WorldObjects = Array.Empty<WorldObjectSnapshot>(),
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
            CurrentTargetServerObjectId = 100,
            CandidateEntityId = 100,
            CandidateServerObjectId = 100
        };
        state.MarkCandidate(100, 100, DateTimeOffset.Now);
        var semiAutoState = new SemiAutoCombatState();
        var context = CreateContext(settings, gameApi, logger);

        await controller.TickAsync(context, plan, semiAutoState, state).ConfigureAwait(false);

        AssertFalse(!state.Fighting, "first missing target tick should keep fight state briefly");
        AssertEqual((ushort)100, state.CurrentTargetEntityId, "first missing target tick should keep current target");
        AssertFalse(!logger.Entries.Any(entry =>
            entry.EventName == "stationary_combat.target.reacquire_wait" &&
            string.Equals(Convert.ToString(entry.Fields["reason"]), "target_mismatch_target_missing", StringComparison.Ordinal)),
            "first missing target tick should log reacquire wait");

        await Task.Delay(5).ConfigureAwait(false);
        await controller.TickAsync(context, plan, semiAutoState, state).ConfigureAwait(false);

        AssertFalse(state.Fighting, "missing target timeout should clear fight state");
        AssertEqual((ushort)0, state.CurrentTargetEntityId, "missing target timeout should clear current target");
        AssertFalse(state.IsTargetIgnored(100, 100), "missing target timeout should not permanently ignore the target");
        AssertFalse(!logger.Entries.Any(entry =>
            entry.EventName == "stationary_combat.target.lost" &&
            string.Equals(Convert.ToString(entry.Fields["reason"]), "target_mismatch_target_missing", StringComparison.Ordinal)),
            "missing target timeout should log target lost");

        gameApi.WorldObjects = new[]
        {
            new WorldObjectSnapshot(101, 101, "next", "monster", new Vector3Snapshot(8, 0, 0), 8, 1000, 1000)
        };
        await controller.TickAsync(context, plan, semiAutoState, state).ConfigureAwait(false);

        AssertEqual((ushort)101, state.CandidateEntityId, "next tick should select a new target after missing target clears");
    }
    finally
    {
        Environment.SetEnvironmentVariable("ROADHOG_MISSING_FIGHT_TARGET_TIMEOUT_MS", previousMissingTimeout);
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

static async Task TestStationaryCombatAcceptsSelfTargetingLockedTargetAfterOpeningAttackAsync()
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

    const uint targetServerObjectId = 100;
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
            new WorldObjectSnapshot(100, targetServerObjectId, "self-targeting", "monster", new Vector3Snapshot(8, 0, 0), 8, 1000, 1000, targetServerObjectId, false)
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
        CurrentTargetServerObjectId = targetServerObjectId
    };
    state.MarkCandidate(100, targetServerObjectId, DateTimeOffset.Now);
    var semiAutoState = new SemiAutoCombatState();
    var context = CreateContext(settings, gameApi, logger);

    await controller.TickAsync(context, plan, semiAutoState, state).ConfigureAwait(false);

    AssertFalse(keyboard.Keys.Contains("C"), "self-targeting locked target should not wait for player targeting via C loop");
    AssertFalse(!keyboard.Keys.Contains("D2"), "self-targeting locked target should continue skill release after opening attack");
    AssertFalse(logger.Entries.Any(entry => entry.EventName == "stationary_combat.opening_attack.wait_targeting"), "self-targeting locked target should not log opening attack wait");
    AssertFalse(!state.Fighting, "self-targeting locked target should remain in fight");
    AssertEqual((ushort)100, state.CurrentTargetEntityId, "current self-targeting locked target should remain selected");
    AssertEqual(targetServerObjectId, state.CurrentTargetServerObjectId, "current self-targeting locked target server id should remain selected");
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
    var ignoredEntry = logger.Entries.FirstOrDefault(entry =>
        entry.EventName == "stationary_combat.target.ignored" &&
        string.Equals(Convert.ToString(entry.Fields["reason"]), "target_owned_by_other", StringComparison.Ordinal));
    AssertFalse(ignoredEntry is null, "claimed target ignore should be logged");
    AssertEqual(999u, Convert.ToUInt32(ignoredEntry!.Fields["targetingServerObjectId"]), "claimed target log should include owner server id");
    AssertEqual(0u, Convert.ToUInt32(ignoredEntry.Fields["localPetServerObjectId"]), "claimed target log should include local pet server id");
    AssertFalse(Convert.ToBoolean(ignoredEntry.Fields["currentTargetIsRevivePathClear"]), "claimed target log should include revive path clear marker");

    gameApi.TargetEntityId = 0;
    gameApi.TargetServerObjectId = 0;
    gameApi.TargetIsTargetingLocalPlayer = false;
    await controller.TickAsync(context, plan, semiAutoState, state).ConfigureAwait(false);

    AssertEqual((ushort)101, state.CandidateEntityId, "next tick should switch to the next unclaimed target");
}

static async Task TestStationaryCombatTreatsSelfTargetingMonsterAsUnclaimedAsync()
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
    settings.SemiAuto.AttackKeyLoopEnabled = false;

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
        CandidateServerObjectId = targetServerObjectId
    };
    state.MarkCandidate(100, targetServerObjectId, DateTimeOffset.Now);
    var semiAutoState = new SemiAutoCombatState();
    var context = CreateContext(settings, gameApi, logger);

    await controller.TickAsync(context, plan, semiAutoState, state).ConfigureAwait(false);

    AssertFalse(!state.Fighting, "self-targeting monster should stay in combat");
    AssertEqual((ushort)100, state.CurrentTargetEntityId, "current self-targeting monster should remain selected");
    AssertFalse(state.IsTargetIgnored(100, targetServerObjectId), "self-targeting monster should not be ignored as claimed");
    AssertFalse(!keyboard.Keys.Contains("D2"), "self-targeting monster should continue skill release");
    AssertFalse(logger.Entries.Any(entry =>
        entry.EventName == "stationary_combat.target.ignored" &&
        string.Equals(Convert.ToString(entry.Fields["reason"]), "target_owned_by_other", StringComparison.Ordinal)),
        "self-targeting monster should not log claimed-target ignore");
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

static async Task TestStationaryCombatKeepsCurrentTargetThatPreviouslyTargetedPlayerAsync()
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

    const uint localServerObjectId = 1;
    const uint targetServerObjectId = 100;
    var keyboard = new RecordingKeyboardInput();
    var logger = new InMemoryRoadhogLogger();
    var gameApi = new FakeGameApi
    {
        Player = new PlayerSnapshot(1, 100, "Fake", 100, 100, 100, 100, 0, new Vector3Snapshot(0, 0, 0), DateTimeOffset.Now, 90, 10, 90),
        TargetEntityId = 999,
        TargetOwnServerObjectId = 999,
        TargetCurrentHp = 1000,
        TargetMaxHp = 1000,
        TargetPosition = new Vector3Snapshot(8, 0, 0),
        TargetServerObjectId = 0,
        LocalServerObjectId = localServerObjectId,
        TargetIsTargetingLocalPlayer = false,
        WorldObjects = new[]
        {
            new WorldObjectSnapshot(100, targetServerObjectId, "previous-local-target", "monster", new Vector3Snapshot(8, 0, 0), 8, 1000, 1000, localServerObjectId, false),
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
        LocalCombatSideServerObjectId = localServerObjectId
    };
    state.MarkCandidate(100, targetServerObjectId, DateTimeOffset.Now);
    var semiAutoState = new SemiAutoCombatState();
    var context = CreateContext(settings, gameApi, logger);

    await controller.TickAsync(context, plan, semiAutoState, state).ConfigureAwait(false);

    AssertFalse(!state.Fighting, "current target that targeted player should remain in fight");
    AssertFalse(!state.CurrentTargetIsMaintenanceDefense, "world targeting local player should set local-side fight marker");
    AssertEqual((ushort)100, state.CurrentTargetEntityId, "current fight target should stay after targeting player");
    AssertEqual(targetServerObjectId, state.CurrentTargetServerObjectId, "current fight target server id should stay after targeting player");
    AssertFalse(state.IsTargetIgnored(100, targetServerObjectId), "current local-side target should not be ignored as claimed");

    gameApi.WorldObjects = new[]
    {
        new WorldObjectSnapshot(100, targetServerObjectId, "previous-local-target", "monster", new Vector3Snapshot(8, 0, 0), 8, 1000, 1000, 999, false),
        new WorldObjectSnapshot(101, 101, "next", "monster", new Vector3Snapshot(12, 0, 0), 12, 1000, 1000)
    };

    await controller.TickAsync(context, plan, semiAutoState, state).ConfigureAwait(false);

    AssertFalse(!state.Fighting, "current target that previously targeted player should remain in fight after retargeting away");
    AssertFalse(!state.CurrentTargetIsMaintenanceDefense, "previous local-side fight marker should remain after retargeting away");
    AssertEqual((ushort)100, state.CurrentTargetEntityId, "current fight target should stay");
    AssertEqual(targetServerObjectId, state.CurrentTargetServerObjectId, "current fight target server id should stay");
    AssertFalse(state.IsTargetIgnored(100, targetServerObjectId), "current previously local-side target should not be ignored as claimed");
    AssertFalse(logger.Entries.Any(entry =>
        entry.EventName == "stationary_combat.target.ignored" &&
        string.Equals(Convert.ToString(entry.Fields["reason"]), "target_owned_by_other", StringComparison.Ordinal)),
        "local-side target should not log claimed-target ignore");
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

static async Task TestStationaryCombatKeepsRevivePathClearTargetClaimedByOtherAsync()
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

    const uint targetServerObjectId = 100;
    const uint otherServerObjectId = 999;
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
        TargetServerObjectId = otherServerObjectId,
        TargetIsTargetingLocalPlayer = false,
        WorldObjects = new[]
        {
            new WorldObjectSnapshot(100, targetServerObjectId, "clear-target", "monster", new Vector3Snapshot(8, 0, 0), 8, 1000, 1000, otherServerObjectId, false)
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
        CurrentTargetIsRevivePathClear = true,
        CurrentTargetBypassesHomeLeash = true
    };
    state.MarkCandidate(100, targetServerObjectId, DateTimeOffset.Now);
    var semiAutoState = new SemiAutoCombatState();
    var context = CreateContext(settings, gameApi, logger);

    await controller.TickAsync(context, plan, semiAutoState, state).ConfigureAwait(false);

    AssertFalse(!state.Fighting, "revive path clear target should stay in fight even if locked target looks claimed");
    AssertEqual((ushort)100, state.CurrentTargetEntityId, "revive path clear current target should stay");
    AssertEqual(targetServerObjectId, state.CurrentTargetServerObjectId, "revive path clear server id should stay");
    AssertFalse(state.IsTargetIgnored(100, targetServerObjectId), "revive path clear target should not be ignored as claimed");
    AssertFalse(logger.Entries.Any(entry =>
        entry.EventName == "stationary_combat.target.ignored" &&
        string.Equals(Convert.ToString(entry.Fields["reason"]), "target_owned_by_other", StringComparison.Ordinal)),
        "revive path clear target should not log claimed-target ignore");
}

static async Task TestStationaryCombatReacquiresRevivePathClearTargetClaimedByOtherAsync()
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

        const uint targetServerObjectId = 100;
        const uint otherServerObjectId = 999;
        var keyboard = new RecordingKeyboardInput();
        var logger = new InMemoryRoadhogLogger();
        var gameApi = new FakeGameApi
        {
            Player = new PlayerSnapshot(1, 200, "Fake", 100, 100, 100, 100, 0, new Vector3Snapshot(0, 0, 0), DateTimeOffset.Now, 90, 10, 90),
            TargetEntityId = 200,
            TargetOwnServerObjectId = 200,
            TargetCurrentHp = 1000,
            TargetMaxHp = 1000,
            TargetPosition = new Vector3Snapshot(8, 0, 0),
            TargetServerObjectId = 0,
            TargetIsTargetingLocalPlayer = false,
            WorldObjects = new[]
            {
                new WorldObjectSnapshot(100, targetServerObjectId, "clear-target", "monster", new Vector3Snapshot(8, 0, 0), 8, 1000, 1000, otherServerObjectId, false),
                new WorldObjectSnapshot(200, 200, "wrong-lock", "monster", new Vector3Snapshot(12, 0, 0), 12, 1000, 1000)
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
            CurrentTargetIsRevivePathClear = true,
            CurrentTargetBypassesHomeLeash = true
        };
        state.MarkCandidate(100, targetServerObjectId, DateTimeOffset.Now);
        var semiAutoState = new SemiAutoCombatState();
        var context = CreateContext(settings, gameApi, logger);

        await controller.TickAsync(context, plan, semiAutoState, state).ConfigureAwait(false);

        AssertFalse(!state.Fighting, "revive path clear target should stay in fight while reacquiring");
        AssertEqual((ushort)100, state.CurrentTargetEntityId, "reacquire should keep original revive path clear target");
        AssertEqual(targetServerObjectId, state.CurrentTargetServerObjectId, "reacquire should keep original revive path clear server id");
        AssertFalse(state.IsTargetIgnored(100, targetServerObjectId), "claimed revive path clear target should not be ignored during reacquire");
        AssertFalse(!logger.Entries.Any(entry =>
            entry.EventName == "stationary_combat.target.reacquire" &&
            Equals(Convert.ToUInt16(entry.Fields["targetEntityId"]), (ushort)100)),
            "revive path clear target should log current target reacquire");
        AssertFalse(logger.Entries.Any(entry =>
            entry.EventName == "stationary_combat.target.ignored" &&
            string.Equals(Convert.ToString(entry.Fields["reason"]), "target_owned_by_other", StringComparison.Ordinal)),
            "revive path clear reacquire should not log claimed-target ignore");
    }
    finally
    {
        Environment.SetEnvironmentVariable("ROADHOG_STATIONARY_TAB_VERIFY_DELAY_MS", previousTabDelay);
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

static async Task TestStationaryCombatSkipsDeadTargetWhenCorpseIsNotLootableAsync()
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
        var gameApi = new FakeGameApi
        {
            Player = new PlayerSnapshot(1, 100, "Fake", 100, 100, 100, 100, 0, new Vector3Snapshot(0, 0, 0), DateTimeOffset.Now, 90, 10, 90),
            TargetEntityId = 100,
            TargetCurrentHp = 0,
            TargetMaxHp = 4430,
            TargetLootableRaw = 0,
            TargetPosition = new Vector3Snapshot(2.5f, 0, 0),
            Skills = CreateSkillSnapshotsById(new Dictionary<uint, uint>())
        };
        var controller = new StationaryCombatController(keyboard, new SemiAutoCombatController(keyboard));
        var plan = SemiAutoSkillPlan.FromSettings(settings.Skills);
        var state = new StationaryCombatState
        {
            Fighting = true,
            CurrentTargetEntityId = 100,
            CandidateEntityId = 100
        };

        await controller
            .TickAsync(CreateContext(settings, gameApi, logger), plan, new SemiAutoCombatState(), state)
            .ConfigureAwait(false);

        AssertSequence(Array.Empty<string>(), keyboard.Keys, "not-lootable dead target should not press loot");
        AssertEqual(0, gameApi.LootReadCount, "same locked corpse should not scan corpse list");
        AssertEqual(StationaryCombatLootAfterKillStep.PostCombatMaintenance, state.LootAfterKill.Step, "not-lootable corpse should skip to maintenance");
        AssertFalse(
            !logger.Entries.Any(entry =>
                entry.EventName == "stationary_combat.loot.skipped" &&
                string.Equals(Convert.ToString(entry.Fields["reason"]), "not_lootable", StringComparison.Ordinal)),
            "not-lootable corpse should log skipped loot");
    }
    finally
    {
        Environment.SetEnvironmentVariable("ROADHOG_LOOT_AFTER_KILL_WAIT_MS", previousAfterKillWait);
        Environment.SetEnvironmentVariable("ROADHOG_LOOT_AFTER_PICK_WAIT_MS", previousWait);
        Environment.SetEnvironmentVariable("ROADHOG_LOOT_PRESS_COUNT", previousPressCount);
        Environment.SetEnvironmentVariable("ROADHOG_LOOT_PRESS_INTERVAL_MS", previousPressInterval);
    }
}

static async Task TestStationaryCombatAttemptsSameLootCorpseOnceAsync()
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
        var gameApi = new FakeGameApi
        {
            Player = new PlayerSnapshot(1, 100, "Fake", 100, 100, 100, 100, 0, new Vector3Snapshot(0, 0, 0), DateTimeOffset.Now, 90, 10, 90),
            TargetEntityId = 100,
            TargetOwnServerObjectId = 5000,
            TargetCurrentHp = 0,
            TargetMaxHp = 4430,
            TargetLootableRaw = 1,
            TargetPosition = new Vector3Snapshot(2.5f, 0, 0),
            Skills = CreateSkillSnapshotsById(new Dictionary<uint, uint>())
        };
        var controller = new StationaryCombatController(keyboard, new SemiAutoCombatController(keyboard));
        var plan = SemiAutoSkillPlan.FromSettings(settings.Skills);
        var state = new StationaryCombatState
        {
            Fighting = true,
            CurrentTargetEntityId = 100,
            CurrentTargetServerObjectId = 5000,
            CandidateEntityId = 100,
            CandidateServerObjectId = 5000
        };
        var context = CreateContext(settings, gameApi, logger);

        await controller
            .TickAsync(context, plan, new SemiAutoCombatState(), state)
            .ConfigureAwait(false);

        AssertSequence(new[] { "NumPadDecimal" }, keyboard.Keys, "first lootable corpse attempt should press loot");

        state.Fighting = true;
        state.CurrentTargetEntityId = 100;
        state.CurrentTargetServerObjectId = 5000;
        state.CandidateEntityId = 100;
        state.CandidateServerObjectId = 5000;

        await controller
            .TickAsync(context, plan, new SemiAutoCombatState(), state)
            .ConfigureAwait(false);

        AssertSequence(new[] { "NumPadDecimal" }, keyboard.Keys, "same corpse should only be attempted once");
        AssertFalse(
            !logger.Entries.Any(entry =>
                entry.EventName == "stationary_combat.loot.skipped" &&
                string.Equals(Convert.ToString(entry.Fields["reason"]), "already_attempted", StringComparison.Ordinal)),
            "second same-corpse pass should log already attempted");
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

static async Task TestStationaryCombatRunsAfterCombatMaintenanceWithoutLootAsync()
{
    var settings = CreateScriptSettings();
    settings.MainMode = AccountMainMode.CustomCombat;
    settings.CombatMode = AccountCombatMode.Stationary;
    settings.Combat = new CombatScriptSettings
    {
        EnableLoot = false,
        HasStationaryCombatPosition = true,
        StationaryCombatX = 0,
        StationaryCombatY = 0,
        StationaryCombatZ = 0,
        StationaryCombatRadius = 60
    };
    settings.Maintenance.SitMaintenanceEnabled = false;
    settings.Maintenance.MpMaintenanceRules.Add(new MaintenanceKeyRuleConfig
    {
        BelowPercent = 60,
        Key = "NumPad1",
        SkillId = 1,
        SkillName = "精神力恢复 II",
        RunTiming = MaintenanceRuleRunTiming.AfterCombat
    });

    var keyboard = new RecordingKeyboardInput();
    var logger = new InMemoryRoadhogLogger();
    var gameApi = new FakeGameApi
    {
        Player = new PlayerSnapshot(1, 100, "Fake", 100, 100, 50, 100, 0, new Vector3Snapshot(0, 0, 0), DateTimeOffset.Now, 90, 10, 90),
        TargetEntityId = 100,
        TargetCurrentHp = 0,
        TargetMaxHp = 4430,
        TargetPosition = new Vector3Snapshot(2.5f, 0, 0),
        Skills = CreateSkillSnapshotsById(new Dictionary<uint, uint>
        {
            [1] = 0
        })
    };
    keyboard.AfterPress = key =>
    {
        if (string.Equals(key, "NumPad1", StringComparison.Ordinal))
        {
            gameApi.Skills = CreateSkillSnapshotsById(new Dictionary<uint, uint>
            {
                [1] = ActiveCooldownEnd()
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

    AssertSequence(new[] { "NumPad1" }, keyboard.Keys, "after-combat mp maintenance should run when loot is disabled");
    AssertFalse(state.Fighting, "fight state should clear after dead target");
    AssertFalse(state.LootAfterKill.Active, "loot state should stay inactive when loot is disabled");
    AssertFalse(
        !logger.Entries.Any(entry => entry.EventName == "stationary_combat.post_combat_maintenance"),
        "non-loot post-combat maintenance should be logged");
}

static async Task TestStationaryCombatRunsAfterCombatMaintenanceRoundAsync()
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
        settings.Maintenance.StatusMaintenanceRules.Add(new StatusMaintenanceRuleConfig
        {
            Key = "NumPad6",
            SkillId = 1,
            SkillName = "Status Buff",
            RunTiming = MaintenanceRuleRunTiming.AfterCombat
        });
        settings.Maintenance.MpMaintenanceRules.Add(new MaintenanceKeyRuleConfig
        {
            BelowPercent = 45,
            Key = "NumPad3",
            SkillId = 5,
            SkillName = "Mana Skill",
            RunTiming = MaintenanceRuleRunTiming.AfterCombat
        });
        settings.Maintenance.MpMaintenanceRules.Add(new MaintenanceKeyRuleConfig
        {
            BelowPercent = 60,
            ActionType = MaintenanceRuleActionType.Potion,
            Key = "NumPadAdd",
            RunTiming = MaintenanceRuleRunTiming.AfterCombat
        });

        PlayerSnapshot PlayerWith(uint hp, uint mp) => new(
            1,
            100,
            "Fake",
            hp,
            100,
            mp,
            100,
            0,
            new Vector3Snapshot(0, 0, 0),
            DateTimeOffset.Now,
            90,
            10,
            90);

        var keyboard = new RecordingKeyboardInput();
        var logger = new InMemoryRoadhogLogger();
        var gameApi = new FakeGameApi
        {
            Player = PlayerWith(100, 20),
            PlayerAbnormalStatuses = PlayerAbnormalStatusSnapshot.Empty(1),
            InventoryItems = new[]
            {
                new InventoryItemSnapshot(1, 1, "上级精神之仙药", 1, 0, false, 17)
            },
            TargetEntityId = 100,
            TargetCurrentHp = 0,
            TargetMaxHp = 4430,
            TargetPosition = new Vector3Snapshot(2.5f, 0, 0),
            Skills = CreateSkillSnapshotsById(new Dictionary<uint, uint>
            {
                [1] = 0,
                [5] = 0
            })
        };
        keyboard.AfterPress = key =>
        {
            if (string.Equals(key, "NumPad6", StringComparison.Ordinal))
            {
                gameApi.PlayerAbnormalStatuses = new PlayerAbnormalStatusSnapshot(
                    1,
                    DateTimeOffset.Now,
                    1,
                    new[] { Abnormal(4001, 0) });
            }
            else if (string.Equals(key, "NumPadAdd", StringComparison.Ordinal))
            {
                gameApi.Player = PlayerWith(100, 40);
            }
            else if (string.Equals(key, "NumPad3", StringComparison.Ordinal))
            {
                gameApi.Player = PlayerWith(100, 80);
                gameApi.Skills = CreateSkillSnapshotsById(new Dictionary<uint, uint>
                {
                    [1] = 0,
                    [5] = ActiveCooldownEnd()
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

        AssertSequence(
            new[] { "NumPadDecimal", "NumPad6", "NumPadAdd", "NumPad3" },
            keyboard.Keys,
            "after-combat maintenance round should continue through status, potion, and skill");
        AssertEqual(
            3,
            logger.Entries.Count(entry => entry.EventName == "stationary_combat.loot.post_combat_maintenance"),
            "each handled after-combat maintenance action should be logged");
        AssertFalse(!logger.Entries.Any(entry => entry.EventName == "semi_auto.maintenance.potion_pressed"), "mp potion should press during after-combat round");
        AssertFalse(gameApi.LastInventoryContext is not null, "after-combat potion should not read inventory");
        AssertFalse(state.LootAfterKill.Active, "loot state should finish after maintenance round");
    }
    finally
    {
        Environment.SetEnvironmentVariable("ROADHOG_LOOT_AFTER_KILL_WAIT_MS", previousAfterKillWait);
        Environment.SetEnvironmentVariable("ROADHOG_LOOT_AFTER_PICK_WAIT_MS", previousWait);
        Environment.SetEnvironmentVariable("ROADHOG_LOOT_PRESS_COUNT", previousPressCount);
        Environment.SetEnvironmentVariable("ROADHOG_LOOT_PRESS_INTERVAL_MS", previousPressInterval);
    }
}

static async Task TestStationaryCombatReturnsFromBagCleanupThroughRevivePathBeforeFinishingLootAsync()
{
    var previousBearingMode = Environment.GetEnvironmentVariable("AION_FACE_TARGET_BEARING_MODE");
    Environment.SetEnvironmentVariable("AION_FACE_TARGET_BEARING_MODE", "y-x");
    try
    {
        var settings = CreateScriptSettings();
        settings.MainMode = AccountMainMode.CustomCombat;
        settings.CombatMode = AccountCombatMode.Path;
        settings.Paths.RevivePathName = "revive-a";
        settings.Paths.CombatPathName = "combat-a";
        settings.Combat = new CombatScriptSettings
        {
            EnableLoot = true,
            StationaryCombatRadius = 20
        };

        var pathStore = new InMemorySharedPathStore(
            CreatePath("revive-a",
                new Vector3Snapshot(0, 0, 0),
                new Vector3Snapshot(100, 0, 0),
                new Vector3Snapshot(200, 0, 0)),
            CreatePath("combat-a",
                new Vector3Snapshot(205, 0, 0),
                new Vector3Snapshot(215, 0, 0)));
        var keyboard = new RecordingKeyboardInput();
        var logger = new InMemoryRoadhogLogger();
        var gameApi = new FakeGameApi
        {
            Player = new PlayerSnapshot(1, 100, "Fake", 100, 100, 100, 100, 0, new Vector3Snapshot(0, 0, 0), DateTimeOffset.Now, 90, 10, 90),
            TargetEntityId = 0,
            TargetCurrentHp = 0,
            TargetMaxHp = 0,
            TargetPosition = null,
            WorldObjects = Array.Empty<WorldObjectSnapshot>(),
            Skills = CreateSkillSnapshotsById(new Dictionary<uint, uint>())
        };
        var semiAuto = new SemiAutoCombatController(keyboard);
        var controller = new StationaryCombatController(keyboard, semiAuto, pathStore);
        var state = new StationaryCombatState();
        state.StartLootAfterKill(
            new LockedTargetSnapshot(
                100,
                5000,
                0,
                LockedTargetSnapshot.MonsterObjectType,
                "dead-target",
                0,
                100,
                new Vector3Snapshot(0, 0, 0),
                0,
                DateTimeOffset.Now),
            DateTimeOffset.Now);
        for (var i = 0; i < 5; i++)
        {
            state.LootAfterKill.Advance(DateTimeOffset.Now);
        }

        state.StartCleanupReturnToCombat();

        await controller
            .TickAsync(CreateContext(settings, gameApi, logger), SemiAutoSkillPlan.FromSettings(settings.Skills), new SemiAutoCombatState(), state)
            .ConfigureAwait(false);

        AssertFalse(!state.CleanupReturnToCombatActive, "cleanup return should stay active while revive path is still running");
        AssertFalse(!state.StartupRecoveryActive, "cleanup return should reuse startup recovery revive-path following");
        AssertEqual("revive-a", state.StartupRecoveryPathName, "cleanup return should use configured revive path");
        AssertEqual(1, state.StartupRecoveryPointIndex, "cleanup return should advance from reached camp point toward combat home");
        AssertFalse(!state.LootAfterKill.Active, "loot should remain active until cleanup return reaches combat home");
        AssertFalse(logger.Entries.Any(entry => entry.EventName == "stationary_combat.loot.finished"),
            "loot should not finish before cleanup return-to-combat recovery completes");
        AssertFalse(!logger.Entries.Any(entry => entry.EventName == "stationary_combat.startup_recovery.selected"),
            "cleanup return should start revive-path recovery");
    }
    finally
    {
        Environment.SetEnvironmentVariable("AION_FACE_TARGET_BEARING_MODE", previousBearingMode);
    }
}

static async Task TestStationaryCombatPostponesAfterCombatMaintenanceWhilePetIsTargetedAsync()
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
        const ushort defenseEntityId = 200;
        const uint defenseServerObjectId = 2200;
        const uint petServerObjectId = 2000;
        var settings = CreateSpiritmasterScriptSettings();
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
            Player = new PlayerSnapshot(
                1,
                100,
                "Spirit",
                40,
                100,
                100,
                100,
                0,
                new Vector3Snapshot(0, 0, 0),
                DateTimeOffset.Now,
                CharacterClass: AionClassCatalog.GetChineseName(AionClassId.Spiritmaster),
                CharacterClassId: AionClassId.Spiritmaster),
            SummonedPetRoster = CreateLocalPetRoster(isSummoned: true),
            TargetEntityId = 100,
            TargetCurrentHp = 0,
            TargetMaxHp = 4430,
            TargetPosition = new Vector3Snapshot(2.5f, 0, 0),
            Skills = CreateSpiritmasterSkillSnapshots(),
            WorldObjects = new[]
            {
                new WorldObjectSnapshot(
                    defenseEntityId,
                    defenseServerObjectId,
                    "pet-targeting",
                    "monster",
                    new Vector3Snapshot(8, 0, 0),
                    8,
                    1000,
                    1000,
                    petServerObjectId,
                    false)
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

        AssertFalse(keyboard.Keys.Contains("D8"), "after-combat maintenance should wait while another monster targets the pet");
        AssertSequence(new[] { "NumPadDecimal" }, keyboard.Keys, "loot should still be pressed for the killed monster");
        AssertFalse(state.LootAfterKill.Active, "loot state should finish before switching to defense target");
        AssertFalse(!state.Fighting, "defense target should become the next fight");
        AssertFalse(!state.CurrentTargetIsMaintenanceDefense, "pet-targeting monster should be marked as local-side defense");
        AssertEqual(defenseEntityId, state.CurrentTargetEntityId, "defense target entity");
        AssertEqual(defenseServerObjectId, state.CurrentTargetServerObjectId, "defense target server object");
        AssertFalse(
            !logger.Entries.Any(entry => entry.EventName == "stationary_combat.loot.post_combat_maintenance_postponed"),
            "post-combat maintenance should log postponement");
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

static async Task TestStationaryCombatReacquiresAdoptedDefenseTargetWhenLockedOnPartyMemberAsync()
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

    const ushort defenseEntityId = 200;
    const uint defenseServerObjectId = 9000;
    const uint localServerObjectId = 1000;
    const uint partyMemberServerObjectId = 2000;
    var keyboard = new RecordingKeyboardInput();
    var logger = new InMemoryRoadhogLogger();
    var gameApi = new FakeGameApi
    {
        Player = new PlayerSnapshot(
            1,
            101,
            "Healer",
            100,
            100,
            100,
            100,
            0,
            new Vector3Snapshot(0, 0, 0),
            DateTimeOffset.Now,
            90,
            10,
            90),
        TargetEntityId = 101,
        TargetOwnServerObjectId = partyMemberServerObjectId,
        TargetServerObjectId = defenseServerObjectId,
        TargetObjectType = LockedTargetSnapshot.PlayerObjectType,
        TargetCurrentHp = 100,
        TargetMaxHp = 100,
        TargetPosition = new Vector3Snapshot(4, 0, 0),
        LocalServerObjectId = localServerObjectId,
        WorldObjects = new[]
        {
            new WorldObjectSnapshot(
                defenseEntityId,
                defenseServerObjectId,
                "attacker",
                "monster",
                new Vector3Snapshot(5, 0, 0),
                5,
                1000,
                1000,
                localServerObjectId,
                true)
        },
        Skills = CreateSkillSnapshotsById(new Dictionary<uint, uint>
        {
            [1] = 0,
            [5] = 0,
            [6] = 0
        })
    };
    var controller = new StationaryCombatController(keyboard, new SemiAutoCombatController(keyboard));
    var plan = SemiAutoSkillPlan.FromSettings(settings.Skills);
    var state = new StationaryCombatState
    {
        Fighting = true,
        CurrentTargetEntityId = defenseEntityId,
        CurrentTargetServerObjectId = defenseServerObjectId,
        CandidateEntityId = defenseEntityId,
        CandidateServerObjectId = defenseServerObjectId,
        FacedCandidateEntityId = defenseEntityId,
        CurrentTargetIsMaintenanceDefense = true,
        CurrentTargetBypassesHomeLeash = true
    };

    await controller
        .TickAsync(CreateContext(settings, gameApi, logger), plan, new SemiAutoCombatState(), state)
        .ConfigureAwait(false);

    AssertFalse(!state.Fighting, "party-member lock mismatch should keep fighting state for adopted defense target");
    AssertEqual(defenseServerObjectId, state.CurrentTargetServerObjectId, "adopted defense target should stay current");
    AssertSequence(new[] { "Tab" }, keyboard.Keys.ToArray(), "party-member lock mismatch should start target reacquire");
    AssertFalse(
        !logger.Entries.Any(entry => entry.EventName == "stationary_combat.target.reacquire"),
        "party-member lock mismatch should log target reacquire instead of clearing");
}

static async Task TestStationaryCombatFacesAdoptedDefenseTargetBeforeReacquireTabAsync()
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
        settings.Maintenance.SitMaintenanceEnabled = false;

        const ushort defenseEntityId = 200;
        const uint defenseServerObjectId = 9000;
        const uint wrongServerObjectId = 7000;
        const uint localServerObjectId = 1000;
        var keyboard = new RecordingKeyboardInput();
        var logger = new InMemoryRoadhogLogger();
        var gameApi = new FakeGameApi
        {
            Player = new PlayerSnapshot(
                1,
                101,
                "Healer",
                100,
                100,
                100,
                100,
                0,
                new Vector3Snapshot(0, 0, 0),
                DateTimeOffset.Now,
                0,
                10,
                0),
            TargetEntityId = 101,
            TargetOwnServerObjectId = wrongServerObjectId,
            TargetServerObjectId = 0,
            TargetObjectType = LockedTargetSnapshot.MonsterObjectType,
            TargetCurrentHp = 1000,
            TargetMaxHp = 1000,
            TargetPosition = new Vector3Snapshot(40, 0, 0),
            LocalServerObjectId = localServerObjectId,
            WorldObjects = new[]
            {
                new WorldObjectSnapshot(
                    defenseEntityId,
                    defenseServerObjectId,
                    "attacker",
                    "monster",
                    new Vector3Snapshot(5, 0, 0),
                    5,
                    1000,
                    1000,
                    localServerObjectId,
                    true)
            },
            Skills = CreateSkillSnapshotsById(new Dictionary<uint, uint>
            {
                [1] = 0,
                [5] = 0,
                [6] = 0
            })
        };
        var controller = new StationaryCombatController(keyboard, new SemiAutoCombatController(keyboard));
        var plan = SemiAutoSkillPlan.FromSettings(settings.Skills);
        var state = new StationaryCombatState
        {
            Fighting = true,
            CurrentTargetEntityId = defenseEntityId,
            CurrentTargetServerObjectId = defenseServerObjectId,
            CandidateEntityId = defenseEntityId,
            CandidateServerObjectId = defenseServerObjectId,
            CurrentTargetIsMaintenanceDefense = true,
            CurrentTargetBypassesHomeLeash = true
        };

        await controller
            .TickAsync(CreateContext(settings, gameApi, logger), plan, new SemiAutoCombatState(), state)
            .ConfigureAwait(false);

        AssertFalse(!state.Fighting, "wrong lock should keep fighting adopted defense target");
        AssertEqual(defenseServerObjectId, state.CurrentTargetServerObjectId, "adopted defense target should stay current");
        AssertFalse(!keyboard.MouseCommands.Contains("down:Right"), "unfaced reacquire target should hold right mouse");
        AssertFalse(
            !keyboard.MouseCommands.Any(command => command.StartsWith("move:", StringComparison.Ordinal)),
            "unfaced reacquire target should move mouse before tab");
        AssertFalse(keyboard.Keys.Contains("Tab"), "unfaced reacquire target should not Tab in the same tick");
        AssertFalse(
            !logger.Entries.Any(entry => entry.EventName == "stationary_combat.target.reacquire"),
            "wrong lock should log current target reacquire");
        var faceEntry = logger.Entries.LastOrDefault(entry => entry.EventName == "stationary_combat.face_target");
        AssertFalse(faceEntry is null, "reacquire should face current defense target before tab");
        AssertEqual(
            defenseEntityId,
            Convert.ToUInt16(faceEntry!.Fields["targetEntityId"]),
            "reacquire face target should use adopted defense target");
    }
    finally
    {
        Environment.SetEnvironmentVariable("AION_FACE_TARGET_BEARING_MODE", previousBearingMode);
    }
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

static Task TestManualSkillCategoryMapsTargetValidStatusAsConditionAsync()
{
    var conditionSkill = new SkillSnapshot(
        1003,
        "Resonance Smoke I",
        1,
        1,
        "Resonance Smoke",
        1,
        false,
        1000,
        0,
        XmlActivation: "Active",
        XmlTags: "condition",
        XmlTargetValidStatuses: "Stumble");

    AssertEqual("条件技能", GetManualSkillCategoryForTest(conditionSkill), "condition skill category");
    return Task.CompletedTask;
}

static Task TestConditionSkillPreemptSwitchPersistsFromSkillUiAsync()
{
    Exception? failure = null;
    var thread = new Thread(() =>
    {
        try
        {
            var settings = CreateScriptSettings();
            settings.SemiAuto.ConditionSkillPreemptsChain = true;
            var configStore = new InMemoryAccountConfigStore(new AccountConfig
            {
                AccountName = "account1",
                ScriptSettings = settings
            });

            using var form = CreateAccountSettingsFormForTestsWithStore(configStore);
            AssertFalse(
                !GetCheckBoxCheckedForTest(form, "conditionSkillPreemptsChainCheckBox"),
                "condition preempt switch should load enabled state");

            SetCheckBoxCheckedForTest(form, "conditionSkillPreemptsChainCheckBox", false);
            var saved = InvokeSaveCurrentSettingsForTest(form, out var error);
            AssertFalse(!saved, "condition preempt switch save failed: " + error);

            var load = configStore.LoadAllAsync().GetAwaiter().GetResult();
            AssertFalse(!load.Success, "saved config should load");
            var savedSettings = load.Value!
                .Single(account => string.Equals(account.AccountName, "account1", StringComparison.OrdinalIgnoreCase))
                .ScriptSettings;
            AssertFalse(
                savedSettings?.SemiAuto.ConditionSkillPreemptsChain != false,
                "condition preempt switch should persist disabled state");
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

static Task TestReturnHomeWhenNoTargetSwitchPersistsFromSummaryUiAsync()
{
    Exception? failure = null;
    var thread = new Thread(() =>
    {
        try
        {
            var settings = CreateScriptSettings();
            settings.MainMode = AccountMainMode.CustomCombat;
            settings.CombatMode = AccountCombatMode.Stationary;
            settings.Combat.ReturnHomeWhenNoTarget = false;
            var configStore = new InMemoryAccountConfigStore(new AccountConfig
            {
                AccountName = "account1",
                ScriptSettings = settings
            });

            using var form = CreateAccountSettingsFormForTestsWithStore(configStore);
            AssertFalse(
                GetCheckBoxCheckedForTest(form, "returnHomeWhenNoTargetCheckBox"),
                "return home when no target switch should load disabled state");

            SetCheckBoxCheckedForTest(form, "returnHomeWhenNoTargetCheckBox", true);
            var saved = InvokeSaveCurrentSettingsForTest(form, out var error);
            AssertFalse(!saved, "return home when no target switch save failed: " + error);

            var load = configStore.LoadAllAsync().GetAwaiter().GetResult();
            AssertFalse(!load.Success, "saved config should load");
            var savedSettings = load.Value!
                .Single(account => string.Equals(account.AccountName, "account1", StringComparison.OrdinalIgnoreCase))
                .ScriptSettings;
            AssertFalse(
                savedSettings?.Combat.ReturnHomeWhenNoTarget != true,
                "return home when no target switch should persist enabled state");
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

static async Task TestSpiritmasterTickSummonsWhenPetRosterIsUnconfirmedAsync()
{
    await AssertSpiritmasterSummonsForUnconfirmedRosterAsync(
            CreateLocalUnconfirmedPetRoster(
                ownerConfirmed: false,
                staticSummonPet: false,
                linkedPetMatches: true),
            "linked-only roster")
        .ConfigureAwait(false);

    await AssertSpiritmasterSummonsForUnconfirmedRosterAsync(
            CreateLocalUnconfirmedPetRoster(
                ownerConfirmed: true,
                staticSummonPet: true,
                linkedPetMatches: false),
            "owner-only temporary summon roster")
        .ConfigureAwait(false);
}

static async Task AssertSpiritmasterSummonsForUnconfirmedRosterAsync(
    SummonedPetRosterSnapshot roster,
    string scenario)
{
    AssertFalse(
        SpiritmasterCombatContext.IsConfirmedLocalSummonedPet(roster.LocalPlayerPet),
        scenario + " should not be treated as confirmed local pet");

    var settings = CreateSpiritmasterScriptSettings();
    settings.Skills.Spiritmaster.SummonSkills = new List<SpiritmasterSkillKeyRuleConfig>
    {
        new() { Key = "NumPad6" }
    };
    var plan = SemiAutoSkillPlan.FromSettings(settings.Skills);
    var keyboard = new RecordingKeyboardInput();
    var logger = new InMemoryRoadhogLogger();
    var gameApi = new FakeGameApi
    {
        Player = CreateSpiritmasterPlayer(),
        SummonedPetRoster = roster,
        Skills = CreateSpiritmasterSkillSnapshots()
    };
    var controller = new SemiAutoCombatController(keyboard);

    await controller.TickAsync(CreateContext(settings, gameApi, logger), plan, new SemiAutoCombatState()).ConfigureAwait(false);

    AssertSequence(new[] { "NumPad6" }, keyboard.Keys.ToArray(), scenario + " summon key");
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

static async Task TestStaleCooldownCalibrationInvalidatesImpossibleCombatCooldownsAsync()
{
    var settings = CreateScriptSettings();
    var plan = SemiAutoSkillPlan.FromSettings(settings.Skills);
    var keyboard = new RecordingKeyboardInput();
    var logger = new InMemoryRoadhogLogger();
    var gameApi = new FakeGameApi
    {
        Skills = CreateSkillSnapshotsById(CreateCombatRootCooldowns(CooldownEndIn(130_000)))
    };
    var controller = new SemiAutoCombatController(keyboard);
    var state = new SemiAutoCombatState();
    CalibrateCooldownClock(state);

    await controller.TickAsync(CreateContext(settings, gameApi, logger), plan, state).ConfigureAwait(false);

    AssertFalse(state.HasCooldownTickCalibration, "impossible cooldowns should clear cooldown calibration");
    AssertFalse(
        !logger.Entries.Any(entry =>
            entry.EventName == "semi_auto.cooldown.calibration_invalidated" &&
            string.Equals(
                entry.Fields.GetValueOrDefault("reason")?.ToString(),
                "short_cooldown_impossible",
                StringComparison.Ordinal)),
        "impossible cooldowns should log calibration invalidation");
    AssertSequence(
        WithPreSkillAttackKey("D2", "D3", "D4", "D1"),
        keyboard.Keys.ToArray(),
        "invalidated cooldown calibration should fall back to first root");
}

static async Task TestStaleCooldownCalibrationSkipsZeroDurationSkillsWhenInvalidatingAsync()
{
    var settings = CreateScriptSettings();
    var plan = SemiAutoSkillPlan.FromSettings(settings.Skills);
    var keyboard = new RecordingKeyboardInput();
    var logger = new InMemoryRoadhogLogger();
    var impossibleCooldownEnd = CooldownEndIn(130_000);
    var gameApi = new FakeGameApi
    {
        Skills = CreateSkillSnapshotsById(CreateCombatRootCooldowns(impossibleCooldownEnd))
            .Select(skill => skill.SkillId == 8
                ? skill with { CooldownDuration = 0, CooldownEndTime = impossibleCooldownEnd }
                : skill)
            .ToArray()
    };
    var controller = new SemiAutoCombatController(keyboard);
    var state = new SemiAutoCombatState();
    CalibrateCooldownClock(state);

    await controller.TickAsync(CreateContext(settings, gameApi, logger), plan, state).ConfigureAwait(false);

    AssertFalse(state.HasCooldownTickCalibration, "zero-duration skill should not block stale cooldown invalidation");
    AssertFalse(
        !logger.Entries.Any(entry => entry.EventName == "semi_auto.cooldown.calibration_invalidated"),
        "stale cooldowns should still log calibration invalidation");
}

static async Task TestValidCooldownCalibrationKeepsPlausibleCombatCooldownsCoolingAsync()
{
    var settings = CreateScriptSettings();
    var plan = SemiAutoSkillPlan.FromSettings(settings.Skills);
    var keyboard = new RecordingKeyboardInput();
    var logger = new InMemoryRoadhogLogger();
    var gameApi = new FakeGameApi
    {
        Skills = CreateSkillSnapshotsById(CreateCombatRootCooldowns(CooldownEndIn(500)))
    };
    var controller = new SemiAutoCombatController(keyboard);
    var state = new SemiAutoCombatState();
    CalibrateCooldownClock(state);

    await controller.TickAsync(CreateContext(settings, gameApi, logger), plan, state).ConfigureAwait(false);

    AssertFalse(!state.HasCooldownTickCalibration, "plausible cooldowns should keep cooldown calibration");
    AssertFalse(
        logger.Entries.Any(entry => entry.EventName == "semi_auto.cooldown.calibration_invalidated"),
        "plausible cooldowns should not log calibration invalidation");
}

static async Task TestInvalidatedCooldownCalibrationRebuildsAfterPressedSkillAdvancesAsync()
{
    var settings = CreateScriptSettings();
    var plan = SemiAutoSkillPlan.FromSettings(settings.Skills);
    var keyboard = new RecordingKeyboardInput();
    var logger = new InMemoryRoadhogLogger();
    var firstEndTick = CooldownEndIn(130_000);
    var gameApi = new FakeGameApi
    {
        Skills = CreateSkillSnapshotsById(CreateCombatRootCooldowns(firstEndTick))
    };
    var controller = new SemiAutoCombatController(keyboard);
    var state = new SemiAutoCombatState();
    CalibrateCooldownClock(state);

    await controller.TickAsync(CreateContext(settings, gameApi, logger), plan, state).ConfigureAwait(false);
    AssertFalse(state.HasCooldownTickCalibration, "first tick should clear stale cooldown calibration");
    AssertFalse(!keyboard.Keys.Contains("D1"), "first tick should press D1 after invalidating calibration");

    gameApi.Skills = CreateSkillSnapshotsById(new Dictionary<uint, uint>(CreateCombatRootCooldowns(firstEndTick))
    {
        [1] = unchecked(firstEndTick + 1_000u)
    });
    keyboard.Keys.Clear();

    await controller.TickAsync(CreateContext(settings, gameApi, logger), plan, state).ConfigureAwait(false);

    AssertFalse(!state.HasCooldownTickCalibration, "advanced pressed skill cooldown should rebuild calibration");
    AssertFalse(
        !logger.Entries.Any(entry =>
            entry.EventName == "semi_auto.cooldown.calibrated" &&
            Convert.ToUInt32(entry.Fields.GetValueOrDefault("skillId")) == 1u),
        "advanced pressed skill cooldown should log recalibration");
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
    settings.SemiAuto.ConfirmTimeoutMs = 20;
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

    await Task.Delay(60).ConfigureAwait(false);
    await controller.TickAsync(CreateContext(settings, gameApi, logger), plan, state).ConfigureAwait(false);
    AssertSequence(new[] { "NumPad0", "NumPad0" }, keyboard.Keys.ToArray(), "opening skill should retry after the normal confirmation window expires");

    gameApi.Skills = CreateSkillSnapshotsById(new Dictionary<uint, uint>
    {
        [8] = ActiveCooldownEnd()
    });
    await controller.TickAsync(CreateContext(settings, gameApi, logger), plan, state).ConfigureAwait(false);
    AssertSequence(new[] { "NumPad0", "NumPad0", "C" }, keyboard.Keys.ToArray(), "opening C should run after opening skill cooldown confirms");
}

static async Task TestOpeningSkillConfirmTimeoutReleasesNormalSkillLoopAsync()
{
    const string timeoutEnvVar = "ROADHOG_OPENING_SKILL_CONFIRM_TIMEOUT_MS";
    var previousTimeout = Environment.GetEnvironmentVariable(timeoutEnvVar);
    Environment.SetEnvironmentVariable(timeoutEnvVar, "30");

    try
    {
        var settings = CreateScriptSettings();
        settings.SemiAuto.AttackKeyLoopEnabled = false;
        settings.SemiAuto.ConfirmTimeoutMs = 1000;
        settings.Skills.ExecutionTree = new List<SkillConfigNode>
        {
            Node(1702, "Normal Skill", "active")
        };
        settings.Skills.OpeningSkill = new OpeningSkillConfig
        {
            Enabled = true,
            SkillId = 1701,
            SkillName = "Opening Skill",
            Key = "NumPad0"
        };
        var plan = SemiAutoSkillPlan.FromSettings(settings.Skills);
        var keyboard = new RecordingKeyboardInput();
        var logger = new InMemoryRoadhogLogger();
        var gameApi = new FakeGameApi
        {
            Skills = new[]
            {
                new SkillSnapshot(1701, "Opening Skill", 1, 1, "Opening Skill", 1, false, 1_000, 0),
                new SkillSnapshot(1702, "Normal Skill", 1, 1, "Normal Skill", 1, false, 1_000, 0)
            }
        };
        var controller = new SemiAutoCombatController(keyboard);
        var state = new SemiAutoCombatState();
        CalibrateCooldownClock(state);

        await controller.TickAsync(CreateContext(settings, gameApi, logger), plan, state).ConfigureAwait(false);
        AssertSequence(new[] { "NumPad0" }, keyboard.Keys.ToArray(), "opening skill should press first");

        keyboard.Keys.Clear();
        await Task.Delay(60).ConfigureAwait(false);
        await controller.TickAsync(CreateContext(settings, gameApi, logger), plan, state).ConfigureAwait(false);

        AssertSequence(new[] { "D1" }, keyboard.Keys.ToArray(), "normal skill loop should run after opening confirm timeout");
        AssertFalse(
            !logger.Entries.Any(entry => entry.EventName == "semi_auto.opening_skill.confirm_timeout"),
            "opening confirm timeout should be logged");
    }
    finally
    {
        Environment.SetEnvironmentVariable(timeoutEnvVar, previousTimeout);
    }
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
    const uint openingCooldownDuration = 200;
    var gameApi = new FakeGameApi
    {
        TargetEntityId = 100,
        TargetOwnServerObjectId = 5000,
        Skills = new[]
        {
            new SkillSnapshot(999, "Opening Skill", 1, 1, "Opening Skill", 1, false, openingCooldownDuration, 0)
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
    AssertSequence(new[] { "NumPad0" }, keyboard.Keys.ToArray(), "same server object should keep retrying until cooldown confirms");

    keyboard.Keys.Clear();
    var openingCooldownEnd = CooldownEndIn((int)openingCooldownDuration);
    gameApi.Skills = new[]
    {
        new SkillSnapshot(999, "Opening Skill", 1, 1, "Opening Skill", 1, false, openingCooldownDuration, openingCooldownEnd)
    };
    await controller.TickAsync(CreateContext(settings, gameApi, logger), plan, state).ConfigureAwait(false);
    AssertSequence(Array.Empty<string>(), keyboard.Keys.ToArray(), "same server object should stop retrying once cooldown confirms");

    gameApi.TargetOwnServerObjectId = 6000;
    await Task.Delay(300).ConfigureAwait(false);
    gameApi.Skills = new[]
    {
        new SkillSnapshot(999, "Opening Skill", 1, 1, "Opening Skill", 1, false, openingCooldownDuration, 0)
    };
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

static async Task TestCoolingOpeningSkillDoesNotRetrySameTargetAfterCooldownAsync()
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
    AssertSequence(Array.Empty<string>(), keyboard.Keys.ToArray(), "same target should not retry opening skill after cooldown becomes ready");

    gameApi.TargetOwnServerObjectId = 6000;
    await controller.TickAsync(CreateContext(settings, gameApi, logger), plan, state).ConfigureAwait(false);
    AssertSequence(new[] { "NumPad0" }, keyboard.Keys.ToArray(), "next target should still press opening skill when ready");
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

static async Task TestMaintenanceMpPotionMatchesAdditionalNamesAsync()
{
    foreach (var potionName in new[] { "高级精神恢复剂", "高级精神秘药" })
    {
        var settings = CreateScriptSettings();
        settings.Maintenance.SitMaintenanceEnabled = false;
        settings.Maintenance.MpMaintenanceRules.Add(new MaintenanceKeyRuleConfig
        {
            BelowPercent = 60,
            ActionType = MaintenanceRuleActionType.Potion,
            Key = "NumPad2"
        });
        var plan = SemiAutoSkillPlan.FromSettings(settings.Skills);
        var keyboard = new RecordingKeyboardInput();
        var gameApi = new FakeGameApi
        {
            Player = new PlayerSnapshot(1, 100, "Fake", 100, 100, 20, 100, 0, new Vector3Snapshot(0, 0, 0), DateTimeOffset.Now),
            InventoryItems = new[]
            {
                new InventoryItemSnapshot(1, 1, potionName, 1, 0, false, 17)
            }
        };

        await new SemiAutoCombatController(keyboard)
            .TickAsync(CreateContext(settings, gameApi, new InMemoryRoadhogLogger()), plan, new SemiAutoCombatState())
            .ConfigureAwait(false);

        AssertSequence(new[] { "NumPad2" }, keyboard.Keys.ToArray(), $"mp potion name {potionName}");
    }
}

static async Task TestMaintenanceMpPotionRejectsWrongTypeAndFallsBackAsync()
{
    var settings = CreateScriptSettings();
    settings.Maintenance.SitMaintenanceEnabled = false;
    settings.Maintenance.MpMaintenanceRules.Add(new MaintenanceKeyRuleConfig
    {
        BelowPercent = 60,
        ActionType = MaintenanceRuleActionType.Potion,
        Key = "NumPad2"
    });
    settings.Maintenance.MpMaintenanceRules.Add(new MaintenanceKeyRuleConfig
    {
        BelowPercent = 60,
        Key = "NumPad3",
        SkillId = 1,
        SkillName = "Mana Skill"
    });
    var plan = SemiAutoSkillPlan.FromSettings(settings.Skills);
    var keyboard = new RecordingKeyboardInput();
    var gameApi = new FakeGameApi
    {
        Player = new PlayerSnapshot(1, 100, "Fake", 100, 100, 20, 100, 0, new Vector3Snapshot(0, 0, 0), DateTimeOffset.Now),
        InventoryItems = new[]
        {
            new InventoryItemSnapshot(1, 1, "魔石:精神力+50", 1, 0, false, 24)
        },
        Skills = CreateSkillSnapshotsById(new Dictionary<uint, uint> { [1] = 0 })
    };
    keyboard.AfterPress = key =>
    {
        if (key == "NumPad3")
        {
            gameApi.Skills = CreateSkillSnapshotsById(new Dictionary<uint, uint> { [1] = ActiveCooldownEnd() });
        }
    };
    var state = new SemiAutoCombatState();
    CalibrateCooldownClock(state);

    await new SemiAutoCombatController(keyboard)
        .TickAsync(CreateContext(settings, gameApi, new InMemoryRoadhogLogger()), plan, state)
        .ConfigureAwait(false);

    AssertSequence(new[] { "NumPad3" }, keyboard.Keys.ToArray(), "wrong item type should skip potion and use skill");
}

static async Task TestMaintenanceMpPotionRunsBeforeSkillAsync()
{
    var settings = CreateScriptSettings();
    settings.Maintenance.SitMaintenanceEnabled = false;
    settings.Maintenance.MpMaintenanceRules.Add(new MaintenanceKeyRuleConfig
    {
        BelowPercent = 60,
        Key = "NumPad3",
        SkillId = 1,
        SkillName = "Mana Skill"
    });
    settings.Maintenance.MpMaintenanceRules.Add(new MaintenanceKeyRuleConfig
    {
        BelowPercent = 60,
        ActionType = MaintenanceRuleActionType.Potion,
        Key = "NumPad2"
    });
    var plan = SemiAutoSkillPlan.FromSettings(settings.Skills);
    var keyboard = new RecordingKeyboardInput();
    var gameApi = new FakeGameApi
    {
        Player = new PlayerSnapshot(1, 100, "Fake", 100, 100, 20, 100, 0, new Vector3Snapshot(0, 0, 0), DateTimeOffset.Now),
        InventoryItems = new[]
        {
            new InventoryItemSnapshot(1, 1, "上级精神之仙药", 1, 0, false, 17)
        },
        Skills = CreateSkillSnapshotsById(new Dictionary<uint, uint> { [1] = 0 })
    };
    keyboard.AfterPress = key =>
    {
        if (key == "NumPad3")
        {
            gameApi.Skills = CreateSkillSnapshotsById(new Dictionary<uint, uint> { [1] = ActiveCooldownEnd() });
        }
    };
    var controller = new SemiAutoCombatController(keyboard);
    var state = new SemiAutoCombatState();
    CalibrateCooldownClock(state);
    var context = CreateContext(settings, gameApi, new InMemoryRoadhogLogger());

    await controller.TickAsync(context, plan, state).ConfigureAwait(false);
    state.MarkMaintenanceKeyAttempted("NumPad2", DateTimeOffset.Now - TimeSpan.FromSeconds(1));
    await controller.TickAsync(context, plan, state).ConfigureAwait(false);

    AssertSequence(new[] { "NumPad2", "NumPad3" }, keyboard.Keys.ToArray(), "potion should run first, then skill on next low-mp tick");
}

static async Task TestMaintenanceGlobalIntervalThrottlesDifferentSelectedSkillAsync()
{
    var settings = CreateScriptSettings();
    settings.Maintenance.SitMaintenanceEnabled = false;
    settings.Maintenance.StatusMaintenanceRules.Add(new StatusMaintenanceRuleConfig
    {
        Key = "NumPad2",
        SkillId = 2,
        SkillName = "Status Buff",
        RunTiming = MaintenanceRuleRunTiming.Always
    });
    settings.Maintenance.MpMaintenanceRules.Add(new MaintenanceKeyRuleConfig
    {
        BelowPercent = 60,
        Key = "NumPad3",
        SkillId = 3,
        SkillName = "Mana Skill"
    });
    var plan = SemiAutoSkillPlan.FromSettings(settings.Skills);
    var keyboard = new RecordingKeyboardInput();
    var logger = new InMemoryRoadhogLogger();
    var gameApi = new FakeGameApi
    {
        Player = new PlayerSnapshot(1, 100, "Fake", 100, 100, 20, 100, 0, new Vector3Snapshot(0, 0, 0), DateTimeOffset.Now),
        PlayerAbnormalStatuses = PlayerAbnormalStatusSnapshot.Empty(1),
        Skills = new[]
        {
            new SkillSnapshot(2, "Status Buff", 1, 1, "Status Buff", 1, false, 5_000, 0),
            new SkillSnapshot(3, "Mana Skill", 1, 1, "Mana Skill", 1, false, 5_000, 0)
        }
    };
    keyboard.AfterPress = key =>
    {
        if (key == "NumPad2")
        {
            gameApi.PlayerAbnormalStatuses = new PlayerAbnormalStatusSnapshot(
                1,
                DateTimeOffset.Now,
                1,
                new[] { Abnormal(2, 0) });
        }
        else if (key == "NumPad3")
        {
            gameApi.Skills = new[]
            {
                new SkillSnapshot(2, "Status Buff", 1, 1, "Status Buff", 1, false, 5_000, 0),
                new SkillSnapshot(3, "Mana Skill", 1, 1, "Mana Skill", 1, false, 5_000, ActiveCooldownEnd())
            };
        }
    };
    var controller = new SemiAutoCombatController(keyboard);
    var state = new SemiAutoCombatState();

    await controller.TryHandleMaintenanceAsync(CreateContext(settings, gameApi, logger), state, gameApi.Player).ConfigureAwait(false);
    AssertSequence(new[] { "NumPad2" }, keyboard.Keys.ToArray(), "first maintenance tick should press status key");

    state.MarkMaintenanceKeyAttempted("NumPad2", DateTimeOffset.Now);
    keyboard.Keys.Clear();
    await controller.TryHandleMaintenanceAsync(CreateContext(settings, gameApi, logger), state, gameApi.Player).ConfigureAwait(false);
    AssertSequence(Array.Empty<string>(), keyboard.Keys.ToArray(), "different maintenance key should wait for global interval");

    state.MarkMaintenanceKeyAttempted("NumPad2", DateTimeOffset.Now - TimeSpan.FromSeconds(1));
    await controller.TryHandleMaintenanceAsync(CreateContext(settings, gameApi, logger), state, gameApi.Player).ConfigureAwait(false);
    AssertSequence(new[] { "NumPad3" }, keyboard.Keys.ToArray(), "different maintenance key should run after global interval");
}

static async Task TestAfterCombatMpPotionSkipsInventoryAndPressesOnceAsync()
{
    var settings = CreateScriptSettings();
    settings.Maintenance.SitMaintenanceEnabled = false;
    settings.Maintenance.MpMaintenanceRules.Add(new MaintenanceKeyRuleConfig
    {
        BelowPercent = 60,
        ActionType = MaintenanceRuleActionType.Potion,
        Key = "NumPadAdd",
        RunTiming = MaintenanceRuleRunTiming.AfterCombat
    });

    var keyboard = new RecordingKeyboardInput();
    var logger = new InMemoryRoadhogLogger();
    var gameApi = new FakeGameApi
    {
        Player = new PlayerSnapshot(1, 100, "Fake", 100, 100, 20, 100, 0, new Vector3Snapshot(0, 0, 0), DateTimeOffset.Now)
    };
    var state = new SemiAutoCombatState();

    var handled = await new SemiAutoCombatController(keyboard)
        .TryHandleMaintenanceAsync(
            CreateContext(settings, gameApi, logger),
            state,
            gameApi.Player,
            allowSitMaintenance: false,
            clearSitWhenDisallowed: false,
            runTiming: MaintenanceRuleRunTiming.AfterCombat)
        .ConfigureAwait(false);

    AssertFalse(!handled, "after-combat mp potion should be handled");
    AssertSequence(new[] { "NumPadAdd" }, keyboard.Keys.ToArray(), "after-combat mp potion should press once");
    AssertFalse(gameApi.LastInventoryContext is not null, "after-combat mp potion should not read inventory");

    var entry = logger.Entries.LastOrDefault(entry => entry.EventName == "semi_auto.maintenance.potion_pressed");
    AssertFalse(entry is null, "after-combat mp potion should log one maintenance action");
    AssertEqual(1, Convert.ToInt32(entry!.Fields["pressCount"]), "after-combat potion press count");
    AssertEqual(0L, Convert.ToInt64(entry.Fields["pressIntervalMs"]), "after-combat potion press interval");
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

static async Task TestDpMaintenanceSkipsBelowRequiredDpAsync()
{
    var settings = CreateScriptSettings();
    settings.Maintenance.SitMaintenanceEnabled = false;
    settings.Maintenance.DpMaintenanceRules.Add(new DpMaintenanceRuleConfig
    {
        RequiredDp = 2000,
        Key = "NumPad9",
        SkillId = 1,
        SkillName = "DP Buff"
    });
    var plan = SemiAutoSkillPlan.FromSettings(settings.Skills);
    var keyboard = new RecordingKeyboardInput();
    var logger = new InMemoryRoadhogLogger();
    var gameApi = new FakeGameApi
    {
        Player = new PlayerSnapshot(1, 100, "Fake", 100, 100, 100, 100, 1000, new Vector3Snapshot(0, 0, 0), DateTimeOffset.Now),
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

    AssertFalse(keyboard.Keys.Contains("NumPad9"), "dp maintenance key must not press below required dp");
    AssertFalse(logger.Entries.Any(entry => entry.EventName == "semi_auto.maintenance.dp_key_pressed"), "below required dp should not log dp maintenance key press");
}

static async Task TestDpMaintenancePressesConfiguredKeyAtRequiredDpAsync()
{
    var settings = CreateScriptSettings();
    settings.Maintenance.SitMaintenanceEnabled = false;
    settings.Maintenance.DpMaintenanceRules.Add(new DpMaintenanceRuleConfig
    {
        RequiredDp = 2000,
        Key = "NumPad9",
        SkillId = 1,
        SkillName = "DP Buff"
    });
    var plan = SemiAutoSkillPlan.FromSettings(settings.Skills);
    var keyboard = new RecordingKeyboardInput();
    var logger = new InMemoryRoadhogLogger();
    var gameApi = new FakeGameApi
    {
        Player = new PlayerSnapshot(1, 100, "Fake", 100, 100, 100, 100, 2000, new Vector3Snapshot(0, 0, 0), DateTimeOffset.Now),
        Skills = CreateSkillSnapshotsById(new Dictionary<uint, uint>
        {
            [1] = 0,
            [5] = 0,
            [6] = 0
        })
    };
    keyboard.AfterPress = key =>
    {
        if (string.Equals(key, "NumPad9", StringComparison.Ordinal))
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

    AssertSequence(new[] { "NumPad9" }, keyboard.Keys.ToArray(), "ready dp maintenance key");
    var entry = logger.Entries.LastOrDefault(entry => entry.EventName == "semi_auto.maintenance.dp_key_pressed");
    AssertFalse(entry is null, "dp maintenance key press should be logged");
    AssertEqual(2000, Convert.ToInt32(entry!.Fields["requiredDp"]), "dp maintenance required dp");
    AssertEqual(1u, Convert.ToUInt32(entry.Fields["confirmedSkillId"]), "confirmed dp maintenance skill id");
}

static async Task TestDpMaintenanceSelectedCoolingSkillSkipsKeyAsync()
{
    var settings = CreateScriptSettings();
    settings.Maintenance.SitMaintenanceEnabled = false;
    settings.Maintenance.DpMaintenanceRules.Add(new DpMaintenanceRuleConfig
    {
        RequiredDp = 2000,
        Key = "NumPad9",
        SkillId = 1,
        SkillName = "DP Buff"
    });
    var plan = SemiAutoSkillPlan.FromSettings(settings.Skills);
    var keyboard = new RecordingKeyboardInput();
    var logger = new InMemoryRoadhogLogger();
    var gameApi = new FakeGameApi
    {
        Player = new PlayerSnapshot(1, 100, "Fake", 100, 100, 100, 100, 4000, new Vector3Snapshot(0, 0, 0), DateTimeOffset.Now),
        Skills = CreateSkillSnapshotsById(new Dictionary<uint, uint>
        {
            [1] = CooldownEndIn(30_000),
            [5] = 0,
            [6] = 0
        })
    };
    var controller = new SemiAutoCombatController(keyboard);
    var state = new SemiAutoCombatState();
    CalibrateCooldownClock(state);

    await controller.TickAsync(CreateContext(settings, gameApi, logger), plan, state).ConfigureAwait(false);

    AssertFalse(keyboard.Keys.Contains("NumPad9"), "cooling selected dp maintenance skill should not press maintenance key");
    AssertFalse(
        !logger.Entries.Any(entry => entry.EventName == "semi_auto.maintenance.dp_skill_cooling"),
        "cooling selected dp maintenance skill should be logged");
}

static async Task TestStatusMaintenancePressesMissingBuffAndLearnsAbnormalIdAsync()
{
    var settings = CreateScriptSettings();
    settings.Maintenance.SitMaintenanceEnabled = false;
    settings.Maintenance.StatusMaintenanceRules.Add(new StatusMaintenanceRuleConfig
    {
        Key = "NumPad2",
        SkillId = 1,
        SkillName = "Status Buff",
        RunTiming = MaintenanceRuleRunTiming.Always
    });
    var keyboard = new RecordingKeyboardInput();
    var logger = new InMemoryRoadhogLogger();
    var gameApi = new FakeGameApi
    {
        Player = new PlayerSnapshot(1, 100, "Fake", 100, 100, 100, 100, 0, new Vector3Snapshot(0, 0, 0), DateTimeOffset.Now),
        PlayerAbnormalStatuses = PlayerAbnormalStatusSnapshot.Empty(1),
        Skills = new[]
        {
            new SkillSnapshot(1, "Status Buff", 1, 1, "Status Buff", 1, false, 5_000, 0)
        }
    };
    keyboard.AfterPress = key =>
    {
        if (string.Equals(key, "NumPad2", StringComparison.Ordinal))
        {
            gameApi.PlayerAbnormalStatuses = new PlayerAbnormalStatusSnapshot(
                1,
                DateTimeOffset.Now,
                1,
                new[] { Abnormal(4001, 0) });
        }
    };

    var controller = new SemiAutoCombatController(keyboard);
    var state = new SemiAutoCombatState();

    await controller.TryHandleMaintenanceAsync(CreateContext(settings, gameApi, logger), state, gameApi.Player).ConfigureAwait(false);

    AssertSequence(new[] { "NumPad2" }, keyboard.Keys.ToArray(), "missing status maintenance should press configured key");
    var entry = logger.Entries.LastOrDefault(entry => entry.EventName == "semi_auto.maintenance.status_key_pressed");
    AssertFalse(entry is null, "status maintenance should log confirmed status key press");
    AssertEqual(4001u, Convert.ToUInt32(entry!.Fields["abnormalStatusId"]), "learned status abnormal id");

    await controller.TryHandleMaintenanceAsync(CreateContext(settings, gameApi, logger), state, gameApi.Player).ConfigureAwait(false);

    AssertEqual(1, keyboard.Keys.Count(key => key == "NumPad2"), "learned active status should block repeat press");
}

static async Task TestSupportStatusMaintenanceSelectsSelfBeforeBuffAsync()
{
    const uint selfServerObjectId = 1879081233;
    const uint leaderServerObjectId = 1879081195;
    var settings = CreateScriptSettings();
    settings.Team.Role = TeamRole.Support;
    settings.Team.Support.Enabled = true;
    settings.Maintenance.SitMaintenanceEnabled = false;
    settings.Maintenance.StatusMaintenanceRules.Add(new StatusMaintenanceRuleConfig
    {
        Key = "NumPad3",
        SkillId = 955,
        SkillName = "Protection Blessing",
        RunTiming = MaintenanceRuleRunTiming.Always
    });
    var keyboard = new RecordingKeyboardInput();
    var logger = new InMemoryRoadhogLogger();
    var gameApi = new FakeGameApi
    {
        Player = new PlayerSnapshot(65535, 65475, "Support", 100, 100, 100, 100, 0, new Vector3Snapshot(0, 0, 0), DateTimeOffset.Now),
        PlayerAbnormalStatuses = PlayerAbnormalStatusSnapshot.Empty(65535),
        LocalServerObjectId = selfServerObjectId,
        TargetEntityId = 65475,
        TargetOwnServerObjectId = leaderServerObjectId,
        TargetObjectType = LockedTargetSnapshot.PlayerObjectType,
        TargetCurrentHp = 100,
        TargetMaxHp = 100,
        Skills = new[]
        {
            new SkillSnapshot(955, "Protection Blessing", 1, 1, "Protection Blessing", 1, false, 5_000, 0)
        }
    };
    keyboard.AfterPress = key =>
    {
        if (string.Equals(key, "F1", StringComparison.Ordinal))
        {
            gameApi.TargetEntityId = gameApi.Player.EntityId;
            gameApi.TargetOwnServerObjectId = selfServerObjectId;
            gameApi.TargetObjectType = LockedTargetSnapshot.PlayerObjectType;
        }
        else if (string.Equals(key, "NumPad3", StringComparison.Ordinal))
        {
            gameApi.PlayerAbnormalStatuses = new PlayerAbnormalStatusSnapshot(
                gameApi.Player.EntityId,
                DateTimeOffset.Now,
                1,
                new[] { Abnormal(955, 0) });
        }
    };

    var controller = new SemiAutoCombatController(keyboard);
    var state = new SemiAutoCombatState();

    await controller
        .TryHandleMaintenanceAsync(CreateContext(settings, gameApi, logger), state, gameApi.Player)
        .ConfigureAwait(false);

    AssertSequence(new[] { "F1", "NumPad3" }, keyboard.Keys.ToArray(), "support status maintenance should select self before pressing the buff key");
    AssertFalse(
        gameApi.LastLockedTargetContext?.BypassMemoryCache != true,
        "self-target confirmation should bypass memory cache");
    var selectionEntry = logger.Entries.LastOrDefault(entry => entry.EventName == "semi_auto.maintenance.self_target_selected");
    AssertFalse(selectionEntry is null, "support self-target selection should be logged");
    AssertEqual(false, Convert.ToBoolean(selectionEntry!.Fields["alreadySelected"]), "selection log should record F1 selection");
    AssertFalse(
        logger.Entries.Any(entry => entry.EventName == "semi_auto.maintenance.self_target_select.failed"),
        "successful F1 self-selection should not log failure");
}

static async Task TestStatusMaintenanceChantFollowsActiveStatusAsync()
{
    var settings = CreateScriptSettings();
    settings.Maintenance.SitMaintenanceEnabled = false;
    settings.Maintenance.StatusMaintenanceRules.Add(new StatusMaintenanceRuleConfig
    {
        Key = "NumPad2",
        SkillId = 8200,
        SkillName = "\u75BE\u98CE\u771F\u8A00 I",
        RunTiming = MaintenanceRuleRunTiming.Always
    });
    var keyboard = new RecordingKeyboardInput();
    var logger = new InMemoryRoadhogLogger();
    var gameApi = new FakeGameApi
    {
        Player = new PlayerSnapshot(1, 100, "Fake", 100, 100, 100, 100, 0, new Vector3Snapshot(0, 0, 0), DateTimeOffset.Now),
        PlayerAbnormalStatuses = PlayerAbnormalStatusSnapshot.Empty(1),
        Skills = new[]
        {
            new SkillSnapshot(
                8200,
                "\u75BE\u98CE\u771F\u8A00 I",
                1,
                1,
                "\u75BE\u98CE\u771F\u8A00",
                1,
                false,
                5_000,
                0,
                XmlSkillCategory: "Chant")
        }
    };
    keyboard.AfterPress = key =>
    {
        if (string.Equals(key, "NumPad2", StringComparison.Ordinal))
        {
            gameApi.PlayerAbnormalStatuses = new PlayerAbnormalStatusSnapshot(
                1,
                DateTimeOffset.Now,
                1,
                new[] { Abnormal(8232, PlayerAbnormalStatusSnapshot.BuffCategory) });
        }
    };

    var controller = new SemiAutoCombatController(keyboard);
    var state = new SemiAutoCombatState();

    await controller.TryHandleMaintenanceAsync(CreateContext(settings, gameApi, logger), state, gameApi.Player).ConfigureAwait(false);
    AssertSequence(new[] { "NumPad2" }, keyboard.Keys.ToArray(), "chant status maintenance should press once");
    var pressedEntry = logger.Entries.LastOrDefault(entry => entry.EventName == "semi_auto.maintenance.status_key_pressed");
    AssertFalse(pressedEntry is null, "chant status maintenance should log the first key press");
    AssertEqual(true, Convert.ToBoolean(pressedEntry!.Fields["oneShot"]), "chant maintenance should keep legacy one-shot log flag");
    AssertEqual(true, Convert.ToBoolean(pressedEntry!.Fields["chant"]), "chant maintenance should be logged as chant");
    AssertFalse(
        !state.TryGetStatusMaintenanceActiveSeenAt("skill:8200", out _),
        "confirmed chant maintenance should remember sticky active status");

    keyboard.Keys.Clear();
    await controller.TryHandleMaintenanceAsync(CreateContext(settings, gameApi, logger), state, gameApi.Player).ConfigureAwait(false);

    AssertSequence(Array.Empty<string>(), keyboard.Keys.ToArray(), "active chant status should block repeat press");
    gameApi.Player = gameApi.Player with { CurrentHp = 0, CapturedAt = DateTimeOffset.Now };
    await controller.TryRecoverAfterReviveAsync(CreateContext(settings, gameApi, logger), state, gameApi.Player).ConfigureAwait(false);
    AssertFalse(
        state.TryGetStatusMaintenanceActiveSeenAt("skill:8200", out _),
        "death/revive recovery should clear sticky chant active status");
    gameApi.Player = gameApi.Player with { CurrentHp = 100, CapturedAt = DateTimeOffset.Now };

    var retryState = new SemiAutoCombatState();
    retryState.RememberStatusMaintenanceAbnormalId(8200, 8232);
    retryState.MarkStatusMaintenanceActive("skill:8200", DateTimeOffset.Now);
    keyboard.Keys.Clear();
    gameApi.PlayerAbnormalStatuses = PlayerAbnormalStatusSnapshot.Empty(1);
    var retryLogger = new InMemoryRoadhogLogger();

    await controller.TryHandleMaintenanceAsync(CreateContext(settings, gameApi, retryLogger), retryState, gameApi.Player).ConfigureAwait(false);

    AssertSequence(Array.Empty<string>(), keyboard.Keys.ToArray(), "first missing learned chant status should defer maintenance press");
    var deferredEntry = retryLogger.Entries.LastOrDefault(entry => entry.EventName == "semi_auto.maintenance.chant_missing_deferred");
    AssertFalse(deferredEntry is null, "first missing learned chant status should log deferred maintenance");
    AssertEqual(1, Convert.ToInt32(deferredEntry!.Fields["missingReadCount"]), "first missing learned chant read count");
    AssertEqual(3, Convert.ToInt32(deferredEntry.Fields["requiredMissingReads"]), "chant missing read threshold");
    AssertEqual(60000L, Convert.ToInt64(deferredEntry.Fields["requiredMissingDurationMs"]), "chant missing duration threshold");
    AssertEqual(true, Convert.ToBoolean(deferredEntry.Fields["stickyActive"]), "missing confirmed chant should be treated as sticky active");

    await controller.TryHandleMaintenanceAsync(CreateContext(settings, gameApi, retryLogger), retryState, gameApi.Player).ConfigureAwait(false);

    AssertSequence(Array.Empty<string>(), keyboard.Keys.ToArray(), "second immediate missing learned chant status should defer maintenance press");
    var secondDeferredEntry = retryLogger.Entries.LastOrDefault(entry => entry.EventName == "semi_auto.maintenance.chant_missing_deferred");
    AssertFalse(secondDeferredEntry is null, "second missing learned chant status should log deferred maintenance");
    AssertEqual(2, Convert.ToInt32(secondDeferredEntry!.Fields["missingReadCount"]), "second missing learned chant read count");

    await controller.TryHandleMaintenanceAsync(CreateContext(settings, gameApi, retryLogger), retryState, gameApi.Player).ConfigureAwait(false);

    AssertSequence(Array.Empty<string>(), keyboard.Keys.ToArray(), "third immediate missing learned chant status should still wait for duration threshold");
    var thirdDeferredEntry = retryLogger.Entries.LastOrDefault(entry => entry.EventName == "semi_auto.maintenance.chant_missing_deferred");
    AssertFalse(thirdDeferredEntry is null, "third missing learned chant status should log deferred maintenance");
    AssertEqual(3, Convert.ToInt32(thirdDeferredEntry!.Fields["missingReadCount"]), "third missing learned chant read count");

    var readyState = new SemiAutoCombatState();
    readyState.RememberStatusMaintenanceAbnormalId(8200, 8232);
    readyState.MarkStatusMaintenanceActive("skill:8200", DateTimeOffset.Now - TimeSpan.FromMinutes(5));
    var firstMissingAt = DateTimeOffset.Now - TimeSpan.FromSeconds(61);
    readyState.MarkStatusMaintenanceMissingRead("skill:8200", firstMissingAt, out _);
    readyState.MarkStatusMaintenanceMissingRead("skill:8200", firstMissingAt + TimeSpan.FromMilliseconds(500), out _);
    keyboard.Keys.Clear();
    gameApi.PlayerAbnormalStatuses = PlayerAbnormalStatusSnapshot.Empty(1);
    var readyLogger = new InMemoryRoadhogLogger();

    await controller.TryHandleMaintenanceAsync(CreateContext(settings, gameApi, readyLogger), readyState, gameApi.Player).ConfigureAwait(false);

    AssertSequence(new[] { "NumPad2" }, keyboard.Keys.ToArray(), "sustained missing learned chant status should allow maintenance press");
    var readyEntry = readyLogger.Entries.LastOrDefault(entry => entry.EventName == "semi_auto.maintenance.chant_missing_ready");
    AssertFalse(readyEntry is null, "sustained missing learned chant status should log ready maintenance");
    AssertEqual(3, Convert.ToInt32(readyEntry!.Fields["missingReadCount"]), "sustained missing learned chant read count");
    AssertEqual(true, Convert.ToBoolean(readyEntry.Fields["stickyActive"]), "sustained missing learned chant should record sticky active");
    AssertFalse(
        Convert.ToInt64(readyEntry.Fields["missingDurationMs"]) < 60000L,
        "sustained missing learned chant duration should reach threshold");
}

static async Task TestStatusMaintenanceSkipsActiveCategoryZeroBuffAsync()
{
    var settings = CreateScriptSettings();
    settings.Maintenance.SitMaintenanceEnabled = false;
    settings.Maintenance.StatusMaintenanceRules.Add(new StatusMaintenanceRuleConfig
    {
        Key = "NumPad2",
        SkillId = 1,
        SkillName = "Status Buff",
        RunTiming = MaintenanceRuleRunTiming.Always
    });
    var keyboard = new RecordingKeyboardInput();
    var logger = new InMemoryRoadhogLogger();
    var gameApi = new FakeGameApi
    {
        Player = new PlayerSnapshot(1, 100, "Fake", 100, 100, 100, 100, 0, new Vector3Snapshot(0, 0, 0), DateTimeOffset.Now),
        PlayerAbnormalStatuses = new PlayerAbnormalStatusSnapshot(
            1,
            DateTimeOffset.Now,
            1,
            new[] { Abnormal(1, 0) }),
        Skills = new[]
        {
            new SkillSnapshot(1, "Status Buff", 1, 1, "Status Buff", 1, false, 5_000, 0)
        }
    };
    var controller = new SemiAutoCombatController(keyboard);
    var state = new SemiAutoCombatState();

    await controller.TryHandleMaintenanceAsync(CreateContext(settings, gameApi, logger), state, gameApi.Player).ConfigureAwait(false);

    AssertSequence(Array.Empty<string>(), keyboard.Keys.ToArray(), "category zero active status should skip status maintenance key");
    AssertFalse(logger.Entries.Any(entry => entry.EventName == "semi_auto.maintenance.status_key_pressed"), "active status skip should not log status key press");
}

static async Task TestStatusMaintenanceInCombatRuleSkipsWithoutTargetAsync()
{
    var settings = CreateScriptSettings();
    settings.Maintenance.SitMaintenanceEnabled = false;
    settings.Maintenance.StatusMaintenanceRules.Add(new StatusMaintenanceRuleConfig
    {
        Key = "NumPad2",
        SkillId = 1,
        SkillName = "Status Buff",
        RunTiming = MaintenanceRuleRunTiming.InCombat
    });
    var plan = SemiAutoSkillPlan.FromSettings(settings.Skills);
    var keyboard = new RecordingKeyboardInput();
    var logger = new InMemoryRoadhogLogger();
    var gameApi = new FakeGameApi
    {
        Player = new PlayerSnapshot(1, 0, "Fake", 100, 100, 100, 100, 0, new Vector3Snapshot(0, 0, 0), DateTimeOffset.Now),
        TargetEntityId = 0,
        TargetCurrentHp = 0,
        TargetMaxHp = 0,
        TargetPosition = null,
        PlayerAbnormalStatuses = PlayerAbnormalStatusSnapshot.Empty(1),
        Skills = new[]
        {
            new SkillSnapshot(1, "Status Buff", 1, 1, "Status Buff", 1, false, 5_000, 0)
        }
    };
    var controller = new SemiAutoCombatController(keyboard);
    var state = new SemiAutoCombatState();

    await controller.TickAsync(CreateContext(settings, gameApi, logger), plan, state).ConfigureAwait(false);

    AssertFalse(keyboard.Keys.Contains("NumPad2"), "in-combat status maintenance must not run without an attackable target");
    AssertFalse(logger.Entries.Any(entry => entry.EventName == "semi_auto.maintenance.status_key_pressed"), "skipped in-combat status maintenance should not log a key press");
}

static async Task TestStatusMaintenanceCooldownDoesNotRecalibrateCombatClockAsync()
{
    var settings = CreateScriptSettings();
    settings.Maintenance.SitMaintenanceEnabled = false;
    settings.Maintenance.StatusMaintenanceRules.Add(new StatusMaintenanceRuleConfig
    {
        Key = "NumPad2",
        SkillId = 1378,
        SkillName = "Status Buff",
        RunTiming = MaintenanceRuleRunTiming.Always
    });
    var keyboard = new RecordingKeyboardInput();
    var logger = new InMemoryRoadhogLogger();
    var state = new SemiAutoCombatState();
    CalibrateCooldownClock(state);
    var originalOffset = state.CooldownTickOffsetMs;
    var osTick = unchecked((uint)Environment.TickCount64);
    var gameTick = state.EstimateGameTick(osTick);
    var statusCooldownEnd = unchecked(gameTick + 30_000u);
    var previousStatusSkill = new SkillSnapshot(
        1378,
        "Status Buff",
        1,
        1,
        "Status Buff",
        1,
        false,
        120_000,
        unchecked(statusCooldownEnd - 10_000u));
    var observedOnly = state.TryUpdateCooldownTickCalibration(
        new[] { previousStatusSkill },
        osTick,
        DateTimeOffset.Now,
        out _);
    AssertFalse(observedOnly, "first status maintenance cooldown observation should not calibrate");

    var gameApi = new FakeGameApi
    {
        Player = new PlayerSnapshot(1, 100, "Fake", 100, 100, 100, 100, 0, new Vector3Snapshot(0, 0, 0), DateTimeOffset.Now),
        PlayerAbnormalStatuses = PlayerAbnormalStatusSnapshot.Empty(1),
        Skills = new[]
        {
            previousStatusSkill with { CooldownEndTime = statusCooldownEnd }
        }
    };
    var controller = new SemiAutoCombatController(keyboard);

    await controller.TryHandleMaintenanceAsync(CreateContext(settings, gameApi, logger), state, gameApi.Player).ConfigureAwait(false);

    AssertSequence(Array.Empty<string>(), keyboard.Keys.ToArray(), "cooling status maintenance should not press key");
    AssertEqual(originalOffset, state.CooldownTickOffsetMs, "status maintenance cooldown should not recalibrate combat cooldown offset");
    AssertFalse(
        logger.Entries.Any(entry =>
            entry.EventName == "semi_auto.cooldown.calibrated" &&
            Convert.ToUInt32(entry.Fields.GetValueOrDefault("skillId")) == 1378u),
        "status maintenance cooldown should not log combat clock calibration");
}

static async Task TestMaintenanceCoolingSkillObservationDoesNotRecalibrateCombatClockAsync()
{
    var settings = CreateScriptSettings();
    settings.Maintenance.SitMaintenanceEnabled = false;
    settings.Maintenance.HpMaintenanceRules.Add(new MaintenanceKeyRuleConfig
    {
        BelowPercent = 50,
        Key = "NumPad0",
        SkillId = 1,
        SkillName = "濞ｅ洦绻冩慨銏＄▕鐎ｎ剚绂?I",
    });

    var keyboard = new RecordingKeyboardInput();
    var logger = new InMemoryRoadhogLogger();
    var previousMaintenanceSkill = CreateSkillSnapshotsById(new Dictionary<uint, uint>
    {
        [1] = CooldownEndIn(5_000)
    }).First(skill => skill.SkillId == 1);
    var gameApi = new FakeGameApi
    {
        Player = new PlayerSnapshot(1, 100, "Fake", 40, 100, 100, 100, 0, new Vector3Snapshot(0, 0, 0), DateTimeOffset.Now),
        Skills = CreateSkillSnapshotsById(new Dictionary<uint, uint>
        {
            [1] = CooldownEndIn(30_000),
            [6] = 0
        })
    };
    var controller = new SemiAutoCombatController(keyboard);
    var state = new SemiAutoCombatState();
    CalibrateCooldownClock(state);
    var originalOffset = state.CooldownTickOffsetMs;
    var observedOnly = state.TryUpdateCooldownTickCalibration(
        new[] { previousMaintenanceSkill },
        unchecked((uint)Environment.TickCount64),
        DateTimeOffset.Now,
        out _);
    AssertFalse(observedOnly, "first maintenance cooldown observation should not calibrate");

    await controller.TryHandleMaintenanceAsync(CreateContext(settings, gameApi, logger), state, gameApi.Player).ConfigureAwait(false);

    AssertSequence(Array.Empty<string>(), keyboard.Keys.ToArray(), "cooling maintenance skill should not press key");
    AssertEqual(originalOffset, state.CooldownTickOffsetMs, "cooling maintenance observation should not recalibrate combat cooldown offset");
    AssertFalse(
        logger.Entries.Any(entry =>
            entry.EventName == "semi_auto.cooldown.calibrated" &&
            Convert.ToUInt32(entry.Fields.GetValueOrDefault("skillId")) == 1u),
        "cooling maintenance observation should not log combat clock calibration");
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

static async Task TestChainClearsAcrossTargetGapAsync()
{
    var settings = CreateScriptSettings();
    var plan = SemiAutoSkillPlan.FromSettings(settings.Skills);
    var keyboard = new RecordingKeyboardInput();
    var logger = new InMemoryRoadhogLogger();
    var gameApi = new FakeGameApi
    {
        TargetServerObjectId = 1000
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
    AssertSequence(WithPreSkillAttackKey("D2", "D3", "D4", "D6"), keyboard.Keys.ToArray(), "source press before target gap");

    keyboard.Keys.Clear();
    gameApi.TargetCurrentHp = 0;
    await controller.TickAsync(context, plan, state).ConfigureAwait(false);
    AssertEqual(0, keyboard.Keys.Count, "dead target should not press");
    AssertFalse(state.HasChainWork, "dead target should clear pending chain");
    AssertFalse(
        !logger.Entries.Any(entry =>
            entry.EventName == "semi_auto.chain.ended" &&
            string.Equals(Convert.ToString(entry.Fields["reason"]), "target_not_attackable", StringComparison.Ordinal)),
        "dead target chain clear should be logged");

    gameApi.TargetEntityId = 200;
    gameApi.TargetServerObjectId = 2000;
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
    AssertSequence(WithPreSkillAttackKey("D2", "D3", "D4", "D7"), keyboard.Keys.ToArray(), "target switch should return to ordinary root selection");
    AssertEqual(plan.Roots.Single(root => root.SkillId == 7).Name, LastPressedSkill(logger), "target switch fallback root skill");

    var switchKeyboard = new RecordingKeyboardInput();
    var switchLogger = new InMemoryRoadhogLogger();
    var switchGameApi = new FakeGameApi
    {
        TargetEntityId = 100,
        TargetServerObjectId = 1000,
        Skills = CreateSkillSnapshotsById(new Dictionary<uint, uint>
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
        })
    };
    var switchController = new SemiAutoCombatController(switchKeyboard);
    var switchState = new SemiAutoCombatState();
    var switchContext = CreateContext(settings, switchGameApi, switchLogger);

    await switchController.TickAsync(switchContext, plan, switchState).ConfigureAwait(false);
    AssertFalse(!switchState.HasChainWork, "source press should leave pending chain before live target switch");

    switchKeyboard.Keys.Clear();
    switchGameApi.TargetEntityId = 201;
    switchGameApi.TargetServerObjectId = 2000;
    switchGameApi.Skills = CreateSkillSnapshotsById(new Dictionary<uint, uint>
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
    await switchController.TickAsync(switchContext, plan, switchState).ConfigureAwait(false);

    AssertSequence(WithPreSkillAttackKey("D2", "D3", "D4", "D7"), switchKeyboard.Keys.ToArray(), "live target switch should clear pending chain before child press");
    AssertFalse(switchState.HasChainWork, "live target switch should leave no pending chain after D7");
    AssertFalse(
        !switchLogger.Entries.Any(entry =>
            entry.EventName == "semi_auto.chain.ended" &&
            string.Equals(Convert.ToString(entry.Fields["reason"]), "target_changed", StringComparison.Ordinal)),
        "live target switch chain clear should be logged");
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

static async Task TestConditionSkillPreemptsPendingChainAsync()
{
    var settings = CreateConditionSkillSettings();
    var plan = SemiAutoSkillPlan.FromSettings(settings.Skills);
    var keyboard = new RecordingKeyboardInput();
    var logger = new InMemoryRoadhogLogger();
    var gameApi = new FakeGameApi
    {
        LockedTargetAbnormalStatuses = CreateLockedTargetAbnormalSnapshot(
            Abnormal(8218, PlayerAbnormalStatusSnapshot.PhysicalDebuffCategory))
    };
    var controller = new SemiAutoCombatController(keyboard);
    var state = new SemiAutoCombatState();
    var root = plan.Roots.Single(node => node.SkillId == 6);
    state.StartPendingChainAdvance(root, root.Children[0], DateTimeOffset.Now.AddSeconds(5), ActiveCooldownEnd(), 1200);
    gameApi.Skills = WithConditionSkillStatus(
        CreateSkillSnapshotsById(new Dictionary<uint, uint>
        {
            [1] = 0,
            [5] = ActiveCooldownEnd(),
            [6] = ActiveCooldownEnd(),
            [61] = 0,
            [62] = 0,
            [7] = ActiveCooldownEnd(),
            [8] = ActiveCooldownEnd(),
            [9] = ActiveCooldownEnd()
        }));

    await controller.TickAsync(CreateContext(settings, gameApi, logger), plan, state).ConfigureAwait(false);

    AssertSequence(new[] { "D1" }, keyboard.Keys.ToArray(), "condition skill should preempt chain without trigger prefix");
    AssertFalse(state.HasChainWork, "condition preempt should clear pending chain");
    AssertFalse(
        !logger.Entries.Any(entry =>
            entry.EventName == "semi_auto.chain.ended" &&
            string.Equals(Convert.ToString(entry.Fields["reason"]), "condition_preempted", StringComparison.Ordinal)),
        "condition preempt should log chain clear");
    var conditionLog = logger.Entries.LastOrDefault(entry => entry.EventName == "semi_auto.condition_skill.pressed");
    AssertFalse(conditionLog is null, "condition skill should log matched status");
    AssertEqual("Stumble", Convert.ToString(conditionLog!.Fields["conditionStatus"]) ?? string.Empty, "matched condition status");
    AssertEqual(8218L, Convert.ToInt64(conditionLog.Fields["conditionAbnormalId"]), "matched condition abnormal id");
    AssertEqual(true, Convert.ToBoolean(conditionLog.Fields["preemptedChain"]), "condition preempt flag");
}

static async Task TestConditionSkillPreemptSwitchKeepsPendingChainPriorityAsync()
{
    var settings = CreateConditionSkillSettings();
    settings.SemiAuto.ConditionSkillPreemptsChain = false;
    var plan = SemiAutoSkillPlan.FromSettings(settings.Skills);
    var keyboard = new RecordingKeyboardInput();
    var logger = new InMemoryRoadhogLogger();
    var gameApi = new FakeGameApi
    {
        LockedTargetAbnormalStatuses = CreateLockedTargetAbnormalSnapshot(
            Abnormal(8218, PlayerAbnormalStatusSnapshot.PhysicalDebuffCategory))
    };
    var controller = new SemiAutoCombatController(keyboard);
    var state = new SemiAutoCombatState();
    var root = plan.Roots.Single(node => node.SkillId == 6);
    state.StartPendingChainAdvance(root, root.Children[0], DateTimeOffset.Now.AddSeconds(5), ActiveCooldownEnd(), 1200);
    gameApi.Skills = WithConditionSkillStatus(
        CreateSkillSnapshotsById(new Dictionary<uint, uint>
        {
            [1] = 0,
            [5] = ActiveCooldownEnd(),
            [6] = ActiveCooldownEnd(),
            [61] = 0,
            [62] = 0,
            [7] = ActiveCooldownEnd(),
            [8] = ActiveCooldownEnd(),
            [9] = ActiveCooldownEnd()
        }));

    await controller.TickAsync(CreateContext(settings, gameApi, logger), plan, state).ConfigureAwait(false);

    AssertSequence(new[] { "D6" }, keyboard.Keys.ToArray(), "disabled preempt should keep pending chain first");
    AssertFalse(!state.HasChainWork, "pending chain should remain active after chain press");
    AssertFalse(gameApi.LastLockedTargetAbnormalContext is not null, "disabled chain preempt should not read condition abnormal");
}

static async Task TestConditionSkillWaitsForTargetStatusAsync()
{
    var settings = CreateConditionSkillSettings();
    var plan = SemiAutoSkillPlan.FromSettings(settings.Skills);
    var keyboard = new RecordingKeyboardInput();
    var logger = new InMemoryRoadhogLogger();
    var gameApi = new FakeGameApi
    {
        LockedTargetAbnormalStatuses = CreateLockedTargetAbnormalSnapshot()
    };
    var controller = new SemiAutoCombatController(keyboard);
    gameApi.Skills = WithConditionSkillStatus(
        CreateSkillSnapshotsById(new Dictionary<uint, uint>
        {
            [1] = 0,
            [5] = 0,
            [6] = ActiveCooldownEnd(),
            [7] = ActiveCooldownEnd(),
            [8] = ActiveCooldownEnd(),
            [9] = ActiveCooldownEnd()
        }));

    await controller.TickAsync(CreateContext(settings, gameApi, logger), plan, new SemiAutoCombatState()).ConfigureAwait(false);

    AssertSequence(WithPreSkillAttackKey("D2", "D3", "D4", "D5"), keyboard.Keys.ToArray(), "condition skill should wait for matching target abnormal");
    AssertFalse(keyboard.Keys.Contains("D1"), "condition skill must not fall through as ordinary root");
    AssertFalse(logger.Entries.Any(entry => entry.EventName == "semi_auto.condition_skill.pressed"), "unmatched condition should not press");
}

static async Task TestConditionSkillRespectsCooldownAsync()
{
    var settings = CreateConditionSkillSettings();
    var plan = SemiAutoSkillPlan.FromSettings(settings.Skills);
    var keyboard = new RecordingKeyboardInput();
    var logger = new InMemoryRoadhogLogger();
    var gameApi = new FakeGameApi
    {
        LockedTargetAbnormalStatuses = CreateLockedTargetAbnormalSnapshot(
            Abnormal(8218, PlayerAbnormalStatusSnapshot.PhysicalDebuffCategory))
    };
    var controller = new SemiAutoCombatController(keyboard);
    gameApi.Skills = WithConditionSkillStatus(
        CreateSkillSnapshotsById(new Dictionary<uint, uint>
        {
            [1] = ActiveCooldownEnd(),
            [5] = 0,
            [6] = ActiveCooldownEnd(),
            [7] = ActiveCooldownEnd(),
            [8] = ActiveCooldownEnd(),
            [9] = ActiveCooldownEnd()
        }));

    await controller.TickAsync(CreateContext(settings, gameApi, logger), plan, new SemiAutoCombatState()).ConfigureAwait(false);

    AssertSequence(WithPreSkillAttackKey("D2", "D3", "D4", "D5"), keyboard.Keys.ToArray(), "cooling condition skill should yield to normal root");
    AssertFalse(keyboard.Keys.Contains("D1"), "cooling condition skill should not press");
}

static async Task TestChainWindowUsesConfiguredDepthAsync()
{
    var twoStageWindow = await StartChainAndReadWindowAsync(CreateScriptSettings(), 5).ConfigureAwait(false);
    AssertEqual(600, twoStageWindow, "two-stage chain window");

    var threeStageWindow = await StartChainAndReadWindowAsync(CreateScriptSettings(), 6).ConfigureAwait(false);
    AssertEqual(1200, threeStageWindow, "three-stage chain window");

    var fourStageSettings = CreateScriptSettings();
    var root = fourStageSettings.Skills.ExecutionTree.Single(node => node.SkillId == 6);
    root.Children[0].Children[0].Children.Add(Node(64, "Fourth Chain Stage", "chain"));

    var fourStageWindow = await StartChainAndReadWindowAsync(fourStageSettings, 6).ConfigureAwait(false);
    AssertEqual(1800, fourStageWindow, "four-stage chain window");

    var customSettings = CreateScriptSettings();
    customSettings.SemiAuto.ChainWindowPerLinkMs = 800;
    var customWindow = await StartChainAndReadWindowAsync(customSettings, 6).ConfigureAwait(false);
    AssertEqual(1600, customWindow, "custom per-link chain window");
}

static async Task TestChainWindowStartsFromRootCooldownAsync()
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

    gameApi.Skills = CreateSkillSnapshotsById(new Dictionary<uint, uint>
    {
        [1] = ActiveCooldownEnd(),
        [5] = ActiveCooldownEnd(),
        [6] = 0,
        [61] = 0,
        [62] = 0,
        [7] = ActiveCooldownEnd(),
        [8] = ActiveCooldownEnd(),
        [9] = ActiveCooldownEnd()
    });

    await controller.TickAsync(context, plan, state).ConfigureAwait(false);

    AssertFalse(!state.HasChainWork, "root press should create pending chain");
    AssertFalse(state.HasPendingChainWindowStarted, "chain window must wait for root cooldown");
    AssertEqual(1200, state.PendingChainWindowMs, "three-stage pending window");

    keyboard.Keys.Clear();
    gameApi.Skills = CreateSkillSnapshotsById(new Dictionary<uint, uint>
    {
        [1] = ActiveCooldownEnd(),
        [5] = ActiveCooldownEnd(),
        [6] = 0,
        [61] = 0,
        [62] = 0,
        [7] = ActiveCooldownEnd(),
        [8] = ActiveCooldownEnd(),
        [9] = ActiveCooldownEnd()
    });

    await controller.TickAsync(context, plan, state).ConfigureAwait(false);

    AssertFalse(state.HasPendingChainWindowStarted, "chain window must not start before root cooldown advances");
    AssertEqual(root.Children[0].Name, LastPressedSkill(logger), "second stage can be attempted before root cooldown is confirmed");

    keyboard.Keys.Clear();
    gameApi.Skills = CreateSkillSnapshotsById(new Dictionary<uint, uint>
    {
        [1] = ActiveCooldownEnd(),
        [5] = ActiveCooldownEnd(),
        [6] = ActiveCooldownEnd(),
        [61] = 0,
        [62] = 0,
        [7] = ActiveCooldownEnd(),
        [8] = ActiveCooldownEnd(),
        [9] = ActiveCooldownEnd()
    });

    await controller.TickAsync(context, plan, state).ConfigureAwait(false);

    AssertFalse(!state.HasPendingChainWindowStarted, "chain window should start when root cooldown advances");
    var remaining = state.PendingChainExpiresAt - DateTimeOffset.Now;
    AssertFalse(remaining <= TimeSpan.Zero, "chain window should have positive remaining time");
    AssertFalse(remaining > TimeSpan.FromMilliseconds(1200), "chain window should not exceed configured total");
}

static async Task TestChainWindowDoesNotResetAfterChildAdvanceAsync()
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

    gameApi.Skills = CreateSkillSnapshotsById(new Dictionary<uint, uint>
    {
        [1] = ActiveCooldownEnd(),
        [5] = ActiveCooldownEnd(),
        [6] = 0,
        [61] = 0,
        [62] = 0,
        [7] = ActiveCooldownEnd(),
        [8] = ActiveCooldownEnd(),
        [9] = ActiveCooldownEnd()
    });

    await controller.TickAsync(context, plan, state).ConfigureAwait(false);

    gameApi.Skills = CreateSkillSnapshotsById(new Dictionary<uint, uint>
    {
        [1] = ActiveCooldownEnd(),
        [5] = ActiveCooldownEnd(),
        [6] = ActiveCooldownEnd(),
        [61] = 0,
        [62] = 0,
        [7] = ActiveCooldownEnd(),
        [8] = ActiveCooldownEnd(),
        [9] = ActiveCooldownEnd()
    });

    await controller.TickAsync(context, plan, state).ConfigureAwait(false);

    AssertFalse(!state.HasPendingChainWindowStarted, "second stage should start root cooldown window");
    var expiresAt = state.PendingChainExpiresAt;

    keyboard.Keys.Clear();
    gameApi.Skills = CreateSkillSnapshotsById(new Dictionary<uint, uint>
    {
        [1] = ActiveCooldownEnd(),
        [5] = ActiveCooldownEnd(),
        [6] = ActiveCooldownEnd(),
        [61] = ActiveCooldownEnd(),
        [62] = 0,
        [7] = ActiveCooldownEnd(),
        [8] = ActiveCooldownEnd(),
        [9] = ActiveCooldownEnd()
    });

    await controller.TickAsync(context, plan, state).ConfigureAwait(false);

    AssertEqual(expiresAt, state.PendingChainExpiresAt, "chain expiry must not reset after child advance");
    AssertEqual(root.SkillId, state.PendingChainSourceNode?.SkillId ?? 0, "pending chain source should remain root");
    AssertEqual(root.Children[0].Children[0].Name, LastPressedSkill(logger), "third stage skill");
}

static async Task<int> StartChainAndReadWindowAsync(ScriptSettings settings, uint readyRootSkillId)
{
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
        [5] = readyRootSkillId == 5 ? 0 : ActiveCooldownEnd(),
        [6] = readyRootSkillId == 6 ? 0 : ActiveCooldownEnd(),
        [7] = ActiveCooldownEnd(),
        [8] = ActiveCooldownEnd(),
        [9] = ActiveCooldownEnd()
    });

    await controller.TickAsync(context, plan, state).ConfigureAwait(false);

    AssertFalse(!state.HasChainWork, "ready chain root should create pending chain");
    AssertFalse(state.HasPendingChainWindowStarted, "chain window should not start until root cooldown advances");
    return state.PendingChainWindowMs;
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

static PartyMemberSnapshot CreatePartyMemberSnapshot(
    uint serverObjectId,
    string name,
    bool isSelf,
    bool isLeader,
    double distanceToLocal)
{
    return new PartyMemberSnapshot(
        "primary",
        isSelf ? 0 : 1,
        0,
        0,
        0,
        serverObjectId,
        name,
        (byte)AionClassId.Spiritmaster,
        AionClassId.Spiritmaster,
        "Spiritmaster",
        42,
        100,
        100,
        80,
        100,
        0,
        0,
        0,
        0,
        new Vector3Snapshot(),
        0,
        0,
        0,
        0,
        0,
        false,
        0,
        0,
        Array.Empty<AbnormalStatusEntrySnapshot>(),
        isSelf,
        isLeader,
        true,
        100,
        0,
        0,
        name,
        0,
        new Vector3Snapshot((float)distanceToLocal, 0, 0),
        distanceToLocal,
        distanceToLocal <= 50.0
            ? PartyMemberVisibilityState.ScreenVisible
            : PartyMemberVisibilityState.LoadedOutOfRange);
}

static PartyMemberSnapshot WithLiveRestState(PartyMemberSnapshot member, bool resting)
{
    return member with
    {
        HasLiveRestState = true,
        LiveStanceFlags = resting ? 5U : 0U,
        LiveMotionMode = resting ? 1U : 0U
    };
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

static SummonedPetRosterSnapshot CreateLocalUnconfirmedPetRoster(
    bool ownerConfirmed,
    bool staticSummonPet,
    bool linkedPetMatches)
{
    var now = DateTimeOffset.Now;
    const uint localServerObjectId = 1000;
    const uint petServerObjectId = 2000;
    var linkedPetServerObjectId = linkedPetMatches ? petServerObjectId : 3000U;
    var pet = new SummonedPetSnapshot(
        true,
        2,
        petServerObjectId,
        0,
        SummonedPetSnapshot.ActorObjectType,
        staticSummonPet ? 1234U : 0U,
        "Pet",
        staticSummonPet ? "Pet" : string.Empty,
        staticSummonPet ? "summon_pet" : string.Empty,
        staticSummonPet ? "pet" : string.Empty,
        50,
        100,
        100,
        100,
        new Vector3Snapshot(1, 0, 0),
        1,
        localServerObjectId,
        now,
        linkedPetServerObjectId,
        ownerConfirmed,
        ownerConfirmed
            ? "owner+static-summon-pet"
            : "local-link-only");

    return new SummonedPetRosterSnapshot(
        localServerObjectId,
        linkedPetServerObjectId,
        now,
        new OwnedSummonedPetSnapshot(
            SummonedPetOwnerKind.LocalPlayer,
            localServerObjectId,
            "Spirit",
            "Spirit",
            pet,
            0,
            Array.Empty<AbnormalStatusEntrySnapshot>(),
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

static ScriptSettings CreateConditionSkillSettings()
{
    var settings = CreateScriptSettings();
    settings.Skills.ExecutionTree[0] = Node(1, "共鸣烟雾 I", "条件技能");
    return settings;
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
    var configStore = new InMemoryAccountConfigStore(new AccountConfig
    {
        AccountName = "account1",
        ScriptSettings = CreateScriptSettings()
    });

    return CreateAccountSettingsFormForTestsWithStore(configStore);
}

static AccountSettingsForm CreateAccountSettingsFormForTestsWithStore(InMemoryAccountConfigStore configStore)
{
    var logger = new InMemoryRoadhogLogger();
    var accounts = new AccountRuntimeManager(logger);
    var runtime = new RoadhogRuntime(new FakeGameApi(), logger, accounts, null!);
    return new AccountSettingsForm(
        "account1",
        runtime,
        configStore,
        new InMemorySharedPathStore(),
        new InMemoryScriptProfileStore());
}

static bool InvokeSaveCurrentSettingsForTest(AccountSettingsForm form, out string error)
{
    var method = typeof(AccountSettingsForm).GetMethod(
        "SaveCurrentSettings",
        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
    AssertFalse(method is null, "save current settings method should exist");

    var arguments = new object?[] { string.Empty };
    var result = (bool)method!.Invoke(form, arguments)!;
    error = arguments[0] as string ?? string.Empty;
    return result;
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

static bool GetCheckBoxCheckedForTest(AccountSettingsForm form, string fieldName)
{
    var checkBox = GetPrivateFieldForTest(form, fieldName);
    var property = checkBox.GetType().GetProperty(
        "Checked",
        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
    AssertFalse(property is null, fieldName + " checked property should exist");
    return (bool)property!.GetValue(checkBox)!;
}

static void SetCheckBoxCheckedForTest(AccountSettingsForm form, string fieldName, bool isChecked)
{
    var checkBox = GetPrivateFieldForTest(form, fieldName);
    var property = checkBox.GetType().GetProperty(
        "Checked",
        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
    AssertFalse(property is null, fieldName + " checked property should exist");
    property!.SetValue(checkBox, isChecked);
}

static string GetTextBoxTextForTest(AccountSettingsForm form, string fieldName)
{
    var textBox = GetPrivateFieldForTest(form, fieldName);
    var property = textBox.GetType().GetProperty(
        "Text",
        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
    AssertFalse(property is null, fieldName + " text property should exist");
    return property!.GetValue(textBox) as string ?? string.Empty;
}

static void SetTextBoxTextForTest(AccountSettingsForm form, string fieldName, string text)
{
    var textBox = GetPrivateFieldForTest(form, fieldName);
    var property = textBox.GetType().GetProperty(
        "Text",
        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
    AssertFalse(property is null, fieldName + " text property should exist");
    property!.SetValue(textBox, text);
}

static object GetPrivateFieldForTest(AccountSettingsForm form, string fieldName)
{
    var field = typeof(AccountSettingsForm).GetField(
        fieldName,
        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
    AssertFalse(field is null, fieldName + " field should exist");
    var value = field!.GetValue(form);
    AssertFalse(value is null, fieldName + " field should be initialized");
    return value!;
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

static IReadOnlyList<SkillSnapshot> WithConditionSkillStatus(
    IReadOnlyList<SkillSnapshot> skills,
    string targetValidStatuses = "Stumble")
{
    return skills
        .Select(skill => skill.SkillId == 1
            ? skill with
            {
                Name = "共鸣烟雾 I",
                DisplayBaseName = "共鸣烟雾",
                XmlTags = AppendSkillTag(skill.XmlTags, "condition"),
                XmlTargetValidStatuses = targetValidStatuses
            }
            : skill)
        .ToArray();
}

static string AppendSkillTag(string? tags, string tag)
{
    if (string.IsNullOrWhiteSpace(tags))
    {
        return tag;
    }

    return tags
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Any(value => string.Equals(value, tag, StringComparison.OrdinalIgnoreCase))
        ? tags
        : tags + "," + tag;
}

static IReadOnlyDictionary<uint, uint> CreateCombatRootCooldowns(uint cooldownEnd)
{
    return new Dictionary<uint, uint>
    {
        [1] = cooldownEnd,
        [5] = cooldownEnd,
        [6] = cooldownEnd,
        [7] = cooldownEnd,
        [8] = cooldownEnd,
        [9] = cooldownEnd
    };
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

static ScriptSettings CreateTeamSupportSettings()
{
    return new ScriptSettings
    {
        MainMode = AccountMainMode.SemiAuto,
        Team = new TeamScriptSettings
        {
            Role = TeamRole.Support,
            Support = new TeamSupportScriptSettings
            {
                Enabled = true,
                JoinCombat = false,
                MentalCleanseEnabled = true,
                PhysicalCleanseEnabled = true,
                MentalCleanseKey = "NumPad8",
                PhysicalCleanseKey = "NumPad7",
                HealSkillRules = new List<TeamHealSkillRuleConfig>
                {
                    new()
                    {
                        BelowPercent = 90,
                        Key = "NumPad1",
                        RunTiming = MaintenanceRuleRunTiming.Always,
                        TargetType = TeamHealSkillTargetType.Single
                    }
                }
            }
        },
        SemiAuto = new SemiAutoScriptSettings
        {
            KeyHoldMs = 1
        }
    };
}

static ScriptSettings CreateTeamOutputSettings()
{
    return new ScriptSettings
    {
        MainMode = AccountMainMode.SemiAuto,
        Team = new TeamScriptSettings
        {
            Role = TeamRole.Output,
            Output = new TeamOutputScriptSettings
            {
                Enabled = true,
                FollowLeader = false,
                OnlyAttackLeaderMarkedTarget = true,
                StopWhenLeaderHasNoTarget = true,
                StopWhenLeaderDead = true,
                AllowSelfDefense = true
            }
        },
        SemiAuto = new SemiAutoScriptSettings
        {
            KeyHoldMs = 1
        }
    };
}

static TeamAbnormalStatusCatalog CreateTeamSupportAbnormalCatalog()
{
    return TeamAbnormalStatusCatalog.LoadedFrom(
        "test",
        new Dictionary<uint, TeamAbnormalStatusStaticInfo>
        {
            [1636] = new(
                1636,
                "MentalDebuff",
                "Debuff",
                "Enemy",
                "DebuffMen",
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                TeamAbnormalStatusCatalog.AbnormalKindNegative),
            [1632] = new(
                1632,
                "PhysicalDebuff",
                "Debuff",
                "Enemy",
                "DebuffPhy",
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                TeamAbnormalStatusCatalog.AbnormalKindNegative),
            [8232] = new(
                8232,
                "PositiveChant",
                "Chant",
                "Friend",
                "DebuffPhy",
                "StatUp",
                string.Empty,
                string.Empty,
                string.Empty,
                TeamAbnormalStatusCatalog.AbnormalKindPositive)
        });
}

static FakeGameApi CreateTeamSupportGameApi(params PartyMemberSnapshot[] members)
{
    var localServerObjectId = members.First(member => member.IsSelf).ServerObjectId;
    return new FakeGameApi
    {
        Party = CreateTeamSupportParty(members),
        SummonedPetRoster = SummonedPetRosterSnapshot.Empty(localServerObjectId, DateTimeOffset.Now),
        TargetEntityId = 0,
        TargetOwnServerObjectId = 0,
        TargetServerObjectId = 0,
        TargetObjectType = 0,
        TargetCurrentHp = 0,
        TargetMaxHp = 0
    };
}

static PartySnapshot CreateTeamSupportParty(params PartyMemberSnapshot[] members)
{
    var now = DateTimeOffset.Now;
    var self = members.First(member => member.IsSelf);
    var leader = members.FirstOrDefault(member => member.IsLeader) ?? self;
    return new PartySnapshot(
        1141852,
        0x3F,
        (ulong)members.Length,
        leader.ServerObjectId,
        self.ServerObjectId,
        self.LiveEntityId,
        self.Name,
        self.LivePosition,
        0,
        members.Length,
        now,
        members);
}

static PartySnapshot CreateTeamPartyWithoutLeader(PartyMemberSnapshot self)
{
    var now = DateTimeOffset.Now;
    var normalizedSelf = self with { IsLeader = false };
    return new PartySnapshot(
        1141852,
        0x3F,
        1,
        0,
        normalizedSelf.ServerObjectId,
        normalizedSelf.LiveEntityId,
        normalizedSelf.Name,
        normalizedSelf.LivePosition,
        0,
        1,
        now,
        new[] { normalizedSelf });
}

static SummonedPetRosterSnapshot CreateTeamSupportRoster(
    uint localServerObjectId,
    PartyMemberSnapshot owner,
    uint petServerObjectId)
{
    var now = DateTimeOffset.Now;
    return new SummonedPetRosterSnapshot(
        localServerObjectId,
        0,
        now,
        new OwnedSummonedPetSnapshot(
            SummonedPetOwnerKind.LocalPlayer,
            localServerObjectId,
            "Healer",
            string.Empty,
            SummonedPetSnapshot.NotSummoned(localServerObjectId, now),
            0,
            Array.Empty<AbnormalStatusEntrySnapshot>()),
        new[]
        {
            new OwnedSummonedPetSnapshot(
                SummonedPetOwnerKind.PartyMember,
                owner.ServerObjectId,
                owner.Name,
                "primary",
                new SummonedPetSnapshot(
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
                    owner.Level,
                    100,
                    100,
                    100,
                    owner.LivePosition,
                    owner.DistanceToLocalPlayer,
                    localServerObjectId,
                    now,
                    petServerObjectId,
                    true,
                    "test"),
                0,
                Array.Empty<AbnormalStatusEntrySnapshot>(),
                OwnerClassId: owner.Class,
                OwnerClassName: owner.ClassName)
        },
        new[] { owner.ServerObjectId });
}

static OwnedSummonedPetSnapshot CreatePartyPet(
    PartyMemberSnapshot owner,
    uint petServerObjectId)
{
    var now = DateTimeOffset.Now;
    return new OwnedSummonedPetSnapshot(
        SummonedPetOwnerKind.PartyMember,
        owner.ServerObjectId,
        owner.Name,
        "primary",
        new SummonedPetSnapshot(
            true,
            (ushort)(petServerObjectId & 0xFFFF),
            petServerObjectId,
            0,
            SummonedPetSnapshot.ActorObjectType,
            0,
            "Pet",
            "Pet",
            "pet",
            "pet",
            owner.Level,
            100,
            100,
            100,
            owner.LivePosition,
            owner.DistanceToLocalPlayer,
            0,
            now,
            petServerObjectId,
            true,
            "test"),
        0,
        Array.Empty<AbnormalStatusEntrySnapshot>(),
        OwnerClassId: owner.Class,
        OwnerClassName: owner.ClassName);
}

static WorldObjectSnapshot CreateMonsterWorldObject(
    ushort entityId,
    uint serverObjectId,
    uint targetServerObjectId,
    Vector3Snapshot position)
{
    return new WorldObjectSnapshot(
        entityId,
        serverObjectId,
        "Monster",
        "monster",
        position,
        null,
        CurrentHp: 100,
        MaxHp: 100,
        TargetServerObjectId: targetServerObjectId);
}

static void SetFakeLockedTarget(
    FakeGameApi gameApi,
    uint serverObjectId,
    uint objectType,
    uint targetServerObjectId,
    uint currentHp)
{
    gameApi.TargetEntityId = serverObjectId == 0 ? (ushort)0 : (ushort)100;
    gameApi.TargetOwnServerObjectId = serverObjectId;
    gameApi.TargetServerObjectId = targetServerObjectId;
    gameApi.TargetObjectType = objectType;
    gameApi.TargetCurrentHp = currentHp;
    gameApi.TargetMaxHp = currentHp == 0 ? 0u : 100u;
}

static OperationResult<LockedTargetSnapshot> CreateFakeLockedTargetResult(
    uint serverObjectId,
    uint objectType,
    uint targetServerObjectId,
    uint currentHp)
{
    return OperationResult<LockedTargetSnapshot>.Ok(new LockedTargetSnapshot(
        serverObjectId == 0 ? (ushort)0 : (ushort)100,
        serverObjectId,
        0,
        objectType,
        "Target",
        currentHp,
        currentHp == 0 ? 0u : 100u,
        null,
        null,
        DateTimeOffset.Now,
        targetServerObjectId,
        false,
        0));
}

static void AssertLeaderTargetAdopted(
    StationaryCombatState combatState,
    uint targetServerObjectId,
    string label)
{
    AssertFalse(!combatState.Fighting, label + " fighting");
    AssertEqual(targetServerObjectId, combatState.CurrentTargetServerObjectId, label + " current target server id");
    AssertEqual(targetServerObjectId, combatState.CandidateServerObjectId, label + " candidate server id");
    AssertFalse(!combatState.CurrentTargetIsMaintenanceDefense, label + " maintenance defense flag");
    AssertFalse(!combatState.CurrentTargetBypassesHomeLeash, label + " bypass home leash flag");
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

static InventoryWindowSnapshot CreateInventoryWindow(bool isOpen, double x, double y)
{
    return new InventoryWindowSnapshot(
        isOpen,
        x,
        y,
        324.8,
        443.2,
        0x1000,
        0x2000,
        DateTimeOffset.Now);
}

sealed class RecordingKeyboardInput : IKeyboardInput
{
    public List<string> Keys { get; } = new();

    public List<string> KeyDowns { get; } = new();

    public List<string> KeyUps { get; } = new();

    public List<string> MouseCommands { get; } = new();

    public Action<string>? AfterPress { get; set; }

    public Action<RoadhogMouseButton>? AfterMouseDown { get; set; }

    public Action<RoadhogMouseButton>? AfterMouseUp { get; set; }

    public Action<int, int>? AfterMove { get; set; }

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
        AfterMouseDown?.Invoke(button);
        return Task.FromResult(OperationResult.Ok());
    }

    public Task<OperationResult> MouseUpAsync(
        RoadhogMouseButton button,
        CancellationToken cancellationToken = default)
    {
        MouseCommands.Add("up:" + button);
        AfterMouseUp?.Invoke(button);
        return Task.FromResult(OperationResult.Ok());
    }

    public Task<OperationResult> MoveMouseRelativeAsync(
        int deltaX,
        int deltaY,
        CancellationToken cancellationToken = default)
    {
        MouseCommands.Add("move:" + deltaX + "," + deltaY);
        AfterMove?.Invoke(deltaX, deltaY);
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

sealed class FakeGameApi : IRoadhogScopedGameApi, IRoadhogScopedPartyGameApi, IInventoryWindowGameApi, IInventoryMoneyGameApi, IInventoryCapacityGameApi
#if DEBUG
    , IRoadhogApiAddressProbe
#endif
{
    private readonly object _playerReadSync = new();

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

    public Queue<OperationResult<PlayerSnapshot>> PlayerReadResults { get; } = new();

    public OperationResult<PlayerSnapshot>? PlayerReadFallback { get; set; }

    public int PlayerReadCount { get; private set; }

    public IReadOnlyList<InventoryItemSnapshot> InventoryItems { get; set; } = Array.Empty<InventoryItemSnapshot>();

    public ulong InventoryMoney { get; set; }

    public int InventoryCapacity { get; set; }

    public PlayerAbnormalStatusSnapshot PlayerAbnormalStatuses { get; set; } =
        PlayerAbnormalStatusSnapshot.Empty(1);

    public SummonedPetSnapshot SummonedPet { get; set; } =
        SummonedPetSnapshot.NotSummoned(0, DateTimeOffset.Now);

    public SummonedPetRosterSnapshot SummonedPetRoster { get; set; } =
        SummonedPetRosterSnapshot.Empty(0, DateTimeOffset.Now);

    public PartySnapshot Party { get; set; } =
        PartySnapshot.Empty(DateTimeOffset.Now);

    public IReadOnlyList<uint>? LastRequestedSkillIds { get; private set; }

    public GameApiReadContext? LastPlayerContext { get; private set; }

    public GameApiReadContext? LastPlayerAbnormalContext { get; private set; }

    public GameApiReadContext? LastSkillsContext { get; private set; }

    public GameApiReadContext? LastInventoryContext { get; private set; }

    public GameApiReadContext? LastInventoryMoneyContext { get; private set; }

    public GameApiReadContext? LastInventoryCapacityContext { get; private set; }

    public GameApiReadContext? LastSummonedPetContext { get; private set; }

    public GameApiReadContext? LastSummonedPetRosterContext { get; private set; }

    public GameApiReadContext? LastPartyContext { get; private set; }

    public GameApiReadContext? LastLockedTargetContext { get; private set; }

    public GameApiReadContext? LastLockedTargetAbnormalContext { get; private set; }

    public GameApiReadContext? LastWorldObjectsContext { get; private set; }

    public GameApiReadContext? LastLootCorpsesContext { get; private set; }

    public GameApiReadContext? LastInventoryWindowContext { get; private set; }

#if DEBUG
    public GameApiReadContext? LastAddressProbeContext { get; private set; }
#endif

    public InventoryWindowRectSource? LastInventoryWindowRectSource { get; private set; }

    public List<InventoryWindowRectSource> InventoryWindowRectSources { get; } = new();

    public InventoryWindowSnapshot InventoryWindow { get; set; } =
        new(false, 0.0, 0.0, 324.8, 443.2, 0x1000, 0x2000, DateTimeOffset.Now);

    public ushort TargetEntityId { get; set; } = 100;

    public string TargetName { get; set; } = "训练用稻草人";

    public uint TargetObjectType { get; set; } = LockedTargetSnapshot.MonsterObjectType;

    public uint TargetCurrentHp { get; set; } = 1000;

    public uint TargetMaxHp { get; set; } = 1000;

    public uint TargetLootableRaw { get; set; } = 1;

    public uint TargetInteractionState { get; set; } = 37;

    public Vector3Snapshot? TargetPosition { get; set; }

    public uint TargetOwnServerObjectId { get; set; }

    public uint TargetServerObjectId { get; set; } = 1;

    public uint LocalServerObjectId { get; set; }

    public bool TargetIsTargetingLocalPlayer { get; set; } = true;

    public Queue<OperationResult<LockedTargetSnapshot>> LockedTargetReadResults { get; } = new();

    public LockedTargetAbnormalStatusSnapshot? LockedTargetAbnormalStatuses { get; set; }

    public IReadOnlyList<WorldObjectSnapshot> WorldObjects { get; set; } = Array.Empty<WorldObjectSnapshot>();

    public IReadOnlyList<LootCorpseSnapshot> LootCorpses { get; set; } = Array.Empty<LootCorpseSnapshot>();

    public int LootReadCount { get; private set; }

    public Task<OperationResult<PlayerSnapshot>> ReadPlayerAsync(CancellationToken cancellationToken = default)
    {
        lock (_playerReadSync)
        {
            PlayerReadCount++;
            if (PlayerReadResults.Count > 0)
            {
                return Task.FromResult(PlayerReadResults.Dequeue());
            }

            return Task.FromResult(
                PlayerReadFallback ?? OperationResult<PlayerSnapshot>.Ok(Player));
        }
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
        LastPlayerAbnormalContext = context;
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

    public Task<OperationResult<PartySnapshot>> ReadPartyAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(OperationResult<PartySnapshot>.Ok(Party));
    }

    public Task<OperationResult<PartySnapshot>> ReadPartyAsync(
        GameApiReadContext context,
        CancellationToken cancellationToken = default)
    {
        LastPartyContext = context;
        return ReadPartyAsync(cancellationToken);
    }

    public Task<OperationResult<LockedTargetSnapshot>> ReadLockedTargetAsync(CancellationToken cancellationToken = default)
    {
        if (LockedTargetReadResults.Count > 0)
        {
            return Task.FromResult(LockedTargetReadResults.Dequeue());
        }

        var objectType = ResolveLockedTargetObjectType();
        var lootableRaw = ResolveLockedTargetLootableRaw(objectType);
        return Task.FromResult(OperationResult<LockedTargetSnapshot>.Ok(new LockedTargetSnapshot(
            TargetEntityId,
            TargetOwnServerObjectId != 0 ? TargetOwnServerObjectId : TargetEntityId,
            0,
            objectType,
            TargetName,
            TargetCurrentHp,
            TargetMaxHp,
            TargetPosition,
            null,
            DateTimeOffset.Now,
            TargetServerObjectId,
            TargetIsTargetingLocalPlayer,
            LocalServerObjectId,
            lootableRaw,
            ResolveLockedTargetInteractionState(lootableRaw))));
    }

    public Task<OperationResult<LockedTargetSnapshot>> ReadLockedTargetAsync(
        GameApiReadContext context,
        CancellationToken cancellationToken = default)
    {
        LastLockedTargetContext = context;
        return ReadLockedTargetAsync(cancellationToken);
    }

    public Task<OperationResult<LockedTargetAbnormalStatusSnapshot>> ReadLockedTargetAbnormalStatusesAsync(
        CancellationToken cancellationToken = default)
    {
        var objectType = ResolveLockedTargetObjectType();
        var lootableRaw = ResolveLockedTargetLootableRaw(objectType);
        var snapshot = LockedTargetAbnormalStatuses ?? new LockedTargetAbnormalStatusSnapshot(
            new LockedTargetSnapshot(
                TargetEntityId,
                TargetOwnServerObjectId != 0 ? TargetOwnServerObjectId : TargetEntityId,
                0,
                objectType,
                TargetName,
                TargetCurrentHp,
                TargetMaxHp,
                TargetPosition,
                null,
                DateTimeOffset.Now,
                TargetServerObjectId,
                TargetIsTargetingLocalPlayer,
                LocalServerObjectId,
                lootableRaw,
                ResolveLockedTargetInteractionState(lootableRaw)),
            0,
            Array.Empty<AbnormalStatusEntrySnapshot>(),
            DateTimeOffset.Now);

        return Task.FromResult(OperationResult<LockedTargetAbnormalStatusSnapshot>.Ok(snapshot));
    }

    private uint ResolveLockedTargetObjectType()
    {
        if (TargetObjectType != 0 ||
            TargetOwnServerObjectId == 0 ||
            Party.Members.All(member => member.ServerObjectId != TargetOwnServerObjectId))
        {
            return TargetObjectType;
        }

        return LockedTargetSnapshot.PlayerObjectType;
    }

    private uint ResolveLockedTargetLootableRaw(uint objectType)
    {
        return objectType == LockedTargetSnapshot.MonsterObjectType &&
               TargetMaxHp > 0 &&
               TargetCurrentHp == 0
            ? TargetLootableRaw
            : 0;
    }

    private uint ResolveLockedTargetInteractionState(uint lootableRaw)
    {
        return lootableRaw != 0 ? TargetInteractionState : 0;
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
        return Task.FromResult(OperationResult<IReadOnlyList<InventoryItemSnapshot>>.Ok(InventoryItems));
    }

    public Task<OperationResult<IReadOnlyList<InventoryItemSnapshot>>> ReadInventoryAsync(
        GameApiReadContext context,
        CancellationToken cancellationToken = default)
    {
        LastInventoryContext = context;
        return ReadInventoryAsync(cancellationToken);
    }

    public Task<OperationResult<ulong>> ReadInventoryMoneyAsync(
        GameApiReadContext context,
        CancellationToken cancellationToken = default)
    {
        LastInventoryMoneyContext = context;
        return Task.FromResult(OperationResult<ulong>.Ok(InventoryMoney));
    }

    public Task<OperationResult<int>> ReadInventoryCapacityAsync(
        GameApiReadContext context,
        CancellationToken cancellationToken = default)
    {
        LastInventoryCapacityContext = context;
        return Task.FromResult(OperationResult<int>.Ok(InventoryCapacity));
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
        LastLootCorpsesContext = context;
        return ReadLootCorpsesAsync(cancellationToken);
    }

    public Task<OperationResult<InventoryWindowSnapshot>> ReadInventoryWindowAsync(
        GameApiReadContext context,
        CancellationToken cancellationToken = default)
    {
        LastInventoryWindowContext = context;
        return Task.FromResult(OperationResult<InventoryWindowSnapshot>.Ok(InventoryWindow));
    }

#if DEBUG
    public Task<OperationResult<IReadOnlyList<GameApiAddressProbeResult>>> ProbeAddressesAsync(
        GameApiReadContext context,
        CancellationToken cancellationToken = default)
    {
        LastAddressProbeContext = context;
        IReadOnlyList<GameApiAddressProbeResult> checks = GameApiAddressProbeResult.RequiredCheckNames
            .Select(name => new GameApiAddressProbeResult(
                name,
                true,
                "Game.dll base=0x10000000, RVA=0x1000, address=0x10001000; fake read ok"))
            .ToArray();
        return Task.FromResult(OperationResult<IReadOnlyList<GameApiAddressProbeResult>>.Ok(checks));
    }
#endif

    public Task<OperationResult<InventoryWindowSnapshot>> ReadInventoryWindowAsync(
        GameApiReadContext context,
        InventoryWindowRectSource rectSource,
        CancellationToken cancellationToken = default)
    {
        LastInventoryWindowRectSource = rectSource;
        InventoryWindowRectSources.Add(rectSource);
        return ReadInventoryWindowAsync(context, cancellationToken);
    }
}
