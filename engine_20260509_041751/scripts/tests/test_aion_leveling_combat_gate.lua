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

local function quest_grind_allowed_with_guard(primary_mode, allow_grind, active_grind, guard_active, combat_mode)
    local mode_id = ({ "combat", "leveling" })[primary_mode] or ""
    local grind_authorized = allow_grind == true and active_grind == true
    local guard_authorized = guard_active == true

    return mode_id == "leveling"
        and (grind_authorized or guard_authorized)
        and combat_mode == 1
end

local function stop_level_grind_tail(state)
    state.active_grind = false
    state.guard_active = false
    state.last_damage_at = 0
    state.last_hp = 0
    state.target_obj = 0
    state.target_name = ""
    state.combat_mode = ""
end

local function effective_combat_radius(base_radius, quest_grind)
    local radius = tonumber(base_radius) or 35
    if quest_grind == true and radius < 60 then
        radius = 60
    end
    return radius
end

local function target_is_damaged(hp, mhp)
    hp = tonumber(hp) or 0
    mhp = tonumber(mhp) or 0
    return mhp > 0 and hp > 0 and hp < mhp
end

local function close_alive_priority(dead, hp, dist, quest_grind)
    return quest_grind == true
        and dead ~= true
        and (tonumber(hp) or 0) > 0
        and (tonumber(dist) or 9999) <= 5
end

local function reject_new_target_as_claimed(hp, mhp, quest_grind)
    if quest_grind ~= true or not target_is_damaged(hp, mhp) then
        return false
    end
    return true
end

local function continue_tracked_target(tracked)
    return tracked == true
end

local function should_stop_for_20613_flow(active_grind, action_authorizes_grind, completed_20613)
    return active_grind == true
        and action_authorizes_grind ~= true
        and completed_20613 == true
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

    T.test("completed 20613 flags do not stop authorized later level grind blocks", function()
        T.assert_eq(should_stop_for_20613_flow(true, true, true), false)
        T.assert_eq(should_stop_for_20613_flow(true, false, true), true)
        T.assert_eq(should_stop_for_20613_flow(false, false, true), false)
    end)

    T.test("level grind completion clears combat tail before next task block", function()
        local state = {
            active_grind = true,
            guard_active = true,
            last_damage_at = 100,
            last_hp = 10,
            target_obj = 123,
            target_name = "old target",
            combat_mode = "stationary",
        }

        stop_level_grind_tail(state)

        T.assert_eq(state.active_grind, false)
        T.assert_eq(state.guard_active, false)
        T.assert_eq(state.last_damage_at, 0)
        T.assert_eq(state.last_hp, 0)
        T.assert_eq(state.target_obj, 0)
        T.assert_eq(state.target_name, "")
        T.assert_eq(quest_grind_allowed_with_guard(2, true, state.active_grind, state.guard_active, 1), false)
    end)

    T.test("quest grind radius has isolated 60m floor", function()
        T.assert_eq(effective_combat_radius(35, true), 60)
        T.assert_eq(effective_combat_radius(80, true), 80)
        T.assert_eq(effective_combat_radius(35, false), 35)
    end)

    T.test("quest grind skips damaged mobs only before locking", function()
        T.assert_eq(reject_new_target_as_claimed(90, 100, true), true)
        T.assert_eq(reject_new_target_as_claimed(100, 100, true), false)
        T.assert_eq(reject_new_target_as_claimed(90, 100, false), false)
        T.assert_eq(continue_tracked_target(true), true)
    end)

    T.test("quest grind prioritizes close alive mobs only in quest grind", function()
        T.assert_eq(close_alive_priority(false, 100, 2, true), true)
        T.assert_eq(close_alive_priority(false, 90, 2, true), true)
        T.assert_eq(close_alive_priority(false, 100, 6, true), false)
        T.assert_eq(close_alive_priority(true, 100, 2, true), false)
        T.assert_eq(close_alive_priority(false, 0, 2, true), false)
        T.assert_eq(close_alive_priority(false, 100, 2, false), false)
        T.assert_eq(reject_new_target_as_claimed(90, 100, true), true)
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

    T.test("main quest combat guard lets explicit story handoff preempt stale target", function()
        local guard = load_guard()
        local block, reason = guard.shouldBlock({
            action = { name = "NavigateToNpc", params = { stage = "temple_npc", preempt_combat = true } },
            live_target = true,
            live_reason = "tracked-target",
            recent_damage = false,
            pending_loot = false,
        })

        T.assert_eq(block, false)
        T.assert_eq(reason, "story-action-preempts-combat")
    end)

    T.test("main quest combat guard blocks story handoff without explicit preempt flag", function()
        local guard = load_guard()
        local block, reason = guard.shouldBlock({
            action = { name = "NavigateToNpc", params = { quest_id = 20590, stage = "temple_npc" } },
            live_target = true,
            live_reason = "tracked-target",
            recent_damage = false,
            pending_loot = false,
        })

        T.assert_eq(block, true)
        T.assert_eq(reason, "tracked-target")
    end)

    T.test("main quest combat guard still blocks story handoff when recently damaged", function()
        local guard = load_guard()
        local block, reason = guard.shouldBlock({
            action = { name = "NavigateToNpc", params = { stage = "temple_npc", preempt_combat = true } },
            live_target = true,
            live_reason = "tracked-target",
            recent_damage = true,
            pending_loot = false,
        })

        T.assert_eq(block, true)
        T.assert_eq(reason, "tracked-target")
    end)

    T.test("main quest combat guard still blocks story handoff when loot is pending", function()
        local guard = load_guard()
        local block, reason = guard.shouldBlock({
            action = { name = "NavigateToNpc", params = { stage = "temple_npc", preempt_combat = true } },
            live_target = true,
            live_reason = "tracked-target",
            recent_damage = false,
            pending_loot = true,
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
