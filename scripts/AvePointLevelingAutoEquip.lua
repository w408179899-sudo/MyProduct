local M = {}

M.VERSION = 1

M.DEFAULT_CONFIG = {
    enabled = true,
    execute_ui = true,
    trigger_after_post_combat_loot = true,
    priority_over_level_up = true,
    periodic_scan_enabled = false,
    after_loot_timeout_ms = 30000,
    scan_interval_ms = 45000,
    retry_ms = 12000,
    min_hp_ratio = 0.72,
    allow_low_hp_maintenance = false,
    safe_no_monster_ms = 1800,
    monster_guard_distance = 1000,
    monster_hard_block_distance = 300,
    nearby_monster_soft_observe_ms = 1800,
    nearby_monster_soft_resource_drop_epsilon = 1,
    open_bag_key_vk = 0x42,
    close_bag_key_vk = 0x42,
    close_bag_after_run = true,
    bag_open_wait_ms = 650,
    bag_close_wait_ms = 350,
    hover_wait_ms = 260,
    move_min_duration_ms = 80,
    move_max_duration_ms = 180,
    right_click_mouse_mode = "driver",
    right_click_foreground_wait_ms = 40,
    right_click_delay_ms = 50,
    equip_wait_ms = 650,
    identify_all_on_bag_open = true,
    identify_all_before_scan = true,
    identify_all_wait_ms = 800,
    identify_all_retry_attempts = 5,
    identify_all_retry_wait_ms = 300,
    identify_all_button_pattern = "pcbag_c.widgettree.pcbagmain.widgettree.pcuigridlistview.widgettree.uibutton_onekey",
    identify_all_button_fallback = {
        client_x = 1264.059570,
        client_y = 850.707092
    },
    bag_close_button_pattern = "pcbag_c.widgettree.pcbagmain.widgettree.uibutton_close",
    scan_max_items = 32,
    max_equips_per_run = 10,
    allow_damage_upgrade_when_survival_equal = true,
    min_survival_gain = 0,
    min_damage_gain = 0,
    bag_grid = {
        center_scan = true,
        first_center_x = 958,
        first_center_y = 570,
        last_center_x = 1392,
        last_center_y = 755,
        columns = 8,
        rows = 4,
        hover_jitter_px = 2,
        min_x = 880,
        max_x = 1395,
        min_y = 550,
        max_y = 790
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

local function text_of(item)
    if type(item) ~= "table" then
        return ""
    end
    return trim(item.text or "")
end

local function is_visible_button(button)
    local x = tonumber(type(button) == "table" and (button.x or button.X))
    local y = tonumber(type(button) == "table" and (button.y or button.Y))
    return x ~= nil and y ~= nil and x > 0 and y > 0
end

local function button_addr(button)
    local addr = type(button) == "table" and (button.addr or button.address)
    return tonumber(addr)
end

local function clamp(value, min_value, max_value)
    value = tonumber(value) or 0
    min_value = tonumber(min_value) or value
    max_value = tonumber(max_value) or value
    if value < min_value then
        return min_value
    end
    if value > max_value then
        return max_value
    end
    return value
end

local function rounded_index(value)
    return math.floor((tonumber(value) or 0) + 0.5)
end

local function small_jitter(radius)
    radius = math.max(0, tonumber(radius) or 0)
    if radius <= 0 then
        return 0
    end
    return (math.random() * 2 - 1) * radius
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

local function reset_safe_observe(runtime)
    if type(runtime) ~= "table" then
        return
    end
    runtime.auto_equip_soft_monster_since = 0
    runtime.auto_equip_soft_monster_resource = nil
    runtime.auto_equip_soft_monster_distance = nil
end

local function block(runtime, reason)
    if type(runtime) == "table" then
        runtime.auto_equip_safe_since = 0
        reset_safe_observe(runtime)
    end
    return false, reason
end

function M.safe_window(ctx, deps, runtime, cfg, current_time, player_x, player_y, hp_ratio, in_main_interface)
    runtime = type(runtime) == "table" and runtime or {}
    current_time = tonumber(current_time) or now_ms(ctx)

    if in_main_interface == false then
        local has_player_position = type(player_x) == "number" and type(player_y) == "number"
        if cfg.allow_position_available_without_main_interface ~= true or not has_player_position then
            return block(runtime, "not_main_interface")
        end
    end
    if type(hp_ratio) == "number"
        and hp_ratio < (tonumber(cfg.min_hp_ratio) or 0.72)
        and cfg.allow_low_hp_maintenance ~= true
    then
        return block(runtime, string.format("low_hp:%.2f", hp_ratio))
    end
    if runtime.task_info_stability_gate_active == true then
        return block(runtime, "task_info_stability_gate")
    end
    if current_time < (tonumber(runtime.task_update_wait_until) or 0) then
        return block(runtime, "task_update_wait")
    end
    if runtime.loading_transition_reacquire_pending == true then
        return block(runtime, "loading_transition_reacquire")
    end
    if type(deps) == "table"
        and type(deps.is_task_entry_action_active) == "function"
        and deps.is_task_entry_action_active(current_time)
    then
        return block(runtime, "task_entry_action")
    end
    if type(deps) == "table"
        and type(deps.is_task_combat_or_post_loot_active) == "function"
        and deps.is_task_combat_or_post_loot_active()
    then
        return block(runtime, "task_combat_or_post_loot")
    end
    if type(runtime.task_target) == "table" then
        return block(runtime, "task_navigation_active")
    end
    if runtime.pending_interaction_origin ~= nil then
        return block(runtime, "pending_interaction")
    end
    if runtime.route_point_action_dialogue_active_key ~= nil
        or runtime.route_point_action_objective_active_key ~= nil
        or runtime.route_point_action_route_active_key ~= nil
    then
        return block(runtime, "route_point_action_active")
    end
    if runtime.task_recipe_active_key ~= nil then
        return block(runtime, "task_recipe_active")
    end
    if type(deps) == "table" and type(deps.dialogue_block_reason) == "function" then
        local dialogue_reason = deps.dialogue_block_reason(ctx, current_time)
        if dialogue_reason ~= nil then
            return block(runtime, tostring(dialogue_reason))
        end
    end

    if type(deps) == "table" and type(deps.find_task_monsters) == "function" then
        local nearby_monsters = deps.find_task_monsters(ctx, current_time, player_x, player_y)
        if type(nearby_monsters) == "table" and type(nearby_monsters.nearest) == "table" then
            local nearest_distance = tonumber(nearby_monsters.nearest.distance) or math.huge
            local guard_distance = math.max(120, tonumber(cfg.monster_guard_distance) or 1000)
            local hard_distance = math.max(0, tonumber(cfg.monster_hard_block_distance) or 300)
            if nearest_distance <= guard_distance then
                if hard_distance > 0 and nearest_distance <= hard_distance then
                    return block(runtime, string.format("nearby_monster_hard:%.1f", nearest_distance))
                end

                local observe_ms = math.max(0, tonumber(cfg.nearby_monster_soft_observe_ms) or 0)
                if observe_ms <= 0 or type(deps.player_resource) ~= "function" then
                    return block(runtime, string.format("nearby_monster:%.1f", nearest_distance))
                end

                local resource = deps.player_resource(ctx, current_time, true)
                if type(resource) ~= "number" then
                    runtime.auto_equip_safe_since = 0
                    reset_safe_observe(runtime)
                    return false, string.format("nearby_monster_soft_no_resource:%.1f", nearest_distance)
                end

                local drop_epsilon = math.max(0, tonumber(cfg.nearby_monster_soft_resource_drop_epsilon) or 1)
                local since = tonumber(runtime.auto_equip_soft_monster_since) or 0
                local baseline = tonumber(runtime.auto_equip_soft_monster_resource)
                if since <= 0 or baseline == nil or resource < baseline - drop_epsilon then
                    runtime.auto_equip_safe_since = 0
                    runtime.auto_equip_soft_monster_since = current_time
                    runtime.auto_equip_soft_monster_resource = resource
                    runtime.auto_equip_soft_monster_distance = nearest_distance
                    return false, string.format("nearby_monster_soft_observe:%.1f", nearest_distance)
                end

                local elapsed = current_time - since
                if elapsed < observe_ms then
                    runtime.auto_equip_safe_since = 0
                    runtime.auto_equip_soft_monster_distance = nearest_distance
                    return false, string.format("nearby_monster_soft_observe:%.1f:%d", nearest_distance, math.max(0, observe_ms - elapsed))
                end

                reset_safe_observe(runtime)
                return true, "ready_soft_monster_passthrough"
            end
        end
    end
    reset_safe_observe(runtime)

    local safe_since = tonumber(runtime.auto_equip_safe_since) or 0
    if safe_since <= 0 then
        runtime.auto_equip_safe_since = current_time
        return false, "safe_window_settle"
    end

    local settle_ms = math.max(0, tonumber(cfg.safe_no_monster_ms) or 1800)
    if current_time - safe_since < settle_ms then
        return false, string.format("safe_window_settle:%d", math.max(0, settle_ms - (current_time - safe_since)))
    end

    return true, "ready"
end

local function is_bag_open(snapshot)
    if type(snapshot) ~= "table" or type(snapshot.buttons) ~= "table" then
        return false
    end
    for _, button in ipairs(snapshot.buttons) do
        local name = identity_of(button)
        if name:find("pcbag_c.widgettree.pcbagmain", 1, true) ~= nil then
            return true
        end
    end
    return false
end

local function parse_percent(text)
    local raw = trim(text)
    local value = raw:match("([%+%-]?%d+%.?%d*)%s*%%")
    if value == nil then
        value = raw:match("([%+%-]?%d+%.?%d*)")
    end
    if value == nil then
        return nil
    end
    return tonumber(value)
end

local function parse_compare(snapshot)
    local result = {
        damage = nil,
        survival = nil,
        item_name = nil,
        item_type = nil
    }
    if type(snapshot) ~= "table" or type(snapshot.texts) ~= "table" then
        return result
    end

    for _, item in ipairs(snapshot.texts) do
        local name = identity_of(item)
        local text = text_of(item)
        if name:find("tipweaponitem_c.widgettree.uitextblock", 1, true) ~= nil
            and result.item_name == nil
            and text ~= ""
        then
            result.item_name = text
        elseif name:find("tipstagitem_type.widgettree.uitextblock", 1, true) ~= nil
            and result.item_type == nil
            and text ~= ""
        then
            result.item_type = text
        elseif name:find("attrbutecompareitems", 1, true) ~= nil
            and name:find("text_attrvalue", 1, true) ~= nil
        then
            local value = parse_percent(text)
            if value ~= nil then
                if name:find("attrbuteitem_dps", 1, true) ~= nil then
                    result.damage = value
                elseif name:find("attrbuteitem_def", 1, true) ~= nil then
                    result.survival = value
                end
            end
        end
    end

    return result
end

local function should_equip(compare, cfg)
    local survival = tonumber(type(compare) == "table" and compare.survival)
    local damage = tonumber(type(compare) == "table" and compare.damage)
    local min_survival = tonumber(cfg.min_survival_gain) or 0
    local min_damage = tonumber(cfg.min_damage_gain) or 0

    if survival == nil and damage == nil then
        return false, "compare_missing"
    end
    if survival ~= nil and survival > min_survival then
        return true, "survival_gain"
    end
    if survival ~= nil and survival < min_survival then
        return false, "survival_loss"
    end
    if cfg.allow_damage_upgrade_when_survival_equal == true
        and (survival == nil or survival >= min_survival)
        and damage ~= nil
        and damage > min_damage
    then
        return true, "damage_gain_survival_equal"
    end
    return false, "no_upgrade"
end

local function has_unidentified_text(snapshot)
    if type(snapshot) ~= "table" or type(snapshot.texts) ~= "table" then
        return false
    end
    for _, item in ipairs(snapshot.texts) do
        local text = text_of(item)
        if text == "未鉴定" or text:find("未鉴定", 1, true) ~= nil then
            return true
        end
    end
    return false
end

local function find_visible_button(snapshot, pattern)
    local needle = tostring(pattern or ""):lower()
    if needle == "" or type(snapshot) ~= "table" or type(snapshot.buttons) ~= "table" then
        return nil
    end

    local best = nil
    for _, button in ipairs(snapshot.buttons) do
        local name = identity_of(button)
        if is_visible_button(button) and name:find(needle, 1, true) ~= nil then
            local best_name = identity_of(best)
            if best == nil or #name < #best_name then
                best = button
            end
        end
    end
    return best
end

local function click_identify_all(ctx, deps, nav_mod, hwnd, cfg, snapshot, character_id)
    local button = find_visible_button(snapshot, cfg.identify_all_button_pattern)
    if type(button) ~= "table" then
        local attempts = math.max(1, math.floor(tonumber(cfg.identify_all_retry_attempts) or 1))
        for attempt = 1, attempts do
            sleep_ms(ctx, tonumber(cfg.identify_all_retry_wait_ms) or 300)
            local retry_snapshot = nav_mod.enum_ui()
            if type(retry_snapshot) == "table" then
                snapshot = retry_snapshot
                button = find_visible_button(snapshot, cfg.identify_all_button_pattern)
                if type(button) == "table" then
                    log_line(ctx, deps, "info", string.format(
                        "[Leveling] auto-equip identify-all button found after retry | id=%s attempt=%d",
                        tostring(character_id or ""),
                        attempt
                    ))
                    break
                end
            end
        end
    end

    if type(button) ~= "table" then
        local fallback = type(cfg.identify_all_button_fallback) == "table" and cfg.identify_all_button_fallback or nil
        if type(fallback) ~= "table" then
            return false, snapshot, "identify-all button not found"
        end
        local fallback_snapshot = nav_mod.enum_ui()
        if type(fallback_snapshot) ~= "table" or not is_bag_open(fallback_snapshot) then
            return false, snapshot, "identify-all button not found, fallback=bag_not_open"
        end
        if type(nav_mod.click_window_to_move) ~= "function" then
            return false, fallback_snapshot, "identify-all button not found, fallback=click unavailable"
        end
        local x = tonumber(fallback.client_x)
        local y = tonumber(fallback.client_y)
        if x == nil or y == nil then
            return false, fallback_snapshot, "identify-all button not found, fallback=point missing"
        end
        local fallback_clicked, fallback_err = nav_mod.click_window_to_move(hwnd, x, y, {
            button = "left",
            delay = tonumber(cfg.right_click_delay_ms) or 50,
            wait = false
        })
        if not fallback_clicked then
            return false, fallback_snapshot, fallback_err or "identify-all fallback click failed"
        end
        sleep_ms(ctx, cfg.identify_all_wait_ms)
        local next_snapshot, enum_err = nav_mod.enum_ui()
        log_line(ctx, deps, "info", string.format(
            "[Leveling] auto-equip identify-all fallback left click | id=%s x=%.1f y=%.1f",
            tostring(character_id or ""),
            x,
            y
        ))
        return true, type(next_snapshot) == "table" and next_snapshot or fallback_snapshot, enum_err
    end

    local addr = button_addr(button)
    local clicked, click_err
    if type(nav_mod.control_click) == "function" and addr ~= nil then
        clicked, click_err = nav_mod.control_click(addr)
    elseif type(nav_mod.click_window_to_move) == "function" then
        clicked, click_err = nav_mod.click_window_to_move(hwnd, tonumber(button.x or button.X) or 0, tonumber(button.y or button.Y) or 0, {
            button = "left",
            delay = tonumber(cfg.right_click_delay_ms) or 50,
            wait = false
        })
    else
        return false, snapshot, "identify-all click unavailable"
    end
    if not clicked then
        return false, snapshot, click_err or "identify-all click failed"
    end

    sleep_ms(ctx, cfg.identify_all_wait_ms)
    local next_snapshot, enum_err = nav_mod.enum_ui()
    log_line(ctx, deps, "info", string.format(
        "[Leveling] auto-equip identify-all clicked | id=%s addr=%s x=%.1f y=%.1f",
        tostring(character_id or ""),
        addr ~= nil and string.format("0x%X", addr) or "",
        tonumber(button.x or button.X) or 0,
        tonumber(button.y or button.Y) or 0
    ))
    return true, type(next_snapshot) == "table" and next_snapshot or snapshot, enum_err
end

local function collect_bag_candidates(snapshot, cfg)
    local candidates = {}
    local seen = {}
    local grid = type(cfg.bag_grid) == "table" and cfg.bag_grid or {}
    local min_x = tonumber(grid.min_x) or 0
    local max_x = tonumber(grid.max_x) or math.huge
    local min_y = tonumber(grid.min_y) or 0
    local max_y = tonumber(grid.max_y) or math.huge

    if type(snapshot) ~= "table" or type(snapshot.buttons) ~= "table" then
        return candidates
    end

    if grid.center_scan == true then
        local columns = math.max(1, math.floor(tonumber(grid.columns) or 1))
        local rows = math.max(1, math.floor(tonumber(grid.rows) or 1))
        local first_center_x = tonumber(grid.first_center_x)
        local first_center_y = tonumber(grid.first_center_y)
        local last_center_x = tonumber(grid.last_center_x)
        local last_center_y = tonumber(grid.last_center_y)
        if first_center_x ~= nil and first_center_y ~= nil and last_center_x ~= nil and last_center_y ~= nil then
            local slot_buttons = {}
            local occupied_buttons = {}
            for _, button in ipairs(snapshot.buttons) do
                local name = identity_of(button)
                local is_grid_item = name:find("pcuigridlistviewitem_c.widgettree.pcequipitem.widgettree.equipitem.widgettree", 1, true) ~= nil
                    and name:find("pcuibagequipitem", 1, true) == nil
                local is_slot_button = is_grid_item and (
                    name:find("selectbtn", 1, true) ~= nil
                    or name:find("nillbtn", 1, true) ~= nil
                )
                if is_slot_button then
                    local x = tonumber(button.x or button.X)
                    local y = tonumber(button.y or button.Y)
                    if x ~= nil and y ~= nil and x >= min_x and x <= max_x and y >= min_y and y <= max_y then
                        local slot = { x = x, y = y, name = name, addr = button.addr or button.address }
                        slot_buttons[#slot_buttons + 1] = slot
                        if name:find("selectbtn", 1, true) ~= nil then
                            occupied_buttons[#occupied_buttons + 1] = slot
                        end
                    end
                end
            end

            if #slot_buttons > 0 and #occupied_buttons > 0 then
                local button_min_x = math.huge
                local button_max_x = -math.huge
                local button_min_y = math.huge
                local button_max_y = -math.huge
                for _, slot in ipairs(slot_buttons) do
                    button_min_x = math.min(button_min_x, slot.x)
                    button_max_x = math.max(button_max_x, slot.x)
                    button_min_y = math.min(button_min_y, slot.y)
                    button_max_y = math.max(button_max_y, slot.y)
                end

                local button_step_x = columns > 1 and ((button_max_x - button_min_x) / (columns - 1)) or 0
                local button_step_y = rows > 1 and ((button_max_y - button_min_y) / (rows - 1)) or 0
                local center_step_x = columns > 1 and ((last_center_x - first_center_x) / (columns - 1)) or 0
                local center_step_y = rows > 1 and ((last_center_y - first_center_y) / (rows - 1)) or 0
                local jitter = math.max(0, tonumber(grid.hover_jitter_px) or 0)

                for _, button in ipairs(occupied_buttons) do
                    local col = button_step_x > 0 and rounded_index((button.x - button_min_x) / button_step_x) or 0
                    local row = button_step_y > 0 and rounded_index((button.y - button_min_y) / button_step_y) or 0
                    col = clamp(col, 0, columns - 1)
                    row = clamp(row, 0, rows - 1)
                    if col >= 0 and col < columns and row >= 0 and row < rows then
                        local key = tostring(row) .. ":" .. tostring(col)
                        if seen[key] ~= true then
                            seen[key] = true
                            candidates[#candidates + 1] = {
                                x = first_center_x + col * center_step_x + small_jitter(jitter),
                                y = first_center_y + row * center_step_y + small_jitter(jitter),
                                row = row + 1,
                                col = col + 1,
                                name = button.name,
                                addr = button.addr
                            }
                        end
                    end
                end

                table.sort(candidates, function(a, b)
                    if (tonumber(a.row) or 0) ~= (tonumber(b.row) or 0) then
                        return (tonumber(a.row) or 0) < (tonumber(b.row) or 0)
                    end
                    return (tonumber(a.col) or 0) < (tonumber(b.col) or 0)
                end)

                local max_items = math.max(1, math.floor(tonumber(cfg.scan_max_items) or #candidates))
                while #candidates > max_items do
                    candidates[#candidates] = nil
                end
                return candidates
            end
        end
    end

    for _, button in ipairs(snapshot.buttons) do
        local name = identity_of(button)
        if name:find("pcuigridlistviewitem_c.widgettree.pcequipitem.widgettree.equipitem.widgettree.selectbtn", 1, true) ~= nil
            and name:find("pcuibagequipitem", 1, true) == nil
        then
            local x = tonumber(button.x or button.X)
            local y = tonumber(button.y or button.Y)
            if x ~= nil and y ~= nil and x >= min_x and x <= max_x and y >= min_y and y <= max_y then
                local key = tostring(math.floor(x + 0.5)) .. ":" .. tostring(math.floor(y + 0.5))
                if seen[key] ~= true then
                    seen[key] = true
                    candidates[#candidates + 1] = {
                        x = x,
                        y = y,
                        name = name,
                        addr = button.addr or button.address
                    }
                end
            end
        end
    end

    table.sort(candidates, function(a, b)
        local ay = tonumber(a.y) or 0
        local by = tonumber(b.y) or 0
        if math.abs(ay - by) > 1 then
            return ay < by
        end
        return (tonumber(a.x) or 0) < (tonumber(b.x) or 0)
    end)

    local max_items = math.max(1, math.floor(tonumber(cfg.scan_max_items) or #candidates))
    while #candidates > max_items do
        candidates[#candidates] = nil
    end

    return candidates
end

local function move_to_candidate(ctx, nav_mod, hwnd, candidate, cfg)
    return nav_mod.move_mouse_to_client(candidate.x, candidate.y, {
        hwnd = hwnd,
        mouse_mode = cfg.mouse_mode or "api",
        min_duration_ms = cfg.move_min_duration_ms,
        max_duration_ms = cfg.move_max_duration_ms,
        hover_ms = cfg.hover_wait_ms,
        set_foreground = true
    })
end

local function right_click_current_position(ctx, deps, hwnd, candidate, cfg)
    if type(human_mouse) == "table" and type(human_mouse.cancel_async_move) == "function" then
        human_mouse.cancel_async_move()
    end

    if type(mouse) ~= "table" then
        return false, "mouse api unavailable"
    end

    if type(wnd) == "table" and type(wnd.set_foreground) == "function" and hwnd ~= nil then
        pcall(wnd.set_foreground, hwnd)
        sleep_ms(ctx, tonumber(cfg.right_click_foreground_wait_ms) or 40)
    end

    if type(mouse.set_window) == "function" and hwnd ~= nil then
        pcall(mouse.set_window, hwnd)
    end

    local previous_mode = nil
    if type(mouse.get_mode) == "function" then
        local ok, mode = pcall(mouse.get_mode)
        if ok then
            previous_mode = mode
        end
    end

    local click_mode = tostring(cfg.right_click_mouse_mode or "driver")
    if click_mode ~= "" and type(mouse.set_mode) == "function" then
        local ok = mouse.set_mode(click_mode)
        if ok == false then
            return false, "mouse.set_mode(" .. click_mode .. ") failed"
        end
    end

    local click_delay_ms = math.max(1, tonumber(cfg.right_click_delay_ms) or 50)
    local clicked = false
    local click_err = nil
    local ok, err = pcall(function()
        if type(mouse.click) == "function" then
            local click_ok = mouse.click("right", click_delay_ms)
            if click_ok == false then
                error("mouse.click(right) failed")
            end
            return true
        end

        if type(mouse.down) == "function" and type(mouse.up) == "function" then
            local down_ok = mouse.down("right")
            if down_ok == false then
                error("mouse.down(right) failed")
            end
            sleep_ms(ctx, click_delay_ms)
            local up_ok = mouse.up("right")
            if up_ok == false then
                error("mouse.up(right) failed")
            end
            return true
        end

        error("mouse.click API is not available")
    end)

    if ok then
        clicked = err == true
    else
        click_err = err
    end

    if type(mouse.set_mode) == "function"
        and type(previous_mode) == "string"
        and previous_mode ~= ""
        and previous_mode ~= click_mode
    then
        pcall(mouse.set_mode, previous_mode)
    end

    if not clicked then
        return false, tostring(click_err or "mouse right click failed")
    end

    log_line(ctx, deps, "info", string.format(
        "[Leveling] auto-equip driver right click | cell=(%.1f, %.1f) mode=%s",
        tonumber(type(candidate) == "table" and candidate.x) or 0,
        tonumber(type(candidate) == "table" and candidate.y) or 0,
        click_mode
    ))
    return true
end

local function press_bag_key(ctx, deps, current_time, vk, label)
    if type(deps) ~= "table" or type(deps.press_key) ~= "function" then
        return false, "press_key hook unavailable"
    end
    return deps.press_key(ctx, current_time, vk, label)
end

function M.perform_scan(ctx, deps, runtime, cfg, current_time, character_id)
    local nav_mod = type(deps) == "table" and deps.nav
    if type(nav_mod) ~= "table" or type(nav_mod.enum_ui) ~= "function" then
        return false, "nav.enum_ui unavailable"
    end
    if type(nav_mod.window_hwnd) ~= "function" then
        return false, "nav.window_hwnd unavailable"
    end
    if type(nav_mod.move_mouse_to_client) ~= "function" then
        return false, "nav.move_mouse_to_client unavailable"
    end
    if type(nav_mod.click_window_to_move) ~= "function" then
        return false, "nav.click_window_to_move unavailable"
    end

    local hwnd, hwnd_err = nav_mod.window_hwnd()
    if not hwnd then
        return false, hwnd_err or "game window not found"
    end

    local snapshot, snapshot_err = nav_mod.enum_ui()
    if type(snapshot) ~= "table" then
        return false, snapshot_err or "enum_ui failed"
    end

    local opened_by_module = false
    local keep_bag_open_after_run = deps.keep_bag_open_after_run == true
    local function close_if_needed(force)
        if opened_by_module ~= true then
            return
        end
        if force ~= true and keep_bag_open_after_run then
            opened_by_module = false
            if type(runtime) == "table" then
                runtime.auto_equip_bag_opened_by_module = false
            end
            log_line(ctx, deps, "info", "[Leveling] auto-equip leaves bag open for recycle")
            return
        end
        if force ~= true and cfg.close_bag_after_run == false then
            opened_by_module = false
            if type(runtime) == "table" then
                runtime.auto_equip_bag_opened_by_module = false
            end
            return
        end
        local close_clicked = false
        local close_snapshot = nav_mod.enum_ui()
        local close_button = find_visible_button(close_snapshot, cfg.bag_close_button_pattern)
        if type(close_button) == "table" and type(nav_mod.control_click) == "function" then
            local addr = button_addr(close_button)
            if addr ~= nil then
                local clicked = nav_mod.control_click(addr)
                close_clicked = clicked == true
                if close_clicked then
                    log_line(ctx, deps, "info", string.format(
                        "[Leveling] auto-equip close button clicked | addr=%s x=%.1f y=%.1f",
                        string.format("0x%X", addr),
                        tonumber(close_button.x or close_button.X) or 0,
                        tonumber(close_button.y or close_button.Y) or 0
                    ))
                end
            end
        end
        if close_clicked ~= true then
            press_bag_key(ctx, deps, current_time, tonumber(cfg.close_bag_key_vk) or 0x42, "auto-equip close bag")
        end
        sleep_ms(ctx, cfg.bag_close_wait_ms)
        opened_by_module = false
        if type(runtime) == "table" then
            runtime.auto_equip_bag_opened_by_module = false
        end
    end

    if not is_bag_open(snapshot) then
        local pressed, press_err = press_bag_key(ctx, deps, current_time, tonumber(cfg.open_bag_key_vk) or 0x42, "auto-equip open bag")
        if not pressed then
            return false, press_err or "open bag failed"
        end
        opened_by_module = true
        if type(runtime) == "table" then
            runtime.auto_equip_bag_opened_by_module = true
        end
        sleep_ms(ctx, cfg.bag_open_wait_ms)
        snapshot, snapshot_err = nav_mod.enum_ui()
        if type(snapshot) ~= "table" then
            close_if_needed(true)
            return false, snapshot_err or "enum_ui after open bag failed"
        end
    end

    if not is_bag_open(snapshot) then
        close_if_needed(true)
        return false, "bag ui not detected"
    end

    local identified_all = false
    local identify_restarts = 0
    if cfg.identify_all_on_bag_open ~= false then
        local identify_clicked, identify_snapshot, identify_err = click_identify_all(
            ctx,
            deps,
            nav_mod,
            hwnd,
            cfg,
            snapshot,
            character_id
        )
        if identify_clicked then
            identified_all = true
            identify_restarts = identify_restarts + 1
            snapshot = type(identify_snapshot) == "table" and identify_snapshot or snapshot
        else
            log_line(ctx, deps, "warn", string.format(
                "[Leveling] auto-equip identify-all on open skipped | id=%s err=%s",
                tostring(character_id or ""),
                tostring(identify_err or "")
            ))
        end
    end

    local max_equips = math.max(1, math.floor(tonumber(cfg.max_equips_per_run) or 1))
    local equipped = 0
    local scanned = 0
    local skipped = 0

    for _ = 1, 2 do
        local candidates = collect_bag_candidates(snapshot, cfg)
        if #candidates == 0 then
            close_if_needed()
            return true, {
                scanned = scanned,
                equipped = equipped,
                skipped = skipped,
                identified_all = identify_restarts,
                reason = scanned == 0 and "no_candidates" or "no_candidates_after_identify"
            }
        end

        local restart_scan = false
        for _, candidate in ipairs(candidates) do
            scanned = scanned + 1
            local moved, move_err = move_to_candidate(ctx, nav_mod, hwnd, candidate, cfg)
            if not moved then
                skipped = skipped + 1
                log_line(ctx, deps, "warn", string.format(
                    "[Leveling] auto-equip hover failed | id=%s cell=(%.1f, %.1f) err=%s",
                    tostring(character_id or ""),
                    tonumber(candidate.x) or 0,
                    tonumber(candidate.y) or 0,
                    tostring(move_err or "")
                ))
            else
                local hover_snapshot, hover_err = nav_mod.enum_ui()
                if type(hover_snapshot) ~= "table" then
                    skipped = skipped + 1
                    log_line(ctx, deps, "warn", string.format(
                        "[Leveling] auto-equip hover enum failed | id=%s cell=(%.1f, %.1f) err=%s",
                        tostring(character_id or ""),
                        tonumber(candidate.x) or 0,
                        tonumber(candidate.y) or 0,
                        tostring(hover_err or "")
                    ))
                else
                    if cfg.identify_all_before_scan ~= false
                        and identified_all ~= true
                        and has_unidentified_text(hover_snapshot)
                    then
                        local identify_clicked, identify_snapshot, identify_err = click_identify_all(
                            ctx,
                            deps,
                            nav_mod,
                            hwnd,
                            cfg,
                            hover_snapshot,
                            character_id
                        )
                        if identify_clicked then
                            identified_all = true
                            identify_restarts = identify_restarts + 1
                            snapshot = type(identify_snapshot) == "table" and identify_snapshot or hover_snapshot
                            restart_scan = true
                            break
                        end
                        log_line(ctx, deps, "warn", string.format(
                            "[Leveling] auto-equip identify-all failed | id=%s cell=(%.1f, %.1f) err=%s",
                            tostring(character_id or ""),
                            tonumber(candidate.x) or 0,
                            tonumber(candidate.y) or 0,
                            tostring(identify_err or "")
                        ))
                    end

                    local compare = parse_compare(hover_snapshot)
                    local equip, reason = should_equip(compare, cfg)
                    log_line(ctx, deps, "info", string.format(
                        "[Leveling] auto-equip candidate | id=%s cell=(%.1f, %.1f) name=%s type=%s survival=%s damage=%s decision=%s unidentified=%s",
                        tostring(character_id or ""),
                        tonumber(candidate.x) or 0,
                        tonumber(candidate.y) or 0,
                        tostring(compare.item_name or ""),
                        tostring(compare.item_type or ""),
                        tostring(compare.survival),
                        tostring(compare.damage),
                        tostring(reason or ""),
                        has_unidentified_text(hover_snapshot) and "true" or "false"
                    ))
                    if equip then
                        local ready, ready_err = move_to_candidate(ctx, nav_mod, hwnd, candidate, cfg)
                        if not ready then
                            skipped = skipped + 1
                            log_line(ctx, deps, "warn", string.format(
                                "[Leveling] auto-equip right click move failed | id=%s cell=(%.1f, %.1f) err=%s",
                                tostring(character_id or ""),
                                tonumber(candidate.x) or 0,
                                tonumber(candidate.y) or 0,
                                tostring(ready_err or "")
                            ))
                        else
                            local clicked, click_err = right_click_current_position(ctx, deps, hwnd, candidate, cfg)
                            if clicked then
                                equipped = equipped + 1
                                sleep_ms(ctx, cfg.equip_wait_ms)
                                if equipped >= max_equips then
                                    break
                                end
                            else
                                skipped = skipped + 1
                                log_line(ctx, deps, "warn", string.format(
                                    "[Leveling] auto-equip right click failed | id=%s cell=(%.1f, %.1f) err=%s",
                                    tostring(character_id or ""),
                                    tonumber(candidate.x) or 0,
                                    tonumber(candidate.y) or 0,
                                    tostring(click_err or "")
                                ))
                            end
                        end
                    else
                        skipped = skipped + 1
                    end
                end
            end
        end

        if not restart_scan or equipped >= max_equips then
            break
        end
    end

    close_if_needed()
    return true, {
        scanned = scanned,
        equipped = equipped,
        skipped = skipped,
        identified_all = identify_restarts
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
    if runtime.auto_equip_active == true then
        return true
    end
    local force_scan = deps.force_scan == true
    if not force_scan and current_time < (tonumber(runtime.auto_equip_next_scan_at) or 0) then
        return false
    end

    local character_id = tostring(deps.character_id or "")
    if character_id == "" then
        runtime.auto_equip_next_scan_at = current_time + math.max(1000, tonumber(cfg.retry_ms) or 12000)
        return false
    end
    runtime.auto_equip_last_result = nil
    runtime.auto_equip_last_summary = nil

    local safe, reason = M.safe_window(
        ctx,
        deps,
        runtime,
        cfg,
        current_time,
        deps.player_x,
        deps.player_y,
        deps.hp_ratio,
        deps.in_main_interface
    )
    if not safe then
        if tostring(reason or "") ~= tostring(runtime.auto_equip_last_block_reason or "") then
            runtime.auto_equip_last_block_reason = tostring(reason or "")
            log_line(ctx, deps, "info", string.format(
                "[Leveling] auto-equip waits for safe window | reason=%s id=%s",
                tostring(reason or ""),
                character_id
            ))
        end
        return false
    end

    runtime.auto_equip_last_block_reason = nil
    runtime.auto_equip_active = true
    runtime.auto_equip_next_scan_at = current_time + math.max(1000, tonumber(cfg.scan_interval_ms) or 45000)

    if type(deps.release_inputs) == "function" then
        deps.release_inputs(ctx, current_time, true)
    end
    if type(deps.hold_navigation) == "function" then
        deps.hold_navigation(ctx, current_time, "auto_equip_maintenance")
    end

    local ok, handled, summary = safe_call(M.perform_scan, ctx, deps, runtime, cfg, current_time, character_id)
    runtime.auto_equip_active = false
    runtime.auto_equip_safe_since = 0

    if not ok then
        if runtime.auto_equip_bag_opened_by_module == true and type(deps.press_key) == "function" then
            deps.press_key(ctx, current_time, tonumber(cfg.close_bag_key_vk) or 0x42, "auto-equip close bag after error")
            sleep_ms(ctx, cfg.bag_close_wait_ms)
            runtime.auto_equip_bag_opened_by_module = false
        end
        runtime.auto_equip_next_scan_at = current_time + math.max(1000, tonumber(cfg.retry_ms) or 12000)
        runtime.auto_equip_last_result = "crashed"
        runtime.auto_equip_last_summary = tostring(handled or "")
        log_line(ctx, deps, "warn", string.format(
            "[Leveling] auto-equip scan crashed | id=%s err=%s",
            character_id,
            tostring(handled or "")
        ))
        return true
    end
    if handled ~= true then
        if runtime.auto_equip_bag_opened_by_module == true and type(deps.press_key) == "function" then
            deps.press_key(ctx, current_time, tonumber(cfg.close_bag_key_vk) or 0x42, "auto-equip close bag after failed scan")
            sleep_ms(ctx, cfg.bag_close_wait_ms)
            runtime.auto_equip_bag_opened_by_module = false
        end
        runtime.auto_equip_next_scan_at = current_time + math.max(1000, tonumber(cfg.retry_ms) or 12000)
        runtime.auto_equip_last_result = "failed"
        runtime.auto_equip_last_summary = tostring(summary or "")
        log_line(ctx, deps, "warn", string.format(
            "[Leveling] auto-equip scan failed | id=%s err=%s",
            character_id,
            tostring(summary or "")
        ))
        return true
    end

    log_line(ctx, deps, "info", string.format(
        "[Leveling] auto-equip scan complete | id=%s scanned=%s equipped=%s skipped=%s identified_all=%s reason=%s",
        character_id,
        tostring(type(summary) == "table" and summary.scanned or ""),
        tostring(type(summary) == "table" and summary.equipped or ""),
        tostring(type(summary) == "table" and summary.skipped or ""),
        tostring(type(summary) == "table" and summary.identified_all or ""),
        tostring(type(summary) == "table" and summary.reason or "")
    ))
    runtime.auto_equip_last_result = "success"
    runtime.auto_equip_last_summary = summary
    return true
end

return M
