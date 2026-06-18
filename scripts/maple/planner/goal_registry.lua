local Registry = {}
Registry.__index = Registry

function Registry.new()
    return setmetatable({ goals = {} }, Registry)
end

function Registry:add(goal)
    self.goals[#self.goals + 1] = goal
    table.sort(self.goals, function(a, b) return a.priority > b.priority end)
end

function Registry:list()
    return self.goals
end

return Registry
