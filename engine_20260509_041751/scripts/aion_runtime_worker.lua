--[[
    Aion runtime worker for manually logged-in multi-client mode.

    One worker is started per account row. It verifies that AionData is bound
    to the account's selected PID before reporting running state. The worker
    currently publishes runtime snapshots and is the place where the later
    Planner/BehaviorTree/Executor loop should be attached.
]]

local ok_core, core = pcall(require, "aion.core")
local ok_map, map = pcall(require, "aion.map")
local ok_inventory, inventory = pcall(require, "aion.inventory")
local ok_target, target = pcall(require, "aion.target")

local runtime_key = tostring(runtime_key or "default")
local account_index = tonumber(account_index or "0") or 0

local stopped = false

local function share_key(field)
    return "aion_runtime." .. runtime_key .. "." .. tostring(field)
end

local function set_share(field, value)
    if sys and type(sys.set_share) == "function" then
        sys.set_share(share_key(field), value)
    end
end

local function set_status(status, message)
    set_share("status", tostring(status or "unknown"))
    set_share("message", tostring(message or ""))
    set_share("updated_at", os.time())
end

local function sleep(ms)
    if sys and sys.sleep then
        sys.sleep(ms)
    end
end

local function now_ms()
    if sys and type(sys.time) == "function" then
        return sys.time()
    end
    return os.time() * 1000
end

local function load_account()
    if not config or not config.load or not config.get then
        return nil, "config module unavailable"
    end
    config.load()
    local accounts = config.get("aion_control.accounts", {})
    if type(accounts) ~= "table" or type(accounts.items) ~= "table" then
        return nil, "accounts config unavailable"
    end
    local account = accounts.items[account_index]
    if type(account) ~= "table" then
        return nil, "account index not found: " .. tostring(account_index)
    end
    return account, nil
end

local function publish_target(account)
    local t = account.target or {}
    set_share("expected_pid", tonumber(t.pid) or 0)
    set_share("expected_hwnd", tonumber(t.hwnd) or 0)
    set_share("target_title", tostring(t.title or ""))
end

local function count_route_points(text)
    local count = 0
    for line in string.gmatch(tostring(text or ""), "[^\r\n]+") do
        if tostring(line):match("%S") then
            count = count + 1
        end
    end
    return count
end

local function load_route_config()
    if not config or not config.load or not config.get then
        return {}
    end

    config.load()
    return config.get("aion_control.route", {}) or {}
end

local function publish_route_plan(route_cfg)
    route_cfg = route_cfg or {}
    local specs = {
        { key = "grind", name = "route_name", points = "route_points" },
        { key = "revive", name = "revive_route_name", points = "revive_points" },
        { key = "supply", name = "vendor_route_name", points = "vendor_points" },
        { key = "gather", name = "gather_route_name", points = "gather_points" },
        { key = "main", name = "leveling_route_name", points = "leveling_points" },
    }

    local total = 0
    for _, spec in ipairs(specs) do
        local name = tostring(route_cfg[spec.name] or "")
        local points = count_route_points(route_cfg[spec.points] or "")
        total = total + points
        set_share("route_" .. spec.key .. "_name", name)
        set_share("route_" .. spec.key .. "_points", points)
    end

    return total
end

local function validate_binding(account)
    if not ok_core or not core then
        return false, "aion.core unavailable"
    end
    if not ok_target or not target then
        return false, "aion.target unavailable"
    end

    local expected_pid = tonumber(account.target and account.target.pid) or 0
    if expected_pid <= 0 then
        return false, "account target pid is not selected"
    end

    local init_ok, init_err = core.ensureInit(expected_pid)
    if not init_ok then
        return false, "AionData init failed: " .. tostring(init_err)
    end

    local matched, status, message, state = target.validate_binding(account.target, core)
    if state then
        set_share("bound_pid", tonumber(state.pid) or 0)
        set_share("bound_hwnd", tonumber(state.hwnd) or 0)
    end
    if not matched then
        return false, tostring(status or "binding_failed") .. ": " .. tostring(message)
    end
    return true, "matched"
end

local function publish_snapshot(started_ms)
    if ok_core and core then
        local ok, char = core.getCharacter()
        if ok and char then
            set_share("character_name", tostring(char.name or ""))
            set_share("level", tonumber(char.level) or 0)
            set_share("hp", tonumber(char.hp) or 0)
            set_share("max_hp", tonumber(char.mhp or char.max_hp) or 0)
            set_share("mp", tonumber(char.mp) or 0)
            set_share("max_mp", tonumber(char.mmp or char.max_mp) or 0)
        end
    end

    if ok_map and map then
        local ok, cur_map = map.current()
        if ok and cur_map then
            set_share("map", tostring(cur_map.region or cur_map.name_cn or cur_map.name_en or ""))
        end
    end

    if ok_inventory and inventory then
        local ok, kinah = inventory.kinah()
        if ok then
            set_share("kinah", tonumber(kinah) or 0)
        end
    end

    set_share("runtime_seconds", math.max(0, math.floor((now_ms() - started_ms) / 1000)))
    set_share("updated_at", os.time())
end

local function run()
    if account_index <= 0 then
        set_status("error", "account_index is required")
        return
    end

    if task and task.on_stop then
        task.on_stop(function()
            stopped = true
            set_status("stopping", "stop requested")
        end)
    end

    local account, err = load_account()
    if not account then
        set_status("error", err)
        return
    end
    local route_cfg = load_route_config()
    local route_point_total = publish_route_plan(route_cfg)

    publish_target(account)
    set_status("binding", "validating pid")
    local ok, message = validate_binding(account)
    if not ok then
        set_status("error", message)
        return
    end

    local started_ms = now_ms()
    set_share("started_at", os.time())
    set_status("running", "runtime worker started; route_points=" .. tostring(route_point_total))
    task.set_progress(0.1)

    while not stopped do
        publish_snapshot(started_ms)
        task.set_progress(0.5)
        sleep(2000)
    end

    set_status("stopped", "runtime worker stopped")
    task.set_progress(1.0)
end

local ok, err = pcall(run)
if not ok then
    set_status("error", tostring(err))
    error(err)
end
