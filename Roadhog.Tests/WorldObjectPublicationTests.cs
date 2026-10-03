using Roadhog.Application;
using Roadhog.Application.SemiAuto;
using Roadhog.Application.StationaryCombat;
using Roadhog.Application.Workers;
using Roadhog.Core.Accounts;
using Roadhog.Core.Api;
using Roadhog.Core.Common;
using Roadhog.Core.Diagnostics;
using Roadhog.Core.Model;
using Roadhog.Infrastructure.Vmm;

internal static class WorldObjectPublicationTests
{
    private static readonly DateTimeOffset Start = DateTimeOffset.Parse("2026-10-04T10:00:00+08:00");
    private static readonly WorldObjectFieldValidity AllFields = new(true, true, true, true, true, true);
    private static readonly GameApiReadContext Context = new("world-publication", 7964, "Aion.bin", "fake-world-device");
    private const ulong Header = 0x10000000, Root = 0x20000000, First = 0x30000000, Last = 0x40000000;

    public static Task ContradictoryCompleteEmptyHoldsAsync()
    {
        var target = Monster(10, 1000);
        var api = Provider();
        var baseline = api.StabilizeWorldObjectRead(Context, Complete(target), Start);
        var legitimateEmpty = Complete();
        var proof = legitimateEmpty.Diagnostics.TreeProof!;
        var invalidCaptures = new[]
        {
            WithProof(legitimateEmpty, null),
            WithProof(legitimateEmpty, proof with { HeaderVerified = false }),
            WithProof(legitimateEmpty, proof with { LinksVerified = false }),
            WithProof(legitimateEmpty, proof with { FinalReadVerified = false }),
            WithProof(legitimateEmpty, proof with { Header = 1, Root = 1, FirstNode = 1, LastNode = 1 }),
            WithProof(legitimateEmpty, proof with { Header = Header + 1, Root = Header + 1,
                FirstNode = Header + 1, LastNode = Header + 1 }),
            WithProof(legitimateEmpty, proof with { Header = 0xFFFF800000000000,
                Root = 0xFFFF800000000000, FirstNode = 0xFFFF800000000000, LastNode = 0xFFFF800000000000 }),
            WithProof(legitimateEmpty, proof with { Root = Root }),
            WithProof(legitimateEmpty, proof with { FirstNode = First }),
            WithProof(legitimateEmpty, proof with { LastNode = Last }),
            WithProof(legitimateEmpty, proof with { NodeCount = 1 }),
            WithDiagnostics(legitimateEmpty, legitimateEmpty.Diagnostics with { EmittedObjects = 1 }),
            WithDiagnostics(legitimateEmpty, legitimateEmpty.Diagnostics with
                { TraversalTermination = WorldObjectTraversalTermination.ReachedTreeEnd })
        };
        foreach (var capture in invalidCaptures)
        {
            var held = api.StabilizeWorldObjectRead(Context, capture, Start.AddSeconds(1));
            Require(held.Success && ReferenceEquals(baseline.Value, held.Value),
                "an unproven Complete empty capture must preserve the exact official publication");
            SameObjects(baseline.Value!, AionVmmGameApi.MergeWorldObjectRead(capture, baseline.Value!),
                "direct collection merge must not turn an unproven empty capture into absence");
        }
        return Task.CompletedTask;
    }

    public static Task UntrustedCompleteShortMergesWithoutPruningAsync()
    {
        var first = Monster(10, 1000);
        var second = Monster(11, 1001) with { CurrentHp = 60 };
        var api = Provider();
        api.StabilizeWorldObjectRead(Context, Complete(first, second), Start);
        var changed = second with { CurrentHp = 40, Position = new(12, 0, 0), DistanceToLocalPlayer = 6 };
        var shortCapture = Complete(changed);
        var proof = shortCapture.Diagnostics.TreeProof!;
        foreach (var capture in new[]
        {
            WithProof(shortCapture, null),
            WithProof(shortCapture, proof with { FinalReadVerified = false }),
            WithProof(shortCapture, proof with { HeaderVerified = false }),
            WithProof(shortCapture, proof with { LinksVerified = false }),
            WithProof(shortCapture, proof with { NodeCount = 2 }),
            WithProof(shortCapture, proof with { Root = Header }),
            WithProof(shortCapture, proof with { FirstNode = Header }),
            WithProof(shortCapture, proof with { LastNode = Header }),
            WithProof(shortCapture, proof with { Root = 1 }),
            WithProof(shortCapture, proof with { FirstNode = First + 1 }),
            WithProof(shortCapture, proof with { LastNode = 0xFFFF800000000000 }),
            WithDiagnostics(shortCapture, shortCapture.Diagnostics with
                { TraversalTermination = WorldObjectTraversalTermination.EmptyTree }),
            WithDiagnostics(shortCapture, shortCapture.Diagnostics with { EmittedObjects = 0 })
        })
        {
            var published = api.StabilizeWorldObjectRead(Context, capture, Start.AddSeconds(1));
            Require(published.Success && published.Value!.Count == 2,
                "unproven Complete short traversal must retain an omitted target");
            Require(published.Value!.Single(item => item.ServerObjectId == second.ServerObjectId) == changed,
                "independently valid observations from a partial capture must update immediately");
            var direct = AionVmmGameApi.MergeWorldObjectRead(capture, new[] { first, second });
            Require(direct.Count == 2 && direct.Any(item => item == first),
                "direct merge must apply the same structural proof before pruning");
        }
        return Task.CompletedTask;
    }

    public static Task InvalidCompleteFieldMergeDoesNotPruneAsync()
    {
        var first = Monster(10, 1000) with { TargetServerObjectId = 7000, IsTargetingLocalPlayer = true };
        var second = Monster(11, 1001);
        var observation = new WorldObjectObservation(first with
        {
            CurrentHp = 0, MaxHp = 120, TargetServerObjectId = 0, IsTargetingLocalPlayer = false,
            Position = new(11, 0, 0), DistanceToLocalPlayer = 5
        }, new(false, true, false, false, true, true));
        var capture = Capture(WorldObjectReadCompleteness.Complete, new[] { observation });
        capture = WithProof(capture, capture.Diagnostics.TreeProof! with { FinalReadVerified = false });
        var api = Provider();
        api.StabilizeWorldObjectRead(Context, Complete(first, second), Start);
        var published = api.StabilizeWorldObjectRead(Context, capture, Start.AddSeconds(1));
        Require(published.Success && published.Value!.Count == 2,
            "invalid Complete must not prune when bad fields select the field-aware merge path");
        var updated = published.Value!.Single(item => item.ServerObjectId == first.ServerObjectId);
        Require(updated.CurrentHp == first.CurrentHp && updated.TargetServerObjectId == 7000 && updated.IsTargetingLocalPlayer,
            "failed fields must preserve prior health and incoming attack");
        Require(updated.MaxHp == 120 && updated.Position == observation.Snapshot.Position,
            "valid fields still publish while the structural proof is incomplete");
        SameObjects(published.Value!, AionVmmGameApi.MergeWorldObjectRead(capture, new[] { first, second }),
            "provider and direct merge must agree on unsafe Complete field merge");
        return Task.CompletedTask;
    }

    public static Task CoveredTreeDecodeFailuresRetainMissingObjectsAsync()
    {
        var first = Monster(10, 1000);
        var second = Monster(11, 1001);
        Func<WorldObjectReadDiagnostics, WorldObjectReadDiagnostics>[] faults =
        {
            diagnostic => diagnostic with { NodeIdentityReadFailures = 1 },
            diagnostic => diagnostic with { EntityLookupFailures = 1 },
            diagnostic => diagnostic with { EntityTypeReadFailures = 1 },
            diagnostic => diagnostic with { PositionReadFailures = 1 },
            diagnostic => diagnostic with { ActorResolutionFailures = 1 },
            diagnostic => diagnostic with { ActorIdentityMismatches = 1 },
            diagnostic => diagnostic with { StaticMetadataMisses = 1 },
            diagnostic => diagnostic with { StaticCatalogErrors = 1 }
        };
        foreach (var fault in faults)
        {
            var api = Provider();
            api.StabilizeWorldObjectRead(Context, Complete(first, second), Start);
            var shortRead = Complete(second with { CurrentHp = 20 });
            shortRead = WithDiagnostics(shortRead, fault(shortRead.Diagnostics));
            var merged = api.StabilizeWorldObjectRead(Context, shortRead, Start.AddSeconds(1));
            Require(merged.Success && merged.Value!.Count == 2 && merged.Value.Any(item => item == first) &&
                merged.Value.Single(item => item.ServerObjectId == second.ServerObjectId).CurrentHp == 20,
                "verified tree traversal cannot prove absence when object decoding omitted an observation");
            var emptyRead = Complete();
            emptyRead = WithDiagnostics(emptyRead, fault(emptyRead.Diagnostics));
            var held = api.StabilizeWorldObjectRead(Context, emptyRead, Start.AddSeconds(2));
            Require(held.Success && ReferenceEquals(merged.Value, held.Value),
                "object-decoding failures must prevent even structurally verified empty Complete from clearing targets");
        }
        return Task.CompletedTask;
    }

    public static Task TrustedCompletePrunesImmediatelyAsync()
    {
        var first = Monster(10, 1000);
        var second = Monster(11, 1001);
        var api = Provider();
        api.StabilizeWorldObjectRead(Context, Complete(first, second), Start);
        var shortCapture = api.StabilizeWorldObjectRead(Context, Complete(second), Start.AddMilliseconds(1));
        Require(shortCapture.Success && shortCapture.Value!.Count == 1 && shortCapture.Value[0] == second,
            "a proven short Complete capture must remove absent objects on its first publication");
        var empty = api.StabilizeWorldObjectRead(Context, Complete(), Start.AddMilliseconds(2));
        Require(empty.Success && empty.Value!.Count == 0,
            "a proven empty tree must clear prior objects immediately");

        api.StabilizeWorldObjectRead(Context, Complete(first), Start.AddMilliseconds(3));
        var filteredEmpty = Capture(WorldObjectReadCompleteness.Complete, Array.Empty<WorldObjectObservation>(), 4);
        var noMonsters = api.StabilizeWorldObjectRead(Context, filteredEmpty, Start.AddMilliseconds(4));
        Require(noMonsters.Success && noMonsters.Value!.Count == 0,
            "a complete nonempty tree containing no matching NPCs is valid official absence");
        return Task.CompletedTask;
    }

    public static Task TrustedCompleteFieldMergePrunesOnlyAbsentObjectsAsync()
    {
        var first = Monster(10, 1000) with { TargetServerObjectId = 7000, IsTargetingLocalPlayer = true };
        var second = Monster(11, 1001);
        var api = Provider();
        api.StabilizeWorldObjectRead(Context, Complete(first, second), Start);
        var read = Capture(WorldObjectReadCompleteness.Complete, new[]
        {
            new WorldObjectObservation(first with { CurrentHp = 0, MaxHp = 120, TargetServerObjectId = 0,
                IsTargetingLocalPlayer = false, Position = new(11, 0, 0) }, new(false, true, false, false, true, true))
        });
        var merged = api.StabilizeWorldObjectRead(Context, read, Start.AddSeconds(1));
        Require(merged.Success && merged.Value!.Count == 1,
            "proven Complete must prune absent objects even when present objects need field merge");
        var target = merged.Value![0];
        Require(target.CurrentHp == 90 && target.MaxHp == 120 && target.TargetServerObjectId == 7000 && target.IsTargetingLocalPlayer,
            "same identity retains bad fields while valid fields update");
        var zero = api.StabilizeWorldObjectRead(Context, Complete(target with
        {
            CurrentHp = 0, TargetServerObjectId = 0, IsTargetingLocalPlayer = false,
            LootableRaw = 0, InteractionState = 0
        }), Start.AddSeconds(2));
        Require(zero.Success && zero.Value![0].CurrentHp == 0 && zero.Value[0].TargetServerObjectId == 0 &&
            !zero.Value[0].IsTargetingLocalPlayer && zero.Value[0].LootableRaw == 0 && zero.Value[0].InteractionState == 0,
            "valid zero and false world fields must replace prior nonzero and true values immediately");
        return Task.CompletedTask;
    }

    public static Task PartialUpdatesAndRetainsOmissionsAsync()
    {
        var first = Monster(10, 1000);
        var second = Monster(11, 1001);
        var api = Provider();
        api.StabilizeWorldObjectRead(Context, Complete(first, second), Start);
        var partial = Capture(WorldObjectReadCompleteness.Partial,
            new[] { new WorldObjectObservation(second with { CurrentHp = 25 }, AllFields) });
        var changed = api.StabilizeWorldObjectRead(Context, partial, Start.AddSeconds(1));
        Require(changed.Success && changed.Value!.Count == 2 && changed.Value.Any(item => item == first) &&
            changed.Value.Single(item => item.ServerObjectId == second.ServerObjectId).CurrentHp == 25,
            "partial traversal must update valid data and retain omitted identities");
        var emptyPartial = Capture(WorldObjectReadCompleteness.Partial, Array.Empty<WorldObjectObservation>());
        var held = api.StabilizeWorldObjectRead(Context, emptyPartial, Start.AddHours(1));
        Require(held.Success, "empty Partial on a warm provider must return the official publication");
        SameObjects(changed.Value!, held.Value!, "zero partial observations must not erase official contents");
        return Task.CompletedTask;
    }

    public static Task FailedCaptureHoldsAllFieldsAsync()
    {
        var first = Monster(10, 1000);
        var second = Monster(11, 1001);
        var api = Provider();
        var initial = api.StabilizeWorldObjectRead(Context, Complete(first, second), Start);
        var failed = Capture(WorldObjectReadCompleteness.Failed,
            new[] { new WorldObjectObservation(first with { CurrentHp = 0, Position = new(999, 999, 999) }, AllFields) });
        foreach (var at in new[] { Start.AddSeconds(1), Start.AddHours(12), Start.AddDays(2) })
        {
            var held = api.StabilizeWorldObjectRead(Context, failed, at);
            Require(held.Success && ReferenceEquals(initial.Value, held.Value),
                "Failed must preserve the exact published object list regardless of age or attached observations");
            SameObjects(initial.Value!, AionVmmGameApi.MergeWorldObjectRead(failed, initial.Value!),
                "direct merge must ignore all observations on Failed");
        }
        return Task.CompletedTask;
    }

    public static async Task ColdStartRetriesUntilTrustedCompleteAsync()
    {
        foreach (var actualEmpty in new[] { true, false })
        {
            var logger = new InMemoryRoadhogLogger();
            var api = new PublicationGameApi(logger);
            using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var retryEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseFinal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var final = actualEmpty ? Complete() : Complete(Monster(10, 1000));
            api.ReadCaptureAsync = async (attempt, token) =>
            {
                if (attempt == 1) return WithProof(Complete(), null);
                if (attempt == 2) return Capture(WorldObjectReadCompleteness.Partial, Array.Empty<WorldObjectObservation>());
                retryEntered.TrySetResult();
                await releaseFinal.Task.WaitAsync(token).ConfigureAwait(false);
                return final;
            };
            var reader = new RoadhogSnapshotReader(Configuration(), api, logger, stop.Token);
            var pending = reader.ReadWorldObjectsAsync();
            try
            {
                await retryEntered.Task.WaitAsync(stop.Token).ConfigureAwait(false);
                Require(!pending.IsCompleted, "cold invalid empty and Partial0 cannot fabricate official absence");
                releaseFinal.TrySetResult();
                var published = await pending.WaitAsync(stop.Token).ConfigureAwait(false);
                Require(api.WorldReadCount == 3 && published.Value.Count == (actualEmpty ? 0 : 1),
                    "first proven Complete must publish immediately after cold provider retries");
                Require(logger.Entries.Any(entry => entry.EventName == "snapshot.read.retry"),
                    "cold uncertainty is diagnosed below the business interface");
            }
            finally
            {
                stop.Cancel();
                releaseFinal.TrySetResult();
                try { await pending.ConfigureAwait(false); } catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
            }
        }
    }

    public static Task IdentityAndSessionIsolationAsync()
    {
        var original = Monster(10, 1000);
        var api = Provider();
        api.StabilizeWorldObjectRead(Context, Complete(original), Start);
        var reusedEntity = original with { ServerObjectId = 2000, CurrentHp = 0, MaxHp = 0 };
        var partial = Capture(WorldObjectReadCompleteness.Partial, new[]
        {
            new WorldObjectObservation(reusedEntity, new(false, false, true, true, true, true))
        });
        var held = api.StabilizeWorldObjectRead(Context, partial, Start.AddSeconds(1));
        Require(held.Success && held.Value!.Count == 1 && held.Value[0] == original,
            "reused short entity ID must not inherit HP from a different server identity");
        var sameServer = original with { EntityId = 11, CurrentHp = 30 };
        var updated = api.StabilizeWorldObjectRead(Context,
            Capture(WorldObjectReadCompleteness.Partial, new[] { new WorldObjectObservation(sameServer, AllFields) }), Start.AddSeconds(2));
        Require(updated.Success && updated.Value!.Count == 1 && updated.Value[0] == sameServer,
            "stable server identity must survive short entity ID changes");
        var failed = Capture(WorldObjectReadCompleteness.Failed, Array.Empty<WorldObjectObservation>());
        foreach (var isolated in new[]
        {
            Context with { AccountName = "other-account" }, Context with { ProcessId = 9000 },
            Context with { VmmDeviceName = "other-device" }, Context with { TargetProcessName = "other-process.bin" }
        })
            Require(!api.StabilizeWorldObjectRead(isolated, failed, Start.AddSeconds(3)).Success,
                "another account, process selector, PID or device cannot inherit the publication");
        var lifecycle = new WorldObjectReadResult(WorldObjectReadCompleteness.Failed,
            Array.Empty<WorldObjectObservation>(), failed.Diagnostics, "Target process not found: Aion.bin");
        Require(!api.StabilizeWorldObjectRead(Context, lifecycle, Start.AddSeconds(4)).Success &&
            !api.StabilizeWorldObjectRead(Context, failed, Start.AddSeconds(5)).Success,
            "confirmed process lifecycle reset must invalidate the old official publication");
        return Task.CompletedTask;
    }

    public static async Task ControllerReturnHomeUsesOnlyTrustedAbsenceAsync()
    {
        var target = Monster(10, 1000);
        foreach (var trustedAbsence in new[] { false, true })
        {
            var logger = new InMemoryRoadhogLogger();
            var api = new PublicationGameApi(logger);
            api.Provider.StabilizeWorldObjectRead(Context, Complete(target), Start);
            api.ReadCaptureAsync = (_, _) => Task.FromResult(trustedAbsence ? Complete() : WithProof(Complete(), null));
            using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var input = new RecordingKeyboardInput();
            var config = Configuration();
            config.ScriptSettings = new ScriptSettings
            {
                MainMode = AccountMainMode.CustomCombat, CombatMode = AccountCombatMode.Stationary,
                Combat = new CombatScriptSettings
                {
                    HasStationaryCombatPosition = true, StationaryCombatX = 0, StationaryCombatY = 0,
                    StationaryCombatZ = 0, StationaryCombatRadius = 30, ReturnHomeWhenNoTarget = true,
                    EnableLoot = false
                }
            };
            var context = new AccountWorkerContext(config, new RoadhogSnapshotReaderFactory(api), logger,
                new AccountRuntimeManager(logger), new AccountWorkerOptions(), stop.Token);
            var controller = new StationaryCombatController(input, new SemiAutoCombatController(input));
            var state = new StationaryCombatState();
            var semiState = new SemiAutoCombatState();
            try
            {
                await controller.TickAsync(context, SemiAutoSkillPlan.FromSettings(config.ScriptSettings.Skills),
                    semiState, state).WaitAsync(stop.Token).ConfigureAwait(false);
                var returnedHome = logger.Entries.Any(entry => entry.EventName == "stationary_combat.no_target.return_home");
                Require(api.WorldReadCount > 0 && returnedHome == trustedAbsence,
                    "controller must return home for proven absence and retain its target through an unproven empty capture");
                Require(trustedAbsence ? state.CandidateEntityId == 0 && input.KeyDowns.Contains("W") : state.CandidateEntityId == target.EntityId,
                    "provider publication must preserve normal target selection and genuine no-target movement");
            }
            finally
            {
                try { await controller.PrepareForChannelSwitchAttemptAsync(context, semiState, state).ConfigureAwait(false); }
                finally { stop.Cancel(); }
            }
        }
    }

    public static Task PublicationDiagnosticsExplainHoldAndPruneAsync()
    {
        var logger = new InMemoryRoadhogLogger();
        var api = new AionVmmGameApi(new AionVmmGameApiOptions(), logger);
        var first = Monster(10, 1000);
        var second = Monster(11, 1001);
        api.StabilizeWorldObjectRead(Context, Complete(first, second), Start);
        var invalidEmpty = WithProof(Complete(), null);
        invalidEmpty = WithDiagnostics(invalidEmpty, invalidEmpty.Diagnostics with { CaptureSequence = 7 });
        api.StabilizeWorldObjectRead(Context, invalidEmpty, Start.AddSeconds(1));
        var held = logger.Entries.Last(entry => entry.EventName == "vmm.world_objects.publication").Fields;
        Require(Equals(held["observedCompleteness"], "Complete") && Equals(held["effectiveCompleteness"], "Failed") &&
            Equals(held["mode"], "hold") && Equals(held["coverageRejected"], true) && Convert.ToInt64(held["captureSequence"]) == 7 &&
            Convert.ToInt32(held["previousCount"]) == 2 && Convert.ToInt32(held["publishedCount"]) == 2 && Convert.ToInt32(held["prunedCount"]) == 0,
            "provider log must associate rejected Complete with its capture and unchanged official contents");
        var shortRead = Complete(second);
        shortRead = WithDiagnostics(shortRead, shortRead.Diagnostics with { CaptureSequence = 8 });
        api.StabilizeWorldObjectRead(Context, shortRead, Start.AddSeconds(2));
        var pruned = logger.Entries.Last(entry => entry.EventName == "vmm.world_objects.publication").Fields;
        Require(Equals(pruned["effectiveCompleteness"], "Complete") && Equals(pruned["mode"], "replace") &&
            Equals(pruned["coverageRejected"], false) && Convert.ToInt64(pruned["captureSequence"]) == 8 &&
            Convert.ToInt32(pruned["previousCount"]) == 2 && Convert.ToInt32(pruned["publishedCount"]) == 1 &&
            Convert.ToInt32(pruned["prunedCount"]) == 1 && Equals(pruned["prunedIdentities"], "s:1000"),
            "legitimate pruning must identify exactly which official target disappeared");
        api.StabilizeWorldObjectRead(Context, Complete(), Start.AddSeconds(3));
        var beforeRepeat = logger.Entries.Count(entry => entry.EventName == "vmm.world_objects.publication");
        api.StabilizeWorldObjectRead(Context, Complete(), Start.AddSeconds(4));
        Require(logger.Entries.Count(entry => entry.EventName == "vmm.world_objects.publication") == beforeRepeat,
            "repeated proven empty captures must not produce a publication log storm");
        return Task.CompletedTask;
    }

    public static async Task ApproachKeepsDirectionUntilProvenTargetAbsenceAsync()
    {
        var previousBearingMode = Environment.GetEnvironmentVariable("AION_FACE_TARGET_BEARING_MODE");
        Environment.SetEnvironmentVariable("AION_FACE_TARGET_BEARING_MODE", "y-x");
        try
        {
            foreach (var scenario in new[] { "bad_empty", "bad_short", "true_empty" })
            {
                var logger = new InMemoryRoadhogLogger();
                var api = new PublicationGameApi(logger)
                {
                    Player = new PlayerSnapshot(1, 0, "Fake", 100, 100, 100, 100, 0, new(6, 0, 0), Start, 90, 10, 90)
                };
                var target = Monster(10, 1000) with { Position = new(40, 0, 0), DistanceToLocalPlayer = 34 };
                var fartherTarget = Monster(11, 1001) with { Position = new(55, 0, 0), DistanceToLocalPlayer = 49 };
                var baseline = Complete(target, fartherTarget);
                var next = scenario == "bad_short" ? WithProof(Complete(fartherTarget), null)
                    : scenario == "bad_empty" ? WithProof(Complete(), null) : Complete();
                var injectNext = false;
                api.ReadCaptureAsync = (_, _) => Task.FromResult(injectNext ? next : baseline);
                using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                var input = new RecordingKeyboardInput();
                var config = Configuration();
                config.ScriptSettings = new ScriptSettings
                {
                    MainMode = AccountMainMode.CustomCombat, CombatMode = AccountCombatMode.Stationary,
                    Combat = new CombatScriptSettings { HasStationaryCombatPosition = true, StationaryCombatRadius = 60,
                        ReturnHomeWhenNoTarget = true, EnableLoot = false }
                };
                var context = new AccountWorkerContext(config, new RoadhogSnapshotReaderFactory(api), logger,
                    new AccountRuntimeManager(logger), new AccountWorkerOptions(), stop.Token);
                var controller = new StationaryCombatController(input, new SemiAutoCombatController(input));
                var state = new StationaryCombatState();
                var semiState = new SemiAutoCombatState();
                var plan = SemiAutoSkillPlan.FromSettings(config.ScriptSettings.Skills);
                try
                {
                    await controller.TickAsync(context, plan, semiState, state).WaitAsync(stop.Token).ConfigureAwait(false);
                    Require(state.CandidateServerObjectId == target.ServerObjectId && state.IsMovingForward &&
                        state.IsRightMouseDown && state.PathFollowPoller is not null,
                        "baseline must actually start path following toward the selected monster");
                    var forwardReleases = input.KeyUps.Count(key => key == "W");
                    var relativeMoves = input.MouseCommands.Count(command => command.StartsWith("move:", StringComparison.Ordinal));
                    var baselineReads = api.WorldReadCount;
                    injectNext = true;
                    await controller.TickAsync(context, plan, semiState, state).WaitAsync(stop.Token).ConfigureAwait(false);
                    Require(api.WorldReadCount > baselineReads, "second tick must consume the injected provider capture");
                    var returnedHome = logger.Entries.Any(entry => entry.EventName == "stationary_combat.no_target.return_home");
                    if (scenario == "true_empty")
                        Require(returnedHome && state.CandidateEntityId == 0,
                            "proven target disappearance must keep the normal return-home behavior while approaching");
                    else
                    {
                        Require(!returnedHome && state.CandidateServerObjectId == target.ServerObjectId && state.IsMovingForward,
                            "an interrupted empty or short capture must not redirect an active approach homeward");
                        Require(input.KeyUps.Count(key => key == "W") == forwardReleases &&
                            input.MouseCommands.Count(command => command.StartsWith("move:", StringComparison.Ordinal)) == relativeMoves,
                            "retained approach must keep its forward input and camera direction through the read fault");
                    }
                }
                finally
                {
                    try { await controller.PrepareForChannelSwitchAttemptAsync(context, semiState, state).ConfigureAwait(false); }
                    finally { stop.Cancel(); }
                }
            }
        }
        finally { Environment.SetEnvironmentVariable("AION_FACE_TARGET_BEARING_MODE", previousBearingMode); }
    }

    private static AionVmmGameApi Provider() => new(new AionVmmGameApiOptions(), NoOpRoadhogLogger.Instance);
    private static WorldObjectSnapshot Monster(ushort entityId, uint serverId) =>
        new(entityId, serverId, "target " + serverId, "monster", new(10, 0, 0), 4, 90, 100);
    private static AccountConfig Configuration() => new()
    {
        AccountName = Context.AccountName, ProcessId = Context.ProcessId, TargetProcessName = Context.TargetProcessName,
        VmmDeviceName = Context.VmmDeviceName, MainMode = AccountMainMode.CustomCombat
    };
    private static WorldObjectReadResult Complete(params WorldObjectSnapshot[] objects) =>
        Capture(WorldObjectReadCompleteness.Complete, objects.Select(item => new WorldObjectObservation(item, AllFields)).ToArray());

    private static WorldObjectReadResult Capture(WorldObjectReadCompleteness completeness,
        IReadOnlyList<WorldObjectObservation> observations, int? treeNodeCount = null)
    {
        var count = treeNodeCount ?? observations.Count;
        var empty = count == 0;
        var proof = new WorldObjectTreeProof(Header, empty ? Header : Root,
            empty ? Header : count <= 2 ? Root : First,
            empty ? Header : count == 1 ? Root : Last, count, true, true, true);
        var diagnostics = new WorldObjectReadDiagnostics(1, Start, Start, false,
            empty ? WorldObjectTraversalTermination.EmptyTree : WorldObjectTraversalTermination.ReachedTreeEnd,
            true, count, observations.Count, observations.Count, observations.Count,
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, null, proof);
        return new WorldObjectReadResult(completeness, observations, diagnostics,
            completeness == WorldObjectReadCompleteness.Complete ? null : "injected capture fault");
    }
    private static WorldObjectReadResult WithProof(WorldObjectReadResult read, WorldObjectTreeProof? proof) =>
        WithDiagnostics(read, read.Diagnostics with { TreeProof = proof });
    private static WorldObjectReadResult WithDiagnostics(WorldObjectReadResult read, WorldObjectReadDiagnostics diagnostics) =>
        new(read.Completeness, read.Observations, diagnostics, read.Error);
    private static void SameObjects(IReadOnlyList<WorldObjectSnapshot> expected, IReadOnlyList<WorldObjectSnapshot> actual, string message) =>
        Require(expected.OrderBy(item => item.ServerObjectId).SequenceEqual(actual.OrderBy(item => item.ServerObjectId)), message);
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    // No hardware transport is created: only the real provider's stabilization and merge boundary executes.
    private sealed class PublicationGameApi : IRoadhogGameApi
    {
        private readonly FakeGameApi _otherChannels = new()
        {
            Player = new PlayerSnapshot(1, 0, "Fake", 100, 100, 100, 100, 0, new(6, 0, 0), Start, 270, 10, 270),
            TargetEntityId = 0, TargetCurrentHp = 0, TargetMaxHp = 0, TargetPosition = null
        };
        public PublicationGameApi(IRoadhogLogger logger) => Provider = new(new AionVmmGameApiOptions(), logger);
        public AionVmmGameApi Provider { get; }
        public PlayerSnapshot Player { get => _otherChannels.Player; set => _otherChannels.Player = value; }
        public int WorldReadCount { get; private set; }
        public Func<int, CancellationToken, Task<WorldObjectReadResult>> ReadCaptureAsync { private get; set; } =
            (_, _) => throw new InvalidOperationException("test capture source was not configured");
        public async Task<OperationResult<IReadOnlyList<WorldObjectSnapshot>>> ReadWorldObjectsAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var capture = await ReadCaptureAsync(++WorldReadCount, cancellationToken).ConfigureAwait(false);
            return Provider.StabilizeWorldObjectRead(Context, capture, Start.AddMilliseconds(WorldReadCount));
        }
        public Task<OperationResult<PlayerSnapshot>> ReadPlayerAsync(CancellationToken cancellationToken = default) => _otherChannels.ReadPlayerAsync(cancellationToken);
        public Task<OperationResult<PlayerAbnormalStatusSnapshot>> ReadPlayerAbnormalStatusesAsync(CancellationToken cancellationToken = default) => _otherChannels.ReadPlayerAbnormalStatusesAsync(cancellationToken);
        public Task<OperationResult<SummonedPetSnapshot>> ReadSummonedPetAsync(CancellationToken cancellationToken = default) => _otherChannels.ReadSummonedPetAsync(cancellationToken);
        public Task<OperationResult<SummonedPetRosterSnapshot>> ReadSummonedPetRosterAsync(CancellationToken cancellationToken = default) => _otherChannels.ReadSummonedPetRosterAsync(cancellationToken);
        public Task<OperationResult<LockedTargetSnapshot>> ReadLockedTargetAsync(CancellationToken cancellationToken = default) => _otherChannels.ReadLockedTargetAsync(cancellationToken);
        public Task<OperationResult<LockedTargetAbnormalStatusSnapshot>> ReadLockedTargetAbnormalStatusesAsync(CancellationToken cancellationToken = default) => _otherChannels.ReadLockedTargetAbnormalStatusesAsync(cancellationToken);
        public Task<OperationResult<IReadOnlyList<SkillSnapshot>>> ReadSkillsAsync(CancellationToken cancellationToken = default) => _otherChannels.ReadSkillsAsync(cancellationToken);
        public Task<OperationResult<IReadOnlyList<InventoryItemSnapshot>>> ReadInventoryAsync(CancellationToken cancellationToken = default) => _otherChannels.ReadInventoryAsync(cancellationToken);
        public Task<OperationResult<GatherSnapshot>> ReadGatherSnapshotAsync(CancellationToken cancellationToken = default) => _otherChannels.ReadGatherSnapshotAsync(cancellationToken);
        public Task<OperationResult<IReadOnlyList<LootCorpseSnapshot>>> ReadLootCorpsesAsync(CancellationToken cancellationToken = default) => _otherChannels.ReadLootCorpsesAsync(cancellationToken);
    }
}
