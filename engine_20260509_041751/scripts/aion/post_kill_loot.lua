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
    local check_at = tonumber(state.post_kill_check_at or state.post_kill_until) or now
    local remain = math.max(0, check_at - now)
    if remain > 0 then
        return {
            action = "delay",
            reason = "check-delay",
            remain = remain,
        }
    end

    if state.loot_target ~= nil then
        return {
            action = "open-loot",
            reason = "loot-ready",
            remain = 0,
        }
    end

    return {
        action = "skip",
        reason = tostring(state.reject_reason or "not-lootable"),
        remain = 0,
    }
end

return M
