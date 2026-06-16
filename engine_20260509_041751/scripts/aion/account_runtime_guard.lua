local M = {}

local stop_prefix = "aion_runtime.stop."

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

local function sanitize_identity(value)
    local text = tostring(value or "")
    text = string.gsub(text, "[^%w_%-%.]+", "_")
    text = string.gsub(text, "_+", "_")
    text = string.gsub(text, "^_+", "")
    text = string.gsub(text, "_+$", "")
    return text
end

local function add_identity(out, seen, kind, value)
    local text = sanitize_identity(value)
    if text == "" or text == "0" then
        return
    end
    local identity = kind .. "." .. text
    if seen[identity] then
        return
    end
    seen[identity] = true
    out[#out + 1] = identity
end

function M.stop_identities(account, index, worker_key, pid)
    account = type(account) == "table" and account or {}
    local runtime = account.runtime or {}
    local target = account.target or {}
    local out = {}
    local seen = {}

    add_identity(out, seen, "index", tonumber(index) or 0)
    add_identity(out, seen, "profile", account.profile_key)
    add_identity(out, seen, "worker", worker_key or runtime.worker_key)
    add_identity(out, seen, "pid", tonumber(pid) or tonumber(target.pid) or tonumber(runtime.bound_pid) or 0)

    return out
end

local function stop_share_key(identity, field)
    return stop_prefix .. tostring(identity or "") .. "." .. tostring(field or "")
end

function M.publish_stop_request(ctx)
    ctx = ctx or {}
    if type(ctx.set_share) ~= "function" then
        return { published = 0, reason = "set-share-missing" }
    end

    local requested_at = tonumber(ctx.requested_at) or tonumber(ctx.now) or os.time()
    local identities = M.stop_identities(ctx.account, ctx.index, ctx.worker_key, ctx.pid)
    for _, identity in ipairs(identities) do
        ctx.set_share(stop_share_key(identity, "requested_at"), requested_at)
        ctx.set_share(stop_share_key(identity, "source"), tostring(ctx.source or "stop"))
    end

    return {
        published = #identities,
        requested_at = requested_at,
        identities = identities,
    }
end

function M.clear_stop_request(ctx)
    ctx = ctx or {}
    if type(ctx.set_share) ~= "function" then
        return { cleared = 0, reason = "set-share-missing" }
    end

    local identities = M.stop_identities(ctx.account, ctx.index, ctx.worker_key, ctx.pid)
    for _, identity in ipairs(identities) do
        ctx.set_share(stop_share_key(identity, "requested_at"), 0)
        ctx.set_share(stop_share_key(identity, "source"), "")
    end

    return {
        cleared = #identities,
        identities = identities,
    }
end

function M.stop_requested(ctx)
    ctx = ctx or {}
    if type(ctx.get_share) ~= "function" then
        return { stop = false, reason = "get-share-missing" }
    end

    local started_at = tonumber(ctx.started_at) or 0
    local identities = M.stop_identities(ctx.account, ctx.index, ctx.worker_key, ctx.pid)
    for _, identity in ipairs(identities) do
        local requested_at = tonumber(ctx.get_share(stop_share_key(identity, "requested_at"))) or 0
        if requested_at > 0 and (started_at <= 0 or requested_at >= started_at) then
            return {
                stop = true,
                reason = "stop-requested",
                identity = identity,
                requested_at = requested_at,
                source = tostring(ctx.get_share(stop_share_key(identity, "source")) or ""),
            }
        end
    end

    return { stop = false, reason = "none" }
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
