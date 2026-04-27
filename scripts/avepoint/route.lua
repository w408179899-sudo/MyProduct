function distance_2d(x1, y1, x2, y2)
    local dx = x1 - x2
    local dy = y1 - y2
    return math.sqrt(dx * dx + dy * dy)
end

function normalize_exact_text(value)
    return trim(value):lower()
end

function lower_text(value)
    return tostring(value or ""):lower()
end

function match_any_text(value, patterns)
    local text = lower_text(value)
    for _, pattern in ipairs(patterns or {}) do
        local needle = lower_text(pattern)
        if needle ~= "" and text:find(needle, 1, true) then
            return true
        end
    end
    return false
end

function pressed_once(vk, modifier_vk)
    local latch_key = vk
    local down = hotkey.is_pressed(vk)
    if modifier_vk ~= nil then
        down = down and hotkey.is_pressed(modifier_vk)
        latch_key = tostring(modifier_vk) .. "+" .. tostring(vk)
    end
    local fired = down and not key_latch[latch_key]
    key_latch[latch_key] = down
    return fired
end

function parse_route_line(line)
    local text = tostring(line or "")
    text = text:gsub("^\239\187\191", "")
    text = trim(text)
    if text == "" or text:sub(1, 1) == "#" then
        return nil
    end

    local x_text, y_text = text:match("^([%-]?%d+%.?%d*)%s*,%s*([%-]?%d+%.?%d*)$")
    if not x_text or not y_text then
        return nil, "Invalid route row: " .. text
    end

    return {
        x = tonumber(x_text),
        y = tonumber(y_text)
    }
end

function load_route_points(relative_path)
    local path = resolve_project_path(relative_path)
    local file, err = io.open(path, "rb")
    if not file then
        return nil, "Unable to open route file: " .. tostring(err or path)
    end

    local points = {}
    local line_no = 0

    for line in file:lines() do
        line_no = line_no + 1
        local point, parse_err = parse_route_line(line)
        if parse_err then
            file:close()
            return nil, string.format("%s (line %d)", parse_err, line_no)
        end
        if point then
            points[#points + 1] = point
        end
    end

    file:close()

    if #points == 0 then
        return nil, "Route file has no points: " .. tostring(path)
    end

    return points
end

function reset_current_map_state()
    current_map_key = nil
    current_map = nil
    state.current_map_key = nil
    state.current_map_label = nil
    state.map_points = nil
end

function active_map_or_err()
    if type(current_map) ~= "table" or type(current_map_key) ~= "string" or current_map_key == "" then
        return nil, nil, "Current map is not selected."
    end

    return current_map, current_map_key
end

function seed_random_once()
    if random_seeded then
        return
    end

    local now = math.floor(tonumber(sys.time()) or 0)
    local seed = (os.time() + now) % 2147483647
    if seed <= 0 then
        seed = 1
    end

    math.randomseed(seed)
    math.random()
    math.random()
    math.random()
    random_seeded = true

    log.info(string.format(
        "Random map seed initialized | seed=%d pool=%d",
        seed,
        #RANDOM_MAP_POOL_KEYS
    ))
end

function pick_random_map_key(excluded_map_key)
    local total = #RANDOM_MAP_POOL_KEYS
    if total <= 0 then
        return nil, nil, total, "Random map pool is empty."
    end

    seed_random_once()

    local excluded = tostring(excluded_map_key or "")
    local candidate_keys = RANDOM_MAP_POOL_KEYS
    if excluded ~= "" and total > 1 then
        candidate_keys = {}
        for _, key in ipairs(RANDOM_MAP_POOL_KEYS) do
            if tostring(key or "") ~= excluded then
                candidate_keys[#candidate_keys + 1] = key
            end
        end
        total = #candidate_keys
    end

    if total <= 0 then
        return nil, nil, total, "Random map pool is empty after exclusion."
    end

    local roll = math.random(1, total)
    local map_key = candidate_keys[roll]
    if not MAP_CONFIGS[map_key] then
        return nil, roll, total, "Unknown random map config: " .. tostring(map_key)
    end

    return map_key, roll, total, nil
end

function activate_random_map(reason, excluded_map_key)
    local map_key, roll, total, pick_err = pick_random_map_key(excluded_map_key)
    if not map_key then
        return false, pick_err
    end

    local map = MAP_CONFIGS[map_key]
    local map_points, err = load_route_points(map.route_file)
    if not map_points then
        return false, err
    end

    current_map_key = map_key
    current_map = map
    state.current_map_key = map_key
    state.current_map_label = tostring(map.label or map_key)
    state.map_points = map_points
    state.button_index = 1
    reset_entry_portal_state()
    state.last_clicked_entry_label = nil
    state.cycle_index = (tonumber(state.cycle_index) or 0) + 1

    log.info(string.format(
        "Random map selected | cycle=%d key=%s label=%s roll=%d/%d file=%s points=%d%s",
        tonumber(state.cycle_index) or 0,
        tostring(map_key),
        tostring(map.label or map_key),
        tonumber(roll) or 0,
        tonumber(total) or 0,
        tostring(resolve_project_path(map.route_file)),
        #map_points,
        reason and reason ~= "" and (" reason=" .. tostring(reason)) or ""
    ))

    return true
end

function normalize_cleanup_run_range()
    local min_runs = math.floor(tonumber(BAG_CLEANUP_RUNS_MIN) or 10)
    local max_runs = math.floor(tonumber(BAG_CLEANUP_RUNS_MAX) or min_runs)

    if min_runs < 1 then
        min_runs = 1
    end
    if max_runs < 1 then
        max_runs = 1
    end
    if max_runs < min_runs then
        min_runs, max_runs = max_runs, min_runs
    end

    return min_runs, max_runs
end

function roll_cleanup_target(reason)
    local min_runs, max_runs = normalize_cleanup_run_range()
    seed_random_once()
    state.cleanup_runs_target = math.random(min_runs, max_runs)

    log.info(string.format(
        "Bag cleanup interval selected | target_runs=%d range=%d..%d%s",
        tonumber(state.cleanup_runs_target) or 0,
        min_runs,
        max_runs,
        reason and reason ~= "" and (" reason=" .. tostring(reason)) or ""
    ))
end

function reset_cleanup_schedule(reason)
    state.cleanup_runs_completed = 0
    state.force_cleanup_after_exit = false
    roll_cleanup_target(reason)
end

function note_run_completed_and_check_cleanup_due()
    if tonumber(state.cleanup_runs_target) == nil or tonumber(state.cleanup_runs_target) <= 0 then
        roll_cleanup_target("missing_target")
    end

    state.cleanup_runs_completed = (tonumber(state.cleanup_runs_completed) or 0) + 1
    local completed = tonumber(state.cleanup_runs_completed) or 0
    local target = tonumber(state.cleanup_runs_target) or 1
    local due = completed >= target
    if state.force_cleanup_after_exit == true then
        due = true
    end

    log.info(string.format(
        "Run cycle completed | since_cleanup=%d target=%d cleanup_due=%s force_cleanup=%s",
        completed,
        target,
        due and "true" or "false",
        state.force_cleanup_after_exit == true and "true" or "false"
    ))

    return due
end

function control_summary(item)
    if type(item) ~= "table" then
        return tostring(item)
    end

    return string.format(
        "kind=%s addr=%s text=%s name=%s fullname=%s x=%s y=%s",
        tostring(item.kind or ""),
        tostring(item.addr or ""),
        tostring(item.text or ""),
        tostring(item.name or ""),
        tostring(item.fullname or ""),
        tostring(item.x or ""),
        tostring(item.y or "")
    )
end

function set_stage(stage, delay_ms)
    state.stage = stage
    state.wait_until = sys.time() + (delay_ms or 0)
end

function reset_button_retry()
    state.button_retry_index = nil
    state.button_retry_started_at = 0
    state.button_retry_last_warn_at = 0
    state.button_retry_dumped = false
end

function reset_entry_portal_state()
    state.entry_portal_started_at = 0
    state.entry_portal_last_warn_at = 0
    state.entry_portal_ready_at = 0
    state.entry_portal_retry_due_at = 0
    state.entry_portal_click_attempts = 0
end

function reset_bag_cleanup_state()
    state.bag_cleanup_index = 1
    state.bag_cleanup_next_stage = nil
    state.bag_cleanup_retry_index = nil
    state.bag_cleanup_retry_started_at = 0
    state.bag_cleanup_retry_last_warn_at = 0
    state.bag_cleanup_last_click_screen_x = nil
    state.bag_cleanup_last_click_screen_y = nil
    state.bag_cleanup_last_click_hwnd = nil
end

function reset_stash_state()
    state.stash_next_stage = nil
    state.stash_retry_started_at = 0
    state.stash_retry_last_warn_at = 0
end

function reset_exit_image_retry()
    state.exit_image_retry_started_at = 0
    state.exit_image_retry_last_warn_at = 0
end

function queue_bag_cleanup(next_stage, delay_ms, reason)
    reset_bag_cleanup_state()
    state.bag_cleanup_next_stage = next_stage
    log.info(string.format(
        "Bag cleanup queued | next_stage=%s%s",
        tostring(next_stage or ""),
        reason and reason ~= "" and (" reason=" .. tostring(reason)) or ""
    ))
    set_stage("bag_cleanup", delay_ms or 0)
end

function reset_route_escape()
    state.route_escape_due_at = 0
    state.route_escape_sent = false
    state.route_escape_hold_until = 0
end

function reset_route_start_retry()
    state.route_start_key = nil
    state.route_start_started_at = 0
    state.route_start_last_warn_at = 0
    state.route_start_ready_at = 0
end

function reset_pickup_state()
    state.pickup_active = false
    state.pickup_next_at = 0
    state.pickup_last_warn_at = 0
    state.pickup_last_info_at = 0
    state.pickup_last_seen_count = 0
    state.pickup_last_logged_count = 0
    state.pickup_stuck_reference_count = 0
    state.pickup_stuck_attempts = 0
    state.pickup_skip_until_exit = false
end

function reset_exit_verify_state()
    state.exit_verify_started_at = 0
    state.exit_verify_last_warn_at = 0
    state.exit_verify_source = nil
end

function reset_revive_state()
    state.revive_started_at = 0
    state.revive_last_warn_at = 0
    state.revive_clicked_at = 0
    state.revive_click_count = 0
    state.revive_resume_ready_at = 0
end

function schedule_human_idle_move(delay_ms)
    local actual_delay = tonumber(delay_ms)
    if actual_delay == nil then
        local min_delay = math.floor(tonumber(HUMAN_IDLE_MOVE_MIN_INTERVAL_MS) or 5000)
        local max_delay = math.floor(tonumber(HUMAN_IDLE_MOVE_MAX_INTERVAL_MS) or min_delay)
        if max_delay < min_delay then
            min_delay, max_delay = max_delay, min_delay
        end
        if max_delay <= min_delay then
            actual_delay = min_delay
        else
            actual_delay = math.random(min_delay, max_delay)
        end
    end

    actual_delay = math.max(0, actual_delay)
    state.human_idle_move_due_at = sys.time() + actual_delay
    return actual_delay
end

function reset_human_idle_move()
    state.human_idle_move_due_at = 0
    if type(human_mouse) == "table" and type(human_mouse.cancel_async_move) == "function" then
        human_mouse.cancel_async_move()
    end
end

function enable_map_pickup(start_delay_ms)
    local delay_ms = math.max(0, tonumber(start_delay_ms) or 0)
    state.pickup_active = true
    state.pickup_next_at = sys.time() + delay_ms
    state.pickup_last_warn_at = 0

    log.info(string.format(
        "Map pickup enabled | key=A scan=%dms press=%dms start_delay=%dms",
        PICKUP_SCAN_INTERVAL_MS,
        PICKUP_PRESS_INTERVAL_MS,
        delay_ms
    ))
end

function disable_map_pickup(reason)
    local was_active = state.pickup_active == true
    reset_pickup_state()

    if was_active and reason and reason ~= "" then
        log.info(reason)
    end
end

function clear_resume_snapshot(reason)
    if type(resume_snapshot) == "table" and reason and reason ~= "" then
        log.info("Resume snapshot cleared | reason=" .. tostring(reason))
    end
    resume_snapshot = nil
end

function capture_resume_snapshot(reason)
    if state.running ~= true then
        return nil
    end

    if tonumber(state.task_mode_id) ~= TASK_MODE.GOLD then
        return nil
    end

    if tostring(reason or "") ~= "AvePoint automation stopped" then
        return nil
    end

    if type(current_map) ~= "table" or type(current_map_key) ~= "string" or current_map_key == "" then
        log.info("Resume snapshot skipped | reason=current map missing")
        return nil
    end

    local cur_x, cur_y, cur_z, pos_err = nav.player_pos()
    if cur_x == nil or cur_y == nil or cur_z == nil then
        log.info("Resume snapshot skipped | reason=player position unavailable err=" .. tostring(pos_err))
        return nil
    end

    local outside, nearest_outside = is_outside_position(cur_x, cur_y, cur_z)
    if outside then
        log.info(string.format(
            "Resume snapshot skipped | reason=player outside distance=%s z_diff=%s",
            nearest_outside and string.format("%.2f", tonumber(nearest_outside.distance) or 0) or "nil",
            nearest_outside and nearest_outside.z_diff and string.format("%.2f", tonumber(nearest_outside.z_diff) or 0) or "nil"
        ))
        return nil
    end

    local route_name = type(state.route) == "table" and tostring(state.route.name or "") or ""
    local stage = tostring(state.stage or "")
    local resume_mode = "map_route"
    if stage == "map_revive" then
        resume_mode = "map_revive"
    elseif stage == "begin_exit_route"
        or stage == "press_exit_d"
        or stage == "verify_exit_result"
        or stage == "exit_interference_escape"
        or stage == "begin_exit_route_for_chumen"
        or stage == "exit_chumen_click"
        or stage == "begin_exit_unstuck_route"
        or route_name == "Exit route"
    then
        resume_mode = "begin_exit_route"
    end

    resume_snapshot = {
        map_key = current_map_key,
        stage = stage,
        route_name = route_name,
        resume_mode = resume_mode,
        cycle_index = tonumber(state.cycle_index) or 0,
        cleanup_runs_completed = tonumber(state.cleanup_runs_completed) or 0,
        cleanup_runs_target = tonumber(state.cleanup_runs_target) or 0,
        force_cleanup_after_exit = state.force_cleanup_after_exit == true,
        last_clicked_entry_label = state.last_clicked_entry_label,
        pos_x = tonumber(cur_x),
        pos_y = tonumber(cur_y),
        pos_z = tonumber(cur_z),
        captured_at = sys.time()
    }

    log.info(string.format(
        "Resume snapshot saved | map=%s stage=%s route=%s mode=%s pos=%.2f, %.2f, %.2f cycle=%d cleanup=%d/%d",
        tostring(resume_snapshot.map_key or ""),
        tostring(resume_snapshot.stage or ""),
        tostring(resume_snapshot.route_name or ""),
        tostring(resume_snapshot.resume_mode or ""),
        tonumber(resume_snapshot.pos_x) or 0,
        tonumber(resume_snapshot.pos_y) or 0,
        tonumber(resume_snapshot.pos_z) or 0,
        tonumber(resume_snapshot.cycle_index) or 0,
        tonumber(resume_snapshot.cleanup_runs_completed) or 0,
        tonumber(resume_snapshot.cleanup_runs_target) or 0
    ))

    return resume_snapshot
end

function try_resume_automation()
    local snapshot = resume_snapshot
    if type(snapshot) ~= "table" then
        return false, "resume snapshot missing"
    end

    local map_key = tostring(snapshot.map_key or "")
    local map = MAP_CONFIGS[map_key]
    if map_key == "" or type(map) ~= "table" then
        clear_resume_snapshot("invalid map key")
        return false, "invalid resume snapshot map"
    end

    local cur_x, cur_y, cur_z, pos_err = nav.player_pos()
    if cur_x == nil or cur_y == nil or cur_z == nil then
        clear_resume_snapshot("player position unavailable")
        return false, pos_err or "player position unavailable"
    end

    local outside, nearest_outside = is_outside_position(cur_x, cur_y, cur_z)
    if outside then
        clear_resume_snapshot(string.format(
            "player outside distance=%s z_diff=%s",
            nearest_outside and string.format("%.2f", tonumber(nearest_outside.distance) or 0) or "nil",
            nearest_outside and nearest_outside.z_diff and string.format("%.2f", tonumber(nearest_outside.z_diff) or 0) or "nil"
        ))
        return false, "player is outside"
    end

    local map_points, load_err = load_route_points(map.route_file)
    if not map_points then
        clear_resume_snapshot("map route load failed")
        return false, load_err
    end

    current_map_key = map_key
    current_map = map
    state.current_map_key = map_key
    state.current_map_label = tostring(map.label or map_key)
    state.map_points = map_points
    state.button_index = 1
    state.cycle_index = math.max(1, tonumber(snapshot.cycle_index) or 1)
    state.cleanup_runs_completed = math.max(0, tonumber(snapshot.cleanup_runs_completed) or 0)
    state.cleanup_runs_target = math.max(0, tonumber(snapshot.cleanup_runs_target) or 0)
    state.force_cleanup_after_exit = snapshot.force_cleanup_after_exit == true
    state.last_clicked_entry_label = snapshot.last_clicked_entry_label

    reset_button_retry()
    reset_bag_cleanup_state()
    reset_stash_state()
    reset_route_escape()
    reset_route_start_retry()
    reset_pickup_state()
    reset_exit_verify_state()
    reset_exit_image_retry()
    reset_revive_state()
    reset_entry_portal_state()
    state.running = true
    schedule_human_idle_move()

    if state.cleanup_runs_target <= 0 then
        roll_cleanup_target("resume_missing_target")
    end

    local resume_mode = tostring(snapshot.resume_mode or "")
    if resume_mode == "map_revive" then
        set_stage("map_revive", 0)
    elseif resume_mode == "begin_exit_route" then
        set_stage("begin_exit_route", 0)
    else
        local start_index, nearest_distance = nearest_route_point_index(map_points, cur_x, cur_y)
        if start_index == nil then
            clear_resume_snapshot("nearest route point missing")
            return false, "map route points are empty"
        end

        local ok, err = start_route(map_points, "Map route", "begin_exit_route", start_index)
        if not ok then
            clear_resume_snapshot("map route resume start failed")
            return false, err
        end

        log.info(string.format(
            "Resume snapshot restored | map=%s mode=map_route start=%d/%d distance=%.2f cycle=%d cleanup=%d/%d",
            tostring(map_key),
            start_index,
            #map_points,
            tonumber(nearest_distance) or 0,
            tonumber(state.cycle_index) or 0,
            tonumber(state.cleanup_runs_completed) or 0,
            tonumber(state.cleanup_runs_target) or 0
        ))
        clear_resume_snapshot()
        return true
    end

    log.info(string.format(
        "Resume snapshot restored | map=%s mode=%s cycle=%d cleanup=%d/%d",
        tostring(map_key),
        resume_mode,
        tonumber(state.cycle_index) or 0,
        tonumber(state.cleanup_runs_completed) or 0,
        tonumber(state.cleanup_runs_target) or 0
    ))
    clear_resume_snapshot()
    return true
end

function TASK_MODE.prepare_start(mode_id, mode_name)
    state.running = false
    state.task_mode_id = tonumber(mode_id) or TASK_MODE.DEFAULT
    state.task_mode_name = tostring(mode_name or TASK_MODE.label(mode_id))
    state.stage = "idle"
    state.wait_until = 0
    state.route = nil
    state.button_index = 1
    state.cycle_index = 0
    state.cleanup_runs_completed = 0
    state.cleanup_runs_target = 0
    state.force_cleanup_after_exit = false
    reset_button_retry()
    reset_bag_cleanup_state()
    reset_stash_state()
    reset_route_escape()
    reset_route_start_retry()
    reset_pickup_state()
    reset_exit_verify_state()
    reset_exit_image_retry()
    reset_human_idle_move()
    reset_revive_state()
    reset_current_map_state()
    state.last_clicked_entry_label = nil
    reset_entry_portal_state()
end

function stop_automation(reason)
    capture_resume_snapshot(reason)

    if type(TASK_MODE.runner) == "table" and type(TASK_MODE.runner.stop) == "function" then
        pcall(TASK_MODE.runner.stop, TASK_MODE.build_context())
    end
    TASK_MODE.runner = nil

    state.running = false
    state.task_mode_id = nil
    state.task_mode_name = nil
    state.stage = "idle"
    state.wait_until = 0
    state.route = nil
    state.cycle_index = 0
    state.cleanup_runs_completed = 0
    state.cleanup_runs_target = 0
    state.force_cleanup_after_exit = false
    state.button_index = 1
    reset_button_retry()
    reset_bag_cleanup_state()
    reset_stash_state()
    reset_route_escape()
    reset_route_start_retry()
    reset_pickup_state()
    reset_exit_verify_state()
    reset_exit_image_retry()
    reset_human_idle_move()
    reset_revive_state()
    reset_current_map_state()
    state.last_clicked_entry_label = nil
    reset_entry_portal_state()

    if reason and reason ~= "" then
        log.info(reason)
    end
end

function reset_f6_loop_state()
    state.f6_loop_active = false
    state.f6_loop_round = 0
    state.f6_loop_total_rounds = F6_LOOP_TOTAL_ROUNDS
    state.f6_loop_phase = nil
    state.f6_loop_started_at = 0
    state.f6_loop_deadline_at = 0
    state.f6_loop_exit_pending = false
    state.f6_loop_wait_until = 0
    state.f6_loop_cycle_pid = 0
end

function stop_f6_loop(reason)
    local was_active = state.f6_loop_active == true
    reset_f6_loop_state()
    if was_active and reason and reason ~= "" then
        log.info(reason)
    end
end

function capture_current_game_pid()
    local pid = tonumber(type(nav) == "table" and nav.pid or 0) or 0
    if pid <= 0 then
        local found_pid = avepoint_find_torchlight_process_pid()
        pid = tonumber(found_pid) or 0
    end
    if pid <= 0 then
        pid = tonumber(state.f5_launch_pid) or 0
    end
    return pid
end

function start_f6_loop_cycle(cycle)
    state.f6_loop_round = math.max(1, tonumber(cycle) or 1)
    state.f6_loop_phase = "launch_and_enter"
    state.f6_loop_started_at = 0
    state.f6_loop_deadline_at = 0
    state.f6_loop_exit_pending = false
    state.f6_loop_wait_until = 0
    state.f6_loop_cycle_pid = 0

    log.info(string.format(
        "F6 3-round loop cycle %d/%d | phase=launch_and_enter",
        tonumber(state.f6_loop_round) or 0,
        tonumber(state.f6_loop_total_rounds) or F6_LOOP_TOTAL_ROUNDS
    ))
end

function start_f6_loop()
    if state.f6_loop_active == true then
        return false, "F6 3-round loop already running."
    end

    state.f6_loop_active = true
    state.f6_loop_total_rounds = F6_LOOP_TOTAL_ROUNDS
    start_f6_loop_cycle(1)
    return true
end

function fail_f6_loop(err)
    local round = tonumber(state.f6_loop_round) or 0
    local total = tonumber(state.f6_loop_total_rounds) or F6_LOOP_TOTAL_ROUNDS
    local phase = tostring(state.f6_loop_phase or "unknown")

    if state.running then
        stop_automation("AvePoint automation stopped by F6 loop error")
    end

    reset_f6_loop_state()
    return false, string.format(
        "F6 3-round loop cycle %d/%d failed | phase=%s err=%s",
        round,
        total,
        phase,
        tostring(err)
    )
end

function mark_f6_first_map_entry(stage_name)
    if state.f6_loop_active ~= true or tostring(state.f6_loop_phase or "") ~= "wait_first_entry" then
        return
    end
    if (tonumber(state.f6_loop_started_at) or 0) > 0 then
        return
    end

    local now = sys.time()
    state.f6_loop_started_at = now
    state.f6_loop_deadline_at = now + F6_LOOP_MAP_DURATION_MS
    state.f6_loop_phase = "run_until_safe_exit"
    state.f6_loop_cycle_pid = capture_current_game_pid()

    log.info(string.format(
        "F6 3-round loop cycle %d/%d timer started | window=%dms stage=%s map=%s map_cycle=%d",
        tonumber(state.f6_loop_round) or 0,
        tonumber(state.f6_loop_total_rounds) or F6_LOOP_TOTAL_ROUNDS,
        F6_LOOP_MAP_DURATION_MS,
        tostring(stage_name or ""),
        tostring(state.current_map_label or state.current_map_key or ""),
        tonumber(state.cycle_index) or 0
    ))
end

function maybe_begin_f6_safe_exit(reason)
    if state.f6_loop_active ~= true or state.f6_loop_exit_pending ~= true then
        return false
    end

    local round = tonumber(state.f6_loop_round) or 0
    local total = tonumber(state.f6_loop_total_rounds) or F6_LOOP_TOTAL_ROUNDS
    state.f6_loop_phase = "exit_game"
    state.f6_loop_exit_pending = false
    state.f6_loop_started_at = 0
    state.f6_loop_deadline_at = 0
    state.f6_loop_wait_until = 0
    state.f6_loop_cycle_pid = capture_current_game_pid()

    log.info(string.format(
        "F6 3-round loop cycle %d/%d | phase=exit_game reason=%s",
        round,
        total,
        tostring(reason or "safe_exit")
    ))
    stop_automation(string.format(
        "AvePoint automation stopped for F6 cycle %d safe exit",
        round
    ))
    return true
end

function move_to_point(point)
    if type(point) ~= "table" then
        return false, "Route point does not exist."
    end

    return nav.move_call(point.x, point.y)
end

function build_exit_unstuck_route()
    local cur_x, cur_y, _, err = nav.player_pos()
    if cur_x == nil or cur_y == nil then
        return nil, err or "Player position unavailable."
    end

    local radius = math.max(80, tonumber(EXIT_UNSTUCK_MOVE_DISTANCE) or 260)
    local diagonal = radius * 0.72
    return {
        { x = cur_x + radius, y = cur_y },
        { x = cur_x - radius, y = cur_y },
        { x = cur_x, y = cur_y + radius },
        { x = cur_x, y = cur_y - radius },
        { x = cur_x + diagonal, y = cur_y + diagonal },
        { x = cur_x - diagonal, y = cur_y + diagonal },
        { x = cur_x + diagonal, y = cur_y - diagonal },
        { x = cur_x - diagonal, y = cur_y - diagonal }
    }
end

function nearest_route_point_index(points, cur_x, cur_y)
    if type(points) ~= "table" or #points == 0 then
        return nil, nil
    end

    if cur_x == nil or cur_y == nil then
        return 1, nil
    end

    local best_index = 1
    local best_distance = nil

    for index, point in ipairs(points) do
        local distance = distance_2d(cur_x, cur_y, point.x, point.y)
        if best_distance == nil or distance < best_distance then
            best_index = index
            best_distance = distance
        end
    end

    return best_index, best_distance
end

function start_route(points, route_name, next_stage, start_index)
    if type(points) ~= "table" or #points == 0 then
        return false, "Route points are empty."
    end

    start_index = tonumber(start_index) or 1
    if start_index < 1 then
        start_index = 1
    elseif start_index > #points then
        start_index = #points
    end

    local first = points[start_index]
    local ok, err = move_to_point(first)
    if not ok then
        return false, err
    end

    state.route = {
        points = points,
        name = route_name,
        index = start_index,
        next_stage = next_stage,
        next_repath_at = sys.time() + REPATH_INTERVAL_MS,
        special_hold_until = 0,
        interacted_points = {},
        point_track_index = start_index,
        point_started_at = sys.time(),
        point_best_distance = math.huge,
        point_break_attempted = false,
        point_break_retry_at = 0
    }
    state.stage = "route"
    state.wait_until = 0

    if route_name == "Map route" then
        enable_map_pickup(MAP_ROUTE_READY_STABLE_MS)
    end

    if route_name == "Map route"
        and type(current_map) == "table"
        and current_map.map_route_escape_enabled == true
    then
        state.route_escape_due_at = sys.time() + (tonumber(current_map.map_route_escape_delay_ms) or MAP_ROUTE_ESCAPE_DELAY_MS)
        state.route_escape_sent = false
        state.route_escape_hold_until = 0
    else
        reset_route_escape()
    end

    log.info(string.format(
        "%s started %d/%d -> %.2f, %.2f",
        route_name,
        start_index,
        #points,
        first.x,
        first.y
    ))

    return true
end

function reset_route_point_tracking(route, now, index, distance)
    if type(route) ~= "table" then
        return
    end

    route.point_track_index = tonumber(index) or route.index
    route.point_started_at = tonumber(now) or sys.time()
    route.point_best_distance = tonumber(distance) or math.huge
    route.point_break_attempted = false
    route.point_break_retry_at = 0
end

function start_outer_route()
    local cur_x, cur_y = nav.player_pos()
    local last_point = ROUTE_POINTS[#ROUTE_POINTS]

    if cur_x ~= nil and cur_y ~= nil and type(last_point) == "table" then
        local last_distance = distance_2d(cur_x, cur_y, last_point.x, last_point.y)
        if last_distance <= OUTER_FINAL_SKIP_DISTANCE then
            log.info(string.format(
                "Outer route skipped | already near final point distance=%.2f threshold=%.2f -> %.2f, %.2f",
                last_distance,
                OUTER_FINAL_SKIP_DISTANCE,
                last_point.x,
                last_point.y
            ))
            set_stage("press_entry_d", 0)
            return true
        end
    end

    local start_index, nearest_distance = nearest_route_point_index(ROUTE_POINTS, cur_x, cur_y)
    if start_index == nil then
        return false, "Outer route points are empty."
    end

    local point = ROUTE_POINTS[start_index]
    if nearest_distance ~= nil and type(point) == "table" then
        log.info(string.format(
            "Outer route nearest point %d/%d distance=%.2f -> %.2f, %.2f",
            start_index,
            #ROUTE_POINTS,
            nearest_distance,
            point.x,
            point.y
        ))
    end

    return start_route(ROUTE_POINTS, "Outer route", "press_entry_d", start_index)
end

function random_between(min_value, max_value)
    seed_random_once()
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

AVEPOINT_DELAY_PROFILES = {
    entry_flow = {
        min_ms = ENTRY_FLOW_DELAY_MIN_MS,
        max_ms = ENTRY_FLOW_DELAY_MAX_MS,
        center_ms = 780,
        sigma_ms = 210,
        gaussian_weight = 0.9
    },
    bag_flow = {
        min_ms = BAG_FLOW_DELAY_MIN_MS,
        max_ms = BAG_FLOW_DELAY_MAX_MS,
        center_ms = 520,
        sigma_ms = 120,
        gaussian_weight = 0.88
    },
    startup_recover = {
        min_ms = STARTUP_RECOVER_T_WAIT_MIN_MS,
        max_ms = STARTUP_RECOVER_T_WAIT_MAX_MS,
        center_ms = math.floor((STARTUP_RECOVER_T_WAIT_MIN_MS + STARTUP_RECOVER_T_WAIT_MAX_MS) * 0.5 + 0.5),
        sigma_ms = 140,
        gaussian_weight = 0.9
    },
    long_action = {
        mode = "scaled",
        min_factor = 0.70,
        max_factor = 1.35,
        sigma_factor = 0.12,
        gaussian_weight = 0.9,
        min_floor = 250
    },
    key_stage = {
        mode = "scaled",
        min_factor = 0.60,
        max_factor = 1.55,
        sigma_factor = 0.18,
        gaussian_weight = 0.9,
        min_floor = 180
    },
    click = {
        mode = "scaled",
        min_factor = 0.50,
        max_factor = 2.20,
        sigma_factor = 0.28,
        gaussian_weight = 0.9,
        min_floor = 12
    },
    hover = {
        mode = "scaled",
        min_factor = 0.50,
        max_factor = 2.25,
        sigma_factor = 0.30,
        gaussian_weight = 0.9,
        min_floor = 18
    },
    verify = {
        mode = "scaled",
        min_factor = 0.40,
        max_factor = 1.80,
        sigma_factor = 0.22,
        gaussian_weight = 0.88,
        min_floor = 80
    },
    ui_gap = {
        mode = "scaled",
        min_factor = 0.45,
        max_factor = 1.90,
        sigma_factor = 0.24,
        gaussian_weight = 0.88,
        min_floor = 120
    },
    ui_gap_range = {
        min_ms = 250,
        max_ms = 500,
        center_ms = 360,
        sigma_ms = 55,
        gaussian_weight = 0.88
    },
    prepare_focus = {
        mode = "scaled",
        min_factor = 0.60,
        max_factor = 1.80,
        sigma_factor = 0.25,
        gaussian_weight = 0.88,
        min_floor = 30
    }
}

function avepoint_randn()
    seed_random_once()

    local u1 = 0
    repeat
        u1 = math.random()
    until u1 > 0.000001

    local u2 = math.random()
    return math.sqrt(-2 * math.log(u1)) * math.cos(2 * math.pi * u2)
end

function avepoint_gaussian_delay_ms(profile)
    if type(profile) ~= "table" then
        return 0
    end

    seed_random_once()

    local min_ms = math.max(0, math.floor(tonumber(profile.min_ms) or 0))
    local max_ms = math.max(min_ms, math.floor(tonumber(profile.max_ms) or min_ms))
    local center_ms = tonumber(profile.center_ms)
    local sigma_ms = tonumber(profile.sigma_ms)
    local gaussian_weight = tonumber(profile.gaussian_weight) or 0.88

    if center_ms == nil then
        center_ms = (min_ms + max_ms) * 0.5
    end
    if sigma_ms == nil or sigma_ms <= 0 then
        sigma_ms = math.max(1, (max_ms - min_ms) / 6)
    end

    if center_ms < min_ms then
        center_ms = min_ms
    elseif center_ms > max_ms then
        center_ms = max_ms
    end

    if max_ms <= min_ms then
        return min_ms
    end

    if gaussian_weight < 1 and math.random() > gaussian_weight then
        return random_between(min_ms, max_ms)
    end

    local sample = center_ms + avepoint_randn() * sigma_ms
    if sample < min_ms then
        sample = min_ms
    elseif sample > max_ms then
        sample = max_ms
    end

    return math.floor(sample + 0.5)
end

function avepoint_delay_ms(profile_name, base_ms)
    local profile = type(AVEPOINT_DELAY_PROFILES) == "table" and AVEPOINT_DELAY_PROFILES[profile_name] or nil
    if type(profile) ~= "table" then
        return math.max(0, math.floor(tonumber(base_ms) or 0))
    end

    if profile.mode == "scaled" then
        local base = math.max(0, tonumber(base_ms) or 0)
        if base <= 0 then
            return 0
        end

        local min_ms = math.floor(base * (tonumber(profile.min_factor) or 1) + 0.5)
        local max_ms = math.floor(base * (tonumber(profile.max_factor) or 1) + 0.5)
        local min_floor = tonumber(profile.min_floor)
        local max_cap = tonumber(profile.max_cap)

        if min_floor ~= nil and min_ms < min_floor then
            min_ms = math.floor(min_floor + 0.5)
        end
        if max_cap ~= nil and max_ms > max_cap then
            max_ms = math.floor(max_cap + 0.5)
        end
        if max_ms < min_ms then
            max_ms = min_ms
        end

        return avepoint_gaussian_delay_ms({
            min_ms = min_ms,
            max_ms = max_ms,
            center_ms = tonumber(profile.center_factor) and (base * tonumber(profile.center_factor)) or base,
            sigma_ms = math.max(1, math.floor(base * (tonumber(profile.sigma_factor) or 0.16) + 0.5)),
            gaussian_weight = profile.gaussian_weight
        })
    end

    return avepoint_gaussian_delay_ms(profile)
end

function random_entry_flow_delay_ms()
    return avepoint_delay_ms("entry_flow")
end

function random_bag_flow_delay_ms()
    return avepoint_delay_ms("bag_flow")
end

function nearest_outside_reference(cur_x, cur_y, cur_z)
    if cur_x == nil or cur_y == nil then
        return nil
    end

    local best = nil
    for _, point in ipairs(OUTSIDE_REFERENCE_POINTS) do
        local distance = distance_2d(cur_x, cur_y, point.x, point.y)
        local z_diff = nil
        if cur_z ~= nil and point.z ~= nil then
            z_diff = math.abs(cur_z - point.z)
        end

        if best == nil
            or distance < best.distance
            or (distance == best.distance and z_diff ~= nil and (best.z_diff == nil or z_diff < best.z_diff))
        then
            best = {
                x = point.x,
                y = point.y,
                z = point.z,
                distance = distance,
                z_diff = z_diff
            }
        end
    end

    return best
end

function is_outside_position(cur_x, cur_y, cur_z)
    local nearest = nearest_outside_reference(cur_x, cur_y, cur_z)
    if type(nearest) ~= "table" then
        return false, nil
    end

    local outside = nearest.distance <= OUTSIDE_REFERENCE_MAX_DISTANCE
        and nearest.z_diff ~= nil
        and nearest.z_diff <= OUTSIDE_REFERENCE_MAX_Z_DIFF

    return outside, nearest
end

function should_run_startup_inside_recovery()
    local cur_x, cur_y, cur_z, err = nav.player_pos()
    if cur_x == nil or cur_y == nil or cur_z == nil then
        log.info("Startup area check skipped | reason=" .. tostring(err or "player position unavailable"))
        return false
    end

    local nearest = nearest_outside_reference(cur_x, cur_y, cur_z)
    if type(nearest) ~= "table" then
        log.info("Startup area check skipped | reason=no outside reference points")
        return false
    end

    local outside = nearest.distance <= OUTSIDE_REFERENCE_MAX_DISTANCE
        and nearest.z_diff ~= nil
        and nearest.z_diff <= OUTSIDE_REFERENCE_MAX_Z_DIFF

    log.info(string.format(
        "Startup area check | pos=%.2f, %.2f, %.2f nearest_outside=%.2f, %.2f, %.2f distance=%.2f z_diff=%s outside=%s",
        cur_x,
        cur_y,
        cur_z,
        nearest.x,
        nearest.y,
        tonumber(nearest.z) or 0,
        nearest.distance,
        nearest.z_diff and string.format("%.2f", nearest.z_diff) or "nil",
        outside and "true" or "false"
    ))

    return not outside
end


function current_map_route_interaction(route, point)
    if route.name ~= "Map route" or type(point) ~= "table" then
        return nil
    end

    local interactions = current_map and current_map.map_route_interact_points
    if type(interactions) ~= "table" then
        return nil
    end

    for _, interaction in ipairs(interactions) do
        if type(interaction) == "table"
            and tonumber(interaction.index) == tonumber(route.index)
            and route.interacted_points[tostring(route.index)] ~= true
        then
            return interaction
        end
    end

    return nil
end

function trigger_map_route_interaction(route, interaction, now)
    local ok, err = press_key(tonumber(interaction.key_vk) or VK_D, interaction.label or "map route interaction")
    if not ok then
        return false, err
    end

    route.interacted_points[tostring(route.index)] = true
    route.index = route.index + 1
    local hold_delay_ms = avepoint_delay_ms(
        "key_stage",
        math.max(0, tonumber(interaction.after_key_delay_ms) or KEY_STAGE_DELAY_MS)
    )
    route.special_hold_until = now + hold_delay_ms
    route.next_repath_at = route.special_hold_until

    log.info(string.format(
        "Map route interaction triggered %d/%d | action=%s hold=%dms",
        route.index - 1,
        #route.points,
        tostring(interaction.label or ""),
        hold_delay_ms
    ))

    return true
end

function update_route(now)
    local route = state.route
    if type(route) ~= "table" then
        return false, "Route state is missing."
    end

    local point = route.points[route.index]
    if not point then
        local next_stage = route.next_stage
        local route_name = route.name
        state.route = nil
        reset_route_escape()
        if route_name == "Exit unstuck route" then
            reset_exit_verify_state()
        end
        if route_name == "Stash return route" then
            reset_stash_state()
        end
        log.info(route_name .. " completed")
        set_stage(next_stage, 0)
        return true
    end

    if route.name == "Map route" and now < (state.route_escape_hold_until or 0) then
        return true
    end

    if now < (route.special_hold_until or 0) then
        return true
    end

    local cur_x, cur_y = nav.player_pos()
    if cur_x ~= nil and cur_y ~= nil then
        if route.point_track_index ~= route.index then
            reset_route_point_tracking(route, now, route.index, nil)
        end

        local interaction = current_map_route_interaction(route, point)
        local distance = distance_2d(cur_x, cur_y, point.x, point.y)
        local arrive_tolerance = ARRIVE_TOLERANCE
        if interaction then
            arrive_tolerance = math.max(arrive_tolerance, tonumber(interaction.arrive_tolerance) or 0)
        end

        if route.name == "Map route" then
            local best_distance = tonumber(route.point_best_distance) or math.huge
            if distance < best_distance then
                route.point_best_distance = distance
                if best_distance == math.huge
                    or distance <= best_distance - MAP_ROUTE_STUCK_PROGRESS_RESET_DISTANCE
                then
                    route.point_started_at = now
                end
            end
        end

        if distance <= arrive_tolerance then
            if interaction then
                return trigger_map_route_interaction(route, interaction, now)
            end

            route.index = route.index + 1
            local next_point = route.points[route.index]
            if next_point then
                reset_route_point_tracking(route, now, route.index, nil)
                local ok, err = move_to_point(next_point)
                if not ok then
                    if route.name == "Map route" then
                        route.next_repath_at = now + STEP_RETRY_POLL_MS
                        log.warn("Map route progress MoveTo retry: " .. tostring(err))
                        return true
                    end
                    return false, err
                end
                route.next_repath_at = now + REPATH_INTERVAL_MS
                log.info(string.format(
                    "%s progress %d/%d -> %.2f, %.2f",
                    route.name,
                    route.index,
                    #route.points,
                    next_point.x,
                    next_point.y
                ))
            else
                local next_stage = route.next_stage
                local route_name = route.name
                state.route = nil
                reset_route_escape()
                if route_name == "Exit unstuck route" then
                    reset_exit_verify_state()
                end
                if route_name == "Stash return route" then
                    reset_stash_state()
                end
                log.info(route_name .. " completed")
                set_stage(next_stage, 0)
            end
            return true
        end

        if route.name == "Map route"
            and now - (route.point_started_at or now) >= MAP_ROUTE_STUCK_SKIP_MS
        then
            if route.point_break_attempted ~= true then
                local break_ok, break_err = click_current_mouse_button(
                    "right",
                    string.format("map route stuck break obstacle index=%d/%d", route.index, #route.points),
                    50
                )
                route.point_break_attempted = true
                route.point_break_retry_at = now + MAP_ROUTE_STUCK_RIGHT_CLICK_RETRY_MS

                if break_ok then
                    route.next_repath_at = now + STEP_RETRY_POLL_MS
                    log.warn(string.format(
                        "Map route appears stuck | index=%d/%d distance=%.2f best=%.2f elapsed=%dms action=right_click_break_obstacle retry_in=%dms",
                        route.index,
                        #route.points,
                        distance,
                        tonumber(route.point_best_distance) or distance,
                        now - (route.point_started_at or now),
                        MAP_ROUTE_STUCK_RIGHT_CLICK_RETRY_MS
                    ))
                    return true
                end

                log.warn("Map route stuck right-click failed: " .. tostring(break_err))
            end

            if now < (route.point_break_retry_at or 0) then
                return true
            end

            local stuck_index = route.index
            local next_index = route.index + 1
            local next_point = route.points[next_index]
            if next_point then
                local stuck_best_distance = tonumber(route.point_best_distance) or distance
                local stuck_elapsed = now - (route.point_started_at or now)
                route.index = next_index
                reset_route_point_tracking(route, now, route.index, nil)
                local ok, err = move_to_point(next_point)
                if not ok then
                    route.index = stuck_index
                    reset_route_point_tracking(route, now, route.index, distance)
                    route.next_repath_at = now + STEP_RETRY_POLL_MS
                    log.warn("Map route stuck skip MoveTo retry: " .. tostring(err))
                    return true
                end

                route.next_repath_at = now + REPATH_INTERVAL_MS
                log.warn(string.format(
                    "Map route appears stuck | index=%d/%d distance=%.2f best=%.2f elapsed=%dms action=skip_to_%d",
                    stuck_index,
                    #route.points,
                    distance,
                    stuck_best_distance,
                    stuck_elapsed,
                    next_index
                ))
                log.info(string.format(
                    "%s progress %d/%d -> %.2f, %.2f",
                    route.name,
                    route.index,
                    #route.points,
                    next_point.x,
                    next_point.y
                ))
                return true
            end
        end
    end

    if now >= route.next_repath_at then
        local ok, err = move_to_point(point)
        if not ok then
            if route.name == "Map route" then
                route.next_repath_at = now + STEP_RETRY_POLL_MS
                log.warn("Map route repath MoveTo retry: " .. tostring(err))
                return true
            end
            return false, err
        end
        route.next_repath_at = now + REPATH_INTERVAL_MS
    end

    return true
end

