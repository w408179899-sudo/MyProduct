local Blackboard = require("maple.blackboard")
local Logger = require("maple.systems.logger")
local MapleApi = require("maple.environment.maple_api")
local MapleEnvironment = require("maple.environment.maple_environment")
local Normalize = require("maple.environment.normalizers")

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

local function run_action(ctx, name, params)
    local result = ctx.env:perform_action({ name = name, params = params or {} }, ctx.bb)
    emit_result(ctx.output, name, result)
    return result
end

local function sleep_ms(ms)
    if sys and sys.sleep then sys.sleep(tonumber(ms) or 0) end
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

return Probe
