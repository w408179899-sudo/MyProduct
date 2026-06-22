local PlatformMap = {}

local function number_or(value, fallback)
    local n = tonumber(value)
    if n == nil then return fallback end
    return n
end

local function copy_point(point)
    return {
        x = number_or(point and point.x, 0),
        y = number_or(point and point.y, 0),
        z = number_or(point and point.z, 0),
        t = point and point.t,
        source = point and point.source
    }
end

local function sort_points(points)
    local out = {}
    for _, point in ipairs(points or {}) do
        out[#out + 1] = copy_point(point)
    end
    table.sort(out, function(a, b)
        if a.x == b.x then return a.y < b.y end
        return a.x < b.x
    end)
    return out
end

local function merge_points(points, epsilon)
    epsilon = number_or(epsilon, 0.05)
    local sorted = sort_points(points)
    local merged = {}
    local bucket = nil

    local function flush()
        if not bucket then return end
        merged[#merged + 1] = {
            x = bucket.x_sum / bucket.count,
            y = bucket.y_sum / bucket.count,
            z = bucket.z_sum / bucket.count,
            count = bucket.count,
            source = "merged"
        }
        bucket = nil
    end

    for _, point in ipairs(sorted) do
        if not bucket or math.abs(point.x - bucket.last_x) > epsilon then
            flush()
            bucket = {
                x_sum = point.x,
                y_sum = point.y,
                z_sum = point.z,
                count = 1,
                last_x = point.x
            }
        else
            bucket.x_sum = bucket.x_sum + point.x
            bucket.y_sum = bucket.y_sum + point.y
            bucket.z_sum = bucket.z_sum + point.z
            bucket.count = bucket.count + 1
            bucket.last_x = point.x
        end
    end
    flush()
    return merged
end

local function normalize_platform(platform, opts)
    opts = opts or {}
    platform = platform or {}
    local points = merge_points(platform.normalized_points or platform.points or {}, opts.merge_epsilon)
    local left_x = number_or(platform.left_x, nil)
    local right_x = number_or(platform.right_x, nil)
    for _, point in ipairs(points) do
        if not left_x or point.x < left_x then left_x = point.x end
        if not right_x or point.x > right_x then right_x = point.x end
    end
    return {
        id = tostring(platform.id or "platform"),
        left_x = number_or(left_x, 0),
        right_x = number_or(right_x, 0),
        safe_margin = number_or(platform.safe_margin, 1),
        points = platform.points or {},
        normalized_points = points
    }
end

function PlatformMap.normalize(raw, opts)
    opts = opts or {}
    raw = raw or {}
    local platforms = {}
    for _, platform in ipairs(raw.platforms or {}) do
        platforms[#platforms + 1] = normalize_platform(platform, opts)
    end
    return {
        version = raw.version or 1,
        kind = raw.kind,
        map_id = raw.map_id,
        map_name = raw.map_name,
        platforms = platforms
    }
end

function PlatformMap.y_at(platform, x)
    platform = platform or {}
    local points = platform.normalized_points or platform.points or {}
    x = number_or(x, 0)
    if #points == 0 then return nil end
    if #points == 1 then return points[1].y end
    if x <= points[1].x then return points[1].y end
    if x >= points[#points].x then return points[#points].y end

    for i = 1, #points - 1 do
        local a = points[i]
        local b = points[i + 1]
        if x >= a.x and x <= b.x then
            local span = b.x - a.x
            if math.abs(span) < 0.000001 then return a.y end
            local ratio = (x - a.x) / span
            return a.y + (b.y - a.y) * ratio
        end
    end
    return nil
end

function PlatformMap.point_delta(platform, point, opts)
    opts = opts or {}
    if not platform or not point then return nil end
    local margin = number_or(opts.x_margin, 0)
    local x = number_or(point.x, 0)
    local y = number_or(point.y, 0)
    local in_x = x >= (platform.left_x - margin) and x <= (platform.right_x + margin)
    local platform_y = PlatformMap.y_at(platform, x)
    if platform_y == nil then return nil end
    local y_delta = y - platform_y
    return {
        platform_id = platform.id,
        platform = platform,
        x = x,
        y = y,
        platform_y = platform_y,
        y_delta = y_delta,
        abs_y_delta = math.abs(y_delta),
        in_x = in_x
    }
end

function PlatformMap.locate_point(map, point, opts)
    opts = opts or {}
    map = map or {}
    local best = nil
    for _, platform in ipairs(map.platforms or {}) do
        local delta = PlatformMap.point_delta(platform, point, opts)
        if delta and delta.in_x then
            if not best or delta.abs_y_delta < best.abs_y_delta then best = delta end
        end
    end
    local tolerance = number_or(opts.y_tolerance, nil)
    if tolerance and best and best.abs_y_delta > tolerance then return nil, best end
    return best, best
end

function PlatformMap.load(path, opts)
    local ok, raw = pcall(dofile, path)
    if not ok then return nil, tostring(raw) end
    if type(raw) ~= "table" then return nil, "map_file_not_table" end
    return PlatformMap.normalize(raw, opts), nil
end

return PlatformMap
