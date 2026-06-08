local M = {}

function M.decide(state)
    state = state or {}
    local last_obj = tonumber(state.last_killed_obj) or 0
    if last_obj <= 0 then
        return {
            action = "none",
            reason = "no-last-kill",
            remain = 0,
        }
    end

    local now = tonumber(state.now) or 0
    local until_time = tonumber(state.post_kill_until) or 0
    local remain = math.max(0, until_time - now)
    if until_time <= now then
        return {
            action = "expired",
            reason = "window-expired",
            remain = 0,
        }
    end

    if state.loot_target ~= nil then
        return {
            action = "open-loot",
            reason = "loot-ready",
            remain = remain,
        }
    end

    return {
        action = "wait",
        reason = tostring(state.reject_reason or "pending"),
        remain = remain,
    }
end

return M
