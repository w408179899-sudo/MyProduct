local M = {}

local DEFAULT_INTERVAL_MS = 1000
local MIN_INTERVAL_MS = 250
local MAX_INTERVAL_MS = 3000

function M.normalize_interval_ms(value)
    local n = tonumber(value)
    if not n then
        n = DEFAULT_INTERVAL_MS
    end
    n = math.floor(n)
    if n < MIN_INTERVAL_MS then
        return MIN_INTERVAL_MS
    end
    if n > MAX_INTERVAL_MS then
        return MAX_INTERVAL_MS
    end
    return n
end

function M.from_config(config)
    config = config or {}
    return {
        interval_ms = M.normalize_interval_ms(config.attack_key_repeat_interval_ms),
    }
end

function M.should_press(args)
    args = args or {}
    local target_obj = tonumber(args.target_obj) or 0
    if target_obj <= 0 then
        return false, "invalid-target"
    end

    local last_obj = tonumber(args.last_attack_key_obj) or 0
    if last_obj ~= target_obj then
        return true, "new-target"
    end

    local last_at = tonumber(args.last_attack_key_at) or 0
    if last_at <= 0 then
        return true, "no-previous-key"
    end

    local now = tonumber(args.now) or 0
    local interval_ms = M.normalize_interval_ms(args.interval_ms)
    local elapsed_ms = math.max(0, (now - last_at) * 1000)
    if elapsed_ms >= interval_ms then
        return true, "interval"
    end

    return false, "waiting"
end

return M
