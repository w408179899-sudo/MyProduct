using System.Text.Json;
using Roadhog.Application.SemiAuto;
using Roadhog.Core.Accounts;
using Roadhog.Core.Api;
using Roadhog.Core.Model;
using static SharedSkillAuditFixture;

internal static class AdvancedSkillTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-11-09T20:00:00+08:00");
    private static SkillConfigNode Node(uint id, params SkillConfigNode[] children) => new()
        { SkillId = id, Name = "skill" + id, BaseName = "skill" + id, Children = children.ToList() };
    private static LockedTargetSnapshot Target(string name = "manual", uint server = 1) =>
        new(1, server, 1, 2, name, 100, 100, null, 1, Now);
    private static AdvancedSkillSettings Settings() => new()
    {
        Enabled = true, Elite = new() { MonsterNames = new() { "manual" }, Skills = new() { Node(11) } }
    };
    private static readonly SkillSnapshot[] Learned =
    {
        Skill(11, "skill11", 1), Skill(21, "skill21", 1) with { XmlChainCategory = "a" },
        Skill(22, "skill22", 1) with { XmlPrechainCategory = "a", XmlChainCategory = "b" },
        Skill(23, "skill23", 1) with { XmlPrechainCategory = "b" }, Skill(31, "skill31", 1)
    };
    private static readonly QuickbarSnapshot Bar = new(0, new QuickbarSlotSnapshot[]
    {
        new(SkillQuickbar.Main, 0, 21, 11), new(SkillQuickbar.Main, 1, 21, 21), new(SkillQuickbar.Main, 2, 21, 31)
    });
    private static SkillAvailabilitySnapshot Available(bool advanced = true, bool normal = true, bool second = true,
        uint effective = 21, uint released = 0, uint releasedTime = 0) => new(0, new SkillAvailabilitySlotSnapshot[]
    {
        new(SkillQuickbar.Main, 0, 21, 11, 11, advanced),
        new(SkillQuickbar.Main, 1, 21, 21, effective, normal),
        new(SkillQuickbar.Main, 2, 21, 31, 31, second)
    }, released, releasedTime);

    public static Task PersistenceAndMembershipAsync()
    {
        var old = JsonSerializer.Deserialize<ScriptSettings>("{}")!;
        Check(!old.AdvancedSkills.Enabled && !old.Clone().AdvancedSkills.Enabled, "old configurations leave advanced mode off");
        var settings = Settings();
        Check(settings.TryAddMonster(AdvancedMonsterCategory.Ranged, "  手动怪物  ", out _), "typed names are trimmed and accepted");
        Check(!settings.TryAddMonster(AdvancedMonsterCategory.Melee, "手动怪物", out var owner) && owner == AdvancedMonsterCategory.Ranged,
            "cross-category manual duplicates are blocked even when global settings are off");
        settings.Enabled = false;
        Check(!settings.TryAddMonster(AdvancedMonsterCategory.Ranged, "手动怪物", out owner), "same-category duplicates are blocked");
        Check(!settings.TryAddMonster(AdvancedMonsterCategory.Melee, "  ", out _), "empty name is rejected");
        settings.Elite.Skills[0].Children.Add(Node(12));
        var clone = settings.Clone(); clone.Elite.Skills[0].Children.Clear(); clone.Ranged.MonsterNames.Clear();
        Check(settings.Elite.Skills[0].Children.Count == 1 && settings.Ranged.MonsterNames.Count == 1, "deep clone owns names and chains");
        var restored = JsonSerializer.Deserialize<AdvancedSkillSettings>(JsonSerializer.Serialize(settings))!;
        Check(restored.Elite.Skills[0].Children.Single().SkillId == 12 && restored.Ranged.MonsterNames.Single() == "手动怪物",
            "account/profile/RPC JSON retains group membership and chain configuration");
        Check(Enum.GetValues<AdvancedMonsterCategory>().Length == 3, "special monsters remain reserved with no runtime category");
        return Task.CompletedTask;
    }

    public static Task MatchingAndIsolationAsync()
    {
        var bindings = new SkillKeyBindings(Bar, Learned);
        var ordinary = QuickbarSkillPlan.FromSettings(new() { ExecutionTree = new() { Node(21, Node(22, Node(23))), Node(31) } }, bindings);
        foreach (var change in new Action<AdvancedSkillSettings>[]
        {
            s => s.Enabled = false, s => s.Elite.Enabled = false, s => s.Elite.Skills.Clear(),
            s => s.Elite.MonsterNames.Clear(), s => s.Ranged.MonsterNames.Add("manual")
        })
        {
            var settings = Settings(); change(settings);
            Check(ReferenceEquals(new AdvancedSkillCombatPlans(settings, ordinary, bindings, null).Resolve(Target()), ordinary),
                "disabled, empty, unlisted or corrupt duplicate configurations return the untouched original plan");
        }
        var plans = new AdvancedSkillCombatPlans(Settings(), ordinary, bindings, null);
        Check(ReferenceEquals(plans.Resolve(Target("manual boss")), ordinary), "no substring or automatic attribute matching");
        Check(ReferenceEquals(plans.Resolve(Target() with { CurrentHp = 0 }), ordinary), "dead target cannot select advanced mode");
        var active = plans.Resolve(Target(" MANUAL "));
        Check(active.IsAdvancedMode && active.Roots.Select(node => node.SkillId).SequenceEqual(new uint[] { 11, 21, 31 }),
            "exact manual match puts advanced skills before the original roots");
        Check(ReferenceEquals(active, plans.Resolve(Target(server: 99))), "same kind of respawned monster keeps manual membership");
        Check(active.Roots[0].NodeKey.StartsWith("advanced:") && !active.Roots[0].NodeKey.Contains('/') &&
            ReferenceEquals(active.Roots[1], ordinary.Roots[0]), "advanced nodes have isolated keys and preserve original root identity");
        Check(!new AdvancedSkillCombatPlans(new(), ordinary, bindings, null).Resolve(Target()).IsAdvancedMode, "another account remains isolated");
        return Task.CompletedTask;
    }

    public static Task PriorityAndChainsAsync()
    {
        var bindings = new SkillKeyBindings(Bar, Learned);
        var ordinary = QuickbarSkillPlan.FromSettings(new() { ExecutionTree = new() { Node(21, Node(22, Node(23))), Node(31) } }, bindings);
        var advancedSettings = Settings(); advancedSettings.Elite.Skills[0].Type = "触发技能";
        var plan = new AdvancedSkillCombatPlans(advancedSettings, ordinary, bindings, null).Resolve(Target());
        var state = new QuickbarSkillCombatState();
        QuickbarSkillReleaseDecision Select(SkillAvailabilitySnapshot available, DateTimeOffset? at = null, IReadOnlySet<uint>? cooling = null) =>
            QuickbarSkillReleasePriority.SelectNext(plan, state, available, at ?? Now, coolingSkillIds: cooling);
        Check(Select(Available()).Node?.SkillId == 11, "advanced first even with all normal roots ready");
        Check(Select(Available(advanced: false)).Node?.SkillId == 21, "first usable ordinary root is fallback");
        Check(Select(Available(advanced: false, normal: false)).Node?.SkillId == 31, "unusable first normal root is skipped");
        Check(Select(Available(advanced: false)).Node?.SkillId == 21, "normal list always restarts at its first usable entry");
        state.BeginAction(plan.Roots[1], Learned[1], Available(advanced: false), Now, TimeSpan.FromSeconds(8));
        Check(Select(Available()).Kind == QuickbarSkillDecisionKind.None, "newly ready advanced skill cannot replace a pending normal action");
        Check(Select(Available(), Now.AddMilliseconds(81)).Node?.SkillId == 21, "normal action retains the original retry mechanism");
        Check(state.TryConfirmAction(Available(released: 21, releasedTime: 1), Learned, Now), "normal starter confirms");
        Check(Select(Available(effective: 22)).Node?.SkillId == 22, "normal continuation outranks advanced skill");
        Check(Select(Available(normal: false, effective: 22)).Kind == QuickbarSkillDecisionKind.WaitForChain, "chain handoff gap remains reserved");
        var child = plan.Roots[1].Children.Single();
        state.BeginAction(child, Learned[2], Available(effective: 22, released: 21, releasedTime: 1), Now, TimeSpan.FromSeconds(8));
        Check(state.TryConfirmAction(Available(effective: 23, released: 22, releasedTime: 2), Learned, Now), "second link confirms");
        Check(Select(Available(effective: 23)).Node?.SkillId == 23, "full chain finishes before advanced selection");
        var last = child.Children.Single();
        state.BeginAction(last, Learned[3], Available(effective: 23, released: 22, releasedTime: 2), Now, TimeSpan.FromSeconds(8));
        Check(state.TryConfirmAction(Available(normal: false, released: 23, releasedTime: 3), Learned, Now), "last link confirms");
        Check(Select(Available(normal: false)).Node?.SkillId == 11, "advanced resumes immediately after complete chain");

        var advancedChain = Settings(); advancedChain.Elite.Skills = new() { Node(21, Node(22, Node(23))) };
        var secondPlan = new AdvancedSkillCombatPlans(advancedChain, ordinary, bindings, null).Resolve(Target());
        var secondState = new QuickbarSkillCombatState(); var root = secondPlan.Roots[0];
        secondState.BeginAction(root, Learned[1], Available(), Now, TimeSpan.FromSeconds(8));
        secondState.TryConfirmAction(Available(released: 21, releasedTime: 1), Learned, Now);
        Check(QuickbarSkillReleasePriority.SelectNext(secondPlan, secondState, Available(effective: 22), Now).Node?.NodeKey == root.Children[0].NodeKey,
            "advanced-origin chains keep their own stable continuation identity");
        var advancedSecond = root.Children[0]; var advancedLast = advancedSecond.Children[0];
        secondState.BeginAction(advancedSecond, Learned[2], Available(effective: 22, released: 21, releasedTime: 1), Now, TimeSpan.FromSeconds(8));
        secondState.TryConfirmAction(Available(effective: 23, released: 22, releasedTime: 2), Learned, Now);
        secondState.BeginAction(advancedLast, Learned[3], Available(effective: 23, released: 22, releasedTime: 2), Now, TimeSpan.FromSeconds(8));
        secondState.TryConfirmAction(Available(effective: 23, released: 23, releasedTime: 3), Learned, Now);
        Check(QuickbarSkillReleasePriority.SelectNext(secondPlan, secondState, Available(effective: 23, second: false), Now).Node?.SkillId != 23,
            "same chain present in both lists must not repeat an already accepted still-lit opportunity");
        var blocked = new QuickbarSkillCombatState();
        blocked.BeginAction(root, Learned[1], Available(), Now, TimeSpan.FromSeconds(8));
        blocked.RejectAction(Now, TimeSpan.FromSeconds(1));
        Check(QuickbarSkillReleasePriority.SelectNext(secondPlan, blocked, Available(second: false), Now).Node?.SkillId != 21,
            "duplicate ordinary entry cannot bypass advanced input failure suppression");
        var simple = QuickbarSkillPlan.FromSettings(new() { ExecutionTree = new() { Node(21), Node(31) } }, bindings);
        var ordered = new AdvancedSkillCombatPlans(Settings(), simple, bindings, null).Resolve(Target());
        var yielded = new QuickbarSkillCombatState();
        yielded.BeginAction(simple.Roots[0], Learned[1] with { CooldownDuration = 0 }, Available(), Now, TimeSpan.FromSeconds(8));
        yielded.TryConfirmAction(Available(released: 21, releasedTime: 1), new[] { Learned[1] with { CooldownDuration = 0 } }, Now);
        Check(QuickbarSkillReleasePriority.SelectNext(ordered, yielded, Available(advanced: false), Now).Node?.SkillId == 21,
            "advanced fallback starts at the first usable normal root even after a zero-CD release");
        Check(QuickbarSkillReleasePriority.SelectNext(simple, yielded, Available(advanced: false), Now).Node?.SkillId == 31,
            "unmatched original plan retains its pre-existing zero-CD round yielding");
        return Task.CompletedTask;
    }

    private static async Task<SharedSkillAuditFixture> FixtureAsync(Action<SharedSkillAuditFixture>? configure = null)
    {
        var f = new SharedSkillAuditFixture();
        f.Settings.Skills.SpiritmasterAutoSkillLogicEnabled = false;
        f.Settings.AdvancedSkills = new()
        {
            Enabled = true, Elite = new() { MonsterNames = new() { f.Api.TargetName }, Skills = new() { Node(911) } }
        };
        f.Settings.Skills.OpeningSkill = new() { Enabled = true, Skills = new() { new() { SkillId = 912, SkillName = "opener" } } };
        f.Api.Skills = f.Api.Skills.Concat(new[] { Skill(911, "advanced", 1), Skill(912, "opener", 1) }).ToArray();
        f.Api.Quickbar = f.Api.Quickbar with { Slots = f.Api.Quickbar.Slots.Concat(new QuickbarSlotSnapshot[]
            { new(SkillQuickbar.Main, 4, 21, 911), new(SkillQuickbar.Main, 5, 21, 912) }).ToArray() };
        f.Api.SkillAvailability = f.Api.SkillAvailability with
        {
            UnsupportedSkillIds = new uint[] { AttackId, 911 },
            BindingSlots = f.Api.Quickbar.Slots.Select(slot => new SkillAvailabilityBindingSnapshot(slot.Bar, slot.Slot, 21, slot.SkillId, slot.SkillId)).ToArray()
        };
        configure?.Invoke(f); await f.PrepareAsync(); return f;
    }

    public static async Task RuntimeLoopAsync()
    {
        using var f = await FixtureAsync(f => f.Api.Skills = f.Api.Skills.Select(skill => skill.SkillId == 911
            ? skill with { CooldownDuration = 400 } : skill).ToArray());
        await f.TickAsync(); Sequence(new[] { "D5" }, f.Keyboard.Keys, "advanced replaces opener");
        f.Confirm(911); await f.TickAsync();
        Sequence(new[] { "D5", "D1" }, f.Keyboard.Keys, "cooling advanced falls back to normal, never opening");
        // The shared cooldown clock uses the OS tick and deliberately holds a previously
        // observed future CD when a subsequent read becomes zero. Let that real CD expire.
        await Task.Delay(600);
        f.Api.Skills = f.Api.Skills.Select(skill => skill.SkillId == 911 ? skill with { CooldownEndTime = 0 } : skill).ToArray();
        await f.TickAsync(); Check(f.Keyboard.Keys.Count == 2, "ready advanced does not interrupt pending normal action");
        f.Confirm(AttackId); await f.TickAsync();
        Sequence(new[] { "D5", "D1", "D5" }, f.Keyboard.Keys, "advanced repeats after current action completes");
        Check(!f.Keyboard.Keys.Contains("D6"), "opening never runs on the configured target");
        f.Api.TargetName = "unlisted"; f.Api.TargetOwnServerObjectId++;
        await f.TickAsync(); Check(f.Keyboard.Keys.Last() == "D6", "new unlisted target restores original opener");
    }

    public static async Task RuntimeFallbacksAsync()
    {
        foreach (var change in new Action<SharedSkillAuditFixture>[]
        {
            f => f.Settings.AdvancedSkills.Enabled = false, f => f.Settings.AdvancedSkills.Elite.Enabled = false,
            f => f.Settings.AdvancedSkills.Elite.Skills.Clear(), f => f.Settings.AdvancedSkills.Elite.MonsterNames.Clear()
        })
        {
            using var f = await FixtureAsync(change); await f.TickAsync();
            Sequence(new[] { "D6" }, f.Keyboard.Keys, "nonmatching/disabled/empty module leaves original opening untouched");
        }
        using (var cooling = await FixtureAsync(f => f.Confirm(911)))
        {
            await cooling.TickAsync(); Sequence(new[] { "D1" }, cooling.Keyboard.Keys, "initially cooling advanced still excludes opening");
        }
        using (var maintenance = await FixtureAsync(f => f.ConfigureResource("hp")))
        {
            await maintenance.TickAsync(); Check(maintenance.Keyboard.Keys.Count > 0 && maintenance.Keyboard.Keys.All(key => key == "D2"),
                "existing HP maintenance retains its input and retry flow before attacks");
        }
        using (var unbound = await FixtureAsync(f => f.Settings.AdvancedSkills.Elite.Skills = new() { Node(99999) }))
        {
            await unbound.TickAsync(); Sequence(new[] { "D1" }, unbound.Keyboard.Keys, "configured but unbound skill skips safely without resurrecting opening");
        }
        using (var only = await FixtureAsync(f => f.ClearAttackTree()))
        {
            await only.TickAsync(); await only.TickAsync();
            Sequence(new[] { "D5" }, only.Keyboard.Keys, "advanced-only plan retains pending action with empty ordinary tree");
            only.Confirm(911); await only.TickAsync();
            Check(only.State.QuickbarSkills.PendingAction is null, "advanced-only release confirms instead of resetting each tick");
            only.Cancel();
            try { await only.TickAsync(); throw new Exception("stop ignored"); } catch (OperationCanceledException) { }
        }
    }

    public static async Task ExecutorChainAsync()
    {
        var bindings = new SkillKeyBindings(Bar, Learned);
        var ordinary = QuickbarSkillPlan.FromSettings(new() { ExecutionTree = new() { Node(21, Node(22, Node(23))), Node(31) } }, bindings);
        var plan = new AdvancedSkillCombatPlans(Settings(), ordinary, bindings, null).Resolve(Target());
        var state = new QuickbarSkillCombatState();
        var reader = new AvailabilityReader { Current = Available(advanced: false, second: false) };
        var keyboard = new RecordingKeyboardInput();
        var controller = new QuickbarSkillCombatController(keyboard, new TestClock());
        Task Tick() => controller.TickAsync(plan, state, Target(), reader,
            ids => Task.FromResult<IReadOnlyList<SkillSnapshot>>(Learned.Where(skill => ids.Contains(skill.SkillId)).ToArray()),
            new() { AttackWeaveEnabled = false }, cooldownReadiness: _ => SemiAutoSkillCooldownReadiness.Ready);
        await Tick(); Sequence(new[] { "D2" }, keyboard.Keys, "normal chain starts through unchanged executor");
        reader.Current = Available(second: false); await Tick();
        Sequence(new[] { "D2" }, keyboard.Keys, "advanced cannot replace an unconfirmed normal release");
        reader.Current = Available(normal: false, second: false, effective: 22, released: 21, releasedTime: 1); await Tick();
        Sequence(new[] { "D2" }, keyboard.Keys, "advanced cannot enter the protected continuation gap");
        reader.Current = reader.Current with { Slots = Available(second: false, effective: 22).Slots }; await Tick();
        reader.Current = Available(second: false, effective: 23, released: 22, releasedTime: 2); await Tick();
        reader.Current = Available(normal: false, second: false, released: 23, releasedTime: 3); await Tick();
        Sequence(new[] { "D2", "D2", "D2", "D1" }, keyboard.Keys, "three-link chain completes before the ready advanced skill");
        reader.Current = reader.Current with { CombatState = new(99, 99, 1, 9999, 100, 100) };
        await Tick(); Check(keyboard.Keys.Count == 4 && state.YieldToWorker, "advanced still respects the official target guard");
    }

    private sealed class AvailabilityReader : ISkillAvailabilitySnapshotReader
    {
        public SkillAvailabilitySnapshot Current { get; set; } = null!;
        private long version;
        public Task<PublishedGameSnapshot<SkillAvailabilitySnapshot>> ReadSkillAvailabilityAsync(long afterVersion = 0, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new PublishedGameSnapshot<SkillAvailabilitySnapshot>(++version, Current));
        }
    }
}
