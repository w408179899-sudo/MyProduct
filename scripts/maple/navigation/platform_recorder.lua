local PlatformRecorder = {}
PlatformRecorder.__index = PlatformRecorder

local DEFAULT_HOTKEYS = {
    start = 0x78,      -- F9
    pause = 0x79,      -- F10
    save = 0x7A,       -- F11
    clear = 0x7B,      -- F12
    mark_left = 0x70,  -- F1
    mark_right = 0x71  -- F2
}

local function now_ms()
    if os and os.clock then return os.clock() * 1000 end
    return 0
end

local function number_or(value, fallback)
    local n = tonumber(value)
    if n == nil then return fallback end
    return n
end

local function quote(value)
    if value == nil then return "nil" end
    return string.format("%q", tostring(value))
end

local function round3(value)
    local n = number_or(value, 0)
    if n >= 0 then return math.floor(n * 1000 + 0.5) / 1000 end
    return math.ceil(n * 1000 - 0.5) / 1000
end

local function distance(a, b)
    if not a or not b then return math.huge end
    local dx = number_or(a.x, 0) - number_or(b.x, 0)
    local dy = number_or(a.y, 0) - number_or(b.y, 0)
    return math.sqrt(dx * dx + dy * dy)
end

local function actor_point(actor)
    actor = actor or {}
    local pos = actor.position or actor
    if pos.x == nil or pos.y == nil then return nil end
    return {
        x = round3(pos.x),
        y = round3(pos.y),
        z = round3(pos.z or 0),
        map_id = actor.map_id or actor.current_map,
        map_name = actor.map_name
    }
end

local function sorted_hotkeys(hotkeys)
    return {
        { action = "start", key = hotkeys.start },
        { action = "pause", key = hotkeys.pause },
        { action = "save", key = hotkeys.save },
        { action = "clear", key = hotkeys.clear },
        { action = "mark_left", key = hotkeys.mark_left },
        { action = "mark_right", key = hotkeys.mark_right }
    }
end

local function parent_dir(path)
    if type(path) ~= "string" then return nil end
    return path:match("^(.*)[/\\][^/\\]+$")
end

local function ensure_dir(path)
    local dir = parent_dir(path)
    if not dir or dir == "" or not os or not os.execute then return end
    os.execute('mkdir "' .. dir .. '" >nul 2>nul')
end

local function encode_platform(data)
    local platform = data.platform or {}
    local points = platform.points or {}
    local lines = {}
    lines[#lines + 1] = "return {"
    lines[#lines + 1] = "    version = 1,"
    lines[#lines + 1] = "    kind = \"manual_platform\","
    lines[#lines + 1] = "    map_id = " .. quote(data.map_id) .. ","
    lines[#lines + 1] = "    map_name = " .. quote(data.map_name) .. ","
    lines[#lines + 1] = "    generated_at = " .. quote(data.generated_at) .. ","
    lines[#lines + 1] = "    platforms = {"
    lines[#lines + 1] = "        {"
    lines[#lines + 1] = "            id = " .. quote(platform.id) .. ","
    lines[#lines + 1] = "            left_x = " .. tostring(platform.left_x or 0) .. ","
    lines[#lines + 1] = "            right_x = " .. tostring(platform.right_x or 0) .. ","
    lines[#lines + 1] = "            safe_margin = " .. tostring(platform.safe_margin or 1) .. ","
    lines[#lines + 1] = "            points = {"
    for _, point in ipairs(points) do
        lines[#lines + 1] = string.format(
            "                { x = %.3f, y = %.3f, z = %.3f, t = %d, source = %s },",
            number_or(point.x, 0),
            number_or(point.y, 0),
            number_or(point.z, 0),
            math.floor(number_or(point.t, 0)),
            quote(point.source or "sample")
        )
    end
    lines[#lines + 1] = "            }"
    lines[#lines + 1] = "        }"
    lines[#lines + 1] = "    }"
    lines[#lines + 1] = "}"
    return table.concat(lines, "\n")
end

local function noop() end

function PlatformRecorder.new(opts)
    opts = opts or {}
    local hotkeys = {}
    for k, v in pairs(DEFAULT_HOTKEYS) do hotkeys[k] = v end
    for k, v in pairs(opts.hotkeys or {}) do hotkeys[k] = v end

    return setmetatable({
        read_actor = opts.read_actor,
        output = opts.output or noop,
        now_ms = opts.now_ms or now_ms,
        sample_ms = number_or(opts.sample_ms, 100),
        min_distance = number_or(opts.min_distance, 0.05),
        max_points = math.max(1, number_or(opts.max_points, 2000)),
        platform_id = tostring(opts.platform_id or "manual_1"),
        save_path = opts.save_path,
        safe_margin = number_or(opts.safe_margin, 1),
        hotkeys = hotkeys,
        key_state = {},
        recording = false,
        points = {},
        left_mark = nil,
        right_mark = nil,
        map_id = opts.map_id,
        map_name = opts.map_name,
        last_sample_ms = 0,
        last_point = nil,
        last_save_path = nil
    }, PlatformRecorder)
end

function PlatformRecorder:emit(message)
    self.output(message)
end

function PlatformRecorder:read_point(source)
    if type(self.read_actor) ~= "function" then
        return nil, "read_actor_missing"
    end
    local actor = self.read_actor()
    local point = actor_point(actor)
    if not point then return nil, "actor_position_missing" end
    point.t = self.now_ms()
    point.source = source or "sample"
    self.map_id = self.map_id or point.map_id
    self.map_name = self.map_name or point.map_name
    return point
end

function PlatformRecorder:add_point(source, force)
    local point, err = self:read_point(source)
    if not point then
        self:emit("record point failed reason=" .. tostring(err))
        return false, err
    end
    if not force and distance(point, self.last_point) < self.min_distance then
        return false, "too_close"
    end
    self.points[#self.points + 1] = point
    self.last_point = point
    while #self.points > self.max_points do table.remove(self.points, 1) end
    self:emit(string.format(
        "record point #%d source=%s pos=(%.3f,%.3f)",
        #self.points,
        tostring(point.source),
        point.x,
        point.y
    ))
    return true, point
end

function PlatformRecorder:start()
    self.recording = true
    self.last_sample_ms = 0
    self:add_point("start", true)
    self:emit("recording started")
end

function PlatformRecorder:pause()
    if self.recording then self:add_point("pause", true) end
    self.recording = false
    self:emit("recording paused")
end

function PlatformRecorder:clear()
    self.recording = false
    self.points = {}
    self.left_mark = nil
    self.right_mark = nil
    self.last_sample_ms = 0
    self.last_point = nil
    self:emit("recording cleared")
end

function PlatformRecorder:mark_left()
    local ok, point = self:add_point("left", true)
    if ok then
        self.left_mark = point
        self:emit(string.format("left boundary marked x=%.3f y=%.3f", point.x, point.y))
    end
end

function PlatformRecorder:mark_right()
    local ok, point = self:add_point("right", true)
    if ok then
        self.right_mark = point
        self:emit(string.format("right boundary marked x=%.3f y=%.3f", point.x, point.y))
    end
end

function PlatformRecorder:sample_if_due()
    if not self.recording then return false end
    local now = self.now_ms()
    if now - self.last_sample_ms < self.sample_ms then return false end
    self.last_sample_ms = now
    return self:add_point("sample", false)
end

function PlatformRecorder:bounds()
    local left_x = self.left_mark and self.left_mark.x
    local right_x = self.right_mark and self.right_mark.x
    for _, point in ipairs(self.points) do
        if not left_x or point.x < left_x then left_x = point.x end
        if not right_x or point.x > right_x then right_x = point.x end
    end
    return left_x or 0, right_x or 0
end

function PlatformRecorder:snapshot()
    local left_x, right_x = self:bounds()
    return {
        map_id = self.map_id,
        map_name = self.map_name,
        generated_at = os and os.date and os.date("!%Y-%m-%dT%H:%M:%SZ") or "",
        platform = {
            id = self.platform_id,
            left_x = left_x,
            right_x = right_x,
            safe_margin = self.safe_margin,
            points = self.points
        }
    }
end

function PlatformRecorder:save(path)
    path = path or self.save_path
    if not path or path == "" then return false, "save_path_missing" end
    if #self.points == 0 then return false, "no_points" end
    self.recording = false
    ensure_dir(path)
    local f, err = io.open(path, "w")
    if not f then return false, tostring(err) end
    f:write(encode_platform(self:snapshot()))
    f:write("\n")
    f:close()
    self.last_save_path = path
    self:emit(string.format("recording saved path=%s points=%d", path, #self.points))
    return true, path
end

function PlatformRecorder:handle_action(action)
    if action == "start" then return self:start() end
    if action == "pause" then return self:pause() end
    if action == "save" then return self:save() end
    if action == "clear" then return self:clear() end
    if action == "mark_left" then return self:mark_left() end
    if action == "mark_right" then return self:mark_right() end
end

function PlatformRecorder:poll_hotkeys(is_pressed)
    local actions = {}
    if type(is_pressed) ~= "function" then return actions end
    for _, item in ipairs(sorted_hotkeys(self.hotkeys)) do
        local down = is_pressed(item.key) == true
        local was_down = self.key_state[item.key] == true
        self.key_state[item.key] = down
        if down and not was_down then
            actions[#actions + 1] = item.action
            self:handle_action(item.action)
        end
    end
    self:sample_if_due()
    return actions
end

return PlatformRecorder
