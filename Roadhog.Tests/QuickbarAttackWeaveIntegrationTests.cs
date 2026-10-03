using System.Reflection;
using Roadhog.Application;
using Roadhog.Application.SemiAuto;
using Roadhog.Application.Workers;
using Roadhog.Core.Accounts;
using Roadhog.Core.Api;
using Roadhog.Core.Common;
using Roadhog.Core.Diagnostics;
using Roadhog.Core.Model;
using Roadhog.Infrastructure.Vmm;

internal static class QuickbarAttackWeaveIntegrationTests
{
    public static async Task OpeningAndMainTreeSharePairAsync()
    {
        using var f = new Fixture(true, 101, 102);
        f.Settings.Skills.OpeningSkill.ReleaseAll = false;
        f.Settings.QuickbarSkills.ExecutionTree.Add(Node(202));
        await f.PrepareAsync();
        await f.TickAsync();
        f.Confirm(101);
        await f.TickAsync();
        Sequence(new[] { "D1", "D4" }, f.Keyboard.Keys, "one confirmed opening hands off to the independent main tree");
        Equal(1, f.State.QuickbarSkills.AttackWeave.ConfirmedCount, "opening confirmation contributes to the new-mode pair");
        f.Confirm(201);
        await f.TickAsync();
        Check(f.State.QuickbarSkills.AttackWeave.IsWaiting, "opening and main-tree release jointly schedule C");
        Equal(2, f.State.QuickbarSkills.AttackWeave.ConfirmedCount, "the shared pair includes two actual releases");

        f.Api.TargetEntityId = 102;
        f.Api.TargetOwnServerObjectId = 2000;
        await f.TickAsync();
        Equal(0, f.State.QuickbarSkills.AttackWeave.ConfirmedCount, "new target cancels the previous pair before its opening");
        Check(!f.State.QuickbarSkills.AttackWeave.IsWaiting && !f.Keyboard.Keys.Contains("C"), "old-target C never leaks into the new opening");
        Equal("D2", f.Keyboard.Keys.Last(), "new target skips the still-cooling first opening and releases its ready fallback");
        f.Confirm(102);
        await f.TickAsync();
        Equal("D5", f.Keyboard.Keys.Last(), "new target skips the still-cooling main skill and releases its ready fallback");
        f.Confirm(202);
        await f.TickAsync();
        Equal(2, f.State.QuickbarSkills.AttackWeave.ConfirmedCount, "the new target builds its own opening/main pair");
        f.Clock.Advance(600);
        await f.TickAsync();
        Equal("C", f.Keyboard.Keys.Last(), "new-target C runs after the configured wait");
        Equal(1, f.Keyboard.Keys.Count(key => key == "C"), "only the completed new-target pair presses C");
        AssertLegacyWeaveUnused(f);
    }

    public static async Task ReleaseAllOpeningResumesAfterWeaveAsync()
    {
        using var f = new Fixture(true, 101, 102, 103);
        await f.PrepareAsync();
        await f.TickAsync();
        f.Confirm(101);
        await f.TickAsync();
        f.Confirm(102);
        await f.TickAsync();
        Sequence(new[] { "D1", "D2" }, f.Keyboard.Keys, "third opening waits for the first confirmed pair");
        Check(f.State.QuickbarSkills.AttackWeave.IsWaiting, "two opening releases schedule new-mode C");
        AssertLegacyWeaveUnused(f);
        f.Clock.Advance(599);
        await f.TickAsync();
        Sequence(new[] { "D1", "D2" }, f.Keyboard.Keys, "no opening retry or third opening runs before C is due");
        f.Clock.Advance(1);
        await f.TickAsync();
        Sequence(new[] { "D1", "D2", "C" }, f.Keyboard.Keys, "the due turn sends only C");
        f.Clock.Advance(30);
        await f.TickAsync();
        Sequence(new[] { "D1", "D2", "C", "D3" }, f.Keyboard.Keys, "ReleaseAll resumes its third opening without legacy CanPress reservations");
        f.Confirm(103);
        await f.TickAsync();
        Equal("D4", f.Keyboard.Keys.Last(), "normal new-mode skills follow the third opening");
        f.Confirm(201);
        await f.TickAsync();
        Equal(2, f.State.QuickbarSkills.AttackWeave.ConfirmedCount, "third opening and normal skill form the next shared pair");
        f.Clock.Advance(600);
        await f.TickAsync();
        Sequence(new[] { "D1", "D2", "C", "D3", "D4", "C" }, f.Keyboard.Keys, "opening-list handoff retains exact two-release accounting");
        AssertLegacyWeaveUnused(f);
    }

    public static async Task OpeningConfirmationEvidenceAsync()
    {
        using (var cooldown = new Fixture(true, 101))
        {
            await cooldown.PrepareAsync();
            await cooldown.TickAsync();
            await cooldown.TickAsync();
            Equal(0, cooldown.State.QuickbarSkills.AttackWeave.ConfirmedCount, "unchanged cooldown and key delivery never confirm an opening");
            cooldown.Confirm(101, publishRelease: false);
            await cooldown.TickAsync();
            Equal(1, cooldown.State.QuickbarSkills.AttackWeave.ConfirmedCount, "forward opening cooldown confirms exactly one actual release");
            await cooldown.TickAsync();
            Equal(1, cooldown.State.QuickbarSkills.AttackWeave.ConfirmedCount, "repeated opening cooldown snapshot is counted only once");
            AssertLegacyWeaveUnused(cooldown);
        }

        using (var precise = new Fixture(true, 101))
        {
            await precise.PrepareAsync();
            await precise.TickAsync();
            precise.Api.SkillAvailability = precise.Api.SkillAvailability with { LastReleasedSkillId = 101, LastReleasedSkillTime = 0 };
            await precise.TickAsync();
            Equal(0, precise.State.QuickbarSkills.AttackWeave.ConfirmedCount, "matching ID with unchanged release time does not confirm");
            precise.Api.SkillAvailability = precise.Api.SkillAvailability with { LastReleasedSkillId = 999, LastReleasedSkillTime = 1 };
            await precise.TickAsync();
            Equal(0, precise.State.QuickbarSkills.AttackWeave.ConfirmedCount, "another skill's official release cannot confirm the opening");
            precise.Api.SkillAvailability = precise.Api.SkillAvailability with { LastReleasedSkillId = 101, LastReleasedSkillTime = 2 };
            await precise.TickAsync();
            Equal(0, precise.State.QuickbarSkills.AttackWeave.ConfirmedCount, "exact opening ID and changed release time do not count without cooldown movement");
            await precise.TickAsync();
            Equal(0, precise.State.QuickbarSkills.AttackWeave.ConfirmedCount, "repeated actor release evidence without cooldown movement never counts");
            precise.Api.SkillAvailability = precise.Api.SkillAvailability with { LastReleasedSkillId = 999, LastReleasedSkillTime = 3 };
            precise.Confirm(101);
            await precise.TickAsync();
            Equal(1, precise.State.QuickbarSkills.AttackWeave.ConfirmedCount, "foreign actor release does not block a pressed opening skill's forward cooldown");
            Equal("D4", precise.Keyboard.Keys.Last(), "opening hands off while unrelated actor release evidence remains unchanged");
            precise.Confirm(201);
            await precise.TickAsync();
            Equal(2, precise.State.QuickbarSkills.AttackWeave.ConfirmedCount, "opening and main-tree cooldown advances count despite a stale foreign actor record");
            Check(precise.State.QuickbarSkills.AttackWeave.IsWaiting, "cooldown evidence schedules C independently of the original action confirmation");
            Check(precise.State.QuickbarSkills.PendingAction is not null,
                "original main action remains unconfirmed while independent cooldown counting already schedules C");
            AssertLegacyWeaveUnused(precise);
        }
    }

    public static async Task DisabledOpeningCompatibilityAsync()
    {
        using var explicitOff = new Fixture(false, 101, 102, 103);
        using var defaultOff = new Fixture(null, 101, 102, 103);
        await explicitOff.PrepareAsync();
        await defaultOff.PrepareAsync();
        foreach (var id in new uint[] { 101, 102, 103 })
        {
            var explicitDelay = await explicitOff.TickAsync();
            var defaultDelay = await defaultOff.TickAsync();
            Equal(defaultDelay, explicitDelay, "unchecked opening preserves the default tick delay");
            Sequence(defaultOff.Keyboard.Keys, explicitOff.Keyboard.Keys, "unchecked opening preserves default key order");
            Sequence(defaultOff.Observer.ReadTrace, explicitOff.Observer.ReadTrace, "unchecked opening preserves default snapshot read calls");
            explicitOff.Confirm(id);
            defaultOff.Confirm(id);
        }
        Sequence(new[] { "D1", "D2", "D3" }, explicitOff.Keyboard.Keys, "unchecked ReleaseAll does not pause or insert C");
        Equal(0, explicitOff.Observer.AvailabilityReads, "unchecked opening never adds an availability read for weave confirmation");
        await explicitOff.TickAsync();
        await defaultOff.TickAsync();
        Sequence(defaultOff.Keyboard.Keys, explicitOff.Keyboard.Keys, "unchecked main-tree handoff preserves default keys");
        Sequence(defaultOff.Observer.ReadTrace, explicitOff.Observer.ReadTrace, "unchecked handoff preserves default read count and arguments");
        Equal(0, explicitOff.State.QuickbarSkills.AttackWeave.ConfirmedCount, "unchecked mode has no weave accounting");
        Check(!explicitOff.State.QuickbarSkills.AttackWeave.IsWaiting, "unchecked mode schedules no wait");
        AssertLegacyWeaveUnused(explicitOff);
    }

    public static async Task OpeningWeaveLifecycleCancellationAsync()
    {
        foreach (var cause in new[] { "death", "maintenance" })
        {
            using var f = new Fixture(true, 101, 102, 103);
            f.Settings.Maintenance.StatusMaintenanceRules.Add(new()
            {
                SkillId = 901, SkillName = "maintenance", Key = "NumPad1", AbnormalStatusId = 900,
                RunTiming = MaintenanceRuleRunTiming.Always
            });
            f.Api.PlayerAbnormalStatuses = Status(f.Api, active: true);
            await f.PrepareAsync();
            await f.TickAsync();
            f.Confirm(101);
            await f.TickAsync();
            f.Confirm(102);
            await f.TickAsync();
            Check(f.State.QuickbarSkills.AttackWeave.IsWaiting, cause + " starts with a confirmed opening pair");
            if (cause == "death") f.Api.Player = f.Api.Player with { CurrentHp = 0 };
            else
            {
                f.Api.PlayerAbnormalStatuses = Status(f.Api, active: false);
                f.Keyboard.AfterPress = key =>
                {
                    if (key == "NumPad1") f.Api.PlayerAbnormalStatuses = Status(f.Api, active: true);
                };
            }
            f.Clock.Advance(600);
            await f.TickAsync();
            Check(!f.Keyboard.Keys.Contains("C"), cause + " cancels a due C before any attack input");
            Equal(0, f.State.QuickbarSkills.AttackWeave.ConfirmedCount, cause + " clears the opening pair");
            Check(!f.State.QuickbarSkills.AttackWeave.IsWaiting, cause + " clears the pending weave wait");
            if (cause == "maintenance") Check(f.Keyboard.Keys.Contains("NumPad1"), "existing maintenance runs before the canceled weave");
            AssertLegacyWeaveUnused(f);
        }
    }

    public static async Task OpeningAttackCDoesNotClearPairAsync()
    {
        using var f = new Fixture(true, 101);
        f.Settings.SemiAuto.AttackKeyLoopEnabled = true;
        await f.PrepareAsync();
        await f.TickAsync();
        f.Confirm(101);
        await f.TickAsync();
        Sequence(new[] { "D1", "C" }, f.Keyboard.Keys, "the shared opening is followed by its independent opening C");
        Equal(1, f.State.QuickbarSkills.AttackWeave.ConfirmedCount, "opening C preserves the opening skill's confirmed count");
        await f.TickAsync();
        Equal("D4", f.Keyboard.Keys.Last(), "normal skills follow the independent opening C");
        f.Confirm(201);
        await f.TickAsync();
        Check(f.State.QuickbarSkills.AttackWeave.IsWaiting, "opening and main skill still form a weave pair across opening C");
        Equal(1, f.Keyboard.Keys.Count(key => key == "C"), "weave C remains delayed after the opening C");
        f.Clock.Advance(600);
        await f.TickAsync();
        Sequence(new[] { "D1", "C", "D4", "C" }, f.Keyboard.Keys, "one opening C and one delayed weave C have independent lifecycles");
        AssertLegacyWeaveUnused(f);
    }

    public static async Task OpeningOnlyPlanStillWeavesAsync()
    {
        using var f = new Fixture(true, 101, 102, 103);
        f.Settings.QuickbarSkills.ExecutionTree.Clear();
        await f.PrepareAsync();
        await f.TickAsync();
        f.Confirm(101);
        await f.TickAsync();
        f.Confirm(102);
        await f.TickAsync();
        Check(f.State.QuickbarSkills.AttackWeave.IsWaiting, "empty main tree retains its confirmed opening pair");
        Sequence(new[] { "D1", "D2" }, f.Keyboard.Keys, "third opening waits even when no main actions exist");
        f.Clock.Advance(600);
        await f.TickAsync();
        f.Clock.Advance(30);
        await f.TickAsync();
        Sequence(new[] { "D1", "D2", "C", "D3" }, f.Keyboard.Keys, "empty main tree still executes delayed C and resumes the third opening");
        AssertLegacyWeaveUnused(f);
    }

    public static async Task DisablePendingWaitResumesSameTickAsync()
    {
        foreach (var path in new[] { "opening", "main" })
        {
            using var f = path == "opening" ? new Fixture(true, 101, 102, 103) : new Fixture(true, 101);
            if (path == "main") f.Settings.QuickbarSkills.ExecutionTree.Add(Node(202));
            await f.PrepareAsync();
            await f.TickAsync();
            f.Confirm(101);
            await f.TickAsync();
            f.Confirm(path == "opening" ? 102u : 201u);
            await f.TickAsync();
            Check(f.State.QuickbarSkills.AttackWeave.IsWaiting, path + " has a pending C before disabling");
            f.Settings.SemiAuto.AttackWeaveEnabled = false;
            f.Clock.Advance(600);
            await f.TickAsync();
            Equal(path == "opening" ? "D3" : "D5", f.Keyboard.Keys.Last(), "disabling resumes the upper " + path + " action in that same tick");
            Equal(0, f.State.QuickbarSkills.AttackWeave.ConfirmedCount, "disabling clears the pending pair");
            Check(!f.State.QuickbarSkills.AttackWeave.IsWaiting && !f.Keyboard.Keys.Contains("C"), "disabling cancels the due C without an extra wait turn");
            AssertLegacyWeaveUnused(f);
        }
    }

    public static async Task StationaryOpeningLoopSharesPairAndHandoffAsync()
    {
        using var f = new Fixture(true, 101, 102, 103);
        f.Settings.SemiAuto.AttackKeyLoopEnabled = true;
        await f.PrepareAsync();
        await f.TickOpeningLoopAsync();
        f.Confirm(101);
        await f.TickOpeningLoopAsync();
        f.Confirm(102);
        await f.TickOpeningLoopAsync();
        Sequence(new[] { "D1", "D2" }, f.Keyboard.Keys, "stationary opening-loop boundary shares the new-mode pair");
        Check(f.State.QuickbarSkills.AttackWeave.IsWaiting, "stationary loop waits after two confirmed openings");
        AssertLegacyWeaveUnused(f);
        f.Clock.Advance(600);
        await f.TickOpeningLoopAsync();
        f.Clock.Advance(30);
        await f.TickOpeningLoopAsync();
        Sequence(new[] { "D1", "D2", "C", "D3" }, f.Keyboard.Keys, "stationary loop completes C before continuing the third opening");
        f.Confirm(103);
        await f.TickOpeningLoopAsync();
        Equal(1, f.State.QuickbarSkills.AttackWeave.ConfirmedCount, "stationary opening C preserves the third opening's success");
        await f.TickAsync();
        Equal("D4", f.Keyboard.Keys.Last(), "combat handoff resumes the new main tree");
        Equal(1, f.State.QuickbarSkills.AttackWeave.ConfirmedCount, "combat handoff retains the stationary opening's confirmed count");
        f.Confirm(201);
        await f.TickAsync();
        Check(f.State.QuickbarSkills.AttackWeave.IsWaiting, "stationary opening and normal combat jointly form the next pair");
        f.Clock.Advance(600);
        await f.TickAsync();
        Sequence(new[] { "D1", "D2", "C", "D3", "C", "D4", "C" }, f.Keyboard.Keys, "stationary handoff preserves opening C and both weave pairs");
        AssertLegacyWeaveUnused(f);
    }

    public static async Task OpeningFailedInputAndZeroDurationDoNotCountAsync()
    {
        using (var failed = new Fixture(true, 101, 102, 103))
        {
            failed.Keyboard.PressResult = key => key == "D1" ? OperationResult.Fail("opening transport failed") : OperationResult.Ok();
            await failed.PrepareAsync();
            await failed.TickAsync();
            Sequence(new[] { "D1", "D2" }, failed.Keyboard.Keys, "failed first opening advances to a successfully delivered fallback");
            failed.Confirm(101);
            await failed.TickAsync();
            Equal(0, failed.State.QuickbarSkills.AttackWeave.ConfirmedCount, "cooldown movement after a failed key cannot count an opening");
            failed.Confirm(102);
            await failed.TickAsync();
            Equal(1, failed.State.QuickbarSkills.AttackWeave.ConfirmedCount, "the successfully delivered fallback counts from its own cooldown");
            failed.Confirm(103);
            await failed.TickAsync();
            Equal(2, failed.State.QuickbarSkills.AttackWeave.ConfirmedCount, "the next successful opening completes the pair without the failed attempt");
            Check(failed.State.QuickbarSkills.AttackWeave.IsWaiting, "two successful opening cooldown changes schedule C");
            AssertLegacyWeaveUnused(failed);
        }

        using (var zero = new Fixture(true, 101))
        {
            zero.Api.Skills = zero.Api.Skills.Select(skill => skill.SkillId == 101 ? skill with { CooldownDuration = 0 } : skill).ToArray();
            await zero.PrepareAsync();
            await zero.TickAsync();
            zero.Confirm(101);
            await zero.TickAsync();
            Equal(0, zero.State.QuickbarSkills.AttackWeave.ConfirmedCount, "zero-duration opening does not count even when its raw end value changes");
            zero.Confirm(201);
            await zero.TickAsync();
            Equal(1, zero.State.QuickbarSkills.AttackWeave.ConfirmedCount, "only the main skill with a real cooldown contributes to the pair");
            Check(!zero.State.QuickbarSkills.AttackWeave.IsWaiting, "zero-duration opening cannot complete a pair with one main release");
            AssertLegacyWeaveUnused(zero);
        }
    }

    public static async Task PendingOpeningCooldownDoesNotSurviveResetAsync()
    {
        foreach (var cause in new[] { "death", "maintenance", "disabled", "target_lost" })
        {
            using var f = new Fixture(true, 101);
            f.Settings.Maintenance.StatusMaintenanceRules.Add(new()
            {
                SkillId = 901, SkillName = "maintenance", Key = "NumPad1", AbnormalStatusId = 900,
                RunTiming = MaintenanceRuleRunTiming.Always
            });
            f.Api.PlayerAbnormalStatuses = Status(f.Api, active: true);
            await f.PrepareAsync();
            await f.TickAsync();
            Equal("D1", f.Keyboard.Keys.Last(), cause + " starts with a delivered but unconfirmed opening");
            Equal(0, f.State.QuickbarSkills.AttackWeave.ConfirmedCount, "key delivery alone reserves no confirmed count");
            if (cause == "death") f.Api.Player = f.Api.Player with { CurrentHp = 0 };
            else if (cause == "maintenance")
            {
                f.Api.PlayerAbnormalStatuses = Status(f.Api, active: false);
                f.Keyboard.AfterPress = key =>
                {
                    if (key == "NumPad1") f.Api.PlayerAbnormalStatuses = Status(f.Api, active: true);
                };
            }
            else if (cause == "disabled") f.Settings.SemiAuto.AttackWeaveEnabled = false;
            else f.Api.TargetEntityId = 0;
            await f.TickAsync();
            f.Confirm(101);
            await f.TickAsync();
            Equal(0, f.State.QuickbarSkills.AttackWeave.ConfirmedCount, cause + " discards the old opening attempt before its late cooldown appears");
            Check(!f.State.QuickbarSkills.AttackWeave.IsWaiting && !f.Keyboard.Keys.Contains("C"), cause + " leaves no phantom pair or delayed C");
            AssertLegacyWeaveUnused(f);
        }
    }

    private static PlayerAbnormalStatusSnapshot Status(FakeGameApi api, bool active) => new(api.Player.EntityId,
        DateTimeOffset.Now, 0, active
            ? new[] { new AbnormalStatusEntrySnapshot(0, 900, PlayerAbnormalStatusSnapshot.BuffCategory, 0, 1, 0) }
            : Array.Empty<AbnormalStatusEntrySnapshot>());

    private static void AssertLegacyWeaveUnused(Fixture f)
    {
        Check(!f.State.AttackWeave.HasAttempts, "new-mode opening never reserves legacy weave slots");
        Equal(0, f.State.AttackWeave.ConfirmedCount, "legacy weave never counts new-mode opening releases");
    }

    private static SkillConfigNode Node(uint id) => new() { SkillId = id, Name = "skill" + id, BaseName = "skill" + id };

    private sealed class Fixture : IDisposable
    {
        public FakeGameApi Api { get; } = new();
        public RecordingKeyboardInput Keyboard { get; } = new();
        public SemiAutoCombatState State { get; } = new();
        public TestClock Clock { get; } = new();
        public AccountWorkerContext Context { get; }
        public SemiAutoCombatController Controller { get; }
        public ScriptSettings Settings => Context.Config.ScriptSettings!;
        public ReadObserver Observer => _factory.Observer!;
        private readonly ObservingFactory _factory;
        private readonly CancellationTokenSource _stop = new(TimeSpan.FromSeconds(15));
        private SemiAutoSkillPlan _plan = null!;
        private uint _releaseTime;

        public Fixture(bool? weaveEnabled, params uint[] openingIds)
        {
            var settings = new ScriptSettings { SkillTreeReleaseMode = SkillTreeReleaseMode.QuickbarAvailability };
            if (weaveEnabled.HasValue) settings.SemiAuto.AttackWeaveEnabled = weaveEnabled.Value;
            settings.SemiAuto.AttackWeaveDelayMs = 600;
            settings.SemiAuto.AttackKeyLoopEnabled = false;
            settings.Maintenance.SitMaintenanceEnabled = false;
            settings.Skills.OpeningSkill = new()
            {
                Enabled = true, ReleaseAll = true,
                Skills = openingIds.Select(id => new OpeningSkillEntryConfig { SkillId = id, SkillName = "skill" + id }).ToList()
            };
            settings.QuickbarSkills.ExecutionTree.Add(new() { SkillId = 201, Name = "skill201", BaseName = "skill201" });
            Api.Skills = new uint[] { 101, 102, 103, 201, 202, 901 }.Select(id =>
                new SkillSnapshot(id, "skill" + id, 1, 1, "skill" + id, 1, false, 10000, 0)).ToArray();
            Api.Quickbar = new(0, new QuickbarSlotSnapshot[]
            {
                new(SkillQuickbar.Main, 0, 21, 101), new(SkillQuickbar.Main, 1, 21, 102),
                new(SkillQuickbar.Main, 2, 21, 103), new(SkillQuickbar.Main, 3, 21, 201),
                new(SkillQuickbar.Main, 4, 21, 202),
                new(SkillQuickbar.Alt, 0, 21, 901)
            });
            Api.SkillAvailability = new(0, Api.Quickbar.Slots.Select(slot =>
                new SkillAvailabilitySlotSnapshot(slot.Bar, slot.Slot, 21, slot.SkillId, slot.SkillId, true)).ToArray(),
                BindingSignature: "opening-weave-layout");
            _factory = new(Api);
            var logger = new InMemoryRoadhogLogger();
            Context = new(new AccountConfig { AccountName = "quickbar-opening-weave", ScriptSettings = settings },
                _factory, logger, new AccountRuntimeManager(logger), new(), _stop.Token);
            Controller = new(Keyboard, timeProvider: Clock);
        }

        public async Task PrepareAsync()
        {
            await Context.PrepareSkillBindingsAsync();
            _plan = SemiAutoSkillPlan.FromSettings(Settings.Skills, Context.SkillBindings);
            Observer.ResetCounts();
        }

        public Task<TimeSpan> TickAsync() => Controller.TickAsync(Context, _plan, State);

        public async Task<TimeSpan> TickOpeningLoopAsync() => await Controller.TickOpeningAttackKeyLoopAsync(Context,
            _plan, State, (await Context.Snapshots.ReadLockedTargetAsync()).Value);

        public void Confirm(uint id, bool publishRelease = false)
        {
            Api.Skills = Api.Skills.Select(skill => skill.SkillId == id
                ? skill with { CooldownEndTime = unchecked((uint)Environment.TickCount64 + 10000u) }
                : skill).ToArray();
            if (publishRelease) Api.SkillAvailability = Api.SkillAvailability with
            {
                LastReleasedSkillId = id, LastReleasedSkillTime = ++_releaseTime
            };
        }

        public void Dispose() => _stop.Dispose();
    }

    private sealed class TestClock : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UtcNow;
        private long _milliseconds;
        public override DateTimeOffset GetUtcNow() => _now;
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() => _milliseconds;
        public void Advance(int milliseconds) { _now = _now.AddMilliseconds(milliseconds); _milliseconds += milliseconds; }
    }

    public interface IObservedSnapshots : IRoadhogSnapshotReader, ISkillAvailabilitySnapshotReader { }

    public class ReadObserver : DispatchProxy
    {
        public IRoadhogSnapshotReader Inner { get; set; } = null!;
        public List<string> ReadTrace { get; } = new();
        public int AvailabilityReads { get; private set; }
        public void ResetCounts() { ReadTrace.Clear(); AvailabilityReads = 0; }
        protected override object? Invoke(MethodInfo? method, object?[]? arguments)
        {
            var signature = method!.Name;
            if (method.Name == nameof(IRoadhogSnapshotReader.ReadSkillsAsync) && arguments![0] is IReadOnlyCollection<uint> ids)
                signature += ":" + string.Join(",", ids.Order());
            ReadTrace.Add(signature);
            if (method.Name == nameof(ISkillAvailabilitySnapshotReader.ReadSkillAvailabilityAsync)) AvailabilityReads++;
            return method.Invoke(Inner, arguments);
        }
    }

    private sealed class ObservingFactory(FakeGameApi api) : IRoadhogSnapshotReaderFactory
    {
        public ReadObserver? Observer { get; private set; }
        public IRoadhogSnapshotReader Create(AccountConfig config, IRoadhogLogger logger, CancellationToken cancellationToken = default)
        {
            var proxy = DispatchProxy.Create<IObservedSnapshots, ReadObserver>();
            Observer = (ReadObserver)proxy;
            Observer.Inner = new RoadhogSnapshotReaderFactory(api).Create(config, logger, cancellationToken);
            return proxy;
        }
    }

    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void Equal<T>(T expected, T actual, string message) => Check(EqualityComparer<T>.Default.Equals(expected, actual),
        message + $": expected {expected}, got {actual}");
    private static void Sequence(IEnumerable<string> expected, IEnumerable<string> actual, string message) => Check(expected.SequenceEqual(actual),
        message + $": expected [{string.Join(",", expected)}], got [{string.Join(",", actual)}]");
}
