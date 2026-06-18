local M = {}

function M.resolve(objective)
    if not objective then return { action = "Idle", params = {} } end
    if objective.type == "travel" then return { action = "NavigateTo", params = { destination = objective.destination } } end
    if objective.type == "talk" then return { action = "InteractNpc", params = { npc_id = objective.npc_id } } end
    if objective.type == "wait" then return { action = "Wait", params = { seconds = objective.seconds or 1 } } end
    return { action = "Idle", params = {} }
end

return M
