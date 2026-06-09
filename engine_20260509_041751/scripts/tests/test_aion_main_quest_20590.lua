local T = require("tests.test_framework")

local function clear_modules()
    package.loaded["aion.main_quest_20590"] = nil
end

local function load_module()
    clear_modules()
    return require("aion.main_quest_20590")
end

local function active_quest(step)
    return { id = 20590, status_code = 3, req_count = step or 0 }
end

local function ready_quest()
    return { id = 20590, status_code = 4, req_count = 3 }
end

local function near_char()
    return { x = 1656.5, y = 1401.0, z = 194.67 }
end

local function far_char()
    return { x = 1665.0, y = 1405.0, z = 194.67 }
end

local function run()
    T.reset()
    T.log("\n=== aion main quest 20590 tests ===")

    T.test("navigates to npc when quest is active but player is far", function()
        local quest = load_module()
        local next_action = quest.nextAction({
            quest = active_quest(),
            char = far_char(),
        })

        T.assert_eq(next_action.name, "NavigateToNpc")
        T.assert_eq(next_action.params.quest_id, 20590)
        T.assert_eq(next_action.params.interact_id, 2147533452)
        T.assert_gt(next_action.params.distance, next_action.params.range)
    end)

    T.test("opens npc dialog when near npc and no dialog is open", function()
        local quest = load_module()
        local next_action = quest.nextAction({
            quest = active_quest(),
            char = near_char(),
        })

        T.assert_eq(next_action.name, "InteractNpc")
        T.assert_eq(next_action.params.interact_id, 2147533452)
    end)

    T.test("clicks quest list and first dialog stages", function()
        local quest = load_module()
        local list_action = quest.nextAction({
            quest = active_quest(),
            char = near_char(),
            dialog = { type_text = "select_quest", dialog_content_id = 10, quest_id = 0 },
        })
        local first_action = quest.nextAction({
            quest = active_quest(),
            char = near_char(),
            dialog = { type_text = "select1", dialog_content_id = 1011, quest_id = 20590 },
        })

        T.assert_eq(list_action.name, "ClickDialogX")
        T.assert_eq(list_action.params.expected_content_id, 10)
        T.assert_eq(first_action.name, "ClickDialogX")
        T.assert_eq(first_action.params.expected_content_id, 1011)
    end)

    T.test("clicks the added first npc dialog before final teleport", function()
        local quest = load_module()
        local next_action = quest.nextAction({
            quest = active_quest(),
            char = near_char(),
            dialog = { type_text = "select1_1", dialog_content_id = 1012, quest_id = 20590 },
        })

        T.assert_eq(next_action.name, "ClickDialogX")
        T.assert_eq(next_action.params.expected_content_id, 1012)
    end)

    T.test("marks final first npc dialog as click and wait for teleport", function()
        local quest = load_module()
        local next_action = quest.nextAction({
            quest = active_quest(),
            char = near_char(),
            dialog = { type_text = "select1_1_1", dialog_content_id = 1013, quest_id = 20590 },
        })

        T.assert_eq(next_action.name, "ClickDialogXWaitTeleport")
        T.assert_eq(next_action.params.expected_content_id, 1013)
    end)

    T.test("waits until position changes after final dialog click", function()
        local quest = load_module()
        local waiting = quest.nextAction({
            quest = active_quest(),
            char = { x = 1657, y = 1401, z = 194.67 },
            big_map_id = 120030000,
        }, {
            waiting_teleport = true,
            teleport_start_pos = { x = 1656, y = 1401, z = 194.67 },
            teleport_start_big_map_id = 120030000,
        })
        local completed = quest.nextAction({
            quest = active_quest(),
            char = { x = 1700, y = 1450, z = 200 },
            big_map_id = 120030000,
        }, {
            waiting_teleport = true,
            teleport_start_pos = { x = 1656, y = 1401, z = 194.67 },
            teleport_start_big_map_id = 120030000,
        })

        T.assert_eq(waiting.name, "WaitPositionChanged")
        T.assert_eq(completed.name, "CompleteStep")
        T.assert_eq(completed.reason, "position_changed")
    end)

    T.test("does not return to first npc after leaving start map", function()
        local quest = load_module()
        local next_action = quest.nextAction({
            quest = active_quest(),
            char = far_char(),
            big_map_id = 120040000,
        })

        T.assert_eq(next_action.name, "CompleteStep")
        T.assert_eq(next_action.reason, "already_left_known_start_stage")
        T.assert_eq(next_action.params.stage, "first_npc_teleport")
    end)

    T.test("follows recorded inner route when quest step is 2", function()
        local quest = load_module()
        local next_action = quest.nextAction({
            quest = active_quest(2),
            char = { x = 507.642, y = 594.726, z = 322.562 },
            big_map_id = 390010000,
        })

        T.assert_eq(next_action.name, "NavigateToNpc")
        T.assert_eq(next_action.params.stage, "inner_npc")
        T.assert_eq(next_action.params.interact_id, 2424368065)
        T.assert_eq(next_action.params.route_index, 2)
        T.assert_eq(next_action.params.route_count, 18)
    end)

    T.test("opens inner npc dialog when near inner npc", function()
        local quest = load_module()
        local next_action = quest.nextAction({
            quest = active_quest(2),
            char = { x = 522.3, y = 575.0, z = 322.0 },
            big_map_id = 390010000,
        })

        T.assert_eq(next_action.name, "InteractNpc")
        T.assert_eq(next_action.params.stage, "inner_npc")
        T.assert_eq(next_action.params.interact_id, 2424368065)
    end)

    T.test("marks inner npc dialog as simple teleport", function()
        local quest = load_module()
        local next_action = quest.nextAction({
            quest = active_quest(2),
            char = { x = 522.3, y = 575.0, z = 322.0 },
            big_map_id = 390010000,
            dialog = {
                npc_dialog_id = 2424368065,
                type_text = "select1",
                dialog_content_id = 1011,
                quest_id = 0,
            },
        })

        T.assert_eq(next_action.name, "ClickDialogXWaitTeleport")
        T.assert_eq(next_action.params.stage, "inner_npc_teleport")
        T.assert_eq(next_action.params.expected_content_id, 1011)
    end)

    T.test("moves to temple npc when quest step is 3", function()
        local quest = load_module()
        local next_action = quest.nextAction({
            quest = active_quest(3),
            char = { x = 1468.82, y = 1450.37, z = 176.93 },
            big_map_id = 120010000,
        })

        T.assert_eq(next_action.name, "NavigateToNpc")
        T.assert_eq(next_action.params.stage, "temple_npc")
        T.assert_eq(next_action.params.interact_id, 2147509246)
        T.assert_gt(next_action.params.distance, next_action.params.range)
    end)

    T.test("opens temple npc dialog when near temple npc", function()
        local quest = load_module()
        local next_action = quest.nextAction({
            quest = active_quest(3),
            char = { x = 1469.0, y = 1465.4, z = 177.8 },
            big_map_id = 120010000,
        })

        T.assert_eq(next_action.name, "InteractNpc")
        T.assert_eq(next_action.params.stage, "temple_npc")
        T.assert_eq(next_action.params.interact_id, 2147509246)
    end)

    T.test("clicks the recorded temple npc dialog chain", function()
        local quest = load_module()
        local cases = {
            { type_text = "select_quest", content_id = 10, action = "ClickDialogX" },
            { type_text = "select4", content_id = 2034, action = "ClickDialogX" },
            { type_text = "select4_1", content_id = 2035, action = "ClickDialogX" },
            { type_text = "select4_1_1", content_id = 2036, action = "ClickDialogX" },
            { type_text = "select4_1_1_1", content_id = 2037, action = "ClickDialogX" },
            { type_text = "select4_2", content_id = 2120, action = "ClickDialogXWaitTeleport" },
        }

        for _, case in ipairs(cases) do
            local next_action = quest.nextAction({
                quest = active_quest(3),
                char = { x = 1469.0, y = 1465.4, z = 177.8 },
                big_map_id = 120010000,
                dialog = {
                    npc_dialog_id = 2147509246,
                    type_text = case.type_text,
                    dialog_content_id = case.content_id,
                    quest_id = case.content_id == 10 and 0 or 20590,
                },
            })

            T.assert_eq(next_action.name, case.action, case.type_text)
            T.assert_eq(next_action.params.stage,
                case.action == "ClickDialogXWaitTeleport" and "temple_npc_teleport" or "temple_npc")
            T.assert_eq(next_action.params.expected_content_id, case.content_id)
            T.assert_eq(next_action.params.interact_id, 2147509246)
        end
    end)

    T.test("moves to reward npc when quest is ready", function()
        local quest = load_module()
        local next_action = quest.nextAction({
            quest = ready_quest(),
            char = { x = 570.0, y = 2785.0, z = 299.5 },
            big_map_id = 220010000,
        })

        T.assert_eq(next_action.name, "NavigateToNpc")
        T.assert_eq(next_action.params.stage, "reward_npc")
        T.assert_eq(next_action.params.interact_id, 2147492916)
    end)

    T.test("does not run reward npc when quest is no longer known", function()
        local quest = load_module()
        local next_action = quest.nextAction({
            char = { x = 564.0, y = 2785.0, z = 299.5 },
            big_map_id = 220010000,
        })

        T.assert_eq(next_action.name, "Idle")
    end)

    T.test("opens reward npc dialog when near ask", function()
        local quest = load_module()
        local next_action = quest.nextAction({
            quest = ready_quest(),
            char = { x = 560.9, y = 2785.9, z = 299.0 },
            big_map_id = 220010000,
        })

        T.assert_eq(next_action.name, "InteractNpc")
        T.assert_eq(next_action.params.stage, "reward_npc")
        T.assert_eq(next_action.params.interact_id, 2147492916)
    end)

    T.test("clicks reward selection then confirms ok", function()
        local quest = load_module()
        local select_success = quest.nextAction({
            quest = ready_quest(),
            char = { x = 560.9, y = 2785.9, z = 299.0 },
            big_map_id = 220010000,
            dialog = {
                npc_dialog_id = 2147492916,
                type_text = "select_success",
                dialog_content_id = 10002,
                quest_id = 20590,
            },
        })
        local confirm_reward = quest.nextAction({
            quest = ready_quest(),
            char = { x = 560.9, y = 2785.9, z = 299.0 },
            big_map_id = 220010000,
            dialog = {
                npc_dialog_id = 2147492916,
                type_text = "select_quest_reward1",
                dialog_content_id = 5,
                quest_id = 20590,
            },
        })

        T.assert_eq(select_success.name, "ClickDialogX")
        T.assert_eq(select_success.params.expected_content_id, 10002)
        T.assert_eq(confirm_reward.name, "ClickDialogOkCompleteQuest")
        T.assert_eq(confirm_reward.params.expected_content_id, 5)
        T.assert_eq(confirm_reward.params.interact_id, 2147492916)
    end)

    clear_modules()
    return T.report("aion_main_quest_20590")
end

return { run = run }
