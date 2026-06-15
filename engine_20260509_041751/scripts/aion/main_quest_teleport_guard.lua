local M = {}

local function text(value)
    return tostring(value or "")
end

function M.shouldBlockQuestTeleport(runtime, quest_id, stage)
    runtime = runtime or {}

    if runtime.waiting_teleport ~= true then
        return false, ""
    end

    local pending_stage = text(runtime.teleport_stage)
    if pending_stage == "" then
        return false, ""
    end

    local next_stage = text(stage)
    local pending_qid = tonumber(runtime.teleport_quest_id) or 0
    local next_qid = tonumber(quest_id) or 0

    if pending_stage ~= next_stage then
        return true, "pending_stage=" .. pending_stage
    end

    if pending_qid > 0 and next_qid > 0 and pending_qid ~= next_qid then
        return true, "pending_quest_id=" .. tostring(pending_qid)
    end

    return false, ""
end

return M
