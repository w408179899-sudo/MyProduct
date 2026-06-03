local core = require("aion.core")
local data = core.data

local M = {}

function M.find(name)
    return core.first("AionData.FindUIObj", data.FindUIObj, name)
end

function M.list(includeNoName)
    local ok, list, err = core.first("AionData.GetUIList", data.GetUIList, includeNoName)
    if not ok then
        return false, nil, err
    end
    return true, list or {}, nil
end

function M.children(parent, maxDepth)
    local ok, list, err = core.first("AionData.GetUIChildren", data.GetUIChildren, parent, maxDepth)
    if not ok then
        return false, nil, err
    end
    return true, list or {}, nil
end

function M.click(ctrl)
    return core.first("AionData.ClickButton", data.ClickButton, ctrl)
end

return M
