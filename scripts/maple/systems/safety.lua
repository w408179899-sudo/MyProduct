local Safety = {}
Safety.__index = Safety

function Safety.new(cfg, logger)
    return setmetatable({ cfg = cfg, logger = logger }, Safety)
end

function Safety:trigger(bb, reason)
    bb.safety.last_trigger = reason
    bb.safety.circuit_breaker_open = true
    bb.metrics.safety_trigger_count = bb.metrics.safety_trigger_count + 1
    if self.logger then self.logger:error("circuit_breaker_opened", { reason = reason }, bb) end
end

function Safety:check(bb)
    if bb.runtime.stop_requested then
        self:trigger(bb, "manual_stop")
        return
    end
    if (tonumber(bb.task.failure_count) or 0) >= (tonumber(self.cfg.limits.max_failures) or 5) then
        self:trigger(bb, "too_many_failures")
        return
    end
    if bb.navigation.is_stuck == true and (tonumber(bb.navigation.stuck_ticks) or 0) >= self.cfg.limits.max_stuck_ticks then
        self:trigger(bb, "stuck_timeout")
    end
end

return Safety
