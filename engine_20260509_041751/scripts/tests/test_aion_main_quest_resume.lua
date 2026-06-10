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

    clear_modules()
    return T.report("aion_main_quest_resume")
end

return { run = run }
