local M = {}

function M.shallow_copy(t)
    local out = {}
    for k, v in pairs(t or {}) do out[k] = v end
    return out
end

function M.count(t)
    local n = 0
    for _ in pairs(t or {}) do n = n + 1 end
    return n
end

return M
