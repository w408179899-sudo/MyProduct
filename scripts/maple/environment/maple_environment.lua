local MockEnvironment = require("maple.environment.mock_environment")

local MapleEnvironment = {}
MapleEnvironment.__index = MapleEnvironment

function MapleEnvironment.new(opts)
    local self = MockEnvironment.new(opts and opts.world or nil)
    self.capabilities.real_client = false
    self.adapter_name = "maple_environment_stub"
    return setmetatable(self, MapleEnvironment)
end

setmetatable(MapleEnvironment, { __index = MockEnvironment })

return MapleEnvironment
