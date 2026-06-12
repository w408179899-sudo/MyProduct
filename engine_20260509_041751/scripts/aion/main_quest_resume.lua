local M = {}

M.remote_reward_quest_id = 24340
M.remote_reward_quest_ids = { 24340, 24341 }
M.main_quest_ids = { 20611, 20612, 20613, 20614, 20615 }
M.big_map_id = 220010000
M.quest_20612_reward_npc = {
    interact_id = 2147495609,
    x = 1050.70,
    y = 2201.12,
    z = 262.81,
}
M.quest_20614_reward_npc = {
    interact_id = 2147511075,
    x = 602.85,
    y = 1480.65,
    z = 299.79,
}
M.quest_20615_target_npc = {
    interact_id = 2147520815,
    x = 589.35,
    y = 2450.16,
    z = 278.38,
}

local function number(value)
    return tonumber(value) or 0
end

local function quest_id(quest)
    return number(quest and quest.id)
end

local function status_code(quest)
    return number(quest and quest.status_code)
end

local function quest_step(quest)
    return number(quest and quest.req_count)
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

local function mark_after_20610(flags)
    flags.completed_20590_reward = true
    flags.completed_20610_start_dialog = true
    flags.completed_20610_task_teleport = true
    flags.completed_20610_reward = true
    flags.active_20611_grind = false
    flags.active_20611_grind_stage = ""
    flags.level_grind_quest_id = 0
    flags.level_grind_required_level = 0
end

local function mark_after_20611(flags)
    mark_after_20610(flags)
    flags.completed_20611_grind = false
    flags.completed_20611_hotspot_reward = true
    flags.reached_20612_start_point = false
    flags.completed_20612_start_dialog = false
    flags.completed_20612_task_teleport = false
    flags.completed_20612_reward_dialog = false
end

local function mark_after_20613(flags)
    mark_after_20611(flags)
    flags.reached_20612_start_point = true
    flags.completed_20612_start_dialog = true
    flags.completed_20612_task_teleport = true
    flags.completed_20612_reward_dialog = true
    flags.completed_20613_task_teleport = true
    flags.completed_20613_start_dialog = true
    flags.completed_20613_after_start_teleport = true
    flags.completed_20613_after_start_reward_dialog = true
    flags.completed_20614_task_teleport = false
    flags.completed_20614_start_dialog = false
    flags.completed_20614_after_start_teleport = false
    flags.completed_20614_reward_dialog = false
end

local function mark_after_20614(flags)
    mark_after_20613(flags)
    flags.completed_20614_task_teleport = true
    flags.completed_20614_start_dialog = true
    flags.completed_20614_after_start_teleport = true
    flags.completed_20614_reward_dialog = true
    flags.completed_20615_task_teleport = false
    flags.completed_20615_target_dialog = false
    flags.completed_20615_big_map_teleport = false
end

function M.findQuest(quests, id)
    id = number(id)
    for _, quest in ipairs(quests or {}) do
        if quest_id(quest) == id then
            return quest
        end
    end
    return nil
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

function M.isMainQuestId(id)
    id = number(id)
    for _, supported_id in ipairs(M.main_quest_ids or {}) do
        if id == number(supported_id) then
            return true
        end
    end
    return false
end

function M.findLevelBlockedQuest(quests)
    local best = nil
    for _, quest in ipairs(quests or {}) do
        if M.isMainQuestId(quest_id(quest))
            and status_code(quest) == 6 then
            if not best then
                best = quest
            else
                local best_seq = number(best.seq)
                local seq = number(quest.seq)
                local best_level = number(best.lv_num)
                local level = number(quest.lv_num)
                if (seq > 0 and (best_seq <= 0 or seq < best_seq))
                    or (seq == best_seq and level > 0 and (best_level <= 0 or level < best_level)) then
                    best = quest
                end
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
            if status_code(quest) == 4 then
                ready = ready or quest
            elseif status_code(quest) == 3 then
                active = active or quest
            else
                fallback = fallback or quest
            end
        end
    end
    return ready or active or fallback
end

function M.isRemoteRewardDialog(dialog)
    return type(dialog) == "table"
        and M.isRemoteRewardQuestId(dialog.quest_id)
        and tostring(dialog.type_text or "") == "select_quest_reward_remote"
end

function M.isQuest20612RewardNpcDialog(dialog)
    return type(dialog) == "table"
        and number(dialog.npc_dialog_id) == M.quest_20612_reward_npc.interact_id
end

function M.isNearQuest20612RewardNpc(snapshot)
    snapshot = snapshot or {}
    if type(snapshot.char) ~= "table" then
        return false
    end
    local current_big_map = number(snapshot.big_map_id)
    if current_big_map > 0 and current_big_map ~= M.big_map_id then
        return false
    end
    return distance3(snapshot.char, M.quest_20612_reward_npc) <= 4
end

function M.isQuest20614RewardNpcDialog(dialog)
    return type(dialog) == "table"
        and number(dialog.npc_dialog_id) == M.quest_20614_reward_npc.interact_id
        and (number(dialog.quest_id) == 0 or number(dialog.quest_id) == 20614)
end

function M.isNearQuest20614RewardNpc(snapshot)
    snapshot = snapshot or {}
    if type(snapshot.char) ~= "table" then
        return false
    end
    local current_big_map = number(snapshot.big_map_id)
    if current_big_map > 0 and current_big_map ~= M.big_map_id then
        return false
    end
    return distance3(snapshot.char, M.quest_20614_reward_npc) <= 4
end

function M.isQuest20615TargetNpcDialog(dialog)
    return type(dialog) == "table"
        and number(dialog.npc_dialog_id) == M.quest_20615_target_npc.interact_id
        and (number(dialog.quest_id) == 0 or number(dialog.quest_id) == 20615)
end

function M.isNearQuest20615TargetNpc(snapshot)
    snapshot = snapshot or {}
    if type(snapshot.char) ~= "table" then
        return false
    end
    local current_big_map = number(snapshot.big_map_id)
    if current_big_map > 0 and current_big_map ~= M.big_map_id then
        return false
    end
    return distance3(snapshot.char, M.quest_20615_target_npc) <= 4
end

function M.plan(snapshot)
    snapshot = snapshot or {}
    local char = type(snapshot.char) == "table" and snapshot.char or {}
    local quests = type(snapshot.quests) == "table" and snapshot.quests or {}
    local dialog = snapshot.dialog
    local level = number(char.level)

    local q20590 = M.findQuest(quests, 20590)
    local q20610 = M.findQuest(quests, 20610)
    local q20611 = M.findQuest(quests, 20611)
    local q20612 = M.findQuest(quests, 20612)
    local q20614 = M.findQuest(quests, 20614)
    local q20615 = M.findQuest(quests, 20615)
    local remote_reward = M.findRemoteRewardQuest(quests)
    local level_blocked = M.findLevelBlockedQuest(quests)
    local level_blocked_id = quest_id(level_blocked)
    local remote_reward_id = quest_id(remote_reward)
    if remote_reward_id <= 0 and M.isRemoteRewardDialog(dialog) then
        remote_reward_id = number(dialog.quest_id)
    end

    local flags = {}
    local stage = "unknown"
    local reason = "no recognized quest snapshot"

    if status_code(q20590) == 3 then
        stage = "20590"
        reason = "quest 20590 is active"
    elseif status_code(q20610) == 4 then
        flags.completed_20590_reward = true
        flags.completed_20610_start_dialog = true
        stage = "20610_reward"
        reason = "quest 20610 is done and needs reward flow"
    elseif status_code(q20610) == 3 then
        flags.completed_20590_reward = true
        stage = "20610_active"
        reason = "quest 20610 is active"
    elseif status_code(q20611) == 4 and quest_step(q20611) == 3 then
        mark_after_20610(flags)
        flags.completed_20611_hotspot_teleport = true
        flags.completed_20611_hotspot_reward = false
        stage = "20611_hotspot_reward"
        reason = "quest 20611 is ready for hotspot reward"
    elseif status_code(q20611) == 3 then
        mark_after_20610(flags)
        stage = "20611_active"
        reason = "quest 20611 is active"
    elseif status_code(level_blocked) == 6 and level_blocked_id == 20611 then
        flags.completed_20590_reward = true
        flags.completed_20610_start_dialog = true
        flags.completed_20610_task_teleport = true
        flags.completed_20610_reward = true
        flags.active_20611_grind = false
        flags.active_20611_grind_stage = ""
        flags.level_grind_quest_id = 0
        flags.level_grind_required_level = 0
        flags.completed_20611_grind = false
        flags.completed_20611_level_move = false
        flags.level_move_quest_id = 0
        stage = "20611_level_blocked"
        reason = "yellow mission " .. tostring(level_blocked_id) .. " is level blocked"
    elseif status_code(q20612) == 3 then
        mark_after_20611(flags)
        if quest_step(q20612) >= 1 then
            flags.completed_20612_start_dialog = true
            stage = "20612_task_teleport"
            reason = "quest 20612 is active after start dialog"
        else
            stage = "20612_start"
            reason = "quest 20612 is active at start dialog"
        end
    elseif status_code(q20612) == 4
        and status_code(level_blocked) == 6
        and level_blocked_id == 20613 then
        local at_reward_npc = M.isQuest20612RewardNpcDialog(dialog)
            or M.isNearQuest20612RewardNpc(snapshot)
        mark_after_20611(flags)
        flags.completed_20612_start_dialog = true
        flags.completed_20612_task_teleport = at_reward_npc
        flags.completed_20612_reward_dialog = false
        if at_reward_npc then
            stage = "20612_reward"
            reason = "quest 20612 is done and character is at reward npc"
        else
            stage = "20612_task_teleport"
            reason = "quest 20612 is done; task teleport before later level grind"
        end
    elseif status_code(q20614) == 3 then
        mark_after_20613(flags)
        if quest_step(q20614) > 0 then
            flags.completed_20614_task_teleport = true
            flags.completed_20614_start_dialog = true
            stage = "20614_after_start_teleport"
            reason = "quest 20614 is active after start dialog and needs task teleport"
        else
            flags.completed_20614_task_teleport = false
            flags.completed_20614_start_dialog = false
            stage = "20614_active"
            reason = "quest 20614 is active and needs task teleport"
        end
        flags.completed_20614_after_start_teleport = false
    elseif status_code(q20614) == 4 then
        local at_reward_npc = M.isQuest20614RewardNpcDialog(dialog)
            or M.isNearQuest20614RewardNpc(snapshot)
        mark_after_20613(flags)
        flags.completed_20614_task_teleport = true
        flags.completed_20614_start_dialog = true
        flags.completed_20614_after_start_teleport = at_reward_npc
        flags.completed_20614_reward_dialog = false
        if at_reward_npc then
            stage = "20614_reward"
            reason = "quest 20614 is done and character is at reward npc"
        else
            stage = "20614_after_start_teleport"
            reason = "quest 20614 is done after start dialog and still needs task teleport"
        end
    elseif status_code(q20615) == 6 then
        mark_after_20614(flags)
        stage = "20615_level_blocked"
        reason = "quest 20615 is level blocked and needs level 20 grind"
    elseif status_code(q20615) == 3 then
        local at_target_npc = M.isQuest20615TargetNpcDialog(dialog)
            or M.isNearQuest20615TargetNpc(snapshot)
        local current_big_map = number(snapshot.big_map_id)
        local after_big_map_teleport = current_big_map > 0 and current_big_map ~= M.big_map_id
        mark_after_20614(flags)
        flags.completed_20615_task_teleport = at_target_npc
            or quest_step(q20615) > 0
            or after_big_map_teleport
        flags.completed_20615_target_dialog = quest_step(q20615) > 0
            or after_big_map_teleport
        flags.completed_20615_big_map_teleport = after_big_map_teleport
        if after_big_map_teleport then
            stage = "20615_big_map_landed"
            reason = "quest 20615 is active and character is already on another big map"
        elseif quest_step(q20615) > 0 then
            stage = "20615_big_map_teleport"
            reason = "quest 20615 has progressed past target npc and needs big map teleport"
        elseif at_target_npc then
            stage = "20615_target_npc"
            reason = "quest 20615 is active and character is at target npc"
        else
            stage = "20615_active"
            reason = "quest 20615 is active and needs task teleport"
        end
    elseif status_code(q20615) == 4 then
        local current_big_map = number(snapshot.big_map_id)
        local after_big_map_teleport = current_big_map > 0 and current_big_map ~= M.big_map_id
        mark_after_20614(flags)
        flags.completed_20615_task_teleport = true
        flags.completed_20615_target_dialog = true
        flags.completed_20615_big_map_teleport = after_big_map_teleport
        if after_big_map_teleport then
            stage = "20615_big_map_landed"
            reason = "quest 20615 is done and character is already on another big map"
        else
            stage = "20615_big_map_teleport"
            reason = "quest 20615 is done after target dialog and needs big map teleport"
        end
    elseif status_code(q20590) == 4 then
        stage = "20590"
        reason = "quest 20590 is done and needs reward flow"
    elseif status_code(level_blocked) == 6 then
        flags.completed_20590_reward = true
        flags.completed_20610_start_dialog = true
        flags.completed_20610_task_teleport = true
        flags.completed_20610_reward = true
        flags.active_20611_grind = false
        flags.active_20611_grind_stage = ""
        flags.level_grind_quest_id = 0
        flags.level_grind_required_level = 0
        flags.completed_20611_grind = false
        flags.completed_20611_level_move = false
        flags.level_move_quest_id = 0
        if level_blocked_id >= 20615 then
            mark_after_20614(flags)
            stage = tostring(level_blocked_id) .. "_level_blocked"
        elseif level_blocked_id >= 20614 then
            mark_after_20613(flags)
            stage = tostring(level_blocked_id) .. "_level_blocked"
        elseif level_blocked_id == 20612
            and not (status_code(q20611) == 4 and quest_step(q20611) == 3) then
            flags.completed_20611_hotspot_reward = true
            flags.reached_20612_start_point = false
            flags.completed_20612_start_dialog = false
            flags.completed_20612_task_teleport = false
            flags.completed_20612_reward_dialog = false
            stage = "20612_level_blocked"
        else
            stage = "20611_level_blocked"
        end
        reason = "yellow mission " .. tostring(level_blocked_id) .. " is level blocked"
    elseif M.isRemoteRewardDialog(dialog) or status_code(remote_reward) == 4 then
        flags.completed_20590_reward = true
        flags.completed_20610_start_dialog = true
        flags.completed_20610_task_teleport = true
        flags.completed_20610_reward = true
        flags.active_20611_grind = false
        flags.active_20611_grind_stage = ""
        flags.level_grind_quest_id = 0
        flags.level_grind_required_level = 0
        flags.completed_20611_grind = false
        stage = "20611_remote_reward"
        reason = "remote reward quest " .. tostring(remote_reward_id) .. " is ready"
    elseif status_code(remote_reward) == 3 then
        flags.completed_20590_reward = true
        flags.completed_20610_start_dialog = true
        flags.completed_20610_task_teleport = true
        flags.completed_20610_reward = true
        flags.active_20611_grind = false
        flags.active_20611_grind_stage = ""
        flags.level_grind_quest_id = 0
        flags.level_grind_required_level = 0
        flags.completed_20611_grind = false
        stage = "20611_grind_active"
        reason = "blue grind task " .. tostring(remote_reward_id) .. " is active"
    elseif level <= 1 then
        stage = "20590"
        reason = "level <= 1 starts from quest 20590 without later quest evidence"
    end

    return {
        stage = stage,
        reason = reason,
        flags = flags,
        character_name = tostring(char.name or ""),
        level = level,
        big_map_id = number(snapshot.big_map_id),
        quest_count = #quests,
        quest_20590_status = status_code(q20590),
        quest_20610_status = status_code(q20610),
        quest_20611_status = status_code(q20611),
        quest_20611_step = quest_step(q20611),
        quest_20612_status = status_code(q20612),
        quest_20612_step = quest_step(q20612),
        quest_20614_status = status_code(q20614),
        quest_20614_step = quest_step(q20614),
        quest_20615_status = status_code(q20615),
        quest_20615_step = quest_step(q20615),
        level_blocked_quest_id = level_blocked_id,
        level_blocked_status = status_code(level_blocked),
        remote_reward_quest_id = remote_reward_id,
        remote_reward_status = status_code(remote_reward),
    }
end

return M
