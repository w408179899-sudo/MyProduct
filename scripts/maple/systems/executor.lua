local Specs = require("maple.data.action_specs")
local ActionQueue = require("maple.systems.action_queue")

local Executor = {}
Executor.__index = Executor

local function missing_required(spec, params)
    for _, name in ipairs(spec.required_params or {}) do
        if params == nil or params[name] == nil then return name end
    end
    return nil
end

function Executor.new(environment, cfg, logger)
    return setmetatable({
        environment = environment,
        cfg = cfg,
        logger = logger,
        queue = ActionQueue.new(cfg, logger)
    }, Executor)
end

function Executor:queue_action(bb, name, params)
    local spec = Specs[name]
    if not spec then
        if self.logger then self.logger:error("action_failure", { name = name, reason = "unknown_action" }, bb) end
        return nil, "unknown_action"
    end
    local missing = missing_required(spec, params or {})
    if missing then
        if self.logger then self.logger:error("action_failure", { name = name, reason = "missing_param", param = missing }, bb) end
        return nil, "missing_param:" .. missing
    end
    return self.queue:push(bb, spec, name, params)
end

function Executor:has_pending()
    return self.queue:size() > 0
end

function Executor:flush(bb)
    bb.metrics.current_action_queue_size = self.queue:size()
    if bb.safety.circuit_breaker_open and bb.task.active_goal ~= "safety" and bb.task.active_goal ~= "recovery" then
        self.queue:clear()
        return
    end
    local action = self.queue:pop()
    if not action then return end
    bb.task.active_action = action
    bb.task.action_id = action.id
    action.status = "started"
    action.started_tick = bb.runtime.tick
    if self.logger then self.logger:info("action_started", { id = action.id, name = action.name }, bb) end

    local ok, result = pcall(function()
        return self.environment:perform_action(action, bb)
    end)

    if not ok then
        result = { ok = false, reason = tostring(result) }
    end
    if result and result.status == "running" then
        action.status = "running"
        action.result = result
        self.queue.items[#self.queue.items + 1] = action
        if self.logger then self.logger:debug("action_running", { id = action.id, name = action.name }, bb) end
        return
    end
    action.ended_tick = bb.runtime.tick
    action.result = result
    if result and result.ok == true then
        action.status = "success"
        bb.metrics.action_success_count = bb.metrics.action_success_count + 1
        bb.task.last_result = result
        if self.logger then self.logger:info("action_success", { id = action.id, name = action.name }, bb) end
    else
        action.status = "failure"
        action.error = result and result.reason or "failed"
        bb.task.failure_count = bb.task.failure_count + 1
        bb.metrics.action_failure_count = bb.metrics.action_failure_count + 1
        bb.task.last_result = result
        if self.logger then self.logger:warn("action_failure", { id = action.id, name = action.name, reason = action.error }, bb) end
    end
    bb.task.active_action = nil
end

return Executor
