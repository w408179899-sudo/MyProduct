local T = require("tests.test_framework")

local function custom_stationary_allowed(primary_mode, sync_enabled, combat_mode)
    return sync_enabled == true
        and primary_mode == 1
        and combat_mode == 1
end

local function quest_grind_allowed(primary_mode, allow_grind, active_grind, combat_mode)
    local mode_id = ({ "combat", "leveling" })[primary_mode] or ""

    return mode_id == "leveling"
        and allow_grind == true
        and active_grind == true
        and combat_mode == 1
end

local function clear_modules()
    package.loaded["aion.main_quest_combat_guard"] = nil
end

local function load_guard()
    clear_modules()
    return require("aion.main_quest_combat_guard")
end

local function run()
    T.reset()
    T.log("\n=== aion leveling combat gate tests ===")

    T.test("custom combat stationary remains enabled", function()
        T.assert_eq(custom_stationary_allowed(1, true, 1), true)
    end)

    T.test("normal stationary gate does not accept leveling mode", function()
        T.assert_eq(custom_stationary_allowed(2, true, 1), false)
    end)

    T.test("quest grind adapter waits until active", function()
        T.assert_eq(quest_grind_allowed(2, true, false, 1), false)
        T.assert_eq(quest_grind_allowed(2, true, true, 1), true)
    end)

    T.test("quest grind adapter respects allow grind and stationary mode", function()
        T.assert_eq(quest_grind_allowed(2, false, true, 1), false)
        T.assert_eq(quest_grind_allowed(2, true, true, 2), false)
        T.assert_eq(quest_grind_allowed(1, true, true, 1), false)
    end)

    T.test("main quest combat guard blocks interruptible actions", function()
        local guard = load_guard()
        local block, reason = guard.shouldBlock({
            action = { name = "InteractNpc", params = { stage = "quest_20611_mission_npc" } },
            live_target = true,
            live_reason = "tracked-target",
        })

        T.assert_eq(block, true)
        T.assert_eq(reason, "tracked-target")
    end)

    T.test("main quest combat guard blocks recent damage before teleport", function()
        local guard = load_guard()
        local block, reason = guard.shouldBlock({
            action = { name = "QuestTeleport", params = { stage = "quest_20611_level_move" } },
            recent_damage = true,
        })

        T.assert_eq(block, true)
        T.assert_eq(reason, "recent-damage")
    end)

    T.test("main quest combat guard does not block safe or combat actions", function()
        local guard = load_guard()
        local wait_block = guard.shouldBlock({
            action = { name = "WaitPositionChanged" },
            live_target = true,
        })
        local grind_block = guard.shouldBlock({
            action = { name = "WaitLevelGrind" },
            live_target = true,
        })

        T.assert_eq(wait_block, false)
        T.assert_eq(grind_block, false)
    end)

    clear_modules()
    return T.report("aion_leveling_combat_gate")
end

return { run = run }
