local core = require("aion.core")
local data = core.data

local M = {}

local function contains(text, needle)
    if not text or not needle then
        return false
    end
    return string.find(text, needle, 1, true) ~= nil
end

function M.list()
    local ok, list, err = core.first("AionData.GetAroundList", data.GetAroundList)
    if not ok then
        return false, nil, err
    end
    return true, list or {}, nil
end

function M.filter(predicate)
    local ok, list, err = M.list()
    if not ok then
        return false, nil, err
    end

    local out = {}
    for _, entity in ipairs(list) do
        if predicate(entity) then
            out[#out + 1] = entity
        end
    end
    return true, out, nil
end

function M.findByName(name, opts)
    opts = opts or {}
    local exact = opts.exact ~= false
    local ok, list, err = M.list()
    if not ok then
        return false, nil, err
    end

    for _, entity in ipairs(list) do
        local matched = exact and entity.name == name or contains(entity.name, name)
        if matched then
            return true, entity, nil
        end
    end
    return true, nil, nil
end

function M.nearest(predicate)
    local ok, char, err = core.getCharacter()
    if not ok then
        return false, nil, err
    end
    if not char then
        return false, nil, "character is nil"
    end

    local listOk, list, listErr = M.list()
    if not listOk then
        return false, nil, listErr
    end

    local best, bestDist = nil, math.huge
    for _, entity in ipairs(list) do
        if predicate(entity) then
            local dist = core.distance3(char, entity)
            if dist < bestDist then
                best = entity
                bestDist = dist
            end
        end
    end

    if best then
        best.distance = bestDist
    end
    return true, best, nil
end

function M.nearestByName(name, opts)
    opts = opts or {}
    local exact = opts.exact ~= false
    return M.nearest(function(entity)
        if opts.aliveOnly and entity.dead then
            return false
        end
        if opts.interactableOnly and (entity.interact_id or 0) == 0 then
            return false
        end
        return exact and entity.name == name or contains(entity.name, name)
    end)
end

function M.nearestNpc(name)
    return M.nearestByName(name, { exact = true, interactableOnly = true })
end

function M.nearestLootable()
    return M.nearest(function(entity)
        return (entity.lootable or 0) ~= 0
    end)
end

return M
