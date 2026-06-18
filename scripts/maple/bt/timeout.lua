local BT = require("maple.bt.constants")

local Timeout = {}
Timeout.__index = Timeout

function Timeout.new(name, child, max_ticks, logger)
    return setmetatable({ name = name, child = child, max_ticks = max_ticks, started = nil, logger = logger }, Timeout)
end

function Timeout:tick(bb)
    self.started = self.started or bb.runtime.tick
    if bb.runtime.tick - self.started > self.max_ticks then
        if self.logger then self.logger:warn("node_failure", { node = self.name, reason = "timeout" }, bb) end
        self.started = nil
        return BT.FAILURE
    end
    local r = self.child:tick(bb)
    if r ~= BT.RUNNING then self.started = nil end
    return r
end

return Timeout
