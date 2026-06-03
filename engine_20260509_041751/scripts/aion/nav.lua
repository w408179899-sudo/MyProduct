local core = require("aion.core")
local data = core.data

local M = {
    defaultMap = "map/world.wmap",
}

function M.load(mapFile)
    if not path or type(path.load) ~= "function" then
        return false, nil, "path module is unavailable"
    end
    local ok = path.load(mapFile or M.defaultMap)
    if not ok then
        return false, nil, "path.load failed"
    end
    return true, true, nil
end

function M.findRoute(fromPos, toPos, maxRange)
    if not path or type(path.find) ~= "function" then
        return false, nil, "path module is unavailable"
    end
    if not fromPos or not toPos then
        return false, nil, "fromPos or toPos is nil"
    end

    local ok, route, err = core.first(
        "path.find",
        path.find,
        fromPos.x,
        fromPos.y,
        fromPos.z or 0,
        toPos.x,
        toPos.y,
        toPos.z or 0,
        maxRange
    )
    if not ok then
        return false, nil, err
    end
    return true, route, nil
end

function M.moveTo(x, y, z)
    return core.first("AionData.MoveTo", data.MoveTo, x, y, z)
end

function M.waitArrive(x, y, z, range, timeoutMs)
    local target = { x = x, y = y, z = z }
    return core.waitUntil("arrival", function()
        local ok, pos = core.getPosition()
        if ok and pos and core.distance3(pos, target) <= (range or 3) then
            return pos
        end
        return nil
    end, timeoutMs or 10000, 200)
end

function M.follow(route, opts)
    opts = opts or {}
    if not route then
        return false, nil, "route is nil"
    end

    local moved = 0
    for _, pt in ipairs(route) do
        local ok, _, err = M.moveTo(pt.x, pt.y, pt.z or opts.z or 0)
        if not ok then
            return false, moved, err
        end
        moved = moved + 1
        if opts.wait ~= false then
            local arriveOk, arriveErr = M.waitArrive(pt.x, pt.y, pt.z or opts.z or 0, opts.range or 5, opts.timeoutMs or 10000)
            if not arriveOk then
                return false, moved, arriveErr
            end
        end
    end

    return true, moved, nil
end

function M.navigateTo(x, y, z, opts)
    opts = opts or {}
    local ok, fromPos, err = core.getPosition()
    if not ok then
        return false, nil, err
    end

    if opts.loadMap ~= false then
        local loadOk, _, loadErr = M.load(opts.mapFile)
        if not loadOk then
            return false, nil, loadErr
        end
    end

    local routeOk, route, routeErr = M.findRoute(fromPos, { x = x, y = y, z = z }, opts.maxRange)
    if not routeOk then
        return false, nil, routeErr
    end
    if not route then
        return false, nil, "route not found"
    end

    return M.follow(route, opts)
end

return M
