local HOTKEY_MOVE_RANDOM = 0x2D
local HOTKEY_MOVE_CENTER = 0x24
local HOTKEY_MOVE_ASYNC = 0x21
local HOTKEY_CANCEL_ASYNC = 0x2E
local HOTKEY_EXIT_CTRL = 0x11
local HOTKEY_EXIT = 0x7B
local POLL_INTERVAL_MS = 10

local EDGE_MARGIN = 36
local PROFILE_KEY = "fullscreen-demo"
local PROFILE_NAME = "steady"
local MOUSE_MODE = "api"

local function load_human_mouse_module()
    local ok, mod = pcall(require, "human_mouse_v2")
    if ok and type(mod) == "table" then
        return mod
    end

    ok, mod = pcall(require, "scripts.human_mouse_v2")
    if ok and type(mod) == "table" then
        return mod
    end

    error("Unable to load human_mouse_v2 module.")
end

local human_mouse = load_human_mouse_module()

local function copy_table(source)
    local result = {}
    for key, value in pairs(source or {}) do
        result[key] = value
    end
    return result
end

local function build_move_opts(extra)
    local opts = {
        bounds_mode = "screen",
        edge_margin = EDGE_MARGIN,
        mouse_mode = MOUSE_MODE,
        profile_key = PROFILE_KEY,
        profile = PROFILE_NAME,
        allow_overshoot = true,
        trail_click_interval_ms = 10,
        trail_click_button = "left",
        trail_click_down_ms = 1,
        trail_click_delay_ms = 0,
        target_width = 14,
        max_steps_per_tick = 48,
        manual_override_distance = 72
    }
    for key, value in pairs(extra or {}) do
        opts[key] = value
    end
    return opts
end

local function screen_bounds()
    if type(sys) ~= "table" or type(sys.screen_size) ~= "function" then
        return nil, "sys.screen_size is not available."
    end

    local width, height = sys.screen_size()
    if type(width) ~= "number"
        or type(height) ~= "number"
        or width <= 0
        or height <= 0
    then
        return nil, "Screen size unavailable."
    end

    local left = EDGE_MARGIN
    local top = EDGE_MARGIN
    local right = math.max(left, width - 1 - EDGE_MARGIN)
    local bottom = math.max(top, height - 1 - EDGE_MARGIN)
    return {
        left = left,
        top = top,
        right = right,
        bottom = bottom,
        width = width,
        height = height
    }
end

local function center_screen_point(bounds)
    local x = math.floor((bounds.left + bounds.right) / 2)
    local y = math.floor((bounds.top + bounds.bottom) / 2)
    return x, y
end

local function is_hotkey_pressed(vk)
    if type(hotkey) ~= "table" or type(hotkey.is_pressed) ~= "function" then
        return false
    end
    return hotkey.is_pressed(vk)
end

local function update_hotkey_edge(vk, was_down)
    local down = is_hotkey_pressed(vk)
    local fired = down and not was_down
    return down, fired
end

local function log_plan(label, plan)
    if type(plan) ~= "table" then
        log.info(string.format("%s finished", label))
        return
    end

    if plan.cancel_reason then
        log.info(string.format(
            "%s cancelled | reason=%s cursor=(%d,%d) distance=%.1f",
            label,
            tostring(plan.cancel_reason),
            math.floor(tonumber(plan.screen_x) or 0),
            math.floor(tonumber(plan.screen_y) or 0),
            tonumber(plan.distance) or 0
        ))
        return
    end

    local points = #(plan.points or {})
    log.info(string.format(
        "%s | points=%d duration=%dms overshoot=%s profile=%s target=(%d,%d)",
        label,
        points,
        math.floor(tonumber(plan.duration_ms) or 0),
        tostring(plan.overshoot == true),
        tostring(plan.profile_name or "?"),
        math.floor(tonumber(plan.points and plan.points[#plan.points] and plan.points[#plan.points].x) or 0),
        math.floor(tonumber(plan.points and plan.points[#plan.points] and plan.points[#plan.points].y) or 0)
    ))
end

local function move_random_sync()
    local ok, plan_or_err = human_mouse.move_random_on_screen(build_move_opts())
    if not ok then
        log.error("Random full-screen move failed: " .. tostring(plan_or_err))
        return
    end
    log_plan("Random full-screen move", plan_or_err)
end

local function move_center_sync()
    local bounds, err = screen_bounds()
    if not bounds then
        log.error("Center move failed: " .. tostring(err))
        return
    end

    local x, y = center_screen_point(bounds)
    local ok, plan_or_err = human_mouse.move_to(x, y, build_move_opts())
    if not ok then
        log.error("Center move failed: " .. tostring(plan_or_err))
        return
    end
    log_plan("Center move", plan_or_err)
end

local function move_random_async()
    local ok, plan_or_err = human_mouse.start_async_random_move_on_screen(build_move_opts())
    if not ok then
        log.error("Async full-screen move failed: " .. tostring(plan_or_err))
        return
    end
    log_plan("Async full-screen move started", plan_or_err)
end

if type(hotkey) ~= "table" or type(hotkey.start) ~= "function" or type(hotkey.is_running) ~= "function" then
    error("hotkey module is not available")
end

local seed = tonumber(type(sys) == "table" and type(sys.time) == "function" and sys.time() or os.time() or 1) or 1
math.randomseed(seed)
math.random()
math.random()
math.random()

local profile_name, profile, traits = human_mouse.configure({
    profile_key = PROFILE_KEY,
    profile = PROFILE_NAME
})
local profile_snapshot = copy_table(profile)
local trait_snapshot = copy_table(traits)

log.info("Human mouse fullscreen demo ready")
log.info(string.format(
    "Profile=%s speed_gain=%.2f-%.2f tremor=%.2f/%.2f overshoot=%.0f%% stability=%.2f",
    tostring(profile_name),
    tonumber(profile_snapshot.speed_gain_min) or 0,
    tonumber(profile_snapshot.speed_gain_max) or 0,
    tonumber(profile_snapshot.tremor_amplitude or profile_snapshot.noise_amplitude) or 0,
    tonumber(profile_snapshot.tremor_frequency or profile_snapshot.noise_frequency) or 0,
    (tonumber(profile_snapshot.overshoot_probability) or 0) * 100,
    tonumber(trait_snapshot.hand_stability) or 0
))
log.info("Insert=random full-screen move | Home=move to screen center | PageUp=async random move | Delete=cancel async | Ctrl+F12=exit")

local random_hotkey_down = false
local center_hotkey_down = false
local async_hotkey_down = false
local cancel_hotkey_down = false

local started_hotkey = false
if not hotkey.is_running() then
    hotkey.start(10)
    started_hotkey = true
end

while true do
    if human_mouse.has_async_move() then
        local ok, meta_or_err, done = human_mouse.tick_async_move()
        if not ok then
            log.error("Async tick failed: " .. tostring(meta_or_err))
        elseif done then
            log_plan("Async full-screen move completed", meta_or_err)
        end
    end

    if is_hotkey_pressed(HOTKEY_EXIT_CTRL) and is_hotkey_pressed(HOTKEY_EXIT) then
        log.info("Exit hotkey pressed")
        break
    end

    local fired

    random_hotkey_down, fired = update_hotkey_edge(HOTKEY_MOVE_RANDOM, random_hotkey_down)
    if fired then
        move_random_sync()
    end

    center_hotkey_down, fired = update_hotkey_edge(HOTKEY_MOVE_CENTER, center_hotkey_down)
    if fired then
        move_center_sync()
    end

    async_hotkey_down, fired = update_hotkey_edge(HOTKEY_MOVE_ASYNC, async_hotkey_down)
    if fired then
        move_random_async()
    end

    cancel_hotkey_down, fired = update_hotkey_edge(HOTKEY_CANCEL_ASYNC, cancel_hotkey_down)
    if fired then
        local cancelled = human_mouse.cancel_async_move()
        if cancelled then
            log.info("Async move cancelled")
        end
    end

    sys.sleep(POLL_INTERVAL_MS)
end

human_mouse.cancel_async_move()
if started_hotkey and type(hotkey) == "table" and type(hotkey.stop) == "function" and hotkey.is_running() then
    hotkey.stop()
end
