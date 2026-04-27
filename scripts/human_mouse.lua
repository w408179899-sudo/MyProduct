local M = {}

local EDGE_MARGIN = 28
local MOVE_DURATION_MIN_MS = 300
local MOVE_DURATION_MAX_MS = 1200
local STEP_SPACING_MIN = 32.0
local STEP_SPACING_MAX = 84.0
local PRE_CLICK_HOVER_MIN_MS = 8
local PRE_CLICK_HOVER_MAX_MS = 28
local POST_CLICK_HOVER_MIN_MS = 10
local POST_CLICK_HOVER_MAX_MS = 32
local CLICK_DOWN_MIN_MS = 16
local CLICK_DOWN_MAX_MS = 36
local MIN_MOVE_STEPS = 4
local MAX_MOVE_STEPS = 22
local CURVE_STYLE_SWING = 1
local CURVE_STYLE_SPIRAL = 2
local CURVE_STYLE_LOOP = 3
local CURVE_STYLE_ZIGZAG = 4
local DIRECTIONAL_JITTER_MIN = 28
local DIRECTIONAL_JITTER_MAX = 110
local MANUAL_OVERRIDE_DISTANCE = 48
local async_move_state = nil

local function ensure_seeded()
    if _G.__human_mouse_rng_seeded == true then
        return
    end

    local sys_time = 0
    if type(sys) == "table" and type(sys.time) == "function" then
        sys_time = math.floor(tonumber(sys.time()) or 0)
    end

    local seed = ((os.time() or 1) + sys_time) % 2147483647
    if seed <= 0 then
        seed = 1
    end

    math.randomseed(seed)
    math.random()
    math.random()
    math.random()
    _G.__human_mouse_rng_seeded = true
end

local function randf(min_value, max_value)
    return min_value + (max_value - min_value) * math.random()
end

local function round(value)
    if value >= 0 then
        return math.floor(value + 0.5)
    end
    return math.ceil(value - 0.5)
end

local function clamp(value, min_value, max_value)
    if value < min_value then
        return min_value
    end
    if value > max_value then
        return max_value
    end
    return value
end

local function copy_point(point)
    return { x = point.x, y = point.y }
end

local function point_distance(a, b)
    local dx = b.x - a.x
    local dy = b.y - a.y
    return math.sqrt(dx * dx + dy * dy)
end

local function normalize(dx, dy)
    local length = math.sqrt(dx * dx + dy * dy)
    if length <= 0.0001 then
        return 0, 0, 0
    end
    return dx / length, dy / length, length
end

local function cubic_point(p0, p1, p2, p3, t)
    local u = 1 - t
    local uu = u * u
    local tt = t * t
    local uuu = uu * u
    local ttt = tt * t
    return {
        x = uuu * p0.x + 3 * uu * t * p1.x + 3 * u * tt * p2.x + ttt * p3.x,
        y = uuu * p0.y + 3 * uu * t * p1.y + 3 * u * tt * p2.y + ttt * p3.y
    }
end

local function random_between(min_value, max_value)
    ensure_seeded()
    local min_number = math.floor(tonumber(min_value) or 0)
    local max_number = math.floor(tonumber(max_value) or min_number)
    if max_number < min_number then
        min_number, max_number = max_number, min_number
    end
    if max_number <= min_number then
        return min_number
    end
    return math.random(min_number, max_number)
end

local function biased_random_between(min_value, max_value, exponent)
    ensure_seeded()

    local min_number = math.floor(tonumber(min_value) or 0)
    local max_number = math.floor(tonumber(max_value) or min_number)
    if max_number < min_number then
        min_number, max_number = max_number, min_number
    end
    if max_number <= min_number then
        return min_number
    end

    local power = tonumber(exponent) or 1.0
    if power <= 0 then
        power = 1.0
    end

    local ratio = math.random() ^ power
    return round(min_number + (max_number - min_number) * ratio)
end

local function choose_move_duration_ms(distance, opts)
    local min_ms = math.max(1, tonumber(opts and opts.min_duration_ms) or MOVE_DURATION_MIN_MS)
    local max_ms = math.max(min_ms, tonumber(opts and opts.max_duration_ms) or MOVE_DURATION_MAX_MS)
    return random_between(min_ms, max_ms)
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

local function clamp_point_to_bounds(point, bounds)
    if not bounds then
        return copy_point(point)
    end

    return {
        x = clamp(point.x, bounds.left, bounds.right),
        y = clamp(point.y, bounds.top, bounds.bottom)
    }
end

local function append_anchor(points, point, bounds)
    points[#points + 1] = clamp_point_to_bounds(point, bounds)
end

local function build_motion_anchors(start_point, end_point, bounds)
    local anchors = { copy_point(start_point) }
    local dx = end_point.x - start_point.x
    local dy = end_point.y - start_point.y
    local dir_x, dir_y, length = normalize(dx, dy)
    local normal_x = -dir_y
    local normal_y = dir_x
    local style = math.random(CURVE_STYLE_SWING, CURVE_STYLE_ZIGZAG)
    local sway = clamp(length * randf(0.12, 0.30), DIRECTIONAL_JITTER_MIN, DIRECTIONAL_JITTER_MAX * 1.8)

    if style == CURVE_STYLE_SWING then
        append_anchor(anchors, {
            x = start_point.x + dx * randf(0.15, 0.26) + normal_x * randf(-sway, sway),
            y = start_point.y + dy * randf(0.15, 0.26) + normal_y * randf(-sway, sway)
        }, bounds)
        append_anchor(anchors, {
            x = start_point.x + dx * randf(0.42, 0.56) + normal_x * randf(-sway * 1.4, sway * 1.4),
            y = start_point.y + dy * randf(0.42, 0.56) + normal_y * randf(-sway * 1.4, sway * 1.4)
        }, bounds)
        append_anchor(anchors, {
            x = start_point.x + dx * randf(0.72, 0.86) + normal_x * randf(-sway, sway),
            y = start_point.y + dy * randf(0.72, 0.86) + normal_y * randf(-sway, sway)
        }, bounds)
    elseif style == CURVE_STYLE_SPIRAL then
        local base_angle = math.atan(dy, dx)
        local orbit_start = randf(0.55, 0.95) * math.pi
        local orbit_end = orbit_start + randf(1.2, 2.5) * math.pi
        local orbit_radius = clamp(length * randf(0.16, 0.28), 48, 170)
        for i = 3, 1, -1 do
            local t = i / 3
            local angle = base_angle + orbit_start + (orbit_end - orbit_start) * (1 - t)
            local radius = orbit_radius * t
            append_anchor(anchors, {
                x = end_point.x + math.cos(angle) * radius,
                y = end_point.y + math.sin(angle) * radius
            }, bounds)
        end
    elseif style == CURVE_STYLE_LOOP then
        append_anchor(anchors, {
            x = start_point.x + dx * randf(0.12, 0.20) + normal_x * randf(-sway, sway),
            y = start_point.y + dy * randf(0.12, 0.20) + normal_y * randf(-sway, sway)
        }, bounds)

        local loop_center_x = start_point.x + dx * randf(0.32, 0.48)
        local loop_center_y = start_point.y + dy * randf(0.32, 0.48)
        local loop_rx = clamp(length * randf(0.12, 0.20), 35, 120)
        local loop_ry = clamp(length * randf(0.10, 0.18), 28, 96)
        local start_angle = randf(0, math.pi * 2)
        for i = 1, 3 do
            local theta = start_angle + i * (math.pi * 0.82)
            append_anchor(anchors, {
                x = loop_center_x + math.cos(theta) * loop_rx,
                y = loop_center_y + math.sin(theta) * loop_ry
            }, bounds)
        end

        append_anchor(anchors, {
            x = start_point.x + dx * randf(0.70, 0.82) + normal_x * randf(-sway, sway),
            y = start_point.y + dy * randf(0.70, 0.82) + normal_y * randf(-sway, sway)
        }, bounds)
    else
        local flip = math.random(0, 1) == 1 and 1 or -1
        append_anchor(anchors, {
            x = start_point.x + dx * randf(0.18, 0.28) + normal_x * sway * flip,
            y = start_point.y + dy * randf(0.18, 0.28) + normal_y * sway * flip
        }, bounds)
        append_anchor(anchors, {
            x = start_point.x + dx * randf(0.42, 0.58) - normal_x * sway * randf(0.9, 1.6) * flip,
            y = start_point.y + dy * randf(0.42, 0.58) - normal_y * sway * randf(0.9, 1.6) * flip
        }, bounds)
        append_anchor(anchors, {
            x = start_point.x + dx * randf(0.70, 0.84) + normal_x * sway * randf(0.7, 1.2) * flip,
            y = start_point.y + dy * randf(0.70, 0.84) + normal_y * sway * randf(0.7, 1.2) * flip
        }, bounds)
    end

    anchors[#anchors + 1] = copy_point(end_point)
    return anchors, style
end

local function build_bezier_segments(points)
    local segments = {}
    for index = 1, #points - 1 do
        local p0 = points[index - 1] or points[index]
        local p1 = points[index]
        local p2 = points[index + 1]
        local p3 = points[index + 2] or points[index + 1]

        local cp1 = {
            x = p1.x + (p2.x - p0.x) / 6,
            y = p1.y + (p2.y - p0.y) / 6
        }
        local cp2 = {
            x = p2.x - (p3.x - p1.x) / 6,
            y = p2.y - (p3.y - p1.y) / 6
        }

        segments[#segments + 1] = {
            p0 = copy_point(p1),
            p1 = cp1,
            p2 = cp2,
            p3 = copy_point(p2),
            length = point_distance(p1, p2)
        }
    end

    return segments
end

local function flatten_segments(segments)
    local flattened = {}
    local total_steps = 0

    for _, segment in ipairs(segments) do
        local spacing = randf(STEP_SPACING_MIN, STEP_SPACING_MAX)
        local steps = clamp(round(segment.length / spacing), MIN_MOVE_STEPS, MAX_MOVE_STEPS)
        local points = {}
        for index = 1, steps do
            local t = index / steps
            points[index] = cubic_point(segment.p0, segment.p1, segment.p2, segment.p3, t)
        end

        flattened[#flattened + 1] = {
            points = points,
            steps = steps
        }
        total_steps = total_steps + steps
    end

    return flattened, total_steps
end

local function build_delay_plan(total_steps, target_duration_ms)
    local weights = {}
    local total_weight = 0
    local burst_phase = randf(0, math.pi * 2)
    local burst_freq = randf(1.6, 2.8)

    for index = 1, total_steps do
        local t = (index - 1) / math.max(1, total_steps - 1)
        local accel_profile = math.sin(t * math.pi) ^ 1.45
        local burst = 1.0 + math.sin(t * math.pi * burst_freq + burst_phase) * 0.12
        local velocity = clamp((0.14 + 0.86 * accel_profile) * burst, 0.10, 1.22)
        local weight = 1.25 / velocity + randf(-0.05, 0.05)
        if weight < 0.12 then
            weight = 0.12
        end

        weights[index] = weight
        total_weight = total_weight + weight
    end

    local delays = {}
    local assigned = 0
    for index = 1, total_steps do
        local delay = math.max(0, round(target_duration_ms * weights[index] / math.max(total_weight, 0.01)))
        delays[index] = delay
        assigned = assigned + delay
    end

    local delta = target_duration_ms - assigned
    local guard = 0
    while delta ~= 0 and total_steps > 0 and guard < total_steps * 4 do
        local index = ((guard % total_steps) + 1)
        if delta > 0 then
            delays[index] = delays[index] + 1
            delta = delta - 1
        elseif delays[index] > 0 then
            delays[index] = delays[index] - 1
            delta = delta + 1
        end
        guard = guard + 1
    end

    return delays
end

local function build_motion_plan(start_point, end_point, bounds, opts)
    local distance = point_distance(start_point, end_point)
    if distance <= 3 then
        return {
            screen_x = round(end_point.x),
            screen_y = round(end_point.y),
            duration_ms = 0,
            distance = distance,
            style = 0,
            anchors = 1,
            steps = {
                {
                    x = round(end_point.x),
                    y = round(end_point.y),
                    delay_ms = 0
                }
            }
        }
    end

    local duration_ms = choose_move_duration_ms(distance, opts)
    local anchors, style = build_motion_anchors(start_point, end_point, bounds)
    local flattened, total_steps = flatten_segments(build_bezier_segments(anchors))
    local delays = build_delay_plan(total_steps, duration_ms)
    local steps = {}
    local written_steps = 0

    for _, segment in ipairs(flattened) do
        for _, point in ipairs(segment.points) do
            written_steps = written_steps + 1
            local tremor = randf(-0.35, 0.35)
            steps[#steps + 1] = {
                x = round(point.x + tremor),
                y = round(point.y - tremor),
                delay_ms = delays[written_steps] or 0
            }
        end
    end

    steps[#steps + 1] = {
        x = round(end_point.x),
        y = round(end_point.y),
        delay_ms = 0
    }

    return {
        screen_x = round(end_point.x),
        screen_y = round(end_point.y),
        duration_ms = duration_ms,
        distance = distance,
        style = style,
        anchors = #anchors,
        steps = steps
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

    local ok, result, err = xpcall(function()
        return fn()
    end, function(trace)
        return trace
    end)

    end_mouse_runtime(runtime)

    if not ok then
        return false, result
    end

    return true, result, err
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
            if point_distance(
                { x = cur_x, y = cur_y },
                { x = state.last_x, y = state.last_y }
            ) >= manual_override_distance then
                end_mouse_runtime(state.runtime)
                async_move_state = nil
                return true, {
                    cancel_reason = "manual_override",
                    screen_x = cur_x,
                    screen_y = cur_y,
                    distance = point_distance(
                        { x = cur_x, y = cur_y },
                        { x = state.last_x, y = state.last_y }
                    )
                }, true
            end
        end
    end

    local max_steps = math.max(6, tonumber(state.max_steps_per_tick) or 24)
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
    if type(mouse) ~= "table"
        or type(mouse.position) ~= "function"
        or type(mouse.move_to) ~= "function"
    then
        return false, "mouse.position/mouse.move_to is not available."
    end

    ensure_seeded()
    M.cancel_async_move()

    local bounds, bounds_err = resolve_bounds(opts and opts.hwnd or nil, opts)
    if not bounds then
        return false, bounds_err
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
        return false, "mouse.position failed."
    end

    local start_point = clamp_point_to_bounds({ x = start_x, y = start_y }, bounds)
    local end_point = clamp_point_to_bounds({ x = screen_x, y = screen_y }, bounds)
    local plan = build_motion_plan(start_point, end_point, bounds, opts)
    local runtime, runtime_err = begin_mouse_runtime(tostring(opts and opts.mouse_mode or "api"))
    if not runtime then
        return false, runtime_err
    end

    async_move_state = {
        runtime = runtime,
        steps = plan.steps or {},
        index = 1,
        next_at = type(sys) == "table" and type(sys.time) == "function" and sys.time() or 0,
        meta = plan,
        last_x = start_point.x,
        last_y = start_point.y,
        manual_override_distance = tonumber(opts and opts.manual_override_distance) or MANUAL_OVERRIDE_DISTANCE,
        max_steps_per_tick = tonumber(opts and opts.max_steps_per_tick) or 24
    }

    return true, plan
end

function M.start_async_random_move_in_window(opts)
    ensure_seeded()

    local bounds, err = resolve_bounds(opts and opts.hwnd or nil, opts)
    if not bounds then
        return false, err
    end

    if type(mouse) ~= "table" or type(mouse.position) ~= "function" then
        return false, "mouse.position is not available."
    end

    local start_x, start_y = mouse.position()
    if type(start_x) ~= "number" or type(start_y) ~= "number" then
        return false, "mouse.position failed."
    end

    local target_x = start_x
    local target_y = start_y
    for _ = 1, 6 do
        target_x = randf(bounds.left, bounds.right)
        target_y = randf(bounds.top, bounds.bottom)
        if point_distance({ x = start_x, y = start_y }, { x = target_x, y = target_y }) >= 60 then
            break
        end
    end

    return M.start_async_move(target_x, target_y, opts)
end

function M.move_to(screen_x, screen_y, opts)
    if type(mouse) ~= "table"
        or type(mouse.position) ~= "function"
        or type(mouse.move_to) ~= "function"
    then
        return false, "mouse.position/mouse.move_to is not available."
    end

    ensure_seeded()
    M.cancel_async_move()

    local bounds, bounds_err = resolve_bounds(opts and opts.hwnd or nil, opts)
    if not bounds then
        return false, bounds_err
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
        return false, "mouse.position failed."
    end

    local start_point = clamp_point_to_bounds({ x = start_x, y = start_y }, bounds)
    local end_point = clamp_point_to_bounds({ x = screen_x, y = screen_y }, bounds)
    local plan = build_motion_plan(start_point, end_point, bounds, opts)

    local ok, move_err = with_mouse_mode(tostring(opts and opts.mouse_mode or "api"), function()
        for _, step in ipairs(plan.steps or {}) do
            local moved = mouse.move_to(step.x, step.y)
            if moved == false then
                error("mouse.move_to failed.")
            end

            local delay_ms = math.max(0, tonumber(step.delay_ms) or 0)
            if delay_ms > 0 then
                sys.sleep(delay_ms)
            end
        end

        return plan
    end)

    if not ok then
        return false, move_err
    end

    return true, move_err
end

function M.move_and_click(screen_x, screen_y, opts)
    M.cancel_async_move()
    local ok, result_or_err = M.move_to(screen_x, screen_y, opts)
    if not ok then
        return false, result_or_err
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

    return true, result_or_err
end

function M.move_random_in_window(opts)
    M.cancel_async_move()
    ensure_seeded()

    local bounds, err = resolve_bounds(opts and opts.hwnd or nil, opts)
    if not bounds then
        return false, err
    end

    if type(mouse) ~= "table" or type(mouse.position) ~= "function" then
        return false, "mouse.position is not available."
    end

    local start_x, start_y = mouse.position()
    if type(start_x) ~= "number" or type(start_y) ~= "number" then
        return false, "mouse.position failed."
    end

    local target_x = start_x
    local target_y = start_y
    for _ = 1, 6 do
        target_x = randf(bounds.left, bounds.right)
        target_y = randf(bounds.top, bounds.bottom)
        if point_distance({ x = start_x, y = start_y }, { x = target_x, y = target_y }) >= 60 then
            break
        end
    end

    return M.move_to(target_x, target_y, opts)
end

return M
