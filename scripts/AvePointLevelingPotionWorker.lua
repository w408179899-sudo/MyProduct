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
local UPDATE_INTERVAL_MS = math.max(20, tonumber(update_interval_ms) or 40)
local Q_THRESHOLD_RATIO = tonumber(q_threshold_ratio) or 0.70
local E_THRESHOLD_RATIO = tonumber(e_threshold_ratio) or 0.80
local POTION_COOLDOWN_MS = math.max(0, tonumber(cooldown_ms) or 100)
local VK_Q = 0x51
local VK_E = 0x45

if SHARE_PREFIX == "" then
    error("share_prefix is required for AvePointLevelingPotionWorker")
end

local nav = load_nav_module()
local last_q_at = 0
local last_e_at = 0
local use_count = 0
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

local function press_key_vk(vk, label)
    local driver_api = type(driver) == "table" and driver or nil
    if type(driver_api) == "table" and type(driver_api.keybd_click) == "function" then
        local ok, result = safe_call(driver_api.keybd_click, vk)
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
        local ok, result = safe_call(keybd_api.click, vk)
        if ok and result ~= false then
            return true, nil, "key_click_driver"
        end
    end

    return false, "potion worker " .. tostring(label or "key") .. " key failed.", nil
end

local function find_named_number(value, names, depth, seen)
    if type(value) ~= "table" then
        return nil, nil
    end
    depth = tonumber(depth) or 0
    if depth < 0 then
        return nil, nil
    end
    seen = seen or {}
    if seen[value] then
        return nil, nil
    end
    seen[value] = true

    local wanted = {}
    for _, name in ipairs(names or {}) do
        wanted[tostring(name)] = true
    end

    for key, item in pairs(value) do
        local key_text = tostring(key)
        if wanted[key_text] then
            local number_value = as_number(item)
            if number_value ~= nil then
                return number_value, key_text
            end
        end
        if type(item) == "table" then
            local number_value, source = find_named_number(item, names, depth - 1, seen)
            if number_value ~= nil then
                if source and source ~= "" then
                    return number_value, key_text .. "." .. tostring(source)
                end
                return number_value, key_text
            end
        end
    end

    return nil, nil
end

local function read_hp_ratio()
    if type(nav) ~= "table" or type(nav.player_info) ~= "function" then
        return nil, nil, nil, "nav.player_info unavailable"
    end
    local info, info_err = nav.player_info()
    if type(info) ~= "table" then
        return nil, nil, nil, info_err or "player_info failed"
    end

    local hp = find_named_number(info, {
        "hp", "HP", "Hp",
        "curHp", "CurHp", "curHP", "CurHP",
        "currentHp", "CurrentHp", "currentHP", "CurrentHP",
        "health", "Health",
        "curHealth", "CurHealth",
        "currentHealth", "CurrentHealth",
        "life", "Life",
        "curLife", "CurLife",
        "currentLife", "CurrentLife",
        "blood", "Blood",
        "curBlood", "CurBlood",
        "currentBlood", "CurrentBlood",
        "\232\161\128\233\135\143", "\229\189\147\229\137\141\232\161\128\233\135\143"
    }, 4)
    local max_hp = find_named_number(info, {
        "maxHp", "MaxHp", "maxHP", "MaxHP",
        "maximumHp", "MaximumHp", "maximumHP", "MaximumHP",
        "maxHealth", "MaxHealth",
        "maximumHealth", "MaximumHealth",
        "maxLife", "MaxLife",
        "maximumLife", "MaximumLife",
        "maxBlood", "MaxBlood",
        "maximumBlood", "MaximumBlood",
        "\230\156\128\229\164\167\232\161\128\233\135\143", "\232\161\128\233\135\143\228\184\138\233\153\144"
    }, 4)
    hp = as_number(hp)
    max_hp = as_number(max_hp)
    if hp == nil or max_hp == nil or max_hp <= 0 then
        return hp, max_hp, nil, "hp/max_hp unavailable"
    end
    return hp, max_hp, hp / max_hp, nil
end

local function maybe_press_potion(current_time, hotkey_vk, hotkey_name, threshold_ratio, last_used_at)
    if current_time - (tonumber(last_used_at) or 0) < POTION_COOLDOWN_MS then
        return false, last_used_at
    end

    local hp, max_hp, hp_ratio, hp_err = read_hp_ratio()
    if type(hp_ratio) ~= "number" or hp <= 0 or hp_ratio > threshold_ratio then
        if hp_err and current_time - last_log_at >= 2000 then
            last_log_at = current_time
            set_status("watching", tostring(hp_err))
        end
        return false, last_used_at
    end

    local ok, err, mode = press_key_vk(hotkey_vk, hotkey_name)
    local finish_at = now_ms()
    if ok then
        use_count = use_count + 1
        share_set(SHARE_PREFIX, "last_potion_at", finish_at)
        share_set(SHARE_PREFIX, "last_potion_key", hotkey_name)
        share_set(SHARE_PREFIX, "use_count", use_count)
        share_set(SHARE_PREFIX, "last_error", nil)
        set_status("potion")
        log_info(string.format(
            "[Leveling] potion worker used | key=%s hp=%.2f max_hp=%.2f ratio=%.2f threshold=%.2f mode=%s count=%d",
            tostring(hotkey_name or ""),
            tonumber(hp) or 0,
            tonumber(max_hp) or 0,
            tonumber(hp_ratio) or 0,
            tonumber(threshold_ratio) or 0,
            tostring(mode or ""),
            tonumber(use_count) or 0
        ))
        return true, finish_at
    end

    set_status("potion_failed", tostring(err or ""))
    log_warn(string.format(
        "[Leveling] potion worker failed | key=%s hp=%.2f max_hp=%.2f ratio=%.2f threshold=%.2f err=%s",
        tostring(hotkey_name or ""),
        tonumber(hp) or 0,
        tonumber(max_hp) or 0,
        tonumber(hp_ratio) or 0,
        tonumber(threshold_ratio) or 0,
        tostring(err or "")
    ))
    return false, finish_at
end

if type(task) == "table" and type(task.on_stop) == "function" then
    task.on_stop(function()
        running = false
        share_set(SHARE_PREFIX, "worker_status", "stopped")
        share_set(SHARE_PREFIX, "heartbeat_at", now_ms())
    end)
end

share_set(SHARE_PREFIX, "worker_status", "starting")
share_set(SHARE_PREFIX, "heartbeat_at", now_ms())

local nav_ok, nav_err = ensure_nav_ready()
if not nav_ok then
    set_status("nav_unavailable", tostring(nav_err or ""))
    log_warn("[Leveling] potion worker nav init failed: " .. tostring(nav_err or ""))
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
        local used_q = false
        used_q, last_q_at = maybe_press_potion(current_time, VK_Q, "Q", Q_THRESHOLD_RATIO, last_q_at)
        current_time = now_ms()
        local used_e = false
        used_e, last_e_at = maybe_press_potion(current_time, VK_E, "E", E_THRESHOLD_RATIO, last_e_at)
        if not used_q and not used_e then
            set_status("watching")
        end
        if not safe_sleep(UPDATE_INTERVAL_MS) then
            break
        end
    end
end

share_set(SHARE_PREFIX, "worker_status", "stopped")
share_set(SHARE_PREFIX, "heartbeat_at", now_ms())
