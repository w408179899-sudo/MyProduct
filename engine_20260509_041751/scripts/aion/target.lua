local M = {}

local GAME_PROCESS_NAME = "aion.bin"
M.game_process_name = GAME_PROCESS_NAME

local function safe_call(fn, ...)
    if type(fn) ~= "function" then
        return false, nil
    end

    local ok, value = pcall(fn, ...)
    if not ok then
        return false, nil, tostring(value)
    end
    return true, value, nil
end

local function lower(value)
    return string.lower(tostring(value or ""))
end

local function basename(value)
    local text = lower(value):gsub("\\", "/")
    return text:match("([^/]+)$") or text
end

local function is_game_process_name(name)
    return basename(name) == GAME_PROCESS_NAME
end

local function is_related_aion_name(name)
    local text = basename(name)
    return text == "aion.exe" or text:find("aion", 1, true) ~= nil
end

local function window_for_pid(pid)
    local hwnd = nil

    if proc and type(proc.window) == "function" then
        local ok, value = safe_call(proc.window, pid)
        if ok and value and value ~= 0 then
            hwnd = value
        end
    end

    if (not hwnd or hwnd == 0) and wnd and type(wnd.find_by_pid) == "function" then
        local ok, value = safe_call(wnd.find_by_pid, pid)
        if ok and value and value ~= 0 then
            hwnd = value
        end
    end

    return hwnd
end

local function window_title(hwnd)
    if not hwnd or hwnd == 0 or not wnd or type(wnd.get_title) ~= "function" then
        return ""
    end

    local ok, value = safe_call(wnd.get_title, hwnd)
    if ok and value then
        return tostring(value)
    end
    return ""
end

local function window_class(hwnd)
    if not hwnd or hwnd == 0 or not wnd or type(wnd.class_name) ~= "function" then
        return ""
    end

    local ok, value = safe_call(wnd.class_name, hwnd)
    if ok and value then
        return tostring(value)
    end
    return ""
end

local function window_bool(hwnd, fn)
    if not hwnd or hwnd == 0 or type(fn) ~= "function" then
        return nil
    end

    local ok, value = safe_call(fn, hwnd)
    if ok then
        return value == true
    end
    return nil
end

local function process_path(pid)
    if not proc or type(proc.path) ~= "function" then
        return ""
    end

    local ok, value = safe_call(proc.path, pid)
    if ok and value then
        return tostring(value)
    end
    return ""
end

local function process_alive(pid)
    if not proc or type(proc.is_alive) ~= "function" then
        return true
    end

    local ok, value = safe_call(proc.is_alive, pid)
    if ok then
        return value == true
    end
    return false
end

function M.list_candidates(options)
    options = options or {}
    if not proc or type(proc.list) ~= "function" then
        return false, {}, "proc.list unavailable"
    end

    local ok, list, err = safe_call(proc.list)
    if not ok or type(list) ~= "table" then
        return false, {}, err or "proc.list failed"
    end

    local out = {}
    local include_all_aion = options.include_all_aion ~= false
    local include_related_aion = options.include_related_aion == true
    local selected_pid = tonumber(options.selected_pid) or 0

    for _, p in ipairs(list) do
        local pid = tonumber(p.pid) or 0
        local name = tostring(p.name or "")
        local is_candidate = is_game_process_name(name)

        if not is_candidate and include_related_aion then
            is_candidate = is_related_aion_name(name)
        end

        if not is_candidate and selected_pid > 0 and pid == selected_pid then
            is_candidate = true
        end

        if include_all_aion and is_candidate and pid > 0 then
            local hwnd = window_for_pid(pid)
            local title = window_title(hwnd)
            table.insert(out, {
                pid = pid,
                process_name = name,
                hwnd = hwnd or 0,
                title = title,
                class_name = window_class(hwnd),
                path = process_path(pid),
                visible = window_bool(hwnd, wnd and wnd.is_visible),
                minimized = window_bool(hwnd, wnd and wnd.is_minimized),
                alive = process_alive(pid),
            })
        end
    end

    table.sort(out, function(a, b)
        return (a.pid or 0) < (b.pid or 0)
    end)

    return true, out, nil
end

function M.first_game_candidate()
    local ok, candidates, err = M.list_candidates({
        include_related_aion = false,
    })
    if not ok then
        return false, nil, err or "target scan failed"
    end
    if #candidates == 0 then
        return false, nil, "no Aion.bin process found"
    end
    return true, candidates[1], nil
end

function M.single_game_candidate()
    local ok, candidates, err = M.list_candidates({
        include_related_aion = false,
    })
    if not ok then
        return false, nil, err or "target scan failed"
    end
    if #candidates == 1 then
        return true, candidates[1], nil
    end
    if #candidates == 0 then
        return false, nil, "no Aion.bin process found"
    end
    return false, nil, "multiple Aion.bin processes found"
end

function M.find_index(candidates, pid)
    pid = tonumber(pid) or 0
    if pid <= 0 then
        return 1
    end

    for index, item in ipairs(candidates or {}) do
        if tonumber(item.pid) == pid then
            return index
        end
    end
    return 1
end

function M.label(candidate, character_name)
    if not candidate then
        return "No Aion window"
    end

    local role = tostring(character_name or "")
    if role ~= "" then
        return string.format("%s | PID %s | HWND %s", role, tostring(candidate.pid), tostring(candidate.hwnd or 0))
    end

    local title = tostring(candidate.title or "")
    if title ~= "" then
        return string.format("%s | PID %s | %s", tostring(candidate.process_name or "Aion"), tostring(candidate.pid), title)
    end

    return string.format("%s | PID %s | HWND %s",
        tostring(candidate.process_name or "Aion"),
        tostring(candidate.pid),
        tostring(candidate.hwnd or 0))
end

function M.apply_candidate(cfg_target, candidate)
    if type(cfg_target) ~= "table" or type(candidate) ~= "table" then
        return false
    end

    cfg_target.pid = tonumber(candidate.pid) or 0
    cfg_target.hwnd = tonumber(candidate.hwnd) or 0
    cfg_target.title = tostring(candidate.title or "")
    cfg_target.process_name = tostring(candidate.process_name or "")
    cfg_target.class_name = tostring(candidate.class_name or "")
    cfg_target.path = tostring(candidate.path or "")
    return true
end

function M.foreground()
    if not wnd or type(wnd.get_foreground) ~= "function" then
        return nil
    end

    local ok, hwnd = safe_call(wnd.get_foreground)
    if not ok or not hwnd or hwnd == 0 then
        return nil
    end

    local pid = 0
    if wnd.get_pid then
        local pid_ok, value = safe_call(wnd.get_pid, hwnd)
        if pid_ok then
            pid = tonumber(value) or 0
        end
    end

    return {
        pid = pid,
        hwnd = hwnd,
        title = window_title(hwnd),
        class_name = window_class(hwnd),
    }
end

M.frontground = M.foreground

function M.state_summary(core_module)
    if not core_module or type(core_module.getState) ~= "function" then
        return false, nil, "core.getState unavailable"
    end

    local ok, state, err = core_module.getState()
    if not ok or type(state) ~= "table" then
        return false, nil, err or "state unavailable"
    end

    return true, {
        pid = tonumber(state.pid) or 0,
        hwnd = tonumber(state.hwnd) or 0,
        inited = state.inited == true,
    }, nil
end

function M.validate_binding(cfg_target, core_module)
    local ok, state, err = M.state_summary(core_module)
    if not ok then
        return false, "state_error", err, nil
    end

    local expected_pid = tonumber(cfg_target and cfg_target.pid) or 0
    if expected_pid <= 0 then
        return false, "not_selected", "target pid is not selected", state
    end

    if state.pid ~= expected_pid then
        return false, "pid_mismatch", string.format(
            "selected pid=%s, AionData pid=%s",
            tostring(expected_pid),
            tostring(state.pid)), state
    end

    return true, "matched", "target pid matched", state
end

return M
