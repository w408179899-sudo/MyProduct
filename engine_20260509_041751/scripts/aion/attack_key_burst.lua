local M = {}

local DEFAULT_COUNT = 5
local DEFAULT_INTERVAL_MS = 200

function M.normalize_count(value)
    local n = tonumber(value)
    if not n then
        n = DEFAULT_COUNT
    end
    n = math.floor(n)
    if n < 1 then
        return 1
    end
    if n > 10 then
        return 10
    end
    return n
end

function M.normalize_interval_ms(value)
    local n = tonumber(value)
    if not n then
        n = DEFAULT_INTERVAL_MS
    end
    n = math.floor(n)
    if n < 50 then
        return 50
    end
    if n > 1000 then
        return 1000
    end
    return n
end

function M.from_config(config)
    config = config or {}
    return {
        count = M.normalize_count(config.attack_key_burst_count),
        interval_ms = M.normalize_interval_ms(config.attack_key_burst_interval_ms),
    }
end

return M
