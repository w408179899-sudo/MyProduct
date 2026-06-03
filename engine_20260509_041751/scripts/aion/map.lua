local core = require("aion.core")
local data = core.data

local M = {}

function M.current()
    return core.first("AionData.GetCurrentMap", data.GetCurrentMap)
end

function M.get(index)
    return core.first("AionData.GetMap", data.GetMap, index)
end

function M.bigMapId()
    return core.first("AionData.GetBigMapId", data.GetBigMapId)
end

function M.nodes(bigMapId)
    local ok, list, err = core.first("AionData.GetMapNodeList", data.GetMapNodeList, bigMapId)
    if not ok then
        return false, nil, err
    end
    return true, list or {}, nil
end

function M.findNodeByName(name, bigMapId)
    local ok, list, err = M.nodes(bigMapId)
    if not ok then
        return false, nil, err
    end

    for _, node in ipairs(list) do
        if node.name == name or node.name_en == name then
            return true, node, nil
        end
    end
    return true, nil, nil
end

function M.canTeleport()
    return core.first("AionData.IsCanTeleport", data.IsCanTeleport)
end

function M.nodeTeleport(nodeId, price)
    return core.first("AionData.NodeTeleport", data.NodeTeleport, nodeId, price)
end

function M.bigMapTeleports(race)
    if type(data.GetBigMapTeleports) == "function" then
        return core.first("AionData.GetBigMapTeleports", data.GetBigMapTeleports, race)
    end

    local ok, char, err = core.getCharacter()
    if not ok then
        return false, nil, err
    end
    local key = char and char.race == 1 and "asmodian" or "elyos"
    return true, data.BIG_MAP_TELEPORTS and data.BIG_MAP_TELEPORTS[key] or {}, nil
end

function M.bigMapTeleport(slot)
    return core.first("AionData.BigMapTeleport", data.BigMapTeleport, slot)
end

return M
