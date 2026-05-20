local M = {}

local STATE_FILE_PATH = "scripts/avepoint_treasure_state.lua"
local CHARACTER_SCOPED_STATE_VERSION = 2
local persisted_state_root = nil
local persisted_state_character_id = nil

local transition_mode
local likely_inside_treasure
local detect_boss_portal_ready
local cfg_by_key
local normalize_route
local landing_ready
local cfg_landing_ready
local cfg_landing_has_point

local function trim(value)
    return tostring(value or ""):gsub("^%s+", ""):gsub("%s+$", "")
end

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

local function now_ms(ctx)
    local sys_api = type(ctx) == "table" and ctx.sys or sys
    if type(sys_api) == "table" and type(sys_api.time) == "function" then
        return sys_api.time()
    end
    return 0
end

local function distance_2d(ax, ay, bx, by)
    if type(ax) ~= "number" or type(ay) ~= "number" or type(bx) ~= "number" or type(by) ~= "number" then
        return math.huge
    end
    local dx = ax - bx
    local dy = ay - by
    return math.sqrt(dx * dx + dy * dy)
end

local function has_reliable_world_pos(player_x, player_y)
    if type(player_x) ~= "number" or type(player_y) ~= "number" then
        return false
    end
    return math.abs(player_x) > 1 or math.abs(player_y) > 1
end

local TREASURE_BOSS_KITE_SWITCH_MS = 700
local TREASURE_BOSS_KITE_POINT_ARRIVE_DISTANCE = 220
local TREASURE_BOSS_KITE_CONFIGURED_SWITCH_MS = 2800
local TREASURE_BOSS_ZERO_MONSTER_GRACE_MS = 700
local TREASURE_BOSS_PRE_ENGAGE_ANCHOR_DISTANCE = 220
local TREASURE_BOSS_LOOT_MAX_PULSES = 2

local function is_valid_point(point)
    return type(point) == "table"
        and type(tonumber(point.x)) == "number"
        and type(tonumber(point.y)) == "number"
end

local function nearest_route_distance(route, player_x, player_y)
    if type(route) ~= "table" or not has_reliable_world_pos(player_x, player_y) then
        return math.huge
    end
    local nearest_gap = math.huge
    for _, point in ipairs(route) do
        if is_valid_point(point) then
            local gap = distance_2d(player_x, player_y, tonumber(point.x), tonumber(point.y))
            if gap < nearest_gap then
                nearest_gap = gap
            end
        end
    end
    return nearest_gap
end

local function match_text_patterns(patterns, value)
    local text = trim(value)
    if type(patterns) ~= "table" or text == "" then
        return false
    end
    for _, pattern in ipairs(patterns) do
        local token = trim(pattern)
        if token ~= "" and text:find(token, 1, true) then
            return true
        end
    end
    return false
end

local function treasure_task_matches(cfg, task_name, task_detail)
    if type(cfg) ~= "table" then
        return false
    end

    local name = trim(task_name)
    local detail = trim(task_detail)
    local has_task_patterns = type(cfg.task_patterns) == "table" and #cfg.task_patterns > 0
    local has_detail_patterns = type(cfg.task_detail_patterns) == "table" and #cfg.task_detail_patterns > 0
    local task_ok = (not has_task_patterns) or match_text_patterns(cfg.task_patterns, name)
    local detail_ok = (not has_detail_patterns) or match_text_patterns(cfg.task_detail_patterns, detail)
    local include_ok = task_ok and detail_ok
    if not include_ok then
        return false
    end

    if match_text_patterns(cfg.exclude_task_patterns, name)
        or match_text_patterns(cfg.exclude_task_patterns, detail)
        or match_text_patterns(cfg.exclude_task_detail_patterns, detail)
    then
        return false
    end

    return true
end

local function current_treasure_task_matches(cfg, hooks)
    local task_name = trim(type(hooks) == "table" and type(hooks.current_task_name) == "function" and hooks.current_task_name() or "")
    local task_detail = trim(type(hooks) == "table" and type(hooks.current_task_detail) == "function" and hooks.current_task_detail() or "")
    return treasure_task_matches(cfg, task_name, task_detail), task_name, task_detail
end

local function configured_target_level(cfg)
    local target_level = tonumber(type(cfg) == "table" and cfg.target_level)
    if target_level == nil or target_level <= 0 then
        return nil
    end
    return math.floor(target_level)
end

local function pick_level_text_candidate(snapshot)
    if type(snapshot) ~= "table" or type(snapshot.texts) ~= "table" then
        return nil
    end

    local best = nil
    for _, item in ipairs(snapshot.texts) do
        local text = trim(type(item) == "table" and item.text or "")
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
            if text:match("%d+%%") then
                score = score + 18
            end
            if text:find("推荐", 1, true) then
                score = score - 120
            end
            if text:find("怪物等级", 1, true) then
                score = score - 160
            end
            if text:find("关卡等级", 1, true) then
                score = score - 160
            end
            if score > 0 then
                local level_value = text:match("等级%s*(%d+)")
                    or normalized:match("lv%s*(%d+)")
                    or normalized:match("level%s*(%d+)")
                local progress_value = text:match("%((%d+)%%%s*%)")
                    or text:match("(%d+)%%%s*$")
                if level_value ~= nil then
                    local candidate = {
                        text = text,
                        x = tonumber(type(item) == "table" and item.x),
                        y = tonumber(type(item) == "table" and item.y),
                        name = tostring(type(item) == "table" and item.name or ""),
                        fullname = tostring(type(item) == "table" and (item.fullname or item.Fullname) or ""),
                        score = score,
                        level = tonumber(level_value),
                        progress = progress_value and tonumber(progress_value) or nil
                    }
                    if best == nil then
                        best = candidate
                    else
                        local best_score = tonumber(best.score) or 0
                        local candidate_score = tonumber(candidate.score) or 0
                        local best_y = tonumber(best.y) or math.huge
                        local candidate_y = tonumber(candidate.y) or math.huge
                        local best_x = tonumber(best.x) or math.huge
                        local candidate_x = tonumber(candidate.x) or math.huge
                        if candidate_score > best_score
                            or (candidate_score == best_score and candidate_y > best_y)
                            or (candidate_score == best_score and candidate_y == best_y and candidate_x < best_x)
                        then
                            best = candidate
                        end
                    end
                end
            end
        end
    end
    return best
end

local function refresh_player_level(ctx, cfg, runtime, hooks, current_time, force)
    if type(runtime) ~= "table" then
        return nil
    end
    current_time = tonumber(current_time) or now_ms(ctx)
    local next_probe_at = tonumber(runtime.player_level_next_probe_at) or 0
    if force ~= true and current_time < next_probe_at then
        return runtime.player_level, runtime.player_level_progress, runtime.player_level_text
    end

    runtime.player_level_next_probe_at = current_time + 1200
    if type(hooks) ~= "table" or type(hooks.enum_ui) ~= "function" then
        return runtime.player_level, runtime.player_level_progress, runtime.player_level_text
    end

    local snapshot, ui_err = hooks.enum_ui(ctx)
    if type(snapshot) ~= "table" then
        if type(hooks.log_throttled) == "function" then
            hooks.log_throttled(ctx, "treasure_level_scan_err_" .. tostring(type(cfg) == "table" and (cfg.key or "") or ""), "warn", 3000,
                "[Treasure] player level scan failed | key=" .. tostring(type(cfg) == "table" and (cfg.key or "") or "") .. " err=" .. tostring(ui_err))
        end
        return runtime.player_level, runtime.player_level_progress, runtime.player_level_text
    end

    local best = pick_level_text_candidate(snapshot)
    if type(best) ~= "table" then
        if type(hooks.log_throttled) == "function" then
            hooks.log_throttled(ctx, "treasure_level_scan_miss_" .. tostring(type(cfg) == "table" and (cfg.key or "") or ""), "info", 3000,
                "[Treasure] player level text not found | key=" .. tostring(type(cfg) == "table" and (cfg.key or "") or ""))
        end
        return runtime.player_level, runtime.player_level_progress, runtime.player_level_text
    end

    local previous_level = tonumber(runtime.player_level)
    local previous_progress = tonumber(runtime.player_level_progress)
    local previous_text = tostring(runtime.player_level_text or "")
    runtime.player_level = tonumber(best.level)
    runtime.player_level_progress = tonumber(best.progress)
    runtime.player_level_text = tostring(best.text or "")
    runtime.player_level_source = tostring(best.name or "")

    if type(hooks.log_info) == "function" and (
        previous_level ~= runtime.player_level
        or previous_progress ~= runtime.player_level_progress
        or previous_text ~= runtime.player_level_text
        or force == true
    ) then
        hooks.log_info(ctx, string.format(
            "[Treasure] player level observed | key=%s level=%s progress=%s target_level=%s text=%s pos=(%s,%s) source=%s score=%s",
            tostring(type(cfg) == "table" and (cfg.key or "") or ""),
            tostring(runtime.player_level or ""),
            tostring(runtime.player_level_progress or ""),
            tostring(configured_target_level(cfg) or ""),
            tostring(runtime.player_level_text or ""),
            tostring(best.x or ""),
            tostring(best.y or ""),
            tostring(runtime.player_level_source or ""),
            tostring(best.score or "")
        ))
    end

    return runtime.player_level, runtime.player_level_progress, runtime.player_level_text
end

local function within_trigger(player_x, player_y, player_z, trigger)
    if not is_valid_point(trigger) then
        return false
    end
    local radius = math.max(80, tonumber(trigger.radius) or 240)
    local z_tolerance = math.max(0, tonumber(trigger.z_tolerance) or 260)
    local trigger_z = tonumber(trigger.z)
    local z_gap = trigger_z ~= nil and type(player_z) == "number"
        and math.abs(player_z - trigger_z)
        or 0
    return distance_2d(player_x, player_y, tonumber(trigger.x), tonumber(trigger.y)) <= radius
        and z_gap <= z_tolerance
end

local function dump_scalar(value)
    if value == nil then
        return "nil"
    end
    local value_type = type(value)
    if value_type == "number" or value_type == "boolean" then
        return tostring(value)
    end
    local text = tostring(value)
    text = text:gsub("\\", "\\\\")
    text = text:gsub("\r", "\\r")
    text = text:gsub("\n", "\\n")
    text = text:gsub("\t", "\\t")
    text = text:gsub("\"", "\\\"")
    return "\"" .. text .. "\""
end

local function serialize_lua(value, indent)
    indent = indent or 0
    local value_type = type(value)
    if value_type ~= "table" then
        return dump_scalar(value)
    end

    local keys = {}
    for key, _ in pairs(value) do
        keys[#keys + 1] = key
    end
    table.sort(keys, function(a, b)
        local at = type(a)
        local bt = type(b)
        if at == bt then
            return tostring(a) < tostring(b)
        end
        return at == "number"
    end)

    local lines = { "{" }
    local next_indent = indent + 4
    local prefix = string.rep(" ", next_indent)
    for _, key in ipairs(keys) do
        local rendered_key
        if type(key) == "string" and key:match("^[%a_][%w_]*$") then
            rendered_key = key
        else
            rendered_key = "[" .. dump_scalar(key) .. "]"
        end
        lines[#lines + 1] = string.format(
            "%s%s = %s,",
            prefix,
            rendered_key,
            serialize_lua(value[key], next_indent)
        )
    end
    lines[#lines + 1] = string.rep(" ", indent) .. "}"
    return table.concat(lines, "\n")
end

local function default_persisted_state(character_id)
    local data = {
        treasures = {},
        resume = nil
    }
    if type(character_id) == "string" and character_id ~= "" then
        data.character_id = character_id
    end
    return data
end

local function default_treasure_record(character_id)
    local record = {
        run_count = 0,
        completed = false,
        route_acquired = false,
        route = nil,
        last_update = 0
    }
    if type(character_id) == "string" and character_id ~= "" then
        record.character_id = character_id
    end
    return record
end

local function current_character_id()
    local id = _G.AVEPOINT_PERSISTENCE_CHARACTER_ID
    if type(id) == "string" and id:match("^%d%d%d%d%d%d%d%d%d%d%d%d%d%d%d%d+$") then
        return id
    end
    return nil
end

local function normalize_state_root(data)
    if type(data) ~= "table" then
        return {
            version = CHARACTER_SCOPED_STATE_VERSION,
            characters = {}
        }
    end
    if type(data.characters) == "table" then
        data.version = tonumber(data.version) or CHARACTER_SCOPED_STATE_VERSION
        return data
    end
    return {
        version = CHARACTER_SCOPED_STATE_VERSION,
        characters = {}
    }
end

local function sanitize_persisted_state(data, character_id)
    if type(data) ~= "table" then
        data = default_persisted_state(character_id)
    end
    local scoped_character_id = type(character_id) == "string" and character_id or ""
    if scoped_character_id ~= "" then
        local stored_character_id = trim(data.character_id)
        if stored_character_id ~= "" and stored_character_id ~= scoped_character_id then
            data = default_persisted_state(scoped_character_id)
        end
        data.character_id = scoped_character_id
    end
    if type(data.treasures) ~= "table" then
        data.treasures = {}
    end
    if scoped_character_id ~= "" then
        for key, record in pairs(data.treasures) do
            if type(record) ~= "table" then
                data.treasures[key] = nil
            else
                local record_character_id = trim(record.character_id)
                if record_character_id == "" or record_character_id ~= scoped_character_id then
                    data.treasures[key] = nil
                else
                    record.character_id = scoped_character_id
                end
            end
        end
    end
    if data.resume ~= nil and type(data.resume) ~= "table" then
        data.resume = nil
    end
    if type(data.resume) == "table" and scoped_character_id ~= "" then
        local resume_character_id = trim(data.resume.character_id)
        if resume_character_id == "" or resume_character_id ~= scoped_character_id then
            data.resume = nil
        else
            data.resume.character_id = scoped_character_id
        end
    end
    return data
end

local function load_state_root()
    local chunk = loadfile(STATE_FILE_PATH)
    if not chunk then
        return normalize_state_root(nil)
    end
    local ok, data = pcall(chunk)
    if not ok or type(data) ~= "table" then
        return normalize_state_root(nil)
    end
    return normalize_state_root(data)
end

local function load_persisted_state()
    local character_id = current_character_id()
    if type(persisted_state_root) ~= "table" then
        persisted_state_root = load_state_root()
    end
    if type(character_id) ~= "string" or character_id == "" then
        persisted_state_character_id = nil
        return default_persisted_state()
    end
    if type(persisted_state_root.characters) ~= "table" then
        persisted_state_root.characters = {}
    end
    if type(persisted_state_root.characters[character_id]) ~= "table" then
        persisted_state_root.characters[character_id] = default_persisted_state(character_id)
    end
    persisted_state_character_id = character_id
    return sanitize_persisted_state(persisted_state_root.characters[character_id], character_id)
end

local function save_persisted_state(data)
    local character_id = current_character_id()
    if type(character_id) ~= "string" or character_id == "" then
        return false, "character persistence id unavailable; treasure state not saved"
    end
    if type(persisted_state_root) ~= "table" then
        persisted_state_root = load_state_root()
    end
    if type(persisted_state_root.characters) ~= "table" then
        persisted_state_root.characters = {}
    end
    persisted_state_root.version = CHARACTER_SCOPED_STATE_VERSION
    persisted_state_root.legacy = nil
    persisted_state_root.legacy_imported_to = nil
    persisted_state_root.characters[character_id] = sanitize_persisted_state(data, character_id)
    persisted_state_character_id = character_id

    local file, err = io.open(STATE_FILE_PATH, "w")
    if not file then
        return false, err
    end
    file:write("return ")
    file:write(serialize_lua(persisted_state_root, 0))
    file:write("\n")
    file:close()
    return true
end

local function save_state_root()
    if type(persisted_state_root) ~= "table" then
        persisted_state_root = load_state_root()
    end
    persisted_state_root.version = CHARACTER_SCOPED_STATE_VERSION
    persisted_state_root.legacy = nil
    persisted_state_root.legacy_imported_to = nil
    if type(persisted_state_root.characters) ~= "table" then
        persisted_state_root.characters = {}
    end

    local file, err = io.open(STATE_FILE_PATH, "w")
    if not file then
        return false, err
    end
    file:write("return ")
    file:write(serialize_lua(persisted_state_root, 0))
    file:write("\n")
    file:close()
    return true
end

local function clear_character_resume(main_state, character_id, reason)
    character_id = trim(character_id or current_character_id())
    if character_id == "" then
        return false, "character persistence id unavailable; treasure resume not cleared"
    end
    if type(persisted_state_root) ~= "table" then
        persisted_state_root = load_state_root()
    end
    if type(persisted_state_root.characters) ~= "table" then
        persisted_state_root.characters = {}
    end
    if type(persisted_state_root.characters[character_id]) ~= "table" then
        persisted_state_root.characters[character_id] = default_persisted_state(character_id)
    end

    local data = sanitize_persisted_state(persisted_state_root.characters[character_id], character_id)
    data.resume = nil
    data.resume_clear_reason = tostring(reason or "clear_character_resume")
    if type(data.treasures) == "table" then
        for key, record in pairs(data.treasures) do
            if type(record) == "table" then
                record.route = nil
                record.route_acquired = false
                record.route_cache_clear_reason = tostring(reason or "clear_character_resume")
                record.character_id = character_id
            else
                data.treasures[key] = nil
            end
        end
    end
    persisted_state_root.characters[character_id] = data
    persisted_state_character_id = character_id

    if type(main_state) == "table"
        and main_state.treasure_persisted_character_id == character_id
        and type(main_state.treasure_persisted) == "table"
    then
        main_state.treasure_persisted.resume = nil
        main_state.treasure_persisted.resume_clear_reason = data.resume_clear_reason
        if type(main_state.treasure_persisted.treasures) == "table" then
            for key, record in pairs(main_state.treasure_persisted.treasures) do
                if type(record) == "table" then
                    record.route = nil
                    record.route_acquired = false
                    record.route_cache_clear_reason = data.resume_clear_reason
                    record.character_id = character_id
                else
                    main_state.treasure_persisted.treasures[key] = nil
                end
            end
        end
    end

    return save_state_root()
end

local function ensure_runtime_state(main_state)
    if type(main_state.treasure_runtime) ~= "table" then
        main_state.treasure_runtime = {
            mode = "inactive",
            active_key = nil,
            task_match_confirmed = false,
            startup_recovery_task_pending_since = nil,
            route = nil,
            route_loaded = false,
            route_cursor = nil,
            route_nearest_index = nil,
            path_retry_count = 0,
            next_retry_at = 0,
            stage_deadline_at = 0,
            last_click_at = 0,
            last_save_err = nil,
            boss_engaged = false,
            boss_clear_started_at = 0,
            boss_zero_monster_started_at = 0,
            boss_portal_detected_at = 0,
            boss_kite_points = nil,
            boss_kite_index = 0,
            boss_kite_next_switch_at = 0,
            portal_kind = nil,
            pending_return_mainline = false,
            entry_step_index = 1,
            loot_next_at = 0,
            loot_ignore_until = 0,
            loot_stuck_reference_count = 0,
            loot_stuck_attempts = 0,
            boss_loot_pulse_count = 0,
            nearby_hold_signature = "",
            nearby_hold_started_at = 0,
            player_level = nil,
            player_level_progress = nil,
            player_level_text = nil,
            player_level_source = nil,
            player_level_next_probe_at = 0
        }
    end
    local character_id = current_character_id()
    if type(main_state.treasure_persisted) ~= "table"
        or main_state.treasure_persisted_character_id ~= character_id
    then
        main_state.treasure_persisted = load_persisted_state()
        main_state.treasure_persisted_character_id = character_id
    end
    if type(main_state.treasure_persisted.treasures) ~= "table" then
        main_state.treasure_persisted.treasures = {}
    end
    return main_state.treasure_runtime
end

local function ensure_persisted_state(main_state)
    if type(main_state) ~= "table" then
        return load_persisted_state()
    end
    local character_id = current_character_id()
    if type(main_state.treasure_persisted) ~= "table"
        or main_state.treasure_persisted_character_id ~= character_id
    then
        main_state.treasure_persisted = load_persisted_state()
        main_state.treasure_persisted_character_id = character_id
    end
    return main_state.treasure_persisted
end

local function clear_round_flags(runtime)
    runtime.boss_engaged = false
    runtime.boss_clear_started_at = 0
    runtime.boss_zero_monster_started_at = 0
    runtime.boss_portal_detected_at = 0
    runtime.boss_loot_seen_items = false
    runtime.boss_loot_empty_started_at = 0
    runtime.boss_loot_pulse_count = 0
    runtime.boss_kite_points = nil
    runtime.boss_kite_index = 0
    runtime.boss_kite_next_switch_at = 0
    runtime.portal_kind = nil
    runtime.nearby_hold_signature = ""
    runtime.nearby_hold_started_at = 0
end

local function clear_mainline_refresh_block(main_state)
    if type(main_state) ~= "table" then
        return false
    end
    local changed = false
    local scalar_fields = {
        "task_update_wait_until",
        "pause_combat_until",
        "next_task_button_click_at",
        "next_task_refresh_at",
        "next_follow_task_button_refresh_at",
        "next_task_button_soft_refresh_at"
    }
    if main_state.require_task_button_refresh == true then
        changed = true
    end
    main_state.require_task_button_refresh = false
    for _, field in ipairs(scalar_fields) do
        if (tonumber(main_state[field]) or 0) ~= 0 then
            changed = true
        end
        main_state[field] = 0
    end
    return changed
end

local function log_refresh_block_clear(ctx, hooks, cfg, runtime, reason, changed)
    if not changed then
        return
    end
    local message = string.format(
        "[Treasure] cleared mainline refresh block | key=%s mode=%s reason=%s",
        tostring(type(cfg) == "table" and (cfg.key or "") or ""),
        tostring(type(runtime) == "table" and (runtime.mode or "") or ""),
        tostring(reason or "")
    )
    if type(hooks.log_throttled) == "function" then
        hooks.log_throttled(
            ctx,
            "treasure_refresh_block_clear_" .. tostring(type(cfg) == "table" and (cfg.key or "") or "") .. "_" .. tostring(reason or ""),
            "info",
            1200,
            message
        )
        return
    end
    if type(hooks.log_info) == "function" then
        hooks.log_info(ctx, message)
    end
end

local function settle_treasure_transition(ctx, main_state, hooks, cfg, runtime, current_time, wait_ms, reason)
    local settle_ms = math.max(300, tonumber(wait_ms) or 0)
    local changed = clear_mainline_refresh_block(main_state)
    if type(main_state) == "table" then
        main_state.task_path_wait_until = 0
        main_state.task_path_refresh_requested = false
    end
    if type(runtime) == "table" then
        runtime.next_retry_at = math.max(tonumber(runtime.next_retry_at) or 0, (tonumber(current_time) or 0) + settle_ms)
    end
    if type(hooks.clear_task_target_state) == "function" then
        hooks.clear_task_target_state()
    end
    log_refresh_block_clear(ctx, hooks, cfg, runtime, reason, changed)
    if type(hooks.log_info) == "function" then
        hooks.log_info(ctx, string.format(
            "[Treasure] transition settle | key=%s mode=%s reason=%s wait=%dms",
            tostring(type(cfg) == "table" and (cfg.key or "") or ""),
            tostring(type(runtime) == "table" and (runtime.mode or "") or ""),
            tostring(reason or ""),
            settle_ms
        ))
    end
    return true
end

local function clear_treasure_combat_kite(ctx, hooks, cfg, runtime, reason)
    if type(hooks.clear_task_combat_state) == "function" then
        hooks.clear_task_combat_state()
    end
    if type(hooks.log_throttled) == "function" then
        hooks.log_throttled(ctx, "treasure_clear_combat_" .. tostring(type(cfg) == "table" and (cfg.key or "") or ""), "info", 1500,
            string.format(
                "[Treasure] cleared combat kite state | key=%s mode=%s reason=%s",
                tostring(type(cfg) == "table" and (cfg.key or "") or ""),
                tostring(type(runtime) == "table" and (runtime.mode or "") or ""),
                tostring(reason or "")
            ))
    end
end

local function set_treasure_stage(main_state, stage)
    if type(main_state) ~= "table" then
        return
    end
    main_state.stage = tostring(stage or "")
    main_state.task_combat_force_kite = false
end

local function owns_execution_mode(mode)
    local normalized = tostring(mode or "")
    return normalized == "grinding"
        or normalized == "boss_fight"
        or normalized == "boss_loot"
        or normalized == "post_boss_portal"
        or normalized == "wait_restart"
        or normalized == "wait_exit"
        or normalized == "return_mainline"
end

local function reset_runtime(main_state)
    local runtime = ensure_runtime_state(main_state)
    runtime.mode = "inactive"
    runtime.active_key = nil
    runtime.task_match_confirmed = false
    runtime.startup_recovery_task_pending_since = nil
    runtime.route = nil
    runtime.route_loaded = false
    runtime.route_cursor = nil
    runtime.route_nearest_index = nil
    runtime.path_retry_count = 0
    runtime.next_retry_at = 0
    runtime.stage_deadline_at = 0
    runtime.last_click_at = 0
    runtime.last_save_err = nil
    runtime.pending_return_mainline = false
    runtime.entry_step_index = 1
    runtime.loot_next_at = 0
    runtime.loot_ignore_until = 0
    runtime.loot_stuck_reference_count = 0
    runtime.loot_stuck_attempts = 0
    runtime.boss_loot_pulse_count = 0
    runtime.nearby_hold_signature = ""
    runtime.nearby_hold_started_at = 0
    runtime.player_level = nil
    runtime.player_level_progress = nil
    runtime.player_level_text = nil
    runtime.player_level_source = nil
    runtime.player_level_next_probe_at = 0
    clear_round_flags(runtime)
end

local function ensure_record(main_state, cfg)
    local persisted = ensure_persisted_state(main_state)
    persisted.treasures = persisted.treasures or {}
    local character_id = current_character_id()
    local key = tostring(cfg.route_store_key or cfg.key or cfg.name or "")
    local record = persisted.treasures[key]
    local record_character_id = type(record) == "table" and trim(record.character_id) or ""
    if type(record) ~= "table"
        or (type(character_id) == "string" and character_id ~= "" and record_character_id ~= character_id)
    then
        persisted.treasures[key] = default_treasure_record(character_id)
    end
    record = persisted.treasures[key]
    if type(character_id) == "string" and character_id ~= "" then
        record.character_id = character_id
    end
    return record
end

local function save_record(ctx, main_state, cfg)
    local record = ensure_record(main_state, cfg)
    record.last_update = now_ms(ctx)
    return save_persisted_state(main_state.treasure_persisted)
end

local function sanitize_resume_mode(mode)
    local normalized = tostring(mode or "")
    if normalized == "entering" then
        return "pending_entry"
    end
    if normalized == "inactive"
        or normalized == "completed"
        or normalized == "failed"
        or normalized == ""
    then
        return nil
    end
    return normalized
end

local function resume_requires_known_inside(mode)
    local normalized = tostring(mode or "")
    return normalized == "boss_fight"
        or normalized == "boss_loot"
        or normalized == "post_boss_portal"
        or normalized == "wait_restart"
end

local function build_resume_snapshot(main_state)
    local runtime = ensure_runtime_state(main_state)
    local mode = sanitize_resume_mode(runtime.mode)
    if mode == nil then
        return nil
    end
    local character_id = current_character_id()
    return {
        character_id = character_id,
        active_key = tostring(runtime.active_key or ""),
        mode = mode,
        route_cursor = tonumber(runtime.route_cursor),
        route_nearest_index = tonumber(runtime.route_nearest_index),
        route_loaded = runtime.route_loaded == true,
        entry_step_index = tonumber(runtime.entry_step_index) or 1,
        portal_kind = tostring(runtime.portal_kind or ""),
        boss_engaged = runtime.boss_engaged == true,
        pending_return_mainline = runtime.pending_return_mainline == true
    }
end

local function save_resume_snapshot(ctx, main_state, reason)
    local persisted = ensure_persisted_state(main_state)
    local snapshot = build_resume_snapshot(main_state)
    persisted.resume = snapshot
    local ok, err = save_persisted_state(persisted)
    if type(ctx) == "table" and type(ctx.log) == "table" and type(ctx.log.info) == "function" then
        if snapshot then
            ctx.log.info(string.format(
                "[Treasure] resume snapshot saved | key=%s mode=%s reason=%s save_ok=%s",
                tostring(snapshot.active_key or ""),
                tostring(snapshot.mode or ""),
                tostring(reason or ""),
                ok and "true" or "false"
            ))
        else
            ctx.log.info(string.format(
                "[Treasure] resume snapshot cleared | reason=%s save_ok=%s",
                tostring(reason or ""),
                ok and "true" or "false"
            ))
        end
    end
    return ok, err
end

local function restore_resume_snapshot(ctx, main_state, configs, player_x, player_y, player_z)
    local persisted = ensure_persisted_state(main_state)
    local snapshot = type(persisted.resume) == "table" and persisted.resume or nil
    if type(snapshot) ~= "table" then
        return false
    end

    local character_id = current_character_id()
    local snapshot_character_id = trim(snapshot.character_id)
    if type(character_id) ~= "string"
        or character_id == ""
        or snapshot_character_id == ""
        or snapshot_character_id ~= character_id
    then
        local discard_reason = (type(character_id) ~= "string" or character_id == "")
            and "character_id_unavailable"
            or (snapshot_character_id == "" and "character_id_missing" or "character_id_mismatch")
        persisted.resume = nil
        save_persisted_state(persisted)
        if type(ctx) == "table" and type(ctx.log) == "table" and type(ctx.log.info) == "function" then
            ctx.log.info(string.format(
                "[Treasure] resume snapshot discarded | key=%s mode=%s reason=%s current_id=%s snapshot_id=%s pos=%.2f, %.2f, %.2f",
                tostring(snapshot.active_key or ""),
                tostring(snapshot.mode or ""),
                tostring(discard_reason),
                tostring(character_id or ""),
                tostring(snapshot_character_id or ""),
                tonumber(player_x) or 0,
                tonumber(player_y) or 0,
                tonumber(player_z) or 0
            ))
        end
        return false
    end

    local cfg = cfg_by_key(configs, snapshot.active_key)
    local mode = sanitize_resume_mode(snapshot.mode)
    if type(cfg) ~= "table" or mode == nil then
        persisted.resume = nil
        save_persisted_state(persisted)
        return false
    end

    local record = ensure_record(main_state, cfg)
    if record.completed == true then
        persisted.resume = nil
        save_persisted_state(persisted)
        return false
    end

    local cached_route = normalize_route(record.route, cfg)
    local allow_restore = true
    local restore_reason = "allowed"
    local resume_override_mode = nil
    local near_zero_inside_landing = landing_ready(player_x, player_y, player_z, cfg.inside_landing)
    local restart_landing_resume_enabled = cfg.resume_restart_landing ~= false
        and cfg.startup_recovery_restart_landing ~= false
    local route_nearby_resume_enabled = cfg.resume_route_nearby ~= false
    local near_zero_restart_landing = restart_landing_resume_enabled
        and landing_ready(player_x, player_y, player_z, cfg.restart_landing)
    local near_zero_exit_landing = cfg_landing_ready(player_x, player_y, player_z, cfg, "exit_landing")
    if not has_reliable_world_pos(player_x, player_y) then
        local near_configured_landing = near_zero_inside_landing or near_zero_restart_landing or near_zero_exit_landing
        local exit_landing_context = mode == "wait_exit"
            or mode == "return_mainline"
            or snapshot.pending_return_mainline == true
        allow_restore = near_configured_landing
        restore_reason = near_configured_landing and "configured_zero_landing" or "invalid_position"
        if near_configured_landing then
            if near_zero_exit_landing and exit_landing_context then
                resume_override_mode = "return_mainline"
            elseif type(cached_route) == "table" and #cached_route >= math.max(1, tonumber(cfg.min_path_points) or 3) then
                resume_override_mode = "grinding"
            else
                resume_override_mode = "acquire_path"
            end
        end
    elseif mode == "pending_entry" then
        allow_restore = within_trigger(player_x, player_y, player_z, cfg.entry_trigger)
        restore_reason = allow_restore and "entry_trigger" or "outside_entry_trigger"
    else
        local near_inside_landing = landing_ready(player_x, player_y, player_z, cfg.inside_landing)
        local near_restart_landing = restart_landing_resume_enabled
            and landing_ready(player_x, player_y, player_z, cfg.restart_landing)
        local near_exit_landing = cfg_landing_ready(player_x, player_y, player_z, cfg, "exit_landing")
        local near_boss_trigger = within_trigger(player_x, player_y, player_z, type(cfg.boss) == "table" and cfg.boss.trigger or nil)
        local near_restart_portal = within_trigger(player_x, player_y, player_z, cfg.portals and cfg.portals.restart and cfg.portals.restart.trigger or nil)
        local near_exit_portal = within_trigger(player_x, player_y, player_z, cfg.portals and cfg.portals.exit and cfg.portals.exit.trigger or nil)
        local near_known_inside = near_inside_landing or near_restart_landing or near_boss_trigger or near_restart_portal or near_exit_portal
        local route_gap = nearest_route_distance(cached_route, player_x, player_y)
        local near_route = route_nearby_resume_enabled
            and route_gap <= math.max(1800, tonumber(cfg.resume_route_distance) or 2600)
        local exit_landing_context = mode == "wait_exit"
            or mode == "return_mainline"
            or snapshot.pending_return_mainline == true
        local exit_landing_shared_with_inside = near_exit_landing and (near_inside_landing or near_restart_landing)
        if near_inside_landing and resume_requires_known_inside(mode) then
            allow_restore = true
            restore_reason = "inside_landing_resume_override"
            resume_override_mode = "grinding"
        elseif near_exit_landing and exit_landing_context then
            allow_restore = true
            restore_reason = "exit_landing_resume"
            resume_override_mode = "return_mainline"
        elseif near_exit_landing
            and not exit_landing_shared_with_inside
            and mode ~= "wait_exit"
            and mode ~= "return_mainline"
        then
            allow_restore = true
            restore_reason = "exit_landing_resume_override"
            resume_override_mode = "return_mainline"
        elseif near_restart_landing and mode ~= "wait_restart" then
            allow_restore = true
            restore_reason = "restart_landing_resume_override"
            resume_override_mode = "grinding"
        elseif mode == "wait_exit" or mode == "return_mainline" then
            allow_restore = cfg_landing_ready(player_x, player_y, player_z, cfg, "exit_landing")
            restore_reason = allow_restore and "exit_landing" or "outside_exit_landing"
        elseif resume_requires_known_inside(mode) then
            allow_restore = near_known_inside or near_route
            restore_reason = allow_restore
                and (near_known_inside and "known_inside_required" or "route_nearby_resume_override")
                or "outside_known_inside"
            if allow_restore and not near_known_inside and near_route then
                resume_override_mode = "grinding"
            end
        else
            allow_restore = near_known_inside or near_route
            restore_reason = allow_restore and (near_known_inside and "known_inside_trigger" or "route_nearby") or "outside_treasure_space"
        end
    end

    if allow_restore
        and type(cfg) == "table"
        and cfg.discard_terminal_route_nearby_resume == true
        and (restore_reason == "route_nearby" or restore_reason == "route_nearby_resume_override")
        and type(cached_route) == "table"
        and #cached_route > 0
    then
        local terminal_slack = math.max(1, math.floor(tonumber(cfg.terminal_route_resume_cursor_slack) or 1))
        local route_cursor = tonumber(snapshot.route_cursor) or tonumber(snapshot.route_nearest_index)
        local terminal_point = cached_route[1]
        local terminal_distance = distance_2d(
            player_x,
            player_y,
            tonumber(type(terminal_point) == "table" and terminal_point.x),
            tonumber(type(terminal_point) == "table" and terminal_point.y)
        )
        local terminal_distance_limit = math.max(
            tonumber(cfg.terminal_route_resume_distance) or 450,
            tonumber(cfg.route_arrive_tolerance) or 150
        )
        if route_cursor ~= nil
            and route_cursor <= terminal_slack
            and terminal_distance <= terminal_distance_limit
        then
            allow_restore = false
            restore_reason = "terminal_route_nearby_resume"
        end
    end

    if not allow_restore then
        persisted.resume = nil
        save_persisted_state(persisted)
        if type(ctx) == "table" and type(ctx.log) == "table" and type(ctx.log.info) == "function" then
            ctx.log.info(string.format(
                "[Treasure] resume snapshot discarded | key=%s mode=%s reason=%s pos=%.2f, %.2f, %.2f",
                tostring(snapshot.active_key or ""),
                tostring(mode or ""),
                tostring(restore_reason or ""),
                tonumber(player_x) or 0,
                tonumber(player_y) or 0,
                tonumber(player_z) or 0
            ))
        end
        return false
    end

    local runtime = ensure_runtime_state(main_state)
    runtime.active_key = tostring(cfg.key or "")
    runtime.task_match_confirmed = false
    runtime.mode = mode
    runtime.path_retry_count = 0
    runtime.next_retry_at = 0
    runtime.stage_deadline_at = 0
    runtime.last_click_at = 0
    runtime.last_save_err = nil
    runtime.entry_step_index = tonumber(snapshot.entry_step_index) or 1
    runtime.portal_kind = trim(snapshot.portal_kind)
    runtime.pending_return_mainline = snapshot.pending_return_mainline == true
    runtime.boss_engaged = snapshot.boss_engaged == true
    runtime.boss_clear_started_at = 0
    runtime.boss_zero_monster_started_at = 0
    runtime.boss_kite_points = nil
    runtime.boss_kite_index = 0
    runtime.boss_kite_next_switch_at = 0
    runtime.loot_next_at = 0
    runtime.loot_ignore_until = 0
    runtime.loot_stuck_reference_count = 0
    runtime.loot_stuck_attempts = 0
    runtime.boss_loot_pulse_count = 0
    runtime.nearby_hold_signature = ""
    runtime.nearby_hold_started_at = 0
    runtime.player_level = nil
    runtime.player_level_progress = nil
    runtime.player_level_text = nil
    runtime.player_level_source = nil
    runtime.player_level_next_probe_at = 0

    if type(cached_route) == "table" and #cached_route >= math.max(1, tonumber(cfg.min_path_points) or 3) then
        runtime.route = cached_route
        runtime.route_loaded = true
        runtime.route_cursor = tonumber(snapshot.route_cursor)
        runtime.route_nearest_index = tonumber(snapshot.route_nearest_index)
    else
        runtime.route = nil
        runtime.route_loaded = false
        runtime.route_cursor = nil
        runtime.route_nearest_index = nil
        if mode == "grinding" or mode == "boss_loot" then
            runtime.mode = "acquire_path"
        end
    end

    if resume_override_mode ~= nil then
        runtime.mode = resume_override_mode
        clear_round_flags(runtime)
        if resume_override_mode == "grinding" then
            runtime.pending_return_mainline = false
            runtime.route_cursor = nil
            runtime.route_nearest_index = nil
        end
    end

    if type(ctx) == "table" and type(ctx.log) == "table" and type(ctx.log.info) == "function" then
        ctx.log.info(string.format(
            "[Treasure] resume snapshot restored | key=%s mode=%s route_loaded=%s runs=%d reason=%s",
            tostring(runtime.active_key or ""),
            tostring(runtime.mode or ""),
            runtime.route_loaded == true and "true" or "false",
            tonumber(record.run_count) or 0,
            tostring(restore_reason or "")
        ))
    end
    return true
end

local function simplify_route(points, cfg)
    if type(points) ~= "table" or #points <= 2 then
        return points, {
            original_points = type(points) == "table" and #points or 0,
            simplified_points = type(points) == "table" and #points or 0
        }
    end
    local simplify_cfg = type(cfg) == "table" and type(cfg.route_simplify) == "table" and cfg.route_simplify or {}
    local min_spacing = math.max(80, tonumber(simplify_cfg.min_spacing) or 220)
    local z_keep_delta = math.max(0, tonumber(simplify_cfg.z_keep_delta) or 75)
    local turn_cos_threshold = tonumber(simplify_cfg.turn_cos_threshold)
    if turn_cos_threshold == nil then
        turn_cos_threshold = 0.9925
    end
    turn_cos_threshold = math.max(-1, math.min(0.9999, turn_cos_threshold))

    local out = { clone_table(points[1]) }
    for index = 2, (#points - 1) do
        local prev_kept = out[#out]
        local current = points[index]
        local next_point = points[index + 1]
        local keep = false

        local kept_distance = distance_2d(prev_kept.x, prev_kept.y, current.x, current.y)
        if kept_distance >= min_spacing then
            keep = true
        end

        if not keep and math.abs((tonumber(current.z) or 0) - (tonumber(prev_kept.z) or 0)) >= z_keep_delta then
            keep = true
        end

        if not keep and is_valid_point(prev_kept) and is_valid_point(next_point) then
            local v1x = (tonumber(current.x) or 0) - (tonumber(prev_kept.x) or 0)
            local v1y = (tonumber(current.y) or 0) - (tonumber(prev_kept.y) or 0)
            local v2x = (tonumber(next_point.x) or 0) - (tonumber(current.x) or 0)
            local v2y = (tonumber(next_point.y) or 0) - (tonumber(current.y) or 0)
            local len1 = math.sqrt(v1x * v1x + v1y * v1y)
            local len2 = math.sqrt(v2x * v2x + v2y * v2y)
            if len1 >= 30 and len2 >= 30 then
                local cosine = ((v1x * v2x) + (v1y * v2y)) / (len1 * len2)
                if cosine <= turn_cos_threshold then
                    keep = true
                end
            end
        end

        if keep then
            out[#out + 1] = clone_table(current)
        end
    end

    local last_point = points[#points]
    if #out == 0 then
        out[1] = clone_table(last_point)
    else
        local last_kept = out[#out]
        if tonumber(last_kept.index) == tonumber(last_point.index) then
            out[#out] = clone_table(last_point)
        else
            out[#out + 1] = clone_table(last_point)
        end
    end

    return out, {
        original_points = #points,
        simplified_points = #out
    }
end

normalize_route = function(points, cfg)
    if type(points) ~= "table" then
        return nil
    end
    local out = {}
    for index, point in ipairs(points) do
        if is_valid_point(point) then
            out[#out + 1] = {
                x = tonumber(point.x),
                y = tonumber(point.y),
                z = tonumber(point.z),
                index = tonumber(point.index) or index
            }
        end
    end
    if #out == 0 then
        return nil
    end
    return simplify_route(out, cfg)
end

cfg_by_key = function(configs, key)
    if type(configs) ~= "table" then
        return nil
    end
    local lookup = tostring(key or "")
    for _, cfg in ipairs(configs) do
        if tostring(cfg and cfg.key or "") == lookup then
            return cfg
        end
    end
    return nil
end

local function current_cfg(main_state, configs)
    local runtime = ensure_runtime_state(main_state)
    return cfg_by_key(configs, runtime.active_key)
end

local function activation_context_matches(cfg, hooks, player_x, player_y, player_z)
    if type(cfg) ~= "table" or cfg.enabled == false then
        return false
    end
    local task_name = trim(type(hooks.current_task_name) == "function" and hooks.current_task_name() or "")
    local task_detail = trim(type(hooks.current_task_detail) == "function" and hooks.current_task_detail() or "")
    local map_name = trim(type(hooks.current_map_name) == "function" and hooks.current_map_name() or "")
    local task_ok = treasure_task_matches(cfg, task_name, task_detail)
    local map_ok = map_name == ""
        or cfg.map_patterns == nil
        or match_text_patterns(cfg.map_patterns, map_name)
    return task_ok and map_ok and within_trigger(player_x, player_y, player_z, cfg.entry_trigger)
end

local function should_activate(cfg, main_state, hooks, player_x, player_y, player_z, current_level)
    if not activation_context_matches(cfg, hooks, player_x, player_y, player_z) then
        return false
    end
    local record = ensure_record(main_state, cfg)
    if record.completed == true then
        return false
    end
    local target_level = configured_target_level(cfg)
    if target_level ~= nil and type(current_level) == "number" and current_level >= target_level then
        return false
    end
    return true
end

local function refresh_completed_activation_record(ctx, main_state, cfg, runtime, hooks, current_time, player_x, player_y, player_z)
    if not activation_context_matches(cfg, hooks, player_x, player_y, player_z) then
        return nil
    end

    local record = ensure_record(main_state, cfg)
    if record.completed ~= true then
        return nil
    end

    local target_level = configured_target_level(cfg)
    if target_level == nil then
        return nil
    end

    local current_level = refresh_player_level(ctx, cfg, runtime, hooks, current_time, runtime.player_level == nil)
    if type(current_level) ~= "number" then
        return nil
    end

    if current_level < target_level then
        record.completed = false
        record.route_acquired = type(record.route) == "table" and #record.route > 0 or record.route_acquired == true
        local save_ok, save_err = save_record(ctx, main_state, cfg)
        runtime.last_save_err = save_ok and nil or save_err
        if type(hooks.log_info) == "function" then
            hooks.log_info(ctx, string.format(
                "[Treasure] stale completed record cleared below target level | key=%s level=%d target_level=%d save_ok=%s err=%s",
                tostring(cfg.key or ""),
                current_level,
                target_level,
                save_ok and "true" or "false",
                tostring(save_err or "")
            ))
        end
    end

    return current_level
end

local function choose_panel_queries(cfg)
    local queries = {}
    local seen = {}
    local function push(value)
        local text = trim(value)
        if text ~= "" and not seen[text] then
            seen[text] = true
            queries[#queries + 1] = text
        end
    end
    push(cfg.panel_query)
    for _, item in ipairs(type(cfg.panel_query_fallbacks) == "table" and cfg.panel_query_fallbacks or {}) do
        push(item)
    end
    return queries
end

local function try_click_task_panel_entry(ctx, hooks, cfg)
    for _, query in ipairs(choose_panel_queries(cfg)) do
        local ok, item = hooks.click_task_panel_entry(ctx, query)
        if ok then
            return true, item, query
        end
    end
    return false, nil, nil
end

local function allow_enter_panel_query_detect(cfg)
    return not (type(cfg) == "table" and cfg.enter_detect_task_panel_query == false)
end

local function entry_distance(player_x, player_y, cfg)
    local trigger = type(cfg) == "table" and cfg.entry_trigger or nil
    if not is_valid_point(trigger) then
        return math.huge
    end
    return distance_2d(player_x, player_y, tonumber(trigger.x), tonumber(trigger.y))
end

local function activate_cfg(ctx, main_state, cfg, hooks, player_x, player_y, player_z)
    local runtime = ensure_runtime_state(main_state)
    runtime.active_key = tostring(cfg.key or "")
    runtime.task_match_confirmed = true
    runtime.startup_recovery_task_pending_since = nil
    transition_mode(ctx, hooks, cfg, runtime, "pending_entry", "activate_cfg")
    runtime.route = nil
    runtime.route_loaded = false
    runtime.route_cursor = nil
    runtime.route_nearest_index = nil
    runtime.path_retry_count = 0
    runtime.next_retry_at = 0
    runtime.stage_deadline_at = 0
    runtime.last_click_at = 0
    runtime.pending_return_mainline = false
    runtime.entry_step_index = 1
    clear_round_flags(runtime)

    local record = ensure_record(main_state, cfg)
    local cached_route, cache_stats = normalize_route(record.route, cfg)
    if type(cached_route) == "table" and #cached_route >= math.max(1, tonumber(cfg.min_path_points) or 3) then
        runtime.route = cached_route
        runtime.route_loaded = true
        record.route = clone_table(cached_route)
        if tonumber(type(cache_stats) == "table" and cache_stats.original_points or 0) > #cached_route then
            local save_ok, save_err = save_record(ctx, main_state, cfg)
            runtime.last_save_err = save_ok and nil or save_err
        end
        if type(hooks.log_info) == "function" then
            hooks.log_info(ctx, string.format(
                "[Treasure] route cache hit | key=%s points=%d original_points=%d runs=%d",
                tostring(cfg.key or ""),
                #cached_route,
                tonumber(type(cache_stats) == "table" and cache_stats.original_points or #cached_route) or #cached_route,
                tonumber(record.run_count) or 0
            ))
        end
    end

    if type(hooks.log_info) == "function" then
        hooks.log_info(ctx, string.format(
            "[Treasure] activated | key=%s runs=%d route_loaded=%s pos=%.2f, %.2f, %.2f entry_distance=%.2f",
            tostring(cfg.key or ""),
            tonumber(record.run_count) or 0,
            runtime.route_loaded and "true" or "false",
            tonumber(player_x) or 0,
            tonumber(player_y) or 0,
            tonumber(player_z) or 0,
            tonumber(entry_distance(player_x, player_y, cfg)) or 0
        ))
    end
end

local function route_destination(cfg, runtime)
    local route = runtime.route
    if type(route) ~= "table" or #route == 0 then
        return nil
    end
    local point = route[1]
    if not is_valid_point(point) then
        return nil
    end
    return {
        x = tonumber(point.x),
        y = tonumber(point.y),
        z = tonumber(point.z)
    }
end

local function reject_acquired_route(cfg, route)
    if type(cfg) ~= "table" or type(route) ~= "table" or #route <= 0 then
        return nil, nil
    end
    local first_point = route[1]
    local reject_trigger = type(cfg.acquire_path_reject_first_point) == "table" and cfg.acquire_path_reject_first_point or nil
    if is_valid_point(first_point) and within_trigger(first_point.x, first_point.y, first_point.z, reject_trigger) then
        return "reject_first_point", string.format(
            "first=%.2f, %.2f, %.2f reject=%.2f, %.2f, %.2f radius=%.2f",
            tonumber(first_point.x) or 0,
            tonumber(first_point.y) or 0,
            tonumber(first_point.z) or 0,
            tonumber(reject_trigger.x) or 0,
            tonumber(reject_trigger.y) or 0,
            tonumber(reject_trigger.z) or 0,
            tonumber(reject_trigger.radius) or 0
        )
    end
    return nil, nil
end

local function route_spawn_point(runtime)
    local route = type(runtime) == "table" and runtime.route or nil
    if type(route) ~= "table" or #route == 0 then
        return nil
    end
    local point = route[#route]
    if not is_valid_point(point) then
        return nil
    end
    return {
        x = tonumber(point.x),
        y = tonumber(point.y),
        z = tonumber(point.z)
    }
end

local function handle_acquire_path_ownership(ctx, hooks, cfg, current_time, player_x, player_y, player_z)
    if type(cfg) ~= "table" or type(hooks) ~= "table" then
        return
    end
    local hold_enabled = cfg.acquire_path_hold_navigation == true
    local combat_enabled = cfg.acquire_path_combat_sidecar == true
    if not hold_enabled and not combat_enabled then
        return
    end

    if hold_enabled then
        if type(hooks.clear_task_target_state) == "function" then
            hooks.clear_task_target_state()
        end
        if type(hooks.hold_navigation) == "function" then
            hooks.hold_navigation(ctx, current_time, "treasure_acquire_path")
        end
    end

    if combat_enabled and type(hooks.tick_combat_sidecar) == "function" then
        hooks.tick_combat_sidecar(ctx, current_time, player_x, player_y, player_z, {
            phase = "treasure_acquire_path",
            allow_when_main_interface_false = true
        })
    end

    if type(hooks.log_throttled) == "function" then
        hooks.log_throttled(ctx, "treasure_acquire_path_ownership_" .. tostring(cfg.key or ""), "info", 2500,
            string.format(
                "[Treasure] acquire_path owns local navigation/combat | key=%s hold=%s combat=%s",
                tostring(cfg.key or ""),
                hold_enabled and "true" or "false",
                combat_enabled and "true" or "false"
            ))
    end
end

transition_mode = function(ctx, hooks, cfg, runtime, next_mode, reason)
    if type(runtime) ~= "table" then
        return
    end
    local prev_mode = tostring(runtime.mode or "")
    local target_mode = tostring(next_mode or "")
    if prev_mode == target_mode then
        return
    end
    runtime.mode = target_mode
    if type(hooks) == "table" and type(hooks.log_info) == "function" then
        hooks.log_info(ctx, string.format(
            "[Treasure] mode transition | key=%s from=%s to=%s reason=%s",
            tostring(type(cfg) == "table" and (cfg.key or "") or ""),
            prev_mode,
            target_mode,
            tostring(reason or "")
        ))
    end
end

likely_inside_treasure = function(ctx, cfg, hooks, runtime, current_time, player_x, player_y, player_z)
    local map_name = trim(type(hooks.current_map_name) == "function" and hooks.current_map_name() or "")
    local task_name = trim(type(hooks.current_task_name) == "function" and hooks.current_task_name() or "")
    local task_detail = trim(type(hooks.current_task_detail) == "function" and hooks.current_task_detail() or "")
    local queries = choose_panel_queries(cfg)
    local treasure_name = trim(type(cfg) == "table" and cfg.name or "")
    local pos_reliable = has_reliable_world_pos(player_x, player_y)
    local entry_gap = pos_reliable and entry_distance(player_x, player_y, cfg) or math.huge
    local post_entry_grace_until = tonumber(type(runtime) == "table" and runtime.next_retry_at or 0) or 0

    if not treasure_task_matches(cfg, task_name, task_detail) then
        if type(hooks.log_throttled) == "function" then
            hooks.log_throttled(ctx, "treasure_inside_detect_task_mismatch_" .. tostring(type(cfg) == "table" and (cfg.key or "") or ""), "info", 2500,
                string.format(
                    "[Treasure] inside detection skipped by task mismatch | key=%s task=%s detail=%s pos=%.2f, %.2f, %.2f",
                    tostring(type(cfg) == "table" and (cfg.key or "") or ""),
                    tostring(task_name or ""),
                    tostring(task_detail or ""),
                    tonumber(player_x) or 0,
                    tonumber(player_y) or 0,
                    tonumber(player_z) or 0
                ))
        end
        return false, "task_mismatch"
    end

    if treasure_name ~= "" and map_name:find(treasure_name, 1, true) then
        return true, "map_name"
    end
    if match_text_patterns(type(cfg) == "table" and cfg.inside_map_patterns or nil, map_name) then
        return true, "inside_map_pattern"
    end
    if landing_ready(player_x, player_y, player_z, type(cfg) == "table" and cfg.inside_landing or nil) then
        return true, "inside_landing"
    end
    if type(cfg) == "table"
        and cfg.inside_detect_restart_landing ~= false
        and landing_ready(player_x, player_y, player_z, cfg.restart_landing)
    then
        return true, "restart_landing"
    end
    if type(cfg) ~= "table" or cfg.inside_detect_task_panel_text ~= false then
        for _, query in ipairs(queries) do
            if task_name:find(query, 1, true) or task_detail:find(query, 1, true) then
                return true, "task_panel_text"
            end
        end
    end

    local spawn = route_spawn_point(runtime)
    if pos_reliable and is_valid_point(spawn) then
        local spawn_gap = distance_2d(player_x, player_y, tonumber(spawn.x), tonumber(spawn.y))
        if spawn_gap <= math.max(600, tonumber(cfg.spawn_detect_radius) or 1200) then
            return true, "route_spawn"
        end
    end

    if runtime.route_loaded == true
        and pos_reliable
        and (tonumber(current_time) or 0) >= post_entry_grace_until
        and entry_gap >= math.max(1800, (tonumber(cfg.entry_trigger and cfg.entry_trigger.radius) or 320) * 4)
    then
        return true, "far_from_entry_with_route"
    end

    return false, nil
end

local function startup_inside_recovery_match(ctx, cfg, hooks, current_time, player_x, player_y, player_z)
    if type(cfg) ~= "table" or cfg.enabled == false then
        return false, nil
    end

    local map_name = trim(type(hooks.current_map_name) == "function" and hooks.current_map_name() or "")
    local treasure_name = trim(cfg.name or "")
    local match_reason = nil
    if treasure_name ~= "" and map_name:find(treasure_name, 1, true) then
        match_reason = "map_name"
    elseif match_text_patterns(cfg.inside_map_patterns, map_name) then
        match_reason = "inside_map_pattern"
    elseif landing_ready(player_x, player_y, player_z, cfg.inside_landing) then
        match_reason = "inside_landing"
    elseif cfg.startup_recovery_restart_landing ~= false
        and landing_ready(player_x, player_y, player_z, cfg.restart_landing)
    then
        match_reason = "restart_landing"
    end

    if match_reason == nil then
        return false, nil
    end

    local task_ok, task_name, task_detail = current_treasure_task_matches(cfg, hooks)
    if not task_ok then
        if cfg.startup_recovery_wait_for_task_panel == true
            and trim(task_name or "") == ""
            and trim(task_detail or "") == ""
            and type(hooks.refresh_current_task_name) == "function"
        then
            hooks.refresh_current_task_name(ctx, current_time)
            task_ok, task_name, task_detail = current_treasure_task_matches(cfg, hooks)
            if task_ok then
                return true, match_reason
            end
        end
        if cfg.startup_recovery_wait_for_task_panel == true
            and trim(task_name or "") == ""
            and trim(task_detail or "") == ""
        then
            return false, "task_pending", task_name, task_detail, match_reason
        end
        return false, "task_mismatch", task_name, task_detail, match_reason
    end

    return true, match_reason
end

local function recover_inside_startup_cfg(ctx, main_state, configs, hooks, current_time, player_x, player_y, player_z)
    if type(configs) ~= "table" then
        return nil
    end

    local runtime = ensure_runtime_state(main_state)
    for _, candidate in ipairs(configs) do
        local inside_match, inside_reason, task_name, task_detail, match_reason = startup_inside_recovery_match(ctx, candidate, hooks, current_time, player_x, player_y, player_z)
        if inside_reason == "task_pending" then
            local target_level = configured_target_level(candidate)
            local pending_level = nil
            if candidate.startup_recovery_activate_by_level_gate == true and target_level ~= nil then
                pending_level = refresh_player_level(ctx, candidate, runtime, hooks, current_time, runtime.player_level == nil)
                if type(pending_level) == "number" and pending_level < target_level then
                    inside_match = true
                    inside_reason = tostring(match_reason or "inside_landing") .. "_level_gate"
                    runtime.startup_recovery_task_pending_since = nil
                    if type(hooks.log_info) == "function" then
                        hooks.log_info(ctx, string.format(
                            "[Treasure] startup inside recovery activated by level gate while task panel pending | key=%s reason=%s level=%d target_level=%d pos=%.2f, %.2f, %.2f",
                            tostring(candidate.key or ""),
                            tostring(match_reason or ""),
                            pending_level,
                            target_level,
                            tonumber(player_x) or 0,
                            tonumber(player_y) or 0,
                            tonumber(player_z) or 0
                        ))
                    end
                end
            end

            if not inside_match then
                runtime.startup_recovery_task_pending_since = runtime.startup_recovery_task_pending_since or current_time
                local pending_elapsed = current_time - (tonumber(runtime.startup_recovery_task_pending_since) or current_time)
                local wait_cap_ms = tonumber(candidate.startup_recovery_task_panel_wait_cap_ms) or 9000
                if pending_elapsed <= wait_cap_ms then
                    local extend_ms = tonumber(candidate.startup_recovery_task_panel_wait_ms) or 1800
                    main_state.startup_state_resolve_until = math.max(
                        tonumber(main_state.startup_state_resolve_until) or 0,
                        current_time + extend_ms
                    )
                end
                if type(hooks.log_throttled) == "function" then
                    hooks.log_throttled(ctx, "treasure_startup_recovery_task_pending_" .. tostring(candidate.key or ""), "info", 900,
                        string.format(
                            "[Treasure] startup inside recovery waits for task panel | key=%s reason=%s level=%s target_level=%s elapsed=%dms cap=%dms pos=%.2f, %.2f, %.2f",
                            tostring(candidate.key or ""),
                            tostring(match_reason or ""),
                            tostring(pending_level or ""),
                            tostring(target_level or ""),
                            math.floor(math.max(0, pending_elapsed)),
                            math.floor(wait_cap_ms),
                            tonumber(player_x) or 0,
                            tonumber(player_y) or 0,
                            tonumber(player_z) or 0
                        ))
                end
                if pending_elapsed <= wait_cap_ms then
                    return "task_pending"
                end
            end
        end
        if inside_reason == "task_mismatch"
            and candidate.startup_recovery_allow_task_mismatch_by_level_gate == true
            and tostring(match_reason or "") == "inside_landing"
        then
            local target_level = configured_target_level(candidate)
            local mismatch_level = nil
            if target_level ~= nil then
                mismatch_level = refresh_player_level(ctx, candidate, runtime, hooks, current_time, runtime.player_level == nil)
                if type(mismatch_level) == "number" and mismatch_level < target_level then
                    inside_match = true
                    inside_reason = "inside_landing_task_mismatch_level_gate"
                    runtime.startup_recovery_task_pending_since = nil
                    if type(hooks.log_info) == "function" then
                        hooks.log_info(ctx, string.format(
                            "[Treasure] startup inside recovery activated by level gate despite task mismatch | key=%s level=%d target_level=%d task=%s detail=%s pos=%.2f, %.2f, %.2f",
                            tostring(candidate.key or ""),
                            mismatch_level,
                            target_level,
                            tostring(task_name or ""),
                            tostring(task_detail or ""),
                            tonumber(player_x) or 0,
                            tonumber(player_y) or 0,
                            tonumber(player_z) or 0
                        ))
                    end
                end
            end
        end
        if inside_reason == "task_mismatch" and type(hooks.log_throttled) == "function" then
            hooks.log_throttled(ctx, "treasure_startup_recovery_task_mismatch_" .. tostring(candidate.key or ""), "info", 2500,
                string.format(
                    "[Treasure] startup inside recovery skipped by task mismatch | key=%s task=%s detail=%s pos=%.2f, %.2f, %.2f",
                    tostring(candidate.key or ""),
                    tostring(task_name or ""),
                    tostring(task_detail or ""),
                    tonumber(player_x) or 0,
                    tonumber(player_y) or 0,
                    tonumber(player_z) or 0
                ))
        end
        if inside_match then
            local record = ensure_record(main_state, candidate)
            local target_level = configured_target_level(candidate)
            local candidate_level = nil
            if target_level ~= nil then
                candidate_level = refresh_player_level(ctx, candidate, runtime, hooks, current_time, runtime.player_level == nil)
            end

            if record.completed == true
                and target_level ~= nil
                and type(candidate_level) == "number"
                and candidate_level < target_level
            then
                record.completed = false
                record.route_acquired = type(record.route) == "table" and #record.route > 0 or record.route_acquired == true
                local save_ok, save_err = save_record(ctx, main_state, candidate)
                runtime.last_save_err = save_ok and nil or save_err
                if type(hooks.log_info) == "function" then
                    hooks.log_info(ctx, string.format(
                        "[Treasure] stale completed record cleared by inside startup recovery | key=%s level=%d target_level=%d reason=%s save_ok=%s err=%s",
                        tostring(candidate.key or ""),
                        candidate_level,
                        target_level,
                        tostring(inside_reason or ""),
                        save_ok and "true" or "false",
                        tostring(save_err or "")
                    ))
                end
            end

            if record.completed ~= true
                and not (target_level ~= nil and type(candidate_level) == "number" and candidate_level >= target_level)
            then
                activate_cfg(ctx, main_state, candidate, hooks, player_x, player_y, player_z)
                local startup_runtime = ensure_runtime_state(main_state)
                transition_mode(
                    ctx,
                    hooks,
                    candidate,
                    startup_runtime,
                    startup_runtime.route_loaded and "grinding" or "acquire_path",
                    "startup_inside_recovery:" .. tostring(inside_reason or "")
                )
                startup_runtime.next_retry_at = current_time
                startup_runtime.stage_deadline_at = current_time + math.max(5000, tonumber(candidate.transition_timeout_ms) or 15000)
                startup_runtime.entry_step_index = 1
                if type(hooks.clear_task_target_state) == "function" then
                    hooks.clear_task_target_state()
                end
                clear_treasure_combat_kite(ctx, hooks, candidate, startup_runtime, "startup_inside_recovery")
                log_refresh_block_clear(ctx, hooks, candidate, startup_runtime, "startup_inside_recovery", clear_mainline_refresh_block(main_state))
                if type(hooks.log_info) == "function" then
                    hooks.log_info(ctx, string.format(
                        "[Treasure] startup inside recovery activated | key=%s reason=%s level=%s target_level=%s pos=%.2f, %.2f, %.2f",
                        tostring(candidate.key or ""),
                        tostring(inside_reason or ""),
                        tostring(candidate_level or ""),
                        tostring(target_level or ""),
                        tonumber(player_x) or 0,
                        tonumber(player_y) or 0,
                        tonumber(player_z) or 0
                    ))
                end
                return candidate
            end
        end
    end

    return nil
end

local function find_nearest_route_index(player_x, player_y, route)
    if type(route) ~= "table" or #route == 0 then
        return nil, nil
    end
    local nearest_index = nil
    local nearest_distance = nil
    for index, point in ipairs(route) do
        local distance = distance_2d(player_x, player_y, tonumber(point.x), tonumber(point.y))
        if nearest_distance == nil or distance < nearest_distance then
            nearest_index = index
            nearest_distance = distance
        end
    end
    return nearest_index, nearest_distance
end

local function resolve_boss_anchor(cfg, runtime)
    local boss = type(cfg) == "table" and cfg.boss or nil
    local trigger = type(boss) == "table" and boss.trigger or nil
    if is_valid_point(trigger) and (tonumber(trigger.x) ~= 0 or tonumber(trigger.y) ~= 0) then
        return {
            x = tonumber(trigger.x),
            y = tonumber(trigger.y),
            z = tonumber(trigger.z),
            radius = math.max(220, tonumber(trigger.radius) or 900),
            z_tolerance = math.max(0, tonumber(trigger.z_tolerance) or 420)
        }
    end
    if type(trigger) == "table" and trigger.use_route_destination == true then
        local destination = route_destination(cfg, runtime)
        if is_valid_point(destination) then
            return {
                x = tonumber(destination.x),
                y = tonumber(destination.y),
                z = tonumber(destination.z),
                radius = math.max(220, tonumber(trigger.radius) or 900),
                z_tolerance = math.max(0, tonumber(trigger.z_tolerance) or 420)
            }
        end
    end
    return nil
end

local function resolve_boss_loot_anchor(cfg, runtime)
    local boss = type(cfg) == "table" and cfg.boss or nil
    local loot_anchor = type(boss) == "table" and boss.loot_anchor or nil
    if is_valid_point(loot_anchor) and (tonumber(loot_anchor.x) ~= 0 or tonumber(loot_anchor.y) ~= 0) then
        return {
            x = tonumber(loot_anchor.x),
            y = tonumber(loot_anchor.y),
            z = tonumber(loot_anchor.z),
            radius = math.max(180, tonumber(loot_anchor.radius) or tonumber(loot_anchor.distance) or 260),
            z_tolerance = math.max(0, tonumber(loot_anchor.z_tolerance) or 420),
            explicit = true
        }
    end
    local boss_anchor = resolve_boss_anchor(cfg, runtime)
    if type(boss_anchor) == "table" then
        boss_anchor.explicit = false
    end
    return boss_anchor
end

local function should_enable_boss_phase(cfg)
    local boss = type(cfg) == "table" and cfg.boss or nil
    local trigger = type(boss) == "table" and boss.trigger or nil
    return type(boss) == "table"
        and boss.enabled ~= false
        and (
            (
                is_valid_point(trigger)
                and ((tonumber(trigger.x) or 0) ~= 0 or (tonumber(trigger.y) or 0) ~= 0)
            )
            or (type(trigger) == "table" and trigger.use_route_destination == true)
        )
end

local function inject_route_target(ctx, main_state, cfg, runtime, hooks, current_time, player_x, player_y)
    local route = normalize_route(runtime.route, cfg)
    if type(route) ~= "table" or #route == 0 then
        return false, "route unavailable"
    end

    main_state.task_path = clone_table(route)
    main_state.task_path_count = #route
    main_state.task_path_route = nil
    main_state.task_path_refresh_requested = false
    main_state.task_path_wait_until = 0
    main_state.task_pos = nil
    local nearest_index, nearest_distance = find_nearest_route_index(player_x, player_y, route)
    if nearest_index == nil then
        return false, "route nearest index unavailable"
    end

    local arrive_tolerance = math.max(90, tonumber(cfg.route_arrive_tolerance) or 150)
    local route_move_interval_ms = math.max(120, tonumber(cfg.route_worker_move_interval_ms)
        or tonumber(cfg.route_refresh_ms)
        or 900)
    local reanchor_forward_delta = math.max(4, tonumber(cfg.route_reanchor_forward_delta) or 8)
    local route_cursor = tonumber(runtime.route_cursor)
    if route_cursor == nil or route_cursor < 1 or route_cursor > #route then
        route_cursor = nearest_index
        if (tonumber(nearest_distance) or math.huge) <= arrive_tolerance and route_cursor > 1 then
            route_cursor = route_cursor - 1
        end
    elseif nearest_index < route_cursor then
        route_cursor = nearest_index
    elseif nearest_index >= route_cursor + reanchor_forward_delta then
        route_cursor = math.max(1, nearest_index - 1)
    end

    local current_point = route[route_cursor]
    if type(current_point) ~= "table" then
        return false, "route point unavailable"
    end
    local current_distance = distance_2d(player_x, player_y, tonumber(current_point.x), tonumber(current_point.y))
    if current_distance <= arrive_tolerance and route_cursor > 1 then
        route_cursor = route_cursor - 1
        current_point = route[route_cursor] or current_point
        current_distance = distance_2d(player_x, player_y, tonumber(current_point.x), tonumber(current_point.y))
    end

    runtime.route_cursor = route_cursor
    runtime.route_nearest_index = nearest_index

    local target = {
        x = tonumber(current_point.x),
        y = tonumber(current_point.y),
        z = tonumber(current_point.z),
        source = "treasure_path",
        path_index = tonumber(current_point.index) or route_cursor,
        route_index = route_cursor,
        path_points = #route,
        nearest_index = nearest_index,
        nearest_distance = tonumber(nearest_distance) or tonumber(current_distance) or 0,
        current_distance = tonumber(current_distance) or 0,
        path_direction = "reverse",
        route_arrive_tolerance = arrive_tolerance,
        route_stuck_skip_ms = math.max(3000, tonumber(cfg.route_stuck_skip_ms) or 10000),
        route_progress_reset_distance = math.max(40, tonumber(cfg.route_progress_reset_distance) or 80),
        move_interval_ms = route_move_interval_ms,
        route_worker_mode = "path_route",
        treasure_key = tostring(cfg.key or "")
    }
    hooks.assign_task_target(ctx, current_time, target)
    if type(hooks.log_throttled) == "function" then
        hooks.log_throttled(ctx, "treasure_grinding_target_" .. tostring(cfg.key or ""), "info", 1500,
            string.format(
                "[Treasure] grinding target injected | key=%s path_points=%d route_cursor=%d nearest_index=%d target=%.2f, %.2f, %.2f current_distance=%.2f direction=%s worker_mode=%s interval_ms=%d arrive_tolerance=%.2f",
                tostring(cfg.key or ""),
                #route,
                tonumber(route_cursor) or 0,
                tonumber(nearest_index) or 0,
                tonumber(target.x) or 0,
                tonumber(target.y) or 0,
                tonumber(target.z) or 0,
                tonumber(current_distance) or 0,
                tostring(target.path_direction or ""),
                tostring(target.route_worker_mode or ""),
                tonumber(route_move_interval_ms) or 0,
                tonumber(arrive_tolerance) or 0
            ))
        hooks.log_throttled(ctx, "treasure_task_pos_suppressed_" .. tostring(cfg.key or ""), "info", 1500,
            string.format(
                "[Treasure] task_pos suppressed during treasure route | key=%s target=%.2f, %.2f, %.2f",
                tostring(cfg.key or ""),
                tonumber(target.x) or 0,
                tonumber(target.y) or 0,
                tonumber(target.z) or 0
            ))
    end
    return true
end

local function build_treasure_boss_kite_points(cfg, boss_cfg, anchor_x, anchor_y, anchor_z, radius)
    local configured_points = type(boss_cfg) == "table" and boss_cfg.kite_points or nil
    if type(configured_points) == "table" then
        local out = {}
        for index, point in ipairs(configured_points) do
            if is_valid_point(point) then
                out[#out + 1] = {
                    x = tonumber(point.x),
                    y = tonumber(point.y),
                    z = tonumber(point.z) or anchor_z,
                    source = "treasure_boss_kite",
                    path_index = #out + 1
                }
            end
        end
        if #out >= 3 then
            for index, point in ipairs(out) do
                point.path_index = index
                point.path_points = #out
            end
            return out, "configured"
        end
    end

    radius = tonumber(radius) or 3200
    return {
        {
            x = anchor_x + radius,
            y = anchor_y,
            z = anchor_z,
            source = "treasure_boss_kite",
            path_index = 1,
            path_points = 4
        },
        {
            x = anchor_x,
            y = anchor_y + radius,
            z = anchor_z,
            source = "treasure_boss_kite",
            path_index = 2,
            path_points = 4
        },
        {
            x = anchor_x - radius,
            y = anchor_y,
            z = anchor_z,
            source = "treasure_boss_kite",
            path_index = 3,
            path_points = 4
        },
        {
            x = anchor_x,
            y = anchor_y - radius,
            z = anchor_z,
            source = "treasure_boss_kite",
            path_index = 4,
            path_points = 4
        }
    }, "generated"
end

local function has_configured_boss_kite_points(boss_cfg)
    local configured_points = type(boss_cfg) == "table" and boss_cfg.kite_points or nil
    if type(configured_points) ~= "table" then
        return false
    end
    local count = 0
    for _, point in ipairs(configured_points) do
        if is_valid_point(point) then
            count = count + 1
            if count >= 3 then
                return true
            end
        end
    end
    return false
end

local function build_treasure_boss_kite_target(ctx, cfg, runtime, hooks, current_time, player_x, player_y, boss_anchor, boss_cfg)
    local anchor_x = tonumber(boss_anchor and boss_anchor.x)
    local anchor_y = tonumber(boss_anchor and boss_anchor.y)
    local anchor_z = tonumber(boss_anchor and boss_anchor.z)
    if anchor_x == nil or anchor_y == nil or player_x == nil or player_y == nil then
        return nil
    end

    local radius = tonumber(type(boss_cfg) == "table" and boss_cfg.kite_radius) or 3200
    local configured_mode = has_configured_boss_kite_points(boss_cfg)
    local configured_switch_ms = math.max(
        TREASURE_BOSS_KITE_SWITCH_MS,
        tonumber(type(boss_cfg) == "table" and boss_cfg.kite_switch_ms) or TREASURE_BOSS_KITE_CONFIGURED_SWITCH_MS
    )
    local route_points = runtime.boss_kite_points
    local needs_rebuild = type(route_points) ~= "table" or #route_points < 3
    if needs_rebuild then
        local build_mode
        route_points, build_mode = build_treasure_boss_kite_points(cfg, boss_cfg, anchor_x, anchor_y, anchor_z, radius)
        runtime.boss_kite_points = route_points
        if configured_mode then
            runtime.boss_kite_index = 1
            runtime.boss_kite_next_switch_at = current_time + configured_switch_ms
        else
            local nearest_index = 1
            local nearest_distance = math.huge
            for index, point in ipairs(route_points) do
                local point_distance = distance_2d(player_x, player_y, point.x, point.y)
                if point_distance < nearest_distance then
                    nearest_distance = point_distance
                    nearest_index = index
                end
            end
            runtime.boss_kite_index = nearest_index
            runtime.boss_kite_next_switch_at = current_time + TREASURE_BOSS_KITE_SWITCH_MS
        end
        if type(hooks.log_info) == "function" then
            local parts = {}
            for index, point in ipairs(route_points) do
                parts[#parts + 1] = string.format(
                    "p%d=%.2f, %.2f, %.2f",
                    index,
                    tonumber(point and point.x) or 0,
                    tonumber(point and point.y) or 0,
                    tonumber(point and point.z) or 0
                )
            end
            hooks.log_info(ctx, string.format(
                "[Treasure] boss kite route built | key=%s mode=%s center=%.2f, %.2f, %.2f radius=%.2f points=%d start_index=%d %s",
                tostring(type(cfg) == "table" and (cfg.key or "") or ""),
                tostring(build_mode or ""),
                anchor_x,
                anchor_y,
                anchor_z or 0,
                radius,
                #route_points,
                tonumber(runtime.boss_kite_index) or 0,
                table.concat(parts, " ")
            ))
        end
    end

    local current_index = tonumber(runtime.boss_kite_index) or 1
    if current_index < 1 or current_index > #route_points then
        current_index = 1
    end

    local current_point = route_points[current_index]
    if type(current_point) ~= "table" then
        current_index = 1
        current_point = route_points[current_index]
    end
    if type(current_point) ~= "table" then
        return nil
    end

    local current_distance = distance_2d(player_x, player_y, current_point.x, current_point.y)
    local point_too_far = current_distance >= math.max(2400, radius * 1.55)
    local should_advance
    if configured_mode then
        should_advance = current_distance <= TREASURE_BOSS_KITE_POINT_ARRIVE_DISTANCE
            or current_time >= (tonumber(runtime.boss_kite_next_switch_at) or 0)
    else
        should_advance = current_distance <= TREASURE_BOSS_KITE_POINT_ARRIVE_DISTANCE
            or point_too_far
            or current_time >= (tonumber(runtime.boss_kite_next_switch_at) or 0)
    end
    if should_advance then
        current_index = current_index + 1
        if current_index > #route_points then
            current_index = 1
        end
        runtime.boss_kite_index = current_index
        runtime.boss_kite_next_switch_at = current_time + (configured_mode and configured_switch_ms or TREASURE_BOSS_KITE_SWITCH_MS)
        current_point = route_points[current_index]
        current_distance = distance_2d(player_x, player_y, current_point.x, current_point.y)
    else
        runtime.boss_kite_index = current_index
    end

    return {
        x = tonumber(current_point.x),
        y = tonumber(current_point.y),
        z = tonumber(current_point.z),
        source = "treasure_boss_kite",
        path_index = current_index,
        path_points = #route_points,
        current_distance = current_distance
    }
end

local function execute_treasure_route_follow(ctx, main_state, cfg, runtime, hooks, current_time, player_x, player_y, player_z)
    local allow_zero_landing = landing_ready(player_x, player_y, player_z, type(cfg) == "table" and cfg.inside_landing or nil)
        or landing_ready(player_x, player_y, player_z, type(cfg) == "table" and cfg.restart_landing or nil)
    if not has_reliable_world_pos(player_x, player_y) and not allow_zero_landing then
        set_treasure_stage(main_state, "treasure_follow_wait_position")
        if type(hooks.hold_navigation) == "function" then
            hooks.hold_navigation(ctx, current_time, "treasure_route_wait_valid_pos")
        end
        if type(hooks.clear_task_target_state) == "function" then
            hooks.clear_task_target_state()
        end
        runtime.next_retry_at = math.max(tonumber(runtime.next_retry_at) or 0, current_time + 250)
        if type(hooks.log_throttled) == "function" then
            hooks.log_throttled(ctx, "treasure_route_wait_valid_pos_" .. tostring(cfg.key or ""), "info", 1200,
                string.format(
                    "[Treasure] route executor waiting valid position | key=%s mode=%s pos=%.2f, %.2f, %.2f",
                    tostring(cfg.key or ""),
                    tostring(runtime.mode or ""),
                    tonumber(player_x) or 0,
                    tonumber(player_y) or 0,
                    tonumber(player_z) or 0
                ))
        end
        return true
    end

    local injected, inject_err = inject_route_target(ctx, main_state, cfg, runtime, hooks, current_time, player_x, player_y)
    local target = type(main_state) == "table" and type(main_state.task_target) == "table" and main_state.task_target or nil
    if not injected or type(target) ~= "table" then
        set_treasure_stage(main_state, "treasure_follow_wait")
        if type(hooks.hold_navigation) == "function" then
            hooks.hold_navigation(ctx, current_time, "treasure_route_wait")
        end
        if type(hooks.log_throttled) == "function" then
            hooks.log_throttled(ctx, "treasure_route_executor_wait_" .. tostring(cfg.key or ""), "warn", 1200,
                string.format(
                    "[Treasure] route executor waiting | key=%s injected=%s err=%s",
                    tostring(cfg.key or ""),
                    injected and "true" or "false",
                    tostring(inject_err or "")
                ))
        end
        return true
    end

    local route_cursor = tonumber(runtime.route_cursor)
    local terminal_distance = tonumber(target.current_distance) or math.huge
    local terminal_arrive_tolerance = math.max(
        90,
        tonumber(target.route_arrive_tolerance) or tonumber(cfg.route_arrive_tolerance) or 150
    )
    if type(cfg) == "table"
        and cfg.terminal_route_fail_without_boss == true
        and route_cursor ~= nil
        and route_cursor <= 1
        and terminal_distance <= terminal_arrive_tolerance
    then
        transition_mode(ctx, hooks, cfg, runtime, "failed", "terminal_route_without_boss")
        runtime.route = nil
        runtime.route_loaded = false
        runtime.route_cursor = nil
        runtime.route_nearest_index = nil
        runtime.next_retry_at = current_time + math.max(600, tonumber(cfg.path_retry_interval_ms) or 1200)
        if type(hooks.clear_task_target_state) == "function" then
            hooks.clear_task_target_state()
        end
        clear_treasure_combat_kite(ctx, hooks, cfg, runtime, "terminal_route_without_boss")
        log_refresh_block_clear(ctx, hooks, cfg, runtime, "terminal_route_without_boss", clear_mainline_refresh_block(main_state))
        if type(hooks.log_info) == "function" then
            hooks.log_info(ctx, string.format(
                "[Treasure] terminal route reached outside boss zone, releasing treasure route | key=%s pos=%.2f, %.2f, %.2f distance=%.2f tolerance=%.2f",
                tostring(cfg.key or ""),
                tonumber(player_x) or 0,
                tonumber(player_y) or 0,
                tonumber(player_z) or 0,
                terminal_distance,
                terminal_arrive_tolerance
            ))
        end
        return true
    end

    set_treasure_stage(main_state, "treasure_follow")
    if type(hooks.log_throttled) == "function" then
        hooks.log_throttled(ctx, "treasure_route_executor_owned_" .. tostring(cfg.key or ""), "info", 1500,
            string.format(
                "[Treasure] module executor arms route worker | key=%s mode=%s pos=%.2f, %.2f, %.2f worker_mode=%s",
                tostring(cfg.key or ""),
                tostring(runtime.mode or ""),
                tonumber(player_x) or 0,
                tonumber(player_y) or 0,
                tonumber(player_z) or 0,
                tostring(target.route_worker_mode or "")
            ))
    end
    local move_ok, move_err = hooks.issue_move(ctx, current_time, target)
    if type(hooks.issue_combat_pulse) == "function" then
        hooks.issue_combat_pulse(ctx, current_time, "treasure_route_follow", true)
    end
    if type(hooks.log_throttled) == "function" then
        hooks.log_throttled(ctx, "treasure_route_executor_move_" .. tostring(cfg.key or ""), "info", 900,
            string.format(
                "[Treasure] route executor move | key=%s pos=%.2f, %.2f, %.2f target=%.2f, %.2f, %.2f route_cursor=%d nearest_index=%d current_distance=%.2f worker_mode=%s move_ok=%s err=%s",
                tostring(cfg.key or ""),
                tonumber(player_x) or 0,
                tonumber(player_y) or 0,
                tonumber(player_z) or 0,
                tonumber(target.x) or 0,
                tonumber(target.y) or 0,
                tonumber(target.z) or 0,
                tonumber(runtime.route_cursor) or 0,
                tonumber(runtime.route_nearest_index) or 0,
                tonumber(target.current_distance) or 0,
                tostring(target.route_worker_mode or ""),
                move_ok and "true" or "false",
                tostring(move_err or "")
            ))
    end
    return true
end

local function execute_treasure_boss_fight(ctx, main_state, cfg, runtime, hooks, current_time, player_x, player_y, player_z, boss_cfg, boss_anchor, record)
    local monsters = type(hooks.find_task_monsters) == "function"
        and hooks.find_task_monsters(ctx, current_time, player_x, player_y)
        or nil
    local monster_count = tonumber(type(monsters) == "table" and monsters.count or 0) or 0
    local anchor_distance = distance_2d(
        player_x,
        player_y,
        tonumber(boss_anchor and boss_anchor.x),
        tonumber(boss_anchor and boss_anchor.y)
    )
    local clear_candidate_distance = math.max(
        360,
        math.floor((tonumber(type(boss_anchor) == "table" and boss_anchor.radius) or 1200) * 0.75)
    )
    local pre_engage_anchor_distance = math.min(
        clear_candidate_distance,
        math.max(
            120,
            tonumber(type(boss_cfg) == "table" and boss_cfg.pre_engage_anchor_distance)
                or TREASURE_BOSS_PRE_ENGAGE_ANCHOR_DISTANCE
        )
    )
    local zero_monster_grace_ms = math.max(
        300,
        tonumber(type(boss_cfg) == "table" and boss_cfg.zero_monster_grace_ms)
            or TREASURE_BOSS_ZERO_MONSTER_GRACE_MS
    )
    local portal_probe_err = "deferred_until_enum_portal_visible"
    set_treasure_stage(main_state, "treasure_boss_fight")
    if type(hooks.log_throttled) == "function" then
        hooks.log_throttled(ctx, "treasure_boss_executor_owned_" .. tostring(cfg.key or ""), "info", 1200,
            string.format(
                "[Treasure] module executor owns boss tick | key=%s mode=%s monsters=%d pos=%.2f, %.2f, %.2f anchor=%.2f, %.2f, %.2f anchor_distance=%.2f clear_candidate_distance=%.2f pre_engage_anchor_distance=%.2f zero_monster_grace_ms=%d",
                tostring(cfg.key or ""),
                tostring(runtime.mode or ""),
                monster_count,
                tonumber(player_x) or 0,
                tonumber(player_y) or 0,
                tonumber(player_z) or 0,
                tonumber(boss_anchor and boss_anchor.x) or 0,
                tonumber(boss_anchor and boss_anchor.y) or 0,
                tonumber(boss_anchor and boss_anchor.z) or 0,
                anchor_distance,
                clear_candidate_distance,
                pre_engage_anchor_distance,
                zero_monster_grace_ms
            ))
    end

    if runtime.boss_engaged == true or anchor_distance <= clear_candidate_distance then
        local portal_ready, portal_kind
        portal_ready, portal_kind, portal_probe_err = detect_boss_portal_ready(ctx, hooks, cfg, runtime, record)
        if portal_ready then
            runtime.portal_kind = portal_kind
            runtime.boss_engaged = true
            runtime.boss_clear_started_at = current_time
            runtime.boss_portal_detected_at = current_time
            runtime.loot_next_at = current_time
            runtime.loot_stuck_reference_count = 0
            runtime.loot_stuck_attempts = 0
            runtime.boss_loot_pulse_count = 0
            transition_mode(
                ctx,
                hooks,
                cfg,
                runtime,
                type(boss_cfg) == "table" and boss_cfg.loot_enabled ~= false and "boss_loot" or "post_boss_portal",
                "boss_portal_enum_visible:" .. tostring(portal_kind or "")
            )
            runtime.next_retry_at = current_time
            set_treasure_stage(main_state, "treasure_boss_portal_ready")
            hooks.clear_task_target_state()
            clear_treasure_combat_kite(ctx, hooks, cfg, runtime, "boss_portal_enum_visible")
            if type(hooks.log_info) == "function" then
                hooks.log_info(ctx, string.format(
                    "[Treasure] boss portal enum visible, switching mode | key=%s portal=%s next_mode=%s",
                    tostring(cfg.key or ""),
                    tostring(portal_kind or ""),
                    tostring(runtime.mode or "")
                ))
            end
            return true
        end
    end

    if monster_count > 0 then
        runtime.boss_engaged = true
        runtime.boss_clear_started_at = 0
        runtime.boss_zero_monster_started_at = 0
        local kite_target = build_treasure_boss_kite_target(ctx, cfg, runtime, hooks, current_time, player_x, player_y, boss_anchor, boss_cfg)
        if type(kite_target) == "table" then
            hooks.issue_move(ctx, current_time, kite_target)
        else
            hooks.issue_move(ctx, current_time, {
                x = tonumber(boss_anchor and boss_anchor.x),
                y = tonumber(boss_anchor and boss_anchor.y),
                z = tonumber(boss_anchor and boss_anchor.z),
                source = "treasure_boss_anchor",
                path_index = 0,
                path_points = 0
            })
        end
        if type(hooks.issue_combat_pulse) == "function" then
            hooks.issue_combat_pulse(ctx, current_time, "treasure_boss_kite", true)
        end
        if type(hooks.log_throttled) == "function" then
            hooks.log_throttled(ctx, "treasure_boss_fight_move_" .. tostring(cfg.key or ""), "info", 900,
                string.format(
                    "[Treasure] boss fight kite move | key=%s monsters=%d portal_err=%s pos=%.2f, %.2f, %.2f kite_index=%d target=%.2f, %.2f, %.2f current_distance=%.2f",
                    tostring(cfg.key or ""),
                    monster_count,
                    tostring(portal_probe_err or ""),
                    tonumber(player_x) or 0,
                    tonumber(player_y) or 0,
                    tonumber(player_z) or 0,
                    tonumber(runtime.boss_kite_index) or 0,
                    tonumber(type(kite_target) == "table" and kite_target.x or 0) or 0,
                    tonumber(type(kite_target) == "table" and kite_target.y or 0) or 0,
                    tonumber(type(kite_target) == "table" and kite_target.z or 0) or 0,
                    tonumber(type(kite_target) == "table" and kite_target.current_distance or 0) or 0
                ))
        end
        return true
    end

    if runtime.boss_engaged ~= true and anchor_distance > pre_engage_anchor_distance then
        runtime.boss_zero_monster_started_at = 0
        hooks.issue_move(ctx, current_time, {
            x = tonumber(boss_anchor and boss_anchor.x),
            y = tonumber(boss_anchor and boss_anchor.y),
            z = tonumber(boss_anchor and boss_anchor.z),
            source = "treasure_boss_anchor_approach",
            path_index = 0,
            path_points = 0,
            move_interval_ms = 260
        })
        if type(hooks.log_throttled) == "function" then
            hooks.log_throttled(ctx, "treasure_boss_anchor_approach_" .. tostring(cfg.key or ""), "info", 900,
                string.format(
                    "[Treasure] boss fight approaching anchor before portal phase | key=%s pos=%.2f, %.2f, %.2f anchor=%.2f, %.2f, %.2f anchor_distance=%.2f pre_engage_anchor_distance=%.2f portal_err=%s",
                    tostring(cfg.key or ""),
                    tonumber(player_x) or 0,
                    tonumber(player_y) or 0,
                    tonumber(player_z) or 0,
                    tonumber(boss_anchor and boss_anchor.x) or 0,
                    tonumber(boss_anchor and boss_anchor.y) or 0,
                    tonumber(boss_anchor and boss_anchor.z) or 0,
                    anchor_distance,
                    pre_engage_anchor_distance,
                    tostring(portal_probe_err or "")
                ))
        end
        return true
    end

    if (tonumber(runtime.boss_zero_monster_started_at) or 0) <= 0 then
        runtime.boss_zero_monster_started_at = current_time
        if type(hooks.log_info) == "function" then
            hooks.log_info(ctx, string.format(
                "[Treasure] boss zero-monster grace started | key=%s engaged=%s anchor_distance=%.2f pre_engage_anchor_distance=%.2f zero_monster_grace_ms=%d",
                tostring(cfg.key or ""),
                runtime.boss_engaged == true and "true" or "false",
                anchor_distance,
                pre_engage_anchor_distance,
                zero_monster_grace_ms
            ))
        end
    end
    local zero_monster_ms = current_time - (tonumber(runtime.boss_zero_monster_started_at) or current_time)
    if zero_monster_ms < zero_monster_grace_ms then
        if runtime.boss_engaged == true then
            local kite_target = build_treasure_boss_kite_target(ctx, cfg, runtime, hooks, current_time, player_x, player_y, boss_anchor, boss_cfg)
            if type(kite_target) == "table" then
                hooks.issue_move(ctx, current_time, kite_target)
            end
            if type(hooks.issue_combat_pulse) == "function" then
                hooks.issue_combat_pulse(ctx, current_time, "treasure_boss_zero_grace", true)
            end
        else
            if type(hooks.hold_navigation) == "function" then
                hooks.hold_navigation(ctx, current_time, "treasure_boss_zero_grace")
            end
        end
        if type(hooks.log_throttled) == "function" then
            hooks.log_throttled(ctx, "treasure_boss_zero_grace_" .. tostring(cfg.key or ""), "info", 900,
                string.format(
                    "[Treasure] boss zero-monster grace waiting | key=%s engaged=%s zero_monster_ms=%d zero_monster_grace_ms=%d anchor_distance=%.2f",
                    tostring(cfg.key or ""),
                    runtime.boss_engaged == true and "true" or "false",
                    zero_monster_ms,
                    zero_monster_grace_ms,
                    anchor_distance
                ))
        end
        return true
    end

    runtime.boss_clear_started_at = 0
    local enum_wait_target = nil
    if runtime.boss_engaged == true then
        enum_wait_target = build_treasure_boss_kite_target(ctx, cfg, runtime, hooks, current_time, player_x, player_y, boss_anchor, boss_cfg)
        if type(enum_wait_target) == "table" then
            hooks.issue_move(ctx, current_time, enum_wait_target)
        elseif is_valid_point(boss_anchor) then
            enum_wait_target = boss_anchor
            hooks.issue_move(ctx, current_time, {
                x = tonumber(boss_anchor.x),
                y = tonumber(boss_anchor.y),
                z = tonumber(boss_anchor.z),
                source = "treasure_boss_wait_enum_anchor",
                path_index = 0,
                path_points = 0,
                move_interval_ms = 260
            })
        end
        if type(hooks.issue_combat_pulse) == "function" then
            hooks.issue_combat_pulse(ctx, current_time, "treasure_boss_wait_enum_portal", true)
        end
    elseif type(hooks.hold_navigation) == "function" then
        hooks.hold_navigation(ctx, current_time, "treasure_boss_wait_enum_portal")
    end
    if type(hooks.log_throttled) == "function" then
        hooks.log_throttled(ctx, "treasure_boss_wait_enum_portal_" .. tostring(cfg.key or ""), "info", 900,
            string.format(
                "[Treasure] boss fight waiting enum portal | key=%s engaged=%s monsters=%d anchor_distance=%.2f clear_candidate_distance=%.2f probe_target=%.2f, %.2f, %.2f portal_err=%s",
                tostring(cfg.key or ""),
                runtime.boss_engaged == true and "true" or "false",
                monster_count,
                anchor_distance,
                clear_candidate_distance,
                tonumber(type(enum_wait_target) == "table" and enum_wait_target.x or 0) or 0,
                tonumber(type(enum_wait_target) == "table" and enum_wait_target.y or 0) or 0,
                tonumber(type(enum_wait_target) == "table" and enum_wait_target.z or 0) or 0,
                tostring(portal_probe_err or "")
            ))
    end
    return true
end

local function should_use_restart_portal(record, cfg, runtime)
    if type(runtime) == "table" and runtime.pending_return_mainline == true then
        return false
    end
    return true
end

local function portal_button_slot_from_step(portal_cfg, step)
    if type(portal_cfg) == "table" and portal_cfg.button_slot ~= nil then
        local configured = tostring(portal_cfg.button_slot or "")
        if configured ~= "" then
            return configured
        end
    end
    if type(step) ~= "table" then
        return nil
    end
    local identity = tostring(step.distance_button_name or ""):lower()
    if identity == "" and type(step.include_patterns) == "table" then
        identity = table.concat(step.include_patterns, " "):lower()
    end
    if identity:find("fightinteractiveview_c.widgettree.maptrapbtn", 1, true) then
        return "treasure_restart_portal"
    end
    if identity:find("fightinteractiveview_c.widgettree.portalbtn", 1, true) then
        return "treasure_exit_portal"
    end
    return nil
end

local function try_click_portal(ctx, hooks, portal_cfg, runtime, record)
    if type(portal_cfg) ~= "table" then
        return false, "portal cfg unavailable"
    end
    local last_fetch_err = nil
    local step = type(portal_cfg.step) == "table" and portal_cfg.step or nil
    if step then
        local slot = portal_button_slot_from_step(portal_cfg, step)
        if slot ~= nil and type(hooks.call_button_slot) == "function" then
            local cache_context = table.concat({
                tostring(portal_cfg.key or ""),
                tostring(portal_cfg.kind or ""),
                "run=" .. tostring(type(record) == "table" and (tonumber(record.run_count) or 0) or 0)
            }, ":")
            local clicked, slot_meta_or_err, slot_retryable, slot_phase = hooks.call_button_slot(ctx, slot, step, {
                reason = "treasure_portal:" .. tostring(portal_cfg.key or portal_cfg.kind or ""),
                try_cached = true,
                cache_context = cache_context
            })
            if clicked == true then
                return true
            end
            last_fetch_err = slot_meta_or_err
            if type(hooks.log_throttled) == "function" then
                hooks.log_throttled(ctx, "treasure_portal_button_slot_miss_" .. tostring(portal_cfg.key or slot), "info", 900,
                    string.format(
                        "[Treasure] portal button slot unavailable, fallback to legacy locator | key=%s kind=%s slot=%s context=%s phase=%s retryable=%s err=%s",
                        tostring(portal_cfg.key or ""),
                        tostring(portal_cfg.kind or ""),
                        tostring(slot),
                        tostring(cache_context),
                        tostring(slot_phase or ""),
                        tostring(slot_retryable),
                        tostring(slot_meta_or_err or "")
                    ))
            end
        end

        local target, fetch_err = hooks.fetch_locator_button_target(ctx, step)
        last_fetch_err = fetch_err
        if target then
            local clicked, click_err = hooks.click_locator_button_target(ctx, step, target)
            if clicked then
                return true
            end
            if click_err then
                return false, click_err
            end
        elseif portal_cfg.direct_nearest_button ~= true then
            return false, fetch_err
        end
    end
    if portal_cfg.fallback_interact == true then
        local ok, err = hooks.press_interact(ctx)
        if ok then
            return true
        end
        return false, err
    end
    return false, last_fetch_err or "portal button unavailable"
end

local function portal_item_position(ctx, hooks, item)
    if type(item) ~= "table" then
        return nil, nil, nil
    end
    if type(hooks) == "table" and type(hooks.extract_position) == "function" then
        local x, y, z = hooks.extract_position(ctx, item)
        if x ~= nil and y ~= nil then
            return x, y, z
        end
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

local function portal_item_label(item)
    if type(item) ~= "table" then
        return ""
    end
    for _, key in ipairs({
        "name", "Name", "text", "Text", "fullname", "Fullname",
        "displayName", "DisplayName", "title", "Title", "classname", "ClassName"
    }) do
        local value = trim(item[key])
        if value ~= "" then
            return value
        end
    end
    return ""
end

local function match_enum_portal(ctx, hooks, portal_cfg, items)
    local trigger = type(portal_cfg) == "table" and portal_cfg.trigger or nil
    if not is_valid_point(trigger) then
        return nil
    end
    if type(items) ~= "table" then
        return nil
    end

    local best = nil
    for index, item in ipairs(items) do
        local portal_x, portal_y, portal_z = portal_item_position(ctx, hooks, item)
        if portal_x ~= nil and portal_y ~= nil and within_trigger(portal_x, portal_y, portal_z, trigger) then
            local trigger_distance = distance_2d(portal_x, portal_y, tonumber(trigger.x), tonumber(trigger.y))
            if not best or trigger_distance < (tonumber(best.trigger_distance) or math.huge) then
                best = {
                    index = index,
                    item = item,
                    x = portal_x,
                    y = portal_y,
                    z = portal_z,
                    label = portal_item_label(item),
                    trigger_distance = trigger_distance
                }
            end
        end
    end
    return best
end

detect_boss_portal_ready = function(ctx, hooks, cfg, runtime, record)
    local portals = type(cfg) == "table" and cfg.portals or nil
    if type(portals) ~= "table" then
        return false, nil, "portal config unavailable"
    end

    local use_restart = should_use_restart_portal(record, cfg, runtime)
    local primary_kind = use_restart and "restart" or "exit"
    local primary_cfg = portals[primary_kind]
    local secondary_kind = use_restart and "exit" or "restart"
    local secondary_cfg = portals[secondary_kind]
    if type(hooks.enum_portals) ~= "function" then
        return false, nil, "EnumPortal hook unavailable"
    end

    local items, enum_err = hooks.enum_portals(ctx)
    if type(items) ~= "table" then
        return false, nil, enum_err or "EnumPortal failed"
    end

    local primary_match = match_enum_portal(ctx, hooks, primary_cfg, items)
    local secondary_match = match_enum_portal(ctx, hooks, secondary_cfg, items)
    local matched_kind = primary_match and primary_kind or (secondary_match and secondary_kind or nil)
    local matched = primary_match or secondary_match
    if matched then
        if type(hooks.log_info) == "function" then
            hooks.log_info(ctx, string.format(
                "[Treasure] boss portal enum matched | key=%s selected=%s matched=%s count=%d index=%d label=%s pos=%.2f, %.2f, %.2f trigger_distance=%.2f",
                tostring(cfg.key or ""),
                tostring(primary_kind),
                tostring(matched_kind or ""),
                #items,
                tonumber(matched.index) or 0,
                tostring(matched.label or ""),
                tonumber(matched.x) or 0,
                tonumber(matched.y) or 0,
                tonumber(matched.z) or 0,
                tonumber(matched.trigger_distance) or 0
            ))
        end
        return true, primary_kind
    end

    if type(hooks.log_throttled) == "function" then
        hooks.log_throttled(ctx, "treasure_boss_portal_probe_miss_" .. tostring(cfg.key or ""), "info", 1200,
            string.format(
                "[Treasure] boss portal enum miss | key=%s preferred=%s count=%d",
                tostring(cfg.key or ""),
                tostring(primary_kind),
                #items
            ))
    end

    return false, nil, "EnumPortal no configured portal matched count=" .. tostring(#items)
end

local function move_to_portal(ctx, hooks, current_time, player_x, player_y, portal_cfg)
    local trigger = type(portal_cfg) == "table" and portal_cfg.trigger or nil
    if not is_valid_point(trigger) or (tonumber(trigger.x) == 0 and tonumber(trigger.y) == 0) then
        return false
    end
    local interact_distance = math.max(120, tonumber(portal_cfg.interact_distance) or 260)
    local distance = distance_2d(player_x, player_y, tonumber(trigger.x), tonumber(trigger.y))
    if distance <= interact_distance then
        return false
    end
    hooks.issue_move(ctx, current_time, {
        x = tonumber(trigger.x),
        y = tonumber(trigger.y),
        z = tonumber(trigger.z),
        source = "treasure_portal",
        path_index = 0,
        path_points = 0,
        move_interval_ms = 260
    })
    if type(hooks.log_throttled) == "function" then
        hooks.log_throttled(ctx, "treasure_move_to_portal_" .. tostring(portal_cfg.key or portal_cfg.kind or ""), "info", 900,
            string.format(
                "[Treasure] move to portal trigger | key=%s portal=%s pos=%.2f, %.2f target=%.2f, %.2f distance=%.2f interact_distance=%.2f",
                tostring(portal_cfg.key or ""),
                tostring(portal_cfg.kind or ""),
                tonumber(player_x) or 0,
                tonumber(player_y) or 0,
                tonumber(trigger.x) or 0,
                tonumber(trigger.y) or 0,
                distance,
                interact_distance
            ))
    end
    return true
end

landing_ready = function(player_x, player_y, player_z, landing)
    if not is_valid_point(landing) then
        return false
    end
    if not has_reliable_world_pos(player_x, player_y) and landing.allow_zero ~= true then
        return false
    end
    if tonumber(landing.x) == 0 and tonumber(landing.y) == 0 and landing.allow_zero ~= true then
        return false
    end
    return within_trigger(player_x, player_y, player_z, landing)
end

local function landing_has_point(landing)
    return is_valid_point(landing) and (tonumber(landing.x) ~= 0 or tonumber(landing.y) ~= 0)
end

local function get_extra_landings(cfg, key)
    if type(cfg) ~= "table" then
        return nil
    end
    local typed_key = tostring(key or "")
    local extra = cfg["extra_" .. typed_key .. "s"]
    if type(extra) == "table" then
        return extra
    end
    local list_key = typed_key:gsub("_landing$", "_landings")
    extra = cfg[list_key]
    if type(extra) == "table" then
        return extra
    end
    return nil
end

cfg_landing_ready = function(player_x, player_y, player_z, cfg, key)
    if type(cfg) ~= "table" then
        return false
    end
    if landing_ready(player_x, player_y, player_z, cfg[key]) then
        return true
    end
    local extras = get_extra_landings(cfg, key)
    if type(extras) == "table" then
        for _, landing in ipairs(extras) do
            if landing_ready(player_x, player_y, player_z, landing) then
                return true
            end
        end
    end
    return false
end

cfg_landing_has_point = function(cfg, key)
    if type(cfg) ~= "table" then
        return false
    end
    if landing_has_point(cfg[key]) then
        return true
    end
    local extras = get_extra_landings(cfg, key)
    if type(extras) == "table" then
        for _, landing in ipairs(extras) do
            if landing_has_point(landing) then
                return true
            end
        end
    end
    return false
end

local function entry_step_trigger(cfg, step, step_index)
    local trigger = type(step) == "table" and step.trigger or nil
    if is_valid_point(trigger) then
        return trigger
    end
    if tonumber(step_index) == 1 then
        trigger = type(cfg) == "table" and cfg.entry_trigger or nil
        if is_valid_point(trigger) then
            return trigger
        end
    end
    return nil
end

local function move_to_entry_step_trigger(ctx, hooks, current_time, player_x, player_y, cfg, step, step_index)
    local trigger = entry_step_trigger(cfg, step, step_index)
    if not is_valid_point(trigger) then
        return false, nil
    end
    local interact_distance = math.max(
        100,
        tonumber(type(step) == "table" and step.interact_distance or 0)
            or tonumber(type(cfg) == "table" and cfg.entry_interact_distance or 0)
            or 170
    )
    local distance = distance_2d(player_x, player_y, tonumber(trigger.x), tonumber(trigger.y))
    if distance <= interact_distance then
        return false, distance
    end
    hooks.clear_task_target_state()
    hooks.issue_move(ctx, current_time, {
        x = tonumber(trigger.x),
        y = tonumber(trigger.y),
        z = tonumber(trigger.z),
        source = "treasure_entry_step",
        path_index = 0,
        path_points = 0,
        move_interval_ms = 220
    })
    return true, distance
end

local function current_entry_step(cfg, runtime)
    local steps = type(cfg) == "table" and cfg.entry_steps or nil
    if type(steps) ~= "table" or #steps == 0 then
        return type(cfg) == "table" and cfg.entry_step or nil, 1, 1
    end
    local index = math.max(1, math.min(#steps, tonumber(runtime.entry_step_index) or 1))
    return steps[index], index, #steps
end

local function entry_step_requires_world_move(cfg, step, step_index)
    return is_valid_point(entry_step_trigger(cfg, step, step_index))
end

local function entry_ui_retry_timeout_ms(cfg, step)
    local step_timeout = tonumber(type(step) == "table" and step.ui_timeout_ms or 0) or 0
    local cfg_timeout = tonumber(type(cfg) == "table" and cfg.entry_ui_retry_timeout_ms or 0) or 0
    return math.max(3500, step_timeout, cfg_timeout, 6500)
end

local function arm_entry_ui_step_deadline(ctx, hooks, cfg, runtime, current_time, step, step_index, step_total)
    if type(runtime) ~= "table" then
        return 0
    end
    local timeout_ms = entry_ui_retry_timeout_ms(cfg, step)
    runtime.stage_deadline_at = (tonumber(current_time) or 0) + timeout_ms
    if type(hooks) == "table" and type(hooks.log_info) == "function" then
        hooks.log_info(ctx, string.format(
            "[Treasure] entry UI step armed | key=%s step=%d/%d step_key=%s timeout=%dms",
            tostring(type(cfg) == "table" and (cfg.key or "") or ""),
            tonumber(step_index) or 0,
            tonumber(step_total) or 0,
            tostring(type(step) == "table" and (step.key or "") or ""),
            timeout_ms
        ))
    end
    return timeout_ms
end

local function reset_entry_chain_to_first_step(ctx, main_state, hooks, cfg, runtime, current_time, reason, player_x, player_y, player_z, step_index, step_total, step_key)
    if type(runtime) ~= "table" then
        return
    end
    runtime.entry_step_index = 1
    runtime.stage_deadline_at = 0
    runtime.next_retry_at = (tonumber(current_time) or 0) + math.max(800, tonumber(type(cfg) == "table" and cfg.entry_retry_backoff_ms or 0) or 1200)
    if type(hooks) == "table" and type(hooks.clear_task_target_state) == "function" then
        hooks.clear_task_target_state()
    end
    log_refresh_block_clear(ctx, hooks, cfg, runtime, tostring(reason or "entry_step_reset"), clear_mainline_refresh_block(main_state))
    if type(hooks) == "table" and type(hooks.log_info) == "function" then
        hooks.log_info(ctx, string.format(
            "[Treasure] entry UI step timed out, retry first step | key=%s step=%d/%d step_key=%s reason=%s pos=%.2f, %.2f, %.2f retry_in=%dms",
            tostring(type(cfg) == "table" and (cfg.key or "") or ""),
            tonumber(step_index) or 0,
            tonumber(step_total) or 0,
            tostring(step_key or ""),
            tostring(reason or ""),
            tonumber(player_x) or 0,
            tonumber(player_y) or 0,
            tonumber(player_z) or 0,
            math.max(0, (tonumber(runtime.next_retry_at) or 0) - (tonumber(current_time) or 0))
        ))
    end
end

local function advance_entry_step_success(ctx, main_state, hooks, cfg, runtime, current_time, step, step_index, step_total, player_x, player_y, player_z)
    local settle_ms = tonumber(step and step.settle_ms) or 1500
    runtime.next_retry_at = current_time + math.max(1200, tonumber(step and step.retry_ms) or 2500)
    settle_treasure_transition(
        ctx,
        main_state,
        hooks,
        cfg,
        runtime,
        current_time,
        settle_ms,
        "entry_step_" .. tostring(step_index)
    )
    if step_index < step_total then
        runtime.entry_step_index = step_index + 1
        runtime.next_retry_at = current_time + math.max(1200, settle_ms)
        local next_step = type(cfg.entry_steps) == "table"
            and type(cfg.entry_steps[runtime.entry_step_index]) == "table"
            and cfg.entry_steps[runtime.entry_step_index]
            or nil
        if next_step and not entry_step_requires_world_move(cfg, next_step, runtime.entry_step_index) then
            arm_entry_ui_step_deadline(
                ctx,
                hooks,
                cfg,
                runtime,
                current_time,
                next_step,
                runtime.entry_step_index,
                step_total
            )
        else
            runtime.stage_deadline_at = 0
        end
        if type(hooks.log_info) == "function" then
            hooks.log_info(ctx, string.format(
                "[Treasure] entry step clicked, advance | key=%s step=%d/%d next_step=%d next_step_key=%s pos=%.2f, %.2f, %.2f next_retry_in=%dms",
                tostring(cfg.key or ""),
                tonumber(step_index) or 0,
                tonumber(step_total) or 0,
                tonumber(runtime.entry_step_index) or 0,
                tostring(type(next_step) == "table" and (next_step.key or "") or ""),
                tonumber(player_x) or 0,
                tonumber(player_y) or 0,
                tonumber(player_z) or 0,
                math.max(0, (tonumber(runtime.next_retry_at) or 0) - current_time)
            ))
        end
        return true
    end
    transition_mode(ctx, hooks, cfg, runtime, "entering", "entry_chain_completed")
    runtime.stage_deadline_at = current_time + math.max(5000, tonumber(cfg.transition_timeout_ms) or 15000)
    runtime.entry_step_index = 1
    if type(hooks.log_info) == "function" then
        hooks.log_info(ctx, string.format(
            "[Treasure] entry chain completed | key=%s pos=%.2f, %.2f, %.2f",
            tostring(cfg.key or ""),
            tonumber(player_x) or 0,
            tonumber(player_y) or 0,
            tonumber(player_z) or 0
        ))
    end
    return true
end

local function summarize_ground_items(items, limit)
    if type(items) ~= "table" then
        return ""
    end
    local names = {}
    for _, item in ipairs(items) do
        local label = trim(type(item) == "table" and (item.label or item.name or item.displayName or item.text) or "")
        if label ~= "" then
            names[#names + 1] = label
            if #names >= (limit or 3) then
                break
            end
        end
    end
    return table.concat(names, ", ")
end

local function maybe_handle_treasure_loot(ctx, main_state, cfg, runtime, hooks, current_time)
    if type(hooks.enum_ground_items) ~= "function" or type(hooks.press_loot_key) ~= "function" then
        return false
    end
    if current_time < (tonumber(runtime.loot_ignore_until) or 0) then
        return false
    end
    local items, enum_err = hooks.enum_ground_items(ctx)
    if type(items) ~= "table" then
        if type(hooks.log_throttled) == "function" then
            hooks.log_throttled(ctx, "treasure_loot_scan_err_" .. tostring(cfg.key or ""), "warn", 2500,
                "[Treasure] loot scan failed | key=" .. tostring(cfg.key or "") .. " err=" .. tostring(enum_err))
        end
        return false
    end
    local item_count = #items
    if item_count <= 0 then
        runtime.loot_stuck_reference_count = 0
        runtime.loot_stuck_attempts = 0
        return false
    end
    local summary = summarize_ground_items(items, 3)
    if current_time >= (tonumber(runtime.loot_next_at) or 0) then
        local ok, press_err = hooks.press_loot_key(ctx)
        runtime.loot_next_at = current_time + math.max(350, tonumber(cfg.loot_press_interval_ms) or 700)
        if tonumber(runtime.loot_stuck_reference_count) == item_count then
            runtime.loot_stuck_attempts = (tonumber(runtime.loot_stuck_attempts) or 0) + 1
        else
            runtime.loot_stuck_reference_count = item_count
            runtime.loot_stuck_attempts = 1
        end
        if type(hooks.log_info) == "function" then
            hooks.log_info(ctx, string.format(
                "[Treasure] loot pickup pulse | key=%s count=%d items=%s press_ok=%s err=%s attempts=%d",
                tostring(cfg.key or ""),
                item_count,
                summary ~= "" and summary or "unknown",
                ok and "true" or "false",
                tostring(press_err or ""),
                tonumber(runtime.loot_stuck_attempts) or 0
            ))
        end
        local max_attempts = math.max(1, tonumber(cfg.loot_stuck_max_attempts) or 2)
        if (tonumber(runtime.loot_stuck_attempts) or 0) >= max_attempts then
            runtime.loot_ignore_until = current_time + math.max(4000, tonumber(cfg.loot_ignore_ms) or 12000)
            runtime.loot_next_at = runtime.loot_ignore_until
            if type(hooks.log_info) == "function" then
                hooks.log_info(ctx, string.format(
                    "[Treasure] loot ignored after repeated pickup attempts | key=%s count=%d items=%s attempts=%d ignore_ms=%d",
                    tostring(cfg.key or ""),
                    item_count,
                    summary ~= "" and summary or "unknown",
                    tonumber(runtime.loot_stuck_attempts) or 0,
                    math.max(4000, tonumber(cfg.loot_ignore_ms) or 12000)
                ))
            end
            return false
        end
    end
    if type(hooks.hold_navigation) == "function" then
        hooks.hold_navigation(ctx, current_time, "treasure_loot")
    end
    return true
end

local function maybe_handle_treasure_boss_loot(ctx, main_state, cfg, runtime, hooks, current_time)
    local boss_cfg = type(cfg) == "table" and cfg.boss or nil
    if type(boss_cfg) ~= "table" or boss_cfg.loot_enabled == false then
        return false
    end
    if type(hooks.enum_ground_items) ~= "function" or type(hooks.press_loot_key) ~= "function" then
        return false
    end
    local max_pulses = tonumber(boss_cfg.loot_max_pulses)
        or tonumber(cfg.boss_loot_max_pulses)
        or TREASURE_BOSS_LOOT_MAX_PULSES
    if max_pulses ~= nil and max_pulses <= 0 then
        max_pulses = nil
    end

    local items, enum_err = hooks.enum_ground_items(ctx)
    if type(items) ~= "table" then
        if type(hooks.log_throttled) == "function" then
            hooks.log_throttled(ctx, "treasure_boss_loot_scan_err_" .. tostring(cfg.key or ""), "warn", 2500,
                "[Treasure] boss loot scan failed | key=" .. tostring(cfg.key or "") .. " err=" .. tostring(enum_err))
        end
        if type(hooks.hold_navigation) == "function" then
            hooks.hold_navigation(ctx, current_time, "treasure_boss_loot_scan")
        end
        runtime.next_retry_at = current_time + math.max(350, tonumber(cfg.loot_press_interval_ms) or 700)
        return true
    end

    local item_count = #items
    if item_count <= 0 then
        local portal_detected_at = tonumber(runtime.boss_portal_detected_at) or 0
        if portal_detected_at <= 0 then
            portal_detected_at = current_time
            runtime.boss_portal_detected_at = portal_detected_at
        end
        local loot_settle_ms = math.max(
            0,
            tonumber(boss_cfg.loot_settle_ms)
                or tonumber(boss_cfg.clear_settle_ms)
                or 1200
        )
        local portal_wait_ms = current_time - portal_detected_at
        if portal_wait_ms < loot_settle_ms then
            runtime.loot_next_at = current_time + math.max(350, tonumber(cfg.loot_press_interval_ms) or 700)
            if type(hooks.hold_navigation) == "function" then
                hooks.hold_navigation(ctx, current_time, "treasure_boss_loot_settle")
            end
            if type(hooks.log_throttled) == "function" then
                hooks.log_throttled(ctx, "treasure_boss_loot_settle_" .. tostring(cfg.key or ""), "info", 900,
                    string.format(
                        "[Treasure] boss room loot waiting after portal enum | key=%s wait_ms=%d settle_ms=%d",
                        tostring(cfg.key or ""),
                        portal_wait_ms,
                        loot_settle_ms
                    ))
            end
            return true
        end
        local empty_confirm_ms = math.max(
            0,
            tonumber(boss_cfg.loot_empty_confirm_ms)
                or tonumber(cfg.loot_empty_confirm_ms)
                or 0
        )
        local empty_confirm_without_seen = type(boss_cfg) == "table"
            and boss_cfg.loot_empty_confirm_without_seen == true
        if empty_confirm_ms > 0 and (runtime.boss_loot_seen_items == true or empty_confirm_without_seen) then
            local empty_started_at = tonumber(runtime.boss_loot_empty_started_at) or 0
            if empty_started_at <= 0 then
                empty_started_at = current_time
                runtime.boss_loot_empty_started_at = empty_started_at
            end
            local empty_wait_ms = current_time - empty_started_at
            if empty_wait_ms < empty_confirm_ms then
                local next_loot_at = tonumber(runtime.loot_next_at) or 0
                if current_time >= next_loot_at then
                    local pulse_count = tonumber(runtime.boss_loot_pulse_count) or 0
                    if max_pulses == nil or pulse_count < max_pulses then
                        local ok, press_err = hooks.press_loot_key(ctx)
                        runtime.boss_loot_pulse_count = pulse_count + 1
                        runtime.loot_next_at = current_time + math.max(350, tonumber(cfg.loot_press_interval_ms) or 700)
                        if type(hooks.log_info) == "function" then
                            hooks.log_info(ctx, string.format(
                                "[Treasure] boss room loot empty confirm pulse | key=%s wait_ms=%d confirm_ms=%d seen_items=%s press_ok=%s err=%s pulses=%d max_pulses=%s",
                                tostring(cfg.key or ""),
                                empty_wait_ms,
                                empty_confirm_ms,
                                runtime.boss_loot_seen_items == true and "true" or "false",
                                ok and "true" or "false",
                                tostring(press_err or ""),
                                tonumber(runtime.boss_loot_pulse_count) or 0,
                                tostring(max_pulses or "")
                            ))
                        end
                    elseif type(hooks.log_throttled) == "function" then
                        hooks.log_throttled(ctx, "treasure_boss_loot_empty_confirm_cap_" .. tostring(cfg.key or ""), "info", 900,
                            string.format(
                                "[Treasure] boss room loot empty confirm pulse cap reached | key=%s pulses=%d max_pulses=%d",
                                tostring(cfg.key or ""),
                                pulse_count,
                                max_pulses
                            ))
                    end
                end
                if type(hooks.hold_navigation) == "function" then
                    hooks.hold_navigation(ctx, current_time, "treasure_boss_loot_empty_confirm")
                end
                if type(hooks.log_throttled) == "function" then
                    hooks.log_throttled(ctx, "treasure_boss_loot_empty_confirm_" .. tostring(cfg.key or ""), "info", 900,
                        string.format(
                            "[Treasure] boss room loot empty confirm waiting | key=%s wait_ms=%d confirm_ms=%d seen_items=%s",
                            tostring(cfg.key or ""),
                            empty_wait_ms,
                            empty_confirm_ms,
                            runtime.boss_loot_seen_items == true and "true" or "false"
                        ))
                end
                return true
            end
        end
        runtime.loot_stuck_reference_count = 0
        runtime.loot_stuck_attempts = 0
        runtime.boss_loot_seen_items = false
        runtime.boss_loot_empty_started_at = 0
        runtime.loot_next_at = current_time + math.max(350, tonumber(cfg.loot_press_interval_ms) or 700)
        if type(hooks.log_throttled) == "function" then
            hooks.log_throttled(ctx, "treasure_boss_loot_clear_" .. tostring(cfg.key or ""), "info", 1200,
                "[Treasure] boss room loot cleared, continue to portal phase | key=" .. tostring(cfg.key or ""))
        end
        return false
    end

    runtime.boss_loot_seen_items = true
    runtime.boss_loot_empty_started_at = 0
    local summary = summarize_ground_items(items, 3)
    local next_loot_at = tonumber(runtime.loot_next_at) or 0
    if max_pulses ~= nil and max_pulses > 0 and (tonumber(runtime.boss_loot_pulse_count) or 0) >= max_pulses then
        if type(hooks.log_info) == "function" then
            hooks.log_info(ctx, string.format(
                "[Treasure] boss room loot pulse cap reached, continue to portal phase | key=%s count=%d items=%s pulses=%d max_pulses=%d",
                tostring(cfg.key or ""),
                item_count,
                summary ~= "" and summary or "unknown",
                tonumber(runtime.boss_loot_pulse_count) or 0,
                max_pulses
            ))
        end
        runtime.loot_stuck_reference_count = 0
        runtime.loot_stuck_attempts = 0
        runtime.boss_loot_seen_items = false
        runtime.boss_loot_empty_started_at = 0
        runtime.loot_next_at = current_time + math.max(350, tonumber(cfg.loot_press_interval_ms) or 700)
        return false
    end
    if current_time >= next_loot_at then
        local ok, press_err = hooks.press_loot_key(ctx)
        runtime.boss_loot_pulse_count = (tonumber(runtime.boss_loot_pulse_count) or 0) + 1
        runtime.loot_next_at = current_time + math.max(350, tonumber(cfg.loot_press_interval_ms) or 700)
        if tonumber(runtime.loot_stuck_reference_count) == item_count then
            runtime.loot_stuck_attempts = (tonumber(runtime.loot_stuck_attempts) or 0) + 1
        else
            runtime.loot_stuck_reference_count = item_count
            runtime.loot_stuck_attempts = 1
        end
        if type(hooks.log_info) == "function" then
            hooks.log_info(ctx, string.format(
                "[Treasure] boss room loot pickup pulse | key=%s count=%d items=%s press_ok=%s err=%s attempts=%d pulses=%d",
                tostring(cfg.key or ""),
                item_count,
                summary ~= "" and summary or "unknown",
                ok and "true" or "false",
                tostring(press_err or ""),
                tonumber(runtime.loot_stuck_attempts) or 0,
                tonumber(runtime.boss_loot_pulse_count) or 0
            ))
        end
        local max_attempts = math.max(1, tonumber(cfg.loot_stuck_max_attempts) or 2)
        if (tonumber(runtime.loot_stuck_attempts) or 0) >= max_attempts then
            if type(hooks.log_info) == "function" then
                hooks.log_info(ctx, string.format(
                    "[Treasure] boss room loot skipped after repeated pickup attempts | key=%s count=%d items=%s attempts=%d",
                    tostring(cfg.key or ""),
                    item_count,
                    summary ~= "" and summary or "unknown",
                    tonumber(runtime.loot_stuck_attempts) or 0
                ))
            end
            runtime.loot_stuck_reference_count = 0
            runtime.loot_stuck_attempts = 0
            runtime.loot_next_at = current_time + math.max(350, tonumber(cfg.loot_press_interval_ms) or 700)
            return false
        end
    elseif type(hooks.log_throttled) == "function" then
        hooks.log_throttled(ctx, "treasure_boss_loot_pending_" .. tostring(cfg.key or ""), "info", 900,
            string.format(
                "[Treasure] boss room loot pending | key=%s count=%d items=%s attempts=%d next_press_in=%dms",
                tostring(cfg.key or ""),
                item_count,
                summary ~= "" and summary or "unknown",
                tonumber(runtime.loot_stuck_attempts) or 0,
                math.max(0, next_loot_at - current_time)
            ))
    end

    if type(hooks.hold_navigation) == "function" then
        hooks.hold_navigation(ctx, current_time, "treasure_boss_loot")
    end
    return true
end

local function maybe_handle_treasure_nearby_monsters(ctx, main_state, cfg, runtime, hooks, current_time, player_x, player_y)
    local monsters = type(hooks.find_task_monsters) == "function"
        and hooks.find_task_monsters(ctx, current_time, player_x, player_y)
        or nil
    if type(monsters) ~= "table" or tonumber(monsters.count) == nil or tonumber(monsters.count) <= 0 then
        runtime.nearby_hold_signature = ""
        runtime.nearby_hold_started_at = 0
        return false
    end
    local nearest = monsters.nearest or {}
    local nearest_distance = tonumber(nearest.distance) or math.huge
    local hard_hold_distance = math.max(80, tonumber(cfg.nearby_monster_hard_hold_distance) or 200)
    local soft_hold_distance = math.max(hard_hold_distance, tonumber(cfg.nearby_monster_soft_hold_distance) or 350)
    local soft_hold_timeout_ms = math.max(1200, tonumber(cfg.nearby_monster_soft_hold_timeout_ms) or 4000)
    local hold_signature = string.format("%d|%.0f", tonumber(monsters.count) or 0, nearest_distance)

    if nearest_distance > soft_hold_distance then
        runtime.nearby_hold_signature = ""
        runtime.nearby_hold_started_at = 0
        if type(hooks.log_throttled) == "function" then
            hooks.log_throttled(ctx, "treasure_nearby_monsters_release_" .. tostring(cfg.key or ""), "info", 1200,
                string.format(
                    "[Treasure] nearby monsters detected but outside hold radius, continue pathing | key=%s count=%d nearest=%s distance=%.2f soft_hold_distance=%.2f",
                    tostring(cfg.key or ""),
                    tonumber(monsters.count) or 0,
                    tostring(nearest.label or ""),
                    nearest_distance,
                    soft_hold_distance
                ))
        end
        return false
    end

    if runtime.nearby_hold_signature ~= hold_signature then
        runtime.nearby_hold_signature = hold_signature
        runtime.nearby_hold_started_at = current_time
    end

    if nearest_distance > hard_hold_distance then
        local held_ms = math.max(0, current_time - (tonumber(runtime.nearby_hold_started_at) or current_time))
        if held_ms >= soft_hold_timeout_ms then
            if type(hooks.log_throttled) == "function" then
                hooks.log_throttled(ctx, "treasure_nearby_monsters_soft_timeout_" .. tostring(cfg.key or ""), "info", 1200,
                    string.format(
                        "[Treasure] nearby monsters soft hold timed out, continue pathing | key=%s count=%d nearest=%s distance=%.2f hard_hold_distance=%.2f soft_hold_distance=%.2f held_ms=%d",
                        tostring(cfg.key or ""),
                        tonumber(monsters.count) or 0,
                        tostring(nearest.label or ""),
                        nearest_distance,
                        hard_hold_distance,
                        soft_hold_distance,
                        held_ms
                    ))
            end
            return false
        end
        if type(hooks.log_throttled) == "function" then
            hooks.log_throttled(ctx, "treasure_nearby_monsters_soft_hold_" .. tostring(cfg.key or ""), "info", 1200,
                string.format(
                    "[Treasure] nearby monsters within soft hold radius, temporarily hold pathing | key=%s count=%d nearest=%s distance=%.2f hard_hold_distance=%.2f soft_hold_distance=%.2f held_ms=%d",
                    tostring(cfg.key or ""),
                    tonumber(monsters.count) or 0,
                    tostring(nearest.label or ""),
                    nearest_distance,
                    hard_hold_distance,
                    soft_hold_distance,
                    held_ms
                ))
        end
    end

    if type(hooks.hold_navigation) == "function" then
        hooks.hold_navigation(ctx, current_time, "treasure_nearby_monsters")
    end
    if type(hooks.issue_combat_pulse) == "function" then
        hooks.issue_combat_pulse(ctx, current_time, "treasure_nearby_monster_hold", true)
    end
    if type(hooks.log_throttled) == "function" then
        hooks.log_throttled(ctx, "treasure_nearby_monsters_" .. tostring(cfg.key or ""), "info", 1200,
            string.format(
                "[Treasure] nearby monsters remain, hold pathing | key=%s count=%d nearest=%s distance=%.2f",
                tostring(cfg.key or ""),
                tonumber(monsters.count) or 0,
                tostring(nearest.label or ""),
                nearest_distance
            ))
    end
    return true
end

function M.reset_state(main_state)
    reset_runtime(main_state)
end

function M.save_resume_snapshot(ctx, main_state, reason)
    return save_resume_snapshot(ctx, main_state, reason)
end

function M.clear_character_resume(ctx, main_state, character_id, reason)
    return clear_character_resume(main_state, character_id, reason)
end

function M.restore_resume_snapshot(ctx, main_state, configs, player_x, player_y, player_z)
    return restore_resume_snapshot(ctx, main_state, configs, player_x, player_y, player_z)
end

function M.should_suspend_task_refresh(main_state)
    local runtime = ensure_runtime_state(main_state)
    local mode = tostring(runtime.mode or "")
    local active_key = trim(runtime.active_key or runtime.route_store_key or "")
    if active_key == "" then
        return false
    end
    return mode ~= "" and mode ~= "inactive" and mode ~= "completed" and mode ~= "failed"
end

function M.provide_task_target_override(ctx, main_state, configs, hooks, current_time, player_x, player_y)
    local runtime = ensure_runtime_state(main_state)
    local mode = tostring(runtime.mode or "")
    if owns_execution_mode(mode) then
        if type(hooks.log_throttled) == "function" then
            hooks.log_throttled(ctx, "treasure_target_override_bypass_" .. tostring(runtime.active_key or ""), "info", 2500,
                string.format(
                    "[Treasure] mainline task target override bypassed | key=%s mode=%s reason=module_executor_owns_execution",
                    tostring(runtime.active_key or ""),
                    mode
                ))
        end
        return false
    end
    if mode ~= "grinding" then
        return false
    end
    local cfg = current_cfg(main_state, configs)
    if type(cfg) ~= "table" then
        return false
    end
    log_refresh_block_clear(ctx, hooks, cfg, runtime, "provide_task_target_override", clear_mainline_refresh_block(main_state))
    return inject_route_target(ctx, main_state, cfg, runtime, hooks, current_time, player_x, player_y)
end

function M.maybe_recover_inside_startup(ctx, main_state, configs, hooks, current_time, player_x, player_y, player_z)
    if type(current_cfg(main_state, configs)) == "table" then
        return false
    end

    local recovered = recover_inside_startup_cfg(
        ctx,
        main_state,
        configs,
        hooks,
        current_time,
        player_x,
        player_y,
        player_z
    )
    return type(recovered) == "table" or recovered == "task_pending"
end

local function recover_completed_exit_if_needed(ctx, main_state, configs, hooks, current_time, player_x, player_y, player_z)
    if type(configs) ~= "table" or not has_reliable_world_pos(player_x, player_y) then
        return nil
    end
    local runtime = ensure_runtime_state(main_state)
    for _, candidate in ipairs(configs) do
        if type(candidate) == "table" and candidate.enabled ~= false then
            local record = ensure_record(main_state, candidate)
            local exit_portal = type(candidate.portals) == "table" and candidate.portals.exit or nil
            local near_exit_portal = within_trigger(player_x, player_y, player_z, type(exit_portal) == "table" and exit_portal.trigger or nil)
            local already_landed = cfg_landing_ready(player_x, player_y, player_z, candidate, "exit_landing")
            if record.completed == true and near_exit_portal and not already_landed then
                record.completed = false
                runtime.last_save_err = nil
                runtime.active_key = tostring(candidate.key or "")
                runtime.route = nil
                runtime.route_loaded = false
                runtime.route_cursor = nil
                runtime.route_nearest_index = nil
                runtime.path_retry_count = 0
                runtime.next_retry_at = tonumber(current_time) or 0
                runtime.stage_deadline_at = 0
                runtime.pending_return_mainline = true
                clear_round_flags(runtime)
                runtime.portal_kind = "exit"
                transition_mode(ctx, hooks, candidate, runtime, "post_boss_portal", "recover_completed_exit_near_portal")
                if type(hooks.clear_task_target_state) == "function" then
                    hooks.clear_task_target_state()
                end
                log_refresh_block_clear(ctx, hooks, candidate, runtime, "recover_completed_exit_near_portal", clear_mainline_refresh_block(main_state))
                if type(hooks.log_info) == "function" then
                    hooks.log_info(ctx, string.format(
                        "[Treasure] recovered premature completed state near exit portal | key=%s pos=%.2f, %.2f, %.2f persist=deferred_until_portal_click",
                        tostring(candidate.key or ""),
                        tonumber(player_x) or 0,
                        tonumber(player_y) or 0,
                        tonumber(player_z) or 0
                    ))
                end
                return candidate
            end
        end
    end
    return nil
end

function M.maybe_handle(ctx, main_state, configs, hooks, current_time, player_x, player_y, player_z)
    local runtime = ensure_runtime_state(main_state)
    current_time = tonumber(current_time) or now_ms(ctx)
    local cfg = current_cfg(main_state, configs)

    if type(cfg) ~= "table" then
        for _, candidate in ipairs(type(configs) == "table" and configs or {}) do
            local candidate_level = refresh_completed_activation_record(
                ctx,
                main_state,
                candidate,
                runtime,
                hooks,
                current_time,
                player_x,
                player_y,
                player_z
            )
            if should_activate(candidate, main_state, hooks, player_x, player_y, player_z, candidate_level) then
                activate_cfg(ctx, main_state, candidate, hooks, player_x, player_y, player_z)
                cfg = candidate
                break
            end
        end
    end

    if type(cfg) ~= "table" then
        cfg = recover_inside_startup_cfg(ctx, main_state, configs, hooks, current_time, player_x, player_y, player_z)
    end

    if type(cfg) ~= "table" then
        cfg = recover_completed_exit_if_needed(ctx, main_state, configs, hooks, current_time, player_x, player_y, player_z)
    elseif ensure_record(main_state, cfg).completed == true then
        cfg = recover_completed_exit_if_needed(ctx, main_state, { cfg }, hooks, current_time, player_x, player_y, player_z) or cfg
    end

    if type(cfg) ~= "table" then
        return false
    end

    local mode = tostring(runtime.mode or "inactive")
    if runtime.task_match_confirmed ~= true then
        local task_ok, task_name, task_detail = current_treasure_task_matches(cfg, hooks)
        if task_ok then
            runtime.task_match_confirmed = true
        elseif mode ~= "inactive" and mode ~= "completed" and mode ~= "failed" then
            local task_info_blank = trim(task_name or "") == "" and trim(task_detail or "") == ""
            if task_info_blank then
                if type(hooks.log_throttled) == "function" then
                    hooks.log_throttled(ctx, "treasure_active_task_blank_" .. tostring(cfg.key or ""), "info", 1200,
                        string.format(
                            "[Treasure] active runtime keeps ownership during task info vacuum | key=%s mode=%s pos=%.2f, %.2f, %.2f",
                            tostring(cfg.key or ""),
                            tostring(mode),
                            tonumber(player_x) or 0,
                            tonumber(player_y) or 0,
                            tonumber(player_z) or 0
                        ))
                end
            else
                if type(hooks.log_info) == "function" then
                    hooks.log_info(ctx, string.format(
                        "[Treasure] active runtime cleared by task mismatch | key=%s mode=%s task=%s detail=%s pos=%.2f, %.2f, %.2f",
                        tostring(cfg.key or ""),
                        tostring(mode),
                        tostring(task_name or ""),
                        tostring(task_detail or ""),
                        tonumber(player_x) or 0,
                        tonumber(player_y) or 0,
                        tonumber(player_z) or 0
                    ))
                end
                reset_runtime(main_state)
                save_resume_snapshot(ctx, main_state, "task_mismatch")
                if type(hooks.clear_task_target_state) == "function" then
                    hooks.clear_task_target_state()
                end
                return false
            end
        end
    end

    local record = ensure_record(main_state, cfg)
    if record.completed == true then
        transition_mode(ctx, hooks, cfg, runtime, "completed", "record_completed")
        return false
    end

    local target_level = configured_target_level(cfg)
    local current_level = nil
    if target_level ~= nil then
        current_level = refresh_player_level(ctx, cfg, runtime, hooks, current_time, runtime.player_level == nil)
        if type(current_level) == "number" and current_level >= target_level then
            if mode == "pending_entry" or mode == "entering" then
                record.completed = true
                local save_ok, save_err = save_record(ctx, main_state, cfg)
                runtime.last_save_err = save_ok and nil or save_err
                transition_mode(ctx, hooks, cfg, runtime, "completed", "target_level_reached_before_entry")
                runtime.pending_return_mainline = false
                runtime.active_key = nil
                runtime.task_match_confirmed = false
                runtime.route = nil
                runtime.route_loaded = false
                runtime.route_cursor = nil
                runtime.route_nearest_index = nil
                clear_round_flags(runtime)
                if type(hooks.clear_task_target_state) == "function" then
                    hooks.clear_task_target_state()
                end
                if type(hooks.log_info) == "function" then
                    hooks.log_info(ctx, string.format(
                        "[Treasure] activation skipped because target level already reached | key=%s level=%d target_level=%d mode=%s save_ok=%s",
                        tostring(cfg.key or ""),
                        current_level,
                        target_level,
                        tostring(mode),
                        save_ok and "true" or "false"
                    ))
                end
                return false
            end
            if mode ~= "wait_exit"
                and mode ~= "return_mainline"
                and mode ~= "completed"
                and runtime.pending_return_mainline ~= true
            then
                runtime.pending_return_mainline = true
                if type(hooks.log_info) == "function" then
                    hooks.log_info(ctx, string.format(
                        "[Treasure] level gate reached, return mainline after current treasure loop | key=%s level=%d target_level=%d mode=%s",
                        tostring(cfg.key or ""),
                        current_level,
                        target_level,
                        tostring(mode)
                    ))
                end
            end
        end
    end

    if mode == "pending_entry" or mode == "entering" then
        local at_configured_landing = landing_ready(player_x, player_y, player_z, cfg.inside_landing)
            or landing_ready(player_x, player_y, player_z, cfg.restart_landing)
        if not has_reliable_world_pos(player_x, player_y)
            and not at_configured_landing
            and type(hooks.log_throttled) == "function"
        then
            hooks.log_throttled(ctx, "treasure_inside_detect_skip_invalid_pos_" .. tostring(cfg.key or ""), "info", 1500,
                string.format(
                    "[Treasure] skip inside-treasure inference due to invalid position | key=%s mode=%s pos=%.2f, %.2f, %.2f next_retry_in=%dms",
                    tostring(cfg.key or ""),
                    tostring(mode),
                    tonumber(player_x) or 0,
                    tonumber(player_y) or 0,
                    tonumber(player_z) or 0,
                    math.max(0, (tonumber(runtime.next_retry_at) or 0) - current_time)
                ))
        end
        local inside_treasure, inside_reason = likely_inside_treasure(ctx, cfg, hooks, runtime, current_time, player_x, player_y, player_z)
        if inside_treasure then
            transition_mode(
                ctx,
                hooks,
                cfg,
                runtime,
                runtime.route_loaded and "grinding" or "acquire_path",
                "inside_treasure_detected:" .. tostring(inside_reason or "")
            )
            runtime.next_retry_at = current_time
            runtime.stage_deadline_at = current_time + math.max(5000, tonumber(cfg.transition_timeout_ms) or 15000)
            runtime.entry_step_index = 1
            hooks.clear_task_target_state()
            clear_treasure_combat_kite(ctx, hooks, cfg, runtime, "inside_treasure_detected")
            log_refresh_block_clear(ctx, hooks, cfg, runtime, "inside_treasure_detected", clear_mainline_refresh_block(main_state))
            if type(hooks.log_info) == "function" then
                hooks.log_info(ctx, string.format(
                    "[Treasure] inside treasure detected, skip entry flow | key=%s reason=%s route_loaded=%s pos=%.2f, %.2f, %.2f entry_distance=%.2f",
                    tostring(cfg.key or ""),
                    tostring(inside_reason or ""),
                    runtime.route_loaded and "true" or "false",
                    tonumber(player_x) or 0,
                    tonumber(player_y) or 0,
                    tonumber(player_z) or 0,
                    tonumber(entry_distance(player_x, player_y, cfg)) or 0
                ))
            end
            mode = tostring(runtime.mode or mode)
        end
    end

    if mode == "pending_entry" then
        local step, step_index, step_total = current_entry_step(cfg, runtime)
        if entry_step_requires_world_move(cfg, step, step_index) then
            if not has_reliable_world_pos(player_x, player_y) then
                if tonumber(step_index) == 1 then
                    if type(hooks.hold_navigation) == "function" then
                        hooks.hold_navigation(ctx, current_time, "treasure_entry_wait_valid_pos")
                    end
                    if type(hooks.clear_task_target_state) == "function" then
                        hooks.clear_task_target_state()
                    end
                    runtime.next_retry_at = current_time + 250
                    if type(hooks.log_throttled) == "function" then
                        hooks.log_throttled(ctx, "treasure_entry_wait_valid_pos_" .. tostring(cfg.key or "") .. "_" .. tostring(step_index), "info", 1200,
                            string.format(
                                "[Treasure] entry step waiting valid position | key=%s step=%d/%d step_key=%s pos=%.2f, %.2f, %.2f",
                                tostring(cfg.key or ""),
                                tonumber(step_index) or 0,
                                tonumber(step_total) or 0,
                                tostring(type(step) == "table" and (step.key or "") or ""),
                                tonumber(player_x) or 0,
                                tonumber(player_y) or 0,
                                tonumber(player_z) or 0
                            ))
                    end
                    return true
                end
                if type(hooks.hold_navigation) == "function" then
                    hooks.hold_navigation(ctx, current_time, "treasure_entry_trigger_ui_probe")
                end
                if type(hooks.clear_task_target_state) == "function" then
                    hooks.clear_task_target_state()
                end
                local deadline_at = tonumber(runtime.stage_deadline_at) or 0
                if deadline_at <= 0 then
                    arm_entry_ui_step_deadline(ctx, hooks, cfg, runtime, current_time, step, step_index, step_total)
                    deadline_at = tonumber(runtime.stage_deadline_at) or 0
                elseif current_time > deadline_at then
                    reset_entry_chain_to_first_step(
                        ctx,
                        main_state,
                        hooks,
                        cfg,
                        runtime,
                        current_time,
                        "entry_trigger_invalid_pos_timeout",
                        player_x,
                        player_y,
                        player_z,
                        step_index,
                        step_total,
                        type(step) == "table" and (step.key or "") or ""
                    )
                    return true
                end
                if type(hooks.log_throttled) == "function" then
                    hooks.log_throttled(ctx, "treasure_entry_invalid_pos_probe_" .. tostring(cfg.key or "") .. "_" .. tostring(step_index), "info", 1200,
                        string.format(
                            "[Treasure] entry trigger invalid position, continue UI button probe | key=%s step=%d/%d step_key=%s pos=%.2f, %.2f, %.2f deadline_in=%dms",
                            tostring(cfg.key or ""),
                            tonumber(step_index) or 0,
                            tonumber(step_total) or 0,
                            tostring(type(step) == "table" and (step.key or "") or ""),
                            tonumber(player_x) or 0,
                            tonumber(player_y) or 0,
                            tonumber(player_z) or 0,
                            math.max(0, deadline_at - current_time)
                        ))
                end
            else
                local trigger = entry_step_trigger(cfg, step, step_index)
                local moved, move_distance = move_to_entry_step_trigger(ctx, hooks, current_time, player_x, player_y, cfg, step, step_index)
                if moved then
                    runtime.next_retry_at = current_time + 250
                    if type(hooks.log_throttled) == "function" then
                        hooks.log_throttled(ctx, "treasure_entry_move_" .. tostring(cfg.key or "") .. "_" .. tostring(step_index), "info", 1200,
                            string.format(
                                "[Treasure] moving to entry trigger | key=%s step=%d/%d step_key=%s distance=%.2f trigger=%.2f, %.2f, %.2f",
                                tostring(cfg.key or ""),
                                tonumber(step_index) or 0,
                                tonumber(step_total) or 0,
                                tostring(type(step) == "table" and (step.key or step.label) or ""),
                                tonumber(move_distance) or 0,
                                tonumber(trigger and trigger.x) or 0,
                                tonumber(trigger and trigger.y) or 0,
                                tonumber(trigger and trigger.z) or 0
                            )
                        )
                    end
                    return true
                end
                if type(hooks.hold_navigation) == "function" then
                    hooks.hold_navigation(ctx, current_time, "treasure_entry_trigger_ready")
                end
                if type(hooks.clear_task_target_state) == "function" then
                    hooks.clear_task_target_state()
                end
                if is_valid_point(type(step) == "table" and step.trigger or nil) then
                    local deadline_at = tonumber(runtime.stage_deadline_at) or 0
                    if deadline_at <= 0 then
                        arm_entry_ui_step_deadline(ctx, hooks, cfg, runtime, current_time, step, step_index, step_total)
                    elseif current_time > deadline_at then
                        reset_entry_chain_to_first_step(
                            ctx,
                            main_state,
                            hooks,
                            cfg,
                            runtime,
                            current_time,
                            "entry_trigger_probe_timeout",
                            player_x,
                            player_y,
                            player_z,
                            step_index,
                            step_total,
                            type(step) == "table" and (step.key or "") or ""
                        )
                        return true
                    end
                end
                if type(hooks.log_throttled) == "function" then
                    hooks.log_throttled(ctx, "treasure_entry_trigger_ready_" .. tostring(cfg.key or "") .. "_" .. tostring(step_index), "info", 1200,
                        string.format(
                            "[Treasure] entry trigger reached, probing button | key=%s step=%d/%d step_key=%s pos=%.2f, %.2f, %.2f trigger=%.2f, %.2f, %.2f deadline_in=%dms",
                            tostring(cfg.key or ""),
                            tonumber(step_index) or 0,
                            tonumber(step_total) or 0,
                            tostring(type(step) == "table" and (step.key or "") or ""),
                            tonumber(player_x) or 0,
                            tonumber(player_y) or 0,
                            tonumber(player_z) or 0,
                            tonumber(trigger and trigger.x) or 0,
                            tonumber(trigger and trigger.y) or 0,
                            tonumber(trigger and trigger.z) or 0,
                            math.max(0, (tonumber(runtime.stage_deadline_at) or 0) - current_time)
                        ))
                end
                if not is_valid_point(type(step) == "table" and step.trigger or nil) then
                    runtime.stage_deadline_at = 0
                end
            end
        end
        if not entry_step_requires_world_move(cfg, step, step_index) then
            if type(hooks.hold_navigation) == "function" then
                hooks.hold_navigation(ctx, current_time, "treasure_entry_ui_step")
            end
            if type(hooks.clear_task_target_state) == "function" then
                hooks.clear_task_target_state()
            end
            local deadline_at = tonumber(runtime.stage_deadline_at) or 0
            if deadline_at <= 0 then
                arm_entry_ui_step_deadline(ctx, hooks, cfg, runtime, current_time, step, step_index, step_total)
                deadline_at = tonumber(runtime.stage_deadline_at) or 0
            elseif current_time > deadline_at then
                reset_entry_chain_to_first_step(
                    ctx,
                    main_state,
                    hooks,
                    cfg,
                    runtime,
                    current_time,
                    "ui_step_timeout",
                    player_x,
                    player_y,
                    player_z,
                    step_index,
                    step_total,
                    type(step) == "table" and (step.key or "") or ""
                )
                return true
            end
            if type(hooks.log_throttled) == "function" then
                hooks.log_throttled(ctx, "treasure_entry_ui_wait_" .. tostring(cfg.key or "") .. "_" .. tostring(step_index), "info", 1200,
                    string.format(
                        "[Treasure] entry UI step holding navigation | key=%s step=%d/%d step_key=%s pos=%.2f, %.2f, %.2f retry_in=%dms deadline_in=%dms",
                        tostring(cfg.key or ""),
                        tonumber(step_index) or 0,
                        tonumber(step_total) or 0,
                        tostring(type(step) == "table" and (step.key or "") or ""),
                        tonumber(player_x) or 0,
                        tonumber(player_y) or 0,
                        tonumber(player_z) or 0,
                        math.max(0, (tonumber(runtime.next_retry_at) or 0) - current_time),
                        math.max(0, deadline_at - current_time)
                    ))
            end
        end
        if current_time < (tonumber(runtime.next_retry_at) or 0) then
            return true
        end
        if type(hooks.log_throttled) == "function" then
            hooks.log_throttled(ctx, "treasure_entry_step_probe_" .. tostring(cfg.key or "") .. "_" .. tostring(step_index), "info", 1800,
                string.format(
                    "[Treasure] probing entry step | key=%s step=%d/%d step_key=%s step_label=%s pos=%.2f, %.2f, %.2f",
                    tostring(cfg.key or ""),
                    tonumber(step_index) or 0,
                    tonumber(step_total) or 0,
                    tostring(type(step) == "table" and (step.key or "") or ""),
                    tostring(type(step) == "table" and (step.label or "") or ""),
                    tonumber(player_x) or 0,
                    tonumber(player_y) or 0,
                    tonumber(player_z) or 0
                ))
        end
        local target, fetch_err = hooks.fetch_locator_button_target(ctx, step)
        if target then
            if type(hooks.log_info) == "function" then
                hooks.log_info(ctx, string.format(
                    "[Treasure] entry step target found | key=%s step=%d/%d step_key=%s label=%s addr=%s pos=(%s,%s)",
                    tostring(cfg.key or ""),
                    tonumber(step_index) or 0,
                    tonumber(step_total) or 0,
                    tostring(type(step) == "table" and (step.key or "") or ""),
                    tostring(type(step) == "table" and (step.label or "") or ""),
                    tostring(target.addr or ""),
                    tostring(target.x or ""),
                    tostring(target.y or "")
                ))
            end
            local clicked, click_err = hooks.click_locator_button_target(ctx, step, target)
            if clicked then
                return advance_entry_step_success(
                    ctx,
                    main_state,
                    hooks,
                    cfg,
                    runtime,
                    current_time,
                    step,
                    step_index,
                    step_total,
                    player_x,
                    player_y,
                    player_z
                )
            end
            if type(hooks.log_throttled) == "function" then
                hooks.log_throttled(ctx, "treasure_entry_click_failed_" .. tostring(cfg.key or "") .. "_" .. tostring(step_index), "warn", 3000,
                    string.format(
                        "[Treasure] entry step click failed | key=%s step=%d/%d step_key=%s pos=%.2f, %.2f, %.2f err=%s",
                        tostring(cfg.key or ""),
                        tonumber(step_index) or 0,
                        tonumber(step_total) or 0,
                        tostring(type(step) == "table" and (step.key or "") or ""),
                        tonumber(player_x) or 0,
                        tonumber(player_y) or 0,
                        tonumber(player_z) or 0,
                        tostring(click_err)
                    ))
            end
            return true
        end
        if type(step) == "table"
            and step.fallback_interact == true
            and type(hooks.press_interact) == "function"
            and tostring(cfg.key or "") == "treasure_fourth_entry_5643_-530"
            and tostring(step.key or "") == "treasure_fourth_entry_map_trap_placeholder"
        then
            local trigger = entry_step_trigger(cfg, step, step_index)
            local fallback_distance = math.max(
                80,
                tonumber(step.fallback_interact_distance)
                    or tonumber(step.interact_distance)
                    or tonumber(type(cfg) == "table" and cfg.entry_interact_distance or 0)
                    or 170
            )
            local trigger_distance = is_valid_point(trigger)
                and distance_2d(player_x, player_y, tonumber(trigger.x), tonumber(trigger.y))
                or 0
            if (not is_valid_point(trigger)) or trigger_distance <= fallback_distance then
                local ok, err = hooks.press_interact(ctx)
                runtime.next_retry_at = current_time + math.max(1000, tonumber(step.fallback_retry_ms) or tonumber(step.retry_ms) or 2500)
                if ok then
                    if type(hooks.log_info) == "function" then
                        hooks.log_info(ctx, string.format(
                            "[Treasure] entry step interact fallback | key=%s step=%d/%d step_key=%s pos=%.2f, %.2f, %.2f trigger_distance=%.2f fallback_distance=%.2f",
                            tostring(cfg.key or ""),
                            tonumber(step_index) or 0,
                            tonumber(step_total) or 0,
                            tostring(type(step) == "table" and (step.key or "") or ""),
                            tonumber(player_x) or 0,
                            tonumber(player_y) or 0,
                            tonumber(player_z) or 0,
                            tonumber(trigger_distance) or 0,
                            tonumber(fallback_distance) or 0
                        ))
                    end
                    return advance_entry_step_success(
                        ctx,
                        main_state,
                        hooks,
                        cfg,
                        runtime,
                        current_time,
                        step,
                        step_index,
                        step_total,
                        player_x,
                        player_y,
                        player_z
                    )
                end
                if type(hooks.log_throttled) == "function" then
                    hooks.log_throttled(ctx, "treasure_entry_interact_failed_" .. tostring(cfg.key or "") .. "_" .. tostring(step_index), "warn", 3000,
                        string.format(
                            "[Treasure] entry step interact fallback failed | key=%s step=%d/%d step_key=%s pos=%.2f, %.2f, %.2f err=%s",
                            tostring(cfg.key or ""),
                            tonumber(step_index) or 0,
                            tonumber(step_total) or 0,
                            tostring(type(step) == "table" and (step.key or "") or ""),
                            tonumber(player_x) or 0,
                            tonumber(player_y) or 0,
                            tonumber(player_z) or 0,
                            tostring(err)
                        ))
                end
                return true
            end
        end
        if type(hooks.log_throttled) == "function" then
            hooks.log_throttled(ctx, "treasure_entry_wait_" .. tostring(cfg.key or "") .. "_" .. tostring(step_index), "info", 3000,
                string.format(
                    "[Treasure] waiting entry step button | key=%s step=%d/%d step_key=%s pos=%.2f, %.2f, %.2f retry_in=%dms deadline_in=%dms err=%s",
                    tostring(cfg.key or ""),
                    tonumber(step_index) or 0,
                    tonumber(step_total) or 0,
                    tostring(type(step) == "table" and (step.key or "") or ""),
                    tonumber(player_x) or 0,
                    tonumber(player_y) or 0,
                    tonumber(player_z) or 0,
                    math.max(0, (tonumber(runtime.next_retry_at) or 0) - current_time),
                    math.max(0, (tonumber(runtime.stage_deadline_at) or 0) - current_time),
                    tostring(fetch_err)
                ))
        end
        return true
    end

    if mode == "entering" then
        local panel_ok = false
        if allow_enter_panel_query_detect(cfg) then
            panel_ok = select(1, try_click_task_panel_entry(ctx, hooks, cfg))
        elseif type(hooks.log_throttled) == "function" then
            hooks.log_throttled(ctx, "treasure_enter_panel_query_disabled_" .. tostring(cfg.key or ""), "info", 2500,
                string.format(
                    "[Treasure] skip panel-query enter detect by cfg | key=%s pos=%.2f, %.2f, %.2f",
                    tostring(cfg.key or ""),
                    tonumber(player_x) or 0,
                    tonumber(player_y) or 0,
                    tonumber(player_z) or 0
                ))
        end
        if panel_ok then
            transition_mode(ctx, hooks, cfg, runtime, runtime.route_loaded and "grinding" or "acquire_path", "panel_query_detected")
            runtime.next_retry_at = current_time
            runtime.stage_deadline_at = current_time + math.max(5000, tonumber(cfg.transition_timeout_ms) or 15000)
            runtime.entry_step_index = 1
            hooks.clear_task_target_state()
            clear_treasure_combat_kite(ctx, hooks, cfg, runtime, "enter_detected")
            log_refresh_block_clear(ctx, hooks, cfg, runtime, "enter_detected", clear_mainline_refresh_block(main_state))
            if type(hooks.log_info) == "function" then
                hooks.log_info(ctx, string.format(
                    "[Treasure] enter detected by panel query | key=%s route_loaded=%s pos=%.2f, %.2f, %.2f",
                    tostring(cfg.key or ""),
                    runtime.route_loaded and "true" or "false",
                    tonumber(player_x) or 0,
                    tonumber(player_y) or 0,
                    tonumber(player_z) or 0
                ))
            end
            return true
        end
        local inside_treasure, inside_reason = likely_inside_treasure(ctx, cfg, hooks, runtime, current_time, player_x, player_y, player_z)
        if inside_treasure then
            transition_mode(
                ctx,
                hooks,
                cfg,
                runtime,
                runtime.route_loaded and "grinding" or "acquire_path",
                "entering_inside_detected:" .. tostring(inside_reason or "")
            )
            runtime.next_retry_at = current_time
            runtime.stage_deadline_at = current_time + math.max(5000, tonumber(cfg.transition_timeout_ms) or 15000)
            runtime.entry_step_index = 1
            hooks.clear_task_target_state()
            clear_treasure_combat_kite(ctx, hooks, cfg, runtime, "entering_inside_detected")
            log_refresh_block_clear(ctx, hooks, cfg, runtime, "entering_inside_detected", clear_mainline_refresh_block(main_state))
            if type(hooks.log_info) == "function" then
                hooks.log_info(ctx, string.format(
                    "[Treasure] entering recovered inside treasure | key=%s reason=%s route_loaded=%s pos=%.2f, %.2f, %.2f",
                    tostring(cfg.key or ""),
                    tostring(inside_reason or ""),
                    runtime.route_loaded and "true" or "false",
                    tonumber(player_x) or 0,
                    tonumber(player_y) or 0,
                    tonumber(player_z) or 0
                ))
            end
            return true
        end
        if current_time > (tonumber(runtime.stage_deadline_at) or 0) then
            transition_mode(ctx, hooks, cfg, runtime, "pending_entry", "enter_timeout")
            runtime.entry_step_index = 1
            runtime.next_retry_at = current_time + 1200
            if type(hooks.log_info) == "function" then
                hooks.log_info(ctx, string.format(
                    "[Treasure] entering timed out, retry entry flow | key=%s pos=%.2f, %.2f, %.2f",
                    tostring(cfg.key or ""),
                    tonumber(player_x) or 0,
                    tonumber(player_y) or 0,
                    tonumber(player_z) or 0
                ))
            end
            return true
        end
        if type(hooks.log_throttled) == "function" then
            hooks.log_throttled(ctx, "treasure_enter_wait_" .. tostring(cfg.key or ""), "info", 2500,
                string.format(
                    "[Treasure] waiting post-entry panel switch | key=%s pos=%.2f, %.2f, %.2f",
                    tostring(cfg.key or ""),
                    tonumber(player_x) or 0,
                    tonumber(player_y) or 0,
                    tonumber(player_z) or 0
                ))
        end
        return true
    end

    if mode == "acquire_path" then
        handle_acquire_path_ownership(ctx, hooks, cfg, current_time, player_x, player_y, player_z)
        if current_time < (tonumber(runtime.next_retry_at) or 0) then
            return true
        end
        runtime.path_retry_count = (tonumber(runtime.path_retry_count) or 0) + 1
        local panel_ok = select(1, try_click_task_panel_entry(ctx, hooks, cfg))
        local route, route_err = hooks.get_main_task_path(ctx)
        local normalized, route_stats = normalize_route(route, cfg)
        local min_points = math.max(1, tonumber(cfg.min_path_points) or 3)
        local reject_reason, reject_detail = reject_acquired_route(cfg, normalized)
        if panel_ok and type(normalized) == "table" and #normalized >= min_points and reject_reason ~= nil then
            runtime.path_retry_count = 0
            runtime.next_retry_at = current_time + math.max(800, tonumber(cfg.path_retry_interval_ms) or 1200)
            runtime.stage_deadline_at = current_time + math.max(5000, tonumber(cfg.transition_timeout_ms) or 15000)
            transition_mode(ctx, hooks, cfg, runtime, "entering", reject_reason)
            hooks.clear_task_target_state()
            if type(hooks.log_info) == "function" then
                hooks.log_info(ctx, string.format(
                    "[Treasure] rejected acquired route, keep waiting for real inside route | key=%s reason=%s detail=%s",
                    tostring(cfg.key or ""),
                    tostring(reject_reason or ""),
                    tostring(reject_detail or "")
                ))
            end
            return true
        end
        if panel_ok and type(normalized) == "table" and #normalized >= min_points then
            runtime.route = normalized
            runtime.route_loaded = true
            runtime.route_cursor = nil
            runtime.route_nearest_index = nil
            runtime.loot_ignore_until = 0
            runtime.loot_stuck_reference_count = 0
            runtime.loot_stuck_attempts = 0
            record.route = clone_table(normalized)
            record.route_acquired = true
            local save_ok, save_err = save_record(ctx, main_state, cfg)
            runtime.last_save_err = save_ok and nil or save_err
            transition_mode(ctx, hooks, cfg, runtime, "grinding", "route_acquired")
            runtime.next_retry_at = current_time
            hooks.clear_task_target_state()
            clear_treasure_combat_kite(ctx, hooks, cfg, runtime, "route_acquired")
            log_refresh_block_clear(ctx, hooks, cfg, runtime, "route_acquired", clear_mainline_refresh_block(main_state))
            if type(hooks.log_info) == "function" then
                hooks.log_info(ctx, string.format(
                    "[Treasure] route acquired | key=%s points=%d original_points=%d save_ok=%s next_mode=%s",
                    tostring(cfg.key or ""),
                    #normalized,
                    tonumber(type(route_stats) == "table" and route_stats.original_points or #normalized) or #normalized,
                    save_ok and "true" or "false",
                    tostring(runtime.mode or "")
                ))
            end
            return true
        end
        runtime.next_retry_at = current_time + math.max(600, tonumber(cfg.path_retry_interval_ms) or 1200)
        if runtime.path_retry_count >= math.max(1, tonumber(cfg.path_retry_count) or 5) then
            transition_mode(ctx, hooks, cfg, runtime, "failed", "route_acquire_failed")
            if type(hooks.log_info) == "function" then
                hooks.log_info(ctx, "[Treasure] route acquire failed | err=" .. tostring(route_err))
            end
            return true
        end
        return true
    end

    if mode == "grinding" then
        log_refresh_block_clear(ctx, hooks, cfg, runtime, "grinding_tick", clear_mainline_refresh_block(main_state))
        if type(hooks.log_throttled) == "function" then
            hooks.log_throttled(ctx, "treasure_grinding_skip_pickup_and_monsters_" .. tostring(cfg.key or ""), "info", 2500,
                string.format(
                    "[Treasure] normal pathing skips loot and nearby-monster holds | key=%s pos=%.2f, %.2f, %.2f",
                    tostring(cfg.key or ""),
                    tonumber(player_x) or 0,
                    tonumber(player_y) or 0,
                    tonumber(player_z) or 0
                ))
        end
        local boss_cfg = type(cfg.boss) == "table" and cfg.boss or nil
        local boss_enabled = should_enable_boss_phase(cfg)
        local boss_anchor = boss_enabled and resolve_boss_anchor(cfg, runtime) or nil
        if not boss_enabled then
            clear_treasure_combat_kite(ctx, hooks, cfg, runtime, "boss_placeholder_disabled")
            if type(hooks.log_throttled) == "function" then
                hooks.log_throttled(ctx, "treasure_boss_disabled_" .. tostring(cfg.key or ""), "info", 2500,
                    string.format(
                        "[Treasure] boss phase skipped until real boss trigger is configured | key=%s has_boss=%s enabled=%s trigger_use_route_destination=%s trigger=%.2f, %.2f, %.2f route_loaded=%s route_points=%d",
                        tostring(cfg.key or ""),
                        type(boss_cfg) == "table" and "true" or "false",
                        type(boss_cfg) == "table" and tostring(boss_cfg.enabled ~= false) or "false",
                        type(boss_cfg) == "table" and type(boss_cfg.trigger) == "table" and tostring(boss_cfg.trigger.use_route_destination == true) or "false",
                        tonumber(type(boss_cfg) == "table" and type(boss_cfg.trigger) == "table" and boss_cfg.trigger.x or 0) or 0,
                        tonumber(type(boss_cfg) == "table" and type(boss_cfg.trigger) == "table" and boss_cfg.trigger.y or 0) or 0,
                        tonumber(type(boss_cfg) == "table" and type(boss_cfg.trigger) == "table" and boss_cfg.trigger.z or 0) or 0,
                        runtime.route_loaded == true and "true" or "false",
                        type(runtime.route) == "table" and #runtime.route or 0
                    ))
            end
            return execute_treasure_route_follow(ctx, main_state, cfg, runtime, hooks, current_time, player_x, player_y, player_z)
        end
        if type(boss_cfg) == "table" and boss_anchor then
            local near_boss = within_trigger(player_x, player_y, player_z, boss_anchor)
            if near_boss then
                transition_mode(ctx, hooks, cfg, runtime, "boss_fight", "boss_anchor_reached")
                runtime.next_retry_at = current_time
                set_treasure_stage(main_state, "treasure_boss_prepare")
                if type(hooks.log_info) == "function" then
                    hooks.log_info(ctx, string.format(
                        "[Treasure] grinding reached boss anchor, hand off to boss fight | key=%s pos=%.2f, %.2f, %.2f anchor=%.2f, %.2f, %.2f",
                        tostring(cfg.key or ""),
                        tonumber(player_x) or 0,
                        tonumber(player_y) or 0,
                        tonumber(player_z) or 0,
                        tonumber(boss_anchor.x) or 0,
                        tonumber(boss_anchor.y) or 0,
                        tonumber(boss_anchor.z) or 0
                    ))
                end
                return true
            else
                local anchor_distance = distance_2d(player_x, player_y, tonumber(boss_anchor.x), tonumber(boss_anchor.y))
                local portal_probe_radius = math.max(
                    tonumber(boss_anchor.radius) or 0,
                    tonumber(boss_cfg.grinding_portal_probe_radius) or ((tonumber(boss_anchor.radius) or 900) + 900)
                )
                if anchor_distance <= portal_probe_radius then
                    local portal_ready, portal_kind = detect_boss_portal_ready(ctx, hooks, cfg, runtime, record)
                    if portal_ready then
                        runtime.portal_kind = portal_kind
                        runtime.boss_engaged = true
                        runtime.boss_clear_started_at = current_time
                        runtime.boss_portal_detected_at = current_time
                        runtime.loot_next_at = current_time
                        runtime.loot_stuck_reference_count = 0
                        runtime.loot_stuck_attempts = 0
                        runtime.boss_loot_pulse_count = 0
                        transition_mode(
                            ctx,
                            hooks,
                            cfg,
                            runtime,
                            boss_cfg.loot_enabled ~= false and "boss_loot" or "post_boss_portal",
                            "grinding_portal_enum_visible:" .. tostring(portal_kind or "")
                        )
                        runtime.next_retry_at = current_time
                        set_treasure_stage(main_state, "treasure_boss_portal_ready")
                        if type(hooks.clear_task_target_state) == "function" then
                            hooks.clear_task_target_state()
                        end
                        clear_treasure_combat_kite(ctx, hooks, cfg, runtime, "grinding_portal_enum_visible")
                        if type(hooks.log_info) == "function" then
                            hooks.log_info(ctx, string.format(
                                "[Treasure] grinding saw boss portal enum, switching mode | key=%s portal=%s next_mode=%s anchor_distance=%.2f",
                                tostring(cfg.key or ""),
                                tostring(portal_kind or ""),
                                tostring(runtime.mode or ""),
                                anchor_distance
                            ))
                        end
                        return true
                    end
                end
                clear_treasure_combat_kite(ctx, hooks, cfg, runtime, "not_in_boss_zone")
            end
        end
        return execute_treasure_route_follow(ctx, main_state, cfg, runtime, hooks, current_time, player_x, player_y, player_z)
    end

    if mode == "boss_fight" then
        local boss_cfg = type(cfg.boss) == "table" and cfg.boss or nil
        local boss_anchor = resolve_boss_anchor(cfg, runtime)
        if type(boss_cfg) ~= "table" or not boss_anchor then
            transition_mode(ctx, hooks, cfg, runtime, "grinding", "boss_fight_missing_anchor")
            if type(hooks.log_info) == "function" then
                hooks.log_info(ctx, "[Treasure] boss fight anchor unavailable, fallback to grinding | key=" .. tostring(cfg.key or ""))
            end
            return true
        end
        local anchor_distance = distance_2d(player_x, player_y, tonumber(boss_anchor.x), tonumber(boss_anchor.y))
        local route_gap = nearest_route_distance(runtime.route, player_x, player_y)
        local route_recover_distance = math.max(1800, tonumber(cfg.resume_route_distance) or 2600)
        local boss_radius = math.max(900, tonumber(boss_anchor.radius) or 900)
        local at_restart_landing = landing_ready(player_x, player_y, player_z, cfg.restart_landing)
        local on_route_before_boss = route_gap <= route_recover_distance and anchor_distance > math.max(boss_radius * 2, 2600)
        if at_restart_landing or on_route_before_boss then
            transition_mode(
                ctx,
                hooks,
                cfg,
                runtime,
                "grinding",
                at_restart_landing and "boss_fight_at_restart_landing" or "boss_fight_on_route_before_anchor"
            )
            runtime.next_retry_at = current_time
            runtime.route_cursor = nil
            runtime.route_nearest_index = nil
            clear_round_flags(runtime)
            if type(hooks.clear_task_target_state) == "function" then
                hooks.clear_task_target_state()
            end
            if type(hooks.log_info) == "function" then
                hooks.log_info(ctx, string.format(
                    "[Treasure] boss fight spatial state corrected to grinding | key=%s pos=%.2f, %.2f, %.2f anchor_distance=%.2f route_gap=%.2f restart_landing=%s",
                    tostring(cfg.key or ""),
                    tonumber(player_x) or 0,
                    tonumber(player_y) or 0,
                    tonumber(player_z) or 0,
                    anchor_distance,
                    route_gap,
                    at_restart_landing and "true" or "false"
                ))
            end
            return true
        end
        return execute_treasure_boss_fight(
            ctx,
            main_state,
            cfg,
            runtime,
            hooks,
            current_time,
            player_x,
            player_y,
            player_z,
            boss_cfg,
            boss_anchor,
            record
        )
    end

    if mode == "boss_loot" then
        set_treasure_stage(main_state, "treasure_boss_loot")
        local boss_anchor = resolve_boss_loot_anchor(cfg, runtime)
        if type(boss_anchor) == "table" then
            local loot_anchor_distance = math.max(
                180,
                (boss_anchor.explicit == true and tonumber(boss_anchor.radius) or nil)
                    or tonumber(type(cfg.boss) == "table" and cfg.boss.loot_anchor_distance)
                    or math.min(520, math.floor((tonumber(boss_anchor.radius) or 900) * 0.35))
            )
            local anchor_distance = distance_2d(
                player_x,
                player_y,
                tonumber(boss_anchor.x),
                tonumber(boss_anchor.y)
            )
            if anchor_distance > loot_anchor_distance then
                runtime.loot_stuck_reference_count = 0
                runtime.loot_stuck_attempts = 0
                if type(hooks.clear_task_target_state) == "function" then
                    hooks.clear_task_target_state()
                end
                local move_ok, move_err = hooks.issue_move(ctx, current_time, {
                    x = tonumber(boss_anchor.x),
                    y = tonumber(boss_anchor.y),
                    z = tonumber(boss_anchor.z),
                    source = boss_anchor.explicit == true and "treasure_boss_loot_config_anchor" or "treasure_boss_loot_anchor",
                    path_index = 0,
                    path_points = 0,
                    move_interval_ms = 260
                })
                if type(hooks.log_throttled) == "function" then
                    hooks.log_throttled(ctx, "treasure_boss_loot_anchor_" .. tostring(cfg.key or ""), "info", 900,
                        string.format(
                            "[Treasure] boss loot approaching anchor before pickup | key=%s anchor_kind=%s pos=%.2f, %.2f, %.2f anchor=%.2f, %.2f, %.2f anchor_distance=%.2f pickup_distance=%.2f move_ok=%s err=%s",
                            tostring(cfg.key or ""),
                            boss_anchor.explicit == true and "config" or "boss",
                            tonumber(player_x) or 0,
                            tonumber(player_y) or 0,
                            tonumber(player_z) or 0,
                            tonumber(boss_anchor.x) or 0,
                            tonumber(boss_anchor.y) or 0,
                            tonumber(boss_anchor.z) or 0,
                            anchor_distance,
                            loot_anchor_distance,
                            move_ok and "true" or "false",
                            tostring(move_err or "")
                        ))
                end
                return true
            end
        end
        if type(hooks.log_throttled) == "function" then
            hooks.log_throttled(ctx, "treasure_boss_loot_owned_" .. tostring(cfg.key or ""), "info", 1200,
                string.format(
                    "[Treasure] module executor owns boss loot tick | key=%s pos=%.2f, %.2f, %.2f next_press_in=%dms",
                    tostring(cfg.key or ""),
                    tonumber(player_x) or 0,
                    tonumber(player_y) or 0,
                    tonumber(player_z) or 0,
                    math.max(0, (tonumber(runtime.loot_next_at) or 0) - current_time)
                ))
        end
        if maybe_handle_treasure_boss_loot(ctx, main_state, cfg, runtime, hooks, current_time) then
            return true
        end
        if type(hooks.mark_auto_equip_after_loot) == "function" then
            hooks.mark_auto_equip_after_loot(ctx, current_time, "treasure:" .. tostring(cfg.key or ""))
        end
        transition_mode(ctx, hooks, cfg, runtime, "post_boss_portal", "boss_loot_finished")
        runtime.next_retry_at = current_time
        hooks.clear_task_target_state()
        if type(hooks.log_info) == "function" then
            hooks.log_info(ctx, "[Treasure] boss loot phase finished, switching to portal phase | key=" .. tostring(cfg.key or ""))
        end
        return true
    end

    if mode == "post_boss_portal" then
        set_treasure_stage(main_state, "treasure_post_boss_portal")
        if current_time < (tonumber(runtime.next_retry_at) or 0) then
            return true
        end
        if target_level ~= nil then
            local portal_level = refresh_player_level(ctx, cfg, runtime, hooks, current_time, true)
            if type(portal_level) == "number" and portal_level >= target_level and runtime.pending_return_mainline ~= true then
                runtime.pending_return_mainline = true
                if type(hooks.log_info) == "function" then
                    hooks.log_info(ctx, string.format(
                        "[Treasure] portal phase level gate reached, switch to exit portal | key=%s level=%d target_level=%d",
                        tostring(cfg.key or ""),
                        portal_level,
                        target_level
                    ))
                end
            end
        end
        local use_restart = should_use_restart_portal(record, cfg, runtime)
        local portal_cfg = use_restart and cfg.portals and cfg.portals.restart or cfg.portals and cfg.portals.exit
        local portal_kind = use_restart and "restart" or "exit"
        runtime.portal_kind = portal_kind
        if type(hooks.log_throttled) == "function" then
            hooks.log_throttled(ctx, "treasure_post_portal_owned_" .. tostring(cfg.key or ""), "info", 1200,
                string.format(
                    "[Treasure] module executor owns portal phase | key=%s portal=%s runs=%d pos=%.2f, %.2f, %.2f",
                    tostring(cfg.key or ""),
                    tostring(portal_kind),
                    tonumber(record.run_count) or 0,
                    tonumber(player_x) or 0,
                    tonumber(player_y) or 0,
                    tonumber(player_z) or 0
                ))
        end

        if move_to_portal(ctx, hooks, current_time, player_x, player_y, portal_cfg) then
            runtime.next_retry_at = current_time + 400
            return true
        end

        local clicked, portal_err = try_click_portal(ctx, hooks, portal_cfg, runtime, record)
        runtime.next_retry_at = current_time + math.max(1200, tonumber(portal_cfg and portal_cfg.retry_ms) or 1500)
        if clicked then
            local after_click_time = now_ms(ctx)
            if after_click_time <= 0 then
                after_click_time = current_time
            end
            record.run_count = (tonumber(record.run_count) or 0) + 1
            -- Exit portal clicks can settle asynchronously; only mark completed after exit_landing is confirmed.
            local save_ok, save_err = save_record(ctx, main_state, cfg)
            runtime.last_save_err = save_ok and nil or save_err
            runtime.stage_deadline_at = after_click_time + math.max(4000, tonumber(portal_cfg and portal_cfg.settle_ms) or 5000)
            transition_mode(ctx, hooks, cfg, runtime, portal_kind == "restart" and "wait_restart" or "wait_exit",
                "portal_clicked:" .. tostring(portal_kind))
            settle_treasure_transition(
                ctx,
                main_state,
                hooks,
                cfg,
                runtime,
                after_click_time,
                tonumber(portal_cfg and portal_cfg.settle_ms) or 5000,
                "portal_" .. tostring(portal_kind)
            )
            if type(hooks.log_info) == "function" then
                hooks.log_info(ctx, string.format(
                    "[Treasure] portal clicked | key=%s portal=%s runs=%d save_ok=%s",
                    tostring(cfg.key or ""),
                    tostring(portal_kind),
                    tonumber(record.run_count) or 0,
                    save_ok and "true" or "false"
                ))
            end
            return true
        end
        if type(hooks.log_throttled) == "function" then
            hooks.log_throttled(ctx, "treasure_portal_wait_" .. tostring(cfg.key or ""), "info", 2500,
                string.format(
                    "[Treasure] waiting portal click | key=%s portal=%s pos=%.2f, %.2f, %.2f retry_in=%dms err=%s",
                    tostring(cfg.key or ""),
                    tostring(portal_kind or ""),
                    tonumber(player_x) or 0,
                    tonumber(player_y) or 0,
                    tonumber(player_z) or 0,
                    math.max(0, (tonumber(runtime.next_retry_at) or 0) - current_time),
                    tostring(portal_err or "")
                ))
        end
        return true
    end

    if mode == "wait_restart" then
        set_treasure_stage(main_state, "treasure_wait_restart")
        if type(hooks.hold_navigation) == "function" then
            hooks.hold_navigation(ctx, current_time, "treasure_wait_restart")
        end
        if type(hooks.clear_task_target_state) == "function" then
            hooks.clear_task_target_state()
        end
        local landing = cfg.restart_landing
        local require_verified_landing = type(cfg) == "table" and cfg.restart_landing_require_verified == true
        local has_landing = is_valid_point(landing)
            and ((tonumber(landing.x) ~= 0 or tonumber(landing.y) ~= 0) or landing.allow_zero == true)
        local deadline_expired = has_landing and current_time >= (tonumber(runtime.stage_deadline_at) or 0)
        local inside_after_restart = false
        local inside_after_restart_reason = ""
        if deadline_expired and not require_verified_landing then
            local boss_cfg = type(cfg.boss) == "table" and cfg.boss or nil
            local boss_anchor = resolve_boss_anchor(cfg, runtime)
            if type(boss_cfg) == "table" and boss_anchor and within_trigger(player_x, player_y, player_z, boss_anchor) then
                inside_after_restart = true
                inside_after_restart_reason = "boss_anchor"
            else
                local route_gap = nearest_route_distance(runtime.route, player_x, player_y)
                local route_recover_distance = math.max(1800, tonumber(cfg.resume_route_distance) or 2600)
                if route_gap <= route_recover_distance then
                    inside_after_restart = true
                    inside_after_restart_reason = "route"
                end
            end
        end
        local ready = cfg_landing_ready(player_x, player_y, player_z, cfg, "restart_landing")
            or (not require_verified_landing and not has_landing and current_time >= (tonumber(runtime.stage_deadline_at) or 0))
            or (not require_verified_landing and inside_after_restart)
        local settle_until = tonumber(runtime.next_retry_at) or 0
        local target_level_reached = target_level ~= nil
            and type(current_level) == "number"
            and current_level >= target_level
        local restart_exit_share_landing = landing_ready(player_x, player_y, player_z, cfg.restart_landing)
            and cfg_landing_ready(player_x, player_y, player_z, cfg, "exit_landing")
        local accidental_exit_landing = runtime.pending_return_mainline ~= true
            and target_level ~= nil
            and target_level_reached ~= true
            and not restart_exit_share_landing
            and cfg_landing_ready(player_x, player_y, player_z, cfg, "exit_landing")
        if accidental_exit_landing then
            transition_mode(ctx, hooks, cfg, runtime, "pending_entry", "restart_landed_exit_before_target_level")
            runtime.next_retry_at = current_time
            runtime.stage_deadline_at = 0
            runtime.entry_step_index = 1
            runtime.portal_kind = nil
            runtime.route_cursor = nil
            runtime.route_nearest_index = nil
            runtime.loot_ignore_until = 0
            runtime.loot_stuck_reference_count = 0
            runtime.loot_stuck_attempts = 0
            hooks.clear_task_target_state()
            clear_round_flags(runtime)
            log_refresh_block_clear(ctx, hooks, cfg, runtime, "restart_landed_exit_before_target_level", clear_mainline_refresh_block(main_state))
            if type(hooks.log_info) == "function" then
                hooks.log_info(ctx, string.format(
                    "[Treasure] restart portal landed outside before target level, retry entry | key=%s level=%s target_level=%s pos=%.2f, %.2f, %.2f",
                    tostring(cfg.key or ""),
                    tostring(current_level or ""),
                    tostring(target_level or ""),
                    tonumber(player_x) or 0,
                    tonumber(player_y) or 0,
                    tonumber(player_z) or 0
                ))
            end
            return true
        end
        if not ready and current_time < settle_until then
            if type(hooks.log_throttled) == "function" then
                hooks.log_throttled(ctx, "treasure_wait_restart_settle_" .. tostring(cfg.key or ""), "info", 1200,
                    string.format(
                        "[Treasure] waiting restart settle before landing retry | key=%s pos=%.2f, %.2f, %.2f settle_in=%dms deadline_in=%dms",
                        tostring(cfg.key or ""),
                        tonumber(player_x) or 0,
                        tonumber(player_y) or 0,
                        tonumber(player_z) or 0,
                        math.max(0, settle_until - current_time),
                        math.max(0, (tonumber(runtime.stage_deadline_at) or 0) - current_time)
                    ))
            end
            return true
        end
        if ready then
            transition_mode(ctx, hooks, cfg, runtime, "grinding",
                inside_after_restart and ("restart_inside_ready:" .. inside_after_restart_reason) or "restart_landing_ready")
            runtime.next_retry_at = current_time
            runtime.route_cursor = nil
            runtime.route_nearest_index = nil
            runtime.loot_ignore_until = 0
            runtime.loot_stuck_reference_count = 0
            runtime.loot_stuck_attempts = 0
            hooks.clear_task_target_state()
            clear_round_flags(runtime)
            log_refresh_block_clear(ctx, hooks, cfg, runtime, "wait_restart_ready", clear_mainline_refresh_block(main_state))
            if type(hooks.log_info) == "function" then
                hooks.log_info(ctx, string.format(
                    "[Treasure] restart landing ready | key=%s pos=%.2f, %.2f, %.2f reason=%s",
                    tostring(cfg.key or ""),
                    tonumber(player_x) or 0,
                    tonumber(player_y) or 0,
                    tonumber(player_z) or 0,
                    inside_after_restart_reason ~= "" and inside_after_restart_reason or "landing"
                ))
            end
        elseif deadline_expired then
            local expired_deadline = tonumber(runtime.stage_deadline_at) or 0
            runtime.pending_return_mainline = false
            runtime.portal_kind = "restart"
            runtime.next_retry_at = current_time
            runtime.stage_deadline_at = 0
            transition_mode(ctx, hooks, cfg, runtime, "post_boss_portal", "restart_landing_timeout_retry")
            if type(hooks.clear_task_target_state) == "function" then
                hooks.clear_task_target_state()
            end
            log_refresh_block_clear(ctx, hooks, cfg, runtime, "restart_landing_timeout_retry", clear_mainline_refresh_block(main_state))
            if type(hooks.log_info) == "function" then
                hooks.log_info(ctx, string.format(
                    "[Treasure] restart landing timeout, retry restart portal | key=%s pos=%.2f, %.2f, %.2f deadline=%d",
                    tostring(cfg.key or ""),
                    tonumber(player_x) or 0,
                    tonumber(player_y) or 0,
                    tonumber(player_z) or 0,
                    expired_deadline
                ))
            end
        elseif type(hooks.log_throttled) == "function" then
            hooks.log_throttled(ctx, "treasure_wait_restart_pending_" .. tostring(cfg.key or ""), "info", 1200,
                string.format(
                    "[Treasure] waiting restart landing | key=%s pos=%.2f, %.2f, %.2f deadline_in=%dms",
                    tostring(cfg.key or ""),
                    tonumber(player_x) or 0,
                    tonumber(player_y) or 0,
                    tonumber(player_z) or 0,
                    math.max(0, (tonumber(runtime.stage_deadline_at) or 0) - current_time)
                ))
        end
        return true
    end

    if mode == "wait_exit" then
        set_treasure_stage(main_state, "treasure_wait_exit")
        local has_landing = cfg_landing_has_point(cfg, "exit_landing")
        local ready = cfg_landing_ready(player_x, player_y, player_z, cfg, "exit_landing")
            or (not has_landing and current_time >= (tonumber(runtime.stage_deadline_at) or 0))
        if ready then
            if runtime.pending_return_mainline == true and record.completed ~= true then
                record.completed = true
                local save_ok, save_err = save_record(ctx, main_state, cfg)
                runtime.last_save_err = save_ok and nil or save_err
                if type(hooks.log_info) == "function" then
                    hooks.log_info(ctx, string.format(
                        "[Treasure] exit landing confirmed, mark completed | key=%s save_ok=%s err=%s",
                        tostring(cfg.key or ""),
                        save_ok and "true" or "false",
                        tostring(save_err or "")
                    ))
                end
            end
            if type(main_state) == "table" then
                main_state.require_task_button_refresh = false
                main_state.task_update_wait_until = 0
                main_state.task_path_wait_until = 0
                main_state.task_path_refresh_requested = false
                main_state.next_task_button_click_at = current_time
                main_state.next_task_refresh_at = current_time
            end
            transition_mode(ctx, hooks, cfg, runtime, "return_mainline", "exit_landing_ready")
            runtime.next_retry_at = current_time
            hooks.clear_task_target_state()
            log_refresh_block_clear(ctx, hooks, cfg, runtime, "wait_exit_ready", clear_mainline_refresh_block(main_state))
            if type(hooks.log_info) == "function" then
                hooks.log_info(ctx, string.format(
                    "[Treasure] exit landing ready, resume mainline immediately | key=%s pos=%.2f, %.2f, %.2f",
                    tostring(cfg.key or ""),
                    tonumber(player_x) or 0,
                    tonumber(player_y) or 0,
                    tonumber(player_z) or 0
                ))
            end
        elseif has_landing and current_time >= (tonumber(runtime.stage_deadline_at) or 0) then
            local expired_deadline = tonumber(runtime.stage_deadline_at) or 0
            runtime.pending_return_mainline = true
            runtime.portal_kind = "exit"
            runtime.next_retry_at = current_time
            runtime.stage_deadline_at = 0
            transition_mode(ctx, hooks, cfg, runtime, "post_boss_portal", "exit_landing_timeout_retry")
            if type(hooks.clear_task_target_state) == "function" then
                hooks.clear_task_target_state()
            end
            log_refresh_block_clear(ctx, hooks, cfg, runtime, "exit_landing_timeout_retry", clear_mainline_refresh_block(main_state))
            if type(hooks.log_info) == "function" then
                hooks.log_info(ctx, string.format(
                    "[Treasure] exit landing timeout, retry exit portal | key=%s pos=%.2f, %.2f, %.2f deadline=%d",
                    tostring(cfg.key or ""),
                    tonumber(player_x) or 0,
                    tonumber(player_y) or 0,
                    tonumber(player_z) or 0,
                    expired_deadline
                ))
            end
        elseif type(hooks.log_throttled) == "function" then
            hooks.log_throttled(ctx, "treasure_wait_exit_pending_" .. tostring(cfg.key or ""), "info", 1200,
                string.format(
                    "[Treasure] waiting exit landing | key=%s pos=%.2f, %.2f, %.2f deadline_in=%dms",
                    tostring(cfg.key or ""),
                    tonumber(player_x) or 0,
                    tonumber(player_y) or 0,
                    tonumber(player_z) or 0,
                    math.max(0, (tonumber(runtime.stage_deadline_at) or 0) - current_time)
                ))
        end
        return true
    end

    if mode == "return_mainline" then
        set_treasure_stage(main_state, "treasure_return_mainline")
        runtime.pending_return_mainline = false
        transition_mode(ctx, hooks, cfg, runtime, record.completed == true and "completed" or "inactive", "return_mainline_done")
        runtime.active_key = record.completed == true and runtime.active_key or nil
        runtime.route = record.completed == true and runtime.route or nil
        runtime.route_loaded = record.completed == true and runtime.route_loaded or false
        clear_round_flags(runtime)
        hooks.clear_task_target_state()
        if type(main_state) == "table" then
            main_state.require_task_button_refresh = false
            main_state.task_update_wait_until = 0
            main_state.task_path_wait_until = 0
            main_state.task_path_refresh_requested = false
            main_state.next_task_button_click_at = current_time
            main_state.next_task_refresh_at = current_time
        end
        log_refresh_block_clear(ctx, hooks, cfg, runtime, "return_mainline", clear_mainline_refresh_block(main_state))
        hooks.try_click_main_task_button(ctx, current_time)
        if type(hooks.log_info) == "function" then
            hooks.log_info(ctx, string.format(
                "[Treasure] return mainline | key=%s completed=%s runs=%d action=click_main_task_button",
                tostring(cfg.key or ""),
                record.completed == true and "true" or "false",
                tonumber(record.run_count) or 0
            ))
        end
        if record.completed ~= true then
            runtime.active_key = nil
            runtime.task_match_confirmed = false
        end
        return true
    end

    if mode == "failed" then
        runtime.active_key = nil
        runtime.task_match_confirmed = false
        runtime.route = nil
        runtime.route_loaded = false
        clear_round_flags(runtime)
        return false
    end

    return mode ~= "inactive" and mode ~= "completed"
end

return M
