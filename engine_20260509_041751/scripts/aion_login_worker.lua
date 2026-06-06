--[[
    Aion login worker.

    Runs in a separate task VM so the blocking AionLogin.AutoLogin call does
    not freeze the control UI. The UI marks queued accounts in script_config,
    starts this worker with a queue_id, then polls sys.get_share status keys.
]]

local ok_target, target = pcall(require, "aion.target")

local queue_id = tostring(queue_id or "default")
local selected_index = tonumber(account_index or "0") or 0

local function share_key(index, field)
    return "aion_login." .. queue_id .. "." .. tostring(index) .. "." .. tostring(field)
end

local function queue_key(field)
    return "aion_login." .. queue_id .. ".queue." .. tostring(field or "")
end

local function get_queue_value(field, default)
    if not sys or type(sys.get_share) ~= "function" then
        return default
    end
    local value = sys.get_share(queue_key(field))
    if value == nil then
        return default
    end
    return value
end

local function set_status(index, status, message)
    if sys and sys.set_share then
        sys.set_share(share_key(index, "status"), tostring(status or "unknown"))
        sys.set_share(share_key(index, "message"), tostring(message or ""))
        sys.set_share(share_key(index, "updated_at"), os.time())
    end
end

local function set_result(index, ret, message)
    if sys and sys.set_share then
        sys.set_share(share_key(index, "ret"), tonumber(ret) or 0)
        sys.set_share(share_key(index, "message"), tostring(message or ""))
        sys.set_share(share_key(index, "done"), true)
        sys.set_share(share_key(index, "updated_at"), os.time())
    end
end

local function set_target(index, candidate)
    if not candidate or not sys or not sys.set_share then
        return
    end
    sys.set_share(share_key(index, "pid"), tonumber(candidate.pid) or 0)
    sys.set_share(share_key(index, "hwnd"), tonumber(candidate.hwnd) or 0)
    sys.set_share(share_key(index, "title"), tostring(candidate.title or ""))
end

local function sleep(ms)
    if sys and sys.sleep then
        sys.sleep(ms)
    end
end

local function now_ms()
    if sys and type(sys.time) == "function" then
        return sys.time()
    end
    return os.time() * 1000
end

local function set_progress(value)
    if task and type(task.set_progress) == "function" then
        task.set_progress(value)
    end
end

local function load_accounts_config()
    local shared_count = tonumber(get_queue_value("count", 0)) or 0
    if shared_count > 0 then
        local accounts = {
            game_path = tostring(get_queue_value("game_path", "")),
            purple_root = tostring(get_queue_value("purple_root", "")),
            dll_path = tostring(get_queue_value("dll_path", "")),
            lang = tostring(get_queue_value("lang", "")),
            captcha_key = tostring(get_queue_value("captcha_key", "")),
            decode_mail = tostring(get_queue_value("decode_mail", "")),
            pid_wait_seconds = tonumber(get_queue_value("pid_wait_seconds", 60)) or 60,
            login_gap_ms = tonumber(get_queue_value("login_gap_ms", 1500)) or 1500,
            items = {},
        }

        for queue_index = 1, shared_count do
            local prefix = "item." .. tostring(queue_index) .. "."
            table.insert(accounts.items, {
                __index = tonumber(get_queue_value(prefix .. "index", queue_index)) or queue_index,
                enabled = true,
                account = tostring(get_queue_value(prefix .. "account", "")),
                password = tostring(get_queue_value(prefix .. "password", "")),
                second_password = tostring(get_queue_value(prefix .. "second_password", "")),
                phone = tostring(get_queue_value(prefix .. "phone", "")),
                label = tostring(get_queue_value(prefix .. "label", "")),
                login = {
                    requested = true,
                },
            })
        end

        return accounts, nil
    end

    if not config or not config.load or not config.get then
        return nil, "config module unavailable"
    end

    config.load()
    local accounts = config.get("aion_control.accounts", {})
    if type(accounts) ~= "table" then
        return nil, "aion_control.accounts is not a table"
    end
    if type(accounts.items) ~= "table" then
        accounts.items = {}
    end
    return accounts, nil
end

local function account_source_index(loop_index, account)
    return tonumber(account and account.__index) or loop_index
end

local function candidate_map()
    local out = {}
    if not ok_target or not target then
        return out
    end

    local ok, list = target.list_candidates({})
    if not ok or type(list) ~= "table" then
        return out
    end

    for _, item in ipairs(list) do
        local pid = tonumber(item.pid) or 0
        if pid > 0 then
            out[pid] = true
        end
    end
    return out
end

local function find_new_candidate(before)
    if not ok_target or not target then
        return nil
    end

    local ok, list = target.list_candidates({})
    if not ok or type(list) ~= "table" then
        return nil
    end

    for _, item in ipairs(list) do
        local pid = tonumber(item.pid) or 0
        if pid > 0 and not before[pid] then
            return item
        end
    end
    return nil
end

local function wait_for_game_window(before, timeout_seconds)
    local deadline = now_ms() + ((timeout_seconds or 60) * 1000)
    local last_candidate = nil

    while now_ms() <= deadline do
        local candidate = find_new_candidate(before)
        if candidate then
            return candidate
        end

        if ok_target and target then
            local ok, list = target.list_candidates({})
            if ok and type(list) == "table" and #list > 0 then
                last_candidate = list[#list]
            end
        end
        sleep(1000)
    end

    return last_candidate
end

local function optional_arg_string(value)
    if value == nil or value == false or value == true then
        return ""
    end
    return tostring(value)
end

local function should_login(index, account)
    if selected_index > 0 and index ~= selected_index then
        return false
    end
    if type(account) ~= "table" then
        return false
    end
    if account.enabled == false then
        return false
    end
    if tostring(account.account or "") == "" then
        return false
    end
    if selected_index > 0 then
        return true
    end
    return type(account.login) == "table" and account.login.requested == true
end

local function call_auto_login(login, accounts_cfg, account, index)
    local game_path = tostring(accounts_cfg.game_path or "")
    local purple_root = tostring(accounts_cfg.purple_root or "")
    local lang = optional_arg_string(accounts_cfg.lang)
    local captcha_key = optional_arg_string(accounts_cfg.captcha_key)
    local phone = tostring(account.phone or "")
    local decode_mail = optional_arg_string(accounts_cfg.decode_mail)

    if tostring(account.account or "") == "" or tostring(account.password or "") == "" then
        set_result(index, 0, "account or password is empty")
        set_status(index, "error", "account or password is empty")
        return 0, "account or password is empty"
    end

    if game_path == "" or purple_root == "" then
        set_result(index, 0, "game_path or purple_root is empty")
        set_status(index, "error", "game_path or purple_root is empty")
        return 0, "game_path or purple_root is empty"
    end

    local before = candidate_map()
    set_status(index, "logging_in", "AutoLogin running")
    set_progress(0.15)

    local ok, ret = pcall(login.AutoLogin,
        tostring(account.account or ""),
        tostring(account.password or ""),
        game_path,
        purple_root,
        lang,
        captcha_key,
        phone,
        decode_mail)

    if not ok then
        set_result(index, 0, tostring(ret))
        set_status(index, "error", tostring(ret))
        return 0, tostring(ret)
    end

    ret = tonumber(ret) or 0
    local ret_text = login.RetText and login.RetText[ret] or "unknown"
    set_result(index, ret, ret_text)

    if ret ~= 1 then
        set_status(index, "error", ret_text)
        return ret, ret_text
    end

    set_status(index, "waiting_pid", "login success, waiting for game window")
    set_progress(0.6)
    local candidate = wait_for_game_window(before, tonumber(accounts_cfg.pid_wait_seconds) or 60)
    if candidate then
        set_target(index, candidate)
        set_status(index, "ready", "pid=" .. tostring(candidate.pid))
        if log and type(log.info) == "function" then
            log.info("[AionLoginWorker] detected game pid=" .. tostring(candidate.pid) .. " hwnd=" .. tostring(candidate.hwnd or 0))
        end
    else
        set_status(index, "game_started", "login success, but pid not detected")
        if log and type(log.warn) == "function" then
            log.warn("[AionLoginWorker] login success, but pid not detected")
        end
    end

    return ret, ret_text
end

local function run()
    local accounts_cfg, cfg_err = load_accounts_config()
    if not accounts_cfg then
        if sys and sys.set_share then
            sys.set_share("aion_login." .. queue_id .. ".error", cfg_err)
        end
        return
    end

    local ok_login, login = pcall(require, "AionLogin")
    if not ok_login or not login then
        for loop_index, account in ipairs(accounts_cfg.items) do
            local source_index = account_source_index(loop_index, account)
            if should_login(source_index, account) then
                set_status(source_index, "error", "AionLogin module unavailable")
            end
        end
        return
    end

    if tostring(accounts_cfg.dll_path or "") ~= "" and type(login.SetDllPath) == "function" then
        pcall(login.SetDllPath, tostring(accounts_cfg.dll_path))
    end

    local total = 0
    for loop_index, account in ipairs(accounts_cfg.items) do
        local source_index = account_source_index(loop_index, account)
        if should_login(source_index, account) then
            total = total + 1
        end
    end

    if total <= 0 then
        if sys and sys.set_share then
            sys.set_share("aion_login." .. queue_id .. ".done", true)
            sys.set_share("aion_login." .. queue_id .. ".message", "no queued account")
        end
        return
    end

    local done = 0
    for loop_index, account in ipairs(accounts_cfg.items) do
        local source_index = account_source_index(loop_index, account)
        if should_login(source_index, account) then
            set_status(source_index, "logging_in", "starting")
            call_auto_login(login, accounts_cfg, account, source_index)
            done = done + 1
            set_progress(done / total)
            sleep(tonumber(accounts_cfg.login_gap_ms) or 1500)
        end
    end

    if sys and sys.set_share then
        sys.set_share("aion_login." .. queue_id .. ".done", true)
        sys.set_share("aion_login." .. queue_id .. ".message", "login worker completed")
    end
end

local ok, err = pcall(run)
if not ok then
    if sys and sys.set_share then
        sys.set_share("aion_login." .. queue_id .. ".error", tostring(err))
    end
    error(err)
end
