local T = require("tests.test_framework")

local function clear_modules()
    package.loaded["aion.main_quest_20610"] = nil
end

local function load_module()
    clear_modules()
    return require("aion.main_quest_20610")
end

local function active_quest()
    return { id = 20610, status_code = 3, req_count = 0 }
end

local function done_quest()
    return { id = 20610, status_code = 4, req_count = 0 }
end

local function near_char()
    return { x = 564.00, y = 2785.00, z = 299.50 }
end

local function far_char()
    return { x = 570.00, y = 2785.00, z = 299.50 }
end

local function reward_char()
    return { x = 223.17, y = 2680.63, z = 295.25 }
end

local function run()
    T.reset()
    T.log("\n=== aion main quest 20610 tests ===")

    T.test("moves to quest npc when active and far", function()
        local quest = load_module()
        local next_action = quest.nextAction({
            quest = active_quest(),
            char = far_char(),
            big_map_id = 220010000,
        })

        T.assert_eq(next_action.name, "NavigateToNpc")
        T.assert_eq(next_action.params.quest_id, 20610)
        T.assert_eq(next_action.params.interact_id, 2147514375)
        T.assert_eq(next_action.params.npc_name, "아스크")
        T.assert_eq(next_action.params.stage, "quest_20610_npc")
    end)

    T.test("opens npc dialog when active and near", function()
        local quest = load_module()
        local next_action = quest.nextAction({
            quest = active_quest(),
            char = near_char(),
            big_map_id = 220010000,
        })

        T.assert_eq(next_action.name, "InteractNpc")
        T.assert_eq(next_action.params.quest_id, 20610)
        T.assert_eq(next_action.params.interact_id, 2147514375)
        T.assert_eq(next_action.params.npc_name, "아스크")
    end)

    T.test("clicks recorded quest 20610 dialog chain", function()
        local quest = load_module()
        local cases = {
            { type_text = "select_quest", content_id = 10, action = "ClickDialogXContinuous" },
            { type_text = "select1", content_id = 1011, action = "ClickDialogXContinuous" },
            { type_text = "select1_1", content_id = 1012, action = "ClickDialogXContinuous" },
            { type_text = "select1_1_1", content_id = 1013, action = "ClickDialogXContinuous" },
            { type_text = "select1_1_1_1", content_id = 1014, action = "ClickDialogXContinuous" },
        }

        for _, case in ipairs(cases) do
            local next_action = quest.nextAction({
                quest = active_quest(),
                char = near_char(),
                big_map_id = 220010000,
                dialog = {
                    npc_dialog_id = 2147514375,
                    type_text = case.type_text,
                    dialog_content_id = case.content_id,
                    quest_id = case.content_id == 10 and 0 or 20610,
                },
            })

            T.assert_eq(next_action.name, case.action, case.type_text)
            T.assert_eq(next_action.params.expected_content_id, case.content_id)
            T.assert_eq(next_action.params.interact_id, 2147514375)
            T.assert_eq(next_action.params.stage, "quest_20610_npc")
        end
    end)

    T.test("uses quest teleport when opening dialog is done", function()
        local quest = load_module()
        local next_action = quest.nextAction({
            quest = done_quest(),
            char = near_char(),
            big_map_id = 220010000,
        })

        T.assert_eq(next_action.name, "QuestTeleport")
        T.assert_eq(next_action.params.quest_id, 20610)
        T.assert_eq(next_action.params.stage, "quest_20610_task_teleport")
        T.assert_eq(next_action.params.wait_teleport, true)
    end)

    T.test("prioritizes opened quest 20610 npc dialog before task teleport", function()
        local quest = load_module()
        local next_action = quest.nextAction({
            quest = done_quest(),
            char = near_char(),
            big_map_id = 220010000,
            dialog = {
                npc_dialog_id = 2147514375,
                type_text = "select1",
                dialog_content_id = 1011,
                quest_id = 20610,
            },
        })

        T.assert_eq(next_action.name, "ClickDialogXContinuous")
        T.assert_eq(next_action.params.stage, "quest_20610_npc")
        T.assert_eq(next_action.params.expected_content_id, 1011)
        T.assert_eq(next_action.params.interact_id, 2147514375)
    end)

    T.test("quest teleport ignores resolution-dependent ui coordinates", function()
        local quest = load_module()
        local next_action = quest.nextAction({
            quest = done_quest(),
            char = near_char(),
            big_map_id = 220010000,
            ui = { quest_detail_target_link = true },
        }, {
            clicked_20610_indicator_teleport = true,
        })

        T.assert_eq(next_action.name, "QuestTeleport")
        T.assert_eq(next_action.params.stage, "quest_20610_task_teleport")
        T.assert_eq(next_action.params.x, nil)
        T.assert_eq(next_action.params.y, nil)
    end)

    T.test("quest teleport waits for position change", function()
        local quest = load_module()
        local click = quest.nextAction({
            quest = done_quest(),
            char = near_char(),
            big_map_id = 220010000,
            ui = { dictionary_teleport_to_npc = true },
        }, {
            clicked_20610_indicator_teleport = true,
            clicked_20610_target_link = true,
        })
        local waiting = quest.nextAction({
            quest = done_quest(),
            char = near_char(),
            big_map_id = 220010000,
        }, {
            waiting_teleport = true,
            teleport_stage = "quest_20610_task_teleport",
            teleport_start_pos = near_char(),
            teleport_start_big_map_id = 220010000,
        })
        local completed = quest.nextAction({
            quest = done_quest(),
            char = { x = 564.5, y = 2785.0, z = 299.5 },
            big_map_id = 220010000,
        }, {
            waiting_teleport = true,
            teleport_stage = "quest_20610_task_teleport",
            teleport_start_pos = near_char(),
            teleport_start_big_map_id = 220010000,
        })

        T.assert_eq(click.name, "QuestTeleport")
        T.assert_eq(click.params.stage, "quest_20610_task_teleport")
        T.assert_eq(waiting.name, "WaitPositionChanged")
        T.assert_eq(completed.name, "CompleteQuestTeleport")
        T.assert_eq(completed.reason, "position_changed")
    end)

    T.test("quest teleport does not depend on stale dictionary ui", function()
        local quest = load_module()
        local dictionary_visible_without_steps = quest.nextAction({
            quest = done_quest(),
            char = near_char(),
            big_map_id = 220010000,
            ui = { dictionary_teleport_to_npc = true },
        })

        T.assert_eq(dictionary_visible_without_steps.name, "QuestTeleport")
        T.assert_eq(dictionary_visible_without_steps.params.stage, "quest_20610_task_teleport")
    end)

    T.test("opens reward npc after task teleport completes", function()
        local quest = load_module()
        local next_action = quest.nextAction({
            quest = done_quest(),
            char = reward_char(),
            big_map_id = 220010000,
        }, {
            completed_20610_task_teleport = true,
        })

        T.assert_eq(next_action.name, "InteractNpc")
        T.assert_eq(next_action.params.stage, "quest_20610_reward_npc")
        T.assert_eq(next_action.params.interact_id, 2147524326)
        T.assert_eq(next_action.params.npc_name, "구헤이툰")
    end)

    T.test("clicks reward dialog and then ok", function()
        local quest = load_module()
        local select_success = quest.nextAction({
            quest = done_quest(),
            char = reward_char(),
            big_map_id = 220010000,
            dialog = {
                npc_dialog_id = 2147524326,
                type_text = "select_success",
                dialog_content_id = 10002,
                quest_id = 20610,
            },
        }, {
            completed_20610_task_teleport = true,
        })
        local ok_reward = quest.nextAction({
            quest = done_quest(),
            char = reward_char(),
            big_map_id = 220010000,
            dialog = {
                npc_dialog_id = 2147524326,
                type_text = "select_quest_reward1",
                dialog_content_id = 5,
                quest_id = 20610,
            },
        }, {
            completed_20610_task_teleport = true,
        })

        T.assert_eq(select_success.name, "ClickDialogX")
        T.assert_eq(select_success.params.expected_content_id, 10002)
        T.assert_eq(select_success.params.interact_id, 2147524326)
        T.assert_eq(ok_reward.name, "ClickDialogOkCompleteQuest")
        T.assert_eq(ok_reward.params.expected_content_id, 5)
        T.assert_eq(ok_reward.params.interact_id, 2147524326)
    end)

    T.test("stops after reward ok completes", function()
        local quest = load_module()
        local next_action = quest.nextAction({
            quest = done_quest(),
            char = reward_char(),
            big_map_id = 220010000,
        }, {
            completed_20610_task_teleport = true,
            completed_20610_reward = true,
        })

        T.assert_eq(next_action.name, "Idle")
    end)

    T.test("does not run when wrong map or inactive", function()
        local quest = load_module()
        local wrong_map = quest.nextAction({
            quest = active_quest(),
            char = near_char(),
            big_map_id = 120030000,
        })
        local inactive = quest.nextAction({
            quest = { id = 20610, status_code = 6 },
            char = near_char(),
            big_map_id = 220010000,
        })

        T.assert_eq(wrong_map.name, "Idle")
        T.assert_eq(inactive.name, "Idle")
    end)

    T.test("does not re-interact when unknown dialog is already open", function()
        local quest = load_module()
        local next_action = quest.nextAction({
            quest = active_quest(),
            char = near_char(),
            big_map_id = 220010000,
            dialog = {
                npc_dialog_id = 2147514375,
                type_text = "unexpected_20610",
                dialog_content_id = 9999,
                quest_id = 20610,
            },
        })

        T.assert_eq(next_action.name, "DumpDialog")
        T.assert_eq(next_action.params.npc_dialog_id, 2147514375)
        T.assert_eq(next_action.params.interact_id, 2147514375)
    end)

    clear_modules()
    return T.report("aion_main_quest_20610")
end

return { run = run }
