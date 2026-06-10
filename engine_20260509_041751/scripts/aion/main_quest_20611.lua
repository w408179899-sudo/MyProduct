local M = {}

M.quest_id = 20611
M.quest_ids = { 20611, 20612, 20613, 20614, 20615 }
M.quest_id_min = 20611
M.quest_id_max = 20699
M.remote_reward_quest_id = 24340
M.remote_reward_quest_ids = { 24340, 24341 }
M.big_map_id = 220010000
M.level_move_stage = "quest_20611_level_move"
M.level_grind_stage = "quest_20611_level_grind"
M.grind_point = {
    x = 194.491,
    y = 2689.982,
    z = 300.625,
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

function M.nextAction(state, runtime, opts)
    state = state or {}
    runtime = runtime or {}
    opts = opts or {}

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
                        wait_teleport = false,
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
                    wait_teleport = false,
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
                    wait_teleport = false,
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
                wait_teleport = false,
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
