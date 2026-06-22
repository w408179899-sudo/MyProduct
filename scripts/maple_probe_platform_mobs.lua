local cwd = sys and sys.get_cwd and sys.get_cwd() or "."
package.path = cwd .. "/scripts/?.lua;" .. cwd .. "/scripts/?/init.lua;" .. package.path

local Blackboard = require("maple.blackboard")
local Logger = require("maple.systems.logger")
local MapleApi = require("maple.environment.maple_api")
local MapleEnvironment = require("maple.environment.maple_environment")
local Normalize = require("maple.environment.normalizers")
local PlatformMap = require("maple.navigation.platform_map")

local function write_line(message)
    message = "[platform_mobs] " .. tostring(message)
    if log and log.info then log.info(message) else print(message) end
end

local function sleep_ms(ms)
    if sys and sys.sleep then sys.sleep(tonumber(ms) or 0) end
end

local function now_ms()
    if os and os.clock then return os.clock() * 1000 end
    return 0
end

local function value(result)
    return result and result.data and result.data.value
end

local function diagnostic(result)
    return result and result.data and result.data.diagnostic or result and result.data
end

local function emit_result(name, result)
    local diag = diagnostic(result) or {}
    write_line(string.format(
        "[%s] %s elapsed=%.3fms count=%s",
        name,
        result and result.ok and "ok" or ("fail:" .. tostring(result and result.reason or "unknown")),
        tonumber(diag.elapsed_ms) or 0,
        tostring(diag.result_count or 0)
    ))
end

local function count(list)
    return type(list) == "table" and #list or 0
end

local function position(entity)
    entity = entity or {}
    return entity.position or { x = entity.x, y = entity.y, z = entity.z }
end

local function round3(value)
    local n = tonumber(value) or 0
    if n >= 0 then return math.floor(n * 1000 + 0.5) / 1000 end
    return math.ceil(n * 1000 - 0.5) / 1000
end

local function stat_for(stats, mob)
    local id = tostring(mob.id or mob.instance_id or mob.type_id or mob.name or "unknown")
    local item = stats[id]
    if not item then
        item = {
            id = id,
            name = mob.name,
            type_id = mob.type_id,
            seen = 0,
            min_x = nil,
            max_x = nil,
            min_y = nil,
            max_y = nil,
            min_delta = nil,
            max_delta = nil,
            sum_delta = 0,
            max_abs_vx = 0,
            max_abs_vy = 0,
            last = nil
        }
        stats[id] = item
    end
    return item
end

local function update_minmax(item, key_min, key_max, value)
    value = tonumber(value) or 0
    if item[key_min] == nil or value < item[key_min] then item[key_min] = value end
    if item[key_max] == nil or value > item[key_max] then item[key_max] = value end
end

local function update_stat(stats, mob, loc, sample_time_ms)
    local item = stat_for(stats, mob)
    local pos = position(mob)
    local x = tonumber(pos.x) or 0
    local y = tonumber(pos.y) or 0
    item.seen = item.seen + 1
    update_minmax(item, "min_x", "max_x", x)
    update_minmax(item, "min_y", "max_y", y)
    if loc then
        update_minmax(item, "min_delta", "max_delta", loc.y_delta)
        item.sum_delta = item.sum_delta + loc.y_delta
    end
    if item.last then
        local dt = (sample_time_ms - item.last.t) / 1000
        if dt > 0 then
            local vx = (x - item.last.x) / dt
            local vy = (y - item.last.y) / dt
            item.max_abs_vx = math.max(item.max_abs_vx, math.abs(vx))
            item.max_abs_vy = math.max(item.max_abs_vy, math.abs(vy))
        end
    end
    item.last = { x = x, y = y, t = sample_time_ms }
end

local account_idx = tonumber(account_index) or 0
local duration_ms = math.max(100, tonumber(probe_duration_ms) or ((tonumber(probe_run_seconds) or 5) * 1000))
local interval_ms = math.max(20, tonumber(probe_sample_ms) or 100)
local max_ticks = math.max(1, math.ceil(duration_ms / interval_ms))
local map_path = probe_platform_path or (cwd .. "/scripts/maple/maps/manual_platform.lua")
local y_tolerance = tonumber(probe_platform_y_tolerance) or 1.2
local x_margin = tonumber(probe_platform_x_margin) or 0.2
local max_log_mobs = math.max(1, tonumber(probe_max_log_mobs) or 5)

write_line(string.format(
    "started duration_ms=%d interval_ms=%d max_ticks=%d map=%s y_tolerance=%.3f",
    duration_ms,
    interval_ms,
    max_ticks,
    tostring(map_path),
    y_tolerance
))

local map, map_err = PlatformMap.load(map_path, { merge_epsilon = tonumber(probe_platform_merge_epsilon) or 0.05 })
if not map then
    write_line("map load failed reason=" .. tostring(map_err))
    return { ok = false, reason = "map_load_failed", error = map_err }
end
write_line(string.format("map loaded map_id=%s platforms=%d", tostring(map.map_id), count(map.platforms)))

local logger = Logger.new("platform_mobs", {
    level = "debug",
    print_to_console = false,
    keep_records = 100
})
local bb = Blackboard.new({ account_index = account_idx })
local api = MapleApi.new({
    module_name = probe_data_module or "data",
    logger = logger,
    account_index = account_idx
})
local env = MapleEnvironment.new({
    api = api,
    logger = logger,
    account_index = account_idx,
    target_name = probe_target_name or "msw.exe",
    license_key = probe_license_key,
    allow_mock_fallback = false
})

local connected = env:bind_client({
    params = {
        target_name = probe_target_name or "msw.exe",
        license_key = probe_license_key
    }
}, bb)
if not connected.ok then
    write_line("connect failed reason=" .. tostring(connected.reason))
    return { ok = false, reason = connected.reason }
end
write_line("connect ok pid=" .. tostring(connected.data and connected.data.pid))

local stats = {}
local platform_seen = {}
local started = now_ms()
local ticks = 0

while ticks < max_ticks do
    ticks = ticks + 1
    local tick_started = now_ms()
    if tick_started - started > duration_ms then break end

    local actor_result = api:call("player_info", bb)
    local nearby_result = api:call("list_nearby", bb)
    emit_result("player_info", actor_result)
    emit_result("list_nearby", nearby_result)

    local actor = Normalize.actor(value(actor_result), diagnostic(actor_result))
    local world = Normalize.world(value(nearby_result), diagnostic(nearby_result))
    local actor_loc = PlatformMap.locate_point(map, actor.position, {
        y_tolerance = y_tolerance,
        x_margin = x_margin
    })
    local actor_platform = actor_loc and actor_loc.platform_id or "none"

    local candidates = {}
    for _, mob in ipairs(world.nearby_targets or {}) do
        local loc = PlatformMap.locate_point(map, position(mob), {
            y_tolerance = y_tolerance,
            x_margin = x_margin
        })
        if loc and (actor_platform == "none" or loc.platform_id == actor_platform) then
            candidates[#candidates + 1] = { mob = mob, loc = loc }
            platform_seen[loc.platform_id] = (platform_seen[loc.platform_id] or 0) + 1
            update_stat(stats, mob, loc, tick_started)
        end
    end

    write_line(string.format(
        "tick=%d actor=(%.3f,%.3f) actor_platform=%s actor_y_delta=%s mobs=%d platform_candidates=%d",
        ticks,
        tonumber(actor.position and actor.position.x) or 0,
        tonumber(actor.position and actor.position.y) or 0,
        tostring(actor_platform),
        actor_loc and string.format("%.3f", actor_loc.y_delta) or "n/a",
        count(world.nearby_targets),
        #candidates
    ))

    table.sort(candidates, function(a, b)
        local ax = tonumber(position(a.mob).x) or 0
        local bx = tonumber(position(b.mob).x) or 0
        local actor_x = tonumber(actor.position and actor.position.x) or 0
        return math.abs(ax - actor_x) < math.abs(bx - actor_x)
    end)
    for i = 1, math.min(max_log_mobs, #candidates) do
        local item = candidates[i]
        local mob = item.mob
        local loc = item.loc
        local pos = position(mob)
        write_line(string.format(
            "tick=%d mob[%d] id=%s name=%s pos=(%.3f,%.3f) platform=%s platform_y=%.3f y_delta=%.3f",
            ticks,
            i,
            tostring(mob.id),
            tostring(mob.name),
            tonumber(pos.x) or 0,
            tonumber(pos.y) or 0,
            tostring(loc.platform_id),
            tonumber(loc.platform_y) or 0,
            tonumber(loc.y_delta) or 0
        ))
    end

    local elapsed = now_ms() - tick_started
    local wait_ms = interval_ms - elapsed
    if wait_ms > 0 then sleep_ms(wait_ms) end
end

local summary = {}
for _, item in pairs(stats) do
    summary[#summary + 1] = item
end
table.sort(summary, function(a, b)
    if a.seen == b.seen then return tostring(a.id) < tostring(b.id) end
    return a.seen > b.seen
end)

write_line(string.format("summary ticks=%d mob_tracks=%d", ticks, #summary))
for i = 1, math.min(12, #summary) do
    local item = summary[i]
    local avg_delta = item.seen > 0 and (item.sum_delta / item.seen) or 0
    write_line(string.format(
        "summary mob[%d] id=%s name=%s seen=%d x=[%.3f,%.3f] y=[%.3f,%.3f] y_delta=[%.3f,%.3f] avg_delta=%.3f max_abs_v=(%.3f,%.3f)",
        i,
        tostring(item.id),
        tostring(item.name),
        item.seen,
        round3(item.min_x),
        round3(item.max_x),
        round3(item.min_y),
        round3(item.max_y),
        round3(item.min_delta or 0),
        round3(item.max_delta or 0),
        round3(avg_delta),
        round3(item.max_abs_vx),
        round3(item.max_abs_vy)
    ))
end

write_line("finished")
return {
    ok = true,
    ticks = ticks,
    stats = summary,
    platform_seen = platform_seen,
    diagnostics = api.last_calls
}
