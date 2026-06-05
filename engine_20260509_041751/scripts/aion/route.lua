local core = require("aion.core")

local M = {}

local function trim(text)
    return tostring(text or ""):match("^%s*(.-)%s*$")
end

local function parse_number_list(line)
    local values = {}
    for token in string.gmatch(line, "[-+]?%d+%.?%d*") do
        values[#values + 1] = tonumber(token)
        if #values >= 3 then
            break
        end
    end
    return values
end

function M.point(x, y, z)
    return {
        x = tonumber(x) or 0,
        y = tonumber(y) or 0,
        z = tonumber(z) or 0,
    }
end

function M.formatPoint(pt)
    return string.format("%.3f, %.3f, %.3f", pt.x or 0, pt.y or 0, pt.z or 0)
end

function M.parse(text)
    local points = {}
    local warnings = {}
    local line_no = 0

    for raw in string.gmatch(text or "", "[^\r\n]+") do
        line_no = line_no + 1
        local line = trim(raw)
        if line ~= "" and not string.match(line, "^#") and not string.match(line, "^%-%-") then
            local nums = parse_number_list(line)
            if #nums >= 3 then
                points[#points + 1] = M.point(nums[1], nums[2], nums[3])
            else
                warnings[#warnings + 1] = string.format("line %d invalid: %s", line_no, raw)
            end
        end
    end

    return true, points, warnings
end

function M.serialize(points)
    local lines = {}
    for _, pt in ipairs(points or {}) do
        lines[#lines + 1] = M.formatPoint(pt)
    end
    return table.concat(lines, "\n")
end

function M.lastPoint(text)
    local _, points = M.parse(text)
    return points[#points]
end

function M.appendText(text, pt, minDistance)
    if not pt then
        return text or "", false, "point is nil"
    end

    local last = M.lastPoint(text)
    if last and minDistance and minDistance > 0 then
        local dist = core.distance3(last, pt)
        if dist < minDistance then
            return text or "", false, string.format("too close: %.2f < %.2f", dist, minDistance)
        end
    end

    local line = M.formatPoint(pt)
    if not text or text == "" then
        return line, true, nil
    end
    return text .. "\n" .. line, true, nil
end

function M.distance(points)
    local total = 0
    local prev = nil
    for _, pt in ipairs(points or {}) do
        if prev then
            total = total + core.distance3(prev, pt)
        end
        prev = pt
    end
    return total
end

function M.stats(points)
    return {
        count = #(points or {}),
        distance = M.distance(points),
    }
end

function M.reverse(points)
    local out = {}
    for i = #(points or {}), 1, -1 do
        out[#out + 1] = points[i]
    end
    return out
end

function M.isValidPoint(pt)
    return type(pt) == "table"
        and type(pt.x) == "number"
        and type(pt.y) == "number"
        and type(pt.z) == "number"
end

function M.nextIndex(index, direction, count, opts)
    opts = opts or {}
    direction = direction or 1
    index = index + direction

    if count <= 0 then
        return nil, direction, true
    end

    if index >= 1 and index <= count then
        return index, direction, false
    end

    if opts.reverse_on_end and count > 1 then
        direction = -direction
        index = index + direction * 2
        if index >= 1 and index <= count then
            return index, direction, false
        end
    end

    if opts.loop then
        if direction > 0 then
            return 1, direction, false
        end
        return count, direction, false
    end

    return nil, direction, true
end

local function route_matches(item, selector, index)
    if selector == nil then
        return index == 1
    end
    if type(selector) == "number" then
        return index == selector
    end

    selector = tostring(selector)
    return tostring(item.id or "") == selector
        or tostring(item.label or "") == selector
        or tostring(item.name or "") == selector
        or tostring(item.points_field or "") == selector
end

function M.loadExportedPackage(path)
    local ok_profile, profile_io = pcall(require, "aion.profile_io")
    if not ok_profile or not profile_io then
        return false, nil, "aion.profile_io is unavailable"
    end

    local ok, package, err = profile_io.readPackage(path, "aion_routes")
    if not ok then
        return false, nil, err
    end

    return true, package.payload or {}, nil
end

function M.loadExportedPoints(path, selector)
    local ok, payload, err = M.loadExportedPackage(path)
    if not ok then
        return false, nil, err
    end

    for index, item in ipairs(payload.routes or {}) do
        if route_matches(item, selector, index) then
            if type(item.points) == "table" and #item.points > 0 then
                return true, item.points, item
            end
            if type(item.points_text) == "string" then
                local _, points, warnings = M.parse(item.points_text)
                return true, points, {
                    id = item.id,
                    label = item.label,
                    name = item.name,
                    warnings = warnings,
                }
            end
        end
    end

    return false, nil, "route not found: " .. tostring(selector)
end

return M
