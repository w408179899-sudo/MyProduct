local Clock = {}
Clock.__index = Clock

function Clock.new()
    return setmetatable({ tick_started_at = 0 }, Clock)
end

function Clock:begin_tick(bb)
    bb.runtime.tick = bb.runtime.tick + 1
    bb.metrics.tick_count = bb.runtime.tick
    self.tick_started_at = os.clock and os.clock() or 0
end

function Clock:end_tick(bb)
    local elapsed = (os.clock and os.clock() or 0) - self.tick_started_at
    local count = math.max(1, bb.metrics.tick_count)
    local prev = tonumber(bb.metrics.average_tick_time) or 0
    bb.metrics.average_tick_time = ((prev * (count - 1)) + elapsed) / count
end

return Clock
