local BT = require("maple.bt.constants")

local Sequence = {}
Sequence.__index = Sequence

function Sequence.new(name, children)
    return setmetatable({ name = name or "Sequence", children = children or {} }, Sequence)
end

function Sequence:tick(bb)
    for _, child in ipairs(self.children) do
        local r = child:tick(bb)
        if r ~= BT.SUCCESS then return r end
    end
    return BT.SUCCESS
end

return Sequence
