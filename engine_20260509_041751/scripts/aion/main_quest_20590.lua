local M = {}
local npc_names = require("aion.npc_names")

M.quest_id = 20590
M.start_big_map_id = 120030000
M.inner_big_map_id = 390010000
M.temple_big_map_id = 120010000
M.alder_big_map_id = 220010000
M.npc = {
    name_key = "MQ20590_NPC_001_START",
    name = npc_names.MQ20590_NPC_001_START,
    interact_id = 2147533452,
    x = 1655.94,
    y = 1400.75,
    z = 194.67,
}
M.inner_npc = {
    name_key = "MQ20590_NPC_002_INNER",
    name = npc_names.MQ20590_NPC_002_INNER,
    interact_id = 2424368065,
    x = 522.68,
    y = 573.38,
    z = 322.03,
}
M.temple_npc = {
    name_key = "MQ20590_NPC_003_TEMPLE",
    name = npc_names.MQ20590_NPC_003_TEMPLE,
    interact_id = 2147509246,
    x = 1469.00,
    y = 1466.00,
    z = 177.82,
}
M.reward_npc = {
    name_key = "MQ20590_NPC_004_REWARD",
    name = npc_names.MQ20590_NPC_004_REWARD,
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
        action = "ClickDialogXContinuousWaitTeleport",
        reason = "complete first npc dialog chain by continuous x-click",
    },
    select1 = {
        content_id = 1011,
        action = "ClickDialogXContinuousWaitTeleport",
        reason = "complete first npc dialog chain by continuous x-click",
    },
    select1_1 = {
        content_id = 1012,
        action = "ClickDialogXContinuousWaitTeleport",
        reason = "complete first npc dialog chain by continuous x-click",
    },
    select1_1_1 = {
        content_id = 1013,
        action = "ClickDialogXContinuousWaitTeleport",
        reason = "complete first npc dialog chain and wait for teleport",
    },
    select10 = {
        content_id = 4080,
        action = "ClickDialogXContinuousWaitTeleport",
        reason = "complete recovered first npc dialog chain and wait for teleport",
    },
}

M.inner_dialog_steps = {
    select1 = {
        content_id = 1011,
        action = "ClickDialogXContinuousWaitTeleport",
        reason = "complete inner npc dialog chain and wait for teleport",
    },
}

M.temple_dialog_steps = {
    select_quest = {
        content_id = 10,
        action = "ClickDialogXContinuousWaitTeleport",
        reason = "complete temple npc dialog chain by continuous x-click",
    },
    select4 = {
        content_id = 2034,
        action = "ClickDialogXContinuousWaitTeleport",
        reason = "complete temple npc dialog chain by continuous x-click",
    },
    select4_1 = {
        content_id = 2035,
        action = "ClickDialogXContinuousWaitTeleport",
        reason = "complete temple npc dialog chain by continuous x-click",
    },
    select4_1_1 = {
        content_id = 2036,
        action = "ClickDialogXContinuousWaitTeleport",
        reason = "complete temple npc dialog chain by continuous x-click",
    },
    select4_1_1_1 = {
        content_id = 2037,
        action = "ClickDialogXContinuousWaitTeleport",
        reason = "complete temple npc dialog chain by continuous x-click",
    },
    select4_2 = {
        content_id = 2120,
        action = "ClickDialogXContinuousWaitTeleport",
        reason = "complete temple npc dialog chain and wait for teleport",
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

local function position_changed(a, b)
    if type(a) ~= "table" or type(b) ~= "table" then
        return false
    end
    local ax, ay, az = tonumber(a.x), tonumber(a.y), tonumber(a.z)
    local bx, by, bz = tonumber(b.x), tonumber(b.y), tonumber(b.z)
    if not ax or not ay or not az or not bx or not by or not bz then
        return false
    end
    return ax ~= bx or ay ~= by or az ~= bz
end

local function action(name, reason, params)
    return {
        name = name,
        reason = reason or "",
        params = params or {},
    }
end

local function waitRouteIfActive(opts, stage, quest)
    local active_stage = tostring(opts and opts.route_following_stage or "")
    if active_stage ~= "" and active_stage == tostring(stage or "") then
        return action("WaitRouteComplete", "wait main quest route complete", {
            quest_id = M.quest_id,
            quest_step = M.questStep(quest),
            stage = stage,
        })
    end
    return nil
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

function M.isRewardDialog(dialog)
    if type(dialog) ~= "table" then
        return false
    end
    local type_text = tostring(dialog.type_text or "")
    if M.reward_dialog_steps[type_text] == nil then
        return false
    end
    local dialog_quest_id = number(dialog.quest_id)
    return dialog_quest_id == M.quest_id
end

function M.teleportDetected(state, runtime, opts)
    opts = opts or {}
    runtime = runtime or {}

    local start_pos = runtime.teleport_start_pos
    local char = state and state.char
    if position_changed(start_pos, char) then
        return true, "position_changed"
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
        })
    end

    local quest = state.quest or M.findQuest(state.quests)

    local char = state.char
    if type(char) ~= "table" then
        return action("ReadState", "character unavailable", { quest_id = M.quest_id })
    end

    local current_big_map = number(state.big_map_id)
    local quest_step = M.questStep(quest)
    if M.isRewardDialog(state.dialog)
        or M.isQuestReady(quest)
        or (current_big_map == M.alder_big_map_id and M.isQuestKnown(quest)) then
        return M.nextRewardAction(state, quest, opts)
    end

    if not M.isQuestActive(quest) then
        return action("Idle", "quest 20590 is not active", { quest_id = M.quest_id })
    end

    if current_big_map == M.temple_big_map_id then
        return M.nextTempleAction(state, quest, opts)
    end
    if current_big_map == M.inner_big_map_id then
        return M.nextInnerAction(state, quest, opts)
    end

    if current_big_map > 0 and current_big_map ~= M.start_big_map_id then
        return action("CompleteStep", "already_left_known_start_stage", {
            quest_id = M.quest_id,
            stage = "first_npc_teleport",
        })
    end

    -- Map-specific branches are safer than quest req_count. Use req_count only
    -- when the current map cannot be read.
    if current_big_map <= 0 then
        if quest_step >= 3 then
            return M.nextTempleAction(state, quest, opts)
        end
        if quest_step >= 2 then
            return M.nextInnerAction(state, quest, opts)
        end
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
            npc_name = M.npc.name,
            stage = "first_npc",
            x = M.npc.x,
            y = M.npc.y,
            z = M.npc.z,
            distance = dist,
            range = range,
        })
    end
    local route_wait = waitRouteIfActive(opts, "first_npc", quest)
    if route_wait then
        return route_wait
    end

    local dialog = state.dialog
    if type(dialog) ~= "table" then
        return action("InteractNpc", "open first mission npc dialog", {
            quest_id = M.quest_id,
            interact_id = M.npc.interact_id,
            npc_name = M.npc.name,
            npc_name_key = M.npc.name_key,
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
            npc_name = M.npc.name,
            stage = (step.action == "ClickDialogXWaitTeleport"
                or step.action == "ClickDialogXContinuousWaitTeleport")
                and "first_npc_teleport" or "first_npc",
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
        return action("FollowRoute", "follow inner mission route", {
            quest_id = M.quest_id,
            quest_step = M.questStep(quest),
            stage = "inner_npc",
            interact_id = M.inner_npc.interact_id,
            npc_name = M.inner_npc.name,
            x = target.x,
            y = target.y,
            z = target.z,
            route_name = "main_quest_20590_inner",
            route_points = M.inner_route,
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
    local route_wait = waitRouteIfActive(opts, "inner_npc", quest)
    if route_wait then
        return route_wait
    end

    local dialog = state.dialog
    if type(dialog) ~= "table" and opts.inner_final_move_done ~= true then
        return action("FinalMoveToNpc", "final move to inner mission npc", {
            quest_id = M.quest_id,
            quest_step = M.questStep(quest),
            stage = "inner_npc",
            interact_id = M.inner_npc.interact_id,
            npc_name = M.inner_npc.name,
            x = M.inner_npc.x,
            y = M.inner_npc.y,
            z = M.inner_npc.z,
            distance = dist,
            range = range,
        })
    end

    if type(dialog) ~= "table" then
        return action("InteractNpc", "open inner mission npc dialog", {
            quest_id = M.quest_id,
            quest_step = M.questStep(quest),
            stage = "inner_npc",
            interact_id = M.inner_npc.interact_id,
            npc_name = M.inner_npc.name,
            npc_name_key = M.inner_npc.name_key,
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
            npc_name = M.inner_npc.name,
            stage = "inner_npc_teleport",
        })
    end

    return action("DumpDialog", "unknown inner mission dialog stage", {
        quest_id = M.quest_id,
        quest_step = M.questStep(quest),
        type_text = type_text,
        content_id = number(dialog.dialog_content_id),
        npc_dialog_id = number(dialog.npc_dialog_id),
        interact_id = M.inner_npc.interact_id,
        npc_name = M.inner_npc.name,
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
            npc_name = M.temple_npc.name,
            x = M.temple_npc.x,
            y = M.temple_npc.y,
            z = M.temple_npc.z,
            distance = dist,
            range = range,
        })
    end
    local route_wait = waitRouteIfActive(opts, "temple_npc", quest)
    if route_wait then
        return route_wait
    end

    local dialog = state.dialog
    if type(dialog) ~= "table" then
        return action("InteractNpc", "open temple mission npc dialog", {
            quest_id = M.quest_id,
            quest_step = M.questStep(quest),
            stage = "temple_npc",
            interact_id = M.temple_npc.interact_id,
            npc_name = M.temple_npc.name,
            npc_name_key = M.temple_npc.name_key,
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
            npc_name = M.temple_npc.name,
            stage = (step.action == "ClickDialogXWaitTeleport"
                or step.action == "ClickDialogXContinuousWaitTeleport")
                and "temple_npc_teleport" or "temple_npc",
        })
    end

    return action("DumpDialog", "unknown temple mission dialog stage", {
        quest_id = M.quest_id,
        quest_step = M.questStep(quest),
        stage = "temple_npc",
        type_text = type_text,
        content_id = number(dialog.dialog_content_id),
        npc_dialog_id = number(dialog.npc_dialog_id),
        interact_id = M.temple_npc.interact_id,
        npc_name = M.temple_npc.name,
    })
end

function M.nextRewardAction(state, quest, opts)
    opts = opts or {}
    local char = state.char
    local range = number(opts.npc_range)
    if range <= 0 then
        range = 4
    end

    local dialog = state.dialog
    if type(dialog) == "table" then
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
                npc_name = M.reward_npc.name,
                stage = "reward_npc",
            })
        end

        return action("DumpDialog", "unknown reward dialog stage", {
            quest_id = M.quest_id,
            quest_step = M.questStep(quest),
            stage = "reward_npc",
            type_text = type_text,
            content_id = number(dialog.dialog_content_id),
            npc_dialog_id = number(dialog.npc_dialog_id),
            interact_id = M.reward_npc.interact_id,
            npc_name = M.reward_npc.name,
        })
    end

    local dist = M.distanceToRewardNpc(char)
    if dist > range then
        return action("NavigateToNpc", "move to reward npc", {
            quest_id = M.quest_id,
            quest_step = M.questStep(quest),
            stage = "reward_npc",
            interact_id = M.reward_npc.interact_id,
            npc_name = M.reward_npc.name,
            x = M.reward_npc.x,
            y = M.reward_npc.y,
            z = M.reward_npc.z,
            distance = dist,
            range = range,
        })
    end
    local route_wait = waitRouteIfActive(opts, "reward_npc", quest)
    if route_wait then
        return route_wait
    end

    return action("InteractNpc", "open reward npc dialog", {
        quest_id = M.quest_id,
        quest_step = M.questStep(quest),
        stage = "reward_npc",
        interact_id = M.reward_npc.interact_id,
        npc_name = M.reward_npc.name,
        npc_name_key = M.reward_npc.name_key,
    })
end

return M
