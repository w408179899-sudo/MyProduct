local Config = {
    tick_interval_ms = 200,
    thresholds = {
        low_hp_percent = 0.30,
        low_mp_percent = 0.25,
        inventory_full_percent = 0.90,
        equipment_score_delta = 5,
        goal_switch_hysteresis_ticks = 5
    },
    limits = {
        max_failures = 5,
        max_stuck_ticks = 100,
        max_stuck_count = 3,
        max_goal_ticks = 1500,
        max_action_retries = 2,
        max_queue_size = 20,
        max_accounts = 20
    },
    timeouts = {
        navigation = 30,
        interaction = 10,
        objective = 120,
        action = 15,
        inventory = 60,
        equipment = 60,
        skill = 60,
        login = 120
    },
    logging = {
        level = "debug",
        print_to_console = true,
        keep_records = 200
    },
    snapshot = {
        enabled = true,
        interval_ticks = 50,
        max_snapshots = 20
    },
    perception = {
        actor_interval_ticks = 1,
        world_interval_ticks = 1,
        quest_interval_ticks = 5,
        inventory_interval_ticks = 10,
        equipment_interval_ticks = 10,
        skill_interval_ticks = 10
    },
    combat = {
        logic_mode = "immediate",
        prediction_horizon_seconds = 2.0,
        prediction_step_seconds = 0.25,
        default_skill_id = "basic_attack",
        default_skill_range_x = 120,
        default_skill_range_y = 50,
        default_skill_windup_seconds = 0.5,
        skill_use_method = "press_key",
        skill_key = "Shift",
        skill_key_code = 0x10,
        skill_input_mode = "foreground",
        key_mode = "api",
        quickslot_use_trusted = false,
        fallback_to_basic_attack = true,
        baseline_run_seconds = 20,
        baseline_max_ticks = 80,
        baseline_tick_ms = 250,
        baseline_move_ms = 220,
        baseline_attack_wait_ms = 750,
        baseline_pick_wait_ms = 250,
        baseline_attack_range_x = 95,
        baseline_attack_range_y = 45,
        baseline_stop_range_x = 65,
        baseline_pursuit_y_tolerance = 70,
        baseline_pickup_enabled = true,
        max_candidate_targets = 8,
        immediate_budget_ms = 1,
        predictive_budget_ms = 5
    }
}

return Config
