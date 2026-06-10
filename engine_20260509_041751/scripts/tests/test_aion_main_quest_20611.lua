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
        T.assert_eq(next_action.params.required_level, 8)
        T.assert_eq(next_action.params.char_level, 6)
        T.assert_eq(next_action.params.until_level, 8)
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
        })

        T.assert_eq(next_action.name, "QuestTeleport")
        T.assert_eq(next_action.params.quest_id, 20611)
        T.assert_eq(next_action.params.stage, "quest_20611_level_move")
        T.assert_eq(next_action.params.required_level, 8)
        T.assert_eq(next_action.params.char_level, 8)
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
        }, {
            active_20611_grind = true,
            active_20611_grind_stage = "quest_20611_level_grind",
            level_grind_quest_id = 20611,
            level_grind_required_level = 8,
        })

        T.assert_eq(next_action.name, "QuestTeleport")
        T.assert_eq(next_action.params.quest_id, 20611)
        T.assert_eq(next_action.params.stage, "quest_20611_level_move")
        T.assert_eq(next_action.params.required_level, 8)
        T.assert_eq(next_action.params.char_level, 10)
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
        })

        T.assert_eq(next_action.name, "QuestTeleport")
        T.assert_eq(next_action.params.quest_id, 20611)
        T.assert_eq(next_action.params.stage, "quest_20611_level_move")
        T.assert_eq(next_action.params.required_level, 8)
        T.assert_eq(next_action.params.char_level, 10)
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

        T.assert_eq(next_action.name, "ClickDialogX")
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
            { type_text = "select1", content_id = 1011, action = "ClickDialogX" },
            { type_text = "select1_1", content_id = 1012, action = "ClickDialogX" },
            { type_text = "select1_1_1", content_id = 1013, action = "ClickDialogX" },
            { type_text = "select1_1_1_1", content_id = 1014, action = "ClickDialogXCompleteQuest" },
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

    T.test("does not repeat completed quest 20611 mission dialog", function()
        local quest = load_module()
        local next_action = quest.nextAction({
            quests = {
                { id = 20611, tab = 0, status_code = 3, req_count = 1, seq = 1, lv_num = 8 },
            },
            char = mission_npc_char(),
            big_map_id = 220010000,
            dialog = {
                npc_dialog_id = 2147503111,
                dialog_content_id = 1014,
                quest_id = 20611,
                type_text = "select1_1_1_1",
            },
        }, {
            completed_20611_mission_dialog = true,
        })

        T.assert_eq(next_action.name, "Idle")
        T.assert_eq(next_action.params.quest_id, 20611)
        T.assert_eq(next_action.params.quest_step, 1)
    end)

    T.test("ignores active 206xx candidates without blue or level-blocked evidence", function()
        local quest = load_module()
        local next_action = quest.nextAction({
            quests = {
                { id = 20612, status_code = 3, req_count = 2 },
            },
            char = far_char(),
            big_map_id = 220010000,
        })

        T.assert_eq(next_action.name, "Idle")
        T.assert_eq(next_action.params.quest_id, quest.remote_reward_quest_id)
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
