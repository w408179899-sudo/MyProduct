local BT = require("maple.bt.constants")

local Retry = {}
Retry.__index = Retry

function Retry.new(name, child, max_retries, logger)
    return setmetatable({ name = name, child = child, max_retries = max_retries, tries = 0, logger = logger }, Retry)
end

function Retry:tick(bb)
    local r = self.child:tick(bb)
    if r == BT.FAILURE then
        self.tries = self.tries + 1
        if self.tries <= self.max_retries then return BT.RUNNING end
        self.tries = 0
        if self.logger then self.logger:warn("node_failure", { node = self.name, reason = "retry_exhausted" }, bb) end
        return BT.FAILURE
    end
    if r == BT.SUCCESS then self.tries = 0 end
    return r
end

return Retry
