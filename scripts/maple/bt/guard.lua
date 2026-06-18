local BT = require("maple.bt.constants")

local Guard = {}
Guard.__index = Guard

function Guard.new(name, condition, child)
    return setmetatable({ name = name, condition = condition, child = child }, Guard)
end

function Guard:tick(bb)
    if not self.condition(bb) then return BT.FAILURE end
    return self.child:tick(bb)
end

return Guard
