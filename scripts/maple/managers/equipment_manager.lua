local M = {}

function M.score_item(item)
    if not item then return 0 end
    return (tonumber(item.attack) or 0) + (tonumber(item.magic) or 0) + (tonumber(item.main_stat) or 0)
end

function M.should_replace(current_item, candidate_item, delta)
    delta = tonumber(delta) or 0
    return M.score_item(candidate_item) - M.score_item(current_item) >= delta
end

function M.get_best_candidate(bb)
    local best, best_score = nil, -math.huge
    for _, item in ipairs(bb.equipment.candidates or {}) do
        local score = M.score_item(item)
        if score > best_score then best, best_score = item, score end
    end
    return best
end

return M
