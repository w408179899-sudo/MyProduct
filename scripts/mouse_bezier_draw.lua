local HOTKEY_DRAW = 0x2D
local HOTKEY_EXIT_CTRL = 0x11
local HOTKEY_EXIT = 0x7B
local POLL_INTERVAL_MS = 10

local MOUSE_MODE = "api"
local STROKE_SCALE_MIN = 0.90
local STROKE_SCALE_MAX = 1.12
local STROKE_ROTATE_DEG = 7.5
local CONTROL_JITTER_MIN = 6
local CONTROL_JITTER_MAX = 22
local POINT_JITTER_MIN = 2
local POINT_JITTER_MAX = 12
local STEP_SPACING_MIN = 4.0
local STEP_SPACING_MAX = 6.5
local BASE_DELAY_MIN_MS = 3
local BASE_DELAY_MAX_MS = 8
local SEGMENT_PAUSE_CHANCE = 0.20
local SEGMENT_PAUSE_MIN_MS = 10
local SEGMENT_PAUSE_MAX_MS = 35
local PRESS_HOLD_MS = 18
local RELEASE_HOLD_MS = 14

local STROKE_TEMPLATE = {
    { x = 0,   y = 0   },
    { x = 30,  y = 6   },
    { x = 58,  y = 10  },
    { x = 22,  y = 18  },
    { x = 70,  y = 28  },
    { x = 142, y = 38  },
    { x = 214, y = 26  },
    { x = 252, y = -4  },
    { x = 266, y = -38 },
    { x = 238, y = -78 },
    { x = 282, y = -102 },
    { x = 344, y = -82 },
    { x = 406, y = -42 },
    { x = 470, y = 2   },
    { x = 528, y = 40  },
    { x = 576, y = 78  },
    { x = 614, y = 34  },
    { x = 594, y = -6  },
    { x = 568, y = -44 },
    { x = 604, y = -18 },
    { x = 650, y = 22  },
    { x = 706, y = 60  }
}

local draw_hotkey_down = false

local function randf(min_value, max_value)
    return min_value + (max_value - min_value) * math.random()
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

local function round(value)
    if value >= 0 then
        return math.floor(value + 0.5)
    end
    return math.ceil(value - 0.5)
end

local function copy_point(point)
    return { x = point.x, y = point.y }
end

local function smoothstep(t)
    return t * t * (3 - 2 * t)
end

local function ease_in_out_sine(t)
    return 0.5 - 0.5 * math.cos(math.pi * t)
end

local function rotate_point(x, y, angle_rad)
    local c = math.cos(angle_rad)
    local s = math.sin(angle_rad)
    return x * c - y * s, x * s + y * c
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

local function build_anchor_points(origin_x, origin_y)
    local scale = randf(STROKE_SCALE_MIN, STROKE_SCALE_MAX)
    local angle = math.rad(randf(-STROKE_ROTATE_DEG, STROKE_ROTATE_DEG))
    local points = {}

    for index, template_point in ipairs(STROKE_TEMPLATE) do
        local px = template_point.x * scale
        local py = template_point.y * scale
        px, py = rotate_point(px, py, angle)

        local jitter = 0
        if index ~= 1 and index ~= #STROKE_TEMPLATE then
            jitter = randf(POINT_JITTER_MIN, POINT_JITTER_MAX)
        end

        if jitter > 0 then
            px = px + randf(-jitter, jitter)
            py = py + randf(-jitter, jitter)
        end

        points[index] = {
            x = origin_x + px,
            y = origin_y + py
        }
    end

    points[1] = { x = origin_x, y = origin_y }
    return points
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

        local dir_x, dir_y, length = normalize(p2.x - p1.x, p2.y - p1.y)
        local normal_x = -dir_y
        local normal_y = dir_x
        local control_jitter = randf(CONTROL_JITTER_MIN, CONTROL_JITTER_MAX)
        local tangent_jitter = control_jitter * 0.40
        local weight = clamp(length / 120, 0.55, 1.35)

        cp1.x = cp1.x + normal_x * randf(-control_jitter, control_jitter) * weight
        cp1.y = cp1.y + normal_y * randf(-control_jitter, control_jitter) * weight
        cp1.x = cp1.x + dir_x * randf(-tangent_jitter, tangent_jitter)
        cp1.y = cp1.y + dir_y * randf(-tangent_jitter, tangent_jitter)

        cp2.x = cp2.x + normal_x * randf(-control_jitter, control_jitter) * weight
        cp2.y = cp2.y + normal_y * randf(-control_jitter, control_jitter) * weight
        cp2.x = cp2.x - dir_x * randf(-tangent_jitter, tangent_jitter)
        cp2.y = cp2.y - dir_y * randf(-tangent_jitter, tangent_jitter)

        segments[#segments + 1] = {
            p0 = copy_point(p1),
            p1 = cp1,
            p2 = cp2,
            p3 = copy_point(p2),
            length = length
        }
    end

    return segments
end

local function flatten_segments(segments)
    local flattened = {}
    local total_points = 0

    for _, segment in ipairs(segments) do
        local spacing = randf(STEP_SPACING_MIN, STEP_SPACING_MAX)
        local steps = math.max(14, round(segment.length / spacing))
        local samples = {}

        for index = 1, steps do
            local t = index / steps
            samples[index] = cubic_point(segment.p0, segment.p1, segment.p2, segment.p3, t)
        end

        flattened[#flattened + 1] = {
            points = samples,
            steps = steps
        }
        total_points = total_points + steps
    end

    return flattened, total_points
end

local function velocity_factor(global_t, local_t)
    local long_curve = 0.32 + 0.88 * (math.sin(global_t * math.pi) ^ 0.80)
    local local_curve = 0.75 + 0.35 * math.sin(local_t * math.pi)
    return clamp(long_curve * local_curve, 0.20, 1.35)
end

local function step_delay_ms(global_t, local_t)
    local base_delay = randf(BASE_DELAY_MIN_MS, BASE_DELAY_MAX_MS)
    local velocity = velocity_factor(global_t, local_t)
    local micro_pause = randf(-0.8, 1.6)
    return math.max(1, round(base_delay / velocity + micro_pause))
end

local function is_hotkey_pressed(vk)
    if type(hotkey) ~= "table" or type(hotkey.is_pressed) ~= "function" then
        return false
    end
    return hotkey.is_pressed(vk)
end

local function should_exit()
    return is_hotkey_pressed(HOTKEY_EXIT_CTRL) and is_hotkey_pressed(HOTKEY_EXIT)
end

local function log_point(label, point)
    log.info(string.format("%s: %.1f, %.1f", label, point.x, point.y))
end

local function draw_human_curve()
    if type(mouse) ~= "table" or type(mouse.position) ~= "function" then
        return false, "mouse module is not available"
    end

    local start_x, start_y = mouse.position()
    if not start_x or not start_y then
        return false, "mouse.position failed"
    end

    local old_mode = type(mouse.get_mode) == "function" and mouse.get_mode() or nil
    local old_trajectory = type(mouse.get_trajectory) == "function" and mouse.get_trajectory() or nil

    if type(mouse.set_mode) == "function" then
        mouse.set_mode(MOUSE_MODE)
    end
    if type(mouse.set_trajectory) == "function" then
        mouse.set_trajectory("none")
    end

    local anchors = build_anchor_points(start_x, start_y)
    local segments = build_bezier_segments(anchors)
    local flattened, total_points = flatten_segments(segments)

    log.info(string.format("Bezier draw start | anchors=%d segments=%d points=%d", #anchors, #segments, total_points))
    log_point("Bezier draw origin", anchors[1])
    log_point("Bezier draw end", anchors[#anchors])

    local ok, err = xpcall(function()
        local moved = mouse.move_to(round(start_x), round(start_y))
        if moved == false then
            error("mouse.move_to start failed")
        end

        local down_ok = mouse.down("left")
        if down_ok == false then
            error("mouse.down failed")
        end

        sys.sleep(PRESS_HOLD_MS)

        local written_points = 0
        for _, segment in ipairs(flattened) do
            for index, point in ipairs(segment.points) do
                if should_exit() then
                    error("draw interrupted by exit hotkey")
                end

                local global_t = written_points / math.max(1, total_points - 1)
                local local_t = index / math.max(1, segment.steps)
                local eased_t = ease_in_out_sine(local_t)

                local x = point.x
                local y = point.y

                local tremor = 0.6 * (1 - smoothstep(global_t))
                x = x + randf(-tremor, tremor)
                y = y + randf(-tremor, tremor)

                local move_ok = mouse.move_to(round(x), round(y))
                if move_ok == false then
                    error("mouse.move_to failed")
                end

                sys.sleep(step_delay_ms(global_t, eased_t))
                written_points = written_points + 1
            end

            if math.random() < SEGMENT_PAUSE_CHANCE then
                sys.sleep(round(randf(SEGMENT_PAUSE_MIN_MS, SEGMENT_PAUSE_MAX_MS)))
            end
        end

        sys.sleep(RELEASE_HOLD_MS)
        mouse.up("left")
    end, debug.traceback)

    if type(mouse.up) == "function" then
        pcall(mouse.up, "left")
    end
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

local function update_hotkey_edge(vk, was_down)
    local down = is_hotkey_pressed(vk)
    local fired = down and not was_down
    return down, fired
end

if type(hotkey) ~= "table" or type(hotkey.start) ~= "function" or type(hotkey.is_running) ~= "function" then
    error("hotkey module is not available")
end

math.randomseed(sys.time())
math.random()
math.random()
math.random()

log.info("Press Insert to draw a human-like bezier stroke from current mouse position")
log.info("Press Ctrl+F12 to exit")

local started_hotkey = false
if not hotkey.is_running() then
    hotkey.start(10)
    started_hotkey = true
end

local running = true
while running do
    local exit_combo = is_hotkey_pressed(HOTKEY_EXIT_CTRL) and is_hotkey_pressed(HOTKEY_EXIT)
    if exit_combo then
        log.info("Exit hotkey pressed")
        break
    end

    local draw_pressed
    draw_hotkey_down, draw_pressed = update_hotkey_edge(HOTKEY_DRAW, draw_hotkey_down)
    if draw_pressed then
        local ok, err = draw_human_curve()
        if ok then
            log.info("Bezier draw completed")
        else
            log.error("Bezier draw failed: " .. tostring(err))
        end
    end

    sys.sleep(POLL_INTERVAL_MS)
end

if started_hotkey and type(hotkey) == "table" and type(hotkey.stop) == "function" and hotkey.is_running() then
    hotkey.stop()
end
