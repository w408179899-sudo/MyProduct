local Blackboard = require("maple.blackboard")
local Config = require("maple.config")
local CombatRuntime = require("maple.combat.runtime")
local PlatformCombatRuntime = require("maple.combat.platform_runtime")
local Logger = require("maple.systems.logger")
local MapleApi = require("maple.environment.maple_api")
local MapleEnvironment = require("maple.environment.maple_environment")
local Normalize = require("maple.environment.normalizers")
local PlatformMap = require("maple.navigation.platform_map")

local Probe = {}

local function write_line(message)
    if log and log.info then log.info(message) else print(message) end
end

local function value(result)
    return result and result.data and result.data.value
end

local function diagnostic(result)
    return result and result.data and result.data.diagnostic or result and result.data
end

local function bool_text(value)
    return value and "true" or "false"
end

local function ok_text(result)
    if result and result.ok == true then return "ok" end
    return "fail:" .. tostring(result and result.reason or "unknown")
end

local function emit_result(output, name, result)
    local diag = diagnostic(result) or {}
    output(string.format(
        "[%s] %s elapsed=%.3fms count=%s",
        name,
        ok_text(result),
        tonumber(diag.elapsed_ms) or 0,
        tostring(diag.result_count or 0)
    ))
end

local function sorted_keys(tbl, max_keys)
    if type(tbl) ~= "table" then return "" end
    local keys = {}
    for k, _ in pairs(tbl) do keys[#keys + 1] = tostring(k) end
    table.sort(keys)
    if max_keys and #keys > max_keys then
        local trimmed = {}
        for i = 1, max_keys do trimmed[#trimmed + 1] = keys[i] end
        trimmed[#trimmed + 1] = "..."
        keys = trimmed
    end
    return table.concat(keys, ",")
end

local function total_keys(tbl)
    if type(tbl) ~= "table" then return 0 end
    local count = 0
    for _, _ in pairs(tbl) do count = count + 1 end
    return count
end

local function compact_value(value, depth)
    depth = depth or 0
    local value_type = type(value)
    if value_type == "string" then return string.format("%q", value) end
    if value_type == "number" or value_type == "boolean" or value_type == "nil" then return tostring(value) end
    if value_type ~= "table" then return string.format("%q", tostring(value)) end
    if depth >= 2 then return "{...}" end

    local keys = {}
    for k, _ in pairs(value) do keys[#keys + 1] = k end
    table.sort(keys, function(a, b) return tostring(a) < tostring(b) end)

    local parts = {}
    local max_fields = 12
    for i = 1, #keys do
        if i > max_fields then
            parts[#parts + 1] = "..."
            break
        end
        local k = keys[i]
        parts[#parts + 1] = tostring(k) .. "=" .. compact_value(value[k], depth + 1)
    end
    return "{" .. table.concat(parts, ",") .. "}"
end

local function emit_raw_table(output, label, tbl)
    if type(tbl) ~= "table" then
        output(string.format("%s type=%s value=%s", label, type(tbl), compact_value(tbl)))
        return
    end
    output(string.format(
        "%s type=table array_count=%d total_keys=%d keys=%s",
        label,
        #tbl,
        total_keys(tbl),
        sorted_keys(tbl, 20)
    ))
end

local function emit_raw_sample(output, label, tbl)
    if type(tbl) ~= "table" then return end
    output(string.format("%s sample=%s", label, compact_value(tbl)))
end

local function emit_raw_list(output, label, list, max_items)
    max_items = tonumber(max_items) or 3
    if type(list) ~= "table" then
        output(string.format("%s type=%s value=%s", label, type(list), compact_value(list)))
        return
    end

    output(string.format(
        "%s type=table array_count=%d total_keys=%d keys=%s",
        label,
        #list,
        total_keys(list),
        sorted_keys(list, 20)
    ))

    local limit = math.min(#list, max_items)
    for i = 1, limit do
        output(string.format("%s[%d]=%s", label, i, compact_value(list[i])))
    end
    if #list > limit then
        output(string.format("%s omitted=%d", label, #list - limit))
    end
end

local function emit_raw_snapshot(output, raw, sample_count)
    sample_count = tonumber(sample_count) or 3

    emit_raw_table(output, "raw.player_info", value(raw.player_info))
    emit_raw_sample(output, "raw.player_info", value(raw.player_info))

    local nearby = value(raw.list_nearby)
    emit_raw_table(output, "raw.list_nearby", nearby)
    if type(nearby) == "table" then
        emit_raw_list(output, "raw.list_nearby.mobs", nearby.mobs, sample_count)
        emit_raw_list(output, "raw.list_nearby.drops", nearby.drops, sample_count)
        emit_raw_list(output, "raw.list_nearby.npcs", nearby.npcs, sample_count)
        emit_raw_list(output, "raw.list_nearby.portals", nearby.portals, sample_count)
    end

    local inventory = value(raw.list_inventory)
    emit_raw_table(output, "raw.list_inventory", inventory)
    if type(inventory) == "table" then emit_raw_list(output, "raw.list_inventory.items", inventory.items, sample_count) end

    local skills = value(raw.list_skills)
    emit_raw_table(output, "raw.list_skills", skills)
    if type(skills) == "table" then emit_raw_list(output, "raw.list_skills.skills", skills.skills, sample_count) end

    local quickslots = value(raw.list_quickslot)
    emit_raw_table(output, "raw.list_quickslot", quickslots)
    if type(quickslots) == "table" and quickslots.slots ~= nil then
        emit_raw_list(output, "raw.list_quickslot.slots", quickslots.slots, sample_count)
    else
        emit_raw_list(output, "raw.list_quickslot", quickslots, sample_count)
    end
end

local function actor_summary(actor)
    actor = actor or {}
    local pos = actor.position or {}
    return string.format(
        "actor level=%s hp=%s/%s mp=%s/%s map=%s pos=(%.2f,%.2f) invincible=%s",
        tostring(actor.level),
        tostring(actor.hp),
        tostring(actor.max_hp),
        tostring(actor.mp),
        tostring(actor.max_mp),
        tostring(actor.map_id or actor.current_map),
        tonumber(pos.x) or 0,
        tonumber(pos.y) or 0,
        bool_text(actor.invincible)
    )
end

local function world_summary(world)
    world = world or {}
    return string.format(
        "world mobs=%d drops=%d npcs=%d portals=%d",
        #(world.nearby_targets or {}),
        #(world.nearby_resources or {}),
        #(world.nearby_npcs or {}),
        #(world.nearby_portals or {})
    )
end

local function skill_summary(skill)
    skill = skill or {}
    return string.format(
        "skill learned=%d quickslots=%d point=%s used=%s",
        #(skill.available or {}),
        #(skill.quickslots or {}),
        tostring(skill.point or 0),
        tostring(skill.used or 0)
    )
end

local function inventory_summary(inventory)
    inventory = inventory or {}
    return string.format(
        "inventory items=%d meso=%s",
        #(inventory.items or {}),
        tostring(inventory.meso or 0)
    )
end

local function new_context(opts)
    opts = opts or {}
    local output = opts.output or write_line
    local logger = opts.logger or Logger.new("maple_probe", {
        level = "debug",
        print_to_console = false,
        keep_records = 100
    })
    local bb = Blackboard.new({ account_index = opts.account_index })
    local api = MapleApi.new({
        data_module = opts.data_module,
        module_name = opts.module_name or "data",
        logger = logger,
        account_index = opts.account_index
    })
    local env = MapleEnvironment.new({
        api = api,
        logger = logger,
        account_index = opts.account_index,
        target_name = opts.target_name,
        license_key = opts.license_key,
        key_api = opts.key_api,
        wnd_api = opts.wnd_api,
        proc_api = opts.proc_api,
        input_mode = opts.input_mode,
        key_mode = opts.key_mode,
        allow_mock_fallback = false
    })
    return {
        output = output,
        logger = logger,
        bb = bb,
        api = api,
        env = env,
        opts = opts
    }
end

local function connect(ctx)
    local result = ctx.env:bind_client({
        params = {
            target_name = ctx.opts.target_name,
            license_key = ctx.opts.license_key
        }
    }, ctx.bb)
    emit_result(ctx.output, "connect", result)
    return result
end

function Probe.readonly(opts)
    local ctx = new_context(opts)
    ctx.output("Maple readonly probe started")

    local connected = connect(ctx)
    if not connected.ok then
        return {
            ok = false,
            reason = connected.reason,
            bb = ctx.bb,
            diagnostics = ctx.api.last_calls
        }
    end

    local raw = {
        player_info = ctx.api:call("player_info", ctx.bb),
        list_nearby = ctx.api:call("list_nearby", ctx.bb),
        list_inventory = ctx.api:call("list_inventory", ctx.bb),
        list_skills = ctx.api:call("list_skills", ctx.bb),
        list_quickslot = ctx.api:call("list_quickslot", ctx.bb)
    }
    for name, result in pairs(raw) do emit_result(ctx.output, name, result) end

    local normalized = {
        actor = Normalize.actor(value(raw.player_info), diagnostic(raw.player_info)),
        world = Normalize.world(value(raw.list_nearby), diagnostic(raw.list_nearby)),
        inventory = Normalize.inventory(value(raw.list_inventory), diagnostic(raw.list_inventory)),
        skill = Normalize.skill(value(raw.list_skills), value(raw.list_quickslot), diagnostic(raw.list_skills), diagnostic(raw.list_quickslot))
    }

    ctx.output(actor_summary(normalized.actor))
    ctx.output(world_summary(normalized.world))
    ctx.output(skill_summary(normalized.skill))
    ctx.output(inventory_summary(normalized.inventory))
    ctx.output("Maple readonly probe finished")

    return {
        ok = true,
        bb = ctx.bb,
        raw = {
            player_info = value(raw.player_info),
            list_nearby = value(raw.list_nearby),
            list_inventory = value(raw.list_inventory),
            list_skills = value(raw.list_skills),
            list_quickslot = value(raw.list_quickslot)
        },
        normalized = normalized,
        diagnostics = ctx.api.last_calls
    }
end

function Probe.snapshot(opts)
    local ctx = new_context(opts)
    ctx.output("Maple raw snapshot probe started")

    local connected = connect(ctx)
    if not connected.ok then
        return {
            ok = false,
            reason = connected.reason,
            bb = ctx.bb,
            diagnostics = ctx.api.last_calls
        }
    end

    local raw = {
        player_info = ctx.api:call("player_info", ctx.bb),
        list_nearby = ctx.api:call("list_nearby", ctx.bb),
        list_inventory = ctx.api:call("list_inventory", ctx.bb),
        list_skills = ctx.api:call("list_skills", ctx.bb),
        list_quickslot = ctx.api:call("list_quickslot", ctx.bb)
    }
    for name, result in pairs(raw) do emit_result(ctx.output, name, result) end

    emit_raw_snapshot(ctx.output, raw, ctx.opts.sample_count)
    ctx.output("Maple raw snapshot probe finished")

    return {
        ok = true,
        bb = ctx.bb,
        raw = {
            player_info = value(raw.player_info),
            list_nearby = value(raw.list_nearby),
            list_inventory = value(raw.list_inventory),
            list_skills = value(raw.list_skills),
            list_quickslot = value(raw.list_quickslot)
        },
        diagnostics = ctx.api.last_calls
    }
end

local function run_action(ctx, name, params)
    local result = ctx.env:perform_action({ name = name, params = params or {} }, ctx.bb)
    emit_result(ctx.output, name, result)
    return result
end

local function action_raw_text(result)
    local data = result and result.data or {}
    if data.raw ~= nil then return compact_value(data.raw) end
    if data.value ~= nil then return compact_value(data.value) end
    return ""
end

local function claimed_pick_count(results)
    local total = 0
    for _, result in ipairs(results or {}) do
        local raw = action_raw_text(result)
        local count = raw:match("picked=(%d+)")
        if count then total = total + (tonumber(count) or 0) end
    end
    return total
end

local function key_result_text(result)
    local data = result and result.data or {}
    local hwnd = data.hwnd and string.format("0x%X", data.hwnd) or "nil"
    return string.format(
        "ok=%s reason=%s method=%s hwnd=%s raw=%s",
        tostring(result and result.ok == true),
        tostring(result and result.reason or ""),
        tostring(data.method or ""),
        hwnd,
        data.raw ~= nil and compact_value(data.raw) or ""
    )
end

local function sleep_ms(ms)
    if sys and sys.sleep then sys.sleep(tonumber(ms) or 0) end
end

local function quickslot_list(raw)
    if type(raw) ~= "table" then return {} end
    if type(raw.slots) == "table" then return raw.slots end
    return raw
end

local function find_quickslot(raw, slot_number)
    for _, slot in ipairs(quickslot_list(raw)) do
        if tonumber(slot.slot) == tonumber(slot_number) then return slot end
    end
    return nil
end

local function find_skill_name(raw_skills, skill_id)
    local numeric_id = tonumber(skill_id)
    if not numeric_id then return "" end
    raw_skills = raw_skills or {}
    for _, skill in ipairs(raw_skills.skills or {}) do
        if tonumber(skill.Code) == numeric_id then return tostring(skill.name or "") end
    end
    return ""
end

local function read_effect_state(ctx)
    local actor_result = ctx.api:call("player_info", ctx.bb)
    emit_result(ctx.output, "player_info", actor_result)
    local world_result = ctx.api:call("list_nearby", ctx.bb)
    emit_result(ctx.output, "list_nearby", world_result)

    local actor = Normalize.actor(value(actor_result), diagnostic(actor_result))
    local world = Normalize.world(value(world_result), diagnostic(world_result))
    return {
        actor = actor,
        world = world,
        actor_result = actor_result,
        world_result = world_result
    }
end

local function emit_effect_state(output, label, state)
    state = state or {}
    local actor = state.actor or {}
    local world = state.world or {}
    output(string.format(
        "%s hp=%s/%s mp=%s/%s mobs=%d drops=%d",
        label,
        tostring(actor.hp),
        tostring(actor.max_hp),
        tostring(actor.mp),
        tostring(actor.max_mp),
        #(world.nearby_targets or {}),
        #(world.nearby_resources or {})
    ))
end

local function emit_effect_delta(output, before, after)
    local before_actor = before and before.actor or {}
    local after_actor = after and after.actor or {}
    local before_world = before and before.world or {}
    local after_world = after and after.world or {}
    output(string.format(
        "effect delta hp=%s mp=%s mobs=%d drops=%d",
        tostring((after_actor.hp or 0) - (before_actor.hp or 0)),
        tostring((after_actor.mp or 0) - (before_actor.mp or 0)),
        #((after_world.nearby_targets or {})) - #((before_world.nearby_targets or {})),
        #((after_world.nearby_resources or {})) - #((before_world.nearby_resources or {}))
    ))
end

local function parse_wait_schedule(value)
    local waits = {}
    if type(value) == "table" then
        for _, item in ipairs(value) do
            waits[#waits + 1] = math.max(0, tonumber(item) or 0)
        end
    elseif type(value) == "string" then
        for item in value:gmatch("%d+") do
            waits[#waits + 1] = math.max(0, tonumber(item) or 0)
        end
    end
    if #waits <= 0 then waits = { 0, 200, 500, 1000, 2000 } end
    table.sort(waits)
    return waits
end

local function drop_key(drop)
    drop = drop or {}
    if drop.id ~= nil and tostring(drop.id) ~= "" then
        return table.concat({
            tostring(drop.id or ""),
            tostring(drop.item_id or ""),
            tostring(drop.name or "")
        }, "|")
    end
    return table.concat({
        tostring(drop.item_id or ""),
        tostring(drop.name or ""),
        string.format("%.3f", tonumber(drop.x) or 0),
        string.format("%.3f", tonumber(drop.y) or 0)
    }, "|")
end

local function item_code_key(item)
    return tostring(item and item.code or "")
end

local function count_map(list, key_fn)
    local map = {}
    for _, item in ipairs(list or {}) do
        local key = key_fn(item)
        if key ~= "" then map[key] = (map[key] or 0) + 1 end
    end
    return map
end

local function inventory_code_counts(inventory)
    local map = {}
    local total_count = 0
    for _, item in ipairs((inventory or {}).items or {}) do
        local key = item_code_key(item)
        local count = tonumber(item.count) or 0
        total_count = total_count + count
        if key ~= "" then map[key] = (map[key] or 0) + count end
    end
    return map, total_count
end

local function diff_count_maps(before_map, after_map)
    local disappeared = 0
    local unchanged = 0
    local appeared = 0
    local disappeared_keys = {}
    local unchanged_keys = {}
    local appeared_keys = {}
    local seen = {}

    for key, before_count in pairs(before_map or {}) do
        local after_count = (after_map or {})[key] or 0
        local same = math.min(before_count, after_count)
        local gone = math.max(0, before_count - after_count)
        unchanged = unchanged + same
        disappeared = disappeared + gone
        if same > 0 then unchanged_keys[#unchanged_keys + 1] = key .. " x" .. tostring(same) end
        if gone > 0 then disappeared_keys[#disappeared_keys + 1] = key .. " x" .. tostring(gone) end
        seen[key] = true
    end

    for key, after_count in pairs(after_map or {}) do
        if not seen[key] then
            appeared = appeared + after_count
            appeared_keys[#appeared_keys + 1] = key .. " x" .. tostring(after_count)
        end
    end

    table.sort(disappeared_keys)
    table.sort(unchanged_keys)
    table.sort(appeared_keys)
    return {
        disappeared = disappeared,
        unchanged = unchanged,
        appeared = appeared,
        disappeared_keys = disappeared_keys,
        unchanged_keys = unchanged_keys,
        appeared_keys = appeared_keys
    }
end

local function first_items(items, limit)
    local parts = {}
    limit = tonumber(limit) or 5
    for i = 1, math.min(#(items or {}), limit) do
        parts[#parts + 1] = tostring(items[i])
    end
    if #(items or {}) > limit then parts[#parts + 1] = "...+" .. tostring(#items - limit) end
    return table.concat(parts, "; ")
end

local function pickup_verify_drop_text(drop)
    drop = drop or {}
    return string.format(
        "%s item=%s name=%s pos=(%.3f,%.3f) can_pick=%s source=%s free=%s",
        tostring(drop.id),
        tostring(drop.item_id),
        tostring(drop.name),
        tonumber(drop.x) or 0,
        tonumber(drop.y) or 0,
        tostring(drop.can_pick),
        tostring(drop.drop_source or drop.owner_cid or ""),
        tostring(drop.free)
    )
end

local function emit_pickup_verify_drops(output, label, drops, max_items)
    max_items = tonumber(max_items) or 8
    output(string.format("%s drops=%d", label, #(drops or {})))
    for i = 1, math.min(#(drops or {}), max_items) do
        output(string.format("%s drop[%d]=%s", label, i, pickup_verify_drop_text(drops[i])))
    end
    if #(drops or {}) > max_items then
        output(string.format("%s drops_omitted=%d", label, #drops - max_items))
    end
end

local function read_pickup_verify_snapshot(ctx, label, max_drop_log)
    local actor_result = ctx.api:call("player_info", ctx.bb)
    emit_result(ctx.output, "player_info", actor_result)
    local world_result = ctx.api:call("list_nearby", ctx.bb)
    emit_result(ctx.output, "list_nearby", world_result)
    local inventory_result = ctx.api:call("list_inventory", ctx.bb)
    emit_result(ctx.output, "list_inventory", inventory_result)

    local actor = Normalize.actor(value(actor_result), diagnostic(actor_result))
    local world = Normalize.world(value(world_result), diagnostic(world_result))
    local inventory = Normalize.inventory(value(inventory_result), diagnostic(inventory_result))
    local code_counts, item_total_count = inventory_code_counts(inventory)
    local drops = world.nearby_resources or {}
    local raw_world = value(world_result) or {}
    local snapshot = {
        label = label,
        actor = actor,
        world = world,
        inventory = inventory,
        drops = drops,
        drop_map = count_map(drops, drop_key),
        inventory_code_counts = code_counts,
        inventory_item_total_count = item_total_count,
        raw_drop_count = raw_world.dropCount
    }

    ctx.output(string.format(
        "pickup_verify snapshot=%s actor_pos=(%.3f,%.3f) hp=%s/%s mp=%s/%s raw_drop_count=%s normalized_drops=%d meso=%s used_slots=%s item_total_count=%s",
        label,
        tonumber(actor.position and actor.position.x) or 0,
        tonumber(actor.position and actor.position.y) or 0,
        tostring(actor.hp),
        tostring(actor.max_hp),
        tostring(actor.mp),
        tostring(actor.max_mp),
        tostring(snapshot.raw_drop_count),
        #drops,
        tostring(inventory.meso),
        tostring(inventory.used_slots),
        tostring(item_total_count)
    ))
    emit_pickup_verify_drops(ctx.output, "pickup_verify " .. label, drops, max_drop_log)
    return snapshot
end

local function pickup_verify_compare(before, after)
    local drop_diff = diff_count_maps(before and before.drop_map or {}, after and after.drop_map or {})
    local before_inventory = before and before.inventory or {}
    local after_inventory = after and after.inventory or {}
    local code_diff = diff_count_maps(before and before.inventory_code_counts or {}, after and after.inventory_code_counts or {})
    return {
        before_drop_count = #((before and before.drops) or {}),
        after_drop_count = #((after and after.drops) or {}),
        disappeared_count = drop_diff.disappeared,
        unchanged_count = drop_diff.unchanged,
        appeared_count = drop_diff.appeared,
        disappeared_keys = drop_diff.disappeared_keys,
        unchanged_keys = drop_diff.unchanged_keys,
        appeared_keys = drop_diff.appeared_keys,
        meso_delta = (tonumber(after_inventory.meso) or 0) - (tonumber(before_inventory.meso) or 0),
        used_slots_delta = (tonumber(after_inventory.used_slots) or 0) - (tonumber(before_inventory.used_slots) or 0),
        item_total_delta = (tonumber(after and after.inventory_item_total_count) or 0) - (tonumber(before and before.inventory_item_total_count) or 0),
        code_added = code_diff.appeared,
        code_removed = code_diff.disappeared,
        code_unchanged = code_diff.unchanged
    }
end

local function emit_pickup_verify_compare(output, label, comparison, max_keys)
    output(string.format(
        "pickup_verify compare=%s before_drops=%d after_drops=%d disappeared=%d unchanged=%d appeared=%d meso_delta=%s used_slots_delta=%s item_total_delta=%s code_added=%d code_removed=%d",
        label,
        comparison.before_drop_count,
        comparison.after_drop_count,
        comparison.disappeared_count,
        comparison.unchanged_count,
        comparison.appeared_count,
        tostring(comparison.meso_delta),
        tostring(comparison.used_slots_delta),
        tostring(comparison.item_total_delta),
        comparison.code_added,
        comparison.code_removed
    ))
    if comparison.disappeared_count > 0 then
        output("pickup_verify disappeared_keys=" .. first_items(comparison.disappeared_keys, max_keys))
    end
    if comparison.unchanged_count > 0 then
        output("pickup_verify unchanged_keys=" .. first_items(comparison.unchanged_keys, max_keys))
    end
    if comparison.appeared_count > 0 then
        output("pickup_verify appeared_keys=" .. first_items(comparison.appeared_keys, max_keys))
    end
end

local function clone_combat_config(opts)
    local cfg = {}
    for k, v in pairs(Config.combat or {}) do cfg[k] = v end
    opts = opts or {}
    local overrides = {
        "baseline_run_seconds",
        "baseline_max_ticks",
        "baseline_tick_ms",
        "baseline_move_ms",
        "baseline_attack_wait_ms",
        "baseline_pick_wait_ms",
        "baseline_attack_range_x",
        "baseline_attack_range_y",
        "baseline_stop_range_x",
        "baseline_pursuit_y_tolerance",
        "baseline_pickup_enabled",
        "skill_key",
        "skill_key_code",
        "skill_input_mode",
        "skill_hold_ms",
        "key_mode",
        "fallback_to_basic_attack"
    }
    for _, key in ipairs(overrides) do
        if opts[key] ~= nil then cfg[key] = opts[key] end
    end
    if opts.key_code ~= nil then cfg.skill_key_code = opts.key_code end
    if opts.key_name ~= nil then cfg.skill_key = opts.key_name end
    if opts.input_mode ~= nil then cfg.skill_input_mode = opts.input_mode end
    if opts.hold_ms ~= nil then cfg.skill_hold_ms = opts.hold_ms end
    return cfg
end

local function clone_platform_combat_config(opts)
    local cfg = clone_combat_config(opts)
    opts = opts or {}
    local defaults = {
        platform_y_tolerance = 1.0,
        actor_platform_y_tolerance = 0.6,
        pickup_platform_y_tolerance = 1.5,
        platform_x_margin = 0.2,
        grounded_y_tolerance = 0.2,
        skill_range_x = 2.0,
        skill_range_y = 0.3,
        preferred_attack_distance = 1.4,
        arrival_tolerance_x = 0.18,
        platform_safe_margin = 0.5,
        cast_delay_seconds = 0.7,
        tick_ms = 120,
        move_ms = 180,
        pickup_move_ms = 360,
        pickup_move_method = "key",
        face_ms = 80,
        attack_wait_ms = 750,
        pick_wait_ms = 250,
        pickup_pick_repeat = 1,
        pickup_pick_repeat_ms = 100,
        pickup_drop_fail_threshold = 1,
        pickup_drop_ignore_ticks = 60,
        pickup_drop_ignore_cluster_x = 0.75,
        pickup_drop_ignore_cluster_y = 1.0,
        pickup_key_enabled = true,
        pickup_key_name = "Z",
        pickup_key_code = 0x5A,
        pickup_key_repeat = 3,
        pickup_key_repeat_ms = 80,
        pickup_key_hold_ms = 80,
        pickup_range_x = 0.65,
        pickup_range_y = 0.5,
        pickup_ignore_raw_y = true,
        pickup_enabled = true,
        pickup_during_combat_enabled = true,
        pickup_during_combat_nearby_range_x = 0.8,
        pickup_during_combat_max_detour_x = 1.5,
        pickup_age_priority_ticks = 80,
        pickup_include_all_drops = true,
        max_log_candidates = 5,
        clear_remaining_threshold = 0,
        pickup_empty_confirm_ticks = 5,
        pickup_sweep_enabled = false,
        pickup_sweep_step = 0.35,
        pickup_sweep_arrival_x = 0.15,
        pickup_sweep_safe_margin = 0.1,
        pickup_sweep_max_ticks = 600,
        move_method = "key",
        move_left_key_code = 0x25,
        move_right_key_code = 0x27,
        face_method = "key",
        face_left_key_code = 0x25,
        face_right_key_code = 0x27
    }
    for key, value in pairs(defaults) do
        if cfg[key] == nil then cfg[key] = value end
    end

    local overrides = {
        "platform_y_tolerance",
        "actor_platform_y_tolerance",
        "pickup_platform_y_tolerance",
        "platform_x_margin",
        "grounded_y_tolerance",
        "skill_range_x",
        "skill_range_y",
        "preferred_attack_distance",
        "arrival_tolerance_x",
        "platform_safe_margin",
        "cast_delay_seconds",
        "tick_ms",
        "move_ms",
        "pickup_move_ms",
        "pickup_move_method",
        "face_ms",
        "attack_wait_ms",
        "pick_wait_ms",
        "pickup_pick_repeat",
        "pickup_pick_repeat_ms",
        "pickup_drop_fail_threshold",
        "pickup_drop_ignore_ticks",
        "pickup_drop_ignore_cluster_x",
        "pickup_drop_ignore_cluster_y",
        "pickup_key_enabled",
        "pickup_key_name",
        "pickup_key_code",
        "pickup_key_repeat",
        "pickup_key_repeat_ms",
        "pickup_key_hold_ms",
        "pickup_range_x",
        "pickup_range_y",
        "pickup_ignore_raw_y",
        "pickup_enabled",
        "pickup_include_all_drops",
        "max_log_candidates",
        "clear_remaining_threshold",
        "pickup_empty_confirm_ticks",
        "pickup_sweep_enabled",
        "pickup_sweep_step",
        "pickup_sweep_arrival_x",
        "pickup_sweep_safe_margin",
        "pickup_sweep_max_ticks",
        "move_method",
        "move_left_key_code",
        "move_right_key_code",
        "face_method",
        "face_left_key_code",
        "face_right_key_code"
    }
    for _, key in ipairs(overrides) do
        local opt_key = "platform_" .. key
        if opts[key] ~= nil then cfg[key] = opts[key] end
        if opts[opt_key] ~= nil then cfg[key] = opts[opt_key] end
    end

    if opts.probe_platform_y_tolerance ~= nil then cfg.platform_y_tolerance = opts.probe_platform_y_tolerance end
    if opts.probe_skill_range_x ~= nil then cfg.skill_range_x = opts.probe_skill_range_x end
    if opts.probe_skill_range_y ~= nil then cfg.skill_range_y = opts.probe_skill_range_y end
    if opts.probe_move_ms ~= nil then cfg.move_ms = opts.probe_move_ms end
    if opts.probe_attack_wait_ms ~= nil then cfg.attack_wait_ms = opts.probe_attack_wait_ms end
    return cfg
end

local function count_items(list)
    return #(list or {})
end

local function find_target_by_id(world, id)
    if not id then return nil end
    for _, target in ipairs((world and world.nearby_targets) or {}) do
        if tostring(target.id) == tostring(id) then return target end
    end
    return nil
end

local function actor_position_text(actor)
    local pos = actor and actor.position or {}
    return string.format("(%.2f,%.2f)", tonumber(pos.x) or 0, tonumber(pos.y) or 0)
end

local function target_text(target, metrics)
    if not target then return "none" end
    metrics = metrics or CombatRuntime.describe_target({}, target)
    return string.format(
        "id=%s name=%s pos=(%.2f,%.2f) dx=%.2f dy=%.2f abs=(%.2f,%.2f)",
        tostring(target.id),
        tostring(target.name),
        tonumber(metrics.x) or tonumber(target.x) or 0,
        tonumber(metrics.y) or tonumber(target.y) or 0,
        tonumber(metrics.dx) or 0,
        tonumber(metrics.dy) or 0,
        tonumber(metrics.abs_x) or 0,
        tonumber(metrics.abs_y) or 0
    )
end

local function action_summary(proposal)
    proposal = proposal or {}
    local params = proposal.params or {}
    return string.format(
        "action=%s reason=%s direction=%s key=%s/0x%X",
        tostring(proposal.action),
        tostring(proposal.reason),
        tostring(params.direction or ""),
        tostring(params.key_name or ""),
        tonumber(params.key_code) or 0
    )
end

local function read_combat_state(ctx, label)
    local state = read_effect_state(ctx)
    local actor = state.actor or {}
    local world = state.world or {}
    ctx.output(string.format(
        "%s summary actor_pos=%s hp=%s/%s mp=%s/%s mobs=%d drops=%d",
        label,
        actor_position_text(actor),
        tostring(actor.hp),
        tostring(actor.max_hp),
        tostring(actor.mp),
        tostring(actor.max_mp),
        count_items(world.nearby_targets),
        count_items(world.nearby_resources)
    ))
    return state
end

local function run_baseline_action(ctx, proposal, cfg, loop_state)
    proposal = proposal or { action = "Wait", params = {} }
    local action = proposal.action
    local params = proposal.params or {}
    local results = {}

    if action == "SetWalkDirection" then
        results[#results + 1] = run_action(ctx, "SetWalkDirection", params)
        loop_state.is_moving = results[#results].ok == true
        sleep_ms(tonumber(cfg.baseline_move_ms) or 220)
        results[#results + 1] = run_action(ctx, "StopMove", {})
        loop_state.is_moving = false
    elseif action == "StopMove" then
        results[#results + 1] = run_action(ctx, "StopMove", {})
        loop_state.is_moving = false
    elseif action == "PressKey" then
        results[#results + 1] = run_action(ctx, "PressKey", params)
        loop_state.just_attacked = results[#results].ok == true
        loop_state.is_moving = false
        if not results[#results].ok and cfg.fallback_to_basic_attack ~= false then
            ctx.output(string.format("fallback action=BasicAttack reason=%s", tostring(results[#results].reason or "press_key_failed")))
            results[#results + 1] = run_action(ctx, "BasicAttack", {})
        end
        sleep_ms(tonumber(cfg.baseline_attack_wait_ms) or 750)
    elseif action == "PickAllDrops" then
        results[#results + 1] = run_action(ctx, "PickAllDrops", {})
        loop_state.just_attacked = false
        sleep_ms(tonumber(cfg.baseline_pick_wait_ms) or 250)
    elseif action == "BasicAttack" then
        results[#results + 1] = run_action(ctx, "BasicAttack", {})
        loop_state.just_attacked = results[#results].ok == true
        sleep_ms(tonumber(cfg.baseline_attack_wait_ms) or 750)
    else
        loop_state.just_attacked = false
        sleep_ms(tonumber(params.seconds) and tonumber(params.seconds) * 1000 or tonumber(cfg.baseline_tick_ms) or 250)
    end

    local ok = true
    for _, result in ipairs(results) do
        if not result.ok then ok = false end
    end
    return ok, results
end

local function emit_after_delta(ctx, tick_index, before, after, proposal)
    local before_world = before and before.world or {}
    local after_world = after and after.world or {}
    local target = proposal and proposal.target
    local target_alive = target and find_target_by_id(after_world, target.id) ~= nil
    ctx.output(string.format(
        "tick=%d after_delta mobs=%d drops=%d target_alive=%s",
        tick_index,
        count_items(after_world.nearby_targets) - count_items(before_world.nearby_targets),
        count_items(after_world.nearby_resources) - count_items(before_world.nearby_resources),
        target and tostring(target_alive) or "n/a"
    ))
end

local function now_ms()
    if os and os.clock then return os.clock() * 1000 end
    return 0
end

local function drop_key(drop)
    drop = drop or {}
    local id = drop.id or drop.instance_id or drop.item_id or drop.source_index
    if id == nil then return "" end
    return tostring(id)
end

local function find_drop_by_id(world, id)
    if id == nil then return nil end
    local wanted = tostring(id)
    for _, drop in ipairs((world and world.nearby_resources) or {}) do
        if drop_key(drop) == wanted then return drop end
    end
    return nil
end

local function drop_position(drop)
    drop = drop or {}
    return drop.position or { x = drop.x, y = drop.y, z = drop.z }
end

local function expire_ignored_drops(ctx, tick_index, loop_state)
    local ignored = loop_state and loop_state.ignored_drops
    if type(ignored) ~= "table" then return end
    for id, entry in pairs(ignored) do
        local until_tick = type(entry) == "table" and tonumber(entry.until_tick) or tonumber(entry)
        if until_tick and tick_index >= until_tick then
            ignored[id] = nil
            ctx.output(string.format(
                "tick=%d pickup_drop_ignore_expired id=%s until_tick=%s",
                tick_index,
                tostring(id),
                tostring(until_tick)
            ))
        end
    end
end

local function update_drop_seen_ticks(ctx, tick_index, loop_state, world)
    if not loop_state then return end
    loop_state.drop_first_seen_ticks = loop_state.drop_first_seen_ticks or {}
    loop_state.drop_last_seen_ticks = loop_state.drop_last_seen_ticks or {}

    local seen = {}
    for _, drop in ipairs((world and world.nearby_resources) or {}) do
        local id = drop_key(drop)
        if id ~= "" then
            seen[id] = true
            if not loop_state.drop_first_seen_ticks[id] then
                loop_state.drop_first_seen_ticks[id] = tick_index
                ctx.output(string.format("tick=%d drop_seen_first id=%s", tick_index, tostring(id)))
            end
            loop_state.drop_last_seen_ticks[id] = tick_index
        end
    end

    for id, last_seen_tick in pairs(loop_state.drop_last_seen_ticks) do
        if not seen[id] and tick_index - (tonumber(last_seen_tick) or tick_index) > 3 then
            loop_state.drop_first_seen_ticks[id] = nil
            loop_state.drop_last_seen_ticks[id] = nil
            ctx.output(string.format("tick=%d drop_seen_expired id=%s", tick_index, tostring(id)))
        end
    end
end

local function ignore_pickup_drop(ctx, tick_index, loop_state, id, failures, ignore_ticks, reason, base_id)
    if id == nil or tostring(id) == "" then return false end
    id = tostring(id)
    loop_state.ignored_drops[id] = {
        until_tick = tick_index + ignore_ticks,
        failures = failures,
        reason = reason,
        base_id = base_id
    }
    loop_state.drop_pick_failures[id] = nil
    ctx.output(string.format(
        "tick=%d pickup_drop_ignore id=%s failures=%d ignore_ticks=%d until_tick=%d reason=%s base_id=%s",
        tick_index,
        id,
        failures,
        ignore_ticks,
        tick_index + ignore_ticks,
        tostring(reason or "single"),
        tostring(base_id or id)
    ))
    return true
end

local function ignore_pickup_cluster(ctx, tick_index, loop_state, cfg, base_drop, after_world, failures, ignore_ticks)
    local base_id = drop_key(base_drop)
    local base_pos = drop_position(base_drop)
    local base_x = tonumber(base_pos.x) or 0
    local base_y = tonumber(base_pos.y) or 0
    local radius_x = math.max(0, tonumber(cfg.pickup_drop_ignore_cluster_x) or 0)
    local radius_y = math.max(0, tonumber(cfg.pickup_drop_ignore_cluster_y) or 0)
    if radius_x <= 0 and radius_y <= 0 then return 0 end

    local ignored = 0
    for _, drop in ipairs((after_world and after_world.nearby_resources) or {}) do
        local id = drop_key(drop)
        if id ~= "" and id ~= base_id and not loop_state.ignored_drops[id] then
            local pos = drop_position(drop)
            local dx = math.abs((tonumber(pos.x) or 0) - base_x)
            local dy = math.abs((tonumber(pos.y) or 0) - base_y)
            local within_x = radius_x <= 0 or dx <= radius_x
            local within_y = radius_y <= 0 or dy <= radius_y
            if within_x and within_y then
                ignore_pickup_drop(ctx, tick_index, loop_state, id, failures, ignore_ticks, "cluster", base_id)
                ignored = ignored + 1
            end
        end
    end
    if ignored > 0 then
        ctx.output(string.format(
            "tick=%d pickup_drop_ignore_cluster base_id=%s count=%d radius=(%.3f,%.3f)",
            tick_index,
            tostring(base_id),
            ignored,
            radius_x,
            radius_y
        ))
    end
    return ignored
end

local function record_pickup_outcome(ctx, tick_index, loop_state, cfg, proposal, before, after)
    if not loop_state or not proposal or proposal.action ~= "PickAllDrops" or not proposal.drop then return end

    local id = drop_key(proposal.drop)
    if id == "" then return end

    loop_state.drop_pick_failures = loop_state.drop_pick_failures or {}
    loop_state.ignored_drops = loop_state.ignored_drops or {}

    local before_world = before and before.world or {}
    local after_world = after and after.world or {}
    local before_count = count_items(before_world.nearby_resources)
    local after_count = count_items(after_world.nearby_resources)
    local still_present = find_drop_by_id(after_world, id) ~= nil

    if after_count < before_count or not still_present then
        if loop_state.drop_pick_failures[id] then
            ctx.output(string.format(
                "tick=%d pickup_drop_attempt_reset id=%s before=%d after=%d still_present=%s",
                tick_index,
                id,
                before_count,
                after_count,
                tostring(still_present)
            ))
        end
        loop_state.drop_pick_failures[id] = nil
        loop_state.ignored_drops[id] = nil
        return
    end

    local failures = (tonumber(loop_state.drop_pick_failures[id]) or 0) + 1
    loop_state.drop_pick_failures[id] = failures
    local threshold = math.max(1, tonumber(cfg.pickup_drop_fail_threshold) or 1)
    ctx.output(string.format(
        "tick=%d pickup_drop_attempt_failed id=%s failures=%d/%d before=%d after=%d still_present=%s",
        tick_index,
        id,
        failures,
        threshold,
        before_count,
        after_count,
        tostring(still_present)
    ))

    if failures >= threshold then
        local ignore_ticks = math.max(1, tonumber(cfg.pickup_drop_ignore_ticks) or 8)
        ignore_pickup_drop(ctx, tick_index, loop_state, id, failures, ignore_ticks, "failed_pick", id)
        ignore_pickup_cluster(ctx, tick_index, loop_state, cfg, proposal.drop, after_world, failures, ignore_ticks)
    end
end

local function round3(value)
    local n = tonumber(value) or 0
    if n >= 0 then return math.floor(n * 1000 + 0.5) / 1000 end
    return math.ceil(n * 1000 - 0.5) / 1000
end

local function entity_position(entity)
    entity = entity or {}
    return entity.position or { x = entity.x, y = entity.y, z = entity.z }
end

local function update_target_tracks(world, tracks, sample_time_ms)
    tracks = tracks or {}
    local seen = {}
    for _, target in ipairs((world and world.nearby_targets) or {}) do
        local id = tostring(target.id or target.instance_id or target.source_index or "")
        local pos = entity_position(target)
        local x = tonumber(pos.x) or 0
        local y = tonumber(pos.y) or 0
        local z = tonumber(pos.z) or 0
        local previous = tracks[id]
        local vx, vy, vz = 0, 0, 0
        local samples = 1
        if previous then
            local dt = (sample_time_ms - previous.t) / 1000
            samples = (previous.samples or 1) + 1
            if dt > 0 then
                vx = (x - previous.x) / dt
                vy = (y - previous.y) / dt
                vz = (z - previous.z) / dt
            end
        end
        target.vx = vx
        target.vy = vy
        target.vz = vz
        target.has_velocity = previous ~= nil
        target.track_samples = samples
        tracks[id] = { x = x, y = y, z = z, t = sample_time_ms, samples = samples }
        seen[id] = true
    end
    for id, _ in pairs(tracks) do
        if not seen[id] then tracks[id] = nil end
    end
end

local function platform_target_text(proposal)
    local metrics = proposal and proposal.metrics
    local target = proposal and proposal.target
    if not target or not metrics then return "target=none" end
    local loc = proposal.target_loc or metrics.loc or {}
    local predicted = metrics.predicted_position or {}
    return string.format(
        "target id=%s name=%s pos=(%.3f,%.3f) vx=%.3f vy=%.3f platform=%s platform_y=%.3f y_delta=%.3f grounded=%s dx=%.3f dy=%.3f predicted=(%.3f,%.3f) predicted_abs=(%.3f,%.3f) stand=(%.3f,%.3f) stand_dx=%.3f",
        tostring(target.id),
        tostring(target.name),
        tonumber(metrics.target_position and metrics.target_position.x) or tonumber(target.x) or 0,
        tonumber(metrics.target_position and metrics.target_position.y) or tonumber(target.y) or 0,
        tonumber(target.vx) or 0,
        tonumber(target.vy) or 0,
        tostring(loc.platform_id),
        tonumber(loc.platform_y) or 0,
        tonumber(loc.y_delta) or 0,
        tostring(metrics.grounded == true),
        tonumber(metrics.dx) or 0,
        tonumber(metrics.dy) or 0,
        tonumber(predicted.x) or 0,
        tonumber(predicted.y) or 0,
        tonumber(metrics.predicted_abs_x) or 0,
        tonumber(metrics.predicted_abs_y) or 0,
        tonumber(metrics.stand_x) or 0,
        tonumber(metrics.stand_y) or 0,
        tonumber(metrics.stand_dx) or 0
    )
end

local function platform_drop_text(proposal)
    local drop = proposal and proposal.drop
    local metrics = proposal and proposal.metrics
    if not drop then return "drop=none" end
    local loc = proposal.drop_loc or metrics and metrics.loc or {}
    return string.format(
        "drop id=%s name=%s pos=(%.3f,%.3f) platform=%s y_delta=%.3f dx=%.3f dy=%.3f",
        tostring(drop.id),
        tostring(drop.name),
        tonumber(drop.x) or 0,
        tonumber(drop.y) or 0,
        tostring(loc.platform_id),
        tonumber(loc.y_delta) or 0,
        tonumber(metrics and metrics.dx) or 0,
        tonumber(metrics and metrics.dy) or 0
    )
end

local function emit_platform_candidates(ctx, tick_index, proposal, cfg)
    local candidates = proposal and proposal.candidates or {}
    local max_count = math.max(1, tonumber(cfg.max_log_candidates) or 5)
    ctx.output(string.format("tick=%d candidate_count=%d", tick_index, #candidates))
    for i = 1, math.min(max_count, #candidates) do
        local item = candidates[i]
        local target = item.target or {}
        local loc = item.loc or {}
        local predicted = item.predicted_position or {}
        ctx.output(string.format(
            "tick=%d candidate[%d] id=%s name=%s pos=(%.3f,%.3f) vx=%.3f vy=%.3f y_delta=%.3f grounded=%s in_box=%s predicted=(%.3f,%.3f) stand=(%.3f,%.3f) stand_dx=%.3f score=%.3f",
            tick_index,
            i,
            tostring(target.id),
            tostring(target.name),
            tonumber(item.target_position and item.target_position.x) or tonumber(target.x) or 0,
            tonumber(item.target_position and item.target_position.y) or tonumber(target.y) or 0,
            tonumber(target.vx) or 0,
            tonumber(target.vy) or 0,
            tonumber(loc.y_delta) or 0,
            tostring(item.grounded == true),
            tostring(item.in_skill_box == true),
            tonumber(predicted.x) or 0,
            tonumber(predicted.y) or 0,
            tonumber(item.stand_x) or 0,
            tonumber(item.stand_y) or 0,
            tonumber(item.stand_dx) or 0,
            tonumber(item.score) or 0
        ))
    end

    local drops = proposal and proposal.drop_candidates or {}
    if #drops > 0 then
        ctx.output(string.format("tick=%d drop_candidate_count=%d", tick_index, #drops))
        for i = 1, math.min(max_count, #drops) do
            local item = drops[i]
            local drop = item.drop or {}
            ctx.output(string.format(
                "tick=%d drop_candidate[%d] id=%s name=%s pos=(%.3f,%.3f) y_delta=%.3f dx=%.3f dy=%.3f score=%.3f can_pick=%s source=%s owner=%s free=%s",
                tick_index,
                i,
                tostring(drop.id),
                tostring(drop.name),
                tonumber(drop.x) or 0,
                tonumber(drop.y) or 0,
                tonumber(item.loc and item.loc.y_delta) or 0,
                tonumber(item.dx) or 0,
                tonumber(item.dy) or 0,
                tonumber(item.score) or 0,
                tostring(drop.can_pick),
                tostring(drop.drop_source),
                tostring(drop.owner_cid),
                tostring(drop.free)
            ))
        end
    end
end

local function entity_probe_position(entity)
    entity = entity or {}
    return entity.position or { x = entity.x, y = entity.y, z = entity.z }
end

local function locate_probe_point(map, point, cfg, tolerance_key)
    return PlatformMap.locate_point(map, point, {
        y_tolerance = tonumber(cfg[tolerance_key]) or tonumber(cfg.platform_y_tolerance) or 1.0,
        x_margin = tonumber(cfg.platform_x_margin) or 0.2
    })
end

local function emit_platform_drop_scan(ctx, tick_index, map, actor, world, cfg, actor_loc)
    local drops = (world and world.nearby_resources) or {}
    local max_count = math.max(1, tonumber(cfg.max_log_candidates) or 5)
    ctx.output(string.format(
        "tick=%d drop_scan raw_count=%d actor_platform=%s include_all=%s pickup_y_tol=%.3f x_margin=%.3f",
        tick_index,
        #drops,
        tostring(actor_loc and actor_loc.platform_id or "none"),
        tostring(cfg.pickup_include_all_drops == true),
        tonumber(cfg.pickup_platform_y_tolerance) or 0,
        tonumber(cfg.platform_x_margin) or 0
    ))

    local actor_pos = entity_probe_position(actor)
    for i = 1, math.min(max_count, #drops) do
        local drop = drops[i] or {}
        local pos = entity_probe_position(drop)
        local loc, best = locate_probe_point(map, pos, cfg, "pickup_platform_y_tolerance")
        local reason = "candidate"
        if cfg.pickup_include_all_drops ~= true and drop.can_pick == false then
            reason = "filtered_not_pickable"
        elseif not actor_loc then
            reason = "actor_not_on_platform"
        elseif not loc then
            reason = best and "filtered_y_tolerance" or "filtered_no_platform"
        elseif tostring(loc.platform_id) ~= tostring(actor_loc.platform_id) then
            reason = "filtered_other_platform"
        end

        ctx.output(string.format(
            "tick=%d drop_scan[%d] id=%s name=%s pos=(%.3f,%.3f) actor_dx=%.3f actor_dy=%.3f loc=%s best=%s y_delta=%s best_y_delta=%s reason=%s can_pick=%s source=%s owner=%s free=%s",
            tick_index,
            i,
            tostring(drop.id),
            tostring(drop.name),
            tonumber(pos.x) or 0,
            tonumber(pos.y) or 0,
            (tonumber(pos.x) or 0) - (tonumber(actor_pos.x) or 0),
            (tonumber(pos.y) or 0) - (tonumber(actor_pos.y) or 0),
            tostring(loc and loc.platform_id or "none"),
            tostring(best and best.platform_id or "none"),
            tostring(loc and loc.y_delta or ""),
            tostring(best and best.y_delta or ""),
            reason,
            tostring(drop.can_pick),
            tostring(drop.drop_source),
            tostring(drop.owner_cid),
            tostring(drop.free)
        ))
    end
end

local function platform_proposal_text(proposal)
    proposal = proposal or {}
    local params = proposal.params or {}
    local debug = proposal.debug or {}
    return string.format(
        "action=%s reason=%s direction=%s key=%s/0x%X platform_candidates=%s total_targets=%s total_drops=%s sweep_target=%s sweep_ticks=%s confidence=%.2f",
        tostring(proposal.action),
        tostring(proposal.reason),
        tostring(params.direction or debug.direction or ""),
        tostring(params.key_name or ""),
        tonumber(params.key_code) or 0,
        tostring(debug.platform_candidates or ""),
        tostring(debug.total_targets or ""),
        tostring(debug.total_drops or ""),
        tostring(debug.sweep_target_x or ""),
        tostring(debug.sweep_ticks or ""),
        tonumber(proposal.confidence) or 0
    )
end

local function platform_safe_bounds_for_probe(platform, cfg)
    platform = platform or {}
    local margin = tonumber(cfg.pickup_sweep_safe_margin)
        or tonumber(cfg.platform_safe_margin)
        or tonumber(platform.safe_margin)
        or 0
    local left = (tonumber(platform.left_x) or 0) + margin
    local right = (tonumber(platform.right_x) or 0) - margin
    if left > right then
        left = tonumber(platform.left_x) or 0
        right = tonumber(platform.right_x) or 0
    end
    return left, right
end

local function init_pickup_sweep(ctx, loop_state, actor, actor_loc, cfg)
    local pos = actor and actor.position or {}
    local actor_x = tonumber(pos.x) or 0
    local left, right = platform_safe_bounds_for_probe(actor_loc and actor_loc.platform or {}, cfg)
    local start_left = math.abs(actor_x - left) <= math.abs(actor_x - right)
    loop_state.pickup_sweep = {
        left = left,
        right = right,
        direction = start_left and 1 or -1,
        target_x = start_left and left or right,
        done = false,
        finish_after_target = false,
        ticks = 0,
        pick_at_current = true
    }
    ctx.output(string.format(
        "pickup_sweep init actor_x=%.3f left=%.3f right=%.3f target=%.3f direction=%d step=%.3f arrival=%.3f margin=%.3f",
        actor_x,
        left,
        right,
        loop_state.pickup_sweep.target_x,
        loop_state.pickup_sweep.direction,
        tonumber(cfg.pickup_sweep_step) or 0.35,
        tonumber(cfg.pickup_sweep_arrival_x) or 0.15,
        tonumber(cfg.pickup_sweep_safe_margin) or tonumber(cfg.platform_safe_margin) or 0
    ))
end

local function advance_pickup_sweep(sweep, cfg)
    local step = math.max(0.2, tonumber(cfg.pickup_sweep_step) or 1.0)
    if sweep.finish_after_target then
        sweep.done = true
        sweep.finish_after_target = false
        return
    end
    local next_x = (tonumber(sweep.target_x) or 0) + (tonumber(sweep.direction) or 1) * step
    if sweep.direction > 0 and next_x > sweep.right then
        sweep.target_x = sweep.right
        sweep.finish_after_target = true
    elseif sweep.direction < 0 and next_x < sweep.left then
        sweep.target_x = sweep.left
        sweep.finish_after_target = true
    else
        sweep.target_x = next_x
    end
end

local function pickup_sweep_proposal(ctx, actor, actor_loc, cfg, loop_state)
    if cfg.pickup_sweep_enabled == false then return nil end
    if not actor_loc then return nil end
    if not loop_state.pickup_sweep then init_pickup_sweep(ctx, loop_state, actor, actor_loc, cfg) end

    local sweep = loop_state.pickup_sweep
    if sweep.done then return nil end
    sweep.ticks = (tonumber(sweep.ticks) or 0) + 1
    local max_ticks = math.max(1, tonumber(cfg.pickup_sweep_max_ticks) or 220)
    if sweep.ticks > max_ticks then
        sweep.done = true
        ctx.output(string.format("pickup_sweep forced_done ticks=%d max_ticks=%d", sweep.ticks, max_ticks))
        return nil
    end

    local actor_x = tonumber(actor and actor.position and actor.position.x) or 0
    local target_x = tonumber(sweep.target_x) or actor_x
    local tolerance = math.max(0.05, tonumber(cfg.pickup_sweep_arrival_x) or 0.25)
    local dx = target_x - actor_x
    local at_target = math.abs(dx) <= tolerance

    if sweep.pick_at_current or at_target then
        sweep.pick_at_current = false
        local proposal = {
            action = "PickAllDrops",
            reason = "platform_pickup_sweep_pick",
            actor_loc = actor_loc,
            candidates = {},
            drop_candidates = {},
            params = {},
            debug = {
                sweep_target_x = target_x,
                sweep_left = sweep.left,
                sweep_right = sweep.right,
                sweep_direction = sweep.direction,
                sweep_ticks = sweep.ticks
            }
        }
        if at_target then
            local old_target = target_x
            advance_pickup_sweep(sweep, cfg)
            ctx.output(string.format(
                "pickup_sweep advance from=%.3f to=%.3f done=%s finish_after_target=%s",
                old_target,
                tonumber(sweep.target_x) or old_target,
                tostring(sweep.done == true),
                tostring(sweep.finish_after_target == true)
            ))
        end
        return proposal
    end

    return {
        action = "SetWalkDirection",
        reason = "platform_pickup_sweep_move",
        actor_loc = actor_loc,
        candidates = {},
        drop_candidates = {},
        params = { direction = dx < 0 and -1 or 1, vertical = 0 },
        metrics = {
            dx = dx,
            abs_x = math.abs(dx),
            target_x = target_x
        },
        debug = {
            sweep_target_x = target_x,
            sweep_left = sweep.left,
            sweep_right = sweep.right,
            sweep_direction = sweep.direction,
            sweep_ticks = sweep.ticks
        }
    }
end

local function run_platform_action(ctx, proposal, cfg, loop_state)
    proposal = proposal or { action = "Wait", params = {} }
    local action = proposal.action
    local params = proposal.params or {}
    local results = {}

    if action == "SetWalkDirection" then
        local direction = tonumber(params.direction) or loop_state.last_direction or 1
        local reason = tostring(proposal.reason or params.reason or "")
        local is_pickup_move = reason == "platform_move_to_drop"
            or reason == "platform_pickup_sweep_move"
            or reason == "platform_drop_nearby_during_combat"
            or reason == "platform_drop_aged_during_combat"
        local move_ms = is_pickup_move and (tonumber(cfg.pickup_move_ms) or tonumber(cfg.move_ms) or 180)
            or (tonumber(cfg.move_ms) or 180)
        local move_method = tostring((is_pickup_move and cfg.pickup_move_method) or cfg.move_method or "key")
        local move_key_code = direction < 0
            and (tonumber(cfg.move_left_key_code) or 0x25)
            or (tonumber(cfg.move_right_key_code) or 0x27)
        local move_key_name = direction < 0 and "Left" or "Right"
        ctx.output(string.format(
            "move_step reason=%s direction=%s method=%s key=%s/0x%X move_ms=%s target_x=%s dx=%s",
            reason,
            tostring(direction),
            move_method,
            move_key_name,
            move_key_code,
            tostring(move_ms),
            tostring(params.target_x or (proposal.debug and proposal.debug.sweep_target_x) or ""),
            tostring(params.dx or (proposal.metrics and proposal.metrics.dx) or "")
        ))
        if move_method == "walk_api" then
            results[#results + 1] = run_action(ctx, "SetWalkDirection", params)
            loop_state.is_moving = results[#results].ok == true
            sleep_ms(move_ms)
            results[#results + 1] = run_action(ctx, "StopMove", {})
        else
            results[#results + 1] = run_action(ctx, "PressKey", {
                key_code = move_key_code,
                key_name = move_key_name,
                input_mode = cfg.skill_input_mode or cfg.input_mode or "foreground",
                key_mode = cfg.key_mode,
                hold_ms = move_ms
            })
        end
        loop_state.last_direction = direction
        loop_state.is_moving = false
    elseif action == "FaceAndPressKey" then
        local direction = tonumber(proposal.metrics and proposal.metrics.direction) or loop_state.last_direction or 1
        local face_ms = tonumber(cfg.face_ms) or 80
        local face_method = tostring(cfg.face_method or "key")
        local face_key_code = direction < 0
            and (tonumber(cfg.face_left_key_code) or 0x25)
            or (tonumber(cfg.face_right_key_code) or 0x27)
        local face_key_name = direction < 0 and "Left" or "Right"
        ctx.output(string.format(
            "face_before_attack direction=%s method=%s key=%s/0x%X face_ms=%s",
            tostring(direction),
            face_method,
            face_key_name,
            face_key_code,
            tostring(face_ms)
        ))
        if face_method == "walk_api" then
            results[#results + 1] = run_action(ctx, "SetWalkDirection", { direction = direction, vertical = 0 })
            sleep_ms(face_ms)
            results[#results + 1] = run_action(ctx, "StopMove", {})
        else
            results[#results + 1] = run_action(ctx, "PressKey", {
                key_code = face_key_code,
                key_name = face_key_name,
                input_mode = cfg.skill_input_mode or cfg.input_mode or "foreground",
                key_mode = cfg.key_mode,
                hold_ms = face_ms
            })
        end
        loop_state.last_direction = direction
        loop_state.is_moving = false
        results[#results + 1] = run_action(ctx, "PressKey", params)
        if not results[#results].ok and cfg.fallback_to_basic_attack ~= false then
            ctx.output(string.format("fallback action=BasicAttack reason=%s", tostring(results[#results].reason or "press_key_failed")))
            results[#results + 1] = run_action(ctx, "BasicAttack", {})
        end
        sleep_ms(tonumber(cfg.attack_wait_ms) or 750)
    elseif action == "PickAllDrops" then
        local repeat_count = math.max(1, tonumber(cfg.pickup_pick_repeat) or 2)
        local repeat_ms = math.max(0, tonumber(cfg.pickup_pick_repeat_ms) or 120)
        local pickup_key_enabled = cfg.pickup_key_enabled ~= false
        local pickup_key_repeat = math.max(1, tonumber(cfg.pickup_key_repeat) or 2)
        local pickup_key_repeat_ms = math.max(0, tonumber(cfg.pickup_key_repeat_ms) or 80)
        local pickup_key_code = tonumber(cfg.pickup_key_code) or 0x5A
        local pickup_key_name = cfg.pickup_key_name or "Z"
        local pickup_key_hold_ms = math.max(0, tonumber(cfg.pickup_key_hold_ms) or 0)
        for i = 1, repeat_count do
            ctx.output(string.format("pick_repeat index=%d/%d", i, repeat_count))
            results[#results + 1] = run_action(ctx, "PickAllDrops", {})
            ctx.output(string.format("pick_result index=%d/%d raw=%s", i, repeat_count, action_raw_text(results[#results])))
            if pickup_key_enabled then
                for key_index = 1, pickup_key_repeat do
                    ctx.output(string.format(
                        "pickup_key_repeat pick_index=%d/%d key_index=%d/%d key=%s/0x%X hold_ms=%d",
                        i,
                        repeat_count,
                        key_index,
                        pickup_key_repeat,
                        tostring(pickup_key_name),
                        pickup_key_code,
                        pickup_key_hold_ms
                    ))
                    local key_result = run_action(ctx, "PressKey", {
                        key_code = pickup_key_code,
                        key_name = pickup_key_name,
                        input_mode = cfg.skill_input_mode or cfg.input_mode or "foreground",
                        key_mode = cfg.key_mode,
                        hold_ms = pickup_key_hold_ms
                    })
                    ctx.output(string.format(
                        "pickup_key_result pick_index=%d/%d key_index=%d/%d %s",
                        i,
                        repeat_count,
                        key_index,
                        pickup_key_repeat,
                        key_result_text(key_result)
                    ))
                    if not key_result.ok then
                        ctx.output(string.format(
                            "pickup_key optional_failed reason=%s key=%s/0x%X",
                            tostring(key_result.reason or "press_key_failed"),
                            tostring(pickup_key_name),
                            pickup_key_code
                        ))
                    end
                    if key_index < pickup_key_repeat and pickup_key_repeat_ms > 0 then sleep_ms(pickup_key_repeat_ms) end
                end
            end
            if i < repeat_count and repeat_ms > 0 then sleep_ms(repeat_ms) end
        end
        sleep_ms(tonumber(cfg.pick_wait_ms) or 250)
    elseif action == "Wait" then
        sleep_ms(tonumber(params.seconds) and tonumber(params.seconds) * 1000 or tonumber(cfg.tick_ms) or 120)
    else
        ctx.output("unsupported platform action=" .. tostring(action) .. " fallback=Wait")
        sleep_ms(tonumber(cfg.tick_ms) or 120)
    end

    local ok = true
    for _, result in ipairs(results) do
        if not result.ok then ok = false end
    end
    return ok, results
end

function Probe.actions(opts)
    local ctx = new_context(opts)
    local quickslot = tonumber(ctx.opts.quickslot_slot) or 1
    local move_ms = tonumber(ctx.opts.move_ms) or 300
    ctx.output("Maple action probe started")
    ctx.output("This probe issues client actions: attack, quickslot, walk, stop, pick.")

    local connected = connect(ctx)
    if not connected.ok then
        return {
            ok = false,
            reason = connected.reason,
            bb = ctx.bb,
            diagnostics = ctx.api.last_calls
        }
    end

    local results = {}
    results[#results + 1] = run_action(ctx, "BasicAttack", {})
    results[#results + 1] = run_action(ctx, "UseQuickslot", { slot = quickslot, action = "press" })
    results[#results + 1] = run_action(ctx, "SetWalkDirection", { direction = -1, vertical = 0 })
    sleep_ms(move_ms)
    results[#results + 1] = run_action(ctx, "StopMove", {})
    results[#results + 1] = run_action(ctx, "SetWalkDirection", { direction = 1, vertical = 0 })
    sleep_ms(move_ms)
    results[#results + 1] = run_action(ctx, "StopMove", {})
    results[#results + 1] = run_action(ctx, "PickAllDrops", {})
    ctx.output("Maple action probe finished")

    local ok = true
    for _, result in ipairs(results) do
        if not result.ok then ok = false end
    end

    return {
        ok = ok,
        bb = ctx.bb,
        results = results,
        diagnostics = ctx.api.last_calls
    }
end

function Probe.quickslot(opts)
    local ctx = new_context(opts)
    local quickslot = tonumber(ctx.opts.quickslot_slot) or 1
    local action = ctx.opts.quickslot_action or "press"
    local repeat_count = math.max(tonumber(ctx.opts.repeat_count) or 1, 1)
    local interval_ms = tonumber(ctx.opts.interval_ms) or 250
    ctx.output(string.format("Maple quickslot probe started slot=%d action=%s repeat=%d", quickslot, tostring(action), repeat_count))

    local connected = connect(ctx)
    if not connected.ok then
        return {
            ok = false,
            reason = connected.reason,
            bb = ctx.bb,
            diagnostics = ctx.api.last_calls
        }
    end

    local slot_result = ctx.api:call("list_quickslot", ctx.bb)
    emit_result(ctx.output, "list_quickslot", slot_result)
    local skills_result = ctx.api:call("list_skills", ctx.bb)
    emit_result(ctx.output, "list_skills", skills_result)

    local selected_slot = find_quickslot(value(slot_result), quickslot)
    if selected_slot then
        ctx.output(string.format(
            "selected quickslot slot=%s key=%s cat=%s id=%s skill_name=%s",
            tostring(selected_slot.slot),
            tostring(selected_slot.key),
            tostring(selected_slot.cat),
            tostring(selected_slot.id),
            find_skill_name(value(skills_result), selected_slot.id)
        ))
    else
        ctx.output(string.format("selected quickslot slot=%d not found", quickslot))
    end

    local results = {}
    for i = 1, repeat_count do
        results[#results + 1] = run_action(ctx, "UseQuickslot", { slot = quickslot, action = action })
        if i < repeat_count then sleep_ms(interval_ms) end
    end
    ctx.output("Maple quickslot probe finished")

    local ok = true
    for _, result in ipairs(results) do
        if not result.ok then ok = false end
    end

    return {
        ok = ok,
        bb = ctx.bb,
        selected_slot = selected_slot,
        results = results,
        diagnostics = ctx.api.last_calls
    }
end

function Probe.quickslot_effect(opts)
    local ctx = new_context(opts)
    local quickslot = tonumber(ctx.opts.quickslot_slot) or 1
    local action = ctx.opts.quickslot_action or "press"
    local wait_ms = tonumber(ctx.opts.wait_ms) or 900
    ctx.output(string.format("Maple quickslot effect probe started slot=%d action=%s wait_ms=%d", quickslot, tostring(action), wait_ms))

    local connected = connect(ctx)
    if not connected.ok then
        return {
            ok = false,
            reason = connected.reason,
            bb = ctx.bb,
            diagnostics = ctx.api.last_calls
        }
    end

    local slot_result = ctx.api:call("list_quickslot", ctx.bb)
    emit_result(ctx.output, "list_quickslot", slot_result)
    local skills_result = ctx.api:call("list_skills", ctx.bb)
    emit_result(ctx.output, "list_skills", skills_result)

    local selected_slot = find_quickslot(value(slot_result), quickslot)
    if selected_slot then
        ctx.output(string.format(
            "selected quickslot slot=%s key=%s cat=%s id=%s skill_name=%s",
            tostring(selected_slot.slot),
            tostring(selected_slot.key),
            tostring(selected_slot.cat),
            tostring(selected_slot.id),
            find_skill_name(value(skills_result), selected_slot.id)
        ))
    else
        ctx.output(string.format("selected quickslot slot=%d not found", quickslot))
    end

    local before = read_effect_state(ctx)
    emit_effect_state(ctx.output, "effect before", before)
    local use_result = run_action(ctx, "UseQuickslot", { slot = quickslot, action = action })
    sleep_ms(wait_ms)
    local after = read_effect_state(ctx)
    emit_effect_state(ctx.output, "effect after", after)
    emit_effect_delta(ctx.output, before, after)
    ctx.output("Maple quickslot effect probe finished")

    return {
        ok = use_result.ok == true,
        bb = ctx.bb,
        selected_slot = selected_slot,
        before = before,
        after = after,
        result = use_result,
        diagnostics = ctx.api.last_calls
    }
end

function Probe.pickup_effect(opts)
    local ctx = new_context(opts)
    local wait_ms = tonumber(ctx.opts.wait_ms) or 900
    local repeat_count = math.max(1, tonumber(ctx.opts.repeat_count) or 1)
    local pickup_api_enabled = ctx.opts.pickup_api_enabled ~= false
    local pickup_key_enabled = ctx.opts.pickup_key_enabled ~= false
    local pickup_key_code = tonumber(ctx.opts.pickup_key_code or ctx.opts.key_code) or 0x5A
    local pickup_key_name = ctx.opts.pickup_key_name or ctx.opts.key_name or "Z"
    local pickup_key_hold_ms = math.max(0, tonumber(ctx.opts.pickup_key_hold_ms or ctx.opts.hold_ms) or 80)
    local input_mode = ctx.opts.input_mode or "foreground"
    local key_mode = ctx.opts.key_mode
    ctx.output(string.format(
        "Maple pickup effect probe started repeat=%d wait_ms=%d api_enabled=%s key_enabled=%s key=%s/0x%X hold_ms=%d mode=%s",
        repeat_count,
        wait_ms,
        tostring(pickup_api_enabled),
        tostring(pickup_key_enabled),
        tostring(pickup_key_name),
        pickup_key_code,
        pickup_key_hold_ms,
        tostring(input_mode)
    ))

    local connected = connect(ctx)
    if not connected.ok then
        return {
            ok = false,
            reason = connected.reason,
            bb = ctx.bb,
            diagnostics = ctx.api.last_calls
        }
    end

    local before = read_effect_state(ctx)
    emit_effect_state(ctx.output, "effect before", before)

    local results = {}
    for i = 1, repeat_count do
        if pickup_api_enabled then
            results[#results + 1] = run_action(ctx, "PickAllDrops", {})
            ctx.output(string.format("pickup_effect pick_result index=%d/%d raw=%s", i, repeat_count, action_raw_text(results[#results])))
        end
        if pickup_key_enabled then
            results[#results + 1] = run_action(ctx, "PressKey", {
                key_code = pickup_key_code,
                key_name = pickup_key_name,
                input_mode = input_mode,
                key_mode = key_mode,
                hold_ms = pickup_key_hold_ms
            })
            ctx.output(string.format("pickup_effect key_result index=%d/%d %s", i, repeat_count, key_result_text(results[#results])))
        end
        if i < repeat_count then sleep_ms(wait_ms) end
    end

    sleep_ms(wait_ms)
    local after = read_effect_state(ctx)
    emit_effect_state(ctx.output, "effect after", after)
    emit_effect_delta(ctx.output, before, after)
    ctx.output("Maple pickup effect probe finished")

    local ok = true
    for _, result in ipairs(results) do
        if not result.ok then ok = false end
    end
    return {
        ok = ok,
        bb = ctx.bb,
        before = before,
        after = after,
        results = results,
        diagnostics = ctx.api.last_calls
    }
end

function Probe.pickup_verify(opts)
    local ctx = new_context(opts)
    local wait_schedule = parse_wait_schedule(ctx.opts.verify_waits or ctx.opts.wait_schedule)
    local repeat_count = math.max(1, tonumber(ctx.opts.repeat_count) or 1)
    local action_wait_ms = math.max(0, tonumber(ctx.opts.action_wait_ms) or 120)
    local max_drop_log = math.max(1, tonumber(ctx.opts.max_drop_log) or 8)
    local max_key_log = math.max(1, tonumber(ctx.opts.max_key_log) or 5)
    local pickup_api_enabled = ctx.opts.pickup_api_enabled ~= false
    local pickup_key_enabled = ctx.opts.pickup_key_enabled == true
    local pickup_key_code = tonumber(ctx.opts.pickup_key_code or ctx.opts.key_code) or 0x5A
    local pickup_key_name = ctx.opts.pickup_key_name or ctx.opts.key_name or "Z"
    local pickup_key_hold_ms = math.max(0, tonumber(ctx.opts.pickup_key_hold_ms or ctx.opts.hold_ms) or 80)
    local input_mode = ctx.opts.input_mode or "foreground"
    local key_mode = ctx.opts.key_mode
    ctx.output(string.format(
        "Maple pickup verify probe started repeat=%d action_wait_ms=%d waits=%s api_enabled=%s key_enabled=%s key=%s/0x%X hold_ms=%d mode=%s",
        repeat_count,
        action_wait_ms,
        table.concat(wait_schedule, ","),
        tostring(pickup_api_enabled),
        tostring(pickup_key_enabled),
        tostring(pickup_key_name),
        pickup_key_code,
        pickup_key_hold_ms,
        tostring(input_mode)
    ))

    local connected = connect(ctx)
    if not connected.ok then
        return {
            ok = false,
            reason = connected.reason,
            bb = ctx.bb,
            diagnostics = ctx.api.last_calls
        }
    end

    local before = read_pickup_verify_snapshot(ctx, "before", max_drop_log)
    if #before.drops <= 0 then
        ctx.output("pickup_verify warning=no_visible_drops_before_action")
    end

    local results = {}
    for i = 1, repeat_count do
        if pickup_api_enabled then
            results[#results + 1] = run_action(ctx, "PickAllDrops", {})
            ctx.output(string.format("pickup_verify pick_result index=%d/%d raw=%s", i, repeat_count, action_raw_text(results[#results])))
        end
        if pickup_key_enabled then
            results[#results + 1] = run_action(ctx, "PressKey", {
                key_code = pickup_key_code,
                key_name = pickup_key_name,
                input_mode = input_mode,
                key_mode = key_mode,
                hold_ms = pickup_key_hold_ms
            })
            ctx.output(string.format("pickup_verify key_result index=%d/%d %s", i, repeat_count, key_result_text(results[#results])))
        end
        if i < repeat_count and action_wait_ms > 0 then sleep_ms(action_wait_ms) end
    end

    local samples = {}
    local final_comparison = nil
    local last_wait = 0
    for _, wait_ms in ipairs(wait_schedule) do
        local delta = wait_ms - last_wait
        if delta > 0 then sleep_ms(delta) end
        last_wait = wait_ms

        local label = "after_" .. tostring(wait_ms) .. "ms"
        local snapshot = read_pickup_verify_snapshot(ctx, label, max_drop_log)
        local comparison = pickup_verify_compare(before, snapshot)
        emit_pickup_verify_compare(ctx.output, label, comparison, max_key_log)
        samples[#samples + 1] = {
            wait_ms = wait_ms,
            snapshot = snapshot,
            comparison = comparison
        }
        final_comparison = comparison
    end

    final_comparison = final_comparison or pickup_verify_compare(before, before)
    local claimed = claimed_pick_count(results)
    local inventory_changed = final_comparison.meso_delta ~= 0
        or final_comparison.used_slots_delta ~= 0
        or final_comparison.item_total_delta ~= 0
        or final_comparison.code_added > 0
        or final_comparison.code_removed > 0
    local same_drop_still_visible = final_comparison.unchanged_count > 0
    local list_changed = final_comparison.disappeared_count > 0 or final_comparison.appeared_count > 0
    local verdict = "no_visible_drop_to_verify"
    if final_comparison.before_drop_count > 0 then
        if not same_drop_still_visible and final_comparison.disappeared_count > 0 then
            verdict = "drop_keys_cleared"
        elseif same_drop_still_visible and inventory_changed then
            verdict = "inventory_changed_but_drop_keys_still_visible"
        elseif same_drop_still_visible and claimed > 0 then
            verdict = "pick_claimed_but_drop_keys_still_visible"
        elseif same_drop_still_visible then
            verdict = "drop_keys_still_visible"
        elseif list_changed then
            verdict = "drop_list_changed"
        else
            verdict = "no_observable_change"
        end
    end

    ctx.output(string.format(
        "pickup_verify conclusion verdict=%s claimed_pick_count=%d same_drop_still_visible=%s list_changed=%s inventory_changed=%s final_drops=%d disappeared=%d unchanged=%d appeared=%d meso_delta=%s used_slots_delta=%s item_total_delta=%s",
        verdict,
        claimed,
        tostring(same_drop_still_visible),
        tostring(list_changed),
        tostring(inventory_changed),
        final_comparison.after_drop_count,
        final_comparison.disappeared_count,
        final_comparison.unchanged_count,
        final_comparison.appeared_count,
        tostring(final_comparison.meso_delta),
        tostring(final_comparison.used_slots_delta),
        tostring(final_comparison.item_total_delta)
    ))
    ctx.output("Maple pickup verify probe finished")

    local ok = true
    for _, result in ipairs(results) do
        if not result.ok then ok = false end
    end
    return {
        ok = ok,
        bb = ctx.bb,
        before = before,
        samples = samples,
        results = results,
        summary = {
            verdict = verdict,
            claimed_pick_count = claimed,
            before_drop_count = final_comparison.before_drop_count,
            final_drop_count = final_comparison.after_drop_count,
            final_disappeared_count = final_comparison.disappeared_count,
            final_unchanged_count = final_comparison.unchanged_count,
            final_appeared_count = final_comparison.appeared_count,
            same_drop_still_visible = same_drop_still_visible,
            list_changed = list_changed,
            inventory_changed = inventory_changed,
            meso_delta = final_comparison.meso_delta,
            used_slots_delta = final_comparison.used_slots_delta,
            item_total_delta = final_comparison.item_total_delta
        },
        diagnostics = ctx.api.last_calls
    }
end

function Probe.key_effect(opts)
    local ctx = new_context(opts)
    local key_code = tonumber(ctx.opts.key_code) or 0x10
    local mode = ctx.opts.input_mode or "foreground"
    local wait_ms = tonumber(ctx.opts.wait_ms) or 900
    local hold_ms = tonumber(ctx.opts.hold_ms) or 0
    ctx.output(string.format("Maple key effect probe started mode=%s key=0x%X wait_ms=%d hold_ms=%d", tostring(mode), key_code, wait_ms, hold_ms))

    local connected = connect(ctx)
    if not connected.ok then
        return {
            ok = false,
            reason = connected.reason,
            bb = ctx.bb,
            diagnostics = ctx.api.last_calls
        }
    end

    local pid = connected.data and connected.data.pid
    ctx.output(string.format("input target pid=%s hwnd=%s", tostring(pid), ctx.opts.hwnd and string.format("0x%X", ctx.opts.hwnd) or "auto"))

    local before = read_effect_state(ctx)
    emit_effect_state(ctx.output, "effect before", before)
    local input_result = run_action(ctx, "PressKey", {
        key_code = key_code,
        key_name = ctx.opts.key_name or "Shift",
        input_mode = mode,
        key_mode = ctx.opts.key_mode,
        hold_ms = hold_ms,
        hwnd = ctx.opts.hwnd
    })
    local press_data = input_result.data or {}
    ctx.output(string.format(
        "key input ok=%s reason=%s method=%s hwnd=%s",
        tostring(input_result.ok == true),
        tostring(input_result.reason or ""),
        tostring(press_data.method or ""),
        press_data.hwnd and string.format("0x%X", press_data.hwnd) or "nil"
    ))
    sleep_ms(wait_ms)
    local after = read_effect_state(ctx)
    emit_effect_state(ctx.output, "effect after", after)
    emit_effect_delta(ctx.output, before, after)
    ctx.output("Maple key effect probe finished")

    return {
        ok = input_result.ok == true,
        reason = input_result.reason,
        bb = ctx.bb,
        pid = pid,
        hwnd = press_data.hwnd,
        key_code = key_code,
        input_mode = mode,
        result = input_result,
        before = before,
        after = after,
        diagnostics = ctx.api.last_calls
    }
end

function Probe.basic_combat(opts)
    local ctx = new_context(opts)
    local cfg = clone_combat_config(ctx.opts)
    local max_ticks = math.max(1, tonumber(ctx.opts.max_ticks or cfg.baseline_max_ticks) or 80)
    local run_seconds = math.max(1, tonumber(ctx.opts.run_seconds or cfg.baseline_run_seconds) or 20)
    local started_at = os and os.time and os.time() or nil
    local loop_state = {
        is_moving = false,
        just_attacked = false,
        last_target_id = nil,
        last_mob_count = nil,
        last_drop_count = nil
    }

    ctx.output(string.format(
        "Maple basic combat probe started seconds=%d max_ticks=%d key=%s/0x%X range=(%s,%s)",
        run_seconds,
        max_ticks,
        tostring(cfg.skill_key or "Shift"),
        tonumber(cfg.skill_key_code) or 0x10,
        tostring(cfg.baseline_attack_range_x),
        tostring(cfg.baseline_attack_range_y)
    ))

    local connected = connect(ctx)
    if not connected.ok then
        return {
            ok = false,
            reason = connected.reason,
            bb = ctx.bb,
            diagnostics = ctx.api.last_calls
        }
    end

    local ok = true
    local ticks = 0
    while ticks < max_ticks do
        ticks = ticks + 1
        if started_at and os.time and ((os.time() - started_at) >= run_seconds) then
            ctx.output(string.format("tick=%d stop reason=duration_reached", ticks))
            break
        end

        ctx.output(string.format("tick=%d begin", ticks))
        local before = read_combat_state(ctx, string.format("tick=%d before", ticks))
        local actor = before.actor or {}
        local world = before.world or {}
        local proposal = CombatRuntime.decide({
            actor = actor,
            world = world,
            cfg = cfg,
            state = loop_state
        })

        local target_info = proposal.target and target_text(proposal.target, proposal.metrics) or "none"
        ctx.output(string.format("tick=%d target %s", ticks, target_info))
        ctx.output(string.format("tick=%d proposal %s", ticks, action_summary(proposal)))

        local action_ok = run_baseline_action(ctx, proposal, cfg, loop_state)
        if not action_ok then ok = false end

        local after = read_combat_state(ctx, string.format("tick=%d after", ticks))
        emit_after_delta(ctx, ticks, before, after, proposal)

        local after_world = after.world or {}
        loop_state.last_target_id = proposal.target and proposal.target.id or loop_state.last_target_id
        loop_state.last_mob_count = count_items(after_world.nearby_targets)
        loop_state.last_drop_count = count_items(after_world.nearby_resources)
        loop_state.just_attacked = false

        ctx.output(string.format("tick=%d end ok=%s", ticks, tostring(action_ok)))
    end

    if loop_state.is_moving then
        ctx.output("cleanup action=StopMove reason=probe_end")
        run_action(ctx, "StopMove", {})
        loop_state.is_moving = false
    end

    ctx.output(string.format("Maple basic combat probe finished ticks=%d ok=%s", ticks, tostring(ok)))

    return {
        ok = ok,
        bb = ctx.bb,
        ticks = ticks,
        state = loop_state,
        diagnostics = ctx.api.last_calls
    }
end

function Probe.platform_combat(opts)
    local ctx = new_context(opts)
    local cfg = clone_platform_combat_config(ctx.opts)
    local cwd = sys and sys.get_cwd and sys.get_cwd() or "."
    local map_path = ctx.opts.platform_path
        or ctx.opts.platform_map_path
        or ctx.opts.probe_platform_path
        or (cwd .. "/scripts/maple/maps/manual_platform.lua")
    local map, map_err = PlatformMap.load(map_path, {
        merge_epsilon = tonumber(ctx.opts.platform_merge_epsilon or ctx.opts.probe_platform_merge_epsilon) or 0.05
    })
    local max_ticks = math.max(0, tonumber(ctx.opts.max_ticks or ctx.opts.probe_max_ticks) or 0)
    local run_seconds = math.max(0, tonumber(ctx.opts.run_seconds or ctx.opts.probe_run_seconds) or 0)
    local clear_remaining_threshold = math.max(0, tonumber(ctx.opts.clear_remaining_threshold or ctx.opts.probe_clear_remaining_threshold or cfg.clear_remaining_threshold) or 1)
    local pickup_empty_confirm_ticks = math.max(1, tonumber(ctx.opts.pickup_empty_confirm_ticks or ctx.opts.probe_pickup_empty_confirm_ticks or cfg.pickup_empty_confirm_ticks) or 3)
    local started_ms = now_ms()
    local loop_state = {
        is_moving = false,
        last_direction = 1,
        tracks = {},
        cleanup_pickup = false,
        empty_drop_scans = 0,
        drop_pick_failures = {},
        ignored_drops = {}
    }

    ctx.output(string.format(
        "Maple platform combat probe started seconds=%d max_ticks=%d clear_remaining_threshold=%d pickup_empty_confirm_ticks=%d pickup_sweep_enabled=%s map=%s key=%s/0x%X skill_range=(%.3f,%.3f) preferred=%.3f cast_delay=%.3fs tolerances actor=%.3f mob=%.3f grounded=%.3f",
        run_seconds,
        max_ticks,
        clear_remaining_threshold,
        pickup_empty_confirm_ticks,
        tostring(cfg.pickup_sweep_enabled == true),
        tostring(map_path),
        tostring(cfg.skill_key or "Shift"),
        tonumber(cfg.skill_key_code) or 0x10,
        tonumber(cfg.skill_range_x) or 0,
        tonumber(cfg.skill_range_y) or 0,
        tonumber(cfg.preferred_attack_distance) or 0,
        tonumber(cfg.cast_delay_seconds) or 0,
        tonumber(cfg.actor_platform_y_tolerance) or 0,
        tonumber(cfg.platform_y_tolerance) or 0,
        tonumber(cfg.grounded_y_tolerance) or 0
    ))

    if not map then
        ctx.output("platform map load failed reason=" .. tostring(map_err))
        return {
            ok = false,
            reason = "platform_map_load_failed",
            error = map_err,
            diagnostics = ctx.api.last_calls
        }
    end
    ctx.output(string.format("platform map loaded map_id=%s platforms=%d", tostring(map.map_id), #(map.platforms or {})))

    local connected = connect(ctx)
    if not connected.ok then
        return {
            ok = false,
            reason = connected.reason,
            bb = ctx.bb,
            diagnostics = ctx.api.last_calls
        }
    end

    local ok = true
    local ticks = 0
    while max_ticks <= 0 or ticks < max_ticks do
        ticks = ticks + 1
        if run_seconds > 0 and now_ms() - started_ms >= run_seconds * 1000 then
            ctx.output(string.format("tick=%d stop reason=duration_reached", ticks))
            break
        end

        local tick_started = now_ms()
        ctx.output(string.format("tick=%d begin elapsed_ms=%.3f", ticks, tick_started - started_ms))
        local before = read_combat_state(ctx, string.format("tick=%d before", ticks))
        update_target_tracks(before.world, loop_state.tracks, tick_started)
        expire_ignored_drops(ctx, ticks, loop_state)
        update_drop_seen_ticks(ctx, ticks, loop_state, before.world)
        loop_state.tick_index = ticks

        local mode = loop_state.cleanup_pickup and "pickup_only" or "combat"
        local proposal = PlatformCombatRuntime.decide({
            mode = mode,
            map = map,
            actor = before.actor,
            world = before.world,
            cfg = cfg,
            state = loop_state
        })

        local actor_loc = proposal.actor_loc
        local platform_mob_count = #(proposal.candidates or {})
        if loop_state.cleanup_pickup and actor_loc and platform_mob_count > clear_remaining_threshold then
            loop_state.cleanup_pickup = false
            loop_state.empty_drop_scans = 0
            loop_state.pickup_sweep = nil
            ctx.output(string.format(
                "tick=%d phase_transition pickup->combat reason=platform_mobs_present platform_mobs=%d threshold=%d",
                ticks,
                platform_mob_count,
                clear_remaining_threshold
            ))
            mode = "combat"
            proposal = PlatformCombatRuntime.decide({
                mode = mode,
                map = map,
                actor = before.actor,
                world = before.world,
                cfg = cfg,
                state = loop_state
            })
            actor_loc = proposal.actor_loc
            platform_mob_count = #(proposal.candidates or {})
        end

        if not loop_state.cleanup_pickup and actor_loc and platform_mob_count <= clear_remaining_threshold then
            loop_state.cleanup_pickup = true
            loop_state.empty_drop_scans = 0
            ctx.output(string.format(
                "tick=%d phase_transition combat->pickup reason=platform_clear platform_mobs=%d threshold=%d",
                ticks,
                platform_mob_count,
                clear_remaining_threshold
            ))
            mode = "pickup_only"
            proposal = PlatformCombatRuntime.decide({
                mode = mode,
                map = map,
                actor = before.actor,
                world = before.world,
                cfg = cfg,
                state = loop_state
            })
            actor_loc = proposal.actor_loc
            platform_mob_count = #(proposal.candidates or {})
        end

        local platform_drop_count = #(proposal.drop_candidates or {})
        local sweep_proposal_active = false
        if loop_state.cleanup_pickup and platform_drop_count > 0 then
            loop_state.empty_drop_scans = 0
            loop_state.pickup_sweep = nil
        elseif loop_state.cleanup_pickup and platform_drop_count <= 0 then
            local sweep_proposal = pickup_sweep_proposal(ctx, before.actor, actor_loc, cfg, loop_state)
            if sweep_proposal then
                sweep_proposal.candidates = proposal.candidates or {}
                proposal = sweep_proposal
                sweep_proposal_active = true
            end
        end

        if actor_loc then
            ctx.output(string.format(
                "tick=%d phase=%s actor_platform=%s platform_y=%.3f y_delta=%.3f platform_mobs=%d platform_drops=%d clear_threshold=%d empty_drop_scans=%d/%d",
                ticks,
                loop_state.cleanup_pickup and "pickup" or "combat",
                tostring(actor_loc.platform_id),
                tonumber(actor_loc.platform_y) or 0,
                tonumber(actor_loc.y_delta) or 0,
                platform_mob_count,
                platform_drop_count,
                clear_remaining_threshold,
                tonumber(loop_state.empty_drop_scans) or 0,
                pickup_empty_confirm_ticks
            ))
        else
            ctx.output(string.format("tick=%d actor_platform=none", ticks))
        end

        emit_platform_drop_scan(ctx, ticks, map, before.actor, before.world, cfg, actor_loc)
        ctx.output(string.format("tick=%d %s", ticks, platform_target_text(proposal)))
        if proposal.drop then ctx.output(string.format("tick=%d %s", ticks, platform_drop_text(proposal))) end
        emit_platform_candidates(ctx, ticks, proposal, cfg)
        ctx.output(string.format("tick=%d proposal %s", ticks, platform_proposal_text(proposal)))

        if loop_state.cleanup_pickup then
            if platform_drop_count <= 0 and not sweep_proposal_active and not (loop_state.pickup_sweep and loop_state.pickup_sweep.done ~= true) then
                loop_state.empty_drop_scans = (tonumber(loop_state.empty_drop_scans) or 0) + 1
                ctx.output(string.format(
                    "tick=%d pickup_confirm_empty scans=%d/%d",
                    ticks,
                    loop_state.empty_drop_scans,
                    pickup_empty_confirm_ticks
                ))
                if loop_state.empty_drop_scans >= pickup_empty_confirm_ticks then
                    local sweep = loop_state.pickup_sweep or {}
                    ctx.output(string.format(
                        "tick=%d stop reason=platform_clear_pickup_done platform_mobs=%d platform_drops=%d threshold=%d empty_scans=%d sweep_done=%s sweep_ticks=%s sweep_left=%s sweep_right=%s",
                        ticks,
                        platform_mob_count,
                        platform_drop_count,
                        clear_remaining_threshold,
                        loop_state.empty_drop_scans,
                        tostring(sweep.done == true),
                        tostring(sweep.ticks or ""),
                        tostring(sweep.left or ""),
                        tostring(sweep.right or "")
                    ))
                    break
                end
            end
        end

        local action_ok = run_platform_action(ctx, proposal, cfg, loop_state)
        if not action_ok then ok = false end

        local after = read_combat_state(ctx, string.format("tick=%d after", ticks))
        emit_after_delta(ctx, ticks, before, after, proposal)
        record_pickup_outcome(ctx, ticks, loop_state, cfg, proposal, before, after)
        ctx.output(string.format(
            "tick=%d end ok=%s tick_elapsed_ms=%.3f last_direction=%s",
            ticks,
            tostring(action_ok),
            now_ms() - tick_started,
            tostring(loop_state.last_direction)
        ))
    end

    if loop_state.is_moving then
        ctx.output("cleanup action=StopMove reason=platform_probe_end")
        run_action(ctx, "StopMove", {})
        loop_state.is_moving = false
    end

    ctx.output(string.format("Maple platform combat probe finished ticks=%d ok=%s", ticks, tostring(ok)))
    return {
        ok = ok,
        bb = ctx.bb,
        ticks = ticks,
        state = loop_state,
        diagnostics = ctx.api.last_calls
    }
end

return Probe
