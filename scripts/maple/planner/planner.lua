local Registry = require("maple.planner.goal_registry")
local Goals = require("maple.planner.goals")

local Planner = {}
Planner.__index = Planner

function Planner.new(cfg, logger)
    local registry = Registry.new()
    for _, id in ipairs({ "recovery", "safety", "stuck", "inventory", "equipment", "skill", "quest", "idle" }) do
        registry:add(Goals[id])
    end
    return setmetatable({ cfg = cfg, logger = logger, registry = registry }, Planner)
end

function Planner:select_goal(bb)
    for _, goal in ipairs(self.registry:list()) do
        if goal.can_activate(bb) then return goal end
    end
    return Goals.idle
end

function Planner:can_switch_goal(bb, current_id, candidate)
    if current_id == nil or current_id == candidate.id then return true end
    local current = Goals[current_id]
    if current and candidate.priority > current.priority then return true end
    local min_ticks = tonumber(self.cfg.thresholds.goal_switch_hysteresis_ticks) or 0
    return (bb.runtime.tick - (bb.task.last_goal_switch_tick or 0)) >= min_ticks
end

function Planner:update(bb)
    local old = bb.task.active_goal
    local candidate = self:select_goal(bb)
    if not self:can_switch_goal(bb, old, candidate) then return end
    if old ~= candidate.id then
        bb.task.previous_goal = old
        bb.task.active_goal = candidate.id
        bb.task.last_goal_switch_tick = bb.runtime.tick
        bb.metrics.goal_change_count = bb.metrics.goal_change_count + 1
        if self.logger then
            self.logger:info("goal_changed", {
                from = old,
                to = candidate.id,
                reason = candidate.reason(bb)
            }, bb)
        end
    end
end

return Planner
