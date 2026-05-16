local M = {}

M.VERSION = 1

M.DEFAULT_CONFIG = {
    enabled = true,
    execute_ui = true,
    trigger_after_auto_equip = true,
    priority_over_level_up = true,
    open_bag_key_vk = 0x42,
    close_bag_key_vk = 0x42,
    bag_open_wait_ms = 650,
    bag_close_wait_ms = 350,
    bag_verify_attempts = 3,
    step_wait_ms = 350,
    confirm_wait_ms = 650,
    button_retry_attempts = 5,
    button_retry_wait_ms = 300,
    recycle_button_pattern = "pcbag_c.widgettree.pcbagmain.widgettree.pcuigridlistview.widgettree.uibutton_recycle",
    recycle_execute_button_pattern = "pcbag_c.widgettree.pcbagmain.widgettree.pcuigridlistview.widgettree.uibutton_recycle",
    rarity_filter_button_pattern = "pcbagfilterrarityitem.widgettree.selectbtn0",
    confirm_button_pattern = "confirmv2_c.widgettree.combuttonv2.widgettree.btn",
    random_click_count = 1,
    random_click_rect = {
        min_x = 981,
        max_x = 1274,
        min_y = 87,
        max_y = 240
    }
}

local function clone_table(value)
    if type(value) ~= "table" then
        return value
    end
    local out = {}
    for k, v in pairs(value) do
        out[k] = clone_table(v)
    end
    return out
end

local function merge_into(dst, src)
    if type(src) ~= "table" then
        return dst
    end
    for k, v in pairs(src) do
        if type(v) == "table" and type(dst[k]) == "table" then
            merge_into(dst[k], v)
        else
            dst[k] = clone_table(v)
        end
    end
    return dst
end

function M.config(user_cfg)
    return merge_into(clone_table(M.DEFAULT_CONFIG), user_cfg)
end

local function trim(value)
    return tostring(value or ""):gsub("^%s+", ""):gsub("%s+$", "")
end

local function identity_of(item)
    if type(item) ~= "table" then
        return ""
    end
    return trim(item.name or item.Fullname or item.fullname or ""):lower()
end

local function now_ms(ctx)
    local sys_api = type(ctx) == "table" and ctx.sys or sys
    if type(sys_api) == "table" and type(sys_api.time) == "function" then
        return tonumber(sys_api.time()) or 0
    end
    return 0
end

local function sleep_ms(ctx, delay_ms)
    local sys_api = type(ctx) == "table" and ctx.sys or sys
    if type(sys_api) == "table" and type(sys_api.sleep) == "function" then
        sys_api.sleep(math.max(0, tonumber(delay_ms) or 0))
    end
end

local function log_api(ctx, deps)
    if type(deps) == "table" and type(deps.logger) == "function" then
        return deps.logger(ctx)
    end
    if type(ctx) == "table" and type(ctx.log) == "table" then
        return ctx.log
    end
    return log
end

local function log_line(ctx, deps, level, message)
    local api = log_api(ctx, deps)
    if type(api) ~= "table" then
        return
    end
    local fn = type(api[level]) == "function" and api[level] or api.info
    if type(fn) == "function" then
        fn(message)
    end
end

local function safe_call(fn, ...)
    if type(fn) ~= "function" then
        return false, "target is not callable"
    end
    return pcall(fn, ...)
end

local function is_visible_button(button)
    local x = tonumber(type(button) == "table" and (button.x or button.X))
    local y = tonumber(type(button) == "table" and (button.y or button.Y))
    return x ~= nil and y ~= nil and x > 0 and y > 0
end

local function is_bag_open(snapshot)
    if type(snapshot) ~= "table" or type(snapshot.buttons) ~= "table" then
        return false
    end
    for _, button in ipairs(snapshot.buttons) do
        local name = identity_of(button)
        if is_visible_button(button)
            and (
                name:find("pcbag_c.widgettree.pcbagmain", 1, true) ~= nil
                or name:find("pcuibagequipitem.widgettree.pcbagequipslot", 1, true) ~= nil
                or name:find("pcuigridlistview.widgettree.uibutton_recycle", 1, true) ~= nil
            )
        then
            return true
        end
    end
    return false
end

local function enum_ui(nav_mod)
    if type(nav_mod) ~= "table" or type(nav_mod.enum_ui) ~= "function" then
        return nil, "nav.enum_ui unavailable"
    end
    local ok, snapshot_or_err = safe_call(nav_mod.enum_ui)
    if not ok then
        return nil, tostring(snapshot_or_err or "enum_ui failed")
    end
    if type(snapshot_or_err) ~= "table" then
        return nil, "enum_ui returned non-table"
    end
    return snapshot_or_err, nil
end

local function press_bag_key(ctx, deps, current_time, vk, label)
    if type(deps) ~= "table" or type(deps.press_key) ~= "function" then
        return false, "press_key hook unavailable"
    end
    return deps.press_key(ctx, current_time, vk, label)
end

local function ensure_bag_open(ctx, deps, nav_mod, cfg, current_time)
    local snapshot, snapshot_err = enum_ui(nav_mod)
    if type(snapshot) == "table" and is_bag_open(snapshot) then
        return true, snapshot
    end

    local attempts = math.max(1, math.floor(tonumber(cfg.bag_verify_attempts) or 3))
    local last_err = snapshot_err
    for _ = 1, attempts do
        local pressed, press_err = press_bag_key(ctx, deps, current_time, tonumber(cfg.open_bag_key_vk) or 0x42, "recycle open bag")
        if not pressed then
            return false, nil, press_err or "open bag failed"
        end
        sleep_ms(ctx, cfg.bag_open_wait_ms)
        snapshot, snapshot_err = enum_ui(nav_mod)
        last_err = snapshot_err
        if type(snapshot) == "table" and is_bag_open(snapshot) then
            return true, snapshot
        end
    end

    return false, snapshot, last_err or "bag open verification failed"
end

local function close_bag(ctx, deps, nav_mod, cfg, current_time)
    local snapshot, snapshot_err = enum_ui(nav_mod)
    if type(snapshot) == "table" and not is_bag_open(snapshot) then
        return true
    end

    local attempts = math.max(1, math.floor(tonumber(cfg.bag_verify_attempts) or 3))
    local last_err = snapshot_err
    for _ = 1, attempts do
        local pressed, press_err = press_bag_key(ctx, deps, current_time, tonumber(cfg.close_bag_key_vk) or 0x42, "recycle close bag")
        if not pressed then
            return false, press_err or "close bag failed"
        end
        sleep_ms(ctx, cfg.bag_close_wait_ms)
        snapshot, last_err = enum_ui(nav_mod)
        if type(snapshot) == "table" and not is_bag_open(snapshot) then
            return true
        end
    end

    return false, last_err or "bag close verification failed"
end

local function find_button(snapshot, pattern)
    local needle = tostring(pattern or ""):lower()
    if needle == "" or type(snapshot) ~= "table" or type(snapshot.buttons) ~= "table" then
        return nil
    end

    local best = nil
    for _, button in ipairs(snapshot.buttons) do
        local name = identity_of(button)
        if is_visible_button(button) and name:find(needle, 1, true) ~= nil then
            if best == nil then
                best = button
            else
                local best_name = identity_of(best)
                if #name < #best_name then
                    best = button
                end
            end
        end
    end
    return best
end

local function button_addr(button)
    local addr = type(button) == "table" and (button.addr or button.address)
    return tonumber(addr)
end

local function click_button(ctx, deps, nav_mod, cfg, snapshot, pattern, label, required)
    local button = find_button(snapshot, pattern)
    if type(button) ~= "table" and required ~= false then
        local attempts = math.max(1, math.floor(tonumber(cfg.button_retry_attempts) or 1))
        for attempt = 1, attempts do
            sleep_ms(ctx, cfg.button_retry_wait_ms)
            local retry_snapshot = enum_ui(nav_mod)
            if type(retry_snapshot) == "table" then
                snapshot = retry_snapshot
                button = find_button(snapshot, pattern)
                if type(button) == "table" then
                    log_line(ctx, deps, "info", string.format(
                        "[Leveling] recycle button found after retry | label=%s attempt=%d pattern=%s",
                        tostring(label or ""),
                        attempt,
                        tostring(pattern or "")
                    ))
                    break
                end
            end
        end
    end

    if type(button) ~= "table" then
        if required == false then
            return false, snapshot, "missing_optional"
        end
        return false, snapshot, "button not found: " .. tostring(label)
    end

    local addr = button_addr(button)
    if type(nav_mod.control_click) ~= "function" then
        return false, snapshot, "nav.control_click unavailable"
    end
    local ok, click_err = nav_mod.control_click(addr)
    if not ok then
        return false, snapshot, click_err or ("control_click failed: " .. tostring(label))
    end

    sleep_ms(ctx, cfg.step_wait_ms)
    local next_snapshot, enum_err = enum_ui(nav_mod)
    log_line(ctx, deps, "info", string.format(
        "[Leveling] recycle button clicked | label=%s addr=%s x=%.1f y=%.1f",
        tostring(label or ""),
        addr ~= nil and string.format("0x%X", addr) or "",
        tonumber(button.x or button.X) or 0,
        tonumber(button.y or button.Y) or 0
    ))
    return true, type(next_snapshot) == "table" and next_snapshot or snapshot, enum_err
end

local function random_between(min_value, max_value)
    min_value = tonumber(min_value) or 0
    max_value = tonumber(max_value) or min_value
    if max_value < min_value then
        min_value, max_value = max_value, min_value
    end
    return min_value + math.random() * (max_value - min_value)
end

local function random_rect_clicks(ctx, deps, nav_mod, hwnd, cfg)
    if type(nav_mod.click_window_to_move) ~= "function" then
        return false, "nav.click_window_to_move unavailable"
    end

    local rect = type(cfg.random_click_rect) == "table" and cfg.random_click_rect or {}
    local count = math.max(0, math.floor(tonumber(cfg.random_click_count) or 0))
    for index = 1, count do
        local x = random_between(rect.min_x, rect.max_x)
        local y = random_between(rect.min_y, rect.max_y)
        local ok, click_err = nav_mod.click_window_to_move(hwnd, x, y, {
            button = "left",
            delay = 50,
            wait = false
        })
        if not ok then
            return false, click_err or "random rect click failed"
        end
        log_line(ctx, deps, "info", string.format(
            "[Leveling] recycle random rect click | index=%d x=%.1f y=%.1f",
            index,
            x,
            y
        ))
        sleep_ms(ctx, cfg.step_wait_ms)
    end
    return true
end

function M.perform_recycle(ctx, deps, runtime, cfg, current_time)
    local nav_mod = type(deps) == "table" and deps.nav
    if type(nav_mod) ~= "table" then
        return false, "nav unavailable"
    end
    if type(nav_mod.window_hwnd) ~= "function" then
        return false, "nav.window_hwnd unavailable"
    end
    if type(nav_mod.control_click) ~= "function" then
        return false, "nav.control_click unavailable"
    end

    local hwnd, hwnd_err = nav_mod.window_hwnd()
    if not hwnd then
        return false, hwnd_err or "game window not found"
    end

    local opened, snapshot, open_err = ensure_bag_open(ctx, deps, nav_mod, cfg, current_time)
    if not opened then
        return false, open_err or "bag did not open"
    end

    local clicked, err
    clicked, snapshot, err = click_button(ctx, deps, nav_mod, cfg, snapshot, cfg.recycle_button_pattern, "recycle_open", true)
    if not clicked then
        close_bag(ctx, deps, nav_mod, cfg, current_time)
        return false, err
    end

    clicked, snapshot, err = click_button(ctx, deps, nav_mod, cfg, snapshot, cfg.rarity_filter_button_pattern, "rarity_filter_0", true)
    if not clicked then
        close_bag(ctx, deps, nav_mod, cfg, current_time)
        return false, err
    end

    clicked, snapshot, err = click_button(
        ctx,
        deps,
        nav_mod,
        cfg,
        snapshot,
        cfg.recycle_execute_button_pattern or cfg.recycle_button_pattern,
        "recycle_execute",
        true
    )
    if not clicked then
        close_bag(ctx, deps, nav_mod, cfg, current_time)
        return false, err
    end

    sleep_ms(ctx, cfg.confirm_wait_ms)
    snapshot = enum_ui(nav_mod) or snapshot
    clicked, snapshot, err = click_button(ctx, deps, nav_mod, cfg, snapshot, cfg.confirm_button_pattern, "confirm", false)
    local confirm_clicked = clicked == true
    if err ~= nil and tostring(err) ~= "missing_optional" then
        log_line(ctx, deps, "warn", "[Leveling] recycle confirm skipped | err=" .. tostring(err))
    end

    local random_ok, random_err = random_rect_clicks(ctx, deps, nav_mod, hwnd, cfg)
    if not random_ok then
        log_line(ctx, deps, "warn", "[Leveling] recycle random rect click failed | err=" .. tostring(random_err or ""))
    end

    local closed, close_err = close_bag(ctx, deps, nav_mod, cfg, current_time)
    if not closed then
        return false, close_err or "bag close failed"
    end

    return true, {
        confirm_clicked = confirm_clicked,
        random_clicked = random_ok == true
    }
end

function M.maybe_handle(ctx, deps)
    deps = type(deps) == "table" and deps or {}
    local runtime = type(deps.runtime) == "table" and deps.runtime or {}
    local cfg = M.config(deps.config)
    if cfg.enabled ~= true or cfg.execute_ui ~= true then
        return false
    end

    local current_time = tonumber(deps.current_time) or now_ms(ctx)
    if runtime.recycle_active == true then
        return true
    end

    runtime.recycle_active = true
    if type(deps.release_inputs) == "function" then
        deps.release_inputs(ctx, current_time, true)
    end
    if type(deps.hold_navigation) == "function" then
        deps.hold_navigation(ctx, current_time, "recycle_maintenance")
    end

    local ok, handled, summary = safe_call(M.perform_recycle, ctx, deps, runtime, cfg, current_time)
    runtime.recycle_active = false

    if not ok then
        log_line(ctx, deps, "warn", "[Leveling] recycle maintenance crashed | err=" .. tostring(handled or ""))
        return true
    end
    if handled ~= true then
        log_line(ctx, deps, "warn", "[Leveling] recycle maintenance failed | err=" .. tostring(summary or ""))
        return true
    end

    log_line(ctx, deps, "info", string.format(
        "[Leveling] recycle maintenance complete | confirm=%s random_click=%s",
        tostring(type(summary) == "table" and summary.confirm_clicked or ""),
        tostring(type(summary) == "table" and summary.random_clicked or "")
    ))
    return true
end

return M
