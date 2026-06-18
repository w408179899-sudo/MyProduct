local BT = require("maple.bt.constants")

local Selector = {}
Selector.__index = Selector

function Selector.new(name, children)
    return setmetatable({ name = name or "Selector", children = children or {} }, Selector)
end

function Selector:tick(bb)
    for _, child in ipairs(self.children) do
        local r = child:tick(bb)
        if r ~= BT.FAILURE then return r end
    end
    return BT.FAILURE
end

return Selector
