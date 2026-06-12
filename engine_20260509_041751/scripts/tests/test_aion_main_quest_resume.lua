local T = require("tests.test_framework")

local function clear_modules()
    package.loaded["aion.main_quest_resume"] = nil
end

local function load_module()
    clear_modules()
    return require("aion.main_quest_resume")
end

local CURRENT_BLUE_TASK = 24341
local LEGACY_BLUE_TASK = 24340

local function run()
    T.reset()
    T.log("\n=== aion main quest resume tests ===")

    T.test("fresh character stays on 20590", function()
        local resume = load_module()
        local plan = resume.plan({
            char = { name = "Fresh", level = 1 },
            big_map_id = 120030000,
            quests = {
                { id = 20590, status_code = 3, req_count = 0 },
            },
        })

        T.assert_eq(plan.stage, "20590")
        T.assert_nil(plan.flags.completed_20590_reward)
    end)

    T.test("level one follows active later task evidence", function()
        local resume = load_module()
        local plan = resume.plan({
            char = { name = "Fresh", level = 1 },
            big_map_id = 220010000,
            quests = {
                { id = 20610, status_code = 3, req_count = 0 },
            },
        })

        T.assert_eq(plan.stage, "20610_active")
        T.assert_eq(plan.flags.completed_20590_reward, true)
        T.assert_nil(plan.flags.completed_20610_reward)
    end)

    T.test("level one without quest evidence starts from 20590", function()
        local resume = load_module()
        local plan = resume.plan({
            char = { name = "Fresh", level = 1 },
            big_map_id = 120030000,
            quests = {},
        })

        T.assert_eq(plan.stage, "20590")
        T.assert_nil(plan.flags.completed_20590_reward)
    end)

    T.test("active 20590 blocks later task evidence", function()
        local resume = load_module()
        local plan = resume.plan({
            char = { name = "Main", level = 2 },
            big_map_id = 120030000,
            quests = {
                { id = 20590, status_code = 3, req_count = 0 },
                { id = CURRENT_BLUE_TASK, tab = 1, status_code = 3, req_count = 2 },
            },
        })

        T.assert_eq(plan.stage, "20590")
        T.assert_nil(plan.flags.completed_20590_reward)
    end)

    T.test("active 20610 marks 20590 as complete", function()
        local resume = load_module()
        local plan = resume.plan({
            char = { name = "Mid", level = 5 },
            big_map_id = 220010000,
            quests = {
                { id = 20610, status_code = 3, req_count = 0 },
            },
        })

        T.assert_eq(plan.stage, "20610_active")
        T.assert_eq(plan.flags.completed_20590_reward, true)
        T.assert_nil(plan.flags.completed_20610_reward)
    end)

    T.test("done 20610 resumes reward flow", function()
        local resume = load_module()
        local plan = resume.plan({
            char = { name = "Reward", level = 6 },
            big_map_id = 220010000,
            quests = {
                { id = 20610, status_code = 4, req_count = 0 },
            },
        })

        T.assert_eq(plan.stage, "20610_reward")
        T.assert_eq(plan.flags.completed_20590_reward, true)
        T.assert_eq(plan.flags.completed_20610_start_dialog, true)
        T.assert_nil(plan.flags.completed_20610_reward)
    end)

    T.test("remote reward quest resumes submit flow", function()
        local resume = load_module()
        local plan = resume.plan({
            char = { name = "Remote", level = 7 },
            big_map_id = 220010000,
            quests = {
                { id = CURRENT_BLUE_TASK, tab = 1, status_code = 4, req_count = 5 },
            },
        })

        T.assert_eq(plan.stage, "20611_remote_reward")
        T.assert_eq(plan.remote_reward_quest_id, CURRENT_BLUE_TASK)
        T.assert_eq(plan.flags.completed_20590_reward, true)
        T.assert_eq(plan.flags.completed_20610_reward, true)
        T.assert_eq(plan.flags.active_20611_grind, false)
        T.assert_eq(plan.flags.completed_20611_grind, false)
    end)

    T.test("active remote reward quest resumes grind flow", function()
        local resume = load_module()
        local plan = resume.plan({
            char = { name = "Grinding", level = 7 },
            big_map_id = 220010000,
            quests = {
                { id = CURRENT_BLUE_TASK, tab = 1, status_code = 3, req_count = 2 },
            },
        })

        T.assert_eq(plan.stage, "20611_grind_active")
        T.assert_eq(plan.remote_reward_quest_id, CURRENT_BLUE_TASK)
        T.assert_eq(plan.flags.completed_20590_reward, true)
        T.assert_eq(plan.flags.completed_20610_reward, true)
        T.assert_eq(plan.flags.active_20611_grind, false)
    end)

    T.test("open remote reward dialog resumes ok click", function()
        local resume = load_module()
        local plan = resume.plan({
            char = { name = "Dialog", level = 7 },
            big_map_id = 220010000,
            quests = {},
            dialog = {
                quest_id = CURRENT_BLUE_TASK,
                type_text = "select_quest_reward_remote",
            },
        })

        T.assert_eq(plan.stage, "20611_remote_reward")
        T.assert_eq(plan.flags.completed_20610_reward, true)
    end)

    T.test("legacy remote reward quest id still resumes grind flow", function()
        local resume = load_module()
        local plan = resume.plan({
            char = { name = "Legacy", level = 7 },
            big_map_id = 220010000,
            quests = {
                { id = LEGACY_BLUE_TASK, tab = 1, status_code = 3, req_count = 2 },
            },
        })

        T.assert_eq(plan.stage, "20611_grind_active")
        T.assert_eq(plan.remote_reward_quest_id, LEGACY_BLUE_TASK)
        T.assert_eq(plan.flags.completed_20610_reward, true)
    end)

    T.test("level blocked yellow mission resumes immediate move stage", function()
        local resume = load_module()
        local plan = resume.plan({
            char = { name = "After", level = 6 },
            big_map_id = 220010000,
            quests = {
                { id = 20615, status_code = 6, seq = 5, lv_num = 20 },
                { id = 20611, status_code = 6, seq = 1, lv_num = 8 },
            },
        })

        T.assert_eq(plan.stage, "20611_level_blocked")
        T.assert_eq(plan.level_blocked_quest_id, 20611)
        T.assert_eq(plan.flags.completed_20590_reward, true)
        T.assert_eq(plan.flags.completed_20610_reward, true)
        T.assert_eq(plan.flags.active_20611_grind, false)
        T.assert_eq(plan.flags.active_20611_grind_stage, "")
        T.assert_eq(plan.flags.completed_20611_level_move, false)
        T.assert_eq(plan.flags.level_move_quest_id, 0)
    end)

    T.test("active 20612 does not override earlier 20611 level block on resume", function()
        local resume = load_module()
        local plan = resume.plan({
            char = { name = "Ordered", level = 7 },
            big_map_id = 220010000,
            quests = {
                { id = 20612, status_code = 3, req_count = 0, seq = 2, lv_num = 11 },
                { id = 20611, status_code = 6, req_count = 0, seq = 1, lv_num = 8 },
            },
        })

        T.assert_eq(plan.stage, "20611_level_blocked")
        T.assert_eq(plan.level_blocked_quest_id, 20611)
        T.assert_eq(plan.flags.completed_20611_level_move, false)
    end)

    T.test("remote reward does not override active 20610 on resume", function()
        local resume = load_module()
        local plan = resume.plan({
            char = { name = "Ordered", level = 7 },
            big_map_id = 220010000,
            quests = {
                { id = CURRENT_BLUE_TASK, tab = 1, status_code = 4, req_count = 5 },
                { id = 20610, status_code = 3, req_count = 0 },
            },
        })

        T.assert_eq(plan.stage, "20610_active")
        T.assert_eq(plan.flags.completed_20590_reward, true)
        T.assert_nil(plan.flags.completed_20610_reward)
    end)

    T.test("active 20612 does not override active 20611 on resume", function()
        local resume = load_module()
        local plan = resume.plan({
            char = { name = "Ordered", level = 10 },
            big_map_id = 220010000,
            quests = {
                { id = 20612, status_code = 3, req_count = 0, seq = 2, lv_num = 11 },
                { id = 20611, status_code = 3, req_count = 1, seq = 1, lv_num = 8 },
            },
        })

        T.assert_eq(plan.stage, "20611_active")
        T.assert_eq(plan.quest_20611_status, 3)
    end)

    T.test("active quest 20612 start resumes task flow instead of grind inference", function()
        local resume = load_module()
        local plan = resume.plan({
            char = { name = "Q20612", level = 11 },
            big_map_id = 220010000,
            quests = {
                { id = 20612, status_code = 3, req_count = 0, seq = 2, lv_num = 11 },
                { id = 20613, status_code = 6, seq = 3, lv_num = 14 },
            },
        })

        T.assert_eq(plan.stage, "20612_start")
        T.assert_eq(plan.quest_20612_status, 3)
        T.assert_eq(plan.quest_20612_step, 0)
        T.assert_eq(plan.flags.completed_20611_hotspot_reward, true)
        T.assert_eq(plan.flags.completed_20612_start_dialog, false)
        T.assert_eq(plan.flags.completed_20612_task_teleport, false)
        T.assert_eq(plan.flags.completed_20612_reward_dialog, false)
        T.assert_eq(plan.flags.active_20611_grind, false)
    end)

    T.test("active quest 20612 step one resumes task teleport", function()
        local resume = load_module()
        local plan = resume.plan({
            char = { name = "Q20612", level = 11 },
            big_map_id = 220010000,
            quests = {
                { id = 20612, status_code = 3, req_count = 1, seq = 2, lv_num = 11 },
                { id = 20613, status_code = 6, seq = 3, lv_num = 14 },
            },
        })

        T.assert_eq(plan.stage, "20612_task_teleport")
        T.assert_eq(plan.quest_20612_step, 1)
        T.assert_eq(plan.flags.completed_20611_hotspot_reward, true)
        T.assert_eq(plan.flags.completed_20612_start_dialog, true)
        T.assert_eq(plan.flags.completed_20612_task_teleport, false)
        T.assert_eq(plan.flags.completed_20612_reward_dialog, false)
        T.assert_eq(plan.flags.active_20611_grind, false)
    end)

    T.test("done quest 20612 resumes post dialog task teleport before 20613 grind", function()
        local resume = load_module()
        local plan = resume.plan({
            char = { name = "Q20612Done", level = 11 },
            big_map_id = 220010000,
            quests = {
                { id = 20612, status_code = 4, req_count = 0, seq = 2, lv_num = 11 },
                { id = 20613, status_code = 6, req_count = 0, seq = 3, lv_num = 14 },
            },
        })

        T.assert_eq(plan.stage, "20612_task_teleport")
        T.assert_eq(plan.quest_20612_status, 4)
        T.assert_eq(plan.level_blocked_quest_id, 20613)
        T.assert_eq(plan.flags.completed_20611_hotspot_reward, true)
        T.assert_eq(plan.flags.completed_20612_start_dialog, true)
        T.assert_eq(plan.flags.completed_20612_task_teleport, false)
        T.assert_eq(plan.flags.completed_20612_reward_dialog, false)
        T.assert_eq(plan.flags.active_20611_grind, false)
    end)

    T.test("done quest 20612 at reward npc resumes npc dialog before 20613 grind", function()
        local resume = load_module()
        local plan = resume.plan({
            char = { name = "Q20612Done", level = 11, x = 1047.94, y = 2203.23, z = 262.36 },
            big_map_id = 220010000,
            quests = {
                { id = 20612, status_code = 4, req_count = 0, seq = 2, lv_num = 11 },
                { id = 20613, status_code = 6, req_count = 0, seq = 3, lv_num = 14 },
            },
        })

        T.assert_eq(plan.stage, "20612_reward")
        T.assert_eq(plan.flags.completed_20612_start_dialog, true)
        T.assert_eq(plan.flags.completed_20612_task_teleport, true)
        T.assert_eq(plan.flags.completed_20612_reward_dialog, false)
        T.assert_eq(plan.flags.active_20611_grind, false)
    end)

    T.test("quest 20612 level blocked resumes its own level gate after 20611", function()
        local resume = load_module()
        local plan = resume.plan({
            char = { name = "Q20612", level = 10 },
            big_map_id = 220010000,
            quests = {
                { id = 20612, status_code = 6, req_count = 0, seq = 2, lv_num = 11 },
                { id = 20613, status_code = 6, req_count = 0, seq = 3, lv_num = 14 },
            },
        })

        T.assert_eq(plan.stage, "20612_level_blocked")
        T.assert_eq(plan.level_blocked_quest_id, 20612)
        T.assert_eq(plan.flags.completed_20611_hotspot_reward, true)
        T.assert_eq(plan.flags.completed_20612_start_dialog, false)
        T.assert_eq(plan.flags.completed_20612_reward_dialog, false)
        T.assert_eq(plan.flags.active_20611_grind, false)
    end)

    T.test("active quest 20614 step zero resumes first task teleport", function()
        local resume = load_module()
        local plan = resume.plan({
            char = { name = "Q20614", level = 17 },
            big_map_id = 220010000,
            quests = {
                { id = 20614, status_code = 3, req_count = 0, seq = 4, lv_num = 17 },
                { id = 20615, status_code = 6, req_count = 0, seq = 5, lv_num = 20 },
            },
        })

        T.assert_eq(plan.stage, "20614_active")
        T.assert_eq(plan.flags.completed_20614_task_teleport, false)
        T.assert_eq(plan.flags.completed_20614_start_dialog, false)
        T.assert_eq(plan.flags.completed_20614_after_start_teleport, false)
    end)

    T.test("active quest 20614 progressed resumes after-start task teleport", function()
        local resume = load_module()
        local plan = resume.plan({
            char = { name = "Q20614", level = 17 },
            big_map_id = 220010000,
            quests = {
                { id = 20614, status_code = 3, req_count = 1, seq = 4, lv_num = 17 },
                { id = 20615, status_code = 6, req_count = 0, seq = 5, lv_num = 20 },
            },
        })

        T.assert_eq(plan.stage, "20614_after_start_teleport")
        T.assert_eq(plan.flags.completed_20614_task_teleport, true)
        T.assert_eq(plan.flags.completed_20614_start_dialog, true)
        T.assert_eq(plan.flags.completed_20614_after_start_teleport, false)
    end)

    T.test("done quest 20614 resumes after-start task teleport", function()
        local resume = load_module()
        local plan = resume.plan({
            char = { name = "Q20614", level = 17 },
            big_map_id = 220010000,
            quests = {
                { id = 20614, status_code = 4, req_count = 0, seq = 4, lv_num = 17 },
                { id = 20615, status_code = 6, req_count = 0, seq = 5, lv_num = 20 },
            },
        })

        T.assert_eq(plan.stage, "20614_after_start_teleport")
        T.assert_eq(plan.flags.completed_20614_task_teleport, true)
        T.assert_eq(plan.flags.completed_20614_start_dialog, true)
        T.assert_eq(plan.flags.completed_20614_after_start_teleport, false)
    end)

    T.test("done quest 20614 at reward npc resumes reward dialog", function()
        local resume = load_module()
        local plan = resume.plan({
            char = { name = "Q20614", level = 17, x = 600.78, y = 1480.36, z = 299.94 },
            big_map_id = 220010000,
            quests = {
                { id = 20614, status_code = 4, req_count = 0, seq = 4, lv_num = 17 },
                { id = 20615, status_code = 6, req_count = 0, seq = 5, lv_num = 20 },
            },
        })

        T.assert_eq(plan.stage, "20614_reward")
        T.assert_eq(plan.flags.completed_20614_task_teleport, true)
        T.assert_eq(plan.flags.completed_20614_start_dialog, true)
        T.assert_eq(plan.flags.completed_20614_after_start_teleport, true)
        T.assert_eq(plan.flags.completed_20614_reward_dialog, false)
    end)

    T.test("level blocked quest 20615 resumes level 20 grind", function()
        local resume = load_module()
        local plan = resume.plan({
            char = { name = "Q20615", level = 17 },
            big_map_id = 220010000,
            quests = {
                { id = 20615, status_code = 6, req_count = 0, seq = 5, lv_num = 20 },
            },
        })

        T.assert_eq(plan.stage, "20615_level_blocked")
        T.assert_eq(plan.flags.completed_20614_task_teleport, true)
        T.assert_eq(plan.flags.completed_20614_start_dialog, true)
        T.assert_eq(plan.flags.completed_20614_after_start_teleport, true)
        T.assert_eq(plan.flags.completed_20614_reward_dialog, true)
    end)

    T.test("active quest 20615 resumes task teleport", function()
        local resume = load_module()
        local plan = resume.plan({
            char = { name = "Q20615", level = 20 },
            big_map_id = 220010000,
            quests = {
                { id = 20615, status_code = 3, req_count = 0, seq = 0, lv_num = 20 },
            },
        })

        T.assert_eq(plan.stage, "20615_active")
        T.assert_eq(plan.flags.completed_20614_task_teleport, true)
        T.assert_eq(plan.flags.completed_20614_start_dialog, true)
        T.assert_eq(plan.flags.completed_20614_after_start_teleport, true)
        T.assert_eq(plan.flags.completed_20614_reward_dialog, true)
        T.assert_eq(plan.flags.completed_20615_task_teleport, false)
    end)

    T.test("active quest 20615 near target npc resumes target dialog", function()
        local resume = load_module()
        local plan = resume.plan({
            char = { name = "Q20615", level = 20, x = 587.72, y = 2451.15, z = 278.38 },
            big_map_id = 220010000,
            quests = {
                { id = 20615, status_code = 3, req_count = 0, seq = 0, lv_num = 20 },
            },
        })

        T.assert_eq(plan.stage, "20615_target_npc")
        T.assert_eq(plan.flags.completed_20614_task_teleport, true)
        T.assert_eq(plan.flags.completed_20614_start_dialog, true)
        T.assert_eq(plan.flags.completed_20614_after_start_teleport, true)
        T.assert_eq(plan.flags.completed_20614_reward_dialog, true)
        T.assert_eq(plan.flags.completed_20615_task_teleport, true)
        T.assert_eq(plan.flags.completed_20615_target_dialog, false)
    end)

    T.test("progressed quest 20615 resumes big map teleport", function()
        local resume = load_module()
        local plan = resume.plan({
            char = { name = "Q20615", level = 20, x = 587.72, y = 2451.15, z = 278.38 },
            big_map_id = 220010000,
            quests = {
                { id = 20615, status_code = 3, req_count = 1, seq = 0, lv_num = 20 },
            },
        })

        T.assert_eq(plan.stage, "20615_big_map_teleport")
        T.assert_eq(plan.flags.completed_20615_task_teleport, true)
        T.assert_eq(plan.flags.completed_20615_target_dialog, true)
        T.assert_eq(plan.flags.completed_20615_big_map_teleport, false)
    end)

    T.test("done quest 20615 resumes big map teleport", function()
        local resume = load_module()
        local plan = resume.plan({
            char = { name = "Q20615", level = 20, x = 587.72, y = 2451.15, z = 278.38 },
            big_map_id = 220010000,
            quests = {
                { id = 20615, status_code = 4, req_count = 0, seq = 0, lv_num = 20 },
            },
        })

        T.assert_eq(plan.stage, "20615_big_map_teleport")
        T.assert_eq(plan.flags.completed_20615_task_teleport, true)
        T.assert_eq(plan.flags.completed_20615_target_dialog, true)
        T.assert_eq(plan.flags.completed_20615_big_map_teleport, false)
    end)

    T.test("active quest 20615 on another big map resumes after big map task teleport", function()
        local resume = load_module()
        local plan = resume.plan({
            char = { name = "Q20615", level = 20, x = 100, y = 100, z = 100 },
            big_map_id = 220020000,
            quests = {
                { id = 20615, status_code = 3, req_count = 1, seq = 0, lv_num = 20 },
            },
        })

        T.assert_eq(plan.stage, "20615_after_big_map_task_teleport")
        T.assert_eq(plan.flags.completed_20615_task_teleport, true)
        T.assert_eq(plan.flags.completed_20615_target_dialog, true)
        T.assert_eq(plan.flags.completed_20615_big_map_teleport, true)
        T.assert_eq(plan.flags.completed_20615_after_big_map_task_teleport, false)
    end)

    T.test("done quest 20615 on another big map resumes after big map task teleport", function()
        local resume = load_module()
        local plan = resume.plan({
            char = { name = "Q20615", level = 20, x = 100, y = 100, z = 100 },
            big_map_id = 220020000,
            quests = {
                { id = 20615, status_code = 4, req_count = 0, seq = 0, lv_num = 20 },
            },
        })

        T.assert_eq(plan.stage, "20615_after_big_map_task_teleport")
        T.assert_eq(plan.flags.completed_20615_task_teleport, true)
        T.assert_eq(plan.flags.completed_20615_target_dialog, true)
        T.assert_eq(plan.flags.completed_20615_big_map_teleport, true)
        T.assert_eq(plan.flags.completed_20615_after_big_map_task_teleport, false)
    end)

    T.test("done quest 20615 near Morheim npc resumes npc dialog", function()
        local resume = load_module()
        local plan = resume.plan({
            char = { name = "Q20615", level = 20, x = 224.60, y = 2416.30, z = 454.56 },
            big_map_id = 220020000,
            quests = {
                { id = 20615, status_code = 4, req_count = 0, seq = 0, lv_num = 20 },
                { id = 20620, status_code = 3, req_count = 0, seq = 0, lv_num = 20 },
            },
        })

        T.assert_eq(plan.stage, "20615_morheim_npc")
        T.assert_eq(plan.flags.completed_20615_task_teleport, true)
        T.assert_eq(plan.flags.completed_20615_target_dialog, true)
        T.assert_eq(plan.flags.completed_20615_big_map_teleport, true)
        T.assert_eq(plan.flags.completed_20615_after_big_map_task_teleport, true)
        T.assert_eq(plan.flags.completed_20615_morheim_npc_dialog, false)
    end)

    T.test("open quest 20615 Morheim npc dialog resumes npc dialog", function()
        local resume = load_module()
        local plan = resume.plan({
            char = { name = "Q20615", level = 20, x = 224.60, y = 2416.30, z = 454.56 },
            big_map_id = 220020000,
            dialog = {
                npc_dialog_id = 2147488159,
                quest_id = 20615,
                type_text = "select_success",
            },
            quests = {
                { id = 20615, status_code = 4, req_count = 0, seq = 0, lv_num = 20 },
                { id = 20620, status_code = 3, req_count = 0, seq = 0, lv_num = 20 },
            },
        })

        T.assert_eq(plan.stage, "20615_morheim_npc")
        T.assert_eq(plan.flags.completed_20615_after_big_map_task_teleport, true)
        T.assert_eq(plan.flags.completed_20615_morheim_npc_dialog, false)
    end)

    T.test("quest 20611 hotspot reward snapshot is not treated as quest 20612 grind", function()
        local resume = load_module()
        local plan = resume.plan({
            char = { name = "Q20611", level = 11 },
            big_map_id = 220010000,
            quests = {
                { id = 20611, status_code = 4, req_count = 3, seq = 1, lv_num = 8 },
                { id = 20612, status_code = 6, req_count = 0, seq = 2, lv_num = 11 },
            },
        })

        T.assert_eq(plan.stage, "20611_hotspot_reward")
        T.assert_eq(plan.flags.completed_20611_hotspot_teleport, true)
        T.assert_eq(plan.flags.completed_20611_hotspot_reward, false)
        T.assert_nil(plan.flags.completed_20612_start_dialog)
    end)

    clear_modules()
    return T.report("aion_main_quest_resume")
end

return { run = run }
