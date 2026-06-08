local M = {}

local function trim(value)
    local text = tostring(value or "")
    text = string.gsub(text, "^%s+", "")
    text = string.gsub(text, "%s+$", "")
    return text
end

local function has_text(value)
    return trim(value) ~= ""
end

function M.is_login_ready(status)
    status = tostring(status or "")
    return status == "ready" or status == "game_started"
end

function M.is_runtime_active(status)
    status = tostring(status or "")
    return status == "starting"
        or status == "queued_start"
        or status == "running"
end

function M.has_route_points(text)
    return has_text(text)
end

function M.decide(ctx)
    ctx = ctx or {}
    local cfg = ctx.cfg or {}
    local account = ctx.account or {}
    local runtime = ctx.runtime or {}
    local accounts_cfg = cfg.accounts or {}
    local login = account.login or {}
    local account_runtime = account.runtime or {}
    local target = account.target or {}

    if accounts_cfg.auto_start_after_login ~= true then
        return { action = "none", reason = "disabled" }
    end

    if not M.is_login_ready(login.status) then
        return { action = "none", reason = "login-not-ready" }
    end

    local pid = tonumber(target.pid) or tonumber(cfg.target and cfg.target.pid) or 0
    if pid <= 0 then
        return { action = "block", reason = "pid-missing", message = "auto start blocked: target pid missing" }
    end

    if runtime.running == true then
        return { action = "none", reason = "runtime-running" }
    end

    if type(runtime.accounts) == "table" and type(runtime.accounts.pending_script) == "table" then
        return { action = "none", reason = "script-pending" }
    end

    if M.is_runtime_active(account_runtime.status) then
        return { action = "none", reason = "account-runtime-active" }
    end

    if type(ctx.is_task_running) == "function" and ctx.is_task_running(account_runtime.task_id) then
        return { action = "none", reason = "account-task-active" }
    end

    local primary_mode = tonumber(cfg.primary_mode) or 1
    if primary_mode == 1 then
        local combat_mode = tonumber(cfg.combat and cfg.combat.mode) or 1
        if combat_mode == 2 and not M.has_route_points(cfg.route and cfg.route.route_points) then
            return {
                action = "block",
                reason = "combat-route-empty",
                message = "auto start blocked: combat patrol route is empty",
            }
        end
    end

    return { action = "start", reason = "login-ready", pid = pid }
end

return M
