using System.Reflection;
using Roadhog.Application;
using Roadhog.Application.SemiAuto;
using Roadhog.Application.Workers;
using Roadhog.Core.Accounts;
using Roadhog.Core.Api;
using Roadhog.Core.Diagnostics;
using Roadhog.Core.Model;
using Roadhog.Infrastructure.Vmm;

// Full editor snapshots deliberately expose only the highest learned rank.
// Exact official snapshots retain the actual ranks placed on the skill bar.
internal sealed class SharedSkillAuditFixture : IDisposable
{
    public const uint AttackId = 200;
    public const uint LowHpId = 1001;
    public const uint HighHpId = 1003;
    public const uint LowMpId = 1011;
    public const uint HighMpId = 1013;
    public FakeGameApi Api { get; } = new();
    public RecordingKeyboardInput Keyboard { get; } = new();
    public InMemoryRoadhogLogger Logger { get; } = new();
    public SemiAutoCombatState State { get; } = new();
    public TestClock Clock { get; } = new();
    public List<TimeSpan> PetBuffDelays { get; } = new();
    public Action<int>? BeforePetBuffDelay { get; set; }
    public AccountWorkerContext Context { get; }
    public SemiAutoCombatController Controller { get; }
    public ScriptSettings Settings => Context.Config.ScriptSettings!;
    public SkillObserver Observer => factory.Observer!;
    public SemiAutoSkillPlan Plan { get; private set; } = null!;
    private readonly CancellationTokenSource stop = new(TimeSpan.FromSeconds(15));
    private readonly ObservingFactory factory;

    public SharedSkillAuditFixture()
    {
        var settings = new ScriptSettings { SkillTreeReleaseMode = SkillTreeReleaseMode.QuickbarAvailability };
        settings.Skills.SpiritmasterAutoSkillLogicEnabled = true;
        settings.Skills.OpeningSkill.Enabled = false;
        settings.SemiAuto.AttackKeyLoopEnabled = false;
        settings.SemiAuto.AttackWeaveEnabled = false;
        settings.Maintenance.SitMaintenanceEnabled = false;
        settings.QuickbarSkills.ExecutionTree.Add(new() { SkillId = AttackId, Name = "attack", BaseName = "attack" });
        Api.Player = Api.Player with { CharacterClassId = AionClassId.Spiritmaster, CurrentDp = 4000 };
        Api.TargetOwnServerObjectId = 1000;
        Api.Skills = new[]
        {
            Skill(AttackId, "attack", 1) with { XmlActivation = "Active", XmlSkillType = "Magical",
                XmlSubType = "Attack", XmlTargetRelationRestriction = "Enemy", XmlEffects = "SpellATK_Instant" },
            Skill(LowHpId, "heal", 1), Skill(HighHpId, "heal", 3),
            Skill(LowMpId, "mana", 1), Skill(HighMpId, "mana", 3),
            Skill(1662, "pet armor", 1, 30000), Skill(1787, "pet shield", 1, 30000)
        };
        Api.Quickbar = new(0, new QuickbarSlotSnapshot[]
        {
            new(SkillQuickbar.Main, 0, 21, AttackId),
            new(SkillQuickbar.Main, 1, 21, LowHpId), new(SkillQuickbar.Main, 2, 21, LowMpId),
            new(SkillQuickbar.Alt, 0, 21, 1662), new(SkillQuickbar.Alt, 1, 21, 1787)
        });
        Api.SkillAvailability = new(0, Array.Empty<SkillAvailabilitySlotSnapshot>(),
            UnsupportedSkillIds: new[] { AttackId }, BindingSignature: "shared-skill-audit",
            BindingSlots: Api.Quickbar.Slots.Select(slot =>
                new SkillAvailabilityBindingSnapshot(slot.Bar, slot.Slot, 21, slot.SkillId, slot.SkillId)).ToArray());
        SetPet(11, 50);
        factory = new(Api);
        Context = new(new AccountConfig { AccountName = "shared-skill-audit", ScriptSettings = settings },
            factory, Logger, new AccountRuntimeManager(Logger), new(), stop.Token);
        Controller = new(Keyboard, timeProvider: Clock, petBuffDelay: (delay, token) =>
        {
            token.ThrowIfCancellationRequested();
            PetBuffDelays.Add(delay);
            BeforePetBuffDelay?.Invoke(PetBuffDelays.Count);
            token.ThrowIfCancellationRequested();
            Clock.Advance((int)delay.TotalMilliseconds);
            return Task.CompletedTask;
        });
        var marker = Skill(900001, "clock evidence", 1, 1000);
        var osTick = unchecked((uint)Environment.TickCount64);
        State.MarkSkillPressed(marker, DateTimeOffset.Now.AddSeconds(1));
        Check(State.TryUpdateCooldownTickCalibration(new[] { marker with { CooldownEndTime = unchecked(osTick + 1000) } },
            osTick, DateTimeOffset.Now, out _), "fixture calibrates only from observed forward cooldown");
    }

    public async Task PrepareAsync()
    {
        await Context.PrepareSkillBindingsAsync();
        Plan = SemiAutoSkillPlan.FromSharedSettings(Settings.Skills, Context.SkillBindings);
        Observer.Requests.Clear();
    }
    public Task<TimeSpan> TickAsync() => Controller.TickAsync(Context, Plan, State);
    public Task<bool> MaintainAsync() => Controller.TryHandleMaintenanceAsync(Context, State, Api.Player,
        allowSitMaintenance: false, plan: Plan);
    public void Cancel() => stop.Cancel();
    public void ClearAttackTree() => Settings.QuickbarSkills.ExecutionTree.Clear();
    public void Confirm(uint id) => Api.Skills = Api.Skills.Select(skill => skill.SkillId == id
        ? skill with { CooldownEndTime = unchecked((uint)Environment.TickCount64 + skill.CooldownDuration) } : skill).ToArray();
    public void AddPetBuff(uint id = 1662) => Settings.Skills.Spiritmaster.PetBuffRules.Add(new()
        { SkillId = id, SkillName = Api.Skills.Single(skill => skill.SkillId == id).Name, AbnormalStatusId = id });
    public void ConfigureResource(string resource)
    {
        if (resource == "hp")
        {
            Settings.Maintenance.HpMaintenanceRules.Add(new() { SkillId = LowHpId, SkillName = "heal I", Key = "D2", BelowPercent = 80 });
            Api.Player = Api.Player with { CurrentHp = 50 };
        }
        else
        {
            Settings.Maintenance.MpMaintenanceRules.Add(new() { SkillId = LowMpId, SkillName = "mana I", Key = "D3", BelowPercent = 80 });
            Api.Player = Api.Player with { CurrentMp = 50 };
        }
    }
    public void SetPet(uint serverId, uint hp, params uint[] abnormalIds)
    {
        var roster = SummonedPetRosterSnapshot.Empty(10, DateTimeOffset.Now);
        var pet = roster.LocalPlayerPet.Pet with
        {
            IsSummoned = true, ServerObjectId = serverId, LocalLinkedPetServerObjectId = serverId,
            CurrentHp = hp, MaxHp = 100, HpPercent = (byte)hp, OwnerConfirmed = true,
            EvidenceSource = "fixture", CapturedAt = DateTimeOffset.Now,
            HealthFields = new(true, true, true)
        };
        Api.SummonedPet = pet;
        Api.SummonedPetRoster = roster with
        {
            LocalLinkedPetServerObjectId = serverId,
            LocalPlayerPet = roster.LocalPlayerPet with { Pet = pet, AbnormalStatuses = abnormalIds.Select(Buff).ToArray() }
        };
    }
    public void AddElementalRule(uint id)
    {
        Api.Skills = Api.Skills.Append(Skill(id, "元素补充", 1)).ToArray();
        Api.Quickbar = Api.Quickbar with { Slots = Api.Quickbar.Slots.Append(new(SkillQuickbar.Main, 3, 21, id)).ToArray() };
        Settings.Skills.Spiritmaster.PetHpMaintenanceRules.Add(new()
            { SkillId = id, SkillName = "元素补充 I", BelowPercent = 75, CooldownMs = 10300 });
    }
    public static SkillSnapshot Skill(uint id, string name, int tier, uint duration = 10000) =>
        new(id, name + (tier == 1 ? " I" : " III"), tier, tier, name, tier, false, duration, 0);
    public static AbnormalStatusEntrySnapshot Buff(uint id) => new(0, id, PlayerAbnormalStatusSnapshot.BuffCategory, 0, 1, 0);
    public static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    public static void Sequence(IEnumerable<string> expected, IEnumerable<string> actual, string message) =>
        Check(expected.SequenceEqual(actual), message + $": expected [{string.Join(",", expected)}], got [{string.Join(",", actual)}]");
    public bool Logged(string eventName, string field, uint id) => Logger.Entries.Any(entry =>
        entry.EventName == eventName && entry.Fields.TryGetValue(field, out var value) && value is uint actual && actual == id);
    public void Dispose() => stop.Dispose();

    public sealed class TestClock : TimeProvider
    {
        private DateTimeOffset now = DateTimeOffset.UtcNow;
        private long milliseconds;
        public override DateTimeOffset GetUtcNow() => now;
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() => milliseconds;
        public void Advance(int durationMs) { milliseconds += durationMs; now = now.AddMilliseconds(durationMs); }
        public void ShiftUtc(TimeSpan delta) => now += delta;
    }
    public interface IObservedSnapshots : IRoadhogSnapshotReader, ISkillAvailabilitySnapshotReader { }
    public class SkillObserver : DispatchProxy
    {
        public IRoadhogSnapshotReader Inner { get; set; } = null!;
        public FakeGameApi Api { get; set; } = null!;
        public CancellationToken Stop { get; set; }
        public List<uint[]?> Requests { get; } = new();
        public Action<int>? BeforePetRead { get; set; }
        public Action<int>? BeforePetHealthRead { get; set; }
        private int petReads;
        private int petHealthReads;
        private long version;
        protected override object? Invoke(MethodInfo? method, object?[]? arguments)
        {
            Stop.ThrowIfCancellationRequested();
            if (method!.Name == nameof(IRoadhogSnapshotReader.ReadSummonedPetRosterAsync))
            {
                petReads++;
                BeforePetRead?.Invoke(petReads);
            }
            if (method.Name == nameof(IRoadhogSnapshotReader.ReadSummonedPetAsync))
            {
                petHealthReads++;
                BeforePetHealthRead?.Invoke(petHealthReads);
            }
            if (method.Name != nameof(IRoadhogSnapshotReader.ReadSkillsAsync)) return method.Invoke(Inner, arguments);
            var ids = arguments![0] as IReadOnlyCollection<uint>;
            Requests.Add(ids?.ToArray());
            IReadOnlyList<SkillSnapshot> value = ids is null
                ? Api.Skills.GroupBy(skill => skill.DisplayBaseName).Select(group => group.OrderByDescending(skill => skill.DisplayTier).First()).ToArray()
                : Api.Skills.Where(skill => ids.Contains(skill.SkillId)).ToArray();
            return Task.FromResult(new PublishedGameSnapshot<IReadOnlyList<SkillSnapshot>>(++version, value));
        }
    }
    private sealed class ObservingFactory(FakeGameApi api) : IRoadhogSnapshotReaderFactory
    {
        public SkillObserver? Observer { get; private set; }
        public IRoadhogSnapshotReader Create(AccountConfig config, IRoadhogLogger logger, CancellationToken cancellationToken = default)
        {
            var proxy = DispatchProxy.Create<IObservedSnapshots, SkillObserver>();
            Observer = (SkillObserver)proxy;
            Observer.Inner = new RoadhogSnapshotReaderFactory(api).Create(config, logger, cancellationToken);
            Observer.Api = api;
            Observer.Stop = cancellationToken;
            return proxy;
        }
    }
}
