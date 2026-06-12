local T = require("tests.test_framework")

local function clear_modules()
    package.loaded["aion.main_quest_20611"] = nil
end

local function load_module()
    clear_modules()
    return require("aion.main_quest_20611")
end

local CURRENT_BLUE_TASK = 24341
local LEGACY_BLUE_TASK = 24340

local function remote_reward_quest(id)
    return { id = id or CURRENT_BLUE_TASK, tab = 1, status_code = 4, req_count = 5 }
end

local function remote_active_quest(step, id)
    return { id = id or CURRENT_BLUE_TASK, tab = 1, status_code = 3, req_count = step or 0 }
end

local function near_char()
    return { x = 194.60, y = 2689.90, z = 300.60 }
end

local function far_char()
    return { x = 223.17, y = 2680.63, z = 295.25 }
end

local function mission_npc_char()
    return { x = 586.19, y = 2467.40, z = 278.62, level = 10 }
end

local function obelisk_char()
    return { x = 584.72, y = 2466.97, z = 278.62, level = 10 }
end

local function target_npc_char()
    return { x = 589.70, y = 2450.37, z = 278.38, level = 10 }
end

local function hotspot_reward_char()
    return { x = 491.90, y = 2299.41, z = 248.75, level = 10 }
end

local function hotspot_reward_level11_char()
    return { x = 491.90, y = 2299.41, z = 248.75, level = 11 }
end

local function quest_20612_start_point_char()
    return { x = 477.20, y = 2304.30, z = 250.70, level = 11 }
end

local function quest_20612_start_npc_char()
    return { x = 493.00, y = 2299.00, z = 248.50, level = 11 }
end

local function quest_20612_reward_npc_char()
    return { x = 1047.94, y = 2203.23, z = 262.36, level = 11 }
end

local function quest_20613_start_npc_char()
    return { x = 1048.52, y = 2198.80, z = 262.33, level = 14 }
end

local function quest_20613_after_start_reward_npc_char()
    return { x = 944.00, y = 1701.69, z = 259.66, level = 14 }
end

local function quest_20614_start_npc_char()
    return { x = 945.91, y = 1702.50, z = 259.62, level = 17 }
end

local function quest_20614_reward_npc_char()
    return { x = 600.78, y = 1480.36, z = 299.94, level = 17 }
end

local function quest_20615_level20_grind_char(level)
    return { x = 666.23, y = 1535.34, z = 294.01, level = level or 17 }
end

local function quest_20615_target_npc_char(level)
    return { x = 587.72, y = 2451.15, z = 278.38, level = level or 20 }
end

local function quest_20615_morheim_npc_char(level)
    return { x = 224.60, y = 2416.30, z = 454.56, level = level or 20 }
end

local function quest_20620_start_npc_char(level)
    return { x = 224.60, y = 2416.30, z = 454.56, level = level or 20 }
end

local function quest_20620_after_teleport_npc_char(level)
    return { x = 233.55, y = 2324.88, z = 446.17, level = level or 20 }
end

local function quest_20620_after_stigma_npc_char(level)
    return { x = 268.68, y = 2339.90, z = 443.74, level = level or 20 }
end

local function quest_20620_obelisk_char(level)
    return { x = 269.49, y = 2340.11, z = 443.74, level = level or 20 }
end

local function quest_20620_after_obelisk_npc_char(level)
    return { x = 194.74, y = 2269.09, z = 438.87, level = level or 20 }
end

local function quest_20621_level22_grind_char(level)
    return { x = 174.508, y = 2298.396, z = 438.510, level = level or 20 }
end

local function post_20612_level14_grind_char(level)
    return { x = 1093.60, y = 2247.10, z = 254.25, level = level or 11 }
end

local function run()
    T.reset()
    T.log("\n=== aion main quest 20611 tests ===")

    T.test("moves to grind point when active and far", function()
        local quest = load_module()
        local next_action = quest.nextAction({
            quests = { remote_active_quest() },
            char = far_char(),
            big_map_id = 220010000,
        })

        T.assert_eq(next_action.name, "NavigateToGrindPoint")
        T.assert_eq(next_action.params.quest_id, CURRENT_BLUE_TASK)
        T.assert_eq(next_action.params.stage, "quest_20611_grind")
        T.assert_nil(next_action.params.requires_combat)
        T.assert_nil(next_action.params.task_step)
        T.assert_eq(next_action.params.x, 194.491)
        T.assert_eq(next_action.params.y, 2689.982)
        T.assert_eq(next_action.params.z, 300.625)
    end)

    T.test("grinds until earliest level blocked yellow mission requirement", function()
        local quest = load_module()
        local next_action = quest.nextAction({
            quests = {
                { id = 20615, tab = 0, status_code = 6, req_count = 0, seq = 5, lv_num = 20 },
                { id = 20611, tab = 0, status_code = 6, req_count = 0, seq = 1, lv_num = 8 },
                { id = 20612, tab = 0, status_code = 6, req_count = 0, seq = 2, lv_num = 11 },
            },
            char = { x = 190.96, y = 2693.78, z = 300.62, level = 6 },
            big_map_id = 220010000,
        })

        T.assert_eq(next_action.name, "StartStationaryGrind")
        T.assert_eq(next_action.params.quest_id, 20611)
        T.assert_eq(next_action.params.stage, "quest_20611_level_grind")
        T.assert_eq(next_action.params.requires_combat, true)
        T.assert_eq(next_action.params.task_step, "grind")
        T.assert_eq(next_action.params.required_level, 8)
        T.assert_eq(next_action.params.char_level, 6)
        T.assert_eq(next_action.params.until_level, 8)
    end)

    T.test("opens hotspot reward npc instead of starting next level grind", function()
        local quest = load_module()
        local next_action = quest.nextAction({
            quests = {
                { id = 20611, tab = 0, status_code = 4, req_count = 3, seq = 0, lv_num = 8 },
                { id = 20612, tab = 0, status_code = 6, req_count = 0, seq = 2, lv_num = 11 },
            },
            char = hotspot_reward_char(),
            big_map_id = 220010000,
        }, {
            completed_20611_hotspot_teleport = true,
        })

        T.assert_eq(next_action.name, "InteractNpc")
        T.assert_eq(next_action.params.quest_id, 20611)
        T.assert_eq(next_action.params.quest_step, 3)
        T.assert_eq(next_action.params.stage, "quest_20611_hotspot_reward_npc")
        T.assert_eq(next_action.params.interact_id, 2147515597)
        T.assert_eq(next_action.params.npc_name_key, "MQ20611_NPC_004_HOTSPOT_REWARD")
        T.assert_eq(next_action.params.allow_interact_id_fallback, true)
    end)

    T.test("starts quest 20612 level grind after hotspot reward below level 11", function()
        local quest = load_module()
        local next_action = quest.nextAction({
            quests = {
                { id = 20612, tab = 0, status_code = 6, req_count = 0, seq = 2, lv_num = 11 },
            },
            char = hotspot_reward_char(),
            big_map_id = 220010000,
        }, {
            completed_20611_hotspot_reward = true,
        })

        T.assert_eq(next_action.name, "StartStationaryGrind")
        T.assert_eq(next_action.params.quest_id, 20612)
        T.assert_eq(next_action.params.stage, "quest_20612_level_grind")
        T.assert_eq(next_action.params.requires_combat, true)
        T.assert_eq(next_action.params.task_step, "grind")
        T.assert_eq(next_action.params.required_level, 11)
        T.assert_eq(next_action.params.char_level, 10)
        T.assert_eq(next_action.params.until_level, 11)
    end)

    T.test("waits while quest 20612 level grind is active", function()
        local quest = load_module()
        local next_action = quest.nextAction({
            quests = {
                { id = 20612, tab = 0, status_code = 6, req_count = 0, seq = 2, lv_num = 11 },
            },
            char = hotspot_reward_char(),
            big_map_id = 220010000,
        }, {
            completed_20611_hotspot_reward = true,
            active_20611_grind = true,
            active_20611_grind_stage = "quest_20612_level_grind",
            level_grind_quest_id = 20612,
            level_grind_required_level = 11,
        })

        T.assert_eq(next_action.name, "WaitLevelGrind")
        T.assert_eq(next_action.params.quest_id, 20612)
        T.assert_eq(next_action.params.stage, "quest_20612_level_grind")
        T.assert_eq(next_action.params.requires_combat, true)
        T.assert_eq(next_action.params.task_step, "grind")
        T.assert_eq(next_action.params.required_level, 11)
        T.assert_eq(next_action.params.char_level, 10)
    end)

    T.test("moves to quest 20612 start point after reaching level 11", function()
        local quest = load_module()
        local next_action = quest.nextAction({
            quests = {
                { id = 20612, tab = 0, status_code = 6, req_count = 0, seq = 2, lv_num = 11 },
            },
            char = hotspot_reward_level11_char(),
            big_map_id = 220010000,
        }, {
            completed_20611_hotspot_reward = true,
            active_20611_grind = true,
            active_20611_grind_stage = "quest_20612_level_grind",
            level_grind_quest_id = 20612,
            level_grind_required_level = 11,
        })

        T.assert_eq(next_action.name, "NavigateToNpc")
        T.assert_eq(next_action.params.quest_id, 20612)
        T.assert_eq(next_action.params.stage, "quest_20612_start_npc")
        T.assert_eq(next_action.params.x, 477.137)
        T.assert_eq(next_action.params.y, 2304.421)
        T.assert_eq(next_action.params.z, 250.734)
    end)

    T.test("moves to quest 20612 start point before talking to npc", function()
        local quest = load_module()
        local next_action = quest.nextAction({
            quests = {
                { id = 20612, tab = 0, status_code = 3, req_count = 0, seq = 2, lv_num = 11 },
                { id = 20613, tab = 0, status_code = 6, req_count = 0, seq = 3, lv_num = 14 },
            },
            char = hotspot_reward_level11_char(),
            big_map_id = 220010000,
        }, {
            completed_20611_hotspot_reward = true,
        })

        T.assert_eq(next_action.name, "NavigateToNpc")
        T.assert_eq(next_action.params.quest_id, 20612)
        T.assert_eq(next_action.params.quest_step, 0)
        T.assert_eq(next_action.params.stage, "quest_20612_start_npc")
        T.assert_eq(next_action.params.x, 477.137)
        T.assert_eq(next_action.params.y, 2304.421)
        T.assert_eq(next_action.params.z, 250.734)
        T.assert_eq(next_action.params.interact_id, 2147515597)
    end)

    T.test("moves before clicking quest 20612 dialog when not at recorded point", function()
        local quest = load_module()
        local next_action = quest.nextAction({
            quests = {
                { id = 20612, tab = 0, status_code = 3, req_count = 0, seq = 2, lv_num = 11 },
            },
            char = hotspot_reward_level11_char(),
            big_map_id = 220010000,
            dialog = {
                npc_dialog_id = 2147515597,
                dialog_content_id = 10,
                quest_id = 0,
                type_text = "select_quest",
            },
        })

        T.assert_eq(next_action.name, "NavigateToNpc")
        T.assert_eq(next_action.params.quest_id, 20612)
        T.assert_eq(next_action.params.stage, "quest_20612_start_npc")
        T.assert_eq(next_action.params.x, 477.137)
        T.assert_eq(next_action.params.y, 2304.421)
        T.assert_eq(next_action.params.z, 250.734)
    end)

    T.test("moves from quest 20612 start point to npc before interacting", function()
        local quest = load_module()
        local next_action = quest.nextAction({
            quests = {
                { id = 20612, tab = 0, status_code = 3, req_count = 0, seq = 2, lv_num = 11 },
            },
            char = quest_20612_start_point_char(),
            big_map_id = 220010000,
        })

        T.assert_eq(next_action.name, "NavigateToNpc")
        T.assert_eq(next_action.params.quest_id, 20612)
        T.assert_eq(next_action.params.stage, "quest_20612_start_npc")
        T.assert_eq(next_action.params.x, 493.15)
        T.assert_eq(next_action.params.y, 2298.88)
        T.assert_eq(next_action.params.z, 248.42)
        T.assert_eq(next_action.params.mark_20612_start_point_reached, true)
    end)

    T.test("opens quest 20612 start npc after reaching npc", function()
        local quest = load_module()
        local next_action = quest.nextAction({
            quests = {
                { id = 20612, tab = 0, status_code = 3, req_count = 0, seq = 2, lv_num = 11 },
            },
            char = quest_20612_start_npc_char(),
            big_map_id = 220010000,
        }, {
            reached_20612_start_point = true,
        })

        T.assert_eq(next_action.name, "InteractNpc")
        T.assert_eq(next_action.params.quest_id, 20612)
        T.assert_eq(next_action.params.stage, "quest_20612_start_npc")
        T.assert_eq(next_action.params.interact_id, 2147515597)
        T.assert_eq(next_action.params.allow_interact_id_fallback, true)
        T.assert_eq(next_action.params.mark_20612_start_point_reached, true)
    end)

    T.test("continuous x-clicks quest 20612 start npc select list", function()
        local quest = load_module()
        local next_action = quest.nextAction({
            quests = {
                { id = 20612, tab = 0, status_code = 3, req_count = 0, seq = 2, lv_num = 11 },
            },
            char = quest_20612_start_npc_char(),
            big_map_id = 220010000,
            dialog = {
                npc_dialog_id = 2147515597,
                dialog_content_id = 10,
                quest_id = 0,
                type_text = "select_quest",
            },
        }, {
            reached_20612_start_point = true,
        })

        T.assert_eq(next_action.name, "ClickDialogXContinuous")
        T.assert_eq(next_action.params.quest_id, 20612)
        T.assert_eq(next_action.params.quest_step, 0)
        T.assert_eq(next_action.params.stage, "quest_20612_start_npc")
        T.assert_eq(next_action.params.expected_content_id, 10)
        T.assert_eq(next_action.params.content_id, 10)
        T.assert_eq(next_action.params.type_text, "select_quest")
        T.assert_eq(next_action.params.click_x, 25)
        T.assert_eq(next_action.params.interact_id, 2147515597)
    end)

    T.test("prioritizes opened quest 20612 start dialog before post-start teleport", function()
        local quest = load_module()
        local next_action = quest.nextAction({
            quests = {
                { id = 20612, tab = 0, status_code = 3, req_count = 1, seq = 2, lv_num = 11 },
            },
            char = quest_20612_start_npc_char(),
            big_map_id = 220010000,
            dialog = {
                npc_dialog_id = 2147515597,
                dialog_content_id = 10,
                quest_id = 0,
                type_text = "select_quest",
            },
        }, {
            reached_20612_start_point = true,
        })

        T.assert_eq(next_action.name, "ClickDialogXContinuous")
        T.assert_eq(next_action.params.quest_id, 20612)
        T.assert_eq(next_action.params.stage, "quest_20612_start_npc")
        T.assert_eq(next_action.params.expected_content_id, 10)
        T.assert_eq(next_action.params.interact_id, 2147515597)
        T.assert_eq(next_action.params.mark_20612_start_point_reached, true)
    end)

    T.test("opens current tracker after quest 20612 start npc dialog is completed", function()
        local quest = load_module()
        local next_action = quest.nextAction({
            quests = {
                { id = 20612, tab = 0, status_code = 3, req_count = 0, seq = 2, lv_num = 11 },
                { id = 20613, tab = 0, status_code = 6, req_count = 0, seq = 3, lv_num = 14 },
            },
            char = quest_20612_start_npc_char(),
            big_map_id = 220010000,
        }, {
            completed_20612_start_dialog = true,
        })

        T.assert_eq(next_action.name, "ClickUiControl")
        T.assert_eq(next_action.params.quest_id, 20612)
        T.assert_eq(next_action.params.stage, "quest_20611_indicator_title")
        T.assert_eq(next_action.params.parent, "quest_indicator_dialog")
        T.assert_eq(next_action.params.name, "prototype")
    end)

    T.test("calls quest 20612 teleport after current tracker panel opens", function()
        local quest = load_module()
        local next_action = quest.nextAction({
            quests = {
                { id = 20612, tab = 0, status_code = 3, req_count = 1, seq = 2, lv_num = 11 },
            },
            char = quest_20612_start_npc_char(),
            big_map_id = 220010000,
            ui = {
                quest_panel_visible = true,
            },
        }, {
            completed_20612_start_dialog = true,
            clicked_20611_indicator_title = true,
        })

        T.assert_eq(next_action.name, "QuestTeleport")
        T.assert_eq(next_action.params.quest_id, 20612)
        T.assert_eq(next_action.params.quest_step, 1)
        T.assert_eq(next_action.params.stage, "quest_20612_task_teleport")
        T.assert_eq(next_action.params.open_panel_key, false)
        T.assert_eq(next_action.params.require_panel_visible, true)
        T.assert_eq(next_action.params.wait_teleport, true)
    end)

    T.test("opens current tracker instead of grinding when quest 20612 is done and 20613 is level blocked", function()
        local quest = load_module()
        local next_action = quest.nextAction({
            quests = {
                { id = 20612, tab = 0, status_code = 4, req_count = 0, seq = 2, lv_num = 11 },
                { id = 20613, tab = 0, status_code = 6, req_count = 0, seq = 3, lv_num = 14 },
            },
            char = quest_20612_start_npc_char(),
            big_map_id = 220010000,
        }, {
            completed_20612_start_dialog = true,
        })

        T.assert_eq(next_action.name, "ClickUiControl")
        T.assert_eq(next_action.params.quest_id, 20613)
        T.assert_eq(next_action.params.stage, "quest_20611_indicator_title")
    end)

    T.test("calls next tracked quest teleport after quest 20612 is done before grinding 20613", function()
        local quest = load_module()
        local next_action = quest.nextAction({
            quests = {
                { id = 20612, tab = 0, status_code = 4, req_count = 0, seq = 2, lv_num = 11 },
                { id = 20613, tab = 0, status_code = 6, req_count = 0, seq = 3, lv_num = 14 },
            },
            char = quest_20612_start_npc_char(),
            big_map_id = 220010000,
            ui = {
                quest_panel_visible = true,
            },
        }, {
            completed_20612_start_dialog = true,
            clicked_20611_indicator_title = true,
        })

        T.assert_eq(next_action.name, "QuestTeleport")
        T.assert_eq(next_action.params.quest_id, 20613)
        T.assert_eq(next_action.params.after_quest_id, 20612)
        T.assert_eq(next_action.params.stage, "quest_20612_task_teleport")
        T.assert_eq(next_action.params.open_panel_key, false)
        T.assert_eq(next_action.params.require_panel_visible, true)
        T.assert_eq(next_action.params.wait_teleport, true)
    end)

    T.test("resumes quest 20612 task teleport at step 1 after restart", function()
        local quest = load_module()
        local next_action = quest.nextAction({
            quests = {
                { id = 20612, tab = 0, status_code = 3, req_count = 1, seq = 2, lv_num = 11 },
            },
            char = quest_20612_start_npc_char(),
            big_map_id = 220010000,
        })

        T.assert_eq(next_action.name, "ClickUiControl")
        T.assert_eq(next_action.params.quest_id, 20612)
        T.assert_eq(next_action.params.stage, "quest_20611_indicator_title")
    end)

    T.test("waits for quest 20612 task teleport landing", function()
        local quest = load_module()
        local next_action = quest.nextAction({
            quests = {
                { id = 20612, tab = 0, status_code = 3, req_count = 1, seq = 2, lv_num = 11 },
            },
            char = quest_20612_start_npc_char(),
            big_map_id = 220010000,
        }, {
            waiting_teleport = true,
            teleport_stage = "quest_20612_task_teleport",
            teleport_start_pos = quest_20612_start_npc_char(),
            teleport_start_big_map_id = 220010000,
        })

        T.assert_eq(next_action.name, "WaitPositionChanged")
        T.assert_eq(next_action.params.quest_id, 20612)
        T.assert_eq(next_action.params.stage, "quest_20612_task_teleport")
    end)

    T.test("waits for post quest 20612 teleport using actual tracked quest id", function()
        local quest = load_module()
        local next_action = quest.nextAction({
            quests = {
                { id = 20612, tab = 0, status_code = 4, req_count = 0, seq = 2, lv_num = 11 },
                { id = 20613, tab = 0, status_code = 6, req_count = 0, seq = 3, lv_num = 14 },
            },
            char = quest_20612_start_npc_char(),
            big_map_id = 220010000,
        }, {
            waiting_teleport = true,
            teleport_quest_id = 20613,
            teleport_stage = "quest_20612_task_teleport",
            teleport_start_pos = quest_20612_start_npc_char(),
            teleport_start_big_map_id = 220010000,
        })

        T.assert_eq(next_action.name, "WaitPositionChanged")
        T.assert_eq(next_action.params.quest_id, 20613)
        T.assert_eq(next_action.params.stage, "quest_20612_task_teleport")
    end)

    T.test("completes quest 20612 task teleport after position changes", function()
        local quest = load_module()
        local next_action = quest.nextAction({
            quests = {
                { id = 20612, tab = 0, status_code = 3, req_count = 1, seq = 2, lv_num = 11 },
            },
            char = { x = 520.00, y = 2260.00, z = 249.00, level = 11 },
            big_map_id = 220010000,
        }, {
            waiting_teleport = true,
            teleport_stage = "quest_20612_task_teleport",
            teleport_start_pos = quest_20612_start_npc_char(),
            teleport_start_big_map_id = 220010000,
        })

        T.assert_eq(next_action.name, "CompleteQuestTeleport")
        T.assert_eq(next_action.params.quest_id, 20612)
        T.assert_eq(next_action.params.stage, "quest_20612_task_teleport")
    end)

    T.test("opens quest 20612 reward npc after task teleport lands", function()
        local quest = load_module()
        local next_action = quest.nextAction({
            quests = {
                { id = 20612, tab = 0, status_code = 4, req_count = 0, seq = 2, lv_num = 11 },
                { id = 20613, tab = 0, status_code = 6, req_count = 0, seq = 3, lv_num = 14 },
            },
            char = quest_20612_reward_npc_char(),
            big_map_id = 220010000,
        }, {
            completed_20612_start_dialog = true,
            completed_20612_task_teleport = true,
        })

        T.assert_eq(next_action.name, "InteractNpc")
        T.assert_eq(next_action.params.quest_id, 20612)
        T.assert_eq(next_action.params.stage, "quest_20612_reward_npc")
        T.assert_eq(next_action.params.interact_id, 2147495609)
        T.assert_eq(next_action.params.allow_interact_id_fallback, true)
    end)

    T.test("opens quest 20612 reward npc after restart at reward npc without teleport flag", function()
        local quest = load_module()
        local next_action = quest.nextAction({
            quests = {
                { id = 20612, tab = 0, status_code = 4, req_count = 0, seq = 2, lv_num = 11 },
                { id = 20613, tab = 0, status_code = 6, req_count = 0, seq = 3, lv_num = 14 },
            },
            char = quest_20612_reward_npc_char(),
            big_map_id = 220010000,
        }, {
            completed_20612_start_dialog = true,
        })

        T.assert_eq(next_action.name, "InteractNpc")
        T.assert_eq(next_action.params.quest_id, 20612)
        T.assert_eq(next_action.params.stage, "quest_20612_reward_npc")
        T.assert_eq(next_action.params.interact_id, 2147495609)
    end)

    T.test("continuous x-clicks opened quest 20612 reward npc dialog", function()
        local quest = load_module()
        local next_action = quest.nextAction({
            quests = {
                { id = 20612, tab = 0, status_code = 4, req_count = 0, seq = 2, lv_num = 11 },
                { id = 20613, tab = 0, status_code = 6, req_count = 0, seq = 3, lv_num = 14 },
            },
            char = quest_20612_reward_npc_char(),
            big_map_id = 220010000,
            dialog = {
                npc_dialog_id = 2147495609,
                dialog_content_id = 10002,
                quest_id = 20612,
                type_text = "select_success",
            },
        }, {
            completed_20612_start_dialog = true,
        })

        T.assert_eq(next_action.name, "ClickDialogXContinuous")
        T.assert_eq(next_action.params.quest_id, 20612)
        T.assert_eq(next_action.params.stage, "quest_20612_reward_npc")
        T.assert_eq(next_action.params.expected_content_id, 10002)
        T.assert_eq(next_action.params.content_id, 10002)
        T.assert_eq(next_action.params.type_text, "select_success")
        T.assert_eq(next_action.params.click_x, 25)
        T.assert_eq(next_action.params.interact_id, 2147495609)
    end)

    T.test("moves to fixed level 14 grind point after quest 20612 reward dialog", function()
        local quest = load_module()
        local next_action = quest.nextAction({
            quests = {
                { id = 20612, tab = 0, status_code = 4, req_count = 0, seq = 2, lv_num = 11 },
                { id = 20613, tab = 0, status_code = 6, req_count = 0, seq = 3, lv_num = 14 },
            },
            char = quest_20612_reward_npc_char(),
            big_map_id = 220010000,
        }, {
            completed_20612_start_dialog = true,
            completed_20612_task_teleport = true,
            completed_20612_reward_dialog = true,
        })

        T.assert_eq(next_action.name, "NavigateToGrindPoint")
        T.assert_eq(next_action.params.quest_id, 20613)
        T.assert_eq(next_action.params.stage, "quest_20613_level14_grind")
        T.assert_eq(next_action.params.x, 1093.552)
        T.assert_eq(next_action.params.y, 2247.044)
        T.assert_eq(next_action.params.z, 254.250)
        T.assert_eq(next_action.params.required_level, 14)
        T.assert_eq(next_action.params.char_level, 11)
    end)

    T.test("starts fixed post quest 20612 grind when at level 14 point", function()
        local quest = load_module()
        local next_action = quest.nextAction({
            quests = {
                { id = 20612, tab = 0, status_code = 4, req_count = 0, seq = 2, lv_num = 11 },
                { id = 20613, tab = 0, status_code = 6, req_count = 0, seq = 3, lv_num = 14 },
            },
            char = post_20612_level14_grind_char(11),
            big_map_id = 220010000,
        }, {
            completed_20612_start_dialog = true,
            completed_20612_task_teleport = true,
            completed_20612_reward_dialog = true,
        })

        T.assert_eq(next_action.name, "StartStationaryGrind")
        T.assert_eq(next_action.params.quest_id, 20613)
        T.assert_eq(next_action.params.stage, "quest_20613_level14_grind")
        T.assert_eq(next_action.params.required_level, 14)
        T.assert_eq(next_action.params.char_level, 11)
        T.assert_eq(next_action.params.until_level, 14)
        T.assert_eq(next_action.params.requires_combat, true)
        T.assert_eq(next_action.params.task_step, "grind")
        T.assert_eq(next_action.params.x, 1093.552)
        T.assert_eq(next_action.params.y, 2247.044)
        T.assert_eq(next_action.params.z, 254.250)
    end)

    T.test("uses fixed level 14 grind point when quest 20612 is gone", function()
        local quest = load_module()
        local next_action = quest.nextAction({
            quests = {
                { id = 20613, tab = 0, status_code = 6, req_count = 0, seq = 3, lv_num = 14 },
            },
            char = quest_20612_reward_npc_char(),
            big_map_id = 220010000,
        })

        T.assert_eq(next_action.name, "NavigateToGrindPoint")
        T.assert_eq(next_action.params.quest_id, 20613)
        T.assert_eq(next_action.params.stage, "quest_20613_level14_grind")
        T.assert_eq(next_action.params.required_level, 14)
        T.assert_eq(next_action.params.char_level, 11)
        T.assert_eq(next_action.params.x, 1093.552)
        T.assert_eq(next_action.params.y, 2247.044)
        T.assert_eq(next_action.params.z, 254.250)
    end)

    T.test("waits during fixed post quest 20612 level 14 grind", function()
        local quest = load_module()
        local next_action = quest.nextAction({
            quests = {
                { id = 20612, tab = 0, status_code = 4, req_count = 0, seq = 2, lv_num = 11 },
                { id = 20613, tab = 0, status_code = 6, req_count = 0, seq = 3, lv_num = 14 },
            },
            char = post_20612_level14_grind_char(13),
            big_map_id = 220010000,
        }, {
            completed_20612_start_dialog = true,
            completed_20612_task_teleport = true,
            completed_20612_reward_dialog = true,
            active_20611_grind = true,
            active_20611_grind_stage = "quest_20613_level14_grind",
            level_grind_quest_id = 20613,
            level_grind_required_level = 14,
        })

        T.assert_eq(next_action.name, "WaitLevelGrind")
        T.assert_eq(next_action.params.quest_id, 20613)
        T.assert_eq(next_action.params.stage, "quest_20613_level14_grind")
        T.assert_eq(next_action.params.required_level, 14)
        T.assert_eq(next_action.params.char_level, 13)
    end)

    T.test("opens current tracker after fixed post quest 20612 grind reaches level 14", function()
        local quest = load_module()
        local next_action = quest.nextAction({
            quests = {
                { id = 20612, tab = 0, status_code = 4, req_count = 0, seq = 2, lv_num = 11 },
                { id = 20613, tab = 0, status_code = 3, req_count = 0, seq = 3, lv_num = 14 },
            },
            char = post_20612_level14_grind_char(14),
            big_map_id = 220010000,
        }, {
            completed_20612_start_dialog = true,
            completed_20612_task_teleport = true,
            completed_20612_reward_dialog = true,
            active_20611_grind = true,
            active_20611_grind_stage = "quest_20613_level14_grind",
            level_grind_quest_id = 20613,
            level_grind_required_level = 14,
        })

        T.assert_eq(next_action.name, "ClickUiControl")
        T.assert_eq(next_action.params.quest_id, 20613)
        T.assert_eq(next_action.params.stage, "quest_20611_indicator_title")
        T.assert_eq(next_action.params.parent, "quest_indicator_dialog")
    end)

    T.test("opens current tracker for active quest 20613 after restart", function()
        local quest = load_module()
        local next_action = quest.nextAction({
            quests = {
                { id = 20613, tab = 0, status_code = 3, req_count = 0, seq = 3, lv_num = 14 },
                { id = 20614, tab = 0, status_code = 6, req_count = 0, seq = 4, lv_num = 17 },
                { id = 20615, tab = 0, status_code = 6, req_count = 0, seq = 5, lv_num = 20 },
            },
            char = { x = 1103.14, y = 2225.12, z = 253.32, level = 14 },
            big_map_id = 220010000,
        })

        T.assert_eq(next_action.name, "ClickUiControl")
        T.assert_eq(next_action.params.quest_id, 20613)
        T.assert_eq(next_action.params.quest_step, 0)
        T.assert_eq(next_action.params.stage, "quest_20611_indicator_title")
    end)

    T.test("calls quest 20613 teleport after current tracker panel opens", function()
        local quest = load_module()
        local next_action = quest.nextAction({
            quests = {
                { id = 20613, tab = 0, status_code = 3, req_count = 0, seq = 3, lv_num = 14 },
                { id = 20614, tab = 0, status_code = 6, req_count = 0, seq = 4, lv_num = 17 },
            },
            char = { x = 1103.14, y = 2225.12, z = 253.32, level = 14 },
            big_map_id = 220010000,
            ui = {
                quest_panel_visible = true,
            },
        }, {
            clicked_20611_indicator_title = true,
        })

        T.assert_eq(next_action.name, "QuestTeleport")
        T.assert_eq(next_action.params.quest_id, 20613)
        T.assert_eq(next_action.params.quest_step, 0)
        T.assert_eq(next_action.params.stage, "quest_20613_task_teleport")
        T.assert_eq(next_action.params.open_panel_key, false)
        T.assert_eq(next_action.params.require_panel_visible, true)
        T.assert_eq(next_action.params.wait_teleport, true)
    end)

    T.test("waits for quest 20613 task teleport landing", function()
        local quest = load_module()
        local next_action = quest.nextAction({
            quests = {
                { id = 20613, tab = 0, status_code = 3, req_count = 0, seq = 3, lv_num = 14 },
            },
            char = { x = 1103.14, y = 2225.12, z = 253.32, level = 14 },
            big_map_id = 220010000,
        }, {
            waiting_teleport = true,
            teleport_quest_id = 20613,
            teleport_stage = "quest_20613_task_teleport",
            teleport_start_pos = { x = 1103.14, y = 2225.12, z = 253.32, level = 14 },
            teleport_start_big_map_id = 220010000,
        })

        T.assert_eq(next_action.name, "WaitPositionChanged")
        T.assert_eq(next_action.params.quest_id, 20613)
        T.assert_eq(next_action.params.stage, "quest_20613_task_teleport")
    end)

    T.test("completes quest 20613 task teleport after position changes", function()
        local quest = load_module()
        local next_action = quest.nextAction({
            quests = {
                { id = 20613, tab = 0, status_code = 3, req_count = 0, seq = 3, lv_num = 14 },
            },
            char = { x = 1150.00, y = 2190.00, z = 250.00, level = 14 },
            big_map_id = 220010000,
        }, {
            waiting_teleport = true,
            teleport_quest_id = 20613,
            teleport_stage = "quest_20613_task_teleport",
            teleport_start_pos = { x = 1103.14, y = 2225.12, z = 253.32, level = 14 },
            teleport_start_big_map_id = 220010000,
        })

        T.assert_eq(next_action.name, "CompleteQuestTeleport")
        T.assert_eq(next_action.params.quest_id, 20613)
        T.assert_eq(next_action.params.stage, "quest_20613_task_teleport")
    end)

    T.test("moves to quest 20613 start npc after task teleport", function()
        local quest = load_module()
        local next_action = quest.nextAction({
            quests = {
                { id = 20613, tab = 0, status_code = 3, req_count = 0, seq = 3, lv_num = 14 },
            },
            char = { x = 1150.00, y = 2190.00, z = 250.00, level = 14 },
            big_map_id = 220010000,
        }, {
            completed_20613_task_teleport = true,
        })

        T.assert_eq(next_action.name, "NavigateToNpc")
        T.assert_eq(next_action.params.quest_id, 20613)
        T.assert_eq(next_action.params.stage, "quest_20613_start_npc")
        T.assert_eq(next_action.params.interact_id, 2147495609)
        T.assert_eq(next_action.params.npc_name_key, "MQ20613_NPC_001_START")
        T.assert_eq(next_action.params.x, 1050.70)
        T.assert_eq(next_action.params.y, 2201.12)
        T.assert_eq(next_action.params.z, 262.81)
    end)

    T.test("opens quest 20613 start npc dialog after task teleport", function()
        local quest = load_module()
        local next_action = quest.nextAction({
            quests = {
                { id = 20613, tab = 0, status_code = 3, req_count = 0, seq = 3, lv_num = 14 },
            },
            char = quest_20613_start_npc_char(),
            big_map_id = 220010000,
        }, {
            completed_20613_task_teleport = true,
        })

        T.assert_eq(next_action.name, "InteractNpc")
        T.assert_eq(next_action.params.quest_id, 20613)
        T.assert_eq(next_action.params.stage, "quest_20613_start_npc")
        T.assert_eq(next_action.params.interact_id, 2147495609)
        T.assert_eq(next_action.params.npc_name_key, "MQ20613_NPC_001_START")
        T.assert_eq(next_action.params.allow_interact_id_fallback, true)
        T.assert_eq(next_action.params.after_open_continuous_x, true)
        T.assert_eq(next_action.params.after_open_expected_content_id, 10)
        T.assert_eq(next_action.params.click_x, 25)
    end)

    T.test("opens quest 20613 start npc dialog after landing even when teleport flag is missing", function()
        local quest = load_module()
        local next_action = quest.nextAction({
            quests = {
                { id = 20613, tab = 0, status_code = 3, req_count = 0, seq = 3, lv_num = 14 },
            },
            char = quest_20613_start_npc_char(),
            big_map_id = 220010000,
        }, {})

        T.assert_eq(next_action.name, "InteractNpc")
        T.assert_eq(next_action.params.quest_id, 20613)
        T.assert_eq(next_action.params.stage, "quest_20613_start_npc")
        T.assert_eq(next_action.params.interact_id, 2147495609)
        T.assert_eq(next_action.params.after_open_continuous_x, true)
    end)

    T.test("keeps quest 20613 task teleport when active quest is not near landing npc", function()
        local quest = load_module()
        local next_action = quest.nextAction({
            quests = {
                { id = 20613, tab = 0, status_code = 3, req_count = 0, seq = 3, lv_num = 14 },
            },
            char = { x = 1103.14, y = 2225.12, z = 253.32, level = 14 },
            big_map_id = 220010000,
        }, {})

        T.assert_eq(next_action.name, "OpenCurrentQuestTracker")
        T.assert_eq(next_action.params.quest_id, 20613)
        T.assert_eq(next_action.params.stage, "quest_20613_task_teleport")
    end)

    T.test("continuous x-clicks opened quest 20613 start npc dialog", function()
        local quest = load_module()
        local next_action = quest.nextAction({
            quests = {
                { id = 20613, tab = 0, status_code = 3, req_count = 0, seq = 3, lv_num = 14 },
            },
            char = quest_20613_start_npc_char(),
            big_map_id = 220010000,
            dialog = {
                npc_dialog_id = 2147495609,
                dialog_content_id = 10,
                quest_id = 0,
                type_text = "select_quest",
            },
        }, {
            completed_20613_task_teleport = true,
        })

        T.assert_eq(next_action.name, "ClickDialogXContinuous")
        T.assert_eq(next_action.params.quest_id, 20613)
        T.assert_eq(next_action.params.stage, "quest_20613_start_npc")
        T.assert_eq(next_action.params.expected_content_id, 10)
        T.assert_eq(next_action.params.content_id, 10)
        T.assert_eq(next_action.params.type_text, "select_quest")
        T.assert_eq(next_action.params.click_x, 25)
        T.assert_eq(next_action.params.interact_id, 2147495609)
    end)

    T.test("continuous x-clicks quest 20613 start npc dialog by content id fallback", function()
        local quest = load_module()
        local next_action = quest.nextAction({
            quests = {
                { id = 20613, tab = 0, status_code = 3, req_count = 0, seq = 3, lv_num = 14 },
            },
            char = quest_20613_start_npc_char(),
            big_map_id = 220010000,
            dialog = {
                npc_dialog_id = 2147495609,
                content_id = 10,
                quest_id = 0,
                type_text = "",
            },
        }, {
            completed_20613_task_teleport = true,
        })

        T.assert_eq(next_action.name, "ClickDialogXContinuous")
        T.assert_eq(next_action.params.quest_id, 20613)
        T.assert_eq(next_action.params.stage, "quest_20613_start_npc")
        T.assert_eq(next_action.params.expected_content_id, 10)
        T.assert_eq(next_action.params.content_id, 10)
        T.assert_eq(next_action.params.type_text, "select_quest")
        T.assert_eq(next_action.params.click_x, 25)
    end)

    T.test("opens current tracker after quest 20613 start dialog is completed", function()
        local quest = load_module()
        local next_action = quest.nextAction({
            quests = {
                { id = 20613, tab = 0, status_code = 3, req_count = 1, seq = 3, lv_num = 14 },
            },
            char = quest_20613_start_npc_char(),
            big_map_id = 220010000,
        }, {
            completed_20613_task_teleport = true,
            completed_20613_start_dialog = true,
        })

        T.assert_eq(next_action.name, "ClickUiControl")
        T.assert_eq(next_action.params.quest_id, 20613)
        T.assert_eq(next_action.params.quest_step, 1)
        T.assert_eq(next_action.params.stage, "quest_20611_indicator_title")
        T.assert_eq(next_action.params.parent, "quest_indicator_dialog")
    end)

    T.test("calls quest 20613 after-start teleport after current tracker panel opens", function()
        local quest = load_module()
        local next_action = quest.nextAction({
            quests = {
                { id = 20613, tab = 0, status_code = 3, req_count = 1, seq = 3, lv_num = 14 },
            },
            char = quest_20613_start_npc_char(),
            big_map_id = 220010000,
            ui = {
                quest_panel_visible = true,
            },
        }, {
            completed_20613_task_teleport = true,
            completed_20613_start_dialog = true,
            clicked_20611_indicator_title = true,
        })

        T.assert_eq(next_action.name, "QuestTeleport")
        T.assert_eq(next_action.params.quest_id, 20613)
        T.assert_eq(next_action.params.quest_step, 1)
        T.assert_eq(next_action.params.stage, "quest_20613_after_start_teleport")
        T.assert_eq(next_action.params.open_panel_key, false)
        T.assert_eq(next_action.params.require_panel_visible, true)
        T.assert_eq(next_action.params.wait_teleport, true)
    end)

    T.test("uses quest 20613 step progress to start after-start teleport when dialog flag is missing", function()
        local quest = load_module()
        local next_action = quest.nextAction({
            quests = {
                { id = 20613, tab = 0, status_code = 3, req_count = 1, seq = 3, lv_num = 14 },
            },
            char = quest_20613_start_npc_char(),
            big_map_id = 220010000,
            ui = {
                quest_panel_visible = true,
            },
        }, {
            completed_20613_task_teleport = true,
            clicked_20611_indicator_title = true,
            completed_20612_reward_dialog = true,
        })

        T.assert_eq(next_action.name, "QuestTeleport")
        T.assert_eq(next_action.params.quest_id, 20613)
        T.assert_eq(next_action.params.quest_step, 1)
        T.assert_eq(next_action.params.stage, "quest_20613_after_start_teleport")
    end)

    T.test("uses quest 20613 done status to start after-start teleport after restart", function()
        local quest = load_module()
        local next_action = quest.nextAction({
            quests = {
                { id = 20613, tab = 0, status_code = 4, req_count = 0, seq = 3, lv_num = 14 },
                { id = 20614, tab = 0, status_code = 6, req_count = 0, seq = 4, lv_num = 17 },
            },
            char = quest_20613_start_npc_char(),
            big_map_id = 220010000,
            ui = {
                quest_panel_visible = true,
            },
        }, {
            clicked_20611_indicator_title = true,
        })

        T.assert_eq(next_action.name, "QuestTeleport")
        T.assert_eq(next_action.params.quest_id, 20613)
        T.assert_eq(next_action.params.stage, "quest_20613_after_start_teleport")
    end)

    T.test("does not restart level 14 grind after quest 20613 task teleport is done", function()
        local quest = load_module()
        local next_action = quest.nextAction({
            quests = {
                { id = 20613, tab = 0, status_code = 3, req_count = 0, seq = 3, lv_num = 14 },
            },
            char = { x = 1048.52, y = 2198.80, z = 262.33, level = 13 },
            big_map_id = 220010000,
        }, {
            completed_20612_reward_dialog = true,
            completed_20613_task_teleport = true,
        })

        T.assert_true(next_action.name ~= "StartStationaryGrind", "20613 teleport flow must not restart level grind")
        T.assert_eq(next_action.params.quest_id, 20613)
        T.assert_eq(next_action.params.stage, "quest_20613_start_npc")
    end)

    T.test("waits for quest 20613 after-start teleport landing", function()
        local quest = load_module()
        local next_action = quest.nextAction({
            quests = {
                { id = 20613, tab = 0, status_code = 3, req_count = 1, seq = 3, lv_num = 14 },
            },
            char = quest_20613_start_npc_char(),
            big_map_id = 220010000,
        }, {
            waiting_teleport = true,
            teleport_quest_id = 20613,
            teleport_stage = "quest_20613_after_start_teleport",
            teleport_start_pos = quest_20613_start_npc_char(),
            teleport_start_big_map_id = 220010000,
        })

        T.assert_eq(next_action.name, "WaitPositionChanged")
        T.assert_eq(next_action.params.quest_id, 20613)
        T.assert_eq(next_action.params.stage, "quest_20613_after_start_teleport")
    end)

    T.test("completes quest 20613 after-start teleport after position changes", function()
        local quest = load_module()
        local next_action = quest.nextAction({
            quests = {
                { id = 20613, tab = 0, status_code = 3, req_count = 1, seq = 3, lv_num = 14 },
            },
            char = { x = 1000.00, y = 2150.00, z = 260.00, level = 14 },
            big_map_id = 220010000,
        }, {
            waiting_teleport = true,
            teleport_quest_id = 20613,
            teleport_stage = "quest_20613_after_start_teleport",
            teleport_start_pos = quest_20613_start_npc_char(),
            teleport_start_big_map_id = 220010000,
        })

        T.assert_eq(next_action.name, "CompleteQuestTeleport")
        T.assert_eq(next_action.params.quest_id, 20613)
        T.assert_eq(next_action.params.stage, "quest_20613_after_start_teleport")
    end)

    T.test("opens quest 20613 after-start reward npc after teleport complete", function()
        local quest = load_module()
        local next_action = quest.nextAction({
            quests = {
                { id = 20613, tab = 0, status_code = 4, req_count = 0, seq = 3, lv_num = 14 },
            },
            char = quest_20613_after_start_reward_npc_char(),
            big_map_id = 220010000,
        }, {
            completed_20613_task_teleport = true,
            completed_20613_start_dialog = true,
            completed_20613_after_start_teleport = true,
        })

        T.assert_eq(next_action.name, "InteractNpc")
        T.assert_eq(next_action.params.quest_id, 20613)
        T.assert_eq(next_action.params.stage, "quest_20613_after_start_reward_npc")
        T.assert_eq(next_action.params.interact_id, 2147507242)
        T.assert_eq(next_action.params.npc_name_key, "MQ20613_NPC_002_AFTER_START_REWARD")
        T.assert_eq(next_action.params.allow_interact_id_fallback, true)
        T.assert_eq(next_action.params.after_open_continuous_x, true)
        T.assert_eq(next_action.params.after_open_expected_content_id, 10002)
        T.assert_eq(next_action.params.click_x, 25)
    end)

    T.test("continuous x-clicks opened quest 20613 after-start reward npc dialog", function()
        local quest = load_module()
        local next_action = quest.nextAction({
            quests = {
                { id = 20613, tab = 0, status_code = 4, req_count = 0, seq = 3, lv_num = 14 },
                { id = 20614, tab = 0, status_code = 6, req_count = 0, seq = 4, lv_num = 17 },
                { id = 20615, tab = 0, status_code = 6, req_count = 0, seq = 5, lv_num = 20 },
            },
            char = quest_20613_after_start_reward_npc_char(),
            big_map_id = 220010000,
            dialog = {
                npc_dialog_id = 2147507242,
                dialog_content_id = 10002,
                content_id = 10002,
                quest_id = 20613,
                type_text = "select_success",
            },
        }, {
            completed_20613_task_teleport = true,
            completed_20613_start_dialog = true,
            completed_20613_after_start_teleport = true,
        })

        T.assert_eq(next_action.name, "ClickDialogXContinuous")
        T.assert_eq(next_action.params.quest_id, 20613)
        T.assert_eq(next_action.params.stage, "quest_20613_after_start_reward_npc")
        T.assert_eq(next_action.params.expected_content_id, 10002)
        T.assert_eq(next_action.params.content_id, 10002)
        T.assert_eq(next_action.params.type_text, "select_success")
        T.assert_eq(next_action.params.click_x, 25)
        T.assert_eq(next_action.params.interact_id, 2147507242)
        T.assert_eq(next_action.params.npc_name_key, "MQ20613_NPC_002_AFTER_START_REWARD")
    end)

    T.test("uses reward npc dialog after landing even when teleport flag is missing", function()
        local quest = load_module()
        local next_action = quest.nextAction({
            quests = {
                { id = 20613, tab = 0, status_code = 4, req_count = 0, seq = 3, lv_num = 14 },
                { id = 20614, tab = 0, status_code = 6, req_count = 0, seq = 4, lv_num = 17 },
                { id = 20615, tab = 0, status_code = 6, req_count = 0, seq = 5, lv_num = 20 },
            },
            char = quest_20613_after_start_reward_npc_char(),
            big_map_id = 220010000,
            dialog = {
                npc_dialog_id = 2147507242,
                dialog_content_id = 10002,
                content_id = 10002,
                quest_id = 20613,
                type_text = "select_success",
            },
        }, {})

        T.assert_eq(next_action.name, "ClickDialogXContinuous")
        T.assert_eq(next_action.params.quest_id, 20613)
        T.assert_eq(next_action.params.stage, "quest_20613_after_start_reward_npc")
    end)

    T.test("idles after quest 20613 after-start reward dialog completed", function()
        local quest = load_module()
        local next_action = quest.nextAction({
            quests = {
                { id = 20614, tab = 0, status_code = 6, req_count = 0, seq = 4, lv_num = 17 },
                { id = 20615, tab = 0, status_code = 6, req_count = 0, seq = 5, lv_num = 20 },
            },
            char = quest_20613_after_start_reward_npc_char(),
            big_map_id = 220010000,
        }, {
            completed_20613_task_teleport = true,
            completed_20613_start_dialog = true,
            completed_20613_after_start_teleport = true,
            completed_20613_after_start_reward_dialog = true,
        })

        T.assert_eq(next_action.name, "Idle")
        T.assert_eq(next_action.params.quest_id, 20613)
        T.assert_eq(next_action.params.stage, "quest_20613_after_start_reward_npc")
    end)

    T.test("opens current tracker for quest 20614 first task teleport", function()
        local quest = load_module()
        local next_action = quest.nextAction({
            quests = {
                { id = 20614, tab = 0, status_code = 3, req_count = 0, seq = 4, lv_num = 17 },
                { id = 20615, tab = 0, status_code = 6, req_count = 0, seq = 5, lv_num = 20 },
            },
            char = { x = 851.74, y = 1738.93, z = 261.27, level = 17 },
            big_map_id = 220010000,
        }, {
            completed_20613_after_start_reward_dialog = true,
        })

        T.assert_eq(next_action.name, "ClickUiControl")
        T.assert_eq(next_action.params.quest_id, 20614)
        T.assert_eq(next_action.params.stage, "quest_20611_indicator_title")
        T.assert_eq(next_action.params.parent, "quest_indicator_dialog")
    end)

    T.test("calls quest 20614 first task teleport after current tracker panel opens", function()
        local quest = load_module()
        local next_action = quest.nextAction({
            quests = {
                { id = 20614, tab = 0, status_code = 3, req_count = 0, seq = 4, lv_num = 17 },
                { id = 20615, tab = 0, status_code = 6, req_count = 0, seq = 5, lv_num = 20 },
            },
            char = { x = 851.74, y = 1738.93, z = 261.27, level = 17 },
            big_map_id = 220010000,
            ui = {
                quest_panel_visible = true,
            },
        }, {
            completed_20613_after_start_reward_dialog = true,
            clicked_20611_indicator_title = true,
        })

        T.assert_eq(next_action.name, "QuestTeleport")
        T.assert_eq(next_action.params.quest_id, 20614)
        T.assert_eq(next_action.params.stage, "quest_20614_task_teleport")
        T.assert_eq(next_action.params.open_panel_key, false)
        T.assert_eq(next_action.params.require_panel_visible, true)
        T.assert_eq(next_action.params.wait_teleport, true)
    end)

    T.test("opens quest 20614 start npc after task teleport complete", function()
        local quest = load_module()
        local next_action = quest.nextAction({
            quests = {
                { id = 20614, tab = 0, status_code = 3, req_count = 0, seq = 4, lv_num = 17 },
                { id = 20615, tab = 0, status_code = 6, req_count = 0, seq = 5, lv_num = 20 },
            },
            char = quest_20614_start_npc_char(),
            big_map_id = 220010000,
        }, {
            completed_20613_after_start_reward_dialog = true,
            completed_20614_task_teleport = true,
        })

        T.assert_eq(next_action.name, "InteractNpc")
        T.assert_eq(next_action.params.quest_id, 20614)
        T.assert_eq(next_action.params.stage, "quest_20614_start_npc")
        T.assert_eq(next_action.params.interact_id, 2147507242)
        T.assert_eq(next_action.params.npc_name_key, "MQ20614_NPC_001_START")
        T.assert_eq(next_action.params.npc_name, "미요우")
        T.assert_eq(next_action.params.allow_interact_id_fallback, true)
        T.assert_eq(next_action.params.after_open_continuous_x, true)
        T.assert_eq(next_action.params.after_open_expected_content_id, 10)
        T.assert_eq(next_action.params.click_x, 25)
    end)

    T.test("continuous x-clicks opened quest 20614 start npc dialog", function()
        local quest = load_module()
        local next_action = quest.nextAction({
            quests = {
                { id = 20614, tab = 0, status_code = 3, req_count = 0, seq = 4, lv_num = 17 },
                { id = 20615, tab = 0, status_code = 6, req_count = 0, seq = 5, lv_num = 20 },
            },
            char = quest_20614_start_npc_char(),
            big_map_id = 220010000,
            dialog = {
                npc_dialog_id = 2147507242,
                dialog_content_id = 10,
                content_id = 10,
                quest_id = 0,
                type_text = "select_quest",
            },
        }, {
            completed_20613_after_start_reward_dialog = true,
            completed_20614_task_teleport = true,
        })

        T.assert_eq(next_action.name, "ClickDialogXContinuous")
        T.assert_eq(next_action.params.quest_id, 20614)
        T.assert_eq(next_action.params.stage, "quest_20614_start_npc")
        T.assert_eq(next_action.params.expected_content_id, 10)
        T.assert_eq(next_action.params.content_id, 10)
        T.assert_eq(next_action.params.type_text, "select_quest")
        T.assert_eq(next_action.params.click_x, 25)
        T.assert_eq(next_action.params.interact_id, 2147507242)
        T.assert_eq(next_action.params.npc_name_key, "MQ20614_NPC_001_START")
    end)

    T.test("opens current tracker for quest 20614 after-start teleport after start dialog completed", function()
        local quest = load_module()
        local next_action = quest.nextAction({
            quests = {
                { id = 20614, tab = 0, status_code = 3, req_count = 0, seq = 4, lv_num = 17 },
                { id = 20615, tab = 0, status_code = 6, req_count = 0, seq = 5, lv_num = 20 },
            },
            char = quest_20614_start_npc_char(),
            big_map_id = 220010000,
        }, {
            completed_20613_after_start_reward_dialog = true,
            completed_20614_task_teleport = true,
            completed_20614_start_dialog = true,
        })

        T.assert_eq(next_action.name, "ClickUiControl")
        T.assert_eq(next_action.params.quest_id, 20614)
        T.assert_eq(next_action.params.stage, "quest_20611_indicator_title")
        T.assert_eq(next_action.params.parent, "quest_indicator_dialog")
    end)

    T.test("calls quest 20614 after-start teleport after current tracker panel opens", function()
        local quest = load_module()
        local next_action = quest.nextAction({
            quests = {
                { id = 20614, tab = 0, status_code = 3, req_count = 0, seq = 4, lv_num = 17 },
                { id = 20615, tab = 0, status_code = 6, req_count = 0, seq = 5, lv_num = 20 },
            },
            char = quest_20614_start_npc_char(),
            big_map_id = 220010000,
            ui = {
                quest_panel_visible = true,
            },
        }, {
            completed_20613_after_start_reward_dialog = true,
            completed_20614_task_teleport = true,
            completed_20614_start_dialog = true,
            clicked_20611_indicator_title = true,
        })

        T.assert_eq(next_action.name, "QuestTeleport")
        T.assert_eq(next_action.params.quest_id, 20614)
        T.assert_eq(next_action.params.stage, "quest_20614_after_start_teleport")
        T.assert_eq(next_action.params.open_panel_key, false)
        T.assert_eq(next_action.params.require_panel_visible, true)
        T.assert_eq(next_action.params.wait_teleport, true)
    end)

    T.test("opens current tracker for quest 20614 after-start teleport when quest is done", function()
        local quest = load_module()
        local next_action = quest.nextAction({
            quests = {
                { id = 20614, tab = 0, status_code = 4, req_count = 0, seq = 4, lv_num = 17 },
                { id = 20615, tab = 0, status_code = 6, req_count = 0, seq = 5, lv_num = 20 },
            },
            char = { x = 939.88, y = 1708.54, z = 259.50, level = 17 },
            big_map_id = 220010000,
        }, {
            completed_20613_after_start_reward_dialog = true,
            completed_20614_task_teleport = true,
            completed_20614_start_dialog = true,
        })

        T.assert_eq(next_action.name, "ClickUiControl")
        T.assert_eq(next_action.params.quest_id, 20614)
        T.assert_eq(next_action.params.stage, "quest_20611_indicator_title")
    end)

    T.test("calls quest 20614 after-start teleport after tracker opens when quest is done", function()
        local quest = load_module()
        local next_action = quest.nextAction({
            quests = {
                { id = 20614, tab = 0, status_code = 4, req_count = 0, seq = 4, lv_num = 17 },
                { id = 20615, tab = 0, status_code = 6, req_count = 0, seq = 5, lv_num = 20 },
            },
            char = { x = 939.88, y = 1708.54, z = 259.50, level = 17 },
            big_map_id = 220010000,
            ui = {
                quest_panel_visible = true,
            },
        }, {
            completed_20613_after_start_reward_dialog = true,
            completed_20614_task_teleport = true,
            completed_20614_start_dialog = true,
            clicked_20611_indicator_title = true,
        })

        T.assert_eq(next_action.name, "QuestTeleport")
        T.assert_eq(next_action.params.quest_id, 20614)
        T.assert_eq(next_action.params.stage, "quest_20614_after_start_teleport")
        T.assert_eq(next_action.params.open_panel_key, false)
        T.assert_eq(next_action.params.require_panel_visible, true)
    end)

    T.test("opens current tracker for quest 20614 after-start teleport even if start dialog snapshot is stale", function()
        local quest = load_module()
        local next_action = quest.nextAction({
            quests = {
                { id = 20614, tab = 0, status_code = 3, req_count = 0, seq = 4, lv_num = 17 },
                { id = 20615, tab = 0, status_code = 6, req_count = 0, seq = 5, lv_num = 20 },
            },
            char = quest_20614_start_npc_char(),
            big_map_id = 220010000,
            dialog = {
                npc_dialog_id = 2147507242,
                dialog_content_id = 10,
                content_id = 10,
                quest_id = 0,
                type_text = "select_quest",
            },
        }, {
            completed_20613_after_start_reward_dialog = true,
            completed_20614_task_teleport = true,
            completed_20614_start_dialog = true,
        })

        T.assert_eq(next_action.name, "ClickUiControl")
        T.assert_eq(next_action.params.quest_id, 20614)
        T.assert_eq(next_action.params.stage, "quest_20611_indicator_title")
    end)

    T.test("opens current tracker for quest 20614 after-start teleport after npc interaction", function()
        local quest = load_module()
        local next_action = quest.nextAction({
            quests = {
                { id = 20614, tab = 0, status_code = 3, req_count = 0, seq = 4, lv_num = 17 },
                { id = 20615, tab = 0, status_code = 6, req_count = 0, seq = 5, lv_num = 20 },
            },
            char = quest_20614_start_npc_char(),
            big_map_id = 220010000,
        }, {
            completed_20613_after_start_reward_dialog = true,
            completed_20614_task_teleport = true,
            last_interact_stage = "quest_20614_start_npc",
        })

        T.assert_eq(next_action.name, "ClickUiControl")
        T.assert_eq(next_action.params.quest_id, 20614)
        T.assert_eq(next_action.params.stage, "quest_20611_indicator_title")
    end)

    T.test("moves to quest 20614 reward npc after after-start teleport completed", function()
        local quest = load_module()
        local next_action = quest.nextAction({
            quests = {
                { id = 20614, tab = 0, status_code = 3, req_count = 0, seq = 4, lv_num = 17 },
                { id = 20615, tab = 0, status_code = 6, req_count = 0, seq = 5, lv_num = 20 },
            },
            char = quest_20614_start_npc_char(),
            big_map_id = 220010000,
        }, {
            completed_20613_after_start_reward_dialog = true,
            completed_20614_task_teleport = true,
            completed_20614_start_dialog = true,
            completed_20614_after_start_teleport = true,
        })

        T.assert_eq(next_action.name, "NavigateToNpc")
        T.assert_eq(next_action.params.quest_id, 20614)
        T.assert_eq(next_action.params.stage, "quest_20614_reward_npc")
        T.assert_eq(next_action.params.interact_id, 2147511075)
        T.assert_eq(next_action.params.npc_name, "드발린")
    end)

    T.test("opens quest 20614 reward npc after after-start teleport complete", function()
        local quest = load_module()
        local next_action = quest.nextAction({
            quests = {
                { id = 20614, tab = 0, status_code = 4, req_count = 0, seq = 4, lv_num = 17 },
                { id = 20615, tab = 0, status_code = 6, req_count = 0, seq = 5, lv_num = 20 },
            },
            char = quest_20614_reward_npc_char(),
            big_map_id = 220010000,
        }, {
            completed_20613_after_start_reward_dialog = true,
            completed_20614_task_teleport = true,
            completed_20614_start_dialog = true,
            completed_20614_after_start_teleport = true,
        })

        T.assert_eq(next_action.name, "InteractNpc")
        T.assert_eq(next_action.params.quest_id, 20614)
        T.assert_eq(next_action.params.stage, "quest_20614_reward_npc")
        T.assert_eq(next_action.params.interact_id, 2147511075)
        T.assert_eq(next_action.params.npc_name, "드발린")
        T.assert_eq(next_action.params.npc_name_key, "MQ20614_NPC_002_REWARD")
        T.assert_eq(next_action.params.allow_interact_id_fallback, true)
        T.assert_eq(next_action.params.after_open_continuous_x, true)
        T.assert_eq(next_action.params.after_open_expected_content_id, 10002)
        T.assert_eq(next_action.params.click_x, 25)
    end)

    T.test("continuous x-clicks opened quest 20614 reward npc dialog", function()
        local quest = load_module()
        local next_action = quest.nextAction({
            quests = {
                { id = 20614, tab = 0, status_code = 4, req_count = 0, seq = 4, lv_num = 17 },
                { id = 20615, tab = 0, status_code = 6, req_count = 0, seq = 5, lv_num = 20 },
            },
            char = quest_20614_reward_npc_char(),
            big_map_id = 220010000,
            dialog = {
                npc_dialog_id = 2147511075,
                dialog_content_id = 10002,
                content_id = 10002,
                quest_id = 20614,
                type_text = "select_success",
            },
        }, {
            completed_20613_after_start_reward_dialog = true,
            completed_20614_task_teleport = true,
            completed_20614_start_dialog = true,
            completed_20614_after_start_teleport = true,
        })

        T.assert_eq(next_action.name, "ClickDialogXContinuous")
        T.assert_eq(next_action.params.quest_id, 20614)
        T.assert_eq(next_action.params.stage, "quest_20614_reward_npc")
        T.assert_eq(next_action.params.expected_content_id, 10002)
        T.assert_eq(next_action.params.content_id, 10002)
        T.assert_eq(next_action.params.type_text, "select_success")
        T.assert_eq(next_action.params.click_x, 25)
        T.assert_eq(next_action.params.interact_id, 2147511075)
        T.assert_eq(next_action.params.npc_name, "드발린")
        T.assert_eq(next_action.params.npc_name_key, "MQ20614_NPC_002_REWARD")
    end)

    T.test("uses quest 20614 reward npc dialog after landing even when teleport flag is missing", function()
        local quest = load_module()
        local next_action = quest.nextAction({
            quests = {
                { id = 20614, tab = 0, status_code = 4, req_count = 0, seq = 4, lv_num = 17 },
                { id = 20615, tab = 0, status_code = 6, req_count = 0, seq = 5, lv_num = 20 },
            },
            char = quest_20614_reward_npc_char(),
            big_map_id = 220010000,
            dialog = {
                npc_dialog_id = 2147511075,
                dialog_content_id = 10002,
                content_id = 10002,
                quest_id = 20614,
                type_text = "select_success",
            },
        }, {})

        T.assert_eq(next_action.name, "ClickDialogXContinuous")
        T.assert_eq(next_action.params.quest_id, 20614)
        T.assert_eq(next_action.params.stage, "quest_20614_reward_npc")
    end)

    T.test("idles after quest 20614 reward dialog completed", function()
        local quest = load_module()
        local next_action = quest.nextAction({
            quests = {
                { id = 20614, tab = 0, status_code = 4, req_count = 0, seq = 4, lv_num = 17 },
                { id = 20615, tab = 0, status_code = 6, req_count = 0, seq = 5, lv_num = 20 },
            },
            char = quest_20614_reward_npc_char(),
            big_map_id = 220010000,
        }, {
            completed_20613_after_start_reward_dialog = true,
            completed_20614_task_teleport = true,
            completed_20614_start_dialog = true,
            completed_20614_after_start_teleport = true,
            completed_20614_reward_dialog = true,
        })

        T.assert_eq(next_action.name, "Idle")
        T.assert_eq(next_action.params.quest_id, 20614)
        T.assert_eq(next_action.params.stage, "quest_20614_reward_npc")
    end)

    T.test("follows quest 20615 level 20 grind route after quest 20614 reward", function()
        local quest = load_module()
        local next_action = quest.nextAction({
            quests = {
                { id = 20615, tab = 0, status_code = 6, req_count = 0, seq = 5, lv_num = 20 },
            },
            char = quest_20614_reward_npc_char(),
            big_map_id = 220010000,
        }, {
            completed_20613_after_start_reward_dialog = true,
            completed_20614_task_teleport = true,
            completed_20614_start_dialog = true,
            completed_20614_after_start_teleport = true,
            completed_20614_reward_dialog = true,
        })

        T.assert_eq(next_action.name, "FollowRoute")
        T.assert_eq(next_action.params.quest_id, 20615)
        T.assert_eq(next_action.params.stage, "quest_20615_level20_grind")
        T.assert_eq(next_action.params.route_name, "main_quest_20615_level20_grind")
        T.assert_eq(next_action.params.route_count, 44)
        T.assert_eq(next_action.params.final_x, 666.227)
        T.assert_eq(next_action.params.final_y, 1535.341)
        T.assert_eq(next_action.params.final_z, 294.009)
        T.assert_eq(next_action.params.main_quest_smooth_route, true)
    end)

    T.test("starts quest 20615 level 20 grind at route end", function()
        local quest = load_module()
        local next_action = quest.nextAction({
            quests = {
                { id = 20615, tab = 0, status_code = 6, req_count = 0, seq = 5, lv_num = 20 },
            },
            char = quest_20615_level20_grind_char(17),
            big_map_id = 220010000,
        }, {
            completed_20614_reward_dialog = true,
        })

        T.assert_eq(next_action.name, "StartStationaryGrind")
        T.assert_eq(next_action.params.quest_id, 20615)
        T.assert_eq(next_action.params.stage, "quest_20615_level20_grind")
        T.assert_eq(next_action.params.required_level, 20)
        T.assert_eq(next_action.params.char_level, 17)
        T.assert_eq(next_action.params.until_level, 20)
    end)

    T.test("waits while quest 20615 level 20 grind is active", function()
        local quest = load_module()
        local next_action = quest.nextAction({
            quests = {
                { id = 20615, tab = 0, status_code = 6, req_count = 0, seq = 5, lv_num = 20 },
            },
            char = quest_20615_level20_grind_char(18),
            big_map_id = 220010000,
        }, {
            active_20611_grind = true,
            active_20611_grind_stage = "quest_20615_level20_grind",
            level_grind_quest_id = 20615,
            level_grind_required_level = 20,
        })

        T.assert_eq(next_action.name, "WaitLevelGrind")
        T.assert_eq(next_action.params.quest_id, 20615)
        T.assert_eq(next_action.params.stage, "quest_20615_level20_grind")
        T.assert_eq(next_action.params.required_level, 20)
        T.assert_eq(next_action.params.char_level, 18)
    end)

    T.test("idles after quest 20615 reaches level 20", function()
        local quest = load_module()
        local next_action = quest.nextAction({
            quests = {
                { id = 20615, tab = 0, status_code = 6, req_count = 0, seq = 5, lv_num = 20 },
            },
            char = quest_20615_level20_grind_char(20),
            big_map_id = 220010000,
        }, {
            completed_20614_reward_dialog = true,
        })

        T.assert_eq(next_action.name, "Idle")
        T.assert_eq(next_action.params.quest_id, 20615)
        T.assert_eq(next_action.params.stage, "quest_20615_level20_grind")
        T.assert_eq(next_action.params.required_level, 20)
        T.assert_eq(next_action.params.char_level, 20)
    end)

    T.test("opens current tracker for quest 20615 task teleport after level 20", function()
        local quest = load_module()
        local next_action = quest.nextAction({
            quests = {
                { id = 20615, tab = 0, status_code = 3, req_count = 0, seq = 0, lv_num = 20 },
            },
            char = quest_20615_level20_grind_char(20),
            big_map_id = 220010000,
        }, {
            completed_20614_reward_dialog = true,
            active_20611_grind = true,
            active_20611_grind_stage = "quest_20615_level20_grind",
            level_grind_quest_id = 20615,
            level_grind_required_level = 20,
        })

        T.assert_eq(next_action.name, "ClickUiControl")
        T.assert_eq(next_action.params.quest_id, 20615)
        T.assert_eq(next_action.params.stage, "quest_20611_indicator_title")
        T.assert_eq(next_action.params.parent, "quest_indicator_dialog")
    end)

    T.test("calls quest 20615 task teleport after current tracker panel opens", function()
        local quest = load_module()
        local next_action = quest.nextAction({
            quests = {
                { id = 20615, tab = 0, status_code = 3, req_count = 0, seq = 0, lv_num = 20 },
            },
            char = quest_20615_level20_grind_char(20),
            big_map_id = 220010000,
            ui = {
                quest_panel_visible = true,
            },
        }, {
            completed_20614_reward_dialog = true,
            clicked_20611_indicator_title = true,
        })

        T.assert_eq(next_action.name, "QuestTeleport")
        T.assert_eq(next_action.params.quest_id, 20615)
        T.assert_eq(next_action.params.stage, "quest_20615_task_teleport")
        T.assert_eq(next_action.params.open_panel_key, false)
        T.assert_eq(next_action.params.require_panel_visible, true)
        T.assert_eq(next_action.params.wait_teleport, true)
    end)

    T.test("waits for quest 20615 task teleport landing after call", function()
        local quest = load_module()
        local next_action = quest.nextAction({
            quests = {
                { id = 20615, tab = 0, status_code = 3, req_count = 0, seq = 0, lv_num = 20 },
            },
            char = quest_20615_level20_grind_char(20),
            big_map_id = 220010000,
            ui = {
                quest_panel_visible = true,
            },
        }, {
            completed_20614_reward_dialog = true,
            clicked_20611_indicator_title = true,
            waiting_teleport = true,
            teleport_quest_id = 20615,
            teleport_stage = "quest_20615_task_teleport",
            teleport_start_pos = quest_20615_level20_grind_char(20),
            teleport_start_big_map_id = 220010000,
        })

        T.assert_eq(next_action.name, "WaitPositionChanged")
        T.assert_eq(next_action.params.quest_id, 20615)
        T.assert_eq(next_action.params.stage, "quest_20615_task_teleport")
    end)

    T.test("completes quest 20615 task teleport after position changes", function()
        local quest = load_module()
        local next_action = quest.nextAction({
            quests = {
                { id = 20615, tab = 0, status_code = 3, req_count = 0, seq = 0, lv_num = 20 },
            },
            char = { x = 705.00, y = 1535.34, z = 294.01, level = 20 },
            big_map_id = 220010000,
            ui = {
                quest_panel_visible = true,
            },
        }, {
            completed_20614_reward_dialog = true,
            clicked_20611_indicator_title = true,
            waiting_teleport = true,
            teleport_quest_id = 20615,
            teleport_stage = "quest_20615_task_teleport",
            teleport_start_pos = quest_20615_level20_grind_char(20),
            teleport_start_big_map_id = 220010000,
        })

        T.assert_eq(next_action.name, "CompleteQuestTeleport")
        T.assert_eq(next_action.params.quest_id, 20615)
        T.assert_eq(next_action.params.stage, "quest_20615_task_teleport")
    end)

    T.test("opens quest 20615 target npc after task teleport landing", function()
        local quest = load_module()
        local next_action = quest.nextAction({
            quests = {
                { id = 20615, tab = 0, status_code = 3, req_count = 0, seq = 0, lv_num = 20 },
            },
            char = quest_20615_target_npc_char(20),
            big_map_id = 220010000,
        }, {
            completed_20614_reward_dialog = true,
            completed_20615_task_teleport = true,
        })

        T.assert_eq(next_action.name, "InteractNpc")
        T.assert_eq(next_action.params.quest_id, 20615)
        T.assert_eq(next_action.params.stage, "quest_20615_target_npc")
        T.assert_eq(next_action.params.interact_id, 2147520815)
        T.assert_eq(next_action.params.npc_name_key, "MQ20611_NPC_003_TARGET")
        T.assert_eq(next_action.params.allow_interact_id_fallback, true)
        T.assert_eq(next_action.params.after_open_continuous_last, true)
        T.assert_eq(next_action.params.click_x, 25)
    end)

    T.test("opens quest 20615 target npc when already near target after resume", function()
        local quest = load_module()
        local next_action = quest.nextAction({
            quests = {
                { id = 20615, tab = 0, status_code = 3, req_count = 0, seq = 0, lv_num = 20 },
            },
            char = quest_20615_target_npc_char(20),
            big_map_id = 220010000,
        }, {
            completed_20614_reward_dialog = true,
        })

        T.assert_eq(next_action.name, "InteractNpc")
        T.assert_eq(next_action.params.quest_id, 20615)
        T.assert_eq(next_action.params.stage, "quest_20615_target_npc")
    end)

    T.test("continuous last-option clicks opened quest 20615 target npc dialog", function()
        local quest = load_module()
        local next_action = quest.nextAction({
            quests = {
                { id = 20615, tab = 0, status_code = 3, req_count = 0, seq = 0, lv_num = 20 },
            },
            char = quest_20615_target_npc_char(20),
            big_map_id = 220010000,
            dialog = {
                npc_dialog_id = 2147520815,
                dialog_content_id = 10,
                content_id = 10,
                quest_id = 20615,
                type_text = "select_quest",
            },
        }, {
            completed_20614_reward_dialog = true,
            completed_20615_task_teleport = true,
        })

        T.assert_eq(next_action.name, "ClickDialogLastContinuousOk")
        T.assert_eq(next_action.params.quest_id, 20615)
        T.assert_eq(next_action.params.stage, "quest_20615_target_npc")
        T.assert_eq(next_action.params.content_id, 10)
        T.assert_eq(next_action.params.type_text, "select_quest")
        T.assert_eq(next_action.params.interact_id, 2147520815)
        T.assert_eq(next_action.params.npc_name_key, "MQ20611_NPC_003_TARGET")
        T.assert_eq(next_action.params.click_x, 25)
    end)

    T.test("calls quest 20615 big map teleport after target npc dialog completed", function()
        local quest = load_module()
        local next_action = quest.nextAction({
            quests = {
                { id = 20615, tab = 0, status_code = 3, req_count = 0, seq = 0, lv_num = 20 },
            },
            char = quest_20615_target_npc_char(20),
            big_map_id = 220010000,
        }, {
            completed_20614_reward_dialog = true,
            completed_20615_task_teleport = true,
            completed_20615_target_dialog = true,
        })

        T.assert_eq(next_action.name, "BigMapTeleport")
        T.assert_eq(next_action.params.quest_id, 20615)
        T.assert_eq(next_action.params.stage, "quest_20615_big_map_teleport")
        T.assert_eq(next_action.params.slot, 0x07)
        T.assert_eq(next_action.params.price, 1200)
        T.assert_eq(next_action.params.min_lv, 20)
        T.assert_eq(next_action.params.target_name, "Morheim")
        T.assert_eq(next_action.params.wait_teleport, true)
    end)

    T.test("calls quest 20615 big map teleport when quest becomes done after target dialog", function()
        local quest = load_module()
        local next_action = quest.nextAction({
            quests = {
                { id = 20615, tab = 0, status_code = 4, req_count = 0, seq = 0, lv_num = 20 },
            },
            char = quest_20615_target_npc_char(20),
            big_map_id = 220010000,
        }, {
            completed_20614_reward_dialog = true,
            completed_20615_task_teleport = true,
            completed_20615_target_dialog = true,
        })

        T.assert_eq(next_action.name, "BigMapTeleport")
        T.assert_eq(next_action.params.quest_id, 20615)
        T.assert_eq(next_action.params.stage, "quest_20615_big_map_teleport")
        T.assert_eq(next_action.params.slot, 0x07)
        T.assert_eq(next_action.params.price, 1200)
    end)

    T.test("waits for quest 20615 big map teleport landing after call", function()
        local quest = load_module()
        local next_action = quest.nextAction({
            quests = {
                { id = 20615, tab = 0, status_code = 3, req_count = 1, seq = 0, lv_num = 20 },
            },
            char = quest_20615_target_npc_char(20),
            big_map_id = 220010000,
        }, {
            completed_20614_reward_dialog = true,
            completed_20615_task_teleport = true,
            completed_20615_target_dialog = true,
            waiting_teleport = true,
            teleport_quest_id = 20615,
            teleport_stage = "quest_20615_big_map_teleport",
            teleport_start_pos = quest_20615_target_npc_char(20),
            teleport_start_big_map_id = 220010000,
        })

        T.assert_eq(next_action.name, "WaitPositionChanged")
        T.assert_eq(next_action.params.quest_id, 20615)
        T.assert_eq(next_action.params.stage, "quest_20615_big_map_teleport")
    end)

    T.test("completes quest 20615 big map teleport after big map changes", function()
        local quest = load_module()
        local next_action = quest.nextAction({
            quests = {
                { id = 20615, tab = 0, status_code = 3, req_count = 1, seq = 0, lv_num = 20 },
            },
            char = { x = 100.00, y = 100.00, z = 100.00, level = 20 },
            big_map_id = 220020000,
        }, {
            completed_20614_reward_dialog = true,
            completed_20615_task_teleport = true,
            completed_20615_target_dialog = true,
            waiting_teleport = true,
            teleport_quest_id = 20615,
            teleport_stage = "quest_20615_big_map_teleport",
            teleport_start_pos = quest_20615_target_npc_char(20),
            teleport_start_big_map_id = 220010000,
        })

        T.assert_eq(next_action.name, "CompleteBigMapTeleport")
        T.assert_eq(next_action.params.quest_id, 20615)
        T.assert_eq(next_action.params.stage, "quest_20615_big_map_teleport")
        T.assert_eq(next_action.params.slot, 0x07)
        T.assert_eq(next_action.params.price, 1200)
    end)

    T.test("calls quest 20615 direct task teleport after big map teleport even with another task", function()
        local quest = load_module()
        local next_action = quest.nextAction({
            quests = {
                { id = 20615, tab = 0, status_code = 4, req_count = 0, seq = 0, lv_num = 20 },
                { id = 24340, tab = 1, status_code = 4, req_count = 0, seq = 0, lv_num = 20 },
            },
            char = { x = 100.00, y = 100.00, z = 100.00, level = 20 },
            big_map_id = 220020000,
        }, {
            completed_20614_reward_dialog = true,
            completed_20615_task_teleport = true,
            completed_20615_target_dialog = true,
            completed_20615_big_map_teleport = true,
        })

        T.assert_eq(next_action.name, "QuestTeleport")
        T.assert_eq(next_action.params.quest_id, 20615)
        T.assert_eq(next_action.params.stage, "quest_20615_after_big_map_task_teleport")
        T.assert_eq(next_action.params.direct_quest_id_only, true)
        T.assert_eq(next_action.params.open_panel_key, false)
        T.assert_eq(next_action.params.require_panel_visible, false)
        T.assert_eq(next_action.params.wait_teleport, true)
    end)

    T.test("waits for quest 20615 after big map task teleport landing after call", function()
        local quest = load_module()
        local next_action = quest.nextAction({
            quests = {
                { id = 20615, tab = 0, status_code = 4, req_count = 0, seq = 0, lv_num = 20 },
                { id = 24340, tab = 1, status_code = 4, req_count = 0, seq = 0, lv_num = 20 },
            },
            char = { x = 100.00, y = 100.00, z = 100.00, level = 20 },
            big_map_id = 220020000,
        }, {
            completed_20614_reward_dialog = true,
            completed_20615_task_teleport = true,
            completed_20615_target_dialog = true,
            completed_20615_big_map_teleport = true,
            waiting_teleport = true,
            teleport_quest_id = 20615,
            teleport_stage = "quest_20615_after_big_map_task_teleport",
            teleport_start_pos = { x = 100.00, y = 100.00, z = 100.00, level = 20 },
            teleport_start_big_map_id = 220020000,
        })

        T.assert_eq(next_action.name, "WaitPositionChanged")
        T.assert_eq(next_action.params.quest_id, 20615)
        T.assert_eq(next_action.params.stage, "quest_20615_after_big_map_task_teleport")
    end)

    T.test("completes quest 20615 after big map task teleport after position changes", function()
        local quest = load_module()
        local next_action = quest.nextAction({
            quests = {
                { id = 20615, tab = 0, status_code = 4, req_count = 0, seq = 0, lv_num = 20 },
                { id = 24340, tab = 1, status_code = 4, req_count = 0, seq = 0, lv_num = 20 },
            },
            char = { x = 180.00, y = 140.00, z = 100.00, level = 20 },
            big_map_id = 220020000,
        }, {
            completed_20614_reward_dialog = true,
            completed_20615_task_teleport = true,
            completed_20615_target_dialog = true,
            completed_20615_big_map_teleport = true,
            waiting_teleport = true,
            teleport_quest_id = 20615,
            teleport_stage = "quest_20615_after_big_map_task_teleport",
            teleport_start_pos = { x = 100.00, y = 100.00, z = 100.00, level = 20 },
            teleport_start_big_map_id = 220020000,
        })

        T.assert_eq(next_action.name, "CompleteQuestTeleport")
        T.assert_eq(next_action.params.quest_id, 20615)
        T.assert_eq(next_action.params.stage, "quest_20615_after_big_map_task_teleport")
    end)

    T.test("opens quest 20615 Morheim npc after after-map task teleport with another task", function()
        local quest = load_module()
        local next_action = quest.nextAction({
            quests = {
                { id = 20615, tab = 0, status_code = 4, req_count = 0, seq = 0, lv_num = 20 },
                { id = 20620, tab = 0, status_code = 3, req_count = 0, seq = 0, lv_num = 20 },
            },
            char = quest_20615_morheim_npc_char(20),
            big_map_id = 220020000,
        }, {
            completed_20614_reward_dialog = true,
            completed_20615_task_teleport = true,
            completed_20615_target_dialog = true,
            completed_20615_big_map_teleport = true,
            completed_20615_after_big_map_task_teleport = true,
        })

        T.assert_eq(next_action.name, "InteractNpc")
        T.assert_eq(next_action.params.quest_id, 20615)
        T.assert_eq(next_action.params.stage, "quest_20615_morheim_npc")
        T.assert_eq(next_action.params.interact_id, 2147488159)
        T.assert_eq(next_action.params.npc_name_key, "MQ20615_NPC_001_MORHEIM_AEGIR")
        T.assert_eq(next_action.params.allow_interact_id_fallback, true)
        T.assert_eq(next_action.params.after_open_continuous_last, true)
        T.assert_eq(next_action.params.click_x, 25)
    end)

    T.test("opens quest 20615 Morheim npc when already near even if after-map task flag is missing", function()
        local quest = load_module()
        local next_action = quest.nextAction({
            quests = {
                { id = 20615, tab = 0, status_code = 4, req_count = 0, seq = 0, lv_num = 20 },
                { id = 20620, tab = 0, status_code = 3, req_count = 0, seq = 0, lv_num = 20 },
            },
            char = quest_20615_morheim_npc_char(20),
            big_map_id = 220020000,
        }, {
            completed_20614_reward_dialog = true,
            completed_20615_task_teleport = true,
            completed_20615_target_dialog = true,
            completed_20615_big_map_teleport = true,
        })

        T.assert_eq(next_action.name, "InteractNpc")
        T.assert_eq(next_action.params.quest_id, 20615)
        T.assert_eq(next_action.params.stage, "quest_20615_morheim_npc")
        T.assert_eq(next_action.params.interact_id, 2147488159)
    end)

    T.test("continuous last-option clicks opened quest 20615 Morheim npc dialog", function()
        local quest = load_module()
        local next_action = quest.nextAction({
            quests = {
                { id = 20615, tab = 0, status_code = 4, req_count = 0, seq = 0, lv_num = 20 },
                { id = 20620, tab = 0, status_code = 3, req_count = 0, seq = 0, lv_num = 20 },
            },
            char = quest_20615_morheim_npc_char(20),
            big_map_id = 220020000,
            dialog = {
                npc_dialog_id = 2147488159,
                dialog_content_id = 11,
                content_id = 11,
                quest_id = 20615,
                type_text = "select_success",
            },
        }, {
            completed_20614_reward_dialog = true,
            completed_20615_task_teleport = true,
            completed_20615_target_dialog = true,
            completed_20615_big_map_teleport = true,
            completed_20615_after_big_map_task_teleport = true,
        })

        T.assert_eq(next_action.name, "ClickDialogLastContinuousOk")
        T.assert_eq(next_action.params.quest_id, 20615)
        T.assert_eq(next_action.params.stage, "quest_20615_morheim_npc")
        T.assert_eq(next_action.params.content_id, 11)
        T.assert_eq(next_action.params.type_text, "select_success")
        T.assert_eq(next_action.params.interact_id, 2147488159)
        T.assert_eq(next_action.params.npc_name_key, "MQ20615_NPC_001_MORHEIM_AEGIR")
        T.assert_eq(next_action.params.click_x, 25)
    end)

    T.test("idles after quest 20615 Morheim npc dialog completed", function()
        local quest = load_module()
        local next_action = quest.nextAction({
            quests = {
                { id = 20615, tab = 0, status_code = 4, req_count = 0, seq = 0, lv_num = 20 },
                { id = 20620, tab = 0, status_code = 3, req_count = 0, seq = 0, lv_num = 20 },
            },
            char = quest_20615_morheim_npc_char(20),
            big_map_id = 220020000,
        }, {
            completed_20614_reward_dialog = true,
            completed_20615_task_teleport = true,
            completed_20615_target_dialog = true,
            completed_20615_big_map_teleport = true,
            completed_20615_after_big_map_task_teleport = true,
            completed_20615_morheim_npc_dialog = true,
        })

        T.assert_eq(next_action.name, "Idle")
        T.assert_eq(next_action.params.quest_id, 20615)
        T.assert_eq(next_action.params.stage, "quest_20615_morheim_npc")
    end)

    T.test("opens quest 20620 start npc after quest 20615 is absent", function()
        local quest = load_module()
        local next_action = quest.nextAction({
            quests = {
                { id = 20620, tab = 0, status_code = 3, req_count = 0, seq = 0, lv_num = 20 },
                { id = 20621, tab = 0, status_code = 6, req_count = 0, seq = 1, lv_num = 22 },
            },
            char = quest_20620_start_npc_char(20),
            big_map_id = 220020000,
        }, {
            completed_20615_morheim_npc_dialog = true,
        })

        T.assert_eq(next_action.name, "InteractNpc")
        T.assert_eq(next_action.params.quest_id, 20620)
        T.assert_eq(next_action.params.stage, "quest_20620_start_npc")
        T.assert_eq(next_action.params.interact_id, 2147488159)
        T.assert_eq(next_action.params.npc_name_key, "MQ20620_NPC_001_START_AEGIR")
        T.assert_eq(next_action.params.allow_interact_id_fallback, true)
        T.assert_eq(next_action.params.after_open_continuous_last, true)
        T.assert_eq(next_action.params.click_x, 25)
    end)

    T.test("continuous last-option clicks opened quest 20620 start npc dialog", function()
        local quest = load_module()
        local next_action = quest.nextAction({
            quests = {
                { id = 20620, tab = 0, status_code = 3, req_count = 0, seq = 0, lv_num = 20 },
                { id = 20621, tab = 0, status_code = 6, req_count = 0, seq = 1, lv_num = 22 },
            },
            char = quest_20620_start_npc_char(20),
            big_map_id = 220020000,
            dialog = {
                npc_dialog_id = 2147488159,
                dialog_content_id = 12,
                content_id = 12,
                quest_id = 20620,
                type_text = "select_quest",
            },
        }, {
            completed_20615_morheim_npc_dialog = true,
        })

        T.assert_eq(next_action.name, "ClickDialogLastContinuousOk")
        T.assert_eq(next_action.params.quest_id, 20620)
        T.assert_eq(next_action.params.stage, "quest_20620_start_npc")
        T.assert_eq(next_action.params.content_id, 12)
        T.assert_eq(next_action.params.type_text, "select_quest")
        T.assert_eq(next_action.params.interact_id, 2147488159)
        T.assert_eq(next_action.params.npc_name_key, "MQ20620_NPC_001_START_AEGIR")
        T.assert_eq(next_action.params.click_x, 25)
    end)

    T.test("calls quest 20620 task teleport after start npc dialog completed", function()
        local quest = load_module()
        local next_action = quest.nextAction({
            quests = {
                { id = 20620, tab = 0, status_code = 3, req_count = 0, seq = 0, lv_num = 20 },
                { id = 20621, tab = 0, status_code = 6, req_count = 0, seq = 1, lv_num = 22 },
            },
            char = quest_20620_start_npc_char(20),
            big_map_id = 220020000,
        }, {
            completed_20615_morheim_npc_dialog = true,
            completed_20620_start_dialog = true,
        })

        T.assert_eq(next_action.name, "QuestTeleport")
        T.assert_eq(next_action.params.quest_id, 20620)
        T.assert_eq(next_action.params.stage, "quest_20620_task_teleport")
        T.assert_eq(next_action.params.direct_quest_id_only, true)
        T.assert_eq(next_action.params.open_panel_key, false)
        T.assert_eq(next_action.params.require_panel_visible, false)
        T.assert_eq(next_action.params.wait_teleport, true)
    end)

    T.test("calls quest 20620 task teleport after quest step advances", function()
        local quest = load_module()
        local next_action = quest.nextAction({
            quests = {
                { id = 20620, tab = 0, status_code = 3, req_count = 1, seq = 0, lv_num = 20 },
                { id = 20621, tab = 0, status_code = 6, req_count = 0, seq = 1, lv_num = 22 },
            },
            char = quest_20620_start_npc_char(20),
            big_map_id = 220020000,
        }, {
            completed_20615_morheim_npc_dialog = true,
        })

        T.assert_eq(next_action.name, "QuestTeleport")
        T.assert_eq(next_action.params.quest_id, 20620)
        T.assert_eq(next_action.params.stage, "quest_20620_task_teleport")
        T.assert_eq(next_action.params.direct_quest_id_only, true)
    end)

    T.test("waits for quest 20620 task teleport landing after call", function()
        local quest = load_module()
        local next_action = quest.nextAction({
            quests = {
                { id = 20620, tab = 0, status_code = 3, req_count = 1, seq = 0, lv_num = 20 },
                { id = 20621, tab = 0, status_code = 6, req_count = 0, seq = 1, lv_num = 22 },
            },
            char = quest_20620_start_npc_char(20),
            big_map_id = 220020000,
        }, {
            completed_20615_morheim_npc_dialog = true,
            completed_20620_start_dialog = true,
            waiting_teleport = true,
            teleport_quest_id = 20620,
            teleport_stage = "quest_20620_task_teleport",
            teleport_start_pos = quest_20620_start_npc_char(20),
            teleport_start_big_map_id = 220020000,
        })

        T.assert_eq(next_action.name, "WaitPositionChanged")
        T.assert_eq(next_action.params.quest_id, 20620)
        T.assert_eq(next_action.params.stage, "quest_20620_task_teleport")
    end)

    T.test("completes quest 20620 task teleport after position changes", function()
        local quest = load_module()
        local next_action = quest.nextAction({
            quests = {
                { id = 20620, tab = 0, status_code = 3, req_count = 1, seq = 0, lv_num = 20 },
                { id = 20621, tab = 0, status_code = 6, req_count = 0, seq = 1, lv_num = 22 },
            },
            char = { x = 260.00, y = 2460.00, z = 455.00, level = 20 },
            big_map_id = 220020000,
        }, {
            completed_20615_morheim_npc_dialog = true,
            completed_20620_start_dialog = true,
            waiting_teleport = true,
            teleport_quest_id = 20620,
            teleport_stage = "quest_20620_task_teleport",
            teleport_start_pos = quest_20620_start_npc_char(20),
            teleport_start_big_map_id = 220020000,
        })

        T.assert_eq(next_action.name, "CompleteQuestTeleport")
        T.assert_eq(next_action.params.quest_id, 20620)
        T.assert_eq(next_action.params.stage, "quest_20620_task_teleport")
    end)

    T.test("opens quest 20620 after-teleport npc after task teleport completed", function()
        local quest = load_module()
        local next_action = quest.nextAction({
            quests = {
                { id = 20620, tab = 0, status_code = 3, req_count = 1, seq = 0, lv_num = 20 },
                { id = 20621, tab = 0, status_code = 6, req_count = 0, seq = 1, lv_num = 22 },
            },
            char = quest_20620_after_teleport_npc_char(20),
            big_map_id = 220020000,
        }, {
            completed_20615_morheim_npc_dialog = true,
            completed_20620_start_dialog = true,
            completed_20620_task_teleport = true,
        })

        T.assert_eq(next_action.name, "InteractNpc")
        T.assert_eq(next_action.params.quest_id, 20620)
        T.assert_eq(next_action.params.stage, "quest_20620_after_teleport_npc")
        T.assert_eq(next_action.params.interact_id, 2147511717)
        T.assert_eq(next_action.params.npc_name_key, "MQ20620_NPC_002_AFTER_TELEPORT")
        T.assert_eq(next_action.params.allow_interact_id_fallback, true)
        T.assert_eq(next_action.params.after_open_continuous_last, true)
        T.assert_eq(next_action.params.click_x, 25)
    end)

    T.test("opens quest 20620 after-teleport npc when already near target", function()
        local quest = load_module()
        local next_action = quest.nextAction({
            quests = {
                { id = 20620, tab = 0, status_code = 3, req_count = 1, seq = 0, lv_num = 20 },
                { id = 20621, tab = 0, status_code = 6, req_count = 0, seq = 1, lv_num = 22 },
            },
            char = quest_20620_after_teleport_npc_char(20),
            big_map_id = 220020000,
        }, {
            completed_20615_morheim_npc_dialog = true,
            completed_20620_start_dialog = true,
        })

        T.assert_eq(next_action.name, "InteractNpc")
        T.assert_eq(next_action.params.quest_id, 20620)
        T.assert_eq(next_action.params.stage, "quest_20620_after_teleport_npc")
        T.assert_eq(next_action.params.interact_id, 2147511717)
    end)

    T.test("continuous last-option clicks opened quest 20620 after-teleport npc dialog", function()
        local quest = load_module()
        local next_action = quest.nextAction({
            quests = {
                { id = 20620, tab = 0, status_code = 3, req_count = 1, seq = 0, lv_num = 20 },
                { id = 20621, tab = 0, status_code = 6, req_count = 0, seq = 1, lv_num = 22 },
            },
            char = quest_20620_after_teleport_npc_char(20),
            big_map_id = 220020000,
            dialog = {
                npc_dialog_id = 2147511717,
                dialog_content_id = 21,
                content_id = 21,
                quest_id = 20620,
                type_text = "select_quest",
            },
        }, {
            completed_20615_morheim_npc_dialog = true,
            completed_20620_start_dialog = true,
            completed_20620_task_teleport = true,
        })

        T.assert_eq(next_action.name, "ClickDialogLastContinuousOk")
        T.assert_eq(next_action.params.quest_id, 20620)
        T.assert_eq(next_action.params.stage, "quest_20620_after_teleport_npc")
        T.assert_eq(next_action.params.content_id, 21)
        T.assert_eq(next_action.params.type_text, "select_quest")
        T.assert_eq(next_action.params.interact_id, 2147511717)
        T.assert_eq(next_action.params.npc_name_key, "MQ20620_NPC_002_AFTER_TELEPORT")
        T.assert_eq(next_action.params.click_x, 25)
    end)

    T.test("uses quest 20620 stigma stone after after-teleport npc dialog completed", function()
        local quest = load_module()
        local next_action = quest.nextAction({
            quests = {
                { id = 20620, tab = 0, status_code = 3, req_count = 1, seq = 0, lv_num = 20 },
                { id = 20621, tab = 0, status_code = 6, req_count = 0, seq = 1, lv_num = 22 },
            },
            char = quest_20620_after_teleport_npc_char(20),
            big_map_id = 220020000,
        }, {
            completed_20615_morheim_npc_dialog = true,
            completed_20620_start_dialog = true,
            completed_20620_task_teleport = true,
            completed_20620_after_teleport_npc_dialog = true,
        })

        T.assert_eq(next_action.name, "UseQuestStigmaStone")
        T.assert_eq(next_action.params.quest_id, 20620)
        T.assert_eq(next_action.params.stage, "quest_20620_socket_stigma")
        T.assert_eq(next_action.params.prefer_keyword, "파멸의 방패")
    end)

    T.test("starts quest 20620 after-stigma teleport after stigma socket completed", function()
        local quest = load_module()
        local next_action = quest.nextAction({
            quests = {
                { id = 20620, tab = 0, status_code = 3, req_count = 2, seq = 0, lv_num = 20 },
                { id = 20621, tab = 0, status_code = 6, req_count = 0, seq = 1, lv_num = 22 },
            },
            char = quest_20620_after_teleport_npc_char(20),
            big_map_id = 220020000,
        }, {
            completed_20615_morheim_npc_dialog = true,
            completed_20620_start_dialog = true,
            completed_20620_task_teleport = true,
            completed_20620_after_teleport_npc_dialog = true,
            completed_20620_stigma_socket = true,
        })

        T.assert_eq(next_action.name, "QuestTeleport")
        T.assert_eq(next_action.params.quest_id, 20620)
        T.assert_eq(next_action.params.stage, "quest_20620_after_stigma_teleport")
        T.assert_eq(next_action.params.direct_quest_id_only, true)
    end)

    T.test("waits for quest 20620 after-stigma teleport landing after call", function()
        local quest = load_module()
        local next_action = quest.nextAction({
            quests = {
                { id = 20620, tab = 0, status_code = 3, req_count = 2, seq = 0, lv_num = 20 },
                { id = 20621, tab = 0, status_code = 6, req_count = 0, seq = 1, lv_num = 22 },
            },
            char = quest_20620_after_teleport_npc_char(20),
            big_map_id = 220020000,
        }, {
            completed_20615_morheim_npc_dialog = true,
            completed_20620_start_dialog = true,
            completed_20620_task_teleport = true,
            completed_20620_after_teleport_npc_dialog = true,
            completed_20620_stigma_socket = true,
            waiting_teleport = true,
            teleport_quest_id = 20620,
            teleport_stage = "quest_20620_after_stigma_teleport",
            teleport_start_pos = quest_20620_after_teleport_npc_char(20),
            teleport_start_big_map_id = 220020000,
        })

        T.assert_eq(next_action.name, "WaitPositionChanged")
        T.assert_eq(next_action.params.quest_id, 20620)
        T.assert_eq(next_action.params.stage, "quest_20620_after_stigma_teleport")
    end)

    T.test("completes quest 20620 after-stigma teleport after position changes", function()
        local quest = load_module()
        local next_action = quest.nextAction({
            quests = {
                { id = 20620, tab = 0, status_code = 3, req_count = 2, seq = 0, lv_num = 20 },
                { id = 20621, tab = 0, status_code = 6, req_count = 0, seq = 1, lv_num = 22 },
            },
            char = { x = 320.00, y = 2380.00, z = 446.00, level = 20 },
            big_map_id = 220020000,
        }, {
            completed_20615_morheim_npc_dialog = true,
            completed_20620_start_dialog = true,
            completed_20620_task_teleport = true,
            completed_20620_after_teleport_npc_dialog = true,
            completed_20620_stigma_socket = true,
            waiting_teleport = true,
            teleport_quest_id = 20620,
            teleport_stage = "quest_20620_after_stigma_teleport",
            teleport_start_pos = quest_20620_after_teleport_npc_char(20),
            teleport_start_big_map_id = 220020000,
        })

        T.assert_eq(next_action.name, "CompleteQuestTeleport")
        T.assert_eq(next_action.params.quest_id, 20620)
        T.assert_eq(next_action.params.stage, "quest_20620_after_stigma_teleport")
    end)

    T.test("opens quest 20620 after-stigma npc after teleport completed", function()
        local quest = load_module()
        local next_action = quest.nextAction({
            quests = {
                { id = 20620, tab = 0, status_code = 3, req_count = 3, seq = 0, lv_num = 20 },
                { id = 20621, tab = 0, status_code = 6, req_count = 0, seq = 1, lv_num = 22 },
            },
            char = quest_20620_after_stigma_npc_char(20),
            big_map_id = 220020000,
        }, {
            completed_20615_morheim_npc_dialog = true,
            completed_20620_start_dialog = true,
            completed_20620_task_teleport = true,
            completed_20620_after_teleport_npc_dialog = true,
            completed_20620_stigma_socket = true,
            completed_20620_after_stigma_teleport = true,
        })

        T.assert_eq(next_action.name, "InteractNpc")
        T.assert_eq(next_action.params.quest_id, 20620)
        T.assert_eq(next_action.params.stage, "quest_20620_after_stigma_npc")
        T.assert_eq(next_action.params.interact_id, 2147515902)
        T.assert_eq(next_action.params.after_open_continuous_last, true)
    end)

    T.test("clicks quest 20620 after-stigma npc dialog with last continuous ok", function()
        local quest = load_module()
        local next_action = quest.nextAction({
            quests = {
                { id = 20620, tab = 0, status_code = 3, req_count = 3, seq = 0, lv_num = 20 },
                { id = 20621, tab = 0, status_code = 6, req_count = 0, seq = 1, lv_num = 22 },
            },
            char = quest_20620_after_stigma_npc_char(20),
            big_map_id = 220020000,
            dialog = {
                quest_id = 20620,
                npc_dialog_id = 2147515902,
                type_text = "select_quest_reward",
                dialog_content_id = 0,
            },
        }, {
            completed_20615_morheim_npc_dialog = true,
            completed_20620_start_dialog = true,
            completed_20620_task_teleport = true,
            completed_20620_after_teleport_npc_dialog = true,
            completed_20620_stigma_socket = true,
            completed_20620_after_stigma_teleport = true,
        })

        T.assert_eq(next_action.name, "ClickDialogLastContinuousOk")
        T.assert_eq(next_action.params.quest_id, 20620)
        T.assert_eq(next_action.params.stage, "quest_20620_after_stigma_npc")
        T.assert_eq(next_action.params.interact_id, 2147515902)
        T.assert_eq(next_action.params.click_x, 25)
    end)

    T.test("opens quest 20620 obelisk after after-stigma npc dialog completed", function()
        local quest = load_module()
        local next_action = quest.nextAction({
            quests = {
                { id = 20620, tab = 0, status_code = 3, req_count = 4, seq = 0, lv_num = 20 },
                { id = 20621, tab = 0, status_code = 6, req_count = 0, seq = 1, lv_num = 22 },
            },
            char = quest_20620_obelisk_char(20),
            big_map_id = 220020000,
        }, {
            completed_20615_morheim_npc_dialog = true,
            completed_20620_start_dialog = true,
            completed_20620_task_teleport = true,
            completed_20620_after_teleport_npc_dialog = true,
            completed_20620_stigma_socket = true,
            completed_20620_after_stigma_teleport = true,
            completed_20620_after_stigma_npc_dialog = true,
        })

        T.assert_eq(next_action.name, "InteractNpc")
        T.assert_eq(next_action.params.quest_id, 20620)
        T.assert_eq(next_action.params.quest_step, 4)
        T.assert_eq(next_action.params.stage, "quest_20620_obelisk")
        T.assert_eq(next_action.params.interact_id, 2147499094)
        T.assert_eq(next_action.params.allow_interact_id_fallback, true)
    end)

    T.test("clicks quest 20620 obelisk confirm after npc interaction", function()
        local quest = load_module()
        local next_action = quest.nextAction({
            quests = {
                { id = 20620, tab = 0, status_code = 3, req_count = 4, seq = 0, lv_num = 20 },
                { id = 20621, tab = 0, status_code = 6, req_count = 0, seq = 1, lv_num = 22 },
            },
            char = quest_20620_obelisk_char(20),
            big_map_id = 220020000,
        }, {
            completed_20615_morheim_npc_dialog = true,
            completed_20620_start_dialog = true,
            completed_20620_task_teleport = true,
            completed_20620_after_teleport_npc_dialog = true,
            completed_20620_stigma_socket = true,
            completed_20620_after_stigma_teleport = true,
            completed_20620_after_stigma_npc_dialog = true,
            opened_20620_obelisk = true,
        })

        T.assert_eq(next_action.name, "ClickObeliskConfirm")
        T.assert_eq(next_action.params.quest_id, 20620)
        T.assert_eq(next_action.params.quest_step, 4)
        T.assert_eq(next_action.params.stage, "quest_20620_obelisk")
        T.assert_eq(next_action.params.confirm_x, 684)
        T.assert_eq(next_action.params.confirm_y, 437)
        T.assert_eq(next_action.params.confirm_tolerance, 90)
    end)

    T.test("clicks quest 20620 obelisk confirm when popup is visible after restart", function()
        local quest = load_module()
        local next_action = quest.nextAction({
            quests = {
                { id = 20620, tab = 0, status_code = 3, req_count = 4, seq = 0, lv_num = 20 },
                { id = 20621, tab = 0, status_code = 6, req_count = 0, seq = 1, lv_num = 22 },
            },
            char = quest_20620_obelisk_char(20),
            big_map_id = 220020000,
            ui = {
                obelisk_confirm_visible = true,
            },
        }, {
            completed_20615_morheim_npc_dialog = true,
            completed_20620_start_dialog = true,
            completed_20620_task_teleport = true,
            completed_20620_after_teleport_npc_dialog = true,
            completed_20620_stigma_socket = true,
            completed_20620_after_stigma_teleport = true,
            completed_20620_after_stigma_npc_dialog = true,
        })

        T.assert_eq(next_action.name, "ClickObeliskConfirm")
        T.assert_eq(next_action.params.quest_id, 20620)
        T.assert_eq(next_action.params.stage, "quest_20620_obelisk")
    end)

    T.test("starts quest 20620 after-obelisk teleport after obelisk confirmed", function()
        local quest = load_module()
        local next_action = quest.nextAction({
            quests = {
                { id = 20620, tab = 0, status_code = 3, req_count = 5, seq = 0, lv_num = 20 },
                { id = 20621, tab = 0, status_code = 6, req_count = 0, seq = 1, lv_num = 22 },
            },
            char = quest_20620_obelisk_char(20),
            big_map_id = 220020000,
        }, {
            completed_20615_morheim_npc_dialog = true,
            completed_20620_start_dialog = true,
            completed_20620_task_teleport = true,
            completed_20620_after_teleport_npc_dialog = true,
            completed_20620_stigma_socket = true,
            completed_20620_after_stigma_teleport = true,
            completed_20620_after_stigma_npc_dialog = true,
            completed_20620_obelisk = true,
        })

        T.assert_eq(next_action.name, "QuestTeleport")
        T.assert_eq(next_action.params.quest_id, 20620)
        T.assert_eq(next_action.params.stage, "quest_20620_after_obelisk_teleport")
        T.assert_eq(next_action.params.direct_quest_id_only, true)
    end)

    T.test("recovers quest 20620 after-obelisk teleport from done snapshot when runtime flag is missing", function()
        local quest = load_module()
        local next_action = quest.nextAction({
            quests = {
                { id = 20620, tab = 0, status_code = 4, req_count = 4, seq = 0, lv_num = 20 },
                { id = 20621, tab = 0, status_code = 6, req_count = 0, seq = 1, lv_num = 22 },
            },
            char = quest_20620_obelisk_char(20),
            big_map_id = 220020000,
        }, {
            completed_20615_morheim_npc_dialog = true,
            completed_20620_start_dialog = true,
            completed_20620_task_teleport = true,
            completed_20620_after_teleport_npc_dialog = true,
        })

        T.assert_eq(next_action.name, "QuestTeleport")
        T.assert_eq(next_action.params.quest_id, 20620)
        T.assert_eq(next_action.params.quest_step, 4)
        T.assert_eq(next_action.params.stage, "quest_20620_after_obelisk_teleport")
        T.assert_eq(next_action.params.direct_quest_id_only, true)
    end)

    T.test("recovers quest 20620 after-obelisk npc near target when teleport flag is missing", function()
        local quest = load_module()
        local next_action = quest.nextAction({
            quests = {
                { id = 20620, tab = 0, status_code = 4, req_count = 4, seq = 0, lv_num = 20 },
                { id = 20621, tab = 0, status_code = 6, req_count = 0, seq = 1, lv_num = 22 },
            },
            char = quest_20620_after_obelisk_npc_char(20),
            big_map_id = 220020000,
        }, {
            completed_20615_morheim_npc_dialog = true,
            completed_20620_start_dialog = true,
            completed_20620_task_teleport = true,
            completed_20620_after_teleport_npc_dialog = true,
        })

        T.assert_eq(next_action.name, "InteractNpc")
        T.assert_eq(next_action.params.quest_id, 20620)
        T.assert_eq(next_action.params.quest_step, 4)
        T.assert_eq(next_action.params.stage, "quest_20620_after_obelisk_npc")
        T.assert_eq(next_action.params.interact_id, 2147535533)
        T.assert_eq(next_action.params.after_open_continuous_last, true)
    end)

    T.test("waits for quest 20620 after-obelisk teleport landing after call", function()
        local quest = load_module()
        local next_action = quest.nextAction({
            quests = {
                { id = 20620, tab = 0, status_code = 3, req_count = 5, seq = 0, lv_num = 20 },
                { id = 20621, tab = 0, status_code = 6, req_count = 0, seq = 1, lv_num = 22 },
            },
            char = quest_20620_obelisk_char(20),
            big_map_id = 220020000,
        }, {
            completed_20615_morheim_npc_dialog = true,
            completed_20620_start_dialog = true,
            completed_20620_task_teleport = true,
            completed_20620_after_teleport_npc_dialog = true,
            completed_20620_stigma_socket = true,
            completed_20620_after_stigma_teleport = true,
            completed_20620_after_stigma_npc_dialog = true,
            completed_20620_obelisk = true,
            waiting_teleport = true,
            teleport_quest_id = 20620,
            teleport_stage = "quest_20620_after_obelisk_teleport",
            teleport_start_pos = quest_20620_obelisk_char(20),
            teleport_start_big_map_id = 220020000,
        })

        T.assert_eq(next_action.name, "WaitPositionChanged")
        T.assert_eq(next_action.params.quest_id, 20620)
        T.assert_eq(next_action.params.stage, "quest_20620_after_obelisk_teleport")
    end)

    T.test("completes quest 20620 after-obelisk teleport after position changes", function()
        local quest = load_module()
        local next_action = quest.nextAction({
            quests = {
                { id = 20620, tab = 0, status_code = 3, req_count = 5, seq = 0, lv_num = 20 },
                { id = 20621, tab = 0, status_code = 6, req_count = 0, seq = 1, lv_num = 22 },
            },
            char = { x = 320.00, y = 2380.00, z = 446.00, level = 20 },
            big_map_id = 220020000,
        }, {
            completed_20615_morheim_npc_dialog = true,
            completed_20620_start_dialog = true,
            completed_20620_task_teleport = true,
            completed_20620_after_teleport_npc_dialog = true,
            completed_20620_stigma_socket = true,
            completed_20620_after_stigma_teleport = true,
            completed_20620_after_stigma_npc_dialog = true,
            completed_20620_obelisk = true,
            waiting_teleport = true,
            teleport_quest_id = 20620,
            teleport_stage = "quest_20620_after_obelisk_teleport",
            teleport_start_pos = quest_20620_obelisk_char(20),
            teleport_start_big_map_id = 220020000,
        })

        T.assert_eq(next_action.name, "CompleteQuestTeleport")
        T.assert_eq(next_action.params.quest_id, 20620)
        T.assert_eq(next_action.params.stage, "quest_20620_after_obelisk_teleport")
    end)

    T.test("opens quest 20620 after-obelisk npc after teleport completed", function()
        local quest = load_module()
        local next_action = quest.nextAction({
            quests = {
                { id = 20620, tab = 0, status_code = 4, req_count = 4, seq = 0, lv_num = 20 },
                { id = 20621, tab = 0, status_code = 6, req_count = 0, seq = 1, lv_num = 22 },
            },
            char = quest_20620_after_obelisk_npc_char(20),
            big_map_id = 220020000,
        }, {
            completed_20615_morheim_npc_dialog = true,
            completed_20620_start_dialog = true,
            completed_20620_task_teleport = true,
            completed_20620_after_teleport_npc_dialog = true,
            completed_20620_stigma_socket = true,
            completed_20620_after_stigma_teleport = true,
            completed_20620_after_stigma_npc_dialog = true,
            completed_20620_obelisk = true,
            completed_20620_after_obelisk_teleport = true,
        })

        T.assert_eq(next_action.name, "InteractNpc")
        T.assert_eq(next_action.params.quest_id, 20620)
        T.assert_eq(next_action.params.quest_step, 4)
        T.assert_eq(next_action.params.stage, "quest_20620_after_obelisk_npc")
        T.assert_eq(next_action.params.interact_id, 2147535533)
        T.assert_eq(next_action.params.npc_name_key, "MQ20620_NPC_005_AFTER_OBELISK")
        T.assert_eq(next_action.params.allow_interact_id_fallback, true)
        T.assert_eq(next_action.params.after_open_continuous_last, true)
    end)

    T.test("clicks quest 20620 after-obelisk npc dialog with last continuous ok", function()
        local quest = load_module()
        local next_action = quest.nextAction({
            quests = {
                { id = 20620, tab = 0, status_code = 4, req_count = 4, seq = 0, lv_num = 20 },
                { id = 20621, tab = 0, status_code = 6, req_count = 0, seq = 1, lv_num = 22 },
            },
            char = quest_20620_after_obelisk_npc_char(20),
            big_map_id = 220020000,
            dialog = {
                quest_id = 20620,
                npc_dialog_id = 2147535533,
                type_text = "select_quest_reward",
                dialog_content_id = 0,
            },
        }, {
            completed_20615_morheim_npc_dialog = true,
            completed_20620_start_dialog = true,
            completed_20620_task_teleport = true,
            completed_20620_after_teleport_npc_dialog = true,
            completed_20620_stigma_socket = true,
            completed_20620_after_stigma_teleport = true,
            completed_20620_after_stigma_npc_dialog = true,
            completed_20620_obelisk = true,
            completed_20620_after_obelisk_teleport = true,
        })

        T.assert_eq(next_action.name, "ClickDialogLastContinuousOk")
        T.assert_eq(next_action.params.quest_id, 20620)
        T.assert_eq(next_action.params.quest_step, 4)
        T.assert_eq(next_action.params.stage, "quest_20620_after_obelisk_npc")
        T.assert_eq(next_action.params.interact_id, 2147535533)
        T.assert_eq(next_action.params.click_x, 25)
    end)

    T.test("idles after quest 20620 after-obelisk npc dialog completed", function()
        local quest = load_module()
        local next_action = quest.nextAction({
            quests = {
                { id = 20620, tab = 0, status_code = 4, req_count = 4, seq = 0, lv_num = 20 },
                { id = 20621, tab = 0, status_code = 6, req_count = 0, seq = 1, lv_num = 22 },
            },
            char = quest_20620_after_obelisk_npc_char(20),
            big_map_id = 220020000,
        }, {
            completed_20615_morheim_npc_dialog = true,
            completed_20620_start_dialog = true,
            completed_20620_task_teleport = true,
            completed_20620_after_teleport_npc_dialog = true,
            completed_20620_stigma_socket = true,
            completed_20620_after_stigma_teleport = true,
            completed_20620_after_stigma_npc_dialog = true,
            completed_20620_obelisk = true,
            completed_20620_after_obelisk_teleport = true,
            completed_20620_after_obelisk_npc_dialog = true,
        })

        T.assert_eq(next_action.name, "Idle")
        T.assert_eq(next_action.params.quest_id, 20620)
        T.assert_eq(next_action.params.stage, "quest_20620_after_obelisk_npc")
    end)

    T.test("moves to quest 20621 level 22 grind point after quest 20620 completed", function()
        local quest = load_module()
        local next_action = quest.nextAction({
            quests = {
                { id = 20621, tab = 0, status_code = 6, req_count = 0, seq = 1, lv_num = 22 },
                { id = 20622, tab = 0, status_code = 6, req_count = 0, seq = 2, lv_num = 25 },
            },
            char = quest_20620_after_obelisk_npc_char(20),
            big_map_id = 220020000,
        }, {
            completed_20620_after_obelisk_npc_dialog = true,
        })

        T.assert_eq(next_action.name, "NavigateToGrindPoint")
        T.assert_eq(next_action.params.quest_id, 20621)
        T.assert_eq(next_action.params.stage, "quest_20621_level22_grind")
        T.assert_eq(next_action.params.required_level, 22)
        T.assert_eq(next_action.params.char_level, 20)
        T.assert_eq(next_action.params.x, 174.508)
        T.assert_eq(next_action.params.y, 2298.396)
        T.assert_eq(next_action.params.z, 438.510)
    end)

    T.test("starts quest 20621 level 22 grind at fixed point", function()
        local quest = load_module()
        local next_action = quest.nextAction({
            quests = {
                { id = 20621, tab = 0, status_code = 6, req_count = 0, seq = 1, lv_num = 22 },
                { id = 20622, tab = 0, status_code = 6, req_count = 0, seq = 2, lv_num = 25 },
            },
            char = quest_20621_level22_grind_char(20),
            big_map_id = 220020000,
        }, {
            completed_20620_after_obelisk_npc_dialog = true,
        })

        T.assert_eq(next_action.name, "StartStationaryGrind")
        T.assert_eq(next_action.params.quest_id, 20621)
        T.assert_eq(next_action.params.stage, "quest_20621_level22_grind")
        T.assert_eq(next_action.params.required_level, 22)
        T.assert_eq(next_action.params.char_level, 20)
        T.assert_eq(next_action.params.until_level, 22)
        T.assert_eq(next_action.params.requires_combat, true)
        T.assert_eq(next_action.params.task_step, "grind")
        T.assert_eq(next_action.params.x, 174.508)
        T.assert_eq(next_action.params.y, 2298.396)
        T.assert_eq(next_action.params.z, 438.510)
    end)

    T.test("waits while quest 20621 level 22 grind is active", function()
        local quest = load_module()
        local next_action = quest.nextAction({
            quests = {
                { id = 20621, tab = 0, status_code = 6, req_count = 0, seq = 1, lv_num = 22 },
            },
            char = quest_20621_level22_grind_char(21),
            big_map_id = 220020000,
        }, {
            completed_20620_after_obelisk_npc_dialog = true,
            active_20611_grind = true,
            active_20611_grind_stage = "quest_20621_level22_grind",
            level_grind_quest_id = 20621,
            level_grind_required_level = 22,
        })

        T.assert_eq(next_action.name, "WaitLevelGrind")
        T.assert_eq(next_action.params.quest_id, 20621)
        T.assert_eq(next_action.params.stage, "quest_20621_level22_grind")
        T.assert_eq(next_action.params.required_level, 22)
        T.assert_eq(next_action.params.char_level, 21)
    end)

    T.test("idles after quest 20621 reaches level 22", function()
        local quest = load_module()
        local next_action = quest.nextAction({
            quests = {
                { id = 20621, tab = 0, status_code = 6, req_count = 0, seq = 1, lv_num = 22 },
            },
            char = quest_20621_level22_grind_char(22),
            big_map_id = 220020000,
        }, {
            completed_20620_after_obelisk_npc_dialog = true,
            active_20611_grind = true,
            active_20611_grind_stage = "quest_20621_level22_grind",
            level_grind_quest_id = 20621,
            level_grind_required_level = 22,
        })

        T.assert_eq(next_action.name, "Idle")
        T.assert_eq(next_action.params.quest_id, 20621)
        T.assert_eq(next_action.params.stage, "quest_20621_level22_grind")
        T.assert_eq(next_action.params.required_level, 22)
        T.assert_eq(next_action.params.char_level, 22)
    end)

    T.test("does not retry quest 20620 task teleport after stigma completed when teleport flag is missing", function()
        local quest = load_module()
        local next_action = quest.nextAction({
            quests = {
                { id = 20620, tab = 0, status_code = 3, req_count = 3, seq = 0, lv_num = 20 },
                { id = 20621, tab = 0, status_code = 6, req_count = 0, seq = 1, lv_num = 22 },
            },
            char = quest_20620_after_teleport_npc_char(20),
            big_map_id = 220020000,
        }, {
            completed_20615_morheim_npc_dialog = true,
            completed_20620_start_dialog = true,
            completed_20620_task_teleport = false,
            completed_20620_after_teleport_npc_dialog = true,
            completed_20620_stigma_socket = true,
        })

        T.assert_eq(next_action.name, "QuestTeleport")
        T.assert_eq(next_action.params.quest_id, 20620)
        T.assert_eq(next_action.params.stage, "quest_20620_after_stigma_teleport")
    end)

    T.test("does not start quest 20614 level grind after quest 20613 start dialog", function()
        local quest = load_module()
        local next_action = quest.nextAction({
            quests = {
                { id = 20614, tab = 0, status_code = 6, req_count = 0, seq = 4, lv_num = 17 },
                { id = 20615, tab = 0, status_code = 6, req_count = 0, seq = 5, lv_num = 20 },
            },
            char = quest_20613_after_start_reward_npc_char(),
            big_map_id = 220010000,
        }, {
            completed_20613_task_teleport = true,
            completed_20613_start_dialog = true,
            completed_20613_after_start_teleport = true,
        })

        T.assert_true(next_action.name ~= "StartStationaryGrind", "20614 grind must wait for 20613 reward dialog")
        T.assert_eq(next_action.params.quest_id, 20613)
        T.assert_eq(next_action.params.stage, "quest_20613_after_start_reward_npc")
    end)

    T.test("idles after quest 20612 task teleport is completed", function()
        local quest = load_module()
        local next_action = quest.nextAction({
            quests = {
                { id = 20612, tab = 0, status_code = 3, req_count = 1, seq = 2, lv_num = 11 },
            },
            char = { x = 520.00, y = 2260.00, z = 249.00, level = 11 },
            big_map_id = 220010000,
        }, {
            completed_20612_start_dialog = true,
            completed_20612_task_teleport = true,
        })

        T.assert_eq(next_action.name, "Idle")
        T.assert_eq(next_action.params.quest_id, 20612)
        T.assert_eq(next_action.params.stage, "quest_20612_task_teleport")
    end)

    T.test("uses earliest level blocked yellow mission immediate move after reaching level", function()
        local quest = load_module()
        local next_action = quest.nextAction({
            quests = {
                { id = 20615, tab = 0, status_code = 6, req_count = 0, seq = 5, lv_num = 20 },
                { id = 20611, tab = 0, status_code = 6, req_count = 0, seq = 1, lv_num = 8 },
                { id = 20612, tab = 0, status_code = 6, req_count = 0, seq = 2, lv_num = 11 },
            },
            char = { x = 190.96, y = 2693.78, z = 300.62, level = 8 },
            big_map_id = 220010000,
            ui = {
                quest_panel_visible = true,
            },
        }, {
            clicked_20611_indicator_title = true,
        })

        T.assert_eq(next_action.name, "QuestTeleport")
        T.assert_eq(next_action.params.quest_id, 20611)
        T.assert_eq(next_action.params.stage, "quest_20611_level_move")
        T.assert_eq(next_action.params.required_level, 8)
        T.assert_eq(next_action.params.char_level, 8)
        T.assert_eq(next_action.params.open_panel_key, false)
        T.assert_eq(next_action.params.require_panel_visible, true)
        T.assert_eq(next_action.params.wait_teleport, true)
    end)

    T.test("waits while yellow mission level grind is active", function()
        local quest = load_module()
        local next_action = quest.nextAction({
            quests = {
                { id = 20611, tab = 0, status_code = 6, req_count = 0, seq = 1, lv_num = 8 },
                { id = 20612, tab = 0, status_code = 6, req_count = 0, seq = 2, lv_num = 11 },
            },
            char = { x = 190.96, y = 2693.78, z = 300.62, level = 7 },
            big_map_id = 220010000,
        }, {
            active_20611_grind = true,
            active_20611_grind_stage = "quest_20611_level_grind",
            level_grind_quest_id = 20611,
            level_grind_required_level = 8,
        })

        T.assert_eq(next_action.name, "WaitLevelGrind")
        T.assert_eq(next_action.params.quest_id, 20611)
        T.assert_eq(next_action.params.required_level, 8)
        T.assert_eq(next_action.params.char_level, 7)
    end)

    T.test("teleports tracked yellow mission instead of switching to next level blocked mission", function()
        local quest = load_module()
        local next_action = quest.nextAction({
            quests = {
                { id = 20611, tab = 0, status_code = 3, req_count = 0, seq = 1, lv_num = 8 },
                { id = 20612, tab = 0, status_code = 6, req_count = 0, seq = 2, lv_num = 11 },
            },
            char = { x = 197.70, y = 2697.09, z = 301.04, level = 10 },
            big_map_id = 220010000,
            ui = {
                quest_panel_visible = true,
            },
        }, {
            active_20611_grind = true,
            active_20611_grind_stage = "quest_20611_level_grind",
            level_grind_quest_id = 20611,
            level_grind_required_level = 8,
            clicked_20611_indicator_title = true,
        })

        T.assert_eq(next_action.name, "QuestTeleport")
        T.assert_eq(next_action.params.quest_id, 20611)
        T.assert_eq(next_action.params.stage, "quest_20611_level_move")
        T.assert_eq(next_action.params.required_level, 8)
        T.assert_eq(next_action.params.char_level, 10)
        T.assert_eq(next_action.params.open_panel_key, false)
        T.assert_eq(next_action.params.require_panel_visible, true)
        T.assert_eq(next_action.params.wait_teleport, true)
    end)

    T.test("teleports active yellow mission after restart when level requirement is met", function()
        local quest = load_module()
        local next_action = quest.nextAction({
            quests = {
                { id = 20611, tab = 0, status_code = 3, req_count = 0, seq = 1, lv_num = 8 },
                { id = 20612, tab = 0, status_code = 6, req_count = 0, seq = 2, lv_num = 11 },
            },
            char = { x = 197.70, y = 2697.09, z = 301.04, level = 10 },
            big_map_id = 220010000,
            ui = {
                quest_panel_visible = true,
            },
        }, {
            clicked_20611_indicator_title = true,
        })

        T.assert_eq(next_action.name, "QuestTeleport")
        T.assert_eq(next_action.params.quest_id, 20611)
        T.assert_eq(next_action.params.stage, "quest_20611_level_move")
        T.assert_eq(next_action.params.required_level, 8)
        T.assert_eq(next_action.params.char_level, 10)
        T.assert_eq(next_action.params.open_panel_key, false)
        T.assert_eq(next_action.params.require_panel_visible, true)
        T.assert_eq(next_action.params.wait_teleport, true)
    end)

    T.test("waits for quest 20611 teleport position change before npc dialog", function()
        local quest = load_module()
        local next_action = quest.nextAction({
            quests = {
                { id = 20611, tab = 0, status_code = 3, req_count = 0, seq = 1, lv_num = 8 },
            },
            char = { x = 190.96, y = 2693.78, z = 300.62, level = 10 },
            big_map_id = 220010000,
        }, {
            waiting_teleport = true,
            teleport_stage = "quest_20611_level_move",
            teleport_start_pos = { x = 190.96, y = 2693.78, z = 300.62 },
            teleport_start_big_map_id = 220010000,
        })

        T.assert_eq(next_action.name, "WaitPositionChanged")
        T.assert_eq(next_action.params.quest_id, 20611)
        T.assert_eq(next_action.params.stage, "quest_20611_level_move")
    end)

    T.test("completes quest 20611 teleport after position changes", function()
        local quest = load_module()
        local next_action = quest.nextAction({
            quests = {
                { id = 20611, tab = 0, status_code = 3, req_count = 0, seq = 1, lv_num = 8 },
            },
            char = mission_npc_char(),
            big_map_id = 220010000,
        }, {
            waiting_teleport = true,
            teleport_stage = "quest_20611_level_move",
            teleport_start_pos = { x = 190.96, y = 2693.78, z = 300.62 },
            teleport_start_big_map_id = 220010000,
        })

        T.assert_eq(next_action.name, "CompleteQuestTeleport")
        T.assert_eq(next_action.params.quest_id, 20611)
        T.assert_eq(next_action.params.stage, "quest_20611_level_move")
    end)

    T.test("does not repeat yellow mission immediate move once requested", function()
        local quest = load_module()
        local next_action = quest.nextAction({
            quests = {
                { id = 20611, tab = 0, status_code = 3, req_count = 0, seq = 1, lv_num = 8 },
            },
            char = { x = 190.96, y = 2693.78, z = 300.62, level = 8 },
            big_map_id = 220010000,
        }, {
            completed_20611_level_move = true,
            level_move_quest_id = 20611,
        })

        T.assert_eq(next_action.name, "Idle")
        T.assert_eq(next_action.params.quest_id, 20611)
    end)

    T.test("opens quest 20611 mission npc after teleport landing", function()
        local quest = load_module()
        local next_action = quest.nextAction({
            quests = {
                { id = 20611, tab = 0, status_code = 3, req_count = 0, seq = 1, lv_num = 8 },
                { id = 20612, tab = 0, status_code = 6, req_count = 0, seq = 2, lv_num = 11 },
            },
            char = mission_npc_char(),
            big_map_id = 220010000,
        }, {
            completed_20611_level_move = true,
            level_move_quest_id = 20611,
        })

        T.assert_eq(next_action.name, "InteractNpc")
        T.assert_eq(next_action.params.quest_id, 20611)
        T.assert_eq(next_action.params.stage, "quest_20611_mission_npc")
        T.assert_eq(next_action.params.interact_id, 2147503111)
        T.assert_eq(next_action.params.npc_name_key, "MQ20611_NPC_001_MISSION")
    end)

    T.test("clicks yellow quest entry in quest 20611 npc select list", function()
        local quest = load_module()
        local next_action = quest.nextAction({
            quests = {
                { id = 20611, tab = 0, status_code = 3, req_count = 0, seq = 1, lv_num = 8 },
            },
            char = mission_npc_char(),
            big_map_id = 220010000,
            dialog = {
                npc_dialog_id = 2147503111,
                dialog_content_id = 10,
                quest_id = 0,
                type_text = "select_quest",
            },
        }, {
            completed_20611_level_move = true,
            level_move_quest_id = 20611,
        })

        T.assert_eq(next_action.name, "ClickDialogXContinuous")
        T.assert_eq(next_action.params.quest_id, 20611)
        T.assert_eq(next_action.params.stage, "quest_20611_mission_npc")
        T.assert_eq(next_action.params.expected_content_id, 10)
        T.assert_eq(next_action.params.content_id, 10)
        T.assert_eq(next_action.params.type_text, "select_quest")
        T.assert_eq(next_action.params.click_x, 25)
        T.assert_eq(next_action.params.click_y, 324)
        T.assert_eq(next_action.params.click_y_tolerance, 8)
        T.assert_eq(next_action.params.npc_name_key, "MQ20611_NPC_001_MISSION")
    end)

    T.test("continues known quest 20611 mission dialog chain", function()
        local quest = load_module()
        local cases = {
            { type_text = "select1", content_id = 1011, action = "ClickDialogXContinuous" },
            { type_text = "select1_1", content_id = 1012, action = "ClickDialogXContinuous" },
            { type_text = "select1_1_1", content_id = 1013, action = "ClickDialogXContinuous" },
            { type_text = "select1_1_1_1", content_id = 1014, action = "ClickDialogXContinuous" },
        }
        for _, case in ipairs(cases) do
            local next_action = quest.nextAction({
                quests = {
                    { id = 20611, tab = 0, status_code = 3, req_count = 0, seq = 1, lv_num = 8 },
                },
                char = mission_npc_char(),
                big_map_id = 220010000,
                dialog = {
                    npc_dialog_id = 2147503111,
                    dialog_content_id = case.content_id,
                    quest_id = 20611,
                    type_text = case.type_text,
                },
            }, {
                completed_20611_level_move = true,
                level_move_quest_id = 20611,
            })

            T.assert_eq(next_action.name, case.action, case.type_text)
            T.assert_eq(next_action.params.expected_content_id, case.content_id, case.type_text)
            T.assert_eq(next_action.params.stage, "quest_20611_mission_npc", case.type_text)
        end
    end)

    T.test("prioritizes opened quest 20611 mission dialog before obelisk step", function()
        local quest = load_module()
        local next_action = quest.nextAction({
            quests = {
                { id = 20611, tab = 0, status_code = 3, req_count = 1, seq = 1, lv_num = 8 },
            },
            char = mission_npc_char(),
            big_map_id = 220010000,
            dialog = {
                npc_dialog_id = 2147503111,
                dialog_content_id = 1011,
                quest_id = 20611,
                type_text = "select1",
            },
        }, {
            completed_20611_level_move = true,
            level_move_quest_id = 20611,
        })

        T.assert_eq(next_action.name, "ClickDialogXContinuous")
        T.assert_eq(next_action.params.stage, "quest_20611_mission_npc")
        T.assert_eq(next_action.params.expected_content_id, 1011)
        T.assert_eq(next_action.params.interact_id, 2147503111)
    end)

    T.test("opens quest 20611 obelisk after mission dialog", function()
        local quest = load_module()
        local next_action = quest.nextAction({
            quests = {
                { id = 20611, tab = 0, status_code = 3, req_count = 1, seq = 1, lv_num = 8 },
            },
            char = obelisk_char(),
            big_map_id = 220010000,
        }, {
            completed_20611_mission_dialog = true,
        })

        T.assert_eq(next_action.name, "InteractNpc")
        T.assert_eq(next_action.params.quest_id, 20611)
        T.assert_eq(next_action.params.quest_step, 1)
        T.assert_eq(next_action.params.stage, "quest_20611_obelisk")
        T.assert_eq(next_action.params.interact_id, 2147505051)
        T.assert_eq(next_action.params.npc_name_key, "MQ20611_NPC_002_OBELISK")
    end)

    T.test("keeps earliest yellow mission when later active mission appears first", function()
        local quest = load_module()
        local next_action = quest.nextAction({
            quests = {
                { id = 20612, tab = 0, status_code = 3, req_count = 0, seq = 2, lv_num = 11 },
                { id = 20611, tab = 0, status_code = 3, req_count = 1, seq = 1, lv_num = 8 },
            },
            char = obelisk_char(),
            big_map_id = 220010000,
        }, {
            completed_20611_mission_dialog = true,
        })

        T.assert_eq(next_action.name, "InteractNpc")
        T.assert_eq(next_action.params.quest_id, 20611)
        T.assert_eq(next_action.params.stage, "quest_20611_obelisk")
    end)

    T.test("blocks quest 20612 active branch while quest 20611 is level blocked", function()
        local quest = load_module()
        local next_action = quest.nextAction({
            quests = {
                { id = 20612, tab = 0, status_code = 3, req_count = 0, seq = 2, lv_num = 11 },
                { id = 20611, tab = 0, status_code = 6, req_count = 0, seq = 1, lv_num = 8 },
            },
            char = { x = 190.96, y = 2693.78, z = 300.62, level = 7 },
            big_map_id = 220010000,
        })

        T.assert_eq(next_action.name, "StartStationaryGrind")
        T.assert_eq(next_action.params.quest_id, 20611)
        T.assert_eq(next_action.params.stage, "quest_20611_level_grind")
        T.assert_eq(next_action.params.required_level, 8)
    end)

    T.test("blocks blue reward while earlier yellow mission is executable", function()
        local quest = load_module()
        local next_action = quest.nextAction({
            quests = {
                remote_reward_quest(),
                { id = 20611, tab = 0, status_code = 3, req_count = 1, seq = 1, lv_num = 8 },
            },
            char = obelisk_char(),
            big_map_id = 220010000,
        }, {
            completed_20611_mission_dialog = true,
        })

        T.assert_eq(next_action.name, "InteractNpc")
        T.assert_eq(next_action.params.quest_id, 20611)
        T.assert_eq(next_action.params.stage, "quest_20611_obelisk")
    end)

    T.test("clicks quest 20611 obelisk confirm after npc interaction", function()
        local quest = load_module()
        local next_action = quest.nextAction({
            quests = {
                { id = 20611, tab = 0, status_code = 3, req_count = 1, seq = 1, lv_num = 8 },
            },
            char = obelisk_char(),
            big_map_id = 220010000,
        }, {
            completed_20611_mission_dialog = true,
            opened_20611_obelisk = true,
        })

        T.assert_eq(next_action.name, "ClickObeliskConfirm")
        T.assert_eq(next_action.params.quest_id, 20611)
        T.assert_eq(next_action.params.quest_step, 1)
        T.assert_eq(next_action.params.stage, "quest_20611_obelisk")
        T.assert_eq(next_action.params.confirm_x, 684)
        T.assert_eq(next_action.params.confirm_y, 437)
        T.assert_eq(next_action.params.confirm_tolerance, 90)
    end)

    T.test("clicks quest 20611 obelisk confirm when popup is visible after restart", function()
        local quest = load_module()
        local next_action = quest.nextAction({
            quests = {
                { id = 20611, tab = 0, status_code = 3, req_count = 1, seq = 1, lv_num = 8 },
            },
            char = obelisk_char(),
            big_map_id = 220010000,
            ui = {
                obelisk_confirm_visible = true,
            },
        }, {
            completed_20611_mission_dialog = true,
        })

        T.assert_eq(next_action.name, "ClickObeliskConfirm")
        T.assert_eq(next_action.params.quest_id, 20611)
        T.assert_eq(next_action.params.stage, "quest_20611_obelisk")
    end)

    T.test("opens current tracked quest before quest 20611 immediate move", function()
        local quest = load_module()
        local next_action = quest.nextAction({
            quests = {
                { id = 20611, tab = 0, status_code = 3, req_count = 2, seq = 1, lv_num = 8 },
            },
            char = obelisk_char(),
            big_map_id = 220010000,
            ui = {
                quest_panel_visible = false,
            },
        })

        T.assert_eq(next_action.name, "ClickUiControl")
        T.assert_eq(next_action.params.quest_id, 20611)
        T.assert_eq(next_action.params.quest_step, 2)
        T.assert_eq(next_action.params.stage, "quest_20611_indicator_title")
        T.assert_eq(next_action.params.parent, "quest_indicator_dialog")
        T.assert_eq(next_action.params.name, "prototype")
    end)

    T.test("does not trust visible quest panel before current tracker title click", function()
        local quest = load_module()
        local next_action = quest.nextAction({
            quests = {
                { id = 20611, tab = 0, status_code = 3, req_count = 2, seq = 1, lv_num = 8 },
            },
            char = obelisk_char(),
            big_map_id = 220010000,
            ui = {
                quest_panel_visible = true,
            },
        })

        T.assert_eq(next_action.name, "ClickUiControl")
        T.assert_eq(next_action.params.quest_id, 20611)
        T.assert_eq(next_action.params.stage, "quest_20611_indicator_title")
    end)

    T.test("tries next current tracker candidate when previous click did not open panel", function()
        local quest = load_module()
        local next_action = quest.nextAction({
            quests = {
                { id = 20611, tab = 0, status_code = 3, req_count = 2, seq = 1, lv_num = 8 },
            },
            char = obelisk_char(),
            big_map_id = 220010000,
            ui = {
                quest_panel_visible = false,
            },
        }, {
            clicked_20611_indicator_title = true,
            clicked_20611_indicator_entry_name = "prototype",
        })

        T.assert_eq(next_action.name, "ClickUiControl")
        T.assert_eq(next_action.params.quest_id, 20611)
        T.assert_eq(next_action.params.stage, "quest_20611_indicator_title")
        T.assert_eq(next_action.params.name, "htmltext")
        T.assert_eq(next_action.params.previous_name, "prototype")
    end)

    T.test("waits for position change after current tracker teleport click", function()
        local quest = load_module()
        local next_action = quest.nextAction({
            quests = {
                { id = 20611, tab = 0, status_code = 3, req_count = 2, seq = 1, lv_num = 8 },
            },
            char = obelisk_char(),
            big_map_id = 220010000,
            ui = {
                quest_panel_visible = false,
            },
        }, {
            clicked_20611_indicator_title = true,
            clicked_20611_indicator_entry_name = "title",
        })

        T.assert_eq(next_action.name, "ClickUiControlWaitTeleport")
        T.assert_eq(next_action.params.quest_id, 20611)
        T.assert_eq(next_action.params.quest_step, 2)
        T.assert_eq(next_action.params.stage, "quest_20611_target_teleport")
        T.assert_eq(next_action.params.parent, "quest_indicator_dialog")
        T.assert_eq(next_action.params.name, "teleport")
        T.assert_eq(next_action.params.previous_name, "title")
        T.assert_eq(next_action.params.wait_teleport, true)
    end)

    T.test("uses quest 20611 immediate move after current quest panel opens", function()
        local quest = load_module()
        local next_action = quest.nextAction({
            quests = {
                { id = 20611, tab = 0, status_code = 3, req_count = 2, seq = 1, lv_num = 8 },
            },
            char = obelisk_char(),
            big_map_id = 220010000,
            ui = {
                quest_panel_visible = true,
            },
        }, {
            clicked_20611_indicator_title = true,
        })

        T.assert_eq(next_action.name, "QuestTeleport")
        T.assert_eq(next_action.params.quest_id, 20611)
        T.assert_eq(next_action.params.quest_step, 2)
        T.assert_eq(next_action.params.stage, "quest_20611_target_teleport")
        T.assert_eq(next_action.params.open_panel_key, false)
        T.assert_eq(next_action.params.require_panel_visible, true)
        T.assert_eq(next_action.params.wait_teleport, true)
    end)

    T.test("opens quest 20611 target npc after target teleport landing", function()
        local quest = load_module()
        local next_action = quest.nextAction({
            quests = {
                { id = 20611, tab = 0, status_code = 3, req_count = 2, seq = 1, lv_num = 8 },
            },
            char = target_npc_char(),
            big_map_id = 220010000,
        }, {
            completed_20611_target_teleport = true,
        })

        T.assert_eq(next_action.name, "InteractNpc")
        T.assert_eq(next_action.params.quest_id, 20611)
        T.assert_eq(next_action.params.quest_step, 2)
        T.assert_eq(next_action.params.stage, "quest_20611_target_npc")
        T.assert_eq(next_action.params.interact_id, 2147520815)
        T.assert_eq(next_action.params.npc_name_key, "MQ20611_NPC_003_TARGET")
    end)

    T.test("opens quest 20611 target npc when already near target", function()
        local quest = load_module()
        local next_action = quest.nextAction({
            quests = {
                { id = 20611, tab = 0, status_code = 3, req_count = 2, seq = 1, lv_num = 8 },
            },
            char = target_npc_char(),
            big_map_id = 220010000,
        })

        T.assert_eq(next_action.name, "InteractNpc")
        T.assert_eq(next_action.params.stage, "quest_20611_target_npc")
        T.assert_eq(next_action.params.npc_name_key, "MQ20611_NPC_003_TARGET")
    end)

    T.test("continuously clicks x in quest 20611 target npc select list", function()
        local quest = load_module()
        local next_action = quest.nextAction({
            quests = {
                { id = 20611, tab = 0, status_code = 3, req_count = 2, seq = 1, lv_num = 8 },
            },
            char = target_npc_char(),
            big_map_id = 220010000,
            dialog = {
                npc_dialog_id = 2147520815,
                dialog_content_id = 10,
                quest_id = 0,
                type_text = "select_quest",
            },
        })

        T.assert_eq(next_action.name, "ClickDialogXContinuous")
        T.assert_eq(next_action.params.quest_id, 20611)
        T.assert_eq(next_action.params.quest_step, 2)
        T.assert_eq(next_action.params.stage, "quest_20611_target_npc")
        T.assert_eq(next_action.params.expected_content_id, 10)
        T.assert_eq(next_action.params.content_id, 10)
        T.assert_eq(next_action.params.type_text, "select_quest")
        T.assert_eq(next_action.params.click_x, 25)
        T.assert_eq(next_action.params.interact_id, 2147520815)
        T.assert_eq(next_action.params.npc_name_key, "MQ20611_NPC_003_TARGET")
    end)

    T.test("teleports quest 20611 to hotspot after target npc dialog", function()
        local quest = load_module()
        local next_action = quest.nextAction({
            quests = {
                { id = 20611, tab = 0, status_code = 3, req_count = 2, seq = 1, lv_num = 8 },
            },
            char = target_npc_char(),
            big_map_id = 220010000,
        }, {
            completed_20611_target_dialog = true,
        })

        T.assert_eq(next_action.name, "MapNodeTeleportByName")
        T.assert_eq(next_action.params.quest_id, 20611)
        T.assert_eq(next_action.params.quest_step, 2)
        T.assert_eq(next_action.params.stage, "quest_20611_hotspot_teleport")
        T.assert_eq(next_action.params.node_name, "투나프레 호수")
        T.assert_eq(next_action.params.node_name_en, "HOTSPOT_DF1_04")
        T.assert_eq(next_action.params.node_id, 66)
        T.assert_eq(next_action.params.wait_teleport, true)
    end)

    T.test("resumes quest 20611 hotspot teleport after target npc advances to step 3", function()
        local quest = load_module()
        local next_action = quest.nextAction({
            quests = {
                { id = 20611, tab = 0, status_code = 3, req_count = 3, seq = 1, lv_num = 8 },
            },
            char = target_npc_char(),
            big_map_id = 220010000,
        })

        T.assert_eq(next_action.name, "MapNodeTeleportByName")
        T.assert_eq(next_action.params.quest_id, 20611)
        T.assert_eq(next_action.params.quest_step, 3)
        T.assert_eq(next_action.params.stage, "quest_20611_hotspot_teleport")
        T.assert_eq(next_action.params.node_id, 66)
    end)

    T.test("opens hotspot reward npc after restart at hotspot", function()
        local quest = load_module()
        local next_action = quest.nextAction({
            quests = {
                { id = 20611, tab = 0, status_code = 3, req_count = 3, seq = 1, lv_num = 8 },
            },
            char = hotspot_reward_char(),
            big_map_id = 220010000,
        })

        T.assert_eq(next_action.name, "InteractNpc")
        T.assert_eq(next_action.params.quest_id, 20611)
        T.assert_eq(next_action.params.quest_step, 3)
        T.assert_eq(next_action.params.stage, "quest_20611_hotspot_reward_npc")
        T.assert_eq(next_action.params.interact_id, 2147515597)
    end)

    T.test("continuous x-clicks quest 20611 hotspot reward npc dialog", function()
        local quest = load_module()
        local next_action = quest.nextAction({
            quests = {
                { id = 20611, tab = 0, status_code = 4, req_count = 3, seq = 0, lv_num = 8 },
            },
            char = hotspot_reward_char(),
            big_map_id = 220010000,
            dialog = {
                npc_dialog_id = 2147515597,
                dialog_content_id = 10002,
                quest_id = 20611,
                type_text = "select_success",
            },
        })

        T.assert_eq(next_action.name, "ClickDialogXContinuous")
        T.assert_eq(next_action.params.quest_id, 20611)
        T.assert_eq(next_action.params.quest_step, 3)
        T.assert_eq(next_action.params.stage, "quest_20611_hotspot_reward_npc")
        T.assert_eq(next_action.params.expected_content_id, 10002)
        T.assert_eq(next_action.params.content_id, 10002)
        T.assert_eq(next_action.params.type_text, "select_success")
        T.assert_eq(next_action.params.click_x, 25)
        T.assert_eq(next_action.params.interact_id, 2147515597)
        T.assert_eq(next_action.params.npc_name_key, "MQ20611_NPC_004_HOTSPOT_REWARD")
    end)

    T.test("dumps unknown quest 20611 target npc dialog pages", function()
        local quest = load_module()
        local next_action = quest.nextAction({
            quests = {
                { id = 20611, tab = 0, status_code = 3, req_count = 2, seq = 1, lv_num = 8 },
            },
            char = target_npc_char(),
            big_map_id = 220010000,
            dialog = {
                npc_dialog_id = 2147520815,
                dialog_content_id = 1201,
                quest_id = 20611,
                type_text = "select_target_followup",
            },
        })

        T.assert_eq(next_action.name, "DumpDialog")
        T.assert_eq(next_action.params.quest_id, 20611)
        T.assert_eq(next_action.params.stage, "quest_20611_target_npc")
        T.assert_eq(next_action.params.content_id, 1201)
    end)

    T.test("does not click stale quest 20611 dictionary teleport before current quest panel", function()
        local quest = load_module()
        local next_action = quest.nextAction({
            quests = {
                { id = 20611, tab = 0, status_code = 3, req_count = 2, seq = 1, lv_num = 8 },
            },
            char = obelisk_char(),
            big_map_id = 220010000,
            ui = {
                dictionary_teleport_to_npc = true,
            },
        })

        T.assert_eq(next_action.name, "ClickUiControl")
        T.assert_eq(next_action.params.quest_id, 20611)
        T.assert_eq(next_action.params.stage, "quest_20611_indicator_title")
    end)

    T.test("waits for quest 20611 target teleport landing", function()
        local quest = load_module()
        local next_action = quest.nextAction({
            quests = {
                { id = 20611, tab = 0, status_code = 3, req_count = 2, seq = 1, lv_num = 8 },
            },
            char = obelisk_char(),
            big_map_id = 220010000,
        }, {
            waiting_teleport = true,
            teleport_stage = "quest_20611_target_teleport",
            teleport_start_pos = obelisk_char(),
            teleport_start_big_map_id = 220010000,
        })

        T.assert_eq(next_action.name, "WaitPositionChanged")
        T.assert_eq(next_action.params.quest_id, 20611)
        T.assert_eq(next_action.params.stage, "quest_20611_target_teleport")
    end)

    T.test("waits for quest 20611 hotspot map node teleport landing", function()
        local quest = load_module()
        local next_action = quest.nextAction({
            quests = {
                { id = 20611, tab = 0, status_code = 3, req_count = 2, seq = 1, lv_num = 8 },
            },
            char = target_npc_char(),
            big_map_id = 220010000,
        }, {
            waiting_teleport = true,
            teleport_stage = "quest_20611_hotspot_teleport",
            teleport_start_pos = target_npc_char(),
            teleport_start_big_map_id = 220010000,
        })

        T.assert_eq(next_action.name, "WaitPositionChanged")
        T.assert_eq(next_action.params.quest_id, 20611)
        T.assert_eq(next_action.params.stage, "quest_20611_hotspot_teleport")
    end)

    T.test("completes quest 20611 hotspot map node teleport after position changes", function()
        local quest = load_module()
        local next_action = quest.nextAction({
            quests = {
                { id = 20611, tab = 0, status_code = 3, req_count = 2, seq = 1, lv_num = 8 },
            },
            char = { x = 491.00, y = 2301.00, z = 300.00, level = 10 },
            big_map_id = 220010000,
        }, {
            waiting_teleport = true,
            teleport_stage = "quest_20611_hotspot_teleport",
            teleport_start_pos = target_npc_char(),
            teleport_start_big_map_id = 220010000,
        })

        T.assert_eq(next_action.name, "CompleteMapNodeTeleport")
        T.assert_eq(next_action.params.quest_id, 20611)
        T.assert_eq(next_action.params.stage, "quest_20611_hotspot_teleport")
    end)

    T.test("completes quest 20611 target teleport after position changes", function()
        local quest = load_module()
        local next_action = quest.nextAction({
            quests = {
                { id = 20611, tab = 0, status_code = 3, req_count = 2, seq = 1, lv_num = 8 },
            },
            char = { x = 640.00, y = 2380.00, z = 280.00, level = 10 },
            big_map_id = 220010000,
        }, {
            waiting_teleport = true,
            teleport_stage = "quest_20611_target_teleport",
            teleport_start_pos = obelisk_char(),
            teleport_start_big_map_id = 220010000,
        })

        T.assert_eq(next_action.name, "CompleteQuestTeleport")
        T.assert_eq(next_action.params.quest_id, 20611)
        T.assert_eq(next_action.params.stage, "quest_20611_target_teleport")
    end)

    T.test("does not repeat completed quest 20611 obelisk confirm", function()
        local quest = load_module()
        local next_action = quest.nextAction({
            quests = {
                { id = 20611, tab = 0, status_code = 3, req_count = 1, seq = 1, lv_num = 8 },
            },
            char = obelisk_char(),
            big_map_id = 220010000,
        }, {
            completed_20611_obelisk = true,
        })

        T.assert_eq(next_action.name, "Idle")
        T.assert_eq(next_action.params.quest_id, 20611)
        T.assert_eq(next_action.params.quest_step, 1)
    end)

    T.test("idles on unrecorded active 206xx steps", function()
        local quest = load_module()
        local next_action = quest.nextAction({
            quests = {
                { id = 20612, status_code = 3, req_count = 2 },
                { id = 20613, status_code = 6, req_count = 0, seq = 3, lv_num = 14 },
            },
            char = far_char(),
            big_map_id = 220010000,
        })

        T.assert_eq(next_action.name, "Idle")
        T.assert_eq(next_action.params.quest_id, 20612)
        T.assert_eq(next_action.params.quest_step, 2)
    end)

    T.test("starts stationary grind when active and near", function()
        local quest = load_module()
        local next_action = quest.nextAction({
            quests = { remote_active_quest() },
            char = near_char(),
            big_map_id = 220010000,
        })

        T.assert_eq(next_action.name, "StartStationaryGrind")
        T.assert_eq(next_action.params.quest_id, CURRENT_BLUE_TASK)
        T.assert_eq(next_action.params.stage, "quest_20611_grind")
        T.assert_eq(next_action.params.x, near_char().x)
        T.assert_eq(next_action.params.y, near_char().y)
        T.assert_eq(next_action.params.z, near_char().z)
    end)

    T.test("still supports previous blue task id", function()
        local quest = load_module()
        local next_action = quest.nextAction({
            quests = { remote_active_quest(0, LEGACY_BLUE_TASK) },
            char = near_char(),
            big_map_id = 220010000,
        })

        T.assert_eq(next_action.name, "StartStationaryGrind")
        T.assert_eq(next_action.params.quest_id, LEGACY_BLUE_TASK)
    end)

    T.test("does not move to grind point after previous quest without blue task evidence", function()
        local quest = load_module()
        local next_action = quest.nextAction({
            char = far_char(),
            big_map_id = 220010000,
        })

        T.assert_eq(next_action.name, "Idle")
        T.assert_eq(next_action.params.quest_id, quest.remote_reward_quest_id)
    end)

    T.test("does not start stationary grind after previous quest without blue task evidence", function()
        local quest = load_module()
        local next_action = quest.nextAction({
            char = near_char(),
            big_map_id = 220010000,
        })

        T.assert_eq(next_action.name, "Idle")
        T.assert_eq(next_action.params.quest_id, quest.remote_reward_quest_id)
    end)

    T.test("done 206xx candidate does not force grind", function()
        local quest = load_module()
        local next_action = quest.nextAction({
            quests = {
                { id = 20612, status_code = 4, req_count = 5 },
            },
            char = near_char(),
            big_map_id = 220010000,
        })

        T.assert_eq(next_action.name, "Idle")
        T.assert_eq(next_action.params.quest_id, quest.remote_reward_quest_id)
    end)

    T.test("waits while stationary grind is active", function()
        local quest = load_module()
        local next_action = quest.nextAction({
            quests = { remote_active_quest(3) },
            char = near_char(),
            big_map_id = 220010000,
        }, {
            active_20611_grind = true,
        })

        T.assert_eq(next_action.name, "WaitQuestComplete")
        T.assert_eq(next_action.params.quest_step, 3)
    end)

    T.test("completes grind when quest is done", function()
        local quest = load_module()
        local next_action = quest.nextAction({
            quests = { remote_reward_quest() },
            char = near_char(),
            big_map_id = 220010000,
        }, {
            active_20611_grind = true,
        })

        T.assert_eq(next_action.name, "OpenQuestSubmit")
        T.assert_eq(next_action.params.quest_id, CURRENT_BLUE_TASK)
        T.assert_eq(next_action.params.quest_step, 5)
    end)

    T.test("opens remote reward submit when blue task is ready", function()
        local quest = load_module()
        local next_action = quest.nextAction({
            quests = {
                remote_active_quest(5),
                remote_reward_quest(),
            },
            char = near_char(),
            big_map_id = 220010000,
        }, {
            active_20611_grind = true,
        })

        T.assert_eq(next_action.name, "OpenQuestSubmit")
        T.assert_eq(next_action.params.quest_id, CURRENT_BLUE_TASK)
        T.assert_eq(next_action.params.quest_step, 5)
        T.assert_eq(next_action.params.stage, "quest_20611_remote_reward")
    end)

    T.test("confirms remote reward dialog with ok", function()
        local quest = load_module()
        local next_action = quest.nextAction({
            quests = {
                remote_reward_quest(),
            },
            dialog = {
                quest_id = CURRENT_BLUE_TASK,
                dialog_content_id = 56,
                type_text = "select_quest_reward_remote",
            },
            char = near_char(),
            big_map_id = 220010000,
        }, {
            active_20611_grind = false,
        })

        T.assert_eq(next_action.name, "ClickDialogOkCompleteQuest")
        T.assert_eq(next_action.params.quest_id, CURRENT_BLUE_TASK)
        T.assert_eq(next_action.params.content_id, 56)
        T.assert_eq(next_action.params.type_text, "select_quest_reward_remote")
    end)

    T.test("does not run when completed or inactive", function()
        local quest = load_module()
        local completed = quest.nextAction({
            quests = {},
            char = near_char(),
            big_map_id = 220010000,
        }, {
            completed_20611_grind = true,
        })
        local inactive = quest.nextAction({
            quest = { id = 20611, status_code = 6 },
            char = near_char(),
            big_map_id = 220010000,
        })

        T.assert_eq(completed.name, "Idle")
        T.assert_eq(inactive.name, "Idle")
    end)

    T.test("does not run on wrong map", function()
        local quest = load_module()
        local next_action = quest.nextAction({
            quests = { remote_active_quest() },
            char = near_char(),
            big_map_id = 120030000,
        })

        T.assert_eq(next_action.name, "Idle")
    end)

    clear_modules()
    return T.report("aion_main_quest_20611")
end

return { run = run }
