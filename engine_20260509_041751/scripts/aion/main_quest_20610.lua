local M = {}
local npc_names = require("aion.npc_names")

M.quest_id = 20610
M.big_map_id = 220010000
M.npc = {
    name_key = "MQ20610_NPC_001_START",
    name = npc_names.MQ20610_NPC_001_START,
    interact_id = 2147514375,
    x = 560.99,
    y = 2786.03,
    z = 299.06,
}
M.reward_npc = {
    name_key = "MQ20610_NPC_002_REWARD",
    name = npc_names.MQ20610_NPC_002_REWARD,
    interact_id = 2147524326,
    x = 223.97,
    y = 2679.86,
    z = 295.25,
}

M.dialog_steps = {
    select_quest = {
        content_id = 10,
        action = "ClickDialogLastContinuousOk",
        reason = "complete quest 20610 opening dialog by last-option chain",
    },
    select1 = {
        content_id = 1011,
        action = "ClickDialogLastContinuousOk",
        reason = "complete quest 20610 opening dialog by last-option chain",
    },
    select1_1 = {
        content_id = 1012,
        action = "ClickDialogLastContinuousOk",
        reason = "complete quest 20610 opening dialog by last-option chain",
    },
    select1_1_1 = {
        content_id = 1013,
        action = "ClickDialogLastContinuousOk",
        reason = "complete quest 20610 opening dialog by last-option chain",
    },
    select1_1_1_1 = {
        content_id = 1014,
        action = "ClickDialogLastContinuousOk",
        reason = "complete quest 20610 opening dialog by last-option chain",
    },
}

M.task_ui = {
    teleport_to_npc = {
        stage = "quest_20610_task_teleport",
    },
}

M.reward_dialog_steps = {
    select_success = {
        content_id = 10002,
        action = "ClickDialogX",
        reason = "open quest 20610 reward selection",
    },
    select_quest_reward1 = {
        content_id = 5,
        action = "ClickDialogOkCompleteQuest",
        reason = "confirm quest 20610 reward",
    },
}

local function number(value)
    return tonumber(value) or 0
end

local function trim_text(value)
    local text = tostring(value or "")
    text = string.gsub(text, "^%s+", "")
    text = string.gsub(text, "%s+$", "")
    return text
end

local function dialog_matches_npc_name(dialog, npc)
    if type(dialog) ~= "table" or type(npc) ~= "table" then
        return false
    end
    local expected_name = trim_text(npc.name)
    if expected_name == "" then
        return false
    end
    local actual_name = trim_text(dialog.npc_name or dialog.name or dialog.target_name)
    if actual_name == "" then
        return true
    end
    return actual_name == expected_name
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

function M.distanceToNpc(char)
    return distance3(char, M.npc)
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

function M.isQuestKnown(quest)
    return type(quest) == "table"
        and number(quest.id) == M.quest_id
end

function M.isQuestActive(quest)
    return M.isQuestKnown(quest)
        and number(quest.status_code) == 3
end

function M.isQuestDone(quest)
    return M.isQuestKnown(quest)
        and number(quest.status_code) == 4
end

function M.isRewardDialog(dialog)
    if type(dialog) ~= "table" then
        return false
    end
    return M.reward_dialog_steps[tostring(dialog.type_text or "")] ~= nil
        and dialog_matches_npc_name(dialog, M.reward_npc)
end

function M.isStartDialog(dialog)
    if type(dialog) ~= "table" then
        return false
    end
    return M.dialog_steps[tostring(dialog.type_text or "")] ~= nil
        and dialog_matches_npc_name(dialog, M.npc)
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

function M.nextTaskTeleportAction(state, runtime, opts, quest)
    local teleport_to_npc = M.task_ui.teleport_to_npc

    return action("QuestTeleport", "quest 20610 teleport to target npc", {
        quest_id = M.quest_id,
        quest_step = M.questStep(quest),
        stage = teleport_to_npc.stage,
        wait_teleport = true,
    })
end

function M.nextRewardAction(state, runtime, opts, quest)
    if runtime.completed_20610_reward == true then
        return action("Idle", "quest 20610 reward already completed", { quest_id = M.quest_id })
    end

    local char = state.char
    if type(char) ~= "table" then
        return action("ReadState", "character unavailable", { quest_id = M.quest_id })
    end

    local range = number(opts.npc_range)
    if range <= 0 then
        range = 4
    end
    local dist = M.distanceToRewardNpc(char)
    if dist > range then
        return action("NavigateToNpc", "move to quest 20610 reward npc", {
            quest_id = M.quest_id,
            quest_step = M.questStep(quest),
            stage = "quest_20610_reward_npc",
            interact_id = M.reward_npc.interact_id,
            npc_name = M.reward_npc.name,
            x = M.reward_npc.x,
            y = M.reward_npc.y,
            z = M.reward_npc.z,
            distance = dist,
            range = range,
        })
    end

    local dialog = state.dialog
    if type(dialog) ~= "table" then
        return action("InteractNpc", "open quest 20610 reward npc dialog", {
            quest_id = M.quest_id,
            quest_step = M.questStep(quest),
            stage = "quest_20610_reward_npc",
            interact_id = M.reward_npc.interact_id,
            npc_name = M.reward_npc.name,
            npc_name_key = M.reward_npc.name_key,
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
            npc_name = M.reward_npc.name,
            stage = "quest_20610_reward_npc",
        })
    end

    return action("DumpDialog", "unknown quest 20610 reward dialog stage", {
        quest_id = M.quest_id,
        quest_step = M.questStep(quest),
        type_text = type_text,
        content_id = number(dialog.dialog_content_id),
        npc_dialog_id = number(dialog.npc_dialog_id),
        interact_id = M.reward_npc.interact_id,
        npc_name = M.reward_npc.name,
        stage = "quest_20610_reward_npc",
    })
end

function M.nextStartDialogAction(state, opts, quest)
    opts = opts or {}
    local dialog = state and state.dialog
    local type_text = tostring(dialog and dialog.type_text or "")
    local step = M.dialog_steps[type_text]
    if step then
        return action(step.action, step.reason, {
            quest_id = M.quest_id,
            quest_step = M.questStep(quest),
            expected_content_id = step.content_id,
            content_id = number(dialog.dialog_content_id),
            type_text = type_text,
            click_x = opts.dialog_click_x or 25,
            interact_id = M.npc.interact_id,
            npc_name = M.npc.name,
            stage = "quest_20610_npc",
        })
    end

    return action("DumpDialog", "unknown quest 20610 dialog stage", {
        quest_id = M.quest_id,
        quest_step = M.questStep(quest),
        type_text = type_text,
        content_id = number(dialog and dialog.dialog_content_id),
        npc_dialog_id = number(dialog and dialog.npc_dialog_id),
        interact_id = M.npc.interact_id,
        npc_name = M.npc.name,
        stage = "quest_20610_npc",
    })
end

function M.nextAction(state, runtime, opts)
    state = state or {}
    runtime = runtime or {}
    opts = opts or {}

    if runtime.waiting_teleport == true
        and tostring(runtime.teleport_stage or "") == M.task_ui.teleport_to_npc.stage then
        local detected, reason = M.teleportDetected(state, runtime, opts)
        if detected then
            return action("CompleteQuestTeleport", reason, {
                quest_id = M.quest_id,
                stage = M.task_ui.teleport_to_npc.stage,
            })
        end
        return action("WaitPositionChanged", reason, {
            quest_id = M.quest_id,
            stage = M.task_ui.teleport_to_npc.stage,
        })
    end

    local quest = state.quest or M.findQuest(state.quests)
    if runtime.completed_20610_start_dialog ~= true
        and M.isStartDialog(state.dialog) then
        return M.nextStartDialogAction(state, opts, quest)
    end

    if runtime.completed_20610_start_dialog == true
        and runtime.completed_20610_task_teleport ~= true then
        return M.nextTaskTeleportAction(state, runtime, opts, quest)
    end
    if M.isQuestDone(quest) then
        if runtime.completed_20610_reward == true then
            return action("Idle", "quest 20610 reward already completed", { quest_id = M.quest_id })
        end
        local range = number(opts.npc_range)
        if range <= 0 then
            range = 4
        end
        if runtime.completed_20610_task_teleport == true
            or M.isRewardDialog(state.dialog)
            or M.distanceToRewardNpc(state.char) <= range then
            return M.nextRewardAction(state, runtime, opts, quest)
        end
        return M.nextTaskTeleportAction(state, runtime, opts, quest)
    end

    local char = state.char
    if type(char) ~= "table" then
        return action("ReadState", "character unavailable", { quest_id = M.quest_id })
    end

    if not M.isQuestActive(quest) then
        return action("Idle", "quest 20610 is not active", { quest_id = M.quest_id })
    end

    local current_big_map = number(state.big_map_id)
    if current_big_map > 0 and current_big_map ~= M.big_map_id then
        return action("Idle", "quest 20610 wrong map", {
            quest_id = M.quest_id,
            big_map_id = current_big_map,
            expected_big_map_id = M.big_map_id,
        })
    end

    local range = number(opts.npc_range)
    if range <= 0 then
        range = 4
    end
    local dist = M.distanceToNpc(char)
    if dist > range then
        return action("NavigateToNpc", "move to quest 20610 npc", {
            quest_id = M.quest_id,
            quest_step = M.questStep(quest),
            stage = "quest_20610_npc",
            interact_id = M.npc.interact_id,
            npc_name = M.npc.name,
            x = M.npc.x,
            y = M.npc.y,
            z = M.npc.z,
            distance = dist,
            range = range,
        })
    end

    local dialog = state.dialog
    if type(dialog) ~= "table" then
        return action("InteractNpc", "open quest 20610 npc dialog", {
            quest_id = M.quest_id,
            quest_step = M.questStep(quest),
            stage = "quest_20610_npc",
            interact_id = M.npc.interact_id,
            npc_name = M.npc.name,
            npc_name_key = M.npc.name_key,
        })
    end

    return M.nextStartDialogAction(state, opts, quest)
end

return M
