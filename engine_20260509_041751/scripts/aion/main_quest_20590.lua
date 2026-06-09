local M = {}

M.quest_id = 20590
M.start_big_map_id = 120030000
M.inner_big_map_id = 390010000
M.temple_big_map_id = 120010000
M.alder_big_map_id = 220010000
M.npc = {
    interact_id = 2147533452,
    x = 1655.94,
    y = 1400.75,
    z = 194.67,
}
M.inner_npc = {
    interact_id = 2424368065,
    x = 522.68,
    y = 573.38,
    z = 322.03,
}
M.temple_npc = {
    interact_id = 2147509246,
    x = 1469.00,
    y = 1466.00,
    z = 177.82,
}
M.reward_npc = {
    interact_id = 2147492916,
    x = 560.99,
    y = 2786.03,
    z = 299.06,
}

M.inner_route = {
    { x = 507.642, y = 594.726, z = 322.562 },
    { x = 507.765, y = 592.186, z = 322.562 },
    { x = 508.212, y = 589.944, z = 322.562 },
    { x = 508.513, y = 588.218, z = 322.562 },
    { x = 509.040, y = 585.777, z = 322.155 },
    { x = 510.030, y = 583.427, z = 322.000 },
    { x = 510.996, y = 581.959, z = 322.000 },
    { x = 512.149, y = 581.300, z = 322.000 },
    { x = 513.041, y = 580.791, z = 322.000 },
    { x = 514.999, y = 579.857, z = 321.933 },
    { x = 516.655, y = 579.169, z = 321.717 },
    { x = 517.367, y = 578.922, z = 321.597 },
    { x = 519.268, y = 578.539, z = 321.558 },
    { x = 520.175, y = 578.391, z = 321.616 },
    { x = 521.243, y = 578.218, z = 321.705 },
    { x = 521.746, y = 576.902, z = 321.743 },
    { x = 522.048, y = 575.966, z = 322.029 },
    { x = 522.377, y = 575.077, z = 322.029 },
}

M.dialog_steps = {
    select_quest = {
        content_id = 10,
        action = "ClickDialogX",
        reason = "open quest detail",
    },
    select1 = {
        content_id = 1011,
        action = "ClickDialogX",
        reason = "continue first dialog",
    },
    select1_1 = {
        content_id = 1012,
        action = "ClickDialogX",
        reason = "continue first dialog before teleport",
    },
    select1_1_1 = {
        content_id = 1013,
        action = "ClickDialogXWaitTeleport",
        reason = "final dialog triggers teleport",
    },
}

M.inner_dialog_steps = {
    select1 = {
        content_id = 1011,
        action = "ClickDialogXWaitTeleport",
        reason = "inner npc simple teleport",
    },
}

M.temple_dialog_steps = {
    select_quest = {
        content_id = 10,
        action = "ClickDialogX",
        reason = "open temple quest detail",
    },
    select4 = {
        content_id = 2034,
        action = "ClickDialogX",
        reason = "continue temple dialog 1",
    },
    select4_1 = {
        content_id = 2035,
        action = "ClickDialogX",
        reason = "continue temple dialog 2",
    },
    select4_1_1 = {
        content_id = 2036,
        action = "ClickDialogX",
        reason = "continue temple dialog 3",
    },
    select4_1_1_1 = {
        content_id = 2037,
        action = "ClickDialogX",
        reason = "continue temple dialog 4",
    },
    select4_2 = {
        content_id = 2120,
        action = "ClickDialogXWaitTeleport",
        reason = "temple dialog completes and teleports",
    },
}

M.reward_dialog_steps = {
    select_success = {
        content_id = 10002,
        action = "ClickDialogX",
        reason = "open quest reward selection",
    },
    select_quest_reward1 = {
        content_id = 5,
        action = "ClickDialogOkCompleteQuest",
        reason = "confirm first mission reward",
    },
}

local function number(value)
    return tonumber(value) or 0
end

local function distance3(a, b)
    if type(a) ~= "table" or type(b) ~= "table" then
        return math.huge
    end
    local dx = number(a.x) - number(b.x)
    local dy = number(a.y) - number(b.y)
    local dz = number(a.z) - number(b.z)
    return math.sqrt(dx * dx + dy * dy + dz * dz)
end

local function action(name, reason, params)
    return {
        name = name,
        reason = reason or "",
        params = params or {},
    }
end

function M.distanceToNpc(char)
    return distance3(char, M.npc)
end

function M.distanceToInnerNpc(char)
    return distance3(char, M.inner_npc)
end

function M.distanceToTempleNpc(char)
    return distance3(char, M.temple_npc)
end

function M.distanceToRewardNpc(char)
    return distance3(char, M.reward_npc)
end

function M.questStep(quest)
    return number(quest and quest.req_count)
end

function M.findQuest(quests)
    for _, quest in ipairs(quests or {}) do
        if number(quest.id) == M.quest_id then
            return quest
        end
    end
    return nil
end

function M.isQuestActive(quest)
    return M.isQuestKnown(quest)
        and number(quest.status_code) == 3
end

function M.isQuestReady(quest)
    return M.isQuestKnown(quest)
        and number(quest.status_code) == 4
end

function M.isQuestKnown(quest)
    return type(quest) == "table"
        and number(quest.id) == M.quest_id
end

function M.teleportDetected(state, runtime, opts)
    opts = opts or {}
    runtime = runtime or {}
    local min_distance = number(opts.teleport_min_distance)
    if min_distance <= 0 then
        min_distance = 20
    end

    local current_big_map = number(state and state.big_map_id)
    local start_big_map = number(runtime.teleport_start_big_map_id)
    if start_big_map > 0 and current_big_map > 0 and start_big_map ~= current_big_map then
        return true, "big_map_changed"
    end

    local start_pos = runtime.teleport_start_pos
    local char = state and state.char
    if type(start_pos) == "table" and type(char) == "table" then
        local dist = distance3(start_pos, char)
        if dist >= min_distance then
            return true, "position_changed"
        end
    end

    return false, "waiting_position_change"
end

function M.nextAction(state, runtime, opts)
    state = state or {}
    runtime = runtime or {}
    opts = opts or {}

    if runtime.waiting_teleport == true then
        local detected, reason = M.teleportDetected(state, runtime, opts)
        if detected then
            return action("CompleteStep", reason, {
                quest_id = M.quest_id,
                stage = tostring(runtime.teleport_stage or "teleport"),
            })
        end
        return action("WaitPositionChanged", reason, {
            quest_id = M.quest_id,
            stage = tostring(runtime.teleport_stage or "teleport"),
            min_distance = opts.teleport_min_distance or 20,
        })
    end

    local quest = state.quest or M.findQuest(state.quests)

    local char = state.char
    if type(char) ~= "table" then
        return action("ReadState", "character unavailable", { quest_id = M.quest_id })
    end

    local current_big_map = number(state.big_map_id)
    local quest_step = M.questStep(quest)
    if M.isQuestReady(quest) or (current_big_map == M.alder_big_map_id and M.isQuestKnown(quest)) then
        return M.nextRewardAction(state, quest, opts)
    end

    if not M.isQuestActive(quest) then
        return action("Idle", "quest 20590 is not active", { quest_id = M.quest_id })
    end

    if current_big_map == M.temple_big_map_id or quest_step >= 3 then
        return M.nextTempleAction(state, quest, opts)
    end
    if current_big_map == M.inner_big_map_id or quest_step >= 2 then
        return M.nextInnerAction(state, quest, opts)
    end

    if current_big_map > 0 and current_big_map ~= M.start_big_map_id then
        return action("CompleteStep", "already_left_known_start_stage", {
            quest_id = M.quest_id,
            stage = "first_npc_teleport",
        })
    end

    local range = number(opts.npc_range)
    if range <= 0 then
        range = 4
    end
    local dist = M.distanceToNpc(char)
    if dist > range then
        return action("NavigateToNpc", "move to first mission npc", {
            quest_id = M.quest_id,
            interact_id = M.npc.interact_id,
            stage = "first_npc",
            x = M.npc.x,
            y = M.npc.y,
            z = M.npc.z,
            distance = dist,
            range = range,
        })
    end

    local dialog = state.dialog
    if type(dialog) ~= "table" then
        return action("InteractNpc", "open first mission npc dialog", {
            quest_id = M.quest_id,
            interact_id = M.npc.interact_id,
            stage = "first_npc",
        })
    end

    local type_text = tostring(dialog.type_text or "")
    local step = M.dialog_steps[type_text]
    if step then
        return action(step.action, step.reason, {
            quest_id = M.quest_id,
            expected_content_id = step.content_id,
            content_id = number(dialog.dialog_content_id),
            type_text = type_text,
            click_x = opts.dialog_click_x or 25,
            interact_id = M.npc.interact_id,
            stage = step.action == "ClickDialogXWaitTeleport" and "first_npc_teleport" or "first_npc",
        })
    end

    return action("DumpDialog", "unknown first mission dialog stage", {
        quest_id = M.quest_id,
        type_text = type_text,
        content_id = number(dialog.dialog_content_id),
    })
end

function M.routeTarget(route_points, char, waypoint_range)
    route_points = route_points or {}
    waypoint_range = number(waypoint_range)
    if waypoint_range <= 0 then
        waypoint_range = 2.0
    end
    if #route_points <= 0 or type(char) ~= "table" then
        return nil, 0, math.huge
    end

    local nearest_index = 1
    local nearest_dist = math.huge
    for index, point in ipairs(route_points) do
        local dist = distance3(char, point)
        if dist < nearest_dist then
            nearest_index = index
            nearest_dist = dist
        end
    end

    local target_index = nearest_index
    if nearest_dist <= waypoint_range and nearest_index < #route_points then
        target_index = nearest_index + 1
    end
    return route_points[target_index], target_index, nearest_dist
end

function M.nextInnerAction(state, quest, opts)
    opts = opts or {}
    local char = state.char
    local range = number(opts.npc_range)
    if range <= 0 then
        range = 4
    end

    local dist = M.distanceToInnerNpc(char)
    if dist > range then
        local target, index, nearest_dist = M.routeTarget(M.inner_route, char, opts.waypoint_range or 2.0)
        target = target or M.inner_npc
        return action("NavigateToNpc", "move to inner mission npc", {
            quest_id = M.quest_id,
            quest_step = M.questStep(quest),
            stage = "inner_npc",
            interact_id = M.inner_npc.interact_id,
            x = target.x,
            y = target.y,
            z = target.z,
            final_x = M.inner_npc.x,
            final_y = M.inner_npc.y,
            final_z = M.inner_npc.z,
            route_index = index,
            route_count = #M.inner_route,
            nearest_route_distance = nearest_dist,
            distance = dist,
            range = range,
        })
    end

    local dialog = state.dialog
    if type(dialog) ~= "table" or number(dialog.npc_dialog_id) ~= M.inner_npc.interact_id then
        return action("InteractNpc", "open inner mission npc dialog", {
            quest_id = M.quest_id,
            quest_step = M.questStep(quest),
            stage = "inner_npc",
            interact_id = M.inner_npc.interact_id,
        })
    end

    local type_text = tostring(dialog.type_text or "")
    local step = M.inner_dialog_steps[type_text]
    if step then
        return action(step.action, step.reason, {
            quest_id = M.quest_id,
            quest_step = M.questStep(quest),
            expected_content_id = step.content_id,
            content_id = number(dialog.dialog_content_id),
            type_text = type_text,
            click_x = opts.dialog_click_x or 25,
            interact_id = M.inner_npc.interact_id,
            stage = "inner_npc_teleport",
        })
    end

    return action("DumpDialog", "unknown inner mission dialog stage", {
        quest_id = M.quest_id,
        quest_step = M.questStep(quest),
        type_text = type_text,
        content_id = number(dialog.dialog_content_id),
        interact_id = M.inner_npc.interact_id,
    })
end

function M.nextTempleAction(state, quest, opts)
    opts = opts or {}
    local char = state.char
    local range = number(opts.npc_range)
    if range <= 0 then
        range = 4
    end

    local dist = M.distanceToTempleNpc(char)
    if dist > range then
        return action("NavigateToNpc", "move to temple mission npc", {
            quest_id = M.quest_id,
            quest_step = M.questStep(quest),
            stage = "temple_npc",
            interact_id = M.temple_npc.interact_id,
            x = M.temple_npc.x,
            y = M.temple_npc.y,
            z = M.temple_npc.z,
            distance = dist,
            range = range,
        })
    end

    local dialog = state.dialog
    if type(dialog) ~= "table" or number(dialog.npc_dialog_id) ~= M.temple_npc.interact_id then
        return action("InteractNpc", "open temple mission npc dialog", {
            quest_id = M.quest_id,
            quest_step = M.questStep(quest),
            stage = "temple_npc",
            interact_id = M.temple_npc.interact_id,
        })
    end

    local type_text = tostring(dialog.type_text or "")
    local step = M.temple_dialog_steps[type_text]
    if step then
        return action(step.action, step.reason, {
            quest_id = M.quest_id,
            quest_step = M.questStep(quest),
            expected_content_id = step.content_id,
            content_id = number(dialog.dialog_content_id),
            type_text = type_text,
            click_x = opts.dialog_click_x or 25,
            interact_id = M.temple_npc.interact_id,
            stage = step.action == "ClickDialogXWaitTeleport" and "temple_npc_teleport" or "temple_npc",
        })
    end

    return action("DumpDialog", "unknown temple mission dialog stage", {
        quest_id = M.quest_id,
        quest_step = M.questStep(quest),
        stage = "temple_npc",
        type_text = type_text,
        content_id = number(dialog.dialog_content_id),
        interact_id = M.temple_npc.interact_id,
    })
end

function M.nextRewardAction(state, quest, opts)
    opts = opts or {}
    local char = state.char
    local range = number(opts.npc_range)
    if range <= 0 then
        range = 4
    end

    local dist = M.distanceToRewardNpc(char)
    if dist > range then
        return action("NavigateToNpc", "move to reward npc", {
            quest_id = M.quest_id,
            quest_step = M.questStep(quest),
            stage = "reward_npc",
            interact_id = M.reward_npc.interact_id,
            x = M.reward_npc.x,
            y = M.reward_npc.y,
            z = M.reward_npc.z,
            distance = dist,
            range = range,
        })
    end

    local dialog = state.dialog
    if type(dialog) ~= "table" or number(dialog.npc_dialog_id) ~= M.reward_npc.interact_id then
        return action("InteractNpc", "open reward npc dialog", {
            quest_id = M.quest_id,
            quest_step = M.questStep(quest),
            stage = "reward_npc",
            interact_id = M.reward_npc.interact_id,
        })
    end

    local type_text = tostring(dialog.type_text or "")
    local step = M.reward_dialog_steps[type_text]
    if step then
        return action(step.action, step.reason, {
            quest_id = M.quest_id,
            quest_step = M.questStep(quest),
            expected_content_id = step.content_id,
            content_id = number(dialog.dialog_content_id),
            type_text = type_text,
            click_x = opts.dialog_click_x or 25,
            interact_id = M.reward_npc.interact_id,
            stage = "reward_npc",
        })
    end

    return action("DumpDialog", "unknown reward dialog stage", {
        quest_id = M.quest_id,
        quest_step = M.questStep(quest),
        stage = "reward_npc",
        type_text = type_text,
        content_id = number(dialog.dialog_content_id),
        interact_id = M.reward_npc.interact_id,
    })
end

return M
