local FoundationProbe = {}

local function output(message)
    message = "[foundation_probe] " .. tostring(message or "")
    if log and log.info then log.info(message) else print(message) end
end

local function now_ms()
    if os and os.clock then return os.clock() * 1000 end
    return 0
end

local function count(value)
    if type(value) ~= "table" then return 0 end
    if type(value.mobs) == "table" then return #value.mobs end
    if type(value.drops) == "table" then return #value.drops end
    if type(value.npcs) == "table" then return #value.npcs end
    if type(value.portals) == "table" then return #value.portals end
    if type(value.players) == "table" then return #value.players end
    if type(value.items) == "table" then return #value.items end
    if type(value.skills) == "table" then return #value.skills end
    if type(value.slots) == "table" then return #value.slots end
    return #value
end

local function sorted_keys(value, limit)
    if type(value) ~= "table" then return "" end
    local keys = {}
    for key in pairs(value) do keys[#keys + 1] = tostring(key) end
    table.sort(keys)
    limit = tonumber(limit) or #keys
    local out = {}
    for i = 1, math.min(#keys, limit) do out[#out + 1] = keys[i] end
    if #keys > limit then out[#out + 1] = "..." end
    return table.concat(out, ",")
end

local function primitive(value)
    local t = type(value)
    if t == "nil" then return "nil" end
    if t == "string" then return string.format("%q", value) end
    if t == "number" or t == "boolean" then return tostring(value) end
    return "<" .. t .. ">"
end

local function compact(value, depth, limit)
    depth = tonumber(depth) or 0
    limit = tonumber(limit) or 8
    if type(value) ~= "table" then return primitive(value) end
    if depth <= 0 then
        return string.format("{count=%d keys=%s}", count(value), sorted_keys(value, limit))
    end
    local parts = {}
    local n = 0
    for key, item in pairs(value) do
        n = n + 1
        if n > limit then
            parts[#parts + 1] = "..."
            break
        end
        parts[#parts + 1] = tostring(key) .. "=" .. compact(item, depth - 1, limit)
    end
    table.sort(parts)
    return "{" .. table.concat(parts, ",") .. "}"
end

local function safe_call(data, name, ...)
    local fn = data and data[name]
    if type(fn) ~= "function" then
        output("case_call missing function=" .. tostring(name))
        return false, "api_missing"
    end
    local started = now_ms()
    local values = nil
    local ok, err = pcall(function(...) values = { fn(...) } end, ...)
    local elapsed = now_ms() - started
    if not ok then
        output(string.format("case_call fail function=%s elapsed=%.3fms error=%s", tostring(name), elapsed, tostring(err)))
        return false, err
    end
    local first = values and values[1]
    output(string.format(
        "case_call ok function=%s elapsed=%.3fms type=%s count=%d keys=%s",
        tostring(name),
        elapsed,
        type(first),
        count(first),
        sorted_keys(first, 16)
    ))
    output("case_result function=" .. tostring(name) .. " value=" .. compact(first, 2, 12))
    return true, first, values
end

local function parse_range(text)
    local s = tostring(text or "")
    local a, b, c = s:match("^%s*([^|,]+)[|,]([^|,]+)[|,]([^|,]+)%s*$")
    return tonumber(a) or -20, tonumber(b) or 20, tonumber(c) or 1
end

local function report_nearby(value)
    value = type(value) == "table" and value or {}
    output(string.format(
        "nearby_summary mobs=%d drops=%d npcs=%d portals=%d players=%d",
        #(value.mobs or {}),
        #(value.drops or {}),
        #(value.npcs or {}),
        #(value.portals or {}),
        #(value.players or {})
    ))
end

local function report_pathfind(value)
    value = type(value) == "table" and value or {}
    local player = value.player or {}
    local map = value.map or {}
    output(string.format(
        "pathfind_summary map=%s player=(%s,%s) portals=%d climbables=%d footholds=%d nearest_npc=%s",
        tostring(map.name or map.id or ""),
        tostring(player.x or ""),
        tostring(player.y or ""),
        #(value.portals or {}),
        #(value.climbables or {}),
        #(value.footholds or {}),
        tostring(value.npc and (value.npc.name or value.npc.code) or "")
    ))
end

local function run_direct_case(data, case, opts)
    local cases = {
        connect_only = function() return true, "connected" end,
        player_info = function() return safe_call(data, "player_info") end,
        list_inventory = function() return safe_call(data, "list_inventory") end,
        list_skills = function() return safe_call(data, "list_skills") end,
        list_quickslot = function() return safe_call(data, "list_quickslot") end,
        list_nearby = function()
            local ok, value = safe_call(data, "list_nearby")
            if ok then report_nearby(value) end
            return ok, value
        end,
        list_portals = function() return safe_call(data, "list_portals") end,
        list_characters = function() return safe_call(data, "list_characters") end,
        probe_systems = function() return safe_call(data, "probe_systems") end,
        probe_action_state = function() return safe_call(data, "probe_action_state") end,
        action_selftest = function() return safe_call(data, "action_selftest") end,
        probe_pathfind = function()
            local ok, value = safe_call(data, "probe_pathfind")
            if ok then report_pathfind(value) end
            return ok, value
        end,
        probe_ground = function()
            local sx, ex, st = parse_range(opts.probe_ground_range)
            output(string.format("ground_range start=%.3f stop=%.3f step=%.3f", sx, ex, st))
            return safe_call(data, "probe_ground", sx, ex, st)
        end,
        teleport_probe = function() return safe_call(data, "teleport_probe") end,
        teleport_to_position = function()
            return safe_call(data, "teleport_to_position", opts.probe_map_id or "", opts.probe_x or "", opts.probe_y or "", opts.probe_force ~= false)
        end,
        teleport_to_spawn = function()
            return safe_call(data, "teleport_to_spawn", opts.probe_map_id or "", opts.probe_force ~= false)
        end,
        teleport_to_portal = function()
            return safe_call(data, "teleport_to_portal", opts.probe_map_id or "", opts.probe_portal or "sp", opts.probe_force ~= false)
        end,
        npc_probe = function() return safe_call(data, "npc_probe") end,
        npc_chat_nearest = function() return safe_call(data, "npc_chat", "") end,
        npc_chat_code = function() return safe_call(data, "npc_chat", opts.probe_npc_code or "") end,
        npc_special_act = function() return safe_call(data, "npc_special_act", opts.probe_npc_code or "", opts.probe_npc_action or "talk") end,
        shop_panel_probe = function() return safe_call(data, "shop_panel_probe") end,
        shop_probe = function() return safe_call(data, "shop_probe", opts.probe_npc_code or "", opts.probe_shop_key or "") end,
        shop_open = function() return safe_call(data, "shop_open", opts.probe_npc_code or "", opts.probe_shop_key or "") end,
        dialogue_probe = function() return safe_call(data, "dialogue_probe") end,
        dialogue_options_probe = function() return safe_call(data, "dialogue_options_probe") end,
        dialogue_button = function() return safe_call(data, "dialogue_button", opts.probe_dialogue_button or "ok", opts.probe_dialogue_kind or "all") end,
        dialogue_select = function() return safe_call(data, "dialogue_select", opts.probe_dialogue_value or "0", opts.probe_dialogue_index or "0", opts.probe_dialogue_kind or "all") end,
        dialogue_close = function() return safe_call(data, "dialogue_close") end,
        do_attack = function() return safe_call(data, "do_attack") end,
        pick_all = function() return safe_call(data, "pick_all") end,
        walk_left = function() return safe_call(data, "walk", -1, 0) end,
        walk_right = function() return safe_call(data, "walk", 1, 0) end,
        walk_stop = function() return safe_call(data, "walk", 0, 0) end,
        set_invincible_on = function() return safe_call(data, "set_invincible", true) end,
        set_invincible_off = function() return safe_call(data, "set_invincible", false) end,
        set_hp_lock_on = function() return safe_call(data, "set_hp_lock", true) end,
        set_hp_lock_off = function() return safe_call(data, "set_hp_lock", false) end,
        set_no_knockback_on = function() return safe_call(data, "set_no_knockback", true) end,
        set_no_knockback_off = function() return safe_call(data, "set_no_knockback", false) end,
        float_on = function() return safe_call(data, "float", true) end,
        float_off = function() return safe_call(data, "float", false) end,
        admin_move_on = function() return safe_call(data, "admin_move", true) end,
        admin_move_off = function() return safe_call(data, "admin_move", false) end,
        action_maintain = function() return safe_call(data, "action_maintain") end
    }

    local fn = cases[case]
    if not fn then
        output("unknown_case case=" .. tostring(case))
        return false, "unknown_case"
    end
    return fn()
end

function FoundationProbe.run(opts)
    opts = opts or {}
    local case = tostring(opts.probe_case or "connect_only")
    output("started case=" .. case)

    local ok_data, data_or_err = pcall(require, opts.probe_data_module or "data")
    if not ok_data then
        output("require data failed error=" .. tostring(data_or_err))
        return { ok = false, reason = "data_module_unavailable", error = tostring(data_or_err), case = case }
    end

    local connected, connect_result, connect_values = safe_call(data_or_err, "connect", opts.probe_target_name or "msw.exe", opts.probe_license_key)
    if not connected then
        return { ok = false, reason = "connect_failed", error = tostring(connect_result), case = case }
    end
    output("connect_values=" .. compact(connect_values, 1, 8))

    local ok, value, values = run_direct_case(data_or_err, case, opts)
    output("finished case=" .. case .. " ok=" .. tostring(ok))
    return { ok = ok == true, case = case, value = value, values = values }
end

return FoundationProbe
