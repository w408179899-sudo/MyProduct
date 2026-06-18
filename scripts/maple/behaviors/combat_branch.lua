local BT = require("maple.bt.constants")
local CombatManager = require("maple.managers.combat_manager")

local CombatBranch = {}

function CombatBranch.new(executor, logger)
    return {
        name = "combat_branch",
        priority = 250,
        tick = function(self, bb)
            if bb.task.active_goal ~= "combat" then return BT.FAILURE end
            bb.debug.last_branch = self.name
            if logger then logger:debug("branch_selected", { branch = self.name, goal = "combat" }, bb) end
            if executor:has_pending() then return BT.RUNNING end

            local proposal = CombatManager.propose(bb)
            bb.combat.last_proposal = proposal
            bb.combat.last_decision = proposal
            bb.combat.logic_mode = proposal.mode
            bb.combat.prediction_horizon_seconds = proposal.horizon_seconds
            bb.combat.candidate_count = proposal.candidate_count or #(bb.world.nearby_targets or {})
            bb.combat.last_fallback_reason = proposal.fallback_reason
            if proposal.degraded then
                bb.metrics.combat_degradation_count = (bb.metrics.combat_degradation_count or 0) + 1
                if logger then
                    logger:warn("combat_degraded", {
                        from = proposal.fallback_from,
                        reason = proposal.fallback_reason
                    }, bb)
                end
            end

            local queued = executor:queue_action(bb, "ExecuteCombatDecision", { proposal = proposal })
            return queued and BT.RUNNING or BT.FAILURE
        end
    }
end

return CombatBranch
