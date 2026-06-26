using Roadhog.Application;
using Roadhog.Application.SemiAuto;
using Roadhog.Application.Workers;
using Roadhog.Core.Accounts;
using Roadhog.Core.Api;
using Roadhog.Core.Common;
using Roadhog.Core.Diagnostics;
using Roadhog.Core.Input;
using Roadhog.Core.Model;

var tests = new (string Name, Func<Task> Run)[]
{
    ("skill tree assigns keys by root order and chain children inherit root key", TestSkillTreeKeyMappingAsync),
    ("skill tree maps at most configured roots across the 22 supported keys", TestConfiguredRootKeyBoundaryAsync),
    ("combat tick presses trigger prefix then first ready root", TestCombatTickPressesPrefixThenReadyRootAsync),
    ("combat tick requests only configured skill ids", TestCombatTickRequestsOnlyConfiguredSkillIdsAsync),
    ("uncalibrated nonzero cooldown falls back to first configured root", TestUncalibratedNonzeroCooldownFallsBackToFirstRootAsync),
    ("calibrated nonzero cooldown skips cooling roots", TestCalibratedNonzeroCooldownSkipsCoolingRootsAsync),
    ("poll result advances root order", TestPollResultAdvancesRootOrderAsync),
    ("dp skill is skipped until dp value support exists", TestDpSkillSkippedAsync),
    ("chain repeats source until cooldown before pressing next stage", TestChainRepeatsSourceUntilCooldownAsync),
    ("chain selects ready configured sibling branch", TestChainSelectsReadyConfiguredSiblingBranchAsync),
    ("chain survives target gap and target switch", TestChainSurvivesTargetGapAsync),
    ("chain keeps root key and does not fall back in same tick when chain breaks", TestChainStrictOrderAsync)
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
    Console.WriteLine("All semi-auto skill tests passed.");
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
        new[] { "D2", "D3", "D4", "D5" },
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

    AssertSequence(new[] { "D2", "D3", "D4", "D1" }, keyboard.Keys.ToArray(), "uncalibrated stale cooldown should not block all roots");
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

    AssertSequence(new[] { "D2", "D3", "D4", "D5" }, keyboard.Keys.ToArray(), "calibrated active cooldown should skip D1");
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
    AssertSequence(new[] { "D2", "D3", "D4", "D1" }, keyboard.Keys.ToArray(), "first ready root");

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
    AssertSequence(new[] { "D2", "D3", "D4", "D5" }, keyboard.Keys.ToArray(), "second ready root");

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
    AssertSequence(new[] { "D2", "D3", "D4", "D5" }, keyboard.Keys.ToArray(), "chain child inherits D5");

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
    AssertSequence(new[] { "D2", "D3", "D4", "D6" }, keyboard.Keys.ToArray(), "poll-unavailable early roots allow D6");
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

    AssertEqual(0, keyboard.Keys.Count, "no key should be pressed when only dp is ready");
    var noneReady = logger.Entries.LastOrDefault(entry => entry.EventName == "semi_auto.skill.none_ready");
    AssertNotNull(noneReady, "none ready log");
    AssertContains("暗黑之惩戒 II[DP技能]@D0:dp-skip", Convert.ToString(noneReady!.Fields["reasons"]) ?? string.Empty, "dp skip reason");
}

static async Task TestChainRepeatsSourceUntilCooldownAsync()
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
    AssertSequence(new[] { "D2", "D3", "D4", "D6" }, keyboard.Keys.ToArray(), "first stage root press");

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
    AssertSequence(new[] { "D2", "D3", "D4", "D6" }, keyboard.Keys.ToArray(), "chain repeats source while source remains ready");
    AssertEqual(plan.Roots.Single(root => root.SkillId == 6).Name, LastPressedSkill(logger), "repeated source skill");

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
    AssertSequence(new[] { "D2", "D3", "D4", "D6" }, keyboard.Keys.ToArray(), "second stage waits for source cooldown");
    AssertEqual(plan.Roots.Single(root => root.SkillId == 6).Children[0].Name, LastPressedSkill(logger), "second stage skill");
}

static async Task TestChainSelectsReadyConfiguredSiblingBranchAsync()
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

    AssertSequence(new[] { "D2", "D3", "D4", "D6" }, keyboard.Keys.ToArray(), "sibling chain key");
    AssertEqual(root.Children[1].Name, LastPressedSkill(logger), "sibling chain skill");
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
    AssertSequence(new[] { "D2", "D3", "D4", "D6" }, keyboard.Keys.ToArray(), "source press before target gap");

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
    AssertSequence(new[] { "D2", "D3", "D4", "D6" }, keyboard.Keys.ToArray(), "target switch should keep chain next stage");
    AssertEqual(plan.Roots.Single(root => root.SkillId == 6).Children[0].Name, LastPressedSkill(logger), "target switch second stage skill");
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
    AssertSequence(new[] { "D2", "D3", "D4", "D6" }, keyboard.Keys.ToArray(), "root chain start order");

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
    AssertSequence(new[] { "D2", "D3", "D4", "D6" }, keyboard.Keys.ToArray(), "chain second stage order");

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
    AssertEqual(0, keyboard.Keys.Count, "broken chain must not fall back to other roots in same tick");

    var ended = logger.Entries.LastOrDefault(entry => entry.EventName == "semi_auto.chain.ended");
    AssertNotNull(ended, "chain ended log");
    AssertEqual("node_not_ready", Convert.ToString(ended!.Fields["reason"]), "chain ended reason");
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
    IRoadhogLogger logger)
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
        new AccountRuntimeManager(logger),
        new AccountWorkerOptions(),
        CancellationToken.None);
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

static void AssertNotNull(object? value, string label)
{
    if (value is null)
    {
        throw new InvalidOperationException(label + " should not be null");
    }
}

static void AssertContains(string expected, string actual, string label)
{
    if (!actual.Contains(expected, StringComparison.Ordinal))
    {
        throw new InvalidOperationException(label + ": expected to contain [" + expected + "] but got [" + actual + "]");
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

    public Task<OperationResult> PressKeyAsync(
        string key,
        TimeSpan holdDuration,
        CancellationToken cancellationToken = default)
    {
        Keys.Add(key);
        return Task.FromResult(OperationResult.Ok());
    }
}

sealed class FakeGameApi : IRoadhogScopedGameApi
{
    public IReadOnlyList<SkillSnapshot> Skills { get; set; } = Array.Empty<SkillSnapshot>();

    public IReadOnlyList<uint>? LastRequestedSkillIds { get; private set; }

    public ushort TargetEntityId { get; set; } = 100;

    public uint TargetCurrentHp { get; set; } = 1000;

    public Task<OperationResult<PlayerSnapshot>> ReadPlayerAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(OperationResult<PlayerSnapshot>.Fail("not used"));
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
            1000,
            null,
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
        return Task.FromResult(OperationResult<IReadOnlyList<WorldObjectSnapshot>>.Fail("not used"));
    }
}
