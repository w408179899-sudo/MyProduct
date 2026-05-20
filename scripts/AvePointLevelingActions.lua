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

local function apply_world_map_send_step_defaults(step)
    if type(step) ~= "table" then
        return step
    end

    local button_name = tostring(step.distance_button_name or "")
    local fullname = button_name:lower()
    if fullname:find("worldmapdetail_c.widgettree.worldmapdetailitem.widgettree.sendbtn", 1, true) == nil then
        return step
    end

    if step.hover_capture_client_left == nil then
        step.hover_capture_client_left = 654
    end
    if step.hover_capture_client_top == nil then
        step.hover_capture_client_top = 789
    end
    if step.hover_capture_client_right == nil then
        step.hover_capture_client_right = 790
    end
    if step.hover_capture_client_bottom == nil then
        step.hover_capture_client_bottom = 810
    end
    if step.hover_capture_retry_ms == nil then
        step.hover_capture_retry_ms = 900
    end

    return step
end

function M.make_world_map_send_linear_recipe(key, send_step, entry_action)
    local action = type(entry_action) == "table" and entry_action or {}
    local step = apply_world_map_send_step_defaults(clone_table(send_step))
    local send_recipe_step = clone_table(step)
    send_recipe_step.kind = "call_button_slot"
    send_recipe_step.slot = "task_entry_send"
    send_recipe_step.key = tostring(key or "") .. "_send"
    send_recipe_step.finish_recipe = true
    send_recipe_step.transition_reason = "task_entry_world_map_send"
    send_recipe_step.transition_wait_ms = tonumber(action.transition_wait_ms) or 1800
    send_recipe_step.force_task_call = true
    send_recipe_step.task_pos_reject_extra_ms = 3500

    return {
        key = tostring(key or "") .. "_linear_recipe",
        mode = "task_recipe",
        recipe_type = "linear",
        activation = "entry_action_active",
        entry_action_key = key,
        timeout_ms = math.max(5000, tonumber(action.timeout_ms) or 12000),
        steps = {
            {
                kind = "wait_entry_action_elapsed",
                key = tostring(key or "") .. "_wait_map",
                duration_ms = tonumber(action.map_open_wait_ms) or 900
            },
            {
                kind = "click_fixed_client_point",
                key = tostring(key or "") .. "_select_point",
                label = tostring(key or ""),
                fixed_client_click = true,
                fixed_ratio_x = tonumber(action.center_click_ratio_x) or 0.5,
                fixed_ratio_y = tonumber(action.center_click_ratio_y) or 0.5,
                fixed_prefer_ratio = true,
                prefer_screen_click = true,
                mouse_mode = tostring(action.center_mouse_mode or action.mouse_mode or "api"),
                click_button = tostring(action.center_click_button or action.click_button or "left"),
                click_delay_ms = tonumber(action.center_click_delay_ms or action.click_delay_ms) or 50,
                hover_delay_ms = tonumber(action.center_hover_delay_ms or action.hover_delay_ms) or 80,
                allow_outside = action.center_allow_outside == true,
                settle_ms = math.max(150, tonumber(action.center_settle_ms) or 450)
            },
            send_recipe_step
        },
        success = {
            mode = "none"
        }
    }
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
    local normalized_step = apply_world_map_send_step_defaults(clone_table(step))
    local enable_linear_recipe = type(extra_task) == "table" and extra_task.enable_linear_recipe == true
    local task_cfg = {
        recipe = {
            key = tostring(key or "") .. "_recipe",
            mode = "task_recipe",
            recipe_type = "compat_entry_action",
            activation = "entry_action_active",
            entry_action_key = key,
            steps = {
                {
                    kind = "compat_entry_action",
                    mode = "world_map_send",
                    action_key = key
                }
            },
            success = {
                mode = "task_info_changed",
                vacuum_ms = 5000,
                settle_ms = 1200
            }
        },
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
            step = normalized_step
        }
    }
    merge_into(task_cfg.entry_action, extra_action)
    if enable_linear_recipe and type(extra_task.recipe) ~= "table" then
        task_cfg.recipe = M.make_world_map_send_linear_recipe(key, normalized_step, task_cfg.entry_action)
    end
    merge_into(task_cfg, extra_task)
    task_cfg.enable_linear_recipe = nil
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

function M.make_maintenance_locator_step(opts)
    local step = {
        hint_max_distance = 80,
        wait_after_ms = 650
    }
    merge_into(step, opts)
    return step
end

function M.make_maintenance_fixed_click_step(opts)
    local step = M.make_fixed_client_click_step({
        fixed_prefer_ratio = true,
        click_delay = 60,
        hover_delay_ms = 90,
        wait_after_ms = 500
    })
    merge_into(step, opts)
    return step
end

function M.make_maintenance_open_menu_step(key, label, extra)
    local step = M.make_maintenance_locator_step({
        key = key,
        label = label or "open maintenance menu",
        include_patterns = {
            "UIButton Transient.GameEngine.CoreGameInstance.FastEntranceView_C.WidgetTree.IconTlBtn"
        },
        hint_client_x = 1383.688110,
        hint_client_y = 52.706509,
        hint_ratio_x = 0.961562,
        hint_ratio_y = 0.058563,
        hint_max_distance = 100,
        wait_after_ms = 800
    })
    merge_into(step, extra)
    return step
end

function M.make_contract_panel_step(key, label, extra)
    local step = M.make_maintenance_locator_step({
        key = key,
        label = label or "open contract panel",
        include_patterns = {
            "UIButton Transient.GameEngine.CoreGameInstance.HomeBtnItem_C.WidgetTree.ClickBtn"
        },
        hint_client_x = 1200.015381,
        hint_client_y = 235.364059,
        hint_ratio_x = 0.833923,
        hint_ratio_y = 0.261516,
        hint_max_distance = 80,
        wait_after_ms = 1000
    })
    merge_into(step, extra)
    return step
end

function M.make_contract_back_click_step(key, label, extra)
    local step = M.make_maintenance_fixed_click_step({
        key = key,
        label = label or "contract back",
        fixed_client_x = 53.00,
        fixed_client_y = 35.00,
        fixed_ratio_x = 0.036831,
        fixed_ratio_y = 0.038889,
        wait_after_ms = 700
    })
    merge_into(step, extra)
    return step
end

local function contract_fixed_click(key, label, x, y, rx, ry, wait_after_ms)
    return M.make_maintenance_fixed_click_step({
        key = key,
        label = label,
        fixed_client_x = x,
        fixed_client_y = y,
        fixed_ratio_x = rx,
        fixed_ratio_y = ry,
        wait_after_ms = wait_after_ms
    })
end

local function contract_point_entry_step(key, label, extra)
    local step = M.make_maintenance_locator_step({
        key = key,
        label = label or "contract point entry",
        distance_button_name = "UIButton Transient.GameEngine.CoreGameInstance.Pet_C.WidgetTree.ServantEquipSlot.WidgetTree.ContractPointEntryBtn",
        include_patterns = {
            "UIButton Transient.GameEngine.CoreGameInstance.Pet_C.WidgetTree.ServantEquipSlot.WidgetTree.ContractPointEntryBtn"
        },
        hint_client_x = 273.851624,
        hint_client_y = 220.836639,
        hint_ratio_x = 0.190307,
        hint_ratio_y = 0.245374,
        hint_max_distance = 90,
        wait_after_ms = 900
    })
    merge_into(step, extra)
    return step
end

local function contract_auto_add_step(key, label, extra)
    local step = M.make_maintenance_locator_step({
        key = key,
        label = label or "contract auto add",
        distance_anchor_exact_text = "自动加点",
        distance_button_name = "UIButton Transient.GameEngine.CoreGameInstance.ContractPoint_C.WidgetTree.AutoAddPointBtn",
        distance_min = 5.235556,
        distance_max = 6.235556,
        include_patterns = {
            "UIButton Transient.GameEngine.CoreGameInstance.ContractPoint_C.WidgetTree.AutoAddPointBtn"
        },
        hint_client_x = 1263.539795,
        hint_client_y = 829.802856,
        hint_ratio_x = 0.878068,
        hint_ratio_y = 0.922003,
        hint_max_distance = 90,
        wait_after_ms = 900
    })
    merge_into(step, extra)
    return step
end

local function contract_point_back_step(key, label, extra)
    local step = M.make_maintenance_locator_step({
        key = key,
        label = label or "contract point back",
        distance_button_name = "UIButton Transient.GameEngine.CoreGameInstance.ContractPoint_C.WidgetTree.UITitleItem.WidgetTree.BackBtn",
        include_patterns = {
            "UIButton Transient.GameEngine.CoreGameInstance.ContractPoint_C.WidgetTree.UITitleItem.WidgetTree.BackBtn"
        },
        hint_client_x = 23.411688,
        hint_client_y = 39.000000,
        hint_ratio_x = 0.016269,
        hint_ratio_y = 0.043333,
        hint_max_distance = 80,
        wait_after_ms = 700
    })
    merge_into(step, extra)
    return step
end

local function contract_pet_back_step(key, label, extra)
    local step = M.make_maintenance_locator_step({
        key = key,
        label = label or "pet panel back",
        distance_button_name = "UIButton Transient.GameEngine.CoreGameInstance.Pet_C.WidgetTree.UITitleItem.WidgetTree.BackBtn",
        include_patterns = {
            "UIButton Transient.GameEngine.CoreGameInstance.Pet_C.WidgetTree.UITitleItem.WidgetTree.BackBtn"
        },
        hint_client_x = 23.411688,
        hint_client_y = 39.000000,
        hint_ratio_x = 0.016269,
        hint_ratio_y = 0.043333,
        hint_max_distance = 80,
        wait_after_ms = 700
    })
    merge_into(step, extra)
    return step
end

local function numbered_label(prefix, index)
    if prefix == nil then
        return nil
    end
    return tostring(prefix) .. tostring(index)
end

local function append_contract_auto_add_steps(steps, level, opts, key_suffix)
    local prefix = "level_" .. tostring(level) .. "_contract_" .. tostring(key_suffix or "auto_add")
    local label_prefix = tostring(level) .. "级契灵"
    steps[#steps + 1] = contract_point_entry_step(
        prefix .. "_entry",
        label_prefix .. "契约点入口按钮",
        opts.entry_extra
    )
    steps[#steps + 1] = contract_auto_add_step(
        prefix .. "_auto_add",
        label_prefix .. "自动加点按钮",
        opts.auto_add_extra
    )
    steps[#steps + 1] = contract_point_back_step(
        prefix .. "_contract_back",
        label_prefix .. "契约点返回按钮",
        opts.contract_back_extra or opts.back_extra
    )
    steps[#steps + 1] = contract_pet_back_step(
        prefix .. "_pet_back",
        label_prefix .. "契灵返回按钮",
        opts.pet_back_extra or opts.back_extra
    )
end

local function contract_plan_base(opts)
    local level = tostring(type(opts) == "table" and opts.level or "")
    return {
        key = tostring(type(opts) == "table" and opts.key or ""),
        label = tostring(type(opts) == "table" and opts.label or ""),
        require_available_points = false,
        monster_guard_distance = 160,
        safe_no_monster_ms = 600,
        close_with_escape = false,
        steps = {}
    }, level
end

function M.make_contract_second_setup_plan(opts)
    opts = type(opts) == "table" and opts or {}
    local plan, level = contract_plan_base(opts)
    plan.key = opts.key or ("level_" .. level .. "_contract_second_setup")
    plan.label = opts.label or ("level " .. level .. " contract second setup")
    merge_into(plan, opts.extra_plan)

    plan.steps = {
        M.make_maintenance_open_menu_step("level_" .. level .. "_contract_open_menu", opts.open_menu_label, opts.open_menu_extra),
        M.make_contract_panel_step("level_" .. level .. "_contract_open_panel", opts.open_panel_label, opts.open_panel_extra)
    }
    append_contract_auto_add_steps(plan.steps, level, opts, "second")
    return plan
end

function M.make_contract_initial_and_second_setup_plan(opts)
    opts = type(opts) == "table" and opts or {}
    local plan, level = contract_plan_base(opts)
    plan.key = opts.key or ("level_" .. level .. "_contract_setup")
    plan.label = opts.label or ("level " .. level .. " contract setup")
    merge_into(plan, opts.extra_plan)

    plan.steps = {
        M.make_maintenance_open_menu_step("level_" .. level .. "_contract_open_menu_first", opts.open_menu_label, opts.open_menu_extra),
        M.make_contract_panel_step("level_" .. level .. "_contract_open_panel_first", opts.open_panel_label, opts.open_panel_extra),
        contract_fixed_click("level_" .. level .. "_contract_first_click_106_376", opts.first_sequence_labels and opts.first_sequence_labels[1] or numbered_label(opts.click_label_prefix, 1), 106.00, 376.00, 0.073662, 0.417778, 450),
        contract_fixed_click("level_" .. level .. "_contract_first_click_295_121", opts.first_sequence_labels and opts.first_sequence_labels[2] or numbered_label(opts.click_label_prefix, 2), 295.00, 121.00, 0.205003, 0.134444, 450),
        contract_fixed_click("level_" .. level .. "_contract_first_click_281_253", opts.first_sequence_labels and opts.first_sequence_labels[3] or numbered_label(opts.click_label_prefix, 3), 281.00, 253.00, 0.195274, 0.281111, 450),
        contract_fixed_click("level_" .. level .. "_contract_first_click_103_226", opts.first_sequence_labels and opts.first_sequence_labels[4] or numbered_label(opts.click_label_prefix, 4), 103.00, 226.00, 0.071577, 0.251111, 450),
        contract_fixed_click("level_" .. level .. "_contract_first_click_1340_845", opts.first_sequence_labels and opts.first_sequence_labels[5] or numbered_label(opts.click_label_prefix, 5), 1340.00, 845.00, 0.931202, 0.938889, 700),
        M.make_contract_back_click_step("level_" .. level .. "_contract_back_first_1", opts.back_label, opts.back_extra),
        M.make_contract_back_click_step("level_" .. level .. "_contract_back_first_2", opts.back_label, opts.back_extra),
        M.make_maintenance_open_menu_step("level_" .. level .. "_contract_open_menu_second", opts.open_menu_label, opts.open_menu_extra),
        M.make_contract_panel_step("level_" .. level .. "_contract_open_panel_second", opts.open_panel_label, opts.open_panel_extra)
    }
    append_contract_auto_add_steps(plan.steps, level, opts, "second")
    return plan
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

local function append_issue(issues, message)
    issues[#issues + 1] = tostring(message or "")
end

local function is_non_empty_array(value)
    return type(value) == "table" and type(value[1]) ~= "nil"
end

local function validate_steps(issues, scope, steps)
    if type(steps) ~= "table" then
        append_issue(issues, scope .. ": missing steps")
        return
    end

    local seen = {}
    for index, step in ipairs(steps) do
        if type(step) ~= "table" then
            append_issue(issues, scope .. ": step " .. tostring(index) .. " is not a table")
        else
            local key = tostring(step.key or "")
            if key == "" then
                append_issue(issues, scope .. ": step " .. tostring(index) .. " missing key")
            elseif seen[key] then
                append_issue(issues, scope .. ": duplicate step key " .. key)
            else
                seen[key] = true
            end

            local has_executor = step.kind ~= nil
                or step.image_preset ~= nil
                or step.fixed_client_click == true
                or is_non_empty_array(step.include_patterns)
                or step.distance_button_name ~= nil
            if not has_executor then
                append_issue(issues, scope .. ": step " .. tostring(key ~= "" and key or index) .. " has no executor locator")
            end

            if step.fixed_client_click == true then
                local has_point = tonumber(step.fixed_client_x) ~= nil and tonumber(step.fixed_client_y) ~= nil
                local has_ratio = tonumber(step.fixed_ratio_x) ~= nil and tonumber(step.fixed_ratio_y) ~= nil
                if not has_point and not has_ratio then
                    append_issue(issues, scope .. ": fixed click step " .. tostring(key) .. " missing point/ratio")
                end
            end

            if type(step.image_preset) == "table" and tostring(step.image_preset.template_path or "") == "skill_level_up.bmp" then
                local threshold = tonumber(step.image_preset.template_threshold)
                if threshold == nil or threshold > 0.85 then
                    append_issue(issues, scope .. ": skill_level_up threshold must be <= 0.85 at " .. tostring(key))
                end
            end
        end
    end
end

local function validate_plan_table(issues, kind, plans)
    if type(plans) ~= "table" then
        return
    end
    local seen_plan_keys = {}
    for level, plan in pairs(plans) do
        local scope = kind .. "[" .. tostring(level) .. "]"
        if type(plan) ~= "table" then
            append_issue(issues, scope .. ": plan is not a table")
        else
            local key = tostring(plan.key or "")
            if key == "" then
                append_issue(issues, scope .. ": missing plan key")
            elseif seen_plan_keys[key] then
                append_issue(issues, scope .. ": duplicate plan key " .. key)
            else
                seen_plan_keys[key] = true
            end
            if plan.close_with_escape == true then
                append_issue(issues, scope .. ": close_with_escape is not allowed for maintenance plans")
            end
            validate_steps(issues, scope, plan.steps)
        end
    end
end

local function validate_route_actions(issues, actions)
    if type(actions) ~= "table" then
        return
    end
    local seen = {}
    for index, action in ipairs(actions) do
        local scope = "route_action[" .. tostring(index) .. "]"
        if type(action) ~= "table" then
            append_issue(issues, scope .. ": not a table")
        else
            local key = tostring(action.key or "")
            if key == "" then
                append_issue(issues, scope .. ": missing key")
            elseif seen[key] then
                append_issue(issues, scope .. ": duplicate key " .. key)
            else
                seen[key] = true
                scope = "route_action[" .. key .. "]"
            end

            if action.allow_without_task_target == true
                and type(action.task_patterns) ~= "table"
                and type(action.task_detail_patterns) ~= "table"
            then
                append_issue(issues, scope .. ": allow_without_task_target requires task/detail patterns")
            end

            local trigger = type(action.trigger) == "table" and action.trigger or action
            local has_trigger = tonumber(trigger.x) ~= nil and tonumber(trigger.y) ~= nil
            if not has_trigger then
                append_issue(issues, scope .. ": missing trigger x/y")
            end
        end
    end
end

local function validate_task_configs(issues, task_configs)
    if type(task_configs) ~= "table" then
        return
    end
    for name, cfg in pairs(task_configs) do
        if type(cfg) == "table" and type(cfg.objective) == "table" and cfg.objective.mode == "boss_kite" then
            if tostring(cfg.objective.key or "") == "" then
                append_issue(issues, "task_config[" .. tostring(name) .. "]: boss_kite missing objective key")
            end
            if cfg.objective.allow_nearby_text_task_change_exit == true
                and type(cfg.objective.nearby_text_task_change_exit_patterns) ~= "table"
            then
                append_issue(issues, "task_config[" .. tostring(name) .. "]: nearby text exit needs explicit patterns")
            end
        end
    end
end

local function validate_treasure_configs(issues, treasure_configs)
    if type(treasure_configs) ~= "table" then
        return
    end
    for key, cfg in pairs(treasure_configs) do
        if type(cfg) == "table" and type(cfg.boss) == "table" and cfg.boss.loot_enabled ~= false then
            local max_pulses = tonumber(cfg.boss.loot_max_pulses) or tonumber(cfg.boss_loot_max_pulses) or 2
            if max_pulses > 2 then
                append_issue(issues, "treasure[" .. tostring(key) .. "]: boss loot max pulses must stay <= 2")
            end
        end
    end
end

function M.validate_leveling_config(config)
    local issues = {}
    if type(config) ~= "table" then
        return false, { "config is not a table" }
    end

    local maintenance = type(config.LEVEL_UP_MAINTENANCE_CONFIG) == "table"
        and config.LEVEL_UP_MAINTENANCE_CONFIG
        or {}
    validate_plan_table(issues, "skill_by_level", maintenance.skill_by_level)
    if type(maintenance.default_skill_plan) == "table" then
        validate_steps(issues, "default_skill_plan", maintenance.default_skill_plan.steps)
    end
    validate_plan_table(issues, "talent_by_level", maintenance.talent_by_level)
    validate_plan_table(issues, "contract_by_level", maintenance.contract_by_level)
    validate_route_actions(issues, config.ROUTE_POINT_ACTIONS)
    validate_task_configs(issues, config.TASK_NAME_CONFIGS)
    validate_treasure_configs(issues, config.TREASURE_DUNGEON_CONFIGS)

    return #issues == 0, issues
end

return M
