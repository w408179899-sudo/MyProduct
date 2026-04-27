local HOTKEY_START = 0x2D
local HOTKEY_EXIT_CTRL = 0x11
local HOTKEY_EXIT = 0x7B
local POLL_INTERVAL_MS = 10

local CLICK_REPEAT_COUNT = 200
local HOLD_LEFT_DURING_MOVE = false
local DRAW_DOT_TRAIL = true
local DRAW_SAMPLE_INTERVAL_MS = 10
local DRAW_SAMPLE_INTERVAL_JITTER_MS = 4
local TARGET_RADIUS_X = 42
local TARGET_RADIUS_Y = 24
local STAGING_DISTANCE_MIN = 260
local STAGING_DISTANCE_MAX = 760
local MOVE_STEP_SPACING_MIN = 7.0
local MOVE_STEP_SPACING_MAX = 18.0
local MOVE_DELAY_MIN_MS = 1
local MOVE_DELAY_MAX_MS = 10
local CLICK_DOWN_MIN_MS = 22
local CLICK_DOWN_MAX_MS = 58
local MOVE_HOLD_MIN_MS = 18
local MOVE_HOLD_MAX_MS = 44
local PRE_CLICK_HOVER_MIN_MS = 18
local PRE_CLICK_HOVER_MAX_MS = 75
local POST_CLICK_HOVER_MIN_MS = 24
local POST_CLICK_HOVER_MAX_MS = 88
local MOVE_AWAY_PAUSE_MIN_MS = 55
local MOVE_AWAY_PAUSE_MAX_MS = 180
local DIRECTIONAL_JITTER_MIN = 28
local DIRECTIONAL_JITTER_MAX = 140
local TARGET_JITTER_MIN = 1.0
local TARGET_JITTER_MAX = 5.5
local DOT_DOWN_MIN_MS = 1
local DOT_DOWN_MAX_MS = 3
local CURVE_STYLE_SWING = 1
local CURVE_STYLE_SPIRAL = 2
local CURVE_STYLE_LOOP = 3
local CURVE_STYLE_ZIGZAG = 4
local EDGE_MARGIN = 24
local MOUSE_MODE = "api"

local start_hotkey_down = false
local stamp_dot

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

local function normalize(dx, dy)
    local length = math.sqrt(dx * dx + dy * dy)
    if length <= 0.0001 then
        return 0, 0, 0
    end
    return dx / length, dy / length, length
end

local function copy_point(point)
    return { x = point.x, y = point.y }
end

local function point_distance(a, b)
    local dx = b.x - a.x
    local dy = b.y - a.y
    return math.sqrt(dx * dx + dy * dy)
end

local function rotate_vector(dx, dy, angle)
    local c = math.cos(angle)
    local s = math.sin(angle)
    return dx * c - dy * s, dx * s + dy * c
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

local function smoothstep(t)
    return t * t * (3 - 2 * t)
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

local function should_exit()
    return is_hotkey_pressed(HOTKEY_EXIT_CTRL) and is_hotkey_pressed(HOTKEY_EXIT)
end

local function get_active_bounds()
    if type(wnd) ~= "table" or type(wnd.get_foreground) ~= "function" or type(wnd.wnd_rect) ~= "function" then
        return nil
    end

    local hwnd = wnd.get_foreground()
    if not hwnd or hwnd == 0 then
        return nil
    end

    local x, y, w, h = wnd.wnd_rect(hwnd)
    if not x or not y or not w or not h or w <= 0 or h <= 0 then
        return nil
    end

    return {
        left = x + EDGE_MARGIN,
        top = y + EDGE_MARGIN,
        right = x + w - EDGE_MARGIN,
        bottom = y + h - EDGE_MARGIN
    }
end

local function clamp_point_to_bounds(point, bounds)
    if not bounds then
        return point
    end

    return {
        x = clamp(point.x, bounds.left, bounds.right),
        y = clamp(point.y, bounds.top, bounds.bottom)
    }
end

local function random_target_point(center_x, center_y, bounds)
    local angle = randf(0, math.pi * 2)
    local radius_x = randf(2, TARGET_RADIUS_X)
    local radius_y = randf(2, TARGET_RADIUS_Y)
    local point = {
        x = center_x + math.cos(angle) * radius_x + randf(-TARGET_JITTER_MIN, TARGET_JITTER_MAX),
        y = center_y + math.sin(angle) * radius_y + randf(-TARGET_JITTER_MIN, TARGET_JITTER_MAX)
    }
    return clamp_point_to_bounds(point, bounds)
end

local function random_staging_point(center_x, center_y, bounds)
    if bounds and math.random() < 0.35 then
        return {
            x = randf(bounds.left, bounds.right),
            y = randf(bounds.top, bounds.bottom)
        }
    end

    local angle = randf(0, math.pi * 2)
    local distance = randf(STAGING_DISTANCE_MIN, STAGING_DISTANCE_MAX)
    local point = {
        x = center_x + math.cos(angle) * distance,
        y = center_y + math.sin(angle) * distance
    }
    return clamp_point_to_bounds(point, bounds)
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
    local sway = clamp(length * randf(0.18, 0.42), DIRECTIONAL_JITTER_MIN, DIRECTIONAL_JITTER_MAX * 2.2)

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
    local count = #points

    for index = 1, count - 1 do
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

        local seg_len = point_distance(p1, p2)
        segments[#segments + 1] = {
            p0 = copy_point(p1),
            p1 = cp1,
            p2 = cp2,
            p3 = copy_point(p2),
            length = seg_len
        }
    end

    return segments
end

local function velocity_factor(global_t)
    local curve = 0.34 + 1.12 * (math.sin(global_t * math.pi) ^ 0.86)
    return clamp(curve, 0.24, 1.40)
end

local function step_delay_ms(global_t, distance, speed_bias)
    local base_delay = randf(MOVE_DELAY_MIN_MS, MOVE_DELAY_MAX_MS)
    local velocity = velocity_factor(global_t)
    local distance_bias = clamp(220 / math.max(80, distance), 0.72, 1.18)
    local delay = base_delay * distance_bias / (velocity * (speed_bias or 1.0))
    return math.max(1, round(delay))
end

local function press_left()
    local ok = mouse.down("left")
    if ok == false then
        return false, "mouse.down failed"
    end
    return true
end

local function release_left()
    local ok = mouse.up("left")
    if ok == false then
        return false, "mouse.up failed"
    end
    return true
end

local function human_move(start_point, end_point, speed_bias, hold_left_down)
    local anchors = nil
    local style = nil
    anchors, style = build_motion_anchors(start_point, end_point, get_active_bounds())
    local segments = build_bezier_segments(anchors)
    local held = false
    local total_steps = 0
    local next_dot_at = nil

    for _, segment in ipairs(segments) do
        local spacing = randf(MOVE_STEP_SPACING_MIN, MOVE_STEP_SPACING_MAX)
        segment.steps = math.max(10, round(segment.length / spacing))
        total_steps = total_steps + segment.steps
    end

    if hold_left_down then
        local press_ok, press_err = press_left()
        if not press_ok then
            return false, press_err
        end
        held = true
        sys.sleep(round(randf(MOVE_HOLD_MIN_MS, MOVE_HOLD_MAX_MS)))
    elseif DRAW_DOT_TRAIL then
        next_dot_at = sys.time()
    end

    local written_steps = 0
    for _, segment in ipairs(segments) do
        for index = 1, segment.steps do
            if should_exit() then
                if held then
                    pcall(mouse.up, "left")
                end
                return false, "interrupted by exit hotkey"
            end

            local t = index / segment.steps
            local eased_t = smoothstep(t)
            local point = cubic_point(segment.p0, segment.p1, segment.p2, segment.p3, eased_t)
            local global_t = written_steps / math.max(1, total_steps - 1)
            local tremor = 1.25 * (1 - eased_t) * randf(0.35, 1.15)

            local x = point.x + randf(-tremor, tremor)
            local y = point.y + randf(-tremor, tremor)

            local ok = mouse.move_to(round(x), round(y))
            if ok == false then
                if held then
                    pcall(mouse.up, "left")
                end
                return false, "mouse.move_to failed"
            end

            if DRAW_DOT_TRAIL and not held then
                local now = sys.time()
                if not next_dot_at or now >= next_dot_at then
                    local dot_ok, dot_err = stamp_dot()
                    if not dot_ok then
                        return false, dot_err
                    end
                    next_dot_at = now + DRAW_SAMPLE_INTERVAL_MS
                        + round(randf(-DRAW_SAMPLE_INTERVAL_JITTER_MS, DRAW_SAMPLE_INTERVAL_JITTER_MS))
                end
            end

            sys.sleep(step_delay_ms(global_t, segment.length, (speed_bias or 1.0) * randf(0.78, 1.32)))
            written_steps = written_steps + 1
        end
    end

    local finalize_ok = mouse.move_to(round(end_point.x), round(end_point.y))
    if finalize_ok == false then
        if held then
            pcall(mouse.up, "left")
        end
        return false, "mouse.move_to finalize failed"
    end

    if held then
        sys.sleep(round(randf(MOVE_HOLD_MIN_MS, MOVE_HOLD_MAX_MS)))
        local release_ok, release_err = release_left()
        if not release_ok then
            return false, release_err
        end
    end

    log.info(string.format(
        "Move style=%d anchors=%d from=(%.1f, %.1f) to=(%.1f, %.1f)",
        style or 0,
        #anchors,
        start_point.x,
        start_point.y,
        end_point.x,
        end_point.y
    ))

    return true
end

local function human_click()
    local ok = mouse.down("left")
    if ok == false then
        return false, "mouse.down failed"
    end

    sys.sleep(round(randf(CLICK_DOWN_MIN_MS, CLICK_DOWN_MAX_MS)))

    ok = mouse.up("left")
    if ok == false then
        return false, "mouse.up failed"
    end

    return true
end

stamp_dot = function()
    local press_ok, press_err = press_left()
    if not press_ok then
        return false, press_err
    end

    sys.sleep(round(randf(DOT_DOWN_MIN_MS, DOT_DOWN_MAX_MS)))

    local release_ok, release_err = release_left()
    if not release_ok then
        return false, release_err
    end

    return true
end

local function log_point(label, point)
    log.info(string.format("%s %.1f, %.1f", label, point.x, point.y))
end

local function run_click_loop()
    if type(mouse) ~= "table" or type(mouse.position) ~= "function" or type(mouse.move_to) ~= "function" then
        return false, "mouse API is not available"
    end

    local center_x, center_y = mouse.position()
    if not center_x or not center_y then
        return false, "mouse.position failed"
    end

    local bounds = get_active_bounds()
    local target_center = clamp_point_to_bounds({ x = center_x, y = center_y }, bounds)
    local old_mode = type(mouse.get_mode) == "function" and mouse.get_mode() or nil
    local old_trajectory = type(mouse.get_trajectory) == "function" and mouse.get_trajectory() or nil

    if type(mouse.set_mode) == "function" then
        mouse.set_mode(MOUSE_MODE)
    end
    if type(mouse.set_trajectory) == "function" then
        mouse.set_trajectory("none")
    end

    local ok, err = xpcall(function()
        log.info(string.format(
            "Human click loop start | repeat=%d center=(%.1f, %.1f) dot_trail=%s sample=%dms",
            CLICK_REPEAT_COUNT,
            target_center.x,
            target_center.y,
            tostring(DRAW_DOT_TRAIL),
            DRAW_SAMPLE_INTERVAL_MS
        ))

        local current_point = {
            x = center_x,
            y = center_y
        }

        local initial_staging = random_staging_point(target_center.x, target_center.y, bounds)
        local move_ok, move_err = human_move(current_point, initial_staging, 1.15, HOLD_LEFT_DURING_MOVE)
        if not move_ok then
            error(move_err)
        end
        current_point = initial_staging
        log_point("Initial staging ->", current_point)

        for index = 1, CLICK_REPEAT_COUNT do
            local target_point = random_target_point(target_center.x, target_center.y, bounds)
            move_ok, move_err = human_move(current_point, target_point, 1.05, HOLD_LEFT_DURING_MOVE)
            if not move_ok then
                error(move_err)
            end

            current_point = target_point
            log.info(string.format("Loop %d/%d target=(%.1f, %.1f)", index, CLICK_REPEAT_COUNT, target_point.x, target_point.y))

            sys.sleep(round(randf(PRE_CLICK_HOVER_MIN_MS, PRE_CLICK_HOVER_MAX_MS)))

            local click_ok, click_err = human_click()
            if not click_ok then
                error(click_err)
            end

            sys.sleep(round(randf(POST_CLICK_HOVER_MIN_MS, POST_CLICK_HOVER_MAX_MS)))

            local staging_point = random_staging_point(target_center.x, target_center.y, bounds)
            move_ok, move_err = human_move(current_point, staging_point, 1.18, HOLD_LEFT_DURING_MOVE)
            if not move_ok then
                error(move_err)
            end

            current_point = staging_point
            log.info(string.format("Loop %d/%d away=(%.1f, %.1f)", index, CLICK_REPEAT_COUNT, staging_point.x, staging_point.y))

            if index < CLICK_REPEAT_COUNT then
                sys.sleep(round(randf(MOVE_AWAY_PAUSE_MIN_MS, MOVE_AWAY_PAUSE_MAX_MS)))
            end
        end
    end, debug.traceback)

    if old_mode and type(mouse.set_mode) == "function" then
        pcall(mouse.set_mode, old_mode)
    end
    if old_trajectory and type(mouse.set_trajectory) == "function" then
        pcall(mouse.set_trajectory, old_trajectory)
    end

    if not ok then
        return false, err
    end

    return true
end

if type(hotkey) ~= "table" or type(hotkey.start) ~= "function" or type(hotkey.is_running) ~= "function" then
    error("hotkey module is not available")
end

math.randomseed(sys.time())
math.random()
math.random()
math.random()

log.info("Move cursor onto the target button, then press Insert")
log.info(string.format("Script will build %d random button-click trajectories", CLICK_REPEAT_COUNT))
log.info("Press Ctrl+F12 to exit")

local started_hotkey = false
if not hotkey.is_running() then
    hotkey.start(10)
    started_hotkey = true
end

while true do
    if should_exit() then
        log.info("Exit hotkey pressed")
        break
    end

    local fired
    start_hotkey_down, fired = update_hotkey_edge(HOTKEY_START, start_hotkey_down)
    if fired then
        local ok, err = run_click_loop()
        if ok then
            log.info("Human click loop completed")
        else
            log.error("Human click loop failed: " .. tostring(err))
        end
    end

    sys.sleep(POLL_INTERVAL_MS)
end

if started_hotkey and hotkey.is_running() and type(hotkey.stop) == "function" then
    hotkey.stop()
end
