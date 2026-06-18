local Uuid = require("maple.core.uuid")

local ActionQueue = {}
ActionQueue.__index = ActionQueue

function ActionQueue.new(cfg, logger)
    return setmetatable({
        cfg = cfg,
        logger = logger,
        items = {}
    }, ActionQueue)
end

function ActionQueue:size()
    return #self.items
end

function ActionQueue:push(bb, spec, name, params)
    local max_size = tonumber(self.cfg.limits.max_queue_size) or 20
    if #self.items >= max_size then
        return nil, "queue_full"
    end
    local action = {
        id = Uuid.next("action"),
        name = name,
        params = params or {},
        status = "queued",
        created_tick = bb.runtime.tick,
        started_tick = nil,
        ended_tick = nil,
        timeout = self.cfg.timeouts[spec.timeout] or self.cfg.timeouts.action,
        retry_count = 0,
        max_retries = tonumber(spec.max_retries) or 0,
        result = nil,
        error = nil
    }
    self.items[#self.items + 1] = action
    if self.logger then self.logger:info("action_queued", { id = action.id, name = name }, bb) end
    return action
end

function ActionQueue:pop()
    if #self.items == 0 then return nil end
    return table.remove(self.items, 1)
end

function ActionQueue:clear()
    self.items = {}
end

return ActionQueue
