local M = {}

local function clone_table(value)
    if type(value) ~= "table" then
        return value
    end
    local out = {}
    for k, v in pairs(value) do
        out[k] = clone_table(v)
    end
    return out
end

local function merge_into(dst, src)
    if type(src) ~= "table" then
        return dst
    end
    for k, v in pairs(src) do
        if type(v) == "table" then
            dst[k] = clone_table(v)
        else
            dst[k] = v
        end
    end
    return dst
end

-- Action builder: task-level boss handling.
function M.make_boss_kite_task(key, extra_objective, extra_task)
    local task_cfg = {
        objective = {
            key = key,
            mode = "boss_kite",
            trigger_distance = 320,
            skip_direct_interact = true,
            allow_any_monster = true,
            force_kite = true
        }
    }
    merge_into(task_cfg.objective, extra_objective)
    merge_into(task_cfg, extra_task)
    return task_cfg
end

-- Action builder: task entry that opens world map and clicks send.
function M.make_world_map_send_task(key, step, extra_action, extra_task)
    local task_cfg = {
        entry_action = {
            key = key,
            mode = "world_map_send",
            map_open_wait_ms = 900,
            center_click_ratio_x = 0.5,
            center_click_ratio_y = 0.5,
            center_settle_ms = 450,
            center_retry_ms = 1200,
            transition_wait_ms = 1800,
            timeout_ms = 12000,
            step = clone_table(step)
        }
    }
    merge_into(task_cfg.entry_action, extra_action)
    merge_into(task_cfg, extra_task)
    return task_cfg
end

-- Task dialogue flow: while an interaction/dialogue is pending, execute one
-- or more locator-button clicks before allowing the normal jump/skip path.
function M.make_dialogue_locator_flow_task(key, steps, extra_flow, extra_task)
    local normalized_steps = {}
    if type(steps) == "table" then
        if type(steps[1]) == "table" then
            for _, step in ipairs(steps) do
                normalized_steps[#normalized_steps + 1] = clone_table(step)
            end
        else
            normalized_steps[1] = clone_table(steps)
        end
    end

    local task_cfg = {
        dialogue_flow = {
            key = key,
            mode = "pre_jump_steps",
            steps = normalized_steps
        }
    }
    merge_into(task_cfg.dialogue_flow, extra_flow)
    merge_into(task_cfg, extra_task)
    return task_cfg
end

-- Fixed client-coordinate click. This is an action step: it does not scan UI
-- controls, and runtime executes it through human_mouse.move_and_click.
function M.make_fixed_client_click_step(opts)
    local step = {
        fixed_client_click = true,
        prefer_screen_click = true,
        mouse_mode = "api",
        click_button = "left",
        hover_delay_ms = 80,
        retry_ms = 500,
        settle_ms = 800
    }
    merge_into(step, opts)
    return step
end

-- Post-dialogue action flow: armed only after the normal dialogue JumpBtn
-- succeeds for the matched task.
function M.make_post_dialogue_flow_task(key, steps, extra_flow, extra_task)
    local normalized_steps = {}
    if type(steps) == "table" then
        if type(steps[1]) == "table" then
            for _, step in ipairs(steps) do
                normalized_steps[#normalized_steps + 1] = clone_table(step)
            end
        else
            normalized_steps[1] = clone_table(steps)
        end
    end

    local task_cfg = {
        post_dialogue_flow = {
            key = key,
            mode = "after_jump_steps",
            steps = normalized_steps
        }
    }
    merge_into(task_cfg.post_dialogue_flow, extra_flow)
    merge_into(task_cfg, extra_task)
    return task_cfg
end

-- Generic route-point action. Runtime still supports old flat schema, this
-- builder just standardizes how configs are authored.
function M.make_route_point_action(opts)
    local action = {}
    merge_into(action, opts)
    return action
end

-- Route action: click a lift button, optionally fallback to D, then board
-- center and interact again.
function M.make_lift_route_action(opts)
    local action = {
        mode = "lift_transition",
        retry_ms = 3500,
        settle_ms = 1200,
        fallback_interact = true,
        fallback_interact_distance = 180,
        fallback_retry_ms = 2500
    }
    merge_into(action, opts)
    return action
end

-- Route action: move to a fixed point, then interact with a nearby NPC.
function M.make_npc_dialogue_route_action(opts)
    local action = {
        mode = "npc_dialogue_point",
        retry_ms = 4500,
        dialogue = {
            radius = 220,
            interact_radius = 120,
            move_interval_ms = 220,
            z_tolerance = 260,
            center_settle_ms = 600,
            interact_retry_ms = 1800,
            timeout_ms = 18000,
            npc_search_radius = 420,
            fallback_interact = true
        }
    }
    merge_into(action, opts)
    return action
end

-- Objective point / boss room style spatial trigger.
function M.make_objective_point(opts)
    local point = {
        mode = "boss_kite",
        constraint_mode = "all",
        radius = 520,
        trigger_distance = 520,
        skip_direct_interact = true,
        allow_any_monster = true,
        force_kite = true
    }
    merge_into(point, opts)
    return point
end

-- Objective point: fixed-point clear room / boss room.
-- Enters 4-point kite when destination reaches the room point, and only
-- resumes main task flow after nearby monsters have disappeared for a short
-- settle window.
function M.make_clear_room_point(opts)
    local point = {
        mode = "boss_kite",
        constraint_mode = "all",
        radius = 520,
        trigger_distance = 520,
        skip_direct_interact = true,
        allow_any_monster = true,
        force_kite = true,
        boss_clear_settle_ms = 3000
    }
    merge_into(point, opts)
    return point
end

-- Boss revive reentry: after checkpoint revive, move to a portal point,
-- click PortalBtn (or optionally fallback to interact), then refresh the
-- quest and re-engage the boss room.
function M.make_revive_reentry(opts)
    local cfg = {
        use_global_portal = true,
        interact_distance = 260,
        retry_ms = 1200,
        settle_ms = 1200,
        timeout_ms = 18000,
        post_transition_boss_engage_ms = 15000,
        call_task_before_reentry = false,
        follow_task_path_to_anchor = false,
        fallback_interact = false
    }
    merge_into(cfg, opts)
    return cfg
end

-- Simple set builder for monster-name driven force-kite triggers.
function M.make_force_kite_name_set(names)
    local out = {}
    if type(names) ~= "table" then
        return out
    end
    for _, name in ipairs(names) do
        if type(name) == "string" and name ~= "" then
            out[name] = true
        end
    end
    return out
end

return M
