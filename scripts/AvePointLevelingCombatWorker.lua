local function loadfile_with_bytecode_fallback(path, label)
    local candidates = { path }
    if type(path) == "string" and path ~= "" then
        if path:sub(-4):lower() == ".lua" then
            candidates[#candidates + 1] = path:sub(1, -5) .. ".luac"
        elseif path:sub(-5):lower() ~= ".luac" then
            candidates[#candidates + 1] = path .. ".luac"
        end
    end

    local last_err = nil
    for _, candidate in ipairs(candidates) do
        local chunk, err = loadfile(candidate)
        if chunk then
            return chunk
        end
        last_err = err
    end

    error(string.format("load %s failed: %s", tostring(label or path), tostring(last_err)))
end

local function load_nav_module()
    local ok, mod = pcall(require, "torch_nav")
    if ok then
        return mod
    end

    ok, mod = pcall(require, "scripts.torch_nav")
    if ok then
        return mod
    end

    local chunk = loadfile_with_bytecode_fallback("scripts/torch_nav.lua", "torch_nav")
    return chunk()
end

local function as_number(value)
    if type(value) == "number" then
        return value
    end
    if type(value) == "string" then
        return tonumber(value)
    end
    return nil
end

local function as_boolean(value)
    if type(value) == "boolean" then
        return value
    end
    local numeric = as_number(value)
    if numeric ~= nil then
        return numeric ~= 0
    end
    local text = tostring(value or ""):lower()
    return text == "true"
end

local function now_ms()
    if type(sys) == "table" and type(sys.time) == "function" then
        return sys.time()
    end
    return 0
end

local function share_key(prefix, suffix)
    return tostring(prefix or "") .. ":" .. tostring(suffix or "")
end

local running = true

local function is_stop_interrupt(value)
    local message = tostring(value or "")
    return message:find("task stopped", 1, true) ~= nil
        or message:find("Script execution interrupted", 1, true) ~= nil
end

local function share_get(prefix, suffix)
    if type(sys) ~= "table" or type(sys.get_share) ~= "function" then
        return nil
    end

    local ok, value = pcall(sys.get_share, share_key(prefix, suffix))
    if ok then
        return value
    end
    if is_stop_interrupt(value) then
        running = false
        return nil
    end
    error(value)
end

local function share_set(prefix, suffix, value)
    if type(sys) ~= "table" or type(sys.set_share) ~= "function" then
        return
    end

    local ok, err = pcall(sys.set_share, share_key(prefix, suffix), value)
    if ok then
        return
    end
    if is_stop_interrupt(err) then
        running = false
        return
    end
    error(err)
end

local function log_info(message)
    if type(log) == "table" and type(log.info) == "function" then
        log.info(message)
    end
end

local function log_warn(message)
    if type(log) == "table" and type(log.warn) == "function" then
        log.warn(message)
    elseif type(log) == "table" and type(log.info) == "function" then
        log.info(message)
    end
end

local function safe_sleep(delay_ms)
    if type(sys) ~= "table" or type(sys.sleep) ~= "function" then
        return true
    end

    local ok, err = pcall(sys.sleep, math.max(0, tonumber(delay_ms) or 0))
    if ok then
        return true
    end

    local message = tostring(err or "")
    if is_stop_interrupt(message) then
        running = false
        return false, message
    end
    error(err)
end

local function safe_call(fn, ...)
    if type(fn) ~= "function" then
        return false, "target is not callable"
    end
    return pcall(fn, ...)
end

local SHARE_PREFIX = tostring(share_prefix or "")
local PROCESS_NAME = tostring(process_name or "")
local RUNTIME_MODE = tostring(runtime_mode or "")
local UPDATE_INTERVAL_MS = math.max(30, tonumber(update_interval_ms) or 40)
local DEFAULT_INTERVAL_MS = math.max(100, tonumber(interval_ms) or 550)
local DEFAULT_RETRY_MS = math.max(50, tonumber(retry_ms) or 100)
local ACTION_MOUSE_HOLD_MS = math.max(10, tonumber(mouse_hold_ms) or 34)
local VK_W = 0x57

if SHARE_PREFIX == "" then
    error("share_prefix is required for AvePointLevelingCombatWorker")
end

local nav = load_nav_module()
local next_pulse_at = 0
local last_pulse_at = 0
local pulse_count = 0
local last_status = ""
local last_log_at = 0

local function ensure_nav_ready()
    if type(nav) ~= "table" or type(nav.ensure_initialized) ~= "function" then
        return false, "nav.ensure_initialized is unavailable."
    end

    local target = PROCESS_NAME ~= "" and PROCESS_NAME or nil
    local mode = RUNTIME_MODE ~= "" and RUNTIME_MODE or nil
    return nav.ensure_initialized(target, mode)
end

local function resolve_hwnd()
    if type(nav) ~= "table" or type(nav.window_hwnd) ~= "function" then
        return nil, "nav.window_hwnd is unavailable."
    end
    local hwnd, hwnd_err = nav.window_hwnd()
    if not hwnd then
        return nil, hwnd_err or "game window not found."
    end
    return hwnd
end

local function set_status(status, detail)
    status = tostring(status or "")
    share_set(SHARE_PREFIX, "worker_status", status)
    if detail ~= nil then
        share_set(SHARE_PREFIX, "last_error", detail)
    end
    if status ~= last_status then
        last_status = status
        share_set(SHARE_PREFIX, "status_changed_at", now_ms())
    end
end

local function press_w()
    local driver_api = type(driver) == "table" and driver or nil
    if type(driver_api) == "table" and type(driver_api.keybd_click) == "function" then
        local ok, result = safe_call(driver_api.keybd_click, VK_W)
        if ok and result ~= false then
            return true, nil, "driver_click"
        end
    end

    local hwnd, hwnd_err = resolve_hwnd()
    if not hwnd then
        return false, hwnd_err, nil
    end

    local keybd_api = type(keybd) == "table" and keybd or nil
    if type(keybd_api) ~= "table" then
        return false, "keybd api unavailable.", nil
    end
    if type(keybd_api.set_mode) == "function" then
        safe_call(keybd_api.set_mode, "driver")
    end
    if type(keybd_api.set_window) == "function" then
        safe_call(keybd_api.set_window, hwnd)
    end
    if type(keybd_api.click) == "function" then
        local ok, result = safe_call(keybd_api.click, VK_W)
        if ok and result ~= false then
            return true, nil, "key_click_driver"
        end
    end

    return false, "combat worker W key failed.", nil
end

local function release_right_mouse(mouse_api)
    if type(mouse_api) ~= "table" or type(mouse_api.up) ~= "function" then
        return false, "mouse.up unavailable"
    end

    local ok, result = safe_call(mouse_api.up, "right")
    return ok and result ~= false
end

local function press_right()
    local hwnd, hwnd_err = resolve_hwnd()
    if not hwnd then
        return false, hwnd_err, nil
    end

    local mouse_api = type(mouse) == "table" and mouse or nil
    if type(mouse_api) ~= "table" then
        return false, "mouse api unavailable.", nil
    end
    if type(mouse_api.set_mode) == "function" then
        safe_call(mouse_api.set_mode, "driver")
    end
    if type(mouse_api.set_window) == "function" then
        safe_call(mouse_api.set_window, hwnd)
    end
    if type(mouse_api.click) == "function" then
        release_right_mouse(mouse_api)
        local ok, result = safe_call(mouse_api.click, "right", ACTION_MOUSE_HOLD_MS)
        release_right_mouse(mouse_api)
        if ok and result ~= false then
            return true, nil, "mouse_click_driver"
        end
    end

    return false, "combat worker right click failed.", nil
end

local function issue_pulse(reason, interval_ms, retry_ms)
    local start_at = now_ms()
    local key_ok, key_err, key_mode = press_w()
    local mouse_ok, mouse_err, mouse_mode = press_right()
    local finish_at = now_ms()

    if key_ok and mouse_ok then
        local previous = last_pulse_at
        last_pulse_at = finish_at
        pulse_count = pulse_count + 1
        next_pulse_at = finish_at + interval_ms
        share_set(SHARE_PREFIX, "last_pulse_at", finish_at)
        share_set(SHARE_PREFIX, "last_pulse_interval_ms", previous > 0 and math.max(0, finish_at - previous) or 0)
        share_set(SHARE_PREFIX, "last_reason", tostring(reason or ""))
        share_set(SHARE_PREFIX, "last_key_mode", tostring(key_mode or ""))
        share_set(SHARE_PREFIX, "last_mouse_mode", tostring(mouse_mode or ""))
        share_set(SHARE_PREFIX, "pulse_count", pulse_count)
        share_set(SHARE_PREFIX, "last_error", nil)
        set_status("pulse")

        if finish_at - last_log_at >= 1000 then
            last_log_at = finish_at
            log_info(string.format(
                "[Leveling] combat worker pulse issued | reason=%s interval_ms=%d actual_gap_ms=%d key_mode=%s mouse_mode=%s elapsed_ms=%d",
                tostring(reason or ""),
                tonumber(interval_ms) or 0,
                previous > 0 and math.max(0, finish_at - previous) or 0,
                tostring(key_mode or ""),
                tostring(mouse_mode or ""),
                math.max(0, finish_at - start_at)
            ))
        end
        return true
    end

    next_pulse_at = finish_at + retry_ms
    local detail = string.format(
        "key_ok=%s key_err=%s mouse_ok=%s mouse_err=%s",
        tostring(key_ok == true),
        tostring(key_err or ""),
        tostring(mouse_ok == true),
        tostring(mouse_err or "")
    )
    set_status("pulse_failed", detail)
    if finish_at - last_log_at >= 1000 then
        last_log_at = finish_at
        log_warn("[Leveling] combat worker pulse failed | reason=" .. tostring(reason or "") .. " " .. detail)
    end
    return false, detail
end

if type(task) == "table" and type(task.on_stop) == "function" then
    task.on_stop(function()
        running = false
        release_right_mouse(type(mouse) == "table" and mouse or nil)
        share_set(SHARE_PREFIX, "worker_status", "stopped")
        share_set(SHARE_PREFIX, "heartbeat_at", now_ms())
    end)
end

share_set(SHARE_PREFIX, "worker_status", "starting")
share_set(SHARE_PREFIX, "heartbeat_at", now_ms())

local nav_ok, nav_err = ensure_nav_ready()
if not nav_ok then
    set_status("nav_unavailable", tostring(nav_err or ""))
    log_warn("[Leveling] combat worker nav init failed: " .. tostring(nav_err or ""))
end

while running do
    local current_time = now_ms()
    share_set(SHARE_PREFIX, "heartbeat_at", current_time)

    if as_boolean(share_get(SHARE_PREFIX, "stop")) then
        running = false
        break
    end

    if nav_ok ~= true then
        nav_ok, nav_err = ensure_nav_ready()
        if nav_ok ~= true then
            set_status("nav_unavailable", tostring(nav_err or ""))
            if not safe_sleep(200) then
                break
            end
        end
    else
        local enabled = as_boolean(share_get(SHARE_PREFIX, "enabled"))
        local paused = as_boolean(share_get(SHARE_PREFIX, "paused"))
        local allow_until = as_number(share_get(SHARE_PREFIX, "allow_until")) or 0
        local reason = tostring(share_get(SHARE_PREFIX, "reason") or "")
        local interval_ms = math.max(100, as_number(share_get(SHARE_PREFIX, "interval_ms")) or DEFAULT_INTERVAL_MS)
        local retry_ms = math.max(50, as_number(share_get(SHARE_PREFIX, "retry_ms")) or DEFAULT_RETRY_MS)

        if paused or not enabled then
            set_status(paused and "paused" or "idle")
            if not safe_sleep(UPDATE_INTERVAL_MS) then
                break
            end
        elseif current_time > allow_until then
            set_status("lease_expired")
            if not safe_sleep(UPDATE_INTERVAL_MS) then
                break
            end
        elseif current_time >= next_pulse_at then
            issue_pulse(reason, interval_ms, retry_ms)
        else
            local wait_ms = math.min(UPDATE_INTERVAL_MS, math.max(10, next_pulse_at - current_time))
            if not safe_sleep(wait_ms) then
                break
            end
        end
    end
end

share_set(SHARE_PREFIX, "worker_status", "stopped")
release_right_mouse(type(mouse) == "table" and mouse or nil)
share_set(SHARE_PREFIX, "heartbeat_at", now_ms())
