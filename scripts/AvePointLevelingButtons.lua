local M = {}

M.VERSION = 1

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

local function trim(value)
    if value == nil then
        return ""
    end
    local text = tostring(value)
    text = text:gsub("^%s+", "")
    text = text:gsub("%s+$", "")
    return text
end

local function lower_identity(value)
    return trim(value):lower()
end

local function merge_into(dst, src)
    if type(src) ~= "table" then
        return dst
    end
    for k, v in pairs(src) do
        dst[k] = clone_table(v)
    end
    return dst
end

M.SLOTS = {
    main_task = {
        label = "main task TaskBtn",
        session_slot = "main_task",
        role = "task_panel",
        button_identity_patterns = {
            "taskitem_c.widgettree.taskbtn"
        },
        require_task_row_guard = true,
        default_physical_fallback = false,
        hover_capture = {
            client_left = 79.0,
            client_top = 255.0,
            client_right = 228.0,
            client_bottom = 288.0,
            retry_ms = 1200
        }
    },
    treasure_task = {
        label = "treasure task TaskBtn",
        session_slot = "treasure_task",
        role = "task_panel",
        button_identity_patterns = {
            "taskitem_c.widgettree.taskbtn"
        },
        require_task_row_guard = true,
        require_query_guard = true,
        default_physical_fallback = false,
        hover_capture = {
            client_left = 76.0,
            client_top = 288.0,
            client_right = 209.0,
            client_bottom = 321.0,
            retry_ms = 900
        }
    },
    dialogue_jump = {
        label = "dialogue JumpBtn",
        session_slot = "dialogue_jump",
        role = "modal_singleton",
        button_identity_patterns = {
            "dialoguetalk_c.widgettree.jumpbtn"
        },
        require_dialogue_window_guard = true,
        max_success_per_dialogue_session = 1,
        default_physical_fallback = false,
        hover_capture = {
            client_left = 1320.0,
            client_top = 36.0,
            client_right = 1363.0,
            client_bottom = 49.0,
            retry_ms = 900
        }
    },
    interaction_prompt = {
        label = "interaction FunctionBtn",
        session_slot = "interaction_prompt",
        role = "prompt_singleton",
        button_identity_patterns = {
            "fightinteractiveview_c.widgettree.functionbtn"
        },
        require_prompt_guard = true,
        default_physical_fallback = false,
        hover_capture = {
            client_left = 709.0,
            client_top = 685.0,
            client_right = 745.0,
            client_bottom = 722.0,
            retry_ms = 900
        }
    },
    task_entry_send = {
        label = "world map SendBtn",
        session_slot = "task_entry_send",
        role = "map_singleton",
        button_identity_patterns = {
            "worldmapdetail_c.widgettree.worldmapdetailitem.widgettree.sendbtn"
        },
        require_live_guard = true,
        default_physical_fallback = false,
        hover_capture = {
            client_left = 654.0,
            client_top = 789.0,
            client_right = 790.0,
            client_bottom = 810.0,
            retry_ms = 900
        }
    },
    treasure_restart_portal = {
        label = "treasure restart MapTrapBtn",
        session_slot = "treasure_restart_portal",
        role = "fight_interactive_portal",
        generic_session_cache = true,
        button_identity_patterns = {
            "fightinteractiveview_c.widgettree.maptrapbtn"
        },
        default_physical_fallback = false,
        hover_capture = {
            client_left = 690.0,
            client_top = 685.0,
            client_right = 745.0,
            client_bottom = 730.0,
            retry_ms = 700
        }
    },
    treasure_exit_portal = {
        label = "treasure exit PortalBtn",
        session_slot = "treasure_exit_portal",
        role = "fight_interactive_portal",
        generic_session_cache = true,
        button_identity_patterns = {
            "fightinteractiveview_c.widgettree.portalbtn"
        },
        default_physical_fallback = false,
        hover_capture = {
            client_left = 690.0,
            client_top = 685.0,
            client_right = 745.0,
            client_bottom = 730.0,
            retry_ms = 700
        }
    }
}

function M.slot_definition(slot)
    local name = trim(slot)
    local def = M.SLOTS[name]
    if type(def) ~= "table" then
        return nil, "unknown button slot: " .. name
    end
    local out = clone_table(def)
    out.slot = name
    return out
end

function M.slot_session_key(slot)
    local def = M.SLOTS[trim(slot)]
    return type(def) == "table" and tostring(def.session_slot or slot) or trim(slot)
end

function M.target_identity_text(target)
    if type(target) ~= "table" then
        return ""
    end
    return table.concat({
        tostring(target.fullname or ""),
        tostring(target.name or ""),
        tostring(target.identity or ""),
        tostring(target.classname or "")
    }, " ")
end

function M.target_matches_slot(slot, target)
    local def = M.SLOTS[trim(slot)]
    if type(def) ~= "table" then
        return false, "unknown_slot"
    end
    if type(target) ~= "table" then
        return false, "target_unavailable"
    end
    if tonumber(target.addr) == nil then
        return false, "target_addr_unavailable"
    end
    local identity = lower_identity(M.target_identity_text(target))
    local patterns = type(def.button_identity_patterns) == "table" and def.button_identity_patterns or {}
    if #patterns <= 0 then
        return true, "no_identity_patterns"
    end
    for _, pattern in ipairs(patterns) do
        local needle = lower_identity(pattern)
        if needle ~= "" and identity:find(needle, 1, true) then
            return true, "identity_match"
        end
    end
    return false, "identity_mismatch:" .. identity
end

function M.normalize_cached_target(slot, target, opts)
    opts = type(opts) == "table" and opts or {}
    local ok, reason = M.target_matches_slot(slot, target)
    if not ok then
        return nil, reason
    end
    local cached = {
        slot = trim(slot),
        addr = tonumber(target.addr),
        x = tonumber(target.x),
        y = tonumber(target.y),
        text = tostring(target.text or ""),
        name = tostring(target.name or ""),
        fullname = tostring(target.fullname or target.identity or ""),
        related_text = tostring(target.related_text or target.nearest_text or ""),
        related_distance = tonumber(target.related_distance or target.nearest_distance),
        source = tostring(opts.source or target.source or ""),
        query = opts.query ~= nil and tostring(opts.query) or target.query,
        captured_at = tonumber(opts.captured_at) or 0,
        session_key = tostring(opts.session_key or target.session_key or "")
    }
    return cached, nil
end

function M.cache_matches_process(cached, process_key)
    if type(cached) ~= "table" then
        return false, "cached_unavailable"
    end
    local current = trim(process_key)
    if current == "" then
        return true, "process_key_unavailable"
    end
    local session_key = trim(cached.session_key)
    if session_key == "" then
        return true, "cached_session_key_unavailable"
    end
    if session_key == current then
        return true, "session_match"
    end
    local cached_pid = session_key:match("^pid=([^|]+)")
    local current_pid = current:match("^pid=([^|]+)")
    if cached_pid ~= nil and current_pid ~= nil and cached_pid == current_pid then
        return true, "pid_match"
    end
    return false, "process_changed"
end

function M.hover_capture_step(slot, overrides)
    local def = M.SLOTS[trim(slot)]
    if type(def) ~= "table" or type(def.hover_capture) ~= "table" then
        return nil, "hover_capture_unavailable"
    end
    local hover = clone_table(def.hover_capture)
    local step = {
        hover_capture_client_left = hover.client_left,
        hover_capture_client_top = hover.client_top,
        hover_capture_client_right = hover.client_right,
        hover_capture_client_bottom = hover.client_bottom,
        hover_capture_retry_ms = hover.retry_ms
    }
    merge_into(step, overrides)
    return step, nil
end

function M.describe_slot(slot)
    local def = M.SLOTS[trim(slot)]
    if type(def) ~= "table" then
        return "slot=" .. trim(slot) .. " unknown"
    end
    local hover = type(def.hover_capture) == "table" and def.hover_capture or {}
    return string.format(
        "slot=%s label=%s role=%s session=%s hover=(%s,%s)-(%s,%s)",
        trim(slot),
        tostring(def.label or ""),
        tostring(def.role or ""),
        tostring(def.session_slot or slot),
        tostring(hover.client_left or ""),
        tostring(hover.client_top or ""),
        tostring(hover.client_right or ""),
        tostring(hover.client_bottom or "")
    )
end

return M
