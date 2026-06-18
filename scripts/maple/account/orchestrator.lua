local Store = require("maple.account.store")

local Orchestrator = {}
Orchestrator.__index = Orchestrator

local function now()
    if os and os.time then return os.time() end
    return 0
end

local function set_share(sys_api, key, value)
    if sys_api and sys_api.set_share then sys_api.set_share(key, value) end
end

function Orchestrator.new(deps)
    deps = deps or {}
    return setmetatable({
        task_api = deps.task_api or task,
        sys_api = deps.sys_api or sys,
        worker_script = deps.worker_script or "scripts/maple_account_worker.lua"
    }, Orchestrator)
end

function Orchestrator:mark_status(index, status, detail)
    set_share(self.sys_api, Store.status_key(index, "status"), status or "")
    set_share(self.sys_api, Store.status_key(index, "detail"), detail or "")
end

function Orchestrator:start_account(account, index)
    if not account then return false, "missing_account" end
    if account.enabled == false then return false, "account_disabled" end
    if not self.task_api or type(self.task_api.run) ~= "function" then
        return false, "task_run_unavailable"
    end
    account.runtime = account.runtime or {}
    if account.runtime.task_id then return true, "already_running" end
    set_share(self.sys_api, Store.status_key(index, "stop"), false)
    local id = self.task_api.run(self.worker_script, {
        name = string.format("MapleAccount%d", tonumber(index) or 0),
        priority = "normal",
        account_index = tostring(index or 0),
        account_key = tostring(account.key or account.account or index or "")
    })
    if not id then
        self:mark_status(index, "start_failed", "task.run returned nil")
        return false, "task_run_failed"
    end
    account.runtime.task_id = id
    account.runtime.status = "running"
    account.runtime.started_at = now()
    self:mark_status(index, "running", "task started")
    return true, id
end

function Orchestrator:stop_account(account, index, reason)
    if not account then return false, "missing_account" end
    account.runtime = account.runtime or {}
    set_share(self.sys_api, Store.status_key(index, "stop"), true)
    local id = account.runtime.task_id
    if id and self.task_api and type(self.task_api.stop) == "function" then
        pcall(self.task_api.stop, id)
    end
    account.runtime.task_id = nil
    account.runtime.status = "stopped"
    account.runtime.stopped_at = now()
    self:mark_status(index, "stopped", reason or "manual_stop")
    return true
end

function Orchestrator:start_all(root)
    local started = 0
    local max_parallel = tonumber(root.max_parallel) or #(root.items or {})
    for i, account in ipairs(root.items or {}) do
        if started >= max_parallel then break end
        local ok = self:start_account(account, i)
        if ok then started = started + 1 end
    end
    return started
end

function Orchestrator:stop_all(root, reason)
    local stopped = 0
    for i, account in ipairs(root.items or {}) do
        local ok = self:stop_account(account, i, reason or "stop_all")
        if ok then stopped = stopped + 1 end
    end
    return stopped
end

function Orchestrator:poll_account(account, index)
    if not account or not account.runtime or not account.runtime.task_id then return nil end
    if not self.task_api or type(self.task_api.info) ~= "function" then return nil end
    local info = self.task_api.info(account.runtime.task_id)
    if not info then return nil end
    account.runtime.status = info.status or account.runtime.status
    self:mark_status(index, account.runtime.status or "", tostring(info.progress or ""))
    return info
end

return Orchestrator
