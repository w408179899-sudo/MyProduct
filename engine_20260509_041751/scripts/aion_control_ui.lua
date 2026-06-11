--[[
    Aion automation control UI draft.

    This file is mostly UI/config. The route tab can record positions and
    issue bounded waypoint movement through the atomic aion.nav wrapper.

    Hotkeys:
      F1       scan nearby NPC names or auto-click open NPC dialog
      F2       dump full UI list and revive candidate child trees to log
      F3       dump 5 UI controls nearest to the mouse position
      F4       dump server selection API details
      F5       dump nearest corpse candidates
      F6       dump current selected target, character, map, and quest snapshot
      F7       show/hide window
      F8       dump current NPC dialog information
      F9       run safe API probe
      F10      pause/resume
      F11      record task-building snapshot
      Ctrl+F12 exit
]]

local ok_core, core = pcall(require, "aion.core")
local ok_probe, probe = pcall(require, "aion.probe")
local ok_entity, entity = pcall(require, "aion.entity")
local ok_inventory, inventory = pcall(require, "aion.inventory")
local ok_quest, quest = pcall(require, "aion.quest")
local ok_combat, combat = pcall(require, "aion.combat")
ok_target_dump, target_dump = pcall(require, "aion.target_dump")
ok_quest_snapshot, quest_snapshot = pcall(require, "aion.quest_snapshot")
ok_task_recorder, task_recorder = pcall(require, "aion.task_recorder")
ok_main_quest_20590, main_quest_20590 = pcall(require, "aion.main_quest_20590")
ok_main_quest_20610, main_quest_20610 = pcall(require, "aion.main_quest_20610")
ok_main_quest_20611, main_quest_20611 = pcall(require, "aion.main_quest_20611")
ok_main_quest_resume, main_quest_resume = pcall(require, "aion.main_quest_resume")
ok_main_quest_combat_guard, main_quest_combat_guard = pcall(require, "aion.main_quest_combat_guard")
local ok_map, map = pcall(require, "aion.map")
local ok_nav, nav = pcall(require, "aion.nav")
ok_remote, remote = pcall(require, "aion.remote")
local ok_route, route_lib = pcall(require, "aion.route")
local route_load_error = nil
if not ok_route then
    route_load_error = tostring(route_lib)
    route_lib = nil
end
local ok_profile_io, profile_io = pcall(require, "aion.profile_io")
local ok_target, target_lib = pcall(require, "aion.target")
ok_login_autostart, login_autostart = pcall(require, "aion.login_autostart")

local ATTACK_KEYCODE = 67 -- VK_C
local ATTACK_KEY_LABEL = "C"

local runtime = {
    running = false,
    paused = false,
    status = "已停止",
    active_mode = "none",
    last_event = "",
    last_probe = "未运行",
    frame = 0,
    ui_visible = true,
    bootstrap = {
        initialized = false,
        pending = false,
        status = "未初始化",
        reason = "",
        current_step = "",
        step_index = 0,
        step_total = 0,
        last_step_ms = 0,
        last_error = "",
        last_refresh_at = 0,
        core_ok = false,
        character_ok = false,
        map_ok = false,
        inventory_ok = false,
        quest_ok = false,
        combat_ok = false,
        skill_count = 0,
        buff_count = 0,
        auto_active_count = 0,
        auto_buff_count = 0,
        inventory_count = 0,
        quest_count = 0,
        map_name = "",
        target_id = 0,
        skills = {},
        buffs = {},
        auto_active_skills = {},
        auto_buff_skills = {},
        inventory_items = {},
        quests = {},
    },
    audit = {
        started_at = 0,
        last_sample_at = 0,
        last_light_sample_at = 0,
        elapsed_seconds = 0,
        samples = 0,
        kills_est = 0,
        gather_est = 0,
        material_gain = 0,
        exp_gain = 0,
        kinah_gain = 0,
        seen_loot = {},
        last_inventory_counts = nil,
        last_level = nil,
        last_exp = nil,
        last_max_exp = nil,
        last_kinah = nil,
        last_error = "",
        current = {
            name = "",
            race = nil,
            race_name = "",
            job = nil,
            job_name = "",
            hp = 0,
            max_hp = 0,
            mp = 0,
            max_mp = 0,
            level = 0,
            map = "",
            entities = 0,
            inventory = 0,
            quests = 0,
            target_id = 0,
            skills = 0,
            buffs = 0,
            auto_active_skills = 0,
            auto_buff_skills = 0,
        },
    },
    route = {
        recording = false,
        record_field = nil,
        record_name = "",
        last_record_at = 0,
        last_record_pos = nil,
        following = false,
        follow_field = nil,
        follow_name = "",
        points = {},
        index = 1,
        direction = 1,
        moving_to = nil,
        last_move_at = 0,
        laps = 0,
        status = "空闲",
        error = "",
        finish_shows_ui = false,
        test_only = false,
    },
    recovery = {
        active = false,
        phase = "idle",
        reason = "",
        wait_until = 0,
        started_at = 0,
        route_after_return = "revive",
        death_count = 0,
        last_status = "",
        last_action_at = 0,
        last_revive_click_at = 0,
        revive_click_attempts = 0,
        revive_last_error = "",
        death_probe_at = 0,
        death_probe_count = 0,
        death_probe_last_log_at = 0,
        death_probe_reason = "",
    },
    combat = {
        anchor = nil,
        mode = "",
        status = "idle",
        target_obj = 0,
        target_name = "",
        target_distance = 0,
        loot_obj = 0,
        loot_name = "",
        loot_distance = 0,
        loot_attempts = 0,
        loot_ignored = {},
        post_kill_until = 0,
        post_kill_started_at = 0,
        last_killed_obj = 0,
        last_killed_interact_id = 0,
        last_killed_name = "",
        last_auto_off_at = 0,
        last_auto_on_at = 0,
        force_auto_until = 0,
        last_force_auto_at = 0,
        last_attack_key_at = 0,
        last_attack_key_obj = 0,
        target_started_at = 0,
        target_start_hp = 0,
        target_last_hp = 0,
        target_last_damage_at = 0,
        target_ignored = {},
        anchor_distance = 0,
        patrol_points = {},
        patrol_index = 1,
        patrol_direction = 1,
        patrol_laps = 0,
        patrol_route_name = "",
        patrol_signature = "",
        last_tick_at = 0,
        last_move_at = 0,
        last_select_at = 0,
        last_loot_at = 0,
        last_loot_interact_at = 0,
        post_loot_maintenance_pending = false,
        post_loot_maintenance_source = "",
        post_loot_maintenance_at = 0,
        log_times = {},
        last_error = "",
    },
    transfer = {
        last_status = "",
    },
    npc_dialog = {
        last_status = "",
        candidates = {},
        candidate_labels = { "No nearby NPC" },
        selected_index = 1,
        last_scan_text = "",
        dialog_children = {},
        dialog_child_labels = { "No dialog child" },
        selected_child_index = 1,
        last_dialog_dump = "",
    },
    teleport_test = {
        last_status = "",
        big_map_id = 0,
        map_name = "",
        can_teleport = false,
        nodes = {},
        node_labels = { "No map nodes" },
        selected_index = 1,
        node_dump = "",
    },
    ui_test = {
        last_status = "",
        controls = {},
        labels = { "No UI controls" },
        selected_index = 1,
        dump = "",
        nearby = {},
        nearby_dump = "",
    },
    loot_test = {
        last_status = "",
        last_dump = "",
    },
    target_dump = {
        last_status = "",
        last_dump = "",
    },
    task_record = {
        last_status = "",
        last_dump = "",
    },
    main_quest = {
        last_status = "",
        last_action = "",
        last_action_at = 0,
        last_quest_read_at = 0,
        cached_quest = nil,
        waiting_teleport = false,
        teleport_quest_id = 0,
        teleport_stage = "",
        teleport_start_pos = nil,
        teleport_start_big_map_id = 0,
        last_nav_stage = "",
        last_nav_at = 0,
        last_nav_distance = 0,
        last_interact_stage = "",
        last_interact_at = 0,
        wait_dialog_stage = "",
        wait_dialog_until = 0,
        last_dialog_signature = "",
        last_decision_signature = "",
        last_decision_log_at = 0,
        last_route_stop_stage = "",
        last_route_stop_reason = "",
        last_route_stop_at = 0,
        action_delay_until = 0,
        action_delay_reason = "",
        post_dialog_settle_until = 0,
        current_action_name = "",
        current_action_stage = "",
        trace_times = {},
        completed_20590_first_teleport = false,
        completed_20590_inner_final_move = false,
        completed_20590_inner_teleport = false,
        completed_20590_temple_teleport = false,
        completed_20590_reward = false,
        completed_20590_teleport = false,
        completed_20610_start_dialog = false,
        completed_20610_task_teleport = false,
        completed_20610_reward = false,
        clicked_20610_indicator_teleport = false,
        clicked_20610_target_link = false,
        clicked_20610_dictionary_teleport = false,
        completed_20611_level_move = false,
        level_move_quest_id = 0,
        active_20611_grind = false,
        active_20611_grind_stage = "",
        level_grind_quest_id = 0,
        level_grind_required_level = 0,
        quest_grind_authorized = false,
        quest_grind_authorized_stage = "",
        quest_grind_authorized_quest_id = 0,
        quest_grind_authorized_until = 0,
        quest_grind_authorized_action = "",
        combat_guard_active = false,
        combat_guard_until = 0,
        combat_guard_reason = "",
        combat_guard_action = "",
        combat_guard_last_hp = 0,
        combat_guard_last_damage_at = 0,
        completed_20611_grind = false,
        completed_20611_mission_dialog = false,
        opened_20611_obelisk = false,
        opened_20611_obelisk_at = 0,
        clicked_20611_obelisk_confirm = false,
        clicked_20611_obelisk_confirm_at = 0,
        obelisk_confirm_wait_until = 0,
        completed_20611_obelisk = false,
        clicked_20611_indicator_title = false,
        clicked_20611_indicator_entry_name = "",
        clicked_20611_target_link = false,
        clicked_20611_dictionary_teleport = false,
        completed_20611_target_teleport = false,
        completed_20611_target_dialog = false,
        completed_20611_hotspot_teleport = false,
        completed_20611_hotspot_reward = false,
        reached_20612_start_point = false,
        completed_20612_start_dialog = false,
        completed_20612_task_teleport = false,
        completed_20612_reward_dialog = false,
        quest_teleport_panel_key = "",
        quest_teleport_panel_opened_at = 0,
    },
    skill_order = {
        left_index = 1,
        right_index = 1,
    },
    maintenance = {
        keycode_reference_open = false,
        keycode_window_visible = false,
        keycode_target_kind = "",
        keycode_target_index = 0,
        floor_recovery = {
            active = false,
            started_at = 0,
            start_hp = 0,
            last_hp = 0,
            start_mp_percent = 0,
            last_mp_percent = 0,
            last_action = "",
            last_reason = "",
        },
    },
    target = {
        candidates = {},
        labels = { "No Aion window" },
        selected_index = 1,
        last_refresh_at = 0,
        last_error = "",
        bound_pid = 0,
        bound_hwnd = 0,
        binding_status = "unknown",
        binding_message = "",
        foreground_pid = 0,
        foreground_hwnd = 0,
        foreground_title = "",
    },
    accounts = {
        view = 1,
        selected_index = 1,
        import_text = "",
        show_import = false,
        add_window_visible = false,
        add_account = "",
        add_password = "",
        add_second_password = "",
        add_draft = nil,
        add_force_size = false,
        settings_window_visible = false,
        last_poll_at = 0,
        last_status = "",
        worker_task_id = 0,
        worker_queue_id = "",
        pending_login = nil,
        pending_script = nil,
        server_labels = { "服务器 1", "服务器 2", "服务器 3", "服务器 4" },
        server_last_status = "",
        account_api_checked = false,
        account_api_ok = false,
        account_api = nil,
        save_feedback_until = 0,
        save_feedback_ok = true,
        save_feedback_text = "",
    },
}

local cfg = {
    profile_name = "默认方案",
    primary_mode = 1,
    priority_mode = 1,

    combat = {
        enabled = true,
        mode = 1,
        target_policy = 1,
        anchor_enabled = false,
        anchor_x = 0,
        anchor_y = 0,
        anchor_z = 0,
        radius = 35,
        min_level = 1,
        max_level = 99,
        return_radius = 4,
        tick_interval = 0.10,
        move_resend_interval = 2.0,
        attack_trigger_mode = 1,
        attack_keycode = ATTACK_KEYCODE,
        attack_key_repeat_interval_ms = 1000,
        target_no_damage_seconds = 6.0,
        target_ignore_seconds = 20.0,
        auto_force_window = 0.60,
        auto_force_interval = 0.10,
        stop_move_on_target = false,
        loot_enabled = true,
        loot_radius = 35,
        loot_interact_range = 4,
        loot_keycode = 67,
        loot_retry_interval = 1.0,
        loot_max_attempts = 2,
        post_kill_check_delay_seconds = 0.1,
        auto_refresh_interval = 1.0,
        debug_log = true,
        debug_log_interval = 2.0,
        prefer_quest_targets = true,
        avoid_elite = false,
        keep_auto_battle = false,
        allow_kill_steal = false,
        counter_enemy_race = false,
        target_names = "",
        blacklist_names = "",
        ignore_summons = true,
        pet_names = "물의 정령\n불의 정령\n바람의 정령\n대지의 정령\n용암의 정령\n태풍의 정령",
    },

    gather = {
        enabled = false,
        mode = 1,
        radius = 30,
        gather_herb = true,
        gather_ore = true,
        gather_resource = true,
        gather_after_combat = true,
        resource_names = "",
        blacklist_names = "",
    },

    skills = {
        enabled = true,
        auto_sync_from_api = true,
        prefer_auto_battle_list = true,
        translate_names = true,
        translation_map = "",
        combat_order = "",
        buff_order = "",
        ignore_names = "",
        notes = "",
    },

    character = {
        auto_sync_from_api = true,
        race = 0,
        job = 1,
        race_name = "天族",
        job_name = "剑星",
    },

    target = {
        enabled = true,
        pid = 0,
        hwnd = 0,
        title = "",
        process_name = "",
        class_name = "",
        path = "",
        character_name = "",
        lock_on_start = true,
        require_match_on_start = true,
        refresh_interval = 2.0,
    },

    accounts = {
        enabled = true,
        auto_start_after_login = true,
        poll_interval = 5.0,
        game_path = "",
        purple_root = "",
        dll_path = "",
        lang = "",
        captcha_key = "",
        decode_mail = "",
        pid_wait_seconds = 60,
        login_gap_ms = 1500,
        items = {},
    },

    route = {
        active_tab = 1,
        selected_route = 1,
        loop = true,
        reverse_on_end = false,
        stop_on_death = true,
        death_confirm_seconds = 0.8,
        death_confirm_count = 2,
        record_interval = 1.5,
        min_record_distance = 2.5,
        waypoint_radius = 3,
        move_timeout = 12,
        resend_interval = 2.5,
        max_waypoint_retries = 2,
        start_from_nearest = true,
        revive_path_snap_radius = 45,
        start_near_radius = 45,
        return_keycode = 187,
        return_wait_seconds = 8,
        dead_return_wait_seconds = 8,
        auto_revive = true,
        revive_click_interval = 0.8,
        post_revive_wait_seconds = 2.0,
        route_name = "打怪路径",
        revive_route_name = "复活路径",
        vendor_route_name = "补给路径",
        gather_route_name = "采集路径",
        leveling_route_name = "主线路径",
        route_points = "",
        revive_points = "",
        vendor_points = "",
        gather_points = "",
        leveling_points = "",
        saved_routes = {},
    },

    leveling = {
        enabled = true,
        mode = 1,
        start_level = 1,
        target_level = 50,
        move_resend_interval = 0.5,
        npc_interact_settle_seconds = 0,
        npc_interact_retry_seconds = 3.0,
        action_delay_seconds = 0.5,
        prefer_quest = true,
        allow_grind = true,
        quest_grind_authorization_ttl_seconds = 0.75,
        allow_gather = false,
        learn_skills = true,
        equip_upgrades = true,
    },

    npc_dialog = {
        accept_npc_name = "",
        accept_npc_interact_id = "",
        accept_quest_id = 0,
        accept_next_dialog_id = 0,
        wait_dialog_ms = 3000,
        scan_radius = 45,
        scan_limit = 12,
        dialog_child_depth = 6,
        auto_click_x = 25,
        auto_click_x_tolerance = 2,
        auto_click_steps = 8,
        auto_click_delay_ms = 450,
    },

    crafting = {
        enabled = false,
        profession = 1,
        item_name = "",
        craft_count = 10,
        stop_when_missing_material = true,
        reserve_kinah = 1000,
        material_rules = "",
    },

    supply = {
        hp_percent = 35,
        mp_percent = 25,
        bag_full_percent = 85,
        bag_slots = 100,
        min_kinah = 0,
        buy_hp_potion = 50,
        buy_mp_potion = 50,
        vendor_name = "",
        keep_items = "",
        sell_rules = "",
        hp_rules = {},
        mp_rules = {},
        floor_recovery = {
            enabled = false,
            start_percent = 15,
            recover_percent = 90,
            sit_keycode = 188,
            stand_keycode = 88,
            cancel_on_damage = true,
        },
    },

    safety = {
        max_failures = 5,
        max_stuck_seconds = 20,
        max_deaths = 3,
        stop_on_unknown_map = true,
        stop_on_api_fail = true,
        circuit_breaker = true,
    },

    audit = {
        enabled = true,
        sample_interval = 2.0,
        show_details = false,
        reset_on_start = true,
        material_keywords = "材料\n粉末\n精气\n精髓\n矿\n药\n纤维\n宝石",
    },

    transfer = {
        route_export_path = "exports/aion_routes.lua",
        route_import_path = "exports/aion_routes.lua",
        profile_export_path = "exports/aion_control_profile.lua",
        profile_import_path = "exports/aion_control_profile.lua",
    },

    test = {
        selected_node_id = 0,
        ui_parent_name = "resurrect_dialog_new",
        ui_child_depth = 6,
        ui_include_no_name = true,
    },
}

local primary_modes = {
    "自定义打怪",
    "自动练级",
}

local primary_mode_ids = {
    "combat",
    "leveling",
}

local function normalize_primary_mode()
    local mode = tonumber(cfg.primary_mode) or 1
    if mode < 1 or mode > #primary_modes then
        mode = 1
    end
    cfg.primary_mode = mode
end

function combat_allowed_by_primary_mode()
    normalize_primary_mode()
    local mode = primary_mode_ids[cfg.primary_mode] or ""
    if mode == "combat" then
        return true
    end
    if mode == "leveling" then
        local guard_authorized = type(main_quest_combat_guard_authorized) == "function"
            and main_quest_combat_guard_authorized()
        local grind_authorized = cfg.leveling
            and cfg.leveling.allow_grind == true
            and runtime
            and runtime.main_quest
            and runtime.main_quest.active_20611_grind == true
            and type(main_quest_grind_authorized) == "function"
            and main_quest_grind_authorized()
        return guard_authorized or grind_authorized
    end
    return false
end

function sync_combat_enabled_from_primary_mode()
    if cfg and cfg.combat then
        cfg.combat.enabled = combat_allowed_by_primary_mode()
    end
    return cfg and cfg.combat and cfg.combat.enabled == true
end

function normalize_combat_config()
    if not cfg or not cfg.combat then
        return
    end
    local interval = tonumber(cfg.combat.tick_interval)
    if interval == nil or interval >= 0.30 then
        interval = 0.10
    end
    cfg.combat.tick_interval = math.max(0.05, math.min(0.20, interval))
    cfg.combat.auto_force_window = math.max(0.20, math.min(1.50, tonumber(cfg.combat.auto_force_window) or 0.60))
    cfg.combat.auto_force_interval = math.max(0.05, math.min(0.30, tonumber(cfg.combat.auto_force_interval) or 0.10))
    if cfg.combat.allow_kill_steal == nil then
        cfg.combat.allow_kill_steal = false
    end
    if cfg.combat.counter_enemy_race == nil then
        cfg.combat.counter_enemy_race = false
    end
    cfg.combat.loot_max_attempts = math.max(1, math.min(2, tonumber(cfg.combat.loot_max_attempts) or 2))
    cfg.combat.post_kill_check_delay_seconds = math.max(0.05, math.min(0.50, tonumber(cfg.combat.post_kill_check_delay_seconds) or 0.1))
    if type(normalize_combat_mode) == "function" then
        normalize_combat_mode()
    end
end

local priority_modes = {
    "优先打怪",
    "优先采集",
    "只打怪",
    "只采集",
    "任务优先",
}

local function normalize_priority_mode()
    cfg.priority_mode = math.max(1, math.min(#priority_modes, tonumber(cfg.priority_mode) or 1))
end

function normalize_route_config()
    if type(cfg.route) ~= "table" then
        return
    end
    cfg.route.return_keycode = 187
end

local combat_modes = {
    "原地打怪",
    "路径打怪",
}

function normalize_combat_mode()
    if not cfg or not cfg.combat then
        return
    end
    cfg.combat.mode = math.max(1, math.min(#combat_modes, tonumber(cfg.combat.mode) or 1))
    cfg.combat.attack_trigger_mode = tonumber(cfg.combat.attack_trigger_mode) == 2 and 2 or 1
    cfg.combat.attack_keycode = ATTACK_KEYCODE
    local ok_repeat, repeat_key = pcall(require, "aion.attack_key_repeat")
    if ok_repeat and repeat_key and type(repeat_key.from_config) == "function" then
        local settings = repeat_key.from_config(cfg.combat)
        cfg.combat.attack_key_repeat_interval_ms = settings.interval_ms
    else
        cfg.combat.attack_key_repeat_interval_ms = math.max(250, math.min(3000, math.floor(tonumber(cfg.combat.attack_key_repeat_interval_ms) or 1000)))
    end
end

local combat_target_policies = {
    "最近目标",
    "任务目标",
    "低血量目标",
    "威胁目标",
    "指定名字",
    "优先攻击主动怪",
}

local gather_modes = {
    "原地采集",
    "路径采集",
    "战后采集",
    "只采任务资源",
}

local race_options = {
    { id = 0, label = "天族" },
    { id = 1, label = "魔族" },
}

local race_names = {
    "天族",
    "魔族",
}

local job_options = {
    { id = 0x1, label = "剑星" },
    { id = 0x2, label = "守护星" },
    { id = 0x4, label = "杀星" },
    { id = 0x5, label = "弓星" },
    { id = 0x7, label = "魔道星" },
    { id = 0x8, label = "精灵星" },
    { id = 0xA, label = "治愈星" },
    { id = 0xB, label = "护法星" },
    { id = 0xD, label = "执行者" },
    { id = 0x10, label = "拳星" },
    { id = 0x13, label = "吟游星" },
    { id = 0x16, label = "机甲星" },
    { id = 0x19, label = "魔剑士" },
}

local job_names = {
    "剑星",
    "守护星",
    "杀星",
    "弓星",
    "魔道星",
    "精灵星",
    "治愈星",
    "护法星",
    "执行者",
    "拳星",
    "吟游星",
    "机甲星",
    "魔剑士",
}

local leveling_modes = {
    "只做主线",
    "主线优先",
}

local professions = {
    "炼金",
    "料理",
    "武器",
    "防具",
    "裁缝",
    "手工",
}

local route_specs = {
    {
        label = "复活路径",
        description = "死亡复活后返回主路径",
        name_field = "revive_route_name",
        points_field = "revive_points",
    },
    {
        label = "打怪路径",
        description = "路径打怪用路径",
        name_field = "route_name",
        points_field = "route_points",
    },
    {
        label = "补给路径",
        description = "去商人/仓库/任务点路径",
        name_field = "vendor_route_name",
        points_field = "vendor_points",
        hidden = true,
    },
    {
        label = "采集路径",
        description = "采集专用巡线路径",
        name_field = "gather_route_name",
        points_field = "gather_points",
        hidden = true,
    },
    {
        label = "主线路径",
        description = "主线任务推进路径",
        name_field = "leveling_route_name",
        points_field = "leveling_points",
        hidden = true,
    },
}

local route_config_keys = {
    "selected_route",
    "loop",
    "reverse_on_end",
    "stop_on_death",
    "death_confirm_seconds",
    "death_confirm_count",
    "record_interval",
    "min_record_distance",
    "waypoint_radius",
    "move_timeout",
    "resend_interval",
    "max_waypoint_retries",
    "start_from_nearest",
    "revive_path_snap_radius",
    "start_near_radius",
    "return_keycode",
    "return_wait_seconds",
    "dead_return_wait_seconds",
    "auto_revive",
    "revive_click_interval",
    "post_revive_wait_seconds",
    "route_name",
    "revive_route_name",
    "vendor_route_name",
    "gather_route_name",
    "leveling_route_name",
    "route_points",
    "revive_points",
    "vendor_points",
    "gather_points",
    "leveling_points",
    "saved_routes",
}

local config_domain_keys = {
    "combat",
    "gather",
    "skills",
    "character",
    "target",
    "accounts",
    "route",
    "leveling",
    "npc_dialog",
    "crafting",
    "supply",
    "safety",
    "audit",
    "transfer",
    "test",
}

local config_top_level_keys = {
    "profile_name",
    "primary_mode",
    "priority_mode",
}

local function rgba(r, g, b, a)
    return { r = r, g = g, b = b, a = a or 1.0 }
end

local function apply_white_blue_style()
    if not imgui or not imgui.set_style_colors then
        return
    end

    if imgui.style_colors_light then
        imgui.style_colors_light()
    end

    local c = imgui
    imgui.set_style_colors({
        [c.Col_Text]                    = rgba(0.08, 0.16, 0.28, 1.0),
        [c.Col_TextDisabled]            = rgba(0.47, 0.56, 0.68, 1.0),
        [c.Col_WindowBg]                = rgba(0.94, 0.97, 1.00, 1.0),
        [c.Col_ChildBg]                 = rgba(0.89, 0.94, 1.00, 1.0),
        [c.Col_PopupBg]                 = rgba(0.96, 0.98, 1.00, 1.0),
        [c.Col_Border]                  = rgba(0.60, 0.72, 0.88, 1.0),
        [c.Col_BorderShadow]            = rgba(0.00, 0.00, 0.00, 0.0),
        [c.Col_FrameBg]                 = rgba(0.84, 0.91, 0.99, 1.0),
        [c.Col_FrameBgHovered]          = rgba(0.74, 0.85, 0.98, 1.0),
        [c.Col_FrameBgActive]           = rgba(0.62, 0.78, 0.96, 1.0),
        [c.Col_TitleBg]                 = rgba(0.74, 0.84, 0.96, 1.0),
        [c.Col_TitleBgActive]           = rgba(0.32, 0.55, 0.86, 1.0),
        [c.Col_TitleBgCollapsed]        = rgba(0.82, 0.89, 0.98, 1.0),
        [c.Col_MenuBarBg]               = rgba(0.88, 0.93, 0.99, 1.0),
        [c.Col_ScrollbarBg]             = rgba(0.88, 0.93, 0.99, 1.0),
        [c.Col_ScrollbarGrab]           = rgba(0.55, 0.72, 0.92, 1.0),
        [c.Col_ScrollbarGrabHovered]    = rgba(0.42, 0.64, 0.90, 1.0),
        [c.Col_ScrollbarGrabActive]     = rgba(0.25, 0.52, 0.86, 1.0),
        [c.Col_CheckMark]               = rgba(0.12, 0.42, 0.82, 1.0),
        [c.Col_SliderGrab]              = rgba(0.20, 0.50, 0.86, 1.0),
        [c.Col_SliderGrabActive]        = rgba(0.10, 0.38, 0.78, 1.0),
        [c.Col_Button]                  = rgba(0.26, 0.52, 0.86, 1.0),
        [c.Col_ButtonHovered]           = rgba(0.18, 0.44, 0.82, 1.0),
        [c.Col_ButtonActive]            = rgba(0.10, 0.34, 0.72, 1.0),
        [c.Col_Header]                  = rgba(0.75, 0.86, 0.98, 1.0),
        [c.Col_HeaderHovered]           = rgba(0.58, 0.76, 0.96, 1.0),
        [c.Col_HeaderActive]            = rgba(0.38, 0.62, 0.90, 1.0),
        [c.Col_Separator]               = rgba(0.58, 0.72, 0.88, 1.0),
        [c.Col_SeparatorHovered]        = rgba(0.34, 0.58, 0.86, 1.0),
        [c.Col_SeparatorActive]         = rgba(0.18, 0.44, 0.80, 1.0),
        [c.Col_Tab]                     = rgba(0.80, 0.89, 0.99, 1.0),
        [c.Col_TabHovered]              = rgba(0.48, 0.70, 0.96, 1.0),
        [c.Col_TabSelected]             = rgba(0.30, 0.56, 0.88, 1.0),
        [c.Col_TabDimmed]               = rgba(0.88, 0.93, 0.99, 1.0),
        [c.Col_TabDimmedSelected]       = rgba(0.72, 0.84, 0.97, 1.0),
        [c.Col_TextSelectedBg]          = rgba(0.30, 0.56, 0.88, 0.35),
        [c.Col_ModalWindowDimBg]        = rgba(0.35, 0.44, 0.58, 0.45),
    })

    if imgui.set_style then
        imgui.set_style({
            window_rounding = 6,
            frame_rounding = 4,
            grab_rounding = 4,
            tab_rounding = 4,
        })
    end
end

local function log_info(msg)
    if log and log.info then
        log.info(msg)
    elseif print then
        print(msg)
    end
end

local function log_warn(msg)
    if log and log.warn then
        log.warn(msg)
    else
        log_info(msg)
    end
end

local set_event
local format_duration

local function now_seconds()
    if sys and type(sys.time) == "function" then
        return sys.time() / 1000
    end
    return os.clock()
end

function main_quest_is_grind_stage(stage)
    stage = tostring(stage or "")
    return stage == "quest_20611_grind"
        or stage == "quest_20611_level_grind"
        or stage == "quest_20612_level_grind"
end

function main_quest_action_authorizes_grind(action)
    if type(action) ~= "table" then
        return false
    end
    local name = tostring(action.name or "")
    local params = type(action.params) == "table" and action.params or {}
    if params.requires_combat ~= true or tostring(params.task_step or "") ~= "grind" then
        return false
    end
    if name ~= "StartStationaryGrind"
        and name ~= "WaitLevelGrind"
        and name ~= "WaitQuestComplete" then
        return false
    end
    return main_quest_is_grind_stage(params.stage)
end

function main_quest_clear_grind_authorization(reason)
    runtime.main_quest = runtime.main_quest or {}
    local r = runtime.main_quest
    r.quest_grind_authorized = false
    r.quest_grind_authorized_stage = ""
    r.quest_grind_authorized_quest_id = 0
    r.quest_grind_authorized_until = 0
    r.quest_grind_authorized_action = ""
    r.quest_grind_authorized_clear_reason = tostring(reason or "")
end

function main_quest_authorize_grind(action, reason)
    if not main_quest_action_authorizes_grind(action) then
        return false
    end
    runtime.main_quest = runtime.main_quest or {}
    local r = runtime.main_quest
    local params = type(action.params) == "table" and action.params or {}
    local ttl = tonumber(cfg and cfg.leveling and cfg.leveling.quest_grind_authorization_ttl_seconds) or 0.75
    ttl = math.max(0.20, math.min(3.00, ttl))
    r.quest_grind_authorized = true
    r.quest_grind_authorized_stage = tostring(params.stage or "")
    r.quest_grind_authorized_quest_id = tonumber(params.quest_id) or 0
    r.quest_grind_authorized_action = tostring(action.name or "")
    r.quest_grind_authorized_until = now_seconds() + ttl
    r.quest_grind_authorized_reason = tostring(reason or action.reason or "")
    return true
end

function main_quest_grind_authorized()
    local r = runtime and runtime.main_quest or nil
    if type(r) ~= "table" or r.quest_grind_authorized ~= true then
        return false
    end
    local stage = tostring(r.quest_grind_authorized_stage or "")
    if not main_quest_is_grind_stage(stage) then
        main_quest_clear_grind_authorization("invalid-stage")
        return false
    end
    if now_seconds() > (tonumber(r.quest_grind_authorized_until) or 0) then
        main_quest_clear_grind_authorization("expired")
        return false
    end
    if r.active_20611_grind ~= true then
        return false
    end
    if tostring(r.active_20611_grind_stage or "") ~= stage then
        return false
    end
    if stage == "quest_20611_level_grind" or stage == "quest_20612_level_grind" then
        local auth_qid = tonumber(r.quest_grind_authorized_quest_id) or 0
        if auth_qid <= 0 or auth_qid ~= (tonumber(r.level_grind_quest_id) or 0) then
            return false
        end
    end
    return true
end

function main_quest_combat_guard_authorized()
    local r = runtime and runtime.main_quest or nil
    if type(r) ~= "table" or r.combat_guard_active ~= true then
        return false
    end
    if now_seconds() > (tonumber(r.combat_guard_until) or 0) then
        r.combat_guard_active = false
        r.combat_guard_reason = "expired"
        r.combat_guard_action = ""
        return false
    end
    return true
end

function main_quest_clear_combat_guard(reason)
    runtime.main_quest = runtime.main_quest or {}
    local r = runtime.main_quest
    r.combat_guard_active = false
    r.combat_guard_until = 0
    r.combat_guard_reason = tostring(reason or "")
    r.combat_guard_action = ""
end

button_feedback = {
    installed = false,
    active = {},
    style_supported = true,
}

function button_feedback_key(kind, label)
    local text = tostring(label or "")
    local explicit_id = string.match(text, "###(.+)$") or string.match(text, "##(.+)$")
    return tostring(kind or "button") .. ":" .. tostring(explicit_id or text)
end

function button_feedback_push(active)
    if not active or not imgui or type(imgui.push_style_color) ~= "function" then
        return 0
    end
    if button_feedback.style_supported == false then
        return 0
    end
    if not imgui.Col_Button or not imgui.Col_ButtonHovered or not imgui.Col_ButtonActive then
        return 0
    end

    local count = 0
    local ok = pcall(imgui.push_style_color, imgui.Col_Button, rgba(0.10, 0.34, 0.72, 1.0))
    if not ok then
        button_feedback.style_supported = false
        return 0
    end
    count = count + 1
    ok = pcall(imgui.push_style_color, imgui.Col_ButtonHovered, rgba(0.08, 0.30, 0.66, 1.0))
    if not ok then
        button_feedback.style_supported = false
        button_feedback_pop(count)
        return 0
    end
    count = count + 1
    ok = pcall(imgui.push_style_color, imgui.Col_ButtonActive, rgba(0.06, 0.24, 0.56, 1.0))
    if not ok then
        button_feedback.style_supported = false
        button_feedback_pop(count)
        return 0
    end
    count = count + 1
    return count
end

function button_feedback_pop(count)
    if count > 0 and imgui and type(imgui.pop_style_color) == "function" then
        pcall(imgui.pop_style_color, count)
    end
end

function draw_feedback_button(kind, label, draw)
    local key = button_feedback_key(kind, label)
    local until_at = tonumber(button_feedback.active[key]) or 0
    local pushed = button_feedback_push(until_at > now_seconds())
    local clicked = draw()
    button_feedback_pop(pushed)

    if clicked then
        button_feedback.active[key] = now_seconds() + 0.22
    end
    return clicked
end

function install_button_feedback()
    if button_feedback.installed or not imgui then
        return
    end
    button_feedback.installed = true

    local raw_button = imgui.button
    if type(raw_button) == "function" then
        imgui.button = function(label, width, height)
            return draw_feedback_button("button", label, function()
                if width == nil then
                    return raw_button(label)
                end
                if height == nil then
                    return raw_button(label, width)
                end
                return raw_button(label, width, height)
            end)
        end
    end

    local raw_small_button = imgui.small_button
    if type(raw_small_button) == "function" then
        imgui.small_button = function(label)
            return draw_feedback_button("small_button", label, function()
                return raw_small_button(label)
            end)
        end
    end

    local raw_arrow_button = imgui.arrow_button
    if type(raw_arrow_button) == "function" then
        imgui.arrow_button = function(label, dir)
            return draw_feedback_button("arrow_button", label, function()
                return raw_arrow_button(label, dir)
            end)
        end
    end
end

local function count_array(list)
    if type(list) ~= "table" then
        return 0
    end

    local n = 0
    for _ in ipairs(list) do
        n = n + 1
    end
    return n
end

local function count_lines(text)
    local n = 0
    for line in string.gmatch(text or "", "[^\r\n]+") do
        if line ~= "" then
            n = n + 1
        end
    end
    return n
end

local function find_option_index(options, id)
    local numeric_id = tonumber(id)
    for index, item in ipairs(options or {}) do
        if item.id == numeric_id then
            return index
        end
    end
    return 1
end

local function option_name(options, id, fallback)
    local numeric_id = tonumber(id)
    for _, item in ipairs(options or {}) do
        if item.id == numeric_id then
            return item.label
        end
    end
    return fallback or ("未知(" .. tostring(id) .. ")")
end

local function race_name_by_id(id, fallback)
    return option_name(race_options, tonumber(id), fallback)
end

local function job_name_by_id(id, fallback)
    return option_name(job_options, tonumber(id), fallback)
end

local function apply_character_from_api(char)
    if type(char) ~= "table" then
        return false
    end

    local race_id = tonumber(char.race)
    local job_id = tonumber(char.job)
    if race_id ~= nil then
        cfg.character.race = race_id
        cfg.character.race_name = char.race_name or race_name_by_id(race_id, cfg.character.race_name)
    end
    if job_id ~= nil then
        cfg.character.job = job_id
        cfg.character.job_name = job_name_by_id(job_id, cfg.character.job_name)
    end
    return race_id ~= nil or job_id ~= nil
end

local function sync_character_from_api()
    if not ok_core or not core then
        set_event("同步角色失败: aion.core 不可用")
        return false
    end

    local ok, char, err = core.getCharacter()
    if not ok or not char then
        set_event("同步角色失败: " .. tostring(err or "当前角色不可用"))
        return false
    end

    apply_character_from_api(char)
    set_event(string.format("已从 API 同步角色: %s %s",
        tostring(cfg.character.race_name),
        tostring(cfg.character.job_name)))
    return true
end

local function audit_reset()
    local a = runtime.audit
    a.started_at = now_seconds()
    a.last_sample_at = 0
    a.last_light_sample_at = 0
    a.elapsed_seconds = 0
    a.samples = 0
    a.kills_est = 0
    a.gather_est = 0
    a.material_gain = 0
    a.exp_gain = 0
    a.kinah_gain = 0
    a.seen_loot = {}
    a.seen_loot_ready = false
    a.last_inventory_counts = nil
    a.last_level = nil
    a.last_exp = nil
    a.last_max_exp = nil
    a.last_kinah = nil
    a.last_error = ""
end

local function audit_rate(value)
    local hours = runtime.audit.elapsed_seconds / 3600
    if hours <= 0 then
        return 0
    end
    return value / hours
end

local function audit_loot_key(e)
    return tostring(e.obj or e.IEntity or e.id or "") .. ":" .. tostring(e.name or "")
end

local function audit_is_material_item(item)
    local text = tostring(item.text or item.name or "") .. " " .. tostring(item.cat_name or "")
    for keyword in string.gmatch(cfg.audit.material_keywords or "", "[^\r\n]+") do
        if keyword ~= "" and string.find(text, keyword, 1, true) then
            return true
        end
    end
    return false
end

local function audit_inventory_counts(items)
    local counts = {}
    for _, item in ipairs(items or {}) do
        if audit_is_material_item(item) then
            local key = tostring(item.text or item.name or item.id or "")
            counts[key] = (counts[key] or 0) + (item.count or 1)
        end
    end
    return counts
end

local function audit_positive_delta(prev, cur)
    if not prev then
        return 0
    end

    local delta = 0
    for key, value in pairs(cur) do
        local old = prev[key] or 0
        if value > old then
            delta = delta + value - old
        end
    end
    return delta
end

local function audit_sample()
    if not cfg.audit.enabled then
        return
    end

    local a = runtime.audit
    local now = now_seconds()
    if a.last_sample_at > 0 and now - a.last_sample_at < cfg.audit.sample_interval then
        return
    end
    a.last_sample_at = now
    a.samples = a.samples + 1

    local count_gains = runtime.running and not runtime.paused
    if a.started_at <= 0 then
        a.started_at = now
    end
    if count_gains then
        a.elapsed_seconds = now - a.started_at
    end

    if ok_core and core then
        local ok, char, err = core.getCharacter()
        if ok and char then
            a.current.name = char.name or ""
            a.current.race = char.race
            a.current.race_name = char.race_name or race_name_by_id(char.race, "")
            a.current.job = char.job
            a.current.job_name = job_name_by_id(char.job, "")
            a.current.hp = char.hp or 0
            a.current.max_hp = char.mhp or char.max_hp or 0
            a.current.mp = char.mp or 0
            a.current.max_mp = char.mmp or char.max_mp or 0
            a.current.level = char.level or 0

            if cfg.character.auto_sync_from_api then
                apply_character_from_api(char)
            end

            if count_gains and a.last_exp ~= nil then
                local exp_delta = 0
                if (char.level or 0) == (a.last_level or 0) then
                    exp_delta = (char.exp or 0) - (a.last_exp or 0)
                elseif (char.level or 0) > (a.last_level or 0) then
                    exp_delta = math.max(0, (a.last_max_exp or 0) - (a.last_exp or 0)) + (char.exp or 0)
                end
                if exp_delta > 0 then
                    a.exp_gain = a.exp_gain + exp_delta
                end
            end

            a.last_level = char.level or 0
            a.last_exp = char.exp or 0
            a.last_max_exp = char.max_exp or 0
        elseif err then
            a.last_error = tostring(err)
        end
    end

    if ok_map and map then
        local map_ok, cur_map = map.current()
        if map_ok and cur_map then
            a.current.map = cur_map.region or cur_map.name_cn or cur_map.name_en or ""
        end
    end

    if ok_entity and entity then
        local list_ok, list, list_err = entity.list()
        if list_ok and list then
            a.current.entities = count_array(list)
            local new_loot = 0
            for _, e in ipairs(list) do
                if (e.lootable or 0) ~= 0 then
                    local key = audit_loot_key(e)
                    if a.seen_loot_ready and not a.seen_loot[key] and count_gains then
                        new_loot = new_loot + 1
                    end
                    a.seen_loot[key] = true
                end
            end
            if not a.seen_loot_ready then
                a.seen_loot_ready = true
            end
            a.kills_est = a.kills_est + new_loot
        elseif list_err then
            a.last_error = tostring(list_err)
        end
    end

    if ok_inventory and inventory then
        local inv_ok, items, inv_err = inventory.list()
        if inv_ok and items then
            a.current.inventory = count_array(items)
            local cur_counts = audit_inventory_counts(items)
            local gain = count_gains and audit_positive_delta(a.last_inventory_counts, cur_counts) or 0
            if gain > 0 then
                a.material_gain = a.material_gain + gain
                a.gather_est = a.gather_est + gain
            end
            a.last_inventory_counts = cur_counts
        elseif inv_err then
            a.last_error = tostring(inv_err)
        end

        local kinah_ok, kinah = inventory.kinah()
        if kinah_ok and kinah then
            if count_gains and a.last_kinah ~= nil then
                a.kinah_gain = a.kinah_gain + (kinah - a.last_kinah)
            end
            a.last_kinah = kinah
        end
    end

    if ok_combat and combat then
        local target_ok, target = combat.currentTarget()
        if target_ok and target then
            a.current.target_id = target.id or 0
        else
            a.current.target_id = 0
        end
    end
end

local function audit_light_sample()
    if not cfg.audit.enabled then
        return
    end

    local a = runtime.audit
    local now = now_seconds()
    local interval = math.max(1.0, tonumber(cfg.audit.sample_interval) or 2.0)
    if a.last_light_sample_at > 0 and now - a.last_light_sample_at < interval then
        return
    end
    a.last_light_sample_at = now

    local count_gains = runtime.running and not runtime.paused
    if a.started_at <= 0 then
        a.started_at = now
    end
    if count_gains then
        a.elapsed_seconds = now - a.started_at
    end

    if ok_core and core then
        local ok, char, err = core.getCharacter()
        if ok and char then
            a.current.name = char.name or ""
            a.current.race = char.race
            a.current.race_name = char.race_name or race_name_by_id(char.race, "")
            a.current.job = char.job
            a.current.job_name = job_name_by_id(char.job, "")
            a.current.hp = char.hp or 0
            a.current.max_hp = char.mhp or char.max_hp or 0
            a.current.mp = char.mp or 0
            a.current.max_mp = char.mmp or char.max_mp or 0
            a.current.level = char.level or 0

            if cfg.character.auto_sync_from_api then
                apply_character_from_api(char)
            end

            if count_gains and a.last_exp ~= nil then
                local exp_delta = 0
                if (char.level or 0) == (a.last_level or 0) then
                    exp_delta = (char.exp or 0) - (a.last_exp or 0)
                elseif (char.level or 0) > (a.last_level or 0) then
                    exp_delta = math.max(0, (a.last_max_exp or 0) - (a.last_exp or 0)) + (char.exp or 0)
                end
                if exp_delta > 0 then
                    a.exp_gain = a.exp_gain + exp_delta
                end
            end

            a.last_level = char.level or 0
            a.last_exp = char.exp or 0
            a.last_max_exp = char.max_exp or 0
        elseif err then
            a.last_error = tostring(err)
        end
    end

    if ok_map and map then
        local map_ok, cur_map = map.current()
        if map_ok and cur_map then
            a.current.map = cur_map.region or cur_map.name_cn or cur_map.name_en or ""
        end
    end

    if ok_inventory and inventory then
        local kinah_ok, kinah = inventory.kinah()
        if kinah_ok and kinah then
            if count_gains and a.last_kinah ~= nil then
                a.kinah_gain = a.kinah_gain + (kinah - a.last_kinah)
            end
            a.last_kinah = kinah
        end
    end
end

local function target_update_foreground()
    local t = runtime.target
    t.foreground_pid = 0
    t.foreground_hwnd = 0
    t.foreground_title = ""

    if not ok_target or not target_lib or type(target_lib.foreground) ~= "function" then
        return
    end

    local fg = target_lib.foreground()
    if fg then
        t.foreground_pid = fg.pid or 0
        t.foreground_hwnd = fg.hwnd or 0
        t.foreground_title = fg.title or ""
    end
end

local function target_refresh(force)
    local t = runtime.target
    local now = now_seconds()
    local interval = tonumber(cfg.target.refresh_interval) or 2.0

    if not force and t.last_refresh_at > 0 and now - t.last_refresh_at < interval then
        return
    end
    t.last_refresh_at = now
    target_update_foreground()

    if not ok_target or not target_lib then
        t.candidates = {}
        t.labels = { "aion.target unavailable" }
        t.selected_index = 1
        t.last_error = "aion.target unavailable"
        return
    end

    local ok, candidates, err = target_lib.list_candidates({
        selected_pid = cfg.target.pid,
    })
    if not ok then
        t.candidates = {}
        t.labels = { tostring(err or "target scan failed") }
        t.selected_index = 1
        t.last_error = tostring(err or "target scan failed")
        return
    end

    t.candidates = candidates or {}
    local selected_pid = tonumber(cfg.target.pid) or 0
    local selected_index = 0
    for index, candidate in ipairs(t.candidates) do
        if tonumber(candidate.pid) == selected_pid then
            selected_index = index
            break
        end
    end

    if #t.candidates > 0 and selected_index == 0 then
        target_lib.apply_candidate(cfg.target, t.candidates[1])
        selected_index = 1
        t.binding_status = "auto"
        t.binding_message = "auto selected first Aion.bin pid=" .. tostring(cfg.target.pid)
    end

    t.labels = {}
    for _, candidate in ipairs(t.candidates) do
        local character_name = ""
        if tonumber(candidate.pid) == tonumber(cfg.target.pid) then
            character_name = cfg.target.character_name or ""
        end
        if character_name == "" and tonumber(candidate.pid) == tonumber(t.bound_pid) then
            character_name = runtime.audit.current.name or ""
        end
        table.insert(t.labels, target_lib.label(candidate, character_name))
    end

    if #t.labels == 0 then
        t.labels = { "No Aion window" }
    end
    t.selected_index = selected_index > 0 and selected_index or target_lib.find_index(t.candidates, cfg.target.pid)
    t.last_error = ""
end

local function target_select_index(index)
    target_refresh(true)

    local candidate = runtime.target.candidates[index]
    if not candidate then
        set_event("目标窗口选择失败: 没有候选窗口")
        return false
    end

    if not ok_target or not target_lib then
        set_event("目标窗口选择失败: aion.target 不可用")
        return false
    end

    target_lib.apply_candidate(cfg.target, candidate)
    runtime.target.selected_index = index
    runtime.target.binding_status = "selected"
    runtime.target.binding_message = "selected pid=" .. tostring(cfg.target.pid)
    if config and type(config.set) == "function" and type(config.save) == "function" then
        pcall(function()
            if type(config.load) == "function" then
                config.load()
            end
            config.set("aion_control.target", cfg.target)
            config.save()
        end)
    end
    set_event("已选择目标窗口 PID=" .. tostring(cfg.target.pid) .. " HWND=" .. tostring(cfg.target.hwnd))
    return true
end

local function target_select_foreground()
    target_update_foreground()

    local fg_pid = tonumber(runtime.target.foreground_pid) or 0
    if fg_pid <= 0 then
        set_event("绑定前台窗口失败: 没有拿到前台 PID")
        return false
    end

    target_refresh(true)
    for index, candidate in ipairs(runtime.target.candidates or {}) do
        if tonumber(candidate.pid) == fg_pid then
            return target_select_index(index)
        end
    end

    cfg.target.pid = fg_pid
    cfg.target.hwnd = tonumber(runtime.target.foreground_hwnd) or 0
    cfg.target.title = tostring(runtime.target.foreground_title or "")
    cfg.target.process_name = ""
    cfg.target.class_name = ""
    cfg.target.path = ""
    runtime.target.binding_status = "selected"
    runtime.target.binding_message = "selected foreground pid=" .. tostring(cfg.target.pid)

    if config and type(config.set) == "function" and type(config.save) == "function" then
        pcall(function()
            if type(config.load) == "function" then
                config.load()
            end
            config.set("aion_control.target", cfg.target)
            config.save()
        end)
    end

    set_event("已绑定前台窗口PID=" .. tostring(cfg.target.pid))
    return true
end

local function target_select_single_game_process()
    if not ok_target or not target_lib or type(target_lib.first_game_candidate) ~= "function" then
        set_event("auto bind Aion.bin failed: aion.target unavailable")
        return false
    end

    local ok, candidate, err = target_lib.first_game_candidate()
    target_refresh(true)

    if not ok or not candidate then
        set_event("auto bind Aion.bin failed: " .. tostring(err or "not found"))
        return false
    end

    for index, item in ipairs(runtime.target.candidates or {}) do
        if tonumber(item.pid) == tonumber(candidate.pid) then
            return target_select_index(index)
        end
    end

    target_lib.apply_candidate(cfg.target, candidate)
    runtime.target.selected_index = target_lib.find_index(runtime.target.candidates, cfg.target.pid)
    runtime.target.binding_status = "selected"
    runtime.target.binding_message = "selected first Aion.bin pid=" .. tostring(cfg.target.pid)

    if config and type(config.set) == "function" and type(config.save) == "function" then
        pcall(function()
            if type(config.load) == "function" then
                config.load()
            end
            config.set("aion_control.target", cfg.target)
            config.save()
        end)
    end

    set_event("auto bound first Aion.bin PID=" .. tostring(cfg.target.pid))
    return true
end

local function target_validate_binding()
    local t = runtime.target
    if not cfg.target.enabled then
        t.binding_status = "disabled"
        t.binding_message = "target lock disabled"
        return true, t.binding_message
    end

    if not ok_target or not target_lib then
        t.binding_status = "module_error"
        t.binding_message = "aion.target unavailable"
        return false, t.binding_message
    end

    if not ok_core or not core then
        t.binding_status = "core_error"
        t.binding_message = "aion.core unavailable"
        return false, t.binding_message
    end

    local ok, status, message, state = target_lib.validate_binding(cfg.target, core)
    if state then
        t.bound_pid = state.pid or 0
        t.bound_hwnd = state.hwnd or 0
    else
        t.bound_pid = 0
        t.bound_hwnd = 0
    end

    t.binding_status = status or (ok and "matched" or "failed")
    t.binding_message = message or ""
    return ok == true, t.binding_message
end

local function target_require_ready_for_start()
    if not cfg.target.enabled or not cfg.target.require_match_on_start then
        return true
    end

    if (tonumber(cfg.target.pid) or 0) <= 0 then
        set_event("启动失败: 请先选择目标角色/PID")
        return false
    end

    if ok_core and core and type(core.ensureInit) == "function" then
        local init_ok, init_err = core.ensureInit(cfg.target.pid)
        if not init_ok then
            set_event("启动失败: AionData 初始化失败" .. tostring(init_err))
            return false
        end
    end

    local ok, message = target_validate_binding()
    if not ok then
        set_event("启动失败: 目标 PID 不匹配" .. tostring(message))
        return false
    end
    return true
end

local function bootstrap_set_error(err)
    runtime.bootstrap.last_error = tostring(err or "")
    if runtime.bootstrap.last_error ~= "" then
        runtime.audit.last_error = runtime.bootstrap.last_error
    end
end

local function bootstrap_update_character()
    local b = runtime.bootstrap
    b.character_ok = false

    if not ok_core or not core then
        bootstrap_set_error("aion.core 不可用")
        return
    end

    local ok, char, err = core.getCharacter()
    if not ok or not char then
        bootstrap_set_error(err or "当前角色不可用")
        return
    end

    local cur = runtime.audit.current
    cur.name = char.name or ""
    cur.race = char.race
    cur.race_name = char.race_name or race_name_by_id(char.race, "")
    cur.job = char.job
    cur.job_name = job_name_by_id(char.job, "")
    cur.hp = char.hp or 0
    cur.max_hp = char.mhp or char.max_hp or 0
    cur.mp = char.mp or 0
    cur.max_mp = char.mmp or char.max_mp or 0
    cur.level = char.level or 0

    apply_character_from_api(char)
    if cfg.target.enabled and (tonumber(cfg.target.pid) or 0) == (tonumber(runtime.target.bound_pid) or 0) then
        cfg.target.character_name = char.name or cfg.target.character_name or ""
    end
    runtime.audit.last_level = char.level or 0
    runtime.audit.last_exp = char.exp or 0
    runtime.audit.last_max_exp = char.max_exp or 0
    b.character_ok = true
end

local function bootstrap_update_map()
    local b = runtime.bootstrap
    b.map_ok = false

    if not ok_map or not map then
        return
    end

    local ok, cur_map, err = map.current()
    if not ok then
        bootstrap_set_error(err)
        return
    end

    if cur_map then
        b.map_name = cur_map.region or cur_map.name_cn or cur_map.name_en or ""
        runtime.audit.current.map = b.map_name
        b.map_ok = true
    end
end

local function bootstrap_update_inventory()
    local b = runtime.bootstrap
    b.inventory_ok = false

    if not ok_inventory or not inventory then
        return
    end

    local ok, items, err = inventory.list()
    if not ok then
        bootstrap_set_error(err)
        return
    end

    b.inventory_items = items or {}
    b.inventory_count = count_array(items)
    runtime.audit.current.inventory = b.inventory_count
    runtime.audit.last_inventory_counts = audit_inventory_counts(items)

    local kinah_ok, kinah = inventory.kinah()
    if kinah_ok then
        runtime.audit.last_kinah = kinah
    end
    b.inventory_ok = true
end

local function bootstrap_update_quest()
    local b = runtime.bootstrap
    b.quest_ok = false

    if not ok_quest or not quest then
        return
    end

    local ok, quests, err = quest.list()
    if not ok then
        bootstrap_set_error(err)
        return
    end

    b.quests = quests or {}
    b.quest_count = count_array(quests)
    runtime.audit.current.quests = b.quest_count
    b.quest_ok = true
end

local function bootstrap_update_combat()
    local b = runtime.bootstrap
    b.combat_ok = false

    if not ok_combat or not combat then
        return
    end

    local skill_ok, skills, skill_err = combat.skillList()
    if skill_ok then
        b.skills = skills or {}
        b.skill_count = count_array(skills)
        runtime.audit.current.skills = b.skill_count
    else
        bootstrap_set_error(skill_err)
    end

    local buff_ok, buffs, buff_err = combat.buffList()
    if buff_ok then
        b.buffs = buffs or {}
        b.buff_count = count_array(buffs)
        runtime.audit.current.buffs = b.buff_count
    else
        bootstrap_set_error(buff_err)
    end

    local active_ok, active, active_err = combat.autoActiveSkills()
    if active_ok then
        b.auto_active_skills = active or {}
        b.auto_active_count = count_array(active)
        runtime.audit.current.auto_active_skills = b.auto_active_count
    else
        bootstrap_set_error(active_err)
    end

    local auto_buff_ok, auto_buff, auto_buff_err = combat.autoBuffSkills()
    if auto_buff_ok then
        b.auto_buff_skills = auto_buff or {}
        b.auto_buff_count = count_array(auto_buff)
        runtime.audit.current.auto_buff_skills = b.auto_buff_count
    else
        bootstrap_set_error(auto_buff_err)
    end

    local target_ok, target = combat.currentTarget()
    if target_ok and target then
        b.target_id = target.id or 0
        runtime.audit.current.target_id = b.target_id
    else
        b.target_id = 0
        runtime.audit.current.target_id = 0
    end

    b.combat_ok = skill_ok and buff_ok and active_ok and auto_buff_ok
end

local function bootstrap_update_core()
    local b = runtime.bootstrap
    b.core_ok = false

    if not ok_core or not core then
        bootstrap_set_error("aion.core 不可用")
        return
    end

    local init_ok, init_err = core.ensureInit(cfg.target.pid)
    if not init_ok then
        bootstrap_set_error(init_err)
        return
    end

    b.core_ok = true
    target_validate_binding()
end

local bootstrap_steps = {
    { label = "核心", run = bootstrap_update_core },
    { label = "角色", run = bootstrap_update_character },
    { label = "地图", run = bootstrap_update_map },
    { label = "背包", run = bootstrap_update_inventory },
    { label = "任务", run = bootstrap_update_quest },
    { label = "技能/Buff", run = bootstrap_update_combat },
}

local function bootstrap_reset_values()
    local b = runtime.bootstrap
    b.initialized = false
    b.core_ok = false
    b.character_ok = false
    b.map_ok = false
    b.inventory_ok = false
    b.quest_ok = false
    b.combat_ok = false
    b.skill_count = 0
    b.buff_count = 0
    b.auto_active_count = 0
    b.auto_buff_count = 0
    b.inventory_count = 0
    b.quest_count = 0
    b.map_name = ""
    b.target_id = 0
    b.skills = {}
    b.buffs = {}
    b.auto_active_skills = {}
    b.auto_buff_skills = {}
    b.inventory_items = {}
    b.quests = {}
end

local function bootstrap_begin(reason)
    local b = runtime.bootstrap
    bootstrap_reset_values()
    b.pending = true
    b.status = "加载中"
    b.reason = reason or "后台初始化"
    b.current_step = "等待"
    b.step_index = 0
    b.step_total = #bootstrap_steps
    b.last_step_ms = 0
    b.last_error = ""
    b.last_refresh_at = now_seconds()
    set_event("开始后台初始化: " .. tostring(b.reason))
end

local function bootstrap_finish()
    local b = runtime.bootstrap
    b.pending = false
    b.initialized = b.core_ok and b.character_ok
    b.status = b.initialized and "已初始化" or "部分初始化"
    b.current_step = ""

    log_info(string.format(
        "[AionControlUI] %s: %s core=%s char=%s map=%s inv=%d quest=%d skill=%d buff=%d auto=%d/%d",
        tostring(b.reason or "后台初始化"),
        b.status,
        tostring(b.core_ok),
        tostring(b.character_ok),
        tostring(b.map_name),
        b.inventory_count or 0,
        b.quest_count or 0,
        b.skill_count or 0,
        b.buff_count or 0,
        b.auto_active_count or 0,
        b.auto_buff_count or 0
    ))
end

local function bootstrap_tick()
    local b = runtime.bootstrap
    if not b.pending then
        return
    end

    local next_index = b.step_index + 1
    local step = bootstrap_steps[next_index]
    if not step then
        bootstrap_finish()
        return
    end

    b.step_index = next_index
    b.current_step = step.label
    b.status = string.format("加载中%s %d/%d", step.label, b.step_index, b.step_total)

    local started = now_seconds()
    local ok, err = pcall(step.run)
    b.last_step_ms = math.floor((now_seconds() - started) * 1000)
    if not ok then
        bootstrap_set_error(err)
    end

    if step.label == "核心" and not b.core_ok then
        b.pending = false
        b.initialized = false
        b.status = "初始化失败"
        b.current_step = ""
        log_warn("[AionControlUI] 后台初始化失败" .. tostring(b.last_error))
        return
    end

    if b.step_index >= b.step_total then
        bootstrap_finish()
    end
end

local function sleep(ms)
    if sys and sys.sleep then
        sys.sleep(ms)
    end
end

set_event = function(text)
    runtime.last_event = text
    log_info("[AionControlUI] " .. text)
end

function combat_trim(text)
    return tostring(text or ""):match("^%s*(.-)%s*$") or ""
end

function combat_text_has_line(text, name)
    name = tostring(name or "")
    if name == "" then
        return false
    end
    for line in string.gmatch(tostring(text or ""), "[^\r\n]+") do
        line = combat_trim(line)
        if line ~= "" and string.find(name, line, 1, true) then
            return true
        end
    end
    return false
end

function combat_text_has_exact_line(text, name)
    name = combat_trim(name)
    if name == "" then
        return false
    end
    for line in string.gmatch(tostring(text or ""), "[^\r\n]+") do
        line = combat_trim(line)
        if line ~= "" and line == name then
            return true
        end
    end
    return false
end

function combat_is_ignored_summon(name)
    if not cfg.combat or cfg.combat.ignore_summons == false then
        return false
    end
    return combat_text_has_exact_line(cfg.combat.pet_names, name)
end

function combat_log(key, message, interval, force)
    if not force and not (cfg.combat and cfg.combat.debug_log == true) then
        return
    end
    local c = runtime.combat
    c.log_times = c.log_times or {}
    local now = now_seconds()
    interval = tonumber(interval)
    if interval == nil then
        interval = tonumber(cfg.combat and cfg.combat.debug_log_interval) or 2.0
    end
    key = tostring(key or "default")
    local last = tonumber(c.log_times[key]) or 0
    if not force and interval > 0 and now - last < interval then
        return
    end
    c.log_times[key] = now
    log_info("[AionCombat] " .. tostring(message or ""))
end

function combat_set_status(status, err, notify)
    local c = runtime.combat
    status = tostring(status or "")
    err = tostring(err or "")
    local changed = c.status ~= status or (err ~= "" and c.last_error ~= err)
    c.status = status
    if err ~= "" then
        c.last_error = err
    end
    if notify and changed then
        set_event("原地打怪" .. status .. (err ~= "" and (" - " .. err) or ""))
    end
end

local combat_set_status_base = combat_set_status
function combat_set_status(status, err, notify)
    local c = runtime.combat
    local old_status = c.status
    local old_error = c.last_error
    combat_set_status_base(status, err, notify)
    status = tostring(status or "")
    err = tostring(err or "")
    local changed = old_status ~= c.status or (err ~= "" and old_error ~= c.last_error)
    if changed then
        combat_log("status:" .. status, "status=" .. status .. (err ~= "" and (" err=" .. err) or ""), notify and 0 or nil, notify)
    end
end

function combat_reset_runtime(reason)
    local c = runtime.combat
    c.anchor = nil
    c.mode = ""
    c.status = "idle"
    c.target_obj = 0
    c.target_name = ""
    c.target_distance = 0
    c.loot_obj = 0
    c.loot_name = ""
    c.loot_distance = 0
    c.loot_attempts = 0
    c.loot_ignored = {}
    c.post_kill_until = 0
    c.post_kill_started_at = 0
    c.last_killed_obj = 0
    c.last_killed_interact_id = 0
    c.last_killed_name = ""
    c.last_auto_off_at = 0
    c.last_auto_on_at = 0
    c.force_auto_until = 0
    c.last_force_auto_at = 0
    c.last_attack_key_at = 0
    c.last_attack_key_obj = 0
    c.target_started_at = 0
    c.target_start_hp = 0
    c.target_last_hp = 0
    c.target_last_damage_at = 0
    c.target_ignored = {}
    c.anchor_distance = 0
    c.patrol_points = {}
    c.patrol_index = 1
    c.patrol_direction = 1
    c.patrol_laps = 0
    c.patrol_route_name = ""
    c.patrol_signature = ""
    c.last_tick_at = 0
    c.last_move_at = 0
    c.last_select_at = 0
    c.last_loot_at = 0
    c.last_loot_interact_at = 0
    c.post_loot_maintenance_pending = false
    c.post_loot_maintenance_source = ""
    c.post_loot_maintenance_at = 0
    c.log_times = {}
    c.last_error = ""
    if runtime.maintenance and type(runtime.maintenance.floor_recovery) == "table" then
        runtime.maintenance.floor_recovery.active = false
        runtime.maintenance.floor_recovery.started_at = 0
        runtime.maintenance.floor_recovery.start_hp = 0
        runtime.maintenance.floor_recovery.last_hp = 0
        runtime.maintenance.floor_recovery.start_mp_percent = 0
        runtime.maintenance.floor_recovery.last_mp_percent = 0
        runtime.maintenance.floor_recovery.last_action = ""
        runtime.maintenance.floor_recovery.last_reason = ""
    end
    if reason then
        combat_log("reset", "reset reason=" .. tostring(reason), 0, true)
    end
    if reason then
        set_event("原地打怪状态重置" .. tostring(reason))
    end
end

function combat_stop_runtime(reason)
    combat_auto_off("script-stop", true)
    combat_set_status("stopped", tostring(reason or ""), false)
    combat_log("stop", "stop reason=" .. tostring(reason or ""), 0, true)
end

function combat_is_stationary_enabled()
    return runtime.running
        and not runtime.paused
        and not (route_recovery_blocks_combat and route_recovery_blocks_combat())
        and sync_combat_enabled_from_primary_mode()
        and tonumber(cfg.primary_mode) == 1
        and tonumber(cfg.combat.mode) == 1
end

function combat_is_quest_grind_enabled()
    local guard_authorized = type(main_quest_combat_guard_authorized) == "function"
        and main_quest_combat_guard_authorized()
    local grind_authorized = cfg.leveling
        and cfg.leveling.allow_grind == true
        and runtime.main_quest
        and runtime.main_quest.active_20611_grind == true
        and type(main_quest_grind_authorized) == "function"
        and main_quest_grind_authorized()
    return runtime.running
        and not runtime.paused
        and not (route_recovery_blocks_combat and route_recovery_blocks_combat())
        and cfg.leveling
        and tonumber(cfg.primary_mode) == 2
        and runtime.main_quest
        and (grind_authorized or guard_authorized)
        and tonumber(cfg.combat.mode) == 1
end

function combat_is_patrol_enabled()
    return runtime.running
        and not runtime.paused
        and not (route_recovery_blocks_combat and route_recovery_blocks_combat())
        and sync_combat_enabled_from_primary_mode()
        and tonumber(cfg.primary_mode) == 1
        and tonumber(cfg.combat.mode) == 2
end

function combat_is_active_enabled()
    return combat_is_stationary_enabled() or combat_is_patrol_enabled()
end

function combat_patrol_signature()
    return tostring(cfg.route.route_name or "") .. "\n" .. tostring(cfg.route.route_points or "")
end

function combat_load_patrol_route(force)
    local c = runtime.combat
    local signature = combat_patrol_signature()
    if not force and c.patrol_signature == signature and #(c.patrol_points or {}) > 0 then
        return true
    end

    if not ok_route or not route_lib then
        combat_set_status("path-error", "aion.route unavailable", true)
        return false
    end

    local ok, points, warnings = route_lib.parse(cfg.route.route_points or "")
    if not ok then
        combat_set_status("path-error", "route parse failed", true)
        return false
    end

    if #(points or {}) <= 0 then
        c.patrol_points = {}
        c.patrol_signature = signature
        c.patrol_route_name = tostring(cfg.route.route_name or "")
        combat_set_status("path-empty", "combat route has no points", false)
        return false
    end

    local start_index = 1
    local start_dist = 0
    if cfg.route.start_from_nearest ~= false and route_current_position and route_nearest_point then
        local pos_ok, pos = route_current_position()
        if pos_ok and pos then
            start_index, start_dist = route_nearest_point(points, pos)
        end
    end

    c.patrol_points = points
    c.patrol_index = start_index
    c.patrol_direction = 1
    c.patrol_laps = 0
    c.patrol_route_name = tostring(cfg.route.route_name or "")
    c.patrol_signature = signature
    c.anchor = nil
    c.last_move_at = 0
    if warnings and #warnings > 0 then
        c.last_error = "route warnings: " .. tostring(#warnings)
    end
    combat_log("patrol-route",
        string.format("loaded route=%s points=%d start_index=%d nearest_dist=%.1f",
            tostring(c.patrol_route_name),
            #points,
            tonumber(start_index) or 1,
            tonumber(start_dist) or 0),
        0,
        true)
    return true
end

function combat_current_patrol_anchor()
    local c = runtime.combat
    if not combat_load_patrol_route(false) then
        return nil
    end

    local count = #(c.patrol_points or {})
    if count <= 0 then
        return nil
    end

    c.patrol_index = math.max(1, math.min(count, tonumber(c.patrol_index) or 1))
    local point = c.patrol_points[c.patrol_index]
    if not point then
        return nil
    end

    c.mode = "patrol"
    c.anchor = {
        x = tonumber(point.x) or 0,
        y = tonumber(point.y) or 0,
        z = tonumber(point.z) or 0,
    }
    return c.anchor
end

function combat_advance_patrol_anchor()
    local c = runtime.combat
    if not ok_route or not route_lib then
        combat_set_status("path-error", "aion.route unavailable", true)
        return false
    end

    local count = #(c.patrol_points or {})
    if count <= 0 then
        combat_set_status("path-empty", "combat route has no points", false)
        return false
    end

    local old_index = tonumber(c.patrol_index) or 1
    local next_index, next_direction, done = route_lib.nextIndex(
        old_index,
        tonumber(c.patrol_direction) or 1,
        count,
        {
            loop = cfg.route.loop,
            reverse_on_end = cfg.route.reverse_on_end,
        }
    )

    if done then
        local finished_field = "route_points"
        runtime.route.completed_field = finished_field
        combat_auto_off("path-complete", true)
        combat_set_status("path-complete", "", true)
        runtime.running = false
        runtime.paused = false
        runtime.status = "stopped"
        runtime.active_mode = "none"
        runtime.ui_visible = true
        return false
    end

    if cfg.route.loop and next_index == 1 and old_index ~= 1 then
        c.patrol_laps = (tonumber(c.patrol_laps) or 0) + 1
    elseif cfg.route.reverse_on_end and next_direction ~= c.patrol_direction then
        c.patrol_laps = (tonumber(c.patrol_laps) or 0) + 1
    end

    c.patrol_index = next_index
    c.patrol_direction = next_direction
    c.anchor = nil
    c.last_move_at = 0
    c.target_obj = 0
    c.target_name = ""
    c.target_distance = 0
    combat_set_status("patrol-next", string.format("%d/%d", next_index, count), false)
    return true
end

function combat_patrol_text()
    local c = runtime.combat
    local count = #(c.patrol_points or {})
    if count <= 0 then
        return "route=0/0 laps=0"
    end
    return string.format("route=%d/%d laps=%d",
        tonumber(c.patrol_index) or 1,
        count,
        tonumber(c.patrol_laps) or 0)
end

function combat_skip_arrived_patrol_anchors(char)
    local c = runtime.combat
    local count = #(c.patrol_points or {})
    if count <= 0 or not char or not ok_core or not core then
        return false
    end

    local waypoint_radius = math.max(0.5, tonumber(cfg.route.waypoint_radius) or 3)
    local max_skip = math.min(count, 50)
    local skipped = 0
    while skipped < max_skip do
        local anchor = combat_current_patrol_anchor()
        if not anchor then
            return skipped > 0
        end

        local dist = core.distance3(char, anchor)
        c.anchor_distance = dist
        if dist > waypoint_radius then
            break
        end

        if not combat_advance_patrol_anchor() then
            return skipped > 0
        end
        skipped = skipped + 1
    end

    if skipped > 0 then
        combat_current_patrol_anchor()
        if c.anchor then
            c.anchor_distance = core.distance3(char, c.anchor)
        end
        combat_log("patrol-skip",
            string.format("skipped reached waypoints count=%d %s dist=%.1f",
                skipped,
                combat_patrol_text(),
                tonumber(c.anchor_distance) or 0),
            1.0,
            false)
    end
    return skipped > 0
end

function combat_configured_anchor()
    if not cfg.combat or cfg.combat.anchor_enabled ~= true then
        return nil
    end
    return {
        x = tonumber(cfg.combat.anchor_x) or 0,
        y = tonumber(cfg.combat.anchor_y) or 0,
        z = tonumber(cfg.combat.anchor_z) or 0,
    }
end

function combat_set_stationary_anchor_current()
    if not ok_core or not core then
        combat_set_status("error", "aion.core unavailable", true)
        return false
    end

    local ok, pos, err = core.getPosition()
    if not ok or not pos then
        combat_set_status("error", "read current position failed: " .. tostring(err), true)
        return false
    end

    cfg.combat.anchor_enabled = true
    cfg.combat.anchor_x = tonumber(pos.x) or 0
    cfg.combat.anchor_y = tonumber(pos.y) or 0
    cfg.combat.anchor_z = tonumber(pos.z) or 0
    runtime.combat.anchor = {
        x = cfg.combat.anchor_x,
        y = cfg.combat.anchor_y,
        z = cfg.combat.anchor_z,
    }
    runtime.combat.target_obj = 0
    runtime.combat.target_name = ""
    runtime.combat.target_distance = 0
    runtime.combat.anchor_distance = 0
    combat_set_status("anchor-ready", combat_anchor_text(), true)
    combat_log("anchor-config", "stationary anchor configured pos=" .. combat_anchor_text(), 0, true)
    set_event("已设定原地打怪坐标 " .. combat_anchor_text())
    if type(save_config) == "function" then
        save_config()
    end
    return true
end

function combat_anchor_text()
    local a = runtime.combat.anchor
    if not a then
        return "未记录"
    end
    return string.format("%.2f, %.2f, %.2f", tonumber(a.x) or 0, tonumber(a.y) or 0, tonumber(a.z) or 0)
end

function combat_ensure_anchor()
    if runtime.combat.anchor then
        return true
    end
    local configured = combat_configured_anchor()
    if configured then
        runtime.combat.anchor = configured
        combat_set_status("anchor-ready", combat_anchor_text(), true)
        combat_log("anchor", "anchor set configured pos=" .. combat_anchor_text(), 0, true)
        return true
    end
    if not ok_core or not core then
        combat_set_status("error", "aion.core 不可用", true)
        return false
    end
    local ok, pos, err = core.getPosition()
    if not ok or not pos then
        combat_set_status("error", "读取中心点失败: " .. tostring(err), true)
        return false
    end
    runtime.combat.anchor = { x = pos.x or 0, y = pos.y or 0, z = pos.z or 0 }
    combat_set_status("anchor-ready", combat_anchor_text(), true)
    combat_log("anchor", "anchor set pos=" .. combat_anchor_text(), 0, true)
    return true
end

function combat_entity_obj(e)
    return tonumber(e and (e.obj or e.IEntity or e.id)) or 0
end

function combat_loot_key(e)
    local obj = combat_entity_obj(e)
    if obj > 0 then
        return obj
    end
    return tonumber(e and e.interact_id) or 0
end

function combat_find_entity_by_obj(list, obj)
    obj = tonumber(obj) or 0
    if obj <= 0 then
        return nil
    end
    for _, e in ipairs(list or {}) do
        if combat_entity_obj(e) == obj then
            return e
        end
    end
    return nil
end

function combat_is_name_allowed(name)
    if combat_text_has_line(cfg.combat.blacklist_names, name) then
        return false
    end
    local wanted = combat_trim(cfg.combat.target_names)
    if wanted ~= "" and not combat_text_has_line(wanted, name) then
        return false
    end
    return true
end

function combat_is_target_candidate(e, anchor)
    if type(e) ~= "table" or not anchor then
        return false
    end
    if e.dead == true then
        return false
    end
    if (tonumber(e.type) or 0) ~= 2 then
        return false
    end
    if (tonumber(e.lootable) or 0) ~= 0 then
        return false
    end

    local name = tostring(e.name or "")
    if name == "" or not combat_is_name_allowed(name) then
        return false
    end
    if combat_is_ignored_summon(name) then
        return false
    end

    local hp = tonumber(e.hp) or 0
    local mhp = tonumber(e.mhp) or 0
    if mhp > 0 and hp <= 0 then
        return false
    end

    local tag = tostring(e.tag or "")
    if tag == "NPC" then
        return false
    end

    local level = tonumber(e.level) or 0
    local min_level = tonumber(cfg.combat.min_level) or 1
    local max_level = tonumber(cfg.combat.max_level) or 99
    if level > 0 and (level < min_level or level > max_level) then
        return false
    end

    if cfg.combat.avoid_elite then
        local rating = tonumber(e.rating) or 0
        if rating > 1 or e.is_mutant == true then
            return false
        end
    end

    local radius = tonumber(cfg.combat.radius) or 35
    if ok_core and core and core.distance3(anchor, e) > radius then
        return false
    end

    return combat_entity_obj(e) > 0
end

function combat_target_reject_reason(e, anchor)
    if type(e) ~= "table" or not anchor then
        return "invalid"
    end
    if e.dead == true then
        return "dead"
    end
    if (tonumber(e.type) or 0) ~= 2 then
        return "type"
    end
    if (tonumber(e.lootable) or 0) ~= 0 then
        return "lootable"
    end

    local name = tostring(e.name or "")
    if name == "" or not combat_is_name_allowed(name) then
        return "name"
    end
    if combat_is_ignored_summon(name) then
        return "summon"
    end

    local hp = tonumber(e.hp) or 0
    local mhp = tonumber(e.mhp) or 0
    if mhp > 0 and hp <= 0 then
        return "hp-zero"
    end

    local tag = tostring(e.tag or "")
    if tag == "NPC" then
        return "npc"
    end

    local level = tonumber(e.level) or 0
    local min_level = tonumber(cfg.combat.min_level) or 1
    local max_level = tonumber(cfg.combat.max_level) or 99
    if level > 0 and (level < min_level or level > max_level) then
        return "level"
    end

    if cfg.combat.avoid_elite then
        local rating = tonumber(e.rating) or 0
        if rating > 1 or e.is_mutant == true then
            return "elite"
        end
    end

    local obj = combat_entity_obj(e)
    if obj <= 0 then
        return "obj"
    end

    local ignored_until = runtime.combat.target_ignored and runtime.combat.target_ignored[obj]
    if ignored_until then
        if now_seconds() < ignored_until then
            return "ignored"
        end
        runtime.combat.target_ignored[obj] = nil
    end

    local radius = tonumber(cfg.combat.radius) or 35
    if ok_core and core and core.distance3(anchor, e) > radius then
        return "radius"
    end
    return nil
end

function combat_is_target_candidate(e, anchor)
    return combat_target_reject_reason(e, anchor) == nil
end

function combat_find_current_entity(list, anchor)
    if not ok_combat or not combat then
        return nil
    end
    local ok, current = combat.currentTarget()
    if not ok or not current then
        return nil
    end
    local current_obj = tonumber(current.obj or current.IEntity or 0) or 0
    local current_id = tonumber(current.id or 0) or 0
    if current_obj <= 0 and current_id <= 0 then
        return nil
    end
    for _, e in ipairs(list or {}) do
        local obj = combat_entity_obj(e)
        local id = tonumber(e.id) or 0
        if (current_obj > 0 and obj == current_obj) or (current_id > 0 and id == current_id) then
            local reject_reason = combat_target_reject_reason(e, anchor)
            if reject_reason == nil or reject_reason == "radius" then
                return e
            end
            return nil
        end
    end
    return nil
end

function combat_current_target_matches_obj(obj)
    obj = tonumber(obj) or 0
    if obj <= 0 or not ok_combat or not combat then
        return false
    end
    local ok, current = combat.currentTarget()
    if not ok or type(current) ~= "table" then
        return false
    end
    local current_obj = tonumber(current.obj or current.IEntity or 0) or 0
    return current_obj > 0 and current_obj == obj
end

function combat_is_aggressive_target(e)
    if type(e) ~= "table" then
        return false
    end
    return tostring(e.tag or "") == "主动怪"
end

function combat_target_score(e, char)
    local dist = ok_core and core and core.distance3(char, e) or 0
    local policy = tonumber(cfg.combat.target_policy) or 1
    if policy == 3 then
        local hp = tonumber(e.hp) or 0
        local mhp = math.max(1, tonumber(e.mhp) or 1)
        return (hp / mhp) * 1000 + dist
    end
    if policy == 6 then
        if combat_is_aggressive_target(e) then
            return dist
        end
        return 10000 + dist
    end
    return dist
end

function combat_choose_target(list, char, anchor)
    local best, best_score = nil, math.huge
    local checked, accepted = 0, 0
    local reject_counts = {}
    local type2_samples = {}
    local near_samples = {}
    local sample_radius = tonumber(cfg.combat.radius) or 35
    for _, e in ipairs(list or {}) do
        checked = checked + 1
        local reason = combat_target_reject_reason(e, anchor)
        local dist = ok_core and core and core.distance3(char, e) or 0
        if #near_samples < 8 and (dist <= sample_radius or (tonumber(e and e.type) or 0) == 2) then
            table.insert(near_samples, string.format("t=%s name=%s dist=%.1f hp=%s/%s tag=%s type_name=%s rating=%s mutant=%s loot=%s iid=%s dead=%s reason=%s",
                tostring(e and e.type or ""),
                tostring(e and e.name or ""),
                tonumber(dist) or 0,
                tostring(e and e.hp or ""),
                tostring(e and e.mhp or ""),
                tostring(e and e.tag or ""),
                tostring(e and e.type_name or ""),
                tostring(e and e.rating or ""),
                tostring(e and e.is_mutant or ""),
                tostring(e and e.lootable or ""),
                tostring(e and e.interact_id or ""),
                tostring(e and e.dead or ""),
                tostring(reason or "accepted")))
        end
        if not reason then
            accepted = accepted + 1
            local score = combat_target_score(e, char)
            if score < best_score then
                best = e
                best_score = score
            end
        else
            reject_counts[reason] = (reject_counts[reason] or 0) + 1
            if (tonumber(e and e.type) or 0) == 2 and #type2_samples < 5 then
                table.insert(type2_samples, string.format("%s:dist=%.1f hp=%s/%s tag=%s iid=%s reason=%s",
                    tostring(e.name or ""),
                    tonumber(dist) or 0,
                    tostring(e.hp or ""),
                    tostring(e.mhp or ""),
                    tostring(e.tag or ""),
                    tostring(e.interact_id or ""),
                    tostring(reason)))
            end
        end
    end
    local reject_parts = {}
    for reason, count in pairs(reject_counts) do
        table.insert(reject_parts, tostring(reason) .. "=" .. tostring(count))
    end
    table.sort(reject_parts)
    local reject_text = table.concat(reject_parts, ",")
    local sample_text = table.concat(type2_samples, " | ")
    local near_sample_text = table.concat(near_samples, " | ")
    if best then
        local best_dist = ok_core and core and core.distance3(char, best) or 0
        combat_log("target-scan",
            string.format("target scan checked=%d accepted=%d best=%s obj=%s dist=%.1f hp=%s/%s tag=%s type_name=%s level=%s rating=%s iid=%s score=%.2f rejects=%s",
                checked,
                accepted,
                tostring(best.name or ""),
                tostring(combat_entity_obj(best)),
                tonumber(best_dist) or 0,
                tostring(best.hp or ""),
                tostring(best.mhp or ""),
                tostring(best.tag or ""),
                tostring(best.type_name or ""),
                tostring(best.level or ""),
                tostring(best.rating or ""),
                tostring(best.interact_id or ""),
                tonumber(best_score) or 0,
                reject_text),
            nil,
            false)
    else
        combat_log("target-scan",
            string.format("target scan checked=%d accepted=%d best=nil rejects=%s type2=%s near=%s",
                checked,
                accepted,
                reject_text,
                sample_text,
                near_sample_text),
            nil,
            false)
    end
    return best
end

function combat_loot_reject_reason(e, anchor)
    if not cfg.combat.loot_enabled then
        return "disabled"
    end
    if type(e) ~= "table" or not anchor then
        return "invalid"
    end
    if (tonumber(e.lootable) or 0) == 0 then
        return "not-lootable"
    end
    if (tonumber(e.type) or 0) ~= 2 then
        return "type"
    end

    local obj = combat_loot_key(e)
    if obj <= 0 then
        return "obj"
    end

    local ignored_until = runtime.combat.loot_ignored and runtime.combat.loot_ignored[obj]
    if ignored_until then
        if now_seconds() < ignored_until then
            return "ignored"
        end
        runtime.combat.loot_ignored[obj] = nil
    end

    local radius = tonumber(cfg.combat.loot_radius) or tonumber(cfg.combat.radius) or 35
    if ok_core and core and core.distance3(anchor, e) > radius then
        return "radius"
    end
    return nil
end

function combat_is_loot_candidate(e, anchor)
    return combat_loot_reject_reason(e, anchor) == nil
end

function combat_choose_loot(list, char, anchor)
    local best, best_dist = nil, math.huge
    local checked, accepted = 0, 0
    local reject_counts = {}
    local loot_samples = {}
    for _, e in ipairs(list or {}) do
        checked = checked + 1
        local reason = combat_loot_reject_reason(e, anchor)
        if not reason then
            accepted = accepted + 1
            local dist = ok_core and core and core.distance3(char, e) or 0
            if dist < best_dist then
                best = e
                best_dist = dist
            end
        else
            reject_counts[reason] = (reject_counts[reason] or 0) + 1
            if (tonumber(e and e.lootable) or 0) ~= 0 and #loot_samples < 5 then
                local dist = ok_core and core and core.distance3(char, e) or tonumber(e and e.distance) or 0
                table.insert(loot_samples, string.format("name=%s type=%s dist=%.1f hp=%s/%s iid=%s reason=%s",
                    tostring(e and e.name or ""),
                    tostring(e and e.type or ""),
                    tonumber(dist) or 0,
                    tostring(e and e.hp or ""),
                    tostring(e and e.mhp or ""),
                    tostring(e and e.interact_id or ""),
                    tostring(reason)))
            end
        end
    end
    local reject_parts = {}
    for reason, count in pairs(reject_counts) do
        table.insert(reject_parts, tostring(reason) .. "=" .. tostring(count))
    end
    table.sort(reject_parts)
    local reject_text = table.concat(reject_parts, ",")
    local sample_text = table.concat(loot_samples, " | ")
    if best then
        best.distance = best_dist
        combat_log("loot-scan",
            string.format("loot scan checked=%d accepted=%d best=%s obj=%s dist=%.1f rejects=%s samples=%s",
                checked,
                accepted,
                tostring(best.name or ""),
                tostring(combat_loot_key(best)),
                tonumber(best_dist) or 0,
                reject_text,
                sample_text),
            nil,
            false)
    else
        combat_log("loot-scan",
            string.format("loot scan checked=%d accepted=%d best=nil rejects=%s samples=%s",
                checked,
                accepted,
                reject_text,
                sample_text),
            nil,
            false)
    end
    return best
end

function combat_clear_last_kill(reason, force_log)
    local c = runtime.combat
    local obj = tonumber(c.last_killed_obj) or 0
    local interact_id = tonumber(c.last_killed_interact_id) or 0
    local name = tostring(c.last_killed_name or "")
    c.last_killed_obj = 0
    c.last_killed_interact_id = 0
    c.last_killed_name = ""
    c.post_kill_until = 0
    c.post_kill_started_at = 0
    if force_log and (obj > 0 or interact_id > 0 or name ~= "") then
        combat_log("post-kill-clear:" .. tostring(obj),
            string.format("clear last killed reason=%s name=%s obj=%s interact_id=%s",
                tostring(reason or ""),
                name,
                tostring(obj),
                tostring(interact_id)),
            0,
            true)
    end
end

function combat_mark_post_loot_maintenance(source)
    local c = runtime.combat
    c.post_loot_maintenance_pending = true
    c.post_loot_maintenance_source = tostring(source or "")
    c.post_loot_maintenance_at = now_seconds()
end

function combat_clear_pending_loot(reason, ignore)
    local c = runtime.combat
    local loot_key = tonumber(c.loot_obj) or 0
    local loot_name = tostring(c.loot_name or "")
    local attempts = tonumber(c.loot_attempts) or 0
    if loot_key > 0 and ignore then
        combat_ignore_loot(loot_key)
    end
    c.loot_obj = 0
    c.loot_name = ""
    c.loot_distance = 0
    c.loot_attempts = 0
    c.last_loot_interact_at = 0
    if loot_key > 0 then
        combat_log("loot-clear-pending:" .. tostring(loot_key),
            string.format("clear pending loot reason=%s name=%s obj=%s attempts=%d ignore=%s",
                tostring(reason or ""),
                loot_name,
                tostring(loot_key),
                attempts,
                tostring(ignore == true)),
            0,
            true)
    end
end

function combat_extend_post_kill(reason, seconds)
    local c = runtime.combat
    if (tonumber(c.last_killed_obj) or 0) <= 0 then
        return
    end
    local extend_until = now_seconds() + math.max(0.5, tonumber(seconds) or 2.0)
    if extend_until > (tonumber(c.post_kill_until) or 0) then
        c.post_kill_until = extend_until
        combat_log("post-kill-extend:" .. tostring(c.last_killed_obj or 0),
            string.format("extend last killed loot window reason=%s name=%s obj=%s remain=%.1fs",
                tostring(reason or ""),
                tostring(c.last_killed_name or ""),
                tostring(c.last_killed_obj or 0),
                math.max(0, c.post_kill_until - now_seconds())),
            1.0,
            false)
    end
end

function combat_match_last_killed(e)
    if type(e) ~= "table" then
        return false
    end
    local c = runtime.combat
    local last_obj = tonumber(c.last_killed_obj) or 0
    local last_interact_id = tonumber(c.last_killed_interact_id) or 0
    local obj = combat_entity_obj(e)
    local interact_id = tonumber(e.interact_id) or 0
    if last_obj > 0 and obj == last_obj then
        return true
    end
    if last_interact_id > 0 and interact_id == last_interact_id then
        return true
    end
    return false
end

function combat_find_last_killed_loot(list, char, anchor)
    local checked = 0
    local found = nil
    for _, e in ipairs(list or {}) do
        checked = checked + 1
        if combat_match_last_killed(e) then
            found = e
            break
        end
    end
    if not found then
        return nil, "missing", nil, checked
    end

    local reason = combat_loot_reject_reason(found, anchor)
    if reason then
        return nil, reason, found, checked
    end
    if ok_core and core then
        found.distance = core.distance3(char, found)
    end
    return found, nil, found, checked
end

function combat_loot_entity_state(loot_key, char)
    loot_key = tonumber(loot_key) or 0
    if loot_key <= 0 then
        return { ok = false, err = "invalid loot key" }
    end
    if not ok_entity or not entity then
        return { ok = false, err = "aion.entity unavailable" }
    end

    local list_ok, list, list_err = entity.list()
    if not list_ok then
        return { ok = false, err = list_err or "entity list failed" }
    end

    for _, e in ipairs(list or {}) do
        if combat_loot_key(e) == loot_key then
            local dist = tonumber(e.distance) or 0
            if ok_core and core and char then
                dist = core.distance3(char, e)
            end
            return {
                ok = true,
                found = true,
                lootable = (tonumber(e.lootable) or 0) ~= 0,
                lootable_value = tonumber(e.lootable) or 0,
                name = tostring(e.name or ""),
                distance = dist,
                interact_id = tonumber(e.interact_id) or 0,
            }
        end
    end

    return { ok = true, found = false }
end

function combat_confirm_loot_cleared(source, detail)
    local c = runtime.combat
    local loot_key = tonumber(c.loot_obj) or 0
    if loot_key <= 0 then
        loot_key = tonumber(c.last_killed_obj) or 0
    end
    if loot_key <= 0 then
        combat_log("loot-verify-skip",
            "loot verify skipped: no current loot key source=" .. tostring(source or ""),
            0,
            true)
        return true, "no-key"
    end

    local char = nil
    if ok_core and core and type(core.getCharacter) == "function" then
        local char_ok, value = core.getCharacter()
        if char_ok then
            char = value
        end
    end

    local last_state = nil
    for attempt = 1, 6 do
        sleep(attempt == 1 and 160 or 120)
        local state = combat_loot_entity_state(loot_key, char)
        last_state = state
        local active, active_detail = combat_loot_dialog_active()

        if state.ok and (not state.found or not state.lootable) then
            if active then
                combat_close_loot_dialog("verified-cleared:" .. tostring(source or ""))
            end
            combat_log("loot-verify-ok:" .. tostring(loot_key),
                string.format("loot verify ok source=%s obj=%s found=%s lootable=%s active=%s detail=%s %s",
                    tostring(source or ""),
                    tostring(loot_key),
                    tostring(state.found == true),
                    tostring(state.lootable_value or ""),
                    tostring(active),
                    tostring(active_detail or ""),
                    tostring(detail or "")),
                0,
                true)
            return true, "cleared"
        end

        combat_log("loot-verify-pending:" .. tostring(loot_key),
            string.format("loot verify pending source=%s obj=%s ok=%s found=%s lootable=%s dist=%s active=%s err=%s",
                tostring(source or ""),
                tostring(loot_key),
                tostring(state.ok == true),
                tostring(state.found == true),
                tostring(state.lootable_value or ""),
                tostring(state.distance or ""),
                tostring(active),
                tostring(state.err or active_detail or "")),
            1.0,
            false)
    end

    local err = "loot target still lootable"
    if last_state and not last_state.ok then
        err = tostring(last_state.err or err)
    end
    combat_log("loot-verify-failed:" .. tostring(loot_key),
        "loot verify failed source=" .. tostring(source or "") ..
        " obj=" .. tostring(loot_key) ..
        " err=" .. tostring(err),
        0,
        true)
    combat_close_loot_dialog("verify-failed:" .. tostring(source or ""))
    c.last_loot_interact_at = 0
    combat_set_status("loot-verify-failed", err, true)
    return false, err
end

function combat_finish_loot(source, detail)
    local c = runtime.combat
    local now = now_seconds()
    local picked_obj = tonumber(c.loot_obj) or 0
    if picked_obj <= 0 then
        picked_obj = tonumber(c.last_killed_obj) or 0
    end
    c.last_loot_at = now
    combat_mark_post_loot_maintenance(source)
    c.loot_obj = 0
    c.loot_name = ""
    c.loot_distance = 0
    c.loot_attempts = 0
    c.last_loot_interact_at = 0
    combat_clear_last_kill("picked", false)
    combat_set_status("looted", tostring(source or ""), false)
    combat_log("loot-picked",
        "loot picked source=" .. tostring(source or "") ..
        " obj=" .. tostring(picked_obj) ..
        " done_current=1 " .. tostring(detail or ""),
        0,
        true)
end

function combat_ui_obj(ctrl)
    if type(ctrl) ~= "table" then
        return tonumber(ctrl) or ctrl
    end
    return ctrl.obj or ctrl.addr
end

function combat_score_loot_all_button(child, parent)
    child = child or {}
    parent = parent or {}
    local obj = combat_ui_obj(child)
    if not obj or tonumber(obj) == 0 then
        return nil
    end
    if child.visible ~= true then
        return nil
    end

    local name = tostring(child.name or "")
    local lower = string.lower(name)
    local score = 0
    local function has(text)
        return lower:find(text, 1, true) ~= nil
    end

    if has("all") then score = score + 80 end
    if has("get") or has("pickup") or has("loot") or has("take") or has("receive") or has("acquire") then
        score = score + 50
    end
    if has("button") or has("btn") or has("ok") then
        score = score + 20
    end
    if has("close") or has("cancel") or has("refuse") or has("prev") then
        score = score - 120
    end

    local x = tonumber(child.x) or 0
    local y = tonumber(child.y) or 0
    local px = tonumber(parent.x) or 0
    local py = tonumber(parent.y) or 0

    local global_rect = (px > 0 or py > 0) and
        x >= px + 35 and x <= px + 170 and
        y >= py + 220 and y <= py + 300
    local relative_rect =
        x >= 35 and x <= 170 and
        y >= 220 and y <= 300
    if global_rect or relative_rect then
        score = score + 65
    end

    local expected_x = (px > 0 and px + 95 or 95)
    local expected_y = (py > 0 and py + 255 or 255)
    local dx = x - expected_x
    local dy = y - expected_y
    local dist = math.sqrt(dx * dx + dy * dy)
    score = score - math.min(35, dist / 12)

    if score <= 15 then
        return nil
    end
    return score
end

function combat_click_loot_all_button()
    local ok_ui, ui_runtime = pcall(require, "aion.ui")
    if not ok_ui or not ui_runtime then
        return false, "aion.ui unavailable"
    end

    local parents = { "loot_dialog", "dlg_loot" }
    local best = nil
    local best_parent = nil
    local best_score = nil
    local best_parent_name = ""
    local last_err = nil

    for _, parent_name in ipairs(parents) do
        local parent = {}
        local find_ok, found = ui_runtime.find(parent_name)
        if find_ok and type(found) == "table" then
            parent = found
        end

        local ok, children, err = ui_runtime.children(parent_name, 8)
        if not ok then
            last_err = err
            combat_log("loot-ui-children:" .. tostring(parent_name),
                "loot children failed parent=" .. tostring(parent_name) .. " err=" .. tostring(err),
                1.0,
                false)
        else
            children = children or {}
            combat_log("loot-ui-children:" .. tostring(parent_name),
                "loot children parent=" .. tostring(parent_name) .. " count=" .. tostring(#children),
                1.0,
                false)
            for index, child in ipairs(children) do
                local score = combat_score_loot_all_button(child, parent)
                if score and (not best_score or score > best_score) then
                    best = child
                    best_parent = parent
                    best_parent_name = parent_name
                    best_score = score
                end
                if index <= 12 then
                    combat_log("loot-ui-child-sample:" .. tostring(parent_name) .. ":" .. tostring(index),
                        string.format("loot child parent=%s idx=%d obj=%s name=%s visible=%s x=%.0f y=%.0f score=%s",
                            tostring(parent_name),
                            tonumber(index) or 0,
                            tostring(combat_ui_obj(child) or 0),
                            tostring((child and child.name) or ""),
                            tostring(child and child.visible == true),
                            tonumber(child and child.x) or 0,
                            tonumber(child and child.y) or 0,
                            tostring(score or "")),
                        0,
                        false)
                end
            end
        end
    end

    if not best then
        return false, "loot all button not found; last_err=" .. tostring(last_err)
    end

    local obj = combat_ui_obj(best)
    local ok, clicked, err = ui_runtime.click(obj)
    if ok and clicked ~= false then
        combat_log("loot-click-all",
            string.format("clicked loot all parent=%s obj=%s name=%s x=%.0f y=%.0f score=%.1f parent_xy=%.0f,%.0f",
                tostring(best_parent_name),
                tostring(obj),
                tostring(best.name or ""),
                tonumber(best.x) or 0,
                tonumber(best.y) or 0,
                tonumber(best_score) or 0,
                tonumber(best_parent and best_parent.x) or 0,
                tonumber(best_parent and best_parent.y) or 0),
            0,
            true)
        return true, nil
    end

    return false, "ClickButton failed obj=" .. tostring(obj) .. " err=" .. tostring(err) .. " clicked=" .. tostring(clicked)
end

function combat_close_loot_dialog(reason)
    local ok_ui, ui_runtime = pcall(require, "aion.ui")
    if not ok_ui or not ui_runtime then
        return false, "aion.ui unavailable"
    end

    local parents = { "loot_dialog", "dlg_loot" }
    for _, parent_name in ipairs(parents) do
        local ok, children = ui_runtime.children(parent_name, 8)
        if ok and children then
            for _, child in ipairs(children) do
                local name = string.lower(tostring(child and child.name or ""))
                local obj = combat_ui_obj(child)
                if child and child.visible == true and obj and tonumber(obj) ~= 0 and
                    (name == "cancel" or name == "close" or name:find("close", 1, true)) then
                    local click_ok, clicked, err = ui_runtime.click(obj)
                    if click_ok and clicked ~= false then
                        combat_log("loot-close",
                            "closed stale loot dialog parent=" .. tostring(parent_name) ..
                            " button=" .. tostring(child.name or "") ..
                            " obj=" .. tostring(obj) ..
                            " reason=" .. tostring(reason or ""),
                            0,
                            true)
                        return true, nil
                    end
                    return false, "close click failed obj=" .. tostring(obj) .. " err=" .. tostring(err)
                end
            end
        end
    end

    return false, "no visible close button"
end

function combat_wait_pickup_dialog(timeout_ms, interval_ms)
    local timeout = math.max(50, tonumber(timeout_ms) or 1000) / 1000.0
    local interval = math.max(20, tonumber(interval_ms) or 100)
    local started = now_seconds()
    local last_err = nil
    while now_seconds() - started <= timeout do
        local ok, err = combat_pickup_dialog()
        if ok then
            return true, nil
        end
        last_err = err
        sleep(interval)
    end
    return false, last_err or "loot dialog timeout"
end

function combat_loot_dialog_active()
    local ok_ui, ui_runtime = pcall(require, "aion.ui")
    if not ok_ui or not ui_runtime then
        return false, "aion.ui unavailable"
    end

    local parents = { "loot_dialog", "dlg_loot" }
    local details = {}
    for _, parent_name in ipairs(parents) do
        local find_ok, parent = ui_runtime.find(parent_name)
        if find_ok and type(parent) == "table" and parent.visible == true then
            return true, "parent=" .. tostring(parent_name)
        end

        local ok, children = ui_runtime.children(parent_name, 8)
        if ok and children then
            local visible_count = 0
            for _, child in ipairs(children) do
                if child and child.visible == true then
                    visible_count = visible_count + 1
                end
            end
            details[#details + 1] = tostring(parent_name) .. ":visible_children=" .. tostring(visible_count)
            if visible_count > 0 then
                return true, details[#details]
            end
        else
            details[#details + 1] = tostring(parent_name) .. ":children_unavailable"
        end
    end

    return false, table.concat(details, ",")
end

function combat_pickup_dialog()
    if not cfg.combat.loot_enabled then
        return false
    end

    local active, active_detail = combat_loot_dialog_active()
    if not active then
        combat_log("loot-dialog",
            "loot dialog not open; wait after pickup key detail=" .. tostring(active_detail),
            1.0,
            false)
        return false, "loot dialog not open"
    end

    local ok_loot, loot_runtime = pcall(require, "aion.loot")
    local api_err = nil
    local api_picked = false
    if ok_loot and loot_runtime then
        local ok, picked, err = loot_runtime.pickupDialog()
        if ok and picked == true then
            api_picked = true
            sleep(120)
        else
            api_err = err or tostring(picked)
        end
        combat_log("loot-api",
            "LootPickup result active=" .. tostring(active_detail) ..
            " picked=" .. tostring(picked) .. " err=" .. tostring(err),
            1.0,
            false)
    else
        api_err = "aion.loot unavailable"
        combat_log("loot-api",
            "LootPickup unavailable: " .. tostring(api_err),
            1.0,
            false)
    end

    local clicked, click_err = combat_click_loot_all_button()
    if clicked then
        local verified, verify_err = combat_confirm_loot_cleared("click-all", "")
        if verified then
            combat_finish_loot("click-all", "")
            return true
        end
        return false, verify_err or "loot click did not clear target"
    end

    if api_picked then
        local verified, verify_err = combat_confirm_loot_cleared("LootPickup", "")
        if verified then
            combat_finish_loot("LootPickup", "verified_clear")
            return true
        end
        local active, active_detail = combat_loot_dialog_active()
        combat_log("loot-api-uncertain",
            "LootPickup returned true, but loot dialog still active; click_err=" .. tostring(click_err) ..
            " detail=" .. tostring(active_detail),
            0,
            true)
        return false, verify_err or ("LootPickup true but loot target still active: " .. tostring(click_err))
    end

    combat_log("loot-dialog",
        "loot dialog not ready api_err=" .. tostring(api_err) .. " click_err=" .. tostring(click_err),
        nil,
        false)
    return false, click_err or api_err
end

function combat_ignore_loot(obj)
    obj = tonumber(obj) or 0
    if obj <= 0 then
        return
    end
    runtime.combat.loot_ignored = runtime.combat.loot_ignored or {}
    runtime.combat.loot_ignored[obj] = now_seconds() + 20
    combat_log("loot-ignore:" .. tostring(obj), "loot ignored obj=" .. tostring(obj), 0, true)
end

function combat_loot_max_attempts()
    return math.max(1, math.min(2, tonumber(cfg.combat.loot_max_attempts) or 2))
end

function combat_ignore_target(obj, reason, name)
    obj = tonumber(obj) or 0
    if obj <= 0 then
        return
    end
    local seconds = math.max(1, tonumber(cfg.combat.target_ignore_seconds) or 20)
    runtime.combat.target_ignored = runtime.combat.target_ignored or {}
    runtime.combat.target_ignored[obj] = now_seconds() + seconds
    combat_log("target-ignore:" .. tostring(obj),
        string.format("target ignored reason=%s name=%s obj=%s seconds=%.1f",
            tostring(reason or ""),
            tostring(name or ""),
            tostring(obj),
            seconds),
        0,
        true)
end

function combat_reset_target_progress()
    local c = runtime.combat
    c.target_started_at = 0
    c.target_start_hp = 0
    c.target_last_hp = 0
    c.target_last_damage_at = 0
end

function combat_begin_target_progress(target, obj)
    local c = runtime.combat
    local now = now_seconds()
    local hp = tonumber(target and target.hp) or 0
    c.target_started_at = now
    c.target_start_hp = hp
    c.target_last_hp = hp
    c.target_last_damage_at = now
end

function combat_target_failure_reason(target, obj)
    if not combat_uses_attack_key() then
        return nil
    end
    obj = tonumber(obj) or 0
    if obj <= 0 or type(target) ~= "table" then
        return nil
    end

    local c = runtime.combat
    local now = now_seconds()
    local hp = tonumber(target.hp) or 0
    local last_hp = tonumber(c.target_last_hp) or 0
    if hp > 0 and last_hp > 0 and hp < last_hp then
        c.target_last_hp = hp
        c.target_last_damage_at = now
        return nil
    end
    if hp > last_hp then
        c.target_last_hp = hp
    elseif last_hp <= 0 and hp > 0 then
        c.target_last_hp = hp
    end

    local started = tonumber(c.target_started_at) or 0
    if started <= 0 then
        combat_begin_target_progress(target, obj)
        return nil
    end

    local last_damage = tonumber(c.target_last_damage_at) or started
    local timeout = math.max(1, tonumber(cfg.combat.target_no_damage_seconds) or 6)
    local elapsed = now - last_damage
    if elapsed >= timeout and hp > 0 then
        return "no-damage"
    end

    return nil
end

function combat_send_loot_key(loot_target, obj, reason)
    obj = tonumber(obj) or combat_entity_obj(loot_target)
    if obj <= 0 then
        return false, "invalid loot obj"
    end

    if ok_combat and combat then
        pcall(function()
            combat.selectTarget(obj)
        end)
    end

    local ok_remote, remote_runtime = pcall(require, "aion.remote")
    if ok_remote and remote_runtime and type(remote_runtime.pressKey) == "function" then
        local keycode = tonumber(cfg.combat.loot_keycode) or 67
        local ok, _, err = remote_runtime.pressKey(keycode)
        if ok then
            combat_set_status("loot-key", "key=" .. tostring(keycode), false)
            combat_log("loot-key:" .. tostring(obj),
                "loot key sent key=" .. tostring(keycode) .. " reason=" .. tostring(reason or "") .. " name=" .. tostring(loot_target and loot_target.name or ""),
                0,
                true)
            return true, nil
        end
        combat_set_status("loot-key-failed", tostring(err), true)
        combat_log("loot-key-failed:" .. tostring(obj), "loot key failed err=" .. tostring(err), 0, true)
        return false, err
    end

    combat_set_status("loot-error", "aion.remote unavailable", true)
    combat_log("loot-error", "loot remote unavailable", 0, true)
    return false, "aion.remote unavailable"
end

function combat_open_loot(loot_target, char)
    if not cfg.combat.loot_enabled then
        return false
    end
    if not loot_target then
        return false
    end

    local c = runtime.combat
    local obj = combat_entity_obj(loot_target)
    local loot_key = combat_loot_key(loot_target)
    if loot_key <= 0 then
        return false
    end

    combat_auto_off("loot", false)

    local dist = ok_core and core and core.distance3(char, loot_target) or tonumber(loot_target.distance) or 9999
    local previous_loot_obj = tonumber(c.loot_obj) or 0
    c.loot_obj = loot_key
    c.loot_name = tostring(loot_target.name or "")
    c.loot_distance = dist
    combat_log("loot-target:" .. tostring(loot_key),
        string.format("loot target name=%s obj=%s dist=%.1f attempts=%d",
            c.loot_name,
            tostring(loot_key),
            tonumber(dist) or 0,
            tonumber(c.loot_attempts) or 0),
        1.0,
        false)

    local interact_range = math.max(0.5, tonumber(cfg.combat.loot_interact_range) or 4)

    local ok_loot_module, loot_runtime = pcall(require, "aion.loot")
    if ok_loot_module and loot_runtime and type(loot_runtime.pickupTarget) == "function" then
        if dist <= interact_range then
            local now = now_seconds()
            local retry = math.max(0.2, tonumber(cfg.combat.loot_retry_interval) or 1.0)
            if c.last_loot_interact_at > 0 and now - c.last_loot_interact_at < retry then
                local picked_ok, picked_result = loot_runtime.waitPickupDialog({
                    timeoutMs = 250,
                    intervalMs = 50,
                    sleep = sleep,
                    now_ms = function()
                        return math.floor(now_seconds() * 1000)
                    end,
                    log = function(event, message)
                        combat_log("loot-flow:" .. tostring(event) .. ":" .. tostring(loot_key), tostring(message or ""), 1.0, false)
                    end,
                })
                if picked_ok then
                    local source = tostring(picked_result and picked_result.source or "pickup-dialog")
                    local detail = "module wait obj=" .. tostring(loot_key)
                    local verified, verify_err = combat_confirm_loot_cleared(source, detail)
                    if verified then
                        combat_finish_loot(source, detail)
                        return true
                    end
                    return false, verify_err or "loot target still lootable after pickup dialog"
                end
                combat_extend_post_kill("loot-wait-dialog", 2.0)
                combat_set_status("loot-wait-dialog", c.loot_name, false)
                combat_log("loot-wait:" .. tostring(loot_key), "loot wait dialog name=" .. c.loot_name, 1.0, false)
                return true
            end

            if previous_loot_obj ~= loot_key then
                c.loot_attempts = 0
            end
            c.loot_attempts = (tonumber(c.loot_attempts) or 0) + 1
            c.last_loot_interact_at = now

            local max_attempts = combat_loot_max_attempts()
            if c.loot_attempts > max_attempts then
                local giveup_name = c.loot_name
                combat_ignore_loot(loot_key)
                c.loot_obj = 0
                c.loot_name = ""
                c.loot_distance = 0
                c.loot_attempts = 0
                combat_clear_last_kill("loot-give-up", false)
                combat_set_status("loot-give-up", giveup_name, true)
                combat_log("loot-give-up:" .. tostring(loot_key),
                    "loot give up name=" .. tostring(giveup_name) ..
                    " after attempts=" .. tostring(max_attempts) .. " obj=" .. tostring(loot_key),
                    0,
                    true)
                return false
            end
        end

        local pick_ok, pick_result, pick_err = loot_runtime.pickupTarget(loot_target, {
            char = char,
            interactRange = interact_range,
            keycode = tonumber(cfg.combat.loot_keycode) or 67,
            waitTimeoutMs = 1200,
            intervalMs = 100,
            moveSettleMs = 250,
            sleep = sleep,
            now_ms = function()
                return math.floor(now_seconds() * 1000)
            end,
            log = function(event, message)
                combat_log("loot-flow:" .. tostring(event) .. ":" .. tostring(loot_key), tostring(message or ""), 1.0, false)
            end,
        })

        local status = tostring(pick_result and pick_result.status or "")
        if pick_ok and status == "picked" then
            local source = tostring(pick_result.source or "pickup")
            local detail = "module obj=" .. tostring(loot_key)
            local verified, verify_err = combat_confirm_loot_cleared(source, detail)
            if verified then
                combat_finish_loot(source, detail)
                return true
            end
            return false, verify_err or "loot target still lootable after pickup"
        end
        if pick_ok and status == "moving" then
            c.loot_distance = tonumber(pick_result.distance) or dist
            combat_extend_post_kill("loot-moving", 2.0)
            combat_set_status("loot-moving", string.format("%.1f", tonumber(c.loot_distance) or 0), false)
            combat_log("loot-moving:" .. tostring(loot_key),
                string.format("loot moving name=%s obj=%s dist=%.1f range=%.1f",
                    c.loot_name,
                    tostring(loot_key),
                    tonumber(c.loot_distance) or 0,
                    tonumber(interact_range) or 0),
                1.0,
                false)
            return true
        end
        if pick_ok and status == "wait_dialog" then
            combat_extend_post_kill("loot-wait-dialog", 2.0)
            combat_set_status("loot-wait-dialog", c.loot_name, false)
            combat_log("loot-wait:" .. tostring(loot_key),
                "loot opened but dialog not picked err=" .. tostring(pick_err or (pick_result and pick_result.error)),
                1.0,
                false)
            return true
        end
        if not pick_ok then
            local err_text = tostring(pick_err or (pick_result and pick_result.error) or "loot module failed")
            combat_set_status("loot-error", err_text, true)
            combat_log("loot-module-failed:" .. tostring(loot_key),
                "loot module failed err=" .. err_text,
                0,
                true)
            return false, err_text
        end
    end

    if dist > interact_range then
        if ok_nav and nav then
            move_trace("loot", loot_target,
                "reason=loot-move name=" .. tostring(loot_target.name or "") ..
                " dist=" .. tostring(dist) ..
                " range=" .. tostring(interact_range),
                0.5)
            local move_ok, _, move_err = nav.moveTo(loot_target.x or 0, loot_target.y or 0, loot_target.z or 0)
            move_trace("loot-result", loot_target,
                "ok=" .. tostring(move_ok) ..
                " err=" .. tostring(move_err or "") ..
                " name=" .. tostring(loot_target.name or ""),
                0.5)
            if not move_ok then
                combat_set_status("loot-move-failed", tostring(move_err), true)
                combat_log("loot-move-failed:" .. tostring(loot_key), "loot move failed err=" .. tostring(move_err), 0, true)
                return false
            end
            local moved_dist = dist
            if ok_core and core and type(core.getCharacter) == "function" then
                local char_ok, moved_char = core.getCharacter()
                if char_ok and moved_char then
                    char = moved_char
                    moved_dist = core.distance3(char, loot_target)
                    c.loot_distance = moved_dist
                end
            end
            if moved_dist <= interact_range then
                dist = moved_dist
                combat_log("loot-arrived:" .. tostring(loot_key),
                    string.format("loot arrived name=%s obj=%s dist=%.1f range=%.1f, continue open",
                        c.loot_name,
                        tostring(loot_key),
                        tonumber(dist) or 0,
                        tonumber(interact_range) or 0),
                    0,
                    true)
            else
                dist = moved_dist
                c.loot_distance = dist
                combat_extend_post_kill("loot-moving", 2.0)
                combat_set_status("loot-moving", string.format("%.1f", dist), false)
                combat_log("loot-moving:" .. tostring(loot_key),
                    string.format("loot moving name=%s obj=%s dist=%.1f range=%.1f",
                        c.loot_name,
                        tostring(loot_key),
                        tonumber(dist) or 0,
                        tonumber(interact_range) or 0),
                    1.0,
                    false)
                return true
            end
        end
        combat_set_status("loot-error", "aion.nav unavailable", true)
        combat_log("loot-error", "loot nav unavailable", 0, true)
        return false
    end

    local now = now_seconds()
    local retry = math.max(0.2, tonumber(cfg.combat.loot_retry_interval) or 1.0)
    if c.last_loot_interact_at > 0 and now - c.last_loot_interact_at < retry then
        if combat_wait_pickup_dialog(250, 50) then
            return true
        end
        combat_extend_post_kill("loot-wait-dialog", 2.0)
        combat_set_status("loot-wait-dialog", c.loot_name, false)
        combat_log("loot-wait:" .. tostring(loot_key), "loot wait dialog name=" .. c.loot_name, 1.0, false)
        return true
    end

    if previous_loot_obj ~= loot_key then
        c.loot_attempts = 0
    end
    c.loot_attempts = (tonumber(c.loot_attempts) or 0) + 1
    c.last_loot_interact_at = now

    local max_attempts = combat_loot_max_attempts()
    if c.loot_attempts > max_attempts then
        local giveup_name = c.loot_name
        combat_ignore_loot(loot_key)
        c.loot_obj = 0
        c.loot_name = ""
        c.loot_distance = 0
        c.loot_attempts = 0
        combat_clear_last_kill("loot-give-up", false)
        combat_set_status("loot-give-up", giveup_name, true)
        combat_log("loot-give-up:" .. tostring(loot_key),
            "loot give up name=" .. tostring(giveup_name) ..
            " after attempts=" .. tostring(max_attempts) .. " obj=" .. tostring(loot_key),
            0,
            true)
        return false
    end

    combat_close_loot_dialog("before-open:" .. tostring(loot_key))
    sleep(80)

    local interact_id = tonumber(loot_target.interact_id) or 0
    if interact_id > 0 then
        local ok_npc, npc_runtime = pcall(require, "aion.npc")
        if ok_npc and npc_runtime and type(npc_runtime.interactId) == "function" then
            local ok, _, err = npc_runtime.interactId(interact_id)
            if ok then
                combat_set_status("loot-open", "interact_id=" .. tostring(interact_id), false)
                combat_log("loot-open:" .. tostring(loot_key),
                    "loot open by interact_id=" .. tostring(interact_id) .. " name=" .. c.loot_name,
                    0,
                    true)
                local picked = combat_wait_pickup_dialog(1200, 100)
                if picked then
                    return true
                end
                local key_ok, key_err = combat_send_loot_key(loot_target, obj, "after-interact-no-dialog")
                if key_ok and combat_wait_pickup_dialog(1000, 100) then
                    return true
                end
                combat_log("loot-open-no-dialog:" .. tostring(loot_key),
                    "loot interact sent but no pickup dialog clicked name=" .. c.loot_name ..
                    " key_ok=" .. tostring(key_ok) ..
                    " key_err=" .. tostring(key_err),
                    0,
                    true)
                return false, key_err or "loot dialog not clicked after interact"
            end
            c.last_error = "loot interact failed: " .. tostring(err)
            combat_log("loot-interact-failed:" .. tostring(loot_key), "loot interact failed err=" .. tostring(err), 0, true)
        end
    end

    local key_ok, key_err = combat_send_loot_key(loot_target, obj, "fallback")
    if key_ok and combat_wait_pickup_dialog(1200, 100) then
        return true
    end
    return false, key_err or "loot dialog not clicked after key"
end

function loot_test_set_status(text)
    runtime.loot_test = runtime.loot_test or {}
    runtime.loot_test.last_status = tostring(text or "")
    set_event("[拾取测试] " .. runtime.loot_test.last_status)
    log_info("[AionLootTest] " .. runtime.loot_test.last_status)
end

function loot_test_dump_near_corpses()
    runtime.loot_test = runtime.loot_test or {}
    runtime.loot_test.last_dump = ""

    if not ok_core or not core then
        loot_test_set_status("F5 failed: aion.core unavailable")
        return false
    end
    if not ok_entity or not entity then
        loot_test_set_status("F5 failed: aion.entity unavailable")
        return false
    end

    local pid = tonumber(cfg.target and cfg.target.pid) or nil
    if core.ensureInit then
        local init_ok, init_err = core.ensureInit(pid)
        if not init_ok then
            loot_test_set_status("F5 failed: init " .. tostring(init_err))
            return false
        end
    end

    local char_ok, char, char_err = core.getCharacter()
    if not char_ok or not char then
        loot_test_set_status("F5 failed: read character " .. tostring(char_err))
        return false
    end

    local list_ok, list, list_err = entity.list()
    if not list_ok then
        loot_test_set_status("F5 failed: read entities " .. tostring(list_err))
        return false
    end
    list = list or {}

    local corpse_rows = {}
    local type2_rows = {}
    local type2_count = 0
    local lootable_count = 0

    for _, e in ipairs(list) do
        local type_val = tonumber(e and e.type) or 0
        local hp = tonumber(e and e.hp) or 0
        local mhp = tonumber(e and e.mhp) or 0
        local lootable = tonumber(e and e.lootable) or 0
        local dead = e and e.dead == true
        local dist = ok_core and core and core.distance3(char, e) or tonumber(e and e.distance) or 9999
        local obj = combat_entity_obj(e)
        local key = combat_loot_key(e)
        local interact_id = tonumber(e and e.interact_id) or 0
        local name = tostring(e and e.name or "")
        local reason = combat_loot_reject_reason(e, char) or "loot-ok"

        if type_val == 2 then
            type2_count = type2_count + 1
            type2_rows[#type2_rows + 1] = {
                dist = dist,
                obj = obj,
                key = key,
                interact_id = interact_id,
                name = name,
                hp = hp,
                mhp = mhp,
                lootable = lootable,
                dead = dead,
                reason = reason,
            }
        end
        if lootable ~= 0 then
            lootable_count = lootable_count + 1
        end
        if type_val == 2 and (lootable ~= 0 or dead or (mhp > 0 and hp <= 0)) then
            corpse_rows[#corpse_rows + 1] = {
                dist = dist,
                obj = obj,
                key = key,
                interact_id = interact_id,
                name = name,
                hp = hp,
                mhp = mhp,
                lootable = lootable,
                dead = dead,
                reason = reason,
            }
        end
    end

    local function by_dist(a, b)
        return (tonumber(a.dist) or 9999) < (tonumber(b.dist) or 9999)
    end
    table.sort(corpse_rows, by_dist)
    table.sort(type2_rows, by_dist)

    local lines = {}
    lines[#lines + 1] = string.format(
        "F5 nearest corpse dump: entities=%d type2=%d corpse=%d lootable=%d char=%.2f, %.2f, %.2f",
        #(list or {}),
        tonumber(type2_count) or 0,
        #corpse_rows,
        tonumber(lootable_count) or 0,
        tonumber(char.x) or 0,
        tonumber(char.y) or 0,
        tonumber(char.z) or 0)

    log_info(string.format("[AionLootF5] begin entities=%d type2=%d corpse=%d lootable=%d char=%.2f,%.2f,%.2f",
        #(list or {}),
        tonumber(type2_count) or 0,
        #corpse_rows,
        tonumber(lootable_count) or 0,
        tonumber(char.x) or 0,
        tonumber(char.y) or 0,
        tonumber(char.z) or 0))

    local rows = corpse_rows
    local label = "corpse"
    if #rows <= 0 then
        rows = type2_rows
        label = "type2"
        lines[#lines + 1] = "No corpse candidates; showing nearest type=2 samples."
    end

    local limit = math.min(#rows, 15)
    for index = 1, limit do
        local row = rows[index]
        local text = string.format(
            "%02d. %s dist=%.1f obj=%s key=%s iid=%s hp=%s/%s lootable=%s dead=%s reason=%s name=%s",
            index,
            label,
            tonumber(row.dist) or 0,
            tostring(row.obj or 0),
            tostring(row.key or 0),
            tostring(row.interact_id or 0),
            tostring(row.hp or 0),
            tostring(row.mhp or 0),
            tostring(row.lootable or 0),
            tostring(row.dead == true),
            tostring(row.reason or ""),
            tostring(row.name or ""))
        lines[#lines + 1] = text
        log_info("[AionLootF5] " .. text)
    end
    if limit <= 0 then
        lines[#lines + 1] = "No type=2 entities found nearby."
        log_info("[AionLootF5] no type=2 entities")
    end
    log_info("[AionLootF5] end")

    runtime.loot_test.last_dump = table.concat(lines, "\n")
    loot_test_set_status(string.format("F5 尸体遍历完成: corpse=%d lootable=%d", #corpse_rows, tonumber(lootable_count) or 0))
    return true
end

function target_f6_set_status(text)
    runtime.target_dump = runtime.target_dump or {}
    runtime.target_dump.last_status = tostring(text or "")
    set_event("[F6 target] " .. runtime.target_dump.last_status)
    log_info("[AionTargetF6] " .. runtime.target_dump.last_status)
end

function target_f6_dump_selected()
    runtime.target_dump = runtime.target_dump or {}
    runtime.target_dump.last_dump = ""

    if not ok_core or not core then
        target_f6_set_status("failed: aion.core unavailable")
        return false
    end
    if not ok_target_dump or not target_dump or type(target_dump.read) ~= "function" then
        target_f6_set_status("failed: aion.target_dump unavailable " .. tostring(target_dump))
        return false
    end

    target_refresh(true)
    local pid = tonumber(cfg.target and cfg.target.pid) or nil
    if (not pid or pid <= 0) and type(core.resolvePid) == "function" then
        pid = tonumber(core.resolvePid()) or nil
    end
    if core.ensureInit then
        local init_ok, init_err = core.ensureInit(pid)
        if not init_ok then
            target_f6_set_status("failed: init " .. tostring(init_err))
            return false
        end
    end

    local ok, result, err = target_dump.read({
        core = core,
        combat = ok_combat and combat or nil,
        entity = ok_entity and entity or nil,
    })
    if not ok then
        target_f6_set_status("failed: " .. tostring(err))
        return false
    end

    result = type(result) == "table" and result or {}
    local target_lines = type(result.lines) == "table" and result.lines or {}
    if #target_lines <= 0 then
        target_lines = { tostring(result.summary or result.status or "empty target result") }
    end

    local lines = {}
    for _, line in ipairs(target_lines) do
        lines[#lines + 1] = tostring(line or "")
    end

    if ok_quest_snapshot and quest_snapshot and type(quest_snapshot.read) == "function" then
        local snap_ok, snap_result, snap_err = quest_snapshot.read({
            core = core,
            quest = ok_quest and quest or nil,
            map = ok_map and map or nil,
        })
        if snap_ok then
            snap_result = type(snap_result) == "table" and snap_result or {}
            lines[#lines + 1] = ""
            lines[#lines + 1] = "snapshot status=" .. tostring(snap_result.status or "") ..
                " summary=" .. tostring(snap_result.summary or "")
            local snap_lines = type(snap_result.lines) == "table" and snap_result.lines or {}
            for _, line in ipairs(snap_lines) do
                lines[#lines + 1] = tostring(line or "")
            end
        else
            lines[#lines + 1] = "snapshot failed: " .. tostring(snap_err)
        end
    else
        lines[#lines + 1] = "snapshot failed: aion.quest_snapshot unavailable " .. tostring(quest_snapshot)
    end

    log_info("[AionTargetF6] begin status=" .. tostring(result.status or "") .. " summary=" .. tostring(result.summary or ""))
    for _, line in ipairs(lines) do
        log_info("[AionTargetF6] " .. tostring(line or ""))
    end
    log_info("[AionTargetF6] end")

    runtime.target_dump.last_dump = table.concat(lines, "\n")
    target_f6_set_status(tostring(result.summary or result.status or "done"))
    return true
end

function task_f11_set_status(text)
    runtime.task_record = runtime.task_record or {}
    runtime.task_record.last_status = tostring(text or "")
    set_event("[F11 task] " .. runtime.task_record.last_status)
    log_info("[AionTaskF11] " .. runtime.task_record.last_status)
end

function task_f11_failed_result(kind, err)
    return {
        status = "failed",
        summary = tostring(err or ""),
        lines = { "error kind=" .. tostring(kind or "") .. " message=" .. tostring(err or "") },
    }
end

function task_f11_read_snapshot()
    if not ok_quest_snapshot or not quest_snapshot or type(quest_snapshot.read) ~= "function" then
        return task_f11_failed_result("snapshot", "aion.quest_snapshot unavailable " .. tostring(quest_snapshot))
    end

    local call_ok, read_ok, result, err = pcall(quest_snapshot.read, {
        core = core,
        quest = ok_quest and quest or nil,
        map = ok_map and map or nil,
    })
    if not call_ok then
        return task_f11_failed_result("snapshot", read_ok)
    end
    if not read_ok then
        return task_f11_failed_result("snapshot", err or result)
    end
    return type(result) == "table" and result or {}
end

function task_f11_read_target()
    if not ok_target_dump or not target_dump or type(target_dump.read) ~= "function" then
        return task_f11_failed_result("target", "aion.target_dump unavailable " .. tostring(target_dump))
    end

    local call_ok, read_ok, result, err = pcall(target_dump.read, {
        core = core,
        combat = ok_combat and combat or nil,
        entity = ok_entity and entity or nil,
    })
    if not call_ok then
        return task_f11_failed_result("target", read_ok)
    end
    if not read_ok then
        return task_f11_failed_result("target", err or result)
    end
    return type(result) == "table" and result or {}
end

function task_f11_read_dialog()
    local ok_npc_runtime, npc_runtime = pcall(require, "aion.npc")
    if not ok_npc_runtime or not npc_runtime or type(npc_runtime.dialog) ~= "function" then
        return nil, {}, "aion.npc dialog unavailable " .. tostring(npc_runtime)
    end

    local ok_ui_runtime, ui_runtime = pcall(require, "aion.ui")
    if not ok_ui_runtime or not ui_runtime or type(ui_runtime.children) ~= "function" then
        return nil, {}, "aion.ui children unavailable " .. tostring(ui_runtime)
    end

    local dialog_ok, info_or_err, dialog_err = npc_runtime.dialog()
    if not dialog_ok then
        return nil, {}, tostring(dialog_err or info_or_err)
    end
    if not info_or_err then
        return nil, {}, "no open NPC dialog"
    end

    cfg.npc_dialog = cfg.npc_dialog or {}
    local depth = math.max(1, tonumber(cfg.npc_dialog.dialog_child_depth) or 6)
    local child_ok, children, child_err = ui_runtime.children("dlg_dialog", depth)
    if not child_ok then
        return info_or_err, {}, "dialog children failed: " .. tostring(child_err)
    end

    local capped = {}
    for _, child in ipairs(children or {}) do
        if child and (tonumber(child.obj or child.addr) or 0) ~= 0 then
            capped[#capped + 1] = child
            if #capped >= 120 then
                break
            end
        end
    end
    return info_or_err, capped, ""
end

function task_f11_record_snapshot()
    runtime.task_record = runtime.task_record or {}
    runtime.task_record.last_dump = ""

    if not ok_task_recorder or not task_recorder or type(task_recorder.build) ~= "function" then
        task_f11_set_status("failed: aion.task_recorder unavailable " .. tostring(task_recorder))
        return false
    end
    if not ok_core or not core then
        task_f11_set_status("failed: aion.core unavailable")
        return false
    end

    target_refresh(true)
    local pid = tonumber(cfg.target and cfg.target.pid) or nil
    if (not pid or pid <= 0) and type(core.resolvePid) == "function" then
        pid = tonumber(core.resolvePid()) or nil
    end
    if core.ensureInit then
        local init_ok, init_err = core.ensureInit(pid)
        if not init_ok then
            task_f11_set_status("failed: init " .. tostring(init_err))
            return false
        end
    end

    local snapshot = task_f11_read_snapshot()
    local target = task_f11_read_target()
    local dialog, dialog_children, dialog_err = task_f11_read_dialog()
    cfg.npc_dialog = cfg.npc_dialog or {}
    local result = task_recorder.build({
        snapshot = snapshot,
        target = target,
        dialog = dialog,
        dialog_error = dialog_err,
        dialog_children = dialog_children,
        opts = {
            dialog_click_x = tonumber(cfg.npc_dialog.auto_click_x) or 25,
            dialog_click_x_tolerance = math.max(0, tonumber(cfg.npc_dialog.auto_click_x_tolerance) or 2),
            dialog_child_limit = 40,
        },
    })
    result = type(result) == "table" and result or {}
    local lines = type(result.lines) == "table" and result.lines or {}
    if #lines <= 0 then
        lines = { tostring(result.summary or result.status or "empty task record") }
    end

    log_info("[AionTaskF11] begin status=" .. tostring(result.status or "") .. " summary=" .. tostring(result.summary or ""))
    for _, line in ipairs(lines) do
        log_info("[AionTaskF11] " .. tostring(line or ""))
    end
    log_info("[AionTaskF11] end")

    runtime.task_record.last_dump = table.concat(lines, "\n")
    task_f11_set_status(tostring(result.summary or result.status or "done"))
    return true
end

function loot_test_pick_nearest()
    runtime.loot_test = runtime.loot_test or {}
    runtime.loot_test.last_dump = ""

    if not ok_core or not core then
        loot_test_set_status("失败: aion.core 不可用")
        return false
    end
    if not ok_entity or not entity then
        loot_test_set_status("失败: aion.entity 不可用")
        return false
    end

    local pid = tonumber(cfg.target and cfg.target.pid) or nil
    if core.ensureInit then
        local init_ok, init_err = core.ensureInit(pid)
        if not init_ok then
            loot_test_set_status("失败: 初始化失败" .. tostring(init_err))
            return false
        end
    end

    local char_ok, char, char_err = core.getCharacter()
    if not char_ok or not char then
        loot_test_set_status("失败: 读取角色失败 " .. tostring(char_err))
        return false
    end

    local list_ok, list, list_err = entity.list()
    if not list_ok then
        loot_test_set_status("失败: 读取实体失败 " .. tostring(list_err))
        return false
    end

    local old_enabled = cfg.combat.loot_enabled
    cfg.combat.loot_enabled = true
    local target = combat_choose_loot(list or {}, char, char)
    if not target then
        cfg.combat.loot_enabled = old_enabled
        runtime.loot_test.last_dump = "未找到可拾取尸体。请确认尸体在拾取半径内，且 API 鐨?lootable=1銆?"
        loot_test_set_status("未找到可拾取尸体 entities=" .. tostring(#(list or {})))
        return false
    end

    local dist = ok_core and core and core.distance3(char, target) or tonumber(target.distance) or 0
    runtime.loot_test.last_dump = string.format(
        "目标: %s\nobj: %s\n距离: %.1f\nlootable: %s\ninteract_id: %s\n坐标: %.2f, %.2f, %.2f",
        tostring(target.name or ""),
        tostring(combat_loot_key(target)),
        tonumber(dist) or 0,
        tostring(target.lootable or ""),
        tostring(target.interact_id or ""),
        tonumber(target.x) or 0,
        tonumber(target.y) or 0,
        tonumber(target.z) or 0)

    local call_ok, ok_or_err = pcall(combat_open_loot, target, char)
    cfg.combat.loot_enabled = old_enabled
    if not call_ok then
        loot_test_set_status("拾取步骤异常: " .. tostring(ok_or_err))
        return false
    end

    if ok_or_err then
        loot_test_set_status(string.format("已执行拾取步骤 %s dist=%.1f", tostring(target.name or ""), tonumber(dist) or 0))
        return true
    end

    loot_test_set_status(string.format("拾取步骤失败: %s dist=%.1f", tostring(target.name or ""), tonumber(dist) or 0))
    return false
end

function loot_test_pick_nearest()
    runtime.loot_test = runtime.loot_test or {}
    runtime.loot_test.last_dump = ""

    if not ok_core or not core then
        loot_test_set_status("failed: aion.core unavailable")
        return false
    end
    if not ok_entity or not entity then
        loot_test_set_status("failed: aion.entity unavailable")
        return false
    end

    local ok_loot_module, loot_runtime = pcall(require, "aion.loot")
    if not ok_loot_module or not loot_runtime or type(loot_runtime.findNearestLootable) ~= "function" or type(loot_runtime.pickupTargetByKey) ~= "function" then
        loot_test_set_status("failed: aion.loot key-pickup unavailable")
        runtime.loot_test.last_dump = tostring(loot_runtime)
        return false
    end

    local pid = tonumber(cfg.target and cfg.target.pid) or nil
    if core.ensureInit then
        local init_ok, init_err = core.ensureInit(pid)
        if not init_ok then
            loot_test_set_status("failed: init " .. tostring(init_err))
            return false
        end
    end

    local started = now_seconds()
    local timeout = 12.0
    local last_target_name = ""
    local last_dist = 0
    local last_entities = 0
    local last_err = nil
    local picked_once = false

    local function loot_test_key(target)
        if loot_runtime and type(loot_runtime.lootKey) == "function" then
            return tonumber(loot_runtime.lootKey(target)) or 0
        end
        return tonumber(combat_loot_key(target)) or 0
    end

    local function set_target_dump(target, dist, status, meta)
        runtime.loot_test.last_dump = string.format(
            "target: %s\nobj: %s\ndist: %.1f\nlootable: %s\ninteract_id: %s\npos: %.2f, %.2f, %.2f\nstatus: %s\nchecked: %s accepted: %s",
            tostring(target and target.name or ""),
            tostring(loot_test_key(target)),
            tonumber(dist) or 0,
            tostring(target and target.lootable or ""),
            tostring(target and target.interact_id or ""),
            tonumber(target and target.x) or 0,
            tonumber(target and target.y) or 0,
            tonumber(target and target.z) or 0,
            tostring(status or ""),
            tostring(meta and meta.checked or ""),
            tostring(meta and meta.accepted or ""))
    end

    local function same_target_still_lootable(list, key)
        key = tonumber(key) or 0
        if key <= 0 then
            return false
        end
        for _, item in ipairs(list or {}) do
            if loot_test_key(item) == key then
                return (tonumber(item and item.lootable) or 0) ~= 0
            end
        end
        return false
    end

    while now_seconds() - started <= timeout do
        local char_ok, char, char_err = core.getCharacter()
        if not char_ok or not char then
            loot_test_set_status("failed: read character " .. tostring(char_err))
            return false
        end

        local list_ok, list, list_err = entity.list()
        if not list_ok then
            loot_test_set_status("failed: read entities " .. tostring(list_err))
            return false
        end
        last_entities = #(list or {})

        local find_ok, target, find_err, meta = loot_runtime.findNearestLootable({
            char = char,
            list = list or {},
            radius = tonumber(cfg.combat.loot_radius) or tonumber(cfg.combat.radius) or 35,
            monsterOnly = true,
        })
        if not find_ok then
            loot_test_set_status("failed: find lootable " .. tostring(find_err))
            return false
        end

        if not target then
            runtime.loot_test.last_dump = string.format(
                "no lootable corpse found\nentities: %d\nchecked: %s accepted: %s\nnote: manual test scans type=2 and lootable=1 without combat ignore list",
                tonumber(last_entities) or 0,
                tostring(meta and meta.checked or ""),
                tostring(meta and meta.accepted or ""))
            if picked_once then
                loot_test_set_status("picked by C: no remaining loot target")
                return true
            end
            last_err = "no loot target"
            sleep(250)
        else
            local dist = ok_core and core and core.distance3(char, target) or tonumber(target.distance) or 0
            last_target_name = tostring(target.name or "")
            last_dist = tonumber(dist) or 0
            local target_key = loot_test_key(target)
            set_target_dump(target, dist, "target-found", meta)

            local call_ok, pick_ok, pick_result, pick_err = pcall(loot_runtime.pickupTargetByKey, target, {
                char = char,
                interactRange = math.max(0.5, tonumber(cfg.combat.loot_interact_range) or 4),
                keycode = tonumber(cfg.combat.loot_keycode) or 67,
                waitTimeoutMs = 1600,
                intervalMs = 100,
                moveSettleMs = 350,
                closeDelayMs = 80,
                sleep = sleep,
                now_ms = function()
                    return math.floor(now_seconds() * 1000)
                end,
                log = function(event, message)
                    log_info("[AionLootTestFlow] " .. tostring(event) .. " " .. tostring(message or ""))
                end,
            })
            if not call_ok then
                loot_test_set_status("loot exception: " .. tostring(pick_ok))
                return false
            end

            local status = tostring(pick_result and pick_result.status or "")
            last_dist = tonumber(pick_result and pick_result.distance) or last_dist
            set_target_dump(target, last_dist, status, meta)
            if pick_ok and status == "moving" then
                loot_test_set_status(string.format("moving to corpse: %s dist=%.1f", last_target_name, last_dist))
                sleep(350)
            elseif pick_ok and status == "picked" then
                picked_once = true
                sleep(300)
                local verify_ok, verify_list, verify_err = entity.list()
                if verify_ok and not same_target_still_lootable(verify_list or {}, target_key) then
                    loot_test_set_status(string.format("picked by C: %s dist=%.1f", last_target_name, last_dist))
                    return true
                end
                last_err = verify_ok and "target still lootable after pickup" or ("verify failed " .. tostring(verify_err))
                loot_test_set_status(string.format("picked dialog, verifying corpse: %s dist=%.1f err=%s",
                    last_target_name,
                    last_dist,
                    tostring(last_err)))
                sleep(300)
            else
                last_err = pick_err or (pick_result and pick_result.error) or "key pickup failed"
                loot_test_set_status(string.format("key pickup failed: %s dist=%.1f err=%s",
                    last_target_name,
                    last_dist,
                    tostring(last_err)))
                sleep(250)
            end
        end
    end

    loot_test_set_status(string.format("timeout: target=%s dist=%.1f entities=%d err=%s",
        tostring(last_target_name),
        tonumber(last_dist) or 0,
        tonumber(last_entities) or 0,
        tostring(last_err or "")))
    return false
end

function combat_pick_loot_with_test_button(loot_target, char, reason)
    if not cfg.combat or cfg.combat.loot_enabled ~= true then
        return false, "loot disabled"
    end
    if type(loot_test_pick_nearest) ~= "function" then
        return false, "loot test button method unavailable"
    end

    local c = runtime.combat
    local loot_key = combat_loot_key(loot_target)
    if loot_key <= 0 then
        return false, "invalid loot key"
    end

    local dist = ok_core and core and char and core.distance3(char, loot_target) or tonumber(loot_target and loot_target.distance) or 0
    c.loot_obj = loot_key
    c.loot_name = tostring(loot_target and loot_target.name or "")
    c.loot_distance = dist
    c.last_loot_interact_at = now_seconds()

    combat_auto_off("loot-button:" .. tostring(reason or ""), false)
    combat_set_status("loot-button", c.loot_name, false)
    combat_log("loot-button:" .. tostring(loot_key),
        string.format("call loot_test_pick_nearest reason=%s name=%s obj=%s dist=%.1f interact_id=%s lootable=%s",
            tostring(reason or ""),
            c.loot_name,
            tostring(loot_key),
            tonumber(dist) or 0,
            tostring(loot_target and loot_target.interact_id or 0),
            tostring(loot_target and loot_target.lootable or "")),
        0,
        true)

    local call_ok, picked = pcall(loot_test_pick_nearest)
    sleep(100)
    if not call_ok then
        local err = tostring(picked)
        combat_set_status("loot-button-error", err, true)
        combat_log("loot-button-error:" .. tostring(loot_key),
            "loot_test_pick_nearest exception err=" .. err,
            0,
            true)
        combat_clear_pending_loot("loot-button-exception", false)
        combat_clear_last_kill("loot-button-exception", false)
        return false, err
    end

    if picked == true then
        combat_finish_loot("loot-test-button", "reason=" .. tostring(reason or "") .. " obj=" .. tostring(loot_key))
        return true, nil
    end

    local err = tostring(runtime.loot_test and runtime.loot_test.last_status or "loot test button failed")
    combat_set_status("loot-button-failed", err, true)
    combat_log("loot-button-failed:" .. tostring(loot_key),
        "loot_test_pick_nearest returned false status=" .. err,
        0,
        true)
    combat_clear_pending_loot("loot-button-failed", false)
    combat_clear_last_kill("loot-button-failed", false)
    return false, err
end

function combat_auto_on(force_start)
    if not ok_combat or not combat then
        return false, "aion.combat unavailable"
    end
    if not force_start and combat.isAutoBattleOn then
        local state_ok, is_on = combat.isAutoBattleOn()
        if state_ok and is_on == true then
            return true, nil, true
        end
    end
    local ok, value, err = combat.autoBattleOn()
    if not ok then
        return false, err
    end
    return value ~= false, nil, false
end

function combat_auto_off(reason, force_log)
    if type(combat_uses_attack_key) == "function" and combat_uses_attack_key() then
        combat_log("auto-off-skip:" .. tostring(reason or "manual"),
            "auto battle off skipped because attack trigger is key" .. ATTACK_KEY_LABEL,
            force_log and 0 or 1.0,
            force_log == true)
        return false, "key" .. ATTACK_KEY_LABEL .. "-trigger"
    end
    if ok_combat and combat and not cfg.combat.keep_auto_battle then
        local c = runtime.combat
        local ok, value, err = combat.autoBattleOff()
        if ok then
            c.last_auto_off_at = now_seconds()
            c.last_auto_on_at = 0
            combat_log("auto-off:" .. tostring(reason or "manual"),
                "auto battle off reason=" .. tostring(reason or "") .. " result=" .. tostring(value),
                force_log and 0 or 1.0,
                force_log == true)
        else
            combat_log("auto-off-failed:" .. tostring(reason or "manual"),
                "auto battle off failed reason=" .. tostring(reason or "") .. " err=" .. tostring(err),
                0,
                true)
        end
    end
end

function combat_begin_post_kill(reason, entity)
    local c = runtime.combat
    local obj = tonumber(c.target_obj) or 0
    if obj <= 0 then
        return false
    end
    local loot_enabled = cfg.combat and cfg.combat.loot_enabled == true
    local check_delay = loot_enabled and math.max(0.05, math.min(0.50, tonumber(cfg.combat.post_kill_check_delay_seconds) or 0.1)) or 0
    local now = now_seconds()
    local pending_loot = tonumber(c.loot_obj) or 0
    if pending_loot > 0 and pending_loot ~= obj then
        combat_close_loot_dialog("new-kill-clear-pending")
        combat_clear_pending_loot("new-kill", false)
    end
    c.last_killed_obj = obj
    c.last_killed_interact_id = tonumber(entity and entity.interact_id) or 0
    c.last_killed_name = tostring(c.target_name or (entity and entity.name) or "")
    c.post_kill_started_at = now
    c.post_kill_until = loot_enabled and (now + check_delay) or 0
    c.target_obj = 0
    c.target_name = ""
    c.target_distance = 0
    c.force_auto_until = 0
    c.last_force_auto_at = 0
    c.last_attack_key_at = 0
    c.last_attack_key_obj = 0
    combat_reset_target_progress()
    if not loot_enabled then
        c.loot_obj = 0
        c.loot_name = ""
        c.loot_distance = 0
        c.loot_attempts = 0
        combat_set_status("target-ended", c.last_killed_name, false)
        combat_log("post-kill-skip-loot:" .. tostring(obj),
            string.format("target ended reason=%s name=%s obj=%s loot_enabled=false",
                tostring(reason or ""),
                tostring(c.last_killed_name or ""),
                tostring(obj)),
            0,
            true)
        return true
    end
    combat_auto_off(reason or "target-ended", true)
    combat_set_status("post-kill-loot", c.last_killed_name, false)
    combat_log("post-kill:" .. tostring(obj),
        string.format("target ended reason=%s name=%s obj=%s interact_id=%s loot_check_delay=%.2fs",
            tostring(reason or ""),
            tostring(c.last_killed_name or ""),
            tostring(obj),
            tostring(c.last_killed_interact_id or 0),
            tonumber(check_delay) or 0),
        0,
        true)
    return true
end

function combat_target_end_reason(e)
    if type(e) ~= "table" then
        return "missing"
    end
    if e.dead == true then
        return "dead"
    end
    if (tonumber(e.lootable) or 0) ~= 0 then
        return "lootable"
    end
    local hp = tonumber(e.hp) or 0
    local mhp = tonumber(e.mhp) or 0
    if mhp > 0 and hp <= 0 then
        return "hp-zero"
    end
    return nil
end

function combat_abort_target(reason, entity)
    local c = runtime.combat
    local obj = tonumber(c.target_obj) or 0
    local name = tostring(c.target_name or (entity and entity.name) or "")
    c.target_obj = 0
    c.target_name = ""
    c.target_distance = 0
    c.post_kill_until = 0
    c.post_kill_started_at = 0
    c.force_auto_until = 0
    c.last_force_auto_at = 0
    c.last_attack_key_at = 0
    c.last_attack_key_obj = 0
    combat_reset_target_progress()
    c.loot_obj = 0
    c.loot_name = ""
    c.loot_attempts = 0
    combat_auto_off("target-abort:" .. tostring(reason or ""), false)
    combat_set_status("target-lost", tostring(reason or ""), false)
    combat_log("target-lost:" .. tostring(obj),
        string.format("target lost reason=%s name=%s obj=%s; no loot wait",
            tostring(reason or ""),
            name,
            tostring(obj)),
        0,
        true)
    return true
end

function combat_move_anchor(reason)
    local c = runtime.combat
    local anchor = c.anchor
    if not anchor or not ok_nav or not nav then
        return false
    end
    local now = now_seconds()
    local interval = tonumber(cfg.combat.move_resend_interval) or 2.0
    if c.last_move_at > 0 and now - c.last_move_at < interval then
        combat_set_status(reason or "returning", "", false)
        combat_log("move-throttle:" .. tostring(reason or "returning"),
            "move throttled reason=" .. tostring(reason or "returning"),
            1.0,
            false)
        return true
    end
    if tostring(reason or "") ~= "patrol-moving" then
        combat_auto_off(reason or "returning", false)
    end
    move_trace("combat-anchor", anchor,
        "reason=" .. tostring(reason or "returning") ..
        " anchor_dist=" .. tostring(c.anchor_distance or ""),
        0.5)
    local ok, _, err = nav.moveTo(anchor.x or 0, anchor.y or 0, anchor.z or 0)
    move_trace("combat-anchor-result", anchor,
        "reason=" .. tostring(reason or "returning") ..
        " ok=" .. tostring(ok) ..
        " err=" .. tostring(err or ""),
        0.5)
    c.last_move_at = now
    if not ok then
        combat_set_status("return-failed", tostring(err), true)
        combat_log("move-failed", "move anchor failed reason=" .. tostring(reason or "returning") .. " err=" .. tostring(err), 0, true)
        return false
    end
    combat_set_status(reason or "returning", "", true)
    combat_log("move-anchor",
        string.format("move anchor reason=%s anchor=%.2f,%.2f,%.2f dist=%.1f",
            tostring(reason or "returning"),
            tonumber(anchor.x) or 0,
            tonumber(anchor.y) or 0,
            tonumber(anchor.z) or 0,
            tonumber(c.anchor_distance) or 0),
        0,
        true)
    return true
end

function combat_stop_movement_for_target(reason)
    if not cfg.combat or cfg.combat.stop_move_on_target == false then
        return true
    end
    if not ok_nav or not nav or not ok_core or not core then
        return false, "nav/core unavailable"
    end

    local now = now_seconds()
    local recent_window = math.max(0.3, tonumber(cfg.combat.move_resend_interval) or 2.0) + 0.5
    local recent_combat_move = (tonumber(runtime.combat.last_move_at) or 0) > 0
        and now - (tonumber(runtime.combat.last_move_at) or 0) <= recent_window
    local recent_route_move = runtime.route and runtime.route.following == true
        and (tonumber(runtime.route.last_move_at) or 0) > 0
        and now - (tonumber(runtime.route.last_move_at) or 0) <= recent_window

    local moving_state = false
    local char_ok, char = core.getCharacter()
    if char_ok and char then
        local move_state = tonumber(char.move_state or char.moveState or char.moving or 0) or 0
        moving_state = move_state ~= 0
    end

    if not recent_combat_move and not recent_route_move and not moving_state then
        return true
    end

    local pos_ok, pos, pos_err = core.getPosition()
    if not pos_ok or not pos then
        return false, pos_err or "position unavailable"
    end

    local ok, _, err = nav.moveTo(pos.x or 0, pos.y or 0, pos.z or 0)
    move_trace("combat-stop-move", pos,
        "reason=" .. tostring(reason or "") ..
        " ok=" .. tostring(ok) ..
        " err=" .. tostring(err or "") ..
        " recent_combat=" .. tostring(recent_combat_move) ..
        " recent_route=" .. tostring(recent_route_move) ..
        " moving=" .. tostring(moving_state),
        0.5)
    if ok then
        runtime.combat.last_move_at = 0
        combat_log("stop-move-target",
            string.format("stop movement for target reason=%s pos=%.2f,%.2f,%.2f recent_combat=%s recent_route=%s moving=%s",
                tostring(reason or ""),
                tonumber(pos.x) or 0,
                tonumber(pos.y) or 0,
                tonumber(pos.z) or 0,
                tostring(recent_combat_move),
                tostring(recent_route_move),
                tostring(moving_state)),
            0,
            true)
        return true
    end
    combat_log("stop-move-target-failed",
        "stop movement failed reason=" .. tostring(reason or "") .. " err=" .. tostring(err),
        0,
        true)
    return false, err
end

function combat_attack_trigger_mode()
    normalize_combat_mode()
    return tonumber(cfg.combat and cfg.combat.attack_trigger_mode) or 1
end

function combat_uses_attack_key()
    return combat_attack_trigger_mode() == 1
end

function combat_send_attack_key(target, obj, reason)
    obj = tonumber(obj) or combat_entity_obj(target)
    if obj <= 0 then
        return false, "invalid target obj"
    end

    local c = runtime.combat
    local ok_remote, remote_runtime = pcall(require, "aion.remote")
    if not ok_remote or not remote_runtime or type(remote_runtime.pressKey) ~= "function" then
        return false, "aion.remote unavailable"
    end

    local keycode = tonumber(cfg.combat and cfg.combat.attack_keycode) or ATTACK_KEYCODE
    local key_started = now_seconds()
    local ok, _, err = remote_runtime.pressKey(keycode)
    local key_ms = math.max(0, (now_seconds() - key_started) * 1000)
    if ok then
        c.last_attack_key_at = now_seconds()
        c.last_attack_key_obj = obj
        combat_log("attack-key:" .. tostring(obj),
            string.format("attack key sent key=%s(%s) reason=%s name=%s obj=%s key_ms=%.0f",
                tostring(keycode),
                ATTACK_KEY_LABEL,
                tostring(reason or ""),
                tostring(target and target.name or ""),
                tostring(obj),
                key_ms),
            0,
            true)
        return true, "sent", key_ms
    end

    combat_log("attack-key-failed:" .. tostring(obj),
        "attack key failed key=" .. tostring(keycode) .. " reason=" .. tostring(reason or "") .. " err=" .. tostring(err),
        0,
        true)
    return false, err or "PressKey failed"
end

function combat_attack_key_repeat_settings()
    local ok_module, module = pcall(require, "aion.attack_key_repeat")
    if ok_module and module and type(module.from_config) == "function" then
        return module.from_config(cfg.combat or {})
    end
    return {
        interval_ms = math.max(250, math.min(3000, math.floor(tonumber(cfg.combat and cfg.combat.attack_key_repeat_interval_ms) or 1000))),
    }
end

function combat_should_send_attack_key(target, obj, force)
    obj = tonumber(obj) or combat_entity_obj(target)
    if obj <= 0 then
        return false, "invalid-target"
    end
    if force == true then
        return true, "force"
    end

    local settings = combat_attack_key_repeat_settings()
    local ok_module, module = pcall(require, "aion.attack_key_repeat")
    if ok_module and module and type(module.should_press) == "function" then
        return module.should_press({
            now = now_seconds(),
            target_obj = obj,
            last_attack_key_at = runtime.combat.last_attack_key_at,
            last_attack_key_obj = runtime.combat.last_attack_key_obj,
            interval_ms = settings.interval_ms,
        })
    end

    local c = runtime.combat
    if tonumber(c.last_attack_key_obj) ~= obj then
        return true, "new-target"
    end
    local last_at = tonumber(c.last_attack_key_at) or 0
    if last_at <= 0 then
        return true, "no-previous-key"
    end
    if (now_seconds() - last_at) * 1000 >= (tonumber(settings.interval_ms) or 1000) then
        return true, "interval"
    end
    return false, "waiting"
end

function combat_maybe_send_attack_key(target, obj, reason, force)
    obj = tonumber(obj) or combat_entity_obj(target)
    if obj <= 0 then
        return false, "invalid target obj", 0, false
    end

    local should_send, cadence_reason = combat_should_send_attack_key(target, obj, force)
    if not should_send then
        return true, "wait-" .. tostring(cadence_reason or ""), 0, false
    end

    local key_ok, key_state, key_ms = combat_send_attack_key(target, obj,
        tostring(reason or "target") .. ":" .. tostring(cadence_reason or "repeat"))
    return key_ok, key_state, tonumber(key_ms) or 0, true
end

function combat_engage_target(target)
    local c = runtime.combat
    local obj = combat_entity_obj(target)
    if obj <= 0 then
        return false
    end
    if not ok_combat or not combat then
        combat_set_status("error", "aion.combat 不可用", true)
        return false
    end

    local now = now_seconds()
    local engage_started = now
    local select_ms = 0
    local auto_ms = 0
    local same_target = tonumber(c.target_obj) == obj
    local use_attack_key = combat_uses_attack_key()
    local force_auto_on = false
    local auto_handled = false
    local auto_state = use_attack_key and ("key" .. ATTACK_KEY_LABEL .. "-new-target") or "new-target"
    if not same_target then
        local select_started = now_seconds()
        local select_ok, selected, select_err = combat.selectTarget(obj)
        c.last_select_at = now_seconds()
        if not select_ok or selected == false then
            combat_set_status("select-failed", tostring(select_err), true)
            combat_log("select-failed:" .. tostring(obj),
                "select failed name=" .. tostring(target.name or "") .. " obj=" .. tostring(obj) .. " err=" .. tostring(select_err),
                0,
                true)
            return false
        end
        select_ms = math.max(0, (now_seconds() - select_started) * 1000)
        c.target_obj = obj
        c.target_name = tostring(target.name or "")
        c.target_distance = tonumber(target.distance) or 0
        c.post_kill_until = 0
        c.post_kill_started_at = 0
        combat_begin_target_progress(target, obj)
        if use_attack_key then
            c.force_auto_until = 0
            c.last_force_auto_at = 0
            local key_ok, key_state, key_ms = combat_maybe_send_attack_key(target, obj, "new-target", true)
            auto_ms = tonumber(key_ms) or 0
            if not key_ok then
                combat_set_status("attack-key-failed", tostring(key_state), true)
                return false
            end
            auto_state = "key" .. ATTACK_KEY_LABEL .. "-" .. tostring(key_state or "sent")
        else
            c.force_auto_until = now_seconds() + (tonumber(cfg.combat.auto_force_window) or 0.60)
            c.last_force_auto_at = 0
            force_auto_on = true
            local auto_started = now_seconds()
            local auto_ok, auto_err, already_on = combat_auto_on(true)
            auto_ms = math.max(0, (now_seconds() - auto_started) * 1000)
            if not auto_ok then
                combat_set_status("auto-battle-failed", tostring(auto_err), true)
                combat_log("auto-failed:" .. tostring(obj),
                    "auto battle on failed name=" .. tostring(target.name or "") .. " obj=" .. tostring(obj) .. " err=" .. tostring(auto_err),
                    0,
                    true)
                return false
            end
            if already_on then
                auto_state = "already-on"
            else
                auto_state = "forced-start"
                c.last_auto_on_at = now_seconds()
            end
            c.last_force_auto_at = now_seconds()
        end
        auto_handled = true
        c.last_move_at = 0
        combat_log("select:" .. tostring(obj),
            string.format("selected name=%s obj=%s select_ms=%.0f",
                tostring(target.name or ""),
                tostring(obj),
                select_ms),
            0,
            true)
    end

    local should_auto_on = (not use_attack_key) and ((not same_target) or tostring(c.status or "") ~= "fighting") and not auto_handled
    local force_window_active = (not use_attack_key) and same_target and (tonumber(c.force_auto_until) or 0) > now_seconds()
    if use_attack_key and same_target then
        local key_ok, key_state, key_ms = combat_maybe_send_attack_key(target, obj, "target-alive", false)
        auto_ms = tonumber(key_ms) or 0
        if not key_ok then
            combat_set_status("attack-key-failed", tostring(key_state), true)
            return false
        end
        auto_state = "key" .. ATTACK_KEY_LABEL .. "-" .. tostring(key_state or "wait")
    elseif force_window_active
        and now_seconds() - (tonumber(c.last_force_auto_at) or 0) >= (tonumber(cfg.combat.auto_force_interval) or 0.10) then
        force_auto_on = true
        should_auto_on = true
        auto_state = "force-window"
    elseif (not use_attack_key) and same_target and tostring(c.status or "") == "fighting" and combat.isAutoBattleOn then
        local state_ok, is_on, state_err = combat.isAutoBattleOn()
        if state_ok then
            auto_state = tostring(is_on == true)
            should_auto_on = not (is_on == true)
        else
            auto_state = "check-failed:" .. tostring(state_err)
            should_auto_on = false
            combat_log("auto-state-failed:" .. tostring(obj),
                "auto battle state check failed name=" .. tostring(target.name or "") .. " obj=" .. tostring(obj) .. " err=" .. tostring(state_err),
                2.0,
                false)
        end
    elseif (not use_attack_key) and same_target and tostring(c.status or "") == "fighting" then
        auto_state = "unchecked"
        should_auto_on = false
    end
    if should_auto_on then
        local auto_started = now_seconds()
        local auto_ok, auto_err, already_on = combat_auto_on(force_auto_on)
        auto_ms = math.max(0, (now_seconds() - auto_started) * 1000)
        if not auto_ok then
            combat_set_status("auto-battle-failed", tostring(auto_err), true)
            combat_log("auto-failed:" .. tostring(obj),
                "auto battle on failed name=" .. tostring(target.name or "") .. " obj=" .. tostring(obj) .. " err=" .. tostring(auto_err),
                0,
                true)
            return false
        end
        if already_on then
            auto_state = "already-on"
        elseif force_auto_on then
            if auto_state ~= "force-window" then
                auto_state = "forced-start"
            end
            c.last_auto_on_at = now_seconds()
        else
            auto_state = "started"
            c.last_auto_on_at = now_seconds()
        end
        if force_auto_on then
            c.last_force_auto_at = now_seconds()
        end
    end

    c.target_obj = obj
    c.target_name = tostring(target.name or "")
    c.target_distance = tonumber(target.distance) or 0
    c.post_kill_until = 0
    c.post_kill_started_at = 0
    if not same_target and not use_attack_key then
        c.force_auto_until = math.max(tonumber(c.force_auto_until) or 0, now_seconds() + (tonumber(cfg.combat.auto_force_window) or 0.60))
    end
    if not same_target then
        c.last_tick_at = 0
    end
    combat_set_status("fighting", c.target_name, false)
    combat_log("fighting:" .. tostring(obj),
        string.format("fighting name=%s obj=%s dist=%.1f hp=%s/%s same=%s trigger=%s auto=%s force=%s auto_state=%s select_ms=%.0f auto_ms=%.0f total_ms=%.0f",
            c.target_name,
            tostring(obj),
            tonumber(c.target_distance) or 0,
            tostring(target.hp or ""),
            tostring(target.mhp or ""),
            tostring(same_target),
            use_attack_key and ("key" .. ATTACK_KEY_LABEL) or "auto_battle",
            tostring(should_auto_on),
            tostring(force_auto_on),
            tostring(auto_state),
            select_ms,
            auto_ms,
            math.max(0, (now_seconds() - engage_started) * 1000)),
        1.0,
        not same_target)
    return true
end

function combat_handle_priority_loot(list, char)
    if not cfg.combat or cfg.combat.loot_enabled ~= true then
        return false
    end
    if type(list) ~= "table" or type(char) ~= "table" then
        return false
    end

    local loot_target = combat_choose_loot(list, char, char)
    if not loot_target then
        return false
    end

    local loot_key = combat_loot_key(loot_target)
    if loot_key <= 0 then
        return false
    end

    local c = runtime.combat
    local pending_loot = tonumber(c.loot_obj) or 0
    if pending_loot > 0 and pending_loot ~= loot_key then
        combat_close_loot_dialog("loot-priority-clear-mismatch")
        combat_clear_pending_loot("loot-priority-mismatch", false)
    end

    local dist = ok_core and core and core.distance3(char, loot_target) or tonumber(loot_target.distance) or 9999
    loot_target.distance = dist
    combat_auto_off("loot-priority", false)
    combat_set_status("loot-priority", tostring(loot_target.name or ""), false)
    combat_log("loot-priority:" .. tostring(loot_key),
        string.format("priority loot claim name=%s obj=%s dist=%.1f interact_id=%s lootable=%s",
            tostring(loot_target.name or ""),
            tostring(loot_key),
            tonumber(dist) or 0,
            tostring(loot_target.interact_id or 0),
            tostring(loot_target.lootable or "")),
        0,
        true)
    combat_pick_loot_with_test_button(loot_target, char, "priority")
    return true
end

function combat_decide_post_kill_loot(args)
    local ok_module, module = pcall(require, "aion.post_kill_loot")
    if ok_module and module and type(module.decide) == "function" then
        return module.decide(args)
    end
    args = args or {}
    local now = tonumber(args.now) or 0
    local check_at = tonumber(args.post_kill_check_at or args.post_kill_until) or now
    local remain = math.max(0, check_at - now)
    if remain > 0 then
        return {
            action = "delay",
            reason = "check-delay",
            remain = remain,
        }
    end
    return {
        action = args.loot_target and "open-loot" or "skip",
        reason = args.loot_target and "loot-ready" or tostring(args.reject_reason or "not-lootable"),
        remain = 0,
    }
end

function combat_floor_recovery_state()
    runtime.maintenance = runtime.maintenance or {}
    if type(runtime.maintenance.floor_recovery) ~= "table" then
        runtime.maintenance.floor_recovery = {}
    end
    local state = runtime.maintenance.floor_recovery
    if state.active == nil then
        state.active = false
    end
    state.started_at = tonumber(state.started_at) or 0
    state.start_hp = tonumber(state.start_hp) or 0
    state.last_hp = tonumber(state.last_hp) or 0
    state.start_mp_percent = tonumber(state.start_mp_percent) or 0
    state.last_mp_percent = tonumber(state.last_mp_percent) or 0
    state.last_action = tostring(state.last_action or "")
    state.last_reason = tostring(state.last_reason or "")
    return state
end

function combat_floor_recovery_settings()
    normalize_supply_config()
    local ok_module, module = pcall(require, "aion.floor_recovery")
    if ok_module and module and type(module.from_config) == "function" then
        return module.from_config(cfg.supply)
    end
    return cfg.supply and cfg.supply.floor_recovery or {
        enabled = false,
        start_percent = 15,
        recover_percent = 90,
        sit_keycode = 188,
        stand_keycode = 88,
        cancel_on_damage = true,
    }
end

function combat_floor_recovery_decide(args)
    local ok_module, module = pcall(require, "aion.floor_recovery")
    if ok_module and module and type(module.decide) == "function" then
        return module.decide(args)
    end
    return {
        action = "skip",
        reason = "module-unavailable",
    }
end

function combat_floor_recovery_press_key(keycode, reason)
    local raw_keycode = tonumber(keycode) or 0
    if raw_keycode <= 0 then
        return false, "invalid keycode"
    end
    keycode = math.max(1, math.min(255, math.floor(raw_keycode)))
    local ok_remote, remote_runtime = pcall(require, "aion.remote")
    if not ok_remote or not remote_runtime or type(remote_runtime.pressKey) ~= "function" then
        return false, "aion.remote unavailable"
    end

    local started = now_seconds()
    local ok, _, err = remote_runtime.pressKey(keycode)
    local key_ms = math.max(0, (now_seconds() - started) * 1000)
    if ok then
        combat_log("floor-recovery-key:" .. tostring(reason or "") .. ":" .. tostring(keycode),
            string.format("floor recovery key sent key=%s reason=%s key_ms=%.0f",
                tostring(keycode),
                tostring(reason or ""),
                key_ms),
            0,
            true)
        return true, nil
    end

    combat_log("floor-recovery-key-failed:" .. tostring(reason or "") .. ":" .. tostring(keycode),
        "floor recovery key failed key=" .. tostring(keycode) .. " reason=" .. tostring(reason or "") .. " err=" .. tostring(err),
        0,
        true)
    return false, tostring(err or "PressKey failed")
end

function combat_floor_percent_text(value)
    local n = tonumber(value)
    if n == nil then
        return "?"
    end
    return string.format("%.1f", n)
end

function combat_floor_mp_pair_text(decision)
    if type(decision) ~= "table" then
        return "?/?"
    end
    return tostring(decision.mp_current or "?") .. "/" .. tostring(decision.mp_max or "?")
end

function combat_clear_floor_recovery(reason)
    local state = combat_floor_recovery_state()
    state.active = false
    state.started_at = 0
    state.start_hp = 0
    state.last_hp = 0
    state.start_mp_percent = 0
    state.last_mp_percent = 0
    state.last_action = ""
    state.last_reason = tostring(reason or "")
    runtime.combat.post_loot_maintenance_pending = false
    runtime.combat.post_loot_maintenance_source = ""
    runtime.combat.post_loot_maintenance_at = 0
end

function combat_update_floor_recovery_observation(state, decision)
    local hp = tonumber(decision and decision.hp)
    if hp ~= nil and hp > 0 then
        if (tonumber(state.last_hp) or 0) <= 0 or hp > (tonumber(state.last_hp) or 0) then
            state.last_hp = hp
        end
    end
    local mp_percent = tonumber(decision and decision.mp_percent)
    if mp_percent ~= nil then
        state.last_mp_percent = mp_percent
    end
end

function combat_handle_floor_recovery_after_loot(char)
    local c = runtime.combat
    local state = combat_floor_recovery_state()
    local active = state.active == true
    local pending_after_loot = c.post_loot_maintenance_pending == true
    if not active and not pending_after_loot then
        return false
    end

    local settings = combat_floor_recovery_settings()
    local decision = combat_floor_recovery_decide({
        settings = settings,
        state = state,
        after_loot_pending = pending_after_loot,
        char = char,
        in_combat = (tonumber(c.target_obj) or 0) > 0,
        loot_pending = (tonumber(c.loot_obj) or 0) > 0,
        post_kill_pending = (tonumber(c.last_killed_obj) or 0) > 0 or (tonumber(c.post_kill_until) or 0) > 0,
    })

    local action = tostring(decision and decision.action or "skip")
    local reason = tostring(decision and decision.reason or "")
    state.last_action = action
    state.last_reason = reason

    if action == "idle" then
        return false
    end

    if action == "defer" then
        combat_set_status("floor-recovery-defer", reason, false)
        combat_log("floor-recovery-defer",
            "floor recovery deferred reason=" .. reason,
            0.5,
            false)
        return true
    end

    if action == "skip" then
        c.post_loot_maintenance_pending = false
        c.post_loot_maintenance_source = ""
        c.post_loot_maintenance_at = 0
        combat_log("floor-recovery-skip",
            string.format("after-loot floor recovery skipped reason=%s mp=%s raw=%s start=%s recover=%s enabled=%s",
                reason,
                combat_floor_percent_text(decision and decision.mp_percent),
                combat_floor_mp_pair_text(decision),
                tostring(settings.start_percent),
                tostring(settings.recover_percent),
                tostring(settings.enabled == true)),
            0,
            true)
        return false
    end

    if action == "start" then
        local source = tostring(c.post_loot_maintenance_source or "")
        c.post_loot_maintenance_pending = false
        c.post_loot_maintenance_source = ""
        c.post_loot_maintenance_at = 0
        local key_ok, key_err = combat_floor_recovery_press_key(decision.keycode or settings.sit_keycode, "sit")
        if not key_ok then
            combat_set_status("floor-recovery-error", tostring(key_err), true)
            return false
        end
        state.active = true
        state.started_at = now_seconds()
        state.start_hp = tonumber(decision.hp) or 0
        state.last_hp = tonumber(decision.hp) or 0
        state.start_mp_percent = tonumber(decision.mp_percent) or 0
        state.last_mp_percent = tonumber(decision.mp_percent) or 0
        combat_auto_off("floor-recovery-start", false)
        combat_set_status("floor-recovery", "mp=" .. combat_floor_percent_text(decision.mp_percent) .. "%", true)
        combat_log("floor-recovery-start",
            string.format("after-loot floor recovery start mp=%s raw=%s start_below=%s recover_to=%s hp=%s source=%s",
                combat_floor_percent_text(decision.mp_percent),
                combat_floor_mp_pair_text(decision),
                tostring(settings.start_percent),
                tostring(settings.recover_percent),
                tostring(decision.hp or ""),
                source),
            0,
            true)
        return true
    end

    if action == "wait" then
        combat_update_floor_recovery_observation(state, decision)
        combat_set_status("floor-recovery", "mp=" .. combat_floor_percent_text(decision.mp_percent) .. "%", false)
        combat_log("floor-recovery-wait",
            string.format("floor recovery wait mp=%s raw=%s recover_to=%s hp=%s elapsed=%.1fs",
                combat_floor_percent_text(decision and decision.mp_percent),
                combat_floor_mp_pair_text(decision),
                tostring(settings.recover_percent),
                tostring(decision and decision.hp or ""),
                math.max(0, now_seconds() - (tonumber(state.started_at) or now_seconds()))),
            1.0,
            false)
        return true
    end

    if action == "finish" then
        local key_ok, key_err = combat_floor_recovery_press_key(decision.keycode or settings.stand_keycode, "stand")
        if not key_ok then
            combat_set_status("floor-recovery-stand-failed", tostring(key_err), true)
            combat_update_floor_recovery_observation(state, decision)
            return true
        end
        combat_log("floor-recovery-done",
            string.format("floor recovery done mp=%s raw=%s recover_to=%s elapsed=%.1fs",
                combat_floor_percent_text(decision and decision.mp_percent),
                combat_floor_mp_pair_text(decision),
                tostring(settings.recover_percent),
                math.max(0, now_seconds() - (tonumber(state.started_at) or now_seconds()))),
            0,
            true)
        combat_clear_floor_recovery("recovered")
        combat_set_status("floor-recovery-done", "mp=" .. combat_floor_percent_text(decision.mp_percent) .. "%", false)
        return true
    end

    if action == "cancel" then
        combat_floor_recovery_press_key(decision.keycode or settings.stand_keycode, "cancel")
        combat_log("floor-recovery-cancel",
            string.format("floor recovery cancelled reason=%s mp=%s raw=%s hp=%s start_hp=%s last_hp=%s",
                reason,
                combat_floor_percent_text(decision and decision.mp_percent),
                combat_floor_mp_pair_text(decision),
                tostring(decision and decision.hp or ""),
                tostring(state.start_hp or ""),
                tostring(state.last_hp or "")),
            0,
            true)
        combat_clear_floor_recovery(reason)
        combat_set_status("floor-recovery-cancel", reason, true)
        return false
    end

    c.post_loot_maintenance_pending = false
    combat_log("floor-recovery-unknown",
        "floor recovery unknown action=" .. action .. " reason=" .. reason,
        0,
        true)
    return false
end

function combat_tick(force_stationary)
    local quest_stationary = force_stationary == true
    if quest_stationary then
        if not combat_is_quest_grind_enabled() then
            return
        end
    elseif not combat_is_active_enabled() then
        return
    end

    local c = runtime.combat
    local patrol_mode = (not quest_stationary) and combat_is_patrol_enabled()
    local now = now_seconds()
    local interval = tonumber(cfg.combat.tick_interval) or 0.10
    if c.last_tick_at > 0 and now - c.last_tick_at < interval then
        return
    end
    c.last_tick_at = now

    if patrol_mode then
        if not combat_current_patrol_anchor() then
            return
        end
    else
        if c.mode ~= "stationary" then
            c.anchor = nil
            c.last_move_at = 0
        end
        c.mode = "stationary"
        if not combat_ensure_anchor() then
            return
        end
    end
    if not ok_core or not core then
        combat_set_status("error", "aion.core 不可用", true)
        return
    end

    local char_ok, char, char_err = core.getCharacter()
    if not char_ok or not char then
        combat_set_status("error", "读取角色失败: " .. tostring(char_err), true)
        return
    end
    local char_hp = tonumber(char.hp or char.HP or char.cur_hp or char.current_hp)
    local char_dead = char.is_dead == true or char.dead == true or (char_hp ~= nil and char_hp <= 0)
    if char_dead then
        local confirmed = true
        if type(route_recovery_maybe_confirm_death) == "function" then
            confirmed = route_recovery_maybe_confirm_death("combat-dead", char)
        end
        if not confirmed then
            combat_set_status("death-confirm", "pending", false)
            combat_log("death-confirm",
                "death read pending hp=" .. tostring(char_hp or "") .. "; hold combat/recovery",
                0.5,
                false)
            return
        end
        combat_auto_off("dead", true)
        combat_set_status("dead", "", true)
        combat_log("dead", "character dead confirmed, auto battle off", 0, true)
        if route_recovery_on_death then
            route_recovery_on_death("combat-dead")
        end
        return
    end

    local anchor = c.anchor
    c.anchor_distance = core.distance3(char, anchor)
    if patrol_mode then
        combat_skip_arrived_patrol_anchors(char)
        anchor = c.anchor
        if not anchor then
            return
        end
        c.anchor_distance = core.distance3(char, anchor)
    end

    local search_anchor = patrol_mode and char or anchor

    if not ok_entity or not entity then
        combat_set_status("error", "aion.entity 不可用", true)
        return
    end
    local list_ok, list, list_err = entity.list()
    if not list_ok then
        combat_set_status("error", "读取实体失败: " .. tostring(list_err), true)
        return
    end

    local entity_count = #(list or {})
    local radius = tonumber(cfg.combat.radius) or 35
    combat_log("tick",
        string.format("tick mode=%s patrol=%s status=%s char=%s hp=%s/%s anchor_dist=%.1f radius=%.1f entities=%d anchor=%s",
            tostring(c.mode or ""),
            tostring(patrol_mode),
            tostring(c.status or ""),
            tostring(char.name or ""),
            tostring(char.hp or ""),
            tostring(char.max_hp or char.mhp or ""),
            tonumber(c.anchor_distance) or 0,
            tonumber(radius) or 0,
            entity_count,
            combat_anchor_text()),
        nil,
        false)

    local tracked_obj = tonumber(c.target_obj) or 0
    if tracked_obj > 0 then
        local tracked = combat_find_entity_by_obj(list, tracked_obj)
        if not tracked and combat_current_target_matches_obj(tracked_obj) then
            if combat_uses_attack_key() then
                local key_ok, key_state = combat_maybe_send_attack_key({ name = c.target_name }, tracked_obj, "tracked-current", false)
                if not key_ok then
                    combat_set_status("attack-key-failed", tostring(key_state), true)
                    return
                end
            end
            combat_log("tracked-target-current:" .. tostring(tracked_obj),
                "tracked target missing from entity list but still current target obj=" .. tostring(tracked_obj) .. "; hold combat",
                0.5,
                false)
            combat_set_status("fighting", c.target_name, false)
            return
        end
        local end_reason = combat_target_end_reason(tracked)
        if not end_reason then
            tracked.distance = core.distance3(char, tracked)
            local failure_reason = combat_target_failure_reason(tracked, tracked_obj)
            if failure_reason then
                combat_ignore_target(tracked_obj, failure_reason, tracked.name or c.target_name)
                combat_abort_target(failure_reason, tracked)
            else
                combat_log("tracked-target:" .. tostring(tracked_obj),
                    string.format("continue tracked target name=%s obj=%s dist=%.1f hp=%s/%s; skip move/loot",
                        tostring(tracked.name or c.target_name or ""),
                        tostring(tracked_obj),
                        tonumber(tracked.distance) or 0,
                        tostring(tracked.hp or ""),
                        tostring(tracked.mhp or "")),
                    0.5,
                    false)
                combat_engage_target(tracked)
                return
            end
        end
        if end_reason == "missing" then
            combat_abort_target("missing", tracked)
        elseif end_reason then
            combat_begin_post_kill(end_reason, tracked)
        end
    end

    local previous_target_obj = tonumber(c.target_obj) or 0
    if previous_target_obj > 0 then
        local previous = combat_find_entity_by_obj(list, previous_target_obj)
        local end_reason = nil
        if previous then
            local reject_reason = combat_target_reject_reason(previous, search_anchor)
            if reject_reason == "hp-zero" or reject_reason == "lootable" or reject_reason == "dead" then
                end_reason = reject_reason
            end
        else
            end_reason = "missing"
        end
        if end_reason then
            combat_begin_post_kill(end_reason, previous)
        end
    end

    -- Post-kill loot has priority over reacquiring the game's current target.
    if cfg.combat.loot_enabled then
        local last_killed_obj = tonumber(c.last_killed_obj) or 0
        local post_kill_check_at = tonumber(c.post_kill_until) or 0
        if last_killed_obj > 0 then
            local pending_loot = tonumber(c.loot_obj) or 0
            if pending_loot > 0 and pending_loot ~= last_killed_obj then
                combat_close_loot_dialog("post-kill-clear-mismatch")
                combat_clear_pending_loot("post-kill-mismatch-current", false)
            end

            local decision = combat_decide_post_kill_loot({
                last_killed_obj = last_killed_obj,
                post_kill_check_at = post_kill_check_at,
                now = now,
            })
            if decision.action == "delay" then
                combat_log("post-kill-delay:" .. tostring(last_killed_obj),
                    string.format("wait one-shot loot check name=%s obj=%s interact_id=%s remain=%.2fs",
                        tostring(c.last_killed_name or ""),
                        tostring(last_killed_obj),
                        tostring(c.last_killed_interact_id or 0),
                        tonumber(decision.remain) or math.max(0, post_kill_check_at - now)),
                    0.5,
                    false)
                combat_set_status("post-kill-check", c.last_killed_name, false)
                return
            end

            local loot_target, reject_reason, seen_entity, checked = combat_find_last_killed_loot(list, char, search_anchor)
            local seen_text = "false"
            local lootable_text = ""
            local dist_text = ""
            if seen_entity then
                seen_text = "true"
                lootable_text = tostring(seen_entity.lootable or "")
                if ok_core and core then
                    dist_text = string.format("%.1f", core.distance3(char, seen_entity))
                end
            end

            decision = combat_decide_post_kill_loot({
                last_killed_obj = last_killed_obj,
                post_kill_check_at = post_kill_check_at,
                now = now,
                loot_target = loot_target,
                reject_reason = reject_reason,
                seen_entity = seen_entity,
            })
            if decision.action == "open-loot" then
                combat_pick_loot_with_test_button(loot_target, char, "post-kill")
                return
            end

            if decision.action == "skip" then
                local skip_name = tostring(c.last_killed_name or "")
                combat_log("post-kill-no-loot:" .. tostring(last_killed_obj),
                    string.format("one-shot loot check done; skip corpse name=%s obj=%s interact_id=%s reason=%s seen=%s lootable=%s dist=%s checked=%d",
                        skip_name,
                        tostring(last_killed_obj),
                        tostring(c.last_killed_interact_id or 0),
                        tostring(decision.reason or reject_reason or ""),
                        seen_text,
                        lootable_text,
                        dist_text,
                        tonumber(checked) or 0),
                    0,
                    true)
                combat_clear_last_kill("not-lootable", false)
                combat_mark_post_loot_maintenance("post-kill-" .. tostring(decision.reason or reject_reason or "skip"))
                combat_set_status("post-kill-skip", skip_name, false)
                return
            end

            combat_clear_last_kill("post-kill-none", false)
            combat_mark_post_loot_maintenance("post-kill-none")
        end
    end

    if combat_handle_floor_recovery_after_loot(char) then
        return
    end

    if cfg.combat.loot_enabled then
        if combat_handle_priority_loot(list, char) then
            return
        end
    end

    local current = combat_find_current_entity(list, search_anchor)
    if current then
        current.distance = core.distance3(char, current)
        combat_log("current-target:" .. tostring(combat_entity_obj(current)),
            string.format("continue current target name=%s obj=%s dist=%.1f hp=%s/%s",
                tostring(current.name or ""),
                tostring(combat_entity_obj(current)),
                tonumber(current.distance) or 0,
                tostring(current.hp or ""),
                tostring(current.mhp or "")),
            1.0,
            false)
        combat_engage_target(current)
        return
    end

    local target = combat_choose_target(list, char, search_anchor)
    if target then
        target.distance = core.distance3(char, target)
        combat_engage_target(target)
        return
    end

    c.target_obj = 0
    c.target_name = ""
    c.target_distance = 0
    if patrol_mode then
        local waypoint_radius = math.max(0.5, tonumber(cfg.route.waypoint_radius) or 3)
        if c.anchor_distance > waypoint_radius then
            combat_log("patrol-moving",
                string.format("patrol moving %s anchor_dist=%.1f waypoint_radius=%.1f",
                    combat_patrol_text(),
                    tonumber(c.anchor_distance) or 0,
                    tonumber(waypoint_radius) or 0),
                1.0,
                false)
            combat_move_anchor("patrol-moving")
        else
            if combat_advance_patrol_anchor() then
                combat_log("patrol-advance", "patrol advance " .. combat_patrol_text(), 0, true)
                combat_current_patrol_anchor()
                if c.anchor then
                    c.anchor_distance = core.distance3(char, c.anchor)
                    if c.anchor_distance > waypoint_radius then
                        combat_move_anchor("patrol-moving")
                    end
                end
            end
        end
        return
    end

    local return_radius = tonumber(cfg.combat.return_radius) or 4
    if c.anchor_distance > return_radius then
        combat_log("no-target-return",
            string.format("no target, return center anchor_dist=%.1f return_radius=%.1f",
                tonumber(c.anchor_distance) or 0,
                tonumber(return_radius) or 0),
            1.0,
            false)
        combat_move_anchor("no-target-return")
    else
        combat_auto_off("waiting-target", false)
        combat_set_status("waiting-target", "", false)
        combat_log("waiting-target",
            string.format("waiting target at anchor anchor_dist=%.1f entities=%d", tonumber(c.anchor_distance) or 0, entity_count),
            nil,
            false)
    end
end

function combat_tick_quest_grind()
    combat_tick(true)
end

function npc_dialog_set_status(text)
    local message = tostring(text or "")
    runtime.npc_dialog.last_status = message
    set_event(message)
end

function npc_scan_nearby()
    if not ok_core or not core or type(core.ensureInit) ~= "function" then
        npc_dialog_set_status("NPC扫描失败: aion.core 不可用")
        return false
    end

    if not ok_entity or not entity then
        npc_dialog_set_status("NPC扫描失败: aion.entity 不可用")
        return false
    end

    local pid = tonumber(cfg.target and cfg.target.pid) or 0
    if pid <= 0 then
        npc_dialog_set_status("NPC扫描失败: 请先绑定目标PID")
        return false
    end

    local init_ok, init_err = core.ensureInit(pid)
    if not init_ok then
        npc_dialog_set_status("NPC扫描失败: AionData初始化失败 .. tostring(init_err)")
        return false
    end

    local char_ok, char, char_err = core.getCharacter()
    if not char_ok or not char then
        npc_dialog_set_status("NPC扫描失败: 当前角色不可用 .. tostring(char_err)")
        return false
    end

    local list_ok, around, list_err = entity.list()
    if not list_ok then
        npc_dialog_set_status("NPC扫描失败: 读取周围实体失败 " .. tostring(list_err))
        return false
    end

    cfg.npc_dialog = cfg.npc_dialog or {}
    local radius = tonumber(cfg.npc_dialog.scan_radius) or 45
    local limit = math.max(1, tonumber(cfg.npc_dialog.scan_limit) or 12)
    local candidates = {}

    for _, item in ipairs(around or {}) do
        local name = tostring(item.name or "")
        local interact_id = tonumber(item.interact_id) or 0
        local dead = item.dead == true or item.dead == 1 or item.dead == "true"
        if name ~= "" and interact_id ~= 0 and not dead then
            local distance = core.distance3(char, item)
            if distance <= radius then
                item.distance = distance
                candidates[#candidates + 1] = item
            end
        end
    end

    table.sort(candidates, function(a, b)
        return (tonumber(a.distance) or 999999) < (tonumber(b.distance) or 999999)
    end)

    local labels = {}
    local lines = {}
    local capped = {}
    for index, item in ipairs(candidates) do
        if index > limit then
            break
        end
        capped[#capped + 1] = item
        local line = string.format("%02d. %s  dist=%.1f  interact_id=%s",
            index,
            tostring(item.name or ""),
            tonumber(item.distance) or 0,
            tostring(item.interact_id or 0))
        labels[#labels + 1] = line
        lines[#lines + 1] = line
    end

    if #labels == 0 then
        labels[1] = "No nearby NPC"
    end

    runtime.npc_dialog.candidates = capped
    runtime.npc_dialog.candidate_labels = labels
    runtime.npc_dialog.selected_index = math.max(1, math.min(tonumber(runtime.npc_dialog.selected_index) or 1, #labels))
    runtime.npc_dialog.last_scan_text = table.concat(lines, "\n")

    log_info("[AionControlUI] NPC扫描半径=" .. tostring(radius) .. " 命中=" .. tostring(#capped))
    for _, line in ipairs(lines) do
        log_info("[NPC-SCAN] " .. line)
    end

    npc_dialog_set_status("NPC扫描完成: " .. tostring(#capped) .. " 个，已打印到日志")
    return true
end

function npc_fill_selected_candidate()
    local index = tonumber(runtime.npc_dialog.selected_index) or 1
    local item = runtime.npc_dialog.candidates and runtime.npc_dialog.candidates[index]
    if not item or not item.name or item.name == "" then
        npc_dialog_set_status("填入NPC失败: 请先按F1扫描并选择NPC")
        return false
    end

    cfg.npc_dialog.accept_npc_name = item.name
    cfg.npc_dialog.accept_npc_interact_id = tostring(item.interact_id or "")
    npc_dialog_set_status("已填入接任务NPC: " .. tostring(item.name))
    return true
end

function npc_dialog_prepare_runtime()
    if not ok_core or not core or type(core.ensureInit) ~= "function" then
        npc_dialog_set_status("NPC对话失败: aion.core 不可用")
        return false, nil, nil
    end

    local ok_npc_runtime, npc_runtime = pcall(require, "aion.npc")
    if not ok_npc_runtime or not npc_runtime then
        npc_dialog_set_status("NPC对话失败: aion.npc 不可用")
        return false, nil, nil
    end

    local ok_ui_runtime, ui_runtime = pcall(require, "aion.ui")
    if not ok_ui_runtime or not ui_runtime then
        npc_dialog_set_status("NPC对话失败: aion.ui 不可用")
        return false, nil, nil
    end

    local pid = tonumber(cfg.target and cfg.target.pid) or 0
    if pid <= 0 then
        npc_dialog_set_status("NPC对话失败: 请先绑定目标PID")
        return false, nil, nil
    end

    local init_ok, init_err = core.ensureInit(pid)
    if not init_ok then
        npc_dialog_set_status("NPC对话失败: AionData初始化失败 .. tostring(init_err)")
        return false, nil, nil
    end

    return true, npc_runtime, ui_runtime
end

function npc_open_dialog_by_interact_id()
    local ready, npc_runtime = npc_dialog_prepare_runtime()
    if not ready then
        return false
    end

    cfg.npc_dialog = cfg.npc_dialog or {}
    local interact_id = tonumber(cfg.npc_dialog.accept_npc_interact_id) or 0
    if interact_id <= 0 then
        npc_dialog_set_status("用ID打开NPC失败: 请先填写接任务NPC ID")
        return false
    end

    local interact_ok, _, interact_err = npc_runtime.interactId(interact_id)
    if not interact_ok then
        npc_dialog_set_status("用ID打开NPC失败: interact_id=" .. tostring(interact_id) .. " err=" .. tostring(interact_err))
        return false
    end

    local wait_ok, dialog_or_err = npc_runtime.waitDialog(tonumber(cfg.npc_dialog.wait_dialog_ms) or 3000)
    if not wait_ok or not dialog_or_err then
        npc_dialog_set_status("用ID打开NPC失败: 等待对话框超时: interact_id=" .. tostring(interact_id) .. " err=" .. tostring(dialog_or_err))
        return false
    end

    npc_dialog_set_status("用ID打开NPC成功: interact_id=" .. tostring(interact_id) ..
        " npc_dialog_id=" .. tostring(dialog_or_err.npc_dialog_id or 0))
    npc_dump_current_dialog()
    return true
end

function npc_dialog_child_label(index, child)
    local obj = child and (child.obj or child.addr) or 0
    local name = tostring(child and child.name or "")
    if name == "" then
        name = "(no-name)"
    end
    return string.format(
        "%02d. depth=%s obj=%s name=%s visible=%s x=%.0f y=%.0f",
        index,
        tostring(child and child.depth or ""),
        tostring(obj or 0),
        name,
        tostring(child and child.visible),
        tonumber(child and child.x) or 0,
        tonumber(child and child.y) or 0)
end

function npc_dump_current_dialog()
    local ready, npc_runtime, ui_runtime = npc_dialog_prepare_runtime()
    if not ready then
        log_warn("[AionDialogF8] prepare failed: " .. tostring(runtime.npc_dialog and runtime.npc_dialog.last_status or ""))
        return false
    end

    local dialog_ok, info_or_err, dialog_err = npc_runtime.dialog()
    if not dialog_ok then
        local message = "读取NPC对话失败: " .. tostring(dialog_err or info_or_err)
        log_warn("[AionDialogF8] " .. message)
        npc_dialog_set_status(message)
        return false
    end
    if not info_or_err then
        local message = "读取NPC对话失败: 当前没有打开NPC对话框"
        log_warn("[AionDialogF8] " .. message)
        npc_dialog_set_status(message)
        return false
    end

    local info = info_or_err
    local summary = string.format(
        "npc_dialog_id=%s content_id=%s quest_id=%s type=%s next=%s has_next=%s text=%s",
        tostring(info.npc_dialog_id),
        tostring(info.dialog_content_id),
        tostring(info.quest_id),
        tostring(info.type_text or ""),
        tostring(info.next_text or ""),
        tostring(info.has_next),
        tostring(info.content_text or ""))
    log_info("[AionDialogF8] " .. summary)

    cfg.npc_dialog = cfg.npc_dialog or {}
    local depth = math.max(1, tonumber(cfg.npc_dialog.dialog_child_depth) or 6)
    local child_ok, children, child_err = ui_runtime.children("dlg_dialog", depth)
    if not child_ok then
        local message = "读取NPC对话子控件失败: " .. tostring(child_err)
        log_warn("[AionDialogF8] " .. message)
        npc_dialog_set_status(message)
        return false
    end

    local capped = {}
    local labels = {}
    local lines = {}
    for _, child in ipairs(children or {}) do
        if child and (tonumber(child.obj) or 0) ~= 0 then
            capped[#capped + 1] = child
            local line = npc_dialog_child_label(#capped, child)
            labels[#labels + 1] = line
            lines[#lines + 1] = line
            log_info("[AionDialogF8] child " .. line)
            if #capped >= 120 then
                break
            end
        end
    end

    if #labels == 0 then
        labels[1] = "No dialog child"
    end

    runtime.npc_dialog.dialog_children = capped
    runtime.npc_dialog.dialog_child_labels = labels
    runtime.npc_dialog.selected_child_index = math.max(1, math.min(tonumber(runtime.npc_dialog.selected_child_index) or 1, #labels))
    runtime.npc_dialog.last_dialog_dump = table.concat(lines, "\n")

    npc_dialog_set_status("读取NPC对话完成: type=" .. tostring(info.type_text or "") ..
        " quest=" .. tostring(info.quest_id or 0) ..
        " children=" .. tostring(#capped))
    return true, info
end

function npc_click_selected_dialog_child()
    local ready, _, ui_runtime = npc_dialog_prepare_runtime()
    if not ready then
        return false
    end

    local index = tonumber(runtime.npc_dialog.selected_child_index) or 1
    local child = runtime.npc_dialog.dialog_children and runtime.npc_dialog.dialog_children[index]
    local obj = child and tonumber(child.obj or child.addr) or 0
    if obj <= 0 then
        npc_dialog_set_status("点击NPC对话控件失败: 请先读取当前对话并选择子控件")
        return false
    end

    local click_ok, clicked, click_err = ui_runtime.click(obj)
    if not click_ok or clicked == false then
        npc_dialog_set_status("点击NPC对话控件失败: " .. tostring(click_err or clicked))
        return false
    end

    npc_dialog_set_status("已点击NPC对话控件: " .. npc_dialog_child_label(index, child))
    return true
end

function npc_accept_current_dialog()
    local ready, npc_runtime = npc_dialog_prepare_runtime()
    if not ready then
        return false
    end

    local dialog_ok, info_or_err, dialog_err = npc_runtime.dialog()
    if not dialog_ok then
        npc_dialog_set_status("发送当前NPC对话失败: " .. tostring(dialog_err or info_or_err))
        return false
    end
    if not info_or_err then
        npc_dialog_set_status("发送当前NPC对话失败: 当前没有打开NPC对话框")
        return false
    end

    local info = info_or_err
    local next_id = tonumber(cfg.npc_dialog.accept_next_dialog_id) or 0
    local quest_id = tonumber(cfg.npc_dialog.accept_quest_id) or 0
    if quest_id <= 0 then
        quest_id = tonumber(info.quest_id) or 0
    end

    if tostring(info.type_text or "") == "select_quest" and quest_id <= 0 then
        npc_dialog_set_status("当前是任务列表(select_quest)，quest_id=0；请先读取对话并点击任务条目，或手动填quest_id")
        return false
    end

    local send_ok, sent, send_err = npc_runtime.sendDialog(info, next_id, quest_id)
    if not send_ok or sent == false then
        npc_dialog_set_status("发送当前NPC对话失败: SendNpcDialog失败 " .. tostring(send_err or sent))
        return false
    end

    npc_dialog_set_status(string.format(
        "已发送当前NPC对话: npc_dialog_id=%s next=%s content=%s quest=%s type=%s",
        tostring(info.npc_dialog_id),
        tostring(next_id),
        tostring(info.dialog_content_id),
        tostring(quest_id),
        tostring(info.type_text or "")
    ))
    return true
end

function npc_click_selected_child_then_accept()
    if not npc_click_selected_dialog_child() then
        return false
    end
    sleep(500)
    npc_dump_current_dialog()
    return npc_accept_current_dialog()
end

function npc_find_dialog_child_by_x(children, target_x, tolerance, target_y, y_tolerance)
    local best_child = nil
    local best_score = nil
    local best_index = 0
    local best_delta = nil
    local use_y = tonumber(target_y) ~= nil
    target_y = tonumber(target_y) or 0
    y_tolerance = math.max(0, tonumber(y_tolerance) or 8)

    for index, child in ipairs(children or {}) do
        local obj = tonumber(child and (child.obj or child.addr)) or 0
        local visible = child and child.visible == true
        local x = tonumber(child and child.x) or 0
        local y = tonumber(child and child.y) or 0
        local delta = math.abs(x - target_x)
        local y_delta = math.abs(y - target_y)
        if obj > 0 and visible and delta <= tolerance and (not use_y or y_delta <= y_tolerance) then
            local score = delta
            if use_y then
                score = score + y_delta
            end
            if not best_score or score < best_score then
                best_child = child
                best_score = score
                best_delta = delta
                best_index = index
            end
        end
    end

    return best_child, best_index, best_delta
end

function npc_find_dialog_child_by_name(children, target_name)
    local best_child = nil
    local best_index = 0
    local best_y = nil

    for index, child in ipairs(children or {}) do
        local obj = tonumber(child and (child.obj or child.addr)) or 0
        local visible = child and child.visible == true
        local name = tostring(child and child.name or "")
        local y = tonumber(child and child.y) or 0
        if obj > 0 and visible and name == target_name then
            if not best_y or y > best_y then
                best_child = child
                best_index = index
                best_y = y
            end
        end
    end

    return best_child, best_index
end

function npc_click_dialog_x_once(ui_runtime, step, click_x, click_y, click_y_tolerance)
    cfg.npc_dialog = cfg.npc_dialog or {}
    local depth = math.max(1, tonumber(cfg.npc_dialog.dialog_child_depth) or 6)
    local target_x = tonumber(click_x) or tonumber(cfg.npc_dialog.auto_click_x) or 25
    local target_y = tonumber(click_y)
    local tolerance = math.max(0, tonumber(cfg.npc_dialog.auto_click_x_tolerance) or 2)
    local y_tolerance = math.max(0, tonumber(click_y_tolerance)
        or tonumber(cfg.npc_dialog.auto_click_y_tolerance)
        or 8)

    local child_ok, children, child_err = ui_runtime.children("dlg_dialog", depth)
    if not child_ok then
        return false, "读取NPC对话子控件失败: " .. tostring(child_err)
    end

    local child, index = npc_find_dialog_child_by_x(children, target_x, tolerance, target_y, y_tolerance)
    if not child then
        return false, "dialog child not found x=" .. tostring(target_x) ..
            " y=" .. tostring(target_y or "")
    end

    local click_ok, clicked, click_err = ui_runtime.click(child.obj or child.addr)
    if not click_ok or clicked == false then
        return false, "点击x控件失败: " .. tostring(click_err or clicked)
    end

    local line = npc_dialog_child_label(index, child)
    log_info("[NPC-AUTO-CLICK] step=" .. tostring(step or 1) ..
        " target_x=" .. tostring(target_x) ..
        " target_y=" .. tostring(target_y or "") ..
        " " .. line)
    return true, line
end

function npc_click_dialog_ok_button(ui_runtime)
    cfg.npc_dialog = cfg.npc_dialog or {}
    local depth = math.max(1, tonumber(cfg.npc_dialog.dialog_child_depth) or 6)

    local child_ok, children, child_err = ui_runtime.children("dlg_dialog", depth)
    if not child_ok then
        return false, "读取NPC对话子控件失败: " .. tostring(child_err)
    end

    local child, index = npc_find_dialog_child_by_name(children, "ok")
    if not child then
        return false, "未找到name=ok visible=true 的按钮"
    end

    local click_ok, clicked, click_err = ui_runtime.click(child.obj or child.addr)
    if not click_ok or clicked == false then
        return false, "点击OK按钮失败: " .. tostring(click_err or clicked)
    end

    local line = npc_dialog_child_label(index, child)
    log_info("[NPC-AUTO-OK] " .. line)
    return true, line
end

function npc_continuous_click_dialog_x(opts)
    opts = opts or {}
    local ready, npc_runtime, ui_runtime = npc_dialog_prepare_runtime()
    if not ready then
        return false
    end

    cfg.npc_dialog = cfg.npc_dialog or {}
    local max_steps = math.max(1, tonumber(opts.max_steps) or tonumber(cfg.npc_dialog.auto_click_steps) or 8)
    local delay_ms = math.max(50, tonumber(opts.delay_ms) or tonumber(cfg.npc_dialog.auto_click_delay_ms) or 450)
    local click_x = tonumber(opts.click_x) or tonumber(cfg.npc_dialog.auto_click_x) or 25
    local click_y = tonumber(opts.click_y)
    local click_y_tolerance = tonumber(opts.click_y_tolerance)
    local clicked_count = 0
    local last_line = ""

    for step = 1, max_steps do
        local dialog_ok, info_or_err, dialog_err = npc_runtime.dialog()
        if not dialog_ok then
            npc_dialog_set_status("F1连续点击停止: 读取对话失败 " .. tostring(dialog_err or info_or_err))
            return clicked_count > 0, "dialog_read_failed"
        end
        if not info_or_err then
            npc_dialog_set_status("F1连续点击完成: 对话框已关闭，点击 " .. tostring(clicked_count) .. " 次")
            return clicked_count > 0, "closed"
        end

        local click_ok, line_or_err = npc_click_dialog_x_once(ui_runtime, step, click_x, click_y, click_y_tolerance)
        if not click_ok then
            npc_dialog_set_status("F1连续点击停止: " .. tostring(line_or_err) ..
                "，已点击 " .. tostring(clicked_count) .. " 次")
            return clicked_count > 0, "click_failed"
        end

        clicked_count = clicked_count + 1
        last_line = tostring(line_or_err or "")
        sleep(delay_ms)
    end

    local ok_clicked, ok_line_or_err = npc_click_dialog_ok_button(ui_runtime)
    if ok_clicked then
        npc_dialog_set_status("F1连续点击达到上限: " .. tostring(clicked_count) ..
            " 次，已补点OK，最后 " .. tostring(last_line) ..
            "，OK=" .. tostring(ok_line_or_err))
        return true, "limit_ok"
    end

    npc_dialog_set_status("F1连续点击达到上限: " .. tostring(clicked_count) ..
        " 次，补点OK失败: " .. tostring(ok_line_or_err) ..
        "，最后 " .. tostring(last_line))
    return clicked_count > 0, "limit_reached"
end

function npc_f1_action()
    local ready, npc_runtime = npc_dialog_prepare_runtime()
    if ready and npc_runtime then
        local dialog_ok, info = npc_runtime.dialog()
        if dialog_ok and info then
            return npc_continuous_click_dialog_x()
        end
    end

    return npc_scan_nearby()
end

function npc_accept_quest_test()
    if not ok_core or not core or type(core.ensureInit) ~= "function" then
        npc_dialog_set_status("NPC接任务失败: aion.core 不可用")
        return false
    end

    local ok_npc_runtime, npc_runtime = pcall(require, "aion.npc")
    if not ok_npc_runtime or not npc_runtime then
        npc_dialog_set_status("NPC接任务失败: aion.npc 不可用")
        return false
    end

    local pid = tonumber(cfg.target and cfg.target.pid) or 0
    if pid <= 0 then
        npc_dialog_set_status("NPC接任务失败: 请先绑定目标PID")
        return false
    end

    local init_ok, init_err = core.ensureInit(pid)
    if not init_ok then
        npc_dialog_set_status("NPC接任务失败: AionData初始化失败 .. tostring(init_err)")
        return false
    end

    cfg.npc_dialog = cfg.npc_dialog or {}
    local interact_id = tonumber(cfg.npc_dialog.accept_npc_interact_id) or 0
    local npc_name = tostring(cfg.npc_dialog.accept_npc_name or ""):gsub("^%s+", ""):gsub("%s+$", "")
    local info

    if interact_id > 0 then
        local interact_ok, _, interact_err = npc_runtime.interactId(interact_id)
        if not interact_ok then
            npc_dialog_set_status("NPC接任务失败: 无法用ID交互NPC interact_id=" .. tostring(interact_id) .. " " .. tostring(interact_err))
            return false
        end

        local wait_ok, dialog_or_err = npc_runtime.waitDialog(tonumber(cfg.npc_dialog.wait_dialog_ms) or 3000)
        if not wait_ok or not dialog_or_err then
            npc_dialog_set_status("NPC接任务失败: 用ID等待NPC对话框超时: interact_id=" .. tostring(interact_id) .. " " .. tostring(dialog_or_err))
            return false
        end
        info = dialog_or_err
    elseif npc_name ~= "" then
        local interact_ok, _, interact_err = npc_runtime.interactByName(npc_name)
        if not interact_ok then
            npc_dialog_set_status("NPC接任务失败: 找不到或无法交互NPC " .. npc_name .. " " .. tostring(interact_err))
            return false
        end

        local wait_ok, dialog_or_err = npc_runtime.waitDialog(tonumber(cfg.npc_dialog.wait_dialog_ms) or 3000)
        if not wait_ok or not dialog_or_err then
            npc_dialog_set_status("NPC接任务失败: 等待NPC对话框超时: " .. tostring(dialog_or_err))
            return false
        end
        info = dialog_or_err
    else
        local dialog_ok, dialog_or_err, dialog_err = npc_runtime.dialog()
        if not dialog_ok then
            npc_dialog_set_status("NPC接任务失败: 读取当前对话框失败:  .. tostring(dialog_err or dialog_or_err)")
            return false
        end
        if not dialog_or_err then
            npc_dialog_set_status("NPC接任务失败: 未配置NPC名，且当前没有打开NPC对话框")
            return false
        end
        info = dialog_or_err
    end

    local next_id = tonumber(cfg.npc_dialog.accept_next_dialog_id) or 0
    local quest_id = tonumber(cfg.npc_dialog.accept_quest_id) or 0
    if quest_id <= 0 then
        quest_id = tonumber(info.quest_id) or 0
    end

    if tostring(info.type_text or "") == "select_quest" and quest_id <= 0 then
        npc_dialog_set_status("当前是任务列表(select_quest)，quest_id=0；请先读取对话并点击任务条目，或手动填quest_id")
        return false
    end

    local send_ok, sent, send_err = npc_runtime.sendDialog(info, next_id, quest_id)
    if not send_ok or sent == false then
        npc_dialog_set_status("NPC接任务失败: SendNpcDialog失败 " .. tostring(send_err))
        return false
    end

    npc_dialog_set_status(string.format(
        "NPC接任务已发送: npc_dialog_id=%s next=%s content=%s quest=%s type=%s",
        tostring(info.npc_dialog_id),
        tostring(next_id),
        tostring(info.dialog_content_id),
        tostring(quest_id),
        tostring(info.type_text or "")
    ))
    return true
end

function main_quest_set_status(text)
    runtime.main_quest = runtime.main_quest or {}
    local message = tostring(text or "")
    runtime.main_quest.last_status = message
    set_event("[主线] " .. message)
    log_info("[AionMainQuest20590] " .. message)
end

function main_quest_dialog_signature(dialog)
    if type(dialog) ~= "table" then
        return "closed"
    end
    return "open:npc=" .. tostring(dialog.npc_dialog_id or "") ..
        ":content=" .. tostring(dialog.dialog_content_id or "") ..
        ":quest=" .. tostring(dialog.quest_id or "") ..
        ":type=" .. tostring(dialog.type_text or "") ..
        ":next=" .. tostring(dialog.next_dialog_id or dialog.next or "")
end

function main_quest_position_text(pos)
    if type(pos) ~= "table" then
        return "nil"
    end
    return string.format("%.2f,%.2f,%.2f",
        tonumber(pos.x) or 0,
        tonumber(pos.y) or 0,
        tonumber(pos.z) or 0)
end

function main_quest_trace(kind, message, min_interval)
    runtime.main_quest = runtime.main_quest or {}
    local r = runtime.main_quest
    r.trace_times = r.trace_times or {}
    local key = tostring(kind or "")
    local now = now_seconds()
    local interval = tonumber(min_interval) or 0
    if interval > 0 and now - (tonumber(r.trace_times[key]) or 0) < interval then
        return
    end
    r.trace_times[key] = now
    log_info("[AionMainQuest20590Trace] " .. key .. " " .. tostring(message or ""))
end

function move_trace_status(quest_id)
    if not ok_quest or not quest or type(quest.findById) ~= "function" then
        return ""
    end
    local ok, item = quest.findById(quest_id)
    if not ok or type(item) ~= "table" then
        return "nil"
    end
    return "status=" .. tostring(item.status_code or "") ..
        "/step=" .. tostring(item.req_count or "") ..
        "/tab=" .. tostring(item.tab or "")
end

function move_trace(source, target, detail, min_interval)
    runtime.move_trace = runtime.move_trace or {}
    local now = now_seconds()
    local key = tostring(source or "") .. ":" ..
        string.format("%.1f,%.1f,%.1f",
            tonumber(target and target.x) or 0,
            tonumber(target and target.y) or 0,
            tonumber(target and target.z) or 0)
    local interval = tonumber(min_interval)
    if interval == nil then
        interval = 0.2
    end
    if interval > 0 and now - (tonumber(runtime.move_trace[key]) or 0) < interval then
        return
    end
    runtime.move_trace[key] = now

    local char_text = "char=nil"
    if ok_core and core and type(core.getCharacter) == "function" then
        local char_ok, char = core.getCharacter()
        if char_ok and type(char) == "table" then
            char_text = "char=" .. tostring(char.name or "") ..
                " level=" .. tostring(char.level or "") ..
                " pos=" .. main_quest_position_text(char)
        end
    end

    local route_state = runtime.route or {}
    local combat_state = runtime.combat or {}
    local mq = runtime.main_quest or {}
    log_info("[AionMoveTrace] source=" .. tostring(source or "") ..
        " target=" .. main_quest_position_text(target) ..
        " " .. char_text ..
        " q20590=" .. move_trace_status(20590) ..
        " q20610=" .. move_trace_status(20610) ..
        " q24340=" .. move_trace_status(24340) ..
        " q24341=" .. move_trace_status(24341) ..
        " running=" .. tostring(runtime.running) ..
        " paused=" .. tostring(runtime.paused) ..
        " route_following=" .. tostring(route_state.following) ..
        " route=" .. tostring(route_state.follow_field or "") .. ":" .. tostring(route_state.index or "") ..
        " combat_mode=" .. tostring(combat_state.mode or "") ..
        " combat_status=" .. tostring(combat_state.status or "") ..
        " mq20590_done=" .. tostring(mq.completed_20590_reward) ..
        " mq20610_done=" .. tostring(mq.completed_20610_reward) ..
        " mq20611_active=" .. tostring(mq.active_20611_grind) ..
        " detail=" .. tostring(detail or ""))
end

function main_quest_delay_seconds()
    return math.max(0, tonumber(cfg.leveling and cfg.leveling.action_delay_seconds) or 0.5)
end

function main_quest_set_action_delay(reason)
    local delay = main_quest_delay_seconds()
    if delay <= 0 then
        return
    end
    runtime.main_quest = runtime.main_quest or {}
    local r = runtime.main_quest
    local now = now_seconds()
    r.action_delay_until = math.max(tonumber(r.action_delay_until) or 0, now + delay)
    r.action_delay_reason = tostring(reason or "")
end

function main_quest_action_waits_after_move(action_name)
    action_name = tostring(action_name or "")
    return action_name == "InteractNpc"
        or action_name == "ClickDialogX"
        or action_name == "ClickDialogXContinuous"
        or action_name == "ClickDialogXContinuousWaitTeleport"
        or action_name == "ClickDialogXWaitTeleport"
        or action_name == "ClickDialogXCompleteQuest"
        or action_name == "ClickDialogOkCompleteQuest"
        or action_name == "ClickObeliskConfirm"
        or action_name == "MapNodeTeleportByName"
        or action_name == "StartStationaryGrind"
end

function main_quest_is_move_action(action_name)
    action_name = tostring(action_name or "")
    return action_name == "NavigateToNpc"
        or action_name == "NavigateToGrindPoint"
        or action_name == "FinalMoveToNpc"
        or action_name == "FollowRoute"
        or action_name == "WaitRouteComplete"
end

function main_quest_wait_action_delay(action_name, stage)
    if not main_quest_action_waits_after_move(action_name) then
        return false
    end
    runtime.main_quest = runtime.main_quest or {}
    local r = runtime.main_quest
    local now = now_seconds()
    local until_at = tonumber(r.action_delay_until) or 0
    if now >= until_at then
        return false
    end
    main_quest_trace("action-delay:" .. tostring(stage or action_name),
        "next=" .. tostring(action_name or "") ..
        " stage=" .. tostring(stage or "") ..
        " remain=" .. string.format("%.2f", until_at - now) ..
        " reason=" .. tostring(r.action_delay_reason or ""),
        0.2)
    return true
end

function main_quest_combat_guard_char_hp(char)
    if type(char) ~= "table" then
        return nil
    end
    return tonumber(char.hp or char.HP or char.cur_hp or char.current_hp)
end

function main_quest_combat_guard_recent_damage(char, now)
    runtime.main_quest = runtime.main_quest or {}
    local r = runtime.main_quest
    now = tonumber(now) or now_seconds()
    local hp = main_quest_combat_guard_char_hp(char)
    if hp ~= nil and hp > 0 then
        local last_hp = tonumber(r.combat_guard_last_hp) or 0
        if last_hp > 0 and hp < last_hp then
            r.combat_guard_last_damage_at = now
        end
        r.combat_guard_last_hp = hp
    end
    local last_damage = tonumber(r.combat_guard_last_damage_at) or 0
    local window = math.max(0.5, tonumber(cfg.leveling and cfg.leveling.combat_guard_damage_window_seconds) or 3.0)
    return last_damage > 0 and now - last_damage <= window
end

function main_quest_combat_guard_entity_alive(e)
    if type(e) ~= "table" then
        return false
    end
    if (tonumber(e.type) or 0) ~= 2 then
        return false
    end
    if type(combat_target_end_reason) == "function"
        and combat_target_end_reason(e) ~= nil then
        return false
    end
    if e.dead == true then
        return false
    end
    local mhp = tonumber(e.mhp or e.max_hp) or 0
    local hp = tonumber(e.hp) or 0
    return mhp <= 0 or hp > 0
end

function main_quest_combat_guard_auto_on()
    if not ok_combat or not combat or type(combat.isAutoBattleOn) ~= "function" then
        return false
    end
    local ok, value = combat.isAutoBattleOn()
    return ok == true and value == true
end

function main_quest_combat_guard_current_entity(list)
    if not ok_combat or not combat or type(combat.currentTarget) ~= "function" then
        return nil
    end
    local ok, current = combat.currentTarget()
    if not ok or type(current) ~= "table" then
        return nil
    end
    local current_obj = tonumber(current.obj or current.IEntity or 0) or 0
    local current_id = tonumber(current.id or 0) or 0
    if current_obj <= 0 and current_id <= 0 then
        return nil
    end
    for _, e in ipairs(list or {}) do
        local obj = type(combat_entity_obj) == "function"
            and combat_entity_obj(e)
            or tonumber(e.obj or e.IEntity or 0) or 0
        local id = tonumber(e.id) or 0
        if (current_obj > 0 and obj == current_obj) or (current_id > 0 and id == current_id) then
            return e
        end
    end
    return nil
end

function main_quest_combat_guard_live_target(state, recent_damage)
    if not ok_entity or not entity or type(entity.list) ~= "function" then
        return false, "", nil
    end
    local ok, list = entity.list()
    if not ok then
        return false, "", nil
    end
    list = list or {}
    local c = runtime.combat or {}
    local tracked_obj = tonumber(c.target_obj) or 0
    if tracked_obj > 0 and type(combat_find_entity_by_obj) == "function" then
        local tracked = combat_find_entity_by_obj(list, tracked_obj)
        if main_quest_combat_guard_entity_alive(tracked) then
            return true, "tracked-target", tracked
        end
        if not tracked and type(combat_current_target_matches_obj) == "function"
            and combat_current_target_matches_obj(tracked_obj) then
            return true, "tracked-current", nil
        end
    end

    local current = main_quest_combat_guard_current_entity(list)
    if main_quest_combat_guard_entity_alive(current) then
        local hp = tonumber(current.hp) or 0
        local mhp = tonumber(current.mhp or current.max_hp) or 0
        local damaged_target = mhp > 0 and hp > 0 and hp < mhp
        if recent_damage == true or damaged_target or main_quest_combat_guard_auto_on() then
            return true, "current-target", current
        end
    end

    return false, "", nil
end

function main_quest_combat_guard_pending_loot(now)
    local c = runtime.combat or {}
    if (tonumber(c.loot_obj) or 0) > 0 then
        return true
    end
    local last_killed = tonumber(c.last_killed_obj) or 0
    local post_until = tonumber(c.post_kill_until) or 0
    return last_killed > 0 and post_until > (tonumber(now) or now_seconds())
end

function main_quest_start_combat_guard(action, state, reason, target)
    runtime.main_quest = runtime.main_quest or {}
    runtime.combat = runtime.combat or {}
    cfg.combat = cfg.combat or {}
    local r = runtime.main_quest
    local now = now_seconds()
    local ttl = math.max(0.5, tonumber(cfg.leveling and cfg.leveling.combat_guard_ttl_seconds) or 1.5)
    local char = type(state) == "table" and state.char or nil

    r.combat_guard_active = true
    r.combat_guard_until = now + ttl
    r.combat_guard_reason = tostring(reason or "")
    r.combat_guard_action = tostring(action and action.name or "")

    cfg.combat.mode = 1
    cfg.combat.enabled = true
    if type(char) == "table" then
        cfg.combat.anchor_enabled = true
        cfg.combat.anchor_x = tonumber(char.x) or 0
        cfg.combat.anchor_y = tonumber(char.y) or 0
        cfg.combat.anchor_z = tonumber(char.z) or 0
        runtime.combat.mode = "stationary"
        runtime.combat.anchor = {
            x = cfg.combat.anchor_x,
            y = cfg.combat.anchor_y,
            z = cfg.combat.anchor_z,
        }
        runtime.combat.anchor_distance = 0
    end
    if type(sync_combat_enabled_from_primary_mode) == "function" then
        sync_combat_enabled_from_primary_mode()
    end

    local target_text = ""
    if type(target) == "table" then
        local obj = type(combat_entity_obj) == "function" and combat_entity_obj(target) or 0
        target_text = " target=" .. tostring(target.name or "") ..
            " hp=" .. tostring(target.hp or "") .. "/" .. tostring(target.mhp or target.max_hp or "") ..
            " obj=" .. tostring(obj)
    end
    if main_quest_action_cooldown("combat-guard:" .. tostring(reason or "") .. ":" .. tostring(action and action.name or ""), 1.0) then
        main_quest_set_status("main quest waits for combat guard reason=" .. tostring(reason or "") ..
            " action=" .. tostring(action and action.name or ""))
        main_quest_trace("combat-guard",
            "block action=" .. tostring(action and action.name or "") ..
            " stage=" .. tostring(action and action.params and action.params.stage or "") ..
            " reason=" .. tostring(reason or "") ..
            target_text ..
            " pos=" .. main_quest_position_text(char),
            0)
    end
end

function main_quest_combat_guard_blocks_action(action, state)
    if not ok_main_quest_combat_guard or not main_quest_combat_guard
        or type(main_quest_combat_guard.shouldBlock) ~= "function" then
        return false
    end
    if not runtime.running or runtime.paused then
        return false
    end
    if primary_mode_ids[cfg.primary_mode] ~= "leveling" then
        return false
    end
    state = state or {}
    local now = now_seconds()
    local char = state.char
    if type(char) ~= "table" and ok_core and core and type(core.getCharacter) == "function" then
        local ok, current_char = core.getCharacter()
        if ok and type(current_char) == "table" then
            char = current_char
            state.char = char
        end
    end

    local recent_damage = main_quest_combat_guard_recent_damage(char, now)
    if type(main_quest_combat_guard.actionInterruptible) == "function"
        and not main_quest_combat_guard.actionInterruptible(action) then
        return false
    end
    local live_target, live_reason, target = main_quest_combat_guard_live_target(state, recent_damage)
    local pending_loot = main_quest_combat_guard_pending_loot(now)
    local block, reason = main_quest_combat_guard.shouldBlock({
        action = action,
        live_target = live_target,
        live_reason = live_reason,
        recent_damage = recent_damage,
        pending_loot = pending_loot,
    })
    if not block then
        if not recent_damage and not pending_loot then
            main_quest_clear_combat_guard(reason)
        end
        return false
    end

    main_quest_start_combat_guard(action, state, reason, target)
    return true
end

function main_quest_target_available(stage)
    if not ok_core or not core or type(core.getCharacter) ~= "function" then
        main_quest_trace("target-unavailable:" .. tostring(stage or ""),
            "skip main quest tick: aion.core unavailable",
            1.0)
        return false
    end
    local char_ok, char_or_err = core.getCharacter()
    if char_ok and type(char_or_err) == "table" then
        return true
    end
    main_quest_trace("target-unavailable:" .. tostring(stage or ""),
        "skip main quest tick err=" .. tostring(char_or_err),
        1.0)
    return false
end

function main_quest_reset_runtime(reason)
    runtime.main_quest = runtime.main_quest or {}
    local r = runtime.main_quest
    r.last_status = ""
    r.last_action = ""
    r.last_action_at = 0
    r.last_quest_read_at = 0
    r.cached_quest = nil
    r.cached_quest_20610 = nil
    r.last_quest_20610_read_at = 0
    r.cached_quest_20611 = nil
    r.last_quest_20611_read_at = 0
    r.waiting_teleport = false
    r.teleport_quest_id = 0
    r.teleport_stage = ""
    r.teleport_start_pos = nil
    r.teleport_start_big_map_id = 0
    r.last_nav_stage = ""
    r.last_nav_at = 0
    r.last_nav_distance = 0
    r.last_interact_stage = ""
    r.last_interact_at = 0
    r.wait_dialog_stage = ""
    r.wait_dialog_until = 0
    r.last_dialog_signature = ""
    r.last_decision_signature = ""
    r.last_decision_log_at = 0
    r.last_route_stop_stage = ""
    r.last_route_stop_reason = ""
    r.last_route_stop_at = 0
    r.action_delay_until = 0
    r.action_delay_reason = ""
    r.post_dialog_settle_until = 0
    r.current_action_name = ""
    r.current_action_stage = ""
    r.trace_times = {}
    r.completed_20590_first_teleport = false
    r.completed_20590_inner_final_move = false
    r.completed_20590_inner_teleport = false
    r.completed_20590_temple_teleport = false
    r.completed_20590_reward = false
    r.completed_20590_teleport = false
    r.completed_20610_start_dialog = false
    r.completed_20610_task_teleport = false
    r.completed_20610_reward = false
    r.clicked_20610_indicator_teleport = false
    r.clicked_20610_target_link = false
    r.clicked_20610_dictionary_teleport = false
    r.completed_20611_level_move = false
    r.level_move_quest_id = 0
    r.active_20611_grind = false
    r.active_20611_grind_stage = ""
    r.level_grind_quest_id = 0
    r.level_grind_required_level = 0
    r.quest_grind_authorized = false
    r.quest_grind_authorized_stage = ""
    r.quest_grind_authorized_quest_id = 0
    r.quest_grind_authorized_until = 0
    r.quest_grind_authorized_action = ""
    r.quest_grind_authorized_reason = ""
    r.quest_grind_authorized_clear_reason = ""
    r.combat_guard_active = false
    r.combat_guard_until = 0
    r.combat_guard_reason = ""
    r.combat_guard_action = ""
    r.combat_guard_last_hp = 0
    r.combat_guard_last_damage_at = 0
    r.completed_20611_grind = false
    r.completed_20611_mission_dialog = false
    r.opened_20611_obelisk = false
    r.opened_20611_obelisk_at = 0
    r.clicked_20611_obelisk_confirm = false
    r.clicked_20611_obelisk_confirm_at = 0
    r.obelisk_confirm_wait_until = 0
    r.completed_20611_obelisk = false
    r.clicked_20611_indicator_title = false
    r.clicked_20611_indicator_entry_name = ""
    r.clicked_20611_target_link = false
    r.clicked_20611_dictionary_teleport = false
    r.completed_20611_target_teleport = false
    r.completed_20611_target_dialog = false
    r.completed_20611_hotspot_teleport = false
    r.completed_20611_hotspot_reward = false
    r.reached_20612_start_point = false
    r.completed_20612_start_dialog = false
    r.completed_20612_task_teleport = false
    r.completed_20612_reward_dialog = false
    r.quest_teleport_panel_key = ""
    r.quest_teleport_panel_opened_at = 0
    log_info("[AionMainQuest20590] reset reason=" .. tostring(reason or ""))
end

function main_quest_cached_20590(now)
    runtime.main_quest = runtime.main_quest or {}
    local r = runtime.main_quest

    if type(r.cached_quest) == "table"
        and now - (tonumber(r.last_quest_read_at) or 0) < 3.0 then
        return r.cached_quest
    end

    if ok_quest and quest and type(quest.findById) == "function" then
        local ok, item = quest.findById(20590)
        r.last_quest_read_at = now
        if ok then
            r.cached_quest = item
            return item
        end
    end

    for _, item in ipairs((runtime.bootstrap and runtime.bootstrap.quests) or {}) do
        if tonumber(item.id) == 20590 then
            r.cached_quest = item
            r.last_quest_read_at = now
            return item
        end
    end
    return r.cached_quest
end

function main_quest_cached_20610(now)
    runtime.main_quest = runtime.main_quest or {}
    local r = runtime.main_quest

    if type(r.cached_quest_20610) == "table"
        and now - (tonumber(r.last_quest_20610_read_at) or 0) < 3.0 then
        return r.cached_quest_20610
    end

    if ok_quest and quest and type(quest.findById) == "function" then
        local ok, item = quest.findById(20610)
        r.last_quest_20610_read_at = now
        if ok then
            r.cached_quest_20610 = item
            return item
        end
    end

    for _, item in ipairs((runtime.bootstrap and runtime.bootstrap.quests) or {}) do
        if tonumber(item.id) == 20610 then
            r.cached_quest_20610 = item
            r.last_quest_20610_read_at = now
            return item
        end
    end
    return r.cached_quest_20610
end

function main_quest_cached_20611(now)
    runtime.main_quest = runtime.main_quest or {}
    local r = runtime.main_quest

    if type(r.cached_quest_20611) == "table"
        and now - (tonumber(r.last_quest_20611_read_at) or 0) < 3.0 then
        return r.cached_quest_20611
    end

    if ok_quest and quest and type(quest.findById) == "function" then
        local ok, item = quest.findById(20611)
        r.last_quest_20611_read_at = now
        if ok then
            r.cached_quest_20611 = item
            return item
        end
    end

    for _, item in ipairs((runtime.bootstrap and runtime.bootstrap.quests) or {}) do
        if tonumber(item.id) == 20611 then
            r.cached_quest_20611 = item
            r.last_quest_20611_read_at = now
            return item
        end
    end
    return r.cached_quest_20611
end

function main_quest_20590_reward_dialog_open()
    if not ok_main_quest_20590 or not main_quest_20590
        or type(main_quest_20590.isRewardDialog) ~= "function" then
        return false
    end
    local ready, npc_runtime = npc_dialog_prepare_runtime()
    if not ready or not npc_runtime or type(npc_runtime.dialog) ~= "function" then
        return false
    end
    local ok, info = npc_runtime.dialog()
    if ok and main_quest_20590.isRewardDialog(info) then
        return true
    end
    return false
end

function main_quest_later_tasks_blocked(now, target_stage)
    if not ok_core or not core or type(core.getCharacter) ~= "function" then
        return false, "character unavailable"
    end
    local char_ok, char = core.getCharacter()
    if char_ok and type(char) == "table" then
        local level = tonumber(char.level) or 0
        runtime.audit = runtime.audit or {}
        runtime.audit.current = runtime.audit.current or {}
        runtime.audit.current.name = tostring(char.name or "")
        runtime.audit.current.level = level
    end

    local quest_20590 = main_quest_cached_20590(now or now_seconds())
    if tonumber(quest_20590 and quest_20590.status_code) == 3 then
        return true, "quest20590-active"
    end
    if main_quest_20590_reward_dialog_open() then
        return true, "quest20590-reward-dialog"
    end
    if tostring(target_stage or "") == "20611"
        and runtime.main_quest
        and runtime.main_quest.completed_20610_reward ~= true
        and ok_main_quest_20610 and main_quest_20610 then
        local quest_20610 = main_quest_cached_20610(now or now_seconds())
        if main_quest_20610.isQuestKnown(quest_20610)
            and (main_quest_20610.isQuestActive(quest_20610)
                or main_quest_20610.isQuestDone(quest_20610)) then
            return true, "quest20610-unfinished"
        end
    end
    return false, ""
end

function main_quest_read_20590_state(now)
    local state = {}
    local char_ok, char = core.getCharacter()
    if char_ok then
        state.char = char
    end

    if ok_map and map and type(map.bigMapId) == "function" then
        local map_ok, big_map_id = map.bigMapId()
        if map_ok then
            state.big_map_id = big_map_id
        end
    end

    local ready, npc_runtime = npc_dialog_prepare_runtime()
    if ready and npc_runtime then
        local dialog_ok, info = npc_runtime.dialog()
        if dialog_ok then
            state.dialog = info
        end
    end

    state.quest = main_quest_cached_20590(now)
    return state
end

function main_quest_ui_child_visible(child)
    return type(child) == "table"
        and child.visible == true
        and (tonumber(child.obj or child.addr) or 0) ~= 0
end

function main_quest_find_ui_child_by_name(children, wanted_name)
    wanted_name = tostring(wanted_name or "")
    for _, child in ipairs(children or {}) do
        if main_quest_ui_child_visible(child)
            and tostring(child.name or "") == wanted_name then
            return child
        end
    end
    return nil
end

function main_quest_find_ui_child_at(children, x, y, tolerance)
    x = tonumber(x) or 0
    y = tonumber(y) or 0
    tolerance = math.max(1, tonumber(tolerance) or 20)
    local best = nil
    local best_dist = math.huge
    for _, child in ipairs(children or {}) do
        if main_quest_ui_child_visible(child) then
            local cx = tonumber(child.x)
            local cy = tonumber(child.y)
            if cx and cy then
                local dx = cx - x
                local dy = cy - y
                local dist = math.sqrt(dx * dx + dy * dy)
                if dist <= tolerance and dist < best_dist then
                    best = child
                    best_dist = dist
                end
            end
        end
    end
    return best, best_dist
end

function main_quest_read_20610_ui_state()
    local out = {}
    local ok_ui_runtime, ui_runtime = pcall(require, "aion.ui")
    if not ok_ui_runtime or not ui_runtime or type(ui_runtime.children) ~= "function" then
        out.err = "aion.ui unavailable " .. tostring(ui_runtime)
        return out
    end

    local ok_indicator, indicator_children = ui_runtime.children("quest_indicator_dialog", 6)
    if ok_indicator then
        out.quest_indicator_teleport = main_quest_find_ui_child_by_name(indicator_children, "teleport") ~= nil
    end

    local ok_quest, quest_children = ui_runtime.children("v3_quest_dialog", 6)
    if ok_quest then
        out.quest_detail_target_link = main_quest_find_ui_child_at(quest_children, 424, 254, 25) ~= nil
    end

    local ok_dictionary, dictionary_children = ui_runtime.children("dictionary_dialog", 6)
    if ok_dictionary then
        out.dictionary_teleport_to_npc = main_quest_find_ui_child_by_name(dictionary_children, "teleport_to_npc") ~= nil
    end

    return out
end

function main_quest_read_20610_state(now)
    local state = {}
    local r = runtime.main_quest or {}
    local post_dialog_settle_until = tonumber(r.post_dialog_settle_until) or 0
    local settling_after_dialog = post_dialog_settle_until > 0 and (tonumber(now) or now_seconds()) < post_dialog_settle_until
    local char_ok, char = core.getCharacter()
    if char_ok then
        state.char = char
    end

    if ok_map and map and type(map.bigMapId) == "function" then
        local map_ok, big_map_id = map.bigMapId()
        if map_ok then
            state.big_map_id = big_map_id
        end
    end

    if not settling_after_dialog then
        local ready, npc_runtime = npc_dialog_prepare_runtime()
        if ready and npc_runtime then
            local dialog_ok, info = npc_runtime.dialog()
            if dialog_ok then
                state.dialog = info
            end
        end
    end

    if settling_after_dialog then
        state.quest = r.cached_quest_20610
        state.ui = { post_dialog_settle = true }
    else
        state.quest = main_quest_cached_20610(now)
        state.ui = main_quest_read_20610_ui_state()
    end
    return state
end

function main_quest_read_20611_state(now)
    local state = {}
    local char_ok, char = core.getCharacter()
    if char_ok then
        state.char = char
    end

    if ok_map and map and type(map.bigMapId) == "function" then
        local map_ok, big_map_id = map.bigMapId()
        if map_ok then
            state.big_map_id = big_map_id
        end
    end

    local ready, npc_runtime = npc_dialog_prepare_runtime()
    if ready and npc_runtime then
        local dialog_ok, info = npc_runtime.dialog()
        if dialog_ok then
            state.dialog = info
        end
    end

    if ok_quest and quest and type(quest.list) == "function" then
        local list_ok, list = quest.list()
        if list_ok and type(list) == "table" then
            state.quests = list
            if ok_main_quest_20611 and main_quest_20611 and type(main_quest_20611.findQuest) == "function" then
                state.quest = main_quest_20611.findQuest(list)
            end
            if ok_main_quest_20611 and main_quest_20611 and type(main_quest_20611.findRemoteRewardQuest) == "function" then
                state.remote_reward_quest = main_quest_20611.findRemoteRewardQuest(list)
            end
            if ok_main_quest_20611 and main_quest_20611 and type(main_quest_20611.findLevelBlockedQuest) == "function" then
                state.level_blocked_quest = main_quest_20611.findLevelBlockedQuest(list)
            end
        end
    end
    if type(state.quest) ~= "table" then
        state.quest = main_quest_cached_20611(now)
    end
    local quest_id = tonumber(state.quest and state.quest.id) or 0
    local quest_step = tonumber(state.quest and state.quest.req_count) or 0
    local r = runtime.main_quest or {}
    local level_blocked_id = tonumber(state.level_blocked_quest and state.level_blocked_quest.id) or 0
    if quest_id == 20611
        or quest_id == 20612
        or level_blocked_id == 20611
        or level_blocked_id == 20612
        or r.completed_20612_start_dialog == true
        or r.active_20611_grind == true
        or r.opened_20611_obelisk == true then
        state.ui = main_quest_read_20611_ui_state()
    end
    return state
end

function main_quest_is_20611_blue_task_id(id)
    if ok_main_quest_20611 and main_quest_20611 and type(main_quest_20611.isRemoteRewardQuestId) == "function" then
        return main_quest_20611.isRemoteRewardQuestId(id)
    end
    local qid = tonumber(id) or 0
    return qid == 24340 or qid == 24341
end

function main_quest_stop_20611_grind(reason, mark_completed)
    runtime.main_quest = runtime.main_quest or {}
    local r = runtime.main_quest
    r.active_20611_grind = false
    r.active_20611_grind_stage = ""
    r.level_grind_quest_id = 0
    r.level_grind_required_level = 0
    if type(main_quest_clear_grind_authorization) == "function" then
        main_quest_clear_grind_authorization(reason or "quest-20611-grind-stop")
    end
    if mark_completed == true then
        r.completed_20611_grind = true
    end
    r.cached_quest_20611 = nil
    r.last_quest_20611_read_at = 0
    runtime.combat = runtime.combat or {}
    runtime.combat.target_obj = 0
    runtime.combat.target_name = ""
    runtime.combat.target_distance = 0
    if type(combat_auto_off) == "function" then
        combat_auto_off(tostring(reason or "quest-20611-grind-stop"), true)
    end
    if type(sync_combat_enabled_from_primary_mode) == "function" then
        sync_combat_enabled_from_primary_mode()
    end
end

function main_quest_read_startup_snapshot()
    local snapshot = {
        char = nil,
        big_map_id = 0,
        quests = {},
        dialog = nil,
    }

    if ok_core and core and type(core.getCharacter) == "function" then
        local char_ok, char = core.getCharacter()
        if char_ok and type(char) == "table" then
            snapshot.char = char
            runtime.audit = runtime.audit or {}
            runtime.audit.current = runtime.audit.current or {}
            runtime.audit.current.name = tostring(char.name or "")
            runtime.audit.current.level = tonumber(char.level) or 0
        end
    end

    if ok_map and map then
        if type(map.bigMapId) == "function" then
            local map_ok, big_map_id = map.bigMapId()
            if map_ok then
                snapshot.big_map_id = tonumber(big_map_id) or 0
            end
        end
        if type(map.current) == "function" then
            local cur_ok, cur_map = map.current()
            if cur_ok and type(cur_map) == "table" then
                snapshot.map = cur_map
                runtime.bootstrap = runtime.bootstrap or {}
                runtime.bootstrap.map_name = cur_map.region or cur_map.name_cn or cur_map.name_en or ""
                runtime.audit.current.map = runtime.bootstrap.map_name
            end
        end
    end

    if ok_quest and quest and type(quest.list) == "function" then
        local quest_ok, quests = quest.list()
        if quest_ok and type(quests) == "table" then
            snapshot.quests = quests
            runtime.bootstrap = runtime.bootstrap or {}
            runtime.bootstrap.quests = quests
            runtime.bootstrap.quest_count = count_array(quests)
            runtime.audit.current.quests = runtime.bootstrap.quest_count
        end
    end

    local ready, npc_runtime = npc_dialog_prepare_runtime()
    if ready and npc_runtime and type(npc_runtime.dialog) == "function" then
        local dialog_ok, info = npc_runtime.dialog()
        if dialog_ok and type(info) == "table" then
            snapshot.dialog = info
        end
    end

    return snapshot
end

function main_quest_apply_startup_snapshot(reason)
    if not ok_main_quest_resume or not main_quest_resume or type(main_quest_resume.plan) ~= "function" then
        main_quest_trace("startup-snapshot",
            "skip reason=" .. tostring(reason or "") .. " resume_module_unavailable=" .. tostring(main_quest_resume),
            0)
        return false
    end

    local snapshot = main_quest_read_startup_snapshot()
    local plan = main_quest_resume.plan(snapshot)
    local flags = type(plan.flags) == "table" and plan.flags or {}
    local stopped_route_stage = ""
    if type(main_quest_active_route_stage) == "function" then
        stopped_route_stage = main_quest_active_route_stage()
    end
    if stopped_route_stage ~= "" and type(main_quest_stop_route) == "function" then
        main_quest_stop_route(stopped_route_stage, "startup-snapshot:" .. tostring(plan.stage or ""))
    end
    runtime.main_quest = runtime.main_quest or {}
    local r = runtime.main_quest
    for key, value in pairs(flags) do
        r[key] = value
    end
    r.startup_snapshot_stage = tostring(plan.stage or "")
    r.startup_snapshot_reason = tostring(plan.reason or "")
    r.startup_snapshot_character = tostring(plan.character_name or "")
    r.startup_snapshot_level = tonumber(plan.level) or 0
    r.startup_snapshot_big_map_id = tonumber(plan.big_map_id) or 0
    r.cached_quest = nil
    r.cached_quest_20610 = nil
    r.cached_quest_20611 = nil
    r.last_quest_read_at = 0
    r.last_quest_20610_read_at = 0
    r.last_quest_20611_read_at = 0

    local flag_text = ""
    for key, value in pairs(flags) do
        flag_text = flag_text .. tostring(key) .. "=" .. tostring(value) .. " "
    end
    main_quest_trace("startup-snapshot",
        "reason=" .. tostring(reason or "") ..
        " stage=" .. tostring(plan.stage or "") ..
        " why=" .. tostring(plan.reason or "") ..
        " char=" .. tostring(plan.character_name or "") ..
        " level=" .. tostring(plan.level or "") ..
        " map=" .. tostring(plan.big_map_id or "") ..
        " quests=" .. tostring(plan.quest_count or 0) ..
        " q20590=" .. tostring(plan.quest_20590_status or 0) ..
        " q20610=" .. tostring(plan.quest_20610_status or 0) ..
        " q20611=" .. tostring(plan.quest_20611_status or 0) ..
        " q20611_step=" .. tostring(plan.quest_20611_step or 0) ..
        " q20612=" .. tostring(plan.quest_20612_status or 0) ..
        " q20612_step=" .. tostring(plan.quest_20612_step or 0) ..
        " qlevel_id=" .. tostring(plan.level_blocked_quest_id or 0) ..
        " qlevel=" .. tostring(plan.level_blocked_status or 0) ..
        " qblue_id=" .. tostring(plan.remote_reward_quest_id or 0) ..
        " qblue=" .. tostring(plan.remote_reward_status or 0) ..
        " stopped_route_stage=" .. tostring(stopped_route_stage or "") ..
        " build=mq-20611-hotspot-map-teleport-20260611" ..
        " flags=" .. flag_text,
        0)
    main_quest_set_status("startup snapshot stage=" .. tostring(plan.stage or "") ..
        " char=" .. tostring(plan.character_name or "") ..
        " level=" .. tostring(plan.level or ""))
    return true
end

function main_quest_action_cooldown(action_name, seconds)
    runtime.main_quest = runtime.main_quest or {}
    local r = runtime.main_quest
    local now = now_seconds()
    if r.last_action == action_name and now - (tonumber(r.last_action_at) or 0) < seconds then
        return false
    end
    r.last_action = action_name
    r.last_action_at = now
    return true
end

function main_quest_ui_obj_visible(obj)
    if type(obj) ~= "table" then
        return false
    end
    if obj.visible == false then
        return false
    end
    return obj.visible == true
        or (tonumber(obj.addr or obj.obj) or 0) ~= 0
end

function main_quest_quest_panel_visible()
    local ok_ui_runtime, ui_runtime = pcall(require, "aion.ui")
    if not ok_ui_runtime or not ui_runtime or type(ui_runtime.find) ~= "function" then
        return false, "aion.ui unavailable " .. tostring(ui_runtime)
    end

    local find_ok, obj, find_err = ui_runtime.find("v3_quest_dialog")
    if not find_ok then
        return false, tostring(find_err or "FindUIObj failed")
    end
    if main_quest_ui_obj_visible(obj) then
        return true, "v3_quest_dialog visible"
    end
    return false, "v3_quest_dialog not visible"
end

function main_quest_prepare_quest_teleport_panel(quest_id, stage)
    local visible, detail = main_quest_quest_panel_visible()
    if visible then
        return true, detail
    end

    runtime.main_quest = runtime.main_quest or {}
    local r = runtime.main_quest
    local key = tostring(stage or "") .. ":" .. tostring(quest_id or "")
    local now = now_seconds()
    local last_key = tostring(r.quest_teleport_panel_key or "")
    local last_opened_at = tonumber(r.quest_teleport_panel_opened_at) or 0
    local settle_seconds = math.max(0.5, tonumber(cfg.leveling and cfg.leveling.quest_panel_settle_seconds) or 1.0)
    if last_key == key and now - last_opened_at < settle_seconds then
        main_quest_set_status("waiting quest panel before teleport quest_id=" .. tostring(quest_id or "") ..
            " stage=" .. tostring(stage or "") ..
            " remain=" .. string.format("%.1f", settle_seconds - (now - last_opened_at)) ..
            " detail=" .. tostring(detail or ""))
        return false, "waiting quest panel"
    end

    if not ok_remote or not remote or type(remote.pressKey) ~= "function" then
        return false, "aion.remote.pressKey unavailable " .. tostring(remote)
    end

    local press_ok, pressed, press_err = remote.pressKey(0x4A)
    r.quest_teleport_panel_key = key
    r.quest_teleport_panel_opened_at = now
    main_quest_trace("quest-panel-open:" .. tostring(stage or ""),
        "quest=" .. tostring(quest_id or "") ..
        " key=J" ..
        " ok=" .. tostring(press_ok) ..
        " result=" .. tostring(pressed) ..
        " err=" .. tostring(press_err or "") ..
        " visible_detail=" .. tostring(detail or ""),
        0)
    if not press_ok or pressed == false then
        return false, "press J failed: " .. tostring(press_err or pressed or "")
    end
    main_quest_set_status("opening quest panel before teleport quest_id=" .. tostring(quest_id or "") ..
        " stage=" .. tostring(stage or ""))
    return false, "opening quest panel"
end

function main_quest_active_route_stage()
    local rt = runtime.route
    if type(rt) ~= "table" or rt.following ~= true then
        return ""
    end
    local field = tostring(rt.follow_field or "")
    if string.sub(field, 1, #"main_quest_20590:") ~= "main_quest_20590:" then
        return ""
    end
    return tostring(rt.main_quest_stage or string.sub(field, string.len("main_quest_20590:") + 1))
end

function main_quest_stop_route(stage, reason)
    local rt = runtime.route
    if type(rt) ~= "table" or rt.following ~= true then
        return
    end
    if stage and stage ~= "" and tostring(rt.main_quest_stage or "") ~= tostring(stage) then
        return
    end
    runtime.main_quest = runtime.main_quest or {}
    runtime.main_quest.last_route_stop_stage = tostring(rt.main_quest_stage or stage or "")
    runtime.main_quest.last_route_stop_reason = tostring(reason or "")
    runtime.main_quest.last_route_stop_at = now_seconds()
    rt.following = false
    rt.follow_field = nil
    rt.follow_name = ""
    rt.points = {}
    rt.index = 1
    rt.direction = 1
    rt.moving_to = nil
    rt.last_move_at = 0
    rt.status = "主线路径停止"
    rt.attach_runtime = false
    rt.finish_shows_ui = false
    rt.loop = nil
    rt.reverse_on_end = nil
    rt.test_only = false
    rt.main_quest_stage = ""
    log_info("[AionMainQuest20590] route stop stage=" .. tostring(stage or "") ..
        " reason=" .. tostring(reason or ""))
end

function main_quest_start_route(action, state)
    local params = type(action.params) == "table" and action.params or {}
    local points = type(params.route_points) == "table" and params.route_points or {}
    if #points <= 0 then
        main_quest_set_status("主线路径失败: route_points 为空")
        return false
    end
    if not ok_nav or not nav or type(nav.moveTo) ~= "function" then
        main_quest_set_status("主线路径失败: aion.nav 不可用")
        return false
    end

    local stage = tostring(params.stage or "")
    local field = "main_quest_20590:" .. stage
    local rt = runtime.route
    if rt.following == true and tostring(rt.follow_field or "") == field then
        return true
    end

    main_quest_stop_route(nil, "switch-main-quest-route")

    local start_index = math.max(1, math.min(#points, tonumber(params.route_index) or 1))
    local start_dist = tonumber(params.nearest_route_distance) or 0
    if route_nearest_point then
        local pos = state and state.char
        if not pos and route_current_position then
            local pos_ok, current_pos = route_current_position()
            if pos_ok then
                pos = current_pos
            end
        end
        if pos then
            start_index, start_dist = route_nearest_point(points, pos)
        end
    end

    rt.following = true
    rt.follow_field = field
    rt.follow_name = tostring(params.route_name or field)
    rt.points = points
    rt.index = start_index
    rt.direction = 1
    rt.moving_to = nil
    rt.last_move_at = 0
    rt.laps = 0
    rt.status = "主线路径移动"
    rt.error = ""
    rt.attach_runtime = false
    rt.finish_shows_ui = false
    rt.test_only = false
    rt.loop = false
    rt.reverse_on_end = false
    rt.main_quest_stage = stage
    if stage == "inner_npc" then
        runtime.main_quest.completed_20590_inner_final_move = false
    end
    main_quest_set_status(string.format(
        "开始主线路径 stage=%s route=%s index=%d/%d nearest=%.1f",
        stage,
        rt.follow_name,
        start_index,
        #points,
        start_dist))
    return true
end

function main_quest_click_ui_control_by_name(ui_runtime, parent, wanted_name, depth)
    local child_ok, children, child_err = ui_runtime.children(parent, math.max(1, tonumber(depth) or 6))
    if not child_ok then
        return false, "read children failed parent=" .. tostring(parent) .. " err=" .. tostring(child_err)
    end
    local child = main_quest_find_ui_child_by_name(children, wanted_name)
    if not child then
        return false, "ui child not found parent=" .. tostring(parent) .. " name=" .. tostring(wanted_name)
    end
    local obj = child.obj or child.addr
    local click_ok, clicked, click_err = ui_runtime.click(obj)
    if not click_ok or clicked == false then
        return false, "click failed parent=" .. tostring(parent) ..
            " name=" .. tostring(wanted_name) ..
            " obj=" .. tostring(obj) ..
            " err=" .. tostring(click_err or clicked)
    end
    return true, string.format("clicked parent=%s name=%s obj=%s x=%.0f y=%.0f",
        tostring(parent),
        tostring(wanted_name),
        tostring(obj),
        tonumber(child.x) or 0,
        tonumber(child.y) or 0)
end

function main_quest_click_ui_control_at(ui_runtime, parent, x, y, tolerance, depth)
    local child_ok, children, child_err = ui_runtime.children(parent, math.max(1, tonumber(depth) or 6))
    if not child_ok then
        return false, "read children failed parent=" .. tostring(parent) .. " err=" .. tostring(child_err)
    end
    local child, dist = main_quest_find_ui_child_at(children, x, y, tolerance)
    if not child then
        return false, "ui child not found parent=" .. tostring(parent) ..
            " x=" .. tostring(x) ..
            " y=" .. tostring(y) ..
            " tolerance=" .. tostring(tolerance)
    end
    local obj = child.obj or child.addr
    local click_ok, clicked, click_err = ui_runtime.click(obj)
    if not click_ok or clicked == false then
        return false, "click failed parent=" .. tostring(parent) ..
            " obj=" .. tostring(obj) ..
            " err=" .. tostring(click_err or clicked)
    end
    return true, string.format("clicked parent=%s obj=%s name=%s x=%.0f y=%.0f dist=%.1f",
        tostring(parent),
        tostring(obj),
        tostring(child.name or ""),
        tonumber(child.x) or 0,
        tonumber(child.y) or 0,
        tonumber(dist) or 0)
end

function main_quest_obelisk_confirm_button_names()
    return {
        "yes",
        "ok",
        "accept",
        "confirm",
        "apply",
        "btn_yes",
        "button_yes",
        "dialog_yes",
        "confirm_yes",
        "agreement_yes",
    }
end

function main_quest_lower_text(value)
    return string.lower(tostring(value or ""))
end

function main_quest_obelisk_button_name_matches(name)
    local lower = main_quest_lower_text(name)
    if lower == "" or lower == "(no-name)" then
        return false
    end
    for _, candidate in ipairs(main_quest_obelisk_confirm_button_names()) do
        if lower == candidate then
            return true
        end
    end
    if string.find(lower, "yes", 1, true) and not string.find(lower, "no", 1, true) then
        return true
    end
    if string.find(lower, "ok", 1, true) then
        return true
    end
    if string.find(lower, "confirm", 1, true) then
        return true
    end
    return false
end

function main_quest_obelisk_confirm_excluded(ctrl)
    local name = main_quest_lower_text(ctrl and ctrl.name)
    local parent = main_quest_lower_text(ctrl and ctrl.parent)
    local text = name .. " " .. parent
    local excluded = {
        "move_state_dialog",
        "static_forward",
        "static_backward",
        "static_leftward",
        "static_rightward",
        "chat_dialog",
        "chat_option",
        "chat_tab",
        "quickbar",
        "minimap",
    }
    for _, token in ipairs(excluded) do
        if string.find(text, token, 1, true) then
            return true
        end
    end
    return false
end

function main_quest_obelisk_coord_candidate(ctrl)
    if main_quest_obelisk_confirm_excluded(ctrl) then
        return false
    end
    local name = main_quest_lower_text(ctrl and ctrl.name)
    local parent = main_quest_lower_text(ctrl and ctrl.parent)
    if main_quest_obelisk_button_name_matches(name) then
        return true
    end
    if string.find(name, "button", 1, true) or string.find(name, "btn", 1, true) then
        return true
    end
    if string.find(parent, "dialog", 1, true)
        or string.find(parent, "popup", 1, true)
        or string.find(parent, "message", 1, true)
        or string.find(parent, "confirm", 1, true) then
        return true
    end
    return false
end

function main_quest_obelisk_confirm_line(ctrl, reason, dist)
    return string.format("%s obj=%s name=%s parent=%s x=%.0f y=%.0f dist=%.1f",
        tostring(reason or "obelisk-confirm"),
        tostring(ctrl and (ctrl.obj or ctrl.addr) or ""),
        tostring(ctrl and ctrl.name or ""),
        tostring(ctrl and ctrl.parent or ""),
        tonumber(ctrl and ctrl.x) or 0,
        tonumber(ctrl and ctrl.y) or 0,
        tonumber(dist) or 0)
end

function main_quest_visible_popup_roots(ui_runtime, params)
    params = type(params) == "table" and params or {}
    if type(ui_runtime.list) ~= "function" then
        return {}, "aion.ui list unavailable"
    end
    local list_ok, list, list_err = ui_runtime.list(true)
    if not list_ok then
        return {}, "ui list failed: " .. tostring(list_err)
    end

    local roots = {}
    for _, ctrl in ipairs(list or {}) do
        local obj = tonumber(ctrl and (ctrl.obj or ctrl.addr)) or 0
        local name = main_quest_lower_text(ctrl and ctrl.name)
        local layer = tonumber(ctrl and ctrl.layer) or 0
        local x = tonumber(ctrl and ctrl.x) or 0
        local y = tonumber(ctrl and ctrl.y) or 0
        local unnamed = name == "" or name == "(no-name)"
        if obj > 0 and main_quest_ui_obj_visible(ctrl)
            and layer >= 2
            and unnamed
            and x >= 250 and x <= 850
            and y >= 120 and y <= 520 then
            roots[#roots + 1] = ctrl
        end
    end
    return roots, nil
end

function main_quest_obelisk_popup_root_visible(ui_runtime, params)
    params = type(params) == "table" and params or {}
    local roots, root_err = main_quest_visible_popup_roots(ui_runtime, params)
    if #roots <= 0 then
        return false, tostring(root_err or "popup root not visible")
    end

    for _, ctrl in ipairs(roots) do
        local x = tonumber(ctrl and ctrl.x) or 0
        local y = tonumber(ctrl and ctrl.y) or 0
        if x >= 480 and x <= 540
            and y >= 280 and y <= 330 then
            return true, main_quest_obelisk_confirm_line(ctrl, "popup-root", 0)
        end
    end

    return false, "popup root not visible"
end

function main_quest_find_obelisk_common_alert_button(ui_runtime, params)
    params = type(params) == "table" and params or {}
    if params.allow_common_alert == false then
        return nil, "common_alert disabled"
    end

    local require_root = params.require_popup_root ~= false
    local root_line = ""
    if require_root then
        local root_visible, root_detail = main_quest_obelisk_popup_root_visible(ui_runtime, params)
        root_line = tostring(root_detail or "")
        if not root_visible then
            return nil, "common_alert root not visible: " .. root_line
        end
    end

    if type(ui_runtime.children) ~= "function" then
        return nil, "aion.ui children unavailable"
    end
    local child_ok, children, child_err = ui_runtime.children("common_alert_dialog", 6)
    if not child_ok then
        return nil, "common_alert children failed: " .. tostring(child_err)
    end

    local cancels = {}
    for index, child in ipairs(children or {}) do
        local name = main_quest_lower_text(child and child.name)
        if main_quest_ui_child_visible(child) and name == "cancel" then
            cancels[#cancels + 1] = {
                index = index,
                child = child,
            }
        end
    end

    if #cancels >= 2 then
        local picked = cancels[1]
        local child = picked.child
        local line = main_quest_obelisk_confirm_line(child,
            "common_alert_dialog.cancel[" .. tostring(picked.index) .. "]",
            0)
        if root_line ~= "" then
            line = line .. " root=" .. root_line
        end
        return child, line
    end

    return nil, "common_alert confirm cancel pair not found count=" .. tostring(#cancels)
end

function main_quest_score_obelisk_child(child)
    if not main_quest_ui_child_visible(child) then
        return nil
    end
    local name = main_quest_lower_text(child and child.name)
    local score = 0
    if main_quest_obelisk_button_name_matches(name) then
        score = score + 120
    end
    if name == "resurrect_ok" then
        score = score + 120
    end
    if string.find(name, "ok", 1, true)
        or string.find(name, "yes", 1, true)
        or string.find(name, "accept", 1, true)
        or string.find(name, "confirm", 1, true) then
        score = score + 80
    end
    if name == "cancel" then
        score = score + 20
    end
    if string.find(name, "no", 1, true)
        or string.find(name, "refuse", 1, true)
        or string.find(name, "close", 1, true) then
        score = score - 120
    end
    if score <= 0 then
        return nil
    end
    return score
end

function main_quest_find_obelisk_popup_root_child(ui_runtime, params)
    params = type(params) == "table" and params or {}
    local roots, root_err = main_quest_visible_popup_roots(ui_runtime, params)
    if #roots <= 0 then
        return nil, tostring(root_err or "no visible popup root")
    end
    if type(ui_runtime.children) ~= "function" then
        return nil, "aion.ui children unavailable"
    end

    local best = nil
    local best_root = nil
    local best_score = nil
    local best_index = 0
    local details = {}

    for _, root in ipairs(roots) do
        local root_obj = root.obj or root.addr
        local child_ok, children, child_err = ui_runtime.children(root_obj, 8)
        if child_ok then
            details[#details + 1] = "root=" .. tostring(root_obj) ..
                " children=" .. tostring(#(children or {}))
            for index, child in ipairs(children or {}) do
                local score = main_quest_score_obelisk_child(child)
                if score and (not best_score or score > best_score) then
                    best = child
                    best_root = root
                    best_score = score
                    best_index = index
                end
            end
        else
            details[#details + 1] = "root=" .. tostring(root_obj) ..
                " children_failed=" .. tostring(child_err)
        end
    end

    if best then
        return best, main_quest_obelisk_confirm_line(best,
            "popup-root-child[" .. tostring(best_index) .. "]" ..
            " root=" .. tostring(best_root and (best_root.obj or best_root.addr) or "") ..
            " score=" .. tostring(best_score),
            0)
    end

    if params.allow_popup_root_click == true and #roots == 1 then
        local root = roots[1]
        return root, main_quest_obelisk_confirm_line(root, "popup-root-direct", 0)
    end

    return nil, "popup root child not found; " .. table.concat(details, "; ")
end

function main_quest_find_obelisk_confirm_button(ui_runtime, params)
    params = type(params) == "table" and params or {}
    local target_x = tonumber(params.confirm_x or params.x) or 684
    local target_y = tonumber(params.confirm_y or params.y) or 437
    local tolerance = math.max(1, tonumber(params.confirm_tolerance or params.tolerance) or 90)

    if type(ui_runtime.find) == "function" then
        for _, name in ipairs(main_quest_obelisk_confirm_button_names()) do
            local find_ok, ctrl = ui_runtime.find(name)
            if find_ok and main_quest_ui_obj_visible(ctrl)
                and not main_quest_obelisk_confirm_excluded(ctrl) then
                return ctrl, main_quest_obelisk_confirm_line(ctrl, "find-name:" .. name, 0)
            end
        end
    end

    local root_child, root_child_line = main_quest_find_obelisk_popup_root_child(ui_runtime, params)
    if root_child then
        return root_child, root_child_line
    end

    local alert_ctrl, alert_line = main_quest_find_obelisk_common_alert_button(ui_runtime, params)
    if alert_ctrl then
        return alert_ctrl, alert_line
    end

    if type(ui_runtime.list) ~= "function" then
        return nil, "aion.ui list unavailable"
    end

    local list_ok, list, list_err = ui_runtime.list(true)
    if not list_ok then
        return nil, "ui list failed: " .. tostring(list_err)
    end

    local best = nil
    local best_reason = ""
    local best_score = math.huge
    local best_dist = 0
    for _, ctrl in ipairs(list or {}) do
        local obj = tonumber(ctrl and (ctrl.obj or ctrl.addr)) or 0
        if obj > 0 and main_quest_ui_obj_visible(ctrl)
            and not main_quest_obelisk_confirm_excluded(ctrl) then
            local x = tonumber(ctrl.x)
            local y = tonumber(ctrl.y)
            local dist = math.huge
            if x and y then
                local dx = x - target_x
                local dy = y - target_y
                dist = math.sqrt(dx * dx + dy * dy)
            end

            local by_name = main_quest_obelisk_button_name_matches(ctrl.name)
            local by_coord = dist <= tolerance and main_quest_obelisk_coord_candidate(ctrl)
            if by_name or by_coord then
                local score = dist
                local reason = "coord"
                if by_name then
                    score = math.min(dist, tolerance)
                    reason = "list-name"
                else
                    score = 1000 + dist
                end
                if score < best_score then
                    best = ctrl
                    best_reason = reason
                    best_score = score
                    best_dist = dist
                end
            end
        end
    end

    if best then
        return best, main_quest_obelisk_confirm_line(best, best_reason, best_dist)
    end

    return nil, "obelisk confirm button not found near x=" .. tostring(target_x) ..
        " y=" .. tostring(target_y) ..
        " tolerance=" .. tostring(tolerance)
end

function main_quest_click_obelisk_confirm(ui_runtime, params)
    local ctrl, line_or_err = main_quest_find_obelisk_confirm_button(ui_runtime, params)
    if not ctrl then
        return false, line_or_err
    end
    local obj = ctrl.obj or ctrl.addr
    local click_ok, clicked, click_err = ui_runtime.click(obj)
    if not click_ok or clicked == false then
        return false, "obelisk confirm click failed obj=" .. tostring(obj) ..
            " err=" .. tostring(click_err or clicked)
    end
    return true, "clicked " .. tostring(line_or_err)
end

function main_quest_press_obelisk_confirm_key(reason)
    if not ok_remote or not remote or type(remote.pressKey) ~= "function" then
        return false, "aion.remote.pressKey unavailable"
    end
    local press_ok, pressed, press_err = remote.pressKey(0x0D)
    if press_ok and pressed ~= false then
        return true, "pressed Enter fallback reason=" .. tostring(reason or "")
    end
    return false, "press Enter failed: " .. tostring(press_err or pressed)
end

function main_quest_read_20611_ui_state()
    local out = {
        obelisk_confirm_visible = false,
        obelisk_confirm_detail = "",
        quest_indicator_entry = false,
        quest_indicator_entry_detail = "",
        quest_panel_visible = false,
        quest_detail_target_link_20611 = false,
        quest_detail_target_link_20611_detail = "",
        dictionary_teleport_to_npc = false,
        dictionary_teleport_to_npc_detail = "",
    }
    local ok_ui_runtime, ui_runtime = pcall(require, "aion.ui")
    if not ok_ui_runtime or not ui_runtime then
        out.obelisk_confirm_detail = "aion.ui unavailable " .. tostring(ui_runtime)
        out.quest_indicator_entry_detail = out.obelisk_confirm_detail
        out.quest_detail_target_link_20611_detail = out.obelisk_confirm_detail
        out.dictionary_teleport_to_npc_detail = out.obelisk_confirm_detail
        return out
    end
    local ctrl, line_or_err = main_quest_find_obelisk_confirm_button(ui_runtime, {
        confirm_x = 684,
        confirm_y = 437,
        confirm_tolerance = 90,
    })
    out.obelisk_confirm_visible = ctrl ~= nil
    out.obelisk_confirm_detail = tostring(line_or_err or "")
    local panel_visible, panel_detail = main_quest_quest_panel_visible()
    out.quest_panel_visible = panel_visible == true
    out.quest_panel_detail = tostring(panel_detail or "")
    if type(ui_runtime.children) == "function" then
        local ok_indicator, indicator_children, indicator_err = ui_runtime.children("quest_indicator_dialog", 4)
        if ok_indicator then
            local entry = main_quest_find_ui_child_by_name(indicator_children, "prototype")
            out.quest_indicator_entry = entry ~= nil
            if entry then
                out.quest_indicator_entry_detail = "obj=" .. tostring(entry.obj or entry.addr or "") ..
                    " x=" .. tostring(entry.x or "") ..
                    " y=" .. tostring(entry.y or "")
            else
                out.quest_indicator_entry_detail = "quest indicator prototype not found"
            end
        else
            out.quest_indicator_entry_detail = "read quest_indicator_dialog children failed " .. tostring(indicator_err or "")
        end
        local ok_quest, quest_children, quest_err = ui_runtime.children("v3_quest_dialog", 6)
        if ok_quest then
            local child, dist = main_quest_find_ui_child_at(quest_children, 463, 171, 45)
            out.quest_detail_target_link_20611 = child ~= nil
            if child then
                out.quest_detail_target_link_20611_detail = string.format("obj=%s name=%s x=%.0f y=%.0f dist=%.1f",
                    tostring(child.obj or child.addr or ""),
                    tostring(child.name or ""),
                    tonumber(child.x) or 0,
                    tonumber(child.y) or 0,
                    tonumber(dist) or 0)
            else
                out.quest_detail_target_link_20611_detail = "quest detail target link not found"
            end
        else
            out.quest_detail_target_link_20611_detail = "read v3_quest_dialog children failed " .. tostring(quest_err or "")
        end
        local ok_dictionary, dictionary_children, dictionary_err = ui_runtime.children("dictionary_dialog", 6)
        if ok_dictionary then
            local teleport = main_quest_find_ui_child_by_name(dictionary_children, "teleport_to_npc")
            out.dictionary_teleport_to_npc = teleport ~= nil
            if teleport then
                out.dictionary_teleport_to_npc_detail = "obj=" .. tostring(teleport.obj or teleport.addr or "") ..
                    " name=" .. tostring(teleport.name or "")
            else
                out.dictionary_teleport_to_npc_detail = "dictionary teleport_to_npc not found"
            end
        else
            out.dictionary_teleport_to_npc_detail = "read dictionary_dialog children failed " .. tostring(dictionary_err or "")
        end
    else
        out.quest_indicator_entry_detail = "aion.ui.children unavailable"
        out.quest_detail_target_link_20611_detail = "aion.ui.children unavailable"
        out.dictionary_teleport_to_npc_detail = "aion.ui.children unavailable"
    end
    return out
end

function main_quest_execute_20590(action, state)
    action = type(action) == "table" and action or {}
    state = type(state) == "table" and state or {}
    runtime.main_quest = runtime.main_quest or {}
    local r = runtime.main_quest
    local name = tostring(action.name or "")
    local params = type(action.params) == "table" and action.params or {}
    local stage = tostring(params.stage or "")
    local authorizes_grind = type(main_quest_action_authorizes_grind) == "function"
        and main_quest_action_authorizes_grind(action)
    if not authorizes_grind and type(main_quest_clear_grind_authorization) == "function" then
        main_quest_clear_grind_authorization("action:" .. name)
    end

    local prev_name = tostring(r.current_action_name or "")
    local prev_stage = tostring(r.current_action_stage or "")
    if name ~= prev_name or stage ~= prev_stage then
        if main_quest_is_move_action(prev_name) and main_quest_action_waits_after_move(name) then
            main_quest_set_action_delay("action-switch:" .. prev_name .. "->" .. name)
        end
        main_quest_trace("action-switch",
            "from=" .. prev_name .. "/" .. prev_stage ..
            " to=" .. name .. "/" .. stage ..
            " pos=" .. main_quest_position_text(state.char) ..
            " dialog=" .. main_quest_dialog_signature(state.dialog),
            0)
        r.current_action_name = name
        r.current_action_stage = stage
    end

    if main_quest_wait_action_delay(name, stage) then
        if authorizes_grind and type(main_quest_clear_grind_authorization) == "function" then
            main_quest_clear_grind_authorization("action-delay:" .. name)
        end
        return true
    end

    if type(main_quest_combat_guard_blocks_action) == "function"
        and main_quest_combat_guard_blocks_action(action, state) then
        return true
    end

    if name == "FollowRoute" then
        return main_quest_start_route(action, state)
    end

    if name == "WaitRouteComplete" then
        if main_quest_action_cooldown(name .. ":" .. tostring(params.stage or ""), 0.5) then
            main_quest_trace("wait-route:" .. tostring(params.stage or ""),
                "stage=" .. tostring(params.stage or "") ..
                " active_stage=" .. tostring(main_quest_active_route_stage()) ..
                " pos=" .. main_quest_position_text(state.char),
                0)
        end
        return true
    end

    if name == "WaitUiControlVisible" then
        if main_quest_action_cooldown(name .. ":" .. tostring(stage), 0.5) then
            main_quest_set_status("等待UI可见 stage=" .. tostring(stage) ..
                " parent=" .. tostring(params.parent or "") ..
                " name=" .. tostring(params.name or ""))
        end
        return true
    end

    if name == "OpenQuestPanel" then
        if not main_quest_action_cooldown(name .. ":" .. tostring(stage), 0.5) then
            return true
        end
        local quest_id = tonumber(params.quest_id) or 0
        local panel_ready, panel_detail = main_quest_prepare_quest_teleport_panel(quest_id, stage)
        main_quest_trace("quest-panel-open-action:" .. tostring(stage),
            "quest=" .. tostring(quest_id) ..
            " ready=" .. tostring(panel_ready) ..
            " detail=" .. tostring(panel_detail or "") ..
            " pos=" .. main_quest_position_text(state.char),
            0)
        main_quest_set_status("open quest panel quest_id=" .. tostring(quest_id) ..
            " stage=" .. tostring(stage) ..
            " result=" .. tostring(panel_detail or ""))
        if panel_ready then
            return true
        end
        local detail_text = tostring(panel_detail or "")
        if string.find(detail_text, "press J failed", 1, true)
            or string.find(detail_text, "aion.remote.pressKey unavailable", 1, true) then
            return false
        end
        return true
    end

    if name == "NavigateToNpc" or name == "FinalMoveToNpc" or name == "NavigateToGrindPoint" then
        if not ok_nav or not nav or type(nav.moveTo) ~= "function" then
            main_quest_set_status("移动失败: aion.nav 不可用")
            return false
        end
        local move_interval = math.max(0.2, tonumber(cfg.leveling and cfg.leveling.move_resend_interval) or 0.5)
        if not main_quest_action_cooldown(name, move_interval) then
            return true
        end
        local target = { x = params.x, y = params.y, z = params.z }
        local quest_id = tostring(params.quest_id or "")
        local source = "main-quest"
        if quest_id ~= "" then
            source = source .. "-" .. quest_id
        end
        move_trace(source, target,
            "action=" .. name ..
            " stage=" .. tostring(params.stage or "") ..
            " route=" .. tostring(params.route_index or "") .. "/" .. tostring(params.route_count or "") ..
            " dist=" .. tostring(params.distance or "") ..
            " state_pos=" .. main_quest_position_text(state.char),
            0)
        local ok, moved, err = nav.moveTo(params.x, params.y, params.z)
        move_trace(source .. "-result", target,
            "action=" .. name ..
            " stage=" .. tostring(params.stage or "") ..
            " ok=" .. tostring(ok) ..
            " moved=" .. tostring(moved) ..
            " err=" .. tostring(err or ""),
            0)
        main_quest_set_status(string.format(
            "前往主线NPC stage=%s route=%s/%s dist=%.1f result=%s err=%s",
            tostring(params.stage or ""),
            tostring(params.route_index or ""),
            tostring(params.route_count or ""),
            tonumber(params.distance) or 0,
            tostring(moved),
            tostring(err or "")))
        if ok and moved ~= false then
            r.last_nav_stage = tostring(params.stage or "")
            r.last_nav_at = now_seconds()
            r.last_nav_distance = tonumber(params.distance) or 0
            if params.mark_20612_start_point_reached == true then
                r.reached_20612_start_point = true
            end
            if name == "FinalMoveToNpc" and tostring(params.stage or "") == "inner_npc" then
                r.completed_20590_inner_final_move = true
            end
        end
        return ok and moved ~= false
    end

    if name == "ClickUiControl" or name == "ClickUiControlAt" or name == "ClickUiControlWaitTeleport" then
        local post_dialog_settle_until = tonumber(r.post_dialog_settle_until) or 0
        local now = now_seconds()
        if now < post_dialog_settle_until then
            if main_quest_action_cooldown("PostDialogSettle:" .. tostring(stage), 0.4) then
                main_quest_set_status("等待对话完成后稳定 stage=" .. tostring(stage) ..
                    " remain=" .. string.format("%.1f", post_dialog_settle_until - now))
            end
            return true
        end
        if not main_quest_action_cooldown(name .. ":" .. tostring(stage), 0.8) then
            return true
        end
        local ok_ui_runtime, ui_runtime = pcall(require, "aion.ui")
        if not ok_ui_runtime or not ui_runtime or type(ui_runtime.click) ~= "function" then
            main_quest_set_status("ui click failed: aion.ui unavailable " .. tostring(ui_runtime))
            return false
        end
        local click_ok, line_or_err
        if name == "ClickUiControlAt" then
            click_ok, line_or_err = main_quest_click_ui_control_at(ui_runtime,
                params.parent,
                params.x,
                params.y,
                params.tolerance,
                params.depth)
        else
            click_ok, line_or_err = main_quest_click_ui_control_by_name(ui_runtime,
                params.parent,
                params.name,
                params.depth)
        end
        if click_ok then
            if stage == "quest_20610_indicator_teleport" then
                r.clicked_20610_indicator_teleport = true
            elseif stage == "quest_20610_target_link" then
                r.clicked_20610_target_link = true
            elseif stage == "quest_20610_task_teleport" then
                r.clicked_20610_dictionary_teleport = true
            elseif stage == "quest_20611_indicator_title" then
                r.clicked_20611_indicator_title = true
                r.clicked_20611_indicator_entry_name = tostring(params.name or "")
            elseif stage == "quest_20611_target_link" then
                r.clicked_20611_target_link = true
            elseif stage == "quest_20611_target_teleport" then
                r.clicked_20611_dictionary_teleport = true
            end
            if name == "ClickUiControlWaitTeleport" then
                r.waiting_teleport = true
                r.teleport_quest_id = tonumber(params.quest_id) or 0
                r.teleport_stage = tostring(params.stage or "")
                r.teleport_start_pos = state.char
                r.teleport_start_big_map_id = tonumber(state.big_map_id) or 0
            end
        end
        main_quest_set_status("ui click action=" .. name ..
            " quest_id=" .. tostring(params.quest_id or "") ..
            " stage=" .. tostring(stage) ..
            " parent=" .. tostring(params.parent or "") ..
            " name=" .. tostring(params.name or "") ..
            " previous_name=" .. tostring(params.previous_name or "") ..
            " result=" .. tostring(line_or_err))
        main_quest_trace("ui-click:" .. tostring(stage),
            "action=" .. name ..
            " ok=" .. tostring(click_ok) ..
            " name=" .. tostring(params.name or "") ..
            " previous_name=" .. tostring(params.previous_name or "") ..
            " result=" .. tostring(line_or_err) ..
            " pos=" .. main_quest_position_text(state.char),
            0)
        return click_ok
    end

    if name == "MapNodeTeleportByName" then
        local post_dialog_settle_until = tonumber(r.post_dialog_settle_until) or 0
        local now = now_seconds()
        if now < post_dialog_settle_until then
            if main_quest_action_cooldown("PostDialogSettle:" .. tostring(stage), 0.4) then
                main_quest_set_status("绛夊緟瀵硅瘽瀹屾垚鍚庡湴鍥句紶閫?stage=" .. tostring(stage) ..
                    " remain=" .. string.format("%.1f", post_dialog_settle_until - now))
            end
            return true
        end
        if not main_quest_action_cooldown(name .. ":" .. tostring(stage), 1.0) then
            return true
        end
        if not ok_map or not map or type(map.nodes) ~= "function" or type(map.nodeTeleport) ~= "function" then
            main_quest_set_status("map node teleport failed: aion.map unavailable")
            return false
        end
        if ok_core and core and type(core.ensureInit) == "function" then
            local init_ok, init_err = core.ensureInit(tonumber(cfg.target.pid) or nil)
            if not init_ok then
                main_quest_set_status("map node teleport init failed: " .. tostring(init_err))
                return false
            end
        end

        local big_map_id = tonumber(params.big_map_id) or tonumber(state.big_map_id) or 0
        if big_map_id <= 0 and type(map.bigMapId) == "function" then
            local id_ok, id_value, id_err = map.bigMapId()
            if id_ok then
                big_map_id = tonumber(id_value) or 0
            else
                main_quest_trace("map-node-teleport-big-map:" .. tostring(stage),
                    "read failed err=" .. tostring(id_err or ""),
                    0)
            end
        end

        local list_ok, nodes, list_err = map.nodes(big_map_id > 0 and big_map_id or nil)
        if not list_ok then
            main_quest_set_status("map node teleport list failed: " .. tostring(list_err))
            return false
        end

        local wanted_name = tostring(params.node_name or "")
        local wanted_name_en = tostring(params.node_name_en or "")
        local wanted_id = tonumber(params.node_id) or 0
        local selected = nil
        local selected_index = 0
        local match_type = ""

        for index, node in ipairs(nodes or {}) do
            local node_name = tostring(node.name or "")
            local node_name_en = tostring(node.name_en or "")
            if (wanted_name ~= "" and (node_name == wanted_name or node_name_en == wanted_name))
                or (wanted_name_en ~= "" and (node_name == wanted_name_en or node_name_en == wanted_name_en)) then
                selected = node
                selected_index = index
                match_type = "name"
                break
            end
        end
        if not selected and wanted_id > 0 then
            for index, node in ipairs(nodes or {}) do
                if tonumber(node.node_id or node.id or 0) == wanted_id then
                    selected = node
                    selected_index = index
                    match_type = "id"
                    break
                end
            end
        end

        if not selected then
            main_quest_set_status("map node teleport target not found name=" .. wanted_name ..
                " name_en=" .. wanted_name_en ..
                " id=" .. tostring(wanted_id) ..
                " count=" .. tostring(count_array(nodes or {})))
            main_quest_trace("map-node-teleport-not-found:" .. tostring(stage),
                "quest=" .. tostring(params.quest_id or "") ..
                " big_map_id=" .. tostring(big_map_id) ..
                " name=" .. wanted_name ..
                " name_en=" .. wanted_name_en ..
                " id=" .. tostring(wanted_id) ..
                " count=" .. tostring(count_array(nodes or {})),
                0)
            return false
        end

        local node_id = tonumber(selected.node_id or selected.id or 0) or 0
        local price = tonumber(selected.price)
        if price == nil then
            price = tonumber(params.price) or 0
        end
        if node_id <= 0 then
            main_quest_set_status("map node teleport failed: invalid node_id match=" .. match_type)
            return false
        end

        if type(map.canTeleport) == "function" then
            local can_ok, can_value, can_err = map.canTeleport()
            if can_ok and can_value ~= true then
                main_quest_set_status("map node teleport waiting: IsCanTeleport=false stage=" .. tostring(stage) ..
                    " node_id=" .. tostring(node_id))
                main_quest_trace("map-node-teleport-wait-can:" .. tostring(stage),
                    "quest=" .. tostring(params.quest_id or "") ..
                    " node_id=" .. tostring(node_id) ..
                    " name=" .. tostring(selected.name or "") ..
                    " name_en=" .. tostring(selected.name_en or "") ..
                    " pos=" .. main_quest_position_text(state.char),
                    0.5)
                return true
            elseif not can_ok then
                main_quest_trace("map-node-teleport-can-failed:" .. tostring(stage),
                    "err=" .. tostring(can_err or "") .. " will_attempt=true",
                    0)
            end
        end

        local ok, result, err = map.nodeTeleport(node_id, price)
        if ok and result ~= false and params.wait_teleport ~= false then
            r.waiting_teleport = true
            r.teleport_quest_id = tonumber(params.quest_id) or 0
            r.teleport_stage = tostring(stage or "")
            r.teleport_start_pos = state.char
            r.teleport_start_big_map_id = tonumber(state.big_map_id) or big_map_id
        end
        if ok and result ~= false and tonumber(params.quest_id) == 20611 then
            r.cached_quest_20611 = nil
            r.last_quest_20611_read_at = 0
        end

        local label = ""
        if type(teleport_node_label) == "function" then
            label = teleport_node_label(selected, selected_index)
        else
            label = tostring(selected.name or selected.name_en or node_id)
        end
        main_quest_set_status("map node teleport call quest_id=" .. tostring(params.quest_id or "") ..
            " stage=" .. tostring(stage) ..
            " match=" .. match_type ..
            " node=" .. label ..
            " result=" .. tostring(result) ..
            " err=" .. tostring(err or ""))
        main_quest_trace("map-node-teleport:" .. tostring(stage),
            "quest=" .. tostring(params.quest_id or "") ..
            " ok=" .. tostring(ok) ..
            " result=" .. tostring(result) ..
            " match=" .. match_type ..
            " node_id=" .. tostring(node_id) ..
            " price=" .. tostring(price) ..
            " name=" .. tostring(selected.name or "") ..
            " name_en=" .. tostring(selected.name_en or "") ..
            " big_map_id=" .. tostring(big_map_id) ..
            " err=" .. tostring(err or "") ..
            " pos=" .. main_quest_position_text(state.char),
            0)
        return ok and result ~= false
    end

    if name == "ClickObeliskConfirm" then
        if not main_quest_action_cooldown(name .. ":" .. tostring(stage), 0.8) then
            return true
        end
        local ok_ui_runtime, ui_runtime = pcall(require, "aion.ui")
        if not ok_ui_runtime or not ui_runtime or type(ui_runtime.click) ~= "function" then
            main_quest_set_status("obelisk confirm failed: aion.ui unavailable " .. tostring(ui_runtime))
            return false
        end
        r.wait_dialog_stage = ""
        r.wait_dialog_until = 0
        local click_params = {}
        for key, value in pairs(params) do
            click_params[key] = value
        end
        click_params.allow_common_alert = true
        if r.opened_20611_obelisk == true then
            click_params.require_popup_root = false
            click_params.allow_popup_root_click = true
        end
        local click_ok, line_or_err = main_quest_click_obelisk_confirm(ui_runtime, click_params)
        if not click_ok and r.opened_20611_obelisk == true then
            local key_ok, key_line = main_quest_press_obelisk_confirm_key(line_or_err)
            if key_ok then
                click_ok = true
                line_or_err = key_line
            else
                line_or_err = tostring(line_or_err) .. "; " .. tostring(key_line)
            end
        end
        if click_ok then
            local now = now_seconds()
            r.opened_20611_obelisk = false
            r.opened_20611_obelisk_at = 0
            r.clicked_20611_obelisk_confirm = true
            r.clicked_20611_obelisk_confirm_at = now
            r.obelisk_confirm_wait_until = now + 2.0
            r.cached_quest_20611 = nil
            r.last_quest_20611_read_at = 0
        else
            local now = now_seconds()
            local opened_at = tonumber(r.opened_20611_obelisk_at) or 0
            if opened_at > 0 and now - opened_at > 4.0 then
                r.opened_20611_obelisk = false
                r.opened_20611_obelisk_at = 0
            end
        end
        main_quest_set_status("obelisk confirm action quest_id=" .. tostring(params.quest_id or "") ..
            " stage=" .. tostring(stage) ..
            " result=" .. tostring(line_or_err))
        main_quest_trace("obelisk-confirm:" .. tostring(stage),
            "ok=" .. tostring(click_ok) ..
            " result=" .. tostring(line_or_err) ..
            " pos=" .. main_quest_position_text(state.char),
            0)
        if click_ok then
            return true
        end
        local result_text = tostring(line_or_err or "")
        if string.find(result_text, "not found", 1, true)
            or string.find(result_text, "ui list failed", 1, true) then
            return true
        end
        return false
    end

    if name == "QuestTeleport" then
        if not main_quest_action_cooldown(name .. ":" .. tostring(stage), 1.0) then
            return true
        end
        local quest_id = tonumber(params.quest_id) or 0
        if stage == "quest_20611_level_move" and r.active_20611_grind == true then
            main_quest_stop_20611_grind("quest-20611-level-reached-before-panel", false)
        end
        if not ok_quest or not quest or type(quest.questTeleport) ~= "function" then
            main_quest_set_status("quest teleport failed: aion.quest unavailable")
            return false
        end
        local panel_ready, panel_detail
        if params.open_panel_key == false or params.require_panel_visible == true then
            panel_ready, panel_detail = main_quest_quest_panel_visible()
        else
            panel_ready, panel_detail = main_quest_prepare_quest_teleport_panel(quest_id, stage)
        end
        if not panel_ready then
            main_quest_trace("quest-teleport-wait-panel:" .. tostring(stage),
                "quest=" .. tostring(quest_id) ..
                " open_panel_key=" .. tostring(params.open_panel_key ~= false) ..
                " detail=" .. tostring(panel_detail or "") ..
                " pos=" .. main_quest_position_text(state.char),
                0.5)
            return true
        end
        local teleport_id = nil
        if type(quest.questTeleportId) == "function" then
            local id_ok, id_value = quest.questTeleportId(quest_id)
            if id_ok and tonumber(id_value) and tonumber(id_value) >= 0 then
                teleport_id = tonumber(id_value)
            end
        end
        local ok, result, err = quest.questTeleport(quest_id, teleport_id)
        local wait_teleport = params.wait_teleport ~= false
        if ok and result ~= false and wait_teleport then
            r.waiting_teleport = true
            r.teleport_quest_id = quest_id
            r.teleport_stage = tostring(params.stage or "")
            r.teleport_start_pos = state.char
            r.teleport_start_big_map_id = tonumber(state.big_map_id) or 0
        end
        if ok and result ~= false and stage == "quest_20611_level_move" and not wait_teleport then
            r.completed_20611_level_move = true
            r.level_move_quest_id = quest_id
        end
        if ok and result ~= false then
            r.quest_teleport_panel_key = ""
            r.quest_teleport_panel_opened_at = 0
        end
        main_quest_set_status("quest teleport quest_id=" .. tostring(quest_id) ..
            " stage=" .. tostring(stage) ..
            " teleport_id=" .. tostring(teleport_id or "") ..
            " wait_teleport=" .. tostring(wait_teleport) ..
            " panel=" .. tostring(panel_detail or "") ..
            " result=" .. tostring(result) ..
            " err=" .. tostring(err or ""))
        main_quest_trace("quest-teleport:" .. tostring(stage),
            "quest=" .. tostring(quest_id) ..
            " ok=" .. tostring(ok) ..
            " result=" .. tostring(result) ..
            " teleport_id=" .. tostring(teleport_id or "") ..
            " wait_teleport=" .. tostring(wait_teleport) ..
            " panel=" .. tostring(panel_detail or "") ..
            " err=" .. tostring(err or "") ..
            " pos=" .. main_quest_position_text(state.char),
            0)
        return ok and result ~= false
    end

    if name == "InteractNpc" then
        main_quest_stop_route(tostring(params.stage or ""), "interact-npc")
        local now = now_seconds()
        local settle_seconds = math.max(0, tonumber(cfg.leveling and cfg.leveling.npc_interact_settle_seconds) or 0)
        local stage = tostring(params.stage or "")
        local wait_until = tonumber(r.wait_dialog_until) or 0
        local waiting_same_stage = stage ~= "" and tostring(r.wait_dialog_stage or "") == stage
        local dialog_timeout_after_interact = waiting_same_stage and wait_until > 0 and now >= wait_until
        local confirm_wait_until = tonumber(r.obelisk_confirm_wait_until) or 0
        if stage == "quest_20611_obelisk" and now < confirm_wait_until then
            if main_quest_action_cooldown("WaitObeliskConfirm:" .. stage, 0.4) then
                main_quest_set_status("waiting obelisk confirm settle stage=" .. stage ..
                    " remain=" .. string.format("%.1f", confirm_wait_until - now))
            end
            return true
        end
        if waiting_same_stage and now < wait_until then
            if main_quest_action_cooldown("WaitNpcDialog:" .. stage, 0.4) then
                main_quest_set_status("waiting npc dialog stage=" .. stage ..
                    " remain=" .. string.format("%.1f", wait_until - now))
            end
            return true
        end
        local since_nav = now - (tonumber(r.last_nav_at) or 0)
        if settle_seconds > 0
            and stage ~= ""
            and tostring(r.last_nav_stage or "") == stage
            and since_nav < settle_seconds then
            if main_quest_action_cooldown("WaitNpcSettle:" .. stage, 0.4) then
                main_quest_set_status("等待靠近NPC后站稳 stage=" .. stage ..
                    " wait=" .. string.format("%.1f", settle_seconds - since_nav))
            end
            return true
        end
        local wait_ms = tonumber(cfg.npc_dialog and cfg.npc_dialog.wait_dialog_ms) or 3000
        local retry_seconds = math.max(1.0,
            tonumber(cfg.leveling and cfg.leveling.npc_interact_retry_seconds) or (wait_ms / 1000))
        if not main_quest_action_cooldown(name, retry_seconds) then
            return true
        end
        local ok_npc_runtime, npc_runtime = pcall(require, "aion.npc")
        if not ok_npc_runtime or not npc_runtime or type(npc_runtime.interactByName) ~= "function" then
            main_quest_set_status("打开NPC失败: aion.npc 名字交互不可用")
            return false
        end
        main_quest_trace("interact-before:" .. stage,
            "stage=" .. stage ..
            " legacy_interact_id=" .. tostring(params.interact_id) ..
            " name_key=" .. tostring(params.npc_name_key or params.name_key or "") ..
            " name=" .. tostring(params.npc_name or "") ..
            " pos=" .. main_quest_position_text(state.char) ..
            " dialog=" .. main_quest_dialog_signature(state.dialog),
            0)
        local ok, result, err = false, false, nil
        local npc_name = tostring(params.npc_name or "")
        local npc_name_key = tostring(params.npc_name_key or params.name_key or "")
        local legacy_interact_id = tonumber(params.interact_id) or 0
        local allow_interact_id_fallback = params.allow_interact_id_fallback == true
        local interact_method = "name"
        if npc_name == "" and not (allow_interact_id_fallback and legacy_interact_id > 0) then
            main_quest_set_status("打开NPC失败: npc_name 为空 stage=" .. stage ..
                " name_key=" .. npc_name_key ..
                " legacy_interact_id=" .. tostring(params.interact_id or ""))
            return false
        end
        if dialog_timeout_after_interact then
            main_quest_trace("interact-name-retry:" .. stage,
                "stage=" .. stage ..
                " legacy_interact_id=" .. tostring(params.interact_id) ..
                " name_key=" .. npc_name_key ..
                " name=" .. tostring(params.npc_name or "") ..
                " reason=dialog_timeout_after_interact",
                0)
        end
        if npc_name ~= "" then
            ok, result, err = npc_runtime.interactByName(npc_name)
        end
        if (not ok or result == false)
            and allow_interact_id_fallback
            and legacy_interact_id > 0
            and type(npc_runtime.interactId) == "function" then
            interact_method = npc_name ~= "" and "id-fallback" or "id"
            ok, result, err = npc_runtime.interactId(legacy_interact_id)
        end
        main_quest_set_status("打开主线NPC对话 by_name name_key=" .. npc_name_key ..
            " legacy_interact_id=" .. tostring(params.interact_id) ..
            " name=" .. tostring(params.npc_name or "") ..
            " method=" .. tostring(interact_method) ..
            " result=" .. tostring(result) .. " err=" .. tostring(err or ""))
        if ok and result ~= false then
            local after_interact = now_seconds()
            r.last_interact_stage = stage
            r.last_interact_at = after_interact
            if params.mark_20612_start_point_reached == true then
                r.reached_20612_start_point = true
            end
            if stage == "quest_20611_obelisk" then
                r.opened_20611_obelisk = true
                r.opened_20611_obelisk_at = after_interact
                r.wait_dialog_stage = ""
                r.wait_dialog_until = 0
            else
                r.wait_dialog_stage = stage
                r.wait_dialog_until = after_interact + math.max(1.0, wait_ms / 1000)
            end
        end
        main_quest_trace("interact-after:" .. stage,
            "stage=" .. stage ..
            " ok=" .. tostring(ok) ..
            " result=" .. tostring(result) ..
            " method=" .. tostring(interact_method) ..
            " name_key=" .. npc_name_key ..
            " err=" .. tostring(err or "") ..
            " wait_dialog_until=" .. tostring(r.wait_dialog_until or 0),
            0)
        return ok and result ~= false
    end

    if name == "ClickDialogXContinuous" or name == "ClickDialogXContinuousWaitTeleport" then
        if not main_quest_action_cooldown(name .. ":" .. tostring(params.stage or params.type_text), 1.0) then
            return true
        end
        local ready = npc_dialog_prepare_runtime()
        if not ready then
            main_quest_set_status("continuous dialog x-click failed: NPC dialog runtime unavailable")
            return false
        end
        r.wait_dialog_stage = ""
        r.wait_dialog_until = 0
        local wait_for_teleport = name == "ClickDialogXContinuousWaitTeleport"
            or params.wait_teleport == true
        local teleport_stage = tostring(params.stage or "teleport")
        local teleport_start_pos = state.char
        local teleport_start_big_map_id = tonumber(state.big_map_id) or 0
        main_quest_trace("click-continuous-before:" .. tostring(params.type_text or ""),
            "action=" .. name ..
            " stage=" .. tostring(params.stage or "") ..
            " type=" .. tostring(params.type_text or "") ..
            " content=" .. tostring(params.content_id or "") ..
            " expected=" .. tostring(params.expected_content_id or "") ..
            " click_x=" .. tostring(params.click_x or "") ..
            " wait_teleport=" .. tostring(wait_for_teleport) ..
            " dialog=" .. main_quest_dialog_signature(state.dialog) ..
            " pos=" .. main_quest_position_text(state.char),
            0)
        local ok, continuous_result = npc_continuous_click_dialog_x({
            click_x = params.click_x,
            click_y = params.click_y,
            click_y_tolerance = params.click_y_tolerance,
            max_steps = params.max_steps,
            delay_ms = params.delay_ms,
        })
        local continuous_finished = continuous_result == "closed" or continuous_result == "limit_ok"
        if ok and wait_for_teleport then
            r.waiting_teleport = true
            r.teleport_quest_id = tonumber(params.quest_id) or 0
            r.teleport_stage = teleport_stage
            r.teleport_start_pos = teleport_start_pos
            r.teleport_start_big_map_id = teleport_start_big_map_id
        end
        if ok then
            local continuous_quest_id = tonumber(params.quest_id) or 0
            local continuous_stage = tostring(params.stage or "")
            if continuous_quest_id == 20610 then
                r.cached_quest_20610 = nil
                r.last_quest_20610_read_at = 0
                if continuous_stage == "quest_20610_npc" and continuous_finished then
                    r.completed_20610_start_dialog = true
                end
            elseif continuous_quest_id == 20611 then
                r.cached_quest_20611 = nil
                r.last_quest_20611_read_at = 0
                if continuous_stage == "quest_20611_mission_npc" and continuous_finished then
                    r.completed_20611_mission_dialog = true
                end
                if continuous_stage == "quest_20611_target_npc" and continuous_finished then
                    r.completed_20611_target_dialog = true
                end
                if continuous_stage == "quest_20611_hotspot_reward_npc" and continuous_finished then
                    r.completed_20611_hotspot_reward = true
                end
            elseif continuous_quest_id == 20612 then
                r.cached_quest_20611 = nil
                r.last_quest_20611_read_at = 0
                if params.mark_20612_start_point_reached == true then
                    r.reached_20612_start_point = true
                end
                if continuous_stage == "quest_20612_start_npc" and continuous_finished then
                    r.completed_20612_start_dialog = true
                end
                if continuous_stage == "quest_20612_reward_npc" and continuous_finished then
                    r.completed_20612_task_teleport = true
                    r.completed_20612_reward_dialog = true
                end
            end
            local settle_seconds = math.max(0,
                tonumber(cfg.leveling and cfg.leveling.post_dialog_settle_seconds) or 2.0)
            if settle_seconds > 0 then
                r.post_dialog_settle_until = math.max(
                    tonumber(r.post_dialog_settle_until) or 0,
                    now_seconds() + settle_seconds)
            end
        end
        local status = tostring(runtime.npc_dialog and runtime.npc_dialog.last_status or "")
        main_quest_set_status("continuous dialog x-click quest_id=" .. tostring(params.quest_id or "") ..
            " stage=" .. tostring(params.stage or "") ..
            " type=" .. tostring(params.type_text or "") ..
            " click_x=" .. tostring(params.click_x or "") ..
            " wait_teleport=" .. tostring(wait_for_teleport) ..
            " result=" .. tostring(ok) ..
            " finish=" .. tostring(continuous_result or "") ..
            " status=" .. status)
        main_quest_trace("click-continuous-after:" .. tostring(params.type_text or ""),
            "ok=" .. tostring(ok) ..
            " finish=" .. tostring(continuous_result or "") ..
            " wait_teleport=" .. tostring(wait_for_teleport) ..
            " status=" .. status,
            0)
        return ok
    end

    if name == "ClickDialogX" or name == "ClickDialogXWaitTeleport" or name == "ClickDialogXCompleteQuest" then
        if not main_quest_action_cooldown(name .. ":" .. tostring(params.type_text), 0.8) then
            return true
        end
        local ready, npc_runtime, ui_runtime = npc_dialog_prepare_runtime()
        if not ready or not ui_runtime then
            main_quest_set_status("点击对话失败: NPC对话运行时不可用")
            return false
        end
        r.wait_dialog_stage = ""
        r.wait_dialog_until = 0
        local wait_for_teleport = name == "ClickDialogXWaitTeleport"
        local complete_after_click = name == "ClickDialogXCompleteQuest"
        local teleport_stage = tostring(params.stage or "teleport")
        local teleport_start_pos = state.char
        local teleport_start_big_map_id = tonumber(state.big_map_id) or 0
        main_quest_trace("click-before:" .. tostring(params.type_text or ""),
            "action=" .. name ..
            " stage=" .. tostring(params.stage or "") ..
            " type=" .. tostring(params.type_text or "") ..
            " content=" .. tostring(params.content_id or "") ..
            " expected=" .. tostring(params.expected_content_id or "") ..
            " dialog=" .. main_quest_dialog_signature(state.dialog) ..
            " pos=" .. main_quest_position_text(state.char),
            0)
        local ok, line_or_err = npc_click_dialog_x_once(ui_runtime,
            "quest" .. tostring(params.quest_id or ""),
            params.click_x,
            params.click_y,
            params.click_y_tolerance)
        local after_sig = "unread"
        if npc_runtime and type(npc_runtime.dialog) == "function" then
            local dialog_ok, info = npc_runtime.dialog()
            if dialog_ok then
                after_sig = main_quest_dialog_signature(info)
            else
                after_sig = "read_failed"
            end
        end
        if ok and wait_for_teleport then
            r.waiting_teleport = true
            r.teleport_quest_id = tonumber(params.quest_id) or 0
            r.teleport_stage = teleport_stage
            r.teleport_start_pos = teleport_start_pos
            r.teleport_start_big_map_id = teleport_start_big_map_id
        end
        if ok and complete_after_click and tonumber(params.quest_id) == 20610 then
            r.completed_20610_start_dialog = true
            r.cached_quest_20610 = nil
            r.last_quest_20610_read_at = 0
            local settle_seconds = math.max(0,
                tonumber(cfg.leveling and cfg.leveling.post_dialog_settle_seconds) or 2.0)
            if settle_seconds > 0 then
                r.post_dialog_settle_until = math.max(
                    tonumber(r.post_dialog_settle_until) or 0,
                    now_seconds() + settle_seconds)
            end
        elseif ok and complete_after_click and tonumber(params.quest_id) == 20611 then
            r.completed_20611_mission_dialog = true
            r.cached_quest_20611 = nil
            r.last_quest_20611_read_at = 0
            local settle_seconds = math.max(0,
                tonumber(cfg.leveling and cfg.leveling.post_dialog_settle_seconds) or 2.0)
            if settle_seconds > 0 then
                r.post_dialog_settle_until = math.max(
                    tonumber(r.post_dialog_settle_until) or 0,
                    now_seconds() + settle_seconds)
            end
        end
        main_quest_set_status("点击首个主线对话 type=" .. tostring(params.type_text) ..
            " content=" .. tostring(params.content_id) ..
            " wait_teleport=" .. tostring(wait_for_teleport) ..
            " complete_after_click=" .. tostring(complete_after_click) ..
            " result=" .. tostring(line_or_err))
        main_quest_trace("click-after:" .. tostring(params.type_text or ""),
            "ok=" .. tostring(ok) ..
            " result=" .. tostring(line_or_err) ..
            " after_dialog=" .. after_sig ..
            " wait_teleport=" .. tostring(wait_for_teleport) ..
            " complete_after_click=" .. tostring(complete_after_click),
            0)
        return ok
    end

    if name == "ClickDialogOkCompleteQuest" then
        if not main_quest_action_cooldown(name .. ":" .. tostring(params.type_text), 0.8) then
            return true
        end
        local ready, _, ui_runtime = npc_dialog_prepare_runtime()
        if not ready or not ui_runtime then
            main_quest_set_status("确认奖励失败: NPC对话运行时不可用")
            return false
        end
        r.wait_dialog_stage = ""
        r.wait_dialog_until = 0
        local ok, line_or_err = npc_click_dialog_ok_button(ui_runtime)
        if ok then
            r.waiting_teleport = false
            if tonumber(params.quest_id) == 20610 then
                r.completed_20610_reward = true
                r.completed_20610_task_teleport = true
                r.cached_quest_20610 = nil
                r.last_quest_20610_read_at = 0
            elseif main_quest_is_20611_blue_task_id(params.quest_id) then
                main_quest_stop_20611_grind("quest-20611-remote-reward-ok", true)
            else
                r.completed_20590_reward = true
                r.completed_20590_teleport = true
                r.cached_quest = nil
                r.last_quest_read_at = 0
            end
        end
        main_quest_set_status("确认主线20590奖励 type=" .. tostring(params.type_text) ..
            " content=" .. tostring(params.content_id) ..
            " result=" .. tostring(line_or_err))
        return ok
    end

    if name == "WaitPositionChanged" then
        if main_quest_action_cooldown(name, 2.0) then
            main_quest_set_status("waiting quest teleport position change quest_id=" ..
                tostring(params.quest_id or "") ..
                " stage=" .. tostring(params.stage or "") ..
                " min_distance=" .. tostring(params.min_distance or ""))
        end
        return true
    end

    if name == "CompleteStep" then
        r.waiting_teleport = false
        r.wait_dialog_stage = ""
        r.wait_dialog_until = 0
        local stage = tostring(params.stage or action.reason or "")
        if stage == "first_npc_teleport" then
            r.completed_20590_first_teleport = true
            r.completed_20590_teleport = false
        elseif stage == "inner_npc_teleport" then
            r.completed_20590_inner_teleport = true
            r.completed_20590_teleport = true
        elseif stage == "temple_npc_teleport" then
            r.completed_20590_temple_teleport = true
            r.completed_20590_teleport = false
        end
        main_quest_set_status("主线20590阶段完成 stage=" .. stage .. " reason=" .. tostring(action.reason or ""))
        return true
    end

    if name == "CompleteMapNodeTeleport" then
        r.waiting_teleport = false
        r.teleport_quest_id = 0
        r.wait_dialog_stage = ""
        r.wait_dialog_until = 0
        local stage = tostring(params.stage or "")
        if tonumber(params.quest_id) == 20611 and stage == "quest_20611_hotspot_teleport" then
            r.completed_20611_hotspot_teleport = true
            r.cached_quest_20611 = nil
            r.last_quest_20611_read_at = 0
        end
        main_quest_set_status("complete map node teleport quest_id=" .. tostring(params.quest_id or "") ..
            " stage=" .. tostring(stage) ..
            " reason=" .. tostring(action.reason or ""))
        main_quest_trace("map-node-teleport-complete:" .. tostring(stage),
            "quest=" .. tostring(params.quest_id or "") ..
            " reason=" .. tostring(action.reason or "") ..
            " pos=" .. main_quest_position_text(state.char),
            0)
        return true
    end

    if name == "CompleteQuestTeleport" then
        r.waiting_teleport = false
        r.teleport_quest_id = 0
        r.wait_dialog_stage = ""
        r.wait_dialog_until = 0
        local stage = tostring(params.stage or "")
        if stage == "quest_20612_task_teleport" then
            r.clicked_20611_indicator_title = false
            r.completed_20612_task_teleport = true
            r.cached_quest_20611 = nil
            r.last_quest_20611_read_at = 0
        elseif tonumber(params.quest_id) == 20610 then
            r.completed_20610_task_teleport = true
            r.cached_quest_20610 = nil
            r.last_quest_20610_read_at = 0
        elseif tonumber(params.quest_id) == 20611 then
            r.clicked_20611_indicator_title = false
            if stage == "quest_20611_target_teleport" then
                r.completed_20611_target_teleport = true
            else
                r.completed_20611_level_move = true
                r.level_move_quest_id = tonumber(params.quest_id) or 20611
            end
            r.cached_quest_20611 = nil
            r.last_quest_20611_read_at = 0
        end
        main_quest_set_status("complete quest teleport quest_id=" .. tostring(params.quest_id or "") ..
            " stage=" .. tostring(params.stage or "") ..
            " reason=" .. tostring(action.reason or ""))
        return true
    end

    if name == "CompleteQuest" then
        r.waiting_teleport = false
        r.wait_dialog_stage = ""
        r.wait_dialog_until = 0
        if tonumber(params.quest_id) == 20610 then
            r.completed_20610_start_dialog = true
            r.cached_quest_20610 = nil
            r.last_quest_20610_read_at = 0
        end
        main_quest_set_status("complete main quest quest_id=" .. tostring(params.quest_id or "") ..
            " stage=" .. tostring(params.stage or "") ..
            " reason=" .. tostring(action.reason or ""))
        return true
    end

    if name == "OpenQuestSubmit" then
        if not main_quest_action_cooldown(name .. ":" .. tostring(params.quest_id or ""), 1.0) then
            return true
        end
        main_quest_stop_20611_grind("quest-20611-reward-ready", false)
        if not ok_quest or not quest or type(quest.openSubmit) ~= "function" then
            main_quest_set_status("open quest submit failed: aion.quest unavailable")
            return false
        end
        local quest_id = tonumber(params.quest_id) or 0
        local submit_ok, opened, submit_err = quest.openSubmit(quest_id)
        local ok = submit_ok == true and opened ~= false
        if ok and main_quest_is_20611_blue_task_id(quest_id) then
            r.opened_20611_remote_reward = true
        end
        main_quest_set_status("open quest submit quest_id=" .. tostring(quest_id) ..
            " stage=" .. tostring(params.stage or "") ..
            " result=" .. tostring(opened) ..
            " err=" .. tostring(submit_err or ""))
        main_quest_trace("open-submit:" .. tostring(params.stage or ""),
            "quest=" .. tostring(quest_id) ..
            " ok=" .. tostring(ok) ..
            " result=" .. tostring(opened) ..
            " err=" .. tostring(submit_err or ""),
            0)
        return ok
    end

    if name == "StartStationaryGrind" then
        local grind_stage = tostring(params.stage or "")
        local is_level_grind = grind_stage == "quest_20611_level_grind"
            or grind_stage == "quest_20612_level_grind"
        r.active_20611_grind = true
        r.active_20611_grind_stage = grind_stage
        if is_level_grind then
            r.level_grind_quest_id = tonumber(params.quest_id) or 0
            r.level_grind_required_level = tonumber(params.until_level or params.required_level) or 0
            r.completed_20611_level_move = false
            r.level_move_quest_id = 0
        else
            r.completed_20611_grind = false
            r.level_grind_quest_id = 0
            r.level_grind_required_level = 0
        end
        if type(main_quest_authorize_grind) == "function" then
            main_quest_authorize_grind(action, "start")
        end
        r.cached_quest_20611 = nil
        r.last_quest_20611_read_at = 0

        cfg.combat = cfg.combat or {}
        cfg.combat.mode = 1
        cfg.combat.enabled = true
        cfg.combat.anchor_enabled = true
        cfg.combat.anchor_x = tonumber(params.x) or 0
        cfg.combat.anchor_y = tonumber(params.y) or 0
        cfg.combat.anchor_z = tonumber(params.z) or 0

        runtime.combat = runtime.combat or {}
        runtime.combat.mode = "stationary"
        runtime.combat.anchor = {
            x = cfg.combat.anchor_x,
            y = cfg.combat.anchor_y,
            z = cfg.combat.anchor_z,
        }
        runtime.combat.target_obj = 0
        runtime.combat.target_name = ""
        runtime.combat.target_distance = 0
        runtime.combat.anchor_distance = 0
        runtime.combat.last_move_at = 0
        if type(sync_combat_enabled_from_primary_mode) == "function" then
            sync_combat_enabled_from_primary_mode()
        end
        if type(combat_auto_off) == "function" then
            combat_auto_off(is_level_grind and ("quest-" .. tostring(params.quest_id or "") .. "-level-grind-start") or "quest-20611-grind-start", false)
        end
        if type(combat_set_status) == "function" then
            combat_set_status(is_level_grind and "level-grind" or "quest-grind", string.format("%.2f,%.2f,%.2f",
                tonumber(params.x) or 0,
                tonumber(params.y) or 0,
                tonumber(params.z) or 0), true)
        end
        main_quest_set_status("quest 20611 stationary grind started stage=" .. grind_stage ..
            " until_level=" .. tostring(params.until_level or params.required_level or "") ..
            " char_level=" .. tostring(params.char_level or ""))
        main_quest_trace("grind-start",
            string.format("quest=%s stage=%s anchor=%.2f,%.2f,%.2f step=%s char_level=%s until_level=%s",
                tostring(params.quest_id or ""),
                grind_stage,
                tonumber(params.x) or 0,
                tonumber(params.y) or 0,
                tonumber(params.z) or 0,
                tostring(params.quest_step or ""),
                tostring(params.char_level or ""),
                tostring(params.until_level or params.required_level or "")),
            0)
        return true
    end

    if name == "WaitLevelGrind" then
        if type(main_quest_authorize_grind) == "function" then
            main_quest_authorize_grind(action, "wait-level")
        end
        if main_quest_action_cooldown(name .. ":" .. tostring(params.quest_id or ""), 2.0) then
            main_quest_set_status("level grind running quest_id=" .. tostring(params.quest_id or "") ..
                " level=" .. tostring(params.char_level or "") ..
                "/" .. tostring(params.required_level or ""))
        end
        return true
    end

    if name == "WaitQuestComplete" then
        if type(main_quest_authorize_grind) == "function" then
            main_quest_authorize_grind(action, "wait-quest")
        end
        if main_quest_action_cooldown(name .. ":" .. tostring(params.quest_id or ""), 2.0) then
            main_quest_set_status("quest " .. tostring(params.quest_id or "") ..
                " grind running step=" .. tostring(params.quest_step or ""))
        end
        return true
    end

    if name == "CompleteQuestGrind" then
        local is_blue_grind = tonumber(params.quest_id) == 20611
            or main_quest_is_20611_blue_task_id(params.quest_id)
        if not is_blue_grind and ok_main_quest_20611 and main_quest_20611
            and type(main_quest_20611.isQuestKnown) == "function" then
            is_blue_grind = main_quest_20611.isQuestKnown({ id = tonumber(params.quest_id) or 0 })
        end
        if is_blue_grind then
            main_quest_stop_20611_grind("quest-20611-grind-complete", true)
        end
        main_quest_set_status("quest grind complete quest_id=" .. tostring(params.quest_id or "") ..
            " step=" .. tostring(params.quest_step or ""))
        return true
    end

    if name == "DumpDialog" then
        r.wait_dialog_stage = ""
        r.wait_dialog_until = 0
        local dump_key = tostring(params.stage or "") .. ":" .. tostring(params.type_text or "")
        if main_quest_action_cooldown("DumpDialog:" .. dump_key, 1.0) then
            main_quest_set_status("unknown npc dialog stage=" .. tostring(params.stage or "") ..
                " type=" .. tostring(params.type_text or "") ..
                " content=" .. tostring(params.content_id or "") ..
                " npc_dialog_id=" .. tostring(params.npc_dialog_id or "") ..
                " expected_interact_id=" .. tostring(params.interact_id or ""))
        end
        return true
    end

    if name == "Idle" then
        return true
    end

    if main_quest_action_cooldown(name, 2.0) then
        main_quest_set_status("首个主线阶段未识别: " .. tostring(action.reason or name))
    end
    return true
end

function main_quest_20590_tick()
    if not runtime.running or runtime.paused then
        return
    end
    if not cfg.leveling or cfg.leveling.enabled ~= true then
        return
    end
    if primary_mode_ids[cfg.primary_mode] ~= "leveling" then
        return
    end
    if not ok_core or not core or not ok_main_quest_20590 or not main_quest_20590 then
        return
    end
    local now = now_seconds()
    if not main_quest_target_available("20590") then
        return
    end
    local state = main_quest_read_20590_state(now)
    local reward_dialog_open = type(main_quest_20590.isRewardDialog) == "function"
        and main_quest_20590.isRewardDialog(state.dialog)
    if runtime.main_quest and runtime.main_quest.completed_20590_reward == true
        and not reward_dialog_open then
        return
    end
    if ok_main_quest_20610 and main_quest_20610 then
        local quest_20610 = main_quest_cached_20610(now)
        if main_quest_20610.isQuestKnown(quest_20610)
            and (main_quest_20610.isQuestActive(quest_20610) or main_quest_20610.isQuestDone(quest_20610))
            and not reward_dialog_open then
            runtime.main_quest.completed_20590_reward = true
            return
        end
    end
    local route_stage = main_quest_active_route_stage()
    local action = main_quest_20590.nextAction(state, runtime.main_quest, {
        npc_range = 4,
        teleport_min_distance = 20,
        waypoint_range = 2,
        dialog_click_x = tonumber(cfg.npc_dialog and cfg.npc_dialog.auto_click_x) or 25,
        route_following_stage = route_stage,
        inner_final_move_done = runtime.main_quest.completed_20590_inner_final_move == true,
    })
    local dialog_sig = main_quest_dialog_signature(state.dialog)
    if dialog_sig ~= tostring(runtime.main_quest.last_dialog_signature or "") then
        main_quest_trace("dialog-change",
            "from=" .. tostring(runtime.main_quest.last_dialog_signature or "") ..
            " to=" .. dialog_sig ..
            " pos=" .. main_quest_position_text(state.char) ..
            " map=" .. tostring(state.big_map_id or "") ..
            " route_stage=" .. tostring(route_stage),
            0)
        runtime.main_quest.last_dialog_signature = dialog_sig
    end
    local decision_sig = tostring(action.name or "") ..
        ":" .. tostring(action.params and action.params.stage or "") ..
        ":" .. dialog_sig
    if decision_sig ~= tostring(runtime.main_quest.last_decision_signature or "") then
        main_quest_trace("decision",
            "action=" .. tostring(action.name or "") ..
            " stage=" .. tostring(action.params and action.params.stage or "") ..
            " reason=" .. tostring(action.reason or "") ..
            " dialog=" .. dialog_sig ..
            " pos=" .. main_quest_position_text(state.char) ..
            " route_stage=" .. tostring(route_stage),
            0)
        runtime.main_quest.last_decision_signature = decision_sig
    end
    main_quest_execute_20590(action, state)
end

function main_quest_20610_tick()
    if not runtime.running or runtime.paused then
        return
    end
    if not cfg.leveling or cfg.leveling.enabled ~= true then
        return
    end
    if primary_mode_ids[cfg.primary_mode] ~= "leveling" then
        return
    end
    if not ok_core or not core or not ok_main_quest_20610 or not main_quest_20610 then
        return
    end
    if runtime.main_quest and runtime.main_quest.completed_20610_reward == true then
        return
    end
    local now = now_seconds()
    local blocked, block_reason = main_quest_later_tasks_blocked(now, "20610")
    if blocked then
        main_quest_trace("stage-gate:20610",
            "blocked=" .. tostring(block_reason or "") .. " run earlier task first",
            1.0)
        return
    end
    if not main_quest_target_available("20610") then
        return
    end
    local state = main_quest_read_20610_state(now)
    local route_stage = main_quest_active_route_stage()
    local action = main_quest_20610.nextAction(state, runtime.main_quest, {
        npc_range = 4,
        dialog_click_x = tonumber(cfg.npc_dialog and cfg.npc_dialog.auto_click_x) or 25,
        route_following_stage = route_stage,
    })
    local dialog_sig = main_quest_dialog_signature(state.dialog)
    if dialog_sig ~= tostring(runtime.main_quest.last_dialog_signature or "") then
        main_quest_trace("dialog-change",
            "quest=20610" ..
            " from=" .. tostring(runtime.main_quest.last_dialog_signature or "") ..
            " to=" .. dialog_sig ..
            " pos=" .. main_quest_position_text(state.char) ..
            " map=" .. tostring(state.big_map_id or "") ..
            " route_stage=" .. tostring(route_stage),
            0)
        runtime.main_quest.last_dialog_signature = dialog_sig
    end
    local decision_sig = "20610:" .. tostring(action.name or "") ..
        ":" .. tostring(action.params and action.params.stage or "") ..
        ":" .. dialog_sig
    if decision_sig ~= tostring(runtime.main_quest.last_decision_signature or "") then
        main_quest_trace("decision",
            "quest=20610" ..
            " action=" .. tostring(action.name or "") ..
            " stage=" .. tostring(action.params and action.params.stage or "") ..
            " reason=" .. tostring(action.reason or "") ..
            " dialog=" .. dialog_sig ..
            " pos=" .. main_quest_position_text(state.char) ..
            " route_stage=" .. tostring(route_stage),
            0)
        runtime.main_quest.last_decision_signature = decision_sig
    end
    main_quest_execute_20590(action, state)
end

function main_quest_20611_tick()
    if not runtime.running or runtime.paused then
        return
    end
    if not cfg.leveling or cfg.leveling.enabled ~= true then
        return
    end
    if primary_mode_ids[cfg.primary_mode] ~= "leveling" then
        return
    end
    if not ok_core or not core or not ok_main_quest_20611 or not main_quest_20611 then
        return
    end
    local now = now_seconds()
    local blocked, block_reason = main_quest_later_tasks_blocked(now, "20611")
    if blocked then
        if runtime.main_quest.active_20611_grind == true then
            main_quest_stop_20611_grind("quest-20611-blocked-" .. tostring(block_reason or ""), false)
        end
        main_quest_trace("stage-gate:20611",
            "blocked=" .. tostring(block_reason or "") .. " run earlier task first",
            1.0)
        return
    end
    if not main_quest_target_available("20611") then
        return
    end
    local state = main_quest_read_20611_state(now)
    local action = main_quest_20611.nextAction(state, runtime.main_quest, {
        grind_point_range = 10,
    })
    local remote_qid = tonumber(state.remote_reward_quest and state.remote_reward_quest.id) or 0
    local remote_status = tonumber(state.remote_reward_quest and state.remote_reward_quest.status_code) or 0
    local level_qid = tonumber(state.level_blocked_quest and state.level_blocked_quest.id) or 0
    local level_status = tonumber(state.level_blocked_quest and state.level_blocked_quest.status_code) or 0
    local level_required = tonumber(state.level_blocked_quest and state.level_blocked_quest.lv_num) or 0
    local char_level = tonumber(state.char and state.char.level) or 0
    local quest_20612_snapshot = nil
    local quest_20613_snapshot = nil
    if ok_main_quest_20611 and main_quest_20611 and type(main_quest_20611.findQuestById) == "function" then
        quest_20612_snapshot = main_quest_20611.findQuestById(state.quests, 20612)
        quest_20613_snapshot = main_quest_20611.findQuestById(state.quests, 20613)
    end
    local q20612_status = tonumber(quest_20612_snapshot and quest_20612_snapshot.status_code) or 0
    local q20612_step = tonumber(quest_20612_snapshot and quest_20612_snapshot.req_count) or 0
    local q20613_status = tonumber(quest_20613_snapshot and quest_20613_snapshot.status_code) or 0
    local q20613_step = tonumber(quest_20613_snapshot and quest_20613_snapshot.req_count) or 0
    local action_qid = tonumber(action.params and action.params.quest_id) or 0
    local action_name = tostring(action.name or "")
    local action_authorizes_grind = type(main_quest_action_authorizes_grind) == "function"
        and main_quest_action_authorizes_grind(action)
    local ui_state = type(state.ui) == "table" and state.ui or {}
    if action_qid == 20612
        and runtime.main_quest.active_20611_grind == true
        and not action_authorizes_grind then
        main_quest_stop_20611_grind("quest-20612-recorded-step", false)
    end
    local decision_sig = "20611:" .. action_name ..
        ":" .. tostring(action.params and action.params.stage or "") ..
        ":" .. tostring(action_qid) ..
        ":" .. tostring(state.quest and state.quest.status_code or "") ..
        ":" .. tostring(state.quest and state.quest.req_count or "") ..
        ":" .. tostring(remote_qid) ..
        ":" .. tostring(remote_status) ..
        ":" .. tostring(level_qid) ..
        ":" .. tostring(level_status) ..
        ":" .. tostring(level_required) ..
        ":" .. tostring(char_level) ..
        ":" .. tostring(q20612_status) ..
        ":" .. tostring(q20612_step) ..
        ":" .. tostring(q20613_status) ..
        ":" .. tostring(q20613_step) ..
        ":" .. tostring(runtime.main_quest.active_20611_grind_stage or "") ..
        ":" .. tostring(ui_state.quest_indicator_entry == true) ..
        ":" .. tostring(ui_state.quest_panel_visible == true) ..
        ":" .. tostring(ui_state.quest_detail_target_link_20611 == true) ..
        ":" .. tostring(ui_state.dictionary_teleport_to_npc == true) ..
        ":" .. tostring(runtime.main_quest.clicked_20611_indicator_title == true) ..
        ":" .. tostring(runtime.main_quest.clicked_20611_indicator_entry_name or "") ..
        ":" .. tostring(runtime.main_quest.clicked_20611_target_link == true) ..
        ":" .. tostring(runtime.main_quest.completed_20611_target_dialog == true) ..
        ":" .. tostring(runtime.main_quest.completed_20611_hotspot_teleport == true) ..
        ":" .. tostring(runtime.main_quest.reached_20612_start_point == true) ..
        ":" .. tostring(runtime.main_quest.completed_20612_start_dialog == true) ..
        ":" .. tostring(runtime.main_quest.completed_20612_task_teleport == true) ..
        ":" .. tostring(runtime.main_quest.completed_20612_reward_dialog == true)
    if decision_sig ~= tostring(runtime.main_quest.last_decision_20611_signature or "") then
        main_quest_trace("decision",
            "quest=20611" ..
            " action=" .. action_name ..
            " action_qid=" .. tostring(action_qid) ..
            " stage=" .. tostring(action.params and action.params.stage or "") ..
            " reason=" .. tostring(action.reason or "") ..
            " quest_status=" .. tostring(state.quest and state.quest.status_code or "") ..
            " quest_step=" .. tostring(state.quest and state.quest.req_count or "") ..
            " qblue_id=" .. tostring(remote_qid) ..
            " qblue_status=" .. tostring(remote_status) ..
            " qblue_step=" .. tostring(state.remote_reward_quest and state.remote_reward_quest.req_count or "") ..
            " qlevel_id=" .. tostring(level_qid) ..
            " qlevel_status=" .. tostring(level_status) ..
            " qlevel_required=" .. tostring(level_required) ..
            " char_level=" .. tostring(char_level) ..
            " q20612_status=" .. tostring(q20612_status) ..
            " q20612_step=" .. tostring(q20612_step) ..
            " q20613_status=" .. tostring(q20613_status) ..
            " q20613_step=" .. tostring(q20613_step) ..
            " grind_stage=" .. tostring(runtime.main_quest.active_20611_grind_stage or "") ..
            " ui_indicator_entry=" .. tostring(ui_state.quest_indicator_entry == true) ..
            " ui_panel=" .. tostring(ui_state.quest_panel_visible == true) ..
            " ui_link=" .. tostring(ui_state.quest_detail_target_link_20611 == true) ..
            " ui_dict=" .. tostring(ui_state.dictionary_teleport_to_npc == true) ..
            " clicked_indicator_title=" .. tostring(runtime.main_quest.clicked_20611_indicator_title == true) ..
            " clicked_indicator_name=" .. tostring(runtime.main_quest.clicked_20611_indicator_entry_name or "") ..
            " clicked_link=" .. tostring(runtime.main_quest.clicked_20611_target_link == true) ..
            " target_dialog_done=" .. tostring(runtime.main_quest.completed_20611_target_dialog == true) ..
            " hotspot_done=" .. tostring(runtime.main_quest.completed_20611_hotspot_teleport == true) ..
            " q20612_point_done=" .. tostring(runtime.main_quest.reached_20612_start_point == true) ..
            " q20612_start_done=" .. tostring(runtime.main_quest.completed_20612_start_dialog == true) ..
            " q20612_teleport_done=" .. tostring(runtime.main_quest.completed_20612_task_teleport == true) ..
            " q20612_reward_done=" .. tostring(runtime.main_quest.completed_20612_reward_dialog == true) ..
            " waiting_teleport=" .. tostring(runtime.main_quest.waiting_teleport == true) ..
            " teleport_qid=" .. tostring(runtime.main_quest.teleport_quest_id or 0) ..
            " teleport_stage=" .. tostring(runtime.main_quest.teleport_stage or "") ..
            " pos=" .. main_quest_position_text(state.char),
            0)
        runtime.main_quest.last_decision_20611_signature = decision_sig
    end
    if tostring(action.name or "") == "Idle"
        and main_quest_is_20611_blue_task_id(action.params and action.params.quest_id)
        and runtime.main_quest.active_20611_grind == true then
        main_quest_stop_20611_grind("quest-20611-no-blue-task-active", false)
    end
    if tostring(action.name or "") == "Idle"
        and (tostring(runtime.main_quest.active_20611_grind_stage or "") == "quest_20611_level_grind"
            or tostring(runtime.main_quest.active_20611_grind_stage or "") == "quest_20612_level_grind")
        and runtime.main_quest.active_20611_grind == true then
        main_quest_stop_20611_grind("quest-20611-level-grind-idle", false)
    end
    main_quest_execute_20590(action, state)
end

local route_start_selected
local route_stop_follow

local function start_bot()
    normalize_primary_mode()
    if primary_mode_ids[cfg.primary_mode] == "leveling" then
        cfg.leveling = cfg.leveling or {}
        if cfg.leveling.enabled ~= true then
            cfg.leveling.enabled = true
            set_event("自动练级已启用主线目标")
            log_info("[AionMainQuest20590] auto-enabled leveling quest runner on start")
        end
    end
    if type(save_config) == "function" then
        save_config()
    end
    target_refresh(true)
    if not target_require_ready_for_start() then
        runtime.running = false
        runtime.paused = false
        runtime.status = "已停止"
        runtime.active_mode = "none"
        runtime.ui_visible = true
        return
    end

    bootstrap_begin("启动前初始化")
    if cfg.audit.reset_on_start then
        audit_reset()
    end
    sync_combat_enabled_from_primary_mode()
    combat_reset_runtime("start")
    main_quest_reset_runtime("start")
    if primary_mode_ids[cfg.primary_mode] == "leveling" then
        main_quest_apply_startup_snapshot("start")
    end
    runtime.running = true
    runtime.paused = false
    runtime.status = "运行中"
    runtime.active_mode = primary_modes[cfg.primary_mode] or "unknown"
    combat_log("start-config",
        string.format("start pid=%s hwnd=%s primary=%s combat_enabled=%s combat_mode=%s radius=%s min=%s max=%s",
            tostring(cfg.target and cfg.target.pid or 0),
            tostring(cfg.target and cfg.target.hwnd or 0),
            tostring(cfg.primary_mode),
            tostring(cfg.combat and cfg.combat.enabled),
            tostring(cfg.combat and cfg.combat.mode),
            tostring(cfg.combat and cfg.combat.radius),
            tostring(cfg.combat and cfg.combat.min_level),
            tostring(cfg.combat and cfg.combat.max_level)),
        0,
        true)
    set_event("启动: " .. runtime.active_mode)
    if route_recovery_plan_start and primary_mode_ids[cfg.primary_mode] == "combat" then
        route_recovery_plan_start("script-start")
    end
end

local function stop_bot()
    if type(save_config) == "function" then
        save_config()
    end
    if route_stop_follow then
        route_stop_follow("脚本停止", false)
    end
    if route_recovery_clear then
        route_recovery_clear("script-stop")
    end
    combat_stop_runtime("script stop")
    runtime.running = false
    runtime.paused = false
    runtime.status = "已停止"
    runtime.active_mode = "none"
    runtime.ui_visible = true
    set_event("停止")
end

local function toggle_start_stop()
    if runtime.running then
        stop_bot()
    else
        start_bot()
    end
end

local function toggle_pause()
    if not runtime.running then
        set_event("未运行，无法暂停")
        return
    end

    runtime.paused = not runtime.paused
    if runtime.paused then
        combat_auto_off("pause", true)
    end
    runtime.status = runtime.paused and "paused" or "running"
    set_event(runtime.paused and "暂停" or "继续")
end

local function toggle_ui_visible()
    runtime.ui_visible = not runtime.ui_visible
    if runtime.ui_visible then
        target_refresh(true)
    end
    set_event(runtime.ui_visible and "显示窗口" or "隐藏窗口")
end

local function clone_value(value)
    if ok_profile_io and profile_io and profile_io.clone then
        return profile_io.clone(value)
    end

    if type(value) ~= "table" then
        return value
    end

    local out = {}
    for key, item in pairs(value) do
        out[clone_value(key)] = clone_value(item)
    end
    return out
end

local function merge_table(dst, src)
    if type(dst) ~= "table" or type(src) ~= "table" then
        return dst
    end

    for key, value in pairs(src) do
        if type(value) == "table" and type(dst[key]) == "table" then
            merge_table(dst[key], value)
        else
            dst[key] = clone_value(value)
        end
    end
    return dst
end

local function account_default()
    return {
        enabled = true,
        label = "",
        account = "",
        password = "",
        second_password = "",
        phone = "",
        note = "",
        target = {
            pid = 0,
            hwnd = 0,
            title = "",
            character_name = "",
        },
        server = {
            key = -1,
            server_id = 0,
            label = "",
            character_count = -1,
            character_name = "",
        },
        character = {
            race = tonumber(cfg.character and cfg.character.race) or 0,
            race_name = race_name_by_id(cfg.character and cfg.character.race, "天族"),
            job = tonumber(cfg.character and cfg.character.job) or 0x1,
            job_name = job_name_by_id(cfg.character and cfg.character.job, "剑星"),
            gender = -1,
            gender_name = "随机",
        },
        login = {
            status = "idle",
            requested = false,
            result = 0,
            message = "",
            last_at = 0,
            task_id = 0,
            worker_key = "",
        },
        runtime = {
            status = "idle",
            message = "",
            task_id = 0,
            worker_key = "",
            started_at = 0,
            updated_at = 0,
            bound_pid = 0,
            bound_hwnd = 0,
        },
        audit = {
            status = "idle",
            level = 0,
            kinah = 0,
            kinah_per_hour = 0,
            kills_per_hour = 0,
            kills = 0,
            map = "",
            runtime_seconds = 0,
            last_refresh_at = 0,
            last_error = "",
        },
    }
end

local function account_ensure_shape(account)
    if type(account) ~= "table" then
        account = {}
    end
    local merged = account_default()
    merge_table(merged, account)
    return merged
end

local function account_items()
    if type(cfg.accounts.items) ~= "table" then
        cfg.accounts.items = {}
    end
    for index, account in ipairs(cfg.accounts.items) do
        cfg.accounts.items[index] = account_ensure_shape(account)
    end
    return cfg.accounts.items
end

local function selected_account()
    local items = account_items()
    local index = math.max(1, math.min(#items, tonumber(runtime.accounts.selected_index) or 1))
    runtime.accounts.selected_index = index
    return items[index], index
end

function account_trim_text(value)
    local text = tostring(value or "")
    text = string.gsub(text, "^%s+", "")
    text = string.gsub(text, "%s+$", "")
    return text
end

function account_ensure_account_api()
    local state = runtime.accounts
    if not state.account_api_checked then
        state.account_api_checked = true
        state.account_api_ok, state.account_api = pcall(require, "aion.account")
    end
    return state.account_api_ok == true, state.account_api
end

function account_selected_server_index(account)
    local selected = account and account.server or {}
    local selected_key = tonumber(selected.key)
    if selected_key == nil or selected_key < 0 then
        return 1
    end
    return math.max(1, math.min(4, math.floor(selected_key) + 1))
end

function account_select_server_index(account, index)
    if type(account) ~= "table" then
        return false
    end

    index = math.max(1, math.min(4, tonumber(index) or 1))
    account.server = account.server or {}
    account.server.key = index - 1
    account.server.server_id = 0
    account.server.label = "服务器 " .. tostring(index)
    account.server.character_count = -1
    account.server.character_name = account_trim_text(account.server.character_name)
    return true
end

function account_server_character_name(account)
    if not account then
        return ""
    end
    account.server = account.server or {}
    account.server.character_name = account_trim_text(account.server.character_name)
    return account.server.character_name
end

function account_ensure_character(account)
    if type(account) ~= "table" then
        return nil
    end
    account.character = type(account.character) == "table" and account.character or {}
    account.character.race = tonumber(account.character.race)
        or tonumber(cfg.character and cfg.character.race)
        or 0
    account.character.race_name = account.character.race_name
        or race_name_by_id(account.character.race, "天族")
    account.character.job = tonumber(account.character.job)
        or tonumber(cfg.character and cfg.character.job)
        or 0x1
    account.character.job_name = account.character.job_name
        or job_name_by_id(account.character.job, "剑星")
    account.character.gender = tonumber(account.character.gender) or -1
    account.character.gender_name = account.character.gender_name or "随机"
    return account.character
end

function account_can_save_settings(account)
    if type(account) ~= "table" then
        return false, "保存失败: 未选择账号"
    end

    return true, nil
end

function draw_account_server_combo(account, id_suffix, input_width, show_hint)
    local state = runtime.accounts
    if type(account) ~= "table" then
        return
    end
    id_suffix = tostring(id_suffix or "")
    local width = tonumber(input_width) or 190

    imgui.set_next_item_width(width)
    local changed, val = imgui.combo("服务器" .. id_suffix, account_selected_server_index(account), state.server_labels)
    if changed then
        if account_select_server_index(account, val) then
            state.server_last_status = "已选择服务器: " .. tostring(account.server.label or "")
        end
    end

    account.server = account.server or {}
    imgui.set_next_item_width(width)
    changed, val = imgui.input_text("角色名" .. id_suffix, account.server.character_name or "")
    if changed then
        account.server.character_name = val
    end
    if show_hint ~= false and primary_mode_ids[cfg.primary_mode] == "leveling" then
        imgui.text_colored(0.92, 0.22, 0.08, 1.0, "如果当前服务器无角色  角色名必填")
    end
end

function draw_account_identity_fields(account, id_suffix, widths)
    if type(account) ~= "table" then
        return
    end
    id_suffix = tostring(id_suffix or "")
    widths = type(widths) == "table" and widths or {}
    local race_width = tonumber(widths.race_width) or 120
    local job_width = tonumber(widths.job_width) or 120
    local server_width = tonumber(widths.server_width) or 190
    local show_hint = widths.show_hint ~= false
    local character = account_ensure_character(account)
    local changed, val

    imgui.set_next_item_width(race_width)
    changed, val = imgui.combo("种族" .. id_suffix, find_option_index(race_options, character.race), race_names)
    if changed then
        local option = race_options[val] or race_options[1]
        character.race = option.id
        character.race_name = option.label
    end

    imgui.same_line()
    imgui.set_next_item_width(job_width)
    changed, val = imgui.combo("职业" .. id_suffix, find_option_index(job_options, character.job), job_names)
    if changed then
        local option = job_options[val] or job_options[1]
        character.job = option.id
        character.job_name = option.label
    end

    imgui.spacing()
    draw_account_server_combo(account, id_suffix, server_width, show_hint)
end

function account_select_configured_server(account)
    if type(account) ~= "table" then
        return false
    end

    local index = account_selected_server_index(account)
    account_select_server_index(account, index)

    if ok_core and core and type(core.ensureInit) == "function" then
        local init_ok, init_err = core.ensureInit(cfg.target and cfg.target.pid)
        if not init_ok then
            runtime.accounts.server_last_status = "自动选服失败: AionData 初始化失败 " .. tostring(init_err)
            set_event(runtime.accounts.server_last_status)
            return false
        end
    end

    local api_ok, account_api = account_ensure_account_api()
    if not api_ok or not account_api or type(account_api.selectServer) ~= "function" then
        runtime.accounts.server_last_status = "自动选服失败: aion.account 不可用"
        set_event(runtime.accounts.server_last_status)
        return false
    end

    local server_key = tonumber(account.server and account.server.key) or 0
    local ok, selected, err = account_api.selectServer(server_key)
    if not ok or selected == false then
        runtime.accounts.server_last_status = "自动选服失败: " .. tostring(err or selected)
        set_event(runtime.accounts.server_last_status)
        return false
    end

    runtime.accounts.server_last_status = "已自动选择" .. tostring(account.server.label or ("服务器 " .. tostring(index)))
    set_event(runtime.accounts.server_last_status)
    return true
end

local function mask_secret(value)
    local text = tostring(value or "")
    if text == "" then
        return ""
    end
    return string.rep("*", math.min(8, #text))
end

local function mask_phone(value)
    local text = tostring(value or "")
    if #text <= 4 then
        return text
    end
    return string.rep("*", math.max(0, #text - 4)) .. string.sub(text, -4)
end

local function account_display_name(account)
    if not account then
        return ""
    end
    if tostring(account.label or "") ~= "" then
        return tostring(account.label)
    end
    return tostring(account.account or "")
end

function account_pid_is_alive(pid)
    pid = tonumber(pid) or 0
    if pid <= 0 then
        return false
    end
    if not ok_target or not target_lib or type(target_lib.list_candidates) ~= "function" then
        return true
    end

    local ok, candidates = target_lib.list_candidates({ selected_pid = pid })
    if not ok or type(candidates) ~= "table" then
        return true
    end

    for _, candidate in ipairs(candidates) do
        if tonumber(candidate.pid) == pid then
            local process_name = string.lower(tostring(candidate.process_name or ""))
            process_name = process_name:gsub("\\", "/")
            process_name = process_name:match("([^/]+)$") or process_name
            return process_name == "aion.bin" and candidate.alive ~= false
        end
    end
    return false
end

function account_clear_stale_target(account, stale_pid)
    if not account then
        return false
    end

    stale_pid = tonumber(stale_pid) or tonumber(account.target and account.target.pid) or 0
    account.target = account.target or {}
    account.runtime = account.runtime or {}
    account.login = account.login or {}
    account.audit = account.audit or {}

    account.target.pid = 0
    account.target.hwnd = 0
    account.target.title = ""
    account.target.process_name = ""
    account.target.class_name = ""
    account.target.path = ""
    account.target.character_name = ""

    account.runtime.status = "idle"
    account.runtime.message = stale_pid > 0 and ("game exited pid=" .. tostring(stale_pid)) or "game exited"
    account.runtime.task_id = 0
    account.runtime.worker_key = ""
    account.runtime.bound_pid = 0
    account.runtime.bound_hwnd = 0

    if account.login.requested ~= true then
        account.login.status = "idle"
        account.login.message = account.runtime.message
    end
    account.audit.status = "idle"

    if stale_pid > 0 and tonumber(cfg.target and cfg.target.pid) == stale_pid then
        cfg.target.pid = 0
        cfg.target.hwnd = 0
        cfg.target.title = ""
        cfg.target.process_name = ""
        cfg.target.class_name = ""
        cfg.target.path = ""
        cfg.target.character_name = ""
        runtime.target.bound_pid = 0
        runtime.target.bound_hwnd = 0
        runtime.target.binding_status = "not_selected"
        runtime.target.binding_message = "target game exited"
    end

    return true
end

local function account_save_domain()
    if not config or type(config.set) ~= "function" or type(config.save) ~= "function" then
        return
    end
    pcall(function()
        if type(config.load) == "function" then
            config.load()
        end
        config.set("aion_control.accounts", cfg.accounts)
        config.save()
    end)
end

local function account_login_share_key(worker_key, index, field)
    return "aion_login." .. tostring(worker_key or "") .. "." .. tostring(index) .. "." .. tostring(field)
end

function account_login_queue_key(worker_key, field)
    return "aion_login." .. tostring(worker_key or "") .. ".queue." .. tostring(field or "")
end

function account_login_queue_value(value)
    if value == nil or value == true or value == false then
        return ""
    end
    return tostring(value)
end

function account_publish_login_queue(worker_key, selected_index)
    if not sys or type(sys.set_share) ~= "function" then
        runtime.accounts.last_status = "sys share unavailable"
        return false
    end

    selected_index = tonumber(selected_index) or 0
    local queue_count = 0

    sys.set_share(account_login_queue_key(worker_key, "game_path"), account_login_queue_value(cfg.accounts.game_path))
    sys.set_share(account_login_queue_key(worker_key, "purple_root"), account_login_queue_value(cfg.accounts.purple_root))
    sys.set_share(account_login_queue_key(worker_key, "dll_path"), account_login_queue_value(cfg.accounts.dll_path))
    sys.set_share(account_login_queue_key(worker_key, "lang"), account_login_queue_value(cfg.accounts.lang))
    sys.set_share(account_login_queue_key(worker_key, "captcha_key"), account_login_queue_value(cfg.accounts.captcha_key))
    sys.set_share(account_login_queue_key(worker_key, "decode_mail"), account_login_queue_value(cfg.accounts.decode_mail))
    sys.set_share(account_login_queue_key(worker_key, "pid_wait_seconds"), tonumber(cfg.accounts.pid_wait_seconds) or 60)
    sys.set_share(account_login_queue_key(worker_key, "login_gap_ms"), tonumber(cfg.accounts.login_gap_ms) or 1500)
    sys.set_share(account_login_queue_key(worker_key, "post_init_timeout_seconds"), 120)
    sys.set_share(account_login_queue_key(worker_key, "server_select_timeout_seconds"), 90)
    sys.set_share(account_login_queue_key(worker_key, "character_select_timeout_seconds"), 90)
    sys.set_share(account_login_queue_key(worker_key, "enter_game_timeout_seconds"), 120)
    sys.set_share(account_login_queue_key(worker_key, "agreement_timeout_seconds"), 20)
    sys.set_share(account_login_queue_key(worker_key, "agreement_retry_interval_ms"), 500)
    sys.set_share(account_login_queue_key(worker_key, "create_character_recheck_timeout_seconds"), 20)
    sys.set_share(account_login_queue_key(worker_key, "create_character_recheck_interval_ms"), 1000)
    sys.set_share(account_login_queue_key(worker_key, "create_character_max_attempts"), 4)

    for index, account in ipairs(account_items()) do
        local selected = selected_index > 0 and index == selected_index
        local queued = selected_index <= 0 and account.login and account.login.requested == true
        if selected or queued then
            queue_count = queue_count + 1
            local prefix = "item." .. tostring(queue_count) .. "."
            sys.set_share(account_login_queue_key(worker_key, prefix .. "index"), index)
            sys.set_share(account_login_queue_key(worker_key, prefix .. "account"), account_login_queue_value(account.account))
            sys.set_share(account_login_queue_key(worker_key, prefix .. "password"), account_login_queue_value(account.password))
            sys.set_share(account_login_queue_key(worker_key, prefix .. "second_password"), account_login_queue_value(account.second_password))
            sys.set_share(account_login_queue_key(worker_key, prefix .. "phone"), account_login_queue_value(account.phone))
            sys.set_share(account_login_queue_key(worker_key, prefix .. "label"), account_login_queue_value(account_display_name(account)))
            sys.set_share(account_login_queue_key(worker_key, prefix .. "server_key"), tonumber(account.server and account.server.key) or -1)
            sys.set_share(account_login_queue_key(worker_key, prefix .. "server_id"), tonumber(account.server and account.server.server_id) or 0)
            sys.set_share(account_login_queue_key(worker_key, prefix .. "character_name"), account_login_queue_value(account.server and account.server.character_name))
            local character = account_ensure_character(account)
            sys.set_share(account_login_queue_key(worker_key, prefix .. "race"), tonumber(character and character.race) or 0)
            sys.set_share(account_login_queue_key(worker_key, prefix .. "job"), tonumber(character and character.job) or 0)
            sys.set_share(account_login_queue_key(worker_key, prefix .. "gender"), tonumber(character and character.gender) or -1)
        end
    end

    sys.set_share(account_login_queue_key(worker_key, "count"), queue_count)
    return queue_count > 0
end

function account_login_module_available()
    local paths = {
        "scripts/AionLogin.lua",
        "scripts/AionLogin.luac",
        "AionLogin.lua",
        "AionLogin.luac",
    }

    if not io or type(io.open) ~= "function" then
        return true
    end

    for _, path in ipairs(paths) do
        local file = io.open(path, "rb")
        if file then
            file:close()
            return true
        end
    end

    log_warn("[AionControlUI] AionLogin preflight not found by io.open; login worker will validate module")
    return true
end

function account_fail_queued_login(index, message)
    index = tonumber(index) or 0
    for item_index, account in ipairs(account_items()) do
        local target = index == 0 and account.login and account.login.requested == true
        target = target or item_index == index
        if target and account.login then
            account.login.status = "error"
            account.login.message = tostring(message or "login failed")
            account.login.requested = false
            account.login.result = 0
            account.login.last_at = now_seconds()
        end
    end
end

local function account_runtime_share_key(worker_key, field)
    return "aion_runtime." .. tostring(worker_key or "") .. "." .. tostring(field)
end

local function account_make_worker_key()
    return tostring(os.time()) .. "_" .. tostring(math.floor((now_seconds() % 100000) * 1000))
end

function account_current_aion_pid_text()
    if not ok_target or not target_lib or type(target_lib.list_candidates) ~= "function" then
        return ""
    end

    local ok, candidates = target_lib.list_candidates({})
    if not ok or type(candidates) ~= "table" then
        return ""
    end

    local pids = {}
    for _, candidate in ipairs(candidates) do
        local pid = tonumber(candidate.pid) or 0
        if pid > 0 then
            pids[#pids + 1] = tostring(pid)
        end
    end
    table.sort(pids)
    return table.concat(pids, ",")
end

function account_start_agreement_watcher(index, worker_key)
    if not task or type(task.run) ~= "function" then
        log_warn("[AionControlUI] agreement watcher unavailable: task.run missing")
        return false
    end

    worker_key = tostring(worker_key or "")
    if worker_key == "" then
        return false
    end

    local known_pids = account_current_aion_pid_text()
    local timeout = math.max(30, tonumber(cfg.accounts and cfg.accounts.pid_wait_seconds) or 60)
    local id = task.run("scripts/aion_login_agreement_worker.lua", {
        name = "AionLoginAgreement_UI_" .. tostring(index or 0),
        priority = "normal",
        queue_id = worker_key,
        account_index = tostring(index or 0),
        known_pids = known_pids,
        timeout_seconds = tostring(timeout),
        poll_interval_ms = "250",
    })
    runtime.accounts.agreement_worker_task_id = tonumber(id) or 0
    log_info("[AionControlUI] agreement watcher started id=" .. tostring(id) ..
        " index=" .. tostring(index or 0) ..
        " known_pids=" .. tostring(known_pids))
    return id ~= nil
end

local function account_worker_is_running()
    local id = tonumber(runtime.accounts.worker_task_id) or 0
    if id <= 0 or not task or type(task.status) ~= "function" then
        return false
    end

    local status = task.status(id)
    return status == "pending" or status == "running" or status == "paused"
end

local function account_start_login_worker(index, worker_key)
    if not task or type(task.run) ~= "function" then
        runtime.accounts.last_status = "task module unavailable"
        set_event("登录 worker 启动失败: task 模块不可用")
        return false
    end

    if account_worker_is_running() then
        runtime.accounts.last_status = "login worker already running"
        set_event("登录 worker 已在运行")
        return false
    end

    worker_key = worker_key or account_make_worker_key()
    runtime.accounts.worker_queue_id = worker_key
    local module_ok, module_err = account_login_module_available()
    if not module_ok then
        account_fail_queued_login(index, module_err)
        runtime.accounts.last_status = tostring(module_err)
        set_event("login worker start failed: " .. tostring(module_err))
        return false
    end
    if not account_publish_login_queue(worker_key, tonumber(index) or 0) then
        runtime.accounts.last_status = "login queue is empty"
        set_event("登录 worker 启动失败: 登录队列为空")
        return false
    end

    local id = task.run("scripts/aion_login_worker.lua", {
        name = "AionLoginWorker",
        priority = "normal",
        queue_id = worker_key,
        account_index = tostring(index or 0),
    })

    runtime.accounts.worker_task_id = tonumber(id) or 0
    runtime.accounts.last_status = "worker started id=" .. tostring(id)
    set_event("登录 worker 已启动id=" .. tostring(id))
    return id ~= nil
end

local function account_task_is_running(id)
    id = tonumber(id) or 0
    if id <= 0 or not task or type(task.status) ~= "function" then
        return false
    end
    local status = task.status(id)
    return status == "pending" or status == "running" or status == "paused"
end

local function account_start_runtime_worker(account, index)
    if not account then
        return false
    end
    if not task or type(task.run) ~= "function" then
        account.runtime.status = "error"
        account.runtime.message = "task module unavailable"
        set_event("运行 worker 启动失败: task 模块不可用")
        return false
    end
    if account_task_is_running(account.runtime and account.runtime.task_id) then
        set_event("运行 worker 已在运行: " .. account_display_name(account))
        return false
    end
    if tonumber(account.target and account.target.pid) == nil or tonumber(account.target.pid) <= 0 then
        account.runtime.status = "error"
        account.runtime.message = "请先绑定 PID"
        set_event("启动失败: 账号未绑定PID")
        return false
    end

    local worker_key = "run_" .. account_make_worker_key()
    local id = task.run("scripts/aion_runtime_worker.lua", {
        name = "AionRuntime_" .. tostring(account_display_name(account)),
        priority = "normal",
        runtime_key = worker_key,
        account_index = tostring(index or 0),
    })

    account.runtime.worker_key = worker_key
    account.runtime.task_id = tonumber(id) or 0
    account.runtime.status = "starting"
    account.runtime.message = "runtime worker started id=" .. tostring(id)
    account_save_domain()
    set_event("运行 worker 已启动" .. account_display_name(account))
    return id ~= nil
end

local function account_stop_runtime_worker(account)
    if not account or not account.runtime then
        return false
    end

    local id = tonumber(account.runtime.task_id) or 0
    if id > 0 and task and type(task.stop) == "function" then
        task.stop(id)
    end
    account.runtime.status = "stopping"
    account.runtime.message = "stop requested"
    account_save_domain()
    set_event("运行 worker 停止请求: " .. account_display_name(account))
    return true
end

function account_maybe_auto_start_after_login(index, account)
    if not ok_login_autostart or not login_autostart or type(login_autostart.decide) ~= "function" then
        return false
    end

    local decision = login_autostart.decide({
        cfg = cfg,
        account = account,
        runtime = runtime,
        is_task_running = account_task_is_running,
    })

    if decision.action == "start" then
        runtime.accounts.last_status = "login ready; auto start queued: " .. account_display_name(account)
        set_event("login ready; auto start queued: " .. account_display_name(account))
        if type(account_queue_local_script) == "function" then
            return account_queue_local_script("start", account, index) == true
        end
        runtime.accounts.last_status = "auto start failed: account_queue_local_script unavailable"
        return false
    end

    if decision.action == "block" then
        local message = tostring(decision.message or decision.reason or "auto start blocked")
        runtime.accounts.last_status = message
        set_event(message)
        return true
    end

    return false
end

local function account_update_from_worker(index, account)
    if not account or not account.login then
        return false
    end

    local worker_key = tostring(account.login.worker_key or "")
    if worker_key == "" or not sys or type(sys.get_share) ~= "function" then
        return false
    end

    local changed = false
    local status = sys.get_share(account_login_share_key(worker_key, index, "status"))
    if status ~= nil and status ~= account.login.status then
        account.login.status = tostring(status)
        changed = true
    end

    local message = sys.get_share(account_login_share_key(worker_key, index, "message"))
    if message ~= nil and message ~= account.login.message then
        account.login.message = tostring(message)
        changed = true
    end

    local ret = sys.get_share(account_login_share_key(worker_key, index, "ret"))
    if ret ~= nil and tonumber(ret) ~= tonumber(account.login.result) then
        account.login.result = tonumber(ret) or 0
        changed = true
    end

    local updated_at = sys.get_share(account_login_share_key(worker_key, index, "updated_at"))
    if updated_at ~= nil and tonumber(updated_at) ~= tonumber(account.login.last_at) then
        account.login.last_at = tonumber(updated_at) or account.login.last_at
        changed = true
    end

    local pid = tonumber(sys.get_share(account_login_share_key(worker_key, index, "pid"))) or 0
    if pid > 0 and pid ~= tonumber(account.target.pid) then
        account.target.pid = pid
        changed = true
    end

    local hwnd = tonumber(sys.get_share(account_login_share_key(worker_key, index, "hwnd"))) or 0
    if hwnd > 0 and hwnd ~= tonumber(account.target.hwnd) then
        account.target.hwnd = hwnd
        changed = true
    end

    local title = sys.get_share(account_login_share_key(worker_key, index, "title"))
    if title ~= nil and title ~= account.target.title then
        account.target.title = tostring(title)
        changed = true
    end

    local character_name = sys.get_share(account_login_share_key(worker_key, index, "character_name"))
    if character_name ~= nil and tostring(character_name) ~= tostring(account.target.character_name or "") then
        account.target.character_name = tostring(character_name)
        changed = true
    end

    local level = sys.get_share(account_login_share_key(worker_key, index, "level"))
    if level ~= nil and tonumber(level) ~= tonumber(account.audit.level) then
        account.audit.level = tonumber(level) or 0
        changed = true
    end

    local race = sys.get_share(account_login_share_key(worker_key, index, "race"))
    local job = sys.get_share(account_login_share_key(worker_key, index, "job"))
    if race ~= nil or job ~= nil then
        local character = account_ensure_character(account)
        local race_id = tonumber(race) or tonumber(character.race) or 0
        local job_id = tonumber(job) or tonumber(character.job) or 0
        if race ~= nil and race_id ~= tonumber(character.race) then
            character.race = race_id
            character.race_name = race_name_by_id(race_id, character.race_name)
            changed = true
        end
        if job_id > 0 and job_id ~= tonumber(character.job) then
            character.job = job_id
            character.job_name = job_name_by_id(job_id, character.job_name)
            changed = true
        end
    end

    if (tonumber(account.target.pid) or 0) > 0 then
        local select_current = (tonumber(cfg.target.pid) or 0) <= 0 or tonumber(runtime.accounts.selected_index) == tonumber(index)
        if select_current then
            if tonumber(cfg.target.pid) ~= tonumber(account.target.pid) then
                cfg.target.pid = tonumber(account.target.pid) or 0
                changed = true
            end
            if tonumber(cfg.target.hwnd) ~= tonumber(account.target.hwnd) then
                cfg.target.hwnd = tonumber(account.target.hwnd) or 0
                changed = true
            end
            if tostring(cfg.target.title or "") ~= tostring(account.target.title or "") then
                cfg.target.title = tostring(account.target.title or "")
                changed = true
            end
            if tostring(cfg.target.character_name or "") ~= tostring(account.target.character_name or "") then
                cfg.target.character_name = tostring(account.target.character_name or "")
                changed = true
            end
        end

        if account.login.status == "ready" and not runtime.bootstrap.pending and not runtime.bootstrap.initialized then
            bootstrap_begin("登录后初始化")
        end
    end

    local done = sys.get_share(account_login_share_key(worker_key, index, "done"))
    if done == true and account.login.requested ~= false then
        account.login.requested = false
        changed = true
    end

    if changed and (account.login.status == "ready" or account.login.status == "error" or account.login.status == "game_started") then
        account.login.requested = false
    end

    if changed and account_maybe_auto_start_after_login(index, account) then
        changed = true
    end

    return changed
end

local function account_update_from_runtime_worker(account)
    if not account or not account.runtime then
        return false
    end
    local worker_key = tostring(account.runtime.worker_key or "")
    if worker_key == "" or not sys or type(sys.get_share) ~= "function" then
        return false
    end

    local changed = false
    local function update_string(target_table, target_key, share_field)
        local value = sys.get_share(account_runtime_share_key(worker_key, share_field))
        if value ~= nil and tostring(value) ~= tostring(target_table[target_key] or "") then
            target_table[target_key] = tostring(value)
            changed = true
        end
    end
    local function update_number(target_table, target_key, share_field)
        local value = sys.get_share(account_runtime_share_key(worker_key, share_field))
        if value ~= nil and tonumber(value) ~= tonumber(target_table[target_key]) then
            target_table[target_key] = tonumber(value) or 0
            changed = true
        end
    end

    update_string(account.runtime, "status", "status")
    update_string(account.runtime, "message", "message")
    update_number(account.runtime, "updated_at", "updated_at")
    update_number(account.runtime, "started_at", "started_at")
    update_number(account.runtime, "bound_pid", "bound_pid")
    update_number(account.runtime, "bound_hwnd", "bound_hwnd")

    update_string(account.target, "character_name", "character_name")
    update_number(account.audit, "level", "level")
    update_number(account.audit, "kinah", "kinah")
    update_number(account.audit, "runtime_seconds", "runtime_seconds")
    update_string(account.audit, "map", "map")

    if account.runtime.status == "running" then
        account.audit.status = "running"
    elseif account.runtime.status == "stopped" or account.runtime.status == "error" then
        account.audit.status = account.runtime.status
    end

    return changed
end

function account_open_add_window()
    local draft = account_default()
    draft.account = ""
    draft.password = ""
    draft.second_password = ""
    draft.label = ""
    account_select_server_index(draft, 1)
    runtime.accounts.add_draft = draft
    runtime.accounts.add_force_size = true
    runtime.accounts.add_window_visible = true
end

function account_confirm_add_window()
    local draft = account_ensure_shape(runtime.accounts.add_draft or {})
    local account_name = tostring(draft.account or "")
    local password = tostring(draft.password or "")
    local second_password = tostring(draft.second_password or "")
    if account_name == "" or password == "" then
        runtime.accounts.last_status = "新增账号失败: 账号或密码为空"
        return false
    end

    local account = draft
    account.account = account_name
    account.password = password
    account.second_password = second_password
    account.label = account_name
    account.server = account.server or {}
    account.server.character_name = account_trim_text(account.server.character_name)
    account_ensure_character(account)

    local items = account_items()
    table.insert(items, account)
    runtime.accounts.selected_index = #items
    runtime.accounts.add_window_visible = false
    runtime.accounts.add_draft = nil
    runtime.accounts.add_force_size = false
    runtime.accounts.settings_window_visible = false
    runtime.accounts.last_status = "新增账号: " .. account_name
    account_save_domain()
    return true
end

function draw_account_add_window()
    if not runtime.accounts.add_window_visible then
        return
    end

    local size_cond = runtime.accounts.add_force_size and (imgui.Cond_Always or imgui.Cond_FirstUseEver)
        or imgui.Cond_FirstUseEver
    imgui.set_next_window_size(560, 330, size_cond)
    imgui.set_next_window_pos(260, 180, imgui.Cond_FirstUseEver)
    local visible, open = imgui.begin_window("新增账号###aion_add_account_window", true, imgui.WindowFlags_NoCollapse)
    if open == false then
        runtime.accounts.add_window_visible = false
        runtime.accounts.add_draft = nil
        runtime.accounts.add_force_size = false
    end
    if visible then
        runtime.accounts.add_force_size = false
        if type(runtime.accounts.add_draft) ~= "table" then
            runtime.accounts.add_draft = account_default()
            account_select_server_index(runtime.accounts.add_draft, 1)
        end
        local draft = runtime.accounts.add_draft
        local changed, val
        imgui.set_next_item_width(360)
        changed, val = imgui.input_text("账号", draft.account)
        if changed then draft.account = val end

        imgui.set_next_item_width(360)
        changed, val = imgui.input_text("密码", draft.password)
        if changed then draft.password = val end

        imgui.set_next_item_width(360)
        changed, val = imgui.input_text("二级密码", draft.second_password)
        if changed then draft.second_password = val end

        imgui.spacing()
        draw_account_identity_fields(draft, "##add_account", {
            race_width = 150,
            job_width = 150,
            server_width = 360,
            show_hint = false,
        })
        imgui.text_colored(0.92, 0.22, 0.08, 1.0, "如果当前服务器没有角色  角色名必填(避免重复)")

        imgui.spacing()
        if imgui.button("确认", 90, 26) then
            account_confirm_add_window()
        end
        imgui.same_line()
        if imgui.button("取消", 90, 26) then
            runtime.accounts.add_window_visible = false
            runtime.accounts.add_draft = nil
            runtime.accounts.add_force_size = false
        end
    end
    imgui.end_window()
end

local function account_remove_selected()
    local items = account_items()
    local index = tonumber(runtime.accounts.selected_index) or 0
    if index <= 0 or index > #items then
        return
    end
    table.remove(items, index)
    runtime.accounts.selected_index = math.max(1, math.min(index, #items))
    account_save_domain()
end

local function account_select(index, open_settings)
    local items = account_items()
    if index < 1 or index > #items then
        return
    end
    runtime.accounts.selected_index = index
    if open_settings then
        runtime.accounts.settings_window_visible = true
    end
end

local function account_parse_import_line(line)
    local parts = {}
    for item in string.gmatch(line or "", "[^,%s\t]+") do
        table.insert(parts, item)
    end
    if #parts == 0 then
        return nil
    end
    local account = account_default()
    account.account = parts[1] or ""
    account.password = parts[2] or ""
    account.second_password = parts[3] or ""
    account.label = account.account
    return account
end

local function account_import_text()
    local added = 0
    local items = account_items()
    for line in string.gmatch(runtime.accounts.import_text or "", "[^\r\n]+") do
        local account = account_parse_import_line(line)
        if account and account.account ~= "" then
            table.insert(items, account)
            added = added + 1
        end
    end
    if added > 0 then
        runtime.accounts.selected_index = #items
        runtime.accounts.import_text = ""
        account_save_domain()
    end
    runtime.accounts.last_status = "imported accounts=" .. tostring(added)
    set_event("账号导入完成: " .. tostring(added))
end

local function account_index_of(account)
    for index, item in ipairs(cfg.accounts.items or {}) do
        if item == account then
            return index
        end
    end
    return 0
end

local function account_queue_login(account, index, worker_key)
    if not account then
        return false
    end
    index = tonumber(index) or account_index_of(account)
    if tostring(cfg.accounts.game_path or "") == "" or tostring(cfg.accounts.purple_root or "") == "" then
        account.login.status = "error"
        account.login.message = "game_path or purple_root is empty"
        account.login.requested = false
        runtime.accounts.last_status = "login rejected: game_path or purple_root is empty"
        account_save_domain()
        return false
    end
    if tostring(account.account or "") == "" or tostring(account.password or "") == "" then
        account.login.status = "error"
        account.login.message = "account or password is empty"
        account.login.requested = false
        runtime.accounts.last_status = "login rejected: account or password is empty"
        account_save_domain()
        return false
    end
    worker_key = worker_key or account_make_worker_key()
    account.login.requested = true
    account.login.status = "queued"
    account.login.last_at = now_seconds()
    account.login.worker_key = worker_key
    account.login.message = "queued for login worker"
    runtime.accounts.last_status = "login queued: " .. account_display_name(account)
    account_save_domain()
    return index > 0
end

local function account_request_login(account, index)
    index = tonumber(index) or account_index_of(account)
    if account_worker_is_running() then
        runtime.accounts.last_status = "login worker already running"
        set_event("登录 worker 正在运行，请等待完成后再登录其他账号")
        return
    end
    local worker_key = account_make_worker_key()
    if not account_queue_login(account, index, worker_key) then
        return
    end
    runtime.accounts.pending_login = {
        index = index,
        worker_key = worker_key,
    }
    account.login.message = "pending login worker"
    runtime.accounts.last_status = "login pending: " .. account_display_name(account)
    account_save_domain()
    set_event("登录任务已提交" .. account_display_name(account))
end

local function account_request_login_all()
    if account_worker_is_running() then
        runtime.accounts.last_status = "login worker already running"
        set_event("登录 worker 正在运行，请等待完成后再全部登录")
        return
    end

    local count = 0
    local worker_key = account_make_worker_key()
    for index, account in ipairs(account_items()) do
        if account.enabled and tostring(account.account or "") ~= "" then
            if account_queue_login(account, index, worker_key) then
                count = count + 1
            end
        end
    end
    runtime.accounts.last_status = "login queued count=" .. tostring(count)
    if count > 0 then
        runtime.accounts.pending_login = {
            index = 0,
            worker_key = worker_key,
        }
    end
    account_save_domain()
end

function account_process_pending_login()
    local pending = runtime.accounts.pending_login
    if type(pending) ~= "table" then
        return
    end
    if account_worker_is_running() then
        return
    end

    runtime.accounts.pending_login = nil

    local index = tonumber(pending.index) or 0
    local worker_key = tostring(pending.worker_key or "")
    if worker_key == "" then
        worker_key = account_make_worker_key()
    end

    account_start_agreement_watcher(index, worker_key)

    if account_start_login_worker(index, worker_key) then
        for item_index, account in ipairs(account_items()) do
            if index == 0 or item_index == index then
                if account.login and account.login.requested then
                    account.login.task_id = runtime.accounts.worker_task_id
                end
            end
        end
        account_save_domain()
    end
end

function account_login_flow_active()
    if type(runtime.accounts.pending_login) == "table" then
        return true
    end
    if account_worker_is_running() then
        return true
    end

    for _, account in ipairs(account_items()) do
        local login = account.login or {}
        local status = tostring(login.status or "")
        if login.requested == true
            or status == "queued"
            or status == "logging_in"
            or status == "agreement_recover"
            or status == "waiting_pid"
            or status == "game_detected" then
            return true
        end
    end
    return false
end

function account_ui_obj_id(obj)
    if type(obj) ~= "table" then
        return tonumber(obj) or 0
    end
    return tonumber(obj.addr) or tonumber(obj.obj) or tonumber(obj.node) or 0
end

function account_ui_visible(obj)
    if type(obj) ~= "table" then
        return false
    end
    if obj.visible == false then
        return false
    end
    return account_ui_obj_id(obj) > 0 or obj.visible == true
end

function account_agreement_page_visible()
    local scene_text = "unknown"
    if ok_core and core and type(core.getScene) == "function" then
        local scene_call_ok, scene_ok, scene, scene_err = pcall(core.getScene)
        if not scene_call_ok then
            local err_text = scene_ok
            scene_ok = false
            scene_err = err_text
        end
        if scene_ok and type(scene) == "table" then
            local idx = tonumber(scene.index) or -1
            scene_text = "idx=" .. tostring(scene.index) .. " name=" .. tostring(scene.name or "")
            if idx == 0x8 or idx == 0x9 then
                return true, scene_text
            end
        else
            scene_text = "failed err=" .. tostring(scene_err)
        end
    end

    local ok_ui_runtime, ui_runtime = pcall(require, "aion.ui")
    if not ok_ui_runtime or not ui_runtime or type(ui_runtime.find) ~= "function" then
        return false, scene_text
    end

    local dialog_call_ok, find_ok, dialog = pcall(ui_runtime.find, "user_agreement_dialog")
    if not dialog_call_ok then
        find_ok = false
    end
    if find_ok and account_ui_visible(dialog) then
        return true, scene_text .. " dialog=user_agreement_dialog"
    end

    local button_call_ok, btn_ok, button = pcall(ui_runtime.find, "agreement_yes")
    if not button_call_ok then
        btn_ok = false
    end
    if btn_ok and account_ui_visible(button) then
        return true, scene_text .. " button=agreement_yes"
    end

    return false, scene_text
end

function account_agreement_click_tick()
    if not account_login_flow_active() then
        return
    end

    local now = now_seconds()
    if now - (tonumber(runtime.accounts.agreement_last_attempt_at) or 0) < 0.5 then
        return
    end
    runtime.accounts.agreement_last_attempt_at = now

    local visible, visible_context = account_agreement_page_visible()
    if not visible then
        local context = tostring(visible_context or "")
        if context ~= "" and context ~= tostring(runtime.accounts.agreement_last_scene or "") then
            runtime.accounts.agreement_last_scene = context
            log_info("[AionControlUI] agreement tick waiting scene " .. context)
        end
        return
    end

    local ok_login_flow, login_flow = pcall(require, "aion.login_flow")
    if not ok_login_flow or not login_flow or type(login_flow.acceptAgreement) ~= "function" then
        return
    end

    local ctx = runtime.accounts.agreement_tick_ctx
    if type(ctx) ~= "table" then
        ctx = {}
        runtime.accounts.agreement_tick_ctx = ctx
    end
    ctx.index = tonumber(runtime.accounts.selected_index) or 0
    ctx.sleep = function(ms)
        if sys and type(sys.sleep) == "function" then
            sys.sleep(math.min(50, tonumber(ms) or 0))
        end
    end
    ctx.now_ms = function()
        if sys and type(sys.time) == "function" then
            return sys.time()
        end
        return os.time() * 1000
    end

    local call_ok, ok, clicked_or_absent = pcall(login_flow.acceptAgreement, ctx, 0, 50)
    if not call_ok then
        local err_text = tostring(ok or "")
        if err_text ~= tostring(runtime.accounts.agreement_last_error or "") then
            runtime.accounts.agreement_last_error = err_text
            log_warn("[AionControlUI] agreement tick interrupted: " .. err_text)
        end
        return
    end

    if ok and clicked_or_absent then
        runtime.accounts.agreement_last_error = ""
        log_info("[AionControlUI] agreement tick clicked agreement_yes")
    elseif not ok then
        local err_text = tostring(clicked_or_absent or "")
        if err_text ~= tostring(runtime.accounts.agreement_last_error or "") then
            runtime.accounts.agreement_last_error = err_text
            log_warn("[AionControlUI] agreement tick failed: " .. err_text)
        end
    end
end

local function account_start_runtime_all()
    local count = 0
    for index, account in ipairs(account_items()) do
        if account.enabled then
            if account_queue_local_script("start", account, index) then
                count = count + 1
            end
        end
    end
    runtime.accounts.last_status = "script start queued count=" .. tostring(count)
    set_event("全部启动已排队: " .. tostring(count))
end

local function account_stop_runtime_all()
    local count = 0
    for index, account in ipairs(account_items()) do
        if account_queue_local_script("stop", account, index) then
            count = count + 1
        end
    end
    runtime.accounts.last_status = "script stop queued count=" .. tostring(count)
    set_event("全部停止已排队: " .. tostring(count))
end

function account_enqueue_pending_script(action, index)
    local request = {
        action = tostring(action or ""),
        index = tonumber(index) or runtime.accounts.selected_index or 1,
    }

    if type(runtime.accounts.pending_script) ~= "table" then
        runtime.accounts.pending_script = request
        return
    end

    if type(runtime.accounts.pending_scripts) ~= "table" then
        runtime.accounts.pending_scripts = {}
    end
    table.insert(runtime.accounts.pending_scripts, request)
end

function account_pop_pending_script()
    local pending = runtime.accounts.pending_script
    if type(pending) == "table" then
        runtime.accounts.pending_script = nil
        return pending
    end

    local queue = runtime.accounts.pending_scripts
    if type(queue) == "table" and #queue > 0 then
        return table.remove(queue, 1)
    end

    return nil
end

function account_has_pending_script_request()
    if type(runtime.accounts.pending_script) == "table" then
        return true
    end
    return type(runtime.accounts.pending_scripts) == "table" and #runtime.accounts.pending_scripts > 0
end

function account_queue_local_script(action, account, index)
    if not account then
        runtime.accounts.last_status = "script " .. tostring(action) .. " queue failed: no account selected"
        set_event("脚本请求失败: 未选择账号")
        return false
    end

    local action_text = tostring(action or "")
    if action_text ~= "start" and action_text ~= "stop" then
        runtime.accounts.last_status = "script queue failed: bad action " .. tostring(action)
        set_event("脚本请求失败: 未知动作 " .. tostring(action))
        return false
    end

    local account_index = tonumber(index) or account_index_of(account)
    if account_index <= 0 then
        account_index = tonumber(runtime.accounts.selected_index) or 1
    end

    if not account.runtime then
        account.runtime = {}
    end

    account_enqueue_pending_script(action_text, account_index)

    if action_text == "start" then
        account.runtime.status = "queued_start"
        account.runtime.message = "start queued"
        runtime.accounts.selected_index = account_index
        runtime.accounts.settings_window_visible = false
        runtime.ui_visible = true
        runtime.accounts.last_status = "script start queued: " .. account_display_name(account)
        set_event("脚本启动已排队" .. account_display_name(account))
    elseif action_text == "stop" then
        account.runtime.status = "queued_stop"
        account.runtime.message = "stop queued"
        runtime.accounts.last_status = "script stop queued: " .. account_display_name(account)
        set_event("脚本停止已排队" .. account_display_name(account))
    end

    account.runtime.updated_at = now_seconds()
    return true
end

local function account_bind_current_target(account)
    if not account then
        return
    end
    account.target.pid = tonumber(cfg.target.pid) or 0
    account.target.hwnd = tonumber(cfg.target.hwnd) or 0
    account.target.title = tostring(cfg.target.title or "")
    account.target.character_name = tostring(cfg.target.character_name or runtime.audit.current.name or "")
    account_save_domain()
    set_event("账号已绑定当前目标PID=" .. tostring(account.target.pid))
end

local function account_apply_to_target(account)
    if not account then
        return
    end
    local target = account.target or {}
    account.target = target
    cfg.target.pid = tonumber(target.pid) or 0
    cfg.target.hwnd = tonumber(target.hwnd) or 0
    cfg.target.title = tostring(target.title or "")
    cfg.target.character_name = tostring(target.character_name or "")
    runtime.target.bound_pid = cfg.target.pid
    runtime.target.bound_hwnd = cfg.target.hwnd
    runtime.target.binding_status = "account"
    runtime.target.binding_message = "account target pid=" .. tostring(cfg.target.pid)
    set_event("已切换到账号目标 PID=" .. tostring(cfg.target.pid))
end

local function account_start_local_script(account, index)
    if not account then
        runtime.accounts.last_status = "start failed: no account selected"
        set_event("启动脚本失败: 未选择账号")
        return false
    end

    if (tonumber(account.target and account.target.pid) or 0) <= 0 and (tonumber(cfg.target and cfg.target.pid) or 0) > 0 then
        account_bind_current_target(account)
    end

    if (tonumber(account.target and account.target.pid) or 0) <= 0 then
        account.runtime.status = "error"
        account.runtime.message = "no target pid; bind current target first"
        account.runtime.updated_at = now_seconds()
        runtime.accounts.last_status = "start failed: account pid missing"
        account_save_domain()
        set_event("启动脚本失败: 账号未绑定PID")
        return false
    end

    if ok_target and target_lib then
        local target_pid = tonumber(account.target.pid) or 0
        local ok_list, candidates, list_err = target_lib.list_candidates({ selected_pid = target_pid })
        if not ok_list then
            account.runtime.status = "error"
            account.runtime.message = "target scan failed: " .. tostring(list_err)
            account.runtime.updated_at = now_seconds()
            runtime.accounts.last_status = "start failed: target scan failed"
            account_save_domain()
            set_event("启动脚本失败: 扫描目标进程失败 " .. tostring(list_err))
            return false
        end

        local matched = nil
        for _, candidate in ipairs(candidates or {}) do
            if tonumber(candidate.pid) == target_pid then
                matched = candidate
                break
            end
        end

        if not matched then
            account.runtime.status = "error"
            account.runtime.message = "target pid not found: " .. tostring(target_pid)
            account.runtime.updated_at = now_seconds()
            runtime.accounts.last_status = "start failed: pid not found " .. tostring(target_pid)
            account_save_domain()
            set_event("启动脚本失败: 账号 PID 不存在: " .. tostring(target_pid))
            return false
        end

        local character_name = tostring(account.target.character_name or "")
        target_lib.apply_candidate(account.target, matched)
        account.target.character_name = character_name
    end

    account_apply_to_target(account)
    account_select_configured_server(account)
    account.runtime.status = "starting"
    account.runtime.message = "starting local script"
    account.runtime.task_id = 0
    account.runtime.worker_key = ""
    account.runtime.bound_pid = tonumber(account.target.pid) or 0
    account.runtime.bound_hwnd = tonumber(account.target.hwnd) or 0
    account.runtime.updated_at = now_seconds()
    runtime.accounts.last_status = "starting script: " .. account_display_name(account)
    account_save_domain()

    start_bot()

    if runtime.running then
        account.runtime.status = "running"
        account.runtime.message = "local script running"
        account.runtime.started_at = now_seconds()
        account.runtime.updated_at = now_seconds()
        account.audit.status = "running"
        runtime.accounts.selected_index = tonumber(index) or runtime.accounts.selected_index
        runtime.accounts.last_status = "script started: " .. account_display_name(account)
        account_save_domain()
        set_event("账号脚本已启动" .. account_display_name(account) .. " PID=" .. tostring(account.target.pid))
        return true
    end

    account.runtime.status = "error"
    local failure_message = account_trim_text(runtime.last_event)
    if failure_message == "" then
        failure_message = account_trim_text(runtime.target and runtime.target.binding_message)
    end
    if failure_message == "" then
        failure_message = "start failed"
    end
    account.runtime.message = failure_message
    account.runtime.updated_at = now_seconds()
    runtime.accounts.last_status = "script start failed: " .. account.runtime.message
    account_save_domain()
    return false
end

local function account_stop_local_script(account)
    if account and runtime.running then
        local account_pid = tonumber(account.target and account.target.pid) or 0
        local current_pid = tonumber(cfg.target and cfg.target.pid) or 0
        if account_pid <= 0 or account_pid == current_pid then
            stop_bot()
            account.runtime.status = "stopped"
            account.runtime.message = "local script stopped"
            account.runtime.updated_at = now_seconds()
            account.audit.status = "stopped"
            runtime.accounts.last_status = "script stopped: " .. account_display_name(account)
            account_save_domain()
            return true
        end
    end
    return account_stop_runtime_worker(account)
end

function account_process_pending_script()
    local pending = account_pop_pending_script()
    if type(pending) ~= "table" then
        return
    end

    local index = tonumber(pending.index) or runtime.accounts.selected_index or 1
    local account = account_items()[index]
    if not account then
        runtime.accounts.last_status = "script request failed: account index missing " .. tostring(index)
        set_event("脚本请求失败: 账号不存在 .. tostring(index)")
        return
    end

    local action = tostring(pending.action or "")
    if action == "start" then
        if (tonumber(account.target and account.target.pid) or 0) > 0 then
            account_apply_to_target(account)
        end
        account_start_local_script(account, index)
    elseif action == "stop" then
        account_stop_local_script(account)
    else
        runtime.accounts.last_status = "script request ignored: bad action " .. action
        set_event("脚本请求忽略: 未知动作 " .. action)
    end
end

function account_open_settings(account, index)
    account_select(index, true)
    if account and (tonumber(account.target and account.target.pid) or 0) > 0 then
        account_apply_to_target(account)
    end
end

local function account_refresh_from_current_runtime()
    local account = nil
    local bound_pid = tonumber(runtime.target.bound_pid) or tonumber(cfg.target.pid) or 0
    if bound_pid <= 0 then
        return
    end

    for _, item in ipairs(account_items()) do
        if tonumber(item.target and item.target.pid) == bound_pid then
            account = item
            break
        end
    end
    if not account then
        return
    end

    local audit = runtime.audit
    local current = audit.current or {}
    account.target.character_name = tostring(current.name or account.target.character_name or "")
    account.audit.status = runtime.running and (runtime.paused and "paused" or "running") or "idle"
    account.audit.level = tonumber(current.level) or account.audit.level or 0
    account.audit.map = tostring(current.map or account.audit.map or "")
    account.audit.runtime_seconds = tonumber(audit.elapsed_seconds) or 0
    account.audit.kills = tonumber(audit.kills_est) or 0
    account.audit.kills_per_hour = audit_rate(audit.kills_est or 0)
    account.audit.kinah = tonumber(audit.last_kinah) or account.audit.kinah or 0
    account.audit.kinah_per_hour = audit_rate(audit.kinah_gain or 0)
    account.audit.last_error = tostring(audit.last_error or "")
    account.audit.last_refresh_at = now_seconds()
end

local function account_poll(force)
    local now = now_seconds()
    local interval = tonumber(cfg.accounts.poll_interval) or 5.0
    if not force and runtime.accounts.last_poll_at > 0 and now - runtime.accounts.last_poll_at < interval then
        return
    end
    runtime.accounts.last_poll_at = now
    account_refresh_from_current_runtime()

    local changed = false
    for index, account in ipairs(account_items()) do
        if account_update_from_worker(index, account) then
            changed = true
        end
        if account_update_from_runtime_worker(account) then
            changed = true
        end
        local account_pid = tonumber(account.target and account.target.pid) or 0
        if account_pid > 0 and not account_pid_is_alive(account_pid) then
            if account_clear_stale_target(account, account_pid) then
                changed = true
                runtime.accounts.last_status = "game exited; cleared pid " .. tostring(account_pid)
            end
        end
    end

    if changed then
        if runtime.accounts.last_status == "" then
            runtime.accounts.last_status = "worker fields updated"
        end
        -- Poll updates are transient runtime state. Avoid config.save() here; sync IO can stall ImGui rendering.
    end
end

local function account_ensure_all()
    account_items()
    if runtime.accounts.selected_index < 1 then
        runtime.accounts.selected_index = 1
    end
end

local function config_snapshot()
    local snapshot = {}
    for _, key in ipairs(config_top_level_keys) do
        snapshot[key] = clone_value(cfg[key])
    end
    for _, key in ipairs(config_domain_keys) do
        snapshot[key] = clone_value(cfg[key])
    end
    return snapshot
end

local function apply_config_snapshot(snapshot)
    if type(snapshot) ~= "table" then
        return false, "配置包数据无效"
    end

    for _, key in ipairs(config_top_level_keys) do
        if snapshot[key] ~= nil then
            cfg[key] = clone_value(snapshot[key])
        end
    end
    for _, key in ipairs(config_domain_keys) do
        if type(snapshot[key]) == "table" and type(cfg[key]) == "table" then
            merge_table(cfg[key], snapshot[key])
        end
    end
    normalize_combat_config()
    sync_combat_enabled_from_primary_mode()
    normalize_route_config()
    normalize_supply_config()

    return true, nil
end

local function save_config_domains()
    for _, key in ipairs(config_top_level_keys) do
        config.set("aion_control." .. key, cfg[key])
    end
    for _, key in ipairs(config_domain_keys) do
        config.set("aion_control." .. key, cfg[key])
    end
end

local function load_config_domain(key)
    local value = config.get("aion_control." .. key)
    if type(value) == "table" and type(cfg[key]) == "table" then
        merge_table(cfg[key], value)
    end
end

local function load_config_domains()
    for _, key in ipairs(config_top_level_keys) do
        cfg[key] = config.get("aion_control." .. key, cfg[key])
    end
    normalize_primary_mode()
    for _, key in ipairs(config_domain_keys) do
        load_config_domain(key)
    end
    normalize_combat_config()
    sync_combat_enabled_from_primary_mode()
    normalize_route_config()
    normalize_supply_config()
end

local function save_route_config()
    for _, key in ipairs(route_config_keys) do
        config.set("aion_control.route." .. key, cfg.route[key])
    end
end

local function load_route_config()
    for _, key in ipairs(route_config_keys) do
        cfg.route[key] = config.get("aion_control.route." .. key, cfg.route[key])
    end
    normalize_route_config()
end

function save_config()
    if not config then
        log_warn("[AionControlUI] config module unavailable")
        return
    end

    normalize_route_config()
    normalize_supply_config()
    sync_combat_enabled_from_primary_mode()
    config.load()
    normalize_combat_config()
    save_config_domains()

    -- Legacy keys kept for older copies of this UI.
    config.set("aion_control.profile_name", cfg.profile_name)
    config.set("aion_control.primary_mode", cfg.primary_mode)
    config.set("aion_control.priority_mode", cfg.priority_mode)
    config.set("aion_control.combat_radius", cfg.combat.radius)
    config.set("aion_control.gather_radius", cfg.gather.radius)
    save_route_config()
    config.set("aion_control.hp_percent", cfg.supply.hp_percent)
    config.set("aion_control.mp_percent", cfg.supply.mp_percent)
    config.set("aion_control.bag_full_percent", cfg.supply.bag_full_percent)
    config.set("aion_control.bag_slots", cfg.supply.bag_slots)
    config.set("aion_control.audit_enabled", cfg.audit.enabled)
    config.set("aion_control.audit_sample_interval", cfg.audit.sample_interval)
    config.set("aion_control.audit_material_keywords", cfg.audit.material_keywords)
    config.save()
    set_event("配置已保存到 script_config.json")
end

local function load_config()
    if not config then
        log_warn("[AionControlUI] config module unavailable")
        return
    end

    config.load()
    load_config_domains()
    account_ensure_all()
    local cleared_login = false
    for _, account in ipairs(account_items()) do
        if account.login and account.login.requested == true and not account_task_is_running(account.login.task_id) then
            account.login.requested = false
            if account.login.status == "queued"
                or account.login.status == "logging_in"
                or account.login.status == "waiting_pid"
                or account.login.status == "game_detected"
                or account.login.status == "agreement_recover"
                or account.login.status == "post_login_init"
                or account.login.status == "waiting_server_select"
                or account.login.status == "selecting_server"
                or account.login.status == "waiting_character_select"
                or account.login.status == "selecting_character"
                or account.login.status == "input_second_password"
                or account.login.status == "waiting_enter_game" then
                account.login.status = "idle"
                account.login.message = "stale login state cleared"
            end
            cleared_login = true
        end
    end

    -- Backward compatibility with early partial UI config.
    if not config.exists or not config.exists("aion_control.combat") then
        cfg.profile_name = config.get("aion_control.profile_name", cfg.profile_name)
        cfg.primary_mode = config.get("aion_control.primary_mode", cfg.primary_mode)
        cfg.priority_mode = config.get("aion_control.priority_mode", cfg.priority_mode)
        cfg.combat.radius = config.get("aion_control.combat_radius", cfg.combat.radius)
        cfg.gather.radius = config.get("aion_control.gather_radius", cfg.gather.radius)
        cfg.route.route_name = config.get("aion_control.route_name", cfg.route.route_name)
        load_route_config()
        cfg.supply.hp_percent = config.get("aion_control.hp_percent", cfg.supply.hp_percent)
        cfg.supply.mp_percent = config.get("aion_control.mp_percent", cfg.supply.mp_percent)
        cfg.supply.bag_full_percent = config.get("aion_control.bag_full_percent", cfg.supply.bag_full_percent)
        cfg.supply.bag_slots = config.get("aion_control.bag_slots", cfg.supply.bag_slots)
        cfg.audit.enabled = config.get("aion_control.audit_enabled", cfg.audit.enabled)
        cfg.audit.sample_interval = config.get("aion_control.audit_sample_interval", cfg.audit.sample_interval)
        cfg.audit.material_keywords = config.get("aion_control.audit_material_keywords", cfg.audit.material_keywords)
    end
    normalize_primary_mode()
    normalize_combat_config()
    sync_combat_enabled_from_primary_mode()
    normalize_route_config()
    if cleared_login then
        account_save_domain()
    end
    set_event("配置已加载")
end

local function run_probe()
    if not ok_probe or not probe then
        runtime.last_probe = "probe 模块不可用"
        log_warn("[AionControlUI] aion.probe unavailable")
        return
    end

    target_refresh(true)
    local probe_pid = tonumber(cfg.target.pid) or 0
    if probe_pid <= 0 and ok_core and core and type(core.resolvePid) == "function" then
        probe_pid = tonumber(core.resolvePid()) or 0
    end

    if probe_pid <= 0 then
        runtime.last_probe = "fail: Aion.bin process not found"
        set_event("API 探针失败: 找不到Aion.bin 进程")
        return
    end

    local _, summary = probe.run({ pid = probe_pid })
    runtime.last_probe = string.format("pass=%d warn=%d fail=%d",
        summary.PASS or 0, summary.WARN or 0, summary.FAIL or 0)
    set_event("API 探针完成: " .. runtime.last_probe)
end

local function run_server_probe()
    local prefix = "[AionServerProbe] "
    local function probe_info(text)
        log_info(prefix .. tostring(text or ""))
    end
    local function probe_warn(text)
        log_warn(prefix .. tostring(text or ""))
    end

    if not ok_core or not core then
        probe_warn("aion.core unavailable")
        set_event("F4 服务器探针失败: aion.core 不可用")
        return
    end

    local account_ok, account_api = account_ensure_account_api()
    if not account_ok or not account_api or type(account_api.serverList) ~= "function" then
        probe_warn("aion.account unavailable")
        set_event("F4 服务器探针失败: aion.account 不可用")
        return
    end

    target_refresh(true)
    local probe_pid = tonumber(cfg.target and cfg.target.pid) or 0
    if probe_pid <= 0 and type(core.resolvePid) == "function" then
        probe_pid = tonumber(core.resolvePid()) or 0
    end
    if probe_pid <= 0 then
        probe_warn("Aion.bin process not found")
        set_event("F4 服务器探针失败: 找不到 Aion.bin 进程")
        return
    end

    local init_ok, init_err = core.ensureInit(probe_pid)
    if not init_ok then
        probe_warn("InitGameinfo failed pid=" .. tostring(probe_pid) .. " err=" .. tostring(init_err))
        set_event("F4 服务器探针失败: 初始化失败 " .. tostring(init_err))
        return
    end

    local scene_text = "unknown"
    if type(core.getScene) == "function" then
        local scene_ok, scene, scene_err = core.getScene()
        if scene_ok and scene then
            scene_text = "idx=" .. tostring(scene.index) .. " name=" .. tostring(scene.name)
        else
            scene_text = "failed: " .. tostring(scene_err)
        end
    end

    local current_text = "unknown"
    if type(account_api.currentServerId) == "function" then
        local cur_ok, cur_id, cur_err = account_api.currentServerId()
        if cur_ok then
            current_text = tostring(cur_id)
        else
            current_text = "failed: " .. tostring(cur_err)
        end
    end

    probe_info("begin pid=" .. tostring(probe_pid) .. " scene=" .. scene_text .. " current_server_id=" .. current_text)

    local list_ok, list, list_err = account_api.serverList()
    if not list_ok then
        probe_warn("GetServerList failed: " .. tostring(list_err))
        set_event("F4 服务器探针失败: " .. tostring(list_err))
        return
    end

    list = list or {}
    probe_info("server_list count=" .. tostring(#list))
    for index, server in ipairs(list) do
        probe_info(string.format(
            "server #%d key=%s server_id=%s addr=%s",
            index,
            tostring(server.key),
            tostring(server.server_id),
            tostring(server.addr)))
    end

    if #list == 0 then
        probe_warn("server list empty; keep the client on server_select_dialog and press F4 again")
    end
    probe_info("end")
    set_event("F4 服务器探针完成: count=" .. tostring(#list))
end

local function capture_position()
    if not ok_core or not core then
        return nil, "aion.core 不可用"
    end

    local ok, pos, err = core.getPosition()
    if not ok then
        return nil, err or "坐标读取失败"
    end

    return pos, nil
end

local function capture_position_text()
    local pos, err = capture_position()
    if not pos then
        return nil, err
    end

    if ok_route and route_lib then
        return route_lib.formatPoint(pos), nil
    end

    return string.format("%.3f, %.3f, %.3f", pos.x or 0, pos.y or 0, pos.z or 0), nil
end

function teleport_node_label(node, index)
    if type(node) ~= "table" then
        return string.format("%02d. <invalid>", tonumber(index) or 0)
    end

    local name = tostring(node.name or "")
    local name_en = tostring(node.name_en or "")
    local title = name
    if title == "" then
        title = name_en
    elseif name_en ~= "" and name_en ~= name then
        title = title .. " / " .. name_en
    end
    if title == "" then
        title = "(no name)"
    end

    return string.format(
        "%02d. %s  id=%s  price=%s  pos=%.1f,%.1f,%.1f",
        tonumber(index) or 0,
        title,
        tostring(node.node_id or node.id or 0),
        tostring(node.price or 0),
        tonumber(node.x) or 0,
        tonumber(node.y) or 0,
        tonumber(node.z) or 0)
end

function teleport_selected_node()
    local index = math.max(1, tonumber(runtime.teleport_test.selected_index) or 1)
    return (runtime.teleport_test.nodes or {})[index]
end

function teleport_refresh_nodes()
    local t = runtime.teleport_test
    t.nodes = {}
    t.node_labels = { "No map nodes" }
    t.node_dump = ""
    t.can_teleport = false

    if not ok_map or not map then
        t.last_status = "据点测试失败: aion.map 不可用"
        set_event(t.last_status)
        return false
    end

    if ok_core and core and type(core.ensureInit) == "function" then
        local init_ok, init_err = core.ensureInit(tonumber(cfg.target.pid) or nil)
        if not init_ok then
            t.last_status = "据点测试初始化失败" .. tostring(init_err)
            set_event(t.last_status)
            return false
        end
    end

    local map_ok, cur_map = map.current()
    if map_ok and cur_map then
        t.map_name = tostring(cur_map.region or cur_map.name_cn or cur_map.name_en or "")
    else
        t.map_name = ""
    end

    local id_ok, big_map_id, id_err = map.bigMapId()
    if not id_ok then
        t.last_status = "读取大地图ID失败: " .. tostring(id_err)
        set_event(t.last_status)
        return false
    end
    t.big_map_id = tonumber(big_map_id) or 0

    local list_ok, nodes, list_err = map.nodes(big_map_id)
    if not list_ok then
        t.last_status = "遍历据点失败: " .. tostring(list_err)
        set_event(t.last_status)
        return false
    end

    t.nodes = nodes or {}
    t.node_labels = {}
    local dump = {}
    local selected_id = tonumber(cfg.test.selected_node_id) or 0
    local selected_index = 1

    for index, node in ipairs(t.nodes) do
        local label = teleport_node_label(node, index)
        t.node_labels[index] = label
        dump[index] = label
        if selected_id > 0 and tonumber(node.node_id or node.id or 0) == selected_id then
            selected_index = index
        end
    end

    if #t.node_labels == 0 then
        t.node_labels[1] = "No map nodes"
    end
    t.selected_index = math.max(1, math.min(#t.node_labels, selected_index))
    t.node_dump = table.concat(dump, "\n")

    local can_ok, can_value = map.canTeleport()
    t.can_teleport = can_ok and can_value == true

    t.last_status = string.format(
        "据点遍历完成: big_map_id=%s count=%d canTeleport=%s",
        tostring(t.big_map_id),
        count_array(t.nodes),
        tostring(t.can_teleport))
    set_event(t.last_status)
    return true
end

function teleport_to_selected_node()
    local t = runtime.teleport_test
    local node = teleport_selected_node()
    if not node then
        if not teleport_refresh_nodes() then
            return false
        end
        node = teleport_selected_node()
    end

    if not node then
        t.last_status = "传送失败: 没有可选据点"
        set_event(t.last_status)
        return false
    end

    if not ok_map or not map then
        t.last_status = "传送失败: aion.map 不可用"
        set_event(t.last_status)
        return false
    end

    local node_id = tonumber(node.node_id or node.id or 0) or 0
    local price = tonumber(node.price) or 0
    if node_id <= 0 then
        t.last_status = "传送失败: 据点 node_id 无效"
        set_event(t.last_status)
        return false
    end

    cfg.test.selected_node_id = node_id

    local can_ok, can_value, can_err = map.canTeleport()
    t.can_teleport = can_ok and can_value == true
    if can_ok and can_value ~= true then
        t.last_status = "传送失败: 据点传送冷却条件未满足"
        set_event(t.last_status)
        return false
    elseif not can_ok then
        t.last_status = "传送前冷却检查失败，仍尝试调用: " .. tostring(can_err)
        set_event(t.last_status)
    end

    local ok, result, err = map.nodeTeleport(node_id, price)
    if not ok then
        t.last_status = "传送调用失败: " .. tostring(err)
        set_event(t.last_status)
        return false
    end

    local label = teleport_node_label(node, t.selected_index)
    t.last_status = string.format("传送调用已发出: %s result=%s", label, tostring(result))
    set_event(t.last_status)
    return true
end

function ui_test_prepare_runtime()
    local ok_ui_runtime, ui_runtime = pcall(require, "aion.ui")
    if not ok_ui_runtime or not ui_runtime then
        runtime.ui_test.last_status = "UI测试失败: aion.ui 不可用"
        set_event(runtime.ui_test.last_status)
        return false, nil
    end

    if ok_core and core and type(core.ensureInit) == "function" then
        local init_ok, init_err = core.ensureInit(tonumber(cfg.target.pid) or nil)
        if not init_ok then
            runtime.ui_test.last_status = "UI测试初始化失败 " .. tostring(init_err)
            set_event(runtime.ui_test.last_status)
            return false, nil
        end
    end

    return true, ui_runtime
end

function ui_test_control_obj(ctrl)
    if type(ctrl) ~= "table" then
        return nil
    end
    return ctrl.obj or ctrl.addr
end

function ui_test_control_label(ctrl, index)
    ctrl = ctrl or {}
    local name = tostring(ctrl.name or "")
    if name == "" then
        name = "(no-name)"
    end
    local depth_or_layer = ctrl.depth
    local prefix = "depth"
    if depth_or_layer == nil then
        depth_or_layer = ctrl.layer
        prefix = "layer"
    end
    return string.format(
        "%02d. %s=%s obj=%s name=%s visible=%s x=%.0f y=%.0f",
        tonumber(index) or 0,
        prefix,
        tostring(depth_or_layer or 0),
        tostring(ui_test_control_obj(ctrl) or 0),
        name,
        tostring(ctrl.visible == true),
        tonumber(ctrl.x) or 0,
        tonumber(ctrl.y) or 0)
end

function ui_test_f3_label(ctrl, index)
    local label = ui_test_control_label(ctrl, index)
    local parts = {}
    if tostring(ctrl.parent_name or "") ~= "" then
        parts[#parts + 1] = "parent=" .. tostring(ctrl.parent_name)
    end
    if tonumber(ctrl.distance) then
        parts[#parts + 1] = string.format("dist=%.1f", tonumber(ctrl.distance) or 0)
    end
    if #parts > 0 then
        label = label .. " " .. table.concat(parts, " ")
    end
    return label
end

function ui_test_set_controls(list, status)
    local t = runtime.ui_test
    t.controls = list or {}
    t.labels = {}
    local dump = {}
    for index, ctrl in ipairs(t.controls) do
        local label = ui_test_control_label(ctrl, index)
        if tonumber(ctrl.distance) or tostring(ctrl.parent_name or "") ~= "" then
            label = ui_test_f3_label(ctrl, index)
        end
        t.labels[index] = label
        dump[index] = label
    end
    if #t.labels == 0 then
        t.labels[1] = "No UI controls"
    end
    t.selected_index = math.max(1, math.min(#t.labels, tonumber(t.selected_index) or 1))
    t.dump = table.concat(dump, "\n")
    t.last_status = status or ("控件数 " .. tostring(#t.controls))
    set_event(t.last_status)
    log_info("[AionUITest] " .. t.last_status)
end

function ui_test_refresh_all()
    local t = runtime.ui_test
    local ready, ui_runtime = ui_test_prepare_runtime()
    if not ready then return false end

    local include_no_name = cfg.test.ui_include_no_name == true
    local ok, list, err = ui_runtime.list(include_no_name)
    if not ok then
        t.last_status = "遍历全部UI失败: " .. tostring(err)
        set_event(t.last_status)
        return false
    end
    ui_test_set_controls(list or {}, string.format("遍历全部UI完成: count=%d includeNoName=%s", #(list or {}), tostring(include_no_name)))
    return true
end

function ui_test_refresh_children()
    local t = runtime.ui_test
    local ready, ui_runtime = ui_test_prepare_runtime()
    if not ready then return false end

    local parent = tostring(cfg.test.ui_parent_name or "")
    if parent == "" then
        t.last_status = "遍历子控件失败: 父控件名为空"
        set_event(t.last_status)
        return false
    end

    local depth = math.max(1, tonumber(cfg.test.ui_child_depth) or 6)
    local ok, list, err = ui_runtime.children(parent, depth)
    if not ok then
        t.last_status = "遍历子控件失败: " .. tostring(err)
        set_event(t.last_status)
        return false
    end
    ui_test_set_controls(list or {}, string.format("遍历子控件完成 parent=%s depth=%d count=%d", parent, depth, #(list or {})))
    return true
end

function ui_test_click_selected()
    local t = runtime.ui_test
    local ready, ui_runtime = ui_test_prepare_runtime()
    if not ready then return false end

    local index = math.max(1, tonumber(t.selected_index) or 1)
    local ctrl = (t.controls or {})[index]
    if not ctrl then
        t.last_status = "点击控件失败: 未选择有效控件"
        set_event(t.last_status)
        return false
    end

    local obj = ui_test_control_obj(ctrl)
    if not obj or tonumber(obj) == 0 then
        t.last_status = "点击控件失败: 控件对象地址无效"
        set_event(t.last_status)
        return false
    end

    local ok, result, err = ui_runtime.click(obj)
    if not ok or result == false then
        t.last_status = "点击控件失败: " .. tostring(err or result) .. " | " .. ui_test_control_label(ctrl, index)
        set_event(t.last_status)
        return false
    end

    t.last_status = "点击控件已发送:  " .. ui_test_control_label(ctrl, index)
    set_event(t.last_status)
    log_info("[AionUITest] " .. t.last_status)
    return true
end

function ui_test_f3_target_hwnd()
    local hwnd = tonumber(cfg.target and cfg.target.hwnd) or 0
    if hwnd <= 0 then
        hwnd = tonumber(runtime.target and runtime.target.bound_hwnd) or 0
    end
    if hwnd <= 0 and proc and type(proc.window) == "function" then
        local pid = tonumber(cfg.target and cfg.target.pid) or 0
        if pid > 0 then
            local ok, value = pcall(proc.window, pid)
            if ok and tonumber(value) and tonumber(value) > 0 then
                hwnd = tonumber(value)
            end
        end
    end
    if hwnd <= 0 and wnd and type(wnd.get_foreground) == "function" then
        local ok, value = pcall(wnd.get_foreground)
        if ok and tonumber(value) and tonumber(value) > 0 then
            hwnd = tonumber(value)
        end
    end
    return hwnd
end

function ui_test_f3_mouse_client_position()
    if not mouse or type(mouse.position) ~= "function" then
        return false, nil, "mouse.position unavailable"
    end
    if not wnd or type(wnd.client_rect) ~= "function" then
        return false, nil, "wnd.client_rect unavailable"
    end

    local hwnd = ui_test_f3_target_hwnd()
    if (tonumber(hwnd) or 0) <= 0 then
        return false, nil, "target hwnd unavailable"
    end

    local mouse_ok, screen_x, screen_y = pcall(mouse.position)
    if not mouse_ok or screen_x == nil or screen_y == nil then
        return false, nil, "mouse.position failed"
    end

    local rect_ok, client_x, client_y, client_w, client_h = pcall(wnd.client_rect, hwnd)
    if not rect_ok or client_x == nil or client_y == nil then
        return false, nil, "wnd.client_rect failed"
    end

    screen_x = tonumber(screen_x) or 0
    screen_y = tonumber(screen_y) or 0
    client_x = tonumber(client_x) or 0
    client_y = tonumber(client_y) or 0

    return true, {
        hwnd = hwnd,
        screen_x = screen_x,
        screen_y = screen_y,
        client_x = screen_x - client_x,
        client_y = screen_y - client_y,
        rect_x = client_x,
        rect_y = client_y,
        rect_w = tonumber(client_w) or 0,
        rect_h = tonumber(client_h) or 0,
    }, nil
end

function ui_test_f3_add_candidate(out, seen, ctrl, parent_name, mouse_x, mouse_y)
    if type(ctrl) ~= "table" or ctrl.visible ~= true then
        return
    end

    local x = tonumber(ctrl.x)
    local y = tonumber(ctrl.y)
    if not x or not y then
        return
    end

    local obj = ui_test_control_obj(ctrl)
    local key = tostring(obj or "")
    if key == "" or key == "0" then
        key = tostring(parent_name or "") .. "|" .. tostring(ctrl.name or "") .. "|" .. tostring(x) .. "|" .. tostring(y)
    end
    if seen[key] then
        return
    end
    seen[key] = true

    local item = {}
    for k, v in pairs(ctrl) do
        item[k] = v
    end
    item.parent_name = tostring(parent_name or "")
    item.mouse_x = mouse_x
    item.mouse_y = mouse_y
    item.distance2 = (x - mouse_x) * (x - mouse_x) + (y - mouse_y) * (y - mouse_y)
    item.distance = math.sqrt(item.distance2)
    out[#out + 1] = item
end

function ui_test_f3_collect_nearby(ui_runtime, mouse_x, mouse_y)
    local ok, list, err = ui_runtime.list(true)
    if not ok then
        return false, nil, err or "GetUIList failed"
    end

    list = list or {}
    local candidates = {}
    local seen = {}
    local parent_seen = {}
    local depth = math.max(1, tonumber(cfg.test.ui_child_depth) or 6)

    for _, ctrl in ipairs(list) do
        ui_test_f3_add_candidate(candidates, seen, ctrl, "", mouse_x, mouse_y)

        local parent_name = tostring(ctrl.name or "")
        if ctrl.visible == true and parent_name ~= "" and not parent_seen[parent_name] then
            parent_seen[parent_name] = true
            local child_ok, children = ui_runtime.children(parent_name, depth)
            if child_ok then
                for _, child in ipairs(children or {}) do
                    ui_test_f3_add_candidate(candidates, seen, child, parent_name, mouse_x, mouse_y)
                end
            end
        end
    end

    table.sort(candidates, function(a, b)
        local ad = tonumber(a.distance2) or 0
        local bd = tonumber(b.distance2) or 0
        if ad ~= bd then
            return ad < bd
        end
        local an = tostring(a.name or "") ~= "" and 0 or 1
        local bn = tostring(b.name or "") ~= "" and 0 or 1
        if an ~= bn then
            return an < bn
        end
        return tostring(a.name or "") < tostring(b.name or "")
    end)

    local nearby = {}
    for index = 1, math.min(5, #candidates) do
        nearby[index] = candidates[index]
    end
    return true, nearby, nil, #candidates, #list
end

function ui_test_f3_dump()
    local ready, ui_runtime = ui_test_prepare_runtime()
    if not ready then
        log_warn("[AionUIF3] prepare failed: " .. tostring(runtime.ui_test.last_status or ""))
        return false
    end

    local pos_ok, pos, pos_err = ui_test_f3_mouse_client_position()
    if not pos_ok then
        runtime.ui_test.last_status = "F3 nearby UI failed: " .. tostring(pos_err)
        set_event(runtime.ui_test.last_status)
        log_warn("[AionUIF3] " .. runtime.ui_test.last_status)
        return false
    end

    local ok, nearby, err, total_count, top_count = ui_test_f3_collect_nearby(ui_runtime, pos.client_x, pos.client_y)
    if not ok then
        runtime.ui_test.last_status = "F3 nearby UI failed: " .. tostring(err)
        set_event(runtime.ui_test.last_status)
        log_warn("[AionUIF3] " .. runtime.ui_test.last_status)
        return false
    end

    nearby = nearby or {}
    runtime.ui_test.nearby = nearby
    ui_test_set_controls(nearby, string.format(
        "F3 nearby UI logged: mouse=(%.0f,%.0f) count=%d",
        tonumber(pos.client_x) or 0,
        tonumber(pos.client_y) or 0,
        #nearby))
    runtime.ui_test.nearby_dump = runtime.ui_test.dump or ""

    log_info(string.format(
        "[AionUIF3] nearby begin hwnd=%s screen=(%.0f,%.0f) client=(%.0f,%.0f) rect=(%.0f,%.0f %.0fx%.0f) top=%d total=%d count=%d",
        tostring(pos.hwnd or 0),
        tonumber(pos.screen_x) or 0,
        tonumber(pos.screen_y) or 0,
        tonumber(pos.client_x) or 0,
        tonumber(pos.client_y) or 0,
        tonumber(pos.rect_x) or 0,
        tonumber(pos.rect_y) or 0,
        tonumber(pos.rect_w) or 0,
        tonumber(pos.rect_h) or 0,
        tonumber(top_count) or 0,
        tonumber(total_count) or 0,
        #nearby))
    for index, ctrl in ipairs(nearby) do
        log_info("[AionUIF3] " .. ui_test_f3_label(ctrl, index))
    end
    log_info("[AionUIF3] nearby end")
    return true
end

function ui_test_f2_dump()
    local ready, ui_runtime = ui_test_prepare_runtime()
    if not ready then
        log_warn("[AionUIF2] prepare failed: " .. tostring(runtime.ui_test.last_status or ""))
        return false
    end

    local ok, list, err = ui_runtime.list(true)
    if not ok then
        runtime.ui_test.last_status = "F2 full UI dump failed: " .. tostring(err)
        set_event(runtime.ui_test.last_status)
        log_warn("[AionUIF2] " .. runtime.ui_test.last_status)
        return false
    end

    list = list or {}
    ui_test_set_controls(list, string.format("F2 full UI dump logged: count=%d", #list))
    log_info("[AionUIF2] full UI begin count=" .. tostring(#list) .. " includeNoName=true")
    for index, ctrl in ipairs(list) do
        log_info("[AionUIF2] UI " .. ui_test_control_label(ctrl, index))
    end
    log_info("[AionUIF2] full UI end")

    local depth = math.max(1, tonumber(cfg.test.ui_child_depth) or 8)
    local parents = {}
    local seen = {}
    local function add_parent(name)
        name = tostring(name or "")
        if name ~= "" and not seen[name] then
            seen[name] = true
            parents[#parents + 1] = name
        end
    end

    add_parent(cfg.test.ui_parent_name)
    add_parent("resurrect_dialog_new")
    add_parent("move_state_dialog")
    add_parent("dlg_revive")
    add_parent("death_object_delay_dialog")
    add_parent("resurrectother_dialog")
    add_parent("common_alert_dialog")
    add_parent("start_dialog")
    add_parent("user_agreement_dialog")
    add_parent("dlg_dialog")
    add_parent("quest_indicator_dialog")
    add_parent("v3_quest_dialog")
    add_parent("dictionary_dialog")
    add_parent("loot_dialog")
    add_parent("dlg_loot")

    for _, parent in ipairs(parents) do
        local child_ok, children, child_err = ui_runtime.children(parent, depth)
        if not child_ok then
            log_warn("[AionUIF2] children parent=" .. tostring(parent) ..
                " depth=" .. tostring(depth) .. " failed: " .. tostring(child_err))
        else
            children = children or {}
            log_info("[AionUIF2] children begin parent=" .. tostring(parent) ..
                " depth=" .. tostring(depth) .. " count=" .. tostring(#children))
            for index, child in ipairs(children) do
                log_info("[AionUIF2] CHILD parent=" .. tostring(parent) ..
                    " " .. ui_test_control_label(child, index))
            end
            log_info("[AionUIF2] children end parent=" .. tostring(parent))
        end
    end

    local popup_roots = {}
    for _, ctrl in ipairs(list or {}) do
        local obj = tonumber(ctrl and (ctrl.obj or ctrl.addr)) or 0
        local name = string.lower(tostring(ctrl and ctrl.name or ""))
        local layer = tonumber(ctrl and ctrl.layer) or 0
        local x = tonumber(ctrl and ctrl.x) or 0
        local y = tonumber(ctrl and ctrl.y) or 0
        if obj > 0
            and ctrl.visible == true
            and layer >= 2
            and (name == "" or name == "(no-name)")
            and x >= 250 and x <= 850
            and y >= 120 and y <= 520 then
            popup_roots[#popup_roots + 1] = ctrl
        end
    end
    log_info("[AionUIF2] visible unnamed popup roots count=" .. tostring(#popup_roots))
    for root_index, root in ipairs(popup_roots) do
        local root_obj = root.obj or root.addr
        log_info("[AionUIF2] popup root " .. tostring(root_index) ..
            " obj=" .. tostring(root_obj) ..
            " x=" .. tostring(root.x or "") ..
            " y=" .. tostring(root.y or ""))
        local child_ok, children, child_err = ui_runtime.children(root_obj, depth)
        if not child_ok then
            log_warn("[AionUIF2] popup root children obj=" .. tostring(root_obj) ..
                " failed: " .. tostring(child_err))
        else
            children = children or {}
            log_info("[AionUIF2] popup root children begin obj=" .. tostring(root_obj) ..
                " depth=" .. tostring(depth) .. " count=" .. tostring(#children))
            for index, child in ipairs(children) do
                log_info("[AionUIF2] POPUP_CHILD root=" .. tostring(root_obj) ..
                    " " .. ui_test_control_label(child, index))
            end
            log_info("[AionUIF2] popup root children end obj=" .. tostring(root_obj))
        end
    end

    runtime.ui_test.last_status = "F2 UI dump complete; copy log lines with [AionUIF2]"
    set_event(runtime.ui_test.last_status)
    log_info("[AionUIF2] dump complete")
    return true
end

local function append_point(field, silentTooClose)
    local pos, err = capture_position()
    if not pos then
        set_event("添加坐标失败: " .. tostring(err))
        return false
    end

    local text = nil
    if ok_route and route_lib then
        local next_text, appended, reason = route_lib.appendText(
            cfg.route[field],
            pos,
            cfg.route.min_record_distance
        )
        if not appended then
            if not silentTooClose then
                set_event("坐标未添加: " .. tostring(reason))
            end
            return false
        end
        cfg.route[field] = next_text
        text = route_lib.formatPoint(pos)
    else
        text = string.format("%.3f, %.3f, %.3f", pos.x or 0, pos.y or 0, pos.z or 0)
        cfg.route[field] = cfg.route[field] == "" and text or cfg.route[field] .. "\n" .. text
    end

    runtime.route.last_record_pos = pos
    set_event("已添加坐标" .. text)
    return true
end

local function clear_route(field)
    if runtime.route.recording and runtime.route.record_field == field then
        runtime.route.recording = false
        runtime.route.record_field = nil
        runtime.route.record_name = ""
    end
    if route_stop_follow and runtime.route.following and runtime.route.follow_field == field then
        route_stop_follow("清空路径", true)
    end
    cfg.route[field] = ""
    set_event("已清空路径: " .. field)
end

local function route_selected_spec()
    return route_specs[cfg.route.selected_route] or route_specs[1]
end

function route_trim(text)
    return tostring(text or ""):match("^%s*(.-)%s*$")
end

function route_spec_by_points_field(pointsField)
    for _, spec in ipairs(route_specs) do
        if spec.points_field == pointsField then
            return spec
        end
    end
    return nil
end

function route_ensure_saved_routes()
    if type(cfg.route.saved_routes) ~= "table" then
        cfg.route.saved_routes = {}
    end

    for _, spec in ipairs(route_specs) do
        if type(cfg.route.saved_routes[spec.points_field]) ~= "table" then
            cfg.route.saved_routes[spec.points_field] = {}
        end
    end
end

function route_saved_items(pointsField)
    route_ensure_saved_routes()
    return cfg.route.saved_routes[pointsField] or {}
end

function route_persist_config()
    if config and type(config.set) == "function" and type(config.save) == "function" then
        save_route_config()
        config.save()
    end
end

function route_point_count(text)
    if ok_route and route_lib then
        local ok, points = route_lib.parse(text or "")
        if ok and type(points) == "table" then
            return #points
        end
    end
    return count_lines(text)
end

function route_saved_labels(pointsField)
    local list = route_saved_items(pointsField)
    if #list == 0 then
        return { "无已保存路径" }
    end

    local labels = { "选择已保存路径 "}
    for _, item in ipairs(list) do
        local name = route_trim(item.name or "")
        if name == "" then
            name = "未命名路径"
        end
        local point_count = tonumber(item.point_count) or route_point_count(item.points_text or "")
        labels[#labels + 1] = string.format("%s (%d点)", name, point_count)
    end
    return labels
end

function route_saved_index(pointsField, nameField)
    local list = route_saved_items(pointsField)
    if #list == 0 then
        return 1
    end

    local name = route_trim(cfg.route[nameField] or "")
    local points_text = tostring(cfg.route[pointsField] or "")
    for index, item in ipairs(list) do
        if name ~= "" and tostring(item.name or "") == name then
            return index + 1
        end
    end
    for index, item in ipairs(list) do
        if points_text ~= "" and tostring(item.points_text or "") == points_text then
            return index + 1
        end
    end
    return 1
end

function route_sync_name_from_saved_points(pointsField, nameField)
    local current_name = route_trim(cfg.route[nameField] or "")
    if current_name ~= "" then
        return false
    end

    local points_text = tostring(cfg.route[pointsField] or "")
    if points_text == "" then
        return false
    end

    for _, item in ipairs(route_saved_items(pointsField)) do
        if tostring(item.points_text or "") == points_text then
            local saved_name = route_trim(item.name or "")
            if saved_name ~= "" then
                cfg.route[nameField] = saved_name
                return true
            end
            return false
        end
    end
    return false
end

function route_sync_all_names_from_saved_points()
    local changed = false
    for _, spec in ipairs(route_specs) do
        if route_sync_name_from_saved_points(spec.points_field, spec.name_field) then
            changed = true
        end
    end
    return changed
end

function route_clear_selection(pointsField, nameField)
    local spec = route_spec_by_points_field(pointsField)
    cfg.route[nameField] = ""
    cfg.route[pointsField] = ""
    set_event("已清空当前路径选择: " .. tostring(spec and spec.label or pointsField))
    route_persist_config()
    return true
end

function route_save_current(pointsField, nameField, silent)
    local points_text = tostring(cfg.route[pointsField] or "")
    if route_point_count(points_text) <= 0 then
        if not silent then
            set_event("保存路径失败: 路径为空")
        end
        return false
    end

    local spec = route_spec_by_points_field(pointsField)
    local name = route_trim(cfg.route[nameField] or "")
    if name == "" then
        name = tostring(spec and spec.label or pointsField) .. "_" .. os.date("%H%M%S")
        cfg.route[nameField] = name
    end

    local list = route_saved_items(pointsField)
    local point_count = route_point_count(points_text)
    local distance = 0
    if ok_route and route_lib then
        local ok, points = route_lib.parse(points_text)
        if ok and type(points) == "table" then
            distance = tonumber(route_lib.stats(points).distance) or 0
        end
    end

    local existing_index = nil
    for index, item in ipairs(list) do
        if tostring(item.name or "") == name then
            existing_index = index
            break
        end
    end

    local item = {
        name = name,
        label = spec and spec.label or pointsField,
        points_field = pointsField,
        points_text = points_text,
        point_count = point_count,
        distance = distance,
        updated_at = os.time(),
    }

    if existing_index then
        list[existing_index] = item
    else
        list[#list + 1] = item
    end

    if not silent then
        set_event("路径已保存: " .. name)
        route_persist_config()
    end
    return true
end

function route_load_saved(pointsField, nameField, index)
    local item_index = (tonumber(index) or 1) - 1
    if item_index <= 0 then
        return route_clear_selection(pointsField, nameField)
    end

    local item = route_saved_items(pointsField)[item_index]
    if not item then
        set_event("选择路径失败: 没有已保存路径")
        return false
    end

    cfg.route[nameField] = tostring(item.name or cfg.route[nameField] or "")
    cfg.route[pointsField] = tostring(item.points_text or "")
    set_event("已选择路径: " .. tostring(cfg.route[nameField]))
    route_persist_config()
    return true
end

function route_delete_saved(pointsField, nameField, index)
    local list = route_saved_items(pointsField)
    index = (tonumber(index) or route_saved_index(pointsField, nameField)) - 1
    if index <= 0 then
        set_event("删除路径失败: 没有选择已保存路径")
        return false
    end

    local item = list[index]
    if not item then
        set_event("删除路径失败: 没有已保存路径")
        return false
    end

    table.remove(list, index)
    set_event("已删除保存路径: " .. tostring(item.name or ""))
    route_persist_config()
    return true
end

function route_copy_current(pointsField, nameField)
    local points_text = tostring(cfg.route[pointsField] or "")
    if route_trim(points_text) == "" then
        set_event("复制路径失败: 当前路径为空")
        return false
    end
    if not sys or type(sys.set_clipboard) ~= "function" then
        set_event("复制路径失败: 剪贴板不可用")
        return false
    end

    local ok, result = pcall(sys.set_clipboard, points_text)
    if not ok or result == false then
        set_event("复制路径失败: " .. tostring(result))
        return false
    end

    local name = route_trim(cfg.route[nameField] or "")
    if name == "" then
        name = tostring(pointsField)
    end
    set_event("已复制路径: " .. name .. " 点数=" .. tostring(route_point_count(points_text)))
    return true
end

function route_selection_summary()
    local parts = {}
    for _, spec in ipairs(route_specs) do
        local name = route_trim(cfg.route[spec.name_field] or "")
        local count = route_point_count(cfg.route[spec.points_field] or "")
        parts[#parts + 1] = string.format("%s=%s(%d点)", spec.label, name ~= "" and name or "未选", count)
    end
    return table.concat(parts, " | ")
end

local function route_read_points(field)
    if not ok_route or not route_lib then
        return false, {}, { "aion.route 不可用 "}
    end

    local ok, points, warnings = route_lib.parse(cfg.route[field] or "")
    if not ok then
        return false, {}, { tostring(points or "路径解析失败") }
    end
    return true, points or {}, warnings or {}
end

function route_distance3(a, b)
    if ok_core and core and type(core.distance3) == "function" then
        return core.distance3(a, b)
    end
    local dx = (tonumber(a and a.x) or 0) - (tonumber(b and b.x) or 0)
    local dy = (tonumber(a and a.y) or 0) - (tonumber(b and b.y) or 0)
    local dz = (tonumber(a and a.z) or 0) - (tonumber(b and b.z) or 0)
    return math.sqrt(dx * dx + dy * dy + dz * dz)
end

function route_format_pos(pos)
    if type(pos) ~= "table" then
        return "nil"
    end
    return string.format("%.2f,%.2f,%.2f",
        tonumber(pos.x) or 0,
        tonumber(pos.y) or 0,
        tonumber(pos.z) or 0)
end

function route_current_position()
    if not ok_core or not core then
        return false, nil, "aion.core unavailable"
    end
    local ok, pos, err = core.getPosition()
    if ok and pos then
        return true, pos, nil
    end
    local char_ok, char, char_err = core.getCharacter()
    if char_ok and char then
        return true, char, nil
    end
    return false, nil, err or char_err or "position unavailable"
end

function route_nearest_point(points, pos)
    if type(points) ~= "table" or not pos then
        return 1, 0
    end

    local best_index = 1
    local best_dist = nil
    for index, point in ipairs(points) do
        local dist = route_distance3(pos, point)
        if best_dist == nil or dist < best_dist then
            best_index = index
            best_dist = dist
        end
    end
    return best_index, best_dist or 0
end

function route_last_point(field)
    local ok, points = route_read_points(field)
    if ok and #points > 0 then
        return points[#points]
    end
    return nil
end

function route_recovery_log(message)
    local text = "[AionRecovery] " .. tostring(message or "")
    runtime.recovery.last_status = tostring(message or "")
    set_event(text)
    log_info(text)
end

function route_recovery_blocks_combat()
    return runtime.recovery and runtime.recovery.active == true
end

function auto_revive_is_death_phase(phase)
    phase = tostring(phase or "")
    return phase == "death-revive" or phase == "death-clicked" or phase == "death-wait"
end

function route_post_revive_wait_seconds()
    return math.max(2.0, tonumber(cfg.route and cfg.route.post_revive_wait_seconds) or 2.0)
end

function auto_revive_control_obj(ctrl)
    if type(ctrl) ~= "table" then
        return nil
    end
    return ctrl.obj or ctrl.addr
end

function auto_revive_control_visible(ctrl)
    return type(ctrl) == "table" and ctrl.visible == true
end

function auto_revive_try_parent(ui_runtime, parent_name, preferred_names, reason)
    local find_ok, parent, find_err = ui_runtime.find(parent_name)
    if not find_ok then
        return false, "find " .. tostring(parent_name) .. " failed: " .. tostring(find_err)
    end
    if not parent then
        return false, "parent not found: " .. tostring(parent_name)
    end
    if type(parent) == "table" and parent.visible == false then
        return false, "parent hidden: " .. tostring(parent_name)
    end

    local child_ok, children, child_err = ui_runtime.children(parent_name, 6)
    if not child_ok then
        return false, "children " .. tostring(parent_name) .. " failed: " .. tostring(child_err)
    end

    children = children or {}
    preferred_names = preferred_names or {}
    local last_err = "no visible clickable child"
    for _, wanted_name in ipairs(preferred_names) do
        for _, child in ipairs(children) do
            if auto_revive_control_visible(child) and tostring(child.name or "") == wanted_name then
                local obj = auto_revive_control_obj(child)
                if obj and tonumber(obj) ~= 0 then
                    local click_ok, clicked, click_err = ui_runtime.click(obj)
                    if click_ok and clicked ~= false then
                        route_recovery_log("auto revive click parent=" .. tostring(parent_name) ..
                            " child=" .. tostring(wanted_name) ..
                            " obj=" .. tostring(obj) ..
                            " reason=" .. tostring(reason or ""))
                        return true, nil
                    end
                    last_err = "click " .. tostring(parent_name) .. "." .. tostring(wanted_name) ..
                        " failed: " .. tostring(click_err or clicked)
                end
            end
        end
    end

    return false, last_err
end

function auto_revive_try_click(reason, force)
    if cfg.route and cfg.route.auto_revive == false then
        return false, "auto revive disabled"
    end

    local rec = runtime.recovery
    local now = now_seconds()
    local interval = math.max(0.2, tonumber(cfg.route and cfg.route.revive_click_interval) or 0.8)
    if not force and (tonumber(rec.last_revive_click_at) or 0) > 0 and now - (tonumber(rec.last_revive_click_at) or 0) < interval then
        return false, "throttled"
    end

    rec.last_revive_click_at = now
    rec.revive_click_attempts = (tonumber(rec.revive_click_attempts) or 0) + 1

    local ok_ui_runtime, ui_runtime = pcall(require, "aion.ui")
    if not ok_ui_runtime or not ui_runtime then
        rec.revive_last_error = "aion.ui unavailable"
        route_recovery_log("auto revive failed: " .. rec.revive_last_error)
        return false, rec.revive_last_error
    end

    local ok, err = auto_revive_try_parent(ui_runtime, "resurrect_dialog_new", {
        "ok",
    }, reason)
    if ok then
        rec.revive_last_error = ""
        return true, nil
    end

    local other_ok, other_err = auto_revive_try_parent(ui_runtime, "resurrectother_dialog", {
        "resurrect_ok",
    }, reason)
    if other_ok then
        rec.revive_last_error = ""
        return true, nil
    end

    rec.revive_last_error = tostring(err or other_err or "revive dialog not ready")
    route_recovery_log("auto revive waiting attempt=" .. tostring(rec.revive_click_attempts) ..
        " reason=" .. tostring(reason or "") ..
        " err=" .. tostring(rec.revive_last_error))
    return false, rec.revive_last_error
end

function route_inventory_is_full()
    if not ok_inventory or not inventory or type(inventory.list) ~= "function" then
        return false, 0, 0
    end
    local ok, items = inventory.list()
    if not ok then
        return false, 0, 0
    end

    local used = #(items or {})
    local slots = math.max(1, tonumber(cfg.supply.bag_slots) or 100)
    local percent = math.max(1, math.min(100, tonumber(cfg.supply.bag_full_percent) or 85))
    local threshold = math.max(1, math.floor(slots * percent / 100))
    return used >= threshold, used, threshold
end

local function route_stats_text(field)
    local ok, points, warnings = route_read_points(field)
    if not ok then
        return "路径模块不可用: " .. tostring(route_load_error or "unknown")
    end

    local stats = route_lib.stats(points)
    return string.format("点数 %d | 总距 %.1f | 无效 %d",
        stats.count or 0,
        stats.distance or 0,
        #(warnings or {}))
end

local function route_validate(field, name)
    local ok, points, warnings = route_read_points(field)
    if not ok then
        runtime.route.error = table.concat(warnings or {}, "; ")
        set_event("路径校验失败: " .. runtime.route.error)
        return false
    end

    if #points == 0 then
        runtime.route.error = "路径为空"
        set_event("路径校验失败: " .. tostring(name) .. " 为空")
        return false
    end

    runtime.route.error = ""
    set_event(string.format("路径校验通过: %s 点数=%d 无效 %d",
        tostring(name),
        #points,
        #(warnings or {})))
    return true
end

local function route_start_record(pointsField, nameField)
    if not ok_core or not core then
        set_event("开始录制失败: aion.core 不可用")
        return false
    end

    if route_stop_follow and runtime.route.following then
        route_stop_follow("开始录制", true)
    end

    if runtime.route.recording and runtime.route.record_field ~= pointsField then
        set_event("切换录制路径: " .. tostring(runtime.route.record_name))
    end

    runtime.route.recording = true
    runtime.route.record_field = pointsField
    runtime.route.record_name = cfg.route[nameField] or pointsField
    runtime.route.last_record_at = 0
    runtime.route.last_record_pos = nil
    runtime.route.error = ""
    set_event("开始录制路径: " .. tostring(runtime.route.record_name))
    append_point(pointsField)
    return true
end

local function route_stop_record()
    if not runtime.route.recording then
        set_event("当前没有正在录制的路径")
        return
    end

    local record_field = runtime.route.record_field
    local record_name = runtime.route.record_name
    local spec = route_spec_by_points_field(record_field)
    local saved = false
    if spec then
        saved = route_save_current(record_field, spec.name_field, true)
    end

    set_event("stop record: " .. tostring(record_name) .. (saved and ", saved" or ""))
    if saved then
        route_persist_config()
    end
    runtime.route.recording = false
    runtime.route.record_field = nil
    runtime.route.record_name = ""
    runtime.route.last_record_at = 0
end

route_stop_follow = function(reason, finishRuntime, completedField)
    local was_following = runtime.route.following
    local show_on_finish = runtime.route.finish_shows_ui
    local completed_field = completedField or runtime.route.completed_field
    local test_only = runtime.route.test_only == true
    local follow_field = tostring(runtime.route.follow_field or "")
    local main_quest_stage = tostring(runtime.route.main_quest_stage or "")
    if was_following
        and string.sub(follow_field, 1, #"main_quest_20590:") == "main_quest_20590:" then
        runtime.main_quest = runtime.main_quest or {}
        runtime.main_quest.last_route_stop_stage = main_quest_stage
        runtime.main_quest.last_route_stop_reason = tostring(reason or "")
        runtime.main_quest.last_route_stop_at = now_seconds()
        main_quest_set_action_delay("route-stop:" .. tostring(reason or ""))
        main_quest_trace("route-stop:" .. main_quest_stage,
            "stage=" .. main_quest_stage ..
            " field=" .. follow_field ..
            " reason=" .. tostring(reason or ""),
            0)
    end
    runtime.route.completed_field = nil
    runtime.route.following = false
    runtime.route.follow_field = nil
    runtime.route.follow_name = ""
    runtime.route.points = {}
    runtime.route.index = 1
    runtime.route.direction = 1
    runtime.route.moving_to = nil
    runtime.route.last_move_at = 0
    runtime.route.status = "空闲"
    runtime.route.attach_runtime = false
    runtime.route.finish_shows_ui = false
    runtime.route.loop = nil
    runtime.route.reverse_on_end = nil
    runtime.route.test_only = false

    if was_following then
        set_event("停止路径: " .. tostring(reason or "手动停止"))
    end

    if finishRuntime then
        runtime.running = false
        runtime.paused = false
        runtime.status = "已停止"
        runtime.active_mode = "none"
        if show_on_finish then
            runtime.ui_visible = true
        end
    end

    if completed_field and not test_only and route_recovery_on_route_complete then
        log_info("[AionRoute] complete field=" .. tostring(completed_field) .. " reason=" .. tostring(reason or ""))
        route_recovery_on_route_complete(completed_field)
    end
end

local function route_start_follow(pointsField, nameField, attachRuntime, opts)
    opts = opts or {}
    if not ok_nav or not nav then
        runtime.route.error = "aion.nav 不可用"
        set_event("路径运行失败: " .. runtime.route.error)
        return false
    end

    local ok, points, warnings = route_read_points(pointsField)
    if not ok then
        runtime.route.error = table.concat(warnings or {}, "; ")
        set_event("路径运行失败: " .. runtime.route.error)
        return false
    end

    if #points == 0 then
        runtime.route.error = "路径为空"
        set_event("路径运行失败: " .. tostring(cfg.route[nameField]) .. " 为空")
        return false
    end

    if runtime.route.recording then
        route_stop_record()
    end

    if runtime.route.following then
        route_stop_follow("切换路径", false)
    end

    local start_index = 1
    local start_dist = 0
    local use_nearest = opts.start_nearest
    if use_nearest == nil then
        use_nearest = cfg.route.start_from_nearest ~= false
    end
    if use_nearest and route_nearest_point then
        local pos = opts.start_pos
        if not pos then
            local pos_ok, current_pos = route_current_position()
            if pos_ok then
                pos = current_pos
            end
        end
        if pos then
            start_index, start_dist = route_nearest_point(points, pos)
        end
    end
    start_index = math.max(1, math.min(#points, tonumber(opts.start_index) or start_index or 1))

    runtime.route.following = true
    runtime.route.follow_field = pointsField
    runtime.route.follow_name = cfg.route[nameField] or pointsField
    runtime.route.points = points
    runtime.route.index = start_index
    runtime.route.direction = 1
    runtime.route.moving_to = nil
    runtime.route.last_move_at = 0
    runtime.route.laps = 0
    runtime.route.status = "准备移动"
    runtime.route.error = (#warnings > 0) and ("有无效行已忽略: " .. tostring(#warnings)) or ""
    runtime.route.attach_runtime = attachRuntime == true
    runtime.route.finish_shows_ui = attachRuntime == true
    runtime.route.test_only = opts.test_only == true
    runtime.route.loop = opts.loop
    runtime.route.reverse_on_end = opts.reverse_on_end
    local start_point = points[start_index]
    log_info(string.format("[AionRoute] start name=%s field=%s points=%d index=%d nearest_dist=%.1f start_nearest=%s target=%s attach=%s",
        tostring(runtime.route.follow_name),
        tostring(pointsField),
        #points,
        tonumber(start_index) or 1,
        tonumber(start_dist) or 0,
        tostring(use_nearest),
        route_format_pos(start_point),
        tostring(attachRuntime == true)))
    if attachRuntime == true then
        runtime.running = true
        runtime.paused = false
        runtime.status = "运行中"
        runtime.active_mode = "路径"
    end

    if runtime.route.test_only then
        set_event(string.format("开始测试路径: %s 点数=%d", runtime.route.follow_name, #points))
    else
        set_event(string.format("开始运行路径: %s 点数=%d", runtime.route.follow_name, #points))
    end
    return true
end

    route_start_selected = function(attachRuntime, opts)
        local spec = route_selected_spec()
        return route_start_follow(spec.points_field, spec.name_field, attachRuntime, opts)
    end

function route_character_life_state()
    if not ok_core or not core then
        return false, false, nil, nil, nil, "aion.core unavailable"
    end

    local ok, char, err = core.getCharacter()
    if not ok or not char then
        return false, false, nil, nil, nil, err or "character unavailable"
    end

    local hp = tonumber(char.hp or char.HP or char.cur_hp or char.current_hp)
    local max_hp = tonumber(char.mhp or char.max_hp or char.maxHP or char.MaxHP)
    local dead = char.is_dead == true or char.dead == true or (hp ~= nil and hp <= 0)
    return true, dead, char, hp, max_hp, nil
end

function route_life_from_char(char)
    if type(char) ~= "table" then
        return false, nil, nil
    end
    local hp = tonumber(char.hp or char.HP or char.cur_hp or char.current_hp)
    local max_hp = tonumber(char.mhp or char.max_hp or char.maxHP or char.MaxHP)
    local dead = char.is_dead == true or char.dead == true or (hp ~= nil and hp <= 0)
    return dead, hp, max_hp
end

function route_recovery_clear_death_probe(reason)
    local rec = runtime.recovery
    if not rec then
        return
    end
    rec.death_probe_at = 0
    rec.death_probe_count = 0
    rec.death_probe_last_log_at = 0
    rec.death_probe_reason = tostring(reason or "")
end

function route_recovery_maybe_confirm_death(reason, char)
    local rec = runtime.recovery
    local dead, hp, max_hp
    local ok = true
    local err = nil
    if type(char) == "table" then
        dead, hp, max_hp = route_life_from_char(char)
    else
        ok, dead, char, hp, max_hp, err = route_character_life_state()
        if not ok then
            route_recovery_log("death confirm skipped: life state unavailable err=" .. tostring(err))
            return false
        end
    end

    if not dead then
        if (tonumber(rec.death_probe_count) or 0) > 0 then
            route_recovery_log("death probe cleared reason=" .. tostring(reason or "") ..
                " hp=" .. tostring(hp or "") .. "/" .. tostring(max_hp or ""))
        end
        route_recovery_clear_death_probe("alive")
        return false
    end

    if rec.active and auto_revive_is_death_phase(rec.phase) then
        return true
    end

    local now = now_seconds()
    if (tonumber(rec.death_probe_at) or 0) <= 0 then
        rec.death_probe_at = now
        rec.death_probe_count = 1
        rec.death_probe_last_log_at = 0
        rec.death_probe_reason = tostring(reason or "")
    else
        rec.death_probe_count = (tonumber(rec.death_probe_count) or 0) + 1
    end

    local confirm_seconds = math.max(0.1, tonumber(cfg.route and cfg.route.death_confirm_seconds) or 0.8)
    local confirm_count = math.max(1, tonumber(cfg.route and cfg.route.death_confirm_count) or 2)
    local elapsed = now - (tonumber(rec.death_probe_at) or now)
    if elapsed < confirm_seconds or (tonumber(rec.death_probe_count) or 0) < confirm_count then
        if now - (tonumber(rec.death_probe_last_log_at) or 0) >= 0.5 then
            rec.death_probe_last_log_at = now
            route_recovery_log("death probe pending reason=" .. tostring(reason or "") ..
                " count=" .. tostring(rec.death_probe_count) .. "/" .. tostring(confirm_count) ..
                " elapsed=" .. string.format("%.1f", elapsed) .. "/" .. string.format("%.1f", confirm_seconds) ..
                " hp=" .. tostring(hp or "") .. "/" .. tostring(max_hp or ""))
        end
        return false
    end

    route_recovery_log("death confirmed reason=" .. tostring(reason or "") ..
        " count=" .. tostring(rec.death_probe_count) ..
        " elapsed=" .. string.format("%.1f", elapsed) ..
        " hp=" .. tostring(hp or "") .. "/" .. tostring(max_hp or ""))
    route_recovery_clear_death_probe("confirmed")
    return true
end

local function route_is_dead(reason)
    return route_recovery_maybe_confirm_death(tostring(reason or "route-dead")) == true
end

function route_recovery_clear(reason)
    local rec = runtime.recovery
    rec.active = false
    rec.phase = "idle"
    rec.reason = tostring(reason or "")
    rec.wait_until = 0
    rec.route_after_return = "revive"
    rec.last_action_at = now_seconds()
    rec.last_revive_click_at = 0
    rec.revive_click_attempts = 0
    rec.revive_last_error = ""
    route_recovery_clear_death_probe(reason)
end

function route_recovery_finish(reason)
    route_recovery_clear(reason)
    combat_reset_runtime("recovery-finish:" .. tostring(reason or ""))
    runtime.status = "running"
    runtime.active_mode = primary_modes[cfg.primary_mode] or "unknown"
    route_recovery_log("finish reason=" .. tostring(reason or ""))
end

function route_recovery_start_revive(reason, start_pos, opts)
    opts = opts or {}
    local ok, points, warnings = route_read_points("revive_points")
    if not ok or #points <= 0 then
        route_recovery_log("revive route missing reason=" .. tostring(reason or "") .. " warnings=" .. tostring(warnings and warnings[1] or ""))
        route_recovery_finish("no-revive-route")
        return false
    end

    local start_nearest = opts.start_nearest
    if start_nearest == nil then
        start_nearest = true
    end
    local start_index = tonumber(opts.start_index)
    route_recovery_log("start revive route request reason=" .. tostring(reason or "") ..
        " points=" .. tostring(#points) ..
        " start_nearest=" .. tostring(start_nearest) ..
        " start_index=" .. tostring(start_index or "") ..
        " start_pos=" .. route_format_pos(start_pos) ..
        " first=" .. route_format_pos(points[1]) ..
        " last=" .. route_format_pos(points[#points]))

    local started = route_start_follow("revive_points", "revive_route_name", false, {
        start_nearest = start_nearest,
        start_index = start_index,
        start_pos = start_pos,
        loop = false,
        reverse_on_end = false,
    })
    if not started then
        route_recovery_log("revive route start failed reason=" .. tostring(reason or ""))
        route_recovery_finish("revive-start-failed")
        return false
    end

    local rec = runtime.recovery
    rec.active = true
    rec.phase = "revive-route"
    rec.reason = tostring(reason or "")
    rec.wait_until = 0
    rec.last_action_at = now_seconds()
    route_recovery_log("revive route started reason=" .. tostring(reason or ""))
    return true
end

function route_recovery_start_vendor(reason, start_pos)
    local ok, points = route_read_points("vendor_points")
    if not ok or #points <= 0 then
        route_recovery_log("vendor route missing, skip to revive reason=" .. tostring(reason or ""))
        return route_recovery_start_revive("vendor-missing", start_pos)
    end

    local started = route_start_follow("vendor_points", "vendor_route_name", false, {
        start_nearest = true,
        start_pos = start_pos,
        loop = false,
        reverse_on_end = false,
    })
    if not started then
        route_recovery_log("vendor route start failed, skip to revive reason=" .. tostring(reason or ""))
        return route_recovery_start_revive("vendor-start-failed", start_pos)
    end

    local rec = runtime.recovery
    rec.active = true
    rec.phase = "vendor-route"
    rec.reason = tostring(reason or "")
    rec.wait_until = 0
    rec.last_action_at = now_seconds()
    route_recovery_log("vendor route started reason=" .. tostring(reason or ""))
    return true
end

function route_recovery_press_return(reason, route_after_return)
    local ok_remote, remote_runtime = pcall(require, "aion.remote")
    if not ok_remote or not remote_runtime or type(remote_runtime.pressKey) ~= "function" then
        route_recovery_log("return key failed: aion.remote unavailable")
        return false
    end

    combat_auto_off("route-recovery-return", true)
    if route_stop_follow and runtime.route.following then
        route_stop_follow("route-recovery-return", false)
    end

    local keycode = tonumber(cfg.route.return_keycode) or 187
    local ok, _, err = remote_runtime.pressKey(keycode)
    if not ok then
        route_recovery_log("return key failed key=" .. tostring(keycode) .. " err=" .. tostring(err))
        return false
    end

    local rec = runtime.recovery
    rec.active = true
    rec.phase = "return-wait"
    rec.reason = tostring(reason or "")
    rec.route_after_return = tostring(route_after_return or "revive")
    rec.started_at = now_seconds()
    rec.wait_until = now_seconds() + math.max(1, tonumber(cfg.route.return_wait_seconds) or 8)
    rec.last_action_at = now_seconds()
    route_recovery_log("return key sent key=" .. tostring(keycode) .. " reason=" .. tostring(reason or "") .. " after=" .. rec.route_after_return)
    return true
end

function route_recovery_on_death(reason)
    local rec = runtime.recovery
    if rec.active and auto_revive_is_death_phase(rec.phase) then
        local clicked = false
        local click_err = nil
        clicked, click_err = auto_revive_try_click(reason, false)
        if clicked then
            rec.phase = "death-clicked"
            rec.wait_until = now_seconds() + route_post_revive_wait_seconds()
            rec.last_action_at = now_seconds()
            route_recovery_log("auto revive clicked while active phase=" .. tostring(rec.phase) ..
                " wait=" .. string.format("%.1f", route_post_revive_wait_seconds()) ..
                " reason=" .. tostring(reason or ""))
        elseif click_err and click_err ~= "throttled" then
            rec.revive_last_error = tostring(click_err)
        end
        return true
    end

    route_recovery_clear_death_probe("enter-death")
    combat_auto_off("route-recovery-death", true)
    if route_stop_follow and runtime.route.following then
        route_stop_follow("route-recovery-death", false)
    end

    local now = now_seconds()
    rec.active = true
    rec.phase = "death-revive"
    rec.reason = tostring(reason or "")
    rec.death_count = (tonumber(rec.death_count) or 0) + 1
    rec.started_at = now
    rec.wait_until = now + route_post_revive_wait_seconds()
    rec.last_action_at = now
    rec.last_revive_click_at = 0
    rec.revive_click_attempts = 0
    rec.revive_last_error = ""

    local clicked, click_err = auto_revive_try_click(reason, true)
    if clicked then
        rec.phase = "death-clicked"
        rec.wait_until = now_seconds() + route_post_revive_wait_seconds()
    end
    route_recovery_log("death detected reason=" .. tostring(reason or "") ..
        " auto_revive_clicked=" .. tostring(clicked) ..
        " err=" .. tostring(click_err or ""))
    return true
end

function route_recovery_on_route_complete(field)
    local rec = runtime.recovery
    if not rec.active then
        return false
    end

    if rec.phase == "vendor-route" and field == "vendor_points" then
        local pos_ok, pos = route_current_position()
        route_recovery_log("vendor route done, start revive")
        return route_recovery_start_revive("vendor-route-done", pos_ok and pos or nil)
    end

    if rec.phase == "revive-route" and field == "revive_points" then
        route_recovery_finish("revive-route-done")
        return true
    end

    return false
end

function route_recovery_stationary_anchor()
    if not cfg.combat then
        return nil
    end
    if tonumber(cfg.primary_mode) ~= 1 then
        return nil
    end
    if not sync_combat_enabled_from_primary_mode() or tonumber(cfg.combat.mode) ~= 1 then
        return nil
    end
    if type(combat_configured_anchor) ~= "function" then
        return nil
    end
    return combat_configured_anchor()
end

function route_recovery_is_near_combat_route(pos)
    if not pos or not cfg.combat then
        return false, nil
    end
    if tonumber(cfg.primary_mode) ~= 1 then
        return false, nil
    end
    if not sync_combat_enabled_from_primary_mode() or tonumber(cfg.combat.mode) ~= 2 then
        return false, nil
    end
    local ok, points = route_read_points("route_points")
    if not ok or not points or #points <= 0 then
        return false, nil
    end
    local nearest_index, nearest_dist = route_nearest_point(points, pos)
    local near = math.max(1, tonumber(cfg.route.start_near_radius) or 45)
    return nearest_dist <= near, {
        index = nearest_index,
        distance = nearest_dist,
        count = #points,
    }
end

function route_recovery_plan_start(reason)
    normalize_primary_mode()
    if primary_mode_ids[cfg.primary_mode] ~= "combat" then
        route_recovery_log("startup recovery skipped primary=" .. tostring(primary_mode_ids[cfg.primary_mode] or ""))
        return false
    end

    local full, used, threshold = route_inventory_is_full()
    if route_is_dead(tostring(reason or "start") .. ":dead") then
        route_recovery_on_death(tostring(reason or "start") .. ":dead")
        return true
    end

    if full then
        route_recovery_log("bag full used=" .. tostring(used) .. " threshold=" .. tostring(threshold))
        route_recovery_press_return("bag-full", "vendor")
        return true
    end

    local pos_ok, pos = route_current_position()
    if not pos_ok or not pos then
        route_recovery_log("startup position unavailable, continue normal")
        return false
    end

    local near_combat_route, combat_route_info = route_recovery_is_near_combat_route(pos)
    if near_combat_route then
        route_recovery_log("startup near combat route index=" ..
            tostring(combat_route_info and combat_route_info.index or "") ..
            "/" .. tostring(combat_route_info and combat_route_info.count or "") ..
            " dist=" .. string.format("%.1f", tonumber(combat_route_info and combat_route_info.distance) or 0) ..
            ", continue patrol combat")
        return false
    end

    local stationary_anchor = route_recovery_stationary_anchor()
    if stationary_anchor then
        local anchor_dist = route_distance3(pos, stationary_anchor)
        local near = math.max(1, tonumber(cfg.route.start_near_radius) or 45)
        if anchor_dist <= near then
            route_recovery_log("startup near stationary anchor dist=" ..
                string.format("%.1f", anchor_dist) .. ", continue combat")
            return false
        end
        route_recovery_log("startup far from stationary anchor dist=" ..
            string.format("%.1f", anchor_dist) .. ", check revive path/return")
    end

    local revive_ok, revive_points = route_read_points("revive_points")
    if revive_ok and #revive_points > 0 then
        local nearest_index, nearest_dist = route_nearest_point(revive_points, pos)
        local snap = math.max(1, tonumber(cfg.route.revive_path_snap_radius) or 45)
        local endpoint = revive_points[#revive_points]
        local hang_point = stationary_anchor or endpoint
        local endpoint_dist = route_distance3(pos, hang_point)
        local near = math.max(1, tonumber(cfg.route.start_near_radius) or 45)

        if endpoint_dist <= near then
            route_recovery_log("startup near hang point dist=" .. string.format("%.1f", endpoint_dist))
            local arrival_radius = math.max(0.5, tonumber(cfg.route.waypoint_radius) or 3)
            if endpoint_dist <= arrival_radius then
                route_recovery_log("startup at hang point, continue combat")
                return false
            end
            route_recovery_start_revive("startup-near-hang", pos)
            return true
        end

        if nearest_dist <= snap then
            route_recovery_log("startup on revive route nearest=" .. tostring(nearest_index) .. " dist=" .. string.format("%.1f", nearest_dist))
            route_recovery_start_revive("startup-on-revive-route", pos)
            return true
        end

        route_recovery_log("startup far from hang point dist=" .. string.format("%.1f", endpoint_dist) .. ", return first")
        route_recovery_press_return("startup-far", "revive")
        return true
    end

    route_recovery_log("startup no revive route, continue normal")
    return false
end

function route_recovery_tick()
    local rec = runtime.recovery
    if not runtime.running or runtime.paused then
        return
    end

    if route_is_dead("tick-dead") then
        route_recovery_on_death("tick-dead")
        return
    end

    if not rec.active then
        return
    end

    local now = now_seconds()
    if auto_revive_is_death_phase(rec.phase) then
        if now < (tonumber(rec.wait_until) or 0) then
            return
        end
        local life_ok, dead, char, hp, max_hp, life_err = route_character_life_state()
        if not life_ok then
            rec.wait_until = now + 0.5
            if now - (tonumber(rec.last_action_at) or 0) >= 2.0 then
                rec.last_action_at = now
                route_recovery_log("wait revive life state unavailable err=" .. tostring(life_err))
            end
            return
        end
        if dead then
            rec.wait_until = now + math.max(0.3, tonumber(cfg.route and cfg.route.revive_click_interval) or 0.8)
            auto_revive_try_click("tick-still-dead", false)
            return
        end

        local pos_ok, pos = route_current_position()
        route_recovery_log("alive after revive hp=" .. tostring(hp or "") .. "/" .. tostring(max_hp or "") ..
            " pos=" .. route_format_pos(pos_ok and pos or char) ..
            " phase=" .. tostring(rec.phase) ..
            " start revive route from first point")
        route_recovery_start_revive("after-auto-revive", nil, {
            start_nearest = false,
            start_index = 1,
        })
        return
    end

    if rec.phase == "return-wait" and now >= (tonumber(rec.wait_until) or 0) then
        local pos_ok, pos = route_current_position()
        if rec.route_after_return == "vendor" then
            route_recovery_start_vendor("after-return", pos_ok and pos or nil)
        else
            route_recovery_start_revive("after-return", pos_ok and pos or nil)
        end
        return
    end

end

local function route_send_move(target)
    if not target then
        return false, "target is nil"
    end

    local r = runtime.route or {}
    log_info(string.format("[AionRoute] move name=%s field=%s index=%s/%s target=%s",
        tostring(r.follow_name or ""),
        tostring(r.follow_field or ""),
        tostring(r.index or ""),
        tostring(type(r.points) == "table" and #r.points or ""),
        route_format_pos(target)))

    move_trace("route", target,
        "name=" .. tostring(r.follow_name or "") ..
        " field=" .. tostring(r.follow_field or "") ..
        " index=" .. tostring(r.index or "") .. "/" .. tostring(type(r.points) == "table" and #r.points or "") ..
        " reason=route_send_move",
        0)
    local ok, _, err = nav.moveTo(target.x, target.y, target.z)
    move_trace("route-result", target,
        "name=" .. tostring(r.follow_name or "") ..
        " field=" .. tostring(r.follow_field or "") ..
        " index=" .. tostring(r.index or "") .. "/" .. tostring(type(r.points) == "table" and #r.points or "") ..
        " ok=" .. tostring(ok) ..
        " err=" .. tostring(err or ""),
        0)
    if not ok then
        return false, err or "MoveTo failed"
    end
    return true, nil
end

local function route_advance_after_arrival()
    local r = runtime.route
    local old_index = r.index
    local route_loop = r.loop
    if route_loop == nil then
        route_loop = cfg.route.loop
    end
    local route_reverse = r.reverse_on_end
    if route_reverse == nil then
        route_reverse = cfg.route.reverse_on_end
    end
    local next_index, next_direction, done = route_lib.nextIndex(
        r.index,
        r.direction,
        #r.points,
        {
            loop = route_loop,
            reverse_on_end = route_reverse,
        }
    )

    if done then
        runtime.route.completed_field = r.follow_field
        route_stop_follow("路径完成", r.attach_runtime, r.follow_field)
        return
    end

    if route_loop and next_index == 1 and old_index ~= 1 then
        r.laps = r.laps + 1
    elseif route_reverse and next_direction ~= r.direction then
        r.laps = r.laps + 1
    end

    r.index = next_index
    r.direction = next_direction
    r.moving_to = nil
    r.status = string.format("到达 %d，下一个 %d/%d", old_index, r.index, #r.points)
end

local function route_handle_timeout(now)
    local r = runtime.route
    local moving = r.moving_to
    local timeout = tonumber(cfg.route.move_timeout) or 0
    if not moving or timeout <= 0 or now - moving.started_at <= timeout then
        return false
    end

    local max_retries = math.max(0, tonumber(cfg.route.max_waypoint_retries) or 0)
    if moving.tries < max_retries then
        moving.tries = moving.tries + 1
        moving.started_at = now
        moving.last_sent_at = 0
        r.status = string.format("路点 %d 超时，重试 %d/%d", r.index, moving.tries, max_retries)
        set_event(r.status)
        return false
    end

    r.error = string.format("路点 %d 超时", r.index)
    route_stop_follow(r.error, r.attach_runtime)
    return true
end

local function route_follow_tick()
    local r = runtime.route
    if not r.following or runtime.paused then
        return
    end

    if cfg.route.stop_on_death and route_is_dead("route-dead") then
        if route_recovery_on_death then
            route_recovery_on_death("route-dead")
            return
        end
        r.error = "角色死亡"
        route_stop_follow("死亡停止路径", r.attach_runtime)
        return
    end

    if not ok_core or not core then
        r.error = "aion.core 不可用"
        route_stop_follow(r.error, r.attach_runtime)
        return
    end

    local ok, pos, err = core.getPosition()
    if not ok or not pos then
        r.error = "坐标读取失败: " .. tostring(err)
        route_stop_follow(r.error, r.attach_runtime)
        return
    end

    local target = r.points[r.index]
    if not target then
        route_stop_follow("路径结束", r.attach_runtime, r.follow_field)
        return
    end

    local radius = math.max(0.5, tonumber(cfg.route.waypoint_radius) or 3)
    local dist = core.distance3(pos, target)
    if dist <= radius then
        route_advance_after_arrival()
        return
    end

    local now = now_seconds()
    if not r.moving_to or r.moving_to.index ~= r.index then
        r.moving_to = {
            index = r.index,
            started_at = now,
            last_sent_at = 0,
            tries = 0,
        }
    end

    if route_handle_timeout(now) then
        return
    end

    local resend = math.max(0.2, tonumber(cfg.route.resend_interval) or 2.5)
    if r.moving_to.last_sent_at > 0 and now - r.moving_to.last_sent_at < resend then
        r.status = string.format("移动中 %d/%d 距离 %.1f", r.index, #r.points, dist)
        return
    end

    local move_ok, move_err = route_send_move(target)
    if not move_ok then
        r.error = "移动指令失败: " .. tostring(move_err)
        route_stop_follow(r.error, r.attach_runtime)
        return
    end

    r.moving_to.last_sent_at = now
    r.last_move_at = now
    r.status = string.format("移动中 %d/%d 距离 %.1f", r.index, #r.points, dist)
end

local function route_record_tick()
    local r = runtime.route
    if not r.recording or runtime.paused then
        return
    end

    local now = now_seconds()
    local interval = math.max(0.2, tonumber(cfg.route.record_interval) or 1.5)
    if r.last_record_at > 0 and now - r.last_record_at < interval then
        return
    end

    r.last_record_at = now
    append_point(r.record_field, true)
end

local function route_tick()
    route_record_tick()
    route_follow_tick()
end

local function set_transfer_status(text)
    runtime.transfer.last_status = text
    set_event(text)
end

local function ensure_profile_io()
    if ok_profile_io and profile_io then
        return true
    end

    set_transfer_status("导入导出失败: aion.profile_io 不可用")
    return false
end

local function route_export_payload()
    route_ensure_saved_routes()

    local routes = {}
    for _, spec in ipairs(route_specs) do
        local points = {}
        local warnings = {}
        local distance = 0

        if ok_route and route_lib then
            local parse_ok, parsed, parsed_warnings = route_read_points(spec.points_field)
            if parse_ok then
                points = parsed or {}
                warnings = parsed_warnings or {}
                distance = route_lib.stats(points).distance or 0
            end
        end

        routes[#routes + 1] = {
            id = spec.points_field,
            label = spec.label,
            description = spec.description,
            name_field = spec.name_field,
            points_field = spec.points_field,
            name = cfg.route[spec.name_field],
            points_text = cfg.route[spec.points_field],
            points = points,
            distance = distance,
            warning_count = #warnings,
        }
    end

    return {
        selected_route = cfg.route.selected_route,
        settings = {
            loop = cfg.route.loop,
            reverse_on_end = cfg.route.reverse_on_end,
            stop_on_death = cfg.route.stop_on_death,
            record_interval = cfg.route.record_interval,
            min_record_distance = cfg.route.min_record_distance,
            waypoint_radius = cfg.route.waypoint_radius,
            move_timeout = cfg.route.move_timeout,
            resend_interval = cfg.route.resend_interval,
            max_waypoint_retries = cfg.route.max_waypoint_retries,
        },
        routes = routes,
        saved_routes = clone_value(cfg.route.saved_routes or {}),
    }
end

local function apply_route_payload(payload)
    if type(payload) ~= "table" then
        return false, "路径包数据无效"
    end

    route_ensure_saved_routes()

    if type(payload.settings) == "table" then
        merge_table(cfg.route, payload.settings)
        normalize_route_config()
    end

    if payload.selected_route ~= nil then
        cfg.route.selected_route = math.max(1, math.min(#route_specs, tonumber(payload.selected_route) or 1))
    end

    if type(payload.saved_routes) == "table" then
        cfg.route.saved_routes = clone_value(payload.saved_routes)
        route_ensure_saved_routes()
    end

    local imported = 0
    for _, item in ipairs(payload.routes or {}) do
        local name_field = item.name_field
        local points_field = item.points_field
        local imported_points = false

        if type(name_field) == "string" and cfg.route[name_field] ~= nil then
            cfg.route[name_field] = tostring(item.name or cfg.route[name_field] or "")
        end

        if type(points_field) == "string" and cfg.route[points_field] ~= nil then
            if type(item.points_text) == "string" then
                cfg.route[points_field] = item.points_text
                imported = imported + 1
                imported_points = true
            elseif ok_route and route_lib and type(item.points) == "table" then
                cfg.route[points_field] = route_lib.serialize(item.points)
                imported = imported + 1
                imported_points = true
            end
        end

        if imported_points and type(name_field) == "string" and cfg.route[name_field] ~= nil then
            route_save_current(points_field, name_field, true)
        end
    end

    route_ensure_saved_routes()
    normalize_route_config()
    return true, imported
end

local function export_route_config()
    if not ensure_profile_io() then
        return
    end

    local path = cfg.transfer.route_export_path
    local ok, err = profile_io.writePackage(path, "aion_routes", route_export_payload())
    if not ok then
        set_transfer_status("路径配置导出失败: " .. tostring(err))
        return
    end

    set_transfer_status("路径配置已导出: " .. tostring(path))
end

local function import_route_config()
    if not ensure_profile_io() then
        return
    end

    local path = cfg.transfer.route_import_path
    local ok, package, err = profile_io.readPackage(path, "aion_routes")
    if not ok then
        set_transfer_status("路径配置导入失败: " .. tostring(err))
        return
    end

    local apply_ok, imported_or_err = apply_route_payload(package.payload)
    if not apply_ok then
        set_transfer_status("路径配置导入失败: " .. tostring(imported_or_err))
        return
    end

    save_config()
    set_transfer_status(string.format("路径配置已导入: %s 路径数 %d", tostring(path), imported_or_err or 0))
end

local function export_profile_config()
    if not ensure_profile_io() then
        return
    end

    local path = cfg.transfer.profile_export_path
    local payload = {
        note = "This profile contains script UI settings only. It does not include engine config.json, keys, DLLs, or account data.",
        config = config_snapshot(),
    }

    local ok, err = profile_io.writePackage(path, "aion_control_profile", payload)
    if not ok then
        set_transfer_status("整体配置导出失败: " .. tostring(err))
        return
    end

    set_transfer_status("整体配置已导出: " .. tostring(path))
end

local function import_profile_config()
    if not ensure_profile_io() then
        return
    end

    local path = cfg.transfer.profile_import_path
    local ok, package, err = profile_io.readPackage(path, "aion_control_profile")
    if not ok then
        set_transfer_status("整体配置导入失败: " .. tostring(err))
        return
    end

    local payload = package.payload or {}
    local apply_ok, apply_err = apply_config_snapshot(payload.config)
    if not apply_ok then
        set_transfer_status("整体配置导入失败: " .. tostring(apply_err))
        return
    end

    save_config()
    set_transfer_status("整体配置已导出: " .. tostring(path))
end

local function help_marker(text)
    imgui.same_line()
    imgui.text_disabled("(?)")
    if imgui.is_item_hovered() then
        imgui.begin_tooltip()
        imgui.text(text)
        imgui.end_tooltip()
    end
end

local function draw_header()
    local bw, bh = 90, 28
    if imgui.button("保存配置", bw, bh) then
        save_config()
    end

    imgui.same_line()
    if imgui.button("加载配置", bw, bh) then
        load_config()
    end

    imgui.separator()
end

local function draw_target_selector()
    local changed, val

    imgui.spacing()
    imgui.text("目标窗口")
    imgui.separator()

    changed, val = imgui.checkbox("启用 PID 锁定", cfg.target.enabled)
    if changed then cfg.target.enabled = val end

    imgui.same_line()
    changed, val = imgui.checkbox("启动时必须匹配", cfg.target.require_match_on_start)
    if changed then cfg.target.require_match_on_start = val end

    imgui.same_line()
    if imgui.button("刷新窗口", 88, 24) then
        target_refresh(true)
    end

    imgui.same_line()
    if imgui.button("Auto Aion.bin", 110, 24) then
        target_select_single_game_process()
    end

    imgui.same_line()
    if imgui.button("绑定前台窗口", 110, 24) then
        target_select_foreground()
    end

    imgui.set_next_item_width(520)
    changed, val = imgui.combo("角色/PID", runtime.target.selected_index, runtime.target.labels)
    if changed then
        target_select_index(val)
    end

    imgui.text(string.format("选中: PID %s  HWND %s  标题 %s",
        tostring(cfg.target.pid or 0),
        tostring(cfg.target.hwnd or 0),
        tostring(cfg.target.title or "")))

    imgui.text(string.format("AionData: PID %s  HWND %s  状态 %s",
        tostring(runtime.target.bound_pid or 0),
        tostring(runtime.target.bound_hwnd or 0),
        tostring(runtime.target.binding_status or "")))

    imgui.text(string.format("前台: PID %s  HWND %s  标题 %s",
        tostring(runtime.target.foreground_pid or 0),
        tostring(runtime.target.foreground_hwnd or 0),
        tostring(runtime.target.foreground_title or "")))

    if runtime.target.binding_message ~= "" then
        imgui.text("校验: " .. tostring(runtime.target.binding_message))
    end
    if runtime.target.last_error ~= "" then
        imgui.text("扫描错误: " .. tostring(runtime.target.last_error))
    end
end

local function account_table_text(value)
    imgui.text(tostring(value or ""))
end

local function account_row_double_clicked()
    return imgui.is_item_hovered
        and imgui.is_mouse_double_clicked
        and imgui.is_item_hovered()
        and imgui.is_mouse_double_clicked(0)
end

local function draw_account_import_panel()
    if not runtime.accounts.show_import then
        return
    end

    imgui.spacing()
    imgui.text("导入账号")
    imgui.text("每行格式: account,password,second_password")
    imgui.set_next_item_width(620)
    local changed, val = imgui.input_text_multiline("##account_import_text", runtime.accounts.import_text, 620, 110)
    if changed then runtime.accounts.import_text = val end

    if imgui.button("确认导入", 100, 26) then
        account_import_text()
        runtime.accounts.show_import = false
    end
    imgui.same_line()
    if imgui.button("取消导入", 100, 26) then
        runtime.accounts.show_import = false
    end
end

local function draw_account_login_common_panel()
    local changed, val

    imgui.text("自动登录公共参数")
    imgui.same_line()
    if imgui.button("保存登录参数", 110, 26) then
        account_save_domain()
        set_event("自动登录公共参数已保存")
    end
    imgui.separator()

    changed, val = imgui.checkbox("登录完成后自动启动挂机##account_auto_start_after_login", cfg.accounts.auto_start_after_login == true)
    if changed then cfg.accounts.auto_start_after_login = val == true end

    imgui.spacing()

    imgui.set_next_item_width(620)
    changed, val = imgui.input_text("游戏路径", cfg.accounts.game_path)
    if changed then cfg.accounts.game_path = val end

    imgui.set_next_item_width(620)
    changed, val = imgui.input_text("Purple 根目录", cfg.accounts.purple_root)
    if changed then cfg.accounts.purple_root = val end

    imgui.spacing()
end

local function draw_accounts_overview()
    draw_account_login_common_panel()

    if imgui.button("新增账号", 90, 26) then
        account_open_add_window()
    end
    imgui.same_line()
    if imgui.button("全部启动", 90, 26) then
        account_start_runtime_all()
    end
    imgui.same_line()
    if imgui.button("全部停止", 90, 26) then
        account_stop_runtime_all()
    end
    imgui.same_line()
    if imgui.button("刷新审计", 90, 26) then
        account_poll(true)
    end

    draw_account_import_panel()

    imgui.spacing()
    local items = account_items()
    if #items == 0 then
        imgui.text("No account. Add one or import account,password,second_password.")
        return
    end

    local table_flags = imgui.TableFlags_Borders + imgui.TableFlags_RowBg + imgui.TableFlags_Resizable
    if imgui.begin_table("##account_overview_table", 8, table_flags) then
        imgui.table_setup_column("账号", imgui.TableColumnFlags_WidthFixed, 74)
        imgui.table_setup_column("角色", imgui.TableColumnFlags_WidthFixed, 62)
        imgui.table_setup_column("PID", imgui.TableColumnFlags_WidthFixed, 48)
        imgui.table_setup_column("状态", imgui.TableColumnFlags_WidthFixed, 58)
        imgui.table_setup_column("金币/h", imgui.TableColumnFlags_WidthFixed, 54)
        imgui.table_setup_column("杀怪/h", imgui.TableColumnFlags_WidthFixed, 54)
        imgui.table_setup_column("时长", imgui.TableColumnFlags_WidthFixed, 64)
        imgui.table_setup_column("操作", imgui.TableColumnFlags_WidthStretch)
        imgui.table_headers_row()

        for index, account in ipairs(items) do
            local audit = account.audit or {}
            local target = account.target or {}
            local account_runtime = account.runtime or {}
            local selected = runtime.accounts.selected_index == index
            local stale_pid = tonumber(target.pid) or 0
            if stale_pid > 0 and not account_pid_is_alive(stale_pid) then
                if account_clear_stale_target(account, stale_pid) then
                    runtime.accounts.last_status = "game exited; cleared pid " .. tostring(stale_pid)
                    account_save_domain()
                    audit = account.audit or {}
                    target = account.target or {}
                    account_runtime = account.runtime or {}
                end
            end

            imgui.table_next_row()
            imgui.table_next_column()
            if imgui.selectable(account_display_name(account) .. "##account_row_" .. tostring(index), selected) then
                account_select(index, false)
            end
            if account_row_double_clicked() then
                account_open_settings(account, index)
            end

            imgui.table_next_column()
            account_table_text(target.character_name)

            imgui.table_next_column()
            account_table_text(target.pid or 0)

            imgui.table_next_column()
            local status_text = tostring(account_runtime.status or "idle")
            if status_text == "idle" and account.login then
                status_text = tostring(account.login.status or "idle")
            end
            account_table_text(status_text)

            imgui.table_next_column()
            account_table_text(string.format("%.0f", tonumber(audit.kinah_per_hour) or 0))

            imgui.table_next_column()
            account_table_text(string.format("%.1f", tonumber(audit.kills_per_hour) or 0))

            imgui.table_next_column()
            account_table_text(format_duration(audit.runtime_seconds or 0))

            imgui.table_next_column()
            if imgui.small_button("登录##account_login_" .. tostring(index)) then
                account_request_login(account, index)
            end
            imgui.same_line()
            if imgui.small_button("设置##account_settings_" .. tostring(index)) then
                account_open_settings(account, index)
            end
            imgui.same_line()
            if imgui.small_button("启动##account_run_" .. tostring(index)) then
                if (tonumber(account.target and account.target.pid) or 0) > 0 then
                    account_apply_to_target(account)
                end
                account_queue_local_script("start", account, index)
            end
            imgui.same_line()
            if imgui.small_button("停止##account_stop_" .. tostring(index)) then
                account_queue_local_script("stop", account, index)
            end
            imgui.same_line()
            if imgui.small_button("删除##account_delete_" .. tostring(index)) then
                account_select(index, false)
                account_remove_selected()
                return
            end
        end

        imgui.end_table()
    end

end

local function draw_account_settings()
    local account = selected_account()
    local changed, val

    imgui.text("账号设置")
    imgui.separator()

    if not account then
        imgui.text("请先在账号总览中选择账号。")
        return
    end

    imgui.text("脚本状态" .. tostring(account.runtime.status or "idle") ..
        " | " .. tostring(account.runtime.message or ""))
    imgui.text("账号PID: " .. tostring(account.target and account.target.pid or 0) ..
        " | 当前脚本PID: " .. tostring(cfg.target and cfg.target.pid or 0))

    imgui.spacing()
    changed, val = imgui.checkbox("启用账号", account.enabled)
    if changed then account.enabled = val end

    imgui.set_next_item_width(320)
    changed, val = imgui.input_text("账号", account.account)
    if changed then account.account = val end

    imgui.set_next_item_width(320)
    changed, val = imgui.input_text("密码", account.password)
    if changed then account.password = val end

    imgui.set_next_item_width(320)
    changed, val = imgui.input_text("二级密码", account.second_password)
    if changed then account.second_password = val end

    imgui.spacing()
    draw_account_identity_fields(account, "##account_settings")
end

local function draw_accounts_tab()
    draw_accounts_overview()
end

local function draw_overview_tab()
    local changed, val

    imgui.text("方案")
    imgui.set_next_item_width(220)
    changed, val = imgui.input_text("方案名", cfg.profile_name)
    if changed then cfg.profile_name = val end

    normalize_primary_mode()
    imgui.set_next_item_width(220)
    changed, val = imgui.combo("##primary_mode_priority", cfg.primary_mode, primary_modes)
    imgui.same_line()
    imgui.text_colored(0.92, 0.22, 0.08, 1.0, "主模式")
    if changed then
        cfg.primary_mode = val
        sync_combat_enabled_from_primary_mode()
        save_config()
    end

    if type(draw_primary_mode_linked_panel) == "function" then
        draw_primary_mode_linked_panel()
    else
        imgui.text("主模式面板尚未初始化")
    end
end

function combat_config_signature()
    local c = cfg.combat or {}
    return table.concat({
        tostring(c.enabled),
        tostring(c.mode),
        tostring(c.target_policy),
        tostring(c.anchor_enabled),
        tostring(c.anchor_x),
        tostring(c.anchor_y),
        tostring(c.anchor_z),
        tostring(c.radius),
        tostring(c.min_level),
        tostring(c.max_level),
        tostring(c.return_radius),
        tostring(c.tick_interval),
        tostring(c.move_resend_interval),
        tostring(c.attack_trigger_mode),
        tostring(c.attack_keycode),
        tostring(c.attack_key_repeat_interval_ms),
        tostring(c.target_no_damage_seconds),
        tostring(c.target_ignore_seconds),
        tostring(c.stop_move_on_target),
        tostring(c.loot_enabled),
        tostring(c.loot_radius),
        tostring(c.loot_interact_range),
        tostring(c.loot_keycode),
        tostring(c.loot_retry_interval),
        tostring(c.loot_max_attempts),
        tostring(c.post_kill_check_delay_seconds),
        tostring(c.auto_refresh_interval),
        tostring(c.debug_log),
        tostring(c.debug_log_interval),
        tostring(c.prefer_quest_targets),
        tostring(c.avoid_elite),
        tostring(c.keep_auto_battle),
        tostring(c.allow_kill_steal),
        tostring(c.counter_enemy_race),
        tostring(c.target_names),
        tostring(c.blacklist_names),
        tostring(c.ignore_summons),
        tostring(c.pet_names),
    }, "\30")
end

function save_combat_config_if_changed(before)
    if tostring(before or "") ~= combat_config_signature() and type(save_config) == "function" then
        save_config()
    end
end

local function draw_combat_tab()
    local changed, val
    local persist_before = combat_config_signature()

    normalize_combat_mode()
    imgui.set_next_item_width(220)
    changed, val = imgui.combo("打怪模式", cfg.combat.mode, combat_modes)
    if changed then
        cfg.combat.mode = val
        save_config()
    end
    if tonumber(cfg.combat.mode) == 2 then
        imgui.text_colored(0.92, 0.22, 0.08, 1.0, "先录制打怪路径")
    end

    if tonumber(cfg.combat.mode) == 1 then
        imgui.same_line()
        if imgui.button("设当前坐标为原地打怪坐标", 210, 24) then
            combat_set_stationary_anchor_current()
        end
        if cfg.combat.anchor_enabled then
            imgui.text(string.format("原地打怪坐标: %.2f, %.2f, %.2f",
                tonumber(cfg.combat.anchor_x) or 0,
                tonumber(cfg.combat.anchor_y) or 0,
                tonumber(cfg.combat.anchor_z) or 0))
        else
            imgui.text("原地打怪坐标: 未设置，启动时使用当前位置")
        end
    end

    changed, val = imgui.checkbox("启用拾取", cfg.combat.loot_enabled)
    if changed then
        cfg.combat.loot_enabled = val
        if not val then
            runtime.combat.post_kill_until = 0
            runtime.combat.post_kill_started_at = 0
            runtime.combat.last_killed_obj = 0
            runtime.combat.last_killed_interact_id = 0
            runtime.combat.last_killed_name = ""
            runtime.combat.loot_obj = 0
            runtime.combat.loot_name = ""
            runtime.combat.loot_distance = 0
            runtime.combat.loot_attempts = 0
        end
    end

    imgui.same_line()
    changed, val = imgui.checkbox("抢怪", cfg.combat.allow_kill_steal)
    if changed then cfg.combat.allow_kill_steal = val end

    imgui.same_line()
    changed, val = imgui.checkbox("反击敌对种族", cfg.combat.counter_enemy_race)
    if changed then cfg.combat.counter_enemy_race = val end

    if imgui.collapsing_header("高级打怪设置") then
    normalize_priority_mode()
    imgui.set_next_item_width(220)
    changed, val = imgui.combo("优先级", cfg.priority_mode, priority_modes)
    if changed then
        cfg.priority_mode = val
        save_config()
    end

    imgui.set_next_item_width(220)
    changed, val = imgui.combo("目标策略", cfg.combat.target_policy, combat_target_policies)
    if changed then cfg.combat.target_policy = val end

    changed, val = imgui.checkbox("使用游戏自动战斗", tonumber(cfg.combat.attack_trigger_mode) == 2)
    if changed then cfg.combat.attack_trigger_mode = val and 2 or 1 end
    if tonumber(cfg.combat.attack_trigger_mode) ~= 2 then
        imgui.text(string.format("Key mode: press %s every %dms while target is alive, keycode %d",
            ATTACK_KEY_LABEL,
            tonumber(cfg.combat.attack_key_repeat_interval_ms) or 1000,
            ATTACK_KEYCODE))
    end

    imgui.set_next_item_width(90)
    changed, val = imgui.input_int("Press C interval ms", cfg.combat.attack_key_repeat_interval_ms)
    if changed then cfg.combat.attack_key_repeat_interval_ms = math.max(250, math.min(3000, val)) end

    imgui.set_next_item_width(90)
    changed, val = imgui.input_float("无伤害超时秒", cfg.combat.target_no_damage_seconds)
    if changed then cfg.combat.target_no_damage_seconds = math.max(1.0, val) end

    imgui.same_line()
    imgui.set_next_item_width(90)
    changed, val = imgui.input_float("失败忽略秒", cfg.combat.target_ignore_seconds)
    if changed then cfg.combat.target_ignore_seconds = math.max(1.0, val) end

    imgui.set_next_item_width(90)
    changed, val = imgui.input_int("搜索半径", cfg.combat.radius)
    if changed then cfg.combat.radius = math.max(1, val) end

    imgui.same_line()
    imgui.set_next_item_width(90)
    changed, val = imgui.input_int("回中心半径", cfg.combat.return_radius)
    if changed then cfg.combat.return_radius = math.max(1, val) end

    imgui.set_next_item_width(90)
    changed, val = imgui.input_int("最低等级", cfg.combat.min_level)
    if changed then cfg.combat.min_level = math.max(1, val) end

    imgui.same_line()
    imgui.set_next_item_width(90)
    changed, val = imgui.input_int("最高等级", cfg.combat.max_level)
    if changed then cfg.combat.max_level = math.max(cfg.combat.min_level, val) end

    changed, val = imgui.checkbox("优先任务目标", cfg.combat.prefer_quest_targets)
    if changed then cfg.combat.prefer_quest_targets = val end

    changed, val = imgui.checkbox("避开精英/高危目标", cfg.combat.avoid_elite)
    if changed then cfg.combat.avoid_elite = val end

    changed, val = imgui.checkbox("保持自动战斗状态", cfg.combat.keep_auto_battle)
    if changed then cfg.combat.keep_auto_battle = val end

    imgui.set_next_item_width(90)
    changed, val = imgui.input_int("拾取半径", cfg.combat.loot_radius)
    if changed then cfg.combat.loot_radius = math.max(1, val) end

    imgui.same_line()
    imgui.set_next_item_width(90)
    changed, val = imgui.input_int("拾取交互距离", cfg.combat.loot_interact_range)
    if changed then cfg.combat.loot_interact_range = math.max(1, val) end

    imgui.set_next_item_width(90)
    changed, val = imgui.input_int("拾取交互键码", cfg.combat.loot_keycode)
    if changed then cfg.combat.loot_keycode = math.max(1, val) end

    imgui.same_line()
    imgui.set_next_item_width(90)
    changed, val = imgui.input_int("拾取最大尝试", cfg.combat.loot_max_attempts)
    if changed then cfg.combat.loot_max_attempts = math.max(1, math.min(2, val)) end

    imgui.same_line()
    imgui.set_next_item_width(90)
    changed, val = imgui.input_float("死亡判定延迟秒", cfg.combat.post_kill_check_delay_seconds)
    if changed then cfg.combat.post_kill_check_delay_seconds = math.max(0.05, math.min(0.50, val)) end

    changed, val = imgui.checkbox("调试打怪日志", cfg.combat.debug_log)
    if changed then cfg.combat.debug_log = val end

    imgui.same_line()
    imgui.set_next_item_width(90)
    changed, val = imgui.input_float("日志间隔秒", cfg.combat.debug_log_interval)
    if changed then cfg.combat.debug_log_interval = math.max(0.2, val) end

    imgui.separator()
    imgui.text("原地打怪状态: " .. tostring(runtime.combat.status or ""))
    imgui.text("中心点: " .. combat_anchor_text() ..
        " | 距中心: " .. string.format("%.1f", tonumber(runtime.combat.anchor_distance) or 0))
    imgui.text("当前目标: " .. tostring(runtime.combat.target_name or "") ..
        " | 距离: " .. string.format("%.1f", tonumber(runtime.combat.target_distance) or 0))
    if tonumber(cfg.combat.mode) == 2 then
        imgui.text("巡逻状态: " .. combat_patrol_text() ..
            " | 路径名: " .. tostring(runtime.combat.patrol_route_name or cfg.route.route_name or ""))
    end
    if cfg.combat.loot_enabled then
        imgui.text("拾取目标: " .. tostring(runtime.combat.loot_name or "") ..
            " | 距离: " .. string.format("%.1f", tonumber(runtime.combat.loot_distance) or 0) ..
            " | 尝试: " .. tostring(runtime.combat.loot_attempts or 0))
    end
    if runtime.combat.last_error ~= "" then
        imgui.text("打怪提示: " .. tostring(runtime.combat.last_error))
    end
    if tonumber(cfg.combat.mode) == 1 then
        if imgui.button("重置原地中心", 120, 26) then
            combat_reset_runtime("manual reset")
        end
    end

    imgui.text("指定目标名")
    imgui.set_next_item_width(420)
    changed, val = imgui.input_text_multiline("##combat_targets", cfg.combat.target_names, 420, 80)
    if changed then cfg.combat.target_names = val end

    imgui.text("黑名单目标名")
    imgui.set_next_item_width(420)
    changed, val = imgui.input_text_multiline("##combat_blacklist", cfg.combat.blacklist_names, 420, 80)
    if changed then cfg.combat.blacklist_names = val end

    end
    save_combat_config_if_changed(persist_before)
end

local function draw_gather_tab()
    local changed, val

    changed, val = imgui.checkbox("启用采集", cfg.gather.enabled)
    if changed then cfg.gather.enabled = val end

    imgui.set_next_item_width(220)
    changed, val = imgui.combo("采集模式", cfg.gather.mode, gather_modes)
    if changed then cfg.gather.mode = val end

    changed, val = imgui.input_int("搜索半径##gather", cfg.gather.radius)
    if changed then cfg.gather.radius = math.max(1, val) end

    changed, val = imgui.checkbox("草药", cfg.gather.gather_herb)
    if changed then cfg.gather.gather_herb = val end

    imgui.same_line()
    changed, val = imgui.checkbox("矿物", cfg.gather.gather_ore)
    if changed then cfg.gather.gather_ore = val end

    imgui.same_line()
    changed, val = imgui.checkbox("资源名", cfg.gather.gather_resource)
    if changed then cfg.gather.gather_resource = val end

    changed, val = imgui.checkbox("战斗后顺手采集", cfg.gather.gather_after_combat)
    if changed then cfg.gather.gather_after_combat = val end

    imgui.text("优先资源名")
    imgui.set_next_item_width(420)
    changed, val = imgui.input_text_multiline("##gather_names", cfg.gather.resource_names, 420, 90)
    if changed then cfg.gather.resource_names = val end

    imgui.text("资源黑名单")
    imgui.set_next_item_width(420)
    changed, val = imgui.input_text_multiline("##gather_blacklist", cfg.gather.blacklist_names, 420, 90)
    if changed then cfg.gather.blacklist_names = val end
end

function skill_type_label(type_id)
    type_id = tonumber(type_id) or 0
    if type_id == 2 then
        return "主动"
    end
    if type_id == 3 then
        return "提取"
    end
    if type_id == 8 then
        return "被动"
    end
    if type_id == -1 then
        return "查表失败"
    end
    return "未知(" .. tostring(type_id) .. ")"
end

function skill_auto_id_text(list)
    local ids = {}
    for _, item in ipairs(list or {}) do
        if type(item) == "table" then
            ids[#ids + 1] = tostring(item.id or item.skill_id or item.skillId or "")
        else
            ids[#ids + 1] = tostring(item)
        end
    end
    if #ids == 0 then
        return "?"
    end
    return table.concat(ids, ", ")
end

function skill_trim(text)
    text = tostring(text or "")
    text = text:gsub("^%s+", "")
    text = text:gsub("%s+$", "")
    return text
end

function skill_extract_identity(skill)
    skill = skill or {}
    local id = skill_trim(skill.id or skill.skill_id or skill.skillId or "")
    local name = skill.name or skill.skill_name or skill.skillName or skill.name_ko or skill.name_kr or skill.name_cn or skill.name_en or ""
    return id, skill_trim(name)
end

function skill_translation_maps()
    local maps = {
        by_id = {},
        by_name = {},
        by_pair = {},
    }
    local text = tostring((cfg.skills and cfg.skills.translation_map) or "")
    for line in (text .. "\n"):gmatch("([^\n]*)\n") do
        line = line:gsub("\r$", "")
        line = skill_trim(line)
        if line ~= "" and line:sub(1, 1) ~= "#" then
            local key, value = line:match("^([^=]+)=(.*)$")
            key = skill_trim(key)
            value = skill_trim(value)
            if key ~= "" and value ~= "" then
                local pair_id, pair_name = key:match("^([^|]+)|(.+)$")
                if pair_id and pair_name then
                    maps.by_pair[skill_trim(pair_id) .. "|" .. skill_trim(pair_name)] = value
                elseif key:match("^%d+$") then
                    maps.by_id[key] = value
                else
                    maps.by_name[key] = value
                end
            end
        end
    end
    return maps
end

function skill_translated_name(skill, maps)
    local id, raw_name = skill_extract_identity(skill)
    if not cfg.skills.translate_names then
        return raw_name, raw_name, false
    end

    maps = maps or skill_translation_maps()
    local translated = nil
    if id ~= "" and raw_name ~= "" then
        translated = maps.by_pair[id .. "|" .. raw_name]
    end
    if not translated and id ~= "" then
        translated = maps.by_id[id]
    end
    if not translated and raw_name ~= "" then
        translated = maps.by_name[raw_name]
    end
    if translated and translated ~= "" then
        return translated, raw_name, true
    end
    return raw_name, raw_name, false
end

function skill_append_translation_template(skills)
    if not cfg.skills then
        return 0
    end

    local current = tostring(cfg.skills.translation_map or "")
    local existing = {}
    for line in (current .. "\n"):gmatch("([^\n]*)\n") do
        line = line:gsub("\r$", "")
        local key = line:match("^([^=]+)=")
        key = skill_trim(key)
        if key ~= "" then
            existing[key] = true
        end
    end

    local added = {}
    for _, skill in ipairs(skills or {}) do
        local id, raw_name = skill_extract_identity(skill)
        if raw_name ~= "" then
            local key = raw_name
            if id ~= "" and id ~= "0" then
                key = id .. "|" .. raw_name
            end
            if not existing[key] and not existing[id] and not existing[raw_name] then
                added[#added + 1] = key .. "="
                existing[key] = true
            end
        end
    end

    if #added == 0 then
        return 0
    end
    if current ~= "" and not current:match("[\r\n]$") then
        current = current .. "\n"
    end
    cfg.skills.translation_map = current .. table.concat(added, "\n")
    return #added
end

function skill_level_value(skill)
    skill = skill or {}
    return tonumber(skill.level or skill.lv or skill.skill_level or skill.skillLevel) or 0
end

function skill_order_line(skill)
    local id, raw_name = skill_extract_identity(skill)
    if id ~= "" and id ~= "0" and raw_name ~= "" then
        return id .. "|" .. raw_name
    end
    if id ~= "" and id ~= "0" then
        return id
    end
    return raw_name
end

function skill_group_key_from_name(name)
    name = skill_trim(name)
    name = name:gsub("%s+[IVXLCDM]+$", "")
    name = name:gsub("%s+[ⅠⅡⅢⅣⅤⅥⅦⅧⅨⅩ]+$", "")
    name = name:gsub("%s+%d+$", "")
    name = name:gsub("%s+", " ")
    return string.lower(name)
end

function skill_group_key(skill)
    local _, raw_name = skill_extract_identity(skill)
    return skill_group_key_from_name(raw_name)
end

function skill_roman_rank_value(text)
    text = skill_trim(text)
    local token = text:match("%s+([IVXLCDM]+)$")
    if not token then
        token = text:match("%s+([ⅠⅡⅢⅣⅤⅥⅦⅧⅨⅩ]+)$")
    end
    if not token then
        token = text:match("%s+(%d+)$")
    end
    if not token then
        return 0
    end

    local unicode_ranks = {}
    if unicode_ranks[token] then
        return unicode_ranks[token]
    end

    local numeric = tonumber(token)
    if numeric then
        return numeric
    end

    local values = { I = 1, V = 5, X = 10, L = 50, C = 100, D = 500, M = 1000 }
    local total = 0
    local last = 0
    for i = #token, 1, -1 do
        local value = values[token:sub(i, i)] or 0
        if value < last then
            total = total - value
        else
            total = total + value
            last = value
        end
    end
    return total
end

function skill_label(skill, maps)
    local id, raw_name = skill_extract_identity(skill)
    local name, _, translated = skill_translated_name(skill, maps)
    if cfg.skills.translate_names and translated and raw_name ~= "" and name ~= raw_name then
        name = name .. " (" .. raw_name .. ")"
    elseif cfg.skills.translate_names and raw_name ~= "" and not translated then
        name = name .. " [未译]"
    end

    local level = skill_level_value(skill)
    local type_name = skill.type_name or skill_type_label(skill.type)
    if id ~= "" and id ~= "0" then
        return string.format("%s  id=%s  Lv.%d  %s", name, id, level, tostring(type_name))
    end
    return string.format("%s  Lv.%d  %s", name, level, tostring(type_name))
end

function skill_highest_learned(skills, maps)
    local by_group = {}
    local order = {}

    for _, skill in ipairs(skills or {}) do
        local _, raw_name = skill_extract_identity(skill)
        if raw_name ~= "" then
            local group = skill_group_key(skill)
            if group ~= "" then
                local current = by_group[group]
                local level = skill_level_value(skill)
                local id = tonumber((skill_extract_identity(skill))) or 0
                local rank = skill_roman_rank_value(raw_name)
                if not current then
                    by_group[group] = {
                        skill = skill,
                        group = group,
                        level = level,
                        id = id,
                        rank = rank,
                    }
                    order[#order + 1] = group
                elseif level > current.level or
                    (level == current.level and rank > (current.rank or 0)) or
                    (level == current.level and rank == (current.rank or 0) and id > current.id) then
                    current.skill = skill
                    current.level = level
                    current.id = id
                    current.rank = rank
                end
            end
        end
    end

    local result = {}
    for _, group in ipairs(order) do
        local item = by_group[group]
        if item and item.skill then
            result[#result + 1] = {
                skill = item.skill,
                group = group,
                key = skill_order_line(item.skill),
                label = skill_label(item.skill, maps),
            }
        end
    end
    return result
end

function skill_order_entries()
    local entries = {}
    local seen = {}
    local text = tostring(cfg.skills.combat_order or "")
    for line in (text .. "\n"):gmatch("([^\n]*)\n") do
        line = line:gsub("\r$", "")
        line = skill_trim(line)
        if line ~= "" then
            local id, name = line:match("^([^|]+)|(.+)$")
            id = skill_trim(id)
            name = skill_trim(name)
            local key = line
            local group = ""
            if name ~= "" then
                key = ((id ~= "" and id ~= "0") and (id .. "|" .. name)) or name
                group = skill_group_key_from_name(name)
            elseif id ~= "" then
                key = id
            end
            if key ~= "" and not seen[key] then
                entries[#entries + 1] = {
                    key = key,
                    id = id,
                    name = name ~= "" and name or line,
                    group = group,
                    line = key,
                }
                seen[key] = true
            end
        end
    end
    return entries
end

function skill_save_order_entries(entries)
    local lines = {}
    for _, entry in ipairs(entries or {}) do
        local line = skill_trim(entry.line or entry.key or "")
        if line ~= "" then
            lines[#lines + 1] = line
        end
    end
    cfg.skills.combat_order = table.concat(lines, "\n")
end

function skill_order_lookup(entries)
    local lookup = {
        by_key = {},
        by_group = {},
    }
    for _, entry in ipairs(entries or {}) do
        if entry.key and entry.key ~= "" then
            lookup.by_key[entry.key] = true
        end
        if entry.group and entry.group ~= "" then
            lookup.by_group[entry.group] = true
        end
    end
    return lookup
end

function skill_order_resolve_label(entry, learned_entries, maps)
    for _, learned in ipairs(learned_entries or {}) do
        if learned.key == entry.key or (entry.group ~= "" and learned.group == entry.group) then
            return learned.label
        end
    end

    local name = entry.name or entry.key or ""
    if entry.id and entry.id ~= "" and entry.id ~= "0" and name ~= entry.id then
        return tostring(name) .. "  id=" .. tostring(entry.id) .. "  [未学习]"
    end
    return tostring(name) .. "  [未学习]"
end

function skill_shuttle_lists(skills)
    local maps = skill_translation_maps()
    local learned = skill_highest_learned(skills or {}, maps)
    local order = skill_order_entries()
    local selected = skill_order_lookup(order)
    local available = {}
    local available_labels = {}
    local order_labels = {}

    for _, item in ipairs(learned) do
        if not selected.by_key[item.key] and not selected.by_group[item.group] then
            available[#available + 1] = item
            available_labels[#available_labels + 1] = item.label
        end
    end

    for _, entry in ipairs(order) do
        order_labels[#order_labels + 1] = skill_order_resolve_label(entry, learned, maps)
    end

    if #available_labels == 0 then
        available_labels[1] = "无可加入技能"
    end
    if #order_labels == 0 then
        order_labels[1] = "技能顺序为空"
    end

    return available, available_labels, order, order_labels
end

function skill_add_selected_to_order(available)
    if #available == 0 then
        return
    end
    runtime.skill_order.left_index = math.max(1, math.min(#available, runtime.skill_order.left_index or 1))
    local item = available[runtime.skill_order.left_index]
    if not item then
        return
    end

    local entries = skill_order_entries()
    entries[#entries + 1] = {
        key = item.key,
        id = (skill_extract_identity(item.skill)),
        name = select(2, skill_extract_identity(item.skill)),
        group = item.group,
        line = item.key,
    }
    skill_save_order_entries(entries)
    runtime.skill_order.right_index = #entries
    set_event("已加入技能顺序" .. tostring(item.label))
end

function skill_remove_selected_from_order(order)
    if #order == 0 then
        return
    end
    runtime.skill_order.right_index = math.max(1, math.min(#order, runtime.skill_order.right_index or 1))
    local index = runtime.skill_order.right_index
    local removed = table.remove(order, index)
    skill_save_order_entries(order)
    runtime.skill_order.right_index = math.max(1, math.min(#order, index))
    set_event("已移出技能顺序" .. tostring(removed and removed.name or ""))
end

function skill_move_order_item(order, delta)
    if #order < 2 then
        return
    end
    runtime.skill_order.right_index = math.max(1, math.min(#order, runtime.skill_order.right_index or 1))
    local index = runtime.skill_order.right_index
    local target_index = index + delta
    if target_index < 1 or target_index > #order then
        return
    end
    order[index], order[target_index] = order[target_index], order[index]
    skill_save_order_entries(order)
    runtime.skill_order.right_index = target_index
end

function draw_skill_shuttle(skills)
    local available, available_labels, order, order_labels = skill_shuttle_lists(skills)
    runtime.skill_order.left_index = math.max(1, math.min(#available_labels, runtime.skill_order.left_index or 1))
    runtime.skill_order.right_index = math.max(1, math.min(#order_labels, runtime.skill_order.right_index or 1))

    local table_flags = imgui.TableFlags_Borders + imgui.TableFlags_RowBg + imgui.TableFlags_Resizable
    if imgui.begin_table("##skill_order_shuttle", 3, table_flags) then
        imgui.table_setup_column("已学习技能", imgui.TableColumnFlags_WidthFixed, 330)
        imgui.table_setup_column("移动", imgui.TableColumnFlags_WidthFixed, 110)
        imgui.table_setup_column("技能顺序", imgui.TableColumnFlags_WidthStretch)
        imgui.table_headers_row()

        imgui.table_next_row()
        imgui.table_next_column()
        imgui.set_next_item_width(320)
        local changed, val = imgui.list_box("##skill_available_box", runtime.skill_order.left_index, available_labels, 12)
        if changed then
            runtime.skill_order.left_index = val
        end

        imgui.table_next_column()
        if imgui.button("加入顺序", 96, 26) then
            skill_add_selected_to_order(available)
        end
        if imgui.button("移回左侧", 96, 26) then
            skill_remove_selected_from_order(order)
        end
        imgui.spacing()
        if imgui.button("上移", 96, 26) then
            skill_move_order_item(order, -1)
        end
        if imgui.button("下移", 96, 26) then
            skill_move_order_item(order, 1)
        end
        imgui.spacing()
        if imgui.button("清空顺序", 96, 26) then
            cfg.skills.combat_order = ""
            runtime.skill_order.right_index = 1
            set_event("技能顺序已清空")
        end

        imgui.table_next_column()
        imgui.set_next_item_width(360)
        changed, val = imgui.list_box("##skill_order_box", runtime.skill_order.right_index, order_labels, 12)
        if changed then
            runtime.skill_order.right_index = val
        end

        imgui.end_table()
    end
end

function draw_skill_tab()
    local changed, val
    local b = runtime.bootstrap

    changed, val = imgui.checkbox("启用技能配置", cfg.skills.enabled)
    if changed then cfg.skills.enabled = val end

    imgui.same_line()
    changed, val = imgui.checkbox("从 API 自动同步", cfg.skills.auto_sync_from_api)
    if changed then cfg.skills.auto_sync_from_api = val end

    imgui.same_line()
    changed, val = imgui.checkbox("使用游戏内部自动技能", cfg.skills.prefer_auto_battle_list)
    if changed then cfg.skills.prefer_auto_battle_list = val end

    changed, val = imgui.checkbox("显示中文技能名", cfg.skills.translate_names)
    if changed then cfg.skills.translate_names = val end

    if imgui.button("刷新技能", 100, 26) then
        bootstrap_update_combat()
        set_event(string.format("技能已刷新: learned=%d active=%d buff=%d",
            tonumber(b.skill_count) or 0,
            tonumber(b.auto_active_count) or 0,
            tonumber(b.auto_buff_count) or 0))
    end

    imgui.same_line()
    if imgui.button("重建技能类型表", 130, 26) then
        if ok_combat and combat and type(combat.rebuildSkillTypeMap) == "function" then
            local ok, err = combat.rebuildSkillTypeMap()
            set_event(ok and "skill type map rebuilt" or ("skill type map rebuild failed: " .. tostring(err)))
            bootstrap_update_combat()
        else
            set_event("技能类型表重建失败: aion.combat 不可用")
        end
    end

    imgui.same_line()
    if imgui.button("生成翻译模板", 120, 26) then
        local added = skill_append_translation_template(b.skills or {})
        set_event("已追加待翻译技能: " .. tostring(added))
    end

    imgui.text(string.format("当前技能: 已学 %d | Buff %d | 自动主动 %d | 自动增益 %d",
        tonumber(b.skill_count) or 0,
        tonumber(b.buff_count) or 0,
        tonumber(b.auto_active_count) or 0,
        tonumber(b.auto_buff_count) or 0))

    imgui.spacing()
    imgui.text("技能执行配置")
    imgui.separator()

    local use_game_auto_skills = cfg.skills.prefer_auto_battle_list == true
    if use_game_auto_skills then
        imgui.text("当前使用游戏内部自动技能；脚本技能顺序、Buff/维护和忽略技能设置不会参与释放。")
    end

    local skills = b.skills or {}
    if use_game_auto_skills then imgui.begin_disabled(true) end
    if #skills == 0 then
        imgui.text("已学习技能为空，先点刷新技能或运行初始化/API 探针。")
    else
        draw_skill_shuttle(skills)
    end
    if use_game_auto_skills then imgui.end_disabled() end

    imgui.spacing()

    imgui.text("技能名翻译表")
    imgui.set_next_item_width(520)
    changed, val = imgui.input_text_multiline("##skill_translation_map", cfg.skills.translation_map, 520, 90)
    if changed then cfg.skills.translation_map = val end

    if use_game_auto_skills then imgui.begin_disabled(true) end
    imgui.text("增益/维护技能")
    imgui.set_next_item_width(520)
    changed, val = imgui.input_text_multiline("##skill_buff_order", cfg.skills.buff_order, 520, 70)
    if changed then cfg.skills.buff_order = val end

    imgui.text("忽略技能名")
    imgui.set_next_item_width(520)
    changed, val = imgui.input_text_multiline("##skill_ignore_names", cfg.skills.ignore_names, 520, 60)
    if changed then cfg.skills.ignore_names = val end

    imgui.text("备注")
    imgui.set_next_item_width(520)
    changed, val = imgui.input_text_multiline("##skill_notes", cfg.skills.notes, 520, 50)
    if changed then cfg.skills.notes = val end
    if use_game_auto_skills then imgui.end_disabled() end

    imgui.spacing()
    imgui.text("API 当前列表")
    imgui.separator()

    imgui.text("自动主动技能ID")
    imgui.set_next_item_width(520)
    changed, val = imgui.input_text_multiline("##skill_auto_active_ids", skill_auto_id_text(b.auto_active_skills), 520, 45)

    imgui.text("自动增益技能ID")
    imgui.set_next_item_width(520)
    changed, val = imgui.input_text_multiline("##skill_auto_buff_ids", skill_auto_id_text(b.auto_buff_skills), 520, 45)

end

local function draw_route_editor(label, nameField, pointsField)
    local changed, val

    imgui.text(label)
    imgui.set_next_item_width(240)
    changed, val = imgui.input_text("路径名##" .. pointsField, cfg.route[nameField])
    if changed then cfg.route[nameField] = val end

    imgui.same_line()
    imgui.set_next_item_width(260)
    changed, val = imgui.combo("已保存路径##saved_" .. pointsField, route_saved_index(pointsField, nameField), route_saved_labels(pointsField))
    if changed then
        route_load_saved(pointsField, nameField, val)
    end

    if imgui.button("保存到列表##" .. pointsField, 100, 26) then
        route_save_current(pointsField, nameField, false)
    end

    imgui.same_line()
    if imgui.button("删除保存##" .. pointsField, 90, 26) then
        route_delete_saved(pointsField, nameField, route_saved_index(pointsField, nameField))
    end

    imgui.text(route_stats_text(pointsField))

    if imgui.button("开始录制##" .. pointsField, 100, 26) then
        route_start_record(pointsField, nameField)
    end

    imgui.same_line()
    if imgui.button("停止录制##" .. pointsField, 100, 26) then
        route_stop_record()
    end

    imgui.same_line()
    if imgui.button("清空##" .. pointsField, 70, 26) then
        clear_route(pointsField)
    end

    imgui.same_line()
    if imgui.button("复制路径##" .. pointsField, 90, 26) then
        route_copy_current(pointsField, nameField)
    end

    imgui.set_next_item_width(560)
    changed, val = imgui.input_text_multiline("##points_" .. pointsField, cfg.route[pointsField], 560, 105)
    if changed then cfg.route[pointsField] = val end
end

local function draw_route_tab()
    local changed, val

    if route_sync_all_names_from_saved_points() then
        route_persist_config()
    end

    if runtime.recovery and runtime.recovery.active then
        imgui.text("恢复状态 " .. tostring(runtime.recovery.phase) .. " | " .. tostring(runtime.recovery.last_status))
    end

    local recording_text = runtime.route.recording and runtime.route.record_name or "否"
    local follow_text = runtime.route.following and string.format("%s %d/%d",
        runtime.route.follow_name,
        runtime.route.index,
        #(runtime.route.points or {})) or "否"
    if runtime.route.error ~= "" then
        imgui.text("路径提示: " .. tostring(runtime.route.error))
    end

    imgui.text("挂机路径选择:")
    for _, spec in ipairs(route_specs) do
        if not spec.hidden then
            local name = route_trim(cfg.route[spec.name_field] or "")
            local count = route_point_count(cfg.route[spec.points_field] or "")
            imgui.text(string.format("  %s: %s (%d点)", spec.label, name ~= "" and name or "未选", count))
        end
    end

    imgui.spacing()

    local selected_spec = route_specs[cfg.route.selected_route]
    if not selected_spec or selected_spec.hidden then
        for index, spec in ipairs(route_specs) do
            if not spec.hidden then
                cfg.route.selected_route = index
                selected_spec = spec
                break
            end
        end
    end

    if imgui.begin_tab_bar("##route_tabs_ordered_v2") then
        for index, spec in ipairs(route_specs) do
            if not spec.hidden then
                if imgui.begin_tab_item(spec.label .. "##" .. spec.points_field) then
                    cfg.route.selected_route = index
                    draw_route_editor(spec.description, spec.name_field, spec.points_field)
                    imgui.end_tab_item()
                end
            end
        end

        imgui.end_tab_bar()
    end

    imgui.spacing()
    if imgui.collapsing_header("高级路径设置") then
        changed, val = imgui.checkbox("循环路径", cfg.route.loop)
        if changed then cfg.route.loop = val end

        imgui.same_line()
        changed, val = imgui.checkbox("到终点反向", cfg.route.reverse_on_end)
        if changed then cfg.route.reverse_on_end = val end

        imgui.same_line()
        changed, val = imgui.checkbox("死亡停止路径", cfg.route.stop_on_death)
        if changed then cfg.route.stop_on_death = val end
    end
end

local function draw_leveling_tab()
    local changed, val

    cfg.leveling.mode = math.max(1, math.min(#leveling_modes, tonumber(cfg.leveling.mode) or 1))
    if imgui.collapsing_header("主线高级设置 / NPC 对话测试") then
    changed, val = imgui.checkbox("启用主线目标", cfg.leveling.enabled)
    if changed then cfg.leveling.enabled = val end

    imgui.set_next_item_width(220)
    changed, val = imgui.combo("主线方式", cfg.leveling.mode, leveling_modes)
    if changed then cfg.leveling.mode = val end

    imgui.set_next_item_width(120)
    changed, val = imgui.input_int("起始等级", cfg.leveling.start_level, 0)
    if changed then cfg.leveling.start_level = math.max(1, val) end

    imgui.same_line()
    imgui.set_next_item_width(120)
    changed, val = imgui.input_int("主线目标等级", cfg.leveling.target_level, 0)
    if changed then cfg.leveling.target_level = math.max(cfg.leveling.start_level, val) end

    changed, val = imgui.checkbox("只做主线任务", cfg.leveling.prefer_quest)
    if changed then cfg.leveling.prefer_quest = val end

    changed, val = imgui.checkbox("允许主线必要打怪", cfg.leveling.allow_grind)
    if changed then
        cfg.leveling.allow_grind = val
        sync_combat_enabled_from_primary_mode()
    end

    changed, val = imgui.checkbox("允许主线必要采集", cfg.leveling.allow_gather)
    if changed then cfg.leveling.allow_gather = val end

    changed, val = imgui.checkbox("自动评估装备升级", cfg.leveling.equip_upgrades)
    if changed then cfg.leveling.equip_upgrades = val end

    cfg.npc_dialog = cfg.npc_dialog or {}
    runtime.npc_dialog.candidate_labels = runtime.npc_dialog.candidate_labels or { "No nearby NPC" }
    runtime.npc_dialog.candidates = runtime.npc_dialog.candidates or {}
    runtime.npc_dialog.dialog_child_labels = runtime.npc_dialog.dialog_child_labels or { "No dialog child" }
    runtime.npc_dialog.dialog_children = runtime.npc_dialog.dialog_children or {}
    imgui.spacing()
    imgui.text("NPC 对话测试")
    imgui.separator()

    changed, val = imgui.input_int("扫描半径", tonumber(cfg.npc_dialog.scan_radius) or 45)
    if changed then cfg.npc_dialog.scan_radius = math.max(1, tonumber(val) or 45) end

    changed, val = imgui.input_int("扫描数量", tonumber(cfg.npc_dialog.scan_limit) or 12)
    if changed then cfg.npc_dialog.scan_limit = math.max(1, tonumber(val) or 12) end

    if imgui.button("刷新附近NPC(F1)", 140, 28) then
        npc_scan_nearby()
    end

    imgui.same_line()
    if imgui.button("填入接任务NPC", 130, 28) then
        npc_fill_selected_candidate()
    end

    imgui.set_next_item_width(520)
    changed, val = imgui.combo("附近NPC", tonumber(runtime.npc_dialog.selected_index) or 1, runtime.npc_dialog.candidate_labels)
    if changed then runtime.npc_dialog.selected_index = val end

    imgui.set_next_item_width(260)
    changed, val = imgui.input_text("接任务NPC", cfg.npc_dialog.accept_npc_name or "")
    if changed then cfg.npc_dialog.accept_npc_name = val end

    imgui.set_next_item_width(260)
    changed, val = imgui.input_text("接任务NPC ID", tostring(cfg.npc_dialog.accept_npc_interact_id or ""))
    if changed then cfg.npc_dialog.accept_npc_interact_id = val end

    changed, val = imgui.input_int("接任务quest_id(0=使用对话)", tonumber(cfg.npc_dialog.accept_quest_id) or 0)
    if changed then cfg.npc_dialog.accept_quest_id = math.max(0, tonumber(val) or 0) end

    changed, val = imgui.input_int("接任务next_dialog_id", tonumber(cfg.npc_dialog.accept_next_dialog_id) or 0)
    if changed then cfg.npc_dialog.accept_next_dialog_id = tonumber(val) or 0 end

    changed, val = imgui.input_int("等待对话毫秒", tonumber(cfg.npc_dialog.wait_dialog_ms) or 3000)
    if changed then cfg.npc_dialog.wait_dialog_ms = math.max(500, tonumber(val) or 3000) end

    changed, val = imgui.input_int("对话子控件深度", tonumber(cfg.npc_dialog.dialog_child_depth) or 6)
    if changed then cfg.npc_dialog.dialog_child_depth = math.max(1, tonumber(val) or 6) end

    changed, val = imgui.input_int("连续点击X", tonumber(cfg.npc_dialog.auto_click_x) or 25)
    if changed then cfg.npc_dialog.auto_click_x = tonumber(val) or 25 end

    changed, val = imgui.input_int("X容差", tonumber(cfg.npc_dialog.auto_click_x_tolerance) or 2)
    if changed then cfg.npc_dialog.auto_click_x_tolerance = math.max(0, tonumber(val) or 2) end

    changed, val = imgui.input_int("连续点击次数", tonumber(cfg.npc_dialog.auto_click_steps) or 8)
    if changed then cfg.npc_dialog.auto_click_steps = math.max(1, tonumber(val) or 8) end

    changed, val = imgui.input_int("连续点击间隔ms", tonumber(cfg.npc_dialog.auto_click_delay_ms) or 450)
    if changed then cfg.npc_dialog.auto_click_delay_ms = math.max(50, tonumber(val) or 450) end

    if imgui.button("读取当前对话", 140, 28) then
        npc_dump_current_dialog()
    end

    imgui.same_line()
    if imgui.button("测试接任务", 140, 28) then
        npc_accept_quest_test()
    end

    imgui.same_line()
    if imgui.button("发送当前对话", 140, 28) then
        npc_accept_current_dialog()
    end

    imgui.same_line()
    if imgui.button("用ID打开对话", 140, 28) then
        npc_open_dialog_by_interact_id()
    end

    imgui.set_next_item_width(720)
    changed, val = imgui.combo("对话子控件", tonumber(runtime.npc_dialog.selected_child_index) or 1, runtime.npc_dialog.dialog_child_labels)
    if changed then runtime.npc_dialog.selected_child_index = val end

    if imgui.button("点击选中控件", 140, 28) then
        npc_click_selected_dialog_child()
    end

    imgui.same_line()
    if imgui.button("点击后接任务", 140, 28) then
        npc_click_selected_child_then_accept()
    end

    imgui.same_line()
    if imgui.button("连续点击X控件", 140, 28) then
        npc_continuous_click_dialog_x()
    end

    end
end

local function draw_crafting_tab()
    local changed, val

    changed, val = imgui.checkbox("启用制作目标", cfg.crafting.enabled)
    if changed then cfg.crafting.enabled = val end

    imgui.set_next_item_width(220)
    changed, val = imgui.combo("制作专业", cfg.crafting.profession, professions)
    if changed then cfg.crafting.profession = val end

    imgui.set_next_item_width(260)
    changed, val = imgui.input_text("制作物品", cfg.crafting.item_name)
    if changed then cfg.crafting.item_name = val end

    changed, val = imgui.input_int("制作数量", cfg.crafting.craft_count)
    if changed then cfg.crafting.craft_count = math.max(1, val) end

    changed, val = imgui.input_int("保留金币", cfg.crafting.reserve_kinah)
    if changed then cfg.crafting.reserve_kinah = math.max(0, val) end

    changed, val = imgui.checkbox("缺材料时停止", cfg.crafting.stop_when_missing_material)
    if changed then cfg.crafting.stop_when_missing_material = val end

    imgui.text("材料规则")
    imgui.set_next_item_width(480)
    changed, val = imgui.input_text_multiline("##material_rules", cfg.crafting.material_rules, 480, 140)
    if changed then cfg.crafting.material_rules = val end
end

function draw_primary_mode_linked_panel()
    normalize_primary_mode()

    local mode = primary_mode_ids[cfg.primary_mode] or ""
    if mode == "leveling" then
        draw_leveling_tab()
    elseif mode == "combat" then
        draw_combat_tab()
    elseif mode == "采集" then
        draw_gather_tab()
    elseif mode == "制作" then
        draw_crafting_tab()
    else
        imgui.text("未知主模式" .. tostring(mode))
    end
end

function normalize_maintenance_rules(rules, default_percent)
    local normalized = {}
    if type(rules) ~= "table" then
        return normalized
    end

    for _, rule in ipairs(rules) do
        if type(rule) == "table" then
            local percent = tonumber(rule.percent or rule.threshold or default_percent) or default_percent
            local keycode = tonumber(rule.keycode or rule.key_code or rule.key or 0) or 0
            normalized[#normalized + 1] = {
                percent = math.max(1, math.min(100, math.floor(percent))),
                keycode = math.max(0, math.floor(keycode)),
            }
        end
    end
    return normalized
end

function normalize_floor_recovery_config()
    cfg.supply = cfg.supply or {}
    local settings = nil
    local ok_module, module = pcall(require, "aion.floor_recovery")
    if ok_module and module and type(module.from_config) == "function" then
        settings = module.from_config(cfg.supply)
    else
        local current = type(cfg.supply.floor_recovery) == "table" and cfg.supply.floor_recovery or {}
        local start_percent = math.max(1, math.min(100, math.floor(tonumber(current.start_percent) or 15)))
        local recover_percent = math.max(1, math.min(100, math.floor(tonumber(current.recover_percent) or 90)))
        if recover_percent <= start_percent then
            recover_percent = math.min(100, start_percent + 1)
        end
        settings = {
            enabled = current.enabled == true,
            start_percent = start_percent,
            recover_percent = recover_percent,
            sit_keycode = 188,
            stand_keycode = math.max(1, math.min(255, math.floor(tonumber(current.stand_keycode) or 88))),
            cancel_on_damage = current.cancel_on_damage ~= false,
        }
    end
    cfg.supply.floor_recovery = {
        enabled = settings.enabled == true,
        start_percent = settings.start_percent,
        recover_percent = settings.recover_percent,
        sit_keycode = settings.sit_keycode,
        stand_keycode = settings.stand_keycode,
        cancel_on_damage = settings.cancel_on_damage ~= false,
    }
    return cfg.supply.floor_recovery
end

function normalize_supply_config()
    cfg.supply = cfg.supply or {}
    cfg.supply.hp_percent = math.max(1, math.min(100, tonumber(cfg.supply.hp_percent) or 35))
    cfg.supply.mp_percent = math.max(1, math.min(100, tonumber(cfg.supply.mp_percent) or 25))
    cfg.supply.bag_full_percent = math.max(1, math.min(100, tonumber(cfg.supply.bag_full_percent) or 85))
    cfg.supply.bag_slots = math.max(1, tonumber(cfg.supply.bag_slots) or 100)
    cfg.supply.min_kinah = math.max(0, tonumber(cfg.supply.min_kinah) or 0)
    cfg.supply.buy_hp_potion = math.max(0, tonumber(cfg.supply.buy_hp_potion) or 0)
    cfg.supply.buy_mp_potion = math.max(0, tonumber(cfg.supply.buy_mp_potion) or 0)
    cfg.supply.vendor_name = tostring(cfg.supply.vendor_name or "")
    cfg.supply.keep_items = tostring(cfg.supply.keep_items or "")
    cfg.supply.sell_rules = tostring(cfg.supply.sell_rules or "")
    cfg.supply.hp_rules = normalize_maintenance_rules(cfg.supply.hp_rules, cfg.supply.hp_percent)
    cfg.supply.mp_rules = normalize_maintenance_rules(cfg.supply.mp_rules, cfg.supply.mp_percent)
    normalize_floor_recovery_config()
end

function add_maintenance_rule(kind)
    normalize_supply_config()
    local is_mp = kind == "mp"
    local rules = is_mp and cfg.supply.mp_rules or cfg.supply.hp_rules
    local percent = is_mp and cfg.supply.mp_percent or cfg.supply.hp_percent
    rules[#rules + 1] = {
        percent = math.max(1, math.min(100, tonumber(percent) or 50)),
        keycode = 0,
    }
    set_event(is_mp and "已新增蓝量维护" or "已新增血量维护")
end

function clear_maintenance_keycode_target()
    runtime.maintenance = runtime.maintenance or {}
    runtime.maintenance.keycode_target_kind = ""
    runtime.maintenance.keycode_target_index = 0
end

function open_maintenance_keycode_picker(kind, index)
    runtime.maintenance = runtime.maintenance or {}
    runtime.maintenance.keycode_window_visible = true
    runtime.maintenance.keycode_target_kind = tostring(kind or "")
    runtime.maintenance.keycode_target_index = tonumber(index) or 0
end

function maintenance_key_label(keycode)
    local code = math.floor(tonumber(keycode) or 0)
    if code <= 0 then
        return "选择按键"
    end

    local name = ""
    if type(keycode_name_for_code) == "function" then
        name = keycode_name_for_code(code)
    end
    if name ~= "" then
        return name .. "=" .. tostring(code)
    end
    return "键码=" .. tostring(code)
end

function draw_maintenance_rule_row(kind, rules, index)
    local rule = rules[index]
    local label = kind == "mp" and "蓝量低于" or "血量低于"
    local changed, val

    imgui.text(label)
    imgui.same_line()
    imgui.set_next_item_width(90)
    changed, val = imgui.input_text("##maintenance_" .. kind .. "_percent_" .. tostring(index), tostring(tonumber(rule.percent) or 1))
    if changed and tonumber(val) then
        rule.percent = math.max(1, math.min(100, math.floor(tonumber(val) or 1)))
    end

    imgui.same_line()
    imgui.text("%  按")
    imgui.same_line()
    if imgui.button(maintenance_key_label(rule.keycode) .. "##maintenance_" .. kind .. "_pick_key_" .. tostring(index), 120, 26) then
        open_maintenance_keycode_picker(kind, index)
    end

    imgui.same_line()
    if imgui.small_button("删除##maintenance_" .. kind .. "_delete_" .. tostring(index)) then
        if runtime.maintenance
            and runtime.maintenance.keycode_target_kind == kind
            and tonumber(runtime.maintenance.keycode_target_index) >= index then
            clear_maintenance_keycode_target()
            runtime.maintenance.keycode_window_visible = false
        end
        table.remove(rules, index)
        set_event(kind == "mp" and "已删除蓝量维护" or "已删除血量维护")
        return true
    end

    return false
end

function draw_maintenance_rule_section(kind)
    local is_mp = kind == "mp"
    local rules = is_mp and cfg.supply.mp_rules or cfg.supply.hp_rules
    local title = is_mp and "蓝量维护" or "血量维护"
    local add_label = is_mp and "新增蓝量维护" or "新增血量维护"

    imgui.text(title)
    imgui.same_line()
    if imgui.button(add_label, 120, 26) then
        add_maintenance_rule(kind)
        rules = is_mp and cfg.supply.mp_rules or cfg.supply.hp_rules
    end

    if #rules == 0 then
        imgui.text("暂无" .. title)
        return
    end

    for index = 1, #rules do
        if draw_maintenance_rule_row(kind, rules, index) then
            break
        end
    end
end

function maintenance_percent_text_input(id, value, fallback)
    local current = math.floor(tonumber(value) or fallback or 1)
    imgui.set_next_item_width(72)
    local changed, text = imgui.input_text(id, tostring(current))
    if changed and tonumber(text) then
        return true, math.max(1, math.min(100, math.floor(tonumber(text) or current)))
    end
    return false, current
end

function draw_floor_recovery_section()
    normalize_supply_config()
    local rule = cfg.supply.floor_recovery
    local changed, val

    imgui.text("坐地板维护")
    imgui.same_line()
    changed, val = imgui.checkbox("启用##floor_recovery_enabled", rule.enabled == true)
    if changed then
        rule.enabled = val == true
    end

    imgui.text("蓝量低于")
    imgui.same_line()
    changed, val = maintenance_percent_text_input("##floor_recovery_start_percent", rule.start_percent, 15)
    if changed then
        rule.start_percent = val
        if (tonumber(rule.recover_percent) or 90) <= rule.start_percent then
            rule.recover_percent = math.min(100, rule.start_percent + 1)
        end
    end

    imgui.same_line()
    imgui.text("% 坐地板，恢复到")
    imgui.same_line()
    changed, val = maintenance_percent_text_input("##floor_recovery_recover_percent", rule.recover_percent, 90)
    if changed then
        rule.recover_percent = val
        if rule.recover_percent <= (tonumber(rule.start_percent) or 15) then
            rule.recover_percent = math.min(100, (tonumber(rule.start_percent) or 15) + 1)
        end
    end

    imgui.same_line()
    imgui.text("% 起来继续打怪")
end

KEYCODE_KEYBOARD_ROWS = {
    {
        { "Esc", 27 }, { gap = 18 },
        { "F1", 112 }, { "F2", 113 }, { "F3", 114 }, { "F4", 115 }, { gap = 12 },
        { "F5", 116 }, { "F6", 117 }, { "F7", 118 }, { "F8", 119 }, { gap = 12 },
        { "F9", 120 }, { "F10", 121 }, { "F11", 122 }, { "F12", 123 }, { gap = 18 },
        { "PrtSc", 44, 54 }, { "ScrLk", 145, 54 }, { "Pause", 19, 54 },
    },
    {
        { "`~", 192 }, { "1!", 49 }, { "2@", 50 }, { "3#", 51 }, { "4$", 52 }, { "5%", 53 },
        { "6^", 54 }, { "7&", 55 }, { "8*", 56 }, { "9(", 57 }, { "0)", 48 },
        { "-_", 189 }, { "=+", 187 }, { "Backspace", 8, 96 }, { gap = 18 },
        { "Ins", 45 }, { "Home", 36 }, { "PgUp", 33 }, { gap = 18 },
        { "Num", 144 }, { "N/", 111 }, { "N*", 106 }, { "N-", 109 },
    },
    {
        { "Tab", 9, 70 }, { "Q", 81 }, { "W", 87 }, { "E", 69 }, { "R", 82 }, { "T", 84 },
        { "Y", 89 }, { "U", 85 }, { "I", 73 }, { "O", 79 }, { "P", 80 },
        { "[{", 219 }, { "]}", 221 }, { "\\|", 220, 70 }, { gap = 18 },
        { "Del", 46 }, { "End", 35 }, { "PgDn", 34 }, { gap = 18 },
        { "N7", 103 }, { "N8", 104 }, { "N9", 105 }, { "N+", 107 },
    },
    {
        { "Caps", 20, 82 }, { "A", 65 }, { "S", 83 }, { "D", 68 }, { "F", 70 }, { "G", 71 },
        { "H", 72 }, { "J", 74 }, { "K", 75 }, { "L", 76 }, { ";:", 186 },
        { "'\"", 222 }, { "Enter", 13, 104 }, { gap = 184 },
        { "N4", 100 }, { "N5", 101 }, { "N6", 102 }, { "N+", 107 },
    },
    {
        { "LShift", 160, 100 }, { "Z", 90 }, { "X", 88 }, { "C", 67 }, { "V", 86 }, { "B", 66 },
        { "N", 78 }, { "M", 77 }, { ",<", 188 }, { ".>", 190 }, { "/?", 191 },
        { "RShift", 161, 124 }, { gap = 76 }, { "Up", 38 }, { gap = 72 },
        { "N1", 97 }, { "N2", 98 }, { "N3", 99 }, { "NEnter", 13, 66 },
    },
    {
        { "LCtrl", 162, 66 }, { "LWin", 91, 62 }, { "LAlt", 164, 62 }, { "Space", 32, 260 },
        { "RAlt", 165, 62 }, { "Menu", 93, 62 }, { "RWin", 92, 62 }, { "RCtrl", 163, 66 },
        { gap = 22 }, { "Left", 37 }, { "Down", 40 }, { "Right", 39 }, { gap = 18 },
        { "N0", 96, 92 }, { "N.", 110 }, { "NEnter", 13, 66 },
    },
}

function keycode_name_for_code(code)
    local target = tonumber(code) or -1
    if type(KEYCODE_KEYBOARD_ROWS) ~= "table" then
        return ""
    end

    for _, row in ipairs(KEYCODE_KEYBOARD_ROWS) do
        for _, item in ipairs(row) do
            if type(item) == "table" and tonumber(item[2]) == target then
                return tostring(item[1] or "")
            end
        end
    end
    return ""
end

function maintenance_keycode_target_text()
    runtime.maintenance = runtime.maintenance or {}
    local kind = tostring(runtime.maintenance.keycode_target_kind or "")
    local index = tonumber(runtime.maintenance.keycode_target_index) or 0
    if kind == "hp" and index > 0 then
        return "当前填入：血量维护第 " .. tostring(index) .. " 行"
    end
    if kind == "mp" and index > 0 then
        return "当前填入：蓝量维护第 " .. tostring(index) .. " 行"
    end
    return "未选择维护行：请先点维护行里的选择按键"
end

function apply_selected_maintenance_keycode(name, code)
    runtime.maintenance = runtime.maintenance or {}
    local kind = tostring(runtime.maintenance.keycode_target_kind or "")
    local index = tonumber(runtime.maintenance.keycode_target_index) or 0
    local rules = nil
    local title = ""

    normalize_supply_config()
    if kind == "hp" then
        rules = cfg.supply.hp_rules
        title = "血量维护"
    elseif kind == "mp" then
        rules = cfg.supply.mp_rules
        title = "蓝量维护"
    end

    if type(rules) ~= "table" or index <= 0 or type(rules[index]) ~= "table" then
        set_event("请先选择维护行")
        return
    end

    local keycode = math.max(0, math.floor(tonumber(code) or 0))
    rules[index].keycode = keycode
    set_event(title .. "第" .. tostring(index) .. "行按键: " .. tostring(name or "") .. "=" .. tostring(keycode))
    clear_maintenance_keycode_target()
    runtime.maintenance.keycode_window_visible = false
end

function draw_keycode_key(item)
    local name = tostring(item[1] or "")
    local code = tostring(item[2] or "")
    local width = tonumber(item[3]) or 44
    local label = name .. "\n" .. code .. "##keycode_key_" .. name .. "_" .. code
    if imgui.button(label, width, 42) then
        apply_selected_maintenance_keycode(name, code)
    end
end

function draw_keycode_keyboard()
    for _, row in ipairs(KEYCODE_KEYBOARD_ROWS) do
        local first = true
        local spacing = 4
        for _, item in ipairs(row) do
            if item.gap then
                spacing = tonumber(item.gap) or 12
            else
                if not first then
                    imgui.same_line(0, spacing)
                end
                draw_keycode_key(item)
                first = false
                spacing = 4
            end
        end
        imgui.spacing()
    end
end

function draw_keycode_reference_window()
    runtime.maintenance = runtime.maintenance or {}
    if not runtime.maintenance.keycode_window_visible then
        return
    end

    imgui.set_next_window_size(1040, 430, imgui.Cond_FirstUseEver)
    imgui.set_next_window_pos(220, 140, imgui.Cond_FirstUseEver)

    local visible, open = imgui.begin_window("键盘键码表###aion_keycode_reference_window", true, imgui.WindowFlags_NoCollapse)
    if open == false then
        runtime.maintenance.keycode_window_visible = false
        clear_maintenance_keycode_target()
    end

    if visible then
        imgui.text("键面文字 / Windows Virtual-Key 键码")
        imgui.text("通用修饰键: Shift=16  Ctrl=17  Alt=18；左右修饰键按键盘实际键位单独列出。")
        imgui.text(maintenance_keycode_target_text())
        imgui.separator()
        draw_keycode_keyboard()
    end

    imgui.end_window()
end

function draw_maintenance_keycode_reference()
    runtime.maintenance = runtime.maintenance or {}
    local label = runtime.maintenance.keycode_window_visible and "关闭键码表" or "打开键码表"
    if imgui.button(label, 120, 26) then
        if runtime.maintenance.keycode_window_visible then
            runtime.maintenance.keycode_window_visible = false
            clear_maintenance_keycode_target()
        else
            clear_maintenance_keycode_target()
            runtime.maintenance.keycode_window_visible = true
        end
    end
end

function draw_maintenance_tab()
    local changed, val

    normalize_supply_config()

    imgui.begin_group()
    draw_floor_recovery_section()
    imgui.spacing()
    draw_maintenance_rule_section("hp")
    imgui.spacing()
    draw_maintenance_rule_section("mp")
    imgui.end_group()

    imgui.spacing()
    imgui.separator()

    if imgui.collapsing_header("高级设置") then
    imgui.set_next_item_width(110)
    changed, val = imgui.input_int("清包阈值", cfg.supply.bag_full_percent)
    if changed then cfg.supply.bag_full_percent = math.max(1, math.min(100, val)) end

    imgui.set_next_item_width(110)
    changed, val = imgui.input_int("背包总格数", cfg.supply.bag_slots)
    if changed then cfg.supply.bag_slots = math.max(1, val) end
    end
end

function draw_supply_tab()
    draw_maintenance_tab()

    imgui.spacing()
    draw_safety_tab()
end

function draw_safety_tab()
    local changed, val

    imgui.text("安全")
    imgui.separator()

    changed, val = imgui.input_int("最大失败次数", cfg.safety.max_failures)
    if changed then cfg.safety.max_failures = math.max(1, val) end

    changed, val = imgui.input_int("卡住秒数", cfg.safety.max_stuck_seconds)
    if changed then cfg.safety.max_stuck_seconds = math.max(1, val) end

    changed, val = imgui.input_int("最大死亡次数", cfg.safety.max_deaths)
    if changed then cfg.safety.max_deaths = math.max(0, val) end

    changed, val = imgui.checkbox("未知地图停止", cfg.safety.stop_on_unknown_map)
    if changed then cfg.safety.stop_on_unknown_map = val end

    changed, val = imgui.checkbox("API 失败停止", cfg.safety.stop_on_api_fail)
    if changed then cfg.safety.stop_on_api_fail = val end

    changed, val = imgui.checkbox("启用 Circuit Breaker", cfg.safety.circuit_breaker)
    if changed then cfg.safety.circuit_breaker = val end
end

local function draw_config_tab()
    local changed, val

    imgui.text("路径配置:")
    imgui.separator()

    imgui.set_next_item_width(520)
    changed, val = imgui.input_text("路径导出文件", cfg.transfer.route_export_path)
    if changed then cfg.transfer.route_export_path = val end

    if imgui.button("导出路径配置", 120, 28) then
        export_route_config()
    end

    imgui.set_next_item_width(520)
    changed, val = imgui.input_text("路径导入文件", cfg.transfer.route_import_path)
    if changed then cfg.transfer.route_import_path = val end

    if imgui.button("导入路径配置", 120, 28) then
        import_route_config()
    end

    imgui.spacing()
    imgui.text("整体脚本配置:")
    imgui.separator()

    imgui.set_next_item_width(520)
    changed, val = imgui.input_text("整体导出文件", cfg.transfer.profile_export_path)
    if changed then cfg.transfer.profile_export_path = val end

    if imgui.button("导出整体配置", 120, 28) then
        export_profile_config()
    end

    imgui.set_next_item_width(520)
    changed, val = imgui.input_text("整体导入文件", cfg.transfer.profile_import_path)
    if changed then cfg.transfer.profile_import_path = val end

    if imgui.button("导入整体配置", 120, 28) then
        import_profile_config()
    end

    imgui.spacing()
    imgui.text("当前配置文件: script_config.json")
    imgui.text("整体配置不包含 config.json、key、DLL、账号或启动器参数。")
    if runtime.transfer.last_status ~= "" then
        imgui.text("最近导入导出: " .. tostring(runtime.transfer.last_status))
    end
end

local function draw_debug_tab()
    imgui.text("调试")
    imgui.separator()

    if imgui.button("运行 API 探针", 140, 28) then
        run_probe()
    end

    imgui.same_line()
    if imgui.button("Init status", 140, 28) then
        bootstrap_begin("调试页初始化")
    end

    imgui.same_line()
    if imgui.button("读取当前坐标", 140, 28) then
        local text, err = capture_position_text()
        set_event(text and ("当前坐标: " .. text) or ("坐标读取失败: " .. tostring(err)))
    end

    imgui.same_line()
    if imgui.button("打印配置摘要", 140, 28) then
        set_event(string.format("mode=%s priority=%s combat=%s gather=%s",
            primary_modes[cfg.primary_mode] or "?",
            priority_modes[cfg.priority_mode] or "?",
            tostring(cfg.combat.enabled),
            tostring(cfg.gather.enabled)))
    end

    imgui.spacing()
    imgui.text("最后事件: " .. tostring(runtime.last_event))
    imgui.text("最近探针: " .. tostring(runtime.last_probe))
    imgui.text("初始化" .. tostring(runtime.bootstrap.status) ..
        " step=" .. tostring(runtime.bootstrap.current_step) ..
        " skill=" .. tostring(runtime.bootstrap.skill_count) ..
        " buff=" .. tostring(runtime.bootstrap.buff_count) ..
        " inv=" .. tostring(runtime.bootstrap.inventory_count) ..
        " quest=" .. tostring(runtime.bootstrap.quest_count))
    imgui.text("甯? " .. tostring(runtime.frame))

    imgui.spacing()
    imgui.text("热键")
    imgui.separator()
    imgui.text("F1: 有NPC对话时连续点击X控件；否则扫描附近NPC名字")
    imgui.text("F2: 打印完整UI列表和复活候选子控件到日志")
    imgui.text("F7: 呼出/隐藏窗口")
    imgui.text("F8: 打印当前NPC对话信息")
    imgui.text("F9: API 探针")
    imgui.text("F10: 暂停/继续")
    imgui.text("Ctrl+F12: 退出 UI 脚本")
end

function draw_test_tab()
    local t = runtime.teleport_test
    local changed, val

    imgui.text("据点传送测试")
    imgui.separator()

    if imgui.button("遍历当前地图据点", 150, 28) then
        teleport_refresh_nodes()
    end

    imgui.same_line()
    if imgui.button("传送到选中据点", 150, 28) then
        teleport_to_selected_node()
    end

    imgui.spacing()
    imgui.text("当前地图: " .. tostring(t.map_name ~= "" and t.map_name or runtime.audit.current.map or ""))
    imgui.text("大地图ID: " .. tostring(t.big_map_id or 0) ..
        " | 据点数 " .. tostring(count_array(t.nodes)) ..
        " | 可传送 " .. tostring(t.can_teleport == true))

    imgui.set_next_item_width(620)
    changed, val = imgui.combo("据点", tonumber(t.selected_index) or 1, t.node_labels or { "No map nodes" })
    if changed then
        t.selected_index = val
        local node = teleport_selected_node()
        if node then
            cfg.test.selected_node_id = tonumber(node.node_id or node.id or 0) or 0
        end
    end

    local node = teleport_selected_node()
    if node then
        imgui.text("选中: " .. teleport_node_label(node, tonumber(t.selected_index) or 1))
    else
        imgui.text("选中: 无")
    end

    if t.last_status ~= "" then
        imgui.text("状态" .. tostring(t.last_status))
    end

    imgui.spacing()
    imgui.text("遍历结果")
    imgui.set_next_item_width(760)
    changed, val = imgui.input_text_multiline("##teleport_node_dump", t.node_dump or "", 760, 360)
    if changed then
        t.node_dump = val
    end

    imgui.spacing()
    imgui.separator()
    imgui.text("拾取测试")

    if imgui.button("拾取最近尸体", 150, 28) then
        loot_test_pick_nearest()
    end

    imgui.same_line()
    imgui.text("半径: " .. tostring(cfg.combat.loot_radius) ..
        " | 交互距离: " .. tostring(cfg.combat.loot_interact_range) ..
        " | 按键码 " .. tostring(cfg.combat.loot_keycode))

    if runtime.loot_test.last_status ~= "" then
        imgui.text("状态 " .. tostring(runtime.loot_test.last_status))
    end
    imgui.text("说明: 有尸体时点击一次会选最近可拾取目标；距离远会先移动，靠近后自动继续拾取。")
    if imgui.button("遍历最近尸体(F5)", 150, 28) then
        loot_test_dump_near_corpses()
    end
    imgui.same_line()
    if imgui.button("Dump selected target(F6)", 180, 28) then
        target_f6_dump_selected()
    end
    imgui.set_next_item_width(760)
    changed, val = imgui.input_text_multiline("##loot_test_dump", runtime.loot_test.last_dump or "", 760, 120)
    if changed then
        runtime.loot_test.last_dump = val
    end

    if false then
    imgui.spacing()
    imgui.separator()
    imgui.text("UI 控件测试")

    imgui.set_next_item_width(220)
    changed, val = imgui.input_text("父控件名", cfg.test.ui_parent_name or "")
    if changed then cfg.test.ui_parent_name = val end

    imgui.same_line()
    imgui.set_next_item_width(90)
    changed, val = imgui.input_int("子控件深度", tonumber(cfg.test.ui_child_depth) or 6)
    if changed then cfg.test.ui_child_depth = math.max(1, tonumber(val) or 6) end

    imgui.same_line()
    changed, val = imgui.checkbox("包含无名控件", cfg.test.ui_include_no_name == true)
    if changed then cfg.test.ui_include_no_name = val end

    if imgui.button("遍历全部UI", 120, 28) then
        ui_test_refresh_all()
    end
    imgui.same_line()
    if imgui.button("遍历父控件子树", 150, 28) then
        ui_test_refresh_children()
    end
    imgui.same_line()
    if imgui.button("点击选中控件", 140, 28) then
        ui_test_click_selected()
    end

    local ui_t = runtime.ui_test
    imgui.set_next_item_width(760)
    changed, val = imgui.combo("控件选择", tonumber(ui_t.selected_index) or 1, ui_t.labels or { "No UI controls" })
    if changed then
        ui_t.selected_index = val
    end

    if ui_t.last_status ~= "" then
        imgui.text("UI状态 " .. tostring(ui_t.last_status))
    end
    imgui.text("提示: 复活窗口可先填 parent=dlg_revive；NPC对话可试 parent=dlg_dialog、ClickButton 只保证按钮类控件可点击。")
    imgui.set_next_item_width(760)
    changed, val = imgui.input_text_multiline("##ui_control_dump", ui_t.dump or "", 760, 260)
    if changed then
        ui_t.dump = val
    end
    end
end

function account_save_feedback_state()
    local state = runtime.accounts
    local until_at = tonumber(state.save_feedback_until) or 0
    if until_at <= now_seconds() then
        return "保存账号配置", false, true, ""
    end

    local dots = string.rep(".", (math.floor(now_seconds() * 6) % 3) + 1)
    local ok = state.save_feedback_ok ~= false
    local label = (ok and "已保存" or "保存失败") .. dots
    return label, true, ok, tostring(state.save_feedback_text or "")
end

function account_mark_save_feedback(ok, text)
    runtime.accounts.save_feedback_until = now_seconds() + 1.4
    runtime.accounts.save_feedback_ok = ok ~= false
    runtime.accounts.save_feedback_text = tostring(text or "")
end

function draw_account_save_feedback(active, ok, text)
    if not active or text == "" then
        return
    end

    imgui.same_line()
    if imgui.text_colored then
        if ok then
            imgui.text_colored(0.12, 0.48, 0.20, 1.0, text)
        else
            imgui.text_colored(0.90, 0.18, 0.12, 1.0, text)
        end
    else
        imgui.text(text)
    end
end

local function draw_account_settings_window()
    if not runtime.accounts.settings_window_visible then
        return
    end

    local account, account_index = selected_account()
    local title_name = account and account_display_name(account) or "未选择账号"

    imgui.set_next_window_size(860, 760, imgui.Cond_FirstUseEver)
    imgui.set_next_window_pos(180, 120, imgui.Cond_FirstUseEver)

    local visible, open = imgui.begin_window(
        "账号设置 - " .. tostring(title_name) .. "###aion_account_settings_window",
        true,
        imgui.WindowFlags_NoCollapse)
    if open == false then
        runtime.accounts.settings_window_visible = false
    end

    if visible then
        local save_label, save_active, save_ok_state, save_feedback_text = account_save_feedback_state()
        if imgui.button(save_label .. "##account_save_config", 120, 26) then
            local save_ok, save_err = account_can_save_settings(account)
            if save_ok then
                save_config()
                account_mark_save_feedback(true, "配置已保存")
                save_active = true
                save_ok_state = true
                save_feedback_text = "配置已保存"
                set_event("账号和脚本配置已保存")
            else
                runtime.accounts.last_status = tostring(save_err)
                account_mark_save_feedback(false, tostring(save_err))
                save_active = true
                save_ok_state = false
                save_feedback_text = tostring(save_err)
                set_event(tostring(save_err))
            end
        end
        draw_account_save_feedback(save_active, save_ok_state, save_feedback_text)
        if account then
            imgui.same_line()
            if imgui.button("启动脚本", 90, 26) then
                local save_ok, save_err = account_can_save_settings(account)
                if save_ok then
                    save_config()
                    if (tonumber(account.target and account.target.pid) or 0) > 0 then
                        account_apply_to_target(account)
                    end
                    account_queue_local_script("start", account, account_index)
                else
                    runtime.accounts.last_status = tostring(save_err)
                    set_event(tostring(save_err))
                end
            end
            imgui.same_line()
            if imgui.button("停止脚本", 90, 26) then
                save_config()
                account_queue_local_script("stop", account, account_index)
            end
        end

        imgui.separator()

        if imgui.begin_tab_bar("##account_settings_tabs_overview_route_account") then
            if imgui.begin_tab_item("总览") then
                draw_overview_tab()
                imgui.end_tab_item()
            end

            if imgui.begin_tab_item("路径") then
                draw_route_tab()
                imgui.end_tab_item()
            end

            if imgui.begin_tab_item("维护") then
                draw_maintenance_tab()
                imgui.end_tab_item()
            end

            if imgui.begin_tab_item("账号") then
                draw_account_settings()
                imgui.end_tab_item()
            end

            if imgui.begin_tab_item("测试") then
                draw_test_tab()
                imgui.end_tab_item()
            end

            imgui.end_tab_bar()
        end
    end

    imgui.end_window()
end

function format_duration(seconds)
    seconds = math.max(0, math.floor(seconds or 0))
    local h = math.floor(seconds / 3600)
    local m = math.floor((seconds % 3600) / 60)
    local s = seconds % 60
    return string.format("%02d:%02d:%02d", h, m, s)
end

local function draw_audit_panel()
    local a = runtime.audit
    local changed, val

    imgui.separator()
    imgui.text("审计")
    help_marker("当前为估算审计：击杀=新出现可拾取尸体；采集材料=资源类物品入包增量；经验和金币按角色数据差值计算。")

    imgui.same_line(90)
    changed, val = imgui.checkbox("启用##audit", cfg.audit.enabled)
    if changed then cfg.audit.enabled = val end

    imgui.same_line(170)
    changed, val = imgui.checkbox("启动时重置##audit", cfg.audit.reset_on_start)
    if changed then cfg.audit.reset_on_start = val end

    imgui.same_line(310)
    if imgui.button("重置审计", 90, 24) then
        audit_reset()
        set_event("审计已重置")
    end

    imgui.same_line(410)
    changed, val = imgui.checkbox("详情##audit", cfg.audit.show_details)
    if changed then cfg.audit.show_details = val end

    imgui.same_line(500)
    imgui.set_next_item_width(70)
    changed, val = imgui.input_float("采样间隔##audit", cfg.audit.sample_interval)
    if changed then cfg.audit.sample_interval = math.max(0.5, val) end

    imgui.text(string.format("时长 %s  |  击杀估算 %d (%.1f/h)  |  采集/入包 %d (%.1f/h)  |  经验 %d (%.0f/h)  |  金币 %+d (%+.0f/h)",
        format_duration(a.elapsed_seconds),
        a.kills_est,
        audit_rate(a.kills_est),
        a.gather_est,
        audit_rate(a.gather_est),
        a.exp_gain,
        audit_rate(a.exp_gain),
        a.kinah_gain,
        audit_rate(a.kinah_gain)))

    imgui.text(string.format("当前 %s Lv.%d %s/%s  HP %d/%d  MP %d/%d  地图 %s  实体 %d  背包 %d  任务 %d  目标 %s",
        tostring(a.current.name or ""),
        a.current.level or 0,
        tostring(a.current.race_name or ""),
        tostring(a.current.job_name or ""),
        a.current.hp or 0,
        a.current.max_hp or 0,
        a.current.mp or 0,
        a.current.max_mp or 0,
        tostring(a.current.map or ""),
        a.current.entities or 0,
        a.current.inventory or 0,
        a.current.quests or 0,
        tostring(a.current.target_id or 0)))

    if cfg.audit.show_details then
        imgui.separator()
        imgui.text("审计口径")
        imgui.text("击杀估算: 新出现 lootable 实体，已见过的尸体不重复计数。")
        imgui.text("采集估算: 背包内匹配关键字的物品数量正向增量。")
        imgui.text("路径点: 打怪 " .. tostring(count_lines(cfg.route.route_points)) ..
            " / 复活 " .. tostring(count_lines(cfg.route.revive_points)) ..
            " / 补给 " .. tostring(count_lines(cfg.route.vendor_points)) ..
            " / 采集 " .. tostring(count_lines(cfg.route.gather_points)) ..
            " / 主线 " .. tostring(count_lines(cfg.route.leveling_points)))

        imgui.text("材料关键字")
        imgui.set_next_item_width(520)
        changed, val = imgui.input_text_multiline("##audit_material_keywords", cfg.audit.material_keywords, 520, 80)
        if changed then cfg.audit.material_keywords = val end

        if a.last_error ~= "" then
            imgui.text("最近审计错误: " .. tostring(a.last_error))
        end
    end
end

local function draw_main_window()
    imgui.set_next_window_size(800, 760, imgui.Cond_FirstUseEver)
    imgui.set_next_window_pos(120, 80, imgui.Cond_FirstUseEver)

    local title = "Aion 控制台#aion_control_main_window"

    local visible, open = imgui.begin_window(title, true, imgui.WindowFlags_NoCollapse)
    if open == false then
        runtime.ui_visible = false
        set_event("隐藏窗口")
    end

    if visible then
        draw_header()
        draw_accounts_tab()
    end

    imgui.end_window()
end

local function on_render()
    runtime.frame = runtime.frame + 1
    if runtime.ui_visible then
        if runtime.accounts.settings_window_visible then
            draw_account_settings_window()
        else
            draw_main_window()
        end
        draw_account_add_window()
        draw_keycode_reference_window()
    end
end

local function background_refresh_tick()
    account_process_pending_script()
    account_process_pending_login()
    account_agreement_click_tick()

    if runtime.ui_visible then
        account_poll(false)
    elseif not runtime.bootstrap.pending then
        target_refresh(false)
        audit_sample()
        account_poll(false)
    end
end

log_info("Aion 控制台UI 启动")
load_config()
target_refresh(true)

imgui.on_render(on_render)

if not imgui.is_initialized() then
    if not imgui.init() then
        log_warn("ImGui 初始化失败")
        return
    end
    apply_white_blue_style()
    install_button_feedback()
    imgui.run()
else
    apply_white_blue_style()
    install_button_feedback()
end

hotkey.start(10)

last_f7 = false
last_f8 = false
last_f9 = false
last_f10 = false
last_f2 = false
last_f3 = false
last_f4 = false
last_f5 = false
last_f6 = false
last_f11 = false

while true do
    ctrl = hotkey.is_pressed(0x11)
    if ctrl and hotkey.is_pressed(0x7B) then
        log_info("Aion 控制台UI 退出")
        break
    end

    if hotkey.is_pressed(0x70) and not runtime.hotkey_f1 then
        npc_f1_action()
    end
    runtime.hotkey_f1 = hotkey.is_pressed(0x70)

    f2 = hotkey.is_pressed(0x71)
    if f2 and not last_f2 then
        ui_test_f2_dump()
    end
    last_f2 = f2

    f3 = hotkey.is_pressed(0x72)
    if f3 and not last_f3 then
        ui_test_f3_dump()
    end
    last_f3 = f3

    f4 = hotkey.is_pressed(0x73)
    if f4 and not last_f4 then
        run_server_probe()
    end
    last_f4 = f4

    f5 = hotkey.is_pressed(0x74)
    if f5 and not last_f5 then
        loot_test_dump_near_corpses()
    end
    last_f5 = f5

    f6 = hotkey.is_pressed(0x75)
    if f6 and not last_f6 then
        target_f6_dump_selected()
    end
    last_f6 = f6

    f7 = hotkey.is_pressed(0x76)
    if f7 and not last_f7 then
        toggle_ui_visible()
    end
    last_f7 = f7

    f8 = hotkey.is_pressed(0x77)
    if f8 and not last_f8 then
        npc_dump_current_dialog()
    end
    last_f8 = f8

    f9 = hotkey.is_pressed(0x78)
    if f9 and not last_f9 then
        run_probe()
    end
    last_f9 = f9

    f10 = hotkey.is_pressed(0x79)
    if f10 and not last_f10 then
        toggle_pause()
    end
    last_f10 = f10

    f11 = hotkey.is_pressed(0x7A)
    if f11 and not last_f11 then
        task_f11_record_snapshot()
    end
    last_f11 = f11

    bootstrap_tick()
    if route_recovery_tick then
        route_recovery_tick()
    end
    main_quest_20590_tick()
    main_quest_20610_tick()
    main_quest_20611_tick()
    combat_tick_quest_grind()
    combat_tick()
    route_tick()
    background_refresh_tick()
    sleep(50)
end

hotkey.stop()
