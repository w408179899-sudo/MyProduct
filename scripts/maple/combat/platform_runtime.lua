local PlatformMap = require("maple.navigation.platform_map")

local PlatformRuntime = {}

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

local function count(list)
    return type(list) == "table" and #list or 0
end

local function abs(value)
    return math.abs(number_or(value, 0))
end

local function drop_lookup_id(drop)
    drop = drop or {}
    local id = drop.id or drop.instance_id or drop.item_id or drop.source_index
    if id == nil then return "" end
    return tostring(id)
end

local function is_drop_ignored(drop, cfg)
    local ignored = cfg and cfg.ignored_drop_ids
    if type(ignored) ~= "table" then return false end
    local id = drop_lookup_id(drop)
    return id ~= "" and ignored[id] ~= nil and ignored[id] ~= false
end

local function drop_age_ticks(drop, cfg)
    local id = drop_lookup_id(drop)
    local first_seen = cfg and cfg.drop_first_seen_ticks and cfg.drop_first_seen_ticks[id]
    local tick_index = cfg and cfg.tick_index
    if id == "" or tonumber(first_seen) == nil or tonumber(tick_index) == nil then return 0 end
    return math.max(0, tonumber(tick_index) - tonumber(first_seen))
end

local function clamp(value, min_value, max_value)
    value = number_or(value, 0)
    if min_value and value < min_value then return min_value end
    if max_value and value > max_value then return max_value end
    return value
end

local function platform_safe_bounds(platform, cfg)
    platform = platform or {}
    local margin = number_or(cfg.platform_safe_margin, platform.safe_margin or 0)
    local left = number_or(platform.left_x, 0) + margin
    local right = number_or(platform.right_x, 0) - margin
    if left > right then
        left = number_or(platform.left_x, 0)
        right = number_or(platform.right_x, 0)
    end
    return left, right
end

local function desired_direction(actor_x, target_x, fallback)
    if number_or(target_x, 0) < number_or(actor_x, 0) then return -1 end
    if number_or(target_x, 0) > number_or(actor_x, 0) then return 1 end
    return number_or(fallback, 1)
end

local function predict_target(target, cfg)
    local pos = position(target)
    local delay = number_or(cfg.cast_delay_seconds, number_or(cfg.skill_cast_delay_seconds, 0.7))
    return {
        x = number_or(pos.x, 0) + number_or(target.vx, 0) * delay,
        y = number_or(pos.y, 0) + number_or(target.vy, 0) * delay,
        z = number_or(pos.z, 0) + number_or(target.vz, 0) * delay
    }
end

local function proposal(fields)
    fields = fields or {}
    fields.action = fields.action or "Wait"
    fields.reason = fields.reason or "platform_runtime"
    fields.params = fields.params or {}
    fields.executable = fields.executable ~= false
    fields.confidence = fields.confidence or 1.0
    return fields
end

local function skill_params(cfg)
    cfg = cfg or {}
    return {
        key_code = number_or(cfg.skill_key_code or cfg.key_code, 0x10),
        key_name = cfg.skill_key or cfg.key_name or "Shift",
        input_mode = cfg.skill_input_mode or cfg.input_mode or "foreground",
        key_mode = cfg.key_mode,
        hold_ms = number_or(cfg.skill_hold_ms or cfg.hold_ms, 0)
    }
end

local function locate(map, point, cfg, tolerance_key)
    return PlatformMap.locate_point(map, point, {
        y_tolerance = number_or(cfg[tolerance_key], number_or(cfg.platform_y_tolerance, 1.0)),
        x_margin = number_or(cfg.platform_x_margin, 0.2)
    })
end

local function candidate_from_target(map, actor_pos, actor_loc, target, cfg)
    local target_pos = position(target)
    local loc = locate(map, target_pos, cfg, "platform_y_tolerance")
    if not loc then return nil end
    if actor_loc and loc.platform_id ~= actor_loc.platform_id then return nil end

    local predicted = predict_target(target, cfg)
    local direction = desired_direction(actor_pos.x, predicted.x, cfg.last_direction)
    local range_x = number_or(cfg.skill_range_x, number_or(cfg.platform_skill_range_x, 2.0))
    local range_y = number_or(cfg.skill_range_y, number_or(cfg.platform_skill_range_y, 0.3))
    local preferred = number_or(cfg.preferred_attack_distance, number_or(cfg.platform_preferred_attack_distance, 1.4))
    local platform = loc.platform
    local left, right = platform_safe_bounds(platform, cfg)
    local stand_x = clamp(predicted.x - direction * preferred, left, right)
    local stand_y = PlatformMap.y_at(platform, stand_x)

    local dx = number_or(target_pos.x, 0) - number_or(actor_pos.x, 0)
    local dy = number_or(target_pos.y, 0) - number_or(actor_pos.y, 0)
    local predicted_dx = number_or(predicted.x, 0) - number_or(actor_pos.x, 0)
    local predicted_dy = number_or(predicted.y, 0) - number_or(actor_pos.y, 0)
    local stand_dx = stand_x - number_or(actor_pos.x, 0)
    local grounded_tolerance = number_or(cfg.grounded_y_tolerance, 0.2)

    return {
        target = target,
        target_position = target_pos,
        loc = loc,
        platform = platform,
        predicted_position = predicted,
        dx = dx,
        dy = dy,
        abs_x = abs(dx),
        abs_y = abs(dy),
        predicted_dx = predicted_dx,
        predicted_dy = predicted_dy,
        predicted_abs_x = abs(predicted_dx),
        predicted_abs_y = abs(predicted_dy),
        direction = direction,
        stand_x = stand_x,
        stand_y = stand_y,
        stand_dx = stand_dx,
        abs_stand_dx = abs(stand_dx),
        grounded = loc.abs_y_delta <= grounded_tolerance,
        in_skill_box = abs(predicted_dx) <= range_x and abs(predicted_dy) <= range_y,
        score = abs(stand_dx) + abs(predicted_dy) * 2
    }
end

local function build_target_candidates(map, actor_pos, actor_loc, targets, cfg)
    local candidates = {}
    for _, target in ipairs(targets or {}) do
        local candidate = candidate_from_target(map, actor_pos, actor_loc, target, cfg)
        if candidate then candidates[#candidates + 1] = candidate end
    end
    table.sort(candidates, function(a, b)
        if a.in_skill_box ~= b.in_skill_box then return a.in_skill_box end
        if a.grounded ~= b.grounded then return a.grounded end
        return a.score < b.score
    end)
    return candidates
end

local function candidate_from_drop(map, actor_pos, actor_loc, drop, cfg)
    local drop_pos = position(drop)
    local loc = locate(map, drop_pos, cfg, "pickup_platform_y_tolerance")
    if not loc then return nil end
    if actor_loc and loc.platform_id ~= actor_loc.platform_id then return nil end
    local dx = number_or(drop_pos.x, 0) - number_or(actor_pos.x, 0)
    local dy = number_or(drop_pos.y, 0) - number_or(actor_pos.y, 0)
    local ignore_raw_y = cfg.pickup_ignore_raw_y ~= false
    return {
        drop = drop,
        loc = loc,
        dx = dx,
        dy = dy,
        abs_x = abs(dx),
        abs_y = abs(dy),
        direction = desired_direction(actor_pos.x, drop_pos.x, cfg.last_direction),
        score = abs(dx) + (ignore_raw_y and 0 or abs(dy) * 2),
        ignore_raw_y = ignore_raw_y,
        age_ticks = drop_age_ticks(drop, cfg)
    }
end

local function build_drop_candidates(map, actor_pos, actor_loc, drops, cfg)
    local candidates = {}
    for _, drop in ipairs(drops or {}) do
        if not is_drop_ignored(drop, cfg) and (cfg.pickup_include_all_drops == true or drop.can_pick ~= false) then
            local candidate = candidate_from_drop(map, actor_pos, actor_loc, drop, cfg)
            if candidate then candidates[#candidates + 1] = candidate end
        end
    end
    table.sort(candidates, function(a, b) return a.score < b.score end)
    return candidates
end

local function pick_range_for_drop(drop, cfg, nearby_key)
    local range_x = number_or(cfg[nearby_key], number_or(cfg.pickup_range_x, 0.45))
    local range_y = number_or(cfg.pickup_range_y, 0.5)
    local ignore_raw_y = cfg.pickup_ignore_raw_y ~= false
    return drop.abs_x <= range_x and (ignore_raw_y or drop.abs_y <= range_y), range_x, range_y, ignore_raw_y
end

local function pickup_proposal(drop, target_candidates, reason, actor_loc, cfg, total_drops)
    local in_range, _, _, ignore_raw_y = pick_range_for_drop(drop, cfg, "pickup_range_x")
    if in_range then
        return proposal({
            action = "PickAllDrops",
            reason = reason,
            drop = drop.drop,
            candidates = target_candidates,
            drop_candidates = { drop },
            drop_loc = drop.loc,
            actor_loc = actor_loc,
            resources_count = 1,
            debug = {
                total_drops = total_drops,
                pickup_ignore_raw_y = ignore_raw_y,
                drop_raw_y_delta = drop.dy,
                drop_platform_y_delta = drop.loc and drop.loc.y_delta,
                drop_age_ticks = drop.age_ticks
            }
        })
    end

    return proposal({
        action = "SetWalkDirection",
        reason = reason,
        drop = drop.drop,
        candidates = target_candidates,
        drop_candidates = { drop },
        drop_loc = drop.loc,
        metrics = drop,
        actor_loc = actor_loc,
        params = {
            direction = drop.direction,
            vertical = 0,
            target_x = number_or((drop.drop and drop.drop.x), nil),
            target_y = number_or((drop.drop and drop.drop.y), nil),
            dx = drop.dx,
            reason = reason
        },
        debug = {
            resources_count = 1,
            total_drops = total_drops,
            target_x = drop.drop and drop.drop.x,
            target_y = drop.drop and drop.drop.y,
            drop_dx = drop.dx,
            drop_raw_y_delta = drop.dy,
            drop_platform_y_delta = drop.loc and drop.loc.y_delta,
            drop_age_ticks = drop.age_ticks
        }
    })
end

local function opportunistic_pickup_proposal(drop_candidates, target_candidates, actor_loc, cfg, total_drops)
    if cfg.pickup_enabled == false or cfg.pickup_during_combat_enabled == false then return nil end

    for _, drop in ipairs(drop_candidates or {}) do
        local in_nearby = pick_range_for_drop(drop, cfg, "pickup_during_combat_nearby_range_x")
        if in_nearby then
            return pickup_proposal(drop, target_candidates, "platform_drop_nearby_during_combat", actor_loc, cfg, total_drops)
        end
    end

    local age_threshold = number_or(cfg.pickup_age_priority_ticks, 0)
    local max_detour_x = number_or(cfg.pickup_during_combat_max_detour_x, 0)
    if age_threshold <= 0 or max_detour_x <= 0 then return nil end

    for _, drop in ipairs(drop_candidates or {}) do
        if number_or(drop.age_ticks, 0) >= age_threshold and drop.abs_x <= max_detour_x then
            return pickup_proposal(drop, target_candidates, "platform_drop_aged_during_combat", actor_loc, cfg, total_drops)
        end
    end
    return nil
end

function PlatformRuntime.decide(context)
    context = context or {}
    local cfg = {}
    for key, value in pairs(context.cfg or {}) do cfg[key] = value end
    local map = context.map or {}
    local actor = context.actor or {}
    local world = context.world or {}
    local state = context.state or {}
    local actor_pos = position(actor)
    local actor_loc = locate(map, actor_pos, cfg, "actor_platform_y_tolerance")
    local last_direction = number_or(state.last_direction, 1)
    local pickup_only = context.mode == "pickup_only" or cfg.pickup_only == true
    cfg.last_direction = last_direction
    cfg.ignored_drop_ids = state.ignored_drops or state.ignored_drop_ids or cfg.ignored_drop_ids
    cfg.drop_first_seen_ticks = state.drop_first_seen_ticks or cfg.drop_first_seen_ticks
    cfg.tick_index = state.tick_index or cfg.tick_index

    if not actor_loc then
        return proposal({
            action = "Wait",
            reason = "actor_not_on_recorded_platform",
            confidence = 0.2,
            actor_position = actor_pos,
            params = { seconds = number_or(cfg.tick_ms, 120) / 1000 },
            debug = {
                total_targets = count(world.nearby_targets),
                total_drops = count(world.nearby_resources)
            }
        })
    end

    local target_candidates = build_target_candidates(map, actor_pos, actor_loc, world.nearby_targets, cfg)
    local drop_candidates = build_drop_candidates(map, actor_pos, actor_loc, world.nearby_resources, cfg)
    if not pickup_only and #target_candidates > 0 then
        local pickup = opportunistic_pickup_proposal(drop_candidates, target_candidates, actor_loc, cfg, count(world.nearby_resources))
        if pickup then
            pickup.drop_candidates = drop_candidates
            return pickup
        end

        local target = target_candidates[1]
        local arrival = number_or(cfg.arrival_tolerance_x, 0.18)

        if target.in_skill_box then
            return proposal({
                action = "FaceAndPressKey",
                reason = "platform_target_in_skill_box",
                target = target.target,
                candidates = target_candidates,
                target_loc = target.loc,
                metrics = target,
                actor_loc = actor_loc,
                params = skill_params(cfg),
                debug = {
                    platform_candidates = #target_candidates,
                    total_targets = count(world.nearby_targets),
                    direction = target.direction,
                    predicted_x = target.predicted_position.x,
                    predicted_y = target.predicted_position.y,
                    y_delta = target.loc.y_delta
                }
            })
        end

        if not target.grounded and target.predicted_abs_y > number_or(cfg.skill_range_y, 0.3) then
            return proposal({
                action = "Wait",
                reason = "platform_target_airborne_wait_land",
                target = target.target,
                candidates = target_candidates,
                target_loc = target.loc,
                metrics = target,
                actor_loc = actor_loc,
                confidence = 0.5,
                params = { seconds = number_or(cfg.tick_ms, 120) / 1000 },
                debug = { platform_candidates = #target_candidates, total_targets = count(world.nearby_targets) }
            })
        end

        if target.abs_stand_dx <= arrival then
            return proposal({
                action = "FaceAndPressKey",
                reason = "platform_at_stand_point_attack",
                target = target.target,
                candidates = target_candidates,
                target_loc = target.loc,
                metrics = target,
                actor_loc = actor_loc,
                params = skill_params(cfg),
                debug = { platform_candidates = #target_candidates, total_targets = count(world.nearby_targets) }
            })
        end

        return proposal({
            action = "SetWalkDirection",
            reason = "platform_move_to_attack_stand",
            target = target.target,
            candidates = target_candidates,
            target_loc = target.loc,
            metrics = target,
            actor_loc = actor_loc,
            params = { direction = target.stand_dx < 0 and -1 or 1, vertical = 0 },
            debug = {
                platform_candidates = #target_candidates,
                total_targets = count(world.nearby_targets),
                stand_x = target.stand_x,
                stand_y = target.stand_y,
                target_x = target.target_position.x,
                target_y = target.target_position.y
            }
        })
    end

    if #drop_candidates > 0 and cfg.pickup_enabled ~= false then
        local drop = drop_candidates[1]
        local range_x = number_or(cfg.pickup_range_x, 0.45)
        local range_y = number_or(cfg.pickup_range_y, 0.5)
        local ignore_raw_y = cfg.pickup_ignore_raw_y ~= false
        local in_pick_y = ignore_raw_y or drop.abs_y <= range_y
        if drop.abs_x <= range_x and in_pick_y then
            return proposal({
                action = "PickAllDrops",
                reason = "platform_drop_in_pick_range",
                drop = drop.drop,
                candidates = target_candidates,
                drop_candidates = drop_candidates,
                drop_loc = drop.loc,
                actor_loc = actor_loc,
                resources_count = #drop_candidates,
                debug = {
                    total_drops = count(world.nearby_resources),
                    pickup_ignore_raw_y = ignore_raw_y,
                    drop_raw_y_delta = drop.dy,
                    drop_platform_y_delta = drop.loc and drop.loc.y_delta
                }
            })
        end
        return proposal({
            action = "SetWalkDirection",
            reason = "platform_move_to_drop",
            drop = drop.drop,
            candidates = target_candidates,
            drop_candidates = drop_candidates,
            drop_loc = drop.loc,
            metrics = drop,
            actor_loc = actor_loc,
            params = {
                direction = drop.direction,
                vertical = 0,
                target_x = number_or((drop.drop and drop.drop.x), nil),
                target_y = number_or((drop.drop and drop.drop.y), nil),
                dx = drop.dx,
                reason = "platform_move_to_drop"
            },
            debug = {
                resources_count = #drop_candidates,
                total_drops = count(world.nearby_resources),
                target_x = drop.drop and drop.drop.x,
                target_y = drop.drop and drop.drop.y,
                drop_dx = drop.dx,
                drop_raw_y_delta = drop.dy,
                drop_platform_y_delta = drop.loc and drop.loc.y_delta
            }
        })
    end

    return proposal({
        action = "Wait",
        reason = pickup_only and "platform_no_drop_for_pickup" or "platform_no_target_or_drop",
        candidates = target_candidates,
        drop_candidates = {},
        actor_loc = actor_loc,
        confidence = 0.4,
        params = { seconds = number_or(cfg.tick_ms, 120) / 1000 },
        debug = {
            total_targets = count(world.nearby_targets),
            total_drops = count(world.nearby_resources)
        }
    })
end

return PlatformRuntime
