local M = {}
local npc_names = require("aion.npc_names")

M.quest_id = 20611
M.quest_ids = { 20611, 20612, 20613, 20614, 20615 }
M.quest_id_min = 20611
M.quest_id_max = 20699
M.remote_reward_quest_id = 24340
M.remote_reward_quest_ids = { 24340, 24341 }
M.big_map_id = 220010000
M.level_move_stage = "quest_20611_level_move"
M.level_grind_stage = "quest_20611_level_grind"
M.obelisk_stage = "quest_20611_obelisk"
M.grind_point = {
    x = 194.491,
    y = 2689.982,
    z = 300.625,
}
M.npc = {
    name_key = "MQ20611_NPC_001_MISSION",
    name = npc_names.MQ20611_NPC_001_MISSION,
    interact_id = 2147503111,
    x = 586.22,
    y = 2465.17,
    z = 278.58,
}
M.obelisk = {
    name_key = "MQ20611_NPC_002_OBELISK",
    name = npc_names.MQ20611_NPC_002_OBELISK,
    interact_id = 2147505051,
    x = 587.69,
    y = 2467.10,
    z = 278.79,
}
M.obelisk_confirm = {
    x = 684,
    y = 437,
    tolerance = 90,
}
M.dialog_steps = {
    select_quest = {
        content_id = 10,
        action = "ClickDialogX",
        reason = "open quest 20611 mission detail",
        click_y = 324,
        click_y_tolerance = 8,
    },
    select1 = {
        content_id = 1011,
        action = "ClickDialogX",
        reason = "continue quest 20611 dialog 1",
    },
    select1_1 = {
        content_id = 1012,
        action = "ClickDialogX",
        reason = "continue quest 20611 dialog 2",
    },
    select1_1_1 = {
        content_id = 1013,
        action = "ClickDialogX",
        reason = "continue quest 20611 dialog 3",
    },
    select1_1_1_1 = {
        content_id = 1014,
        action = "ClickDialogXCompleteQuest",
        reason = "complete quest 20611 mission dialog",
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

local function quest_id(quest)
    return number(quest and quest.id)
end

local function is_earlier_quest(candidate, current)
    if not current then
        return true
    end
    local current_seq = number(current.seq)
    local seq = number(candidate.seq)
    local current_level = number(current.lv_num)
    local level = number(candidate.lv_num)
    if seq > 0 and (current_seq <= 0 or seq < current_seq) then
        return true
    end
    if seq == current_seq and level > 0 and (current_level <= 0 or level < current_level) then
        return true
    end
    if seq == current_seq and level == current_level then
        local id = quest_id(candidate)
        local current_id = quest_id(current)
        return id > 0 and (current_id <= 0 or id < current_id)
    end
    return false
end

local function anchor_from_char(char)
    local anchor = {
        x = number(char and char.x),
        y = number(char and char.y),
        z = number(char and char.z),
    }
    if anchor.x == 0 and anchor.y == 0 and anchor.z == 0 then
        anchor = M.grind_point
    end
    return anchor
end

function M.isRemoteRewardQuestId(id)
    id = number(id)
    if id == number(M.remote_reward_quest_id) then
        return true
    end
    for _, supported_id in ipairs(M.remote_reward_quest_ids or {}) do
        if id == number(supported_id) then
            return true
        end
    end
    return false
end

local function is_supported_quest_id(id)
    id = number(id)
    if id >= M.quest_id_min and id <= M.quest_id_max then
        return true
    end
    if id == M.quest_id then
        return true
    end
    for _, supported_id in ipairs(M.quest_ids or {}) do
        if id == number(supported_id) then
            return true
        end
    end
    return false
end

function M.distanceToGrindPoint(char)
    return distance3(char, M.grind_point)
end

function M.distanceToNpc(char)
    return distance3(char, M.npc)
end

function M.distanceToObelisk(char)
    return distance3(char, M.obelisk)
end

function M.questStep(quest)
    return number(quest and quest.req_count)
end

function M.questRequiredLevel(quest)
    return number(quest and quest.lv_num)
end

function M.findQuest(quests)
    local fallback = nil
    local done = nil
    for _, quest in ipairs(quests or {}) do
        if is_supported_quest_id(quest_id(quest)) then
            if M.isQuestActive(quest) then
                return quest
            elseif M.isQuestDone(quest) then
                done = done or quest
            else
                fallback = fallback or quest
            end
        end
    end
    return fallback or done
end

function M.findQuestById(quests, id)
    id = number(id)
    if id <= 0 then
        return nil
    end
    for _, quest in ipairs(quests or {}) do
        if quest_id(quest) == id then
            return quest
        end
    end
    return nil
end

function M.findActiveQuest(quests)
    local best = nil
    for _, quest in ipairs(quests or {}) do
        if M.isQuestActive(quest) and is_earlier_quest(quest, best) then
            best = quest
        end
    end
    return best
end

function M.findLevelBlockedQuest(quests)
    local best = nil
    for _, quest in ipairs(quests or {}) do
        if is_supported_quest_id(quest_id(quest))
            and number(quest.status_code) == 6 then
            if is_earlier_quest(quest, best) then
                best = quest
            end
        end
    end
    return best
end

function M.findRemoteRewardQuest(quests)
    local active = nil
    local ready = nil
    local fallback = nil
    for _, quest in ipairs(quests or {}) do
        if M.isRemoteRewardQuestId(quest_id(quest)) then
            if number(quest.status_code) == 4 then
                ready = ready or quest
            elseif number(quest.status_code) == 3 then
                active = active or quest
            else
                fallback = fallback or quest
            end
        end
    end
    return ready or active or fallback
end

function M.isQuestKnown(quest)
    return type(quest) == "table"
        and is_supported_quest_id(quest_id(quest))
end

function M.isQuestActive(quest)
    return M.isQuestKnown(quest)
        and number(quest.status_code) == 3
end

function M.isQuestDone(quest)
    return M.isQuestKnown(quest)
        and number(quest.status_code) == 4
end

function M.isQuestLevelBlocked(quest)
    return M.isQuestKnown(quest)
        and number(quest.status_code) == 6
end

function M.isRemoteRewardQuest(quest)
    return type(quest) == "table"
        and M.isRemoteRewardQuestId(quest_id(quest))
end

function M.isRemoteRewardReady(quest)
    return M.isRemoteRewardQuest(quest)
        and number(quest.status_code) == 4
end

function M.isRemoteGrindActive(quest)
    return M.isRemoteRewardQuest(quest)
        and number(quest.status_code) == 3
end

function M.isRemoteRewardDialog(dialog)
    if type(dialog) ~= "table" then
        return false
    end
    return M.isRemoteRewardQuestId(dialog.quest_id)
        and tostring(dialog.type_text or "") == "select_quest_reward_remote"
end

function M.isMissionNpcDialog(dialog)
    return type(dialog) == "table"
        and number(dialog.npc_dialog_id) == M.npc.interact_id
end

function M.isObeliskConfirmVisible(state)
    return type(state) == "table"
        and type(state.ui) == "table"
        and state.ui.obelisk_confirm_visible == true
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

function M.nextMissionNpcAction(state, runtime, opts, quest)
    state = state or {}
    runtime = runtime or {}
    opts = opts or {}
    quest = quest or state.quest or M.findQuestById(state.quests, M.quest_id)

    if runtime.completed_20611_mission_dialog == true then
        return action("Idle", "quest 20611 mission dialog already completed", {
            quest_id = M.quest_id,
            quest_step = M.questStep(quest),
            stage = "quest_20611_mission_npc",
        })
    end

    local dialog = state.dialog
    if M.isMissionNpcDialog(dialog) then
        local type_text = tostring(dialog.type_text or "")
        local step = M.dialog_steps[type_text]
        if step then
            return action(step.action, step.reason, {
                quest_id = M.quest_id,
                quest_step = M.questStep(quest),
                expected_content_id = step.content_id,
                content_id = number(dialog.dialog_content_id),
                type_text = type_text,
                click_x = opts.dialog_click_x or 25,
                click_y = step.click_y,
                click_y_tolerance = step.click_y_tolerance,
                interact_id = M.npc.interact_id,
                npc_name = M.npc.name,
                npc_name_key = M.npc.name_key,
                stage = "quest_20611_mission_npc",
            })
        end

        return action("DumpDialog", "unknown quest 20611 mission dialog stage", {
            quest_id = M.quest_id,
            quest_step = M.questStep(quest),
            type_text = type_text,
            content_id = number(dialog.dialog_content_id),
            npc_dialog_id = number(dialog.npc_dialog_id),
            interact_id = M.npc.interact_id,
            npc_name = M.npc.name,
            npc_name_key = M.npc.name_key,
            stage = "quest_20611_mission_npc",
        })
    end

    if type(dialog) == "table" then
        return action("DumpDialog", "different npc dialog is already open", {
            quest_id = M.quest_id,
            quest_step = M.questStep(quest),
            type_text = tostring(dialog.type_text or ""),
            content_id = number(dialog.dialog_content_id),
            npc_dialog_id = number(dialog.npc_dialog_id),
            interact_id = M.npc.interact_id,
            npc_name = M.npc.name,
            npc_name_key = M.npc.name_key,
            stage = "quest_20611_mission_npc",
        })
    end

    local char = state.char
    if type(char) ~= "table" then
        return action("ReadState", "character unavailable", { quest_id = M.quest_id })
    end

    local current_big_map = number(state.big_map_id)
    if current_big_map > 0 and current_big_map ~= M.big_map_id then
        return action("Idle", "quest 20611 mission npc wrong map", {
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
        return action("NavigateToNpc", "move to quest 20611 mission npc", {
            quest_id = M.quest_id,
            quest_step = M.questStep(quest),
            stage = "quest_20611_mission_npc",
            interact_id = M.npc.interact_id,
            npc_name = M.npc.name,
            npc_name_key = M.npc.name_key,
            x = M.npc.x,
            y = M.npc.y,
            z = M.npc.z,
            distance = dist,
            range = range,
        })
    end

    return action("InteractNpc", "open quest 20611 mission npc dialog", {
        quest_id = M.quest_id,
        quest_step = M.questStep(quest),
        stage = "quest_20611_mission_npc",
        interact_id = M.npc.interact_id,
        npc_name = M.npc.name,
        npc_name_key = M.npc.name_key,
    })
end

function M.nextObeliskAction(state, runtime, opts, quest)
    state = state or {}
    runtime = runtime or {}
    opts = opts or {}
    quest = quest or state.quest or M.findQuestById(state.quests, M.quest_id)

    if runtime.completed_20611_obelisk == true then
        return action("Idle", "quest 20611 obelisk already confirmed", {
            quest_id = M.quest_id,
            quest_step = M.questStep(quest),
            stage = M.obelisk_stage,
        })
    end

    if M.isObeliskConfirmVisible(state) or runtime.opened_20611_obelisk == true then
        return action("ClickObeliskConfirm", "confirm quest 20611 obelisk registration", {
            quest_id = M.quest_id,
            quest_step = M.questStep(quest),
            stage = M.obelisk_stage,
            npc_name = M.obelisk.name,
            npc_name_key = M.obelisk.name_key,
            confirm_x = M.obelisk_confirm.x,
            confirm_y = M.obelisk_confirm.y,
            confirm_tolerance = M.obelisk_confirm.tolerance,
        })
    end

    if type(state.dialog) == "table" then
        return action("DumpDialog", "different npc dialog is already open before obelisk confirm", {
            quest_id = M.quest_id,
            quest_step = M.questStep(quest),
            type_text = tostring(state.dialog.type_text or ""),
            content_id = number(state.dialog.dialog_content_id),
            npc_dialog_id = number(state.dialog.npc_dialog_id),
            interact_id = M.obelisk.interact_id,
            npc_name = M.obelisk.name,
            npc_name_key = M.obelisk.name_key,
            stage = M.obelisk_stage,
        })
    end

    local char = state.char
    if type(char) ~= "table" then
        return action("ReadState", "character unavailable", { quest_id = M.quest_id })
    end

    local current_big_map = number(state.big_map_id)
    if current_big_map > 0 and current_big_map ~= M.big_map_id then
        return action("Idle", "quest 20611 obelisk wrong map", {
            quest_id = M.quest_id,
            quest_step = M.questStep(quest),
            big_map_id = current_big_map,
            expected_big_map_id = M.big_map_id,
            stage = M.obelisk_stage,
        })
    end

    local range = number(opts.npc_range)
    if range <= 0 then
        range = 4
    end
    local dist = M.distanceToObelisk(char)
    if dist > range then
        return action("NavigateToNpc", "move to quest 20611 obelisk", {
            quest_id = M.quest_id,
            quest_step = M.questStep(quest),
            stage = M.obelisk_stage,
            interact_id = M.obelisk.interact_id,
            npc_name = M.obelisk.name,
            npc_name_key = M.obelisk.name_key,
            x = M.obelisk.x,
            y = M.obelisk.y,
            z = M.obelisk.z,
            distance = dist,
            range = range,
        })
    end

    return action("InteractNpc", "open quest 20611 obelisk confirm popup", {
        quest_id = M.quest_id,
        quest_step = M.questStep(quest),
        stage = M.obelisk_stage,
        interact_id = M.obelisk.interact_id,
        npc_name = M.obelisk.name,
        npc_name_key = M.obelisk.name_key,
    })
end

function M.nextAction(state, runtime, opts)
    state = state or {}
    runtime = runtime or {}
    opts = opts or {}

    if runtime.waiting_teleport == true
        and tostring(runtime.teleport_stage or "") == M.level_move_stage then
        local detected, reason = M.teleportDetected(state, runtime, opts)
        if detected then
            return action("CompleteQuestTeleport", reason, {
                quest_id = M.quest_id,
                stage = M.level_move_stage,
            })
        end
        return action("WaitPositionChanged", reason, {
            quest_id = M.quest_id,
            stage = M.level_move_stage,
            min_distance = opts.teleport_min_distance or 20,
        })
    end

    local remote_reward_quest = state.remote_reward_quest or M.findRemoteRewardQuest(state.quests)
    if M.isRemoteRewardDialog(state.dialog) then
        local dialog_qid = number(state.dialog.quest_id)
        if dialog_qid <= 0 then
            dialog_qid = quest_id(remote_reward_quest)
        end
        return action("ClickDialogOkCompleteQuest", "confirm blue grind remote reward", {
            quest_id = dialog_qid,
            quest_step = M.questStep(remote_reward_quest),
            stage = "quest_20611_remote_reward",
            content_id = number(state.dialog.dialog_content_id),
            type_text = tostring(state.dialog.type_text or ""),
        })
    end
    if M.isRemoteRewardReady(remote_reward_quest) then
        local ready_qid = quest_id(remote_reward_quest)
        return action("OpenQuestSubmit", "open blue grind remote reward", {
            quest_id = ready_qid,
            quest_step = M.questStep(remote_reward_quest),
            stage = "quest_20611_remote_reward",
        })
    end

    local quest = nil
    if M.isRemoteGrindActive(remote_reward_quest)
        and runtime.completed_20611_grind ~= true then
        quest = remote_reward_quest
    end
    local qid = quest_id(quest)
    if qid <= 0 then
        qid = M.quest_id
    end

    if not M.isRemoteGrindActive(quest) then
        local active_quest = nil
        if M.isQuestActive(state.quest) then
            active_quest = state.quest
        else
            active_quest = M.findActiveQuest(state.quests)
        end
        if M.isQuestActive(active_quest) then
            local active_qid = quest_id(active_quest)
            local active_step = M.questStep(active_quest)
            if active_qid == M.quest_id and active_step == 1 then
                return M.nextObeliskAction(state, runtime, opts, active_quest)
            end
            if active_qid == M.quest_id and active_step > 1 then
                return action("Idle", "quest 20611 next step is not recorded yet", {
                    quest_id = active_qid,
                    quest_step = active_step,
                })
            end
            local range = number(opts.npc_range)
            if range <= 0 then
                range = 4
            end
            local near_mission_npc = type(state.char) == "table"
                and M.distanceToNpc(state.char) <= range
            if active_qid == M.quest_id
                and (M.isMissionNpcDialog(state.dialog) or near_mission_npc) then
                return M.nextMissionNpcAction(state, runtime, opts, active_quest)
            end
            if active_qid == M.quest_id
                and runtime.completed_20611_level_move == true then
                return action("Idle", "waiting quest 20611 teleport landing", {
                    quest_id = active_qid,
                    quest_step = M.questStep(active_quest),
                    stage = M.level_move_stage,
                })
            end
            local required_level = M.questRequiredLevel(active_quest)
            if required_level <= 0 and number(runtime.level_grind_quest_id) == active_qid then
                required_level = number(runtime.level_grind_required_level)
            end
            local char_level = number(state.char and state.char.level)
            if required_level > 0 then
                if type(state.char) ~= "table" then
                    return action("ReadState", "character unavailable", { quest_id = active_qid })
                end
                if char_level <= 0 then
                    return action("ReadState", "character level unavailable", { quest_id = active_qid })
                end
                if char_level >= required_level then
                    if runtime.completed_20611_level_move == true
                        and number(runtime.level_move_quest_id) == active_qid then
                        return action("Idle", "yellow mission immediate move already requested", {
                            quest_id = active_qid,
                            required_level = required_level,
                            char_level = char_level,
                            stage = M.level_move_stage,
                        })
                    end
                    return action("QuestTeleport", "active yellow mission level reached; immediate move", {
                        quest_id = active_qid,
                        quest_step = M.questStep(active_quest),
                        required_level = required_level,
                        char_level = char_level,
                        stage = M.level_move_stage,
                        wait_teleport = true,
                    })
                end
            end
        end

        local active_stage = tostring(runtime.active_20611_grind_stage or "")
        local tracked_level_qid = number(runtime.level_grind_quest_id)
        if runtime.active_20611_grind == true
            and active_stage == M.level_grind_stage
            and tracked_level_qid > 0 then
            if type(state.char) ~= "table" then
                return action("ReadState", "character unavailable", { quest_id = tracked_level_qid })
            end
            local tracked_quest = M.findQuestById(state.quests, tracked_level_qid)
            local required_level = number(runtime.level_grind_required_level)
            if required_level <= 0 then
                required_level = M.questRequiredLevel(tracked_quest)
            end
            local char_level = number(state.char and state.char.level)
            if required_level > 0 and char_level <= 0 then
                return action("ReadState", "character level unavailable", { quest_id = tracked_level_qid })
            end
            if required_level > 0 and char_level >= required_level then
                return action("QuestTeleport", "tracked yellow mission level reached; immediate move", {
                    quest_id = tracked_level_qid,
                    quest_step = M.questStep(tracked_quest),
                    required_level = required_level,
                    char_level = char_level,
                    stage = M.level_move_stage,
                    wait_teleport = true,
                })
            end
            return action("WaitLevelGrind", "tracked yellow mission level grind running", {
                quest_id = tracked_level_qid,
                quest_step = M.questStep(tracked_quest),
                required_level = required_level,
                char_level = char_level,
                stage = M.level_grind_stage,
            })
        end

        local level_quest = state.level_blocked_quest or M.findLevelBlockedQuest(state.quests)
        if M.isQuestLevelBlocked(level_quest) then
            local level_qid = quest_id(level_quest)
            if type(state.char) ~= "table" then
                return action("ReadState", "character unavailable", { quest_id = level_qid })
            end
            local required_level = M.questRequiredLevel(level_quest)
            local char_level = number(state.char and state.char.level)
            if required_level > 0 and char_level <= 0 then
                return action("ReadState", "character level unavailable", { quest_id = level_qid })
            end
            if required_level > 0 and char_level < required_level then
                local active_stage = tostring(runtime.active_20611_grind_stage or "")
                if runtime.active_20611_grind == true
                    and active_stage == M.level_grind_stage
                    and number(runtime.level_grind_quest_id) == level_qid then
                    return action("WaitLevelGrind", "yellow mission level grind running", {
                        quest_id = level_qid,
                        quest_step = M.questStep(level_quest),
                        required_level = required_level,
                        char_level = char_level,
                        stage = M.level_grind_stage,
                    })
                end
                local anchor = anchor_from_char(state.char)
                return action("StartStationaryGrind", "start yellow mission level grind", {
                    quest_id = level_qid,
                    quest_step = M.questStep(level_quest),
                    required_level = required_level,
                    char_level = char_level,
                    until_level = required_level,
                    stage = M.level_grind_stage,
                    x = anchor.x,
                    y = anchor.y,
                    z = anchor.z,
                })
            end
            if runtime.active_20611_grind == true
                and tostring(runtime.active_20611_grind_stage or "") == M.level_grind_stage then
                return action("QuestTeleport", "yellow mission level reached; immediate move", {
                    quest_id = level_qid,
                    quest_step = M.questStep(level_quest),
                    required_level = required_level,
                    char_level = char_level,
                    stage = M.level_move_stage,
                    wait_teleport = true,
                })
            end
            if runtime.completed_20611_level_move == true
                and number(runtime.level_move_quest_id) == level_qid then
                return action("Idle", "yellow mission immediate move already requested", {
                    quest_id = level_qid,
                    required_level = required_level,
                    char_level = char_level,
                    stage = M.level_move_stage,
                })
            end
            return action("QuestTeleport", "yellow mission immediate move", {
                quest_id = level_qid,
                quest_step = M.questStep(level_quest),
                required_level = required_level,
                char_level = char_level,
                stage = M.level_move_stage,
                wait_teleport = true,
            })
        end
        if runtime.completed_20611_grind == true then
            return action("Idle", "blue grind quest already completed", { quest_id = M.remote_reward_quest_id })
        end
        return action("Idle", "blue normal grind task is not active", { quest_id = M.remote_reward_quest_id })
    end

    local char = state.char
    if type(char) ~= "table" then
        return action("ReadState", "character unavailable", { quest_id = qid })
    end

    local current_big_map = number(state.big_map_id)
    if current_big_map > 0 and current_big_map ~= M.big_map_id then
        return action("Idle", "blue grind quest wrong map", {
            quest_id = qid,
            big_map_id = current_big_map,
            expected_big_map_id = M.big_map_id,
        })
    end

    local range = number(opts.grind_point_range)
    if range <= 0 then
        range = 3
    end
    local dist = M.distanceToGrindPoint(char)
    if dist > range then
        return action("NavigateToGrindPoint", "move to blue grind quest point", {
            quest_id = qid,
            quest_step = M.questStep(quest),
            stage = "quest_20611_grind",
            x = M.grind_point.x,
            y = M.grind_point.y,
            z = M.grind_point.z,
            distance = dist,
            range = range,
        })
    end

    if runtime.active_20611_grind == true then
        return action("WaitQuestComplete", "blue grind quest stationary grind running", {
            quest_id = qid,
            quest_step = M.questStep(quest),
            stage = "quest_20611_grind",
        })
    end

    local anchor = anchor_from_char(char)

    return action("StartStationaryGrind", "start blue grind quest stationary grind", {
        quest_id = qid,
        quest_step = M.questStep(quest),
        stage = "quest_20611_grind",
        x = anchor.x,
        y = anchor.y,
        z = anchor.z,
    })
end

return M
