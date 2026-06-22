local Runtime = {}

local function number_or(value, fallback)
    local n = tonumber(value)
    if n == nil then return fallback end
    return n
end

local function position(entity)
    entity = entity or {}
    return entity.position or {
        x = number_or(entity.x, 0),
        y = number_or(entity.y, 0),
        z = number_or(entity.z, 0)
    }
end

local function distance(actor_pos, target)
    local target_pos = position(target)
    local dx = number_or(target_pos.x, 0) - number_or(actor_pos.x, 0)
    local dy = number_or(target_pos.y, 0) - number_or(actor_pos.y, 0)
    return {
        dx = dx,
        dy = dy,
        abs_x = math.abs(dx),
        abs_y = math.abs(dy),
        manhattan = math.abs(dx) + math.abs(dy)
    }
end

local function choose_nearest_target(actor_pos, targets)
    local best, best_metrics = nil, nil
    for _, target in ipairs(targets or {}) do
        local metrics = distance(actor_pos, target)
        if not best_metrics or metrics.manhattan < best_metrics.manhattan then
            best = target
            best_metrics = metrics
        end
    end
    return best, best_metrics
end

local function pickable_resources(resources)
    local out = {}
    for _, item in ipairs(resources or {}) do
        if item.can_pick ~= false then out[#out + 1] = item end
    end
    return out
end

local function proposal(fields)
    fields = fields or {}
    fields.action = fields.action or "Wait"
    fields.reason = fields.reason or "baseline"
    fields.params = fields.params or {}
    fields.executable = fields.executable ~= false
    fields.confidence = fields.confidence or 1.0
    return fields
end

local function key_code_number(value, fallback)
    if type(value) == "string" then
        return tonumber(value) or tonumber(value:match("^0[xX](%x+)$"), 16) or fallback
    end
    return tonumber(value) or fallback
end

local function skill_params(cfg)
    cfg = cfg or {}
    return {
        key_code = key_code_number(cfg.skill_key_code or cfg.key_code, 0x10),
        key_name = cfg.skill_key or cfg.key_name or "Shift",
        input_mode = cfg.skill_input_mode or cfg.input_mode or "foreground",
        key_mode = cfg.key_mode,
        hold_ms = number_or(cfg.skill_hold_ms or cfg.hold_ms, 0)
    }
end

function Runtime.describe_target(actor, target)
    local actor_pos = position(actor)
    if not target then
        return { exists = false, dx = 0, dy = 0, abs_x = 0, abs_y = 0, manhattan = 0 }
    end
    local metrics = distance(actor_pos, target)
    metrics.exists = true
    metrics.id = target.id
    metrics.name = target.name
    metrics.x = position(target).x
    metrics.y = position(target).y
    return metrics
end

function Runtime.decide(context)
    context = context or {}
    local cfg = context.cfg or {}
    local actor = context.actor or {}
    local actor_pos = position(actor)
    local world = context.world or {}
    local state = context.state or {}

    local resources = pickable_resources(world.nearby_resources or {})
    local target, metrics = choose_nearest_target(actor_pos, world.nearby_targets or {})
    if not target then
        if cfg.baseline_pickup_enabled ~= false and #resources > 0 and state.just_attacked ~= true then
            return proposal({
                action = "PickAllDrops",
                reason = "pickable_drop_visible_no_target",
                resources_count = #resources,
                target_count = #(world.nearby_targets or {})
            })
        end
        if state.is_moving then
            return proposal({ action = "StopMove", reason = "no_target_stop_move" })
        end
        return proposal({
            action = "Wait",
            reason = "no_target",
            params = { seconds = number_or(cfg.baseline_tick_ms, 250) / 1000 },
            confidence = 0.4
        })
    end

    local range_x = number_or(cfg.baseline_attack_range_x, cfg.default_skill_range_x or 95)
    local range_y = number_or(cfg.baseline_attack_range_y, cfg.default_skill_range_y or 45)
    local stop_range_x = number_or(cfg.baseline_stop_range_x, math.max(0, range_x - 30))
    local pursuit_y = number_or(cfg.baseline_pursuit_y_tolerance, range_y)
    local in_attack_box = metrics.abs_x <= range_x and metrics.abs_y <= range_y

    if in_attack_box then
        if state.is_moving and metrics.abs_x <= stop_range_x then
            return proposal({
                action = "StopMove",
                reason = "target_in_range_stop_before_attack",
                target = target,
                metrics = metrics
            })
        end
        return proposal({
            action = "PressKey",
            reason = "target_in_attack_box",
            target = target,
            metrics = metrics,
            params = skill_params(cfg)
        })
    end

    if metrics.abs_y > pursuit_y then
        if state.is_moving then
            return proposal({
                action = "StopMove",
                reason = "target_y_too_far_stop_move",
                target = target,
                metrics = metrics
            })
        end
        return proposal({
            action = "Wait",
            reason = "target_y_too_far",
            target = target,
            metrics = metrics,
            params = { seconds = number_or(cfg.baseline_tick_ms, 250) / 1000 },
            confidence = 0.3
        })
    end

    local direction = metrics.dx < 0 and -1 or 1
    return proposal({
        action = "SetWalkDirection",
        reason = "approach_nearest_target",
        target = target,
        metrics = metrics,
        params = { direction = direction, vertical = 0 }
    })
end

return Runtime
