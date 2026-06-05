local data = require("AionData")

local M = {
    data = data,
}

local unpack_fn = table.unpack or unpack

local function pack(...)
    return { n = select("#", ...), ... }
end

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

local function normalize_pid(pid)
    pid = tonumber(pid) or 0
    if pid > 0 then
        return math.floor(pid)
    end
    return nil
end

local function process_basename(value)
    local text = string.lower(tostring(value or "")):gsub("\\", "/")
    return text:match("([^/]+)$") or text
end

local function config_pid()
    if not config or type(config.get) ~= "function" then
        return nil
    end

    local ok, value = pcall(config.get, "aion_control.target.pid", 0)
    if ok then
        return normalize_pid(value)
    end
    return nil
end

local function first_aion_pid()
    if not proc or type(proc.list) ~= "function" then
        return nil
    end

    local ok, list = safe_call(proc.list)
    if not ok or type(list) ~= "table" then
        return nil
    end

    for _, p in ipairs(list) do
        local name = process_basename(p.name)
        local pid = normalize_pid(p.pid)
        if pid and name == "aion.bin" then
            return pid
        end
    end

    return nil
end

local function state_pid_if_inited()
    if type(data.GetState) ~= "function" then
        return nil
    end

    local ok, state = safe_call(data.GetState)
    if ok and type(state) == "table" and state.inited == true then
        return normalize_pid(state.pid)
    end
    return nil
end

function M.resolvePid(pid)
    return normalize_pid(pid)
        or config_pid()
        or state_pid_if_inited()
        or first_aion_pid()
end

function M.call(name, fn, ...)
    if type(fn) ~= "function" then
        return false, nil, string.format("%s is not callable", tostring(name))
    end

    local ret = pack(pcall(fn, ...))
    if not ret[1] then
        return false, nil, tostring(ret[2])
    end

    local values = { n = ret.n - 1 }
    for i = 2, ret.n do
        values[i - 1] = ret[i]
    end
    return true, values, nil
end

function M.first(name, fn, ...)
    local ok, values, err = M.call(name, fn, ...)
    if not ok then
        return false, nil, err
    end
    return true, values[1], nil
end

function M.ensureInit(pid)
    local target_pid = M.resolvePid(pid)
    if not target_pid then
        return false, "target pid is not selected"
    end

    local current_pid = state_pid_if_inited()
    if current_pid and current_pid ~= target_pid then
        return false, string.format(
            "AionData already initialized with pid=%s, selected pid=%s",
            tostring(current_pid),
            tostring(target_pid))
    end

    local ok, values, err = M.call("AionData.InitGameinfo", data.InitGameinfo, target_pid)
    if not ok then
        return false, err
    end
    if values[1] ~= true then
        return false, tostring(values[2] or "InitGameinfo returned false")
    end

    local post_pid = state_pid_if_inited()
    if post_pid and post_pid ~= target_pid then
        return false, string.format(
            "AionData initialized pid mismatch: expected=%s actual=%s",
            tostring(target_pid),
            tostring(post_pid))
    end

    return true, nil
end

function M.getState()
    return M.first("AionData.GetState", data.GetState)
end

function M.getScene()
    local ok, values, err = M.call("AionData.GetSceneIndex", data.GetSceneIndex)
    if not ok then
        return false, nil, err
    end
    return true, { index = values[1], name = values[2] }, nil
end

function M.getCharacter()
    return M.first("AionData.GetCharacter", data.GetCharacter)
end

function M.getPosition()
    local ok, char, err = M.getCharacter()
    if not ok then
        return false, nil, err
    end
    if not char then
        return false, nil, "character is nil"
    end
    return true, { x = char.x, y = char.y, z = char.z }, nil
end

function M.distance3(a, b)
    if not a or not b then
        return math.huge
    end
    local dx = (a.x or 0) - (b.x or 0)
    local dy = (a.y or 0) - (b.y or 0)
    local dz = (a.z or 0) - (b.z or 0)
    return math.sqrt(dx * dx + dy * dy + dz * dz)
end

function M.distance2(a, b)
    if not a or not b then
        return math.huge
    end
    local dx = (a.x or 0) - (b.x or 0)
    local dy = (a.y or 0) - (b.y or 0)
    return math.sqrt(dx * dx + dy * dy)
end

function M.sleep(ms)
    if sys and type(sys.sleep) == "function" then
        sys.sleep(ms)
    end
end

function M.nowMs()
    return math.floor(os.clock() * 1000)
end

function M.waitUntil(label, fn, timeoutMs, intervalMs)
    timeoutMs = timeoutMs or 3000
    intervalMs = intervalMs or 100
    local started = M.nowMs()

    while M.nowMs() - started <= timeoutMs do
        local ok, value = pcall(fn)
        if ok and value then
            return true, value
        end
        M.sleep(intervalMs)
    end

    return false, string.format("timeout waiting for %s", tostring(label))
end

function M.unpack(values)
    if not values then
        return nil
    end
    return unpack_fn(values, 1, values.n or #values)
end

return M
