function avepoint_wait_click_image_step(step, timeout_ms, success_probe_step, fetch_opts)
    local started_at = sys.time()
    local last_warn_at = 0
    local last_err = nil

    while true do
        local target, err = fetch_button_for_step(step, fetch_opts)
        if target then
            local ok, click_err, retryable = click_fetched_target(step, target, {
                success_probe_step = success_probe_step
            })
            if ok then
                return true
            end

            last_err = click_err
            if not retryable then
                return false, click_err
            end
        else
            last_err = err
        end

        local now = sys.time()
        local elapsed = now - started_at
        if elapsed >= math.max(1000, tonumber(timeout_ms) or STEP_FETCH_TIMEOUT_MS) then
            return false, string.format(
                "Wait/click timeout [%s]: %s",
                tostring(step and step.label or ""),
                tostring(last_err)
            )
        end

        if now - last_warn_at >= STEP_WARN_INTERVAL_MS then
            last_warn_at = now
            log.warn(string.format(
                "Waiting hotkey step %s | elapsed=%dms err=%s",
                tostring(step and step.label or ""),
                elapsed,
                tostring(last_err)
            ))
        end

        sys.sleep(STEP_RETRY_POLL_MS)
    end
end

function avepoint_wait_for_torch_init(timeout_ms, target, mode)
    local started_at = sys.time()
    local last_warn_at = 0
    local last_err = nil
    local init_target = target or PROCESS_NAME
    local init_mode = mode or MODE

    while true do
        if initialized
            and type(nav) == "table"
            and type(nav.is_initialized) == "function"
            and nav.is_initialized()
        then
            local current_pid = tonumber(nav.pid) or 0
            local target_pid = tonumber(init_target) or 0
            if target_pid <= 0 or current_pid == target_pid then
                return true
            end
            initialized = false
        end

        local ok, err = nav.init(init_target, init_mode)
        if ok then
            local attached_pid = tonumber(type(nav) == "table" and nav.pid or 0) or 0
            local target_pid = tonumber(init_target) or 0
            if target_pid > 0 and attached_pid > 0 and attached_pid ~= target_pid then
                last_err = string.format(
                    "Attached unexpected pid=%d target=%d",
                    attached_pid,
                    target_pid
                )
            else
                initialized = true
                last_init_error = nil
                next_init_retry_at = sys.time() + INIT_RETRY_MS
                log.info(string.format(
                    "Torch API initialized by hotkey flow | pid=%s target=%s",
                    attached_pid > 0 and tostring(attached_pid) or "unknown",
                    tostring(init_target)
                ))
                return true
            end
        else
            last_err = err
        end

        local now = sys.time()
        local elapsed = now - started_at
        if elapsed >= math.max(1000, tonumber(timeout_ms) or 60000) then
            return false, string.format("Torch init timeout after %dms: %s", elapsed, tostring(last_err))
        end

        if now - last_warn_at >= STEP_WARN_INTERVAL_MS then
            last_warn_at = now
            log.warn(string.format(
                "Waiting torch init for hotkey flow | elapsed=%dms target=%s mode=%s err=%s",
                elapsed,
                tostring(init_target),
                tostring(init_mode),
                tostring(last_err)
            ))
        end

        sys.sleep(STEP_RETRY_POLL_MS)
    end
end

function avepoint_clear_nav_binding(reload_api)
    initialized = false
    last_init_error = nil
    next_init_retry_at = 0
    if type(nav) == "table" and type(nav.reset) == "function" then
        nav.reset({
            reload_api = reload_api == true
        })
    end
end

function avepoint_hotkey_attach_target_pid()
    local pid = tonumber(state.f5_launch_pid) or 0
    if pid > 0 then
        return pid
    end

    local current_pid = capture_current_game_pid()
    if current_pid > 0 then
        return current_pid
    end

    return PROCESS_NAME
end

function avepoint_find_torchlight_process_pid()
    if type(proc) ~= "table" or type(proc.list) ~= "function" then
        return nil, "proc.list is not available."
    end

    local list = proc.list()
    if type(list) ~= "table" then
        return nil, "proc.list returned invalid data."
    end

    local wanted = tostring(PROCESS_NAME or ""):lower()
    local matched_pid = nil
    for _, item in ipairs(list) do
        if tostring(item and item.name or ""):lower() == wanted then
            local pid = tonumber(item.pid)
            if pid and pid > 0 and (matched_pid == nil or pid > matched_pid) then
                matched_pid = pid
            end
        end
    end

    if matched_pid ~= nil then
        return matched_pid
    end

    return nil, "Target process not found."
end

function avepoint_find_window_by_pid(pid)
    local hwnd = nil

    if type(proc) == "table" and type(proc.window) == "function" then
        hwnd = proc.window(pid)
    end

    if (hwnd == nil or hwnd == 0)
        and type(wnd) == "table"
        and type(wnd.find_by_pid) == "function"
    then
        hwnd = wnd.find_by_pid(pid)
    end

    if hwnd ~= nil and hwnd ~= 0 then
        return hwnd
    end

    return nil
end

function avepoint_wait_for_launch_process_window_ready(timeout_ms)
    local started_at = sys.time()
    local last_warn_at = 0
    local timeout_limit = tonumber(timeout_ms) or 90000
    local process_ready_stable_ms = 1500
    local window_ready_stable_ms = 1500
    local window_settle_ms = 500
    local stable_pid = nil
    local process_stable_since = 0
    local stable_hwnd = nil
    local window_stable_since = 0
    local last_err = "Target process not found."

    while true do
        local now = sys.time()
        local pid, pid_err = avepoint_find_torchlight_process_pid()
        if pid then
            local alive = true
            if type(proc) == "table" and type(proc.is_alive) == "function" then
                alive = proc.is_alive(pid)
            end

            if alive then
                if stable_pid ~= pid then
                    stable_pid = pid
                    process_stable_since = now
                    stable_hwnd = nil
                    window_stable_since = 0
                end

                local process_stable_ms = now - (process_stable_since or now)
                if process_stable_ms >= process_ready_stable_ms then
                    local hwnd = avepoint_find_window_by_pid(pid)
                    if hwnd then
                        local window_ready = true
                        if type(wnd) == "table" then
                            if type(wnd.is_visible) == "function" and not wnd.is_visible(hwnd) then
                                window_ready = false
                                last_err = string.format("Game window is not visible | pid=%d hwnd=%s", pid, tostring(hwnd))
                            elseif type(wnd.is_enabled) == "function" and not wnd.is_enabled(hwnd) then
                                window_ready = false
                                last_err = string.format("Game window is not enabled | pid=%d hwnd=%s", pid, tostring(hwnd))
                            elseif type(wnd.is_minimized) == "function" and wnd.is_minimized(hwnd) then
                                window_ready = false
                                last_err = string.format("Game window is minimized | pid=%d hwnd=%s", pid, tostring(hwnd))
                            end
                        end

                        if window_ready then
                            if stable_hwnd ~= hwnd then
                                stable_hwnd = hwnd
                                window_stable_since = now
                            end

                            local window_stable_ms = now - (window_stable_since or now)
                            if window_stable_ms >= window_ready_stable_ms then
                                if type(wnd) == "table" and type(wnd.set_foreground) == "function" then
                                    wnd.set_foreground(hwnd)
                                end
                                if window_settle_ms > 0 then
                                    sys.sleep(window_settle_ms)
                                end
                                log.info(string.format(
                                    "Game launch process/window ready | pid=%d hwnd=%s process_stable=%dms window_stable=%dms",
                                    pid,
                                    tostring(hwnd),
                                    process_stable_ms,
                                    window_stable_ms
                                ))
                                return true, pid, hwnd
                            end

                            last_err = string.format(
                                "Game window detected | pid=%d hwnd=%s stable=%d/%dms",
                                pid,
                                tostring(hwnd),
                                window_stable_ms,
                                window_ready_stable_ms
                            )
                        else
                            stable_hwnd = nil
                            window_stable_since = 0
                        end
                    else
                        stable_hwnd = nil
                        window_stable_since = 0
                        last_err = string.format("Game window not found for pid=%d", pid)
                    end
                else
                    stable_hwnd = nil
                    window_stable_since = 0
                    last_err = string.format(
                        "Game process detected | pid=%d stable=%d/%dms",
                        pid,
                        process_stable_ms,
                        process_ready_stable_ms
                    )
                end
            else
                stable_pid = nil
                process_stable_since = 0
                stable_hwnd = nil
                window_stable_since = 0
                last_err = string.format("Game process is not alive | pid=%d", pid)
            end
        else
            stable_pid = nil
            process_stable_since = 0
            stable_hwnd = nil
            window_stable_since = 0
            last_err = pid_err or "Target process not found."
        end

        local elapsed = now - started_at
        if elapsed >= math.max(1000, timeout_limit) then
            return false, string.format(
                "Launch process/window not ready after %dms: %s",
                elapsed,
                tostring(last_err)
            )
        end

        if now - last_warn_at >= STEP_WARN_INTERVAL_MS then
            last_warn_at = now
            log.warn(string.format(
                "Waiting launch process/window ready | elapsed=%dms err=%s",
                elapsed,
                tostring(last_err)
            ))
        end

        sys.sleep(STEP_RETRY_POLL_MS)
    end
end

function avepoint_wait_post_launch_settle(settle_ms)
    local total_ms = math.max(0, tonumber(settle_ms) or 5000)
    if total_ms <= 0 then
        return true
    end

    log.info(string.format(
        "Waiting post-launch settle before torch init | delay=%dms",
        total_ms
    ))

    local started_at = sys.time()
    while true do
        local now = sys.time()
        local elapsed = now - started_at
        if elapsed >= total_ms then
            break
        end

        sys.sleep(math.max(1, math.min(STEP_RETRY_POLL_MS, total_ms - elapsed)))
    end

    log.info(string.format(
        "Post-launch settle finished | delay=%dms",
        total_ms
    ))
    return true
end

function avepoint_wait_for_game_process_exit(target_pid, timeout_ms)
    local watched_pid = tonumber(target_pid) or 0
    local started_at = sys.time()
    local last_warn_at = 0
    local exited_since = 0
    local stable_required_ms = 1500
    local timeout_limit = tonumber(timeout_ms) or 90000
    local last_err = "Target process is still running."

    while true do
        local now = sys.time()
        local current_pid = nil
        local pid_err = nil
        local alive = false

        current_pid, pid_err = avepoint_find_torchlight_process_pid()
        if current_pid ~= nil then
            alive = true
        elseif watched_pid > 0
            and type(proc) == "table"
            and type(proc.is_alive) == "function"
            and proc.is_alive(watched_pid)
        then
            alive = true
            current_pid = watched_pid
        end

        if not alive then
            if exited_since == 0 then
                exited_since = now
            end

            local stable_elapsed = now - exited_since
            if stable_elapsed >= stable_required_ms then
                log.info(string.format(
                    "Game process exit confirmed | old_pid=%s stable=%dms",
                    watched_pid > 0 and tostring(watched_pid) or "unknown",
                    stable_elapsed
                ))
                return true
            end

            last_err = string.format(
                "game process gone stable=%d/%dms",
                stable_elapsed,
                stable_required_ms
            )
        else
            exited_since = 0
            if watched_pid > 0 and tonumber(current_pid) == watched_pid then
                last_err = string.format("old game process still alive | pid=%d", watched_pid)
            elseif watched_pid > 0 and tonumber(current_pid) ~= nil then
                last_err = string.format(
                    "game process still present | old_pid=%d current_pid=%d",
                    watched_pid,
                    tonumber(current_pid) or 0
                )
            else
                last_err = pid_err or string.format("game process still present | pid=%s", tostring(current_pid))
            end
        end

        local elapsed = now - started_at
        if elapsed >= math.max(1000, timeout_limit) then
            return false, string.format(
                "Game process did not exit after %dms: %s",
                elapsed,
                tostring(last_err)
            )
        end

        if now - last_warn_at >= STEP_WARN_INTERVAL_MS then
            last_warn_at = now
            log.warn(string.format(
                "Waiting game process exit | elapsed=%dms err=%s",
                elapsed,
                tostring(last_err)
            ))
        end

        sys.sleep(STEP_RETRY_POLL_MS)
    end
end

function avepoint_is_hotkey_game_process_alive()
    local watched_pid = tonumber(state.f5_launch_pid) or 0
    if watched_pid > 0
        and type(proc) == "table"
        and type(proc.is_alive) == "function"
        and proc.is_alive(watched_pid)
    then
        return true
    end

    local pid = avepoint_find_torchlight_process_pid()
    return pid ~= nil
end

function avepoint_wait_for_game_window_capture_ready(timeout_ms, opts)
    if type(vision) ~= "table"
        or type(vision.capture) ~= "function"
        or type(vision.capture_window) ~= "function"
    then
        return false, "vision.capture/vision.capture_window is not available."
    end

    local started_at = sys.time()
    local last_warn_at = 0
    local last_err = nil
    local stable_ready_count = 0
    local stable_ready_required = 1
    local timeout_limit = tonumber(timeout_ms)
    if timeout_limit ~= nil and timeout_limit <= 0 then
        timeout_limit = nil
    end

    while true do
        local hwnd, hwnd_err = avepoint_resolve_fetch_target_hwnd(opts)
        if hwnd then
            local capture, capture_method = avepoint_capture_target_window_for_image_search(hwnd, {
                capture_set_foreground = true,
                capture_foreground_delay_ms = 60
            })
            if capture then
                stable_ready_count = stable_ready_count + 1
                free_image(capture)
                if stable_ready_count >= stable_ready_required then
                    log.info(string.format(
                        "Game window capture ready | hwnd=%s method=%s stable=%d/%d",
                        tostring(hwnd),
                        tostring(capture_method or ""),
                        stable_ready_count,
                        stable_ready_required
                    ))
                    return true
                end

                last_err = string.format(
                    "capture ready %d/%d method=%s",
                    stable_ready_count,
                    stable_ready_required,
                    tostring(capture_method or "")
                )
            else
                stable_ready_count = 0
                last_err = "capture failed."
            end
        else
            stable_ready_count = 0
            last_err = hwnd_err or "Game window not found."
        end

        local now = sys.time()
        local elapsed = now - started_at
        if timeout_limit ~= nil and elapsed >= math.max(1000, timeout_limit) then
            return false, string.format(
                "Game window capture not ready after %dms: %s",
                elapsed,
                tostring(last_err)
            )
        end

        if now - last_warn_at >= STEP_WARN_INTERVAL_MS then
            last_warn_at = now
            log.warn(string.format(
                "Waiting game window capture ready | elapsed=%dms err=%s",
                elapsed,
                tostring(last_err)
            ))
        end

        sys.sleep(STEP_RETRY_POLL_MS)
    end
end

function avepoint_has_valid_player_state(x, y, z, info)
    local px = tonumber(x)
    local py = tonumber(y)
    local pz = tonumber(z) or 0
    if px == nil or py == nil or info == nil then
        return false
    end

    if math.abs(px) <= 0.001
        and math.abs(py) <= 0.001
        and math.abs(pz) <= 0.001
    then
        return false
    end

    return true
end

function avepoint_wait_for_stable_player_state(timeout_ms, stable_ms, wait_log_label, allow_reinit)
    local started_at = sys.time()
    local last_warn_at = 0
    local last_err = nil
    local stable_since = 0
    local timeout_limit = tonumber(timeout_ms)
    local stable_required_ms = math.max(500, tonumber(stable_ms) or 1200)

    if timeout_limit ~= nil and timeout_limit <= 0 then
        timeout_limit = nil
    end

    while true do
        local x, y, z, pos_err = nav.player_pos()
        local info, info_err = nav.player_info()
        local now = sys.time()

        if avepoint_has_valid_player_state(x, y, z, info) then
            if stable_since == 0 then
                stable_since = now
            end

            local stable_elapsed = now - stable_since
            if stable_elapsed >= stable_required_ms then
                return true, {
                    x = tonumber(x) or 0,
                    y = tonumber(y) or 0,
                    z = tonumber(z) or 0,
                    info = info,
                    stable_ms = stable_elapsed
                }
            end

            last_err = string.format(
                "player state ready stable=%d/%dms pos=%.2f, %.2f, %.2f",
                stable_elapsed,
                stable_required_ms,
                tonumber(x) or 0,
                tonumber(y) or 0,
                tonumber(z) or 0
            )
        else
            stable_since = 0
            if x ~= nil and y ~= nil and info ~= nil then
                last_err = string.format(
                    "player state rejected zero position pos=%.2f, %.2f, %.2f",
                    tonumber(x) or 0,
                    tonumber(y) or 0,
                    tonumber(z) or 0
                )
            else
                last_err = tostring(pos_err or info_err or "Player info unavailable.")
            end
        end

        local elapsed = now - started_at
        if timeout_limit ~= nil and elapsed >= math.max(1000, timeout_limit) then
            return false, string.format(
                "%s timeout after %dms: %s",
                tostring(wait_log_label or "Player state wait"),
                elapsed,
                tostring(last_err)
            )
        end

        if now - last_warn_at >= STEP_WARN_INTERVAL_MS then
            last_warn_at = now
            log.warn(string.format(
                "%s | elapsed=%dms err=%s",
                tostring(wait_log_label or "Waiting player state"),
                elapsed,
                tostring(last_err)
            ))
        end

        if allow_reinit
            and type(nav) == "table"
            and type(nav.is_initialized) == "function"
            and not nav.is_initialized()
        then
            initialized = false
            last_init_error = nil
            next_init_retry_at = 0
            local reinit_ok, reinit_err = avepoint_wait_for_torch_init(
                30000,
                avepoint_hotkey_attach_target_pid(),
                MODE
            )
            if not reinit_ok then
                last_err = reinit_err
            else
                stable_since = 0
            end
        end

        sys.sleep(STEP_RETRY_POLL_MS)
    end
end

function avepoint_wait_click_image_offset_step(step, offset_x, offset_y, timeout_ms, opts)
    local started_at = sys.time()
    local last_warn_at = 0
    local last_err = nil
    local timeout_limit = tonumber(timeout_ms)
    local candidates = {}

    if type(opts) == "table" and type(opts.alternative_steps) == "table" then
        for _, alt in ipairs(opts.alternative_steps) do
            if type(alt) == "table" and type(alt.step) == "table" then
                candidates[#candidates + 1] = {
                    step = alt.step,
                    offset_x = tonumber(alt.offset_x) or 0,
                    offset_y = tonumber(alt.offset_y) or 0
                }
            end
        end
    end

    candidates[#candidates + 1] = {
        step = step,
        offset_x = tonumber(offset_x) or 0,
        offset_y = tonumber(offset_y) or 0
    }

    if timeout_limit ~= nil and timeout_limit <= 0 then
        timeout_limit = nil
    end

    while true do
        local matched = nil
        local target = nil
        local errors = {}

        for _, candidate in ipairs(candidates) do
            local candidate_target, err = fetch_button_for_step(candidate.step, opts)
            if candidate_target then
                matched = candidate
                target = candidate_target
                break
            end

            errors[#errors + 1] = string.format(
                "%s=%s",
                tostring(candidate.step and candidate.step.label or ""),
                tostring(err)
            )
        end

        if matched and target then
            local ok, click_err = click_screen_point(
                tostring(matched.step and matched.step.label or ""),
                (tonumber(target.click_screen_x) or 0) + (tonumber(matched.offset_x) or 0),
                (tonumber(target.click_screen_y) or 0) + (tonumber(matched.offset_y) or 0),
                {
                    hwnd = target.hwnd,
                    click_mode = target.click_mode,
                    hover_delay_ms = target.hover_delay_ms,
                    click_button = target.click_button,
                    click_delay = target.click_delay
                }
            )
            if ok then
                log.info(string.format(
                    "Clicked hotkey offset target %s | offset=(%d,%d)",
                    tostring(matched.step and matched.step.label or ""),
                    tonumber(matched.offset_x) or 0,
                    tonumber(matched.offset_y) or 0
                ))
                return true, tostring(matched.step and matched.step.label or "")
            end

            last_err = click_err
        else
            last_err = table.concat(errors, " | ")
        end

        local now = sys.time()
        local elapsed = now - started_at
        if timeout_limit ~= nil and elapsed >= math.max(1000, timeout_limit) then
            return false, string.format(
                "Offset click timeout [%s]: %s",
                tostring(step and step.label or ""),
                tostring(last_err)
            )
        end

        if now - last_warn_at >= STEP_WARN_INTERVAL_MS then
            last_warn_at = now
            log.warn(string.format(
                "Waiting hotkey offset step %s | elapsed=%dms err=%s",
                tostring(step and step.label or ""),
                elapsed,
                tostring(last_err)
            ))
        end

        sys.sleep(STEP_RETRY_POLL_MS)
    end
end

function avepoint_click_game_window_center_before_start(opts)
    local hwnd, hwnd_err = avepoint_resolve_fetch_target_hwnd(opts)
    if not hwnd then
        return false, hwnd_err
    end

    if type(wnd) ~= "table" or type(wnd.client_rect) ~= "function" then
        return false, "wnd.client_rect is not available."
    end

    local origin_x, origin_y, client_w, client_h = wnd.client_rect(hwnd)
    if type(origin_x) ~= "number"
        or type(origin_y) ~= "number"
        or type(client_w) ~= "number"
        or type(client_h) ~= "number"
        or client_w <= 0
        or client_h <= 0
    then
        return false, "wnd.client_rect failed."
    end

    local center_x = origin_x + client_w * 0.5
    local center_y = origin_y + client_h * 0.5
    local range_x = math.max(40, math.floor(client_w * 0.10))
    local range_y = math.max(30, math.floor(client_h * 0.10))

    for index = 1, 2 do
        local target_x = math.floor(center_x + random_between(-range_x, range_x) + 0.5)
        local target_y = math.floor(center_y + random_between(-range_y, range_y) + 0.5)
        local ok, err = click_screen_point(
            string.format("pre-start center click %d/2", index),
            target_x,
            target_y,
            {
                hwnd = hwnd,
                click_mode = "api",
                click_button = "left",
                click_delay = 50
            }
        )
        if not ok then
            return false, err
        end

        log.info(string.format(
            "Pre-start center click %d/2 | screen=(%d,%d) center=(%d,%d) range=(%d,%d)",
            index,
            target_x,
            target_y,
            math.floor(center_x + 0.5),
            math.floor(center_y + 0.5),
            range_x,
            range_y
        ))

        if index < 2 then
            sys.sleep(avepoint_delay_ms("ui_gap_range"))
        end
    end

    return true
end

function avepoint_open_exit_menu_for_hotkey(exit_step, timeout_ms, max_attempts, fetch_opts)
    local attempts = math.max(1, tonumber(max_attempts) or 5)
    local wait_ms = math.max(500, tonumber(timeout_ms) or 2500)
    local last_err = nil

    for attempt = 1, attempts do
        log.info(string.format(
            "Opening exit menu via ESC | attempt=%d/%d",
            attempt,
            attempts
        ))

        local ok, err = press_escape_key("Hotkey exit game open menu", fetch_opts)
        if not ok then
            last_err = err
        else
            local started_at = sys.time()
            local last_warn_at = 0

            while true do
                local target, fetch_err = fetch_button_for_step(exit_step, fetch_opts)
                if target then
                    log.info(string.format(
                        "Exit menu detected | attempt=%d/%d",
                        attempt,
                        attempts
                    ))
                    return true
                end

                last_err = fetch_err
                local now = sys.time()
                local elapsed = now - started_at
                if elapsed >= wait_ms then
                    break
                end

                if now - last_warn_at >= STEP_WARN_INTERVAL_MS then
                    last_warn_at = now
                    log.warn(string.format(
                        "Waiting exit menu after ESC | attempt=%d/%d elapsed=%dms err=%s",
                        attempt,
                        attempts,
                        elapsed,
                        tostring(last_err)
                    ))
                end

                sys.sleep(STEP_RETRY_POLL_MS)
            end
        end

        log.warn(string.format(
            "Exit menu not detected after ESC | attempt=%d/%d last_err=%s action=press_esc_again",
            attempt,
            attempts,
            tostring(last_err)
        ))

        if attempt < attempts then
            sys.sleep(avepoint_delay_ms("ui_gap", 500))
        end
    end

    return false, string.format(
        "Exit menu did not open after %d ESC attempts: %s",
        attempts,
        tostring(last_err)
    )
end

function avepoint_prepare_for_hotkey_exit_move()
    local cur_x, cur_y, cur_z, pos_err = nav.player_pos()
    if cur_x == nil or cur_y == nil then
        return false, pos_err or "Player position unavailable before exit move."
    end

    local start_x = cur_x
    local start_y = cur_y
    local target = nil
    local target_label = "relative step"
    local first_point = ROUTE_POINTS[1]

    if type(first_point) == "table" then
        local first_distance = distance_2d(cur_x, cur_y, first_point.x, first_point.y)
        if first_distance > ARRIVE_TOLERANCE then
            target = {
                x = first_point.x,
                y = first_point.y,
                z = first_point.z
            }
            target_label = "outside first point"
        elseif type(ROUTE_POINTS[2]) == "table" then
            local second_point = ROUTE_POINTS[2]
            local dx = second_point.x - cur_x
            local dy = second_point.y - cur_y
            local length = math.sqrt(dx * dx + dy * dy)
            if length > 1 then
                local step_distance = 260
                target = {
                    x = cur_x + (dx / length) * step_distance,
                    y = cur_y + (dy / length) * step_distance,
                    z = cur_z
                }
                target_label = "small step toward outer route"
            end
        end
    end

    if type(target) ~= "table" then
        target = {
            x = cur_x + 260,
            y = cur_y,
            z = cur_z
        }
    end

    log.info(string.format(
        "Pre-exit move start | target=%s from=%.2f, %.2f, %.2f to=%.2f, %.2f, %.2f",
        target_label,
        cur_x,
        cur_y,
        cur_z or 0,
        tonumber(target.x) or 0,
        tonumber(target.y) or 0,
        tonumber(target.z) or 0
    ))

    local projected_point, project_err = nav.project_move_call_mouse_target(target.x, target.y, {
        mode = "direction",
        radius = 180
    })
    if not projected_point then
        return false, project_err
    end

    log.info(string.format(
        "Pre-exit move click | client=(%d,%d) screen=(%d,%d) world_distance=%.2f",
        tonumber(projected_point.client_x) or 0,
        tonumber(projected_point.client_y) or 0,
        tonumber(projected_point.screen_x) or 0,
        tonumber(projected_point.screen_y) or 0,
        tonumber(projected_point.world_distance) or 0
    ))

    local ok, move_err = click_screen_point(
        "pre-exit move click",
        projected_point.screen_x,
        projected_point.screen_y,
        {
            hwnd = projected_point.hwnd,
            click_mode = "api",
            click_button = "left",
            click_delay = 50
        }
    )
    if not ok then
        return false, move_err
    end

    local started_at = sys.time()
    local last_warn_at = 0
    local last_err = nil
    local stable_since = 0
    local stable_ref_x = nil
    local stable_ref_y = nil
    local stable_required_ms = 1500
    local stable_tolerance = 18
    while true do
        local now = sys.time()
        local x, y, z, err = nav.player_pos()
        if x ~= nil and y ~= nil then
            local moved_distance = distance_2d(start_x, start_y, x, y)
            local remaining_distance = distance_2d(x, y, target.x, target.y)
            if moved_distance >= ARRIVE_TOLERANCE or remaining_distance <= ARRIVE_TOLERANCE then
                if stable_ref_x == nil
                    or stable_ref_y == nil
                    or distance_2d(stable_ref_x, stable_ref_y, x, y) > stable_tolerance
                then
                    stable_ref_x = x
                    stable_ref_y = y
                    stable_since = now
                end

                local stable_elapsed = now - (stable_since or now)
                if stable_elapsed >= stable_required_ms then
                    log.info(string.format(
                        "Pre-exit move ready | moved=%.2f remaining=%.2f stable=%dms pos=%.2f, %.2f, %.2f",
                        moved_distance,
                        remaining_distance,
                        stable_elapsed,
                        x,
                        y,
                        z or 0
                    ))
                    return true
                end

                last_err = string.format(
                    "move reached, stabilizing %d/%dms remaining=%.2f",
                    stable_elapsed,
                    stable_required_ms,
                    remaining_distance
                )
            else
                stable_since = 0
                stable_ref_x = nil
                stable_ref_y = nil
                last_err = string.format(
                    "moved=%.2f remaining=%.2f",
                    moved_distance,
                    remaining_distance
                )
            end
        else
            stable_since = 0
            stable_ref_x = nil
            stable_ref_y = nil
            last_err = err
        end

        local elapsed = now - started_at
        if elapsed >= 12000 then
            return false, string.format(
                "Pre-exit move timeout after %dms: %s",
                elapsed,
                tostring(last_err)
            )
        end

        if now - last_warn_at >= STEP_WARN_INTERVAL_MS then
            last_warn_at = now
            log.warn(string.format(
                "Waiting pre-exit move | elapsed=%dms err=%s",
                elapsed,
                tostring(last_err)
            ))
        end

        sys.sleep(STEP_RETRY_POLL_MS)
    end
end

function avepoint_hotkey_exit_game()
    local latest_pid = avepoint_find_torchlight_process_pid()
    if latest_pid ~= nil then
        state.f5_launch_pid = tonumber(latest_pid) or state.f5_launch_pid
        state.f5_launch_hwnd = nil
    end

    local exit_fetch_opts = {
        preferred_pid = tonumber(latest_pid) or tonumber(state.f5_launch_pid) or 0,
        preferred_hwnd = tonumber(state.f5_launch_hwnd) or nil
    }
    local use_ui_only_exit = state.f5_allow_ui_only_exit == true
    local ui_only_reason = nil

    if not use_ui_only_exit and not initialized then
        use_ui_only_exit = true
        ui_only_reason = "Torch API not ready yet"
    end

    if not use_ui_only_exit then
        local cur_x, cur_y, cur_z, pos_err = nav.player_pos()
        if cur_x == nil or cur_y == nil or cur_z == nil then
            use_ui_only_exit = true
            ui_only_reason = pos_err or "Player position unavailable."
        else
            local outside, nearest_outside = is_outside_position(cur_x, cur_y, cur_z)
            if not outside then
                use_ui_only_exit = true
                ui_only_reason = string.format(
                    "Exit game requires outside position. nearest_distance=%s z_diff=%s",
                    nearest_outside and string.format("%.2f", tonumber(nearest_outside.distance) or 0) or "nil",
                    nearest_outside and nearest_outside.z_diff and string.format("%.2f", tonumber(nearest_outside.z_diff) or 0) or "nil"
                )
            end
        end
    end

    if state.running then
        stop_automation("AvePoint automation stopped for hotkey exit game flow")
    end

    if use_ui_only_exit then
        log.warn(string.format(
            "Hotkey exit game flow using UI-only fallback | reason=%s",
            tostring(ui_only_reason or "forced")
        ))
    else
        local move_ok, move_err = avepoint_prepare_for_hotkey_exit_move()
        if not move_ok then
            return false, move_err
        end
    end

    local exit_step = {
        label = "ExitGame"
    }
    local confirm_step = {
        label = "ExitGameConfirm"
    }

    local ok, err = avepoint_open_exit_menu_for_hotkey(exit_step, 2500, 5, exit_fetch_opts)
    if not ok then
        return false, err
    end

    local exit_ok, exit_err = avepoint_wait_click_image_step(exit_step, STEP_FETCH_TIMEOUT_MS, confirm_step, exit_fetch_opts)
    if not exit_ok then
        return false, exit_err
    end

    sys.sleep(avepoint_delay_ms("ui_gap", 500))

    local confirm_ok, confirm_err = avepoint_wait_click_image_step(confirm_step, STEP_FETCH_TIMEOUT_MS, nil, exit_fetch_opts)
    if not confirm_ok then
        return false, confirm_err
    end

    avepoint_clear_nav_binding(true)
    state.f5_allow_ui_only_exit = false
    log.info("Hotkey exit game flow finished")
    return true
end

function avepoint_hotkey_launch_and_enter_game()
    if state.running then
        stop_automation("AvePoint automation stopped for hotkey launch game flow")
    end

    avepoint_clear_nav_binding(true)
    state.f5_allow_ui_only_exit = false
    state.f5_launch_pid = 0
    state.f5_launch_hwnd = nil

    local launch_ok, launch_err = avepoint_launch_torchlight_game()
    if not launch_ok then
        return false, launch_err
    end

    local launch_state_ok, launch_pid_or_err, launch_hwnd = avepoint_wait_for_launch_process_window_ready(90000)
    if not launch_state_ok then
        return false, launch_pid_or_err
    end
    local launch_pid = tonumber(launch_pid_or_err) or 0
    state.f5_launch_pid = launch_pid
    state.f5_launch_hwnd = launch_hwnd

    avepoint_wait_post_launch_settle(15000)

    local launch_image_search_opts = {
        preferred_pid = launch_pid,
        preferred_hwnd = launch_hwnd
    }
    local attach_target = launch_pid > 0 and launch_pid or PROCESS_NAME

    do
        local quick_init_ok, quick_init_err = avepoint_wait_for_torch_init(2500, attach_target, MODE)
        if quick_init_ok then
            local already_in_game, state_or_err = avepoint_wait_for_stable_player_state(
                2500,
                1200,
                "Checking in-game state before ClickStartGame",
                false
            )
            if already_in_game then
                log.info(string.format(
                    "Hotkey start game flow skipped start buttons | already in game pos=%.2f, %.2f, %.2f stable=%dms player_info=ok",
                    tonumber(state_or_err.x) or 0,
                    tonumber(state_or_err.y) or 0,
                    tonumber(state_or_err.z) or 0,
                    tonumber(state_or_err.stable_ms) or 0
                ))
                return true
            end

            log.info(string.format(
                "Hotkey start game flow requires start buttons | reason=%s",
                tostring(state_or_err)
            ))
        else
            initialized = false
            last_init_error = quick_init_err
            next_init_retry_at = 0
            log.info(string.format(
                "Torch init not ready before hotkey start buttons | reason=%s action=search_start_images_by_hwnd",
                tostring(quick_init_err)
            ))
        end
    end

    local window_ready_ok, window_ready_err = avepoint_wait_for_game_window_capture_ready(0, launch_image_search_opts)
    if not window_ready_ok then
        return false, window_ready_err
    end

    local pre_click_ok, pre_click_err = avepoint_click_game_window_center_before_start(launch_image_search_opts)
    if not pre_click_ok then
        log.warn(string.format(
            "Skipping pre-start center clicks | err=%s",
            tostring(pre_click_err)
        ))
    end

    local click_start_step = {
        label = "ClickStartGame"
    }
    local start_game_step = {
        label = "StartGame"
    }

    local click_start_opts = {
        preferred_pid = launch_image_search_opts.preferred_pid,
        preferred_hwnd = launch_image_search_opts.preferred_hwnd,
        alternative_steps = {
            {
                step = start_game_step,
                offset_x = 110,
                offset_y = 0
            }
        }
    }

    local ok, clicked_label_or_err = avepoint_wait_click_image_offset_step(
        click_start_step,
        0,
        175,
        0,
        click_start_opts
    )
    if not ok then
        return false, clicked_label_or_err
    end

    if clicked_label_or_err ~= "StartGame" then
        sys.sleep(avepoint_delay_ms("ui_gap", 500))

        ok, clicked_label_or_err = avepoint_wait_click_image_offset_step(start_game_step, 110, 0, 0, launch_image_search_opts)
        if not ok then
            return false, clicked_label_or_err
        end
    else
        log.info("StartGame detected before ClickStartGame | action=skip_click_start")
    end

    local init_ok, init_err = avepoint_wait_for_torch_init(5000, attach_target, MODE)
    if not init_ok then
        if avepoint_is_hotkey_game_process_alive() then
            avepoint_clear_nav_binding(false)
            last_init_error = init_err
            state.f5_allow_ui_only_exit = true
            log.warn(string.format(
                "Hotkey start game flow continuing without nav after StartGame | reason=%s action=allow_ui_only_exit",
                tostring(init_err)
            ))
            return true
        end
        return false, init_err
    end

    local ready_ok, state_or_err = avepoint_wait_for_stable_player_state(
        5000,
        1200,
        "Waiting valid player state after hotkey start_game",
        true
    )
    if not ready_ok then
        if avepoint_is_hotkey_game_process_alive() then
            state.f5_allow_ui_only_exit = true
            log.warn(string.format(
                "Hotkey start game flow continuing without stable player state | reason=%s action=allow_ui_only_exit",
                tostring(state_or_err)
            ))
            return true
        end
        return false, state_or_err
    end

    state.f5_allow_ui_only_exit = false
    log.info(string.format(
        "Hotkey start game flow finished | pos=%.2f, %.2f, %.2f stable=%dms player_info=ok",
        tonumber(state_or_err.x) or 0,
        tonumber(state_or_err.y) or 0,
        tonumber(state_or_err.z) or 0,
        tonumber(state_or_err.stable_ms) or 0
    ))
    return true
end

function update_f6_loop(now)
    if state.f6_loop_active ~= true then
        return true
    end

    local round = tonumber(state.f6_loop_round) or 0
    local total = tonumber(state.f6_loop_total_rounds) or F6_LOOP_TOTAL_ROUNDS
    local phase = tostring(state.f6_loop_phase or "")

    if phase == "launch_and_enter" then
        local ok, err = avepoint_hotkey_launch_and_enter_game()
        if not ok then
            return fail_f6_loop("launch/enter failed: " .. tostring(err))
        end

        state.f6_loop_cycle_pid = capture_current_game_pid()
        local player_state = nil
        if not initialized or not (type(nav) == "table" and type(nav.is_initialized) == "function" and nav.is_initialized()) then
            local attach_ok, attach_err = avepoint_wait_for_torch_init(
                30000,
                avepoint_hotkey_attach_target_pid(),
                MODE
            )
            if not attach_ok then
                return fail_f6_loop("Torch API attach failed after launch/enter: " .. tostring(attach_err))
            end
        end

        local player_ok, player_state_or_err = avepoint_wait_for_stable_player_state(
            30000,
            1200,
            "Waiting valid player state after F6 launch/enter",
            true
        )
        if not player_ok then
            return fail_f6_loop("player state not ready after launch/enter: " .. tostring(player_state_or_err))
        end
        player_state = player_state_or_err

        ok, err = start_automation()
        if not ok then
            return fail_f6_loop("automation start failed: " .. tostring(err))
        end

        state.f6_loop_phase = "wait_first_entry"
        state.f6_loop_started_at = 0
        state.f6_loop_deadline_at = 0
        state.f6_loop_exit_pending = false
        state.f6_loop_wait_until = 0
        state.f6_loop_cycle_pid = capture_current_game_pid()

        log.info(string.format(
            "F6 3-round loop cycle %d/%d | phase=wait_first_entry timer_will_start_on_first_map_D pos=%.2f, %.2f, %.2f stable=%dms",
            round,
            total,
            tonumber(player_state and player_state.x) or 0,
            tonumber(player_state and player_state.y) or 0,
            tonumber(player_state and player_state.z) or 0,
            tonumber(player_state and player_state.stable_ms) or 0
        ))
        log.info("AvePoint automation started for F6 3-round loop")
        return true
    end

    if phase == "wait_first_entry" then
        if state.running ~= true then
            return fail_f6_loop("Automation stopped before first map entry.")
        end
        return true
    end

    if phase == "run_until_safe_exit" then
        if state.running ~= true then
            return fail_f6_loop("Automation stopped before safe exit.")
        end

        if state.f6_loop_exit_pending ~= true then
            local deadline_at = tonumber(state.f6_loop_deadline_at) or 0
            if deadline_at > 0 and now >= deadline_at then
                state.f6_loop_exit_pending = true
                log.info(string.format(
                    "F6 3-round loop cycle %d/%d | exit_pending=true elapsed=%dms action=wait_current_run_then_exit",
                    round,
                    total,
                    now - (tonumber(state.f6_loop_started_at) or now)
                ))
            end
        end

        return true
    end

    if phase == "exit_game" then
        local cycle_pid = tonumber(state.f6_loop_cycle_pid) or 0
        if cycle_pid <= 0 then
            cycle_pid = capture_current_game_pid()
        end
        state.f6_loop_cycle_pid = cycle_pid

        local ok, err = avepoint_hotkey_exit_game()
        if not ok then
            return fail_f6_loop("exit game failed: " .. tostring(err))
        end

        local exit_wait_ok, exit_wait_err = avepoint_wait_for_game_process_exit(cycle_pid, 120000)
        if not exit_wait_ok then
            return fail_f6_loop("wait process exit failed: " .. tostring(exit_wait_err))
        end

        if round >= total then
            log.info(string.format(
                "F6 3-round loop finished | cycles=%d",
                total
            ))
            reset_f6_loop_state()
            return true
        end

        state.f6_loop_phase = "cooldown"
        state.f6_loop_wait_until = sys.time() + F6_LOOP_RELAUNCH_DELAY_MS
        state.f6_loop_started_at = 0
        state.f6_loop_deadline_at = 0
        state.f6_loop_exit_pending = false

        log.info(string.format(
            "F6 3-round loop cycle %d/%d complete | wait_before_next_launch=%dms",
            round,
            total,
            F6_LOOP_RELAUNCH_DELAY_MS
        ))
        return true
    end

    if phase == "cooldown" then
        local wait_until = tonumber(state.f6_loop_wait_until) or 0
        if now < wait_until then
            return true
        end

        start_f6_loop_cycle(round + 1)
        return true
    end

    return fail_f6_loop("Unknown F6 phase: " .. tostring(phase))
end

function avepoint_hotkey_two_cycle_observe_flow()
    local total_cycles = 5
    if state.running then
        stop_automation("AvePoint automation stopped for F5 5-cycle observe flow")
    end

    for cycle = 1, total_cycles do
        local cycle_pid = nil
        log.info(string.format(
            "F5 observe flow cycle %d/%d | phase=launch_and_enter",
            cycle
            ,
            total_cycles
        ))

        local ok, err = avepoint_hotkey_launch_and_enter_game()
        if not ok then
            return false, string.format(
                "F5 observe flow cycle %d launch/enter failed: %s",
                cycle,
                tostring(err)
            )
        end

        local wait_before_exit_ms = avepoint_delay_ms("long_action", 10000)
        log.info(string.format(
            "F5 observe flow cycle %d/%d | wait_before_exit=%dms",
            cycle,
            total_cycles,
            wait_before_exit_ms
        ))
        sys.sleep(wait_before_exit_ms)

        log.info(string.format(
            "F5 observe flow cycle %d/%d | phase=exit_game",
            cycle,
            total_cycles
        ))

        cycle_pid = tonumber(type(nav) == "table" and nav.pid or 0) or 0
        if cycle_pid <= 0 then
            local found_pid = avepoint_find_torchlight_process_pid()
            cycle_pid = tonumber(found_pid) or 0
        end

        ok, err = avepoint_hotkey_exit_game()
        if not ok then
            return false, string.format(
                "F5 observe flow cycle %d exit failed: %s",
                cycle,
                tostring(err)
            )
        end

        if cycle < total_cycles then
            local exit_wait_ok, exit_wait_err = avepoint_wait_for_game_process_exit(cycle_pid, 120000)
            if not exit_wait_ok then
                return false, string.format(
                    "F5 observe flow cycle %d wait process exit failed: %s",
                    cycle,
                    tostring(exit_wait_err)
                )
            end

            local wait_before_next_launch_ms = avepoint_delay_ms("long_action", 20000)
            log.info(string.format(
                "F5 observe flow cycle %d/%d complete | wait_before_next_launch=%dms",
                cycle,
                total_cycles,
                wait_before_next_launch_ms
            ))
            sys.sleep(wait_before_next_launch_ms)
        end
    end

    log.info(string.format(
        "F5 observe flow finished | cycles=%d",
        total_cycles
    ))
    return true
end

local note_entry_portal_click_attempt

function update_entry_buttons()
    local map, _, map_err = active_map_or_err()
    if not map then
        return false, map_err
    end

    local step = map.entry_button_steps[state.button_index]
    if not step then
        reset_button_retry()
        set_stage("begin_map_route", 0)
        return true
    end

    local now = sys.time()
    if state.button_retry_index ~= state.button_index then
        state.button_retry_index = state.button_index
        state.button_retry_started_at = now
        state.button_retry_last_warn_at = 0
        state.button_retry_dumped = false
    end

    local target, err = fetch_button_for_step(step)
    if not target then
        local elapsed = now - (state.button_retry_started_at or now)

        if elapsed >= STEP_DEBUG_DUMP_AFTER_MS and not state.button_retry_dumped then
            state.button_retry_dumped = true
            nav.dump_visible_controls({
                include_buttons = true,
                include_texts = true,
                include_images = false,
                header = "AvePoint debug visible controls for " .. tostring(step.label or ""),
                limit = 40
            })
        end

        if elapsed >= STEP_FETCH_TIMEOUT_MS then
            return false, string.format(
                "Fetch button timeout [%s]: %s",
                tostring(step.label or ""),
                tostring(err)
            )
        end

        if now - (state.button_retry_last_warn_at or 0) >= STEP_WARN_INTERVAL_MS then
            state.button_retry_last_warn_at = now
            log.warn(string.format(
                "Waiting button %s | elapsed=%dms err=%s",
                tostring(step.label or ""),
                elapsed,
                tostring(err)
            ))
        end

        set_stage("entry_buttons", STEP_RETRY_POLL_MS)
        return true
    end

    local ok, click_err, retryable = click_fetched_target(step, target)
    if not ok then
        if retryable then
            log.warn(tostring(click_err))
            set_stage("entry_buttons", IMAGE_CLICK_RETRY_STAGE_DELAY_MS)
            return true
        end
        return false, click_err
    end

    state.last_clicked_entry_label = step.label
    note_entry_portal_click_attempt(map, step, "entry_buttons")
    state.button_index = state.button_index + 1
    reset_button_retry()
    local next_delay = tonumber(step.after_click_delay_ms) ~= nil
        and avepoint_delay_ms("long_action", tonumber(step.after_click_delay_ms))
        or random_entry_flow_delay_ms()
    if state.button_index > #map.entry_button_steps then
        set_stage("entry_portal_confirm", next_delay)
    else
        set_stage("entry_buttons", next_delay)
    end

    return true
end

start_automation = function(mode_id, mode_name)
    TASK_MODE.runner = nil
    TASK_MODE.prepare_start(mode_id or TASK_MODE.GOLD, mode_name or TASK_MODE.label(TASK_MODE.GOLD))

    local resumed, resume_err = try_resume_automation()
    if resumed then
        return true
    end
    if resume_err and resume_err ~= "resume snapshot missing" then
        log.info("Resume current run skipped | reason=" .. tostring(resume_err))
    end

    local ok, err = activate_random_map("start")
    if not ok then
        return false, err
    end

    reset_cleanup_schedule("start")

    state.running = true
    schedule_human_idle_move()
    if should_run_startup_inside_recovery() then
        log.warn("Startup detected non-outside position | action=press T then D before outer route")
        set_stage("startup_press_t", 0)
        return true
    end

    return start_outer_route()
end

function TASK_MODE.start_selected()
    local mode_id, mode_name = TASK_MODE.read_configured()
    if mode_id == TASK_MODE.GOLD then
        local ok, err = start_automation(mode_id, mode_name)
        if not ok then
            return false, err
        end
        return true, mode_name, mode_id
    end

    local runner, load_err = TASK_MODE.load_runner(mode_id)
    if not runner then
        return false, load_err
    end

    TASK_MODE.prepare_start(mode_id, mode_name)
    TASK_MODE.runner = runner

    local start_fn = runner.start
    if type(start_fn) ~= "function" then
        TASK_MODE.runner = nil
        return false, string.format("%s 模块缺少 start(ctx)", mode_name)
    end

    local ok, start_ok, err = pcall(start_fn, TASK_MODE.build_context())
    if not ok then
        stop_automation()
        return false, start_ok
    end
    if start_ok == false then
        stop_automation()
        return false, err
    end

    state.running = true
    return true, mode_name, mode_id
end

function TASK_MODE.update_selected(now)
    if tonumber(state.task_mode_id) == TASK_MODE.GOLD then
        if type(human_mouse) == "table" and type(human_mouse.tick_async_move) == "function" then
            local tick_ok, tick_result_or_err = human_mouse.tick_async_move(now)
            if tick_ok == false and tick_result_or_err then
                log.warn("Human idle async move failed: " .. tostring(tick_result_or_err))
            elseif type(tick_result_or_err) == "table"
                and tick_result_or_err.cancel_reason == "manual_override"
            then
                log.info(string.format(
                    "Human idle async move canceled | reason=manual_override pos=(%d,%d) distance=%.2f",
                    tonumber(tick_result_or_err.screen_x) or 0,
                    tonumber(tick_result_or_err.screen_y) or 0,
                    tonumber(tick_result_or_err.distance) or 0
                ))
            end
        end

        local revive_triggered = avepoint_maybe_handle_map_death()
        if revive_triggered then
            return true
        end

        maybe_pickup_loot(now)
        maybe_perform_human_idle_move(now)
        if now < (state.wait_until or 0) then
            return true
        end

        local step_ok, err
        if state.route then
            local map = current_map
            local skip_route_tick = false
            if state.route.name == "Map route"
                and type(map) == "table"
                and map.map_route_escape_enabled == true
                and state.route_escape_sent ~= true
                and now >= (state.route_escape_due_at or 0)
            then
                local esc_ok, esc_err = press_escape_key("skip map intro")
                if not esc_ok then
                    step_ok, err = false, esc_err
                else
                    state.route_escape_sent = true
                    state.route_escape_hold_until = now + (tonumber(map.map_route_escape_hold_ms) or MAP_ROUTE_ESCAPE_HOLD_MS)
                    state.route.next_repath_at = state.route_escape_hold_until
                    step_ok = true
                    skip_route_tick = true
                end
            end

            if step_ok ~= false and not skip_route_tick then
                step_ok, err = update_route(now)
            end
        else
            step_ok, err = update_stage()
        end

        return step_ok, err
    end

    if type(TASK_MODE.runner) ~= "table" then
        return false, "当前任务模式没有可用的运行器"
    end

    local update_fn = TASK_MODE.runner.update
    if type(update_fn) ~= "function" then
        return false, string.format("%s 模块缺少 update(now, ctx)", tostring(state.task_mode_name or "当前"))
    end

    local ok, step_ok, err = pcall(update_fn, now, TASK_MODE.build_context())
    if not ok then
        return false, step_ok
    end
    if step_ok == false then
        return false, err
    end

    return true
end

function begin_route_with_retry(stage_name, points, route_name, next_stage)
    local now = sys.time()
    local err = nil
    if state.route_start_key ~= stage_name then
        state.route_start_key = stage_name
        state.route_start_started_at = now
        state.route_start_last_warn_at = 0
        state.route_start_ready_at = 0
    end

    local elapsed = now - (state.route_start_started_at or now)
    if elapsed >= ROUTE_START_TIMEOUT_MS then
        reset_route_start_retry()
        if route_name == "Map route"
            and state.last_clicked_entry_label == "MysteryBossDetail EnterBtn"
        then
            return false, string.format(
                "%s start timeout after %dms: portal did not open, likely insufficient item count.",
                tostring(route_name or stage_name),
                elapsed
            )
        end
        return false, string.format(
            "%s start timeout after %dms: %s",
            tostring(route_name or stage_name),
            elapsed,
            tostring(err)
        )
    end

    if route_name == "Map route" then
        local cur_x, cur_y, _, pos_err = nav.player_pos()
        if cur_x == nil or cur_y == nil then
            state.route_start_ready_at = 0
            err = pos_err or "Player position unavailable."
        else
            if (state.route_start_ready_at or 0) == 0 then
                state.route_start_ready_at = now
            end

            local ready_elapsed = now - (state.route_start_ready_at or now)
            if ready_elapsed < MAP_ROUTE_READY_STABLE_MS then
                err = string.format("Player position ready, stabilizing %d/%dms", ready_elapsed, MAP_ROUTE_READY_STABLE_MS)
            end
        end
    end

    if not err then
        local ok, start_err = start_route(points, route_name, next_stage)
        if ok then
            reset_route_start_retry()
            return true
        end
        err = start_err
    end

    if now - (state.route_start_last_warn_at or 0) >= ROUTE_START_WARN_INTERVAL_MS then
        state.route_start_last_warn_at = now
        log.warn(string.format(
            "Waiting %s start | elapsed=%dms err=%s",
            tostring(route_name or stage_name),
            elapsed,
            tostring(err)
        ))
    end

    set_stage(stage_name, ROUTE_START_RETRY_POLL_MS)
    return true
end

function maybe_perform_human_idle_move(now)
    if not state.running or type(state.route) ~= "table" then
        return
    end

    if type(human_mouse) == "table"
        and type(human_mouse.has_async_move) == "function"
        and human_mouse.has_async_move()
    then
        return
    end

    if now < (state.human_idle_move_due_at or 0) then
        return
    end

    local route = state.route
    local route_name = tostring(route.name or "")
    if route_name == ""
        or route_name == "Outer route"
        or route_name == "Stash route"
        or route_name == "Stash return route"
    then
        schedule_human_idle_move()
        return
    end

    local cur_x, cur_y, cur_z = nav.player_pos()
    if cur_x ~= nil and cur_y ~= nil then
        local outside = is_outside_position(cur_x, cur_y, cur_z)
        if outside then
            schedule_human_idle_move()
            return
        end
    end

    if route_name == "Map route" then
        local point_count = type(route.points) == "table" and #route.points or 0
        local route_index = tonumber(route.index) or 1
        if point_count <= 3 or route_index >= (point_count - 2) then
            schedule_human_idle_move()
            return
        end
    end

    local hwnd, hwnd_err = nav.window_hwnd()
    if not hwnd then
        log.warn("Human idle mouse move skipped: " .. tostring(hwnd_err))
        schedule_human_idle_move()
        return
    end

    local ok, result_or_err = human_mouse.start_async_random_move_in_window({
        hwnd = hwnd,
        mouse_mode = "api",
        min_duration_ms = HUMAN_MOUSE_MOVE_DURATION.min_ms,
        max_duration_ms = HUMAN_MOUSE_MOVE_DURATION.max_ms,
        duration_center_ms = HUMAN_MOUSE_MOVE_DURATION.center_ms,
        duration_sigma_ms = HUMAN_MOUSE_MOVE_DURATION.sigma_ms,
        duration_gaussian_weight = HUMAN_MOUSE_MOVE_DURATION.gaussian_weight,
        duration_distribution = "gaussian",
        report_rate_hz = HUMAN_MOUSE_MOVE_DURATION.report_rate_hz,
        edge_margin = 36
    })
    if not ok then
        log.warn("Human idle mouse move failed: " .. tostring(result_or_err))
        schedule_human_idle_move()
        return
    end

    log.info(string.format(
        "Human idle mouse move scheduled | route=%s target=(%d,%d) duration=%dms",
        route_name,
        tonumber(result_or_err.screen_x) or 0,
        tonumber(result_or_err.screen_y) or 0,
        tonumber(result_or_err.duration_ms) or 0
    ))
    schedule_human_idle_move()
end

local function resolve_entry_confirm_policy(map)
    local policy = type(map) == "table" and map.entry_confirm_policy or nil
    if type(policy) ~= "table" then
        return nil
    end
    return policy
end

local function entry_confirm_retry_limit(policy)
    local retry_limit = math.max(0, math.floor(tonumber(policy and policy.retry_limit) or 0))
    if retry_limit <= 0 then
        return nil
    end
    return retry_limit
end

local function entry_confirm_policy_tracks_step(policy, portal_step)
    if type(policy) ~= "table" then
        return false
    end

    local tracked_label = tostring(policy.retry_counter_step_label or "")
    local portal_label = tostring(type(portal_step) == "table" and portal_step.label or "")
    return tracked_label == "" or tracked_label == portal_label
end

note_entry_portal_click_attempt = function(map, portal_step, source)
    local policy = resolve_entry_confirm_policy(map)
    local retry_limit = entry_confirm_retry_limit(policy)
    if retry_limit == nil or not entry_confirm_policy_tracks_step(policy, portal_step) then
        return false
    end

    state.entry_portal_click_attempts = (tonumber(state.entry_portal_click_attempts) or 0) + 1
    local source_suffix = source and source ~= "" and (" | source=" .. tostring(source)) or ""
    log.info(string.format(
        "%s %d/%d%s",
        tostring(policy.retry_counter_log_label or "入口触发点击"),
        tonumber(state.entry_portal_click_attempts) or 0,
        retry_limit,
        source_suffix
    ))
    return true
end

local function apply_entry_confirm_retry_limit_policy(map)
    local policy = resolve_entry_confirm_policy(map)
    local retry_limit = entry_confirm_retry_limit(policy)
    local attempt_count = tonumber(state.entry_portal_click_attempts) or 0
    if retry_limit == nil or attempt_count < retry_limit then
        return false, "entry confirm retry limit policy not triggered"
    end

    local policy_kind = tostring(policy.kind or "")
    if policy_kind ~= "switch_random_map" then
        return false, "unsupported entry confirm policy kind: " .. policy_kind
    end

    log.warn(string.format(
        "%s连续 %d 次入口重试后仍未进图，按策略切换地图 | action=switch_random_map",
        tostring(policy.exhausted_log_label or map.label or current_map_key or "当前地图"),
        attempt_count
    ))

    local ok, err = press_escape_key(tostring(policy.close_before_switch_label or "close unavailable portal page"))
    if not ok then
        return false, err
    end

    local old_map_key = tostring(current_map_key or "")
    local excluded_map_key = policy.exclude_current_map ~= false and old_map_key or nil
    ok, err = activate_random_map(
        tostring(policy.switch_reason or "entry_confirm_retry_limit"),
        excluded_map_key ~= "" and excluded_map_key or nil
    )
    if not ok then
        return false, err
    end

    reset_button_retry()
    reset_entry_portal_state()
    set_stage(
        tostring(policy.retry_stage or "entry_buttons"),
        tonumber(policy.retry_stage_delay_ms) or random_entry_flow_delay_ms()
    )
    return true
end

local function maybe_apply_entry_confirm_retry_limit_policy(map)
    local policy = resolve_entry_confirm_policy(map)
    local retry_limit = entry_confirm_retry_limit(policy)
    if retry_limit == nil or (tonumber(state.entry_portal_click_attempts) or 0) < retry_limit then
        return false, nil
    end

    return apply_entry_confirm_retry_limit_policy(map)
end

function avepoint_update_entry_portal_confirm(map)
    local now = sys.time()
    if (state.entry_portal_started_at or 0) == 0 then
        state.entry_portal_started_at = now
        state.entry_portal_last_warn_at = 0
        state.entry_portal_ready_at = 0
        state.entry_portal_retry_due_at = now + avepoint_delay_ms("key_stage", KEY_STAGE_DELAY_MS)
    end

    local elapsed = now - (state.entry_portal_started_at or now)
    if type(proc) == "table"
        and type(proc.exists) == "function"
        and type(nav) == "table"
        and nav.pid ~= nil
        and not proc.exists(nav.pid)
    then
        return false, "Game process exited while waiting for map entry."
    end

    local portal_step = nil
    if type(map.entry_button_steps) == "table" and #map.entry_button_steps > 0 then
        portal_step = map.entry_button_steps[#map.entry_button_steps]
    end

    local portal_page_visible = false
    local portal_target = nil
    local entry_err = nil
    if type(portal_step) == "table" then
        local portal_err = nil
        portal_target, portal_err = fetch_button_for_step(portal_step)
        if portal_target then
            portal_page_visible = true
            entry_err = string.format(
                "Portal page still visible [%s]",
                tostring(portal_step.label or "")
            )
        elseif portal_err
            and not (
                type(portal_err) == "string"
                and portal_err ~= ""
                and portal_err:find("not found", 1, true) ~= nil
            )
        then
            return false, string.format(
                "Portal visibility check failed [%s]: %s",
                tostring(portal_step.label or ""),
                tostring(portal_err)
            )
        end
    end

    local step = type(map.entry_confirm_step) == "table" and map.entry_confirm_step or make_select_ditu_confirm_step()
    local target, err = fetch_button_for_step(step)
    if target then
        if target.kind == "image" then
            local pre_ok, pre_err = click_screen_point(
                "SelectDituConfirm PreOffset",
                (tonumber(target.click_screen_x) or 0) - 100,
                (tonumber(target.click_screen_y) or 0) + 54,
                {
                    hwnd = target.hwnd,
                    click_mode = target.click_mode,
                    hover_delay_ms = target.hover_delay_ms,
                    click_button = target.click_button,
                    click_delay = target.click_delay
                }
            )
            if not pre_ok then
                return false, string.format(
                    "Click target failed [%s]: %s",
                    "SelectDituConfirm PreOffset",
                    tostring(pre_err)
                )
            end
        end

        local ok, click_err, retryable = click_fetched_target(step, target)
        if not ok then
            if retryable then
                log.warn(tostring(click_err))
                set_stage("entry_portal_confirm", IMAGE_CLICK_RETRY_STAGE_DELAY_MS)
                return true
            end
            return false, click_err
        end

        local confirm_delay_ms = avepoint_delay_ms("key_stage", KEY_STAGE_DELAY_MS)
        state.entry_portal_ready_at = 0
        state.entry_portal_retry_due_at = now + confirm_delay_ms
        set_stage("entry_portal_confirm", confirm_delay_ms)
        return true
    end

    if err
        and not (
            type(err) == "string"
            and err ~= ""
            and err:find("not found", 1, true) ~= nil
        )
    then
        return false, err
    end

    if portal_page_visible then
        state.entry_portal_ready_at = 0
        if portal_target and now >= (state.entry_portal_retry_due_at or 0) then
            local handled, policy_err = maybe_apply_entry_confirm_retry_limit_policy(map)
            if handled then
                return true
            end
            if policy_err then
                return false, policy_err
            end

            local ok, click_err, retryable = click_fetched_target(portal_step, portal_target)
            if not ok then
                if retryable then
                    log.warn(tostring(click_err))
                    set_stage("entry_portal_confirm", IMAGE_CLICK_RETRY_STAGE_DELAY_MS)
                    return true
                end
                return false, click_err
            end

            note_entry_portal_click_attempt(map, portal_step, "entry_portal_confirm_retry")

            state.entry_portal_retry_due_at = now + avepoint_delay_ms("key_stage", KEY_STAGE_DELAY_MS)
            log.info(string.format(
                "Re-clicked portal trigger [%s] while waiting for map entry",
                tostring(portal_step and portal_step.label or "")
            ))
            set_stage("entry_portal_confirm", ROUTE_START_RETRY_POLL_MS)
            return true
        end
    else
        local cur_x, cur_y, cur_z, pos_err = nav.player_pos()
        if cur_x ~= nil and cur_y ~= nil then
            local outside, nearest_outside = is_outside_position(cur_x, cur_y, cur_z)
            if outside then
                state.entry_portal_ready_at = 0
                entry_err = string.format(
                    "Player still outside after portal click distance=%s z_diff=%s",
                    nearest_outside and string.format("%.2f", tonumber(nearest_outside.distance) or 0) or "nil",
                    nearest_outside and nearest_outside.z_diff and string.format("%.2f", tonumber(nearest_outside.z_diff) or 0) or "nil"
                )
            else
                if (state.entry_portal_ready_at or 0) == 0 then
                    state.entry_portal_ready_at = now
                end

                local ready_elapsed = now - (state.entry_portal_ready_at or now)
                if ready_elapsed >= MAP_ROUTE_READY_STABLE_MS then
                    reset_entry_portal_state()
                    set_stage("begin_map_route", 0)
                    return true
                end

                entry_err = string.format(
                    "Map entry detected, stabilizing %d/%dms",
                    ready_elapsed,
                    MAP_ROUTE_READY_STABLE_MS
                )
            end
        else
            state.entry_portal_ready_at = 0
            entry_err = pos_err or "Player position unavailable."
        end
    end

    if elapsed >= ROUTE_START_TIMEOUT_MS then
        local handled, policy_err = maybe_apply_entry_confirm_retry_limit_policy(map)
        if handled then
            return true
        end
        if policy_err then
            return false, policy_err
        end

        return false, string.format(
            "Map entry confirm timeout after %dms: %s",
            elapsed,
            tostring(entry_err or "Map entry not detected.")
        )
    end

    if now - (state.entry_portal_last_warn_at or 0) >= ROUTE_START_WARN_INTERVAL_MS then
        state.entry_portal_last_warn_at = now
        log.warn(string.format(
            "Waiting map entry confirm | elapsed=%dms err=%s",
            elapsed,
            tostring(entry_err or "Map entry not detected.")
        ))
    end

    set_stage("entry_portal_confirm", ROUTE_START_RETRY_POLL_MS)
    return true
end

local function begin_stash_route(stage_name, next_stage)
    state.stash_next_stage = next_stage
    state.stash_retry_started_at = 0
    state.stash_retry_last_warn_at = 0
    return begin_route_with_retry(stage_name, STASH_ROUTE_POINTS, "Stash route", "press_stash_d")
end

local function handle_stage_verify_exit_result(map)
    local now = sys.time()
    if (state.exit_verify_started_at or 0) == 0 then
        state.exit_verify_started_at = now
        state.exit_verify_last_warn_at = 0
    end

    local cur_x, cur_y, cur_z, pos_err = nav.player_pos()
    if cur_x == nil or cur_y == nil then
        if now - (state.exit_verify_last_warn_at or 0) >= PICKUP_WARN_INTERVAL_MS then
            state.exit_verify_last_warn_at = now
            log.warn("Waiting exit result position: " .. tostring(pos_err))
        end
        set_stage("verify_exit_result", EXIT_VERIFY_RETRY_MS)
        return true
    end

    local distance = distance_2d(cur_x, cur_y, map.exit_point.x, map.exit_point.y)
    local outside, nearest_outside = is_outside_position(cur_x, cur_y, cur_z)
    if distance <= EXIT_STILL_IN_MAP_DISTANCE or not outside then
        local retry_source = tostring(state.exit_verify_source or "unknown")
        local retry_stage = retry_source == "chumen" and "begin_exit_route_for_chumen" or "exit_interference_escape"
        local retry_action = retry_source == "chumen" and "reroute_then_image_retry" or "escape_then_image_retry"
        log.warn(string.format(
            "Exit interaction likely failed | source=%s exit_distance=%.2f threshold=%.2f outside=%s nearest_outside_distance=%s nearest_outside_z_diff=%s action=%s",
            retry_source,
            distance,
            EXIT_STILL_IN_MAP_DISTANCE,
            outside and "true" or "false",
            nearest_outside and string.format("%.2f", tonumber(nearest_outside.distance) or 0) or "nil",
            nearest_outside and nearest_outside.z_diff and string.format("%.2f", tonumber(nearest_outside.z_diff) or 0) or "nil",
            retry_action
        ))
        reset_exit_verify_state()
        reset_exit_image_retry()
        set_stage(retry_stage, 0)
        return true
    end

    reset_exit_verify_state()
    reset_exit_image_retry()
    local cleanup_due = note_run_completed_and_check_cleanup_due()
    if maybe_begin_f6_safe_exit("after current run before next D") then
        return true
    end
    local next_stage = cleanup_due and "bag_cleanup_before_reenter" or "press_reenter_d"
    set_stage(next_stage, avepoint_delay_ms("key_stage", KEY_STAGE_DELAY_MS))
    return true
end

local function handle_stage_begin_exit_unstuck_route(map)
    local points, err = build_exit_unstuck_route()
    if not points then
        if sys.time() - (state.exit_verify_last_warn_at or 0) >= PICKUP_WARN_INTERVAL_MS then
            state.exit_verify_last_warn_at = sys.time()
            log.warn("Build exit unstuck route failed: " .. tostring(err))
        end
        set_stage("begin_exit_route", EXIT_VERIFY_RETRY_MS)
        return true
    end

    return begin_route_with_retry("begin_exit_unstuck_route", points, "Exit unstuck route", "begin_exit_route")
end

local function handle_stage_press_reenter_d(map)
    if maybe_begin_f6_safe_exit("before press_reenter_d") then
        return true
    end

    local next_delay = random_entry_flow_delay_ms()
    local ok, err = press_key(tonumber(map.reenter_key_vk) or VK_D, "resume next cycle")
    if not ok then
        return false, err
    end

    mark_f6_first_map_entry("press_reenter_d")
    ok, err = activate_random_map("next_cycle")
    if not ok then
        return false, err
    end

    state.button_index = 1
    reset_button_retry()
    set_stage("entry_buttons", next_delay)
    return true
end

local STAGE_HANDLERS = {
    startup_press_t = function(map)
        local ok, err = press_key(VK_T, "startup recover from in-map state")
        if not ok then
            return false, err
        end

        local wait_ms = avepoint_delay_ms("startup_recover")
        log.info(string.format(
            "Startup recovery wait scheduled | after_T=%dms next=startup_press_d",
            wait_ms
        ))
        set_stage("startup_press_d", wait_ms)
        return true
    end,
    startup_press_d = function(map)
        local ok, err = press_key(VK_D, "startup leave current map")
        if not ok then
            return false, err
        end

        set_stage("startup_begin_outer_route", avepoint_delay_ms("key_stage", KEY_STAGE_DELAY_MS))
        return true
    end,
    startup_begin_outer_route = function(map)
        log.info("Startup recovery finished | restarting outer route")
        return start_outer_route()
    end,
    bag_cleanup_before_entry = function(map)
        queue_bag_cleanup("begin_stash_route_before_entry", 0, "before entry D")
        return true
    end,
    bag_cleanup_before_reenter = function(map)
        reset_cleanup_schedule("after_cleanup")
        queue_bag_cleanup("begin_stash_route_before_reenter", 0, "before reenter D")
        return true
    end,
    bag_cleanup = function(map)
        return update_bag_cleanup()
    end,
    begin_stash_route_before_entry = function(map)
        return begin_stash_route("begin_stash_route_before_entry", "press_entry_d")
    end,
    begin_stash_route_before_reenter = function(map)
        return begin_stash_route("begin_stash_route_before_reenter", "press_reenter_d")
    end,
    press_stash_d = function(map)
        local ok, err = press_key(VK_D, "open stash")
        if not ok then
            return false, err
        end
        set_stage("stash_store_click", random_bag_flow_delay_ms())
        return true
    end,
    stash_store_click = function(map)
        return update_stash_store()
    end,
    stash_store_escape = function(map)
        return update_stash_escape()
    end,
    begin_stash_return = function(map)
        local next_stage = state.stash_next_stage or "press_entry_d"
        return begin_route_with_retry("begin_stash_return", STASH_RETURN_ROUTE_POINTS, "Stash return route", next_stage)
    end,
    press_entry_d = function(map)
        local ok, err = press_key(tonumber(map.entry_key_vk) or VK_D, "after outer route")
        if not ok then
            return false, err
        end

        mark_f6_first_map_entry("press_entry_d")
        state.button_index = 1
        reset_button_retry()
        set_stage("entry_buttons", random_entry_flow_delay_ms())
        return true
    end,
    entry_buttons = function(map)
        return update_entry_buttons()
    end,
    entry_portal_confirm = function(map)
        return avepoint_update_entry_portal_confirm(map)
    end,
    map_revive = function(map)
        return avepoint_update_map_revive()
    end,
    begin_map_route = function(map)
        return begin_route_with_retry("begin_map_route", state.map_points, "Map route", "begin_exit_route")
    end,
    begin_exit_route = function(map)
        if maybe_wait_for_loot_before_exit("begin_exit_route", sys.time(), "starting exit route") then
            return true
        end
        return begin_route_with_retry("begin_exit_route", { map.exit_point }, "Exit route", "press_exit_d")
    end,
    press_exit_d = function(map)
        if maybe_wait_for_loot_before_exit("press_exit_d", sys.time(), "leave map") then
            return true
        end
        disable_map_pickup("Map pickup disabled before exit interaction")
        return update_exit_portal_click()
    end,
    verify_exit_result = handle_stage_verify_exit_result,
    exit_interference_escape = function(map)
        local ok, err = press_escape_key("close wrong portal page before exit retry")
        if not ok then
            return false, err
        end
        reset_exit_image_retry()
        set_stage("exit_chumen_click", EXIT_UNSTUCK_ESCAPE_DELAY_MS)
        return true
    end,
    begin_exit_route_for_chumen = function(map)
        return begin_route_with_retry("begin_exit_route_for_chumen", { map.exit_point }, "Exit route", "exit_chumen_click")
    end,
    exit_chumen_click = function(map)
        return update_exit_chumen_click()
    end,
    begin_exit_unstuck_route = handle_stage_begin_exit_unstuck_route,
    press_reenter_d = handle_stage_press_reenter_d
}

function update_stage()
    local map, _, map_err = active_map_or_err()
    if not map then
        return false, map_err
    end

    local handler = STAGE_HANDLERS[state.stage]
    if type(handler) ~= "function" then
        return false, "Unknown stage: " .. tostring(state.stage)
    end

    return handler(map)
end

function avepoint_format_addr_hex(value)
    local number_value = tonumber(value)
    if number_value ~= nil then
        local integer_value = math.tointeger and math.tointeger(number_value) or nil
        if integer_value == nil and number_value == math.floor(number_value) then
            integer_value = math.floor(number_value)
        end
        if integer_value ~= nil then
            return string.format("0x%X", integer_value)
        end
    end

    return tostring(value or "")
end

function avepoint_hotkey_short_control_name(value)
    local text = tostring(value or "")
    local short = text:match("([^.]+)$")
    if short and short ~= "" then
        return short
    end
    return text
end

function avepoint_hotkey_count_same_button_name(buttons, button_name)
    local count = 0
    for _, item in ipairs(buttons or {}) do
        if tostring(item.name or "") == tostring(button_name or "") then
            count = count + 1
        end
    end
    return count
end

function avepoint_hotkey_distance_tolerance(distance, preset)
    local raw_distance = math.abs(tonumber(distance) or 0)
    local ratio = math.max(0, tonumber(preset and preset.distance_tolerance_ratio) or 0.03)
    local min_tolerance = math.max(0, tonumber(preset and preset.distance_tolerance_min) or 0.5)
    local max_tolerance = math.max(min_tolerance, tonumber(preset and preset.distance_tolerance_max) or 2.5)
    local tolerance = raw_distance * ratio
    if tolerance < min_tolerance then
        tolerance = min_tolerance
    end
    if tolerance > max_tolerance then
        tolerance = max_tolerance
    end
    return tolerance
end

local HOTKEY_NOISY_TEXT_ANCHOR_BUTTON_PATTERNS = {
    "skill_c.widgettree.skillextabitem.widgettree.clickbtn",
    "skill_c.widgettree.skillexviewitem.widgettree.clickbtn",
    "tipskillhanditem_c.widgettree.levelupbtn",
    "talentpointitem_c.widgettree.selectbtn",
    "tabtalentitem_c.widgettree.tiptalentitem.widgettree.activebtn",
    "uicareerpointitem_c.widgettree.selectbtn",
    "tipcareeritem_c.widgettree.activebtn"
}

function avepoint_hotkey_button_identity_text(button_item)
    if type(button_item) ~= "table" then
        return ""
    end

    local name = trim(button_item.name or "")
    if name ~= "" then
        return name:lower()
    end

    local fullname = trim(button_item.Fullname or button_item.fullname or "")
    if fullname ~= "" then
        return fullname:lower()
    end

    return ""
end

function avepoint_hotkey_button_uses_noisy_nearest_text(button_item)
    local identity = avepoint_hotkey_button_identity_text(button_item)
    if identity == "" then
        return false
    end

    for _, pattern in ipairs(HOTKEY_NOISY_TEXT_ANCHOR_BUTTON_PATTERNS) do
        if identity:find(pattern, 1, true) ~= nil then
            return true
        end
    end

    return false
end

function avepoint_hotkey_noisy_anchor_button_label(button_item)
    local identity = avepoint_hotkey_button_identity_text(button_item)
    if identity:find("skill_c.widgettree.skillextabitem.widgettree.clickbtn", 1, true) ~= nil then
        return "技能页标签按钮"
    end
    if identity:find("skill_c.widgettree.skillexviewitem.widgettree.clickbtn", 1, true) ~= nil then
        return "技能列表项按钮"
    end
    if identity:find("tipskillhanditem_c.widgettree.levelupbtn", 1, true) ~= nil then
        return "技能升级按钮"
    end
    if identity:find("talentpointitem_c.widgettree.selectbtn", 1, true) ~= nil then
        return "天赋节点按钮"
    end
    if identity:find("tabtalentitem_c.widgettree.tiptalentitem.widgettree.activebtn", 1, true) ~= nil then
        return "天赋节点激活按钮"
    end
    if identity:find("uicareerpointitem_c.widgettree.selectbtn", 1, true) ~= nil then
        return "天赋大类按钮"
    end
    if identity:find("tipcareeritem_c.widgettree.activebtn", 1, true) ~= nil then
        return "天赋大类激活按钮"
    end

    return ""
end

function avepoint_hotkey_locator_label(button_item, nearest_text_item)
    if avepoint_hotkey_button_uses_noisy_nearest_text(button_item) then
        local neutral_label = avepoint_hotkey_noisy_anchor_button_label(button_item)
        if neutral_label ~= "" then
            return neutral_label
        end
    end

    local nearest_text = trim(nearest_text_item and nearest_text_item.text or "")
    if nearest_text ~= "" then
        return nearest_text .. "按钮"
    end

    local button_text = trim(button_item and button_item.text or "")
    if button_text ~= "" then
        return button_text .. "按钮"
    end

    local short_name = avepoint_hotkey_short_control_name(button_item and button_item.name or "")
    if short_name ~= "" then
        return short_name
    end
    local short_fullname = avepoint_hotkey_short_control_name(button_item and (button_item.Fullname or button_item.fullname) or "")
    if short_fullname ~= "" then
        return short_fullname
    end

    return "Hotkey Probe Button"
end

function avepoint_hotkey_escape_lua_string(value)
    local text = tostring(value or "")
    text = text:gsub("\\", "\\\\")
    text = text:gsub("\"", "\\\"")
    return "\"" .. text .. "\""
end

function avepoint_hotkey_normalize_locator_step(step)
    if type(step) ~= "table" then
        return nil
    end

    local normalized = {}
    for key, value in pairs(step) do
        normalized[key] = value
    end

    if trim(normalized.distance_anchor_exact_text or "") == "" and trim(normalized.anchor_exact_text or "") ~= "" then
        normalized.distance_anchor_exact_text = normalized.anchor_exact_text
    end
    if trim(normalized.distance_button_name or "") == "" and trim(normalized.button_name or "") ~= "" then
        normalized.distance_button_name = normalized.button_name
    end

    return normalized
end

function avepoint_hotkey_format_locator_step(step)
    local normalized = avepoint_hotkey_normalize_locator_step(step)
    if type(normalized) ~= "table" then
        return "{}"
    end

    local fields = {
        "label = " .. avepoint_hotkey_escape_lua_string(normalized.label or "")
    }

    if trim(normalized.distance_anchor_exact_text or "") ~= "" and trim(normalized.distance_button_name or "") ~= "" then
        fields[#fields + 1] = "distance_anchor_exact_text = " .. avepoint_hotkey_escape_lua_string(normalized.distance_anchor_exact_text)
        fields[#fields + 1] = "distance_button_name = " .. avepoint_hotkey_escape_lua_string(normalized.distance_button_name)
        fields[#fields + 1] = string.format("distance_min = %.6f", tonumber(normalized.distance_min) or 0)
        fields[#fields + 1] = string.format("distance_max = %.6f", tonumber(normalized.distance_max) or 0)
    end

    if type(normalized.include_patterns) == "table" and #normalized.include_patterns > 0 then
        local parts = {}
        for _, pattern in ipairs(normalized.include_patterns) do
            parts[#parts + 1] = avepoint_hotkey_escape_lua_string(pattern)
        end
        fields[#fields + 1] = "include_patterns = { " .. table.concat(parts, ", ") .. " }"
    end

    if tonumber(normalized.hint_client_x) ~= nil then
        fields[#fields + 1] = string.format("hint_client_x = %.6f", tonumber(normalized.hint_client_x) or 0)
    end
    if tonumber(normalized.hint_client_y) ~= nil then
        fields[#fields + 1] = string.format("hint_client_y = %.6f", tonumber(normalized.hint_client_y) or 0)
    end
    if tonumber(normalized.hint_ratio_x) ~= nil then
        fields[#fields + 1] = string.format("hint_ratio_x = %.6f", tonumber(normalized.hint_ratio_x) or 0)
    end
    if tonumber(normalized.hint_ratio_y) ~= nil then
        fields[#fields + 1] = string.format("hint_ratio_y = %.6f", tonumber(normalized.hint_ratio_y) or 0)
    end
    if tonumber(normalized.hint_max_distance) ~= nil then
        fields[#fields + 1] = string.format("hint_max_distance = %.3f", tonumber(normalized.hint_max_distance) or 0)
    end

    return "{ " .. table.concat(fields, ", ") .. " }"
end

function avepoint_hotkey_collect_mouse_button_candidates(preset)
    if type(preset) ~= "table" then
        return nil, "Hotkey mouse button preset is not configured."
    end

    local cursor, cursor_err = nav.cursor_client_pos()
    if not cursor then
        return nil, cursor_err or "Unable to read current mouse position."
    end

    local snapshot, err = nav.enum_ui()
    if not snapshot then
        return nil, err
    end

    local buttons = snapshot.buttons or {}
    local texts = snapshot.texts or {}
    local button_name = trim(preset.button_name or "")
    local include_zero_position = preset.include_zero_position == true
    local nearest_text_max_distance = math.max(0, tonumber(preset.nearest_text_max_distance) or 260)
    local cursor_max_distance = math.max(0, tonumber(preset.cursor_max_distance) or 30)
    local limit = math.max(1, math.floor(tonumber(preset.limit) or 8))
    local all_entries = {}
    local preferred_entries = {}
    local skipped_invalid = 0
    local skipped_zero = 0

    for button_index, button_item in ipairs(buttons) do
        local button_x = tonumber(button_item.x)
        local button_y = tonumber(button_item.y)
        if button_name ~= "" and tostring(button_item.name or "") ~= button_name then
            local _ = button_index
        elseif button_x == nil or button_y == nil then
            skipped_invalid = skipped_invalid + 1
        elseif not include_zero_position and button_x == 0 and button_y == 0 then
            skipped_zero = skipped_zero + 1
        else
            local cursor_distance = distance_2d(button_x, button_y, cursor.client_x, cursor.client_y)
            local nearest_text_item = nil
            local nearest_text_index = nil
            local nearest_distance = nil

            for text_index, text_item in ipairs(texts) do
                local text_x = tonumber(text_item.x)
                local text_y = tonumber(text_item.y)
                if text_x ~= nil and text_y ~= nil then
                    local distance = distance_2d(button_x, button_y, text_x, text_y)
                    if nearest_distance == nil or distance < nearest_distance then
                        nearest_distance = distance
                        nearest_text_item = text_item
                        nearest_text_index = text_index
                    end
                end
            end

            local entry = {
                button = button_item,
                button_index = button_index,
                nearest_text_item = nearest_text_item,
                nearest_text_index = nearest_text_index,
                nearest_distance = nearest_distance,
                cursor_distance = cursor_distance
            }

            all_entries[#all_entries + 1] = entry
            if cursor_distance <= cursor_max_distance then
                preferred_entries[#preferred_entries + 1] = entry
            end
        end
    end

    local function entry_less(a, b)
        local ad = tonumber(a.cursor_distance) or 0
        local bd = tonumber(b.cursor_distance) or 0
        if ad ~= bd then
            return ad < bd
        end

        local ay = tonumber(a.button.y) or 0
        local by = tonumber(b.button.y) or 0
        if ay ~= by then
            return ay < by
        end

        return tostring(a.button.addr or "") < tostring(b.button.addr or "")
    end

    table.sort(all_entries, entry_less)
    table.sort(preferred_entries, entry_less)

    return {
        preset = preset,
        cursor = cursor,
        buttons = buttons,
        texts = texts,
        button_name = button_name,
        include_zero_position = include_zero_position,
        nearest_text_max_distance = nearest_text_max_distance,
        cursor_max_distance = cursor_max_distance,
        limit = limit,
        all_entries = all_entries,
        preferred_entries = preferred_entries,
        listed = #preferred_entries > 0 and preferred_entries or all_entries,
        skipped_invalid = skipped_invalid,
        skipped_zero = skipped_zero
    }
end

function avepoint_hotkey_log_button_candidates(prefix, collected)
    local listed = collected.listed or {}
    log.info(string.format(
        "%s start | label=%s texts=%d buttons=%d button_name=%s include_zero_position=%s nearest_text_max_distance=%.3f cursor=(%.2f, %.2f) screen=(%.2f, %.2f) cursor_prefer_distance=%.3f limit=%d",
        tostring(prefix or "Hotkey"),
        tostring(collected.preset and collected.preset.label or ""),
        #(collected.texts or {}),
        #(collected.buttons or {}),
        trim(collected.button_name or "") ~= "" and tostring(collected.button_name) or "<all visible buttons>",
        tostring(collected.include_zero_position == true),
        tonumber(collected.nearest_text_max_distance) or 0,
        tonumber(collected.cursor and collected.cursor.client_x) or 0,
        tonumber(collected.cursor and collected.cursor.client_y) or 0,
        tonumber(collected.cursor and collected.cursor.screen_x) or 0,
        tonumber(collected.cursor and collected.cursor.screen_y) or 0,
        tonumber(collected.cursor_max_distance) or 0,
        tonumber(collected.limit) or 0
    ))

    log.info(string.format(
        "%s summary | preferred=%d total=%d skipped_invalid=%d skipped_zero=%d",
        tostring(prefix or "Hotkey"),
        #(collected.preferred_entries or {}),
        #(collected.all_entries or {}),
        tonumber(collected.skipped_invalid) or 0,
        tonumber(collected.skipped_zero) or 0
    ))

    if #listed == 0 then
        return false
    end

    if #(collected.preferred_entries or {}) == 0 then
        local nearest_entry = collected.all_entries and collected.all_entries[1] or nil
        if nearest_entry and nearest_entry.button then
            log.warn(string.format(
                "%s no button inside preferred cursor distance %.3f; nearest candidate addr=%s distance=%.6f",
                tostring(prefix or "Hotkey"),
                tonumber(collected.cursor_max_distance) or 0,
                avepoint_format_addr_hex(nearest_entry.button.addr),
                tonumber(nearest_entry.cursor_distance) or 0
            ))
        end
    end

    local output_count = math.min(tonumber(collected.limit) or 0, #listed)
    for output_index = 1, output_count do
        local entry = listed[output_index]
        local button = entry.button
        local nearest_text_item = entry.nearest_text_item
        local nearest_text_summary = " nearest_text=<none>"

        if nearest_text_item
            and entry.nearest_distance ~= nil
            and entry.nearest_distance <= (tonumber(collected.nearest_text_max_distance) or 0)
        then
            nearest_text_summary = string.format(
                " nearest_text=%s nearest_text_addr=%s nearest_text_name=%s nearest_text_x=%s nearest_text_y=%s nearest_distance=%.6f nearest_text_index=%d",
                tostring(nearest_text_item.text or ""),
                avepoint_format_addr_hex(nearest_text_item.addr),
                tostring(nearest_text_item.name or ""),
                tostring(nearest_text_item.x or ""),
                tostring(nearest_text_item.y or ""),
                tonumber(entry.nearest_distance) or 0,
                tonumber(entry.nearest_text_index) or 0
            )
        end

        log.info(string.format(
            "%s button[%d/%d] | button_index=%d addr=%s name=%s text=%s fullname=%s x=%s y=%s cursor_distance=%.6f%s",
            tostring(prefix or "Hotkey"),
            output_index,
            #listed,
            tonumber(entry.button_index) or 0,
            avepoint_format_addr_hex(button.addr),
            tostring(button.name or ""),
            tostring(button.text or ""),
            tostring(button.Fullname or button.fullname or ""),
            tostring(button.x or ""),
            tostring(button.y or ""),
            tonumber(entry.cursor_distance) or 0,
            nearest_text_summary
        ))
    end

    if output_count < #listed then
        log.info(string.format(
            "%s button dump truncated | shown=%d total=%d",
            tostring(prefix or "Hotkey"),
            output_count,
            #listed
        ))
    end

    return true
end

local function avepoint_hotkey_collect_snapshot_button_entries(snapshot)
    if type(snapshot) ~= "table" then
        return {
            buttons = {},
            texts = {},
            entries = {},
            task_entries = {},
            invalid_position_count = 0
        }
    end

    local buttons = snapshot.buttons or {}
    local texts = snapshot.texts or {}
    local entries = {}
    local task_entries = {}
    local invalid_position_count = 0

    for button_index, button_item in ipairs(buttons) do
        local button_x = tonumber(button_item.x)
        local button_y = tonumber(button_item.y)
        if button_x == nil or button_y == nil then
            invalid_position_count = invalid_position_count + 1
        else
            local nearest_text_item = nil
            local nearest_text_index = nil
            local nearest_distance = nil
            for text_index, text_item in ipairs(texts) do
                local text_x = tonumber(text_item.x)
                local text_y = tonumber(text_item.y)
                if text_x ~= nil and text_y ~= nil then
                    local distance = distance_2d(button_x, button_y, text_x, text_y)
                    if nearest_distance == nil or distance < nearest_distance then
                        nearest_distance = distance
                        nearest_text_item = text_item
                        nearest_text_index = text_index
                    end
                end
            end

            local identity = tostring(button_item.Fullname or button_item.fullname or button_item.name or "")
            local identity_key = trim(identity):lower()
            local nearest_text_value = trim(nearest_text_item and nearest_text_item.text or "")
            local nearest_text_key = nearest_text_value:lower()
            local is_task_related = identity_key:find("taskitem_c.widgettree", 1, true) ~= nil
                or identity_key:find("fighttasklistview_c.widgettree.btntask", 1, true) ~= nil
                or nearest_text_key:find("主线", 1, true) ~= nil
                or nearest_text_key:find("支线", 1, true) ~= nil
                or nearest_text_key:find("藏宝地", 1, true) ~= nil
                or nearest_text_key:find("涓荤嚎", 1, true) ~= nil
                or nearest_text_key:find("鏀嚎", 1, true) ~= nil

            local entry = {
                button = button_item,
                button_index = button_index,
                nearest_text_item = nearest_text_item,
                nearest_text_index = nearest_text_index,
                nearest_distance = nearest_distance,
                identity = identity,
                is_task_related = is_task_related
            }
            entries[#entries + 1] = entry
            if is_task_related then
                task_entries[#task_entries + 1] = entry
            end
        end
    end

    local function entry_less(a, b)
        local ay = tonumber(a and a.button and a.button.y) or math.huge
        local by = tonumber(b and b.button and b.button.y) or math.huge
        if ay ~= by then
            return ay < by
        end

        local ax = tonumber(a and a.button and a.button.x) or math.huge
        local bx = tonumber(b and b.button and b.button.x) or math.huge
        if ax ~= bx then
            return ax < bx
        end

        return tostring(a and a.button and a.button.addr or "") < tostring(b and b.button and b.button.addr or "")
    end

    table.sort(entries, entry_less)
    table.sort(task_entries, entry_less)

    return {
        buttons = buttons,
        texts = texts,
        entries = entries,
        task_entries = task_entries,
        invalid_position_count = invalid_position_count
    }
end

local function avepoint_hotkey_log_snapshot_buttons(prefix, snapshot)
    local collected = avepoint_hotkey_collect_snapshot_button_entries(snapshot)
    local task_limit = 24
    local all_limit = 40

    log.info(string.format(
        "%s snapshot summary | texts=%d buttons=%d task_related=%d invalid_pos=%d",
        tostring(prefix or "F3"),
        #(collected.texts or {}),
        #(collected.buttons or {}),
        #(collected.task_entries or {}),
        tonumber(collected.invalid_position_count) or 0
    ))

    local function log_entry(label, index, total, entry)
        local button = entry and entry.button or {}
        local nearest_text_item = entry and entry.nearest_text_item or nil
        log.info(string.format(
            "%s[%d/%d] | button_index=%d addr=%s name=%s text=%s fullname=%s x=%s y=%s nearest_text=%s nearest_text_addr=%s nearest_distance=%s nearest_text_index=%s",
            tostring(label or ""),
            tonumber(index) or 0,
            tonumber(total) or 0,
            tonumber(entry and entry.button_index) or 0,
            avepoint_format_addr_hex(button.addr),
            tostring(button.name or ""),
            tostring(button.text or ""),
            tostring(button.Fullname or button.fullname or ""),
            tostring(button.x or ""),
            tostring(button.y or ""),
            tostring(nearest_text_item and nearest_text_item.text or ""),
            avepoint_format_addr_hex(nearest_text_item and nearest_text_item.addr),
            entry and entry.nearest_distance ~= nil and string.format("%.6f", tonumber(entry.nearest_distance) or 0) or "nil",
            tostring(entry and entry.nearest_text_index or "")
        ))
    end

    local task_entries = collected.task_entries or {}
    if #task_entries == 0 then
        log.warn(string.format("%s task-related snapshot buttons: none", tostring(prefix or "F3")))
    else
        local task_output_count = math.min(task_limit, #task_entries)
        for index = 1, task_output_count do
            log_entry(tostring(prefix or "F3") .. " task_button", index, #task_entries, task_entries[index])
        end
        if task_output_count < #task_entries then
            log.info(string.format(
                "%s task-related snapshot truncated | shown=%d total=%d",
                tostring(prefix or "F3"),
                task_output_count,
                #task_entries
            ))
        end
    end

    local entries = collected.entries or {}
    if #entries == 0 then
        log.warn(string.format("%s snapshot buttons: none", tostring(prefix or "F3")))
        return false
    end

    local output_count = math.min(all_limit, #entries)
    for index = 1, output_count do
        log_entry(tostring(prefix or "F3") .. " all_button", index, #entries, entries[index])
    end
    if output_count < #entries then
        log.info(string.format(
            "%s all-button snapshot truncated | shown=%d total=%d",
            tostring(prefix or "F3"),
            output_count,
            #entries
        ))
    end

    return true
end

local function avepoint_hotkey_log_controls_near_point(prefix, label, client_x, client_y, snapshot, opts)
    if type(nav) ~= "table" or type(nav.find_controls_at_point) ~= "function" then
        log.warn(string.format("%s controls near point skipped | label=%s err=nav.find_controls_at_point unavailable", tostring(prefix or "F3"), tostring(label or "")))
        return false
    end

    local max_distance = math.max(1, tonumber(opts and opts.max_distance) or 90)
    local limit = math.max(1, math.floor(tonumber(opts and opts.limit) or 16))
    local controls, err = nav.find_controls_at_point(client_x, client_y, {
        snapshot = snapshot,
        include_buttons = true,
        include_images = true,
        include_texts = true,
        max_distance = max_distance,
        limit = limit
    })

    if type(controls) ~= "table" or #controls == 0 then
        log.warn(string.format(
            "%s controls near point miss | label=%s point=(%.2f,%.2f) max_distance=%.2f err=%s",
            tostring(prefix or "F3"),
            tostring(label or ""),
            tonumber(client_x) or 0,
            tonumber(client_y) or 0,
            tonumber(max_distance) or 0,
            tostring(err or "")
        ))
        return false
    end

    log.info(string.format(
        "%s controls near point | label=%s point=(%.2f,%.2f) max_distance=%.2f count=%d",
        tostring(prefix or "F3"),
        tostring(label or ""),
        tonumber(client_x) or 0,
        tonumber(client_y) or 0,
        tonumber(max_distance) or 0,
        #controls
    ))

    for index, control in ipairs(controls) do
        log.info(string.format(
            "%s control[%d/%d] | label=%s kind=%s addr=%s name=%s text=%s fullname=%s x=%s y=%s distance=%s",
            tostring(prefix or "F3"),
            index,
            #controls,
            tostring(label or ""),
            tostring(control.kind or ""),
            avepoint_format_addr_hex(control.addr),
            tostring(control.name or ""),
            tostring(control.text or ""),
            tostring(control.fullname or ""),
            tostring(control.x or ""),
            tostring(control.y or ""),
            tostring(control.distance or "")
        ))
    end

    return true
end

local function avepoint_hotkey_log_task_control_points(prefix, snapshot)
    if type(snapshot) ~= "table" then
        return false
    end

    local main_task_hint_x = 89.907120
    local main_task_hint_y = 235.181610
    local any_logged = false

    if avepoint_hotkey_log_controls_near_point(
            prefix,
            "main_task_fixed_hint",
            main_task_hint_x,
            main_task_hint_y,
            snapshot,
            { max_distance = 90, limit = 20 }
        ) then
        any_logged = true
    end

    local texts = snapshot.texts or {}
    local mainline_text = nil
    for _, text_item in ipairs(texts) do
        local text_value = trim(text_item and text_item.text or "")
        local text_key = text_value:lower()
        if text_key:find("主线", 1, true) ~= nil or text_key:find("涓荤嚎", 1, true) ~= nil then
            mainline_text = text_item
            break
        end
    end

    if type(mainline_text) == "table" then
        local text_x = tonumber(mainline_text.x)
        local text_y = tonumber(mainline_text.y)
        if text_x ~= nil and text_y ~= nil then
            local inferred_button_x = text_x - 31
            local inferred_button_y = text_y - 4
            if avepoint_hotkey_log_controls_near_point(
                    prefix,
                    "main_task_text_inferred",
                    inferred_button_x,
                    inferred_button_y,
                    snapshot,
                    { max_distance = 90, limit = 20 }
                ) then
                any_logged = true
            end
        end
    else
        log.warn(string.format("%s task control point text probe miss | mainline text not found", tostring(prefix or "F3")))
    end

    return any_logged
end

local function avepoint_hotkey_probe_main_task_button_by_coordinate()
    if type(nav.game_api) ~= "table" then
        return false, "nav.game_api unavailable"
    end
    if type(nav.game_api.EnumCButton) ~= "function" then
        return false, "EnumCButton unavailable"
    end

    local raw_ok, raw_buttons, raw_err = pcall(nav.game_api.EnumCButton)
    if not raw_ok or type(raw_buttons) ~= "table" then
        return false, raw_ok and "EnumCButton returned non-table" or tostring(raw_buttons or raw_err or "")
    end

    local client_w = nil
    local client_h = nil
    local cursor_client_x = nil
    local cursor_client_y = nil
    local hwnd = nil

    if type(nav.cursor_client_pos) == "function" then
        local cursor = select(1, nav.cursor_client_pos({
            allow_outside = true
        }))
        if type(cursor) == "table" then
            client_w = tonumber(cursor.client_w)
            client_h = tonumber(cursor.client_h)
            cursor_client_x = tonumber(cursor.client_x)
            cursor_client_y = tonumber(cursor.client_y)
            hwnd = cursor.hwnd
        end
    end

    log.info(string.format(
        "F1 EnumCButton dump start | total=%d cursor=(%s,%s) client_size=(%s,%s) hwnd=%s source=nav.game_api.EnumCButton",
        #raw_buttons,
        tostring(cursor_client_x or ""),
        tostring(cursor_client_y or ""),
        tostring(client_w or ""),
        tostring(client_h or ""),
        avepoint_format_addr_hex(hwnd)
    ))

    local function format_extra_fields(button)
        if type(button) ~= "table" then
            return "{}"
        end
        local primary = {
            addr = true,
            name = true,
            text = true,
            fullname = true,
            Fullname = true,
            x = true,
            y = true
        }
        local keys = {}
        for key, value in pairs(button) do
            if primary[key] ~= true and (type(value) ~= "table" and type(value) ~= "function") then
                keys[#keys + 1] = key
            end
        end
        table.sort(keys, function(a, b)
            return tostring(a) < tostring(b)
        end)
        local parts = {}
        for index = 1, math.min(#keys, 16) do
            local key = keys[index]
            parts[#parts + 1] = tostring(key) .. "=" .. tostring(button[key])
        end
        if #keys > 16 then
            parts[#parts + 1] = "_keys=" .. tostring(#keys)
        end
        if #parts == 0 then
            return "{}"
        end
        return "{" .. table.concat(parts, " ") .. "}"
    end

    for index, button in ipairs(raw_buttons) do
        log.info(string.format(
            "F1 EnumCButton button[%d/%d] | addr=%s name=%s text=%s fullname=%s x=%s y=%s raw_extra=%s",
            index,
            #raw_buttons,
            avepoint_format_addr_hex(type(button) == "table" and button.addr or nil),
            tostring(type(button) == "table" and button.name or ""),
            tostring(type(button) == "table" and button.text or ""),
            tostring(type(button) == "table" and (button.Fullname or button.fullname) or ""),
            tostring(type(button) == "table" and button.x or ""),
            tostring(type(button) == "table" and button.y or ""),
            format_extra_fields(button)
        ))
    end

    log.info(string.format("F1 EnumCButton dump complete | total=%d", #raw_buttons))

    return true
end

local function avepoint_hotkey_format_raw_extra_fields(item, primary, max_fields)
    if type(item) ~= "table" then
        return "{}"
    end
    local keys = {}
    for key, value in pairs(item) do
        if primary[key] ~= true and type(value) ~= "table" and type(value) ~= "function" then
            keys[#keys + 1] = key
        end
    end
    table.sort(keys, function(a, b)
        return tostring(a) < tostring(b)
    end)

    local parts = {}
    local limit = math.max(1, tonumber(max_fields) or 16)
    for index = 1, math.min(#keys, limit) do
        local key = keys[index]
        parts[#parts + 1] = tostring(key) .. "=" .. tostring(item[key])
    end
    if #keys > limit then
        parts[#parts + 1] = "_keys=" .. tostring(#keys)
    end
    if #parts == 0 then
        return "{}"
    end
    return "{" .. table.concat(parts, " ") .. "}"
end

local function avepoint_hotkey_extract_entity_position(item)
    if type(nav) == "table" and type(nav.extract_position) == "function" then
        local x, y, z = nav.extract_position(item)
        if x ~= nil and y ~= nil then
            return x, y, z
        end
    end
    if type(item) ~= "table" then
        return nil, nil, nil
    end

    local function pick(tbl, keys)
        if type(tbl) ~= "table" then
            return nil
        end
        for _, key in ipairs(keys) do
            local value = tonumber(tbl[key])
            if value ~= nil then
                return value
            end
        end
        return nil
    end

    local x = pick(item, { "x", "X", "posX", "PosX", "worldX", "WorldX" })
    local y = pick(item, { "y", "Y", "posY", "PosY", "worldY", "WorldY" })
    local z = pick(item, { "z", "Z", "posZ", "PosZ", "worldZ", "WorldZ" })
    if x ~= nil and y ~= nil then
        return x, y, z
    end

    for _, key in ipairs({ "pos", "position", "coord", "coords", "point", "location", "Location" }) do
        local nested = item[key]
        if type(nested) == "table" then
            x = pick(nested, { "x", "X", "posX", "PosX", "worldX", "WorldX" })
            y = pick(nested, { "y", "Y", "posY", "PosY", "worldY", "WorldY" })
            z = pick(nested, { "z", "Z", "posZ", "PosZ", "worldZ", "WorldZ" })
            if x ~= nil and y ~= nil then
                return x, y, z
            end
        end
    end

    return nil, nil, nil
end

local function avepoint_hotkey_entity_label(item)
    if type(item) ~= "table" then
        return ""
    end
    for _, key in ipairs({
        "name", "Name", "text", "Text", "fullname", "Fullname",
        "displayName", "DisplayName", "title", "Title", "label", "Label"
    }) do
        local value = trim(item[key])
        if value ~= "" then
            return value
        end
    end
    return ""
end

local function avepoint_hotkey_dump_nearby_npcs()
    if type(nav.game_api) ~= "table" then
        return false, "nav.game_api unavailable"
    end
    if type(nav.game_api.EnumNPC) ~= "function" then
        return false, "EnumNPC unavailable"
    end

    local raw_ok, raw_npcs, raw_err = pcall(nav.game_api.EnumNPC)
    if not raw_ok or type(raw_npcs) ~= "table" then
        return false, raw_ok and "EnumNPC returned non-table" or tostring(raw_npcs or raw_err or "")
    end

    local player_x = nil
    local player_y = nil
    local player_z = nil
    local pos_err = nil
    if type(nav.player_pos) == "function" then
        player_x, player_y, player_z, pos_err = nav.player_pos()
    end

    log.info(string.format(
        "F5 EnumNPC dump start | total=%d player_pos=(%s,%s,%s) pos_err=%s source=nav.game_api.EnumNPC",
        #raw_npcs,
        tostring(player_x or ""),
        tostring(player_y or ""),
        tostring(player_z or ""),
        tostring(pos_err or "")
    ))

    local primary = {
        addr = true,
        classname = true,
        className = true,
        ClassName = true,
        entityId = true,
        entityID = true,
        id = true,
        name = true,
        Name = true,
        text = true,
        Text = true,
        label = true,
        Label = true,
        fullname = true,
        Fullname = true,
        x = true,
        y = true,
        z = true,
        X = true,
        Y = true,
        Z = true
    }

    for index, npc in ipairs(raw_npcs) do
        local x, y, z = avepoint_hotkey_extract_entity_position(npc)
        local player_distance = ""
        if type(player_x) == "number" and type(player_y) == "number" and x ~= nil and y ~= nil then
            player_distance = string.format("%.2f", distance_2d(player_x, player_y, x, y))
        end
        log.info(string.format(
            "F5 EnumNPC npc[%d/%d] | addr=%s classname=%s entityId=%s label=%s x=%s y=%s z=%s player_distance=%s raw_extra=%s",
            index,
            #raw_npcs,
            avepoint_format_addr_hex(type(npc) == "table" and npc.addr or nil),
            tostring(type(npc) == "table" and (npc.classname or npc.className or npc.ClassName) or ""),
            tostring(type(npc) == "table" and (npc.entityId or npc.entityID or npc.id) or ""),
            avepoint_hotkey_entity_label(npc),
            tostring(x or ""),
            tostring(y or ""),
            tostring(z or ""),
            player_distance,
            avepoint_hotkey_format_raw_extra_fields(npc, primary, 16)
        ))
    end

    log.info(string.format("F5 EnumNPC dump complete | total=%d", #raw_npcs))
    return true
end

local function avepoint_hotkey_dump_visible_text_controls(hotkey_label)
    hotkey_label = tostring(hotkey_label or "F2")
    if type(nav.game_api) ~= "table" then
        return false, "nav.game_api unavailable"
    end
    if type(nav.game_api.EnumCText) ~= "function" then
        return false, "EnumCText unavailable"
    end

    local raw_ok, raw_texts, raw_err = pcall(nav.game_api.EnumCText)
    if not raw_ok or type(raw_texts) ~= "table" then
        return false, raw_ok and "EnumCText returned non-table" or tostring(raw_texts or raw_err or "")
    end

    local client_w = nil
    local client_h = nil
    local cursor_client_x = nil
    local cursor_client_y = nil
    local hwnd = nil

    if type(nav.cursor_client_pos) == "function" then
        local cursor = select(1, nav.cursor_client_pos({
            allow_outside = true
        }))
        if type(cursor) == "table" then
            client_w = tonumber(cursor.client_w)
            client_h = tonumber(cursor.client_h)
            cursor_client_x = tonumber(cursor.client_x)
            cursor_client_y = tonumber(cursor.client_y)
            hwnd = cursor.hwnd
        end
    end

    local function format_dump_text(value)
        local text = tostring(value or "")
        text = text:gsub("\\", "\\\\")
        text = text:gsub("\r", "\\r")
        text = text:gsub("\n", "\\n")
        text = text:gsub("\t", "\\t")
        return text
    end

    local function format_extra_fields(item)
        if type(item) ~= "table" then
            return "{}"
        end
        local primary = {
            addr = true,
            name = true,
            text = true,
            fullname = true,
            Fullname = true,
            x = true,
            y = true
        }
        local keys = {}
        for key, value in pairs(item) do
            if primary[key] ~= true and (type(value) ~= "table" and type(value) ~= "function") then
                keys[#keys + 1] = key
            end
        end
        table.sort(keys, function(a, b)
            return tostring(a) < tostring(b)
        end)
        local parts = {}
        for index = 1, math.min(#keys, 16) do
            local key = keys[index]
            parts[#parts + 1] = tostring(key) .. "=" .. format_dump_text(item[key])
        end
        if #keys > 16 then
            parts[#parts + 1] = "_keys=" .. tostring(#keys)
        end
        if #parts == 0 then
            return "{}"
        end
        return "{" .. table.concat(parts, " ") .. "}"
    end

    log.info(string.format(
        "%s EnumCText dump start | total=%d cursor=(%s,%s) client_size=(%s,%s) hwnd=%s source=nav.game_api.EnumCText",
        hotkey_label,
        #raw_texts,
        tostring(cursor_client_x or ""),
        tostring(cursor_client_y or ""),
        tostring(client_w or ""),
        tostring(client_h or ""),
        avepoint_format_addr_hex(hwnd)
    ))

    for index, item in ipairs(raw_texts) do
        log.info(string.format(
            "%s EnumCText text[%d/%d] | addr=%s name=%s text=%s fullname=%s x=%s y=%s raw_extra=%s",
            hotkey_label,
            index,
            #raw_texts,
            avepoint_format_addr_hex(type(item) == "table" and item.addr or nil),
            format_dump_text(type(item) == "table" and item.name or ""),
            format_dump_text(type(item) == "table" and item.text or ""),
            format_dump_text(type(item) == "table" and (item.Fullname or item.fullname) or ""),
            tostring(type(item) == "table" and item.x or ""),
            tostring(type(item) == "table" and item.y or ""),
            format_extra_fields(item)
        ))
    end

    log.info(string.format("%s EnumCText dump complete | total=%d", hotkey_label, #raw_texts))

    return true
end

local function avepoint_hotkey_dump_visible_image_controls(hotkey_label)
    hotkey_label = tostring(hotkey_label or "F3")
    if type(nav.game_api) ~= "table" then
        return false, "nav.game_api unavailable"
    end
    if type(nav.game_api.EnumCImage) ~= "function" then
        return false, "EnumCImage unavailable"
    end

    local raw_ok, raw_images, raw_err = pcall(nav.game_api.EnumCImage)
    if not raw_ok or type(raw_images) ~= "table" then
        return false, raw_ok and "EnumCImage returned non-table" or tostring(raw_images or raw_err or "")
    end

    local client_w = nil
    local client_h = nil
    local cursor_client_x = nil
    local cursor_client_y = nil
    local hwnd = nil

    if type(nav.cursor_client_pos) == "function" then
        local cursor = select(1, nav.cursor_client_pos({
            allow_outside = true
        }))
        if type(cursor) == "table" then
            client_w = tonumber(cursor.client_w)
            client_h = tonumber(cursor.client_h)
            cursor_client_x = tonumber(cursor.client_x)
            cursor_client_y = tonumber(cursor.client_y)
            hwnd = cursor.hwnd
        end
    end

    local function format_dump_value(value)
        local text = tostring(value or "")
        text = text:gsub("\\", "\\\\")
        text = text:gsub("\r", "\\r")
        text = text:gsub("\n", "\\n")
        text = text:gsub("\t", "\\t")
        return text
    end

    local function first_field(item, ...)
        if type(item) ~= "table" then
            return ""
        end
        for index = 1, select("#", ...) do
            local key = select(index, ...)
            local value = item[key]
            if value ~= nil then
                return value
            end
        end
        return ""
    end

    local function format_extra_fields(item)
        if type(item) ~= "table" then
            return "{}"
        end
        local primary = {
            addr = true,
            name = true,
            text = true,
            fullname = true,
            Fullname = true,
            x = true,
            y = true,
            w = true,
            h = true,
            width = true,
            height = true,
            Width = true,
            Height = true
        }
        local keys = {}
        for key, value in pairs(item) do
            if primary[key] ~= true and type(value) ~= "function" then
                keys[#keys + 1] = key
            end
        end
        table.sort(keys, function(a, b)
            return tostring(a) < tostring(b)
        end)
        local parts = {}
        for index = 1, math.min(#keys, 24) do
            local key = keys[index]
            local value = item[key]
            if type(value) == "table" then
                value = "<table>"
            end
            parts[#parts + 1] = tostring(key) .. "=" .. format_dump_value(value)
        end
        if #keys > 24 then
            parts[#parts + 1] = "_keys=" .. tostring(#keys)
        end
        if #parts == 0 then
            return "{}"
        end
        return "{" .. table.concat(parts, " ") .. "}"
    end

    log.info(string.format(
        "%s EnumCImage dump start | total=%d cursor=(%s,%s) client_size=(%s,%s) hwnd=%s source=nav.game_api.EnumCImage",
        hotkey_label,
        #raw_images,
        tostring(cursor_client_x or ""),
        tostring(cursor_client_y or ""),
        tostring(client_w or ""),
        tostring(client_h or ""),
        avepoint_format_addr_hex(hwnd)
    ))

    for index, item in ipairs(raw_images) do
        log.info(string.format(
            "%s EnumCImage image[%d/%d] | addr=%s name=%s text=%s fullname=%s x=%s y=%s w=%s h=%s raw_extra=%s",
            hotkey_label,
            index,
            #raw_images,
            avepoint_format_addr_hex(type(item) == "table" and item.addr or nil),
            format_dump_value(type(item) == "table" and item.name or ""),
            format_dump_value(type(item) == "table" and item.text or ""),
            format_dump_value(type(item) == "table" and (item.Fullname or item.fullname) or ""),
            tostring(type(item) == "table" and item.x or ""),
            tostring(type(item) == "table" and item.y or ""),
            tostring(first_field(item, "w", "width", "Width")),
            tostring(first_field(item, "h", "height", "Height")),
            format_extra_fields(item)
        ))
    end

    log.info(string.format("%s EnumCImage dump complete | total=%d", hotkey_label, #raw_images))

    return true
end

local function avepoint_hotkey_click_skill_add_panel_button()
    local step = {
        label = "技能加点入口按钮",
        include_patterns = {
            "UIButton Transient.GameEngine.CoreGameInstance.FastEntranceView_C.WidgetTree.HomePointItem.WidgetTree.AddPanelBtn"
        },
        hint_client_x = 1267.297729,
        hint_client_y = 52.706509,
        hint_ratio_x = 0.880679,
        hint_ratio_y = 0.058563,
        hint_max_distance = 120
    }

    local target, err = fetch_button_for_step(step)
    if not target then
        return false, err
    end

    local ok, click_err = click_fetched_target(step, target)
    if not ok then
        return false, click_err
    end

    return true, target
end

function avepoint_hotkey_build_cursor_probe_step(entry, cursor, buttons, preset)
    if type(entry) ~= "table" or type(entry.button) ~= "table" then
        return nil, "Invalid cursor probe entry."
    end

    local button = entry.button
    local nearest_text_item = entry.nearest_text_item
    local nearest_text_value = trim(nearest_text_item and nearest_text_item.text or "")
    local use_text_anchor = not avepoint_hotkey_button_uses_noisy_nearest_text(button)
    local same_name_count = avepoint_hotkey_count_same_button_name(buttons, button.name)
    local label = avepoint_hotkey_locator_label(button, nearest_text_item)
    local step = {
        label = label,
        include_patterns = {
            tostring(button.name or "")
        },
        hint_client_x = tonumber(button.x) or nil,
        hint_client_y = tonumber(button.y) or nil,
        hint_max_distance = math.max(0, tonumber(preset and preset.hint_max_distance) or 80)
    }

    if type(cursor) == "table" then
        if type(cursor.client_w) == "number" and cursor.client_w > 0 and tonumber(button.x) ~= nil then
            step.hint_ratio_x = tonumber(button.x) / cursor.client_w
        end
        if type(cursor.client_h) == "number" and cursor.client_h > 0 and tonumber(button.y) ~= nil then
            step.hint_ratio_y = tonumber(button.y) / cursor.client_h
        end
    end

    if use_text_anchor
        and nearest_text_value ~= ""
        and tonumber(entry.nearest_distance) ~= nil
        and tonumber(entry.nearest_distance) <= math.max(0, tonumber(preset and preset.nearest_text_max_distance) or 260)
    then
        local tolerance = avepoint_hotkey_distance_tolerance(entry.nearest_distance, preset)
        local raw_distance = tonumber(entry.nearest_distance) or 0
        step.distance_anchor_exact_text = nearest_text_value
        step.distance_button_name = tostring(button.name or "")
        step.distance_min = math.max(0, raw_distance - tolerance)
        step.distance_max = raw_distance + tolerance
    end

    return step, {
        label = label,
        same_name_count = same_name_count,
        cursor_distance = tonumber(entry.cursor_distance) or 0,
        nearest_text = nearest_text_value,
        nearest_distance = tonumber(entry.nearest_distance) or nil,
        nearest_text_ignored = not use_text_anchor
    }
end

function avepoint_hotkey_build_selected_probe_step(preset)
    if type(nav) ~= "table" or type(nav.get_current_selected_button) ~= "function" then
        return nil, "GetCurrentSelected is unavailable."
    end

    local selected, selected_err = nav.get_current_selected_button()
    if type(selected) ~= "table" then
        return nil, selected_err or "Current selected button not found."
    end

    local selected_x = tonumber(selected.x)
    local selected_y = tonumber(selected.y)
    if selected_x == nil or selected_y == nil then
        return nil, "GetCurrentSelected returned selected button without valid coordinates."
    end

    local identity = trim(selected.name or "")
    local fullname = trim(selected.Fullname or selected.fullname or "")
    if identity == "" then
        identity = fullname
    end
    if identity == "" then
        return nil, "GetCurrentSelected returned selected button without name/fullname."
    end

    local cursor = nil
    if type(nav.cursor_client_pos) == "function" then
        cursor = select(1, nav.cursor_client_pos())
    end

    local snapshot = nil
    if type(nav.enum_ui) == "function" then
        snapshot = select(1, nav.enum_ui())
    end

    local nearest_text_item = nil
    local nearest_distance = nil
    local nearest_text_index = nil
    local nearest_text_max_distance = math.max(0, tonumber(preset and preset.nearest_text_max_distance) or 260)
    if type(snapshot) == "table" then
        for text_index, text_item in ipairs(snapshot.texts or {}) do
            local text_x = tonumber(text_item.x)
            local text_y = tonumber(text_item.y)
            if text_x ~= nil and text_y ~= nil then
                local dist = distance_2d(selected_x, selected_y, text_x, text_y)
                if nearest_distance == nil or dist < nearest_distance then
                    nearest_distance = dist
                    nearest_text_item = text_item
                    nearest_text_index = text_index
                end
            end
        end
    end

    local selected_button_for_locator = {
        name = selected.name,
        text = selected.text,
        Fullname = selected.Fullname or selected.fullname
    }
    local use_text_anchor = not avepoint_hotkey_button_uses_noisy_nearest_text(selected_button_for_locator)
    local label = avepoint_hotkey_locator_label(selected_button_for_locator, nearest_text_item)
    local step = {
        label = label,
        include_patterns = {
            identity
        },
        hint_client_x = selected_x,
        hint_client_y = selected_y,
        hint_max_distance = math.max(0, tonumber(preset and preset.hint_max_distance) or 80)
    }

    if type(cursor) == "table" then
        if type(cursor.client_w) == "number" and cursor.client_w > 0 then
            step.hint_ratio_x = selected_x / cursor.client_w
        end
        if type(cursor.client_h) == "number" and cursor.client_h > 0 then
            step.hint_ratio_y = selected_y / cursor.client_h
        end
    end

    local nearest_text_value = trim(nearest_text_item and nearest_text_item.text or "")
    if use_text_anchor
        and trim(identity) ~= ""
        and nearest_text_value ~= ""
        and nearest_distance ~= nil
        and nearest_distance <= nearest_text_max_distance
    then
        local tolerance = avepoint_hotkey_distance_tolerance(nearest_distance, preset)
        local nearest_text_x = tonumber(nearest_text_item and nearest_text_item.x)
        local nearest_text_y = tonumber(nearest_text_item and nearest_text_item.y)
        step.distance_anchor_exact_text = nearest_text_value
        step.distance_button_name = tostring(
            trim(selected.name or "") ~= "" and selected.name or identity
        )
        step.distance_min = math.max(0, nearest_distance - tolerance)
        step.distance_max = nearest_distance + tolerance
        if nearest_text_x ~= nil and nearest_text_y ~= nil then
            step.related_text = nearest_text_value
            step.related_dx = nearest_text_x - selected_x
            step.related_dy = nearest_text_y - selected_y
            step.related_tolerance = math.max(18, tolerance * 10)
        end
    end

    return step, {
        label = label,
        selected = selected,
        cursor = cursor,
        snapshot = snapshot,
        identity = identity,
        nearest_text = nearest_text_value,
        nearest_distance = nearest_distance,
        nearest_text_item = nearest_text_item,
        nearest_text_index = nearest_text_index,
        nearest_text_ignored = not use_text_anchor,
        cursor_distance = type(cursor) == "table"
            and tonumber(cursor.client_x) ~= nil
            and tonumber(cursor.client_y) ~= nil
            and distance_2d(selected_x, selected_y, cursor.client_x, cursor.client_y)
            or nil
    }
end

function avepoint_hotkey_find_button_by_text_distance(preset)
    if type(preset) ~= "table" then
        return nil, "Distance click preset is not configured."
    end

    local snapshot, err = nav.enum_ui()
    if not snapshot then
        return nil, err
    end

    local anchor_text = normalize_exact_text(preset.anchor_exact_text or "")
    local button_name = tostring(preset.button_name or "")
    local distance_min = tonumber(preset.distance_min) or 0
    local distance_max = tonumber(preset.distance_max) or distance_min
    if distance_max < distance_min then
        distance_min, distance_max = distance_max, distance_min
    end

    local target_distance = (distance_min + distance_max) * 0.5
    local texts = snapshot.texts or {}
    local buttons = snapshot.buttons or {}
    local anchors = {}

    for index, item in ipairs(texts) do
        if normalize_exact_text(item.text or "") == anchor_text then
            anchors[#anchors + 1] = {
                index = index,
                item = item
            }
        end
    end

    log.info(string.format(
        "F12 target search | label=%s texts=%d buttons=%d anchor_text=%s button_name=%s distance_range=(%.3f, %.3f)",
        tostring(preset.label or ""),
        #texts,
        #buttons,
        tostring(preset.anchor_exact_text or ""),
        button_name,
        distance_min,
        distance_max
    ))

    if #anchors == 0 then
        return nil, "F12 anchor text not found: " .. tostring(preset.anchor_exact_text or "")
    end

    local best = nil
    local nearest = nil

    for _, anchor in ipairs(anchors) do
        local text_x = tonumber(anchor.item.x)
        local text_y = tonumber(anchor.item.y)
        if text_x ~= nil and text_y ~= nil then
            for button_index, btn in ipairs(buttons) do
                if tostring(btn.name or "") == button_name then
                    local button_x = tonumber(btn.x)
                    local button_y = tonumber(btn.y)
                    if button_x ~= nil and button_y ~= nil then
                        local distance = distance_2d(button_x, button_y, text_x, text_y)
                        local delta = math.abs(distance - target_distance)
                        local in_range = distance >= distance_min and distance <= distance_max
                        local candidate = {
                            button = btn,
                            anchor = anchor.item,
                            button_index = button_index,
                            text_index = anchor.index,
                            distance = distance,
                            delta = delta
                        }

                        if not nearest or delta < nearest.delta then
                            nearest = candidate
                        end

                        if in_range and (not best or delta < best.delta) then
                            best = candidate
                        end
                    end
                end
            end
        end
    end

    if not best then
        if nearest then
            return nil, string.format(
                "F12 target button not found. nearest button_addr=%s text_addr=%s distance=%.6f delta=%.6f",
                avepoint_format_addr_hex(nearest.button.addr),
                avepoint_format_addr_hex(nearest.anchor.addr),
                tonumber(nearest.distance) or 0,
                tonumber(nearest.delta) or 0
            )
        end
        return nil, "F12 target button not found."
    end

    return {
        button = best.button,
        anchor = best.anchor,
        button_index = best.button_index,
        text_index = best.text_index,
        raw_distance = tonumber(best.distance) or 0,
        delta = tonumber(best.delta) or 0
    }
end

function avepoint_hotkey_preview_target()
    local preset = HOTKEY_DISTANCE_PREVIEW_PRESETS and HOTKEY_DISTANCE_PREVIEW_PRESETS.current or nil
    if type(preset) ~= "table" then
        return false, "F4 preview preset is not configured."
    end

    local has_distance_preset = trim(preset.anchor_exact_text or "") ~= ""
        and trim(preset.button_name or "") ~= ""

    if has_distance_preset then
        local result, err = avepoint_hotkey_find_button_by_text_distance(preset)
        if result then
            return true, {
                preview_mode = "text_distance",
                kind = "button",
                addr = result.button.addr,
                name = result.button.name,
                text = result.button.text,
                fullname = result.button.Fullname or result.button.fullname,
                x = result.button.x,
                y = result.button.y,
                distance = result.raw_distance,
                delta = result.delta,
                anchor_addr = result.anchor.addr,
                anchor_x = result.anchor.x,
                anchor_y = result.anchor.y,
                anchor_text = result.anchor.text,
                text_index = result.text_index,
                button_index = result.button_index
            }
        end

        local image_preset = resolve_image_click_preset(preset)
        if not step_has_non_distance_button_lookup(preset, image_preset) then
            return false, err
        end

        log.warn(string.format(
            "F4 distance-first preview miss [%s]; fallback to generic match: %s",
            tostring(preset.label or ""),
            tostring(err)
        ))

        local fallback_preset = {}
        for key, value in pairs(preset) do
            if key ~= "anchor_exact_text"
                and key ~= "button_name"
                and key ~= "distance_min"
                and key ~= "distance_max"
            then
                fallback_preset[key] = value
            end
        end
        preset = fallback_preset
    end

    local target, err = fetch_button_for_step(preset)
    if not target then
        return false, err
    end

    return true, {
        preview_mode = "button_match",
        kind = target.kind,
        addr = target.addr,
        name = target.name,
        text = target.text,
        fullname = target.fullname,
        x = target.x,
        y = target.y,
        related_text = target.related_text,
        related_name = target.related_name,
        related_distance = target.related_text_distance,
        hint_distance = target.hint_distance,
        hint_x = target.hint_x,
        hint_y = target.hint_y
    }
end

function avepoint_hotkey_click_target()
    local raw_step = HOTKEY_DISTANCE_CLICK_PRESETS and HOTKEY_DISTANCE_CLICK_PRESETS.current or nil
    local step = avepoint_hotkey_normalize_locator_step(raw_step)
    if type(step) ~= "table" then
        return false, "F12 click preset is not configured."
    end

    local target, err = fetch_button_for_step(step)
    if not target then
        return false, err
    end

    local ok, click_err = click_fetched_target(step, target)
    if not ok then
        return false, click_err
    end

    return true, {
        kind = target.kind,
        addr = target.addr,
        name = target.name,
        text = target.text,
        fullname = target.fullname,
        x = target.x,
        y = target.y,
        related_text = target.related_text,
        related_name = target.related_name,
        related_distance = target.related_text_distance,
        hint_distance = target.hint_distance,
        hint_x = target.hint_x,
        hint_y = target.hint_y,
        step_snippet = avepoint_hotkey_format_locator_step(step)
    }
end

function avepoint_hotkey_dump_visible_buttons()
    local preset = HOTKEY_BUTTON_ENUM_PRESETS and HOTKEY_BUTTON_ENUM_PRESETS.current or nil
    local collected, err = avepoint_hotkey_collect_mouse_button_candidates(preset)
    if not collected then
        return false, err
    end
    local nearby_ok = avepoint_hotkey_log_button_candidates("F3", collected)
    local snapshot_ok = avepoint_hotkey_log_snapshot_buttons("F3", {
        buttons = collected.buttons,
        texts = collected.texts
    })
    local control_points_ok = avepoint_hotkey_log_task_control_points("F3", {
        buttons = collected.buttons,
        texts = collected.texts
    })
    if not nearby_ok and not snapshot_ok and not control_points_ok then
        return false, "No visible buttons with valid positions."
    end
    return true
end

local function avepoint_hotkey_resolve_cursor_probe_target(options)
    local opts = type(options) == "table" and options or {}
    local hotkey_label = tostring(opts.hotkey_label or "F11")
    local perform_click = opts.perform_click ~= false
    local preset = HOTKEY_CURSOR_CLICK_PRESETS and HOTKEY_CURSOR_CLICK_PRESETS.current or nil

    local selected_step, selected_meta = avepoint_hotkey_build_selected_probe_step(preset)
    if selected_step and type(selected_meta) == "table" and type(selected_meta.selected) == "table" then
        if tostring(selected_meta.identity or ""):lower():find("taskitem_c.widgettree.taskbtn", 1, true) ~= nil then
            _G.AVEPOINT_MAIN_TASK_BUTTON_LOCATOR = {
                fullname = tostring(selected_meta.identity or ""),
                include_patterns = type(selected_step.include_patterns) == "table"
                    and selected_step.include_patterns
                    or { "taskitem_c.widgettree.taskbtn" },
                hint_client_x = tonumber(selected_step.hint_client_x),
                hint_client_y = tonumber(selected_step.hint_client_y),
                hint_ratio_x = tonumber(selected_step.hint_ratio_x),
                hint_ratio_y = tonumber(selected_step.hint_ratio_y),
                hint_max_distance = tonumber(selected_step.hint_max_distance),
                distance_anchor_exact_text = tostring(selected_step.distance_anchor_exact_text or ""),
                distance_button_name = tostring(selected_step.distance_button_name or ""),
                distance_min = tonumber(selected_step.distance_min),
                distance_max = tonumber(selected_step.distance_max),
                related_text = tostring(selected_step.related_text or selected_meta.nearest_text or ""),
                related_dx = tonumber(selected_step.related_dx),
                related_dy = tonumber(selected_step.related_dy),
                related_tolerance = tonumber(selected_step.related_tolerance),
                x = tonumber(selected_meta.selected.x),
                y = tonumber(selected_meta.selected.y),
                captured_addr = selected_meta.selected.addr,
                source = hotkey_label .. " GetCurrentSelected locator",
                cached_at = type(sys) == "table" and type(sys.time) == "function" and sys.time() or nil
            }
        end

        log.info(string.format(
            "%s GetCurrentSelected target | label=%s addr=%s identity=%s pos=(%.2f,%.2f) cursor_distance=%s nearest_text=%s nearest_distance=%s nearest_text_ignored=%s",
            hotkey_label,
            tostring(selected_meta.label or ""),
            avepoint_format_addr_hex(selected_meta.selected.addr),
            tostring(selected_meta.identity or ""),
            tonumber(selected_meta.selected.x) or 0,
            tonumber(selected_meta.selected.y) or 0,
            selected_meta.cursor_distance ~= nil and string.format("%.6f", tonumber(selected_meta.cursor_distance) or 0) or "nil",
            tostring(selected_meta.nearest_text or ""),
            selected_meta.nearest_distance ~= nil and string.format("%.6f", tonumber(selected_meta.nearest_distance) or 0) or "nil",
            selected_meta.nearest_text_ignored == true and "true" or "false"
        ))

        local target, fetch_err = fetch_button_for_step(selected_step)
        local target_source = "locator"
        if not target then
            log.warn(string.format(
                "%s GetCurrentSelected locator fetch miss; using selected addr directly: %s",
                hotkey_label,
                tostring(fetch_err)
            ))
            target_source = "GetCurrentSelected"
            target = {
                kind = "button",
                addr = selected_meta.selected.addr,
                name = selected_meta.selected.name,
                text = selected_meta.selected.text,
                fullname = selected_meta.selected.Fullname or selected_meta.selected.fullname,
                x = selected_meta.selected.x,
                y = selected_meta.selected.y,
                item = selected_meta.selected,
                related_text = selected_meta.nearest_text,
                related_name = selected_meta.nearest_text_item and selected_meta.nearest_text_item.name or nil,
                related_fullname = selected_meta.nearest_text_item and (
                    selected_meta.nearest_text_item.Fullname or selected_meta.nearest_text_item.fullname
                ) or nil,
                related_text_distance = selected_meta.nearest_distance,
                related_text_x = selected_meta.nearest_text_item and selected_meta.nearest_text_item.x or nil,
                related_text_y = selected_meta.nearest_text_item and selected_meta.nearest_text_item.y or nil
            }
        end

        if perform_click then
            local ok, click_err = click_fetched_target(selected_step, target)
            if not ok then
                return false, click_err
            end
        end

        return true, {
            kind = target.kind,
            addr = target.addr,
            name = target.name,
            text = target.text,
            fullname = target.fullname,
            x = target.x,
            y = target.y,
            related_text = target.related_text,
            related_name = target.related_name,
            related_distance = target.related_text_distance,
            hint_distance = target.hint_distance,
            hint_x = target.hint_x,
            hint_y = target.hint_y,
            source = target_source,
            step_snippet = avepoint_hotkey_format_locator_step(selected_step)
        }
    elseif selected_meta ~= nil then
        log.warn(string.format(
            "%s GetCurrentSelected target unavailable; fallback to cursor-nearby scan: %s",
            hotkey_label,
            tostring(selected_meta)
        ))
    end

    local collected, err = avepoint_hotkey_collect_mouse_button_candidates(preset)
    if not collected then
        return false, err
    end

    if not avepoint_hotkey_log_button_candidates(hotkey_label, collected) then
        return false, "No visible buttons with valid positions."
    end

    local listed = collected.listed or {}
    if #listed <= 0 then
        return false, string.format(
            "%s has no query result to %s.",
            hotkey_label,
            perform_click and "click" or "inspect"
        )
    end

    if #listed > 1 then
        log.warn(string.format(
            "%s has %d query results; using the first candidate.",
            hotkey_label,
            #listed
        ))
    end

    local step, meta = avepoint_hotkey_build_cursor_probe_step(
        listed[1],
        collected.cursor,
        collected.buttons,
        preset
    )
    if not step then
        return false, meta or string.format("Unable to build %s transient step.", hotkey_label)
    end

    log.info(string.format(
        "%s transient target | label=%s cursor_distance=%.6f same_name_visible=%d nearest_text=%s nearest_distance=%s nearest_text_ignored=%s",
        hotkey_label,
        tostring(meta.label or ""),
        tonumber(meta.cursor_distance) or 0,
        tonumber(meta.same_name_count) or 0,
        tostring(meta.nearest_text or ""),
        tostring(meta.nearest_distance or ""),
        meta.nearest_text_ignored == true and "true" or "false"
    ))

    local target, fetch_err = fetch_button_for_step(step)
    if not target then
        return false, fetch_err
    end

    if perform_click then
        local ok, click_err = click_fetched_target(step, target)
        if not ok then
            return false, click_err
        end
    end

    return true, {
        kind = target.kind,
        addr = target.addr,
        name = target.name,
        text = target.text,
        fullname = target.fullname,
        x = target.x,
        y = target.y,
        related_text = target.related_text,
        related_name = target.related_name,
        related_distance = target.related_text_distance,
        hint_distance = target.hint_distance,
        hint_x = target.hint_x,
        hint_y = target.hint_y,
        step_snippet = avepoint_hotkey_format_locator_step(step)
    }
end

function avepoint_hotkey_click_cursor_probe_target()
    return avepoint_hotkey_resolve_cursor_probe_target({
        hotkey_label = "F11",
        perform_click = true
    })
end

function avepoint_hotkey_preview_cursor_probe_target()
    return avepoint_hotkey_resolve_cursor_probe_target({
        hotkey_label = "F10",
        perform_click = false
    })
end

function avepoint_hotkey_dump_cursor_api_raw()
    if type(nav) ~= "table"
        or type(nav.dump_current_selected_button) ~= "function"
    then
        return false, "GetCurrentSelected raw dump is unavailable."
    end

    return nav.dump_current_selected_button({
        header = "F12 GetCurrentSelected raw dump",
        dump_depth = 4,
        dump_table_limit = 48
    })
end

local function avepoint_hotkey_print_cursor_client_pos(label)
    if type(nav) ~= "table" or type(nav.cursor_client_pos) ~= "function" then
        return false, "nav.cursor_client_pos is unavailable."
    end

    local cursor, cursor_err = nav.cursor_client_pos({
        allow_outside = true
    })
    if type(cursor) ~= "table" then
        return false, cursor_err or "Unable to read current mouse position."
    end

    local client_x = tonumber(cursor.client_x) or 0
    local client_y = tonumber(cursor.client_y) or 0
    local client_w = tonumber(cursor.client_w) or 0
    local client_h = tonumber(cursor.client_h) or 0
    local ratio_x = client_w > 0 and client_x / client_w or 0
    local ratio_y = client_h > 0 and client_y / client_h or 0
    local outside = client_x < 0 or client_y < 0 or client_x > client_w or client_y > client_h
    local hotkey_label = tostring(label or "F8")

    log.info(string.format(
        "%s cursor pos: client=(%.2f, %.2f) ratio=(%.6f, %.6f) screen=(%.2f, %.2f) origin=(%.2f, %.2f) client_size=(%.2f, %.2f) hwnd=%s outside=%s",
        hotkey_label,
        client_x,
        client_y,
        ratio_x,
        ratio_y,
        tonumber(cursor.screen_x) or 0,
        tonumber(cursor.screen_y) or 0,
        tonumber(cursor.origin_x) or 0,
        tonumber(cursor.origin_y) or 0,
        client_w,
        client_h,
        avepoint_format_addr_hex(cursor.hwnd),
        outside and "true" or "false"
    ))

    return true
end

local function avepoint_data_api_is_scalar(value)
    local value_type = type(value)
    return value_type ~= "table"
        and value_type ~= "function"
        and value_type ~= "userdata"
        and value_type ~= "thread"
end

local function avepoint_data_api_format_scalar(value)
    if value == nil then
        return "nil"
    end
    local value_type = type(value)
    if value_type == "number" or value_type == "boolean" then
        return tostring(value)
    end
    local text = tostring(value or "")
    text = text:gsub("\\", "\\\\")
    text = text:gsub("\r", "\\r")
    text = text:gsub("\n", "\\n")
    text = text:gsub("\t", "\\t")
    text = text:gsub("\"", "\\\"")
    if #text > 120 then
        text = text:sub(1, 117) .. "..."
    end
    return "\"" .. text .. "\""
end

local function avepoint_data_api_shallow_table(value, max_items)
    if type(value) ~= "table" then
        return avepoint_data_api_format_scalar(value)
    end

    local keys = {}
    for key, _ in pairs(value) do
        if type(key) == "string" or type(key) == "number" then
            keys[#keys + 1] = key
        end
    end
    table.sort(keys, function(a, b)
        return tostring(a) < tostring(b)
    end)

    local parts = {}
    local limit = tonumber(max_items) or 16
    local count = 0
    for _, key in ipairs(keys) do
        local item = value[key]
        if avepoint_data_api_is_scalar(item) then
            parts[#parts + 1] = tostring(key) .. "=" .. avepoint_data_api_format_scalar(item)
            count = count + 1
            if count >= limit then
                break
            end
        end
    end

    if #parts == 0 then
        return "{}"
    end
    if #keys > count then
        parts[#parts + 1] = "_keys=" .. tostring(#keys)
    end
    return "{" .. table.concat(parts, " ") .. "}"
end

local function avepoint_data_api_position_summary(value)
    if type(nav) ~= "table" or type(nav.extract_position) ~= "function" then
        return ""
    end
    local x, y, z = nav.extract_position(value)
    if x == nil or y == nil then
        return ""
    end
    return string.format(
        " pos=%.2f, %.2f, %.2f",
        tonumber(x) or 0,
        tonumber(y) or 0,
        tonumber(z) or 0
    )
end

local function avepoint_data_api_summary(value)
    local value_type = type(value)
    if value_type ~= "table" then
        return string.format("type=%s value=%s", value_type, avepoint_data_api_format_scalar(value))
    end

    local array_count = #value
    local summary = string.format(
        "type=table count=%d%s fields=%s",
        array_count,
        avepoint_data_api_position_summary(value),
        avepoint_data_api_shallow_table(value, 14)
    )
    if array_count > 0 then
        local first = value[1]
        summary = summary .. " first=" .. avepoint_data_api_shallow_table(first, 12)
        if type(first) == "table" then
            summary = summary .. avepoint_data_api_position_summary(first)
        end
    end
    return summary
end

local function avepoint_data_api_log_result(label, ok, value, err)
    if not ok then
        log.warn(string.format("F9 Data API %s: FAIL error=%s", tostring(label), tostring(value)))
        return false
    end

    if value == nil then
        log.warn(string.format("F9 Data API %s: FAIL nil err=%s", tostring(label), tostring(err or "")))
        return false
    end

    log.info(string.format("F9 Data API %s: OK %s", tostring(label), avepoint_data_api_summary(value)))
    return true
end

local function avepoint_data_api_call_raw(api, name)
    if type(api) ~= "table" then
        log.warn("F9 Data API " .. tostring(name) .. ": FAIL game_api unavailable")
        return false
    end

    local fn = api[name]
    if type(fn) ~= "function" then
        log.warn("F9 Data API " .. tostring(name) .. ": FAIL function missing")
        return false
    end

    local ok, value, err = pcall(fn)
    return avepoint_data_api_log_result(name, ok, value, err)
end

local function avepoint_data_api_call_wrapper(label, fn)
    if type(fn) ~= "function" then
        log.warn("F9 Data API " .. tostring(label) .. ": FAIL wrapper missing")
        return false
    end

    local ok, value, err = pcall(fn)
    return avepoint_data_api_log_result(label, ok, value, err)
end

function avepoint_hotkey_test_data_api()
    if type(nav) ~= "table" then
        log.warn("F9 Data API test failed: nav is unavailable.")
        return false
    end
    if type(nav.ensure_initialized) == "function" then
        local ok, err = nav.ensure_initialized(PROCESS_NAME, MODE)
        if not ok then
            log.warn("F9 Data API test failed: " .. tostring(err or "Torch API init failed."))
            return false
        end
        initialized = true
        last_init_error = nil
        next_init_retry_at = sys.time() + INIT_RETRY_MS
    elseif initialized ~= true then
        log.warn("F9 Data API test failed: Torch API not ready.")
        return false
    end

    local api = nav.game_api
    local started_at = sys.time()
    local pass_count = 0
    local fail_count = 0

    log.info("F9 Data API test begin")
    log.info(string.format(
        "F9 Data API context | pid=%s mode=%s initialized=%s",
        tostring(nav.pid or ""),
        tostring(nav.mode or MODE),
        tostring(initialized == true)
    ))

    local raw_names = {
        "GetPlayerAddr",
        "GetPlayerinfo",
        "IsMainInterface",
        "Isloading",
        "GetMainTaskPos",
        "GetMainTaskPath",
        "EnumMonster",
        "EnumGroundItem",
        "EnumPortal",
        "EnumNPC",
        "EnumInteractiveItem",
        "EnumCButton",
        "EnumCText",
        "EnumCImage",
        "GetCurrentSelected"
    }

    for _, name in ipairs(raw_names) do
        if avepoint_data_api_call_raw(api, name) then
            pass_count = pass_count + 1
        else
            fail_count = fail_count + 1
        end
    end

    for _, name in ipairs({ "MoveTo", "control_click" }) do
        local available = type(api) == "table" and type(api[name]) == "function"
        log.info(string.format(
            "F9 Data API %s: SKIP available=%s reason=side_effect_not_called",
            tostring(name),
            available and "true" or "false"
        ))
    end

    local wrapper_tests = {
        { label = "nav.player_info", fn = function() return nav.player_info() end },
        { label = "nav.player_pos", fn = function()
            local x, y, z, err = nav.player_pos()
            if x == nil or y == nil then
                return nil, err
            end
            return { x = x, y = y, z = z }
        end },
        { label = "nav.is_main_interface", fn = function() return nav.is_main_interface() end },
        { label = "nav.is_loading", fn = function() return nav.is_loading() end },
        { label = "nav.get_main_task_pos", fn = function() return nav.get_main_task_pos() end },
        { label = "nav.get_main_task_path", fn = function() return nav.get_main_task_path() end },
        { label = "nav.enum_ground_items", fn = function() return nav.enum_ground_items() end },
        { label = "nav.enum_portals", fn = function() return nav.enum_portals() end },
        { label = "nav.enum_npcs", fn = function() return nav.enum_npcs() end },
        { label = "nav.enum_monsters", fn = function() return nav.enum_monsters() end },
        { label = "nav.get_current_selected_button", fn = function() return nav.get_current_selected_button() end }
    }

    for _, item in ipairs(wrapper_tests) do
        if avepoint_data_api_call_wrapper(item.label, item.fn) then
            pass_count = pass_count + 1
        else
            fail_count = fail_count + 1
        end
    end

    log.info(string.format(
        "F9 Data API test end | ok=%d fail=%d elapsed=%dms",
        pass_count,
        fail_count,
        sys.time() - started_at
    ))
    return fail_count == 0
end

local F9_LEVEL_UP_MAINTENANCE_TEST = {
    kind = "skill",
    level = 8,
    tick_ms = 50,
    timeout_ms = 120000,
    active = false
}

local function avepoint_hotkey_level_up_test_plan_label(kind, level, plan)
    if type(plan) == "table" then
        local label = tostring(plan.label or plan.key or "")
        if label ~= "" then
            return label
        end
    end
    return string.format("%s level %d", tostring(kind or ""), tonumber(level) or 0)
end

local function avepoint_hotkey_stop_level_up_maintenance_test(reason)
    if F9_LEVEL_UP_MAINTENANCE_TEST.active ~= true then
        return
    end

    local runner = F9_LEVEL_UP_MAINTENANCE_TEST.runner
    if type(runner) == "table" and type(runner.clear_level_up_maintenance_executor_state) == "function" then
        pcall(runner.clear_level_up_maintenance_executor_state)
    end

    log.info(string.format(
        "F9 level-up maintenance test stopped | kind=%s level=%s plan=%s reason=%s",
        tostring(F9_LEVEL_UP_MAINTENANCE_TEST.kind or ""),
        tostring(F9_LEVEL_UP_MAINTENANCE_TEST.level or ""),
        tostring(F9_LEVEL_UP_MAINTENANCE_TEST.plan_label or ""),
        tostring(reason or "")
    ))

    F9_LEVEL_UP_MAINTENANCE_TEST.active = false
    F9_LEVEL_UP_MAINTENANCE_TEST.runner = nil
    F9_LEVEL_UP_MAINTENANCE_TEST.ctx = nil
    F9_LEVEL_UP_MAINTENANCE_TEST.plan = nil
    F9_LEVEL_UP_MAINTENANCE_TEST.plan_label = nil
    F9_LEVEL_UP_MAINTENANCE_TEST.started_at = nil
    F9_LEVEL_UP_MAINTENANCE_TEST.next_tick_at = nil
end

local function avepoint_hotkey_start_level_up_maintenance_test(kind, level)
    kind = tostring(kind or F9_LEVEL_UP_MAINTENANCE_TEST.kind or "skill")
    level = math.floor(tonumber(level) or tonumber(F9_LEVEL_UP_MAINTENANCE_TEST.level) or 0)
    if kind == "" or level <= 0 then
        return false, "invalid F9 level-up maintenance test target"
    end
    if state.running == true then
        return false, "automation is running"
    end
    if state.f6_loop_active == true then
        return false, "F6 3-round loop is active"
    end
    if F9_LEVEL_UP_MAINTENANCE_TEST.active == true then
        return false, "F9 level-up maintenance test is already running"
    end

    if not initialized then
        local attach_target = avepoint_hotkey_attach_target_pid()
        local init_ok, init_err = avepoint_wait_for_torch_init(3500, attach_target, MODE)
        if not init_ok then
            return false, "Torch API init failed: " .. tostring(init_err)
        end
    end
    if not initialized then
        return false, "Torch API not ready"
    end

    local runner, load_err = TASK_MODE.load_runner(TASK_MODE.LEVELING)
    if type(runner) ~= "table" then
        return false, tostring(load_err or "load AvePointLeveling runner failed")
    end
    if type(runner.level_up_maintenance_config) ~= "function"
        or type(runner.level_up_maintenance_get_level_plan) ~= "function"
        or type(runner.level_up_maintenance_plan_steps) ~= "function"
        or type(runner.start_level_up_maintenance_executor) ~= "function"
        or type(runner.progress_level_up_maintenance_executor) ~= "function"
    then
        return false, "AvePointLeveling runner lacks level-up maintenance test APIs"
    end

    local cfg = runner.level_up_maintenance_config()
    local plan = runner.level_up_maintenance_get_level_plan(cfg, kind, level)
    local steps = runner.level_up_maintenance_plan_steps(plan)
    if type(plan) ~= "table" or type(steps) ~= "table" or #steps <= 0 then
        return false, string.format("missing %s_by_level[%d] maintenance plan", kind, level)
    end

    local plan_id = tostring(plan.key or "")
    if plan_id == "" and type(runner.level_up_maintenance_plan_id) == "function" then
        plan_id = runner.level_up_maintenance_plan_id(kind, level, plan)
    end
    local ctx = TASK_MODE.build_context()
    if type(runner.refresh_persistence_character_identity) == "function" then
        pcall(runner.refresh_persistence_character_identity, ctx, true)
    end

    local now = sys.time()
    local started, start_err = runner.start_level_up_maintenance_executor(ctx, now, {
        kind = kind,
        level = level,
        plan = plan,
        steps = steps,
        id = plan_id
    })
    if not started then
        return false, tostring(start_err or "executor start failed")
    end

    F9_LEVEL_UP_MAINTENANCE_TEST.kind = kind
    F9_LEVEL_UP_MAINTENANCE_TEST.level = level
    F9_LEVEL_UP_MAINTENANCE_TEST.runner = runner
    F9_LEVEL_UP_MAINTENANCE_TEST.ctx = ctx
    F9_LEVEL_UP_MAINTENANCE_TEST.plan = plan
    F9_LEVEL_UP_MAINTENANCE_TEST.plan_label = avepoint_hotkey_level_up_test_plan_label(kind, level, plan)
    F9_LEVEL_UP_MAINTENANCE_TEST.started_at = now
    F9_LEVEL_UP_MAINTENANCE_TEST.next_tick_at = now
    F9_LEVEL_UP_MAINTENANCE_TEST.active = true

    log.info(string.format(
        "F9 level-up maintenance test started | kind=%s level=%d plan=%s steps=%d",
        kind,
        level,
        tostring(F9_LEVEL_UP_MAINTENANCE_TEST.plan_label or ""),
        #steps
    ))
    return true
end

local function avepoint_hotkey_update_level_up_maintenance_test(current_time)
    if F9_LEVEL_UP_MAINTENANCE_TEST.active ~= true then
        return
    end

    current_time = tonumber(current_time) or sys.time()
    if current_time < (tonumber(F9_LEVEL_UP_MAINTENANCE_TEST.next_tick_at) or 0) then
        return
    end

    if state.running == true then
        avepoint_hotkey_stop_level_up_maintenance_test("automation started")
        return
    end

    local started_at = tonumber(F9_LEVEL_UP_MAINTENANCE_TEST.started_at) or current_time
    local timeout_ms = tonumber(F9_LEVEL_UP_MAINTENANCE_TEST.timeout_ms) or 120000
    if timeout_ms > 0 and current_time - started_at > timeout_ms then
        avepoint_hotkey_stop_level_up_maintenance_test("timeout")
        return
    end

    local runner = F9_LEVEL_UP_MAINTENANCE_TEST.runner
    local ctx = F9_LEVEL_UP_MAINTENANCE_TEST.ctx
    if type(runner) ~= "table" or type(runner.progress_level_up_maintenance_executor) ~= "function" then
        avepoint_hotkey_stop_level_up_maintenance_test("runner unavailable")
        return
    end

    local ok, step_ok = pcall(runner.progress_level_up_maintenance_executor, ctx, current_time)
    if not ok then
        log.error("F9 level-up maintenance test failed: " .. tostring(step_ok))
        avepoint_hotkey_stop_level_up_maintenance_test("progress error")
        return
    end
    if step_ok == false then
        avepoint_hotkey_stop_level_up_maintenance_test("executor inactive")
        return
    end

    if type(runner.level_up_maintenance_executor_is_active) == "function"
        and runner.level_up_maintenance_executor_is_active() ~= true
    then
        local done = false
        if type(runner.level_up_maintenance_plan_done) == "function" then
            done = runner.level_up_maintenance_plan_done(
                F9_LEVEL_UP_MAINTENANCE_TEST.kind,
                F9_LEVEL_UP_MAINTENANCE_TEST.level,
                F9_LEVEL_UP_MAINTENANCE_TEST.plan
            ) == true
        end
        if done then
            log.info(string.format(
                "F9 level-up maintenance test completed | kind=%s level=%s plan=%s elapsed=%dms",
                tostring(F9_LEVEL_UP_MAINTENANCE_TEST.kind or ""),
                tostring(F9_LEVEL_UP_MAINTENANCE_TEST.level or ""),
                tostring(F9_LEVEL_UP_MAINTENANCE_TEST.plan_label or ""),
                math.max(0, current_time - started_at)
            ))
        else
            log.warn(string.format(
                "F9 level-up maintenance test ended without completion mark | kind=%s level=%s plan=%s elapsed=%dms",
                tostring(F9_LEVEL_UP_MAINTENANCE_TEST.kind or ""),
                tostring(F9_LEVEL_UP_MAINTENANCE_TEST.level or ""),
                tostring(F9_LEVEL_UP_MAINTENANCE_TEST.plan_label or ""),
                math.max(0, current_time - started_at)
            ))
        end
        F9_LEVEL_UP_MAINTENANCE_TEST.active = false
        F9_LEVEL_UP_MAINTENANCE_TEST.runner = nil
        F9_LEVEL_UP_MAINTENANCE_TEST.ctx = nil
        F9_LEVEL_UP_MAINTENANCE_TEST.plan = nil
        F9_LEVEL_UP_MAINTENANCE_TEST.plan_label = nil
        F9_LEVEL_UP_MAINTENANCE_TEST.started_at = nil
        F9_LEVEL_UP_MAINTENANCE_TEST.next_tick_at = nil
        return
    end

    F9_LEVEL_UP_MAINTENANCE_TEST.next_tick_at = sys.time() + math.max(10, tonumber(F9_LEVEL_UP_MAINTENANCE_TEST.tick_ms) or 50)
end

function main()
    write_hotkey_owner_lock()

    if type(_G) == "table" and _G.__CUNNEI_PROCESS_GUARD_READY == true then
        log.info("Process guard already ready. Source: " .. tostring(_G.__CUNNEI_PROCESS_GUARD_SOURCE or "unknown"))
    else
        log.info("Process guard starting")
        local ok = protect_current_process()
        if not ok then
            remove_hotkey_owner_lock()
            return
        end
        log.info("Process guard ready")
    end

    nav.set_move_call_mouse_sync({
        enabled = false
    })

    TASK_MODE.refresh_config()
    log.info(string.format(
        "Configured %s=%d (%s) in %s",
        TASK_MODE.CONFIG_KEY,
        TASK_MODE.configured_id,
        TASK_MODE.configured_name,
        tostring(guard.engine_config)
    ))
    if TASK_MODE.configured_id == TASK_MODE.GOLD and HOTKEY_F6_ENABLED == true then
        log.info(string.format(
            "Press F6 to print current player coordinates; Insert or Ctrl+F9 or [ to start AvePoint automation; hold Delete or Ctrl+F10 or ] to stop (%d points, %d random maps)",
            #ROUTE_POINTS,
            #RANDOM_MAP_POOL_KEYS
        ))
    else
        log.info("Press Insert or Ctrl+F9 or [ to start current task mode; hold Delete or Ctrl+F10 or ] to stop")
    end
    log.info("Press F1 to dump all buttons returned by EnumCButton")
    log.info("Press F2 to dump all text controls returned by EnumCText")
    log.info("Press F5 to dump nearby NPCs returned by EnumNPC")
    log.info("Press F12 to dump raw GetCurrentSelected API output without clicking")
    local f4_preset = HOTKEY_DISTANCE_PREVIEW_PRESETS and HOTKEY_DISTANCE_PREVIEW_PRESETS.current or nil
    log.info(string.format(
        "Press F4 to preview the configured target for %s without clicking",
        tostring(f4_preset and f4_preset.label or "preview target")
    ))
    log.info("Press F3 to dump all images returned by EnumCImage")
    local f11_preset = HOTKEY_CURSOR_CLICK_PRESETS and HOTKEY_CURSOR_CLICK_PRESETS.current or nil
    log.info(string.format(
        "Press F10 to inspect the same uniquely matched mouse target for %s without clicking",
        tostring(f11_preset and f11_preset.label or "cursor click test")
    ))
    log.info(string.format(
        "Press F11 or \\ to validate-click a uniquely matched mouse target for %s",
        tostring(f11_preset and f11_preset.label or "cursor click test")
    ))
    log.info("Press F6 to print current player coordinates")
    log.info("Press F7 to print current player position and nearby portals")
    log.info("Press F8 to print current mouse client coordinates")
    log.info("Press F9 to test level 8 skill maintenance plan")
    log.info("Press Ctrl+F12 to exit")
    log.info("Waiting for torchlight API/game init...")

    if not hotkey.is_running() then
        hotkey.start(10)
        started_hotkey = true
    end

    local STOP_HOTKEY_CONFIRM_HOLD_MS = 180
    local stop_hotkey_pending_source = nil
    local stop_hotkey_pending_vk = nil
    local stop_hotkey_pending_modifier_vk = nil
    local stop_hotkey_pending_at = 0

    local function stop_hotkey_down(vk, modifier_vk)
        if vk == nil then
            return false
        end
        if modifier_vk ~= nil and not hotkey.is_pressed(modifier_vk) then
            return false
        end
        return hotkey.is_pressed(vk)
    end

    local function arm_stop_hotkey_candidate(source, vk, modifier_vk, current_time)
        stop_hotkey_pending_source = source
        stop_hotkey_pending_vk = vk
        stop_hotkey_pending_modifier_vk = modifier_vk
        stop_hotkey_pending_at = current_time
        log.info(string.format(
            "Stop hotkey candidate | source=%s hold_required=%dms running=%s f6_loop=%s",
            tostring(source),
            STOP_HOTKEY_CONFIRM_HOLD_MS,
            tostring(state.running == true),
            tostring(state.f6_loop_active == true)
        ))
    end

    local function clear_stop_hotkey_candidate()
        stop_hotkey_pending_source = nil
        stop_hotkey_pending_vk = nil
        stop_hotkey_pending_modifier_vk = nil
        stop_hotkey_pending_at = 0
    end

    while true do
        local loop_now = sys.time()
        if loop_now - (hotkey_owner_last_write_at or 0) >= HOTKEY_OWNER_HEARTBEAT_MS then
            write_hotkey_owner_lock()
        end

        local exit_down = hotkey.is_pressed(HOTKEY_EXIT_CTRL) and hotkey.is_pressed(HOTKEY_EXIT)
        if exit_down and not exit_latch then
            log.info("Exit hotkey pressed")
            break
        end
        exit_latch = exit_down

        if not initialized and sys.time() >= next_init_retry_at then
            local init_ok, err = nav.init(PROCESS_NAME, MODE)
            if init_ok then
                initialized = true
                last_init_error = nil
                log.info("Torch API initialized")

                local x, y, z, pos_err = nav.player_pos()
                if x ~= nil and y ~= nil then
                    log.info(string.format("Current pos: %.2f, %.2f, %.2f", x, y, z or 0))
                else
                    log.warn("Read position failed: " .. tostring(pos_err))
                end
            else
                if err ~= last_init_error then
                    log.warn("Torch init pending: " .. tostring(err))
                    last_init_error = err
                end
                next_init_retry_at = sys.time() + INIT_RETRY_MS
            end
        end

        local stop_source = nil
        local stop_vk = nil
        local stop_modifier_vk = nil
        if pressed_once(0x2E) then
            stop_source = "Delete"
            stop_vk = 0x2E
        elseif pressed_once(0x79, HOTKEY_EXIT_CTRL) then
            stop_source = "Ctrl+F10"
            stop_vk = 0x79
            stop_modifier_vk = HOTKEY_EXIT_CTRL
        elseif pressed_once(HOTKEY_STOP_BRACKET) then
            stop_source = "]"
            stop_vk = HOTKEY_STOP_BRACKET
        end

        if stop_source ~= nil then
            arm_stop_hotkey_candidate(stop_source, stop_vk, stop_modifier_vk, loop_now)
        end

        if stop_hotkey_pending_source ~= nil then
            if not stop_hotkey_down(stop_hotkey_pending_vk, stop_hotkey_pending_modifier_vk) then
                log.info(string.format(
                    "Stop hotkey candidate cancelled | source=%s held_ms=%d",
                    tostring(stop_hotkey_pending_source),
                    math.max(0, loop_now - (tonumber(stop_hotkey_pending_at) or loop_now))
                ))
                clear_stop_hotkey_candidate()
            elseif loop_now - (tonumber(stop_hotkey_pending_at) or loop_now) >= STOP_HOTKEY_CONFIRM_HOLD_MS then
                log.info(string.format(
                    "Stop hotkey confirmed | source=%s held_ms=%d running=%s f6_loop=%s",
                    tostring(stop_hotkey_pending_source),
                    math.max(0, loop_now - (tonumber(stop_hotkey_pending_at) or loop_now)),
                    tostring(state.running == true),
                    tostring(state.f6_loop_active == true)
                ))
                if state.running then
                    stop_automation("AvePoint automation stopped")
                end
                if F9_LEVEL_UP_MAINTENANCE_TEST.active == true then
                    avepoint_hotkey_stop_level_up_maintenance_test("stop hotkey")
                end
                if state.f6_loop_active == true then
                    stop_f6_loop("F6 3-round loop stopped")
                end
                clear_stop_hotkey_candidate()
            end
        end

        if pressed_once(HOTKEY_F1) then
            if not initialized then
                local attach_target = avepoint_hotkey_attach_target_pid()
                local init_ok, init_err = avepoint_wait_for_torch_init(3500, attach_target, MODE)
                if not init_ok then
                    log.warn("F1 EnumCButton dump unavailable: Torch API init failed: " .. tostring(init_err))
                end
            end
            if initialized then
                local ok, err = avepoint_hotkey_probe_main_task_button_by_coordinate()
                if not ok then
                    log.error("F1 EnumCButton dump failed: " .. tostring(err))
                end
            end
        end

        if pressed_once(HOTKEY_F2) then
            if not initialized then
                local attach_target = avepoint_hotkey_attach_target_pid()
                local init_ok, init_err = avepoint_wait_for_torch_init(3500, attach_target, MODE)
                if not init_ok then
                    log.warn("F2 EnumCText dump unavailable: Torch API init failed: " .. tostring(init_err))
                end
            end
            if initialized then
                local ok, err = avepoint_hotkey_dump_visible_text_controls("F2")
                if not ok then
                    log.error("F2 EnumCText dump failed: " .. tostring(err))
                end
            end
        end

        if pressed_once(HOTKEY_F5) then
            if not initialized then
                local attach_target = avepoint_hotkey_attach_target_pid()
                local init_ok, init_err = avepoint_wait_for_torch_init(3500, attach_target, MODE)
                if not init_ok then
                    log.warn("F5 EnumNPC dump unavailable: Torch API init failed: " .. tostring(init_err))
                end
            end
            if initialized then
                local ok, err = avepoint_hotkey_dump_nearby_npcs()
                if not ok then
                    log.error("F5 EnumNPC dump failed: " .. tostring(err))
                end
            end
        end

        if HOTKEY_F6_ENABLED == true and pressed_once(HOTKEY_F6) then
            if not initialized then
                log.warn("Torch API not ready yet")
            else
                local x, y, z, pos_err = nav.player_pos()
                if x ~= nil and y ~= nil then
                    log.info(string.format(
                        "F6 current pos: %.2f, %.2f, %.2f",
                        tonumber(x) or 0,
                        tonumber(y) or 0,
                        tonumber(z) or 0
                    ))
                else
                    log.warn("F6 current pos unavailable: " .. tostring(pos_err))
                end
            end
        end

        if pressed_once(HOTKEY_F7) then
            if not initialized then
                log.warn("Torch API not ready yet")
            else
                local x, y, z, pos_err = nav.player_pos()
                if x ~= nil and y ~= nil then
                    local snapshot, snapshot_err = nav.enum_ui()
                    local map_ui, map_err = nav.get_map_ui_info(snapshot)
                    local task_panel, task_panel_err = nav.get_task_panel_info(snapshot)
                    local function trim_text(value)
                        return tostring(value or ""):gsub("^%s+", ""):gsub("%s+$", "")
                    end
                    local function is_scalar_dump_value(value)
                        local value_type = type(value)
                        return value_type ~= "table"
                            and value_type ~= "function"
                            and value_type ~= "userdata"
                            and value_type ~= "thread"
                    end
                    local function format_dump_value(value)
                        if value == nil then
                            return "nil"
                        end
                        local value_type = type(value)
                        if value_type == "number" or value_type == "boolean" then
                            return tostring(value)
                        end
                        local text = tostring(value or "")
                        text = text:gsub("\\", "\\\\")
                        text = text:gsub("\r", "\\r")
                        text = text:gsub("\n", "\\n")
                        text = text:gsub("\t", "\\t")
                        text = text:gsub("\"", "\\\"")
                        return "\"" .. text .. "\""
                    end
                    local function summarize_table_shallow(tbl, max_items)
                        if type(tbl) ~= "table" then
                            return tostring(tbl)
                        end
                        local keys = {}
                        for key, _ in pairs(tbl) do
                            if type(key) == "string" or type(key) == "number" then
                                keys[#keys + 1] = key
                            end
                        end
                        table.sort(keys, function(a, b)
                            return tostring(a) < tostring(b)
                        end)
                        local parts = {}
                        local count = 0
                        local limit = max_items or 24
                        for _, key in ipairs(keys) do
                            local value = tbl[key]
                            if is_scalar_dump_value(value) then
                                parts[#parts + 1] = tostring(key) .. "=" .. format_dump_value(value)
                                count = count + 1
                                if count >= limit then
                                    break
                                end
                            end
                        end
                        if #parts == 0 then
                            return "{}"
                        end
                        if #keys > count then
                            parts[#parts + 1] = "_keys=" .. tostring(#keys)
                        end
                        return "{" .. table.concat(parts, " ") .. "}"
                    end
                    local function extract_f7_item_position(item)
                        if type(item) ~= "table" then
                            return nil, nil, nil
                        end
                        if type(nav.extract_position) == "function" then
                            local item_x, item_y, item_z = nav.extract_position(item)
                            if item_x ~= nil and item_y ~= nil then
                                return item_x, item_y, item_z
                            end
                        end

                        local function pick_number(tbl, keys)
                            if type(tbl) ~= "table" then
                                return nil
                            end
                            for _, key in ipairs(keys) do
                                local value = tonumber(tbl[key])
                                if value ~= nil then
                                    return value
                                end
                            end
                            return nil
                        end

                        local item_x = pick_number(item, { "x", "X", "posX", "PosX", "worldX", "WorldX" })
                        local item_y = pick_number(item, { "y", "Y", "posY", "PosY", "worldY", "WorldY" })
                        local item_z = pick_number(item, { "z", "Z", "posZ", "PosZ", "worldZ", "WorldZ" })
                        if item_x ~= nil and item_y ~= nil then
                            return item_x, item_y, item_z
                        end

                        for _, key in ipairs({ "pos", "position", "coord", "coords", "point", "location", "Location" }) do
                            local nested = item[key]
                            if type(nested) == "table" then
                                item_x = pick_number(nested, { "x", "X", "posX", "PosX", "worldX", "WorldX" })
                                item_y = pick_number(nested, { "y", "Y", "posY", "PosY", "worldY", "WorldY" })
                                item_z = pick_number(nested, { "z", "Z", "posZ", "PosZ", "worldZ", "WorldZ" })
                                if item_x ~= nil and item_y ~= nil then
                                    return item_x, item_y, item_z
                                end
                            end
                        end

                        return nil, nil, nil
                    end
                    local function f7_item_label(item)
                        if type(item) ~= "table" then
                            return ""
                        end
                        for _, key in ipairs({
                            "name", "Name", "text", "Text", "fullname", "Fullname",
                            "displayName", "DisplayName", "title", "Title"
                        }) do
                            local value = trim_text(item[key])
                            if value ~= "" then
                                return value
                            end
                        end
                        return ""
                    end
                    local function log_f7_nearby_portals(player_x, player_y)
                        if type(nav.enum_portals) ~= "function" then
                            log.warn("F7 nearby portals unavailable: nav.enum_portals is unavailable.")
                            return
                        end

                        local portals, portal_err = nav.enum_portals()
                        if type(portals) ~= "table" then
                            log.warn("F7 nearby portals failed: " .. tostring(portal_err or "EnumPortal failed."))
                            return
                        end

                        log.info(string.format("F7 nearby portals count=%d", #portals))
                        if #portals == 0 then
                            return
                        end

                        local rows = {}
                        for index, item in ipairs(portals) do
                            local portal_x, portal_y, portal_z = extract_f7_item_position(item)
                            local player_distance = nil
                            if portal_x ~= nil and portal_y ~= nil and player_x ~= nil and player_y ~= nil then
                                player_distance = distance_2d(player_x, player_y, portal_x, portal_y)
                            end
                            rows[#rows + 1] = {
                                index = index,
                                item = item,
                                x = portal_x,
                                y = portal_y,
                                z = portal_z,
                                distance = player_distance,
                                label = f7_item_label(item)
                            }
                        end
                        table.sort(rows, function(a, b)
                            local ad = tonumber(a.distance) or math.huge
                            local bd = tonumber(b.distance) or math.huge
                            if ad ~= bd then
                                return ad < bd
                            end
                            return tonumber(a.index) < tonumber(b.index)
                        end)

                        local limit = math.min(8, #rows)
                        for output_index = 1, limit do
                            local row = rows[output_index]
                            log.info(string.format(
                                "F7 portal[%d/%d] source_index=%d label=%s pos=%.2f, %.2f, %.2f distance=%s raw=%s",
                                output_index,
                                #rows,
                                tonumber(row.index) or 0,
                                tostring(row.label or ""),
                                tonumber(row.x) or 0,
                                tonumber(row.y) or 0,
                                tonumber(row.z) or 0,
                                row.distance ~= nil and string.format("%.2f", tonumber(row.distance) or 0) or "nil",
                                summarize_table_shallow(row.item, 20)
                            ))
                        end
                        if #rows > limit then
                            log.info(string.format("F7 nearby portals truncated | shown=%d total=%d", limit, #rows))
                        end
                    end
                    local function summarize_level_candidates(info)
                        if type(info) ~= "table" then
                            return ""
                        end
                        local keys = {
                            "level", "Level", "lv", "Lv", "LV",
                            "playerLevel", "PlayerLevel", "roleLevel", "RoleLevel",
                            "charLevel", "CharLevel", "grade", "Grade",
                            "exp", "Exp", "experience", "Experience"
                        }
                        local parts = {}
                        for _, key in ipairs(keys) do
                            local value = info[key]
                            if value ~= nil and tostring(value) ~= "" then
                                parts[#parts + 1] = tostring(key) .. "=" .. format_dump_value(value)
                            end
                        end
                        return table.concat(parts, " ")
                    end
                    local function collect_level_text_candidates(ui_snapshot)
                        if type(ui_snapshot) ~= "table" or type(ui_snapshot.texts) ~= "table" then
                            return {}
                        end
                        local candidates = {}
                        for index, item in ipairs(ui_snapshot.texts) do
                            local text = trim_text(item and item.text)
                            local name = tostring(item and item.name or "")
                            local fullname = tostring(item and (item.Fullname or item.fullname) or "")
                            if text ~= "" then
                                local normalized = text:lower()
                                local score = 0
                                if text:match("等级%s*%d+") then
                                    score = score + 120
                                end
                                if text:match("等级%s*%d+%s*%(%d+%%%)") then
                                    score = score + 80
                                end
                                if normalized:match("lv%s*%d+") or normalized:match("level%s*%d+") then
                                    score = score + 60
                                end
                                if text:find("经验", 1, true) ~= nil then
                                    score = score + 24
                                end
                                if text:match("%d+%%") then
                                    score = score + 18
                                end
                                if normalized:find("level", 1, true) ~= nil or normalized:find("lv", 1, true) ~= nil then
                                    score = score + 12
                                end
                                if score > 0 then
                                    local level_value = text:match("等级%s*(%d+)")
                                        or normalized:match("lv%s*(%d+)")
                                        or normalized:match("level%s*(%d+)")
                                    local progress_value = text:match("%((%d+)%%%s*%)")
                                        or text:match("(%d+)%%%s*$")
                                    candidates[#candidates + 1] = {
                                        index = index,
                                        text = text,
                                        name = name,
                                        fullname = fullname,
                                        x = tonumber(item and item.x),
                                        y = tonumber(item and item.y),
                                        score = score,
                                        level = level_value and tonumber(level_value) or nil,
                                        progress = progress_value and tonumber(progress_value) or nil
                                    }
                                end
                            end
                        end
                        table.sort(candidates, function(a, b)
                            local sa = tonumber(a and a.score) or 0
                            local sb = tonumber(b and b.score) or 0
                            if sa ~= sb then
                                return sa > sb
                            end
                            local ya = tonumber(a and a.y) or math.huge
                            local yb = tonumber(b and b.y) or math.huge
                            if ya ~= yb then
                                return ya > yb
                            end
                            local xa = tonumber(a and a.x) or math.huge
                            local xb = tonumber(b and b.x) or math.huge
                            return xa < xb
                        end)
                        return candidates
                    end
                    local function summarize_level_text_candidates(ui_snapshot, max_items)
                        local candidates = collect_level_text_candidates(ui_snapshot)
                        if #candidates == 0 then
                            return "", nil
                        end
                        local parts = {}
                        local limit = math.max(1, max_items or 4)
                        for index = 1, math.min(limit, #candidates) do
                            local item = candidates[index]
                            parts[#parts + 1] = string.format(
                                "[%d]text=%s level=%s progress=%s x=%s y=%s name=%s score=%d",
                                index,
                                tostring(item.text or ""),
                                tostring(item.level or ""),
                                tostring(item.progress or ""),
                                tostring(item.x or ""),
                                tostring(item.y or ""),
                                tostring(item.name or ""),
                                tonumber(item.score) or 0
                            )
                        end
                        return table.concat(parts, " | "), candidates[1]
                    end
                    local function resolve_f7_task_display()
                        if type(task_panel) == "table" and type(task_panel.tasks) == "table" then
                            local fallback_item = nil
                            for _, item in ipairs(task_panel.tasks) do
                                local kind = trim_text(item and item.kind)
                                local title = trim_text(item and (item.title or item.raw_text))
                                if title ~= "" then
                                    if fallback_item == nil then
                                        fallback_item = item
                                    end
                                    if kind == "主线" then
                                        return title, trim_text(item and item.detail), "task_panel_mainline"
                                    end
                                end
                            end
                            if fallback_item ~= nil then
                                local fallback_title = trim_text(fallback_item.title or fallback_item.raw_text)
                                return fallback_title, trim_text(fallback_item.detail), "task_panel_fallback"
                            end
                        end

                        local current_task_name = trim_text(_G.AVEPOINT_CURRENT_TASK_NAME)
                        if current_task_name:match("^主线%s+") then
                            current_task_name = trim_text(_G.AVEPOINT_LAST_TASK_NAME or current_task_name)
                        elseif current_task_name == "" then
                            current_task_name = trim_text(_G.AVEPOINT_LAST_TASK_NAME)
                        end
                        local current_task_detail = trim_text(_G.AVEPOINT_CURRENT_TASK_DETAIL)
                        if current_task_detail == "" then
                            current_task_detail = trim_text(_G.AVEPOINT_LAST_TASK_DETAIL)
                        end
                        return current_task_name, current_task_detail, "runtime_state"
                    end
                    local current_task_name, current_task_detail = resolve_f7_task_display()
                    local function log_f7_task_panel_entries()
                        if type(task_panel) ~= "table"
                            or type(task_panel.tasks) ~= "table"
                            or #task_panel.tasks <= 0
                        then
                            return false
                        end

                        local task_parts = {}
                        local missing_main_detail = nil
                        for index, item in ipairs(task_panel.tasks) do
                            local kind = trim_text(item and item.kind)
                            local title = trim_text(item and (item.title or item.raw_text))
                            local detail = trim_text(item and item.detail)
                            local detail_suffix = detail ~= "" and (" -> " .. detail) or ""
                            task_parts[#task_parts + 1] = string.format(
                                "%d=%s%s%s",
                                index,
                                kind ~= "" and (kind .. " ") or "",
                                title,
                                detail_suffix
                            )
                            if missing_main_detail == nil and kind == "主线" and title ~= "" and detail == "" then
                                missing_main_detail = item
                            end
                        end
                        log.info("F7 visible task panel entries: " .. table.concat(task_parts, " | "))

                        if type(missing_main_detail) == "table"
                            and type(missing_main_detail.detail_debug_candidates) == "table"
                            and #missing_main_detail.detail_debug_candidates > 0
                        then
                            local title = trim_text(missing_main_detail.title or missing_main_detail.raw_text)
                            log.info(
                                "F7 main task nearby detail candidates: task="
                                .. title
                                .. " | "
                                .. table.concat(missing_main_detail.detail_debug_candidates, " | ")
                            )
                        end
                        return true
                    end
                    local function f7_short_text(value, max_len)
                        local text = tostring(value or "")
                        text = text:gsub("\r", " "):gsub("\n", " "):gsub("\t", " ")
                        local limit = math.max(8, tonumber(max_len) or 96)
                        if #text > limit then
                            return text:sub(1, limit - 3) .. "..."
                        end
                        return text
                    end
                    local function f7_button_identity(item)
                        return table.concat({
                            tostring(item and item.name or ""),
                            tostring(item and (item.Fullname or item.fullname) or ""),
                            tostring(item and item.text or "")
                        }, " ")
                    end
                    local function f7_is_task_button(item)
                        local identity = f7_button_identity(item):lower()
                        return identity:find("taskitem_c.widgettree.taskbtn", 1, true) ~= nil
                            or identity:find("widgettree.taskbtn", 1, true) ~= nil
                    end
                    local function log_f7_task_panel_debug()
                        local snapshot_texts = type(snapshot) == "table" and type(snapshot.texts) == "table" and snapshot.texts or {}
                        local snapshot_buttons = type(snapshot) == "table" and type(snapshot.buttons) == "table" and snapshot.buttons or {}
                        local parsed_tasks = type(task_panel) == "table" and type(task_panel.tasks) == "table" and #task_panel.tasks or 0
                        local parsed_buttons = type(task_panel) == "table" and tonumber(task_panel.button_count) or nil
                        local logged = false

                        log.warn(string.format(
                            "F7 task panel parser summary | entries=%d task_buttons=%s raw_buttons=%d raw_texts=%d err=%s",
                            parsed_tasks,
                            tostring(parsed_buttons or ""),
                            #snapshot_buttons,
                            #snapshot_texts,
                            tostring(task_panel_err or "")
                        ))
                        logged = true

                        if type(task_panel) == "table"
                            and type(task_panel.debug_candidates) == "table"
                            and #task_panel.debug_candidates > 0
                        then
                            log.warn("F7 task panel candidates: " .. table.concat(task_panel.debug_candidates, " | "))
                        end

                        local task_button_parts = {}
                        local left_button_parts = {}
                        for index, item in ipairs(snapshot_buttons) do
                            local x_pos = tonumber(item and item.x)
                            local y_pos = tonumber(item and item.y)
                            local is_task_button = f7_is_task_button(item)
                            if is_task_button and #task_button_parts < 10 then
                                task_button_parts[#task_button_parts + 1] = string.format(
                                    "[%d]addr=%s x=%s y=%s name=%s fullname=%s",
                                    index,
                                    tostring(item and item.addr or ""),
                                    tostring(x_pos or ""),
                                    tostring(y_pos or ""),
                                    f7_short_text(item and item.name, 48),
                                    f7_short_text(item and (item.Fullname or item.fullname), 96)
                                )
                            end
                            if x_pos ~= nil and y_pos ~= nil
                                and x_pos >= 0 and x_pos <= 260
                                and y_pos >= 120 and y_pos <= 430
                                and #left_button_parts < 12
                            then
                                left_button_parts[#left_button_parts + 1] = string.format(
                                    "[%d]addr=%s x=%.1f y=%.1f task=%s name=%s fullname=%s",
                                    index,
                                    tostring(item and item.addr or ""),
                                    x_pos,
                                    y_pos,
                                    is_task_button and "true" or "false",
                                    f7_short_text(item and item.name, 40),
                                    f7_short_text(item and (item.Fullname or item.fullname), 72)
                                )
                            end
                        end

                        if #task_button_parts > 0 then
                            log.warn("F7 raw task buttons: " .. table.concat(task_button_parts, " | "))
                        else
                            log.warn("F7 raw task buttons: none")
                        end
                        if #left_button_parts > 0 then
                            log.warn("F7 left panel raw buttons: " .. table.concat(left_button_parts, " | "))
                        else
                            log.warn("F7 left panel raw buttons: none")
                        end

                        local left_text_rows = {}
                        for index, item in ipairs(snapshot_texts) do
                            local text = trim_text(item and item.text)
                            local x_pos = tonumber(item and item.x)
                            local y_pos = tonumber(item and item.y)
                            if text ~= ""
                                and x_pos ~= nil and y_pos ~= nil
                                and x_pos >= 0 and x_pos <= 620
                                and y_pos >= 120 and y_pos <= 430
                            then
                                left_text_rows[#left_text_rows + 1] = {
                                    index = index,
                                    text = text,
                                    x = x_pos,
                                    y = y_pos,
                                    name = tostring(item and item.name or ""),
                                    fullname = tostring(item and (item.Fullname or item.fullname) or "")
                                }
                            end
                        end
                        table.sort(left_text_rows, function(a, b)
                            if a.y ~= b.y then
                                return a.y < b.y
                            end
                            return a.x < b.x
                        end)

                        local text_parts = {}
                        for index = 1, math.min(14, #left_text_rows) do
                            local item = left_text_rows[index]
                            text_parts[#text_parts + 1] = string.format(
                                "[%d]src=%d text=%s x=%.1f y=%.1f name=%s fullname=%s",
                                index,
                                tonumber(item.index) or 0,
                                f7_short_text(item.text, 72),
                                tonumber(item.x) or 0,
                                tonumber(item.y) or 0,
                                f7_short_text(item.name, 40),
                                f7_short_text(item.fullname, 72)
                            )
                        end
                        if #text_parts > 0 then
                            log.warn("F7 left panel texts: " .. table.concat(text_parts, " | "))
                        else
                            log.warn("F7 left panel texts: none")
                        end

                        return logged
                    end
                    local player_info, player_info_err = nav.player_info()
                    log_f7_nearby_portals(x, y)
                    if type(map_ui) == "table" then
                        local parts = {
                            string.format("F7 current pos: %.2f, %.2f, %.2f", x, y, z or 0)
                        }
                        if current_task_name ~= "" then
                            parts[#parts + 1] = "task=" .. current_task_name
                        end
                        if current_task_detail ~= "" then
                            parts[#parts + 1] = "detail=" .. current_task_detail
                        end
                        if type(map_ui.current_map) == "table" then
                            parts[#parts + 1] = "map=" .. tostring(map_ui.current_map.text or "")
                        end
                        if type(map_ui.monster_level) == "table" then
                            parts[#parts + 1] = "monster_level=" .. tostring(map_ui.monster_level.text or "")
                        end
                        if type(map_ui.remaining_enemies) == "table" then
                            parts[#parts + 1] = "remaining_enemies=" .. tostring(map_ui.remaining_enemies.text or "")
                        end
                        if type(task_panel) == "table" and type(task_panel.tasks) == "table" and #task_panel.tasks > 0 then
                            parts[#parts + 1] = "visible_tasks=" .. tostring(#task_panel.tasks)
                        end
                        log.info(table.concat(parts, " | "))
                        if type(player_info) == "table" then
                            local level_candidates = summarize_level_candidates(player_info)
                            if level_candidates ~= "" then
                                log.info("F7 player info level candidates: " .. level_candidates)
                            end
                            log.info("F7 player info shallow: " .. summarize_table_shallow(player_info, 32))
                        else
                            log.warn("F7 read player info failed: " .. tostring(player_info_err))
                        end
                        local ok_level_texts, level_text_summary, best_level_text = pcall(summarize_level_text_candidates, snapshot, 4)
                        if not ok_level_texts then
                            log.warn("F7 level text scan failed: " .. tostring(level_text_summary))
                        elseif level_text_summary ~= "" then
                            log.info("F7 level text candidates: " .. level_text_summary)
                            if type(best_level_text) == "table" then
                                log.info(string.format(
                                    "F7 level text best: text=%s level=%s progress=%s x=%s y=%s name=%s fullname=%s score=%d",
                                    tostring(best_level_text.text or ""),
                                    tostring(best_level_text.level or ""),
                                    tostring(best_level_text.progress or ""),
                                    tostring(best_level_text.x or ""),
                                    tostring(best_level_text.y or ""),
                                    tostring(best_level_text.name or ""),
                                    tostring(best_level_text.fullname or ""),
                                    tonumber(best_level_text.score) or 0
                                ))
                            end
                        else
                            log.warn("F7 level text candidates: none")
                        end
                        if type(map_ui.current_map) ~= "table"
                            and type(map_ui.debug_candidates) == "table"
                            and #map_ui.debug_candidates > 0
                        then
                            log.warn("F7 current map candidates: " .. table.concat(map_ui.debug_candidates, " | "))
                        end
                        if log_f7_task_panel_entries() then
                            -- Printed above.
                        elseif log_f7_task_panel_debug() then
                            -- Printed above.
                        elseif snapshot == nil then
                            log.warn("F7 read task panel failed: " .. tostring(task_panel_err or snapshot_err))
                        end
                    else
                        local parts = {
                            string.format("F7 current pos: %.2f, %.2f, %.2f", x, y, z or 0)
                        }
                        if current_task_name ~= "" then
                            parts[#parts + 1] = "task=" .. current_task_name
                        end
                        if current_task_detail ~= "" then
                            parts[#parts + 1] = "detail=" .. current_task_detail
                        end
                        log.info(table.concat(parts, " | "))
                        if type(player_info) == "table" then
                            local level_candidates = summarize_level_candidates(player_info)
                            if level_candidates ~= "" then
                                log.info("F7 player info level candidates: " .. level_candidates)
                            end
                            log.info("F7 player info shallow: " .. summarize_table_shallow(player_info, 32))
                        else
                            log.warn("F7 read player info failed: " .. tostring(player_info_err))
                        end
                        local ok_level_texts, level_text_summary, best_level_text = pcall(summarize_level_text_candidates, snapshot, 4)
                        if not ok_level_texts then
                            log.warn("F7 level text scan failed: " .. tostring(level_text_summary))
                        elseif level_text_summary ~= "" then
                            log.info("F7 level text candidates: " .. level_text_summary)
                            if type(best_level_text) == "table" then
                                log.info(string.format(
                                    "F7 level text best: text=%s level=%s progress=%s x=%s y=%s name=%s fullname=%s score=%d",
                                    tostring(best_level_text.text or ""),
                                    tostring(best_level_text.level or ""),
                                    tostring(best_level_text.progress or ""),
                                    tostring(best_level_text.x or ""),
                                    tostring(best_level_text.y or ""),
                                    tostring(best_level_text.name or ""),
                                    tostring(best_level_text.fullname or ""),
                                    tonumber(best_level_text.score) or 0
                                ))
                            end
                        else
                            log.warn("F7 level text candidates: none")
                        end
                        log.warn("F7 read current map failed: " .. tostring(map_err))
                        if log_f7_task_panel_entries() then
                            -- Printed above.
                        elseif log_f7_task_panel_debug() then
                            -- Printed above.
                        elseif snapshot == nil then
                            log.warn("F7 read task panel failed: " .. tostring(task_panel_err or snapshot_err))
                        end
                    end
                else
                    log.warn("F7 read position failed: " .. tostring(pos_err))
                end
            end
        end

        if pressed_once(HOTKEY_F8) then
            if not initialized then
                log.warn("F8 cursor pos unavailable: Torch API not ready yet")
            else
                local cursor_ok, cursor_err = avepoint_hotkey_print_cursor_client_pos()
                if not cursor_ok then
                    log.warn("F8 cursor pos unavailable: " .. tostring(cursor_err))
                end
            end
        end

        if pressed_once(HOTKEY_F9) then
            local ok, err = avepoint_hotkey_start_level_up_maintenance_test("skill", 8)
            if not ok then
                log.warn("F9 level-up maintenance test unavailable: " .. tostring(err))
            end
        end

        if pressed_once(HOTKEY_F3) then
            if not initialized then
                local attach_target = avepoint_hotkey_attach_target_pid()
                local init_ok, init_err = avepoint_wait_for_torch_init(3500, attach_target, MODE)
                if not init_ok then
                    log.warn("F3 EnumCImage dump unavailable: Torch API init failed: " .. tostring(init_err))
                end
            end
            if initialized then
                local dump_ok, dump_err = avepoint_hotkey_dump_visible_image_controls("F3")
                if not dump_ok then
                    log.error("F3 EnumCImage dump failed: " .. tostring(dump_err))
                end
            end
        end

        if pressed_once(HOTKEY_F4) then
            if state.running then
                log.info("F4 target preview ignored while automation is running")
            elseif state.f6_loop_active == true then
                log.info("F4 target preview ignored while F6 3-round loop is active")
            elseif not initialized then
                log.warn("Torch API not ready yet")
            else
                local preview_ok, result = avepoint_hotkey_preview_target()
                if preview_ok then
                    if tostring(result.preview_mode or "") == "text_distance" then
                        log.info(string.format(
                            "F4 target preview: mode=%s kind=%s addr=%s name=%s text=%s fullname=%s x=%s y=%s anchor_addr=%s anchor_text=%s anchor_pos=(%.2f, %.2f) distance=%.6f delta=%.6f button_index=%s text_index=%s",
                            tostring(result.preview_mode or ""),
                            tostring(result.kind or ""),
                            avepoint_format_addr_hex(result.addr),
                            tostring(result.name or ""),
                            tostring(result.text or ""),
                            tostring(result.fullname or ""),
                            tostring(result.x or ""),
                            tostring(result.y or ""),
                            avepoint_format_addr_hex(result.anchor_addr),
                            tostring(result.anchor_text or ""),
                            tonumber(result.anchor_x) or 0,
                            tonumber(result.anchor_y) or 0,
                            tonumber(result.distance) or 0,
                            tonumber(result.delta) or 0,
                            tostring(result.button_index or ""),
                            tostring(result.text_index or "")
                        ))
                    else
                        log.info(string.format(
                            "F4 target preview: mode=%s kind=%s addr=%s name=%s text=%s fullname=%s x=%s y=%s related_text=%s related_name=%s related_distance=%s hint_distance=%s",
                            tostring(result.preview_mode or ""),
                            tostring(result.kind or ""),
                            avepoint_format_addr_hex(result.addr),
                            tostring(result.name or ""),
                            tostring(result.text or ""),
                            tostring(result.fullname or ""),
                            tostring(result.x or ""),
                            tostring(result.y or ""),
                            tostring(result.related_text or ""),
                            tostring(result.related_name or ""),
                            tostring(result.related_distance or ""),
                            tostring(result.hint_distance or "")
                        ))
                    end
                else
                    log.error("F4 target preview failed: " .. tostring(result))
                end
            end
        end

        if pressed_once(HOTKEY_F10) and not hotkey.is_pressed(HOTKEY_EXIT_CTRL) then
            if state.running then
                log.info("F10 cursor probe ignored while automation is running")
            elseif state.f6_loop_active == true then
                log.info("F10 cursor probe ignored while F6 3-round loop is active")
            elseif not initialized then
                log.warn("Torch API not ready yet")
            else
                local cursor_ok, cursor_err = avepoint_hotkey_print_cursor_client_pos("F10")
                if not cursor_ok then
                    log.warn("F10 cursor pos unavailable: " .. tostring(cursor_err))
                end
                local f10_ok, result = avepoint_hotkey_preview_cursor_probe_target()
                if f10_ok then
                    log.info(string.format(
                        "F10 target matched: kind=%s addr=%s name=%s text=%s fullname=%s x=%s y=%s related_text=%s related_name=%s related_distance=%s hint_distance=%s source=%s",
                        tostring(result.kind or ""),
                        avepoint_format_addr_hex(result.addr),
                        tostring(result.name or ""),
                        tostring(result.text or ""),
                        tostring(result.fullname or ""),
                        tostring(result.x or ""),
                        tostring(result.y or ""),
                        tostring(result.related_text or ""),
                        tostring(result.related_name or ""),
                        tostring(result.related_distance or ""),
                        tostring(result.hint_distance or ""),
                        tostring(result.source or "")
                    ))
                    log.info("F10 locator step: " .. tostring(result.step_snippet or ""))
                else
                    log.error("F10 target inspect failed: " .. tostring(result))
                end
            end
        end

        if pressed_once(HOTKEY_F11) or pressed_once(HOTKEY_F11_ALT) then
            if state.running then
                log.info("F11 cursor click ignored while automation is running")
            elseif state.f6_loop_active == true then
                log.info("F11 cursor click ignored while F6 3-round loop is active")
            elseif not initialized then
                log.warn("Torch API not ready yet")
            else
                local cursor_ok, cursor_err = avepoint_hotkey_print_cursor_client_pos("F11")
                if not cursor_ok then
                    log.warn("F11 cursor pos unavailable: " .. tostring(cursor_err))
                end
                local f11_ok, result = avepoint_hotkey_click_cursor_probe_target()
                if f11_ok then
                    log.info(string.format(
                        "F11 target clicked: kind=%s addr=%s name=%s text=%s fullname=%s x=%s y=%s related_text=%s related_name=%s related_distance=%s hint_distance=%s source=%s",
                        tostring(result.kind or ""),
                        avepoint_format_addr_hex(result.addr),
                        tostring(result.name or ""),
                        tostring(result.text or ""),
                        tostring(result.fullname or ""),
                        tostring(result.x or ""),
                        tostring(result.y or ""),
                        tostring(result.related_text or ""),
                        tostring(result.related_name or ""),
                        tostring(result.related_distance or ""),
                        tostring(result.hint_distance or ""),
                        tostring(result.source or "")
                    ))
                    log.info("F11 locator step: " .. tostring(result.step_snippet or ""))
                else
                    log.error("F11 target click failed: " .. tostring(result))
                end
            end
        end

        if pressed_once(HOTKEY_EXIT) and not hotkey.is_pressed(HOTKEY_EXIT_CTRL) then
            if state.running then
                log.info("F12 raw API dump ignored while automation is running")
            elseif state.f6_loop_active == true then
                log.info("F12 raw API dump ignored while F6 3-round loop is active")
            elseif not initialized then
                log.warn("Torch API not ready yet")
            else
                local hotkey_ok, result = avepoint_hotkey_dump_cursor_api_raw()
                if hotkey_ok then
                    if type(result) == "table" then
                        local selected_identity = tostring(result.Fullname or result.fullname or result.name or "")
                        if selected_identity:lower():find("taskitem_c.widgettree.taskbtn", 1, true) ~= nil then
                            _G.AVEPOINT_MAIN_TASK_BUTTON_LOCATOR = {
                                fullname = selected_identity,
                                x = tonumber(result.x),
                                y = tonumber(result.y),
                                related_text = tostring(result.rel1_text or ""),
                                related_dx = tonumber(result.rel1_dx),
                                related_dy = tonumber(result.rel1_dy),
                                related_tolerance = 42,
                                max_distance = 140,
                                include_patterns = {
                                    "taskitem_c.widgettree.taskbtn"
                                },
                                source = "F12 GetCurrentSelected locator",
                                captured_addr = result.addr,
                                cached_at = type(sys) == "table" and type(sys.time) == "function" and sys.time() or nil
                            }
                            log.info(string.format(
                                "F12 cached main task TaskBtn locator | fullname=%s x=%.2f y=%.2f related=%s dx=%s dy=%s captured_addr=%s",
                                selected_identity,
                                tonumber(result.x) or 0,
                                tonumber(result.y) or 0,
                                tostring(result.rel1_text or ""),
                                tostring(result.rel1_dx or ""),
                                tostring(result.rel1_dy or ""),
                                tostring(result.addr or "")
                            ))
                        end
                        log.info(string.format(
                            "F12 GetCurrentSelected raw dump complete | addr=%s name=%s x=%.2f y=%.2f text=%s",
                            tostring(result.addr or ""),
                            tostring(result.Fullname or result.name or ""),
                            tonumber(result.x) or 0,
                            tonumber(result.y) or 0,
                            tostring(result.text or "")
                        ))
                    else
                        log.info("F12 GetCurrentSelected raw dump complete")
                    end
                else
                    log.error("F12 GetCurrentSelected raw dump failed: " .. tostring(result))
                end
            end
        end

        if pressed_once(HOTKEY_START)
            or pressed_once(0x78, HOTKEY_EXIT_CTRL)
            or pressed_once(HOTKEY_START_BRACKET)
        then
            if state.f6_loop_active == true then
                log.info("F6 3-round loop is active; start hotkey ignored")
            elseif F9_LEVEL_UP_MAINTENANCE_TEST.active == true then
                log.info("F9 level-up maintenance test is active; start hotkey ignored")
            elseif not initialized then
                log.warn("Torch API not ready yet")
            elseif state.running then
                log.info(string.format(
                    "AvePoint automation already running | mode=%s(%s)",
                    tostring(state.task_mode_name or TASK_MODE.label(TASK_MODE.DEFAULT)),
                    tostring(state.task_mode_id or TASK_MODE.DEFAULT)
                ))
            else
                TASK_MODE.last_start_ok, TASK_MODE.last_start_name_or_err, TASK_MODE.last_start_id_or_err =
                    TASK_MODE.start_selected()
                if not TASK_MODE.last_start_ok then
                    log.error("AvePoint automation start failed: " .. tostring(TASK_MODE.last_start_name_or_err))
                    stop_automation()
                else
                    log.info(string.format(
                        "AvePoint automation started | mode=%s(%s)",
                        tostring(TASK_MODE.last_start_name_or_err),
                        tostring(TASK_MODE.last_start_id_or_err)
                    ))
                end
            end
        end

        local loop_now = sys.time()
        avepoint_hotkey_update_level_up_maintenance_test(loop_now)

        if state.f6_loop_active == true then
            local f6_ok, f6_err = update_f6_loop(loop_now)
            if not f6_ok then
                log.error(tostring(f6_err))
            end
            loop_now = sys.time()
        end

        if initialized and state.running then
            TASK_MODE.last_step_ok, TASK_MODE.last_step_err = TASK_MODE.update_selected(loop_now)
            if not TASK_MODE.last_step_ok then
                if state.f6_loop_active == true then
                    local _, f6_err = fail_f6_loop("AvePoint automation failed: " .. tostring(TASK_MODE.last_step_err))
                    log.error(tostring(f6_err))
                else
                    log.error(string.format(
                        "AvePoint automation failed | mode=%s(%s) err=%s",
                        tostring(state.task_mode_name or TASK_MODE.label(TASK_MODE.DEFAULT)),
                        tostring(state.task_mode_id or TASK_MODE.DEFAULT),
                        tostring(TASK_MODE.last_step_err)
                    ))
                    stop_automation("AvePoint automation stopped by error")
                end
            end
        end

        sys.sleep(POLL_INTERVAL_MS)
    end

    avepoint_hotkey_stop_level_up_maintenance_test("hotkey exit")
    stop_automation()

    if started_hotkey and hotkey.is_running() then
        hotkey.stop()
    end

    remove_hotkey_owner_lock()
end

