local core = require("aion.core")
local data = core.data

local M = {
    STATUS_DOING = 3,
    STATUS_DONE = 4,
    STATUS_LEVEL_BLOCKED = 6,
}

function M.list()
    local ok, list, err = core.first("AionData.GetQuestList", data.GetQuestList)
    if not ok then
        return false, nil, err
    end
    return true, list or {}, nil
end

function M.findById(id)
    local ok, list, err = M.list()
    if not ok then
        return false, nil, err
    end

    for _, quest in ipairs(list) do
        if quest.id == id then
            return true, quest, nil
        end
    end
    return true, nil, nil
end

function M.findByName(name)
    local ok, list, err = M.list()
    if not ok then
        return false, nil, err
    end

    for _, quest in ipairs(list) do
        if quest.name == name then
            return true, quest, nil
        end
    end
    return true, nil, nil
end

function M.completed()
    local ok, list, err = M.list()
    if not ok then
        return false, nil, err
    end

    local out = {}
    for _, quest in ipairs(list) do
        if quest.status_code == M.STATUS_DONE then
            out[#out + 1] = quest
        end
    end
    return true, out, nil
end

function M.openSubmit(questId)
    return core.first("AionData.OpenQuestSubmit", data.OpenQuestSubmit, questId)
end

function M.questTeleportId(questId)
    return core.first("AionData.GetQuestTeleportId", data.GetQuestTeleportId, questId)
end

function M.questTeleport(questId, teleportId)
    return core.first("AionData.QuestTeleport", data.QuestTeleport, questId, teleportId)
end

function M.taskTeleportPrice(questId)
    return core.first("AionData.GetTaskTeleportPrice", data.GetTaskTeleportPrice, questId)
end

function M.taskTeleport(questId)
    return core.first("AionData.TaskTeleport", data.TaskTeleport, questId)
end

function M.achievementList(typeId)
    return core.first("AionData.GetAchievementTaskList", data.GetAchievementTaskList, typeId)
end

function M.achievementObject(taskId)
    return core.first("AionData.GetAchievementTaskObject", data.GetAchievementTaskObject, taskId)
end

return M
