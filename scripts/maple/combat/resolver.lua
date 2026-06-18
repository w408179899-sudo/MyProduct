local Resolver = {}

local function number_or(value, fallback)
    local n = tonumber(value)
    if n == nil then return fallback end
    return n
end

local function clone_position(entity)
    entity = entity or {}
    return {
        x = number_or(entity.x, 0),
        y = number_or(entity.y, 0),
        z = number_or(entity.z, 0)
    }
end

local function velocity(entity)
    entity = entity or {}
    return {
        x = number_or(entity.vx, 0),
        y = number_or(entity.vy, 0),
        z = number_or(entity.vz, 0)
    }
end

local function elapsed_ms(started_at)
    if not started_at or not os or not os.clock then return 0 end
    return (os.clock() - started_at) * 1000
end

local function over_budget(context)
    local budget = number_or(context.budget_ms, 0)
    return budget > 0 and elapsed_ms(context.started_at) > budget
end

local function distance_score(actor, target)
    local dx = math.abs(number_or(target.x, 0) - number_or(actor.x, 0))
    local dy = math.abs(number_or(target.y, 0) - number_or(actor.y, 0))
    return dx + dy
end

local function predict_position(entity, seconds)
    local pos = clone_position(entity)
    local vel = velocity(entity)
    return {
        x = pos.x + vel.x * seconds,
        y = pos.y + vel.y * seconds,
        z = pos.z + vel.z * seconds
    }
end

local function in_skill_box(actor, pos, cfg)
    local dx = math.abs(number_or(pos.x, 0) - number_or(actor.x, 0))
    local dy = math.abs(number_or(pos.y, 0) - number_or(actor.y, 0))
    return dx <= number_or(cfg.default_skill_range_x, 120)
        and dy <= number_or(cfg.default_skill_range_y, 50)
end

local function trim_targets(context)
    local actor = context.actor_position or { x = 0, y = 0, z = 0 }
    local ranked = {}
    for _, target in ipairs(context.targets or {}) do
        ranked[#ranked + 1] = {
            target = target,
            score = distance_score(actor, target)
        }
    end
    table.sort(ranked, function(a, b) return a.score < b.score end)

    local limit = math.max(1, number_or(context.cfg and context.cfg.max_candidate_targets, #ranked))
    local trimmed = {}
    for i = 1, math.min(limit, #ranked) do
        trimmed[#trimmed + 1] = ranked[i].target
    end
    return trimmed
end

local function proposal(fields)
    fields = fields or {}
    fields.intent = fields.intent or fields.action or "wait"
    fields.action = fields.action or fields.intent
    fields.executable = fields.executable ~= false
    fields.confidence = fields.confidence or 1.0
    fields.risk = fields.risk or 0
    fields.reason = fields.reason or "resolved"
    fields.params = fields.params or {}
    return fields
end

function Resolver.immediate(context)
    context = context or {}
    local actor = context.actor_position or { x = 0, y = 0, z = 0 }
    local cfg = context.cfg or {}
    local best, best_score = nil, math.huge
    local targets = trim_targets(context)

    for _, target in ipairs(targets) do
        local score = distance_score(actor, target)
        if score < best_score then
            best, best_score = target, score
        end
    end

    if not best then
        return proposal({
            mode = "immediate",
            action = "wait",
            executable = false,
            confidence = 0.2,
            reason = "no_target",
            candidate_count = 0
        })
    end

    return proposal({
        mode = "immediate",
        action = "cast_skill",
        skill_id = cfg.default_skill_id,
        target_id = best.id,
        target_position = clone_position(best),
        score = -best_score,
        reason = "nearest_current_target",
        candidate_count = #targets,
        params = {
            skill_id = cfg.default_skill_id,
            target_id = best.id
        }
    })
end

function Resolver.predictive(context)
    context = context or {}
    local cfg = context.cfg or {}
    local actor = context.actor_position or { x = 0, y = 0, z = 0 }
    local horizon = math.max(0.5, number_or(cfg.prediction_horizon_seconds, 2.0))
    local step = math.max(0.1, number_or(cfg.prediction_step_seconds, 0.25))
    local windup = math.max(0, number_or(cfg.default_skill_windup_seconds, 0.5))
    local targets = trim_targets(context)
    local best = nil

    local t = windup
    while t <= horizon do
        if over_budget(context) then
            return proposal({
                mode = "predictive",
                action = "fallback",
                executable = false,
                confidence = 0.1,
                score = 0,
                horizon_seconds = horizon,
                candidate_count = #targets,
                fallback_requested = true,
                fallback_reason = "budget_exceeded",
                reason = "budget_exceeded"
            })
        end

        local hits = {}
        for _, target in ipairs(targets) do
            local predicted = predict_position(target, t)
            if in_skill_box(actor, predicted, cfg) then
                hits[#hits + 1] = {
                    id = target.id,
                    predicted_position = predicted
                }
            end
        end

        local candidate = {
            hit_time = t,
            hits = hits,
            score = #hits
        }
        if not best or candidate.score > best.score then best = candidate end
        t = t + step
    end

    if not best or best.score <= 0 then
        return proposal({
            mode = "predictive",
            action = "reposition_or_wait",
            executable = false,
            confidence = 0.4,
            score = 0,
            horizon_seconds = horizon,
            candidate_count = #targets,
            reason = "no_predicted_hit"
        })
    end

    return proposal({
        mode = "predictive",
        action = "cast_skill",
        skill_id = cfg.default_skill_id,
        hit_time = best.hit_time,
        predicted_hits = best.hits,
        score = best.score,
        horizon_seconds = horizon,
        candidate_count = #targets,
        reason = "best_predicted_hit_count",
        params = {
            skill_id = cfg.default_skill_id,
            hit_time = best.hit_time,
            predicted_hits = best.hits
        }
    })
end

function Resolver.resolve(context)
    if context and context.mode == "predictive" then return Resolver.predictive(context) end
    return Resolver.immediate(context)
end

return Resolver
