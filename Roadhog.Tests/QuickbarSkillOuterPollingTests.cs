using System.Reflection;
using Roadhog.Application;
using Roadhog.Application.SemiAuto;
using Roadhog.Application.Workers;
using Roadhog.Core.Accounts;
using Roadhog.Core.Api;
using Roadhog.Core.Diagnostics;
using Roadhog.Core.Model;
using Roadhog.Infrastructure.Vmm;

internal static class QuickbarSkillOuterPollingTests
{
    public static async Task MaintenanceCadenceAsync()
    {
        using var fixture = new Fixture();
        await fixture.PrepareAsync();

        await fixture.Controller.TickAsync(fixture.Context, fixture.MaintenancePlan, fixture.State);

        Check(fixture.Observer.AvailabilityReads >= 2, "one bounded outer tick performs several lightweight availability polls");
        Check(fixture.Observer.AbnormalReads <= 2 && fixture.Observer.AbnormalReads < fixture.Observer.AvailabilityReads,
            "maintenance status checks do not repeat for every fast availability poll");
        Check(fixture.Observer.FullSkillReads == 0, "the fast combat path never rereads the full learned skill table");
        Check(fixture.Observer.SkillIdReads.All(ids => !ids.Contains(999u)), "narrow reads omit unconfigured learned skills");
        Check(fixture.Keyboard.Keys.IsEmpty, "an already active maintenance buff and dark attack skill send no keys");
    }

    public static Task DeathGuardAsync() => GuardStopsAsync(dying: true);

    public static Task TargetGuardAsync() => GuardStopsAsync(dying: false);

    public static async Task HpThresholdYieldAsync()
    {
        using var fixture = new Fixture();
        fixture.Context.Config.ScriptSettings!.Maintenance.HpMaintenanceRules.Add(new()
        {
            ActionType = MaintenanceRuleActionType.Skill, SkillId = 901, SkillName = "heal", Key = "D4",
            BelowPercent = 40, RunTiming = MaintenanceRuleRunTiming.Always
        });
        fixture.Api.Skills = fixture.Api.Skills.Append(new SkillSnapshot(901, "heal", 1, 1, "heal", 1, false, 30000, 0)).ToArray();
        fixture.Api.Quickbar = fixture.Api.Quickbar with
        {
            Slots = fixture.Api.Quickbar.Slots.Append(new QuickbarSlotSnapshot(SkillQuickbar.Main, 3, 21, 901)).ToArray()
        };
        fixture.Api.SkillAvailabilityRead = () => fixture.Availability(canUse: fixture.Api.SkillAvailabilityReadCount >= 2) with
        {
            CombatState = fixture.CombatState with { CurrentHp = fixture.Api.SkillAvailabilityReadCount >= 2 ? 40u : 100u }
        };
        await fixture.PrepareAsync();

        await fixture.Controller.TickAsync(fixture.Context, fixture.MaintenancePlan, fixture.State);

        Check(fixture.Observer.AvailabilityReads >= 2 && fixture.Observer.AvailabilityReads <= 4,
            "a due HP rule at its inclusive threshold promptly yields the bounded fast segment");
        Check(fixture.Keyboard.Keys.IsEmpty && fixture.State.QuickbarSkills.PendingAction is null,
            "a lit attack opportunity yields to a due HP maintenance rule without sending either key concurrently");

        // The next outer turn sees the newly published full player state and uses the existing maintenance implementation.
        fixture.Api.Player = fixture.Api.Player with { CurrentHp = 40 };
        fixture.Keyboard.AfterPress = key =>
        {
            if (key == "D4")
            {
                fixture.Api.Player = fixture.Api.Player with { CurrentHp = 100 };
                fixture.Api.Skills = fixture.Api.Skills.Select(skill => skill.SkillId == 901
                    ? skill with { CooldownEndTime = unchecked((uint)Environment.TickCount64 + 30000u) }
                    : skill).ToArray();
            }
        };
        await fixture.Controller.TickAsync(fixture.Context, fixture.MaintenancePlan, fixture.State);

        Check(fixture.Keyboard.Keys.Count > 0 && fixture.Keyboard.Keys.All(key => key == "D4"),
            "the next serial outer turn executes the unchanged HP maintenance key and no attack key");
    }

    public static async Task CoolingHpMaintenanceDoesNotStarveAsync()
    {
        using var fixture = new Fixture();
        var osTick = unchecked((uint)Environment.TickCount64);
        fixture.Context.Config.ScriptSettings!.Maintenance.HpMaintenanceRules.Add(new()
        {
            ActionType = MaintenanceRuleActionType.Skill, SkillId = 901, SkillName = "heal", Key = "D4",
            BelowPercent = 40, RunTiming = MaintenanceRuleRunTiming.Always
        });
        fixture.Api.Player = fixture.Api.Player with { CurrentHp = 40 };
        fixture.Api.Skills = fixture.Api.Skills.Append(new SkillSnapshot(901, "heal", 1, 1, "heal", 1, false,
            30000, unchecked(osTick + 30000u))).ToArray();
        fixture.Api.Quickbar = fixture.Api.Quickbar with
        {
            Slots = fixture.Api.Quickbar.Slots.Append(new QuickbarSlotSnapshot(SkillQuickbar.Main, 3, 21, 901)).ToArray()
        };
        var attackReleased = false;
        fixture.Api.SkillAvailabilityRead = () => fixture.Availability(canUse: !attackReleased) with
        {
            LastReleasedSkillId = attackReleased ? 21u : 0u,
            LastReleasedSkillTime = attackReleased ? 1u : 0u,
            CombatState = fixture.CombatState with { CurrentHp = 40 }
        };
        fixture.Keyboard.AfterPress = key =>
        {
            if (key == "D2")
            {
                attackReleased = true;
                fixture.Api.Skills = fixture.Api.Skills.Select(skill => skill.SkillId == 21
                    ? skill with { CooldownEndTime = unchecked((uint)Environment.TickCount64 + 30000u) }
                    : skill).ToArray();
            }
        };
        var marker = new SkillSnapshot(900001, "clock marker", 1, 1, "clock marker", 1, false, 1000, 0);
        fixture.State.MarkSkillPressed(marker, DateTimeOffset.Now.AddSeconds(1));
        Check(fixture.State.TryUpdateCooldownTickCalibration(new[] { marker with { CooldownEndTime = unchecked(osTick + 1000u) } },
            osTick, DateTimeOffset.Now, out _), "test begins with a previously confirmed game cooldown clock");
        await fixture.PrepareAsync();

        await fixture.Controller.TickAsync(fixture.Context, fixture.MaintenancePlan, fixture.State);

        Check(fixture.Observer.AvailabilityReads >= 3 && fixture.Keyboard.Keys.Count == 1 && fixture.Keyboard.Keys.Single() == "D2",
            "a due HP key whose actual skill remains cooling does not repeatedly yield or starve a lit attack opportunity");
        Check(fixture.State.QuickbarSkills.PendingAction is null, "the accepted attack is confirmed while cooling maintenance remains skipped");
    }

    public static async Task ColdClockStartsExpiredOrdinarySkillAsync()
    {
        using var fixture = new Fixture();
        fixture.Context.Config.ScriptSettings!.QuickbarSkills.ExecutionTree.Insert(0,
            new() { SkillId = 31, Name = "future cooldown", BaseName = "future cooldown" });
        fixture.Api.Skills = fixture.Api.Skills.Select(skill => skill.SkillId == 21
            ? skill with { Name = "ordinary", DisplayBaseName = "ordinary", XmlCounterSkill = null, CooldownEndTime = 300000 }
            : skill).Append(new SkillSnapshot(31, "future cooldown", 1, 1, "future cooldown", 1, false, 30000, 380000)).ToArray();
        fixture.Api.Quickbar = fixture.Api.Quickbar with
        {
            Slots = fixture.Api.Quickbar.Slots.Append(new QuickbarSlotSnapshot(SkillQuickbar.Main, 3, 21, 31)).ToArray()
        };
        var released = false;
        fixture.Api.SkillAvailabilityRead = () => fixture.Availability(canUse: false) with
        {
            Slots = Array.Empty<SkillAvailabilitySlotSnapshot>(),
            UnsupportedSkillIds = new uint[] { 21, 31, 900 },
            LastReleasedSkillId = released ? 21u : 0u,
            LastReleasedSkillTime = released ? 370000u : 310000u
        };
        fixture.Keyboard.AfterPress = key =>
        {
            if (key == "D2")
            {
                released = true;
                fixture.Api.Skills = fixture.Api.Skills.Select(skill => skill.SkillId == 21
                    ? skill with { CooldownEndTime = 400000 }
                    : skill).ToArray();
            }
        };
        await fixture.PrepareAsync();
        Check(!fixture.State.HasCooldownTickCalibration, "the new skill loop starts without a calibrated game clock");
        var elapsed = System.Diagnostics.Stopwatch.StartNew();

        await fixture.Controller.TickAsync(fixture.Context, fixture.MaintenancePlan, fixture.State);

        Check(keyboardKeysAreCorrect(),
            "the actor release clock proves ordinary 21 already expired; higher-priority future 31 is never pressed");
        Check(fixture.State.HasCooldownTickCalibration,
            "the first accepted ordinary skill advances its cooldown and calibrates the existing shared game clock");
        Check(fixture.State.QuickbarSkills.PendingAction is null && elapsed.Elapsed < TimeSpan.FromSeconds(2),
            "the cold-clock startup confirms its finite action and returns within a bounded outer tick");

        bool keyboardKeysAreCorrect() => fixture.Keyboard.Keys.Count == 1 && fixture.Keyboard.Keys.Single() == "D2";
    }

    private static async Task GuardStopsAsync(bool dying)
    {
        using var fixture = new Fixture();
        fixture.Api.SkillAvailabilityRead = () =>
        {
            var changed = fixture.Api.SkillAvailabilityReadCount >= 2;
            return fixture.Availability(canUse: changed) with
            {
                CombatState = fixture.CombatState with
                {
                    CurrentHp = changed && dying ? 0u : 100u,
                    TargetServerObjectId = changed && !dying ? 9999u : 1000u
                }
            };
        };
        await fixture.PrepareAsync();

        await fixture.Controller.TickAsync(fixture.Context, fixture.MaintenancePlan, fixture.State);

        Check(fixture.Observer.AvailabilityReads >= 2 && fixture.Observer.AvailabilityReads <= 4,
            "a lightweight life or target change promptly ends the bounded fast segment");
        Check(fixture.Keyboard.Keys.IsEmpty,
            dying ? "a lit icon cannot attack after the official role life guard becomes dead"
                : "a lit icon cannot attack another server object using the previous locked-target scope");
        Check(fixture.State.QuickbarSkills.PendingAction is null, "interrupted polling leaves no outstanding attack attempt");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class Fixture : IDisposable
    {
        public FakeGameApi Api { get; } = new();
        public RecordingKeyboardInput Keyboard { get; } = new();
        public SemiAutoCombatState State { get; } = new();
        public SemiAutoCombatController Controller { get; }
        public AccountWorkerContext Context { get; }
        public SemiAutoSkillPlan MaintenancePlan { get; }
        public SnapshotReadObserver Observer => Factory.Observer!;
        public SkillAvailabilityCombatSnapshot CombatState { get; } = new(1, 1, 100, 1000, 100, 100,
            CurrentMp: 100, MaxMp: 100, CurrentDp: 100);
        private ObservingFactory Factory { get; }
        private CancellationTokenSource Stop { get; } = new(TimeSpan.FromSeconds(5));

        public Fixture()
        {
            var settings = new ScriptSettings { SkillTreeReleaseMode = SkillTreeReleaseMode.QuickbarAvailability };
            settings.SemiAuto.AttackKeyLoopEnabled = false;
            settings.Maintenance.SitMaintenanceEnabled = false;
            settings.QuickbarSkills.ExecutionTree.Add(new() { SkillId = 21, Name = "counter", BaseName = "counter" });
            settings.Maintenance.StatusMaintenanceRules.Add(new()
            {
                SkillId = 900, SkillName = "already active buff", Key = "D3", RunTiming = MaintenanceRuleRunTiming.Always
            });
            Api.Player = new(1, 100, "Fake", 100, 100, 100, 100, 100, new(0, 0, 0), DateTimeOffset.Now);
            Api.TargetEntityId = 100;
            Api.TargetServerObjectId = 1; // The monster targets the local player; its own identity is below.
            Api.TargetOwnServerObjectId = 1000;
            Api.TargetCurrentHp = 1000;
            Api.TargetMaxHp = 1000;
            Api.PlayerAbnormalStatuses = new(1, DateTimeOffset.Now, 1,
                new[] { new AbnormalStatusEntrySnapshot(0, 900, PlayerAbnormalStatusSnapshot.BuffCategory, 5000, 1, 0) });
            Api.Skills = new[]
            {
                new SkillSnapshot(21, "counter", 1, 1, "counter", 1, false, 30000, 0, XmlCounterSkill: "Parry"),
                new SkillSnapshot(900, "already active buff", 1, 1, "already active buff", 1, false, 30000, 0),
                new SkillSnapshot(999, "unconfigured", 1, 1, "unconfigured", 1, false, 30000, 0)
            };
            Api.Quickbar = new(0, new[]
            {
                new QuickbarSlotSnapshot(SkillQuickbar.Main, 1, 21, 21),
                new QuickbarSlotSnapshot(SkillQuickbar.Main, 2, 21, 900)
            });
            Api.SkillAvailability = Availability(canUse: false);
            Factory = new(Api);
            var logger = new InMemoryRoadhogLogger();
            Context = new(new AccountConfig { AccountName = "outer-fast-poll", ScriptSettings = settings }, Factory,
                logger, new AccountRuntimeManager(logger), new(), Stop.Token);
            MaintenancePlan = SemiAutoSkillPlan.FromSettings(settings.Skills);
            Controller = new(Keyboard);
        }

        public SkillAvailabilitySnapshot Availability(bool canUse) => new(0,
            new[] { new SkillAvailabilitySlotSnapshot(SkillQuickbar.Main, 1, 21, 21, 21, canUse) },
            BindingSlots: Api.Quickbar.Slots.Select(slot => new SkillAvailabilityBindingSnapshot(
                slot.Bar, slot.Slot, slot.ContentType, slot.SkillId, slot.SkillId)).ToArray(), CombatState: CombatState);

        public async Task PrepareAsync()
        {
            await Context.PrepareSkillBindingsAsync();
            Observer.ResetCounts();
        }

        public void Dispose() => Stop.Dispose();
    }

    public interface IObservedSkillSnapshots : IRoadhogSnapshotReader, ISkillAvailabilitySnapshotReader { }

    public class SnapshotReadObserver : DispatchProxy
    {
        public IRoadhogSnapshotReader Inner { get; set; } = null!;
        public int AvailabilityReads { get; private set; }
        public int AbnormalReads { get; private set; }
        public int FullSkillReads { get; private set; }
        public List<uint[]> SkillIdReads { get; } = new();

        public void ResetCounts()
        {
            AvailabilityReads = AbnormalReads = FullSkillReads = 0;
            SkillIdReads.Clear();
        }

        protected override object? Invoke(MethodInfo? method, object?[]? arguments)
        {
            switch (method!.Name)
            {
                case nameof(ISkillAvailabilitySnapshotReader.ReadSkillAvailabilityAsync): AvailabilityReads++; break;
                case nameof(IRoadhogSnapshotReader.ReadPlayerAbnormalStatusesAsync): AbnormalReads++; break;
                case nameof(IRoadhogSnapshotReader.ReadSkillsAsync):
                    if (arguments![0] is IReadOnlyCollection<uint> ids) SkillIdReads.Add(ids.ToArray());
                    else FullSkillReads++;
                    break;
            }
            // Forward every request to the real official reader. The observer owns no snapshot or fallback.
            return method.Invoke(Inner, arguments);
        }
    }

    private sealed class ObservingFactory(FakeGameApi api) : IRoadhogSnapshotReaderFactory
    {
        public SnapshotReadObserver? Observer { get; private set; }

        public IRoadhogSnapshotReader Create(AccountConfig config, IRoadhogLogger logger, CancellationToken cancellationToken = default)
        {
            var proxy = DispatchProxy.Create<IObservedSkillSnapshots, SnapshotReadObserver>();
            Observer = (SnapshotReadObserver)proxy;
            Observer.Inner = new RoadhogSnapshotReaderFactory(api).Create(config, logger, cancellationToken);
            return proxy;
        }
    }
}
