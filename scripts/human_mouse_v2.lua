local M = {}

local function load_mouse_driver_module()
    local ok, mod = pcall(require, "mouse_driver_v2")
    if ok and type(mod) == "table" then
        return mod
    end

    ok, mod = pcall(require, "scripts.mouse_driver_v2")
    if ok and type(mod) == "table" then
        return mod
    end

    error("Unable to load mouse_driver_v2 module.")
end

local MouseDriver = load_mouse_driver_module()
local default_driver = MouseDriver.new()
local async_move_state = nil

local EDGE_MARGIN = 28
local PRE_CLICK_HOVER_MIN_MS = 10
local PRE_CLICK_HOVER_MAX_MS = 26
local POST_CLICK_HOVER_MIN_MS = 12
local POST_CLICK_HOVER_MAX_MS = 30
local CLICK_DOWN_MIN_MS = 16
local CLICK_DOWN_MAX_MS = 36
local MANUAL_OVERRIDE_DISTANCE = 48

local function clamp(value, min_value, max_value)
    if value < min_value then
        return min_value
    end
    if value > max_value then
        return max_value
    end
    return value
end

local function round(value)
    if value >= 0 then
        return math.floor(value + 0.5)
    end
    return math.ceil(value - 0.5)
end

local function point_distance(ax, ay, bx, by)
    local dx = (tonumber(bx) or 0) - (tonumber(ax) or 0)
    local dy = (tonumber(by) or 0) - (tonumber(ay) or 0)
    return math.sqrt(dx * dx + dy * dy)
end

local function random_between(min_value, max_value)
    return default_driver:randi(min_value, max_value)
end

local function resolve_driver(opts)
    if type(opts) == "table"
        and type(opts.driver) == "table"
        and type(opts.driver.plan_move) == "function"
    then
        return opts.driver
    end

    if type(opts) == "table"
        and (
            opts.seed ~= nil
            or opts.profile_key ~= nil
            or opts.profile ~= nil
            or type(opts.profile_overrides) == "table"
        )
    then
        return MouseDriver.new({
            seed = opts.seed,
            profile_key = opts.profile_key,
            profile = opts.profile,
            profile_overrides = opts.profile_overrides
        })
    end

    return default_driver
end

local function resolve_bounds(hwnd, opts)
    local margin = math.max(0, tonumber(opts and opts.edge_margin) or EDGE_MARGIN)
    if type(wnd) ~= "table" then
        return nil, "wnd module is not available."
    end

    local target_hwnd = hwnd
    if not target_hwnd and type(wnd.get_foreground) == "function" then
        target_hwnd = wnd.get_foreground()
    end
    if not target_hwnd then
        return nil, "Window handle unavailable."
    end

    local x, y, w, h
    if type(wnd.client_rect) == "function" then
        x, y, w, h = wnd.client_rect(target_hwnd)
    elseif type(wnd.wnd_rect) == "function" then
        x, y, w, h = wnd.wnd_rect(target_hwnd)
    else
        return nil, "wnd.client_rect/wnd.wnd_rect is not available."
    end

    if type(x) ~= "number"
        or type(y) ~= "number"
        or type(w) ~= "number"
        or type(h) ~= "number"
        or w <= 0
        or h <= 0
    then
        return nil, "Window bounds unavailable."
    end

    local left = x + margin
    local top = y + margin
    local right = x + w - margin
    local bottom = y + h - margin
    if right <= left then
        left = x
        right = x + w
    end
    if bottom <= top then
        top = y
        bottom = y + h
    end

    return {
        hwnd = target_hwnd,
        left = left,
        top = top,
        right = right,
        bottom = bottom
    }
end

local function begin_mouse_runtime(mode)
    local runtime = {
        previous_mode = type(mouse.get_mode) == "function" and mouse.get_mode() or nil,
        previous_trajectory = type(mouse.get_trajectory) == "function" and mouse.get_trajectory() or nil,
        mode = mode
    }

    if type(mouse.set_mode) == "function"
        and type(mode) == "string"
        and mode ~= ""
        and runtime.previous_mode ~= mode
    then
        local ok = mouse.set_mode(mode)
        if ok == false then
            return nil, "mouse.set_mode failed."
        end
    end

    if type(mouse.set_trajectory) == "function" then
        pcall(mouse.set_trajectory, "none")
    end

    return runtime
end

local function end_mouse_runtime(runtime)
    if type(runtime) ~= "table" then
        return
    end

    if type(mouse.set_trajectory) == "function" and runtime.previous_trajectory then
        pcall(mouse.set_trajectory, runtime.previous_trajectory)
    end

    if type(mouse.set_mode) == "function"
        and type(runtime.previous_mode) == "string"
        and runtime.previous_mode ~= ""
        and runtime.previous_mode ~= runtime.mode
    then
        pcall(mouse.set_mode, runtime.previous_mode)
    end
end

local function with_mouse_mode(mode, fn)
    local runtime, runtime_err = begin_mouse_runtime(mode)
    if not runtime then
        return false, runtime_err
    end

    local ok, result = xpcall(fn, function(trace)
        return trace
    end)

    end_mouse_runtime(runtime)

    if not ok then
        return false, result
    end

    return true, result
end

local function click_current_position(button, opts)
    local click_button = tostring(button or "left")
    local down_ms = random_between(
        tonumber(opts and opts.click_down_min_ms) or CLICK_DOWN_MIN_MS,
        tonumber(opts and opts.click_down_max_ms) or CLICK_DOWN_MAX_MS
    )
    local extra_delay_ms = math.max(0, tonumber(opts and opts.click_delay_ms) or 0)

    if type(mouse.down) == "function" and type(mouse.up) == "function" then
        local ok = mouse.down(click_button)
        if ok == false then
            return false, "mouse.down failed."
        end

        sys.sleep(down_ms + extra_delay_ms)

        ok = mouse.up(click_button)
        if ok == false then
            return false, "mouse.up failed."
        end
        return true
    end

    if type(mouse.click) == "function" then
        local ok = mouse.click(click_button, down_ms + extra_delay_ms)
        if ok == false then
            return false, "mouse.click failed."
        end
        return true
    end

    return false, "mouse click API is not available."
end

local function click_current_position_precise(button, down_ms, extra_delay_ms)
    local click_button = tostring(button or "left")
    local hold_ms = math.max(0, tonumber(down_ms) or 0)
    local extra_ms = math.max(0, tonumber(extra_delay_ms) or 0)

    if type(mouse.down) == "function" and type(mouse.up) == "function" then
        local ok = mouse.down(click_button)
        if ok == false then
            return false, "mouse.down failed."
        end

        if hold_ms + extra_ms > 0 then
            sys.sleep(hold_ms + extra_ms)
        end

        ok = mouse.up(click_button)
        if ok == false then
            return false, "mouse.up failed."
        end
        return true
    end

    if type(mouse.click) == "function" then
        local ok = mouse.click(click_button, hold_ms + extra_ms)
        if ok == false then
            return false, "mouse.click failed."
        end
        return true
    end

    return false, "mouse click API is not available."
end

local function create_trail_click_state(opts)
    local interval_ms = math.max(0, tonumber(opts and opts.trail_click_interval_ms) or 0)
    if interval_ms <= 0 then
        return {
            enabled = false
        }
    end

    return {
        enabled = true,
        interval_ms = math.max(1, round(interval_ms)),
        next_plan_time = math.max(1, round(interval_ms)),
        button = tostring(opts and opts.trail_click_button or "left"),
        down_ms = math.max(0, tonumber(opts and opts.trail_click_down_ms) or 1),
        extra_delay_ms = math.max(0, tonumber(opts and opts.trail_click_delay_ms) or 0)
    }
end

local function emit_trail_clicks(trail_state, current_plan_time)
    if type(trail_state) ~= "table" or trail_state.enabled ~= true then
        return true
    end

    local plan_time = math.max(0, tonumber(current_plan_time) or 0)
    while plan_time >= (tonumber(trail_state.next_plan_time) or math.huge) do
        local ok, err = click_current_position_precise(
            trail_state.button,
            trail_state.down_ms,
            trail_state.extra_delay_ms
        )
        if not ok then
            return false, err
        end
        trail_state.next_plan_time = (tonumber(trail_state.next_plan_time) or 0) + (tonumber(trail_state.interval_ms) or 1)
    end

    return true
end

local function copy_bounds(bounds)
    if type(bounds) ~= "table" then
        return nil
    end

    return {
        hwnd = bounds.hwnd,
        left = tonumber(bounds.left) or 0,
        top = tonumber(bounds.top) or 0,
        right = tonumber(bounds.right) or 0,
        bottom = tonumber(bounds.bottom) or 0
    }
end

local function normalize_bounds_mode(mode, default_mode)
    if mode == nil or mode == "" then
        return default_mode or "window"
    end

    local value = string.lower(tostring(mode))
    if value == "screen" or value == "fullscreen" or value == "absolute" or value == "absolute_screen" then
        return "screen"
    end
    if value == "none" or value == "unbounded" then
        return "none"
    end
    if value == "window" or value == "client" then
        return "window"
    end

    return default_mode or "window"
end

local function copy_opts(opts)
    local result = {}
    for key, value in pairs(opts or {}) do
        result[key] = value
    end
    return result
end

local function with_bounds_mode(opts, bounds_mode)
    local next_opts = copy_opts(opts)
    next_opts.bounds_mode = bounds_mode
    return next_opts
end

local function resolve_screen_bounds(opts)
    local margin = math.max(0, tonumber(opts and opts.edge_margin) or EDGE_MARGIN)
    if type(sys) ~= "table" or type(sys.screen_size) ~= "function" then
        return nil, "sys.screen_size is not available."
    end

    local width, height = sys.screen_size()
    if type(width) ~= "number"
        or type(height) ~= "number"
        or width <= 0
        or height <= 0
    then
        return nil, "Screen bounds unavailable."
    end

    local left = margin
    local top = margin
    local right = width - 1 - margin
    local bottom = height - 1 - margin
    if right <= left then
        left = 0
        right = math.max(0, width - 1)
    end
    if bottom <= top then
        top = 0
        bottom = math.max(0, height - 1)
    end

    return {
        mode = "screen",
        left = left,
        top = top,
        right = right,
        bottom = bottom,
        width = width,
        height = height
    }
end

local function resolve_plan_bounds(opts, default_mode)
    if type(opts) == "table" and type(opts.bounds) == "table" then
        return copy_bounds(opts.bounds)
    end

    local fallback_mode = default_mode or "window"
    if type(opts) == "table" then
        if opts.use_screen_bounds == true or opts.full_screen == true or opts.absolute_screen == true then
            fallback_mode = "screen"
        elseif opts.allow_unbounded == true or opts.unbounded == true then
            fallback_mode = "none"
        elseif opts.use_window_bounds == true then
            fallback_mode = "window"
        end
    end

    local mode = normalize_bounds_mode(type(opts) == "table" and opts.bounds_mode or nil, fallback_mode)
    if mode == "none" then
        return nil
    end
    if mode == "screen" then
        return resolve_screen_bounds(opts)
    end
    return resolve_bounds(opts and opts.hwnd or nil, opts)
end

local function plan_with_driver(driver, start_x, start_y, screen_x, screen_y, opts, bounds)
    local plan = driver:plan_move(start_x, start_y, screen_x, screen_y, {
        bounds = bounds,
        target_width = opts and opts.target_width or nil,
        report_rate_hz = opts and opts.report_rate_hz or nil,
        allow_overshoot = opts == nil or opts.allow_overshoot ~= false,
        overshoot_probability = opts and opts.overshoot_probability or nil,
        duration_scale = opts and opts.duration_scale or nil,
        min_duration_ms = opts and opts.min_duration_ms or nil,
        max_duration_ms = opts and opts.max_duration_ms or nil,
        duration_center_ms = opts and opts.duration_center_ms or nil,
        duration_sigma_ms = opts and opts.duration_sigma_ms or nil,
        duration_gaussian_weight = opts and opts.duration_gaussian_weight or nil,
        duration_distribution = opts and opts.duration_distribution or nil,
        now_ms = opts and opts.now_ms or nil
    })
    plan.bounds = bounds
    plan.driver = driver
    plan.screen_x = round(tonumber(screen_x) or 0)
    plan.screen_y = round(tonumber(screen_y) or 0)
    plan.start_x = round(tonumber(start_x) or 0)
    plan.start_y = round(tonumber(start_y) or 0)
    return plan
end

local function build_plan(screen_x, screen_y, opts)
    if type(mouse) ~= "table"
        or type(mouse.position) ~= "function"
        or type(mouse.move_to) ~= "function"
    then
        return nil, "mouse.position/mouse.move_to is not available."
    end

    local bounds, bounds_err = resolve_plan_bounds(opts, "window")
    if bounds == nil and bounds_err ~= nil then
        return nil, bounds_err
    end

    if opts and opts.set_foreground == true
        and type(wnd) == "table"
        and type(wnd.set_foreground) == "function"
        and bounds.hwnd
    then
        wnd.set_foreground(bounds.hwnd)
        sys.sleep(math.max(40, tonumber(opts.foreground_delay_ms) or 60))
    end

    local start_x, start_y = mouse.position()
    if type(start_x) ~= "number" or type(start_y) ~= "number" then
        return nil, "mouse.position failed."
    end

    local driver = resolve_driver(opts)
    return plan_with_driver(driver, start_x, start_y, screen_x, screen_y, opts, bounds)
end

function M.configure(opts)
    default_driver = MouseDriver.new(opts or {})
    return default_driver:get_profile()
end

function M.new_driver(opts)
    return MouseDriver.new(opts or {})
end

function M.new_profile_generator(opts)
    return MouseDriver.new_profile_generator(opts or {})
end

function M.get_profile()
    return default_driver:get_profile()
end

function M.seed_from_profile_key(profile_key)
    return MouseDriver.seed_from_key(profile_key)
end

function M.plan_move(screen_x, screen_y, opts)
    return build_plan(screen_x, screen_y, opts)
end

function M.preview_move(screen_x, screen_y, opts)
    return M.plan_move(screen_x, screen_y, opts)
end

function M.generate_path(start_x, start_y, end_x, end_y, opts)
    local bounds, bounds_err = resolve_plan_bounds(opts, "none")
    if bounds == nil and bounds_err ~= nil then
        return false, bounds_err
    end

    local driver = resolve_driver(opts)
    local plan = plan_with_driver(
        driver,
        tonumber(start_x) or 0,
        tonumber(start_y) or 0,
        tonumber(end_x) or 0,
        tonumber(end_y) or 0,
        opts,
        bounds
    )
    return true, plan
end

function M.generate_points(start_x, start_y, end_x, end_y, opts)
    local ok, plan_or_err = M.generate_path(start_x, start_y, end_x, end_y, opts)
    if not ok then
        return false, plan_or_err
    end
    return true, plan_or_err.points, plan_or_err
end

function M.sleep_random(min_ms, max_ms)
    local delay_ms = random_between(min_ms, max_ms)
    sys.sleep(delay_ms)
    return delay_ms
end

function M.has_async_move()
    return type(async_move_state) == "table"
end

function M.cancel_async_move()
    if type(async_move_state) ~= "table" then
        return false
    end

    end_mouse_runtime(async_move_state.runtime)
    async_move_state = nil
    return true
end

function M.tick_async_move(now)
    local state = async_move_state
    if type(state) ~= "table" then
        return true, nil, false
    end

    now = tonumber(now) or (type(sys) == "table" and type(sys.time) == "function" and sys.time() or 0)
    if now < (state.next_at or 0) then
        return true, state.meta, false
    end

    if type(mouse) == "table"
        and type(mouse.position) == "function"
        and type(state.last_x) == "number"
        and type(state.last_y) == "number"
    then
        local cur_x, cur_y = mouse.position()
        if type(cur_x) == "number" and type(cur_y) == "number" then
            local manual_override_distance = tonumber(state.manual_override_distance) or MANUAL_OVERRIDE_DISTANCE
            if point_distance(cur_x, cur_y, state.last_x, state.last_y) >= manual_override_distance then
                end_mouse_runtime(state.runtime)
                async_move_state = nil
                return true, {
                    cancel_reason = "manual_override",
                    screen_x = cur_x,
                    screen_y = cur_y,
                    distance = point_distance(cur_x, cur_y, state.last_x, state.last_y)
                }, true
            end
        end
    end

    local max_steps = math.max(6, tonumber(state.max_steps_per_tick) or 36)
    while max_steps > 0 do
        local step = state.steps[state.index]
        if not step then
            end_mouse_runtime(state.runtime)
            local meta = state.meta
            async_move_state = nil
            return true, meta, true
        end

        local moved = mouse.move_to(step.x, step.y)
        if moved == false then
            end_mouse_runtime(state.runtime)
            async_move_state = nil
            return false, "mouse.move_to failed during async move.", true
        end

        state.last_x = step.x
        state.last_y = step.y
        state.index = state.index + 1

        local trail_ok, trail_err = emit_trail_clicks(state.trail_click_state, step.time)
        if not trail_ok then
            end_mouse_runtime(state.runtime)
            async_move_state = nil
            return false, trail_err, true
        end

        if state.steps[state.index] == nil then
            end_mouse_runtime(state.runtime)
            local meta = state.meta
            async_move_state = nil
            return true, meta, true
        end

        local delay_ms = math.max(0, tonumber(step.delay_ms) or 0)
        local scheduled_at = tonumber(state.next_at) or now
        state.next_at = scheduled_at + delay_ms
        max_steps = max_steps - 1
        if state.next_at > now then
            break
        end
    end

    return true, state.meta, false
end

function M.start_async_move(screen_x, screen_y, opts)
    M.cancel_async_move()

    local plan, plan_err = build_plan(screen_x, screen_y, opts)
    if not plan then
        return false, plan_err
    end

    local runtime, runtime_err = begin_mouse_runtime(tostring(opts and opts.mouse_mode or "api"))
    if not runtime then
        return false, runtime_err
    end

    async_move_state = {
        runtime = runtime,
        steps = plan.points or {},
        index = 2,
        next_at = type(sys) == "table" and type(sys.time) == "function" and sys.time() or 0,
        meta = plan,
        last_x = plan.points[1] and plan.points[1].x or nil,
        last_y = plan.points[1] and plan.points[1].y or nil,
        trail_click_state = create_trail_click_state(opts),
        manual_override_distance = tonumber(opts and opts.manual_override_distance) or MANUAL_OVERRIDE_DISTANCE,
        max_steps_per_tick = tonumber(opts and opts.max_steps_per_tick) or 36
    }

    return true, plan
end

function M.start_async_random_move_in_window(opts)
    local bounds, err = resolve_plan_bounds(opts, "window")
    if bounds == nil and err ~= nil then
        return false, err
    end
    if type(bounds) ~= "table" then
        return false, "Random move requires resolved bounds."
    end

    if type(mouse) ~= "table" or type(mouse.position) ~= "function" then
        return false, "mouse.position is not available."
    end

    local start_x, start_y = mouse.position()
    if type(start_x) ~= "number" or type(start_y) ~= "number" then
        return false, "mouse.position failed."
    end

    local driver = resolve_driver(opts)
    local target_x = start_x
    local target_y = start_y
    for _ = 1, 6 do
        target_x = driver:randf(bounds.left, bounds.right)
        target_y = driver:randf(bounds.top, bounds.bottom)
        if point_distance(start_x, start_y, target_x, target_y) >= 60 then
            break
        end
    end

    return M.start_async_move(target_x, target_y, opts)
end

function M.start_async_random_move_on_screen(opts)
    return M.start_async_random_move_in_window(with_bounds_mode(opts, "screen"))
end

function M.move_to(screen_x, screen_y, opts)
    M.cancel_async_move()

    local plan, plan_err = build_plan(screen_x, screen_y, opts)
    if not plan then
        return false, plan_err
    end

    local ok, result_or_err = with_mouse_mode(tostring(opts and opts.mouse_mode or "api"), function()
        local trail_click_state = create_trail_click_state(opts)
        for index = 2, #(plan.points or {}) do
            local step = plan.points[index]
            local moved = mouse.move_to(step.x, step.y)
            if moved == false then
                error("mouse.move_to failed.")
            end

            local trail_ok, trail_err = emit_trail_clicks(trail_click_state, step.time)
            if not trail_ok then
                error(trail_err)
            end

            local previous = plan.points[index - 1]
            local delay_ms = math.max(0, (tonumber(step.time) or 0) - (tonumber(previous and previous.time) or 0))
            if delay_ms > 0 then
                sys.sleep(delay_ms)
            end
        end
        return plan
    end)
    if not ok then
        return false, result_or_err
    end

    return true, result_or_err
end

function M.move_and_click(screen_x, screen_y, opts)
    M.cancel_async_move()
    local ok, plan_or_err = M.move_to(screen_x, screen_y, opts)
    if not ok then
        return false, plan_or_err
    end

    sys.sleep(random_between(
        tonumber(opts and opts.pre_click_hover_min_ms) or PRE_CLICK_HOVER_MIN_MS,
        tonumber(opts and opts.pre_click_hover_max_ms) or PRE_CLICK_HOVER_MAX_MS
    ) + math.max(0, tonumber(opts and opts.before_click_extra_delay_ms) or 0))

    local click_ok, click_err = with_mouse_mode(tostring(opts and opts.mouse_mode or "api"), function()
        local pressed, press_err = click_current_position(opts and opts.click_button or "left", opts)
        if not pressed then
            error(press_err)
        end
        return true
    end)
    if not click_ok then
        return false, click_err
    end

    sys.sleep(random_between(
        tonumber(opts and opts.post_click_hover_min_ms) or POST_CLICK_HOVER_MIN_MS,
        tonumber(opts and opts.post_click_hover_max_ms) or POST_CLICK_HOVER_MAX_MS
    ))

    return true, plan_or_err
end

function M.move_random_in_window(opts)
    M.cancel_async_move()

    local bounds, err = resolve_plan_bounds(opts, "window")
    if bounds == nil and err ~= nil then
        return false, err
    end
    if type(bounds) ~= "table" then
        return false, "Random move requires resolved bounds."
    end

    if type(mouse) ~= "table" or type(mouse.position) ~= "function" then
        return false, "mouse.position is not available."
    end

    local start_x, start_y = mouse.position()
    if type(start_x) ~= "number" or type(start_y) ~= "number" then
        return false, "mouse.position failed."
    end

    local driver = resolve_driver(opts)
    local target_x = start_x
    local target_y = start_y
    for _ = 1, 6 do
        target_x = driver:randf(bounds.left, bounds.right)
        target_y = driver:randf(bounds.top, bounds.bottom)
        if point_distance(start_x, start_y, target_x, target_y) >= 60 then
            break
        end
    end

    return M.move_to(target_x, target_y, opts)
end

function M.move_random_on_screen(opts)
    return M.move_random_in_window(with_bounds_mode(opts, "screen"))
end

return M
