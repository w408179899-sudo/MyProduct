local core = require("aion.core")
local data = core.data

local M = {}

function M.info()
    return core.first("AionData.GetChannelInfo", data.GetChannelInfo)
end

function M.switch(channelIndex)
    return core.first("AionData.SwitchChannel", data.SwitchChannel, channelIndex)
end

return M
