local Blackboard = {}

local function now()
    if os and os.time then return os.time() end
    return 0
end

function Blackboard.new(opts)
    opts = opts or {}
    return {
        meta = {
            schema_version = "1.0.0",
            project = "MapleStory",
            account_index = tonumber(opts.account_index) or 0,
            account_key = tostring(opts.account_key or ""),
            worker_task_id = opts.worker_task_id
        },
        runtime = {
            running = true,
            paused = false,
            tick = 0,
            started_at = now(),
            last_error = nil,
            stop_requested = false
        },
        actor = {
            level = 1,
            hp = 100,
            max_hp = 100,
            mp = 100,
            max_mp = 100,
            is_dead = false,
            is_in_combat = false,
            position = { x = 0, y = 0, z = 0 },
            current_map = nil
        },
        quest = {
            active = {},
            completed = {},
            current_quest_id = nil,
            current_objective_index = 1,
            objective_failure_count = 0
        },
        inventory = {
            used_slots = 0,
            max_slots = 100,
            items = {},
            is_full = false,
            has_required_items = false
        },
        equipment = {
            current = {},
            candidates = {},
            upgrade_available = false,
            durability_low = false
        },
        skill = {
            learned = {},
            available = {},
            should_learn = false,
            trainer_known = false
        },
        navigation = {
            route = {},
            destination = nil,
            current_waypoint_index = 1,
            is_moving = false,
            is_stuck = false,
            stuck_ticks = 0,
            stuck_count = 0,
            last_position = nil
        },
        world = {
            nearby_npcs = {},
            nearby_targets = {},
            nearby_resources = {},
            selected_entity = nil
        },
        combat = {
            logic_mode = opts.combat_logic_mode,
            last_proposal = nil,
            last_decision = nil,
            prediction_horizon_seconds = nil,
            candidate_count = 0,
            last_fallback_reason = nil
        },
        account = opts.account or {},
        task = {
            previous_goal = nil,
            active_goal = nil,
            active_action = nil,
            action_id = nil,
            failure_count = 0,
            last_result = nil,
            last_goal_switch_tick = -999999
        },
        action_queue = {},
        safety = {
            stop_reason = nil,
            last_trigger = nil,
            circuit_breaker_open = false
        },
        debug = {
            enabled = true,
            last_node = nil,
            last_branch = nil,
            last_action = nil,
            last_api_call = nil
        },
        metrics = {
            tick_count = 0,
            goal_change_count = 0,
            action_success_count = 0,
            action_failure_count = 0,
            action_timeout_count = 0,
            safety_trigger_count = 0,
            average_tick_time = 0,
            current_action_queue_size = 0,
            combat_degradation_count = 0,
            perception_refresh_count = 0,
            api_call_count = 0,
            api_error_count = 0,
            latest_api_latency_ms = 0
        }
    }
end

return Blackboard
