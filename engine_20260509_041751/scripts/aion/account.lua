local core = require("aion.core")
local data = core.data

local M = {}

function M.serverList(serverUi)
    local ok, list, err = core.first("AionData.GetServerList", data.GetServerList, serverUi)
    if not ok then
        return false, nil, err
    end
    return true, list or {}, nil
end

function M.currentServerId()
    return core.first("AionData.GetCurSelectedServerId", data.GetCurSelectedServerId)
end

function M.selectServer(serverIndex)
    return core.first("AionData.SelectServer", data.SelectServer, serverIndex)
end

function M.characterList()
    local ok, list, err = core.first("AionData.GetCharacterList", data.GetCharacterList)
    if not ok then
        return false, nil, err
    end
    return true, list or {}, nil
end

function M.selectCharacter(index)
    return core.first("AionData.SelectCharacter", data.SelectCharacter, index)
end

function M.createCharacter(name, gender, race, jobId)
    return core.first("AionData.CreateCharacter", data.CreateCharacter, name, gender, race, jobId)
end

return M
