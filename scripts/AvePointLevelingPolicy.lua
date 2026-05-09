local M = {}

local function trim(value)
    if value == nil then
        return ""
    end
    local text = tostring(value)
    text = text:gsub("^%s+", "")
    text = text:gsub("%s+$", "")
    return text
end

function M.normalize_map_name(value)
    local text = trim(value)
    if text == "" then
        return nil
    end
    return text
end

function M.current_map_task_config(current_map_name, map_task_configs)
    local map_name = M.normalize_map_name(current_map_name)
    if map_name == nil then
        return nil
    end
    return type(map_task_configs) == "table" and map_task_configs[map_name] or nil, map_name
end

local function lookup_task_config(query_text, task_name_configs)
    local task_name = M.normalize_map_name(query_text)
    if task_name == nil then
        return nil, nil
    end

    local task_cfg = task_name_configs[task_name]
    if task_cfg ~= nil then
        return task_cfg, task_name
    end

    local stripped_task_name = trim(task_name:gsub("^主线%s*", ""))
    if stripped_task_name ~= "" and stripped_task_name ~= task_name then
        task_cfg = task_name_configs[stripped_task_name]
        if task_cfg ~= nil then
            return task_cfg, stripped_task_name
        end
    end

    return nil, task_name
end

function M.current_task_config(current_task_name, current_task_detail, task_name_configs)
    if type(current_task_detail) == "table" and task_name_configs == nil then
        task_name_configs = current_task_detail
        current_task_detail = nil
    end

    if type(task_name_configs) ~= "table" then
        return nil, M.normalize_map_name(current_task_name) or M.normalize_map_name(current_task_detail)
    end

    local task_cfg, matched_name = lookup_task_config(current_task_name, task_name_configs)
    if task_cfg ~= nil then
        return task_cfg, matched_name, "task_name"
    end

    local detail_cfg, matched_detail = lookup_task_config(current_task_detail, task_name_configs)
    if detail_cfg ~= nil then
        return detail_cfg, matched_detail, "task_detail"
    end

    return nil, M.normalize_map_name(current_task_name) or M.normalize_map_name(current_task_detail)
end

function M.objective_ready_distance(default_distance, objective_cfg)
    local base_distance = tonumber(default_distance) or 0
    if type(objective_cfg) == "table" and tostring(objective_cfg.mode or "") == "boss_kite" then
        return math.max(base_distance, tonumber(objective_cfg.trigger_distance) or base_distance)
    end
    return base_distance
end

function M.should_handle_low_priority_ui(opts)
    opts = type(opts) == "table" and opts or {}

    if opts.require_task_button_refresh == true
        or opts.dialogue_escape_due == true
        or opts.in_task_update_wait == true
    then
        return false
    end

    if opts.has_target ~= true then
        return true
    end

    if opts.is_stalled == true then
        return true
    end

    if tostring(opts.phase or "") == "task_reached" then
        return true
    end

    local goal_distance = tonumber(opts.goal_distance)
    local ready_distance = tonumber(opts.objective_ready_distance)
    if goal_distance ~= nil and ready_distance ~= nil and goal_distance <= ready_distance then
        return true
    end

    return false
end

function M.resolve_task_intent(opts)
    opts = type(opts) == "table" and opts or {}

    local ordered = {
        { bucket = "task_recipe", candidate = opts.task_recipe },
        { bucket = "entry_action", candidate = opts.entry_action },
        { bucket = "route_point_active", candidate = opts.route_point_active },
        { bucket = "route_point_candidate", candidate = opts.route_point_candidate },
        { bucket = "post_dialogue_flow", candidate = opts.post_dialogue_flow },
        { bucket = "dialogue_jump", candidate = opts.dialogue_jump },
        { bucket = "generic_follow_task", candidate = opts.generic_follow_task },
        { bucket = "generic_task_reached", candidate = opts.generic_task_reached },
        { bucket = "click_main_task_button", candidate = opts.click_main_task_button }
    }

    for _, item in ipairs(ordered) do
        local candidate = type(item) == "table" and item.candidate or nil
        if type(candidate) == "table" then
            return {
                bucket = tostring(item.bucket or ""),
                kind = tostring(candidate.kind or item.bucket or ""),
                matched_source = tostring(candidate.matched_source or item.bucket or ""),
                matched_key = tostring(candidate.matched_key or ""),
                config = type(candidate.config) == "table" and candidate.config or nil,
                reason = tostring(candidate.reason or "")
            }
        end
    end

    return {
        bucket = "none",
        kind = "none",
        matched_source = "",
        matched_key = "",
        config = nil,
        reason = tostring(opts.fallback_reason or "no_matching_intent")
    }
end

return M
