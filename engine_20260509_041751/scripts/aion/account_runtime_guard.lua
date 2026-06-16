local M = {}

local queue_blocking_statuses = {
    queued_start = true,
    starting = true,
    running = true,
}

local begin_blocking_statuses = {
    starting = true,
    running = true,
}

local spawn_blocking_statuses = {
    running = true,
}

local function norm_status(status)
    return tostring(status or "")
end

local function account_runtime(account)
    if type(account) ~= "table" then
        return {}
    end
    return account.runtime or {}
end

local function task_active(runtime, is_task_running)
    local id = tonumber(runtime and runtime.task_id) or 0
    if id <= 0 or type(is_task_running) ~= "function" then
        return false
    end
    return is_task_running(id) == true
end

local function pending_matches(request, index)
    if type(request) ~= "table" then
        return false
    end
    if tostring(request.action or "") ~= "start" then
        return false
    end
    return (tonumber(request.index) or 0) == (tonumber(index) or 0)
end

function M.is_runtime_active(status)
    return queue_blocking_statuses[norm_status(status)] == true
end

function M.has_pending_start(runtime_accounts, index)
    if type(runtime_accounts) ~= "table" then
        return false
    end
    if pending_matches(runtime_accounts.pending_script, index) then
        return true
    end
    local queue = runtime_accounts.pending_scripts
    if type(queue) == "table" then
        for _, request in ipairs(queue) do
            if pending_matches(request, index) then
                return true
            end
        end
    end
    return false
end

function M.can_queue_start(ctx)
    ctx = ctx or {}
    local runtime = account_runtime(ctx.account)
    local index = tonumber(ctx.index) or 0

    if M.has_pending_start(ctx.runtime_accounts, index) then
        return {
            allowed = false,
            reason = "start-pending",
            message = "start already queued for account",
        }
    end

    if queue_blocking_statuses[norm_status(runtime.status)] then
        return {
            allowed = false,
            reason = "account-runtime-active",
            message = "account runtime already active",
        }
    end

    if task_active(runtime, ctx.is_task_running) then
        return {
            allowed = false,
            reason = "account-task-active",
            message = "account task already active",
        }
    end

    return { allowed = true, reason = "ok" }
end

function M.can_begin_start(ctx)
    ctx = ctx or {}
    local runtime = account_runtime(ctx.account)

    if begin_blocking_statuses[norm_status(runtime.status)] then
        return {
            allowed = false,
            reason = "account-runtime-active",
            message = "account runtime already active",
        }
    end

    if task_active(runtime, ctx.is_task_running) then
        return {
            allowed = false,
            reason = "account-task-active",
            message = "account task already active",
        }
    end

    return { allowed = true, reason = "ok" }
end

function M.can_spawn_worker(ctx)
    ctx = ctx or {}
    local runtime = account_runtime(ctx.account)

    if spawn_blocking_statuses[norm_status(runtime.status)] then
        return {
            allowed = false,
            reason = "account-runtime-active",
            message = "account runtime already active",
        }
    end

    if task_active(runtime, ctx.is_task_running) then
        return {
            allowed = false,
            reason = "account-task-active",
            message = "account task already active",
        }
    end

    return { allowed = true, reason = "ok" }
end

return M
