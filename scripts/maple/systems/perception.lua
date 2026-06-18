local Perception = {}
Perception.__index = Perception

function Perception.new(environment, logger)
    return setmetatable({ environment = environment, logger = logger }, Perception)
end

function Perception:update(bb)
    local ok, err = pcall(function()
        bb.actor = self.environment:get_actor_state()
        bb.inventory = self.environment:get_inventory_state()
        bb.quest = self.environment:get_quest_state()
        bb.equipment = self.environment:get_equipment_state()
        bb.skill = self.environment:get_skill_state()
        bb.world = self.environment:get_world_state()
    end)
    if not ok then
        bb.runtime.last_error = tostring(err)
        if self.logger then self.logger:error("perception_failure", { error = tostring(err) }, bb) end
    end
end

return Perception
