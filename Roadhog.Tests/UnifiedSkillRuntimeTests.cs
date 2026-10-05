using Roadhog.Application;
using Roadhog.Application.JumpAssist;
using Roadhog.Application.SemiAuto;
using Roadhog.Application.Workers;
using Roadhog.Core.Accounts;
using Roadhog.Core.Api;
using Roadhog.Core.Diagnostics;
using Roadhog.Core.Model;
using Roadhog.Infrastructure.Vmm;

internal static class UnifiedSkillRuntimeTests
{
    private const uint CommandId = 100;
    private const uint OtherId = 200;
    private const uint ChildId = 300;
    private const uint FallbackId = 400;

    public static async Task LegacyEntryUsesQuickbarExecutorAsync()
    {
        using var f = new Fixture();
        var original = f.Settings;
        original.SkillTreeReleaseMode = SkillTreeReleaseMode.Legacy;
        original.QuickbarSkills.ExecutionTree.Clear();
        original.Skills.ExecutionTree = new() { Node(OtherId) };
        var plan = SemiAutoSkillPlan.FromSettings(original.Skills);
        await f.Controller.TickAsync(f.Context, plan, f.State);
        Sequence(new[] { "D2" }, f.Keyboard.Keys, "a direct legacy entry migrates its tree and uses the exact bar key");
        Check(f.Context.Config.ScriptSettings!.SkillTreeReleaseMode == SkillTreeReleaseMode.QuickbarAvailability,
            "the runtime settings are normalized to the single executor");
        Check(original.SkillTreeReleaseMode == SkillTreeReleaseMode.Legacy && original.QuickbarSkills.ExecutionTree.Count == 0,
            "entry migration operates on a clone of the supplied settings");
        Check(f.Api.SkillAvailabilityReadCount > 0 && f.State.QuickbarSkills.PendingAction?.Node.SkillId == OtherId,
            "legacy settings actually dispatch the current availability executor");
        Check(f.Logger.Entries.Any(entry => entry.EventName == "quickbar_skill.key.pressed") &&
            !f.Logger.Entries.Any(entry => entry.EventName == "semi_auto.key.pressed"), "there is no old main attack dispatch");
    }

    public static async Task ExplicitEmptyTreeRemainsEmptyAsync()
    {
        using var f = new Fixture();
        f.Settings.QuickbarSkills.ExecutionTree.Clear();
        f.Settings.Skills.ExecutionTree = new() { Node(OtherId) };
        await f.PrepareAsync();
        await f.TickAsync();
        Check(f.Keyboard.Keys.IsEmpty, "an explicitly empty current tree cannot revive the old tree");
        Check(f.Api.SkillAvailabilityReadCount == 0, "an empty attack plan leaves shared work as the only path");
    }

    public static async Task MissingPetSkipsRootAndSummonedPetRestoresItAsync()
    {
        using var f = new Fixture();
        await f.PrepareAsync();
        await f.TickAsync();
        Sequence(new[] { "D2" }, f.Keyboard.Keys, "a missing local pet skips the earlier command root");
        f.Confirm(OtherId);
        f.SetPet(true);
        f.Clock.Advance(80);
        await f.TickAsync();
        Sequence(new[] { "D2", "D1" }, f.Keyboard.Keys, "the official summoned pet immediately restores command eligibility");
    }

    public static async Task MissingPetStopsPendingCommandRetryAsync()
    {
        using var f = new Fixture();
        f.SetPet(true);
        await f.PrepareAsync();
        await f.TickAsync();
        Check(f.State.QuickbarSkills.PendingAction is { AttemptCount: 1, Node.SkillId: CommandId }, "command starts unconfirmed");
        f.SetPet(false);
        f.Clock.Advance(80);
        await f.TickAsync();
        Sequence(new[] { "D1", "D2" }, f.Keyboard.Keys, "pet disappearance stops the pending command instead of retrying its key");
        Check(!f.Logger.Entries.Any(entry => entry.EventName == "quickbar_skill.release.confirmed"),
            "a pet guard never fabricates actor or cooldown release evidence");

        using var child = new Fixture();
        child.SetPet(true);
        child.Settings.QuickbarSkills.ExecutionTree = new() { Node(OtherId, Node(CommandId)), Node(FallbackId) };
        child.SetSlot(CommandId, false);
        child.Keyboard.AfterPress = key =>
        {
            if (key != "D2") return;
            child.Confirm(OtherId);
            child.SetSlot(CommandId, true);
        };
        await child.PrepareAsync();
        await child.TickEngineAsync();
        await child.TickEngineAsync();
        Check(child.State.QuickbarSkills.PendingAction is { Node.NodeKey: "0/0", AttemptCount: 1 },
            "the pet command child has an unconfirmed pending attempt");
        child.SetPet(false);
        child.Clock.Advance(80);
        await child.TickEngineAsync();
        Sequence(new[] { "D2", "D1", "D4" }, child.Keyboard.Keys,
            "a missing pet also retracts pending command continuations without retrying them");
    }

    public static async Task MissingPetChildKeepsNonCommandSiblingAsync()
    {
        using var f = new Fixture();
        f.Settings.QuickbarSkills.ExecutionTree = new() { Node(OtherId, Node(CommandId), Node(ChildId)), Node(FallbackId) };
        f.SetSlot(CommandId, false);
        f.SetSlot(ChildId, false);
        f.Keyboard.AfterPress = key =>
        {
            if (key != "D2") return;
            f.Confirm(OtherId);
            f.SetSlot(CommandId, true);
            f.SetSlot(ChildId, true);
        };
        await f.PrepareAsync();
        await f.TickEngineAsync();
        await f.TickEngineAsync();
        Sequence(new[] { "D2", "D3" }, f.Keyboard.Keys, "a suppressed command child cannot block the available non-command sibling");
        Check(f.State.QuickbarSkills.PendingAction?.Node.NodeKey == "0/1", "the continuation belongs to the normal configured child");
    }

    public static async Task MissingPetChildDoesNotHoldChainWaitAsync()
    {
        using var f = new Fixture();
        f.Settings.QuickbarSkills.ExecutionTree = new() { Node(OtherId, Node(CommandId)), Node(FallbackId) };
        f.SetSlot(CommandId, false);
        f.Keyboard.AfterPress = key =>
        {
            if (key != "D2") return;
            f.Confirm(OtherId);
            f.SetSlot(CommandId, true);
        };
        await f.PrepareAsync();
        await f.TickEngineAsync();
        await f.TickEngineAsync();
        Sequence(new[] { "D2", "D4" }, f.Keyboard.Keys, "an unavailable pet command continuation yields to an unrelated root without waiting 1500ms");
        Check(f.State.QuickbarSkills.PendingAction?.Node.SkillId == FallbackId, "the next root owns a fresh attempt");
    }

    public static async Task FinalPetLossRetractsCommandAsync()
    {
        using var f = new Fixture();
        f.SetPet(true);
        await f.PrepareAsync();
        var reads = 0;
        Task<SummonedPetRosterSnapshot> ReadPet()
        {
            if (++reads >= 3) f.SetPet(false);
            return Task.FromResult(f.Api.SummonedPetRoster);
        }
        await f.TickEngineAsync(ReadPet);
        Check(reads >= 3, "the selected command reaches its final pet recheck");
        Sequence(new[] { "D2" }, f.Keyboard.Keys, "pet loss during the last key boundary prevents any command key");
    }

    public static async Task TargetChangeDuringFinalPetReadIsGuardedAsync()
    {
        foreach (var combined in new[] { false, true })
        {
            using var f = new Fixture();
            f.SetPet(true);
            SkillAvailabilityCombatSnapshot Guard() => new(f.Api.Player.EntityId, 10, f.Api.TargetEntityId,
                f.Api.TargetOwnServerObjectId, 100, 100);
            if (combined) f.Api.SkillAvailability = f.Api.SkillAvailability with { CombatState = Guard() };
            await f.PrepareAsync();
            var reads = 0;
            Task<SummonedPetRosterSnapshot> ReadPet()
            {
                if (++reads == 3)
                {
                    f.Api.TargetOwnServerObjectId = 2000;
                    if (combined) f.Api.SkillAvailability = f.Api.SkillAvailability with { CombatState = Guard() };
                }
                return Task.FromResult(f.Api.SummonedPetRoster);
            }
            await f.TickEngineAsync(ReadPet);
            Check(reads >= 3 && f.Keyboard.Keys.IsEmpty, "the final pet read cannot bypass the target guard, combined=" + combined);
            Check(f.State.QuickbarSkills.YieldToWorker, "target movement yields control after the added read");
        }
    }

    public static async Task MissingPetExcludedFromClockBootstrapAsync()
    {
        using var f = new Fixture();
        f.Api.Skills = f.Api.Skills.Select(skill => skill with { CooldownEndTime = 900 }).ToArray();
        f.Api.SkillAvailability = f.Api.SkillAvailability with
        {
            Slots = Array.Empty<SkillAvailabilitySlotSnapshot>(),
            UnsupportedSkillIds = new[] { CommandId, OtherId },
            BindingSlots = f.Api.Quickbar.Slots.Select(slot =>
                new SkillAvailabilityBindingSnapshot(slot.Bar, slot.Slot, 21, slot.SkillId, slot.SkillId)).ToArray(),
            LastReleasedSkillId = 999, LastReleasedSkillTime = 1000
        };
        await f.PrepareAsync();
        await f.TickEngineAsync(unknownClock: true);
        Sequence(new[] { "D2" }, f.Keyboard.Keys, "unknown-CD startup trials also require a pet for command roots");
        Check(f.State.QuickbarSkills.PendingAction is { IsClockBootstrap: true, Node.SkillId: OtherId },
            "the allowed ordinary root receives the finite startup attempt");
        Check(f.State.QuickbarSkills.ClockBootstrap.AttemptedCandidateCount == 1, "suppressed commands spend no bootstrap trial");
    }

    public static async Task PetGuardScopeAndAliasesAsync()
    {
        foreach (var mode in new[] { "disabled", "other-class" })
        {
            using var f = new Fixture();
            if (mode == "disabled") f.Settings.Skills.SpiritmasterAutoSkillLogicEnabled = false;
            else f.Api.Player = f.Api.Player with { CharacterClassId = AionClassId.Sorcerer };
            await f.PrepareAsync();
            await f.TickAsync();
            Sequence(new[] { "D1" }, f.Keyboard.Keys, mode + " retains its existing non-Spiritmaster execution path");
            Check(f.Api.SummonedPetRosterReadCount == 0, mode + " adds no pet-policy reads");
        }
        foreach (var name in new[] { "命令:技能", " 命令：技能", "command:skill" })
        {
            using var f = new Fixture();
            f.Api.Skills = f.Api.Skills.Select(skill => skill.SkillId == CommandId
                ? skill with { Name = name, DisplayBaseName = name } : skill).ToArray();
            await f.PrepareAsync();
            var policy = new QuickbarSpiritmasterPetPolicy(f.NewPlan(), f.Api.Skills,
                () => Task.FromResult(f.Api.SummonedPetRoster));
            Check(policy.HasCommands && (await policy.ReadSuppressedSkillIdsAsync()).Contains(CommandId),
                "command identity recognizes the existing language aliases: " + name);
        }
    }

    public static Task ArchivedAttackTreeDoesNotExpandSharedReadAsync()
    {
        var settings = new SkillScriptSettings { SpiritmasterAutoSkillLogicEnabled = true };
        settings.ExecutionTree = new() { Node(CommandId), new SkillConfigNode { Name = "archived unknown skill" } };
        settings.OpeningSkill.Enabled = true;
        settings.OpeningSkill.ReleaseAll = true;
        settings.OpeningSkill.Skills = new() { new() { SkillId = OtherId, SkillName = Name(OtherId), Key = "D2" } };
        settings.Spiritmaster.DotSkills = new() { new() { SkillId = ChildId, SkillName = Name(ChildId) } };
        settings.Spiritmaster.PetHpMaintenanceRules = new() { new() { SkillId = FallbackId, SkillName = Name(FallbackId) } };
        var archive = SemiAutoSkillPlan.FromSettings(settings);
        var runtime = SemiAutoSkillPlan.FromSharedSettings(settings);
        Check(archive.RequiresFullSkillRead && archive.SkillReadIds.Contains(CommandId),
            "the old pure plan still describes its legacy input for compatibility tools");
        Check(!runtime.RequiresFullSkillRead && !runtime.RequiresFullSharedSkillRead && runtime.Roots.Count == 0,
            "archived roots and missing legacy IDs cannot force a full read in runtime");
        Sequence(new[] { OtherId, ChildId, FallbackId }.Select(id => id.ToString()),
            runtime.SkillReadIds.Select(id => id.ToString()), "runtime reads only current shared actions");
        Check(runtime.HasOpeningSkill && runtime.ReleaseAllOpeningSkills, "shared opening semantics are retained");
        return Task.CompletedTask;
    }

    public static async Task MigratedAlternateModesUseSpiritmasterCheckboxAsync()
    {
        foreach (var mode in new[] { SkillConfigurationMode.ManualMapping, SkillConfigurationMode.SystemClassification })
        {
            using var f = new Fixture();
            f.Settings.SkillTreeReleaseMode = SkillTreeReleaseMode.Legacy;
            f.Settings.QuickbarSkills.ExecutionTree.Clear();
            f.Settings.Skills.Mode = mode;
            f.Settings.Skills.SystemExecutionTree = new() { Node(CommandId), Node(OtherId) };
            f.Settings.Skills.ManualMappings = new()
            {
                new() { SkillName = Name(CommandId), Key = "D1", SkillType = "主动技能" },
                new() { SkillName = Name(OtherId), Key = "D2", SkillType = "主动技能" }
            };
            await f.PrepareAsync();
            var archive = SemiAutoSkillPlan.FromSettings(f.Settings.Skills, f.Context.SkillBindings);
            Check(!archive.UsesSpiritmasterAutoLogic, "old pure plan retains its mode contract");
            var runtime = SemiAutoSkillPlan.FromSharedSettings(f.Settings.Skills, f.Context.SkillBindings);
            Check(runtime.UsesSpiritmasterAutoLogic, "the current Spiritmaster checkbox is independent of archive mode");
            await f.Controller.TickAsync(f.Context, archive, f.State);
            Sequence(new[] { "D2" }, f.Keyboard.Keys, "migrated " + mode + " enables the checked command-pet guard");
            Check(f.Settings.Skills.Mode == mode && f.Api.SummonedPetRosterReadCount > 0,
                "archive is retained while the shared Spiritmaster path runs");
        }
    }

    public static async Task SharedOpeningActivatesPreparedTeamJumpAsync()
    {
        foreach (var phase in new[] { "attack-key", "opening-skill", "spirit-opening", "spirit-special" })
        {
            using var f = new Fixture();
            var expectedKey = "C";
            if (phase == "attack-key")
            {
                f.Settings.Skills.SpiritmasterAutoSkillLogicEnabled = false;
                f.Settings.SemiAuto.AttackKeyLoopEnabled = true;
            }
            else if (phase == "opening-skill")
            {
                expectedKey = "D2";
                f.Settings.Skills.SpiritmasterAutoSkillLogicEnabled = false;
                f.Settings.Skills.OpeningSkill.Enabled = true;
                f.Settings.Skills.OpeningSkill.Skills = new()
                {
                    new() { SkillId = OtherId, SkillName = Name(OtherId), Key = "D2" }
                };
                f.Api.Skills = f.Api.Skills.Select(skill => skill.SkillId == OtherId
                    ? skill with { CooldownDuration = 0 } : skill).ToArray();
            }
            else if (phase == "spirit-opening")
            {
                expectedKey = "D3";
                f.Settings.Skills.Spiritmaster.OpeningAttackSkillId = ChildId;
                f.Settings.Skills.Spiritmaster.OpeningAttackSkillName = Name(ChildId);
                f.Settings.Skills.Spiritmaster.OpeningAttackKey = expectedKey;
            }
            else
            {
                expectedKey = "D4";
                f.Settings.Skills.Spiritmaster.SummonSkills = new()
                {
                    new() { SkillId = FallbackId, SkillName = Name(FallbackId), Key = expectedKey }
                };
            }
            await f.PrepareAsync();
            await using var jump = new CombatJumpAssistSession(f.Context, f.Keyboard, teamFollower: true,
                jumpInterval: TimeSpan.FromMilliseconds(500), cooldownPollInterval: TimeSpan.FromMilliseconds(5),
                keyHoldDuration: TimeSpan.FromMilliseconds(1));
            await jump.EnterTeamGroupAsync();
            await jump.PrepareTeamCombatJumpAsync(f.Target().ServerObjectId);
            Check(!f.Logger.Entries.Any(entry => entry.EventName == "jump_assist.team_target_jump.requested"),
                "team target acceptance only prepares jump before " + phase);
            await f.Controller.TickAsync(f.Context,
                SemiAutoSkillPlan.FromSharedSettings(f.Settings.Skills, f.Context.SkillBindings), f.State, jumpAssist: jump);
            Check(f.Keyboard.Keys.Contains(expectedKey), "the shared opening branch actually runs: " + phase);
            Check(f.Logger.Entries.Any(entry => entry.EventName == "jump_assist.team_target_jump.requested"),
                "shared opening early return must activate the prepared team jump: " + phase);
        }
    }

    public static async Task ImpossibleCooldownInvalidatesAndRebuildsAsync()
    {
        using var f = new Fixture();
        f.ConfigureOrdinaryRoots(OtherId);
        f.SetCooldown(OtherId, 10000, 5000);
        CalibrateClock(f.State);
        await f.PrepareAsync();
        await f.TickAsync();
        Check(f.Keyboard.Keys.IsEmpty && f.State.QuickbarSkills.ClockBootstrap.IsCompleted,
            "a valid calibrated cooling root completes startup without pressing a key");

        var badEnd = f.SetCooldown(OtherId, 10000, 130000);
        await f.TickAsync();
        Check(!f.State.HasCooldownTickCalibration && HasInvalidation(f), "an impossible short cooldown invalidates the stale clock");
        Sequence(new[] { "D2" }, f.Keyboard.Keys, "the invalidated clock reopens a finite exact-bar startup trial");
        Check(f.State.QuickbarSkills.PendingAction is { IsClockBootstrap: true, AttemptCount: 1 } &&
            f.State.QuickbarSkills.ClockBootstrap.AttemptedCandidateCount == 1,
            "only one real bootstrap attempt is spent after invalidation");

        f.Api.Skills = f.Api.Skills.Select(skill => skill.SkillId == OtherId
            ? skill with { CooldownEndTime = unchecked(badEnd + 1000u) } : skill).ToArray();
        f.Clock.Advance(80);
        await f.TickAsync();
        Check(f.State.HasCooldownTickCalibration && f.State.QuickbarSkills.ClockBootstrap.IsCompleted,
            "a real CD advance reconstructs the shared clock and closes startup");
        Check(f.Logger.Entries.Any(entry => entry.EventName == "semi_auto.cooldown.calibrated" &&
            Convert.ToUInt32(entry.Fields.GetValueOrDefault("skillId")) == OtherId), "clock rebuilding is logged for the released exact skill");
        Sequence(new[] { "D2" }, f.Keyboard.Keys, "the rebuilt clock keeps the actual cooldown from being retried");
    }

    public static async Task ZeroDurationDoesNotBlockClockInvalidationAsync()
    {
        using var f = new Fixture();
        f.ConfigureOrdinaryRoots(OtherId, ChildId);
        f.SetCooldown(OtherId, 0, 130000);
        f.SetCooldown(ChildId, 10000, 130000);
        CalibrateClock(f.State);
        await f.PrepareAsync();
        await f.TickAsync();
        Check(!f.State.HasCooldownTickCalibration && HasInvalidation(f, ChildId),
            "a zero-duration root is ignored while an impossible ordinary cooldown still invalidates");

        using var onlyZero = new Fixture();
        onlyZero.ConfigureOrdinaryRoots(OtherId);
        onlyZero.SetCooldown(OtherId, 0, 130000);
        CalibrateClock(onlyZero.State);
        await onlyZero.PrepareAsync();
        await onlyZero.TickAsync();
        Check(onlyZero.State.HasCooldownTickCalibration && !HasInvalidation(onlyZero),
            "zero-duration data alone cannot invalidate the clock");
    }

    public static async Task PlausibleCooldownsStayCoolingAsync()
    {
        using var f = new Fixture();
        f.ConfigureOrdinaryRoots(OtherId, ChildId);
        f.SetCooldown(OtherId, 60000, 50000);
        f.SetCooldown(ChildId, 10000, 5000);
        CalibrateClock(f.State);
        await f.PrepareAsync();
        await f.TickAsync();
        Check(f.State.HasCooldownTickCalibration && !HasInvalidation(f) && f.Keyboard.Keys.IsEmpty,
            "a real long cooldown and a trusted short cooldown stay cooling without startup retries");
    }

    public static async Task ArchivedRootsCannotInvalidateCurrentClockAsync()
    {
        using var f = new Fixture();
        f.ConfigureOrdinaryRoots(OtherId);
        f.Settings.Skills.SpiritmasterAutoSkillLogicEnabled = true;
        f.Settings.Skills.ExecutionTree = new() { Node(CommandId) };
        f.Settings.Skills.Spiritmaster.DotSkills = new() { new() { SkillId = CommandId, SkillName = Name(CommandId) } };
        f.SetCooldown(CommandId, 1000, 130000);
        f.SetCooldown(OtherId, 10000, 5000);
        CalibrateClock(f.State);
        await f.PrepareAsync();
        await f.TickAsync();
        Check(f.State.HasCooldownTickCalibration && !HasInvalidation(f) && f.Keyboard.Keys.IsEmpty,
            "an archived root read for a shared DOT reference cannot invalidate the current attack clock");
    }

    public static async Task ObservedOrdinaryCooldownSurvivesZeroReadAsync()
    {
        using var f = new Fixture();
        f.ConfigureOrdinaryRoots(OtherId);
        f.SetCooldown(OtherId, 10000, 5000);
        CalibrateClock(f.State);
        await f.PrepareAsync();
        await f.TickAsync();
        f.SetCooldown(OtherId, 10000, 0);
        await f.TickAsync();
        Check(f.State.HasCooldownTickCalibration && !HasInvalidation(f) && f.Keyboard.Keys.IsEmpty,
            "the existing known future ordinary cooldown still blocks a transient zero end-tick observation");
    }

    public static async Task NearReadyOrdinaryCooldownKeepsToleranceAsync()
    {
        using var f = new Fixture();
        f.ConfigureOrdinaryRoots(OtherId);
        CalibrateClock(f.State);
        await f.PrepareAsync();
        f.SetCooldown(OtherId, 10000, SemiAutoSkillReleasePriority.CooldownReadyToleranceMs);
        await f.TickAsync();
        Check(f.State.HasCooldownTickCalibration && !HasInvalidation(f), "a near-ready root keeps its valid calibration");
        Sequence(new[] { "D2" }, f.Keyboard.Keys, "the actual unified entry retains the existing ready tolerance");
        Check(f.State.QuickbarSkills.PendingAction is { IsClockBootstrap: false }, "a near-ready cooldown needs no exceptional clock trial");
    }

    private static bool HasInvalidation(Fixture f, uint? skillId = null) => f.Logger.Entries.Any(entry =>
        entry.EventName == "semi_auto.cooldown.calibration_invalidated" &&
        (!skillId.HasValue || Convert.ToUInt32(entry.Fields.GetValueOrDefault("skillId")) == skillId));

    private static void CalibrateClock(SemiAutoCombatState state)
    {
        var osTick = unchecked((uint)Environment.TickCount64);
        var marker = new SkillSnapshot(900001, "clock-marker", 1, 1, "clock-marker", 1, false, 1000, 0);
        state.MarkSkillPressed(marker, DateTimeOffset.Now.AddSeconds(1));
        Check(state.TryUpdateCooldownTickCalibration(new[] { marker with { CooldownEndTime = unchecked(osTick + 1000u) } },
            osTick, DateTimeOffset.Now, out _), "the fixture has an actual initial CD advancement");
    }

    private static string Name(uint id) => id == CommandId ? "命令:技能" : "skill" + id;
    private static SkillConfigNode Node(uint id, params SkillConfigNode[] children) => new()
    {
        SkillId = id, Name = Name(id), BaseName = Name(id), Type = "主动技能", Children = children.ToList()
    };

    private sealed class Fixture : IDisposable
    {
        public FakeGameApi Api { get; } = new();
        public RecordingKeyboardInput Keyboard { get; } = new();
        public InMemoryRoadhogLogger Logger { get; } = new();
        public SemiAutoCombatState State { get; } = new();
        public Clock Clock { get; } = new();
        public AccountWorkerContext Context { get; }
        public ScriptSettings Settings => Context.Config.ScriptSettings!;
        public SemiAutoCombatController Controller { get; }
        private readonly CancellationTokenSource stop = new(TimeSpan.FromSeconds(15));
        private readonly AvailabilityReader availability;
        private SemiAutoSkillPlan sharedPlan = null!;
        private uint releaseTime;

        public Fixture()
        {
            var settings = new ScriptSettings { SkillTreeReleaseMode = SkillTreeReleaseMode.QuickbarAvailability };
            settings.Skills.Mode = SkillConfigurationMode.Auto;
            settings.Skills.SpiritmasterAutoSkillLogicEnabled = true;
            settings.Skills.OpeningSkill.Enabled = false;
            settings.SemiAuto.AttackKeyLoopEnabled = false;
            settings.SemiAuto.AttackWeaveEnabled = false;
            settings.Maintenance.SitMaintenanceEnabled = false;
            settings.QuickbarSkills.ExecutionTree = new() { Node(CommandId), Node(OtherId) };
            Api.Player = Api.Player with { CharacterClassId = AionClassId.Spiritmaster };
            Api.TargetOwnServerObjectId = 1000;
            Api.Skills = new[] { CommandId, OtherId, ChildId, FallbackId }.Select(id => new SkillSnapshot(id, Name(id), 1,
                1, Name(id), 1, false, 10000, 0, XmlActivation: "Active", XmlSkillType: "Magical", XmlSubType: "Attack",
                XmlTargetRelationRestriction: "Enemy", XmlEffects: "SpellATK_Instant")).ToArray();
            Api.Quickbar = new(0, Api.Skills.Select((skill, index) =>
                new QuickbarSlotSnapshot(SkillQuickbar.Main, index, 21, skill.SkillId)).ToArray());
            Api.SkillAvailability = new(0, Api.Quickbar.Slots.Select(slot =>
                new SkillAvailabilitySlotSnapshot(slot.Bar, slot.Slot, 21, slot.SkillId, slot.SkillId, true)).ToArray(),
                BindingSignature: "unified-command-layout");
            SetPet(false);
            Api.LockedTargetAbnormalStatuses = new(Target(), 0, Array.Empty<AbnormalStatusEntrySnapshot>(), Clock.GetUtcNow());
            Context = new(new AccountConfig { AccountName = "unified-runtime", ScriptSettings = settings },
                new RoadhogSnapshotReaderFactory(Api), Logger, new AccountRuntimeManager(Logger), new(), stop.Token);
            Controller = new(Keyboard, timeProvider: Clock);
            availability = new(Api);
        }

        public async Task PrepareAsync()
        {
            await Context.PrepareSkillBindingsAsync();
            sharedPlan = SemiAutoSkillPlan.FromSharedSettings(Settings.Skills, Context.SkillBindings);
        }
        public async Task<TimeSpan> TickAsync()
        {
            try { return await Controller.TickAsync(Context, sharedPlan, State); }
            catch (OperationCanceledException exception)
            {
                throw new InvalidOperationException("runtime fixture canceled: availability=" + Api.SkillAvailabilityReadCount +
                    ", pet=" + Api.SummonedPetRosterReadCount + ", combined=" + State.QuickbarSkills.SupportsCombatState +
                    ", keys=" + string.Join(",", Keyboard.Keys) + ", events=" +
                    string.Join(",", Logger.Entries.TakeLast(12).Select(entry => entry.EventName)), exception);
            }
        }
        public QuickbarSkillPlan NewPlan() => QuickbarSkillPlan.FromSettings(Settings.QuickbarSkills, Context.SkillBindings!);
        public Task<TimeSpan> TickEngineAsync(Func<Task<SummonedPetRosterSnapshot>>? readPet = null, bool unknownClock = false)
        {
            var plan = NewPlan();
            var policy = new QuickbarSpiritmasterPetPolicy(plan, Api.Skills,
                readPet ?? (() => Task.FromResult(Api.SummonedPetRoster)));
            return new QuickbarSkillCombatController(Keyboard, Clock).TickAsync(plan, State.QuickbarSkills, Target(), availability,
                ids => Task.FromResult<IReadOnlyList<SkillSnapshot>>(Api.Skills.Where(skill => ids.Contains(skill.SkillId)).ToArray()),
                Settings.SemiAuto, Logger, stop.Token, readTargetBeforePress: () => Task.FromResult(Target()),
                ordinaryReadiness: skills => skills.Select(skill => skill.SkillId).ToHashSet(),
                cooldownReadiness: skill => unknownClock ? SemiAutoSkillCooldownReadiness.Unknown :
                    skill.CooldownEndTime == 0 ? SemiAutoSkillCooldownReadiness.Ready : SemiAutoSkillCooldownReadiness.CoolingDown,
                isCooldownClockCalibrated: unknownClock ? () => false : null,
                readSuppressedSkillIds: policy.ReadSuppressedSkillIdsAsync);
        }
        public LockedTargetSnapshot Target() => new(Api.TargetEntityId, Api.TargetOwnServerObjectId, 1,
            LockedTargetSnapshot.MonsterObjectType, "dummy", 1000, 1000, null, 1, Clock.GetUtcNow());
        public void SetPet(bool present)
        {
            var roster = SummonedPetRosterSnapshot.Empty(10, Clock.GetUtcNow());
            Api.SummonedPetRoster = !present ? roster : roster with
            {
                LocalLinkedPetServerObjectId = 11,
                LocalPlayerPet = roster.LocalPlayerPet with
                {
                    Pet = roster.LocalPlayerPet.Pet with
                    {
                        IsSummoned = true, ServerObjectId = 11, LocalLinkedPetServerObjectId = 11,
                        CurrentHp = 100, MaxHp = 100, HpPercent = 100,
                        OwnerConfirmed = true, EvidenceSource = "fixture",
                        HealthFields = new SummonedPetHealthFieldValidity(true, true, true)
                    }
                }
            };
        }
        public void SetSlot(uint id, bool canUse) => Api.SkillAvailability = Api.SkillAvailability with
        {
            Slots = Api.SkillAvailability.Slots.Select(slot => slot.EffectiveSkillId == id ? slot with { CanUse = canUse } : slot).ToArray()
        };
        public void Confirm(uint id)
        {
            Api.Skills = Api.Skills.Select(skill => skill.SkillId == id
                ? skill with { CooldownEndTime = unchecked((uint)Environment.TickCount64 + 10000u) } : skill).ToArray();
            Api.SkillAvailability = Api.SkillAvailability with { LastReleasedSkillId = id, LastReleasedSkillTime = ++releaseTime };
        }
        public void ConfigureOrdinaryRoots(params uint[] ids)
        {
            Settings.Skills.SpiritmasterAutoSkillLogicEnabled = false;
            Settings.QuickbarSkills.ExecutionTree = ids.Select(id => Node(id)).ToList();
            Api.SkillAvailability = Api.SkillAvailability with
            {
                Slots = Array.Empty<SkillAvailabilitySlotSnapshot>(), UnsupportedSkillIds = ids,
                BindingSlots = Api.Quickbar.Slots.Select(slot =>
                    new SkillAvailabilityBindingSnapshot(slot.Bar, slot.Slot, 21, slot.SkillId, slot.SkillId)).ToArray(),
                LastReleasedSkillId = 999, LastReleasedSkillTime = unchecked((uint)Environment.TickCount64)
            };
        }
        public uint SetCooldown(uint id, uint duration, int remainingMs)
        {
            var end = remainingMs == 0 ? 0u : unchecked((uint)Environment.TickCount64 + (uint)remainingMs);
            Api.Skills = Api.Skills.Select(skill => skill.SkillId == id
                ? skill with { CooldownDuration = duration, CooldownEndTime = end } : skill).ToArray();
            return end;
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
        public Task<PublishedGameSnapshot<SkillAvailabilitySnapshot>> ReadSkillAvailabilityAsync(long afterVersion = 0,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new PublishedGameSnapshot<SkillAvailabilitySnapshot>(++version, api.SkillAvailability));
        }
    }
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void Sequence(IEnumerable<string> expected, IEnumerable<string> actual, string message) =>
        Check(expected.SequenceEqual(actual), message + $": expected [{string.Join(",", expected)}], got [{string.Join(",", actual)}]");
}
