local M = {}

function M.get_current_quest(bb)
    return bb.quest.active and bb.quest.active[bb.quest.current_quest_id] or nil
end

function M.get_current_objective(bb)
    local quest = M.get_current_quest(bb)
    if not quest then return nil end
    return quest.objectives and quest.objectives[bb.quest.current_objective_index or 1] or nil
end

function M.is_objective_complete(objective, bb)
    if not objective then return false end
    if objective.type == "wait" then return true end
    if objective.type == "level" then return (tonumber(bb.actor.level) or 1) >= (tonumber(objective.level) or 1) end
    return false
end

return M
