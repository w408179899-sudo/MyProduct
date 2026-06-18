local Selector = require("maple.bt.selector")

local PrioritySelector = {}
PrioritySelector.__index = PrioritySelector

function PrioritySelector.new(name, children)
    table.sort(children, function(a, b) return (a.priority or 0) > (b.priority or 0) end)
    local self = Selector.new(name or "PrioritySelector", children)
    return setmetatable(self, PrioritySelector)
end

setmetatable(PrioritySelector, { __index = Selector })

return PrioritySelector
