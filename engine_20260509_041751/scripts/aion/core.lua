local data = require("AionData")

local M = {
    data = data,
}

local unpack_fn = table.unpack or unpack

local function pack(...)
    return { n = select("#", ...), ... }
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

function M.ensureInit()
    local ok, values, err = M.call("AionData.InitGameinfo", data.InitGameinfo)
    if not ok then
        return false, err
    end
    if values[1] ~= true then
        return false, tostring(values[2] or "InitGameinfo returned false")
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
