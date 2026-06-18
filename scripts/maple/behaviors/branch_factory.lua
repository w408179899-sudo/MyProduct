local BT = require("maple.bt.constants")

local M = {}

function M.new(goal_id, priority, action_name, params_fn, executor, logger)
    return {
        name = goal_id .. "_branch",
        priority = priority,
        tick = function(self, bb)
            if bb.task.active_goal ~= goal_id then return BT.FAILURE end
            bb.debug.last_branch = self.name
            if logger then logger:debug("branch_selected", { branch = self.name, goal = goal_id }, bb) end
            if executor:has_pending() then return BT.RUNNING end
            local params = params_fn and params_fn(bb) or {}
            local ok = executor:queue_action(bb, action_name, params)
            return ok and BT.RUNNING or BT.FAILURE
        end
    }
end

return M
