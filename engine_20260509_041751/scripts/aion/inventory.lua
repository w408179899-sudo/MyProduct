local core = require("aion.core")
local data = core.data

local M = {}

function M.list()
    local ok, list, err = core.first("AionData.GetInventoryList", data.GetInventoryList)
    if not ok then
        return false, nil, err
    end
    return true, list or {}, nil
end

function M.kinah()
    return core.first("AionData.GetKinah", data.GetKinah)
end

function M.findByName(name)
    local ok, list, err = M.list()
    if not ok then
        return false, nil, err
    end

    for _, item in ipairs(list) do
        if item.text == name or item.name == name then
            return true, item, nil
        end
    end
    return true, nil, nil
end

function M.countByName(name)
    local ok, list, err = M.list()
    if not ok then
        return false, nil, err
    end

    local count = 0
    for _, item in ipairs(list) do
        if item.text == name or item.name == name then
            count = count + (item.count or 1)
        end
    end
    return true, count, nil
end

function M.useItem(itemId)
    return core.first("AionData.UseItem", data.UseItem, itemId)
end

function M.useByName(name)
    local ok, item, err = M.findByName(name)
    if not ok then
        return false, nil, err
    end
    if not item then
        return false, nil, "item not found: " .. tostring(name)
    end
    return M.useItem(item.id)
end

function M.equipItem(itemId, equipPos, unequip)
    return core.first("AionData.EquipItem", data.EquipItem, itemId, equipPos, unequip)
end

function M.equipByName(name)
    local ok, item, err = M.findByName(name)
    if not ok then
        return false, nil, err
    end
    if not item then
        return false, nil, "item not found: " .. tostring(name)
    end
    return M.equipItem(item.id, item.equip_pos, false)
end

function M.decomposeItem(itemId)
    return core.first("AionData.DecomposeItem", data.DecomposeItem, itemId)
end

function M.decomposeByCategory(catName, limit)
    local ok, list, err = M.list()
    if not ok then
        return false, nil, err
    end

    local done = 0
    for _, item in ipairs(list) do
        if item.cat_name == catName then
            local callOk, _, callErr = M.decomposeItem(item.id)
            if not callOk then
                return false, done, callErr
            end
            done = done + 1
            if limit and done >= limit then
                break
            end
        end
    end
    return true, done, nil
end

return M
