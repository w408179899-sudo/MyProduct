local goals = {}

goals.recovery = {
    id = "recovery", priority = 800, timeout = 60, cooldown = 5,
    can_activate = function(bb) return bb.runtime.last_error ~= nil end,
    reason = function(bb) return bb.runtime.last_error or "runtime_error" end
}

goals.safety = {
    id = "safety", priority = 700, timeout = 60, cooldown = 5,
    can_activate = function(bb) return bb.safety.circuit_breaker_open == true end,
    reason = function() return "circuit_breaker_open" end
}

goals.stuck = {
    id = "stuck", priority = 600, timeout = 60, cooldown = 5,
    can_activate = function(bb) return bb.navigation.is_stuck == true end,
    reason = function() return "navigation_stuck" end
}

goals.inventory = {
    id = "inventory", priority = 500, timeout = 60, cooldown = 5,
    can_activate = function(bb) return bb.inventory.is_full == true end,
    reason = function() return "inventory_full" end
}

goals.equipment = {
    id = "equipment", priority = 400, timeout = 60, cooldown = 5,
    can_activate = function(bb) return bb.equipment.upgrade_available == true or bb.equipment.durability_low == true end,
    reason = function() return "equipment_attention" end
}

goals.skill = {
    id = "skill", priority = 300, timeout = 60, cooldown = 5,
    can_activate = function(bb) return bb.skill.should_learn == true end,
    reason = function() return "skill_available" end
}

goals.quest = {
    id = "quest", priority = 200, timeout = 300, cooldown = 2,
    can_activate = function(bb) return bb.quest.current_quest_id ~= nil or bb.account.enabled ~= false end,
    reason = function() return "quest_or_default_work" end
}

goals.idle = {
    id = "idle", priority = 0, timeout = 30, cooldown = 0,
    can_activate = function() return true end,
    reason = function() return "no_goal_ready" end
}

return goals
