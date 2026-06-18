local Metrics = {}

function Metrics.tick(bb, executor)
    bb.metrics.tick_count = bb.runtime.tick
    bb.metrics.current_action_queue_size = executor and executor.queue and executor.queue:size() or 0
end

return Metrics
