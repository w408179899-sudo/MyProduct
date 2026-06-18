local Config = require("maple.config")
local ImmediateTick = require("maple.combat.immediate_tick")
local PredictiveTick = require("maple.combat.predictive_tick")

local CombatManager = {}

local function combat_config(bb)
    local cfg = {}
    for k, v in pairs(Config.combat or {}) do cfg[k] = v end
    if bb.combat and bb.combat.logic_mode then cfg.logic_mode = bb.combat.logic_mode end
    if bb.account then
        if bb.account.smart_combat_enabled == true then
            cfg.logic_mode = "predictive"
        elseif bb.account.combat_logic_mode then
            cfg.logic_mode = bb.account.combat_logic_mode
        end
    end
    return cfg
end

function CombatManager.decide(bb)
    local cfg = combat_config(bb)
    if cfg.logic_mode == "immediate" then
        return ImmediateTick.decide(bb, cfg)
    end
    local proposal = PredictiveTick.decide(bb, cfg)
    if proposal and proposal.fallback_requested then
        local fallback = ImmediateTick.decide(bb, cfg)
        fallback.fallback_from = "predictive"
        fallback.fallback_reason = proposal.fallback_reason or proposal.reason
        fallback.degraded = true
        return fallback
    end
    return proposal
end

function CombatManager.propose(bb)
    return CombatManager.decide(bb)
end

function CombatManager.has_targets(bb)
    return #(bb.world.nearby_targets or {}) > 0
end

return CombatManager
