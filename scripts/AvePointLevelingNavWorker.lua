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
    if text == "true" then
        return true
    end
    if text == "false" then
        return false
    end

    return false
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

local SHARE_PREFIX = tostring(share_prefix or "")
local PROCESS_NAME = tostring(process_name or "")
local RUNTIME_MODE = tostring(runtime_mode or "")
local UPDATE_INTERVAL_MS = 40
local MAX_ROUTE_POINTS = 1024

if SHARE_PREFIX == "" then
    error("share_prefix is required for AvePointLevelingNavWorker")
end

local nav = load_nav_module()
local last_version = -1
local last_route_version = -1
local next_move_at = 0
local last_issue_at = 0
local route_points = nil
local route_index = 1
local route_next_switch_at = 0
local route_point_started_at = 0
local route_point_best_distance = math.huge
local route_point_track_index = 0
local last_route_mode = ""
local last_route_signature = ""
local last_route_count = -1
local last_route_source = ""

if type(task) == "table" and type(task.on_stop) == "function" then
    task.on_stop(function()
        running = false
        share_set(SHARE_PREFIX, "worker_status", "stopped")
        share_set(SHARE_PREFIX, "heartbeat_at", type(sys) == "table" and type(sys.time) == "function" and sys.time() or 0)
    end)
end

local function ensure_nav_ready()
    if type(nav) ~= "table" or type(nav.ensure_initialized) ~= "function" then
        return false, "nav.ensure_initialized is unavailable."
    end

    local target = PROCESS_NAME ~= "" and PROCESS_NAME or nil
    local mode = RUNTIME_MODE ~= "" and RUNTIME_MODE or nil
    return nav.ensure_initialized(target, mode)
end

local function clear_move_feedback(now_ms)
    share_set(SHARE_PREFIX, "heartbeat_at", now_ms)
    share_set(SHARE_PREFIX, "last_error", nil)
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

local function safe_move_call(target_x, target_y)
    local ok, move_ok, move_err = pcall(nav.move_call, target_x, target_y, {
        move_call_mouse_sync = {
            enabled = false
        }
    })
    if ok then
        return move_ok, move_err
    end

    local message = tostring(move_ok or "")
    if is_stop_interrupt(message) then
        running = false
        return false, message, true
    end

    error(move_ok)
end

local function safe_player_pos()
    if type(nav) ~= "table" or type(nav.player_pos) ~= "function" then
        return nil, nil, nil, "nav.player_pos is unavailable."
    end

    local ok, x, y, z = pcall(nav.player_pos)
    if ok then
        return x, y, z
    end

    local message = tostring(x or "")
    if is_stop_interrupt(message) then
        running = false
        return nil, nil, nil, message
    end

    error(x)
end

local function distance_2d(ax, ay, bx, by)
    local dx = (tonumber(ax) or 0) - (tonumber(bx) or 0)
    local dy = (tonumber(ay) or 0) - (tonumber(by) or 0)
    return math.sqrt(dx * dx + dy * dy)
end

local function next_route_index(index, count)
    count = math.max(1, tonumber(count) or 1)
    index = (tonumber(index) or 1) + 1
    if index > count then
        index = 1
    end
    return index
end

local function load_route_points(prefix, count)
    local points = {}
    count = math.min(MAX_ROUTE_POINTS, math.max(0, tonumber(count) or 0))
    for index = 1, count do
        local x = as_number(share_get(prefix, "route_point_" .. index .. "_x"))
        local y = as_number(share_get(prefix, "route_point_" .. index .. "_y"))
        local z = as_number(share_get(prefix, "route_point_" .. index .. "_z"))
        local original_index = as_number(share_get(prefix, "route_point_" .. index .. "_index"))
        if x ~= nil and y ~= nil then
            points[#points + 1] = {
                x = x,
                y = y,
                z = z,
                index = original_index or index
            }
        end
    end
    return points
end

local function select_route_start_index(points, player_x, player_y, arrive_distance)
    if type(points) ~= "table" or #points <= 0 then
        return 1
    end
    if player_x == nil or player_y == nil then
        return 1
    end

    local nearest_index = 1
    local nearest_distance = math.huge
    for index, point in ipairs(points) do
        local point_distance = distance_2d(player_x, player_y, point.x, point.y)
        if point_distance < nearest_distance then
            nearest_distance = point_distance
            nearest_index = index
        end
    end

    if nearest_distance <= (tonumber(arrive_distance) or 0) then
        return next_route_index(nearest_index, #points)
    end
    return nearest_index
end

local function select_path_route_start_index(points, player_x, player_y, arrive_distance, direction)
    if type(points) ~= "table" or #points <= 0 then
        return 1
    end
    if player_x == nil or player_y == nil then
        return direction == -1 and #points or 1
    end

    local nearest_index = 1
    local nearest_distance = math.huge
    for index, point in ipairs(points) do
        local point_distance = distance_2d(player_x, player_y, point.x, point.y)
        if point_distance < nearest_distance then
            nearest_distance = point_distance
            nearest_index = index
        end
    end

    if nearest_distance <= (tonumber(arrive_distance) or 0) then
        return nearest_index + (direction == -1 and -1 or 1)
    end
    return nearest_index
end

local function reset_route_point_tracking(now_ms)
    route_point_started_at = now_ms
    route_point_best_distance = math.huge
    route_point_track_index = route_index
end

share_set(SHARE_PREFIX, "worker_status", "starting")
share_set(SHARE_PREFIX, "last_error", nil)
share_set(SHARE_PREFIX, "last_issue_at", 0)
share_set(SHARE_PREFIX, "heartbeat_at", 0)

while running do
    local now_ms = type(sys) == "table" and type(sys.time) == "function" and sys.time() or 0
    share_set(SHARE_PREFIX, "heartbeat_at", now_ms)
    if not running then
        break
    end

    if as_boolean(share_get(SHARE_PREFIX, "stop")) then
        break
    end
    if not running then
        break
    end

    local paused = as_boolean(share_get(SHARE_PREFIX, "paused"))
    local mode = tostring(share_get(SHARE_PREFIX, "mode") or "target")
    local version = as_number(share_get(SHARE_PREFIX, "target_version")) or 0
    local route_version = as_number(share_get(SHARE_PREFIX, "route_version")) or 0
    local route_signature = tostring(share_get(SHARE_PREFIX, "route_signature") or "")
    local target_source = tostring(share_get(SHARE_PREFIX, "target_source") or "")
    local target_x = as_number(share_get(SHARE_PREFIX, "target_x"))
    local target_y = as_number(share_get(SHARE_PREFIX, "target_y"))
    local move_interval_floor_ms = math.max(30, as_number(share_get(SHARE_PREFIX, "move_interval_floor_ms")) or 80)
    local move_interval_ms = math.max(
        move_interval_floor_ms,
        as_number(share_get(SHARE_PREFIX, "move_interval_ms")) or 900
    )
    if not running then
        break
    end

    if paused then
        share_set(SHARE_PREFIX, "worker_status", "paused")
        clear_move_feedback(now_ms)
        local slept = safe_sleep(UPDATE_INTERVAL_MS)
        if slept == false then
            break
        end
    elseif mode == "path_route" then
        local route_count = as_number(share_get(SHARE_PREFIX, "route_count")) or 0
        local arrive_distance = math.max(80, as_number(share_get(SHARE_PREFIX, "route_arrive_distance")) or 120)
        local direction = as_number(share_get(SHARE_PREFIX, "route_direction")) or 1
        direction = direction == -1 and -1 or 1
        local stuck_skip_ms = math.max(2000, as_number(share_get(SHARE_PREFIX, "route_stuck_skip_ms")) or 2000)
        local progress_reset_distance = math.max(40, as_number(share_get(SHARE_PREFIX, "route_progress_reset_distance")) or 80)
        if not running then
            break
        end

        if route_version <= 0 or route_count <= 0 then
            share_set(SHARE_PREFIX, "worker_status", "path_idle")
            clear_move_feedback(now_ms)
            local slept = safe_sleep(UPDATE_INTERVAL_MS)
            if slept == false then
                break
            end
        else
            local nav_ok, nav_err = ensure_nav_ready()
            if not nav_ok then
                share_set(SHARE_PREFIX, "worker_status", "nav_wait")
                share_set(SHARE_PREFIX, "last_error", tostring(nav_err or "nav init failed"))
                local slept = safe_sleep(math.max(UPDATE_INTERVAL_MS, 200))
                if slept == false then
                    break
                end
            else
                local player_x, player_y, _, pos_err = safe_player_pos()
                if not running then
                    break
                end
                if route_version ~= last_route_version
                    or last_route_mode ~= mode
                    or route_count ~= last_route_count
                    or route_signature ~= last_route_signature
                    or target_source ~= last_route_source
                then
                    route_points = load_route_points(SHARE_PREFIX, route_count)
                    route_index = select_path_route_start_index(route_points, player_x, player_y, arrive_distance, direction)
                    next_move_at = 0
                    last_route_version = route_version
                    last_route_mode = mode
                    last_route_signature = route_signature
                    last_route_count = route_count
                    last_route_source = target_source
                    reset_route_point_tracking(now_ms)
                end

                if type(route_points) ~= "table" or #route_points <= 0 then
                    share_set(SHARE_PREFIX, "worker_status", "path_empty")
                    share_set(SHARE_PREFIX, "last_error", "path_route has no usable points")
                elseif route_index < 1 or route_index > #route_points then
                    share_set(SHARE_PREFIX, "worker_status", "path_done")
                    clear_move_feedback(now_ms)
                else
                    if route_point_track_index ~= route_index then
                        reset_route_point_tracking(now_ms)
                    end

                    local point = route_points[route_index]
                    local path_distance = nil
                    if player_x ~= nil and player_y ~= nil and type(point) == "table" then
                        path_distance = distance_2d(player_x, player_y, point.x, point.y)
                    elseif pos_err ~= nil then
                        share_set(SHARE_PREFIX, "last_error", tostring(pos_err))
                    end

                    if path_distance ~= nil then
                        local best_distance = tonumber(route_point_best_distance) or math.huge
                        if path_distance < best_distance then
                            route_point_best_distance = path_distance
                            if best_distance == math.huge or path_distance <= best_distance - progress_reset_distance then
                                route_point_started_at = now_ms
                            end
                        end

                        if path_distance <= arrive_distance then
                            route_index = route_index + direction
                            reset_route_point_tracking(now_ms)
                            next_move_at = 0
                            point = route_points[route_index]
                        elseif now_ms - (tonumber(route_point_started_at) or now_ms) >= stuck_skip_ms then
                            local next_index = route_index + direction
                            if next_index >= 1 and next_index <= #route_points then
                                route_index = next_index
                                reset_route_point_tracking(now_ms)
                                next_move_at = 0
                                point = route_points[route_index]
                                share_set(SHARE_PREFIX, "worker_status", "path_skip")
                            end
                        end
                    end

                    if route_index < 1 or route_index > #route_points or type(point) ~= "table" then
                        share_set(SHARE_PREFIX, "worker_status", "path_done")
                        clear_move_feedback(now_ms)
                    else
                        local route_point_index = tonumber(point.index) or route_index
                        share_set(SHARE_PREFIX, "worker_status", "path_route")
                        share_set(SHARE_PREFIX, "target_path_index", route_point_index)
                        share_set(SHARE_PREFIX, "last_route_mode", "path_route")
                        share_set(SHARE_PREFIX, "last_route_index", route_index)
                        share_set(SHARE_PREFIX, "last_route_count", #route_points)
                        share_set(SHARE_PREFIX, "last_route_direction", direction)
                        share_set(SHARE_PREFIX, "last_route_distance", path_distance)
                        share_set(SHARE_PREFIX, "last_route_point_x", point.x)
                        share_set(SHARE_PREFIX, "last_route_point_y", point.y)
                        share_set(SHARE_PREFIX, "last_route_original_index", route_point_index)
                        if now_ms >= next_move_at then
                            local move_ok, move_err, interrupted = safe_move_call(point.x, point.y)
                            if interrupted == true then
                                break
                            end
                            if move_ok then
                                last_issue_at = now_ms
                                share_set(SHARE_PREFIX, "last_target_x", point.x)
                                share_set(SHARE_PREFIX, "last_target_y", point.y)
                                share_set(SHARE_PREFIX, "last_target_path_index", route_point_index)
                                share_set(SHARE_PREFIX, "last_target_version", route_version)
                                share_set(SHARE_PREFIX, "target_path_index", route_point_index)
                                share_set(SHARE_PREFIX, "last_error", nil)
                                share_set(SHARE_PREFIX, "last_issue_at", now_ms)
                                next_move_at = now_ms + move_interval_ms
                            else
                                share_set(SHARE_PREFIX, "worker_status", "move_error")
                                share_set(SHARE_PREFIX, "last_error", tostring(move_err or "MoveTo failed"))
                                next_move_at = now_ms + math.min(move_interval_ms, 300)
                            end
                        end
                    end
                end

                local sleep_for = UPDATE_INTERVAL_MS
                if next_move_at > now_ms then
                    sleep_for = math.min(UPDATE_INTERVAL_MS, math.max(5, next_move_at - now_ms))
                end
                local slept = safe_sleep(sleep_for)
                if slept == false then
                    break
                end
            end
        end
    elseif mode == "route_loop" then
        local route_count = as_number(share_get(SHARE_PREFIX, "route_count")) or 0
        local arrive_distance = math.max(80, as_number(share_get(SHARE_PREFIX, "route_arrive_distance")) or 220)
        local switch_ms = math.max(300, as_number(share_get(SHARE_PREFIX, "route_switch_ms")) or 2400)
        if not running then
            break
        end

        if route_version <= 0 or route_count <= 0 then
            share_set(SHARE_PREFIX, "worker_status", "route_idle")
            clear_move_feedback(now_ms)
            local slept = safe_sleep(UPDATE_INTERVAL_MS)
            if slept == false then
                break
            end
        else
            local nav_ok, nav_err = ensure_nav_ready()
            if not nav_ok then
                share_set(SHARE_PREFIX, "worker_status", "nav_wait")
                share_set(SHARE_PREFIX, "last_error", tostring(nav_err or "nav init failed"))
                local slept = safe_sleep(math.max(UPDATE_INTERVAL_MS, 200))
                if slept == false then
                    break
                end
            else
                local player_x, player_y, _, pos_err = safe_player_pos()
                if not running then
                    break
                end
                if route_version ~= last_route_version
                    or last_route_mode ~= mode
                    or route_count ~= last_route_count
                    or route_signature ~= last_route_signature
                    or target_source ~= last_route_source
                then
                    route_points = load_route_points(SHARE_PREFIX, route_count)
                    route_index = select_route_start_index(route_points, player_x, player_y, arrive_distance)
                    route_next_switch_at = now_ms + switch_ms
                    next_move_at = 0
                    last_route_version = route_version
                    last_route_mode = mode
                    last_route_signature = route_signature
                    last_route_count = route_count
                    last_route_source = target_source
                    reset_route_point_tracking(now_ms)
                end

                if type(route_points) ~= "table" or #route_points <= 0 then
                    share_set(SHARE_PREFIX, "worker_status", "route_empty")
                    share_set(SHARE_PREFIX, "last_error", "route_loop has no usable points")
                else
                    if route_index < 1 or route_index > #route_points then
                        route_index = 1
                    end

                    local point = route_points[route_index]
                    local route_distance = nil
                    if player_x ~= nil and player_y ~= nil and type(point) == "table" then
                        route_distance = distance_2d(player_x, player_y, point.x, point.y)
                    elseif pos_err ~= nil then
                        share_set(SHARE_PREFIX, "last_error", tostring(pos_err))
                    end

                    if type(point) == "table"
                        and (
                            (route_distance ~= nil and route_distance <= arrive_distance)
                            or now_ms >= route_next_switch_at
                        )
                    then
                        route_index = next_route_index(route_index, #route_points)
                        point = route_points[route_index]
                        route_next_switch_at = now_ms + switch_ms
                        next_move_at = 0
                    end

                    local route_point_index = tonumber(type(point) == "table" and point.index) or route_index
                    share_set(SHARE_PREFIX, "worker_status", "route_loop")
                    share_set(SHARE_PREFIX, "target_path_index", route_point_index)
                    share_set(SHARE_PREFIX, "last_route_mode", "route_loop")
                    share_set(SHARE_PREFIX, "last_route_index", route_index)
                    share_set(SHARE_PREFIX, "last_route_count", #route_points)
                    share_set(SHARE_PREFIX, "last_route_direction", 1)
                    share_set(SHARE_PREFIX, "last_route_distance", route_distance)
                    if type(point) == "table" then
                        share_set(SHARE_PREFIX, "last_route_point_x", point.x)
                        share_set(SHARE_PREFIX, "last_route_point_y", point.y)
                    end
                    share_set(SHARE_PREFIX, "last_route_original_index", route_point_index)
                    if now_ms >= next_move_at and type(point) == "table" then
                        local move_ok, move_err, interrupted = safe_move_call(point.x, point.y)
                        if interrupted == true then
                            break
                        end
                        if move_ok then
                            last_issue_at = now_ms
                            share_set(SHARE_PREFIX, "last_target_x", point.x)
                            share_set(SHARE_PREFIX, "last_target_y", point.y)
                            share_set(SHARE_PREFIX, "last_target_path_index", route_point_index)
                            share_set(SHARE_PREFIX, "last_target_version", route_version)
                            share_set(SHARE_PREFIX, "target_path_index", route_point_index)
                            share_set(SHARE_PREFIX, "last_error", nil)
                            share_set(SHARE_PREFIX, "last_issue_at", now_ms)
                            next_move_at = now_ms + move_interval_ms
                        else
                            share_set(SHARE_PREFIX, "worker_status", "move_error")
                            share_set(SHARE_PREFIX, "last_error", tostring(move_err or "MoveTo failed"))
                            next_move_at = now_ms + math.min(move_interval_ms, 300)
                        end
                    end
                end

                local sleep_for = UPDATE_INTERVAL_MS
                if next_move_at > now_ms then
                    sleep_for = math.min(UPDATE_INTERVAL_MS, math.max(5, next_move_at - now_ms))
                end
                local slept = safe_sleep(sleep_for)
                if slept == false then
                    break
                end
            end
        end
    elseif version <= 0 or target_x == nil or target_y == nil then
        share_set(SHARE_PREFIX, "worker_status", paused and "paused" or "idle")
        last_route_mode = mode
        last_route_signature = route_signature
        last_route_count = -1
        last_route_source = target_source
        if version ~= last_version then
            next_move_at = 0
            last_version = version
        end
        clear_move_feedback(now_ms)
        local slept = safe_sleep(UPDATE_INTERVAL_MS)
        if slept == false then
            break
        end
    else
        last_route_mode = mode
        last_route_signature = route_signature
        last_route_count = -1
        last_route_source = target_source
        if version ~= last_version then
            target_x = as_number(share_get(SHARE_PREFIX, "target_x"))
            target_y = as_number(share_get(SHARE_PREFIX, "target_y"))
            move_interval_floor_ms = math.max(
                30,
                as_number(share_get(SHARE_PREFIX, "move_interval_floor_ms")) or 80
            )
            move_interval_ms = math.max(
                move_interval_floor_ms,
                as_number(share_get(SHARE_PREFIX, "move_interval_ms")) or 900
            )
            last_version = version
            next_move_at = 0
        end

        local nav_ok, nav_err = ensure_nav_ready()
        if not nav_ok then
            share_set(SHARE_PREFIX, "worker_status", "nav_wait")
            share_set(SHARE_PREFIX, "last_error", tostring(nav_err or "nav init failed"))
            local slept = safe_sleep(math.max(UPDATE_INTERVAL_MS, 200))
            if slept == false then
                break
            end
        else
            share_set(SHARE_PREFIX, "worker_status", "running")
            if now_ms >= next_move_at then
                local move_ok, move_err, interrupted = safe_move_call(target_x, target_y)
                if interrupted == true then
                    break
                end
                if move_ok then
                    local issued_path_index = as_number(share_get(SHARE_PREFIX, "target_path_index")) or 0
                    last_issue_at = now_ms
                    share_set(SHARE_PREFIX, "last_route_mode", "target")
                    share_set(SHARE_PREFIX, "last_route_index", 0)
                    share_set(SHARE_PREFIX, "last_route_count", 0)
                    share_set(SHARE_PREFIX, "last_route_direction", 0)
                    share_set(SHARE_PREFIX, "last_route_distance", nil)
                    share_set(SHARE_PREFIX, "last_route_point_x", target_x)
                    share_set(SHARE_PREFIX, "last_route_point_y", target_y)
                    share_set(SHARE_PREFIX, "last_route_original_index", issued_path_index)
                    share_set(SHARE_PREFIX, "last_target_x", target_x)
                    share_set(SHARE_PREFIX, "last_target_y", target_y)
                    share_set(SHARE_PREFIX, "last_target_path_index", issued_path_index)
                    share_set(SHARE_PREFIX, "last_target_version", version)
                    share_set(SHARE_PREFIX, "last_error", nil)
                    share_set(SHARE_PREFIX, "last_issue_at", now_ms)
                    next_move_at = now_ms + move_interval_ms
                else
                    share_set(SHARE_PREFIX, "worker_status", "move_error")
                    share_set(SHARE_PREFIX, "last_error", tostring(move_err or "MoveTo failed"))
                    next_move_at = now_ms + math.min(move_interval_ms, 300)
                end
            end

            local sleep_for = UPDATE_INTERVAL_MS
            if next_move_at > now_ms then
                sleep_for = math.min(UPDATE_INTERVAL_MS, math.max(5, next_move_at - now_ms))
            end
            local slept = safe_sleep(sleep_for)
            if slept == false then
                break
            end
        end
    end
end

share_set(SHARE_PREFIX, "worker_status", "stopped")
share_set(SHARE_PREFIX, "heartbeat_at", type(sys) == "table" and type(sys.time) == "function" and sys.time() or 0)
