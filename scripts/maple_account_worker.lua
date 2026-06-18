local cwd = sys and sys.get_cwd and sys.get_cwd() or "."
package.path = cwd .. "/scripts/?.lua;" .. cwd .. "/scripts/?/init.lua;" .. package.path

local Bootstrap = require("maple.bootstrap")
local Store = require("maple.account.store")

local index = tonumber(account_index) or 0
local key = tostring(account_key or "")
local stop_key = Store.status_key(index, "stop")

local root = Store.load()
local account = Store.get(root, index) or Store.new_account({ key = key })

local system = Bootstrap.new({
    account_index = index,
    account_key = key,
    account = account
})

local running = true

local function share(name, value)
    if sys and sys.set_share then sys.set_share(Store.status_key(index, name), value) end
end

local function stop_requested()
    if not sys or not sys.get_share then return false end
    local value = sys.get_share(stop_key)
    return value == true or value == "true" or value == "1"
end

if task and task.on_stop then
    task.on_stop(function()
        running = false
        system.blackboard.runtime.stop_requested = true
    end)
end

share("status", "running")
share("detail", "worker started")
system.logger:info("program_started", { account_index = index, account_key = key }, system.blackboard)

while running and system.blackboard.runtime.running do
    if stop_requested() then
        system.blackboard.runtime.stop_requested = true
        system.blackboard.runtime.running = false
        break
    end

    Bootstrap.tick(system)
    share("tick", tostring(system.blackboard.runtime.tick))
    share("goal", tostring(system.blackboard.task.active_goal or ""))
    share("level", tostring(system.blackboard.actor.level or ""))
    share("status", system.blackboard.safety.circuit_breaker_open and "safety" or "running")

    if task and task.set_progress then
        task.set_progress((system.blackboard.runtime.tick % 100) / 100)
    end
    if sys and sys.sleep then sys.sleep(system.config.tick_interval_ms) else break end
end

share("status", "stopped")
share("detail", system.blackboard.safety.last_trigger or system.blackboard.runtime.last_error or "worker stopped")
system.logger:info("program_stopped", { tick = system.blackboard.runtime.tick }, system.blackboard)

return true
