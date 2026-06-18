local Bootstrap = require("maple.bootstrap")

local Main = {}

function Main.run(opts)
    opts = opts or {}
    local system = Bootstrap.new(opts)
    system.logger:info("program_started", { account_index = opts.account_index }, system.blackboard)
    local max_ticks = tonumber(opts.max_ticks) or nil
    while system.blackboard.runtime.running do
        Bootstrap.tick(system)
        if max_ticks and system.blackboard.runtime.tick >= max_ticks then break end
        if sys and sys.sleep then sys.sleep(system.config.tick_interval_ms) else break end
    end
    system.logger:info("program_stopped", { tick = system.blackboard.runtime.tick }, system.blackboard)
    return system
end

return Main
