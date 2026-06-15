local M = {}

local function number(value)
    return tonumber(value) or 0
end

local function find_quest(quests, quest_id)
    for _, quest in ipairs(quests or {}) do
        if number(quest.id) == quest_id then
            return quest
        end
    end
    return nil
end

local function active_or_done(quest)
    local status = number(quest and quest.status_code)
    return status == 3 or status == 4
end

function M.choose(flags, quests, reward_dialog_open)
    flags = flags or {}

    local quest_20590 = find_quest(quests, 20590)
    local quest_20610 = find_quest(quests, 20610)

    if reward_dialog_open == true then
        return "20590", "quest20590-reward-dialog"
    end

    if flags.completed_20590_reward ~= true and active_or_done(quest_20590) then
        return "20590", "quest20590-current"
    end

    if flags.completed_20610_reward ~= true and active_or_done(quest_20610) then
        return "20610", "quest20610-current"
    end

    return "20611", "main-chain"
end

return M
