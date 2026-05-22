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
    force_open_bag_ignores_hp_and_monsters = false,
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
    ring_slot_selection_enabled = true,
    ring_slot_select_wait_ms = 450,
    ring_slot_left = {
        client_x = 972,
        client_y = 381
    },
    ring_slot_right = {
        client_x = 1376,
        client_y = 376
    },
    keep_equipped_rules = {
        {
            key = "firebirth_rings",
            item_type_patterns = { "戒指" },
            keep_names = { "火焰降生" },
            mode = "all_ring_slots",
            reason = "ring_keep_both_equipped"
        },
        {
            key = "survival_belt",
            item_type_patterns = { "腰带", "护腰" },
            keep_names = { "求生之欲" },
            mode = "any_equipped",
            reason = "belt_keep_equipped"
        },
        {
            key = "lost_time_boots",
            item_type_patterns = { "鞋", "靴", "脚部", "足部" },
            keep_names = { "失期" },
            keep_name_match_mode = "contains",
            mode = "any_equipped",
            reason = "boots_keep_lost_time"
        }
    },
    keep_equipped_panel_max_x = 650,
    keep_equipped_marker_match_max_dx = 180,
    keep_equipped_marker_match_max_dy = 100,
    skip_non_two_hand_weapons = true,
    weapon_type_patterns = {
        "单手",
        "双手",
        "主手",
        "副手",
        "剑",
        "斧",
        "锤",
        "杖",
        "弓",
        "枪",
        "盾",
        "刀",
        "爪",
        "匕",
        "弩",
        "拳",
        "炮",
        "法器"
    },
    two_hand_weapon_type_patterns = { "双手" },
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
    natural_regen_priority_enabled = true,
    natural_regen_text_patterns = { "每秒自然回复" },
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

local function item_x(item)
    return tonumber(type(item) == "table" and (item.x or item.X))
end

local function item_y(item)
    return tonumber(type(item) == "table" and (item.y or item.Y))
end

local function is_visible_button(button)
    local x = item_x(button)
    local y = item_y(button)
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
    local force_open_bag = cfg.force_open_bag_ignores_hp_and_monsters == true
        or (type(deps) == "table" and deps.force_open_bag_ignores_hp_and_monsters == true)
    local ignore_completed_loot_combat = force_open_bag
        and type(deps) == "table"
        and deps.ignore_task_combat_after_completed_loot == true

    if in_main_interface == false then
        local has_player_position = type(player_x) == "number" and type(player_y) == "number"
        if cfg.allow_position_available_without_main_interface ~= true or not has_player_position then
            return block(runtime, "not_main_interface")
        end
    end
    if force_open_bag then
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
            and not ignore_completed_loot_combat
        then
            return block(runtime, "task_combat_or_post_loot")
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

        runtime.auto_equip_safe_since = 0
        reset_safe_observe(runtime)
        return true, "ready_force_open_bag"
    end
    if not force_open_bag
        and type(hp_ratio) == "number"
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

local function text_contains(text, needle)
    text = tostring(text or "")
    needle = tostring(needle or "")
    return needle ~= "" and text:find(needle, 1, true) ~= nil
end

local function is_ring_type(item_type)
    return text_contains(item_type, "戒指")
end

local function text_contains_any(text, patterns)
    if type(patterns) ~= "table" then
        return false
    end
    for _, pattern in ipairs(patterns) do
        if text_contains(text, pattern) then
            return true
        end
    end
    return false
end

local function configured_patterns(cfg, key)
    local patterns = type(cfg) == "table" and cfg[key] or nil
    if type(patterns) == "table" then
        return patterns
    end
    return M.DEFAULT_CONFIG[key]
end

local function parse_natural_regen_value(text, cfg)
    if text_contains_any(text, configured_patterns(cfg, "natural_regen_text_patterns")) ~= true then
        return nil
    end

    local raw = trim(text)
    local fallback = nil
    for value in raw:gmatch("([%+%-]?%d+%.?%d*)") do
        local parsed = tonumber(value)
        if parsed ~= nil then
            if parsed > 0 then
                return parsed
            end
            fallback = fallback or parsed
        end
    end
    return fallback
end

local function is_weapon_type(item_type, cfg)
    return text_contains_any(item_type, configured_patterns(cfg, "weapon_type_patterns"))
end

local function is_two_hand_weapon_type(item_type, cfg)
    return text_contains_any(item_type, configured_patterns(cfg, "two_hand_weapon_type_patterns"))
end

local function compare_slot_from_text(text)
    if text == "left" or text == "right" then
        return text
    end
    if text_contains(text, "左") then
        return "left"
    end
    if text_contains(text, "右") then
        return "right"
    end
    return nil
end

local function keep_name_matches(item_name, keep_names, match_mode)
    local name = trim(item_name)
    if name == "" then
        return false
    end

    if type(keep_names) ~= "table" then
        return false
    end
    for _, keep_name in ipairs(keep_names) do
        local expected = trim(keep_name)
        if expected ~= "" and tostring(match_mode or "") == "contains" then
            if name:find(expected, 1, true) ~= nil then
                return true
            end
        elseif name == expected then
            return true
        end
    end
    return false
end

local function nearest_tip_item_name(marker, item_names, max_dx, max_dy)
    local marker_x = tonumber(marker and marker.x)
    local marker_y = tonumber(marker and marker.y)
    if marker_x == nil or marker_y == nil then
        return nil
    end

    max_dx = tonumber(max_dx) or 180
    max_dy = tonumber(max_dy) or 100
    local best = nil
    local best_score = nil
    for _, item in ipairs(item_names) do
        local name_x = tonumber(item.x)
        local name_y = tonumber(item.y)
        if name_x ~= nil and name_y ~= nil then
            local dx = math.abs(name_x - marker_x)
            local dy = math.abs(name_y - marker_y)
            if dx <= max_dx and dy <= max_dy then
                local score = dx + dy
                if best == nil or score < best_score then
                    best = item
                    best_score = score
                end
            end
        end
    end
    return best
end

local function parse_equipped_ring_names(texts, cfg)
    local item_names = {}
    local hand_marks = {}
    local max_panel_x = tonumber(type(cfg) == "table" and cfg.keep_equipped_panel_max_x)
        or tonumber(M.DEFAULT_CONFIG.keep_equipped_panel_max_x)
        or 650
    local max_dx = tonumber(type(cfg) == "table" and cfg.keep_equipped_marker_match_max_dx)
        or tonumber(M.DEFAULT_CONFIG.keep_equipped_marker_match_max_dx)
        or 180
    local max_dy = tonumber(type(cfg) == "table" and cfg.keep_equipped_marker_match_max_dy)
        or tonumber(M.DEFAULT_CONFIG.keep_equipped_marker_match_max_dy)
        or 100

    for _, item in ipairs(texts) do
        local name = identity_of(item)
        local text = text_of(item)
        local x = item_x(item)
        local y = item_y(item)
        if x ~= nil and y ~= nil and x > 0 and y > 0 and x <= max_panel_x then
            if name:find("tipweaponitem_c.widgettree.uitextblock", 1, true) ~= nil
                and text ~= ""
            then
                table.insert(item_names, {
                    text = text,
                    x = x,
                    y = y
                })
            elseif name:find("tipequipmarkitem_c.widgettree.handtext", 1, true) ~= nil then
                local slot = compare_slot_from_text(text)
                if slot ~= nil then
                    table.insert(hand_marks, {
                        slot = slot,
                        x = x,
                        y = y
                    })
                end
            end
        end
    end

    local equipped = {}
    for _, marker in ipairs(hand_marks) do
        local item_name = nearest_tip_item_name(marker, item_names, max_dx, max_dy)
        if item_name ~= nil then
            equipped[marker.slot] = item_name.text
        end
    end
    return equipped
end

local function parse_equipped_item_names(texts, cfg)
    local item_names = {}
    local equipped_marks = {}
    local max_panel_x = tonumber(type(cfg) == "table" and cfg.keep_equipped_panel_max_x)
        or tonumber(M.DEFAULT_CONFIG.keep_equipped_panel_max_x)
        or 650
    local max_dx = tonumber(type(cfg) == "table" and cfg.keep_equipped_marker_match_max_dx)
        or tonumber(M.DEFAULT_CONFIG.keep_equipped_marker_match_max_dx)
        or 180
    local max_dy = tonumber(type(cfg) == "table" and cfg.keep_equipped_marker_match_max_dy)
        or tonumber(M.DEFAULT_CONFIG.keep_equipped_marker_match_max_dy)
        or 100

    for _, item in ipairs(texts) do
        local name = identity_of(item)
        local text = text_of(item)
        local x = item_x(item)
        local y = item_y(item)
        if x ~= nil and y ~= nil and x > 0 and y > 0 and x <= max_panel_x then
            if name:find("tipweaponitem_c.widgettree.uitextblock", 1, true) ~= nil
                and text ~= ""
            then
                table.insert(item_names, {
                    text = text,
                    x = x,
                    y = y
                })
            elseif name:find("tipequipmarkitem_c.widgettree.uitextblock", 1, true) ~= nil
                and text_contains(text, "已装备")
            then
                table.insert(equipped_marks, {
                    x = x,
                    y = y
                })
            end
        end
    end

    local equipped = {}
    for _, marker in ipairs(equipped_marks) do
        local item_name = nearest_tip_item_name(marker, item_names, max_dx, max_dy)
        if item_name ~= nil then
            table.insert(equipped, item_name.text)
        end
    end
    return equipped
end

local function equipped_panel_max_x(cfg)
    return tonumber(type(cfg) == "table" and cfg.keep_equipped_panel_max_x)
        or tonumber(M.DEFAULT_CONFIG.keep_equipped_panel_max_x)
        or 650
end

local function text_item_is_equipped_panel(item, cfg)
    local x = item_x(item)
    return x ~= nil and x > 0 and x <= equipped_panel_max_x(cfg)
end

local function assign_max_number(container, key, value)
    local parsed = tonumber(value)
    if type(container) ~= "table" or parsed == nil then
        return
    end
    local current = tonumber(container[key])
    if current == nil or parsed > current then
        container[key] = parsed
    end
end

local function item_type_matches_rule(item_type, rule)
    local patterns = type(rule) == "table" and rule.item_type_patterns or nil
    if type(patterns) ~= "table" then
        return false
    end

    for _, pattern in ipairs(patterns) do
        if text_contains(item_type, pattern) then
            return true
        end
    end
    return false
end

local function configured_keep_equipped_rules(cfg)
    if type(cfg) == "table" and type(cfg.keep_equipped_rules) == "table" then
        return cfg.keep_equipped_rules
    end
    if type(M.DEFAULT_CONFIG.keep_equipped_rules) == "table" then
        return M.DEFAULT_CONFIG.keep_equipped_rules
    end
    return {}
end

local function keep_rule_skip_reason(compare, rule)
    if type(compare) ~= "table" or type(rule) ~= "table" then
        return nil
    end
    if not item_type_matches_rule(compare.item_type, rule) then
        return nil
    end

    local keep_names = type(rule.keep_names) == "table" and rule.keep_names or {}
    local keep_match_mode = tostring(rule.keep_name_match_mode or rule.name_match_mode or "")
    local mode = tostring(rule.mode or "any_equipped")
    if mode == "all_ring_slots" then
        local equipped = type(compare.equipped_ring_names) == "table" and compare.equipped_ring_names or {}
        if keep_name_matches(equipped.left, keep_names, keep_match_mode)
            and keep_name_matches(equipped.right, keep_names, keep_match_mode)
        then
            return tostring(rule.reason or "keep_all_ring_slots")
        end
        return nil
    end

    if mode == "any_ring_slot" then
        local equipped = type(compare.equipped_ring_names) == "table" and compare.equipped_ring_names or {}
        if keep_name_matches(equipped.left, keep_names, keep_match_mode)
            or keep_name_matches(equipped.right, keep_names, keep_match_mode)
        then
            return tostring(rule.reason or "keep_any_ring_slot")
        end
        return nil
    end

    if mode == "ring_slot_lock" then
        return nil
    end

    local equipped_items = type(compare.equipped_item_names) == "table" and compare.equipped_item_names or {}
    for _, item_name in ipairs(equipped_items) do
        if keep_name_matches(item_name, keep_names, keep_match_mode) then
            if mode == "same_keep_name_only"
                and keep_name_matches(compare.item_name, keep_names, keep_match_mode)
            then
                return nil
            end
            return tostring(rule.reason or "keep_equipped")
        end
    end
    return nil
end

local function equipment_keep_skip_reason(compare, cfg)
    for _, rule in ipairs(configured_keep_equipped_rules(cfg)) do
        local reason = keep_rule_skip_reason(compare, rule)
        if reason ~= nil then
            return reason
        end
    end
    return nil
end

local function ring_candidate_matches_rule(compare, cfg, mode)
    if type(compare) ~= "table" or not is_ring_type(compare.item_type) then
        return false
    end

    local expected_mode = tostring(mode or "")
    for _, rule in ipairs(configured_keep_equipped_rules(cfg)) do
        if type(rule) == "table"
            and tostring(rule.mode or "") == expected_mode
            and item_type_matches_rule(compare.item_type, rule)
            and keep_name_matches(
                compare.item_name,
                rule.keep_names,
                tostring(rule.keep_name_match_mode or rule.name_match_mode or "")
            )
        then
            return true
        end
    end
    return false
end

local function missing_all_ring_slot_keep_reason(compare, cfg)
    if type(compare) ~= "table" or not is_ring_type(compare.item_type) then
        return nil
    end

    local equipped = type(compare.equipped_ring_names) == "table" and compare.equipped_ring_names or {}
    for _, rule in ipairs(configured_keep_equipped_rules(cfg)) do
        if type(rule) == "table"
            and tostring(rule.mode or "") == "all_ring_slots"
            and item_type_matches_rule(compare.item_type, rule)
            and keep_name_matches(
                compare.item_name,
                rule.keep_names,
                tostring(rule.keep_name_match_mode or rule.name_match_mode or "")
            )
        then
            local keep_match_mode = tostring(rule.keep_name_match_mode or rule.name_match_mode or "")
            local left_matches = keep_name_matches(equipped.left, rule.keep_names, keep_match_mode)
            local right_matches = keep_name_matches(equipped.right, rule.keep_names, keep_match_mode)
            if not (left_matches and right_matches) then
                return tostring(rule.force_reason or rule.equip_reason or "ring_force_missing_keep_slot")
            end
        end
    end
    return nil
end

local ring_slot_lock_reason_from_keep_rules

local function forced_ring_candidate_reason(compare, cfg, reason)
    if type(compare) ~= "table" or not is_ring_type(compare.item_type) then
        return nil
    end
    if compare.direct_ring_equip ~= true
        and compare_slot_from_text(compare.compare_slot) == nil
    then
        return nil
    end
    if compare.direct_ring_equip ~= true
        and ring_slot_lock_reason_from_keep_rules(compare, cfg, compare.compare_slot) ~= nil
    then
        return nil
    end

    local force_reason = trim(compare.force_equip_reason)
    if force_reason ~= "" and ring_candidate_matches_rule(compare, cfg, "candidate_ring_force_equip") then
        return force_reason
    end

    local missing_keep_reason = missing_all_ring_slot_keep_reason(compare, cfg)
    if missing_keep_reason ~= nil then
        return missing_keep_reason
    end

    local current_reason = tostring(reason or "")
    if current_reason:find("ring_force", 1, true) ~= nil
        and (
            ring_candidate_matches_rule(compare, cfg, "candidate_ring_force_equip")
            or ring_candidate_matches_rule(compare, cfg, "all_ring_slots")
        )
    then
        return current_reason
    end
    return nil
end

local function joined_names(names)
    if type(names) ~= "table" or #names == 0 then
        return ""
    end
    return table.concat(names, "/")
end

local function compare_row_key(name)
    return tostring(name or ""):match("attrbutecompareitem(%d+)")
end

local function ensure_compare_row(rows, key)
    if type(rows) ~= "table" or key == nil or key == "" then
        return nil
    end

    local row = rows[key]
    if row == nil then
        row = { key = key }
        rows[key] = row
        rows[#rows + 1] = row
    end
    return row
end

local function collect_ring_compare_row(rows, name, text)
    if name:find("attrbutecompareitems", 1, true) == nil then
        return
    end

    local row = ensure_compare_row(rows, compare_row_key(name))
    if row == nil then
        return
    end

    if name:find("tipstagitem_name.widgettree.uitextblock", 1, true) ~= nil then
        row.slot = compare_slot_from_text(text) or row.slot
        return
    end

    if name:find("text_attrvalue", 1, true) == nil then
        return
    end

    local value = parse_percent(text)
    if value == nil then
        return
    end
    if name:find("attrbuteitem_dps", 1, true) ~= nil then
        row.damage = value
    elseif name:find("attrbuteitem_def", 1, true) ~= nil then
        row.survival = value
    end
end

local function ring_row_can_equip(row, cfg)
    if type(row) ~= "table" or row.slot == nil then
        return false
    end

    local survival = tonumber(row.survival)
    local damage = tonumber(row.damage)
    local min_survival = tonumber(cfg.min_survival_gain) or 0
    local min_damage = tonumber(cfg.min_damage_gain) or 0
    if survival == nil and damage == nil then
        return false
    end
    if survival ~= nil and survival > min_survival then
        return true
    end
    if survival ~= nil and survival < min_survival then
        return false
    end
    return cfg.allow_damage_upgrade_when_survival_equal == true
        and (survival == nil or survival >= min_survival)
        and damage ~= nil
        and damage > min_damage
end

local function ring_row_score(row, cfg)
    local min_survival = tonumber(cfg.min_survival_gain) or 0
    local survival = tonumber(type(row) == "table" and row.survival)
    local damage = tonumber(type(row) == "table" and row.damage)
    return survival ~= nil and survival or min_survival, damage ~= nil and damage or -1000000000
end

local function ring_row_is_better(row, best, cfg)
    if best == nil then
        return true
    end

    local survival, damage = ring_row_score(row, cfg)
    local best_survival, best_damage = ring_row_score(best, cfg)
    if survival ~= best_survival then
        return survival > best_survival
    end
    if damage ~= best_damage then
        return damage > best_damage
    end

    return tonumber(row.key) ~= nil
        and tonumber(best.key) ~= nil
        and tonumber(row.key) < tonumber(best.key)
end

local function select_ring_compare_row(rows, cfg, require_upgrade)
    if type(rows) ~= "table" then
        return nil
    end

    local best = nil
    for _, row in ipairs(rows) do
        if type(row) == "table" and row.slot ~= nil then
            if require_upgrade ~= true or ring_row_can_equip(row, cfg) then
                if ring_row_is_better(row, best, cfg) then
                    best = row
                end
            end
        end
    end
    return best
end

local function select_ring_compare_row_by_slot(rows, cfg, slot)
    slot = compare_slot_from_text(slot)
    if type(rows) ~= "table" or slot == nil then
        return nil
    end

    local best = nil
    for _, row in ipairs(rows) do
        if type(row) == "table" and compare_slot_from_text(row.slot) == slot then
            if ring_row_is_better(row, best, cfg) then
                best = row
            end
        end
    end
    return best
end

ring_slot_lock_reason_from_keep_rules = function(result, cfg, slot)
    slot = compare_slot_from_text(slot)
    if type(result) ~= "table" or slot == nil then
        return nil
    end

    local equipped = type(result.equipped_ring_names) == "table" and result.equipped_ring_names or {}
    local equipped_name = equipped[slot]
    for _, rule in ipairs(configured_keep_equipped_rules(cfg)) do
        if type(rule) == "table"
            and tostring(rule.mode or "") == "ring_slot_lock"
            and item_type_matches_rule(result.item_type, rule)
            and keep_name_matches(
                equipped_name,
                rule.keep_names,
                tostring(rule.keep_name_match_mode or rule.name_match_mode or "")
            )
        then
            return tostring(rule.reason or "ring_slot_locked")
        end
    end
    return nil
end

local function candidate_force_ring_rule(result, cfg)
    if type(result) ~= "table" or not is_ring_type(result.item_type) then
        return nil
    end

    for _, rule in ipairs(configured_keep_equipped_rules(cfg)) do
        if type(rule) == "table"
            and (tostring(rule.mode or "") == "candidate_ring_force_equip" or rule.force_candidate_equip == true)
            and item_type_matches_rule(result.item_type, rule)
            and keep_name_matches(
                result.item_name,
                rule.keep_names,
                tostring(rule.keep_name_match_mode or rule.name_match_mode or "")
            )
        then
            return rule
        end
    end
    return nil
end

local function append_unique_ring_slot(slots, slot)
    slot = compare_slot_from_text(slot)
    if slot == nil then
        return
    end
    for _, existing in ipairs(slots) do
        if existing == slot then
            return
        end
    end
    table.insert(slots, slot)
end

local function fallback_ring_slot_for_forced_candidate(result, cfg, rule)
    if type(result) ~= "table" or type(rule) ~= "table" then
        return nil
    end

    local slots = {}
    if type(rule.preferred_slots) == "table" then
        for _, slot in ipairs(rule.preferred_slots) do
            append_unique_ring_slot(slots, slot)
        end
    end
    append_unique_ring_slot(slots, rule.preferred_slot)
    append_unique_ring_slot(slots, rule.fallback_slot)
    append_unique_ring_slot(slots, "right")
    append_unique_ring_slot(slots, "left")

    local equipped = type(result.equipped_ring_names) == "table" and result.equipped_ring_names or {}
    for _, slot in ipairs(slots) do
        if ring_slot_lock_reason_from_keep_rules(result, cfg, slot) == nil
            and trim(equipped[slot]) == ""
        then
            return slot
        end
    end

    for _, slot in ipairs(slots) do
        if ring_slot_lock_reason_from_keep_rules(result, cfg, slot) == nil then
            return slot
        end
    end
    return nil
end

local function fallback_empty_unlocked_ring_slot(result, cfg, preferred_slot)
    if type(result) ~= "table" then
        return nil
    end

    local slots = {}
    append_unique_ring_slot(slots, preferred_slot)
    append_unique_ring_slot(slots, "right")
    append_unique_ring_slot(slots, "left")

    local equipped = type(result.equipped_ring_names) == "table" and result.equipped_ring_names or {}
    for _, slot in ipairs(slots) do
        if ring_slot_lock_reason_from_keep_rules(result, cfg, slot) == nil
            and trim(equipped[slot]) == ""
        then
            return slot
        end
    end
    return nil
end

local function ring_compare_values_can_equip(result, cfg)
    if type(result) ~= "table" then
        return false
    end

    local survival = tonumber(result.survival)
    local damage = tonumber(result.damage)
    local min_survival = tonumber(cfg.min_survival_gain) or 0
    local min_damage = tonumber(cfg.min_damage_gain) or 0
    if survival == nil and damage == nil then
        return false
    end
    if survival ~= nil and survival > min_survival then
        return true
    end
    if survival ~= nil and survival < min_survival then
        return false
    end
    return cfg.allow_damage_upgrade_when_survival_equal == true
        and (survival == nil or survival >= min_survival)
        and damage ~= nil
        and damage > min_damage
end

local function equipped_ring_slots_empty(result)
    local equipped = type(result) == "table" and type(result.equipped_ring_names) == "table"
        and result.equipped_ring_names
        or {}
    return trim(equipped.left) == "" and trim(equipped.right) == ""
end

local function first_ring_slot_lock_reason(rows, result, cfg)
    if type(rows) ~= "table" then
        return nil
    end
    for _, row in ipairs(rows) do
        local reason = ring_slot_lock_reason_from_keep_rules(result, cfg, type(row) == "table" and row.slot or nil)
        if reason ~= nil then
            return reason
        end
    end
    return nil
end

local function preferred_ring_slot_from_keep_rules(result, cfg)
    if type(result) ~= "table" then
        return nil
    end

    local equipped = type(result.equipped_ring_names) == "table" and result.equipped_ring_names or {}
    for _, rule in ipairs(configured_keep_equipped_rules(cfg)) do
        if type(rule) == "table"
            and tostring(rule.mode or "") == "all_ring_slots"
            and item_type_matches_rule(result.item_type, rule)
            and keep_name_matches(
                result.item_name,
                rule.keep_names,
                tostring(rule.keep_name_match_mode or rule.name_match_mode or "")
            )
        then
            local keep_match_mode = tostring(rule.keep_name_match_mode or rule.name_match_mode or "")
            local left_matches = keep_name_matches(equipped.left, rule.keep_names, keep_match_mode)
            local right_matches = keep_name_matches(equipped.right, rule.keep_names, keep_match_mode)
            if left_matches and not right_matches then
                return "right"
            end
            if right_matches and not left_matches then
                return "left"
            end
        end
    end
    return nil
end

local function apply_ring_compare_choice(result, ring_rows, cfg)
    if type(result) ~= "table" or not is_ring_type(result.item_type) then
        return
    end

    local force_rule = candidate_force_ring_rule(result, cfg)
    local preferred_slot = preferred_ring_slot_from_keep_rules(result, cfg)
    local preferred_locked_reason = ring_slot_lock_reason_from_keep_rules(result, cfg, preferred_slot)
    local selected = preferred_locked_reason == nil and select_ring_compare_row_by_slot(ring_rows, cfg, preferred_slot) or nil
    if selected == nil then
        local best_upgrade = nil
        for _, row in ipairs(ring_rows) do
            if ring_slot_lock_reason_from_keep_rules(result, cfg, type(row) == "table" and row.slot or nil) == nil
                and ring_row_can_equip(row, cfg)
                and ring_row_is_better(row, best_upgrade, cfg)
            then
                best_upgrade = row
            end
        end
        selected = best_upgrade
    end
    if selected == nil then
        local best_any = nil
        for _, row in ipairs(ring_rows) do
            if ring_slot_lock_reason_from_keep_rules(result, cfg, type(row) == "table" and row.slot or nil) == nil
                and ring_row_is_better(row, best_any, cfg)
            then
                best_any = row
            end
        end
        selected = best_any
    end
    if selected == nil then
        if force_rule ~= nil
            and force_rule.direct_equip_when_no_rings ~= false
            and equipped_ring_slots_empty(result)
        then
            result.direct_ring_equip = true
            result.force_equip_reason = tostring(force_rule.reason or "ring_force_candidate")
            result.ring_slot_lock_reason = nil
            return
        end
        local empty_slot = fallback_empty_unlocked_ring_slot(result, cfg, preferred_slot)
        if empty_slot ~= nil and ring_compare_values_can_equip(result, cfg) then
            result.compare_slot = empty_slot
            result.ring_slot_lock_reason = nil
            result.ring_slot_fallback_reason = "empty_unlocked_ring_slot"
            return
        end
        local fallback_slot = fallback_ring_slot_for_forced_candidate(result, cfg, force_rule)
        if fallback_slot ~= nil then
            result.compare_slot = fallback_slot
            result.force_equip_reason = tostring(force_rule.reason or "ring_force_candidate")
            result.ring_slot_lock_reason = nil
            return
        end
        result.compare_slot = nil
        result.ring_slot_lock_reason = first_ring_slot_lock_reason(ring_rows, result, cfg)
        return
    end

    result.compare_slot = selected.slot
    result.damage = selected.damage
    result.survival = selected.survival
    result.ring_slot_lock_reason = nil
    if force_rule ~= nil then
        result.force_equip_reason = tostring(force_rule.reason or "ring_force_candidate")
        if force_rule.direct_equip_when_no_rings ~= false and equipped_ring_slots_empty(result) then
            result.compare_slot = nil
            result.direct_ring_equip = true
        end
    end
end

local function parse_compare(snapshot, cfg)
    local result = {
        damage = nil,
        survival = nil,
        natural_regen = nil,
        equipped_natural_regen = nil,
        item_name = nil,
        item_type = nil,
        compare_slot = nil,
        equipped_ring_names = {},
        equipped_item_names = {}
    }
    if type(snapshot) ~= "table" or type(snapshot.texts) ~= "table" then
        return result
    end

    result.equipped_ring_names = parse_equipped_ring_names(snapshot.texts, cfg)
    result.equipped_item_names = parse_equipped_item_names(snapshot.texts, cfg)
    local ring_rows = {}

    for _, item in ipairs(snapshot.texts) do
        local name = identity_of(item)
        local text = text_of(item)
        collect_ring_compare_row(ring_rows, name, text)
        if cfg.natural_regen_priority_enabled ~= false then
            local natural_regen = parse_natural_regen_value(text, cfg)
            if natural_regen ~= nil then
                if text_item_is_equipped_panel(item, cfg) then
                    assign_max_number(result, "equipped_natural_regen", natural_regen)
                else
                    assign_max_number(result, "natural_regen", natural_regen)
                end
            end
        end
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

    apply_ring_compare_choice(result, ring_rows, cfg)
    return result
end

local function skip_reason_for_special_equipment(compare, cfg)
    local item_type = trim(type(compare) == "table" and compare.item_type or "")
    if cfg.ring_slot_selection_enabled ~= false
        and is_ring_type(item_type)
        and type(compare) == "table"
        and compare.direct_ring_equip ~= true
    then
        local locked_reason = ring_slot_lock_reason_from_keep_rules(compare, cfg, compare.compare_slot)
        if locked_reason ~= nil then
            return locked_reason
        end
    end

    local force_reason = trim(type(compare) == "table" and compare.force_equip_reason or "")
    local keep_reason = force_reason == "" and equipment_keep_skip_reason(compare, cfg) or nil
    if keep_reason ~= nil then
        return keep_reason
    end

    if cfg.skip_non_two_hand_weapons ~= false
        and is_weapon_type(item_type, cfg)
        and not is_two_hand_weapon_type(item_type, cfg)
    then
        return "skip_non_two_hand_weapon"
    end

    if cfg.ring_slot_selection_enabled ~= false
        and is_ring_type(item_type)
        and type(compare) == "table"
        and compare.direct_ring_equip ~= true
        and compare_slot_from_text(type(compare) == "table" and compare.compare_slot or nil) == nil
    then
        return tostring(type(compare) == "table" and compare.ring_slot_lock_reason or "") ~= ""
            and tostring(compare.ring_slot_lock_reason)
            or "ring_slot_unknown"
    end

    return nil
end

local function should_equip(compare, cfg)
    local survival = tonumber(type(compare) == "table" and compare.survival)
    local damage = tonumber(type(compare) == "table" and compare.damage)
    local natural_regen = tonumber(type(compare) == "table" and compare.natural_regen)
    local equipped_natural_regen = tonumber(type(compare) == "table" and compare.equipped_natural_regen)
    local min_survival = tonumber(cfg.min_survival_gain) or 0
    local min_damage = tonumber(cfg.min_damage_gain) or 0
    if cfg.ring_slot_selection_enabled ~= false
        and type(compare) == "table"
        and compare.direct_ring_equip ~= true
        and is_ring_type(compare.item_type)
    then
        local locked_reason = ring_slot_lock_reason_from_keep_rules(compare, cfg, compare.compare_slot)
        if locked_reason ~= nil then
            return false, locked_reason
        end
    end

    local force_reason = trim(type(compare) == "table" and compare.force_equip_reason or "")
    if force_reason ~= "" then
        return true, force_reason
    end

    local keep_reason = equipment_keep_skip_reason(compare, cfg)
    if keep_reason ~= nil then
        return false, keep_reason
    end

    if cfg.natural_regen_priority_enabled ~= false then
        if natural_regen ~= nil and natural_regen > 0 then
            if equipped_natural_regen == nil or natural_regen > equipped_natural_regen then
                return true, "natural_regen_gain"
            end
            if equipped_natural_regen ~= nil and natural_regen < equipped_natural_regen then
                return false, "natural_regen_loss"
            end
        elseif equipped_natural_regen ~= nil and equipped_natural_regen > 0 then
            return false, "natural_regen_loss"
        end
    end

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

local function count_visible_bag_items(snapshot, cfg)
    local grid = type(cfg) == "table" and type(cfg.bag_grid) == "table" and cfg.bag_grid or {}
    local min_x = tonumber(grid.min_x) or 0
    local max_x = tonumber(grid.max_x) or math.huge
    local min_y = tonumber(grid.min_y) or 0
    local max_y = tonumber(grid.max_y) or math.huge
    local count = 0

    if type(snapshot) ~= "table" or type(snapshot.buttons) ~= "table" then
        return count
    end

    for _, button in ipairs(snapshot.buttons) do
        local name = identity_of(button)
        local is_grid_item = name:find("pcuigridlistviewitem_c.widgettree", 1, true) ~= nil
            and name:find("pcuibagequipitem", 1, true) == nil
        local is_occupied = is_grid_item
            and name:find("selectbtn", 1, true) ~= nil
            and name:find("nillbtn", 1, true) == nil
        if is_occupied and is_visible_button(button) then
            local x = tonumber(button.x or button.X)
            local y = tonumber(button.y or button.Y)
            if x ~= nil and y ~= nil and x >= min_x and x <= max_x and y >= min_y and y <= max_y then
                count = count + 1
            end
        end
    end

    return count
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

local function click_current_position(ctx, deps, hwnd, button, cfg)
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
            local click_ok = mouse.click(button, click_delay_ms)
            if click_ok == false then
                error("mouse.click(" .. tostring(button) .. ") failed")
            end
            return true
        end

        if type(mouse.down) == "function" and type(mouse.up) == "function" then
            local down_ok = mouse.down(button)
            if down_ok == false then
                error("mouse.down(" .. tostring(button) .. ") failed")
            end
            sleep_ms(ctx, click_delay_ms)
            local up_ok = mouse.up(button)
            if up_ok == false then
                error("mouse.up(" .. tostring(button) .. ") failed")
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
        return false, tostring(click_err or ("mouse " .. tostring(button) .. " click failed"))
    end

    return true, click_mode
end

local function right_click_current_position(ctx, deps, hwnd, candidate, cfg)
    local clicked, click_mode_or_err = click_current_position(ctx, deps, hwnd, "right", cfg)
    if not clicked then
        return false, click_mode_or_err
    end

    log_line(ctx, deps, "info", string.format(
        "[Leveling] auto-equip driver right click | cell=(%.1f, %.1f) mode=%s",
        tonumber(type(candidate) == "table" and candidate.x) or 0,
        tonumber(type(candidate) == "table" and candidate.y) or 0,
        tostring(click_mode_or_err or "")
    ))
    return true
end

local function left_click_client_point(ctx, deps, nav_mod, hwnd, x, y, cfg, label)
    if type(nav_mod) ~= "table" or type(nav_mod.move_mouse_to_client) ~= "function" then
        return false, "nav.move_mouse_to_client unavailable"
    end

    local moved, move_err = nav_mod.move_mouse_to_client(x, y, {
        hwnd = hwnd,
        mouse_mode = cfg.mouse_mode or "api",
        min_duration_ms = cfg.move_min_duration_ms,
        max_duration_ms = cfg.move_max_duration_ms,
        hover_ms = math.max(0, tonumber(cfg.ring_slot_select_hover_ms) or 80),
        set_foreground = true
    })
    if not moved then
        return false, move_err or "move to click point failed"
    end

    local clicked, click_mode_or_err = click_current_position(ctx, deps, hwnd, "left", cfg)
    if not clicked then
        return false, click_mode_or_err
    end

    log_line(ctx, deps, "info", string.format(
        "[Leveling] auto-equip fixed left click | label=%s x=%.1f y=%.1f mode=%s",
        tostring(label or ""),
        tonumber(x) or 0,
        tonumber(y) or 0,
        tostring(click_mode_or_err or "")
    ))
    return true
end

local function select_ring_slot_after_right_click(ctx, deps, nav_mod, hwnd, compare, cfg, character_id, candidate)
    if cfg.ring_slot_selection_enabled == false or not is_ring_type(type(compare) == "table" and compare.item_type or "") then
        return true
    end
    if type(compare) == "table" and compare.direct_ring_equip == true then
        log_line(ctx, deps, "info", string.format(
            "[Leveling] auto-equip ring direct equip | id=%s cell=(%.1f, %.1f) name=%s",
            tostring(character_id or ""),
            tonumber(type(candidate) == "table" and candidate.x) or 0,
            tonumber(type(candidate) == "table" and candidate.y) or 0,
            tostring(compare.item_name or "")
        ))
        return true
    end

    local slot = compare_slot_from_text(type(compare) == "table" and compare.compare_slot or nil)
    local point = nil
    if slot == "left" then
        point = type(cfg.ring_slot_left) == "table" and cfg.ring_slot_left or nil
    elseif slot == "right" then
        point = type(cfg.ring_slot_right) == "table" and cfg.ring_slot_right or nil
    end
    if type(point) ~= "table" then
        return false, "ring slot selection point missing"
    end

    local x = tonumber(point.client_x)
    local y = tonumber(point.client_y)
    if x == nil or y == nil then
        return false, "ring slot selection point invalid"
    end

    local clicked, click_err = left_click_client_point(
        ctx,
        deps,
        nav_mod,
        hwnd,
        x,
        y,
        cfg,
        "ring_" .. slot
    )
    if not clicked then
        return false, click_err
    end

    log_line(ctx, deps, "info", string.format(
        "[Leveling] auto-equip ring slot selected | id=%s cell=(%.1f, %.1f) slot=%s x=%.1f y=%.1f",
        tostring(character_id or ""),
        tonumber(type(candidate) == "table" and candidate.x) or 0,
        tonumber(type(candidate) == "table" and candidate.y) or 0,
        slot,
        x,
        y
    ))
    sleep_ms(ctx, cfg.ring_slot_select_wait_ms)
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

    local bag_item_count = count_visible_bag_items(snapshot, cfg)
    if bag_item_count <= 0 then
        close_if_needed(true)
        log_line(ctx, deps, "info", string.format(
            "[Leveling] auto-equip skipped because bag has no visible items | id=%s",
            tostring(character_id or "")
        ))
        return true, {
            scanned = 0,
            equipped = 0,
            skipped = 0,
            identified_all = 0,
            reason = "empty_bag",
            empty_bag = true
        }
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

                    local compare = parse_compare(hover_snapshot, cfg)
                    local equip, reason = should_equip(compare, cfg)
                    if equip then
                        local special_skip_reason = skip_reason_for_special_equipment(compare, cfg)
                        if special_skip_reason ~= nil then
                            equip = false
                            reason = special_skip_reason
                        end
                    end

                    local forced_ring_reason = forced_ring_candidate_reason(compare, cfg, reason)
                    if forced_ring_reason ~= nil then
                        equip = true
                        reason = forced_ring_reason
                    end
                    log_line(ctx, deps, "info", string.format(
                        "[Leveling] auto-equip candidate | id=%s cell=(%.1f, %.1f) name=%s type=%s slot=%s equipped_rings=%s/%s equipped_items=%s survival=%s damage=%s natural_regen=%s equipped_natural_regen=%s decision=%s unidentified=%s",
                        tostring(character_id or ""),
                        tonumber(candidate.x) or 0,
                        tonumber(candidate.y) or 0,
                        tostring(compare.item_name or ""),
                        tostring(compare.item_type or ""),
                        tostring(compare.compare_slot or ""),
                        tostring(type(compare.equipped_ring_names) == "table" and compare.equipped_ring_names.left or ""),
                        tostring(type(compare.equipped_ring_names) == "table" and compare.equipped_ring_names.right or ""),
                        joined_names(compare.equipped_item_names),
                        tostring(compare.survival),
                        tostring(compare.damage),
                        tostring(compare.natural_regen),
                        tostring(compare.equipped_natural_regen),
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
                                local post_click_ok, post_click_err = select_ring_slot_after_right_click(
                                    ctx,
                                    deps,
                                    nav_mod,
                                    hwnd,
                                    compare,
                                    cfg,
                                    character_id,
                                    candidate
                                )
                                if post_click_ok then
                                    equipped = equipped + 1
                                    sleep_ms(ctx, cfg.equip_wait_ms)
                                    if equipped >= max_equips then
                                        break
                                    end
                                    if is_ring_type(compare.item_type) then
                                        local refreshed_snapshot, refresh_err = nav_mod.enum_ui()
                                        if type(refreshed_snapshot) == "table" then
                                            snapshot = refreshed_snapshot
                                            restart_scan = true
                                            break
                                        end
                                        log_line(ctx, deps, "warn", string.format(
                                            "[Leveling] auto-equip ring refresh failed | id=%s cell=(%.1f, %.1f) err=%s",
                                            tostring(character_id or ""),
                                            tonumber(candidate.x) or 0,
                                            tonumber(candidate.y) or 0,
                                            tostring(refresh_err or "")
                                        ))
                                    end
                                else
                                    skipped = skipped + 1
                                    log_line(ctx, deps, "warn", string.format(
                                        "[Leveling] auto-equip post-click selection failed | id=%s cell=(%.1f, %.1f) type=%s slot=%s err=%s",
                                        tostring(character_id or ""),
                                        tonumber(candidate.x) or 0,
                                        tonumber(candidate.y) or 0,
                                        tostring(compare.item_type or ""),
                                        tostring(compare.compare_slot or ""),
                                        tostring(post_click_err or "")
                                    ))
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
