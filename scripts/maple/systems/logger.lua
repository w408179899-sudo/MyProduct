local Logger = {}
Logger.__index = Logger

local levels = { debug = 1, info = 2, warn = 3, error = 4 }

local function serialize(value, depth)
    depth = depth or 0
    if depth > 3 then return '"..."' end
    local t = type(value)
    if t == "string" then return string.format("%q", value) end
    if t == "number" or t == "boolean" or t == "nil" then return tostring(value) end
    if t ~= "table" then return string.format("%q", tostring(value)) end
    local parts = {}
    for k, v in pairs(value) do
        parts[#parts + 1] = tostring(k) .. "=" .. serialize(v, depth + 1)
    end
    return "{" .. table.concat(parts, ",") .. "}"
end

function Logger.new(module, cfg)
    cfg = cfg or {}
    return setmetatable({
        module = module or "maple",
        level = cfg.level or "debug",
        print_to_console = cfg.print_to_console ~= false,
        keep_records = tonumber(cfg.keep_records) or 200,
        records = {}
    }, Logger)
end

function Logger:write(level, event, data, bb)
    if levels[level] < levels[self.level] then return end
    local rec = {
        tick = bb and bb.runtime and bb.runtime.tick or 0,
        level = level,
        event = event,
        module = self.module,
        data = data or {}
    }
    self.records[#self.records + 1] = rec
    while #self.records > self.keep_records do table.remove(self.records, 1) end
    if self.print_to_console then
        local msg = string.format("[maple][%s][%s] %s", level, event, serialize(data or {}))
        if log and log[level] then log[level](msg) elseif log and log.info then log.info(msg) else print(msg) end
    end
end

function Logger:debug(event, data, bb) self:write("debug", event, data, bb) end
function Logger:info(event, data, bb) self:write("info", event, data, bb) end
function Logger:warn(event, data, bb) self:write("warn", event, data, bb) end
function Logger:error(event, data, bb) self:write("error", event, data, bb) end

return Logger
