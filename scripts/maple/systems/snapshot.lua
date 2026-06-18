local Snapshot = {}
Snapshot.__index = Snapshot

function Snapshot.new(cfg, logger)
    return setmetatable({ cfg = cfg, logger = logger, items = {} }, Snapshot)
end

function Snapshot:summarize(bb)
    return {
        tick = bb.runtime.tick,
        active_goal = bb.task.active_goal,
        action_queue_size = #(bb.action_queue or {}),
        failure_count = bb.task.failure_count,
        last_error = bb.runtime.last_error
    }
end

function Snapshot:maybe_save(bb)
    if not self.cfg.snapshot.enabled then return nil end
    if bb.runtime.tick % self.cfg.snapshot.interval_ticks ~= 0 then return nil end
    local item = self:summarize(bb)
    self.items[#self.items + 1] = item
    while #self.items > self.cfg.snapshot.max_snapshots do table.remove(self.items, 1) end
    if self.logger then self.logger:info("snapshot_saved", item, bb) end
    return item
end

return Snapshot
