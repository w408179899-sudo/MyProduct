local M = {}

local function num(value)
    return tonumber(value)
end

local function text(value)
    if value == nil then
        return ""
    end
    return tostring(value)
end

local function bool(value)
    return value == true
end

local function table_count(value)
    if type(value) ~= "table" then
        return 0
    end
    return #value
end

local function first_non_nil(a, b)
    if a ~= nil then
        return a
    end
    return b
end

local function runtime_owner(state)
    local runtime = type(state.bt_runtime) == "table" and state.bt_runtime or {}
    return runtime.active_owner, runtime.active_node
end

function M.snapshot(state, current_time, opts)
    state = type(state) == "table" and state or {}
    opts = type(opts) == "table" and opts or {}

    local now = num(current_time) or num(opts.current_time) or 0
    local task_target = type(state.task_target) == "table" and state.task_target or nil
    local task_path = type(state.task_path) == "table" and state.task_path or nil
    local treasure_runtime = type(state.treasure_runtime) == "table" and state.treasure_runtime or nil
    local active_owner, active_node = runtime_owner(state)

    local player_x = num(first_non_nil(opts.player_x, state.last_known_player_x))
    local player_y = num(first_non_nil(opts.player_y, state.last_known_player_y))
    local player_z = num(first_non_nil(opts.player_z, state.last_known_player_z))
    local in_main_interface = opts.in_main_interface

    local level_pending =
        bool(state.level_up_maintenance_pending_skill)
        or bool(state.level_up_maintenance_pending_talent)
        or bool(state.level_up_maintenance_pending_contract)

    return {
        current_time = now,
        now = now,
        stage = text(state.stage),
        state = state,
        ctx = opts.ctx,

        owner = {
            active = text(active_owner),
            node = text(active_node),
            last = text(type(state.bt_runtime) == "table" and state.bt_runtime.last_owner or nil),
            last_node = text(type(state.bt_runtime) == "table" and state.bt_runtime.last_node or nil)
        },

        task = {
            name = text(state.current_task_name),
            detail = text(state.current_task_detail),
            log_name = text(opts.task_log_name),
            log_detail = text(opts.task_log_detail),
            target = task_target,
            has_target = task_target ~= nil,
            target_source = text(task_target and task_target.source or nil),
            target_updated_at = num(state.task_target_updated_at) or 0,
            path = task_path,
            path_count = num(state.task_path_count) or table_count(task_path),
            path_raw_count = num(state.task_path_raw_count) or 0,
            path_wait_until = num(state.task_path_wait_until) or 0,
            waiting_for_path = task_target == nil and (num(state.task_path_wait_until) or 0) > now,
            require_button_refresh = bool(state.require_task_button_refresh),
            require_button_refresh_reason = text(state.require_task_button_refresh_reason),
            next_refresh_at = num(state.next_task_refresh_at) or 0,
            next_button_click_at = num(state.next_task_button_click_at) or 0,
            update_wait_until = num(state.task_update_wait_until) or 0,
            info_gate_active = bool(state.task_info_stability_gate_active),
            info_gate_reason = text(state.task_info_stability_gate_reason)
        },

        player = {
            x = player_x,
            y = player_y,
            z = player_z,
            has_pos = player_x ~= nil and player_y ~= nil,
            hp = num(opts.hp),
            max_hp = num(opts.max_hp),
            hp_ratio = num(opts.hp_ratio),
            pos_err = text(opts.pos_err)
        },

        ui = {
            in_main_interface = in_main_interface,
            main_interface_err = text(opts.main_interface_err),
            loading = bool(opts.loading)
        },

        flow = {
            running = bool(state.running),
            startup_until = num(state.startup_state_resolve_until) or 0,
            pending_interaction_origin = text(state.pending_interaction_origin),
            pending_interaction_label = text(state.pending_interaction_label),
            dialogue_jump_window_until = num(state.dialogue_jump_window_until) or 0,
            dialogue_jump_consumed = bool(state.dialogue_jump_consumed),
            post_dialogue_flow_key = text(state.post_dialogue_flow_key),
            route_point_action_key = text(state.route_point_action_active_key),
            route_point_action_state_key = text(state.route_point_action_active_state_key),
            revive_reentry_pending = bool(state.revive_reentry_pending)
        },

        maintenance = {
            post_combat_loot_active = text(state.post_combat_loot_active_key) ~= ""
                or text(state.stage) == "post_combat_loot",
            post_combat_loot_active_key = text(state.post_combat_loot_active_key),
            post_combat_loot_completed_key = text(state.post_combat_loot_completed_key),
            after_loot_pending = bool(state.after_loot_maintenance_pending),
            recycle_pending = bool(state.recycle_maintenance_pending),
            auto_equip_pending = bool(state.auto_equip_maintenance_pending),
            level_up_executor_active = bool(state.level_up_maintenance_executor_active),
            level_up_executor_kind = text(state.level_up_maintenance_executor_kind),
            level_up_executor_level = num(state.level_up_maintenance_executor_level),
            level_up_pending = level_pending,
            level_up_pending_skill = bool(state.level_up_maintenance_pending_skill),
            level_up_pending_talent = bool(state.level_up_maintenance_pending_talent),
            level_up_pending_contract = bool(state.level_up_maintenance_pending_contract),
            level_up_pending_since = num(state.level_up_maintenance_pending_since) or 0,
            level_up_next_retry_at = num(state.level_up_maintenance_next_retry_at) or 0,
            level_up_last_block_reason = text(state.level_up_maintenance_last_block_reason)
        },

        treasure = {
            active = treasure_runtime ~= nil and text(treasure_runtime.active_key or treasure_runtime.stage) ~= "",
            runtime = treasure_runtime,
            active_key = text(treasure_runtime and treasure_runtime.active_key or nil),
            stage = text(treasure_runtime and treasure_runtime.stage or nil),
            route_store_key = text(treasure_runtime and treasure_runtime.route_store_key or nil)
        },

        combat = {
            force_kite = bool(state.task_combat_force_kite),
            stage = text(state.stage),
            last_seen_at = num(state.task_combat_last_seen_at) or 0,
            anchor_x = num(state.task_combat_anchor_x),
            anchor_y = num(state.task_combat_anchor_y),
            pulse_next_at = num(state.next_combat_pulse_at) or 0
        }
    }
end

return M
