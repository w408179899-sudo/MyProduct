local M = {}

function M.can_learn(skill, bb)
    if not skill then return false end
    if bb.skill.learned and bb.skill.learned[skill.id] then return false end
    return (tonumber(bb.actor.level) or 1) >= (tonumber(skill.required_level) or 1)
end

function M.get_learnable_skills(bb)
    local out = {}
    for _, skill in ipairs(bb.skill.available or {}) do
        if M.can_learn(skill, bb) then out[#out + 1] = skill end
    end
    table.sort(out, function(a, b) return (a.priority or 0) > (b.priority or 0) end)
    return out
end

return M
