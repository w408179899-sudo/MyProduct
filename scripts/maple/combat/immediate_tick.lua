local Resolver = require("maple.combat.resolver")

local ImmediateTick = {}

local function actor_position(bb)
    return bb.actor and bb.actor.position or { x = 0, y = 0, z = 0 }
end

function ImmediateTick.build_context(bb, cfg)
    return {
        mode = "immediate",
        actor_position = actor_position(bb),
        targets = bb.world and bb.world.nearby_targets or {},
        cfg = cfg or {},
        budget_ms = cfg and cfg.immediate_budget_ms,
        started_at = os and os.clock and os.clock() or nil
    }
end

function ImmediateTick.decide(bb, cfg)
    return Resolver.resolve(ImmediateTick.build_context(bb, cfg))
end

return ImmediateTick
