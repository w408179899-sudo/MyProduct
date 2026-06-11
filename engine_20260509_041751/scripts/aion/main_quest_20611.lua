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
M.indicator_title_stage = "quest_20611_indicator_title"
M.target_link_stage = "quest_20611_target_link"
M.target_teleport_stage = "quest_20611_target_teleport"
M.hotspot_teleport_stage = "quest_20611_hotspot_teleport"
M.hotspot_reward_stage = "quest_20611_hotspot_reward_npc"
M.quest_20612_id = 20612
M.quest_20612_required_level = 11
M.quest_20612_level_grind_stage = "quest_20612_level_grind"
M.quest_20612_start_stage = "quest_20612_start_npc"
M.quest_20612_teleport_stage = "quest_20612_task_teleport"
M.quest_20612_reward_stage = "quest_20612_reward_npc"
M.quest_20613_id = 20613
M.quest_20613_level_grind_stage = "quest_20613_level14_grind"
M.post_20612_level14_required_level = 14
M.grind_point = {
    x = 194.491,
    y = 2689.982,
    z = 300.625,
}
M.post_20612_level14_grind_point = {
    x = 1093.552,
    y = 2247.044,
    z = 254.250,
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
M.target_npc = {
    name_key = "MQ20611_NPC_003_TARGET",
    name = npc_names.MQ20611_NPC_003_TARGET,
    interact_id = 2147520815,
    x = 589.35,
    y = 2450.16,
    z = 278.38,
}
M.hotspot_node = {
    name = "투나프레 호수",
    name_en = "HOTSPOT_DF1_04",
    node_id = 66,
    x = 491.0,
    y = 2301.0,
    z = 300.0,
}
M.hotspot_reward_npc = {
    name_key = "MQ20611_NPC_004_HOTSPOT_REWARD",
    name = npc_names.MQ20611_NPC_004_HOTSPOT_REWARD,
    interact_id = 2147515597,
    x = 493.15,
    y = 2298.88,
    z = 248.42,
}
M.quest_20612_start_point = {
    x = 477.137,
    y = 2304.421,
    z = 250.734,
}
M.quest_20612_start_npc = {
    name_key = "MQ20611_NPC_004_HOTSPOT_REWARD",
    name = npc_names.MQ20611_NPC_004_HOTSPOT_REWARD,
    interact_id = 2147515597,
    x = 493.15,
    y = 2298.88,
    z = 248.42,
}
M.quest_20612_reward_npc = {
    name_key = "MQ20612_NPC_001_REWARD",
    name = npc_names.MQ20612_NPC_001_REWARD or "",
    interact_id = 2147495609,
    x = 1050.70,
    y = 2201.12,
    z = 262.81,
}
M.obelisk_confirm = {
    x = 684,
    y = 437,
    tolerance = 90,
}
M.indicator_title = {
    parent = "quest_indicator_dialog",
    name = "prototype",
    depth = 4,
}
M.indicator_entry_names = {
    "prototype",
    "htmltext",
    "title",
}
M.indicator_teleport = {
    parent = "quest_indicator_dialog",
    name = "teleport",
    depth = 4,
}
M.target_link = {
    parent = "v3_quest_dialog",
    x = 463,
    y = 171,
    tolerance = 45,
    depth = 6,
}
M.dictionary_teleport = {
    parent = "dictionary_dialog",
    name = "teleport_to_npc",
    depth = 6,
}
M.dialog_steps = {
    select_quest = {
        content_id = 10,
        action = "ClickDialogXContinuous",
        reason = "complete quest 20611 mission npc dialog by continuous x-click",
        click_y = 324,
        click_y_tolerance = 8,
    },
    select1 = {
        content_id = 1011,
        action = "ClickDialogXContinuous",
        reason = "complete quest 20611 mission npc dialog by continuous x-click",
    },
    select1_1 = {
        content_id = 1012,
        action = "ClickDialogXContinuous",
        reason = "complete quest 20611 mission npc dialog by continuous x-click",
    },
    select1_1_1 = {
        content_id = 1013,
        action = "ClickDialogXContinuous",
        reason = "complete quest 20611 mission npc dialog by continuous x-click",
    },
    select1_1_1_1 = {
        content_id = 1014,
        action = "ClickDialogXContinuous",
        reason = "complete quest 20611 mission npc dialog by continuous x-click",
    },
}
M.target_dialog_steps = {
    select_quest = {
        content_id = 10,
        action = "ClickDialogXContinuous",
        reason = "accept quest 20611 target npc dialog by continuous x-click",
    },
}
M.hotspot_reward_dialog_steps = {
    select_success = {
        content_id = 10002,
        action = "ClickDialogXContinuous",
        reason = "complete quest 20611 hotspot reward npc dialog by continuous x-click",
    },
}
M.quest_20612_start_dialog_steps = {
    select_quest = {
        content_id = 10,
        action = "ClickDialogXContinuous",
        reason = "accept quest 20612 start npc dialog by continuous x-click",
    },
}
M.quest_20612_reward_dialog_steps = {
    select_success = {
        content_id = 10002,
        action = "ClickDialogXContinuous",
        reason = "complete quest 20612 reward npc dialog by continuous x-click",
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

local function distance2(a, b)
    if type(a) ~= "table" or type(b) ~= "table" then
        return math.huge
    end
    local dx = number(a.x) - number(b.x)
    local dy = number(a.y) - number(b.y)
    return math.sqrt(dx * dx + dy * dy)
end

local function is_grind_action_name(name)
    return name == "StartStationaryGrind"
        or name == "WaitLevelGrind"
        or name == "WaitQuestComplete"
end

local function action(name, reason, params)
    params = params or {}
    if is_grind_action_name(name) then
        params.requires_combat = true
        params.task_step = "grind"
    end
    return {
        name = name,
        reason = reason or "",
        params = params,
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

function M.distanceToTargetNpc(char)
    return distance3(char, M.target_npc)
end

function M.distanceToHotspotRewardNpc(char)
    return distance3(char, M.hotspot_reward_npc)
end

function M.distanceToQuest20612StartPoint(char)
    return distance3(char, M.quest_20612_start_point)
end

function M.distanceToQuest20612StartNpc(char)
    return distance3(char, M.quest_20612_start_npc)
end

function M.distanceToQuest20612RewardNpc(char)
    return distance3(char, M.quest_20612_reward_npc)
end

function M.distanceToPost20612Level14GrindPoint(char)
    return distance3(char, M.post_20612_level14_grind_point)
end

function M.isNearQuest20612RewardNpc(state, opts)
    state = state or {}
    opts = opts or {}
    if type(state.char) ~= "table" then
        return false
    end
    local current_big_map = number(state.big_map_id)
    if current_big_map > 0 and current_big_map ~= M.big_map_id then
        return false
    end
    local range = number(opts.npc_range)
    if range <= 0 then
        range = 4
    end
    return M.distanceToQuest20612RewardNpc(state.char) <= range
end

function M.questStep(quest)
    return number(quest and quest.req_count)
end

function M.questRequiredLevel(quest)
    return number(quest and quest.lv_num)
end

function M.quest20612RequiredLevel(quest)
    local required = M.questRequiredLevel(quest)
    if required <= 0 then
        required = M.quest_20612_required_level
    end
    return required
end

function M.findQuest(quests)
    local active = nil
    local fallback = nil
    local done = nil
    for _, quest in ipairs(quests or {}) do
        if is_supported_quest_id(quest_id(quest)) then
            if M.isQuestActive(quest) then
                if is_earlier_quest(quest, active) then
                    active = quest
                end
            elseif M.isQuestDone(quest) then
                if is_earlier_quest(quest, done) then
                    done = quest
                end
            else
                if is_earlier_quest(quest, fallback) then
                    fallback = quest
                end
            end
        end
    end
    return active or fallback or done
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

local function sequential_quest_done(runtime, quest)
    runtime = runtime or {}
    local qid = quest_id(quest)
    if qid == M.quest_id then
        return M.isQuestDone(quest)
            and runtime.completed_20611_hotspot_reward == true
    end
    if qid == M.quest_20612_id then
        return M.isQuestDone(quest)
            and runtime.completed_20612_reward_dialog == true
    end
    return M.isQuestDone(quest)
end

function M.findSequentialQuest(quests, runtime)
    local best = nil
    for _, quest in ipairs(quests or {}) do
        if M.isQuestKnown(quest)
            and (M.isQuestActive(quest)
                or M.isQuestLevelBlocked(quest)
                or M.isQuestDone(quest))
            and not sequential_quest_done(runtime, quest) then
            if is_earlier_quest(quest, best) then
                best = quest
            end
        end
    end
    return best
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

function M.isTargetNpcDialog(dialog)
    return type(dialog) == "table"
        and number(dialog.npc_dialog_id) == M.target_npc.interact_id
end

function M.isHotspotRewardNpcDialog(dialog)
    return type(dialog) == "table"
        and number(dialog.npc_dialog_id) == M.hotspot_reward_npc.interact_id
end

function M.isQuest20612StartNpcDialog(dialog)
    return type(dialog) == "table"
        and number(dialog.npc_dialog_id) == M.quest_20612_start_npc.interact_id
end

function M.isQuest20612RewardNpcDialog(dialog)
    return type(dialog) == "table"
        and number(dialog.npc_dialog_id) == M.quest_20612_reward_npc.interact_id
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

function M.nextTargetNpcAction(state, runtime, opts, quest)
    state = state or {}
    opts = opts or {}
    quest = quest or state.quest or M.findQuestById(state.quests, M.quest_id)

    local dialog = state.dialog
    if M.isTargetNpcDialog(dialog) then
        local type_text = tostring(dialog.type_text or "")
        local step = M.target_dialog_steps[type_text]
        if step then
            local expected_content_id = number(step.content_id)
            if expected_content_id <= 0 then
                expected_content_id = number(dialog.dialog_content_id)
            end
            return action(step.action, step.reason, {
                quest_id = M.quest_id,
                quest_step = M.questStep(quest),
                expected_content_id = expected_content_id,
                content_id = number(dialog.dialog_content_id),
                type_text = type_text,
                click_x = opts.dialog_click_x or 25,
                click_y = step.click_y,
                click_y_tolerance = step.click_y_tolerance,
                max_steps = step.max_steps,
                delay_ms = step.delay_ms,
                interact_id = M.target_npc.interact_id,
                npc_name = M.target_npc.name,
                npc_name_key = M.target_npc.name_key,
                stage = "quest_20611_target_npc",
            })
        end

        return action("DumpDialog", "unknown quest 20611 target npc dialog stage", {
            quest_id = M.quest_id,
            quest_step = M.questStep(quest),
            type_text = type_text,
            content_id = number(dialog.dialog_content_id),
            npc_dialog_id = number(dialog.npc_dialog_id),
            interact_id = M.target_npc.interact_id,
            npc_name = M.target_npc.name,
            npc_name_key = M.target_npc.name_key,
            stage = "quest_20611_target_npc",
        })
    end

    if type(dialog) == "table" then
        return action("DumpDialog", "different npc dialog is already open before target npc", {
            quest_id = M.quest_id,
            quest_step = M.questStep(quest),
            type_text = tostring(dialog.type_text or ""),
            content_id = number(dialog.dialog_content_id),
            npc_dialog_id = number(dialog.npc_dialog_id),
            interact_id = M.target_npc.interact_id,
            npc_name = M.target_npc.name,
            npc_name_key = M.target_npc.name_key,
            stage = "quest_20611_target_npc",
        })
    end

    local char = state.char
    if type(char) ~= "table" then
        return action("ReadState", "character unavailable", { quest_id = M.quest_id })
    end

    local current_big_map = number(state.big_map_id)
    if current_big_map > 0 and current_big_map ~= M.big_map_id then
        return action("Idle", "quest 20611 target npc wrong map", {
            quest_id = M.quest_id,
            quest_step = M.questStep(quest),
            big_map_id = current_big_map,
            expected_big_map_id = M.big_map_id,
            stage = "quest_20611_target_npc",
        })
    end

    local range = number(opts.npc_range)
    if range <= 0 then
        range = 4
    end
    local dist = M.distanceToTargetNpc(char)
    if dist > range then
        return action("NavigateToNpc", "move to quest 20611 target npc", {
            quest_id = M.quest_id,
            quest_step = M.questStep(quest),
            stage = "quest_20611_target_npc",
            interact_id = M.target_npc.interact_id,
            npc_name = M.target_npc.name,
            npc_name_key = M.target_npc.name_key,
            x = M.target_npc.x,
            y = M.target_npc.y,
            z = M.target_npc.z,
            distance = dist,
            range = range,
        })
    end

    return action("InteractNpc", "open quest 20611 target npc dialog", {
        quest_id = M.quest_id,
        quest_step = M.questStep(quest),
        stage = "quest_20611_target_npc",
        interact_id = M.target_npc.interact_id,
        npc_name = M.target_npc.name,
        npc_name_key = M.target_npc.name_key,
    })
end

function M.nextIndicatorEntryName(runtime)
    local names = M.indicator_entry_names or {}
    local last = tostring(runtime and runtime.clicked_20611_indicator_entry_name or "")
    if last ~= "" then
        for index, name in ipairs(names) do
            if name == last then
                return names[(index % #names) + 1] or M.indicator_title.name
            end
        end
    end
    return M.indicator_title.name
end

function M.openCurrentTrackerAction(quest, reason, runtime)
    local qid = quest_id(quest)
    if qid <= 0 then
        qid = M.quest_id
    end
    local entry_name = M.nextIndicatorEntryName(runtime)
    return action("ClickUiControl", reason or "open current tracked quest detail", {
        quest_id = qid,
        quest_step = M.questStep(quest),
        stage = M.indicator_title_stage,
        parent = M.indicator_title.parent,
        name = entry_name,
        depth = M.indicator_title.depth,
        previous_name = tostring(runtime and runtime.clicked_20611_indicator_entry_name or ""),
    })
end

function M.currentTrackerTeleportAction(quest, runtime, params)
    params = params or {}
    local qid = number(params.quest_id)
    if qid <= 0 then
        qid = quest_id(quest)
    end
    if qid <= 0 then
        qid = M.quest_id
    end
    return action("ClickUiControlWaitTeleport", "current tracker direct teleport after panel did not open", {
        quest_id = qid,
        quest_step = params.quest_step or M.questStep(quest),
        stage = params.stage or M.target_teleport_stage,
        parent = M.indicator_teleport.parent,
        name = M.indicator_teleport.name,
        depth = M.indicator_teleport.depth,
        previous_name = tostring(runtime and runtime.clicked_20611_indicator_entry_name or ""),
        wait_teleport = true,
    })
end

function M.nextCurrentQuestTeleportAction(state, runtime, quest, reason, params)
    state = state or {}
    runtime = runtime or {}
    params = params or {}
    local ui = type(state.ui) == "table" and state.ui or {}
    if ui.quest_panel_visible ~= true
        or runtime.clicked_20611_indicator_title ~= true then
        local last_open_candidate = M.indicator_entry_names[#M.indicator_entry_names]
        if runtime.clicked_20611_indicator_title == true
            and tostring(runtime.clicked_20611_indicator_entry_name or "") == tostring(last_open_candidate or "") then
            return M.currentTrackerTeleportAction(quest, runtime, params)
        end
        return M.openCurrentTrackerAction(quest, "open current tracked quest before teleport", runtime)
    end

    local out = {}
    for key, value in pairs(params) do
        out[key] = value
    end
    local qid = number(out.quest_id)
    if qid <= 0 then
        qid = quest_id(quest)
    end
    if qid <= 0 then
        qid = M.quest_id
    end
    out.quest_id = qid
    out.quest_step = out.quest_step or M.questStep(quest)
    out.open_panel_key = false
    out.require_panel_visible = true
    if out.wait_teleport == nil then
        out.wait_teleport = true
    end
    return action("QuestTeleport", reason or "current quest panel visible; immediate move", out)
end

function M.nextTargetTeleportAction(state, runtime, opts, quest)
    state = state or {}
    runtime = runtime or {}
    opts = opts or {}
    quest = quest or state.quest or M.findQuestById(state.quests, M.quest_id)

    if runtime.completed_20611_target_teleport == true then
        return action("Idle", "quest 20611 target teleport already completed", {
            quest_id = M.quest_id,
            quest_step = M.questStep(quest),
            stage = M.target_teleport_stage,
        })
    end

    return M.nextCurrentQuestTeleportAction(state, runtime, quest, "quest 20611 quest panel visible; immediate move", {
        quest_id = M.quest_id,
        quest_step = M.questStep(quest),
        stage = M.target_teleport_stage,
        wait_teleport = true,
    })
end

function M.nextHotspotTeleportAction(state, runtime, opts, quest)
    state = state or {}
    runtime = runtime or {}
    opts = opts or {}
    quest = quest or state.quest or M.findQuestById(state.quests, M.quest_id)

    if runtime.completed_20611_hotspot_teleport == true then
        return M.nextHotspotRewardAction(state, runtime, opts, quest)
    end

    local arrival_range = number(opts.hotspot_arrival_range)
    if arrival_range <= 0 then
        arrival_range = 12
    end
    if distance2(state.char, M.hotspot_node) <= arrival_range then
        return M.nextHotspotRewardAction(state, runtime, opts, quest)
    end

    return action("MapNodeTeleportByName", "teleport quest 20611 to hotspot node", {
        quest_id = M.quest_id,
        quest_step = M.questStep(quest),
        stage = M.hotspot_teleport_stage,
        node_name = M.hotspot_node.name,
        node_name_en = M.hotspot_node.name_en,
        node_id = M.hotspot_node.node_id,
        x = M.hotspot_node.x,
        y = M.hotspot_node.y,
        z = M.hotspot_node.z,
        wait_teleport = true,
    })
end

function M.nextTargetStepAction(state, runtime, opts, quest)
    state = state or {}
    runtime = runtime or {}
    opts = opts or {}
    quest = quest or state.quest or M.findQuestById(state.quests, M.quest_id)

    local range = number(opts.npc_range)
    if range <= 0 then
        range = 4
    end
    local near_target_npc = type(state.char) == "table"
        and M.distanceToTargetNpc(state.char) <= range
    if M.isTargetNpcDialog(state.dialog) then
        return M.nextTargetNpcAction(state, runtime, opts, quest)
    end
    if runtime.completed_20611_target_dialog == true then
        return M.nextHotspotTeleportAction(state, runtime, opts, quest)
    end
    if near_target_npc or runtime.completed_20611_target_teleport == true then
        return M.nextTargetNpcAction(state, runtime, opts, quest)
    end

    return M.nextTargetTeleportAction(state, runtime, opts, quest)
end

function M.nextHotspotRewardAction(state, runtime, opts, quest)
    state = state or {}
    runtime = runtime or {}
    opts = opts or {}
    quest = quest or state.quest or M.findQuestById(state.quests, M.quest_id)

    if runtime.completed_20611_hotspot_reward == true then
        return action("Idle", "quest 20611 hotspot reward npc already completed", {
            quest_id = M.quest_id,
            quest_step = M.questStep(quest),
            stage = M.hotspot_reward_stage,
        })
    end

    local dialog = state.dialog
    if M.isHotspotRewardNpcDialog(dialog) then
        local type_text = tostring(dialog.type_text or "")
        local step = M.hotspot_reward_dialog_steps[type_text]
        if step then
            return action(step.action, step.reason, {
                quest_id = M.quest_id,
                quest_step = M.questStep(quest),
                expected_content_id = step.content_id,
                content_id = number(dialog.dialog_content_id),
                type_text = type_text,
                click_x = opts.dialog_click_x or 25,
                max_steps = step.max_steps,
                delay_ms = step.delay_ms,
                interact_id = M.hotspot_reward_npc.interact_id,
                npc_name = M.hotspot_reward_npc.name,
                npc_name_key = M.hotspot_reward_npc.name_key,
                stage = M.hotspot_reward_stage,
            })
        end

        return action("DumpDialog", "unknown quest 20611 hotspot reward npc dialog stage", {
            quest_id = M.quest_id,
            quest_step = M.questStep(quest),
            type_text = type_text,
            content_id = number(dialog.dialog_content_id),
            npc_dialog_id = number(dialog.npc_dialog_id),
            interact_id = M.hotspot_reward_npc.interact_id,
            npc_name = M.hotspot_reward_npc.name,
            npc_name_key = M.hotspot_reward_npc.name_key,
            stage = M.hotspot_reward_stage,
        })
    end

    if type(dialog) == "table" then
        return action("DumpDialog", "different npc dialog is already open before hotspot reward npc", {
            quest_id = M.quest_id,
            quest_step = M.questStep(quest),
            type_text = tostring(dialog.type_text or ""),
            content_id = number(dialog.dialog_content_id),
            npc_dialog_id = number(dialog.npc_dialog_id),
            interact_id = M.hotspot_reward_npc.interact_id,
            npc_name = M.hotspot_reward_npc.name,
            npc_name_key = M.hotspot_reward_npc.name_key,
            stage = M.hotspot_reward_stage,
        })
    end

    local char = state.char
    if type(char) ~= "table" then
        return action("ReadState", "character unavailable", { quest_id = M.quest_id })
    end

    local current_big_map = number(state.big_map_id)
    if current_big_map > 0 and current_big_map ~= M.big_map_id then
        return action("Idle", "quest 20611 hotspot reward npc wrong map", {
            quest_id = M.quest_id,
            quest_step = M.questStep(quest),
            big_map_id = current_big_map,
            expected_big_map_id = M.big_map_id,
            stage = M.hotspot_reward_stage,
        })
    end

    local range = number(opts.npc_range)
    if range <= 0 then
        range = 4
    end
    local dist = M.distanceToHotspotRewardNpc(char)
    if dist > range then
        return action("NavigateToNpc", "move to quest 20611 hotspot reward npc", {
            quest_id = M.quest_id,
            quest_step = M.questStep(quest),
            stage = M.hotspot_reward_stage,
            interact_id = M.hotspot_reward_npc.interact_id,
            npc_name = M.hotspot_reward_npc.name,
            npc_name_key = M.hotspot_reward_npc.name_key,
            x = M.hotspot_reward_npc.x,
            y = M.hotspot_reward_npc.y,
            z = M.hotspot_reward_npc.z,
            distance = dist,
            range = range,
        })
    end

    return action("InteractNpc", "open quest 20611 hotspot reward npc dialog", {
        quest_id = M.quest_id,
        quest_step = M.questStep(quest),
        stage = M.hotspot_reward_stage,
        interact_id = M.hotspot_reward_npc.interact_id,
        npc_name = M.hotspot_reward_npc.name,
        npc_name_key = M.hotspot_reward_npc.name_key,
        allow_interact_id_fallback = true,
    })
end

function M.nextQuest20612StartAction(state, runtime, opts, quest)
    state = state or {}
    runtime = runtime or {}
    opts = opts or {}
    quest = quest or state.quest or M.findQuestById(state.quests, M.quest_20612_id)

    if runtime.completed_20612_start_dialog == true then
        return M.nextQuest20612TaskTeleportAction(state, runtime, opts, quest)
    end

    local char = state.char
    if type(char) == "table" then
        local current_big_map = number(state.big_map_id)
        if current_big_map > 0 and current_big_map ~= M.big_map_id then
            return action("Idle", "quest 20612 start npc wrong map", {
                quest_id = M.quest_20612_id,
                quest_step = M.questStep(quest),
                big_map_id = current_big_map,
                expected_big_map_id = M.big_map_id,
                stage = M.quest_20612_start_stage,
            })
        end

        local point_range = number(opts.quest_20612_start_point_range)
        if point_range <= 0 then
            point_range = 3
        end
        local point_dist = M.distanceToQuest20612StartPoint(char)
        if runtime.reached_20612_start_point ~= true and point_dist > point_range then
            return action("NavigateToNpc", "move to quest 20612 start point", {
                quest_id = M.quest_20612_id,
                quest_step = M.questStep(quest),
                stage = M.quest_20612_start_stage,
                interact_id = M.quest_20612_start_npc.interact_id,
                npc_name = M.quest_20612_start_npc.name,
                npc_name_key = M.quest_20612_start_npc.name_key,
                x = M.quest_20612_start_point.x,
                y = M.quest_20612_start_point.y,
                z = M.quest_20612_start_point.z,
                distance = point_dist,
                range = point_range,
            })
        end
    elseif type(state.dialog) ~= "table" then
        return action("ReadState", "character unavailable", { quest_id = M.quest_20612_id })
    end

    local dialog = state.dialog
    if M.isQuest20612StartNpcDialog(dialog) then
        local type_text = tostring(dialog.type_text or "")
        local step = M.quest_20612_start_dialog_steps[type_text]
        if step then
            return action(step.action, step.reason, {
                quest_id = M.quest_20612_id,
                quest_step = M.questStep(quest),
                expected_content_id = step.content_id,
                content_id = number(dialog.dialog_content_id),
                type_text = type_text,
                click_x = opts.dialog_click_x or 25,
                click_y = step.click_y,
                click_y_tolerance = step.click_y_tolerance,
                max_steps = step.max_steps,
                delay_ms = step.delay_ms,
                interact_id = M.quest_20612_start_npc.interact_id,
                npc_name = M.quest_20612_start_npc.name,
                npc_name_key = M.quest_20612_start_npc.name_key,
                stage = M.quest_20612_start_stage,
                mark_20612_start_point_reached = true,
            })
        end

        return action("DumpDialog", "unknown quest 20612 start npc dialog stage", {
            quest_id = M.quest_20612_id,
            quest_step = M.questStep(quest),
            type_text = type_text,
            content_id = number(dialog.dialog_content_id),
            npc_dialog_id = number(dialog.npc_dialog_id),
            interact_id = M.quest_20612_start_npc.interact_id,
            npc_name = M.quest_20612_start_npc.name,
            npc_name_key = M.quest_20612_start_npc.name_key,
            stage = M.quest_20612_start_stage,
        })
    end

    if type(dialog) == "table" then
        return action("DumpDialog", "different npc dialog is already open before quest 20612 start npc", {
            quest_id = M.quest_20612_id,
            quest_step = M.questStep(quest),
            type_text = tostring(dialog.type_text or ""),
            content_id = number(dialog.dialog_content_id),
            npc_dialog_id = number(dialog.npc_dialog_id),
            interact_id = M.quest_20612_start_npc.interact_id,
            npc_name = M.quest_20612_start_npc.name,
            npc_name_key = M.quest_20612_start_npc.name_key,
            stage = M.quest_20612_start_stage,
        })
    end

    if type(char) == "table" then
        local npc_range = number(opts.npc_range)
        if npc_range <= 0 then
            npc_range = 4
        end
        local npc_dist = M.distanceToQuest20612StartNpc(char)
        if npc_dist > npc_range then
            return action("NavigateToNpc", "move from quest 20612 start point to npc", {
                quest_id = M.quest_20612_id,
                quest_step = M.questStep(quest),
                stage = M.quest_20612_start_stage,
                interact_id = M.quest_20612_start_npc.interact_id,
                npc_name = M.quest_20612_start_npc.name,
                npc_name_key = M.quest_20612_start_npc.name_key,
                x = M.quest_20612_start_npc.x,
                y = M.quest_20612_start_npc.y,
                z = M.quest_20612_start_npc.z,
                distance = npc_dist,
                range = npc_range,
                mark_20612_start_point_reached = true,
            })
        end
    end

    return action("InteractNpc", "open quest 20612 start npc dialog", {
        quest_id = M.quest_20612_id,
        quest_step = M.questStep(quest),
        stage = M.quest_20612_start_stage,
        interact_id = M.quest_20612_start_npc.interact_id,
        npc_name = M.quest_20612_start_npc.name,
        npc_name_key = M.quest_20612_start_npc.name_key,
        allow_interact_id_fallback = true,
        mark_20612_start_point_reached = true,
    })
end

function M.nextQuest20612RewardAction(state, runtime, opts, quest)
    state = state or {}
    runtime = runtime or {}
    opts = opts or {}
    quest = quest or M.findQuestById(state.quests, M.quest_20612_id)

    if runtime.completed_20612_reward_dialog == true then
        return action("Idle", "quest 20612 reward npc already completed", {
            quest_id = M.quest_20612_id,
            quest_step = M.questStep(quest),
            stage = M.quest_20612_reward_stage,
        })
    end

    local dialog = state.dialog
    if M.isQuest20612RewardNpcDialog(dialog) then
        local type_text = tostring(dialog.type_text or "")
        local step = M.quest_20612_reward_dialog_steps[type_text]
        if step then
            return action(step.action, step.reason, {
                quest_id = M.quest_20612_id,
                quest_step = M.questStep(quest),
                expected_content_id = step.content_id,
                content_id = number(dialog.dialog_content_id),
                type_text = type_text,
                click_x = opts.dialog_click_x or 25,
                click_y = step.click_y,
                click_y_tolerance = step.click_y_tolerance,
                max_steps = step.max_steps,
                delay_ms = step.delay_ms,
                interact_id = M.quest_20612_reward_npc.interact_id,
                npc_name = M.quest_20612_reward_npc.name,
                npc_name_key = M.quest_20612_reward_npc.name_key,
                stage = M.quest_20612_reward_stage,
            })
        end

        return action("DumpDialog", "unknown quest 20612 reward npc dialog stage", {
            quest_id = M.quest_20612_id,
            quest_step = M.questStep(quest),
            type_text = type_text,
            content_id = number(dialog.dialog_content_id),
            npc_dialog_id = number(dialog.npc_dialog_id),
            interact_id = M.quest_20612_reward_npc.interact_id,
            npc_name = M.quest_20612_reward_npc.name,
            npc_name_key = M.quest_20612_reward_npc.name_key,
            stage = M.quest_20612_reward_stage,
        })
    end

    if type(dialog) == "table" then
        return action("DumpDialog", "different npc dialog is already open before quest 20612 reward npc", {
            quest_id = M.quest_20612_id,
            quest_step = M.questStep(quest),
            type_text = tostring(dialog.type_text or ""),
            content_id = number(dialog.dialog_content_id),
            npc_dialog_id = number(dialog.npc_dialog_id),
            interact_id = M.quest_20612_reward_npc.interact_id,
            npc_name = M.quest_20612_reward_npc.name,
            npc_name_key = M.quest_20612_reward_npc.name_key,
            stage = M.quest_20612_reward_stage,
        })
    end

    local char = state.char
    if type(char) ~= "table" then
        return action("ReadState", "character unavailable", { quest_id = M.quest_20612_id })
    end

    local current_big_map = number(state.big_map_id)
    if current_big_map > 0 and current_big_map ~= M.big_map_id then
        return action("Idle", "quest 20612 reward npc wrong map", {
            quest_id = M.quest_20612_id,
            quest_step = M.questStep(quest),
            big_map_id = current_big_map,
            expected_big_map_id = M.big_map_id,
            stage = M.quest_20612_reward_stage,
        })
    end

    local range = number(opts.npc_range)
    if range <= 0 then
        range = 4
    end
    local dist = M.distanceToQuest20612RewardNpc(char)
    if dist > range then
        return action("NavigateToNpc", "move to quest 20612 reward npc", {
            quest_id = M.quest_20612_id,
            quest_step = M.questStep(quest),
            stage = M.quest_20612_reward_stage,
            interact_id = M.quest_20612_reward_npc.interact_id,
            npc_name = M.quest_20612_reward_npc.name,
            npc_name_key = M.quest_20612_reward_npc.name_key,
            x = M.quest_20612_reward_npc.x,
            y = M.quest_20612_reward_npc.y,
            z = M.quest_20612_reward_npc.z,
            distance = dist,
            range = range,
        })
    end

    return action("InteractNpc", "open quest 20612 reward npc dialog", {
        quest_id = M.quest_20612_id,
        quest_step = M.questStep(quest),
        stage = M.quest_20612_reward_stage,
        interact_id = M.quest_20612_reward_npc.interact_id,
        npc_name = M.quest_20612_reward_npc.name,
        npc_name_key = M.quest_20612_reward_npc.name_key,
        allow_interact_id_fallback = true,
    })
end

function M.nextQuest20612TaskTeleportAction(state, runtime, opts, quest)
    state = state or {}
    runtime = runtime or {}
    opts = opts or {}
    quest = quest or M.findQuestById(state.quests, M.quest_20612_id)
    local teleport_quest = quest
    if not M.isQuestActive(teleport_quest) then
        local level_quest = state.level_blocked_quest or M.findLevelBlockedQuest(state.quests)
        if M.isQuestLevelBlocked(level_quest)
            and quest_id(level_quest) > M.quest_20612_id then
            teleport_quest = level_quest
        end
    end
    local teleport_qid = quest_id(teleport_quest)
    if teleport_qid <= 0 then
        teleport_qid = M.quest_20612_id
    end

    if runtime.completed_20612_task_teleport == true then
        return action("Idle", "quest 20612 task teleport already completed", {
            quest_id = teleport_qid,
            quest_step = M.questStep(teleport_quest),
            stage = M.quest_20612_teleport_stage,
        })
    end

    return M.nextCurrentQuestTeleportAction(state, runtime, teleport_quest, "post quest 20612 current tracker task teleport", {
        quest_id = teleport_qid,
        quest_step = M.questStep(teleport_quest),
        after_quest_id = M.quest_20612_id,
        stage = M.quest_20612_teleport_stage,
        wait_teleport = true,
    })
end

function M.nextQuest20612LevelGateAction(state, runtime, opts, quest)
    state = state or {}
    runtime = runtime or {}
    opts = opts or {}
    quest = quest or M.findQuestById(state.quests, M.quest_20612_id)

    if type(state.char) ~= "table" then
        return action("ReadState", "character unavailable", { quest_id = M.quest_20612_id })
    end

    local required_level = M.quest20612RequiredLevel(quest)
    local char_level = number(state.char and state.char.level)
    if char_level <= 0 then
        return action("ReadState", "character level unavailable", { quest_id = M.quest_20612_id })
    end

    if char_level < required_level then
        local active_stage = tostring(runtime.active_20611_grind_stage or "")
        if runtime.active_20611_grind == true
            and active_stage == M.quest_20612_level_grind_stage
            and number(runtime.level_grind_quest_id) == M.quest_20612_id then
            return action("WaitLevelGrind", "quest 20612 level grind running", {
                quest_id = M.quest_20612_id,
                quest_step = M.questStep(quest),
                required_level = required_level,
                char_level = char_level,
                stage = M.quest_20612_level_grind_stage,
            })
        end

        local anchor = anchor_from_char(state.char)
        return action("StartStationaryGrind", "start quest 20612 level grind", {
            quest_id = M.quest_20612_id,
            quest_step = M.questStep(quest),
            required_level = required_level,
            char_level = char_level,
            until_level = required_level,
            stage = M.quest_20612_level_grind_stage,
            x = anchor.x,
            y = anchor.y,
            z = anchor.z,
        })
    end

    return M.nextQuest20612StartAction(state, runtime, opts, quest)
end

function M.nextPostQuest20612Level14GrindAction(state, runtime, opts, quest)
    state = state or {}
    runtime = runtime or {}
    opts = opts or {}
    quest = quest or M.findQuestById(state.quests, M.quest_20613_id)

    if type(state.dialog) == "table" then
        return action("Idle", "waiting quest 20612 reward dialog close before level 14 grind", {
            quest_id = M.quest_20613_id,
            quest_step = M.questStep(quest),
            stage = M.quest_20613_level_grind_stage,
        })
    end

    local char = state.char
    if type(char) ~= "table" then
        return action("ReadState", "character unavailable", { quest_id = M.quest_20613_id })
    end

    local required_level = number(opts.post_20612_level14_required_level)
    if required_level <= 0 then
        required_level = M.post_20612_level14_required_level
    end
    local char_level = number(char.level)
    if char_level <= 0 then
        return action("ReadState", "character level unavailable", { quest_id = M.quest_20613_id })
    end

    if char_level >= required_level then
        return action("Idle", "post quest 20612 level 14 grind complete; wait next instruction", {
            quest_id = M.quest_20613_id,
            quest_step = M.questStep(quest),
            required_level = required_level,
            char_level = char_level,
            stage = M.quest_20613_level_grind_stage,
        })
    end

    local current_big_map = number(state.big_map_id)
    if current_big_map > 0 and current_big_map ~= M.big_map_id then
        return action("Idle", "post quest 20612 level 14 grind wrong map", {
            quest_id = M.quest_20613_id,
            quest_step = M.questStep(quest),
            big_map_id = current_big_map,
            expected_big_map_id = M.big_map_id,
            required_level = required_level,
            char_level = char_level,
            stage = M.quest_20613_level_grind_stage,
        })
    end

    local active_stage = tostring(runtime.active_20611_grind_stage or "")
    if runtime.active_20611_grind == true
        and active_stage == M.quest_20613_level_grind_stage
        and number(runtime.level_grind_quest_id) == M.quest_20613_id then
        return action("WaitLevelGrind", "post quest 20612 level 14 grind running", {
            quest_id = M.quest_20613_id,
            quest_step = M.questStep(quest),
            required_level = required_level,
            char_level = char_level,
            stage = M.quest_20613_level_grind_stage,
        })
    end

    local range = number(opts.post_20612_level14_grind_point_range)
    if range <= 0 then
        range = number(opts.grind_point_range)
    end
    if range <= 0 then
        range = 10
    end
    local point = M.post_20612_level14_grind_point
    local dist = M.distanceToPost20612Level14GrindPoint(char)
    if dist > range then
        return action("NavigateToGrindPoint", "move to post quest 20612 level 14 grind point", {
            quest_id = M.quest_20613_id,
            quest_step = M.questStep(quest),
            required_level = required_level,
            char_level = char_level,
            stage = M.quest_20613_level_grind_stage,
            x = point.x,
            y = point.y,
            z = point.z,
            distance = dist,
            range = range,
        })
    end

    return action("StartStationaryGrind", "start post quest 20612 level 14 grind", {
        quest_id = M.quest_20613_id,
        quest_step = M.questStep(quest),
        required_level = required_level,
        char_level = char_level,
        until_level = required_level,
        stage = M.quest_20613_level_grind_stage,
        x = point.x,
        y = point.y,
        z = point.z,
    })
end

function M.nextAction(state, runtime, opts)
    state = state or {}
    runtime = runtime or {}
    opts = opts or {}

    local teleport_stage = tostring(runtime.teleport_stage or "")
    if runtime.waiting_teleport == true
        and (teleport_stage == M.level_move_stage
            or teleport_stage == M.target_teleport_stage
            or teleport_stage == M.hotspot_teleport_stage
            or teleport_stage == M.quest_20612_teleport_stage) then
        local waiting_qid = number(runtime.teleport_quest_id)
        if waiting_qid <= 0 then
            waiting_qid = teleport_stage == M.quest_20612_teleport_stage and M.quest_20612_id or M.quest_id
        end
        local detected, reason = M.teleportDetected(state, runtime, opts)
        if detected then
            if teleport_stage == M.hotspot_teleport_stage then
                return action("CompleteMapNodeTeleport", reason, {
                    quest_id = M.quest_id,
                    stage = teleport_stage,
                })
            end
            return action("CompleteQuestTeleport", reason, {
                quest_id = waiting_qid,
                stage = teleport_stage,
            })
        end
        return action("WaitPositionChanged", reason, {
            quest_id = waiting_qid,
            stage = teleport_stage,
            min_distance = opts.teleport_min_distance or 20,
        })
    end

    local quest_20611 = M.findQuestById(state.quests, M.quest_id)
    if M.isMissionNpcDialog(state.dialog)
        and runtime.completed_20611_mission_dialog ~= true then
        return M.nextMissionNpcAction(state, runtime, opts, quest_20611)
    end
    if M.isTargetNpcDialog(state.dialog)
        and runtime.completed_20611_target_dialog ~= true then
        return M.nextTargetNpcAction(state, runtime, opts, quest_20611)
    end
    if M.isHotspotRewardNpcDialog(state.dialog)
        and runtime.completed_20611_hotspot_reward ~= true then
        return M.nextHotspotRewardAction(state, runtime, opts, quest_20611)
    end

    local quest_20612 = M.findQuestById(state.quests, M.quest_20612_id)
    local sequential_quest = M.findSequentialQuest(state.quests, runtime)
    local sequential_qid = quest_id(sequential_quest)
    local allow_quest_20612_flow = sequential_qid <= 0
        or sequential_qid >= M.quest_20612_id
    if M.isQuest20612StartNpcDialog(state.dialog)
        and runtime.completed_20612_start_dialog ~= true then
        if not allow_quest_20612_flow then
            return action("Idle", "quest 20612 dialog blocked by earlier yellow mission", {
                quest_id = sequential_qid,
                blocked_quest_id = M.quest_20612_id,
                blocked_stage = M.quest_20612_start_stage,
            })
        end
        return M.nextQuest20612StartAction(state, runtime, opts, quest_20612)
    end
    if M.isQuest20612RewardNpcDialog(state.dialog)
        and runtime.completed_20612_reward_dialog ~= true then
        if not allow_quest_20612_flow then
            return action("Idle", "quest 20612 reward dialog blocked by earlier yellow mission", {
                quest_id = sequential_qid,
                blocked_quest_id = M.quest_20612_id,
                blocked_stage = M.quest_20612_reward_stage,
            })
        end
        return M.nextQuest20612RewardAction(state, runtime, opts, quest_20612)
    end

    local hotspot_reward_pending = runtime.completed_20611_hotspot_reward ~= true
        and (runtime.completed_20611_hotspot_teleport == true
            or (M.isQuestDone(quest_20611) and M.questStep(quest_20611) == 3))
    if hotspot_reward_pending then
        return M.nextHotspotRewardAction(state, runtime, opts, quest_20611)
    end

    local quest_20612_active_start = M.isQuestActive(quest_20612)
        and M.questStep(quest_20612) == 0
    local quest_20612_level_ready = M.isQuestLevelBlocked(quest_20612)
        and (runtime.completed_20611_hotspot_reward == true
            or not M.isQuestKnown(quest_20611))
    local quest_20612_task_teleport_ready = M.isQuestActive(quest_20612)
        and (runtime.completed_20612_start_dialog == true
            or M.questStep(quest_20612) == 1)
    local level_quest_after_20612 = state.level_blocked_quest or M.findLevelBlockedQuest(state.quests)
    local quest_20612_reward_dialog_open = M.isQuest20612RewardNpcDialog(state.dialog)
    local quest_20612_reward_npc_near = M.isNearQuest20612RewardNpc(state, opts)
    local quest_20612_reward_ready = M.isQuestDone(quest_20612)
        and runtime.completed_20612_reward_dialog ~= true
        and (runtime.completed_20612_task_teleport == true
            or quest_20612_reward_dialog_open
            or quest_20612_reward_npc_near)
    local quest_20612_done_task_teleport_ready = M.isQuestDone(quest_20612)
        and runtime.completed_20612_task_teleport ~= true
        and runtime.completed_20612_reward_dialog ~= true
        and M.isQuestLevelBlocked(level_quest_after_20612)
        and quest_id(level_quest_after_20612) > M.quest_20612_id
    if allow_quest_20612_flow and (quest_20612_reward_dialog_open or quest_20612_reward_ready) then
        return M.nextQuest20612RewardAction(state, runtime, opts, quest_20612)
    end
    if allow_quest_20612_flow and (quest_20612_active_start or quest_20612_level_ready) then
        return M.nextQuest20612LevelGateAction(state, runtime, opts, quest_20612)
    end
    if allow_quest_20612_flow and quest_20612_task_teleport_ready then
        return M.nextQuest20612TaskTeleportAction(state, runtime, opts, quest_20612)
    end
    if allow_quest_20612_flow and quest_20612_done_task_teleport_ready then
        return M.nextQuest20612TaskTeleportAction(state, runtime, opts, level_quest_after_20612)
    end

    local post_20612_level14_ready = runtime.completed_20612_reward_dialog == true
        or (M.isQuestLevelBlocked(level_quest_after_20612)
            and quest_id(level_quest_after_20612) == M.quest_20613_id
            and not M.isQuestKnown(quest_20612))
    if post_20612_level14_ready then
        local quest_20613 = M.findQuestById(state.quests, M.quest_20613_id)
        return M.nextPostQuest20612Level14GrindAction(state, runtime, opts, quest_20613 or level_quest_after_20612)
    end

    if runtime.completed_20611_hotspot_reward == true
        or (M.isQuestDone(quest_20611) and M.questStep(quest_20611) == 3) then
        return M.nextHotspotRewardAction(state, runtime, opts, quest_20611)
    end

    local remote_reward_quest = state.remote_reward_quest or M.findRemoteRewardQuest(state.quests)
    local allow_remote_reward_flow = sequential_qid <= 0
    if M.isRemoteRewardDialog(state.dialog) then
        if not allow_remote_reward_flow then
            return action("Idle", "remote reward dialog blocked by earlier yellow mission", {
                quest_id = sequential_qid,
                blocked_quest_id = quest_id(remote_reward_quest),
                stage = "quest_20611_remote_reward",
            })
        end
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
    if allow_remote_reward_flow and M.isRemoteRewardReady(remote_reward_quest) then
        local ready_qid = quest_id(remote_reward_quest)
        return action("OpenQuestSubmit", "open blue grind remote reward", {
            quest_id = ready_qid,
            quest_step = M.questStep(remote_reward_quest),
            stage = "quest_20611_remote_reward",
        })
    end

    local quest = nil
    if allow_remote_reward_flow
        and M.isRemoteGrindActive(remote_reward_quest)
        and runtime.completed_20611_grind ~= true then
        quest = remote_reward_quest
    end
    local qid = quest_id(quest)
    if qid <= 0 then
        qid = M.quest_id
    end

    if not M.isRemoteGrindActive(quest) then
        local active_quest = nil
        if M.isQuestActive(sequential_quest) then
            active_quest = sequential_quest
        elseif sequential_qid <= 0 then
            if M.isQuestActive(state.quest) then
                active_quest = state.quest
            end
        end
        if not active_quest then
            active_quest = M.findActiveQuest(state.quests)
        end
        if active_quest and sequential_qid > 0
            and is_earlier_quest(sequential_quest, active_quest) then
            active_quest = nil
        end
        if M.isQuestActive(active_quest) then
            local active_qid = quest_id(active_quest)
            local active_step = M.questStep(active_quest)
            if active_qid == M.quest_id and active_step == 1 then
                return M.nextObeliskAction(state, runtime, opts, active_quest)
            end
            if active_qid == M.quest_id and active_step == 2 then
                return M.nextTargetStepAction(state, runtime, opts, active_quest)
            end
            if active_qid == M.quest_id and active_step == 3 then
                return M.nextHotspotTeleportAction(state, runtime, opts, active_quest)
            end
            if active_qid == M.quest_id and active_step > 1 then
                return action("Idle", "quest 20611 next step is not recorded yet", {
                    quest_id = active_qid,
                    quest_step = active_step,
                })
            end
            if active_qid > M.quest_id then
                return action("Idle", "active yellow mission next step is not recorded yet", {
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
                    return M.nextCurrentQuestTeleportAction(state, runtime, active_quest, "active yellow mission level reached; immediate move", {
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
                return M.nextCurrentQuestTeleportAction(state, runtime, tracked_quest, "tracked yellow mission level reached; immediate move", {
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

        local level_quest = level_quest_after_20612 or state.level_blocked_quest or M.findLevelBlockedQuest(state.quests)
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
                return M.nextCurrentQuestTeleportAction(state, runtime, level_quest, "yellow mission level reached; immediate move", {
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
            return M.nextCurrentQuestTeleportAction(state, runtime, level_quest, "yellow mission immediate move", {
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
