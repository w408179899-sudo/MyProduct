--[[
    Watches the newly launched Aion client while AionLogin.AutoLogin is blocked
    and clicks the user agreement button as soon as the agreement page appears.
]]

local ok_target, target = pcall(require, "aion.target")
local ok_core, core = pcall(require, "aion.core")
local ok_login_flow, login_flow = pcall(require, "aion.login_flow")

local queue_id = tostring(queue_id or "default")
local account_index = tonumber(account_index or "0") or 0
local known_pids_text = tostring(known_pids or "")
local timeout_seconds = tonumber(timeout_seconds or "90") or 90
local poll_interval_ms = tonumber(poll_interval_ms or "500") or 500

local function share_key(field)
    return "aion_login." .. queue_id .. "." .. tostring(account_index) .. "." .. tostring(field)
end

local function set_share(field, value)
    if sys and type(sys.set_share) == "function" then
        sys.set_share(share_key(field), value)
    end
end

local function set_status(status, message)
    set_share("agreement_status", tostring(status or "unknown"))
    set_share("agreement_message", tostring(message or ""))
    set_share("agreement_updated_at", os.time())
end

local function sleep(ms)
    if sys and type(sys.sleep) == "function" then
        sys.sleep(ms)
    end
end

local function now_ms()
    if sys and type(sys.time) == "function" then
        return sys.time()
    end
    return os.time() * 1000
end

local function log_line(level, event, data)
    if not log then
        return
    end
    local fn = log[level]
    if type(fn) ~= "function" then
        fn = log.info
    end
    if type(fn) == "function" then
        fn("[AionLoginAgreement] index=" .. tostring(account_index) ..
            " event=" .. tostring(event or "") ..
            (data and (" " .. tostring(data)) or ""))
    end
end

local function parse_known_pids(text)
    local out = {}
    local count = 0
    for pid in string.gmatch(tostring(text or ""), "%d+") do
        local value = tonumber(pid) or 0
        if value > 0 and not out[value] then
            count = count + 1
            out[value] = true
        end
    end
    return out, count
end

local function find_candidate(known, known_count)
    if not ok_target or not target or type(target.list_candidates) ~= "function" then
        return nil, "aion.target unavailable"
    end

    local ok, list, err = target.list_candidates({})
    if not ok or type(list) ~= "table" then
        return nil, err or "target list failed"
    end

    local fallback = nil
    for _, item in ipairs(list) do
        local pid = tonumber(item.pid) or 0
        if pid > 0 then
            if (tonumber(known_count) or 0) <= 0 then
                fallback = item
            end
            if not known[pid] then
                return item, nil
            end
        end
    end
    return fallback, nil
end

local function ensure_init(pid)
    if not ok_core or not core or type(core.ensureInit) ~= "function" then
        return false, "aion.core unavailable"
    end
    return core.ensureInit(pid)
end

local function run()
    if not ok_login_flow or not login_flow or type(login_flow.acceptAgreement) ~= "function" then
        set_status("error", "aion.login_flow.acceptAgreement unavailable: " .. tostring(login_flow))
        return
    end

    local known, known_count = parse_known_pids(known_pids_text)
    local deadline = now_ms() + math.max(5, timeout_seconds) * 1000
    local selected = nil
    local last_err = nil
    set_status("watching", "waiting for agreement")
    log_line("info", "begin", "known_pids=" .. known_pids_text .. " timeout=" .. tostring(timeout_seconds))

    while now_ms() <= deadline do
        if not selected then
            local candidate, err = find_candidate(known, known_count)
            if candidate and (tonumber(candidate.pid) or 0) > 0 then
                selected = candidate
                set_share("agreement_pid", tonumber(candidate.pid) or 0)
                log_line("info", "target_detected", "pid=" .. tostring(candidate.pid) .. " hwnd=" .. tostring(candidate.hwnd or 0))
            elseif err and err ~= last_err then
                last_err = err
                log_line("warn", "target_wait_failed", tostring(err))
            end
        end

        if selected then
            local init_ok, init_err = ensure_init(selected.pid)
            if init_ok then
                local ok, clicked_or_absent = login_flow.acceptAgreement({
                    index = account_index,
                    sleep = sleep,
                    now_ms = now_ms,
                }, 1, 250)
                if ok and clicked_or_absent then
                    set_status("clicked", "agreement clicked")
                    log_line("info", "clicked", "pid=" .. tostring(selected.pid))
                    return
                elseif not ok then
                    set_status("error", tostring(clicked_or_absent))
                    log_line("warn", "click_failed", tostring(clicked_or_absent))
                    return
                end
            elseif tostring(init_err or "") ~= last_err then
                last_err = tostring(init_err or "")
                log_line("warn", "init_pending", "pid=" .. tostring(selected.pid) .. " err=" .. last_err)
            end
        end

        sleep(math.max(250, poll_interval_ms))
    end

    set_status("timeout", "agreement not observed")
    log_line("warn", "timeout", "last_err=" .. tostring(last_err or ""))
end

local ok, err = pcall(run)
if not ok then
    set_status("error", tostring(err))
    error(err)
end
