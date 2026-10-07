using Roadhog.Application;
using Roadhog.Application.SemiAuto;
using Roadhog.Application.Workers;
using Roadhog.Core.Accounts;
using Roadhog.Core.Api;
using Roadhog.Core.Diagnostics;
using Roadhog.Core.Model;
using Roadhog.Infrastructure.Vmm;

internal static class QuickbarSpiritmasterDotTests
{
    private const uint DotId = 1389;
    private const uint DifferentStatusDotId = 113580;
    private const uint OtherId = 1600;
    private const uint LearnedId = 113582;

    public static async Task ActiveStatusAndExpirationAsync()
    {
        using var f = new Fixture();
        f.SetAbnormal(DotId);
        await f.PrepareAsync();
        await f.TickAsync();
        Sequence(new[] { "D2" }, f.Keyboard.Keys, "actual target DOT skips the configured first root in the new mode");
        f.Confirm(OtherId);
        f.SetAbnormal();
        f.Clock.Advance(80);
        await f.TickAsync();
        Sequence(new[] { "D2", "D1" }, f.Keyboard.Keys, "disappearance of the target status allows DOT again without a local duration window");
        Check(f.State.QuickbarSkills.PendingAction?.Node.SkillId == DotId, "the new executor owns the resumed DOT action");
    }

    public static async Task ImmediateDifferentAbnormalLearningAsync()
    {
        using var f = new Fixture(DifferentStatusDotId);
        f.SetAbnormal(4000);
        f.Keyboard.AfterPress = key =>
        {
            if (key != "D1") return;
            f.SetAbnormal(4000, LearnedId);
            f.Confirm(DifferentStatusDotId);
        };
        await f.PrepareAsync();
        await f.TickAsync();
        Learned(f, LearnedId, "other DOT skills still learn a new abnormal ID from the same target", DifferentStatusDotId);
        var learned = f.Logger.Entries.Single(entry => entry.EventName == "semi_auto.spiritmaster.dot_learned");
        Equal(DifferentStatusDotId, (uint)learned.Fields["skillId"]!, "learning identifies the requested DOT skill");
        Equal(LearnedId, (uint)learned.Fields["abnormalId"]!, "learning excludes the preexisting abnormal ID");
        Equal(f.Api.TargetOwnServerObjectId, (uint)learned.Fields["targetServerObjectId"]!, "learning records the target server identity");
        await f.TickAsync();
        Sequence(new[] { "D1", "D2" }, f.Keyboard.Keys, "learned abnormal ID suppresses a repeat DOT after release confirmation");
        Check(f.Logger.Entries.Any(entry => entry.EventName == "quickbar_skill.release.confirmed"),
            "DOT learning preserves the executor's existing independent release confirmation");
    }

    public static async Task LateStatusStopsUnconfirmedRetryAsync()
    {
        using var f = new Fixture(DifferentStatusDotId);
        f.SetAbnormal(4000);
        await f.PrepareAsync();
        await f.TickAsync();
        Check(f.State.QuickbarSkills.PendingAction is { AttemptCount: 1 }, "key delivery alone leaves the DOT unconfirmed");
        f.Clock.Advance(3500);
        f.SetAbnormal(4000, LearnedId);
        await f.TickAsync();
        Learned(f, LearnedId, "other DOT skills still learn a late target abnormal", DifferentStatusDotId);
        Sequence(new[] { "D1", "D2" }, f.Keyboard.Keys, "late actual DOT suppresses the pending retry and permits another root");
        Check(!f.Logger.Entries.Any(entry => entry.EventName == "quickbar_skill.release.confirmed"),
            "an abnormal-status hit never invents actor or cooldown confirmation");
    }

    public static async Task UnconfirmedRetryPreservesBaselineAsync()
    {
        using var f = new Fixture(DifferentStatusDotId);
        f.SetAbnormal(4000);
        var attempts = 0;
        f.Keyboard.AfterPress = key =>
        {
            if (key == "D1" && ++attempts == 2) f.SetAbnormal(4000, LearnedId);
        };
        await f.PrepareAsync();
        await f.TickAsync();
        f.Clock.Advance(79);
        await f.TickAsync();
        Sequence(new[] { "D1" }, f.Keyboard.Keys, "no status and no release evidence retain the existing 80ms retry cadence");
        f.Clock.Advance(1);
        await f.TickAsync();
        Sequence(new[] { "D1", "D1" }, f.Keyboard.Keys, "absence of DOT never suppresses an eligible unconfirmed retry");
        Learned(f, LearnedId, "the second successful key retains the first pre-press abnormal baseline", DifferentStatusDotId);
        await f.TickAsync();
        Sequence(new[] { "D1", "D1", "D2" }, f.Keyboard.Keys, "the newly learned actual status stops the next retry");
        Check(!f.Logger.Entries.Any(entry => entry.EventName == "quickbar_skill.release.confirmed"),
            "retry learning leaves release confirmation semantics unchanged");
    }

    public static async Task TargetServerIdentityAsync()
    {
        using var f = new Fixture();
        f.SetAbnormal(DotId);
        await f.PrepareAsync();
        await f.TickAsync();
        Equal("D2", f.Keyboard.Keys.Single(), "first target has the DOT");
        f.Api.TargetOwnServerObjectId = 2000;
        f.SetAbnormal();
        await f.TickAsync();
        Sequence(new[] { "D2", "D1" }, f.Keyboard.Keys, "a reused entity ID with a new server ID has its own DOT eligibility");
        Check(f.State.QuickbarSkills.PendingAction is { AttemptCount: 1, Node.SkillId: DotId },
            "target change clears the previous target's pending action");
    }

    public static async Task DisabledAndOtherClassCompatibilityAsync()
    {
        foreach (var mode in new[] { "disabled", "other-class" })
        {
            using var f = new Fixture();
            if (mode == "disabled") f.Settings.Skills.SpiritmasterAutoSkillLogicEnabled = false;
            else f.Api.Player = f.Api.Player with { CharacterClassId = AionClassId.Sorcerer };
            f.SetAbnormal(DotId);
            await f.PrepareAsync();
            await f.TickAsync();
            Sequence(new[] { "D1" }, f.Keyboard.Keys, mode + " preserves ordinary new-mode root selection");
            Check(!f.State.TryGetSpiritmasterDotAbnormalId(DotId, out _), mode + " does not learn DOT state");
            Check(!f.Logger.Entries.Any(entry => entry.EventName == "semi_auto.spiritmaster.dot_learned"),
                mode + " adds no Spiritmaster learning event");
        }
    }

    public static async Task FailedInputDoesNotLearnAsync()
    {
        using var f = new Fixture();
        f.SetAbnormal(4000);
        f.Keyboard.PressResult = key => key == "D1"
            ? Roadhog.Core.Common.OperationResult.Fail("mock transport failure")
            : Roadhog.Core.Common.OperationResult.Ok();
        await f.PrepareAsync();
        await f.TickAsync();
        f.SetAbnormal(4000, LearnedId);
        await f.TickAsync();
        Sequence(new[] { "D1", "D2" }, f.Keyboard.Keys, "failed input keeps the existing temporary rejection and fallback");
        Check(!f.State.TryGetSpiritmasterDotAbnormalId(DotId, out _), "failed key cannot establish a DOT-learning baseline");
        Check(!f.Logger.Entries.Any(entry => entry.EventName == "semi_auto.spiritmaster.dot_learned"),
            "later unrelated target statuses do not get attributed to failed input");
    }

    public static async Task LaterAttackCannotContaminateLearningAsync()
    {
        using var f = new Fixture();
        f.SetAbnormal(4000);
        f.Keyboard.AfterPress = key =>
        {
            if (key == "D1") f.Confirm(DotId);
            else if (key == "D2") f.SetAbnormal(4000, LearnedId);
        };
        await f.PrepareAsync();
        await f.TickAsync();
        await f.TickAsync();
        Sequence(new[] { "D1", "D2" }, f.Keyboard.Keys, "a released but resisted DOT hands over to the next attack");
        f.Clock.Advance(80);
        await f.TickAsync();
        Check(!f.State.TryGetSpiritmasterDotAbnormalId(DotId, out _),
            "a later attack's new debuff cannot be learned as the resisted DOT's abnormal ID");
        Check(!f.Logger.Entries.Any(entry => entry.EventName == "semi_auto.spiritmaster.dot_learned"),
            "the canceled DOT observation produces no false learning event");
        Equal(1, f.Logger.Entries.Count(entry => entry.EventName == "quickbar_skill.release.confirmed"),
            "the later debuff cannot confirm its unaccepted attack");
    }

    public static async Task BeforePressStatusRecheckAsync()
    {
        using var f = new Fixture();
        await f.PrepareAsync();
        var reads = 0;
        Task<LockedTargetAbnormalStatusSnapshot> Read()
        {
            f.SetAbnormal(++reads >= 2 ? new[] { DotId } : Array.Empty<uint>());
            return Task.FromResult(f.Api.LockedTargetAbnormalStatuses!);
        }
        await f.TickEngineAsync(readAbnormal: Read);
        Check(reads >= 2, "DOT statuses are re-read at the actual input boundary");
        Sequence(new[] { "D2" }, f.Keyboard.Keys, "a DOT that appears after selection retracts the first root before any key");
    }

    public static async Task TargetChangeDuringDotReadIsGuardedAsync()
    {
        foreach (var combinedSnapshot in new[] { false, true })
        foreach (var changeAtRead in new[] { 1, 2 })
        {
            using var f = new Fixture();
            SkillAvailabilityCombatSnapshot Guard() => new(f.Api.Player.EntityId, 10, f.Api.TargetEntityId,
                f.Api.TargetOwnServerObjectId, 100, 100);
            if (combinedSnapshot) f.Api.SkillAvailability = f.Api.SkillAvailability with { CombatState = Guard() };
            await f.PrepareAsync();
            var reads = 0;
            Task<LockedTargetAbnormalStatusSnapshot> Read()
            {
                if (++reads == changeAtRead)
                {
                    f.Api.TargetOwnServerObjectId = 2000;
                    f.SetAbnormal();
                    if (combinedSnapshot) f.Api.SkillAvailability = f.Api.SkillAvailability with { CombatState = Guard() };
                }
                return Task.FromResult(f.Api.LockedTargetAbnormalStatuses!);
            }
            await f.TickEngineAsync(readAbnormal: Read);
            var scenario = $"combined snapshot {combinedSnapshot}, target change during DOT read {changeAtRead}";
            Check(reads >= changeAtRead, scenario + " reaches the intended target-change boundary");
            Check(f.Keyboard.Keys.IsEmpty, scenario + " rechecks the target before any attack key");
            Check(f.State.QuickbarSkills.YieldToWorker && f.State.QuickbarSkills.PendingAction is null,
                scenario + " yields and clears old-target action state");
            Check(!f.Logger.Entries.Any(entry => entry.EventName == "quickbar_skill.key.pressed"),
                scenario + " emits no false input event");
        }
    }

    public static async Task ClockBootstrapCannotBypassActiveDotAsync()
    {
        using var f = new Fixture();
        f.SetAbnormal(DotId);
        f.UseUnknownCooldownClock();
        await f.PrepareAsync();
        await f.TickEngineAsync(unknownClock: true);
        Sequence(new[] { "D2" }, f.Keyboard.Keys, "unknown-CD startup proposals exclude target-active DOT roots");
        Check(f.State.QuickbarSkills.PendingAction is { IsClockBootstrap: true, Node.SkillId: OtherId },
            "the unsuppressed next root still receives its normal finite bootstrap attempt");
        Equal(1, f.State.QuickbarSkills.ClockBootstrap.AttemptedCandidateCount, "suppressed DOT never spends a bootstrap candidate");
    }

    public static async Task ActiveStatusStopsBootstrapRetryAsync()
    {
        using var f = new Fixture();
        f.UseUnknownCooldownClock();
        await f.PrepareAsync();
        await f.TickEngineAsync(unknownClock: true);
        Sequence(new[] { "D1" }, f.Keyboard.Keys, "absent DOT allows the first finite unknown-CD startup attempt");
        Check(f.State.QuickbarSkills.PendingAction is { IsClockBootstrap: true, AttemptCount: 1, Node.SkillId: DotId },
            "the DOT begins as an unconfirmed bootstrap action");
        f.SetAbnormal(DotId);
        f.Clock.Advance(80);
        await f.TickEngineAsync(unknownClock: true);
        Sequence(new[] { "D1", "D2" }, f.Keyboard.Keys, "an actual target DOT stops bootstrap retries and hands over to the next root");
        Check(f.State.QuickbarSkills.PendingAction is { IsClockBootstrap: true, AttemptCount: 1, Node.SkillId: OtherId },
            "the next root receives a fresh candidate instead of retaining the DOT's baseline");
        Equal(OtherId, f.State.QuickbarSkills.ClockBootstrap.CurrentCandidate!.SkillId, "the active DOT candidate is finished");
        Equal(2, f.State.QuickbarSkills.ClockBootstrap.AttemptedCandidateCount, "the handoff spends only the two actual startup candidates");
        Check(f.Logger.Entries.Any(entry => entry.EventName == "quickbar_skill.clock.bootstrap.candidate.finished" &&
            Equals(entry.Fields["skillId"], DotId) && Equals(entry.Fields["reason"], "target_dot_active")),
            "the original candidate ends specifically because the target DOT became active");
        Check(!f.Logger.Entries.Any(entry => entry.EventName == "quickbar_skill.release.confirmed"),
            "active target DOT does not fabricate actor or cooldown confirmation for the startup attempt");
    }

    public static async Task ActiveDotDoesNotBlockSameIdChainAsync()
    {
        using var f = new Fixture();
        f.Settings.QuickbarSkills.ExecutionTree = new() { Node(DotId), Node(OtherId, Node(DotId)) };
        f.SetAbnormal(DotId);
        f.SetSlotCanUse(DotId, false);
        f.Keyboard.AfterPress = key =>
        {
            if (key != "D2") return;
            f.Confirm(OtherId);
            f.SetSlotCanUse(DotId, true);
        };
        await f.PrepareAsync();
        await f.TickEngineAsync();
        Sequence(new[] { "D2" }, f.Keyboard.Keys, "suppression applies to the first DOT root while another root begins its chain");
        await f.TickEngineAsync();
        Sequence(new[] { "D2", "D1" }, f.Keyboard.Keys, "an available child with the same DOT ID preserves continuation priority");
        Check(f.State.QuickbarSkills.PendingAction?.Node.NodeKey == "1/0", "the second key belongs to the configured child, not the suppressed root");
        Learned(f, DotId, "continuing a chain preserves the already learned DOT mapping");
    }

    public static async Task WrongTargetSnapshotCannotSuppressOrLearnAnotherSkillAsync()
    {
        using var f = new Fixture();
        await f.PrepareAsync();
        var target = f.Target();
        var snapshot = f.Api.LockedTargetAbnormalStatuses!;
        var policy = f.CreatePolicy(() => Task.FromResult(snapshot));
        Check(!(await policy.ReadSuppressedRootSkillIdsAsync()).Contains(DotId), "a confirmed same-target empty status list permits DOT");
        var root = f.NewPlan().Roots[0];
        f.State.QuickbarSkills.BeginAction(root, f.Api.Skills[0], f.Api.SkillAvailability, f.Clock.GetUtcNow(), TimeSpan.FromSeconds(8));
        snapshot = snapshot with { Target = target with { ServerObjectId = target.ServerObjectId + 1 }, Entries = Entries(LearnedId) };
        await policy.OnSkillPressedAsync(root);
        Check((await policy.ReadSuppressedRootSkillIdsAsync()).Contains(DotId), "a status snapshot for a different server ID conservatively holds the DOT root");
        Check(!f.State.TryGetSpiritmasterDotAbnormalId(DotId, out _), "a different target's abnormal ID never gets learned");
        snapshot = snapshot with { Target = target };
        Check(!(await policy.ReadSuppressedRootSkillIdsAsync()).Contains(DotId), "restored same-target unrelated statuses remove the conservative hold");
        Check(!f.State.TryGetSpiritmasterDotAbnormalId(DotId, out _),
            "returning to the old target with the same new abnormal ID proves that the foreign-target read canceled learning");
    }

    public static async Task ConfiguredNameAndNoDotReadAsync()
    {
        using var f = new Fixture();
        f.Settings.Skills.Spiritmaster.DotSkills = new() { new() { SkillId = 1388, SkillName = Name(DotId) } };
        f.SetAbnormal(DotId);
        await f.PrepareAsync();
        await f.TickAsync();
        Sequence(new[] { "D2" }, f.Keyboard.Keys, "configured DOT name still identifies the exact new-tree rank");
        var reads = 0;
        var emptyPolicy = new QuickbarSpiritmasterDotPolicy(f.State, f.NewPlan(), f.Api.Skills, new(), f.Target(),
            () => { reads++; return Task.FromResult(f.Api.LockedTargetAbnormalStatuses!); }, f.Clock);
        Check(!emptyPolicy.HasDotRoots, "unconfigured roots do not activate DOT policy");
        Check((await emptyPolicy.ReadSuppressedRootSkillIdsAsync()).Count == 0, "no DOT roots produces no suppression");
        Equal(0, reads, "no DOT roots adds no abnormal-status reads");
    }

    public static async Task ErosionOpeningStatusDoesNotStopUnconfirmedRetryAsync()
    {
        foreach (var unrelatedId in new uint[] { 1603, 65536, LearnedId })
        {
            using var f = new Fixture();
            f.SetAbnormal(4000);
            await f.PrepareAsync();
            await f.TickEngineAsync();
            f.Clock.Advance(3500);
            f.SetAbnormal(4000, unrelatedId);
            await f.TickEngineAsync();
            Sequence(new[] { "D1", "D1" }, f.Keyboard.Keys,
                "a delayed opener or unrelated status must not hand an unconfirmed Erosion to another root");
            Check(f.State.QuickbarSkills.PendingAction is { Node.SkillId: DotId, AttemptCount: 2, RetryStopped: false },
                "Erosion retains its finite retry budget when only another status appears");
            Check(!f.State.TryGetSpiritmasterDotAbnormalId(DotId, out _), "Erosion does not learn the unrelated status");
            Check(!f.Logger.Entries.Any(entry => entry.EventName == "semi_auto.spiritmaster.dot_learned"),
                "an unrelated status cannot produce an Erosion learning event");

            f.SetAbnormal(4000, unrelatedId, DotId);
            await f.TickEngineAsync();
            Sequence(new[] { "D1", "D1", "D2" }, f.Keyboard.Keys,
                "the actual Erosion status still stops redundant retries and permits another root");
            Learned(f, DotId, "Erosion remembers only its actual status ID");
            Check(!f.Logger.Entries.Any(entry => entry.EventName == "quickbar_skill.release.confirmed"),
                "a target status still does not fabricate release confirmation");
        }
    }

    public static async Task ErosionConfirmedReleaseDoesNotLearnOpeningStatusAsync()
    {
        using var f = new Fixture();
        f.SetAbnormal(4000);
        f.Keyboard.AfterPress = key =>
        {
            if (key != "D1") return;
            f.SetAbnormal(4000, 1603);
            f.Confirm(DotId);
        };
        await f.PrepareAsync();
        await f.TickAsync();
        await f.TickAsync();
        Sequence(new[] { "D1", "D2" }, f.Keyboard.Keys, "confirmed Erosion still hands off normally");
        Check(f.Logger.Entries.Any(entry => entry.EventName == "quickbar_skill.release.confirmed" &&
            Equals(entry.Fields["skillId"], DotId)), "the existing release evidence still confirms Erosion");
        Check(!f.State.TryGetSpiritmasterDotAbnormalId(DotId, out _),
            "even a confirmed Erosion cannot attribute the opener's status to itself");
        var policy = f.CreatePolicy(() => Task.FromResult(f.Api.LockedTargetAbnormalStatuses!));
        Check(!(await policy.ReadSuppressedRootSkillIdsAsync()).Contains(DotId),
            "the opener alone cannot authorize target-status suppression after release confirmation");
    }

    public static async Task ErosionIgnoresWrongLearnedIdAndStatusExpirationAsync()
    {
        using var f = new Fixture();
        f.State.RememberSpiritmasterDotAbnormalId(DotId, 1603);
        f.SetAbnormal(1603);
        await f.PrepareAsync();
        await f.TickEngineAsync();
        Sequence(new[] { "D1" }, f.Keyboard.Keys, "a previously learned opener cannot suppress Erosion");

        f.SetAbnormal(1603, DotId);
        await f.TickEngineAsync();
        Sequence(new[] { "D1", "D2" }, f.Keyboard.Keys, "actual Erosion remains authoritative beside the opener");
        Learned(f, DotId, "the actual status replaces the incorrect learned association");
        f.Confirm(OtherId);
        f.SetAbnormal(1603);
        f.Clock.Advance(80);
        await f.TickEngineAsync();
        Sequence(new[] { "D1", "D2", "D1" }, f.Keyboard.Keys,
            "Erosion becomes eligible again when its own status disappears even if the opener remains");
    }

    public static async Task ErosionLegacySelectionIgnoresWrongLearnedIdAsync()
    {
        using var f = new Fixture();
        f.Settings.Skills.ExecutionTree = new() { Node(DotId), Node(OtherId) };
        f.State.RememberSpiritmasterDotAbnormalId(DotId, 1603);
        f.SetAbnormal(1603);
        await f.PrepareAsync();
        var plan = SemiAutoSkillPlan.FromSettings(f.Settings.Skills, f.Context.SkillBindings);
        SemiAutoSkillReleaseDecision Select() => SpiritmasterAutoSkillReleasePriority.SelectNext(
            plan, f.State, f.Api.Skills, f.Settings.SemiAuto, f.Settings.Skills.Spiritmaster,
            new SpiritmasterCombatContext(f.Api.Player, null, f.Api.LockedTargetAbnormalStatuses), f.Clock.GetUtcNow());
        Equal(DotId, Select().Node!.SkillId, "legacy selection ignores the learned opener and keeps Erosion eligible");
        f.SetAbnormal(1603, DotId);
        Equal(OtherId, Select().Node!.SkillId, "legacy selection still skips the actual Erosion status");
        f.SetAbnormal(1603);
        Equal(DotId, Select().Node!.SkillId, "legacy selection resumes Erosion after its actual status disappears");
    }

    private static string Name(uint id) => "skill" + id;
    private static SkillConfigNode Node(uint id, params SkillConfigNode[] children) => new()
    {
        SkillId = id, Name = Name(id), BaseName = Name(id), Children = children.ToList()
    };
    private static IReadOnlyList<AbnormalStatusEntrySnapshot> Entries(params uint[] ids) => ids.Select(id =>
        new AbnormalStatusEntrySnapshot(0, id, PlayerAbnormalStatusSnapshot.PhysicalDebuffCategory, 0, 1, 0)).ToArray();

    private sealed class Fixture : IDisposable
    {
        public FakeGameApi Api { get; } = new();
        public RecordingKeyboardInput Keyboard { get; } = new();
        public InMemoryRoadhogLogger Logger { get; } = new();
        public SemiAutoCombatState State { get; } = new();
        public Clock Clock { get; } = new();
        public AccountWorkerContext Context { get; }
        public ScriptSettings Settings => Context.Config.ScriptSettings!;
        private readonly CancellationTokenSource stop = new(TimeSpan.FromSeconds(15));
        private readonly SemiAutoCombatController controller;
        private readonly AvailabilityReader availability;
        private SemiAutoSkillPlan maintenancePlan = null!;
        private uint releaseTime;

        public Fixture(uint dotId = DotId)
        {
            var settings = new ScriptSettings { SkillTreeReleaseMode = SkillTreeReleaseMode.QuickbarAvailability };
            settings.Skills.Mode = SkillConfigurationMode.Auto;
            settings.Skills.SpiritmasterAutoSkillLogicEnabled = true;
            settings.Skills.Spiritmaster.DotSkills.Add(new() { SkillId = dotId, SkillName = Name(dotId) });
            settings.Skills.OpeningSkill.Enabled = false;
            settings.SemiAuto.AttackKeyLoopEnabled = false;
            settings.SemiAuto.AttackWeaveEnabled = false;
            settings.Maintenance.SitMaintenanceEnabled = false;
            settings.QuickbarSkills.ExecutionTree = new() { Node(dotId), Node(OtherId) };
            Api.Player = Api.Player with { CharacterClassId = AionClassId.Spiritmaster };
            Api.TargetOwnServerObjectId = 1000;
            Api.Skills = new[] { dotId, OtherId }.Select(id => new SkillSnapshot(id, Name(id), 1, 1, Name(id), 1, false,
                10000, 0, XmlActivation: "Active", XmlSkillType: "Magical", XmlSubType: "Attack",
                XmlTargetRelationRestriction: "Enemy", XmlEffects: "SpellATK_Instant", XmlEffectRemainMs: 15000)).ToArray();
            Api.Quickbar = new(0, new QuickbarSlotSnapshot[]
            {
                new(SkillQuickbar.Main, 0, 21, dotId), new(SkillQuickbar.Main, 1, 21, OtherId)
            });
            Api.SkillAvailability = new(0, Api.Quickbar.Slots.Select(slot =>
                new SkillAvailabilitySlotSnapshot(slot.Bar, slot.Slot, 21, slot.SkillId, slot.SkillId, true)).ToArray(),
                BindingSignature: "quickbar-dot-layout");
            SetAbnormal();
            Context = new(new AccountConfig { AccountName = "quickbar-dot", ScriptSettings = settings },
                new RoadhogSnapshotReaderFactory(Api), Logger, new AccountRuntimeManager(Logger), new(), stop.Token);
            controller = new(Keyboard, timeProvider: Clock);
            availability = new(Api);
        }

        public async Task PrepareAsync()
        {
            await Context.PrepareSkillBindingsAsync();
            maintenancePlan = SemiAutoSkillPlan.FromSettings(Settings.Skills, Context.SkillBindings);
        }
        public Task<TimeSpan> TickAsync() => controller.TickAsync(Context, maintenancePlan, State);
        public QuickbarSkillPlan NewPlan() => QuickbarSkillPlan.FromSettings(Settings.QuickbarSkills, Context.SkillBindings!);
        public QuickbarSpiritmasterDotPolicy CreatePolicy(Func<Task<LockedTargetAbnormalStatusSnapshot>> read) =>
            new(State, NewPlan(), Api.Skills, Settings.Skills.Spiritmaster, Target(), read, Clock, Logger, Context.Config.AccountName);
        public Task<TimeSpan> TickEngineAsync(bool unknownClock = false, Func<Task<LockedTargetAbnormalStatusSnapshot>>? readAbnormal = null)
        {
            var plan = NewPlan();
            var policy = CreatePolicy(readAbnormal ?? (() => Task.FromResult(Api.LockedTargetAbnormalStatuses!)));
            return new QuickbarSkillCombatController(Keyboard, Clock).TickAsync(plan, State.QuickbarSkills, Target(), availability,
                ids => Task.FromResult<IReadOnlyList<SkillSnapshot>>(Api.Skills.Where(skill => ids.Contains(skill.SkillId)).ToArray()),
                Settings.SemiAuto, Logger, stop.Token, readTargetBeforePress: () => Task.FromResult(Target()),
                ordinaryReadiness: skills => skills.Select(skill => skill.SkillId).ToHashSet(),
                cooldownReadiness: _ => unknownClock ? SemiAutoSkillCooldownReadiness.Unknown : SemiAutoSkillCooldownReadiness.Ready,
                isCooldownClockCalibrated: unknownClock ? () => false : null,
                readSuppressedRootSkillIds: policy.ReadSuppressedRootSkillIdsAsync, onSkillPressed: policy.OnSkillPressedAsync);
        }
        public LockedTargetSnapshot Target() => new(Api.TargetEntityId, Api.TargetOwnServerObjectId, 1,
            LockedTargetSnapshot.MonsterObjectType, "dummy", 1000, 1000, null, 1, Clock.GetUtcNow());
        public void SetAbnormal(params uint[] ids) => Api.LockedTargetAbnormalStatuses = new(Target(), 0, Entries(ids), Clock.GetUtcNow());
        public void SetSlotCanUse(uint id, bool canUse) => Api.SkillAvailability = Api.SkillAvailability with
        {
            Slots = Api.SkillAvailability.Slots.Select(slot => slot.EffectiveSkillId == id ? slot with { CanUse = canUse } : slot).ToArray()
        };
        public void UseUnknownCooldownClock()
        {
            Api.Skills = Api.Skills.Select(skill => skill with { CooldownEndTime = 900 }).ToArray();
            Api.SkillAvailability = Api.SkillAvailability with
            {
                Slots = Array.Empty<SkillAvailabilitySlotSnapshot>(), UnsupportedSkillIds = new[] { DotId, OtherId },
                BindingSlots = Api.Quickbar.Slots.Select(slot =>
                    new SkillAvailabilityBindingSnapshot(slot.Bar, slot.Slot, 21, slot.SkillId, slot.SkillId)).ToArray(),
                LastReleasedSkillId = 999, LastReleasedSkillTime = 1000
            };
        }
        public void Confirm(uint id)
        {
            Api.Skills = Api.Skills.Select(skill => skill.SkillId == id
                ? skill with { CooldownEndTime = unchecked((uint)Environment.TickCount64 + 10000u) }
                : skill).ToArray();
            Api.SkillAvailability = Api.SkillAvailability with { LastReleasedSkillId = id, LastReleasedSkillTime = ++releaseTime };
        }
        public void Dispose() => stop.Dispose();
    }

    private sealed class Clock : TimeProvider
    {
        private DateTimeOffset now = DateTimeOffset.UtcNow;
        private long milliseconds;
        public override DateTimeOffset GetUtcNow() => now;
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() => milliseconds;
        public void Advance(int durationMs) { now = now.AddMilliseconds(durationMs); milliseconds += durationMs; }
    }
    private sealed class AvailabilityReader(FakeGameApi api) : ISkillAvailabilitySnapshotReader
    {
        private long version;
        public Task<PublishedGameSnapshot<SkillAvailabilitySnapshot>> ReadSkillAvailabilityAsync(long afterVersion = 0, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new PublishedGameSnapshot<SkillAvailabilitySnapshot>(++version, api.SkillAvailability));
        }
    }
    private static void Learned(Fixture f, uint expected, string message, uint skillId = DotId)
    {
        Check(f.State.TryGetSpiritmasterDotAbnormalId(skillId, out var learned), message + ": missing learned ID");
        Equal(expected, learned, message);
    }
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void Equal<T>(T expected, T actual, string message) => Check(EqualityComparer<T>.Default.Equals(expected, actual),
        message + $": expected {expected}, got {actual}");
    private static void Sequence(IEnumerable<string> expected, IEnumerable<string> actual, string message) => Check(expected.SequenceEqual(actual),
        message + $": expected [{string.Join(",", expected)}], got [{string.Join(",", actual)}]");
}
