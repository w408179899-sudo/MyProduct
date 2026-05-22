local M = {}

M.PRIORITY = {
    runtime_guard = 10,
    recovery = 20,
    startup = 25,
    interaction = 30,
    post_loot = 40,
    maintenance_active = 50,
    maintenance_pending = 60,
    treasure = 70,
    task_combat = 75,
    task_intent = 80,
    task_info = 90,
    task_path = 100,
    task_follow = 110,
    combat_sidecar = 120,
    idle = 900
}

M.SELECTOR_OWNERS = {
    ["runtime.guard.nav"] = "runtime_guard",
    ["runtime.guard.loading"] = "runtime_guard",
    ["runtime.guard.position"] = "runtime_guard",
    ["recovery.critical"] = "recovery",
    ["recovery.loading_transition"] = "recovery",
    ["recovery.revive_reentry_transition"] = "recovery",
    ["interaction.hard_flow"] = "interaction",
    ["interaction.hard_flow.treasure_gate"] = "interaction",
    ["interaction.hard_flow.pre_main_gate"] = "interaction",
    ["interaction.hard_flow.main_gate"] = "interaction",
    ["startup.resolve"] = "startup",
    ["task.intent"] = "task",
    ["task.info_runtime_gate"] = "task",
    ["maintenance.priority"] = "post_loot",
    ["maintenance.level_up.priority"] = "level_up",
    ["task.path_acquire"] = "task",
    ["task.path_wait"] = "task",
    ["task.no_target_wait"] = "task",
    ["task.combat_active"] = "task_combat",
    ["task.combat_completion"] = "task_combat",
    ["task.active_target_intent"] = "task",
    ["task.reached_action"] = "task",
    ["task.follow_precheck"] = "task",
    ["task.follow"] = "task"
}

local function text(value)
    if value == nil then
        return ""
    end
    return tostring(value)
end

local function bool(value)
    return value == true
end

local function num(value)
    return tonumber(value) or 0
end

local function table_field(root, key)
    if type(root) ~= "table" then
        return {}
    end
    local value = root[key]
    if type(value) ~= "table" then
        return {}
    end
    return value
end

local function choose(priority, owner, node, reason, opts)
    opts = type(opts) == "table" and opts or {}
    local allowed_owners = opts.allowed_owners
    if type(allowed_owners) ~= "table" then
        allowed_owners = M.allowed_owners_for(owner, opts)
    end
    return {
        priority = priority,
        owner = owner,
        node = node,
        reason = reason,
        allowed_owners = allowed_owners,
        blocks_main_task = opts.blocks_main_task ~= false,
        allows_combat_sidecar = opts.allows_combat_sidecar == true,
        diagnostic_only = true
    }
end

function M.allowed_owners_for(owner, opts)
    owner = text(owner)
    opts = type(opts) == "table" and opts or {}

    local allowed = {}
    local function add(value)
        value = text(value)
        if value ~= "" then
            allowed[value] = true
        end
    end

    add(owner)
    if owner == "task" then
        add("task_info")
        add("task_path")
        add("task_follow")
        add("task_intent")
        add("task_reached")
        add("task_no_target")
        add("task_combat")
        add("ui_low_priority")
    elseif owner == "post_loot" then
        add("recycle")
        add("auto_equip")
    elseif owner == "startup" then
        add("treasure")
    elseif owner == "interaction" then
        add("task_intent")
    elseif owner == "runtime_guard" then
        add("recovery")
    elseif owner == "combat" then
        add("task_combat")
    elseif owner == "task_combat" then
        add("combat")
    end

    if opts.allows_combat_sidecar == true then
        add("combat")
        add("task_combat")
    end
    return allowed
end

function M.owner_for_selector_key(key, children)
    key = text(key)
    local mapped = text(M.SELECTOR_OWNERS[key])
    if mapped ~= "" then
        return mapped, "selector_key"
    end

    if type(children) == "table" then
        for _, child in ipairs(children) do
            if type(child) == "table" then
                local owner = text(child.owner)
                if owner ~= "" then
                    return owner, "first_child"
                end
            end
        end
    end

    return "", "ownerless"
end

function M.allows(scheduler, actual_owner)
    scheduler = type(scheduler) == "table" and scheduler or {}
    actual_owner = text(actual_owner)
    if actual_owner == "" then
        return true, "ownerless"
    end

    if actual_owner == "runtime_guard" or actual_owner == "recovery" then
        return true, "preemptive_owner"
    end

    local scheduler_owner = text(scheduler.owner)
    if scheduler_owner == "" or scheduler_owner == "idle" then
        return true, "scheduler_idle"
    end

    local allowed = type(scheduler.allowed_owners) == "table"
        and scheduler.allowed_owners
        or M.allowed_owners_for(scheduler_owner, scheduler)
    if allowed[actual_owner] == true then
        return true, "allowed_owner"
    end

    if scheduler.allows_combat_sidecar == true
        and (actual_owner == "combat" or actual_owner == "task_combat")
    then
        return true, "allowed_combat_sidecar"
    end

    return false, "scheduler_owner:" .. scheduler_owner .. ":actual_owner:" .. actual_owner
end

local function has_wait_until(value, now)
    return num(value) > num(now)
end

local function stage_is_any(stage, values)
    stage = text(stage)
    if stage == "" or type(values) ~= "table" then
        return false
    end
    for _, value in ipairs(values) do
        if stage == value then
            return true
        end
    end
    return false
end

function M.evaluate(blackboard)
    blackboard = type(blackboard) == "table" and blackboard or {}

    local now = num(blackboard.now or blackboard.current_time)
    local stage = text(blackboard.stage)
    local ui = table_field(blackboard, "ui")
    local player = table_field(blackboard, "player")
    local flow = table_field(blackboard, "flow")
    local maintenance = table_field(blackboard, "maintenance")
    local treasure = table_field(blackboard, "treasure")
    local task = table_field(blackboard, "task")
    local combat = table_field(blackboard, "combat")

    if bool(ui.loading) then
        return choose(M.PRIORITY.runtime_guard, "runtime_guard", "runtime.guard.loading", "loading")
    end

    if bool(flow.running) and bool(player.has_pos) == false then
        return choose(M.PRIORITY.runtime_guard, "runtime_guard", "runtime.guard.position", "player_position_missing")
    end

    if bool(flow.revive_reentry_pending) then
        return choose(M.PRIORITY.recovery, "recovery", "recovery.revive_reentry", "revive_reentry_pending")
    end

    if not bool(treasure.active) and (bool(combat.force_kite) or stage_is_any(stage, {
            "task_combat",
            "task_combat_kite",
            "task_combat_settle",
            "task_combat_complete_settle",
            "boss_kite",
            "combat_loop_until_task_change"
        }))
    then
        return choose(M.PRIORITY.task_combat, "task_combat", "task.combat_active", "task_combat_active", {
            allows_combat_sidecar = true
        })
    end

    if text(flow.pending_interaction_origin) ~= "" then
        return choose(M.PRIORITY.interaction, "interaction", "interaction.pending", text(flow.pending_interaction_origin))
    end

    if text(flow.post_dialogue_flow_key) ~= "" then
        return choose(M.PRIORITY.interaction, "interaction", "interaction.post_dialogue", text(flow.post_dialogue_flow_key))
    end

    if has_wait_until(flow.dialogue_jump_window_until, now) then
        return choose(M.PRIORITY.interaction, "interaction", "interaction.dialogue_jump", "dialogue_jump_window")
    end

    if text(flow.route_point_action_key) ~= "" then
        return choose(M.PRIORITY.interaction, "interaction", "interaction.route_point_action", text(flow.route_point_action_key))
    end

    if has_wait_until(flow.startup_until, now) then
        return choose(M.PRIORITY.startup, "startup", "startup.resolve", "startup_window", {
            allows_combat_sidecar = true
        })
    end

    if stage_is_any(stage, {
        "post_combat_loot",
        "post_combat_loot_finished",
        "post_combat_loot_maintenance",
        "auto_equip_maintenance",
        "recycle_maintenance"
    }) or bool(maintenance.post_combat_loot_active)
        or bool(maintenance.after_loot_pending)
        or bool(maintenance.auto_equip_pending)
        or bool(maintenance.recycle_pending)
    then
        return choose(M.PRIORITY.post_loot, "post_loot", "maintenance.after_loot", "post_loot_or_bag_pending")
    end

    if bool(maintenance.level_up_executor_active)
        or stage_is_any(stage, {
            "level_up_maintenance",
            "level_up_maintenance_wait_safe"
        })
    then
        local kind = text(maintenance.level_up_executor_kind)
        local level = num(maintenance.level_up_executor_level)
        local reason = kind ~= "" and string.format("%s:%s", kind, tostring(level)) or "executor_active"
        return choose(M.PRIORITY.maintenance_active, "level_up", "maintenance.level_up.executor", reason)
    end

    if bool(maintenance.level_up_pending) then
        return choose(M.PRIORITY.maintenance_pending, "level_up", "maintenance.level_up.pending", "level_up_pending")
    end

    if bool(treasure.active) then
        local reason = text(treasure.active_key)
        if reason == "" then
            reason = text(treasure.stage)
        end
        return choose(M.PRIORITY.treasure, "treasure", "treasure.runtime", reason ~= "" and reason or "active")
    end

    if bool(task.info_gate_active) or has_wait_until(task.update_wait_until, now) then
        local reason = text(task.info_gate_reason)
        if reason == "" then
            reason = "task_info_wait"
        end
        return choose(M.PRIORITY.task_info, "task", "task.info_runtime_gate", reason, {
            allows_combat_sidecar = true
        })
    end

    if bool(task.require_button_refresh) then
        local reason = text(task.require_button_refresh_reason)
        return choose(M.PRIORITY.task_path, "task", "task.path_acquire.refresh_button",
            reason ~= "" and reason or "require_button_refresh", {
                allows_combat_sidecar = true
            })
    end

    if bool(task.waiting_for_path) or has_wait_until(task.path_wait_until, now) then
        return choose(M.PRIORITY.task_path, "task", "task.path_wait", "waiting_for_path", {
            allows_combat_sidecar = true
        })
    end

    if bool(task.has_target) then
        local source = text(task.target_source)
        return choose(M.PRIORITY.task_follow, "task", "task.follow", source ~= "" and source or "target", {
            allows_combat_sidecar = true
        })
    end

    return choose(M.PRIORITY.idle, "idle", "idle", "no_owner", {
        blocks_main_task = false,
        allows_combat_sidecar = true
    })
end

return M
