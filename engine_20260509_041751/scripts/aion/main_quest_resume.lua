local M = {}

M.remote_reward_quest_id = 24340
M.remote_reward_quest_ids = { 24340, 24341 }
M.main_quest_ids = { 20611, 20612, 20613, 20614, 20615 }

local function number(value)
    return tonumber(value) or 0
end

local function quest_id(quest)
    return number(quest and quest.id)
end

local function status_code(quest)
    return number(quest and quest.status_code)
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

function M.plan(snapshot)
    snapshot = snapshot or {}
    local char = type(snapshot.char) == "table" and snapshot.char or {}
    local quests = type(snapshot.quests) == "table" and snapshot.quests or {}
    local dialog = snapshot.dialog
    local level = number(char.level)

    local q20590 = M.findQuest(quests, 20590)
    local q20610 = M.findQuest(quests, 20610)
    local remote_reward = M.findRemoteRewardQuest(quests)
    local level_blocked = M.findLevelBlockedQuest(quests)
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
    elseif status_code(q20610) == 4 then
        flags.completed_20590_reward = true
        flags.completed_20610_start_dialog = true
        stage = "20610_reward"
        reason = "quest 20610 is done and needs reward flow"
    elseif status_code(q20610) == 3 then
        flags.completed_20590_reward = true
        stage = "20610_active"
        reason = "quest 20610 is active"
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
        stage = "20611_level_blocked"
        reason = "yellow mission " .. tostring(quest_id(level_blocked)) .. " is level blocked"
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
        level_blocked_quest_id = quest_id(level_blocked),
        level_blocked_status = status_code(level_blocked),
        remote_reward_quest_id = remote_reward_id,
        remote_reward_status = status_code(remote_reward),
    }
end

return M
