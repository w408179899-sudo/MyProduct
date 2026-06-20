local Perception = {}
Perception.__index = Perception

local function interval(cfg, name, fallback)
    cfg = cfg or {}
    return math.max(1, tonumber(cfg[name]) or fallback or 1)
end

local function due(bb, last_refresh, name, ticks)
    local last = last_refresh[name]
    return last == nil or ((bb.runtime.tick or 0) - last) >= ticks
end

function Perception.new(environment, logger, cfg)
    return setmetatable({
        environment = environment,
        logger = logger,
        cfg = cfg or {},
        last_refresh = {}
    }, Perception)
end

function Perception:refresh_domain(bb, name, reader)
    bb[name] = reader()
    self.last_refresh[name] = bb.runtime.tick or 0
    bb.metrics.perception_refresh_count = (bb.metrics.perception_refresh_count or 0) + 1
end

function Perception:update(bb)
    local ok, err = pcall(function()
        if due(bb, self.last_refresh, "actor", interval(self.cfg, "actor_interval_ticks", 1)) then
            self:refresh_domain(bb, "actor", function() return self.environment:get_actor_state(bb) end)
        end
        if due(bb, self.last_refresh, "world", interval(self.cfg, "world_interval_ticks", 1)) then
            self:refresh_domain(bb, "world", function() return self.environment:get_world_state(bb) end)
        end
        if due(bb, self.last_refresh, "quest", interval(self.cfg, "quest_interval_ticks", 5)) then
            self:refresh_domain(bb, "quest", function() return self.environment:get_quest_state(bb) end)
        end
        if due(bb, self.last_refresh, "inventory", interval(self.cfg, "inventory_interval_ticks", 10)) then
            self:refresh_domain(bb, "inventory", function() return self.environment:get_inventory_state(bb) end)
        end
        if due(bb, self.last_refresh, "equipment", interval(self.cfg, "equipment_interval_ticks", 10)) then
            self:refresh_domain(bb, "equipment", function() return self.environment:get_equipment_state(bb) end)
        end
        if due(bb, self.last_refresh, "skill", interval(self.cfg, "skill_interval_ticks", 10)) then
            self:refresh_domain(bb, "skill", function() return self.environment:get_skill_state(bb) end)
        end
    end)
    if not ok then
        bb.runtime.last_error = tostring(err)
        if self.logger then self.logger:error("perception_failure", { error = tostring(err) }, bb) end
    end
end

return Perception
