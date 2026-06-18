local M = {}

function M.distance2(a, b)
    if not a or not b then return math.huge end
    local dx = (tonumber(a.x) or 0) - (tonumber(b.x) or 0)
    local y1 = tonumber(a.y) or 0
    local y2 = tonumber(b.y) or 0
    return dx * dx + (y1 - y2) * (y1 - y2)
end

function M.is_stuck(bb)
    return bb.navigation.is_stuck == true or (tonumber(bb.navigation.stuck_count) or 0) > 0
end

return M
