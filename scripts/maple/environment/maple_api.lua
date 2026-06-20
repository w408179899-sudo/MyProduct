local Result = require("maple.core.result")

local MapleApi = {}
MapleApi.__index = MapleApi

local function now_ms()
    if os and os.clock then return os.clock() * 1000 end
    return 0
end

local function result_count(value)
    if type(value) ~= "table" then return 0 end
    if type(value.mobs) == "table" then return #value.mobs end
    if type(value.drops) == "table" then return #value.drops end
    if type(value.items) == "table" then return #value.items end
    if type(value.skills) == "table" then return #value.skills end
    if type(value.portals) == "table" then return #value.portals end
    return #value
end

local function update_bb(bb, diagnostic, failed)
    if not bb then return end
    if bb.debug then bb.debug.last_api_call = diagnostic end
    if bb.metrics then
        bb.metrics.api_call_count = (bb.metrics.api_call_count or 0) + 1
        bb.metrics.latest_api_latency_ms = diagnostic.elapsed_ms
        if failed then bb.metrics.api_error_count = (bb.metrics.api_error_count or 0) + 1 end
    end
end

function MapleApi.new(opts)
    opts = opts or {}
    return setmetatable({
        data = opts.data_module,
        module_name = opts.module_name or "data",
        logger = opts.logger,
        account_index = opts.account_index,
        last_calls = {},
        keep_records = tonumber(opts.keep_records) or 100
    }, MapleApi)
end

function MapleApi:load()
    if self.data then return true, self.data end
    local ok, mod = pcall(require, self.module_name)
    if ok then
        self.data = mod
        return true, mod
    end
    return false, tostring(mod)
end

function MapleApi:remember(diagnostic)
    self.last_calls[#self.last_calls + 1] = diagnostic
    while #self.last_calls > self.keep_records do table.remove(self.last_calls, 1) end
end

function MapleApi:call(name, bb, ...)
    local loaded, mod_or_err = self:load()
    local started = now_ms()
    if not loaded then
        local diagnostic = {
            api_name = name,
            ok = false,
            reason = "module_unavailable",
            error = mod_or_err,
            elapsed_ms = 0,
            result_count = 0,
            account_index = self.account_index
        }
        self:remember(diagnostic)
        update_bb(bb, diagnostic, true)
        if self.logger then self.logger:warn("maple_api_failure", diagnostic, bb) end
        return Result.failure("module_unavailable", diagnostic)
    end

    local fn = mod_or_err[name]
    if type(fn) ~= "function" then
        local diagnostic = {
            api_name = name,
            ok = false,
            reason = "api_missing",
            elapsed_ms = 0,
            result_count = 0,
            account_index = self.account_index
        }
        self:remember(diagnostic)
        update_bb(bb, diagnostic, true)
        if self.logger then self.logger:warn("maple_api_failure", diagnostic, bb) end
        return Result.failure("api_missing", diagnostic)
    end

    local values = nil
    local ok, err = pcall(function(...) values = { fn(...) } end, ...)
    local elapsed = now_ms() - started
    if not ok then
        local diagnostic = {
            api_name = name,
            ok = false,
            reason = "api_error",
            error = tostring(err),
            elapsed_ms = elapsed,
            result_count = 0,
            account_index = self.account_index
        }
        self:remember(diagnostic)
        update_bb(bb, diagnostic, true)
        if self.logger then self.logger:warn("maple_api_failure", diagnostic, bb) end
        return Result.failure("api_error", diagnostic)
    end

    local diagnostic = {
        api_name = name,
        ok = true,
        elapsed_ms = elapsed,
        result_count = result_count(values and values[1]),
        account_index = self.account_index
    }
    self:remember(diagnostic)
    update_bb(bb, diagnostic, false)
    if self.logger then self.logger:debug("maple_api_success", diagnostic, bb) end
    return Result.success({
        value = values and values[1],
        values = values or {},
        diagnostic = diagnostic
    })
end

return MapleApi
