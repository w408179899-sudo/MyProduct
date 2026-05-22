local M = {}

M.SUCCESS = "success"
M.RUNNING = "running"
M.FAILURE = "failure"
M.SKIPPED = "skipped"
M.BLOCKED = "blocked"

M.DEFAULT_NODE_ORDER = {
    "config.validation",
    "blackboard.refresh",
    "character.persistence",
    "runtime.guard",
    "startup.resolve",
    "recovery.critical",
    "owner.resume",
    "abort.cleanup",
    "interaction.hard_flow",
    "maintenance.after_loot",
    "maintenance.level_up",
    "treasure.runtime",
    "task.detail_recovery",
    "map.runtime_detection",
    "task.info_runtime_gate",
    "task.package_blocking_side_task",
    "task.intent",
    "task.path_acquire",
    "task.follow",
    "task.reached_action",
    "task.combat_completion",
    "ui.low_priority",
    "idle"
}

M.SERVICE_NODE_ORDER = {
    "service.button_registry_live_locator",
    "service.combat_pulse",
    "service.potion_watch",
    "service.text_image_probe",
    "service.nav_worker",
    "service.persistence_save"
}

local function now_value(current_time)
    local value = tonumber(current_time)
    if value ~= nil then
        return value
    end
    if type(os) == "table" and type(os.clock) == "function" then
        return math.floor(os.clock() * 1000)
    end
    return 0
end

local function ensure_runtime(root)
    if type(root) ~= "table" then
        return {}
    end
    if type(root.bt_runtime) ~= "table" then
        root.bt_runtime = {
            tick_id = 0,
            active_owner = nil,
            active_node = nil,
            active_since = 0,
            last_node = nil,
            last_result = nil,
            last_owner = nil,
            last_at = 0,
            history = {}
        }
    end
    return root.bt_runtime
end

local function normalize_result(result)
    local text = tostring(result or "")
    if text == M.SUCCESS or text == M.RUNNING or text == M.FAILURE or text == M.SKIPPED or text == M.BLOCKED then
        return text
    end
    if result == true then
        return M.SUCCESS
    end
    if result == false or result == nil then
        return M.SKIPPED
    end
    return text
end

function M.result_from_legacy_handled(handled)
    if handled then
        return M.RUNNING
    end
    return M.SKIPPED
end

function M.is_terminal(result)
    local value = normalize_result(result)
    return value == M.SUCCESS or value == M.FAILURE or value == M.SKIPPED
end

function M.is_active_result(result)
    return normalize_result(result) == M.RUNNING
end

local function current_time_from_blackboard(blackboard)
    if type(blackboard) == "table" then
        return blackboard.current_time or blackboard.now or blackboard.time
    end
    return nil
end

local function call_optional(fn, ...)
    if type(fn) == "function" then
        return fn(...)
    end
    return nil
end

local function scheduler_from_opts(opts)
    opts = type(opts) == "table" and opts or {}
    if type(opts.scheduler) == "table" then
        return opts.scheduler
    end
    local blackboard = type(opts.blackboard) == "table" and opts.blackboard or nil
    if type(blackboard) == "table" and type(blackboard.scheduler) == "table" then
        return blackboard.scheduler
    end
    return {}
end

local function scheduler_allows_owner(scheduler, owner)
    scheduler = type(scheduler) == "table" and scheduler or {}
    owner = tostring(owner or "")
    if owner == "" then
        return true, "ownerless"
    end

    local scheduler_owner = tostring(scheduler.owner or "")
    if scheduler_owner == "" or scheduler_owner == "idle" then
        return true, "scheduler_idle"
    end

    local allowed = type(scheduler.allowed_owners) == "table" and scheduler.allowed_owners or nil
    if type(allowed) == "table" and allowed[owner] == true then
        return true, "allowed_owner"
    end

    if scheduler.allows_combat_sidecar == true
        and (owner == "combat" or owner == "task_combat")
    then
        return true, "allowed_combat_sidecar"
    end

    if scheduler_owner == owner then
        return true, "same_owner"
    end

    return false, "scheduler_owner:" .. scheduler_owner .. ":actual_owner:" .. owner
end

function M.begin_tick(root, current_time)
    local runtime = ensure_runtime(root)
    runtime.tick_id = (tonumber(runtime.tick_id) or 0) + 1
    runtime.tick_started_at = now_value(current_time)
    return runtime
end

function M.active_owner(root)
    local runtime = ensure_runtime(root)
    return runtime.active_owner, runtime.active_node
end

function M.clear_owner(root, reason, current_time)
    local runtime = ensure_runtime(root)
    runtime.last_cleared_owner = runtime.active_owner
    runtime.last_cleared_node = runtime.active_node
    runtime.last_clear_reason = tostring(reason or "")
    runtime.last_clear_at = now_value(current_time)
    runtime.active_owner = nil
    runtime.active_node = nil
    runtime.active_since = 0
    return runtime
end

function M.record(root, key, result, opts)
    local runtime = ensure_runtime(root)
    opts = type(opts) == "table" and opts or {}

    local node_key = tostring(key or "")
    local owner = tostring(opts.owner or "")
    local node_result = normalize_result(result)
    local current_time = now_value(opts.current_time)
    local transient = opts.transient == true
    local scheduler = scheduler_from_opts(opts)
    local scheduler_owner = tostring(opts.scheduler_owner or scheduler.owner or "")
    local scheduler_node = tostring(opts.scheduler_node or scheduler.node or "")
    local scheduler_reason = tostring(opts.scheduler_reason or scheduler.reason or "")
    local scheduler_priority = tonumber(opts.scheduler_priority or scheduler.priority)
    local scheduler_allows, scheduler_mismatch_reason = scheduler_allows_owner(scheduler, owner)

    runtime.last_node = node_key
    runtime.last_result = node_result
    runtime.last_owner = owner ~= "" and owner or nil
    runtime.last_at = current_time
    runtime.last_stage = tostring(opts.stage or "")
    runtime.last_reason = tostring(opts.reason or "")
    runtime.last_scheduler_owner = scheduler_owner ~= "" and scheduler_owner or nil
    runtime.last_scheduler_node = scheduler_node ~= "" and scheduler_node or nil
    runtime.last_scheduler_reason = scheduler_reason ~= "" and scheduler_reason or nil
    runtime.last_scheduler_priority = scheduler_priority
    runtime.last_scheduler_allows_owner = scheduler_allows
    runtime.last_scheduler_record_mismatch_reason = scheduler_allows and nil or scheduler_mismatch_reason

    local active_mismatch = scheduler_allows == false
        and owner ~= ""
        and node_result ~= M.SKIPPED
    if active_mismatch then
        runtime.scheduler_mismatch_count = (tonumber(runtime.scheduler_mismatch_count) or 0) + 1
        runtime.last_scheduler_mismatch_at = current_time
        runtime.last_scheduler_mismatch_node = node_key
        runtime.last_scheduler_mismatch_owner = owner
        runtime.last_scheduler_mismatch_result = node_result
        runtime.last_scheduler_mismatch_reason = scheduler_mismatch_reason
    end

    if owner ~= "" and not transient and node_result == M.RUNNING then
        if runtime.active_owner ~= owner or runtime.active_node ~= node_key then
            runtime.active_since = current_time
        end
        runtime.active_owner = owner
        runtime.active_node = node_key
    elseif owner ~= ""
        and runtime.active_owner == owner
        and runtime.active_node == node_key
        and (node_result == M.SUCCESS or node_result == M.FAILURE or node_result == M.SKIPPED)
    then
        runtime.active_owner = nil
        runtime.active_node = nil
        runtime.active_since = 0
    end

    local history = type(runtime.history) == "table" and runtime.history or {}
    runtime.history = history
    history[#history + 1] = {
        key = node_key,
        owner = owner ~= "" and owner or nil,
        result = node_result,
        at = current_time,
        stage = runtime.last_stage,
        reason = runtime.last_reason,
        scheduler_owner = runtime.last_scheduler_owner,
        scheduler_node = runtime.last_scheduler_node,
        scheduler_reason = runtime.last_scheduler_reason,
        scheduler_priority = runtime.last_scheduler_priority,
        scheduler_allows_owner = runtime.last_scheduler_allows_owner,
        scheduler_mismatch_reason = runtime.last_scheduler_record_mismatch_reason,
        scheduler_active_mismatch = active_mismatch
    }
    while #history > 32 do
        table.remove(history, 1)
    end

    return node_result, runtime
end

function M.node(def)
    def = type(def) == "table" and def or {}
    local node = {
        kind = tostring(def.kind or "node"),
        key = tostring(def.key or ""),
        owner = tostring(def.owner or ""),
        priority = tonumber(def.priority) or 0,
        service = def.service == true,
        transient = def.transient == true,
        can_enter = def.can_enter,
        enter = def.enter,
        tick = def.tick,
        abort = def.abort,
        children = def.children
    }

    function node:run(root, ctx, blackboard)
        return M.run_node(root, self, ctx, blackboard)
    end

    function node:abort_node(root, ctx, blackboard, reason)
        return M.abort_node(root, self, ctx, blackboard, reason)
    end

    return node
end

function M.abort_node(root, node, ctx, blackboard, reason)
    node = type(node) == "table" and node or {}
    local current_time = current_time_from_blackboard(blackboard)
    call_optional(node.abort, ctx, blackboard, reason)
    M.clear_owner(root, reason or ("abort:" .. tostring(node.key or "")), current_time)
    return M.record(root, tostring(node.key or ""), M.FAILURE, {
        owner = node.owner,
        current_time = current_time,
        reason = reason or "abort",
        blackboard = blackboard
    })
end

function M.owner_available(root, node)
    node = type(node) == "table" and node or {}
    local owner = tostring(node.owner or "")
    if owner == "" or node.service == true then
        return true
    end
    local runtime = ensure_runtime(root)
    local active_owner = tostring(runtime.active_owner or "")
    local active_node = tostring(runtime.active_node or "")
    if active_owner == "" then
        return true
    end
    if active_owner == owner then
        return true
    end
    return false, string.format("owner_active:%s:%s", active_owner, active_node)
end

function M.run_node(root, node, ctx, blackboard)
    node = type(node) == "table" and node or {}
    blackboard = type(blackboard) == "table" and blackboard or {}
    blackboard.root = root
    local current_time = current_time_from_blackboard(blackboard)
    local node_key = tostring(node.key or "")
    local owner = tostring(node.owner or "")

    if blackboard.bt_active_owner_enforce == true then
        local owner_ok, owner_reason = M.owner_available(root, node)
        if not owner_ok then
            return M.record(root, node_key, M.BLOCKED, {
                owner = owner,
                current_time = current_time,
                reason = owner_reason,
                blackboard = blackboard
            })
        end
    end

    local can_enter, can_enter_reason = call_optional(node.can_enter, ctx, blackboard)
    if can_enter == false or can_enter == M.SKIPPED or can_enter == M.BLOCKED then
        return M.record(root, node_key, can_enter == M.BLOCKED and M.BLOCKED or M.SKIPPED, {
            owner = owner,
            current_time = current_time,
            reason = tostring(can_enter_reason or ""),
            blackboard = blackboard
        })
    end

    local runtime = ensure_runtime(root)
    local was_active = runtime.active_owner == owner and runtime.active_node == node_key
    if owner ~= "" and not was_active then
        call_optional(node.enter, ctx, blackboard)
    elseif owner == "" and node.service ~= true then
        call_optional(node.enter, ctx, blackboard)
    end

    local tick_fn = type(node.tick) == "function" and node.tick or nil
    local result, reason
    if tick_fn ~= nil then
        result, reason = tick_fn(ctx, blackboard)
    else
        result, reason = M.SKIPPED, "missing tick"
    end

    return M.record(root, node_key, result, {
        owner = owner,
        current_time = current_time,
        reason = reason,
        transient = node.transient == true,
        blackboard = blackboard
    })
end

function M.legacy_bool_node(def)
    def = type(def) == "table" and def or {}
    local legacy_tick = def.tick or def.fn
    def.kind = def.kind or "legacy_bool"
    def.tick = function(ctx, blackboard)
        if type(legacy_tick) ~= "function" then
            return M.FAILURE, "missing legacy handler"
        end
        local handled, reason = legacy_tick(ctx, blackboard)
        return M.result_from_legacy_handled(handled), reason
    end
    return M.node(def)
end

local function normalize_children(children)
    if type(children) ~= "table" then
        return {}
    end
    return children
end

function M.priority_selector(def)
    def = type(def) == "table" and def or {}
    local children = normalize_children(def.children)
    table.sort(children, function(a, b)
        return (tonumber(a and a.priority) or 0) < (tonumber(b and b.priority) or 0)
    end)
    def.kind = "priority_selector"
    def.children = children
    def.tick = function(ctx, blackboard)
        local first_blocked_reason = nil
        for _, child in ipairs(children) do
            local result, reason = M.run_node(blackboard.root, child, ctx, blackboard)
            if result == M.RUNNING or result == M.SUCCESS then
                return result
            elseif result == M.BLOCKED and first_blocked_reason == nil then
                first_blocked_reason = reason or "blocked"
            end
        end
        if first_blocked_reason ~= nil then
            return M.BLOCKED, first_blocked_reason
        end
        return M.SKIPPED
    end
    return M.node(def)
end

function M.selector(def)
    def = type(def) == "table" and def or {}
    local children = normalize_children(def.children)
    def.kind = "selector"
    def.children = children
    def.tick = function(ctx, blackboard)
        for _, child in ipairs(children) do
            local result = M.run_node(blackboard.root, child, ctx, blackboard)
            if result == M.RUNNING or result == M.SUCCESS or result == M.BLOCKED then
                return result
            end
        end
        return M.SKIPPED
    end
    return M.node(def)
end

function M.sequence(def)
    def = type(def) == "table" and def or {}
    local children = normalize_children(def.children)
    def.kind = "sequence"
    def.children = children
    def.tick = function(ctx, blackboard)
        for _, child in ipairs(children) do
            local result = M.run_node(blackboard.root, child, ctx, blackboard)
            if result ~= M.SUCCESS then
                return result
            end
        end
        return M.SUCCESS
    end
    return M.node(def)
end

function M.owner_guard(owner, child)
    return M.node({
        kind = "owner_guard",
        key = "owner_guard." .. tostring(owner or ""),
        owner = owner,
        tick = function(ctx, blackboard)
            if type(child) ~= "table" then
                return M.FAILURE, "missing child"
            end
            return M.run_node(blackboard.root, child, ctx, blackboard)
        end
    })
end

function M.service(def)
    def = type(def) == "table" and def or {}
    def.kind = def.kind or "service"
    def.service = true
    def.owner = ""
    def.transient = true
    return M.node(def)
end

function M.service_selector(def)
    def = type(def) == "table" and def or {}
    local children = normalize_children(def.children)
    table.sort(children, function(a, b)
        return (tonumber(a and a.priority) or 0) < (tonumber(b and b.priority) or 0)
    end)
    def.kind = "service_selector"
    def.service = true
    def.owner = ""
    def.transient = true
    def.children = children
    def.tick = function(ctx, blackboard)
        local any_success = false
        for _, child in ipairs(children) do
            local result = M.run_node(blackboard.root, child, ctx, blackboard)
            if result == M.SUCCESS or result == M.RUNNING then
                any_success = true
            end
        end
        if any_success then
            return M.SUCCESS
        end
        return M.SKIPPED
    end
    return M.node(def)
end

function M.cooldown(def)
    def = type(def) == "table" and def or {}
    local child = def.child
    local cooldown_ms = math.max(0, tonumber(def.cooldown_ms) or 0)
    return M.node({
        kind = "cooldown",
        key = tostring(def.key or "cooldown"),
        owner = tostring(def.owner or ""),
        tick = function(ctx, blackboard)
            local root = blackboard.root
            local runtime = ensure_runtime(root)
            local cooldowns = type(runtime.cooldowns) == "table" and runtime.cooldowns or {}
            runtime.cooldowns = cooldowns
            local key = tostring(def.key or "cooldown")
            local current_time = now_value(current_time_from_blackboard(blackboard))
            if current_time < (tonumber(cooldowns[key]) or 0) then
                return M.BLOCKED, "cooldown"
            end
            local result = M.run_node(root, child, ctx, blackboard)
            if result == M.SUCCESS or result == M.FAILURE then
                cooldowns[key] = current_time + cooldown_ms
            end
            return result
        end
    })
end

function M.timeout(def)
    def = type(def) == "table" and def or {}
    local child = def.child
    local timeout_ms = math.max(0, tonumber(def.timeout_ms) or 0)
    return M.node({
        kind = "timeout",
        key = tostring(def.key or "timeout"),
        owner = tostring(def.owner or ""),
        tick = function(ctx, blackboard)
            local root = blackboard.root
            local runtime = ensure_runtime(root)
            local deadlines = type(runtime.deadlines) == "table" and runtime.deadlines or {}
            runtime.deadlines = deadlines
            local key = tostring(def.key or "timeout")
            local current_time = now_value(current_time_from_blackboard(blackboard))
            if (tonumber(deadlines[key]) or 0) == 0 then
                deadlines[key] = current_time + timeout_ms
            elseif timeout_ms > 0 and current_time > deadlines[key] then
                deadlines[key] = nil
                M.abort_node(root, child, ctx, blackboard, "timeout")
                return M.FAILURE, "timeout"
            end
            local result = M.run_node(root, child, ctx, blackboard)
            if result ~= M.RUNNING then
                deadlines[key] = nil
            end
            return result
        end
    })
end

function M.retry(def)
    def = type(def) == "table" and def or {}
    local child = def.child
    local max_attempts = math.max(1, tonumber(def.max_attempts) or 1)
    return M.node({
        kind = "retry",
        key = tostring(def.key or "retry"),
        owner = tostring(def.owner or ""),
        tick = function(ctx, blackboard)
            local root = blackboard.root
            local runtime = ensure_runtime(root)
            local attempts = type(runtime.retry_attempts) == "table" and runtime.retry_attempts or {}
            runtime.retry_attempts = attempts
            local key = tostring(def.key or "retry")
            local result = M.run_node(root, child, ctx, blackboard)
            if result == M.FAILURE then
                attempts[key] = (tonumber(attempts[key]) or 0) + 1
                if attempts[key] < max_attempts then
                    M.clear_owner(root, "retry", current_time_from_blackboard(blackboard))
                    return M.RUNNING, "retry"
                end
                attempts[key] = nil
            elseif result == M.SUCCESS or result == M.SKIPPED then
                attempts[key] = nil
            end
            return result
        end
    })
end

return M
